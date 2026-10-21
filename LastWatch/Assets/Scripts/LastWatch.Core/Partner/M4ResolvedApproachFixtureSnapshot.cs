using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public sealed class M4NormalApproachAssignmentSnapshot
    {
        public EnemyArchetype Archetype { get; }
        public FaceId Face { get; }
        public AttackPointId AssignedAttackPoint { get; }
        public bool SourceMappingAvailable { get; }
        public string SourceUnavailableReason { get; }

        internal M4NormalApproachAssignmentSnapshot(EnemyArchetype archetype, FaceId face,
            AttackPointId target, bool available, string reason)
        {
            Archetype = archetype;
            Face = face;
            AssignedAttackPoint = target;
            SourceMappingAvailable = available;
            SourceUnavailableReason = reason ?? string.Empty;
        }
    }

    public sealed class M4NormalApproachAssignmentInventorySnapshot
    {
        public string Domain => "NORMAL_AUTHORED_SOURCE_SUPPORTED_TUPLES_ONLY";
        public string ObservedSourceFingerprint { get; }
        public string FingerprintProvenance => "CALLER_SUPPLIED_SOURCE_EVIDENCE_NOT_RUNTIME_SELF_VERIFICATION";
        public bool SourceDescriptorsAvailable { get; }
        public string SourceUnavailableReason { get; }
        public bool RuntimeGeometryAvailable => false;
        public bool WorldBuilt => false;
        public string RuntimeUnavailableReason => "DETACHED_SOURCE_DESCRIPTORS_ONLY_NO_INSTANTIATED_WORLD_OR_COLLIDERS";
        public bool SampleCoverageAvailable => false;
        public bool GuardResolvedPosesAvailable => false;
        public bool MajorAttackPhaseAvailable => false;
        public bool AttackOutcomeAvailable => false;
        public bool SemanticSamplesAvailable => false;
        public IReadOnlyList<M4NormalApproachAssignmentSnapshot> Assignments { get; }

        internal M4NormalApproachAssignmentInventorySnapshot(string fingerprint, bool descriptorsAvailable,
            string reason, M4NormalApproachAssignmentSnapshot[] assignments)
        {
            ObservedSourceFingerprint = fingerprint ?? string.Empty;
            SourceDescriptorsAvailable = descriptorsAvailable;
            SourceUnavailableReason = reason ?? string.Empty;
            Assignments = Array.AsReadOnly((M4NormalApproachAssignmentSnapshot[])assignments.Clone());
        }
    }

    // Values are caller evidence/setup, not additional gameplay rules or a runtime source hash.
    public sealed class M4ResolvedApproachFixtureRequest
    {
        public EnemyArchetype Archetype { get; }
        public FaceId Face { get; }
        public AttackPointId AssignedAttackPoint { get; }
        public string ExpectedSourceFingerprint { get; }
        public int DiagnosticIterationBudget { get; }

        public M4ResolvedApproachFixtureRequest(EnemyArchetype archetype, FaceId face,
            AttackPointId target, string expectedSourceFingerprint, int diagnosticIterationBudget)
        {
            Archetype = archetype;
            Face = face;
            AssignedAttackPoint = target;
            ExpectedSourceFingerprint = expectedSourceFingerprint ?? string.Empty;
            DiagnosticIterationBudget = diagnosticIterationBudget;
        }
    }

    public sealed class M4ResolvedApproachFixtureColliderSnapshot
    {
        public int InstanceId { get; }
        public string FixtureOwnerId { get; }
        public string Role { get; }
        public string ColliderType { get; }
        public bool Enabled { get; }
        public bool ActiveInHierarchy { get; }
        public bool IsTrigger { get; }
        public int Layer { get; }
        public float ContactOffset { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 LossyScale { get; }
        public Vector3? ShapeCenter { get; }
        public float? CapsuleRadius { get; }
        public float? CapsuleHeight { get; }
        public int? CapsuleDirection { get; }
        public Vector3? BoxSize { get; }
        public string RegistryDomain => "FIXTURE_OWNED_UNREGISTERED_IN_RUNTIME";
        public bool RuntimeRegistered => false;

        internal M4ResolvedApproachFixtureColliderSnapshot(Collider value, string ownerId, string role)
        {
            InstanceId = value.GetInstanceID();
            FixtureOwnerId = ownerId;
            Role = role;
            ColliderType = value.GetType().Name;
            Enabled = value.enabled;
            ActiveInHierarchy = value.gameObject.activeInHierarchy;
            IsTrigger = value.isTrigger;
            Layer = value.gameObject.layer;
            ContactOffset = value.contactOffset;
            Position = value.transform.position;
            Rotation = value.transform.rotation;
            LossyScale = value.transform.lossyScale;
            if (value is CapsuleCollider capsule)
            {
                ShapeCenter = capsule.center;
                CapsuleRadius = capsule.radius;
                CapsuleHeight = capsule.height;
                CapsuleDirection = capsule.direction;
            }
            else if (value is BoxCollider box)
            {
                ShapeCenter = box.center;
                BoxSize = box.size;
            }
        }
    }

    public sealed class M4ResolvedApproachFixtureSnapshot
    {
        public string FixtureSessionId { get; }
        public ulong ActorGeneration { get; }
        public string Domain => "ISOLATED_NORMAL_GRAYBOX_TICK_APPROACH";
        public string WorldKind => "NORMAL_ONLY_EXISTING_M2_BUILDER";
        public string PhysicsIsolation => "OWNED_LOCAL_PHYSICS3D_SCENE_NO_SIMULATE_SYNC_OR_GLOBAL_QUERY";
        public string RegistryDomain => "FIXTURE_OWNED_UNREGISTERED_IN_RUNTIME";
        public string FingerprintProvenance => "CALLER_SUPPLIED_SOURCE_EVIDENCE_NOT_RUNTIME_SELF_VERIFICATION";
        public EnemyArchetype Archetype { get; }
        public FaceId Face { get; }
        public AttackPointId AssignedAttackPoint { get; }
        public string ExpectedSourceFingerprint { get; }
        public string ObservedSourceFingerprint { get; }
        public int FixtureIteration { get; }
        public int DiagnosticIterationBudget { get; }
        public bool Available { get; }
        public string Reason { get; }
        public bool Stopped { get; }
        public bool Released { get; }
        public bool AwaitingDestroyCompletion { get; }
        public bool CanonicalContextAvailable => false;
        public int? CompletedRunFixedTick => null;
        public int? CompletedNightFixedTick => null;
        public int? NightOrdinal => null;
        public bool MajorAttackPhaseAvailable => false;
        public bool AttackOutcomeAvailable => false;
        public bool SemanticSamplesAvailable => false;
        public bool WholeAuthoredSampleCoverageAvailable => false;
        public string PhaseMeaning => "EXISTING_MOVEMENT_OUTCOME_ONLY_NOT_ATTACK_PHASE";
        public string ObservationTiming => "CACHED_DETACHED_LAST_SETUP_OR_ADVANCE_ROW; RELEASED_ROWS_ARE_HISTORICAL";
        public string IndexMeaning => "APPLIED_WAYPOINT_INDEX_NOT_DESCRIPTOR_SAMPLE_ID";
        public M4MovementAttemptSnapshot Attempt { get; }
        public M4UpperLaneActorSnapshot Actor { get; }
        public IReadOnlyList<M4UpperLaneRouteSnapshot> SourceRoutes { get; }
        public IReadOnlyList<Vector3> AuthoredBaseWaypoints { get; }
        public IReadOnlyList<M4ResolvedApproachFixtureColliderSnapshot> Colliders { get; }
        public string ExceptionType { get; }

        internal M4ResolvedApproachFixtureSnapshot(string session, ulong generation,
            M4ResolvedApproachFixtureRequest request, string fingerprint, int iteration, bool available,
            string reason, bool stopped, bool released, bool awaitingDestroy, M4MovementAttemptSnapshot attempt,
            M4UpperLaneActorSnapshot actor, M4UpperLaneRouteSnapshot[] routes, Vector3[] baseWaypoints,
            M4ResolvedApproachFixtureColliderSnapshot[] colliders, string exceptionType)
        {
            FixtureSessionId = session;
            ActorGeneration = generation;
            Archetype = request.Archetype;
            Face = request.Face;
            AssignedAttackPoint = request.AssignedAttackPoint;
            ExpectedSourceFingerprint = request.ExpectedSourceFingerprint;
            ObservedSourceFingerprint = fingerprint ?? string.Empty;
            FixtureIteration = iteration;
            DiagnosticIterationBudget = request.DiagnosticIterationBudget;
            Available = available;
            Reason = reason;
            Stopped = stopped;
            Released = released;
            AwaitingDestroyCompletion = awaitingDestroy;
            Attempt = attempt;
            Actor = actor;
            SourceRoutes = Array.AsReadOnly((M4UpperLaneRouteSnapshot[])routes.Clone());
            AuthoredBaseWaypoints = Array.AsReadOnly((Vector3[])baseWaypoints.Clone());
            Colliders = Array.AsReadOnly((M4ResolvedApproachFixtureColliderSnapshot[])colliders.Clone());
            ExceptionType = exceptionType ?? string.Empty;
        }
    }
}
