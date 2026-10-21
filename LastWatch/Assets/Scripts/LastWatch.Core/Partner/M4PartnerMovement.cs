using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Authored input. No default graph, speed, anchor, collider role, or safe node.
    [Serializable] public sealed class M4PartnerMovementProfile
    {
        public string initialNodeId;
        public float metresPerSecond;
        public M4PartnerMovementNode[] nodes;
        public M4PartnerMovementEdge[] edges;
        public string[] safeFallbackOrder;
        public string[] blockerColliderNames;
        public string[] supportColliderNames;
        public string[] presentationColliderNames;
        public M4PartnerColliderSelector[] colliderSelectors;
    }
    [Serializable] public sealed class M4PartnerColliderSelector
    {
        public string key;
        public string colliderName;
        public Vector3 expectedWorldPosition;
    }
    [Serializable] public sealed class M4PartnerMovementNode
    {
        public string id;
        public string stationDescriptorId;
        public string[] aliases;
        public Vector3 bodyAnchor;
        public bool safe;
        public string[] alternativeNodeIds;
        public string[] occupancyColliderNames;
    }
    [Serializable] public sealed class M4PartnerMovementEdge
    {
        public string fromNodeId;
        public string toNodeId;
        public Vector3[] bodyPoints;
    }
    [Serializable] public sealed class M4PartnerAuthoringFacts
    {
        public string scope = "Read-only actual builder/capsule facts; no graph/speed/collider policy assigned.";
        public M4PartnerColliderFact body;
        public float actualCapsuleFootY;
        public float actualCapsuleRadius;
        public Vector3[] actualCapsuleAxis;
        public M4PartnerColliderFact[] builderColliders;
        public string availabilityReason;
    }
    [Serializable] public sealed class M4PartnerColliderFact
    {
        public string name, colliderType;
        public Vector3 position, lossyScale, boundsMinimum, boundsMaximum, localCenter, localSize;
        public Quaternion rotation;
        public string[] exactPositionComponents;
        public float localRadius, localHeight;
        public int capsuleDirection;
        public bool enabled, isTrigger, active;
        // Interpret only when colliderType identifies MeshCollider.
        public bool meshConvex;
    }

    // Canonical persistence. This is a navigation receipt, not playable actor restore.
    [Serializable] public sealed class M4PartnerMovementProgress
    {
        public ulong planId;
        public string runIdentity;
        public int nightOrdinal;
        public int ticksPerSecond = 50;
        public string profileIdentity;
        public string targetNodeId;
        public string lastValidNodeId;
        public Vector3 position;
        public Vector3[] points;
        public string[] nodeAtPoint;
        public int[] edgeAtPoint;
        public int[] blockedEdges;
        public int[] laneBlockedEdges;
        public ulong laneBlockedSequence;
        public int cursor;
        public int startTick;
        public int lastTick;
        public int lastProgressTick;
        public bool completed;
        public bool cancelled;
        public string reason;
    }

    public static class M4PartnerMovementPersistence
    {
        public static void Validate(PartnerRuntimeState state)
        {
            if (state.NextMovementPlanId == 0) throw new FormatException("Invalid Partner movement plan cursor.");
            var p = state.MovementProgress;
            if (p == null) return;
            if (p.planId == 0 || p.planId >= state.NextMovementPlanId || string.IsNullOrWhiteSpace(p.runIdentity) ||
                p.nightOrdinal < 1 || p.nightOrdinal > 3 || p.ticksPerSecond != 50 || string.IsNullOrWhiteSpace(p.profileIdentity) ||
                string.IsNullOrWhiteSpace(p.targetNodeId) || string.IsNullOrWhiteSpace(p.lastValidNodeId) || !Finite(p.position) ||
                p.points == null || p.points.Length == 0 || p.nodeAtPoint == null || p.edgeAtPoint == null || p.blockedEdges == null || p.laneBlockedEdges == null ||
                p.nodeAtPoint.Length != p.points.Length || p.edgeAtPoint.Length != p.points.Length || p.cursor < 0 || p.cursor > p.points.Length ||
                p.startTick < 0 || p.lastTick < p.startTick || p.lastProgressTick < p.startTick || p.lastProgressTick > p.lastTick ||
                (p.completed && p.cancelled) || (p.completed && p.cursor != p.points.Length))
                throw new FormatException("Invalid Partner movement progress/context.");
            for (int i = 0; i < p.points.Length; i++)
                if (!Finite(p.points[i]) || p.edgeAtPoint[i] < -1) throw new FormatException("Invalid Partner movement waypoint.");
            var edges = new HashSet<int>();
            foreach (int edge in p.blockedEdges) if (edge < 0 || !edges.Add(edge)) throw new FormatException("Invalid failed edge receipt.");
            var laneEdges = new HashSet<int>();
            foreach (int edge in p.laneBlockedEdges) if (!edges.Contains(edge) || !laneEdges.Add(edge)) throw new FormatException("Invalid lane edge receipt.");
            if ((p.laneBlockedEdges.Length != 0) != (p.laneBlockedSequence != 0)) throw new FormatException("Invalid lane edge lifetime.");
            bool held = state.ExchangeFlow != null && state.ExchangeFlow.suspended;
            if (!held && !p.completed && !p.cancelled && (state.ReservedNodeId != p.targetNodeId || state.CurrentNodeId != p.lastValidNodeId ||
                (state.Movement != PartnerMovementState.Moving && state.Movement != PartnerMovementState.Reserved && state.Movement != PartnerMovementState.Recovering)))
                throw new FormatException("Partner navigation/reservation mirror mismatch.");
        }
        public static void Cancel(PartnerRuntimeState state, int tick, string reason)
        {
            var p = state.MovementProgress;
            if (p == null || p.completed || p.cancelled) return;
            p.cancelled = true; p.lastTick = tick; p.reason = reason;
            state.ReservedNodeId = null; state.Movement = PartnerMovementState.Stationary;
            state.Decision = PartnerDecisionState.Idle;
        }
        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        internal static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
    }

    // Graph and physical input binding. Input order is the explicit equal-cost policy.
    public sealed class M4PartnerMovement : IPartnerStationGraph
    {
        readonly M4PartnerMovementProfile profile;
        readonly M4PartnerMovementProfile sourceProfile;
        readonly TowerGeometryFixture geometry;
        readonly CapsuleCollider body;
        readonly Collider[] owned;
        readonly Dictionary<string, int> labels = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, Collider> colliders = new Dictionary<string, Collider>(StringComparer.Ordinal);
        readonly HashSet<Collider> blockers = new HashSet<Collider>();
        readonly HashSet<Collider> supports = new HashSet<Collider>();
        readonly HashSet<Collider> presentation = new HashSet<Collider>();
        readonly ZoneVolumeDescriptor upperZone;
        readonly Quaternion bodyRotation;
        readonly string identity;
        public string UnavailableReason { get; private set; }
        public bool Available => UnavailableReason == null;
        public string Receipt { get; private set; } = "Partner movement UNAVAILABLE: authored profile missing.";
        public string LatestRecoveryReceipt { get; private set; } = "No technical recovery recorded.";
        readonly Queue<DiagnosticTransition> diagnosticTransitions = new Queue<DiagnosticTransition>();
        ulong diagnosticSequence;
        // Runtime-only provenance. Canonical navigation receipts/save wire stay unchanged.
        // Each destination retains its original authored segment even after repeated reversals.
        sealed class SegmentReceipt
        {
            public int edge, segment;
            public bool forward;
            public ConnectorReceipt connector;
        }
        sealed class ConnectorReceipt
        {
            public Vector3 start;
            public int target;
            public int[] witnesses;
            // Actual traversed return path to the last reached station, never a visit queue.
            public Vector3[] backPoints;
            public string[] backNodes;
            public int[] backEdges;
            public SegmentReceipt[] backSegments;
        }
        ulong segmentPlanId;
        string segmentRunIdentity;
        Vector3[] segmentPlanPoints;
        SegmentReceipt[] segmentPlan;
        bool connectorFallbackActive;
        [Serializable] sealed class DiagnosticTransition
        {
            public string sequence, planId, runIdentity;
            public bool completed, cancelled;
            public string role, movement, decision, progressPose;
            public int tick, cursor, waypointCount, lastProgressTick, nightOrdinal;
            public string kind, reason, currentNode, requestedNode, lastValidNode, targetNode, profileIdentity;
            public string fromPose, toPose;
            public int[] failedEdges;
            public string[] waypointNodes, waypointPoses;
        }
        [Serializable] sealed class DiagnosticExport
        {
            public string runIdentity, receipt, latestRecovery, actualPose;
            public int runTick;
            public DiagnosticTransition current;
            public DiagnosticTransition[] transitions;
        }
        static string Pose(Vector3 position) => string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R})", position.x, position.y, position.z);
        DiagnosticTransition Snapshot(M4Session session, int tick, string kind, string reason, Vector3 from, int[] failed = null)
        {
            var state = session.RuntimePartner; var p = state.MovementProgress;
            return new DiagnosticTransition { tick = tick, kind = kind, reason = reason, profileIdentity = identity,
                completed = p != null && p.completed, cancelled = p != null && p.cancelled,
                role = state.Role.ToString(), movement = state.Movement.ToString(), decision = state.Decision.ToString(),
                progressPose = p == null ? null : Pose(p.position),
                planId = p == null ? "0" : p.planId.ToString(CultureInfo.InvariantCulture), runIdentity = session.Metadata.RunId.ToString(),
                nightOrdinal = session.ActiveEntry == null ? 0 : session.ActiveEntry.Key.NightOrdinal,
                cursor = p == null ? -1 : p.cursor, waypointCount = p == null || p.points == null ? 0 : p.points.Length,
                lastProgressTick = p == null ? -1 : p.lastProgressTick, currentNode = state.CurrentNodeId, requestedNode = state.ReservedNodeId,
                lastValidNode = p == null ? null : p.lastValidNodeId, targetNode = p == null ? null : p.targetNodeId,
                waypointNodes = p == null || p.nodeAtPoint == null ? Array.Empty<string>() : (string[])p.nodeAtPoint.Clone(),
                waypointPoses = p == null || p.points == null ? Array.Empty<string>() : p.points.Select(Pose).ToArray(),
                fromPose = Pose(from), toPose = body == null ? "UNAVAILABLE" : Pose(body.transform.position), failedEdges = failed ?? (p == null || p.blockedEdges == null ? Array.Empty<int>() : (int[])p.blockedEdges.Clone()) };
        }
        void Trace(M4Session session, int tick, string kind, string reason, Vector3 from, int[] failed = null)
        {
            var observation = Snapshot(session, tick, kind, reason, from, failed); observation.sequence = (++diagnosticSequence).ToString(CultureInfo.InvariantCulture);
            if (diagnosticTransitions.Count >= 64) diagnosticTransitions.Dequeue();
            diagnosticTransitions.Enqueue(observation);
            if (kind.StartsWith("RECOVERY", StringComparison.Ordinal))
                LatestRecoveryReceipt = "#" + observation.sequence + " " + kind + " tick=" + tick + " plan=" + observation.planId +
                    " cursor=" + observation.cursor + "/" + observation.waypointCount + " " + observation.fromPose + " -> " + observation.toPose + " | " + reason;
        }
        public void RecordCommand(M4Session session, int tick, string kind, string reason)
        { if (session != null && body != null) Trace(session, tick, kind, reason, body.transform.position); }
        public string ExportMovementDiagnostics(M4Session session)
        {
            if (session == null || body == null) return "{\"availability\":\"Movement diagnostics require current session and actual body\"}";
            return JsonUtility.ToJson(new DiagnosticExport { runIdentity = session.Metadata.RunId.ToString(), runTick = session.RuntimeRun.FixedTick,
                receipt = Receipt, latestRecovery = LatestRecoveryReceipt, actualPose = Pose(body.transform.position),
                current = Snapshot(session, session.RuntimeRun.FixedTick, "CURRENT_READ", null, body.transform.position), transitions = diagnosticTransitions.ToArray() }, true);
        }

        // 실제 입력 사실만 복사한다. 경로·속도·문양 역할은 생성하거나 선택하지 않는다.
        public string ExportAuthoringFacts()
        {
            var facts = new M4PartnerAuthoringFacts { body = ColliderFact(body),
                builderColliders = owned.Where(c => c != null).Select(ColliderFact).ToArray(), availabilityReason = UnavailableReason };
            if (body != null)
            {
                CapsuleAt(body.transform.position, out Vector3 a, out Vector3 b, out float radius);
                facts.actualCapsuleAxis = new[] { a, b }; facts.actualCapsuleFootY = Mathf.Min(a.y, b.y) - radius; facts.actualCapsuleRadius = radius;
            }
            return JsonUtility.ToJson(facts, true);
        }
        static M4PartnerColliderFact ColliderFact(Collider c)
        {
            if (c == null) return null;
            Vector3 p = c.transform.position;
            var f = new M4PartnerColliderFact { name = c.name, colliderType = c.GetType().FullName, position = p, rotation = c.transform.rotation,
                lossyScale = c.transform.lossyScale, boundsMinimum = c.bounds.min, boundsMaximum = c.bounds.max,
                exactPositionComponents = new[] { p.x.ToString("R", CultureInfo.InvariantCulture), p.y.ToString("R", CultureInfo.InvariantCulture), p.z.ToString("R", CultureInfo.InvariantCulture) },
                enabled = c.enabled, isTrigger = c.isTrigger, active = c.gameObject.activeInHierarchy };
            if (c is BoxCollider box) { f.localCenter = box.center; f.localSize = box.size; }
            else if (c is CapsuleCollider capsule) { f.localCenter = capsule.center; f.localRadius = capsule.radius; f.localHeight = capsule.height; f.capsuleDirection = capsule.direction; }
            else if (c is SphereCollider sphere) { f.localCenter = sphere.center; f.localRadius = sphere.radius; }
            else if (c is MeshCollider mesh)
            {
                f.meshConvex = mesh.convex;
                if (mesh.sharedMesh != null) { f.localCenter = mesh.sharedMesh.bounds.center; f.localSize = mesh.sharedMesh.bounds.size; }
            }
            return f;
        }

        public M4PartnerMovement(M4PartnerMovementProfile input, TowerGeometryFixture actualGeometry, CapsuleCollider actualBody, Collider[] builderOwned)
        {
            sourceProfile = input;
            profile = input == null ? null : JsonUtility.FromJson<M4PartnerMovementProfile>(JsonUtility.ToJson(input));
            geometry = actualGeometry; body = actualBody;
            owned = builderOwned ?? Array.Empty<Collider>();
            bodyRotation = body == null ? Quaternion.identity : body.transform.rotation;
            try
            {
                Require(profile != null && geometry != null && body != null, "authored profile/geometry/actual capsule missing");
                var zones = geometry.Zones.Where(z => z.Zone == LocationZone.UpperSafeZone).ToArray();
                Require(zones.Length == 1 && zones[0].Shape == ZoneShapeKind.Cylinder && zones[0].InnerRadius == 0f,
                    "unique solid cylindrical UpperSafeZone missing");
                upperZone = zones[0];
                Require(M4PartnerMovementPersistence.Finite(profile.metresPerSecond) && profile.metresPerSecond > 0f &&
                    profile.nodes != null && profile.nodes.Length > 0 && profile.edges != null && profile.safeFallbackOrder != null &&
                    profile.safeFallbackOrder.Length > 0 && profile.blockerColliderNames != null && profile.supportColliderNames != null &&
                    profile.supportColliderNames.Length > 0, "incomplete graph/speed/safe/collider-role authoring");
                if (profile.colliderSelectors != null)
                    foreach (var selector in profile.colliderSelectors)
                    {
                        Require(selector != null && !string.IsNullOrWhiteSpace(selector.key) && !string.IsNullOrWhiteSpace(selector.colliderName) &&
                            M4PartnerMovementPersistence.Finite(selector.expectedWorldPosition) && !colliders.ContainsKey(selector.key), "invalid/duplicate explicit collider selector");
                        var matches = owned.Where(c => c != null && c.name == selector.colliderName && c.transform.position.Equals(selector.expectedWorldPosition)).ToArray();
                        Require(matches.Length == 1, "explicit collider selector is not unique: " + selector.key);
                        colliders.Add(selector.key, matches[0]);
                    }
                BindRoles(profile.blockerColliderNames, blockers); BindRoles(profile.supportColliderNames, supports);
                BindRoles(profile.presentationColliderNames ?? Array.Empty<string>(), presentation);
                Require(!blockers.Overlaps(supports) && !blockers.Overlaps(presentation) && !supports.Overlaps(presentation), "collider has conflicting authored roles");
                for (int i = 0; i < profile.nodes.Length; i++)
                {
                    var n = profile.nodes[i]; Require(n != null && M4PartnerMovementPersistence.Finite(n.bodyAnchor) && upperZone.Contains(n.bodyAnchor) &&
                        n.aliases != null && n.alternativeNodeIds != null && n.occupancyColliderNames != null, "invalid authored node");
                    Label(n.id, i); Label(n.stationDescriptorId, i);
                    foreach (string alias in n.aliases) Label(alias, i);
                    Require(geometry.Stations.Count(s => s.Id.ToString() == n.stationDescriptorId && s.IsAvailable) == 1,
                        "unique available station descriptor missing: " + n.stationDescriptorId);
                    var stationBox = BindCollider(n.stationDescriptorId) as BoxCollider;
                    Require(stationBox != null, "actual station BoxCollider missing: " + n.stationDescriptorId);
                    Vector3 stationLocal = stationBox.transform.InverseTransformPoint(n.bodyAnchor) - stationBox.center;
                    Require(Mathf.Abs(stationLocal.x) <= stationBox.size.x * .5f && Mathf.Abs(stationLocal.z) <= stationBox.size.z * .5f,
                        "authored node origin outside actual station footprint: " + n.id);
                    foreach (string name in n.occupancyColliderNames) BindCollider(name);
                }
                Require(Resolve(profile.initialNodeId) >= 0, "initial node/alias missing");
                Require(Resolve("FaceA.Cover") == Resolve(profile.initialNodeId) &&
                    profile.nodes[Resolve(profile.initialNodeId)].stationDescriptorId == "S_A", "initial fixture requires explicit FaceA.Cover alias to S_A");
                var safeIds = new HashSet<int>();
                foreach (string id in profile.safeFallbackOrder)
                { int i = Resolve(id); Require(i >= 0 && profile.nodes[i].safe && safeIds.Add(i), "invalid/duplicate authored safe fallback"); }
                foreach (var n in profile.nodes) foreach (string id in n.alternativeNodeIds) Require(Resolve(id) >= 0, "alternate node missing");
                foreach (var e in profile.edges)
                {
                    Require(e != null && Resolve(e.fromNodeId) >= 0 && Resolve(e.toNodeId) >= 0 && Resolve(e.fromNodeId) != Resolve(e.toNodeId) &&
                        e.bodyPoints != null && e.bodyPoints.Length >= 2, "invalid authored directed edge");
                    Require(e.bodyPoints[0].Equals(profile.nodes[Resolve(e.fromNodeId)].bodyAnchor) &&
                        e.bodyPoints[e.bodyPoints.Length - 1].Equals(profile.nodes[Resolve(e.toNodeId)].bodyAnchor), "edge endpoint/anchor mismatch");
                    for (int i = 0; i < e.bodyPoints.Length; i++) Require(M4PartnerMovementPersistence.Finite(e.bodyPoints[i]) && upperZone.Contains(e.bodyPoints[i]) &&
                        (i == 0 || !e.bodyPoints[i].Equals(e.bodyPoints[i - 1])), "nonfinite/zero-length edge segment");
                }
                identity = Hash(JsonUtility.ToJson(profile) + "|" + BodyIdentity());
                Receipt = "Partner movement authored; initial setup and actual guards pending.";
            }
            catch (InvalidOperationException e) { UnavailableReason = e.Message; Receipt = "Partner movement UNAVAILABLE: " + e.Message; }
        }
        void BindRoles(string[] names, HashSet<Collider> target)
        {
            foreach (string name in names)
            { Collider c = BindCollider(name); Require(c.enabled && !c.isTrigger &&
                c.gameObject.activeInHierarchy && target.Add(c), "missing/disabled/duplicate role collider: " + name); }
        }
        Collider BindCollider(string key)
        {
            Require(!string.IsNullOrWhiteSpace(key), "empty collider key");
            if (colliders.TryGetValue(key, out Collider selected)) return selected;
            var matches = owned.Where(c => c != null && c.name == key).ToArray();
            Require(matches.Length == 1, "name-only collider key missing/ambiguous; explicit selector required: " + key);
            colliders.Add(key, matches[0]); return matches[0];
        }
        void Label(string id, int node)
        {
            Require(!string.IsNullOrWhiteSpace(id), "empty node label");
            Require(!labels.TryGetValue(id, out int previous) || previous == node, "ambiguous node label: " + id);
            labels[id] = node;
        }
        int Resolve(string id) => id != null && labels.TryGetValue(id, out int node) ? node : -1;
        static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        string BodyIdentity() => JsonUtility.ToJson(new BodyShape { radius = body.radius, height = body.height, direction = body.direction,
            center = body.center, scale = body.transform.lossyScale, rotation = bodyRotation });
        [Serializable] sealed class BodyShape { public float radius, height; public int direction; public Vector3 center, scale; public Quaternion rotation; }
        bool IdentityCurrent() => body != null && body.enabled && !body.isTrigger && body.gameObject.activeInHierarchy &&
            body.transform.rotation.Equals(bodyRotation) && JsonUtility.ToJson(sourceProfile) == JsonUtility.ToJson(profile) &&
            identity == Hash(JsonUtility.ToJson(profile) + "|" + BodyIdentity());

        public bool TryInitialSetup(out string initialNode)
        {
            initialNode = null;
            if (!Available) return false;
            int i = Resolve(profile.initialNodeId);
            if (!Guard(profile.nodes[i].bodyAnchor, profile.nodes[i].bodyAnchor, default(M4PartnerMuzzleLane), out string reason) || Occupied(i))
            { UnavailableReason = "initial authored anchor unavailable: " + reason; Receipt = "Partner movement UNAVAILABLE: " + UnavailableReason; return false; }
            body.transform.position = profile.nodes[i].bodyAnchor; // Explicit authored pre-Defense fixture setup only.
            initialNode = profile.nodes[i].id; Receipt = "Authored initial setup: " + initialNode + "; no runtime teleport."; return true;
        }

        public PartnerPathResult Request(string fromNodeId, string targetNodeId, string nearestSafeNodeId, int fixedTick)
        {
            int from = Resolve(fromNodeId), to = Resolve(targetNodeId);
            if (!Available || from < 0 || to < 0) throw new InvalidOperationException("Authored graph/current/target unavailable.");
            if (Route(from, to, new HashSet<int>(), out _))
                return new PartnerPathResult(PartnerPathStatus.Reserved, fromNodeId, targetNodeId, profile.nodes[to].id, fixedTick);
            if (Resolve(nearestSafeNodeId) < 0 || !profile.nodes[Resolve(nearestSafeNodeId)].safe)
                throw new InvalidOperationException("Authored safe fallback missing.");
            return new PartnerPathResult(PartnerPathStatus.NoPathRecoveredToSafeNode, fromNodeId, targetNodeId, nearestSafeNodeId, fixedTick);
        }
        bool Route(int from, int target, HashSet<int> failed, out int[] route, Func<int, bool> validNode = null)
        {
            if (validNode != null && (!validNode(from) || !validNode(target))) { route = null; return false; }
            int count = profile.nodes.Length; var distance = new double[count]; var visited = new bool[count]; var previous = new int[count];
            for (int i = 0; i < count; i++) { distance[i] = double.PositiveInfinity; previous[i] = -1; }
            distance[from] = 0d;
            for (int visit = 0; visit < count; visit++)
            {
                int n = -1;
                for (int i = 0; i < count; i++) if (!visited[i] && (n < 0 || distance[i] < distance[n])) n = i;
                if (n < 0 || double.IsPositiveInfinity(distance[n])) break;
                visited[n] = true; if (n == target) break;
                for (int e = 0; e < profile.edges.Length; e++)
                {
                    var edge = profile.edges[e]; int next = Resolve(edge.toNodeId);
                    if (failed.Contains(e) || Resolve(edge.fromNodeId) != n || visited[next] || (validNode != null && !validNode(next))) continue;
                    double cost = 0d;
                    for (int p = 1; p < edge.bodyPoints.Length; p++) cost += Vector3.Distance(edge.bodyPoints[p - 1], edge.bodyPoints[p]);
                    double candidate = distance[n] + cost;
                    if (candidate < distance[next]) { distance[next] = candidate; previous[next] = e; }
                }
            }
            if (double.IsPositiveInfinity(distance[target])) { route = null; return false; }
            var reverse = new List<int>();
            for (int n = target; n != from;) { int e = previous[n]; if (e < 0) { route = null; return false; } reverse.Add(e); n = Resolve(profile.edges[e].fromNodeId); }
            reverse.Reverse(); route = reverse.ToArray(); return true;
        }

        // Motor: existing controllers own role/arrival; this bridge owns real navigation.
        public bool CanHoldForExchange(M4Session session)
        {
            if (session == null || !Available || !IdentityCurrent()) return false;
            var p = session.RuntimePartner.MovementProgress;
            if (p != null && !p.position.Equals(body.transform.position)) return false;
            try { M4PartnerMovementPersistence.Validate(session.RuntimePartner); }
            catch (FormatException) { return false; }
            return Guard(body.transform.position, body.transform.position, default(M4PartnerMuzzleLane), out _);
        }
        public bool CanResumeExchangeCover(M4Session session)
        {
            if (session == null || !Available || !IdentityCurrent()) return false;
            var f = session.RuntimePartner.ExchangeFlow;
            if (f == null || !f.suspended || f.priorRole != PartnerRole.CoverFace) return false;
            int target = Resolve(f.priorReservedNodeId ?? session.RuntimePartner.CurrentNodeId);
            if (target < 0 || !NodeAvailable(session, target) || Occupied(target)) return false;
            var station = geometry.Stations.Single(s => s.Id.ToString() == profile.nodes[target].stationDescriptorId);
            string face = station.Face == FaceId.FaceA ? "A" : station.Face == FaceId.FaceB ? "B" : station.Face == FaceId.FaceC ? "C" : null;
            if (face == null || face != f.priorFaceId) return false;
            var p = f.priorMovementProgress;
            if (p != null && (p.cancelled || p.profileIdentity != identity || p.runIdentity != session.Metadata.RunId.ToString() ||
                p.nightOrdinal != session.ActiveEntry.Key.NightOrdinal || !p.position.Equals(body.transform.position))) return false;
            return Guard(body.transform.position, body.transform.position, default(M4PartnerMuzzleLane), out _);
        }
        public bool CanRetainRequest(M4Session session, string targetNodeId)
        {
            if (session == null || !Available || !IdentityCurrent() || session.Stage != M4SessionStage.Defense ||
                session.RuntimeRun.Phase != RunPhase.Defense || session.RuntimeRun.Tower != TowerState.Stable) return false;
            var state = session.RuntimePartner;
            if (state == null || (state.Life != PartnerLifeState.Healthy && state.Life != PartnerLifeState.Injured)) return false;
            var p = state.MovementProgress;
            int target = Resolve(targetNodeId);
            if (p == null || p.cancelled || target < 0 || Resolve(p.targetNodeId) != target || !NodeAvailable(session, target) ||
                p.profileIdentity != identity || p.runIdentity != session.Metadata.RunId.ToString() ||
                p.nightOrdinal != session.ActiveEntry.Key.NightOrdinal || !p.position.Equals(body.transform.position)) return false;
            try { M4PartnerMovementPersistence.Validate(state); }
            catch (FormatException) { return false; }
            if (p.completed)
                return state.Movement == PartnerMovementState.Stationary && string.IsNullOrWhiteSpace(state.ReservedNodeId) &&
                    Resolve(state.CurrentNodeId) == target && body.transform.position.Equals(profile.nodes[target].bodyAnchor);
            return (state.Movement == PartnerMovementState.Moving || state.Movement == PartnerMovementState.Reserved) &&
                Resolve(state.ReservedNodeId) == target;
        }
        void FailBegin(PartnerRuntimeState state, int tick, string reason)
        {
            // A rejected replacement must not leave an active old target/new reservation mirror.
            M4PartnerMovementPersistence.Cancel(state, tick, reason);
            Receipt = "Partner movement UNAVAILABLE: " + reason;
        }
        void AddAuthoredPoint(int edgeIndex, int pointIndex, bool forward, List<Vector3> points,
            List<string> nodes, List<int> edges, List<SegmentReceipt> segments)
        {
            var edge = profile.edges[edgeIndex];
            Vector3 point = edge.bodyPoints[pointIndex];
            if (points[points.Count - 1].Equals(point)) return;
            points.Add(point); edges.Add(edgeIndex);
            nodes.Add(pointIndex == 0 ? profile.nodes[Resolve(edge.fromNodeId)].id :
                pointIndex == edge.bodyPoints.Length - 1 ? profile.nodes[Resolve(edge.toNodeId)].id : null);
            segments.Add(new SegmentReceipt { edge = edgeIndex, segment = forward ? pointIndex : pointIndex + 1, forward = forward });
        }
        bool AddRoute(M4Session session, int from, int target, List<Vector3> points,
            List<string> nodes, List<int> edges, List<SegmentReceipt> segments)
        {
            if (!Route(from, target, new HashSet<int>(), out int[] route, n => NodeAvailable(session, n))) return false;
            foreach (int edgeIndex in route)
                for (int i = 1; i < profile.edges[edgeIndex].bodyPoints.Length; i++)
                    AddAuthoredPoint(edgeIndex, i, true, points, nodes, edges, segments);
            return true;
        }
        static double PathLength(List<Vector3> points)
        {
            double length = 0d;
            for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
            return length;
        }
        bool CurrentSegment(M4PartnerMovementProgress old, out SegmentReceipt segment)
        {
            segment = null;
            if (old == null || old.planId != segmentPlanId || old.runIdentity != segmentRunIdentity || segmentPlan == null || segmentPlanPoints == null ||
                old.points.Length != segmentPlan.Length || old.points.Length != segmentPlanPoints.Length) return false;
            for (int i = 0; i < old.points.Length; i++)
                if (!old.points[i].Equals(segmentPlanPoints[i])) return false;
            int next = old.cursor;
            // A freshly accepted replacement can receive another Q before its first tick.
            while (next < old.points.Length && old.points[next].Equals(body.transform.position)) next++;
            if (next >= old.points.Length || segmentPlan[next] == null) return false;
            segment = segmentPlan[next];
            if (segment.connector != null) return old.edgeAtPoint[next] == -1;
            return segment.edge >= 0 && segment.edge < profile.edges.Length && segment.segment > 0 &&
                segment.segment < profile.edges[segment.edge].bodyPoints.Length && old.edgeAtPoint[next] == segment.edge;
        }
        static void AddRetainedPoint(Vector3 point, string node, int edge, SegmentReceipt segment,
            List<Vector3> points, List<string> nodes, List<int> edges, List<SegmentReceipt> segments)
        {
            if (points[points.Count - 1].Equals(point)) return;
            points.Add(point); nodes.Add(node); edges.Add(edge); segments.Add(segment);
        }
        bool EndpointCandidate(M4Session session, SegmentReceipt current, bool forward, int target,
            out List<Vector3> points, out List<string> nodes, out List<int> edges, out List<SegmentReceipt> segments)
        {
            points = new List<Vector3> { body.transform.position }; nodes = new List<string> { null };
            edges = new List<int> { -1 }; segments = new List<SegmentReceipt> { null };
            if (current.connector != null)
            {
                var c = current.connector;
                if (forward)
                {
                    if (!NodeAvailable(session, c.target)) return false;
                    AddRetainedPoint(profile.nodes[c.target].bodyAnchor, profile.nodes[c.target].id, -1,
                        new SegmentReceipt { edge = -1, forward = true, connector = c }, points, nodes, edges, segments);
                    return AddRoute(session, c.target, target, points, nodes, edges, segments);
                }
                AddRetainedPoint(c.start, null, -1, new SegmentReceipt { edge = -1, forward = false, connector = c },
                    points, nodes, edges, segments);
                for (int i = 1; i < c.backPoints.Length; i++)
                    AddRetainedPoint(c.backPoints[i], c.backNodes[i], c.backEdges[i], c.backSegments[i], points, nodes, edges, segments);
                int anchor = Resolve(c.backNodes[c.backNodes.Length - 1]);
                return anchor >= 0 && AddRoute(session, anchor, target, points, nodes, edges, segments);
            }
            var edge = profile.edges[current.edge];
            int endpoint = Resolve(forward ? edge.toNodeId : edge.fromNodeId);
            if (!NodeAvailable(session, endpoint)) return false;
            // Forward follows the original directed edge. Backward only retraces the part
            // already entered from its source anchor; it never invents a reverse graph edge.
            if (forward)
                for (int i = current.segment; i < edge.bodyPoints.Length; i++)
                    AddAuthoredPoint(current.edge, i, true, points, nodes, edges, segments);
            else
                for (int i = current.segment - 1; i >= 0; i--)
                    AddAuthoredPoint(current.edge, i, false, points, nodes, edges, segments);
            return AddRoute(session, endpoint, target, points, nodes, edges, segments);
        }
        void BindSegments(M4PartnerMovementProgress progress, List<SegmentReceipt> segments)
        {
            segmentPlanId = progress.planId;
            segmentRunIdentity = progress.runIdentity;
            segmentPlanPoints = (Vector3[])progress.points.Clone();
            segmentPlan = segments.ToArray();
            connectorFallbackActive = false;
        }
        bool ReciprocalDirect(int from, int to)
        {
            foreach (var edge in profile.edges)
            {
                if (Resolve(edge.fromNodeId) != from || Resolve(edge.toNodeId) != to) continue;
                if (ReciprocalEdge(edge)) return true;
            }
            return false;
        }
        bool ReciprocalEdge(M4PartnerMovementEdge edge)
        {
            foreach (var reverse in profile.edges)
            {
                if (Resolve(reverse.fromNodeId) != Resolve(edge.toNodeId) || Resolve(reverse.toNodeId) != Resolve(edge.fromNodeId) ||
                    reverse.bodyPoints.Length != edge.bodyPoints.Length) continue;
                bool same = true;
                for (int i = 0; i < edge.bodyPoints.Length; i++)
                    if (!edge.bodyPoints[i].Equals(reverse.bodyPoints[edge.bodyPoints.Length - 1 - i])) { same = false; break; }
                if (same) return true;
            }
            return false;
        }
        bool TryConnector(M4Session session, SegmentReceipt current, int last, int target, M4PartnerMuzzleLane lane,
            out ConnectorReceipt connector)
        {
            connector = null;
            if (current.connector == null && !ReciprocalEdge(profile.edges[current.edge])) return false;
            var witnesses = current.connector == null
                ? new[] { Resolve(profile.edges[current.edge].fromNodeId), Resolve(profile.edges[current.edge].toNodeId), target }.Distinct().ToArray()
                : current.connector.witnesses.Concat(new[] { target }).Distinct().ToArray();
            for (int i = 0; i < witnesses.Length; i++)
            {
                if (!NodeAvailable(session, witnesses[i])) return false;
                for (int j = i + 1; j < witnesses.Length; j++)
                    if (!ReciprocalDirect(witnesses[i], witnesses[j])) return false;
            }
            if (!NodeAvailable(session, target) || Occupied(target) ||
                !Guard(body.transform.position, body.transform.position, lane, out _) ||
                !Guard(body.transform.position, profile.nodes[target].bodyAnchor, lane, out _)) return false;
            // Keep only the actual return provenance; direct replanning does not visit it.
            if (current.connector == null ? Resolve(profile.edges[current.edge].fromNodeId) != last :
                Resolve(current.connector.backNodes[current.connector.backNodes.Length - 1]) != last) return false;
            if (!EndpointCandidate(session, current, false, last, out var bp, out var bn, out var be, out var bs)) return false;
            connector = new ConnectorReceipt { start = body.transform.position, target = target, witnesses = witnesses,
                backPoints = bp.ToArray(), backNodes = bn.ToArray(), backEdges = be.ToArray(), backSegments = bs.ToArray() };
            return true;
        }
        bool BuildWaypoints(M4Session session, M4PartnerMovementProgress old, int last, int target,
            out List<Vector3> points, out List<string> nodes, out List<int> edges,
            out List<SegmentReceipt> segments, out string reason, M4PartnerMuzzleLane lane = default(M4PartnerMuzzleLane),
            bool allowConnector = true, bool allowForward = true, bool allowBackward = true)
        {
            points = new List<Vector3> { body.transform.position }; nodes = new List<string> { null }; edges = new List<int> { -1 };
            segments = new List<SegmentReceipt> { null };
            reason = null;
            if (!body.transform.position.Equals(profile.nodes[last].bodyAnchor))
            {
                if (old == null || old.profileIdentity != identity || old.runIdentity != session.Metadata.RunId.ToString() ||
                    old.nightOrdinal != session.ActiveEntry.Key.NightOrdinal)
                { reason = "retained path context unavailable"; return false; }
                if (!CurrentSegment(old, out SegmentReceipt current))
                { reason = "actual authored segment provenance unavailable; prior request retained"; return false; }
                if (allowConnector && TryConnector(session, current, last, target, lane, out ConnectorReceipt connector))
                {
                    AddRetainedPoint(profile.nodes[target].bodyAnchor, profile.nodes[target].id, -1,
                        new SegmentReceipt { edge = -1, forward = true, connector = connector }, points, nodes, edges, segments);
                    return true;
                }
                bool forward = EndpointCandidate(session, current, true, target, out var fp, out var fn, out var fe, out var fs) && allowForward;
                bool backward = EndpointCandidate(session, current, false, target, out var bp, out var bn, out var be, out var bs) && allowBackward;
                double fc = forward ? PathLength(fp) : double.PositiveInfinity;
                double bc = backward ? PathLength(bp) : double.PositiveInfinity;
                forward &= !double.IsNaN(fc) && !double.IsInfinity(fc);
                backward &= !double.IsNaN(bc) && !double.IsInfinity(bc);
                if (!forward && !backward) { reason = "authored endpoint routes unavailable; prior request retained"; return false; }
                bool chooseForward = forward && (!backward || fc < bc || (fc == bc && current.forward));
                points = chooseForward ? fp : bp; nodes = chooseForward ? fn : bn;
                edges = chooseForward ? fe : be; segments = chooseForward ? fs : bs;
                return true;
            }
            if (!AddRoute(session, last, target, points, nodes, edges, segments))
            { reason = "authored route unavailable"; return false; }
            if (points.Count == 1) nodes[0] = profile.nodes[target].id;
            else nodes[0] = profile.nodes[last].id;
            return true;
        }
        public bool CanPrepareReplacement(M4Session session, string targetNodeId, out string reason,
            M4PartnerMuzzleLane lane = default(M4PartnerMuzzleLane))
        {
            reason = null;
            if (session == null) { reason = "current session unavailable"; return false; }
            var state = session.RuntimePartner; var old = state.MovementProgress;
            // Preserve the original no-authoring/no-active-plan role ACK-only contract.
            if (!Available || old == null || old.completed || old.cancelled) return true;
            int last = Resolve(state.CurrentNodeId), target = Resolve(targetNodeId);
            if (!IdentityCurrent() || session.Stage != M4SessionStage.Defense || session.ActiveEntry == null ||
                session.RuntimeRun.Phase != RunPhase.Defense || session.RuntimeRun.Tower != TowerState.Stable ||
                (state.Life != PartnerLifeState.Healthy && state.Life != PartnerLifeState.Injured) || last < 0 || target < 0 ||
                state.NextMovementPlanId == ulong.MaxValue || old.profileIdentity != identity ||
                old.runIdentity != session.Metadata.RunId.ToString() || old.nightOrdinal != session.ActiveEntry.Key.NightOrdinal ||
                !old.position.Equals(body.transform.position) || old.lastValidNodeId != profile.nodes[last].id)
            { reason = "active replacement context/pose/cursor unavailable; prior request retained"; return false; }
            try { M4PartnerMovementPersistence.Validate(state); }
            catch (FormatException error) { reason = "active replacement invalid: " + error.Message; return false; }
            return BuildWaypoints(session, old, last, target, out _, out _, out _, out _, out reason, lane);
        }
        public void Begin(M4Session session, int tick, M4PartnerMuzzleLane lane = default(M4PartnerMuzzleLane))
        {
            var state = session.RuntimePartner;
            var old = state.MovementProgress;
            if (session.Stage != M4SessionStage.Defense || session.RuntimeRun.Phase != RunPhase.Defense || session.RuntimeRun.Tower != TowerState.Stable ||
                (state.Life != PartnerLifeState.Healthy && state.Life != PartnerLifeState.Injured))
            { FailBegin(state, tick, "upper Defense/life/safety domain ended"); return; }
            if (!Available || !IdentityCurrent()) { FailBegin(state, tick, UnavailableReason ?? "profile/body identity changed"); return; }
            int last = Resolve(state.CurrentNodeId), target = Resolve(state.ReservedNodeId);
            if (last < 0 || target < 0) { FailBegin(state, tick, "explicit current/target alias missing; ACK preserved"); return; }
            if (old != null && !old.position.Equals(body.transform.position)) { FailBegin(state, tick, "actual pose/progress mismatch"); return; }
            if (old != null && !old.completed && !old.cancelled &&
                (old.profileIdentity != identity || old.runIdentity != session.Metadata.RunId.ToString() ||
                    old.nightOrdinal != session.ActiveEntry.Key.NightOrdinal))
            { FailBegin(state, tick, "active plan context mismatch"); return; }
            if (old != null && !old.completed && CanRetainRequest(session, state.ReservedNodeId))
            { Receipt = "Partner movement unchanged: active authored plan retained."; return; }
            if (state.NextMovementPlanId == ulong.MaxValue) { FailBegin(state, tick, "plan identity exhausted"); return; }
            if (!BuildWaypoints(session, old, last, target, out var points, out var nodes, out var edges, out var segments, out string reason, lane))
            {
                if (reason == "authored route unavailable") Recover(session, tick, new HashSet<int>(), reason, lane);
                else FailBegin(state, tick, reason ?? "retained route construction unavailable");
                return;
            }
            new PartnerController(state).RequestPath(this, profile.nodes[target].id, profile.safeFallbackOrder[0], tick);
            if (old != null && !old.completed && !old.cancelled) { old.cancelled = true; old.lastTick = tick; old.reason = "role/reservation replanned"; }
            state.MovementProgress = new M4PartnerMovementProgress { planId = state.NextMovementPlanId++, runIdentity = session.Metadata.RunId.ToString(),
                nightOrdinal = session.ActiveEntry.Key.NightOrdinal, profileIdentity = identity, targetNodeId = profile.nodes[target].id,
                lastValidNodeId = profile.nodes[last].id, position = body.transform.position, points = points.ToArray(), nodeAtPoint = nodes.ToArray(),
                edgeAtPoint = edges.ToArray(), blockedEdges = Array.Empty<int>(), laneBlockedEdges = Array.Empty<int>(),
                cursor = 0, startTick = tick, lastTick = tick, lastProgressTick = tick };
            BindSegments(state.MovementProgress, segments);
            state.CurrentNodeId = profile.nodes[last].id; state.Movement = PartnerMovementState.Moving; state.Decision = PartnerDecisionState.Moving;
            Receipt = "Moving to " + state.ReservedNodeId + " using guarded navigation; no cover fire.";
            Trace(session, tick, "BEGIN", Receipt, body.transform.position);
        }
        public void Tick(M4Session session, int tick, M4PartnerMuzzleLane lane)
        {
            if (session != null && session.RuntimePartner.ExchangeFlow != null && session.RuntimePartner.ExchangeFlow.suspended)
            {
                var heldState = session.RuntimePartner;
                if (session.Stage != M4SessionStage.Defense || session.RuntimeRun.Phase != RunPhase.Defense ||
                    session.RuntimeRun.Tower != TowerState.Stable || session.RuntimeRun.PlayerLife != PlayerLifeState.Alive || heldState.Life != PartnerLifeState.Healthy)
                {
                    new PartnerController(heldState).CancelExchange(tick, "canonical phase/life/safety ended while held");
                    heldState.ExchangeFlow.suspended = false; heldState.ExchangeFlow.returnReason = "safety ended; prior Cover not resumed";
                    M4PartnerMovementPersistence.Cancel(heldState, tick, heldState.ExchangeFlow.returnReason);
                    Receipt = heldState.ExchangeFlow.returnReason; return;
                }
                Receipt = "Partner movement held at actual pose for exchange; route/cursor retained."; return;
            }
            var state = session.RuntimePartner; var p = state.MovementProgress;
            if (p == null || p.completed || p.cancelled)
            {
                if (!string.IsNullOrWhiteSpace(state.ReservedNodeId) &&
                    (state.Movement == PartnerMovementState.Reserved || state.Movement == PartnerMovementState.Recovering)) Begin(session, tick, lane);
                return;
            }
            p.lastTick = tick;
            if (session.Stage != M4SessionStage.Defense || session.RuntimeRun.Phase != RunPhase.Defense ||
                session.RuntimeRun.Tower != TowerState.Stable || (state.Life != PartnerLifeState.Healthy && state.Life != PartnerLifeState.Injured))
            { M4PartnerMovementPersistence.Cancel(state, tick, "phase/life/safety domain ended"); Receipt = p.reason; return; }
            if (!Available || !IdentityCurrent() || p.profileIdentity != identity || p.runIdentity != session.Metadata.RunId.ToString() ||
                p.nightOrdinal != session.ActiveEntry.Key.NightOrdinal || !p.position.Equals(body.transform.position))
            { M4PartnerMovementPersistence.Cancel(state, tick, "authored identity/context/pose mismatch"); Receipt = "Partner movement UNAVAILABLE: " + p.reason; return; }
            if (state.ReservedNodeId != p.targetNodeId)
            { Begin(session, tick, lane); return; } // Existing safety/controller reservation gets its own actual route.
            int target = Resolve(p.targetNodeId);
            if (p.laneBlockedSequence != 0 && (!lane.available || lane.sequence != p.laneBlockedSequence))
            {
                var transient = new HashSet<int>(p.laneBlockedEdges);
                p.blockedEdges = p.blockedEdges.Where(e => !transient.Contains(e)).ToArray();
                p.laneBlockedEdges = Array.Empty<int>(); p.laneBlockedSequence = 0;
            }
            if (!NodeAvailable(session, target) || Occupied(target))
            { Recover(session, tick, new HashSet<int>(p.blockedEdges), "reserved target unavailable/occupied", lane); return; }
            float remaining = profile.metresPerSecond / 50f;
            if (!M4PartnerMovementPersistence.Finite(remaining) || remaining <= 0f)
            { M4PartnerMovementPersistence.Cancel(state, tick, "authored step unrepresentable"); Receipt = "Partner movement UNAVAILABLE: " + p.reason; return; }
            string held = null;
            while (p.cursor < p.points.Length)
            {
                Vector3 destination = p.points[p.cursor], prior = body.transform.position;
                if (p.nodeAtPoint[p.cursor] != null && !NodeAvailable(session, Resolve(p.nodeAtPoint[p.cursor])))
                { held = "authored route node no longer available"; break; }
                if (prior.Equals(destination))
                {
                    if (p.nodeAtPoint[p.cursor] != null) state.CurrentNodeId = p.lastValidNodeId = p.nodeAtPoint[p.cursor];
                    p.cursor++; continue;
                }
                Vector3 proposed = Vector3.MoveTowards(prior, destination, remaining);
                if (!Guard(prior, proposed, lane, out held)) break;
                if (proposed.Equals(prior)) { held = "actual movement step made no representable progress"; break; }
                body.transform.position = proposed; p.position = proposed;
                if (!proposed.Equals(prior)) p.lastProgressTick = tick;
                remaining = Mathf.Max(0f, remaining - Vector3.Distance(prior, proposed));
                if (!proposed.Equals(destination)) break;
                if (p.nodeAtPoint[p.cursor] != null) state.CurrentNodeId = p.lastValidNodeId = p.nodeAtPoint[p.cursor];
                p.cursor++;
                if (remaining <= 0f) break;
            }
            if (p.cursor == p.points.Length && body.transform.position.Equals(profile.nodes[target].bodyAnchor))
            {
                new PartnerController(state).ArriveAtReservedNode(); p.completed = true; p.position = body.transform.position;
                p.lastValidNodeId = state.CurrentNodeId; p.reason = "actual authored destination arrived";
                Receipt = "ARRIVED " + state.CurrentNodeId + " plan=" + p.planId + " once; cover fire not implemented.";
                Trace(session, tick, "ARRIVED", Receipt, body.transform.position); return;
            }
            if (held != null)
            {
                p.reason = held; Receipt = "Partner movement held: " + held;
                if (!connectorFallbackActive && CurrentSegment(p, out SegmentReceipt blocked) && blocked.connector != null &&
                    BuildWaypoints(session, p, Resolve(p.lastValidNodeId), target, out var fp, out var fn, out var fe, out var fs,
                        out _, lane, false, !blocked.forward, blocked.forward))
                {
                    // No role request/ACK/new plan and no reset of the actual no-progress clock.
                    p.points = fp.ToArray(); p.nodeAtPoint = fn.ToArray(); p.edgeAtPoint = fe.ToArray(); p.cursor = 0;
                    BindSegments(p, fs); connectorFallbackActive = true;
                    Receipt = "Connector held; retained traversed return path and authored fallback to " + p.targetNodeId;
                    Trace(session, tick, "CONNECTOR_FALLBACK", held, body.transform.position);
                }
            }
            if (tick - p.lastProgressTick >= 38)
            {
                var failed = new HashSet<int>(p.blockedEdges);
                bool laneHeld = held == "actual emitted FireSequence muzzle lane protected" || held == "actual emitted muzzle lane crossed by proposed movement";
                if (p.cursor < p.edgeAtPoint.Length && p.edgeAtPoint[p.cursor] >= 0)
                {
                    int e = p.edgeAtPoint[p.cursor]; failed.Add(e);
                    if (laneHeld && lane.sequence != 0) { p.laneBlockedEdges = p.laneBlockedEdges.Concat(new[] { e }).Distinct().ToArray(); p.laneBlockedSequence = lane.sequence; }
                }
                Recover(session, tick, failed, "0.75s no progress: " + (held ?? "no actual displacement"), lane);
            }
        }
        bool NodeAvailable(M4Session session, int node)
        {
            if (node < 0) return false;
            var station = geometry.Stations.Single(s => s.Id.ToString() == profile.nodes[node].stationDescriptorId);
            return station.IsAvailable && (!station.Face.HasValue || session.ReadTopology().ValidFaces.Contains(station.Face.Value));
        }
        bool Occupied(int node)
        {
            CapsuleAt(profile.nodes[node].bodyAnchor, out Vector3 a, out Vector3 b, out float radius);
            var names = profile.nodes[node].occupancyColliderNames;
            foreach (Collider c in Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore))
                if (c != body && names.Any(key => colliders.TryGetValue(key, out Collider expected) && c == expected)) return true;
            return false;
        }
        void Recover(M4Session session, int tick, HashSet<int> failed, string cause, M4PartnerMuzzleLane lane)
        {
            var state = session.RuntimePartner; var p = state.MovementProgress;
            int last = Resolve(p == null ? state.CurrentNodeId : p.lastValidNodeId), requested = Resolve(state.ReservedNodeId);
            Vector3 beforeRecovery = body.transform.position;
            Trace(session, tick, "RECOVERY_START", cause, beforeRecovery, failed.OrderBy(x => x).ToArray());
            if (p != null) p.blockedEdges = failed.OrderBy(x => x).ToArray();
            if (p == null && last >= 0 && requested >= 0 && state.NextMovementPlanId != ulong.MaxValue)
            {
                p = state.MovementProgress = new M4PartnerMovementProgress { planId = state.NextMovementPlanId++, runIdentity = session.Metadata.RunId.ToString(),
                    nightOrdinal = session.ActiveEntry.Key.NightOrdinal, profileIdentity = identity, targetNodeId = profile.nodes[requested].id,
                    lastValidNodeId = profile.nodes[last].id, position = body.transform.position, points = new[] { body.transform.position },
                    nodeAtPoint = new[] { state.CurrentNodeId }, edgeAtPoint = new[] { -1 }, blockedEdges = failed.OrderBy(x => x).ToArray(), laneBlockedEdges = Array.Empty<int>(),
                    cursor = 0, startTick = tick, lastTick = tick, lastProgressTick = tick, cancelled = true, reason = cause };
            }
            if (last >= 0 && NodeAvailable(session, last) && !Occupied(last) && Guard(profile.nodes[last].bodyAnchor, profile.nodes[last].bodyAnchor, lane, out _))
            {
                body.transform.position = profile.nodes[last].bodyAnchor; // DESIGN 12.6 technical recovery, not normal movement.
                state.CurrentNodeId = profile.nodes[last].id;
                if (p != null) { p.position = body.transform.position; p.cancelled = true; p.lastTick = tick; p.reason = cause; }
                Trace(session, tick, "RECOVERY_LAST_VALID_POSITION", cause, beforeRecovery, failed.OrderBy(x => x).ToArray());
                var candidates = new List<int>(); if (requested >= 0) candidates.Add(requested);
                if (requested >= 0) foreach (string alternate in profile.nodes[requested].alternativeNodeIds) candidates.Add(Resolve(alternate));
                foreach (int candidate in candidates)
                    if (NodeAvailable(session, candidate) && !Occupied(candidate) && Route(last, candidate, failed, out _, n => NodeAvailable(session, n)))
                    {
                        state.ReservedNodeId = profile.nodes[candidate].id;
                        BeginExcluding(session, tick, failed);
                        state.LastSignal = PartnerSignalKind.PathRecovered;
                        Receipt = "PathRecovered to last-valid node; authored alternate route: " + cause;
                        Trace(session, tick, "RECOVERY_ALTERNATE_ROUTE", Receipt, beforeRecovery, failed.OrderBy(x => x).ToArray()); return;
                    }
            }
            int safe = -1; double best = double.PositiveInfinity;
            foreach (string id in profile.safeFallbackOrder)
            {
                int candidate = Resolve(id);
                if (!NodeAvailable(session, candidate) || Occupied(candidate) || !Guard(profile.nodes[candidate].bodyAnchor, profile.nodes[candidate].bodyAnchor, lane, out _)) continue;
                double distance = (profile.nodes[candidate].bodyAnchor - body.transform.position).sqrMagnitude;
                if (distance < best) { safe = candidate; best = distance; }
            }
            if (safe < 0)
            { if (p != null) M4PartnerMovementPersistence.Cancel(state, tick, cause); Receipt = "Partner movement UNAVAILABLE: no guarded authored recovery anchor; " + cause; return; }
            body.transform.position = profile.nodes[safe].bodyAnchor;
            new PartnerController(state).RecoverPath(profile.nodes[safe].id);
            // Recovery is already at a validated anchor; it is not an ordinary arrival.
            state.CurrentNodeId = profile.nodes[safe].id; state.ReservedNodeId = null;
            state.Movement = PartnerMovementState.Stationary; state.Decision = PartnerDecisionState.Idle;
            if (p != null) { p.cancelled = true; p.lastTick = tick; p.position = body.transform.position; p.lastValidNodeId = state.CurrentNodeId; p.reason = cause; }
            Trace(session, tick, "RECOVERY_SAFE_POSITION", cause + "; safe=" + profile.nodes[safe].id, beforeRecovery, failed.OrderBy(x => x).ToArray());
            Receipt = "PathRecovered nearest authored safe anchor (error, no damage): " + cause;
        }
        void BeginExcluding(M4Session session, int tick, HashSet<int> failed)
        {
            var prior = session.RuntimePartner.MovementProgress;
            int[] laneEdges = prior == null ? Array.Empty<int>() : prior.laneBlockedEdges;
            ulong laneSequence = prior == null ? 0 : prior.laneBlockedSequence;
            // Make the selected authored route explicit, then reuse the ordinary begin seam.
            Begin(session, tick);
            var p = session.RuntimePartner.MovementProgress;
            if (p == null || p.cancelled || p.completed) return;
            int from = Resolve(p.lastValidNodeId), target = Resolve(p.targetNodeId);
            if (!Route(from, target, failed, out int[] route, n => NodeAvailable(session, n))) { M4PartnerMovementPersistence.Cancel(session.RuntimePartner, tick, "alternate route unavailable"); return; }
            var points = new List<Vector3> { body.transform.position }; var nodes = new List<string> { profile.nodes[from].id }; var edges = new List<int> { -1 };
            var segments = new List<SegmentReceipt> { null };
            foreach (int e in route)
                for (int i = 1; i < profile.edges[e].bodyPoints.Length; i++)
                    AddAuthoredPoint(e, i, true, points, nodes, edges, segments);
            p.points = points.ToArray(); p.nodeAtPoint = nodes.ToArray(); p.edgeAtPoint = edges.ToArray(); p.cursor = 0; p.blockedEdges = failed.OrderBy(x => x).ToArray();
            BindSegments(p, segments);
            p.laneBlockedEdges = laneEdges.Where(failed.Contains).ToArray(); p.laneBlockedSequence = p.laneBlockedEdges.Length == 0 ? 0 : laneSequence;
        }

        // Actual capsule/continuous collision/support and actual emitted muzzle-lane guard.
        void CapsuleAt(Vector3 position, out Vector3 a, out Vector3 b, out float radius)
        {
            Vector3 scale = body.transform.lossyScale; scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 localAxis = body.direction == 0 ? Vector3.right : body.direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = body.direction == 0 ? scale.x : body.direction == 1 ? scale.y : scale.z;
            float crossScale = body.direction == 0 ? Mathf.Max(scale.y, scale.z) : body.direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
            radius = body.radius * crossScale; float half = Mathf.Max(0f, body.height * axisScale * .5f - radius);
            Vector3 center = position + bodyRotation * Vector3.Scale(body.center, body.transform.lossyScale);
            Vector3 axis = bodyRotation * localAxis; a = center - axis * half; b = center + axis * half;
        }
        bool Guard(Vector3 from, Vector3 to, M4PartnerMuzzleLane lane, out string reason)
        {
            reason = null;
            if (!IdentityCurrent() || !M4PartnerMovementPersistence.Finite(from) || !M4PartnerMovementPersistence.Finite(to) ||
                !upperZone.Contains(from) || !upperZone.Contains(to)) { reason = "profile/body/UpperSafeZone pose unavailable"; return false; }
            CapsuleAt(to, out Vector3 a, out Vector3 b, out float radius);
            if (!M4PartnerMovementPersistence.Finite(a) || !M4PartnerMovementPersistence.Finite(b) || !M4PartnerMovementPersistence.Finite(radius) || radius <= 0f)
            { reason = "actual capsule geometry unavailable"; return false; }
            CapsuleAt(body.transform.position, out Vector3 actualA, out Vector3 actualB, out float actualRadius);
            bool priorInLane = lane.available && SegmentDistanceSquared(actualA, actualB, lane.origin, lane.end) <= (double)actualRadius * actualRadius;
            if (lane.available && !priorInLane && SegmentDistanceSquared(a, b, lane.origin, lane.end) <= (double)radius * radius)
            { reason = "actual emitted FireSequence muzzle lane protected"; return false; }
            foreach (Collider c in Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == body || QueryOnly(c) || presentation.Contains(c)) continue;
                if (!blockers.Contains(c) && !supports.Contains(c)) { reason = "unclassified solid collider role: " + c.name; return false; }
                if (Physics.ComputePenetration(body, to, bodyRotation, c, c.transform.position, c.transform.rotation, out _, out float depth) && depth > 0f)
                { reason = "actual capsule penetration: " + c.name + " depth=" + depth.ToString("R", CultureInfo.InvariantCulture) + " pose=" + Pose(to); return false; }
            }
            Vector3 delta = to - from;
            bool continuousSupport = !delta.Equals(Vector3.zero);
            bool supported = false, unsupportedConvexType = false;
            float footY = Mathf.Min(a.y, b.y) - radius; Vector3 center = (a + b) * .5f;
            CapsuleAt(from, out Vector3 supportFromA, out Vector3 supportFromB, out float supportFromRadius);
            float fromFootY = Mathf.Min(supportFromA.y, supportFromB.y) - supportFromRadius;
            Vector3 fromCenter = (supportFromA + supportFromB) * .5f;
            foreach (Collider support in supports)
            {
                if (support == null || !support.enabled || !support.gameObject.activeInHierarchy) { reason = "authored support unavailable"; return false; }
                if (continuousSupport && !KnownConvexSupport(support)) { unsupportedConvexType = true; continue; }
                if (SupportedPoint(support, center, footY) && (!continuousSupport || SupportedPoint(support, fromCenter, fromFootY))) supported = true;
            }
            if (!supported)
            {
                reason = !continuousSupport ? "authored support beneath actual capsule unavailable"
                    : unsupportedConvexType ? "continuous support UNAVAILABLE: no single validated convex support; nonconvex/unknown authored type present"
                    : "continuous support UNAVAILABLE: same convex collider does not support both endpoints";
                return false;
            }
            if (!delta.Equals(Vector3.zero))
            {
                CapsuleAt(from, out Vector3 startA, out Vector3 startB, out float startRadius);
                foreach (RaycastHit hit in Physics.CapsuleCastAll(startA, startB, startRadius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    Collider c = hit.collider; if (c == body || QueryOnly(c) || presentation.Contains(c)) continue;
                    if (supports.Contains(c))
                    {
                        // Fixed capsule translation above the collider's conservative AABB
                        // cannot enter its volume. Keep endpoint penetration rejection above.
                        if (SupportBelowSweep(c.bounds, fromFootY, footY)) continue;
                        // A floor touch parallel to the support normal is not entry into it.
                        // An entering sweep or unavailable normal cannot be ignored as floor.
                        if (!M4PartnerMovementPersistence.Finite(hit.normal) || hit.normal.Equals(Vector3.zero) || Vector3.Dot(delta, hit.normal) < 0f)
                        { reason = "authored support swept/normal unavailable: " + c.name + " | normal=" + Pose(hit.normal) +
                            " dot=" + Vector3.Dot(delta, hit.normal).ToString("R", CultureInfo.InvariantCulture) +
                            " fromFoot=" + fromFootY.ToString("R", CultureInfo.InvariantCulture) + " toFoot=" + footY.ToString("R", CultureInfo.InvariantCulture) +
                            " supportTop=" + c.bounds.max.y.ToString("R", CultureInfo.InvariantCulture); return false; }
                        continue;
                    }
                    if (!blockers.Contains(c)) { reason = "unclassified swept solid collider role: " + c.name; return false; }
                    if (hit.distance < delta.magnitude) { reason = "authored blocker swept: " + c.name; return false; }
                }
                if (lane.available && !priorInLane && SweptAxisDistanceSquared(startA, startB, a, b, lane.origin, lane.end) <= (double)radius * radius)
                { reason = "actual emitted muzzle lane crossed by proposed movement"; return false; }
            }
            return true;
        }
        // 서로 다른 발판의 양끝 지지만으로 중간 구간을 허용하지 않는다.
        static bool SupportBelowSweep(Bounds bounds, float fromFootY, float toFootY)
        {
            Vector3 extent = bounds.extents;
            return M4PartnerMovementPersistence.Finite(bounds.center) && M4PartnerMovementPersistence.Finite(extent) &&
                M4PartnerMovementPersistence.Finite(bounds.min) && M4PartnerMovementPersistence.Finite(bounds.max) &&
                extent.x >= 0f && extent.y >= 0f && extent.z >= 0f &&
                M4PartnerMovementPersistence.Finite(fromFootY) && M4PartnerMovementPersistence.Finite(toFootY) &&
                bounds.max.y <= Mathf.Min(fromFootY, toFootY);
        }
        // 같은 convex 실물 안의 두 ray hit를 보간하면 매 중간 XZ 아래에도
        // 발판 안의 점이 존재한다. 높이 돌출/몸체 침투는 기존 sweep가 별도로 막는다.
        static bool KnownConvexSupport(Collider support) => support is BoxCollider || support is SphereCollider ||
            support is CapsuleCollider || (support is MeshCollider mesh && mesh.convex);
        static bool SupportedPoint(Collider support, Vector3 center, float footY)
        {
            if (!M4PartnerMovementPersistence.Finite(center) || !M4PartnerMovementPersistence.Finite(footY)) return false;
            float length = center.y - support.bounds.min.y;
            if (!M4PartnerMovementPersistence.Finite(length) || length < 0f) return false;
            return support.Raycast(new Ray(center, Vector3.down), out RaycastHit hit, length) &&
                M4PartnerMovementPersistence.Finite(hit.point) && M4PartnerMovementPersistence.Finite(hit.distance) &&
                hit.distance >= 0f && hit.distance <= length && hit.point.y <= footY;
        }
        static bool QueryOnly(Collider c)
        { var tag = c.GetComponent<M2GrayboxColliderTag>(); return tag != null && tag.Kind == M2GrayboxColliderKind.AttackCapsule; }
        // Minimum squared distance between two finite closed segments, including points.
        static double SegmentDistanceSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
        {
            Vector3 u = q - p, v = b - a, w = p - a;
            double uu = Dot(u, u), vv = Dot(v, v), uv = Dot(u, v), uw = Dot(u, w), vw = Dot(v, w);
            double s = 0d, t = 0d;
            if (uu == 0d) t = vv == 0d ? 0d : Clamp(vw / vv);
            else if (vv == 0d) s = Clamp(-uw / uu);
            else
            {
                double denominator = uu * vv - uv * uv;
                s = denominator > 0d ? Clamp((uv * vw - uw * vv) / denominator) : 0d;
                t = (uv * s + vw) / vv;
                if (t < 0d) { t = 0d; s = Clamp(-uw / uu); }
                else if (t > 1d) { t = 1d; s = Clamp((uv - uw) / uu); }
            }
            double x = w.x + u.x * s - v.x * t, y = w.y + u.y * s - v.y * t, z = w.z + u.z * s - v.z * t;
            return x * x + y * y + z * z;
        }
        static double Dot(Vector3 a, Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        static double Clamp(double x) => Math.Max(0d, Math.Min(1d, x));

        // A translated capsule's axis sweeps a closed parallelogram. Its radius
        // expands that surface; checking the full surface protects head/body lanes.
        static double SweptAxisDistanceSquared(Vector3 startA, Vector3 startB, Vector3 endA, Vector3 endB, Vector3 rayA, Vector3 rayB)
        {
            double best = Math.Min(Math.Min(SegmentDistanceSquared(startA, startB, rayA, rayB), SegmentDistanceSquared(endA, endB, rayA, rayB)),
                Math.Min(SegmentDistanceSquared(startA, endA, rayA, rayB), SegmentDistanceSquared(startB, endB, rayA, rayB)));
            Vector3 axis = startB - startA, travel = endA - startA, normal = Vector3.Cross(axis, travel);
            double normalSquared = Dot(normal, normal);
            if (normalSquared == 0d) return best;
            Vector3 ray = rayB - rayA;
            double direction = Dot(normal, ray);
            if (direction != 0d)
            {
                double fraction = -Dot(normal, rayA - startA) / direction;
                if (fraction >= 0d && fraction <= 1d && InteriorProjection(rayA, ray, fraction, startA, axis, travel, normal, normalSquared, out _)) return 0d;
            }
            if (InteriorProjection(rayA, Vector3.zero, 0d, startA, axis, travel, normal, normalSquared, out double first)) best = Math.Min(best, first);
            if (InteriorProjection(rayB, Vector3.zero, 0d, startA, axis, travel, normal, normalSquared, out double last)) best = Math.Min(best, last);
            return best;
        }
        static bool InteriorProjection(Vector3 rayOrigin, Vector3 rayDirection, double fraction, Vector3 rectangleOrigin,
            Vector3 axis, Vector3 travel, Vector3 normal, double normalSquared, out double distanceSquared)
        {
            double x = (double)rayOrigin.x + rayDirection.x * fraction - rectangleOrigin.x;
            double y = (double)rayOrigin.y + rayDirection.y * fraction - rectangleOrigin.y;
            double z = (double)rayOrigin.z + rayDirection.z * fraction - rectangleOrigin.z;
            double plane = x * normal.x + y * normal.y + z * normal.z;
            distanceSquared = plane * plane / normalSquared;
            double aa = Dot(axis, axis), bb = Dot(travel, travel), ab = Dot(axis, travel), denominator = aa * bb - ab * ab;
            if (denominator <= 0d) return false;
            double u = x * axis.x + y * axis.y + z * axis.z, v = x * travel.x + y * travel.y + z * travel.z;
            double a = (u * bb - v * ab) / denominator, b = (v * aa - u * ab) / denominator;
            return a >= 0d && a <= 1d && b >= 0d && b <= 1d;
        }
    }

    public readonly struct M4PartnerMuzzleLane
    {
        public readonly bool available;
        public readonly ulong sequence;
        public readonly Vector3 origin, end;
        public M4PartnerMuzzleLane(Vector3 origin, Vector3 end, ulong sequence)
        { this.origin = origin; this.end = end; this.sequence = sequence;
            available = sequence != 0 && M4PartnerMovementPersistence.Finite(origin) && M4PartnerMovementPersistence.Finite(end); }
    }
}
