using Microsoft.Extensions.DependencyInjection;
using TelegramNotifications.Api.Contracts.Notifications;
using TelegramNotifications.Api.Domain.Enums;
using TelegramNotifications.Api.Services;
using TelegramNotifications.Tests.TestDoubles;
using TelegramNotifications.Tests.TestInfrastructure;

namespace TelegramNotifications.Tests;

public sealed class NotificationDispatchServiceTests
{
    [Fact]
    public async Task DispatchAsync_BroadcastsToAllActiveSubscriptions()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        await admin.UpsertTemplateAsync(777, service.PublicId, "build-failed", TemplateParseMode.PlainText, "Hello {{name}}", CancellationToken.None);
        var invite = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.General, null, 10, 24, CancellationToken.None);
        await admin.RedeemInviteAsync(1001, invite.InviteCode, CancellationToken.None);
        await admin.RedeemInviteAsync(1002, invite.InviteCode, CancellationToken.None);

        var result = await dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey,
            TemplateKey = "build-failed",
            Variables = new Dictionary<string, object?> { ["name"] = "Yakoo" },
            Broadcast = true
        }, CancellationToken.None);

        Assert.Equal(2, result.ResolvedRecipientsCount);
        Assert.Equal(2, sender.SentMessages.Count);
    }

    [Fact]
    public async Task DispatchAsync_TargetsSpecificExternalUserKeys()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        await admin.UpsertTemplateAsync(777, service.PublicId, "targeted", TemplateParseMode.PlainText, "Hello {{name}}", CancellationToken.None);
        var inviteA = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.Personal, "user-a", 1, 24, CancellationToken.None);
        var inviteB = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.Personal, "user-b", 1, 24, CancellationToken.None);
        await admin.RedeemInviteAsync(2001, inviteA.InviteCode, CancellationToken.None);
        await admin.RedeemInviteAsync(2002, inviteB.InviteCode, CancellationToken.None);

        var result = await dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey,
            TemplateKey = "targeted",
            Variables = new Dictionary<string, object?> { ["name"] = "Target" },
            RecipientExternalKeys = ["user-b"]
        }, CancellationToken.None);

        Assert.Equal(1, result.ResolvedRecipientsCount);
        Assert.Single(sender.SentMessages);
        Assert.Equal(2002, sender.SentMessages[0].UserId);
    }

    [Fact]
    public async Task DispatchAsync_RejectsInvalidServiceKey()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = "bad",
            TemplateKey = "x"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task DispatchAsync_RejectsUnknownTemplate()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey,
            TemplateKey = "missing"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task DispatchAsync_AllowsDirectPlainTextWithoutTemplate()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.Personal, "user-a", 1, 24, CancellationToken.None);
        await admin.RedeemInviteAsync(2001, invite.InviteCode, CancellationToken.None);

        var result = await dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey,
            Text = "Прямое уведомление",
            RecipientExternalKeys = ["user-a"]
        }, CancellationToken.None);

        Assert.Equal(1, result.ResolvedRecipientsCount);
        Assert.Single(sender.SentMessages);
        Assert.Equal("Прямое уведомление", sender.SentMessages[0].Text);
        Assert.Equal(TemplateParseMode.PlainText, sender.SentMessages[0].ParseMode);
    }

    [Fact]
    public async Task DispatchAsync_AllowsDirectHtmlWithoutTemplate()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.Personal, "user-a", 1, 24, CancellationToken.None);
        await admin.RedeemInviteAsync(2001, invite.InviteCode, CancellationToken.None);

        var result = await dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey,
            Text = "<b>Важное уведомление</b>",
            ParseMode = TemplateParseMode.Html,
            RecipientExternalKeys = ["user-a"]
        }, CancellationToken.None);

        Assert.Equal(1, result.ResolvedRecipientsCount);
        Assert.Single(sender.SentMessages);
        Assert.Equal("<b>Важное уведомление</b>", sender.SentMessages[0].Text);
        Assert.Equal(TemplateParseMode.Html, sender.SentMessages[0].ParseMode);
    }

    [Fact]
    public async Task DispatchAsync_RejectsRequestWithoutTemplateOrText()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey
        }, CancellationToken.None));
    }

    [Fact]
    public async Task DispatchAsync_RejectsMissingVariables()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        await admin.UpsertTemplateAsync(777, service.PublicId, "missing-vars", TemplateParseMode.PlainText, "Hello {{name}}", CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => dispatch.DispatchAsync(new SendNotificationRequest
        {
            ServiceKey = service.ServiceKey,
            TemplateKey = "missing-vars",
            Variables = new Dictionary<string, object?>()
        }, CancellationToken.None));
    }
}
