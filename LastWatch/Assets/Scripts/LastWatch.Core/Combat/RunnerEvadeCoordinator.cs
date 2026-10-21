using System;
using System.Collections.Generic;
using LastWatch.Core.Identity;

namespace LastWatch.Core.Combat
{
    public enum RunnerCoordinationSkipReason { None, FireSequenceConsumed, ReservationIllegal, ExecutionStateChanged, GlobalActiveCap }

    public readonly struct RunnerReservationSnapshot
    {
        public EntityId RunnerId{get;} public FireSequenceId FireSequenceId{get;} public RunnerReactionDecision Decision{get;} public RunnerEvadePhase Phase{get;} public RunnerCoordinationSkipReason SkipReason{get;} public int CooldownUntilTick{get;} public int ReactionsThisApproach{get;}
        internal RunnerReservationSnapshot(EntityId id,FireSequenceId fire,RunnerReactionDecision decision,RunnerEvadePhase phase,RunnerCoordinationSkipReason reason,int cooldown,int reactions){RunnerId=id;FireSequenceId=fire;Decision=decision;Phase=phase;SkipReason=reason;CooldownUntilTick=cooldown;ReactionsThisApproach=reactions;}
    }

    internal sealed class RunnerReservationRecord { internal RunnerReservationSnapshot Value; internal RunnerEvadeRuntime Runtime; }

    public sealed class RunnerCoordinatorSaveSnapshot
    {
        public RunnerReservationSnapshot[] Reservations{get;private set;} public string[] ConsumedKeys{get;private set;}
        internal static RunnerCoordinatorSaveSnapshot Create(RunnerReservationSnapshot[] values,string[] consumed)=>new RunnerCoordinatorSaveSnapshot{Reservations=(RunnerReservationSnapshot[])values.Clone(),ConsumedKeys=(string[])consumed.Clone()};
    }

