using System;
using System.Collections.Generic;
using UnityEngine;

namespace FengChenInn
{
    public sealed class DuelEncounter : MonoBehaviour
    {
        public enum Phase { Waiting, Approach, Windup, Slash, Recover, Stagger, Victory, Defeat }
        public enum AttackKind { HighSlash, LowSweep }
        public enum EncounterStage { Arrival, Tutorial, EliteReady, EliteDuel, EliteVictory, Rescue, Complete, Failed }
        public const int PlayerMax = 100, OpponentMax = 90;
        public const float WindupTime = .58f, SlashTime = .20f, RecoveryTime = .68f;
        // Legacy serialized reference only. No procedural animation is invoked.
        public RocketboxVisual opponentVisualPrefab;
        [Header("Animated character prefabs")]
        public CombatActor eliteActorPrefab, gruntActorPrefab;
        public GameObject eliteVisualPrefab, gruntVisualPrefab;
        public AholoMotionSet eliteMotions, gruntMotions;
        [Header("Encounter balance")]
        [Min(1)] public int eliteMaxHealth = 180, gruntMaxHealth = 55;
        [Min(1)] public int RescueEnemyCount = 2;
        [Range(.05f, 1f)] public float eliteAllyRestoreFraction = .5f;
        public Vector3 tutorialPosition = new(.75f, .05f, -1.3f);
        public Vector3 eliteArrivalPosition = new(.65f, .05f, -3.5f);
        public Vector3 eliteGuardPosition = new(0f, .05f, -3.0f);
        public Vector3 rescueArrivalPosition = new(0f, .05f, -5.8f);
        public CombatAttack[] eliteAttacks, gruntAttacks;
        public EncounterStage Stage { get; private set; } = EncounterStage.Arrival;
        public bool EliteVictoryAchieved { get; private set; }
        public CombatActor PlayerActor { get; private set; }
        public CombatActor EliteActor { get; private set; }
        public CombatActor TutorialActor { get; private set; }
        public IReadOnlyList<CombatActor> EnemyActors => enemies;
        public event Action<EncounterStage> StageChanged;
        public event Action<CombatActor> ActorSpawned;
        public bool IsCombatActive => combatRunning && (Stage == EncounterStage.Tutorial || Stage == EncounterStage.EliteDuel || Stage == EncounterStage.Rescue);
        public bool HasRequiredBindings => BindingProblem(PlayerActor) == null && BindingProblem(TutorialActor) == null && BindingProblem(EliteActor) == null;
        public string MissingBindingReason => BindingProblem(PlayerActor) ?? BindingProblem(TutorialActor) ?? BindingProblem(EliteActor) ?? "";
        public int RemainingEnemies
        {
            get
            {
                int count = 0;
                foreach (var actor in enemies)
                    if (actor != null && actor.gameObject.activeInHierarchy && actor.Team == CombatTeam.Enemy && !actor.IsDefeated) count++;
                return count;
            }
        }
        public CombatActor ActiveEnemy
        {
            get
            {
                if (Stage == EncounterStage.Arrival || Stage == EncounterStage.Tutorial) return TutorialActor;
                if (Stage != EncounterStage.Rescue && Stage != EncounterStage.Complete) return EliteActor;
                CombatActor nearest = null;
                float best = float.PositiveInfinity;
                foreach (var actor in enemies)
                {
                    if (actor == null || actor.IsDefeated || actor.Team != CombatTeam.Enemy || !actor.gameObject.activeInHierarchy) continue;
                    float distance = PlayerActor != null ? (actor.transform.position - PlayerActor.transform.position).sqrMagnitude : 0f;
                    if (distance < best) { nearest = actor; best = distance; }
                }
                return nearest;
            }
        }
        public int PlayerHealth => PlayerActor != null ? PlayerActor.Health : PlayerMax;
        public int OpponentHealth => ActiveEnemy != null ? ActiveEnemy.Health : 0;
        public Transform Opponent => ActiveEnemy != null ? ActiveEnemy.transform : EliteActor != null ? EliteActor.transform : null;
        public Collider WeaponBlocker => null;
        public string Feedback { get; private set; } = "";
        public AttackKind CurrentAttack => AttackKind.HighSlash;
        public float TellDuration => ActiveEnemy?.CurrentAttack?.activeStart ?? WindupTime;
        public float TellProgress => ActiveEnemy != null && ActiveEnemy.IsAttacking ? Mathf.Clamp01(ActiveEnemy.AttackElapsed / Mathf.Max(.01f, TellDuration)) : 0f;
        public float StrikeProgress => ActiveEnemy != null && ActiveEnemy.IsAttacking ? Mathf.Clamp01((ActiveEnemy.AttackElapsed - TellDuration) / Mathf.Max(.01f, ActiveEnemy.CurrentAttack.activeEnd - TellDuration)) : 0f;
        public Phase State
        {
            get
            {
                if (Stage == EncounterStage.Failed) return Phase.Defeat;
                if (Stage == EncounterStage.EliteVictory || Stage == EncounterStage.Complete) return Phase.Victory;
                var ai = ActiveEnemy != null ? ActiveEnemy.GetComponent<EnemyCombatAI>() : null;
                if (!combatRunning || ai == null) return Phase.Waiting;
                return ai.State switch
                {
                    EnemyCombatAI.Behaviour.Windup => Phase.Windup,
                    EnemyCombatAI.Behaviour.Strike => Phase.Slash,
                    EnemyCombatAI.Behaviour.Recovery => Phase.Recover,
                    EnemyCombatAI.Behaviour.Stagger => Phase.Stagger,
                    _ => Phase.Approach
                };
            }
        }
        readonly List<CombatActor> spawned = new();
        readonly List<CombatActor> enemies = new();
        bool initialized, combatRunning, eliteRestored;
        float feedbackUntil;
        float entranceZ = float.NaN;
        Transform spawnStaging;
        void Start() => Initialize();
        void Update() { if (Time.time >= feedbackUntil) Feedback = ""; }
        void Initialize()
        {
            if (initialized) return;
            initialized = true;
            var entrance = FindFirstObjectByType<InnEntranceDoors>();
            if (entrance != null) entranceZ = entrance.transform.position.z;
            PlayerActor = GetComponent<CombatActor>();
            if (PlayerActor == null) PlayerActor = gameObject.AddComponent<CombatActor>();
            PlayerActor.Configure(CombatRole.Player, CombatTeam.Player, Mathf.Max(PlayerMax, PlayerActor.MaxHealth));
            PlayerActor.DisplayName = "红拂女";
            PlayerActor.Defeated += PlayerDefeated;
            PlayerActor.Damaged += PlayerDamaged;
            PlayerActor.Blocked += PlayerBlocked;
            PlayerActor.HitLanded += PlayerLanded;
            ResetEncounter();
        }
        public void ResetEncounter()
        {
            if (!initialized) { Initialize(); return; }
            StopCombat();
            foreach (var actor in spawned)
            {
                if (actor == null) continue;
                actor.gameObject.SetActive(false);
                Destroy(actor.gameObject);
            }
            spawned.Clear(); enemies.Clear();
            PlayerActor.ResetActor();
            PlayerActor.SetTeam(CombatTeam.Player);
            PlayerActor.ControlLocked = false;
            EliteVictoryAchieved = eliteRestored = false;
            Feedback = "";
            TutorialActor = SpawnInactive(CombatRole.Grunt, tutorialPosition, "驻守小兵");
            TryRevealActor(TutorialActor, tutorialPosition);
            EliteActor = SpawnInactive(CombatRole.Elite, eliteArrivalPosition, "追兵");
            Stage = EncounterStage.Arrival;
            StageChanged?.Invoke(Stage);
        }
        public bool BeginTutorial()
        {
            Initialize();
            if (Stage != EncounterStage.Arrival || TutorialActor == null || PlayerActor.IsDefeated) return false;
            if (!CheckBindings()) return false;
            if (!TutorialActor.gameObject.activeSelf && !TryRevealActor(TutorialActor, tutorialPosition)) return false;
            StartCombat(EncounterStage.Tutorial, TutorialActor);
            return true;
        }
        public bool BeginEliteDuel()
        {
            Initialize();
            if (Stage != EncounterStage.EliteReady || EliteActor == null || PlayerActor.IsDefeated) return false;
            if (!CheckBindings()) return false;
            if (!EliteActor.gameObject.activeSelf && !TryRevealActor(EliteActor, eliteArrivalPosition)) return false;
            StartCombat(EncounterStage.EliteDuel, EliteActor);
            return true;
        }
        public bool MakeEliteAlly()
        {
            if (!EliteVictoryAchieved || Stage != EncounterStage.EliteVictory || EliteActor == null) return false;
            if (!eliteRestored)
            {
                var controller = EliteActor.GetComponent<CharacterController>();
                var facing = Quaternion.Euler(0f, 180f, 0f);
                if (!ActorPlacement.TryPlace(controller, eliteGuardPosition, facing, entranceZ)
                    && !ActorPlacement.TryPlace(controller, EliteActor.transform.position, facing, entranceZ))
                {
                    Show("追兵站位被挡住");
                    return false;
                }
                EliteActor.ResetActor(Mathf.Max(1, Mathf.RoundToInt(eliteMaxHealth * eliteAllyRestoreFraction)));
                eliteRestored = true;
            }
            EliteActor.SetTeam(CombatTeam.Player);
            EliteActor.ControlLocked = true;
            var brain = EliteActor.GetComponent<EnemyCombatAI>();
            if (brain != null) { brain.SetCombatEnabled(false); brain.ClearTarget(); }
            if (EliteActor.Animation != null && EliteActor.Animation.Ready && EliteActor.Animation.HasMotion(CombatMotion.Idle))
                EliteActor.Animation.Play(CombatMotion.Idle, .2f);
            return true;
        }
        public bool BeginRescueBattle()
        {
            if (PlayerActor == null || PlayerActor.IsDefeated || !EliteVictoryAchieved || Stage != EncounterStage.EliteVictory) return false;
            if (!CheckBindings() || !MakeEliteAlly()) return false;
            var reinforcements = new List<CombatActor>();
            for (int i = 0; i < Mathf.Max(1, RescueEnemyCount); i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Vector3 point = rescueArrivalPosition + new Vector3(side * (.55f + (i / 2) * .6f), 0f, -(i / 2) * .8f);
                var reinforcement = SpawnInactive(CombatRole.Grunt, point, "后续小兵 " + (i + 1));
                reinforcements.Add(reinforcement);
                if (!TryRevealActor(reinforcement, point))
                {
                    foreach (var staged in reinforcements)
                    {
                        staged.gameObject.SetActive(false);
                        spawned.Remove(staged);
                        enemies.Remove(staged);
                        Destroy(staged.gameObject);
                    }
                    return false;
                }
            }
            enemies.Clear();
            enemies.AddRange(reinforcements);
            foreach (var reinforcement in reinforcements)
            {
                reinforcement.ControlLocked = false;
                var brain = reinforcement.GetComponent<EnemyCombatAI>();
                brain.SetCombatEnabled(true);
                brain.PreferOpeningTarget(EliteActor);
            }
            combatRunning = true;
            PlayerActor.ControlLocked = false;
            EliteActor.ControlLocked = false;
            EliteActor.GetComponent<EnemyCombatAI>().SetCombatEnabled(true);
            SetStage(EncounterStage.Rescue);
            return true;
        }
        bool CheckBindings()
        {
            string problem = MissingBindingReason;
            if (problem.Length == 0) return true;
            Show("动画尚未接入：" + problem);
            Debug.LogWarning("Combat cannot start until Aholo bindings are installed: " + problem, this);
            return false;
        }
        static string BindingProblem(CombatActor actor)
        {
            if (actor == null) return "角色尚未建立";
            actor.RefreshBindings();
            var animation = actor.Animation;
            if (animation == null || animation.motionSet == null || animation.targetAnimator == null ||
                (actor.gameObject.activeInHierarchy && !animation.Ready)) return actor.DisplayName + "缺少动画驱动或模型绑定";
            if (!actor.HasWeapon) return actor.DisplayName + "缺少剑的挂接点";
            foreach (CombatMotion motion in new[] { CombatMotion.Idle, CombatMotion.Walk, CombatMotion.Light, CombatMotion.Heavy })
                if (!animation.HasMotion(motion)) return actor.DisplayName + "缺少" + motion + "片段";
            return null;
        }
        public void StopCombat()
        {
            combatRunning = false;
            if (PlayerActor != null) PlayerActor.CancelAttack();
            foreach (var actor in spawned)
            {
                if (actor == null) continue;
                actor.CancelAttack();
                actor.ControlLocked = true;
                actor.GetComponent<EnemyCombatAI>()?.SetCombatEnabled(false);
            }
        }
        void StartCombat(EncounterStage stage, CombatActor opponent)
        {
            StopCombat(); enemies.Clear(); enemies.Add(opponent);
            combatRunning = true;
            PlayerActor.ControlLocked = false;
            opponent.ControlLocked = false;
            opponent.GetComponent<EnemyCombatAI>().SetCombatEnabled(true);
            SetStage(stage);
        }
        bool TryRevealActor(CombatActor actor, Vector3 position)
        {
            if (!ActorPlacement.TryPlace(actor.GetComponent<CharacterController>(), position, actor.transform.rotation, entranceZ))
            {
                Show(actor.DisplayName + "的站位被挡住");
                return false;
            }
            actor.gameObject.SetActive(true);
            return true;
        }

