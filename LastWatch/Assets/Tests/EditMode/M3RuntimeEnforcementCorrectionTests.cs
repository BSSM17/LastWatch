using System;
using System.Collections.Generic;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Tests
{
    [TestFixture]
    public sealed class M3RuntimeEnforcementCorrectionTests
    {
        [TestCase(ActivationBlockReason.TotalPressure)]
        [TestCase(ActivationBlockReason.FaceEntityCount)]
        [TestCase(ActivationBlockReason.FacePressure)]
        [TestCase(ActivationBlockReason.BreakerCount)]
        [TestCase(ActivationBlockReason.NewSpawnPressureWindow)]
        public void RuntimeActivationCaps_OneOverCapRemainsPendingWithoutIdentityOrCursorAdvance(
            ActivationBlockReason expected)
        {
            M3TicketFixtureDefinition[] definitions = DefinitionsFor(expected);
            var runtime = new EnemyDirectorRuntime(M3RuntimeFixtureFactory.CreateQueue(definitions));
            var pool = new EnemyPool(0x5000UL);
            int accepted = definitions.Length - 1;
            for (int index = 0; index < accepted; index++)
            {
                int tick = expected == ActivationBlockReason.NewSpawnPressureWindow ? 0 : index * 501;
                Assert.That(runtime.TryActivateNextDetailed(tick, pool).Accepted, Is.True, $"setup index={index}");
            }

            int attemptTick = expected == ActivationBlockReason.NewSpawnPressureWindow ? 0 : accepted * 501;
            EnemyDirectorRuntimeSnapshot before = runtime.Snapshot();
            ActivationAttemptResult result = runtime.TryActivateNextDetailed(attemptTick, pool);
            EnemyDirectorRuntimeSnapshot after = runtime.Snapshot();

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(expected));
            Assert.That(result.CursorAfter, Is.EqualTo(result.CursorBefore));
            Assert.That(result.EntityId, Is.Null);
            Assert.That(result.ActualSpawnTick, Is.Null);
            Assert.That(result.LifecycleAfter, Is.EqualTo(result.LifecycleBefore));
            Assert.That(after.ActiveCount, Is.EqualTo(before.ActiveCount));
            Assert.That(after.Tickets[result.CursorAfter].Ticket.TicketId,
                Is.EqualTo(before.Tickets[result.CursorBefore].Ticket.TicketId));
            Assert.That(after.Tickets[result.CursorAfter].Ticket.Archetype,
                Is.EqualTo(before.Tickets[result.CursorBefore].Ticket.Archetype));
            Assert.That(after.Tickets[result.CursorAfter].Ticket.Face,
                Is.EqualTo(before.Tickets[result.CursorBefore].Ticket.Face));
        }

        [Test]
        public void HighThreatWindup_ThirdCandidateIsRejectedAndPriorStateIsPreserved()
        {
            EnemyDirectorQueue queue = M3RuntimeFixtureFactory.CreateQueue(
                D(EnemyArchetype.Breaker, FaceId.FaceA),
                D(EnemyArchetype.Climber, FaceId.FaceB),
                D(EnemyArchetype.Breaker, FaceId.FaceC));
            var runtime = new EnemyDirectorRuntime(queue);
            var pool = new EnemyPool(0x6000UL);
            var ids = new List<EntityId>();
            for (int index = 0; index < 3; index++)
            {
                ActivationAttemptResult activation = runtime.TryActivateNextDetailed(index * 501, pool);
                Assert.That(activation.Accepted, Is.True);
                ids.Add(activation.EntityId.Value);
            }
            Assert.That(runtime.TryBeginHighThreatWindupDetailed(ids[0], 1600).Accepted, Is.True);
            Assert.That(runtime.TryBeginHighThreatWindupDetailed(ids[1], 1601).Accepted, Is.True);
            WindupAttemptResult blocked = runtime.TryBeginHighThreatWindupDetailed(ids[2], 1602);
            Assert.That(blocked.Accepted, Is.False);
            Assert.That(blocked.Reason, Is.EqualTo(WindupBlockReason.HighThreatWindupCap));
            Assert.That(blocked.Current, Is.EqualTo(blocked.Prior));
            Assert.That(blocked.CountBefore, Is.EqualTo(2));
            Assert.That(blocked.CountAfter, Is.EqualTo(2));
            Assert.That(runtime.Snapshot().HighThreatWindupCount, Is.EqualTo(2));
        }

        [Test]
        public void StoryQueues_SeedsZeroThrough999_AllDaysAndDifficultiesPassCorePredicatesAndTicketBounds()
        {
            int queues = 0;
            foreach (LastWatchDifficulty difficulty in Enum.GetValues(typeof(LastWatchDifficulty)))
            for (int day = 1; day <= 7; day++)
            for (ulong seed = 0; seed < 1000; seed++)
            {
                EnemyDirectorQueue queue = EnemyDirector.CreateStoryQueue(
                    new EnemyDirectorRequest(seed, difficulty, day, 31));
                SpawnTicket[] tickets = queue.Tickets.ToArray();
                Assert.That(EnemyDirector.SatisfiesRollingFaceShare(tickets, 10 * EnemyDirector.TickRate), Is.True,
                    $"seed={seed}; day={day}; difficulty={difficulty}");
                Assert.That(tickets.GroupBy(x => x.GroupId).All(x => x.Count() <= 4), Is.True);
                Assert.That(tickets.All(x => x.EarliestStructureDamageTick - x.WarningStartTick >= 400), Is.True);
                Assert.That(MaxSameFaceRun(tickets), Is.LessThanOrEqualTo(3));
                foreach (SpawnTicket[] group in tickets.GroupBy(x => x.GroupId)
                             .Select(x => x.OrderBy(y => y.QueueIndex).ToArray()))
                {
                    for (int index = 1; index < group.Length; index++)
                        Assert.That(group[index].ScheduledTick - group[index - 1].ScheduledTick, Is.InRange(20, 35));
                    for (int left = 0; left < group.Length; left++)
                    for (int right = left + 1; right < group.Length; right++)
                        Assert.That(Vector3.Distance(group[left].SpawnPosition, group[right].SpawnPosition),
                            Is.GreaterThanOrEqualTo(1.1999f));
                }
                AssertRollingScheduledPressure(tickets);
                queues++;
            }
            Assert.That(queues, Is.EqualTo(14000));
        }

        [Test]
        public void RollingFaceShare_ZeroAndOneAreExemptAndTwoTicketBoundariesUseHalfOpenWindow()
        {
            Assert.That(EnemyDirector.SatisfiesRollingFaceShare(Array.Empty<SpawnTicket>(), 500), Is.True);
            Assert.That(EnemyDirector.SatisfiesRollingFaceShare(new[] { Ticket(1, FaceId.FaceA, 0) }, 500), Is.True);
            Assert.That(EnemyDirector.SatisfiesRollingFaceShare(new[]
            {
                Ticket(1, FaceId.FaceA, 0), Ticket(2, FaceId.FaceB, 499)
            }, 500), Is.True);
            Assert.That(EnemyDirector.SatisfiesRollingFaceShare(new[]
            {
                Ticket(1, FaceId.FaceA, 0), Ticket(2, FaceId.FaceA, 499)
            }, 500), Is.False);
            Assert.That(EnemyDirector.SatisfiesRollingFaceShare(new[]
            {
                Ticket(1, FaceId.FaceA, 0), Ticket(2, FaceId.FaceA, 500)
            }, 500), Is.True);
        }

        [Test]
        public void RunnerCoordinator_UsesStableDueOrderGlobalThreeAndConsumesExcessReservation()
        {
            var coordinator = new RunnerEvadeCoordinator();
            const ulong fireValue = 700;
            for (ulong runner = 1; runner <= 4; runner++)
            {
                RunnerReservationSnapshot reservation = coordinator.Reserve(Request(runner, fireValue, runner));
                Assert.That(reservation.Phase, Is.EqualTo(RunnerEvadePhase.Scheduled));
            }
            RunnerReservationSnapshot[] scheduled = coordinator.Snapshot();
            int dueTick = scheduled.Max(x => x.Decision.ScheduledTick);
            var states = scheduled.ToDictionary(x => x.RunnerId.Value, _ => EnemyMovementState.Approach);
            RunnerReservationSnapshot[] changed = coordinator.ExecuteDue(dueTick, states);
            RunnerReservationSnapshot[] expectedOrder = scheduled.OrderBy(x => x.Decision.ScheduledTick)
                .ThenBy(x => x.RunnerId).ToArray();
            Assert.That(changed.Select(x => x.RunnerId), Is.EqualTo(expectedOrder.Select(x => x.RunnerId)));
            Assert.That(changed.Count(x => x.Phase == RunnerEvadePhase.Moving), Is.EqualTo(3));
            Assert.That(changed.Single(x => x.Phase == RunnerEvadePhase.Skipped).SkipReason,
                Is.EqualTo(RunnerCoordinationSkipReason.GlobalActiveCap));
            RunnerReservationSnapshot duplicate = coordinator.Reserve(Request(1, fireValue, 99));
            Assert.That(duplicate.SkipReason, Is.EqualTo(RunnerCoordinationSkipReason.FireSequenceConsumed));
            Assert.That(duplicate.Phase, Is.EqualTo(RunnerEvadePhase.Skipped));
        }

        [Test]
        public void RunnerExecutableProbes_ThreeShotBurstPrecedesMovementAndOneThousandRaysApplyOrdinaryDamage()
        {
            RunnerBurstObservation burst = RunnerExecutableProbe.ExecuteFixedAssaultRifleBurst(
                new EntityId(0xa001), new FireSequenceId(0xb001), 100, 1);
            Assert.That(burst.Shots, Has.Length.EqualTo(3));
            Assert.That(burst.Shots.All(x => x.DamageApplied), Is.True);
            Assert.That(burst.Shots.All(x => !x.EvadeMovementStarted), Is.True);
            Assert.That(burst.Shots.Max(x => x.ResolutionTick), Is.LessThan(burst.ReactionScheduledTick));

            CrossingRayObservation rays = RunnerExecutableProbe.ExecuteCrossingRays(1000);
            Assert.That(rays.AttemptCount, Is.EqualTo(1000));
            Assert.That(rays.OrdinaryDamageAppliedCount, Is.EqualTo(1000));
            Assert.That(rays.PriorShotCancellationCount, Is.Zero);
            Assert.That(rays.HitboxShrinkCount, Is.Zero);
            Assert.That(rays.CapsuleRadiusAfter, Is.EqualTo(rays.CapsuleRadiusBefore));
            Assert.That(rays.CapsuleHeightAfter, Is.EqualTo(rays.CapsuleHeightBefore));
        }

        [Test]
        public void ClimberPhysicsProbe_AllRequiredBranchPosesExecuteAgainstPopulatedGrayboxAndRemainVisible()
        {
            var root = new GameObject("M3_Climber_LOS_Fixture");
            try
            {
                M2GrayboxBuilder builder = root.AddComponent<M2GrayboxBuilder>();
                builder.BuildNormal();
                ClimberPoseObservation[] poses = ClimberPhysicsLineOfSightProbe.Execute(builder);
                Assert.That(poses, Is.Not.Empty);
                Assert.That(poses.All(x => x.PhysicsLinecastExecuted), Is.True);
                Assert.That(poses.All(x => !x.IsUpperDeck), Is.True);
                Assert.That(poses.All(x => x.HasCanonicalUpperLineOfSight), Is.True,
                    string.Join(";", poses.Where(x => !x.HasCanonicalUpperLineOfSight)
                        .Take(10).Select(x => x.Face + "/" + x.Route + "/" + x.Branch + "/" + x.PoseIndex)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Data30SinkIsolation_NullMemoryAndFailingSinksDoNotChangeGameplayHash()
        {
            var emitter = new M3FactEmitter();
            emitter.Emit(M3CoreFactKind.DefenseSummary, 10, detail: "fixture");
            string baseline = emitter.GameplayHash();
            Assert.That(M3DataIsolationProbe.Execute(emitter, null).GameplayInvariant, Is.True);
            Assert.That(M3DataIsolationProbe.Execute(emitter, new CollectingSink()).GameplayInvariant, Is.True);
            Assert.That(M3DataIsolationProbe.Execute(emitter, new FailingSink()).GameplayInvariant, Is.True);
            Assert.That(emitter.GameplayHash(), Is.EqualTo(baseline));
        }

        [Test]
        public void UpperOnlyReplay_NormalAndHardRemainExplicitlyInconclusive()
        {
            foreach (LastWatchDifficulty difficulty in Enum.GetValues(typeof(LastWatchDifficulty)))
            {
                var observation = new UpperOnlyReplayObservation(difficulty, 1234);
                Assert.That(observation.Status, Is.EqualTo(UpperOnlyReplayStatus.InconclusiveCombatFixtureUnavailable));
                Assert.That(observation.Reason, Is.Not.Empty);
            }
        }

        [Test]
        public void RuntimeSaveRestore_ContinuationMatchesUninterruptedStateFactsAndExactOnceProjection()
        {
            EnemyDirectorQueue queue = M3RuntimeFixtureFactory.CreateQueue(
                new M3TicketFixtureDefinition(EnemyArchetype.Walker, FaceId.FaceA, 0),
                new M3TicketFixtureDefinition(EnemyArchetype.Runner, FaceId.FaceB, 600));
            var facts = new M3FactEmitter();
            var director = new EnemyDirectorRuntime(queue, facts);
            var pool = new EnemyPool(0x7000UL);
            var runners = new RunnerEvadeCoordinator(facts);
            Assert.That(director.TryActivateNextDetailed(0, pool).Accepted, Is.True);
            runners.Reserve(Request(0x9000, 0x8000, 1));

            M3RuntimeSaveSnapshot checkpoint = M3RuntimeSaveSnapshot.Capture(director, pool, runners, facts);
            M3RuntimeRestoreResult restored = checkpoint.Restore();
            Assert.That(restored.Director.Snapshot().StateHash, Is.EqualTo(director.Snapshot().StateHash));
            Assert.That(restored.Facts.GameplayHash(), Is.EqualTo(facts.GameplayHash()));

            Assert.That(director.TryActivateNextDetailed(600, pool).Accepted, Is.True);
            Assert.That(restored.Director.TryActivateNextDetailed(600, restored.Pool).Accepted, Is.True);
            int due = runners.Snapshot().Max(x => x.Decision.ScheduledTick);
            var state = new Dictionary<ulong, EnemyMovementState> { { 0x9000, EnemyMovementState.Approach } };
            runners.ExecuteDue(due, state);
            restored.Runners.ExecuteDue(due, state);

            M3RuntimeSaveSnapshot control = M3RuntimeSaveSnapshot.Capture(director, pool, runners, facts);
            M3RuntimeSaveSnapshot resumed = M3RuntimeSaveSnapshot.Capture(restored.Director, restored.Pool,
                restored.Runners, restored.Facts);
            Assert.That(resumed.AuthoritativeHash, Is.EqualTo(control.AuthoritativeHash));
            Assert.That(restored.Director.Snapshot().StateHash, Is.EqualTo(director.Snapshot().StateHash));
            Assert.That(restored.Facts.Snapshot().Select(x => x.Sequence),
                Is.EqualTo(facts.Snapshot().Select(x => x.Sequence)));
            Assert.That(restored.Facts.GameplayHash(), Is.EqualTo(facts.GameplayHash()));
            Assert.That(restored.Facts.ReadAfter(0).Length, Is.EqualTo(restored.Facts.Snapshot().Count));
        }

        private static RunnerReactionRequest Request(ulong runner, ulong fire, ulong sequence) =>
            new RunnerReactionRequest(new EntityId(runner), new FireSequenceId(fire), sequence, 100,
                EnemyMovementState.Approach, 0, 0, true, true, 0, runner * 17);

        private static SpawnTicket Ticket(ulong id, FaceId face, int tick) =>
            new SpawnTicket(id, (int)id - 1, EnemyArchetype.Walker, face, 0, tick, id, EnemyDirector.TickRate);

        private static M3TicketFixtureDefinition D(EnemyArchetype archetype, FaceId face) =>
            new M3TicketFixtureDefinition(archetype, face, 0);

        private static int MaxSameFaceRun(SpawnTicket[] tickets)
        {
            int run = tickets.Length == 0 ? 0 : 1, maximum = run;
            for (int index = 1; index < tickets.Length; index++)
            {
                run = tickets[index].Face == tickets[index - 1].Face ? run + 1 : 1;
                maximum = Math.Max(maximum, run);
            }
            return maximum;
        }

        private static void AssertRollingScheduledPressure(SpawnTicket[] tickets)
        {
            for (int start = 0; start < tickets.Length; start++)
            {
                int end = tickets[start].ScheduledTick + M3PressureRules.RollingWindowTicks;
                int units = tickets.Where(x => x.ScheduledTick >= tickets[start].ScheduledTick &&
                                               x.ScheduledTick < end)
                    .Sum(x => M3PressureRules.UnitsFor(x.Archetype));
                Assert.That(units, Is.LessThanOrEqualTo(M3PressureRules.RollingSpawnCapUnits),
                    $"seed queue window start={tickets[start].ScheduledTick}; pressureUnits={units}");
            }
        }

        private static M3TicketFixtureDefinition[] DefinitionsFor(ActivationBlockReason reason)
        {
            switch (reason)
            {
                case ActivationBlockReason.TotalPressure:
                    return Enumerable.Range(0, 9).Select(x => D(EnemyArchetype.Climber, (FaceId)(x % 3)))
                        .Concat(new[] { D(EnemyArchetype.Walker, FaceId.FaceA) }).ToArray();
                case ActivationBlockReason.FaceEntityCount:
                    return Enumerable.Repeat(D(EnemyArchetype.Walker, FaceId.FaceA), 7).ToArray();
                case ActivationBlockReason.FacePressure:
                    return new[] { D(EnemyArchetype.Breaker, FaceId.FaceA), D(EnemyArchetype.Breaker, FaceId.FaceA), D(EnemyArchetype.Runner, FaceId.FaceA) };
                case ActivationBlockReason.BreakerCount:
                    return new[] { D(EnemyArchetype.Breaker, FaceId.FaceA), D(EnemyArchetype.Breaker, FaceId.FaceB), D(EnemyArchetype.Breaker, FaceId.FaceC) };
                case ActivationBlockReason.NewSpawnPressureWindow:
                    return Enumerable.Range(0, 8).Select(x => D(EnemyArchetype.Walker, (FaceId)(x % 3))).ToArray();
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason));
            }
        }

        private sealed class CollectingSink : IM3CoreFactSink
        {
            public readonly List<M3CoreFact> Facts = new List<M3CoreFact>();
            public void Observe(M3CoreFact fact) => Facts.Add(fact);
        }

        private sealed class FailingSink : IM3CoreFactSink
        {
            public void Observe(M3CoreFact fact) => throw new InvalidOperationException("expected sink failure");
        }
    }
}
