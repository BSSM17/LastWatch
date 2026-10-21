using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.State;
using LastWatch.Telemetry.Sinks;

namespace LastWatch.Telemetry.Projection
{
    public enum TelemetryActorLifeState { Alive, Healthy, Injured, Downed, Stabilized, Dead }

    public readonly struct AuthoritativeActorStateTransition
    {
        public EntityId ActorId { get; }
        public DamageableKind ActorKind { get; }
        public TelemetryActorLifeState PreviousState { get; }
        public TelemetryActorLifeState CurrentState { get; }
        public int FixedTick { get; }
        public ulong EventSequence { get; }
        public DeathFact? DeathFact { get; }
        public DamageableKind? LethalSourceActorKind { get; }
        public EntityId LethalSourceEntityId { get; }

        public AuthoritativeActorStateTransition(EntityId actorId, DamageableKind actorKind,
            TelemetryActorLifeState previousState, TelemetryActorLifeState currentState,
            int fixedTick, ulong eventSequence, DeathFact? deathFact,
            DamageableKind? lethalSourceActorKind, EntityId lethalSourceEntityId)
        {
            ActorId = actorId;
            ActorKind = actorKind;
            PreviousState = previousState;
            CurrentState = currentState;
            FixedTick = fixedTick;
            EventSequence = eventSequence;
            DeathFact = deathFact;
            LethalSourceActorKind = lethalSourceActorKind;
            LethalSourceEntityId = lethalSourceEntityId;
        }
    }

    public enum TelemetryProjectionStatus { Recorded, DuplicateSuppressed, NonCommittedFact, InvalidAuthoritativeFact, DispatchFailed }

    public readonly struct TelemetryProjectionResult
    {
        public TelemetryProjectionStatus Status { get; }
        public TelemetryDispatchResult Dispatch { get; }
        public TelemetryProjectionResult(TelemetryProjectionStatus status, TelemetryDispatchResult dispatch)
        { Status = status; Dispatch = dispatch; }
    }

    public sealed class T1TelemetryProjector
    {
        private readonly SafeTelemetryDispatcher dispatcher;
        private readonly ITelemetryIdentitySource identities;
        private readonly HashSet<string> projectionLedger = new HashSet<string>(StringComparer.Ordinal);

        public T1TelemetryProjector(SafeTelemetryDispatcher dispatcher, ITelemetryIdentitySource identities)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        }

        public TelemetryProjectionResult ProjectDamageApplied(TelemetryContext context,
            in DamagePacket packet, in DamageReceipt receipt, DamageableKind targetKind)
        {
            if (receipt.Status != DamageApplyStatus.Applied)
                return Result(TelemetryProjectionStatus.NonCommittedFact);
            if (receipt.EventSequence != packet.EventSequence || receipt.Target != packet.Target)
                return Result(TelemetryProjectionStatus.InvalidAuthoritativeFact);
            string key = Key(TelemetryEventNames.DamageApplied, packet.EventSequence, packet.Target.ToString());
            if (!projectionLedger.Add(key)) return Result(TelemetryProjectionStatus.DuplicateSuppressed);
            var payload = new TelemetryPayload()
                .Add("amount", TelemetryValue.Number(packet.Amount))
                .Add("becameResolved", TelemetryValue.Boolean(receipt.BecameResolved))
                .Add("damageKind", TelemetryValue.String(packet.Kind.ToString()))
                .Add("fireSequenceId", IdOrNull(packet.FireSequence.IsValid, packet.FireSequence.ToString()))
                .Add("hitPointsAfter", TelemetryValue.Number(receipt.HitPointsAfter))
                .Add("hitPointsBefore", TelemetryValue.Number(receipt.HitPointsBefore))
                .Add("instigatorEntityId", IdOrNull(packet.Instigator.IsValid, packet.Instigator.ToString()))
                .Add("sourceEntityId", IdOrNull(packet.Source.IsValid, packet.Source.ToString()))
                .Add("targetEntityId", TelemetryValue.String(packet.Target.ToString()))
                .Add("targetKind", TelemetryValue.String(targetKind.ToString()));
            return Dispatch(new TelemetryEventEnvelope(identities.NextEventId(), context,
                packet.EventSequence, packet.FixedTick, TelemetryEventNames.DamageApplied, payload));
        }

