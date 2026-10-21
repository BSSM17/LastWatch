using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LastWatch.Core.Combat;
using LastWatch.Core.Space;

namespace LastWatch.Core.Partner
{
    public enum M4ReplayShotDiagnosticOutcome
    {
        NotAttempted,
        NoHit,
        WorldBlock,
        ResolvedEnemyBlock,
        ActiveEnemyHit
    }

    public enum M4ReplayShotNoRaycastReason
    {
        None,
        PlayerDead,
        Reloading,
        ShotCadence,
        EmptyMagazine,
        ResolverDidNotRun
    }

    public readonly struct M4ReplayShotDiagnostic
    {
        public string CaptureRunId { get; }
        public string InputPlanId { get; }
        // Zero-based plan position; -1 means this diagnostic came from tape replay.
        public int InputPlanCommandIndex { get; }
        public int ReplayConsumedTick { get; }
        public int RunFixedTick { get; }
        public string WeaponId { get; }
        public bool RaycastAttempted { get; }
        public M4ReplayShotDiagnosticOutcome Outcome { get; }
        public M4ReplayShotNoRaycastReason NoRaycastReason { get; }
        public bool HasColliderHit { get; }
        public string ColliderName { get; }
        public float HitDistance { get; }
        public bool HasGrayboxTag { get; }
        public M2GrayboxColliderKind ColliderKind { get; }
        public bool HasFace { get; }
        public FaceId Face { get; }
        public bool HasActiveEnemyActor { get; }
        public bool ActorRegistryKeyPresent { get; }
        public bool ActorInstanceMatchesRegistry { get; }
        public DamageApplyStatus? DamageStatus { get; }
        public bool DamageBecameResolved { get; }

        public bool HasDamageReceipt => DamageStatus.HasValue;

        internal M4ReplayShotDiagnostic(string captureRunId, string inputPlanId, int inputPlanCommandIndex,
            int replayConsumedTick, int runFixedTick, string weaponId,
            bool raycastAttempted, M4ReplayShotDiagnosticOutcome outcome,
            M4ReplayShotNoRaycastReason noRaycastReason, bool hasColliderHit, string colliderName,
            float hitDistance, bool hasGrayboxTag, M2GrayboxColliderKind colliderKind,
            bool hasFace, FaceId face, bool hasActiveEnemyActor, bool actorRegistryKeyPresent,
            bool actorInstanceMatchesRegistry, DamageApplyStatus? damageStatus, bool damageBecameResolved)
        {
            CaptureRunId = captureRunId ?? string.Empty;
            InputPlanId = inputPlanId ?? string.Empty;
            InputPlanCommandIndex = inputPlanCommandIndex;
            ReplayConsumedTick = replayConsumedTick;
            RunFixedTick = runFixedTick;
            WeaponId = weaponId ?? string.Empty;
            RaycastAttempted = raycastAttempted;
            Outcome = outcome;
            NoRaycastReason = noRaycastReason;
            HasColliderHit = hasColliderHit;
            ColliderName = colliderName ?? string.Empty;
            HitDistance = hitDistance;
            HasGrayboxTag = hasGrayboxTag;
            ColliderKind = colliderKind;
            HasFace = hasFace;
            Face = face;
            HasActiveEnemyActor = hasActiveEnemyActor;
            ActorRegistryKeyPresent = actorRegistryKeyPresent;
            ActorInstanceMatchesRegistry = actorInstanceMatchesRegistry;
            DamageStatus = damageStatus;
            DamageBecameResolved = damageBecameResolved;
        }
    }

    public readonly struct M4ReplayShotOutcomeCount
    {
        public M4ReplayShotDiagnosticOutcome Outcome { get; }
        public int Count { get; }
        public IReadOnlyList<M4ReplayShotDiagnostic> Examples { get; }
        public bool ExamplesTruncated { get; }

        internal M4ReplayShotOutcomeCount(M4ReplayShotDiagnosticOutcome outcome, int count,
            IReadOnlyList<M4ReplayShotDiagnostic> examples, bool examplesTruncated)
        {
            Outcome = outcome;
            Count = count;
            Examples = examples;
            ExamplesTruncated = examplesTruncated;
        }
    }

