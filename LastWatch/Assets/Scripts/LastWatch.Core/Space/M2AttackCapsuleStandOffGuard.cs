using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastWatch.Core.Space
{
    public enum M2AttackCapsulePoseResult
    {
        Clear,
        TargetCapsuleReached
    }

    /// <summary>
    /// Classifies a proposed enemy pose against its assigned AttackCapsule target/query volume.
    /// The caller must retain its last safe pose when the target capsule is reached. This does
    /// not resolve or redefine attack behavior; non-target capsules can be measured separately.
    /// </summary>
    public static class M2AttackCapsuleStandOffGuard
    {
        public static M2AttackCapsulePoseResult ClassifyAssignedTargetPose(Collider movingCollider,
            Collider assignedTargetCapsule, Vector3 proposedPosition, Quaternion proposedRotation,
            out float penetrationDepth)
        {
            if (movingCollider == null) throw new ArgumentNullException(nameof(movingCollider));
            if (assignedTargetCapsule == null) throw new ArgumentNullException(nameof(assignedTargetCapsule));

            penetrationDepth = 0f;
            if (!IsEnabled(assignedTargetCapsule)) return M2AttackCapsulePoseResult.Clear;
            if (!Physics.ComputePenetration(movingCollider, proposedPosition, proposedRotation,
                    assignedTargetCapsule, assignedTargetCapsule.transform.position,
                    assignedTargetCapsule.transform.rotation, out _, out penetrationDepth) || penetrationDepth <= 0f)
            {
                penetrationDepth = 0f;
                return M2AttackCapsulePoseResult.Clear;
            }
            return M2AttackCapsulePoseResult.TargetCapsuleReached;
        }

        public static int CountPositivePenetrations(Collider movingCollider, IReadOnlyList<Collider> attackCapsules,
            Vector3 proposedPosition, Quaternion proposedRotation, out Collider firstOverlappingCapsule)
        {
            if (movingCollider == null) throw new ArgumentNullException(nameof(movingCollider));
            if (attackCapsules == null) throw new ArgumentNullException(nameof(attackCapsules));

            int overlappingCapsuleCount = 0;
            firstOverlappingCapsule = null;
            for (int index = 0; index < attackCapsules.Count; index++)
            {
                Collider attackCapsule = attackCapsules[index];
                if (!IsEnabled(attackCapsule)) continue;
                if (!Physics.ComputePenetration(movingCollider, proposedPosition, proposedRotation,
                        attackCapsule, attackCapsule.transform.position, attackCapsule.transform.rotation,
                        out _, out float depth) || depth <= 0f)
                    continue;

                overlappingCapsuleCount++;
                if (firstOverlappingCapsule == null) firstOverlappingCapsule = attackCapsule;
            }
            return overlappingCapsuleCount;
        }

        /// <summary>
        /// Measures signed surface separation for two CapsuleColliders. A positive result is a
        /// measured surface gap, zero is contact, and a negative result indicates intersecting
        /// capsules. No clearance threshold or epsilon is applied.
        /// </summary>
        public static bool TryMeasureSurfaceGap(CapsuleCollider movingCapsule, Vector3 movingPosition,
            Quaternion movingRotation, CapsuleCollider targetCapsule, out float signedGap)
        {
            signedGap = 0f;
            if (movingCapsule == null || targetCapsule == null) return false;
            if (!TryGetCapsuleGeometry(movingCapsule, movingPosition, movingRotation,
                    out Vector3 movingStart, out Vector3 movingEnd, out float movingRadius) ||
                !TryGetCapsuleGeometry(targetCapsule, targetCapsule.transform.position,
                    targetCapsule.transform.rotation, out Vector3 targetStart, out Vector3 targetEnd,
                    out float targetRadius))
                return false;

            float centerlineDistanceSquared = SegmentDistanceSquared(movingStart, movingEnd, targetStart, targetEnd);
            signedGap = Mathf.Sqrt(centerlineDistanceSquared) - movingRadius - targetRadius;
            return IsFinite(signedGap);
        }

        static bool IsEnabled(Collider collider) => collider != null && collider.enabled && collider.gameObject.activeInHierarchy;

        static bool TryGetCapsuleGeometry(CapsuleCollider capsule, Vector3 position, Quaternion rotation,
            out Vector3 segmentStart, out Vector3 segmentEnd, out float radius)
        {
            segmentStart = Vector3.zero;
            segmentEnd = Vector3.zero;
            radius = 0f;
            Vector3 scale = capsule.transform.lossyScale;
            int direction = capsule.direction;
            float axialScale;
            float radialScale;
            Vector3 localAxis;
            switch (direction)
            {
                case 0:
                    axialScale = Mathf.Abs(scale.x);
                    radialScale = Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    localAxis = Vector3.right;
                    break;
                case 1:
                    axialScale = Mathf.Abs(scale.y);
                    radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    localAxis = Vector3.up;
                    break;
                case 2:
                    axialScale = Mathf.Abs(scale.z);
                    radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                    localAxis = Vector3.forward;
                    break;
                default:
                    return false;
            }

            radius = capsule.radius * radialScale;
            float halfSegment = Mathf.Max(0f, capsule.height * axialScale * 0.5f - radius);
            Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
            Vector3 axis = (rotation * localAxis).normalized;
            segmentStart = center - axis * halfSegment;
            segmentEnd = center + axis * halfSegment;
            return radius > 0f && IsFinite(segmentStart) && IsFinite(segmentEnd) && IsFinite(radius);
        }

        static float SegmentDistanceSquared(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1)
        {
            Vector3 u = p1 - p0;
            Vector3 v = q1 - q0;
            Vector3 w = p0 - q0;
            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);
            float s;
            float t;

            if (a <= Mathf.Epsilon && c <= Mathf.Epsilon)
            {
                s = 0f;
                t = 0f;
            }
            else if (a <= Mathf.Epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(e / c);
            }
            else if (c <= Mathf.Epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-d / a);
            }
            else
            {
                float denominator = a * c - b * b;
                s = denominator != 0f ? Mathf.Clamp01((b * e - c * d) / denominator) : 0f;
                t = (b * s + e) / c;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-d / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - d) / a);
                }
            }

            Vector3 closestDifference = w + s * u - t * v;
            return closestDifference.sqrMagnitude;
        }

        static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
