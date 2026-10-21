using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Core.Partner
{
    public sealed class M4AuthoritativeCommitCoordinator
    {
        readonly M4ThreeNightHarness harness; readonly RunState run; readonly PartnerRuntimeState partner;
        readonly EntityId[] barriers; readonly EntityId[] supports; readonly PartnerData40Journal data40;
        M4CoordinatorSave state;
        public M4AuthoritativeCommitCoordinator(M4ThreeNightHarness harness,RunState run,PartnerRuntimeState partner,
            EntityId[] barrierIds,EntityId[] supportIds,M4CoordinatorSave restored=null)
        {
            this.harness=harness??throw new ArgumentNullException(nameof(harness));this.run=run??throw new ArgumentNullException(nameof(run));this.partner=partner??throw new ArgumentNullException(nameof(partner));
            barriers=(EntityId[])(barrierIds??Array.Empty<EntityId>()).Clone();supports=(EntityId[])(supportIds??Array.Empty<EntityId>()).Clone();data40=new PartnerData40Journal(partner);state=restored?.Copy()??new M4CoordinatorSave();
            ValidateRestoredEvidence();
        }
        public M4CoordinatorSave CaptureSave()=>state.Copy();
        public M4LossTotals CaptureAggregateLosses()=>state.AggregateLosses();
        public M4LossTotals CapturePerNightLosses()=>state.PerNightLosses.Copy();
        // The M4 session only needs a durable commit when this evidence projection changes.
        // Reading the M3 snapshot and fact cursor is cheap; cloning/serializing a full session
        // every 50 Hz is not an approved simulation requirement.
        public bool RequiresObservation(EnemyDirectorRuntime director)
        {
            if (director == null) throw new ArgumentNullException(nameof(director));
            if (harness.IsFrozen || state.CurrentNightCommitted) return false;
            EnemyDirectorRuntimeSnapshot snapshot = director.Snapshot();
            if (snapshot.ActiveCount > state.MaximumActiveEnemies) return true;
            if (CommittedFactJournal.ReadAfter(run, state.RunFactCursor).Records.Length != 0) return true;
            return snapshot.Tickets.Length > 0 && snapshot.ResolvedCount == snapshot.Tickets.Length &&
                snapshot.PendingCount == 0 && snapshot.PoolWaitingCount == 0 && snapshot.ActiveCount == 0;
        }
        void ValidateRestoredEvidence()
        {
            if (state.EvidenceSchemaVersion != 2 || state.PerNightLosses == null)
                throw new FormatException("Legacy coordinator evidence does not contain per-Night continuation state.");
            if (state.NightOrdinal != harness.CurrentNightOrdinal || state.MaximumActiveEnemies < 0 ||
                (state.CurrentNightCommitted && harness.CompletedNights == 0) ||
                (harness.IsFrozen && !state.CurrentNightCommitted))
                throw new FormatException("Coordinator and Harness history disagree.");
            if (state.RunFactCursor > CommittedFactJournal.ReadAfter(run, 0).LastCommittedCursor)
                throw new FormatException("Coordinator cursor exceeds the committed journal.");
            M4LossTotals aggregate = state.AggregateLosses();
            aggregate.Validate(); state.PerNightLosses.Validate();
            M4LossTotals committed = harness.LastCommittedLosses();
            if (!aggregate.Contains(state.PerNightLosses) || !aggregate.Contains(committed) ||
                (state.CurrentNightCommitted && !committed.Contains(aggregate)))
                throw new FormatException("Coordinator loss history is inconsistent.");
            if (state.CurrentNightCommitted)
            {
                NightLossObservation last = harness.LastCommittedObservation();
                if (!state.PerNightLosses.Contains(last.PerNightLosses) ||
                    !last.PerNightLosses.Contains(state.PerNightLosses) ||
                    state.MaximumActiveEnemies != last.MaximumActiveEnemies)
                    throw new FormatException("Committed coordinator observations disagree with Harness history.");
            }
        }
        public void Observe(EnemyDirectorRuntime director)
        {
            if(director==null)throw new ArgumentNullException(nameof(director));if(harness.IsFrozen||state.CurrentNightCommitted)return;
            ConsumeRunFacts();EnemyDirectorRuntimeSnapshot snapshot=director.Snapshot();state.MaximumActiveEnemies=Math.Max(state.MaximumActiveEnemies,snapshot.ActiveCount);state.LastDirectorStateHash=snapshot.StateHash;state.LastQueueHash=snapshot.ImmutableQueueHash;
            bool complete=snapshot.Tickets.Length>0&&snapshot.ResolvedCount==snapshot.Tickets.Length&&snapshot.PendingCount==0&&snapshot.PoolWaitingCount==0&&snapshot.ActiveCount==0;
            if(!complete||state.CurrentNightCommitted)return;
            string hash=M4UnifiedRunStateJson.CanonicalGameplayHash(run,partner)+"|"+snapshot.ImmutableQueueHash+"|"+snapshot.StateHash;
            var observation=new NightLossObservation{NightOrdinal=state.NightOrdinal,NightSeed=harness.CurrentNightSeed,ResolvedTicketCount=snapshot.ResolvedCount,TotalTicketCount=snapshot.Tickets.Length,MaximumActiveEnemies=state.MaximumActiveEnemies,PlayerDamage=state.PlayerDamage,PartnerDamage=state.PartnerDamage,BarrierDamage=state.BarrierDamage,SupportDamage=state.SupportDamage,PlayerDeath=state.PlayerDeath,PartnerDeath=state.PartnerDeath,BarrierPermanentLoss=state.BarrierPermanentLoss,SupportPermanentLoss=state.SupportPermanentLoss,GameplayHash=hash,PerNightLosses=state.PerNightLosses.Copy(),AggregateLosses=state.AggregateLosses()};
            harness.CommitNight(observation,true);state.CurrentNightCommitted=true;data40.Append(Data40FactKind.RoleTransition,run.FixedTick,run.IssueEventSequence(),"night-"+state.NightOrdinal,"authoritative-night-complete");state.NightOrdinal++;
        }
        public void BeginNextCanonicalNight()
        {
            if(harness.IsFrozen)throw new InvalidOperationException("Night 3 evidence is frozen.");if(!state.CurrentNightCommitted)throw new InvalidOperationException("Prior Night is not committed.");
            state.CurrentNightCommitted=false;state.MaximumActiveEnemies=0;
            state.PerNightLosses = new M4LossTotals(); // observation interval only; cumulative/gameplay loss is retained
        }
        void ConsumeRunFacts()
        {
            CommittedFactJournalSnapshot journal=CommittedFactJournal.ReadAfter(run,state.RunFactCursor);
            M4LossTotals aggregate=state.AggregateLosses();
            foreach(CommittedFactRecord record in journal.Records)
            {
                M4LossReducer.Apply(aggregate,record,barriers,supports);
                M4LossReducer.Apply(state.PerNightLosses,record,barriers,supports);
                state.RunFactCursor=record.Cursor;
            }
            state.PlayerDamage=aggregate.PlayerDamage;state.PartnerDamage=aggregate.PartnerDamage;
            state.BarrierDamage=aggregate.BarrierDamage;state.SupportDamage=aggregate.SupportDamage;
            state.PlayerDeath=aggregate.PlayerDeath;state.PartnerDeath=aggregate.PartnerDeath;
            state.BarrierPermanentLoss=aggregate.BarrierPermanentLoss;state.SupportPermanentLoss=aggregate.SupportPermanentLoss;
        }
        static bool Contains(EntityId[] values,EntityId id){foreach(EntityId x in values)if(x==id)return true;return false;}
    }
}
