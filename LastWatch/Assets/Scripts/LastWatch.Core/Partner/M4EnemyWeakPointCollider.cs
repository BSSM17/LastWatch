using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Author the collider and its local Transform in a prefab; no extra anchor or coordinates.
    [DisallowMultipleComponent]
    public sealed class M4EnemyWeakPointCollider : MonoBehaviour
    {
        [SerializeField, Tooltip("A non-trigger Box, Sphere or Capsule Collider on this GameObject.")]
        Collider hitCollider;

        public Collider HitCollider => hitCollider;
        public ActiveEnemyActor Owner => GetComponentInParent<ActiveEnemyActor>();
        public bool IsConfigured => hitCollider != null && hitCollider.transform == transform &&
            !hitCollider.isTrigger && (hitCollider is BoxCollider || hitCollider is SphereCollider ||
            hitCollider is CapsuleCollider) && GetComponentsInChildren<Collider>(true).Length == 1;
        public bool IsAvailable => IsConfigured && isActiveAndEnabled && hitCollider.enabled && Owner != null;

        void Reset()
        {
            hitCollider = GetComponent<Collider>();
        }

        public bool TryGetWorldCenter(out Vector3 position)
        {
            position = default;
            if (!IsConfigured) return false;
            if (hitCollider is BoxCollider box) position = box.transform.TransformPoint(box.center);
            else if (hitCollider is SphereCollider sphere) position = sphere.transform.TransformPoint(sphere.center);
            else if (hitCollider is CapsuleCollider capsule) position = capsule.transform.TransformPoint(capsule.center);
            else return false;
            return true;
        }

        internal static ActiveEnemyActor ResolveHitActor(Collider collider)
        {
            if (collider == null) return null;
            ActiveEnemyActor direct = collider.GetComponent<ActiveEnemyActor>();
            if (direct != null) return direct;
            M4EnemyWeakPointCollider weakPoint = collider.GetComponent<M4EnemyWeakPointCollider>();
            return weakPoint != null && weakPoint.HitCollider == collider && weakPoint.IsAvailable
                ? weakPoint.Owner : null;
        }
    }
}
