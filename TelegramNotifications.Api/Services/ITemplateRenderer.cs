using TelegramNotifications.Api.Domain.Enums;

namespace TelegramNotifications.Api.Services;

public interface ITemplateRenderer
{
    TemplateRenderResult Render(string body, IReadOnlyDictionary<string, object?> variables, TemplateParseMode parseMode);
}
