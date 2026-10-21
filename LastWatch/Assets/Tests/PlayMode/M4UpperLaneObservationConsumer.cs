using System;
using System.Collections.Generic;
using System.Globalization;
using LastWatch.Core.Partner;
using UnityEngine;

namespace LastWatch.Tests.M4
{
    [Serializable]
    internal sealed class UpperLaneCoreObservationReport
    {
        public int schemaVersion = 3;
        public string evidenceType = "DETACHED_CORE_SUPPORTED_FIELDS_POST_TICK_LIVE_QUERY";
        public string fullGeometryGateAssessment = "INCONCLUSIVE_CORE_DECLARED_COVERAGE_GAPS";
        public string contextAssessment;
        public bool independentNightFixedTickExpectationAvailable;
        public string nightTickExpectationReason = "ENTRY_OWNED_PROCESSED_TICK_STAMP; REQUIRES_CURRENT_COMPLETION_WINDOW";
        public string identityPolicy = "CANONICAL_RUN_ID_HAS_NO_CONTROLLER_COUNTERPART; CLOCK_MATCH_IS_NOT_FULL_IDENTITY_PASS; EMPTY_CAPTURE_RUN_ID_ALLOWED";
        public string timingPolicy = "AFTER_FIXED_UPDATE_COMPLETION_AND_EXISTING_TOOLS_PHYSICS_SYNC; BEFORE_UPDATE_OR_NIGHT_TRANSITION; NOT_SAME_TICK_SHOT_POSE";
        public string retentionPolicy = "FIRST_8_EXPOSED_CONTEXT_MATCHES_FIRST_16_ISSUES_AND_LAST_OBSERVATION; COUNTERS_INCLUDE_ALL_ATTEMPTS; REPRESENTATIVES_NOT_COMPLETE_POSE_HISTORY";
        public int attempts;
        public int exposedContextMatchedObservations;
        public int expectedCommandUnavailableObservations;
        public int completedContextUnavailableObservations;
        public int exposedContextMismatchObservations;
        public int captureFailureObservations;
        public int independentNightTickUnknownObservations;
        public int hostContextAvailableObservations;
        public int hostContextUnavailableObservations;
        public int hostWindowChangedObservations;
        public int hostInstanceChangedObservations;
        public int hostGenerationMismatchObservations;
        public int hostRunMismatchObservations;
        public int nightMismatchObservations;
        public int tickMismatchObservations;
        public int tapeMismatchObservations;
        public bool fullRunIdentityComparisonAvailable = false;
        public string runIdentityAssessment = "INCONCLUSIVE_CONTROLLER_CANONICAL_RUN_ID_NOT_EXPOSED";
        public int geometryUnavailableObservations;
        public int registeredActorObservations;
        public int invalidAimSampleObservations;
        public int replayConsumedTicks;
        public int skippedConsumedTicks;
        public bool exposedContextWindowComplete;
        public int maximumMatchedRepresentatives = 8;
        public int maximumIssueRepresentatives = 16;
        public int omittedMatchedRepresentatives;
        public int omittedIssueRepresentatives;
        public bool representativeHistoryComplete;
        public string lastSnapshotSerializationFailure = "";
        public string[] coverageGaps;
        public UpperLaneCoreObservationRecord[] matchedRepresentatives;
        public UpperLaneCoreObservationRecord[] issueRepresentatives;
        public UpperLaneCoreObservationRecord lastObservation;
        public string movementEvidencePolicy = "ALL_OBSERVED_ACTOR_ROWS; SUPPORTED_ATTEMPT_LABELS_AND_ACTOR_TUPLE_ONLY; NO_FULL_RUN_IDENTITY_OR_GEOMETRY_PASS";
        public UpperLaneMovementCounts movement = new UpperLaneMovementCounts();
    }

    [Serializable]
    internal sealed class UpperLaneCoreObservationRecord
    {
        public int consumedTicks;
        public bool expectedCommandAvailable;
        public int expectedRunFixedTick = -1;
        public int expectedNightOrdinal;
        public bool expectedNightFixedTickAvailable;
        public int expectedNightFixedTick = -1;
        public int tapeRunFixedTick = -1;
        public int tapeNightOrdinal;
        public bool tapeContextMatches;
        public bool hostContextAvailable;
        public int hostInstanceId;
        public string hostRunId;
        public string hostGeneration;
        public string hostCompletionAttempt;
        public string hostUnavailableReason;
        public int hostAdvancedRunFixedTick = -1;
        public int hostAdvancedNightFixedTick = -1;
        public bool hostNightCompleted;
        public bool hostWindowStable;
        public bool hostInstanceChanged;
        public bool hostGenerationMismatch;
        public bool hostRunMismatch;
        public bool nightMismatch;
        public bool tickMismatch;
        public string contextClassification;
        public string reason;
        public bool snapshotAvailable;
        public UpperLaneCoreSnapshotRecord snapshot;
        public UpperLaneMovementCounts movement = new UpperLaneMovementCounts();
    }

