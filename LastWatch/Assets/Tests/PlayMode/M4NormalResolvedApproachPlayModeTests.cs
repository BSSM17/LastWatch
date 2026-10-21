using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastWatch.Core.Partner;
using LastWatch.Core.Combat;
using LastWatch.Core.Space;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.Compilation;
#endif

namespace LastWatch.Tests.M4
{
    // Explicit diagnostic filter only; never loaded by the existing four-replay harness.
    public sealed class M4NormalResolvedApproachPlayModeTests
    {
        const int Budget = 4096;
        const int CleanupFrames = 300;
        const double CleanupSeconds = 30;
        readonly List<M4NormalResolvedApproachFixture> pending = new List<M4NormalResolvedApproachFixture>();
        M4NormalResolvedApproachEvidenceWriter writer;
        Preflight preflight;
        string projectRoot, fingerprint;
        int completedCases;
        bool success;

        [Serializable] public sealed class SourceFile
        { public string path; public string sha256; public long length; public string module; }
        [Serializable] public sealed class Preflight
        { public int schemaVersion; public string fingerprint; public SourceFile[] sources; }

        void Open(string suite)
        {
            completedCases = 0;
            success = false;
            writer = null;
            projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            string output = Environment.GetEnvironmentVariable("LASTWATCH_NORMAL_APPROACH_OUTPUT");
            string input = Environment.GetEnvironmentVariable("LASTWATCH_NORMAL_APPROACH_PREFLIGHT");
            string inputSha = Environment.GetEnvironmentVariable("LASTWATCH_NORMAL_APPROACH_PREFLIGHT_SHA256");
            Assert.That(output, Is.Not.Null.And.Not.Empty, "Explicit approved runner output is required.");
            Assert.That(input, Is.Not.Null.And.Not.Empty, "Source preflight is required.");
            Assert.That(M4NormalResolvedApproachEvidenceWriter.Sha(input), Is.EqualTo(inputSha));
            preflight = JsonUtility.FromJson<Preflight>(File.ReadAllText(input));
            Assert.That(preflight.schemaVersion, Is.EqualTo(1));
            Assert.That(preflight.sources, Is.Not.Null.And.Not.Empty);
            fingerprint = preflight.fingerprint;
            Assert.That(fingerprint, Is.Not.Null.And.Not.Empty);
            VerifySources();
            writer = new M4NormalResolvedApproachEvidenceWriter(Path.Combine(output, suite + ".ndjson"));
            writer.Write("run-header", string.Empty, M4NormalResolvedApproachEvidenceWriter.Map(
                "sourceFingerprint", fingerprint, "sourcePreflightSha256", inputSha,
                "compileReceipt", CompilationReceipt(), "diagnosticIterationBudget", Budget,
                "cleanupFrames", CleanupFrames, "cleanupSeconds", CleanupSeconds,
                "caseSelection", Environment.GetEnvironmentVariable("LASTWATCH_NORMAL_APPROACH_PROFILE"),
                "fullGeometryGateAssessment", "INCONCLUSIVE_SUPPORTED_NORMAL_MOVEMENT_ONLY",
                "sourceAssociationMeaning", "UNITY_DECLARED_COMPILATION_INPUTS_AND_CURRENT_BYTES_NOT_CRYPTOGRAPHIC_BUILD_ATTESTATION"));
        }

        void VerifySources()
        {
            foreach (SourceFile file in preflight.sources)
            {
                string path = Path.GetFullPath(Path.Combine(projectRoot, file.path));
                Assert.That(path.StartsWith(projectRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase), Is.True, "Unsafe source path.");
                Assert.That(new FileInfo(path).Length, Is.EqualTo(file.length), file.path);
                Assert.That(M4NormalResolvedApproachEvidenceWriter.Sha(path), Is.EqualTo(file.sha256), file.path);
            }
        }

