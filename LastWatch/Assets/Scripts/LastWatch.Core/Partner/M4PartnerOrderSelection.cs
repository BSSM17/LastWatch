using System;
using System.Collections.Generic;
using System.Linq;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Functional QA command HUD only. It never owns colliders, movement or role state.
    // Values are QA_INITIAL_TUNING approved in the v3.7 implementation packet.
    internal sealed class M4PartnerOrderSelection : IDisposable
    {
        const float HalfWidth = 7f;
        const float HalfHeight = 4f;
        readonly List<Region> regions = new List<Region>();
        readonly Material material;
        bool visible;
        public string UnavailableReason { get; }

        sealed class Region
        {
            public FaceId Face;
            public Vector3 Center, Normal, Right;
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
        }

        public M4PartnerOrderSelection(Transform owner, TowerGeometryFixture geometry)
        {
            // Editor QA source delivery only: Shader.Find is deliberately fail-closed.
            // No Resources search, Always Included setting or build-strip claim.
            Shader shader = Shader.Find("LastWatch/QA/PartnerOrderSelection");
            if (shader == null || !shader.isSupported || geometry == null)
            { UnavailableReason = "QA command HUD shader/geometry unavailable"; return; }
            material = new Material(shader);
            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
            {
                StationDescriptor[] stations = geometry.Stations.Where(s => s.Face == face && s.IsAvailable).ToArray();
                if (stations.Length != 1) continue;
                Vector3 normal = TowerGeometryFixture.DirectionFor(face).normalized;
                var region = new Region { Face = face, Normal = normal,
                    Center = stations[0].EyePosition + normal * 9f, Right = Vector3.Cross(Vector3.up, normal).normalized };
                region.Object = new GameObject("QA_CommandHUD_" + face);
                region.Object.transform.SetParent(owner, false);
                // Mesh vertices are explicit world-linked positions; parent pose is cancelled.
                region.Object.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                region.Object.transform.localScale = Vector3.one;
                region.Mesh = BuildMesh(region);
                region.Object.AddComponent<MeshFilter>().sharedMesh = region.Mesh;
                region.Renderer = region.Object.AddComponent<MeshRenderer>();
                region.Renderer.sharedMaterial = material;
                region.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                region.Renderer.receiveShadows = false;
                region.Object.SetActive(false);
                regions.Add(region);
            }
            if (regions.Count == 0) UnavailableReason = "unique available Face stations unavailable";
        }

        static Mesh BuildMesh(Region r)
        {
            Vector3[] outer = Corners(r, HalfWidth, HalfHeight);
            Vector3[] inner = Corners(r, HalfWidth - .08f, HalfHeight - .08f);
            var vertices = new Vector3[12];
            Array.Copy(outer, vertices, 4); Array.Copy(outer, 0, vertices, 4, 4); Array.Copy(inner, 0, vertices, 8, 4);
            var triangles = new List<int> { 0, 1, 2, 0, 2, 3 };
            for (int i = 0; i < 4; i++)
            {
                int n = (i + 1) % 4;
                triangles.AddRange(new[] { 4 + i, 4 + n, 8 + n, 4 + i, 8 + n, 8 + i });
            }
            var mesh = new Mesh { name = "QA Face selection / same bounded plane" };
            mesh.vertices = vertices; mesh.triangles = triangles.ToArray();
            Paint(mesh, false, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3[] Corners(Region r, float width, float height) => new[] {
            r.Center - r.Right * width - Vector3.up * height,
            r.Center + r.Right * width - Vector3.up * height,
            r.Center + r.Right * width + Vector3.up * height,
            r.Center - r.Right * width + Vector3.up * height };

        static void Paint(Mesh mesh, bool selected, bool enabled)
        {
            Color color = enabled ? (selected ? Color.yellow : Color.cyan) : Color.gray;
            var colors = new Color[12];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(color.r, color.g, color.b,
                i < 4 ? (selected ? .18f : .08f) : .8f);
            mesh.colors = colors;
        }

        public void Show(bool show, FaceId? selected, bool armed)
        {
            visible = show;
            foreach (Region r in regions)
            {
                r.Object.SetActive(show);
                Paint(r.Mesh, armed && selected == r.Face, armed);
            }
        }

        public bool TrySelect(Camera camera, out FaceId face, out string reason)
        {
            face = default(FaceId);
            reason = UnavailableReason;
            if (!visible || material == null || material.shader == null || !material.shader.isSupported ||
                camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
            { reason = reason ?? "command HUD/camera unavailable"; return false; }
            Vector3 origin = camera.transform.position, direction = camera.transform.forward;
            int matches = 0;
            foreach (Region r in regions)
            {
                if (!r.Object.activeInHierarchy || !r.Renderer.enabled || r.Renderer.sharedMaterial != material ||
                    r.Object.GetComponent<MeshFilter>().sharedMesh != r.Mesh ||
                    (camera.cullingMask & (1 << r.Object.layer)) == 0) continue;
                float denominator = Vector3.Dot(direction, r.Normal);
                if (denominator <= 0f) continue; // Only outward-facing view from inside the command field.
                float distance = Vector3.Dot(r.Center - origin, r.Normal) / denominator;
                if (float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f) continue;
                Vector3 hit = origin + direction * distance;
                Vector3 local = hit - r.Center;
                Vector3 viewport = camera.WorldToViewportPoint(hit);
                if (viewport.z <= camera.nearClipPlane || viewport.z >= camera.farClipPlane ||
                    viewport.x <= 0f || viewport.x >= 1f || viewport.y <= 0f || viewport.y >= 1f ||
                    Mathf.Abs(Vector3.Dot(local, r.Right)) >= HalfWidth || Mathf.Abs(local.y) >= HalfHeight) continue;
                matches++; face = r.Face;
            }
            reason = matches == 1 ? string.Empty : matches == 0 ? "no visible Face region under aim" : "ambiguous Face regions";
            return matches == 1;
        }

        public void DrawLabels(Camera camera)
        {
            if (!visible || camera == null) return;
            foreach (Region r in regions)
            {
                Vector3 point = camera.WorldToViewportPoint(r.Center);
                if (point.z <= camera.nearClipPlane || point.z >= camera.farClipPlane ||
                    point.x <= 0f || point.x >= 1f || point.y <= 0f || point.y >= 1f) continue;
                string label = r.Face == FaceId.FaceA ? "A / ▲" : r.Face == FaceId.FaceB ? "B / ■" : "C / ◆";
                GUI.Box(new Rect(point.x * Screen.width - 75f, (1f - point.y) * Screen.height - 18f, 150f, 36f), label);
            }
        }

        public void Dispose()
        {
            foreach (Region r in regions) { UnityEngine.Object.Destroy(r.Object); UnityEngine.Object.Destroy(r.Mesh); }
            regions.Clear();
            if (material != null) UnityEngine.Object.Destroy(material);
        }
    }
}
