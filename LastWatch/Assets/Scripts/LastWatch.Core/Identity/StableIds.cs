using System;
using System.Globalization;

namespace LastWatch.Core.Identity
{
    internal static class StableIdText
    {
        internal static string Format(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

        internal static ulong Parse(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsed))
                throw new FormatException($"{fieldName} must be a hexadecimal unsigned 64-bit value.");
            return parsed;
        }
    }

    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public static readonly EntityId Invalid = default;
        public ulong Value { get; }
        public bool IsValid => Value != 0;
        public EntityId(ulong value) => Value = value;
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => StableIdText.Format(Value);
        public static EntityId Parse(string value) => new EntityId(StableIdText.Parse(value, nameof(EntityId)));
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }

    public readonly struct RunId : IEquatable<RunId>, IComparable<RunId>
    {
        public static readonly RunId Invalid = default;
        public ulong Value { get; }
        public bool IsValid => Value != 0;
        public RunId(ulong value) => Value = value;
        public int CompareTo(RunId other) => Value.CompareTo(other.Value);
        public bool Equals(RunId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is RunId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => StableIdText.Format(Value);
        public static RunId Parse(string value) => new RunId(StableIdText.Parse(value, nameof(RunId)));
        public static bool operator ==(RunId left, RunId right) => left.Equals(right);
        public static bool operator !=(RunId left, RunId right) => !left.Equals(right);
    }

    public readonly struct FireSequenceId : IEquatable<FireSequenceId>, IComparable<FireSequenceId>
    {
        public static readonly FireSequenceId Invalid = default;
        public ulong Value { get; }
        public bool IsValid => Value != 0;
        public FireSequenceId(ulong value) => Value = value;
        public int CompareTo(FireSequenceId other) => Value.CompareTo(other.Value);
        public bool Equals(FireSequenceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is FireSequenceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => StableIdText.Format(Value);
        public static FireSequenceId Parse(string value) => new FireSequenceId(StableIdText.Parse(value, nameof(FireSequenceId)));
        public static bool operator ==(FireSequenceId left, FireSequenceId right) => left.Equals(right);
        public static bool operator !=(FireSequenceId left, FireSequenceId right) => !left.Equals(right);
    }
}
