using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public static class AholoWeaponPreview
{
    const string Output = "Verification/Aholo20261116/Weapons";
    [Serializable] sealed class Report { public bool wroteScene; public string status; public List<RoleReport> roles = new(); }
    [Serializable] sealed class RoleReport
    {
        public string role, visual, sword;
        public WeaponCalibration calibration;
        public float bladeStart, bladeEnd, bladeLength, bladeRadius, gripError;
        public float flatNormalVelocityRmsRoll0, flatNormalVelocityRmsRoll90;
        public List<PoseReport> poses = new();
    }
    [Serializable] sealed class PoseReport
    {
        public string clip;
        public float seconds;
        public Vector3 grip, bladeBase, bladeTip, forward, broad, rightHand, socketLocal;
    }

    [MenuItem("FengChenInn/Aholo 20261116/Review Independent Sword Bindings")]
    public static void Review()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        Directory.CreateDirectory(Output);
        var plan = AholoCombatSceneInstall.ReadPlan();
        var report = new Report { wroteScene = false, status = "Real static hand attachments and animated-pose renders; not gameplay contact acceptance." };
        foreach (var pair in new[] { ("Hongfu", plan.player), ("Grunt", plan.grunt) })
            report.roles.Add(ReviewRole(pair.Item1, pair.Item2));
        File.WriteAllText(Output + "/WeaponCalibrationReview.json", JsonUtility.ToJson(report, true));
        Debug.Log("AHOLO_WEAPON_REVIEW_READY: two real character/sword pairs rendered in isolated previews; no scene saved.");
    }

    static RoleReport ReviewRole(string role, AholoCombatSceneInstall.RolePlan row)
    {
        var visual = AssetDatabase.LoadAssetAtPath<GameObject>(row.visualPrefabPath);
        var motions = AssetDatabase.LoadAssetAtPath<AholoMotionSet>(row.motionSetPath);
        var sword = AssetDatabase.LoadAssetAtPath<GameObject>(row.swordAssetPath);
        if (visual == null || motions == null || sword == null) throw new FileNotFoundException(role + " resources missing.");
        WeaponCalibration calibration = row.weapon;
        if (row.weaponCalibrationMode == "MeasuredRonin")
        {
            calibration = AholoWeaponBinding.MeasuredRonin(sword);
            calibration.uniformSwordScale = row.weapon.uniformSwordScale;
            calibration.gripFraction = row.weapon.gripFraction;
            calibration.socketPositionOffsetMeters = row.weapon.socketPositionOffsetMeters;
            calibration.socketEulerOffset = row.weapon.socketEulerOffset;
            calibration.bladeRadius = row.weapon.bladeRadius;
        }
        var result = new RoleReport { role = role, visual = row.visualPrefabPath, sword = row.swordAssetPath, calibration = calibration };
        var preview = new PreviewRenderUtility();
        PlayableGraph graph = default;
        try
        {
            var body = new GameObject(role + "_SwordReview");
            preview.AddSingleGO(body);
            var model = Object.Instantiate(visual, body.transform);
            var animator = model.GetComponentInChildren<Animator>();
            animator.Rebind(); animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var actor = body.AddComponent<CombatActor>();
            actor.Configure(role == "Hongfu" ? CombatRole.Player : CombatRole.Grunt, CombatTeam.Player, 100);
            Transform socket = AholoWeaponBinding.Attach(actor, animator, sword, calibration);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            result.bladeStart = actor.BladeStart; result.bladeEnd = actor.BladeEnd;
            result.bladeLength = Vector3.Distance(actor.BladeBase.position, actor.BladeTip.position);
            result.bladeRadius = actor.BladeRadius;
            var originalMarker = socket.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GripCenter");
            result.gripError = originalMarker != null ? Vector3.Distance(originalMarker.position, socket.position) : 0f;
            double sum0 = 0, sum90 = 0, weight = 0;
            foreach (var definition in row.attacks)
            {
                var clip = motions.GetClip(definition.motion); if (clip == null) continue;
                OpenPoseGraph(animator, clip, ref graph, out var playable);
                foreach (var window in definition.hitWindows)
                    for (float t = window.start; t <= window.end + .0001f; t += 1f/30f)
                    {
                        SetTime(graph, playable, t);
                        Vector3 before = actor.BladeTip.position;
                        Vector3 normal0 = socket.up, normal90 = socket.right;
                        SetTime(graph, playable, Mathf.Min(clip.length, t + 1f/60f));
                        Vector3 velocity = actor.BladeTip.position - before;
                        if (velocity.sqrMagnitude < .0000001f) continue;
                        sum0 += Math.Pow(Vector3.Dot(normal0, velocity.normalized),2)*velocity.sqrMagnitude;
                        sum90 += Math.Pow(Vector3.Dot(normal90, velocity.normalized),2)*velocity.sqrMagnitude;
                        weight += velocity.sqrMagnitude;
                    }
                graph.Destroy(); graph = default;
            }
            result.flatNormalVelocityRmsRoll0 = weight > 0 ? (float)Math.Sqrt(sum0/weight) : 0;
            result.flatNormalVelocityRmsRoll90 = weight > 0 ? (float)Math.Sqrt(sum90/weight) : 0;
            foreach (var pose in new[] { ("Idle", motions.idle, motions.idle != null ? motions.idle.length * .4f : 0f),
                ("Light", motions.light, role == "Hongfu" ? .4166667f : .215f) })
            {
                if (pose.Item2 == null) continue;
                OpenPoseGraph(animator, pose.Item2, ref graph, out var playable);
                SetTime(graph, playable, pose.Item3);
                // Offscreen render calls in one Editor tick can reuse stale GPU
                // skinning buffers. Bake ONLY these transient preview renderers
                // from the current bone pose; never alter the delivered skin asset.
                var baked = BakePreviewPose(body);
                result.poses.Add(new PoseReport
                {
                    clip = pose.Item2.name, seconds = pose.Item3,
                    grip = body.transform.InverseTransformPoint(socket.position),
                    rightHand = body.transform.InverseTransformPoint(hand.position), socketLocal = socket.localPosition,
                    bladeBase = body.transform.InverseTransformPoint(actor.BladeBase.position),
                    bladeTip = body.transform.InverseTransformPoint(actor.BladeTip.position),
                    forward = body.transform.InverseTransformDirection(socket.forward), broad = body.transform.InverseTransformDirection(socket.right)
                });
                Bounds bounds = body.GetComponentsInChildren<Renderer>().First(r=>r.enabled).bounds;
                foreach (var renderer in body.GetComponentsInChildren<Renderer>().Where(r=>r.enabled)) bounds.Encapsulate(renderer.bounds);
                Render(preview, bounds.center, Mathf.Max(bounds.size.y, bounds.size.x) * 2.4f,
                    new Vector3(.48f,.12f,1f), Output + "/" + role + "_" + pose.Item1 + "_Full.png");
                Render(preview, Vector3.Lerp(hand.position, socket.position, .7f), 1.0f,
                    new Vector3(.7f,.25f,1f), Output + "/" + role + "_" + pose.Item1 + "_Grip.png");
                Render(preview, Vector3.Lerp(socket.position, actor.BladeTip.position, .4f), 2.25f,
                    new Vector3(1f,.15f,.1f), Output + "/" + role + "_" + pose.Item1 + "_Side.png");
                foreach(var item in baked) { Object.DestroyImmediate(item.mesh); Object.DestroyImmediate(item.go); item.renderer.enabled = true; }
                graph.Destroy(); graph = default;
            }
        }
        finally { if (graph.IsValid()) graph.Destroy(); preview.Cleanup(); }
        return result;
    }
    static List<(GameObject go, Mesh mesh, Renderer renderer)> BakePreviewPose(GameObject body)
    {
        var result = new List<(GameObject, Mesh, Renderer)>();
        foreach(var renderer in body.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh;
            if(renderer is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh, false); }
            else if(renderer is MeshRenderer && renderer.GetComponent<MeshFilter>()?.sharedMesh != null)
                mesh = Object.Instantiate(renderer.GetComponent<MeshFilter>().sharedMesh);
            else continue;
            Matrix4x4 matrix = body.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            mesh.vertices = mesh.vertices.Select(v=>matrix.MultiplyPoint3x4(v)).ToArray();
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            mesh.normals = mesh.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
            mesh.RecalculateBounds();
            var go = new GameObject("TransientBakedPose"); go.transform.SetParent(body.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            renderer.enabled = false; result.Add((go, mesh, renderer));
        }
        return result;
    }
    static void OpenPoseGraph(Animator animator, AnimationClip clip, ref PlayableGraph graph, out AnimationClipPlayable playable)
    {
        if (graph.IsValid()) graph.Destroy();
        animator.Rebind();
        graph = PlayableGraph.Create("WeaponStaticPoseReview"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(playable);
        graph.Play(); playable.SetSpeed(0);
    }
    static void SetTime(PlayableGraph graph, AnimationClipPlayable clip, float seconds) { clip.SetTime(seconds); graph.Evaluate(0); }
    static void Render(PreviewRenderUtility preview, Vector3 center, float distance, Vector3 viewDirection, string path)
    {
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(.20f,.22f,.24f,1f);
        preview.camera.fieldOfView = 30f; preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100f;
        preview.camera.transform.position = center + viewDirection.normalized * distance; preview.camera.transform.LookAt(center);
        preview.lights[0].intensity = 2.2f; preview.lights[0].transform.rotation = Quaternion.Euler(35,-40,0);
        preview.lights[1].intensity = 1.3f; preview.lights[1].transform.rotation = Quaternion.Euler(25,140,0);
        preview.ambientColor = new Color(.5f,.5f,.5f,1f);
        Texture2D texture = null;
        try { preview.BeginStaticPreview(new Rect(0,0,1100,1100)); preview.Render(true); texture = preview.EndStaticPreview(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { if(texture != null) Object.DestroyImmediate(texture); }
    }
}
