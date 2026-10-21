using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;
using LastWatch.Core.Persistence;
using LastWatch.Core.Space;

namespace LastWatch.Core.State
{
    public enum M4CommandStatus { Committed, NoChange, AlreadyCommitted, StoreFailure, EntryConflict, StaleOrForeignDirector, UnsupportedEntryTopology, InvalidPhase, HigherPriorityState }
    public sealed class M4CommandResult
    {
        public M4CommandStatus Status { get; internal set; }
        public string Reason { get; internal set; }
        public M4StoreReceipt Receipt { get; internal set; }
    }
    public readonly struct M4ContextualFact
    {
        public M4Cursor Cursor { get; }
        public M4EntryEventContext Context { get; }
        internal M4ContextualFact(M4Cursor cursor,M4EntryEventContext context){Cursor=cursor;Context=context;}
    }

    // This owns canonical identity/continuation. Harness and Coordinator are private projections.
    // Public mutable compatibility DTOs are always detached. The M3 handle is identity-bound.
    public sealed partial class M4Session
    {
        readonly object gate=new object();bool writing;
        M4RunMetadata metadata;RunState run;PartnerRuntimeState partner;M4PlayerWeaponActionSaveState playerWeaponAction;M4TopologySnapshot topology;
        readonly List<DefenseEntrySnapshot> entries=new List<DefenseEntrySnapshot>();
        readonly List<ulong> completionCursors=new List<ulong>();
        M4ThreeNightHarness harness;M4AuthoritativeCommitCoordinator coordinator;
        EnemyDirectorRuntime director;EnemyPool pool;RunnerEvadeCoordinator runners;M3FactEmitter projection;
        M4SessionStage stage;ulong generation;int? nightStart;
        public M4RunMetadata Metadata=>metadata;
        public M4SessionStage Stage=>stage;
        public ulong Generation=>generation;
        public int CompletedNights=>completionCursors.Count;
        public DefenseEntrySnapshot ActiveEntry=>entries.Count==0?null:entries[entries.Count-1];
        public M4EntryEventContext ActiveEventContext=>run.M4ActiveEntryContext;
        public EnemyDirectorRuntime BoundDirector=>stage==M4SessionStage.Defense||stage==M4SessionStage.NightCompleted||stage==M4SessionStage.Frozen?director:null;
        // Integration access remains inside the Core assembly. Actor simulation ownership is
        // intentionally not exported as a mutable Tools setup or a fabricated tick loop.
        internal RunState RuntimeRun=>run;
        internal PartnerRuntimeState RuntimePartner=>partner;
        internal EnemyPool RuntimePool=>pool;
        internal RunnerEvadeCoordinator RuntimeRunners=>runners;
        public int? NightElapsedTick=>nightStart.HasValue?checked(run.FixedTick-nightStart.Value):(int?)null;
        public RunStateSnapshot ReadRun()=>run.Snapshot();
        public PartnerRuntimeState ReadPartner()=>M4ValueCodec.Copy(partner);
        public M4PlayerWeaponActionSaveState ReadPlayerWeaponActionState()=>playerWeaponAction==null?null:playerWeaponAction.Copy();
        internal void RecordPlayerWeaponActionState(M4PlayerWeaponActionSaveState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            state.Validate();
            lock(gate)
            {
                if(writing)throw new InvalidOperationException("A session save transaction is pending.");
                playerWeaponAction=state.Copy();
            }
        }
        public M4HarnessSaveSnapshot ReadEvidence()=>harness.CaptureSave();
        public M4TopologySnapshot ReadTopology()=>topology;
        public M4FrozenEvidence ExportFrozen()=>harness.ExportFrozen();
        M4Session(){}

        public static M4Session CreateNew(M4RunMetadata metadata,string buildVersion,M4TopologySnapshot topology,DamageableFixture[] setup,PartnerRuntimeState initialPartner,M4PlayerWeaponActionSaveState initialPlayerWeaponAction=null)
        {
            if(metadata==null||topology==null||setup==null||initialPartner==null)throw new ArgumentNullException();
            var s=new M4Session{metadata=metadata,topology=topology,stage=M4SessionStage.New};
            s.run=RunStateFactory.CreateNew(metadata.RunId,buildVersion,"3.1",metadata.RunSeed);s.run.RunContext=RunContextDefinition.ForM4(metadata);
            foreach(DamageableFixture d in setup){if(d==null||!Enum.IsDefined(typeof(DamageableKind),d.Kind))throw new ArgumentException("Invalid setup damageable.");s.run.AddDamageable(new EntityId(d.EntityValue),d.Kind,d.HitPoints,d.IsResolved);}
            s.partner=M4ValueCodec.Copy(initialPartner);
            if(initialPlayerWeaponAction!=null){initialPlayerWeaponAction.Validate();s.playerWeaponAction=initialPlayerWeaponAction.Copy();}
            if(s.partner.Data40Facts==null||s.partner.Data40Facts.Count!=0||s.partner.FriendlyIncidents==null||s.partner.FriendlyIncidents.Count!=0||s.partner.Life!=PartnerLifeState.Healthy||s.partner.DeathTick.HasValue)
                throw new ArgumentException("New session requires uncommitted initial Partner state.");
            s.partner.Role=PartnerRole.CoverFace;s.partner.DesignatedFaceId="A";s.partner.CurrentNodeId="FaceA.Cover";
            s.harness=new M4ThreeNightHarness((M4HarnessDifficulty)metadata.Difficulty);
            s.coordinator=s.MakeCoordinator();s.ValidateCoherence();return s;
        }
        M4AuthoritativeCommitCoordinator MakeCoordinator(M4CoordinatorSave saved=null)=>new M4AuthoritativeCommitCoordinator(harness,run,partner,topology.Ids(M4BindingRole.Barrier),topology.Ids(M4BindingRole.Support),saved);

