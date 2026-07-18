using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentService.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace PaymentService.Controllers;

[ApiController]
public class OperationsController : ControllerBase
{
    private readonly PaymentDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OperationsController> _logger;

    public OperationsController(
        PaymentDbContext db,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OperationsController> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("/health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy" });
    }

    [HttpPost("/operations")]
    public async Task<IActionResult> CreateOperation([FromBody] CreateOperationRequest request)
    {
        if (string.IsNullOrEmpty(request.OperationId))
            return BadRequest(new { error = "operationId обязателен" });

        if (await _db.Operations.AnyAsync(o => o.OperationId == request.OperationId))
            return Conflict(new { error = "Операция уже существует" });

        var operation = new Operation
        {
            OperationId = request.OperationId,
            Amount = request.Amount,
            Currency = request.Currency,
            Description = request.Description,
            Status = "CREATED",
            ProviderPaymentId = null
        };

        operation.Events.Add(new OperationEvent
        {
            OperationId = operation.OperationId,
            Type = "CREATED",
            FromStatus = null,
            ToStatus = "CREATED",
            Message = "Operation created",
            OccurredAt = DateTime.UtcNow
        });

        _db.Operations.Add(operation);
        await _db.SaveChangesAsync();

        return Created($"/operations/{operation.OperationId}", operation);
    }

    [HttpPost("/operations/{id}/submit")]
    public async Task<IActionResult> SubmitOperation(string id)
    {
        var operation = await _db.Operations
            .Include(o => o.Events)
            .FirstOrDefaultAsync(o => o.OperationId == id);

        if (operation == null)
            return NotFound(new { error = "Операция не найдена" });

        if (operation.Status != "CREATED")
            return Ok(operation);

        operation.Status = "PROCESSING";

        operation.Events.Add(new OperationEvent
        {
            OperationId = operation.OperationId,
            Type = "PROCESSING",
            FromStatus = "CREATED",
            ToStatus = "PROCESSING",
            Message = "Submit intent saved, processing started",
            OccurredAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Запускаем вызов провайдера в фоне (не ждём ответа)
        _ = Task.Run(() => SendToProviderAsync(operation.OperationId));

        return Accepted(operation);
    }

   
        private async Task SendToProviderAsync(string operationId)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<PaymentDbContext>();
                var dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "payments.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath}");

                using var db = new PaymentDbContext(optionsBuilder.Options);
                var operation = await db.Operations.FindAsync(operationId);

                if (operation == null || operation.Status != "PROCESSING")
                    return;

                var providerUrl = _configuration["PROVIDER_URL"] ?? "http://localhost:8081";

                var payload = new
                {
                    operationId = operation.OperationId,
                    amount = operation.Amount,
                    currency = operation.Currency
                };

                using var httpClient = new HttpClient();

                var request = new HttpRequestMessage(HttpMethod.Post, $"{providerUrl}/payments")
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Add("Idempotency-Key", operationId);
                request.Headers.Add("X-Correlation-ID", operationId);

                _logger.LogInformation("Sending payment {OperationId} to provider", operationId);

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var response = await httpClient.SendAsync(request);

                        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < 3)
                        {
                            _logger.LogWarning("Provider unavailable for {OperationId}, retry {Attempt}/3", operationId, attempt);
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
                                _logger.LogInformation("Received providerPaymentId {ProviderId} for {OperationId}", providerPaymentId, operationId);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Provider returned {StatusCode} for {OperationId}", response.StatusCode, operationId);
                        }

                        break;
                    }
                    catch (HttpRequestException ex) when (attempt < 3)
                    {
                        _logger.LogWarning(ex, "Network error for {OperationId}, retry {Attempt}/3", operationId, attempt);
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100 + Random.Shared.Next(100)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send {OperationId} to provider", operationId);
            }
        }
    

    [HttpPost("/receipts")]
    public async Task<IActionResult> ProcessReceipt([FromBody] ReceiptRequest receipt)
    {
        var operation = await _db.Operations
            .Include(o => o.Events)
            .FirstOrDefaultAsync(o => o.OperationId == receipt.OperationId);

        if (operation == null)
            return NoContent();

        if (operation.ProviderPaymentId != null && operation.ProviderPaymentId != receipt.ProviderPaymentId)
            return Conflict(new { error = "ProviderPaymentId mismatch" });

        if (operation.ProviderPaymentId == null)
            operation.ProviderPaymentId = receipt.ProviderPaymentId;

        if (operation.Status == "COMPLETED" || operation.Status == "REJECTED")
        {
            if (operation.Status == receipt.Result)
                return NoContent();

            operation.Events.Add(new OperationEvent
            {
                OperationId = operation.OperationId,
                Type = "IGNORED_RECEIPT",
                FromStatus = operation.Status,
                ToStatus = operation.Status,
                Message = $"Conflicting receipt ignored: {receipt.Result} - {receipt.Message}",
                OccurredAt = receipt.OccurredAt
            });

            await _db.SaveChangesAsync();
            return NoContent();
        }

        var newStatus = receipt.Result == "COMPLETED" ? "COMPLETED" : "REJECTED";

        operation.Status = newStatus;

        operation.Events.Add(new OperationEvent
        {
            OperationId = operation.OperationId,
            Type = receipt.Result,
            FromStatus = "PROCESSING",
            ToStatus = newStatus,
            Message = receipt.Message,
            OccurredAt = receipt.OccurredAt
        });

        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("/operations/{id}")]
    public async Task<IActionResult> GetOperation(string id)
    {
        var operation = await _db.Operations
            .Include(o => o.Events)
            .FirstOrDefaultAsync(o => o.OperationId == id);

        if (operation == null)
            return NotFound(new { error = "Операция не найдена" });

        return Ok(operation);
    }

    [HttpGet("/operations/{id}/events")]
    public async Task<IActionResult> GetEvents(string id)
    {
        var operation = await _db.Operations.AnyAsync(o => o.OperationId == id);

        if (!operation)
            return NotFound(new { error = "Операция не найдена" });

        var events = await _db.Events
            .Where(e => e.OperationId == id)
            .OrderBy(e => e.EventId)
            .ToListAsync();

        return Ok(events);
    }
}