    [Serializable]
    internal sealed class UpperLaneCoreSnapshotRecord
    {
        public string captureRunId;
        public string qaRunValue;
        public bool completedTickAvailable;
        public int completedRunFixedTick;
        public int completedNightFixedTick;
        public int nightOrdinal;
        public int replayConsumedTicks;
        public string timing;
        public bool geometryAvailable;
        public bool unavailableFaceValueAvailable;
        public string unavailableFace;
        public UpperLaneStationRecord[] stations;
        public UpperLaneRouteRecord[] routes;
        public UpperLaneTargetRecord[] targets;
        public UpperLaneActorRecord[] actors;
        public bool liveCameraAvailable;
        public Vector3 liveCameraOrigin;
        public Vector3 liveCameraForward;
        public float liveCameraPitchDegrees;
        public float interactivePitchMinimumDegrees;
        public float interactivePitchMaximumDegrees;
        public string selectedWeapon;
        public float selectedWeaponRangeMeters;
        public int shotLayerMask;
        public string shotTriggerQuery;
        public bool globalQueriesHitTriggers;
        public bool partnerPlacementAvailable;
        public Vector3 partnerPosition;
        public Quaternion partnerRotation;
        public string[] coverageGaps;
    }

    [Serializable]
    internal sealed class UpperLaneStationRecord
    {
        public string id;
        public string face;
        public Vector3 position;
        public Vector3 eyePosition;
        public bool isAvailable;
    }

    [Serializable]
    internal sealed class UpperLaneRouteRecord
    {
        public string id;
        public string face;
        public string kind;
        public bool isAvailable;
        public float sampleSpacingMeters;
        public Vector3[] authoredSamples;
        public string poseMeaning;
    }

    [Serializable]
    internal sealed class UpperLaneTargetRecord
    {
        public string id;
        public string face;
        public string kind;
        public Vector3 bottom;
        public Vector3 top;
        public float radius;
        public bool isAvailable;
        public UpperLaneAimSampleRecord[] querySamples;
    }

    [Serializable]
    internal sealed class UpperLaneActorRecord
    {
        public string entityValue;
        public string ticketId;
        public string archetype;
        public string assignedAttackPoint;
        public bool registered;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 lossyScale;
        public bool activeAndEnabled;
        public bool routeAvailable;
        public int nextWaypointIndex;
        public int climbWaypointStartIndex;
        public bool climbSegmentSelected;
        public float appliedGroundRouteLiftMeters;
        public float authoredSampleCenterOffsetMeters;
        public Vector3[] appliedAuthoredWaypoints;
        public bool hasReachedStandOff;
        public bool surfaceGapAvailable;
        public string surfaceGapMeters;
        public string[] canonicalStations;
        public string[] assignedRoutes;
        public string routeMeaning;
        public UpperLaneColliderRecord[] colliders;
        public UpperLaneAimSampleRecord[] aimSamples;
        public bool movementAttemptSnapshotAvailable;
        public string movementContextClassification;
        public UpperLaneMovementAttemptRecord latestMovementAttempt;
    }

    [Serializable]
    internal sealed class UpperLaneMovementCounts
    {
        public int actorRows;
        public int noInvocation;
        public int attemptedContextUnavailable;
        public int comparisonUnavailable;
        public int exposedAttemptMismatch;
        public int supportedAttemptLabelsMatched;
        public int movementNotProposed;
        public int supportGuardInvoked;
        public int targetGuardInvoked;
        public int gapMeasurementInvoked;
        public int transformApplied;

        public void Add(UpperLaneMovementCounts value)
        {
            actorRows += value.actorRows; noInvocation += value.noInvocation;
            attemptedContextUnavailable += value.attemptedContextUnavailable; comparisonUnavailable += value.comparisonUnavailable;
            exposedAttemptMismatch += value.exposedAttemptMismatch; supportedAttemptLabelsMatched += value.supportedAttemptLabelsMatched;
            movementNotProposed += value.movementNotProposed; supportGuardInvoked += value.supportGuardInvoked;
            targetGuardInvoked += value.targetGuardInvoked; gapMeasurementInvoked += value.gapMeasurementInvoked;
            transformApplied += value.transformApplied;
        }
    }

