using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastWatch.Core.Space
{
    /// <summary>
    /// Resolves a proposed moving-collider pose against the authored tower support legs.
    /// The caller owns movement and must retain its prior safe pose when resolution fails.
    /// </summary>
    public static class M2SupportLegCollisionGuard
    {
        public static bool TryResolve(Collider movingCollider, IReadOnlyList<Collider> supportLegs,
            Vector3 proposedPosition, Quaternion proposedRotation, out Vector3 resolvedPosition)
            => TryResolveCore(movingCollider, supportLegs, proposedPosition, proposedRotation,
                out resolvedPosition, null);

        public static bool TryResolveWithTrace(Collider movingCollider, IReadOnlyList<Collider> supportLegs,
            Vector3 proposedPosition, Quaternion proposedRotation, out Vector3 resolvedPosition,
            out M2SupportResolutionTraceSnapshot trace)
        {
            if (movingCollider == null) throw new ArgumentNullException(nameof(movingCollider));
            if (supportLegs == null) throw new ArgumentNullException(nameof(supportLegs));
            SupportTraceCapture capture = SupportTraceCapture.Create(supportLegs.Count);
            resolvedPosition = proposedPosition;
            trace = null;
            try { return TryResolveCore(movingCollider, supportLegs, proposedPosition, proposedRotation,
                out resolvedPosition, capture); }
            catch (Exception error) { capture?.Abort(error, resolvedPosition); throw; }
            finally { trace = capture?.Freeze(); }
        }

        static bool TryResolveCore(Collider movingCollider, IReadOnlyList<Collider> supportLegs,
            Vector3 proposedPosition, Quaternion proposedRotation, out Vector3 resolvedPosition,
            SupportTraceCapture capture)
        {
            if (movingCollider == null) throw new ArgumentNullException(nameof(movingCollider));
            if (supportLegs == null) throw new ArgumentNullException(nameof(supportLegs));

            resolvedPosition = proposedPosition;
            int correctionPasses = Math.Max(1, supportLegs.Count * 2);
            for (int pass = 0; pass < correctionPasses; pass++)
            {
                bool corrected = false;
                for (int index = 0; index < supportLegs.Count; index++)
                {
                    Collider supportLeg = supportLegs[index];
                    if (!IsSolidEnabled(supportLeg))
                    {
                        capture?.Skip("CORRECTION", pass, index, movingCollider, supportLeg,
                            resolvedPosition, proposedRotation);
                        continue;
                    }

                    Vector3 supportPosition = supportLeg.transform.position;
                    Quaternion supportRotation = supportLeg.transform.rotation;
                    capture?.Begin("CORRECTION", pass, index, movingCollider, supportLeg,
                        resolvedPosition, proposedRotation);
                    bool overlap = Physics.ComputePenetration(movingCollider, resolvedPosition, proposedRotation,
                            supportLeg, supportPosition, supportRotation,
                            out Vector3 direction, out float depth);
                    capture?.Returned(overlap, depth, direction);
                    if (!overlap || depth <= 0f)
                    {
                        capture?.End(resolvedPosition, false, null);
                        continue;
                    }

                    if (!IsFinite(depth) || !IsFinite(direction) || direction.sqrMagnitude <= 0f)
                    {
                        capture?.End(resolvedPosition, false, null);
                        capture?.Finish("INVALID_CORRECTION_GEOMETRY", false, resolvedPosition);
                        return false;
                    }

                    float separationMargin = Mathf.Max(movingCollider.contactOffset, supportLeg.contactOffset);
                    resolvedPosition += direction.normalized * (depth + separationMargin);
                    capture?.End(resolvedPosition, true, separationMargin);
                    corrected = true;
                }

                if (!corrected)
                {
                    capture?.Finish("EARLY_NO_CORRECTION_PASS", true, resolvedPosition);
                    return true;
                }
            }

            bool result = !HasPositivePenetrationCore(movingCollider, supportLegs,
                resolvedPosition, proposedRotation, capture);
            capture?.Finish("POST_MAX_PASS_VERIFICATION", result, resolvedPosition);
            return result;
        }

        public static bool HasPositivePenetration(Collider movingCollider, IReadOnlyList<Collider> supportLegs,
            Vector3 position, Quaternion rotation)
            => HasPositivePenetrationCore(movingCollider, supportLegs, position, rotation, null);

        static bool HasPositivePenetrationCore(Collider movingCollider, IReadOnlyList<Collider> supportLegs,
            Vector3 position, Quaternion rotation, SupportTraceCapture capture)
        {
            if (movingCollider == null) throw new ArgumentNullException(nameof(movingCollider));
            if (supportLegs == null) throw new ArgumentNullException(nameof(supportLegs));

            for (int index = 0; index < supportLegs.Count; index++)
            {
                Collider supportLeg = supportLegs[index];
                if (!IsSolidEnabled(supportLeg))
                {
                    capture?.Skip("FINAL_VERIFICATION", null, index, movingCollider, supportLeg, position, rotation);
                    continue;
                }
                Vector3 supportPosition = supportLeg.transform.position;
                Quaternion supportRotation = supportLeg.transform.rotation;
                capture?.Begin("FINAL_VERIFICATION", null, index, movingCollider, supportLeg, position, rotation);
                bool overlap = Physics.ComputePenetration(movingCollider, position, rotation,
                        supportLeg, supportPosition, supportRotation,
                        out Vector3 direction, out float depth);
                capture?.Returned(overlap, depth, direction);
                capture?.End(position, false, null);
                if (overlap && depth > 0f)
                    return true;
            }
            return false;
        }

        static bool IsSolidEnabled(Collider collider) => collider != null && collider.enabled &&
            collider.gameObject.activeInHierarchy && !collider.isTrigger;

        static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // All observation failures disable the receipt, never change the guard result/exception.
        sealed class SupportTraceCapture
        {
            readonly int count;
            readonly List<M2SupportResolutionVisitSnapshot> visits = new List<M2SupportResolutionVisitSnapshot>();
            M2SupportTraceVisitRecord pending;
            bool failed, finished;
            bool? result;
            Vector3? final;
            string outcome = "NOT_COMPLETED", failure = string.Empty, originalException = string.Empty;
            SupportTraceCapture(int value) { count = value; }
            internal static SupportTraceCapture Create(int count)
            { try { return new SupportTraceCapture(count); } catch (Exception) { return null; } }
            void Disable(Exception error) { failed = true; failure = error.GetType().FullName; }
            internal void Begin(string phase, int? pass, int index, Collider mover, Collider support,
                Vector3 position, Quaternion rotation)
            {
                if (failed) return;
                try { pending = new M2SupportTraceVisitRecord { Phase = phase, PassOrdinal = pass,
                    SupportInputIndex = index, MoverColliderInstanceId = mover.GetInstanceID(),
                    SupportColliderInstanceId = support == null ? (int?)null : support.GetInstanceID(),
                    SupportName = support == null ? string.Empty : support.name,
                    PositionBefore = position, PositionAfter = position, Rotation = rotation,
                    PhysicsQueryInvoked = true, EligibilityReason = "SOLID_ENABLED_PAIR" }; }
                catch (Exception error) { Disable(error); }
            }
            internal void Skip(string phase, int? pass, int index, Collider mover, Collider support,
                Vector3 position, Quaternion rotation)
            {
                Begin(phase, pass, index, mover, support, position, rotation);
                if (failed) return;
                pending.PhysicsQueryInvoked = false;
                pending.EligibilityReason = "SKIPPED_NULL_DISABLED_INACTIVE_OR_TRIGGER";
                End(position, false, null);
            }
            internal void Returned(bool overlap, float depth, Vector3 direction)
            {
                if (failed || pending == null) return;
                pending.PhysicsQueryCompleted = true; pending.OverlapReported = overlap;
                pending.DepthValueFinite = IsFinite(depth); pending.DirectionValueFinite = IsFinite(direction);
                pending.DepthMeters = pending.DepthValueFinite == true ? (float?)depth : null;
                pending.Direction = pending.DirectionValueFinite == true ? (Vector3?)direction : null;
            }
            internal void End(Vector3 position, bool corrected, float? margin)
            {
                if (failed || pending == null) return;
                try { pending.PositionAfter = position; pending.CorrectionApplied = corrected;
                    pending.SeparationMarginMeters = margin;
                    visits.Add(new M2SupportResolutionVisitSnapshot(pending)); pending = null; }
                catch (Exception error) { Disable(error); }
            }
            internal void Finish(string value, bool guardResult, Vector3 position)
            { outcome = value; result = guardResult; final = position; finished = true; }
            internal void Abort(Exception error, Vector3 position)
            {
                originalException = error.GetType().FullName; outcome = "ORIGINAL_GUARD_EXCEPTION";
                final = position; End(position, false, null);
            }
            internal M2SupportResolutionTraceSnapshot Freeze()
            {
                try { return new M2SupportResolutionTraceSnapshot(count, finished && !failed,
                    result, final, outcome, failure, originalException, visits.ToArray()); }
                catch (Exception) { return null; }
            }
        }
    }

    internal sealed class M2SupportTraceVisitRecord
    {
        internal string Phase, SupportName, EligibilityReason;
        internal int? PassOrdinal, SupportColliderInstanceId;
        internal int SupportInputIndex, MoverColliderInstanceId;
        internal bool PhysicsQueryInvoked, PhysicsQueryCompleted, CorrectionApplied;
        internal bool? OverlapReported;
        internal bool? DepthValueFinite, DirectionValueFinite;
        internal float? DepthMeters, SeparationMarginMeters;
        internal Vector3 PositionBefore, PositionAfter;
        internal Vector3? Direction;
        internal Quaternion Rotation;
    }

    public sealed class M2SupportResolutionVisitSnapshot
    {
        public string Phase { get; }
        public int? PassOrdinal { get; }
        public int SupportInputIndex { get; }
        public int MoverColliderInstanceId { get; }
        public int? SupportColliderInstanceId { get; }
        public string SupportName { get; }
        public string EligibilityReason { get; }
        public Vector3 PositionBefore { get; }
        public Quaternion Rotation { get; }
        public Vector3 PositionAfter { get; }
        public bool PhysicsQueryInvoked { get; }
        public bool PhysicsQueryCompleted { get; }
        public bool? OverlapReported { get; }
        public float? DepthMeters { get; }
        public Vector3? Direction { get; }
        public bool? DepthValueFinite { get; }
        public bool? DirectionValueFinite { get; }
        public bool CorrectionApplied { get; }
        public float? SeparationMarginMeters { get; }
        internal M2SupportResolutionVisitSnapshot(M2SupportTraceVisitRecord value)
        {
            Phase = value.Phase; PassOrdinal = value.PassOrdinal; SupportInputIndex = value.SupportInputIndex;
            MoverColliderInstanceId = value.MoverColliderInstanceId; SupportColliderInstanceId = value.SupportColliderInstanceId;
            SupportName = value.SupportName; EligibilityReason = value.EligibilityReason;
            PositionBefore = value.PositionBefore; Rotation = value.Rotation; PositionAfter = value.PositionAfter;
            PhysicsQueryInvoked = value.PhysicsQueryInvoked; PhysicsQueryCompleted = value.PhysicsQueryCompleted;
            OverlapReported = value.OverlapReported; DepthMeters = value.DepthMeters; Direction = value.Direction;
            DepthValueFinite = value.DepthValueFinite; DirectionValueFinite = value.DirectionValueFinite;
            CorrectionApplied = value.CorrectionApplied; SeparationMarginMeters = value.SeparationMarginMeters;
        }
    }

    public sealed class M2SupportResolutionTraceSnapshot
    {
        public string Domain => "EXISTING_SUPPORT_GUARD_ACTUAL_VISITS_NO_EXTRA_QUERY";
        public int SupportInputCount { get; }
        public int ConfiguredCorrectionPasses { get; }
        public long MaximumPotentialQueryVisits { get; }
        public bool Complete { get; }
        public bool? GuardResult { get; }
        public Vector3? ResolvedPosition { get; }
        public string Outcome { get; }
        public string CaptureFailureReason { get; }
        public string OriginalExceptionType { get; }
        public IReadOnlyList<M2SupportResolutionVisitSnapshot> Visits { get; }
        public string QueryMeaning => "COMPLETED_IS_RETURNED; OVERLAP_REPORTED_IS_PHYSICS_BOOLEAN_NOT_QUERY_SUCCESS";
        internal M2SupportResolutionTraceSnapshot(int count, bool complete, bool? result, Vector3? final,
            string outcome, string failure, string originalException, M2SupportResolutionVisitSnapshot[] visits)
        {
            SupportInputCount = count; ConfiguredCorrectionPasses = Math.Max(1, count * 2);
            MaximumPotentialQueryVisits = (long)ConfiguredCorrectionPasses * count + count;
            Complete = complete; GuardResult = result; ResolvedPosition = final; Outcome = outcome;
            CaptureFailureReason = failure; OriginalExceptionType = originalException;
            Visits = Array.AsReadOnly((M2SupportResolutionVisitSnapshot[])visits.Clone());
        }
    }
}
