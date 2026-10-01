using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer
    {
        private object SendKeys(McpArgs args)
        {
            var keys = args.RequiredString("keys");
            var strokes = KeyParser.Parse(keys);

            var (control, root, description) = ResolveInputTarget(args);
            foreach (var stroke in strokes)
            {
                InputInjector.Send(root, stroke, (IInputElement)control!);
            }

            return new
            {
                target = description,
                sent = strokes.Count,
                focused = DescribeFocused(root)
            };
        }

        private object TypeText(McpArgs args)
        {
            var text = args.String("text") ?? throw new McpToolException("Argument 'text' is required.");
            var (control, root, description) = ResolveInputTarget(args, requireTarget: true);

            if (control is TextBox { IsReadOnly: true })
            {
                throw new McpToolException($"{description} is a read-only TextBox.");
            }

            if (args.Bool("clear") && control is TextBox textBox)
            {
                textBox.SelectAll();
            }

            foreach (var element in KeyParser.TextElements(text))
            {
                InputInjector.Send(root, new KeyStroke(Key.None, KeyModifiers.None, element), control!);
            }

            return new
            {
                target = description,
                typed = text.Length,
                text = control is TextBox typed ? (NodeInfo.IsSensitive(typed) ? NodeInfo.Redacted : typed.Text) : null
            };
        }

        private (Control? Control, TopLevel Root, string Description) ResolveInputTarget(McpArgs args, bool requireTarget = false)
        {
            var useFocused = args.Bool("focused") || (!requireTarget && !args.Has("path"));

            if (useFocused)
            {
                var root = _tree.GetRoot(args.Int("rootIndex") ?? 0);
                var focused = root.FocusManager?.GetFocusedElement() as Control;
                if (focused is null)
                {
                    throw new McpToolException("No element has keyboard focus in this root; pass `path` of a control to focus first (or use devtools_focus).");
                }

                var description = _tree.PathOf(focused, NodeTree.ParseTree(args.String("tree")), out var rootIndex) is { } p
                    ? NodeTree.CreateDomId(rootIndex, p)
                    : NodeTree.Describe(focused);
                return (focused, root, description);
            }

            var target = _tree.Resolve(args);
            var control = RequireControl(target);
            if (!control.Focus())
            {
                throw new McpToolException($"{NodeTree.Describe(control)} could not take keyboard focus (is it focusable, enabled and visible?).");
            }

            var topLevel = TopLevel.GetTopLevel(control) ?? target.Root;
            return (control, topLevel, target.DomId);
        }

        private static string? DescribeFocused(TopLevel root)
        {
            return root.FocusManager?.GetFocusedElement() is AvaloniaObject focused ? NodeTree.Describe(focused) : null;
        }
    }

    internal readonly record struct KeyStroke(Key Key, KeyModifiers Modifiers, string? Text);

    /// <summary>
    /// Parses the <c>keys</c> mini-language: plain text, <c>{Key}</c> and <c>{Modifier+Key}</c> tokens, <c>{{</c>/<c>}}</c> for braces.
    /// </summary>
    internal static class KeyParser
    {
        private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = Key.Escape,
            ["Return"] = Key.Enter,
            ["Backspace"] = Key.Back,
            ["Del"] = Key.Delete,
            ["Ins"] = Key.Insert,
            ["PgUp"] = Key.PageUp,
            ["PgDn"] = Key.PageDown,
            ["PageDown"] = Key.PageDown,
            ["Space"] = Key.Space,
            ["ArrowUp"] = Key.Up,
            ["ArrowDown"] = Key.Down,
            ["ArrowLeft"] = Key.Left,
            ["ArrowRight"] = Key.Right
        };

        private static readonly Dictionary<string, KeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = KeyModifiers.Control,
            ["Control"] = KeyModifiers.Control,
            ["Shift"] = KeyModifiers.Shift,
            ["Alt"] = KeyModifiers.Alt,
            ["Option"] = KeyModifiers.Alt,
            ["Meta"] = KeyModifiers.Meta,
            ["Cmd"] = KeyModifiers.Meta,
            ["Command"] = KeyModifiers.Meta,
            ["Win"] = KeyModifiers.Meta,
            ["Super"] = KeyModifiers.Meta
        };

        public static IEnumerable<string> TextElements(string text)
        {
            var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                yield return (string)enumerator.Current;
            }
        }

        public static List<KeyStroke> Parse(string keys)
        {
            var result = new List<KeyStroke>();
            var literal = new System.Text.StringBuilder();

            void FlushLiteral()
            {
                if (literal.Length == 0)
                {
                    return;
                }

                result.AddRange(TextElements(literal.ToString()).Select(e => new KeyStroke(Key.None, KeyModifiers.None, e)));
                literal.Clear();
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var c = keys[i];
                if (c == '{')
                {
                    if (i + 1 < keys.Length && keys[i + 1] == '{')
                    {
                        literal.Append('{');
                        i++;
                        continue;
                    }

                    var end = keys.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        throw new McpToolException($"keys: unterminated '{{' at position {i}. Use '{{{{' for a literal brace.");
                    }

                    FlushLiteral();
                    result.Add(ParseToken(keys.Substring(i + 1, end - i - 1)));
                    i = end;
                }
                else if (c == '}')
                {
                    if (i + 1 < keys.Length && keys[i + 1] == '}')
                    {
                        literal.Append('}');
                        i++;
                        continue;
                    }

                    throw new McpToolException($"keys: unmatched '}}' at position {i}. Use '}}}}' for a literal brace.");
                }
                else
                {
                    literal.Append(c);
                }
            }

            FlushLiteral();
            return result;
        }

        private static KeyStroke ParseToken(string token)
        {
            token = token.Trim();
            if (token.Length == 0)
            {
                throw new McpToolException("keys: empty {} token.");
            }

            if (token == "+")
            {
                return new KeyStroke(Key.None, KeyModifiers.None, "+");
            }

            var parts = token.Split('+', StringSplitOptions.TrimEntries);
            var modifiers = KeyModifiers.None;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!ModifierNames.TryGetValue(parts[i], out var modifier))
                {
                    throw new McpToolException($"keys: unknown modifier '{parts[i]}' in {{{token}}}. Use Ctrl, Shift, Alt or Meta/Cmd/Win.");
                }

                modifiers |= modifier;
            }

            var name = parts[^1];
            if (Aliases.TryGetValue(name, out var aliased))
            {
                return new KeyStroke(aliased, modifiers, null);
            }

            if (name.Length == 1 && char.IsDigit(name[0]) && Enum.TryParse<Key>("D" + name, out var digit))
            {
                return new KeyStroke(digit, modifiers, null);
            }

            if (Enum.TryParse<Key>(name, ignoreCase: true, out var key) && key != Key.None)
            {
                return new KeyStroke(key, modifiers, null);
            }

            throw new McpToolException($"keys: unknown key '{name}' in {{{token}}}. Examples: Enter, Tab, Escape, F2, Up, Down, Left, Right, Home, End, PageUp, Delete, Backspace, Space, A, D1.");
        }
    }

    /// <summary>
    /// Delivers keyboard input as the routed events a keyboard produces (KeyDown, TextInput, KeyUp), always to the element that
    /// currently has focus, so key handlers, text input, bindings and validation behave as with a real keyboard.
    /// </summary>
    internal static class InputInjector
    {
        public static void Send(TopLevel root, KeyStroke stroke, IInputElement fallback)
        {
            // Re-read the focus for every stroke: a previous {Tab} or {Enter} may have moved it.
            var target = root.FocusManager?.GetFocusedElement() ?? fallback;

            if (stroke.Text is { } text)
            {
                target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = target });
                return;
            }

            target.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = stroke.Key,
                KeyModifiers = stroke.Modifiers,
                Source = target
            });

            // The key may have moved focus (Tab) or closed the control (Escape); the release goes to the new focus owner.
            target = root.FocusManager?.GetFocusedElement() ?? target;
            target.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = stroke.Key,
                KeyModifiers = stroke.Modifiers,
                Source = target
            });
        }
    }
}
