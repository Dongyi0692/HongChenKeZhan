using UnityEngine;

namespace FengChenInn
{
    public enum FootstepGround { WetGround, WoodFloor }

    // Add to a collider (or its parent) to override automatic Stage12 surface mapping.
    [DisallowMultipleComponent]
    public sealed class FootstepSurface : MonoBehaviour
    {
        public FootstepGround ground = FootstepGround.WetGround;

        public static FootstepGround Resolve(Collider collider)
        {
            var marker = collider.GetComponentInParent<FootstepSurface>();
            if (marker) return marker.ground;
            string name = collider.name;
            // These are the actual Stage12 collision meshes, including the invisible stair ramp.
            // Exterior porch stays wet even though its deck is made of wood.
            if (name.StartsWith("GroundFloor_slab") || name.StartsWith("GroundFloor_plank_")
                || name.StartsWith("SecondFloor_") || name.StartsWith("Staircase_step_")
                || name == "Stair_New_ClosedRiser" || name == "Stage12 Stair smooth walking surface"
                || name == "Stair_UpperLanding_Seam" || name == "Main hall floor") return FootstepGround.WoodFloor;
            return FootstepGround.WetGround;
        }
    }
}
