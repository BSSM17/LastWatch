using System;
using System.Collections.Generic;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Core.Partner
{
    [Serializable]
    public sealed class FriendlyHitIncident
    {
        public ulong IncidentId;
        public ulong FireSequenceId;
        public int FirstHitTick;
        public int LastHitTick;
        public int HitCount;
        public float TotalDamage;
        public PartnerLifeState StateBefore;
        public PartnerLifeState StateAfter;
        public bool PlayerCausedDowning;
        public bool PlayerCausedDeath;
        public bool ConfirmedPartnerExecution;
        public PartnerReactionStage ReactionStage;
    }

    [Serializable]
    public sealed class PartnerRuntimeState
    {
        internal M4EntryEventContext M4ActiveEntryContext;
        internal readonly Dictionary<ulong,M4EntryEventContext> M4FactContexts = new Dictionary<ulong,M4EntryEventContext>();
        public PartnerRole Role;
        public PartnerDecisionState Decision;
        public PartnerMovementState Movement;
        public PartnerFireState Fire;
        public PartnerLifeState Life = PartnerLifeState.Healthy;
        public string DesignatedFaceId;
        public string CurrentNodeId;
        public string ReservedNodeId;
        public int ArAmmo = 90;
        public int Parts;
        public float HitPoints = 100f;
        public int? DownedTick;
        public int? BleedoutTick;
        public int LastAcknowledgementTick = -1;
        public PartnerSignalKind? LastSignal;
        public int TechnicalDamageOrDeathCount;
        public ulong NextFriendlyIncidentId = 1;
        public ulong? LastReactionIncidentId;
        public bool ConfirmedPartnerExecution;
        public PartnerDeathCause DeathCause;
        public int? DeathTick;
        public ulong? DeathEventSequence;
        public ulong PartnerCombatRngState0 = 1;
        public ulong PartnerCombatRngState1 = 2;
        public List<FriendlyHitIncident> FriendlyIncidents = new List<FriendlyHitIncident>();
        public ulong NextData40Cursor = 1;
        public ulong Data40AcknowledgedCursor;
        public List<Data40Fact> Data40Facts = new List<Data40Fact>();
        public ExchangeTransaction PendingExchange;
        public ulong NextExchangeTransactionId = 1;
        public M4PartnerMovementProgress MovementProgress;
        public ulong NextMovementPlanId = 1;
        public M4PartnerExchangeFlow ExchangeFlow;

        public PartnerObservation Observe() => new PartnerObservation(this);
    }

    [Serializable]
    public sealed class ExchangeTransaction
    {
        public ulong TransactionId;
        public ExchangeResource Resource;
        public ExchangeDirection Direction;
        public int Amount;
        public int CompletionTick;
        public bool Completed;
        public bool Cancelled;
        public int StartTick;
        public int TicksPerSecond;
        public int? TerminalTick;
        public string CancellationReason;
        public ulong TargetPartnerEntityValue;
        public string RunIdentity;
        public int NightOrdinal;
        public string StationDescriptorId;
        public float PlayerHitPointsAtBegin;
        public float PartnerHitPointsAtBegin;
    }
}
