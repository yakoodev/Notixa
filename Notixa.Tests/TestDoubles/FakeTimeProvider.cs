using Notixa.Api.Services;

namespace Notixa.Tests.TestDoubles;

public sealed class FakeTimeProvider(DateTimeOffset utcNow) : ITimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
