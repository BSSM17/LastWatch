using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastWatch.Core.Space
{
    [DisallowMultipleComponent]
    public sealed class M2GrayboxBuilder : MonoBehaviour
    {
        private readonly List<Material> generatedMaterials = new List<Material>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();

        public TowerGeometryFixture Geometry { get; private set; }

        public void BuildNormal() => Build(TowerGeometryFixture.CreateNormal());

        public void BuildOneFaceUnavailable(FaceId face) =>
            Build(TowerGeometryFixture.CreateOneFaceUnavailable(face));

        public void Build(TowerGeometryFixture geometry)
        {
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            ClearGeneratedObjects();

            Material ground = CreateMaterial("GroundDanger", new Color(0.18f, 0.2f, 0.19f));
            Material structure = CreateMaterial("TowerStructure", new Color(0.46f, 0.5f, 0.53f));
            Material safe = CreateMaterial("UpperSafeZone", new Color(0.65f, 0.72f, 0.7f));
            Material interaction = CreateMaterial("Interaction", new Color(0.95f, 0.73f, 0.24f));
            Material pattern = CreateMaterial("FacePattern", new Color(0.035f, 0.045f, 0.05f));
            Material route = CreateMaterial("Routes", new Color(0.16f, 0.52f, 0.7f));
            Material los = CreateMaterial("LOS", new Color(0.72f, 0.95f, 0.38f));

            GameObject groundVisual = CreatePrimitive("GroundVisual", PrimitiveType.Cylinder, Vector3.down * 0.15f,
                new Vector3(100f, 0.1f, 100f), ground);
            groundVisual.GetComponent<Collider>().enabled = false;
            // Physical support extends past the 45m Zone boundary to cover the 46m spawn ring.
            GameObject groundSupport = CreatePrimitive("GroundSurface", PrimitiveType.Cube, Vector3.down * 0.15f,
                new Vector3(100f, 0.2f, 100f), ground);
            groundSupport.GetComponent<Renderer>().enabled = false;
            Tag(groundSupport, M2GrayboxColliderKind.GroundSurface, null);
            Tag(CreatePrimitive("TowerCore", PrimitiveType.Cylinder, Vector3.up * 13f,
                new Vector3(1.2f, 13f, 1.2f), structure), M2GrayboxColliderKind.Structure, null);
            GameObject upperDeck = CreatePrimitive("UpperSafeZone_Deck", PrimitiveType.Cylinder,
                Vector3.up * TowerGeometryFixture.UpperDeckY,
                new Vector3(TowerGeometryFixture.UpperDeckRadius * 2f, 0.25f,
                    TowerGeometryFixture.UpperDeckRadius * 2f), safe);
            Tag(upperDeck, M2GrayboxColliderKind.UpperSurface, null);
            if(geometry.ManualAccess==null)ConfigureUpperDeckSupport(upperDeck);
            else CreateManualDeckOpening(upperDeck,geometry.ManualAccess,safe);

            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
            {
                Vector3 direction = TowerGeometryFixture.DirectionFor(face);
                Vector3 supportDirection = Quaternion.Euler(0f, 5f, 0f) * direction;
                bool available = geometry.UnavailableFace != face;
                if (available)
                {
                    Vector3 groundJoint = supportDirection * TowerGeometryFixture.GroundJointRadius + Vector3.up * 0.5f;
                    Vector3 upperJoint = supportDirection * TowerGeometryFixture.UpperDeckRadius +
                                         Vector3.up * (TowerGeometryFixture.UpperDeckY - 0.5f);
                    Tag(CreateBeam($"{face}_SupportLeg", groundJoint, upperJoint, 0.5f, structure),
                        M2GrayboxColliderKind.SupportLeg, face);
                }
                else
                {
                    DrawBrokenFootprint($"{face}_Unavailable", direction, interaction);
                }
                CreateFacePattern(face,
                    direction * TowerGeometryFixture.GroundJointRadius + Vector3.up * 0.18f, pattern, 1.35f);
            }

            if(geometry.ManualAccess==null)CreateLowerAccessFrame(structure,interaction);
            else CreateManualAccess(geometry.ManualAccess,structure,interaction);

            foreach (StationDescriptor station in geometry.Stations)
            {
                if (!station.IsAvailable) continue;
                CreatePrimitive(station.Id.ToString(), PrimitiveType.Cube, station.Position,
                    new Vector3(1.5f, 0.2f, 1.5f), interaction);
                if (station.Face.HasValue) CreateFacePattern(station.Face.Value, station.Position, pattern, 1f);
            }

            foreach (AttackCapsuleDescriptor attack in geometry.AttackPoints)
            {
                if (!attack.IsAvailable) continue;
                Vector3 center = Vector3.Lerp(attack.Bottom, attack.Top, 0.5f);
                float height = Vector3.Distance(attack.Bottom, attack.Top) + attack.Radius * 2f;
                Tag(CreatePrimitive(attack.Id.ToString(), PrimitiveType.Capsule, center,
                    new Vector3(attack.Radius * 2f, height * 0.5f, attack.Radius * 2f), interaction),
                    M2GrayboxColliderKind.AttackCapsule, attack.Face);
            }

            foreach (RouteDescriptor descriptor in geometry.Routes)
            {
                if (!descriptor.IsAvailable) continue;
                CreateLine(descriptor.Id.ToString(), descriptor.ControlPoints, 0.14f, route);
            }

            CreateZoneOverlay(geometry, route);
            CreateLineOfSightOverlay(geometry, los);
        }

        private void CreateLineOfSightOverlay(TowerGeometryFixture geometry, Material material)
        {
            foreach (AttackCapsuleDescriptor attack in geometry.AttackPoints)
            {
                if (!attack.IsAvailable) continue;
                Vector3 target = Vector3.Lerp(attack.Bottom, attack.Top, 0.5f);
                foreach (StationId stationId in TowerLineOfSightContract.GetCanonicalStations(attack.Id))
                {
                    StationDescriptor station = FindStation(geometry, stationId);
                    if (station.IsAvailable)
                        CreateLine($"LOS_{stationId}_{attack.Id}", new[] { station.EyePosition, target }, 0.075f, material);
                }
            }
        }

        private static StationDescriptor FindStation(TowerGeometryFixture geometry, StationId stationId)
        {
            foreach (StationDescriptor station in geometry.Stations)
                if (station.Id == stationId) return station;
            throw new InvalidOperationException($"Missing station {stationId}.");
        }

        private void CreateZoneOverlay(TowerGeometryFixture geometry, Material material)
        {
            foreach (ZoneVolumeDescriptor zone in geometry.Zones)
            {
                if (zone.Zone == State.LocationZone.UpperSafeZone)
                    CreateRing("Zone_UpperSafeZone", Vector3.up * (TowerGeometryFixture.UpperDeckY + 0.35f),
                        TowerGeometryFixture.UpperDeckRadius, material);
                else if (zone.Zone == State.LocationZone.SafePerimeter)
                    CreateRing("Zone_SafePerimeter", Vector3.up * 0.08f,
                        TowerGeometryFixture.SafePerimeterRadius, material);
            }
        }

        private void CreateRing(string objectName, Vector3 center, float radius, Material material)
        {
            const int segments = 96;
            var points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                points[i] = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }
            CreateLine(objectName, points, 0.1f, material);
        }

        private void CreateFacePattern(FaceId face, Vector3 center, Material material, float scale)
        {
            Vector3 origin = center + Vector3.up * 0.16f;
            Vector3 outward = TowerGeometryFixture.DirectionFor(face);
            float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
            if (face == FaceId.FaceA)
            {
                CreatePatternBar("FaceA_Pattern_Vertical", origin, yaw, 0f, material, scale);
            }
            else if (face == FaceId.FaceB)
            {
                CreatePatternBar("FaceB_Pattern_Left", origin, yaw, 45f, material, scale);
                CreatePatternBar("FaceB_Pattern_Right", origin, yaw, -45f, material, scale);
            }
            else
            {
                Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
                CreatePatternBar("FaceC_Pattern_Left", origin - side * 0.28f * scale, yaw, 35f, material, scale);
                CreatePatternBar("FaceC_Pattern_Right", origin + side * 0.28f * scale, yaw, -35f, material, scale);
            }
        }

        private void CreatePatternBar(string objectName, Vector3 position, float yaw, float offsetYaw,
            Material material, float scale)
        {
            GameObject bar = CreatePrimitive(objectName, PrimitiveType.Cube, position,
                new Vector3(0.2f * scale, 0.07f, 1.35f * scale), material);
            bar.transform.rotation = Quaternion.Euler(0f, yaw + offsetYaw, 0f);
        }

        private void CreateManualAccess(M4ManualTraversalAccess access, Material structure, Material interaction)
        {
            for(int i=0;i<access.Nodes.Count;i++)
            {
                // Existing station platform thickness. Tag honestly as solid Structure;
                // do not bypass capsule guards by declaring new UpperSurface geometry.
                var landing=CreatePrimitive("QA_AccessLanding_"+i,PrimitiveType.Cube,
                    access.LandingSurfaceCenter(i)-Vector3.up*0.1f,
                    new Vector3(M4ManualTraversalAccess.Width,0.2f,M4ManualTraversalAccess.Width*0.5f),interaction);
                landing.transform.rotation=Quaternion.LookRotation(access.Outward,Vector3.up);
                Tag(landing,M2GrayboxColliderKind.Structure,null);
                if(i+1<access.Nodes.Count)
                {
                    Vector3 a=access.Nodes[i], b=access.Nodes[i+1], side=access.Side*(M4ManualTraversalAccess.Width*0.5f);
                    Vector3[] top={a-side,a+side,b+side,b-side};
                    var vertices=new List<Vector3>();var indices=new List<int>();
                    AddQuad(vertices,indices,top[3],top[2],top[1],top[0]);
                    AddQuad(vertices,indices,top[0]-Vector3.up*0.2f,top[1]-Vector3.up*0.2f,top[2]-Vector3.up*0.2f,top[3]-Vector3.up*0.2f);
                    for(int edge=0;edge<4;edge++)AddQuad(vertices,indices,top[edge],top[(edge+1)%4],top[(edge+1)%4]-Vector3.up*0.2f,top[edge]-Vector3.up*0.2f);
                    var support=CreateOwnedMesh("QA_AccessConnector_"+i,vertices,indices,structure);
                    Tag(support,M2GrayboxColliderKind.Structure,null);
                    CreateLine("QA_NormalTraversal_"+i,new[]{access.OriginAt(i),access.OriginAt(i+1)},0.14f,interaction);
                }
            }
        }

        private void CreateManualDeckOpening(GameObject deck,M4ManualTraversalAccess access,Material material)
        {
            // Clip the actual primitive mesh, preserving its outer footprint. The only
            // removed solid is the opted-in access notch, not the whole upper floor.
            Mesh source=deck.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] original=source.vertices;int[] triangles=source.triangles;
            var vertices=new[]{new List<Vector3>(),new List<Vector3>(),new List<Vector3>()};
            var indices=new[]{new List<int>(),new List<int>(),new List<int>()};
            for(int n=0;n<triangles.Length;n+=3)
            {
                var triangle=new List<Vector3>{deck.transform.TransformPoint(original[triangles[n]]),
                    deck.transform.TransformPoint(original[triangles[n+1]]),deck.transform.TransformPoint(original[triangles[n+2]])};
                AddPolygon(vertices[0],indices[0],Clip(triangle,access.Outward,access.OpeningStart,true));
                var outward=Clip(triangle,access.Outward,access.OpeningStart,false);
                AddPolygon(vertices[1],indices[1],Clip(outward,access.Side,-M4ManualTraversalAccess.Width*0.5f,true));
                AddPolygon(vertices[2],indices[2],Clip(outward,access.Side,M4ManualTraversalAccess.Width*0.5f,false));
            }
            deck.GetComponent<Collider>().enabled=false;deck.GetComponent<Renderer>().enabled=false;
            // Keep each cell's clipped half-space input separate instead of cooking
            // one hull across the notch. Native cooking/contact still requires QA.
            // The main real cell retains the graph's single-convex support key.
            string[] names={"UpperSafeZone_Deck_Support","QA_UpperDeck_AccessSideNegative","QA_UpperDeck_AccessSidePositive"};
            for(int cell=0;cell<names.Length;cell++)
            {
                var support=CreateOwnedMesh(names[cell],vertices[cell],indices[cell],material);
                support.GetComponent<MeshCollider>().convex=true;
                Tag(support,M2GrayboxColliderKind.UpperSurface,null);
            }
        }

        static List<Vector3> Clip(List<Vector3> polygon,Vector3 normal,float offset,bool less)
        {
            var result=new List<Vector3>();if(polygon.Count==0)return result;
            Vector3 prior=polygon[polygon.Count-1];float priorDistance=Vector3.Dot(prior,normal)-offset;
            bool priorInside=less?priorDistance<=0f:priorDistance>=0f;
            foreach(Vector3 point in polygon)
            {
                float distance=Vector3.Dot(point,normal)-offset;bool inside=less?distance<=0f:distance>=0f;
                if(inside!=priorInside)result.Add(Vector3.Lerp(prior,point,priorDistance/(priorDistance-distance)));
                if(inside)result.Add(point);
                prior=point;priorDistance=distance;priorInside=inside;
            }
            return result;
        }
        static void AddPolygon(List<Vector3> vertices,List<int> indices,List<Vector3> polygon)
        {
            for(int i=1;i+1<polygon.Count;i++)
            {if(Vector3.Cross(polygon[i]-polygon[0],polygon[i+1]-polygon[0]).sqrMagnitude==0f)continue;
                int start=vertices.Count;vertices.Add(polygon[0]);vertices.Add(polygon[i]);vertices.Add(polygon[i+1]);indices.Add(start);indices.Add(start+1);indices.Add(start+2);}
        }
        static void AddQuad(List<Vector3> vertices,List<int> indices,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            =>AddPolygon(vertices,indices,new List<Vector3>{a,b,c,d});
        GameObject CreateOwnedMesh(string name,List<Vector3> vertices,List<int> indices,Material material)
        {
            var value=new GameObject(name);value.transform.SetParent(transform,false);
            var local=new Vector3[vertices.Count];for(int i=0;i<local.Length;i++)local[i]=transform.InverseTransformPoint(vertices[i]);
            var mesh=new Mesh{name=name};mesh.vertices=local;mesh.triangles=indices.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            generatedMeshes.Add(mesh);value.AddComponent<MeshFilter>().sharedMesh=mesh;
            value.AddComponent<MeshRenderer>().sharedMaterial=material;value.AddComponent<MeshCollider>().sharedMesh=mesh;
            return value;
        }

        private void CreateLowerAccessFrame(Material structure, Material interaction)
        {
            Vector3 direction = (TowerGeometryFixture.DirectionFor(FaceId.FaceC) +
                                 TowerGeometryFixture.DirectionFor(FaceId.FaceA)).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 center = direction * 8.5f;
            CreateBeam("LowerAccess_Frame_Left", center - side * 1.15f, center - side * 1.15f + Vector3.up * 27f,
                0.22f, structure);
            CreateBeam("LowerAccess_Frame_Right", center + side * 1.15f, center + side * 1.15f + Vector3.up * 27f,
                0.22f, structure);
            for (int y = 2; y <= 26; y += 3)
                CreateBeam($"LowerAccess_Rung_{y}", center - side * 1.15f + Vector3.up * y,
                    center + side * 1.15f + Vector3.up * y, 0.12f, interaction);
        }

        private void DrawBrokenFootprint(string objectName, Vector3 direction, Material material)
        {
            Vector3 center = direction * TowerGeometryFixture.UpperDeckRadius +
                             Vector3.up * (TowerGeometryFixture.UpperDeckY + 0.12f);
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            CreateLine(objectName, new[]
            {
                center - side * 1.1f - direction * 0.6f,
                center + side * 0.2f,
                center - side * 0.15f + direction * 0.5f,
                center + side * 1.1f + direction * 0.1f
            }, 0.18f, material);
        }

        private GameObject CreatePrimitive(string objectName, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject value = GameObject.CreatePrimitive(type);
            value.name = objectName;
            value.transform.SetParent(transform, false);
            value.transform.position = position;
            value.transform.localScale = scale;
            Renderer renderer = value.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            return value;
        }

        private static void ConfigureUpperDeckSupport(GameObject deck)
        {
            MeshFilter filter = deck.GetComponent<MeshFilter>();
            Collider original = deck.GetComponent<Collider>();
            if (filter == null || filter.sharedMesh == null || original == null)
                throw new InvalidOperationException("Upper deck requires its actual render mesh and original collider.");
            // The primitive capsule cannot flatten into the rendered circular deck.
            // Preserve it disabled and use the same mesh for actual world support.
            var supportObject = new GameObject("UpperSafeZone_Deck_Support");
            supportObject.transform.SetParent(deck.transform, false);
            supportObject.layer = deck.layer;
            supportObject.tag = deck.tag;
            supportObject.isStatic = deck.isStatic;
            MeshCollider support = supportObject.AddComponent<MeshCollider>();
            support.convex = true;
            support.sharedMesh = filter.sharedMesh;
            support.sharedMaterial = original.sharedMaterial;
            support.isTrigger = original.isTrigger;
            support.contactOffset = original.contactOffset;
            support.enabled = original.enabled;
            Tag(supportObject, M2GrayboxColliderKind.UpperSurface, null);
            original.enabled = false;
        }

        private GameObject CreateBeam(string objectName, Vector3 start, Vector3 end, float width, Material material)
        {
            Vector3 direction = end - start;
            GameObject beam = CreatePrimitive(objectName, PrimitiveType.Cube, Vector3.Lerp(start, end, 0.5f),
                new Vector3(width, width, direction.magnitude), material);
            beam.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return beam;
        }

        private void CreateLine(string objectName, IReadOnlyList<Vector3> points, float width, Material material)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = points.Count;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        private Material CreateMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("No compatible graybox shader is available.");
            var material = new Material(shader) { name = materialName, color = color };
            generatedMaterials.Add(material);
            return material;
        }

        private static void Tag(GameObject target, M2GrayboxColliderKind kind, FaceId? face)
        {
            target.AddComponent<M2GrayboxColliderTag>().Initialize(kind, face);
        }

        // Caller must own this freshly-created builder/root; no public live-world cleanup entry.
        internal UnityEngine.Object[] CaptureOwnedGeneratedObjects()
        {
            var owned = new List<UnityEngine.Object>();
            for (int index = 0; index < transform.childCount; index++)
                owned.Add(transform.GetChild(index).gameObject);
            owned.AddRange(generatedMaterials);
            owned.AddRange(generatedMeshes);
            return owned.ToArray();
        }

        internal void ReleaseOwnedGeneratedObjects() => ClearGeneratedObjects();

        private void ClearGeneratedObjects()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            foreach (Material material in generatedMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
            generatedMaterials.Clear();
            foreach(Mesh mesh in generatedMeshes)
                if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            generatedMeshes.Clear();
        }
    }

    public enum M2GrayboxColliderKind
    {
        GroundSurface,
        UpperSurface,
        SupportLeg,
        Structure,
        AttackCapsule
    }

    [DisallowMultipleComponent]
    public sealed class M2GrayboxColliderTag : MonoBehaviour
    {
        public M2GrayboxColliderKind Kind { get; private set; }
        public FaceId? Face { get; private set; }

        internal void Initialize(M2GrayboxColliderKind kind, FaceId? face)
        {
            Kind = kind;
            Face = face;
        }
    }
}
