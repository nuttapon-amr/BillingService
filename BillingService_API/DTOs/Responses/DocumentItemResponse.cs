namespace BillingService_API.DTOs.Responses;

public class DocumentItemResponse
{
    public Guid DocumentItemId { get; set; }
    public int? LineNo { get; set; }
    public string? ItemCode { get; set; }
    public string? UnitName { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public string? ItemRemark { get; set; }
}
