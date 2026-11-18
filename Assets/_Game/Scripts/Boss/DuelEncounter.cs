using System;
using System.Collections.Generic;
using UnityEngine;

namespace FengChenInn
{
    public sealed class DuelEncounter : MonoBehaviour
    {
        public enum Phase { Waiting, Approach, Windup, Slash, Recover, Stagger, Victory, Defeat }
        public enum AttackKind { HighSlash, LowSweep }
        public enum EncounterStage { Arrival, Tutorial, EliteReady, EliteDuel, EliteVictory, Rescue, Complete, Failed, RescueCover }
        public const int PlayerMax = 100, OpponentMax = 90;
        public const float WindupTime = .58f, SlashTime = .20f, RecoveryTime = .68f;
        // Legacy serialized reference only. No procedural animation is invoked.
        public RocketboxVisual opponentVisualPrefab;
        [Header("Animated character prefabs")]
        public CombatActor eliteActorPrefab, gruntActorPrefab;
        public GameObject eliteVisualPrefab, gruntVisualPrefab;
        public AholoMotionSet eliteMotions, gruntMotions;
        [Header("Encounter balance")]
        [Min(.1f)] public float healthMultiplier = 1.5f;
        [Min(1)] public int eliteMaxHealth = 180, gruntMaxHealth = 55;
        [Min(1)] public int RescueEnemyCount = 5;
        [Min(1)] public int eliteAllyMaxHealth = 600;
        [Min(1)] public int eliteAllyMinimumHealth = 1;
        // Retained for old serialized scenes; allies now receive their own health budget.
        [HideInInspector]
        [Range(.05f, 1f)] public float eliteAllyRestoreFraction = .5f;
        public Vector3 tutorialPosition = new(.75f, .05f, -1.3f);
        public Vector3 eliteArrivalPosition = new(.65f, .05f, -3.5f);
        public Vector3 eliteGuardPosition = new(0f, .05f, -3.0f);
        public Vector3 rescueArrivalPosition = new(0f, .05f, -5.8f);
        [Tooltip("Optional exact reinforcement positions. Unassigned entries use the arrival formation.")]
        public Transform[] rescueSpawnAnchors = Array.Empty<Transform>();
        public CombatAttack[] eliteAttacks, gruntAttacks;
        [Header("Reinforcements through the front door")]
        [Min(.5f)] public float reinforcementEntranceSpeed = 2.2f;
        public bool RescueEntranceInProgress { get; private set; }
        public int RescueEnteredCount { get; private set; }
        [Header("Elite entrance")]
        [Min(.1f)] public float eliteEntranceSpeed = 1.7f;
        public bool EliteEntranceInProgress { get; private set; }
        public bool EliteEntranceComplete { get; private set; }
        public bool EliteEntranceStoppedAtDoor { get; private set; }
        public bool EliteEntranceReadyForDialogue => EliteEntranceComplete && Time.time >= eliteEntranceReadyAt;
        public EncounterStage Stage { get; private set; } = EncounterStage.Arrival;
        public EncounterStage RetryStage { get; private set; } = EncounterStage.Arrival;
        public bool CanRetryCurrentBattle => checkpoint.Count > 1 && IsBattleStage(RetryStage)
            && (Stage == EncounterStage.Failed || IsCombatActive && Stage == RetryStage);
        public bool EliteVictoryAchieved { get; private set; }
        public bool TutorialSentryRevealed { get; private set; }
        public bool TutorialInstructionsCompleted { get; private set; }
        public CombatActor PlayerActor { get; private set; }
        public CombatActor EliteActor { get; private set; }
        public CombatActor TutorialActor { get; private set; }
        public IReadOnlyList<CombatActor> EnemyActors => enemies;
        public event Action<EncounterStage> StageChanged;
        public event Action<CombatActor> ActorSpawned;
        public bool IsCombatActive => combatRunning && (Stage == EncounterStage.Tutorial || Stage == EncounterStage.EliteDuel || Stage == EncounterStage.Rescue);
        public bool RescueCoverActive => combatRunning && Stage == EncounterStage.RescueCover;
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
                if (Stage != EncounterStage.Rescue && Stage != EncounterStage.RescueCover && Stage != EncounterStage.Complete) return EliteActor;
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
        // Keep the same actors across retries: completed fights and ally allegiance
        // must not be reconstructed by replaying earlier story transitions.
        readonly List<BattleCheckpointActor> checkpoint = new();
        readonly List<RescueEntry> rescueEntries = new();
        sealed class RescueEntry
        {
            public CombatActor actor;
            public float verticalSpeed, stalledFor;
            public bool entered;
        }
        sealed class BattleCheckpointActor
        {
            public CombatActor actor;
            public Vector3 position;
            public Quaternion rotation;
            public CombatTeam team;
            public int health;
        }
        bool initialized, combatRunning, eliteRestored;
        float feedbackUntil;
        float entranceZ = float.NaN;
        InnEntranceDoors entranceDoors;
        Vector3 entranceWaypoint;
        float entranceMoveAt, entranceStartedAt, entranceStalledFor, entranceVerticalSpeed, eliteEntranceReadyAt;
        bool crossedEntrance;
        float rescueEntryMoveAt;
        Transform spawnStaging;
        void Start() => Initialize();
        void Update()
        {
            if (Time.time >= feedbackUntil) Feedback = "";
            if (EliteEntranceInProgress && Time.deltaTime > 0f) UpdateEliteEntrance(Time.deltaTime);
            if (RescueEntranceInProgress && Time.deltaTime > 0f) UpdateRescueEntrance(Time.deltaTime);
        }
        void Initialize()
        {
            if (initialized) return;
            initialized = true;
            var entrance = FindFirstObjectByType<InnEntranceDoors>();
            entranceDoors = entrance;
            if (entrance != null) entranceZ = entrance.transform.position.z;
            PlayerActor = GetComponent<CombatActor>();
            if (PlayerActor == null) PlayerActor = gameObject.AddComponent<CombatActor>();
            PlayerActor.Configure(CombatRole.Player, CombatTeam.Player, ScaledHealth(Mathf.Max(PlayerMax, PlayerActor.MaxHealth)));
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
            checkpoint.Clear();
            rescueEntries.Clear();
            RescueEntranceInProgress = false;
            RescueEnteredCount = 0;
            RetryStage = EncounterStage.Arrival;
            EliteEntranceInProgress = EliteEntranceComplete = EliteEntranceStoppedAtDoor = false;
            eliteEntranceReadyAt = 0f;
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
            TutorialSentryRevealed = TutorialInstructionsCompleted = false;
            Feedback = "";
            TutorialActor = SpawnInactive(CombatRole.Grunt, tutorialPosition, "驻守小兵");
            EliteActor = SpawnInactive(CombatRole.Elite, eliteArrivalPosition, "斗笠客");
            SetStage(EncounterStage.Arrival);
        }
        public bool RevealTutorialSentry()
        {
            Initialize();
            if (Stage != EncounterStage.Arrival || PlayerActor.IsDefeated || TutorialActor == null) return false;
            if (TutorialSentryRevealed) return TutorialActor.gameObject.activeInHierarchy;
            if (!CheckBindings() || !TryRevealActor(TutorialActor, tutorialPosition)) return false;
            TutorialActor.CancelAttack();
            TutorialActor.ControlLocked = true;
            TutorialActor.Invulnerable = true;
            TutorialActor.GetComponent<EnemyCombatAI>()?.SetCombatEnabled(false);
            TutorialSentryRevealed = true;
            return true;
        }
        public bool CompleteTutorialInstructions()
        {
            if (Stage != EncounterStage.Arrival || !TutorialSentryRevealed || TutorialActor == null
                || !TutorialActor.gameObject.activeInHierarchy || PlayerActor == null || PlayerActor.IsDefeated) return false;
            TutorialInstructionsCompleted = true;
            PlayerActor.CancelAttack();
            TutorialActor.CancelAttack();
            TutorialActor.Invulnerable = false;
            return true;
        }
        public bool BeginTutorial()
        {
            Initialize();
            if (Stage != EncounterStage.Arrival || TutorialActor == null || PlayerActor.IsDefeated
                || !TutorialSentryRevealed || !TutorialInstructionsCompleted) return false;
            if (!CheckBindings()) return false;
            if (!TutorialActor.gameObject.activeInHierarchy) return false;
            StartCombat(EncounterStage.Tutorial, TutorialActor);
            return true;
        }
        public bool BeginEliteDuel()
        {
            Initialize();
            if (Stage != EncounterStage.EliteReady || !EliteEntranceReadyForDialogue || EliteActor == null || PlayerActor.IsDefeated) return false;
            if (!CheckBindings()) return false;
            if (!EliteActor.gameObject.activeSelf && !TryRevealActor(EliteActor, eliteArrivalPosition)) return false;
            StartCombat(EncounterStage.EliteDuel, EliteActor);
            return true;
        }

