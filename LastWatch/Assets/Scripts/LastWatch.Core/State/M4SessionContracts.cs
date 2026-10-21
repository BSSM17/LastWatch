using System;
using System.Globalization;
using LastWatch.Core.Identity;

namespace LastWatch.Core.State
{
    public enum M4GameMode { Story = 0 }
    public enum M4SessionStage { New = 0, Prepared = 1, Defense = 2, NightCompleted = 3, Frozen = 4 }
    public enum M4BindingRole { Player = 0, Partner = 1, Barrier = 2, Support = 3, NightBrace = 4 }
    public enum M4CursorNamespace { RunFacts, DirectorFacts, ProjectionFacts, PartnerData40, EventSequence, SaveGeneration }

    public readonly struct M4Cursor
    {
        public M4CursorNamespace Namespace { get; }
        public ulong Value { get; }
        public M4Cursor(M4CursorNamespace ns, ulong value)
        { if (!Enum.IsDefined(typeof(M4CursorNamespace), ns)) throw new ArgumentOutOfRangeException(nameof(ns)); Namespace = ns; Value = value; }
        public ulong Require(M4CursorNamespace ns)
        { if (Namespace != ns) throw new ArgumentException("Cursor namespace mismatch."); return Value; }
    }

    public readonly struct EnterDefenseKey : IEquatable<EnterDefenseKey>
    {
        public RunId RunId { get; }
        public M4GameMode GameMode { get; }
        public int NightOrdinal { get; }
        public EnterDefenseKey(RunId runId, M4GameMode mode, int ordinal)
        { if (!runId.IsValid || mode != M4GameMode.Story || ordinal < 1 || ordinal > 3) throw new ArgumentException("Invalid bounded entry key."); RunId = runId; GameMode = mode; NightOrdinal = ordinal; }
        public bool Equals(EnterDefenseKey other) => RunId.Equals(other.RunId) && GameMode == other.GameMode && NightOrdinal == other.NightOrdinal;
        public override bool Equals(object value) => value is EnterDefenseKey && Equals((EnterDefenseKey)value);
        public override int GetHashCode() => RunId.GetHashCode() ^ (NightOrdinal * 397);
        public override string ToString() => RunId + "|Story|" + NightOrdinal.ToString(CultureInfo.InvariantCulture);
    }
}
