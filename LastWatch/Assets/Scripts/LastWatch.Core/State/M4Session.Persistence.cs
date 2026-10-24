using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;
using LastWatch.Core.Persistence;

namespace LastWatch.Core.State
{
    // Persistence and coherence stay in the same session type so no mutable state becomes public.
    public sealed partial class M4Session
    {
        M4Session Clone()=>RestoreDto(CaptureDto());
        void Adopt(M4Session s)
        {
            bool sameEntry=ActiveEntry!=null&&s.ActiveEntry!=null&&ActiveEntry.Key.Equals(s.ActiveEntry.Key);
            metadata=s.metadata;run=s.run;partner=s.partner;playerWeaponAction=s.playerWeaponAction;playerResources=s.playerResources;topology=s.topology;entries.Clear();entries.AddRange(s.entries);completionCursors.Clear();completionCursors.AddRange(s.completionCursors);harness=s.harness;coordinator=s.coordinator;
            if(!sameEntry){director=s.director;pool=s.pool;runners=s.runners;projection=s.projection;}
            stage=s.stage;generation=s.generation;nightStart=s.nightStart;
        }

        void BindContext()
        {if(ActiveEntry==null||!nightStart.HasValue)return;var context=new M4EntryEventContext(metadata,ActiveEntry,nightStart.Value);run.M4ActiveEntryContext=context;partner.M4ActiveEntryContext=context;}

        public M4ContextualFact[] ReadEventContexts()
        {
            var values=new List<M4ContextualFact>();foreach(var f in run.CommittedFacts)if(f.M4Context!=null)values.Add(new M4ContextualFact(new M4Cursor(M4CursorNamespace.RunFacts,f.Cursor),f.M4Context));
            foreach(var f in partner.Data40Facts)if(partner.M4FactContexts.TryGetValue(f.Cursor,out M4EntryEventContext context))values.Add(new M4ContextualFact(new M4Cursor(M4CursorNamespace.PartnerData40,f.Cursor),context));
            if(director!=null&&ActiveEventContext!=null){foreach(var f in director.Facts)values.Add(new M4ContextualFact(new M4Cursor(M4CursorNamespace.DirectorFacts,f.Sequence),ActiveEventContext));foreach(var f in projection.Snapshot())values.Add(new M4ContextualFact(new M4Cursor(M4CursorNamespace.ProjectionFacts,f.Sequence),ActiveEventContext));}
            return values.ToArray();
        }

        internal M4SessionDto CaptureDto()
        {
            var list=new M4EntryDto[entries.Count];for(int n=0;n<list.Length;n++){list[n]=M4SessionJson.Entry(entries[n]);list[n].digest=entries[n].Digest;}
            var contexts=new List<M4ContextStampDto>();foreach(var f in ReadEventContexts())if(f.Cursor.Namespace==M4CursorNamespace.RunFacts||f.Cursor.Namespace==M4CursorNamespace.PartnerData40)contexts.Add(new M4ContextStampDto{producer=f.Cursor.Namespace,cursor=f.Cursor.Value,ordinal=f.Context.Key.NightOrdinal,entryDigest=f.Context.EntrySnapshotHash});
            return new M4SessionDto{format=M4SessionJson.Format,schema=3,hashVersion=1,profile=M4BridgeProfile.Id,seedAlgorithm=M4BridgeProfile.SeedAlgorithm,queueAlgorithm=M4BridgeProfile.QueueAlgorithm,ruleset=31,runId=metadata.RunId.Value,runSeed=metadata.RunSeed,difficulty=metadata.Difficulty,rulesetHash=metadata.RulesetHash,contentVersion=metadata.ContentVersion,generation=generation,stage=stage,completedNights=CompletedNights,snapshotTick=run.FixedTick,nightStartTick=nightStart,runtimeCoverage=M4SessionJson.RuntimeCoverage,
                run=RunStateJson.CaptureM4Payload(run),partner=M4ValueCodec.Copy(partner),playerWeaponAction=playerWeaponAction==null?null:playerWeaponAction.Copy(),playerResources=playerResources==null?null:playerResources.Copy(),entries=list,topology=M4SessionJson.Topology(topology),combat=director==null?null:M4CombatCodec.Capture(director,pool,runners,projection),coordinator=coordinator.CaptureSave(),harness=harness.CaptureSave(),contexts=contexts.ToArray(),completionRunFactCursors=completionCursors.ToArray()};
        }

