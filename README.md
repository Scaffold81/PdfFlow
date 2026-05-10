# PdfFlow

Система обработки PDF-документов на базе двух микросервисов с асинхронной обработкой через RabbitMQ.

## Архитектура

```
Client → POST /api/documents
       → API: сохраняет файл в /app/storage + запись в БД (status=pending)
       → API: публикует {documentId, filePath} в RabbitMQ
       → Worker: получает сообщение → status=processing
       → Worker: PdfPig извлекает текст постранично
       → Worker: сохраняет текст → status=completed/failed
       → Client: GET /api/documents/{id}/content
```

## Запуск

```bash
docker compose up --build
```

| Сервис           | URL                                  |
|------------------|--------------------------------------|
| API + Swagger    | http://localhost:8080/swagger        |
| RabbitMQ UI      | http://localhost:15672 (guest/guest) |

## REST API

| Метод | Путь                            | Описание                  |
|-------|---------------------------------|---------------------------|
| POST  | /api/documents                  | Загрузить PDF             |
| GET   | /api/documents                  | Список документов         |
| GET   | /api/documents/{id}/content     | Извлечённый текст         |

## Статусы

| Статус       | Описание                              |
|--------------|---------------------------------------|
| Pending      | Загружен, ожидает обработки           |
| Processing   | Worker взял задачу                    |
| Completed    | Текст успешно извлечён                |
| Failed       | Ошибка (см. errorMessage)             |

## Стек

- ASP.NET Core 8 / .NET Worker Service
- PostgreSQL 16 + EF Core
- RabbitMQ 3.13
- UglyToad.PdfPig (pure .NET PDF parser)
- Docker + Docker Compose
