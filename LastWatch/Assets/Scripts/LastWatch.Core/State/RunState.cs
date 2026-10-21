using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;
using LastWatch.Core.Simulation;
using LastWatch.Core.Terminal;

namespace LastWatch.Core.State
{
    public enum RunPhase
    {
        Dawn = 0,
        Scavenge = 1,
        Allocation = 2,
        Defense = 3,
        FinalCollapseWarning = 4,
        CollapseImpact = 5,
        Afterfall = 6,
        Ending = 7,
        TerminalOverrun = 8,
        TerminalAfterfall = 9
    }
    public enum PlayerLifeState { Alive = 0, Dead = 1 }
    public enum PartnerLifeState { Healthy = 0, Injured = 1, Downed = 2, Stabilized = 3, Dead = 4 }
    public enum TowerState { Stable = 0, OneFaceLost = 1, CollapseWarning = 2, Collapsed = 3 }
    public enum LocationZone
    {
        UpperSafeZone = 0,
        UpperDeck = UpperSafeZone,
        LowerAccess = 1,
        GroundDanger = 2,
        SafePerimeter = 3
    }
    public enum TraversalState { None = 0, Ascending = 1, Descending = 2, EmergencyDescending = 3 }

    // Schema-v1 compatibility input only. Schema v2 authority is split between
    // WorldSourceState and SourceAccessState.
    public enum SourceState { Unknown = 0, Located = 1, Armed = 2, Destroyed = 3 }

    public readonly struct DeathFact
    {
        public int Tick { get; }
        public ulong EventSequence { get; }
        public DamageKind Cause { get; }
        public DeathFact(int tick, ulong eventSequence, DamageKind cause)
        {
            if (tick < 0 || eventSequence == 0) throw new ArgumentOutOfRangeException();
            Tick = tick;
            EventSequence = eventSequence;
            Cause = cause;
        }
    }

    public readonly struct DamageableSnapshot : IDamageable
    {
        public EntityId EntityId { get; }
        public DamageableKind Kind { get; }
        public float HitPoints { get; }
        public bool IsResolved { get; }
        // Only active M3 tickets carry this value. A resolved/recycled pool slot has no
        // current ticket binding, even though its previous HP record remains inspectable.
        public ulong? ActiveEnemyTicketId { get; }
        internal DamageableSnapshot(EntityId id, DamageableKind kind, float hp, bool resolved, ulong? activeEnemyTicketId)
        { EntityId = id; Kind = kind; HitPoints = hp; IsResolved = resolved; ActiveEnemyTicketId = activeEnemyTicketId; }
    }

    internal sealed class DamageableRecord
    {
        internal EntityId Id;
        internal DamageableKind Kind;
        internal float HitPoints;
        internal bool IsResolved;
        internal ulong? ActiveEnemyTicketId;
        internal DamageableSnapshot Snapshot() => new DamageableSnapshot(Id, Kind, HitPoints, IsResolved, ActiveEnemyTicketId);
    }

    public sealed class RunState
    {
        private readonly Dictionary<ulong, DamageableRecord> damageables = new Dictionary<ulong, DamageableRecord>();
        private readonly HashSet<ulong> appliedDamageEvents = new HashSet<ulong>();
        private readonly List<CommittedFactRecord> committedFacts = new List<CommittedFactRecord>();
        private readonly EventSequenceAllocator sequences;

        public const int SchemaVersion = 2;
        public RunId RunId { get; }
        public string BuildVersion { get; }
        public string ConfigurationVersion { get; }
        public RunContextDefinition RunContext { get; internal set; }
        internal M4EntryEventContext M4ActiveEntryContext { get; set; }
        public int FixedTick { get; private set; }
        public ulong NextEventSequence => sequences.NextValue;
        public RunPhase Phase { get; internal set; }
        public PlayerLifeState PlayerLife { get; internal set; }
        public PartnerLifeState PartnerLife { get; internal set; }
        public TowerState Tower { get; internal set; }
        public LocationZone PlayerZone { get; internal set; }
        public TraversalState PlayerTraversal { get; internal set; }
        public WorldSourceState WorldSource { get; internal set; }
        public SourceAccessState SourceAccess { get; internal set; }
        public int? SourceManifestedTick { get; internal set; }
        public int? SourceSealedTick { get; internal set; }
        public ulong? SourceSealSourceEventId { get; internal set; }
        public int? SourceOverloadCommitTick { get; internal set; }
        public ulong? SourceOverloadCommitSequence { get; internal set; }
        public int? SourceOverloadCompletionTick { get; internal set; }
        public SourceOverloadCommitOrigin SourceOverloadCommitOrigin { get; internal set; }
        public int? SourceDestroyedTick { get; internal set; }
        public ulong? SourceDestroySequence { get; internal set; }
        public bool ConfirmedPartnerExecution { get; internal set; }
        public int? ConfirmedPartnerExecutionTick { get; internal set; }
        public ulong? ConfirmedPartnerExecutionSequence { get; internal set; }
        public TerminalState Terminal { get; internal set; }
        public CommittedOutcome? Outcome { get; internal set; }
        internal ulong NextCommittedFactCursor { get; set; } = 1;
        internal ulong CommittedFactAcknowledgedCursor { get; set; }