        internal static M4Session RestoreDto(M4SessionDto source)
        {
            M4SessionDto d=M4ValueCodec.Copy(source);
            if(d.run==null||d.partner==null||d.entries==null||d.contexts==null||d.completionRunFactCursors==null||d.entries.Length>3||d.completedNights!=d.completionRunFactCursors.Length)throw new FormatException("Incomplete session graph.");
            var s=new M4Session{metadata=new M4RunMetadata(new RunId(d.runId),d.runSeed,d.difficulty),topology=M4SessionJson.Topology(d.topology),stage=d.stage,generation=d.generation,nightStart=d.nightStartTick,partner=d.partner,playerWeaponAction=d.playerWeaponAction==null?null:d.playerWeaponAction.Copy(),playerResources=d.playerResources==null?null:d.playerResources.Copy()};
            s.run=RunStateJson.RestoreM4Payload(d.run,s.metadata);if(s.run.FixedTick!=d.snapshotTick)throw new FormatException("Snapshot tick mismatch.");
            for(int n=0;n<d.entries.Length;n++){if(d.entries[n].ordinal!=n+1)throw new FormatException("Entry ledger ordinal mismatch.");s.entries.Add(M4SessionJson.Entry(d.entries[n],s.metadata));}
            s.completionCursors.AddRange(d.completionRunFactCursors);s.harness=M4ThreeNightHarness.Restore(d.harness);s.coordinator=s.MakeCoordinator(d.coordinator);
            if(d.combat!=null)
            {
                M3RuntimeRestoreResult r=M4CombatCodec.Restore(d.combat);
                s.director=r.Director;s.pool=r.Pool;s.runners=r.Runners;s.projection=r.Facts;
                // The Director persists ticket-to-recycled-pool identity while RunState
                // persists HP. Rebind them without rearming/resetting HP, then require
                // the pair to remain coherent for all later M4 hashes/commits.
                EnemyIncarnationLifecycle.RestoreAndValidate(s.run,s.director);
            }
            var seen=new HashSet<string>();
            foreach(var row in d.contexts)
            {
                if(row.ordinal<1||row.ordinal>s.entries.Count||row.entryDigest!=s.entries[row.ordinal-1].Digest||!seen.Add(row.producer+":"+row.cursor))throw new FormatException("Invalid/duplicate event context.");
                DefenseEntrySnapshot entry=s.entries[row.ordinal-1];var context=new M4EntryEventContext(s.metadata,entry,entry.PreparedAtFixedTick);
                if(row.producer==M4CursorNamespace.RunFacts)s.run.RestoreM4FactContext(row.cursor,context);
                else if(row.producer==M4CursorNamespace.PartnerData40){if(row.cursor==0||row.cursor>=s.partner.NextData40Cursor)throw new FormatException("Missing DATA-40 context cursor.");s.partner.M4FactContexts.Add(row.cursor,context);}
                else throw new FormatException("Unsupported event context namespace.");
            }
            s.BindContext();s.ValidateCoherence();return s;
        }

        void ValidateCoherence()
        {
            topology.ValidateRun(run);M4SessionValidation.ValidatePartner(run,partner,topology,playerResources);
            if(playerWeaponAction!=null)playerWeaponAction.Validate();
            if(playerResources!=null)playerResources.Validate();
            if(!run.RunId.Equals(metadata.RunId)||harness.Difficulty!=(M4HarnessDifficulty)metadata.Difficulty||harness.CompletedNights!=CompletedNights||CompletedNights>3)throw new FormatException("Run/history identity mismatch.");
            bool committed=stage==M4SessionStage.NightCompleted||stage==M4SessionStage.Frozen;
            if(stage==M4SessionStage.New){if(entries.Count!=0||CompletedNights!=0||director!=null||nightStart.HasValue)throw new FormatException("New session has continuation state.");}
            else
            {
                if(ActiveEntry==null||director==null||ActiveEntry.Key.NightOrdinal!=(committed?CompletedNights:CompletedNights+1)||director.Snapshot().ImmutableQueueHash!=ActiveEntry.QueueHash)throw new FormatException("Entry/Director identity mismatch.");
                EnemyIncarnationLifecycle.Validate(run,director);
                if(stage==M4SessionStage.Prepared){if(nightStart.HasValue||run.Phase!=ActiveEntry.PriorPhase||director.Snapshot().Cursor!=0)throw new FormatException("Invalid prepared state.");}
                else if(!nightStart.HasValue||nightStart.Value!=ActiveEntry.PreparedAtFixedTick||nightStart.Value>run.FixedTick||run.Phase!=RunPhase.Defense)throw new FormatException("Defense clock/phase mismatch.");
                if(committed){var snap=director.Snapshot();if(snap.ResolvedCount!=snap.Tickets.Length||snap.ActiveCount!=0)throw new FormatException("Completed entry contains unresolved tickets.");}
            }
            if((stage==M4SessionStage.Frozen)!=(CompletedNights==3)||harness.IsFrozen!=(stage==M4SessionStage.Frozen))throw new FormatException("Freeze mismatch.");
            bool projectionCommitted=coordinator.CaptureSave().CurrentNightCommitted;
            if(stage!=M4SessionStage.Prepared&&projectionCommitted!=committed)throw new FormatException("Completion projection mismatch.");
            M4SessionValidation.ValidateLosses(run,partner,topology,coordinator.CaptureSave(),harness.CaptureSave(),completionCursors.ToArray());
            foreach(var f in run.CommittedFacts)if(f.M4Context==null)throw new FormatException("Canonical Run fact is missing event-time context.");
            foreach(var f in partner.Data40Facts)if(!partner.M4FactContexts.ContainsKey(f.Cursor))throw new FormatException("Canonical DATA-40 fact is missing event-time context.");
        }
    }
}
