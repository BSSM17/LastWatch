using System;

namespace LastWatch.Core.State
{
    public enum PersonalEndingId
    {
        Unresolved = 0,
        TowerFall = 1,
        GroundDeath = 2,
        BothDevoured = 3,
        CollapseEscape = 4,
        SurvivedAfterPartnerKill = 5,
        StorySurvived = 6,
        SourceMissionSurvived = 7,
        TerminalOverrunSurvived = 8
    }

    public enum WorldOutcome { SourceActive = 0, SourceDestroyed = 1 }
    public enum EndingCategory { Bad = 0, Normal = 1, Hidden = 2 }
    public enum OutcomeCommitStatus { Committed = 0, AlreadyCommittedSame = 1, RejectedConflict = 2 }

    public readonly struct RunOutcome : IEquatable<RunOutcome>
    {
        public PersonalEndingId Personal { get; }
        public WorldOutcome World { get; }
        public EndingCategory PrimaryCategory { get; }
        public bool SecretEnding { get; }

        public RunOutcome(PersonalEndingId personal, WorldOutcome world, EndingCategory primaryCategory, bool secretEnding)
        {
            if (personal == PersonalEndingId.Unresolved) throw new ArgumentException("A committed outcome cannot be unresolved.", nameof(personal));
            Personal = personal;
            World = world;
            PrimaryCategory = primaryCategory;
            SecretEnding = secretEnding;
        }

        public bool Equals(RunOutcome other) => Personal == other.Personal && World == other.World &&
            PrimaryCategory == other.PrimaryCategory && SecretEnding == other.SecretEnding;
        public override bool Equals(object obj) => obj is RunOutcome other && Equals(other);
        public override int GetHashCode() => (((int)Personal * 397) ^ (int)World) * 397 ^ (int)PrimaryCategory ^ (SecretEnding ? 1 : 0);
    }

    public readonly struct CommittedOutcome : IEquatable<CommittedOutcome>
    {
        public ulong PendingOutcomeId { get; }
        public RunOutcome Value { get; }
        public CommittedOutcome(ulong pendingOutcomeId, RunOutcome value)
        {
            if (pendingOutcomeId == 0) throw new ArgumentOutOfRangeException(nameof(pendingOutcomeId));
            PendingOutcomeId = pendingOutcomeId;
            Value = value;
        }
        public bool Equals(CommittedOutcome other) => PendingOutcomeId == other.PendingOutcomeId && Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is CommittedOutcome other && Equals(other);
        public override int GetHashCode() => (PendingOutcomeId.GetHashCode() * 397) ^ Value.GetHashCode();
    }

    public static class OutcomeCommitContract
    {
        public static OutcomeCommitStatus TryCommit(RunState state, ulong pendingOutcomeId, RunOutcome outcome)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var candidate = new CommittedOutcome(pendingOutcomeId, outcome);
            if (!state.Outcome.HasValue)
            {
                state.Outcome = candidate;
                return OutcomeCommitStatus.Committed;
            }
            return state.Outcome.Value.Equals(candidate)
                ? OutcomeCommitStatus.AlreadyCommittedSame
                : OutcomeCommitStatus.RejectedConflict;
        }
    }

    public static class EndingPresentationKeys
    {
        public const string TerminalOverrunSurvived = "ending.secret_bad.unending_watch";

        public static string For(PersonalEndingId ending) =>
            ending == PersonalEndingId.TerminalOverrunSurvived ? TerminalOverrunSurvived : null;
    }
}
