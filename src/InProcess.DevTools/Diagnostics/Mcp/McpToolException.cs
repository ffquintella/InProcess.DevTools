using System;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// A failure caused by the request (bad path, unsupported target, ...). It is reported to the
    /// client as a tool result with <c>isError=true</c> and the message as text.
    /// </summary>
    internal sealed class McpToolException : Exception
    {
        public McpToolException(string message) : base(message)
        {
        }
    }
}
