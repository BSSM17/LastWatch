using System;
using System.Collections.Generic;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Combat
{
    public readonly struct RunnerReactionRequest
    {
        public EntityId RunnerId { get; }
        public FireSequenceId FireSequenceId { get; }
        public ulong ResolvedDamageEventSequence { get; }
        public int ShotResolvedTick { get; }
        public EnemyMovementState State { get; }
        public int CooldownUntilTick { get; }
        public int ReactionsThisApproach { get; }
        public bool OriginalDirectionClear { get; }
        public bool OppositeDirectionClear { get; }
        public int ActiveEvadeMoves { get; }
        public ulong EvasionSeed { get; }
        public RunnerReactionRequest(EntityId id,FireSequenceId fire,ulong resolvedSequence,int tick,EnemyMovementState state,
            int cooldownUntil,int reactions,bool originalClear,bool oppositeClear,int activeMoves,ulong seed)
        { RunnerId=id;FireSequenceId=fire;ResolvedDamageEventSequence=resolvedSequence;ShotResolvedTick=tick;State=state;CooldownUntilTick=cooldownUntil;ReactionsThisApproach=reactions;OriginalDirectionClear=originalClear;OppositeDirectionClear=oppositeClear;ActiveEvadeMoves=activeMoves;EvasionSeed=seed; }
    }

    public readonly struct RunnerReactionDecision
    {
        public RunnerEvadePhase Phase { get; }
        public bool DamageRemainedApplied => true;
        public int ScheduledTick { get; }
        public int CooldownUntilTick { get; }
        public float LateralDistanceMeters { get; }
        public int MovementDurationTicks { get; }
        public int DirectionSign { get; }
        public string SkipReason { get; }
        internal RunnerReactionDecision(RunnerEvadePhase phase,int scheduled,int cooldown,float distance,int duration,int direction,string reason)
        {Phase=phase;ScheduledTick=scheduled;CooldownUntilTick=cooldown;LateralDistanceMeters=distance;MovementDurationTicks=duration;DirectionSign=direction;SkipReason=reason;}
    }

    public static class RunnerReactionPolicy
    {
        public const int TickRate=50;
        public static RunnerReactionDecision Resolve(RunnerReactionRequest r,ISet<string> alreadyScheduledFireSequences)
        {
            if(!r.RunnerId.IsValid||r.ResolvedDamageEventSequence==0) throw new ArgumentException("Resolved hit identity is required.");
            string key=r.RunnerId.ToString()+"/"+r.FireSequenceId.ToString();
            if(alreadyScheduledFireSequences!=null&&alreadyScheduledFireSequences.Contains(key)) return Skip("fire-sequence-consumed",r.ShotResolvedTick);
            if(r.State!=EnemyMovementState.Approach&&r.State!=EnemyMovementState.TargetActorApproach) return Skip("state",r.ShotResolvedTick);
            if(r.ShotResolvedTick<r.CooldownUntilTick) return Skip("cooldown",r.ShotResolvedTick);
            if(r.ReactionsThisApproach>=2) return Skip("approach-cap",r.ShotResolvedTick);
            if(r.ActiveEvadeMoves>=3) return Skip("global-active-cap",r.ShotResolvedTick);
            ulong seed=EnemyDirector.Mix(r.EvasionSeed^r.RunnerId.Value^r.ResolvedDamageEventSequence);
            int direction=(seed&1UL)==0? -1:1; bool clear=direction<0?r.OriginalDirectionClear:r.OppositeDirectionClear;
            if(!clear){direction=-direction;clear=direction<0?r.OriginalDirectionClear:r.OppositeDirectionClear;}
            if(!clear) return Skip("capsule-sweep-blocked",r.ShotResolvedTick);
            int delay=13+(int)((seed>>1)%10); int duration=20+(int)((seed>>8)%11); float distance=0.9f+(float)((seed>>16)%501)/1000f;
            int cooldown=110+(int)((seed>>28)%51); alreadyScheduledFireSequences?.Add(key);
            return new RunnerReactionDecision(RunnerEvadePhase.Scheduled,r.ShotResolvedTick+delay,r.ShotResolvedTick+cooldown,distance,duration,direction,null);
        }
        private static RunnerReactionDecision Skip(string why,int tick)=>new RunnerReactionDecision(RunnerEvadePhase.Skipped,tick,tick,0,0,0,why);
    }

    public readonly struct ClimberRoutePose
    {
        public RouteId Route { get; }
        public ClimberRailBranch Branch { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public bool IsUpperDeck { get; }
        public bool HasUpperLineOfSight { get; }
        public ClimberRoutePose(RouteId route,ClimberRailBranch branch,Vector3 position,Vector3 forward,bool isUpperDeck,bool upperLos)
        {Route=route;Branch=branch;Position=position;Forward=forward;IsUpperDeck=isUpperDeck;HasUpperLineOfSight=upperLos;}
    }

    public readonly struct FaceRerouteDecision
    {
        public FaceId PreviousFace { get; }
        public FaceId NewFace { get; }
        public int FaceLossSequence { get; }
        public int RerouteStartTick { get; }
        public int EarliestDamageTick { get; }
        public bool DeletesOrTeleports => false;
        public FaceRerouteDecision(FaceId oldFace,FaceId newFace,int sequence,int tick)
        {PreviousFace=oldFace;NewFace=newFace;FaceLossSequence=sequence;RerouteStartTick=tick;EarliestDamageTick=checked(tick+8*EnemyDirector.TickRate);}
    }

    public static class EnemyReroutePolicy
    {
        // DESIGN v3.0 11.4: unspawned selection is independent of the active-enemy loss sequence.
        public static FaceId UnspawnedTicketFace(ulong ticketId, ulong runSeed, IReadOnlyList<FaceId> availableFaces)
        {
            if (availableFaces == null || availableFaces.Count == 0) throw new InvalidOperationException("No remaining Face is available.");
            var sorted = new List<FaceId>(availableFaces); sorted.Sort();
            for (int n=0;n<sorted.Count;n++) if (!Enum.IsDefined(typeof(FaceId), sorted[n]) || (n>0 && sorted[n]==sorted[n-1])) throw new ArgumentException("Invalid remaining Faces.");
            return sorted[(int)(EnemyDirector.Mix(ticketId ^ runSeed) % (ulong)sorted.Count)];
        }
        public static FaceRerouteDecision FaceUnavailable(EntityId id,FaceId oldFace,int faceLossSequence,int tick,IReadOnlyList<FaceId> availableFaces)
        {
            if(availableFaces==null||availableFaces.Count==0) throw new InvalidOperationException("No remaining Face is available.");
            var sorted=new List<FaceId>(availableFaces);sorted.Sort(); ulong choice=EnemyDirector.Mix(id.Value^(ulong)(uint)faceLossSequence);
            return new FaceRerouteDecision(oldFace,sorted[(int)(choice%(ulong)sorted.Count)],faceLossSequence,tick);
        }
    }

    public enum EnemyPoolEventKind { Prewarmed, Activated, ShortageDelayed, Resolved, CorpsePresentationRecycled }
    public readonly struct EnemyPoolEvent { public EnemyPoolEventKind Kind{get;} public int Tick{get;} public ulong TicketId{get;} public EntityId? EntityId{get;} public EnemyPoolEvent(EnemyPoolEventKind kind,int tick,ulong ticket,EntityId? entity){Kind=kind;Tick=tick;TicketId=ticket;EntityId=entity;} }
    public readonly struct EnemyPoolSnapshot
    {
        public int PrewarmCapacity{get;} public int ActiveCount{get;} public int AvailableCount{get;} public int VisibleCorpseCount{get;}
        public int RetainedDeathFactCount{get;} public int CombatInstantiateCount{get;} public int CombatDestroyCount{get;}
        internal EnemyPoolSnapshot(int active,int corpse,int deaths){PrewarmCapacity=20;ActiveCount=active;AvailableCount=20-active;VisibleCorpseCount=corpse;RetainedDeathFactCount=deaths;CombatInstantiateCount=0;CombatDestroyCount=0;}
    }
    public sealed class EnemyPool
    {
        private readonly Queue<EntityId> available=new Queue<EntityId>(); private readonly HashSet<ulong> active=new HashSet<ulong>(); private readonly List<EnemyPoolEvent> events=new List<EnemyPoolEvent>(); private int corpses,deaths;
        public EnemyPool(ulong firstEntityId=0x1000){for(ulong n=0;n<20;n++)available.Enqueue(new EntityId(firstEntityId+n));events.Add(new EnemyPoolEvent(EnemyPoolEventKind.Prewarmed,0,0,null));}
        public IReadOnlyList<EnemyPoolEvent> Events=>events.AsReadOnly(); public EnemyPoolSnapshot Snapshot()=>new EnemyPoolSnapshot(active.Count,corpses,deaths);
        public EnemyPoolSaveSnapshot CaptureSave()=>new EnemyPoolSaveSnapshot(available.ToArray(),new List<ulong>(active).ToArray(),corpses,deaths,events.ToArray());
        public static EnemyPool Restore(EnemyPoolSaveSnapshot save){if(save==null)throw new ArgumentNullException(nameof(save));var pool=new EnemyPool();pool.available.Clear();pool.active.Clear();pool.events.Clear();foreach(EntityId id in save.Available)pool.available.Enqueue(id);foreach(ulong id in save.Active)pool.active.Add(id);pool.corpses=save.VisibleCorpses;pool.deaths=save.DeathFacts;pool.events.AddRange(save.Events);if(pool.available.Count+pool.active.Count!=20||pool.active.Count>14)throw new FormatException("Enemy pool save violates capacity.");return pool;}
        public bool TryActivate(ulong ticketId,int tick,int directorActiveCap,out EntityId entity)
        {
            int cap=Math.Min(14,directorActiveCap); if(active.Count>=cap||available.Count==0){entity=default;events.Add(new EnemyPoolEvent(EnemyPoolEventKind.ShortageDelayed,tick,ticketId,null));return false;}
            entity=available.Dequeue();active.Add(entity.Value);events.Add(new EnemyPoolEvent(EnemyPoolEventKind.Activated,tick,ticketId,entity));return true;
        }
        public void Resolve(EntityId entity,int tick,ulong ticketId)
        {
            if(!active.Remove(entity.Value))throw new InvalidOperationException("Entity is not active.");available.Enqueue(entity);deaths++;corpses++;
            events.Add(new EnemyPoolEvent(EnemyPoolEventKind.Resolved,tick,ticketId,entity));if(corpses>4){corpses=4;events.Add(new EnemyPoolEvent(EnemyPoolEventKind.CorpsePresentationRecycled,tick,ticketId,entity));}
        }
    }
    public sealed class EnemyPoolSaveSnapshot
    {public EntityId[] Available{get;}public ulong[] Active{get;}public int VisibleCorpses{get;}public int DeathFacts{get;}public EnemyPoolEvent[] Events{get;}internal EnemyPoolSaveSnapshot(EntityId[] available,ulong[] active,int corpses,int deaths,EnemyPoolEvent[] events){Available=(EntityId[])available.Clone();Active=(ulong[])active.Clone();Array.Sort(Active);VisibleCorpses=corpses;DeathFacts=deaths;Events=(EnemyPoolEvent[])events.Clone();}}
}
