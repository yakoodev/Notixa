namespace Notixa.Api.Services;

public sealed record TelegramScreenResult(long ChatId, int MessageId, bool WasEdited);
