using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;
using LastWatch.Core.Simulation;
using LastWatch.Core.Terminal;

namespace LastWatch.Core.State
{
    [Serializable]
    public sealed class DamageableFixture
    {
        public ulong EntityValue;
        public DamageableKind Kind;
        public float HitPoints;
        public bool IsResolved;
    }

    [Serializable]
    public sealed class RunStateFixture
    {
        public ulong RunValue = 1;
        public string BuildVersion = "0.1.0-m1";
        public string ConfigurationVersion = "3.0";
        public int FixedTick;
        public ulong NextEventSequence = 1;
        public TowerState Tower = TowerState.Stable;
        public LocationZone PlayerZone = LocationZone.UpperSafeZone;
        public TraversalState PlayerTraversal = TraversalState.None;
        public DamageableFixture[] Damageables = Array.Empty<DamageableFixture>();
        public TerminalStateFixture Terminal;
    }

    [Serializable]
    public sealed class TerminalTicketFixture
    {
        public ulong TicketId;
        public int QueueIndex;
        public string EnemyArchetypeId;
        public int OriginalScheduledTick;
        public string InitialRouteChoiceKey;
        public int RulesetVersion;
        public TerminalTicketLifecycle Lifecycle;
        public ulong ActiveEntityValue;
    }

    [Serializable]
    public sealed class TerminalStateFixture
    {
        public TerminalLifecycleState ProgressState;
        public int RulesetVersion;
        public ulong Seed;
        public int? EnteredTick;
        public int? GroundContinuationTick;
        public int? ClearedTick;
        public int? NextTicketIndex;
        public TerminalTicketFixture[] Tickets = Array.Empty<TerminalTicketFixture>();
    }

    public static class RunStateFactory
    {
        public static RunState CreateNew(RunId runId, string buildVersion, string configurationVersion, ulong seed)
        {
            var state = new RunState(runId, buildVersion, configurationVersion, 0, 1)
            { RunContext = RunContextDefinition.PreMilestoneUnavailable };
            SetRng(state, seed);
            return state;
        }

        public static RunState CreateFixture(RunStateFixture fixture)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            var state = new RunState(new RunId(fixture.RunValue), fixture.BuildVersion, fixture.ConfigurationVersion,
                fixture.FixedTick, fixture.NextEventSequence);
            SetRng(state, fixture.RunValue);
            state.Tower = fixture.Tower;
            state.PlayerZone = fixture.PlayerZone;
            state.PlayerTraversal = fixture.PlayerTraversal;
            foreach (DamageableFixture item in fixture.Damageables ?? Array.Empty<DamageableFixture>())
                state.AddDamageable(new EntityId(item.EntityValue), item.Kind, item.HitPoints, item.IsResolved);
            if (fixture.Terminal != null) state.Terminal = CreateTerminalFixture(state.RunId, fixture.Terminal);
            return state;
        }

        // Explicit test/setup-only seam. It does not generate tickets or choose gameplay values.
        public static TerminalStateSnapshot PreserveTerminalForCollapseFixture(RunState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Terminal.PreserveForCollapse();
            return state.Snapshot().Terminal;
        }

        private static TerminalState CreateTerminalFixture(RunId runId, TerminalStateFixture fixture)
        {
            if (fixture.ProgressState == TerminalLifecycleState.None)
            {
                if ((fixture.Tickets?.Length ?? 0) != 0) throw new ArgumentException("Terminal None fixture cannot contain tickets.");
                return TerminalState.Empty;
            }
            var state = TerminalState.Prepare(new TerminalEntryKey(runId, fixture.RulesetVersion), fixture.Seed,
                Array.Empty<TerminalTicket>());
            state.ProgressState = fixture.ProgressState;
            state.EnteredTick = fixture.EnteredTick;
            state.GroundContinuationTick = fixture.GroundContinuationTick;
            state.ClearedTick = fixture.ClearedTick;
            state.NextTicketIndex = fixture.NextTicketIndex;
            foreach (TerminalTicketFixture item in fixture.Tickets ?? Array.Empty<TerminalTicketFixture>())
            {
                if (item == null) throw new ArgumentException("Terminal ticket fixture is missing.");
                var ticket = new TerminalTicket(item.TicketId, item.QueueIndex, item.EnemyArchetypeId,
                    item.OriginalScheduledTick, item.InitialRouteChoiceKey, item.RulesetVersion);
                state.RestoreTicket(ticket, item.Lifecycle,
                    item.ActiveEntityValue == 0 ? (EntityId?)null : new EntityId(item.ActiveEntityValue));
            }
            return state;
        }

        private static void SetRng(RunState state, ulong seed)
        {
            if (seed == 0) seed = 0x9e3779b97f4a7c15UL;
            state.SpawnRng = Stream(seed, 1); state.EvasionRng = Stream(seed, 2);
            state.PartnerCombatRng = Stream(seed, 3); state.LootRng = Stream(seed, 4); state.CosmeticRng = Stream(seed, 5);
        }

        private static RngStreamState Stream(ulong seed, ulong stream)
        {
            ulong a = Mix(seed + stream * 0x9e3779b97f4a7c15UL);
            ulong b = Mix(a + 0x9e3779b97f4a7c15UL);
            return new RngStreamState(a, b);
        }

        private static ulong Mix(ulong value)
        {
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }
    }
}
