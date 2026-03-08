[Русская версия](README.md) | [English](README.en.md)

# Notixa

Notixa - это небольшой сервис на ASP.NET Core 8, который объединяет:
- Telegram-бота для администрирования платформы и подписок
- приватный HTTP API для отправки уведомлений
- SQLite-хранилище для сервисов, инвайтов, шаблонов и логов доставки

## Возможности

- создание сервисов уведомлений через Telegram
- выпуск общих и персональных инвайт-кодов
- подписка Telegram-пользователей на сервисы
- отправка простого текста или уведомлений по шаблонам
- управление администраторами сервисов через бота
- локальный запуск с SQLite или в Docker

## Стек

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core + SQLite
- Telegram.Bot
- xUnit

## Конфигурация

Приложение читает настройки из `appsettings.json`, `appsettings.Development.json`, переменных окружения и Docker-переопределений.

Обязательные параметры:

| Ключ | Описание |
| --- | --- |
| `TelegramBot__BotToken` | Токен, выданный `@BotFather` |
| `TelegramBot__BotUsername` | Username бота без `@` |
| `Security__SuperAdminTelegramUserId` | Telegram user id первого администратора платформы |

Необязательные параметры:

| Ключ | По умолчанию | Описание |
| --- | --- | --- |
| `TelegramBot__UpdateMode` | `LongPolling` | `LongPolling` или `Webhook` |
| `TelegramBot__WebhookBaseUrl` | пусто | Публичный базовый URL для webhook-режима |
| `Storage__ConnectionString` | `Data Source=app_data/telegram-notifications.db` | Строка подключения SQLite |

## Локальный запуск

1. Установи [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Создай Telegram-бота через `@BotFather`.
3. Задай обязательные переменные окружения.
4. Запусти API:

```powershell
$env:TelegramBot__BotToken="YOUR_BOT_TOKEN"
$env:TelegramBot__BotUsername="your_bot_username"
$env:Security__SuperAdminTelegramUserId="123456789"
dotnet run --project .\TelegramNotifications.Api\
```

5. В режиме разработки Swagger будет доступен по `https://localhost:7136/swagger` или `http://localhost:5212/swagger`.
6. Открой бота в Telegram и отправь `/start`.

## Запуск в Docker

1. Скопируй `.env.example` в `.env`.
2. Заполни Telegram-параметры.
3. Подними контейнер:

```powershell
docker compose up --build
```

Контейнер слушает `http://localhost:8080`.

## Команды бота

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

Любой некомандный текст обрабатывается как инвайт-код.

## Notification API

Можно отправлять либо прямой текст, либо сохранённый шаблон всем подписчикам или только выбранным получателям.

Пример запроса с прямым текстом:

```json
{
  "serviceKey": "svc_...",
  "text": "<b>Build failed</b>",
  "parseMode": "Html",
  "recipientExternalKeys": ["user1"]
}
```

Пример запроса с шаблоном:

```json
{
  "serviceKey": "svc_...",
  "templateKey": "build-failed",
  "variables": {
    "name": "frontend"
  }
}
```

Пример `curl`-запроса:

```bash
curl -X POST http://localhost:5212/api/notifications/send \
  -H "Content-Type: application/json" \
  -d '{
    "serviceKey": "svc_...",
    "text": "Build frontend failed",
    "parseMode": "PlainText"
  }'
```

## Типовой первый сценарий

1. Запусти приложение.
2. Отправь `/start` боту.
3. Создай сервис командой `/create_service Orders | Order notifications`.
4. Скопируй `PublicId` и `ServiceKey` из ответа бота.
5. Создай инвайт через `/generate_general_invite <PublicId>`.
6. Активируй инвайт с целевого Telegram-аккаунта.
7. Отправь тестовое уведомление через `/api/notifications/send`.

## Тесты

```powershell
dotnet test
```

## Безопасность

- Репозиторий намеренно не содержит bot token, персональные Telegram id, локальные базы данных и helper-скрипты.
- Для секретов используй переменные окружения или приватный локальный конфиг.
- Если bot token когда-либо светился вне доверенной локальной среды, перед продом его нужно перевыпустить.
