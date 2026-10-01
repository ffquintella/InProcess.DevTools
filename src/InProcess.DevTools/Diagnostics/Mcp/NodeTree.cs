using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// Path conventions: a node is addressed by (<c>rootIndex</c>, <c>path</c>) within a tree kind (visual or logical).
    /// <c>rootIndex</c> picks the top-level from devtools_list_roots and is never part of <c>path</c>.
    /// <c>path</c> is a slash-separated list of zero-based child indexes starting at the children of that root;
    /// an empty path is the root itself.
    /// </summary>
    internal sealed class NodeTree
    {
        private const int MaxVisitedNodes = 250_000;

        private readonly IDevToolsTopLevelGroup _group;

        public NodeTree(IDevToolsTopLevelGroup group)
        {
            _group = group;
        }

        public readonly record struct Target(AvaloniaObject Node, TopLevel Root, int RootIndex, string Tree, string Path)
        {
            public string DomId => CreateDomId(RootIndex, Path);
        }

        public static string CreateDomId(int rootIndex, string path)
        {
            return string.IsNullOrEmpty(path) ? $"root:{rootIndex}" : $"root:{rootIndex}:{path}";
        }

        public static string AppendPath(string path, int index)
        {
            return string.IsNullOrEmpty(path) ? index.ToString() : $"{path}/{index}";
        }

        public TopLevel[] Roots => _group.Items.Where(item => item is not Views.MainWindow).ToArray();

        public static string ParseTree(string? tree)
        {
            if (string.IsNullOrWhiteSpace(tree) || string.Equals(tree, "visual", StringComparison.OrdinalIgnoreCase))
            {
                return "visual";
            }

            return string.Equals(tree, "logical", StringComparison.OrdinalIgnoreCase)
                ? "logical"
                : throw new McpToolException($"Unknown tree '{tree}'. Use 'visual' or 'logical'.");
        }

        public TopLevel GetRoot(int rootIndex)
        {
            var roots = Roots;
            if (roots.Length == 0)
            {
                throw new McpToolException("No roots are attached; there is nothing to inspect yet.");
            }

            if (rootIndex < 0 || rootIndex >= roots.Length)
            {
                throw new McpToolException($"rootIndex {rootIndex} does not exist; valid values are 0..{roots.Length - 1} (see devtools_list_roots).");
            }

            return roots[rootIndex];
        }

        /// <summary>Resolves the standard <c>tree</c>/<c>rootIndex</c>/<c>path</c> arguments.</summary>
        public Target Resolve(McpArgs args, string pathArgument = "path")
        {
            return Resolve(ParseTree(args.String("tree")), args.Int("rootIndex") ?? 0, args.String(pathArgument) ?? string.Empty);
        }

        public Target Resolve(string tree, int rootIndex, string path)
        {
            var root = GetRoot(rootIndex);
            var segments = ParsePath(path);

            AvaloniaObject current = root;
            var walked = new List<int>();

            foreach (var segment in segments)
            {
                var children = GetChildren(current, tree).ToArray();
                if (segment < 0 || segment >= children.Length)
                {
                    var prefix = string.Join("/", walked);
                    var reason = children.Length == 0
                        ? $"'{Describe(current)}' has no children in the {tree} tree"
                        : $"'{Describe(current)}' has {children.Length} children (0..{children.Length - 1}) in the {tree} tree";
                    throw new McpToolException(
                        $"path '{path.Trim()}' not found (rootIndex {rootIndex}, {tree} tree): segment {walked.Count + 1} is {segment} but {reason}; closest valid prefix: '{prefix}'. " +
                        "Paths are relative to the root: do not include the rootIndex, the first segment is the first child of the root.");
                }

                current = children[segment];
                walked.Add(segment);
            }

            return new Target(current, root, rootIndex, tree, string.Join("/", walked));
        }

        public static string Describe(AvaloniaObject node)
        {
            var name = (node as INamed)?.Name;
            return string.IsNullOrEmpty(name) ? node.GetType().Name : $"{node.GetType().Name}#{name}";
        }

        public static IReadOnlyList<int> ParsePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Array.Empty<int>();
            }

            var result = new List<int>();
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(segment, out var index))
                {
                    throw new McpToolException($"path '{path}' is invalid: '{segment}' is not a child index. Use slash-separated integers such as \"0/2/1\".");
                }

                result.Add(index);
            }

            return result;
        }

        public static IEnumerable<AvaloniaObject> GetChildren(AvaloniaObject node, string tree)
        {
            if (string.Equals(tree, "logical", StringComparison.OrdinalIgnoreCase) && node is ILogical logical)
            {
                return logical.LogicalChildren.OfType<AvaloniaObject>();
            }

            if (node is Visual visual)
            {
                return visual.GetVisualChildren().OfType<AvaloniaObject>();
            }

            return Enumerable.Empty<AvaloniaObject>();
        }

        /// <summary>
        /// Path of <paramref name="node"/> relative to its root in the given tree, or null when the node is
        /// not attached to one of the roots (or is not part of that tree).
        /// </summary>
        public string? PathOf(AvaloniaObject node, string tree, out int rootIndex)
        {
            rootIndex = -1;
            var segments = new List<int>();
            AvaloniaObject current = node;
            var roots = Roots;

            // Stop at the root itself: a Window's visual parent is the platform host, which is not part of the addressable tree.
            while (Array.IndexOf(roots, current) < 0)
            {
                var parent = tree == "logical" ? (current as ILogical)?.LogicalParent as AvaloniaObject : (current as Visual)?.GetVisualParent() as AvaloniaObject;
                if (parent is null)
                {
                    break;
                }

                var index = GetChildren(parent, tree).ToList().IndexOf(current);
                if (index < 0)
                {
                    return null;
                }

                segments.Add(index);
                current = parent;
            }

            rootIndex = Array.IndexOf(roots, current);
            if (rootIndex < 0)
            {
                return null;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        public Target TargetFor(AvaloniaObject node, string tree = "visual")
        {
            var path = PathOf(node, tree, out var rootIndex)
                       ?? throw new McpToolException($"{Describe(node)} is not reachable from any root in the {tree} tree.");
            return new Target(node, Roots[rootIndex], rootIndex, tree, path);
        }

        /// <summary>
        /// Depth-first walk below <paramref name="start"/> (inclusive) up to <paramref name="maxDepth"/> levels.
        /// The callback returns false to stop the walk.
        /// </summary>
        public void Walk(Target start, int maxDepth, Func<AvaloniaObject, string, int, bool> visit)
        {
            var visited = 0;
            bool Recurse(AvaloniaObject node, string path, int depth)
            {
                if (++visited > MaxVisitedNodes || !visit(node, path, depth))
                {
                    return false;
                }

                if (depth >= maxDepth)
                {
                    return true;
                }

                var index = 0;
                foreach (var child in GetChildren(node, start.Tree))
                {
                    if (!Recurse(child, AppendPath(path, index++), depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            }

            Recurse(start.Node, start.Path, 0);
        }

        public readonly record struct FindMatch(AvaloniaObject Node, string Path);

        public List<FindMatch> Find(Target start, NodeFilter filter, int maxDepth, int limit, out bool truncated)
        {
            var matches = new List<FindMatch>();
            var hitLimit = false;
            Walk(start, maxDepth, (node, path, _) =>
            {
                if (filter.Matches(node))
                {
                    if (matches.Count >= limit)
                    {
                        hitLimit = true;
                        return false;
                    }

                    matches.Add(new FindMatch(node, path));
                }

                return true;
            });

            truncated = hitLimit;
            return matches;
        }
    }
}
