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

## Telegram UX

The bot no longer uses slash commands as its working interface. Management now lives in inline buttons and screen-driven flows.

What text input is still used for:

- `/start` can be used as an entry point and reset back to the home screen
- any other text is treated either as input for the active flow or as an invite code
- manual commands like `/services` or `/create_service ...` are no longer part of the supported UX

Main flows:

- `/start` opens the home screen with `📬 My Subscriptions`, `🛠️ My Services`, and `👑 Creators` for the super-admin
- inline button clicks edit only the message where the button was pressed
- service creation, template creation, personal invites, and admin management are handled through buttons plus guided input
- invite redemption is confirmed with `✅ Yes` and `❌ No` buttons
- unsubscribe starts from `🗑️ Unsubscribe from {ServiceName}` and also requires confirmation
- service notifications are sent as separate messages and start with the service name: `🔔 {ServiceName}`

## Notification API

Send either direct text or a stored template to all subscribers or to targeted recipients.

Basic rules:

- Use either `text` or `templateKey`.
- For `text`, you can pass `parseMode`: `PlainText`, `Html`, `Markdown`, or `MarkdownV2`.
- If `recipientExternalKeys` is omitted, the notification is sent to all subscribers of the service.
- If `recipientExternalKeys` is present, the notification is sent only to the listed external keys.

### Request combinations

1. Direct text, `PlainText`, one recipient

```json
{
  "serviceKey": "svc_...",
  "text": "Order #1234 has been paid",
  "parseMode": "PlainText",
  "recipientExternalKeys": ["user1"]
}
```

2. Direct text, `Html`, one recipient

```json
{
  "serviceKey": "svc_...",
  "text": "<b>Build failed</b>",
  "parseMode": "Html",
  "recipientExternalKeys": ["user1"]
}
```

3. Direct text, `PlainText`, all subscribers

```json
{
  "serviceKey": "svc_...",
  "text": "Broadcast notification to all subscribers",
  "parseMode": "PlainText"
}
```

4. Direct text, `Html`, all subscribers

```json
{
  "serviceKey": "svc_...",
  "text": "<b>Deployment finished</b>\nAll checks passed",
  "parseMode": "Html"
}
```

5. Template, one recipient

```json
{
  "serviceKey": "svc_...",
  "templateKey": "build-failed",
  "variables": {
    "name": "frontend"
  },
  "recipientExternalKeys": ["user1"]
}
```

6. Template, all subscribers

```json
{
  "serviceKey": "svc_...",
  "templateKey": "build-failed",
  "variables": {
    "name": "frontend"
  }
}
```

7. Template, selected group of recipients

```json
{
  "serviceKey": "svc_...",
  "templateKey": "order-paid",
  "variables": {
    "orderId": "1234",
    "amount": "$15.00"
  },
  "recipientExternalKeys": ["user1", "user2", "user3"]
}
```

### JavaScript examples

`fetch`: direct text to one recipient

```js
await fetch("http://localhost:8080/api/notifications/send", {
  method: "POST",
  headers: {
    "Content-Type": "application/json"
  },
  body: JSON.stringify({
    serviceKey: "svc_...",
    text: "Order #1234 has been paid",
    parseMode: "PlainText",
    recipientExternalKeys: ["user1"]
  })
});
```

`fetch`: HTML to all subscribers

```js
await fetch("http://localhost:8080/api/notifications/send", {
  method: "POST",
  headers: {
    "Content-Type": "application/json"
  },
  body: JSON.stringify({
    serviceKey: "svc_...",
    text: "<b>Deployment finished</b>",
    parseMode: "Html"
  })
});
```

`fetch`: template to one recipient

```js
await fetch("http://localhost:8080/api/notifications/send", {
  method: "POST",
  headers: {
    "Content-Type": "application/json"
  },
  body: JSON.stringify({
    serviceKey: "svc_...",
    templateKey: "order-paid",
    variables: {
      orderId: "1234",
      customerName: "John",
      amount: "$15.00"
    },
    recipientExternalKeys: ["user1"]
  })
});
```

`fetch`: template to all subscribers

```js
await fetch("http://localhost:8080/api/notifications/send", {
  method: "POST",
  headers: {
    "Content-Type": "application/json"
  },
  body: JSON.stringify({
    serviceKey: "svc_...",
    templateKey: "order-paid",
    variables: {
      orderId: "1234",
      customerName: "John",
      amount: "$15.00"
    }
  })
});
```

### curl examples

```bash
curl -X POST http://localhost:5212/api/notifications/send \
  -H "Content-Type: application/json" \
  -d '{
    "serviceKey": "svc_...",
    "text": "Build frontend failed",
    "parseMode": "PlainText"
  }'
```

```bash
curl -X POST http://localhost:5212/api/notifications/send \
  -H "Content-Type: application/json" \
  -d '{
    "serviceKey": "svc_...",
    "text": "<b>Build failed</b>",
    "parseMode": "Html",
    "recipientExternalKeys": ["user1"]
  }'
```

```bash
curl -X POST http://localhost:5212/api/notifications/send \
  -H "Content-Type: application/json" \
  -d '{
    "serviceKey": "svc_...",
    "templateKey": "build-failed",
    "variables": {
      "name": "frontend"
    }
  }'
```

## Typical First-Time Flow

1. Start the app.
2. Send `/start` to the bot.
3. Open `➕ Create Service` and complete the guided creation flow.
4. Copy the `PublicId` and `ServiceKey` from the bot response.
5. Open `🛠️ My Services`, choose the service, and create a general invite with `🎟️ General Invite`.
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
