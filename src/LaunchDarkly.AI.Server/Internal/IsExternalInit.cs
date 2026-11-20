#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Lets the compiler emit init-only setters and records for netstandard2.0, which does not
    /// ship this type.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
