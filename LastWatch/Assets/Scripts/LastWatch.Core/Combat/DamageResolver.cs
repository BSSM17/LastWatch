using LastWatch.Core.State;
using LastWatch.Core.Safety;
using LastWatch.Core.Identity;

namespace LastWatch.Core.Combat
{
    public sealed class DamageResolver
    {
        private readonly RunState state;
        public DamageResolver(RunState state) => this.state = state ?? throw new System.ArgumentNullException(nameof(state));


        internal M4ClimberStructureAttackSnapshot ApplyClimberStructureAttack(EnemyDirectorRuntime director,
            M4TopologySnapshot topology, EntityId source, ulong ticketId, int activationRunTick,
            int nightTick, bool reachedStandOff, float? currentGap, bool upperVisible, UnityEngine.Vector3 hitPoint)
        {
            M4ClimberStructureAttackSnapshot Hold(string reason, M4BindingRole? layer = null)
                => new M4ClimberStructureAttackSnapshot(reason, state.FixedTick, layer, null, null, upperVisible);
            if (director == null || topology == null || activationRunTick < 0 || activationRunTick > state.FixedTick)
                return Hold("CANONICAL_ATTACK_CONTEXT_UNAVAILABLE");
            SpawnTicketSnapshot? active = null;
            foreach (SpawnTicketSnapshot candidate in director.Snapshot().Tickets)
                if (candidate.Lifecycle == SpawnTicketLifecycle.Active && candidate.ActiveEntityId == source &&
                    candidate.Ticket.TicketId == ticketId) { active = candidate; break; }
            if (!active.HasValue || active.Value.Ticket.Archetype != EnemyArchetype.Climber ||
                !state.TryGetDamageable(source, out DamageableSnapshot enemy) || enemy.Kind != DamageableKind.Enemy ||
                enemy.IsResolved || enemy.ActiveEnemyTicketId != ticketId)
                return Hold("ACTIVE_CLIMBER_INCARNATION_UNAVAILABLE");
            SpawnTicketSnapshot incarnation = active.Value;
            LastWatch.Core.Space.FaceId face = incarnation.Ticket.Face;
            if (System.Array.IndexOf(topology.ValidFaces, face) < 0) return Hold("ASSIGNED_FACE_UNAVAILABLE");
            if (!reachedStandOff || !currentGap.HasValue || float.IsNaN(currentGap.Value) ||
                float.IsInfinity(currentGap.Value) || currentGap.Value <= 0f)
                return Hold("POSITIVE_EXTERNAL_STANDOFF_REQUIRED");
            int warningTicks = checked(8 * EnemyDirector.TickRate);
            if (!incarnation.ActualSpawnTick.HasValue || nightTick < incarnation.Ticket.EarliestStructureDamageTick ||
                nightTick < checked(incarnation.Ticket.WarningStartTick + warningTicks) ||
                nightTick < checked(incarnation.ActualSpawnTick.Value + warningTicks))
                return Hold("FIRST_HIT_TIME_GATE_PENDING; ACTUAL_WARNING_AUTHORITY_NOT_EXPOSED");
            EnemyArchetypeConfig config = EnemyArchetypeCatalog.Get(EnemyArchetype.Climber);
            int period = checked((int)System.Math.Ceiling(config.StructurePeriodSeconds * EnemyDirector.TickRate));
            foreach (CommittedFactRecord fact in state.CommittedFacts)
                if (fact.Kind == CommittedFactKind.Damage && fact.Damage.SourceEntityId == source &&
                    fact.Damage.SourceActorKind == DamageableKind.Enemy && fact.Damage.TargetKind == DamageableKind.Structure &&
                    fact.Damage.FixedTick >= activationRunTick && state.FixedTick - fact.Damage.FixedTick < period)
                    return Hold("WAITING_FOR_VALID_DAMAGE_EVENT_PERIOD");
            EntityId target = default;
            M4BindingRole? selectedLayer = null;
            foreach (M4BindingRole layer in new[] { M4BindingRole.Barrier, M4BindingRole.NightBrace, M4BindingRole.Support })
            {
                EntityId bound = default;
                foreach (M4EntityBinding binding in topology.Bindings)
                    if (binding.Face == face && binding.Role == layer)
                    {
                        if (bound.IsValid) return Hold("AMBIGUOUS_SAME_FACE_STRUCTURE_BINDING", layer);
                        bound = binding.EntityId;
                    }
                if (!bound.IsValid || !state.TryGetDamageable(bound, out DamageableSnapshot structure) ||
                    structure.Kind != DamageableKind.Structure) return Hold("CANONICAL_STRUCTURE_LAYER_UNAVAILABLE", layer);
                if (structure.HitPoints <= 0f || structure.IsResolved) continue;
                target = bound; selectedLayer = layer; break;
            }
            if (!target.IsValid) return Hold("FACE_LOST_INTEGRATION_UNAVAILABLE; ALL_STRUCTURE_LAYERS_RESOLVED");
            // Scheduled warning fields are not an actually emitted valid-warning fact.
            if (selectedLayer == M4BindingRole.Support)
                return Hold("SUPPORT_ATTACK_BLOCKED; ACTUAL_VALID_WARNING_EVENT_UNAVAILABLE", selectedLayer);
            // Current unallocated QA has Brace0. Configured brace loss/lifecycle is not this slice.
            if (selectedLayer == M4BindingRole.NightBrace)
                return Hold("CONFIGURED_NIGHT_BRACE_LIFECYCLE_AND_LOSS_EVIDENCE_UNAVAILABLE", selectedLayer);
            if (!upperVisible) return Hold("CURRENT_UPPER_BODY_AND_JOINT_SIGHT_UNAVAILABLE", selectedLayer);
            WindupAttemptResult windup = director.TryBeginHighThreatWindupDetailed(source, nightTick);
            if (!windup.Accepted) return Hold("JOINT_WINDUP_ADMISSION_BLOCKED", selectedLayer);
            try
            {
                ulong sequence = state.IssueEventSequence();
                var packet = new DamagePacket(sequence, state.FixedTick, source, source, target,
                    DamageKind.Melee, config.StructureDamage, hitPoint, hitPoint, default);
                DamageReceipt receipt = Apply(packet);
                return new M4ClimberStructureAttackSnapshot(receipt.Status == DamageApplyStatus.Applied
                    ? "CANONICAL_STRUCTURE_DAMAGE_APPLIED" : "CANONICAL_DAMAGE_REJECTED_" + receipt.Status,
                    state.FixedTick, selectedLayer, target, receipt, upperVisible);
            }
            finally { director.TryEndHighThreatWindup(source); }
        }

