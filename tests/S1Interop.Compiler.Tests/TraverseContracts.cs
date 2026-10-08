internal static class TraverseContracts
{
    // Portable contract for the installed Harmony traversal behavior; actual Harmony
    // is exercised separately in the game probe.
    public const string Library = """
        namespace HarmonyLib {
            public sealed class Traverse {
                private readonly object root;
                private readonly System.Type type;
                private readonly System.Reflection.MemberInfo member;
                private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
                private Traverse(object value, System.Type owner, System.Reflection.MemberInfo info = null) {
                    root = value; type = owner; member = info;
                }
                public static Traverse Create(object value) => new Traverse(value, value?.GetType());
                public static Traverse Create(System.Type type) => new Traverse(null, type);
                public Traverse Field(string name) {
                    for (var owner = type; owner != null; owner = owner.BaseType) {
                        var field = owner.GetField(name, Flags);
                        if (field != null) return field.IsStatic || root != null ? new Traverse(root, type, field) : new Traverse(null, null);
                    }
                    return new Traverse(null, null);
                }
                public Traverse Property(string name, object[] index = null) {
                    for (var owner = type; owner != null; owner = owner.BaseType) {
                        var property = owner.GetProperty(name, Flags);
                        if (property != null) return new Traverse(root, type, property);
                    }
                    return new Traverse(null, null);
                }
                public object GetValue() => member is System.Reflection.FieldInfo field ? field.GetValue(root) :
                    member is System.Reflection.PropertyInfo property ? property.GetValue(root) : root ?? (object)type;
                public T GetValue<T>() { object value = GetValue(); return value == null ? default(T) : (T)value; }
            }
        }
        """;
}
