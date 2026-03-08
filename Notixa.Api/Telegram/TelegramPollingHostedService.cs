using Telegram.Bot;
using Microsoft.Extensions.Options;
using Notixa.Api.Options;

namespace Notixa.Api.Telegram;

public sealed class TelegramPollingHostedService(
    IServiceScopeFactory scopeFactory,
    IBotClientAccessor botClientAccessor,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramPollingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!botClientAccessor.IsConfigured)
        {
            logger.LogWarning("Telegram bot token is not configured. Polling worker is disabled.");
            return;
        }

        if (!string.Equals(options.Value.UpdateMode, TelegramUpdateModes.LongPolling, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Telegram update mode is {Mode}. Polling worker is idle.", options.Value.UpdateMode);
            return;
        }

        var client = botClientAccessor.Client!;
        var offset = 0;
        var transientFailureCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdates(offset, timeout: 30, cancellationToken: stoppingToken);
                if (TelegramPollingLogPolicy.ShouldLogRecovery(transientFailureCount))
                {
                    logger.LogInformation(
                        "Telegram polling recovered after {FailureCount} transient failures.",
                        transientFailureCount);
                }

                transientFailureCount = 0;
                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ITelegramUpdateProcessor>();
                    await processor.ProcessAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (TelegramPollingErrorClassifier.IsTransient(ex))
            {
                transientFailureCount++;
                var delay = TelegramPollingBackoff.GetDelay(transientFailureCount);
                logger.LogDebug(
                    ex,
                    "Telegram polling transient failure #{FailureCount}. Retrying in {DelaySeconds} seconds.",
                    transientFailureCount,
                    delay.TotalSeconds);
                if (TelegramPollingLogPolicy.ShouldEscalateWarning(transientFailureCount))
                {
                    logger.LogWarning(
                        "Telegram polling is degraded after {FailureCount} transient failures in a row.",
                        transientFailureCount);
                }

                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram polling iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
