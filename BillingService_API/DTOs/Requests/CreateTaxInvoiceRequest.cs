using System.ComponentModel.DataAnnotations;

namespace BillingService_API.DTOs.Requests;

public class CreateTaxInvoiceRequest
{
    [Required]
    public DateTime IssueDate { get; set; }

    public string? Remark { get; set; }
}