    [Serializable]
    internal sealed class UpperLaneMovementAttemptRecord
    {
        public string domain, timing, outcome, sequence, entityValue, ticketId, archetype, assignedAttackPoint;
        public bool invocationAvailable, canonicalContextAvailable;
        public string contextUnavailableReason;
        public bool attemptedRunFixedTickAvailable, attemptedNightFixedTickAvailable, nightOrdinalAvailable;
        public int attemptedRunFixedTick = -1, attemptedNightFixedTick = -1, nightOrdinal = -1;
        public int waypointIndexBefore, waypointIndexAfter;
        public bool movementProposed, climbSegmentSelected, destinationAvailable;
        public Vector3 destination, priorPosition, finalPosition;
        public Quaternion priorRotation, finalRotation;
        public bool proposedPositionAvailable, proposedRotationAvailable;
        public Vector3 proposedPosition;
        public Quaternion proposedRotation;
        public bool supportGuardInvoked, supportResolvedAvailable, supportResolved, supportResolvedPositionAvailable;
        public Vector3 supportResolvedPosition;
        public bool targetGuardInvoked, assignedTargetEnabledAvailable, assignedTargetEnabled, targetClassificationAvailable;
        public string targetClassification, targetPenetrationDepthMeters;
        public bool targetPenetrationDepthAvailable;
        public bool gapMeasurementInvoked, gapMeasurementSucceededAvailable, gapMeasurementSucceeded, measuredPriorPoseGapAvailable;
        public string measuredPriorPoseGapMeters;
        public bool transformApplied, moverColliderInstanceIdAvailable, assignedTargetColliderInstanceIdAvailable;
        public int moverColliderInstanceId, assignedTargetColliderInstanceId;
        public string moverRole, targetRole, guardMeaning;
    }

    [Serializable]
    internal sealed class UpperLaneColliderRecord
    {
        public int instanceId;
        public string name;
        public string colliderType;
        public bool enabled;
        public bool activeInHierarchy;
        public bool isTrigger;
        public bool isDirectActorCollider;
        public bool acceptedHitRole;
        public bool nonTriggerPhysicalCollider;
        public string role;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public string boundsTiming;
    }

    [Serializable]
    internal sealed class UpperLaneAimSampleRecord
    {
        public string semantic;
        public Vector3 position;
        public int colliderInstanceId;
        public bool valid;
        public string unsupportedReason;
        public string domain;
    }

    // Serialization only. Does not cast new rays, resolve movement, or certify a gate.
    internal sealed class UpperLaneCoreObservationAccumulator
    {
        readonly UpperLaneCoreObservationReport report = new UpperLaneCoreObservationReport();
        readonly List<UpperLaneCoreObservationRecord> matched = new List<UpperLaneCoreObservationRecord>();
        readonly List<UpperLaneCoreObservationRecord> issues = new List<UpperLaneCoreObservationRecord>();
        readonly SortedSet<string> gaps = new SortedSet<string>(StringComparer.Ordinal);
        M4UpperLaneSnapshot lastSnapshot;

        M4ThreeNightQaSceneEntry previousHost;
        string previousRunId;
        ulong previousGeneration;
        int previousNight;
        bool previousHostAvailable;

