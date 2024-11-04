// ReSharper disable once CheckNamespace

namespace System.Runtime.CompilerServices
{
    // This is needed to fully support C#9 record types without .NET 5 which isn't supported by Unity.
    // See https://tooslowexception.com/6-less-popular-facts-about-c-9-records/
    public class IsExternalInit
    { }
}