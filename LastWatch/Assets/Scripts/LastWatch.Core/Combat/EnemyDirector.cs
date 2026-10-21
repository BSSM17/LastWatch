using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Combat
{
    public enum LastWatchDifficulty { Normal, Hard }
    public enum SpawnTicketLifecycle { Pending, PoolWaiting, Active, Resolved, Rerouted }
    public enum ActivationBlockReason { None, PoolOrActiveCount, TotalPressure, FaceEntityCount, FacePressure, BreakerCount, NewSpawnPressureWindow }

    public readonly struct DifficultyQuantityProfile
    {
        public float TotalMultiplier { get; }
        public int ActiveCapBonus { get; }
        public float DensityMultiplier { get; }
        public DifficultyQuantityProfile(float total, int cap, float density)
        { TotalMultiplier=total; ActiveCapBonus=cap; DensityMultiplier=density; }
        public static DifficultyQuantityProfile For(LastWatchDifficulty difficulty) => difficulty == LastWatchDifficulty.Hard
            ? new DifficultyQuantityProfile(1.25f,2,1.15f) : new DifficultyQuantityProfile(1f,0,1f);
    }

    public readonly struct StoryQuantity
    {
        public int Total { get; }
        public int ActiveCap { get; }
        public float DensityPer10Seconds { get; }
        public StoryQuantity(int total,int active,float density) { Total=total; ActiveCap=active; DensityPer10Seconds=density; }
    }

    public readonly struct EnemyDirectorRequest
    {
        public ulong RunSeed { get; }
        public LastWatchDifficulty Difficulty { get; }
        public int Day { get; }
        public int RulesetVersion { get; }
        public EnemyDirectorRequest(ulong runSeed, LastWatchDifficulty difficulty, int day, int rulesetVersion)
        { if(day<1||day>7) throw new ArgumentOutOfRangeException(nameof(day)); if(rulesetVersion<=0) throw new ArgumentOutOfRangeException(nameof(rulesetVersion)); RunSeed=runSeed; Difficulty=difficulty; Day=day; RulesetVersion=rulesetVersion; }
    }

    public readonly struct SpawnTicket
    {
        public ulong TicketId { get; }
        public int QueueIndex { get; }
        public EnemyArchetype Archetype { get; }
        public FaceId Face { get; }
        public int GroupId { get; }
        public int ScheduledTick { get; }
        public ulong RngSeed { get; }
        public int WarningStartTick { get; }
        public int EarliestStructureDamageTick { get; }
        public string SpawnPointId { get; }
        public Vector3 SpawnPosition { get; }
        public SpawnTicket(ulong id,int index,EnemyArchetype archetype,FaceId face,int group,int scheduled,ulong seed,int tickRate)
        { TicketId=id; QueueIndex=index; Archetype=archetype; Face=face; GroupId=group; ScheduledTick=scheduled; RngSeed=seed; WarningStartTick=scheduled; EarliestStructureDamageTick=checked(scheduled + 8*tickRate);int slot=index%4;SpawnPointId=face+".spawn.slot"+slot;Vector3 direction=TowerGeometryFixture.DirectionFor(face);Vector3 side=Vector3.Cross(Vector3.up,direction).normalized;SpawnPosition=direction*TowerGeometryFixture.SpawnRingMaximumRadius+Vector3.up*0.5f+side*(slot-1.5f)*1.2f; }
    }

    public readonly struct SpawnTicketSnapshot
    {
        public SpawnTicket Ticket { get; }
        public SpawnTicketLifecycle Lifecycle { get; }
        public EntityId? ActiveEntityId { get; }
        public int? ActualSpawnTick { get; }
        public ActivationBlockReason BlockReason { get; }
        public EnemyMovementState MovementState { get; }
        public SpawnTicketSnapshot(SpawnTicket ticket,SpawnTicketLifecycle lifecycle,EntityId? active,int? actual,ActivationBlockReason blockReason=ActivationBlockReason.None,EnemyMovementState movementState=EnemyMovementState.Spawn) { Ticket=ticket; Lifecycle=lifecycle; ActiveEntityId=active; ActualSpawnTick=actual; BlockReason=blockReason; MovementState=movementState; }
    }

    public sealed class EnemyDirectorQueue
    {
        public const int TickRate = 50;
        private readonly SpawnTicket[] tickets;
        public EnemyDirectorRequest Request { get; }
        public StoryQuantity Quantity { get; }
        public IReadOnlyList<SpawnTicket> Tickets => Array.AsReadOnly((SpawnTicket[])tickets.Clone());
        internal EnemyDirectorQueue(EnemyDirectorRequest request,StoryQuantity quantity,SpawnTicket[] values) { Request=request; Quantity=quantity; tickets=values; }
        public string CanonicalHash()
        {
            var b=new StringBuilder(); b.Append("LW.M3.QUEUE|v=").Append(Request.RulesetVersion).Append("|seed=").Append(Request.RunSeed.ToString("x16")).Append("|d=").Append(Request.Day).Append("|q=").Append((int)Request.Difficulty).Append("|total=").Append(Quantity.Total).Append("|active=").Append(Quantity.ActiveCap).Append("|density=").Append(Quantity.DensityPer10Seconds.ToString("R",CultureInfo.InvariantCulture));
            foreach(SpawnTicket x in tickets) b.Append('|').Append(x.TicketId.ToString("x16")).Append(':').Append(x.QueueIndex).Append(':').Append((int)x.Archetype).Append(':').Append((int)x.Face).Append(':').Append(x.GroupId).Append(':').Append(x.ScheduledTick).Append(':').Append(x.RngSeed.ToString("x16")).Append(':').Append(x.SpawnPointId).Append(':').Append(x.SpawnPosition.x.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(x.SpawnPosition.y.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(x.SpawnPosition.z.ToString("R",CultureInfo.InvariantCulture));
            using(SHA256 sha=SHA256.Create()) return EnemyArchetypeCatalog.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())));
        }
    }

    public static class EnemyDirector
    {
        public const int TickRate = 50;
        public static StoryQuantity GetStoryQuantity(int day,LastWatchDifficulty difficulty)
        {
            if(day<1||day>7) throw new ArgumentOutOfRangeException(nameof(day)); DifficultyQuantityProfile p=DifficultyQuantityProfile.For(difficulty);
            int total=(int)Math.Floor((18+6*(day-1))*p.TotalMultiplier+0.5f);
            int active=Math.Min(14,8+(day-1)/2+p.ActiveCapBonus);
            float density=Math.Min(6f,(2.6f+0.30f*(day-1))*p.DensityMultiplier);
            return new StoryQuantity(total,active,(float)Math.Round(density,1,MidpointRounding.AwayFromZero));
        }

        public static EnemyDirectorQueue CreateStoryQueue(EnemyDirectorRequest request)
        {
            StoryQuantity q=GetStoryQuantity(request.Day,request.Difficulty); int[] counts=Allocate(q.Total);
            var archetypes=new List<EnemyArchetype>(q.Total);
            for(int a=0;a<counts.Length;a++) for(int n=0;n<counts[a];n++) archetypes.Add((EnemyArchetype)a);
            ulong state=Mix(request.RunSeed ^ ((ulong)(uint)request.Day<<32) ^ (uint)request.RulesetVersion);
            for(int n=archetypes.Count-1;n>0;n--) { int j=(int)(Next(ref state)%(ulong)(n+1)); EnemyArchetype t=archetypes[n]; archetypes[n]=archetypes[j]; archetypes[j]=t; }
            var values=new SpawnTicket[q.Total];
            // A four-ticket group arrives with the authored 0.4-0.7 s internal gaps.
            // Group starts are separated so every rolling ten-second window stays at or
            // below DensityPer10Seconds. Rotating Faces makes a four-ticket group 2/1/1,
            // therefore no Face can exceed 60% in that window.
            int groupInterval=Math.Max(1,(int)Math.Ceiling(40f*TickRate/q.DensityPer10Seconds));
            int faceOffset=(int)(Next(ref state)%3); int previousScheduled=0,actualGroup=-1,membersInGroup=0;
            var scheduledPressure=new List<Tuple<int,int>>();
            for(int n=0;n<values.Length;n++)
            {
                int nominalGroup=n/4; int within=n%4;if(within==0){actualGroup++;membersInGroup=0;}
                int face=(faceOffset+n)%3;
                int groupStart=nominalGroup*groupInterval; int scheduled=n==0?groupStart:Math.Max(groupStart,previousScheduled+1);
                if(within>0) scheduled=Math.Max(scheduled,previousScheduled+20+(int)(Next(ref state)%16));
                scheduled=EarliestPressureSafeTick(scheduled,M3PressureRules.UnitsFor(archetypes[n]),scheduledPressure,10*TickRate,M3PressureRules.RollingSpawnCapUnits);
                if(membersInGroup>0&&scheduled-previousScheduled>35){actualGroup++;membersInGroup=0;}
                previousScheduled=scheduled;
                ulong seed=Next(ref state); ulong id=Mix(request.RunSeed ^ seed ^ (ulong)(uint)n);
                values[n]=new SpawnTicket(id,n,archetypes[n],(FaceId)face,actualGroup,scheduled,seed,TickRate);membersInGroup++;scheduledPressure.Add(Tuple.Create(scheduled,M3PressureRules.UnitsFor(archetypes[n])));
            }
            var queue=new EnemyDirectorQueue(request,q,values);if(!SatisfiesRollingFaceShare(queue.Tickets,10*TickRate))throw new InvalidOperationException("Generated queue violates the approved rolling Face-share gate.");if(!SatisfiesRollingScheduledPressure(queue.Tickets,10*TickRate,M3PressureRules.RollingSpawnCapUnits))throw new InvalidOperationException("Generated queue violates rolling scheduled pressure.");return queue;
        }

        public static bool SatisfiesRollingFaceShare(IReadOnlyList<SpawnTicket> tickets,int windowTicks)
        {
            if(tickets==null)throw new ArgumentNullException(nameof(tickets));if(windowTicks<=0)throw new ArgumentOutOfRangeException(nameof(windowTicks));
            for(int start=0;start<tickets.Count;start++){int endTick=checked(tickets[start].ScheduledTick+windowTicks);int total=0;int[] faces=new int[3];for(int n=start;n<tickets.Count&&tickets[n].ScheduledTick<endTick;n++){total++;faces[(int)tickets[n].Face]++;}if(total<2)continue;for(int face=0;face<3;face++)if(faces[face]*100>total*60)return false;}return true;
        }
        public static bool SatisfiesRollingScheduledPressure(IReadOnlyList<SpawnTicket> tickets,int windowTicks,int capUnits)
        {if(tickets==null)throw new ArgumentNullException(nameof(tickets));if(windowTicks<=0||capUnits<=0)throw new ArgumentOutOfRangeException();for(int start=0;start<tickets.Count;start++){int end=checked(tickets[start].ScheduledTick+windowTicks),units=0;for(int n=start;n<tickets.Count&&tickets[n].ScheduledTick<end;n++)units+=M3PressureRules.UnitsFor(tickets[n].Archetype);if(units>capUnits)return false;}return true;}

        private static int EarliestPressureSafeTick(int proposed,int candidateUnits,List<Tuple<int,int>> prior,int windowTicks,int capUnits)
        {int tick=proposed;while(true){int units=candidateUnits,earliest=int.MaxValue;for(int n=0;n<prior.Count;n++){Tuple<int,int> x=prior[n];if(x.Item1>tick-windowTicks&&x.Item1<=tick){units+=x.Item2;if(x.Item1<earliest)earliest=x.Item1;}}if(units<=capUnits)return tick;if(earliest==int.MaxValue)throw new InvalidOperationException("Candidate pressure exceeds its own cap.");tick=Math.Max(tick+1,checked(earliest+windowTicks));}}

        private static int[] Allocate(int total)
        {
            int[] weights={70,15,10,5}; int[] result=new int[4]; int used=0;
            var remainder=new List<Tuple<int,int>>();
            for(int i=0;i<4;i++){ int scaled=total*weights[i]; result[i]=scaled/100; used+=result[i]; remainder.Add(Tuple.Create(scaled%100,i)); }
            remainder.Sort((a,b)=>{int c=b.Item1.CompareTo(a.Item1);return c!=0?c:a.Item2.CompareTo(b.Item2);});
            for(int i=0;i<total-used;i++) result[remainder[i].Item2]++;
            return result;
        }
        internal static ulong Mix(ulong z){z+=0x9e3779b97f4a7c15UL;z=(z^(z>>30))*0xbf58476d1ce4e5b9UL;z=(z^(z>>27))*0x94d049bb133111ebUL;return z^(z>>31);}
        internal static ulong Next(ref ulong state){state=Mix(state);return state;}
    }
}