    public readonly struct M4ReplayShotColliderKindCount
    {
        public bool HasGrayboxTag { get; }
        public M2GrayboxColliderKind ColliderKind { get; }
        public int Count { get; }
        public IReadOnlyList<M4ReplayShotDiagnostic> Examples { get; }
        public bool ExamplesTruncated { get; }

        internal M4ReplayShotColliderKindCount(bool hasGrayboxTag, M2GrayboxColliderKind colliderKind,
            int count, IReadOnlyList<M4ReplayShotDiagnostic> examples, bool examplesTruncated)
        {
            HasGrayboxTag = hasGrayboxTag;
            ColliderKind = colliderKind;
            Count = count;
            Examples = examples;
            ExamplesTruncated = examplesTruncated;
        }
    }

    public readonly struct M4ReplayShotColliderNameCount
    {
        public string ColliderName { get; }
        public bool IsOverflowBucket { get; }
        public int Count { get; }
        public IReadOnlyList<M4ReplayShotDiagnostic> Examples { get; }
        public bool ExamplesTruncated { get; }

        internal M4ReplayShotColliderNameCount(string colliderName, bool isOverflowBucket, int count,
            IReadOnlyList<M4ReplayShotDiagnostic> examples, bool examplesTruncated)
        {
            ColliderName = colliderName ?? string.Empty;
            IsOverflowBucket = isOverflowBucket;
            Count = count;
            Examples = examples;
            ExamplesTruncated = examplesTruncated;
        }
    }

    public readonly struct M4ReplayShotFaceCount
    {
        public bool HasFace { get; }
        public FaceId Face { get; }
        public int Count { get; }
        public IReadOnlyList<M4ReplayShotDiagnostic> Examples { get; }
        public bool ExamplesTruncated { get; }

        internal M4ReplayShotFaceCount(bool hasFace, FaceId face, int count,
            IReadOnlyList<M4ReplayShotDiagnostic> examples, bool examplesTruncated)
        {
            HasFace = hasFace;
            Face = face;
            Count = count;
            Examples = examples;
            ExamplesTruncated = examplesTruncated;
        }
    }

    public sealed class M4ReplayShotDiagnosticSnapshot
    {
        public string CaptureRunId { get; }
        public string InputPlanId { get; }
        public bool IsInputPlanCapture { get; }
        public int ReplayFireCommandCount { get; }
        public int FireCommandCount => ReplayFireCommandCount;
        public int RaycastAttemptCount { get; }
        public bool ColliderNameBucketsTruncated { get; }
        public bool ExamplesTruncated { get; }
        public IReadOnlyList<M4ReplayShotOutcomeCount> OutcomeCounts { get; }
        public IReadOnlyList<M4ReplayShotColliderKindCount> ColliderKindCounts { get; }
        public IReadOnlyList<M4ReplayShotColliderNameCount> ColliderNameCounts { get; }
        public IReadOnlyList<M4ReplayShotFaceCount> FaceCounts { get; }

        internal M4ReplayShotDiagnosticSnapshot(string captureRunId, string inputPlanId, bool isInputPlanCapture,
            int replayFireCommandCount, int raycastAttemptCount,
            bool colliderNameBucketsTruncated, bool examplesTruncated,
            IReadOnlyList<M4ReplayShotOutcomeCount> outcomeCounts,
            IReadOnlyList<M4ReplayShotColliderKindCount> colliderKindCounts,
            IReadOnlyList<M4ReplayShotColliderNameCount> colliderNameCounts,
            IReadOnlyList<M4ReplayShotFaceCount> faceCounts)
        {
            CaptureRunId = captureRunId ?? string.Empty;
            InputPlanId = inputPlanId ?? string.Empty;
            IsInputPlanCapture = isInputPlanCapture;
            ReplayFireCommandCount = replayFireCommandCount;
            RaycastAttemptCount = raycastAttemptCount;
            ColliderNameBucketsTruncated = colliderNameBucketsTruncated;
            ExamplesTruncated = examplesTruncated;
            OutcomeCounts = outcomeCounts;
            ColliderKindCounts = colliderKindCounts;
            ColliderNameCounts = colliderNameCounts;
            FaceCounts = faceCounts;
        }
    }

    internal sealed class M4ReplayShotDiagnosticAccumulator
    {
        const int ExamplesPerBucket = 2;
        const int MaximumColliderNameBuckets = 256;
        const string OverflowName = "<collider-name-bucket-limit>";

