using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// A tool an application registers for a config's tools. Either a <see cref="FunctionTool"/>
    /// the SDK calls, or a <see cref="NativeTool"/> the provider runs itself.
    /// </summary>
    public abstract class ToolHandler
    {
        private protected ToolHandler()
        {
        }
    }

    /// <summary>
    /// A tool implemented by the application.
    /// </summary>
    public sealed class FunctionTool : ToolHandler
    {
        /// <summary>
        /// Creates a function tool.
        /// </summary>
        /// <param name="function">Called with the tool arguments the model sent. Its result is sent
        /// back to the model as text.</param>
        public FunctionTool(Func<JsonElement, CancellationToken, Task<object?>> function)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// For the tracking stub the SDK builds from a <see cref="NativeTool"/>, that native tool, so a
        /// provider package can recover the provider's tool name. <see langword="null"/> otherwise.
        /// </summary>
        internal NativeTool? WrappedNativeTool => throw new NotImplementedException();

        /// <summary>
        /// Calls the tool.
        /// </summary>
        /// <param name="arguments">The arguments the model sent.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The tool's result.</returns>
        public Task<object?> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// A provider-executed (built-in) tool. The SDK never calls it and never offers it to the model
    /// as a function tool.
    /// </summary>
    public sealed class NativeTool : ToolHandler
    {
        /// <summary>
        /// Creates a native tool.
        /// </summary>
        /// <param name="toolName">The provider's name for the built-in tool.</param>
        public NativeTool(string toolName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The provider's name for the built-in tool.
        /// </summary>
        public string ToolName => throw new NotImplementedException();
    }
}
