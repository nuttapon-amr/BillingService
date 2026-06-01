using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;

namespace BillingService_API.Services.Interfaces;

public interface IDocumentService
{
    Task<DocumentResponse> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken cancellationToken = default);
    Task<DocumentResponse?> GetReceiptDetailAsync(Guid receiptId, CancellationToken cancellationToken = default);
    Task<(byte[] Content, string FileName)> GetReceiptPdfContentAsync(Guid receiptId, CancellationToken cancellationToken = default);
    Task<(byte[] Content, string FileName)> GetTaxInvoicePdfContentAsync(Guid taxInvoiceId, CancellationToken cancellationToken = default);

    Task<DocumentResponse> CreateTaxInvoiceFromReceiptAsync(Guid receiptId, CreateTaxInvoiceRequest request, CancellationToken cancellationToken = default);

    Task<DocumentResponse> CancelDocumentAsync(Guid documentId, CancelDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentResponse?> GetDocumentDetailAsync(Guid documentId, CancellationToken cancellationToken = default);
}
