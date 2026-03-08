using Notixa.Api.Contracts;
using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public interface IPlatformAdministrationService
{
    Task RegisterOrUpdateUserAsync(global::Telegram.Bot.Types.User telegramUser, CancellationToken cancellationToken);

    Task<BotScreenState> GetBotScreenStateAsync(long telegramUserId, CancellationToken cancellationToken);

    Task SaveBotScreenStateAsync(long telegramUserId, long chatId, int messageId, CancellationToken cancellationToken);

    Task<bool> SetCreatorPermissionAsync(long actingTelegramUserId, long targetTelegramUserId, bool allowed, CancellationToken cancellationToken);

    Task<CreateServiceResult> CreateServiceAsync(long actingTelegramUserId, string name, string description, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ServiceListItem>> GetManagedServicesAsync(long actingTelegramUserId, CancellationToken cancellationToken);

    Task<CreateInviteResult> CreateInviteAsync(long actingTelegramUserId, string servicePublicId, InviteCodeType inviteType, string? externalUserKey, int? usageLimit, int? expiresInHours, CancellationToken cancellationToken);

    Task AddServiceAdminAsync(long actingTelegramUserId, string servicePublicId, long targetTelegramUserId, CancellationToken cancellationToken);

    Task RemoveServiceAdminAsync(long actingTelegramUserId, string servicePublicId, long targetTelegramUserId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<long>> GetServiceAdminsAsync(long actingTelegramUserId, string servicePublicId, CancellationToken cancellationToken);

    Task<PreviewInviteResult> PreviewInviteAsync(long telegramUserId, string inviteCode, CancellationToken cancellationToken);

    Task<RedeemInviteResult> RedeemInviteAsync(long telegramUserId, string inviteCode, CancellationToken cancellationToken);

    Task<bool> UnsubscribeAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SubscriptionListItem>> GetSubscriptionsAsync(long telegramUserId, CancellationToken cancellationToken);

    Task<UpsertTemplateResult> UpsertTemplateAsync(long actingTelegramUserId, string servicePublicId, string templateKey, TemplateParseMode parseMode, string body, CancellationToken cancellationToken);
}
