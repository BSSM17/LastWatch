using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;

namespace LastWatch.Core.Combat
{
    public enum M3CoreFactKind { SpawnTicketStateChanged, FireSequenceResolved, EnemyResolved, DefenseSummary, RunnerReactionAttempted, RunnerEvadeStarted, RunnerEvadeEnded, RunnerHitDuringEvade, PathRecovery, FaceRerouted }
    public readonly struct M3CoreFact
    {
        public M3CoreFactKind Kind{get;} public int FixedTick{get;} public ulong Sequence{get;} public ulong? TicketId{get;} public EntityId? EntityId{get;} public FireSequenceId? FireSequenceId{get;} public string Detail{get;}
        public M3CoreFact(M3CoreFactKind kind,int tick,ulong sequence,ulong? ticket,EntityId? entity,FireSequenceId? fire,string detail){if(tick<0||sequence==0)throw new ArgumentOutOfRangeException();Kind=kind;FixedTick=tick;Sequence=sequence;TicketId=ticket;EntityId=entity;FireSequenceId=fire;Detail=detail??string.Empty;}
    }
    internal sealed class SpawnTicketRecord
    {
        internal SpawnTicket Ticket; internal SpawnTicketLifecycle Lifecycle; internal EntityId? ActiveEntityId; internal int? ActualSpawnTick; internal ActivationBlockReason BlockReason; internal EnemyMovementState MovementState;
    }
    public sealed class EnemyDirectorRuntimeSnapshot
    {
        public int Cursor{get;internal set;} public string ImmutableQueueHash{get;internal set;} public string StateHash{get;internal set;}
        public SpawnTicketSnapshot[] Tickets{get;internal set;} public int PendingCount{get;internal set;} public int PoolWaitingCount{get;internal set;} public int ActiveCount{get;internal set;} public int ResolvedCount{get;internal set;}
        public float ActivePressure{get;internal set;} public int ActivePressureUnits{get;internal set;} public int[] FaceActiveCounts{get;internal set;} public float[] FacePressures{get;internal set;} public int[] FacePressureUnits{get;internal set;} public int ActiveBreakerCount{get;internal set;} public int HighThreatWindupCount{get;internal set;} public ActiveEnemyPressureSnapshot[] ActiveEnemies{get;internal set;} public SpawnPressureSample[] RecentSpawnPressure{get;internal set;} public int LedgerAsOfTick{get;internal set;} public ulong NextFactSequence{get;internal set;} public ulong AcknowledgedFactCursor{get;internal set;}
    }
    public sealed class EnemyDirectorRuntime
    {
        private readonly EnemyDirectorQueue queue; private readonly List<SpawnTicketRecord> records=new List<SpawnTicketRecord>(); private readonly List<M3CoreFact> facts=new List<M3CoreFact>(); private readonly Queue<Tuple<int,int>> recentSpawnPressure=new Queue<Tuple<int,int>>(); private ulong nextFactSequence=1; private ulong acknowledgedFactCursor; private int ledgerAsOfTick; private M3FactEmitter projectionFacts;
        public EnemyDirectorRuntime(EnemyDirectorQueue source)
        {queue=source??throw new ArgumentNullException(nameof(source));foreach(SpawnTicket x in source.Tickets)records.Add(new SpawnTicketRecord{Ticket=x,Lifecycle=SpawnTicketLifecycle.Pending});}
        public EnemyDirectorRuntime(EnemyDirectorQueue source,M3FactEmitter authoritativeFacts):this(source){projectionFacts=authoritativeFacts??throw new ArgumentNullException(nameof(authoritativeFacts));}
        internal void AttachFactEmitter(M3FactEmitter authoritativeFacts)=>projectionFacts=authoritativeFacts??throw new ArgumentNullException(nameof(authoritativeFacts));
        public IReadOnlyList<M3CoreFact> Facts=>facts.AsReadOnly();
        public bool TryActivateNext(int tick,EnemyPool pool,out EntityId entity)
        {ActivationAttemptResult result=TryActivateNextDetailed(tick,pool);entity=result.EntityId.GetValueOrDefault();return result.Accepted;}
        public ActivationAttemptResult TryActivateNextDetailed(int tick,EnemyPool pool)
        {
            if(pool==null)throw new ArgumentNullException(nameof(pool));if(tick<ledgerAsOfTick)throw new InvalidOperationException("Activation tick cannot move backwards.");ledgerAsOfTick=tick;PruneRecent(tick);EnemyDirectorRuntimeSnapshot pre=Snapshot();
            SpawnTicketRecord r=records.Find(x=>x.Lifecycle==SpawnTicketLifecycle.Pending||x.Lifecycle==SpawnTicketLifecycle.PoolWaiting);
            if(r==null||tick<r.Ticket.ScheduledTick)return new ActivationAttemptResult(false,ActivationBlockReason.None,null,pre,pre,null);
            SpawnTicketSnapshot before=new SpawnTicketSnapshot(r.Ticket,r.Lifecycle,r.ActiveEntityId,r.ActualSpawnTick,r.BlockReason,r.MovementState);
            ActivationBlockReason block=CanActivate(r,tick);if(block!=ActivationBlockReason.None){r.BlockReason=block;EnemyDirectorRuntimeSnapshot denied=Snapshot();return new ActivationAttemptResult(false,block,before,pre,denied,null);}
            if(!pool.TryActivate(r.Ticket.TicketId,tick,queue.Quantity.ActiveCap,out EntityId entity)){r.BlockReason=ActivationBlockReason.PoolOrActiveCount;return new ActivationAttemptResult(false,r.BlockReason,before,pre,Snapshot(),null);}
            r.ActiveEntityId=entity;r.ActualSpawnTick=tick;r.BlockReason=ActivationBlockReason.None;r.MovementState=EnemyMovementState.Spawn;recentSpawnPressure.Enqueue(Tuple.Create(tick,M3PressureRules.UnitsFor(r.Ticket.Archetype)));Transition(r,SpawnTicketLifecycle.Active,tick,"activated");return new ActivationAttemptResult(true,ActivationBlockReason.None,before,pre,Snapshot(),entity);
        }
        public bool TryResolve(EntityId entity,int tick,EnemyPool pool)
        {
            SpawnTicketRecord r=records.Find(x=>x.ActiveEntityId.HasValue&&x.ActiveEntityId.Value==entity);if(r==null)return false;
            return TryResolve(entity,r.Ticket.TicketId,tick,pool);
        }
        // The ticket identity is required by new pool-reuse integrations. The legacy
        // EntityId-only overload remains for existing M3 callers that cannot recycle a
        // slot within their bounded execution path.
        public bool TryResolve(EntityId entity,ulong ticketId,int tick,EnemyPool pool)
        {
            if(pool==null||ticketId==0)return false;
            SpawnTicketRecord r=records.Find(x=>x.ActiveEntityId.HasValue&&x.ActiveEntityId.Value==entity&&x.Ticket.TicketId==ticketId);if(r==null)return false;
            pool.Resolve(entity,tick,r.Ticket.TicketId);r.ActiveEntityId=null;Transition(r,SpawnTicketLifecycle.Resolved,tick,"resolved");
            facts.Add(new M3CoreFact(M3CoreFactKind.EnemyResolved,tick,nextFactSequence++,r.Ticket.TicketId,entity,null,string.Empty));projectionFacts?.Emit(M3CoreFactKind.EnemyResolved,tick,r.Ticket.TicketId,entity,null,"resolved");return true;
        }
        public bool TryBeginHighThreatWindup(EntityId entity,int tick,out string blockedReason)
        {
            SpawnTicketRecord r=records.Find(x=>x.ActiveEntityId.HasValue&&x.ActiveEntityId.Value==entity);if(r==null){blockedReason="not-active";return false;}
            bool high=r.Ticket.Archetype==EnemyArchetype.Climber||r.Ticket.Archetype==EnemyArchetype.Breaker;if(!high){blockedReason="not-high-threat";return false;}
            if(HighThreatWindupCount()>=2){blockedReason="high-threat-windup-cap";return false;}
            r.MovementState=r.Ticket.Archetype==EnemyArchetype.Climber?EnemyMovementState.JointWindup:EnemyMovementState.AttackWindup;blockedReason=null;return true;
        }
        public WindupAttemptResult TryBeginHighThreatWindupDetailed(EntityId entity,int tick)
        {SpawnTicketRecord r=records.Find(x=>x.ActiveEntityId.HasValue&&x.ActiveEntityId.Value==entity);int before=HighThreatWindupCount();if(r==null)return new WindupAttemptResult(false,WindupBlockReason.NotActive,entity,default,default,before,before);EnemyMovementState prior=r.MovementState;bool high=r.Ticket.Archetype==EnemyArchetype.Climber||r.Ticket.Archetype==EnemyArchetype.Breaker;if(!high)return new WindupAttemptResult(false,WindupBlockReason.NotHighThreat,entity,prior,prior,before,before);if(before>=M3PressureRules.HighThreatWindupCap)return new WindupAttemptResult(false,WindupBlockReason.HighThreatWindupCap,entity,prior,prior,before,before);r.MovementState=r.Ticket.Archetype==EnemyArchetype.Climber?EnemyMovementState.JointWindup:EnemyMovementState.AttackWindup;return new WindupAttemptResult(true,WindupBlockReason.None,entity,prior,r.MovementState,before,before+1);}
        public bool TryEndHighThreatWindup(EntityId entity)
        {SpawnTicketRecord r=records.Find(x=>x.ActiveEntityId.HasValue&&x.ActiveEntityId.Value==entity);if(r==null||!IsHighThreatWindup(r))return false;r.MovementState=EnemyMovementState.AttackRecover;return true;}
        public bool RerouteUnavailableFace(FaceId unavailable,IReadOnlyList<FaceId> remaining,int faceLossSequence,int tick)
        {
            if(tick<ledgerAsOfTick||!Enum.IsDefined(typeof(FaceId),unavailable)||faceLossSequence<0)throw new ArgumentException("Invalid Face-loss context.");
            if(remaining==null||remaining.Count==0)throw new NotSupportedException("UnsupportedEntryTopology: no remaining Face.");
            foreach(FaceId face in remaining)if(face==unavailable)throw new ArgumentException("Unavailable Face remains in topology.");
            // Validate the whole candidate before changing any ticket or appending any fact.
            var proposed=new List<SpawnTicket>();
            foreach(SpawnTicketRecord record in records)
            {
                SpawnTicket x=record.Ticket;
                if((record.Lifecycle==SpawnTicketLifecycle.Pending||record.Lifecycle==SpawnTicketLifecycle.PoolWaiting)&&x.Face==unavailable)
                    x=new SpawnTicket(x.TicketId,x.QueueIndex,x.Archetype,EnemyReroutePolicy.UnspawnedTicketFace(x.TicketId,queue.Request.RunSeed,remaining),x.GroupId,Math.Max(x.ScheduledTick,tick),x.RngSeed,EnemyDirector.TickRate);
                proposed.Add(x);
            }
            proposed.Sort((a,b)=>{int order=a.ScheduledTick.CompareTo(b.ScheduledTick);return order!=0?order:a.QueueIndex.CompareTo(b.QueueIndex);});
            if(!EnemyDirector.SatisfiesRollingFaceShare(proposed,M3PressureRules.RollingWindowTicks)||!EnemyDirector.SatisfiesRollingScheduledPressure(proposed,M3PressureRules.RollingWindowTicks,M3PressureRules.RollingSpawnCapUnits))
                throw new NotSupportedException("UnsupportedEntryTopology: reroute candidate cannot satisfy the locked Face-share/pressure schedule.");
            bool changed=false;for(int n=0;n<records.Count;n++){SpawnTicketRecord r=records[n];if((r.Lifecycle!=SpawnTicketLifecycle.Pending&&r.Lifecycle!=SpawnTicketLifecycle.PoolWaiting)||r.Ticket.Face!=unavailable)continue;
                FaceId nextFace=EnemyReroutePolicy.UnspawnedTicketFace(r.Ticket.TicketId,queue.Request.RunSeed,remaining);
                SpawnTicket x=r.Ticket;r.Ticket=new SpawnTicket(x.TicketId,x.QueueIndex,x.Archetype,nextFace,x.GroupId,Math.Max(x.ScheduledTick,tick),x.RngSeed,EnemyDirector.TickRate);r.Lifecycle=SpawnTicketLifecycle.Rerouted;Transition(r,SpawnTicketLifecycle.Pending,tick,"face-reroute");changed=true;}
            return changed;
        }
        public EnemyDirectorRuntimeSnapshot Snapshot()
        {
            var a=new SpawnTicketSnapshot[records.Count];int pending=0,waiting=0,active=0,resolved=0,breakers=0;float pressure=0;int[] faceCounts=new int[3];float[] facePressure=new float[3];
            for(int n=0;n<a.Length;n++){SpawnTicketRecord r=records[n];a[n]=new SpawnTicketSnapshot(r.Ticket,r.Lifecycle,r.ActiveEntityId,r.ActualSpawnTick,r.BlockReason,r.MovementState);if(r.Lifecycle==SpawnTicketLifecycle.Pending||r.Lifecycle==SpawnTicketLifecycle.Rerouted)pending++;else if(r.Lifecycle==SpawnTicketLifecycle.PoolWaiting)waiting++;else if(r.Lifecycle==SpawnTicketLifecycle.Active){active++;float p=EnemyArchetypeCatalog.Get(r.Ticket.Archetype).Pressure;pressure+=p;faceCounts[(int)r.Ticket.Face]++;facePressure[(int)r.Ticket.Face]+=p;if(r.Ticket.Archetype==EnemyArchetype.Breaker)breakers++;}else resolved++;}
            int[] faceUnits=new int[3];for(int n=0;n<3;n++)faceUnits[n]=(int)Math.Round(facePressure[n]*4f);var activeRows=new List<ActiveEnemyPressureSnapshot>();foreach(SpawnTicketSnapshot x in a)if(x.Lifecycle==SpawnTicketLifecycle.Active&&x.ActiveEntityId.HasValue)activeRows.Add(new ActiveEnemyPressureSnapshot(x.ActiveEntityId.Value,x));var pressureRows=new SpawnPressureSample[recentSpawnPressure.Count];int pi=0;foreach(Tuple<int,int> x in recentSpawnPressure)pressureRows[pi++]=new SpawnPressureSample(x.Item1,x.Item2);
            return new EnemyDirectorRuntimeSnapshot{Cursor=FindCursor(),ImmutableQueueHash=queue.CanonicalHash(),StateHash=HashState(a),Tickets=a,PendingCount=pending,PoolWaitingCount=waiting,ActiveCount=active,ResolvedCount=resolved,ActivePressure=pressure,ActivePressureUnits=(int)Math.Round(pressure*4f),FaceActiveCounts=faceCounts,FacePressures=facePressure,FacePressureUnits=faceUnits,ActiveBreakerCount=breakers,HighThreatWindupCount=HighThreatWindupCount(),ActiveEnemies=activeRows.ToArray(),RecentSpawnPressure=pressureRows,LedgerAsOfTick=ledgerAsOfTick,NextFactSequence=nextFactSequence,AcknowledgedFactCursor=acknowledgedFactCursor};
        }
        public static EnemyDirectorRuntime Restore(EnemyDirectorQueue queue,IReadOnlyList<SpawnTicketSnapshot> saved)
        {
            var r=new EnemyDirectorRuntime(queue);if(saved==null||saved.Count!=r.records.Count)throw new FormatException("Ticket snapshot count mismatch.");
            for(int n=0;n<saved.Count;n++){SpawnTicketSnapshot x=saved[n];SpawnTicketRecord target=r.records[n];SpawnTicket original=target.Ticket;if(original.TicketId!=x.Ticket.TicketId||original.QueueIndex!=x.Ticket.QueueIndex||original.Archetype!=x.Ticket.Archetype||original.GroupId!=x.Ticket.GroupId||original.RngSeed!=x.Ticket.RngSeed||x.Ticket.ScheduledTick<original.ScheduledTick||!Enum.IsDefined(typeof(FaceId),x.Ticket.Face)||!Enum.IsDefined(typeof(SpawnTicketLifecycle),x.Lifecycle)||!Enum.IsDefined(typeof(ActivationBlockReason),x.BlockReason)||!Enum.IsDefined(typeof(EnemyMovementState),x.MovementState))throw new FormatException("Immutable ticket identity or mutable state mismatch.");if((x.Lifecycle==SpawnTicketLifecycle.Active)!=x.ActiveEntityId.HasValue||((x.Lifecycle==SpawnTicketLifecycle.Active||x.Lifecycle==SpawnTicketLifecycle.Resolved)!=x.ActualSpawnTick.HasValue)||x.ActualSpawnTick.GetValueOrDefault(int.MaxValue)<x.Ticket.ScheduledTick)throw new FormatException("Ticket lifecycle identity/timing mismatch.");target.Ticket=x.Ticket;target.Lifecycle=x.Lifecycle;target.ActiveEntityId=x.ActiveEntityId;target.ActualSpawnTick=x.ActualSpawnTick;target.BlockReason=x.BlockReason;target.MovementState=x.MovementState;}
            return r;
        }
        public M3CoreFact[] ReadFactsAfter(ulong cursor){var result=new List<M3CoreFact>();foreach(M3CoreFact fact in facts)if(fact.Sequence>cursor)result.Add(fact);return result.ToArray();}
        public void AcknowledgeFactsThrough(ulong cursor){if(cursor>=nextFactSequence)throw new ArgumentOutOfRangeException(nameof(cursor));if(cursor>acknowledgedFactCursor)acknowledgedFactCursor=cursor;}
        public M3DirectorSaveSnapshot CaptureSave()=>M3DirectorSaveSnapshot.Create(queue,Snapshot(),facts.ToArray(),recentSpawnPressure.ToArray(),nextFactSequence,acknowledgedFactCursor);
        public static EnemyDirectorRuntime Restore(M3DirectorSaveSnapshot save)
        {
            if(save==null)throw new ArgumentNullException(nameof(save));if(save.SchemaVersion!=2||save.OriginalTickets==null)throw new FormatException("Legacy Director capture has no original queue.");var queue=new EnemyDirectorQueue(save.Request,save.Quantity,(SpawnTicket[])save.OriginalTickets.Clone());if(queue.CanonicalHash()!=save.ImmutableQueueHash)throw new FormatException("Director queue hash mismatch.");var r=Restore(queue,save.Tickets);
            r.facts.Clear();foreach(M3CoreFact fact in save.Facts)r.facts.Add(fact);r.nextFactSequence=save.NextFactSequence;r.acknowledgedFactCursor=save.AcknowledgedFactCursor;r.ledgerAsOfTick=save.LedgerAsOfTick;foreach(SpawnPressureSample x in save.RecentSpawnPressure)r.recentSpawnPressure.Enqueue(Tuple.Create(x.FixedTick,x.PressureUnits));return r;
        }
        private ActivationBlockReason CanActivate(SpawnTicketRecord candidate,int tick)
        {
            int p=M3PressureRules.UnitsFor(candidate.Ticket.Archetype),total=p,face=p,faceCount=1,breakers=candidate.Ticket.Archetype==EnemyArchetype.Breaker?1:0;
            foreach(SpawnTicketRecord x in records){if(x.Lifecycle!=SpawnTicketLifecycle.Active)continue;int q=M3PressureRules.UnitsFor(x.Ticket.Archetype);total+=q;if(x.Ticket.Face==candidate.Ticket.Face){face+=q;faceCount++;}if(x.Ticket.Archetype==EnemyArchetype.Breaker)breakers++;}
            if(total>M3PressureRules.TotalActiveCapUnits)return ActivationBlockReason.TotalPressure;if(faceCount>M3PressureRules.FaceEntityCap)return ActivationBlockReason.FaceEntityCount;if(face>M3PressureRules.FaceActiveCapUnits)return ActivationBlockReason.FacePressure;if(breakers>M3PressureRules.BreakerCap)return ActivationBlockReason.BreakerCount;
            int recent=p;foreach(Tuple<int,int> x in recentSpawnPressure)recent+=x.Item2;if(recent>M3PressureRules.RollingSpawnCapUnits)return ActivationBlockReason.NewSpawnPressureWindow;return ActivationBlockReason.None;
        }
        private void PruneRecent(int tick){while(recentSpawnPressure.Count>0&&recentSpawnPressure.Peek().Item1<=tick-M3PressureRules.RollingWindowTicks)recentSpawnPressure.Dequeue();}
        private int HighThreatWindupCount(){int count=0;foreach(SpawnTicketRecord x in records)if(IsHighThreatWindup(x))count++;return count;}
        private static bool IsHighThreatWindup(SpawnTicketRecord x)=>x.Lifecycle==SpawnTicketLifecycle.Active&&M3PressureRules.IsHighThreatWindup(x.Ticket.Archetype,x.MovementState);
        private void Transition(SpawnTicketRecord r,SpawnTicketLifecycle state,int tick,string why){if(r.Lifecycle==state)return;r.Lifecycle=state;facts.Add(new M3CoreFact(M3CoreFactKind.SpawnTicketStateChanged,tick,nextFactSequence++,r.Ticket.TicketId,r.ActiveEntityId,null,why));projectionFacts?.Emit(M3CoreFactKind.SpawnTicketStateChanged,tick,r.Ticket.TicketId,r.ActiveEntityId,null,why);}
        private int FindCursor(){for(int n=0;n<records.Count;n++)if(records[n].Lifecycle==SpawnTicketLifecycle.Pending||records[n].Lifecycle==SpawnTicketLifecycle.PoolWaiting||records[n].Lifecycle==SpawnTicketLifecycle.Rerouted)return n;return records.Count;}
        private static string HashState(SpawnTicketSnapshot[] a){var b=new StringBuilder("LW.M3.STATE|");foreach(SpawnTicketSnapshot x in a)b.Append(x.Ticket.TicketId.ToString("x16")).Append(':').Append((int)x.Lifecycle).Append(':').Append(x.ActiveEntityId.HasValue?x.ActiveEntityId.Value.ToString():"-").Append(':').Append(x.ActualSpawnTick.HasValue?x.ActualSpawnTick.Value.ToString():"-").Append(':').Append((int)x.BlockReason).Append(':').Append((int)x.MovementState).Append('|');using(SHA256 sha=SHA256.Create())return EnemyArchetypeCatalog.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())));}
    }
}