        object CompilationReceipt()
        {
#if UNITY_EDITOR
            // Read metadata for the two named assemblies only; no recursive filesystem scan.
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var core = assemblies.Single(a => a.name == "LastWatch.Core");
            string[] actual = core.sourceFiles.Select(p => Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, p))).OrderBy(p => p,
                    StringComparer.OrdinalIgnoreCase).ToArray();
            string[] expected = preflight.sources.Where(p => p.module == "LastWatch.Core")
                .Select(p => Path.GetFullPath(Path.Combine(projectRoot, p.path)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Core compilation source membership changed.");
            for (int i = 0; i < actual.Length; i++)
                Assert.That(string.Equals(actual[i], expected[i], StringComparison.OrdinalIgnoreCase), Is.True);
            var test = assemblies.Single(a => a.name == "LastWatch.Tests.M4Replay.PlayMode");
            foreach (string focused in new[] { "M4NormalResolvedApproachPlayModeTests.cs", "M4NormalResolvedApproachEvidenceWriter.cs" })
                Assert.That(test.sourceFiles.Count(p => System.IO.Path.GetFileName(p) == focused), Is.EqualTo(1),
                    "Focused source must belong to the existing test assembly.");
            var loadedCore = typeof(M4NormalResolvedApproachFixture).Assembly;
            var loadedTests = typeof(M4NormalResolvedApproachPlayModeTests).Assembly;
            return M4NormalResolvedApproachEvidenceWriter.Map(
                "coreName", core.name, "coreSources", preflight.sources.Where(p => p.module == core.name)
                    .Select(p => M4NormalResolvedApproachEvidenceWriter.Map("path", p.path, "sha256", p.sha256,
                        "length", p.length)).ToArray(),
                "coreMvid", loadedCore.ManifestModule.ModuleVersionId.ToString("N"),
                "coreAssemblyPath", loadedCore.Location,
                "coreAssemblySha256", M4NormalResolvedApproachEvidenceWriter.Sha(loadedCore.Location),
                "testMvid", loadedTests.ManifestModule.ModuleVersionId.ToString("N"),
                "testAssemblyPath", loadedTests.Location,
                "testAssemblySha256", M4NormalResolvedApproachEvidenceWriter.Sha(loadedTests.Location),
                "testCompilationSourceCount", test.sourceFiles.Length);
#else
            throw new InvalidOperationException("Editor compilation-input receipt unavailable.");
#endif
        }

        M4NormalApproachAssignmentInventorySnapshot Catalog()
        {
            var value = M4NormalResolvedApproachFixture.CaptureSupportedNormalAssignments(fingerprint);
            writer.Write("catalog", string.Empty, value);
            Assert.That(value.SourceDescriptorsAvailable, Is.True, value.SourceUnavailableReason);
            Assert.That(value.Assignments.Count, Is.EqualTo(12));
            Assert.That(value.Assignments.All(v => v.SourceMappingAvailable), Is.True);
            Assert.That(value.Assignments.Select(v => v.Archetype + "/" + v.Face + "/" + v.AssignedAttackPoint)
                .Distinct().Count(), Is.EqualTo(12));
            Assert.That(value.WorldBuilt || value.RuntimeGeometryAvailable || value.SampleCoverageAvailable ||
                value.GuardResolvedPosesAvailable || value.AttackOutcomeAvailable || value.SemanticSamplesAvailable,
                Is.False, "Source inventory must not assert runtime evidence.");
            return value;
        }

        M4NormalResolvedApproachFixture Create(M4NormalApproachAssignmentSnapshot tuple, int budget,
            string observed = null, string expected = null)
        {
            var request = new M4ResolvedApproachFixtureRequest(tuple.Archetype, tuple.Face,
                tuple.AssignedAttackPoint, expected ?? fingerprint, budget);
            var owner = M4NormalResolvedApproachFixture.Create(request, observed ?? fingerprint);
            pending.Add(owner);
            return owner;
        }

        void Row(string kind, string id, M4ResolvedApproachFixtureSnapshot row)
        {
            Assert.That(row.CanonicalContextAvailable || row.MajorAttackPhaseAvailable || row.AttackOutcomeAvailable ||
                row.SemanticSamplesAvailable || row.WholeAuthoredSampleCoverageAvailable, Is.False);
            Assert.That(row.CompletedRunFixedTick.HasValue || row.CompletedNightFixedTick.HasValue ||
                row.NightOrdinal.HasValue, Is.False);
            Assert.That(row.Actor == null || !row.Actor.Registered, Is.True);
            writer.Write(kind, id, M4NormalResolvedApproachEvidenceWriter.Step(row));
        }

        IEnumerator Release(M4NormalResolvedApproachFixture owner, string id)
        {
            owner.Close();
            int frames = 0; double start = Time.realtimeSinceStartupAsDouble;
            var row = owner.Capture();
            Row("release", id, row);
            while (row.AwaitingDestroyCompletion && frames < CleanupFrames &&
                Time.realtimeSinceStartupAsDouble - start < CleanupSeconds)
            {
                yield return null;
                frames++;
                row = owner.Capture();
            }
            Row("cleanup", id, row);
            Assert.That(row.Released, Is.True);
            Assert.That(row.Available, Is.False, "Released row is historical.");
            Assert.That(row.AwaitingDestroyCompletion, Is.False, "CLEANUP_LIMIT_NOT_COMPLETED");
            Assert.That(row.ExceptionType, Is.Empty, "Cleanup exception is not success.");
            pending.Remove(owner);
        }

        [UnityTest, Timeout(850000)]
        public IEnumerator NormalAssignmentMatrix()
        {
            Open("normal-matrix");
            var inventory = Catalog();
            var identities = new HashSet<string>();
            foreach (var tuple in inventory.Assignments)
            {
                string id = tuple.Archetype + "/" + tuple.Face + "/" + tuple.AssignedAttackPoint;
                var owner = Create(tuple, Budget);
                var first = owner.Capture();
                string frozen = M4NormalResolvedApproachEvidenceWriter.Json(first);
                writer.Write("fixture-header", id, first);
                try
                {
                    VerifySources();
                    Assert.That(first.Available, Is.True, first.Reason);
                    Assert.That(identities.Add(first.FixtureSessionId), Is.True);
                    var row = first;
                    int steps = 0;
                    while (!row.Stopped && steps < Budget)
                    {
                        row = owner.AdvanceOnce(fingerprint);
                        steps++;
                        Row("advance", id, row);
                        Assert.That(row.FixtureIteration, Is.EqualTo(steps));
                        Assert.That(row.Available, Is.True, row.Reason);
                        Assert.That(row.Attempt, Is.Not.Null);
                        Assert.That(row.Attempt.InvocationAvailable, Is.True);
                        Assert.That(row.Attempt.CanonicalContextAvailable, Is.False);
                        Assert.That(row.ExceptionType, Is.Empty);
                    }
                    Row("stop", id, row);
                    Assert.That(row.Stopped, Is.True, "DIAGNOSTIC_LIMIT_TERMINAL_NOT_OBSERVED");
                    Assert.That(row.Reason, Is.EqualTo("STANDOFF_REACHED_POSITIVE_GAP_HELD_PRIOR_POSE_NOT_ATTACK_COMPLETION"));
                    Assert.That(row.Attempt.GapMeasurementInvoked, Is.True);
                    Assert.That(row.Attempt.GapMeasurementSucceeded, Is.EqualTo(true));
                    Assert.That(row.Attempt.MeasuredPriorPoseGapMeters.HasValue, Is.True);
                    // No new gap threshold. The outcome is the existing Core movement conclusion.
                    Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(first), Is.EqualTo(frozen));
                }
                finally { owner.Close(); }
                yield return Release(owner, id);
                VerifySources();
                Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(first), Is.EqualTo(frozen));
                writer.Write("case-end", id, M4NormalResolvedApproachEvidenceWriter.Map("outcome", "OBSERVED_SUPPORTED_MOVEMENT",
                    "snapshotImmutable", true, "cleanupCompleted", true));
                completedCases++;
            }
            VerifySources();
            success = true;
        }

        [UnityTest, Timeout(850000)]
        public IEnumerator ClimberNormalResolvedApproach()
        {
            Open("normal-climber");
            var tuple = Catalog().Assignments.Single(v =>
                v.Archetype == EnemyArchetype.Climber && v.Face == FaceId.FaceA);
            string id = tuple.Archetype + "/" + tuple.Face + "/" + tuple.AssignedAttackPoint;
            var owner = Create(tuple, Budget);
            var first = owner.Capture();
            string frozen = M4NormalResolvedApproachEvidenceWriter.Json(first);
            writer.Write("fixture-header", id, first);
            try
            {
                VerifySources();
                Assert.That(first.Available, Is.True, first.Reason);
                Assert.That(first.Actor.ClimberCenterAdaptationApplied, Is.True);
                Assert.That(first.Actor.ClimberCenterShapeCoverage, Is.EqualTo(
                    "BODY_CAPSULE_ONLY_NULL_TEMPLATE; FULL_SOLID_ATTACK_SHOT_COVERAGE_UNAVAILABLE"));
                Assert.That(first.Actor.ClimberCenterSupportInstanceId.HasValue, Is.True);
                Assert.That(first.Colliders.Count(v => v.Role == "AUTHORED_SUPPORT_LEG_PAIR_INPUT" &&
                    v.InstanceId == first.Actor.ClimberCenterSupportInstanceId.Value), Is.EqualTo(1));
                Assert.That(first.Actor.ClimberCenterSupportNormal.HasValue &&
                    first.Actor.ClimberCenterStartPlaneDistanceMeters.HasValue, Is.True);
                Assert.That(first.Actor.AppliedAuthoredWaypoints.Count, Is.EqualTo(first.AuthoredBaseWaypoints.Count));
                int climbStart = first.Actor.ClimbWaypointStartIndex;
                Assert.That(climbStart, Is.GreaterThan(0).And.LessThan(first.AuthoredBaseWaypoints.Count));
                for (int i = 0; i < climbStart; i++)
                    Assert.That(first.Actor.AppliedAuthoredWaypoints[i].Equals(first.AuthoredBaseWaypoints[i]), Is.True);
                Assert.That(Enumerable.Range(climbStart, first.AuthoredBaseWaypoints.Count - climbStart)
                    .Any(i => !first.Actor.AppliedAuthoredWaypoints[i].Equals(first.AuthoredBaseWaypoints[i])), Is.True);
                var row = first;
                int steps = 0;
                while (!row.Stopped && steps < Budget)
                {
                    row = owner.AdvanceOnceWithSupportTrace(fingerprint);
                    steps++;
                    Row("advance", id, row);
                    Assert.That(row.FixtureIteration, Is.EqualTo(steps));
                    Assert.That(row.Available, Is.True, row.Reason);
                    Assert.That(row.Attempt, Is.Not.Null);
                    Assert.That(row.Attempt.InvocationAvailable, Is.True);
                    Assert.That(row.Attempt.CanonicalContextAvailable, Is.False);
                    Assert.That(row.ExceptionType, Is.Empty);
                    RequireCompleteTrace(row);
                }
                Row("stop", id, row);
                Assert.That(row.Stopped, Is.True, "DIAGNOSTIC_LIMIT_TERMINAL_NOT_OBSERVED");
                Assert.That(row.Reason, Is.EqualTo("STANDOFF_REACHED_POSITIVE_GAP_HELD_PRIOR_POSE_NOT_ATTACK_COMPLETION"));
                Assert.That(row.Attempt.GapMeasurementInvoked, Is.True);
                Assert.That(row.Attempt.GapMeasurementSucceeded, Is.EqualTo(true));
                Assert.That(row.Attempt.MeasuredPriorPoseGapMeters.HasValue, Is.True);
                Assert.That(row.Attempt.MeasuredPriorPoseGapMeters.Value, Is.GreaterThan(0));
                Assert.That(row.Attempt.TransformApplied, Is.False);
                Assert.That(row.Actor.NextWaypointIndex, Is.GreaterThan(97), "HISTORICAL_STALL_WAYPOINT_NOT_PASSED");
                Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(first), Is.EqualTo(frozen));
            }
            finally { owner.Close(); }
            yield return Release(owner, id);
            VerifySources();
            Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(first), Is.EqualTo(frozen));
            writer.Write("case-end", id, M4NormalResolvedApproachEvidenceWriter.Map("outcome", "OBSERVED_SUPPORTED_MOVEMENT",
                "snapshotImmutable", true, "cleanupCompleted", true));
            completedCases++;
            success = true;
        }

        // Exact copied-value comparison only; no Unity approximate vector operators or new tolerance.
        static bool IsSingleNoDisplacement(M4MovementAttemptSnapshot attempt)
        {
            return attempt.ProposedPosition.HasValue && attempt.SupportResolvedPosition.HasValue &&
                !attempt.ProposedPosition.Value.Equals(attempt.PriorPosition) &&
                attempt.SupportResolvedPosition.Value.Equals(attempt.PriorPosition) &&
                attempt.FinalPosition.Equals(attempt.PriorPosition) &&
                attempt.WaypointIndexBefore == attempt.WaypointIndexAfter;
        }

        static void RequireCompleteTrace(M4ResolvedApproachFixtureSnapshot row)
        {
            var attempt = row.Attempt;
            Assert.That(attempt.SupportTraceRequested && attempt.SupportTraceAvailable, Is.True,
                attempt.SupportTraceUnavailableReason);
            var trace = attempt.SupportTrace;
            Assert.That(trace, Is.Not.Null);
            Assert.That(trace.Domain, Is.EqualTo("EXISTING_SUPPORT_GUARD_ACTUAL_VISITS_NO_EXTRA_QUERY"));
            Assert.That(trace.QueryMeaning, Is.EqualTo("COMPLETED_IS_RETURNED; OVERLAP_REPORTED_IS_PHYSICS_BOOLEAN_NOT_QUERY_SUCCESS"));
            Assert.That(trace.Complete, Is.True, "PARTIAL_TRACE_NOT_COLLECTION_SUCCESS");
            Assert.That(trace.GuardResult, Is.EqualTo(true));
            Assert.That(trace.CaptureFailureReason, Is.Empty);
            Assert.That(trace.OriginalExceptionType, Is.Empty);
            Assert.That(trace.ResolvedPosition.HasValue && attempt.SupportResolvedPosition.HasValue, Is.True);
            Assert.That(trace.ResolvedPosition.Value.Equals(attempt.SupportResolvedPosition.Value), Is.True);
            var supports = row.Colliders.Where(v => v.Role == "AUTHORED_SUPPORT_LEG_PAIR_INPUT").ToArray();
            Assert.That(trace.SupportInputCount, Is.EqualTo(supports.Length));
            Assert.That(trace.Visits.Count, Is.GreaterThan(0).And.LessThanOrEqualTo(trace.MaximumPotentialQueryVisits));
            foreach (var visit in trace.Visits)
            {
                Assert.That(visit.SupportInputIndex, Is.InRange(0, trace.SupportInputCount - 1));
                Assert.That(visit.MoverColliderInstanceId, Is.EqualTo(attempt.MoverColliderInstanceId));
                Assert.That(visit.SupportColliderInstanceId, Is.EqualTo(supports[visit.SupportInputIndex].InstanceId));
                if (visit.Phase == "CORRECTION")
                    Assert.That(visit.PassOrdinal, Is.InRange(0, trace.ConfiguredCorrectionPasses - 1));
                else { Assert.That(visit.Phase, Is.EqualTo("FINAL_VERIFICATION")); Assert.That(visit.PassOrdinal, Is.Null); }
                Assert.That(visit.PhysicsQueryCompleted, Is.EqualTo(visit.PhysicsQueryInvoked));
                if (visit.PhysicsQueryInvoked)
                {
                    Assert.That(visit.EligibilityReason, Is.EqualTo("SOLID_ENABLED_PAIR"));
                    Assert.That(visit.OverlapReported.HasValue, Is.True); // False is a normal no-overlap return.
                    Assert.That(visit.DepthValueFinite.HasValue && visit.DirectionValueFinite.HasValue, Is.True);
                    Assert.That(visit.DepthMeters.HasValue, Is.EqualTo(visit.DepthValueFinite.Value));
                    Assert.That(visit.Direction.HasValue, Is.EqualTo(visit.DirectionValueFinite.Value));
                }
                else
                {
                    Assert.That(visit.OverlapReported, Is.Null);
                    Assert.That(visit.DepthMeters, Is.Null); Assert.That(visit.Direction, Is.Null);
                    Assert.That(visit.DepthValueFinite, Is.Null); Assert.That(visit.DirectionValueFinite, Is.Null);
                    Assert.That(visit.CorrectionApplied, Is.False);
                }
                if (visit.CorrectionApplied)
                {
                    Assert.That(visit.Phase, Is.EqualTo("CORRECTION"));
                    Assert.That(visit.OverlapReported, Is.EqualTo(true));
                    Assert.That(visit.DepthValueFinite, Is.EqualTo(true));
                    Assert.That(visit.DirectionValueFinite, Is.EqualTo(true));
                    Assert.That(visit.SeparationMarginMeters.HasValue, Is.True);
                }
                else Assert.That(visit.SeparationMarginMeters, Is.Null);
            }
        }

        [UnityTest, Timeout(850000)]
        public IEnumerator ClimberSupportTraceObservation()
        {
            Assert.That(Environment.GetEnvironmentVariable("LASTWATCH_NORMAL_APPROACH_PROFILE"),
                Is.EqualTo("SupportTraceClimberOnly"), "Explicit one-tuple diagnostic profile required.");
            Open("climber-support-trace");
            var tuple = Catalog().Assignments.Single(v => v.Archetype == EnemyArchetype.Climber && v.Face == FaceId.FaceA);
            string id = tuple.Archetype + "/" + tuple.Face + "/" + tuple.AssignedAttackPoint;
            writer.Write("diagnostic-intent", id, M4NormalResolvedApproachEvidenceWriter.Map(
                "selection", "CORE_PUBLIC_CATALOG_UNIQUE_CLIMBER_FACE_A", "caseSelection", "SupportTraceClimberOnly",
                "standOffAssessment", "NOT_OBSERVED", "persistenceAssessment", "NOT_ESTABLISHED",
                "fullGeometryGateAssessment", "INCONCLUSIVE", "excludedHistoricalCases", "COMPLETED_WALKER_RUNNER_AND_PUBLIC_CONTRACT"));
            var owner = Create(tuple, Budget);
            var first = owner.Capture();
            string frozen = M4NormalResolvedApproachEvidenceWriter.Json(first);
            M4ResolvedApproachFixtureSnapshot selected = null;
            string selectedFrozen = null;
            writer.Write("fixture-header", id, first);
            try
            {
                VerifySources();
                Assert.That(first.Available, Is.True, first.Reason);
                var row = first;
                for (int steps = 1; steps <= Budget; steps++)
                {
                    row = owner.AdvanceOnceWithSupportTrace(fingerprint);
                    Row("advance", id, row); // Preserve actual partial/failed receipt before assertions.
                    Assert.That(row.FixtureIteration, Is.EqualTo(steps));
                    Assert.That(row.Available, Is.True, row.Reason);
                    Assert.That(row.ExceptionType, Is.Empty);
                    Assert.That(row.Attempt, Is.Not.Null);
                    Assert.That(row.Attempt.InvocationAvailable, Is.True);
                    RequireCompleteTrace(row);
                    Assert.That(row.Stopped, Is.False, "UNEXPECTED_TERMINAL_BEFORE_DIAGNOSTIC_OBSERVATION");
                    if (IsSingleNoDisplacement(row.Attempt))
                    {
                        selected = row;
                        selectedFrozen = M4NormalResolvedApproachEvidenceWriter.Json(row);
                        Row("observation-stop", id, row);
                        break;
                    }
                }
                if (selected == null) Row("observation-limit", id, row);
                Assert.That(selected, Is.Not.Null, "DIAGNOSTIC_LIMIT_SUPPORT_TRACE_OBSERVATION_NOT_FOUND");
                Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(first), Is.EqualTo(frozen));
            }
            finally { owner.Close(); }
            yield return Release(owner, id);
            VerifySources();
            Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(first), Is.EqualTo(frozen));
            Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(selected), Is.EqualTo(selectedFrozen));
            writer.Write("case-end", id, M4NormalResolvedApproachEvidenceWriter.Map(
                "outcome", "OBSERVED_SINGLE_SUPPORT_RESOLVED_NO_DISPLACEMENT", "snapshotImmutable", true,
                "cleanupCompleted", true, "standOffAssessment", "NOT_OBSERVED", "persistenceAssessment", "NOT_ESTABLISHED",
                "fullGeometryGateAssessment", "INCONCLUSIVE"));
            completedCases = 1;
            success = true;
        }

        [UnityTest, Timeout(850000)]
        public IEnumerator PublicContractAndLifetime()
        {
            Open("public-contract");
            var tuples = Catalog().Assignments;
            var tuple = tuples[0];
            var other = tuples.First(v => v.AssignedAttackPoint != tuple.AssignedAttackPoint);
            // Public request-only negative inputs; no actor/collider/private-state mutation.
            foreach (var sample in new[] { "create-mismatch", "invalid-budget", "invalid-target" })
            {
                M4NormalResolvedApproachFixture owner;
                if (sample == "invalid-target")
                {
                    owner = M4NormalResolvedApproachFixture.Create(new M4ResolvedApproachFixtureRequest(
                        tuple.Archetype, tuple.Face, other.AssignedAttackPoint, fingerprint, Budget), fingerprint);
                    pending.Add(owner);
                }
                else owner = Create(tuple, sample == "invalid-budget" ? 0 : Budget,
                    sample == "create-mismatch" ? fingerprint + ":mismatch" : fingerprint);
                try
                {
                    var row = owner.Capture(); Row("rejected", sample, row);
                    Assert.That(row.Available, Is.False); Assert.That(row.Stopped, Is.True);
                    Assert.That(row.FixtureIteration, Is.Zero); Assert.That(row.Actor, Is.Null);
                    string reason = sample == "create-mismatch" ? "SOURCE_FINGERPRINT_UNAVAILABLE_OR_MISMATCH"
                        : sample == "invalid-budget" ? "UNSUPPORTED_ASSIGNMENT_OR_DIAGNOSTIC_BUDGET" : "UNSUPPORTED_ASSIGNMENT_TARGET";
                    Assert.That(row.Reason, Is.EqualTo(reason));
                }
                finally { owner.Close(); }
                yield return Release(owner, sample);
                writer.Write("case-end", sample, M4NormalResolvedApproachEvidenceWriter.Map("outcome", "OBSERVED_PUBLIC_REJECTION",
                    "snapshotImmutable", true, "cleanupCompleted", true)); completedCases++;
            }
            var limit = Create(tuple, 1);
            try
            {
                Row("fixture-header", "budget-limit", limit.Capture());
                Row("advance", "budget-limit", limit.AdvanceOnce(fingerprint));
                var row = limit.AdvanceOnce(fingerprint); Row("limit", "budget-limit", row);
                Assert.That(row.Reason, Is.EqualTo("DIAGNOSTIC_ITERATION_BUDGET_EXHAUSTED_NOT_GAMEPLAY_OUTCOME"));
                Assert.That(row.FixtureIteration, Is.EqualTo(1)); Assert.That(row.Available, Is.False);
            }
            finally { limit.Close(); }
            yield return Release(limit, "budget-limit");
            writer.Write("case-end", "budget-limit", M4NormalResolvedApproachEvidenceWriter.Map("outcome", "OBSERVED_DIAGNOSTIC_LIMIT",
                "snapshotImmutable", true, "cleanupCompleted", true)); completedCases++;

            var active = Create(tuple, Budget);
            var historical = active.Capture(); string before = M4NormalResolvedApproachEvidenceWriter.Json(historical);
            try
            {
                Row("fixture-header", "owner-lifecycle", historical);
                Assert.That(historical.Available, Is.True);
                var blocked = Create(tuple, Budget);
                try
                {
                    var row = blocked.Capture(); Row("blocked-owner", "owner-lifecycle", row);
                    Assert.That(row.Reason, Is.EqualTo("PREVIOUS_FIXTURE_ACTIVE_OR_AWAITING_DESTROY"));
                }
                finally { blocked.Close(); }
                yield return Release(blocked, "blocked-owner");
                var mismatch = active.AdvanceOnce(fingerprint + ":mismatch");
                Row("source-stop", "owner-lifecycle", mismatch);
                Assert.That(mismatch.Reason, Is.EqualTo("SOURCE_FINGERPRINT_UNAVAILABLE_OR_MISMATCH"));
                Assert.That(mismatch.FixtureIteration, Is.Zero);
                Assert.That(mismatch.Available, Is.False); Assert.That(mismatch.Stopped, Is.True);
                active.Close();
                var closing = active.Capture(); Row("release", "owner-lifecycle", closing);
                // Observe the real deferred-cleanup window; never manufacture it or delay destruction.
                if (closing.AwaitingDestroyCompletion)
                {
                    var awaitingBlocked = Create(tuple, Budget);
                    try
                    {
                        var row = awaitingBlocked.Capture(); Row("blocked-owner", "awaiting-owner", row);
                        Assert.That(row.Reason, Is.EqualTo("PREVIOUS_FIXTURE_ACTIVE_OR_AWAITING_DESTROY"));
                        Assert.That(row.Available, Is.False); Assert.That(row.Stopped, Is.True);
                    }
                    finally { awaitingBlocked.Close(); }
                    yield return Release(awaitingBlocked, "awaiting-owner");
                }
            }
            finally { active.Close(); }
            yield return Release(active, "owner-lifecycle");
            var fresh = Create(tuple, Budget);
            try
            {
                var row = fresh.Capture(); Row("fresh-owner", "owner-lifecycle", row);
                Assert.That(row.Available, Is.True, row.Reason);
                Assert.That(row.FixtureSessionId, Is.Not.EqualTo(historical.FixtureSessionId));
                Assert.That(row.ActorGeneration, Is.Not.EqualTo(historical.ActorGeneration));
                Assert.That(M4NormalResolvedApproachEvidenceWriter.Json(historical), Is.EqualTo(before));
            }
            finally { fresh.Close(); }
            yield return Release(fresh, "fresh-owner");
            writer.Write("case-end", "owner-lifecycle", M4NormalResolvedApproachEvidenceWriter.Map("outcome", "OBSERVED_LIFETIME_CONTRACT",
                "snapshotImmutable", true, "cleanupCompleted", true)); completedCases++;
            VerifySources(); success = true;
        }

        [UnityTearDown]
        public IEnumerator PreserveFailureAndRelease()
        {
            // Assertion/serialization failures still release every owner; incomplete evidence remains FAIL.
            foreach (var owner in pending.ToArray())
            {
                owner.Close();
                int frames = 0; double start = Time.realtimeSinceStartupAsDouble;
                while (owner.Capture().AwaitingDestroyCompletion && frames++ < CleanupFrames &&
                    Time.realtimeSinceStartupAsDouble - start < CleanupSeconds) yield return null;
                if (writer != null && !writer.Closed) Row("teardown-cleanup", "failure", owner.Capture());
            }
            pending.Clear();
            if (writer != null && !writer.Closed)
            {
                try { writer.Seal(success ? "BOUNDED_DIAGNOSTIC_COMPLETE" : "FAIL_OR_LIMIT",
                    success ? "SUPPORTED_DOMAIN_ONLY" : "ASSERTION_SETUP_SERIALIZATION_OR_CLEANUP_STOP", completedCases); }
                finally { writer.Dispose(); }
            }
        }
    }
}
