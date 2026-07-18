namespace PaymentService.Models;

public class OperationEvent
{
    public int EventId { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}