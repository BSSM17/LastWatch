using System;
using System.Collections.Generic;

namespace LastWatch.Core.Terminal
{
    public readonly struct TerminalAfterfallRouteId : IEquatable<TerminalAfterfallRouteId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);
        public TerminalAfterfallRouteId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Route ID is required.", nameof(value));
            Value = value;
        }
        public bool Equals(TerminalAfterfallRouteId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TerminalAfterfallRouteId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value;
    }

    public interface ITerminalAfterfallRouteCatalog
    {
        IReadOnlyList<TerminalAfterfallRouteId> RouteIds { get; }
    }

    // Deliberately empty until the M6 topology task provides approved route evidence.
    public sealed class EmptyTerminalAfterfallRouteCatalog : ITerminalAfterfallRouteCatalog
    {
        private static readonly TerminalAfterfallRouteId[] Empty = Array.Empty<TerminalAfterfallRouteId>();
        public IReadOnlyList<TerminalAfterfallRouteId> RouteIds => Empty;
    }
}
