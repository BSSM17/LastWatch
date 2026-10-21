using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Detached value observations only: no Collider, Transform, actor, or session references.
    // This seam reports supported facts and gaps; it does not certify a geometry gate.
    public sealed class M4UpperLaneSnapshot
    {
        public string CaptureRunId { get; }
        public ulong QaRunValue { get; }
        public bool CompletedTickAvailable { get; }
        public int CompletedRunFixedTick { get; }
        public int CompletedNightFixedTick { get; }
        public int NightOrdinal { get; }
        public int ReplayConsumedTicks { get; }
        public string Timing => "LIVE_QUERY_WITH_LAST_COMPLETED_PROCESSOR_CONTEXT; CALL_AFTER_COMPLETE_ONE_FIXED_TICK_BEFORE_UPDATE_OR_NIGHT_TRANSITION; NOT_SAME_TICK_SHOT_POSE";
        public bool GeometryAvailable { get; }
        public FaceId? UnavailableFace { get; }
        public IReadOnlyList<StationDescriptor> Stations { get; }
        public IReadOnlyList<M4UpperLaneRouteSnapshot> Routes { get; }
        public IReadOnlyList<M4UpperLaneTargetSnapshot> Targets { get; }
        public IReadOnlyList<M4UpperLaneActorSnapshot> Actors { get; }
        public bool LiveCameraAvailable { get; }
        public Vector3 LiveCameraOrigin { get; }
        public Vector3 LiveCameraForward { get; }
        public float LiveCameraPitchDegrees { get; }
        public float InteractivePitchMinimumDegrees => -80f;
        public float InteractivePitchMaximumDegrees => 80f;
        public string SelectedWeapon { get; }
        public float SelectedWeaponRangeMeters { get; }
        public int ShotLayerMask => Physics.DefaultRaycastLayers;
        public QueryTriggerInteraction ShotTriggerQuery => QueryTriggerInteraction.UseGlobal;
        public bool GlobalQueriesHitTriggers { get; }
        public bool PartnerPlacementAvailable { get; }
        public Vector3 PartnerPosition { get; }
        public Quaternion PartnerRotation { get; }
        public IReadOnlyList<string> CoverageGaps { get; }

        internal M4UpperLaneSnapshot(string runId, ulong qaRun, bool tickAvailable, int runTick,
            int nightTick, int night, int consumed, TowerGeometryFixture geometry,
            M4UpperLaneRouteSnapshot[] routes, M4UpperLaneActorSnapshot[] actors,
            bool cameraAvailable, Vector3 cameraOrigin, Vector3 cameraForward, float pitch,
            string weapon, float range, bool queriesHitTriggers, bool partnerAvailable,
            Vector3 partnerPosition, Quaternion partnerRotation)
        {
            CaptureRunId = runId ?? string.Empty;
            QaRunValue = qaRun;
            CompletedTickAvailable = tickAvailable;
            CompletedRunFixedTick = runTick;
            CompletedNightFixedTick = nightTick;
            NightOrdinal = night;
            ReplayConsumedTicks = consumed;
            GeometryAvailable = geometry != null;
            UnavailableFace = geometry == null ? null : geometry.UnavailableFace;
            var stations = new List<StationDescriptor>();
            if (geometry != null) stations.AddRange(geometry.Stations);
            Stations = Array.AsReadOnly(stations.ToArray());
            Routes = Array.AsReadOnly((M4UpperLaneRouteSnapshot[])routes.Clone());
            var targets = new List<M4UpperLaneTargetSnapshot>();
            if (geometry != null)
                foreach (AttackCapsuleDescriptor target in geometry.AttackPoints)
                    targets.Add(new M4UpperLaneTargetSnapshot(target));
            Targets = Array.AsReadOnly(targets.ToArray());
            Actors = Array.AsReadOnly((M4UpperLaneActorSnapshot[])actors.Clone());
            LiveCameraAvailable = cameraAvailable;
            LiveCameraOrigin = cameraOrigin;
            LiveCameraForward = cameraForward;
            LiveCameraPitchDegrees = pitch;
            SelectedWeapon = weapon;
            SelectedWeaponRangeMeters = range;
            GlobalQueriesHitTriggers = queriesHitTriggers;
            PartnerPlacementAvailable = partnerAvailable;
            PartnerPosition = partnerPosition;
            PartnerRotation = partnerRotation;
            CoverageGaps = Array.AsReadOnly(new[] {
                "AUTHORED_ENEMY_HEAD_BODY_WEAKPOINT_SAMPLE_MAPPING_UNAVAILABLE",
                "ISOLATED_GUARD_RESOLVED_WHOLE_ROUTE_FIXTURE_UNAVAILABLE",
                "MAJOR_ATTACK_ANIMATION_POSES_AND_ATTACK_COMPLETION_UNAVAILABLE",
                "CONTINUOUS_BETWEEN_TICK_GEOMETRY_PROOF_UNAVAILABLE",
                "STRUCTURAL_STAGE_MESH_COMBINATIONS_UNAVAILABLE",
                "PERMITTED_PARTNER_STATION_COMBINATIONS_UNAVAILABLE",
                "COLLIDER_INVENTORY_BOUNDED_TO_REGISTERED_ACTOR_HIERARCHIES"
            });
        }
    }

    public sealed class M4UpperLaneTargetSnapshot
    {
        public AttackCapsuleDescriptor Descriptor { get; }
        public IReadOnlyList<M4UpperLaneAimSample> QuerySamples { get; }

        internal M4UpperLaneTargetSnapshot(AttackCapsuleDescriptor descriptor)
        {
            Descriptor = descriptor;
            Vector3[] points = descriptor.GetLineOfSightSamples();
            string[] labels = { "DescriptorTop", "DescriptorMiddle", "DescriptorBottom" };
            var samples = new M4UpperLaneAimSample[points.Length];
            for (int index = 0; index < points.Length; index++)
                samples[index] = new M4UpperLaneAimSample(labels[index], points[index], 0,
                    descriptor.IsAvailable, descriptor.IsAvailable ? string.Empty : "UNAVAILABLE_ASSIGNED_TARGET_DESCRIPTOR",
                    "AUTHORED_TARGET_QUERY_DESCRIPTOR_NOT_ENEMY_DAMAGE_COLLIDER");
            QuerySamples = Array.AsReadOnly(samples);
        }
    }

    public sealed class M4UpperLaneRouteSnapshot
    {
        public RouteId Id { get; }
        public FaceId Face { get; }
        public RouteKind Kind { get; }
        public bool IsAvailable { get; }
        public float SampleSpacingMeters => TowerGeometryFixture.LineOfSightSampleSpacing;
        public IReadOnlyList<Vector3> AuthoredSamples { get; }
        public string PoseMeaning => "STATIC_DESCRIPTOR_SAMPLES; ACTOR_OFFSETS_AND_GUARDS_NOT_APPLIED";

        internal M4UpperLaneRouteSnapshot(RouteDescriptor route)
        {
            Id = route.Id;
            Face = route.Face;
            Kind = route.Kind;
            IsAvailable = route.IsAvailable;
            AuthoredSamples = Array.AsReadOnly(route.Sample(SampleSpacingMeters));
        }
    }

    public sealed class M4UpperLaneActorSnapshot
    {
        public ulong EntityValue { get; }
        public ulong TicketId { get; }
        public EnemyArchetype Archetype { get; }
        public AttackPointId AssignedAttackPoint { get; }
        public bool Registered { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 LossyScale { get; }
        public bool ActiveAndEnabled { get; }
        public bool RouteAvailable { get; }
        public int NextWaypointIndex { get; }
        public int ClimbWaypointStartIndex { get; }
        public bool ClimbSegmentSelected { get; }
        public float AppliedGroundRouteLiftMeters { get; }
        public float AuthoredSampleCenterOffsetMeters => 0.5f;
        public IReadOnlyList<Vector3> AppliedAuthoredWaypoints { get; }
        public bool ClimberCenterAdaptationApplied { get; }
        public string ClimberCenterAdaptationReason => ClimberCenterAdaptationApplied
            ? "SAME_SUPPORT_START_PLANE_ACTOR_CENTER_APPLIED; SPATIAL_ACCEPTANCE_UNAVAILABLE"
            : Archetype == EnemyArchetype.Climber ? "SHARED_FACTORY_ADAPTATION_UNAVAILABLE" : "NOT_APPLICABLE_NON_CLIMBER";
        public string ClimberCenterAdaptationDomain => "ACTUAL_SHARED_FACTORY_SAME_SUPPORT_LOCAL_X_START_PLANE; NOT_GUARD_RESOLVED_ROUTE_OR_GEOMETRY_PASS";
        public int? ClimberCenterSupportInstanceId { get; }
        public Vector3? ClimberCenterSupportNormal { get; }
        public float? ClimberCenterStartPlaneDistanceMeters { get; }
        public string ClimberCenterShapeCoverage { get; }
        public M4ClimberStructureAttackSnapshot LatestClimberStructureAttack { get; }
        public bool HasReachedStandOff { get; }
        public bool SurfaceGapAvailable { get; }
        public float SurfaceGapMeters { get; }
        public IReadOnlyList<StationId> CanonicalStations { get; }
        public IReadOnlyList<RouteId> AssignedRoutes { get; }
        public IReadOnlyList<M4UpperLaneColliderSnapshot> Colliders { get; }
        public IReadOnlyList<M4UpperLaneAimSample> AimSamples { get; }
        public M4MovementAttemptSnapshot LatestMovementAttempt { get; }
        public string RouteMeaning => "APPLIED_AUTHORED_WAYPOINTS_WITH_ACTOR_CENTER_ADAPTATION_WHEN_REPORTED; CURRENT_POSE_IS_GUARD_RESOLVED; WHOLE_ROUTE_RESOLVED_POSES_UNAVAILABLE";

        internal M4UpperLaneActorSnapshot(ActiveEnemyActor actor, bool registered,
            Vector3[] waypoints, int nextWaypoint, int climbStart, float lift,
            RouteId[] routeIds, M4UpperLaneColliderSnapshot[] colliders, M4UpperLaneAimSample[] samples)
        {
            EntityValue = actor.EntityId.Value;
            TicketId = actor.TicketId;
            Archetype = actor.Archetype;
            AssignedAttackPoint = actor.TargetAttackPoint;
            Registered = registered;
            Position = actor.transform.position;
            Rotation = actor.transform.rotation;
            LossyScale = actor.transform.lossyScale;
            ActiveAndEnabled = actor.isActiveAndEnabled;
            RouteAvailable = waypoints != null;
            NextWaypointIndex = nextWaypoint;
            ClimbWaypointStartIndex = climbStart;
            ClimbSegmentSelected = actor.Archetype == EnemyArchetype.Climber && nextWaypoint >= climbStart;
            AppliedGroundRouteLiftMeters = lift;
            AppliedAuthoredWaypoints = Array.AsReadOnly(waypoints == null ? Array.Empty<Vector3>() : (Vector3[])waypoints.Clone());
            ClimberCenterAdaptationApplied = actor.ClimberCenterAdaptationApplied;
            ClimberCenterSupportInstanceId = actor.ClimberCenterSupportInstanceId;
            ClimberCenterSupportNormal = actor.ClimberCenterSupportNormal;
            ClimberCenterStartPlaneDistanceMeters = actor.ClimberCenterStartPlaneDistance;
            ClimberCenterShapeCoverage = actor.ClimberCenterShapeCoverage;
            LatestClimberStructureAttack = actor.LatestClimberStructureAttack;
            HasReachedStandOff = actor.HasReachedAttackCapsuleStandOff;
            SurfaceGapAvailable = actor.HasMeasuredAttackCapsuleSurfaceGap;
            SurfaceGapMeters = actor.AttackCapsuleSurfaceGapMeters;
            CanonicalStations = Array.AsReadOnly(TowerLineOfSightContract.GetCanonicalStations(actor.TargetAttackPoint));
            AssignedRoutes = Array.AsReadOnly((RouteId[])routeIds.Clone());
            Colliders = Array.AsReadOnly((M4UpperLaneColliderSnapshot[])colliders.Clone());
            AimSamples = Array.AsReadOnly((M4UpperLaneAimSample[])samples.Clone());
            LatestMovementAttempt = actor.LatestMovementAttempt;
        }
    }

    public readonly struct M4UpperLaneColliderSnapshot
    {
        public int InstanceId { get; }
        public string Name { get; }
        public string ColliderType { get; }
        public bool Enabled { get; }
        public bool ActiveInHierarchy { get; }
        public bool IsTrigger { get; }
        public bool IsDirectActorCollider { get; }
        public bool AcceptedHitRole { get; }
        public bool NonTriggerPhysicalCollider { get; }
        public string Role { get; }
        public Bounds ObservedBounds { get; }
        public string BoundsTiming => "CURRENT_UNITY_BOUNDS; NO_PHYSICS_SYNC_PERFORMED_BY_SNAPSHOT";

        internal M4UpperLaneColliderSnapshot(Collider collider, ActiveEnemyActor actor, bool registered,
            bool assignedTargetQuery = false)
        {
            InstanceId = collider.GetInstanceID();
            Name = collider.name;
            ColliderType = collider.GetType().Name;
            Enabled = collider.enabled;
            ActiveInHierarchy = collider.gameObject.activeInHierarchy;
            IsTrigger = collider.isTrigger;
            IsDirectActorCollider = collider.GetComponent<ActiveEnemyActor>() == actor;
            AcceptedHitRole = registered && M4EnemyWeakPointCollider.ResolveHitActor(collider) == actor;
            NonTriggerPhysicalCollider = !assignedTargetQuery && !IsTrigger;
            Role = assignedTargetQuery ? "CORE_ASSIGNED_ATTACK_TARGET_QUERY" :
                AcceptedHitRole ? (IsDirectActorCollider ? "CORE_REGISTERED_DIRECT_ACTOR_HIT_COLLIDER" :
                    "CORE_REGISTERED_WEAK_POINT_HIT_COLLIDER") : "ACTOR_HIERARCHY_COLLIDER_NOT_ACCEPTED_AS_THIS_ACTOR_HIT";
            ObservedBounds = collider.bounds;
        }
    }

    public readonly struct M4UpperLaneAimSample
    {
        public string Semantic { get; }
        public Vector3 Position { get; }
        public int ColliderInstanceId { get; }
        public bool Valid { get; }
        public string UnsupportedReason { get; }
        public string Domain { get; }

        internal M4UpperLaneAimSample(string semantic, Vector3 position, int colliderId,
            bool valid, string reason, string domain)
        {
            Semantic = semantic;
            Position = position;
            ColliderInstanceId = colliderId;
            Valid = valid;
            UnsupportedReason = reason ?? string.Empty;
            Domain = domain;
        }
    }
}
