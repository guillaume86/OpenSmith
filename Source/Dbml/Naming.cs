using System;

namespace LinqToSqlShared.DbmlObjectModel;

public static class Naming
{
    public static string GetModifier(AccessModifier? access) =>
        access.ToString().ToLower();

    public static string GetModifier(AccessModifier? access, ClassModifier? modifier) =>
        !modifier.HasValue
            ? access.ToString().ToLower()
            : $"{access.ToString().ToLower()} {modifier.ToString().ToLower()}";

    public static string GetModifier(AccessModifier? access, MemberModifier? modifier) =>
        !modifier.HasValue
            ? access.ToString().ToLower()
            : $"{access.ToString().ToLower()} {modifier.ToString().ToLower()}";

    public static string GetVisualBasicModifier(AccessModifier? access) =>
        GetVisualBasicModifier(access.ToString());

    public static string GetVisualBasicModifier(AccessModifier? access, ClassModifier? modifier) =>
        !modifier.HasValue
            ? GetVisualBasicModifier(access.ToString())
            : $"{GetVisualBasicModifier(access.ToString())} {modifier}";

    public static string GetVisualBasicModifier(AccessModifier? access, MemberModifier? modifier) =>
        !modifier.HasValue
            ? GetVisualBasicModifier(access.ToString())
            : $"{GetVisualBasicModifier(access.ToString())} {modifier}";

    private static string GetVisualBasicModifier(string modifier)
    {
        if (String.Equals(modifier, "Internal", StringComparison.OrdinalIgnoreCase))
            return "Friend";

        if (String.Equals(modifier, "ProtectedInternal", StringComparison.OrdinalIgnoreCase))
            return "Protected Friend";

        return modifier;
    }
}
