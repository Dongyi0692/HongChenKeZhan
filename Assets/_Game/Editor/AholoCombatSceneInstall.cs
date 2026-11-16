using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit, preflight-gated installation into the EXISTING InnDuel scene.
// No InitializeOnLoad, RuntimeInitializeOnLoad, old installer, or scene creation.
public static class AholoCombatSceneInstall
{
    public const string ScenePath = "Assets/_Game/Scenes/InnDuel.unity";
    public const string PlanPath = "Assets/_Game/Art/Aholo20261116/Runtime/CombatInstallPlan.json";
    public const string OutputFolder = "Assets/_Game/Art/Aholo20261116/Runtime/Actors";
    public const string ReportPath = "Verification/Aholo20261116/CombatInstallPreflight.json";

    [Serializable] public sealed class InstallPlan
    {
        public RolePlan player, elite, grunt;
        public int rescueEnemyCount = 2;
        public float eliteAllyRestoreFraction = .5f;
    }
    [Serializable] public sealed class RolePlan
    {
        public string visualPrefabPath, motionSetPath, swordAssetPath;
        // Markers uses explicit source markers; MeasuredRonin requires the known source geometry.
        public string weaponCalibrationMode = "Markers";
        public WeaponCalibration weapon = new WeaponCalibration();
        public int maxHealth = 100;
        public float controllerHeight = 1.8f, controllerRadius = .36f;
        public CombatAttack[] attacks = Array.Empty<CombatAttack>();
    }
    [Serializable] sealed class PreflightReport
    {
        public string checkedUtc, planPath = PlanPath, targetScene = ScenePath;
        public bool ready, installed, savedScene;
        public string backupDirectory;
        public string[] errors, warnings, generatedPrefabs;
    }
    sealed class ResolvedRole
    {
        public CombatRole role;
        public RolePlan plan;
        public GameObject visual, sword;
        public AholoMotionSet motions;
        public WeaponCalibration calibration;
    }
    sealed class SceneSnapshot
    {
        public readonly Dictionary<Object, string> protectedState = new();
        public readonly HashSet<int> protectedObjects = new();
        public readonly Vector3 playerPosition, playerScale;
        public readonly Quaternion playerRotation;
        public SceneSnapshot(Scene scene, Transform player)
        {
            playerPosition = player.position; playerRotation = player.rotation; playerScale = player.localScale;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (item == player || item.IsChildOf(player)) continue;
                    protectedObjects.Add(item.gameObject.GetInstanceID());
                    protectedState[item.gameObject] = EditorJsonUtility.ToJson(item.gameObject);
                    foreach (Component component in item.GetComponents<Component>())
                        if (component != null) protectedState[component] = EditorJsonUtility.ToJson(component);
                }
        }
        public void AssertUnchanged(Scene scene, Transform player)
        {
            if (player.position != playerPosition || player.rotation != playerRotation || player.localScale != playerScale)
                throw new InvalidOperationException("Player spawn transform changed unexpectedly.");
            var current = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => t != player && !t.IsChildOf(player)).Select(t => t.gameObject.GetInstanceID()).ToHashSet();
            if (!current.SetEquals(protectedObjects)) throw new InvalidOperationException("Non-player scene objects were added or removed.");
            foreach (var entry in protectedState)
                if (entry.Key == null || EditorJsonUtility.ToJson(entry.Key) != entry.Value)
                    throw new InvalidOperationException("Protected environment/camera/UI object changed: "
                        + (entry.Key == null ? "destroyed object" : entry.Key.name));
        }
    }

    [MenuItem("FengChenInn/Aholo 20261116/Validate Combat Install Plan")]
    public static void ValidateFromManifest() => Preflight(ReadPlan(), false);

    [MenuItem("FengChenInn/Aholo 20261116/Install Validated Combat Into Existing InnDuel")]
    public static void InstallFromManifest() => Install(ReadPlan());

    public static InstallPlan ReadPlan()
    {
        if (!File.Exists(PlanPath)) throw new FileNotFoundException("Fill the explicit three-role install plan first.", PlanPath);
        return JsonUtility.FromJson<InstallPlan>(File.ReadAllText(PlanPath))
            ?? throw new InvalidDataException("Cannot read combat install plan.");
    }

    public static void Install(InstallPlan plan)
    {
        // All role checks, real animation checks and weapon construction run before scene mutation.
        var roles = Preflight(plan, true);
        Scene scene = RequireEditableScene();
        var fighter = SceneComponents<PrototypeFighter>(scene).Single();
        var encounter = fighter.GetComponent<DuelEncounter>();
        var snapshot = new SceneSnapshot(scene, fighter.transform);
        string backup = "Backups/AholoCombatInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, "InnDuel.unity"));
        string[] prefabPaths = { OutputFolder + "/Elite_Actor.prefab", OutputFolder + "/Grunt_Actor.prefab" };
        var existed = prefabPaths.ToDictionary(p => p, File.Exists);
        foreach (string path in prefabPaths)
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));

        int undo = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Install validated Aholo combat actors");
        bool sceneSaved = false;
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            EnsureFolder(OutputFolder);
            CombatActor elite = SaveActorPrefab(roles[1], preview, prefabPaths[0]);
            CombatActor grunt = SaveActorPrefab(roles[2], preview, prefabPaths[1]);
            if (elite == null || grunt == null) throw new IOException("Actor prefab save did not produce both prefabs.");

            Undo.RegisterFullObjectHierarchyUndo(fighter.gameObject, "Replace character visuals and combat bindings");
            BindPlayer(fighter, roles[0]);
            encounter.eliteActorPrefab = elite;
            encounter.gruntActorPrefab = grunt;
            encounter.eliteVisualPrefab = roles[1].visual;
            encounter.gruntVisualPrefab = roles[2].visual;
            encounter.eliteMotions = roles[1].motions;
            encounter.gruntMotions = roles[2].motions;
            encounter.opponentVisualPrefab = null;
            encounter.eliteAttacks = CloneAttacks(roles[1]);
            encounter.gruntAttacks = CloneAttacks(roles[2]);
            encounter.eliteMaxHealth = roles[1].plan.maxHealth;
            encounter.gruntMaxHealth = roles[2].plan.maxHealth;
            encounter.RescueEnemyCount = Mathf.Max(1, plan.rescueEnemyCount);
            encounter.eliteAllyRestoreFraction = Mathf.Clamp(plan.eliteAllyRestoreFraction, .05f, 1f);
            EditorUtility.SetDirty(encounter);
            EditorUtility.SetDirty(fighter);
            snapshot.AssertUnchanged(scene, fighter.transform);
            // SaveAsPrefabAsset already wrote our two outputs. Do not globally save
            // unrelated assets that a teammate may currently have dirty in the Editor.
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Saving existing InnDuel failed.");
            sceneSaved = true;
            Undo.CollapseUndoOperations(undo);
        }
        catch
        {
            if (!sceneSaved)
            {
                Undo.RevertAllDownToGroup(undo);
                foreach (string path in prefabPaths)
                {
                    if (existed[path])
                    {
                        File.Copy(Path.Combine(backup, Path.GetFileName(path)), path, true);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    }
                    else if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                }
            }
            throw;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        WriteReport(new PreflightReport
        {
            checkedUtc = DateTime.UtcNow.ToString("o"), ready = true, installed = true, savedScene = true,
            backupDirectory = backup, generatedPrefabs = prefabPaths, errors = Array.Empty<string>(),
            warnings = MissingOptional(roles).ToArray()
        });
        Debug.Log("AHOLO_COMBAT_INSTALLED: existing InnDuel updated; player spawn, environment, doors, furniture, camera and HUD preserved.");
    }

    static ResolvedRole[] Preflight(InstallPlan plan, bool requireScene)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (EditorApplication.isPlayingOrWillChangePlaymode) errors.Add("Exit Play Mode before installing.");
        if (plan == null) errors.Add("Install plan is null.");
        var roles = new[]
        {
            Resolve(plan?.player, CombatRole.Player, errors),
            Resolve(plan?.elite, CombatRole.Elite, errors),
            Resolve(plan?.grunt, CombatRole.Grunt, errors)
        };
        if (requireScene)
        {
            try { RequireEditableScene(); }
            catch (Exception exception) { errors.Add(exception.Message); }
        }
        if (errors.Count == 0)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (ResolvedRole role in roles)
                {
                    GameObject body = null;
                    try
                    {
                        body = BuildActor(role, preview).gameObject;
                        var animator = body.GetComponentInChildren<Animator>(true);
                        if (animator == null || animator.avatar == null || !animator.avatar.isValid)
                            throw new InvalidDataException("Animator has no valid Avatar.");
                        foreach (CombatMotion motion in RequiredMotions(role.role))
                        {
                            AnimationClip clip = role.motions.GetClip(motion);
                            if (clip == null || clip.length <= .001f) throw new InvalidDataException("Missing usable " + motion + " clip.");
                            if (clip.legacy || clip.isHumanMotion != animator.avatar.isHuman)
                                throw new InvalidDataException(motion + " clip type does not match this Avatar.");
                            if (!clip.isHumanMotion)
                                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                                    if (binding.type == typeof(Transform) && binding.path.Length > 0 && animator.transform.Find(binding.path) == null)
                                        throw new InvalidDataException(motion + " curve path is missing: " + binding.path);
                        }
                        var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        if (skins.Length == 0 || skins.Any(s => s.sharedMesh == null || s.bones.Length == 0 || s.bones.Any(b => b == null)))
                            throw new InvalidDataException("Missing mesh or skin bone binding.");
                        if (skins.Any(s => s.sharedMaterials.Length != s.sharedMesh.subMeshCount || s.sharedMaterials.Any(m => m == null)))
                            throw new InvalidDataException("Incomplete material/submesh mapping.");
                        var actor = body.GetComponent<CombatActor>();
                        if (!actor.HasWeapon || actor.BladeBase == null || actor.BladeTip == null)
                            throw new InvalidDataException("Sword attachment/endpoints are incomplete.");
                        if (Vector3.Distance(actor.BladeBase.position, actor.BladeTip.position) < .15f)
                            throw new InvalidDataException("Sword blade is too short; verify the source calibration.");
                    }
                    catch (Exception exception) { errors.Add(role.role + ": " + exception.Message); }
                    finally { if (body != null) Object.DestroyImmediate(body); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        warnings.AddRange(MissingOptional(roles));
        WriteReport(new PreflightReport
        {
            checkedUtc = DateTime.UtcNow.ToString("o"), ready = errors.Count == 0, installed = false,
            savedScene = false, errors = errors.ToArray(), warnings = warnings.ToArray()
        });
        if (errors.Count > 0) throw new InvalidDataException("Combat install preflight failed; scene unchanged.\n" + string.Join("\n", errors));
        Debug.Log("AHOLO_COMBAT_PREFLIGHT_OK: three roles, their real clips, materials and sword endpoints checked. No scene saved.");
        return roles;
    }

    static ResolvedRole Resolve(RolePlan plan, CombatRole role, List<string> errors)
    {
        if (plan == null) { errors.Add(role + ": role plan missing."); return null; }
        var result = new ResolvedRole { role = role, plan = plan };
        result.visual = Load<GameObject>(plan.visualPrefabPath, role + " visual", errors);
        result.sword = Load<GameObject>(plan.swordAssetPath, role + " sword", errors);
        result.motions = Load<AholoMotionSet>(plan.motionSetPath, role + " motions", errors);
        if (plan.maxHealth < 1 || plan.controllerHeight < 1f || plan.controllerRadius <= 0f)
            errors.Add(role + ": invalid health or capsule dimensions.");
        if (result.motions != null)
        {
            foreach (CombatMotion motion in RequiredMotions(role))
                if (result.motions.GetClip(motion) == null) errors.Add(role + ": missing " + motion + ".");
            if (result.motions.light != null && result.motions.light == result.motions.heavy)
                errors.Add(role + ": light and heavy/diagonal must be distinct selected real attacks.");
            if (result.motions.visualPrefab != null && result.motions.visualPrefab != result.visual)
                errors.Add(role + ": motion set references a different visual prefab.");
            ValidateAttacks(result, errors);
        }
        try
        {
            if (result.sword != null)
            {
                if (plan.weaponCalibrationMode == "MeasuredRonin")
                {
                    result.calibration = AholoWeaponBinding.MeasuredRonin(result.sword);
                    if (plan.weapon != null)
                    {
                        result.calibration.uniformSwordScale = plan.weapon.uniformSwordScale;
                        result.calibration.gripFraction = plan.weapon.gripFraction;
                        result.calibration.socketPositionOffsetMeters = plan.weapon.socketPositionOffsetMeters;
                        result.calibration.socketEulerOffset = plan.weapon.socketEulerOffset;
                        result.calibration.bladeRadius = plan.weapon.bladeRadius;
                    }
                }
                else if (plan.weaponCalibrationMode == "Markers" || plan.weaponCalibrationMode == "Explicit")
                    result.calibration = plan.weapon ?? new WeaponCalibration();
                else errors.Add(role + ": unknown weapon calibration mode.");
            }
        }
        catch (Exception exception) { errors.Add(role + ": " + exception.Message); }
        return result;
    }

    static void ValidateAttacks(ResolvedRole role, List<string> errors)
    {
        CombatMotion[] expected = role.role == CombatRole.Grunt
            ? new[] { CombatMotion.Light, CombatMotion.Heavy }
            : new[] { CombatMotion.Light, CombatMotion.Heavy, CombatMotion.Skill };
        var definitions = role.plan.attacks ?? Array.Empty<CombatAttack>();
        if (definitions.Length != expected.Length) errors.Add(role.role + ": expected exactly " + expected.Length + " attack definitions.");
        foreach (CombatMotion motion in expected)
        {
            var matches = definitions.Where(a => a != null && a.motion == motion).ToArray();
            if (matches.Length != 1) { errors.Add(role.role + ": expected one " + motion + " attack definition."); continue; }
            CombatAttack attack = matches[0];
            AnimationClip clip = role.motions.GetClip(motion);
            if (clip == null) continue;
            float duration = clip.length / Mathf.Max(.05f, attack.speed);
            if (attack.damage <= 0 || attack.speed <= 0f || attack.range <= 0f) errors.Add(role.role + ": invalid " + motion + " damage/speed/range.");
            var windows = attack.hitWindows != null && attack.hitWindows.Length > 0 ? attack.hitWindows
                : new[] { new CombatHitWindow { start = attack.activeStart, end = attack.activeEnd } };
            if (windows.Any(w => w == null || w.start < 0f || w.end <= w.start || w.end > duration + .01f || w.damageMultiplier <= 0f))
                errors.Add(role.role + ": " + motion + " damage windows extend outside its real clip duration.");
        }
    }

    static IEnumerable<CombatMotion> RequiredMotions(CombatRole role)
    {
        yield return CombatMotion.ExplorationIdle;
        yield return CombatMotion.Idle; yield return CombatMotion.Walk;
        yield return CombatMotion.Light; yield return CombatMotion.Heavy;
        if (role != CombatRole.Grunt) yield return CombatMotion.Skill;
        if (role == CombatRole.Player)
        {
            yield return CombatMotion.Run; yield return CombatMotion.Guard;
            yield return CombatMotion.JumpStart; yield return CombatMotion.JumpLoop; yield return CombatMotion.JumpLand;
        }
    }
    static IEnumerable<string> MissingOptional(ResolvedRole[] roles)
    {
        foreach (var role in roles)
        {
            if (role?.motions == null) continue;
            foreach (CombatMotion motion in new[] { CombatMotion.Dodge, CombatMotion.Hit, CombatMotion.Knockback, CombatMotion.Death })
                if (role.motions.GetClip(motion) == null) yield return role.role + ": " + motion + " unavailable; no replacement animation is generated.";
        }
        yield return "Weapon appearance/contact and gameplay are left for user review; no automated acceptance run is requested.";
    }

    static CombatActor BuildActor(ResolvedRole role, Scene preview)
    {
        var body = EditorUtility.CreateGameObjectWithHideFlags(role.role + "_Actor", HideFlags.HideAndDontSave);
        SceneManager.MoveGameObjectToScene(body, preview);
        body.hideFlags = HideFlags.None;
        try
        {
            var controller = body.AddComponent<CharacterController>();
            controller.height = role.plan.controllerHeight; controller.radius = role.plan.controllerRadius;
            controller.center = Vector3.up * (controller.height * .5f);
            controller.stepOffset = .25f; controller.skinWidth = .03f; controller.minMoveDistance = .001f;
            var actor = body.AddComponent<CombatActor>();
            actor.Configure(role.role, role.role == CombatRole.Player ? CombatTeam.Player : CombatTeam.Enemy, role.plan.maxHealth);
            actor.DisplayName = role.role == CombatRole.Player ? "红拂女" : role.role == CombatRole.Elite ? "追兵" : "小兵";
            AttachVisualAndWeapon(body, role, actor);
            if (role.role != CombatRole.Player)
            {
                var ai = body.AddComponent<EnemyCombatAI>();
                // Keep encounter balance; the real jog clip is slowed to match this speed.
                ai.moveSpeed = role.role == CombatRole.Elite ? 2.35f : 1.85f;
                ai.attackInterval = role.role == CombatRole.Elite ? .55f : .95f;
                ai.attacks = CloneAttacks(role);
            }
            return actor;
        }
        catch { Object.DestroyImmediate(body); throw; }
    }
    static AholoAnimationDriver AttachVisualAndWeapon(GameObject body, ResolvedRole role, CombatActor actor, bool recordUndo = false)
    {
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(role.visual, body.transform);
        visual.name = "Aholo " + role.role + " Visual";
        if (recordUndo) Undo.RegisterCreatedObjectUndo(visual, "Install Aholo visual");
        foreach (var old in visual.GetComponentsInChildren<RocketboxVisual>(true)) old.enabled = false;
        foreach (var childDriver in visual.GetComponentsInChildren<AholoAnimationDriver>(true)) childDriver.enabled = false;
        var animators = visual.GetComponentsInChildren<Animator>(true);
        if (animators.Length != 1) throw new InvalidDataException("Visual prefab must contain exactly one Animator.");
        Animator animator = animators[0];
        animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var driver = body.GetComponent<AholoAnimationDriver>();
        if (driver == null) driver = recordUndo ? Undo.AddComponent<AholoAnimationDriver>(body) : body.AddComponent<AholoAnimationDriver>();
        driver.enabled = true; driver.motionSet = role.motions; driver.targetAnimator = animator;
        AholoWeaponBinding.Attach(actor, animator, role.sword, role.calibration);
        actor.RefreshBindings();
        return driver;
    }
    static CombatActor SaveActorPrefab(ResolvedRole role, Scene preview, string path)
    {
        CombatActor actor = BuildActor(role, preview);
        try { return PrefabUtility.SaveAsPrefabAsset(actor.gameObject, path)?.GetComponent<CombatActor>(); }
        finally { Object.DestroyImmediate(actor.gameObject); }
    }
    static void BindPlayer(PrototypeFighter fighter, ResolvedRole role)
    {
        foreach (Transform child in fighter.transform.Cast<Transform>().ToArray())
        {
            if (child.name == "Aholo Player Visual" || child.GetComponentInChildren<Renderer>(true) != null
                || child.GetComponentInChildren<RocketboxVisual>(true) != null)
                Undo.DestroyObjectImmediate(child.gameObject);
        }
        foreach (var old in fighter.GetComponentsInChildren<RocketboxVisual>(true)) old.enabled = false;
        var actor = fighter.GetComponent<CombatActor>();
        if (actor == null) actor = Undo.AddComponent<CombatActor>(fighter.gameObject);
        actor.Configure(CombatRole.Player, CombatTeam.Player, role.plan.maxHealth);
        actor.DisplayName = "红拂女";
        var driver = AttachVisualAndWeapon(fighter.gameObject, role, actor, true);
        fighter.animationDriver = driver;
        fighter.visualRoot = fighter.transform.Find("Aholo Player Visual");
        fighter.skinnedVisual = null;
        fighter.leftArm = fighter.rightArm = fighter.leftLeg = fighter.rightLeg = null;
        fighter.walkSpeed = role.motions.walkReferenceSpeed;
        fighter.moveSpeed = role.motions.runReferenceSpeed;
        fighter.useFullClipDurations = true;
        CombatAttack[] attacks = CloneAttacks(role);
        fighter.lightAttack = attacks.Single(a => a.motion == CombatMotion.Light);
        fighter.heavyAttack = attacks.Single(a => a.motion == CombatMotion.Heavy);
        fighter.skillAttack = attacks.Single(a => a.motion == CombatMotion.Skill);
        fighter.RefreshBindings();
        EditorUtility.SetDirty(actor); EditorUtility.SetDirty(driver);
    }
    static CombatAttack[] CloneAttacks(ResolvedRole role)
    {
        return role.plan.attacks.Select(a =>
        {
            var result = JsonUtility.FromJson<CombatAttack>(JsonUtility.ToJson(a));
            result.duration = role.motions.GetClip(result.motion).length / Mathf.Max(.05f, result.speed);
            return result;
        }).ToArray();
    }
    static Scene RequireEditableScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open existing InnDuel first; installer does not open/create scenes.");
        if (scene.isDirty) throw new InvalidOperationException("InnDuel has unsaved edits; save or resolve them before installation.");
        var fighters = SceneComponents<PrototypeFighter>(scene).ToArray();
        if (fighters.Length != 1) throw new InvalidDataException("Expected exactly one existing PrototypeFighter.");
        if (fighters[0].GetComponent<DuelEncounter>() == null || fighters[0].GetComponent<PrototypeInput>() == null)
            throw new InvalidDataException("Existing player has no encounter/input binding.");
        if (!SceneComponents<ActionHud>(scene).Any(h => h.fighter == fighters[0]))
            throw new InvalidDataException("Existing ActionHud is not connected to this player.");
        return scene;
    }
    static IEnumerable<T> SceneComponents<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true));
    static T Load<T>(string path, string label, List<string> errors) where T : Object
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
        { errors.Add(label + ": explicit Assets/ path required."); return null; }
        T result = AssetDatabase.LoadAssetAtPath<T>(path);
        if (result == null) errors.Add(label + ": asset not found at " + path);
        return result;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static void WriteReport(PreflightReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
}
