using System;
using System.Collections.Generic;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Space
{
    public sealed class TowerGeometryFixture
    {
        public const float UpperDeckY = 28f;
        public const float UpperDeckRadius = 6.5f;
        public const float GroundJointRadius = 10.5f;
        public const float SpawnRingMinimumRadius = 38f;
        public const float SpawnRingMaximumRadius = 46f;
        public const float SafePerimeterRadius = 45f;
        public const float LineOfSightSampleSpacing = 0.5f;

        private readonly StationDescriptor[] stations;
        private readonly AttackCapsuleDescriptor[] attackPoints;
        private readonly RouteDescriptor[] routes;
        private readonly ZoneVolumeDescriptor[] zones;

        public FaceId? UnavailableFace { get; }
        public IReadOnlyList<StationDescriptor> Stations => Array.AsReadOnly(stations);
        public IReadOnlyList<AttackCapsuleDescriptor> AttackPoints => Array.AsReadOnly(attackPoints);
        public IReadOnlyList<RouteDescriptor> Routes => Array.AsReadOnly(routes);
        public IReadOnlyList<ZoneVolumeDescriptor> Zones => Array.AsReadOnly(zones);

        private TowerGeometryFixture(FaceId? unavailableFace)
        {
            UnavailableFace = unavailableFace;
            stations = BuildStations(unavailableFace);
            attackPoints = BuildAttackPoints(unavailableFace);
            routes = BuildRoutes(unavailableFace);
            zones = BuildZones();
        }

        public static TowerGeometryFixture CreateNormal() => new TowerGeometryFixture(null);

        public static TowerGeometryFixture CreateOneFaceUnavailable(FaceId unavailableFace)
        {
            return new TowerGeometryFixture(unavailableFace);
        }

        public static Vector3 DirectionFor(FaceId face)
        {
            switch (face)
            {
                case FaceId.FaceA: return Vector3.forward;
                case FaceId.FaceB: return new Vector3(0.8660254f, 0f, -0.5f);
                case FaceId.FaceC: return new Vector3(-0.8660254f, 0f, -0.5f);
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        private static StationDescriptor[] BuildStations(FaceId? unavailable)
        {
            var values = new List<StationDescriptor>
            {
                new StationDescriptor(StationId.S_0, null, new Vector3(0f, UpperDeckY + 0.25f, 0f),
                    new Vector3(0f, UpperDeckY + 1.65f, 0f), true)
            };
            AddStation(values, StationId.S_A, FaceId.FaceA, unavailable);
            AddStation(values, StationId.S_B, FaceId.FaceB, unavailable);
            AddStation(values, StationId.S_C, FaceId.FaceC, unavailable);
            return values.ToArray();
        }

        private static void AddStation(List<StationDescriptor> values, StationId id, FaceId face, FaceId? unavailable)
        {
            Vector3 point = DirectionFor(face) * 5f + Vector3.up * (UpperDeckY + 0.25f);
            values.Add(new StationDescriptor(id, face, point, point + Vector3.up * 1.4f, unavailable != face));
        }

        private static AttackCapsuleDescriptor[] BuildAttackPoints(FaceId? unavailable)
        {
            var values = new List<AttackCapsuleDescriptor>();
            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
            {
                Vector3 direction = DirectionFor(face);
                bool available = unavailable != face;
                values.Add(new AttackCapsuleDescriptor(GroundAttackId(face), face, AttackPointKind.GroundStructure,
                    direction * GroundJointRadius + Vector3.up * 0.25f,
                    direction * GroundJointRadius + Vector3.up * 1.75f, 0.5f, available));
                values.Add(new AttackCapsuleDescriptor(ClimbAttackId(face), face, AttackPointKind.ClimbJoint,
                    direction * UpperDeckRadius + Vector3.up * 23f,
                    direction * UpperDeckRadius + Vector3.up * 25f, 0.5f, available));
            }
            return values.ToArray();
        }

        private static RouteDescriptor[] BuildRoutes(FaceId? unavailable)
        {
            var values = new List<RouteDescriptor>();
            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
            {
                Vector3 direction = DirectionFor(face);
                bool available = unavailable != face;
                values.Add(new RouteDescriptor(LaneId(face), face, RouteKind.SpawnLane,
                    new[] { direction * SpawnRingMaximumRadius + Vector3.up * 0.5f,
                            direction * SpawnRingMinimumRadius + Vector3.up * 0.5f }, available));
                values.Add(new RouteDescriptor(ApproachId(face), face, RouteKind.ApproachSpline,
                    new[] { direction * SpawnRingMinimumRadius + Vector3.up * 0.5f,
                            direction * 24f + Vector3.up * 0.5f,
                            direction * GroundJointRadius + Vector3.up * 0.5f }, available));
                values.Add(new RouteDescriptor(ClimberId(face), face, RouteKind.ClimberRail,
                    new[] { direction * GroundJointRadius + Vector3.up * 0.5f,
                            direction * 8.5f + Vector3.up * 12f,
                            direction * UpperDeckRadius + Vector3.up * 24f }, available));
            }
            return values.ToArray();
        }

        private static ZoneVolumeDescriptor[] BuildZones()
        {
            Vector3 accessDirection = (DirectionFor(FaceId.FaceC) + DirectionFor(FaceId.FaceA)).normalized;
            Vector3 accessCenter = accessDirection * 8.5f + Vector3.up * 14f;
            return new[]
            {
                new ZoneVolumeDescriptor(LocationZone.UpperSafeZone, ZoneShapeKind.Cylinder, Vector3.zero,
                    Vector3.zero, 0f, UpperDeckRadius, UpperDeckY - 0.5f, UpperDeckY + 3f),
                new ZoneVolumeDescriptor(LocationZone.LowerAccess, ZoneShapeKind.Box, accessCenter,
                    new Vector3(4f, 28f, 4f), 0f, 0f, 0f, UpperDeckY),
                new ZoneVolumeDescriptor(LocationZone.GroundDanger, ZoneShapeKind.Cylinder, Vector3.zero,
                    Vector3.zero, 0f, SafePerimeterRadius, -1f, 4f),
                new ZoneVolumeDescriptor(LocationZone.SafePerimeter, ZoneShapeKind.OutsideCylinder, Vector3.zero,
                    Vector3.zero, SafePerimeterRadius, float.MaxValue, -1f, 4f)
            };
        }

        private static AttackPointId GroundAttackId(FaceId face) =>
            face == FaceId.FaceA ? AttackPointId.AP_A_G : face == FaceId.FaceB ? AttackPointId.AP_B_G : AttackPointId.AP_C_G;
        private static AttackPointId ClimbAttackId(FaceId face) =>
            face == FaceId.FaceA ? AttackPointId.AP_A_C : face == FaceId.FaceB ? AttackPointId.AP_B_C : AttackPointId.AP_C_C;
        private static RouteId LaneId(FaceId face) =>
            face == FaceId.FaceA ? RouteId.LaneA : face == FaceId.FaceB ? RouteId.LaneB : RouteId.LaneC;
        private static RouteId ApproachId(FaceId face) =>
            face == FaceId.FaceA ? RouteId.ApproachA : face == FaceId.FaceB ? RouteId.ApproachB : RouteId.ApproachC;
        private static RouteId ClimberId(FaceId face) =>
            face == FaceId.FaceA ? RouteId.ClimberRailA : face == FaceId.FaceB ? RouteId.ClimberRailB : RouteId.ClimberRailC;
    }

    public static class TowerLineOfSightContract
    {
        private static readonly Dictionary<AttackPointId, StationId[]> Matrix = new Dictionary<AttackPointId, StationId[]>
        {
            { AttackPointId.AP_A_G, new[] { StationId.S_0, StationId.S_A, StationId.S_B } },
            { AttackPointId.AP_A_C, new[] { StationId.S_0, StationId.S_A, StationId.S_C } },
            { AttackPointId.AP_B_G, new[] { StationId.S_0, StationId.S_A, StationId.S_B, StationId.S_C } },
            { AttackPointId.AP_B_C, new[] { StationId.S_0, StationId.S_B } },
            { AttackPointId.AP_C_G, new[] { StationId.S_0, StationId.S_A, StationId.S_C } },
            { AttackPointId.AP_C_C, new[] { StationId.S_0, StationId.S_B, StationId.S_C } }
        };

        public static StationId[] GetCanonicalStations(AttackPointId attackPoint)
        {
            if (!Matrix.TryGetValue(attackPoint, out StationId[] values))
                throw new ArgumentOutOfRangeException(nameof(attackPoint));
            return (StationId[])values.Clone();
        }

        public static bool IsCanonicalPair(StationId station, AttackPointId attackPoint)
        {
            StationId[] values = GetCanonicalStations(attackPoint);
            return Array.IndexOf(values, station) >= 0;
        }
    }
}
