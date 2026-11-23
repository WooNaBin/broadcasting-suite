// CustomFlags remains available for dynamic flag sets.
// Work log templates are stored as string[] in JSON (see docs/schema.md).
// Prefer WorkTemplateCatalog in DataFormats.cs for the fixed UI list.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkLog
{
    [JsonConverter(typeof(CustomFlagsConverter))]
    public class CustomFlags
    {
        private static readonly Dictionary<string, int> _flagRegistry = new();
        private static int _nextIndex = 0;
        private readonly BitArray _bits;

        public static void RegisterFlag(string flagName)
        {
            if (!_flagRegistry.ContainsKey(flagName))
                _flagRegistry[flagName] = _nextIndex++;
        }

        public CustomFlags()
        {
            _bits = new BitArray(Math.Max(1, _nextIndex));
        }

        public void Set(string flagName, bool value)
        {
            if (!_flagRegistry.TryGetValue(flagName, out int index))
                throw new ArgumentException($"등록되지 않은 플래그 이름입니다: {flagName}");
            if (index >= _bits.Length)
                _bits.Length = _nextIndex;
            _bits.Set(index, value);
        }

        public bool Has(string flagName)
        {
            if (_flagRegistry.TryGetValue(flagName, out int index) && index < _bits.Length)
                return _bits.Get(index);
            return false;
        }

        public List<string> GetActiveFlags()
        {
            var activeFlags = new List<string>();
            foreach (var kvp in _flagRegistry)
            {
                if (kvp.Value < _bits.Length && _bits.Get(kvp.Value))
                    activeFlags.Add(kvp.Key);
            }
            return activeFlags;
        }
    }

    public class CustomFlagsConverter : JsonConverter<CustomFlags>
    {
        public override CustomFlags Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var flags = new CustomFlags();
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("배열 형식이 아닙니다.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? flagName = reader.GetString();
                if (!string.IsNullOrEmpty(flagName))
                {
                    CustomFlags.RegisterFlag(flagName);
                    flags.Set(flagName, true);
                }
            }
            return flags;
        }

        public override void Write(Utf8JsonWriter writer, CustomFlags value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var flag in value.GetActiveFlags())
                writer.WriteStringValue(flag);
            writer.WriteEndArray();
        }
    }
}
