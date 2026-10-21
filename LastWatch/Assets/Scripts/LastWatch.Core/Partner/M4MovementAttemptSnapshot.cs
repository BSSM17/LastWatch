using LastWatch.Core.Combat;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Detached observation of one actual mover invocation; no fixture or geometry verdict.
    public sealed class M4MovementAttemptSnapshot
    {
        public string Domain => "ACTUAL_ACTOR_MOVEMENT_INVOCATION_NOT_ISOLATED_ROUTE_FIXTURE";
        public string Timing => "ATTEMPTED_PROCESSOR_TICK_NOT_CANONICAL_COMPLETION_OR_SHOT_POSE";
        public bool InvocationAvailable { get; }
        public string Outcome { get; }
        public ulong Sequence { get; }
        public ulong EntityValue { get; }
        public ulong TicketId { get; }
        public EnemyArchetype Archetype { get; }
        public AttackPointId AssignedAttackPoint { get; }
        public bool CanonicalContextAvailable { get; }
        public string ContextUnavailableReason => CanonicalContextAvailable ? string.Empty : "NO_CANONICAL_CONTROLLER_CONTEXT_FOR_INVOCATION";
        public int? AttemptedRunFixedTick { get; }
        public int? AttemptedNightFixedTick { get; }
        public int? NightOrdinal { get; }
        public int WaypointIndexBefore { get; }
        public int WaypointIndexAfter { get; }
        public bool MovementProposed { get; }
        public bool ClimbSegmentSelected { get; }
        public Vector3? Destination { get; }
        public Vector3 PriorPosition { get; }
        public Quaternion PriorRotation { get; }
        public Vector3? ProposedPosition { get; }
        public Quaternion? ProposedRotation { get; }
        public bool SupportGuardInvoked { get; }
        public bool? SupportResolved { get; }
        public bool SupportTraceRequested { get; }
        public bool SupportTraceAvailable { get; }
        public string SupportTraceUnavailableReason { get; }
        public M2SupportResolutionTraceSnapshot SupportTrace { get; }
        public Vector3? SupportResolvedPosition { get; }
        public bool TargetGuardInvoked { get; }
        public bool? AssignedTargetEnabled { get; }
        public M2AttackCapsulePoseResult? TargetClassification { get; }
        public float? TargetPenetrationDepthMeters { get; }
        public bool ProposedGapMeasurementInvoked { get; }
        public bool? ProposedGapMeasurementSucceeded { get; }
        public float? MeasuredProposedPoseGapMeters { get; }
        public string ProposedGapMeasurementUnavailableReason { get; }
        public string ProposedGapPoseMeaning => "POST_SUPPORT_RESOLUTION_PROPOSED_POSITION_AND_ROTATION_BEFORE_TRANSFORM_COMMIT";
        public bool GapMeasurementInvoked { get; }
        public bool? GapMeasurementSucceeded { get; }
        public float? MeasuredPriorPoseGapMeters { get; }
        public bool TransformApplied { get; }
        public Vector3 FinalPosition { get; }
        public Quaternion FinalRotation { get; }
        public int? MoverColliderInstanceId { get; }
        public int? AssignedTargetColliderInstanceId { get; }
        public string MoverRole => "CURRENT_SAME_OBJECT_COLLIDER; HIT_ACCEPTANCE_REQUIRES_SEPARATE_REGISTRY_OBSERVATION";
        public string TargetRole => "ASSIGNED_AP_QUERY_NOT_ENEMY_DAMAGE_COLLIDER";
        public string GuardMeaning => "INVOKED_IS_METHOD_CALL_NOT_PHYSICS_QUERY_COUNT_OR_GEOMETRY_PASS";

        internal M4MovementAttemptSnapshot(M4MovementAttemptRecord value)
        {
            InvocationAvailable = value.InvocationAvailable;
            Outcome = value.Outcome;
            Sequence = value.Sequence;
            EntityValue = value.EntityValue;
            TicketId = value.TicketId;
            Archetype = value.Archetype;
            AssignedAttackPoint = value.AssignedAttackPoint;
            CanonicalContextAvailable = value.CanonicalContextAvailable;
            AttemptedRunFixedTick = value.AttemptedRunFixedTick;
            AttemptedNightFixedTick = value.AttemptedNightFixedTick;
            NightOrdinal = value.NightOrdinal;
            WaypointIndexBefore = value.WaypointIndexBefore;
            WaypointIndexAfter = value.WaypointIndexAfter;
            MovementProposed = value.MovementProposed;
            ClimbSegmentSelected = value.ClimbSegmentSelected;
            Destination = value.Destination;
            PriorPosition = value.PriorPosition;
            PriorRotation = value.PriorRotation;
            ProposedPosition = value.ProposedPosition;
            ProposedRotation = value.ProposedRotation;
            SupportGuardInvoked = value.SupportGuardInvoked;
            SupportResolved = value.SupportResolved;
            SupportTraceRequested = value.SupportTraceRequested;
            SupportTrace = value.SupportTrace;
            SupportTraceAvailable = SupportTrace != null;
            SupportTraceUnavailableReason = value.SupportTraceUnavailableReason;
            SupportResolvedPosition = value.SupportResolvedPosition;
            TargetGuardInvoked = value.TargetGuardInvoked;
            AssignedTargetEnabled = value.AssignedTargetEnabled;
            TargetClassification = value.TargetClassification;
            TargetPenetrationDepthMeters = value.TargetPenetrationDepthMeters;
            ProposedGapMeasurementInvoked = value.ProposedGapMeasurementInvoked;
            ProposedGapMeasurementSucceeded = value.ProposedGapMeasurementSucceeded;
            MeasuredProposedPoseGapMeters = value.MeasuredProposedPoseGapMeters;
            ProposedGapMeasurementUnavailableReason = value.ProposedGapMeasurementUnavailableReason;
            GapMeasurementInvoked = value.GapMeasurementInvoked;
            GapMeasurementSucceeded = value.GapMeasurementSucceeded;
            MeasuredPriorPoseGapMeters = value.MeasuredPriorPoseGapMeters;
            TransformApplied = value.TransformApplied;
            FinalPosition = value.FinalPosition;
            FinalRotation = value.FinalRotation;
            MoverColliderInstanceId = value.MoverColliderInstanceId;
            AssignedTargetColliderInstanceId = value.AssignedTargetColliderInstanceId;
        }
    }

    // Private-to-Core per-call scratch data. Never returned to a consumer.
    internal sealed class M4MovementAttemptRecord
    {
        internal bool InvocationAvailable;
        internal string Outcome = "NO_INVOCATION_SINCE_INITIALIZE";
        internal ulong Sequence, EntityValue, TicketId;
        internal EnemyArchetype Archetype;
        internal AttackPointId AssignedAttackPoint;
        internal bool CanonicalContextAvailable;
        internal int? AttemptedRunFixedTick, AttemptedNightFixedTick, NightOrdinal;
        internal int WaypointIndexBefore, WaypointIndexAfter;
        internal bool MovementProposed, ClimbSegmentSelected;
        internal Vector3? Destination, ProposedPosition, SupportResolvedPosition;
        internal Quaternion? ProposedRotation;
        internal Vector3 PriorPosition, FinalPosition;
        internal Quaternion PriorRotation, FinalRotation;
        internal bool SupportGuardInvoked, TargetGuardInvoked, GapMeasurementInvoked, TransformApplied;
        internal bool? SupportResolved, AssignedTargetEnabled, GapMeasurementSucceeded;
        internal bool SupportTraceRequested;
        internal M2SupportResolutionTraceSnapshot SupportTrace;
        internal string SupportTraceUnavailableReason = "SUPPORT_TRACE_NOT_REQUESTED";
        internal M2AttackCapsulePoseResult? TargetClassification;
        internal float? TargetPenetrationDepthMeters, MeasuredPriorPoseGapMeters;
        internal bool ProposedGapMeasurementInvoked;
        internal bool? ProposedGapMeasurementSucceeded;
        internal float? MeasuredProposedPoseGapMeters;
        internal string ProposedGapMeasurementUnavailableReason = "PROPOSED_GAP_NOT_EVALUATED";
        internal int? MoverColliderInstanceId, AssignedTargetColliderInstanceId;
    }
}