        readonly Dictionary<M4ReplayShotDiagnosticOutcome, MutableCount> outcomes =
            new Dictionary<M4ReplayShotDiagnosticOutcome, MutableCount>();
        readonly Dictionary<ColliderKindKey, MutableCount> colliderKinds =
            new Dictionary<ColliderKindKey, MutableCount>();
        readonly Dictionary<ColliderNameKey, MutableCount> colliderNames =
            new Dictionary<ColliderNameKey, MutableCount>();
        readonly Dictionary<FaceKey, MutableCount> faces = new Dictionary<FaceKey, MutableCount>();
        int replayFireCommandCount;
        int raycastAttemptCount;
        int storedColliderNameBucketCount;
        bool colliderNameBucketsTruncated;

        public void Reset()
        {
            outcomes.Clear();
            colliderKinds.Clear();
            colliderNames.Clear();
            faces.Clear();
            replayFireCommandCount = 0;
            raycastAttemptCount = 0;
            storedColliderNameBucketCount = 0;
            colliderNameBucketsTruncated = false;
        }

        public void Record(M4ReplayShotDiagnostic diagnostic)
        {
            replayFireCommandCount++;
            if (diagnostic.RaycastAttempted) raycastAttemptCount++;
            Add(outcomes, diagnostic.Outcome, diagnostic);
            if (!diagnostic.HasColliderHit) return;

            Add(colliderKinds, new ColliderKindKey(diagnostic.HasGrayboxTag, diagnostic.ColliderKind), diagnostic);
            ColliderNameKey nameKey = FindOrCreateColliderNameKey(diagnostic.ColliderName);
            Add(colliderNames, nameKey, diagnostic);
            Add(faces, new FaceKey(diagnostic.HasFace, diagnostic.Face), diagnostic);
        }

        public M4ReplayShotDiagnosticSnapshot CreateSnapshot(string captureRunId, string inputPlanId, bool isInputPlanCapture)
        {
            var outcomeRows = new List<M4ReplayShotOutcomeCount>();
            foreach (KeyValuePair<M4ReplayShotDiagnosticOutcome, MutableCount> pair in outcomes)
                outcomeRows.Add(new M4ReplayShotOutcomeCount(pair.Key, pair.Value.Count,
                    pair.Value.CopyExamples(), pair.Value.ExamplesTruncated));
            outcomeRows.Sort((left, right) => left.Outcome.CompareTo(right.Outcome));

            var kindRows = new List<M4ReplayShotColliderKindCount>();
            foreach (KeyValuePair<ColliderKindKey, MutableCount> pair in colliderKinds)
                kindRows.Add(new M4ReplayShotColliderKindCount(pair.Key.HasGrayboxTag, pair.Key.Kind,
                    pair.Value.Count, pair.Value.CopyExamples(), pair.Value.ExamplesTruncated));
            kindRows.Sort((left, right) =>
            {
                int tagOrder = left.HasGrayboxTag.CompareTo(right.HasGrayboxTag);
                return tagOrder != 0 ? tagOrder : left.ColliderKind.CompareTo(right.ColliderKind);
            });

            var nameRows = new List<M4ReplayShotColliderNameCount>();
            foreach (KeyValuePair<ColliderNameKey, MutableCount> pair in colliderNames)
                nameRows.Add(new M4ReplayShotColliderNameCount(pair.Key.Overflow ? OverflowName : pair.Key.Name,
                    pair.Key.Overflow, pair.Value.Count, pair.Value.CopyExamples(), pair.Value.ExamplesTruncated));
            nameRows.Sort((left, right) =>
            {
                int overflowOrder = left.IsOverflowBucket.CompareTo(right.IsOverflowBucket);
                return overflowOrder != 0 ? overflowOrder : StringComparer.Ordinal.Compare(left.ColliderName, right.ColliderName);
            });

            var faceRows = new List<M4ReplayShotFaceCount>();
            foreach (KeyValuePair<FaceKey, MutableCount> pair in faces)
                faceRows.Add(new M4ReplayShotFaceCount(pair.Key.HasFace, pair.Key.Face, pair.Value.Count,
                    pair.Value.CopyExamples(), pair.Value.ExamplesTruncated));
            faceRows.Sort((left, right) =>
            {
                int hasFaceOrder = left.HasFace.CompareTo(right.HasFace);
                return hasFaceOrder != 0 ? hasFaceOrder : left.Face.CompareTo(right.Face);
            });

            bool examplesTruncated = HasTruncatedExamples(outcomes) || HasTruncatedExamples(colliderKinds) ||
                                     HasTruncatedExamples(colliderNames) || HasTruncatedExamples(faces);
            return new M4ReplayShotDiagnosticSnapshot(captureRunId, inputPlanId, isInputPlanCapture,
                replayFireCommandCount, raycastAttemptCount,
                colliderNameBucketsTruncated, examplesTruncated,
                ReadOnly(outcomeRows), ReadOnly(kindRows), ReadOnly(nameRows), ReadOnly(faceRows));
        }

