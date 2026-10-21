using System;
using System.Collections.Generic;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;
using LastWatch.Core.Safety;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using LastWatch.Core.Traversal;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Tests
{
    [TestFixture]
    public sealed class M2CoreContractTests
    {
        [Test]
        public void NormalFixture_AllAttackPointsHaveAtLeastTwoCanonicalStations()
        {
            TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
            StationId[] availableStations = fixture.Stations.Where(x => x.IsAvailable).Select(x => x.Id).ToArray();
            AttackCapsuleDescriptor[] attackPoints = fixture.AttackPoints.Where(x => x.IsAvailable).ToArray();

            Assert.That(attackPoints, Has.Length.EqualTo(6));
            foreach (AttackCapsuleDescriptor attackPoint in attackPoints)
            {
                int validCount = TowerLineOfSightContract.GetCanonicalStations(attackPoint.Id)
                    .Count(availableStations.Contains);
                Assert.That(validCount, Is.GreaterThanOrEqualTo(2),
                    $"{attackPoint.Id} has only {validCount} canonical station lines.");
                Assert.That(attackPoint.GetLineOfSightSamples(), Has.Length.EqualTo(3));
            }
        }

        [TestCase(FaceId.FaceA)]
        [TestCase(FaceId.FaceB)]
        [TestCase(FaceId.FaceC)]
        public void SingleFaceUnavailable_RemainingAttackPointsHaveCanonicalFallbackLine(FaceId unavailableFace)
        {
            TowerGeometryFixture fixture = TowerGeometryFixture.CreateOneFaceUnavailable(unavailableFace);
            StationId[] availableStations = fixture.Stations.Where(x => x.IsAvailable).Select(x => x.Id).ToArray();
            AttackCapsuleDescriptor[] remaining = fixture.AttackPoints.Where(x => x.IsAvailable).ToArray();

            Assert.That(remaining, Has.Length.EqualTo(4));
            Assert.That(fixture.AttackPoints.Count(x => !x.IsAvailable), Is.EqualTo(2));
            Assert.That(fixture.Stations.Count(x => !x.IsAvailable), Is.EqualTo(1));
            foreach (AttackCapsuleDescriptor attackPoint in remaining)
            {
                StationId[] valid = TowerLineOfSightContract.GetCanonicalStations(attackPoint.Id)
                    .Where(availableStations.Contains).ToArray();
                Assert.That(valid, Is.Not.Empty, $"{attackPoint.Id} has no remaining canonical station line.");
                Assert.That(valid.Contains(StationId.S_0) || valid.Any(x => x != StationId.S_0), Is.True);
            }
        }

        [Test]
        public void Routes_SampleEveryAvailablePathAtNoMoreThanHalfMeterSpacing()
        {
            TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
            RouteDescriptor[] routes = fixture.Routes.Where(x => x.IsAvailable).ToArray();

            Assert.That(routes, Has.Length.EqualTo(9));
            Assert.That(routes.Count(x => x.Kind == RouteKind.SpawnLane), Is.EqualTo(3));
            Assert.That(routes.Count(x => x.Kind == RouteKind.ApproachSpline), Is.EqualTo(3));
            Assert.That(routes.Count(x => x.Kind == RouteKind.ClimberRail), Is.EqualTo(3));
            foreach (RouteDescriptor route in routes)
            {
                Vector3[] samples = route.Sample(TowerGeometryFixture.LineOfSightSampleSpacing);
                Assert.That(samples.Length, Is.GreaterThan(1));
                for (int i = 1; i < samples.Length; i++)
                {
                    float distance = Vector3.Distance(samples[i - 1], samples[i]);
                    Assert.That(distance, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.50001f),
                        $"{route.Id} sample {i} spacing was {distance}m.");
                }
            }
        }

        [Test]
        public void AttackCapsuleStandOff_GroundApproachesStopAtMeasuredPositiveGap()
        {
            var grayboxRoot = new GameObject("M2_AttackCapsuleStandOff_Graybox");
            GameObject actorObject = null;
            try
            {
                TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
                M2GrayboxBuilder builder = grayboxRoot.AddComponent<M2GrayboxBuilder>();
                builder.Build(fixture);
                Physics.SyncTransforms();

                var supportLegs = new List<Collider>();
                var attackCapsules = new List<Collider>();
                var groundAttackByFace = new Dictionary<FaceId, Collider>();
                var climbAttackByFace = new Dictionary<FaceId, Collider>();
                Collider groundSurface = null;
                foreach (M2GrayboxColliderTag tag in builder.GetComponentsInChildren<M2GrayboxColliderTag>(true))
                {
                    Collider collider = tag.GetComponent<Collider>();
                    if (collider == null) continue;
                    if (tag.Kind == M2GrayboxColliderKind.GroundSurface) groundSurface = collider;
                    if (tag.Kind == M2GrayboxColliderKind.SupportLeg)
                    {
                        supportLegs.Add(collider);
                    }
                    else if (tag.Kind == M2GrayboxColliderKind.AttackCapsule)
                    {
                        attackCapsules.Add(collider);
                        if (collider.bounds.center.y < 10f) groundAttackByFace.Add(tag.Face.Value, collider);
                        else climbAttackByFace.Add(tag.Face.Value, collider);
                    }
                }

                Assert.That(attackCapsules, Has.Count.EqualTo(6));
                Assert.That(groundSurface, Is.Not.Null);
                actorObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                CapsuleCollider actorCollider = actorObject.GetComponent<CapsuleCollider>();
                Assert.That(actorCollider, Is.Not.Null);

                foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
                {
                    RouteDescriptor lane = fixture.Routes.Single(route =>
                        route.Face == face && route.Kind == RouteKind.SpawnLane);
                    RouteDescriptor approach = fixture.Routes.Single(route =>
                        route.Face == face && route.Kind == RouteKind.ApproachSpline);
                    var groundWaypoints = lane.Sample(TowerGeometryFixture.LineOfSightSampleSpacing)
                        .Select(point => point + Vector3.up * 0.5f)
                        .Concat(approach.Sample(TowerGeometryFixture.LineOfSightSampleSpacing)
                            .Skip(1).Select(point => point + Vector3.up * 0.5f)).ToArray();
                    Collider targetCapsule = groundAttackByFace[face];

                    foreach (EnemyArchetype archetype in EnemyArchetypeCatalog.All.Select(x => x.Archetype))
                    {
                        actorObject.transform.localScale = archetype == EnemyArchetype.Breaker
                            ? new Vector3(1.4f, 1.5f, 1.4f)
                            : Vector3.one;
                        float groundRouteLift = archetype == EnemyArchetype.Breaker
                            ? ActiveEnemyActor.GroundRouteLift(actorCollider)
                            : 0f;
                        Assert.That(groundRouteLift, Is.EqualTo(archetype == EnemyArchetype.Breaker ? 0.5f : 0f));
                        Physics.SyncTransforms();
                        Vector3 position = groundWaypoints[0] + Vector3.up * groundRouteLift;
                        Quaternion rotation = Quaternion.identity;
                        Assert.That(Overlaps(actorCollider, position, rotation, groundSurface), Is.False,
                            $"{archetype} penetrated GroundSurface at spawn on {face}.");
                        float step = EnemyArchetypeCatalog.Get(archetype).GroundSpeedMetersPerSecond /
                                     EnemyDirector.TickRate;
                        bool reachedStandOff = false;
                        int nonTargetOverlapObservations = 0;

                        for (int waypointIndex = 1; waypointIndex < groundWaypoints.Length && !reachedStandOff;
                             waypointIndex++)
                        {
                            Vector3 waypoint = groundWaypoints[waypointIndex] + Vector3.up * groundRouteLift;
                            int tickLimit = Mathf.CeilToInt(Vector3.Distance(position, waypoint) / step) + 2;
                            for (int tick = 0; tick < tickLimit && Vector3.Distance(position, waypoint) > 0.001f;
                                 tick++)
                            {
                                Vector3 proposed = Vector3.MoveTowards(position, waypoint, step);
                                Vector3 forward = waypoint - proposed;
                                Quaternion proposedRotation = forward.sqrMagnitude > 0.001f
                                    ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                                    : rotation;
                                Assert.That(M2SupportLegCollisionGuard.TryResolve(actorCollider, supportLegs,
                                        proposed, proposedRotation, out Vector3 resolved), Is.True,
                                    $"Could not resolve {archetype} on ground route {face}, waypoint {waypointIndex}, tick {tick}.");
                                Assert.That(HasPositiveSupportPenetration(actorCollider, supportLegs,
                                        resolved, proposedRotation), Is.False,
                                    $"{archetype} penetrated a SupportLeg on ground route {face}, waypoint {waypointIndex}, tick {tick}.");
                                Assert.That(Overlaps(actorCollider, resolved, proposedRotation, groundSurface), Is.False,
                                    $"{archetype} penetrated GroundSurface on ground route {face}, waypoint {waypointIndex}, tick {tick}.");

                                M2AttackCapsulePoseResult result = M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(
                                    actorCollider, targetCapsule, resolved, proposedRotation, out _);
                                int allCapsuleOverlapCount = M2AttackCapsuleStandOffGuard.CountPositivePenetrations(
                                    actorCollider, attackCapsules, resolved, proposedRotation, out _);
                                int nonTargetOverlapCount = allCapsuleOverlapCount -
                                    (result == M2AttackCapsulePoseResult.TargetCapsuleReached ? 1 : 0);
                                nonTargetOverlapObservations += nonTargetOverlapCount;
                                if (result == M2AttackCapsulePoseResult.TargetCapsuleReached)
                                {
                                    Assert.That(M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(actorCollider,
                                            position, rotation, targetCapsule as CapsuleCollider, out float surfaceGap), Is.True,
                                        $"Could not measure the {archetype}/{face} terminal capsule surface gap.");
                                    Assert.That(surfaceGap, Is.GreaterThan(0f),
                                        $"{archetype}/{face} did not retain positive terminal stand-off.");
                                    Assert.That(M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(actorCollider,
                                            targetCapsule, position, rotation, out _),
                                        Is.EqualTo(M2AttackCapsulePoseResult.Clear));
                                    int terminalAllOverlapCount = M2AttackCapsuleStandOffGuard.CountPositivePenetrations(
                                        actorCollider, attackCapsules, position, rotation, out _);
                                    TestContext.WriteLine($"ground/{archetype}/{face}/{targetCapsule.name}: " +
                                                          $"gap={surfaceGap:R} m, non-target overlap observations={nonTargetOverlapObservations}, " +
                                                          $"terminal non-target pair count={terminalAllOverlapCount}");
                                    reachedStandOff = true;
                                    break;
                                }

                                position = resolved;
                                rotation = proposedRotation;
                            }
                        }

                        Assert.That(reachedStandOff, Is.True,
                            $"{archetype} could not reach a clear stand-off pose for {face} on its authored ground route.");
                    }
                }
            }
            finally
            {
                if (actorObject != null) UnityEngine.Object.DestroyImmediate(actorObject);
                if (grayboxRoot != null) UnityEngine.Object.DestroyImmediate(grayboxRoot);
            }
        }

        [Test]
        public void ClimberRail_AssignedAPCStandsOffWhileNonTargetAPGCrossingRemainsInformational()
        {
            var grayboxRoot = new GameObject("M2_ClimberAttackCapsuleStandOff_Graybox");
            GameObject actorObject = null;
            try
            {
                TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
                M2GrayboxBuilder builder = grayboxRoot.AddComponent<M2GrayboxBuilder>();
                builder.Build(fixture);
                Physics.SyncTransforms();

                var supportLegs = new List<Collider>();
                var attackCapsules = new List<Collider>();
                var groundAttackByFace = new Dictionary<FaceId, Collider>();
                var climbAttackByFace = new Dictionary<FaceId, Collider>();
                foreach (M2GrayboxColliderTag tag in builder.GetComponentsInChildren<M2GrayboxColliderTag>(true))
                {
                    Collider collider = tag.GetComponent<Collider>();
                    if (collider == null) continue;
                    if (tag.Kind == M2GrayboxColliderKind.SupportLeg) supportLegs.Add(collider);
                    if (tag.Kind != M2GrayboxColliderKind.AttackCapsule) continue;
                    attackCapsules.Add(collider);
                    if (collider.bounds.center.y < 10f) groundAttackByFace.Add(tag.Face.Value, collider);
                    else climbAttackByFace.Add(tag.Face.Value, collider);
                }

                actorObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                CapsuleCollider actorCollider = actorObject.GetComponent<CapsuleCollider>();
                Assert.That(actorCollider, Is.Not.Null);
                Assert.That(attackCapsules, Has.Count.EqualTo(6));

                foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
                {
                    RouteDescriptor rail = fixture.Routes.Single(route =>
                        route.Face == face && route.Kind == RouteKind.ClimberRail);
                    RouteDescriptor lane = fixture.Routes.Single(route =>
                        route.Face == face && route.Kind == RouteKind.SpawnLane);
                    RouteDescriptor approach = fixture.Routes.Single(route =>
                        route.Face == face && route.Kind == RouteKind.ApproachSpline);
                    Vector3[] waypoints = lane.Sample(TowerGeometryFixture.LineOfSightSampleSpacing)
                        .Select(point => point + Vector3.up * 0.5f)
                        .Concat(approach.Sample(TowerGeometryFixture.LineOfSightSampleSpacing)
                            .Skip(1).Select(point => point + Vector3.up * 0.5f))
                        .Concat(rail.Sample(TowerGeometryFixture.LineOfSightSampleSpacing)
                            .Skip(1).Select(point => point + Vector3.up * 0.5f)).ToArray();
                    Collider targetCapsule = climbAttackByFace[face];
                    actorObject.transform.localScale = Vector3.one;
                    Physics.SyncTransforms();
                    Vector3 authoredRailOrigin = rail.ControlPoints[0] + Vector3.up * 0.5f;
                    Assert.That(Overlaps(actorCollider, authoredRailOrigin, Quaternion.identity,
                            groundAttackByFace[face]), Is.True,
                        $"Expected the ClimberRail origin {rail.Id} to cross its non-target AP_G.");
                    Assert.That(M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(actorCollider,
                            groundAttackByFace[face], authoredRailOrigin, Quaternion.identity, out float originDepth),
                        Is.EqualTo(M2AttackCapsulePoseResult.TargetCapsuleReached));
                    Assert.That(originDepth, Is.GreaterThan(0f));
                    Assert.That(M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(actorCollider,
                            targetCapsule, authoredRailOrigin, Quaternion.identity, out _),
                        Is.EqualTo(M2AttackCapsulePoseResult.Clear));
                    int authoredOriginCapsuleOverlapCount = M2AttackCapsuleStandOffGuard.CountPositivePenetrations(
                        actorCollider, attackCapsules, authoredRailOrigin, Quaternion.identity,
                        out Collider authoredOriginFirstOverlap);
                    Assert.That(authoredOriginCapsuleOverlapCount, Is.EqualTo(1));
                    Assert.That(authoredOriginFirstOverlap, Is.SameAs(groundAttackByFace[face]));
                    TestContext.WriteLine($"climber/{rail.Id}/authored-origin: non-target pair " +
                                          $"{groundAttackByFace[face].name} depth={originDepth:R} m; permitted for AP_C assignment.");
                    Vector3 position = waypoints[0];
                    Quaternion rotation = Quaternion.identity;
                    int climbWaypointStartIndex = lane.Sample(TowerGeometryFixture.LineOfSightSampleSpacing).Length +
                                                  approach.Sample(TowerGeometryFixture.LineOfSightSampleSpacing).Length - 1;
                    int nonTargetOverlapObservations = authoredOriginCapsuleOverlapCount;
                    bool reachedClimbStandOff = false;
                    EnemyArchetypeConfig config = EnemyArchetypeCatalog.Get(EnemyArchetype.Climber);

                    for (int waypointIndex = 1; waypointIndex < waypoints.Length && !reachedClimbStandOff;
                         waypointIndex++)
                    {
                        Vector3 waypoint = waypoints[waypointIndex];
                        float speed = waypointIndex >= climbWaypointStartIndex
                            ? config.ClimbSpeedMetersPerSecond
                            : config.GroundSpeedMetersPerSecond;
                        float step = speed / EnemyDirector.TickRate;
                        int tickLimit = Mathf.CeilToInt(Vector3.Distance(position, waypoint) / step) + 2;
                        for (int tick = 0; tick < tickLimit && Vector3.Distance(position, waypoint) > 0.001f;
                             tick++)
                        {
                            Vector3 proposed = Vector3.MoveTowards(position, waypoint, step);
                            Vector3 forward = waypoint - proposed;
                            Quaternion proposedRotation = forward.sqrMagnitude > 0.001f
                                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                                : rotation;
                            Assert.That(M2SupportLegCollisionGuard.TryResolve(actorCollider, supportLegs,
                                    proposed, proposedRotation, out Vector3 resolved), Is.True,
                                $"Could not resolve Climber on authored route {rail.Id}, waypoint {waypointIndex}.");
                            Assert.That(HasPositiveSupportPenetration(actorCollider, supportLegs,
                                    resolved, proposedRotation), Is.False,
                                $"Climber penetrated a SupportLeg on authored route {rail.Id}, waypoint {waypointIndex}.");

                            M2AttackCapsulePoseResult result = M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(
                                actorCollider, targetCapsule, resolved, proposedRotation, out _);
                            int allCapsuleOverlapCount = M2AttackCapsuleStandOffGuard.CountPositivePenetrations(
                                actorCollider, attackCapsules, resolved, proposedRotation, out _);
                            nonTargetOverlapObservations += allCapsuleOverlapCount -
                                (result == M2AttackCapsulePoseResult.TargetCapsuleReached ? 1 : 0);
                            if (result == M2AttackCapsulePoseResult.TargetCapsuleReached)
                            {
                                Assert.That(M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(actorCollider,
                                        position, rotation, targetCapsule as CapsuleCollider,
                                        out float surfaceGap), Is.True);
                                Assert.That(surfaceGap, Is.GreaterThan(0f),
                                    $"Climber/{face} terminal AP_C gap was not positive.");
                                Assert.That(M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(actorCollider,
                                        targetCapsule, position, rotation, out _),
                                    Is.EqualTo(M2AttackCapsulePoseResult.Clear));
                                TestContext.WriteLine($"climber/{face}/{targetCapsule.name}: gap={surfaceGap:R} m, " +
                                                      $"non-target overlap observations={nonTargetOverlapObservations}");
                                reachedClimbStandOff = true;
                                break;
                            }
                            position = resolved;
                            rotation = proposedRotation;
                        }
                    }
                    Assert.That(reachedClimbStandOff, Is.True,
                        $"Climber could not reach an assigned AP_C stand-off along {rail.Id}.");
                    Assert.That(nonTargetOverlapObservations, Is.GreaterThan(0),
                        $"Climber/{face} did not retain the expected informational AP_G overlap observation.");
                }
            }
            finally
            {
                if (actorObject != null) UnityEngine.Object.DestroyImmediate(actorObject);
                if (grayboxRoot != null) UnityEngine.Object.DestroyImmediate(grayboxRoot);
            }
        }

        [Test]
        public void UpperSafeZone_TargetAssignmentIsRejected()
        {
            ActorSpatialSnapshot player = UpperActor(new EntityId(10), DamageableKind.Player);
            ActorSpatialSnapshot partner = UpperActor(new EntityId(20), DamageableKind.Partner);

            Assert.That(UpperSafeZoneSafety.EvaluateEnemyTargetAssignment(player),
                Is.EqualTo(EnemyTargetAssignmentStatus.RejectedUpperSafeZone));
            Assert.That(UpperSafeZoneSafety.EvaluateEnemyTargetAssignment(partner),
                Is.EqualTo(EnemyTargetAssignmentStatus.RejectedUpperSafeZone));
            Assert.That(UpperSafeZoneSafety.CanReceiveEnemyAttack(player), Is.False);
            Assert.That(UpperSafeZoneSafety.CanReceiveEnemyAttack(partner), Is.False);
        }

        [Test]
        public void UpperSafeZone_OneThousandEnemyDamageAttemptsApplyZeroActorDamage()
        {
            EntityId playerId = new EntityId(10);
            RunState state = RunStateFactory.CreateFixture(new RunStateFixture
            {
                RunValue = 2,
                FixedTick = 100,
                NextEventSequence = 1,
                Damageables = new[]
                {
                    new DamageableFixture
                    {
                        EntityValue = playerId.Value,
                        Kind = DamageableKind.Player,
                        HitPoints = 100
                    }
                }
            });
            var resolver = new DamageResolver(state);
            ActorSpatialSnapshot spatial = UpperActor(playerId, DamageableKind.Player);

            int rejected = 0;
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                DamageReceipt receipt = resolver.ApplyEnemyAttack(new DamagePacket(
                    state.IssueEventSequence(), state.FixedTick, new EntityId(100), new EntityId(101), playerId,
                    DamageKind.Melee, 1f, spatial.Position, Vector3.zero, new FireSequenceId((ulong)attempt + 1)), spatial);
                if (receipt.Status == DamageApplyStatus.RejectedUpperSafeZone) rejected++;
                Assert.That(receipt.HitPointsAfter, Is.EqualTo(receipt.HitPointsBefore));
            }

            Assert.That(rejected, Is.EqualTo(1000));
            Assert.That(state.TryGetDamageable(playerId, out DamageableSnapshot snapshot), Is.True);
            Assert.That(snapshot.HitPoints, Is.EqualTo(100f));
            Assert.That(state.PlayerLife, Is.EqualTo(PlayerLifeState.Alive));
        }

        [Test]
        public void NormalDescent_CompletesAtTick180_WithEntryAndExitPlacement()
        {
            var traversal = new TraversalController();
            Assert.That(traversal.BeginNormalDescent(), Is.EqualTo(TraversalCommandStatus.Started));
            Assert.That(traversal.Snapshot.State, Is.EqualTo(TraversalState.Descending));
            Assert.That(traversal.Snapshot.Zone, Is.EqualTo(LocationZone.UpperSafeZone));

            AssertInProgressThrough(traversal, TraversalController.NormalDescentTicks - 1);
            Assert.That(traversal.Snapshot.ElapsedTicks, Is.EqualTo(179));
            Assert.That(traversal.AdvanceOneTick(true, false), Is.EqualTo(TraversalStepStatus.Completed));
            AssertCompletedAt(traversal, LocationZone.GroundDanger, TraversalPlatformNodeId.GroundLanding);
        }

        [Test]
        public void NormalAscent_CompletesAtTick360_WithEntryAndExitPlacement()
        {
            TraversalController traversal = CreateAtGround();
            Assert.That(traversal.BeginNormalAscent(), Is.EqualTo(TraversalCommandStatus.Started));

            AssertInProgressThrough(traversal, TraversalController.NormalAscentTicks - 1);
            Assert.That(traversal.Snapshot.ElapsedTicks, Is.EqualTo(359));
            Assert.That(traversal.AdvanceOneTick(true, false), Is.EqualTo(TraversalStepStatus.Completed));
            AssertCompletedAt(traversal, LocationZone.UpperSafeZone, TraversalPlatformNodeId.UpperDeck);
        }

        [TestCase(false, TraversalStepStatus.InterruptedInputReleased)]
        [TestCase(true, TraversalStepStatus.InterruptedLargeDamage)]
        public void Traversal_InterruptUsesLastPassedValidatedPlatform(bool largeDamage, TraversalStepStatus expected)
        {
            var traversal = new TraversalController();
            Assert.That(traversal.BeginNormalDescent(), Is.EqualTo(TraversalCommandStatus.Started));
            AssertInProgressThrough(traversal, 82);
            Assert.That(traversal.Snapshot.LastPassedNode, Is.EqualTo(TraversalPlatformNodeId.MidAccess));

            TraversalStepStatus status = traversal.AdvanceOneTick(largeDamage, largeDamage);

            Assert.That(status, Is.EqualTo(expected));
            AssertCompletedAt(traversal, LocationZone.LowerAccess, TraversalPlatformNodeId.MidAccess);
        }

        private static ActorSpatialSnapshot UpperActor(EntityId id, DamageableKind kind)
        {
            return new ActorSpatialSnapshot(id, kind, LocationZone.UpperSafeZone,
                new Vector3(0f, TowerGeometryFixture.UpperDeckY, 0f));
        }

        private static bool Overlaps(Collider movingCollider, Vector3 position, Quaternion rotation,
            Collider targetCollider)
        {
            return Physics.ComputePenetration(movingCollider, position, rotation,
                targetCollider, targetCollider.transform.position, targetCollider.transform.rotation,
                out _, out float depth) && depth > 0f;
        }

        private static bool HasPositiveSupportPenetration(Collider movingCollider,
            IReadOnlyList<Collider> supportLegs, Vector3 position, Quaternion rotation)
        {
            foreach (Collider supportLeg in supportLegs)
            {
                if (Physics.ComputePenetration(movingCollider, position, rotation,
                        supportLeg, supportLeg.transform.position, supportLeg.transform.rotation,
                        out _, out float depth) && depth > 0f)
                    return true;
            }
            return false;
        }

        private static TraversalController CreateAtGround()
        {
            var traversal = new TraversalController();
            Assert.That(traversal.BeginNormalDescent(), Is.EqualTo(TraversalCommandStatus.Started));
            for (int i = 0; i < TraversalController.NormalDescentTicks; i++)
                traversal.AdvanceOneTick(true, false);
            return traversal;
        }

        private static void AssertInProgressThrough(TraversalController traversal, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                Assert.That(traversal.AdvanceOneTick(true, false), Is.EqualTo(TraversalStepStatus.InProgress));
        }

        private static void AssertCompletedAt(TraversalController traversal, LocationZone zone,
            TraversalPlatformNodeId node)
        {
            TraversalSnapshot snapshot = traversal.Snapshot;
            Assert.That(snapshot.State, Is.EqualTo(TraversalState.None));
            Assert.That(snapshot.Zone, Is.EqualTo(zone));
            Assert.That(snapshot.LastPassedNode, Is.EqualTo(node));
            Assert.That(snapshot.CanPerformNonMovementAction, Is.True);
        }
    }
}
