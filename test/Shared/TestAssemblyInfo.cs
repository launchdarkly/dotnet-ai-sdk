using Xunit;

// The SDK keeps one process-wide client, process-wide defaults and a process-wide
// ActivitySource, and some tests set LD_SDK_KEY, so tests in an assembly run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
