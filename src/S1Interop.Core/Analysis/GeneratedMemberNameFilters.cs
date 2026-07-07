namespace S1Interop.Core.Analysis;

internal static class GeneratedMemberNameFilters
{
    public static bool IsBackingFieldName(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        return memberName.Contains("BackingField", StringComparison.Ordinal) ||
               (memberName.StartsWith("<", StringComparison.Ordinal) &&
                memberName.Contains(">k__BackingField", StringComparison.Ordinal));
    }
}
