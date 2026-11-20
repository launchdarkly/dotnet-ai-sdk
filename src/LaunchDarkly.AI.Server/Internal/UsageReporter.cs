using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Records which public helpers an application calls: one helper-usage event per helper per
    /// client. A call made before a client exists is held and sent when a client is initialized.
    /// SDK code calls the non-reporting internal version of each helper, so only a call from the
    /// application reports.
    /// </summary>
    internal static class UsageReporter
    {
        /// <summary>The usage ID of <see cref="Config.InvokeAsync"/>.</summary>
        internal const string ConfigInvoke = "client.config.invoke";

        /// <summary>The usage ID of the public <c>LdAi.CreateHandler</c> overloads.</summary>
        internal const string CreateHandler = "client.createHandler";

        /// <summary>The usage ID of <c>InspectConfigAsync</c>.</summary>
        internal const string InspectConfig = "client.inspectConfig";

        private static readonly object _lock = new object();

        // Held helpers in the order they were called, with the package each belongs to.
        private static readonly List<(string Helper, string PackageName, string PackageVersion)> _pending =
            new List<(string, string, string)>();

        private static readonly HashSet<string> _pendingSet = new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Records a call to a public helper. Sends the event now when a client exists, otherwise
        /// holds it. Does nothing when the helper has already reported. Never throws.
        /// </summary>
        /// <param name="helper">The helper's usage ID.</param>
        /// <param name="packageName">The helper's package; the core package when omitted.</param>
        /// <param name="packageVersion">That package's version; the core version when omitted.</param>
        internal static void Report(string helper, string? packageName = null, string? packageVersion = null)
        {
            try
            {
                lock (_lock)
                {
                    if (_reported.Contains(helper) || _pendingSet.Contains(helper))
                    {
                        return;
                    }

                    var name = packageName ?? SdkInfo.Name;
                    var version = packageVersion ?? SdkInfo.Version;
                    var client = Lifecycle.CurrentLdClient;
                    if (client is null)
                    {
                        _pendingSet.Add(helper);
                        _pending.Add((helper, name, version));
                        return;
                    }

                    _reported.Add(helper);
                    Deliver(client, helper, name, version);
                }
            }
            catch (Exception e)
            {
                // Reporting a helper never breaks the call that asked for it.
                SdkLog.Logger.Debug("Could not record AI SDK helper usage: {0}", e.Message);
            }
        }

        /// <summary>
        /// Sends every helper held from before a client existed.
        /// </summary>
        /// <param name="client">The client to send to.</param>
        internal static void Flush(ILdClient client)
        {
            lock (_lock)
            {
                foreach (var (helper, name, version) in _pending)
                {
                    _reported.Add(helper);
                    Deliver(client, helper, name, version);
                }

                _pending.Clear();
                _pendingSet.Clear();
            }
        }

        /// <summary>
        /// Forgets every reported and held helper, so the next client hears each one again.
        /// </summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                _reported.Clear();
                _pending.Clear();
                _pendingSet.Clear();
            }
        }

        private static void Deliver(ILdClient client, string helper, string packageName, string packageVersion)
        {
            try
            {
                var data = LdValue.BuildObject()
                    .Add("aiSdkName", SdkInfo.Name)
                    .Add("aiSdkVersion", SdkInfo.Version)
                    .Add("aiSdkLanguage", SdkInfo.Language)
                    .Add("helper", helper)
                    .Add("helperPackageName", packageName)
                    .Add("helperPackageVersion", packageVersion)
                    .Build();
                client.Track(InternalEvents.SdkUsage, InternalEvents.Context, data, 1);
            }
            catch (Exception e)
            {
                SdkLog.Logger.Debug("Could not send the AI SDK helper usage event: {0}", e.Message);
            }
        }
    }
}
