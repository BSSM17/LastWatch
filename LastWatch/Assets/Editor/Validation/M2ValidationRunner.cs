using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Safety;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using LastWatch.Core.Traversal;
using UnityEditor;
using UnityEngine;

namespace LastWatch.Editor.Validation
{
    public static class M2ValidationRunner
    {
        [Serializable]
        private sealed class Summary
        {
            public string unityVersion;
            public string generatedUtc;
            public int total;
            public int passed;
            public int failed;
            public Gate[] gates;
        }

        [Serializable]
        private sealed class Gate
        {
            public string id;
            public bool passed;
            public string classification;
            public string detail;
        }

        private static readonly List<Gate> Gates = new List<Gate>();
        private static readonly StringBuilder LosCsv = new StringBuilder();

        [MenuItem("LastWatch/Validation/Run M2 Gates")]
        public static void RunFromCommandLine()
        {
            Gates.Clear();
            LosCsv.Clear();
            LosCsv.AppendLine("fixture,target_kind,target_id,sample_index,station,is_clear,blocking_collider");

            Execute("M2-LOS-01", ValidateNormalAttackPointLos);
            Execute("M2-LOS-02", ValidateUnavailableAttackPointLos);
            Execute("M2-LOS-03", ValidateRouteLos);
            Execute("M2-SAFE-01", ValidateTargetRejection);
            Execute("M2-SAFE-02", ValidateOneThousandDamageAttempts);
            Execute("M2-TRAV-01", ValidateDescent);
            Execute("M2-TRAV-02", ValidateAscent);
            Execute("M2-TRAV-03", () => ValidateInterrupt(false));
            Execute("M2-TRAV-04", () => ValidateInterrupt(true));
            Execute("M2-SMOKE-01", ValidatePathSmoke);

            string evidence = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tests",
                "LastWatch", "evidence", "M2"));
            Directory.CreateDirectory(evidence);
            string summaryPath = Path.Combine(evidence, "m2-validation-summary.json");
            string csvPath = Path.Combine(evidence, "m2-los-samples.csv");
            var summary = new Summary
            {
                unityVersion = Application.unityVersion,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                total = Gates.Count,
                passed = Gates.Count(x => x.passed),
                failed = Gates.Count(x => !x.passed),
                gates = Gates.ToArray()
            };
            File.WriteAllText(summaryPath, JsonUtility.ToJson(summary, true), new UTF8Encoding(false));
            File.WriteAllText(csvPath, LosCsv.ToString(), new UTF8Encoding(false));
            Debug.Log($"LASTWATCH_M2_VALIDATION total={summary.total} passed={summary.passed} failed={summary.failed} " +
                      $"summary={summaryPath} los={csvPath}");
            if (summary.failed != 0)
                throw new InvalidOperationException($"LastWatch M2 validation failed {summary.failed}/{summary.total} gates.");
        }

        private static string ValidateNormalAttackPointLos()
        {
            TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
            return WithGraybox(fixture, root =>
            {
                int attackCount = 0;
                int lineCount = 0;
                foreach (AttackCapsuleDescriptor attack in fixture.AttackPoints.Where(x => x.IsAvailable))
                {
                    attackCount++;
                    int clear = 0;
                    foreach (StationDescriptor station in fixture.Stations.Where(x => x.IsAvailable))
                    {
                        if (!TowerLineOfSightContract.IsCanonicalPair(station.Id, attack.Id)) continue;
                        LosProbeResult[] probes = TowerLosValidator.ValidateAttackPoint("Normal", root, station, attack);
                        Record("Normal", "attack", attack.Id.ToString(), station.Id, probes);
                        if (probes.Any(x => x.IsClear)) clear++;
                    }
                    Require(clear >= 2, $"{attack.Id} clear stations={clear}, expected >=2");
                    lineCount += clear;
                }
                Require(attackCount == 6, $"attack points={attackCount}, expected 6");
                return $"attackPoints={attackCount}; clearStationLines={lineCount}";
            });
        }

        private static string ValidateUnavailableAttackPointLos()
        {
            int fixtures = 0;
            int remainingPoints = 0;
            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
            {
                TowerGeometryFixture fixture = TowerGeometryFixture.CreateOneFaceUnavailable(face);
                WithGraybox(fixture, root =>
                {
                    AttackCapsuleDescriptor[] attacks = fixture.AttackPoints.Where(x => x.IsAvailable).ToArray();
                    Require(attacks.Length == 4, $"{face} remaining attack points={attacks.Length}, expected 4");
                    foreach (AttackCapsuleDescriptor attack in attacks)
                    {
                        bool clear = false;
                        foreach (StationDescriptor station in fixture.Stations.Where(x => x.IsAvailable))
                        {
                            if (!TowerLineOfSightContract.IsCanonicalPair(station.Id, attack.Id)) continue;
                            LosProbeResult[] probes = TowerLosValidator.ValidateAttackPoint(face.ToString(), root, station, attack);
                            Record(face.ToString(), "attack", attack.Id.ToString(), station.Id, probes);
                            clear |= probes.Any(x => x.IsClear);
                        }
                        Require(clear, $"{face}/{attack.Id} has no clear fallback LOS");
                    }
                    remainingPoints += attacks.Length;
                    return string.Empty;
                });
                fixtures++;
            }
            return $"fixtures={fixtures}; remainingAttackPoints={remainingPoints}";
        }

        private static string ValidateRouteLos()
        {
            TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
            return WithGraybox(fixture, root =>
            {
                int routes = 0;
                int samples = 0;
                foreach (RouteDescriptor route in fixture.Routes.Where(x => x.IsAvailable))
                {
                    Vector3[] points = route.Sample(0.5f);
                    for (int index = 0; index < points.Length; index++)
                    {
                        bool clear = false;
                        foreach (StationDescriptor station in fixture.Stations.Where(x => x.IsAvailable))
                        {
                            LosProbeResult probe = TowerLosValidator.ValidateRouteSample(
                                "Normal", root, station, route, index, points[index]);
                            Record("Normal", "route", route.Id.ToString(), station.Id, new[] { probe });
                            clear |= probe.IsClear;
                        }
                        Require(clear, $"{route.Id} sample={index}/{points.Length - 1} has zero upper LOS");
                    }
                    samples += points.Length;
                    routes++;
                }
                Require(routes == 9, $"routes={routes}, expected 9");
                return $"routes={routes}; samples={samples}; spacing=0.5m";
            });
        }

        private static string ValidateTargetRejection()
        {
            ActorSpatialSnapshot[] actors =
            {
                UpperActor(new EntityId(10), DamageableKind.Player),
                UpperActor(new EntityId(20), DamageableKind.Partner)
            };
            int rejected = actors.Count(actor => UpperSafeZoneSafety.EvaluateEnemyTargetAssignment(actor) ==
                                                 EnemyTargetAssignmentStatus.RejectedUpperSafeZone);
            Require(rejected == actors.Length, $"rejected={rejected}, attempts={actors.Length}");
            return $"attempts={actors.Length}; rejected={rejected}; assigned=0";
        }

        private static string ValidateOneThousandDamageAttempts()
        {
            EntityId playerId = new EntityId(10);
            RunState state = RunStateFactory.CreateFixture(new RunStateFixture
            {
                RunValue = 3,
                FixedTick = 100,
                NextEventSequence = 1,
                Damageables = new[]
                {
                    new DamageableFixture { EntityValue = playerId.Value, Kind = DamageableKind.Player, HitPoints = 100 }
                }
            });
            ActorSpatialSnapshot actor = UpperActor(playerId, DamageableKind.Player);
            var resolver = new DamageResolver(state);
            int rejected = 0;
            for (int index = 0; index < 1000; index++)
            {
                DamageReceipt receipt = resolver.ApplyEnemyAttack(new DamagePacket(state.IssueEventSequence(),
                    state.FixedTick, new EntityId(100), new EntityId(101), playerId, DamageKind.Melee, 1f,
                    actor.Position, Vector3.zero, new FireSequenceId((ulong)index + 1)), actor);
                if (receipt.Status == DamageApplyStatus.RejectedUpperSafeZone) rejected++;
                Require(receipt.HitPointsBefore == receipt.HitPointsAfter, $"attempt={index} changed HP");
            }
            state.TryGetDamageable(playerId, out DamageableSnapshot snapshot);
            Require(rejected == 1000 && snapshot.HitPoints == 100f, $"rejected={rejected}; hp={snapshot.HitPoints}");
            return $"attempts=1000; rejected={rejected}; actualDamage=0; finalHp={snapshot.HitPoints}";
        }

        private static string ValidateDescent()
        {
            var controller = new TraversalController();
            Require(controller.BeginNormalDescent() == TraversalCommandStatus.Started, "descent did not start");
            for (int tick = 1; tick < 180; tick++)
                Require(controller.AdvanceOneTick(true, false) == TraversalStepStatus.InProgress,
                    $"descent completed early at tick {tick}");
            Require(controller.AdvanceOneTick(true, false) == TraversalStepStatus.Completed, "descent not complete at tick 180");
            Require(controller.Snapshot.Zone == LocationZone.GroundDanger, "descent exit Zone mismatch");
            return "start=UpperSafeZone; ticks=180; exit=GroundDanger/GroundLanding";
        }

        private static string ValidateAscent()
        {
            TraversalController controller = CreateAtGround();
            Require(controller.BeginNormalAscent() == TraversalCommandStatus.Started, "ascent did not start");
            for (int tick = 1; tick < 360; tick++)
                Require(controller.AdvanceOneTick(true, false) == TraversalStepStatus.InProgress,
                    $"ascent completed early at tick {tick}");
            Require(controller.AdvanceOneTick(true, false) == TraversalStepStatus.Completed, "ascent not complete at tick 360");
            Require(controller.Snapshot.Zone == LocationZone.UpperSafeZone, "ascent exit Zone mismatch");
            return "start=GroundDanger; ticks=360; exit=UpperSafeZone/UpperDeck";
        }

        private static string ValidateInterrupt(bool largeDamage)
        {
            var controller = new TraversalController();
            controller.BeginNormalDescent();
            for (int tick = 0; tick < 82; tick++) controller.AdvanceOneTick(true, false);
            Require(controller.Snapshot.LastPassedNode == TraversalPlatformNodeId.MidAccess,
                "expected MidAccess before interrupt");
            TraversalStepStatus status = controller.AdvanceOneTick(largeDamage, largeDamage);
            TraversalStepStatus expected = largeDamage
                ? TraversalStepStatus.InterruptedLargeDamage
                : TraversalStepStatus.InterruptedInputReleased;
            Require(status == expected, $"interrupt status={status}, expected={expected}");
            Require(controller.Snapshot.State == TraversalState.None &&
                    controller.Snapshot.LastPassedNode == TraversalPlatformNodeId.MidAccess &&
                    controller.Snapshot.Zone == LocationZone.LowerAccess, "interrupt placement mismatch");
            return $"cause={(largeDamage ? "largeDamage" : "inputRelease")}; node=MidAccess; zone=LowerAccess";
        }

        private static string ValidatePathSmoke()
        {
            int fixtures = 0;
            int routes = 0;
            int samples = 0;
            var definitions = new TowerGeometryFixture[]
            {
                TowerGeometryFixture.CreateNormal(),
                TowerGeometryFixture.CreateOneFaceUnavailable(FaceId.FaceA),
                TowerGeometryFixture.CreateOneFaceUnavailable(FaceId.FaceB),
                TowerGeometryFixture.CreateOneFaceUnavailable(FaceId.FaceC)
            };
            foreach (TowerGeometryFixture fixture in definitions)
            {
                M2PathSmokeSnapshot result = M2PathSmokeProbe.Execute(fixture);
                Require(result.AllAvailableRoutesCompleted, $"fixture={fixture.UnavailableFace} incomplete routes");
                Require(result.TechnicalPathOrColliderFailureCount == 0 && result.FallCount == 0 && result.DeathCount == 0,
                    $"fixture={fixture.UnavailableFace} technical={result.TechnicalPathOrColliderFailureCount} " +
                    $"falls={result.FallCount} deaths={result.DeathCount}");
                fixtures++;
                routes += result.Routes.Count;
                samples += result.Routes.Sum(x => x.TotalSamples);
            }
            return $"fixtures={fixtures}; routes={routes}; samples={samples}; technicalFailures=0; falls=0; deaths=0";
        }

        private static string WithGraybox(TowerGeometryFixture fixture, Func<Transform, string> validate)
        {
            var root = new GameObject("M2ValidationRunner_Graybox");
            try
            {
                root.AddComponent<M2GrayboxBuilder>().Build(fixture);
                Physics.SyncTransforms();
                return validate(root.transform);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Record(string fixture, string kind, string target, StationId station,
            IEnumerable<LosProbeResult> probes)
        {
            foreach (LosProbeResult probe in probes)
            {
                LosCsv.Append(fixture).Append(',').Append(kind).Append(',').Append(target).Append(',')
                    .Append(probe.SampleIndex).Append(',').Append(station).Append(',')
                    .Append(probe.IsClear ? "true" : "false").Append(',').Append(probe.BlockingCollider).AppendLine();
            }
        }

        private static void Execute(string id, Func<string> action)
        {
            try
            {
                Gates.Add(new Gate { id = id, passed = true, classification = "PASS", detail = action() });
            }
            catch (Exception exception)
            {
                Gates.Add(new Gate
                {
                    id = id,
                    passed = false,
                    classification = exception is InvalidOperationException ? "CORE_DEFECT" : "TOOLS_DEFECT",
                    detail = exception.GetType().Name + ": " + exception.Message
                });
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static ActorSpatialSnapshot UpperActor(EntityId id, DamageableKind kind)
        {
            return new ActorSpatialSnapshot(id, kind, LocationZone.UpperSafeZone,
                new Vector3(0f, TowerGeometryFixture.UpperDeckY, 0f));
        }

        private static TraversalController CreateAtGround()
        {
            var controller = new TraversalController();
            controller.BeginNormalDescent();
            for (int tick = 0; tick < 180; tick++) controller.AdvanceOneTick(true, false);
            return controller;
        }
    }
}
