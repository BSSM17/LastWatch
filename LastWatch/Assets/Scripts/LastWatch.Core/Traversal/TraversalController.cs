using System;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Traversal
{
    public enum TraversalCommandStatus { Started, RejectedBusy, RejectedInvalidZone }
    public enum TraversalStepStatus { InProgress, Completed, InterruptedInputReleased, InterruptedLargeDamage, NotTraversing }
    public enum TraversalPlatformNodeId { UpperDeck, UpperLanding, MidAccess, GroundLanding }

    public readonly struct TraversalSnapshot
    {
        public TraversalState State { get; }
        public LocationZone Zone { get; }
        public TraversalPlatformNodeId LastPassedNode { get; }
        public int ElapsedTicks { get; }
        public int TotalTicks { get; }
        public float NormalizedProgress { get; }
        public Vector3 Position { get; }
        public bool CanPerformNonMovementAction => State == TraversalState.None;

        internal TraversalSnapshot(TraversalState state, LocationZone zone, TraversalPlatformNodeId lastPassedNode,
            int elapsedTicks, int totalTicks, Vector3 position)
        {
            State = state;
            Zone = zone;
            LastPassedNode = lastPassedNode;
            ElapsedTicks = elapsedTicks;
            TotalTicks = totalTicks;
            NormalizedProgress = totalTicks <= 0 ? 0f : Mathf.Clamp01((float)elapsedTicks / totalTicks);
            Position = position;
        }
    }

    public sealed class TraversalController
    {
        public const int NormalDescentTicks = 3 * 60;
        public const int NormalAscentTicks = 6 * 60;

        private readonly Vector3 upperPosition;
        private readonly Vector3 upperLanding;
        private readonly Vector3 midAccess;
        private readonly Vector3 groundPosition;
        private TraversalState state;
        private LocationZone zone;
        private TraversalPlatformNodeId lastPassedNode;
        private int elapsedTicks;
        private int totalTicks;
        private Vector3 start;
        private Vector3 end;
        readonly M4ManualTraversalAccess manualAccess;
        readonly int tickRate=60;
        readonly bool automaticManual;
        Vector3[] manualPath;
        TraversalPlatformNodeId[] manualNodeIds;
        float[] manualDistances;
        int manualPassedIndex;
        Vector3 manualPosition;
        TraversalState interruptedDirection;
        public bool HasInterruptedManualRoute => manualAccess!=null&&interruptedDirection!=TraversalState.None;
        public string LastManualAbortReason { get; private set; }
        public void ObserveManualEndpointWalk(Vector3 actualOrigin)
        {
            if(state!=TraversalState.None||!HasInterruptedManualRoute||
                (lastPassedNode!=TraversalPlatformNodeId.UpperDeck&&lastPassedNode!=TraversalPlatformNodeId.GroundLanding))return;
            if(actualOrigin.Equals(manualPosition))return;
            // A real walk on an ordinary endpoint ends stale interrupted intent;
            // intermediate landing continuation and remaining ticks are untouched.
            interruptedDirection=TraversalState.None;elapsedTicks=0;totalTicks=0;manualPosition=actualOrigin;
        }

        public TraversalController()
        {
            Vector3 accessDirection = (TowerGeometryFixture.DirectionFor(FaceId.FaceC) +
                                       TowerGeometryFixture.DirectionFor(FaceId.FaceA)).normalized;
            upperPosition = accessDirection * 5.5f + Vector3.up * TowerGeometryFixture.UpperDeckY;
            upperLanding = accessDirection * 6.5f + Vector3.up * 24f;
            midAccess = accessDirection * 8.5f + Vector3.up * 14f;
            groundPosition = accessDirection * TowerGeometryFixture.GroundJointRadius;
            state = TraversalState.None;
            zone = LocationZone.UpperSafeZone;
            lastPassedNode = TraversalPlatformNodeId.UpperDeck;
        }

        public TraversalSnapshot Snapshot => CreateSnapshot();

        public ManualTraversalEntryEvaluation EvaluateManualEntry(Vector3 actualOrigin)
        {
            if(manualAccess==null)return new ManualTraversalEntryEvaluation(false,false,-1,"입구를 확인할 수 없습니다");
            if(state!=TraversalState.None)return new ManualTraversalEntryEvaluation(false,state==TraversalState.Ascending,-1,"이동 중입니다");
            if(HasInterruptedManualRoute)
                return new ManualTraversalEntryEvaluation(actualOrigin.Equals(manualPosition),interruptedDirection==TraversalState.Ascending,
                    (int)lastPassedNode,actualOrigin.Equals(manualPosition)?null:"복귀 발판 위치를 확인하세요",true);
            return manualAccess.EvaluateNormalEntry(actualOrigin);
        }

        public TraversalController(int ticksPerSecond,M4ManualTraversalAccess access) : this(ticksPerSecond,access,false) {}

        public TraversalController(int ticksPerSecond,M4ManualTraversalAccess access,bool automatic) : this()
        {
            if(ticksPerSecond<=0)throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            manualAccess=access??throw new ArgumentNullException(nameof(access));tickRate=ticksPerSecond;
            automaticManual=automatic;
            manualPosition=access.OriginAt(0);
        }
        public TraversalCommandStatus BeginManualNormal(Vector3 actualOrigin,bool ascend)
        {
            if(manualAccess==null)throw new InvalidOperationException("Explicit manual access required.");
            if(state!=TraversalState.None)return TraversalCommandStatus.RejectedBusy;
            var entry=EvaluateManualEntry(actualOrigin);
            if(!entry.Eligible)return TraversalCommandStatus.RejectedInvalidZone;
            if(HasInterruptedManualRoute)
            {
                if(!actualOrigin.Equals(manualPosition))return TraversalCommandStatus.RejectedInvalidZone;
                state=interruptedDirection;interruptedDirection=TraversalState.None;
                return TraversalCommandStatus.Started;
            }
            if(entry.Ascending!=ascend)return TraversalCommandStatus.RejectedInvalidZone;
            state=ascend?TraversalState.Ascending:TraversalState.Descending;
            totalTicks=checked((ascend?6:automaticManual?1:3)*tickRate);elapsedTicks=0;manualPassedIndex=0;
            manualPath=new Vector3[5];manualNodeIds=new TraversalPlatformNodeId[5];manualDistances=new float[5];
            manualPath[0]=actualOrigin;manualNodeIds[0]=ascend?TraversalPlatformNodeId.GroundLanding:TraversalPlatformNodeId.UpperDeck;
            for(int i=0;i<4;i++)
            {
                int index=ascend?3-i:i;manualPath[i+1]=manualAccess.OriginAt(index);
                manualNodeIds[i+1]=(TraversalPlatformNodeId)index;
                manualDistances[i+1]=manualDistances[i]+Vector3.Distance(manualPath[i],manualPath[i+1]);
            }
            manualPosition=actualOrigin;lastPassedNode=manualNodeIds[0];
            zone=ascend?LocationZone.GroundDanger:LocationZone.UpperSafeZone;
            return TraversalCommandStatus.Started;
        }

        TraversalStepStatus AdvanceManual(bool inputHeld,bool largeDamage)
        {
            if((!automaticManual&&!inputHeld)||largeDamage)
            {
                ReturnManualToPassedNode();
                return largeDamage?TraversalStepStatus.InterruptedLargeDamage:TraversalStepStatus.InterruptedInputReleased;
            }
            elapsedTicks++;
            float distance=manualDistances[4]*Mathf.Clamp01((float)elapsedTicks/totalTicks);
            while(manualPassedIndex<4&&manualDistances[manualPassedIndex+1]<=distance)
                manualPassedIndex++;
            lastPassedNode=manualNodeIds[manualPassedIndex];
            int segment=Math.Min(manualPassedIndex,3);
            float length=manualDistances[segment+1]-manualDistances[segment];
            manualPosition=length<=0f?manualPath[segment+1]:Vector3.Lerp(manualPath[segment],manualPath[segment+1],
                (distance-manualDistances[segment])/length);
            zone=ZoneFor(lastPassedNode);
            if(elapsedTicks<totalTicks)return TraversalStepStatus.InProgress;
            manualPosition=manualPath[4];lastPassedNode=manualNodeIds[4];zone=ZoneFor(lastPassedNode);
            state=TraversalState.None;interruptedDirection=TraversalState.None;
            return TraversalStepStatus.Completed;
        }

        void ReturnManualToPassedNode()
        {
            interruptedDirection=state;state=TraversalState.None;
            manualPosition=manualPath[manualPassedIndex];
            elapsedTicks=Mathf.CeilToInt(totalTicks*manualDistances[manualPassedIndex]/manualDistances[4]);
            zone=ZoneFor(lastPassedNode);
        }
        // Owned life/safety/phase closure, never synthesized as key-up or damage.
        public bool AbortManualForDomain(string reason)
        {
            if(manualAccess==null)throw new InvalidOperationException("Explicit manual access required.");
            if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Domain abort reason required.",nameof(reason));
            if(state==TraversalState.None)return false;
            LastManualAbortReason=reason;
            ReturnManualToPassedNode();return true;
        }

        public TraversalCommandStatus BeginNormalDescent()
        {
            if(manualAccess!=null)throw new InvalidOperationException("Manual QA requires BeginManualNormal with the actual actor origin.");
            if (state != TraversalState.None) return TraversalCommandStatus.RejectedBusy;
            if (zone != LocationZone.UpperSafeZone) return TraversalCommandStatus.RejectedInvalidZone;
            Begin(TraversalState.Descending, NormalDescentTicks, upperPosition, groundPosition,
                TraversalPlatformNodeId.UpperDeck);
            return TraversalCommandStatus.Started;
        }

        public TraversalCommandStatus BeginNormalAscent()
        {
            if(manualAccess!=null)throw new InvalidOperationException("Manual QA requires BeginManualNormal with the actual actor origin.");
            if (state != TraversalState.None) return TraversalCommandStatus.RejectedBusy;
            if (zone != LocationZone.GroundDanger) return TraversalCommandStatus.RejectedInvalidZone;
            Begin(TraversalState.Ascending, NormalAscentTicks, groundPosition, upperPosition,
                TraversalPlatformNodeId.GroundLanding);
            return TraversalCommandStatus.Started;
        }

        public TraversalStepStatus AdvanceOneTick(bool inputHeld, bool largeDamage)
        {
            if (state == TraversalState.None) return TraversalStepStatus.NotTraversing;
            if(manualAccess!=null)return AdvanceManual(inputHeld,largeDamage);
            if (!inputHeld) return Interrupt(TraversalStepStatus.InterruptedInputReleased);
            if (largeDamage) return Interrupt(TraversalStepStatus.InterruptedLargeDamage);

            elapsedTicks++;
            UpdateLastPassedNode();
            if (elapsedTicks < totalTicks) return TraversalStepStatus.InProgress;

            bool wasDescending = state == TraversalState.Descending;
            state = TraversalState.None;
            zone = wasDescending ? LocationZone.GroundDanger : LocationZone.UpperSafeZone;
            lastPassedNode = wasDescending ? TraversalPlatformNodeId.GroundLanding : TraversalPlatformNodeId.UpperDeck;
            return TraversalStepStatus.Completed;
        }

        private void Begin(TraversalState newState, int duration, Vector3 from, Vector3 to,
            TraversalPlatformNodeId initialNode)
        {
            state = newState;
            zone = newState == TraversalState.Descending ? LocationZone.UpperSafeZone : LocationZone.GroundDanger;
            elapsedTicks = 0;
            totalTicks = duration;
            start = from;
            end = to;
            lastPassedNode = initialNode;
        }

        private TraversalStepStatus Interrupt(TraversalStepStatus status)
        {
            Vector3 nodePosition = PositionFor(lastPassedNode);
            zone = ZoneFor(lastPassedNode);
            state = TraversalState.None;
            start = nodePosition;
            end = nodePosition;
            elapsedTicks = 0;
            totalTicks = 0;
            return status;
        }

        private void UpdateLastPassedNode()
        {
            float progress = (float)elapsedTicks / totalTicks;
            if (state == TraversalState.Descending)
            {
                if (progress >= 0.45f) lastPassedNode = TraversalPlatformNodeId.MidAccess;
                else if (progress >= 0.15f) lastPassedNode = TraversalPlatformNodeId.UpperLanding;
            }
            else
            {
                if (progress >= 0.55f) lastPassedNode = TraversalPlatformNodeId.UpperLanding;
                else if (progress >= 0.25f) lastPassedNode = TraversalPlatformNodeId.MidAccess;
            }
            zone = ZoneFor(lastPassedNode);
        }

        private TraversalSnapshot CreateSnapshot()
        {
            Vector3 position = manualAccess!=null ? manualPosition : state == TraversalState.None
                ? PositionFor(lastPassedNode)
                : Vector3.Lerp(start, end, (float)elapsedTicks / totalTicks);
            LocationZone actualZone=zone;
            if(manualAccess!=null&&manualAccess.TryClassifyOrigin(position,out LocationZone classified))actualZone=classified;
            return new TraversalSnapshot(state, actualZone, lastPassedNode, elapsedTicks, totalTicks, position);
        }

        private Vector3 PositionFor(TraversalPlatformNodeId node)
        {
            switch (node)
            {
                case TraversalPlatformNodeId.UpperDeck: return upperPosition;
                case TraversalPlatformNodeId.UpperLanding: return upperLanding;
                case TraversalPlatformNodeId.MidAccess: return midAccess;
                case TraversalPlatformNodeId.GroundLanding: return groundPosition;
                default: throw new ArgumentOutOfRangeException(nameof(node));
            }
        }

        private static LocationZone ZoneFor(TraversalPlatformNodeId node)
        {
            if (node == TraversalPlatformNodeId.UpperDeck) return LocationZone.UpperSafeZone;
            if (node == TraversalPlatformNodeId.GroundLanding) return LocationZone.GroundDanger;
            return LocationZone.LowerAccess;
        }
    }
}
