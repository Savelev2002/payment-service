namespace PaymentService.Models;

public class CreateOperationRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Currency { get; set; } = "RUB";
    public string? Description { get; set; }
}