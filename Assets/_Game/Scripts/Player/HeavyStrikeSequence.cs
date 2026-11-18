using UnityEngine;

namespace FengChenInn
{
    // Optional player-only sequencing. It does not edit the source clip or NPC attacks.
    public sealed class HeavyStrikeSequence
    {
        int next;
        float expires;
        CombatActor target;
        public int NextIndex => Time.time <= expires ? next : 0;
        public void Reset() { next = 0; expires = 0f; target = null; }
        public void ObserveTarget(CombatActor currentTarget)
        { if (next != 0 && target != currentTarget) Reset(); }
        public void Sustain(float grace)
        { if (next != 0 && Time.time <= expires) expires = Mathf.Max(expires, Time.time + grace); }
        public int Select(CombatActor currentTarget)
        {
            if (Time.time > expires || target != currentTarget) Reset();
            return next;
        }
        public void Accepted(int index, int count, CombatActor currentTarget, float duration, float grace)
        {
            next = (index + 1) % Mathf.Max(1, count);
            target = currentTarget;
            expires = Time.time + duration + grace;
        }

        public static CombatAttack Prepare(CombatAttack source, float clipLength, float tempo, int slice = -1)
        {
            float baseSpeed = Mathf.Max(.05f, source.speed);
            float speed = baseSpeed * Mathf.Max(.5f, tempo);
            var original = source.hitWindows != null && source.hitWindows.Length > 0 ? source.hitWindows
                : new[] { new CombatHitWindow { start = source.activeStart, end = source.activeEnd } };
            float from = 0f, to = clipLength;
            int first = 0, count = original.Length;
            if (slice >= 0)
            {
                first = Mathf.Clamp(slice, 0, original.Length - 1); count = 1;
                from = first == 0 ? 0f : (original[first - 1].end + original[first].start) * .5f * baseSpeed;
                to = first + 1 < original.Length ? (original[first].end + original[first + 1].start) * .5f * baseSpeed
                    : Mathf.Min(clipLength, (original[first].end + .28f) * baseSpeed);
            }
            var windows = new CombatHitWindow[count];
            for (int i = 0; i < count; i++)
            {
                var w = original[first + i];
                windows[i] = new CombatHitWindow { start = Mathf.Max(0f, (w.start * baseSpeed - from) / speed),
                    end = Mathf.Max(0f, (w.end * baseSpeed - from) / speed), damageMultiplier = w.damageMultiplier };
            }
            return new CombatAttack { label = slice < 0 ? source.label : "重击第 " + (first + 1) + " 式",
                motion = source.motion, damage = source.damage, duration = (to - from) / speed, speed = speed,
                activeStart = windows[0].start, activeEnd = windows[count - 1].end, hitWindows = windows,
                range = source.range, knockback = source.knockback, stagger = source.stagger, unblockable = source.unblockable,
                clipStart = from, clipEnd = to, blendIn = slice < 0 ? .07f : .10f };
        }
    }
}
