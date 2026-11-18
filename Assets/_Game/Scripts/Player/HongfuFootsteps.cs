using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FengChenInn
{
    [DisallowMultipleComponent, RequireComponent(typeof(PrototypeFighter))]
    [DefaultExecutionOrder(300)]
    public sealed class HongfuFootsteps : MonoBehaviour
    {
        [Range(0, 1)] public float volume = .8f;
        [Range(0, .15f)] public float pitchVariation = .045f;
        // The follow camera sits 3 m behind the character and pulls back to 3.2 m for
        // combat framing. Keeping that whole range inside minDistance means the local
        // player's own steps never lose level to 3D rolloff.
        [Min(.1f)] public float minDistance = 4f;
        [Min(1)] public float maxDistance = 18;
        // Wet steps sit under a continuous rain bed, so the promoted set carries a
        // presence lift and a louder body than the original mono segments.
        [Tooltip("Promoted wet set, falls back to the original segments if missing.")]
        public string wetFolder = "Footsteps/WetGroundLoud";
        [Tooltip("Fallback only when no locomotion clip is available.")]
        public float walkStepDistance = .8f, runStepDistance = 1.4f;
        // Sampled foot contacts are at phase 0 / .5; clips retain ~8 ms of attack pre-roll.
        [Range(0, .5f)] public float runFirstContact = .49f, walkFirstContact = .49f;

        public FootstepGround CurrentGround { get; private set; }
        public Collider GroundCollider { get; private set; }
        public int TotalSteps { get; private set; }
        public float ActualSpeed { get; private set; }
        public float LastPitch { get; private set; }
        public AudioClip LastClip { get; private set; }
        public bool UsesAnimationEvents { get; private set; }
        public event Action<FootstepGround, AudioClip> StepPlayed;

        PrototypeFighter fighter;
        CombatActor actor;
        CharacterController controller;
        AholoAnimationDriver driver;
        AudioClip[] wet, wood;
        readonly AudioSource[] voices = new AudioSource[3];
        readonly float[] voiceVolumes = new float[3];
        readonly RaycastHit[] hits = new RaycastHit[24];
        Vector3 previousPosition;
        float distance, nextStepDistance, lastStride, lastStepTime = -10;
        float landingAt = -10, airTime;
        bool wasGrounded = true;
        int voiceIndex, lastWet = -1, lastWood = -1;
        bool moving, pendingEvent, walkEvents, runEvents;
        CombatMotion previousMotion, lastDisplayedMotion;
        float lastAnimationPhase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe()
        {
            SceneManager.sceneLoaded -= Attach;
            SceneManager.sceneLoaded += Attach;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachInitial() => Attach(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        static void Attach(Scene scene, LoadSceneMode mode)
        {
            foreach (var player in FindObjectsByType<PrototypeFighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (player.gameObject.scene == scene && !player.GetComponent<HongfuFootsteps>())
                    player.gameObject.AddComponent<HongfuFootsteps>();
        }
        void Awake()
        {
            fighter = GetComponent<PrototypeFighter>(); actor = GetComponent<CombatActor>(); controller = GetComponent<CharacterController>();
            wet = Resources.LoadAll<AudioClip>(wetFolder);
            if (wet == null || wet.Length == 0) wet = Resources.LoadAll<AudioClip>("Footsteps/WetGround");
            wood = Resources.LoadAll<AudioClip>("Footsteps/WoodFloor");
            Array.Sort(wet, (a,b) => string.CompareOrdinal(a.name,b.name));
            Array.Sort(wood, (a,b) => string.CompareOrdinal(a.name,b.name));
            for (int i=0; i<voices.Length; i++)
            {
                var child = new GameObject("Hongfu footsteps " + i);
                child.transform.SetParent(transform, false); child.transform.localPosition = Vector3.up * .12f;
                var source = child.AddComponent<AudioSource>(); voices[i] = source;
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 1;
                source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = minDistance;
                source.maxDistance = maxDistance; source.dopplerLevel = 0; source.spread = 0;
                source.priority = 100;
            }
            previousPosition = transform.position;
            if (wet.Length == 0 || wood.Length == 0) Debug.LogWarning("Hongfu footsteps: missing surface clips", this);
        }
        void Start()
        {
            driver = fighter.animationDriver;
            if (!driver || !driver.motionSet) return;
            walkEvents = HasEvents(driver.motionSet.walk); runEvents = HasEvents(driver.motionSet.run);
            if (driver.targetAnimator)
            {
                var relay = driver.targetAnimator.GetComponent<FootstepAnimationEvents>()
                    ?? driver.targetAnimator.gameObject.AddComponent<FootstepAnimationEvents>();
                relay.owner = this;
            }
        }
        static bool HasEvents(AnimationClip clip)
        {
            if (!clip) return false;
            foreach (var e in clip.events) if (FootstepAnimationEvents.Supports(e.functionName)) return true;
            return false;
        }
        public void QueueAnimationStep() => pendingEvent = true;

        void LateUpdate()
        {
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            float moved = new Vector2(delta.x, delta.z).magnitude;
            ActualSpeed = Time.deltaTime > 0 ? moved / Time.deltaTime : 0;
            var displayedMotion = driver && driver.Ready ? driver.CurrentMotion : CombatMotion.Run;
            var motion = displayedMotion == CombatMotion.Walk ? CombatMotion.Walk : CombatMotion.Run;
            float phase = driver && driver.Ready ? driver.NormalizedTime : 0;
            bool timelineChanged = displayedMotion != lastDisplayedMotion
                || Mathf.Repeat(phase - lastAnimationPhase, 1) > .15f;
            lastDisplayedMotion = displayedMotion; lastAnimationPhase = phase;
            bool contact = ProbeGround();
            bool grounded = contact && (fighter.IsGrounded || (groundGap <= controller.skinWidth + .025f && delta.y <= .015f));
            // The gait restarts on landing, so the next step to fire is the actual footfall.
            // Only lift that one, and only for a real drop.
            if (grounded && !wasGrounded && airTime > .15f) landingAt = Time.time;
            wasGrounded = grounded;
            airTime = grounded ? 0 : airTime + Time.deltaTime;
            bool valid = Time.timeScale > 0 && fighter.enabled && grounded
                && fighter.State == PrototypeFighter.ActionState.Ready && !fighter.IsDialoguePoseActive
                && fighter.MoveInput.sqrMagnitude > .0001f && ActualSpeed > .12f
                && (!actor || (!actor.IsAttacking && !actor.IsDefeated && !actor.ControlLocked && !actor.IsStunned))
                && !fighter.IsGuarding && (!driver || !driver.IsImpactPaused)
                && moved < Mathf.Max(.75f, fighter.EffectiveMoveSpeed * Time.deltaTime * 2.5f)
                && (!driver || !driver.Ready || displayedMotion == CombatMotion.Walk || displayedMotion == CombatMotion.Run);
            if (!valid)
            {
                // A one-frame collision/step seam must not erase half a completed stride.
                // Genuine jumps, stopping and action changes start a new gait instead.
                if (Time.timeScale <= 0 || ActualSpeed <= .12f || fighter.MoveInput.sqrMagnitude <= .0001f
                    || fighter.State != PrototypeFighter.ActionState.Ready || fighter.IsDialoguePoseActive
                    || (driver && driver.Ready && displayedMotion != CombatMotion.Walk && displayedMotion != CombatMotion.Run)
                    || !contact || groundGap > .35f)
                { moving = false; distance = 0; }
                pendingEvent = false;
                // Finish a very short fade, including while the game clock is paused.
                for (int i=0;i<voices.Length;i++) if (voices[i] && voices[i].isPlaying)
                {
                    voices[i].volume = Mathf.MoveTowards(voices[i].volume, 0, Time.unscaledDeltaTime * voiceVolumes[i] / .04f);
                    if (voices[i].volume <= .0001f) voices[i].Stop();
                }
                return;
            }
            bool running = ActualSpeed >= fighter.EffectiveMoveSpeed * .75f;
            float stride = running ? runStepDistance : walkStepDistance;
            if (driver && driver.Ready && driver.motionSet)
            {
                var clip = driver.motionSet.GetClip(motion);
                float reference = motion == CombatMotion.Run ? driver.motionSet.runReferenceSpeed : driver.motionSet.walkReferenceSpeed;
                // Match the controller's actual clamped playback rate, including slow input.
                float rate = Mathf.Clamp(ActualSpeed / Mathf.Max(.1f, reference), .35f, 1.5f);
                if (clip) stride = Mathf.Max(.05f, ActualSpeed * clip.length / (2 * rate));
            }
            UsesAnimationEvents = motion == CombatMotion.Walk ? walkEvents : runEvents;
            if (!moving || previousMotion != motion || timelineChanged)
            {
                distance = -moved; nextStepDistance = stride * .5f;
                if (driver && driver.Ready && displayedMotion == motion)
                {
                    float contactPhase = motion == CombatMotion.Run ? runFirstContact : walkFirstContact;
                    // Align the first distance threshold to this clip's current gait phase.
                    float fraction = Mathf.Repeat(contactPhase - driver.NormalizedTime, .5f) * 2;
                    nextStepDistance = stride * Mathf.Max(.04f, fraction);
                }
            }
            else if (lastStride > 0)
            {
                // Preserve progress through the current step when input speed changes.
                distance *= stride / lastStride;
                nextStepDistance *= stride / lastStride;
            }
            lastStride = stride;
            moving = true; previousMotion = motion; distance += moved;
            if (UsesAnimationEvents)
            {
                if (pendingEvent && Time.time - lastStepTime > .12f) PlayStep(running);
            }
            else if (distance >= nextStepDistance && Time.time - lastStepTime > .12f)
            {
                distance = Mathf.Min(distance - nextStepDistance, stride * .25f);
                nextStepDistance = stride;
                PlayStep(running);
            }
            pendingEvent = false;
        }
        float groundGap;
        bool ProbeGround()
        {
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .4f, Vector3.down, hits, .9f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity; Collider found = null;
            for (int i=0; i<count; i++)
            {
                var hit = hits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .45f || hit.distance >= closest) continue;
                closest = hit.distance; found = hit.collider;
            }
            GroundCollider = found;
            groundGap = closest - .4f;
            if (!found) return false;
            CurrentGround = FootstepSurface.Resolve(found); return true;
        }
        void PlayStep(bool running)
        {
            var clips = CurrentGround == FootstepGround.WoodFloor ? wood : wet;
            if (clips.Length == 0) return;
            int previous = CurrentGround == FootstepGround.WoodFloor ? lastWood : lastWet;
            int choice = UnityEngine.Random.Range(0, clips.Length);
            if (clips.Length > 1 && choice == previous) choice = (choice + UnityEngine.Random.Range(1, clips.Length)) % clips.Length;
            if (CurrentGround == FootstepGround.WoodFloor) lastWood = choice; else lastWet = choice;
            var source = voices[voiceIndex];
            source.Stop(); source.clip = clips[choice]; source.pitch = UnityEngine.Random.Range(1 - pitchVariation, 1 + pitchVariation);
            float landing = Time.time - landingAt < .5f ? 1.14f : 1f;
            source.volume = Mathf.Clamp01(volume * (running ? 1 : .83f) * UnityEngine.Random.Range(.94f, 1) * landing);
            voiceVolumes[voiceIndex] = source.volume;
            source.Play(); voiceIndex = (voiceIndex + 1) % voices.Length;
            lastStepTime = Time.time; LastPitch = source.pitch; LastClip = source.clip; TotalSteps++;
            StepPlayed?.Invoke(CurrentGround, LastClip);
        }
        void OnEnable() { previousPosition = transform.position; moving = false; distance = 0; }
        void OnDisable()
        {
            foreach (var source in voices) if (source) source.Stop();
            moving = false; pendingEvent = false; distance = 0;
        }
    }
}
