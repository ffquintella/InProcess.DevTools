using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// Typed, error-friendly access to the arguments of a tool call.
    /// </summary>
    internal sealed class McpArgs
    {
        private readonly JsonObject _json;

        public McpArgs(JsonObject? json)
        {
            _json = json ?? new JsonObject();
        }

        public JsonObject Json => _json;

        public bool Has(string name) => Node(name) is not null;

        // Argument names are matched ignoring case, '_' and '-', so rootIndex, root_index and root-index are equivalent.
        public JsonNode? Node(string name)
        {
            if (_json.TryGetPropertyValue(name, out var node))
            {
                return node;
            }

            var wanted = Normalize(name);
            foreach (var pair in _json)
            {
                if (Normalize(pair.Key) == wanted)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        private static string Normalize(string name) => name.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();

        public string? String(string name, string? fallback = null)
        {
            var node = Node(name);
            if (node is null)
            {
                return fallback;
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text;
            }

            if (node is JsonValue)
            {
                return node.ToJsonString().Trim('"');
            }

            throw Wrong(name, "a string");
        }

        public string RequiredString(string name)
        {
            var value = String(name);
            if (string.IsNullOrEmpty(value))
            {
                throw new McpToolException($"Argument '{name}' is required.");
            }

            return value;
        }

        public int? Int(string name)
        {
            var node = Node(name);
            if (node is null)
            {
                return null;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out var i))
                {
                    return i;
                }

                if (value.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue)
                {
                    return (int)d;
                }

                if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed))
                {
                    return parsed;
                }
            }

            throw Wrong(name, "an integer");
        }

        public int RequiredInt(string name)
        {
            return Int(name) ?? throw new McpToolException($"Argument '{name}' is required.");
        }

        public double? Double(string name)
        {
            var node = Node(name);
            if (node is null)
            {
                return null;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<double>(out var d))
                {
                    return d;
                }

                if (value.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            throw Wrong(name, "a number");
        }

        public bool Bool(string name, bool fallback = false)
        {
            var node = Node(name);
            if (node is null)
            {
                return fallback;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<bool>(out var b))
                {
                    return b;
                }

                if (value.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed))
                {
                    return parsed;
                }
            }

            throw Wrong(name, "a boolean");
        }

        public bool? NullableBool(string name) => Has(name) ? Bool(name) : null;

        public IReadOnlyList<string> StringList(string name)
        {
            var node = Node(name);
            if (node is null)
            {
                return Array.Empty<string>();
            }

            if (node is JsonArray array)
            {
                return array.Select(item => item is JsonValue v && v.TryGetValue<string>(out var s) ? s : item?.ToJsonString().Trim('"') ?? string.Empty).ToArray();
            }

            if (node is JsonValue single)
            {
                return new[] { single.TryGetValue<string>(out var s) ? s : single.ToJsonString().Trim('"') };
            }

            throw Wrong(name, "an array of strings");
        }

        public McpArgs? Object(string name)
        {
            var node = Node(name);
            if (node is null)
            {
                return null;
            }

            return node is JsonObject obj ? new McpArgs(obj) : throw Wrong(name, "an object");
        }

        public IReadOnlyList<McpArgs> ObjectList(string name)
        {
            var node = Node(name);
            if (node is null)
            {
                return Array.Empty<McpArgs>();
            }

            if (node is JsonArray array)
            {
                return array.Select(item => item is JsonObject obj ? new McpArgs(obj) : throw Wrong(name, "an array of objects")).ToArray();
            }

            throw Wrong(name, "an array of objects");
        }

        private static McpToolException Wrong(string name, string expected)
        {
            return new McpToolException($"Argument '{name}' must be {expected}.");
        }
    }
}
