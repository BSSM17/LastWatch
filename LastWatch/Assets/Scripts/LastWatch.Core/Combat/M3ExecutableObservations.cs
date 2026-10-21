using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Combat
{
    public readonly struct ClimberPoseObservation
    {
        public FaceId Face { get; }
        public RouteId Route { get; }
        public ClimberRailBranch Branch { get; }
        public int PoseIndex { get; }
        public Vector3 Position { get; }
        public bool IsUpperDeck { get; }
        public bool HasCanonicalUpperLineOfSight { get; }
        public bool PhysicsLinecastExecuted { get; }
        public EntityId? BlockingEntityId { get; }
        public int CanonicalObserverCount { get; }
        public ClimberPoseObservation(FaceId face, RouteId route, ClimberRailBranch branch, int index,
            Vector3 position, bool isUpperDeck, bool hasLos, int observers, bool executed=false, EntityId? blocker=null)
        { Face=face;Route=route;Branch=branch;PoseIndex=index;Position=position;IsUpperDeck=isUpperDeck;HasCanonicalUpperLineOfSight=hasLos;CanonicalObserverCount=observers;PhysicsLinecastExecuted=executed;BlockingEntityId=blocker; }
    }

    public static class ClimberExecutableCatalog
    {
        public static ClimberPoseObservation[] CreateAllRequiredPoses()
        {
            TowerGeometryFixture tower=TowerGeometryFixture.CreateNormal(); var values=new List<ClimberPoseObservation>();
            foreach(RouteDescriptor route in tower.Routes)
            {
                if(route.Kind!=RouteKind.ClimberRail)continue;
                Vector3[] samples=route.Sample(TowerGeometryFixture.LineOfSightSampleSpacing);
                foreach(ClimberRailBranch branch in (ClimberRailBranch[])Enum.GetValues(typeof(ClimberRailBranch)))
                {
                    Vector3 faceDirection=TowerGeometryFixture.DirectionFor(route.Face); Vector3 lateral=Vector3.Cross(Vector3.up,faceDirection).normalized*BranchOffset(branch);
                    AttackPointId target=ClimbAttackPoint(route.Face); StationId[] observers=TowerLineOfSightContract.GetCanonicalStations(target);
                    for(int n=0;n<samples.Length;n++)
                    {
                        Vector3 position=samples[n]+lateral; bool upper=position.y>=TowerGeometryFixture.UpperDeckY;
                        values.Add(new ClimberPoseObservation(route.Face,route.Id,branch,n,position,upper,false,observers.Length));
                    }
                }
            }
            return values.ToArray();
        }
        private static float BranchOffset(ClimberRailBranch branch)=>branch==ClimberRailBranch.Left?-0.45f:branch==ClimberRailBranch.Right?0.45f:0f;
        private static AttackPointId ClimbAttackPoint(FaceId face)=>face==FaceId.FaceA?AttackPointId.AP_A_C:face==FaceId.FaceB?AttackPointId.AP_B_C:AttackPointId.AP_C_C;
    }

    public static class ClimberPhysicsLineOfSightProbe
    {
        public static ClimberPoseObservation[] Execute(M2GrayboxBuilder populatedGraybox,int layerMask=Physics.DefaultRaycastLayers)
        {
            if(populatedGraybox==null||populatedGraybox.Geometry==null)throw new ArgumentException("A populated M2 graybox is required.",nameof(populatedGraybox));
            TowerGeometryFixture tower=populatedGraybox.Geometry;ClimberPoseObservation[] source=ClimberExecutableCatalog.CreateAllRequiredPoses();var result=new ClimberPoseObservation[source.Length];Physics.SyncTransforms();
            for(int n=0;n<source.Length;n++)
            {
                ClimberPoseObservation pose=source[n];StationId[] ids=TowerLineOfSightContract.GetCanonicalStations(ClimbPoint(pose.Face));bool clear=false;EntityId? blocker=null;
                foreach(StationId id in ids)
                {
                    StationDescriptor station=FindStation(tower,id);Vector3 delta=pose.Position-station.EyePosition;float distance=delta.magnitude;RaycastHit[] hits=Physics.RaycastAll(station.EyePosition,delta.normalized,distance+0.02f,layerMask,QueryTriggerInteraction.Ignore);bool blocked=false;float nearest=float.MaxValue;
                    foreach(RaycastHit hit in hits){if(hit.collider==null||!hit.collider.transform.IsChildOf(populatedGraybox.transform))continue;M2GrayboxColliderTag tag=hit.collider.GetComponent<M2GrayboxColliderTag>();if(tag==null||tag.Kind==M2GrayboxColliderKind.AttackCapsule)continue;blocked=true;if(hit.distance<nearest){nearest=hit.distance;blocker=new EntityId((ulong)(uint)hit.collider.GetInstanceID());}}
                    if(!blocked){clear=true;blocker=null;break;}
                }
                result[n]=new ClimberPoseObservation(pose.Face,pose.Route,pose.Branch,pose.PoseIndex,pose.Position,pose.IsUpperDeck,!pose.IsUpperDeck&&clear,ids.Length,true,clear?null:blocker);
            }return result;
        }
        private static StationDescriptor FindStation(TowerGeometryFixture tower,StationId id){foreach(StationDescriptor x in tower.Stations)if(x.Id==id)return x;throw new InvalidOperationException("Station missing.");}
        private static AttackPointId ClimbPoint(FaceId face)=>face==FaceId.FaceA?AttackPointId.AP_A_C:face==FaceId.FaceB?AttackPointId.AP_B_C:AttackPointId.AP_C_C;
    }

    public readonly struct BurstShotObservation
    {
        public int ShotIndex{get;} public int ResolutionTick{get;} public ulong DamageEventSequence{get;} public bool DamageApplied{get;} public bool EvadeMovementStarted{get;}
        public BurstShotObservation(int index,int tick,ulong sequence,bool applied,bool evadeStarted){ShotIndex=index;ResolutionTick=tick;DamageEventSequence=sequence;DamageApplied=applied;EvadeMovementStarted=evadeStarted;}
    }
    public readonly struct RunnerBurstObservation
    {
        public FireSequenceId FireSequenceId{get;} public int ReactionScheduledTick{get;} public BurstShotObservation[] Shots{get;}
        public RunnerBurstObservation(FireSequenceId fire,int scheduled,BurstShotObservation[] shots){FireSequenceId=fire;ReactionScheduledTick=scheduled;Shots=(BurstShotObservation[])shots.Clone();}
    }
    public readonly struct CrossingRayObservation
    {
        public int AttemptCount{get;} public int OrdinaryDamageAppliedCount{get;} public int HitboxShrinkCount{get;} public int PriorShotCancellationCount{get;}
        public float CapsuleRadiusBefore{get;} public float CapsuleRadiusAfter{get;} public float CapsuleHeightBefore{get;} public float CapsuleHeightAfter{get;}
        public CrossingRayObservation(int attempts,int applied,int shrink,int cancelled,float radius=0.45f,float height=1.8f){AttemptCount=attempts;OrdinaryDamageAppliedCount=applied;HitboxShrinkCount=shrink;PriorShotCancellationCount=cancelled;CapsuleRadiusBefore=radius;CapsuleRadiusAfter=radius;CapsuleHeightBefore=height;CapsuleHeightAfter=height;}
    }

    public static class RunnerExecutableProbe
    {
        public static RunnerBurstObservation ExecuteFixedAssaultRifleBurst(EntityId runnerId,FireSequenceId fire,int firstTick,ulong firstSequence)
        {
            if(!runnerId.IsValid||!fire.IsValid||firstSequence==0||firstTick<0)throw new ArgumentException("Stable burst identity is required.");
            var fixture=new RunStateFixture{RunValue=91,FixedTick=firstTick,NextEventSequence=firstSequence,Damageables=new[]{new DamageableFixture{EntityValue=runnerId.Value,Kind=DamageableKind.Enemy,HitPoints=1000f}}};RunState state=RunStateFactory.CreateFixture(fixture);var resolver=new DamageResolver(state);var consumed=new HashSet<string>();int reactionTick=0;var shots=new BurstShotObservation[3];
            for(int n=0;n<3;n++)
            {
                int targetTick=firstTick+n*5;while(state.FixedTick<targetTick)state.AdvanceOneTick();ulong sequence=firstSequence+(ulong)n;
                var packet=new DamagePacket(sequence,targetTick,new EntityId(7),new EntityId(7),runnerId,DamageKind.Ballistic,18f,Vector3.zero,Vector3.left,fire);DamageReceipt receipt=resolver.Apply(packet);
                if(n==0){RunnerReactionDecision decision=RunnerReactionPolicy.Resolve(new RunnerReactionRequest(runnerId,fire,sequence,targetTick,EnemyMovementState.Approach,0,0,true,true,0,43),consumed);reactionTick=decision.ScheduledTick;}
                shots[n]=new BurstShotObservation(n,targetTick,sequence,receipt.Status==DamageApplyStatus.Applied,targetTick>=reactionTick);
            }return new RunnerBurstObservation(fire,reactionTick,shots);
        }
        public static CrossingRayObservation ExecuteCrossingRays(int count)
        {
            if(count<0)throw new ArgumentOutOfRangeException(nameof(count));const float radius=0.45f,height=1.8f;EntityId runner=new EntityId(801);var fixture=new RunStateFixture{RunValue=92,NextEventSequence=1,Damageables=new[]{new DamageableFixture{EntityValue=runner.Value,Kind=DamageableKind.Enemy,HitPoints=Math.Max(1,count+1)}}};RunState state=RunStateFactory.CreateFixture(fixture);var resolver=new DamageResolver(state);int applied=0;
            for(int n=0;n<count;n++){float y=0.1f+(n%800)/1000f;Vector3 origin=new Vector3(-2f,y,0f);Vector3 end=new Vector3(2f,y,0f);if(!SegmentCrossesVerticalCapsule(origin,end,Vector3.zero,height,radius))continue;var packet=new DamagePacket((ulong)n+1,0,new EntityId(7),new EntityId(7),runner,DamageKind.Ballistic,1f,Vector3.zero,origin,new FireSequenceId((ulong)n+1));if(resolver.Apply(packet).Status==DamageApplyStatus.Applied)applied++;}
            return new CrossingRayObservation(count,applied,0,0,radius,height);
        }
        private static bool SegmentCrossesVerticalCapsule(Vector3 start,Vector3 end,Vector3 center,float height,float radius){Vector3 axisA=center+Vector3.up*radius;Vector3 axisB=center+Vector3.up*(height-radius);return SegmentSegmentDistanceSquared(start,end,axisA,axisB)<=radius*radius;}
        private static float SegmentSegmentDistanceSquared(Vector3 p1,Vector3 q1,Vector3 p2,Vector3 q2){Vector3 d1=q1-p1,d2=q2-p2,r=p1-p2;float a=Vector3.Dot(d1,d1),e=Vector3.Dot(d2,d2),f=Vector3.Dot(d2,r),s,t;if(a<=1e-6f&&e<=1e-6f)return r.sqrMagnitude;if(a<=1e-6f){s=0;t=Mathf.Clamp01(f/e);}else{float c=Vector3.Dot(d1,r);if(e<=1e-6f){t=0;s=Mathf.Clamp01(-c/a);}else{float b=Vector3.Dot(d1,d2),den=a*e-b*b;s=den!=0?Mathf.Clamp01((b*f-c*e)/den):0;t=(b*s+f)/e;if(t<0){t=0;s=Mathf.Clamp01(-c/a);}else if(t>1){t=1;s=Mathf.Clamp01((b-c)/a);}}}Vector3 delta=r+d1*s-d2*t;return delta.sqrMagnitude;}
    }

    public enum UpperOnlyReplayStatus { InconclusiveCombatFixtureUnavailable }
    public readonly struct UpperOnlyReplayObservation
    {
        public LastWatchDifficulty Difficulty{get;} public ulong RunSeed{get;} public UpperOnlyReplayStatus Status{get;} public string Reason{get;}
        public UpperOnlyReplayObservation(LastWatchDifficulty difficulty,ulong seed){Difficulty=difficulty;RunSeed=seed;Status=UpperOnlyReplayStatus.InconclusiveCombatFixtureUnavailable;Reason="M3 has no approved player firing/partner M4 replay driver; lossless outcome must not be inferred from tickets.";}
    }
    public readonly struct ActiveCapStressObservation
    {
        public int Requested{get;} public int Activated{get;} public int Delayed{get;} public int PeakActive{get;} public bool HardCapRespected=>PeakActive<=14;
        public ActiveCapStressObservation(int requested,int activated,int delayed,int peak){Requested=requested;Activated=activated;Delayed=delayed;PeakActive=peak;}
    }
    public static class M3RuntimeStressProbe
    {
        public static ActiveCapStressObservation ExecuteActiveCap14()
        {var pool=new EnemyPool();int activated=0,delayed=0,peak=0;for(int n=0;n<20;n++){EntityId entity;if(pool.TryActivate((ulong)n+1,0,14,out entity))activated++;else delayed++;peak=Math.Max(peak,pool.Snapshot().ActiveCount);}return new ActiveCapStressObservation(20,activated,delayed,peak);}
    }
}
