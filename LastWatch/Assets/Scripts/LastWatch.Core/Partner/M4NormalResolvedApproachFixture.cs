using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LastWatch.Core.Partner
{
    // Explicitly created diagnostic owner, never attached to a production controller/session.
    // Supports PlayMode main thread only. All physics calculations remain existing pair-explicit guards.
    public sealed class M4NormalResolvedApproachFixture : IDisposable
    {
        static int mainThreadId;
        static ulong nextGeneration;
        static M4NormalResolvedApproachFixture activeOwner;
        readonly M4ResolvedApproachFixtureRequest request;
        readonly string sessionId = Guid.NewGuid().ToString("N");
        readonly ulong generation;
        readonly List<Object> ownedObjects = new List<Object>();
        readonly List<Collider> supports = new List<Collider>();
        readonly List<Collider> attackTargets = new List<Collider>();
        GameObject root;
        Scene fixtureScene;
        AsyncOperation sceneUnload;
        M2GrayboxBuilder builder;
        ActiveEnemyActor actor;
        Collider assignedTarget;
        RouteId[] routeIds = Array.Empty<RouteId>();
        M4UpperLaneRouteSnapshot[] routes = Array.Empty<M4UpperLaneRouteSnapshot>();
        Vector3[] baseWaypoints = Array.Empty<Vector3>();
        int iteration;
        bool stopped, released, available;
        string reason = "SETUP_NOT_STARTED", fingerprint, exceptionType = string.Empty;
        M4MovementAttemptSnapshot lastAttempt;
        M4UpperLaneActorSnapshot lastActor;
        M4ResolvedApproachFixtureColliderSnapshot[] lastColliders = Array.Empty<M4ResolvedApproachFixtureColliderSnapshot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CapturePlayModeMainThread()
        {
            // No world creation or query at initialization. Preserve any surviving owner when domain reload is disabled.
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        static void RequireMainThread()
        {
            if (mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Fixture requires initialized PlayMode Unity main thread.");
        }

        M4NormalResolvedApproachFixture(M4ResolvedApproachFixtureRequest value, string observedFingerprint)
        {
            request = value;
            fingerprint = observedFingerprint ?? string.Empty;
            generation = checked(++nextGeneration);
        }

        // Pure source inventory: no PlayMode owner, scene, collider, session, guard or physics is created.
        public static M4NormalApproachAssignmentInventorySnapshot CaptureSupportedNormalAssignments(
            string observedSourceFingerprint)
        {
            M4AuthoredRouteAssignmentCatalogSnapshot source =
                M4NightOneGrayboxController.BuildAuthoredRouteAssignmentCatalog(TowerGeometryFixture.CreateNormal());
            return new M4NormalApproachAssignmentInventorySnapshot(observedSourceFingerprint,
                source.GeometryAvailable, source.UnavailableReason,
                source.Assignments.Select(row => new M4NormalApproachAssignmentSnapshot(row.Archetype,
                    row.Face, row.AssignedAttackPoint, row.Available, row.UnavailableReason)).ToArray());
        }

        public static M4NormalResolvedApproachFixture Create(M4ResolvedApproachFixtureRequest request,
            string observedSourceFingerprint)
        {
            RequireMainThread();
            if (request == null) throw new ArgumentNullException(nameof(request));
            var fixture = new M4NormalResolvedApproachFixture(request, observedSourceFingerprint);
            if (activeOwner != null && !activeOwner.DestructionComplete())
            {
                fixture.Stop("PREVIOUS_FIXTURE_ACTIVE_OR_AWAITING_DESTROY", false);
                return fixture;
            }
            activeOwner = null;
            if (!Application.isPlaying)
            {
                fixture.Stop("PLAY_MODE_REQUIRED", false);
                return fixture;
            }
            if (string.IsNullOrWhiteSpace(request.ExpectedSourceFingerprint) || !fixture.FingerprintMatches())
            {
                fixture.Stop("SOURCE_FINGERPRINT_UNAVAILABLE_OR_MISMATCH", false);
                return fixture;
            }
            if (request.DiagnosticIterationBudget <= 0 ||
                !Enum.IsDefined(typeof(EnemyArchetype), request.Archetype) ||
                !Enum.IsDefined(typeof(FaceId), request.Face))
            {
                fixture.Stop("UNSUPPORTED_ASSIGNMENT_OR_DIAGNOSTIC_BUDGET", false);
                return fixture;
            }
            AttackPointId target = request.Archetype == EnemyArchetype.Climber
                ? M4NightOneGrayboxController.AttackPointIdForClimbFace(request.Face)
                : M4NightOneGrayboxController.AttackPointIdForGroundFace(request.Face);
            if (target != request.AssignedAttackPoint)
            {
                fixture.Stop("UNSUPPORTED_ASSIGNMENT_TARGET", false);
                return fixture;
            }
            activeOwner = fixture;
            try
            {
                fixture.Build();
                fixture.available = true;
                fixture.reason = "READY_NO_INVOCATION";
                fixture.RefreshDetachedObservation();
            }
            catch (Exception error)
            {
                fixture.exceptionType = error.GetType().FullName;
                fixture.Stop("SETUP_FAILED", false);
                fixture.Close();
            }
            return fixture;
        }

        bool FingerprintMatches() => string.Equals(request.ExpectedSourceFingerprint, fingerprint, StringComparison.Ordinal);

        void Build()
        {
            // Local physics world prevents both production queries and autonomous production physics seeing this owner.
            fixtureScene = SceneManager.CreateScene("M4_NormalApproachFixture_" + sessionId,
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            root = new GameObject("M4_NormalApproachFixture_" + sessionId);
            ownedObjects.Add(root);
            SceneManager.MoveGameObjectToScene(root, fixtureScene);
            builder = root.AddComponent<M2GrayboxBuilder>();
            builder.BuildNormal();
            TowerGeometryFixture geometry = builder.Geometry;
            M2GrayboxColliderTag[] tags = builder.GetComponentsInChildren<M2GrayboxColliderTag>(true);
            foreach (M2GrayboxColliderTag tag in tags)
            {
                Collider collider = tag.GetComponent<Collider>();
                if (collider == null) continue;
                if (tag.Kind == M2GrayboxColliderKind.SupportLeg) supports.Add(collider);
                else if (tag.Kind == M2GrayboxColliderKind.AttackCapsule)
                {
                    attackTargets.Add(collider);
                    if (collider.name == request.AssignedAttackPoint.ToString())
                    {
                        if (assignedTarget != null) throw new InvalidOperationException("Duplicate assigned target.");
                        assignedTarget = collider;
                    }
                }
            }
            if (geometry == null || geometry.UnavailableFace.HasValue || attackTargets.Count != 6 ||
                supports.Count != 3 || assignedTarget == null || !assignedTarget.enabled ||
                !assignedTarget.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Complete normal authored supports and six AP targets are required.");
            bool climb = request.Archetype == EnemyArchetype.Climber;
            RouteKind[] kinds = climb
                ? new[] { RouteKind.SpawnLane, RouteKind.ApproachSpline, RouteKind.ClimberRail }
                : new[] { RouteKind.SpawnLane, RouteKind.ApproachSpline };
            routes = kinds.Select(kind => new M4UpperLaneRouteSnapshot(geometry.Routes.Single(
                route => route.Face == request.Face && route.Kind == kind))).ToArray();
            routeIds = routes.Select(route => route.Id).ToArray();
            baseWaypoints = M4NightOneGrayboxController.BuildAuthoredApproachWaypoints(geometry,
                request.Face, climb, out _);
            // Fixture-only identity is never armed in RunState, Director, Pool or a live hit registry.
            var incarnation = new ActiveEnemyIncarnation(new EntityId(generation), generation, request.Archetype);
            actor = M4NightOneGrayboxController.CreateSharedApproachActor(incarnation, request.Face,
                geometry, _ => assignedTarget, created =>
                {
                    ownedObjects.Add(created);
                    SceneManager.MoveGameObjectToScene(created, fixtureScene);
                }, material => ownedObjects.Add(material), actualSupportLegs: supports);
            actor.transform.SetParent(root.transform, true);
            if (actor.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Shared factory did not supply its actual mover collider.");
        }

        public M4ResolvedApproachFixtureSnapshot AdvanceOnce(string observedSourceFingerprint)
            => AdvanceOnceCore(observedSourceFingerprint, false);

        public M4ResolvedApproachFixtureSnapshot AdvanceOnceWithSupportTrace(string observedSourceFingerprint)
            => AdvanceOnceCore(observedSourceFingerprint, true);

        M4ResolvedApproachFixtureSnapshot AdvanceOnceCore(string observedSourceFingerprint, bool captureSupportTrace)
        {
            RequireMainThread();
            fingerprint = observedSourceFingerprint ?? string.Empty;
            if (!FingerprintMatches())
            {
                Stop("SOURCE_FINGERPRINT_UNAVAILABLE_OR_MISMATCH", false);
                return Capture();
            }
            if (released || stopped) return Capture();
            if (iteration >= request.DiagnosticIterationBudget)
            {
                Stop("DIAGNOSTIC_ITERATION_BUDGET_EXHAUSTED_NOT_GAMEPLAY_OUTCOME", false);
                return Capture();
            }
            if (actor == null || assignedTarget == null || actor.GetComponent<Collider>() == null ||
                !actor.isActiveAndEnabled || !assignedTarget.enabled || !assignedTarget.gameObject.activeInHierarchy ||
                supports.Any(value => value == null || !value.enabled || !value.gameObject.activeInHierarchy))
            {
                Stop("OWNED_SETUP_COLLIDER_OR_ACTOR_UNAVAILABLE", false);
                return Capture();
            }
            iteration++;
            try
            {
                bool accepted = captureSupportTrace ? actor.AdvanceOneTickWithSupportTrace(supports)
                    : actor.AdvanceOneTick(supports);
                RefreshDetachedObservation();
                if (lastAttempt == null || !lastAttempt.InvocationAvailable)
                    Stop("ACTUAL_INVOCATION_OBSERVATION_UNAVAILABLE", false);
                else if (!accepted)
                    Stop(lastAttempt.Outcome, false);
                else if (lastAttempt.Outcome == "STANDOFF_REACHED_POSITIVE_GAP_HELD_PRIOR_POSE")
                    Stop("STANDOFF_REACHED_POSITIVE_GAP_HELD_PRIOR_POSE_NOT_ATTACK_COMPLETION", true);
                else if (lastAttempt.Outcome != "TRANSFORM_APPLIED")
                    Stop("UNSUPPORTED_MOVEMENT_OUTCOME_" + lastAttempt.Outcome, false);
                else reason = "ACTUAL_TICK_TRANSFORM_APPLIED";
            }
            catch (Exception error)
            {
                exceptionType = error.GetType().FullName;
                lastAttempt = actor == null ? null : actor.LatestMovementAttempt;
                Stop("INTERRUPTED_INVOCATION_OR_OBSERVATION", false);
            }
            return Capture();
        }

        void Stop(string stopReason, bool rowAvailable)
        {
            stopped = true;
            available = rowAvailable;
            reason = stopReason;
        }

        void RefreshDetachedObservation()
        {
            lastAttempt = actor.LatestMovementAttempt;
            lastActor = actor.CaptureUpperLaneObservation(false, routeIds);
            var values = new List<M4ResolvedApproachFixtureColliderSnapshot>();
            Collider mover = actor.GetComponent<Collider>();
            if (mover != null) values.Add(new M4ResolvedApproachFixtureColliderSnapshot(mover, sessionId, "FIXTURE_ACTOR_MOVER_UNREGISTERED"));
            foreach (Collider support in supports)
                if (support != null) values.Add(new M4ResolvedApproachFixtureColliderSnapshot(support, sessionId, "AUTHORED_SUPPORT_LEG_PAIR_INPUT"));
            foreach (Collider target in attackTargets)
                if (target != null) values.Add(new M4ResolvedApproachFixtureColliderSnapshot(target, sessionId,
                    target == assignedTarget ? "ASSIGNED_AP_QUERY" : "NON_TARGET_AP_QUERY_NOT_STANDOFF_GATE"));
            lastColliders = values.ToArray();
        }

        // Pure detached copy; no mover/guard/physics sync/query. Lifecycle poll only inspects owned Unity null state.
        public M4ResolvedApproachFixtureSnapshot Capture()
        {
            RequireMainThread();
            return new M4ResolvedApproachFixtureSnapshot(sessionId, generation, request, fingerprint,
                iteration, available && !released, reason, stopped, released, released && !DestructionComplete(),
                lastAttempt, lastActor, routes, baseWaypoints, lastColliders, exceptionType);
        }

        bool DestructionComplete() => released && ownedObjects.All(value => value == null) &&
            (!fixtureScene.IsValid() || !fixtureScene.isLoaded) && (sceneUnload == null || sceneUnload.isDone);

        public void Close()
        {
            RequireMainThread();
            if (released) return;
            released = true;
            stopped = true;
            if (root != null) root.SetActive(false);
            Object[] generated = Array.Empty<Object>();
            try
            {
                if (builder != null)
                {
                    // Receipts precede release, so deferred Destroy can be polled without live API escaping.
                    generated = builder.CaptureOwnedGeneratedObjects();
                    ownedObjects.AddRange(generated);
                    builder.ReleaseOwnedGeneratedObjects();
                }
            }
            catch (Exception error)
            {
                exceptionType += "; CLEANUP_RELEASE:" + error.GetType().FullName;
                reason = "CLEANUP_RELEASE_EXCEPTION";
                available = false;
                generated = Array.Empty<Object>(); // Retry only known owner receipts; never search a live/shared root.
            }
            // Builder handles its own children/materials. Actor primitive/material and root belong to this owner only.
            foreach (Object value in ownedObjects.Distinct())
            {
                if (value == null || generated.Contains(value)) continue;
                if (value is GameObject gameObject) gameObject.SetActive(false);
                Object.Destroy(value);
            }
            if (fixtureScene.IsValid() && fixtureScene.isLoaded)
            {
                try { sceneUnload = SceneManager.UnloadSceneAsync(fixtureScene); }
                catch (Exception error)
                {
                    exceptionType += "; SCENE_UNLOAD:" + error.GetType().FullName;
                    reason = "SCENE_UNLOAD_EXCEPTION";
                    available = false;
                }
            }
        }

        public void Dispose() => Close();
    }
}
