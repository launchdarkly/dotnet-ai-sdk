#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Lets the net462 test build compile init-only setters and records. Both libraries declare
    /// their own internal copy for netstandard2.0, and the test assemblies can see both through
    /// InternalsVisibleTo; a copy in the test assembly itself takes precedence over them.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
