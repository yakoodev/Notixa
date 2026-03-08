using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using TelegramNotifications.Api.Options;
using TelegramNotifications.Api.Telegram;

namespace TelegramNotifications.Api.Controllers;

[ApiController]
[Route("api/telegram")]
public sealed class TelegramWebhookController(
    ITelegramUpdateProcessor updateProcessor,
    IOptions<TelegramBotOptions> options) : ControllerBase
{
    [HttpPost("webhook")]
    public async Task<IActionResult> PostAsync([FromBody] Update update, CancellationToken cancellationToken)
    {
        if (!string.Equals(options.Value.UpdateMode, TelegramUpdateModes.Webhook, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { Error = "Webhook mode is not enabled." });
        }

        await updateProcessor.ProcessAsync(update, cancellationToken);
        return Ok();
    }
}
