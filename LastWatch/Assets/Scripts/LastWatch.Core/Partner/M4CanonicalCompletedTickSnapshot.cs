namespace LastWatch.Core.Partner
{
    // Detached historical values; availability is current only at getter return on the
    // Unity main thread. Run identity is canonical Metadata.RunId, not CaptureRunId.
    public sealed class M4CanonicalCompletedTickSnapshot
    {
        public bool CompletedTickAvailable { get; }
        public string UnavailableReason { get; }
        public string RunId { get; }
        public ulong ContextGeneration { get; }
        public ulong CompletionAttempt { get; }
        // Processor input ticks, matching the controller's successful processor stamp.
        public int RunFixedTick { get; }
        public int NightFixedTick { get; }
        public int NightOrdinal { get; }
        public int AdvancedRunFixedTick { get; }
        public int AdvancedNightFixedTick { get; }
        public bool NightCompleted { get; }

        internal M4CanonicalCompletedTickSnapshot(bool available, string reason, string runId,
            ulong generation, ulong attempt, int runTick, int nightTick, int ordinal,
            int advancedRunTick, int advancedNightTick, bool nightCompleted)
        {
            CompletedTickAvailable = available;
            UnavailableReason = reason;
            RunId = runId;
            ContextGeneration = generation;
            CompletionAttempt = attempt;
            RunFixedTick = runTick;
            NightFixedTick = nightTick;
            NightOrdinal = ordinal;
            AdvancedRunFixedTick = advancedRunTick;
            AdvancedNightFixedTick = advancedNightTick;
            NightCompleted = nightCompleted;
        }
    }
}
