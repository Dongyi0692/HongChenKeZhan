using UnityEngine;

namespace FengChenInn
{
    // Distant sounds heard after Hongfu has crossed the rear threshold.
    // The live combat emitters still handle every visible swing, hit and block.
    [DisallowMultipleComponent]
    public sealed class RescueCoverAudio : MonoBehaviour
    {
        const float BattleDelay = 1.35f;
        static readonly float[] StepTimes = { 0f, .43f, .88f };

        DuelEncounter encounter;
        AudioSource stepsSource, battleSource;
        AudioClip[] woodSteps;
        AudioClip battleClip;
        bool active, walking, paused, fightBegan;
        float elapsed;
        int nextStep, lastStep = -1;

        public void Initialize(DuelEncounter duel)
        {
            encounter = duel;
            woodSteps = Resources.LoadAll<AudioClip>("Footsteps/WoodFloor");
            battleClip = Resources.Load<AudioClip>("RescueCoverAudio/SwordBattle2_Loop");
            stepsSource = MakeSource("Distant hall footsteps", false, 17f, 2300f);
            battleSource = MakeSource("Distant hall sword fight", true, 22f, 1450f);
            battleSource.clip = battleClip;
            if (woodSteps.Length == 0 || !battleClip)
                Debug.LogWarning("Rescue cover audio is missing its wood steps or sword fight clip.", this);
        }

        AudioSource MakeSource(string label, bool loop, float distance, float cutoff)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = distance;
            source.priority = 145;
            var lowPass = child.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = cutoff;
            return source;
        }

        public void BeginCover()
        {
            StopCover();
            active = true;
            if (CombatAudioSystem.Instance != null)
                CombatAudioSystem.Instance.SoundPlayed += OnCombatSound;
        }

        public void StopCover()
        {
            if (CombatAudioSystem.Instance != null)
                CombatAudioSystem.Instance.SoundPlayed -= OnCombatSound;
            active = walking = paused = false;
            fightBegan = false;
            elapsed = 0f;
            nextStep = 0;
            if (stepsSource) stepsSource.Stop();
            if (battleSource) { battleSource.Stop(); battleSource.volume = 0f; }
        }

        void Update()
        {
            if (!active) return;
            if (!encounter || !encounter.RescueCoverActive) { StopCover(); return; }
            if (encounter.EliteActor)
            {
                var position = encounter.EliteActor.transform.position + Vector3.up;
                stepsSource.transform.position = position;
                battleSource.transform.position = position;
            }
            if (Time.timeScale <= 0f)
            {
                if (!paused) { stepsSource.Pause(); battleSource.Pause(); paused = true; }
                return;
            }
            if (paused) { stepsSource.UnPause(); battleSource.UnPause(); paused = false; }

            // Wait for a real pursuer to finish entering the hall before the cue begins.
            if (!walking)
            {
                if (encounter.RescueEnteredCount == 0) return;
                walking = true;
            }
            elapsed += Time.deltaTime;
            while (nextStep < StepTimes.Length && elapsed >= StepTimes[nextStep])
            {
                PlayWoodStep();
                nextStep++;
            }
            if (elapsed < BattleDelay || !fightBegan || !battleClip) return;
            if (!battleSource.isPlaying) { battleSource.volume = 0f; battleSource.Play(); }
            // The recording stays behind the live sword effects and outdoor rain.
            float target = InnRainZone.Outdoor > .5f ? .19f : 0f;
            battleSource.volume = Mathf.MoveTowards(battleSource.volume, target, Time.deltaTime * .18f);
        }

        void PlayWoodStep()
        {
            if (woodSteps == null || woodSteps.Length == 0) return;
            int index = Random.Range(0, woodSteps.Length);
            if (woodSteps.Length > 1 && index == lastStep) index = (index + 1) % woodSteps.Length;
            lastStep = index;
            stepsSource.pitch = Random.Range(.97f, 1.03f);
            stepsSource.PlayOneShot(woodSteps[index], .28f);
        }

        void OnCombatSound(string kind, AudioClip clip)
        {
            if (walking && (kind == "Swing" || kind == "Hit" || kind == "Block"))
                fightBegan = true;
        }

        void OnDisable() => StopCover();
    }
}
