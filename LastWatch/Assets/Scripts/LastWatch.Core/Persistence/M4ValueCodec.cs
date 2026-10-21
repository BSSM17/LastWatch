using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LastWatch.Core.Persistence
{
    // JsonUtility only sees fields, arrays and strings. Nullable and UInt64 never reach it.
    // This codec is restricted to field-only Core DTO graphs; it does not deserialize type names.
    [Serializable] public sealed class M4ValueDto
    { public string name; public string kind; public string scalar; public M4ValueDto[] children; }

    // Unity JsonUtility limits nested object serialization to ten levels. M4ValueDto is a
    // deliberately recursive in-memory representation, so session payloads are written as a
    // flat node table at the JSON boundary and reconstructed only after parsing.
    [Serializable] internal sealed class M4FlatValueDto
    { public string name; public string kind; public bool hasScalar; public string scalar; public int[] children; }

    internal static class M4ValueCodec
    {
        internal static M4ValueDto Encode<T>(T value) => EncodeValue(value, typeof(T), "", 0);
        internal static T Decode<T>(M4ValueDto value) => (T)DecodeValue(value, typeof(T), 0);
        internal static T Copy<T>(T value) => Decode<T>(Encode(value));
        // Explicit pre-navigation Partner layout. This is not permissive field decoding.
        internal static M4ValueDto EncodeBeforePartnerMovement<T>(T value) => EncodeValue(value, typeof(T), "", 0, true);
        internal static T DecodeBeforePartnerMovement<T>(M4ValueDto value) => (T)DecodeValue(value, typeof(T), 0, true);
        internal static M4ValueDto EncodeBeforePartnerExchange<T>(T value) => EncodeValue(value, typeof(T), "", 0, false, true);
        internal static T DecodeBeforePartnerExchange<T>(M4ValueDto value) => (T)DecodeValue(value, typeof(T), 0, false, true);
        static FieldInfo[] BeforeExchangeFields(Type type)
        {
            var result = new List<FieldInfo>();
            foreach (var field in Fields(type)) if (field.Name != "ExchangeFlow") result.Add(field);
            return result.ToArray();
        }
        internal static bool HasBeforePartnerMovementShape(M4ValueDto root)
        {
            if (root == null || root.children == null) return false;
            M4ValueDto partner = null;
            foreach (var child in root.children) if (child != null && child.name == "partner")
            { if (partner != null) return false; partner = child; }
            if (partner == null || partner.kind != "object" || partner.scalar != null || partner.children == null) return false;
            var fields = BeforeMovementFields(typeof(LastWatch.Core.Partner.PartnerRuntimeState));
            if (partner.children.Length != fields.Length) return false;
            for (int i = 0; i < fields.Length; i++) if (partner.children[i] == null || partner.children[i].name != fields[i].Name) return false;
            return true;
        }
        static FieldInfo[] BeforeMovementFields(Type type)
        {
            var all = Fields(type); var result = new List<FieldInfo>();
            foreach (var field in all)
                if (field.Name != "MovementProgress" && field.Name != "NextMovementPlanId" && field.Name != "ExchangeFlow") result.Add(field);
            return result.ToArray();
        }
        internal static M4FlatValueDto[] Flatten(M4ValueDto root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var nodes = new List<M4FlatValueDto>();
            FlattenNode(root, nodes, 0);
            return nodes.ToArray();
        }
        internal static M4ValueDto Inflate(M4FlatValueDto[] nodes) => Inflate(nodes, false);
        // Envelope-v3 did not preserve null string fields through JsonUtility. It has no
        // committed runtime evidence, but this permits best-effort reads while v4 is explicit.
        internal static M4ValueDto InflateLegacyV3(M4FlatValueDto[] nodes) => Inflate(nodes, true);
        static M4ValueDto Inflate(M4FlatValueDto[] nodes, bool legacyScalarEncoding)
        {
            if (nodes == null || nodes.Length == 0 || nodes.Length > 1000000) throw new FormatException("Malformed flat DTO payload.");
            var visiting = new bool[nodes.Length];
            var visited = new bool[nodes.Length];
            M4ValueDto root = InflateNode(nodes, 0, visiting, visited, 0, legacyScalarEncoding);
            for (int index = 0; index < visited.Length; index++) if (!visited[index]) throw new FormatException("Detached flat DTO node.");
            return root;
        }
        static int FlattenNode(M4ValueDto source, List<M4FlatValueDto> nodes, int depth)
        {
            if (source == null || source.children == null || depth > 48) throw new FormatException("Malformed DTO node.");
            int index = nodes.Count;
            nodes.Add(new M4FlatValueDto { name = source.name, kind = source.kind, hasScalar = source.scalar != null, scalar = source.scalar, children = new int[source.children.Length] });
            for (int child = 0; child < source.children.Length; child++) nodes[index].children[child] = FlattenNode(source.children[child], nodes, depth + 1);
            return index;
        }
        static M4ValueDto InflateNode(M4FlatValueDto[] nodes, int index, bool[] visiting, bool[] visited, int depth, bool legacyScalarEncoding)
        {
            if (index < 0 || index >= nodes.Length || depth > 48 || visiting[index] || visited[index]) throw new FormatException("Malformed flat DTO graph.");
            M4FlatValueDto source = nodes[index];
            if (source == null || source.children == null || source.children.Length > 1000000) throw new FormatException("Malformed flat DTO node.");
            string scalar;
            if (legacyScalarEncoding)
                scalar = source.kind == "object" || source.kind == "array" || source.kind == "null" ? null : source.scalar;
            else
            {
                // JsonUtility rehydrates an omitted string as an empty string. hasScalar is the
                // authoritative presence bit; only that known empty-string artifact is allowed.
                if ((source.hasScalar && source.scalar == null) || (!source.hasScalar && source.scalar != null && source.scalar != string.Empty))
                    throw new FormatException("Flat DTO scalar-presence mismatch.");
                scalar = source.hasScalar ? source.scalar : null;
            }
            visiting[index] = true;
            var result = new M4ValueDto { name = source.name, kind = source.kind, scalar = scalar, children = new M4ValueDto[source.children.Length] };
            for (int child = 0; child < source.children.Length; child++) result.children[child] = InflateNode(nodes, source.children[child], visiting, visited, depth + 1, legacyScalarEncoding);
            visiting[index] = false;
            visited[index] = true;
            return result;
        }
        static FieldInfo[] Fields(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (a,b) => string.CompareOrdinal(a.Name,b.Name));
            if (fields.Length == 0) throw new FormatException("Field-only DTO required: " + type.Name);
            return fields;
        }
        static M4ValueDto EncodeValue(object value, Type type, string name, int depth, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {
            if (depth > 48) throw new FormatException("DTO nesting limit.");
            var node = new M4ValueDto { name = name, children = Array.Empty<M4ValueDto>() };
            if (value == null) { node.kind = "null"; return node; }
            Type nullable = Nullable.GetUnderlyingType(type); if (nullable != null) type = nullable;
            if (type == typeof(string)) { node.kind = "string"; node.scalar = (string)value; }
            else if (type == typeof(ulong)) { node.kind = "u64"; node.scalar = ((ulong)value).ToString("x16", CultureInfo.InvariantCulture); }
            else if (type == typeof(int) || type.IsEnum) { node.kind = type.IsEnum ? "enum" : "i32"; node.scalar = Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture); }
            else if (type == typeof(bool)) { node.kind = "bool"; node.scalar = (bool)value ? "1" : "0"; }
            else if (type == typeof(float))
            { float f = (float)value; if (float.IsNaN(f) || float.IsInfinity(f)) throw new FormatException("Nonfinite float."); node.kind = "f32"; node.scalar = (f == 0 ? 0f : f).ToString("R",CultureInfo.InvariantCulture); }
            else if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                node.kind = "array"; Type itemType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                var rows = new List<M4ValueDto>(); foreach (object item in (IEnumerable)value) rows.Add(EncodeValue(item,itemType,"",depth+1,beforePartnerMovement,beforePartnerExchange)); node.children = rows.ToArray();
            }
            else
            {
                bool oldPartner = beforePartnerMovement && type == typeof(LastWatch.Core.Partner.PartnerRuntimeState);
                if (oldPartner)
                {
                    var partner = (LastWatch.Core.Partner.PartnerRuntimeState)value;
                    if (partner.MovementProgress != null || partner.NextMovementPlanId != 1) throw new FormatException("Legacy Partner cannot contain navigation provenance.");
                }
                node.kind = "object"; var fields = oldPartner ? BeforeMovementFields(type) : ((beforePartnerExchange && type == typeof(LastWatch.Core.Partner.PartnerRuntimeState)) ? BeforeExchangeFields(type) : Fields(type)); node.children = new M4ValueDto[fields.Length];
                for (int n=0;n<fields.Length;n++) node.children[n] = EncodeValue(fields[n].GetValue(value), fields[n].FieldType, fields[n].Name, depth+1,beforePartnerMovement,beforePartnerExchange);
            }
            return node;
        }
        static object DecodeValue(M4ValueDto node, Type type, int depth, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {
            if (node == null || node.children == null || depth > 48 || node.children.Length > 1000000) throw new FormatException("Malformed DTO node.");
            Type nullable = Nullable.GetUnderlyingType(type);
            if (node.kind == "null") { if ((type.IsValueType && nullable == null) || node.scalar != null || node.children.Length != 0) throw new FormatException("Unexpected null."); return null; }
            if (nullable != null) type = nullable;
            if (type == typeof(string)) { RequireKind(node,"string"); return node.scalar ?? throw new FormatException("Missing string."); }
            if (type == typeof(ulong)) { RequireKind(node,"u64"); return U64(node.scalar); }
            if (type == typeof(int) || type.IsEnum)
            { RequireKind(node,type.IsEnum ? "enum" : "i32"); int v = int.Parse(node.scalar,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture); if (type.IsEnum) { if (!Enum.IsDefined(type,v)) throw new FormatException("Unknown enum."); return Enum.ToObject(type,v); } return v; }
            if (type == typeof(bool)) { RequireKind(node,"bool"); if (node.scalar != "0" && node.scalar != "1") throw new FormatException("Invalid bool."); return node.scalar == "1"; }
            if (type == typeof(float)) { RequireKind(node,"f32"); float v = float.Parse(node.scalar,NumberStyles.Float,CultureInfo.InvariantCulture); if (float.IsNaN(v)||float.IsInfinity(v)) throw new FormatException("Nonfinite float."); return v == 0 ? 0f : v; }
            if (node.scalar != null) throw new FormatException("Unexpected compound scalar at '" + node.name + "' for " + type.FullName + " (kind=" + node.kind + ").");
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                if (node.kind != "array") throw new FormatException("Expected array."); Type item = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                Array array = Array.CreateInstance(item,node.children.Length);
                for (int n=0;n<array.Length;n++) { if(node.children[n].name!="")throw new FormatException("Named array element."); array.SetValue(DecodeValue(node.children[n],item,depth+1,beforePartnerMovement,beforePartnerExchange),n); }
                if (type.IsArray) return array; IList list = (IList)Activator.CreateInstance(type); foreach(object v in array)list.Add(v); return list;
            }
            if (node.kind != "object") throw new FormatException("Expected object.");
            bool oldPartner = beforePartnerMovement && type == typeof(LastWatch.Core.Partner.PartnerRuntimeState);
            FieldInfo[] fields = oldPartner ? BeforeMovementFields(type) : ((beforePartnerExchange && type == typeof(LastWatch.Core.Partner.PartnerRuntimeState)) ? BeforeExchangeFields(type) : Fields(type)); if (fields.Length != node.children.Length) throw new FormatException("Missing/unknown DTO field.");
            object result = Activator.CreateInstance(type);
            for (int n=0;n<fields.Length;n++) { if (node.children[n].name != fields[n].Name) throw new FormatException("Duplicate, unordered or missing DTO field."); fields[n].SetValue(result,DecodeValue(node.children[n],fields[n].FieldType,depth+1,beforePartnerMovement,beforePartnerExchange)); }
            if (oldPartner)
            { var partner = (LastWatch.Core.Partner.PartnerRuntimeState)result; partner.MovementProgress = null; partner.NextMovementPlanId = 1; }
            return result;
        }
        static void RequireKind(M4ValueDto node,string kind) { if(node.kind!=kind||node.children.Length!=0)throw new FormatException("Scalar kind mismatch."); }
        internal static ulong U64(string value)
        {
            if(value==null||value.Length!=16)throw new FormatException("Expected fixed lowercase u64 hex.");
            foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))throw new FormatException("Invalid u64 hex.");
            return ulong.Parse(value,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture);
        }
        internal static string Hash(string domain, M4ValueDto value)
        {
            var writer = new StringBuilder(); Text(writer,domain); Write(writer,value);
            using(var sha=SHA256.Create())return State.M4BridgeProfile.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(writer.ToString())));
        }
        static void Text(StringBuilder b,string text) { if(text==null){b.Append("N;");return;} b.Append(Encoding.UTF8.GetByteCount(text).ToString(CultureInfo.InvariantCulture)).Append(':').Append(text).Append(';'); }
        static void Write(StringBuilder b,M4ValueDto n) { Text(b,n.name);Text(b,n.kind);Text(b,n.scalar);b.Append(n.children.Length.ToString(CultureInfo.InvariantCulture)).Append('[');foreach(var child in n.children)Write(b,child);b.Append(']'); }
    }

    // JsonUtility accepts duplicate keys and absent fields; reject those before DTO decoding.
    internal sealed class M4StrictJson
    {
        readonly string text; int offset; int depth;
        M4StrictJson(string value){text=value;}
        internal static void Validate(string value)
        { if(string.IsNullOrWhiteSpace(value)||value.Length>32*1024*1024)throw new FormatException("JSON size/missing input.");var p=new M4StrictJson(value);p.Value();p.White();if(p.offset!=value.Length)throw new FormatException("Trailing JSON."); }
        void White(){while(offset<text.Length&&char.IsWhiteSpace(text[offset]))offset++;}
        void Expect(char c){White();if(offset>=text.Length||text[offset++]!=c)throw new FormatException("Invalid JSON punctuation.");}
        string String()
        {
            White();int start=offset;Expect('"');bool escape=false;
            while(offset<text.Length){char c=text[offset++];if(c<' ')throw new FormatException("JSON control character.");if(escape){escape=false;continue;}if(c=='\\'){escape=true;continue;}if(c=='"'){string raw=text.Substring(start,offset-start);return JsonUtility.FromJson<StringBox>("{\"value\":"+raw+"}").value;}}
            throw new FormatException("Unterminated JSON string.");
        }
        [Serializable] sealed class StringBox { public string value; }
        void Value()
        {
            if(++depth>128)throw new FormatException("JSON nesting limit.");White();if(offset>=text.Length)throw new FormatException("Missing JSON value.");char c=text[offset];
            if(c=='{'){offset++;var names=new HashSet<string>(StringComparer.Ordinal);White();if(offset<text.Length&&text[offset]=='}')offset++;else while(true){string key=String();if(!names.Add(key))throw new FormatException("Duplicate JSON key.");Expect(':');Value();White();if(offset<text.Length&&text[offset]=='}'){offset++;break;}Expect(',');}}
            else if(c=='['){offset++;White();if(offset<text.Length&&text[offset]==']')offset++;else while(true){Value();White();if(offset<text.Length&&text[offset]==']'){offset++;break;}Expect(',');}}
            else if(c=='"')String();
            else {int start=offset;while(offset<text.Length&&",]} \r\n\t".IndexOf(text[offset])<0)offset++;string token=text.Substring(start,offset-start);double number;if(token!="null"&&token!="true"&&token!="false"&&(!double.TryParse(token,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||double.IsNaN(number)||double.IsInfinity(number)))throw new FormatException("Invalid JSON token.");}
            depth--;
        }
    }
}
