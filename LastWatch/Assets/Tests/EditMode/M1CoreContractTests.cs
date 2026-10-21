using System;
using System.IO;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Persistence;
using LastWatch.Core.Simulation;
using LastWatch.Core.State;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Tests
{
    [TestFixture]
    public sealed class M1CoreContractTests
    {
        [Test]
        public void StableIds_UseUnsignedOrder_InvalidZero_AndHexRoundTrip()
        {
            Assert.That(EntityId.Invalid.IsValid, Is.False);
            Assert.That(new RunId(0).IsValid, Is.False);
            Assert.That(new FireSequenceId(0).IsValid, Is.False);

            EntityId[] ids =
            {
                new EntityId(ulong.MaxValue),
                new EntityId(1),
                new EntityId(0x8000000000000000UL)
            };
            Array.Sort(ids);

            Assert.That(ids.Select(id => id.Value), Is.EqualTo(new[]
            {
                1UL,
                0x8000000000000000UL,
                ulong.MaxValue
            }));
            Assert.That(EntityId.Parse(ids[2].ToString()), Is.EqualTo(ids[2]));
            Assert.That(ids[0].ToString(), Is.EqualTo("0000000000000001"));
            Assert.Throws<FormatException>(() => EntityId.Parse("not-hex"));
        }

        [Test]
        public void SimulationTime_IsSixtyHertz_AndNeverRoundsMinimumDurationDown()
        {
            Assert.That(SimulationTime.TickRate, Is.EqualTo(60));
            Assert.That(SimulationTime.SecondsPerTick, Is.EqualTo(1.0 / 60.0));
            Assert.That(SimulationTime.MinimumSecondsToTicks(0), Is.Zero);
            Assert.That(SimulationTime.MinimumSecondsToTicks(1.0 / 60.0), Is.EqualTo(1));
            Assert.That(SimulationTime.MinimumSecondsToTicks((1.0 / 60.0) + 0.000001), Is.EqualTo(2));
            Assert.That(SimulationTime.MinimumSecondsToTicks(3.0), Is.EqualTo(180));

            const double requested = 2.0001;
            int ticks = SimulationTime.MinimumSecondsToTicks(requested);
            Assert.That(ticks * SimulationTime.SecondsPerTick, Is.GreaterThanOrEqualTo(requested));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimulationTime.MinimumSecondsToTicks(-0.1));
        }

        [Test]
        public void EventSequence_IsMonotonic_AndNextValueSurvivesRoundTrip()
        {
            RunState state = CreateState();

            Assert.That(state.IssueEventSequence(), Is.EqualTo(1UL));
            Assert.That(state.IssueEventSequence(), Is.EqualTo(2UL));
            Assert.That(state.IssueEventSequence(), Is.EqualTo(3UL));
            Assert.That(state.NextEventSequence, Is.EqualTo(4UL));

            RunState restored = RunStateJson.Deserialize(RunStateJson.Serialize(state));
            Assert.That(restored.NextEventSequence, Is.EqualTo(4UL));
            Assert.That(restored.IssueEventSequence(), Is.EqualTo(4UL));
            Assert.That(restored.IssueEventSequence(), Is.EqualTo(5UL));
        }

        [Test]
        public void DamagePacket_DuplicateSequence_DecreasesHitPointsExactlyOnce()
        {
            var target = new EntityId(100);
            RunState state = CreateState(new DamageableFixture
            {
                EntityValue = target.Value,
                Kind = DamageableKind.Enemy,
                HitPoints = 45
            });
            DamagePacket packet = Packet(state, target, 10);
            var resolver = new DamageResolver(state);

            DamageReceipt first = resolver.Apply(packet);
            DamageReceipt duplicate = resolver.Apply(packet);

            Assert.That(first.Status, Is.EqualTo(DamageApplyStatus.Applied));
            Assert.That(first.HitPointsBefore, Is.EqualTo(45));
            Assert.That(first.HitPointsAfter, Is.EqualTo(35));
            Assert.That(duplicate.Status, Is.EqualTo(DamageApplyStatus.Duplicate));
            Assert.That(state.TryGetDamageable(target, out DamageableSnapshot snapshot), Is.True);
            Assert.That(snapshot.HitPoints, Is.EqualTo(35));
        }

        [Test]
        public void InvalidTargetAndPacket_DoNotMutateStateOrAppliedHistory()
        {
            var target = new EntityId(100);
            RunState state = CreateState(new DamageableFixture
            {
                EntityValue = target.Value,
                Kind = DamageableKind.Enemy,
                HitPoints = 45
            });
            var resolver = new DamageResolver(state);
            DamagePacket unknownTargetPacket = Packet(state, new EntityId(999), 10);
            string before = RunStateJson.CanonicalHash(state);

            DamageReceipt invalidTarget = resolver.Apply(unknownTargetPacket);
            DamageReceipt invalidPacket = resolver.Apply(new DamagePacket(
                0, state.FixedTick, new EntityId(1), new EntityId(2), target,
                DamageKind.Ballistic, 10, Vector3.zero, Vector3.zero, new FireSequenceId(1)));

            Assert.That(invalidTarget.Status, Is.EqualTo(DamageApplyStatus.InvalidTarget));
            Assert.That(invalidPacket.Status, Is.EqualTo(DamageApplyStatus.InvalidPacket));
            Assert.That(RunStateJson.CanonicalHash(state), Is.EqualTo(before));
            Assert.That(state.TryGetDamageable(target, out DamageableSnapshot snapshot), Is.True);
            Assert.That(snapshot.HitPoints, Is.EqualTo(45));

            DamageReceipt validReuse = resolver.Apply(new DamagePacket(
                unknownTargetPacket.EventSequence, state.FixedTick,
                new EntityId(1), new EntityId(2), target, DamageKind.Ballistic, 5,
                Vector3.zero, Vector3.zero, new FireSequenceId(1)));
            Assert.That(validReuse.Status, Is.EqualTo(DamageApplyStatus.Applied),
                "An invalid target must not consume the damage application sequence.");
        }

        [Test]
        public void PlayerDeath_SealsFirstLethalPacketFacts()
        {
            var player = new EntityId(10);
            RunState state = CreateState(new DamageableFixture
            {
                EntityValue = player.Value,
                Kind = DamageableKind.Player,
                HitPoints = 20
            });
            state.AdvanceOneTick();
            DamagePacket lethal = Packet(state, player, 25, DamageKind.Melee);
            DamageReceipt first = new DamageResolver(state).Apply(lethal);
            DamageReceipt repeated = new DamageResolver(state).Apply(lethal);

            Assert.That(first.Status, Is.EqualTo(DamageApplyStatus.Applied));
            Assert.That(first.BecameResolved, Is.True);
            Assert.That(repeated.Status, Is.EqualTo(DamageApplyStatus.Duplicate));
            Assert.That(state.PlayerLife, Is.EqualTo(PlayerLifeState.Dead));
            Assert.That(state.PlayerDeath.HasValue, Is.True);
            Assert.That(state.PlayerDeath.Value.Tick, Is.EqualTo(state.FixedTick));
            Assert.That(state.PlayerDeath.Value.EventSequence, Is.EqualTo(lethal.EventSequence));
            Assert.That(state.PlayerDeath.Value.Cause, Is.EqualTo(DamageKind.Melee));
        }

        [Test]
        public void PartnerAtZeroHitPoints_BecomesDownedWithoutDeathFact()
        {
            var partner = new EntityId(20);
            RunState state = CreateState(new DamageableFixture
            {
                EntityValue = partner.Value,
                Kind = DamageableKind.Partner,
                HitPoints = 10
            });

            DamageReceipt receipt = new DamageResolver(state).Apply(Packet(state, partner, 10));

            Assert.That(receipt.Status, Is.EqualTo(DamageApplyStatus.Applied));
            Assert.That(receipt.BecameResolved, Is.False);
            Assert.That(state.PartnerLife, Is.EqualTo(PartnerLifeState.Downed));
            Assert.That(state.PartnerDeath.HasValue, Is.False);
            Assert.That(state.TryGetDamageable(partner, out DamageableSnapshot snapshot), Is.True);
            Assert.That(snapshot.HitPoints, Is.Zero);
            Assert.That(snapshot.IsResolved, Is.False);
        }

        [Test]
        public void JsonRoundTrip_PreservesCanonicalHashIdentityTickOrderAndNullFacts()
        {
            RunState state = RunStateFactory.CreateFixture(new RunStateFixture
            {
                RunValue = 0xfedcba9876543210UL,
                FixedTick = 123,
                NextEventSequence = 77,
                Damageables = new[]
                {
                    new DamageableFixture { EntityValue = 9, Kind = DamageableKind.Structure, HitPoints = 80 },
                    new DamageableFixture { EntityValue = 2, Kind = DamageableKind.Enemy, HitPoints = 15 }
                }
            });
            string hash = RunStateJson.CanonicalHash(state);

            string compact = RunStateJson.Serialize(state);
            RunState restored = RunStateJson.Deserialize(compact);
            string pretty = RunStateJson.Serialize(restored, true);
            RunState restoredAgain = RunStateJson.Deserialize(pretty);

            Assert.That(RunStateJson.CanonicalHash(restored), Is.EqualTo(hash));
            Assert.That(RunStateJson.CanonicalHash(restoredAgain), Is.EqualTo(hash));
            Assert.That(restored.RunId, Is.EqualTo(state.RunId));
            Assert.That(restored.FixedTick, Is.EqualTo(123));
            Assert.That(restored.NextEventSequence, Is.EqualTo(77UL));
            Assert.That(restored.PlayerDeath.HasValue, Is.False);
            Assert.That(restored.PartnerDeath.HasValue, Is.False);
            Assert.That(restored.Snapshot().Damageables.Select(x => x.EntityId.Value), Is.EqualTo(new[] { 2UL, 9UL }));
            StringAssert.Contains("\"runId\":\"fedcba9876543210\"", compact);
        }

        [Test]
        public void Deserialize_RejectsDeadLifeStateWithoutDeathFact()
        {
            string json = RunStateJson.Serialize(CreateState());
            string inconsistent = json.Replace("\"playerLife\":0", "\"playerLife\":1");

            Assert.That(inconsistent, Is.Not.EqualTo(json), "Fixture mutation must alter playerLife.");
            Assert.Throws<FormatException>(() => RunStateJson.Deserialize(inconsistent));
        }

        [Test]
        public void DamageableInputOrder_DoesNotChangeSnapshotOrderOrCanonicalHash()
        {
            DamageableFixture a = new DamageableFixture { EntityValue = 7, Kind = DamageableKind.Enemy, HitPoints = 30 };
            DamageableFixture b = new DamageableFixture { EntityValue = 2, Kind = DamageableKind.Structure, HitPoints = 90 };
            RunState first = CreateState(a, b);
            RunState second = CreateState(b, a);

            Assert.That(first.Snapshot().Damageables.Select(x => x.EntityId.Value), Is.EqualTo(new[] { 2UL, 7UL }));
            Assert.That(second.Snapshot().Damageables.Select(x => x.EntityId.Value), Is.EqualTo(new[] { 2UL, 7UL }));
            Assert.That(RunStateJson.CanonicalHash(second), Is.EqualTo(RunStateJson.CanonicalHash(first)));
        }

        [Test]
        public void RuntimeSource_HasNoForbiddenNondeterministicOrEditorDependencies()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "LastWatch.Core");
            string[] source = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Assert.That(source, Is.Not.Empty);

            string combined = string.Join("\n", source.Select(File.ReadAllText));
            string[] forbidden =
            {
                "UnityEngine.Random",
                "Time.time",
                "Task.Run",
                "System.Threading",
                "using UnityEditor",
                "UnityEditor.",
                "MonoBehaviour",
                "GameObject"
            };

            foreach (string token in forbidden)
                Assert.That(combined, Does.Not.Contain(token), $"Forbidden runtime dependency: {token}");
        }

        private static RunState CreateState(params DamageableFixture[] damageables)
        {
            return RunStateFactory.CreateFixture(new RunStateFixture
            {
                RunValue = 1,
                FixedTick = 10,
                NextEventSequence = 1,
                Damageables = damageables
            });
        }

        private static DamagePacket Packet(
            RunState state,
            EntityId target,
            float amount,
            DamageKind kind = DamageKind.Ballistic)
        {
            return new DamagePacket(
                state.IssueEventSequence(),
                state.FixedTick,
                new EntityId(1),
                new EntityId(2),
                target,
                kind,
                amount,
                new Vector3(1, 2, 3),
                new Vector3(4, 5, 6),
                new FireSequenceId(1));
        }
    }
}