        public bool BeginEliteEntrance()
        {
            if (Stage != EncounterStage.EliteReady || EliteActor == null) return false;
            if (EliteEntranceInProgress || EliteEntranceComplete) return true;
            if (!CheckBindings()) return false;
            Vector3 door = entranceDoors != null ? entranceDoors.transform.position
                : new Vector3(eliteArrivalPosition.x, eliteArrivalPosition.y, float.IsNaN(entranceZ) ? -4.96f : entranceZ);
            Vector3 inward = eliteArrivalPosition.z >= door.z ? Vector3.forward : Vector3.back;
            Vector3 start = door - inward * 1.7f;
            start.y = eliteArrivalPosition.y;
            if (!ActorPlacement.TryPlace(EliteActor.GetComponent<CharacterController>(), start,
                Quaternion.LookRotation(inward), entranceZ))
            {
                Show("门外站位被挡住，请留出进门通道");
                return false;
            }
            EliteActor.gameObject.SetActive(true);
            EliteActor.ControlLocked = true;
            EliteActor.GetComponent<EnemyCombatAI>()?.SetCombatEnabled(false);
            entranceWaypoint = door + inward * .85f;
            entranceWaypoint.y = eliteArrivalPosition.y;
            crossedEntrance = false;
            entranceStalledFor = entranceVerticalSpeed = 0f;
            entranceStartedAt = Time.time;
            float openWait = 0f;
            if (entranceDoors != null)
            {
                entranceDoors.OpenRequested = true;
                openWait = Mathf.Clamp(entranceDoors.openAngle / Mathf.Max(10f, entranceDoors.rotationSpeed) + .15f, .2f, 3f);
            }
            entranceMoveAt = Time.time + openWait;
            EliteEntranceInProgress = true;
            EliteEntranceStoppedAtDoor = false;
            return true;
        }

