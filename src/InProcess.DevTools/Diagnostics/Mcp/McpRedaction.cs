using Avalonia;

namespace InProcess.DevTools
{
    /// <summary>
    /// Opt-out switch for the MCP server: values of controls marked with
    /// <see cref="IsSensitiveProperty"/> (and of all their descendants) are always returned as redacted.
    /// <see cref="Avalonia.Controls.TextBox"/> controls that define a PasswordChar are always redacted too.
    /// </summary>
    public static class McpRedaction
    {
        /// <summary>
        /// Marks a control subtree as sensitive. The value is inherited by descendants.
        /// </summary>
        public static readonly AttachedProperty<bool> IsSensitiveProperty =
            AvaloniaProperty.RegisterAttached<AvaloniaObject, bool>("IsSensitive", typeof(McpRedaction), inherits: true);

        public static bool GetIsSensitive(AvaloniaObject element) => element.GetValue(IsSensitiveProperty);

        public static void SetIsSensitive(AvaloniaObject element, bool value) => element.SetValue(IsSensitiveProperty, value);
    }
}
