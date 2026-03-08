[Русская версия](README.md) | [English](README.en.md)

# Notixa

Notixa is a small ASP.NET Core 8 service that combines:
- a Telegram bot for platform administration and subscriptions
- a private HTTP API for sending notifications
- SQLite storage for services, invites, templates, and delivery logs

## Features

- create notification services from Telegram
- issue general or personal invite codes
- subscribe Telegram users to services with inline confirmation
- send raw text notifications or render stored templates
- manage service admins from the bot
- use screen-style bot navigation with inline buttons and unsubscribe confirmation
- run locally with SQLite or in Docker

## Stack

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core + SQLite
- Telegram.Bot
- xUnit

## Configuration

The application reads settings from `appsettings.json`, `appsettings.Development.json`, environment variables, or Docker environment overrides.

Required values:

| Key | Description |
| --- | --- |
| `TelegramBot__BotToken` | Token issued by `@BotFather` |
| `TelegramBot__BotUsername` | Bot username without `@` |
| `Security__SuperAdminTelegramUserId` | Telegram user id of the first platform admin |

Optional values:

| Key | Default | Description |
| --- | --- | --- |
| `TelegramBot__UpdateMode` | `LongPolling` | `LongPolling` or `Webhook` |
| `TelegramBot__WebhookBaseUrl` | empty | Public base URL used for webhook mode |
| `Storage__ConnectionString` | `Data Source=app_data/notixa.db` | SQLite connection string |

## Local Run

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Create a Telegram bot with `@BotFather`.
3. Export the required environment variables.
4. Start the API:

```powershell
$env:TelegramBot__BotToken="YOUR_BOT_TOKEN"
$env:TelegramBot__BotUsername="your_bot_username"
$env:Security__SuperAdminTelegramUserId="123456789"
dotnet run --project .\Notixa.Api\
```

5. Open Swagger in development mode at `https://localhost:7136/swagger` or `http://localhost:5212/swagger`.
6. Open the bot in Telegram and send `/start`.

## Docker Run

1. Copy `.env.example` to `.env`.
2. Fill in the Telegram values.
3. Start the stack:

```powershell
docker compose up --build
```

The container listens on `http://localhost:8080`.

## Telegram Commands

```text
/start
/subscriptions
/services
/create_service Name | Description
/create_template serviceId templateKey Html | Hello, {{name}}
/generate_general_invite serviceId [usageLimit] [expiresHours]
/generate_personal_invite serviceId externalUserKey [expiresHours]
/service_admins serviceId
/add_service_admin serviceId telegramUserId
/remove_service_admin serviceId telegramUserId
/allow_creator telegramUserId
/deny_creator telegramUserId
```

Any non-command text is treated as an invite code.

## Telegram UX

- `/start` creates a new bot screen with inline navigation.
- Inline button clicks edit only the message where the button was pressed.
- Invite redemption is confirmed with `✅ Yes` and `❌ No` buttons.
- Unsubscribe starts from `🗑️ Unsubscribe from {ServiceName}` and also requires confirmation.
- Service notifications are sent as separate messages and start with the service name: `🔔 {ServiceName}`.

## Notification API

Send either direct text or a stored template to all subscribers or to targeted recipients.

Example request with direct text:

```json
{
  "serviceKey": "svc_...",
  "text": "<b>Build failed</b>",
  "parseMode": "Html",
  "recipientExternalKeys": ["user1"]
}
```

Example request with a template:

```json
{
  "serviceKey": "svc_...",
  "templateKey": "build-failed",
  "variables": {
    "name": "frontend"
  }
}
```

Example `curl` call:

```bash
curl -X POST http://localhost:5212/api/notifications/send \
  -H "Content-Type: application/json" \
  -d '{
    "serviceKey": "svc_...",
    "text": "Build frontend failed",
    "parseMode": "PlainText"
  }'
```

## Typical First-Time Flow

1. Start the app.
2. Send `/start` to the bot.
3. Create a service with `/create_service Orders | Order notifications`.
4. Copy the `PublicId` and `ServiceKey` from the bot response.
5. Open `🛠️ My Services` and generate an invite with a button or `/generate_general_invite <PublicId>`.
6. Redeem the invite from the target Telegram account and confirm the subscription with `✅ Yes`.
7. Send a test notification through `/api/notifications/send`.

## Tests

```powershell
dotnet test
```

## Security Notes

- The repository intentionally does not contain bot tokens, personal Telegram ids, local databases, or helper scripts.
- Use environment variables or private local config for secrets.
- Rotate the Telegram bot token before production use if it was ever exposed outside a trusted local environment.
