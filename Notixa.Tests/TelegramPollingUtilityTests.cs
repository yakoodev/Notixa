using Notixa.Api.Telegram;
using System.Net;
using System.Net.Http;
using Telegram.Bot.Exceptions;

namespace Notixa.Tests;

public sealed class TelegramPollingUtilityTests
{
    [Fact]
    public void BotClientFactory_CreatesHttpClientWithHttp11AndExtendedTimeout()
    {
        using var client = TelegramBotClientFactory.CreateHttpClient();

        Assert.Equal(HttpVersion.Version11, client.DefaultRequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, client.DefaultVersionPolicy);
        Assert.True(client.Timeout > TelegramBotClientFactory.PollingRequestTimeout);
    }

    [Fact]
    public void ErrorClassifier_TreatsRequestExceptionsAsTransient()
    {
        var exception = new RequestException("service failure", new HttpRequestException("network"));

        Assert.True(TelegramPollingErrorClassifier.IsTransient(exception));
    }

    [Fact]
    public void ErrorClassifier_TreatsPlainHttpRequestExceptionAsTransient()
    {
        Assert.True(TelegramPollingErrorClassifier.IsTransient(new HttpRequestException("network")));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(10, 30)]
    public void Backoff_ReturnsExpectedDelay(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TelegramPollingBackoff.GetDelay(attempt));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void LogPolicy_EscalatesWarningOnlyAtThreshold(int attempt, bool expected)
    {
        Assert.Equal(expected, TelegramPollingLogPolicy.ShouldEscalateWarning(attempt));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    public void LogPolicy_LogsRecoveryOnlyAfterDegradation(int attempt, bool expected)
    {
        Assert.Equal(expected, TelegramPollingLogPolicy.ShouldLogRecovery(attempt));
    }
}
