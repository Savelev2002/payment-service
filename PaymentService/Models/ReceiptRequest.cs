namespace PaymentService.Models;

public class ReceiptRequest
{
    public Guid ProviderPaymentId { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}