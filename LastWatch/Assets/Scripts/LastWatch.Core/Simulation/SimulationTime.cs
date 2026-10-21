using System;

namespace LastWatch.Core.Simulation
{
    public static class SimulationTime
    {
        public const int TickRate = 60;
        public const double SecondsPerTick = 1.0 / TickRate;

        public static int MinimumSecondsToTicks(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double ticks = Math.Ceiling(seconds * TickRate);
            if (ticks > int.MaxValue) throw new OverflowException("Tick duration exceeds Int32 capacity.");
            return (int)ticks;
        }
    }

    public sealed class EventSequenceAllocator
    {
        public ulong NextValue { get; private set; }
        public EventSequenceAllocator(ulong nextValue = 1)
        {
            if (nextValue == 0) throw new ArgumentOutOfRangeException(nameof(nextValue));
            NextValue = nextValue;
        }

        public ulong Issue()
        {
            ulong issued = NextValue;
            if (issued == ulong.MaxValue) throw new OverflowException("EventSequence exhausted.");
            NextValue = issued + 1;
            return issued;
        }
    }

    [Serializable]
    public readonly struct RngStreamState : IEquatable<RngStreamState>
    {
        public ulong State0 { get; }
        public ulong State1 { get; }
        public RngStreamState(ulong state0, ulong state1)
        {
            if ((state0 | state1) == 0) throw new ArgumentException("RNG state cannot be all zero.");
            State0 = state0;
            State1 = state1;
        }
        public bool Equals(RngStreamState other) => State0 == other.State0 && State1 == other.State1;
        public override bool Equals(object obj) => obj is RngStreamState other && Equals(other);
        public override int GetHashCode() => (State0.GetHashCode() * 397) ^ State1.GetHashCode();
    }
}
