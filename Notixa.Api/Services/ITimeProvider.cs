namespace Notixa.Api.Services;

public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}
