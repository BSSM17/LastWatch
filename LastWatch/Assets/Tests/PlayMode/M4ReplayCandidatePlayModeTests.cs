using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using LastWatch.Core.Partner;
using LastWatch.Core.Space;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LastWatch.Tests.M4
{
    [Timeout(660000)]
    public sealed class M4ReplayCandidatePlayModeTests
    {
        const string ScenePath = "Assets/Scenes/LastWatch_M2M3M4_3Night_QA.unity";
        const double PerReplayWallTimeoutSeconds = 600;
        static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        [Serializable]
        sealed class ReplayBoundaryState
        {
            public int consumedTicks;
            public int nextTapeNight;
            public int activeNight;
            public int completedNights;
            public bool fixedTickRunning;
            public string stage;
            public string lastTickStatus;
            public string lastTickReason;
            public int totalTickets;
            public int resolvedTickets;
            public int activeTickets;
            public int pendingTickets;
            public int poolWaitingTickets;
        }

        [Serializable]
        sealed class ColliderDiagnosticAggregate
        {
            public string channel;
            public string colliderKind;
            public string colliderName;
            public string face;
            public int observations;
            public int distinctEnemyColliderPairs;
            public int firstConsumedTick;
            public int lastConsumedTick;
            public string[] representativeSamples;
        }

        [Serializable]
        sealed class ActualShotDiagnosticExample
        {
            public int replayConsumedTick;
            public int runFixedTick;
            public string weaponId;
            public bool raycastAttempted;
            public string outcome;
            public string noRaycastReason;
            public bool hasColliderHit;
            public string colliderName;
            public float hitDistance;
            public bool hasGrayboxTag;
            public string colliderKind;
            public bool hasFace;
            public string face;
            public bool hasActiveEnemyActor;
            public bool actorRegistryKeyPresent;
            public bool actorInstanceMatchesRegistry;
            public string damageStatus;
            public bool damageBecameResolved;
        }

        [Serializable]
        sealed class ActualShotDiagnosticAggregate
        {
            public string dimension;
            public string key;
            public bool hasGrayboxTag;
            public bool overflowBucket;
            public int count;
            public bool examplesTruncated;
            public ActualShotDiagnosticExample[] examples;
        }

        [Serializable]
        sealed class ActualShotDiagnosticReport
        {
            public string evidenceType = "ACTUAL_CORE_SHOT_RESOLVER_SNAPSHOT";
            public int replayFireCommandCount;
            public int raycastAttemptCount;
            public bool colliderNameBucketsTruncated;
            public bool examplesTruncated;
            public string recorderFailure;
            public int expectedFireCommandsInConsumedTapePrefix;
            public int outcomeCountTotal;
            public int colliderKindCountTotal;
            public int colliderNameCountTotal;
            public int faceCountTotal;
            public int worldBlockOutcomeCount;
            public int resolvedEnemyBlockOutcomeCount;
            public bool fireCommandCountMatchesConsumedTape;
            public bool outcomeCountsMatchFireCommands;
            public bool colliderMarginalCountsMatch;
            public bool worldBlockCountMatchesCoreCounter;
            public bool resolvedEnemyBlockCountMatchesCoreCounter;
            public string reconciliation;
            public ActualShotDiagnosticAggregate[] outcomeCounts;
            public ActualShotDiagnosticAggregate[] colliderKindCounts;
            public ActualShotDiagnosticAggregate[] colliderNameCounts;
            public ActualShotDiagnosticAggregate[] faceCounts;
        }

        [Serializable]
        sealed class ColliderDiagnosticReport
        {
            public int schemaVersion = 8;
            public string samplingContract = "Physical collider pairs are broadphase candidates sampled after each observed Core fixed tick; the 0.002m bounds expansion only broadens candidate collection. ComputePenetration results are recorded separately from candidate discovery. Raycasts are post-tick geometry snapshots for fire-command ticks, not actual Core shot results.";
            public string actualShotResultClassification = "NOT_SERIALIZED";
            public ActualShotDiagnosticReport actualCoreShotResults;
            public int postTickRaycastQueryMaxDistanceMeters = 180;
            public string physicalOverlapAssessment;
            public string physicalOverlapPolicy = "LastWatch v3.4: V32 solid-structure penetration/contact remains its own gate; AttackCapsule target/query volumes are excluded from that solid gate and measured separately by attackCapsuleStandOff under V34-01/03. Actual shot first-hit/WorldBlock and upper-lane existence remain separate. No epsilon/contact rule or gameplay PASS is inferred.";
            public string upperLaneGeometryAssessment = "NOT_EVALUATED_BY_SHOT_FIRST_HIT_COUNTS_SEPARATE_DETERMINISTIC_GEOMETRY_PROBE_REQUIRED";
            public UpperLaneGeometryReport upperLaneGeometry;
            public UpperLaneCoreObservationReport upperLaneCoreObservation;
            public string solidStructureGateAssessment;
            public string solidStructureGateDisposition = "NOT_ASSERTED_BY_TOOLS";
            public bool geometryObservationComplete;
            public bool semanticRoleClassificationComplete;
            public bool pairBucketCapReached;
            public bool colliderRoleBucketCapReached;
            public int omittedPairObservations;
            public int omittedPairObservationSamples;
            public int colliderPairBucketCount;
            public int colliderRoleBucketCount;
            public int physicalColliderPairCandidateObservations;
            public int computePenetrationEvaluatedObservations;
            public int computePenetrationReportedOverlapObservations;
            public int computePenetrationNoOverlapObservations;
            public int computePenetrationUnavailableObservations;
            public int separationDistanceAvailableObservations;
            public int solidStructureGateCandidateObservations;
            public int solidStructureGatePenetrationReportedObservations;
            public int solidStructureGateUnavailableObservations;
            public int permittedAttackCapsuleCandidateObservations;
            public int triggerCandidateObservations;
            public int structuralTriggerPairObservations;
            public int coreClassifiedEnemyHitTriggerPairObservations;
            public int unclassifiedTriggerRolePairObservations;
            public int inactiveEnemyColliderObservationsSkipped;
            public int inactiveWorldColliderCandidateObservations;
            public int excludedEnemyActorCandidateObservations;
            public bool pairObservationSamplesTruncated;
            public string raycastAssessment;
            public int replayConsumedTicks;
            public int observationStartConsumedTick;
            public int observationEndConsumedTick;
            public int observedConsumedTickSamples;
            public int skippedConsumedTickSamples;
            public int fireCommandTicksObserved;
            public int fireCommandRaycastHits;
            public int fireCommandRaycastNoHits;
            public int physicalOverlapDetectedTicks;
            public int physicalOverlapPairObservations;
            public int overlapBroadphaseOverflowSamples;
            public int coreReplayBlockedWorldShots;
            public int coreReplayBlockedResolvedEnemyShots;
            public string coreReplayPriorStatus;
            public ColliderPairAggregate[] colliderPairs;
            public ColliderRoleAggregate[] colliderRoleAggregates;
            public ColliderDiagnosticAggregate[] byChannelAndCollider;
            public AttackCapsuleStandOffReport attackCapsuleStandOff;
        }

        [Serializable]
        sealed class UpperLaneGeometryReport
        {
            public int schemaVersion = 1;
            public string method = "CORE_TOWER_GEOMETRY_CANONICAL_AVAILABLE_UPPER_STATIONS_TO_SCENE_ACTIVE_ENEMY_ROOT_COLLIDER_CENTER; UNITY_PHYSICS_FIRST_HIT_MUST_BE_SAME_ACTOR_COMPONENT; SAMPLED_FIXED_TICKS_ONLY; ACTOR_REGISTRY_AND_ALL_VALID_ROUTES_NOT_CERTIFIED";
            public string assessment;
            public bool observationWindowComplete;
            public bool coreGeometryAvailable;
            public int actorTickObservations;
            public int witnessedActorTickObservations;
            public int unwitnessedActorTickObservations;
            public int unavailableActorTickObservations;
            public int stationRayQueries;
            public string firstUnwitnessedContext;
            public string[] representativeUnwitnessedContexts;
            public bool representativeUnwitnessedContextsTruncated;
        }

        [Serializable]
        sealed class AttackCapsuleStandOffReport
        {
            public int schemaVersion = 1;
            public string gate = "V34_ATTACK_CAPSULE_OVERLAP_AND_STANDOFF";
            public string gateDisposition = "SEPARATE_FROM_V32_SOLID_STRUCTURE_AND_ACTUAL_SHOT_RESULTS";
            public string targetAssignmentPolicy = "ONLY_EACH_ACTIVE_ENEMY_ACTORS_CORE_ASSIGNED_TARGET_ATTACK_POINT_IS_GATE_INCLUDED; OTHER_AP_CROSSINGS_ARE_INFORMATIONAL";
            public string nonTargetCrossingPolicy = "CLIMBER_ASSIGNED_AP_C_RAIL_ORIGIN_AP_G_CROSSINGS_ARE_INFORMATIONAL_NOT_A_TARGET_GATE_FAILURE";
            public string samplingMethod = "EXHAUSTIVE_AP_TAG_X_ACTIVE_ENEMY_ACTOR_CHILD_COLLIDERS_AT_EACH_OBSERVED_FIXED_TICK; NO_BROADPHASE_QUERY";
            public string enemyColliderInventoryScope = "ACTIVE_ENEMY_ACTOR_DIRECT_AND_CHILD_COLLIDERS_ONLY; CORE_REGISTERED_COLLIDERS_OUTSIDE_THIS_HIERARCHY_NOT_EXPOSED";
            public string positiveGapMeasurementMethod = "CORE_M2AttackCapsuleStandOffGuard_TRYMEASURESURFACEGAP; ANALYTIC_CAPSULE_SEGMENT_DISTANCE_MINUS_RADII_FOR_CAPSULECOLLIDER_PAIRS; NONCAPSULE_PAIRS_UNSUPPORTED; NO_CLOSESTPOINT_APPROXIMATION_OR_EPSILON";
            public string observedPathCoverage = "OBSERVED_REPLAY_FIXED_TICK_SAMPLES_ONLY; NOT_ALL_VALID_PATHS_OR_BETWEEN_TICK_CONTINUOUS_PATHS";
            public string validRouteSetCoverage = "INCONCLUSIVE_CORE_ROUTE_OR_RAIL_ID_AND_MOVEMENT_PHASE_NOT_EXPOSED";
            public string representativeSampleRetention = "Aggregate counters/minima use each observed tick; serialized pair samples retain representative first/overlap/min-gap/terminal/last ticks, not every transform sample.";
            public string observedPathVolumeAssessment;
            public string terminalAttackPoseStandOffAssessment = "INCONCLUSIVE_NO_CORE_TERMINAL_ATTACK_POSE_OBSERVED";
            public string outsideVolumeAttackEvidence = "INCONCLUSIVE_CORE_ATTACK_RESOLUTION_FACT_NOT_EXPOSED";
            public string minimumDistanceThreshold = "TUNING / NOT GATED (d_min PENDING EVIDENCE)";
            public string terminalPoseAuthority = "CORE_ACTIVE_ENEMY_ACTOR_READ_ONLY_TARGET_ATTACK_POINT_AND_REACHED_STANDOFF_FLAGS";
            public string attackResolutionAuthority = "UNAVAILABLE_REPLAYSHOTDIAGNOSTICS_COVERS_PLAYER_SHOTS_NOT_ENEMY_ATTACK_RESOLUTION";
            public bool observationWindowComplete;
            public bool replayConsumedAllInputTicks;
            public bool allExpectedAttackCapsulesObserved;
            public bool allEnemyPairMeasurementsEvaluated;
            public string pairMeasurementCompletenessScope = "ALL_ACTIVE_ENEMY_COLLIDERS_AGAINST_EACH_ACTORS_ASSIGNED_TARGET_AP_ONLY; NON_TARGET_PAIRS_INFORMATIONAL";
            public int expectedAttackCapsuleCount = 6;
            public int distinctAttackCapsuleIdsObserved;
            public int missingAttackCapsuleIds;
            public int missingAttackCapsuleTickObservations;
            public string[] missingAttackCapsuleIdentityIds;
            public int duplicateAttackCapsuleIds;
            public string[] observedAttackCapsuleIds;
            public int inactiveOrDisabledPairObservations;
            public int observedFixedTickSamples;
            public int skippedFixedTickSamples;
            public int activeEnemyActorObservations;
            public int enemyActorsWithoutColliderObservations;
            public int candidatePairObservations;
            public int assignedTargetPairObservations;
            public int informationalNonTargetPairObservations;
            public int uniqueAttackCapsuleEnemyColliderPairs;
            public int pairBucketCount;
            public int omittedPairObservations;
            public bool pairBucketCapReached;
            public int computePenetrationEvaluatedObservations;
            public int computePenetrationOverlapObservations;
            public int computePenetrationNoOverlapObservations;
            public int computePenetrationUnavailableObservations;
            public int assignedTargetComputePenetrationEvaluatedObservations;
            public int assignedTargetComputePenetrationOverlapObservations;
            public int assignedTargetComputePenetrationNoOverlapObservations;
            public int assignedTargetComputePenetrationUnavailableObservations;
            public int informationalNonTargetOverlapObservations;
            public int assignedTargetPositiveVolumeOverlapObservations;
            public int assignedTargetOverlapDepthUnresolvedObservations;
            public int informationalNonTargetPositiveVolumeOverlapObservations;
            public int inactiveAssignedTargetCapsulePairObservations;
            public int positiveGapMeasurementObservations;
            public int nonPositiveSurfaceClearanceObservations;
            public int unavailableSurfaceGapObservations;
            public int unsupportedColliderPairObservations;
            public int unclassifiedEnemyTriggerPairObservations;
            public int assignedTargetUnclassifiedEnemyTriggerPairObservations;
            public int candidateMinimumGapPairObservationCount;
            public int terminalAttackPoseObservations;
            public int terminalTargetPairObservations;
            public int terminalTargetPairPositiveGapObservations;
            public int terminalTargetPairNonPositiveClearanceObservations;
            public int terminalTargetPairUnsupportedGapObservations;
            public int terminalTargetOverlapObservations;
            public int terminalTargetPositiveVolumeOverlapObservations;
            public int terminalTargetOverlapDepthUnresolvedObservations;
            public int terminalPoseTargetAssignmentMismatches;
            public int terminalCoreGapUnavailableObservations;
            public int terminalCoreGapReconciliationMismatches;
            public string minimumObservedPositiveGapCandidateMeters = "unavailable";
            public string minimumObservedPositiveGapAttackCapsuleId = "unavailable";
            public string minimumObservedPositiveGapEnemyEntityId = "unavailable";
            public string minimumObservedPositiveGapEnemyColliderPath = "unavailable";
            public int minimumObservedPositiveGapConsumedTick;
            public string minimumObservedPositiveGapSampleContext = "NO_POSITIVE_GAP_MEASUREMENT_RECORDED";
            public string minimumTerminalPositiveGapCandidateMeters = "unavailable";
            public string minimumTerminalPositiveGapAttackPointId = "unavailable";
            public string minimumTerminalPositiveGapEnemyEntityId = "unavailable";
            public string minimumTerminalPositiveGapEnemyColliderPath = "unavailable";
            public string minimumTerminalPositiveGapActorPosition = "unavailable";
            public int minimumTerminalPositiveGapConsumedTick;
            public int outsideVolumeAttackSuccessObservations;
            public string armScrapeVisualReview = "PENDING_OWNER_QA_VISUAL_REVIEW";
            public AttackCapsuleTerminalPoseDiagnostic[] terminalAttackPoses;
            public AttackCapsulePairAggregate[] pairs;
        }

        [Serializable]
        sealed class AttackCapsuleTerminalPoseDiagnostic
        {
            public int replayConsumedTick;
            public float unityFixedTimeSeconds;
            public string sceneName;
            public int activeNight;
            public string qaStage;
            public string entityId;
            public string ticketId;
            public string enemyArchetype;
            public string targetAttackPointId;
            public string movementState = "NOT_EXPOSED_BY_CORE";
            public string actorTransformPath;
            public string actorPosition;
            public string actorRotation;
            public string terminalPoseAuthority = "CORE_ACTIVE_ENEMY_ACTOR_HAS_REACHED_STANDOFF";
            public bool hasReachedAttackCapsuleStandOff;
            public bool hasMeasuredCoreSurfaceGap;
            public string coreSurfaceGapMeters = "unavailable";
            public string coreGapMeasurementApi = "M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap";
            public bool rootColliderGapMeasurementAvailable;
            public string rootColliderSignedGapMeters = "unavailable";
            public bool coreAndToolsRootGapExactlyEqual;
            public string targetAttackCapsulePath;
            public string targetCapsuleMappingStatus;
            public int activeEnemyColliderCount;
            public int unsupportedActiveEnemyColliderCount;
            public int unclassifiedActiveTriggerColliderCount;
            public string evidenceScope = "OBSERVED_REPLAY_TERMINAL_POSE_ONLY; NOT_ALL_VALID_TERMINAL_POSES_OR_ATTACK_SUCCESS";
        }

        [Serializable]
        sealed class AttackCapsulePairSample
        {
            public int replayConsumedTick;
            public float unityFixedTimeSeconds;
            public string sceneName;
            public int activeNight;
            public string qaStage;
            public string poseContext = "OBSERVED_FIXED_TICK; MOVEMENT_STATE_NOT_EXPOSED";
            public string enemyMovementState = "unavailable";
            public string attackPointId;
            public string targetAttackPointId;
            public string apRelation;
            public bool isAssignedTargetAttackPoint;
            public bool isTargetAttackCapsule;
            public bool bothCollidersActive;
            public bool targetCapsuleActive;
            public bool enemyColliderActive;
            public bool coreTerminalStandOffReached;
            public string entityId;
            public string ticketId;
            public string enemyArchetype;
            public string actorTransformPath;
            public string actorPosition;
            public ColliderIdentityDiagnostic attackCapsule;
            public ColliderIdentityDiagnostic enemyCollider;
            public bool computePenetrationEvaluated;
            public bool computePenetrationReturnedOverlap;
            public string computePenetrationReturnedDirection;
            public bool computePenetrationDepthAvailable;
            public string computePenetrationReturnedDepthMeters;
            public bool positiveVolumeOverlapReported;
            public bool overlapDepthUnresolved;
            public bool surfaceGapMeasurementSupported;
            public bool positiveSurfaceGapAvailable;
            public string signedSurfaceGapMeters;
            public string positiveSurfaceGapMeters;
            public string gapUnavailableReason;
        }

        [Serializable]
        sealed class AttackCapsulePairAggregate
        {
            public string pairKey;
            public string attackPointId;
            public string enemyEntityId;
            public string enemyTicketId;
            public string enemyArchetype;
            public ColliderIdentityDiagnostic attackCapsule;
            public ColliderIdentityDiagnostic enemyCollider;
            public string enemySemanticRole;
            public bool enemyRoleClassificationInconclusive;
            public bool isAssignedTargetAttackPoint;
            public string gateDisposition;
            public int observations;
            public int distinctConsumedTickObservations;
            public int computePenetrationEvaluatedObservations;
            public int computePenetrationOverlapObservations;
            public int computePenetrationNoOverlapObservations;
            public int computePenetrationUnavailableObservations;
            public int positiveVolumeOverlapObservations;
            public int overlapDepthUnresolvedObservations;
            public int positiveGapMeasurementObservations;
            public int nonPositiveSurfaceClearanceObservations;
            public int unavailableSurfaceGapObservations;
            public int terminalAttackPoseObservations;
            public int terminalTargetPairObservations;
            public int terminalTargetPairPositiveGapObservations;
            public int terminalTargetPairNonPositiveClearanceObservations;
            public int terminalTargetPairUnsupportedGapObservations;
            public int terminalTargetOverlapObservations;
            public int terminalTargetPositiveVolumeOverlapObservations;
            public int terminalTargetOverlapDepthUnresolvedObservations;
            public int firstTerminalConsumedTick;
            public string minimumTerminalPositiveGapMeters = "unavailable";
            public int minimumTerminalPositiveGapConsumedTick;
            public string minimumTerminalPositiveGapActorPosition = "unavailable";
            public string terminalPoseActorPosition = "unavailable";
            public int firstConsumedTick;
            public int lastConsumedTick;
            public string minimumPositiveGapMeters = "unavailable";
            public int minimumPositiveGapConsumedTick;
            public string minimumPositiveGapActorPosition = "unavailable";
            public string minimumPositiveGapSampleContext = "NO_POSITIVE_GAP_MEASUREMENT_RECORDED";
            public string sampleRetentionPolicy = "Aggregate counts include every observed tick; retain first, first-overlap, minimum-positive-gap, first-terminal, minimum-terminal-positive-gap, and last samples only.";
            public AttackCapsulePairSample[] representativeSamples;
        }

        [Serializable]
        sealed class ColliderIdentityDiagnostic
        {
            public int instanceId;
            public string colliderName;
            public string transformPath;
            public string gameObjectTag;
            public int layer;
            public string layerName;
            public bool activeSelf;
            public bool activeInHierarchy;
            public bool componentEnabled;
            public bool isTrigger;
            public bool hasGrayboxTag;
            public bool grayboxTagComponentEnabled;
            public string colliderKind;
            public string face;
            public string semanticRole;
            public string roleClassificationEvidence;
            public bool roleClassificationInconclusive;
            public bool enemySideGateCandidate;
            public bool solidStructureGateCandidate;
        }

        [Serializable]
        sealed class ColliderPairSample
        {
            public int replayConsumedTick;
            public float unityFixedTimeSeconds;
            public string sceneName;
            public int activeNight;
            public string stage;
            public string entityId;
            public string ticketId;
            public string enemyArchetype;
            public string actorTransformPath;
            public string actorPosition;
            public string enemyColliderPosition;
            public string worldColliderPosition;
            public bool computePenetrationEvaluated;
            public bool computePenetrationReturnedOverlap;
            public bool separationDirectionAvailable;
            public string returnedSeparationDirection;
            public bool separationDistanceAvailable;
            public string returnedSeparationDistanceMeters;
        }

        [Serializable]
        sealed class ColliderPairAggregate
        {
            public string pairKey;
            public string pairRole;
            public string gateDisposition;
            public int observations;
            public int distinctConsumedTickObservations;
            public int computePenetrationEvaluatedObservations;
            public int computePenetrationReportedOverlapObservations;
            public int computePenetrationNoOverlapObservations;
            public int computePenetrationUnavailableObservations;
            public int separationDistanceAvailableObservations;
            public int firstConsumedTick;
            public int lastConsumedTick;
            public string minimumReportedSeparationDistanceMeters;
            public string maximumReportedSeparationDistanceMeters;
            public ColliderIdentityDiagnostic enemyCollider;
            public ColliderIdentityDiagnostic worldCollider;
            public bool samplesTruncated;
            public ColliderPairSample[] samples;
        }

        [Serializable]
        sealed class ColliderRoleAggregate
        {
            public string pairRole;
            public string colliderKind;
            public string colliderName;
            public string face;
            public int candidateObservations;
            public int uniqueColliderPairs;
            public int computePenetrationReportedOverlapObservations;
            public int computePenetrationNoOverlapObservations;
            public int computePenetrationUnavailableObservations;
            public int firstConsumedTick;
            public int lastConsumedTick;
        }

        sealed class ColliderRoleBucket
        {
            public string pairRole;
            public string colliderKind;
            public string colliderName;
            public string face;
            public int candidateObservations;
            public int overlaps;
            public int noOverlaps;
            public int unavailable;
            public int firstConsumedTick = int.MaxValue;
            public int lastConsumedTick;
            public readonly HashSet<string> pairs = new HashSet<string>(StringComparer.Ordinal);

            public ColliderRoleAggregate ToReport()
            {
                return new ColliderRoleAggregate
                {
                    pairRole = pairRole,
                    colliderKind = colliderKind,
                    colliderName = colliderName,
                    face = face,
                    candidateObservations = candidateObservations,
                    uniqueColliderPairs = pairs.Count,
                    computePenetrationReportedOverlapObservations = overlaps,
                    computePenetrationNoOverlapObservations = noOverlaps,
                    computePenetrationUnavailableObservations = unavailable,
                    firstConsumedTick = firstConsumedTick == int.MaxValue ? 0 : firstConsumedTick,
                    lastConsumedTick = lastConsumedTick
                };
            }
        }

        sealed class ColliderPairBucket
        {
            public string pairKey;
            public string pairRole;
            public string gateDisposition;
            public ColliderIdentityDiagnostic enemyCollider;
            public ColliderIdentityDiagnostic worldCollider;
            public int observations;
            public int computePenetrationEvaluatedObservations;
            public int overlaps;
            public int noOverlaps;
            public int unavailable;
            public int distanceAvailable;
            public int firstConsumedTick = int.MaxValue;
            public int lastConsumedTick;
            public float minimumDistance = float.PositiveInfinity;
            public float maximumDistance = float.NegativeInfinity;
            public bool samplesTruncated;
            public readonly List<ColliderPairSample> samples = new List<ColliderPairSample>();
            public readonly HashSet<int> ticks = new HashSet<int>();

            public void Add(ColliderPairSample sample, bool retainRawSample)
            {
                observations++;
                ticks.Add(sample.replayConsumedTick);
                if (sample.replayConsumedTick < firstConsumedTick) firstConsumedTick = sample.replayConsumedTick;
                if (sample.replayConsumedTick > lastConsumedTick) lastConsumedTick = sample.replayConsumedTick;
                if (!sample.computePenetrationEvaluated) unavailable++;
                else
                {
                    computePenetrationEvaluatedObservations++;
                    if (sample.computePenetrationReturnedOverlap) overlaps++;
                    else noOverlaps++;
                }
                if (sample.separationDistanceAvailable)
                {
                    float distance = float.Parse(sample.returnedSeparationDistanceMeters, System.Globalization.CultureInfo.InvariantCulture);
                    distanceAvailable++;
                    minimumDistance = Math.Min(minimumDistance, distance);
                    maximumDistance = Math.Max(maximumDistance, distance);
                }
                if (retainRawSample) samples.Add(sample);
                else samplesTruncated = true;
            }

            public ColliderPairAggregate ToReport()
            {
                return new ColliderPairAggregate
                {
                    pairKey = pairKey,
                    pairRole = pairRole,
                    gateDisposition = gateDisposition,
                    observations = observations,
                    distinctConsumedTickObservations = ticks.Count,
                    computePenetrationEvaluatedObservations = computePenetrationEvaluatedObservations,
                    computePenetrationReportedOverlapObservations = overlaps,
                    computePenetrationNoOverlapObservations = noOverlaps,
                    computePenetrationUnavailableObservations = unavailable,
                    separationDistanceAvailableObservations = distanceAvailable,
                    firstConsumedTick = firstConsumedTick == int.MaxValue ? 0 : firstConsumedTick,
                    lastConsumedTick = lastConsumedTick,
                    minimumReportedSeparationDistanceMeters = float.IsPositiveInfinity(minimumDistance) ? "unavailable" : minimumDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    maximumReportedSeparationDistanceMeters = float.IsNegativeInfinity(maximumDistance) ? "unavailable" : maximumDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    enemyCollider = enemyCollider,
                    worldCollider = worldCollider,
                    samplesTruncated = samplesTruncated,
                    samples = samples.ToArray()
                };
            }
        }

        sealed class ColliderDiagnosticBucket
        {
            public string channel;
            public string colliderKind;
            public string colliderName;
            public string face;
            public int observations;
            public int firstConsumedTick = int.MaxValue;
            public int lastConsumedTick;
            public readonly HashSet<string> pairs = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> examples = new List<string>();

            public ColliderDiagnosticAggregate ToReport()
            {
                return new ColliderDiagnosticAggregate
                {
                    channel = channel,
                    colliderKind = colliderKind,
                    colliderName = colliderName,
                    face = face,
                    observations = observations,
                    distinctEnemyColliderPairs = pairs.Count,
                    firstConsumedTick = firstConsumedTick == int.MaxValue ? 0 : firstConsumedTick,
                    lastConsumedTick = lastConsumedTick,
                    representativeSamples = examples.ToArray()
                };
            }
        }

        sealed class AttackCapsulePairBucket
        {
            public string pairKey;
            public string attackPointId;
            public string enemyEntityId;
            public string enemyTicketId;
            public string enemyArchetype;
            public ColliderIdentityDiagnostic attackCapsule;
            public ColliderIdentityDiagnostic enemyCollider;
            public string enemySemanticRole;
            public bool enemyRoleClassificationInconclusive;
            public bool isAssignedTargetAttackPoint;
            public int observations;
            public int computePenetrationEvaluatedObservations;
            public int computePenetrationOverlapObservations;
            public int computePenetrationNoOverlapObservations;
            public int computePenetrationUnavailableObservations;
            public int positiveVolumeOverlapObservations;
            public int overlapDepthUnresolvedObservations;
            public int positiveGapMeasurementObservations;
            public int nonPositiveSurfaceClearanceObservations;
            public int unavailableSurfaceGapObservations;
            public int terminalAttackPoseObservations;
            public int terminalTargetPairObservations;
            public int terminalTargetPairPositiveGapObservations;
            public int terminalTargetPairNonPositiveClearanceObservations;
            public int terminalTargetPairUnsupportedGapObservations;
            public int terminalTargetOverlapObservations;
            public int terminalTargetPositiveVolumeOverlapObservations;
            public int terminalTargetOverlapDepthUnresolvedObservations;
            public int firstConsumedTick = int.MaxValue;
            public int lastConsumedTick;
            public float minimumPositiveGap = float.PositiveInfinity;
            public float minimumTerminalPositiveGap = float.PositiveInfinity;
            public int firstTerminalConsumedTick;
            public int minimumTerminalPositiveGapConsumedTick;
            public string minimumTerminalPositiveGapActorPosition = "unavailable";
            public string terminalPoseActorPosition = "unavailable";
            public int minimumPositiveGapConsumedTick;
            public string minimumPositiveGapActorPosition = "unavailable";
            public AttackCapsulePairSample firstSample;
            public AttackCapsulePairSample firstOverlapSample;
            public AttackCapsulePairSample minimumPositiveGapSample;
            public AttackCapsulePairSample terminalSample;
            public AttackCapsulePairSample minimumTerminalPositiveGapSample;
            public AttackCapsulePairSample lastSample;
            public readonly HashSet<int> ticks = new HashSet<int>();

            public void Add(AttackCapsulePairSample sample, float positiveGap)
            {
                observations++;
                ticks.Add(sample.replayConsumedTick);
                if (sample.replayConsumedTick < firstConsumedTick) firstConsumedTick = sample.replayConsumedTick;
                if (sample.replayConsumedTick > lastConsumedTick) lastConsumedTick = sample.replayConsumedTick;
                if (firstSample == null) firstSample = sample;
                lastSample = sample;
                if (sample.coreTerminalStandOffReached)
                {
                    terminalAttackPoseObservations++;
                    if (terminalSample == null)
                    {
                        terminalSample = sample;
                        firstTerminalConsumedTick = sample.replayConsumedTick;
                        terminalPoseActorPosition = sample.actorPosition;
                    }
                    if (sample.isAssignedTargetAttackPoint && sample.bothCollidersActive)
                    {
                        terminalTargetPairObservations++;
                        if (sample.positiveVolumeOverlapReported)
                        {
                            terminalTargetOverlapObservations++;
                            terminalTargetPositiveVolumeOverlapObservations++;
                        }
                        else if (sample.overlapDepthUnresolved)
                            terminalTargetOverlapDepthUnresolvedObservations++;
                        if (!sample.surfaceGapMeasurementSupported)
                            terminalTargetPairUnsupportedGapObservations++;
                        else if (!sample.positiveSurfaceGapAvailable)
                            terminalTargetPairNonPositiveClearanceObservations++;
                        else
                        {
                            terminalTargetPairPositiveGapObservations++;
                            if (positiveGap < minimumTerminalPositiveGap)
                            {
                                minimumTerminalPositiveGap = positiveGap;
                                minimumTerminalPositiveGapConsumedTick = sample.replayConsumedTick;
                                minimumTerminalPositiveGapActorPosition = sample.actorPosition;
                                minimumTerminalPositiveGapSample = sample;
                            }
                        }
                    }
                }
                if (!sample.bothCollidersActive)
                {
                    // Inactive/disabled candidates are accounted for separately; they are not an API failure.
                }
                else if (!sample.computePenetrationEvaluated) computePenetrationUnavailableObservations++;
                else
                {
                    computePenetrationEvaluatedObservations++;
                    if (sample.positiveVolumeOverlapReported) positiveVolumeOverlapObservations++;
                    if (sample.overlapDepthUnresolved) overlapDepthUnresolvedObservations++;
                    if (sample.computePenetrationReturnedOverlap)
                    {
                        computePenetrationOverlapObservations++;
                        if (firstOverlapSample == null) firstOverlapSample = sample;
                    }
                    else computePenetrationNoOverlapObservations++;
                }
                if (!sample.surfaceGapMeasurementSupported) unavailableSurfaceGapObservations++;
                else if (!sample.positiveSurfaceGapAvailable) nonPositiveSurfaceClearanceObservations++;
                else
                {
                    positiveGapMeasurementObservations++;
                    if (positiveGap < minimumPositiveGap)
                    {
                        minimumPositiveGap = positiveGap;
                        minimumPositiveGapConsumedTick = sample.replayConsumedTick;
                        minimumPositiveGapActorPosition = sample.actorPosition;
                        minimumPositiveGapSample = sample;
                    }
                }
            }

            public AttackCapsulePairAggregate ToReport()
            {
                var samples = new List<AttackCapsulePairSample>();
                AddUnique(samples, firstSample);
                AddUnique(samples, firstOverlapSample);
                AddUnique(samples, minimumPositiveGapSample);
                AddUnique(samples, terminalSample);
                AddUnique(samples, minimumTerminalPositiveGapSample);
                AddUnique(samples, lastSample);
                return new AttackCapsulePairAggregate
                {
                    pairKey = pairKey,
                    attackPointId = attackPointId,
                    enemyEntityId = enemyEntityId,
                    enemyTicketId = enemyTicketId,
                    enemyArchetype = enemyArchetype,
                    attackCapsule = attackCapsule,
                    enemyCollider = enemyCollider,
                    enemySemanticRole = enemySemanticRole,
                    enemyRoleClassificationInconclusive = enemyRoleClassificationInconclusive,
                    isAssignedTargetAttackPoint = isAssignedTargetAttackPoint,
                    gateDisposition = isAssignedTargetAttackPoint
                        ? "V34_ASSIGNED_TARGET_AP_GATE_INCLUDED"
                        : "INFORMATIONAL_NON_TARGET_AP_CROSSING_NOT_A_V34_FAILURE",
                    observations = observations,
                    distinctConsumedTickObservations = ticks.Count,
                    computePenetrationEvaluatedObservations = computePenetrationEvaluatedObservations,
                    computePenetrationOverlapObservations = computePenetrationOverlapObservations,
                    computePenetrationNoOverlapObservations = computePenetrationNoOverlapObservations,
                    computePenetrationUnavailableObservations = computePenetrationUnavailableObservations,
                    positiveVolumeOverlapObservations = positiveVolumeOverlapObservations,
                    overlapDepthUnresolvedObservations = overlapDepthUnresolvedObservations,
                    positiveGapMeasurementObservations = positiveGapMeasurementObservations,
                    nonPositiveSurfaceClearanceObservations = nonPositiveSurfaceClearanceObservations,
                    unavailableSurfaceGapObservations = unavailableSurfaceGapObservations,
                    terminalAttackPoseObservations = terminalAttackPoseObservations,
                    terminalTargetPairObservations = terminalTargetPairObservations,
                    terminalTargetPairPositiveGapObservations = terminalTargetPairPositiveGapObservations,
                    terminalTargetPairNonPositiveClearanceObservations = terminalTargetPairNonPositiveClearanceObservations,
                    terminalTargetPairUnsupportedGapObservations = terminalTargetPairUnsupportedGapObservations,
                    terminalTargetOverlapObservations = terminalTargetOverlapObservations,
                    terminalTargetPositiveVolumeOverlapObservations = terminalTargetPositiveVolumeOverlapObservations,
                    terminalTargetOverlapDepthUnresolvedObservations = terminalTargetOverlapDepthUnresolvedObservations,
                    firstTerminalConsumedTick = firstTerminalConsumedTick,
                    minimumTerminalPositiveGapMeters = float.IsPositiveInfinity(minimumTerminalPositiveGap)
                        ? "unavailable" : minimumTerminalPositiveGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    minimumTerminalPositiveGapConsumedTick = minimumTerminalPositiveGapConsumedTick,
                    minimumTerminalPositiveGapActorPosition = minimumTerminalPositiveGapActorPosition,
                    terminalPoseActorPosition = terminalPoseActorPosition,
                    firstConsumedTick = firstConsumedTick == int.MaxValue ? 0 : firstConsumedTick,
                    lastConsumedTick = lastConsumedTick,
                    minimumPositiveGapMeters = float.IsPositiveInfinity(minimumPositiveGap)
                        ? "unavailable" : minimumPositiveGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    minimumPositiveGapConsumedTick = minimumPositiveGapConsumedTick,
                    minimumPositiveGapActorPosition = minimumPositiveGapActorPosition,
                    minimumPositiveGapSampleContext = minimumPositiveGapSample == null
                        ? "NO_POSITIVE_GAP_MEASUREMENT_RECORDED" : minimumPositiveGapSample.poseContext,
                    representativeSamples = samples.ToArray()
                };
            }

            static void AddUnique(List<AttackCapsulePairSample> samples, AttackCapsulePairSample sample)
            {
                if (sample != null && !samples.Contains(sample)) samples.Add(sample);
            }
        }

        sealed class ColliderDiagnosticAccumulator
        {
            const int MaximumRepresentativePairs = 5;
            const int MaximumRaycastDistance = 180;
            const int OverlapBufferLength = 128;
            const int MaximumPairBuckets = 8192;
            const int MaximumColliderRoleBuckets = 1024;
            const int MaximumPairObservationSamples = 100000;
            const int MaximumAttackCapsulePairBuckets = 8192;
            static readonly string[] ExpectedAttackCapsuleIds = { "AP_A_G", "AP_B_G", "AP_C_G", "AP_A_C", "AP_B_C", "AP_C_C" };

            readonly Dictionary<string, ColliderDiagnosticBucket> buckets = new Dictionary<string, ColliderDiagnosticBucket>(StringComparer.Ordinal);
            readonly Dictionary<string, ColliderPairBucket> pairBuckets = new Dictionary<string, ColliderPairBucket>(StringComparer.Ordinal);
            readonly Dictionary<string, ColliderRoleBucket> roleBuckets = new Dictionary<string, ColliderRoleBucket>(StringComparer.Ordinal);
            readonly Dictionary<string, AttackCapsulePairBucket> attackCapsulePairBuckets = new Dictionary<string, AttackCapsulePairBucket>(StringComparer.Ordinal);
            readonly HashSet<string> observedAttackCapsuleIds = new HashSet<string>(StringComparer.Ordinal);
            readonly List<string> upperLaneUnwitnessed = new List<string>();
            readonly UpperLaneCoreObservationAccumulator upperLaneCoreObservations = new UpperLaneCoreObservationAccumulator();
            readonly Collider[] overlapBuffer = new Collider[OverlapBufferLength];
            readonly Camera playerCamera;
            readonly M4ThreeNightQaSceneEntry sceneEntry;
            readonly M2GrayboxBuilder upperLaneGraybox;
            readonly HashSet<int> overlapTicks = new HashSet<int>();
            int observationStartConsumedTick = int.MaxValue;
            int observationEndConsumedTick;
            int skippedTicks;
            int raycastHits;
            int raycastNoHits;
            int fireCommandTicks;
            int overlapPairObservations;
            int overlapBroadphaseOverflowSamples;
            int physicalColliderPairCandidateObservations;
            int computePenetrationEvaluatedObservations;
            int computePenetrationReportedOverlapObservations;
            int computePenetrationNoOverlapObservations;
            int computePenetrationUnavailableObservations;
            int separationDistanceAvailableObservations;
            int solidStructureGateCandidateObservations;
            int solidStructureGatePenetrationReportedObservations;
            int solidStructureGateUnavailableObservations;
            int permittedAttackCapsuleCandidateObservations;
            int triggerCandidateObservations;
            int structuralTriggerPairObservations;
            int coreClassifiedEnemyHitTriggerPairObservations;
            int unclassifiedTriggerRolePairObservations;
            int inactiveEnemyColliderObservationsSkipped;
            int inactiveWorldColliderCandidateObservations;
            int excludedEnemyActorCandidateObservations;
            int omittedPairObservations;
            int retainedPairObservationSamples;
            int omittedPairObservationSamples;
            int missingAttackCapsuleTickObservations;
            int duplicateAttackCapsuleIds;
            int activeEnemyActorObservations;
            int enemyActorsWithoutColliderObservations;
            int attackCapsulePairCandidateObservations;
            int attackCapsuleComputePenetrationEvaluatedObservations;
            int attackCapsuleComputePenetrationOverlapObservations;
            int attackCapsuleComputePenetrationNoOverlapObservations;
            int attackCapsuleComputePenetrationUnavailableObservations;
            int inactiveOrDisabledAttackCapsulePairObservations;
            int inactiveAssignedTargetCapsulePairObservations;
            int attackCapsulePositiveGapMeasurementObservations;
            int attackCapsuleNonPositiveClearanceObservations;
            int attackCapsuleUnavailableGapObservations;
            int attackCapsuleUnsupportedPairObservations;
            int attackCapsuleUnclassifiedEnemyTriggerPairObservations;
            int attackCapsuleUnclassifiedAssignedTargetTriggerPairObservations;
            int omittedAttackCapsulePairObservations;
            int upperLaneActorTicks;
            int upperLaneWitnessedActorTicks;
            int upperLaneUnavailableActorTicks;
            int upperLaneStationRays;
            bool upperLaneExamplesTruncated;
            readonly List<AttackCapsuleTerminalPoseDiagnostic> terminalAttackPoses = new List<AttackCapsuleTerminalPoseDiagnostic>();
            readonly HashSet<string> terminalActorKeys = new HashSet<string>(StringComparer.Ordinal);
            bool pairBucketCapReached;
            bool colliderRoleBucketCapReached;
            bool pairObservationSampleCapReached;
            bool attackCapsulePairBucketCapReached;
            float minimumObservedPositiveAttackCapsuleGap = float.PositiveInfinity;
            string minimumObservedPositiveAttackCapsulePairKey = string.Empty;
            string minimumObservedPositiveAttackCapsuleId = string.Empty;
            string minimumObservedPositiveAttackCapsuleEnemyId = string.Empty;
            string minimumObservedPositiveAttackCapsuleEnemyPath = string.Empty;
            int minimumObservedPositiveAttackCapsuleTick;

            public ColliderDiagnosticAccumulator()
            {
                sceneEntry = UnityEngine.Object.FindFirstObjectByType<M4ThreeNightQaSceneEntry>();
                upperLaneGraybox = UnityEngine.Object.FindFirstObjectByType<M2GrayboxBuilder>();
                Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (Camera candidate in cameras)
                    if (candidate != null && candidate.name == "QA_PlayerCamera")
                    {
                        playerCamera = candidate;
                        break;
                    }
            }

            public void RecordSkippedTicks(int count)
            {
                if (count > 0) skippedTicks += count;
            }

            public void ObserveTick(M4ThreeNightQaSceneEntry entry, M4NightOneGrayboxController controller, int consumedTicks, M4ReplayTick command)
            {
                if (consumedTicks < observationStartConsumedTick) observationStartConsumedTick = consumedTicks;
                if (consumedTicks > observationEndConsumedTick) observationEndConsumedTick = consumedTicks;
                Physics.SyncTransforms();
                upperLaneCoreObservations.Observe(entry, controller, consumedTicks, command);
                ObserveEnemyStructureOverlaps(consumedTicks);
                ObserveAttackCapsuleEnemyPairs(consumedTicks);
                ObserveUpperLaneGeometry(consumedTicks);
                if (command != null && command.firePressed)
                    ObserveFireCommandRaycast(consumedTicks);
            }

            void ObserveUpperLaneGeometry(int consumedTicks)
            {
                ActiveEnemyActor[] actors = UnityEngine.Object.FindObjectsByType<ActiveEnemyActor>(FindObjectsSortMode.None);
                foreach (ActiveEnemyActor actor in actors)
                {
                    if (actor == null || !actor.isActiveAndEnabled) continue;
                    upperLaneActorTicks++;
                    Collider target = actor.GetComponent<Collider>();
                    if (upperLaneGraybox == null || upperLaneGraybox.Geometry == null ||
                        target == null || !target.enabled || !target.gameObject.activeInHierarchy)
                    {
                        upperLaneUnavailableActorTicks++;
                        RecordUpperLaneUnwitnessed(consumedTicks, actor, "MISSING_CORE_GEOMETRY_OR_ACTIVE_TARGET_COLLIDER");
                        continue;
                    }
                    bool witnessed = false;
                    StationId[] canonical = TowerLineOfSightContract.GetCanonicalStations(actor.TargetAttackPoint);
                    foreach (StationId stationId in canonical)
                    {
                        StationDescriptor? station = null;
                        foreach (StationDescriptor candidate in upperLaneGraybox.Geometry.Stations)
                            if (candidate.Id == stationId) { station = candidate; break; }
                        if (!station.HasValue || !station.Value.IsAvailable) continue;
                        Vector3 delta = target.bounds.center - station.Value.EyePosition;
                        float distance = delta.magnitude;
                        if (distance <= 0f) continue;
                        upperLaneStationRays++;
                        if (Physics.Raycast(station.Value.EyePosition, delta / distance, out RaycastHit hit, distance + 0.02f) &&
                            hit.collider != null && hit.collider.GetComponent<ActiveEnemyActor>() == actor)
                        {
                            witnessed = true;
                            break;
                        }
                    }
                    if (witnessed) upperLaneWitnessedActorTicks++;
                    else RecordUpperLaneUnwitnessed(consumedTicks, actor, "NO_CANONICAL_STATION_FIRST_HIT_WITNESS");
                }
            }

            void RecordUpperLaneUnwitnessed(int consumedTicks, ActiveEnemyActor actor, string reason)
            {
                if (upperLaneUnwitnessed.Count < 32)
                    upperLaneUnwitnessed.Add("tick=" + consumedTicks + ";entity=" + actor.EntityId.Value +
                        ";target=" + actor.TargetAttackPoint + ";reason=" + reason);
                else upperLaneExamplesTruncated = true;
            }

            void ObserveAttackCapsuleEnemyPairs(int consumedTicks)
            {
                M2GrayboxColliderTag[] tags = UnityEngine.Object.FindObjectsByType<M2GrayboxColliderTag>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                var attackCapsules = new List<Tuple<string, M2GrayboxColliderTag, Collider>>();
                var attackIdsThisTick = new HashSet<string>(StringComparer.Ordinal);
                Array.Sort(tags, (left, right) =>
                {
                    if (left == null) return right == null ? 0 : 1;
                    if (right == null) return -1;
                    int nameOrder = string.CompareOrdinal(left.gameObject.name, right.gameObject.name);
                    return nameOrder != 0 ? nameOrder : left.GetInstanceID().CompareTo(right.GetInstanceID());
                });
                foreach (M2GrayboxColliderTag tag in tags)
                {
                    if (tag == null || tag.Kind != M2GrayboxColliderKind.AttackCapsule) continue;
                    string attackPointId = tag.gameObject.name;
                    observedAttackCapsuleIds.Add(attackPointId);
                    if (!attackIdsThisTick.Add(attackPointId)) duplicateAttackCapsuleIds++;
                    Collider capsuleCollider = tag.GetComponent<Collider>();
                    if (capsuleCollider == null)
                    {
                        missingAttackCapsuleTickObservations++;
                        continue;
                    }
                    attackCapsules.Add(Tuple.Create(attackPointId, tag, capsuleCollider));
                }
                foreach (string expectedId in ExpectedAttackCapsuleIds)
                    if (!attackIdsThisTick.Contains(expectedId)) missingAttackCapsuleTickObservations++;

                ActiveEnemyActor[] foundActors = UnityEngine.Object.FindObjectsByType<ActiveEnemyActor>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                Array.Sort(foundActors, (left, right) =>
                {
                    if (left == null) return right == null ? 0 : 1;
                    if (right == null) return -1;
                    int entityOrder = left.EntityId.Value.CompareTo(right.EntityId.Value);
                    if (entityOrder != 0) return entityOrder;
                    int ticketOrder = left.TicketId.CompareTo(right.TicketId);
                    return ticketOrder != 0 ? ticketOrder : string.CompareOrdinal(GetFullTransformPath(left.transform), GetFullTransformPath(right.transform));
                });
                foreach (ActiveEnemyActor actor in foundActors)
                {
                    if (actor == null || !actor.isActiveAndEnabled) continue;
                    activeEnemyActorObservations++;
                    Collider[] foundEnemyColliders = actor.GetComponentsInChildren<Collider>(true);
                    Array.Sort(foundEnemyColliders, (left, right) =>
                    {
                        if (left == null) return right == null ? 0 : 1;
                        if (right == null) return -1;
                        int pathOrder = string.CompareOrdinal(GetFullTransformPath(left.transform), GetFullTransformPath(right.transform));
                        if (pathOrder != 0) return pathOrder;
                        int nameOrder = string.CompareOrdinal(left.name, right.name);
                        return nameOrder != 0 ? nameOrder : left.GetInstanceID().CompareTo(right.GetInstanceID());
                    });
                    var enemyColliders = new List<Collider>();
                    foreach (Collider enemyCollider in foundEnemyColliders)
                        if (enemyCollider != null) enemyColliders.Add(enemyCollider);
                    if (enemyColliders.Count == 0) enemyActorsWithoutColliderObservations++;

                    bool terminalPoseFirstObserved = RecordTerminalAttackPose(actor, attackCapsules,
                        enemyColliders, consumedTicks);

                    foreach (Tuple<string, M2GrayboxColliderTag, Collider> attack in attackCapsules)
                    foreach (Collider enemyCollider in enemyColliders)
                    {
                        if (enemyCollider == attack.Item3) continue;
                        RecordAttackCapsuleEnemyPair(attack.Item1, attack.Item2, attack.Item3,
                            actor, enemyCollider, consumedTicks, terminalPoseFirstObserved);
                    }
                }
            }

            bool RecordTerminalAttackPose(ActiveEnemyActor actor,
                List<Tuple<string, M2GrayboxColliderTag, Collider>> attackCapsules,
                List<Collider> enemyColliders, int consumedTicks)
            {
                if (!actor.HasReachedAttackCapsuleStandOff) return false;
                string targetAttackPointId = actor.TargetAttackPoint.ToString();
                string key = actor.EntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    actor.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + targetAttackPointId;
                if (!terminalActorKeys.Add(key)) return false;

                var targetMatches = new List<Tuple<string, M2GrayboxColliderTag, Collider>>();
                foreach (Tuple<string, M2GrayboxColliderTag, Collider> attack in attackCapsules)
                    if (string.Equals(attack.Item1, targetAttackPointId, StringComparison.Ordinal))
                        targetMatches.Add(attack);
                Tuple<string, M2GrayboxColliderTag, Collider> target = targetMatches.Count == 1 ? targetMatches[0] : null;

                Collider actorRootCollider = actor.GetComponent<Collider>();
                CapsuleCollider movingCapsule = actorRootCollider as CapsuleCollider;
                CapsuleCollider targetCapsule = target == null ? null : target.Item3 as CapsuleCollider;
                float toolSignedGap = float.NaN;
                bool toolRootGapAvailable = movingCapsule != null && targetCapsule != null &&
                    M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(movingCapsule, actor.transform.position,
                        actor.transform.rotation, targetCapsule, out toolSignedGap);
                bool coreGapAvailable = actor.HasMeasuredAttackCapsuleSurfaceGap && IsFinite(actor.AttackCapsuleSurfaceGapMeters);
                bool exactGapMatch = coreGapAvailable && toolRootGapAvailable &&
                    actor.AttackCapsuleSurfaceGapMeters == toolSignedGap;

                int activeColliderCount = 0;
                int unsupportedColliderCount = 0;
                int unclassifiedTriggerCount = 0;
                foreach (Collider enemyCollider in enemyColliders)
                {
                    if (enemyCollider == null || !enemyCollider.enabled || !enemyCollider.gameObject.activeInHierarchy) continue;
                    activeColliderCount++;
                    bool hasGap = targetCapsule != null && enemyCollider is CapsuleCollider &&
                        TryMeasurePositiveCapsuleGap(target.Item3, enemyCollider, out _, out _);
                    if (!hasGap) unsupportedColliderCount++;
                    if (enemyCollider.isTrigger)
                    {
                        ClassifyEnemyCollider(enemyCollider, actor, out _, out _, out _,
                            out bool roleInconclusive, out _);
                        if (roleInconclusive) unclassifiedTriggerCount++;
                    }
                }

                terminalAttackPoses.Add(new AttackCapsuleTerminalPoseDiagnostic
                {
                    replayConsumedTick = consumedTicks,
                    unityFixedTimeSeconds = Time.fixedTime,
                    sceneName = SceneManager.GetActiveScene().name,
                    activeNight = sceneEntry == null ? 0 : sceneEntry.ActiveNightOrdinal,
                    qaStage = sceneEntry == null ? "QA_SCENE_ENTRY_MISSING" : sceneEntry.CurrentStage.ToString(),
                    entityId = actor.EntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ticketId = actor.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    enemyArchetype = actor.Archetype.ToString(),
                    targetAttackPointId = targetAttackPointId,
                    actorTransformPath = GetFullTransformPath(actor.transform),
                    actorPosition = FormatVector(actor.transform.position),
                    actorRotation = FormatVector(actor.transform.eulerAngles),
                    hasReachedAttackCapsuleStandOff = actor.HasReachedAttackCapsuleStandOff,
                    hasMeasuredCoreSurfaceGap = coreGapAvailable,
                    coreSurfaceGapMeters = coreGapAvailable
                        ? actor.AttackCapsuleSurfaceGapMeters.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                    rootColliderGapMeasurementAvailable = toolRootGapAvailable,
                    rootColliderSignedGapMeters = toolRootGapAvailable
                        ? toolSignedGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                    coreAndToolsRootGapExactlyEqual = exactGapMatch,
                    targetAttackCapsulePath = target == null ? "unavailable" : GetFullTransformPath(target.Item3.transform),
                    targetCapsuleMappingStatus = target == null
                        ? targetMatches.Count == 0 ? "INCONCLUSIVE_ASSIGNED_AP_NOT_FOUND" : "INCONCLUSIVE_DUPLICATE_ASSIGNED_AP_ID"
                        : targetCapsule == null ? "INCONCLUSIVE_ASSIGNED_AP_IS_NOT_CAPSULE" : "ASSIGNED_AP_FOUND",
                    activeEnemyColliderCount = activeColliderCount,
                    unsupportedActiveEnemyColliderCount = unsupportedColliderCount,
                    unclassifiedActiveTriggerColliderCount = unclassifiedTriggerCount
                });
                return true;
            }

            void RecordAttackCapsuleEnemyPair(string attackPointId, M2GrayboxColliderTag attackTag,
                Collider attackCollider, ActiveEnemyActor actor, Collider enemyCollider, int consumedTicks,
                bool terminalPoseFirstObserved)
            {
                attackCapsulePairCandidateObservations++;
                string enemyRole;
                string enemyRoleEvidence;
                string enemyGateDisposition;
                bool enemySideGateCandidate;
                bool enemyRoleInconclusive;
                ClassifyEnemyCollider(enemyCollider, actor, out enemyRole, out enemyRoleEvidence,
                    out enemySideGateCandidate, out enemyRoleInconclusive, out enemyGateDisposition);
                if (enemyRoleInconclusive) attackCapsuleUnclassifiedEnemyTriggerPairObservations++;
                string targetAttackPointId = actor.TargetAttackPoint.ToString();
                bool isAssignedTargetAttackPoint = string.Equals(attackPointId, targetAttackPointId, StringComparison.Ordinal);
                if (isAssignedTargetAttackPoint && enemyRoleInconclusive)
                    attackCapsuleUnclassifiedAssignedTargetTriggerPairObservations++;

                ColliderIdentityDiagnostic capsuleIdentity = DescribeCollider(attackCollider, null,
                    "ATTACK_CAPSULE_TARGET_QUERY_VOLUME", "M2_GRAYBOX_ATTACK_CAPSULE_TAG_V34_GATE", false, false, false);
                ColliderIdentityDiagnostic enemyIdentity = DescribeCollider(enemyCollider, actor.transform,
                    enemyRole, enemyRoleEvidence, enemyRoleInconclusive, enemySideGateCandidate, false);
                string pairKey = "attackPoint=" + attackPointId + ";assignedTargetAttackPoint=" + targetAttackPointId +
                    ";attackCapsuleInstance=" + attackCollider.GetInstanceID() +
                    ";entity=" + actor.EntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ";ticket=" + actor.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ";enemyColliderInstance=" + enemyCollider.GetInstanceID() +
                    ";enemyColliderPath=" + GetTransformPath(enemyCollider.transform, actor.transform);

                bool bothActive = attackCollider.enabled && attackCollider.gameObject.activeInHierarchy &&
                    enemyCollider.enabled && enemyCollider.gameObject.activeInHierarchy;
                bool targetCapsuleActive = attackCollider.enabled && attackCollider.gameObject.activeInHierarchy;
                bool enemyColliderActive = enemyCollider.enabled && enemyCollider.gameObject.activeInHierarchy;
                if (isAssignedTargetAttackPoint && !targetCapsuleActive)
                    inactiveAssignedTargetCapsulePairObservations++;
                bool computeEvaluated = false;
                bool penetration = false;
                Vector3 penetrationDirection = Vector3.zero;
                float penetrationDepth = float.NaN;
                if (bothActive)
                {
                    computeEvaluated = true;
                    penetration = Physics.ComputePenetration(attackCollider, attackCollider.transform.position, attackCollider.transform.rotation,
                        enemyCollider, enemyCollider.transform.position, enemyCollider.transform.rotation,
                        out penetrationDirection, out penetrationDepth);
                    attackCapsuleComputePenetrationEvaluatedObservations++;
                    if (penetration) attackCapsuleComputePenetrationOverlapObservations++;
                    else attackCapsuleComputePenetrationNoOverlapObservations++;
                }
                else inactiveOrDisabledAttackCapsulePairObservations++;

                bool gapSupported = false;
                bool positiveGap = false;
                float gapMeters = float.NaN;
                if (bothActive)
                    gapSupported = TryMeasurePositiveCapsuleGap(attackCollider, enemyCollider, out positiveGap, out gapMeters);
                if (!bothActive || !gapSupported) attackCapsuleUnavailableGapObservations++;
                else if (positiveGap) attackCapsulePositiveGapMeasurementObservations++;
                else attackCapsuleNonPositiveClearanceObservations++;
                if (bothActive && !gapSupported) attackCapsuleUnsupportedPairObservations++;

                var sample = new AttackCapsulePairSample
                {
                    replayConsumedTick = consumedTicks,
                    unityFixedTimeSeconds = Time.fixedTime,
                    sceneName = SceneManager.GetActiveScene().name,
                    activeNight = sceneEntry == null ? 0 : sceneEntry.ActiveNightOrdinal,
                    qaStage = sceneEntry == null ? "QA_SCENE_ENTRY_MISSING" : sceneEntry.CurrentStage.ToString(),
                    poseContext = terminalPoseFirstObserved
                        ? "FIRST_OBSERVED_CORE_ASSIGNED_TARGET_STANDOFF_POSE"
                        : "OBSERVED_FIXED_TICK; MOVEMENT_STATE_NOT_EXPOSED",
                    enemyMovementState = "NOT_EXPOSED_BY_CORE",
                    attackPointId = attackPointId,
                    targetAttackPointId = targetAttackPointId,
                    apRelation = isAssignedTargetAttackPoint ? "ASSIGNED_TARGET_GATE_INCLUDED" : "NON_TARGET_INFORMATIONAL_ONLY",
                    isAssignedTargetAttackPoint = isAssignedTargetAttackPoint,
                    isTargetAttackCapsule = isAssignedTargetAttackPoint,
                    bothCollidersActive = bothActive,
                    targetCapsuleActive = targetCapsuleActive,
                    enemyColliderActive = enemyColliderActive,
                    coreTerminalStandOffReached = terminalPoseFirstObserved,
                    entityId = actor.EntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ticketId = actor.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    enemyArchetype = actor.Archetype.ToString(),
                    actorTransformPath = GetFullTransformPath(actor.transform),
                    actorPosition = FormatVector(actor.transform.position),
                    attackCapsule = capsuleIdentity,
                    enemyCollider = enemyIdentity,
                    computePenetrationEvaluated = computeEvaluated,
                    computePenetrationReturnedOverlap = computeEvaluated && penetration,
                    computePenetrationReturnedDirection = computeEvaluated ? FormatVector(penetrationDirection) : "unavailable",
                    computePenetrationDepthAvailable = computeEvaluated && IsFinite(penetrationDepth),
                    computePenetrationReturnedDepthMeters = computeEvaluated && IsFinite(penetrationDepth)
                        ? penetrationDepth.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                    positiveVolumeOverlapReported = computeEvaluated && penetration && IsFinite(penetrationDepth) && penetrationDepth > 0f,
                    overlapDepthUnresolved = computeEvaluated && penetration && (!IsFinite(penetrationDepth) || penetrationDepth <= 0f),
                    surfaceGapMeasurementSupported = gapSupported,
                    positiveSurfaceGapAvailable = gapSupported && positiveGap,
                    signedSurfaceGapMeters = gapSupported
                        ? gapMeters.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                    positiveSurfaceGapMeters = gapSupported && positiveGap
                        ? gapMeters.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        : gapSupported ? "not-positive" : "unavailable",
                    gapUnavailableReason = !bothActive ? "COLLIDER_INACTIVE_OR_DISABLED" : gapSupported ? string.Empty : "UNSUPPORTED_COLLIDER_TYPE_OR_SCALE"
                };

                if (!attackCapsulePairBuckets.TryGetValue(pairKey, out AttackCapsulePairBucket pairBucket))
                {
                    if (attackCapsulePairBuckets.Count >= MaximumAttackCapsulePairBuckets)
                    {
                        attackCapsulePairBucketCapReached = true;
                        omittedAttackCapsulePairObservations++;
                        return;
                    }
                    pairBucket = new AttackCapsulePairBucket
                    {
                        pairKey = pairKey,
                        attackPointId = attackPointId,
                        enemyEntityId = sample.entityId,
                        enemyTicketId = sample.ticketId,
                        enemyArchetype = sample.enemyArchetype,
                        attackCapsule = capsuleIdentity,
                        enemyCollider = enemyIdentity,
                        enemySemanticRole = enemyRole,
                        enemyRoleClassificationInconclusive = enemyRoleInconclusive,
                        isAssignedTargetAttackPoint = isAssignedTargetAttackPoint
                    };
                    attackCapsulePairBuckets.Add(pairKey, pairBucket);
                }
                pairBucket.Add(sample, gapSupported && positiveGap ? gapMeters : float.PositiveInfinity);

                if (isAssignedTargetAttackPoint && gapSupported && positiveGap &&
                    (gapMeters < minimumObservedPositiveAttackCapsuleGap ||
                     (gapMeters == minimumObservedPositiveAttackCapsuleGap && string.CompareOrdinal(pairKey, minimumObservedPositiveAttackCapsulePairKey) < 0)))
                {
                    minimumObservedPositiveAttackCapsuleGap = gapMeters;
                    minimumObservedPositiveAttackCapsulePairKey = pairKey;
                    minimumObservedPositiveAttackCapsuleId = attackPointId;
                    minimumObservedPositiveAttackCapsuleEnemyId = sample.entityId;
                    minimumObservedPositiveAttackCapsuleEnemyPath = enemyIdentity.transformPath;
                    minimumObservedPositiveAttackCapsuleTick = consumedTicks;
                }
            }

            void ObserveEnemyStructureOverlaps(int consumedTicks)
            {
                ActiveEnemyActor[] actors = UnityEngine.Object.FindObjectsByType<ActiveEnemyActor>(FindObjectsSortMode.None);
                bool tickHasOverlap = false;
                foreach (ActiveEnemyActor actor in actors)
                {
                    if (actor == null || !actor.isActiveAndEnabled) continue;
                    Collider[] enemyColliders = actor.GetComponentsInChildren<Collider>();
                    foreach (Collider enemyCollider in enemyColliders)
                    {
                        if (enemyCollider == null) continue;
                        if (!enemyCollider.enabled || !enemyCollider.gameObject.activeInHierarchy) { inactiveEnemyColliderObservationsSkipped++; continue; }
                        Bounds bounds = enemyCollider.bounds;
                        int candidateCount = Physics.OverlapBoxNonAlloc(bounds.center,
                            bounds.extents + Vector3.one * 0.002f, overlapBuffer, Quaternion.identity,
                            Physics.AllLayers, QueryTriggerInteraction.Collide);
                        if (candidateCount == overlapBuffer.Length) overlapBroadphaseOverflowSamples++;
                        for (int i = 0; i < candidateCount; i++)
                        {
                            Collider worldCollider = overlapBuffer[i];
                            if (worldCollider == null || worldCollider == enemyCollider) continue;
                            if (!worldCollider.enabled || !worldCollider.gameObject.activeInHierarchy) inactiveWorldColliderCandidateObservations++;
                            ActiveEnemyActor otherActor = worldCollider.GetComponentInParent<ActiveEnemyActor>();
                            if (otherActor == actor) continue;
                            if (otherActor != null) { excludedEnemyActorCandidateObservations++; continue; }

                            M2GrayboxColliderTag tag = worldCollider.GetComponent<M2GrayboxColliderTag>();
                            string enemyRole;
                            string enemyRoleEvidence;
                            string enemyGateDisposition;
                            bool enemySideGateCandidate;
                            bool enemyRoleInconclusive;
                            ClassifyEnemyCollider(enemyCollider, actor, out enemyRole, out enemyRoleEvidence,
                                out enemySideGateCandidate, out enemyRoleInconclusive, out enemyGateDisposition);
                            string worldRole;
                            string worldRoleEvidence;
                            string worldGateDisposition;
                            bool solidGateCandidate;
                            bool worldRoleInconclusive;
                            bool worldStructuralTrigger;
                            ClassifyWorldCollider(worldCollider, tag, out worldRole, out worldRoleEvidence,
                                out solidGateCandidate, out worldRoleInconclusive, out worldStructuralTrigger,
                                out worldGateDisposition);
                            string gateDisposition = ResolvePairGateDisposition(enemySideGateCandidate,
                                enemyRoleInconclusive, enemyGateDisposition, solidGateCandidate,
                                worldRoleInconclusive, worldGateDisposition, worldRole);
                            ColliderIdentityDiagnostic enemyIdentity = DescribeCollider(enemyCollider, actor.transform,
                                enemyRole, enemyRoleEvidence, enemyRoleInconclusive, enemySideGateCandidate, false);
                            ColliderIdentityDiagnostic worldIdentity = DescribeCollider(worldCollider, null,
                                worldRole, worldRoleEvidence, worldRoleInconclusive, false, solidGateCandidate);
                            string pairKey = "entity=" + actor.EntityId.Value + ";ticket=" + actor.TicketId +
                                ";enemyColliderInstance=" + enemyCollider.GetInstanceID() +
                                ";worldColliderInstance=" + worldCollider.GetInstanceID();
                            bool computeEvaluated = enemyCollider.enabled && worldCollider.enabled &&
                                enemyCollider.gameObject.activeInHierarchy && worldCollider.gameObject.activeInHierarchy;
                            bool penetration = false;
                            Vector3 separationDirection = Vector3.zero;
                            float separationDistance = 0f;
                            bool directionAvailable = false;
                            bool distanceAvailable = false;
                            if (computeEvaluated)
                            {
                                penetration = Physics.ComputePenetration(enemyCollider, enemyCollider.transform.position, enemyCollider.transform.rotation,
                                    worldCollider, worldCollider.transform.position, worldCollider.transform.rotation,
                                    out separationDirection, out separationDistance);
                                directionAvailable = penetration && IsFinite(separationDirection.x) && IsFinite(separationDirection.y) && IsFinite(separationDirection.z);
                                distanceAvailable = penetration && IsFinite(separationDistance);
                                computePenetrationEvaluatedObservations++;
                                if (penetration)
                                {
                                    computePenetrationReportedOverlapObservations++;
                                    tickHasOverlap = true;
                                    overlapPairObservations++;
                                    if (enemySideGateCandidate && solidGateCandidate) solidStructureGatePenetrationReportedObservations++;
                                }
                                else computePenetrationNoOverlapObservations++;
                                if (distanceAvailable) separationDistanceAvailableObservations++;
                            }
                            else
                            {
                                computePenetrationUnavailableObservations++;
                                if (enemySideGateCandidate && solidGateCandidate) solidStructureGateUnavailableObservations++;
                            }
                            if (enemyCollider.isTrigger || worldCollider.isTrigger) triggerCandidateObservations++;
                            physicalColliderPairCandidateObservations++;
                            if (enemySideGateCandidate && solidGateCandidate) solidStructureGateCandidateObservations++;
                            if (worldStructuralTrigger) structuralTriggerPairObservations++;
                            if (enemyRole == "CORE_CLASSIFIED_ENEMY_HIT_TRIGGER" && enemySideGateCandidate)
                                coreClassifiedEnemyHitTriggerPairObservations++;
                            if (enemyRoleInconclusive || worldRoleInconclusive)
                                unclassifiedTriggerRolePairObservations++;
                            if (tag != null && tag.Kind == M2GrayboxColliderKind.AttackCapsule) permittedAttackCapsuleCandidateObservations++;

                            string pairRole = "ENEMY_" + enemyRole + "_TO_WORLD_" + worldRole;
                            string kind = tag == null ? "UntaggedWorldCollider" : tag.Kind.ToString();
                            string face = tag != null && tag.Face.HasValue ? tag.Face.Value.ToString() : string.Empty;
                            AddColliderPairObservation(pairKey, pairRole, gateDisposition, enemyIdentity, worldIdentity,
                                actor, enemyCollider, worldCollider, consumedTicks, computeEvaluated, penetration,
                                directionAvailable, separationDirection, distanceAvailable, separationDistance,
                                kind, face);
                        }
                    }
                }
                if (tickHasOverlap) overlapTicks.Add(consumedTicks);
            }

            void AddColliderPairObservation(string pairKey, string pairRole, string gateDisposition,
                ColliderIdentityDiagnostic enemyIdentity, ColliderIdentityDiagnostic worldIdentity,
                ActiveEnemyActor actor, Collider enemyCollider, Collider worldCollider, int consumedTicks,
                bool computeEvaluated, bool penetration, bool directionAvailable, Vector3 direction,
                bool distanceAvailable, float distance, string kind, string face)
            {
                UpdateColliderRoleAggregate(pairKey, pairRole, kind, worldCollider.name, face,
                    consumedTicks, computeEvaluated, penetration);
                if (!pairBuckets.TryGetValue(pairKey, out ColliderPairBucket pairBucket))
                {
                    if (pairBuckets.Count >= MaximumPairBuckets)
                    {
                        pairBucketCapReached = true;
                        omittedPairObservations++;
                        return;
                    }
                    pairBucket = new ColliderPairBucket
                    {
                        pairKey = pairKey,
                        pairRole = pairRole,
                        gateDisposition = gateDisposition,
                        enemyCollider = enemyIdentity,
                        worldCollider = worldIdentity
                    };
                    pairBuckets.Add(pairKey, pairBucket);
                }

                var sample = new ColliderPairSample
                {
                    replayConsumedTick = consumedTicks,
                    unityFixedTimeSeconds = Time.fixedTime,
                    sceneName = SceneManager.GetActiveScene().name,
                    activeNight = sceneEntry == null ? 0 : sceneEntry.ActiveNightOrdinal,
                    stage = sceneEntry == null ? "QA_SCENE_ENTRY_MISSING" : sceneEntry.CurrentStage.ToString(),
                    entityId = actor.EntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ticketId = actor.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    enemyArchetype = actor.Archetype.ToString(),
                    actorTransformPath = GetFullTransformPath(actor.transform),
                    actorPosition = FormatVector(actor.transform.position),
                    enemyColliderPosition = FormatVector(enemyCollider.transform.position),
                    worldColliderPosition = FormatVector(worldCollider.transform.position),
                    computePenetrationEvaluated = computeEvaluated,
                    computePenetrationReturnedOverlap = computeEvaluated && penetration,
                    separationDirectionAvailable = directionAvailable,
                    returnedSeparationDirection = directionAvailable ? FormatVector(direction) : "unavailable",
                    separationDistanceAvailable = distanceAvailable,
                    returnedSeparationDistanceMeters = distanceAvailable
                        ? distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        : "unavailable"
                };
                bool retainRawSample = retainedPairObservationSamples < MaximumPairObservationSamples;
                if (retainRawSample) retainedPairObservationSamples++;
                else
                {
                    pairObservationSampleCapReached = true;
                    omittedPairObservationSamples++;
                }
                pairBucket.Add(sample, retainRawSample);
            }

            void UpdateColliderRoleAggregate(string pairKey, string pairRole, string kind, string colliderName,
                string face, int consumedTicks, bool computeEvaluated, bool penetration)
            {
                string roleKey = pairRole + "\n" + kind + "\n" + colliderName + "\n" + face;
                if (!roleBuckets.TryGetValue(roleKey, out ColliderRoleBucket roleBucket))
                {
                    if (roleBuckets.Count >= MaximumColliderRoleBuckets)
                    {
                        colliderRoleBucketCapReached = true;
                        return;
                    }
                    roleBucket = new ColliderRoleBucket
                    {
                        pairRole = pairRole,
                        colliderKind = kind,
                        colliderName = colliderName,
                        face = face
                    };
                    roleBuckets.Add(roleKey, roleBucket);
                }
                roleBucket.candidateObservations++;
                roleBucket.pairs.Add(pairKey);
                if (consumedTicks < roleBucket.firstConsumedTick) roleBucket.firstConsumedTick = consumedTicks;
                if (consumedTicks > roleBucket.lastConsumedTick) roleBucket.lastConsumedTick = consumedTicks;
                if (!computeEvaluated) roleBucket.unavailable++;
                else if (penetration) roleBucket.overlaps++;
                else roleBucket.noOverlaps++;
            }

            static ColliderIdentityDiagnostic DescribeCollider(Collider collider, Transform pathRoot,
                string semanticRole, string roleEvidence, bool roleInconclusive,
                bool enemySideGateCandidate, bool solidStructureGateCandidate)
            {
                M2GrayboxColliderTag tag = collider.GetComponent<M2GrayboxColliderTag>();
                string kind = tag == null ? "UntaggedWorldCollider" : tag.Kind.ToString();
                string face = tag != null && tag.Face.HasValue ? tag.Face.Value.ToString() : string.Empty;
                return new ColliderIdentityDiagnostic
                {
                    instanceId = collider.GetInstanceID(),
                    colliderName = collider.name,
                    transformPath = pathRoot == null ? GetFullTransformPath(collider.transform) : GetTransformPath(collider.transform, pathRoot),
                    gameObjectTag = collider.gameObject.tag,
                    layer = collider.gameObject.layer,
                    layerName = LayerMask.LayerToName(collider.gameObject.layer),
                    activeSelf = collider.gameObject.activeSelf,
                    activeInHierarchy = collider.gameObject.activeInHierarchy,
                    componentEnabled = collider.enabled,
                    isTrigger = collider.isTrigger,
                    hasGrayboxTag = tag != null,
                    grayboxTagComponentEnabled = tag != null && tag.isActiveAndEnabled,
                    colliderKind = kind,
                    face = face,
                    semanticRole = semanticRole,
                    roleClassificationEvidence = roleEvidence,
                    roleClassificationInconclusive = roleInconclusive,
                    enemySideGateCandidate = enemySideGateCandidate,
                    solidStructureGateCandidate = solidStructureGateCandidate
                };
            }

            static void ClassifyEnemyCollider(Collider collider, ActiveEnemyActor actor,
                out string semanticRole, out string roleEvidence, out bool enemySideGateCandidate,
                out bool roleInconclusive, out string gateDisposition)
            {
                enemySideGateCandidate = false;
                roleInconclusive = false;
                bool active = collider.enabled && collider.gameObject.activeInHierarchy;
                bool coreShotTarget = collider.GetComponent<ActiveEnemyActor>() == actor;
                roleEvidence = coreShotTarget
                    ? "ACTIVE_ENEMY_ACTOR_COMPONENT_ON_COLLIDER_USED_BY_CORE_SHOT_RESOLVER"
                    : "COLLIDER_IN_ACTIVE_ENEMY_ACTOR_HIERARCHY";

                if (!active)
                {
                    semanticRole = collider.isTrigger ? "INACTIVE_ENEMY_TRIGGER" : "INACTIVE_ENEMY_COLLIDER";
                    gateDisposition = "INACTIVE_ENEMY_COLLIDER_NOT_A_GATE_CANDIDATE";
                    return;
                }
                if (collider.isTrigger)
                {
                    if (coreShotTarget)
                    {
                        semanticRole = "CORE_CLASSIFIED_ENEMY_HIT_TRIGGER";
                        enemySideGateCandidate = true;
                        gateDisposition = "V32_ENEMY_HIT_TRIGGER_SIDE_CANDIDATE";
                    }
                    else
                    {
                        semanticRole = "UNCLASSIFIED_ENEMY_TRIGGER_ROLE";
                        roleInconclusive = true;
                        gateDisposition = "INCONCLUSIVE_UNCLASSIFIED_ENEMY_TRIGGER_NOT_A_PASS";
                    }
                    return;
                }

                semanticRole = coreShotTarget ? "CORE_CLASSIFIED_ENEMY_HIT_COLLIDER" : "ENEMY_PHYSICAL_COLLIDER";
                enemySideGateCandidate = true;
                gateDisposition = "V32_ENEMY_PHYSICAL_OR_HIT_SIDE_CANDIDATE";
            }

            static void ClassifyWorldCollider(Collider collider, M2GrayboxColliderTag tag,
                out string semanticRole, out string roleEvidence, out bool solidGateCandidate,
                out bool roleInconclusive, out bool structuralTrigger, out string gateDisposition)
            {
                solidGateCandidate = false;
                roleInconclusive = false;
                structuralTrigger = false;
                bool active = collider.enabled && collider.gameObject.activeInHierarchy;
                bool attackCapsule = tag != null && tag.Kind == M2GrayboxColliderKind.AttackCapsule;
                bool namedPattern = IsNamedFacePatternCollider(collider.name);
                bool taggedStructure = tag != null && IsStructuralColliderKind(tag.Kind);
                roleEvidence = attackCapsule ? "M2_GRAYBOX_ATTACK_CAPSULE_TAG" :
                    taggedStructure ? "M2_GRAYBOX_STRUCTURAL_KIND_TAG" :
                    namedPattern ? "APPROVED_FACE_PATTERN_NAME_MAPPING" : "NO_APPROVED_COLLIDER_ROLE_MAPPING";

                if (attackCapsule)
                {
                    semanticRole = "ATTACK_CAPSULE_TARGET_QUERY_VOLUME";
                    gateDisposition = "SEPARATE_V34_ATTACK_CAPSULE_GATE_NOT_V32_SOLID_STRUCTURE";
                    return;
                }
                if (taggedStructure || namedPattern)
                {
                    if (collider.isTrigger)
                    {
                        semanticRole = "STRUCTURAL_TRIGGER_NON_SOLID";
                        structuralTrigger = true;
                        gateDisposition = "STRUCTURAL_TRIGGER_NON_SOLID_OUTSIDE_V32_GATE";
                    }
                    else
                    {
                        semanticRole = "STRUCTURAL_SOLID";
                        if (active)
                        {
                            solidGateCandidate = true;
                            gateDisposition = namedPattern
                                ? "STRUCTURAL_SOLID_V32_GATE_INCLUDED_PATTERN_V32_06_MOVEMENT_AND_SHOT_ROLE_OPEN"
                                : "STRUCTURAL_SOLID_V32_GATE_INCLUDED";
                        }
                        else gateDisposition = "INACTIVE_STRUCTURAL_SOLID_NOT_A_GATE_CANDIDATE";
                    }
                    return;
                }
                if (collider.isTrigger)
                {
                    semanticRole = "UNCLASSIFIED_TRIGGER_ROLE";
                    roleInconclusive = active;
                    gateDisposition = active
                        ? "INCONCLUSIVE_UNCLASSIFIED_TRIGGER_ROLE_NOT_A_PASS"
                        : "INACTIVE_UNCLASSIFIED_TRIGGER_NOT_A_GATE_CANDIDATE";
                    return;
                }
                if (!active)
                {
                    semanticRole = "INACTIVE_UNCLASSIFIED_NONTRIGGER_COLLIDER";
                    gateDisposition = "INACTIVE_UNCLASSIFIED_COLLIDER_NOT_A_GATE_CANDIDATE";
                    return;
                }
                semanticRole = "UNCLASSIFIED_NONTRIGGER_GEOMETRY";
                gateDisposition = "MEASURED_UNCLASSIFIED_NONTRIGGER_NOT_IN_V32_GATE";
            }

            static string ResolvePairGateDisposition(bool enemyCandidate, bool enemyRoleInconclusive,
                string enemyDisposition, bool worldCandidate, bool worldRoleInconclusive,
                string worldDisposition, string worldSemanticRole)
            {
                if (enemyRoleInconclusive || worldRoleInconclusive)
                    return "INCONCLUSIVE_UNCLASSIFIED_TRIGGER_ROLE_NOT_A_PASS";
                if (worldSemanticRole == "ATTACK_CAPSULE_TARGET_QUERY_VOLUME")
                    return "SEPARATE_V34_ATTACK_CAPSULE_GATE_NOT_V32_SOLID_STRUCTURE";
                if (worldSemanticRole == "STRUCTURAL_TRIGGER_NON_SOLID")
                    return "STRUCTURAL_TRIGGER_NON_SOLID_OUTSIDE_V32_GATE";
                if (enemyCandidate && worldCandidate)
                    return "V32_GATE_INCLUDED_PAIR_CANDIDATE_NO_PASS_INFERRED";
                if (!worldCandidate) return worldDisposition;
                return enemyDisposition;
            }

            static bool IsStructuralColliderKind(M2GrayboxColliderKind kind)
            {
                return kind == M2GrayboxColliderKind.GroundSurface ||
                    kind == M2GrayboxColliderKind.UpperSurface ||
                    kind == M2GrayboxColliderKind.SupportLeg ||
                    kind == M2GrayboxColliderKind.Structure;
            }

            static bool IsNamedFacePatternCollider(string colliderName)
            {
                if (string.IsNullOrEmpty(colliderName)) return false;
                return colliderName.StartsWith("FaceA_Pattern_", StringComparison.OrdinalIgnoreCase) ||
                    colliderName.StartsWith("FaceB_Pattern_", StringComparison.OrdinalIgnoreCase) ||
                    colliderName.StartsWith("FaceC_Pattern_", StringComparison.OrdinalIgnoreCase);
            }

            static string FormatVector(Vector3 value)
            {
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                return value.x.ToString("R", culture) + "," + value.y.ToString("R", culture) + "," + value.z.ToString("R", culture);
            }

            static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

            static bool TryMeasurePositiveCapsuleGap(Collider attackCollider, Collider enemyCollider,
                out bool positive, out float signedGapMeters)
            {
                positive = false;
                signedGapMeters = float.NaN;
                CapsuleCollider targetCapsule = attackCollider as CapsuleCollider;
                CapsuleCollider movingCapsule = enemyCollider as CapsuleCollider;
                if (targetCapsule == null || movingCapsule == null) return false;

                if (!M2AttackCapsuleStandOffGuard.TryMeasureSurfaceGap(movingCapsule,
                        movingCapsule.transform.position, movingCapsule.transform.rotation,
                        targetCapsule, out signedGapMeters) || !IsFinite(signedGapMeters))
                    return false;
                positive = signedGapMeters > 0f;
                return true;
            }

            void ObserveFireCommandRaycast(int consumedTicks)
            {
                fireCommandTicks++;
                if (playerCamera == null)
                {
                    Add("postTickRaycastFirstHit", "CameraUnavailable", "QA_PlayerCamera", "", consumedTicks,
                        "No replay camera was found for the post-tick geometry query.", false);
                    return;
                }

                RaycastHit[] hits = Physics.RaycastAll(playerCamera.transform.position, playerCamera.transform.forward,
                    MaximumRaycastDistance);
                Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                if (hits.Length == 0)
                {
                    raycastNoHits++;
                    return;
                }

                raycastHits++;
                Collider hitCollider = hits[0].collider;
                M2GrayboxColliderTag tag = hitCollider.GetComponent<M2GrayboxColliderTag>();
                string kind = tag == null ? (hitCollider.GetComponent<ActiveEnemyActor>() == null ? "Untagged" : "ActiveEnemyActor") : tag.Kind.ToString();
                string face = tag != null && tag.Face.HasValue ? tag.Face.Value.ToString() : "";
                string example = "distance=" + hits[0].distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    ";pose=postCoreFixedTick;actualCoreShotResult=false";
                Add("postTickRaycastFirstHit", kind, hitCollider.name, face, consumedTicks, example, false);
            }

            void Add(string channel, string kind, string name, string face, int consumedTicks, string pair, bool isPair)
            {
                string key = channel + "\n" + kind + "\n" + name + "\n" + face;
                if (!buckets.TryGetValue(key, out ColliderDiagnosticBucket bucket))
                {
                    bucket = new ColliderDiagnosticBucket
                    {
                        channel = channel,
                        colliderKind = kind,
                        colliderName = name,
                        face = face
                    };
                    buckets.Add(key, bucket);
                }
                bucket.observations++;
                if (consumedTicks < bucket.firstConsumedTick) bucket.firstConsumedTick = consumedTicks;
                if (consumedTicks > bucket.lastConsumedTick) bucket.lastConsumedTick = consumedTicks;
                if (isPair) bucket.pairs.Add(pair);
                if (bucket.examples.Count < MaximumRepresentativePairs && !bucket.examples.Contains(pair))
                    bucket.examples.Add(pair);
            }

            AttackCapsuleStandOffReport BuildAttackCapsuleStandOffReport(int sampledTicks, int replayConsumedTicks,
                int totalInputTicks)
            {
                var expectedObserved = new List<string>();
                var missing = new List<string>();
                foreach (string expectedId in ExpectedAttackCapsuleIds)
                {
                    if (observedAttackCapsuleIds.Contains(expectedId)) expectedObserved.Add(expectedId);
                    else missing.Add(expectedId);
                }
                expectedObserved.Sort(StringComparer.Ordinal);
                missing.Sort(StringComparer.Ordinal);
                string[] allObservedIds = new string[observedAttackCapsuleIds.Count];
                observedAttackCapsuleIds.CopyTo(allObservedIds);
                Array.Sort(allObservedIds, StringComparer.Ordinal);

                bool replayConsumedAllInputTicks = totalInputTicks > 0 && replayConsumedTicks == totalInputTicks;
                bool windowComplete = replayConsumedAllInputTicks && sampledTicks + skippedTicks == replayConsumedTicks &&
                    skippedTicks == 0 && observationStartConsumedTick == 1 && observationEndConsumedTick == replayConsumedTicks;
                bool allCapsulesObserved = missing.Count == 0 && missingAttackCapsuleTickObservations == 0 &&
                    duplicateAttackCapsuleIds == 0;
                int allCandidatePairObservations = attackCapsulePairCandidateObservations;
                int assignedPairObservations = 0;
                int informationalPairObservations = 0;
                int allPenetrationEvaluated = 0;
                int allPenetrationOverlap = 0;
                int allPenetrationNoOverlap = 0;
                int allPenetrationUnavailable = 0;
                int assignedPenetrationEvaluated = 0;
                int assignedPenetrationOverlap = 0;
                int assignedPenetrationNoOverlap = 0;
                int assignedPenetrationUnavailable = 0;
                int informationalOverlap = 0;
                int assignedPositiveVolumeOverlaps = 0;
                int assignedOverlapDepthUnresolved = 0;
                int informationalPositiveVolumeOverlaps = 0;
                int allPositiveGaps = 0;
                int allNonPositive = 0;
                int allUnavailableGaps = 0;
                int terminalPairCount = 0;
                int terminalPairPositive = 0;
                int terminalPairNonPositive = 0;
                int terminalPairUnsupported = 0;
                int terminalTargetOverlaps = 0;
                int terminalTargetOverlapDepthUnresolved = 0;
                float minimumTerminalPositiveGap = float.PositiveInfinity;
                AttackCapsulePairAggregate minimumTerminalPositiveGapPair = null;
                var pairReports = new List<AttackCapsulePairAggregate>();
                foreach (AttackCapsulePairBucket bucket in attackCapsulePairBuckets.Values)
                {
                    AttackCapsulePairAggregate pair = bucket.ToReport();
                    pairReports.Add(pair);
                    allPenetrationEvaluated += pair.computePenetrationEvaluatedObservations;
                    allPenetrationOverlap += pair.computePenetrationOverlapObservations;
                    allPenetrationNoOverlap += pair.computePenetrationNoOverlapObservations;
                    allPenetrationUnavailable += pair.computePenetrationUnavailableObservations;
                    allPositiveGaps += pair.positiveGapMeasurementObservations;
                    allNonPositive += pair.nonPositiveSurfaceClearanceObservations;
                    allUnavailableGaps += pair.unavailableSurfaceGapObservations;
                    if (!pair.isAssignedTargetAttackPoint)
                    {
                        informationalPairObservations += pair.observations;
                        informationalOverlap += pair.computePenetrationOverlapObservations;
                        informationalPositiveVolumeOverlaps += pair.positiveVolumeOverlapObservations;
                        continue;
                    }
                    assignedPairObservations += pair.observations;
                    assignedPenetrationEvaluated += pair.computePenetrationEvaluatedObservations;
                    assignedPenetrationOverlap += pair.computePenetrationOverlapObservations;
                    assignedPenetrationNoOverlap += pair.computePenetrationNoOverlapObservations;
                    assignedPenetrationUnavailable += pair.computePenetrationUnavailableObservations;
                    assignedPositiveVolumeOverlaps += pair.positiveVolumeOverlapObservations;
                    assignedOverlapDepthUnresolved += pair.overlapDepthUnresolvedObservations;
                    terminalPairCount += pair.terminalTargetPairObservations;
                    terminalPairPositive += pair.terminalTargetPairPositiveGapObservations;
                    terminalPairNonPositive += pair.terminalTargetPairNonPositiveClearanceObservations;
                    terminalPairUnsupported += pair.terminalTargetPairUnsupportedGapObservations;
                    terminalTargetOverlaps += pair.terminalTargetPositiveVolumeOverlapObservations;
                    terminalTargetOverlapDepthUnresolved += pair.terminalTargetOverlapDepthUnresolvedObservations;
                    float pairTerminalGap;
                    if (pair.isAssignedTargetAttackPoint &&
                        float.TryParse(pair.minimumTerminalPositiveGapMeters,
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                            out pairTerminalGap) && IsFinite(pairTerminalGap) &&
                        (pairTerminalGap < minimumTerminalPositiveGap ||
                         (pairTerminalGap == minimumTerminalPositiveGap && minimumTerminalPositiveGapPair != null &&
                          string.CompareOrdinal(pair.pairKey, minimumTerminalPositiveGapPair.pairKey) < 0)))
                    {
                        minimumTerminalPositiveGap = pairTerminalGap;
                        minimumTerminalPositiveGapPair = pair;
                    }
                }
                pairReports.Sort((left, right) => string.CompareOrdinal(left.pairKey, right.pairKey));

                bool pairCapComplete = !attackCapsulePairBucketCapReached && omittedAttackCapsulePairObservations == 0;
                bool targetPairsComplete = windowComplete && allCapsulesObserved && pairCapComplete &&
                    assignedPairObservations > 0 && assignedPenetrationUnavailable == 0 &&
                    attackCapsuleUnclassifiedAssignedTargetTriggerPairObservations == 0 &&
                    inactiveAssignedTargetCapsulePairObservations == 0 && enemyActorsWithoutColliderObservations == 0;
                string pathAssessment = assignedPositiveVolumeOverlaps > 0
                    ? "ASSIGNED_TARGET_COMPUTEPENETRATION_OVERLAP_REPORTED_ON_OBSERVED_TICKS_REVIEW_REQUIRED_EVEN_IF_WINDOW_INCOMPLETE"
                    : assignedOverlapDepthUnresolved > 0
                        ? "INCONCLUSIVE_COMPUTEPENETRATION_REPORTED_OVERLAP_WITHOUT_POSITIVE_DEPTH_ON_OBSERVED_SAMPLES"
                    : !targetPairsComplete
                        ? "INCONCLUSIVE_ASSIGNED_TARGET_PAIR_OBSERVATION_INCOMPLETE"
                        : "NO_ASSIGNED_TARGET_COMPUTEPENETRATION_OVERLAP_REPORTED_ON_COMPLETE_OBSERVED_TICKS_NOT_ALL_PATHS_OR_CONTINUOUS_GATE_PASS";

                int terminalCoreUnavailable = 0;
                int terminalGapMismatches = 0;
                int terminalMappingMismatches = 0;
                foreach (AttackCapsuleTerminalPoseDiagnostic terminal in terminalAttackPoses)
                {
                    if (!terminal.hasMeasuredCoreSurfaceGap || !terminal.rootColliderGapMeasurementAvailable)
                        terminalCoreUnavailable++;
                    if (terminal.hasMeasuredCoreSurfaceGap && terminal.rootColliderGapMeasurementAvailable &&
                        !terminal.coreAndToolsRootGapExactlyEqual) terminalGapMismatches++;
                    if (terminal.targetCapsuleMappingStatus != "ASSIGNED_AP_FOUND") terminalMappingMismatches++;
                }
                string terminalAssessment = terminalAttackPoses.Count == 0
                    ? "INCONCLUSIVE_NO_CORE_TERMINAL_ATTACK_POSE_OBSERVED"
                    : !pairCapComplete || attackCapsuleUnclassifiedAssignedTargetTriggerPairObservations > 0 ||
                        terminalMappingMismatches > 0 || terminalCoreUnavailable > 0 || terminalGapMismatches > 0
                        ? "INCONCLUSIVE_TERMINAL_TARGET_MAPPING_OR_CORE_GAP_RECONCILIATION"
                    : terminalPairCount == 0 || terminalPairUnsupported > 0
                        ? "INCONCLUSIVE_ACTIVE_TERMINAL_TARGET_PAIR_GAP_UNSUPPORTED_OR_MISSING"
                    : terminalTargetOverlaps > 0 || terminalPairNonPositive > 0
                        ? "NONPOSITIVE_TERMINAL_ASSIGNED_TARGET_CLEARANCE_REPORTED_REVIEW_REQUIRED"
                    : terminalTargetOverlapDepthUnresolved > 0
                        ? "INCONCLUSIVE_TERMINAL_COMPUTEPENETRATION_DEPTH_NOT_POSITIVE_OR_AVAILABLE"
                    : terminalPairPositive != terminalPairCount
                        ? "INCONCLUSIVE_NOT_EVERY_ACTIVE_TERMINAL_TARGET_PAIR_HAS_A_POSITIVE_GAP_MEASUREMENT"
                        : "POSITIVE_GAP_RECONCILED_FOR_OBSERVED_ASSIGNED_TERMINAL_PAIRS_ONLY_NOT_ALL_VALID_TERMINAL_POSES";

                return new AttackCapsuleStandOffReport
                {
                    observedPathVolumeAssessment = pathAssessment,
                    terminalAttackPoseStandOffAssessment = terminalAssessment,
                    observationWindowComplete = windowComplete,
                    replayConsumedAllInputTicks = replayConsumedAllInputTicks,
                    allExpectedAttackCapsulesObserved = allCapsulesObserved,
                    allEnemyPairMeasurementsEvaluated = targetPairsComplete,
                    distinctAttackCapsuleIdsObserved = expectedObserved.Count,
                    missingAttackCapsuleIds = missing.Count,
                    missingAttackCapsuleIdentityIds = missing.ToArray(),
                    missingAttackCapsuleTickObservations = missingAttackCapsuleTickObservations,
                    duplicateAttackCapsuleIds = duplicateAttackCapsuleIds,
                    observedAttackCapsuleIds = allObservedIds,
                    inactiveOrDisabledPairObservations = inactiveOrDisabledAttackCapsulePairObservations,
                    observedFixedTickSamples = sampledTicks,
                    skippedFixedTickSamples = skippedTicks,
                    activeEnemyActorObservations = activeEnemyActorObservations,
                    enemyActorsWithoutColliderObservations = enemyActorsWithoutColliderObservations,
                    candidatePairObservations = allCandidatePairObservations,
                    assignedTargetPairObservations = assignedPairObservations,
                    informationalNonTargetPairObservations = informationalPairObservations,
                    uniqueAttackCapsuleEnemyColliderPairs = attackCapsulePairBuckets.Count,
                    pairBucketCount = attackCapsulePairBuckets.Count,
                    omittedPairObservations = omittedAttackCapsulePairObservations,
                    pairBucketCapReached = attackCapsulePairBucketCapReached,
                    computePenetrationEvaluatedObservations = allPenetrationEvaluated,
                    computePenetrationOverlapObservations = allPenetrationOverlap,
                    computePenetrationNoOverlapObservations = allPenetrationNoOverlap,
                    computePenetrationUnavailableObservations = allPenetrationUnavailable,
                    assignedTargetComputePenetrationEvaluatedObservations = assignedPenetrationEvaluated,
                    assignedTargetComputePenetrationOverlapObservations = assignedPenetrationOverlap,
                    assignedTargetComputePenetrationNoOverlapObservations = assignedPenetrationNoOverlap,
                    assignedTargetComputePenetrationUnavailableObservations = assignedPenetrationUnavailable,
                    informationalNonTargetOverlapObservations = informationalOverlap,
                    assignedTargetPositiveVolumeOverlapObservations = assignedPositiveVolumeOverlaps,
                    assignedTargetOverlapDepthUnresolvedObservations = assignedOverlapDepthUnresolved,
                    informationalNonTargetPositiveVolumeOverlapObservations = informationalPositiveVolumeOverlaps,
                    positiveGapMeasurementObservations = allPositiveGaps,
                    nonPositiveSurfaceClearanceObservations = allNonPositive,
                    unavailableSurfaceGapObservations = allUnavailableGaps,
                    unsupportedColliderPairObservations = attackCapsuleUnsupportedPairObservations,
                    unclassifiedEnemyTriggerPairObservations = attackCapsuleUnclassifiedEnemyTriggerPairObservations,
                    assignedTargetUnclassifiedEnemyTriggerPairObservations = attackCapsuleUnclassifiedAssignedTargetTriggerPairObservations,
                    candidateMinimumGapPairObservationCount = CountPositiveGapPairs(attackCapsulePairBuckets),
                    terminalAttackPoseObservations = terminalAttackPoses.Count,
                    terminalTargetPairObservations = terminalPairCount,
                    terminalTargetPairPositiveGapObservations = terminalPairPositive,
                    terminalTargetPairNonPositiveClearanceObservations = terminalPairNonPositive,
                    terminalTargetPairUnsupportedGapObservations = terminalPairUnsupported,
                    terminalTargetOverlapObservations = terminalTargetOverlaps,
                    terminalTargetPositiveVolumeOverlapObservations = terminalTargetOverlaps,
                    terminalTargetOverlapDepthUnresolvedObservations = terminalTargetOverlapDepthUnresolved,
                    terminalPoseTargetAssignmentMismatches = terminalMappingMismatches,
                    terminalCoreGapUnavailableObservations = terminalCoreUnavailable,
                    terminalCoreGapReconciliationMismatches = terminalGapMismatches,
                    minimumObservedPositiveGapCandidateMeters = IsFinite(minimumObservedPositiveAttackCapsuleGap)
                        ? minimumObservedPositiveAttackCapsuleGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                    minimumObservedPositiveGapAttackCapsuleId = string.IsNullOrEmpty(minimumObservedPositiveAttackCapsuleId) ? "unavailable" : minimumObservedPositiveAttackCapsuleId,
                    minimumObservedPositiveGapEnemyEntityId = string.IsNullOrEmpty(minimumObservedPositiveAttackCapsuleEnemyId) ? "unavailable" : minimumObservedPositiveAttackCapsuleEnemyId,
                    minimumObservedPositiveGapEnemyColliderPath = string.IsNullOrEmpty(minimumObservedPositiveAttackCapsuleEnemyPath) ? "unavailable" : minimumObservedPositiveAttackCapsuleEnemyPath,
                    minimumObservedPositiveGapConsumedTick = minimumObservedPositiveAttackCapsuleTick,
                    minimumObservedPositiveGapSampleContext = IsFinite(minimumObservedPositiveAttackCapsuleGap)
                        ? "ASSIGNED_TARGET_OBSERVED_REPLAY_TICK_NOT_TERMINAL_BY_DEFAULT" : "NO_ASSIGNED_TARGET_POSITIVE_GAP_MEASUREMENT_RECORDED",
                    minimumTerminalPositiveGapCandidateMeters = IsFinite(minimumTerminalPositiveGap)
                        ? minimumTerminalPositiveGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                    minimumTerminalPositiveGapAttackPointId = minimumTerminalPositiveGapPair == null
                        ? "unavailable" : minimumTerminalPositiveGapPair.attackPointId,
                    minimumTerminalPositiveGapEnemyEntityId = minimumTerminalPositiveGapPair == null
                        ? "unavailable" : minimumTerminalPositiveGapPair.enemyEntityId,
                    minimumTerminalPositiveGapEnemyColliderPath = minimumTerminalPositiveGapPair == null
                        ? "unavailable" : minimumTerminalPositiveGapPair.enemyCollider.transformPath,
                    minimumTerminalPositiveGapActorPosition = minimumTerminalPositiveGapPair == null
                        ? "unavailable" : minimumTerminalPositiveGapPair.minimumTerminalPositiveGapActorPosition,
                    minimumTerminalPositiveGapConsumedTick = minimumTerminalPositiveGapPair == null
                        ? 0 : minimumTerminalPositiveGapPair.minimumTerminalPositiveGapConsumedTick,
                    terminalAttackPoses = terminalAttackPoses.ToArray(),
                    pairs = pairReports.ToArray()
                };
            }

            static int CountPositiveGapPairs(Dictionary<string, AttackCapsulePairBucket> source)
            {
                int count = 0;
                foreach (AttackCapsulePairBucket pair in source.Values)
                    if (pair.isAssignedTargetAttackPoint && !float.IsPositiveInfinity(pair.minimumPositiveGap)) count++;
                return count;
            }

            public ColliderDiagnosticReport Complete(M4NightOneGrayboxController controller, M4ReplayTape tape, int sampledTicks)
            {
                var reportBuckets = new List<ColliderDiagnosticAggregate>();
                foreach (ColliderDiagnosticBucket bucket in buckets.Values)
                    reportBuckets.Add(bucket.ToReport());
                reportBuckets.Sort((left, right) =>
                {
                    int channelOrder = string.CompareOrdinal(left.channel, right.channel);
                    if (channelOrder != 0) return channelOrder;
                    int kindOrder = string.CompareOrdinal(left.colliderKind, right.colliderKind);
                    if (kindOrder != 0) return kindOrder;
                    return string.CompareOrdinal(left.colliderName, right.colliderName);
                });

                var pairReports = new List<ColliderPairAggregate>();
                foreach (ColliderPairBucket bucket in pairBuckets.Values)
                    pairReports.Add(bucket.ToReport());
                pairReports.Sort((left, right) => string.CompareOrdinal(left.pairKey, right.pairKey));
                var roleReports = new List<ColliderRoleAggregate>();
                foreach (ColliderRoleBucket bucket in roleBuckets.Values)
                    roleReports.Add(bucket.ToReport());
                roleReports.Sort((left, right) =>
                {
                    int roleOrder = string.CompareOrdinal(left.pairRole, right.pairRole);
                    if (roleOrder != 0) return roleOrder;
                    int kindOrder = string.CompareOrdinal(left.colliderKind, right.colliderKind);
                    if (kindOrder != 0) return kindOrder;
                    return string.CompareOrdinal(left.colliderName, right.colliderName);
                });
                ActualShotDiagnosticReport actualShots = BuildActualShotReport(controller, tape);
                int replayConsumedTicks = controller.ReplayConsumedTicks;
                AttackCapsuleStandOffReport attackCapsuleStandOff =
                    BuildAttackCapsuleStandOffReport(sampledTicks, replayConsumedTicks,
                        tape == null || tape.ticks == null ? 0 : tape.ticks.Length);
                bool observationWindowMatchesReplay = sampledTicks + skippedTicks == replayConsumedTicks &&
                    (replayConsumedTicks == 0 || (observationStartConsumedTick == 1 && observationEndConsumedTick == replayConsumedTicks));
                bool geometryObservationComplete = replayConsumedTicks > 0 && observationWindowMatchesReplay &&
                    skippedTicks == 0 && overlapBroadphaseOverflowSamples == 0 &&
                    !pairBucketCapReached && !colliderRoleBucketCapReached && !pairObservationSampleCapReached &&
                    omittedPairObservations == 0 && omittedPairObservationSamples == 0;
                bool semanticRoleClassificationComplete = unclassifiedTriggerRolePairObservations == 0;
                bool upperLaneWindowComplete = replayConsumedTicks > 0 && observationWindowMatchesReplay && skippedTicks == 0;
                int upperLaneUnwitnessedCount = upperLaneActorTicks - upperLaneWitnessedActorTicks - upperLaneUnavailableActorTicks;
                string upperLaneAssessment = !upperLaneWindowComplete || upperLaneGraybox == null ||
                    upperLaneGraybox.Geometry == null || upperLaneActorTicks == 0 || upperLaneUnavailableActorTicks > 0
                    ? "INCONCLUSIVE_INCOMPLETE_UPPER_LANE_OBSERVATION"
                    : upperLaneUnwitnessedCount > 0
                        ? "SAMPLED_ACTOR_TICKS_WITHOUT_CANONICAL_UPPER_FIRST_HIT_WITNESS_REVIEW_REQUIRED"
                        : "CANONICAL_UPPER_FIRST_HIT_WITNESSED_ON_ALL_SAMPLED_ACTOR_TICKS_NOT_CONTINUOUS_OR_ALL_ROUTE_PASS";
                string solidGateAssessment = !geometryObservationComplete
                    ? "INDETERMINATE_INCOMPLETE_GEOMETRY_OBSERVATION"
                    : !semanticRoleClassificationComplete
                        ? "INDETERMINATE_UNCLASSIFIED_TRIGGER_ROLE"
                    : solidStructureGatePenetrationReportedObservations > 0
                        ? "SOLID_STRUCTURE_COMPUTEPENETRATION_OVERLAP_REPORTED_REVIEW_REQUIRED"
                        : solidStructureGateUnavailableObservations > 0
                            ? "INDETERMINATE_SOLID_STRUCTURE_CANDIDATES_NOT_ALL_EVALUATED"
                            : "NO_SOLID_STRUCTURE_COMPUTEPENETRATION_OVERLAP_REPORTED_NOT_A_PASS";
                return new ColliderDiagnosticReport
                {
                    actualShotResultClassification = actualShots.reconciliation,
                    actualCoreShotResults = actualShots,
                    upperLaneGeometryAssessment = upperLaneAssessment,
                    upperLaneCoreObservation = upperLaneCoreObservations.Complete(replayConsumedTicks, skippedTicks),
                    upperLaneGeometry = new UpperLaneGeometryReport
                    {
                        assessment = upperLaneAssessment,
                        observationWindowComplete = upperLaneWindowComplete,
                        coreGeometryAvailable = upperLaneGraybox != null && upperLaneGraybox.Geometry != null,
                        actorTickObservations = upperLaneActorTicks,
                        witnessedActorTickObservations = upperLaneWitnessedActorTicks,
                        unwitnessedActorTickObservations = upperLaneUnwitnessedCount,
                        unavailableActorTickObservations = upperLaneUnavailableActorTicks,
                        stationRayQueries = upperLaneStationRays,
                        firstUnwitnessedContext = upperLaneUnwitnessed.Count == 0 ? "unavailable" : upperLaneUnwitnessed[0],
                        representativeUnwitnessedContexts = upperLaneUnwitnessed.ToArray(),
                        representativeUnwitnessedContextsTruncated = upperLaneExamplesTruncated
                    },
                    physicalOverlapAssessment = !geometryObservationComplete
                        ? "INDETERMINATE_INCOMPLETE_GEOMETRY_OBSERVATION"
                        : overlapPairObservations == 0 ? "NO_COMPUTEPENETRATION_OVERLAP_REPORTED_MEASUREMENT_ONLY" : "COMPUTEPENETRATION_OVERLAP_REPORTED_MEASUREMENT_ONLY",
                    solidStructureGateAssessment = solidGateAssessment,
                    geometryObservationComplete = geometryObservationComplete,
                    semanticRoleClassificationComplete = semanticRoleClassificationComplete,
                    pairBucketCapReached = pairBucketCapReached,
                    colliderRoleBucketCapReached = colliderRoleBucketCapReached,
                    omittedPairObservations = omittedPairObservations,
                    omittedPairObservationSamples = omittedPairObservationSamples,
                    colliderPairBucketCount = pairBuckets.Count,
                    colliderRoleBucketCount = roleBuckets.Count,
                    physicalColliderPairCandidateObservations = physicalColliderPairCandidateObservations,
                    computePenetrationEvaluatedObservations = computePenetrationEvaluatedObservations,
                    computePenetrationReportedOverlapObservations = computePenetrationReportedOverlapObservations,
                    computePenetrationNoOverlapObservations = computePenetrationNoOverlapObservations,
                    computePenetrationUnavailableObservations = computePenetrationUnavailableObservations,
                    separationDistanceAvailableObservations = separationDistanceAvailableObservations,
                    solidStructureGateCandidateObservations = solidStructureGateCandidateObservations,
                    solidStructureGatePenetrationReportedObservations = solidStructureGatePenetrationReportedObservations,
                    solidStructureGateUnavailableObservations = solidStructureGateUnavailableObservations,
                    permittedAttackCapsuleCandidateObservations = permittedAttackCapsuleCandidateObservations,
                    triggerCandidateObservations = triggerCandidateObservations,
                    structuralTriggerPairObservations = structuralTriggerPairObservations,
                    coreClassifiedEnemyHitTriggerPairObservations = coreClassifiedEnemyHitTriggerPairObservations,
                    unclassifiedTriggerRolePairObservations = unclassifiedTriggerRolePairObservations,
                    inactiveEnemyColliderObservationsSkipped = inactiveEnemyColliderObservationsSkipped,
                    inactiveWorldColliderCandidateObservations = inactiveWorldColliderCandidateObservations,
                    excludedEnemyActorCandidateObservations = excludedEnemyActorCandidateObservations,
                    pairObservationSamplesTruncated = pairObservationSampleCapReached,
                    raycastAssessment = raycastHits == 0 ? "NO_POST_TICK_FIRST_HITS_OBSERVED" : "POST_TICK_FIRST_HITS_OBSERVED",
                    replayConsumedTicks = replayConsumedTicks,
                    observationStartConsumedTick = observationStartConsumedTick == int.MaxValue ? 0 : observationStartConsumedTick,
                    observationEndConsumedTick = observationEndConsumedTick,
                    observedConsumedTickSamples = sampledTicks,
                    skippedConsumedTickSamples = skippedTicks,
                    fireCommandTicksObserved = fireCommandTicks,
                    fireCommandRaycastHits = raycastHits,
                    fireCommandRaycastNoHits = raycastNoHits,
                    physicalOverlapDetectedTicks = overlapTicks.Count,
                    physicalOverlapPairObservations = overlapPairObservations,
                    overlapBroadphaseOverflowSamples = overlapBroadphaseOverflowSamples,
                    coreReplayBlockedWorldShots = controller.ReplayBlockedWorldShots,
                    coreReplayBlockedResolvedEnemyShots = controller.ReplayBlockedResolvedEnemyShots,
                    coreReplayPriorStatus = controller.ReplayPriorStatus,
                    colliderPairs = pairReports.ToArray(),
                    colliderRoleAggregates = roleReports.ToArray(),
                    byChannelAndCollider = reportBuckets.ToArray(),
                    attackCapsuleStandOff = attackCapsuleStandOff
                };
            }

            static ActualShotDiagnosticReport BuildActualShotReport(M4NightOneGrayboxController controller, M4ReplayTape tape)
            {
                M4ReplayShotDiagnosticSnapshot snapshot = controller.ReplayShotDiagnostics;
                var report = new ActualShotDiagnosticReport
                {
                    replayFireCommandCount = snapshot.ReplayFireCommandCount,
                    raycastAttemptCount = snapshot.RaycastAttemptCount,
                    colliderNameBucketsTruncated = snapshot.ColliderNameBucketsTruncated,
                    examplesTruncated = snapshot.ExamplesTruncated,
                    recorderFailure = controller.ReplayShotDiagnosticsFailure ?? string.Empty,
                    outcomeCounts = ConvertOutcomeCounts(snapshot.OutcomeCounts),
                    colliderKindCounts = ConvertColliderKindCounts(snapshot.ColliderKindCounts),
                    colliderNameCounts = ConvertColliderNameCounts(snapshot.ColliderNameCounts),
                    faceCounts = ConvertFaceCounts(snapshot.FaceCounts)
                };

                report.expectedFireCommandsInConsumedTapePrefix = CountConsumedFireCommands(tape, controller.ReplayConsumedTicks);
                report.outcomeCountTotal = SumCounts(report.outcomeCounts);
                report.colliderKindCountTotal = SumCounts(report.colliderKindCounts);
                report.colliderNameCountTotal = SumCounts(report.colliderNameCounts);
                report.faceCountTotal = SumCounts(report.faceCounts);
                report.worldBlockOutcomeCount = FindOutcomeCount(snapshot.OutcomeCounts, M4ReplayShotDiagnosticOutcome.WorldBlock);
                report.resolvedEnemyBlockOutcomeCount = FindOutcomeCount(snapshot.OutcomeCounts, M4ReplayShotDiagnosticOutcome.ResolvedEnemyBlock);
                report.fireCommandCountMatchesConsumedTape = report.replayFireCommandCount == report.expectedFireCommandsInConsumedTapePrefix;
                report.outcomeCountsMatchFireCommands = report.outcomeCountTotal == report.replayFireCommandCount;
                report.colliderMarginalCountsMatch = report.colliderKindCountTotal == report.colliderNameCountTotal &&
                    report.colliderNameCountTotal == report.faceCountTotal;
                report.worldBlockCountMatchesCoreCounter = report.worldBlockOutcomeCount == controller.ReplayBlockedWorldShots;
                report.resolvedEnemyBlockCountMatchesCoreCounter = report.resolvedEnemyBlockOutcomeCount == controller.ReplayBlockedResolvedEnemyShots;

                if (!string.IsNullOrEmpty(report.recorderFailure))
                    report.reconciliation = "INDETERMINATE_CORE_DIAGNOSTIC_RECORDER_FAILURE";
                else if (report.fireCommandCountMatchesConsumedTape && report.outcomeCountsMatchFireCommands &&
                    report.colliderMarginalCountsMatch && report.worldBlockCountMatchesCoreCounter &&
                    report.resolvedEnemyBlockCountMatchesCoreCounter)
                    report.reconciliation = "PASS_CORE_SNAPSHOT_RECONCILED";
                else
                    report.reconciliation = "FAIL_CORE_SNAPSHOT_RECONCILIATION";
                return report;
            }

            static int CountConsumedFireCommands(M4ReplayTape tape, int consumedTicks)
            {
                int count = 0;
                int limit = Math.Min(Math.Max(consumedTicks, 0), tape == null || tape.ticks == null ? 0 : tape.ticks.Length);
                for (int i = 0; i < limit; i++)
                    if (tape.ticks[i].firePressed) count++;
                return count;
            }

            static int FindOutcomeCount(IReadOnlyList<M4ReplayShotOutcomeCount> values, M4ReplayShotDiagnosticOutcome outcome)
            {
                for (int i = 0; i < values.Count; i++)
                    if (values[i].Outcome == outcome) return values[i].Count;
                return 0;
            }

            static int SumCounts(ActualShotDiagnosticAggregate[] values)
            {
                int total = 0;
                foreach (ActualShotDiagnosticAggregate value in values) total += value.count;
                return total;
            }

            static ActualShotDiagnosticAggregate[] ConvertOutcomeCounts(IReadOnlyList<M4ReplayShotOutcomeCount> values)
            {
                var rows = new ActualShotDiagnosticAggregate[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    M4ReplayShotOutcomeCount value = values[i];
                    rows[i] = new ActualShotDiagnosticAggregate
                    {
                        dimension = "outcome", key = value.Outcome.ToString(), count = value.Count,
                        examplesTruncated = value.ExamplesTruncated, examples = ConvertExamples(value.Examples)
                    };
                }
                return rows;
            }

            static ActualShotDiagnosticAggregate[] ConvertColliderKindCounts(IReadOnlyList<M4ReplayShotColliderKindCount> values)
            {
                var rows = new ActualShotDiagnosticAggregate[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    M4ReplayShotColliderKindCount value = values[i];
                    rows[i] = new ActualShotDiagnosticAggregate
                    {
                        dimension = "colliderKind", key = value.HasGrayboxTag ? value.ColliderKind.ToString() : "Untagged",
                        hasGrayboxTag = value.HasGrayboxTag, count = value.Count,
                        examplesTruncated = value.ExamplesTruncated, examples = ConvertExamples(value.Examples)
                    };
                }
                return rows;
            }

            static ActualShotDiagnosticAggregate[] ConvertColliderNameCounts(IReadOnlyList<M4ReplayShotColliderNameCount> values)
            {
                var rows = new ActualShotDiagnosticAggregate[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    M4ReplayShotColliderNameCount value = values[i];
                    rows[i] = new ActualShotDiagnosticAggregate
                    {
                        dimension = "colliderName", key = value.ColliderName, overflowBucket = value.IsOverflowBucket,
                        count = value.Count, examplesTruncated = value.ExamplesTruncated,
                        examples = ConvertExamples(value.Examples)
                    };
                }
                return rows;
            }

            static ActualShotDiagnosticAggregate[] ConvertFaceCounts(IReadOnlyList<M4ReplayShotFaceCount> values)
            {
                var rows = new ActualShotDiagnosticAggregate[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    M4ReplayShotFaceCount value = values[i];
                    rows[i] = new ActualShotDiagnosticAggregate
                    {
                        dimension = "face", key = value.HasFace ? value.Face.ToString() : "NoFace",
                        count = value.Count, examplesTruncated = value.ExamplesTruncated,
                        examples = ConvertExamples(value.Examples)
                    };
                }
                return rows;
            }

            static ActualShotDiagnosticExample[] ConvertExamples(IReadOnlyList<M4ReplayShotDiagnostic> values)
            {
                var examples = new ActualShotDiagnosticExample[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    M4ReplayShotDiagnostic value = values[i];
                    examples[i] = new ActualShotDiagnosticExample
                    {
                        replayConsumedTick = value.ReplayConsumedTick,
                        runFixedTick = value.RunFixedTick,
                        weaponId = value.WeaponId,
                        raycastAttempted = value.RaycastAttempted,
                        outcome = value.Outcome.ToString(),
                        noRaycastReason = value.NoRaycastReason.ToString(),
                        hasColliderHit = value.HasColliderHit,
                        colliderName = value.ColliderName,
                        hitDistance = value.HitDistance,
                        hasGrayboxTag = value.HasGrayboxTag,
                        colliderKind = value.HasGrayboxTag ? value.ColliderKind.ToString() : "Untagged",
                        hasFace = value.HasFace,
                        face = value.HasFace ? value.Face.ToString() : "",
                        hasActiveEnemyActor = value.HasActiveEnemyActor,
                        actorRegistryKeyPresent = value.ActorRegistryKeyPresent,
                        actorInstanceMatchesRegistry = value.ActorInstanceMatchesRegistry,
                        damageStatus = value.DamageStatus.HasValue ? value.DamageStatus.Value.ToString() : "",
                        damageBecameResolved = value.DamageBecameResolved
                    };
                }
                return examples;
            }

            static string GetTransformPath(Transform value, Transform root)
            {
                if (value == root) return value.name;
                string path = value.name;
                Transform current = value.parent;
                while (current != null && current != root)
                {
                    path = current.name + "/" + path;
                    current = current.parent;
                }
                return root.name + "/" + path;
            }

            static string GetFullTransformPath(Transform value)
            {
                if (value == null) return string.Empty;
                string path = value.name;
                Transform current = value.parent;
                while (current != null)
                {
                    path = current.name + "/" + path;
                    current = current.parent;
                }
                return path;
            }
        }

        [Serializable]
        sealed class ReplayReceipt
        {
            public string difficulty;
            public int repetition;
            public string status;
            public string failure;
            public int consumedTicks;
            public int totalTicks;
            public string evidencePath;
            public double elapsedWallSeconds;
            public string replayPriorStatus;
            public int replayBlockedWorldShots;
            public int replayBlockedResolvedEnemyShots;
            public string colliderDiagnosticsPath;
            public string colliderDiagnosticsSha256;
            public ReplayBoundaryState[] boundaryStates;
            public ReplayBoundaryState terminalState;
        }

        [UnityTest] public IEnumerator NormalReplayFirst() { yield return RunReplay("normal", 1); }
        [UnityTest] public IEnumerator NormalReplaySecond() { yield return RunReplay("normal", 2); }
        [UnityTest] public IEnumerator HardReplayFirst() { yield return RunReplay("hard", 1); }
        [UnityTest] public IEnumerator HardReplaySecond() { yield return RunReplay("hard", 2); }

        static IEnumerator RunReplay(string difficulty, int repetition)
        {
            string inputDirectory = Environment.GetEnvironmentVariable("LASTWATCH_M4_REPLAY_INPUT_DIR");
            string outputDirectory = Environment.GetEnvironmentVariable("LASTWATCH_M4_REPLAY_OUTPUT_DIR");
            Assert.That(inputDirectory, Is.Not.Null.And.Not.Empty, "Replay input directory was not supplied.");
            Assert.That(outputDirectory, Is.Not.Null.And.Not.Empty, "Replay output directory was not supplied.");
            Directory.CreateDirectory(outputDirectory);

            string inputPath = Path.Combine(inputDirectory, difficulty + "-replay-input.json");
            string rawInput = File.ReadAllText(inputPath, Utf8NoBom);
            Assert.That(M4ReplayTape.TryParse(rawInput, out M4ReplayTape tape, out string parseReason), Is.True, parseReason);
            Assert.That(tape.frozen, Is.True);
            Assert.That(tape.difficulty, Is.EqualTo(difficulty == "normal" ? 0 : 1));
            var nightBoundaries = new List<int>();
            for (int i = 1; i < tape.ticks.Length; i++)
                if (tape.ticks[i].nightOrdinal != tape.ticks[i - 1].nightOrdinal) nightBoundaries.Add(i);

#if UNITY_EDITOR
            Scene scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True, "QA scene did not load.");
#else
            Assert.Fail("This replay QA test requires the Unity Editor.");
            yield break;
#endif
            yield return null;

            M4NightOneGrayboxController controller = UnityEngine.Object.FindFirstObjectByType<M4NightOneGrayboxController>();
            Assert.That(controller, Is.Not.Null, "QA controller was not found in the loaded scene.");
            controller.BeginReplay(tape);

            var timer = Stopwatch.StartNew();
            var boundaryStates = new List<ReplayBoundaryState>();
            var colliderDiagnostics = new ColliderDiagnosticAccumulator();
            int lastObservedTick = 0;
            int lastBoundaryTick = -1;
            int observedConsumedTickSamples = 0;
            int skippedConsumedTickSamples = 0;
            while (controller.ReplayStatus == M4QaReplayStatus.Running &&
                   timer.Elapsed.TotalSeconds < PerReplayWallTimeoutSeconds)
            {
                yield return new WaitForFixedUpdate();
                int consumed = controller.ReplayConsumedTicks;
                if (consumed > 0 && consumed > lastObservedTick)
                {
                    int newlyConsumed = consumed - lastObservedTick;
                    if (newlyConsumed > 1)
                    {
                        int missed = newlyConsumed - 1;
                        skippedConsumedTickSamples += missed;
                        colliderDiagnostics.RecordSkippedTicks(missed);
                    }
                    M4ReplayTick command = consumed > 0 && consumed <= tape.ticks.Length ? tape.ticks[consumed - 1] : null;
                    colliderDiagnostics.ObserveTick(controller.GetComponent<M4ThreeNightQaSceneEntry>(), controller, consumed, command);
                    observedConsumedTickSamples++;
                    lastObservedTick = consumed;
                }
                if (consumed != lastBoundaryTick && IsNearNightBoundary(nightBoundaries, consumed))
                {
                    boundaryStates.Add(CaptureState(controller, tape));
                    lastBoundaryTick = consumed;
                }
            }
            timer.Stop();

            string stem = difficulty + "-replay-" + repetition;
            string evidencePath = Path.Combine(outputDirectory, stem + ".evidence.json");
            string colliderDiagnosticsPath = Path.Combine(outputDirectory, stem + ".collider-diagnostics.json");
            ColliderDiagnosticReport diagnosticReport = colliderDiagnostics.Complete(controller, tape, observedConsumedTickSamples);
            File.WriteAllText(colliderDiagnosticsPath, JsonUtility.ToJson(diagnosticReport, true), Utf8NoBom);
            var receipt = new ReplayReceipt
            {
                difficulty = difficulty,
                repetition = repetition,
                status = controller.ReplayStatus.ToString(),
                failure = controller.ReplayFailure,
                consumedTicks = controller.ReplayConsumedTicks,
                totalTicks = controller.ReplayTotalTicks,
                evidencePath = null,
                elapsedWallSeconds = timer.Elapsed.TotalSeconds,
                replayPriorStatus = controller.ReplayPriorStatus,
                replayBlockedWorldShots = controller.ReplayBlockedWorldShots,
                replayBlockedResolvedEnemyShots = controller.ReplayBlockedResolvedEnemyShots,
                colliderDiagnosticsPath = colliderDiagnosticsPath,
                colliderDiagnosticsSha256 = GetSha256(colliderDiagnosticsPath),
                boundaryStates = boundaryStates.ToArray(),
                terminalState = CaptureState(controller, tape)
            };
            if (controller.ReplayStatus == M4QaReplayStatus.Completed)
            {
                File.WriteAllText(evidencePath, controller.ExportFrozenReplayEvidenceJson(), Utf8NoBom);
                receipt.evidencePath = evidencePath;
            }
            File.WriteAllText(Path.Combine(outputDirectory, stem + ".receipt.json"), JsonUtility.ToJson(receipt, true), Utf8NoBom);

            Assert.That(controller.ReplayStatus, Is.EqualTo(M4QaReplayStatus.Completed),
                "Replay stopped: " + controller.ReplayFailure + "; consumed " +
                controller.ReplayConsumedTicks + "/" + controller.ReplayTotalTicks + " ticks.");
            Assert.That(controller.ReplayConsumedTicks, Is.EqualTo(controller.ReplayTotalTicks),
                "Replay completed with unconsumed input commands.");
        }

        static bool IsNearNightBoundary(List<int> nightBoundaries, int consumed)
        {
            foreach (int boundary in nightBoundaries)
                if (Math.Abs(consumed - boundary) <= 2) return true;
            return false;
        }

        static string GetSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(stream);
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        static ReplayBoundaryState CaptureState(M4NightOneGrayboxController controller, M4ReplayTape tape)
        {
            M4ThreeNightQaSceneEntry entry = controller.GetComponent<M4ThreeNightQaSceneEntry>();
            M4DiagnosticBootstrap bootstrap = controller.GetComponent<M4DiagnosticBootstrap>();
            if (bootstrap == null) bootstrap = UnityEngine.Object.FindFirstObjectByType<M4DiagnosticBootstrap>();
            int consumed = controller.ReplayConsumedTicks;
            var state = new ReplayBoundaryState
            {
                consumedTicks = consumed,
                nextTapeNight = consumed < tape.ticks.Length ? tape.ticks[consumed].nightOrdinal : 0,
                activeNight = entry == null ? 0 : entry.ActiveNightOrdinal,
                completedNights = entry == null ? 0 : entry.CompletedNights,
                fixedTickRunning = entry != null && entry.FixedTickRunning,
                stage = entry == null ? "QA_SCENE_ENTRY_MISSING" : entry.CurrentStage.ToString(),
                lastTickStatus = entry == null || entry.LastTickResult == null ? string.Empty : entry.LastTickResult.Status.ToString(),
                lastTickReason = entry == null || entry.LastTickResult == null ? string.Empty : entry.LastTickResult.Reason
            };
            var director = bootstrap == null || bootstrap.CanonicalSession == null ? null : bootstrap.CanonicalSession.BoundDirector;
            if (director != null)
            {
                var snapshot = director.Snapshot();
                state.totalTickets = snapshot.Tickets.Length;
                state.resolvedTickets = snapshot.ResolvedCount;
                state.activeTickets = snapshot.ActiveCount;
                state.pendingTickets = snapshot.PendingCount;
                state.poolWaitingTickets = snapshot.PoolWaitingCount;
            }
            return state;
        }
    }
}