        public void Observe(M4ThreeNightQaSceneEntry entry, M4NightOneGrayboxController controller,
            int consumed, M4ReplayTick command)
        {
            var record = new UpperLaneCoreObservationRecord
            {
                consumedTicks = consumed, expectedCommandAvailable = command != null,
                tapeRunFixedTick = command == null ? -1 : command.runFixedTick,
                tapeNightOrdinal = command == null ? 0 : command.nightOrdinal,
                hostInstanceId = entry == null ? 0 : entry.GetInstanceID(),
                hostUnavailableReason = entry == null ? "ENTRY_UNAVAILABLE" : ""
            };
            report.attempts++;
            M4UpperLaneSnapshot snapshot = null;
            M4CanonicalCompletedTickSnapshot host = null;
            int registeredActors = 0, invalidSamples = 0;
            try
            {
                bool available = entry != null && entry.TryGetLastCanonicalCompletedTick(out host);
                record.hostContextAvailable = available && host != null && host.CompletedTickAvailable;
                if (host != null)
                {
                    record.hostGeneration = host.ContextGeneration.ToString(CultureInfo.InvariantCulture);
                    record.hostCompletionAttempt = host.CompletionAttempt.ToString(CultureInfo.InvariantCulture);
                    record.hostUnavailableReason = host.UnavailableReason ?? "";
                }
                if (record.hostContextAvailable)
                {
                    record.expectedRunFixedTick = host.RunFixedTick;
                    record.expectedNightOrdinal = host.NightOrdinal;
                    record.expectedNightFixedTick = host.NightFixedTick;
                    record.expectedNightFixedTickAvailable = true;
                    record.hostRunId = host.RunId;
                    record.hostAdvancedRunFixedTick = host.AdvancedRunFixedTick;
                    record.hostAdvancedNightFixedTick = host.AdvancedNightFixedTick;
                    record.hostNightCompleted = host.NightCompleted;
                    record.hostInstanceChanged = previousHostAvailable && !ReferenceEquals(entry, previousHost);
                    record.hostRunMismatch = previousHostAvailable && ReferenceEquals(entry, previousHost) &&
                        !string.Equals(host.RunId, previousRunId, StringComparison.Ordinal);
                    record.hostGenerationMismatch = previousHostAvailable && ReferenceEquals(entry, previousHost) &&
                        host.NightOrdinal == previousNight && host.ContextGeneration != previousGeneration;
                }
                snapshot = controller == null ? null : controller.CaptureUpperLaneObservation();
                record.snapshotAvailable = snapshot != null;
                if (snapshot != null) foreach (string gap in snapshot.CoverageGaps) gaps.Add(gap);
                M4CanonicalCompletedTickSnapshot after;
                record.hostWindowStable = record.hostContextAvailable && entry != null &&
                    entry.TryGetLastCanonicalCompletedTick(out after) && after.CompletedTickAvailable &&
                    after.ContextGeneration == host.ContextGeneration && after.CompletionAttempt == host.CompletionAttempt &&
                    string.Equals(after.RunId, host.RunId, StringComparison.Ordinal) &&
                    after.RunFixedTick == host.RunFixedTick && after.NightFixedTick == host.NightFixedTick &&
                    after.NightOrdinal == host.NightOrdinal;
                record.tapeContextMatches = command != null && record.hostContextAvailable &&
                    command.runFixedTick == host.RunFixedTick && command.nightOrdinal == host.NightOrdinal &&
                    snapshot != null && snapshot.ReplayConsumedTicks == consumed;
                if (record.hostContextAvailable && snapshot != null && snapshot.CompletedTickAvailable)
                {
                    record.nightMismatch = snapshot.NightOrdinal != host.NightOrdinal;
                    record.tickMismatch = snapshot.CompletedRunFixedTick != host.RunFixedTick ||
                        snapshot.CompletedNightFixedTick != host.NightFixedTick;
                }
                if (command == null) record.contextClassification = "EXPECTED_COMMAND_UNAVAILABLE";
                else if (!record.hostContextAvailable) record.contextClassification = "HOST_CONTEXT_UNAVAILABLE";
                else if (snapshot == null || !snapshot.CompletedTickAvailable) record.contextClassification = "COMPLETED_CONTEXT_UNAVAILABLE";
                else if (!record.hostWindowStable || record.hostInstanceChanged || record.hostGenerationMismatch ||
                    record.hostRunMismatch || record.nightMismatch || record.tickMismatch || !record.tapeContextMatches)
                    record.contextClassification = "EXPOSED_CONTEXT_MISMATCH";
                else
                {
                    record.contextClassification = "SUPPORTED_PROCESSED_CLOCK_CONTEXT_MATCHED";
                    foreach (M4UpperLaneActorSnapshot actor in snapshot.Actors)
                    {
                        if (actor.Registered) registeredActors++;
                        foreach (M4UpperLaneAimSample sample in actor.AimSamples) if (!sample.Valid) invalidSamples++;
                    }
                }
                record.reason = record.contextClassification == "HOST_CONTEXT_UNAVAILABLE"
                    ? record.hostUnavailableReason : "SUPPORTED_CLOCKS_AND_SEPARATE_TAPE_RECONCILIATION; FULL_RUN_IDENTITY_UNSUPPORTED";
                bool retain = record.contextClassification == "SUPPORTED_PROCESSED_CLOCK_CONTEXT_MATCHED"
                    ? matched.Count < report.maximumMatchedRepresentatives : issues.Count < report.maximumIssueRepresentatives;
                if (retain && snapshot != null) record.snapshot = Serialize(snapshot, record);
            }
            catch (Exception error)
            {
                record.contextClassification = "CAPTURE_FAILURE";
                record.reason = error.GetType().Name;
            }
            record.movement = CountMovement(snapshot, record);
            report.movement.Add(record.movement);
            if (record.hostContextAvailable)
            {
                report.hostContextAvailableObservations++;
                if (!record.hostWindowStable) report.hostWindowChangedObservations++;
                if (record.hostInstanceChanged) report.hostInstanceChangedObservations++;
                if (record.hostGenerationMismatch) report.hostGenerationMismatchObservations++;
                if (record.hostRunMismatch) report.hostRunMismatchObservations++;
                if (record.nightMismatch) report.nightMismatchObservations++;
                if (record.tickMismatch) report.tickMismatchObservations++;
                if (record.expectedCommandAvailable && !record.tapeContextMatches) report.tapeMismatchObservations++;
                previousHost = entry; previousRunId = host.RunId;
                previousGeneration = host.ContextGeneration; previousNight = host.NightOrdinal;
                previousHostAvailable = true;
            }
            else report.independentNightTickUnknownObservations++;
            lastSnapshot = snapshot;
            report.lastObservation = record;
            if (record.contextClassification == "SUPPORTED_PROCESSED_CLOCK_CONTEXT_MATCHED")
            {
                report.exposedContextMatchedObservations++;
                if (!snapshot.GeometryAvailable) report.geometryUnavailableObservations++;
                report.registeredActorObservations += registeredActors;
                report.invalidAimSampleObservations += invalidSamples;
                if (matched.Count < report.maximumMatchedRepresentatives) matched.Add(record);
                else report.omittedMatchedRepresentatives++;
            }
            else
            {
                switch (record.contextClassification)
                {
                    case "EXPECTED_COMMAND_UNAVAILABLE": report.expectedCommandUnavailableObservations++; break;
                    case "HOST_CONTEXT_UNAVAILABLE": report.hostContextUnavailableObservations++; break;
                    case "COMPLETED_CONTEXT_UNAVAILABLE": report.completedContextUnavailableObservations++; break;
                    case "EXPOSED_CONTEXT_MISMATCH": report.exposedContextMismatchObservations++; break;
                    default: report.captureFailureObservations++; break;
                }
                if (issues.Count < report.maximumIssueRepresentatives) issues.Add(record);
                else report.omittedIssueRepresentatives++;
            }
        }
        public UpperLaneCoreObservationReport Complete(int consumed, int skipped)
        {
            // Retain the final detached Core values without serializing every discarded pose.
            if (report.lastObservation != null && report.lastObservation.snapshot == null && lastSnapshot != null)
            {
                try { report.lastObservation.snapshot = Serialize(lastSnapshot, report.lastObservation); }
                catch (Exception error) { report.lastSnapshotSerializationFailure = error.GetType().Name; }
            }
            report.replayConsumedTicks = consumed;
            report.skippedConsumedTicks = skipped;
            report.exposedContextWindowComplete = consumed > 0 && skipped == 0 &&
                report.attempts == consumed && report.exposedContextMatchedObservations == consumed;
            report.contextAssessment = report.attempts == 0 ? "NOT_OBSERVED" :
                !report.exposedContextWindowComplete ? "INCONCLUSIVE_CONTEXT_UNAVAILABLE_MISMATCH_OR_SKIPPED" :
                "MATCH_SUPPORTED_PROCESSED_CLOCK_WINDOW_RUN_IDENTITY_UNSUPPORTED";
            report.independentNightFixedTickExpectationAvailable = report.attempts > 0 &&
                report.hostContextAvailableObservations == report.attempts;
            report.representativeHistoryComplete = report.omittedMatchedRepresentatives == 0 &&
                report.omittedIssueRepresentatives == 0;
            report.coverageGaps = Copy(gaps);
            report.matchedRepresentatives = matched.ToArray();
            report.issueRepresentatives = issues.ToArray();
            return report;
        }

