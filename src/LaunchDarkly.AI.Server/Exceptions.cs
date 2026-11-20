using System;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// The base class of every exception the AI SDK throws.
    /// </summary>
    public class LdAiException : Exception
    {
        /// <summary>
        /// Creates an exception with a message.
        /// </summary>
        /// <param name="message">What went wrong.</param>
        public LdAiException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an exception with a message and the exception that caused it.
        /// </summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The cause.</param>
        public LdAiException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when the flag for an AI Config returns no variation (the key does not exist, or
    /// evaluation fell back to the default).
    /// </summary>
    public sealed class AiConfigNotFoundException : LdAiException
    {
        internal AiConfigNotFoundException(string configKey, string message)
            : base(message)
        {
            ConfigKey = configKey;
        }

        /// <summary>
        /// The AI Config key that was evaluated.
        /// </summary>
        public string ConfigKey { get; }
    }

    /// <summary>
    /// Thrown when the evaluated variation is not enabled. Only <c>_ldMeta.enabled</c> equal to
    /// <see langword="true"/> counts as enabled.
    /// </summary>
    public sealed class AiConfigDisabledException : LdAiException
    {
        internal AiConfigDisabledException(string configKey, string message)
            : base(message)
        {
            ConfigKey = configKey;
        }

        /// <summary>
        /// The AI Config key that was evaluated.
        /// </summary>
        public string ConfigKey { get; }
    }

    /// <summary>
    /// Thrown when no handler matches the variation's provider and mode, or the variation has
    /// no provider.
    /// </summary>
    public sealed class NoHandlerException : LdAiException
    {
        internal NoHandlerException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown when <see cref="LdAi.Configure(SdkOptions)"/> is called with different options after
    /// the first AI invocation.
    /// </summary>
    public sealed class ConfigureLockedException : LdAiException
    {
        internal ConfigureLockedException(string message)
            : base(message)
        {
        }
    }
}
