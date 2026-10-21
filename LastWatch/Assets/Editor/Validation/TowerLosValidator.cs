using System;
using System.Collections.Generic;
using System.Linq;
using LastWatch.Core.Space;
using UnityEngine;

namespace LastWatch.Editor.Validation
{
    public readonly struct LosProbeResult
    {
        public string FixtureId { get; }
        public StationId Station { get; }
        public string TargetId { get; }
        public int SampleIndex { get; }
        public Vector3 Origin { get; }
        public Vector3 Target { get; }
        public bool IsClear { get; }
        public string BlockingCollider { get; }

        public LosProbeResult(string fixtureId, StationId station, string targetId, int sampleIndex,
            Vector3 origin, Vector3 target, bool isClear, string blockingCollider)
        {
            FixtureId = fixtureId;
            Station = station;
            TargetId = targetId;
            SampleIndex = sampleIndex;
            Origin = origin;
            Target = target;
            IsClear = isClear;
            BlockingCollider = blockingCollider ?? string.Empty;
        }
    }

    public static class TowerLosValidator
    {
        private const float EndpointTolerance = 0.02f;

        public static LosProbeResult[] ValidateAttackPoint(
            string fixtureId,
            Transform grayboxRoot,
            StationDescriptor station,
            AttackCapsuleDescriptor attack)
        {
            if (grayboxRoot == null) throw new ArgumentNullException(nameof(grayboxRoot));
            Vector3[] samples = attack.GetLineOfSightSamples();
            var results = new LosProbeResult[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                results[index] = Probe(fixtureId, grayboxRoot, station, attack.Id.ToString(), index,
                    samples[index], attack.Id.ToString());
            }
            return results;
        }

        public static LosProbeResult ValidateRouteSample(
            string fixtureId,
            Transform grayboxRoot,
            StationDescriptor station,
            RouteDescriptor route,
            int sampleIndex,
            Vector3 sample)
        {
            if (grayboxRoot == null) throw new ArgumentNullException(nameof(grayboxRoot));
            return Probe(fixtureId, grayboxRoot, station, route.Id.ToString(), sampleIndex, sample, null);
        }

        private static LosProbeResult Probe(string fixtureId, Transform grayboxRoot, StationDescriptor station,
            string targetId, int sampleIndex, Vector3 target, string allowedEndpointCollider)
        {
            Vector3 delta = target - station.EyePosition;
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon)
                return new LosProbeResult(fixtureId, station.Id, targetId, sampleIndex,
                    station.EyePosition, target, true, string.Empty);

            Physics.SyncTransforms();
            RaycastHit[] hits = Physics.RaycastAll(station.EyePosition, delta / distance, distance + EndpointTolerance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits.OrderBy(x => x.distance))
            {
                if (hit.collider == null || !hit.collider.transform.IsChildOf(grayboxRoot)) continue;
                if (allowedEndpointCollider != null && hit.collider.gameObject.name == allowedEndpointCollider) continue;
                M2GrayboxColliderTag tag = hit.collider.GetComponent<M2GrayboxColliderTag>();
                if (tag == null || tag.Kind == M2GrayboxColliderKind.AttackCapsule) continue;
                return new LosProbeResult(fixtureId, station.Id, targetId, sampleIndex,
                    station.EyePosition, target, false, hit.collider.gameObject.name);
            }

            return new LosProbeResult(fixtureId, station.Id, targetId, sampleIndex,
                station.EyePosition, target, true, string.Empty);
        }
    }
}