        static UpperLaneCoreSnapshotRecord Serialize(M4UpperLaneSnapshot source, UpperLaneCoreObservationRecord observation)
        {
            var stations = new List<UpperLaneStationRecord>();
            foreach (var value in source.Stations)
                stations.Add(new UpperLaneStationRecord { id = value.Id.ToString(), face = value.Face.HasValue ? value.Face.Value.ToString() : "none", position = value.Position, eyePosition = value.EyePosition, isAvailable = value.IsAvailable });
            var routes = new List<UpperLaneRouteRecord>();
            foreach (var value in source.Routes)
                routes.Add(new UpperLaneRouteRecord { id = value.Id.ToString(), face = value.Face.ToString(), kind = value.Kind.ToString(), isAvailable = value.IsAvailable, sampleSpacingMeters = value.SampleSpacingMeters, authoredSamples = Copy(value.AuthoredSamples), poseMeaning = value.PoseMeaning });
            var targets = new List<UpperLaneTargetRecord>();
            foreach (var value in source.Targets)
                targets.Add(new UpperLaneTargetRecord { id = value.Descriptor.Id.ToString(), face = value.Descriptor.Face.ToString(), kind = value.Descriptor.Kind.ToString(), bottom = value.Descriptor.Bottom, top = value.Descriptor.Top, radius = value.Descriptor.Radius, isAvailable = value.Descriptor.IsAvailable, querySamples = Samples(value.QuerySamples) });
            var actors = new List<UpperLaneActorRecord>();
            foreach (var value in source.Actors)
            {
                var colliders = new List<UpperLaneColliderRecord>();
                foreach (var collider in value.Colliders)
                    colliders.Add(new UpperLaneColliderRecord { instanceId = collider.InstanceId, name = collider.Name, colliderType = collider.ColliderType, enabled = collider.Enabled, activeInHierarchy = collider.ActiveInHierarchy, isTrigger = collider.IsTrigger, isDirectActorCollider = collider.IsDirectActorCollider, acceptedHitRole = collider.AcceptedHitRole, nonTriggerPhysicalCollider = collider.NonTriggerPhysicalCollider, role = collider.Role, boundsCenter = collider.ObservedBounds.center, boundsSize = collider.ObservedBounds.size, boundsTiming = collider.BoundsTiming });
                actors.Add(new UpperLaneActorRecord
                {
                    entityValue = value.EntityValue.ToString(CultureInfo.InvariantCulture), ticketId = value.TicketId.ToString(CultureInfo.InvariantCulture), archetype = value.Archetype.ToString(), assignedAttackPoint = value.AssignedAttackPoint.ToString(), registered = value.Registered,
                    position = value.Position, rotation = value.Rotation, lossyScale = value.LossyScale, activeAndEnabled = value.ActiveAndEnabled,
                    routeAvailable = value.RouteAvailable, nextWaypointIndex = value.NextWaypointIndex, climbWaypointStartIndex = value.ClimbWaypointStartIndex, climbSegmentSelected = value.ClimbSegmentSelected, appliedGroundRouteLiftMeters = value.AppliedGroundRouteLiftMeters, authoredSampleCenterOffsetMeters = value.AuthoredSampleCenterOffsetMeters, appliedAuthoredWaypoints = Copy(value.AppliedAuthoredWaypoints),
                    hasReachedStandOff = value.HasReachedStandOff, surfaceGapAvailable = value.SurfaceGapAvailable, surfaceGapMeters = value.SurfaceGapAvailable ? value.SurfaceGapMeters.ToString("R", CultureInfo.InvariantCulture) : "unavailable",
                    canonicalStations = Names(value.CanonicalStations), assignedRoutes = Names(value.AssignedRoutes), routeMeaning = value.RouteMeaning, colliders = colliders.ToArray(), aimSamples = Samples(value.AimSamples),
                    movementAttemptSnapshotAvailable = value.LatestMovementAttempt != null,
                    movementContextClassification = MovementClassification(value, observation),
                    latestMovementAttempt = Movement(value.LatestMovementAttempt)
                });
            }
            return new UpperLaneCoreSnapshotRecord
            {
                captureRunId = source.CaptureRunId, qaRunValue = source.QaRunValue.ToString(CultureInfo.InvariantCulture), completedTickAvailable = source.CompletedTickAvailable, completedRunFixedTick = source.CompletedRunFixedTick, completedNightFixedTick = source.CompletedNightFixedTick, nightOrdinal = source.NightOrdinal, replayConsumedTicks = source.ReplayConsumedTicks, timing = source.Timing,
                geometryAvailable = source.GeometryAvailable, unavailableFaceValueAvailable = source.GeometryAvailable && source.UnavailableFace.HasValue, unavailableFace = !source.GeometryAvailable ? "unavailable_geometry" : source.UnavailableFace.HasValue ? source.UnavailableFace.Value.ToString() : "none",
                stations = stations.ToArray(), routes = routes.ToArray(), targets = targets.ToArray(), actors = actors.ToArray(), liveCameraAvailable = source.LiveCameraAvailable, liveCameraOrigin = source.LiveCameraOrigin, liveCameraForward = source.LiveCameraForward, liveCameraPitchDegrees = source.LiveCameraPitchDegrees, interactivePitchMinimumDegrees = source.InteractivePitchMinimumDegrees, interactivePitchMaximumDegrees = source.InteractivePitchMaximumDegrees,
                selectedWeapon = source.SelectedWeapon, selectedWeaponRangeMeters = source.SelectedWeaponRangeMeters, shotLayerMask = source.ShotLayerMask, shotTriggerQuery = source.ShotTriggerQuery.ToString(), globalQueriesHitTriggers = source.GlobalQueriesHitTriggers, partnerPlacementAvailable = source.PartnerPlacementAvailable, partnerPosition = source.PartnerPosition, partnerRotation = source.PartnerRotation, coverageGaps = Copy(source.CoverageGaps)
            };
        }

