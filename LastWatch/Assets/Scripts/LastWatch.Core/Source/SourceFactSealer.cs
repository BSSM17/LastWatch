using System;
using LastWatch.Core.State;

namespace LastWatch.Core.Source
{
    public readonly struct SourceTickFactBatch
    {
        public int FixedTick { get; }
        public ulong? OverloadCommitSequence { get; }
        public ulong? ConfirmedPartnerExecutionSequence { get; }
        public ulong? OverloadCompletionSequence { get; }

        public SourceTickFactBatch(int fixedTick, ulong? overloadCommitSequence,
            ulong? confirmedPartnerExecutionSequence, ulong? overloadCompletionSequence)
        {
            if (fixedTick < 0) throw new ArgumentOutOfRangeException(nameof(fixedTick));
            ValidateSequence(overloadCommitSequence, nameof(overloadCommitSequence));
            ValidateSequence(confirmedPartnerExecutionSequence, nameof(confirmedPartnerExecutionSequence));
            ValidateSequence(overloadCompletionSequence, nameof(overloadCompletionSequence));
            FixedTick = fixedTick;
            OverloadCommitSequence = overloadCommitSequence;
            ConfirmedPartnerExecutionSequence = confirmedPartnerExecutionSequence;
            OverloadCompletionSequence = overloadCompletionSequence;
        }

        private static void ValidateSequence(ulong? value, string name)
        {
            if (value.HasValue && value.Value == 0) throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct SourceFactSealResult
    {
        public bool OverloadCommitted { get; }
        public bool OverloadRejectedByPriorSeal { get; }
        public bool ExecutionCommitted { get; }
        public bool SourceDestroyed { get; }

        internal SourceFactSealResult(bool overloadCommitted, bool overloadRejectedByPriorSeal,
            bool executionCommitted, bool sourceDestroyed)
        {
            OverloadCommitted = overloadCommitted;
            OverloadRejectedByPriorSeal = overloadRejectedByPriorSeal;
            ExecutionCommitted = executionCommitted;
            SourceDestroyed = sourceDestroyed;
        }
    }

    public static class SourceFactSealer
    {
        public static SourceFactSealResult Seal(RunState state, in SourceTickFactBatch batch)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (batch.FixedTick != state.FixedTick) throw new InvalidOperationException("Fact batch tick must match RunState.FixedTick.");

            bool destroyedAtStart = state.WorldSource == WorldSourceState.Destroyed;
            bool sealedBeforeTick = state.SourceAccess == SourceAccessState.Sealed &&
                (!state.SourceSealedTick.HasValue || state.SourceSealedTick.Value < batch.FixedTick);
            bool alreadyOverloadCommitted = state.SourceOverloadCommitTick.HasValue;
            bool overloadCommitted = false;
            bool overloadRejected = false;
            bool executionCommitted = false;

            if (batch.OverloadCommitSequence.HasValue && !alreadyOverloadCommitted)
            {
                if (destroyedAtStart || sealedBeforeTick)
                {
                    overloadRejected = true;
                }
                else
                {
                    state.SourceOverloadCommitTick = batch.FixedTick;
                    state.SourceOverloadCommitSequence = batch.OverloadCommitSequence;
                    state.SourceOverloadCommitOrigin = SourceOverloadCommitOrigin.RuntimeEvent;
                    state.SourceAccess = SourceAccessState.OverloadCommitted;
                    overloadCommitted = true;
                }
            }

            if (batch.ConfirmedPartnerExecutionSequence.HasValue && !state.ConfirmedPartnerExecution)
            {
                state.ConfirmedPartnerExecution = true;
                state.ConfirmedPartnerExecutionTick = batch.FixedTick;
                state.ConfirmedPartnerExecutionSequence = batch.ConfirmedPartnerExecutionSequence;
                state.SourceSealedTick = batch.FixedTick;
                state.SourceSealSourceEventId = batch.ConfirmedPartnerExecutionSequence;
                state.SourceAccess = SourceAccessState.Sealed;
                executionCommitted = true;
            }

            if (batch.OverloadCompletionSequence.HasValue && state.SourceOverloadCommitTick.HasValue &&
                state.WorldSource != WorldSourceState.Destroyed)
            {
                state.SourceOverloadCompletionTick = batch.FixedTick;
                state.WorldSource = WorldSourceState.Destroyed;
                state.SourceDestroyedTick = batch.FixedTick;
                state.SourceDestroySequence = batch.OverloadCompletionSequence;
            }

            return new SourceFactSealResult(overloadCommitted, overloadRejected, executionCommitted,
                state.WorldSource == WorldSourceState.Destroyed);
        }
    }
}
