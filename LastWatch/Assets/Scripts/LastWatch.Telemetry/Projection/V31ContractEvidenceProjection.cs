using System;
using LastWatch.Core.Identity;
using LastWatch.Core.State;
using LastWatch.Core.Terminal;

namespace LastWatch.Telemetry.Projection
{
    public sealed class SourceContractEvidence
    {
        public WorldSourceState WorldState { get; internal set; }
        public SourceAccessState AccessState { get; internal set; }
        public int? OverloadCommitTick { get; internal set; }
        public ulong? OverloadCommitSequence { get; internal set; }
        public int? OverloadCompletionTick { get; internal set; }
        public bool ConfirmedPartnerExecution { get; internal set; }
        public int? ConfirmedPartnerExecutionTick { get; internal set; }
        public ulong? ConfirmedPartnerExecutionSequence { get; internal set; }
    }

    public sealed class TerminalTicketContractEvidence
    {
        public ulong TicketId { get; internal set; }
        public int QueueIndex { get; internal set; }
        public string EnemyArchetypeId { get; internal set; }
        public int OriginalScheduledTick { get; internal set; }
        public string InitialRouteChoiceKey { get; internal set; }
        public int RulesetVersion { get; internal set; }
        public TerminalTicketLifecycle Lifecycle { get; internal set; }
        public EntityId? ActiveEntityId { get; internal set; }
    }

    public sealed class TerminalContractEvidence
    {
        public TerminalLifecycleState ProgressState { get; internal set; }
        public string QueueHash { get; internal set; }
        public int TotalCount { get; internal set; }
        public int PendingCount { get; internal set; }
        public int ActiveCount { get; internal set; }
        public int ResolvedCount { get; internal set; }
        public TerminalTicketContractEvidence[] Tickets { get; internal set; }
    }

    public sealed class V31ContractEvidence
    {
        public SourceContractEvidence Source { get; internal set; }
        public TerminalContractEvidence Terminal { get; internal set; }
    }

    public static class V31ContractEvidenceProjection
    {
        public static V31ContractEvidence Capture(RunStateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            SourceStateSnapshot source = snapshot.Source ?? throw new ArgumentException("Source snapshot is required.", nameof(snapshot));
            TerminalStateSnapshot terminal = snapshot.Terminal ?? throw new ArgumentException("Terminal snapshot is required.", nameof(snapshot));
            TerminalTicketSnapshot[] sourceTickets = terminal.Tickets ?? Array.Empty<TerminalTicketSnapshot>();
            var tickets = new TerminalTicketContractEvidence[sourceTickets.Length];
            for (int index = 0; index < sourceTickets.Length; index++)
            {
                TerminalTicketSnapshot item = sourceTickets[index];
                tickets[index] = new TerminalTicketContractEvidence
                {
                    TicketId = item.Ticket.TicketId,
                    QueueIndex = item.Ticket.QueueIndex,
                    EnemyArchetypeId = item.Ticket.EnemyArchetypeId,
                    OriginalScheduledTick = item.Ticket.OriginalScheduledTick,
                    InitialRouteChoiceKey = item.Ticket.InitialRouteChoiceKey,
                    RulesetVersion = item.Ticket.RulesetVersion,
                    Lifecycle = item.Lifecycle,
                    ActiveEntityId = item.ActiveEntityId
                };
            }

            return new V31ContractEvidence
            {
                Source = new SourceContractEvidence
                {
                    WorldState = source.WorldState,
                    AccessState = source.AccessState,
                    OverloadCommitTick = source.OverloadCommitTick,
                    OverloadCommitSequence = source.OverloadCommitSequence,
                    OverloadCompletionTick = source.OverloadCompletionTick,
                    ConfirmedPartnerExecution = source.ConfirmedPartnerExecution,
                    ConfirmedPartnerExecutionTick = source.ConfirmedPartnerExecutionTick,
                    ConfirmedPartnerExecutionSequence = source.ConfirmedPartnerExecutionSequence
                },
                Terminal = new TerminalContractEvidence
                {
                    ProgressState = terminal.ProgressState,
                    QueueHash = terminal.QueueHash,
                    TotalCount = terminal.TotalCount,
                    PendingCount = terminal.PendingCount,
                    ActiveCount = terminal.ActiveCount,
                    ResolvedCount = terminal.ResolvedCount,
                    Tickets = tickets
                }
            };
        }
    }
}
