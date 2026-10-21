using System;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;
using LastWatch.Core.Persistence;
using LastWatch.Core.Source;
using LastWatch.Core.State;
using LastWatch.Core.Terminal;
using LastWatch.Telemetry.Projection;
using LastWatch.Telemetry.Sinks;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Telemetry.Tests
{
    [TestFixture]
    public sealed class V31TelemetryIntegrationTests
    {
        [Test]
        public void CommittedFactAdapter_ProjectsDamageStateAndDeathThenAcknowledges()
        {
            var player = new EntityId(10);
            RunState state = State(new DamageableFixture
            { EntityValue = player.Value, Kind = DamageableKind.Player, HitPoints = 5 });
            DamagePacket packet = Packet(state, player, 5, DamageKind.Melee);
            Assert.That(new DamageResolver(state).Apply(packet).Status, Is.EqualTo(DamageApplyStatus.Applied));
            Assert.That(CommittedFactJournal.ReadAfter(state, 0).Records.Length, Is.EqualTo(2));

            var sink = new InMemoryTelemetrySink(8);
            CommittedFactProjectionBatchResult result = Adapter(sink).ProjectAvailable(state, 0, Supplement());

            Assert.That(result.CoreFactCount, Is.EqualTo(2));
            Assert.That(result.AcknowledgedCursor, Is.EqualTo(2));
            Assert.That(sink.Events.Select(x => x.EventName), Is.EqualTo(new[]
            {
                TelemetryEventNames.DamageApplied,
                TelemetryEventNames.ActorStateChanged,
                TelemetryEventNames.ActorDeathCommitted
            }));
            Assert.That(sink.Events.Select(x => x.EventSequence), Is.EqualTo(new[]
            { packet.EventSequence, packet.EventSequence, packet.EventSequence }));
        }

        [Test]
        public void CommittedFactAcknowledgement_SurvivesSaveLoadAndPreventsReprojection()
        {
            var enemy = new EntityId(20);
            RunState state = State(new DamageableFixture
            { EntityValue = enemy.Value, Kind = DamageableKind.Enemy, HitPoints = 10 });
            DamagePacket packet = Packet(state, enemy, 1, DamageKind.Ballistic);
            new DamageResolver(state).Apply(packet);
            string gameplayHashBeforeAcknowledgement = RunStateJson.CanonicalHash(state);
            Adapter(new NullTelemetrySink()).ProjectAvailable(state, 0, Supplement());
            Assert.That(RunStateJson.CanonicalHash(state), Is.EqualTo(gameplayHashBeforeAcknowledgement),
                "The observability acknowledgement must be excluded from gameplay canonical state.");

            RunState restored = RunStateJson.Deserialize(RunStateJson.Serialize(state));
            var sink = new InMemoryTelemetrySink(8);
            CommittedFactProjectionBatchResult replay = Adapter(sink).ProjectAvailable(restored, 0, Supplement());
            Assert.That(replay.CoreFactCount, Is.Zero);
            Assert.That(replay.StartCursor, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        }

        [Test]
        public void CommittedFactAdapter_UsesOnlyStoredAuthoritativeSentinelContext()
        {
            RunState state = RunStateFactory.CreateNew(new RunId(21), "0.1.0-m1", "3.1", 21);
            var sink = new InMemoryTelemetrySink(4);

            CommittedFactProjectionBatchResult result = Adapter(sink).ProjectAvailable(state, 0, Supplement());

            Assert.That(result.IsPreMilestoneUnavailable, Is.True);
            Assert.That(result.IsStoredInRunState, Is.True);
            Assert.That(result.IsCrossRunComparisonEligible, Is.False);
            Assert.That(result.CoreFactCount, Is.Zero);
            Assert.That(sink.Events, Is.Empty);
        }

        [Test]
        public void CommittedFactAdapter_PreservesApprovedUnavailableContextAndExcludesComparison()
        {
            var enemy = new EntityId(22);
            RunState state = State(new DamageableFixture
            { EntityValue = enemy.Value, Kind = DamageableKind.Enemy, HitPoints = 10 });
            new DamageResolver(state).Apply(Packet(state, enemy, 1, DamageKind.Ballistic));
            var sink = new InMemoryTelemetrySink(4);

            CommittedFactProjectionBatchResult result = Adapter(sink).ProjectAvailable(state, 0, Supplement());

            Assert.That(result.IsPreMilestoneUnavailable, Is.True);
            Assert.That(result.IsStoredInRunState, Is.False);
            Assert.That(result.IsCrossRunComparisonEligible, Is.False);
            Assert.That(sink.Events.Single().RulesetHash,
                Is.EqualTo(RunContextDefinition.UnavailableRulesetHash));
            Assert.That(sink.Events.Single().ContentVersion,
                Is.EqualTo(RunContextDefinition.UnavailableToken));
            Assert.That(sink.Events.Single().Mode, Is.EqualTo(RunContextDefinition.UnavailableToken));
            Assert.That(sink.Events.Single().Difficulty, Is.EqualTo(RunContextDefinition.UnavailableToken));
            Assert.That(sink.Events.Single().Night, Is.Null);
        }

        [Test]
        public void SourceSameTick_SequencePermutationPreservesOverloadAndExecutionFacts()
        {
            RunState overloadFirst = State();
            RunState executionFirst = State();
            SourceFactSealResult first = SourceFactSealer.Seal(overloadFirst,
                new SourceTickFactBatch(overloadFirst.FixedTick, 1, 2, null));
            SourceFactSealResult second = SourceFactSealer.Seal(executionFirst,
                new SourceTickFactBatch(executionFirst.FixedTick, 2, 1, null));

            Assert.That(first.OverloadCommitted && first.ExecutionCommitted, Is.True);
            Assert.That(second.OverloadCommitted && second.ExecutionCommitted, Is.True);
            foreach (SourceStateSnapshot source in new[] { overloadFirst.Snapshot().Source, executionFirst.Snapshot().Source })
            {
                Assert.That(source.WorldState, Is.EqualTo(WorldSourceState.Active));
                Assert.That(source.AccessState, Is.EqualTo(SourceAccessState.Sealed));
                Assert.That(source.OverloadCommitTick, Is.EqualTo(10));
                Assert.That(source.ConfirmedPartnerExecutionTick, Is.EqualTo(10));
                Assert.That(source.ConfirmedPartnerExecution, Is.True);
            }
        }

        [Test]
        public void SourceExecutionOnPriorTick_RejectsLaterOverloadAttempt()
        {
            RunState state = State();
            SourceFactSealResult execution = SourceFactSealer.Seal(state,
                new SourceTickFactBatch(state.FixedTick, null, 1, null));
            state.AdvanceOneTick();
            SourceFactSealResult overload = SourceFactSealer.Seal(state,
                new SourceTickFactBatch(state.FixedTick, 2, null, null));
            Assert.That(execution.ExecutionCommitted, Is.True);
            Assert.That(overload.OverloadCommitted, Is.False);
            Assert.That(overload.OverloadRejectedByPriorSeal, Is.True);
            Assert.That(state.Snapshot().Source.OverloadCommitTick.HasValue, Is.False);
        }

        [Test]
        public void V1MigrationAndCorruptionIsolation_AreConsumedThroughPublicLoadContract()
        {
            RunStateLoadResult migrated = RunStateJson.TryDeserialize(V1Json(SourceState.Armed));
            Assert.That(migrated.Status, Is.EqualTo(RunStateLoadStatus.MigratedV1));
            Assert.That(migrated.IsLoaded, Is.True);
            Assert.That(migrated.State.Snapshot().Source.WorldState, Is.EqualTo(WorldSourceState.Active));
            Assert.That(migrated.State.Snapshot().Source.AccessState, Is.EqualTo(SourceAccessState.OverloadCommitted));
            Assert.That(migrated.State.Snapshot().Source.OverloadCommitOrigin, Is.EqualTo(SourceOverloadCommitOrigin.LegacyV1Migration));
            Assert.That(migrated.State.Snapshot().Terminal.ProgressState, Is.EqualTo(TerminalLifecycleState.None));

            Assert.That(RunStateJson.TryDeserialize("{\"schemaVersion\":3}").Status,
                Is.EqualTo(RunStateLoadStatus.UnsupportedFutureSchema));
            Assert.That(RunStateJson.TryDeserialize("{\"schemaVersion\":2}").Status,
                Is.EqualTo(RunStateLoadStatus.CorruptOrInvalid));
        }

        [Test]
        public void EmptyTerminalSnapshot_ProjectsWithoutInventingQueueFacts()
        {
            RunState state = State();
            V31ContractEvidence evidence = V31ContractEvidenceProjection.Capture(state.Snapshot());
            Assert.That(evidence.Terminal.ProgressState, Is.EqualTo(TerminalLifecycleState.None));
            Assert.That(evidence.Terminal.QueueHash, Is.Null);
            Assert.That(evidence.Terminal.TotalCount, Is.Zero);
            Assert.That(evidence.Terminal.Tickets, Is.Empty);
        }

        [Test]
        public void NonemptyTerminalSnapshot_ProjectsCoreTicketIdentityLifecycleAndCounts()
        {
            RunState state = TerminalStateFixtureRun(LocationZone.UpperSafeZone, 100);
            V31ContractEvidence evidence = V31ContractEvidenceProjection.Capture(state.Snapshot());
            Assert.That(evidence.Terminal.ProgressState, Is.EqualTo(TerminalLifecycleState.Active));
            Assert.That(evidence.Terminal.QueueHash, Has.Length.EqualTo(64));
            Assert.That(evidence.Terminal.TotalCount, Is.EqualTo(3));
            Assert.That(evidence.Terminal.PendingCount, Is.EqualTo(1));
            Assert.That(evidence.Terminal.ActiveCount, Is.EqualTo(1));
            Assert.That(evidence.Terminal.ResolvedCount, Is.EqualTo(1));
            Assert.That(evidence.Terminal.Tickets.Select(x => x.QueueIndex), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(evidence.Terminal.Tickets[1].ActiveEntityId, Is.EqualTo(new EntityId(501)));
        }

        [Test]
        public void TerminalQueueHashAndTicketLedger_SurviveSaveLoad()
        {
            RunState state = TerminalStateFixtureRun(LocationZone.UpperSafeZone, 100);
            V31ContractEvidence before = V31ContractEvidenceProjection.Capture(state.Snapshot());
            string gameplayHash = RunStateJson.CanonicalHash(state);
            RunState restored = RunStateJson.Deserialize(RunStateJson.Serialize(state));
            V31ContractEvidence after = V31ContractEvidenceProjection.Capture(restored.Snapshot());

            Assert.That(after.Terminal.QueueHash, Is.EqualTo(before.Terminal.QueueHash));
            Assert.That(after.Terminal.Tickets.Select(TicketEvidenceKey),
                Is.EqualTo(before.Terminal.Tickets.Select(TicketEvidenceKey)));
            Assert.That(RunStateJson.CanonicalHash(restored), Is.EqualTo(gameplayHash));
        }

        [Test]
        public void TerminalQueueHash_IgnoresPlayerZoneAndHitPoints()
        {
            RunState upper = TerminalStateFixtureRun(LocationZone.UpperSafeZone, 100);
            RunState ground = TerminalStateFixtureRun(LocationZone.GroundDanger, 17);
            Assert.That(ground.Snapshot().Terminal.QueueHash, Is.EqualTo(upper.Snapshot().Terminal.QueueHash));
            Assert.That(RunStateJson.CanonicalHash(ground), Is.Not.EqualTo(RunStateJson.CanonicalHash(upper)),
                "The fixture must prove gameplay state changed while immutable queue identity did not.");
        }

        [Test]
        public void TerminalCollapseFixture_PreservesQueueHashAndTicketLifecycle()
        {
            RunState state = TerminalStateFixtureRun(LocationZone.UpperSafeZone, 100);
            TerminalStateSnapshot before = state.Snapshot().Terminal;
            TerminalStateSnapshot after = RunStateFactory.PreserveTerminalForCollapseFixture(state);
            Assert.That(after.QueueHash, Is.EqualTo(before.QueueHash));
            Assert.That(after.Tickets.Select(x => x.Ticket.TicketId),
                Is.EqualTo(before.Tickets.Select(x => x.Ticket.TicketId)));
            Assert.That(after.Tickets.Select(x => x.Lifecycle),
                Is.EqualTo(before.Tickets.Select(x => x.Lifecycle)));
            Assert.That(after.PendingCount, Is.EqualTo(before.PendingCount));
            Assert.That(after.ActiveCount, Is.EqualTo(before.ActiveCount));
            Assert.That(after.ResolvedCount, Is.EqualTo(before.ResolvedCount));
        }

        private static CommittedFactTelemetryAdapter Adapter(ITelemetrySink sink) =>
            new CommittedFactTelemetryAdapter(new T1TelemetryProjector(
                new SafeTelemetryDispatcher(sink), new SequentialIdentitySource()));

        private static TelemetryContextSupplement Supplement() => new TelemetryContextSupplement(
            Guid.Parse("00000000-0000-0000-0000-000000000010"));

        private static RunState State(params DamageableFixture[] values) =>
            RunStateFactory.CreateFixture(new RunStateFixture
            { RunValue = 1, FixedTick = 10, NextEventSequence = 1, Damageables = values });

        private static RunState TerminalStateFixtureRun(LocationZone playerZone, float playerHitPoints)
        {
            return RunStateFactory.CreateFixture(new RunStateFixture
            {
                RunValue = 1,
                FixedTick = 10,
                NextEventSequence = 1,
                PlayerZone = playerZone,
                Damageables = new[]
                {
                    new DamageableFixture { EntityValue = 10, Kind = DamageableKind.Player, HitPoints = playerHitPoints }
                },
                Terminal = new TerminalStateFixture
                {
                    ProgressState = TerminalLifecycleState.Active,
                    RulesetVersion = 1,
                    Seed = 0x1234,
                    EnteredTick = 7,
                    NextTicketIndex = 2,
                    Tickets = new[]
                    {
                        Ticket(103, 2, TerminalTicketLifecycle.Resolved, 0),
                        Ticket(101, 0, TerminalTicketLifecycle.Pending, 0),
                        Ticket(102, 1, TerminalTicketLifecycle.Active, 501)
                    }
                }
            });
        }

        private static TerminalTicketFixture Ticket(ulong id, int index,
            TerminalTicketLifecycle lifecycle, ulong activeEntityValue)
        {
            return new TerminalTicketFixture
            {
                TicketId = id,
                QueueIndex = index,
                EnemyArchetypeId = index == 1 ? "Runner" : "Walker",
                OriginalScheduledTick = 20 + index,
                InitialRouteChoiceKey = "Face" + index,
                RulesetVersion = 1,
                Lifecycle = lifecycle,
                ActiveEntityValue = activeEntityValue
            };
        }

        private static string TicketEvidenceKey(TerminalTicketContractEvidence value) =>
            value.TicketId + "|" + value.QueueIndex + "|" + value.EnemyArchetypeId + "|" +
            value.OriginalScheduledTick + "|" + value.InitialRouteChoiceKey + "|" +
            value.RulesetVersion + "|" + value.Lifecycle + "|" +
            (value.ActiveEntityId.HasValue ? value.ActiveEntityId.Value.ToString() : "null");

        private static DamagePacket Packet(RunState state, EntityId target, float amount, DamageKind kind) =>
            new DamagePacket(state.IssueEventSequence(), state.FixedTick, new EntityId(30),
                new EntityId(31), target, kind, amount, Vector3.zero, Vector3.zero, new FireSequenceId(1));

        private static string V1Json(SourceState source)
        {
            string rng = "{\"state0\":\"0000000000000001\",\"state1\":\"0000000000000002\"}";
            return "{\"schemaVersion\":1,\"buildVersion\":\"0.1.0-m1\",\"configurationVersion\":\"3.0\"," +
                "\"runId\":\"0000000000000001\",\"fixedTick\":10,\"nextEventSequence\":\"0000000000000001\"," +
                "\"phase\":0,\"playerLife\":0,\"partnerLife\":0,\"tower\":0,\"playerZone\":0,\"playerTraversal\":0," +
                "\"source\":" + (int)source + ",\"playerDeath\":null,\"partnerDeath\":null," +
                "\"spawnRng\":" + rng + ",\"evasionRng\":" + rng + ",\"partnerCombatRng\":" + rng +
                ",\"lootRng\":" + rng + ",\"cosmeticRng\":" + rng + ",\"damageables\":[],\"appliedDamageEvents\":[]}";
        }

        private sealed class SequentialIdentitySource : ITelemetryIdentitySource
        {
            private int next = 1;
            public Guid NextEventId()
            {
                byte[] bytes = new byte[16];
                bytes[15] = (byte)next++;
                return new Guid(bytes);
            }
        }
    }
}
