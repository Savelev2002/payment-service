namespace PaymentService.Models;

public class Operation
{
    public string OperationId { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Currency { get; set; } = "RUB";
    public string? Description { get; set; }
    public string Status { get; set; } = "CREATED";
    public Guid? ProviderPaymentId { get; set; }
    public List<OperationEvent> Events { get; set; } = new();
}