        static string MovementClassification(M4UpperLaneActorSnapshot actor, UpperLaneCoreObservationRecord observation)
        {
            var value = actor.LatestMovementAttempt;
            if (value == null || !value.InvocationAvailable) return "NO_INVOCATION";
            if (!value.CanonicalContextAvailable || !value.AttemptedRunFixedTick.HasValue ||
                !value.AttemptedNightFixedTick.HasValue || !value.NightOrdinal.HasValue) return "ATTEMPTED_CONTEXT_UNAVAILABLE";
            if (observation.contextClassification != "SUPPORTED_PROCESSED_CLOCK_CONTEXT_MATCHED") return "COMPARISON_UNAVAILABLE";
            if (value.AttemptedRunFixedTick.Value != observation.expectedRunFixedTick ||
                value.AttemptedNightFixedTick.Value != observation.expectedNightFixedTick ||
                value.NightOrdinal.Value != observation.expectedNightOrdinal || value.EntityValue != actor.EntityValue ||
                value.TicketId != actor.TicketId || value.Archetype != actor.Archetype ||
                value.AssignedAttackPoint != actor.AssignedAttackPoint) return "EXPOSED_ATTEMPT_MISMATCH";
            return "SUPPORTED_ATTEMPT_LABELS_MATCHED";
        }

