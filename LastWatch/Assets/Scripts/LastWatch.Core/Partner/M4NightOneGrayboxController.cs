using System;
using System.Collections.Generic;
using System.Linq;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using LastWatch.Core.Traversal;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public enum M4QaReplayStatus { NotStarted, Running, Completed, Diverged }

    // Bounded three-Night diagnostic actor/input bridge. It deliberately consumes the
    // session-owned M3 Director and completion boundary supplied by M4ThreeNightQaSceneEntry;
    // it does not own a queue, choose a Night seed, advance a clock, or manufacture completion.
    [DisallowMultipleComponent]
    public sealed class M4NightOneGrayboxController : MonoBehaviour, IM4ThreeNightQaFixedTickProcessor, IM4PartnerExchangeCompletionSink
    {
        enum GrayboxWeapon { ServicePistol, AssaultRifle, WatchRifle }
        enum ReloadTransactionStatus { None, Active, Committed, Cancelled }

        [Serializable] sealed class PlayerPartsQaFixture
        {
            public string fixtureId;
            public int playerParts;
            public int partnerParts;
        }

        const ulong PlayerEntityValue = 0x4d340101UL;
        const ulong PartnerEntityValue = 0x4d340102UL;
        const ulong QaRunValue = 0x4d340001UL;

        // Inherited v3.0 section 9.1 weapon values. Ammo is run-scoped and is never refilled
        // between the three QA Nights; Service Pistol keeps its specified infinite reserve.
        const int ServicePistolMagazineCapacity = 12;
        const float ServicePistolDamage = 28f;
        const int ServicePistolReloadTicks = 58; // 1.15 seconds at 50Hz, rounded up.
        const int ServicePistolShotIntervalTicks = 12; // 0.24 seconds at 50Hz.
        const int AssaultRifleMagazineCapacity = 30;
        const int AssaultRifleStartingReserve = 150;
        const float AssaultRifleDamage = 18f;
        const int AssaultRifleReloadTicks = 88; // 1.75 seconds represented at 50Hz; completes on the next tick.
        const int AssaultRifleMinimumShotIntervalTicks = 5;
        const int WatchRifleMagazineCapacity = 5;
        const int WatchRifleStartingReserve = 10;
        const float WatchRifleDamage = 112f;
        const int WatchRifleReloadTicks = 113; // 2.25 seconds at 50Hz, rounded up.
        const int WatchRifleShotIntervalTicks = 46; // 0.92 seconds at 50Hz.

        [Header("Optional authored weak-point collider prefabs")]
        [SerializeField] M4EnemyWeakPointCollider walkerWeakPointPrefab;
        [SerializeField] M4EnemyWeakPointCollider runnerWeakPointPrefab;
        [SerializeField] M4EnemyWeakPointCollider breakerWeakPointPrefab;
        [SerializeField] M4EnemyWeakPointCollider climberWeakPointPrefab;
        [Header("Optional authored Partner movement (no default graph or speed)")]
        [SerializeField] M4PartnerMovementProfile partnerMovementProfile;
        [Header("Explicit temporary QA profile asset (scene opt-in only)")]
        [SerializeField] TextAsset qaPartnerMovementProfileAsset;
        [SerializeField] TextAsset qaPartnerExchangePolicyAsset;
        [SerializeField] TextAsset qaPlayerPartsFixtureAsset;
        [SerializeField] bool qaEnableManualTraversal;
        TraversalController manualTraversal;
        bool manualERequested, manualEArmed = true, manualFocused = true, manualTraversalUsed;
        int manualArrivalFrame = -1;
        int manualFreshEFrame = -1;
        string manualLastArrival;
        float manualArrivalCueUntil;
        string manualArrivalNoticeFact,manualArrivalNoticeAction,manualArrivalNoticeText;
        GUIStyle manualCueStyle,manualArrivalStyle;
        enum ManualEntryStage { Preview, Enqueue, FixedPending }
        readonly struct ManualEntryReadiness
        {
            public readonly ManualTraversalEntryEvaluation Entry;
            public readonly bool Eligible,ControlsOpen;
            public readonly string Reason;
            public ManualEntryReadiness(ManualTraversalEntryEvaluation entry,bool eligible,bool controlsOpen,string reason)
            {Entry=entry;Eligible=eligible;ControlsOpen=controlsOpen;Reason=reason;}
        }
        string manualTraversalReceipt = "Normal traversal: explicit QA opt-in unavailable.";
        M4PartnerExchangePolicy partnerExchangePolicy;
        string partnerExchangePolicyIdentity;
        ulong partnerExchangeSelectionEpisode;

        readonly Dictionary<ulong, ActiveEnemyActor> actors = new Dictionary<ulong, ActiveEnemyActor>();
        readonly List<Collider> supportLegColliders = new List<Collider>();
        readonly List<Collider> attackCapsules = new List<Collider>();
        readonly Dictionary<FaceId, Collider> groundAttackCapsules = new Dictionary<FaceId, Collider>();
        readonly Dictionary<FaceId, Collider> climbAttackCapsules = new Dictionary<FaceId, Collider>();
        readonly M4ReplayShotDiagnosticAccumulator replayShotDiagnostics = new M4ReplayShotDiagnosticAccumulator();
        M4ThreeNightQaSceneEntry entry;
        TowerGeometryFixture runtimeGeometry;
        Camera playerCamera;
        Transform playerTransform;
        Transform partnerTransform;
        M4PartnerMovement partnerMovement;
        string authoredInitialPartnerNode;
        ulong lastPartnerProtectedShotSequence;
        int lastPartnerProtectedShotTick = -1;
        Vector3 lastPartnerProtectedShotOrigin;
        Vector3 lastPartnerProtectedShotEnd;
        string manualUpperOriginReceipt = "Manual UpperSafeZone origin check: awaiting movement input.";
        bool startRequested;
        bool shotRequested;
        bool reloadRequested;
        bool partnerCoverRequested;
        enum PartnerQMode { None, Face, Exchange, Unavailable }
        M4PartnerOrderSelection partnerOrderSelection;
        PartnerQMode partnerQMode;
        bool partnerQArmed;
        bool partnerExchangeOpenRequested;
        bool partnerMouse0Blocked, partnerMouse1Blocked;
        string partnerQPreview = "Q: 방향 지정";
        bool partnerCoverAimValid;
        bool partnerCoverFaceValid;
        FaceId partnerCoverRequestedFace;
        Vector3 partnerCoverAimOrigin;
        Vector3 partnerCoverAimDirection;
        string partnerCoverAimFailure;
        string partnerCoverInputRunIdentity;
        int partnerCoverInputNight;
        BoxCollider partnerExchangeStation;
        bool partnerExchangeMenuOpen;
        bool partnerExchangeBeginRequested;
        bool partnerExchangeCancelRequested;
        ulong partnerExchangeCancelTransactionId;
        ExchangeDirection partnerExchangeDirection;
        ExchangeResource partnerExchangeResource;
        string partnerExchangeInputRunIdentity;
        int partnerExchangeInputNight;
        int suppressMenuShotThroughFrame = -1;
        string partnerExchangeReceipt = "RallyExchange AR partial slice; Parts inventory UNAVAILABLE.";
        // Read-only runtime evidence, deliberately outside canonical DTO/save-wire state.
        [Serializable] sealed class ExchangeFeedback
        {
            public string runIdentity, selectionEpisode, direction, result, transactionId;
            public string resource;
            public int nightOrdinal, runTick, amount;
            public int playerReserveBefore, playerReserveAfter, partnerStockBefore, partnerStockAfter;
            public int transferredResourcePlayerBefore, transferredResourcePlayerAfter;
            public int transferredResourcePartnerBefore, transferredResourcePartnerAfter;
        }
        [Serializable] sealed class ExchangeFeedbackSnapshot
        {
            public string schema = "LastWatch.ExchangeFeedback.v1", runIdentity;
            public int runTick, playerArMagazine, playerArReserve, partnerArStock;
            public int partnerParts;
            public string playerPartsAvailability;
            public M4PlayerResourceInventory playerResourceInventory;
            public ManualTraversalFeedback manualTraversal;
            public ExchangeFeedback latestSelection, lastCompletedTransfer;
            public ExchangeTransaction retainedTransaction;
        }
        [Serializable] sealed class ManualTraversalFeedback
        {
            public string actualPose,actualZone,canonicalTraversal,state,lastPassedNode,receipt,domainAbortReason;
            public string entryBlockedReason,arrival,actionReadiness;
            public bool entryEligible,entryAscending;
            public int elapsedTicks,totalTicks;
            public bool interruptedRoute;
            public string limits="50Hz source QA; big-damage event, full ground AI, body collision, replay and whole-actor restore not verified/supported.";
        }
        string exchangeFeedbackRun;
        ExchangeFeedback latestExchangeSelection, lastExchangeTransfer;
        Vector2 partnerExchangeScroll;
        string partnerRequestReceipt = "Q: CoverFace request; movement/path execution unavailable.";
        Vector2 qaPanelScroll;
        M4PartnerAcknowledgementCue partnerAcknowledgementCue;
        string structureAttackFeedback = string.Empty;
        bool started;
        bool recordingReplay;
        bool playingReplay;
        M4ReplayTape replayTape;
        string captureRunId;
        M4QaCaptureRuntimeIdentity captureRuntimeIdentityAtStart;
        M4QaCaptureInputKind captureInputKind = M4QaCaptureInputKind.OwnerLiveInput;
        string captureInputPlanId = string.Empty;
        M4ReplayConsumer replayConsumer;
        string replayCaptureFailure;
        bool replayCaptureStoppedByManualPartner;
#if UNITY_EDITOR
        bool deterministicInputPlanCapture;
        bool playingInputPlan;
        bool playingAdaptiveQa;
        Func<M4QaAdaptiveSnapshot, M4ReplayTick> adaptiveQaCommand;
        int adaptiveQaConsumedTicks;
        M4ReplayConsumer inputPlanConsumer;
        M4QaInputPlanCaptureStatus inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.NotStarted;
        string inputPlanCaptureFailure = string.Empty;
#endif
        string replayFailure = string.Empty;
        float cameraPitchDegrees;
        GrayboxWeapon currentWeapon = GrayboxWeapon.AssaultRifle;
        int servicePistolMagazine = ServicePistolMagazineCapacity;
        int assaultRifleMagazine = AssaultRifleMagazineCapacity;
        int assaultRifleReserve = AssaultRifleStartingReserve;
        int watchRifleMagazine = WatchRifleMagazineCapacity;
        int watchRifleReserve = WatchRifleStartingReserve;
        int reloadCompleteRunTick = -1;
        GrayboxWeapon reloadingWeapon;
        ReloadTransactionStatus reloadTransactionStatus;
        ulong nextReloadTransactionId = 1;
        ulong activeReloadTransactionId;
        int reloadShotBlockedThroughRunTick = -1;
        bool reloadCancellationPending;
        int pistolNextLegalShotRunTick;
        int assaultNextLegalShotRunTick;
        int watchNextLegalShotRunTick;
        ulong nextFireSequence = 1;
        ulong activeFireSequence;
        int lastPlayerShotRunTick = -1;
        bool arTriggerHeld;
        bool arFirstShotRequested;
        bool triggerReleased = true;

        void StopManualTrigger()
        {
            arTriggerHeld = false;
            arFirstShotRequested = false;
            activeFireSequence = 0;
            triggerReleased = true;
            shotRequested = false;
        }

        void ApplyTriggerBoundary()
        {
            if (triggerReleased || !arTriggerHeld) activeFireSequence = 0;
        }

        FireSequenceId EmitPlayerFireSequence(int runTick)
        {
            if (!arTriggerHeld || activeFireSequence == 0 || lastPlayerShotRunTick < 0 ||
                runTick - lastPlayerShotRunTick >= 18) // ceil(.35s * existing 50Hz).
            {
                if (nextFireSequence == ulong.MaxValue)
                    throw new InvalidOperationException("Player FireSequence cursor exhausted.");
                activeFireSequence = nextFireSequence++;
            }
            lastPlayerShotRunTick = runTick;
            ulong emitted = activeFireSequence;
            if (!arTriggerHeld) activeFireSequence = 0;
            return new FireSequenceId(emitted);
        }
        string status = "Select Normal or Hard, then press Start.";
        bool aimingAtActiveEnemy;
        string hitFeedback = string.Empty;
        Color hitFeedbackColor = Color.white;
        float hitFeedbackUntil;
        float hitFlashUntil;
        string replayShotDiagnosticsFailure = string.Empty;
        int upperLaneCompletedRunTick = -1;
        int upperLaneCompletedNightTick = -1;
        int upperLaneCompletedNightOrdinal;

        // Read-only QA outputs. A completed replay still needs an evidence/hash comparison.
        public M4QaReplayStatus ReplayStatus { get; private set; } = M4QaReplayStatus.NotStarted;
        public string ReplayFailure => replayFailure;
        public string ReplayPriorStatus { get; private set; } = string.Empty;
        public int ReplayBlockedResolvedEnemyShots { get; private set; }
        public int ReplayBlockedWorldShots { get; private set; }
        public M4ReplayShotDiagnosticSnapshot ReplayShotDiagnostics => replayShotDiagnostics.CreateSnapshot(
            captureRunId,
            captureInputKind == M4QaCaptureInputKind.DeterministicInputPlan ? captureInputPlanId : string.Empty,
            captureInputKind == M4QaCaptureInputKind.DeterministicInputPlan);
        public string ReplayShotDiagnosticsFailure => replayShotDiagnosticsFailure;
        public int ReplayConsumedTicks => replayConsumer == null ? 0 : replayConsumer.Cursor;
        public int ReplayTotalTicks => replayConsumer == null ? 0 : replayConsumer.Count;

        // Main-thread passive fixture inventory, including inactive archetypes. Base paths
        // reuse this owner's runtime builder; no actor, collider, guard or tick is evaluated.
        // Before normal graybox initialization, returns unavailable without creating a fixture.
        // Returned detached values survive later ticks, Night changes and controller destruction.
        public M4AuthoredRouteAssignmentCatalogSnapshot CaptureAuthoredRouteAssignmentCatalog()
            => BuildAuthoredRouteAssignmentCatalog(runtimeGeometry);

        // Shared detached source disclosure; the instance wrapper still reports unavailable before world initialization.
        internal static M4AuthoredRouteAssignmentCatalogSnapshot BuildAuthoredRouteAssignmentCatalog(TowerGeometryFixture geometry)
        {
            if (geometry == null)
                return new M4AuthoredRouteAssignmentCatalogSnapshot(false, "RUNTIME_GEOMETRY_UNAVAILABLE", null,
                    Array.Empty<M4UpperLaneRouteSnapshot>(), Array.Empty<M4AuthoredRouteAssignmentSnapshot>());

            M4UpperLaneRouteSnapshot[] allRoutes = geometry.Routes
                .Select(route => new M4UpperLaneRouteSnapshot(route)).ToArray();
            var assignments = new List<M4AuthoredRouteAssignmentSnapshot>();
            FaceId[] faces = geometry.AttackPoints.Select(target => target.Face)
                .Concat(geometry.Routes.Select(route => route.Face)).Distinct().OrderBy(face => face).ToArray();
            foreach (FaceId face in faces)
            foreach (EnemyArchetypeConfig config in EnemyArchetypeCatalog.All)
            {
                bool climb = config.Archetype == EnemyArchetype.Climber;
                AttackPointId targetId = climb ? AttackPointIdForClimbFace(face) : AttackPointIdForGroundFace(face);
                AttackCapsuleDescriptor[] targets = geometry.AttackPoints.Where(target => target.Id == targetId).ToArray();
                RouteKind[] kinds = climb
                    ? new[] { RouteKind.SpawnLane, RouteKind.ApproachSpline, RouteKind.ClimberRail }
                    : new[] { RouteKind.SpawnLane, RouteKind.ApproachSpline };
                var routes = new List<M4UpperLaneRouteSnapshot>();
                string reason = targets.Length != 1 ? "ASSIGNED_TARGET_DESCRIPTOR_MISSING_OR_AMBIGUOUS"
                    : !targets[0].IsAvailable ? "ASSIGNED_TARGET_DESCRIPTOR_UNAVAILABLE" : string.Empty;
                foreach (RouteKind kind in kinds)
                {
                    RouteDescriptor[] descriptors = geometry.Routes.Where(route => route.Face == face && route.Kind == kind).ToArray();
                    foreach (RouteDescriptor descriptor in descriptors) routes.Add(new M4UpperLaneRouteSnapshot(descriptor));
                    if (descriptors.Length != 1 && reason.Length == 0) reason = "SOURCE_ROUTE_DESCRIPTOR_MISSING_OR_AMBIGUOUS";
                    else if (descriptors.Any(route => !route.IsAvailable) && reason.Length == 0) reason = "SOURCE_ROUTE_DESCRIPTOR_UNAVAILABLE";
                }
                Vector3[] waypoints = Array.Empty<Vector3>();
                int? climbStart = null;
                if (reason.Length == 0)
                {
                    waypoints = BuildAuthoredApproachWaypoints(geometry, face, climb, out int index);
                    if (climb) climbStart = index;
                }
                assignments.Add(new M4AuthoredRouteAssignmentSnapshot(config.Archetype, face, targetId,
                    reason.Length == 0, reason, routes.ToArray(), waypoints, climbStart));
            }
            return new M4AuthoredRouteAssignmentCatalogSnapshot(true, string.Empty, geometry.UnavailableFace,
                allRoutes, assignments.ToArray());
        }

        // Caller observes after CompleteOneFixedTick returns. No live state reference,
        // movement, raycast, clock advancement, or Physics.SyncTransforms is performed.
        public M4UpperLaneSnapshot CaptureUpperLaneObservation()
        {
            var actorSnapshots = new List<M4UpperLaneActorSnapshot>();
            foreach (KeyValuePair<ulong, ActiveEnemyActor> pair in actors.OrderBy(value => value.Key))
            {
                ActiveEnemyActor actor = pair.Value;
                if (actor == null) continue;
                bool registered = pair.Key == actor.EntityId.Value;
                RouteId[] routeIds = runtimeGeometry == null ? Array.Empty<RouteId>() : runtimeGeometry.Routes
                    .Where(route => route.Face == FaceForAttackPoint(actor.TargetAttackPoint) &&
                        (route.Kind != RouteKind.ClimberRail || actor.Archetype == EnemyArchetype.Climber))
                    .Select(route => route.Id).ToArray();
                actorSnapshots.Add(actor.CaptureUpperLaneObservation(registered, routeIds));
            }
            M4UpperLaneRouteSnapshot[] routes = runtimeGeometry == null ? Array.Empty<M4UpperLaneRouteSnapshot>() :
                runtimeGeometry.Routes.Select(route => new M4UpperLaneRouteSnapshot(route)).ToArray();
            return new M4UpperLaneSnapshot(captureRunId, QaRunValue, upperLaneCompletedRunTick >= 0,
                upperLaneCompletedRunTick, upperLaneCompletedNightTick, upperLaneCompletedNightOrdinal,
                ReplayConsumedTicks, runtimeGeometry, routes, actorSnapshots.ToArray(), playerCamera != null,
                playerCamera == null ? Vector3.zero : playerCamera.transform.position,
                playerCamera == null ? Vector3.zero : playerCamera.transform.forward, cameraPitchDegrees,
                currentWeapon.ToString(), WeaponRange(currentWeapon), Physics.queriesHitTriggers,
                partnerTransform != null, partnerTransform == null ? Vector3.zero : partnerTransform.position,
                partnerTransform == null ? Quaternion.identity : partnerTransform.rotation);
        }

        static FaceId FaceForAttackPoint(AttackPointId point)
        {
            switch (point)
            {
                case AttackPointId.AP_A_G:
                case AttackPointId.AP_A_C: return FaceId.FaceA;
                case AttackPointId.AP_B_G:
                case AttackPointId.AP_B_C: return FaceId.FaceB;
                case AttackPointId.AP_C_G:
                case AttackPointId.AP_C_C: return FaceId.FaceC;
                default: throw new ArgumentOutOfRangeException(nameof(point));
            }
        }
#if UNITY_EDITOR
        public M4QaInputPlanCaptureStatus InputPlanCaptureStatus => inputPlanCaptureStatus;
        public string InputPlanCaptureFailure => inputPlanCaptureFailure;
        public string InputPlanId => captureInputPlanId;
        public int InputPlanConsumedTicks => playingAdaptiveQa || adaptiveQaConsumedTicks > 0 ? adaptiveQaConsumedTicks : inputPlanConsumer == null ? 0 : inputPlanConsumer.Cursor;
        public int InputPlanTotalTicks => inputPlanConsumer == null ? adaptiveQaConsumedTicks : inputPlanConsumer.Count;
#endif

        // Programmatic equivalent of the clipboard replay control on a fresh scene instance.
        public void BeginReplay(M4ReplayTape tape)
        {
            if (entry == null) throw new InvalidOperationException("The QA scene entry is unavailable.");
            if (started || entry.Started || ReplayStatus != M4QaReplayStatus.NotStarted)
                throw new InvalidOperationException("Replay requires a fresh, unstarted QA scene instance.");
            if (tape == null) throw new ArgumentNullException(nameof(tape));
            if (!tape.TryValidate(out string reason)) throw new ArgumentException(reason, nameof(tape));
            if (!tape.frozen) throw new ArgumentException("Replay requires a frozen three-Night input tape.", nameof(tape));

            entry.SelectDifficulty((LastWatchDifficulty)tape.difficulty);
            replayTape = tape;
            StartExplicitRun(false);
            if (!started) throw new InvalidOperationException("Replay explicit Start failed: " + status);
            status = "Replay started. Core will stop at the first command mismatch.";
        }

#if UNITY_EDITOR
        // Editor-only automated capture. The plan is detached/validated by its command
        // consumer, while the resulting replayTape is recorded tick-by-tick from commands
        // actually applied in this new canonical run.
        public void BeginDeterministicInputPlanCapture(M4QaInputPlan plan)
        {
            if (entry == null) throw new InvalidOperationException("The QA scene entry is unavailable.");
            if (started || entry.Started || ReplayStatus != M4QaReplayStatus.NotStarted || inputPlanCaptureStatus != M4QaInputPlanCaptureStatus.NotStarted)
                throw new InvalidOperationException("Input-plan capture requires a fresh, unstarted QA scene instance.");
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.TryValidate(out string reason)) throw new ArgumentException(reason, nameof(plan));

            entry.SelectDifficulty((LastWatchDifficulty)plan.difficulty);
            inputPlanConsumer = new M4ReplayConsumer(plan);
            replayShotDiagnostics.Reset();
            replayShotDiagnosticsFailure = string.Empty;
            deterministicInputPlanCapture = true;
            inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.Running;
            inputPlanCaptureFailure = string.Empty;
            StartExplicitRun(true, M4QaCaptureInputKind.DeterministicInputPlan, plan.planId);
            if (!started)
            {
                recordingReplay = false;
                inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.Failed;
                inputPlanCaptureFailure = "Canonical explicit Start failed: " + status;
                throw new InvalidOperationException(inputPlanCaptureFailure);
            }
            playingInputPlan = true;
            status = "Deterministic input-plan capture started through canonical Start and fixed-tick Core processing.";
        }

        // Editor-only policy boundary. The callback receives detached values from the
        // current canonical run, before this tick's player command is applied.
        public void BeginAdaptiveQaCapture(int difficulty, string stimulusId,
            Func<M4QaAdaptiveSnapshot, M4ReplayTick> nextCommand)
        {
            if (entry == null) throw new InvalidOperationException("The QA scene entry is unavailable.");
            if (started || entry.Started || ReplayStatus != M4QaReplayStatus.NotStarted ||
                inputPlanCaptureStatus != M4QaInputPlanCaptureStatus.NotStarted)
                throw new InvalidOperationException("Adaptive QA capture requires a fresh, unstarted QA scene instance.");
            if (difficulty != 0 && difficulty != 1) throw new ArgumentOutOfRangeException(nameof(difficulty));
            if (!M4QaAdaptiveSnapshot.IsValidStimulusId(stimulusId)) throw new ArgumentException("Invalid QA stimulusId.", nameof(stimulusId));
            if (nextCommand == null) throw new ArgumentNullException(nameof(nextCommand));
            entry.SelectDifficulty((LastWatchDifficulty)difficulty);
            adaptiveQaCommand = nextCommand;
            replayShotDiagnostics.Reset();
            replayShotDiagnosticsFailure = string.Empty;
            deterministicInputPlanCapture = true;
            inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.Running;
            inputPlanCaptureFailure = string.Empty;
            StartExplicitRun(true, M4QaCaptureInputKind.DeterministicInputPlan, stimulusId);
            if (!started)
            {
                recordingReplay = false;
                inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.Failed;
                inputPlanCaptureFailure = "Canonical explicit Start failed: " + status;
                adaptiveQaCommand = null;
                throw new InvalidOperationException(inputPlanCaptureFailure);
            }
            playingAdaptiveQa = true;
            status = "Adaptive QA capture started through canonical Start and fixed-tick Core processing.";
        }
