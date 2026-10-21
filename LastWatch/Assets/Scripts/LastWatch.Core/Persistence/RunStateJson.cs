using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;
using LastWatch.Core.Simulation;
using LastWatch.Core.State;
using LastWatch.Core.Terminal;
using UnityEngine;

namespace LastWatch.Core.Persistence
{
    public static partial class RunStateJson
    {
        public static string Serialize(RunState state, bool prettyPrint = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.RunContext != null && !state.RunContext.IsPreMilestoneUnavailable)
                throw new NotSupportedException("Canonical M4 metadata requires M4SessionJson; legacy schema-v2 cannot resume it.");
            ValidateV2(state);
            return JsonUtility.ToJson(ToV2(state), prettyPrint);
        }

        public static RunState Deserialize(string json)
        {
            RunStateLoadResult result = TryDeserialize(json);
            if (result.IsLoaded) return result.State;
            if (result.Status == RunStateLoadStatus.UnsupportedFutureSchema) throw new NotSupportedException(result.IsolationReason);
            throw new FormatException(result.IsolationReason);
        }

        public static RunStateLoadResult TryDeserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Isolated(RunStateLoadStatus.CorruptOrInvalid, "RunState JSON is missing.");
            try
            {
                SchemaHeaderDto header = JsonUtility.FromJson<SchemaHeaderDto>(json);
                if (header == null || header.schemaVersion <= 0)
                    return Isolated(RunStateLoadStatus.CorruptOrInvalid, "RunState schemaVersion is missing or invalid.");
                if (header.schemaVersion > RunState.SchemaVersion)
                    return Isolated(RunStateLoadStatus.UnsupportedFutureSchema, $"Unsupported future RunState schema {header.schemaVersion}.");
                if (header.schemaVersion == 1)
                {
                    RunStateV1Dto v1 = JsonUtility.FromJson<RunStateV1Dto>(json) ?? throw new FormatException("Invalid schema-v1 JSON.");
                    ValidateV1(v1);
                    RunState migrated = MigrateV1(v1); ValidateV2(migrated);
                    return new RunStateLoadResult { Status = RunStateLoadStatus.MigratedV1, State = migrated };
                }
                RunStateV2Dto v2 = JsonUtility.FromJson<RunStateV2Dto>(json) ?? throw new FormatException("Invalid schema-v2 JSON.");
                RunState loaded = FromV2(v2); ValidateV2(loaded);
                return new RunStateLoadResult { Status = RunStateLoadStatus.LoadedV2, State = loaded };
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            { return Isolated(RunStateLoadStatus.CorruptOrInvalid, error.Message); }
        }