        public TelemetryProjectionResult ProjectDamageApplied(TelemetryContext context,
            in CommittedDamageFact fact)
        {
            string key = Key(TelemetryEventNames.DamageApplied, fact.EventSequence, fact.TargetEntityId.ToString());
            if (!projectionLedger.Add(key)) return Result(TelemetryProjectionStatus.DuplicateSuppressed);
            var payload = new TelemetryPayload()
                .Add("amount", TelemetryValue.Number(fact.Amount))
                .Add("becameResolved", TelemetryValue.Boolean(fact.BecameResolved))
                .Add("damageKind", TelemetryValue.String(fact.DamageKind.ToString()))
                .Add("fireSequenceId", IdOrNull(fact.FireSequenceId.IsValid, fact.FireSequenceId.ToString()))
                .Add("hitPointsAfter", TelemetryValue.Number(fact.HitPointsAfter))
                .Add("hitPointsBefore", TelemetryValue.Number(fact.HitPointsBefore))
                .Add("instigatorEntityId", IdOrNull(fact.InstigatorEntityId.IsValid, fact.InstigatorEntityId.ToString()))
                .Add("sourceEntityId", IdOrNull(fact.SourceEntityId.IsValid, fact.SourceEntityId.ToString()))
                .Add("targetEntityId", TelemetryValue.String(fact.TargetEntityId.ToString()))
                .Add("targetKind", TelemetryValue.String(fact.TargetKind.ToString()));
            return Dispatch(new TelemetryEventEnvelope(identities.NextEventId(), context,
                fact.EventSequence, fact.FixedTick, TelemetryEventNames.DamageApplied, payload));
        }

        public TelemetryProjectionResult ProjectActorStateChanged(TelemetryContext context,
            in AuthoritativeActorStateTransition transition)
        {
            if (!ValidActorTransition(transition)) return Result(TelemetryProjectionStatus.InvalidAuthoritativeFact);
            string key = Key(TelemetryEventNames.ActorStateChanged, transition.EventSequence, transition.ActorId.ToString());
            if (!projectionLedger.Add(key)) return Result(TelemetryProjectionStatus.DuplicateSuppressed);
            var payload = new TelemetryPayload()
                .Add("actorId", TelemetryValue.String(transition.ActorId.ToString()))
                .Add("actorKind", TelemetryValue.String(transition.ActorKind.ToString()))
                .Add("currentLifeState", TelemetryValue.String(transition.CurrentState.ToString()))
                .Add("previousLifeState", TelemetryValue.String(transition.PreviousState.ToString()));
            return Dispatch(new TelemetryEventEnvelope(identities.NextEventId(), context,
                transition.EventSequence, transition.FixedTick, TelemetryEventNames.ActorStateChanged, payload));
        }

        public TelemetryProjectionResult ProjectActorDeathCommitted(TelemetryContext context,
            in AuthoritativeActorStateTransition transition)
        {
            if (!ValidActorTransition(transition) || transition.CurrentState != TelemetryActorLifeState.Dead ||
                transition.PreviousState == TelemetryActorLifeState.Dead || !transition.DeathFact.HasValue)
                return Result(TelemetryProjectionStatus.InvalidAuthoritativeFact);
            DeathFact death = transition.DeathFact.Value;
            if (death.Tick != transition.FixedTick || death.EventSequence != transition.EventSequence)
                return Result(TelemetryProjectionStatus.InvalidAuthoritativeFact);
            string key = Key(TelemetryEventNames.ActorDeathCommitted, transition.EventSequence, transition.ActorId.ToString());
            if (!projectionLedger.Add(key)) return Result(TelemetryProjectionStatus.DuplicateSuppressed);
            var payload = new TelemetryPayload()
                .Add("actorId", TelemetryValue.String(transition.ActorId.ToString()))
                .Add("actorKind", TelemetryValue.String(transition.ActorKind.ToString()))
                .Add("deathCause", TelemetryValue.String(death.Cause.ToString()))
                .Add("deathSequence", TelemetryValue.String(death.EventSequence.ToString("x16")))
                .Add("deathTick", TelemetryValue.Integer(death.Tick))
                .Add("lethalDamageEventSequence", TelemetryValue.String(death.EventSequence.ToString("x16")))
                .Add("lethalSourceActorKind", transition.LethalSourceActorKind.HasValue
                    ? TelemetryValue.String(transition.LethalSourceActorKind.Value.ToString()) : TelemetryValue.Null())
                .Add("lethalSourceEntityId", IdOrNull(transition.LethalSourceEntityId.IsValid,
                    transition.LethalSourceEntityId.ToString()))
                .Add("previousLifeState", TelemetryValue.String(transition.PreviousState.ToString()));
            return Dispatch(new TelemetryEventEnvelope(identities.NextEventId(), context,
                transition.EventSequence, transition.FixedTick, TelemetryEventNames.ActorDeathCommitted, payload));
        }