        void UpdateEliteEntrance(float dt)
        {
            if (Stage != EncounterStage.EliteReady || EliteActor == null)
            { EliteEntranceInProgress = false; return; }
            if (Time.time < entranceMoveAt)
            {
                SetEntranceLocomotion(0f);
                return;
            }
            var motor = EliteActor.GetComponent<CharacterController>();
            if (motor == null || !motor.enabled) { CompleteEliteEntrance(true); return; }
            Vector3 destination = crossedEntrance ? eliteArrivalPosition : entranceWaypoint;
            Vector3 delta = Vector3.ProjectOnPlane(destination - EliteActor.transform.position, Vector3.up);
            if (delta.magnitude <= .18f)
            {
                if (!crossedEntrance) { crossedEntrance = true; return; }
                CompleteEliteEntrance(false);
                return;
            }
            Quaternion facing = Quaternion.LookRotation(delta);
            EliteActor.transform.rotation = Quaternion.RotateTowards(EliteActor.transform.rotation, facing, 360f * dt);
            Vector3 before = EliteActor.transform.position;
            if (motor.isGrounded && entranceVerticalSpeed < 0f) entranceVerticalSpeed = -2f;
            entranceVerticalSpeed = Mathf.Max(-12f, entranceVerticalSpeed - 22f * dt);
            motor.Move(delta.normalized * (Mathf.Min(eliteEntranceSpeed, delta.magnitude / dt) * dt)
                + Vector3.up * (entranceVerticalSpeed * dt));
            float moved = Vector3.ProjectOnPlane(EliteActor.transform.position - before, Vector3.up).magnitude;
            entranceStalledFor = moved < .1f * dt ? entranceStalledFor + dt : 0f;
            SetEntranceLocomotion(moved / dt);
            // Stop at the actual safe position if the player or new scenery blocks
            // the route. Never teleport through the obstacle to finish the scene.
            if (entranceStalledFor > 1.5f || Time.time - entranceStartedAt > 9f)
                CompleteEliteEntrance(true);
        }

