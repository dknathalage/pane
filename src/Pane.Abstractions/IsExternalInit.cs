// Polyfill: records with `init`-only setters require this type, which is not
// present in netstandard2.0. Compiled only for that target; net10.0 has it built in.
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
