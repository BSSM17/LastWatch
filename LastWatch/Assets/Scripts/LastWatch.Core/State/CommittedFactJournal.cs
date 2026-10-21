using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;

namespace LastWatch.Core.State
{
    public enum CommittedFactKind { Damage = 0, ActorStateTransition = 1 }
    public enum ActorLifeStateCode
    {
        PlayerAlive = 0, PlayerDead = 1,
        PartnerHealthy = 10, PartnerInjured = 11, PartnerDowned = 12, PartnerStabilized = 13, PartnerDead = 14
    }

    public readonly struct CommittedDamageFact
    {
        public int FixedTick { get; } public ulong EventSequence { get; }
        public EntityId SourceEntityId { get; } public EntityId InstigatorEntityId { get; } public EntityId TargetEntityId { get; }
        public DamageableKind? SourceActorKind { get; } public DamageableKind TargetKind { get; }
        public DamageKind DamageKind { get; } public float Amount { get; } public FireSequenceId FireSequenceId { get; }
        public float HitPointsBefore { get; } public float HitPointsAfter { get; } public bool BecameResolved { get; }

        public CommittedDamageFact(in DamagePacket packet, DamageableKind? sourceKind, DamageableKind targetKind, in DamageReceipt receipt)
        {
            FixedTick = packet.FixedTick; EventSequence = packet.EventSequence; SourceEntityId = packet.Source;
            InstigatorEntityId = packet.Instigator; TargetEntityId = packet.Target; SourceActorKind = sourceKind;
            TargetKind = targetKind; DamageKind = packet.Kind; Amount = packet.Amount; FireSequenceId = packet.FireSequence;
            HitPointsBefore = receipt.HitPointsBefore; HitPointsAfter = receipt.HitPointsAfter; BecameResolved = receipt.BecameResolved;
        }
    }

    public readonly struct CommittedActorStateTransitionFact
    {
        public EntityId ActorEntityId { get; } public DamageableKind ActorKind { get; }
        public ActorLifeStateCode PreviousLifeState { get; } public ActorLifeStateCode CurrentLifeState { get; }
        public int FixedTick { get; } public ulong EventSequence { get; } public DeathFact? Death { get; }
        public ulong? LethalDamageEventSequence { get; } public DamageableKind? LethalSourceActorKind { get; }
        public EntityId? LethalSourceEntityId { get; }

        public CommittedActorStateTransitionFact(EntityId actorEntityId, DamageableKind actorKind,
            ActorLifeStateCode previousLifeState, ActorLifeStateCode currentLifeState, int fixedTick,
            ulong eventSequence, DeathFact? death, ulong? lethalSequence, DamageableKind? lethalSourceKind,
            EntityId? lethalSourceEntityId)
        {
            ActorEntityId = actorEntityId; ActorKind = actorKind; PreviousLifeState = previousLifeState;
            CurrentLifeState = currentLifeState; FixedTick = fixedTick; EventSequence = eventSequence; Death = death;
            LethalDamageEventSequence = lethalSequence; LethalSourceActorKind = lethalSourceKind;
            LethalSourceEntityId = lethalSourceEntityId;
        }
    }

    public readonly struct CommittedFactRecord
    {
        public M4EntryEventContext M4Context { get; }
        public ulong Cursor { get; } public CommittedFactKind Kind { get; }
        public CommittedDamageFact Damage { get; } public CommittedActorStateTransitionFact ActorTransition { get; }
        internal CommittedFactRecord(ulong cursor, CommittedDamageFact damage, M4EntryEventContext context = null)
        { Cursor = cursor; Kind = CommittedFactKind.Damage; Damage = damage; ActorTransition = default; M4Context = context; }
        internal CommittedFactRecord(ulong cursor, CommittedActorStateTransitionFact transition, M4EntryEventContext context = null)
        { Cursor = cursor; Kind = CommittedFactKind.ActorStateTransition; Damage = default; ActorTransition = transition; M4Context = context; }
    }

    public sealed class CommittedFactJournalSnapshot
    {
        public ulong AcknowledgedCursor { get; internal set; }
        public ulong LastCommittedCursor { get; internal set; }
        public CommittedFactRecord[] Records { get; internal set; }
    }

    public readonly struct CommittedFactEnvelopeContext
    {
        public RunId RunId { get; } public string BuildVersion { get; } public string ConfigurationVersion { get; }
        public string RulesetHash { get; } public string ContentVersion { get; }
        public string Mode { get; } public string Difficulty { get; } public int? Night { get; }
        public bool IsPreMilestoneUnavailable { get; } public bool IsStoredInRunState { get; }
        public RunPhase Phase { get; }
        public CommittedFactEnvelopeContext(RunId runId, string buildVersion, string configurationVersion,
            RunContextDefinition runContext, RunPhase phase, bool isStoredInRunState)
        {
            if (runContext == null) throw new ArgumentNullException(nameof(runContext));
            RunId = runId; BuildVersion = buildVersion; ConfigurationVersion = configurationVersion;
            RulesetHash = runContext.RulesetHash; ContentVersion = runContext.ContentVersion;
            Mode = runContext.ModeToken; Difficulty = runContext.DifficultyToken; Night = runContext.Night;
            IsPreMilestoneUnavailable = runContext.IsPreMilestoneUnavailable;
            IsStoredInRunState = isStoredInRunState; Phase = phase;
        }
    }

    public static class CommittedFactJournal
    {
        public static CommittedFactJournalSnapshot ReadAfter(RunState state, ulong cursor)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var values = new List<CommittedFactRecord>();
            foreach (CommittedFactRecord record in state.CommittedFacts) if (record.Cursor > cursor) values.Add(record);
            return new CommittedFactJournalSnapshot { AcknowledgedCursor = state.CommittedFactAcknowledgedCursor,
                LastCommittedCursor = state.NextCommittedFactCursor - 1, Records = values.ToArray() };
        }

        public static void AcknowledgeThrough(RunState state, ulong cursor)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cursor < state.CommittedFactAcknowledgedCursor || cursor >= state.NextCommittedFactCursor)
                throw new ArgumentOutOfRangeException(nameof(cursor));
            state.CommittedFactAcknowledgedCursor = cursor;
        }

        public static CommittedFactEnvelopeContext Context(RunState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            RunContextDefinition context = state.RunContext ?? RunContextDefinition.PreMilestoneUnavailable;
            return new CommittedFactEnvelopeContext(state.RunId, state.BuildVersion, state.ConfigurationVersion,
                context, state.Phase, state.RunContext != null);
        }
    }
}
