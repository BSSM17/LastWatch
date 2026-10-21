using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastWatch.Core.Space
{
    public enum PathSmokeCompletionStatus
    {
        Completed,
        TechnicalPathOrColliderFailure
    }

    public readonly struct PathSmokeRouteObservation
    {
        public RouteId RouteId { get; }
        public int TotalSamples { get; }
        public int CompletedSamples { get; }
        public float Progress { get; }
        public PathSmokeCompletionStatus CompletionStatus { get; }
        public int TechnicalPathOrColliderFailureCount { get; }
        public int FallCount { get; }
        public int DeathCount { get; }

        internal PathSmokeRouteObservation(RouteId routeId, int totalSamples, int completedSamples,
            PathSmokeCompletionStatus completionStatus, int technicalFailureCount, int fallCount, int deathCount)
        {
            RouteId = routeId;
            TotalSamples = totalSamples;
            CompletedSamples = completedSamples;
            Progress = totalSamples == 0 ? 0f : (float)completedSamples / totalSamples;
            CompletionStatus = completionStatus;
            TechnicalPathOrColliderFailureCount = technicalFailureCount;
            FallCount = fallCount;
            DeathCount = deathCount;
        }
    }

    public sealed class M2PathSmokeSnapshot
    {
        private readonly PathSmokeRouteObservation[] routes;

        public IReadOnlyList<PathSmokeRouteObservation> Routes => Array.AsReadOnly(routes);
        public int TechnicalPathOrColliderFailureCount { get; }
        public int FallCount { get; }
        public int DeathCount { get; }
        public bool AllAvailableRoutesCompleted { get; }

        internal M2PathSmokeSnapshot(PathSmokeRouteObservation[] routeObservations)
        {
            routes = (PathSmokeRouteObservation[])routeObservations.Clone();
            bool allCompleted = true;
            foreach (PathSmokeRouteObservation route in routes)
            {
                TechnicalPathOrColliderFailureCount += route.TechnicalPathOrColliderFailureCount;
                FallCount += route.FallCount;
                DeathCount += route.DeathCount;
                allCompleted &= route.CompletionStatus == PathSmokeCompletionStatus.Completed;
            }
            AllAvailableRoutesCompleted = allCompleted;
        }
    }

    public static class M2PathSmokeProbe
    {
        private const float GroundProbeHeight = 1f;
        private const float GroundProbeDistance = 2.25f;
        private const float SupportProbeRadius = 1f;

        public static M2PathSmokeSnapshot Execute(TowerGeometryFixture fixture)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));

            var grayboxRoot = new GameObject("M2PathSmoke_Graybox");
            var actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actor.name = "M2PathSmoke_FixtureActor";
            Collider actorCollider = actor.GetComponent<Collider>();
            if (actorCollider != null) actorCollider.enabled = false;

            try
            {
                grayboxRoot.AddComponent<M2GrayboxBuilder>().Build(fixture);
                Physics.SyncTransforms();
                var observations = new List<PathSmokeRouteObservation>();
                foreach (RouteDescriptor route in fixture.Routes)
                {
                    if (!route.IsAvailable) continue;
                    observations.Add(ExecuteRoute(route, actor));
                }
                return new M2PathSmokeSnapshot(observations.ToArray());
            }
            finally
            {
                DestroyFixtureObject(actor);
                DestroyFixtureObject(grayboxRoot);
            }
        }

        private static PathSmokeRouteObservation ExecuteRoute(RouteDescriptor route, GameObject actor)
        {
            Vector3[] samples = route.Sample(TowerGeometryFixture.LineOfSightSampleSpacing);
            int completed = 0;
            int technicalFailures = 0;
            int falls = 0;
            int deaths = 0;

            foreach (Vector3 sample in samples)
            {
                actor.transform.position = sample;
                Physics.SyncTransforms();

                bool finite = IsFinite(sample);
                bool supported = finite && IsSupported(route, sample);
                if (!finite || !supported)
                {
                    technicalFailures++;
                    falls++;
                    deaths++;
                    break;
                }
                completed++;
            }

            PathSmokeCompletionStatus status = completed == samples.Length
                ? PathSmokeCompletionStatus.Completed
                : PathSmokeCompletionStatus.TechnicalPathOrColliderFailure;
            return new PathSmokeRouteObservation(route.Id, samples.Length, completed, status,
                technicalFailures, falls, deaths);
        }

        private static bool IsSupported(RouteDescriptor route, Vector3 sample)
        {
            if (route.Kind == RouteKind.ClimberRail)
            {
                Collider[] nearby = Physics.OverlapSphere(sample, SupportProbeRadius);
                foreach (Collider collider in nearby)
                {
                    M2GrayboxColliderTag tag = collider.GetComponent<M2GrayboxColliderTag>();
                    if (tag != null && tag.Kind == M2GrayboxColliderKind.SupportLeg && tag.Face == route.Face)
                        return true;
                }
                return false;
            }

            Vector3 origin = sample + Vector3.up * GroundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, GroundProbeDistance);
            foreach (RaycastHit hit in hits)
            {
                M2GrayboxColliderTag groundTag = hit.collider.GetComponent<M2GrayboxColliderTag>();
                if (groundTag != null && groundTag.Kind == M2GrayboxColliderKind.GroundSurface) return true;
            }
            return false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void DestroyFixtureObject(GameObject value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
