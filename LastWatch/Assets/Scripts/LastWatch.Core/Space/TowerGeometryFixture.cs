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
        public M4ManualTraversalAccess ManualAccess { get; }
        public IReadOnlyList<StationDescriptor> Stations => Array.AsReadOnly(stations);
        public IReadOnlyList<AttackCapsuleDescriptor> AttackPoints => Array.AsReadOnly(attackPoints);
        public IReadOnlyList<RouteDescriptor> Routes => Array.AsReadOnly(routes);
        public IReadOnlyList<ZoneVolumeDescriptor> Zones => Array.AsReadOnly(zones);

        private TowerGeometryFixture(FaceId? unavailableFace, bool manualTraversal = false)
        {
            UnavailableFace = unavailableFace;
            stations = BuildStations(unavailableFace);
            attackPoints = BuildAttackPoints(unavailableFace);
            routes = BuildRoutes(unavailableFace);
            zones = BuildZones();
            ManualAccess = manualTraversal ? new M4ManualTraversalAccess(zones) : null;
        }

        public static TowerGeometryFixture CreateNormal() => new TowerGeometryFixture(null);
        public static TowerGeometryFixture CreateManualTraversalQa() => new TowerGeometryFixture(null, true);

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

    public readonly struct ManualTraversalEntryEvaluation
    {
        public bool Eligible { get; }
        public bool Ascending { get; }
        public bool IsRecovery { get; }
        public int FootprintNode { get; }
        public string Reason { get; }
        public ManualTraversalEntryEvaluation(bool eligible,bool ascending,int footprintNode,string reason,bool recovery=false)
        {Eligible=eligible;Ascending=ascending;FootprintNode=footprintNode;Reason=reason;IsRecovery=recovery;}
    }

    // Explicit current-QA input. Shared by traversal, entry admission and builder;
    // these are legacy prototype node poses, not a verified release collision mesh.
    public sealed class M4ManualTraversalAccess
    {
        public const float Width = 2.3f; // existing lower-frame side-to-side width
        public const float OriginLift = 0.35f; // existing QA player-origin offset
        readonly Vector3[] nodes;
        readonly IReadOnlyList<ZoneVolumeDescriptor> actualZones;
        public IReadOnlyList<Vector3> Nodes => Array.AsReadOnly(nodes);
        public Vector3 Outward => (nodes[3]-new Vector3(0f,nodes[3].y,0f)).normalized;
        public Vector3 Side => Vector3.Cross(Vector3.up,Outward).normalized;
        public float OpeningStart => Vector3.Dot(nodes[0],Outward);
        public bool IsDeckOpening(Vector3 origin) => Vector3.Dot(origin,Outward)>OpeningStart&&Mathf.Abs(Vector3.Dot(origin,Side))<Width*0.5f;
        internal M4ManualTraversalAccess(IReadOnlyList<ZoneVolumeDescriptor> zones)
        {
            actualZones=zones??throw new ArgumentNullException(nameof(zones));
            Vector3 direction = (TowerGeometryFixture.DirectionFor(FaceId.FaceC) + TowerGeometryFixture.DirectionFor(FaceId.FaceA)).normalized;
            nodes = new[] { direction*5.5f+Vector3.up*28f, direction*6.5f+Vector3.up*24f,
                direction*8.5f+Vector3.up*14f, direction*10.5f };
        }
        public Vector3 OriginAt(int node) => nodes[node] + Vector3.up*OriginLift;
        public bool TryClassifyOrigin(Vector3 origin,out LocationZone zone)=>TryClassify(origin,actualZones,out zone);
        public bool ContainsEntry(Vector3 origin, bool upper)
        {
            Vector3 delta = origin-OriginAt(upper?0:3);
            Vector2 depthBounds=EntryDepthBounds(upper?0:3);
            float depth=Vector3.Dot(delta,Outward);
            return Mathf.Abs(Vector3.Dot(delta,Side))<=Width*0.5f &&
                depth>=depthBounds.x && depth<=depthBounds.y && delta.y==0f;
        }
        public Vector2 EntryDepthBounds(int node)
        {
            if(node<0||node>=nodes.Length)throw new ArgumentOutOfRangeException(nameof(node));
            return node==3?new Vector2(0f,Width*0.5f):new Vector2(-Width*0.5f,0f);
        }
        public Vector3 LandingSurfaceCenter(int node)
        {
            Vector2 depthBounds=EntryDepthBounds(node);
            return nodes[node]+Outward*((depthBounds.x+depthBounds.y)*0.5f);
        }
        public ManualTraversalEntryEvaluation EvaluateNormalEntry(Vector3 origin)
        {
            if(!TryClassifyOrigin(origin,out LocationZone actual)||
                (actual!=LocationZone.UpperSafeZone&&actual!=LocationZone.GroundDanger))
                return new ManualTraversalEntryEvaluation(false,false,-1,"표시된 입구로 이동하세요");
            bool ascend=actual==LocationZone.GroundDanger;
            bool eligible=ContainsEntry(origin,!ascend);
            return new ManualTraversalEntryEvaluation(eligible,ascend,ascend?3:0,eligible?null:"표시된 입구 안으로 이동하세요");
        }
        public Vector3 EntryCorner(int node,int corner)
        {
            if(node<0||node>=nodes.Length||corner<0||corner>3)throw new ArgumentOutOfRangeException();
            float side=corner==0||corner==3?-Width*0.5f:Width*0.5f;
            Vector2 depthBounds=EntryDepthBounds(node);
            float depth=corner<2?depthBounds.x:depthBounds.y;
            return OriginAt(node)+Side*side+Outward*depth;
        }
        public bool TryClassify(Vector3 origin, IReadOnlyList<ZoneVolumeDescriptor> zones, out LocationZone zone)
        {
            for(int i=0;i<zones.Count;i++)if(zones[i].Zone==LocationZone.UpperSafeZone&&zones[i].Contains(origin))
            {zone=zones[i].Zone;return true;}
            for(int i=0;i<zones.Count;i++)if(zones[i].Zone==LocationZone.GroundDanger&&zones[i].Contains(origin))
            {zone=zones[i].Zone;return true;}
            for(int i=0;i<nodes.Length-1;i++)
            {
                Vector3 a=OriginAt(i), b=OriginAt(i+1), edge=b-a;
                float t=Mathf.Clamp01(Vector3.Dot(origin-a,edge)/edge.sqrMagnitude);
                if((origin-Vector3.Lerp(a,b,t)).sqrMagnitude<=Width*Width*0.25f)
                {zone=LocationZone.LowerAccess;return true;}
            }
            zone=default;return false;
        }
    }
}
