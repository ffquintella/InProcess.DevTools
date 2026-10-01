using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer
    {
        private async Task<object?> WaitIdle(McpArgs args)
        {
            return await WaitIdleFromArgsAsync(args).ConfigureAwait(false);
        }

        private async Task<object> WaitIdleFromArgsAsync(McpArgs args)
        {
            var timeout = Math.Clamp(args.Int("timeoutMs") ?? args.Int("waitIdleTimeoutMs") ?? 5000, 0, 60_000);
            var settle = Math.Clamp(args.Int("settleMs") ?? 100, 0, 5000);
            var conditions = args.ObjectList("conditions");

            // Validate the conditions once, up front, so typos are reported instead of timing out.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var condition in conditions)
                {
                    EvaluateCondition(condition, validateOnly: true);
                }
            }).GetTask().ConfigureAwait(false);

            var clock = Stopwatch.StartNew();
            var states = new bool[conditions.Count];

            async Task<bool> PassAsync()
            {
                await DrainDispatcherAsync().ConfigureAwait(false);
                states = await Dispatcher.UIThread.InvokeAsync(() => conditions.Select(c => EvaluateCondition(c, validateOnly: false)).ToArray()).GetTask().ConfigureAwait(false);
                return states.All(s => s);
            }

            var satisfied = false;
            while (true)
            {
                if (await PassAsync().ConfigureAwait(false))
                {
                    if (settle == 0)
                    {
                        satisfied = true;
                        break;
                    }

                    await Task.Delay(settle).ConfigureAwait(false);
                    if (await PassAsync().ConfigureAwait(false))
                    {
                        satisfied = true;
                        break;
                    }
                }

                if (clock.ElapsedMilliseconds >= timeout)
                {
                    break;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            return new
            {
                idle = true,
                satisfied,
                timedOut = !satisfied,
                elapsedMs = clock.ElapsedMilliseconds,
                conditions = conditions.Select((c, i) => new { index = i, kind = c.String("kind"), satisfied = states[i] }).ToArray()
            };
        }

        /// <summary>Completes a layout pass and lets everything queued at or above Background priority run.</summary>
        private async Task DrainDispatcherAsync()
        {
            for (var i = 0; i < 2; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(UpdateLayouts, DispatcherPriority.Background).GetTask().ConfigureAwait(false);
            }
        }

        private void UpdateLayouts()
        {
            foreach (var root in _tree.Roots)
            {
                try
                {
                    (root as Layoutable)?.UpdateLayout();
                }
                catch
                {
                }
            }
        }

        private bool EvaluateCondition(McpArgs condition, bool validateOnly)
        {
            var kind = condition.RequiredString("kind").ToLowerInvariant();
            if (kind is not ("exists" or "not_exists" or "visible" or "property_equals"))
            {
                throw new McpToolException($"Unknown condition kind '{kind}'. Use exists, not_exists, visible or property_equals.");
            }

            var byQuery = condition.Has("query") || condition.Has("filter");
            if (!byQuery && !condition.Has("path"))
            {
                throw new McpToolException($"Condition '{kind}' needs a `path` (with optional tree/rootIndex) or a `query`/`filter`.");
            }

            if (kind == "property_equals")
            {
                condition.RequiredString("propertyName");
                if (!condition.Has("value"))
                {
                    throw new McpToolException("Condition 'property_equals' needs a `value`.");
                }
            }

            if (validateOnly)
            {
                return true;
            }

            var node = FindConditionTarget(condition, byQuery);
            switch (kind)
            {
                case "exists":
                    return node is not null;
                case "not_exists":
                    return node is null;
                case "visible":
                    return node is Avalonia.Visual { IsEffectivelyVisible: true };
                default:
                {
                    if (node is null)
                    {
                        return false;
                    }

                    if (NodeInfo.IsSensitive(node))
                    {
                        throw new McpToolException("property_equals cannot be used on a sensitive control.");
                    }

                    var read = ValueReader.Read(node, condition.RequiredString("propertyName"));
                    if (!read.Found || read.IsHidden)
                    {
                        return false;
                    }

                    var actual = ConditionText(read.Value);
                    var expected = condition.Node("value") is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var s) ? s : condition.Node("value")!.ToJsonString();
                    return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private static string? ConditionText(object? value)
        {
            return value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
        }

        private Avalonia.AvaloniaObject? FindConditionTarget(McpArgs condition, bool byQuery)
        {
            try
            {
                if (!byQuery)
                {
                    return _tree.Resolve(condition).Node;
                }

                var tree = NodeTree.ParseTree(condition.String("tree"));
                var filter = NodeFilter.From(condition.Object("filter"), condition.String("query"));
                var rootIndex = condition.Int("rootIndex");
                var rootCount = _tree.Roots.Length;
                for (var i = rootIndex ?? 0; i < (rootIndex is null ? rootCount : rootIndex.Value + 1) && i < rootCount; i++)
                {
                    var start = _tree.Resolve(tree, i, condition.String("path") ?? string.Empty);
                    var match = _tree.Find(start, filter, 64, 1, out _).FirstOrDefault();
                    if (match.Node is not null)
                    {
                        return match.Node;
                    }
                }

                return null;
            }
            catch (McpToolException)
            {
                // The target does not exist (yet): the condition is simply not satisfied.
                return null;
            }
        }
    }
}
