using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notixa.Api.Data;
using Notixa.Api.Domain.Entities;
using Notixa.Api.Options;
using Microsoft.Data.Sqlite;

namespace Notixa.Api.Services;

public sealed class StartupDatabaseInitializer(
    ApplicationDbContext dbContext,
    ITimeProvider timeProvider,
    IOptions<AppSecurityOptions> securityOptions,
    ILogger<StartupDatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await RepairLegacySubscriptionSchemaAsync(cancellationToken);

        var superAdminId = securityOptions.Value.SuperAdminTelegramUserId;
        if (superAdminId == 0)
        {
            logger.LogWarning("Super admin Telegram user id is not configured.");
            return;
        }

        var existing = await dbContext.Users.SingleOrDefaultAsync(x => x.TelegramUserId == superAdminId, cancellationToken);
        if (existing is null)
        {
            dbContext.Users.Add(new AppUser
            {
                TelegramUserId = superAdminId,
                DisplayName = $"super-admin-{superAdminId}",
                CanCreateServices = true,
                CreatedAtUtc = timeProvider.UtcNow,
                UpdatedAtUtc = timeProvider.UtcNow
            });
        }
        else
        {
            existing.CanCreateServices = true;
            existing.UpdatedAtUtc = timeProvider.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RepairLegacySubscriptionSchemaAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('Subscriptions');";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("UserTelegramUserId"))
        {
            return;
        }

        logger.LogWarning("Legacy subscription schema detected. Repairing Subscriptions table.");

        var sql = """
            PRAGMA foreign_keys=OFF;

            CREATE TABLE "__Subscriptions_New" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Subscriptions" PRIMARY KEY,
                "ServiceDefinitionId" TEXT NOT NULL,
                "TelegramUserId" INTEGER NOT NULL,
                "ExternalUserKey" TEXT NULL,
                "Status" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_Subscriptions_Services_ServiceDefinitionId" FOREIGN KEY ("ServiceDefinitionId") REFERENCES "Services" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_Subscriptions_Users_TelegramUserId" FOREIGN KEY ("TelegramUserId") REFERENCES "Users" ("TelegramUserId") ON DELETE CASCADE
            );

            INSERT INTO "__Subscriptions_New" (
                "Id",
                "ServiceDefinitionId",
                "TelegramUserId",
                "ExternalUserKey",
                "Status",
                "CreatedAtUtc",
                "UpdatedAtUtc"
            )
            SELECT
                "Id",
                "ServiceDefinitionId",
                COALESCE("TelegramUserId", "UserTelegramUserId"),
                "ExternalUserKey",
                "Status",
                "CreatedAtUtc",
                "UpdatedAtUtc"
            FROM "Subscriptions";

            DROP TABLE "Subscriptions";
            ALTER TABLE "__Subscriptions_New" RENAME TO "Subscriptions";

            CREATE UNIQUE INDEX "IX_Subscriptions_ServiceDefinitionId_TelegramUserId"
                ON "Subscriptions" ("ServiceDefinitionId", "TelegramUserId");

            CREATE INDEX "IX_Subscriptions_ServiceDefinitionId_ExternalUserKey"
                ON "Subscriptions" ("ServiceDefinitionId", "ExternalUserKey");

            PRAGMA foreign_keys=ON;
            """;

        if (connection is SqliteConnection sqliteConnection)
        {
            await using var repairCommand = sqliteConnection.CreateCommand();
            repairCommand.CommandText = sql;
            await repairCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }
}
