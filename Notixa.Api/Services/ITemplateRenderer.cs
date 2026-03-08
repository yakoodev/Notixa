using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public interface ITemplateRenderer
{
    TemplateRenderResult Render(string body, IReadOnlyDictionary<string, object?> variables, TemplateParseMode parseMode);
}
