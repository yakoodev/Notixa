namespace Notixa.Api.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ConnectionString { get; set; } = "Data Source=app_data/notixa.db";
}
