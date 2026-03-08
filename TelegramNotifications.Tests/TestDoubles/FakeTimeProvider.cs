using TelegramNotifications.Api.Services;

namespace TelegramNotifications.Tests.TestDoubles;

public sealed class FakeTimeProvider(DateTimeOffset utcNow) : ITimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
