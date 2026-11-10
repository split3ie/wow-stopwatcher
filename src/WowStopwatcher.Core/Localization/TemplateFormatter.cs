using System.Globalization;
using System.Text;

namespace WowStopwatcher.Core.Localization;

/// <summary>
/// Fills named placeholders in a translation text: <c>{name}</c> is replaced, <c>{{</c> and <c>}}</c> give literal braces.
/// A placeholder without a matching argument is left as written.
/// </summary>
internal static class TemplateFormatter
{
    public static string Format(string template, IReadOnlyList<(string Name, object? Value)> args)
    {
        if (template.IndexOfAny(['{', '}']) < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        Scan(template, literal => result.Append(literal), name =>
        {
            foreach (var (argName, value) in args)
            {
                if (argName == name)
                {
                    // Numbers (and everything else) use InvariantCulture on purpose: the app shows times and counts
                    // with invariant digits in every language. Day and month names use Loc.Culture instead.
                    result.Append(value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString());
                    return;
                }
            }

            result.Append('{').Append(name).Append('}');
        });

        return result.ToString();
    }

    /// <summary>The placeholder names used in <paramref name="template"/>, without duplicates.</summary>
    public static IReadOnlySet<string> PlaceholderNames(string template)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Scan(template, _ => { }, name => names.Add(name));
        return names;
    }

    private static void Scan(string template, Action<string> onLiteral, Action<string> onPlaceholder)
    {
        var literal = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                literal.Append('{');
                i++;
            }
            else if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i++;
            }
            else if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    literal.Append(c);
                    continue;
                }

                if (literal.Length > 0)
                {
                    onLiteral(literal.ToString());
                    literal.Clear();
                }

                onPlaceholder(template.Substring(i + 1, end - i - 1));
                i = end;
            }
            else
            {
                literal.Append(c);
            }
        }

        if (literal.Length > 0)
        {
            onLiteral(literal.ToString());
        }
    }
}
