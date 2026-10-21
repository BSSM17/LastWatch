using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Identity;

namespace LastWatch.Core.Terminal
{
    public enum TerminalLifecycleState { None = 0, Pending = 1, Active = 2, GroundContinuation = 3, Cleared = 4 }
    public enum TerminalTicketLifecycle { Pending = 0, Active = 1, Resolved = 2 }

    public readonly struct TerminalEntryKey : IEquatable<TerminalEntryKey>
    {
        public RunId RunId { get; }
        public int RulesetVersion { get; }
        public bool IsValid => RunId.IsValid && RulesetVersion > 0;

        public TerminalEntryKey(RunId runId, int rulesetVersion)
        {
            if (!runId.IsValid) throw new ArgumentException("RunId is required.", nameof(runId));
            if (rulesetVersion <= 0) throw new ArgumentOutOfRangeException(nameof(rulesetVersion));
            RunId = runId;
            RulesetVersion = rulesetVersion;
        }

        public bool Equals(TerminalEntryKey other) => RunId == other.RunId && RulesetVersion == other.RulesetVersion;
        public override bool Equals(object obj) => obj is TerminalEntryKey other && Equals(other);
        public override int GetHashCode() => (RunId.GetHashCode() * 397) ^ RulesetVersion;
    }

    public readonly struct TerminalTicket
    {
        public ulong TicketId { get; }
        public int QueueIndex { get; }
        public string EnemyArchetypeId { get; }
        public int OriginalScheduledTick { get; }
        public string InitialRouteChoiceKey { get; }
        public int RulesetVersion { get; }

        public TerminalTicket(ulong ticketId, int queueIndex, string enemyArchetypeId,
            int originalScheduledTick, string initialRouteChoiceKey, int rulesetVersion)
        {
            if (ticketId == 0) throw new ArgumentOutOfRangeException(nameof(ticketId));
            if (queueIndex < 0) throw new ArgumentOutOfRangeException(nameof(queueIndex));
            if (string.IsNullOrWhiteSpace(enemyArchetypeId)) throw new ArgumentException("Archetype is required.", nameof(enemyArchetypeId));
            if (originalScheduledTick < 0) throw new ArgumentOutOfRangeException(nameof(originalScheduledTick));
            if (string.IsNullOrWhiteSpace(initialRouteChoiceKey)) throw new ArgumentException("Route choice key is required.", nameof(initialRouteChoiceKey));
            if (rulesetVersion <= 0) throw new ArgumentOutOfRangeException(nameof(rulesetVersion));
            TicketId = ticketId;
            QueueIndex = queueIndex;
            EnemyArchetypeId = enemyArchetypeId;
            OriginalScheduledTick = originalScheduledTick;
            InitialRouteChoiceKey = initialRouteChoiceKey;
            RulesetVersion = rulesetVersion;
        }
    }

    internal sealed class TerminalTicketRecord
    {
        internal TerminalTicket Ticket;
        internal TerminalTicketLifecycle Lifecycle;
        internal EntityId? ActiveEntityId;
    }

    public sealed class TerminalTicketSnapshot
    {
        public TerminalTicket Ticket { get; private set; }
        public TerminalTicketLifecycle Lifecycle { get; private set; }
        public EntityId? ActiveEntityId { get; private set; }
        internal static TerminalTicketSnapshot From(TerminalTicketRecord record) => new TerminalTicketSnapshot
        { Ticket = record.Ticket, Lifecycle = record.Lifecycle, ActiveEntityId = record.ActiveEntityId };
    }

    public sealed class TerminalStateSnapshot
    {
        public TerminalLifecycleState ProgressState { get; internal set; }
        public TerminalEntryKey? EntryKey { get; internal set; }
        public ulong? Seed { get; internal set; }
        public int? EnteredTick { get; internal set; }
        public int? GroundContinuationTick { get; internal set; }
        public int? ClearedTick { get; internal set; }
        public int? NextTicketIndex { get; internal set; }
        public string QueueHash { get; internal set; }
        public int TotalCount { get; internal set; }
        public int PendingCount { get; internal set; }
        public int ActiveCount { get; internal set; }
        public int ResolvedCount { get; internal set; }
        public TerminalTicketSnapshot[] Tickets { get; internal set; }
    }

    public sealed class TerminalState
    {
        private readonly List<TerminalTicketRecord> tickets = new List<TerminalTicketRecord>();
        public static TerminalState Empty => new TerminalState();
        public TerminalLifecycleState ProgressState { get; internal set; }
        public TerminalEntryKey? EntryKey { get; internal set; }
        public ulong? Seed { get; internal set; }
        public int? EnteredTick { get; internal set; }
        public int? GroundContinuationTick { get; internal set; }
        public int? ClearedTick { get; internal set; }
        public int? NextTicketIndex { get; internal set; }
        internal IEnumerable<TerminalTicketRecord> Records => tickets;

