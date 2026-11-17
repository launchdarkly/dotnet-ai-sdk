using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Waiting on a shared task with a caller's own cancellation token.
    /// </summary>
    internal static class TaskExtensions
    {
        /// <summary>
        /// Returns a task that completes as <paramref name="task"/> does, or is cancelled when
        /// <paramref name="cancellationToken"/> is. Cancelling ends only this wait, never
        /// <paramref name="task"/>.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="task">The shared task.</param>
        /// <param name="cancellationToken">Cancels this wait.</param>
        /// <returns>The wait.</returns>
        internal static Task<T> WaitForAsync<T>(this Task<T> task, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            return task.WaitAsync(cancellationToken);
#else
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                return task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            return WaitSlowAsync(task, cancellationToken);
#endif
        }

        /// <summary>
        /// Returns a task that completes as <paramref name="task"/> does, or is cancelled when
        /// <paramref name="cancellationToken"/> is.
        /// </summary>
        /// <param name="task">The task.</param>
        /// <param name="cancellationToken">Cancels this wait.</param>
        /// <returns>The wait.</returns>
        internal static Task WaitForAsync(this Task task, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            return task.WaitAsync(cancellationToken);
#else
            return WaitForAsync(task.ContinueWith(
                t =>
                {
                    t.GetAwaiter().GetResult();
                    return true;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default), cancellationToken);
#endif
        }

#if !NET8_0_OR_GREATER
        private static async Task<T> WaitSlowAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task.ConfigureAwait(false);
        }
#endif
    }
}