#endif

        public string ExportFrozenReplayEvidenceJson()
        {
            if (ReplayStatus != M4QaReplayStatus.Completed || !entry.EvidenceFrozen)
                throw new InvalidOperationException("A completed frozen replay is required before evidence export.");
            return entry.ExportFrozenEvidenceJson();
        }

        public bool TryGetFrozenCaptureBundle(out M4QaCaptureBundle bundle, out string reason)
        {
            bundle = null;
            if (!recordingReplay || string.IsNullOrWhiteSpace(captureRunId))
            {
                reason = "A same-run capture bundle is available only from a fresh capture run, not a replay.";
                return false;
            }
#if UNITY_EDITOR
            if (deterministicInputPlanCapture && inputPlanCaptureStatus != M4QaInputPlanCaptureStatus.Completed)
            {
                reason = "The deterministic input plan has not completed all commands through a canonical frozen Night 3.";
                return false;
            }
#endif
            if (!Guid.TryParseExact(captureRunId, "N", out _))
            {
                reason = "The capture run identity is invalid.";
                return false;
            }
            if (!string.IsNullOrEmpty(replayCaptureFailure))
            {
                reason = "The captured command tape is invalid: " + replayCaptureFailure;
                return false;
            }
            if (!started || entry == null || !entry.EvidenceFrozen || entry.CompletedNights != 3)
            {
                reason = "The canonical evidence is not frozen after exactly three Nights.";
                return false;
            }
            if (replayTape == null || !replayTape.frozen || replayTape.ticks == null || replayTape.ticks.Length == 0)
            {
                reason = "The original capture tape is unavailable, unfrozen, or empty.";
                return false;
            }
            if (!replayTape.TryValidate(out string tapeReason))
            {
                reason = "The original capture tape is invalid: " + tapeReason;
                return false;
            }
            if (replayTape.runSeed != M4ThreeNightQaSceneEntry.EvidenceRunSeed ||
                replayTape.difficulty != (int)entry.SelectedDifficulty)
            {
                reason = "The capture tape seed or difficulty does not match the active evidence run.";
                return false;
            }

            string evidenceJson;
            string tapeJson;
            M4FrozenEvidence evidence;
            try
            {
                evidenceJson = entry.ExportFrozenEvidenceJson();
                tapeJson = replayTape.ToJson();
                evidence = JsonUtility.FromJson<M4FrozenEvidence>(evidenceJson);
            }
            catch (Exception exception)
            {
                reason = "The frozen Core exports could not be read: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
            if (evidence == null || evidence.EvidenceSchemaVersion != 2 || evidence.RunSeed != replayTape.runSeed ||
                (int)evidence.Difficulty != replayTape.difficulty || !evidence.Frozen || evidence.CompletedNights != 3 ||
                evidence.Nights == null || evidence.Nights.Length != 3 || string.IsNullOrWhiteSpace(evidence.FinalGameplayHash) ||
                evidence.Nights[2] == null || evidence.FinalGameplayHash != evidence.Nights[2].GameplayHash)
            {
                reason = "The frozen evidence identity or final GameplayHash does not match the active capture.";
                return false;
            }
            if (captureRuntimeIdentityAtStart == null)
            {
                reason = "Runtime identity was not captured when this run started.";
                return false;
            }

            bundle = new M4QaCaptureBundle
            {
                CaptureRunId = captureRunId,
                RunSeed = evidence.RunSeed,
                Difficulty = (int)evidence.Difficulty,
                Frozen = evidence.Frozen,
                CompletedNights = evidence.CompletedNights,
                FinalGameplayHash = evidence.FinalGameplayHash,
                ReplayTickCount = replayTape.ticks.Length,
                InputKind = captureInputKind,
                InputPlanId = captureInputKind == M4QaCaptureInputKind.DeterministicInputPlan ? captureInputPlanId : string.Empty,
                FrozenEvidence = new M4QaCapturePayload { CaptureRunId = captureRunId, RawJson = evidenceJson },
                ReplayTape = new M4QaCapturePayload { CaptureRunId = captureRunId, RawJson = tapeJson },
                RuntimeIdentity = captureRuntimeIdentityAtStart.Copy()
            };
            reason = string.Empty;
            return true;
        }

        void Awake()
        {
            entry = GetComponent<M4ThreeNightQaSceneEntry>();
            if (entry == null) throw new InvalidOperationException("M4ThreeNightQaSceneEntry is required.");
            if (qaPartnerMovementProfileAsset != null)
            {
                if (string.IsNullOrWhiteSpace(qaPartnerMovementProfileAsset.text))
                    throw new InvalidOperationException("The explicitly selected QA Partner movement profile is empty.");
                try { partnerMovementProfile = JsonUtility.FromJson<M4PartnerMovementProfile>(qaPartnerMovementProfileAsset.text); }
                catch (ArgumentException exception)
                { throw new InvalidOperationException("The explicitly selected QA Partner movement profile is invalid JSON.", exception); }
                if (partnerMovementProfile == null)
                    throw new InvalidOperationException("The explicitly selected QA Partner movement profile is null.");
            }
            if (qaPartnerExchangePolicyAsset != null)
            {
                partnerExchangePolicy = JsonUtility.FromJson<M4PartnerExchangePolicy>(qaPartnerExchangePolicyAsset.text);
                if (partnerExchangePolicy == null) throw new FormatException("Selected exchange policy is missing.");
                partnerExchangePolicy.Validate();
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    partnerExchangePolicyIdentity = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(qaPartnerExchangePolicyAsset.text))).Replace("-", "");
            }
            BuildRuntimeGraybox();
            if (qaPartnerMovementProfileAsset != null && qaPartnerExchangePolicyAsset != null)
                partnerOrderSelection = new M4PartnerOrderSelection(transform, runtimeGeometry);
        }

        void Update()
        {
            // Observe every E edge before menu/pause/focus early returns. A held
            // down discarded by those domains cannot later appear freshly armed.
            if(qaEnableManualTraversal)
            {
                if(!Input.GetKey(KeyCode.E))manualEArmed=true;
                if(Input.GetKeyDown(KeyCode.E))
                {manualFreshEFrame=manualEArmed?Time.frameCount:-1;manualEArmed=false;}
            }
            if (!started)
            {
                manualFreshEFrame=-1;
                if(qaEnableManualTraversal&&Input.GetKey(KeyCode.E))manualEArmed=false;
                if (Input.GetKeyDown(KeyCode.Return)) startRequested = true;
                return;
            }
            AdvanceReplayLifecycle();
            // Release is observed even during pause/closed phase; it never queues a late order.
            if (partnerQMode != PartnerQMode.None && (!entry.FixedTickRunning || playingReplay))
            {
                DisarmPartnerQ();
                if (!Input.GetKey(KeyCode.Q)) EndPartnerQ(false);
            }
            if (!Input.GetMouseButton(0)) partnerMouse0Blocked = false;
            if (!Input.GetMouseButton(1)) partnerMouse1Blocked = false;
#if UNITY_EDITOR
            AdvanceInputPlanLifecycle();
            #endif
            if (recordingReplay && entry.EvidenceFrozen && replayTape != null && string.IsNullOrEmpty(replayCaptureFailure))
                replayTape.frozen = true;
#if UNITY_EDITOR
            if (deterministicInputPlanCapture) return;
#endif
            if (playingReplay)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) && (entry.FixedTickRunning || entry.OwnerPaused))
            {
                bool pause = !entry.OwnerPaused;
                if (pause) { DiscardExchangeSelection("일시정지 전 미접수 선택 폐기"); partnerExchangeBeginRequested = false; partnerCoverRequested = false; partnerExchangeOpenRequested = false; DisarmPartnerQ(); StopManualTrigger(); ClearManualTraversalLatch(); }
                entry.SetOwnerPause(pause);
                if (partnerQMode != PartnerQMode.None && !Input.GetKey(KeyCode.Q)) EndPartnerQ(false);
                Cursor.lockState = pause ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = pause;
                return;
            }
            if (!entry.FixedTickRunning)
            {
                if(qaEnableManualTraversal&&!entry.OwnerPaused)CloseManualTraversalDomain("canonical fixed-tick domain ended");
                StopManualTrigger();
                DiscardExchangeSelection("진행 종료로 미접수 선택 폐기");
                partnerCoverRequested = false; partnerExchangeBeginRequested = false;
                return;
            }

            if(qaEnableManualTraversal&&!manualFocused){StopManualTrigger();return;}

            if (partnerExchangeMenuOpen)
            {
                StopManualTrigger();
                CapturePartnerMouse();
                if (!Input.GetKey(KeyCode.Q)) EndPartnerQ(false);
                return; // Menu clicks never become player-fired bullets or camera input.
            }

            if(qaEnableManualTraversal&&manualTraversal!=null)
            {
                if(Input.GetKeyDown(KeyCode.E))
                {
                    var admission=ReadManualEntryReadiness(ManualEntryStage.Enqueue);
                    if(admission.Eligible)manualERequested=true;
                    else manualTraversalReceipt="E: "+admission.Reason;
                }
                if(manualERequested||manualTraversal.Snapshot.State!=TraversalState.None)
                {StopManualTrigger();UpdateManualLook();return;}
            }
            UpdatePlayerMovementAndLook();
            UpdateAimIndicator();
            if (Input.GetKeyDown(KeyCode.Q) && partnerQMode == PartnerQMode.None) BeginPartnerQ();
            if (partnerQMode != PartnerQMode.None)
            {
                CapturePartnerMouse();
                StopManualTrigger();
                playerCamera.fieldOfView = 70f;
                RefreshPartnerQPreview();
                if (!Input.GetKey(KeyCode.Q)) EndPartnerQ(true);
                suppressMenuShotThroughFrame = Time.frameCount;
            }
            else partnerQPreview = DescribePartnerQContext();
            if (Input.GetMouseButtonUp(0))
            {
                arTriggerHeld = false; triggerReleased = true;
                if (currentWeapon == GrayboxWeapon.AssaultRifle) shotRequested = false;
            }
            if (Input.GetMouseButtonDown(0) && !partnerMouse0Blocked && Time.frameCount > suppressMenuShotThroughFrame && !partnerExchangeMenuOpen && !PartnerExchangeUiOwnsPointer())
            {
                shotRequested = true;
                if (currentWeapon == GrayboxWeapon.AssaultRifle)
                { arTriggerHeld = true; arFirstShotRequested = true; }
            }
            if (Input.GetKeyDown(KeyCode.R)) reloadRequested = true;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectWeapon(GrayboxWeapon.ServicePistol);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectWeapon(GrayboxWeapon.AssaultRifle);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectWeapon(GrayboxWeapon.WatchRifle);
            if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
        }

        void OnGUI()
        {
            const float width = 470f;
            if (started && !entry.FixedTickRunning && (entry.CanEnterNextNight || entry.EvidenceFrozen))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            GUILayout.BeginArea(new Rect(16f, 16f, width, 360f), GUI.skin.box);
            qaPanelScroll = GUILayout.BeginScrollView(qaPanelScroll);
            GUILayout.Label(M4ThreeNightQaSceneEntry.EntryLabel + " — Night " + entry.ActiveNightOrdinal + " / 3 graybox");
            GUILayout.Label("RunSeed 1234 | " + entry.SelectedDifficulty);
            if(qaEnableManualTraversal)
            {
                GUILayout.Label(manualTraversalReceipt);
                GUILayout.Label("접근 통로에서 E 한 번: 자동 하강 약1s / 상승6s. 키를 놓아도 계속 이동합니다.");
                GUILayout.Label("Big-damage input UNAVAILABLE | ground AI/direct damage/body collision not accepted.");
            }
            GUILayout.Label("Entry=" + entry.Started + " | Fixed ticks=" + entry.FixedTickRunning + " | Cursor=" + Cursor.lockState);
            GUILayout.Label("Completed Nights=" + entry.CompletedNights + " | Stage=" + entry.CurrentStage);
            GUILayout.Label("Status: " + status);
            if (started)
            {
                GUILayout.Label(partnerQPreview);
                GUILayout.Label(partnerRequestReceipt);
                GUILayout.Label(partnerMovement == null ? "Partner movement UNAVAILABLE: authoring not bound." : partnerMovement.Receipt);
                if (partnerMovement != null) GUILayout.Label(partnerMovement.LatestRecoveryReceipt);
                if (partnerMovement != null && GUILayout.Button("Copy Partner movement diagnostics (read-only)"))
                {
                    GUIUtility.systemCopyBuffer = ExportPartnerDiagnostics(entry.CanonicalSessionForPlayerAction);
                    status = "Partner movement and exchange selection/actual transfer evidence copied.";
                }
                bool effectivePositionInput = playingReplay;
#if UNITY_EDITOR
                effectivePositionInput |= deterministicInputPlanCapture || playingInputPlan || playingAdaptiveQa;
#endif
                GUILayout.Label(effectivePositionInput
                    ? "Upper origin boundary: NOT_CHECKED for effective replay/plan input."
                    : manualUpperOriginReceipt);
            }
            if (!string.IsNullOrEmpty(replayCaptureFailure))
                GUILayout.Label((replayCaptureStoppedByManualPartner ? "REPLAY RECORDING STOPPED: " : "REPLAY CAPTURE INVALID: ") + replayCaptureFailure);
            if (!started)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Normal")) entry.SelectDifficulty(LastWatchDifficulty.Normal);
                if (GUILayout.Button("Hard")) entry.SelectDifficulty(LastWatchDifficulty.Hard);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Explicit Start (Enter)")) startRequested = true;
                if (GUILayout.Button("Replay input tape from clipboard")) StartReplayFromClipboard();
                GUILayout.Label("WASD/Shift move, Mouse look, LMB fire, RMB aim, 1/2/3 weapon, R reload, Q CoverFace.");
                if (partnerMovement != null && GUILayout.Button("Copy movement authoring facts (read-only)"))
                {
                    GUIUtility.systemCopyBuffer = partnerMovement.ExportAuthoringFacts();
                    status = "Actual capsule/builder collider facts copied (read-only).";
                }
            }
            else
            {
                GUILayout.Label("Selected: " + WeaponName(currentWeapon) + (IsReloadActive ? "  | Reloading " + WeaponName(reloadingWeapon) : string.Empty));
                GUILayout.Label("1 Pistol " + servicePistolMagazine + "/12 (∞)  |  2 AR " + assaultRifleMagazine + "/" + assaultRifleReserve + "  |  3 Watch " + watchRifleMagazine + "/" + watchRifleReserve);
                GUILayout.Label("M3 active " + actors.Count);
                GUILayout.Label("AR hold=" + arTriggerHeld + " | active FS=" + activeFireSequence + " | last emitted FS=" + (nextFireSequence - 1) + " | next FS=" + nextFireSequence);
                if (recordingReplay) GUILayout.Label("Recording replay commands: " + (replayTape == null ? 0 : replayTape.ticks.Length));
                if (playingReplay && replayConsumer != null) GUILayout.Label("Replay tick " + replayConsumer.Cursor + "/" + replayConsumer.Count);
                GUILayout.Label("1/2/3 select weapon; R reloads selected weapon.");
                if (entry.CanEnterNextNight)
                {
                    GUILayout.Label("Canonical Night " + entry.CompletedNights + " is complete. Dawn/Scavenge/Allocation are excluded in this QA slice.");
                    if (GUILayout.Button("Enter Night " + entry.ActiveNightOrdinal + " (canonical)")) EnterNextNight();
                }
                else if (entry.EvidenceFrozen)
                {
                    GUILayout.Label("All three canonical Nights are complete; evidence is frozen.");
                    if (GUILayout.Button("Copy frozen 3-Night evidence JSON")) CopyFrozenEvidence();
                    if (recordingReplay && GUILayout.Button("Copy frozen replay input JSON")) CopyReplayTape();
                }
                else GUILayout.Label("LMB fires selected weapon; only player-fired enemy damage is enabled in this bounded graybox.");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (started) DrawCombatReadability();
            if(started&&qaEnableManualTraversal)DrawManualTraversalCue();
            if (started)
            {
                DrawPartnerExchangeMenu();
                if (partnerQMode == PartnerQMode.Face)
                {
                    partnerOrderSelection?.DrawLabels(playerCamera);
                    GUI.Box(new Rect(Screen.width * .5f - 240f, Screen.height * .5f + 32f, 480f, 52f),
                        "명령 선택영역 / 영역 사이 빈 공간은 선택 없음\n" + partnerQPreview);
                }
            }

            if (!started && startRequested)
            {
                startRequested = false;
                StartExplicitRun();
            }
        }

        void AdvanceReplayLifecycle()
        {
            if (playingReplay && entry.EvidenceFrozen)
            {
                playingReplay = false;
                string completionReason = "Replay consumer is unavailable.";
                if (replayConsumer != null && replayConsumer.TryComplete(true, out completionReason))
                {
                    ReplayStatus = M4QaReplayStatus.Completed;
                    status = "REPLAY COMPLETE: all frozen three-Night commands consumed; compare the frozen evidence JSON/hash with the original capture.";
                }
                else FailReplay(completionReason);
            }
            if (playingReplay && entry.CanEnterNextNight && replayConsumer != null && !replayConsumer.IsExhausted)
                EnterNextNight();
            else if (playingReplay && entry.CanEnterNextNight && (replayConsumer == null || replayConsumer.IsExhausted))
                FailReplay("Tape ended after Night " + entry.CompletedNights + "; three-Night replay is incomplete.");
        }

#if UNITY_EDITOR
        void AdvanceInputPlanLifecycle()
        {
            if ((!playingInputPlan && !playingAdaptiveQa) || inputPlanCaptureStatus != M4QaInputPlanCaptureStatus.Running) return;
            if (entry.EvidenceFrozen)
            {
                playingInputPlan = false;
                playingAdaptiveQa = false;
                adaptiveQaCommand = null;
                if (!deterministicInputPlanCapture)
                {
                    FailInputPlanCapture("QA capture mode is unavailable at canonical Night-3 freeze.");
                    return;
                }
                if (inputPlanConsumer == null && adaptiveQaConsumedTicks == 0)
                {
                    FailInputPlanCapture("Input-plan command consumer is unavailable at canonical Night-3 freeze.");
                    return;
                }
                if (inputPlanConsumer != null && !inputPlanConsumer.IsExhausted)
                {
                    FailInputPlanCapture("Canonical Night 3 froze with " + (inputPlanConsumer.Count - inputPlanConsumer.Cursor) + " unconsumed input-plan commands.");
                    return;
                }
                int expectedCount = inputPlanConsumer != null ? inputPlanConsumer.Count : adaptiveQaConsumedTicks;
                if (!recordingReplay || !string.IsNullOrEmpty(replayCaptureFailure) || replayTape == null || replayTape.ticks == null || replayTape.ticks.Length != expectedCount)
                {
                    FailInputPlanCapture("The actual-command tape is incomplete or invalid at canonical Night-3 freeze.");
                    return;
                }
                inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.Completed;
                status = "INPUT PLAN CAPTURE COMPLETE: all planned commands were applied and recorded through canonical Night 3.";
                return;
            }

            if (entry.CanEnterNextNight)
            {
                if (!playingAdaptiveQa && (inputPlanConsumer == null || inputPlanConsumer.IsExhausted))
                {
                    FailInputPlanCapture("Input plan ended after Night " + entry.CompletedNights + "; canonical next-Night entry was not invoked.");
                    return;
                }
                EnterNextNight();
            }
        }

        void FailInputPlanCapture(string reason)
        {
            playingInputPlan = false;
            playingAdaptiveQa = false;
            adaptiveQaCommand = null;
            recordingReplay = false;
            replayCaptureFailure = reason;
            inputPlanCaptureFailure = reason;
            inputPlanCaptureStatus = M4QaInputPlanCaptureStatus.Failed;
            status = "QA INPUT PLAN CAPTURE INVALID: " + reason + ". Discard this capture; no bundle is available.";
            entry.HaltFixedTicksForQaInputFailure(reason);
            Debug.LogError("[LastWatch M4] " + status, this);
        }