        internal static TerminalState Prepare(TerminalEntryKey entryKey, ulong seed, IEnumerable<TerminalTicket> source)
        {
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (source == null) throw new ArgumentNullException(nameof(source));
            var state = new TerminalState
            {
                ProgressState = TerminalLifecycleState.Pending,
                EntryKey = entryKey,
                Seed = seed,
                NextTicketIndex = 0
            };
            foreach (TerminalTicket ticket in source)
                state.RestoreTicket(ticket, TerminalTicketLifecycle.Pending, null);
            return state;
        }

        internal bool TryActivate(int queueIndex, EntityId entityId)
        {
            if (!entityId.IsValid) throw new ArgumentException("EntityId is required.", nameof(entityId));
            TerminalTicketRecord record = tickets.Find(x => x.Ticket.QueueIndex == queueIndex);
            if (record == null || record.Lifecycle != TerminalTicketLifecycle.Pending) return false;
            record.Lifecycle = TerminalTicketLifecycle.Active;
            record.ActiveEntityId = entityId;
            NextTicketIndex = queueIndex + 1;
            return true;
        }

        internal bool TryResolve(EntityId entityId)
        {
            TerminalTicketRecord record = tickets.Find(x => x.ActiveEntityId.HasValue && x.ActiveEntityId.Value == entityId);
            if (record == null || record.Lifecycle != TerminalTicketLifecycle.Active) return false;
            record.Lifecycle = TerminalTicketLifecycle.Resolved;
            record.ActiveEntityId = null;
            return true;
        }

        // Collapse changes routes and world state, never immutable ticket identity or lifecycle.
        internal void PreserveForCollapse() { }

        internal void RestoreTicket(TerminalTicket ticket, TerminalTicketLifecycle lifecycle, EntityId? activeEntityId)
        {
            if (EntryKey.HasValue && ticket.RulesetVersion != EntryKey.Value.RulesetVersion)
                throw new FormatException("Terminal ticket ruleset does not match entry key.");
            if (tickets.Exists(x => x.Ticket.TicketId == ticket.TicketId || x.Ticket.QueueIndex == ticket.QueueIndex))
                throw new FormatException("Duplicate Terminal ticket identity or queue index.");
            if ((lifecycle == TerminalTicketLifecycle.Active) != activeEntityId.HasValue)
                throw new FormatException("Only an active Terminal ticket may have an active EntityId.");
            tickets.Add(new TerminalTicketRecord { Ticket = ticket, Lifecycle = lifecycle, ActiveEntityId = activeEntityId });
        }

        internal TerminalStateSnapshot Snapshot()
        {
            var ordered = new List<TerminalTicketRecord>(tickets);
            ordered.Sort((a, b) => a.Ticket.QueueIndex.CompareTo(b.Ticket.QueueIndex));
            var values = new TerminalTicketSnapshot[ordered.Count];
            int pending = 0, active = 0, resolved = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                values[i] = TerminalTicketSnapshot.From(ordered[i]);
                if (ordered[i].Lifecycle == TerminalTicketLifecycle.Pending) pending++;
                else if (ordered[i].Lifecycle == TerminalTicketLifecycle.Active) active++;
                else resolved++;
            }
            return new TerminalStateSnapshot
            {
                ProgressState = ProgressState, EntryKey = EntryKey, Seed = Seed,
                EnteredTick = EnteredTick, GroundContinuationTick = GroundContinuationTick,
                ClearedTick = ClearedTick, NextTicketIndex = NextTicketIndex,
                QueueHash = TerminalQueueHash.Compute(EntryKey, Seed, ordered), TotalCount = values.Length,
                PendingCount = pending, ActiveCount = active, ResolvedCount = resolved, Tickets = values
            };
        }
    }

    public static class TerminalQueueHash
    {
        public const int HashVersion = 1;

        internal static string Compute(TerminalEntryKey? entryKey, ulong? seed, List<TerminalTicketRecord> ordered)
        {
            if (!entryKey.HasValue) return null;
            if (!seed.HasValue) throw new InvalidOperationException("A prepared Terminal queue requires a seed.");
            var text = new StringBuilder();
            text.Append(HashVersion).Append('|').Append(entryKey.Value.RulesetVersion).Append('|')
                .Append(seed.Value.ToString("x16", CultureInfo.InvariantCulture));
            foreach (TerminalTicketRecord record in ordered)
            {
                TerminalTicket ticket = record.Ticket;
                text.Append('|').Append(ticket.TicketId.ToString("x16", CultureInfo.InvariantCulture))
                    .Append(':').Append(ticket.QueueIndex).Append(':');
                AppendLengthPrefixed(text, ticket.EnemyArchetypeId);
                text.Append(':').Append(ticket.OriginalScheduledTick).Append(':');
                AppendLengthPrefixed(text, ticket.InitialRouteChoiceKey);
            }
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void AppendLengthPrefixed(StringBuilder text, string value) =>
            text.Append(value.Length).Append('#').Append(value);
    }
}
