using System;
using LastWatch.Core.Configuration;
using LastWatch.Core.State;
using LastWatch.Core.Terminal;

namespace LastWatch.Core.Persistence
{
    // Validation is isolated from JSON mapping; accepted schema fields and rules are unchanged.
    public static partial class RunStateJson
    {
        private static void ValidateV1(RunStateV1Dto d)
        {
            if (d.schemaVersion != 1 || (int)d.phase < 0 || (int)d.phase > 7 || !Enum.IsDefined(typeof(SourceState), d.source))
                throw new FormatException("Invalid schema-v1 enum value.");
            Common(d.runId, d.buildVersion, d.configurationVersion, d.fixedTick, d.nextEventSequence, d.phase,
                d.playerLife, d.partnerLife, d.tower, d.playerZone, d.playerTraversal, d.playerDeath, d.partnerDeath,
                d.spawnRng, d.evasionRng, d.partnerCombatRng, d.lootRng, d.cosmeticRng, d.damageables, d.appliedDamageEvents);
        }

        private static void ValidateV2(RunState s)
        {
            if (!Enum.IsDefined(typeof(RunPhase), s.Phase) || !Enum.IsDefined(typeof(WorldSourceState), s.WorldSource) ||
                !Enum.IsDefined(typeof(SourceAccessState), s.SourceAccess) || !Enum.IsDefined(typeof(SourceOverloadCommitOrigin), s.SourceOverloadCommitOrigin) ||
                !Enum.IsDefined(typeof(PlayerLifeState), s.PlayerLife) || !Enum.IsDefined(typeof(PartnerLifeState), s.PartnerLife) ||
                !Enum.IsDefined(typeof(TowerState), s.Tower) || !Enum.IsDefined(typeof(LocationZone), s.PlayerZone) ||
                !Enum.IsDefined(typeof(TraversalState), s.PlayerTraversal))
                throw new FormatException("Unknown authoritative enum value.");
            if ((s.PlayerLife == PlayerLifeState.Dead) != s.PlayerDeath.HasValue ||
                (s.PartnerLife == PartnerLifeState.Dead) != s.PartnerDeath.HasValue) throw new FormatException("Life state/fact mismatch.");
            if (s.ConfirmedPartnerExecution != s.ConfirmedPartnerExecutionTick.HasValue ||
                s.ConfirmedPartnerExecution != s.ConfirmedPartnerExecutionSequence.HasValue)
                throw new FormatException("Confirmed execution fact is incomplete.");
            if (s.SourceOverloadCommitTick.HasValue && s.SourceOverloadCommitOrigin == SourceOverloadCommitOrigin.None)
                throw new FormatException("Overload commit provenance is missing.");
            if (s.WorldSource == WorldSourceState.Destroyed && !s.SourceDestroyedTick.HasValue)
                throw new FormatException("Destroyed Source requires a destruction tick.");
            TerminalStateSnapshot t = s.Terminal?.Snapshot() ?? throw new FormatException("Missing Terminal state.");
            if (!Enum.IsDefined(typeof(TerminalLifecycleState), t.ProgressState))
                throw new FormatException("Unknown Terminal lifecycle state.");
            if (t.ProgressState == TerminalLifecycleState.None && t.EntryKey.HasValue) throw new FormatException("Terminal None cannot own a queue.");
            if (t.ProgressState != TerminalLifecycleState.None && (!t.EntryKey.HasValue || !t.Seed.HasValue))
                throw new FormatException("Prepared Terminal state requires key and seed.");
            if (t.EntryKey.HasValue && t.EntryKey.Value.RunId != s.RunId) throw new FormatException("Terminal RunId mismatch.");
            foreach (TerminalTicketSnapshot ticket in t.Tickets)
                if (!Enum.IsDefined(typeof(TerminalTicketLifecycle), ticket.Lifecycle))
                    throw new FormatException("Unknown Terminal ticket lifecycle.");
            if (s.Outcome.HasValue && (!Enum.IsDefined(typeof(PersonalEndingId), s.Outcome.Value.Value.Personal) ||
                !Enum.IsDefined(typeof(WorldOutcome), s.Outcome.Value.Value.World) ||
                !Enum.IsDefined(typeof(EndingCategory), s.Outcome.Value.Value.PrimaryCategory)))
                throw new FormatException("Unknown outcome enum value.");
            if (s.CommittedFactAcknowledgedCursor >= s.NextCommittedFactCursor)
                throw new FormatException("Committed fact acknowledgement cursor exceeds journal.");
            if (s.RunContext != null && s.RunContext.IsPreMilestoneUnavailable)
                _ = RunContextDefinition.Restore(s.RunContext.RulesetHash, s.RunContext.ContentVersion,
                    s.RunContext.ModeToken, s.RunContext.DifficultyToken, s.RunContext.Night);
        }
    }
}
