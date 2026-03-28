using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenSmith.Engine;

public static class StringUtil
{
    public static string ToPascalCase(string value)
    {
        if (value is null) return null;
        if (value.Length == 0) return value;

        var words = SplitIntoWords(value);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
                sb.Append(word[1..].ToLowerInvariant());
        }
        return sb.ToString();
    }

    public static string ToCamelCase(string value)
    {
        if (value is null) return null;
        if (value.Length == 0) return value;

        var pascal = ToPascalCase(value);
        if (pascal.Length == 0) return pascal;

        // Find the end of the leading uppercase run
        int upperRun = 0;
        while (upperRun < pascal.Length && char.IsUpper(pascal[upperRun]))
            upperRun++;

        if (upperRun <= 1)
            return char.ToLowerInvariant(pascal[0]) + pascal[1..];

        // For runs like "HTML" at start, lowercase all but last if followed by lowercase
        return pascal[..1].ToLowerInvariant() + pascal[1..];
    }

    private static List<string> SplitIntoWords(string value)
    {
        var words = new List<string>();

        // First split on delimiters
        var parts = Regex.Split(value, @"[_\-\.\s]+");

        foreach (var part in parts)
        {
            if (part.Length == 0) continue;

            // Split camelCase/PascalCase/ACRONYM boundaries
            var sb = new StringBuilder();
            for (int i = 0; i < part.Length; i++)
            {
                if (i > 0 && char.IsUpper(part[i]))
                {
                    bool prevIsUpper = char.IsUpper(part[i - 1]);
                    bool nextIsLower = i + 1 < part.Length && char.IsLower(part[i + 1]);

                    // Split before uppercase if previous was lowercase
                    // Or if previous was uppercase but next is lowercase (end of acronym)
                    if (!prevIsUpper || (prevIsUpper && nextIsLower))
                    {
                        words.Add(sb.ToString());
                        sb.Clear();
                    }
                }
                sb.Append(part[i]);
            }
            if (sb.Length > 0)
                words.Add(sb.ToString());
        }

        return words;
    }

    #region Pluralization

    private static readonly Dictionary<string, string> IrregularPlurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["child"] = "children",
        ["person"] = "people",
        ["man"] = "men",
        ["woman"] = "women",
        ["mouse"] = "mice",
        ["goose"] = "geese",
        ["tooth"] = "teeth",
        ["foot"] = "feet",
        ["ox"] = "oxen",
        ["leaf"] = "leaves",
        ["life"] = "lives",
        ["knife"] = "knives",
        ["wife"] = "wives",
        ["half"] = "halves",
        ["self"] = "selves",
        ["calf"] = "calves",
    };

    private static readonly Dictionary<string, string> IrregularSingulars;

    private static readonly (Regex pattern, string replacement)[] PluralRules =
    [
        (new Regex(@"(quiz)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1zes"),
        (new Regex(@"^(ox)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1en"),
        (new Regex(@"([m|l])ouse$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ice"),
        (new Regex(@"(matr|vert|append)ix$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ices"),
        (new Regex(@"(x|ch|ss|sh)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1es"),
        (new Regex(@"([^aeiouy]|qu)y$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ies"),
        (new Regex(@"(hive)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1s"),
        (new Regex(@"(?:([^f])fe|([lr])f)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1$2ves"),
        (new Regex(@"sis$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ses"),
        (new Regex(@"([ti])um$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1a"),
        (new Regex(@"(buffal|tomat|volcan)o$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1oes"),
        (new Regex(@"(bu)s$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ses"),
        (new Regex(@"(alias|status)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1es"),
        (new Regex(@"(octop|vir)us$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1i"),
        (new Regex(@"(ax|test)is$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1es"),
        (new Regex(@"s$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "s"),
        (new Regex(@"$", RegexOptions.Compiled), "s"),
    ];

    private static readonly (Regex pattern, string replacement)[] SingularRules =
    [
        (new Regex(@"(quiz)zes$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"(matr|vert|append)ices$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ix"),
        (new Regex(@"(alias|status)es$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"(octop|vir)i$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1us"),
        (new Regex(@"(cris|ax|test)es$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1is"),
        (new Regex(@"(shoe)s$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"(o)es$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"(bu)ses$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1s"),
        (new Regex(@"([m|l])ice$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ouse"),
        (new Regex(@"(x|ch|ss|sh)es$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"(m)ovies$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ovie"),
        (new Regex(@"(s)eries$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1eries"),
        (new Regex(@"([^aeiouy]|qu)ies$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1y"),
        (new Regex(@"([lr])ves$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1f"),
        (new Regex(@"(tive)s$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"(hive)s$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1"),
        (new Regex(@"([^f])ves$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1fe"),
        (new Regex(@"(^analy)ses$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1sis"),
        (new Regex(@"((a)naly|(b)a|(d)iagno|(p)arenthe|(p)rogno|(s)ynop|(t)he)ses$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1$2sis"),
        (new Regex(@"([ti])a$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1um"),
        (new Regex(@"(n)ews$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1ews"),
        (new Regex(@"(p)eople$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1erson"),
        (new Regex(@"(c)hildren$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1hild"),
        (new Regex(@"(m)en$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1an"),
        (new Regex(@"(w)omen$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1oman"),
        (new Regex(@"s$", RegexOptions.IgnoreCase | RegexOptions.Compiled), ""),
    ];

    private static readonly HashSet<string> Uncountables = new(StringComparer.OrdinalIgnoreCase)
    {
        "equipment", "information", "rice", "money", "species", "series", "fish", "sheep", "deer", "aircraft"
    };

    static StringUtil()
    {
        IrregularSingulars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in IrregularPlurals)
            IrregularSingulars[kvp.Value] = kvp.Key;
    }

    public static string ToPlural(string value)
    {
        if (value is null) return null;
        if (value.Length == 0) return value;
        if (Uncountables.Contains(value)) return value;

        // Check irregulars
        if (IrregularPlurals.TryGetValue(value, out var irregular))
            return PreserveCase(value, irregular);

        // Apply rules
        foreach (var (pattern, replacement) in PluralRules)
        {
            if (pattern.IsMatch(value))
                return pattern.Replace(value, replacement);
        }

        return value + "s";
    }

    public static string ToSingular(string value)
    {
        if (value is null) return null;
        if (value.Length == 0) return value;
        if (Uncountables.Contains(value)) return value;

        // Check irregulars
        if (IrregularSingulars.TryGetValue(value, out var irregular))
            return PreserveCase(value, irregular);

        // Apply rules
        foreach (var (pattern, replacement) in SingularRules)
        {
            if (pattern.IsMatch(value))
                return pattern.Replace(value, replacement);
        }

        return value;
    }

    private static string PreserveCase(string original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0) return replacement;
        if (char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return char.ToLowerInvariant(replacement[0]) + replacement[1..];
    }

    #endregion
}
