using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Detached source inventory, not an actor pose, tick observation or geometry PASS.
    public sealed class M4AuthoredRouteAssignmentCatalogSnapshot
    {
        public string Domain => "CURRENT_GRAYBOX_SOURCE_AUTHORED_ASSIGNMENTS";
        public bool GeometryAvailable { get; }
        public string UnavailableReason { get; }
        public FaceId? UnavailableFace { get; }
        public string ControllerWorldSupport => "CURRENT_RUNTIME_INITIALIZATION_NORMAL_ONLY";
        public IReadOnlyList<M4UpperLaneRouteSnapshot> SourceRoutes { get; }
        public IReadOnlyList<M4AuthoredRouteAssignmentSnapshot> Assignments { get; }
        public bool GuardResolvedPosesAvailable => false;
        public bool SemanticSamplesAvailable => false;
        public string CoverageLimitations => "NO_ACTOR_COLLIDER_CENTER_OR_RUNNER_EVADE; NO_GUARD_STANDOFF_PHASE_OR_MAJOR_POSE; NO_SEMANTIC_SAMPLE_OR_WORLD_PARTNER_COVERAGE";

        internal M4AuthoredRouteAssignmentCatalogSnapshot(bool available, string reason, FaceId? unavailableFace,
            M4UpperLaneRouteSnapshot[] routes, M4AuthoredRouteAssignmentSnapshot[] assignments)
        {
            GeometryAvailable = available;
            UnavailableReason = reason;
            UnavailableFace = unavailableFace;
            SourceRoutes = Array.AsReadOnly((M4UpperLaneRouteSnapshot[])routes.Clone());
            Assignments = Array.AsReadOnly((M4AuthoredRouteAssignmentSnapshot[])assignments.Clone());
        }
    }

    public sealed class M4AuthoredRouteAssignmentSnapshot
    {
        public EnemyArchetype Archetype { get; }
        public FaceId Face { get; }
        public AttackPointId AssignedAttackPoint { get; }
        public bool Available { get; }
        public string UnavailableReason { get; }
        public IReadOnlyList<M4UpperLaneRouteSnapshot> SourceRoutes { get; }
        public IReadOnlyList<Vector3> AuthoredBaseWaypoints { get; }
        public int? ClimbWaypointStartIndex { get; }
        public float SampleSpacingMeters => TowerGeometryFixture.LineOfSightSampleSpacing;
        public float AuthoredBaseVerticalOffsetMeters => 0.5f;
        public string WaypointDomain => "CORE_BUILDER_BASE_WAYPOINTS_BEFORE_ACTOR_SPECIFIC_LIFT_OR_GUARDS";
        public bool ColliderDependentGroundLiftAvailable => false;
        public bool ColliderDependentGroundLiftApplied => false;
        public string GroundLiftReason => Archetype == EnemyArchetype.Breaker
            ? "BREAKER_ACTUAL_GROUND_ROUTE_LIFT_REQUIRES_INSTANTIATED_COLLIDER"
            : "BASE_CATALOG_DOES_NOT_OBSERVE_ACTOR_COLLIDER_LIFT";
        public bool GuardResolvedPosesAvailable => false;
        public bool SemanticSamplesAvailable => false;

        internal M4AuthoredRouteAssignmentSnapshot(EnemyArchetype archetype, FaceId face,
            AttackPointId target, bool available, string reason, M4UpperLaneRouteSnapshot[] routes,
            Vector3[] waypoints, int? climbStart)
        {
            Archetype = archetype;
            Face = face;
            AssignedAttackPoint = target;
            Available = available;
            UnavailableReason = reason;
            SourceRoutes = Array.AsReadOnly((M4UpperLaneRouteSnapshot[])routes.Clone());
            AuthoredBaseWaypoints = Array.AsReadOnly((Vector3[])waypoints.Clone());
            ClimbWaypointStartIndex = climbStart;
        }
    }
}
