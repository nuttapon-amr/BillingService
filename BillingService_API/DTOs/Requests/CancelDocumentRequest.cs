using System.ComponentModel.DataAnnotations;

namespace BillingService_API.DTOs.Requests;

public class CancelDocumentRequest
{
    [Required]
    [MinLength(1)]
    public string CancelReason { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? CancelledBy { get; set; }
}
