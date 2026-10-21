using System;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    internal interface IM4PartnerExchangeCompletionSink
    {
        void ProcessExchangeCompletionBoundary(int completedRunTick);
    }
    public readonly struct M4PartnerCoverEquipmentObservation
    {
        public readonly bool CoreArContractKnown;
        public readonly bool HostFireReloadPipelineAvailable;
        public readonly string Reason;
        public M4PartnerCoverEquipmentObservation(bool coreArContractKnown, bool hostFireReloadPipelineAvailable, string reason)
        { CoreArContractKnown = coreArContractKnown; HostFireReloadPipelineAvailable = hostFireReloadPipelineAvailable; Reason = reason; }
    }
    // Explicit scene input only; no distance, timing or LOS default policy.
    [Serializable] public sealed class M4PartnerExchangePolicy
    {
        public string policyId;
        public float startDistanceMetres, maintainDistanceMetres;
        public float occlusionGraceSeconds;
        public string distanceMetric, lineOfSightMetric;
        public float holdDelaySeconds;
        public bool requireClearAtCompletion;
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(policyId) || !M4PartnerMovementPersistence.Finite(startDistanceMetres) ||
                !M4PartnerMovementPersistence.Finite(maintainDistanceMetres) || startDistanceMetres <= 0f || maintainDistanceMetres <= startDistanceMetres ||
                !M4PartnerMovementPersistence.Finite(occlusionGraceSeconds) || occlusionGraceSeconds < 0f ||
                holdDelaySeconds != 0f || !requireClearAtCompletion || distanceMetric != "PlayerOriginXZToPartnerCapsuleCenterXZ" ||
                lineOfSightMetric != "ActualCameraToPartnerCapsuleCenter")
                throw new FormatException("Unsupported or incomplete explicit Partner exchange policy.");
        }
    }

    // Persists input identity and the interrupted intent, without inventing an actor restore.
    [Serializable] public sealed class M4PartnerExchangeFlow
    {
        public ulong intentRevision;
        public ulong transactionIntentRevision;
        public ulong lastSelectionEpisode;
        public ulong nextSelectionEpisode = 1;
        public PartnerRole priorRole;
        public string priorFaceId, priorReservedNodeId;
        public PartnerDecisionState priorDecision;
        public PartnerMovementState priorMovement;
        public M4PartnerMovementProgress priorMovementProgress;
        public bool suspended;
        public int suspendedTick;
        public int occludedSinceTick = -1;
        public string policyIdentity;
        public string returnReason;
    }

    public static class M4PartnerExchangePersistence
    {
        public static bool Active(PartnerRuntimeState state) => state.PendingExchange != null &&
            !state.PendingExchange.Completed && !state.PendingExchange.Cancelled;
        public static void Validate(PartnerRuntimeState state)
        {
            var f = state.ExchangeFlow;
            if (f == null) return; // Explicitly migrated pre-exchange contract.
            if (f.nextSelectionEpisode == 0 || f.lastSelectionEpisode >= f.nextSelectionEpisode ||
                f.transactionIntentRevision > f.intentRevision || f.occludedSinceTick < -1 || f.suspendedTick < 0 ||
                !Enum.IsDefined(typeof(PartnerRole), f.priorRole) || !Enum.IsDefined(typeof(PartnerDecisionState), f.priorDecision) ||
                !Enum.IsDefined(typeof(PartnerMovementState), f.priorMovement)) throw new FormatException("Invalid exchange intent receipt.");
            if (f.suspended && (state.PendingExchange == null || (Active(state) && state.Role != PartnerRole.RallyExchange) ||
                state.Movement != PartnerMovementState.Stationary || state.ReservedNodeId != null ||
                f.transactionIntentRevision != f.intentRevision || string.IsNullOrWhiteSpace(f.policyIdentity)))
                throw new FormatException("Exchange suspension/role/intent mismatch.");
            if (f.suspended && f.priorMovementProgress != null)
            {
                M4PartnerMovementPersistence.Validate(new PartnerRuntimeState {
                    MovementProgress = f.priorMovementProgress, NextMovementPlanId = state.NextMovementPlanId,
                    CurrentNodeId = state.CurrentNodeId, ReservedNodeId = f.priorReservedNodeId, Movement = f.priorMovement });
                if (state.MovementProgress == null || f.priorMovementProgress.position != state.MovementProgress.position ||
                    f.priorMovementProgress.planId != state.MovementProgress.planId)
                    throw new FormatException("Interrupted route/actual held route mismatch.");
            }
        }
        public static M4PartnerExchangeFlow Ensure(PartnerRuntimeState state)
            => state.ExchangeFlow ?? (state.ExchangeFlow = new M4PartnerExchangeFlow());
    }
}
