using System;
using LastWatch.Core.Identity;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public enum PartnerRole { None = 0, CoverFace = 1, EmergencyBrace = 2, RallyExchange = 3 }
    public enum PartnerDecisionState { Idle = 0, Acknowledging = 1, Moving = 2, Working = 3, Recovering = 4 }
    public enum PartnerMovementState { Stationary = 0, Reserved = 1, Moving = 2, Recovering = 3 }
    public enum PartnerFireState { Idle = 0, Acquiring = 1, HoldingForPlayer = 2, Firing = 3, Reloading = 4 }
    public enum PartnerSignalKind { RequestAcknowledged = 0, ImmediateSupport = 1, CoverDirection = 2, FireHeld = 3, AmmoEmpty = 4, PathRecovered = 5 }
    public enum PartnerReactionStage { None = 0, FirstIncident = 1, RepeatedIncident = 2, DownedOrStabilizedHit = 3, Dead = 4 }
    public enum PartnerDeathCause { None = 0, Bleedout = 1, LostFaceFloorHazard = 2, CollapseImpact = 3, AfterfallTimeoutHazard = 4, PlayerFire = 5, OtherValidDamage = 6 }
    public enum ExchangeResource { ArAmmo = 0, Parts = 1 }
    public enum ExchangeDirection { PlayerToPartner = 0, PartnerToPlayer = 1 }

    public readonly struct PartnerRoleRequest
    {
        public PartnerRole Role { get; }
        public string FaceId { get; }
        public string StationNodeId { get; }
        public int RequestedTick { get; }
        public PartnerRoleRequest(PartnerRole role, string faceId, string stationNodeId, int requestedTick)
        { Role = role; FaceId = faceId; StationNodeId = stationNodeId; RequestedTick = requestedTick; }
    }

    public readonly struct PartnerTargetObservation
    {
        public EntityId EntityId { get; }
        public string FaceId { get; }
        public float PredictedStructureDamage { get; }
        public float Distance { get; }
        public Vector3 AimPoint { get; }
        public bool IsAttackWindup { get; }
        public PartnerTargetObservation(EntityId id, string faceId, float predictedDamage, float distance, Vector3 aimPoint, bool windup)
        { EntityId = id; FaceId = faceId; PredictedStructureDamage = predictedDamage; Distance = distance; AimPoint = aimPoint; IsAttackWindup = windup; }
    }

    public readonly struct PartnerObservation
    {
        public PartnerRole Role { get; }
        public PartnerDecisionState Decision { get; }
        public PartnerMovementState Movement { get; }
        public PartnerFireState Fire { get; }
        public PartnerLifeState Life { get; }
        public string DesignatedFaceId { get; }
        public string CurrentNodeId { get; }
        public string ReservedNodeId { get; }
        public int ArAmmo { get; }
        public int Parts { get; }
        public int LastAcknowledgementTick { get; }
        public PartnerSignalKind? LastSignal { get; }
        public int TechnicalDamageOrDeathCount { get; }
        public int FriendlyIncidentCount { get; }
        public bool ConfirmedPartnerExecution { get; }
        internal PartnerObservation(PartnerRuntimeState s)
        {
            Role=s.Role; Decision=s.Decision; Movement=s.Movement; Fire=s.Fire; Life=s.Life;
            DesignatedFaceId=s.DesignatedFaceId; CurrentNodeId=s.CurrentNodeId; ReservedNodeId=s.ReservedNodeId;
            ArAmmo=s.ArAmmo; Parts=s.Parts; LastAcknowledgementTick=s.LastAcknowledgementTick;
            LastSignal=s.LastSignal; TechnicalDamageOrDeathCount=s.TechnicalDamageOrDeathCount;
            FriendlyIncidentCount=s.FriendlyIncidents.Count; ConfirmedPartnerExecution=s.ConfirmedPartnerExecution;
        }
    }
}