    public sealed class RunnerEvadeCoordinator
    {
        private readonly HashSet<string> consumed=new HashSet<string>();private readonly List<RunnerReservationRecord> reservations=new List<RunnerReservationRecord>();private M3FactEmitter facts;
        public RunnerEvadeCoordinator(){}
        public RunnerEvadeCoordinator(M3FactEmitter authoritativeFacts){facts=authoritativeFacts??throw new ArgumentNullException(nameof(authoritativeFacts));}
        internal void AttachFactEmitter(M3FactEmitter authoritativeFacts)=>facts=authoritativeFacts??throw new ArgumentNullException(nameof(authoritativeFacts));
        public RunnerReservationSnapshot Reserve(RunnerReactionRequest request)
        {string key=Key(request.RunnerId,request.FireSequenceId);if(consumed.Contains(key))return new RunnerReservationSnapshot(request.RunnerId,request.FireSequenceId,default,RunnerEvadePhase.Skipped,RunnerCoordinationSkipReason.FireSequenceConsumed,request.CooldownUntilTick,request.ReactionsThisApproach);var normalized=new RunnerReactionRequest(request.RunnerId,request.FireSequenceId,request.ResolvedDamageEventSequence,request.ShotResolvedTick,request.State,request.CooldownUntilTick,request.ReactionsThisApproach,request.OriginalDirectionClear,request.OppositeDirectionClear,0,request.EvasionSeed);RunnerReactionDecision d=RunnerReactionPolicy.Resolve(normalized,consumed);RunnerCoordinationSkipReason why=d.Phase==RunnerEvadePhase.Scheduled?RunnerCoordinationSkipReason.None:RunnerCoordinationSkipReason.ReservationIllegal;var value=new RunnerReservationSnapshot(request.RunnerId,request.FireSequenceId,d,d.Phase,why,d.CooldownUntilTick,request.ReactionsThisApproach+(d.Phase==RunnerEvadePhase.Scheduled?1:0));reservations.Add(new RunnerReservationRecord{Value=value});facts?.Emit(M3CoreFactKind.RunnerReactionAttempted,request.ShotResolvedTick,null,request.RunnerId,request.FireSequenceId,d.Phase+":"+why);return value;}
        public RunnerReservationSnapshot[] ExecuteDue(int tick,IReadOnlyDictionary<ulong,EnemyMovementState> states)
        {var due=reservations.FindAll(x=>x.Value.Phase==RunnerEvadePhase.Scheduled&&x.Value.Decision.ScheduledTick<=tick);due.Sort((a,b)=>{int c=a.Value.Decision.ScheduledTick.CompareTo(b.Value.Decision.ScheduledTick);return c!=0?c:a.Value.RunnerId.CompareTo(b.Value.RunnerId);});int moving=0;foreach(RunnerReservationRecord x in reservations)if(x.Value.Phase==RunnerEvadePhase.Moving)moving++;var changed=new List<RunnerReservationSnapshot>();foreach(RunnerReservationRecord x in due){EnemyMovementState state=states!=null&&states.TryGetValue(x.Value.RunnerId.Value,out EnemyMovementState s)?s:EnemyMovementState.Dead;if(state!=EnemyMovementState.Approach&&state!=EnemyMovementState.TargetActorApproach){Set(x,RunnerEvadePhase.Skipped,RunnerCoordinationSkipReason.ExecutionStateChanged,tick);changed.Add(x.Value);continue;}if(moving>=3){Set(x,RunnerEvadePhase.Skipped,RunnerCoordinationSkipReason.GlobalActiveCap,tick);changed.Add(x.Value);continue;}x.Runtime=new RunnerEvadeRuntime(x.Value.RunnerId,x.Value.FireSequenceId,x.Value.Decision);x.Runtime.TryAdvance(tick,state,out RunnerEvadeTransition transition);Set(x,RunnerEvadePhase.Moving,RunnerCoordinationSkipReason.None,tick);moving++;changed.Add(x.Value);}return changed.ToArray();}
        public RunnerReservationSnapshot[] Snapshot(){var result=new RunnerReservationSnapshot[reservations.Count];for(int n=0;n<result.Length;n++)result[n]=reservations[n].Value;return result;}
        public RunnerReservationSnapshot[] AdvanceMoving(int tick,IReadOnlyDictionary<ulong,EnemyMovementState> states)
        {var changed=new List<RunnerReservationSnapshot>();foreach(RunnerReservationRecord x in reservations){if(x.Value.Phase!=RunnerEvadePhase.Moving||x.Runtime==null)continue;EnemyMovementState state=states!=null&&states.TryGetValue(x.Value.RunnerId.Value,out EnemyMovementState s)?s:EnemyMovementState.Dead;if(state==EnemyMovementState.Dead){Set(x,RunnerEvadePhase.Skipped,RunnerCoordinationSkipReason.ExecutionStateChanged,tick);changed.Add(x.Value);continue;}if(x.Runtime.TryAdvance(tick,state,out RunnerEvadeTransition transition)){Set(x,transition.Current,transition.Current==RunnerEvadePhase.Skipped?RunnerCoordinationSkipReason.ExecutionStateChanged:RunnerCoordinationSkipReason.None,tick);changed.Add(x.Value);}}return changed.ToArray();}
        public RunnerCoordinatorSaveSnapshot CaptureSave(){var keys=new string[consumed.Count];consumed.CopyTo(keys);Array.Sort(keys,StringComparer.Ordinal);return RunnerCoordinatorSaveSnapshot.Create(Snapshot(),keys);}
        public static RunnerEvadeCoordinator Restore(RunnerCoordinatorSaveSnapshot save)
        {if(save==null)throw new ArgumentNullException(nameof(save));var result=new RunnerEvadeCoordinator();foreach(string key in save.ConsumedKeys??Array.Empty<string>())if(!result.consumed.Add(key))throw new FormatException("Duplicate consumed Runner key.");foreach(RunnerReservationSnapshot value in save.Reservations??Array.Empty<RunnerReservationSnapshot>()){if(!value.RunnerId.IsValid||!value.FireSequenceId.IsValid)throw new FormatException("Runner reservation identity is invalid.");var record=new RunnerReservationRecord{Value=value};if(value.Phase==RunnerEvadePhase.Moving){record.Runtime=new RunnerEvadeRuntime(value.RunnerId,value.FireSequenceId,value.Decision);record.Runtime.TryAdvance(value.Decision.ScheduledTick,EnemyMovementState.Approach,out RunnerEvadeTransition ignored);}result.reservations.Add(record);}return result;}
        private void Set(RunnerReservationRecord x,RunnerEvadePhase phase,RunnerCoordinationSkipReason why,int tick){RunnerReservationSnapshot v=x.Value;x.Value=new RunnerReservationSnapshot(v.RunnerId,v.FireSequenceId,v.Decision,phase,why,v.CooldownUntilTick,v.ReactionsThisApproach);M3CoreFactKind kind=phase==RunnerEvadePhase.Moving?M3CoreFactKind.RunnerEvadeStarted:M3CoreFactKind.RunnerEvadeEnded;facts?.Emit(kind,tick,null,v.RunnerId,v.FireSequenceId,phase+":"+why);}
        private static string Key(EntityId id,FireSequenceId fire)=>id.ToString()+"/"+fire.ToString();
    }
}
