using System.ComponentModel.DataAnnotations;

namespace BillingService_API.DTOs.Requests;

public class CreateReceiptRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    public Guid? CustomerId { get; set; }

    [Required]
    [MaxLength(50)]
    public string SourceType { get; set; } = string.Empty;

    [Required]
    public Guid SourceId { get; set; }

    [MaxLength(100)]
    public string? SourceNo { get; set; }

    [Required]
    public DateTime IssueDate { get; set; }

    public string? Remark { get; set; }

    [MaxLength(50)]
    public string? PaymentMethodSnapshot { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateReceiptItemRequest> Items { get; set; } = new();
}

public class CreateReceiptItemRequest
{
    public int? LineNo { get; set; }

    [MaxLength(50)]
    public string? ItemCode { get; set; }

    [MaxLength(50)]
    public string? UnitName { get; set; }

    [Required]
    [MaxLength(255)]
    public string ItemName { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; } // ราคาต่อหน่วยรวม VAT

    [Range(0, 100)]
    public decimal VatRate { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? DiscountAmount { get; set; }

    [MaxLength(255)]
    public string? ItemRemark { get; set; }
}
