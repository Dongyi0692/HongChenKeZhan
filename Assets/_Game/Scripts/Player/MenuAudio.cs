using UnityEngine;

namespace FengChenInn
{
    // Lives on the UI root: hiding the title must not cut off its release or rain.
    public sealed class MenuAudio : MonoBehaviour
    {
        const string VolumeKey = "FengChenInn.UI.musicVolume";
        const string RainVolumeKey = "FengChenInn.UI.rainVolume";
        const double Overlap = 2.5;
        readonly AudioSource[] music = new AudioSource[2];
        readonly double[] starts = new double[2];
        AudioSource rain;
        AudioSource openRain;
        AudioClip track;
        double lastStart, fadeStart;
        float envelope, fadeFrom, target, fadeSeconds;
        bool running, menu;
        float rainDoorGain = 1;
        float rainFocus = 1;
        float rainFocusTarget = 1;
        float outdoor;
        public float MusicVolume { get; private set; }
        public float RainVolume { get; private set; }
        // 0 = roof rain only, 1 = outdoor rain only. Crossfaded, never cut.
        public float OutdoorMix => outdoor;
        public AudioSource RoofRain => rain;
        public AudioSource OutdoorRain => openRain;

        // Indoor/outdoor rain blend. Two seconds from full indoor to full outdoor,
        // matched to the shelter debounce in InnRainZone.
        [Min(.1f)] public float rainTransition = 2f;

        void Awake()
        {
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, .65f));
            RainVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(RainVolumeKey, .12f));
            track = Resources.Load<AudioClip>("MenuAudio/MainMenuTheme");
            for (int i = 0; i < music.Length; i++) music[i] = Source("Menu music " + i, track, false);
            rain = Source("Rain night ambience", Resources.Load<AudioClip>("MenuAudio/RainNight"), true);
            // Always running, like the roof layer, so entering or leaving the inn never
            // restarts a loop or drops its playback position.
            openRain = Source("Rain outdoor ambience", Resources.Load<AudioClip>("MenuAudio/OutdoorRainNight"), true);
            if (openRain.clip) openRain.Play();
            if (!track || !rain.clip) Debug.LogError("Missing main menu audio resources.");
            if (!openRain.clip) Debug.LogError("Missing outdoor rain loop.");
        }

        AudioSource Source(string label, AudioClip clip, bool loop)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.loop = loop;
            source.volume = 0;
            source.clip = clip;
            return source;
        }

        public void SetMenu(bool active)
        {
            if (active == menu) return;
            menu = active;
            if (active)
            {
                if (rain.clip && !rain.isPlaying) rain.Play();
                if (track && !running)
                {
                    running = true;
                    Schedule(0, AudioSettings.dspTime + .1);
                    Schedule(1, starts[0] + track.length - Overlap);
                }
            }
            fadeFrom = envelope;
            target = active ? 1 : 0;
            fadeSeconds = active ? 1.8f : 2f;
            fadeStart = AudioSettings.dspTime;
        }

        public void SetMusicVolume(float value)
        {
            MusicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, MusicVolume);
        }

        public void SetRainVolume(float value)
        {
            RainVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(RainVolumeKey, RainVolume);
        }

        public void SetVictoryRain(bool active) => rainFocusTarget = active ? 1.7f : 1f;

        void Schedule(int index, double when)
        {
            starts[index] = lastStart = when;
            music[index].volume = 0;
            music[index].PlayScheduled(when);
        }

        void Update()
        {
            double now = AudioSettings.dspTime;
            float progress = fadeSeconds > 0 ? Mathf.Clamp01((float)(now - fadeStart) / fadeSeconds) : 1;
            envelope = Mathf.Lerp(fadeFrom, target, Mathf.SmoothStep(0, 1, progress));
            float targetDoorGain = menu ? 1 : Mathf.Lerp(1, 1.55f, InnDoorAudio.RainExposure);
            float response = targetDoorGain > rainDoorGain ? .65f : 1.4f;
            rainDoorGain = Mathf.Lerp(rainDoorGain, targetDoorGain, 1 - Mathf.Exp(-Time.unscaledDeltaTime / response));
            rainFocus = Mathf.MoveTowards(rainFocus, rainFocusTarget, Time.unscaledDeltaTime * .5f);
            // Title, settings and prologue keep the original roof-only bed. Once the
            // player is in the inn, the two loops share one equal-power crossfade:
            // cos/sin keeps roof² + outdoor² constant, so the bed never gets louder
            // just because both loops are audible mid-transition.
            float targetOutdoor = menu || !InnRainZone.Active ? 0f : InnRainZone.Outdoor;
            outdoor = Mathf.MoveTowards(outdoor, targetOutdoor, Time.unscaledDeltaTime / Mathf.Max(.1f, rainTransition));
            float bed = RainVolume * rainDoorGain * rainFocus;
            float roofGain = Mathf.Cos(outdoor * Mathf.PI * .5f);
            float openGain = Mathf.Sin(outdoor * Mathf.PI * .5f);
            if (rain) rain.volume = Mathf.MoveTowards(rain.volume, Mathf.Clamp01(bed * roofGain), Time.unscaledDeltaTime * .5f);
            if (openRain) openRain.volume = Mathf.MoveTowards(openRain.volume, Mathf.Clamp01(bed * openGain), Time.unscaledDeltaTime * .5f);
            if (!running) return;
            if (!menu && progress >= 1)
            {
                foreach (var source in music) { source.Stop(); source.volume = 0; }
                running = false;
                return;
            }
            for (int i = 0; i < music.Length; i++)
            {
                if (menu && now >= starts[i] + track.length)
                    Schedule(i, System.Math.Max(lastStart + track.length - Overlap, now + .1));
                double age = now - starts[i];
                // The source already fades almost to silence at 59 s. Complete the incoming
                // rise by 59 s (1.5 s into the overlap), rather than waiting for silent EOF.
                float head = Mathf.Sin(Mathf.Clamp01((float)(age / 1.5)) * Mathf.PI * .5f);
                float tail = Mathf.Cos(Mathf.Clamp01((float)((age - (track.length - Overlap)) / Overlap)) * Mathf.PI * .5f);
                music[i].volume = envelope * MusicVolume * head * tail;
            }
        }
    }
}
