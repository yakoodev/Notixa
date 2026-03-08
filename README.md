[Русская версия](README.md) | [English](README.en.md)

# Notixa

Notixa - это небольшой сервис на ASP.NET Core 8, который объединяет:
- Telegram-бота для администрирования платформы и подписок
- приватный HTTP API для отправки уведомлений
- SQLite-хранилище для сервисов, инвайтов, шаблонов и логов доставки

## Возможности

- создание сервисов уведомлений через Telegram
- выпуск общих и персональных инвайт-кодов
- подписка Telegram-пользователей на сервисы с подтверждением через inline-кнопки
- отправка простого текста или уведомлений по шаблонам
- управление администраторами сервисов через бота
- экранная навигация бота через inline-кнопки с подтверждением отписки
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
| `Storage__ConnectionString` | `Data Source=app_data/notixa.db` | Строка подключения SQLite |

## Локальный запуск

1. Установи [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Создай Telegram-бота через `@BotFather`.
3. Задай обязательные переменные окружения.
4. Запусти API:

```powershell
$env:TelegramBot__BotToken="YOUR_BOT_TOKEN"
$env:TelegramBot__BotUsername="your_bot_username"
$env:Security__SuperAdminTelegramUserId="123456789"
dotnet run --project .\Notixa.Api\
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

## Telegram UX

- `/start` создает новый экран бота с inline-навигацией.
- Нажатия на inline-кнопки редактируют только то сообщение, в котором была нажата кнопка.
- Подписка по инвайт-коду подтверждается кнопками `✅ Да` и `❌ Нет`.
- Отписка запускается кнопкой `🗑️ Отписаться от {ServiceName}` и тоже требует подтверждения.
- Уведомления сервисов приходят отдельными сообщениями и начинаются с имени сервиса: `🔔 {ServiceName}`.

## Notification API

Можно отправлять либо прямой текст, либо сохранённый шаблон всем подписчикам или только выбранным получателям.

Базовые правила:

- Используй либо `text`, либо `templateKey`.
- Для `text` можно передать `parseMode`: `PlainText`, `Html`, `Markdown`, `MarkdownV2`.
- Если `recipientExternalKeys` не передан, уведомление уйдет всем подписчикам сервиса.
- Если `recipientExternalKeys` передан, уведомление уйдет только указанным внешним ключам.

### Варианты запросов

1. Прямой текст, `PlainText`, одному получателю

```json
{
  "serviceKey": "svc_...",
  "text": "Заказ #1234 оплачен",
  "parseMode": "PlainText",
  "recipientExternalKeys": ["user1"]
}
```

2. Прямой текст, `Html`, одному получателю

```json
{
  "serviceKey": "svc_...",
  "text": "<b>Build failed</b>",
  "parseMode": "Html",
  "recipientExternalKeys": ["user1"]
}
```

3. Прямой текст, `PlainText`, всем подписчикам

```json
{
  "serviceKey": "svc_...",
  "text": "Общее уведомление для всех подписчиков",
  "parseMode": "PlainText"
}
```

4. Прямой текст, `Html`, всем подписчикам

```json
{
  "serviceKey": "svc_...",
  "text": "<b>Сборка завершена</b>\nВсе проверки прошли успешно",
  "parseMode": "Html"
}
```

5. Шаблон, одному получателю

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

6. Шаблон, всем подписчикам

```json
{
  "serviceKey": "svc_...",
  "templateKey": "build-failed",
  "variables": {
    "name": "frontend"
  }
}
```

7. Шаблон, нескольким выбранным получателям

```json
{
  "serviceKey": "svc_...",
  "templateKey": "order-paid",
  "variables": {
    "orderId": "1234",
    "amount": "1500 ₽"
  },
  "recipientExternalKeys": ["user1", "user2", "user3"]
}
```

### Примеры JavaScript

`fetch`: прямой текст одному получателю

```js
await fetch("http://localhost:8080/api/notifications/send", {
  method: "POST",
  headers: {
    "Content-Type": "application/json"
  },
  body: JSON.stringify({
    serviceKey: "svc_...",
    text: "Заказ #1234 оплачен",
    parseMode: "PlainText",
    recipientExternalKeys: ["user1"]
  })
});
```

`fetch`: HTML всем подписчикам

```js
await fetch("http://localhost:8080/api/notifications/send", {
  method: "POST",
  headers: {
    "Content-Type": "application/json"
  },
  body: JSON.stringify({
    serviceKey: "svc_...",
    text: "<b>Сборка завершена</b>",
    parseMode: "Html"
  })
});
```

`fetch`: шаблон одному получателю

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
      customerName: "Иван",
      amount: "1500 ₽"
    },
    recipientExternalKeys: ["user1"]
  })
});
```

`fetch`: шаблон всем подписчикам

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
      customerName: "Иван",
      amount: "1500 ₽"
    }
  })
});
```

### Примеры curl

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

## Типовой первый сценарий

1. Запусти приложение.
2. Отправь `/start` боту.
3. Создай сервис командой `/create_service Orders | Order notifications`.
4. Скопируй `PublicId` и `ServiceKey` из ответа бота.
5. Открой `🛠️ Мои сервисы` и создай инвайт кнопкой или командой `/generate_general_invite <PublicId>`.
6. Активируй инвайт с целевого Telegram-аккаунта и подтверди подписку кнопкой `✅ Да`.
7. Отправь тестовое уведомление через `/api/notifications/send`.

## Тесты

```powershell
dotnet test
```

## Безопасность

- Репозиторий намеренно не содержит bot token, персональные Telegram id, локальные базы данных и helper-скрипты.
- Для секретов используй переменные окружения или приватный локальный конфиг.
- Если bot token когда-либо светился вне доверенной локальной среды, перед продом его нужно перевыпустить.
