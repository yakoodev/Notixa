using System.Text.RegularExpressions;
using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public sealed partial class TemplateRenderer : ITemplateRenderer
{
    [GeneratedRegex(@"\{\{(?<name>[a-zA-Z0-9_\-\.]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    public TemplateRenderResult Render(string body, IReadOnlyDictionary<string, object?> variables, TemplateParseMode parseMode)
    {
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rendered = PlaceholderRegex().Replace(body, match =>
        {
            var name = match.Groups["name"].Value;
            if (!variables.TryGetValue(name, out var value) || value is null)
            {
                missing.Add(name);
                return match.Value;
            }

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        });

        return new TemplateRenderResult(missing.Count == 0, rendered, missing.ToArray(), parseMode);
    }
}
