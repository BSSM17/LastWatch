using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections;
using LastWatch.Core.Combat;
using LastWatch.Core.Partner;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LastWatch.Tests.M4
{
#if UNITY_EDITOR
    [Timeout(780000)]
    public sealed class M4DeterministicInputPlanCaptureTests
    {
        const string ScenePath = "Assets/Scenes/LastWatch_M2M3M4_3Night_QA.unity";
        const double PerCaptureWallTimeoutSeconds = 660;
        const string ExportMenuPath = "LastWatch/QA/Export Frozen M4 Capture Bundle";

        [Serializable]
        sealed class CaptureDriverReceipt
        {
            public int SchemaVersion;
            public string Classification;
            public string CaptureRunId;
            public int Difficulty;
            public string InputKind;
            public string InputPlanId;
            public string InputPlanSha256;
            public string SourceBuildId;
            public string SourceDeliverySha256;
            public string CaptureDirectory;
            public string Message;
        }

        [Serializable]
        sealed class ShotDiagnosticsReceipt
        {
            public int SchemaVersion;
            public string CaptureRunId;
            public string InputPlanId;
            public bool IsInputPlanCapture;
            public string InputPlanCaptureStatus;
            public string InputPlanCaptureFailure;
            public string ReplayShotDiagnosticsFailure;
            public int InputPlanConsumedTicks;
            public int InputPlanTotalTicks;
            public int FireCommandCount;
            public int RaycastAttemptCount;
            public bool ColliderNameBucketsTruncated;
            public bool ExamplesTruncated;
            public OutcomeBucket[] OutcomeCounts;
            public ColliderKindBucket[] ColliderKindCounts;
            public ColliderNameBucket[] ColliderNameCounts;
            public FaceBucket[] FaceCounts;
            public DirectorDiagnostic Director;
        }

        [Serializable]
        sealed class DirectorDiagnostic
        {
            public string SessionStage;
            public int CompletedNights;
            public int TicketCount;
            public int PendingCount;
            public int PoolWaitingCount;
            public int ActiveCount;
            public int ResolvedCount;
            public DirectorTicketDiagnostic[] Tickets;
        }

        [Serializable]
        sealed class DirectorTicketDiagnostic
        {
            public string TicketId;
            public int QueueIndex;
            public string Archetype;
            public string Face;
            public string Lifecycle;
            public string ActiveEntityId;
            public string MovementState;
            public string BlockReason;
            public bool ActorFound;
            public float ActorPositionX;
            public float ActorPositionY;
            public float ActorPositionZ;
        }

        [Serializable]
        sealed class OutcomeBucket
        {
            public string Outcome;
            public int Count;
            public bool ExamplesTruncated;
            public ShotExample[] Examples;
        }

        [Serializable]
        sealed class ColliderKindBucket
        {
            public bool HasGrayboxTag;
            public string ColliderKind;
            public int Count;
            public bool ExamplesTruncated;
            public ShotExample[] Examples;
        }

        [Serializable]
        sealed class ColliderNameBucket
        {
            public string ColliderName;
            public bool IsOverflowBucket;
            public int Count;
            public bool ExamplesTruncated;
            public ShotExample[] Examples;
        }

        [Serializable]
        sealed class FaceBucket
        {
            public bool HasFace;
            public string Face;
            public int Count;
            public bool ExamplesTruncated;
            public ShotExample[] Examples;
        }

        [Serializable]
        sealed class ShotExample
        {
            public string CaptureRunId;
            public string InputPlanId;
            public int InputPlanCommandIndex;
            public int ReplayConsumedTick;
            public int RunFixedTick;
            public string WeaponId;
            public bool RaycastAttempted;
            public string Outcome;
            public string NoRaycastReason;
            public bool HasColliderHit;
            public string ColliderName;
            public float HitDistance;
            public bool HasGrayboxTag;
            public string ColliderKind;
            public bool HasFace;
            public string Face;
            public bool HasActiveEnemyActor;
            public bool ActorRegistryKeyPresent;
            public bool ActorInstanceMatchesRegistry;
            public bool HasDamageReceipt;
            public string DamageStatus;
            public bool DamageBecameResolved;
        }

        [UnityTest] public IEnumerator NormalPlanCapture() { yield return Capture("normal", 0); }
        [UnityTest] public IEnumerator HardPlanCapture() { yield return Capture("hard", 1); }

        static IEnumerator Capture(string difficulty, int expectedDifficulty)
        {
            string planPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_INPUT_PLAN_PATH");
            string resultPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_CAPTURE_RESULT_PATH");
            string diagnosticsPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_CAPTURE_DIAGNOSTICS_PATH");
            Assert.That(planPath, Is.Not.Null.And.Not.Empty, "Fixed-input plan path was not supplied.");
            Assert.That(resultPath, Is.Not.Null.And.Not.Empty, "Capture result receipt path was not supplied.");
            Assert.That(diagnosticsPath, Is.Not.Null.And.Not.Empty, "Same-run shot-diagnostics output path was not supplied.");

            byte[] originalPlanBytes = File.ReadAllBytes(planPath);
            string originalPlanJson = new UTF8Encoding(false, true).GetString(originalPlanBytes);
            Assert.That(M4QaInputPlan.TryParse(originalPlanJson, out M4QaInputPlan plan, out string parseReason), Is.True, parseReason);
            Assert.That(plan.difficulty, Is.EqualTo(expectedDifficulty));
            Assert.That(plan.runSeed, Is.EqualTo(M4ThreeNightQaSceneEntry.EvidenceRunSeed));
            Assert.That(plan.ticks[0].runFixedTick, Is.EqualTo(0));
            Assert.That(plan.ticks[plan.ticks.Length - 1].nightOrdinal, Is.EqualTo(3));

#if UNITY_EDITOR
            Scene scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True, "QA scene did not load.");
#else
            Assert.Fail("Deterministic M4 capture requires Unity Editor Play Mode.");
            yield break;
#endif
            yield return null;

            M4NightOneGrayboxController controller = UnityEngine.Object.FindFirstObjectByType<M4NightOneGrayboxController>();
            Assert.That(controller, Is.Not.Null, "QA controller was not found in the loaded scene.");
            controller.BeginDeterministicInputPlanCapture(plan);

            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                while (controller.InputPlanCaptureStatus == M4QaInputPlanCaptureStatus.Running &&
                       timer.Elapsed.TotalSeconds < PerCaptureWallTimeoutSeconds)
                    yield return new WaitForFixedUpdate();
            }
            finally
            {
                timer.Stop();
                WriteShotDiagnostics(controller, diagnosticsPath);
            }
            timer.Stop();

            Assert.That(controller.InputPlanCaptureStatus, Is.EqualTo(M4QaInputPlanCaptureStatus.Completed),
                "Input-plan capture stopped: " + controller.InputPlanCaptureFailure +
                "; plan=" + controller.InputPlanId + "; commands=" + plan.ticks.Length +
                "; elapsed=" + timer.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s.");
            Assert.That(controller.InputPlanId, Is.EqualTo(plan.planId));

#if UNITY_EDITOR
            Assert.That(EditorApplication.ExecuteMenuItem(ExportMenuPath), Is.True,
                "Same-run Core capture bundle exporter menu was unavailable.");
#endif
            Assert.That(File.Exists(resultPath), Is.True, "Capture collector did not write its execution receipt.");
            CaptureDriverReceipt receipt = JsonUtility.FromJson<CaptureDriverReceipt>(File.ReadAllText(resultPath, Encoding.UTF8));
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.SchemaVersion, Is.EqualTo(1));
            Assert.That(receipt.Classification, Is.EqualTo("CAPTURE_SOURCE_FINGERPRINTED"), receipt.Message);
            Assert.That(receipt.Difficulty, Is.EqualTo(expectedDifficulty));
            Assert.That(receipt.InputKind, Is.EqualTo(M4QaCaptureInputKind.DeterministicInputPlan.ToString()));
            Assert.That(receipt.InputPlanId, Is.EqualTo(plan.planId));
            Assert.That(receipt.InputPlanSha256, Is.EqualTo(Sha256(originalPlanBytes)));
            Assert.That(receipt.CaptureRunId, Does.Match("^[A-Fa-f0-9]{32}$"));
            Assert.That(receipt.SourceBuildId, Is.Not.Null.And.Not.Empty);
            Assert.That(receipt.SourceDeliverySha256, Does.Match("^[A-Fa-f0-9]{64}$"));
            Assert.That(receipt.CaptureDirectory, Is.Not.Null.And.Not.Empty);
        }

        static void WriteShotDiagnostics(M4NightOneGrayboxController controller, string path)
        {
            if (controller == null || string.IsNullOrWhiteSpace(path)) return;

            M4ReplayShotDiagnosticSnapshot snapshot = controller.ReplayShotDiagnostics;
            var receipt = new ShotDiagnosticsReceipt
            {
                SchemaVersion = 2,
                CaptureRunId = snapshot.CaptureRunId,
                InputPlanId = snapshot.InputPlanId,
                IsInputPlanCapture = snapshot.IsInputPlanCapture,
                InputPlanCaptureStatus = controller.InputPlanCaptureStatus.ToString(),
                InputPlanCaptureFailure = controller.InputPlanCaptureFailure,
                ReplayShotDiagnosticsFailure = controller.ReplayShotDiagnosticsFailure,
                InputPlanConsumedTicks = controller.InputPlanConsumedTicks,
                InputPlanTotalTicks = controller.InputPlanTotalTicks,
                FireCommandCount = snapshot.FireCommandCount,
                RaycastAttemptCount = snapshot.RaycastAttemptCount,
                ColliderNameBucketsTruncated = snapshot.ColliderNameBucketsTruncated,
                ExamplesTruncated = snapshot.ExamplesTruncated,
                Director = CaptureDirectorDiagnostic()
            };

            var outcomes = new List<OutcomeBucket>();
            foreach (M4ReplayShotOutcomeCount row in snapshot.OutcomeCounts)
                outcomes.Add(new OutcomeBucket
                {
                    Outcome = row.Outcome.ToString(), Count = row.Count,
                    ExamplesTruncated = row.ExamplesTruncated, Examples = ConvertExamples(row.Examples)
                });
            receipt.OutcomeCounts = outcomes.ToArray();

            var kinds = new List<ColliderKindBucket>();
            foreach (M4ReplayShotColliderKindCount row in snapshot.ColliderKindCounts)
                kinds.Add(new ColliderKindBucket
                {
                    HasGrayboxTag = row.HasGrayboxTag, ColliderKind = row.ColliderKind.ToString(), Count = row.Count,
                    ExamplesTruncated = row.ExamplesTruncated, Examples = ConvertExamples(row.Examples)
                });
            receipt.ColliderKindCounts = kinds.ToArray();

            var names = new List<ColliderNameBucket>();
            foreach (M4ReplayShotColliderNameCount row in snapshot.ColliderNameCounts)
                names.Add(new ColliderNameBucket
                {
                    ColliderName = row.ColliderName, IsOverflowBucket = row.IsOverflowBucket, Count = row.Count,
                    ExamplesTruncated = row.ExamplesTruncated, Examples = ConvertExamples(row.Examples)
                });
            receipt.ColliderNameCounts = names.ToArray();

            var faces = new List<FaceBucket>();
            foreach (M4ReplayShotFaceCount row in snapshot.FaceCounts)
                faces.Add(new FaceBucket
                {
                    HasFace = row.HasFace, Face = row.Face.ToString(), Count = row.Count,
                    ExamplesTruncated = row.ExamplesTruncated, Examples = ConvertExamples(row.Examples)
                });
            receipt.FaceCounts = faces.ToArray();

            string parentDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(parentDirectory)) Directory.CreateDirectory(parentDirectory);
            File.WriteAllText(path, JsonUtility.ToJson(receipt, true), new UTF8Encoding(false));
        }

        static DirectorDiagnostic CaptureDirectorDiagnostic()
        {
            M4DiagnosticBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<M4DiagnosticBootstrap>();
            if (bootstrap == null || bootstrap.CanonicalSession == null ||
                bootstrap.CanonicalSession.BoundDirector == null) return null;

            EnemyDirectorRuntimeSnapshot snapshot = bootstrap.CanonicalSession.BoundDirector.Snapshot();
            ActiveEnemyActor[] actors = UnityEngine.Object.FindObjectsByType<ActiveEnemyActor>(FindObjectsSortMode.None);
            var tickets = new DirectorTicketDiagnostic[snapshot.Tickets.Length];
            for (int index = 0; index < tickets.Length; index++)
            {
                SpawnTicketSnapshot ticket = snapshot.Tickets[index];
                var row = new DirectorTicketDiagnostic
                {
                    TicketId = ticket.Ticket.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    QueueIndex = ticket.Ticket.QueueIndex,
                    Archetype = ticket.Ticket.Archetype.ToString(),
                    Face = ticket.Ticket.Face.ToString(),
                    Lifecycle = ticket.Lifecycle.ToString(),
                    ActiveEntityId = ticket.ActiveEntityId.HasValue ? ticket.ActiveEntityId.Value.ToString() : string.Empty,
                    MovementState = ticket.MovementState.ToString(),
                    BlockReason = ticket.BlockReason.ToString()
                };
                foreach (ActiveEnemyActor actor in actors)
                {
                    if (actor.TicketId != ticket.Ticket.TicketId) continue;
                    row.ActorFound = true;
                    Vector3 position = actor.transform.position;
                    row.ActorPositionX = position.x;
                    row.ActorPositionY = position.y;
                    row.ActorPositionZ = position.z;
                    break;
                }
                tickets[index] = row;
            }

            return new DirectorDiagnostic
            {
                SessionStage = bootstrap.CanonicalSession.Stage.ToString(),
                CompletedNights = bootstrap.CanonicalSession.CompletedNights,
                TicketCount = tickets.Length,
                PendingCount = snapshot.PendingCount,
                PoolWaitingCount = snapshot.PoolWaitingCount,
                ActiveCount = snapshot.ActiveCount,
                ResolvedCount = snapshot.ResolvedCount,
                Tickets = tickets
            };
        }

        static ShotExample[] ConvertExamples(IReadOnlyList<M4ReplayShotDiagnostic> diagnostics)
        {
            var examples = new ShotExample[diagnostics == null ? 0 : diagnostics.Count];
            for (int index = 0; index < examples.Length; index++)
            {
                M4ReplayShotDiagnostic row = diagnostics[index];
                examples[index] = new ShotExample
                {
                    CaptureRunId = row.CaptureRunId,
                    InputPlanId = row.InputPlanId,
                    InputPlanCommandIndex = row.InputPlanCommandIndex,
                    ReplayConsumedTick = row.ReplayConsumedTick,
                    RunFixedTick = row.RunFixedTick,
                    WeaponId = row.WeaponId,
                    RaycastAttempted = row.RaycastAttempted,
                    Outcome = row.Outcome.ToString(),
                    NoRaycastReason = row.NoRaycastReason.ToString(),
                    HasColliderHit = row.HasColliderHit,
                    ColliderName = row.ColliderName,
                    HitDistance = row.HitDistance,
                    HasGrayboxTag = row.HasGrayboxTag,
                    ColliderKind = row.ColliderKind.ToString(),
                    HasFace = row.HasFace,
                    Face = row.Face.ToString(),
                    HasActiveEnemyActor = row.HasActiveEnemyActor,
                    ActorRegistryKeyPresent = row.ActorRegistryKeyPresent,
                    ActorInstanceMatchesRegistry = row.ActorInstanceMatchesRegistry,
                    HasDamageReceipt = row.HasDamageReceipt,
                    DamageStatus = row.DamageStatus.HasValue ? row.DamageStatus.Value.ToString() : string.Empty,
                    DamageBecameResolved = row.DamageBecameResolved
                };
            }
            return examples;
        }

        static string Sha256(byte[] bytes)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) result.Append(digest[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
#endif
}
