using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DouyinQualityHelper
{
    /// <summary>
    /// 轻量 JSON 读写。
    ///
    /// WinUI 3 项目没有 System.Web.Extensions，所以不能像旧 WPF 版那样用
    /// JavaScriptSerializer。这里改用 System.Text.Json，并把结果转换成
    /// Dictionary&lt;string, object&gt; / List&lt;object&gt;，这样上层代码（Config、
    /// VersionChecker、UserscriptBundler…）一行都不用改。
    /// </summary>
    public static class JsonUtil
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            using (var doc = JsonDocument.Parse(json))
                return Convert(doc.RootElement);
        }

        static object Convert(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var p in el.EnumerateObject())
                        dict[p.Name] = Convert(p.Value);
                    return dict;

                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in el.EnumerateArray())
                        list.Add(Convert(item));
                    return list;

                case JsonValueKind.String:
                    return el.GetString();

                case JsonValueKind.Number:
                    return el.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                default:
                    return null;
            }
        }

        public static string Serialize(object obj)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                    Write(w, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        static void Write(Utf8JsonWriter w, object obj)
        {
            if (obj == null) { w.WriteNullValue(); return; }

            var s = obj as string;
            if (s != null) { w.WriteStringValue(s); return; }

            if (obj is bool) { w.WriteBooleanValue((bool)obj); return; }

            if (obj is int || obj is long || obj is short || obj is byte)
            {
                w.WriteNumberValue(System.Convert.ToInt64(obj));
                return;
            }

            if (obj is double || obj is float || obj is decimal)
            {
                w.WriteNumberValue(System.Convert.ToDouble(obj));
                return;
            }

            var dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                w.WriteStartObject();
                foreach (var kv in dict)
                {
                    w.WritePropertyName(kv.Key);
                    Write(w, kv.Value);
                }
                w.WriteEndObject();
                return;
            }

            var list = obj as System.Collections.IEnumerable;
            if (list != null && !(obj is string))
            {
                w.WriteStartArray();
                foreach (var item in list) Write(w, item);
                w.WriteEndArray();
                return;
            }

            w.WriteStringValue(System.Convert.ToString(obj, CultureInfo.InvariantCulture));
        }

        public static Dictionary<string, object> AsDict(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string GetString(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return "";
            if (v is double)
            {
                double dv = (double)v;
                return dv == Math.Floor(dv)
                    ? ((long)dv).ToString(CultureInfo.InvariantCulture)
                    : dv.ToString(CultureInfo.InvariantCulture);
            }
            if (v is bool) return ((bool)v) ? "true" : "false";
            return System.Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>取布尔值，缺省用 defaultValue。</summary>
        public static bool GetBool(Dictionary<string, object> d, string key, bool defaultValue)
        {
            object v = Get(d, key);
            if (v is bool) return (bool)v;
            if (v is string)
            {
                string s = ((string)v).Trim().ToLowerInvariant();
                if (s == "true" || s == "1" || s == "yes") return true;
                if (s == "false" || s == "0" || s == "no") return false;
            }
            if (v is double) return (double)v != 0;
            return defaultValue;
        }
    }
}
