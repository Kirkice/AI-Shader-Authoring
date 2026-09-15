// Unity 6 ships System.Text.Json. Earlier editor versions use the Newtonsoft-backed API shim below.
#if UNITY_EDITOR && !UNITY_6000_0_OR_NEWER
// Unity 2019.4 does not include System.Text.Json. This narrow compatibility layer preserves the
// existing structured MCP protocol API while delegating parsing and serialization to Newtonsoft.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace System.Text.Json
{
    public enum JsonValueKind
    {
        Undefined,
        Object,
        Array,
        String,
        Number,
        True,
        False,
        Null
    }

    public enum JsonNamingPolicy
    {
        CamelCase
    }

    public sealed class JsonSerializerOptions
    {
        public JsonNamingPolicy PropertyNamingPolicy { get; set; }
        public bool IncludeFields { get; set; }
        public bool WriteIndented { get; set; }
    }

    public sealed class JsonDocument : IDisposable
    {
        private readonly JToken token;

        private JsonDocument(JToken token)
        {
            this.token = token;
        }

        public JsonElement RootElement
        {
            get { return new JsonElement(token); }
        }

        public static JsonDocument Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("JSON content cannot be empty.", nameof(json));
            return new JsonDocument(JToken.Parse(json));
        }

        public void Dispose()
        {
        }
    }

    public struct JsonElement
    {
        private readonly JToken token;

        internal JsonElement(JToken token)
        {
            this.token = token;
        }

        public JsonValueKind ValueKind
        {
            get
            {
                if (token == null) return JsonValueKind.Undefined;
                if (token.Type == JTokenType.Object) return JsonValueKind.Object;
                if (token.Type == JTokenType.Array) return JsonValueKind.Array;
                if (token.Type == JTokenType.String || token.Type == JTokenType.Date || token.Type == JTokenType.Guid || token.Type == JTokenType.Uri || token.Type == JTokenType.TimeSpan) return JsonValueKind.String;
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) return JsonValueKind.Number;
                if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? JsonValueKind.True : JsonValueKind.False;
                return JsonValueKind.Null;
            }
        }

        public bool TryGetProperty(string name, out JsonElement value)
        {
            var objectToken = token as JObject;
            JToken child;
            if (objectToken != null && objectToken.TryGetValue(name, StringComparison.Ordinal, out child))
            {
                value = new JsonElement(child);
                return true;
            }
            value = default(JsonElement);
            return false;
        }

        public string GetString()
        {
            return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
        }

        public string GetRawText()
        {
            return token == null ? "null" : token.ToString(Formatting.None);
        }

        public JsonElement Clone()
        {
            return new JsonElement(token == null ? null : token.DeepClone());
        }

        public IEnumerable<JsonElement> EnumerateArray()
        {
            var array = token as JArray;
            if (array == null) yield break;
            foreach (var item in array) yield return new JsonElement(item);
        }

        public bool TryGetInt32(out int value)
        {
            value = 0;
            return token != null && token.Type == JTokenType.Integer && int.TryParse(token.ToString(), out value);
        }

        public bool TryGetSingle(out float value)
        {
            value = 0f;
            return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) && float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        internal JToken ToNewtonsoftToken()
        {
            return token;
        }
    }

    public static class JsonSerializer
    {
        public static string Serialize(object value)
        {
            return Serialize(value, null);
        }

        public static string Serialize(object value, JsonSerializerOptions options)
        {
            return JsonConvert.SerializeObject(value, CreateSettings(options));
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        private static JsonSerializerSettings CreateSettings(JsonSerializerOptions options)
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new JsonElementConverter());
            if (options == null) return settings;
            settings.Formatting = options.WriteIndented ? Formatting.Indented : Formatting.None;
            if (options.IncludeFields || options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase)
            {
                settings.ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase ? new CamelCaseNamingStrategy() : null
                };
            }
            return settings;
        }
    }

    internal sealed class JsonElementConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(JsonElement);
        }

        public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
        {
            var token = ((JsonElement)value).ToNewtonsoftToken();
            if (token == null) writer.WriteNull();
            else token.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            throw new NotSupportedException("Deserializing directly to JsonElement is not supported.");
        }

        public override bool CanRead
        {
            get { return false; }
        }
    }
}
#endif
