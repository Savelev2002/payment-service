# Payment Service

Сервис обработки платежей с идемпотентностью и восстановлением после сбоев.

## Требования
- Docker Desktop

## Запуск
```bash
docker compose up --build
```

## Сквозной сценарий

### 1. Проверка здоровья:

```bash
curl http://localhost:8080/health
```

### 2. Создание операции:

```bash
curl -X POST http://localhost:8080/operations \
  -H "Content-Type: application/json" \
  -d '{"operationId":"test-001","amount":"1000.00","currency":"RUB","description":"Тест"}'
```

### 3. Отправка:
```bash
curl -X POST http://localhost:8080/operations/test-001/submit
```

### 4. Проверка статуса:
```bash
curl http://localhost:8080/operations/test-001
```

### 5. История:
```bash
curl http://localhost:8080/operations/test-001/events
```