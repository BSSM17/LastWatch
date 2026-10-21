using System;

namespace LastWatch.Core.Configuration
{
    public interface IReadOnlyRunConfiguration
    {
        string Version { get; }
        int TickRate { get; }
        RunContextDefinition RunContext { get; }
    }

    public sealed class RunConfiguration : IReadOnlyRunConfiguration
    {
        public string Version { get; }
        public int TickRate => Simulation.SimulationTime.TickRate;
        public RunContextDefinition RunContext { get; }

        public RunConfiguration(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Config version is required.", nameof(version));
            Version = version;
            RunContext = RunContextDefinition.PreMilestoneUnavailable;
        }
    }
}
