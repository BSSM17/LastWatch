using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;

namespace LastWatch.Core.State
{
    // Run-scoped identity and the immutable context exposed when a Night begins.
    public sealed class M4RunMetadata
    {
        public RunId RunId { get; }
        public ulong RunSeed { get; }
        public LastWatchDifficulty Difficulty { get; }
        public string RulesetHash => M4BridgeProfile.RulesetHash;
        public string ContentVersion => M4BridgeProfile.ContentVersion;
        public string ProfileId => M4BridgeProfile.Id;
        public M4RunMetadata(RunId runId, ulong runSeed, LastWatchDifficulty difficulty)
        {
            if (!runId.IsValid || runSeed != M4ThreeNightHarness.EvidenceRunSeed || !Enum.IsDefined(typeof(LastWatchDifficulty), difficulty))
                throw new ArgumentException("M4 requires an explicit RunId, seed 1234 and Normal/Hard.");
            RunId = runId; RunSeed = runSeed; Difficulty = difficulty;
        }
        public EnemyDirectorRequest Request(int ordinal) => new EnemyDirectorRequest(RunSeed, Difficulty, new EnterDefenseKey(RunId, M4GameMode.Story, ordinal).NightOrdinal, M4BridgeProfile.RulesetVersion);
    }

    public sealed class M4EntryEventContext
    {
        public M4RunMetadata Metadata { get; }
        public EnterDefenseKey Key { get; }
        public string EntrySnapshotHash { get; }
        public int NightStartFixedTick { get; }
        internal M4EntryEventContext(M4RunMetadata metadata, DefenseEntrySnapshot entry, int start)
        { Metadata = metadata; Key = entry.Key; EntrySnapshotHash = entry.Digest; NightStartFixedTick = start; }
        public int ToRunTick(int nightTick) { if (nightTick < 0) throw new ArgumentOutOfRangeException(nameof(nightTick)); return checked(NightStartFixedTick + nightTick); }
    }
}
