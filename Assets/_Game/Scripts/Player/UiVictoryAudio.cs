using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FengChenInn
{
    // Kept on the interface root so hiding a panel never cuts off its sound.
    public sealed class UiVictoryAudio : MonoBehaviour
    {
        public static UiVictoryAudio Instance { get; private set; }
        const string Key = "FengChenInn.UI.";
        AudioSource ui, victory;
        AudioClip hover, confirm, page, ledgerPage, victoryClip;
        float hoverAt = -10, target, envelope;
        bool started;
        public int HoverCount { get; private set; }
        public int ConfirmCount { get; private set; }
        public int PageCount { get; private set; }
        public int LedgerPageCount { get; private set; }
        public int VictoryCount { get; private set; }
        public float HoverVolume { get; private set; }
        public float ConfirmVolume { get; private set; }
        public float PageVolume { get; private set; }
        public float VictoryVolume { get; private set; }
        public bool VictoryPlaying => victory && victory.isPlaying;
        // Exposed for the Play Mode audit; nothing else drives these directly.
        public AudioClip VictoryClip => victoryClip;
        public AudioSource VictorySource => victory;
        public bool VictoryStarted => started;
        public AudioClip LedgerClip => ledgerPage;

        void Awake()
        {
            Instance = this;
            HoverVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key + "hoverVolume", .18f));
            ConfirmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key + "confirmVolume", .36f));
            PageVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key + "pageVolume", .5f));
            VictoryVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key + "victoryVolume", .52f));
            // Recorded brush-past sound replaces the earlier synthetic hover blip; the previous
            // clip stays in the project so the tone can still be compared or restored.
            hover = Resources.Load<AudioClip>("UiVictory/UI_Hover_Brush")
                ?? Resources.Load<AudioClip>("UiVictory/UI_Hover_Soft");
            confirm = Resources.Load<AudioClip>("UiVictory/UI_Confirm_Wood");
            page = Resources.Load<AudioClip>("UiVictory/UI_Story_PageTurn");
            // A real page turn for the ledger spread; the story dialogue keeps its own cue.
            ledgerPage = Resources.Load<AudioClip>("UiVictory/UI_Ledger_PageTurn") ?? page;
            victoryClip = Resources.Load<AudioClip>("UiVictory/Victory_B_Softer_100-116s");
            ui = MakeSource("UI one shots");
            victory = MakeSource("Victory B once");
            victory.clip = victoryClip;
            if (!victoryClip) Debug.LogError("Victory B music is missing", this);
        }
        AudioSource MakeSource(string label)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.ignoreListenerPause = true;
            return source;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
        public void SetHoverVolume(float value) { HoverVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(Key + "hoverVolume", HoverVolume); }
        public void SetConfirmVolume(float value) { ConfirmVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(Key + "confirmVolume", ConfirmVolume); }
        public void SetPageVolume(float value) { PageVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(Key + "pageVolume", PageVolume); }
        public void SetVictoryVolume(float value) { VictoryVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(Key + "victoryVolume", VictoryVolume); }
        public void Hover()
        {
            if (!hover || Time.unscaledTime - hoverAt < .11f) return;
            hoverAt = Time.unscaledTime;
            ui.PlayOneShot(hover, HoverVolume);
            HoverCount++;
        }
        public void Confirm()
        {
            if (!confirm) return;
            ui.PlayOneShot(confirm, ConfirmVolume);
            ConfirmCount++;
        }
        public void Page()
        {
            if (!page) return;
            ui.PlayOneShot(page, PageVolume);
            PageCount++;
        }
        // Opening or closing the ledger spread: one paper turn per action, never on hover.
        public void LedgerPage()
        {
            if (!ledgerPage) return;
            ui.PlayOneShot(ledgerPage, Mathf.Clamp01(PageVolume * 1.15f));
            LedgerPageCount++;
        }
        public void StartVictory()
        {
            if (!victoryClip) return;
            // One cue per completion. Re-entering the result screen or re-arming the
            // fade must never restart the music; only ResetVictory (new run) clears it.
            if (started) { target = 1; return; }
            started = true;
            target = 1;
            envelope = 0;
            victory.volume = 0;
            victory.Play();
            VictoryCount++;
        }
        public void LeaveVictory() { target = 0; }
        public void ResetVictory()
        {
            target = envelope = 0;
            started = false;
            if (victory) victory.Stop();
        }
        void Update()
        {
            if (!victory) return;
            envelope = Mathf.MoveTowards(envelope, target, Time.unscaledDeltaTime / (target > envelope ? 1.3f : 1.8f));
            victory.volume = envelope * VictoryVolume;
            if (target == 0 && envelope == 0 && victory.isPlaying) victory.Stop();
        }
    }

    public sealed class UiButtonSound : MonoBehaviour, IPointerEnterHandler
    {
        Button button;
        // Dialogue boxes read best without a hover cue: the pointer simply travelling
        // across the line of text should not chirp.
        public bool hoverEnabled = true;
        void Awake() { button = GetComponent<Button>(); }
        public void OnPointerEnter(PointerEventData _)
        {
            if (hoverEnabled && button && button.IsInteractable()) UiVictoryAudio.Instance?.Hover();
        }
        public static void Bind(Button button, bool hover = true)
        {
            var trigger = button.GetComponent<UiButtonSound>();
            if (!trigger) trigger = button.gameObject.AddComponent<UiButtonSound>();
            trigger.hoverEnabled = hover;
            button.onClick.AddListener(() => UiVictoryAudio.Instance?.Confirm());
        }
    }
}