        ColliderNameKey FindOrCreateColliderNameKey(string colliderName)
        {
            var requestedKey = new ColliderNameKey(colliderName ?? string.Empty, false);
            if (colliderNames.ContainsKey(requestedKey)) return requestedKey;
            if (storedColliderNameBucketCount < MaximumColliderNameBuckets)
            {
                storedColliderNameBucketCount++;
                return requestedKey;
            }
            colliderNameBucketsTruncated = true;
            return new ColliderNameKey(string.Empty, true);
        }

        static void Add<TKey>(Dictionary<TKey, MutableCount> counts, TKey key, M4ReplayShotDiagnostic diagnostic)
        {
            if (!counts.TryGetValue(key, out MutableCount count))
            {
                count = new MutableCount();
                counts.Add(key, count);
            }
            count.Add(diagnostic, ExamplesPerBucket);
        }

        static bool HasTruncatedExamples<TKey>(Dictionary<TKey, MutableCount> counts)
        {
            foreach (MutableCount count in counts.Values)
                if (count.ExamplesTruncated) return true;
            return false;
        }

        static IReadOnlyList<T> ReadOnly<T>(List<T> values) => Array.AsReadOnly(values.ToArray());

        sealed class MutableCount
        {
            readonly List<M4ReplayShotDiagnostic> examples = new List<M4ReplayShotDiagnostic>(ExamplesPerBucket);
            public int Count { get; private set; }
            public bool ExamplesTruncated { get; private set; }

            public void Add(M4ReplayShotDiagnostic diagnostic, int maximumExamples)
            {
                Count++;
                if (examples.Count < maximumExamples) examples.Add(diagnostic);
                else ExamplesTruncated = true;
            }

            public IReadOnlyList<M4ReplayShotDiagnostic> CopyExamples() => Array.AsReadOnly(examples.ToArray());
        }

        readonly struct ColliderKindKey : IEquatable<ColliderKindKey>
        {
            public bool HasGrayboxTag { get; }
            public M2GrayboxColliderKind Kind { get; }

            public ColliderKindKey(bool hasGrayboxTag, M2GrayboxColliderKind kind)
            {
                HasGrayboxTag = hasGrayboxTag;
                Kind = kind;
            }

            public bool Equals(ColliderKindKey other) => HasGrayboxTag == other.HasGrayboxTag && Kind == other.Kind;
            public override bool Equals(object value) => value is ColliderKindKey && Equals((ColliderKindKey)value);
            public override int GetHashCode() => ((int)Kind * 397) ^ (HasGrayboxTag ? 1 : 0);
        }

        readonly struct ColliderNameKey : IEquatable<ColliderNameKey>
        {
            public string Name { get; }
            public bool Overflow { get; }

            public ColliderNameKey(string name, bool overflow)
            {
                Name = name ?? string.Empty;
                Overflow = overflow;
            }

            public bool Equals(ColliderNameKey other) => Overflow == other.Overflow &&
                string.Equals(Name, other.Name, StringComparison.Ordinal);
            public override bool Equals(object value) => value is ColliderNameKey && Equals((ColliderNameKey)value);
            public override int GetHashCode() => ((Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name)) * 397) ^
                (Overflow ? 1 : 0);
        }

        readonly struct FaceKey : IEquatable<FaceKey>
        {
            public bool HasFace { get; }
            public FaceId Face { get; }

            public FaceKey(bool hasFace, FaceId face)
            {
                HasFace = hasFace;
                Face = face;
            }

            public bool Equals(FaceKey other) => HasFace == other.HasFace && Face == other.Face;
            public override bool Equals(object value) => value is FaceKey && Equals((FaceKey)value);
            public override int GetHashCode() => ((int)Face * 397) ^ (HasFace ? 1 : 0);
        }
    }
}
