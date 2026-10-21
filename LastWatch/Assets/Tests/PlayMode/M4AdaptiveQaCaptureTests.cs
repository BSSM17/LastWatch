#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Partner;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LastWatch.Tests.M4
{
    // Stimulus policy only. Core owns the tick, shot, Director, tape, and frozen evidence.
    [Timeout(780000)]
    public sealed class M4AdaptiveQaCaptureTests
    {
        const string ScenePath = "Assets/Scenes/LastWatch_M2M3M4_3Night_QA.unity";
        const string ExportMenuPath = "LastWatch/QA/Export Frozen M4 Capture Bundle";
        const int MaximumCommands = 90000;
        const double WallTimeoutSeconds = 660;

        [Serializable] sealed class Decision
        {
            public int runFixedTick;
            public int nightOrdinal;
            public string sessionStage;
            public int completedNights;
            public string targetTicketId;
            public string reason;
            public int weapon;
            public bool fire;
            public bool reload;
            public M4ReplayTick command;
        }

        [Serializable] sealed class DecisionLog
        {
            public int schemaVersion = 1;
            public string policyId = "m4-adaptive-visible-actor-v0.1";
            public string stimulusId;
            public string baselinePlanSha256;
            public int difficulty;
            public Decision[] decisions;
        }

        [UnityTest] public IEnumerator NormalAdaptiveCapture() { yield return Capture(0); }
        [UnityTest] public IEnumerator HardAdaptiveCapture() { yield return Capture(1); }

        static IEnumerator Capture(int difficulty)
        {
            string baselinePath = RequiredPath("LASTWATCH_M4_BASELINE_PLAN_PATH");
            string stimulusPath = RequiredPath("LASTWATCH_M4_ADAPTIVE_PLAN_PATH");
            string decisionPath = RequiredPath("LASTWATCH_M4_ADAPTIVE_DECISION_PATH");
            string resultPath = RequiredPath("LASTWATCH_M4_CAPTURE_RESULT_PATH");
            string stimulusId = Environment.GetEnvironmentVariable("LASTWATCH_M4_ADAPTIVE_STIMULUS_ID");
            Assert.That(stimulusId, Does.Match("^m4-adaptive-[A-Za-z0-9._-]{1,68}$"));
            Assert.That(File.Exists(stimulusPath), Is.False, "Stimulus output must be new.");
            Assert.That(File.Exists(decisionPath), Is.False, "Decision output must be new.");

            byte[] baselineBytes = File.ReadAllBytes(baselinePath);
            string baselineJson = new UTF8Encoding(false, true).GetString(baselineBytes);
            Assert.That(M4QaInputPlan.TryParse(baselineJson, out M4QaInputPlan baseline, out string reason), Is.True, reason);
            Assert.That(baseline.difficulty, Is.EqualTo(difficulty));
            var policy = new Policy(baseline, stimulusId, Sha256(baselineBytes));

            Scene scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True);
            yield return null;
            M4NightOneGrayboxController controller = UnityEngine.Object.FindFirstObjectByType<M4NightOneGrayboxController>();
            Assert.That(controller, Is.Not.Null);
            controller.BeginAdaptiveQaCapture(difficulty, stimulusId, policy.Next);

            Stopwatch timer = Stopwatch.StartNew();
            while (controller.InputPlanCaptureStatus == M4QaInputPlanCaptureStatus.Running &&
                   timer.Elapsed.TotalSeconds < WallTimeoutSeconds)
                yield return new WaitForFixedUpdate();
            timer.Stop();

            policy.WriteDecisionLog(decisionPath);
            Assert.That(controller.InputPlanCaptureStatus, Is.EqualTo(M4QaInputPlanCaptureStatus.Completed),
                controller.InputPlanCaptureFailure + "; elapsed=" + timer.Elapsed.TotalSeconds.ToString("F1") + "s");
            Assert.That(controller.InputPlanConsumedTicks, Is.EqualTo(policy.Count));
            Assert.That(controller.InputPlanTotalTicks, Is.EqualTo(policy.Count));
            Assert.That(controller.TryGetFrozenCaptureBundle(out M4QaCaptureBundle bundle, out reason), Is.True, reason);
            Assert.That(bundle.ReplayTickCount, Is.EqualTo(policy.Count));
            policy.AssertCoreTapeMatches(bundle.ReplayTape.RawJson);
            policy.WriteStimulus(stimulusPath, difficulty);

            Assert.That(EditorApplication.ExecuteMenuItem(ExportMenuPath), Is.True);
            Assert.That(File.Exists(resultPath), Is.True, "Collector did not create a receipt.");
        }

        static string RequiredPath(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            Assert.That(value, Is.Not.Null.And.Not.Empty, name + " is required.");
            return Path.GetFullPath(value);
        }

        static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var text = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        sealed class Policy
        {
            readonly M4QaInputPlan baseline;
            readonly string stimulusId;
            readonly string baselineSha;
            readonly List<M4ReplayTick> commands = new List<M4ReplayTick>();
            readonly List<Decision> decisions = new List<Decision>();
            readonly List<Vector3> vantages = new List<Vector3>();

            public int Count => commands.Count;

            public Policy(M4QaInputPlan baseline, string stimulusId, string baselineSha)
            {
                this.baseline = baseline;
                this.stimulusId = stimulusId;
                this.baselineSha = baselineSha;
                // Positions come from the declared stimulus source; no game geometry is invented here.
                vantages.Add(baseline.ticks[0].Position);
                int stride = Math.Max(1, baseline.ticks.Length / 24);
                for (int index = 0; index < baseline.ticks.Length; index += stride)
                {
                    Vector3 point = baseline.ticks[index].Position;
                    if (!vantages.Exists(existing => Vector3.Distance(existing, point) < 0.5f)) vantages.Add(point);
                }
            }

            public M4ReplayTick Next(M4QaAdaptiveSnapshot state)
            {
                if (state == null || state.RunFixedTick != commands.Count)
                    throw new InvalidOperationException("Adaptive stimulus snapshot/tick mismatch.");
                if (state.RunFixedTick >= MaximumCommands) return null;
                if (state.PlayerDead) throw new InvalidOperationException("Player died; lossless QA capture cannot continue.");
                if (state.Weapons == null || state.Weapons.Count != 3 || state.Tickets == null)
                    throw new InvalidOperationException("Core adaptive observation is incomplete.");

                Vector3 position = state.RunFixedTick < baseline.ticks.Length
                    ? baseline.ticks[state.RunFixedTick].Position : state.PlayerPosition;
                Vector3 cameraOffset = state.PlayerCameraPosition - state.PlayerPosition;
                int weapon = ChooseWeapon(state);
                bool reload = state.Weapons[weapon].Magazine == 0 &&
                    (weapon == 0 || state.Weapons[weapon].Reserve > 0) && !state.ReloadActive;
                bool ready = !state.ReloadActive && state.RunFixedTick > state.ReloadShotBlockedThroughRunTick &&
                    state.RunFixedTick >= state.Weapons[weapon].NextLegalShotRunTick &&
                    state.Weapons[weapon].Magazine > 0;

                M4QaTicketSnapshot? chosen = null;
                Vector3 aim = Vector3.zero;
                if (ready)
                {
                    // Raycasts are read-only stimulus screening. Core still resolves every shot.
                    foreach (M4QaTicketSnapshot ticket in state.Tickets)
                    {
                        if (!ticket.ActorPresent || !ticket.ActiveEntityId.HasValue) continue;
                        if (TryVisible(position + cameraOffset, ticket, out aim)) { chosen = ticket; break; }
                    }
                    if (!chosen.HasValue)
                    {
                        foreach (Vector3 vantage in vantages)
                        {
                            foreach (M4QaTicketSnapshot ticket in state.Tickets)
                            {
                                if (!ticket.ActorPresent || !ticket.ActiveEntityId.HasValue) continue;
                                if (!TryVisible(vantage + cameraOffset, ticket, out aim)) continue;
                                position = vantage;
                                chosen = ticket;
                                break;
                            }
                            if (chosen.HasValue) break;
                        }
                    }
                }

                float yaw = state.PlayerYaw;
                float pitch = state.CameraPitch;
                if (chosen.HasValue)
                {
                    Vector3 delta = aim - (position + cameraOffset);
                    yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                    pitch = Mathf.Clamp(-Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg, -80f, 80f);
                }
                var command = M4ReplayTick.Capture(state.RunFixedTick, state.NightOrdinal, position,
                    yaw, pitch, weapon, chosen.HasValue, reload);
                commands.Add(command);
                decisions.Add(new Decision
                {
                    runFixedTick = state.RunFixedTick, nightOrdinal = state.NightOrdinal,
                    sessionStage = state.SessionStage.ToString(), completedNights = state.CompletedNights,
                    targetTicketId = chosen.HasValue ? chosen.Value.TicketId.ToString() : string.Empty,
                    reason = chosen.HasValue ? "visible-active-actor" : reload ? "reload" : "hold-no-clear-target-or-cadence",
                    weapon = weapon, fire = command.firePressed, reload = command.reloadPressed, command = command
                });
                return command;
            }

            static bool TryVisible(Vector3 origin, M4QaTicketSnapshot ticket, out Vector3 aim)
            {
                aim = ticket.ActorAimPoint;
                Vector3 displacement = aim - origin;
                float distance = displacement.magnitude;
                if (distance < 0.01f || distance > 70f) return false;
                if (!Physics.Raycast(origin, displacement / distance, out RaycastHit hit, distance + 1f)) return false;
                ActiveEnemyActor actor = hit.collider.GetComponent<ActiveEnemyActor>();
                return actor != null && actor.TicketId == ticket.TicketId;
            }

            static int ChooseWeapon(M4QaAdaptiveSnapshot state)
            {
                if (state.ReloadActive) return state.SelectedWeapon;
                if (state.Weapons[2].Magazine > 0 && state.RunFixedTick >= state.Weapons[2].NextLegalShotRunTick) return 2;
                if (state.Weapons[1].Magazine > 0 && state.RunFixedTick >= state.Weapons[1].NextLegalShotRunTick) return 1;
                if (state.Weapons[0].Magazine > 0) return 0;
                if (state.Weapons[2].Reserve > 0) return 2;
                if (state.Weapons[1].Reserve > 0) return 1;
                return 0;
            }

            public void WriteDecisionLog(string path)
            {
                var log = new DecisionLog { stimulusId = stimulusId, baselinePlanSha256 = baselineSha,
                    difficulty = baseline.difficulty, decisions = decisions.ToArray() };
                WriteNew(path, JsonUtility.ToJson(log, false));
            }

            public void WriteStimulus(string path, int difficulty)
            {
                var plan = new M4QaInputPlan { planId = stimulusId,
                    runSeed = M4ThreeNightQaSceneEntry.EvidenceRunSeed,
                    difficulty = difficulty, ticks = commands.ToArray() };
                Assert.That(plan.TryValidate(out string reason), Is.True, reason);
                WriteNew(path, JsonUtility.ToJson(plan, false));
            }

            public void AssertCoreTapeMatches(string rawJson)
            {
                Assert.That(M4ReplayTape.TryParse(rawJson, out M4ReplayTape tape, out string reason), Is.True, reason);
                Assert.That(tape.ticks.Length, Is.EqualTo(commands.Count));
                for (int i = 0; i < commands.Count; i++)
                {
                    M4ReplayTick applied = tape.ticks[i];
                    M4ReplayTick requested = commands[i];
                    Assert.That(applied.runFixedTick, Is.EqualTo(requested.runFixedTick));
                    Assert.That(applied.nightOrdinal, Is.EqualTo(requested.nightOrdinal));
                    Assert.That(Vector3.Distance(applied.Position, requested.Position), Is.LessThan(0.001f));
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(applied.bodyYaw, requested.bodyYaw)), Is.LessThan(0.01f));
                    Assert.That(Mathf.Abs(applied.cameraPitch - requested.cameraPitch), Is.LessThan(0.01f));
                    Assert.That(applied.weapon, Is.EqualTo(requested.weapon));
                    Assert.That(applied.firePressed, Is.EqualTo(requested.firePressed));
                    Assert.That(applied.reloadPressed, Is.EqualTo(requested.reloadPressed));
                }
            }

            static void WriteNew(string path, string json)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json + "\n");
            }
        }
    }
}
#endif
