using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TelegramNotifications.Api.Data;
using TelegramNotifications.Api.Options;

namespace TelegramNotifications.Api.Services;

public sealed class UserContextService(ApplicationDbContext dbContext, IOptions<AppSecurityOptions> options) : IUserContextService
{
    public async Task<UserContext> GetUserContextAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var isSuperAdmin = telegramUserId != 0 && telegramUserId == options.Value.SuperAdminTelegramUserId;
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken);
        return new UserContext(telegramUserId, isSuperAdmin, isSuperAdmin || user?.CanCreateServices == true);
    }
}