        public SourceState Source
        {
            get
            {
                if (WorldSource == WorldSourceState.Destroyed) return SourceState.Destroyed;
                if (SourceOverloadCommitTick.HasValue) return SourceState.Armed;
                if (SourceAccess == SourceAccessState.Manifested) return SourceState.Located;
                return SourceState.Unknown;
            }
            internal set => ApplyLegacySource(value);
        }
        public DeathFact? PlayerDeath { get; internal set; }
        public DeathFact? PartnerDeath { get; internal set; }
        public RngStreamState SpawnRng { get; internal set; }
        public RngStreamState EvasionRng { get; internal set; }
        public RngStreamState PartnerCombatRng { get; internal set; }
        public RngStreamState LootRng { get; internal set; }
        public RngStreamState CosmeticRng { get; internal set; }

        internal RunState(RunId runId, string buildVersion, string configVersion, int fixedTick, ulong nextSequence)
        {
            if (!runId.IsValid) throw new ArgumentException("RunId is required.", nameof(runId));
            if (string.IsNullOrWhiteSpace(buildVersion) || string.IsNullOrWhiteSpace(configVersion)) throw new ArgumentException("Versions are required.");
            if (fixedTick < 0) throw new ArgumentOutOfRangeException(nameof(fixedTick));
            RunId = runId; BuildVersion = buildVersion; ConfigurationVersion = configVersion; FixedTick = fixedTick;
            sequences = new EventSequenceAllocator(nextSequence);
            Phase = RunPhase.Dawn; PlayerLife = PlayerLifeState.Alive; PartnerLife = PartnerLifeState.Healthy;
            Tower = TowerState.Stable; PlayerZone = LocationZone.UpperSafeZone;
            WorldSource = WorldSourceState.Active; SourceAccess = SourceAccessState.Dormant;
            SourceOverloadCommitOrigin = SourceOverloadCommitOrigin.None;
            Terminal = TerminalState.Empty;
        }

        public ulong IssueEventSequence() => sequences.Issue();
        public void AdvanceOneTick() { if (FixedTick == int.MaxValue) throw new OverflowException(); FixedTick++; }
        public bool TryGetDamageable(EntityId id, out DamageableSnapshot snapshot)
        {
            if (damageables.TryGetValue(id.Value, out DamageableRecord record)) { snapshot = record.Snapshot(); return true; }
            snapshot = default; return false;
        }
        public RunStateSnapshot Snapshot() => RunStateSnapshot.Create(this);

