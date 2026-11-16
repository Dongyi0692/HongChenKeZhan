using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FengChenInn
{
    // Watches actions that actually execute. Pressing a rejected/queued key does
    // not advance practice, and the final skill must finish before battle starts.
    public sealed class InnPracticeGuide : IDisposable
    {
        public const int StepCount = 8;
        public int StepIndex { get; private set; }
        public bool Active { get; private set; }
        public bool Complete => StepIndex >= StepCount;
        public string Progress => $"实操 {Mathf.Min(StepIndex + 1, StepCount)} / {StepCount}";
        private PrototypeFighter fighter;
        private Camera view;
        private Vector3 lastPosition, lastViewForward;
        private float distance, lookAngle, guardTime;
        private bool actionExecuted;

        public void Begin(PrototypeFighter player, Camera camera)
        {
            Dispose();
            fighter = player;
            view = camera;
            StepIndex = 0;
            Active = true;
            fighter.ActionPerformed += OnAction;
            ResetStep();
        }

        public string Instruction => StepIndex switch
        {
            0 => "WASD  移动并转身 · 实际走出一小段距离",
            1 => "移动鼠标  转动视角 · 看一眼四周",
            2 => "鼠标左键  轻击一次 · 等剑势收回",
            3 => "鼠标右键  重击一次 · 等连招结束",
            4 => "按住 Q  格挡片刻 · 面向来招",
            5 => "Shift  闪避一次 · 消耗体力",
            6 => "Space  跳跃一次 · 落地后继续",
            7 => $"F  突进斩一次 · 消耗 {fighter.skillStaminaCost:0} 体力，等收势结束",
            _ => "练习完成 · 准备交手"
        };

        public void Tick(float dt)
        {
            if (!Active || Complete || fighter == null || dt <= 0f) return;
            Vector3 position = fighter.transform.position;
            Vector3 forward = view != null ? view.transform.forward : lastViewForward;
            if (StepIndex == 0 && fighter.MoveInput.sqrMagnitude > .01f)
                distance += Vector3.ProjectOnPlane(position - lastPosition, Vector3.up).magnitude;
            if (StepIndex == 1 && view != null && Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > .04f)
                lookAngle += Vector3.Angle(lastViewForward, forward);
            lastPosition = position;
            lastViewForward = forward;
            if (StepIndex == 4) guardTime = fighter.IsGuarding ? guardTime + dt : 0f;
            bool settled = fighter.State == PrototypeFighter.ActionState.Ready && fighter.IsGrounded
                && (fighter.GetComponent<CombatActor>() == null || !fighter.GetComponent<CombatActor>().IsAttacking);
            bool done = StepIndex == 0 ? distance >= 1.2f : StepIndex == 1 ? lookAngle >= 18f
                : StepIndex == 4 ? guardTime >= .4f : actionExecuted && settled;
            if (!done) return;
            StepIndex++;
            ResetStep();
            if (Complete) Active = false;
        }

        private void OnAction(CombatMotion action)
        {
            if (!Active) return;
            CombatMotion expected = StepIndex switch {
                2 => CombatMotion.Light, 3 => CombatMotion.Heavy, 4 => CombatMotion.Guard,
                5 => CombatMotion.Dodge, 6 => CombatMotion.JumpStart, 7 => CombatMotion.Skill,
                _ => CombatMotion.Idle
            };
            if (StepIndex >= 2 && action == expected) actionExecuted = true;
        }

        private void ResetStep()
        {
            distance = lookAngle = guardTime = 0f;
            actionExecuted = false;
            lastPosition = fighter != null ? fighter.transform.position : Vector3.zero;
            lastViewForward = view != null ? view.transform.forward : Vector3.forward;
        }

        public void Dispose()
        {
            if (fighter != null) fighter.ActionPerformed -= OnAction;
            Active = false;
        }
    }
}
