using System;
using UnityEngine;

namespace FengChenInn
{
    [Serializable]
    public sealed class AholoRootMotion
    {
        public bool enabled;
        [Min(0f)] public float duration;
        public AnimationCurve x = new AnimationCurve();
        public AnimationCurve z = new AnimationCurve();

        public bool Ready => enabled && duration > 0f && x != null && z != null && x.length >= 2 && z.length >= 2;
        public Vector3 Evaluate(float clipSeconds)
        {
            float time = Mathf.Clamp(clipSeconds, 0f, duration);
            return new Vector3(x.Evaluate(time), 0f, z.Evaluate(time));
        }
    }

    [CreateAssetMenu(menuName = "FengChenInn/Aholo Motion Set", fileName = "AholoMotions")]
    public sealed class AholoMotionSet : ScriptableObject
    {
        public GameObject visualPrefab;
        [Header("Locomotion — use in-place clips on this model's skeleton")]
        public AnimationClip idle, walk, run;
        [Tooltip("Ordinary standing idle used outside combat; never substitute sword-ready idle.")]
        public AnimationClip explorationIdle;
        [Header("Combat")]
        public AnimationClip light, heavy, skill, guard, dodge;
        [Header("Jump and reactions")]
        public AnimationClip jumpStart, jumpLoop, jumpLand, hit, knockback, death;
        [Header("Extracted horizontal motion — applied by CharacterController")]
        public AholoRootMotion lightRootMotion = new AholoRootMotion();
        public AholoRootMotion heavyRootMotion = new AholoRootMotion();
        public AholoRootMotion skillRootMotion = new AholoRootMotion();
        [Header("Playback tuning")]
        [Min(.1f)] public float walkReferenceSpeed = 2.2f;
        [Min(.1f)] public float runReferenceSpeed = 4.2f;
        [Min(.1f)] public float runTransitionSpeed = 3.15f;
        [Range(.02f, .5f)] public float locomotionFade = .16f;
        [Tooltip("Stop at this point of Sword_Block while the guard button is held.")]
        [Range(0f, 1f)] public float guardHoldNormalized = .55f;
        [Tooltip("Switch from the take-off clip to the air loop at this fraction of the clip.")]
        [Range(.05f, 1f)] public float jumpStartToLoopNormalized = .65f;

        public bool HasRootMotion(CombatMotion motion) => RootMotionFor(motion)?.Ready == true;

        public bool TryGetRootDelta(CombatMotion motion, float fromClipSeconds, float toClipSeconds, out Vector3 localDelta)
        {
            AholoRootMotion curve = RootMotionFor(motion);
            localDelta = Vector3.zero;
            if (curve == null || !curve.Ready || toClipSeconds < fromClipSeconds) return false;
            localDelta = curve.Evaluate(toClipSeconds) - curve.Evaluate(fromClipSeconds);
            return true;
        }

        private AholoRootMotion RootMotionFor(CombatMotion motion) => motion switch
        {
            CombatMotion.Light => lightRootMotion,
            CombatMotion.Heavy => heavyRootMotion,
            CombatMotion.Skill => skillRootMotion,
            _ => null
        };

        public AnimationClip GetClip(CombatMotion motion)
        {
            return motion switch
            {
                CombatMotion.Idle => idle,
                CombatMotion.ExplorationIdle => explorationIdle,
                CombatMotion.Walk => walk,
                CombatMotion.Run => run,
                CombatMotion.Light => light,
                CombatMotion.Heavy => heavy,
                CombatMotion.Skill => skill,
                CombatMotion.Guard => guard,
                CombatMotion.JumpStart => jumpStart,
                CombatMotion.JumpLoop => jumpLoop,
                CombatMotion.JumpLand => jumpLand,
                CombatMotion.Dodge => dodge,
                CombatMotion.Hit => hit,
                CombatMotion.Knockback => knockback,
                CombatMotion.Death => death,
                _ => null
            };
        }
    }
}
