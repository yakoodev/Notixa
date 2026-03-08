using TelegramNotifications.Api.Domain.Enums;

namespace TelegramNotifications.Api.Services;

public sealed record TemplateRenderResult(
    bool IsSuccess,
    string RenderedText,
    IReadOnlyCollection<string> MissingVariables,
    TemplateParseMode ParseMode);
