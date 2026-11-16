using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FengChenInn
{
    // Keep the authored visible steps and railings, but walk on one smooth surface.
    // Derive the surface from this scene's step colliders so moving the staircase
    // as a whole does not leave a stale ramp in the old location.
    public sealed class InnStairTraversal : MonoBehaviour
    {
        readonly List<BoxCollider> steps = new();
        readonly List<bool> enabledBefore = new();
        GameObject ramp;
        Mesh mesh;
        public Vector3 Bottom { get; private set; }
        public Vector3 Top { get; private set; }
        public bool Ready => ramp != null;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            var candidates = GetComponentsInChildren<BoxCollider>(true)
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && c.name.StartsWith("Staircase_step_"))
                .OrderBy(c => c.bounds.center.y).ToArray();
            if (candidates.Length < 2) return;
            Bounds first = candidates[0].bounds, last = candidates[candidates.Length - 1].bounds;
            Vector3 forward = Vector3.ProjectOnPlane(last.center - first.center, Vector3.up).normalized;
            if (forward.sqrMagnitude < .9f) return;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float halfLength = Mathf.Abs(forward.x) * first.extents.x + Mathf.Abs(forward.z) * first.extents.z;
            float halfWidth = Mathf.Abs(right.x) * first.extents.x + Mathf.Abs(right.z) * first.extents.z;
            float rise = (last.max.y - first.max.y) / (candidates.Length - 1);
            Bottom = new Vector3(first.center.x, first.max.y - rise * .5f, first.center.z) - forward * halfLength;
            Top = new Vector3(last.center.x, last.max.y + rise * .5f, last.center.z) + forward * halfLength;
            // Match the existing upper walkway rather than adding a lip at the join.
            foreach (var c in GetComponentsInChildren<BoxCollider>(true))
                if (c.name == "Upper rear walkway" && Mathf.Abs(c.bounds.max.y - Top.y) < .2f)
                    Top = new Vector3(Top.x, c.bounds.max.y, Top.z);
            float baseY = Mathf.Min(first.min.y - .1f, Bottom.y - .1f);
            Vector3[] points =
            {
                Bottom-right*halfWidth, Bottom+right*halfWidth, Top-right*halfWidth, Top+right*halfWidth,
                Bottom-right*halfWidth, Bottom+right*halfWidth, Top-right*halfWidth, Top+right*halfWidth
            };
            for (int i = 0; i < 4; i++) points[i].y = baseY;
            ramp = new GameObject("Stair smooth walking surface");
            ramp.layer = gameObject.layer;
            ramp.transform.SetParent(transform, false);
            for (int i = 0; i < points.Length; i++) points[i] = ramp.transform.InverseTransformPoint(points[i]);
            mesh = new Mesh { name = "Inn stair traversal mesh" };
            mesh.vertices = points;
            mesh.triangles = new[] { 4,6,5, 5,6,7, 0,1,2, 1,3,2, 0,4,1, 1,4,5, 2,3,6, 3,7,6, 0,2,4, 2,6,4, 1,5,3, 3,5,7 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
            foreach (var step in candidates)
            {
                steps.Add(step); enabledBefore.Add(step.enabled); step.enabled = false;
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < steps.Count; i++) if (steps[i] != null) steps[i].enabled = enabledBefore[i];
            steps.Clear(); enabledBefore.Clear();
            if (ramp != null) { ramp.SetActive(false); Destroy(ramp); }
            if (mesh != null) Destroy(mesh);
            ramp = null; mesh = null;
        }
    }
}