        public M4CommandResult EnterDefense(IM4SessionStore store)
        {
            if(store==null)throw new ArgumentNullException(nameof(store));
            lock(gate)
            {
                if(writing)return Result(M4CommandStatus.EntryConflict,"A transaction is already being committed.");writing=true;
                try
                {
                    if(stage==M4SessionStage.Defense)return Result(M4CommandStatus.AlreadyCommitted,"Current immutable entry is already in Defense.");
                    if(stage==M4SessionStage.Frozen)return Result(M4CommandStatus.InvalidPhase,"Night 4 is outside this slice.");
                    if(run.PlayerLife==PlayerLifeState.Dead||run.Outcome.HasValue||run.Tower==TowerState.Collapsed||run.Phase==RunPhase.FinalCollapseWarning)return Result(M4CommandStatus.HigherPriorityState,"Higher-priority state prevents the next entry.");
                    if(stage!=M4SessionStage.Prepared)
                    {
                        if(stage!=M4SessionStage.New&&stage!=M4SessionStage.NightCompleted)return Result(M4CommandStatus.InvalidPhase,"Cannot prepare from this state.");
                        if(topology.ValidFaces.Length!=3)return Result(M4CommandStatus.UnsupportedEntryTopology,"Reduced entry topology has no approved queue-share profile; no tickets generated.");
                        M4Session candidate=Clone();int ordinal=checked(CompletedNights+1);
                        M4CommittedAllocation allocation=ActiveEntry==null?M4CommittedAllocation.Initial:ActiveEntry.Allocation;
                        var entry=new DefenseEntrySnapshot(metadata,ordinal,run.FixedTick,run.Phase,allocation,topology,EnemyDirector.CreateStoryQueue(metadata.Request(ordinal)));
                        candidate.entries.Add(entry);candidate.stage=M4SessionStage.Prepared;candidate.nightStart=null;
                        candidate.projection=new M3FactEmitter();candidate.director=new EnemyDirectorRuntime(entry.OriginalQueue,candidate.projection);candidate.pool=pool==null?new EnemyPool():EnemyPool.Restore(pool.CaptureSave());candidate.runners=new RunnerEvadeCoordinator(candidate.projection);
                        M4CommandResult first=Commit(store,candidate);if(first.Status!=M4CommandStatus.Committed)return first;
                    }
                    // The same prepared snapshot is reused after first-commit recovery. No RNG/queue generation here.
                    M4Session defense=Clone();if(defense.coordinator.CaptureSave().CurrentNightCommitted)defense.coordinator.BeginNextCanonicalNight();
                    defense.stage=M4SessionStage.Defense;defense.run.Phase=RunPhase.Defense;defense.nightStart=defense.run.FixedTick;defense.BindContext();
                    return Commit(store,defense);
                }
                finally{writing=false;}
            }
        }