        void SetEntranceLocomotion(float speed)
        {
            if (EliteActor.Animation != null && EliteActor.Animation.Ready)
                EliteActor.Animation.SetLocomotion(speed, false);
        }

        void CompleteEliteEntrance(bool stoppedAtDoor)
        {
            EliteEntranceInProgress = false;
            EliteEntranceComplete = true;
            EliteEntranceStoppedAtDoor = stoppedAtDoor;
            SetEntranceLocomotion(0f);
            // Let the real walk-to-idle blend finish before dialogue pauses game
            // time; otherwise the last walking pose freezes with a raised foot.
            float idleFade = EliteActor.Animation != null && EliteActor.Animation.motionSet != null
                ? EliteActor.Animation.motionSet.locomotionFade : 0f;
            eliteEntranceReadyAt = Time.time + Mathf.Max(.25f, idleFade + .05f);
            Vector3 toward = Vector3.ProjectOnPlane(PlayerActor.transform.position - EliteActor.transform.position, Vector3.up);
            if (toward.sqrMagnitude > .001f) EliteActor.transform.rotation = Quaternion.LookRotation(toward);
        }
        public bool MakeEliteAlly()
        {
            if (!EliteVictoryAchieved || Stage != EncounterStage.EliteVictory || EliteActor == null) return false;
            if (!eliteRestored)
            {
                var controller = EliteActor.GetComponent<CharacterController>();
                Vector3 inward = FrontDoorInward;
                Vector3 hallPosition = FrontDoorPosition + inward * 3.6f + Vector3.Cross(Vector3.up, inward) * 1.3f;
                hallPosition.y = eliteArrivalPosition.y;
                var facing = Quaternion.LookRotation(-inward);
                if (!ActorPlacement.TryPlace(controller, hallPosition, facing, entranceZ)
                    && !ActorPlacement.TryPlace(controller, EliteActor.transform.position, facing, entranceZ))
                {
                    Show("斗笠客站位被挡住");
                    return false;
                }
                EliteActor.Configure(CombatRole.Elite, CombatTeam.Player, ScaledHealth(eliteAllyMaxHealth));
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
            if (RescueCoverActive) return JoinRescueBattle();
            return StartRescueBattle(false);
        }

        public bool BeginRescueCover() => StartRescueBattle(true);

        bool StartRescueBattle(bool coverOnly)
        {
            if (PlayerActor == null || PlayerActor.IsDefeated || !EliteVictoryAchieved || Stage != EncounterStage.EliteVictory) return false;
            if (!CheckBindings() || !MakeEliteAlly()) return false;
            Vector3 door = FrontDoorPosition, inward = FrontDoorInward;
            // The player walks out and returns under their own control. Starting
            // the NPC cover skirmish must never pull them back into the hall.
            rescueEntries.Clear();
            var reinforcements = new List<CombatActor>();
            for (int i = 0; i < Mathf.Max(1, RescueEnemyCount); i++)
            {
                // Ignore the legacy rear-yard anchors. A spaced single-file queue
                // physically crosses the front threshold; nobody appears in the hall.
                Vector3 point = door - inward * (1.8f + i * 1.15f);
                point.y = eliteArrivalPosition.y;
                var reinforcement = SpawnInactive(CombatRole.Grunt, point, "后续小兵 " + (i + 1));
                reinforcement.transform.rotation = Quaternion.LookRotation(inward);
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
                rescueEntries.Add(new RescueEntry { actor = reinforcement });
            }
            enemies.Clear();
            enemies.AddRange(reinforcements);
            if (!coverOnly) PlayerActor.RestoreHealthToFull();
            PlayerActor.SetStorySurvivalFloor(coverOnly ? 1 : 0);
            SetRescueAllyProtection(true);
            combatRunning = true;
            PlayerActor.ControlLocked = false;
            EliteActor.ControlLocked = false;
            EliteActor.GetComponent<EnemyCombatAI>().SetCombatEnabled(true);
            foreach (var reinforcement in reinforcements)
                reinforcement.SetStorySurvivalFloor(coverOnly ? 1 : 0);
            StartRescueEntrance();
            if (!coverOnly) CaptureBattleCheckpoint(EncounterStage.Rescue);
            SetStage(coverOnly ? EncounterStage.RescueCover : EncounterStage.Rescue);
            return true;
        }

        public bool JoinRescueBattle()
        {
            if (!RescueCoverActive || PlayerActor == null || PlayerActor.IsDefeated || EliteActor == null) return false;
            // The UI checks the rear-door crossing. Also reject a remote join
            // so a stale interaction cannot start combat from outside the inn.
            Vector3 distance = PlayerActor.transform.position - EliteActor.transform.position;
            if (Mathf.Abs(distance.y) > 1.5f || Vector3.ProjectOnPlane(distance, Vector3.up).sqrMagnitude > 25f) return false;
            PlayerActor.CancelAttack();
            PlayerActor.SetStorySurvivalFloor(0);
            PlayerActor.RestoreHealthToFull();
            foreach (var actor in enemies)
            {
                if (actor == null) continue;
                actor.SetStorySurvivalFloor(0);
                actor.GetComponent<EnemyCombatAI>()?.SetExclusiveStoryTarget(null);
            }
            SetRescueAllyProtection(true);
            PlayerActor.ControlLocked = false;
            CaptureBattleCheckpoint(EncounterStage.Rescue);
            SetStage(EncounterStage.Rescue);
            return true;
        }

        int ScaledHealth(int baseline) => Mathf.Max(1, Mathf.CeilToInt(baseline * Mathf.Max(.1f, healthMultiplier)));
        Vector3 FrontDoorPosition => entranceDoors != null ? entranceDoors.transform.position
            : new Vector3(eliteArrivalPosition.x, eliteArrivalPosition.y, float.IsNaN(entranceZ) ? -4.96f : entranceZ);
        Vector3 FrontDoorInward => eliteArrivalPosition.z >= FrontDoorPosition.z ? Vector3.forward : Vector3.back;

        void StartRescueEntrance()
        {
            RescueEnteredCount = 0;
            RescueEntranceInProgress = rescueEntries.Count > 0;
            float wait = 0f;
            if (entranceDoors != null)
            {
                entranceDoors.OpenRequested = true;
                wait = Mathf.Clamp(entranceDoors.openAngle / Mathf.Max(10f, entranceDoors.rotationSpeed) + .15f, .2f, 3f);
            }
            rescueEntryMoveAt = Time.time + wait;
            foreach (var entry in rescueEntries)
            {
                entry.entered = false;
                entry.verticalSpeed = entry.stalledFor = 0f;
                entry.actor.ControlLocked = true;
                entry.actor.Invulnerable = true;
                entry.actor.GetComponent<EnemyCombatAI>()?.SetCombatEnabled(false);
            }
        }

        void UpdateRescueEntrance(float dt)
        {
            if (!combatRunning || Stage != EncounterStage.Rescue && Stage != EncounterStage.RescueCover) { RescueEntranceInProgress = false; return; }
            if (entranceDoors != null) entranceDoors.OpenRequested = true;
            Vector3 door = FrontDoorPosition, inward = FrontDoorInward;
            for (int i = 0; i < rescueEntries.Count; i++)
            {
                var entry = rescueEntries[i];
                if (entry.entered || entry.actor == null) continue;
                var actor = entry.actor;
                var motor = actor.GetComponent<CharacterController>();
                if (Time.time < rescueEntryMoveAt + i * .18f) continue;
                float insideDistance = Vector3.Dot(actor.transform.position - door, inward);
                if (insideDistance >= 2.2f)
                {
                    entry.entered = true;
                    RescueEnteredCount++;
                    actor.Invulnerable = false;
                    actor.ControlLocked = false;
                    var brain = actor.GetComponent<EnemyCombatAI>();
                    brain.SetCombatEnabled(true);
                    brain.PreferOpeningTarget(EliteActor);
                    if (RescueCoverActive) brain.SetExclusiveStoryTarget(EliteActor);
                    continue;
                }
                if (motor == null || !motor.enabled) continue;
                Vector3 target = door + inward * 2.45f;
                Vector3 direction = Vector3.ProjectOnPlane(target - actor.transform.position, Vector3.up).normalized;
                actor.transform.rotation = Quaternion.RotateTowards(actor.transform.rotation, Quaternion.LookRotation(direction), 360f * dt);
                if (motor.isGrounded && entry.verticalSpeed < 0f) entry.verticalSpeed = -2f;
                entry.verticalSpeed = Mathf.Max(-12f, entry.verticalSpeed - 22f * dt);
                Vector3 before = actor.transform.position;
                motor.Move(direction * (reinforcementEntranceSpeed * dt) + Vector3.up * (entry.verticalSpeed * dt));
                float moved = Vector3.ProjectOnPlane(actor.transform.position - before, Vector3.up).magnitude;
                actor.Animation?.SetLocomotion(moved / Mathf.Max(.001f, dt), false);
                entry.stalledFor = moved < .1f * dt ? entry.stalledFor + dt : 0f;
                // A player standing in the doorway must never be bypassed by teleporting.
                if (entry.stalledFor > 2f) Show("来兵正在入门，请留出门口通道");
            }
            if (RescueEnteredCount == rescueEntries.Count)
            {
                RescueEntranceInProgress = false;
                if (entranceDoors != null) entranceDoors.OpenRequested = false;
            }
        }

        static bool IsBattleStage(EncounterStage stage) => stage == EncounterStage.Tutorial
            || stage == EncounterStage.EliteDuel || stage == EncounterStage.Rescue;

        void CaptureBattleCheckpoint(EncounterStage stage)
        {
            RetryStage = stage;
            checkpoint.Clear();
            Capture(PlayerActor);
            foreach (var enemy in enemies) Capture(enemy);
            if (stage == EncounterStage.Rescue) Capture(EliteActor);

            void Capture(CombatActor actor)
            {
                if (actor == null) return;
                foreach (var saved in checkpoint) if (saved.actor == actor) return;
                checkpoint.Add(new BattleCheckpointActor
                {
                    actor = actor, position = actor.transform.position, rotation = actor.transform.rotation,
                    team = actor.Team,
                    // The player and opponents get a fresh attempt. The ally keeps
                    // the health granted when the rescue segment originally began.
                    health = actor == PlayerActor || actor.Team == CombatTeam.Enemy ? -1 : actor.Health
                });
            }
        }

        public bool RetryCurrentBattle()
        {
            if (!CanRetryCurrentBattle || !CheckBindings()) return false;
            foreach (var saved in checkpoint)
                if (saved.actor == null || saved.actor.GetComponent<CharacterController>() == null)
                { Show("当前战斗角色已丢失，无法重试"); return false; }

            StopCombat();
            if (!TryRestoreCheckpointPositions())
            {
                PlayerActor.ControlLocked = true;
                SetStage(EncounterStage.Failed);
                Show("重试站位被挡住，请移开障碍后再次重试");
                return false;
            }

            Vector3 playerPosition = PlayerActor.transform.position;
            Quaternion playerRotation = PlayerActor.transform.rotation;
            foreach (var saved in checkpoint)
            {
                var actor = saved.actor;
                actor.gameObject.SetActive(true);
                actor.SetTeam(saved.team);
                actor.ResetActor(saved.health);
                actor.ControlLocked = true;
                actor.GetComponent<EnemyCombatAI>()?.ResetBrain();
            }
            var fighter = PlayerActor.GetComponent<PrototypeFighter>();
            if (fighter != null)
            {
                fighter.ResetPose();
                fighter.PlaceForStory(playerPosition, playerRotation);
            }

            enemies.Clear();
            combatRunning = true;
            SetRescueAllyProtection(RetryStage == EncounterStage.Rescue);
            foreach (var saved in checkpoint)
            {
                var actor = saved.actor;
                actor.ControlLocked = false;
                if (actor.Team == CombatTeam.Enemy) enemies.Add(actor);
                var brain = actor.GetComponent<EnemyCombatAI>();
                if (brain != null)
                {
                    brain.SetCombatEnabled(true);
                    if (RetryStage == EncounterStage.Rescue && actor.Team == CombatTeam.Enemy)
                        brain.PreferOpeningTarget(EliteActor);
                }
                if (actor.Animation != null && actor.Animation.Ready && actor.Animation.HasMotion(CombatMotion.Idle))
                    actor.Animation.Play(CombatMotion.Idle, .1f);
            }
            Feedback = "";
            feedbackUntil = 0f;
            if (RetryStage == EncounterStage.Rescue) StartRescueEntrance();
            SetStage(RetryStage);
            return true;
        }

        bool TryRestoreCheckpointPositions()
        {
            var oldPositions = new Vector3[checkpoint.Count];
            var oldRotations = new Quaternion[checkpoint.Count];
            var colliders = new List<Collider>();
            var enabledStates = new List<bool>();
            for (int i = 0; i < checkpoint.Count; i++)
            {
                var actor = checkpoint[i].actor;
                oldPositions[i] = actor.transform.position;
                oldRotations[i] = actor.transform.rotation;
                foreach (var collider in actor.GetComponentsInChildren<Collider>(true))
                {
                    colliders.Add(collider);
                    enabledStates.Add(collider.enabled);
                    collider.enabled = false;
                }
            }

            bool placed = false;
            try
            {
                foreach (var saved in checkpoint)
                {
                    var controller = saved.actor.GetComponent<CharacterController>();
                    if (!ActorPlacement.TryPlace(controller, saved.position, saved.rotation, entranceZ)) return false;
                    // Reserve each restored standing position before placing the
                    // next actor, including previously defeated/hidden grunts.
                    controller.enabled = true;
                    Physics.SyncTransforms();
                }
                placed = true;
                return true;
            }
            finally
            {
                foreach (var collider in colliders) if (collider != null) collider.enabled = false;
                if (!placed)
                    for (int i = 0; i < checkpoint.Count; i++)
                        checkpoint[i].actor.transform.SetPositionAndRotation(oldPositions[i], oldRotations[i]);
                for (int i = 0; i < colliders.Count; i++)
                    if (colliders[i] != null) colliders[i].enabled = enabledStates[i];
                Physics.SyncTransforms();
            }
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
            RescueEntranceInProgress = false;
            SetRescueAllyProtection(false);
            if (PlayerActor != null) { PlayerActor.CancelAttack(); PlayerActor.SetStorySurvivalFloor(0); }
            foreach (var actor in spawned)
            {
                if (actor == null) continue;
                actor.CancelAttack();
                actor.SetStorySurvivalFloor(0);
                actor.ControlLocked = true;
                actor.GetComponent<EnemyCombatAI>()?.SetCombatEnabled(false);
            }
        }
        void SetRescueAllyProtection(bool active)
        {
            if (EliteActor != null)
                EliteActor.SetGruntDamageHealthFloor(active && EliteVictoryAchieved && EliteActor.Team == CombatTeam.Player
                    ? Mathf.Max(1, eliteAllyMinimumHealth) : 0);
        }
        void StartCombat(EncounterStage stage, CombatActor opponent)
        {
            StopCombat(); enemies.Clear(); enemies.Add(opponent);
            PlayerActor.RestoreHealthToFull();
            combatRunning = true;
            PlayerActor.ControlLocked = false;
            opponent.ControlLocked = false;
            opponent.GetComponent<EnemyCombatAI>().SetCombatEnabled(true);
            CaptureBattleCheckpoint(stage);
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
            actor.Configure(role, CombatTeam.Enemy, ScaledHealth(elite ? eliteMaxHealth : gruntMaxHealth));
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
        void SetStage(EncounterStage value)
        {
            Stage = value;
            if (entranceDoors != null)
            {
                // Includes dialogue, cover and failure while the current battle can be retried.
                entranceDoors.HoldOpenForEncounter = value is not (EncounterStage.Arrival or EncounterStage.Complete);
                if (!entranceDoors.HoldOpenForEncounter) entranceDoors.OpenRequested = false;
            }
            StageChanged?.Invoke(value);
        }
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
