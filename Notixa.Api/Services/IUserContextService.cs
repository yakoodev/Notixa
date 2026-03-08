namespace Notixa.Api.Services;

public interface IUserContextService
{
    Task<UserContext> GetUserContextAsync(long telegramUserId, CancellationToken cancellationToken);
}
