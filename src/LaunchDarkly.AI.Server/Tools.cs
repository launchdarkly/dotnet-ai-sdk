using System;
using System.Collections.Generic;
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
        private readonly Func<JsonElement, CancellationToken, Task<object?>> _function;

        /// <summary>
        /// Creates a function tool.
        /// </summary>
        /// <param name="function">Called with the tool arguments the model sent. Its result is sent
        /// back to the model as text.</param>
        /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
        public FunctionTool(Func<JsonElement, CancellationToken, Task<object?>> function)
            : this(function, null)
        {
        }

        private FunctionTool(Func<JsonElement, CancellationToken, Task<object?>> function, NativeTool? wrappedNativeTool)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            WrappedNativeTool = wrappedNativeTool;
        }

        /// <summary>
        /// For the tracking stub the SDK builds from a <see cref="NativeTool"/>, that native tool, so a
        /// provider package can recover the provider's tool name. <see langword="null"/> otherwise.
        /// </summary>
        internal NativeTool? WrappedNativeTool { get; }

        /// <summary>
        /// Calls the tool.
        /// </summary>
        /// <param name="arguments">The arguments the model sent.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The tool's result.</returns>
        public Task<object?> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
            _function(arguments, cancellationToken);

        /// <summary>
        /// Builds the stub that stands in for a native tool: calling it runs
        /// <paramref name="onCall"/> and returns <see langword="null"/>.
        /// </summary>
        internal static FunctionTool ForNativeTool(NativeTool native, Action onCall) =>
            new FunctionTool(
                (_, _) =>
                {
                    onCall();
                    return Task.FromResult<object?>(null);
                },
                native);
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
        /// <exception cref="ArgumentNullException"><paramref name="toolName"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="toolName"/> is empty or blank.</exception>
        public NativeTool(string toolName)
        {
            if (toolName == null)
            {
                throw new ArgumentNullException(nameof(toolName));
            }

            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new ArgumentException("A native tool needs the provider's tool name.", nameof(toolName));
            }

            ToolName = toolName;
        }

        /// <summary>
        /// The provider's name for the built-in tool.
        /// </summary>
        public string ToolName { get; }
    }

    /// <summary>
    /// Narrows the registered tools to a variation's tools and wraps them so each call is tracked.
    /// </summary>
    internal static class ToolRouting
    {
        /// <summary>
        /// The key prefix of the synthetic routing tools an agent graph adds. Calls to them are not
        /// tool calls and are not tracked.
        /// </summary>
        internal const string HandoffToolPrefix = "__handoff_";

        /// <summary>
        /// Returns the registered tools the config attaches, keyed by the config's tool keys. A config
        /// tool with no registered tool is left out, so nothing reaches a handler that the variation
        /// did not attach.
        /// </summary>
        internal static Dictionary<string, ToolHandler> Scope(
            IReadOnlyDictionary<string, ToolDefinition>? configTools,
            IReadOnlyDictionary<string, ToolHandler>? registered)
        {
            var scoped = new Dictionary<string, ToolHandler>(StringComparer.Ordinal);
            if (configTools == null || registered == null)
            {
                return scoped;
            }

            foreach (var key in configTools.Keys)
            {
                if (registered.TryGetValue(key, out var tool) && tool != null)
                {
                    scoped[key] = tool;
                }
            }

            return scoped;
        }

        /// <summary>
        /// Wraps each tool so that calling it first runs <paramref name="onToolCall"/> with its key,
        /// then the tool. A <see cref="NativeTool"/> becomes a <see cref="FunctionTool"/> stub that only
        /// reports the call. Handoff tools are wrapped but not reported.
        /// </summary>
        internal static Dictionary<string, ToolHandler> Wrap(
            IReadOnlyDictionary<string, ToolHandler> tools,
            Action<string> onToolCall)
        {
            var wrapped = new Dictionary<string, ToolHandler>(tools.Count, StringComparer.Ordinal);
            foreach (var entry in tools)
            {
                var key = entry.Key;
                var tracked = !key.StartsWith(HandoffToolPrefix, StringComparison.Ordinal);
                switch (entry.Value)
                {
                    case NativeTool native:
                        wrapped[key] = FunctionTool.ForNativeTool(native, () => onToolCall(key));
                        break;
                    case FunctionTool function:
                        wrapped[key] = new FunctionTool((arguments, cancellationToken) =>
                        {
                            if (tracked)
                            {
                                onToolCall(key);
                            }

                            return function.InvokeAsync(arguments, cancellationToken);
                        });
                        break;
                }
            }

            return wrapped;
        }
    }
}
