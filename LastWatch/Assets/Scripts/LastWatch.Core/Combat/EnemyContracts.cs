using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Combat
{
    public enum EnemyArchetype { Walker = 0, Runner = 1, Climber = 2, Breaker = 3 }
    public enum EnemyMovementState { Spawn, Approach, TargetActorApproach, AttackWindup, AttackRecover, ApproachSupport, Climb, JointWindup, Recover, PathRecovery, Dead }
    public enum RunnerEvadePhase { None, Scheduled, Moving, Completed, Skipped }
    public enum ClimberRailBranch { Left, Right, Diagonal }
    public enum EnemyRerouteReason { None, PathStalled, FaceUnavailable }

    public readonly struct EnemyArchetypeConfig
    {
        public EnemyArchetype Archetype { get; }
        public float HitPoints { get; }
        public float GroundSpeedMetersPerSecond { get; }
        public float ClimbSpeedMetersPerSecond { get; }
        public float StructureDamage { get; }
        public float StructurePeriodSeconds { get; }
        public float ActorDamage { get; }
        public float ActorPeriodSeconds { get; }
        public float ActorWindupSeconds { get; }
        public float StructureWindupSeconds { get; }
        public float Pressure { get; }
        public bool CanTargetGroundActor { get; }
        public bool CanEnterUpperDeck => false;
        public bool CanTargetUpperActor => false;
        public bool HasRangedAttack => false;

        public EnemyArchetypeConfig(EnemyArchetype archetype, float hp, float groundSpeed, float climbSpeed,
            float structureDamage, float structurePeriod, float actorDamage, float actorPeriod,
            float actorWindup, float structureWindup, float pressure, bool canTargetGroundActor)
        {
            Archetype = archetype; HitPoints = hp; GroundSpeedMetersPerSecond = groundSpeed;
            ClimbSpeedMetersPerSecond = climbSpeed; StructureDamage = structureDamage;
            StructurePeriodSeconds = structurePeriod; ActorDamage = actorDamage;
            ActorPeriodSeconds = actorPeriod; ActorWindupSeconds = actorWindup;
            StructureWindupSeconds = structureWindup; Pressure = pressure;
            CanTargetGroundActor = canTargetGroundActor;
        }

        internal void AppendCanonical(StringBuilder b)
        {
            b.Append((int)Archetype).Append('|').Append(HitPoints.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(GroundSpeedMetersPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(ClimbSpeedMetersPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(StructureDamage.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(StructurePeriodSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(ActorDamage.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(ActorPeriodSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(ActorWindupSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(StructureWindupSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
             .Append(Pressure.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(CanTargetGroundActor ? '1' : '0');
        }
    }

    public static class EnemyArchetypeCatalog
    {
        private static readonly EnemyArchetypeConfig[] Values = {
            new EnemyArchetypeConfig(EnemyArchetype.Walker,45f,2.2f,0f,4f,1.6f,18f,1.5f,0.55f,0f,1f,true),
            new EnemyArchetypeConfig(EnemyArchetype.Runner,36f,4.8f,0f,3f,1.2f,12f,0.9f,0.30f,0f,1.25f,true),
            new EnemyArchetypeConfig(EnemyArchetype.Climber,72f,2f,2.5f,8f,6f,0f,0f,0f,0f,1.75f,false),
            new EnemyArchetypeConfig(EnemyArchetype.Breaker,180f,1.6f,0f,20f,7f,0f,0f,0f,2.5f,3f,false)
        };
        public static IReadOnlyList<EnemyArchetypeConfig> All => Array.AsReadOnly((EnemyArchetypeConfig[])Values.Clone());
        public static EnemyArchetypeConfig Get(EnemyArchetype archetype) => Values[(int)archetype];
        public static string CanonicalHash()
        {
            var b = new StringBuilder("LASTWATCH.ENEMY.CONFIG.v3.0|");
            foreach (EnemyArchetypeConfig value in Values) { value.AppendCanonical(b); b.Append(';'); }
            using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())));
        }
        internal static string Hex(byte[] bytes) { var b = new StringBuilder(bytes.Length * 2); foreach (byte x in bytes) b.Append(x.ToString("x2")); return b.ToString(); }
    }

    public readonly struct EnemyReadOnlySnapshot
    {
        public int FixedTick { get; }
        public EntityId EntityId { get; }
        public EnemyArchetype Archetype { get; }
        public EnemyMovementState State { get; }
        public FaceId AssignedFace { get; }
        public FaceId CurrentFace { get; }
        public AttackPointId TargetStructure { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public int WarningStartTick { get; }
        public int EarliestLegalDamageTick { get; }
        public int? ActualDamageTick { get; }
        public RouteId Route { get; }
        public ClimberRailBranch? ClimberBranch { get; }
        public RunnerEvadePhase EvadePhase { get; }
        public ulong? TriggeringEventSequence { get; }
        public bool TriggerDamageRemainedApplied { get; }
        public EnemyRerouteReason RerouteReason { get; }

        public EnemyReadOnlySnapshot(int tick, EntityId id, EnemyArchetype archetype, EnemyMovementState state,
            FaceId assignedFace, FaceId currentFace, AttackPointId target, Vector3 position, Vector3 forward,
            int warningStartTick, int earliestDamageTick, int? actualDamageTick, RouteId route,
            ClimberRailBranch? branch, RunnerEvadePhase evade, ulong? triggeringSequence,
            bool triggerDamageRemainedApplied, EnemyRerouteReason rerouteReason)
        {
            FixedTick=tick; EntityId=id; Archetype=archetype; State=state; AssignedFace=assignedFace;
            CurrentFace=currentFace; TargetStructure=target; Position=position; Forward=forward;
            WarningStartTick=warningStartTick; EarliestLegalDamageTick=earliestDamageTick;
            ActualDamageTick=actualDamageTick; Route=route; ClimberBranch=branch; EvadePhase=evade;
            TriggeringEventSequence=triggeringSequence; TriggerDamageRemainedApplied=triggerDamageRemainedApplied;
            RerouteReason=rerouteReason;
        }
    }
}
