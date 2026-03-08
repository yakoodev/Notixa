namespace Notixa.Api.Options;

public sealed class AppSecurityOptions
{
    public const string SectionName = "Security";

    public long SuperAdminTelegramUserId { get; set; }
}
