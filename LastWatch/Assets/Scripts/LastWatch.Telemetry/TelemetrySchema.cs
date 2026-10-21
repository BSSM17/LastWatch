using System;
using System.Collections.Generic;

namespace LastWatch.Telemetry
{
    public sealed class TelemetryFieldRule
    {
        public string Name { get; }
        public TelemetryValueKind Kind { get; }
        public bool Nullable { get; }
        public ISet<string> AllowedValues { get; }

        public TelemetryFieldRule(string name, TelemetryValueKind kind, bool nullable = false,
            params string[] allowedValues)
        {
            Name = name;
            Kind = kind;
            Nullable = nullable;
            AllowedValues = new HashSet<string>(allowedValues ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
    }

    public sealed class TelemetryEventRule
    {
        private readonly Dictionary<string, TelemetryFieldRule> fields;
        public string EventName { get; }
        public IReadOnlyDictionary<string, TelemetryFieldRule> Fields => fields;

        public TelemetryEventRule(string eventName, params TelemetryFieldRule[] fieldRules)
        {
            EventName = eventName;
            fields = new Dictionary<string, TelemetryFieldRule>(StringComparer.Ordinal);
            foreach (TelemetryFieldRule rule in fieldRules) fields.Add(rule.Name, rule);
        }
    }

    public static class TelemetrySchemaRegistry
    {
        private static readonly Dictionary<string, TelemetryEventRule> Rules = Build();
        public static IReadOnlyDictionary<string, TelemetryEventRule> Events => Rules;

        public static bool TryGet(string eventName, out TelemetryEventRule rule) => Rules.TryGetValue(eventName, out rule);

        private static Dictionary<string, TelemetryEventRule> Build()
        {
            TelemetryFieldRule S(string name, params string[] values) =>
                new TelemetryFieldRule(name, TelemetryValueKind.String, false, values);
            TelemetryFieldRule SN(string name, params string[] values) =>
                new TelemetryFieldRule(name, TelemetryValueKind.String, true, values);
            var result = new Dictionary<string, TelemetryEventRule>(StringComparer.Ordinal)
            {
                [TelemetryEventNames.DamageApplied] = new TelemetryEventRule(TelemetryEventNames.DamageApplied,
                    S("targetEntityId"), S("targetKind", "Player", "Partner", "Enemy", "Structure"),
                    S("damageKind", "Ballistic", "Melee", "Environment", "CollapseImpact", "AfterfallTimeoutHazard"),
                    new TelemetryFieldRule("amount", TelemetryValueKind.Number),
                    new TelemetryFieldRule("hitPointsBefore", TelemetryValueKind.Number),
                    new TelemetryFieldRule("hitPointsAfter", TelemetryValueKind.Number),
                    new TelemetryFieldRule("becameResolved", TelemetryValueKind.Boolean),
                    SN("sourceEntityId"), SN("instigatorEntityId"), SN("fireSequenceId")),
                [TelemetryEventNames.ActorStateChanged] = new TelemetryEventRule(TelemetryEventNames.ActorStateChanged,
                    S("actorId"), S("actorKind", "Player", "Partner"),
                    S("previousLifeState", "Alive", "Healthy", "Injured", "Downed", "Stabilized", "Dead"),
                    S("currentLifeState", "Alive", "Healthy", "Injured", "Downed", "Stabilized", "Dead")),
                [TelemetryEventNames.ActorDeathCommitted] = new TelemetryEventRule(TelemetryEventNames.ActorDeathCommitted,
                    S("actorId"), S("actorKind", "Player", "Partner"),
                    S("previousLifeState", "Alive", "Healthy", "Injured", "Downed", "Stabilized"),
                    new TelemetryFieldRule("deathTick", TelemetryValueKind.Integer),
                    S("deathSequence"),
                    S("deathCause", "Ballistic", "Melee", "Environment", "CollapseImpact", "AfterfallTimeoutHazard"),
                    S("lethalDamageEventSequence"),
                    SN("lethalSourceActorKind", "Player", "Partner", "Enemy", "Structure"),
                    SN("lethalSourceEntityId")),
                [TelemetryEventNames.InvariantViolation] = new TelemetryEventRule(TelemetryEventNames.InvariantViolation,
                    S("invariantId"), S("classification", "CoreFactMismatch", "SchemaRejected", "OrderingViolation")),
                [TelemetryEventNames.TelemetryHealth] = new TelemetryEventRule(TelemetryEventNames.TelemetryHealth,
                    S("condition", "Healthy", "SinkException", "CapacityExceeded", "ValidationRejected"),
                    new TelemetryFieldRule("droppedEventCount", TelemetryValueKind.Integer))
            };
            return result;
        }
    }

    public readonly struct TelemetryValidationResult
    {
        public bool IsValid { get; }
        public string ErrorCode { get; }
        public TelemetryValidationResult(bool isValid, string errorCode) { IsValid = isValid; ErrorCode = errorCode; }
        public static TelemetryValidationResult Valid => new TelemetryValidationResult(true, null);
        public static TelemetryValidationResult Invalid(string code) => new TelemetryValidationResult(false, code);
    }

    public sealed class TelemetrySchemaValidator
    {
        private static readonly HashSet<string> ForbiddenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "username", "userName", "accountId", "deviceId", "absolutePath", "filePath", "freeText" };
        private static readonly HashSet<string> StableIdFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "actorId", "deathSequence", "fireSequenceId", "instigatorEntityId",
            "lethalDamageEventSequence", "lethalSourceEntityId", "sourceEntityId", "targetEntityId"
        };
        private static readonly HashSet<string> NonNegativeIntegerFields = new HashSet<string>(StringComparer.Ordinal)
        { "deathTick", "droppedEventCount" };

