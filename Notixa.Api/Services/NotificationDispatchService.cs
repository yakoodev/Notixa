using Microsoft.EntityFrameworkCore;
using Notixa.Api.Contracts.Notifications;
using Notixa.Api.Data;
using Notixa.Api.Domain.Entities;
using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public sealed class NotificationDispatchService(
    ApplicationDbContext dbContext,
    ISecretHasher secretHasher,
    ITemplateRenderer templateRenderer,
    ITelegramMessageSender telegramMessageSender,
    ITimeProvider timeProvider) : INotificationDispatchService
{
    public async Task<NotificationDispatchResult> DispatchAsync(SendNotificationRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var serviceKeyHash = secretHasher.Hash(request.ServiceKey.Trim());
        var service = await dbContext.Services
            .SingleOrDefaultAsync(x => x.ServiceKeyHash == serviceKeyHash && x.Status == ServiceStatus.Active, cancellationToken);

        if (service is null)
        {
            throw new InvalidOperationException("Invalid service key.");
        }

        var hasDirectText = !string.IsNullOrWhiteSpace(request.Text);
        var hasTemplateKey = !string.IsNullOrWhiteSpace(request.TemplateKey);
        if (!hasDirectText && !hasTemplateKey)
        {
            throw new ArgumentException("Either text or templateKey must be provided.");
        }

        TemplateRenderResult renderResult;
        string? templateKeyForLog;
        if (hasDirectText)
        {
            renderResult = new TemplateRenderResult(
                true,
                request.Text!.Trim(),
                Array.Empty<string>(),
                request.ParseMode ?? TemplateParseMode.PlainText);
            templateKeyForLog = request.TemplateKey?.Trim();
        }
        else
        {
            var templateKey = request.TemplateKey!.Trim();
            var template = await dbContext.NotificationTemplates
                .SingleOrDefaultAsync(
                    x => x.ServiceDefinitionId == service.Id && x.TemplateKey == templateKey && x.IsEnabled,
                    cancellationToken);

            if (template is null)
            {
                throw new InvalidOperationException("Template not found.");
            }

            renderResult = templateRenderer.Render(template.Body, request.Variables, template.ParseMode);
            if (!renderResult.IsSuccess)
            {
                throw new ArgumentException($"Missing variables: {string.Join(", ", renderResult.MissingVariables)}");
            }

            templateKeyForLog = template.TemplateKey;
        }

        var recipientKeys = request.RecipientExternalKeys?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var query = dbContext.Subscriptions.AsNoTracking()
            .Where(x => x.ServiceDefinitionId == service.Id && x.Status == SubscriptionStatus.Active);

        if (recipientKeys is { Length: > 0 })
        {
            query = query.Where(x => x.ExternalUserKey != null && recipientKeys.Contains(x.ExternalUserKey));
        }

        var recipients = await query.ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            try
            {
                await telegramMessageSender.SendAsync(recipient.TelegramUserId, renderResult.RenderedText, renderResult.ParseMode, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"User {recipient.TelegramUserId}: {ex.Message}");
            }
        }

        var log = new NotificationLog
        {
            Id = Guid.NewGuid(),
            ServiceDefinitionId = service.Id,
            TemplateKey = templateKeyForLog ?? string.Empty,
            ResolvedRecipientsCount = recipients.Count,
            SuccessfulDeliveriesCount = recipients.Count - errors.Count,
            FailedDeliveriesCount = errors.Count,
            Status = errors.Count == 0 ? "Succeeded" : (recipients.Count == 0 ? "NoRecipients" : "PartialFailure"),
            FailureDetails = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
            CreatedAtUtc = timeProvider.UtcNow
        };

        dbContext.NotificationLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new NotificationDispatchResult(log.Id, log.ResolvedRecipientsCount, log.SuccessfulDeliveriesCount, log.FailedDeliveriesCount, errors);
    }
}
