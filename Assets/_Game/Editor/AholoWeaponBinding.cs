using System;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only equipment calibration. Offsets are intentional review controls, not bone animation.</summary>
[Serializable]
public sealed class WeaponCalibration
{
    public bool useNamedMarkers = true;
    public string sourceMeshName = "";
    public Vector3 sourceGrip, sourceBladeBase, sourceBladeTip, sourceBroadPoint;
    public float uniformSwordScale = 1f;
    [Range(0f, 1f)] public float gripFraction = .65f;
    public Vector3 socketPositionOffsetMeters, socketEulerOffset;
    public float bladeRadius = .035f;
}

/// <summary>Builds a static attachment to the animated right hand. Never rotates rig bones or adds IK.</summary>
public static class AholoWeaponBinding
{
    public const string SocketName = "AholoSwordSocket";

    public static Transform Attach(CombatActor actor, Animator animator, GameObject swordAsset, WeaponCalibration calibration)
    {
        if (actor == null || animator == null || swordAsset == null || calibration == null)
            throw new ArgumentNullException("Actor, Animator, sword asset and explicit calibration are required.");
        if (!(calibration.uniformSwordScale > 0f) || !(calibration.bladeRadius > 0f))
            throw new InvalidOperationException("Sword scale and radius must be positive.");
        Transform hand = Bone(animator, HumanBodyBones.RightHand, "mixamorig:RightHand");
        Transform index = Bone(animator, HumanBodyBones.RightIndexProximal, "mixamorig:RightHandIndex1");
        Transform middle = Bone(animator, HumanBodyBones.RightMiddleProximal, "mixamorig:RightHandMiddle1");
        Transform pinky = Bone(animator, HumanBodyBones.RightLittleProximal, "mixamorig:RightHandPinky1");
        if (hand == null || index == null || middle == null || pinky == null)
            throw new InvalidOperationException("An exposed right hand and three proximal finger bones are required.");
        if (hand.Find(SocketName) != null)
            throw new InvalidOperationException("The hand already has an Aholo sword socket; review/remove it before rebinding.");
        Vector3 scale = hand.lossyScale;
        if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f ||
            Mathf.Abs(scale.x - scale.y) > Mathf.Abs(scale.x) * .005f ||
            Mathf.Abs(scale.x - scale.z) > Mathf.Abs(scale.x) * .005f)
            throw new InvalidOperationException("Nonuniform or mirrored hand scale cannot safely preserve sword dimensions.");
        Vector3 alongHandle = (index.position - pinky.position).normalized;
        Vector3 palmUp = Vector3.ProjectOnPlane(middle.position - hand.position, alongHandle).normalized;
        if (alongHandle.sqrMagnitude < .99f || palmUp.sqrMagnitude < .99f)
            throw new InvalidOperationException("Right-hand bone positions do not define a valid grip frame.");
        Vector3 knuckles = (index.position + middle.position + pinky.position) / 3f;
        var socket = new GameObject(SocketName).transform;
        try
        {
            socket.SetParent(hand, false);
            socket.localScale = Vector3.one / scale.x;
            socket.SetPositionAndRotation(
                Vector3.Lerp(hand.position, knuckles, calibration.gripFraction) + hand.rotation * calibration.socketPositionOffsetMeters,
                Quaternion.LookRotation(alongHandle, palmUp) * Quaternion.Euler(calibration.socketEulerOffset));
            // Normalize a neutral wrapper, preserving FBX-import root transforms/unit conversion.
            var geometry = new GameObject("IndependentSword").transform;
            geometry.SetParent(socket, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(swordAsset, geometry);
            if (model == null) throw new InvalidOperationException("Could not instantiate the separate sword asset.");
            Vector3 grip, bladeBase, tip, broad;
            if (calibration.useNamedMarkers)
            {
                grip = Point(geometry, "GripCenter");
                bladeBase = Point(geometry, "BladeBase");
                tip = Point(geometry, "BladeTip");
                broad = Point(geometry, "BladeBroadAxis");
            }
            else
            {
                Transform reference = model.transform;
                if (!string.IsNullOrEmpty(calibration.sourceMeshName))
                    reference = model.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(m => m.name == calibration.sourceMeshName)?.transform
                        ?? throw new InvalidOperationException("Calibrated sword mesh not found: " + calibration.sourceMeshName);
                Vector3 RootPoint(Vector3 local) => geometry.InverseTransformPoint(reference.TransformPoint(local));
                grip = RootPoint(calibration.sourceGrip);
                bladeBase = RootPoint(calibration.sourceBladeBase);
                tip = RootPoint(calibration.sourceBladeTip);
                broad = RootPoint(calibration.sourceBroadPoint);
            }
            Vector3 forward = (tip - grip).normalized;
            Vector3 right = Vector3.ProjectOnPlane(broad - grip, forward).normalized;
            if ((tip - bladeBase).magnitude < .1f || forward.sqrMagnitude < .99f || right.sqrMagnitude < .99f)
                throw new InvalidOperationException("Sword marker/calibration geometry is invalid.");
            Quaternion normalize = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.Cross(forward, right)));
            geometry.localRotation = normalize;
            geometry.localScale = Vector3.one * calibration.uniformSwordScale;
            geometry.localPosition = -(normalize * grip) * calibration.uniformSwordScale;
            Transform start = NewPoint(socket, "BladeBase", (normalize * (bladeBase - grip)) * calibration.uniformSwordScale);
            Transform end = NewPoint(socket, "BladeTip", (normalize * (tip - grip)) * calibration.uniformSwordScale);
            // Sword damage comes only from CombatActor sweep. Never introduce an unreviewed Rigidbody/collider.
            if (model.GetComponentsInChildren<Collider>(true).Length > 0 || model.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                throw new InvalidOperationException("Sword asset contains physics components; strip them in an equipment prefab first.");
            actor.WeaponRoot = socket;
            actor.BladeBase = start;
            actor.BladeTip = end;
            actor.BladeAxis = Vector3.forward;
            actor.BladeStart = start.localPosition.z;
            actor.BladeEnd = end.localPosition.z;
            actor.BladeRadius = calibration.bladeRadius;
            EditorUtility.SetDirty(actor);
            return socket;
        }
        catch { UnityEngine.Object.DestroyImmediate(socket.gameObject); throw; }
    }

    // Profile inferred from this project's inspected RoninSword geometry. It remains subject to hand/roll visual review.
    // Actual measured source length .683531m; grip spans roughly -.04..+.10m, guard .10..+.14m, blade ends +.60738m.
    public static WeaponCalibration MeasuredRonin(GameObject swordAsset)
    {
        if (swordAsset == null) throw new ArgumentNullException(nameof(swordAsset));
        var mesh = swordAsset.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(m => m.name == "RoninSword");
        if (mesh == null || mesh.sharedMesh == null) throw new InvalidOperationException("Expected RoninSword mesh.");
        Vector3[] points = mesh.sharedMesh.vertices;
        Bounds bounds = mesh.sharedMesh.bounds;
        int along = LargestAxis(bounds.size), broad = LargestOtherAxis(bounds.size, along);
        float sign = Mathf.Abs(bounds.max[along]) >= Mathf.Abs(bounds.min[along]) ? 1f : -1f;
        float back = sign > 0f ? bounds.min[along] : -bounds.max[along];
        float length = bounds.size[along];
        Vector3 alongAxis = Vector3.zero; alongAxis[along] = 1f;
        float importedLength = length * mesh.transform.TransformVector(alongAxis).magnitude;
        if (Mathf.Abs(importedLength - .683531f) > .015f)
            throw new InvalidOperationException("Ronin source dimensions differ from the inspected asset; recalibration is required.");
        Vector3 At(float fraction)
        {
            float coordinate = (back + length * fraction) * sign;
            var slice = points.Where(v => Mathf.Abs(v[along] - coordinate) < length * .012f).ToArray();
            if (slice.Length == 0) throw new InvalidOperationException("Cannot locate sword geometry calibration section.");
            Vector3 lo = slice[0], hi = slice[0];
            foreach (var v in slice) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            Vector3 center = (lo + hi) * .5f;
            center[along] = coordinate;
            return center;
        }
        Vector3 grip = At((.0761507f + .025f) / .683531f);
        Vector3 basePoint = At((.0761507f + .145f) / .683531f);
        Vector3 tip = At(.997f);
        Vector3 across = Vector3.zero; across[broad] = .10f;
        return new WeaponCalibration { useNamedMarkers = false, sourceMeshName = "RoninSword",
            sourceGrip = grip, sourceBladeBase = basePoint, sourceBladeTip = tip, sourceBroadPoint = grip + across,
            bladeRadius = .026f };
    }

    static int LargestAxis(Vector3 v) => v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;
    static int LargestOtherAxis(Vector3 v, int exclude) { v[exclude] = -1f; return LargestAxis(v); }
    static Transform Bone(Animator animator, HumanBodyBones bone, string name) =>
        animator.isHuman ? animator.GetBoneTransform(bone) : animator.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name);
    static Vector3 Point(Transform root, string name) => root.InverseTransformPoint(
        root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name)?.position
        ?? throw new InvalidOperationException("Sword marker missing: " + name));
    static Transform NewPoint(Transform parent, string name, Vector3 local)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false); t.localPosition = local; return t;
    }
}
