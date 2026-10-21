using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Telemetry
{
    public static class TelemetryEventNames
    {
        public const string DamageApplied = "DamageApplied";
        public const string ActorStateChanged = "ActorStateChanged";
        public const string ActorDeathCommitted = "ActorDeathCommitted";
        public const string InvariantViolation = "InvariantViolation";
        public const string TelemetryHealth = "TelemetryHealth";

        public static readonly string[] All =
        {
            DamageApplied,
            ActorStateChanged,
            ActorDeathCommitted,
            InvariantViolation,
            TelemetryHealth
        };
    }

    public enum TelemetryValueKind { String, Integer, Number, Boolean, Null }

    public readonly struct TelemetryValue
    {
        public TelemetryValueKind Kind { get; }
        public string Text { get; }

        private TelemetryValue(TelemetryValueKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public static TelemetryValue String(string value) =>
            new TelemetryValue(TelemetryValueKind.String, value ?? throw new ArgumentNullException(nameof(value)));
        public static TelemetryValue Integer(long value) =>
            new TelemetryValue(TelemetryValueKind.Integer, value.ToString(CultureInfo.InvariantCulture));
        public static TelemetryValue Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return new TelemetryValue(TelemetryValueKind.Number, value.ToString("R", CultureInfo.InvariantCulture));
        }
        public static TelemetryValue Boolean(bool value) =>
            new TelemetryValue(TelemetryValueKind.Boolean, value ? "true" : "false");
        public static TelemetryValue Null() => new TelemetryValue(TelemetryValueKind.Null, null);
    }

    public sealed class TelemetryPayload
    {
        private readonly SortedDictionary<string, TelemetryValue> fields =
            new SortedDictionary<string, TelemetryValue>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, TelemetryValue> Fields =>
            new ReadOnlyDictionary<string, TelemetryValue>(fields);

        public TelemetryPayload Add(string name, TelemetryValue value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Payload field name is required.", nameof(name));
            if (fields.ContainsKey(name)) throw new InvalidOperationException("Duplicate payload field: " + name);
            fields.Add(name, value);
            return this;
        }
    }

    public sealed class TelemetryContext
    {
        public Guid SessionId { get; }
        public RunId RunId { get; }
        public string BuildId { get; }
        public string RulesetHash { get; }
        public string ContentVersion { get; }
        public string Mode { get; }
        public string Difficulty { get; }
        public int? Night { get; }
        public RunPhase Phase { get; }

        public TelemetryContext(Guid sessionId, RunId runId, string buildId, string rulesetHash,
            string contentVersion, string mode, string difficulty, int? night, RunPhase phase)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("SessionId is required.", nameof(sessionId));
            if (!runId.IsValid) throw new ArgumentException("RunId is required.", nameof(runId));
            SessionId = sessionId;
            RunId = runId;
            BuildId = RequireToken(buildId, nameof(buildId));
            RulesetHash = RequireSha256(rulesetHash);
            ContentVersion = RequireToken(contentVersion, nameof(contentVersion));
            Mode = RequireToken(mode, nameof(mode));
            Difficulty = RequireToken(difficulty, nameof(difficulty));
            if (night.HasValue && night.Value < 0) throw new ArgumentOutOfRangeException(nameof(night));
            Night = night;
            Phase = phase;
        }

        private static string RequireToken(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\\', '/', '\r', '\n' }) >= 0)
                throw new ArgumentException(field + " must be a non-path token.", field);
            return value;
        }

        private static string RequireSha256(string value)
        {
            if (value == null || value.Length != 64) throw new ArgumentException("RulesetHash must be lowercase SHA-256.", nameof(value));
            for (int i = 0; i < value.Length; i++)
                if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f')))
                    throw new ArgumentException("RulesetHash must be lowercase SHA-256.", nameof(value));
            return value;
        }
    }

    public sealed class TelemetryEventEnvelope
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; }
        public Guid EventId { get; }
        public Guid SessionId { get; }
        public RunId RunId { get; }
        public string BuildId { get; }
        public string RulesetHash { get; }
        public string ContentVersion { get; }
        public string Mode { get; }
        public string Difficulty { get; }
        public ulong EventSequence { get; }
        public int SimulationTick { get; }
        public int? Night { get; }
        public RunPhase Phase { get; }
        public string EventName { get; }
        public TelemetryPayload Payload { get; }

        public TelemetryEventEnvelope(Guid eventId, TelemetryContext context, ulong eventSequence,
            int simulationTick, string eventName, TelemetryPayload payload)
        {
            if (eventId == Guid.Empty) throw new ArgumentException("EventId is required.", nameof(eventId));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (eventSequence == 0) throw new ArgumentOutOfRangeException(nameof(eventSequence));
            if (simulationTick < 0) throw new ArgumentOutOfRangeException(nameof(simulationTick));
            EventId = eventId;
            SchemaVersion = CurrentSchemaVersion;
            SessionId = context.SessionId;
            RunId = context.RunId;
            BuildId = context.BuildId;
            RulesetHash = context.RulesetHash;
            ContentVersion = context.ContentVersion;
            Mode = context.Mode;
            Difficulty = context.Difficulty;
            EventSequence = eventSequence;
            SimulationTick = simulationTick;
            Night = context.Night;
            Phase = context.Phase;
            EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }

    public interface ITelemetryIdentitySource
    {
        Guid NextEventId();
    }

    public sealed class SystemGuidTelemetryIdentitySource : ITelemetryIdentitySource
    {
        public Guid NextEventId() => Guid.NewGuid();
    }
}