        internal bool TryGetRecord(EntityId id, out DamageableRecord record) => damageables.TryGetValue(id.Value, out record);
        internal bool IsDamageApplied(ulong sequence) => appliedDamageEvents.Contains(sequence);
        internal void MarkDamageApplied(ulong sequence) => appliedDamageEvents.Add(sequence);
        internal void AddDamageable(EntityId id, DamageableKind kind, float hp, bool resolved)
        {
            if (!id.IsValid || hp < 0 || float.IsNaN(hp) || float.IsInfinity(hp)) throw new ArgumentException("Invalid damageable.");
            if (damageables.ContainsKey(id.Value)) throw new InvalidOperationException("Duplicate EntityId.");
            damageables.Add(id.Value, new DamageableRecord { Id = id, Kind = kind, HitPoints = hp, IsResolved = resolved });
        }
        // Enemy HP stays in the same canonical RunState record used by DamageResolver.
        // This is the sole rearm path for a recycled EnemyPool EntityId.
        internal void ArmEnemyIncarnation(EntityId id, ulong ticketId, float hp)
        {
            if (!id.IsValid || ticketId == 0 || hp <= 0f || float.IsNaN(hp) || float.IsInfinity(hp))
                throw new ArgumentException("Invalid enemy incarnation.");
            if (!damageables.TryGetValue(id.Value, out DamageableRecord record))
            {
                damageables.Add(id.Value, new DamageableRecord { Id = id, Kind = DamageableKind.Enemy, HitPoints = hp, IsResolved = false, ActiveEnemyTicketId = ticketId });
                return;
            }
            if (record.Kind != DamageableKind.Enemy || record.ActiveEnemyTicketId.HasValue)
                throw new InvalidOperationException("Enemy pool slot is not available for a new incarnation.");
            record.HitPoints = hp; record.IsResolved = false; record.ActiveEnemyTicketId = ticketId;
        }
        internal void RestoreActiveEnemyIncarnation(EntityId id, ulong ticketId)
        {
            if (!id.IsValid || ticketId == 0 || !damageables.TryGetValue(id.Value, out DamageableRecord record) ||
                record.Kind != DamageableKind.Enemy || record.IsResolved || record.HitPoints <= 0f ||
                record.ActiveEnemyTicketId.HasValue)
                throw new FormatException("Active enemy incarnation cannot be restored from RunState.");
            record.ActiveEnemyTicketId = ticketId;
        }
        internal bool IsActiveEnemyIncarnation(EntityId id, ulong ticketId)
        {
            return ticketId != 0 && damageables.TryGetValue(id.Value, out DamageableRecord record) &&
                record.Kind == DamageableKind.Enemy && record.ActiveEnemyTicketId == ticketId;
        }
        internal bool DisarmResolvedEnemyIncarnation(EntityId id, ulong ticketId)
        {
            if (!IsActiveEnemyIncarnation(id, ticketId) || !damageables.TryGetValue(id.Value, out DamageableRecord record) || !record.IsResolved)
                return false;
            record.ActiveEnemyTicketId = null;
            return true;
        }
        internal IEnumerable<DamageableRecord> ActiveEnemyIncarnations
        {
            get { foreach (DamageableRecord record in damageables.Values) if (record.Kind == DamageableKind.Enemy && record.ActiveEnemyTicketId.HasValue) yield return record; }
        }
        internal IEnumerable<DamageableRecord> DamageableRecords => damageables.Values;
        internal IEnumerable<ulong> AppliedDamageEvents => appliedDamageEvents;
        internal IEnumerable<CommittedFactRecord> CommittedFacts => committedFacts;
        internal void RestoreAppliedDamage(ulong sequence) { if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence)); appliedDamageEvents.Add(sequence); }
        internal void AppendCommittedDamage(CommittedDamageFact fact)
        {
            committedFacts.Add(new CommittedFactRecord(NextCommittedFactCursor++, fact, M4ActiveEntryContext));
        }
        internal void AppendCommittedActorTransition(CommittedActorStateTransitionFact fact)
        {
            committedFacts.Add(new CommittedFactRecord(NextCommittedFactCursor++, fact, M4ActiveEntryContext));
        }
        internal void RestoreCommittedFact(CommittedFactRecord fact)
        {
            if (fact.Cursor != NextCommittedFactCursor) throw new FormatException("Committed fact cursor is not contiguous.");
            committedFacts.Add(fact); NextCommittedFactCursor++;
        }
        internal void RestoreM4FactContext(ulong cursor, M4EntryEventContext context)
        {
            if(cursor==0||cursor>(ulong)committedFacts.Count)throw new FormatException("Missing context fact cursor.");
            int index=checked((int)cursor-1);CommittedFactRecord f=committedFacts[index];
            committedFacts[index]=f.Kind==CommittedFactKind.Damage?new CommittedFactRecord(cursor,f.Damage,context):new CommittedFactRecord(cursor,f.ActorTransition,context);
        }

        internal void ApplyLegacySource(SourceState value)
        {
            WorldSource = value == SourceState.Destroyed ? WorldSourceState.Destroyed : WorldSourceState.Active;
            SourceAccess = value == SourceState.Unknown ? SourceAccessState.Dormant :
                value == SourceState.Located ? SourceAccessState.Manifested : SourceAccessState.OverloadCommitted;
        }
    }

    public sealed class RunStateSnapshot
    {
        public RunId RunId { get; private set; }
        public int FixedTick { get; private set; }
        public ulong NextEventSequence { get; private set; }
        public RunPhase Phase { get; private set; }
        public PlayerLifeState PlayerLife { get; private set; }
        public PartnerLifeState PartnerLife { get; private set; }
        public TowerState Tower { get; private set; }
        public LocationZone PlayerZone { get; private set; }
        public TraversalState PlayerTraversal { get; private set; }
        public DeathFact? PlayerDeath { get; private set; }
        public DeathFact? PartnerDeath { get; private set; }
        public DamageableSnapshot[] Damageables { get; private set; }
        public SourceStateSnapshot Source { get; private set; }
        public TerminalStateSnapshot Terminal { get; private set; }
        public CommittedOutcome? Outcome { get; private set; }

        internal static RunStateSnapshot Create(RunState state)
        {
            var values = new List<DamageableSnapshot>();
            foreach (DamageableRecord record in state.DamageableRecords) values.Add(record.Snapshot());
            values.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
            return new RunStateSnapshot { RunId = state.RunId, FixedTick = state.FixedTick,
                NextEventSequence = state.NextEventSequence, Phase = state.Phase, PlayerLife = state.PlayerLife,
                PartnerLife = state.PartnerLife, Tower = state.Tower, PlayerZone = state.PlayerZone,
                PlayerTraversal = state.PlayerTraversal, PlayerDeath = state.PlayerDeath, PartnerDeath = state.PartnerDeath,
                Damageables = values.ToArray(), Source = SourceStateSnapshot.Create(state),
                Terminal = state.Terminal.Snapshot(), Outcome = state.Outcome };
        }
    }
}
