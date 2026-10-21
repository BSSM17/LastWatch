using System;
using LastWatch.Core.Identity;
using UnityEngine;

namespace LastWatch.Core.Combat
{
    public enum DamageKind { Ballistic, Melee, Environment, CollapseImpact, AfterfallTimeoutHazard }
    public enum DamageableKind { Player, Partner, Enemy, Structure }
    public enum DamageApplyStatus
    {
        Applied,
        Duplicate,
        InvalidPacket,
        InvalidTarget,
        TargetAlreadyResolved,
        RejectedUpperSafeZone,
        InvalidTargetIncarnation
    }

    public readonly struct DamagePacket
    {
        public ulong EventSequence { get; }
        public int FixedTick { get; }
        public EntityId Source { get; }
        public EntityId Instigator { get; }
        public EntityId Target { get; }
        public DamageKind Kind { get; }
        public float Amount { get; }
        public Vector3 HitPoint { get; }
        public Vector3 ShotOrigin { get; }
        public FireSequenceId FireSequence { get; }
        // Pool EntityIds are recycled. This is required only for a Core-bound active
        // enemy incarnation and prevents a late packet from damaging the next ticket
        // occupying the same visual/pool slot.
        public ulong? TargetTicketId { get; }

        public DamagePacket(ulong eventSequence, int fixedTick, EntityId source, EntityId instigator,
            EntityId target, DamageKind kind, float amount, Vector3 hitPoint, Vector3 shotOrigin,
            FireSequenceId fireSequence)
        {
            EventSequence = eventSequence;
            FixedTick = fixedTick;
            Source = source;
            Instigator = instigator;
            Target = target;
            Kind = kind;
            Amount = amount;
            HitPoint = hitPoint;
            ShotOrigin = shotOrigin;
            FireSequence = fireSequence;
            TargetTicketId = null;
        }

        public DamagePacket(ulong eventSequence, int fixedTick, EntityId source, EntityId instigator,
            EntityId target, DamageKind kind, float amount, Vector3 hitPoint, Vector3 shotOrigin,
            FireSequenceId fireSequence, ulong targetTicketId)
            : this(eventSequence, fixedTick, source, instigator, target, kind, amount, hitPoint, shotOrigin, fireSequence)
        {
            if (targetTicketId == 0) throw new ArgumentOutOfRangeException(nameof(targetTicketId));
            TargetTicketId = targetTicketId;
        }
    }

    public interface IDamageable
    {
        EntityId EntityId { get; }
        DamageableKind Kind { get; }
        float HitPoints { get; }
        bool IsResolved { get; }
    }

    public readonly struct DamageReceipt
    {
        public DamageApplyStatus Status { get; }
        public ulong EventSequence { get; }
        public EntityId Target { get; }
        public float HitPointsBefore { get; }
        public float HitPointsAfter { get; }
        public bool BecameResolved { get; }

        public DamageReceipt(DamageApplyStatus status, ulong eventSequence, EntityId target,
            float hitPointsBefore, float hitPointsAfter, bool becameResolved)
        {
            Status = status;
            EventSequence = eventSequence;
            Target = target;
            HitPointsBefore = hitPointsBefore;
            HitPointsAfter = hitPointsAfter;
            BecameResolved = becameResolved;
        }
    }
}
