using Microsoft.Extensions.Options;
using TelegramNotifications.Api.Options;

namespace TelegramNotifications.Tests;

public sealed class TelegramModeConfigurationTests
{
    [Theory]
    [InlineData(TelegramUpdateModes.LongPolling)]
    [InlineData(TelegramUpdateModes.Webhook)]
    public void Options_AcceptBothSupportedModes(string mode)
    {
        var options = Options.Create(new TelegramBotOptions
        {
            UpdateMode = mode
        });

        Assert.Equal(mode, options.Value.UpdateMode);
    }
}
