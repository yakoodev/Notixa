namespace Notixa.Api.Domain.Entities;

public sealed class NotificationLog
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public ServiceDefinition ServiceDefinition { get; set; } = null!;

    public string TemplateKey { get; set; } = string.Empty;

    public int ResolvedRecipientsCount { get; set; }

    public int SuccessfulDeliveriesCount { get; set; }

    public int FailedDeliveriesCount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FailureDetails { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
