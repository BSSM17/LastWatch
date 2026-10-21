using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.State;
using LastWatch.Core.Terminal;

namespace LastWatch.Core.Persistence
{
    // DTOs deliberately retain their existing field names for JsonUtility compatibility.
    [Serializable] internal sealed class SchemaHeaderDto { public int schemaVersion; }
    [Serializable] internal sealed class RngDto { public string state0; public string state1; }
    [Serializable] internal sealed class DeathDto { public bool hasValue; public int tick; public string sequence; public DamageKind cause; }
    [Serializable] internal sealed class DamageableDto { public string entityId; public DamageableKind kind; public float hitPoints; public bool isResolved; }
    [Serializable] internal sealed class OptionalIntDto { public bool hasValue; public int value; }
    [Serializable] internal sealed class OptionalUlongDto { public bool hasValue; public string value; }

    [Serializable] internal sealed class RunStateV1Dto
    {
        public int schemaVersion; public string buildVersion; public string configurationVersion; public string runId;
        public int fixedTick; public string nextEventSequence; public RunPhase phase; public PlayerLifeState playerLife;
        public PartnerLifeState partnerLife; public TowerState tower; public LocationZone playerZone;
        public TraversalState playerTraversal; public SourceState source; public DeathDto playerDeath; public DeathDto partnerDeath;
        public RngDto spawnRng; public RngDto evasionRng; public RngDto partnerCombatRng; public RngDto lootRng; public RngDto cosmeticRng;
        public DamageableDto[] damageables; public string[] appliedDamageEvents;
    }

    [Serializable] internal sealed class TerminalTicketDto
    {
        public string ticketId; public int queueIndex; public string enemyArchetypeId; public int originalScheduledTick;
        public string initialRouteChoiceKey; public int rulesetVersion; public TerminalTicketLifecycle lifecycle;
        public OptionalUlongDto activeEntityId;
    }

    [Serializable] internal sealed class TerminalStateDto
    {
        public TerminalLifecycleState progressState; public bool hasEntry; public string entryRunId; public int rulesetVersion;
        public OptionalUlongDto seed; public OptionalIntDto enteredTick; public OptionalIntDto groundContinuationTick;
        public OptionalIntDto clearedTick; public OptionalIntDto nextTicketIndex; public TerminalTicketDto[] tickets;
    }

    [Serializable] internal sealed class OutcomeDto
    {
        public bool hasValue; public string pendingOutcomeId; public PersonalEndingId personal;
        public WorldOutcome world; public EndingCategory primaryCategory; public bool secretEnding;
    }

    [Serializable] internal sealed class CommittedFactDto
    {
        public string cursor; public CommittedFactKind kind; public int fixedTick; public string eventSequence;
        public string sourceEntityId; public string instigatorEntityId; public string targetEntityId;
        public bool hasSourceActorKind; public DamageableKind sourceActorKind; public DamageableKind targetKind;
        public DamageKind damageKind; public float amount; public string fireSequenceId;
        public float hitPointsBefore; public float hitPointsAfter; public bool becameResolved;
        public DamageableKind actorKind; public ActorLifeStateCode previousLifeState; public ActorLifeStateCode currentLifeState;
        public DeathDto death; public OptionalUlongDto lethalDamageEventSequence;
        public bool hasLethalSourceActorKind; public DamageableKind lethalSourceActorKind;
        public OptionalUlongDto lethalSourceEntityId;
    }

    [Serializable] internal sealed class RunStateV2Dto
    {
        public int schemaVersion; public string buildVersion; public string configurationVersion; public string runId;
        public int fixedTick; public string nextEventSequence; public RunPhase phase; public PlayerLifeState playerLife;
        public PartnerLifeState partnerLife; public TowerState tower; public LocationZone playerZone;
        public TraversalState playerTraversal; public DeathDto playerDeath; public DeathDto partnerDeath;
        public RngDto spawnRng; public RngDto evasionRng; public RngDto partnerCombatRng; public RngDto lootRng; public RngDto cosmeticRng;
        public DamageableDto[] damageables; public string[] appliedDamageEvents;
        public WorldSourceState worldSource; public SourceAccessState sourceAccess;
        public OptionalIntDto sourceManifestedTick; public OptionalIntDto sourceSealedTick; public OptionalUlongDto sourceSealSourceEventId;
        public OptionalIntDto sourceOverloadCommitTick; public OptionalUlongDto sourceOverloadCommitSequence;
        public OptionalIntDto sourceOverloadCompletionTick; public SourceOverloadCommitOrigin sourceOverloadCommitOrigin;
        public OptionalIntDto sourceDestroyedTick; public OptionalUlongDto sourceDestroySequence;
        public bool confirmedPartnerExecution; public OptionalIntDto confirmedPartnerExecutionTick;
        public OptionalUlongDto confirmedPartnerExecutionSequence; public TerminalStateDto terminal; public OutcomeDto outcome;
        public string committedFactAcknowledgedCursor; public CommittedFactDto[] committedFacts;
        public bool hasRunContext; public string rulesetHash; public string contentVersion;
        public string gameMode; public string difficulty; public OptionalIntDto night;
    }

    public enum RunStateLoadStatus { LoadedV2 = 0, MigratedV1 = 1, UnsupportedFutureSchema = 2, CorruptOrInvalid = 3 }

    public sealed class RunStateLoadResult
    {
        public RunStateLoadStatus Status { get; internal set; }
        public RunState State { get; internal set; }
        public string IsolationReason { get; internal set; }
        public bool IsLoaded => State != null;
    }
}