        static UpperLaneMovementCounts CountMovement(M4UpperLaneSnapshot source, UpperLaneCoreObservationRecord observation)
        {
            var result = new UpperLaneMovementCounts();
            if (source == null) return result;
            foreach (var actor in source.Actors)
            {
                result.actorRows++;
                switch (MovementClassification(actor, observation))
                {
                    case "NO_INVOCATION": result.noInvocation++; break;
                    case "ATTEMPTED_CONTEXT_UNAVAILABLE": result.attemptedContextUnavailable++; break;
                    case "COMPARISON_UNAVAILABLE": result.comparisonUnavailable++; break;
                    case "EXPOSED_ATTEMPT_MISMATCH": result.exposedAttemptMismatch++; break;
                    default: result.supportedAttemptLabelsMatched++; break;
                }
                var value = actor.LatestMovementAttempt;
                if (value == null || !value.InvocationAvailable) continue;
                if (!value.MovementProposed) result.movementNotProposed++;
                if (value.SupportGuardInvoked) result.supportGuardInvoked++;
                if (value.TargetGuardInvoked) result.targetGuardInvoked++;
                if (value.GapMeasurementInvoked) result.gapMeasurementInvoked++;
                if (value.TransformApplied) result.transformApplied++;
            }
            return result;
        }

