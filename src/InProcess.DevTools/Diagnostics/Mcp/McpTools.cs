using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer
    {
        private const string PathConventions =
            "Addressing: a node is identified by (tree, rootIndex, path). rootIndex is the top-level index from devtools_list_roots and is NOT part of path. " +
            "path is a slash-separated list of zero-based child indexes below that root, in the chosen tree: \"\" is the root itself, \"0\" is its first child, \"0/2/1\" is child 1 of child 2 of child 0. " +
            "The visual tree (default) and the logical tree number their children differently, so always pass the same tree that produced the path. " +
            "Take paths from devtools_get_dom, devtools_get_tree or devtools_find.";

        private const string WaitIdleNote = " Pass wait_idle=true to also wait for the dispatcher to drain after the action (see devtools_wait_idle).";

        private IReadOnlyList<ToolDefinition> BuildTools()
        {
            var tools = new List<ToolDefinition>();

            void Add(string name, string description, Dictionary<string, object> properties, string[] required, Func<bool> enabled,
                Func<McpArgs, Task<object?>> handler, bool isAction = false)
            {
                if (isAction)
                {
                    properties["wait_idle"] = Prop("boolean", "If true, wait for the UI dispatcher to drain after the action (requires EnableWaiting).");
                    properties["waitIdleTimeoutMs"] = Prop("integer", "Timeout for wait_idle in milliseconds. Default 5000, maximum 60000.");
                    description += WaitIdleNote;
                }

                tools.Add(new ToolDefinition(name, description, Schema(properties, required), enabled, handler, isAction));
            }

            Add("devtools_get_status", "Returns the InProcess.DevTools MCP endpoint and attached Avalonia root summary.",
                Props(), Array.Empty<string>(), () => true, Ui(_ => GetStatus()));

            // ----- inspection -----
            Func<bool> dom = () => _options.EnableDomInspection;

            Add("devtools_list_roots", "Lists attached Avalonia top-level roots available for inspection. The index is the rootIndex used by every other tool.",
                Props(), Array.Empty<string>(), dom, Ui(_ => GetRoots()));

            Add("devtools_get_dom",
                "Returns a DOM-like snapshot with rootIndex/path selectors, bounds, text, classes, control state and childCount. " + PathConventions + " " +
                "Use `path` to start at any node and get a sub-tree with its own maxDepth budget (maxDepth counts from that node), so deep panels never need a huge global depth. " +
                "`filter` prunes the result to matching nodes plus their ancestors (matched nodes carry matched=true). " +
                "Result flags: `truncated` (maxNodes reached) and `depthLimited` (some nodes have children cut by maxDepth; those nodes have childrenTruncated=true and a childCount > 0). " +
                "Password boxes and nodes marked with McpRedaction.IsSensitive report redacted text. With output=\"file\" the JSON is written to a temp file and {path, bytes, nodeCount, truncated} is returned.",
                TreeProps(withMaxDepth: "Maximum depth below the start node. Default 6, maximum 64.", defaultDepth: 6), Array.Empty<string>(), dom, Ui(GetDom));

            Add("devtools_get_tree", "Returns a compact read-only snapshot (type, name, classes, bounds, path, childCount) of the visual or logical tree. Supports the same path, filter, maxDepth, maxNodes and output options as devtools_get_dom. " + PathConventions,
                TreeProps(withMaxDepth: "Maximum depth below the start node. Default 4, maximum 64.", defaultDepth: 4), Array.Empty<string>(), dom, Ui(GetTree));

            Add("devtools_find",
                "Finds nodes and returns their rootIndex/path. `query` is a case-insensitive substring matched against Name, type name and text; or \"#Name\" for an exact Name, \"type:Button\" for a type (base types match too), \"text:Save\" for text. " +
                "`filter` adds structured constraints. Searches all roots unless rootIndex is given; `path` limits the search to a sub-tree. " + PathConventions,
                Merge(TargetProps(rootDefault: null), Props(
                    ("query", Prop("string", "Free text or #Name / type:X / text:X query.")),
                    ("filter", FilterSchema()),
                    ("maxDepth", Prop("integer", "Maximum depth below the start node. Default 64, maximum 64.")),
                    ("limit", Prop("integer", "Maximum number of matches. Default 50, maximum 1000.")))),
                Array.Empty<string>(), dom, Ui(Find));

            Add("devtools_capture_screenshot",
                "Captures a PNG screenshot of a target control. Returns base64 inline by default; with output=\"file\" the PNG is written to a temp file and {path, bytes, width, height} is returned instead (recommended: inline images can exceed client limits). " +
                "Use `scale` to shrink or enlarge the image and `region` to crop to part of the control. Rendered pixels are returned as shown on screen: password boxes render their mask character, but content of controls marked with McpRedaction.IsSensitive is not hidden. " + PathConventions,
                Merge(TargetProps(), Props(
                    ("dpi", Prop("number", "Base DPI. Default 96.", 96)),
                    ("scale", Prop("number", "Multiplier applied to the DPI (0.05 to 4). Default 1.", 1)),
                    ("region", RegionSchema()),
                    ("output", OutputProp("inline")))),
                Array.Empty<string>(), () => _options.EnableScreenshots, Ui(CaptureScreenshot));

            Func<bool> values = () => Extended(_options.EnableValueInspection);

            Add("devtools_get_property",
                "Reads a public CLR property of the node at the given address. propertyName may be dotted (e.g. \"SelectedItem.Name\", \"DataContext.Title\"); special segments: `Count` on any collection (e.g. \"ItemsSource.Count\") and `$type` for the type full name. " +
                "Aliases: DataContextType (= DataContext.$type) and ItemsSourceCount (= ItemsSource.Count). Works for SelectedItem, SelectedValue, SelectedIndex, Text, IsChecked, etc. " +
                "Returns {value, type, redacted}. Complex values are returned as {type, text} or {type, count}. Password boxes, McpRedaction.IsSensitive subtrees and properties whose name looks secret (password, secret, token, apikey, ...) always return \"" + NodeInfo.Redacted + "\". " + PathConventions,
                Merge(TargetProps(), Props(("propertyName", Prop("string", "Property name or dotted path.")))),
                new[] { "propertyName" }, values, Ui(GetProperty));

            Add("devtools_get_items",
                "Returns the visible/realised items of an ItemsControl, ComboBox, ListBox, TabControl or DataGrid with their display text. For a DataGrid every realised row lists one entry per column {column, header, text}; " +
                "ComboBox/ListBox items are listed even when the popup is closed (text comes from the realised container, DisplayMemberBinding or ToString). Secret cells and sensitive controls are redacted. " + PathConventions,
                Merge(TargetProps(), Props(("maxItems", Prop("integer", "Maximum rows/items to return. Default 200, maximum 2000.", 200)))),
                Array.Empty<string>(), values, Ui(GetItems));

            Add("devtools_get_datacontext",
                "Returns the DataContext (view-model) type of the node and the values of the named public view-model properties. propertyNames may be dotted. Omit `properties` to list the readable property names instead. " +
                "Properties with secret-looking names, values equal to the text of a password box in the sub-tree, and everything under McpRedaction.IsSensitive are redacted. " + PathConventions,
                Merge(TargetProps(), Props(("properties", Prop("array", "Names of view-model properties to read.", items: new { type = "string" })))),
                Array.Empty<string>(), values, Ui(GetDataContext));

            // ----- navigation / interaction -----
            Func<bool> nav = () => _options.EnableNavigation;
            Func<bool> navExtended = () => Extended(_options.EnableNavigation);

            Add("devtools_focus", "Moves keyboard focus to a target control. " + PathConventions,
                TargetProps(), Array.Empty<string>(), nav, Ui(Focus), isAction: true);

            Add("devtools_click",
                "Simulates a user click on the target: Button/ToggleButton/CheckBox (executes Command and toggles), MenuItem, TabItem (selects the tab), ListBoxItem/ComboBoxItem/TreeViewItem (selects the item), " +
                "DataGridRow/DataGridCell/DataGridRowHeader (selects the row, and the column for a cell). Any other Control is focused. " + PathConventions,
                TargetProps(), Array.Empty<string>(), nav, Ui(Click), isAction: true);

            Add("devtools_double_click",
                "Simulates a double click: selects the target like devtools_click and raises DoubleTapped. On a DataGridCell of an editable, non-read-only column it also starts editing that cell (like a user double click). " +
                "Afterwards the editing control can be found with devtools_get_dom/devtools_find. " + PathConventions,
                TargetProps(), Array.Empty<string>(), navExtended, Ui(DoubleClick), isAction: true);

            Add("devtools_raise_event", "Raises a supported UI event on a target. Supported eventName values are click and focus. " + PathConventions,
                Merge(TargetProps(), Props(("eventName", Prop("string", "Supported values: click, focus.", "click")))),
                Array.Empty<string>(), () => _options.EnableEvents, Ui(RaiseSupportedEvent), isAction: true);

            Add("devtools_set_property",
                "Sets a writable public CLR property on a target Avalonia object. By default this behaves like code setting the property. " +
                "With raise_events=true (requires EnableMutationEvents) the change notifications a user interaction would raise are guaranteed: SelectionChanged for SelectedIndex/SelectedItem/SelectedValue/IsSelected on selecting controls and DataGrid, TextChanged for TextBox.Text, IsCheckedChanged for ToggleButton.IsChecked; " +
                "events the framework already raised are not duplicated. The response lists eventsRaised. Values of sensitive controls are never echoed. " + PathConventions,
                Merge(TargetProps(), Props(
                    ("propertyName", Prop("string", "Writable public CLR property name, for example Text, IsChecked, SelectedIndex, or Value.")),
                    ("value", new Dictionary<string, object> { ["description"] = "New property value." }),
                    ("raise_events", Prop("boolean", "Fire the same change notifications as user input. Default false.", false)))),
                new[] { "propertyName" }, () => _options.EnableStateMutation, Ui(SetProperty), isAction: true);

            Add("devtools_select_item",
                "Selects an item like a user would, for ComboBox, ListBox, TabControl (or a TabItem/ListBoxItem path, which selects within its parent) and DataGrid (selects the row). Raises SelectionChanged and updates two-way bindings. " +
                "Provide exactly one of: `index` (zero-based), `value` (matches SelectedValueBinding/SelectedValuePath, or the item itself) or `text` (display text, case-insensitive; the first match wins and the number of matches is returned). " +
                "A ComboBox popup is closed after selecting. Disabled tabs/items cannot be selected. " + PathConventions,
                Merge(TargetProps(), Props(
                    ("index", Prop("integer", "Zero-based item index.")),
                    ("value", new Dictionary<string, object> { ["description"] = "Item value to match." }),
                    ("text", Prop("string", "Item display text to match.")))),
                Array.Empty<string>(), () => Extended(_options.EnableSelection), Ui(SelectItem), isAction: true);

            Add("devtools_invoke_command",
                "Executes an ICommand exposed by a control property (default \"Command\"), passing the control's matching parameter property (CommandParameter, or <Property>Parameter). The command's CanExecute is honoured; the call fails if it returns false. " + PathConventions,
                Merge(TargetProps(), Props(("commandProperty", Prop("string", "Name of the ICommand property on the control. Default \"Command\".", "Command")))),
                Array.Empty<string>(), () => Extended(_options.EnableCommands), Ui(InvokeCommand), isAction: true);

            // ----- DataGrid editing -----
            Func<bool> editing = () => Extended(_options.EnableEditing);

            Add("devtools_begin_edit",
                "Starts editing a DataGrid cell so its CellEditingTemplate (ComboBox, TextBox, ...) is created. `path` addresses the DataGrid (or a node inside it); `row` is the zero-based data row index; `column` is a zero-based column index or the column header text/bound property name. " +
                "The response contains editingPath: the path (same tree and rootIndex) of the editing control, which is also visible to devtools_get_dom/devtools_find from now on. Fails if the grid or column is read-only. " + PathConventions,
                Merge(TargetProps(), Props(
                    ("row", Prop("integer", "Zero-based data row index.")),
                    ("column", new Dictionary<string, object> { ["description"] = "Column index (integer) or header text / bound property name (string)." }))),
                new[] { "row", "column" }, editing, Ui(BeginEdit), isAction: true);

            Add("devtools_commit_edit",
                "Commits the current DataGrid edit (bindings write back to the view-model; validation runs). `path` may be the DataGrid or any node inside it, such as the editing control. Returns committed=false when validation rejected the edit (the cell stays in edit mode). " + PathConventions,
                Merge(TargetProps(), Props(("unit", Prop("string", "\"row\" (default) commits the whole row, \"cell\" only the cell.", "row")))),
                Array.Empty<string>(), editing, Ui(CommitEdit), isAction: true);

            Add("devtools_cancel_edit",
                "Cancels the current DataGrid edit, discarding uncommitted changes. `path` may be the DataGrid or any node inside it. " + PathConventions,
                Merge(TargetProps(), Props(("unit", Prop("string", "\"row\" (default) or \"cell\".", "row")))),
                Array.Empty<string>(), editing, Ui(CancelEdit), isAction: true);

            // ----- keyboard / text input -----
            Func<bool> input = () => Extended(_options.EnableInput);

            Add("devtools_send_keys",
                "Sends keyboard input as the routed events a keyboard raises (KeyDown, TextInput, KeyUp) on the element that has focus at each step. Omit `path` (or pass focused=true) to use the currently focused element of the root; otherwise the target is focused first. " +
                "`keys` is plain text typed character by character, with special keys and chords in braces: {Enter} {Tab} {Escape} {F2} {Up} {Down} {Left} {Right} {Home} {End} {PageUp} {PageDown} {Backspace} {Delete} {Space} {F1}..{F12}, " +
                "chords such as {Ctrl+A} {Shift+Tab} {Alt+Down} {Cmd+A} (modifiers: Ctrl, Shift, Alt, Meta/Cmd/Win), and `{{` / `}}` for literal braces. Example: \"hello{Tab}world{Enter}\". Platform shortcuts differ (select-all is Cmd+A on macOS). " + PathConventions,
                Merge(TargetProps(), Props(
                    ("keys", Prop("string", "Text and {Key} tokens to send.")),
                    ("focused", Prop("boolean", "Send to the currently focused element instead of a path.")))),
                new[] { "keys" }, input, Ui(SendKeys), isAction: true);

            Add("devtools_type_text",
                "Types text into the target through real text input events, so bindings, TextChanged and validation fire as with a user. The target is focused first. Set clear=true to replace the existing text of a TextBox. " +
                "Does not interpret {Key} tokens (use devtools_send_keys for those). " + PathConventions,
                Merge(TargetProps(), Props(
                    ("text", Prop("string", "Text to type.")),
                    ("clear", Prop("boolean", "Select all existing text first so the typed text replaces it. Default false.", false)))),
                new[] { "text" }, input, Ui(TypeText), isAction: true);

            // ----- timing -----
            Add("devtools_wait_idle",
                "Waits until the UI dispatcher has drained (layout done, queued UI work processed, then quiet for settleMs) and all `conditions` hold, or timeoutMs elapses. Use it after actions that start async view-model work (loading schemas, object types, ...). " +
                "A dispatcher that is merely idle cannot know about work still running on background threads, so pass conditions for such loads. " +
                "Each condition is {kind, ...} where kind is one of: \"exists\" / \"not_exists\" / \"visible\" (target by `path` [+ tree, rootIndex] or by `query`/`filter` as in devtools_find), or \"property_equals\" ({path, propertyName, value}; compared as text, case-insensitive; sensitive properties are rejected). " +
                "Returns {idle, satisfied, timedOut, elapsedMs, conditions:[{index, kind, satisfied}]}; a timeout is reported with timedOut=true rather than as an error. " + PathConventions,
                Props(
                    ("timeoutMs", Prop("integer", "Overall timeout. Default 5000, maximum 60000.", 5000)),
                    ("settleMs", Prop("integer", "How long the dispatcher must stay drained. Default 100, maximum 5000.", 100)),
                    ("conditions", Prop("array", "Conditions that must all hold.", items: new { type = "object" }))),
                Array.Empty<string>(), () => Extended(_options.EnableWaiting), WaitIdle);

            return tools;
        }

        // ----- schema helpers -----

        private static Dictionary<string, object> Props(params (string Name, object Schema)[] properties)
        {
            return properties.ToDictionary(p => p.Name, p => p.Schema);
        }

        private static Dictionary<string, object> Merge(Dictionary<string, object> first, Dictionary<string, object> second)
        {
            var result = new Dictionary<string, object>(first);
            foreach (var pair in second)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static Dictionary<string, object> Prop(string type, string description, object? @default = null, object? items = null)
        {
            var schema = new Dictionary<string, object> { ["type"] = type, ["description"] = description };
            if (@default is not null)
            {
                schema["default"] = @default;
            }

            if (items is not null)
            {
                schema["items"] = items;
            }

            return schema;
        }

        private static object Schema(Dictionary<string, object> properties, string[] required)
        {
            var schema = new Dictionary<string, object> { ["type"] = "object", ["properties"] = properties };
            if (required.Length > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        private static Dictionary<string, object> TargetProps(int? rootDefault = 0)
        {
            return new Dictionary<string, object>
            {
                ["tree"] = Prop("string", "Tree kind used to resolve path: visual or logical.", "visual"),
                ["rootIndex"] = Prop("integer", "Root index from devtools_list_roots." + (rootDefault is null ? " Omit to use all roots." : string.Empty), rootDefault),
                ["path"] = Prop("string", "Slash-delimited child indexes below the root, e.g. \"0/2/1\". Empty string targets the root itself.", string.Empty)
            };
        }

        private static Dictionary<string, object> TreeProps(string withMaxDepth, int defaultDepth)
        {
            var props = TargetProps(rootDefault: null);
            props["path"] = Prop("string", "Start node, relative to the root (see addressing). Default: the root. maxDepth counts from this node.", string.Empty);
            props["maxDepth"] = Prop("integer", withMaxDepth, defaultDepth);
            props["maxNodes"] = Prop("integer", "Maximum number of nodes to return (1..100000). Default 5000. When reached the result has truncated=true.", 5000);
            props["filter"] = FilterSchema();
            props["output"] = OutputProp("inline");
            return props;
        }

        private static Dictionary<string, object> FilterSchema()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["description"] = "Keep only matching nodes (and their ancestors for context). All given members must match.",
                ["properties"] = new Dictionary<string, object>
                {
                    ["types"] = Prop("array", "Type names (short or full); a node matches if it or a base type has that name.", items: new { type = "string" }),
                    ["name"] = Prop("string", "Exact Name of the node."),
                    ["textContains"] = Prop("string", "Case-insensitive substring of the node text."),
                    ["isVisible"] = Prop("boolean", "Match nodes whose effective visibility equals this."),
                    ["interactiveOnly"] = Prop("boolean", "Only buttons, inputs, selectors, tabs, menu items, grids, rows and other focusable controls.")
                }
            };
        }

        private static Dictionary<string, object> RegionSchema()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["description"] = "Crop rectangle in the control's own device-independent coordinates.",
                ["properties"] = new Dictionary<string, object>
                {
                    ["x"] = Prop("number", "Left."),
                    ["y"] = Prop("number", "Top."),
                    ["width"] = Prop("number", "Width."),
                    ["height"] = Prop("number", "Height.")
                }
            };
        }

        private static Dictionary<string, object> OutputProp(string @default)
        {
            return Prop("string", "\"inline\" returns the data in the response; \"file\" writes it to a temp file and returns {path, bytes, ...} (requires EnableFileOutput).", @default);
        }
    }
}
