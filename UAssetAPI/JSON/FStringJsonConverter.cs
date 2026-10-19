using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;

namespace UAssetAPI.JSON
{
    internal class FStringJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(FString);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var str = value as FString;
            if (str?.Value == null)
            {
                writer.WriteNull();
                return;
            }
            // Keep existing string shorthand unless it would lose the native encoding.
            if (str.Encoding.CodePage == new FString(str.Value).Encoding.CodePage)
            {
                writer.WriteValue(str.Value);
                return;
            }
            string encoding = str.Encoding.CodePage switch
            {
                65001 => "utf-8",
                1200 => "utf-16",
                _ => throw new JsonSerializationException($"Unsupported FString encoding {str.Encoding.WebName}")
            };
            writer.WriteStartObject();
            writer.WritePropertyName("Value");
            writer.WriteValue(str.Value);
            writer.WritePropertyName("Encoding");
            writer.WriteValue(encoding);
            writer.WriteEndObject();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType == JsonToken.String) return new FString((string)reader.Value);
            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException("Expected an FString string, null or encoding object");
            var obj = JObject.Load(reader);
            if (obj["Value"]?.Type != JTokenType.String || obj["Encoding"]?.Type != JTokenType.String)
                throw new JsonSerializationException("An FString encoding object requires string Value and Encoding fields");
            Encoding encoding = (string)obj["Encoding"] switch
            {
                "utf-8" => Encoding.UTF8,
                "utf-16" => Encoding.Unicode,
                _ => throw new JsonSerializationException("Unsupported FString encoding " + obj["Encoding"])
            };
            return new FString((string)obj["Value"], encoding);
        }
    }
}
