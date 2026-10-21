using System;
using System.Globalization;
using System.Text;

namespace LastWatch.Telemetry
{
    public static class DeterministicTelemetryJson
    {
        public static string Serialize(TelemetryEventEnvelope value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var text = new StringBuilder(512);
            text.Append('{');
            Property(text, "schemaVersion", value.SchemaVersion.ToString(CultureInfo.InvariantCulture), false, false);
            Property(text, "eventId", value.EventId.ToString("D"), true, true);
            Property(text, "sessionId", value.SessionId.ToString("D"), true, true);
            Property(text, "runId", value.RunId.ToString(), true, true);
            Property(text, "buildId", value.BuildId, true, true);
            Property(text, "rulesetHash", value.RulesetHash, true, true);
            Property(text, "contentVersion", value.ContentVersion, true, true);
            Property(text, "mode", value.Mode, true, true);
            Property(text, "difficulty", value.Difficulty, true, true);
            Property(text, "eventSequence", value.EventSequence.ToString(CultureInfo.InvariantCulture), false, true);
            Property(text, "simulationTick", value.SimulationTick.ToString(CultureInfo.InvariantCulture), false, true);
            Property(text, "night", value.Night.HasValue ? value.Night.Value.ToString(CultureInfo.InvariantCulture) : "null", false, true);
            Property(text, "phase", value.Phase.ToString(), true, true);
            Property(text, "eventName", value.EventName, true, true);
            text.Append(",\"payload\":{");
            bool comma = false;
            foreach (var field in value.Payload.Fields)
            {
                if (comma) text.Append(',');
                AppendQuoted(text, field.Key);
                text.Append(':');
                if (field.Value.Kind == TelemetryValueKind.String) AppendQuoted(text, field.Value.Text);
                else text.Append(field.Value.Kind == TelemetryValueKind.Null ? "null" : field.Value.Text);
                comma = true;
            }
            text.Append("}}");
            return text.ToString();
        }

        private static void Property(StringBuilder text, string name, string value, bool quoted, bool comma)
        {
            if (comma) text.Append(',');
            AppendQuoted(text, name);
            text.Append(':');
            if (quoted) AppendQuoted(text, value); else text.Append(value);
        }

        private static void AppendQuoted(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 0x20) text.Append("\\u").Append(((int)character).ToString("x4"));
                        else text.Append(character);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
