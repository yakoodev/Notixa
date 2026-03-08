using Microsoft.AspNetCore.Mvc;
using TelegramNotifications.Api.Contracts.Notifications;
using TelegramNotifications.Api.Services;

namespace TelegramNotifications.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationDispatchService dispatchService) : ControllerBase
{
    [HttpPost("send")]
    [ProducesResponseType(typeof(SendNotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SendNotificationResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendAsync([FromBody] SendNotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await dispatchService.DispatchAsync(request, cancellationToken);
            return Ok(new SendNotificationResponse
            {
                LogId = result.LogId,
                ResolvedRecipientsCount = result.ResolvedRecipientsCount,
                SuccessfulDeliveriesCount = result.SuccessfulDeliveriesCount,
                FailedDeliveriesCount = result.FailedDeliveriesCount,
                Errors = result.Errors
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new SendNotificationResponse
            {
                Errors = new[] { ex.Message }
            });
        }
    }
}