#endif

        void StartExplicitRun(bool beginRecording = true,
            M4QaCaptureInputKind inputKind = M4QaCaptureInputKind.OwnerLiveInput, string inputPlanId = null)
        {
            // Fixture input is consumed only at fresh session creation. Never grant
            // on menu entry, Night transition or restoration of an existing session.
            M4PlayerResourceInventory initialResources = null;
            int initialPartnerParts = 0;
            if (qaPlayerPartsFixtureAsset != null)
            {
                try
                {
                    string fixtureText = qaPlayerPartsFixtureAsset.text;
                    LastWatch.Core.Persistence.M4StrictJson.Validate(fixtureText);
                    var fixture = JsonUtility.FromJson<PlayerPartsQaFixture>(fixtureText);
                    if (fixture == null || fixture.fixtureId != "LW-M4-PARTS-QA-20261022-R1" ||
                        fixture.playerParts != 10 || fixture.partnerParts != 10)
                        throw new FormatException("Selected QA fixture requires explicit Player10/Partner10 and the approved fixture ID.");
                    string textHash;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        textHash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fixtureText))).Replace("-", "");
                    initialResources = new M4PlayerResourceInventory { parts = fixture.playerParts,
                        sourceReceiptId = fixture.fixtureId + "/UTF8_TEXT_SHA256/" + textHash };
                    initialResources.Validate();
                    initialPartnerParts = fixture.partnerParts;
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    status = "Explicit Start rejected: invalid selected Parts QA fixture: " + error.Message;
                    Debug.LogError("[LastWatch M4] " + status, this);
                    return;
                }
            }
            ResetExchangeFeedback(null);
            string nextCaptureRunId = beginRecording ? Guid.NewGuid().ToString("N") : string.Empty;
            M4QaCaptureRuntimeIdentity nextCaptureRuntimeIdentity = beginRecording
                ? M4QaCaptureRuntimeIdentity.CaptureFromRuntime()
                : null;
            captureRunId = string.Empty;
            captureRuntimeIdentityAtStart = null;
            var metadata = new M4RunMetadata(new RunId(QaRunValue), M4ThreeNightQaSceneEntry.EvidenceRunSeed, entry.SelectedDifficulty);
            var topology = new M4TopologySnapshot(M4BridgeProfile.ContentVersion, 0,
                new[] { FaceId.FaceA, FaceId.FaceB, FaceId.FaceC }, BuildBindings());
            var partner = new PartnerRuntimeState
            {
                Role = PartnerRole.CoverFace,
                DesignatedFaceId = "A",
                CurrentNodeId = "FaceA.Cover",
                HitPoints = 100f,
                Life = PartnerLifeState.Healthy,
                Parts = initialPartnerParts
            };
            if (authoredInitialPartnerNode != null) partner.CurrentNodeId = authoredInitialPartnerNode;
            lastPartnerProtectedShotSequence = 0; lastPartnerProtectedShotTick = -1;
            M4CommandResult result = entry.StartExplicit(metadata, topology, BuildSetup(), partner, null,
                CapturePlayerWeaponActionSaveState(0), initialResources);
            started = result.Status == M4CommandStatus.Committed || result.Status == M4CommandStatus.AlreadyCommitted;
            status = started ? "Night 1 is running. Clear real scheduled tickets to complete." : result.Status + ": " + result.Reason;
            if (!started) Debug.LogError("[LastWatch M4] Explicit Start rejected: " + status, this);
            if (started)
            {
                recordingReplay = beginRecording;
                playingReplay = !beginRecording;
                replayCaptureFailure = null;
                if (beginRecording)
                {
                    captureRunId = nextCaptureRunId;
                    captureRuntimeIdentityAtStart = nextCaptureRuntimeIdentity;
                    captureInputKind = inputKind;
                    captureInputPlanId = inputKind == M4QaCaptureInputKind.DeterministicInputPlan ? inputPlanId ?? string.Empty : string.Empty;
                    replayTape = new M4ReplayTape(metadata.RunSeed, (int)metadata.Difficulty);
                }
                replayConsumer = beginRecording ? null : new M4ReplayConsumer(replayTape);
                if (!beginRecording)
                {
                    ReplayStatus = M4QaReplayStatus.Running;
                    replayFailure = string.Empty;
                    ReplayPriorStatus = string.Empty;
                    ReplayBlockedResolvedEnemyShots = 0;
                    ReplayBlockedWorldShots = 0;
                    replayShotDiagnostics.Reset();
                    replayShotDiagnosticsFailure = string.Empty;
                }
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void StartReplayFromClipboard()
        {
            if (!M4ReplayTape.TryParse(GUIUtility.systemCopyBuffer, out M4ReplayTape tape, out string reason))
            {
                status = "Replay rejected: " + reason;
                return;
            }
            try { BeginReplay(tape); }
            catch (Exception exception) { status = "Replay rejected: " + exception.Message; }
        }

        void EnterNextNight()
        {
            M4CommandResult result = entry.EnterNextCanonicalDefense();
            started = result.Status == M4CommandStatus.Committed || result.Status == M4CommandStatus.AlreadyCommitted;
            status = started
                ? "Night " + entry.ActiveNightOrdinal + " is running with the preceding committed allocation and continuing player state."
                : result.Status + ": " + result.Reason;
            if (started)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (playingReplay) FailReplay("Next canonical Night rejected: " + status);
#if UNITY_EDITOR
            else if (playingInputPlan) FailInputPlanCapture("Next canonical Night rejected: " + status);
#endif
            else Debug.LogError("[LastWatch M4] Next canonical Night rejected: " + status, this);
        }

        void CopyFrozenEvidence()
        {
            string json = entry.ExportFrozenEvidenceJson();
            GUIUtility.systemCopyBuffer = json;
            status = "Frozen Night-3 evidence JSON copied to clipboard. Save it unchanged with the QA run artifacts.";
        }

        void CopyReplayTape()
        {
            if (!string.IsNullOrEmpty(replayCaptureFailure))
            {
                status = "Replay capture is invalid and cannot be exported. Repeat the capture before replay validation.";
                return;
            }
            if (replayTape == null || !entry.EvidenceFrozen)
            {
                status = "Replay tape is available only after the recorded run freezes at Night 3.";
                return;
            }
            replayTape.frozen = true;
            string json = replayTape.ToJson();
            GUIUtility.systemCopyBuffer = json;
            status = "Frozen input tape copied (" + replayTape.ticks.Length + " ticks, SHA-256 " + replayTape.ComputeSha256() + "). Save unchanged.";
        }

        void IM4ThreeNightQaFixedTickProcessor.ProcessCompletedCoreTick(M4ThreeNightQaFixedTickContext context)
        {
            if (!started || context == null) return;
            // Invalidate prior metadata until this processor actually completes. This
            // does not freeze poses; consumers must query before Update/Night transitions.
            upperLaneCompletedRunTick = -1;
            upperLaneCompletedNightTick = -1;
            upperLaneCompletedNightOrdinal = 0;
            bool replayFireCommand = false;
            int replayConsumedTick = 0;
            if (playingReplay)
            {
                if (replayConsumer == null)
                {
                    FailReplay("Replay consumer is unavailable.");
                    return;
                }
                M4ReplayConsumeStatus consumeStatus = replayConsumer.TryConsume(context.RunFixedTick, entry.ActiveNightOrdinal, out M4ReplayTick command);
                if (consumeStatus != M4ReplayConsumeStatus.Matched)
                {
                    FailReplay("Command " + replayConsumer.Cursor + " was not consumed: " + consumeStatus + " (actual run tick " + context.RunFixedTick + ", Night " + entry.ActiveNightOrdinal + ").");
                    return;
                }
                ApplyReplayCommand(command);
                replayFireCommand = command.firePressed;
                replayConsumedTick = replayConsumer.Cursor;
            }
#if UNITY_EDITOR
            else if (playingAdaptiveQa)
            {
                if (adaptiveQaCommand == null)
                {
                    FailInputPlanCapture("Adaptive QA command callback is unavailable.");
                    return;
                }
                M4ReplayTick command;
                try
                {
                    command = adaptiveQaCommand(CreateAdaptiveQaSnapshot(context));
                }
                catch (Exception exception)
                {
                    FailInputPlanCapture("Adaptive QA command callback failed at runFixedTick=" + context.RunFixedTick +
                        ": " + exception.GetType().Name + ": " + exception.Message);
                    return;
                }
                if (!M4ReplayTape.TryValidateTick(command, out string invalidReason))
                {
                    FailInputPlanCapture("Adaptive QA command is missing, invalid, or mismatched at runFixedTick=" +
                        context.RunFixedTick + ", Night " + entry.ActiveNightOrdinal + ": " + invalidReason);
                    return;
                }
                if (command.runFixedTick != context.RunFixedTick || command.nightOrdinal != entry.ActiveNightOrdinal)
                {
                    FailInputPlanCapture("Adaptive QA command expected runFixedTick=" + context.RunFixedTick +
                        ", Night " + entry.ActiveNightOrdinal + " but received runFixedTick=" +
                        command.runFixedTick + ", Night " + command.nightOrdinal + ".");
                    return;
                }
                M4ReplayTick detachedCommand = command.DetachedCopy();
                ApplyReplayCommand(detachedCommand);
                replayFireCommand = detachedCommand.firePressed;
                adaptiveQaConsumedTicks++;
                replayConsumedTick = adaptiveQaConsumedTicks;
                TryRecordReplayTick(context.RunFixedTick, entry.ActiveNightOrdinal);
                if (!recordingReplay)
                {
                    FailInputPlanCapture("The applied adaptive command could not be recorded: " +
                        (replayCaptureFailure ?? "unknown recorder failure"));
                    return;
                }
            }
            else if (playingInputPlan)
            {
                if (inputPlanConsumer == null)
                {
                    FailInputPlanCapture("Input-plan command consumer is unavailable.");
                    return;
                }
                M4ReplayConsumeStatus consumeStatus = inputPlanConsumer.TryConsume(context.RunFixedTick, entry.ActiveNightOrdinal, out M4ReplayTick command);
                if (consumeStatus != M4ReplayConsumeStatus.Matched)
                {
                    FailInputPlanCapture("Input-plan command " + inputPlanConsumer.Cursor + " was not consumed: " + consumeStatus +
                        " (actual run tick " + context.RunFixedTick + ", Night " + entry.ActiveNightOrdinal + ").");
                    return;
                }
                ApplyReplayCommand(command);
                replayFireCommand = command.firePressed;
                replayConsumedTick = inputPlanConsumer.Cursor;
                TryRecordReplayTick(context.RunFixedTick, entry.ActiveNightOrdinal);
                if (!recordingReplay)
                {
                    if (replayFireCommand)
                        RecordReplayShotNoRaycast(replayConsumedTick, context.RunFixedTick,
                            currentWeapon.ToString(), M4ReplayShotNoRaycastReason.ResolverDidNotRun);
                    FailInputPlanCapture("The applied command could not be recorded: " + (replayCaptureFailure ?? "unknown recorder failure"));
                    return;
                }
            }
#endif
            else
            {
                if(qaEnableManualTraversal)ProcessManualTraversal(context);
                if (currentWeapon == GrayboxWeapon.AssaultRifle) shotRequested = arFirstShotRequested || arTriggerHeld;
                arFirstShotRequested = false; // Initial click is tried once, never queued beyond a blocked tick.
                if (recordingReplay) TryRecordReplayTick(context.RunFixedTick, entry.ActiveNightOrdinal);
            }
            ApplyTriggerBoundary();
            triggerReleased = false;
            ProcessManualPartnerCoverRequest(context);
            if (reloadCancellationPending)
            {
                reloadShotBlockedThroughRunTick = Math.Max(reloadShotBlockedThroughRunTick, context.RunFixedTick);
                reloadCancellationPending = false;
            }
            if (context.Run.PlayerLife == PlayerLifeState.Dead)
            {
                StopManualTrigger();
                activeFireSequence = 0;
                M4PartnerMovementPersistence.Cancel(entry.CanonicalSessionForPlayerAction.RuntimePartner,
                    context.RunFixedTick, "QA manual Player-life input domain ended");
                ProcessPartnerExchange(context);
                if (replayFireCommand)
                    RecordReplayShotNoRaycast(replayConsumedTick, context.RunFixedTick,
                        currentWeapon.ToString(), M4ReplayShotNoRaycastReason.PlayerDead);
                shotRequested = false;
                reloadRequested = false;
                PersistPlayerWeaponActionState(checked(context.RunFixedTick + 1));
                return;
            }
            CompleteReloadIfDue(context.RunFixedTick);
            ProcessPartnerExchange(context);
            ActivateScheduledTicket(context);
            ResolveRequestedPlayerShot(context, replayFireCommand, replayConsumedTick);
            AdvanceVisibleActors(context);
            AdvanceClimberStructureAttacks(context);
            if (partnerMovement != null)
            {
                M4Session currentSession = entry.CanonicalSessionForPlayerAction;
                partnerMovement.Tick(currentSession, context.RunFixedTick, CapturePartnerProtectedLane(context.RunFixedTick));
            }
            PersistPlayerWeaponActionState(checked(context.RunFixedTick + 1));
            upperLaneCompletedRunTick = context.RunFixedTick;
            upperLaneCompletedNightTick = context.NightFixedTick;
            upperLaneCompletedNightOrdinal = entry.ActiveNightOrdinal;
        }

        void IM4ThreeNightQaFixedTickProcessor.RestorePlayerWeaponActionStateFromSession(M4Session session)
        {
            RestorePlayerWeaponActionStateFromSession(session);
            started = true;
            status = "Saved player weapon/action state restored; Core ticks remain stopped because actor continuation is unavailable.";
        }

        public M4PlayerWeaponActionSaveState CapturePlayerWeaponActionSaveState(int nextRunFixedTick)
        {
            if (nextRunFixedTick < 0) throw new ArgumentOutOfRangeException(nameof(nextRunFixedTick));
            var state = new M4PlayerWeaponActionSaveState
            {
                selectedWeapon = (M4PlayerWeaponId)(int)currentWeapon,
                nextFireSequence = nextFireSequence,
                activeFireSequence = activeFireSequence,
                triggerHeld = arTriggerHeld,
                lastShotAgeTicks = lastPlayerShotRunTick < 0 ? -1 : Math.Max(0, nextRunFixedTick - lastPlayerShotRunTick),
                servicePistolMagazine = servicePistolMagazine,
                servicePistolReserve = int.MaxValue,
                assaultRifleMagazine = assaultRifleMagazine,
                assaultRifleReserve = assaultRifleReserve,
                watchRifleMagazine = watchRifleMagazine,
                watchRifleReserve = watchRifleReserve,
                reloadStatus = (M4PlayerReloadStatus)(int)reloadTransactionStatus,
                reloadingWeapon = (M4PlayerWeaponId)(int)reloadingWeapon,
                nextReloadTransactionId = nextReloadTransactionId,
                activeReloadTransactionId = activeReloadTransactionId,
                reloadRemainingTicks = IsReloadActive ? Math.Max(0, reloadCompleteRunTick - nextRunFixedTick + 1) : 0,
                reloadShotBlockedRemainingTicks = Math.Max(reloadCancellationPending ? 1 : 0,
                    Math.Max(0, reloadShotBlockedThroughRunTick - nextRunFixedTick + 1)),
                fireCooldownRemainingTicks = new[]
                {
                    Math.Max(0, pistolNextLegalShotRunTick - nextRunFixedTick),
                    Math.Max(0, assaultNextLegalShotRunTick - nextRunFixedTick),
                    Math.Max(0, watchNextLegalShotRunTick - nextRunFixedTick)
                }
            };
            state.Validate();
            return state;
        }

        public void RestorePlayerWeaponActionStateFromSession(M4Session session)
        {
            if(manualTraversalUsed)throw new InvalidOperationException("Manual traversal actor/route restore is unavailable; start a fresh QA run.");
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!ReferenceEquals(session, entry.CanonicalSessionForPlayerAction))
                throw new ArgumentException("Restore must use the currently bound canonical M4 session.", nameof(session));
            ResetExchangeFeedback(session.Metadata.RunId.ToString());
            M4PlayerWeaponActionSaveState state = session.ReadPlayerWeaponActionState();
            if (state == null) throw new InvalidOperationException("The saved M4 session has no player weapon/action state.");
            RestorePlayerWeaponActionSaveState(state, session.ReadRun().FixedTick);
        }

        public void RestorePlayerWeaponActionSaveState(M4PlayerWeaponActionSaveState state, int runFixedTick)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (runFixedTick < 0) throw new ArgumentOutOfRangeException(nameof(runFixedTick));
            state.Validate();
            if (state.lastShotAgeTicks > runFixedTick)
                throw new FormatException("Saved last-shot age exceeds the current run clock.");
            if (state.reloadStatus == M4PlayerReloadStatus.Active &&
                (state.selectedWeapon != state.reloadingWeapon ||
                    (state.reloadingWeapon == M4PlayerWeaponId.ServicePistol
                        ? state.servicePistolMagazine >= ServicePistolMagazineCapacity
                        : state.reloadingWeapon == M4PlayerWeaponId.AssaultRifle
                            ? state.assaultRifleMagazine >= AssaultRifleMagazineCapacity || state.assaultRifleReserve <= 0
                            : state.watchRifleMagazine >= WatchRifleMagazineCapacity || state.watchRifleReserve <= 0)))
                throw new FormatException("Saved active reload does not match current weapon eligibility.");

            currentWeapon = (GrayboxWeapon)(int)state.selectedWeapon;
            nextFireSequence = state.nextFireSequence;
            lastPlayerShotRunTick = state.lastShotAgeTicks < 0 ? -1 : checked(runFixedTick - state.lastShotAgeTicks);
            activeFireSequence = 0; // A saved device hold never synthesizes a new live trigger action.
            StopManualTrigger();
            servicePistolMagazine = state.servicePistolMagazine;
            assaultRifleMagazine = state.assaultRifleMagazine;
            assaultRifleReserve = state.assaultRifleReserve;
            watchRifleMagazine = state.watchRifleMagazine;
            watchRifleReserve = state.watchRifleReserve;
            reloadTransactionStatus = (ReloadTransactionStatus)(int)state.reloadStatus;
            reloadingWeapon = (GrayboxWeapon)(int)state.reloadingWeapon;
            nextReloadTransactionId = state.nextReloadTransactionId;
            activeReloadTransactionId = state.activeReloadTransactionId;
            reloadCompleteRunTick = IsReloadActive ? checked(runFixedTick + state.reloadRemainingTicks - 1) : -1;
            reloadShotBlockedThroughRunTick = state.reloadShotBlockedRemainingTicks == 0
                ? runFixedTick - 1
                : checked(runFixedTick + state.reloadShotBlockedRemainingTicks - 1);
            pistolNextLegalShotRunTick = checked(runFixedTick + state.fireCooldownRemainingTicks[0]);
            assaultNextLegalShotRunTick = checked(runFixedTick + state.fireCooldownRemainingTicks[1]);
            watchNextLegalShotRunTick = checked(runFixedTick + state.fireCooldownRemainingTicks[2]);
            reloadCancellationPending = false;
            shotRequested = false;
            reloadRequested = false;
            status = IsReloadActive ? "Reload restored: " + WeaponName(reloadingWeapon) + "." :
                "Player weapon/action state restored.";
        }

        void PersistPlayerWeaponActionState(int nextRunFixedTick)
        {
            M4Session session = entry.CanonicalSessionForPlayerAction;
            if (session != null)
                session.RecordPlayerWeaponActionState(CapturePlayerWeaponActionSaveState(nextRunFixedTick));
        }

        void TryRecordReplayTick(int runFixedTick, int nightOrdinal)
        {
            try
            {
                if (replayTape == null)
                {
                    InvalidateReplayCapture("runFixedTick=" + runFixedTick + ", nightOrdinal=" + nightOrdinal + ": replay tape instance is unavailable.");
                    return;
                }
                M4ReplayTick capturedTick = M4ReplayTick.Capture(runFixedTick, nightOrdinal,
                    playerTransform.position, playerTransform.eulerAngles.y, cameraPitchDegrees,
                    (int)currentWeapon, shotRequested, reloadRequested, arTriggerHeld, triggerReleased);
                if (!replayTape.TryAppend(capturedTick, out string reason))
                    InvalidateReplayCapture(reason);
            }
            catch (Exception exception)
            {
                // Replay recording is a QA sidecar; recorder failures must not escape through
                // the canonical gameplay fixed-tick processor.
                InvalidateReplayCapture("runFixedTick=" + runFixedTick + ", nightOrdinal=" + nightOrdinal + ": recorder exception " + exception.GetType().Name + ": " + exception.Message);
            }
        }

#if UNITY_EDITOR
        M4QaAdaptiveSnapshot CreateAdaptiveQaSnapshot(M4ThreeNightQaFixedTickContext context)
        {
            EnemyDirectorRuntimeSnapshot director = context.Director.Snapshot();
            var tickets = new M4QaTicketSnapshot[director.Tickets.Length];
            for (int i = 0; i < tickets.Length; i++)
            {
                SpawnTicketSnapshot ticket = director.Tickets[i];
                ulong? entityId = ticket.ActiveEntityId.HasValue ? ticket.ActiveEntityId.Value.Value : (ulong?)null;
                ActiveEnemyActor actor = null;
                if (entityId.HasValue) actors.TryGetValue(entityId.Value, out actor);
                bool present = actor != null && actor.TicketId == ticket.Ticket.TicketId;
                Collider collider = present ? actor.GetComponent<Collider>() : null;
                tickets[i] = new M4QaTicketSnapshot(ticket.Ticket.TicketId, ticket.Lifecycle,
                    (int)ticket.Ticket.Face, entityId, ticket.MovementState, present,
                    present ? actor.transform.position : Vector3.zero,
                    collider != null ? collider.bounds.center : present ? actor.transform.position : Vector3.zero);
            }
            var weapons = new[]
            {
                new M4QaWeaponSnapshot(0, servicePistolMagazine, -1, pistolNextLegalShotRunTick),
                new M4QaWeaponSnapshot(1, assaultRifleMagazine, assaultRifleReserve, assaultNextLegalShotRunTick),
                new M4QaWeaponSnapshot(2, watchRifleMagazine, watchRifleReserve, watchNextLegalShotRunTick)
            };
            return new M4QaAdaptiveSnapshot(context.RunFixedTick, entry.ActiveNightOrdinal,
                entry.CurrentStage, entry.CompletedNights, playerTransform.position,
                playerCamera.transform.position, playerTransform.eulerAngles.y, cameraPitchDegrees,
                (int)currentWeapon, context.Run.PlayerLife == PlayerLifeState.Dead,
                IsReloadActive, reloadCompleteRunTick, reloadShotBlockedThroughRunTick, weapons, tickets);
        }
