using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Deterministic 64-bit fingerprint of a state object graph, identical on the client (Mono / IL2CPP, Newtonsoft) and the
    /// server (.NET, System.Text.Json): local-first commands compare the client's predicted state with the server's by this
    /// hash instead of shipping the whole state (docs/server.md, "Local-first commands").
    /// <list type="bullet">
    /// <item>Walks public instance fields only (what both JSON serializers persist for state classes), ordered by name;
    /// properties, internal fields and <c>[NonSerialized]</c> fields are not part of the state.</item>
    /// <item>Dictionaries are hashed in ordinal key order, sets in sorted element order (never in iteration order).</item>
    /// <item>A null collection hashes like an empty one; -0.0 like 0.0; numbers by value (bits for doubles).</item>
    /// </list>
    /// FNV-1a over a typed token stream; not cryptographic (it detects divergence, it does not authenticate).
    /// </summary>
    public static class StateHash
    {
        private const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;
        private static readonly Dictionary<Type, FieldInfo[]> Fields = new Dictionary<Type, FieldInfo[]>();
        private const int MaxDepth = 64;

        /// <summary>The fingerprint of <paramref name="value"/> (0x… hex string form: <see cref="Hex"/>).</summary>
        public static ulong Of(object value)
        {
            ulong h = Offset;
            Walk(ref h, value, value?.GetType(), 0);
            return h;
        }

        /// <summary>16 lowercase hex digits (the wire form).</summary>
        public static string Hex(object value) => Of(value).ToString("x16");

        private static void Byte(ref ulong h, byte b) { h ^= b; h *= Prime; }

        private static void U64(ref ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { Byte(ref h, (byte)v); v >>= 8; }
        }

        private static void Str(ref ulong h, string s)
        {
            U64(ref h, (ulong)s.Length);
            foreach (char c in s) { Byte(ref h, (byte)c); Byte(ref h, (byte)(c >> 8)); }
        }

        private static void Walk(ref ulong h, object v, Type declared, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("StateHash: object graph too deep (cycle?)");
            if (v == null)
            {
                // A null collection hashes like an empty one (serializers may restore either).
                if (declared != null && declared != typeof(string) && typeof(IEnumerable).IsAssignableFrom(declared)) { Byte(ref h, 7); U64(ref h, 0); return; }
                Byte(ref h, 0);
                return;
            }
            var t = v.GetType();
            switch (v)
            {
                case string s: Byte(ref h, 1); Str(ref h, s); return;
                case bool b: Byte(ref h, 2); Byte(ref h, b ? (byte)1 : (byte)0); return;
                case double d: Byte(ref h, 3); U64(ref h, d == 0 ? 0UL : (ulong)BitConverter.DoubleToInt64Bits(d)); return;
                case float f: Byte(ref h, 3); U64(ref h, f == 0 ? 0UL : (ulong)BitConverter.DoubleToInt64Bits(f)); return;
                case decimal m: Byte(ref h, 1); Str(ref h, m.ToString(System.Globalization.CultureInfo.InvariantCulture)); return;
                case ulong ul: Byte(ref h, 4); U64(ref h, ul); return;
            }
            if (t.IsEnum || t.IsPrimitive)
            {
                Byte(ref h, 4);
                U64(ref h, unchecked((ulong)Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture)));
                return;
            }
            if (v is IDictionary dict)
            {
                var keys = new List<object>();
                foreach (var k in dict.Keys) keys.Add(k);
                keys.Sort(CompareKeys);
                Byte(ref h, 8);
                U64(ref h, (ulong)keys.Count);
                foreach (var k in keys)
                {
                    Walk(ref h, k, k.GetType(), depth + 1);
                    Walk(ref h, dict[k], null, depth + 1);
                }
                return;
            }
            if (v is IEnumerable seq)
            {
                var items = new List<object>();
                foreach (var x in seq) items.Add(x);
                if (IsSet(t)) items.Sort(CompareKeys);
                Byte(ref h, 7);
                U64(ref h, (ulong)items.Count);
                Type elem = t.IsArray ? t.GetElementType() : (t.IsGenericType ? t.GetGenericArguments()[0] : null);
                foreach (var x in items) Walk(ref h, x, elem, depth + 1);
                return;
            }
            Byte(ref h, 9);
            foreach (var f in FieldsOf(t))
            {
                Str(ref h, f.Name);
                Walk(ref h, f.GetValue(v), f.FieldType, depth + 1);
            }
            Byte(ref h, 10);
        }

        private static bool IsSet(Type t)
        {
            if (!t.IsGenericType) return false;
            foreach (var i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>)) return true;
            return false;
        }

        private static int CompareKeys(object a, object b)
        {
            if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
            if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
            var ta = a.GetType();
            if ((ta.IsEnum || ta.IsPrimitive) && (b.GetType().IsEnum || b.GetType().IsPrimitive))
            {
                if (a is double || a is float) return Convert.ToDouble(a).CompareTo(Convert.ToDouble(b));
                if (a is ulong) return ((ulong)a).CompareTo((ulong)b);
                return Convert.ToInt64(a).CompareTo(Convert.ToInt64(b));
            }
            return string.CompareOrdinal(a.ToString(), b.ToString());
        }

        private static FieldInfo[] FieldsOf(Type t)
        {
            lock (Fields)
            {
                if (Fields.TryGetValue(t, out var cached)) return cached;
                var list = new List<FieldInfo>();
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (!f.IsNotSerialized && !f.IsInitOnly) list.Add(f);
                list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                var arr = list.ToArray();
                Fields[t] = arr;
                return arr;
            }
        }
    }
}
