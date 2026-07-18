using Microsoft.EntityFrameworkCore;
using PaymentService.Models;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Подключаем SQLite
var dbPath = "/data/payments.db";
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// Создаём базу данных и восстанавливаем незавершённые операции
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    db.Database.EnsureCreated();

    // Восстанавливаем PROCESSING операции
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    var processingOperations = db.Operations
        .Where(o => o.Status == "PROCESSING")
        .ToList();

    if (processingOperations.Any())
    {
        logger.LogInformation("Found {Count} operations in PROCESSING, resuming...", processingOperations.Count);

        foreach (var op in processingOperations)
        {
            _ = Task.Run(async () =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<PaymentDbContext>();
                optionsBuilder.UseSqlite($"Data Source=/data/payments.db");

                using var db = new PaymentDbContext(optionsBuilder.Options);
                using var httpClient = new HttpClient();

                var operation = await db.Operations.FindAsync(op.OperationId);
                if (operation == null || operation.Status != "PROCESSING") return;

                var providerUrl = configuration["PROVIDER_URL"] ?? "http://provider-simulator:8081";

                var payload = new
                {
                    operationId = operation.OperationId,
                    amount = operation.Amount,
                    currency = operation.Currency
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{providerUrl}/payments")
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Add("Idempotency-Key", operation.OperationId);
                request.Headers.Add("X-Correlation-ID", operation.OperationId);

                logger.LogInformation("Resuming payment {OperationId}", operation.OperationId);

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var response = await httpClient.SendAsync(request);

                        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < 3)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100));
                            continue;
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                            var providerPaymentId = json.GetProperty("providerPaymentId").GetGuid();

                            if (operation.ProviderPaymentId == null)
                            {
                                operation.ProviderPaymentId = providerPaymentId;
                                await db.SaveChangesAsync();
                                logger.LogInformation("Resumed payment {OperationId} got providerPaymentId {ProviderId}", operation.OperationId, providerPaymentId);
                            }
                        }
                        break;
                    }
                    catch (HttpRequestException) when (attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100 + Random.Shared.Next(100)));
                    }
                }
            });
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();