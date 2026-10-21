using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Safety
{
    public enum EnemyTargetAssignmentStatus
    {
        Assigned,
        RejectedInvalidActor,
        RejectedUpperSafeZone
    }

    public readonly struct ActorSpatialSnapshot
    {
        public EntityId EntityId { get; }
        public DamageableKind Kind { get; }
        public LocationZone Zone { get; }
        public Vector3 Position { get; }

        public ActorSpatialSnapshot(EntityId entityId, DamageableKind kind, LocationZone zone, Vector3 position)
        {
            if (!entityId.IsValid) throw new ArgumentException("Actor EntityId is required.", nameof(entityId));
            if (kind != DamageableKind.Player && kind != DamageableKind.Partner)
                throw new ArgumentOutOfRangeException(nameof(kind), "Only Player and Partner are target actors.");
            EntityId = entityId;
            Kind = kind;
            Zone = zone;
            Position = position;
        }
    }

    public static class UpperSafeZoneSafety
    {
        public static EnemyTargetAssignmentStatus EvaluateEnemyTargetAssignment(in ActorSpatialSnapshot actor)
        {
            if (!actor.EntityId.IsValid ||
                (actor.Kind != DamageableKind.Player && actor.Kind != DamageableKind.Partner))
                return EnemyTargetAssignmentStatus.RejectedInvalidActor;
            return actor.Zone == LocationZone.UpperSafeZone
                ? EnemyTargetAssignmentStatus.RejectedUpperSafeZone
                : EnemyTargetAssignmentStatus.Assigned;
        }

        public static bool CanReceiveEnemyAttack(in ActorSpatialSnapshot actor)
        {
            return EvaluateEnemyTargetAssignment(actor) == EnemyTargetAssignmentStatus.Assigned;
        }
    }
}
