using UnityEngine;

namespace FengChenInn
{
    // Lives on the Animator object, where Unity dispatches Animation Events.
    // Only locomotion clips should bind these callbacks; never attack clips.
    [DisallowMultipleComponent]
    public sealed class FootstepAnimationEvents : MonoBehaviour
    {
        public HongfuFootsteps owner;
        public void Footstep() { if(owner) owner.QueueAnimationStep(); }
        public void OnFootstep() => Footstep();
        public void FootstepLeft() => Footstep();
        public void FootstepRight() => Footstep();
        public static bool Supports(string function) => function == "Footstep" || function == "OnFootstep"
            || function == "FootstepLeft" || function == "FootstepRight";
    }
}