        public DamageReceipt Apply(in DamagePacket packet)
        {
            return ApplyInternal(packet, null);
        }

        public DamageReceipt ApplyEnemyAttack(in DamagePacket packet, in ActorSpatialSnapshot targetSpatial)
        {
            return ApplyInternal(packet, targetSpatial);
        }

        private DamageReceipt ApplyInternal(in DamagePacket packet, ActorSpatialSnapshot? enemyTargetSpatial)
        {
            if (packet.EventSequence == 0 || packet.FixedTick != state.FixedTick || !packet.Target.IsValid ||
                packet.Amount <= 0 || float.IsNaN(packet.Amount) || float.IsInfinity(packet.Amount))
                return new DamageReceipt(DamageApplyStatus.InvalidPacket, packet.EventSequence, packet.Target, 0, 0, false);
            if (!state.TryGetRecord(packet.Target, out DamageableRecord target))
                return new DamageReceipt(DamageApplyStatus.InvalidTarget, packet.EventSequence, packet.Target, 0, 0, false);
            if (target.Kind == DamageableKind.Enemy && target.ActiveEnemyTicketId.HasValue &&
                (!packet.TargetTicketId.HasValue || packet.TargetTicketId.Value != target.ActiveEnemyTicketId.Value))
                return new DamageReceipt(DamageApplyStatus.InvalidTargetIncarnation, packet.EventSequence, packet.Target,
                    target.HitPoints, target.HitPoints, false);
            // Incarnation identity wins over duplicate classification: a recorded packet
            // from a resolved ticket must be diagnosable as stale after its pool slot is reused.
            if (state.IsDamageApplied(packet.EventSequence))
                return new DamageReceipt(DamageApplyStatus.Duplicate, packet.EventSequence, packet.Target, 0, 0, false);
            if (enemyTargetSpatial.HasValue)
            {
                ActorSpatialSnapshot spatial = enemyTargetSpatial.Value;
                if (spatial.EntityId != packet.Target || spatial.Kind != target.Kind ||
                    (target.Kind != DamageableKind.Player && target.Kind != DamageableKind.Partner))
                    return new DamageReceipt(DamageApplyStatus.InvalidPacket, packet.EventSequence, packet.Target,
                        target.HitPoints, target.HitPoints, false);
                if (!UpperSafeZoneSafety.CanReceiveEnemyAttack(spatial))
                    return new DamageReceipt(DamageApplyStatus.RejectedUpperSafeZone, packet.EventSequence, packet.Target,
                        target.HitPoints, target.HitPoints, false);
            }
            if (target.IsResolved)
                return new DamageReceipt(DamageApplyStatus.TargetAlreadyResolved, packet.EventSequence, packet.Target,
                    target.HitPoints, target.HitPoints, false);

            float before = target.HitPoints;
            PlayerLifeState playerLifeBefore = state.PlayerLife;
            PartnerLifeState partnerLifeBefore = state.PartnerLife;
            target.HitPoints = System.Math.Max(0f, before - packet.Amount);
            bool becameResolved = target.HitPoints <= 0f;
            target.IsResolved = becameResolved;
            state.MarkDamageApplied(packet.EventSequence);

            if (becameResolved && target.Kind == DamageableKind.Player && state.PlayerLife == PlayerLifeState.Alive)
            {
                state.PlayerLife = PlayerLifeState.Dead;
                state.PlayerDeath = new DeathFact(packet.FixedTick, packet.EventSequence, packet.Kind);
            }
            else if (becameResolved && target.Kind == DamageableKind.Partner && state.PartnerLife != PartnerLifeState.Dead)
            {
                state.PartnerLife = PartnerLifeState.Downed;
                target.IsResolved = false;
                becameResolved = false;
            }

            var receipt = new DamageReceipt(DamageApplyStatus.Applied, packet.EventSequence, packet.Target,
                before, target.HitPoints, becameResolved);
            DamageableKind? sourceKind = state.TryGetRecord(packet.Source, out DamageableRecord sourceRecord)
                ? sourceRecord.Kind : (DamageableKind?)null;
            state.AppendCommittedDamage(new CommittedDamageFact(packet, sourceKind, target.Kind, receipt));

            if (target.Kind == DamageableKind.Player && playerLifeBefore != state.PlayerLife)
            {
                state.AppendCommittedActorTransition(new CommittedActorStateTransitionFact(
                    target.Id, DamageableKind.Player, PlayerCode(playerLifeBefore), PlayerCode(state.PlayerLife),
                    packet.FixedTick, packet.EventSequence, state.PlayerDeath, packet.EventSequence,
                    sourceKind, packet.Source.IsValid ? packet.Source : (EntityId?)null));
            }
            else if (target.Kind == DamageableKind.Partner && partnerLifeBefore != state.PartnerLife)
            {
                state.AppendCommittedActorTransition(new CommittedActorStateTransitionFact(
                    target.Id, DamageableKind.Partner, PartnerCode(partnerLifeBefore), PartnerCode(state.PartnerLife),
                    packet.FixedTick, packet.EventSequence, state.PartnerDeath,
                    state.PartnerLife == PartnerLifeState.Dead ? packet.EventSequence : (ulong?)null,
                    state.PartnerLife == PartnerLifeState.Dead ? sourceKind : null,
                    state.PartnerLife == PartnerLifeState.Dead && packet.Source.IsValid ? packet.Source : (EntityId?)null));
            }
            return receipt;
        }

