using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The registered LaunchDarkly AI packages, and which of them have reported themselves with a
    /// package-information event since the last shutdown.
    /// </summary>
    internal static class SdkInfoReporter
    {
        private static readonly object _lock = new object();

        // Registration order is kept so packages report in the order they registered.
        private static readonly List<(string Name, string Version)> _registered = new List<(string, string)>
        {
            (SdkInfo.Name, SdkInfo.Version),
        };

        private static readonly HashSet<(string Name, string Version)> _registeredSet =
            new HashSet<(string, string)>(_registered);

        private static readonly HashSet<(string Name, string Version)> _reported = new HashSet<(string, string)>();

        /// <summary>
        /// Registers a package. Each name and version pair is kept once.
        /// </summary>
        /// <param name="name">The package ID.</param>
        /// <param name="version">The package version.</param>
        internal static void Register(string name, string version)
        {
            lock (_lock)
            {
                if (_registeredSet.Add((name, version)))
                {
                    _registered.Add((name, version));
                }
            }
        }

        /// <summary>
        /// Sends one package-information event to <paramref name="client"/> for each registered
        /// package that has not reported yet. A failed send still counts as reported, so a client
        /// whose <c>Track</c> keeps failing is not retried.
        /// </summary>
        /// <param name="client">The client to report to.</param>
        internal static void ReportTo(ILdClient client)
        {
            lock (_lock)
            {
                foreach (var package in _registered)
                {
                    if (!_reported.Add(package))
                    {
                        continue;
                    }

                    try
                    {
                        var data = LdValue.BuildObject()
                            .Add("aiSdkName", package.Name)
                            .Add("aiSdkVersion", package.Version)
                            .Add("aiSdkLanguage", SdkInfo.Language)
                            .Build();
                        client.Track(InternalEvents.SdkInfo, InternalEvents.Context, data, 1);
                    }
                    catch (Exception e)
                    {
                        SdkLog.Logger.Debug("Could not send the AI SDK package information event: {0}", e.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Forgets what has been reported, keeping the registrations, so the next client hears
        /// from every package again.
        /// </summary>
        internal static void ResetReported()
        {
            lock (_lock)
            {
                _reported.Clear();
            }
        }
    }
}