        static UpperLaneMovementAttemptRecord Movement(M4MovementAttemptSnapshot value)
        {
            if (value == null) return null;
            return new UpperLaneMovementAttemptRecord
            {
                domain = value.Domain, timing = value.Timing, outcome = value.Outcome,
                sequence = value.Sequence.ToString(CultureInfo.InvariantCulture),
                entityValue = value.EntityValue.ToString(CultureInfo.InvariantCulture), ticketId = value.TicketId.ToString(CultureInfo.InvariantCulture),
                archetype = value.Archetype.ToString(), assignedAttackPoint = value.AssignedAttackPoint.ToString(),
                invocationAvailable = value.InvocationAvailable, canonicalContextAvailable = value.CanonicalContextAvailable,
                contextUnavailableReason = value.ContextUnavailableReason,
                attemptedRunFixedTickAvailable = value.AttemptedRunFixedTick.HasValue, attemptedRunFixedTick = value.AttemptedRunFixedTick ?? -1,
                attemptedNightFixedTickAvailable = value.AttemptedNightFixedTick.HasValue, attemptedNightFixedTick = value.AttemptedNightFixedTick ?? -1,
                nightOrdinalAvailable = value.NightOrdinal.HasValue, nightOrdinal = value.NightOrdinal ?? -1,
                waypointIndexBefore = value.WaypointIndexBefore, waypointIndexAfter = value.WaypointIndexAfter,
                movementProposed = value.MovementProposed, climbSegmentSelected = value.ClimbSegmentSelected,
                destinationAvailable = value.Destination.HasValue, destination = value.Destination ?? Vector3.zero,
                priorPosition = value.PriorPosition, priorRotation = value.PriorRotation,
                proposedPositionAvailable = value.ProposedPosition.HasValue, proposedPosition = value.ProposedPosition ?? Vector3.zero,
                proposedRotationAvailable = value.ProposedRotation.HasValue, proposedRotation = value.ProposedRotation ?? Quaternion.identity,
                supportGuardInvoked = value.SupportGuardInvoked, supportResolvedAvailable = value.SupportResolved.HasValue,
                supportResolved = value.SupportResolved ?? false,
                supportResolvedPositionAvailable = value.SupportResolvedPosition.HasValue, supportResolvedPosition = value.SupportResolvedPosition ?? Vector3.zero,
                targetGuardInvoked = value.TargetGuardInvoked, assignedTargetEnabledAvailable = value.AssignedTargetEnabled.HasValue,
                assignedTargetEnabled = value.AssignedTargetEnabled ?? false,
                targetClassificationAvailable = value.TargetClassification.HasValue,
                targetClassification = value.TargetClassification.HasValue ? value.TargetClassification.Value.ToString() : "unavailable",
                targetPenetrationDepthAvailable = value.TargetPenetrationDepthMeters.HasValue,
                targetPenetrationDepthMeters = value.TargetPenetrationDepthMeters.HasValue ? value.TargetPenetrationDepthMeters.Value.ToString("R", CultureInfo.InvariantCulture) : "unavailable",
                gapMeasurementInvoked = value.GapMeasurementInvoked, gapMeasurementSucceededAvailable = value.GapMeasurementSucceeded.HasValue,
                gapMeasurementSucceeded = value.GapMeasurementSucceeded ?? false,
                measuredPriorPoseGapAvailable = value.MeasuredPriorPoseGapMeters.HasValue,
                measuredPriorPoseGapMeters = value.MeasuredPriorPoseGapMeters.HasValue ? value.MeasuredPriorPoseGapMeters.Value.ToString("R", CultureInfo.InvariantCulture) : "unavailable",
                transformApplied = value.TransformApplied, finalPosition = value.FinalPosition, finalRotation = value.FinalRotation,
                moverColliderInstanceIdAvailable = value.MoverColliderInstanceId.HasValue, moverColliderInstanceId = value.MoverColliderInstanceId ?? 0,
                assignedTargetColliderInstanceIdAvailable = value.AssignedTargetColliderInstanceId.HasValue,
                assignedTargetColliderInstanceId = value.AssignedTargetColliderInstanceId ?? 0,
                moverRole = value.MoverRole, targetRole = value.TargetRole, guardMeaning = value.GuardMeaning
            };
        }

        static UpperLaneAimSampleRecord[] Samples(IEnumerable<M4UpperLaneAimSample> source)
        {
            var result = new List<UpperLaneAimSampleRecord>();
            foreach (var value in source)
                result.Add(new UpperLaneAimSampleRecord { semantic = value.Semantic, position = value.Position, colliderInstanceId = value.ColliderInstanceId, valid = value.Valid, unsupportedReason = value.UnsupportedReason, domain = value.Domain });
            return result.ToArray();
        }

        static T[] Copy<T>(IEnumerable<T> source) { return new List<T>(source).ToArray(); }
        static string[] Names<T>(IEnumerable<T> source)
        {
            var result = new List<string>();
            foreach (T value in source) result.Add(value.ToString());
            return result.ToArray();
        }
    }
}
