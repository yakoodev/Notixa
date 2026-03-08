using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Notixa.Api.Data;
using Notixa.Api.Contracts;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Services;
using Notixa.Tests.TestDoubles;
using Notixa.Tests.TestInfrastructure;

namespace Notixa.Tests;

public sealed class PlatformAdministrationServiceTests
{
    [Fact]
    public async Task InitializeAsync_BootstrapsSuperAdmin()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();

        await initializer.InitializeAsync(CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var superAdmin = await db.Users.FindAsync(777L);
        Assert.NotNull(superAdmin);
        Assert.True(superAdmin!.CanCreateServices);
    }

    [Fact]
    public async Task CreateServiceAsync_RequiresCreatorPermission()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);

        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.CreateServiceAsync(123, "Svc", "Desc", CancellationToken.None));
    }

    [Fact]
    public async Task CreateServiceAsync_ReturnsPlaintextKeyAndPersistsService()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var result = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.PublicId));
        Assert.StartsWith("svc_", result.ServiceKey);
    }

    [Theory]
    [InlineData("   ", "Description", "Название сервиса не может быть пустым.")]
    [InlineData("Название", "Description", "Замените шаблонное значение 'Название' на реальное имя сервиса.")]
    [InlineData("Orders", "Описание", "Замените шаблонное значение 'Описание' на реальное описание сервиса.")]
    public async Task CreateServiceAsync_RejectsEmptyOrPlaceholderValues(string name, string description, string expectedMessage)
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateServiceAsync(777, name, description, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task RedeemInviteAsync_SupportsGeneralInvite()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.General, null, 5, 24, CancellationToken.None);
        var redeem = await service.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);
        var subscription = redeem.Subscription;

        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Created, redeem.Status);
        Assert.NotNull(subscription);
        Assert.Equal("Orders", subscription!.ServiceName);
        Assert.Null(subscription.ExternalUserKey);
    }

    [Fact]
    public async Task RedeemInviteAsync_PersonalInviteAssignsExternalUserKey()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.Personal, "customer-42", 1, 24, CancellationToken.None);
        var redeem = await service.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);
        var subscription = redeem.Subscription;

        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Created, redeem.Status);
        Assert.NotNull(subscription);
        Assert.Equal("customer-42", subscription!.ExternalUserKey);
    }

    [Fact]
    public async Task RedeemInviteAsync_RejectsExpiredOrDepletedInvite()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), time);
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.General, null, 1, 1, CancellationToken.None);

        var first = await service.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);
        var second = await service.RedeemInviteAsync(333, invite.InviteCode, CancellationToken.None);

        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Created, first.Status);
        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Invalid, second.Status);

        var expiringInvite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.General, null, 5, 1, CancellationToken.None);
        time.UtcNow = time.UtcNow.AddHours(2);
        var expired = await service.RedeemInviteAsync(444, expiringInvite.InviteCode, CancellationToken.None);
        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Invalid, expired.Status);
    }

    [Fact]
    public async Task PreviewInviteAsync_ReturnsServiceWithoutConsumingInvite()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.General, null, 2, 24, CancellationToken.None);

        var preview = await service.PreviewInviteAsync(222, invite.InviteCode, CancellationToken.None);
        var storedInvite = await db.InviteCodes.SingleAsync(x => x.ServiceDefinitionId == db.Services.Single().Id, CancellationToken.None);

        Assert.Equal(PreviewInviteStatus.Available, preview.Status);
        Assert.Equal("Orders", preview.ServiceName);
        Assert.Equal(0, storedInvite.UsageCount);
    }

    [Fact]
    public async Task PreviewInviteAsync_ReturnsAlreadySubscribedForActiveSubscription()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.Personal, "customer-42", 1, 24, CancellationToken.None);
        await service.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);

        var preview = await service.PreviewInviteAsync(222, invite.InviteCode, CancellationToken.None);

        Assert.Equal(PreviewInviteStatus.AlreadySubscribed, preview.Status);
        Assert.Equal("customer-42", preview.ExternalUserKey);
    }

    [Fact]
    public async Task PreviewThenRedeem_ConsumesInviteOnlyOnce()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.General, null, 1, 24, CancellationToken.None);

        var preview = await service.PreviewInviteAsync(222, invite.InviteCode, CancellationToken.None);
        var redeem = await service.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);
        var storedInvite = await db.InviteCodes.SingleAsync(CancellationToken.None);

        Assert.Equal(PreviewInviteStatus.Available, preview.Status);
        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Created, redeem.Status);
        Assert.Equal(1, storedInvite.UsageCount);
    }

    [Fact]
    public async Task RedeemInviteAsync_DoesNotOverwriteExistingSubscription()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var firstInvite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.Personal, "user-a", 1, 24, CancellationToken.None);
        var secondInvite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.Personal, "user-b", 1, 24, CancellationToken.None);

        var firstRedeem = await service.RedeemInviteAsync(222, firstInvite.InviteCode, CancellationToken.None);
        var secondRedeem = await service.RedeemInviteAsync(222, secondInvite.InviteCode, CancellationToken.None);

        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.Created, firstRedeem.Status);
        Assert.Equal(Notixa.Api.Contracts.RedeemInviteStatus.AlreadySubscribed, secondRedeem.Status);
        Assert.Equal("user-a", secondRedeem.Subscription!.ExternalUserKey);
    }

    [Fact]
    public async Task UnsubscribeAsync_DisablesActiveSubscription()
    {
        var provider = TestServiceFactory.Create(777, new FakeTelegramMessageSender(), new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var service = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var created = await service.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await service.CreateInviteAsync(777, created.PublicId, InviteCodeType.General, null, 1, 24, CancellationToken.None);
        await service.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);

        var removed = await service.UnsubscribeAsync(222, created.PublicId, CancellationToken.None);
        var subscriptions = await service.GetSubscriptionsAsync(222, CancellationToken.None);

        Assert.True(removed);
        Assert.Empty(subscriptions);
    }
}
