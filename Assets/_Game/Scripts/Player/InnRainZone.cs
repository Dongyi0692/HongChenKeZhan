using UnityEngine;
using UnityEngine.SceneManagement;

namespace FengChenInn
{
    // Shelter state for the rain ambience (0 = under the inn roof, 1 = open sky).
    //
    // The inn is the only roofed playable volume, and the project already encodes that
    // boundary in its surface map: every walkable collider under the roof resolves to
    // WoodFloor (hall, stairs, upper floor and the sheltered upper walkway), while the
    // porch, the road and the backyard stay WetGround on purpose. Reusing that map means
    // the rain crossfade and the footstep swap happen on exactly the same threshold,
    // with no extra trigger volumes, colliders or scene edits.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class InnRainZone : MonoBehaviour
    {
        [Tooltip("Seconds of a consistent reading before moving further under the roof.")]
        [Min(0f)] public float enterDelay = .25f;
        [Tooltip("Seconds of a consistent reading before stepping back out into the rain.")]
        [Min(0f)] public float exitDelay = .45f;

        // Read by MenuAudio every frame. 0 = indoor roof rain only, 1 = outdoor rain only.
        public static float Outdoor { get; private set; }
        public static bool Active { get; private set; }

        public bool Indoor => committed;
        public string GroundName { get; private set; }
        public int Changes { get; private set; }

        HongfuFootsteps steps;
        readonly RaycastHit[] hits = new RaycastHit[12];
        bool committed, candidate, sampled;
        float dwell;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Outdoor = 0; Active = false; }

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
                if (player.gameObject.scene == scene && !player.GetComponent<InnRainZone>())
                    player.gameObject.AddComponent<InnRainZone>();
        }

        void Awake() => steps = GetComponent<HongfuFootsteps>();

        void LateUpdate()
        {
            if (!ResolveGround(out var ground)) return;
            // The very first reading decides the zone outright: the rain that greets the
            // player at scene start must already match where they are standing.
            if (!sampled)
            {
                sampled = true;
                committed = candidate = ground == FootstepGround.WoodFloor;
                Outdoor = committed ? 0 : 1;
                Active = true;
                return;
            }
            bool indoor = ground == FootstepGround.WoodFloor;
            if (indoor == committed) { candidate = indoor; dwell = 0; return; }
            if (indoor != candidate) { candidate = indoor; dwell = 0; return; }
            dwell += Time.unscaledDeltaTime;
            if (dwell < (indoor ? enterDelay : exitDelay)) return;
            committed = indoor;
            dwell = 0;
            Changes++;
            Outdoor = committed ? 0 : 1;
            Active = true;
        }

        // Prefer the footstep probe that already runs every frame; it resolves the same
        // Stage12 colliders the game uses for surface audio.
        bool ResolveGround(out FootstepGround ground)
        {
            ground = FootstepGround.WetGround;
            if (steps && steps.GroundCollider)
            {
                ground = steps.CurrentGround;
                GroundName = steps.GroundCollider.name;
                return true;
            }
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .4f, Vector3.down, hits, .9f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            Collider found = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .45f || hit.distance >= closest) continue;
                closest = hit.distance;
                found = hit.collider;
            }
            if (!found) return false;
            GroundName = found.name;
            ground = FootstepSurface.Resolve(found);
            return true;
        }

        void OnDisable() { sampled = false; dwell = 0; }
    }
}
