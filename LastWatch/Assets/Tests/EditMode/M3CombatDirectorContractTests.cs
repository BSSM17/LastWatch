using System;
using System.Collections.Generic;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using NUnit.Framework;

namespace LastWatch.Tests
{
    [TestFixture]
    public sealed class M3CombatDirectorContractTests
    {
        private static readonly int[] NormalTotals = { 18, 24, 30, 36, 42, 48, 54 };
        private static readonly int[] NormalCaps = { 8, 8, 9, 9, 10, 10, 11 };
        private static readonly float[] NormalDensity = { 2.6f, 2.9f, 3.2f, 3.5f, 3.8f, 4.1f, 4.4f };
        private static readonly int[] HardTotals = { 23, 30, 38, 45, 53, 60, 68 };
        private static readonly int[] HardCaps = { 10, 10, 11, 11, 12, 12, 13 };
        private static readonly float[] HardDensity = { 3.0f, 3.3f, 3.7f, 4.0f, 4.4f, 4.7f, 5.1f };

        [Test]
        public void ArchetypeCatalog_HasOnlyFourFixedNonRangedUpperSafeDefinitions()
        {
            EnemyArchetypeConfig[] all = EnemyArchetypeCatalog.All.ToArray();
            Assert.That(all.Select(x => x.Archetype), Is.EquivalentTo(new[]
            {
                EnemyArchetype.Walker, EnemyArchetype.Runner, EnemyArchetype.Climber, EnemyArchetype.Breaker
            }));
            Assert.That(all, Has.Length.EqualTo(4));
            Assert.That(all.Count(x => x.HasRangedAttack || x.CanEnterUpperDeck || x.CanTargetUpperActor), Is.Zero);
            Assert.That(all.Single(x => x.Archetype == EnemyArchetype.Climber).CanTargetGroundActor, Is.False);
            Assert.That(all.Single(x => x.Archetype == EnemyArchetype.Breaker).CanTargetGroundActor, Is.False);
            Assert.That(EnemyArchetypeCatalog.CanonicalHash(), Has.Length.EqualTo(64));
        }

        [TestCase(LastWatchDifficulty.Normal)]
        [TestCase(LastWatchDifficulty.Hard)]
        public void StoryDayOneThroughSeven_MatchesApprovedQuantityTable(LastWatchDifficulty difficulty)
        {
            int[] totals = difficulty == LastWatchDifficulty.Normal ? NormalTotals : HardTotals;
            int[] caps = difficulty == LastWatchDifficulty.Normal ? NormalCaps : HardCaps;
            float[] density = difficulty == LastWatchDifficulty.Normal ? NormalDensity : HardDensity;
            for (int day = 1; day <= 7; day++)
            {
                StoryQuantity actual = EnemyDirector.GetStoryQuantity(day, difficulty);
                Assert.That(actual.Total, Is.EqualTo(totals[day - 1]), $"day={day} total");
                Assert.That(actual.ActiveCap, Is.EqualTo(caps[day - 1]), $"day={day} cap");
                Assert.That(actual.DensityPer10Seconds, Is.EqualTo(density[day - 1]).Within(0.001f), $"day={day} density");
            }
        }

        [Test]
        public void Queue_IsDeterministicAndUsesLargestRemainderQuotas()
        {
            foreach (LastWatchDifficulty difficulty in Enum.GetValues(typeof(LastWatchDifficulty)))
            for (int day = 1; day <= 7; day++)
            {
                var request = new EnemyDirectorRequest(0x123456789abcdef0UL, difficulty, day, 31);
                EnemyDirectorQueue first = EnemyDirector.CreateStoryQueue(request);
                EnemyDirectorQueue second = EnemyDirector.CreateStoryQueue(request);
                Assert.That(first.CanonicalHash(), Is.EqualTo(second.CanonicalHash()));
                Assert.That(first.Tickets.Select(x => x.TicketId), Is.EqualTo(second.Tickets.Select(x => x.TicketId)));
                int[] expected = LargestRemainder(first.Quantity.Total);
                for (int kind = 0; kind < 4; kind++)
                    Assert.That(first.Tickets.Count(x => (int)x.Archetype == kind), Is.EqualTo(expected[kind]));
            }
        }

