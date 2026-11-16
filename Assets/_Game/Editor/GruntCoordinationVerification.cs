using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Coordination rules only. Attack state and gap boundaries are explicitly set;
// this does not simulate sword animation, AI navigation or human playability.
public static class GruntCoordinationVerification
{
    [Serializable] public sealed class Report
    {
        public string scope = "Synchronous isolated grunt turn rules in Play Mode; temporary actors, reflected attack state and gap boundaries. No animation/gameplay claim, no scene save.";
        public bool passed;
        public int checks;
        public List<string> successes = new(), failures = new();
    }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;

    [Unity.Pipeline.Commands.CliCommand("inncombat", "grunt_rules", "Check isolated grunt attack coordination including cross-Update release races")]
    public static string Run()
    {
        var report = new Report();
        if (!EditorApplication.isPlaying)
        {
            report.failures.Add("Enter Play Mode in the existing scene first.");
            return JsonUtility.ToJson(report, true);
        }
        var turns = (Dictionary<CombatActor, EnemyCombatAI>)typeof(EnemyCombatAI).GetField("attackers", StaticHidden).GetValue(null);
        var gaps = (Dictionary<CombatActor, float>)typeof(EnemyCombatAI).GetField("nextOpening", StaticHidden).GetValue(null);
        var originalTurns = new Dictionary<CombatActor, EnemyCombatAI>(turns);
        var originalGaps = new Dictionary<CombatActor, float>(gaps);
        var ownedActors = new List<CombatActor>();
        GameObject fixture = null;
        void Check(bool value, string text)
        {
            report.checks++;
            if (value) report.successes.Add(text); else report.failures.Add(text);
        }
        try
        {
            fixture = new GameObject("GRUNT_TURN_RULES_TEMPORARY");
            CombatActor Create(string name, CombatRole role, CombatTeam team, Vector3 offset)
            {
                var body = new GameObject(name);
                body.transform.SetParent(fixture.transform, false);
                body.transform.position = new Vector3(14000f, 200f, 14000f) + offset;
                body.AddComponent<CharacterController>();
                var actor = body.AddComponent<CombatActor>();
                actor.Configure(role, team, 100);
                ownedActors.Add(actor);
                return actor;
            }
            var first = Create("Grunt A", CombatRole.Grunt, CombatTeam.Enemy, Vector3.zero).gameObject.AddComponent<EnemyCombatAI>();
            var second = Create("Grunt B", CombatRole.Grunt, CombatTeam.Enemy, Vector3.right * 4f).gameObject.AddComponent<EnemyCombatAI>();
            var ally = Create("Target ally", CombatRole.Elite, CombatTeam.Player, Vector3.forward * 2f);
            var otherAlly = Create("Other target", CombatRole.Elite, CombatTeam.Player, Vector3.forward * 5f);
            var player = Create("Target player", CombatRole.Player, CombatTeam.Player, Vector3.left * 5f);
            first.sharedAttackGap = second.sharedAttackGap = .28f;
            first.SetCombatEnabled(true);
            second.SetCombatEnabled(true);
            Target(first, ally); Target(second, ally);
            Check(CanStart(first), "First grunt can take an unclaimed target");
            BeginTurn(first, ally);
            Check(!CanStart(second), "A second grunt cannot attack the same target concurrently");
            Target(second, otherAlly);
            Check(CanStart(second), "Grunts may attack different targets concurrently");
            Target(second, ally);

            // A finishes in LateUpdate. B asks first next frame, before A.Update
            // would normally release its slot. B must establish and respect the gap.
            first.Actor.CancelAttack();
            Check(!CanStart(second), "Cross-Update handoff cannot bypass the shared attack gap");
            bool foundRaceGap = gaps.TryGetValue(ally, out float raceGap);
            Check(!turns.ContainsKey(ally) && foundRaceGap && raceGap >= Time.time + .27f,
                "Lazy release removes finished owner and establishes its shared gap");
            Call(first, "ReleaseAttackTurn");
            Check(gaps.TryGetValue(ally, out float unchangedGap) && Mathf.Approximately(unchangedGap, raceGap),
                "Previous owner's later release does not overwrite or extend the gap");
            gaps[ally] = Time.time - .01f;
            Check(CanStart(second) && !gaps.ContainsKey(ally), "Expired shared gap allows the next grunt and is cleaned up");

            BeginTurn(first, ally);
            Check(first.Actor.ReceiveDamage(ally, 1000, 0f, 0f, true), "Temporary grunt defeated through real damage interface");
            Check(!CanStart(second) && !turns.ContainsKey(ally), "Defeated owner releases its turn but keeps a brief gap");
            gaps[ally] = Time.time - .01f;
            Check(CanStart(second), "Defeated owner cannot leave a permanent turn lock");

            first.Actor.ResetActor();
            BeginTurn(first, ally);
            first.enabled = false;
            Check(!turns.ContainsKey(ally) && !CanStart(second), "Disabling an owner releases its slot and respects the gap");
            gaps[ally] = Time.time - .01f;
            Check(CanStart(second), "A disabled owner's slot becomes available after the gap");
            first.enabled = true;
            BeginTurn(first, ally);
            first.ResetBrain();
            Check(!turns.ContainsKey(ally) && !first.Actor.IsAttacking && !first.CombatEnabled && first.Target == null,
                "Retry brain reset clears reservation, target and old attack");
            gaps[ally] = Time.time - .01f;
            Check(CanStart(second), "Retry reset does not permanently block other grunts");

            Camera camera = Camera.main;
            Check(camera != null, "Current scene provides a main camera for visibility rules");
            if (camera != null)
            {
                Target(second, player);
                second.transform.position = camera.transform.position - camera.transform.forward * 4f - Vector3.up;
                Check(!CanStart(second), "A grunt behind the camera cannot start a fresh attack on the player");
                second.transform.position = camera.transform.position + camera.transform.forward * 4f - Vector3.up;
                Check(CanStart(second), "An on-screen grunt can start attacking an unclaimed player target");
            }

            // These isolated bodies have no floor beneath them, so retreat is
            // correctly unsafe. Its pause must expire without starving combat.
            first.SetCombatEnabled(true); Target(first, ally);
            Vector3 toward = Vector3.forward * 1.1f;
            ally.transform.position = first.transform.position + toward;
            Physics.SyncTransforms();
            var yieldMethod = typeof(EnemyCombatAI).GetMethod("GiveCloseContactSpace", Hidden);
            bool Yield() => (bool)yieldMethod.Invoke(first, new object[] { toward, toward.magnitude, .016f });
            Check(Yield() && first.SpacePathBlocked, "Unsafe retreat creates only a brief pause, without stepping off a ledge");
            typeof(EnemyCombatAI).GetField("spacePauseUntil", Hidden).SetValue(first, Time.time - .01f);
            Check(!Yield() && !first.GivingSpace, "After a blocked retreat pause, normal combat is no longer intercepted");
            Check(!Yield(), "Persistent close contact cannot restart the same blocked-retreat loop");
            first.ResetBrain(); first.SetCombatEnabled(true); Target(first, ally);
            Check(Yield() && first.SpacePathBlocked, "Retry resets the one-attempt retreat guard for the next encounter");
        }
        catch (Exception error) { report.failures.Add(error.GetBaseException().ToString()); }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            // Destroy callbacks may leave a gap for a temporary target; remove
            // only fixture-owned keys, never reset live encounter coordination.
            foreach (var actor in ownedActors) { turns.Remove(actor); gaps.Remove(actor); }
            Check(turns.Count == originalTurns.Count && Same(turns, originalTurns), "Original scene turn reservations remain unchanged");
            Check(gaps.Count == originalGaps.Count && Same(gaps, originalGaps), "Original scene shared gaps remain unchanged");
            Check(fixture == null, "All temporary test actors cleaned up without saving scene changes");
        }
        report.passed = report.failures.Count == 0;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        Directory.CreateDirectory(folder);
        string json = JsonUtility.ToJson(report, true);
        File.WriteAllText(Path.Combine(folder, "GruntCoordinationVerification.json"), json);
        return json;
    }
    static bool Same<T>(Dictionary<CombatActor, T> a, Dictionary<CombatActor, T> b)
    {
        foreach (var pair in b)
            if (!a.TryGetValue(pair.Key, out T value) || !EqualityComparer<T>.Default.Equals(value, pair.Value)) return false;
        return true;
    }
    static bool CanStart(EnemyCombatAI brain) => (bool)Call(brain, "CanStartAttackTurn");
    static object Call(EnemyCombatAI brain, string method) => typeof(EnemyCombatAI).GetMethod(method, Hidden).Invoke(brain, null);
    static void Target(EnemyCombatAI brain, CombatActor actor)
        => typeof(EnemyCombatAI).GetProperty("Target").GetSetMethod(true).Invoke(brain, new object[] { actor });
    static void BeginTurn(EnemyCombatAI brain, CombatActor target)
    {
        brain.SetCombatEnabled(true);
        Target(brain, target);
        typeof(CombatActor).GetField("attack", Hidden).SetValue(brain.Actor, new CombatAttack());
        Call(brain, "ClaimAttackTurn");
    }
}
