using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public sealed record TemplateRenderResult(
    bool IsSuccess,
    string RenderedText,
    IReadOnlyCollection<string> MissingVariables,
    TemplateParseMode ParseMode);
