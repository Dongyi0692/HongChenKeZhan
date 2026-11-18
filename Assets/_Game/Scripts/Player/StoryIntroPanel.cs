using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FengChenInn
{
    /// <summary>Layered prologue artwork. Completion delegates to the existing game entry.</summary>
    public sealed class StoryIntroPanel : MonoBehaviour
    {
        [Serializable] public sealed class Layout { public float[] canvas; public Item[] sprites; }
        [Serializable] public sealed class Item
        {
            public string file;
            public float[] rect_top_left_px;
            public float[] anchoredPosition_from_center_px;
        }

        private CanvasGroup panel, heading, signature;
        private readonly List<CanvasGroup> paragraphs = new();
        private readonly List<RectTransform> rain = new();
        private readonly List<Image> glows = new();
        private Texture2D glowTexture;
        private Sprite glowSprite;
        private Action onComplete;
        private bool built, callbackSent;
        private float inputAfter;
        private int lastInputFrame = -1;
        public bool FullyRevealed { get; private set; }
        public bool IsLeaving { get; private set; }
        public Button ContinueButton { get; private set; }
        public Button SkipButton { get; private set; }
        public string SourceText { get; private set; }

        public void Build(Action complete)
        {
            if (built) throw new InvalidOperationException("Prologue already built.");
            onComplete = complete ?? throw new ArgumentNullException(nameof(complete));
            var layoutAsset = Resources.Load<TextAsset>("StoryIntro/布局清单");
            var source = Resources.Load<TextAsset>("StoryIntro/序章原文");
            if (!layoutAsset || !source) throw new InvalidOperationException("Missing prologue layout or original text.");
            SourceText = source.text;
            var layout = JsonUtility.FromJson<Layout>(layoutAsset.text);
            if (layout.canvas.Length != 2 || layout.canvas[0] != 1920 || layout.canvas[1] != 1080)
                throw new InvalidOperationException("Prologue requires the supplied 1920x1080 layout.");

            panel = gameObject.AddComponent<CanvasGroup>();
            var background = ImageAt(transform, "Background", "00_雨夜客栈_无UI背景");
            Stretch(background.rectTransform);
            MakeWeather(); // Behind artwork, only on the right side of the image.
            heading = Group("Heading and Title");
            var story = RectAt(transform, "Story");
            Stretch(story);
            signature = Group("Signature");
            var controls = RectAt(transform, "Controls");
            Stretch(controls);

            foreach (var item in layout.sprites)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(item.file);
                int prefix = int.Parse(name.Substring(0, 2));
                Transform parent;
                if (prefix <= 3) parent = heading.transform;
                else if (prefix <= 10)
                {
                    var group = Group("Paragraph " + (paragraphs.Count + 1), story);
                    paragraphs.Add(group);
                    parent = group.transform;
                }
                else parent = prefix == 11 ? signature.transform : controls;
                var image = ImageAt(parent, name, name);
                image.rectTransform.sizeDelta = new Vector2(item.rect_top_left_px[2], item.rect_top_left_px[3]);
                image.rectTransform.anchoredPosition = new Vector2(item.anchoredPosition_from_center_px[0], item.anchoredPosition_from_center_px[1]);
                if (prefix == 13) ContinueButton = MakeButton(image, Continue);
                if (prefix == 14) SkipButton = MakeButton(image, Skip);
            }
            if (paragraphs.Count != 6 || !ContinueButton || !SkipButton)
                throw new InvalidOperationException("Prologue must contain six paragraphs and both buttons.");
            built = true;
            if (isActiveAndEnabled) Begin();
        }

        private void OnEnable() { if (built) Begin(); }
        private void OnDisable() { StopAllCoroutines(); }
        private void Begin()
        {
            StopAllCoroutines();
            FullyRevealed = IsLeaving = callbackSent = false;
            inputAfter = Time.unscaledTime + .12f;
            lastInputFrame = -1;
            panel.alpha = 0;
            panel.interactable = panel.blocksRaycasts = true;
            heading.alpha = signature.alpha = 0;
            foreach (var paragraph in paragraphs) paragraph.alpha = 0;
            StartCoroutine(Reveal());
        }

        private IEnumerator Reveal()
        {
            yield return Fade(panel, 0, 1, .6f);
            yield return Fade(heading, 0, 1, .5f);
            for (int index = 0; index < paragraphs.Count; index++)
            {
                if (index > 0) UiVictoryAudio.Instance?.Page();
                yield return Fade(paragraphs[index], 0, 1, .45f);
                yield return new WaitForSecondsRealtime(.2f);
            }
            yield return Fade(signature, 0, 1, .4f);
            FullyRevealed = true;
        }

        public void Continue()
        {
            if (!AcceptInput()) return;
            if (!FullyRevealed)
            {
                StopAllCoroutines();
                panel.alpha = heading.alpha = signature.alpha = 1;
                foreach (var paragraph in paragraphs) paragraph.alpha = 1;
                FullyRevealed = true;
                // A double click that reveals text must not accidentally dismiss it.
                inputAfter = Time.unscaledTime + .25f;
                return;
            }
            Leave();
        }

        public void Skip() { if (AcceptInput()) Leave(); }
        private bool AcceptInput()
        {
            if (!built || !isActiveAndEnabled || IsLeaving || callbackSent || Time.unscaledTime < inputAfter || lastInputFrame == Time.frameCount)
                return false;
            lastInputFrame = Time.frameCount;
            return true;
        }
        private void Leave()
        {
            IsLeaving = true;
            panel.interactable = false;
            StopAllCoroutines();
            StartCoroutine(Exit());
        }
        private IEnumerator Exit()
        {
            yield return Fade(panel, panel.alpha, 0, .35f);
            if (callbackSent) yield break;
            callbackSent = true;
            onComplete();
        }
        private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, time / duration));
                yield return null;
            }
            group.alpha = to;
        }

        private void Update()
        {
            if (!built) return;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Continue();
            for (int i = 0; i < rain.Count; i++)
            {
                Vector2 p = rain[i].anchoredPosition;
                p += new Vector2(-22, -110 - i % 5 * 15) * Time.unscaledDeltaTime;
                p.x = 1030 + Mathf.Repeat(p.x - 1030, 860);
                p.y = Mathf.Repeat(p.y + 1100, 1140) - 1100;
                rain[i].anchoredPosition = p;
            }
            for (int i = 0; i < glows.Count; i++)
                glows[i].color = new Color(1, 1, 1, .06f + .012f * Mathf.Sin(Time.unscaledTime * 1.05f + i));
        }

        private static RectTransform RectAt(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            return rect;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private CanvasGroup Group(string name, Transform parent = null)
        {
            var rect = RectAt(parent ? parent : transform, name);
            Stretch(rect);
            return rect.gameObject.AddComponent<CanvasGroup>();
        }
        private static Image ImageAt(Transform parent, string name, string resource)
        {
            var rect = RectAt(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = Resources.Load<Sprite>("StoryIntro/PNG/" + resource);
            if (!image.sprite) throw new InvalidOperationException("Missing prologue sprite: " + resource);
            return image;
        }
        private static Button MakeButton(Image image, UnityEngine.Events.UnityAction action)
        {
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.08f, 1.02f, 1);
            colors.pressedColor = new Color(.8f, .72f, .6f, 1);
            colors.fadeDuration = .1f;
            button.colors = colors;
            // The prologue is a full-screen reading view, not a menu: the pointer
            // should not chirp while it happens to sit over 继续 or 跳过.
            UiButtonSound.Bind(button, false);
            button.onClick.AddListener(action);
            return button;
        }
        private void MakeWeather()
        {
            var weather = RectAt(transform, "Subtle right-side rain and lanterns");
            Stretch(weather);
            var random = new System.Random(31);
            for (int i = 0; i < 28; i++)
            {
                var rect = RectAt(weather, "Rain " + i);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(1030 + (float)random.NextDouble() * 860, -(float)random.NextDouble() * 1080);
                rect.sizeDelta = new Vector2(.65f, 13 + i % 9);
                rect.localEulerAngles = new Vector3(0, 0, -11);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = new Color(.65f, .77f, .86f, .045f);
                rain.Add(rect);
            }
            glowTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            glowTexture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float a = Mathf.Pow(Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32), 2);
                glowTexture.SetPixel(x, y, new Color(1, .6f, .2f, a));
            }
            glowTexture.Apply();
            glowSprite = Sprite.Create(glowTexture, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
            foreach (var point in new[] { new Vector2(1464, -405), new Vector2(1217, -575) })
            {
                var rect = RectAt(weather, "Lantern glow");
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.anchoredPosition = point; rect.sizeDelta = new Vector2(170, 210);
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = glowSprite; image.raycastTarget = false;
                image.color = new Color(1, 1, 1, .06f);
                glows.Add(image);
            }
        }
        private void OnDestroy()
        {
            if (glowSprite) Destroy(glowSprite);
            if (glowTexture) Destroy(glowTexture);
        }
    }
}
