using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FengChenInn
{
    // Every clip targets this visual's exported skeleton. CharacterController owns
    // world movement; this graph only supplies the animated pose, never old IK.
    [DefaultExecutionOrder(50)]
    public sealed class AholoAnimationDriver : MonoBehaviour
    {
        public AholoMotionSet motionSet;
        public Animator targetAnimator;
        public bool Ready => graph.IsValid() && targetAnimator != null && motionSet != null;
        public CombatMotion CurrentMotion { get; private set; } = CombatMotion.Idle;
        public float NormalizedTime
        {
            get
            {
                if (!Ready || currentSlot < 0 || !clips[currentSlot].IsValid()) return 0f;
                float duration = rangeEnd > rangeStart ? rangeEnd - rangeStart : MotionLength(CurrentMotion);
                float time = ((float)clips[currentSlot].GetTime() - rangeStart) / Mathf.Max(.001f, duration);
                return IsLoop(CurrentMotion) ? Mathf.Repeat(time, 1f) : Mathf.Clamp01(time);
            }
        }

        private const int SlotCount = 4;
        private const float StartStopFade = .24f;
        private const float MinimumStartStopFrames = 6f;
        private const float LocomotionStartSpeed = .16f;
        private const float LocomotionStopSpeed = .08f;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[SlotCount];
        private readonly float[] fromWeights = new float[SlotCount];
        private readonly HashSet<CombatMotion> missingReported = new HashSet<CombatMotion>();
        private int currentSlot = -1;
        private float fadeClock, fadeDuration;
        private float rangeStart, rangeEnd;
        private bool bindingReported, guardReleasing, blendingStartStop;
        private bool dialoguePoseLocked;
        private float impactPauseUntil;
        public bool IsDialoguePoseActive => dialoguePoseLocked;
        public bool IsImpactPaused => Time.time < impactPauseUntil;
        public bool IsGuardReleasing => Ready && CurrentMotion == CombatMotion.Guard && guardReleasing && NormalizedTime < .999f;

        public void PauseImpact(float duration)
        {
            impactPauseUntil = Mathf.Max(impactPauseUntil, Time.time + Mathf.Clamp(duration, 0f, .1f));
            if (mixer.IsValid()) mixer.SetSpeed(0d);
        }

        public void ClearImpactPause()
        {
            impactPauseUntil = 0f;
            if (mixer.IsValid()) mixer.SetSpeed(dialoguePoseLocked ? 0d : 1d);
        }

        private void Update()
        {
            if (mixer.IsValid()) mixer.SetSpeed(dialoguePoseLocked || IsImpactPaused ? 0d : 1d);
        }

        private void Awake() => BuildGraph();
        private void OnEnable() { if (!graph.IsValid()) BuildGraph(); }
        private void OnDisable() => ReleaseGraph();
        private void OnDestroy() => ReleaseGraph();

        public void Configure(AholoMotionSet motions, Animator animator = null)
        {
            ReleaseGraph();
            motionSet = motions;
            targetAnimator = animator != null ? animator : GetComponentInChildren<Animator>(true);
            missingReported.Clear();
            bindingReported = false;
            BuildGraph();
        }

        private void BuildGraph()
        {
            if (graph.IsValid()) return;
            if (targetAnimator == null) targetAnimator = GetComponentInChildren<Animator>(true);
            if (targetAnimator == null || motionSet == null) return;
            targetAnimator.enabled = true;
            targetAnimator.applyRootMotion = false;
            targetAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create(name + " Aholo Animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, SlotCount);
            AnimationPlayableOutput.Create(graph, "Aholo pose", targetAnimator).SetSourcePlayable(mixer);
            graph.Play();
            Play(CombatMotion.ExplorationIdle, 0f);
        }

        public bool HasMotion(CombatMotion motion) => motionSet != null && motionSet.GetClip(motion) != null;

        public float MotionLength(CombatMotion motion, float fallback = 1f)
        {
            AnimationClip clip = motionSet != null ? motionSet.GetClip(motion) : null;
            return clip != null ? Mathf.Max(.001f, clip.length) : fallback;
        }

        public bool TryGetMotionTime(CombatMotion motion, out float clipSeconds)
        {
            clipSeconds = 0f;
            if (!Ready || CurrentMotion != motion || currentSlot < 0 || !clips[currentSlot].IsValid()) return false;
            clipSeconds = Mathf.Clamp((float)clips[currentSlot].GetTime(), 0f, MotionLength(motion));
            return true;
        }

        // Only this actor's graph is evaluated. Zero delta neither advances
        // global game time nor another actor; caller restores the final pose.
        public bool SampleMotionPose(CombatMotion motion, float clipSeconds)
        {
            if (dialoguePoseLocked || !Ready || CurrentMotion != motion || currentSlot < 0 || !clips[currentSlot].IsValid()) return false;
            clips[currentSlot].SetTime(Mathf.Clamp(clipSeconds, 0f, MotionLength(motion)));
            graph.Evaluate(0f);
            return true;
        }

        public void Play(CombatMotion motion, float fade = .12f, float speed = 1f)
            => PlayInternal(motion, fade, speed, false);

        // Attack and damage events must start a fresh clip even when a previous
        // instance of that same motion was interrupted before its final frame.
        public void Restart(CombatMotion motion, float fade = .12f, float speed = 1f)
            => PlayInternal(motion, fade, speed, true);

        public void RestartRange(CombatMotion motion, float start, float end, float fade, float speed)
        {
            PlayInternal(motion, fade, speed, true);
            if (!Ready || currentSlot < 0 || !clips[currentSlot].IsValid()) return;
            rangeStart = Mathf.Clamp(start, 0f, MotionLength(motion));
            rangeEnd = Mathf.Clamp(end, rangeStart + .001f, MotionLength(motion));
            clips[currentSlot].SetTime(rangeStart);
        }

        public bool ReleaseGuard(float speed = 1f)
        {
            if (!Ready || currentSlot < 0 || CurrentMotion != CombatMotion.Guard
                || !HasMotion(CombatMotion.Guard) || !clips[currentSlot].IsValid() || NormalizedTime >= .999f)
                return false;
            guardReleasing = true;
            clips[currentSlot].SetSpeed(Mathf.Max(.01f, speed));
            return true;
        }

        public void SettleForDialogue(bool battle = false)
        {
            if (!Ready) return;
            CombatMotion rest = battle ? CombatMotion.Idle : CombatMotion.ExplorationIdle;
            if (!HasMotion(rest)) rest = CombatMotion.Idle;
            if (!HasMotion(rest)) return;
            ClearImpactPause();
            Restart(rest, 0f);
            graph.Evaluate(0f);
        }

        // Own this graph for the whole dialogue, including its opening frame.
        // Time.deltaTime can still be positive in the frame that pauses time;
        // an explicit lock prevents later callers from replacing the sampled idle.
        // Dialogue uses the existing relaxed standing idle; the sword remains
        // attached to the hand. Sword-ready Idle is intentionally reserved for
        // combat, since its forward lean and spread arms read poorly in dialogue.
        public bool SetDialoguePose(bool active)
        {
            if (!active)
            {
                dialoguePoseLocked = false;
                if (mixer.IsValid()) mixer.SetSpeed(IsImpactPaused ? 0d : 1d);
                return true;
            }
            if (dialoguePoseLocked) return true;
            CombatMotion standing = HasMotion(CombatMotion.ExplorationIdle) ? CombatMotion.ExplorationIdle : CombatMotion.Idle;
            if (!Ready || !HasMotion(standing)) return false;
            ClearImpactPause();
            Restart(standing, 0f);
            dialoguePoseLocked = true;
            mixer.SetSpeed(0d);
            graph.Evaluate(0f);
            return true;
        }

        private void PlayInternal(CombatMotion motion, float fade, float speed, bool restart)
        {
            if (dialoguePoseLocked) return;
            if (!Ready)
            {
                if (!bindingReported)
                {
                    Debug.LogWarning(name + ": Aholo animator or motion set is not bound; animation is unavailable.", this);
                    bindingReported = true;
                }
                return;
            }
            AnimationClip clip = motionSet.GetClip(motion);
            if (clip == null)
            {
                if (missingReported.Add(motion))
                    Debug.LogWarning(name + ": Missing Aholo clip " + motion + "; no procedural replacement is used.", this);
                return;
            }
            bool reenterGuard = motion == CombatMotion.Guard && guardReleasing;
            if (!restart && !reenterGuard && currentSlot >= 0 && CurrentMotion == motion && clips[currentSlot].IsValid()
                && (IsLoop(motion) || motion == CombatMotion.Guard || NormalizedTime < .999f))
            {
                if (motion != CombatMotion.Guard || NormalizedTime < motionSet.guardHoldNormalized)
                    clips[currentSlot].SetSpeed(Mathf.Max(.01f, speed));
                return;
            }

            // A bounded landing/attack can still carry weight while fading
            // out. Hold its actual exit pose before its range is replaced;
            // otherwise that old port keeps playing past the selected range
            // and can reintroduce the deep landing crouch during an idle fade.
            if (rangeEnd > rangeStart && currentSlot >= 0 && clips[currentSlot].IsValid())
            {
                clips[currentSlot].SetTime(Mathf.Clamp((float)clips[currentSlot].GetTime(), rangeStart, rangeEnd));
                clips[currentSlot].SetSpeed(0d);
            }

            int next = -1;
            float lowestWeight = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                fromWeights[i] = mixer.GetInputWeight(i);
                if (i != currentSlot && (!clips[i].IsValid() || fromWeights[i] < lowestWeight))
                {
                    next = i;
                    lowestWeight = clips[i].IsValid() ? fromWeights[i] : -1f;
                }
            }
            if (next < 0) next = 0;
            blendingStartStop = fade > 0f && (IsTravelMotion(CurrentMotion) && IsStandingMotion(motion)
                || IsStandingMotion(CurrentMotion) && IsTravelMotion(motion));
            if (clips[next].IsValid())
            {
                mixer.DisconnectInput(next);
                graph.DestroyPlayable(clips[next]);
            }
            clips[next] = AnimationClipPlayable.Create(graph, clip);
            rangeStart = rangeEnd = 0f;
            clips[next].SetApplyFootIK(false);
            clips[next].SetApplyPlayableIK(false);
            clips[next].SetTime(0d);
            clips[next].SetSpeed(Mathf.Max(.01f, speed));
            graph.Connect(clips[next], 0, mixer, next);
            fromWeights[next] = 0f;
            currentSlot = next;
            CurrentMotion = motion;
            guardReleasing = false;
            fadeClock = 0f;
            fadeDuration = blendingStartStop ? Mathf.Max(fade, StartStopFade) : Mathf.Max(0f, fade);
            // Connect can give a newly connected port a live weight before the
            // next LateUpdate. Native Animator evaluation happens in between:
            // leaving the old and new ports at 1 would add two humanoid hips
            // translations for one rendered frame. Install the starting blend
            // now, and renormalize after removing the reused port.
            float sourceWeight = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                if (i == next || !clips[i].IsValid()) fromWeights[i] = 0f;
                else { fromWeights[i] = Mathf.Max(0f, fromWeights[i]); sourceWeight += fromWeights[i]; }
            }
            if (sourceWeight > .00001f)
                for (int i = 0; i < SlotCount; i++) fromWeights[i] /= sourceWeight;
            else fadeDuration = 0f;
            ApplyWeights(fadeDuration <= 0f ? 1f : 0f);
        }

        public bool HasTravelSpeed(float speed) => speed >= (IsTravelMotion(CurrentMotion) ? LocomotionStopSpeed : LocomotionStartSpeed);

        public void SetLocomotion(float speed, bool battle)
        {
            if (!Ready) return;
            // Stun and animation recovery are separate: at rest let the real
            // reaction finish, while movement/actions may cancel after stun.
            if (speed < .08f && (CurrentMotion == CombatMotion.Hit || CurrentMotion == CombatMotion.Knockback)
                && NormalizedTime < .999f) return;
            float threshold = motionSet.runTransitionSpeed;
            if (CurrentMotion == CombatMotion.Run) threshold -= .25f;
            CombatMotion motion = !HasTravelSpeed(speed) ? (battle ? CombatMotion.Idle : CombatMotion.ExplorationIdle)
                : speed >= threshold ? CombatMotion.Run : CombatMotion.Walk;
            float reference = motion == CombatMotion.Run ? motionSet.runReferenceSpeed : motionSet.walkReferenceSpeed;
            float rate = motion == CombatMotion.Idle || motion == CombatMotion.ExplorationIdle ? 1f
                : Mathf.Clamp(speed / Mathf.Max(.1f, reference), .35f, 1.5f);
            Play(motion, motionSet.locomotionFade, rate);
        }

        private void LateUpdate()
        {
            if (dialoguePoseLocked || !Ready || currentSlot < 0) return;
            if (IsImpactPaused) return;
            if (fadeDuration > 0f && fadeClock < fadeDuration)
            {
                // A slow render frame must not consume the entire start/stop blend.
                // Attack and airborne timing still follow their original fades.
                float elapsed = blendingStartStop ? Mathf.Min(Time.deltaTime, fadeDuration / MinimumStartStopFrames) : Time.deltaTime;
                fadeClock = Mathf.Min(fadeDuration, fadeClock + elapsed);
                ApplyWeights(Mathf.SmoothStep(0f, 1f, fadeClock / fadeDuration));
            }
            AnimationClipPlayable current = clips[currentSlot];
            if (!current.IsValid()) return;
            float length = MotionLength(CurrentMotion);
            if (IsLoop(CurrentMotion))
            {
                if (current.GetTime() >= length) current.SetTime(current.GetTime() % length);
            }
            else
            {
                double end = rangeEnd > rangeStart ? rangeEnd : CurrentMotion == CombatMotion.Guard && !guardReleasing ? length * motionSet.guardHoldNormalized : length;
                if (current.GetTime() >= end)
                {
                    current.SetTime(end);
                    current.SetSpeed(0d);
                }
            }
        }

        private void ApplyWeights(float blend)
        {
            blend = Mathf.Clamp01(blend);
            float total = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                float weight = clips[i].IsValid() ? Mathf.Max(0f, Mathf.Lerp(fromWeights[i], i == currentSlot ? 1f : 0f, blend)) : 0f;
                mixer.SetInputWeight(i, weight);
                total += weight;
            }
            if (total > .00001f)
                for (int i = 0; i < SlotCount; i++) mixer.SetInputWeight(i, mixer.GetInputWeight(i) / total);
            else if (currentSlot >= 0 && clips[currentSlot].IsValid()) mixer.SetInputWeight(currentSlot, 1f);
        }

        private static bool IsStandingMotion(CombatMotion motion) => motion == CombatMotion.Idle || motion == CombatMotion.ExplorationIdle;
        private static bool IsTravelMotion(CombatMotion motion) => motion == CombatMotion.Walk || motion == CombatMotion.Run;
        private static bool IsLoop(CombatMotion motion) => IsStandingMotion(motion) || IsTravelMotion(motion) || motion == CombatMotion.JumpLoop;

        private void ReleaseGraph()
        {
            dialoguePoseLocked = false;
            impactPauseUntil = 0f;
            if (graph.IsValid()) graph.Destroy();
            currentSlot = -1;
            guardReleasing = false;
            for (int i = 0; i < SlotCount; i++) { clips[i] = default; fromWeights[i] = 0f; }
        }
    }
}
