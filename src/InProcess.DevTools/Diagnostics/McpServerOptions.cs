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
    }
}