#endif

        void InvalidateReplayCapture(string reason, bool expectedManualInput = false)
        {
            recordingReplay = false;
            replayCaptureFailure = reason;
            replayCaptureStoppedByManualPartner = expectedManualInput;
            if (expectedManualInput)
            {
                status = "Partner interaction accepted; replay recording stopped because this tape cannot represent role input. Manual play continues.";
                Debug.LogWarning("[LastWatch M4] " + status, this);
                return;
            }
            status = "REPLAY CAPTURE INVALID: " + reason + ". Combat continues; discard this tape and repeat the capture.";
            Debug.LogError("[LastWatch M4] " + status, this);
        }

        void ApplyReplayCommand(M4ReplayTick command)
        {
            playerTransform.position = command.Position;
            playerTransform.rotation = Quaternion.Euler(0f, command.bodyYaw, 0f);
            cameraPitchDegrees = command.cameraPitch;
            playerCamera.transform.localRotation = Quaternion.Euler(cameraPitchDegrees, 0f, 0f);
            SelectWeapon((GrayboxWeapon)command.weapon);
            shotRequested = command.firePressed;
            arTriggerHeld = command.triggerHeld;
            triggerReleased = command.triggerReleased;
            reloadRequested = command.reloadPressed;
        }

        void FailReplay(string reason)
        {
            playingReplay = false;
            ReplayStatus = M4QaReplayStatus.Diverged;
            replayFailure = reason;
            ReplayPriorStatus = status;
            entry.HaltFixedTicksForReplayFailure();
            status = "REPLAY DIVERGED: " + reason;
            Debug.LogError("[LastWatch M4] " + status, this);
        }

        void ActivateScheduledTicket(M4ThreeNightQaFixedTickContext context)
        {
            if (!EnemyIncarnationLifecycle.TryActivateNext(context.Director, context.Pool, context.Run, context.NightFixedTick,
                out ActiveEnemyIncarnation incarnation)) return;
            ActiveEnemyActor created = CreateEnemyActor(incarnation, FindTicketFace(context.Director, incarnation.TicketId));
            created.CanonicalActivationRunTick = context.RunFixedTick;
            actors.Add(incarnation.EntityId.Value, created);
            status = "Activated " + incarnation.Archetype + " ticket " + incarnation.TicketId + " on " + incarnation.EntityId + ".";
        }

        void ResolveRequestedPlayerShot(M4ThreeNightQaFixedTickContext context, bool replayFireCommand,
            int replayConsumedTick)
        {
            if (reloadRequested)
            {
                reloadRequested = false;
                if (!IsReloadActive && Magazine(currentWeapon) < MagazineCapacity(currentWeapon) &&
                    (currentWeapon == GrayboxWeapon.ServicePistol || Reserve(currentWeapon) > 0))
                {
                    StartReloadTransaction(currentWeapon, context.RunFixedTick);
                    status = "Reload started: " + WeaponName(reloadingWeapon) + ".";
                }
            }
            if (!shotRequested)
            {
                if (replayFireCommand)
                    RecordReplayShotNoRaycast(replayConsumedTick, context.RunFixedTick,
                        currentWeapon.ToString(), M4ReplayShotNoRaycastReason.ResolverDidNotRun);
                return;
            }
            shotRequested = false;
            if (IsReloadActive || context.RunFixedTick <= reloadShotBlockedThroughRunTick ||
                context.RunFixedTick < NextLegalShotRunTick(currentWeapon) || Magazine(currentWeapon) <= 0)
            {
                if (replayFireCommand)
                {
                    M4ReplayShotNoRaycastReason reason = Magazine(currentWeapon) <= 0
                        ? M4ReplayShotNoRaycastReason.EmptyMagazine
                        : IsReloadActive || context.RunFixedTick <= reloadShotBlockedThroughRunTick
                            ? M4ReplayShotNoRaycastReason.Reloading
                            : M4ReplayShotNoRaycastReason.ShotCadence;
                    RecordReplayShotNoRaycast(replayConsumedTick, context.RunFixedTick,
                        currentWeapon.ToString(), reason);
                }
                status = Magazine(currentWeapon) <= 0
                    ? (currentWeapon == GrayboxWeapon.ServicePistol ? "Pistol empty; press R." : "Magazine empty; press R or switch to 1 for infinite pistol reserve.")
                    : "Shot cannot resolve on this tick.";
                return;
            }
            FireSequenceId shotFireSequence = EmitPlayerFireSequence(context.RunFixedTick);
            Vector3 actualShotOrigin = playerCamera.transform.position;
            Vector3 actualShotDirection = playerCamera.transform.forward;
            bool actualShotHit = Physics.Raycast(actualShotOrigin, actualShotDirection, out RaycastHit hit, WeaponRange(currentWeapon));
            lastPartnerProtectedShotSequence = shotFireSequence.Value;
            lastPartnerProtectedShotTick = context.RunFixedTick;
            lastPartnerProtectedShotOrigin = actualShotOrigin;
            lastPartnerProtectedShotEnd = actualShotHit ? hit.point : actualShotOrigin + actualShotDirection * WeaponRange(currentWeapon);
            if (!actualShotHit)
            {
                if (replayFireCommand)
                    RecordReplayShotNoHit(replayConsumedTick, context.RunFixedTick, currentWeapon.ToString());
                status = "Shot resolved: no collider hit.";
                SetHitFeedback("MISS", new Color(1f, 0.78f, 0.2f), false);
                ConsumeShot(context.RunFixedTick);
                return;
            }
            var enemy = M4EnemyWeakPointCollider.ResolveHitActor(hit.collider);
            ActiveEnemyActor registeredActor = null;
            bool actorRegistryKeyPresent = enemy != null && actors.TryGetValue(enemy.EntityId.Value, out registeredActor);
            bool actorInstanceMatchesRegistry = actorRegistryKeyPresent && registeredActor == enemy;
            if (enemy == null || !actorRegistryKeyPresent)
            {
                if (enemy == null)
                {
                    if (playingReplay) ReplayBlockedWorldShots++;
                    if (replayFireCommand)
                        RecordReplayShotRaycastHit(replayConsumedTick, context.RunFixedTick, hit,
                            M4ReplayShotDiagnosticOutcome.WorldBlock, null, false, false, null, false);
                    status = "Shot resolved: first collider '" + hit.collider.name + "' has no accepted enemy hit binding.";
                }
                else
                {
                    if (playingReplay) ReplayBlockedResolvedEnemyShots++;
                    if (replayFireCommand)
                        RecordReplayShotRaycastHit(replayConsumedTick, context.RunFixedTick, hit,
                            M4ReplayShotDiagnosticOutcome.ResolvedEnemyBlock, enemy, actorRegistryKeyPresent,
                            actorInstanceMatchesRegistry, null, false);
                    status = "Shot resolved: first collider '" + hit.collider.name + "' has ActiveEnemyActor entity=" + enemy.EntityId.Value +
                        ", ticket=" + enemy.TicketId + " absent from the active actor registry.";
                }
                SetHitFeedback("BLOCKED", new Color(1f, 0.78f, 0.2f), false);
                ConsumeShot(context.RunFixedTick);
                return;
            }
            ulong sequence = context.Run.IssueEventSequence();
            var packet = new DamagePacket(sequence, context.Run.FixedTick, new EntityId(PlayerEntityValue), new EntityId(PlayerEntityValue),
                enemy.EntityId, DamageKind.Ballistic, DamageFor(currentWeapon), hit.point, playerCamera.transform.position,
                shotFireSequence, enemy.TicketId);
            DamageReceipt receipt = new DamageResolver(context.Run).Apply(packet);
            if (replayFireCommand)
                RecordReplayShotRaycastHit(replayConsumedTick, context.RunFixedTick, hit,
                    M4ReplayShotDiagnosticOutcome.ActiveEnemyHit, enemy, actorRegistryKeyPresent,
                    actorInstanceMatchesRegistry, receipt.Status, receipt.BecameResolved);
            ConsumeShot(context.RunFixedTick);
            if (receipt.Status != DamageApplyStatus.Applied)
            {
                status = "Shot rejected: " + receipt.Status + ".";
                SetHitFeedback("REJECTED: " + receipt.Status, Color.red, false);
                return;
            }
            SetHitFeedback("HIT  " + enemy.Archetype + "  " + receipt.HitPointsAfter.ToString("0") + " HP", Color.red, true);
            if (receipt.BecameResolved)
            {
                if (!EnemyIncarnationLifecycle.TryResolveLethal(context.Director, context.Pool, context.Run, packet, receipt, context.NightFixedTick))
                    throw new InvalidOperationException("Lethal enemy receipt did not match the session-owned active incarnation.");
                actors.Remove(enemy.EntityId.Value);
                Destroy(enemy.gameObject);
                status = "Enemy death resolved through DamageResolver and M3 Director.";
            }
            else status = "Hit " + enemy.Archetype + ": " + receipt.HitPointsAfter + " HP.";
        }

        void ConsumeShot(int runTick)
        {
            SetMagazine(currentWeapon, Magazine(currentWeapon) - 1);
            SetNextLegalShotRunTick(currentWeapon, checked(runTick + ShotIntervalTicksFor(currentWeapon)));
        }

        void CompleteReloadIfDue(int runTick)
        {
            CompleteReloadIfDue(activeReloadTransactionId, runTick);
        }

        void CompleteReloadIfDue(ulong transactionId, int runTick)
        {
            if (transactionId == 0 || transactionId != activeReloadTransactionId ||
                reloadTransactionStatus != ReloadTransactionStatus.Active || runTick < reloadCompleteRunTick) return;

            GrayboxWeapon transactionWeapon = reloadingWeapon;
            int needed = MagazineCapacity(transactionWeapon) - Magazine(transactionWeapon);
            int moved = transactionWeapon == GrayboxWeapon.ServicePistol
                ? needed
                : Mathf.Min(needed, Reserve(transactionWeapon));
            int committedMagazine = Magazine(transactionWeapon) + moved;
            int committedReserve = transactionWeapon == GrayboxWeapon.ServicePistol
                ? Reserve(transactionWeapon)
                : Reserve(transactionWeapon) - moved;

            // The transaction is due only once and both values are computed before either
            // write, so duplicate/stale transaction IDs cannot transfer ammunition again.
            SetMagazine(transactionWeapon, committedMagazine);
            if (transactionWeapon != GrayboxWeapon.ServicePistol)
                SetReserve(transactionWeapon, committedReserve);
            reloadTransactionStatus = ReloadTransactionStatus.Committed;
            activeReloadTransactionId = 0;
            reloadCompleteRunTick = -1;
            reloadShotBlockedThroughRunTick = Math.Max(reloadShotBlockedThroughRunTick, runTick);
            status = "Reload complete: " + WeaponName(transactionWeapon) + ".";
        }

        void StartReloadTransaction(GrayboxWeapon weapon, int runTick)
        {
            if (IsReloadActive) return;
            ulong transactionId = nextReloadTransactionId;
            nextReloadTransactionId = checked(nextReloadTransactionId + 1);
            reloadingWeapon = weapon;
            activeReloadTransactionId = transactionId;
            reloadTransactionStatus = ReloadTransactionStatus.Active;
            reloadCompleteRunTick = checked(runTick + ReloadTicksFor(weapon));
            reloadShotBlockedThroughRunTick = Math.Max(reloadShotBlockedThroughRunTick, runTick);
        }

        bool CancelReloadTransaction()
        {
            if (!IsReloadActive) return false;
            reloadTransactionStatus = ReloadTransactionStatus.Cancelled;
            activeReloadTransactionId = 0;
            reloadCompleteRunTick = -1;
            reloadCancellationPending = true;
            return true;
        }

        void SelectWeapon(GrayboxWeapon weapon)
        {
            if (currentWeapon == weapon) return;
            StopManualTrigger();
            bool cancelledReload = CancelReloadTransaction();
            currentWeapon = weapon;
            status = cancelledReload
                ? "Selected " + WeaponName(weapon) + "; pending reload cancelled without ammunition transfer."
                : "Selected " + WeaponName(weapon) + ".";
        }

        bool IsReloadActive => reloadTransactionStatus == ReloadTransactionStatus.Active &&
            activeReloadTransactionId != 0 && reloadCompleteRunTick >= 0;

        static string WeaponName(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return "Service Pistol";
                case GrayboxWeapon.WatchRifle: return "Watch Rifle";
                default: return "Assault Rifle";
            }
        }

        void RecordReplayShotNoRaycast(int replayConsumedTick, int runFixedTick, string weaponId,
            M4ReplayShotNoRaycastReason reason)
        {
            RecordReplayShotDiagnostic(new M4ReplayShotDiagnostic(captureRunId, DiagnosticInputPlanId,
                DiagnosticInputPlanCommandIndex(replayConsumedTick), replayConsumedTick, runFixedTick,
                weaponId, false, M4ReplayShotDiagnosticOutcome.NotAttempted, reason, false,
                string.Empty, 0f, false, default(M2GrayboxColliderKind), false, default(FaceId),
                false, false, false, null, false));
        }

        void RecordReplayShotNoHit(int replayConsumedTick, int runFixedTick, string weaponId)
        {
            RecordReplayShotDiagnostic(new M4ReplayShotDiagnostic(captureRunId, DiagnosticInputPlanId,
                DiagnosticInputPlanCommandIndex(replayConsumedTick), replayConsumedTick, runFixedTick,
                weaponId, true, M4ReplayShotDiagnosticOutcome.NoHit, M4ReplayShotNoRaycastReason.None,
                false, string.Empty, 0f, false, default(M2GrayboxColliderKind), false, default(FaceId),
                false, false, false, null, false));
        }

        void RecordReplayShotRaycastHit(int replayConsumedTick, int runFixedTick, RaycastHit hit,
            M4ReplayShotDiagnosticOutcome outcome, ActiveEnemyActor enemy, bool actorRegistryKeyPresent,
            bool actorInstanceMatchesRegistry, DamageApplyStatus? damageStatus, bool damageBecameResolved)
        {
            M2GrayboxColliderTag tag = hit.collider.GetComponent<M2GrayboxColliderTag>();
            bool hasFace = tag != null && tag.Face.HasValue;
            RecordReplayShotDiagnostic(new M4ReplayShotDiagnostic(captureRunId, DiagnosticInputPlanId,
                DiagnosticInputPlanCommandIndex(replayConsumedTick), replayConsumedTick, runFixedTick,
                currentWeapon.ToString(), true, outcome, M4ReplayShotNoRaycastReason.None, true,
                hit.collider.name, hit.distance, tag != null,
                tag == null ? default(M2GrayboxColliderKind) : tag.Kind, hasFace,
                hasFace ? tag.Face.Value : default(FaceId), enemy != null, actorRegistryKeyPresent,
                actorInstanceMatchesRegistry, damageStatus, damageBecameResolved));
        }

        void RecordReplayShotDiagnostic(M4ReplayShotDiagnostic diagnostic)
        {
            bool captureDiagnostics = playingReplay;
#if UNITY_EDITOR
            captureDiagnostics |= playingInputPlan;
#endif
            if (!captureDiagnostics) return;
            try
            {
                replayShotDiagnostics.Record(diagnostic);
            }
            catch (Exception exception)
            {
                // Shot diagnostics are a QA sidecar and must not change the Core shot result.
                replayShotDiagnosticsFailure = exception.GetType().Name + ": " + exception.Message;
            }
        }

        string DiagnosticInputPlanId => captureInputKind == M4QaCaptureInputKind.DeterministicInputPlan
            ? captureInputPlanId : string.Empty;

        int DiagnosticInputPlanCommandIndex(int replayConsumedTick) =>
            captureInputKind == M4QaCaptureInputKind.DeterministicInputPlan ? replayConsumedTick - 1 : -1;

        static int MagazineCapacity(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return ServicePistolMagazineCapacity;
                case GrayboxWeapon.WatchRifle: return WatchRifleMagazineCapacity;
                default: return AssaultRifleMagazineCapacity;
            }
        }

        int Magazine(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return servicePistolMagazine;
                case GrayboxWeapon.WatchRifle: return watchRifleMagazine;
                default: return assaultRifleMagazine;
            }
        }

        void SetMagazine(GrayboxWeapon weapon, int value)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: servicePistolMagazine = value; break;
                case GrayboxWeapon.WatchRifle: watchRifleMagazine = value; break;
                default: assaultRifleMagazine = value; break;
            }
        }

        int Reserve(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.WatchRifle: return watchRifleReserve;
                case GrayboxWeapon.AssaultRifle: return assaultRifleReserve;
                default: return int.MaxValue;
            }
        }

        void SetReserve(GrayboxWeapon weapon, int value)
        {
            if (weapon == GrayboxWeapon.WatchRifle) watchRifleReserve = value;
            else if (weapon == GrayboxWeapon.AssaultRifle) assaultRifleReserve = value;
        }

        static float DamageFor(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return ServicePistolDamage;
                case GrayboxWeapon.WatchRifle: return WatchRifleDamage;
                default: return AssaultRifleDamage;
            }
        }

        static float WeaponRange(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return 72f;
                case GrayboxWeapon.WatchRifle: return 180f;
                default: return 105f;
            }
        }

        static int ReloadTicksFor(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return ServicePistolReloadTicks;
                case GrayboxWeapon.WatchRifle: return WatchRifleReloadTicks;
                default: return AssaultRifleReloadTicks;
            }
        }

        static int ShotIntervalTicksFor(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return ServicePistolShotIntervalTicks;
                case GrayboxWeapon.WatchRifle: return WatchRifleShotIntervalTicks;
                default: return AssaultRifleMinimumShotIntervalTicks;
            }
        }

        int NextLegalShotRunTick(GrayboxWeapon weapon)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: return pistolNextLegalShotRunTick;
                case GrayboxWeapon.WatchRifle: return watchNextLegalShotRunTick;
                default: return assaultNextLegalShotRunTick;
            }
        }

        void SetNextLegalShotRunTick(GrayboxWeapon weapon, int value)
        {
            switch (weapon)
            {
                case GrayboxWeapon.ServicePistol: pistolNextLegalShotRunTick = value; break;
                case GrayboxWeapon.WatchRifle: watchNextLegalShotRunTick = value; break;
                default: assaultNextLegalShotRunTick = value; break;
            }
        }

        void AdvanceVisibleActors(M4ThreeNightQaFixedTickContext context)
        {
            foreach (ActiveEnemyActor actor in actors.Values)
            {
                if (actor.AdvanceOneTick(supportLegColliders, context, entry.ActiveNightOrdinal)) continue;
                status = "Enemy " + actor.EntityId.Value +
                         " held at its last safe pose: SupportLeg separation or AttackCapsule stand-off could not be resolved.";
            }
        }

        ActiveEnemyActor CreateEnemyActor(ActiveEnemyIncarnation incarnation, FaceId face)
        {
            M4EnemyWeakPointCollider weakPointPrefab = WeakPointPrefabFor(incarnation.Archetype);
            if (weakPointPrefab != null && !weakPointPrefab.IsConfigured)
                throw new InvalidOperationException("Weak-point prefab requires exactly one non-trigger Box, Sphere or Capsule Collider on its marked root.");
            ActiveEnemyActor actor = CreateSharedApproachActor(incarnation, face, runtimeGeometry,
                climber => climber ? climbAttackCapsules[face] : groundAttackCapsules[face],
                actualSupportLegs: supportLegColliders);
            if (weakPointPrefab != null)
            {
                Instantiate(weakPointPrefab, actor.transform, false);
                actor.MarkAttachedHitShapeCoverageUnavailable();
            }
            return actor;
        }

        M4EnemyWeakPointCollider WeakPointPrefabFor(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Walker: return walkerWeakPointPrefab;
                case EnemyArchetype.Runner: return runnerWeakPointPrefab;
                case EnemyArchetype.Breaker: return breakerWeakPointPrefab;
                case EnemyArchetype.Climber: return climberWeakPointPrefab;
                default: return null;
            }
        }

        // Shared creation law only. Caller owns lifecycle; the production wrapper keeps its unparented actor.
        internal static ActiveEnemyActor CreateSharedApproachActor(ActiveEnemyIncarnation incarnation, FaceId face,
            TowerGeometryFixture geometry, Func<bool, Collider> resolveAssignedTarget, Action<GameObject> observeCreatedObject = null,
            Action<Material> observeCreatedMaterial = null, IReadOnlyList<Collider> actualSupportLegs = null)
        {
            bool climber = incarnation.Archetype == EnemyArchetype.Climber;
            Vector3[] approachWaypoints = BuildAuthoredApproachWaypoints(geometry, face, climber,
                out int climbWaypointStartIndex);
            BoxCollider climbSupport = climber ? RequireClimberSupport(actualSupportLegs, face) : null;
            Vector3 direction = TowerGeometryFixture.DirectionFor(face);
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            observeCreatedObject?.Invoke(gameObject);
            gameObject.name = "M3_" + incarnation.Archetype + "_" + incarnation.EntityId + "_T" + incarnation.TicketId;
            gameObject.transform.position = direction * TowerGeometryFixture.SpawnRingMaximumRadius + Vector3.up;
            gameObject.transform.localScale = incarnation.Archetype == EnemyArchetype.Breaker ? new Vector3(1.4f, 1.5f, 1.4f) : Vector3.one;
            float groundRouteLift = incarnation.Archetype == EnemyArchetype.Breaker
                ? ActiveEnemyActor.GroundRouteLift(gameObject.GetComponent<CapsuleCollider>())
                : 0f;
            gameObject.transform.position += Vector3.up * groundRouteLift;
            Renderer renderer = gameObject.GetComponent<Renderer>();
            Material actorMaterial = renderer.material;
            observeCreatedMaterial?.Invoke(actorMaterial);
            actorMaterial.color = ArchetypeColor(incarnation.Archetype);
            var actor = gameObject.AddComponent<ActiveEnemyActor>();
            Collider targetAttackCapsule = resolveAssignedTarget(climber);
            AttackPointId targetAttackPoint = climber ? AttackPointIdForClimbFace(face) : AttackPointIdForGroundFace(face);
            for (int index = 0; index < approachWaypoints.Length; index++)
                approachWaypoints[index] += Vector3.up * groundRouteLift;
            try
            {
                Vector3 normal = default;
                float planeDistance = 0f;
                if (climber)
                    approachWaypoints = BuildClimberCenterWaypoints(approachWaypoints, climbWaypointStartIndex,
                        gameObject.GetComponent<CapsuleCollider>(), climbSupport, out normal, out planeDistance);
                actor.Initialize(incarnation, targetAttackPoint, targetAttackCapsule, approachWaypoints,
                    climbWaypointStartIndex, groundRouteLift);
                if (climber)
                {
                    actor.RecordClimberCenterAdaptation(climbSupport.GetInstanceID(), normal, planeDistance);
                    gameObject.AddComponent<M4ClimberStrikeFeedback>();
                }
            }
            catch
            {
                // Only this factory's new primitive/material; fixture callbacks still own their receipts.
                gameObject.SetActive(false);
                UnityEngine.Object.Destroy(gameObject);
                UnityEngine.Object.Destroy(actorMaterial);
                throw;
            }
            return actor;
        }

        static BoxCollider RequireClimberSupport(IReadOnlyList<Collider> supports, FaceId face)
        {
            BoxCollider selected = null;
            if (supports == null) throw new InvalidOperationException("CLIMBER_CENTER_SUPPORT_INPUT_UNAVAILABLE");
            foreach (Collider value in supports)
            {
                if (value == null) continue;
                M2GrayboxColliderTag tag = value.GetComponent<M2GrayboxColliderTag>();
                if (tag == null || tag.Kind != M2GrayboxColliderKind.SupportLeg || tag.Face != face) continue;
                if (selected != null) throw new InvalidOperationException("CLIMBER_CENTER_SUPPORT_INPUT_AMBIGUOUS");
                selected = value as BoxCollider;
                if (selected == null || !selected.enabled || selected.isTrigger || !selected.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("CLIMBER_CENTER_SUPPORT_INPUT_UNSUPPORTED");
            }
            if (selected == null) throw new InvalidOperationException("CLIMBER_CENTER_SAME_FACE_SUPPORT_UNAVAILABLE");
            // Current builder roots are unscaled/unrotated. The Box's own nonuniform beam scale is retained.
            for (Transform parent = selected.transform.parent; parent != null; parent = parent.parent)
                if (!parent.localScale.Equals(Vector3.one) || !parent.localRotation.Equals(Quaternion.identity))
                    throw new InvalidOperationException("CLIMBER_CENTER_SUPPORT_PARENT_TRANSFORM_UNSUPPORTED");
            return selected;
        }

        static Vector3[] BuildClimberCenterWaypoints(Vector3[] source, int climbStart,
            CapsuleCollider mover, BoxCollider support, out Vector3 normal, out float planeDistance)
        {
            normal = support.transform.right;
            Vector3 boxCenter = support.transform.TransformPoint(support.center);
            Vector3 boxScale = support.transform.lossyScale;
            Vector3 bodyScale = mover == null ? Vector3.zero : mover.transform.lossyScale;
            // Quaternion-to-axis float roundoff admission only; never a position/contact/gameplay epsilon.
            const float frameRoundoff = 8f * 1.1920929e-7f;
            if (mover == null || !mover.enabled || mover.isTrigger || mover.direction != 1 ||
                !mover.center.Equals(Vector3.zero) || mover.radius != 0.5f || mover.height != 2f ||
                mover.transform.parent != null || !mover.transform.localScale.Equals(Vector3.one) ||
                !CenterFinite(bodyScale) || bodyScale.x <= 0f || bodyScale.y <= 0f || bodyScale.z <= 0f ||
                !CenterFinite(boxCenter) || !CenterFinite(normal) || !CenterFinite(boxScale) ||
                Mathf.Abs(normal.y) > frameRoundoff ||
                boxScale.x <= 0f || boxScale.y <= 0f || boxScale.z <= 0f ||
                !CenterFinite(support.size) || support.size.x <= 0f || support.size.y <= 0f || support.size.z <= 0f ||
                !CenterFinite(mover.contactOffset) || !CenterFinite(support.contactOffset) ||
                mover.contactOffset < 0f || support.contactOffset < 0f || climbStart < 1 || climbStart >= source.Length)
                throw new InvalidOperationException("CLIMBER_CENTER_SHAPE_OR_ROUTE_INPUT_UNSUPPORTED");
            float radius = mover.radius * Mathf.Max(bodyScale.x, bodyScale.z);
            float halfLine = Mathf.Max(0f, mover.height * bodyScale.y * 0.5f - radius);
            float halfWidth = support.size.x * boxScale.x * 0.5f;
            float margin = Mathf.Max(mover.contactOffset, support.contactOffset);
            return BuildClimberCenterWaypoints(source, climbStart, boxCenter, normal, radius,
                halfLine, halfWidth, margin, out planeDistance);
        }

        // Copied value inputs only; no collider lifetime, query, state mutation or gameplay simulation.
        static Vector3[] BuildClimberCenterWaypoints(Vector3[] source, int climbStart, Vector3 boxCenter,
            Vector3 normal, float radius, float halfLine, float halfWidth, float margin, out float planeDistance)
        {
            planeDistance = Vector3.Dot(source[climbStart - 1] - boxCenter, normal);
            if (!CenterFinite(radius) || !CenterFinite(halfLine) || !CenterFinite(halfWidth) ||
                !CenterFinite(planeDistance) || planeDistance <= halfWidth + radius + margin)
                throw new InvalidOperationException("CLIMBER_CENTER_START_PLANE_CLEARANCE_UNAVAILABLE");
            Vector3[] result = (Vector3[])source.Clone();
            for (int index = 0; index < source.Length; index++)
            {
                if (!CenterFinite(source[index])) throw new InvalidOperationException("CLIMBER_CENTER_BASE_POINT_NONFINITE");
                if (index < climbStart) continue;
                result[index] = source[index] + normal * (planeDistance - Vector3.Dot(source[index] - boxCenter, normal));
                Vector3 forward = result[index] - result[index - 1];
                if (!CenterFinite(result[index]) || !CenterFinite(forward) || forward.sqrMagnitude <= 0f)
                    throw new InvalidOperationException("CLIMBER_CENTER_DERIVED_SEGMENT_UNAVAILABLE");
                Vector3 bodyAxis = Quaternion.LookRotation(forward.normalized, Vector3.up) * Vector3.up;
                float extent = radius + halfLine * Mathf.Abs(Vector3.Dot(bodyAxis, normal));
                if (!CenterFinite(extent) || planeDistance <= halfWidth + extent + margin)
                    throw new InvalidOperationException("CLIMBER_CENTER_SEGMENT_BODY_CLEARANCE_UNAVAILABLE");
            }
            return result;
        }

        static bool CenterFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool CenterFinite(Vector3 value) => CenterFinite(value.x) && CenterFinite(value.y) && CenterFinite(value.z);

        internal static AttackPointId AttackPointIdForGroundFace(FaceId face)
        {
            switch (face)
            {
                case FaceId.FaceA: return AttackPointId.AP_A_G;
                case FaceId.FaceB: return AttackPointId.AP_B_G;
                case FaceId.FaceC: return AttackPointId.AP_C_G;
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        internal static AttackPointId AttackPointIdForClimbFace(FaceId face)
        {
            switch (face)
            {
                case FaceId.FaceA: return AttackPointId.AP_A_C;
                case FaceId.FaceB: return AttackPointId.AP_B_C;
                case FaceId.FaceC: return AttackPointId.AP_C_C;
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        internal static Vector3[] BuildAuthoredApproachWaypoints(TowerGeometryFixture geometry, FaceId face, bool climb,
            out int climbWaypointStartIndex)
        {
            if (geometry == null) throw new InvalidOperationException("The runtime tower geometry is unavailable.");
            var waypoints = new List<Vector3>();
            AppendRouteWaypoints(waypoints, geometry.Routes.Single(route =>
                route.Face == face && route.Kind == RouteKind.SpawnLane), false);
            AppendRouteWaypoints(waypoints, geometry.Routes.Single(route =>
                route.Face == face && route.Kind == RouteKind.ApproachSpline), true);
            climbWaypointStartIndex = int.MaxValue;
            if (climb)
            {
                climbWaypointStartIndex = waypoints.Count;
                AppendRouteWaypoints(waypoints, geometry.Routes.Single(route =>
                    route.Face == face && route.Kind == RouteKind.ClimberRail), true);
            }
            return waypoints.ToArray();
        }

        static void AppendRouteWaypoints(List<Vector3> destination, RouteDescriptor route, bool skipSharedFirst)
        {
            if (!route.IsAvailable) throw new InvalidOperationException("The assigned enemy route is unavailable: " + route.Id);
            Vector3[] samples = route.Sample(TowerGeometryFixture.LineOfSightSampleSpacing);
            int first = skipSharedFirst ? 1 : 0;
            for (int index = first; index < samples.Length; index++)
                destination.Add(samples[index] + Vector3.up * 0.5f);
        }

        static FaceId FindTicketFace(EnemyDirectorRuntime director, ulong ticketId)
        {
            foreach (SpawnTicketSnapshot ticket in director.Snapshot().Tickets)
                if (ticket.Ticket.TicketId == ticketId) return ticket.Ticket.Face;
            throw new InvalidOperationException("Active incarnation ticket is absent from the session-owned Director.");
        }

        static Color ArchetypeColor(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Runner: return new Color(0.95f, 0.7f, 0.2f);
                case EnemyArchetype.Climber: return new Color(0.55f, 0.75f, 0.95f);
                case EnemyArchetype.Breaker: return new Color(0.95f, 0.25f, 0.2f);
                default: return new Color(0.72f, 0.8f, 0.72f);
            }
        }

        void BuildRuntimeGraybox()
        {
            var builder = gameObject.AddComponent<M2GrayboxBuilder>();
            if(qaEnableManualTraversal)builder.Build(TowerGeometryFixture.CreateManualTraversalQa());
            else builder.BuildNormal();
            runtimeGeometry = builder.Geometry;
            if(qaEnableManualTraversal)
            {
                manualTraversal=new TraversalController(50,runtimeGeometry.ManualAccess,automatic:true);
                manualTraversalReceipt="Tap traversal ready: walk to the marked C/A access footprint, press E once.";
            }
            BoxCollider[] stationMatches = builder.GetComponentsInChildren<BoxCollider>(true)
                .Where(collider => collider.name == StationId.S_A.ToString()).ToArray();
            partnerExchangeStation = stationMatches.Length == 1 ? stationMatches[0] : null;
            supportLegColliders.Clear();
            attackCapsules.Clear();
            groundAttackCapsules.Clear();
            climbAttackCapsules.Clear();
            M2GrayboxColliderTag[] colliderTags = builder.GetComponentsInChildren<M2GrayboxColliderTag>(true);
            foreach (M2GrayboxColliderTag colliderTag in colliderTags)
            {
                Collider supportLeg = colliderTag.GetComponent<Collider>();
                if (supportLeg == null) continue;
                if (colliderTag.Kind == M2GrayboxColliderKind.SupportLeg)
                {
                    supportLegColliders.Add(supportLeg);
                }
                else if (colliderTag.Kind == M2GrayboxColliderKind.AttackCapsule)
                {
                    attackCapsules.Add(supportLeg);
                    if (supportLeg.name.EndsWith("_G", StringComparison.Ordinal))
                        groundAttackCapsules.Add(colliderTag.Face.Value, supportLeg);
                    else if (supportLeg.name.EndsWith("_C", StringComparison.Ordinal))
                        climbAttackCapsules.Add(colliderTag.Face.Value, supportLeg);
                }
            }
            if (attackCapsules.Count != 6 || groundAttackCapsules.Count != 3 || climbAttackCapsules.Count != 3)
                throw new InvalidOperationException("The M4 graybox requires all six authored AttackCapsule targets.");
            CreatePlayerAndCamera();
            CreatePartnerMarker();
            partnerMovement = new M4PartnerMovement(partnerMovementProfile, runtimeGeometry,
                partnerTransform.GetComponent<CapsuleCollider>(), builder.GetComponentsInChildren<Collider>(true));
            partnerMovement.TryInitialSetup(out authoredInitialPartnerNode);
            var lightObject = new GameObject("QA_DirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        void CreatePlayerAndCamera()
        {
            var player = new GameObject("QA_Player");
            player.transform.position = Vector3.up * (TowerGeometryFixture.UpperDeckY + 0.35f);
            playerTransform = player.transform;
            var cameraObject = new GameObject("QA_PlayerCamera");
            cameraObject.transform.SetParent(playerTransform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            playerCamera = cameraObject.AddComponent<Camera>();
            playerCamera.fieldOfView = 70f;
            cameraObject.AddComponent<AudioListener>();
        }

        void CreatePartnerMarker()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            marker.name = "QA_Partner_CoverFace_A";
            marker.transform.position = TowerGeometryFixture.DirectionFor(FaceId.FaceA) * 5f + Vector3.up * (TowerGeometryFixture.UpperDeckY + 1.2f);
            marker.GetComponent<Renderer>().material.color = new Color(0.3f, 0.65f, 0.95f);
            partnerTransform = marker.transform;
            partnerAcknowledgementCue = marker.AddComponent<M4PartnerAcknowledgementCue>();
        }

        bool TrySelectPartnerCoverFace(out FaceId face, out string reason)
        {
            face = default(FaceId);
            reason = "explicit two-asset QA command HUD opt-in unavailable";
            if (partnerOrderSelection == null || !partnerOrderSelection.TrySelect(playerCamera, out face, out reason)) return false;
            M4Session session = entry.CanonicalSessionForPlayerAction;
            RunStateSnapshot run = session == null ? null : session.ReadRun();
            if (run == null || !entry.FixedTickRunning || session.Stage != M4SessionStage.Defense ||
                run.Phase != RunPhase.Defense || run.PlayerLife != PlayerLifeState.Alive ||
                run.PlayerTraversal != TraversalState.None || run.Tower != TowerState.Stable)
            { reason = "phase/traversal/tower safety domain unavailable"; return false; }
            if (!session.ReadTopology().ValidFaces.Contains(face))
            { reason = "selected Face unavailable in active topology"; return false; }
            FaceId selectedFace = face;
            StationDescriptor[] stations = runtimeGeometry.Stations.Where(s => s.Face == selectedFace && s.IsAvailable).ToArray();
            if (stations.Length != 1) { reason = "unique available Face station unavailable"; return false; }
            PartnerRuntimeState partner = session.RuntimePartner;
            if (partner == null || partner.Life != PartnerLifeState.Healthy)
            { reason = "current life state cannot accept CoverFace"; return false; }
            if (partner.Decision == PartnerDecisionState.Recovering || partner.Movement == PartnerMovementState.Recovering ||
                (partner.Role == PartnerRole.None && !string.IsNullOrWhiteSpace(partner.ReservedNodeId)))
            { reason = "existing recovery/safety movement has priority"; return false; }
            return true; // Canonical fixed tick rechecks these and the actual retained/replacement path contract.
        }

        PartnerQMode ReadPartnerQContext(out string reason)
        {
            reason = string.Empty;
            if (playerCamera == null || !IsFinitePartnerAim(playerCamera.transform.position) || !IsFinitePartnerAim(playerCamera.transform.forward))
            { reason = "camera aim unavailable"; return PartnerQMode.Unavailable; }
            RaycastHit[] hits = Physics.RaycastAll(playerCamera.transform.position, playerCamera.transform.forward,
                Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.enabled) continue;
                M2GrayboxColliderTag tag = collider.GetComponent<M2GrayboxColliderTag>();
                if (tag != null && tag.Kind == M2GrayboxColliderKind.AttackCapsule) continue;
                if (partnerTransform != null && collider.transform.IsChildOf(partnerTransform)) return PartnerQMode.Exchange;
                if (tag != null && (tag.Kind == M2GrayboxColliderKind.SupportLeg || tag.Kind == M2GrayboxColliderKind.Structure ||
                    tag.Kind == M2GrayboxColliderKind.UpperSurface || tag.Kind == M2GrayboxColliderKind.GroundSurface)) return PartnerQMode.Face;
                reason = "Q target role/policy UNAVAILABLE: " + collider.name;
                return PartnerQMode.Unavailable;
            }
            return PartnerQMode.Face;
        }

        string DescribePartnerQContext()
        {
            PartnerQMode mode = ReadPartnerQContext(out string reason);
            return mode == PartnerQMode.Exchange ? "Q: Partner 교환 (거리/LOS 재검사)" :
                mode == PartnerQMode.Face ? "Q 홀드: 방향 지정 / 놓으면 확정" : "Q: 설비/대상 요청 불가 — " + reason;
        }

        void BeginPartnerQ()
        {
            partnerQMode = ReadPartnerQContext(out string reason);
            partnerQArmed = true;
            CapturePartnerQInput();
            if (partnerQMode == PartnerQMode.Exchange) partnerExchangeOpenRequested = true;
            else if (partnerQMode == PartnerQMode.Unavailable) { partnerQArmed = false; RejectPartnerCoverRequest(reason); }
            else partnerOrderSelection?.Show(true, null, true);
            if (partnerQMode != PartnerQMode.Face) partnerQPreview = partnerQMode == PartnerQMode.Exchange
                ? "Partner 교환 / 진입 검사 중 · Q를 놓으면 닫기" : "대상 요청 불가 — " + reason;
        }

        void CapturePartnerMouse()
        {
            partnerMouse0Blocked |= Input.GetMouseButton(0);
            partnerMouse1Blocked |= Input.GetMouseButton(1);
        }

        void RefreshPartnerQPreview()
        {
            if (partnerQMode != PartnerQMode.Face) return;
            bool candidate = TrySelectPartnerCoverFace(out FaceId face, out string reason);
            partnerQPreview = !partnerQArmed ? "선택 비활성 — Q를 놓고 다시 누르십시오" : candidate
                ? "Q를 놓으면 " + (face == FaceId.FaceA ? "A" : face == FaceId.FaceB ? "B" : "C") + " 엄호"
                : "선택 없음 — Q를 놓으면 종료 / " + reason;
            partnerOrderSelection?.Show(true, candidate ? (FaceId?)face : null, partnerQArmed);
        }

        void EndPartnerQ(bool allowOrder)
        {
            if (partnerQMode == PartnerQMode.Face && partnerQArmed && allowOrder && entry.FixedTickRunning)
            {
                // Re-evaluate the current visible region, never the last valid hover.
                if (TrySelectPartnerCoverFace(out FaceId face, out string reason))
                {
                    partnerCoverRequestedFace = face;
                    partnerCoverFaceValid = true;
                    // Preserve down-time run/Night identity; copy only release aim primitives.
                    partnerCoverAimOrigin = playerCamera.transform.position;
                    partnerCoverAimDirection = playerCamera.transform.forward;
                    partnerCoverAimValid = IsFinitePartnerAim(partnerCoverAimOrigin) && IsFinitePartnerAim(partnerCoverAimDirection);
                    partnerCoverRequested = true;
                }
                else partnerRequestReceipt = "Q: no order; prior order retained / " + reason;
            }
            partnerOrderSelection?.Show(false, null, false);
            partnerQMode = PartnerQMode.None; partnerQArmed = false;
            partnerExchangeOpenRequested = false;
            if (partnerExchangeMenuOpen) ClosePartnerExchangeMenu();
            suppressMenuShotThroughFrame = Time.frameCount;
        }

        void DisarmPartnerQ()
        {
            DiscardExchangeSelection("입력 비활성화로 미접수 선택 폐기");
            partnerQArmed = false;
            partnerCoverRequested = false; partnerExchangeOpenRequested = false;
            partnerExchangeBeginRequested = false;
            CapturePartnerMouse(); StopManualTrigger();
            RefreshPartnerQPreview();
            if (partnerQMode == PartnerQMode.Exchange) partnerQPreview = "교환 선택 비활성 — Q를 놓고 다시 누르십시오";
        }

        void OnApplicationFocus(bool focus)
        {
            manualFocused=focus;
            ClearManualTraversalLatch(); // discard unaccepted/stale input only; committed movement continues
            if (!focus) DisarmPartnerQ();
        }

        void OnDisable()
        {
            if(qaEnableManualTraversal)CloseManualTraversalDomain("controller disabled");
            else ClearManualTraversalLatch();
            DiscardExchangeSelection("화면 비활성화로 미접수 선택 폐기");
            partnerCoverRequested = false; partnerExchangeOpenRequested = false;
            partnerExchangeBeginRequested = false; partnerQArmed = false; partnerQMode = PartnerQMode.None;
            partnerExchangeMenuOpen = false;
            StopManualTrigger();
            partnerOrderSelection?.Show(false, null, false);
        }

        void OnDestroy() { partnerOrderSelection?.Dispose(); }

        void CapturePartnerQInput()
        {
            M4Session session = entry.CanonicalSessionForPlayerAction;
            partnerCoverInputRunIdentity = session == null ? null : session.Metadata.RunId.ToString();
            partnerCoverInputNight = entry.ActiveNightOrdinal;
            partnerCoverAimValid = playerCamera != null;
            if (!partnerCoverAimValid) return;
            // Copy primitive input values at the Q edge; never cache session state.
            partnerCoverAimOrigin = playerCamera.transform.position;
            partnerCoverAimDirection = playerCamera.transform.forward;
            if (!IsFinitePartnerAim(partnerCoverAimOrigin) || !IsFinitePartnerAim(partnerCoverAimDirection))
            {
                partnerCoverAimValid = false;
                partnerCoverAimFailure = "Q-edge aim origin/direction unavailable";
            }
        }

        static bool IsFinitePartnerAim(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        void ProcessManualPartnerCoverRequest(M4ThreeNightQaFixedTickContext context)
        {
            if (!partnerCoverRequested && !partnerExchangeOpenRequested) return;
            bool openExchange = partnerExchangeOpenRequested;
            partnerCoverRequested = false;
            partnerExchangeOpenRequested = false;
            if (playingReplay)
            { RejectPartnerCoverRequest("manual Q unavailable during replay"); return; }
#if UNITY_EDITOR
            if (deterministicInputPlanCapture || playingInputPlan || playingAdaptiveQa)
            { RejectPartnerCoverRequest("manual Q unavailable during automated input"); return; }
#endif
            M4Session session = entry.CanonicalSessionForPlayerAction;
            if (session == null || session.Metadata.RunId.ToString() != partnerCoverInputRunIdentity ||
                entry.ActiveNightOrdinal != partnerCoverInputNight)
            { RejectPartnerCoverRequest("Q-edge run/Night context changed"); return; }
            if (session == null || !entry.FixedTickRunning || session.Stage != M4SessionStage.Defense ||
                context.Run.Phase != RunPhase.Defense || context.Run.PlayerLife != PlayerLifeState.Alive ||
                context.Run.PlayerTraversal != TraversalState.None || context.Run.Tower != TowerState.Stable)
            { RejectPartnerCoverRequest("phase/traversal/tower safety domain unavailable"); return; }
            if (!partnerCoverAimValid)
            { RejectPartnerCoverRequest(partnerCoverAimFailure); return; }
            FaceId face = partnerCoverRequestedFace;
            if (openExchange)
            {
            // The first real solid target keeps its meaning; equipment and Partner
            // requests are not silently converted to a directional CoverFace request.
            RaycastHit[] hits = Physics.RaycastAll(partnerCoverAimOrigin, partnerCoverAimDirection,
                Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.enabled) continue;
                M2GrayboxColliderTag tag = collider.GetComponent<M2GrayboxColliderTag>();
                if (tag != null && tag.Kind == M2GrayboxColliderKind.AttackCapsule) continue;
                if (partnerTransform != null && collider.transform.IsChildOf(partnerTransform))
                {
                    string exchangeReason = "hold Q while aiming at Partner";
                    if (partnerQMode == PartnerQMode.Exchange && partnerQArmed && Input.GetKey(KeyCode.Q) &&
                        TryAdmitPartnerExchange(session, context.Run, out exchangeReason))
                    {
                        if (!partnerExchangeMenuOpen)
                        {
                            var flow = M4PartnerExchangePersistence.Ensure(session.RuntimePartner);
                            if (flow.nextSelectionEpisode == ulong.MaxValue) { RejectPartnerCoverRequest("selection episode exhausted"); return; }
                            partnerExchangeSelectionEpisode = flow.nextSelectionEpisode++;
                        }
                        partnerExchangeMenuOpen = true;
                        StopManualTrigger();
                        partnerExchangeInputRunIdentity = partnerCoverInputRunIdentity;
                        partnerExchangeInputNight = partnerCoverInputNight;
                        shotRequested = false;
                        if (recordingReplay) InvalidateReplayCapture("manual Partner exchange menu input accepted; current tape has no Q/menu command; export unavailable", expectedManualInput: true);
                        suppressMenuShotThroughFrame = Time.frameCount;
                        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                        partnerQPreview = "Partner 교환 / 명시 선택 · Q를 놓으면 닫기";
                    }
                    else RejectPartnerCoverRequest(exchangeReason);
                    return;
                }
                // Owner approved known-world first hits as directional Cover intent.
                // Do not reinterpret hidden later hits as a Partner/equipment target.
                if (tag != null && (tag.Kind == M2GrayboxColliderKind.SupportLeg || tag.Kind == M2GrayboxColliderKind.Structure ||
                    tag.Kind == M2GrayboxColliderKind.UpperSurface || tag.Kind == M2GrayboxColliderKind.GroundSurface)) break;
                string reason = "Q target role/policy UNAVAILABLE: " + collider.name;
                RejectPartnerCoverRequest(reason);
                return;
            }
                RejectPartnerCoverRequest("Q-down explicit Partner target no longer available; no Face fallback");
                return;
            }
            if (!partnerCoverFaceValid)
            { RejectPartnerCoverRequest(partnerCoverAimFailure); return; }
            if (!session.ReadTopology().ValidFaces.Contains(face))
            { RejectPartnerCoverRequest("selected Face unavailable in active topology"); return; }
            StationDescriptor[] stations = runtimeGeometry.Stations.Where(station => station.Face == face && station.IsAvailable).ToArray();
            if (stations.Length != 1)
            { RejectPartnerCoverRequest("unique available Face station descriptor unavailable"); return; }
            PartnerRuntimeState partner = session.RuntimePartner;
            if (partner == null || partner.Life != PartnerLifeState.Healthy)
            { RejectPartnerCoverRequest(partner != null && partner.Life == PartnerLifeState.Injured
                ? "Injured: Cover combat unavailable; Partner can still signal" : "current life state cannot accept CoverFace"); return; }
            if (partner.Decision == PartnerDecisionState.Recovering || partner.Movement == PartnerMovementState.Recovering ||
                (partner.Role == PartnerRole.None && !string.IsNullOrWhiteSpace(partner.ReservedNodeId)))
            { RejectPartnerCoverRequest("existing recovery/safety movement has priority"); return; }
            string stationId = stations[0].Id.ToString();
            string faceId = face == FaceId.FaceA ? "A" : face == FaceId.FaceB ? "B" : "C";
            ExchangeTransaction pendingExchange = partner.PendingExchange;
            if (partner.Role == PartnerRole.CoverFace && partner.DesignatedFaceId == faceId &&
                (pendingExchange == null || pendingExchange.Completed || pendingExchange.Cancelled) &&
                partnerMovement != null && partnerMovement.CanRetainRequest(session, stationId))
            {
                partnerRequestReceipt = "Q NO-OP: CoverFace(" + faceId + ") already " +
                    (partner.MovementProgress.completed ? "arrived" : "moving") + "; existing request/plan retained.";
                partnerMovement.RecordCommand(session, context.RunFixedTick, "Q_NO_OP", "CoverFace " + faceId + "; target=" + stationId);
                return;
            }
            M4PartnerMuzzleLane orderLane = CapturePartnerProtectedLane(context.RunFixedTick);
            if (partnerMovement != null && !partnerMovement.CanPrepareReplacement(session, stationId, out string replacementReason, orderLane))
            {
                partnerMovement.RecordCommand(session, context.RunFixedTick, "Q_REPLACEMENT_REJECTED", "CoverFace " + faceId + "; attempted=" + stationId + "; " + replacementReason);
                RejectPartnerCoverRequest(replacementReason ?? "replacement retained route unavailable; prior request preserved"); return;
            }
            var controller = new PartnerController(partner);
            try
            {
                controller.RequestRole(new PartnerRoleRequest(PartnerRole.CoverFace,
                    faceId, stationId, context.RunFixedTick), context.RunFixedTick);
            }
            catch (ArgumentException exception) { RejectPartnerCoverRequest(exception.Message); return; }
            catch (InvalidOperationException exception) { RejectPartnerCoverRequest(exception.Message); return; }
            if (partnerMovement != null)
            {
                partnerMovement.RecordCommand(session, context.RunFixedTick, "Q_ACK", "CoverFace " + faceId + "; requested=" + stationId);
                partnerMovement.Begin(session, context.RunFixedTick, orderLane);
            }
            PartnerObservation observation = controller.Observe();
            // This success belongs to this call, rather than a stale LastSignal read.
            if (recordingReplay)
                InvalidateReplayCapture("manual Partner Q accepted at runFixedTick=" + context.RunFixedTick +
                    "; current tape has no role command; canonical request continues, export unavailable", expectedManualInput: true);
            partnerRequestReceipt = "ACK " + observation.Role + "(" + observation.DesignatedFaceId + ") @runTick " + context.RunFixedTick +
                " | " + observation.Decision + "/" + observation.Movement +
                " | current=" + observation.CurrentNodeId + " reservedDescriptor=" + observation.ReservedNodeId +
                " | signal=" + partner.LastSignal + " | " + (partnerMovement == null ? "movement authoring UNAVAILABLE" : partnerMovement.Receipt);
            if (partnerAcknowledgementCue != null) partnerAcknowledgementCue.Emit();
        }

        void RejectPartnerCoverRequest(string reason)
        { partnerRequestReceipt = "Q REJECTED: " + reason + " (no acknowledgement cue)"; }

        M4PartnerMuzzleLane CapturePartnerProtectedLane(int runTick)
        {
            bool active = lastPartnerProtectedShotSequence != 0 && (lastPartnerProtectedShotTick == runTick ||
                (activeFireSequence == lastPartnerProtectedShotSequence && lastPlayerShotRunTick >= 0 && runTick - lastPlayerShotRunTick < 18));
            return active ? new M4PartnerMuzzleLane(lastPartnerProtectedShotOrigin, lastPartnerProtectedShotEnd, lastPartnerProtectedShotSequence)
                : default(M4PartnerMuzzleLane);
        }

        bool TryAdmitPartnerExchange(M4Session session, RunState run, out string reason, bool maintaining = false, int tick = -1)
        {
            reason = null;
            if (partnerExchangePolicy == null) { reason = "explicit exchange QA policy missing"; return false; }
            if (session == null || session.Stage != M4SessionStage.Defense || run.Phase != RunPhase.Defense ||
                run.Tower != TowerState.Stable || run.PlayerLife != PlayerLifeState.Alive || run.PlayerTraversal != TraversalState.None ||
                run.PartnerLife != PartnerLifeState.Healthy || run.PlayerZone != LocationZone.UpperSafeZone ||
                session.RuntimePartner.Life != PartnerLifeState.Healthy)
            { reason = "phase/life/traversal/safety domain unavailable"; return false; }
            var partner = session.RuntimePartner;
            if (partnerMovement == null || !partnerMovement.CanHoldForExchange(session))
            { reason = "actual Partner pose/movement contract unavailable"; return false; }
            if (partner.Decision == PartnerDecisionState.Recovering || partner.Movement == PartnerMovementState.Recovering ||
                (partner.Role == PartnerRole.None && !string.IsNullOrWhiteSpace(partner.ReservedNodeId)))
            { reason = "recovery/safety movement has priority"; return false; }
            CapsuleCollider capsule = partnerTransform == null ? null : partnerTransform.GetComponent<CapsuleCollider>();
            if (playerTransform == null || playerCamera == null || capsule == null || !capsule.enabled || capsule.isTrigger || !capsule.gameObject.activeInHierarchy)
            { reason = "actual Player/Camera/Partner capsule missing"; return false; }
            Vector3 target = capsule.transform.TransformPoint(capsule.center);
            Vector3 delta = target - playerTransform.position; delta.y = 0f;
            float limit = maintaining ? partnerExchangePolicy.maintainDistanceMetres : partnerExchangePolicy.startDistanceMetres;
            if (!IsFinitePartnerAim(delta) || delta.sqrMagnitude > limit * limit)
            { reason = "actual actor distance outside " + (maintaining ? "maintenance" : "start") + " range"; return false; }
            Vector3 line = target - playerCamera.transform.position;
            bool clear = IsFinitePartnerAim(line) && line.sqrMagnitude > 0f;
            if (!clear) { reason = "actual camera-to-Partner LOS geometry unavailable"; return false; }
            RaycastHit[] hits = Physics.RaycastAll(playerCamera.transform.position, line.normalized, line.magnitude, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                Collider collider = hit.collider; if (collider == null || collider.transform.IsChildOf(partnerTransform) || collider.transform.IsChildOf(playerTransform)) continue;
                var tag = collider.GetComponent<M2GrayboxColliderTag>();
                if (tag != null && tag.Kind == M2GrayboxColliderKind.AttackCapsule) continue;
                if (tag == null || (tag.Kind != M2GrayboxColliderKind.Structure && tag.Kind != M2GrayboxColliderKind.SupportLeg &&
                    tag.Kind != M2GrayboxColliderKind.GroundSurface && tag.Kind != M2GrayboxColliderKind.UpperSurface))
                { reason = "exchange LOS UNAVAILABLE: actual collider role/policy not specified: " + collider.name; return false; }
                clear = false; break;
            }
            var flow = partner.ExchangeFlow;
            if (clear) { if (maintaining && flow != null) flow.occludedSinceTick = -1; return true; }
            if (maintaining && tick >= 0 && flow != null)
            {
                if (flow.occludedSinceTick < 0) flow.occludedSinceTick = tick;
                bool due = partner.PendingExchange != null && tick >= partner.PendingExchange.CompletionTick;
                int grace = Mathf.CeilToInt(partnerExchangePolicy.occlusionGraceSeconds * 50f);
                if (!due && tick - flow.occludedSinceTick < grace) return true;
            }
            reason = "actual camera-to-Partner LOS blocked by known world structure"; return false;
        }
        void ClosePartnerExchangeMenu()
        {
            partnerExchangeMenuOpen = false;
            suppressMenuShotThroughFrame = Time.frameCount;
            if (entry.FixedTickRunning) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void ResetExchangeFeedback(string runIdentity)
        {
            exchangeFeedbackRun = runIdentity;
            latestExchangeSelection = null; lastExchangeTransfer = null;
            partnerExchangeScroll = Vector2.zero;
            partnerExchangeReceipt = "아직 확인된 교환 선택이 없습니다.";
        }

        void EnsureExchangeFeedback(M4Session session)
        {
            if (session != null && exchangeFeedbackRun != session.Metadata.RunId.ToString())
                ResetExchangeFeedback(session.Metadata.RunId.ToString());
        }

        ExchangeFeedback CaptureExchangeFeedback(M4Session session, int tick, string result, ExchangeTransaction transaction = null)
        {
            return new ExchangeFeedback {
                runIdentity = session.Metadata.RunId.ToString(), nightOrdinal = entry.ActiveNightOrdinal, runTick = tick,
                selectionEpisode = partnerExchangeSelectionEpisode.ToString(), direction = partnerExchangeDirection.ToString(),
                resource = partnerExchangeResource.ToString(),
                result = result, transactionId = transaction == null ? null : transaction.TransactionId.ToString(),
                amount = transaction == null ? 0 : transaction.Amount,
                playerReserveBefore = assaultRifleReserve, playerReserveAfter = assaultRifleReserve,
                partnerStockBefore = session.RuntimePartner.ArAmmo, partnerStockAfter = session.RuntimePartner.ArAmmo };
        }

        void RecordExchangeSelection(M4Session session, int tick, string result, ExchangeTransaction transaction = null)
        {
            EnsureExchangeFeedback(session);
            latestExchangeSelection = CaptureExchangeFeedback(session, tick, result, transaction);
        }

        void DiscardExchangeSelection(string reason)
        {
            if (partnerExchangeBeginRequested && latestExchangeSelection != null && latestExchangeSelection.result == "선택 대기 / QUEUED")
                latestExchangeSelection.result = reason;
        }

        string ExportPartnerDiagnostics(M4Session session)
        {
            if (session == null) return "{\"exchange\":{\"result\":\"canonical session unavailable\"}}";
            EnsureExchangeFeedback(session);
            M4PlayerResourceInventory resources = session.ReadPlayerResourceInventory();
            var exchange = new ExchangeFeedbackSnapshot {
                runIdentity = session.Metadata.RunId.ToString(), runTick = session.RuntimeRun.FixedTick,
                playerArMagazine = assaultRifleMagazine, playerArReserve = assaultRifleReserve,
                partnerArStock = session.RuntimePartner.ArAmmo, partnerParts = session.RuntimePartner.Parts, latestSelection = latestExchangeSelection,
                playerPartsAvailability = resources == null ? "UNAVAILABLE" : resources.parts == 0 ? "KNOWN_EMPTY" : "AVAILABLE",
                playerResourceInventory = resources,
                lastCompletedTransfer = lastExchangeTransfer, retainedTransaction = session.ReadPartner().PendingExchange };
            if(qaEnableManualTraversal&&manualTraversal!=null)
            {
                var view=manualTraversal.Snapshot;
                var admission=ReadManualEntryReadiness(ManualEntryStage.Preview);
                exchange.manualTraversal=new ManualTraversalFeedback {actualPose=playerTransform.position.ToString("R"),
                    actualZone=session.RuntimeRun.PlayerZone.ToString(),canonicalTraversal=session.RuntimeRun.PlayerTraversal.ToString(),
                    state=view.State.ToString(),lastPassedNode=view.LastPassedNode.ToString(),elapsedTicks=view.ElapsedTicks,
                    totalTicks=view.TotalTicks,interruptedRoute=manualTraversal.HasInterruptedManualRoute,receipt=manualTraversalReceipt,
                    domainAbortReason=manualTraversal.LastManualAbortReason,entryEligible=admission.Eligible,
                    entryAscending=admission.Entry.Ascending,entryBlockedReason=admission.Reason,arrival=manualLastArrival,
                    actionReadiness=ReadManualActionReadiness(admission)};
            }
            string feedbackJson = JsonUtility.ToJson(exchange, true);
            // Preserve every existing movement root field for existing diagnostic consumers.
            string movementJson = partnerMovement == null ? "{}" : partnerMovement.ExportMovementDiagnostics(session).TrimEnd();
            if (movementJson.EndsWith("}", StringComparison.Ordinal))
                return movementJson.Substring(0, movementJson.Length - 1) +
                    (movementJson.Trim() == "{}" ? "" : ",") + "\n\"exchange\":" + feedbackJson + "\n}";
            return "{\"exchange\":" + feedbackJson + "}";
        }

        Rect PartnerExchangePanelRect() => new Rect(Mathf.Max(16f, Screen.width - 456f), 16f, 440f,
            Mathf.Min(480f, Mathf.Max(160f, Screen.height - 32f)));

        void DrawPartnerExchangeMenu()
        {
            M4Session session = entry.CanonicalSessionForPlayerAction;
            if (session == null) return;
            EnsureExchangeFeedback(session);
            PartnerRuntimeState view = session.ReadPartner();
            ExchangeTransaction transaction = view.PendingExchange;
            if (!partnerExchangeMenuOpen && transaction == null && latestExchangeSelection == null && lastExchangeTransfer == null) return;
            bool wasEnabled = GUI.enabled;
            // Merely aiming at a Face candidate must not click through to transaction cancellation.
            bool exchangeControlsEnabled = wasEnabled && entry.FixedTickRunning && partnerQMode != PartnerQMode.Face &&
                (!partnerExchangeMenuOpen || partnerQArmed);
            GUI.enabled = wasEnabled; // Scrolling and copying evidence also work during Owner pause.
            GUILayout.BeginArea(PartnerExchangePanelRect(), GUI.skin.box);
            partnerExchangeScroll = GUILayout.BeginScrollView(partnerExchangeScroll);
            GUILayout.Label("RallyExchange: AR / Parts");
            GUILayout.Label("현재 AR 예비탄 " + assaultRifleReserve + " | Partner 보유탄 " + view.ArAmmo);
            GUILayout.Label("교환은 AR 예비탄을 옮깁니다. 탄창 장전은 R입니다.");
            M4PlayerResourceInventory resources = session.ReadPlayerResourceInventory();
            GUILayout.Label(resources == null ? "Parts: UNAVAILABLE (explicit Player inventory not supplied)" :
                "현재 Parts Player " + resources.parts + " | Partner " + view.Parts);
            if (latestExchangeSelection != null)
                GUILayout.Label("최근 선택 " + latestExchangeSelection.resource + " " + latestExchangeSelection.direction + " · " + latestExchangeSelection.result +
                    (latestExchangeSelection.transactionId == null ? " · 새 거래 없음" : " · 연결 Tx " + latestExchangeSelection.transactionId));
            if (lastExchangeTransfer != null)
                GUILayout.Label("마지막 실제 이전 Tx " + lastExchangeTransfer.transactionId + " " + lastExchangeTransfer.resource + " " + lastExchangeTransfer.direction +
                    " x" + lastExchangeTransfer.amount + "\nPlayer " + lastExchangeTransfer.transferredResourcePlayerBefore + " → " + lastExchangeTransfer.transferredResourcePlayerAfter +
                    " | Partner " + lastExchangeTransfer.transferredResourcePartnerBefore + " → " + lastExchangeTransfer.transferredResourcePartnerAfter);
            // Evidence copying remains available during Owner pause; it issues no gameplay input.
            if (GUILayout.Button("Copy exchange + movement diagnostics (read-only)"))
                GUIUtility.systemCopyBuffer = ExportPartnerDiagnostics(session);
            GUI.enabled = exchangeControlsEnabled;
            GUILayout.Label("Escape pauses / resumes; active exchange retains progress.");
            GUILayout.Label("Partner role: " + view.Role + " | " + (view.ExchangeFlow == null ? "" : view.ExchangeFlow.returnReason));
            GUILayout.Label(partnerExchangeReceipt);
            if (transaction != null)
                GUILayout.Label((latestExchangeSelection != null && latestExchangeSelection.result == "접수 / ACCEPTED" &&
                    latestExchangeSelection.transactionId == transaction.TransactionId.ToString()
                    ? "최근 선택과 연결된 거래 Tx " : "보존된 이전 거래 Tx ") + transaction.TransactionId + " " + transaction.Resource + " " + transaction.Direction + " x" + transaction.Amount +
                    " | " + (transaction.Completed ? "COMPLETED" : transaction.Cancelled ? "CANCELLED: " + transaction.CancellationReason :
                    "PENDING until runTick " + transaction.CompletionTick));
            if (partnerExchangeMenuOpen)
            {
                GUILayout.Label("Hold Q while choosing direction. Release Q closes menu without transfer.");
                if (!partnerQArmed) GUILayout.Label("Selection disarmed: release Q, then press Q again.");
                if (GUILayout.Button("Give Partner AR (30 or remaining)")) QueuePartnerExchangeBegin(ExchangeDirection.PlayerToPartner);
                if (GUILayout.Button("Receive Partner AR (30 or remaining)")) QueuePartnerExchangeBegin(ExchangeDirection.PartnerToPlayer);
                GUI.enabled = exchangeControlsEnabled && resources != null;
                if (GUILayout.Button("Give Partner Parts (1)")) QueuePartnerExchangeBegin(ExchangeDirection.PlayerToPartner, ExchangeResource.Parts);
                if (GUILayout.Button("Receive Partner Parts (1)")) QueuePartnerExchangeBegin(ExchangeDirection.PartnerToPlayer, ExchangeResource.Parts);
                GUI.enabled = exchangeControlsEnabled;
            }
            if (transaction != null && !transaction.Completed && !transaction.Cancelled && GUILayout.Button("Cancel pending exchange"))
            { partnerExchangeCancelRequested = true; partnerExchangeCancelTransactionId = transaction.TransactionId; shotRequested = false; suppressMenuShotThroughFrame = Time.frameCount; }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = wasEnabled;
        }

        bool PartnerExchangeUiOwnsPointer()
        {
            var session = entry.CanonicalSessionForPlayerAction;
            if (session == null || (!partnerExchangeMenuOpen && session.RuntimePartner.PendingExchange == null &&
                latestExchangeSelection == null && lastExchangeTransfer == null)) return false;
            Vector2 point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return PartnerExchangePanelRect().Contains(point);
        }

        void QueuePartnerExchangeBegin(ExchangeDirection direction, ExchangeResource resource = ExchangeResource.ArAmmo)
        {
            if (!entry.FixedTickRunning || !partnerQArmed || partnerQMode != PartnerQMode.Exchange || !Input.GetKey(KeyCode.Q)) return;
            partnerExchangeDirection = direction;
            partnerExchangeResource = resource;
            M4Session session = entry.CanonicalSessionForPlayerAction;
            if (session != null) RecordExchangeSelection(session, session.RuntimeRun.FixedTick, "선택 대기 / QUEUED");
            partnerExchangeBeginRequested = true;
            shotRequested = false;
            suppressMenuShotThroughFrame = Time.frameCount;
        }

        void ProcessPartnerExchange(M4ThreeNightQaFixedTickContext context, bool completionBoundary = false)
        {
            M4Session session = entry.CanonicalSessionForPlayerAction;
            if (session == null) return;
            EnsureExchangeFeedback(session);
            PartnerRuntimeState partner = session.RuntimePartner;
            var controller = new PartnerController(partner);
            ExchangeTransaction transaction = partner.PendingExchange;
            bool activeExchange = M4PartnerExchangePersistence.Active(partner);
            bool admitted = TryAdmitPartnerExchange(session, context.Run, out string reason, activeExchange, context.RunFixedTick);
            ulong[] partnerIds = session.ReadTopology().Bindings.Where(binding => binding.Role == M4BindingRole.Partner)
                .Select(binding => binding.EntityId.Value).ToArray();
            ulong partnerId = partnerIds.Length == 1 ? partnerIds[0] : 0;
            DamageableSnapshot playerHealth = default(DamageableSnapshot);
            DamageableSnapshot latestPartnerHealth = default(DamageableSnapshot);
            bool hasActorHealth = partnerId != 0 && context.Run.TryGetDamageable(new EntityId(PlayerEntityValue), out playerHealth) &&
                context.Run.TryGetDamageable(new EntityId(partnerId), out latestPartnerHealth);
            if (transaction != null && !transaction.Completed && !transaction.Cancelled)
            {
                bool explicitCancel = partnerExchangeCancelRequested && partnerExchangeCancelTransactionId == transaction.TransactionId;
                bool contextMatches = transaction.RunIdentity == session.Metadata.RunId.ToString() &&
                    transaction.NightOrdinal == entry.ActiveNightOrdinal && transaction.TargetPartnerEntityValue == partnerId &&
                    transaction.TicksPerSecond == 50 && partner.Role == PartnerRole.RallyExchange && partner.ExchangeFlow != null &&
                    partner.ExchangeFlow.policyIdentity == partnerExchangePolicyIdentity;
                if (explicitCancel || !admitted || !contextMatches || !hasActorHealth ||
                    playerHealth.HitPoints != transaction.PlayerHitPointsAtBegin || latestPartnerHealth.HitPoints != transaction.PartnerHitPointsAtBegin)
                {
                    controller.CancelExchange(context.RunFixedTick, explicitCancel ? "user cancelled" :
                        !admitted ? reason : "target/context/actor damage changed");
                    partnerExchangeReceipt = "Cancelled: resources unchanged.";
                }
            }
            partnerExchangeCancelRequested = false;
            partnerExchangeCancelTransactionId = 0;
            if (partnerExchangeBeginRequested)
            {
                partnerExchangeBeginRequested = false;
                bool manual = !playingReplay;
#if UNITY_EDITOR
                manual = manual && !deterministicInputPlanCapture && !playingInputPlan && !playingAdaptiveQa;
#endif
                if (!manual || !admitted || !hasActorHealth || partnerExchangeInputRunIdentity != session.Metadata.RunId.ToString() ||
                    partnerExchangeInputNight != entry.ActiveNightOrdinal)
                {
                    partnerExchangeReceipt = "Exchange rejected: " + (!admitted ? reason : "manual target/context unavailable");
                    RecordExchangeSelection(session, context.RunFixedTick, partnerExchangeReceipt);
                }
                else
                {
                    try
                    {
                        ulong previousTransaction = partner.PendingExchange == null ? 0 : partner.PendingExchange.TransactionId;
                        if (partnerExchangeResource == ExchangeResource.Parts)
                            session.BeginPlayerPartsExchange(partnerExchangeDirection, context.RunFixedTick, partnerId,
                                partner.CurrentNodeId ?? "ACTUAL_POSE", partnerExchangeSelectionEpisode, partnerExchangePolicyIdentity);
                        else
                            controller.BeginExchange(partner.NextExchangeTransactionId, ExchangeResource.ArAmmo, partnerExchangeDirection,
                                30, context.RunFixedTick, assaultRifleReserve, 0, 50, partnerId, session.Metadata.RunId.ToString(),
                                entry.ActiveNightOrdinal, partner.CurrentNodeId ?? "ACTUAL_POSE", partnerExchangeSelectionEpisode, partnerExchangePolicyIdentity);
                        bool newTransaction = partner.PendingExchange != null && partner.PendingExchange.TransactionId != previousTransaction;
                        if (!newTransaction)
                        {
                            partnerExchangeReceipt = "Selection NO-OP: existing transaction/terminal episode retained.";
                            RecordExchangeSelection(session, context.RunFixedTick, partnerExchangeReceipt, partner.PendingExchange);
                            return;
                        }
                        partner.PendingExchange.PlayerHitPointsAtBegin = playerHealth.HitPoints;
                        partner.PendingExchange.PartnerHitPointsAtBegin = latestPartnerHealth.HitPoints;
                        if (recordingReplay) InvalidateReplayCapture("manual RallyExchange accepted; current tape has no exchange command; export unavailable", expectedManualInput: true);
                        partnerExchangeReceipt = "Accepted Tx " + partner.PendingExchange.TransactionId + " x" + partner.PendingExchange.Amount +
                            "; no resources move before runTick " + partner.PendingExchange.CompletionTick;
                        RecordExchangeSelection(session, context.RunFixedTick, "접수 / ACCEPTED", partner.PendingExchange);
                        if (partnerAcknowledgementCue != null) partnerAcknowledgementCue.Emit();
                    }
                    catch (ArgumentException exception) { partnerExchangeReceipt = "Exchange rejected: " + exception.Message; RecordExchangeSelection(session, context.RunFixedTick, partnerExchangeReceipt); }
                    catch (InvalidOperationException exception) { partnerExchangeReceipt = "Exchange rejected: " + exception.Message; RecordExchangeSelection(session, context.RunFixedTick, partnerExchangeReceipt); }
                    catch (OverflowException) { partnerExchangeReceipt = "Exchange rejected: clock/identity capacity unavailable"; RecordExchangeSelection(session, context.RunFixedTick, partnerExchangeReceipt); }
                }
            }
            transaction = partner.PendingExchange;
            if (completionBoundary && transaction != null && !transaction.Completed && !transaction.Cancelled)
            {
                // AR retains its original ref path. Parts may only use Session-owned
                // inventory; never complete a Parts transaction against dummy zero.
                int unusedParts = 0;
                int playerBefore = assaultRifleReserve, partnerBefore = partner.ArAmmo;
                bool partsTransaction = transaction.Resource == ExchangeResource.Parts;
                M4PlayerResourceInventory partsBefore = partsTransaction ? session.ReadPlayerResourceInventory() : null;
                int resourcePlayerBefore = partsTransaction ? (partsBefore == null ? 0 : partsBefore.parts) : playerBefore;
                int resourcePartnerBefore = partsTransaction ? partner.Parts : partnerBefore;
                bool transferred = partsTransaction ? session.CompletePlayerPartsExchange(context.RunFixedTick) :
                    controller.CompleteExchange(context.RunFixedTick, ref assaultRifleReserve, ref unusedParts);
                if (transferred)
                {
                    // Capture only a real successful transfer, directly around the original ref call.
                    lastExchangeTransfer = CaptureExchangeFeedback(session, context.RunFixedTick, "COMPLETED", transaction);
                    lastExchangeTransfer.runIdentity = transaction.RunIdentity;
                    lastExchangeTransfer.direction = transaction.Direction.ToString();
                    lastExchangeTransfer.nightOrdinal = transaction.NightOrdinal;
                    // Latest episode associated with this transaction at completion, not its original issue episode.
                    lastExchangeTransfer.selectionEpisode = partner.ExchangeFlow == null ? null : partner.ExchangeFlow.lastSelectionEpisode.ToString();
                    lastExchangeTransfer.playerReserveBefore = playerBefore; lastExchangeTransfer.partnerStockBefore = partnerBefore;
                    lastExchangeTransfer.resource = transaction.Resource.ToString();
                    lastExchangeTransfer.transferredResourcePlayerBefore = resourcePlayerBefore;
                    lastExchangeTransfer.transferredResourcePartnerBefore = resourcePartnerBefore;
                    lastExchangeTransfer.transferredResourcePlayerAfter = partsTransaction ? session.ReadPlayerResourceInventory().parts : assaultRifleReserve;
                    lastExchangeTransfer.transferredResourcePartnerAfter = partsTransaction ? partner.Parts : partner.ArAmmo;
                    partnerExchangeReceipt = partsTransaction ? "Committed Parts Tx " + transaction.TransactionId + " (one transfer)" :
                        "Committed Tx " + transaction.TransactionId + ": Player AR " + assaultRifleReserve +
                        ", Partner AR " + partner.ArAmmo + " (one transfer)";
                }
                else if (transaction.Cancelled) partnerExchangeReceipt = "Cancelled: " + transaction.CancellationReason + "; resources unchanged.";
            }
            if (completionBoundary && partner.ExchangeFlow != null && partner.ExchangeFlow.suspended && !M4PartnerExchangePersistence.Active(partner))
            {
                var f = partner.ExchangeFlow;
                bool validFace = (f.priorFaceId == "A" && session.ReadTopology().ValidFaces.Contains(FaceId.FaceA)) ||
                    (f.priorFaceId == "B" && session.ReadTopology().ValidFaces.Contains(FaceId.FaceB)) ||
                    (f.priorFaceId == "C" && session.ReadTopology().ValidFaces.Contains(FaceId.FaceC));
                bool valid = session.Stage == M4SessionStage.Defense && context.Run.Phase == RunPhase.Defense &&
                    context.Run.Tower == TowerState.Stable && context.Run.PlayerLife == PlayerLifeState.Alive &&
                    validFace && controller.ObserveCoverEquipment(false).CoreArContractKnown && partnerMovement != null && partnerMovement.CanResumeExchangeCover(session);
                controller.ResumePriorCover(context.RunFixedTick, valid, "prior Cover/life/Face/ammo/route unavailable; holding actual pose");
                if (f.priorRole == PartnerRole.CoverFace && partner.Role == PartnerRole.None && partner.ArAmmo == 0 && session.Stage == M4SessionStage.Defense &&
                    context.Run.PlayerLife == PlayerLifeState.Alive && context.Run.Tower == TowerState.Stable && partner.Life == PartnerLifeState.Healthy)
                { controller.EndCoverIfEmpty("S_0"); if (partnerMovement != null) partnerMovement.Begin(session, context.RunFixedTick); }
            }
        }

        void IM4PartnerExchangeCompletionSink.ProcessExchangeCompletionBoundary(int completedRunTick)
        {
            M4Session session = entry.CanonicalSessionForPlayerAction; // Observe can swap the canonical instance.
            if (session == null) return;
            var context = new M4ThreeNightQaFixedTickContext(completedRunTick, session.NightElapsedTick.GetValueOrDefault(-1),
                session.RuntimeRun, session.BoundDirector, session.RuntimePool, session.RuntimeRunners);
            ProcessPartnerExchange(context, true);
            PersistPlayerWeaponActionState(session.RuntimeRun.FixedTick);
        }

        void UpdatePlayerMovementAndLook()
        {
            if(qaEnableManualTraversal){UpdateManualGroundOrUpperMovement();return;}
            float moveSpeed = Input.GetKey(KeyCode.LeftShift) ? 7f : 4f;
            Vector3 horizontal = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (horizontal.sqrMagnitude > 1f) horizontal.Normalize();
            Vector3 proposedOrigin = playerTransform.position + playerTransform.TransformDirection(horizontal) * (moveSpeed * Time.deltaTime);
            proposedOrigin.y = TowerGeometryFixture.UpperDeckY + 0.35f;
            // This manual QA gate bounds the actor origin only. It does not supply
            // a body collider, walls, railings, traversal, or an effective-input replay gate.
            ZoneVolumeDescriptor? upperZone = null;
            bool duplicateUpperZone = false;
            if (runtimeGeometry != null)
                foreach (ZoneVolumeDescriptor zone in runtimeGeometry.Zones)
                    if (zone.Zone == LocationZone.UpperSafeZone)
                    {
                        if (upperZone.HasValue) { duplicateUpperZone = true; break; }
                        upperZone = zone;
                    }
            if (!upperZone.HasValue || duplicateUpperZone)
                manualUpperOriginReceipt = "Manual movement unavailable: unique UpperSafeZone missing.";
            else if (!IsFinitePartnerAim(playerTransform.position) || !upperZone.Value.Contains(playerTransform.position))
                manualUpperOriginReceipt = "Manual movement unavailable: current origin outside UpperSafeZone; no automatic recovery.";
            else if (!IsFinitePartnerAim(proposedOrigin))
                manualUpperOriginReceipt = "Manual movement unavailable: proposed origin is nonfinite.";
            else if (!upperZone.Value.Contains(proposedOrigin))
            {
                if (TryProjectManualUpperOrigin(upperZone.Value, playerTransform.position, proposedOrigin, out Vector3 boundedOrigin))
                {
                    playerTransform.position = boundedOrigin;
                    manualUpperOriginReceipt = "Manual origin slides along UpperSafeZone boundary; body/wall/railing collision pending.";
                }
                else manualUpperOriginReceipt = "Manual movement unavailable: boundary projection requires a valid solid cylindrical UpperSafeZone.";
            }
            else
            {
                playerTransform.position = proposedOrigin;
                manualUpperOriginReceipt = "Manual UpperSafeZone origin bounded; body/wall/railing collision pending.";
            }
            playerTransform.Rotate(0f, Input.GetAxis("Mouse X") * 3f, 0f, UnityEngine.Space.Self);
            cameraPitchDegrees = Mathf.Clamp(cameraPitchDegrees - Input.GetAxis("Mouse Y") * 3f, -80f, 80f);
            playerCamera.transform.localRotation = Quaternion.Euler(cameraPitchDegrees, 0f, 0f);
            playerCamera.fieldOfView = partnerQMode == PartnerQMode.None && !partnerMouse1Blocked && Input.GetMouseButton(1) ? 55f : 70f;
        }

        ManualEntryReadiness ReadManualEntryReadiness(ManualEntryStage stage)
        {
            var physical=new ManualTraversalEntryEvaluation(false,false,-1,"입구를 확인할 수 없습니다");
            if(manualTraversal!=null&&playerTransform!=null)physical=manualTraversal.EvaluateManualEntry(playerTransform.position);
            M4Session session=entry==null?null:entry.CanonicalSessionForPlayerAction;
            string blocked=null;
            if(!qaEnableManualTraversal||!started||session==null||manualTraversal==null||playerCamera==null||!playerCamera.isActiveAndEnabled||
                !IsFinitePartnerAim(playerCamera.transform.position)||!IsFinitePartnerAim(playerCamera.transform.forward))
                blocked="게임 시작 후 입구를 확인하세요";
            else if(!isActiveAndEnabled||!manualFocused)blocked="게임 화면을 선택하세요";
            else if(entry.OwnerPaused)blocked="일시정지 중";
            else if(!entry.FixedTickRunning||session.Stage!=M4SessionStage.Defense||session.RuntimeRun.Phase!=RunPhase.Defense)
                blocked="현재는 이동을 시작할 수 없습니다";
            else if(session.RuntimeRun.PlayerLife!=PlayerLifeState.Alive||session.RuntimeRun.Tower!=TowerState.Stable)
                blocked="현재 상태에서는 이동할 수 없습니다";
            else if(playingReplay)blocked="재생 중에는 직접 이동할 수 없습니다";
#if UNITY_EDITOR
            else if(deterministicInputPlanCapture||playingInputPlan||playingAdaptiveQa)blocked="현재는 직접 이동할 수 없습니다";
#endif
            else if(manualTraversal.Snapshot.State!=TraversalState.None||session.RuntimeRun.PlayerTraversal!=TraversalState.None)
                blocked="이동 중입니다";
            else if(Time.frameCount<=manualArrivalFrame)blocked="도착 확인 중";
            else if(partnerExchangeMenuOpen||partnerQMode!=PartnerQMode.None||
                (Cursor.lockState!=CursorLockMode.Locked&&PartnerExchangeUiOwnsPointer()))blocked="파트너 메뉴 선택 중";
            if(blocked!=null)return new ManualEntryReadiness(physical,false,false,blocked);
            if(IsReloadActive||reloadRequested)return new ManualEntryReadiness(physical,false,true,"재장전 중");
            if(stage==ManualEntryStage.FixedPending)
            {
                // This intent already proves a fresh down. E-up and armed=false
                // cannot discard it; only latest domain/entry gates may reject it.
                if(!manualERequested)return new ManualEntryReadiness(physical,false,true,"새 E 입력이 필요합니다");
            }
            else
            {
                if(manualERequested)return new ManualEntryReadiness(physical,false,false,"이동을 시작하는 중");
                bool fresh=stage==ManualEntryStage.Enqueue?manualFreshEFrame==Time.frameCount:manualEArmed;
                if(!fresh)return new ManualEntryReadiness(physical,false,true,"E 키를 놓고 다시 누르세요");
            }
            return new ManualEntryReadiness(physical,physical.Eligible,true,physical.Reason);
        }

        string ReadManualActionReadiness(ManualEntryReadiness admission)
        {
            if(!admission.ControlsOpen)return admission.Reason;
            if(IsReloadActive||reloadRequested)return "재장전 중 · 이동과 시점 조작 가능";
            if(partnerMouse0Blocked||partnerMouse1Blocked||Input.GetKey(KeyCode.Q))return "버튼을 놓고 새로 입력하세요";
            if(manualTraversal.HasInterruptedManualRoute&&manualTraversal.Snapshot.Zone==LocationZone.LowerAccess)
                return "복귀 발판 · E로 남은 경로 이동";
            if(Magazine(currentWeapon)<=0)return "탄창이 비었습니다";
            var session=entry.CanonicalSessionForPlayerAction;
            if(session!=null&&(session.RuntimeRun.FixedTick<=reloadShotBlockedThroughRunTick||
                session.RuntimeRun.FixedTick<NextLegalShotRunTick(currentWeapon)))return "사격 준비 중 · 이동과 시점 조작 가능";
            return "이동·시점·새 입력 가능";
        }

        void DrawManualTraversalCue()
        {
            if(manualTraversal==null||playerCamera==null||!playerCamera.isActiveAndEnabled)return;
            var admission=ReadManualEntryReadiness(ManualEntryStage.Preview);
            var view=manualTraversal.Snapshot;
            if(manualCueStyle==null)manualCueStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=18,wordWrap=true};
            if(manualArrivalStyle==null)manualArrivalStyle=new GUIStyle(manualCueStyle){fontSize=14};
            Color previous=GUI.color;
            bool moving=view.State!=TraversalState.None;
            bool footprintVisible=false;
            if(!moving&&manualFocused&&!entry.OwnerPaused&&entry.FixedTickRunning&&admission.Entry.FootprintNode>=0&&
                !partnerExchangeMenuOpen&&partnerQMode==PartnerQMode.None)
                footprintVisible=DrawManualEntryOutline(admission.Entry.FootprintNode,admission.Eligible?Color.green:Color.yellow);
            // Pure presentation duration; never a command cooldown or readiness gate.
            bool showArrival=!moving&&!string.IsNullOrEmpty(manualLastArrival)&&Time.unscaledTime<manualArrivalCueUntil;
            if(!moving&&!manualERequested&&!admission.Eligible&&!footprintVisible&&!showArrival)return;
            float y=Screen.height*0.5f+36f;
            string cue=moving?(entry.OwnerPaused?"일시정지":view.State==TraversalState.Ascending?"올라가는 중":"내려가는 중"):
                admission.Eligible?(admission.Entry.IsRecovery?"[E] 남은 경로 이동":admission.Entry.Ascending?"[E] 올라가기":"[E] 내려가기"):admission.Reason;
            GUI.color=admission.Eligible?Color.green:Color.white;
            GUI.Label(new Rect(Screen.width*0.5f-220f,y,440f,32f),cue,manualCueStyle);
            if(moving)
            {
                GUI.color=new Color(0f,0f,0f,0.65f);GUI.DrawTexture(new Rect(Screen.width*0.5f-90f,y+33f,180f,5f),Texture2D.whiteTexture);
                GUI.color=Color.cyan;GUI.DrawTexture(new Rect(Screen.width*0.5f-90f,y+33f,180f*view.NormalizedProgress,5f),Texture2D.whiteTexture);
            }
            else if(showArrival)
            {
                string action=ReadManualActionReadiness(admission);
                if(manualArrivalNoticeFact!=manualLastArrival||manualArrivalNoticeAction!=action)
                {manualArrivalNoticeFact=manualLastArrival;manualArrivalNoticeAction=action;manualArrivalNoticeText=manualLastArrival+" | "+action;}
                GUI.color=Color.white;GUI.Label(new Rect(Screen.width*0.5f-270f,y+30f,540f,36f),manualArrivalNoticeText,manualArrivalStyle);
            }
            GUI.color=previous;
        }

        bool DrawManualEntryOutline(int node,Color color)
        {
            var access=runtimeGeometry.ManualAccess;
            float minX=float.PositiveInfinity,minY=float.PositiveInfinity,maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
            // Do not project another layer or a footprint behind the camera.
            for(int corner=0;corner<4;corner++)
            {
                Vector3 screen=playerCamera.WorldToScreenPoint(access.EntryCorner(node,corner));
                if(!IsFinitePartnerAim(screen)||screen.z<=playerCamera.nearClipPlane)return false;
                minX=Mathf.Min(minX,screen.x);maxX=Mathf.Max(maxX,screen.x);minY=Mathf.Min(minY,screen.y);maxY=Mathf.Max(maxY,screen.y);
            }
            if(maxX<0f||maxY<0f||minX>Screen.width||minY>Screen.height)return false;
            Matrix4x4 matrix=GUI.matrix;Color previous=GUI.color;GUI.color=color;
            for(int corner=0;corner<4;corner++)
            {
                Vector3 a=playerCamera.WorldToScreenPoint(access.EntryCorner(node,corner));
                Vector3 b=playerCamera.WorldToScreenPoint(access.EntryCorner(node,(corner+1)%4));
                Vector2 from=new Vector2(a.x,Screen.height-a.y),to=new Vector2(b.x,Screen.height-b.y);
                GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg,from);
                GUI.DrawTexture(new Rect(from.x,from.y,(to-from).magnitude,2f),Texture2D.whiteTexture);
                GUI.matrix=matrix;
            }
            GUI.color=previous;
            return true;
        }

        void ClearManualTraversalLatch()
        {manualERequested=false;manualEArmed=false;manualFreshEFrame=-1;}

        void BlockManualTraversalActions()
        {
            StopManualTrigger();reloadRequested=false;
            partnerCoverRequested=false;partnerExchangeBeginRequested=false;partnerExchangeOpenRequested=false;
            if(partnerQMode!=PartnerQMode.None)EndPartnerQ(false);
            partnerMouse0Blocked=true;partnerMouse1Blocked=true;
        }

        void CloseManualTraversalDomain(string reason)
        {
            ClearManualTraversalLatch();
            if(manualTraversal==null||manualTraversal.Snapshot.State==TraversalState.None)return;
            manualTraversal.AbortManualForDomain(reason);
            playerTransform.position=manualTraversal.Snapshot.Position;
            var session=entry.CanonicalSessionForPlayerAction;
            if(session!=null)SyncManualTraversalZone(session.RuntimeRun);
            manualTraversalReceipt="Traversal domain abort: "+reason+"; returned to "+manualTraversal.Snapshot.LastPassedNode+
                " at "+playerTransform.position.ToString("R")+". No damage generated.";
        }

        void SyncManualTraversalZone(RunState run)
        {
            if(runtimeGeometry.ManualAccess.TryClassify(playerTransform.position,runtimeGeometry.Zones,out LocationZone actual))
                run.PlayerZone=actual;
            else manualTraversalReceipt="Traversal UNAVAILABLE: actual origin has no authored volume.";
            run.PlayerTraversal=manualTraversal.Snapshot.State;
        }

        void ProcessManualTraversal(M4ThreeNightQaFixedTickContext context)
        {
            if(manualTraversal==null)return;
            if(!isActiveAndEnabled){CloseManualTraversalDomain("controller inactive at fixed boundary");return;}
            if(context.Run.PlayerLife!=PlayerLifeState.Alive||context.Run.Phase!=RunPhase.Defense||context.Run.Tower!=TowerState.Stable)
            {CloseManualTraversalDomain("life="+context.Run.PlayerLife+", phase="+context.Run.Phase+", tower="+context.Run.Tower);return;}
            bool blockedTick=manualTraversal.Snapshot.State!=TraversalState.None;
            if(blockedTick)manualERequested=false; // no repeated or reverse command can wait behind an accepted traversal
            if(manualERequested)
            {
                var admission=ReadManualEntryReadiness(ManualEntryStage.FixedPending);
                manualERequested=false;
                if(!admission.Eligible)
                    manualTraversalReceipt="E 거절: "+admission.Reason;
                else
                {
                        var result=manualTraversal.BeginManualNormal(playerTransform.position,admission.Entry.Ascending);
                        manualTraversalReceipt="Normal traversal: "+result;
                        if(result==TraversalCommandStatus.Started)
                        {
                            manualLastArrival=null;
                            blockedTick=true;manualTraversalUsed=true;
                            if(recordingReplay)InvalidateReplayCapture("manual normal traversal accepted; tape/whole actor route restore unavailable",expectedManualInput:true);
                        }
                }
            }
            if(manualTraversal.Snapshot.State!=TraversalState.None)
            {
                bool ascending=manualTraversal.Snapshot.State==TraversalState.Ascending;
                var step=manualTraversal.AdvanceOneTick(true,false); // accepted command, independent of key/focus; no invented large-damage source
                var view=manualTraversal.Snapshot;
                playerTransform.position=view.Position;
                if(step==TraversalStepStatus.Completed)
                {manualArrivalFrame=Time.frameCount;manualERequested=false;manualLastArrival=ascending?"상부 도착 · 이동 완료":"지상 도착 · 이동 완료";
                    manualArrivalCueUntil=Time.unscaledTime+1.5f;}
                manualTraversalReceipt="Tap traversal "+step+" | "+view.State+" | "+view.ElapsedTicks+"/"+view.TotalTicks+
                    " | passed "+view.LastPassedNode;
            }
            if(blockedTick)BlockManualTraversalActions();
            SyncManualTraversalZone(context.Run);
        }

        void UpdateManualLook()
        {
            playerTransform.Rotate(0f,Input.GetAxis("Mouse X")*3f,0f,UnityEngine.Space.Self);
            cameraPitchDegrees=Mathf.Clamp(cameraPitchDegrees-Input.GetAxis("Mouse Y")*3f,-80f,80f);
            playerCamera.transform.localRotation=Quaternion.Euler(cameraPitchDegrees,0f,0f);
            playerCamera.fieldOfView=manualTraversal.Snapshot.State==TraversalState.None&&partnerQMode==PartnerQMode.None&&
                !partnerMouse1Blocked&&Input.GetMouseButton(1)?55f:70f;
        }

        void UpdateManualGroundOrUpperMovement()
        {
            Vector3 prior=playerTransform.position;
            if(runtimeGeometry.ManualAccess.TryClassify(prior,runtimeGeometry.Zones,out LocationZone actual)&&actual!=LocationZone.LowerAccess)
            {
                Vector3 input=new Vector3(Input.GetAxisRaw("Horizontal"),0f,Input.GetAxisRaw("Vertical"));
                if(input.sqrMagnitude>1f)input.Normalize();
                Vector3 desired=prior+playerTransform.TransformDirection(input)*((Input.GetKey(KeyCode.LeftShift)?7f:4f)*Time.deltaTime);
                desired.y=prior.y;
                foreach(var volume in runtimeGeometry.Zones)
                    if(volume.Zone==actual)
                    {
                        if(actual==LocationZone.UpperSafeZone&&runtimeGeometry.ManualAccess.IsDeckOpening(desired))break;
                        if(volume.Contains(desired))playerTransform.position=desired;
                        else if(TryProjectManualUpperOrigin(volume,prior,desired,out Vector3 bounded)&&
                            (actual!=LocationZone.UpperSafeZone||!runtimeGeometry.ManualAccess.IsDeckOpening(bounded)))playerTransform.position=bounded;
                        break;
                    }
                if(!playerTransform.position.Equals(prior))manualTraversal.ObserveManualEndpointWalk(playerTransform.position);
                manualUpperOriginReceipt="Manual "+actual+" origin bounded; body/wall/enemy collision and full ground AI remain unverified.";
            }
            else manualUpperOriginReceipt="Access recovery landing: translation held; normal key release no longer stops movement. Camera look remains available.";
            UpdateManualLook();
        }

        static bool TryProjectManualUpperOrigin(ZoneVolumeDescriptor zone, Vector3 prior, Vector3 desired, out Vector3 bounded)
        {
            bounded = prior;
            if (zone.Shape != ZoneShapeKind.Cylinder || zone.InnerRadius != 0f ||
                !CenterFinite(zone.OuterRadius) || zone.OuterRadius <= 0f || !IsFinitePartnerAim(zone.Center) ||
                desired.y < zone.MinimumY || desired.y > zone.MaximumY)
                return false;
            double offsetX = (double)desired.x - zone.Center.x;
            double offsetZ = (double)desired.z - zone.Center.z;
            double length = Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0d) return false;
            double scale = zone.OuterRadius / length;
            Vector3 projected = new Vector3((float)(zone.Center.x + offsetX * scale), desired.y,
                (float)(zone.Center.z + offsetZ * scale));
            if (!IsFinitePartnerAim(projected)) return false;
            // Nearest-point projection onto a convex disk cannot increase the
            // requested displacement from an origin already inside that disk.
            double requestedDistanceSquared = ManualOriginDistanceSquared(prior, desired);
            if (zone.Contains(projected) && ManualOriginDistanceSquared(prior, projected) <= requestedDistanceSquared)
            { bounded = projected; return true; }
            // Casting a mathematical boundary point to float can round outside.
            // Fit the endpoint on the convex prior-to-projection chord using the
            // actual predicate, stopping when no distinct float midpoint exists.
            // No epsilon, inset, iteration budget, or gameplay margin is added.
            Vector3 inside = new Vector3(prior.x, desired.y, prior.z);
            Vector3 outside = projected;
            if (!zone.Contains(inside) || ManualOriginDistanceSquared(prior, inside) > requestedDistanceSquared) return false;
            while (true)
            {
                Vector3 midpoint = new Vector3((float)(((double)inside.x + outside.x) * 0.5d), desired.y,
                    (float)(((double)inside.z + outside.z) * 0.5d));
                if (midpoint.Equals(inside) || midpoint.Equals(outside))
                { bounded = inside; return true; }
                if (zone.Contains(midpoint) && ManualOriginDistanceSquared(prior, midpoint) <= requestedDistanceSquared)
                    inside = midpoint;
                else outside = midpoint;
            }
        }

        static double ManualOriginDistanceSquared(Vector3 from, Vector3 to)
        {
            double x = (double)to.x - from.x, y = (double)to.y - from.y, z = (double)to.z - from.z;
            return x * x + y * y + z * z;
        }

        void UpdateAimIndicator()
        {
            aimingAtActiveEnemy = false;
            if (playerCamera == null || !Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, WeaponRange(currentWeapon))) return;
            var enemy = M4EnemyWeakPointCollider.ResolveHitActor(hit.collider);
            aimingAtActiveEnemy = enemy != null && actors.TryGetValue(enemy.EntityId.Value, out ActiveEnemyActor active) && active == enemy;
        }

        void SetHitFeedback(string value, Color color, bool flash)
        {
            hitFeedback = value;
            hitFeedbackColor = color;
            hitFeedbackUntil = Time.unscaledTime + 0.65f;
            if (flash) hitFlashUntil = Time.unscaledTime + 0.16f;
        }

        void DrawCombatReadability()
        {
            Color prior = GUI.color;
            bool hitFlash = Time.unscaledTime < hitFlashUntil;
            GUI.color = hitFlash ? Color.red : aimingAtActiveEnemy ? Color.green : Color.white;
            GUI.Label(new Rect(Screen.width * 0.5f - 8f, Screen.height * 0.5f - 13f, 20f, 26f), "+");
            if (Time.unscaledTime < hitFeedbackUntil)
            {
                GUI.color = hitFlash ? Color.red : hitFeedbackColor;
                GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.5f + 19f, 240f, 24f), hitFeedback);
            }
            if (!string.IsNullOrEmpty(structureAttackFeedback))
            {
                GUI.color = Color.cyan;
                GUI.Label(new Rect(20f, Screen.height - 52f, Screen.width - 40f, 32f), structureAttackFeedback);
            }
            GUI.color = prior;
        }


        void AdvanceClimberStructureAttacks(M4ThreeNightQaFixedTickContext context)
        {
            M4TopologySnapshot topology = entry.CanonicalSessionForPlayerAction.ReadTopology();
            foreach (ActiveEnemyActor actor in actors.Values.OrderBy(value => value.EntityId.Value))
            {
                if (actor.Archetype != EnemyArchetype.Climber) continue;
                float? gap = null;
                if (actor.HasReachedAttackCapsuleStandOff && actor.TryMeasureCurrentAssignedGap(out float measured))
                    gap = measured;
                Vector3 joint = actor.AssignedJointCenter;
                bool visible = actor.HasReachedAttackCapsuleStandOff && HasUpperClimberSight(actor, joint);
                M4ClimberStructureAttackSnapshot result = new DamageResolver(context.Run).ApplyClimberStructureAttack(
                    context.Director, topology, actor.EntityId, actor.TicketId, actor.CanonicalActivationRunTick,
                    context.NightFixedTick, actor.HasReachedAttackCapsuleStandOff, gap, visible, joint);
                actor.RecordStructureAttack(result);
                if (result.ActualDamageApplied)
                {
                    DamageReceipt receipt = result.Receipt.Value;
                    FaceId face = FindTicketFace(context.Director, actor.TicketId);
                    structureAttackFeedback = "Climber " + face + " " + result.Layer + " " +
                        receipt.HitPointsBefore.ToString("0.##") + " -> " + receipt.HitPointsAfter.ToString("0.##") +
                        " | DamageEvent " + receipt.EventSequence + " tick " + result.RunFixedTick;
                    actor.GetComponent<M4ClimberStrikeFeedback>()?.Show(joint);
                }
                else if (actor.HasReachedAttackCapsuleStandOff &&
                    result.Outcome != "WAITING_FOR_VALID_DAMAGE_EVENT_PERIOD")
                    structureAttackFeedback = "Climber held: " + result.Outcome;
            }
        }

        bool HasUpperClimberSight(ActiveEnemyActor actor, Vector3 joint)
        {
            if (runtimeGeometry == null) return false;
            Vector3 body = actor.transform.position;
            foreach (StationId id in TowerLineOfSightContract.GetCanonicalStations(actor.TargetAttackPoint))
            {
                StationDescriptor? station = null;
                foreach (StationDescriptor candidate in runtimeGeometry.Stations)
                    if (candidate.Id == id && candidate.IsAvailable) { station = candidate; break; }
                if (station.HasValue && UpperLineClear(station.Value.EyePosition, body, actor) &&
                    UpperLineClear(station.Value.EyePosition, joint, actor)) return true;
            }
            return false;
        }

        bool UpperLineClear(Vector3 origin, Vector3 endpoint, ActiveEnemyActor actor)
        {
            Vector3 delta = endpoint - origin;
            if (delta.sqrMagnitude <= 0f) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
                ~0, QueryTriggerInteraction.Ignore))
            {
                Collider collider = hit.collider;
                if (collider == null || collider.transform.IsChildOf(actor.transform) || attackCapsules.Contains(collider)) continue;
                return false;
            }
            return true;
        }


        static EntityId StructureEntity(FaceId face, M4BindingRole layer)
            => new EntityId(0x4d341000UL + (ulong)face * 16UL + (ulong)layer);

        static M4EntityBinding[] BuildBindings()
        {
            var bindings = new List<M4EntityBinding> {
                new M4EntityBinding(new EntityId(PlayerEntityValue), M4BindingRole.Player, null),
                new M4EntityBinding(new EntityId(PartnerEntityValue), M4BindingRole.Partner, null)
            };
            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
                foreach (M4BindingRole layer in new[] { M4BindingRole.Barrier, M4BindingRole.NightBrace, M4BindingRole.Support })
                    bindings.Add(new M4EntityBinding(StructureEntity(face, layer), layer, face));
            return bindings.ToArray();
        }

        static DamageableFixture[] BuildSetup()
        {
            var setup = new List<DamageableFixture> {
                new DamageableFixture { EntityValue = PlayerEntityValue, Kind = DamageableKind.Player, HitPoints = 100f },
                new DamageableFixture { EntityValue = PartnerEntityValue, Kind = DamageableKind.Partner, HitPoints = 100f }
            };
            foreach (FaceId face in (FaceId[])Enum.GetValues(typeof(FaceId)))
                foreach (M4BindingRole layer in new[] { M4BindingRole.Barrier, M4BindingRole.NightBrace, M4BindingRole.Support })
                    setup.Add(new DamageableFixture { EntityValue = StructureEntity(face, layer).Value,
                        Kind = DamageableKind.Structure, HitPoints = layer == M4BindingRole.NightBrace ? 0f : 100f,
                        IsResolved = layer == M4BindingRole.NightBrace });
            return setup.ToArray();
        }

    }

    [DisallowMultipleComponent]
    public sealed class ActiveEnemyActor : MonoBehaviour
    {
        // Keep the scaled capsule's foot at the authored ground-route foot height.
        // Transform scale is current immediately; Collider.bounds can still reflect the prior physics pose.
        public static float GroundRouteLift(CapsuleCollider capsule)
        {
            if (capsule == null) throw new ArgumentNullException(nameof(capsule));
            return Mathf.Max(0f, capsule.height * (Mathf.Abs(capsule.transform.lossyScale.y) - 1f) * 0.5f);
        }

        public EntityId EntityId { get; private set; }
        public ulong TicketId { get; private set; }
        public EnemyArchetype Archetype { get; private set; }
        public AttackPointId TargetAttackPoint { get; private set; }
        public bool HasReachedAttackCapsuleStandOff { get; private set; }
        public bool HasMeasuredAttackCapsuleSurfaceGap { get; private set; }
        public float AttackCapsuleSurfaceGapMeters { get; private set; }
        int nextWaypointIndex = 1;
        Vector3[] authoredWaypoints;
        Collider targetAttackCapsule;
        int climbWaypointStartIndex;
        float appliedGroundRouteLift;
        ulong movementInvocationSequence;
        public M4MovementAttemptSnapshot LatestMovementAttempt { get; private set; }

        internal int CanonicalActivationRunTick { get; set; } = -1;
        internal M4ClimberStructureAttackSnapshot LatestClimberStructureAttack { get; private set; }
        internal Vector3 AssignedJointCenter => targetAttackCapsule == null ? transform.position :
            targetAttackCapsule.transform.TransformPoint(targetAttackCapsule is CapsuleCollider target ? target.center : Vector3.zero);
        internal bool TryMeasureCurrentAssignedGap(out float gap)
            => M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(GetComponent<CapsuleCollider>(),
                transform.position, transform.rotation, targetAttackCapsule as CapsuleCollider, out gap);
        internal void RecordStructureAttack(M4ClimberStructureAttackSnapshot receipt)
            => LatestClimberStructureAttack = receipt;

        internal bool ClimberCenterAdaptationApplied { get; private set; }
        internal int? ClimberCenterSupportInstanceId { get; private set; }
        internal Vector3? ClimberCenterSupportNormal { get; private set; }
        internal float? ClimberCenterStartPlaneDistance { get; private set; }
        internal string ClimberCenterShapeCoverage { get; private set; } = "ADAPTATION_UNAVAILABLE";

        internal void RecordClimberCenterAdaptation(int supportId, Vector3 normal, float planeDistance)
        {
            ClimberCenterAdaptationApplied = true;
            ClimberCenterSupportInstanceId = supportId;
            ClimberCenterSupportNormal = normal;
            ClimberCenterStartPlaneDistance = planeDistance;
            ClimberCenterShapeCoverage = "BODY_CAPSULE_ONLY_NULL_TEMPLATE; FULL_SOLID_ATTACK_SHOT_COVERAGE_UNAVAILABLE";
        }

        internal void MarkAttachedHitShapeCoverageUnavailable()
        {
            if (ClimberCenterAdaptationApplied)
                ClimberCenterShapeCoverage = "BODY_ROUTE_ONLY; ATTACHED_HIT_SHAPE_COVERAGE_UNAVAILABLE";
        }

        internal M4UpperLaneActorSnapshot CaptureUpperLaneObservation(bool registered, RouteId[] routeIds)
        {
            Collider[] inventory = GetComponentsInChildren<Collider>(true);
            var colliders = new List<M4UpperLaneColliderSnapshot>();
            var samples = new List<M4UpperLaneAimSample>();
            foreach (Collider collider in inventory.OrderBy(value => value.GetInstanceID()))
            {
                colliders.Add(new M4UpperLaneColliderSnapshot(collider, this, registered));
                CapsuleCollider capsule = collider as CapsuleCollider;
                bool direct = collider.GetComponent<ActiveEnemyActor>() == this;
                if (direct && capsule != null)
                    samples.Add(new M4UpperLaneAimSample("GeometricCenter", capsule.transform.TransformPoint(capsule.center),
                        collider.GetInstanceID(), registered && collider.enabled && collider.gameObject.activeInHierarchy,
                        registered && collider.enabled && collider.gameObject.activeInHierarchy ? string.Empty : "INACTIVE_OR_UNREGISTERED_COLLIDER",
                        "ENEMY_DIRECT_HIT_COLLIDER_GEOMETRY_NOT_AUTHORED_SEMANTIC_SAMPLE"));
            }
            foreach (string semantic in new[] { "Head", "Body" })
                samples.Add(new M4UpperLaneAimSample(semantic, Vector3.zero, 0, false,
                    "AUTHORED_ENEMY_SEMANTIC_MAPPING_UNAVAILABLE", "ENEMY_REQUIRED_SEMANTIC_SAMPLE"));
            M4EnemyWeakPointCollider[] weakPoints = GetComponentsInChildren<M4EnemyWeakPointCollider>(true)
                .Where(weakPoint => weakPoint.Owner == this && weakPoint.IsConfigured).ToArray();
            Vector3 weakPointCenter = default;
            bool weakPointMapped = weakPoints.Length == 1 && weakPoints[0].TryGetWorldCenter(out weakPointCenter);
            bool weakPointValid = weakPointMapped && registered && weakPoints[0].IsAvailable;
            samples.Add(new M4UpperLaneAimSample("WeakPoint", weakPointCenter,
                weakPointMapped ? weakPoints[0].HitCollider.GetInstanceID() : 0, weakPointValid,
                weakPointValid ? string.Empty : weakPointMapped ? "INACTIVE_OR_UNREGISTERED_COLLIDER" :
                    weakPoints.Length > 1 ? "AMBIGUOUS_ENEMY_WEAK_POINT_MAPPING" : "AUTHORED_ENEMY_SEMANTIC_MAPPING_UNAVAILABLE",
                "AUTHORED_WEAK_POINT_COLLIDER_CENTER_NOT_ATTACK_TARGET_QUERY"));
            CapsuleCollider target = targetAttackCapsule as CapsuleCollider;
            if (target != null)
            {
                colliders.Add(new M4UpperLaneColliderSnapshot(target, this, false, true));
                samples.Add(new M4UpperLaneAimSample("AssignedTargetQueryCenter", target.transform.TransformPoint(target.center),
                    target.GetInstanceID(), target.enabled && target.gameObject.activeInHierarchy,
                    target.enabled && target.gameObject.activeInHierarchy ? string.Empty : "INACTIVE_TARGET_QUERY",
                    "ASSIGNED_TARGET_QUERY_NOT_ENEMY_DAMAGE_COLLIDER"));
            }
            return new M4UpperLaneActorSnapshot(this, registered, authoredWaypoints, nextWaypointIndex,
                climbWaypointStartIndex, appliedGroundRouteLift, routeIds, colliders.ToArray(), samples.ToArray());
        }

        public void Initialize(ActiveEnemyIncarnation incarnation, AttackPointId attackPoint,
            Collider targetCapsule, Vector3[] waypoints, int climbStartIndex, float observedGroundRouteLift = 0f)
        {
            if (targetCapsule == null) throw new ArgumentNullException(nameof(targetCapsule));
            if (waypoints == null || waypoints.Length < 2) throw new ArgumentException("An authored enemy route is required.", nameof(waypoints));
            CanonicalActivationRunTick = -1;
            LatestClimberStructureAttack = null;
            EntityId = incarnation.EntityId;
            TicketId = incarnation.TicketId;
            Archetype = incarnation.Archetype;
            TargetAttackPoint = attackPoint;
            targetAttackCapsule = targetCapsule;
            authoredWaypoints = (Vector3[])waypoints.Clone();
            nextWaypointIndex = 1;
            climbWaypointStartIndex = climbStartIndex;
            appliedGroundRouteLift = observedGroundRouteLift;
            ClimberCenterAdaptationApplied = false;
            ClimberCenterSupportInstanceId = null;
            ClimberCenterSupportNormal = null;
            ClimberCenterStartPlaneDistance = null;
            ClimberCenterShapeCoverage = incarnation.Archetype == EnemyArchetype.Climber
                ? "SHARED_FACTORY_ADAPTATION_UNAVAILABLE" : "NOT_APPLICABLE_NON_CLIMBER";
            movementInvocationSequence = 0;
            LatestMovementAttempt = null;
            LatestMovementAttempt = new M4MovementAttemptSnapshot(NewMovementAttemptRecord(null, 0, false));
        }

        public bool AdvanceOneTick(IReadOnlyList<Collider> supportLegs)
            => AdvanceOneTick(supportLegs, null, 0);

        M4MovementAttemptRecord NewMovementAttemptRecord(M4ThreeNightQaFixedTickContext context,
            int nightOrdinal, bool invocation)
        {
            return new M4MovementAttemptRecord
            {
                InvocationAvailable = invocation,
                Outcome = invocation ? "INTERRUPTED_BEFORE_RETURN" : "NO_INVOCATION_SINCE_INITIALIZE",
                Sequence = movementInvocationSequence,
                EntityValue = EntityId.Value,
                TicketId = TicketId,
                Archetype = Archetype,
                AssignedAttackPoint = TargetAttackPoint,
                CanonicalContextAvailable = context != null && nightOrdinal > 0,
                AttemptedRunFixedTick = context == null || nightOrdinal <= 0 ? (int?)null : context.RunFixedTick,
                AttemptedNightFixedTick = context == null || nightOrdinal <= 0 ? (int?)null : context.NightFixedTick,
                NightOrdinal = context == null || nightOrdinal <= 0 ? (int?)null : nightOrdinal,
                WaypointIndexBefore = nextWaypointIndex,
                WaypointIndexAfter = nextWaypointIndex,
                PriorPosition = transform.position,
                PriorRotation = transform.rotation,
                FinalPosition = transform.position,
                FinalRotation = transform.rotation,
                AssignedTargetColliderInstanceId = targetAttackCapsule == null ? (int?)null : targetAttackCapsule.GetInstanceID()
            };
        }

        internal bool AdvanceOneTick(IReadOnlyList<Collider> supportLegs,
            M4ThreeNightQaFixedTickContext context, int nightOrdinal)
            => AdvanceOneTickCore(supportLegs, context, nightOrdinal, false);

        public bool AdvanceOneTickWithSupportTrace(IReadOnlyList<Collider> supportLegs)
            => AdvanceOneTickCore(supportLegs, null, 0, true);

        bool AdvanceOneTickCore(IReadOnlyList<Collider> supportLegs,
            M4ThreeNightQaFixedTickContext context, int nightOrdinal, bool captureSupportTrace)
        {
            // Invalidate even if detached context/pose capture itself fails.
            LatestMovementAttempt = null;
            movementInvocationSequence++;
            var observation = NewMovementAttemptRecord(context, nightOrdinal, true);
            observation.SupportTraceRequested = captureSupportTrace;
            observation.SupportTraceUnavailableReason = captureSupportTrace
                ? "SUPPORT_GUARD_NOT_INVOKED" : "SUPPORT_TRACE_NOT_REQUESTED";
            // Clear prior successful data before any potentially failing mover evaluation.
            LatestMovementAttempt = new M4MovementAttemptSnapshot(observation);
            try
            {
                if (HasReachedAttackCapsuleStandOff)
                {
                    observation.Outcome = "ALREADY_REACHED_STANDOFF_NOT_EVALUATED";
                    return true;
                }
                EnemyArchetypeConfig config = EnemyArchetypeCatalog.Get(Archetype);
                float speed = Archetype == EnemyArchetype.Climber && nextWaypointIndex >= climbWaypointStartIndex
                    ? config.ClimbSpeedMetersPerSecond
                    : config.GroundSpeedMetersPerSecond;
                float distance = speed / EnemyDirector.TickRate;
                Vector3 destination = authoredWaypoints[Mathf.Min(nextWaypointIndex, authoredWaypoints.Length - 1)];
                Vector3 proposedPosition = Vector3.MoveTowards(transform.position, destination, distance);
                Vector3 forward = destination - proposedPosition;
                Quaternion proposedRotation = forward.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                    : transform.rotation;
                observation.MovementProposed = true;
                observation.ClimbSegmentSelected = Archetype == EnemyArchetype.Climber && nextWaypointIndex >= climbWaypointStartIndex;
                observation.Destination = destination;
                observation.ProposedPosition = proposedPosition;
                observation.ProposedRotation = proposedRotation;

                Collider movingCollider = GetComponent<Collider>();
                observation.MoverColliderInstanceId = movingCollider == null ? (int?)null : movingCollider.GetInstanceID();
                if (movingCollider != null)
                {
                    observation.SupportGuardInvoked = true;
                    bool supportResolved;
                    if (captureSupportTrace)
                    {
                        M2SupportResolutionTraceSnapshot supportTrace = null;
                        try { supportResolved = M2SupportLegCollisionGuard.TryResolveWithTrace(movingCollider,
                            supportLegs, proposedPosition, proposedRotation, out proposedPosition, out supportTrace); }
                        finally
                        {
                            observation.SupportTrace = supportTrace;
                            observation.SupportTraceUnavailableReason = supportTrace == null
                                ? "SUPPORT_TRACE_CAPTURE_UNAVAILABLE" : string.Empty;
                        }
                    }
                    else supportResolved = M2SupportLegCollisionGuard.TryResolve(movingCollider, supportLegs,
                        proposedPosition, proposedRotation, out proposedPosition);
                    observation.SupportResolved = supportResolved;
                    if (!supportResolved)
                    {
                        observation.Outcome = "SUPPORT_RESOLUTION_FAILED_HELD_PRIOR_POSE";
                        return false;
                    }
                    observation.SupportResolvedPosition = proposedPosition;
                }

                if (movingCollider != null)
                {
                    observation.TargetGuardInvoked = true;
                    observation.AssignedTargetEnabled = targetAttackCapsule == null ? (bool?)null :
                        targetAttackCapsule.enabled && targetAttackCapsule.gameObject.activeInHierarchy;
                    M2AttackCapsulePoseResult capsuleResult = M2AttackCapsuleStandOffGuard.ClassifyAssignedTargetPose(
                        movingCollider, targetAttackCapsule, proposedPosition, proposedRotation, out float penetrationDepth);
                    observation.TargetClassification = capsuleResult;
                    observation.TargetPenetrationDepthMeters = penetrationDepth;
                    CapsuleCollider target = targetAttackCapsule as CapsuleCollider;
                    CapsuleCollider moving = movingCollider as CapsuleCollider;
                    bool proposedGapNonpositive = false;
                    if (observation.AssignedTargetEnabled == true && target != null && moving != null)
                    {
                        observation.ProposedGapMeasurementInvoked = true;
                        bool proposedGapMeasured = M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(moving,
                            proposedPosition, proposedRotation, target, out float proposedGap);
                        observation.ProposedGapMeasurementSucceeded = proposedGapMeasured;
                        observation.MeasuredProposedPoseGapMeters = proposedGapMeasured ? (float?)proposedGap : null;
                        observation.ProposedGapMeasurementUnavailableReason = proposedGapMeasured
                            ? string.Empty : "PROPOSED_CAPSULE_GAP_MEASUREMENT_FAILED";
                        if (!proposedGapMeasured)
                        {
                            observation.Outcome = "PROPOSED_GAP_MEASUREMENT_FAILED_HELD_PRIOR_POSE";
                            return false;
                        }
                        proposedGapNonpositive = proposedGap <= 0f;
                    }
                    else
                    {
                        observation.ProposedGapMeasurementUnavailableReason = observation.AssignedTargetEnabled != true
                            ? "ASSIGNED_TARGET_DISABLED_OR_UNAVAILABLE"
                            : "PROPOSED_GAP_CAPSULE_PAIR_UNSUPPORTED";
                    }
                    if (capsuleResult == M2AttackCapsulePoseResult.TargetCapsuleReached || proposedGapNonpositive)
                    {
                        if (target == null || moving == null)
                        {
                            observation.Outcome = "GAP_CAPSULE_GEOMETRY_UNAVAILABLE_HELD_PRIOR_POSE";
                            return false;
                        }
                        observation.GapMeasurementInvoked = true;
                        bool gapMeasured = M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(moving, transform.position,
                            transform.rotation, target, out float measuredGap);
                        observation.GapMeasurementSucceeded = gapMeasured;
                        observation.MeasuredPriorPoseGapMeters = gapMeasured ? (float?)measuredGap : null;
                        if (!gapMeasured || measuredGap <= 0f)
                        {
                            observation.Outcome = gapMeasured ? "NONPOSITIVE_GAP_HELD_PRIOR_POSE" : "GAP_MEASUREMENT_FAILED_HELD_PRIOR_POSE";
                            return false;
                        }

                        AttackCapsuleSurfaceGapMeters = measuredGap;
                        HasMeasuredAttackCapsuleSurfaceGap = true;
                        HasReachedAttackCapsuleStandOff = true;
                        observation.Outcome = "STANDOFF_REACHED_POSITIVE_GAP_HELD_PRIOR_POSE";
                        return true;
                    }
                }

                transform.SetPositionAndRotation(proposedPosition, proposedRotation);
                observation.TransformApplied = true;
                if (Vector3.Distance(proposedPosition, destination) <= 0.001f && nextWaypointIndex < authoredWaypoints.Length - 1)
                    nextWaypointIndex++;
                observation.Outcome = movingCollider == null ? "TRANSFORM_APPLIED_GUARDS_NOT_EVALUATED_NO_MOVER_COLLIDER" : "TRANSFORM_APPLIED";
                return true;
            }
            finally
            {
                observation.FinalPosition = transform.position;
                observation.FinalRotation = transform.rotation;
                observation.WaypointIndexAfter = nextWaypointIndex;
                LatestMovementAttempt = new M4MovementAttemptSnapshot(observation);
            }
        }
    }
    // Acknowledgement presentation only. This component never moves the Partner body,
    // changes a path/FSM or uses its cosmetic clock to accept another request.
    internal sealed class M4PartnerAcknowledgementCue : MonoBehaviour
    {
        LineRenderer hand;
        Material ownedMaterial;
        AudioSource metal;
        AudioClip ownedClip;
        float cosmeticEnd;
        const float CosmeticSeconds = 0.35f;

        public void Emit()
        {
            if (hand == null)
            {
                var cueObject = new GameObject("Partner_ActualRequestAcknowledgement");
                cueObject.transform.SetParent(transform, false);
                hand = cueObject.AddComponent<LineRenderer>();
                hand.useWorldSpace = false; hand.positionCount = 3;
                hand.startWidth = 0.1f; hand.endWidth = 0.08f;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    ownedMaterial = new Material(shader); ownedMaterial.color = Color.yellow;
                    hand.sharedMaterial = ownedMaterial;
                }
                metal = cueObject.AddComponent<AudioSource>();
                metal.playOnAwake = false; metal.spatialBlend = 1f;
                const int rate = 22050;
                var pcm = new float[4410];
                for (int index = 0; index < pcm.Length; index++)
                {
                    float t = index / (float)rate;
                    pcm[index] = (Mathf.Sin(2f * Mathf.PI * 880f * t) +
                        0.35f * Mathf.Sin(2f * Mathf.PI * 1379f * t)) * Mathf.Exp(-35f * t) * 0.45f;
                }
                ownedClip = AudioClip.Create("Partner_QA_RequestAcknowledged_OneMetalTap", pcm.Length, 1, rate, false);
                ownedClip.SetData(pcm, 0);
            }
            cosmeticEnd = Time.unscaledTime + CosmeticSeconds;
            hand.enabled = true;
            UpdateGesture();
            metal.PlayOneShot(ownedClip);
        }

        void Update()
        {
            if (hand == null || !hand.enabled) return;
            if (Time.unscaledTime >= cosmeticEnd) { hand.enabled = false; return; }
            UpdateGesture();
        }

        void UpdateGesture()
        {
            float progress = 1f - (cosmeticEnd - Time.unscaledTime) / CosmeticSeconds;
            hand.SetPosition(0, new Vector3(0.25f, 0.25f, 0f));
            hand.SetPosition(1, new Vector3(0.6f, 0.5f, 0f));
            hand.SetPosition(2, new Vector3(0.6f + 0.18f * Mathf.Sin(progress * Mathf.PI), 1.05f, 0f));
        }

        void OnDestroy()
        { if (ownedMaterial != null) Destroy(ownedMaterial); if (ownedClip != null) Destroy(ownedClip); }
    }

    // Cosmetic evidence of an actual Applied event; body pose and damage timing never use this component.
    internal sealed class M4ClimberStrikeFeedback : MonoBehaviour
    {
        LineRenderer arm;
        Material ownedMaterial;
        AudioSource impact;
        AudioClip ownedClip;
        Vector3 joint;
        float cosmeticEnd;
        public void Show(Vector3 target)
        {
            joint = target; cosmeticEnd = Time.unscaledTime + 0.4f;
            if (arm == null)
            {
                arm = gameObject.AddComponent<LineRenderer>();
                arm.positionCount = 3; arm.useWorldSpace = true; arm.startWidth = 0.09f; arm.endWidth = 0.04f;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) { ownedMaterial = new Material(shader); ownedMaterial.color = Color.cyan; arm.sharedMaterial = ownedMaterial; }
                var audioObject = new GameObject("Climber_CommittedImpactAudio"); audioObject.transform.SetParent(transform, false);
                impact = audioObject.AddComponent<AudioSource>(); impact.spatialBlend = 1f; impact.playOnAwake = false;
                const int rate = 22050;
                var pcm = new float[4410];
                for (int index = 0; index < pcm.Length; index++)
                {
                    float t = index / (float)rate;
                    pcm[index] = Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-32f * t) * 0.65f;
                }
                ownedClip = AudioClip.Create("Climber_QA_CommittedStructureImpact", pcm.Length, 1, rate, false);
                ownedClip.SetData(pcm, 0);
            }
            arm.enabled = true; impact.transform.position = joint; impact.PlayOneShot(ownedClip);
        }
        void Update()
        {
            if (arm == null || !arm.enabled) return;
            float remaining = cosmeticEnd - Time.unscaledTime;
            if (remaining <= 0f) { arm.enabled = false; return; }
            Vector3 shoulder = transform.position + Vector3.up * 0.25f;
            float scrape = Mathf.Sin((1f - remaining / 0.4f) * Mathf.PI);
            arm.SetPosition(0, shoulder);
            arm.SetPosition(1, Vector3.Lerp(shoulder, joint, 0.5f) + transform.right * (0.25f * scrape));
            arm.SetPosition(2, joint + Vector3.up * (0.3f * scrape));
        }
        void OnDestroy()
        { if (ownedMaterial != null) Destroy(ownedMaterial); if (ownedClip != null) Destroy(ownedClip); }
    }

}
