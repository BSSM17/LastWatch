using System;
using System.Collections.Generic;
using LastWatch.Core.State;

namespace LastWatch.Telemetry.Projection
{
    public sealed class TelemetryContextSupplement
    {
        public Guid SessionId { get; }

        public TelemetryContextSupplement(Guid sessionId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("SessionId is required.", nameof(sessionId));
            SessionId = sessionId;
        }
    }

    public sealed class CommittedFactProjectionBatchResult
    {
        public ulong StartCursor { get; internal set; }
        public ulong AcknowledgedCursor { get; internal set; }
        public int CoreFactCount { get; internal set; }
        public TelemetryProjectionResult[] ProjectionResults { get; internal set; }
        public bool IsPreMilestoneUnavailable { get; internal set; }
        public bool IsStoredInRunState { get; internal set; }
        public bool IsCrossRunComparisonEligible => !IsPreMilestoneUnavailable;
    }

    public sealed class CommittedFactTelemetryAdapter
    {
        private readonly T1TelemetryProjector projector;

        public CommittedFactTelemetryAdapter(T1TelemetryProjector projector)
        {
            this.projector = projector ?? throw new ArgumentNullException(nameof(projector));
        }

        public CommittedFactProjectionBatchResult ProjectAvailable(RunState state, ulong requestedCursor,
            TelemetryContextSupplement supplement)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (supplement == null) throw new ArgumentNullException(nameof(supplement));
            CommittedFactJournalSnapshot initial = CommittedFactJournal.ReadAfter(state, requestedCursor);
            ulong start = Math.Max(requestedCursor, initial.AcknowledgedCursor);
            CommittedFactJournalSnapshot journal = start == requestedCursor
                ? initial : CommittedFactJournal.ReadAfter(state, start);
            CommittedFactEnvelopeContext core = CommittedFactJournal.Context(state);
            var context = new TelemetryContext(supplement.SessionId, core.RunId, core.BuildVersion,
                core.RulesetHash, core.ContentVersion, core.Mode, core.Difficulty, core.Night, core.Phase);
            var results = new List<TelemetryProjectionResult>();
            ulong acknowledged = start;

            foreach (CommittedFactRecord record in journal.Records)
            {
                if (record.Kind == CommittedFactKind.Damage)
                {
                    results.Add(projector.ProjectDamageApplied(context, record.Damage));
                }
                else if (record.Kind == CommittedFactKind.ActorStateTransition)
                {
                    TelemetryProjectionResult stateResult = projector.ProjectActorStateChanged(context, record.ActorTransition);
                    results.Add(stateResult);
                    if (record.ActorTransition.CurrentLifeState == ActorLifeStateCode.PlayerDead ||
                        record.ActorTransition.CurrentLifeState == ActorLifeStateCode.PartnerDead)
                        results.Add(projector.ProjectActorDeathCommitted(context, record.ActorTransition));
                }
                else
                {
                    throw new InvalidOperationException("Unknown committed Core fact kind.");
                }

                // A Sink cannot control gameplay or cause an authoritative fact to be replayed forever.
                // Core owns and persists this observability-only acknowledgement.
                CommittedFactJournal.AcknowledgeThrough(state, record.Cursor);
                acknowledged = record.Cursor;
            }

            return new CommittedFactProjectionBatchResult
            {
                StartCursor = start,
                AcknowledgedCursor = acknowledged,
                CoreFactCount = journal.Records.Length,
                ProjectionResults = results.ToArray(),
                IsPreMilestoneUnavailable = core.IsPreMilestoneUnavailable,
                IsStoredInRunState = core.IsStoredInRunState
            };
        }
    }
}
