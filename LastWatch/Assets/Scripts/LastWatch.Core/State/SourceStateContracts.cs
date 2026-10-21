using System;

namespace LastWatch.Core.State
{
    public enum WorldSourceState { Active = 0, Destroyed = 1 }
    public enum SourceAccessState { Dormant = 0, Manifested = 1, OverloadCommitted = 2, Sealed = 3 }
    public enum SourceOverloadCommitOrigin { None = 0, RuntimeEvent = 1, LegacyV1Migration = 2 }

    public sealed class SourceStateSnapshot
    {
        public WorldSourceState WorldState { get; private set; }
        public SourceAccessState AccessState { get; private set; }
        public int? ManifestedTick { get; private set; }
        public int? SealedTick { get; private set; }
        public ulong? SealSourceEventId { get; private set; }
        public int? OverloadCommitTick { get; private set; }
        public ulong? OverloadCommitSequence { get; private set; }
        public int? OverloadCompletionTick { get; private set; }
        public SourceOverloadCommitOrigin OverloadCommitOrigin { get; private set; }
        public int? DestroyedTick { get; private set; }
        public ulong? DestroySequence { get; private set; }
        public bool ConfirmedPartnerExecution { get; private set; }
        public int? ConfirmedPartnerExecutionTick { get; private set; }
        public ulong? ConfirmedPartnerExecutionSequence { get; private set; }

        internal static SourceStateSnapshot Create(RunState state) => new SourceStateSnapshot
        {
            WorldState = state.WorldSource,
            AccessState = state.SourceAccess,
            ManifestedTick = state.SourceManifestedTick,
            SealedTick = state.SourceSealedTick,
            SealSourceEventId = state.SourceSealSourceEventId,
            OverloadCommitTick = state.SourceOverloadCommitTick,
            OverloadCommitSequence = state.SourceOverloadCommitSequence,
            OverloadCompletionTick = state.SourceOverloadCompletionTick,
            OverloadCommitOrigin = state.SourceOverloadCommitOrigin,
            DestroyedTick = state.SourceDestroyedTick,
            DestroySequence = state.SourceDestroySequence,
            ConfirmedPartnerExecution = state.ConfirmedPartnerExecution,
            ConfirmedPartnerExecutionTick = state.ConfirmedPartnerExecutionTick,
            ConfirmedPartnerExecutionSequence = state.ConfirmedPartnerExecutionSequence
        };
    }
}
