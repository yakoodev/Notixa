using Microsoft.EntityFrameworkCore;
using Notixa.Api.Contracts;
using Notixa.Api.Data;
using Notixa.Api.Domain.Entities;
using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public sealed class PlatformAdministrationService(
    ApplicationDbContext dbContext,
    IUserContextService userContextService,
    ISecretGenerator secretGenerator,
    ISecretHasher secretHasher,
    ITimeProvider timeProvider) : IPlatformAdministrationService
{
    public async Task RegisterOrUpdateUserAsync(global::Telegram.Bot.Types.User telegramUser, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([telegramUser.Id], cancellationToken);
        var displayName = string.Join(' ', new[] { telegramUser.FirstName, telegramUser.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = telegramUser.Username ?? telegramUser.Id.ToString();
        }

        if (user is null)
        {
            dbContext.Users.Add(new AppUser
            {
                TelegramUserId = telegramUser.Id,
                Username = telegramUser.Username,
                DisplayName = displayName,
                CreatedAtUtc = timeProvider.UtcNow,
                UpdatedAtUtc = timeProvider.UtcNow
            });
        }
        else
        {
            user.Username = telegramUser.Username;
            user.DisplayName = displayName;
            user.UpdatedAtUtc = timeProvider.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BotScreenState> GetBotScreenStateAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken);

        return user is null
            ? new BotScreenState(null, null)
            : new BotScreenState(user.LastBotChatId, user.LastBotMessageId);
    }

    public async Task SaveBotScreenStateAsync(long telegramUserId, long chatId, int messageId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([telegramUserId], cancellationToken)
            ?? throw new InvalidOperationException("Пользователь не найден.");

        user.LastBotChatId = chatId;
        user.LastBotMessageId = messageId;
        user.UpdatedAtUtc = timeProvider.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SetCreatorPermissionAsync(long actingTelegramUserId, long targetTelegramUserId, bool allowed, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(actingTelegramUserId, cancellationToken);
        if (!context.IsSuperAdmin)
        {
            return false;
        }

        var user = await dbContext.Users.FindAsync([targetTelegramUserId], cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                TelegramUserId = targetTelegramUserId,
                DisplayName = targetTelegramUserId.ToString(),
                CreatedAtUtc = timeProvider.UtcNow,
                UpdatedAtUtc = timeProvider.UtcNow
            };
            dbContext.Users.Add(user);
        }

        user.CanCreateServices = allowed;
        user.UpdatedAtUtc = timeProvider.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CreateServiceResult> CreateServiceAsync(long actingTelegramUserId, string name, string description, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(actingTelegramUserId, cancellationToken);
        if (!context.CanCreateServices)
        {
            throw new InvalidOperationException("У вас нет прав на создание сервисов.");
        }

        var normalizedName = name.Trim();
        var normalizedDescription = description.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new InvalidOperationException("Название сервиса не может быть пустым.");
        }

        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            throw new InvalidOperationException("Описание сервиса не может быть пустым.");
        }

        if (string.Equals(normalizedName, "Название", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Замените шаблонное значение 'Название' на реальное имя сервиса.");
        }

        if (string.Equals(normalizedDescription, "Описание", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Замените шаблонное значение 'Описание' на реальное описание сервиса.");
        }

        var now = timeProvider.UtcNow;
        var serviceKey = secretGenerator.GenerateServiceKey();
        var service = new ServiceDefinition
        {
            Id = Guid.NewGuid(),
            PublicId = await GenerateUniquePublicIdAsync(cancellationToken),
            Name = normalizedName,
            Description = normalizedDescription,
            ServiceKeyHash = secretHasher.Hash(serviceKey),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Services.Add(service);
        dbContext.ServiceAdmins.Add(new ServiceAdmin
        {
            ServiceDefinition = service,
            TelegramUserId = actingTelegramUserId,
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreateServiceResult(service.PublicId, serviceKey);
    }

    public async Task<IReadOnlyCollection<ServiceListItem>> GetManagedServicesAsync(long actingTelegramUserId, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(actingTelegramUserId, cancellationToken);
        IQueryable<ServiceDefinition> query = dbContext.Services.AsNoTracking();

        if (!context.IsSuperAdmin)
        {
            query = query.Where(service => service.Admins.Any(admin => admin.TelegramUserId == actingTelegramUserId));
        }

        return await query.OrderBy(x => x.Name)
            .Select(x => new ServiceListItem(x.PublicId, x.Name, x.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<CreateInviteResult> CreateInviteAsync(long actingTelegramUserId, string servicePublicId, InviteCodeType inviteType, string? externalUserKey, int? usageLimit, int? expiresInHours, CancellationToken cancellationToken)
    {
        var service = await GetManagedServiceAsync(actingTelegramUserId, servicePublicId, cancellationToken);
        if (inviteType == InviteCodeType.Personal && string.IsNullOrWhiteSpace(externalUserKey))
        {
            throw new InvalidOperationException("Для персонального приглашения нужен внешний ключ пользователя.");
        }

        var code = secretGenerator.GenerateInviteCode();
        dbContext.InviteCodes.Add(new InviteCode
        {
            Id = Guid.NewGuid(),
            ServiceDefinitionId = service.Id,
            CodeHash = secretHasher.Hash(code),
            Type = inviteType,
            ExternalUserKey = externalUserKey?.Trim(),
            ExpiresAtUtc = expiresInHours.HasValue ? timeProvider.UtcNow.AddHours(expiresInHours.Value) : null,
            UsageLimit = usageLimit,
            UsageCount = 0,
            CreatedByTelegramUserId = actingTelegramUserId,
            CreatedAtUtc = timeProvider.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreateInviteResult(code, service.PublicId, externalUserKey);
    }

    public async Task AddServiceAdminAsync(long actingTelegramUserId, string servicePublicId, long targetTelegramUserId, CancellationToken cancellationToken)
    {
        var service = await GetManagedServiceAsync(actingTelegramUserId, servicePublicId, cancellationToken);
        var target = await dbContext.Users.FindAsync([targetTelegramUserId], cancellationToken)
            ?? throw new InvalidOperationException("Пользователь еще не начал диалог с ботом.");

        var existing = await dbContext.ServiceAdmins.FindAsync([service.Id, targetTelegramUserId], cancellationToken);
        if (existing is not null)
        {
            return;
        }

        dbContext.ServiceAdmins.Add(new ServiceAdmin
        {
            ServiceDefinitionId = service.Id,
            TelegramUserId = target.TelegramUserId,
            CreatedAtUtc = timeProvider.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveServiceAdminAsync(long actingTelegramUserId, string servicePublicId, long targetTelegramUserId, CancellationToken cancellationToken)
    {
        var service = await GetManagedServiceAsync(actingTelegramUserId, servicePublicId, cancellationToken);
        var existing = await dbContext.ServiceAdmins.FindAsync([service.Id, targetTelegramUserId], cancellationToken);
        if (existing is null)
        {
            return;
        }

        dbContext.ServiceAdmins.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<long>> GetServiceAdminsAsync(long actingTelegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        var service = await GetManagedServiceAsync(actingTelegramUserId, servicePublicId, cancellationToken);
        return await dbContext.ServiceAdmins.AsNoTracking()
            .Where(x => x.ServiceDefinitionId == service.Id)
            .OrderBy(x => x.TelegramUserId)
            .Select(x => x.TelegramUserId)
            .ToListAsync(cancellationToken);
    }

    public async Task<PreviewInviteResult> PreviewInviteAsync(long telegramUserId, string inviteCode, CancellationToken cancellationToken)
    {
        var invite = await FindInviteAsync(inviteCode, cancellationToken);
        if (invite is null)
        {
            return new PreviewInviteResult(PreviewInviteStatus.Invalid, null, null, null);
        }

        var subscription = await dbContext.Subscriptions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ServiceDefinitionId == invite.ServiceDefinitionId && x.TelegramUserId == telegramUserId,
                cancellationToken);

        if (subscription is not null && subscription.Status == SubscriptionStatus.Active)
        {
            return new PreviewInviteResult(
                PreviewInviteStatus.AlreadySubscribed,
                invite.ServiceDefinition.PublicId,
                invite.ServiceDefinition.Name,
                subscription.ExternalUserKey);
        }

        if (!IsInviteRedeemable(invite))
        {
            return new PreviewInviteResult(PreviewInviteStatus.Invalid, null, null, null);
        }

        return new PreviewInviteResult(
            PreviewInviteStatus.Available,
            invite.ServiceDefinition.PublicId,
            invite.ServiceDefinition.Name,
            invite.Type == InviteCodeType.Personal ? invite.ExternalUserKey : null);
    }

    public async Task<RedeemInviteResult> RedeemInviteAsync(long telegramUserId, string inviteCode, CancellationToken cancellationToken)
    {
        var invite = await FindValidInviteAsync(inviteCode, cancellationToken);
        if (invite is null)
        {
            return new RedeemInviteResult(RedeemInviteStatus.Invalid, null);
        }

        var user = await dbContext.Users.FindAsync([telegramUserId], cancellationToken);
        if (user is null)
        {
            dbContext.Users.Add(new AppUser
            {
                TelegramUserId = telegramUserId,
                DisplayName = telegramUserId.ToString(),
                CreatedAtUtc = timeProvider.UtcNow,
                UpdatedAtUtc = timeProvider.UtcNow
            });
        }

        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                x => x.ServiceDefinitionId == invite.ServiceDefinitionId && x.TelegramUserId == telegramUserId,
                cancellationToken);

        if (subscription is not null && subscription.Status == SubscriptionStatus.Active)
        {
            return new RedeemInviteResult(
                RedeemInviteStatus.AlreadySubscribed,
                new SubscriptionListItem(invite.ServiceDefinition.PublicId, invite.ServiceDefinition.Name, subscription.ExternalUserKey));
        }

        if (subscription is null)
        {
            subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                ServiceDefinitionId = invite.ServiceDefinitionId,
                TelegramUserId = telegramUserId,
                Status = SubscriptionStatus.Active,
                CreatedAtUtc = timeProvider.UtcNow,
                UpdatedAtUtc = timeProvider.UtcNow
            };

            dbContext.Subscriptions.Add(subscription);
        }

        if (invite.Type == InviteCodeType.Personal)
        {
            subscription.ExternalUserKey = invite.ExternalUserKey;
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.UpdatedAtUtc = timeProvider.UtcNow;
        invite.UsageCount += 1;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RedeemInviteResult(
            RedeemInviteStatus.Created,
            new SubscriptionListItem(invite.ServiceDefinition.PublicId, invite.ServiceDefinition.Name, subscription.ExternalUserKey));
    }

    private async Task<InviteCode?> FindValidInviteAsync(string inviteCode, CancellationToken cancellationToken)
    {
        var invite = await FindInviteAsync(inviteCode, cancellationToken);
        return invite is not null && IsInviteRedeemable(invite) ? invite : null;
    }

    private async Task<InviteCode?> FindInviteAsync(string inviteCode, CancellationToken cancellationToken)
    {
        var inviteHash = secretHasher.Hash(inviteCode.Trim());
        return await dbContext.InviteCodes
            .Include(x => x.ServiceDefinition)
            .SingleOrDefaultAsync(x => x.CodeHash == inviteHash, cancellationToken);
    }

    private bool IsInviteRedeemable(InviteCode invite)
    {
        if (invite is null || invite.IsRevoked)
        {
            return false;
        }

        if (invite.ExpiresAtUtc.HasValue && invite.ExpiresAtUtc.Value < timeProvider.UtcNow)
        {
            return false;
        }

        if (invite.UsageLimit.HasValue && invite.UsageCount >= invite.UsageLimit.Value)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> UnsubscribeAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services.SingleOrDefaultAsync(x => x.PublicId == servicePublicId, cancellationToken);
        if (service is null)
        {
            return false;
        }

        var subscription = await dbContext.Subscriptions.SingleOrDefaultAsync(
            x => x.ServiceDefinitionId == service.Id && x.TelegramUserId == telegramUserId && x.Status == SubscriptionStatus.Active,
            cancellationToken);

        if (subscription is null)
        {
            return false;
        }

        subscription.Status = SubscriptionStatus.Disabled;
        subscription.UpdatedAtUtc = timeProvider.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<SubscriptionListItem>> GetSubscriptionsAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        return await dbContext.Subscriptions.AsNoTracking()
            .Where(x => x.TelegramUserId == telegramUserId && x.Status == SubscriptionStatus.Active)
            .Include(x => x.ServiceDefinition)
            .OrderBy(x => x.ServiceDefinition.Name)
            .Select(x => new SubscriptionListItem(x.ServiceDefinition.PublicId, x.ServiceDefinition.Name, x.ExternalUserKey))
            .ToListAsync(cancellationToken);
    }

    public async Task<UpsertTemplateResult> UpsertTemplateAsync(long actingTelegramUserId, string servicePublicId, string templateKey, TemplateParseMode parseMode, string body, CancellationToken cancellationToken)
    {
        var service = await GetManagedServiceAsync(actingTelegramUserId, servicePublicId, cancellationToken);
        var template = await dbContext.NotificationTemplates
            .SingleOrDefaultAsync(x => x.ServiceDefinitionId == service.Id && x.TemplateKey == templateKey, cancellationToken);

        if (template is null)
        {
            template = new NotificationTemplate
            {
                Id = Guid.NewGuid(),
                ServiceDefinitionId = service.Id,
                TemplateKey = templateKey.Trim(),
                Body = body,
                ParseMode = parseMode,
                IsEnabled = true,
                CreatedAtUtc = timeProvider.UtcNow,
                UpdatedAtUtc = timeProvider.UtcNow
            };
            dbContext.NotificationTemplates.Add(template);
        }
        else
        {
            template.Body = body;
            template.ParseMode = parseMode;
            template.IsEnabled = true;
            template.UpdatedAtUtc = timeProvider.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new UpsertTemplateResult(service.PublicId, template.TemplateKey);
    }

    private async Task<ServiceDefinition> GetManagedServiceAsync(long actingTelegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(actingTelegramUserId, cancellationToken);
        var service = await dbContext.Services.SingleOrDefaultAsync(x => x.PublicId == servicePublicId, cancellationToken)
            ?? throw new InvalidOperationException("Сервис не найден.");

        if (context.IsSuperAdmin)
        {
            return service;
        }

        var isAdmin = await dbContext.ServiceAdmins.AnyAsync(
            x => x.ServiceDefinitionId == service.Id && x.TelegramUserId == actingTelegramUserId,
            cancellationToken);

        if (!isAdmin)
        {
            throw new InvalidOperationException("У вас нет прав на управление этим сервисом.");
        }

        return service;
    }

    private async Task<string> GenerateUniquePublicIdAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var publicId = secretGenerator.GeneratePublicId();
            var exists = await dbContext.Services.AnyAsync(x => x.PublicId == publicId, cancellationToken);
            if (!exists)
            {
                return publicId;
            }
        }

        return Guid.NewGuid().ToString("N")[..10];
    }
}
