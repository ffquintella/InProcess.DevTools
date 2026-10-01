namespace InProcess.DevTools
{
    /// <summary>
    /// Describes the localhost MCP endpoint exposed by DevTools.
    /// </summary>
    public class McpServerOptions
    {
        /// <summary>
        /// Gets or sets the MCP HTTP host. The default value binds only to localhost.
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Gets or sets the MCP HTTP port.
        /// </summary>
        public int Port { get; set; } = 43210;

        /// <summary>
        /// Gets or sets the MCP HTTP path.
        /// </summary>
        public string Path { get; set; } = "/mcp";

        /// <summary>
        /// Gets or sets whether DOM and tree inspection tools are exposed.
        /// </summary>
        public bool EnableDomInspection { get; set; } = true;

        /// <summary>
        /// Gets or sets whether screenshot tools are exposed.
        /// </summary>
        public bool EnableScreenshots { get; set; }

        /// <summary>
        /// Gets or sets whether focus and click navigation tools are exposed.
        /// </summary>
        public bool EnableNavigation { get; set; }

        /// <summary>
        /// Gets or sets whether supported event raising tools are exposed.
        /// </summary>
        public bool EnableEvents { get; set; }

        /// <summary>
        /// Gets or sets whether writable public CLR properties can be changed.
        /// </summary>
        public bool EnableStateMutation { get; set; }

        /// <summary>
        /// Gets or sets whether DataGrid cell editing tools are exposed
        /// (<c>devtools_begin_edit</c>, <c>devtools_commit_edit</c>, <c>devtools_cancel_edit</c>). Default: false.
        /// </summary>
        public bool EnableEditing { get; set; }

        /// <summary>
        /// Gets or sets whether keyboard and text input tools are exposed
        /// (<c>devtools_send_keys</c>, <c>devtools_type_text</c>). Default: false.
        /// </summary>
        public bool EnableInput { get; set; }

        /// <summary>
        /// Gets or sets whether user-like selection (<c>devtools_select_item</c>) is exposed. Default: false.
        /// </summary>
        public bool EnableSelection { get; set; }

        /// <summary>
        /// Gets or sets whether <c>devtools_set_property</c> accepts <c>raise_events</c> to fire the
        /// change notifications a user interaction would raise. Requires <see cref="EnableStateMutation"/>. Default: false.
        /// </summary>
        public bool EnableMutationEvents { get; set; }

        /// <summary>
        /// Gets or sets whether value-reading tools are exposed
        /// (<c>devtools_get_property</c>, <c>devtools_get_items</c>, <c>devtools_get_datacontext</c>).
        /// Password boxes and controls marked with <see cref="McpRedaction"/> are always redacted. Default: false.
        /// </summary>
        public bool EnableValueInspection { get; set; }

        /// <summary>
        /// Gets or sets whether <c>devtools_wait_idle</c> and the <c>wait_idle</c> option of action tools are available. Default: false.
        /// </summary>
        public bool EnableWaiting { get; set; }

        /// <summary>
        /// Gets or sets whether <c>devtools_invoke_command</c> is exposed. Default: false.
        /// </summary>
        public bool EnableCommands { get; set; }

        /// <summary>
        /// Gets or sets whether tools may write screenshots and JSON dumps to a temp file
        /// (<c>output: "file"</c>). Default: false.
        /// </summary>
        public bool EnableFileOutput { get; set; }

        /// <summary>
        /// Gets or sets whether the capabilities introduced after <see cref="EnableStateMutation"/>
        /// (editing, input, selection, mutation events, value inspection, waiting, commands, file output)
        /// may run when the host application was built with optimizations (a Release build).
        /// When false (the default) those capabilities are silently disabled in optimized builds even if their flag is set.
        /// </summary>
        public bool AllowInReleaseBuilds { get; set; }
    }
}
