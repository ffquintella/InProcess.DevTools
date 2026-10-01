using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer
    {
        private const int DefaultMaxNodes = 5000;

        private object GetStatus()
        {
            var roots = _tree.Roots.Select((item, index) => DescribeRoot(item, index)).ToArray();

            return new
            {
                endpoint = Endpoint,
                rootCount = roots.Length,
                roots,
                extendedCapabilities = _extendedCapabilitiesAllowed
            };
        }

        private object GetRoots()
        {
            return _tree.Roots.Select((item, index) => DescribeRoot(item, index)).ToArray();
        }

        private static object DescribeRoot(TopLevel root, int index)
        {
            return new
            {
                index,
                type = root.GetType().FullName,
                name = (root as INamed)?.Name,
                title = root is Window window ? window.Title : null,
                isVisible = root.IsVisible
            };
        }

        // ----- get_dom / get_tree -----

        private object GetDom(McpArgs args) => SnapshotTree(args, compact: false, defaultDepth: 6, toolName: "dom");

        private object GetTree(McpArgs args) => SnapshotTree(args, compact: true, defaultDepth: 4, toolName: "tree");

        private object SnapshotTree(McpArgs args, bool compact, int defaultDepth, string toolName)
        {
            var tree = NodeTree.ParseTree(args.String("tree"));
            var maxDepth = Math.Clamp(args.Int("maxDepth") ?? defaultDepth, 0, 64);
            var maxNodes = Math.Clamp(args.Int("maxNodes") ?? DefaultMaxNodes, 1, 100_000);
            var filter = NodeFilter.From(args.Object("filter"));
            var path = args.String("path") ?? string.Empty;
            var rootIndex = args.Int("rootIndex");
            var output = ParseOutput(args);

            var starts = new List<NodeTree.Target>();
            if (rootIndex is null && string.IsNullOrWhiteSpace(path))
            {
                var roots = _tree.Roots;
                for (var i = 0; i < roots.Length; i++)
                {
                    starts.Add(_tree.Resolve(tree, i, string.Empty));
                }
            }
            else
            {
                starts.Add(_tree.Resolve(tree, rootIndex ?? 0, path));
            }

            var results = starts.Select(start => BuildSnapshot(start, maxDepth, maxNodes, filter, compact)).ToList();
            object payload = rootIndex is null && string.IsNullOrWhiteSpace(path) ? results : results[0];

            if (output != OutputMode.File)
            {
                return payload;
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            var file = WriteTempFile(toolName, ".json", json);
            return new
            {
                path = file,
                bytes = json.Length,
                nodeCount = results.Sum(r => (int)r["nodeCount"]!),
                truncated = results.Any(r => (bool)r["truncated"]!),
                depthLimited = results.Any(r => (bool)r["depthLimited"]!)
            };
        }

        private Dictionary<string, object?> BuildSnapshot(NodeTree.Target start, int maxDepth, int maxNodes, NodeFilter filter, bool compact)
        {
            var truncated = false;
            var depthLimited = false;
            var count = 0;
            Dictionary<string, object?> root;

            if (filter.IsEmpty)
            {
                // Breadth-first, so a node budget keeps the upper levels of every branch rather than one deep branch.
                root = MakeNode(start.Node, start.Path, start, compact);
                count = 1;
                var queue = new Queue<(AvaloniaObject Node, Dictionary<string, object?> Dto, string Path, int Depth)>();
                queue.Enqueue((start.Node, root, start.Path, 0));

                while (queue.Count > 0)
                {
                    var (node, dto, path, depth) = queue.Dequeue();
                    var children = NodeTree.GetChildren(node, start.Tree).ToArray();
                    var list = new List<Dictionary<string, object?>>();
                    dto["children"] = list;

                    if (children.Length > 0 && depth >= maxDepth)
                    {
                        dto["childrenTruncated"] = true;
                        depthLimited = true;
                        continue;
                    }

                    for (var i = 0; i < children.Length; i++)
                    {
                        if (count >= maxNodes)
                        {
                            dto["childrenTruncated"] = true;
                            truncated = true;
                            break;
                        }

                        var childPath = NodeTree.AppendPath(path, i);
                        var childDto = MakeNode(children[i], childPath, start, compact, node);
                        list.Add(childDto);
                        count++;
                        queue.Enqueue((children[i], childDto, childPath, depth + 1));
                    }
                }
            }
            else
            {
                // Filtered: keep the matches plus their ancestors, in document order.
                var keep = new Dictionary<string, (AvaloniaObject Node, bool Matched)>();
                keep[start.Path] = (start.Node, filter.Matches(start.Node));
                var ancestors = new List<(AvaloniaObject Node, string Path)>();

                void Visit(AvaloniaObject node, string path, int depth)
                {
                    if (truncated)
                    {
                        return;
                    }

                    var isStart = depth == 0;
                    var matched = filter.Matches(node);
                    if (matched && !isStart)
                    {
                        var missing = ancestors.Where(a => !keep.ContainsKey(a.Path)).ToList();
                        if (keep.Count + missing.Count + 1 > maxNodes)
                        {
                            truncated = true;
                            return;
                        }

                        foreach (var ancestor in missing)
                        {
                            keep[ancestor.Path] = (ancestor.Node, false);
                        }

                        keep[path] = (node, true);
                    }

                    var children = NodeTree.GetChildren(node, start.Tree).ToArray();
                    if (depth >= maxDepth)
                    {
                        depthLimited |= children.Length > 0;
                        return;
                    }

                    ancestors.Add((node, path));
                    for (var i = 0; i < children.Length; i++)
                    {
                        Visit(children[i], NodeTree.AppendPath(path, i), depth + 1);
                    }

                    ancestors.RemoveAt(ancestors.Count - 1);
                }

                Visit(start.Node, start.Path, 0);

                var dtos = new Dictionary<string, Dictionary<string, object?>>();
                root = MakeNode(start.Node, start.Path, start, compact);
                root["matched"] = keep[start.Path].Matched;
                root["children"] = new List<Dictionary<string, object?>>();
                dtos[start.Path] = root;

                foreach (var (path, entry) in keep)
                {
                    if (path == start.Path)
                    {
                        continue;
                    }

                    var parentPath = path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;
                    var dto = MakeNode(entry.Node, path, start, compact);
                    dto["matched"] = entry.Matched;
                    dto["children"] = new List<Dictionary<string, object?>>();
                    dtos[path] = dto;
                    ((List<Dictionary<string, object?>>)dtos[parentPath]["children"]!).Add(dto);
                }

                count = keep.Count;
            }

            root["nodeCount"] = count;
            root["truncated"] = truncated;
            root["depthLimited"] = depthLimited;
            return root;
        }

        private Dictionary<string, object?> MakeNode(AvaloniaObject node, string path, NodeTree.Target start, bool compact, AvaloniaObject? parent = null)
        {
            var classes = node is StyledElement styled ? styled.Classes.ToArray() : Array.Empty<string>();
            var bounds = node is Visual visual ? visual.Bounds.ToString() : null;
            var childCount = NodeTree.GetChildren(node, start.Tree).Count();

            if (compact)
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = node.GetType().FullName,
                    ["name"] = (node as INamed)?.Name,
                    ["classes"] = classes,
                    ["bounds"] = bounds,
                    ["path"] = path,
                    ["childCount"] = childCount
                };
            }

            var dto = new Dictionary<string, object?>
            {
                ["domId"] = NodeTree.CreateDomId(start.RootIndex, path),
                ["rootIndex"] = start.RootIndex,
                ["path"] = path,
                ["tree"] = start.Tree,
                ["type"] = node.GetType().FullName,
                ["name"] = (node as INamed)?.Name,
                ["text"] = NodeInfo.GetText(node),
                ["classes"] = classes,
                ["bounds"] = bounds,
                ["isVisible"] = node is Visual { IsVisible: var isVisible } ? isVisible : null as bool?,
                ["isEnabled"] = node is InputElement { IsEnabled: var isEnabled } ? isEnabled : null as bool?,
                ["state"] = NodeInfo.GetState(node),
                ["childCount"] = childCount
            };

            if (node is StyledElement { DataContext: { } dataContext } element)
            {
                parent ??= tree_ParentOf(element, start.Tree);
                if (parent is not StyledElement { DataContext: { } parentContext } || !ReferenceEquals(parentContext, dataContext))
                {
                    dto["dataContextType"] = dataContext.GetType().FullName;
                }
            }

            return dto;
        }

        private static AvaloniaObject? tree_ParentOf(StyledElement element, string tree)
        {
            return tree == "logical" ? element.GetLogicalParent() as AvaloniaObject : (element as Visual)?.GetVisualParent() as AvaloniaObject;
        }

        // ----- find -----

        private object Find(McpArgs args)
        {
            var tree = NodeTree.ParseTree(args.String("tree"));
            var maxDepth = Math.Clamp(args.Int("maxDepth") ?? 64, 0, 64);
            var limit = Math.Clamp(args.Int("limit") ?? 50, 1, 1000);
            var filter = NodeFilter.From(args.Object("filter"), args.String("query"));
            var path = args.String("path") ?? string.Empty;
            var rootIndex = args.Int("rootIndex");

            if (filter.IsEmpty)
            {
                throw new McpToolException("Provide a non-empty `query` and/or `filter`.");
            }

            var starts = new List<NodeTree.Target>();
            if (rootIndex is null && string.IsNullOrWhiteSpace(path))
            {
                for (var i = 0; i < _tree.Roots.Length; i++)
                {
                    starts.Add(_tree.Resolve(tree, i, string.Empty));
                }
            }
            else
            {
                starts.Add(_tree.Resolve(tree, rootIndex ?? 0, path));
            }

            var matches = new List<object>();
            var truncated = false;
            foreach (var start in starts)
            {
                var found = _tree.Find(start, filter, maxDepth, limit - matches.Count + 1, out _);
                foreach (var match in found)
                {
                    if (matches.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }

                    matches.Add(new
                    {
                        domId = NodeTree.CreateDomId(start.RootIndex, match.Path),
                        rootIndex = start.RootIndex,
                        path = match.Path,
                        tree,
                        type = match.Node.GetType().FullName,
                        name = (match.Node as INamed)?.Name,
                        text = NodeInfo.GetText(match.Node)
                    });
                }

                if (truncated)
                {
                    break;
                }
            }

            return new { count = matches.Count, truncated, matches };
        }

        // ----- reads -----

        private static readonly HashSet<string> SafeSensitiveProperties = new(StringComparer.Ordinal)
        {
            "IsVisible", "IsEnabled", "IsFocused", "IsReadOnly", "MaxLength", "PasswordChar", "Name", "$type", "IsEffectivelyVisible", "IsEffectivelyEnabled"
        };

        private object GetProperty(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var name = args.RequiredString("propertyName").Trim();
            var resolvedName = name switch
            {
                "DataContextType" => "DataContext.$type",
                "ItemsSourceCount" => "ItemsSource.Count",
                _ => name
            };

            if (NodeInfo.IsSensitive(target.Node) && !SafeSensitiveProperties.Contains(resolvedName))
            {
                return new { target = target.DomId, propertyName = name, type = (string?)null, value = (object?)NodeInfo.Redacted, redacted = true };
            }

            var read = ValueReader.Read(target.Node, resolvedName);
            if (!read.Found)
            {
                throw new McpToolException($"{read.Error} (target {target.DomId}: {target.Node.GetType().FullName})");
            }

            if (read.IsHidden)
            {
                return new { target = target.DomId, propertyName = name, type = (string?)null, value = (object?)NodeInfo.Redacted, redacted = true };
            }

            return new
            {
                target = target.DomId,
                propertyName = name,
                type = read.Value?.GetType().FullName,
                value = ValueReader.Describe(read.Value),
                redacted = false
            };
        }

        private object GetDataContext(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var context = (target.Node as StyledElement)?.DataContext;
            if (context is null)
            {
                return new { target = target.DomId, hasDataContext = false };
            }

            var properties = args.StringList("properties");
            if (properties.Count == 0)
            {
                return new
                {
                    target = target.DomId,
                    hasDataContext = true,
                    typeName = context.GetType().FullName,
                    availableProperties = ValueReader.ReadablePropertyNames(context.GetType())
                };
            }

            var subtreeRedacted = target.Node.GetValue(McpRedaction.IsSensitiveProperty);
            var secrets = new HashSet<string>(StringComparer.Ordinal);
            if (target.Node is Visual visual)
            {
                foreach (var box in visual.GetSelfAndVisualDescendants().OfType<TextBox>())
                {
                    if (NodeInfo.IsSensitive(box) && !string.IsNullOrEmpty(box.Text))
                    {
                        secrets.Add(box.Text);
                    }
                }
            }

            var values = new Dictionary<string, object?>();
            foreach (var property in properties)
            {
                if (subtreeRedacted)
                {
                    values[property] = new { value = (object?)NodeInfo.Redacted, redacted = true };
                    continue;
                }

                var read = ValueReader.Read(context, property);
                if (!read.Found)
                {
                    values[property] = new { error = read.Error };
                }
                else if (read.IsHidden || (read.Value is string text && secrets.Contains(text)))
                {
                    values[property] = new { value = (object?)NodeInfo.Redacted, redacted = true };
                }
                else
                {
                    values[property] = new { type = read.Value?.GetType().FullName, value = ValueReader.Describe(read.Value), redacted = false };
                }
            }

            return new { target = target.DomId, hasDataContext = true, typeName = context.GetType().FullName, properties = values };
        }

        private object GetItems(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var maxItems = Math.Clamp(args.Int("maxItems") ?? 200, 1, 2000);

            if (NodeInfo.IsSensitive(target.Node))
            {
                return new { target = target.DomId, redacted = true, items = Array.Empty<object>() };
            }

            if (target.Node is DataGrid grid)
            {
                var columns = DataGridReader.VisibleColumns(grid)
                    .Select(c => new { index = grid.Columns.IndexOf(c), header = DataGridReader.ColumnHeader(c), isReadOnly = c.IsReadOnly })
                    .ToArray();
                var rows = DataGridReader.RealizedRows(grid).Take(maxItems).Select(row => new
                {
                    index = row.Index,
                    isSelected = row.IsSelected,
                    isEditing = DataGridReader.IsRowEditing(row),
                    cells = DataGridReader.ReadRow(grid, row).Select(cell => new
                    {
                        column = cell.ColumnIndex,
                        header = cell.Header,
                        text = cell.Text,
                        realized = cell.Realized,
                        redacted = cell.Redacted
                    }).ToArray()
                }).ToArray();

                return new
                {
                    target = target.DomId,
                    kind = "DataGrid",
                    totalItems = DataGridReader.ItemCount(grid),
                    selectedIndex = grid.SelectedIndex,
                    columns,
                    items = rows,
                    note = "Only realised (visible) rows are listed."
                };
            }

            if (target.Node is ItemsControl itemsControl)
            {
                var selecting = itemsControl as SelectingItemsControl;
                var items = new List<object>();
                var index = 0;
                foreach (var item in itemsControl.Items)
                {
                    if (items.Count >= maxItems)
                    {
                        break;
                    }

                    items.Add(new
                    {
                        index,
                        text = NodeInfo.ItemText(itemsControl, item),
                        isSelected = selecting is not null && selecting.SelectedIndex == index,
                        type = item?.GetType().FullName
                    });
                    index++;
                }

                return new
                {
                    target = target.DomId,
                    kind = itemsControl.GetType().Name,
                    totalItems = itemsControl.ItemCount,
                    selectedIndex = selecting?.SelectedIndex,
                    items,
                    truncated = itemsControl.ItemCount > items.Count
                };
            }

            throw new McpToolException($"{NodeTree.Describe(target.Node)} is not an ItemsControl or DataGrid; devtools_get_items cannot list its items.");
        }

        // ----- screenshot -----

        private object CaptureScreenshot(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var output = ParseOutput(args);
            var scale = Math.Clamp(args.Double("scale") ?? 1, 0.05, 4);
            var dpi = (args.Double("dpi") ?? 96) * scale;

            if (target.Node is not Control control)
            {
                throw new McpToolException("The target node is not a Control and cannot be rendered.");
            }

            if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            {
                throw new McpToolException($"{NodeTree.Describe(control)} has zero size ({control.Bounds}); it is not laid out or not visible.");
            }

            Rect? region = null;
            if (args.Object("region") is { } regionArgs)
            {
                var requested = new Rect(
                    regionArgs.Double("x") ?? 0,
                    regionArgs.Double("y") ?? 0,
                    regionArgs.Double("width") ?? control.Bounds.Width,
                    regionArgs.Double("height") ?? control.Bounds.Height);
                var clipped = requested.Intersect(new Rect(control.Bounds.Size));
                if (clipped.Width <= 0 || clipped.Height <= 0)
                {
                    throw new McpToolException($"region {requested} does not intersect the control, whose size is {control.Bounds.Size}.");
                }

                region = clipped;
            }

            using var stream = new MemoryStream();
            control.RenderTo(stream, dpi, region, out var pixelSize);
            var bytes = stream.ToArray();
            if (bytes.Length == 0)
            {
                throw new McpToolException("The control could not be rendered (it may not be attached to a visual root).");
            }

            if (output == OutputMode.File)
            {
                var file = WriteTempFile("screenshot", ".png", bytes);
                return new { target = target.DomId, mimeType = "image/png", path = file, bytes = bytes.Length, width = pixelSize.Width, height = pixelSize.Height };
            }

            return new
            {
                target = target.DomId,
                mimeType = "image/png",
                width = pixelSize.Width,
                height = pixelSize.Height,
                base64 = Convert.ToBase64String(bytes)
            };
        }

        // ----- file output -----

        private enum OutputMode
        {
            Inline,
            File
        }

        private OutputMode ParseOutput(McpArgs args)
        {
            var output = args.String("output") ?? "inline";
            if (string.Equals(output, "inline", StringComparison.OrdinalIgnoreCase))
            {
                return OutputMode.Inline;
            }

            if (!string.Equals(output, "file", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException($"Unknown output '{output}'. Use 'inline' or 'file'.");
            }

            if (!Extended(_options.EnableFileOutput))
            {
                throw new McpToolException("output=\"file\" requires McpServerOptions.EnableFileOutput (and is unavailable in optimized builds unless AllowInReleaseBuilds is set).");
            }

            return OutputMode.File;
        }

        private static string WriteTempFile(string prefix, string extension, byte[] bytes)
        {
            var directory = Path.Combine(Path.GetTempPath(), "inprocess-devtools-mcp");
            Directory.CreateDirectory(directory);

            // Keep the folder small: drop files from earlier sessions.
            foreach (var old in Directory.EnumerateFiles(directory).Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-24)))
            {
                try
                {
                    File.Delete(old);
                }
                catch
                {
                }
            }

            var file = Path.Combine(directory, $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}{extension}");
            File.WriteAllBytes(file, bytes);
            return file;
        }
    }
}