        private static ActorLifeStateCode PlayerCode(PlayerLifeState value) =>
            value == PlayerLifeState.Dead ? ActorLifeStateCode.PlayerDead : ActorLifeStateCode.PlayerAlive;

        private static ActorLifeStateCode PartnerCode(PartnerLifeState value)
        {
            switch (value)
            {
                case PartnerLifeState.Healthy: return ActorLifeStateCode.PartnerHealthy;
                case PartnerLifeState.Injured: return ActorLifeStateCode.PartnerInjured;
                case PartnerLifeState.Downed: return ActorLifeStateCode.PartnerDowned;
                case PartnerLifeState.Stabilized: return ActorLifeStateCode.PartnerStabilized;
                default: return ActorLifeStateCode.PartnerDead;
            }
        }
    }
    // Detached actual resolver receipt, not a damage timer or a geometry/visual PASS.
    public sealed class M4ClimberStructureAttackSnapshot
    {
        public string Outcome { get; }
        public int RunFixedTick { get; }
        public M4BindingRole? Layer { get; }
        public LastWatch.Core.Identity.EntityId? Target { get; }
        public DamageReceipt? Receipt { get; }
        public bool CurrentUpperVisibilityAccepted { get; }
        public bool ActualDamageApplied => Receipt.HasValue && Receipt.Value.Status == DamageApplyStatus.Applied;
        internal M4ClimberStructureAttackSnapshot(string outcome, int tick, M4BindingRole? layer,
            LastWatch.Core.Identity.EntityId? target, DamageReceipt? receipt, bool visible)
        { Outcome = outcome; RunFixedTick = tick; Layer = layer; Target = target; Receipt = receipt; CurrentUpperVisibilityAccepted = visible; }
    }

}