        public static string CanonicalHash(RunState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ValidateV2(state);
            RunStateV2Dto d = ToV2(state); var b = new StringBuilder();
            b.Append(d.schemaVersion).Append('|'); Text(b, d.buildVersion); Text(b, d.configurationVersion);
            b.Append(d.runId).Append('|').Append(d.fixedTick).Append('|').Append(d.nextEventSequence)
                .Append('|').Append((int)d.phase).Append('|').Append((int)d.playerLife).Append('|').Append((int)d.partnerLife)
                .Append('|').Append((int)d.tower).Append('|').Append((int)d.playerZone).Append('|').Append((int)d.playerTraversal);
            Death(b, d.playerDeath); Death(b, d.partnerDeath); Rng(b, d.spawnRng); Rng(b, d.evasionRng);
            Rng(b, d.partnerCombatRng); Rng(b, d.lootRng); Rng(b, d.cosmeticRng);
            foreach (DamageableDto x in d.damageables) b.Append('|').Append(x.entityId).Append(':').Append((int)x.kind)
                .Append(':').Append(x.hitPoints.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(x.isResolved ? '1' : '0');
            foreach (string x in d.appliedDamageEvents) b.Append('|').Append(x);
            b.Append('|').Append((int)d.worldSource).Append('|').Append((int)d.sourceAccess);
            Opt(b, d.sourceManifestedTick); Opt(b, d.sourceSealedTick); Opt(b, d.sourceSealSourceEventId);
            Opt(b, d.sourceOverloadCommitTick); Opt(b, d.sourceOverloadCommitSequence); Opt(b, d.sourceOverloadCompletionTick);
            b.Append('|').Append((int)d.sourceOverloadCommitOrigin); Opt(b, d.sourceDestroyedTick); Opt(b, d.sourceDestroySequence);
            b.Append('|').Append(d.confirmedPartnerExecution ? '1' : '0'); Opt(b, d.confirmedPartnerExecutionTick); Opt(b, d.confirmedPartnerExecutionSequence);
            TerminalStateSnapshot terminal = state.Terminal.Snapshot();
            b.Append('|').Append((int)terminal.ProgressState); NullableText(b, terminal.QueueHash);
            NullableInt(b, terminal.EnteredTick); NullableInt(b, terminal.GroundContinuationTick);
            NullableInt(b, terminal.ClearedTick); NullableInt(b, terminal.NextTicketIndex);
            foreach (TerminalTicketSnapshot x in terminal.Tickets)
            {
                b.Append('|').Append(x.Ticket.QueueIndex).Append(':').Append((int)x.Lifecycle);
                NullableUlong(b, x.ActiveEntityId.HasValue ? x.ActiveEntityId.Value.Value : (ulong?)null);
            }
            if (state.Outcome.HasValue)
            {
                CommittedOutcome x = state.Outcome.Value;
                b.Append("|1:").Append(Hex(x.PendingOutcomeId)).Append(':').Append((int)x.Value.Personal)
                    .Append(':').Append((int)x.Value.World).Append(':').Append((int)x.Value.PrimaryCategory)
                    .Append(':').Append(x.Value.SecretEnding ? '1' : '0');
            }
            else b.Append("|0");
            if (state.RunContext != null)
            {
                b.Append("|CTX|"); Text(b, state.RunContext.RulesetHash); Text(b, state.RunContext.ContentVersion);
                Text(b, state.RunContext.ModeToken); Text(b, state.RunContext.DifficultyToken);
                NullableInt(b, state.RunContext.Night);
            }
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static RunState MigrateV1(RunStateV1Dto d)
        {
            RunState s = Common(d.runId, d.buildVersion, d.configurationVersion, d.fixedTick, d.nextEventSequence,
                d.phase, d.playerLife, d.partnerLife, d.tower, d.playerZone, d.playerTraversal, d.playerDeath,
                d.partnerDeath, d.spawnRng, d.evasionRng, d.partnerCombatRng, d.lootRng, d.cosmeticRng,
                d.damageables, d.appliedDamageEvents);
            s.ApplyLegacySource(d.source);
            if (d.source == SourceState.Located) s.SourceManifestedTick = d.fixedTick;
            if (d.source == SourceState.Armed)
            { s.SourceOverloadCommitTick = d.fixedTick; s.SourceOverloadCommitOrigin = SourceOverloadCommitOrigin.LegacyV1Migration; }
            else if (d.source == SourceState.Destroyed)
            { s.SourceDestroyedTick = d.fixedTick; s.SourceOverloadCommitOrigin = SourceOverloadCommitOrigin.LegacyV1Migration; }
            s.Terminal = TerminalState.Empty; return s;
        }

        internal static RunState RestoreM4Payload(RunStateV2Dto d, M4RunMetadata metadata)
        {
            if (d == null || d.schemaVersion != 3 || !d.hasRunContext || d.rulesetHash != metadata.RulesetHash ||
                d.contentVersion != metadata.ContentVersion || d.gameMode != "Story" ||
                d.difficulty != metadata.Difficulty.ToString() || d.night == null || d.night.hasValue)
                throw new FormatException("M4 run metadata mismatch.");
            // The DTO is owned by the detached decoder. The legacy reader remains sentinel-only.
            d.schemaVersion = 2;
            RunState state = FromV2(d, RunContextDefinition.ForM4(metadata));
            d.schemaVersion = 3;
            ValidateV2(state);
            return state;
        }

        internal static RunStateV2Dto CaptureM4Payload(RunState state)
        {
            ValidateV2(state);
            RunStateV2Dto dto = ToV2(state); dto.schemaVersion = 3; return dto;
        }

        private static RunState FromV2(RunStateV2Dto d, RunContextDefinition boundedContext = null)
        {
            if (d.schemaVersion != 2) throw new FormatException("Expected schema-v2 RunState.");
            RunState s = Common(d.runId, d.buildVersion, d.configurationVersion, d.fixedTick, d.nextEventSequence,
                d.phase, d.playerLife, d.partnerLife, d.tower, d.playerZone, d.playerTraversal, d.playerDeath,
                d.partnerDeath, d.spawnRng, d.evasionRng, d.partnerCombatRng, d.lootRng, d.cosmeticRng,
                d.damageables, d.appliedDamageEvents);
            s.WorldSource = d.worldSource; s.SourceAccess = d.sourceAccess;
            s.SourceManifestedTick = I(d.sourceManifestedTick); s.SourceSealedTick = I(d.sourceSealedTick);
            s.SourceSealSourceEventId = U(d.sourceSealSourceEventId); s.SourceOverloadCommitTick = I(d.sourceOverloadCommitTick);
            s.SourceOverloadCommitSequence = U(d.sourceOverloadCommitSequence); s.SourceOverloadCompletionTick = I(d.sourceOverloadCompletionTick);
            s.SourceOverloadCommitOrigin = d.sourceOverloadCommitOrigin; s.SourceDestroyedTick = I(d.sourceDestroyedTick);
            s.SourceDestroySequence = U(d.sourceDestroySequence); s.ConfirmedPartnerExecution = d.confirmedPartnerExecution;
            s.ConfirmedPartnerExecutionTick = I(d.confirmedPartnerExecutionTick);
            s.ConfirmedPartnerExecutionSequence = U(d.confirmedPartnerExecutionSequence);
            s.Terminal = FromTerminal(d.terminal); s.Outcome = FromOutcome(d.outcome);
            if (d.hasRunContext)
                s.RunContext = boundedContext ?? RunContextDefinition.Restore(d.rulesetHash, d.contentVersion, d.gameMode, d.difficulty, I(d.night));
            RestoreFacts(s, d.committedFacts, d.committedFactAcknowledgedCursor); return s;
        }

        private static RunState Common(string runId, string build, string config, int tick, string next,
            RunPhase phase, PlayerLifeState playerLife, PartnerLifeState partnerLife, TowerState tower,
            LocationZone zone, TraversalState traversal, DeathDto playerDeath, DeathDto partnerDeath,
            RngDto spawn, RngDto evasion, RngDto partner, RngDto loot, RngDto cosmetic,
            DamageableDto[] damageables, string[] applied)
        {
            var s = new RunState(RunId.Parse(runId), build, config, tick, ParseHex(next));
            s.Phase = phase; s.PlayerLife = playerLife; s.PartnerLife = partnerLife; s.Tower = tower;
            s.PlayerZone = zone; s.PlayerTraversal = traversal; s.PlayerDeath = FromDeath(playerDeath); s.PartnerDeath = FromDeath(partnerDeath);
            s.SpawnRng = FromRng(spawn); s.EvasionRng = FromRng(evasion); s.PartnerCombatRng = FromRng(partner);
            s.LootRng = FromRng(loot); s.CosmeticRng = FromRng(cosmetic);
            foreach (DamageableDto x in damageables ?? Array.Empty<DamageableDto>())
                s.AddDamageable(EntityId.Parse(x.entityId), x.kind, x.hitPoints, x.isResolved);
            foreach (string x in applied ?? Array.Empty<string>()) s.RestoreAppliedDamage(ParseHex(x));
            return s;
        }

        private static RunStateV2Dto ToV2(RunState s) => new RunStateV2Dto
        {
            schemaVersion = 2, buildVersion = s.BuildVersion, configurationVersion = s.ConfigurationVersion,
            runId = s.RunId.ToString(), fixedTick = s.FixedTick, nextEventSequence = Hex(s.NextEventSequence),
            phase = s.Phase, playerLife = s.PlayerLife, partnerLife = s.PartnerLife, tower = s.Tower,
            playerZone = s.PlayerZone, playerTraversal = s.PlayerTraversal, playerDeath = ToDeath(s.PlayerDeath), partnerDeath = ToDeath(s.PartnerDeath),
            spawnRng = ToRng(s.SpawnRng), evasionRng = ToRng(s.EvasionRng), partnerCombatRng = ToRng(s.PartnerCombatRng),
            lootRng = ToRng(s.LootRng), cosmeticRng = ToRng(s.CosmeticRng), damageables = Damageables(s), appliedDamageEvents = Applied(s),
            worldSource = s.WorldSource, sourceAccess = s.SourceAccess, sourceManifestedTick = O(s.SourceManifestedTick),
            sourceSealedTick = O(s.SourceSealedTick), sourceSealSourceEventId = O(s.SourceSealSourceEventId),
            sourceOverloadCommitTick = O(s.SourceOverloadCommitTick), sourceOverloadCommitSequence = O(s.SourceOverloadCommitSequence),
            sourceOverloadCompletionTick = O(s.SourceOverloadCompletionTick), sourceOverloadCommitOrigin = s.SourceOverloadCommitOrigin,
            sourceDestroyedTick = O(s.SourceDestroyedTick), sourceDestroySequence = O(s.SourceDestroySequence),
            confirmedPartnerExecution = s.ConfirmedPartnerExecution, confirmedPartnerExecutionTick = O(s.ConfirmedPartnerExecutionTick),
            confirmedPartnerExecutionSequence = O(s.ConfirmedPartnerExecutionSequence), terminal = ToTerminal(s.Terminal), outcome = ToOutcome(s.Outcome),
            committedFactAcknowledgedCursor = Hex(s.CommittedFactAcknowledgedCursor), committedFacts = ToFacts(s),
            hasRunContext = s.RunContext != null,
            rulesetHash = s.RunContext?.RulesetHash, contentVersion = s.RunContext?.ContentVersion,
            gameMode = s.RunContext?.ModeToken,
            difficulty = s.RunContext?.DifficultyToken,
            night = O(s.RunContext?.Night)
        };

        private static TerminalStateDto ToTerminal(TerminalState s)
        {
            TerminalStateSnapshot x = s.Snapshot(); var tickets = new TerminalTicketDto[x.Tickets.Length];
            for (int n = 0; n < tickets.Length; n++)
            {
                TerminalTicketSnapshot t = x.Tickets[n];
                tickets[n] = new TerminalTicketDto { ticketId = Hex(t.Ticket.TicketId), queueIndex = t.Ticket.QueueIndex,
                    enemyArchetypeId = t.Ticket.EnemyArchetypeId, originalScheduledTick = t.Ticket.OriginalScheduledTick,
                    initialRouteChoiceKey = t.Ticket.InitialRouteChoiceKey, rulesetVersion = t.Ticket.RulesetVersion,
                    lifecycle = t.Lifecycle, activeEntityId = O(t.ActiveEntityId.HasValue ? t.ActiveEntityId.Value.Value : (ulong?)null) };
            }
            return new TerminalStateDto { progressState = x.ProgressState, hasEntry = x.EntryKey.HasValue,
                entryRunId = x.EntryKey.HasValue ? x.EntryKey.Value.RunId.ToString() : null,
                rulesetVersion = x.EntryKey.HasValue ? x.EntryKey.Value.RulesetVersion : 0, seed = O(x.Seed),
                enteredTick = O(x.EnteredTick), groundContinuationTick = O(x.GroundContinuationTick),
                clearedTick = O(x.ClearedTick), nextTicketIndex = O(x.NextTicketIndex), tickets = tickets };
        }

        private static TerminalState FromTerminal(TerminalStateDto d)
        {
            if (d == null) throw new FormatException("Missing Terminal state.");
            TerminalState s = TerminalState.Empty; s.ProgressState = d.progressState;
            if (d.hasEntry) s.EntryKey = new TerminalEntryKey(RunId.Parse(d.entryRunId), d.rulesetVersion);
            s.Seed = U(d.seed); s.EnteredTick = I(d.enteredTick); s.GroundContinuationTick = I(d.groundContinuationTick);
            s.ClearedTick = I(d.clearedTick); s.NextTicketIndex = I(d.nextTicketIndex);
            foreach (TerminalTicketDto x in d.tickets ?? Array.Empty<TerminalTicketDto>())
            {
                ulong? active = U(x.activeEntityId);
                s.RestoreTicket(new TerminalTicket(ParseHex(x.ticketId), x.queueIndex, x.enemyArchetypeId,
                    x.originalScheduledTick, x.initialRouteChoiceKey, x.rulesetVersion), x.lifecycle,
                    active.HasValue ? new EntityId(active.Value) : (EntityId?)null);
            }
            return s;
        }

        private static OutcomeDto ToOutcome(CommittedOutcome? x) => !x.HasValue ? new OutcomeDto() :
            new OutcomeDto { hasValue = true, pendingOutcomeId = Hex(x.Value.PendingOutcomeId), personal = x.Value.Value.Personal,
                world = x.Value.Value.World, primaryCategory = x.Value.Value.PrimaryCategory, secretEnding = x.Value.Value.SecretEnding };
        private static CommittedOutcome? FromOutcome(OutcomeDto x) => x == null || !x.hasValue ? (CommittedOutcome?)null :
            new CommittedOutcome(ParseHex(x.pendingOutcomeId), new RunOutcome(x.personal, x.world, x.primaryCategory, x.secretEnding));

        private static CommittedFactDto[] ToFacts(RunState state)
        {
            var values = new List<CommittedFactDto>();
            foreach (CommittedFactRecord record in state.CommittedFacts)
            {
                var d = new CommittedFactDto { cursor = Hex(record.Cursor), kind = record.Kind };
                if (record.Kind == CommittedFactKind.Damage)
                {
                    CommittedDamageFact x = record.Damage;
                    d.fixedTick = x.FixedTick; d.eventSequence = Hex(x.EventSequence);
                    d.sourceEntityId = x.SourceEntityId.ToString(); d.instigatorEntityId = x.InstigatorEntityId.ToString();
                    d.targetEntityId = x.TargetEntityId.ToString(); d.hasSourceActorKind = x.SourceActorKind.HasValue;
                    d.sourceActorKind = x.SourceActorKind.GetValueOrDefault(); d.targetKind = x.TargetKind;
                    d.damageKind = x.DamageKind; d.amount = x.Amount; d.fireSequenceId = x.FireSequenceId.ToString();
                    d.hitPointsBefore = x.HitPointsBefore; d.hitPointsAfter = x.HitPointsAfter; d.becameResolved = x.BecameResolved;
                }
                else
                {
                    CommittedActorStateTransitionFact x = record.ActorTransition;
                    d.fixedTick = x.FixedTick; d.eventSequence = Hex(x.EventSequence); d.targetEntityId = x.ActorEntityId.ToString();
                    d.actorKind = x.ActorKind; d.previousLifeState = x.PreviousLifeState; d.currentLifeState = x.CurrentLifeState;
                    d.death = ToDeath(x.Death); d.lethalDamageEventSequence = O(x.LethalDamageEventSequence);
                    d.hasLethalSourceActorKind = x.LethalSourceActorKind.HasValue;
                    d.lethalSourceActorKind = x.LethalSourceActorKind.GetValueOrDefault();
                    d.lethalSourceEntityId = O(x.LethalSourceEntityId.HasValue ? x.LethalSourceEntityId.Value.Value : (ulong?)null);
                }
                values.Add(d);
            }
            return values.ToArray();
        }

        private static void RestoreFacts(RunState state, CommittedFactDto[] values, string acknowledgedCursor)
        {
            foreach (CommittedFactDto d in values ?? Array.Empty<CommittedFactDto>())
            {
                ulong cursor = ParseHex(d.cursor); ulong sequence = ParseHex(d.eventSequence);
                if (d.kind == CommittedFactKind.Damage)
                {
                    var packet = new DamagePacket(sequence, d.fixedTick, EntityId.Parse(d.sourceEntityId),
                        EntityId.Parse(d.instigatorEntityId), EntityId.Parse(d.targetEntityId), d.damageKind, d.amount,
                        Vector3.zero, Vector3.zero, FireSequenceId.Parse(d.fireSequenceId));
                    var receipt = new DamageReceipt(DamageApplyStatus.Applied, sequence, packet.Target,
                        d.hitPointsBefore, d.hitPointsAfter, d.becameResolved);
                    state.RestoreCommittedFact(new CommittedFactRecord(cursor,
                        new CommittedDamageFact(packet, d.hasSourceActorKind ? d.sourceActorKind : (DamageableKind?)null, d.targetKind, receipt)));
                }
                else if (d.kind == CommittedFactKind.ActorStateTransition)
                {
                    ulong? lethalSource = U(d.lethalSourceEntityId);
                    state.RestoreCommittedFact(new CommittedFactRecord(cursor,
                        new CommittedActorStateTransitionFact(EntityId.Parse(d.targetEntityId), d.actorKind,
                            d.previousLifeState, d.currentLifeState, d.fixedTick, sequence, FromDeath(d.death),
                            U(d.lethalDamageEventSequence), d.hasLethalSourceActorKind ? d.lethalSourceActorKind : (DamageableKind?)null,
                            lethalSource.HasValue ? new EntityId(lethalSource.Value) : (EntityId?)null)));
                }
                else throw new FormatException("Unknown committed fact kind.");
            }
            state.CommittedFactAcknowledgedCursor = string.IsNullOrWhiteSpace(acknowledgedCursor) ? 0 : ParseHex(acknowledgedCursor);
            if (state.CommittedFactAcknowledgedCursor >= state.NextCommittedFactCursor)
                throw new FormatException("Committed fact acknowledgement cursor exceeds journal.");
        }

        private static DamageableDto[] Damageables(RunState s)
        { var a = new List<DamageableDto>(); foreach (DamageableRecord x in s.DamageableRecords) a.Add(new DamageableDto { entityId = x.Id.ToString(), kind = x.Kind, hitPoints = x.HitPoints, isResolved = x.IsResolved }); a.Sort((x, y) => string.CompareOrdinal(x.entityId, y.entityId)); return a.ToArray(); }
        private static string[] Applied(RunState s)
        { var a = new List<ulong>(s.AppliedDamageEvents); a.Sort(); var r = new string[a.Count]; for (int i = 0; i < r.Length; i++) r[i] = Hex(a[i]); return r; }
        private static RunStateLoadResult Isolated(RunStateLoadStatus s, string why) => new RunStateLoadResult { Status = s, IsolationReason = why ?? "RunState load failed." };
        private static string Hex(ulong x) => x.ToString("x16", CultureInfo.InvariantCulture);
        private static ulong ParseHex(string x) => ulong.Parse(x, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        private static RngDto ToRng(RngStreamState x) => new RngDto { state0 = Hex(x.State0), state1 = Hex(x.State1) };
        private static RngStreamState FromRng(RngDto x) { if (x == null) throw new FormatException("Missing RNG state."); return new RngStreamState(ParseHex(x.state0), ParseHex(x.state1)); }
        private static DeathDto ToDeath(DeathFact? x) => x.HasValue ? new DeathDto { hasValue = true, tick = x.Value.Tick, sequence = Hex(x.Value.EventSequence), cause = x.Value.Cause } : new DeathDto();
        private static DeathFact? FromDeath(DeathDto x) => x == null || !x.hasValue ? (DeathFact?)null : new DeathFact(x.tick, ParseHex(x.sequence), x.cause);
        private static OptionalIntDto O(int? x) => new OptionalIntDto { hasValue = x.HasValue, value = x.GetValueOrDefault() };
        private static OptionalUlongDto O(ulong? x) => new OptionalUlongDto { hasValue = x.HasValue, value = x.HasValue ? Hex(x.Value) : null };
        private static int? I(OptionalIntDto x) => x != null && x.hasValue ? x.value : (int?)null;
        private static ulong? U(OptionalUlongDto x) => x != null && x.hasValue ? ParseHex(x.value) : (ulong?)null;
        private static void Death(StringBuilder b, DeathDto x) { b.Append('|').Append(x.hasValue ? '1' : '0'); if (x.hasValue) b.Append(':').Append(x.tick).Append(':').Append(x.sequence).Append(':').Append((int)x.cause); }
        private static void Rng(StringBuilder b, RngDto x) => b.Append('|').Append(x.state0).Append(':').Append(x.state1);
        private static void Opt(StringBuilder b, OptionalIntDto x) => NullableInt(b, I(x));
        private static void Opt(StringBuilder b, OptionalUlongDto x) => NullableUlong(b, U(x));
        private static void NullableInt(StringBuilder b, int? x) { b.Append('|').Append(x.HasValue ? '1' : '0'); if (x.HasValue) b.Append(':').Append(x.Value); }
        private static void NullableUlong(StringBuilder b, ulong? x) { b.Append('|').Append(x.HasValue ? '1' : '0'); if (x.HasValue) b.Append(':').Append(Hex(x.Value)); }
        private static void NullableText(StringBuilder b, string x) { b.Append('|').Append(x == null ? -1 : x.Length).Append('#'); if (x != null) b.Append(x); }
        private static void Text(StringBuilder b, string x) => b.Append(x.Length).Append('#').Append(x).Append('|');
    }

    public interface ISaveBackend { void WritePending(string slot, string utf8Json); void Commit(string slot); }
}
