using System;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Persistence;
using LastWatch.Core.State;
using LastWatch.Telemetry.Projection;
using LastWatch.Telemetry.Sinks;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Telemetry.Tests
{
    [TestFixture]
    public sealed class T1TelemetryFoundationTests
    {
        [Test]
        public void Registry_ContainsExactlyTheFiveT1Events()
        {
            Assert.That(TelemetrySchemaRegistry.Events.Keys.OrderBy(x => x),
                Is.EqualTo(TelemetryEventNames.All.OrderBy(x => x)));
        }

        [Test]
        public void Schema_RejectsUnknownEventFieldEnumAndAbsolutePath()
        {
            var validator = new TelemetrySchemaValidator();
            Assert.That(validator.Validate(Envelope("Unknown", new TelemetryPayload())).IsValid, Is.False);
            Assert.That(validator.Validate(Envelope(TelemetryEventNames.TelemetryHealth,
                new TelemetryPayload().Add("condition", TelemetryValue.String("Unknown"))
                    .Add("droppedEventCount", TelemetryValue.Integer(0)))).ErrorCode, Is.EqualTo("ENUM_UNKNOWN"));
            Assert.That(validator.Validate(Envelope(TelemetryEventNames.InvariantViolation,
                new TelemetryPayload().Add("classification", TelemetryValue.String("SchemaRejected"))
                    .Add("invariantId", TelemetryValue.String("C:\\Users\\name")))).ErrorCode,
                Is.EqualTo("PATH_VALUE_FORBIDDEN"));
            Assert.That(validator.Validate(Envelope(TelemetryEventNames.TelemetryHealth,
                new TelemetryPayload().Add("condition", TelemetryValue.String("Healthy"))
                    .Add("droppedEventCount", TelemetryValue.Integer(0))
                    .Add("username", TelemetryValue.String("name")))).IsValid, Is.False);
        }

        [Test]
        public void Serialization_IsStableAndPayloadFieldsUseOrdinalOrder()
        {
            TelemetryEventEnvelope value = Envelope(TelemetryEventNames.InvariantViolation,
                new TelemetryPayload().Add("invariantId", TelemetryValue.String("T1-ORDER"))
                    .Add("classification", TelemetryValue.String("OrderingViolation")));
            string first = DeterministicTelemetryJson.Serialize(value);
            string second = DeterministicTelemetryJson.Serialize(value);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.IndexOf("\"classification\"", StringComparison.Ordinal),
                Is.LessThan(first.IndexOf("\"invariantId\"", StringComparison.Ordinal)));
        }

        [Test]
        public void InMemorySink_IsBounded_AllowsEqualSequenceAndRejectsDecreasingSequence()
        {
            var sink = new InMemoryTelemetrySink(2);
            sink.Record(HealthEnvelope(10));
            sink.Record(HealthEnvelope(10));
            sink.Record(HealthEnvelope(11));
            Assert.That(sink.Events.Count, Is.EqualTo(2));
            Assert.That(sink.DroppedEventCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => sink.Record(HealthEnvelope(9)));
        }

        [Test]
        public void Dispatcher_ContainsSinkFailure_WithoutChangingCoreState()
        {
            RunState state = CreateState();
            string beforeHash = RunStateJson.CanonicalHash(state);
            string beforeSave = RunStateJson.Serialize(state);
            var sink = new FailureInjectionTelemetrySink();
            var dispatcher = new SafeTelemetryDispatcher(sink);
            TelemetryDispatchResult result = dispatcher.Dispatch(HealthEnvelope(1));
            Assert.That(result.Status, Is.EqualTo(TelemetryDispatchStatus.SinkFailed));
            Assert.That(dispatcher.SinkFailureCount, Is.EqualTo(1));
            Assert.That(RunStateJson.CanonicalHash(state), Is.EqualTo(beforeHash));
            Assert.That(RunStateJson.Serialize(state), Is.EqualTo(beforeSave));
            Assert.That(state.FixedTick, Is.EqualTo(10));
            Assert.That(state.NextEventSequence, Is.EqualTo(1));
        }

        [Test]
        public void DamageProjection_UsesAppliedReceiptAndSuppressesDuplicateProjection()
        {
            var target = new EntityId(100);
            RunState state = CreateState(new DamageableFixture
            { EntityValue = target.Value, Kind = DamageableKind.Enemy, HitPoints = 20 });
            DamagePacket packet = new DamagePacket(state.IssueEventSequence(), state.FixedTick,
                new EntityId(1), new EntityId(2), target, DamageKind.Ballistic, 5,
                Vector3.zero, Vector3.zero, new FireSequenceId(3));
            DamageReceipt receipt = new DamageResolver(state).Apply(packet);
            var sink = new InMemoryTelemetrySink(8);
            var projector = Projector(sink);
            Assert.That(projector.ProjectDamageApplied(Context(), packet, receipt, DamageableKind.Enemy).Status,
                Is.EqualTo(TelemetryProjectionStatus.Recorded));
            Assert.That(projector.ProjectDamageApplied(Context(), packet, receipt, DamageableKind.Enemy).Status,
                Is.EqualTo(TelemetryProjectionStatus.DuplicateSuppressed));
            Assert.That(sink.Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void ActorDeathProjection_RequiresMatchingDeadFactAndEmitsExactlyOnce()
        {
            var transition = new AuthoritativeActorStateTransition(new EntityId(10), DamageableKind.Player,
                TelemetryActorLifeState.Alive, TelemetryActorLifeState.Dead, 7, 5,
                new DeathFact(7, 5, DamageKind.Melee), DamageableKind.Enemy, new EntityId(20));
            var sink = new InMemoryTelemetrySink(8);
            var projector = Projector(sink);
            Assert.That(projector.ProjectActorStateChanged(Context(), transition).Status,
                Is.EqualTo(TelemetryProjectionStatus.Recorded));
            Assert.That(projector.ProjectActorDeathCommitted(Context(), transition).Status,
                Is.EqualTo(TelemetryProjectionStatus.Recorded));
            Assert.That(projector.ProjectActorDeathCommitted(Context(), transition).Status,
                Is.EqualTo(TelemetryProjectionStatus.DuplicateSuppressed));
            Assert.That(sink.Events.Select(x => x.EventName),
                Is.EqualTo(new[] { TelemetryEventNames.ActorStateChanged, TelemetryEventNames.ActorDeathCommitted }));
        }

        [Test]
        public void NullSinkAndFailureSink_LeaveGameplayHashRngTickAndSaveIdentical()
        {
            RunState first = CreateState();
            RunState second = RunStateJson.Deserialize(RunStateJson.Serialize(first));
            Projector(new NullTelemetrySink()).ProjectTelemetryHealth(Context(), 1, first.FixedTick, "Healthy", 0);
            Projector(new FailureInjectionTelemetrySink()).ProjectTelemetryHealth(Context(), 1, second.FixedTick, "Healthy", 0);
            Assert.That(RunStateJson.CanonicalHash(second), Is.EqualTo(RunStateJson.CanonicalHash(first)));
            Assert.That(RunStateJson.Serialize(second), Is.EqualTo(RunStateJson.Serialize(first)));
        }

        [Test]
        public void CapacityOverflow_LeavesGameplayHashRngTickAndSaveIdentical()
        {
            RunState state = CreateState();
            string beforeHash = RunStateJson.CanonicalHash(state);
            string beforeSave = RunStateJson.Serialize(state);
            var sink = new InMemoryTelemetrySink(2);
            T1TelemetryProjector projector = Projector(sink);
            projector.ProjectTelemetryHealth(Context(), 1, state.FixedTick, "Healthy", 0);
            projector.ProjectTelemetryHealth(Context(), 2, state.FixedTick, "Healthy", 0);
            projector.ProjectTelemetryHealth(Context(), 3, state.FixedTick, "CapacityExceeded", 1);
            Assert.That(sink.DroppedEventCount, Is.EqualTo(1));
            Assert.That(RunStateJson.CanonicalHash(state), Is.EqualTo(beforeHash));
            Assert.That(RunStateJson.Serialize(state), Is.EqualTo(beforeSave));
            Assert.That(state.FixedTick, Is.EqualTo(10));
            Assert.That(state.NextEventSequence, Is.EqualTo(1));
        }

        [Test]
        public void SaveLoadDuplicateDamageReceipt_DoesNotReemit()
        {
            var target = new EntityId(100);
            RunState state = CreateState(new DamageableFixture
            { EntityValue = target.Value, Kind = DamageableKind.Enemy, HitPoints = 20 });
            DamagePacket packet = new DamagePacket(state.IssueEventSequence(), state.FixedTick,
                new EntityId(1), new EntityId(2), target, DamageKind.Ballistic, 5,
                Vector3.zero, Vector3.zero, new FireSequenceId(3));
            DamageReceipt first = new DamageResolver(state).Apply(packet);
            var sink = new InMemoryTelemetrySink(8);
            T1TelemetryProjector projector = Projector(sink);
            Assert.That(projector.ProjectDamageApplied(Context(), packet, first, DamageableKind.Enemy).Status,
                Is.EqualTo(TelemetryProjectionStatus.Recorded));

            RunState restored = RunStateJson.Deserialize(RunStateJson.Serialize(state));
            DamageReceipt duplicateAfterLoad = new DamageResolver(restored).Apply(packet);
            Assert.That(duplicateAfterLoad.Status, Is.EqualTo(DamageApplyStatus.Duplicate));
            Assert.That(projector.ProjectDamageApplied(Context(), packet, duplicateAfterLoad, DamageableKind.Enemy).Status,
                Is.EqualTo(TelemetryProjectionStatus.NonCommittedFact));
            Assert.That(sink.Events.Count, Is.EqualTo(1));
        }

        private static T1TelemetryProjector Projector(ITelemetrySink sink) =>
            new T1TelemetryProjector(new SafeTelemetryDispatcher(sink), new SequentialIdentitySource());

        private static TelemetryContext Context() => new TelemetryContext(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), new RunId(1), "0.1.0-m1",
            new string('a', 64), "graybox-001", "Story", "Normal", null, RunPhase.Defense);

        private static TelemetryEventEnvelope Envelope(string eventName, TelemetryPayload payload) =>
            new TelemetryEventEnvelope(Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Context(), 1, 10, eventName, payload);

        private static TelemetryEventEnvelope HealthEnvelope(ulong sequence) =>
            new TelemetryEventEnvelope(Guid.NewGuid(), Context(), sequence, 10,
                TelemetryEventNames.TelemetryHealth,
                new TelemetryPayload().Add("condition", TelemetryValue.String("Healthy"))
                    .Add("droppedEventCount", TelemetryValue.Integer(0)));

        private static RunState CreateState(params DamageableFixture[] values) =>
            RunStateFactory.CreateFixture(new RunStateFixture
            { RunValue = 1, FixedTick = 10, NextEventSequence = 1, Damageables = values });

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
