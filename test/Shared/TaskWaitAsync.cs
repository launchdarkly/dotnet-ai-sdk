#if NETFRAMEWORK
using System;
namespace System.Threading.Tasks
{
    /// <summary>
    /// Task.WaitAsync(TimeSpan) for the net462 test build, where the framework lacks it. On
    /// net8.0 and later the framework's instance method is used instead.
    /// </summary>
    internal static class TaskWaitAsync
    {
        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
            {
                throw new TimeoutException();
            }
            await task.ConfigureAwait(false);
        }
    }
}
#endif