        // Must be invoked at a completed fixed-tick boundary by the Core integration owner.
        // Passing a queue-equivalent Director from another entry/run is still rejected.
        public M4CommandResult ObserveCompletedTick(IM4SessionStore store,EnemyDirectorRuntime source)
        {
            if(store==null)throw new ArgumentNullException(nameof(store));
            lock(gate)
            {
                if(writing)return Result(M4CommandStatus.EntryConflict,"A transaction is pending.");
                if(source==null||!ReferenceEquals(source,director)||ActiveEntry==null||source.Snapshot().ImmutableQueueHash!=ActiveEntry.QueueHash)return Result(M4CommandStatus.StaleOrForeignDirector,"Director is not the session-owned current entry instance.");
                if(stage==M4SessionStage.NightCompleted||stage==M4SessionStage.Frozen)return Result(M4CommandStatus.AlreadyCommitted,"Current completion key is already committed.");
                if(stage!=M4SessionStage.Defense)return Result(M4CommandStatus.InvalidPhase,"Defense has not completed its second commit.");
                if(!coordinator.RequiresObservation(source))return Result(M4CommandStatus.NoChange,"No M4 persistence observation changed on this fixed tick.");
                EnemyDirectorRuntimeSnapshot snapshot=source.Snapshot();
                bool completed=snapshot.Tickets.Length>0&&snapshot.ResolvedCount==snapshot.Tickets.Length&&snapshot.PendingCount==0&&snapshot.PoolWaitingCount==0&&snapshot.ActiveCount==0;
                if(!completed)
                {
                    // Damage and active-count observations remain canonical in the live session,
                    // but a real-time hit must not synchronously serialize/validate the entire
                    // continuation graph. SaveBoundary and Night completion are durable edges.
                    coordinator.Observe(director);
                    return Result(M4CommandStatus.NoChange,"Live M4 evidence updated; persistence is deferred to a save boundary or Night completion.");
                }
                writing=true;
                try
                {
                    M4Session candidate=Clone();candidate.coordinator.Observe(candidate.director);
                    if(candidate.coordinator.CaptureSave().CurrentNightCommitted)
                    {
                        candidate.completionCursors.Add(candidate.coordinator.CaptureSave().RunFactCursor);
                        candidate.stage=candidate.CompletedNights==3?M4SessionStage.Frozen:M4SessionStage.NightCompleted;
                        new PartnerController(candidate.partner).CancelExchange(candidate.run.FixedTick, "night completed");
                        if (candidate.partner.ExchangeFlow != null)
                        { candidate.partner.ExchangeFlow.suspended = false; candidate.partner.ExchangeFlow.returnReason = "Night closed; prior Cover not resumed"; }
                        M4PartnerMovementPersistence.Cancel(candidate.partner, candidate.run.FixedTick, "night completed");
                        candidate.harness.BindLastCanonicalHash(M4SessionHashes.Gameplay(candidate));
                    }
                    return Commit(store,candidate);
                }
                finally{writing=false;}
            }
        }

        public M4CommandResult SaveBoundary(IM4SessionStore store)
        {if(store==null)throw new ArgumentNullException(nameof(store));lock(gate){if(writing)return Result(M4CommandStatus.EntryConflict,"A transaction is pending.");writing=true;try{return Commit(store,Clone());}finally{writing=false;}}}
        public static M4LoadResult Recover(IM4SessionStore store,RunId runId)
        {
            if(store==null)throw new ArgumentNullException(nameof(store));M4StoredSnapshot saved=store.ReadLatest(runId);
            if(saved==null)return new M4LoadResult{Status=M4LoadStatus.CorruptOrInconsistent,Reason="No confirmed generation for this RunId."};
            M4LoadResult result=M4SessionJson.TryDeserialize(saved.Json,M4RestorePurpose.BoundedContract);
            if(result.Session!=null)
            {
                var dto=result.Session.CaptureDto();M4SessionJson.Seal(dto);
                bool currentReceipt = saved.Receipt != null && saved.Receipt.RunId.Equals(runId) &&
                    result.Session.Metadata.RunId.Equals(runId) && saved.Receipt.Generation == dto.generation &&
                    saved.Receipt.Stage == dto.stage && saved.Receipt.Digest == dto.integrityHash;
                bool verifiedOriginalReceipt = !currentReceipt && result.Session.Metadata.RunId.Equals(runId) &&
                    M4SessionJson.MatchesVerifiedOriginalReceipt(saved.Json, saved.Receipt, runId);
                if(!currentReceipt && !verifiedOriginalReceipt)
                    return new M4LoadResult{Status=M4LoadStatus.CorruptOrInconsistent,Reason="Receipt and read-back payload disagree."};
            }
            return result;
        }
        M4CommandResult Commit(IM4SessionStore store,M4Session candidate)
        {
            candidate.generation=checked(generation+1);candidate.ValidateCoherence();M4SessionDto dto=candidate.CaptureDto();var request=new M4StoreCandidate(dto);
            M4StoreResult result;
            try{result=store.CommitAndFlush(request);}catch(Exception e) when(!(e is OutOfMemoryException)){result=new M4StoreResult(M4StoreStatus.Unknown,null,e.Message);}
            M4StoreReceipt receipt=result==null?null:result.Receipt;
            if(result==null||result.Status!=M4StoreStatus.Committed||receipt==null||!receipt.Matches(request))
            {
                // A flush may have completed immediately before the acknowledgement was lost.
                M4StoredSnapshot latest=null;try{latest=store.ReadLatest(metadata.RunId);}catch(Exception e) when(!(e is OutOfMemoryException)){return Result(M4CommandStatus.StoreFailure,e.Message);}
                if(latest==null||!latest.Receipt.Matches(request)||latest.Json!=request.Json)return Result(M4CommandStatus.StoreFailure,result==null?"Missing receipt.":result.Reason);
                receipt=latest.Receipt;
            }
            Adopt(candidate);return new M4CommandResult{Status=M4CommandStatus.Committed,Receipt=receipt,Reason=receipt.Durability==M4StoreDurability.InMemoryContractOnly?"Memory contract commit only; platform durable flush NOT_IMPLEMENTED.":null};
        }
        static M4CommandResult Result(M4CommandStatus status,string reason)=>new M4CommandResult{Status=status,Reason=reason};
    }
}