        [Test]
        public void Queue_RespectsGroupFaceAndWarningConstraints()
        {
            foreach (LastWatchDifficulty difficulty in Enum.GetValues(typeof(LastWatchDifficulty)))
            for (int day = 1; day <= 7; day++)
            {
                SpawnTicket[] tickets = EnemyDirector.CreateStoryQueue(
                    new EnemyDirectorRequest(0xfedcba9876543210UL, difficulty, day, 31)).Tickets.ToArray();
                Assert.That(tickets.GroupBy(x => x.GroupId).Max(x => x.Count()), Is.LessThanOrEqualTo(4));
                Assert.That(tickets.All(x => x.EarliestStructureDamageTick - x.WarningStartTick >= 8 * EnemyDirector.TickRate), Is.True);
                foreach (IGrouping<int, SpawnTicket> group in tickets.GroupBy(x => x.GroupId))
                {
                    SpawnTicket[] rows = group.OrderBy(x => x.QueueIndex).ToArray();
                    for (int index = 1; index < rows.Length; index++)
                        Assert.That(rows[index].ScheduledTick - rows[index - 1].ScheduledTick,
                            Is.InRange(20, 35), $"day={day} group={group.Key} internal gap");
                    if (rows.Length == 4)
                        Assert.That(rows.GroupBy(x => x.Face).Max(x => x.Count()) / (float)rows.Length,
                            Is.LessThanOrEqualTo(0.60f));
                }
                int run = 1;
                for (int index = 1; index < tickets.Length; index++)
                {
                    run = tickets[index].Face == tickets[index - 1].Face ? run + 1 : 1;
                    Assert.That(run, Is.LessThanOrEqualTo(3));
                }
            }
        }

        [Test]
        public void DirectorRuntime_RoundTripPreservesTicketLifecycleEntityAndHash()
        {
            EnemyDirectorQueue queue = EnemyDirector.CreateStoryQueue(
                new EnemyDirectorRequest(77UL, LastWatchDifficulty.Normal, 1, 31));
            var runtime = new EnemyDirectorRuntime(queue);
            var pool = new EnemyPool(0x2000UL);
            Assert.That(runtime.TryActivateNext(0, pool, out EntityId first), Is.True);
            Assert.That(runtime.TryActivateNext(10000, pool, out EntityId second), Is.True);
            Assert.That(runtime.TryResolve(first, 10001, pool), Is.True);
            EnemyDirectorRuntimeSnapshot before = runtime.Snapshot();
            EnemyDirectorRuntime restored = EnemyDirectorRuntime.Restore(queue, before.Tickets);
            EnemyDirectorRuntimeSnapshot after = restored.Snapshot();
            Assert.That(after.ImmutableQueueHash, Is.EqualTo(before.ImmutableQueueHash));
            Assert.That(after.StateHash, Is.EqualTo(before.StateHash));
            Assert.That(after.Cursor, Is.EqualTo(before.Cursor));
            Assert.That(after.Tickets.Select(x => x.ActiveEntityId), Is.EqualTo(before.Tickets.Select(x => x.ActiveEntityId)));
            Assert.That(second.IsValid, Is.True);
        }

        [Test]
        public void Pool_EnforcesTwentyPrewarmFourteenActiveAndFourVisibleCorpses()
        {
            var pool = new EnemyPool(0x3000UL);
            var active = new List<EntityId>();
            for (int index = 0; index < 14; index++)
            {
                Assert.That(pool.TryActivate((ulong)index + 1, index, 14, out EntityId entity), Is.True);
                active.Add(entity);
            }
            Assert.That(pool.TryActivate(99, 20, 14, out _), Is.False);
            Assert.That(pool.Events.Last().Kind, Is.EqualTo(EnemyPoolEventKind.ShortageDelayed));
            for (int index = 0; index < 5; index++) pool.Resolve(active[index], 30 + index, (ulong)index + 1);
            EnemyPoolSnapshot snapshot = pool.Snapshot();
            Assert.That(snapshot.PrewarmCapacity, Is.EqualTo(20));
            Assert.That(snapshot.ActiveCount, Is.EqualTo(9));
            Assert.That(snapshot.VisibleCorpseCount, Is.EqualTo(4));
            Assert.That(snapshot.RetainedDeathFactCount, Is.EqualTo(5));
            Assert.That(snapshot.CombatInstantiateCount, Is.Zero);
            Assert.That(snapshot.CombatDestroyCount, Is.Zero);
        }

        [Test]
        public void RunnerReaction_TenThousandResolvedHitsNeverCancelDamageAndObeysBounds()
        {
            var consumed = new HashSet<string>();
            int scheduled = 0;
            for (int index = 0; index < 10000; index++)
            {
                var request = new RunnerReactionRequest(new EntityId((ulong)index + 1),
                    new FireSequenceId((ulong)index + 1), (ulong)index + 1, 100,
                    EnemyMovementState.Approach, 0, 0, true, true, index % 4, (ulong)index + 9);
                RunnerReactionDecision result = RunnerReactionPolicy.Resolve(request, consumed);
                Assert.That(result.DamageRemainedApplied, Is.True);
                if (result.Phase != RunnerEvadePhase.Scheduled) continue;
                scheduled++;
                Assert.That(result.ScheduledTick - 100, Is.InRange(13, 22));
                Assert.That(result.LateralDistanceMeters, Is.InRange(0.9f, 1.4f));
                Assert.That(result.MovementDurationTicks, Is.InRange(20, 30));
                Assert.That(result.CooldownUntilTick - 100, Is.InRange(110, 160));
            }
            Assert.That(scheduled, Is.GreaterThan(0));
        }

