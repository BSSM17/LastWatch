using System;
using System.Linq;
using LastWatch.Core.Space;
using LastWatch.Editor.Validation;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Tests
{
    [TestFixture]
    public sealed class M2SpatialValidationTests
    {
        private GameObject root;
        private M2GrayboxBuilder builder;

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void NormalFixture_AllAttackPointsHaveAtLeastTwoPhysicsValidatedLinesOfSight()
        {
            TowerGeometryFixture fixture = Build(TowerGeometryFixture.CreateNormal());
            foreach (AttackCapsuleDescriptor attack in fixture.AttackPoints.Where(x => x.IsAvailable))
            {
                int clearStations = fixture.Stations.Where(x => x.IsAvailable)
                    .Where(station => TowerLineOfSightContract.IsCanonicalPair(station.Id, attack.Id))
                    .Count(station => TowerLosValidator.ValidateAttackPoint(
                        "Normal", root.transform, station, attack).Any(x => x.IsClear));
                Assert.That(clearStations, Is.GreaterThanOrEqualTo(2),
                    $"{attack.Id} had {clearStations} Physics-validated station lines.");
            }
        }

        [TestCase(FaceId.FaceA)]
        [TestCase(FaceId.FaceB)]
        [TestCase(FaceId.FaceC)]
        public void SingleFaceUnavailable_RemainingAttackPointsHavePhysicsValidatedFallbackLine(FaceId unavailableFace)
        {
            TowerGeometryFixture fixture = Build(TowerGeometryFixture.CreateOneFaceUnavailable(unavailableFace));
            foreach (AttackCapsuleDescriptor attack in fixture.AttackPoints.Where(x => x.IsAvailable))
            {
                bool hasClearLine = fixture.Stations.Where(x => x.IsAvailable)
                    .Where(station => TowerLineOfSightContract.IsCanonicalPair(station.Id, attack.Id))
                    .Any(station => TowerLosValidator.ValidateAttackPoint(
                        unavailableFace.ToString(), root.transform, station, attack).Any(x => x.IsClear));
                Assert.That(hasClearLine, Is.True, $"{unavailableFace}/{attack.Id} had no clear fallback line.");
            }
        }

        [Test]
        public void AllRoutes_HaveValidUpperLineOfSightAtEveryHalfMeterSample()
        {
            TowerGeometryFixture fixture = Build(TowerGeometryFixture.CreateNormal());
            StationDescriptor[] stations = fixture.Stations.Where(x => x.IsAvailable).ToArray();
            foreach (RouteDescriptor route in fixture.Routes.Where(x => x.IsAvailable))
            {
                Vector3[] samples = route.Sample(TowerGeometryFixture.LineOfSightSampleSpacing);
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    int index = sampleIndex;
                    bool hasClearLine = stations.Any(station => TowerLosValidator.ValidateRouteSample(
                        "Normal", root.transform, station, route, index, samples[index]).IsClear);
                    Assert.That(hasClearLine, Is.True,
                        $"{route.Id} sample {sampleIndex}/{samples.Length - 1} had zero upper LOS at {samples[sampleIndex]}.");
                }
            }
        }

        [Test]
        public void GrayboxFixture_CreatesFiniteCoreOwnedCollidersAndPaths()
        {
            TowerGeometryFixture fixture = Build(TowerGeometryFixture.CreateNormal());
            Collider[] colliders = root.GetComponentsInChildren<Collider>();

            Assert.That(colliders.Length, Is.GreaterThanOrEqualTo(14));
            Assert.That(fixture.Routes.All(route => route.Sample(0.5f).All(IsFinite)), Is.True);
            Assert.That(fixture.AttackPoints.All(attack => IsFinite(attack.Bottom) && IsFinite(attack.Top)), Is.True);
            Assert.That(colliders.All(collider => IsFinite(collider.bounds.center) && IsFinite(collider.bounds.size)), Is.True);
        }

        [TestCase(null)]
        [TestCase(FaceId.FaceA)]
        [TestCase(FaceId.FaceB)]
        [TestCase(FaceId.FaceC)]
        public void PathSmoke_AllAvailableRoutesCompleteWithoutTechnicalFallOrDeath(FaceId? unavailableFace)
        {
            TowerGeometryFixture fixture = unavailableFace.HasValue
                ? TowerGeometryFixture.CreateOneFaceUnavailable(unavailableFace.Value)
                : TowerGeometryFixture.CreateNormal();

            M2PathSmokeSnapshot snapshot = M2PathSmokeProbe.Execute(fixture);

            Assert.That(snapshot.Routes.Count, Is.EqualTo(unavailableFace.HasValue ? 6 : 9));
            Assert.That(snapshot.AllAvailableRoutesCompleted, Is.True);
            Assert.That(snapshot.TechnicalPathOrColliderFailureCount, Is.Zero);
            Assert.That(snapshot.FallCount, Is.Zero);
            Assert.That(snapshot.DeathCount, Is.Zero);
            Assert.That(snapshot.Routes.All(route =>
                route.CompletionStatus == PathSmokeCompletionStatus.Completed &&
                route.CompletedSamples == route.TotalSamples &&
                route.Progress == 1f), Is.True);
        }

        private TowerGeometryFixture Build(TowerGeometryFixture fixture)
        {
            root = new GameObject("M2ValidationGraybox");
            builder = root.AddComponent<M2GrayboxBuilder>();
            builder.Build(fixture);
            Physics.SyncTransforms();
            return fixture;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
