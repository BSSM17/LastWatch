using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Core.Combat
{
    // Append-only M3 facts, isolated downstream projection, and authoritative recording.
    public interface IM3CoreFactSink { void Observe(M3CoreFact fact); }
    public sealed class M3FactEmitter
    {
        private readonly List<M3CoreFact> facts=new List<M3CoreFact>(); private ulong nextSequence=1; private ulong acknowledgedCursor;
        public IReadOnlyList<M3CoreFact> Snapshot()=>facts.AsReadOnly();
        public M3CoreFact Emit(M3CoreFactKind kind,int tick,ulong? ticketId=null,EntityId? entityId=null,FireSequenceId? fire=null,string detail=null)
        {var fact=new M3CoreFact(kind,tick,nextSequence++,ticketId,entityId,fire,detail);facts.Add(fact);return fact;}
        public void ProjectTo(IM3CoreFactSink sink){if(sink==null)return;foreach(M3CoreFact fact in facts)sink.Observe(fact);}
        public M3CoreFact[] ReadAfter(ulong cursor){var result=new List<M3CoreFact>();foreach(M3CoreFact x in facts)if(x.Sequence>cursor)result.Add(x);return result.ToArray();}
        public void AcknowledgeThrough(ulong cursor){if(cursor>=nextSequence)throw new ArgumentOutOfRangeException(nameof(cursor));if(cursor>acknowledgedCursor)acknowledgedCursor=cursor;}
        public M3FactJournalSaveSnapshot CaptureSave()=>new M3FactJournalSaveSnapshot(facts.ToArray(),nextSequence,acknowledgedCursor,GameplayHash());
        public static M3FactEmitter Restore(M3FactJournalSaveSnapshot save){if(save==null)throw new ArgumentNullException(nameof(save));var result=new M3FactEmitter();result.facts.Clear();ulong expected=1;foreach(M3CoreFact fact in save.Facts??Array.Empty<M3CoreFact>()){if(fact.Sequence!=expected++)throw new FormatException("Fact journal sequence gap or duplicate.");result.facts.Add(fact);}if(save.NextSequence!=expected||save.AcknowledgedCursor>=save.NextSequence)throw new FormatException("Fact journal cursor is invalid.");result.nextSequence=save.NextSequence;result.acknowledgedCursor=save.AcknowledgedCursor;if(result.GameplayHash()!=save.GameplayHash)throw new FormatException("Fact journal hash mismatch.");return result;}
        public string GameplayHash()
        {
            var b=new StringBuilder("LW.M3.FACTS|");foreach(M3CoreFact x in facts)b.Append((int)x.Kind).Append(':').Append(x.FixedTick).Append(':').Append(x.Sequence).Append(':').Append(x.TicketId.HasValue?x.TicketId.Value.ToString("x16"):"-").Append(':').Append(x.EntityId.HasValue?x.EntityId.Value.ToString():"-").Append(':').Append(x.FireSequenceId.HasValue?x.FireSequenceId.Value.ToString():"-").Append(':').Append(x.Detail.Length).Append('#').Append(x.Detail).Append('|');
            using(SHA256 sha=SHA256.Create())return EnemyArchetypeCatalog.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())));
        }
    }
    public sealed class M3FactJournalSaveSnapshot
    {public M3CoreFact[] Facts{get;}public ulong NextSequence{get;}public ulong AcknowledgedCursor{get;}public string GameplayHash{get;}public M3FactJournalSaveSnapshot(M3CoreFact[] facts,ulong next,ulong acknowledged,string hash){Facts=(M3CoreFact[])(facts??Array.Empty<M3CoreFact>()).Clone();NextSequence=next;AcknowledgedCursor=acknowledged;GameplayHash=hash??throw new ArgumentNullException(nameof(hash));}}
    public readonly struct SinkIsolationObservation
    {
        public string WithoutSinkHash{get;} public string WithSinkHash{get;} public bool GameplayInvariant=>WithoutSinkHash==WithSinkHash;
        public SinkIsolationObservation(string none,string sink){WithoutSinkHash=none;WithSinkHash=sink;}
    }
    public static class M3DataIsolationProbe
    {
        public static SinkIsolationObservation Execute(M3FactEmitter emitter,IM3CoreFactSink sink)
        {if(emitter==null)throw new ArgumentNullException(nameof(emitter));string before=emitter.GameplayHash();try{emitter.ProjectTo(sink);}catch(Exception){/* sink failure is presentation/data failure only */}return new SinkIsolationObservation(before,emitter.GameplayHash());}
    }

    // Typed authority boundary: callers supply committed Core receipts/decisions, not an
    // arbitrary telemetry kind. Projection remains downstream and cannot alter decisions.
    public sealed class M3AuthoritativeFactRecorder
    {
        private readonly M3FactEmitter emitter;
        public M3AuthoritativeFactRecorder(M3FactEmitter emitter){this.emitter=emitter??throw new ArgumentNullException(nameof(emitter));}
        public void RecordTicketTransition(SpawnTicketSnapshot snapshot,int tick)
        {emitter.Emit(M3CoreFactKind.SpawnTicketStateChanged,tick,snapshot.Ticket.TicketId,snapshot.ActiveEntityId,null,snapshot.Lifecycle.ToString());}
        public void RecordFireSequence(FireSequenceId fire,int tick,IReadOnlyList<DamageReceipt> committedReceipts)
        {if(!fire.IsValid||committedReceipts==null)throw new ArgumentException("Committed fire receipts are required.");int applied=0;foreach(DamageReceipt x in committedReceipts)if(x.Status==DamageApplyStatus.Applied)applied++;emitter.Emit(M3CoreFactKind.FireSequenceResolved,tick,null,null,fire,"applied="+applied+"/"+committedReceipts.Count);}
        public void RecordEnemyResolved(SpawnTicketSnapshot snapshot,EntityId entity,int tick)
        {if(snapshot.Lifecycle!=SpawnTicketLifecycle.Resolved)throw new InvalidOperationException("Enemy resolution must already be committed.");emitter.Emit(M3CoreFactKind.EnemyResolved,tick,snapshot.Ticket.TicketId,entity,null,"resolved");}
        public void RecordDefenseSummary(int tick,EnemyDirectorRuntimeSnapshot committedSnapshot)
        {if(committedSnapshot==null)throw new ArgumentNullException(nameof(committedSnapshot));emitter.Emit(M3CoreFactKind.DefenseSummary,tick,null,null,null,"resolved="+committedSnapshot.ResolvedCount+";pending="+committedSnapshot.PendingCount+";active="+committedSnapshot.ActiveCount+";waiting="+committedSnapshot.PoolWaitingCount+";state="+committedSnapshot.StateHash);}
        public void RecordRunnerDecision(RunnerReactionRequest request,RunnerReactionDecision decision)
        {emitter.Emit(M3CoreFactKind.RunnerReactionAttempted,request.ShotResolvedTick,null,request.RunnerId,request.FireSequenceId,decision.Phase+":"+(decision.SkipReason??"scheduled"));}
        public void RecordRunnerTransition(RunnerEvadeTransition transition)
        {bool start=transition.Previous==RunnerEvadePhase.Scheduled&&transition.Current==RunnerEvadePhase.Moving;bool end=transition.Previous==RunnerEvadePhase.Moving&&transition.Current==RunnerEvadePhase.Completed;bool skipped=transition.Previous==RunnerEvadePhase.Scheduled&&transition.Current==RunnerEvadePhase.Skipped;if(!transition.RunnerId.IsValid||!transition.FireSequenceId.IsValid||transition.FixedTick<0||(!start&&!end&&!skipped))throw new InvalidOperationException("Illegal Runner evade transition.");M3CoreFactKind kind=start?M3CoreFactKind.RunnerEvadeStarted:M3CoreFactKind.RunnerEvadeEnded;emitter.Emit(kind,transition.FixedTick,null,transition.RunnerId,transition.FireSequenceId,transition.Current.ToString());}
        public void RecordRunnerHitDuringEvade(DamageReceipt committedReceipt,RunnerEvadeRuntime runtime,int tick)
        {if(runtime==null||committedReceipt.Status!=DamageApplyStatus.Applied||committedReceipt.Target!=runtime.RunnerId||!runtime.IsMovingAt(tick))throw new InvalidOperationException("A committed hit on the actively moving Runner is required.");emitter.Emit(M3CoreFactKind.RunnerHitDuringEvade,tick,null,runtime.RunnerId,runtime.FireSequenceId,"damage-applied");}
    }
}