        [Test]
        public void RunnerReaction_OnePerFireSequenceAndAllRestrictionPathsSkip()
        {
            var consumed = new HashSet<string>();
            var valid = new RunnerReactionRequest(new EntityId(1), new FireSequenceId(7), 1, 100,
                EnemyMovementState.Approach, 0, 0, true, true, 0, 9);
            Assert.That(RunnerReactionPolicy.Resolve(valid, consumed).Phase, Is.EqualTo(RunnerEvadePhase.Scheduled));
            Assert.That(RunnerReactionPolicy.Resolve(valid, consumed).SkipReason, Is.EqualTo("fire-sequence-consumed"));
            AssertSkip(EnemyMovementState.AttackWindup, 0, 0, 0, true, true, "state");
            AssertSkip(EnemyMovementState.Approach, 101, 0, 0, true, true, "cooldown");
            AssertSkip(EnemyMovementState.Approach, 0, 2, 0, true, true, "approach-cap");
            AssertSkip(EnemyMovementState.Approach, 0, 0, 3, true, true, "global-active-cap");
            AssertSkip(EnemyMovementState.Approach, 0, 0, 0, false, false, "capsule-sweep-blocked");
        }

        [Test]
        public void FaceUnavailableReroute_IsDeterministicNonTeleportingAndRewarnsEightSeconds()
        {
            FaceRerouteDecision a = EnemyReroutePolicy.FaceUnavailable(new EntityId(9), FaceId.FaceA, 3, 200,
                new[] { FaceId.FaceB, FaceId.FaceC });
            FaceRerouteDecision b = EnemyReroutePolicy.FaceUnavailable(new EntityId(9), FaceId.FaceA, 3, 200,
                new[] { FaceId.FaceC, FaceId.FaceB });
            Assert.That(a.NewFace, Is.EqualTo(b.NewFace));
            Assert.That(a.NewFace, Is.Not.EqualTo(FaceId.FaceA));
            Assert.That(a.DeletesOrTeleports, Is.False);
            Assert.That(a.EarliestDamageTick - a.RerouteStartTick, Is.EqualTo(8 * EnemyDirector.TickRate));
        }

        [Test]
        public void Data30FactCatalog_ContainsRequiredAuthoritativeFactKinds()
        {
            M3CoreFactKind[] values = (M3CoreFactKind[])Enum.GetValues(typeof(M3CoreFactKind));
            Assert.That(values, Does.Contain(M3CoreFactKind.SpawnTicketStateChanged));
            Assert.That(values, Does.Contain(M3CoreFactKind.FireSequenceResolved));
            Assert.That(values, Does.Contain(M3CoreFactKind.EnemyResolved));
            Assert.That(values, Does.Contain(M3CoreFactKind.DefenseSummary));
            Assert.That(values, Does.Contain(M3CoreFactKind.RunnerReactionAttempted));
            Assert.That(values, Does.Contain(M3CoreFactKind.RunnerEvadeStarted));
            Assert.That(values, Does.Contain(M3CoreFactKind.RunnerEvadeEnded));
            Assert.That(values, Does.Contain(M3CoreFactKind.RunnerHitDuringEvade));
        }

        private static void AssertSkip(EnemyMovementState state, int cooldown, int reactions, int active,
            bool originalClear, bool oppositeClear, string reason)
        {
            var request = new RunnerReactionRequest(new EntityId(99), new FireSequenceId(99), 99, 100,
                state, cooldown, reactions, originalClear, oppositeClear, active, 99);
            Assert.That(RunnerReactionPolicy.Resolve(request, new HashSet<string>()).SkipReason, Is.EqualTo(reason));
        }

        private static int[] LargestRemainder(int total)
        {
            int[] weights = { 70, 15, 10, 5 };
            int[] result = weights.Select(x => total * x / 100).ToArray();
            int missing = total - result.Sum();
            int[] order = Enumerable.Range(0, 4).OrderByDescending(x => total * weights[x] % 100).ThenBy(x => x).ToArray();
            for (int index = 0; index < missing; index++) result[order[index]]++;
            return result;
        }
    }
}