        CombatActor SpawnInactive(CombatRole role, Vector3 position, string displayName)
        {
            bool elite = role == CombatRole.Elite;
            CombatActor prefab = elite ? eliteActorPrefab : gruntActorPrefab;
            AholoMotionSet motions = elite ? eliteMotions : gruntMotions;
            CombatActor actor;
            var facing = Quaternion.Euler(0f, 180f, 0f);
            if (prefab != null)
            {
                if (spawnStaging == null)
                {
                    var staging = new GameObject("待落位角色");
                    staging.SetActive(false);
                    spawnStaging = staging.transform;
                    spawnStaging.SetParent(transform, false);
                }
                actor = Instantiate(prefab, position, facing, spawnStaging);
                actor.gameObject.SetActive(false);
                actor.transform.SetParent(null, true);
            }
            else
            {
                var body = new GameObject(displayName);
                body.SetActive(false);
                body.transform.SetPositionAndRotation(position, facing);
                var controller = body.AddComponent<CharacterController>();
                controller.height = 1.8f; controller.radius = .36f;
                controller.center = Vector3.up * .9f; controller.stepOffset = .25f;
                actor = body.AddComponent<CombatActor>();
                GameObject visualPrefab = elite ? eliteVisualPrefab : gruntVisualPrefab;
                if (visualPrefab == null && motions != null) visualPrefab = motions.visualPrefab;
                if (visualPrefab != null) Instantiate(visualPrefab, body.transform);
            }
            actor.name = actor.DisplayName = displayName;
            actor.Configure(role, CombatTeam.Enemy, elite ? eliteMaxHealth : gruntMaxHealth);
            var driver = actor.GetComponentInChildren<AholoAnimationDriver>(true);
            if (driver == null) driver = actor.gameObject.AddComponent<AholoAnimationDriver>();
            if (motions != null) driver.Configure(motions, actor.GetComponentInChildren<Animator>(true));
            foreach (var old in actor.GetComponentsInChildren<RocketboxVisual>(true)) old.enabled = false;
            actor.RefreshBindings();
            var brain = actor.GetComponent<EnemyCombatAI>();
            if (brain == null) brain = actor.gameObject.AddComponent<EnemyCombatAI>();
            brain.moveSpeed = elite ? 2.35f : 1.85f;
            brain.attackInterval = elite ? .55f : .95f;
            brain.attacks = CloneAttacks(elite ? eliteAttacks : gruntAttacks, elite, driver);
            brain.ResetBrain();
            actor.Defeated += EnemyDefeated;
            actor.ControlLocked = true;
            spawned.Add(actor); enemies.Add(actor);
            ActorSpawned?.Invoke(actor);
            if (BindingProblem(actor) != null)
                Debug.LogWarning(displayName + ": Aholo animation or sword anchor is not configured; attacks remain unavailable.", actor);
            return actor;
        }
        static CombatAttack[] CloneAttacks(CombatAttack[] configured, bool elite, AholoAnimationDriver driver)
        {
            if (configured == null || configured.Length == 0)
                configured = elite
                    ? new[] { DefaultAttack(CombatMotion.Light, "横砍", 17), DefaultAttack(CombatMotion.Heavy, "重斩", 24), DefaultAttack(CombatMotion.Skill, "剑技", 28) }
                    : new[] { DefaultAttack(CombatMotion.Light, "横砍", 10), DefaultAttack(CombatMotion.Heavy, "斜劈", 13) };
            var copy = new List<CombatAttack>();
            foreach (var definition in configured)
            {
                if (definition == null) continue;
                CombatAttack attack = JsonUtility.FromJson<CombatAttack>(JsonUtility.ToJson(definition));
                if (driver != null && driver.HasMotion(attack.motion))
                    attack.duration = driver.MotionLength(attack.motion) / Mathf.Max(.05f, attack.speed);
                attack.activeStart = Mathf.Min(attack.activeStart, attack.duration * .65f);
                attack.activeEnd = Mathf.Clamp(attack.activeEnd, attack.activeStart + .02f, attack.duration);
                copy.Add(attack);
            }
            return copy.ToArray();
        }
        static CombatAttack DefaultAttack(CombatMotion motion, string label, int damage) => new()
        {
            label = label, motion = motion, damage = damage, duration = motion == CombatMotion.Light ? 1f : 1.5f,
            activeStart = motion == CombatMotion.Light ? .3f : .55f,
            activeEnd = motion == CombatMotion.Light ? .55f : .9f,
            range = 1.9f, knockback = motion == CombatMotion.Light ? .14f : .28f
        };
        void EnemyDefeated(CombatActor actor)
        {
            actor.GetComponent<EnemyCombatAI>()?.SetCombatEnabled(false);
            if (Stage == EncounterStage.Tutorial && actor == TutorialActor)
            {
                StopCombat();
                TryRevealActor(EliteActor, eliteArrivalPosition);
                SetStage(EncounterStage.EliteReady);
            }
            else if (Stage == EncounterStage.EliteDuel && actor == EliteActor)
            {
                EliteVictoryAchieved = true;
                StopCombat();
                SetStage(EncounterStage.EliteVictory);
            }
            else if (Stage == EncounterStage.Rescue && actor.Team == CombatTeam.Enemy && RemainingEnemies == 0)
            {
                StopCombat();
                SetStage(EncounterStage.Complete);
            }
            // Allied elite defeat stops its fighting; only player defeat fails the rescue.
        }
        void PlayerDefeated(CombatActor actor)
        {
            if (!IsCombatActive) return;
            StopCombat(); SetStage(EncounterStage.Failed);
        }
        void PlayerDamaged(CombatActor source, int amount) => Show("PLAYER HIT");
        void PlayerBlocked(CombatActor source) => Show("BLOCKED");
        void PlayerLanded(CombatActor target, int amount) => Show("LIGHT HIT");
        void SetStage(EncounterStage value) { Stage = value; StageChanged?.Invoke(value); }
        void Show(string value) { Feedback = value; feedbackUntil = Time.time + .9f; }
        public void Begin()
        {
            if (Stage == EncounterStage.Arrival) BeginTutorial();
            else if (Stage == EncounterStage.EliteReady) BeginEliteDuel();
        }
        public void SetWaitingSeat(Vector3 position) { }
        public void PlaceForStory(Vector3 position, Vector3 lookAt, RocketboxVisual.StoryPose stance, bool swordDrawn = true)
        {
            CombatActor actor = ActiveEnemy != null ? ActiveEnemy : EliteActor;
            if (actor == null) return;
            actor.CancelAttack();
            var motor = actor.GetComponent<CharacterController>();
            Vector3 direction = Vector3.ProjectOnPlane(lookAt - position, Vector3.up);
            Quaternion facing = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : actor.transform.rotation;
            ActorPlacement.TryPlace(motor, position, facing, entranceZ);
        }
        void OnDestroy()
        {
            if (PlayerActor != null)
            {
                PlayerActor.Defeated -= PlayerDefeated;
                PlayerActor.Damaged -= PlayerDamaged;
                PlayerActor.Blocked -= PlayerBlocked;
                PlayerActor.HitLanded -= PlayerLanded;
            }
            foreach (var actor in spawned) if (actor != null) Destroy(actor.gameObject);
        }
    }
}
