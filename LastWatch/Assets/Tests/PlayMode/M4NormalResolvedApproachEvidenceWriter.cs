using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Partner;
using UnityEngine;

namespace LastWatch.Tests.M4
{
    // Passive detached-value serialization. No simulation, guard, mapping or live-object traversal.
    internal sealed class M4NormalResolvedApproachEvidenceWriter : IDisposable
    {
        readonly StreamWriter output;
        internal readonly string Path;
        internal int Rows { get; private set; }
        internal bool Closed { get; private set; }

        internal M4NormalResolvedApproachEvidenceWriter(string path)
        {
            Path = path;
            output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read), new UTF8Encoding(false));
        }

        internal static Dictionary<string, object> Map(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }

        internal static Dictionary<string, object> Properties(object value, params string[] omit)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (value == null) return result;
            foreach (PropertyInfo p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.GetIndexParameters().Length == 0 && !omit.Contains(p.Name))
                    result.Add(p.Name, p.GetValue(value, null));
            return result;
        }

        internal static object Step(M4ResolvedApproachFixtureSnapshot row)
        {
            var values = Properties(row, "SourceRoutes", "AuthoredBaseWaypoints", "Actor");
            values.Add("Actor", row.Actor == null ? null : Properties(row.Actor,
                "AppliedAuthoredWaypoints", "AssignedRoutes", "CanonicalStations"));
            return values;
        }

        internal void Write(string kind, string caseId, object values)
        {
            if (Closed) throw new InvalidOperationException("Evidence stream already closed.");
            output.WriteLine(Json(Map("schemaVersion", 1, "recordKind", kind,
                "rowOrdinal", Rows + 1, "caseId", caseId, "values", values)));
            output.Flush();
            Rows++;
        }

        internal void Seal(string outcome, string reason, int completedCases)
        {
            Write("suite-end", string.Empty, Map("outcome", outcome, "reason", reason,
                "completedCases", completedCases, "precedingRows", Rows));
            Dispose();
        }

        public void Dispose()
        {
            if (Closed) return;
            output.Dispose();
            Closed = true;
        }

        internal static string Sha(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        internal static string Json(object value)
        {
            var text = new StringBuilder();
            Append(text, value, 0);
            return text.ToString();
        }

        static void Quote(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }
            text.Append('"');
        }

        static void Append(StringBuilder text, object value, int depth)
        {
            if (depth > 32) throw new InvalidDataException("Detached serialization depth exceeded.");
            if (value == null) { text.Append("null"); return; }
            if (value is UnityEngine.Object) throw new InvalidDataException("Live Unity reference rejected.");
            if (value is string s) { Quote(text, s); return; }
            if (value is bool b) { text.Append(b ? "true" : "false"); return; }
            Type type = value.GetType();
            if (type.IsEnum) { Quote(text, value.ToString()); return; }
            if (value is float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) throw new InvalidDataException("Nonfinite float.");
                text.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
            }
            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new InvalidDataException("Nonfinite double.");
                text.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
            }
            if (type.IsPrimitive || value is decimal)
            { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            if (value is Vector3 v) value = Map("x", v.x, "y", v.y, "z", v.z);
            else if (value is Quaternion q) value = Map("x", q.x, "y", q.y, "z", q.z, "w", q.w);
            else if (value is Bounds bounds) value = Map("center", bounds.center, "extents", bounds.extents);
            if (value is IDictionary<string, object> map)
            {
                text.Append('{'); bool first = true;
                foreach (var pair in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first) text.Append(','); first = false;
                    Quote(text, pair.Key); text.Append(':'); Append(text, pair.Value, depth + 1);
                }
                text.Append('}'); return;
            }
            if (value is IEnumerable sequence)
            {
                text.Append('['); bool first = true;
                foreach (object item in sequence)
                { if (!first) text.Append(','); first = false; Append(text, item, depth + 1); }
                text.Append(']'); return;
            }
            // Only approved Core value DTOs/identifier structs may use their public getters.
            if (type.Namespace == null || !type.Namespace.StartsWith("LastWatch.Core", StringComparison.Ordinal))
                throw new InvalidDataException("Unapproved serialization type: " + type.FullName);
            var properties = Properties(value);
            if (properties.Count == 0) throw new InvalidDataException("No detached public values: " + type.FullName);
            Append(text, properties, depth + 1);
        }
    }
}
