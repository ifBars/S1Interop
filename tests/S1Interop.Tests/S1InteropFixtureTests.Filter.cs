using System.Reflection;
using System.Runtime.ExceptionServices;

internal sealed partial class S1InteropFixtureTests
{
    public IReadOnlyList<string> ListTests() => GetFixtureMethods().Select(method => method.Name).ToArray();

    public int RunFiltered(string name)
    {
        MethodInfo[] matches = GetFixtureMethods()
            .Where(method => method.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            throw new ArgumentException($"No fixtures match '{name}'. Use --list-tests to find an exact name.", nameof(name));
        }

        foreach (MethodInfo method in matches)
        {
            Console.WriteLine($"Running {method.Name}");
            try
            {
                method.Invoke(this, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        return matches.Length;
    }

    private static IEnumerable<MethodInfo> GetFixtureMethods() => typeof(S1InteropFixtureTests)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => method.ReturnType == typeof(void) && method.GetParameters().Length == 0)
        .OrderBy(method => method.Name, StringComparer.Ordinal);
}
