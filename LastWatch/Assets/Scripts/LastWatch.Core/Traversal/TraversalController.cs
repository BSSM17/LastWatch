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

        public TraversalCommandStatus BeginNormalDescent()
        {
            if (state != TraversalState.None) return TraversalCommandStatus.RejectedBusy;
            if (zone != LocationZone.UpperSafeZone) return TraversalCommandStatus.RejectedInvalidZone;
            Begin(TraversalState.Descending, NormalDescentTicks, upperPosition, groundPosition,
                TraversalPlatformNodeId.UpperDeck);
            return TraversalCommandStatus.Started;
        }

        public TraversalCommandStatus BeginNormalAscent()
        {
            if (state != TraversalState.None) return TraversalCommandStatus.RejectedBusy;
            if (zone != LocationZone.GroundDanger) return TraversalCommandStatus.RejectedInvalidZone;
            Begin(TraversalState.Ascending, NormalAscentTicks, groundPosition, upperPosition,
                TraversalPlatformNodeId.GroundLanding);
            return TraversalCommandStatus.Started;
        }

        public TraversalStepStatus AdvanceOneTick(bool inputHeld, bool largeDamage)
        {
            if (state == TraversalState.None) return TraversalStepStatus.NotTraversing;
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
            Vector3 position = state == TraversalState.None
                ? PositionFor(lastPassedNode)
                : Vector3.Lerp(start, end, (float)elapsedTicks / totalTicks);
            return new TraversalSnapshot(state, zone, lastPassedNode, elapsedTicks, totalTicks, position);
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
