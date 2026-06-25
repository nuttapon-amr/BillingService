namespace BillingService_API.DTOs.Responses;

public class DocumentResponse
{
    public Guid DocumentId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CompanyNameSnapshot { get; set; }
    public string? CompanyTaxIdSnapshot { get; set; }
    public string? CompanyBranchNoSnapshot { get; set; }
    public string? CompanyAddressSnapshot { get; set; }
    public string? CompanyEmailSnapshot { get; set; }
    public string? CompanyPhoneSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerTypeSnapshot { get; set; }
    public string? CustomerTaxIdSnapshot { get; set; }
    public string? CustomerBranchNoSnapshot { get; set; }
    public string? CustomerAddressSnapshot { get; set; }
    public string? CustomerPostalCodeSnapshot { get; set; }
    public string? CustomerEmailSnapshot { get; set; }
    public string? CustomerPhoneSnapshot { get; set; }
    public string? PaymentMethodSnapshot { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string? SourceNo { get; set; }
    public string RunningYearMonth { get; set; } = string.Empty;
    public int RunningNumber { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Remark { get; set; }
    public Guid? ReferenceDocumentId { get; set; }
    public string? ReferenceDocumentNoSnapshot { get; set; }
    public DateTime? ReferenceIssueDateSnapshot { get; set; }
    public string? ReferenceDocumentTypeSnapshot { get; set; }
    public string? OriginalDocumentNoSnapshot { get; set; }
    public DateTime? OriginalIssueDateSnapshot { get; set; }
    public string? OriginalDocumentTypeSnapshot { get; set; }
    public decimal? OriginalSubTotalSnapshot { get; set; }
    public decimal? OriginalVatAmountSnapshot { get; set; }
    public decimal? OriginalGrandTotalSnapshot { get; set; }
    public string? CreditNoteReasonSnapshot { get; set; }
    public bool TaxInvoiceIssued { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<DocumentItemResponse> Items { get; set; } = new();
}