        public TelemetryValidationResult Validate(TelemetryEventEnvelope value)
        {
            if (value == null) return TelemetryValidationResult.Invalid("ENVELOPE_NULL");
            if (value.SchemaVersion != TelemetryEventEnvelope.CurrentSchemaVersion)
                return TelemetryValidationResult.Invalid("SCHEMA_VERSION_UNKNOWN");
            if (!TelemetrySchemaRegistry.TryGet(value.EventName, out TelemetryEventRule rule))
                return TelemetryValidationResult.Invalid("EVENT_UNKNOWN");
            if (value.Payload.Fields.Count != rule.Fields.Count)
                return TelemetryValidationResult.Invalid("PAYLOAD_FIELD_SET_MISMATCH");

            foreach (KeyValuePair<string, TelemetryValue> field in value.Payload.Fields)
            {
                if (ForbiddenNames.Contains(field.Key)) return TelemetryValidationResult.Invalid("PRIVACY_FIELD_FORBIDDEN");
                if (!rule.Fields.TryGetValue(field.Key, out TelemetryFieldRule fieldRule))
                    return TelemetryValidationResult.Invalid("FIELD_UNKNOWN");
                if (field.Value.Kind == TelemetryValueKind.Null)
                {
                    if (!fieldRule.Nullable) return TelemetryValidationResult.Invalid("FIELD_NULL_NOT_ALLOWED");
                    continue;
                }
                if (field.Value.Kind != fieldRule.Kind) return TelemetryValidationResult.Invalid("FIELD_TYPE_MISMATCH");
                if (fieldRule.AllowedValues.Count > 0 && !fieldRule.AllowedValues.Contains(field.Value.Text))
                    return TelemetryValidationResult.Invalid("ENUM_UNKNOWN");
                if (field.Value.Kind == TelemetryValueKind.String && ContainsForbiddenPath(field.Value.Text))
                    return TelemetryValidationResult.Invalid("PATH_VALUE_FORBIDDEN");
                if (field.Value.Kind == TelemetryValueKind.String && StableIdFields.Contains(field.Key) &&
                    !IsStableHexId(field.Value.Text))
                    return TelemetryValidationResult.Invalid("STABLE_ID_INVALID");
                if (field.Value.Kind == TelemetryValueKind.Integer && NonNegativeIntegerFields.Contains(field.Key) &&
                    (!long.TryParse(field.Value.Text, out long integer) || integer < 0))
                    return TelemetryValidationResult.Invalid("INTEGER_RANGE_INVALID");
            }
            return TelemetryValidationResult.Valid;
        }

        private static bool IsStableHexId(string value)
        {
            if (value == null || value.Length != 16) return false;
            for (int i = 0; i < value.Length; i++)
                if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f')))
                    return false;
            return true;
        }

        private static bool ContainsForbiddenPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf("\\", StringComparison.Ordinal) >= 0 ||
                   value.StartsWith("/", StringComparison.Ordinal) ||
                   (value.Length > 2 && char.IsLetter(value[0]) && value[1] == ':');
        }
    }
}
