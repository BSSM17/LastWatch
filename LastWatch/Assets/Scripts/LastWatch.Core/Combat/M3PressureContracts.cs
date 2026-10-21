using System;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;

namespace LastWatch.Core.Combat
{
    public static class M3PressureRules
    {
        public const int TotalActiveCapUnits=64, FaceActiveCapUnits=28, RollingSpawnCapUnits=28;
        public const int FaceEntityCap=6, BreakerCap=2, HighThreatWindupCap=2, RollingWindowTicks=500;
        public static int UnitsFor(EnemyArchetype archetype)=>archetype==EnemyArchetype.Walker?4:archetype==EnemyArchetype.Runner?5:archetype==EnemyArchetype.Climber?7:12;
        public static bool IsHighThreatWindup(EnemyArchetype archetype,EnemyMovementState state)=>(archetype==EnemyArchetype.Breaker&&state==EnemyMovementState.AttackWindup)||(archetype==EnemyArchetype.Climber&&state==EnemyMovementState.JointWindup);
    }

    public readonly struct M3TicketFixtureDefinition { public EnemyArchetype Archetype{get;}public FaceId Face{get;}public int ScheduledTick{get;}public M3TicketFixtureDefinition(EnemyArchetype archetype,FaceId face,int tick){if(tick<0)throw new ArgumentOutOfRangeException(nameof(tick));Archetype=archetype;Face=face;ScheduledTick=tick;} }
    public static class M3RuntimeFixtureFactory
    {
        public static EnemyDirectorQueue CreateQueue(params M3TicketFixtureDefinition[] definitions)
        {if(definitions==null||definitions.Length==0)throw new ArgumentException("At least one ticket is required.");var request=new EnemyDirectorRequest(0x4d335f4649585455UL,LastWatchDifficulty.Normal,1,31);var tickets=new SpawnTicket[definitions.Length];for(int n=0;n<tickets.Length;n++){M3TicketFixtureDefinition x=definitions[n];tickets[n]=new SpawnTicket(EnemyDirector.Mix(request.RunSeed^(ulong)(uint)n),n,x.Archetype,x.Face,n/4,x.ScheduledTick,EnemyDirector.Mix((ulong)(uint)n+1),EnemyDirector.TickRate);}return new EnemyDirectorQueue(request,new StoryQuantity(definitions.Length,14,6f),tickets);}
    }
    public readonly struct SpawnPressureSample { public int FixedTick{get;} public int PressureUnits{get;} public float Pressure=>PressureUnits/4f; public SpawnPressureSample(int tick,int units){FixedTick=tick;PressureUnits=units;} }
    public readonly struct ActiveEnemyPressureSnapshot
    {public EntityId EntityId{get;}public ulong TicketId{get;}public EnemyArchetype Archetype{get;}public FaceId Face{get;}public int PressureUnits{get;}public int ActivationTick{get;}public EnemyMovementState MovementState{get;}public bool IsHighThreatWindup{get;}internal ActiveEnemyPressureSnapshot(EntityId entity,SpawnTicketSnapshot x){EntityId=entity;TicketId=x.Ticket.TicketId;Archetype=x.Ticket.Archetype;Face=x.Ticket.Face;PressureUnits=M3PressureRules.UnitsFor(Archetype);ActivationTick=x.ActualSpawnTick.GetValueOrDefault();MovementState=x.MovementState;IsHighThreatWindup=M3PressureRules.IsHighThreatWindup(Archetype,MovementState);}}
    public readonly struct ActivationAttemptResult
    {
        public bool Accepted{get;} public ActivationBlockReason Reason{get;} public ulong? TicketId{get;} public EnemyArchetype Archetype{get;} public FaceId Face{get;} public int CursorBefore{get;} public int CursorAfter{get;} public SpawnTicketLifecycle LifecycleBefore{get;} public SpawnTicketLifecycle LifecycleAfter{get;} public EntityId? EntityId{get;} public int? ActualSpawnTick{get;}
        internal ActivationAttemptResult(bool accepted,ActivationBlockReason reason,SpawnTicketSnapshot? before,EnemyDirectorRuntimeSnapshot pre,EnemyDirectorRuntimeSnapshot post,EntityId? entity){Accepted=accepted;Reason=reason;TicketId=before.HasValue?before.Value.Ticket.TicketId:(ulong?)null;Archetype=before.HasValue?before.Value.Ticket.Archetype:default;Face=before.HasValue?before.Value.Ticket.Face:default;CursorBefore=pre.Cursor;CursorAfter=post.Cursor;LifecycleBefore=before.HasValue?before.Value.Lifecycle:default;LifecycleAfter=before.HasValue?Find(post,before.Value.Ticket.TicketId).Lifecycle:default;EntityId=entity;ActualSpawnTick=before.HasValue?Find(post,before.Value.Ticket.TicketId).ActualSpawnTick:null;}
        private static SpawnTicketSnapshot Find(EnemyDirectorRuntimeSnapshot s,ulong id){foreach(SpawnTicketSnapshot x in s.Tickets)if(x.Ticket.TicketId==id)return x;return default;}
    }
    public enum WindupBlockReason { None, NotActive, NotHighThreat, HighThreatWindupCap }
    public readonly struct WindupAttemptResult { public bool Accepted{get;} public WindupBlockReason Reason{get;} public EntityId EntityId{get;} public EnemyMovementState Prior{get;} public EnemyMovementState Current{get;} public int CountBefore{get;} public int CountAfter{get;} internal WindupAttemptResult(bool ok,WindupBlockReason why,EntityId id,EnemyMovementState prior,EnemyMovementState current,int before,int after){Accepted=ok;Reason=why;EntityId=id;Prior=prior;Current=current;CountBefore=before;CountAfter=after;} }
}
