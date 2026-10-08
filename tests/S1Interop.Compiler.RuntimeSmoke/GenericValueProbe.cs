namespace S1Interop.CompilerSmoke;

internal static class GenericValueProbe
{
    internal static T Make<T>() where T : new() => new T();
    internal static T Identity<T>(this T value) => value;
}
