using System;
using System.Collections.Generic;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Space
{
    public enum FaceId { FaceA, FaceB, FaceC }
    public enum StationId { S_0, S_A, S_B, S_C }
    public enum AttackPointId { AP_A_G, AP_A_C, AP_B_G, AP_B_C, AP_C_G, AP_C_C }
    public enum RouteId
    {
        LaneA, ApproachA, ClimberRailA,
        LaneB, ApproachB, ClimberRailB,
        LaneC, ApproachC, ClimberRailC
    }
    public enum RouteKind { SpawnLane, ApproachSpline, ClimberRail }
    public enum AttackPointKind { GroundStructure, ClimbJoint }
    public enum ZoneShapeKind { Cylinder, Box, OutsideCylinder }

    public readonly struct StationDescriptor
    {
        public StationId Id { get; }
        public FaceId? Face { get; }
        public Vector3 Position { get; }
        public Vector3 EyePosition { get; }
        public bool IsAvailable { get; }

        public StationDescriptor(StationId id, FaceId? face, Vector3 position, Vector3 eyePosition, bool isAvailable)
        {
            Id = id;
            Face = face;
            Position = position;
            EyePosition = eyePosition;
            IsAvailable = isAvailable;
        }
    }

    public readonly struct AttackCapsuleDescriptor
    {
        public AttackPointId Id { get; }
        public FaceId Face { get; }
        public AttackPointKind Kind { get; }
        public Vector3 Bottom { get; }
        public Vector3 Top { get; }
        public float Radius { get; }
        public bool IsAvailable { get; }

        public AttackCapsuleDescriptor(AttackPointId id, FaceId face, AttackPointKind kind,
            Vector3 bottom, Vector3 top, float radius, bool isAvailable)
        {
            if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius));
            Id = id;
            Face = face;
            Kind = kind;
            Bottom = bottom;
            Top = top;
            Radius = radius;
            IsAvailable = isAvailable;
        }

        public Vector3[] GetLineOfSightSamples()
        {
            Vector3 middle = Vector3.Lerp(Bottom, Top, 0.5f);
            return new[] { Top, middle, Bottom };
        }
    }

    public sealed class RouteDescriptor
    {
        private readonly Vector3[] controlPoints;

        public RouteId Id { get; }
        public FaceId Face { get; }
        public RouteKind Kind { get; }
        public bool IsAvailable { get; }
        public IReadOnlyList<Vector3> ControlPoints => Array.AsReadOnly(controlPoints);

        public RouteDescriptor(RouteId id, FaceId face, RouteKind kind, Vector3[] points, bool isAvailable)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("A route needs at least two points.", nameof(points));
            Id = id;
            Face = face;
            Kind = kind;
            controlPoints = (Vector3[])points.Clone();
            IsAvailable = isAvailable;
        }

        public Vector3[] Sample(float spacingMeters)
        {
            if (spacingMeters <= 0f || float.IsNaN(spacingMeters) || float.IsInfinity(spacingMeters))
                throw new ArgumentOutOfRangeException(nameof(spacingMeters));

            var samples = new List<Vector3> { controlPoints[0] };
            for (int segment = 0; segment < controlPoints.Length - 1; segment++)
            {
                Vector3 start = controlPoints[segment];
                Vector3 end = controlPoints[segment + 1];
                float distance = Vector3.Distance(start, end);
                int steps = Math.Max(1, Mathf.CeilToInt(distance / spacingMeters));
                for (int i = 1; i <= steps; i++)
                {
                    // Even subdivision avoids a precision-only near-end sample followed by
                    // the exact endpoint when distance / spacing rounds just above an integer.
                    // Every interval remains positive and no greater than spacingMeters.
                    samples.Add(Vector3.Lerp(start, end, (float)i / steps));
                }
            }
            return samples.ToArray();
        }
    }

    public readonly struct ZoneVolumeDescriptor
    {
        public LocationZone Zone { get; }
        public ZoneShapeKind Shape { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public float InnerRadius { get; }
        public float OuterRadius { get; }
        public float MinimumY { get; }
        public float MaximumY { get; }

        public ZoneVolumeDescriptor(LocationZone zone, ZoneShapeKind shape, Vector3 center, Vector3 size,
            float innerRadius, float outerRadius, float minimumY, float maximumY)
        {
            if (maximumY < minimumY || innerRadius < 0f || outerRadius < innerRadius)
                throw new ArgumentException("Invalid zone bounds.");
            Zone = zone;
            Shape = shape;
            Center = center;
            Size = size;
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
            MinimumY = minimumY;
            MaximumY = maximumY;
        }

        public bool Contains(Vector3 point)
        {
            if (point.y < MinimumY || point.y > MaximumY) return false;
            if (Shape == ZoneShapeKind.Box)
            {
                Vector3 delta = point - Center;
                return Math.Abs(delta.x) <= Size.x * 0.5f && Math.Abs(delta.y) <= Size.y * 0.5f &&
                       Math.Abs(delta.z) <= Size.z * 0.5f;
            }
            Vector2 flat = new Vector2(point.x - Center.x, point.z - Center.z);
            float radius = flat.magnitude;
            if (Shape == ZoneShapeKind.OutsideCylinder) return radius >= InnerRadius;
            return radius >= InnerRadius && radius <= OuterRadius;
        }
    }
}
