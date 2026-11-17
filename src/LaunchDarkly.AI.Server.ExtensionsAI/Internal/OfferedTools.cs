using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server.Internal;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// The tools offered to the model for one request, and the only tools a tool call can run.
    /// </summary>
    /// <remarks>
    /// The config's tools are the allowlist. A tool is offered only when the config attaches it and
    /// the request holds a <see cref="FunctionTool"/> for it under that key. A
    /// <see cref="NativeTool"/>, or the tracking stub the SDK builds from one, is never offered as a
    /// function: it is skipped with one warning per tool key per process.
    /// </remarks>
    internal sealed class OfferedTools
    {
        private const string NativeWarningScope = "extensionsai-messages.native-tool";

        private readonly Dictionary<string, FunctionTool> _byName;

        private OfferedTools(Dictionary<string, FunctionTool> byName, IReadOnlyList<AITool> declarations)
        {
            _byName = byName;
            Declarations = declarations;
        }

        /// <summary>The declarations sent in <see cref="ChatOptions.Tools"/>, in the config's order.</summary>
        internal IReadOnlyList<AITool> Declarations { get; }

        /// <summary>Whether any tool is offered.</summary>
        internal bool Any => _byName.Count > 0;

        /// <summary>
        /// Selects the tools to offer.
        /// </summary>
        /// <param name="configTools">The config's tools.</param>
        /// <param name="handlers">The request's tools, by tool key.</param>
        /// <returns>The offered tools.</returns>
        internal static OfferedTools Select(
            IReadOnlyDictionary<string, ToolDefinition>? configTools,
            IReadOnlyDictionary<string, ToolHandler>? handlers)
        {
            var byName = new Dictionary<string, FunctionTool>(StringComparer.Ordinal);
            var declarations = new List<AITool>();
            if (configTools == null || handlers == null)
            {
                return new OfferedTools(byName, declarations);
            }

            foreach (var entry in configTools)
            {
                if (!handlers.TryGetValue(entry.Key, out var handler) || handler == null)
                {
                    continue;
                }

                if (handler is NativeTool || (handler is FunctionTool stub && stub.WrappedNativeTool != null))
                {
                    WarnNativeTool(entry.Key);
                    continue;
                }

                if (handler is not FunctionTool function)
                {
                    continue;
                }

                byName[entry.Key] = function;
                declarations.Add(AIFunctionFactory.CreateDeclaration(entry.Key, entry.Value.Description ?? "", entry.Value.Parameters));
            }

            return new OfferedTools(byName, declarations);
        }

        /// <summary>
        /// Runs one tool call the model requested, in an <c>execute_tool</c> span under
        /// <paramref name="root"/>. The name is resolved only against the offered tools; any other name
        /// fails closed with an <see cref="LdAiException"/> naming the tool, and runs nothing. A tool's
        /// exception is recorded on the span and rethrown unchanged.
        /// </summary>
        /// <param name="call">The model's tool call.</param>
        /// <param name="root">The run's root span.</param>
        /// <param name="captureContent">The effective capture setting.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The result to send back to the model, as text.</returns>
        /// <exception cref="LdAiException">The model named a tool that was not offered.</exception>
        internal async Task<FunctionResultContent> ExecuteAsync(
            FunctionCallContent call,
            Activity? root,
            bool captureContent,
            CancellationToken cancellationToken)
        {
            if (!_byName.TryGetValue(call.Name, out var tool))
            {
                throw new LdAiException($"The model called the tool \"{call.Name}\", which was not offered for this request.");
            }

            using var span = Telemetry.StartExecuteTool(root, call.Name, call.CallId);
            try
            {
                var arguments = Arguments(call.Arguments);
                var result = await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
                Telemetry.SetToolCallContent(span, captureContent, call.Arguments, result);
                Telemetry.SetOk(span);
                return new FunctionResultContent(call.CallId, Telemetry.ToolResultString(result));
            }
            catch (Exception e)
            {
                Telemetry.RecordError(span, e);
                throw;
            }
        }

        private static JsonElement Arguments(IDictionary<string, object?>? arguments) =>
            JsonSerializer.SerializeToElement(arguments ?? new Dictionary<string, object?>());

        private static void WarnNativeTool(string key)
        {
            if (OnceKeys.TryClaim(NativeWarningScope, key))
            {
                SdkLog.Logger.Warn(
                    "The tool \"{0}\" is a native tool, which this handler cannot run; it is not offered to the model.",
                    key);
            }
        }
    }
}