        public TelemetryProjectionResult ProjectActorStateChanged(TelemetryContext context,
            in CommittedActorStateTransitionFact fact)
        {
            return ProjectActorStateChanged(context, ToTransition(fact));
        }

        public TelemetryProjectionResult ProjectActorDeathCommitted(TelemetryContext context,
            in CommittedActorStateTransitionFact fact)
        {
            return ProjectActorDeathCommitted(context, ToTransition(fact));
        }

        public TelemetryProjectionResult ProjectInvariantViolation(TelemetryContext context, ulong sequence,
            int fixedTick, string invariantId, string classification)
        {
            var payload = new TelemetryPayload()
                .Add("classification", TelemetryValue.String(classification))
                .Add("invariantId", TelemetryValue.String(invariantId));
            return Dispatch(new TelemetryEventEnvelope(identities.NextEventId(), context, sequence, fixedTick,
                TelemetryEventNames.InvariantViolation, payload));
        }

        public TelemetryProjectionResult ProjectTelemetryHealth(TelemetryContext context, ulong sequence,
            int fixedTick, string condition, long droppedEventCount)
        {
            var payload = new TelemetryPayload()
                .Add("condition", TelemetryValue.String(condition))
                .Add("droppedEventCount", TelemetryValue.Integer(droppedEventCount));
            return Dispatch(new TelemetryEventEnvelope(identities.NextEventId(), context, sequence, fixedTick,
                TelemetryEventNames.TelemetryHealth, payload));
        }

        private static bool ValidActorTransition(in AuthoritativeActorStateTransition value) =>
            value.ActorId.IsValid &&
            (value.ActorKind == DamageableKind.Player || value.ActorKind == DamageableKind.Partner) &&
            value.FixedTick >= 0 && value.EventSequence != 0 && value.PreviousState != value.CurrentState;

        private static AuthoritativeActorStateTransition ToTransition(in CommittedActorStateTransitionFact fact)
        {
            return new AuthoritativeActorStateTransition(fact.ActorEntityId, fact.ActorKind,
                MapLifeState(fact.PreviousLifeState), MapLifeState(fact.CurrentLifeState),
                fact.FixedTick, fact.EventSequence, fact.Death, fact.LethalSourceActorKind,
                fact.LethalSourceEntityId.GetValueOrDefault());
        }

        private static TelemetryActorLifeState MapLifeState(ActorLifeStateCode value)
        {
            switch (value)
            {
                case ActorLifeStateCode.PlayerAlive: return TelemetryActorLifeState.Alive;
                case ActorLifeStateCode.PlayerDead: return TelemetryActorLifeState.Dead;
                case ActorLifeStateCode.PartnerHealthy: return TelemetryActorLifeState.Healthy;
                case ActorLifeStateCode.PartnerInjured: return TelemetryActorLifeState.Injured;
                case ActorLifeStateCode.PartnerDowned: return TelemetryActorLifeState.Downed;
                case ActorLifeStateCode.PartnerStabilized: return TelemetryActorLifeState.Stabilized;
                case ActorLifeStateCode.PartnerDead: return TelemetryActorLifeState.Dead;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private TelemetryProjectionResult Dispatch(TelemetryEventEnvelope value)
        {
            TelemetryDispatchResult dispatch = dispatcher.Dispatch(value);
            return new TelemetryProjectionResult(dispatch.Status == TelemetryDispatchStatus.Recorded
                ? TelemetryProjectionStatus.Recorded : TelemetryProjectionStatus.DispatchFailed, dispatch);
        }

        private static TelemetryProjectionResult Result(TelemetryProjectionStatus status) =>
            new TelemetryProjectionResult(status, default);
        private static string Key(string eventName, ulong sequence, string discriminator) =>
            eventName + "|" + sequence.ToString("x16") + "|" + discriminator;
        private static TelemetryValue IdOrNull(bool valid, string value) => valid ? TelemetryValue.String(value) : TelemetryValue.Null();
    }
}
