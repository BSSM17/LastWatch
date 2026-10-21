using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;

namespace LastWatch.Core.Persistence
{
    [Serializable] internal sealed class M4TicketDto
    { public ulong id; public int index; public EnemyArchetype archetype; public FaceId face; public int group; public int scheduled; public ulong seed; }
    [Serializable] internal sealed class M4RuntimeTicketDto
    { public M4TicketDto ticket; public SpawnTicketLifecycle lifecycle; public ulong? active; public int? actual; public ActivationBlockReason block; public EnemyMovementState movement; }
    [Serializable] internal sealed class M4CombatFactDto
    { public M3CoreFactKind kind; public int tick; public ulong sequence; public ulong? ticket; public ulong? entity; public ulong? fire; public string detail; }
    [Serializable] internal sealed class M4PressureDto { public int tick; public int units; }
    [Serializable] internal sealed class M4PoolEventDto { public EnemyPoolEventKind kind; public int tick; public ulong ticket; public ulong? entity; }
    [Serializable] internal sealed class M4RunnerDto
    { public ulong runner; public ulong fire; public RunnerEvadePhase phase; public RunnerCoordinationSkipReason reason; public int cooldown; public int reactions; public RunnerEvadePhase decisionPhase; public int scheduled; public int decisionCooldown; public float distance; public int duration; public int direction; public string skip; }
    [Serializable] internal sealed class M4CombatDto
    {
        public int schema; public ulong runSeed; public LastWatchDifficulty difficulty; public int day; public int ruleset;
        public int total; public int activeCap; public float density; public string queueHash;
        public M4TicketDto[] originals; public M4RuntimeTicketDto[] tickets; public int cursor; public int ledgerTick;
        public M4CombatFactDto[] directorFacts; public ulong directorNext; public ulong directorAck; public M4PressureDto[] pressure;
        public ulong[] available; public ulong[] active; public int corpses; public int deaths; public M4PoolEventDto[] poolEvents;
        public M4RunnerDto[] runners; public string[] consumed;
        public M4CombatFactDto[] projectionFacts; public ulong projectionNext; public ulong projectionAck;
    }
    internal static class M4CombatCodec
    {
        internal static M4TicketDto Ticket(SpawnTicket t) => new M4TicketDto { id=t.TicketId,index=t.QueueIndex,archetype=t.Archetype,face=t.Face,group=t.GroupId,scheduled=t.ScheduledTick,seed=t.RngSeed };
        internal static SpawnTicket Ticket(M4TicketDto t)
        { if(t==null||t.index<0||t.group<0||t.scheduled<0||!Enum.IsDefined(typeof(EnemyArchetype),t.archetype)||!Enum.IsDefined(typeof(FaceId),t.face))throw new FormatException("Invalid ticket.");return new SpawnTicket(t.id,t.index,t.archetype,t.face,t.group,t.scheduled,t.seed,EnemyDirector.TickRate); }
        static M4CombatFactDto Fact(M3CoreFact f) => new M4CombatFactDto{kind=f.Kind,tick=f.FixedTick,sequence=f.Sequence,ticket=f.TicketId,entity=f.EntityId.HasValue?f.EntityId.Value.Value:(ulong?)null,fire=f.FireSequenceId.HasValue?f.FireSequenceId.Value.Value:(ulong?)null,detail=f.Detail};
        static M3CoreFact Fact(M4CombatFactDto f) => new M3CoreFact(f.kind,f.tick,f.sequence,f.ticket,f.entity.HasValue?new EntityId(f.entity.Value):(EntityId?)null,f.fire.HasValue?new FireSequenceId(f.fire.Value):(FireSequenceId?)null,f.detail);
        internal static M4CombatDto Capture(EnemyDirectorRuntime director,EnemyPool pool,RunnerEvadeCoordinator runners,M3FactEmitter facts)
        {
            M3DirectorSaveSnapshot s=director.CaptureSave();EnemyPoolSaveSnapshot p=pool.CaptureSave();RunnerCoordinatorSaveSnapshot r=runners.CaptureSave();M3FactJournalSaveSnapshot f=facts.CaptureSave();
            var d=new M4CombatDto{schema=1,runSeed=s.Request.RunSeed,difficulty=s.Request.Difficulty,day=s.Request.Day,ruleset=s.Request.RulesetVersion,total=s.Quantity.Total,activeCap=s.Quantity.ActiveCap,density=s.Quantity.DensityPer10Seconds,queueHash=s.ImmutableQueueHash,
                originals=Array.ConvertAll(s.OriginalTickets,Ticket),tickets=Array.ConvertAll(s.Tickets,x=>new M4RuntimeTicketDto{ticket=Ticket(x.Ticket),lifecycle=x.Lifecycle,active=x.ActiveEntityId.HasValue?x.ActiveEntityId.Value.Value:(ulong?)null,actual=x.ActualSpawnTick,block=x.BlockReason,movement=x.MovementState}),cursor=director.Snapshot().Cursor,ledgerTick=s.LedgerAsOfTick,
                directorFacts=Array.ConvertAll(s.Facts,Fact),directorNext=s.NextFactSequence,directorAck=s.AcknowledgedFactCursor,pressure=Array.ConvertAll(s.RecentSpawnPressure,x=>new M4PressureDto{tick=x.FixedTick,units=x.PressureUnits}),
                available=Array.ConvertAll(p.Available,x=>x.Value),active=(ulong[])p.Active.Clone(),corpses=p.VisibleCorpses,deaths=p.DeathFacts,poolEvents=Array.ConvertAll(p.Events,x=>new M4PoolEventDto{kind=x.Kind,tick=x.Tick,ticket=x.TicketId,entity=x.EntityId.HasValue?x.EntityId.Value.Value:(ulong?)null}),
                consumed=(string[])r.ConsumedKeys.Clone(),projectionFacts=Array.ConvertAll(f.Facts,Fact),projectionNext=f.NextSequence,projectionAck=f.AcknowledgedCursor};
            d.runners=Array.ConvertAll(r.Reservations,x=>new M4RunnerDto{runner=x.RunnerId.Value,fire=x.FireSequenceId.Value,phase=x.Phase,reason=x.SkipReason,cooldown=x.CooldownUntilTick,reactions=x.ReactionsThisApproach,decisionPhase=x.Decision.Phase,scheduled=x.Decision.ScheduledTick,decisionCooldown=x.Decision.CooldownUntilTick,distance=x.Decision.LateralDistanceMeters,duration=x.Decision.MovementDurationTicks,direction=x.Decision.DirectionSign,skip=x.Decision.SkipReason});return d;
        }
        internal static EnemyDirectorQueue OriginalQueue(M4CombatDto d)
        {
            if(d==null||d.schema!=1||d.originals==null||d.originals.Length!=d.total||d.total<1||d.total>1000)throw new FormatException("M3 schema/queue size mismatch.");
            var ids=new HashSet<ulong>();SpawnTicket[] originals=Array.ConvertAll(d.originals,Ticket);
            for(int n=0;n<originals.Length;n++)if(originals[n].QueueIndex!=n||!ids.Add(originals[n].TicketId))throw new FormatException("Original queue identity mismatch.");
            var q=new EnemyDirectorQueue(new EnemyDirectorRequest(d.runSeed,d.difficulty,d.day,d.ruleset),new StoryQuantity(d.total,d.activeCap,d.density),originals);
            StoryQuantity expected=EnemyDirector.GetStoryQuantity(d.day,d.difficulty);
            if(q.CanonicalHash()!=d.queueHash||d.total!=expected.Total||d.activeCap!=expected.ActiveCap||d.density!=expected.DensityPer10Seconds)throw new FormatException("Original queue hash/quantity mismatch.");return q;
        }
        internal static M3RuntimeRestoreResult Restore(M4CombatDto d)
        {
            EnemyDirectorQueue q=OriginalQueue(d);
            if(d.tickets==null||d.directorFacts==null||d.pressure==null||d.available==null||d.active==null||d.poolEvents==null||d.runners==null||d.consumed==null||d.projectionFacts==null||d.ledgerTick<0)throw new FormatException("Incomplete M3 payload.");
            var rows=Array.ConvertAll(d.tickets,x=>new SpawnTicketSnapshot(Ticket(x.ticket),x.lifecycle,x.active.HasValue?new EntityId(x.active.Value):(EntityId?)null,x.actual,x.block,x.movement));
            var state=new EnemyDirectorRuntimeSnapshot{Tickets=rows,LedgerAsOfTick=d.ledgerTick};
            var pressure=Array.ConvertAll(d.pressure,x=>Tuple.Create(x.tick,x.units));
            var save=M3DirectorSaveSnapshot.Create(q,state,Array.ConvertAll(d.directorFacts,Fact),pressure,d.directorNext,d.directorAck);
            var director=EnemyDirectorRuntime.Restore(save);if(director.Snapshot().Cursor!=d.cursor)throw new FormatException("Ticket cursor mismatch.");
            var ids=new HashSet<ulong>();foreach(ulong id in d.available)if(id==0||!ids.Add(id))throw new FormatException("Pool duplicate available identity.");foreach(ulong id in d.active)if(id==0||!ids.Add(id))throw new FormatException("Pool active/available overlap.");
            if(d.corpses<0||d.corpses>4||d.deaths<d.corpses)throw new FormatException("Pool corpse/death count mismatch.");
            var pool=EnemyPool.Restore(new EnemyPoolSaveSnapshot(Array.ConvertAll(d.available,x=>new EntityId(x)),d.active,d.corpses,d.deaths,Array.ConvertAll(d.poolEvents,x=>new EnemyPoolEvent(x.kind,x.tick,x.ticket,x.entity.HasValue?new EntityId(x.entity.Value):(EntityId?)null))));
            var reservations=Array.ConvertAll(d.runners,x=>new RunnerReservationSnapshot(new EntityId(x.runner),new FireSequenceId(x.fire),new RunnerReactionDecision(x.decisionPhase,x.scheduled,x.decisionCooldown,x.distance,x.duration,x.direction,x.skip),x.phase,x.reason,x.cooldown,x.reactions));
            var runners=RunnerEvadeCoordinator.Restore(RunnerCoordinatorSaveSnapshot.Create(reservations,d.consumed));
            var projection=new M3FactEmitter();ulong next=1;
            foreach(var row in d.projectionFacts){if(row.sequence!=next++)throw new FormatException("Projection sequence gap.");M3CoreFact f=Fact(row);projection.Emit(f.Kind,f.FixedTick,f.TicketId,f.EntityId,f.FireSequenceId,f.Detail);}
            if(d.projectionNext!=next||d.projectionAck>=next)throw new FormatException("Projection cursor mismatch.");projection.AcknowledgeThrough(d.projectionAck);
            // Existing Core cross-component factory checks active IDs, pressure and fact cursors.
            return M3RuntimeSaveSnapshot.Capture(director,pool,runners,projection).Restore();
        }
    }
}
