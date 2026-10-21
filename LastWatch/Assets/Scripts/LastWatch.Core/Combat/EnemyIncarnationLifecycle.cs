using System;
using System.Collections.Generic;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Core.Combat
{
    // Core-owned bridge between the M3 ticket director/pool and the canonical
    // RunState damage model. It intentionally owns no queue, actor transform, or HP table.
    public readonly struct ActiveEnemyIncarnation
    {
        public EntityId EntityId { get; }
        public ulong TicketId { get; }
        public EnemyArchetype Archetype { get; }
        internal ActiveEnemyIncarnation(EntityId entityId, ulong ticketId, EnemyArchetype archetype)
        { EntityId = entityId; TicketId = ticketId; Archetype = archetype; }
    }

    public static class EnemyIncarnationLifecycle
    {
        public static bool TryActivateNext(EnemyDirectorRuntime director, EnemyPool pool, RunState run, int tick,
            out ActiveEnemyIncarnation incarnation)
        {
            if (director == null || pool == null || run == null) throw new ArgumentNullException();
            ActivationAttemptResult result = director.TryActivateNextDetailed(tick, pool);
            if (!result.Accepted) { incarnation = default; return false; }
            SpawnTicketSnapshot active = FindActive(director.Snapshot(), result.EntityId.GetValueOrDefault());
            run.ArmEnemyIncarnation(active.ActiveEntityId.Value, active.Ticket.TicketId,
                EnemyArchetypeCatalog.Get(active.Ticket.Archetype).HitPoints);
            incarnation = new ActiveEnemyIncarnation(active.ActiveEntityId.Value, active.Ticket.TicketId, active.Ticket.Archetype);
            return true;
        }

        public static bool TryResolveLethal(EnemyDirectorRuntime director, EnemyPool pool, RunState run,
            in DamagePacket packet, in DamageReceipt receipt, int tick)
        {
            if (director == null || pool == null || run == null) throw new ArgumentNullException();
            if (receipt.Status != DamageApplyStatus.Applied || !receipt.BecameResolved || !packet.TargetTicketId.HasValue ||
                receipt.Target != packet.Target || !run.IsActiveEnemyIncarnation(packet.Target, packet.TargetTicketId.Value))
                return false;
            if (!director.TryResolve(packet.Target, packet.TargetTicketId.Value, tick, pool)) return false;
            if (!run.DisarmResolvedEnemyIncarnation(packet.Target, packet.TargetTicketId.Value))
                throw new InvalidOperationException("Director resolved an enemy whose canonical HP incarnation could not be disarmed.");
            return true;
        }

        // The Director snapshot is the persistent ticket-to-pool mapping. RunState holds
        // the persistent HP. Rebinding only happens after restore and never resets HP.
        public static void RestoreAndValidate(RunState run, EnemyDirectorRuntime director)
        {
            if (run == null || director == null) throw new ArgumentNullException();
            EnemyDirectorRuntimeSnapshot snapshot = director.Snapshot();
            var activeTickets = new Dictionary<ulong, ulong>();
            foreach (SpawnTicketSnapshot row in snapshot.Tickets)
            {
                if (row.Lifecycle != SpawnTicketLifecycle.Active) continue;
                if (!row.ActiveEntityId.HasValue || activeTickets.ContainsKey(row.ActiveEntityId.Value.Value))
                    throw new FormatException("Director active incarnation identity is invalid.");
                activeTickets.Add(row.ActiveEntityId.Value.Value, row.Ticket.TicketId);
                run.RestoreActiveEnemyIncarnation(row.ActiveEntityId.Value, row.Ticket.TicketId);
            }
            foreach (DamageableRecord record in run.ActiveEnemyIncarnations)
            {
                if (!activeTickets.TryGetValue(record.Id.Value, out ulong ticketId) || ticketId != record.ActiveEnemyTicketId.Value)
                    throw new FormatException("RunState enemy incarnation has no matching Director ticket.");
            }
        }

        public static void Validate(RunState run, EnemyDirectorRuntime director)
        {
            if (run == null || director == null) throw new ArgumentNullException();
            var activeTickets = new Dictionary<ulong, ulong>();
            foreach (SpawnTicketSnapshot row in director.Snapshot().Tickets)
            {
                if (row.Lifecycle != SpawnTicketLifecycle.Active) continue;
                if (!row.ActiveEntityId.HasValue || activeTickets.ContainsKey(row.ActiveEntityId.Value.Value) ||
                    !run.IsActiveEnemyIncarnation(row.ActiveEntityId.Value, row.Ticket.TicketId))
                    throw new FormatException("Director/RunState active enemy incarnation mismatch.");
                activeTickets.Add(row.ActiveEntityId.Value.Value, row.Ticket.TicketId);
            }
            foreach (DamageableRecord record in run.ActiveEnemyIncarnations)
                if (!activeTickets.TryGetValue(record.Id.Value, out ulong ticketId) || ticketId != record.ActiveEnemyTicketId.Value)
                    throw new FormatException("RunState enemy incarnation has no matching Director ticket.");
        }

        static SpawnTicketSnapshot FindActive(EnemyDirectorRuntimeSnapshot snapshot, EntityId entity)
        {
            foreach (SpawnTicketSnapshot row in snapshot.Tickets)
                if (row.Lifecycle == SpawnTicketLifecycle.Active && row.ActiveEntityId.HasValue && row.ActiveEntityId.Value == entity)
                    return row;
            throw new InvalidOperationException("Accepted Director activation has no active ticket identity.");
        }
    }
}
