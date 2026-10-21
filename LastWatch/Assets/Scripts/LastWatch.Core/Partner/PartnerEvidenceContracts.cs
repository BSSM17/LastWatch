using System;
using System.Collections.Generic;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Core.Partner
{
    public enum PartnerPathStatus { Reserved = 0, Arrived = 1, NoPathRecoveredToSafeNode = 2 }
    public readonly struct PartnerPathResult
    {
        public PartnerPathStatus Status { get; }
        public string FromNodeId { get; }
        public string RequestedNodeId { get; }
        public string ResolvedNodeId { get; }
        public int FixedTick { get; }
        public PartnerPathResult(PartnerPathStatus status,string from,string requested,string resolved,int tick)
        { Status=status;FromNodeId=from;RequestedNodeId=requested;ResolvedNodeId=resolved;FixedTick=tick; }
    }
    public interface IPartnerStationGraph
    {
        PartnerPathResult Request(string fromNodeId,string targetNodeId,string nearestSafeNodeId,int fixedTick);
    }

    public readonly struct SafetyOverrideResult
    {
        public bool Activated { get; }
        public PartnerRole InterruptedRole { get; }
        public string SafeNodeId { get; }
        public int FixedTick { get; }
        public SafetyOverrideResult(bool activated,PartnerRole interrupted,string safeNode,int tick)
        { Activated=activated;InterruptedRole=interrupted;SafeNodeId=safeNode;FixedTick=tick; }
    }

    public readonly struct PartnerResolvedFireFact
    {
        public int FixedTick { get; }
        public ulong EventSequence { get; }
        public FireSequenceId FireSequenceId { get; }
        public string DesignatedFaceId { get; }
        public EntityId TargetEntityId { get; }
        public int AmmoAfter { get; }
        public bool HeldForPlayerLine { get; }
        public float PlayerDamage { get; }
        public PartnerResolvedFireFact(int tick,ulong sequence,FireSequenceId fireSequence,string face,EntityId target,int ammo,bool held)
        { FixedTick=tick;EventSequence=sequence;FireSequenceId=fireSequence;DesignatedFaceId=face;TargetEntityId=target;AmmoAfter=ammo;HeldForPlayerLine=held;PlayerDamage=0f; }
    }

    public enum Data40FactKind { RoleTransition = 0, SignalIssued = 1, PathRecovery = 2, FireResolved = 3, FriendlyHitIncidentClosed = 4, LifeTransition = 5, SafetyOverride = 6 }
    [Serializable]
    public sealed class Data40Fact
    {
        public ulong Cursor;
        public Data40FactKind Kind;
        public int FixedTick;
        public ulong EventSequence;
        public string CorrelationId;
        public string Payload;
    }
    public sealed class Data40Snapshot
    {
        public ulong AcknowledgedCursor { get; internal set; }
        public ulong LastCommittedCursor { get; internal set; }
        public Data40Fact[] Records { get; internal set; }
    }
    public sealed class PartnerData40Journal
    {
        readonly PartnerRuntimeState state;
        public PartnerData40Journal(PartnerRuntimeState state){this.state=state??throw new ArgumentNullException(nameof(state));}
        public Data40Fact Append(Data40FactKind kind,int tick,ulong sequence,string correlationId,string payload)
        {
            if(tick<0||sequence==0)throw new ArgumentException("Invalid DATA-40 fact envelope.");
            foreach(var x in state.Data40Facts)if(x.EventSequence==sequence&&x.Kind==kind)return x;
            var fact=new Data40Fact{Cursor=state.NextData40Cursor++,Kind=kind,FixedTick=tick,EventSequence=sequence,CorrelationId=correlationId,Payload=payload};
            state.Data40Facts.Add(fact);if(state.M4ActiveEntryContext!=null)state.M4FactContexts.Add(fact.Cursor,state.M4ActiveEntryContext);return fact;
        }
        public Data40Snapshot ReadAfter(ulong cursor)
        { var values=new List<Data40Fact>();foreach(var x in state.Data40Facts)if(x.Cursor>cursor)values.Add(x);return new Data40Snapshot{AcknowledgedCursor=state.Data40AcknowledgedCursor,LastCommittedCursor=state.NextData40Cursor-1,Records=values.ToArray()}; }
        public void AcknowledgeThrough(ulong cursor)
        { if(cursor<state.Data40AcknowledgedCursor||cursor>=state.NextData40Cursor)throw new ArgumentOutOfRangeException(nameof(cursor));state.Data40AcknowledgedCursor=cursor; }
    }

    public readonly struct PartnerLifeTransitionResult
    {
        public PartnerLifeState Before { get; }
        public PartnerLifeState After { get; }
        public PartnerDeathCause DeathCause { get; }
        public bool Applied { get; }
        public float RetaliationDamageToPlayer { get; }
        public PartnerLifeTransitionResult(PartnerLifeState before,PartnerLifeState after,PartnerDeathCause cause,bool applied)
        { Before=before;After=after;DeathCause=cause;Applied=applied;RetaliationDamageToPlayer=0f; }
    }
}
