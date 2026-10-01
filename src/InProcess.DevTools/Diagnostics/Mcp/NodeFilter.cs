using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// Predicate over tree nodes, built from the <c>filter</c> argument and/or a <c>find</c> query string.
    /// </summary>
    internal sealed class NodeFilter
    {
        public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();

        public string? Name { get; init; }

        public string? TextContains { get; init; }

        public bool? IsVisible { get; init; }

        public bool InteractiveOnly { get; init; }

        /// <summary>Free-text query: matches Name, type name or text (case-insensitive substring).</summary>
        public string? Query { get; init; }

        public bool IsEmpty => Types.Count == 0 && Name is null && TextContains is null && IsVisible is null && !InteractiveOnly && Query is null;

        public static NodeFilter From(McpArgs? filter, string? query = null)
        {
            var types = new List<string>();
            string? name = null;
            string? textContains = null;
            string? freeText = null;

            if (filter is not null)
            {
                types.AddRange(filter.StringList("types"));
                types.AddRange(filter.StringList("type"));
                name = filter.String("name");
                textContains = filter.String("textContains");
            }

            // Query syntax: "#Name" exact name, "type:Button" type, "text:foo" text, anything else is a free-text match.
            if (!string.IsNullOrWhiteSpace(query))
            {
                query = query.Trim();
                if (query.StartsWith('#'))
                {
                    name = query[1..];
                }
                else if (query.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
                {
                    types.Add(query[5..].Trim());
                }
                else if (query.StartsWith("text:", StringComparison.OrdinalIgnoreCase))
                {
                    textContains = query[5..].Trim();
                }
                else
                {
                    freeText = query;
                }
            }

            return new NodeFilter
            {
                Types = types,
                Name = name,
                TextContains = textContains,
                IsVisible = filter?.NullableBool("isVisible"),
                InteractiveOnly = filter?.Bool("interactiveOnly") ?? false,
                Query = freeText
            };
        }

        public bool Matches(AvaloniaObject node)
        {
            if (Types.Count > 0 && !Types.Any(t => NodeInfo.TypeChain(node).Any(type =>
                    string.Equals(type.Name, t, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.FullName, t, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            if (Name is not null && !string.Equals((node as INamed)?.Name, Name, StringComparison.Ordinal))
            {
                return false;
            }

            if (IsVisible is { } wantVisible && (node is Visual visual ? visual.IsEffectivelyVisible : true) != wantVisible)
            {
                return false;
            }

            if (InteractiveOnly && !NodeInfo.IsInteractive(node))
            {
                return false;
            }

            if (TextContains is not null && NodeInfo.GetText(node)?.Contains(TextContains, StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }

            if (Query is not null
                && (node as INamed)?.Name?.Contains(Query, StringComparison.OrdinalIgnoreCase) != true
                && !node.GetType().Name.Contains(Query, StringComparison.OrdinalIgnoreCase)
                && NodeInfo.GetText(node)?.Contains(Query, StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }

            return true;
        }
    }
}
