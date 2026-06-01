using System.Text.Json;
using System.Text;
using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;
using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace BillingService_API.Services;

public class DocumentService : IDocumentService
{
    private const string IssuedStatus = "Issued";
    private const string CancelledStatus = "Cancelled";
    private const string ReceiptDocumentType = "RC";
    private const string TaxInvoiceDocumentType = "TI";

    private readonly BillingServiceContext _context;
    private readonly IDocumentNumberService _documentNumberService;
    private readonly ILogger<DocumentService> _logger;
    private readonly string? _receiptPdfStoragePath;
    private readonly IWebHostEnvironment _environment;

    public DocumentService(
        BillingServiceContext context,
        IDocumentNumberService documentNumberService,
        ILogger<DocumentService> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _context = context;
        _documentNumberService = documentNumberService;
        _logger = logger;
        _receiptPdfStoragePath = configuration["Documents:ReceiptPdfStoragePath"];
        _environment = environment;
    }

    public async Task<DocumentResponse> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCreateReceiptRequest(request);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == request.CompanyId, cancellationToken);

        if (company is null)
        {
            throw new NotFoundException("Company not found.");
        }
        var customer = await GetCustomerAsync(request.CustomerId, cancellationToken);
        var template = await GetDefaultTemplateAsync(request.CompanyId, ReceiptDocumentType, cancellationToken);

        var (subTotal, vatAmount, grandTotal, itemEntities) = BuildItemsAndTotals(request.Items);
        var (runningNumber, yearMonth, documentNo) = await _documentNumberService.GenerateDocumentNumberAsync(
            request.CompanyId,
            company.CompanyCode,
            ReceiptDocumentType,
            request.IssueDate,
            cancellationToken);

        var document = new Document
        {
            DocumentId = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            CustomerId = request.CustomerId,
            SourceType = request.SourceType.Trim(),
            SourceId = request.SourceId,
            SourceNo = request.SourceNo,
            DocumentType = ReceiptDocumentType,
            DocumentNo = documentNo,
            RunningYearMonth = yearMonth,
            RunningNumber = runningNumber,
            IssueDate = request.IssueDate,
            SubTotal = subTotal,
            VatAmount = vatAmount,
            GrandTotal = grandTotal,
            Status = IssuedStatus,
            Remark = request.Remark,
            TaxInvoiceIssued = false
        };

        ApplyCompanySnapshot(document, company);
        ApplyReceiverSnapshot(document, customer);

        foreach (var item in itemEntities)
        {
            item.DocumentId = document.DocumentId;
            document.DocumentItems.Add(item);
        }

        _context.Documents.Add(document);
        _context.DocumentAuditLogs.Add(CreateAuditLog(document.DocumentId, "Created", null, new { document.DocumentNo, document.Status }));
        var autoPdfFile = await CreateAutoDocumentPdfFileAsync(document, "Receipt", company.CompanyName, template, cancellationToken);
        _context.DocumentFiles.Add(autoPdfFile);
        _context.DocumentAuditLogs.Add(CreateAuditLog(
            document.DocumentId,
            "PdfGenerated",
            null,
            new { autoPdfFile.DocumentFileId, autoPdfFile.FileName, autoPdfFile.FileUrl, autoPdfFile.FileHash }));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Receipt created: DocumentId={DocumentId}, DocumentNo={DocumentNo}", document.DocumentId, document.DocumentNo);

        return MapDocument(document);
    }

    public async Task<DocumentResponse?> GetReceiptDetailAsync(Guid receiptId, CancellationToken cancellationToken = default)
    {
        if (receiptId == Guid.Empty)
        {
            throw new ValidationException("ReceiptId is required.");
        }

        var receipt = await _context.Documents
            .AsNoTracking()
            .Include(d => d.DocumentItems)
            .SingleOrDefaultAsync(d => d.DocumentId == receiptId && d.DocumentType == ReceiptDocumentType, cancellationToken);

        return receipt is null ? null : MapDocument(receipt);
    }

    public async Task<(byte[] Content, string FileName)> GetReceiptPdfContentAsync(Guid receiptId, CancellationToken cancellationToken = default)
    {
        return await GetDocumentPdfContentByTypeAsync(receiptId, ReceiptDocumentType, "ReceiptId", cancellationToken);
    }

    public async Task<(byte[] Content, string FileName)> GetTaxInvoicePdfContentAsync(Guid taxInvoiceId, CancellationToken cancellationToken = default)
    {
        return await GetDocumentPdfContentByTypeAsync(taxInvoiceId, TaxInvoiceDocumentType, "TaxInvoiceId", cancellationToken);
    }

    private async Task<DocumentFile> CreateAutoDocumentPdfFileAsync(
        Document document,
        string title,
        string companyName,
        DocumentTemplate template,
        CancellationToken cancellationToken)
    {
        var receiptsDir = ResolveReceiptPdfDirectory();
        Directory.CreateDirectory(receiptsDir);

        var fileName = $"{document.DocumentNo}.pdf";
        var filePath = Path.Combine(receiptsDir, fileName);
        var pdfBytes = BuildSimplePdfBytes(document, companyName, title, template);
        await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);

        return new DocumentFile
        {
            DocumentFileId = Guid.NewGuid(),
            DocumentId = document.DocumentId,
            FileType = "PDF",
            FileName = fileName,
            FileUrl = $"/files/receipts/{fileName}",
            FileHash = null,
            GeneratedBy = "system",
            GeneratedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    private async Task<(byte[] Content, string FileName)> GetDocumentPdfContentByTypeAsync(
        Guid documentId,
        string documentType,
        string idName,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new ValidationException($"{idName} is required.");
        }

        var documentExists = await _context.Documents
            .AsNoTracking()
            .AnyAsync(d => d.DocumentId == documentId && d.DocumentType == documentType, cancellationToken);

        if (!documentExists)
        {
            throw new NotFoundException("Document not found.");
        }

        var file = await _context.DocumentFiles
            .AsNoTracking()
            .Where(f => f.DocumentId == documentId && f.FileType == "PDF")
            .OrderByDescending(f => f.IsActive == true)
            .ThenByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
        {
            throw new NotFoundException("PDF file not found.");
        }

        var filePath = Path.Combine(ResolveReceiptPdfDirectory(), file.FileName);
        if (!System.IO.File.Exists(filePath))
        {
            throw new NotFoundException("PDF file not found in storage.");
        }

        var content = await System.IO.File.ReadAllBytesAsync(filePath, cancellationToken);
        return (content, file.FileName);
    }

    private async Task<DocumentTemplate> GetDefaultTemplateAsync(Guid companyId, string documentType, CancellationToken cancellationToken)
    {
        var template = await _context.DocumentTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(t =>
                t.CompanyId == companyId &&
                t.DocumentType == documentType &&
                t.IsDefault &&
                t.IsActive != false, cancellationToken);

        if (template is null)
        {
            throw new ValidationException($"Default template is not configured for CompanyId={companyId} and DocumentType={documentType}.");
        }

        return template;
    }

    private string ResolveReceiptPdfDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_receiptPdfStoragePath))
        {
            return Path.GetFullPath(_receiptPdfStoragePath);
        }

        var webRootPath = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(_environment.ContentRootPath, "wwwroot")
            : _environment.WebRootPath;
        return Path.Combine(webRootPath, "files", "receipts");
    }

    private static byte[] BuildSimplePdfBytes(Document document, string companyName, string title, DocumentTemplate template)
    {
        var header = string.IsNullOrWhiteSpace(template.HeaderText) ? "-" : template.HeaderText!;
        var footer = string.IsNullOrWhiteSpace(template.FooterText) ? "-" : template.FooterText!;
        var logoUrl = string.IsNullOrWhiteSpace(template.LogoUrl) ? "-" : template.LogoUrl!;
        var templateName = string.IsNullOrWhiteSpace(template.TemplateName) ? "-" : template.TemplateName;

        var lines = new[]
        {
            $"BT /F1 14 Tf 50 780 Td ({EscapePdfText(title)}) Tj ET",
            $"BT /F1 10 Tf 50 765 Td ({EscapePdfText($"Template: {templateName}")}) Tj ET",
            $"BT /F1 10 Tf 50 750 Td ({EscapePdfText($"Document No: {document.DocumentNo}")}) Tj ET",
            $"BT /F1 10 Tf 50 735 Td ({EscapePdfText($"Company: {companyName}")}) Tj ET",
            $"BT /F1 10 Tf 50 720 Td ({EscapePdfText($"Issue Date: {document.IssueDate:yyyy-MM-dd HH:mm:ss}")}) Tj ET",
            $"BT /F1 10 Tf 50 705 Td ({EscapePdfText($"Grand Total: {document.GrandTotal:0.00}")}) Tj ET",
            $"BT /F1 10 Tf 50 690 Td ({EscapePdfText($"Header: {header}")}) Tj ET",
            $"BT /F1 10 Tf 50 675 Td ({EscapePdfText($"Footer: {footer}")}) Tj ET",
            $"BT /F1 10 Tf 50 660 Td ({EscapePdfText($"LogoUrl: {logoUrl}")}) Tj ET"
        };
        var content = string.Join("\n", lines);
        var contentBytes = Encoding.ASCII.GetBytes(content);

        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream\nendobj\n"
        };

        var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true);
        writer.Write("%PDF-1.4\n");
        writer.Flush();

        var offsets = new List<long> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(ms.Position);
            writer.Write(obj);
            writer.Flush();
        }

        var xrefPos = ms.Position;
        writer.Write($"xref\n0 {objects.Count + 1}\n");
        writer.Write("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Count; i++)
        {
            writer.Write($"{offsets[i]:D10} 00000 n \n");
        }

        writer.Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF");
        writer.Flush();
        return ms.ToArray();
    }

    private static string EscapePdfText(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    public async Task<DocumentResponse> CreateTaxInvoiceFromReceiptAsync(Guid receiptId, CreateTaxInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (receiptId == Guid.Empty)
        {
            throw new ValidationException("ReceiptId is required.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await _context.Documents
            .Include(d => d.DocumentItems)
            .SingleOrDefaultAsync(d => d.DocumentId == receiptId, cancellationToken);

        if (receipt is null)
        {
            throw new NotFoundException("Receipt not found.");
        }

        if (receipt.DocumentType != ReceiptDocumentType)
        {
            throw new ConflictException("Referenced document is not a receipt (RC).");
        }

        if (receipt.Status != IssuedStatus)
        {
            throw new ConflictException("Tax invoice can only be created from issued receipt.");
        }

        var existedTaxInvoice = await _context.Documents
            .AsNoTracking()
            .AnyAsync(d => d.ReferenceDocumentId == receiptId && d.DocumentType == TaxInvoiceDocumentType, cancellationToken);

        if (existedTaxInvoice)
        {
            throw new ConflictException("This receipt already has a tax invoice.");
        }

        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == receipt.CompanyId, cancellationToken);

        if (company is null)
        {
            throw new NotFoundException("Company not found.");
        }
        var template = await GetDefaultTemplateAsync(receipt.CompanyId, TaxInvoiceDocumentType, cancellationToken);

        var (runningNumber, yearMonth, documentNo) = await _documentNumberService.GenerateDocumentNumberAsync(
            receipt.CompanyId,
            company.CompanyCode,
            TaxInvoiceDocumentType,
            request.IssueDate,
            cancellationToken);

        var taxInvoice = new Document
        {
            DocumentId = Guid.NewGuid(),
            CompanyId = receipt.CompanyId,
            CustomerId = receipt.CustomerId,
            SourceType = receipt.SourceType,
            SourceId = receipt.SourceId,
            SourceNo = receipt.SourceNo,
            ReferenceDocumentId = receipt.DocumentId,
            DocumentType = TaxInvoiceDocumentType,
            DocumentNo = documentNo,
            RunningYearMonth = yearMonth,
            RunningNumber = runningNumber,
            IssueDate = request.IssueDate,
            SubTotal = receipt.SubTotal,
            VatAmount = receipt.VatAmount,
            GrandTotal = receipt.GrandTotal,
            Status = IssuedStatus,
            Remark = request.Remark,
            TaxInvoiceIssued = false
        };

        ApplyCompanySnapshot(taxInvoice, company);

        CopyReceiverSnapshotFromReceipt(taxInvoice, receipt);

        foreach (var sourceItem in receipt.DocumentItems)
        {
            taxInvoice.DocumentItems.Add(new DocumentItem
            {
                DocumentItemId = Guid.NewGuid(),
                ItemCode = sourceItem.ItemCode,
                ItemName = sourceItem.ItemName,
                Quantity = sourceItem.Quantity,
                UnitPrice = sourceItem.UnitPrice,
                Amount = sourceItem.Amount,
                VatRate = sourceItem.VatRate,
                VatAmount = sourceItem.VatAmount
            });
        }

        receipt.TaxInvoiceIssued = true;

        _context.Documents.Add(taxInvoice);
        _context.DocumentAuditLogs.Add(CreateAuditLog(taxInvoice.DocumentId, "Created", null, new { taxInvoice.DocumentNo, taxInvoice.Status }));
        var autoPdfFile = await CreateAutoDocumentPdfFileAsync(taxInvoice, "Tax Invoice", company.CompanyName, template, cancellationToken);
        _context.DocumentFiles.Add(autoPdfFile);
        _context.DocumentAuditLogs.Add(CreateAuditLog(
            taxInvoice.DocumentId,
            "PdfGenerated",
            null,
            new { autoPdfFile.DocumentFileId, autoPdfFile.FileName, autoPdfFile.FileUrl, autoPdfFile.FileHash }));
        _context.DocumentAuditLogs.Add(CreateAuditLog(receipt.DocumentId, "TaxInvoiceIssued", new { TaxInvoiceIssued = false }, new { TaxInvoiceIssued = true }));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Tax invoice created: DocumentId={DocumentId}, ReferenceReceiptId={ReceiptId}", taxInvoice.DocumentId, receiptId);

        return MapDocument(taxInvoice);
    }

    public async Task<DocumentResponse> CancelDocumentAsync(Guid documentId, CancelDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
        {
            throw new ValidationException("DocumentId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CancelReason))
        {
            throw new ValidationException("CancelReason is required.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var document = await _context.Documents
            .Include(d => d.DocumentItems)
            .SingleOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        if (document.Status == CancelledStatus)
        {
            throw new ConflictException("Document is already cancelled.");
        }

        var oldStatus = document.Status;
        document.Status = CancelledStatus;

        _context.DocumentCancelLogs.Add(new DocumentCancelLog
        {
            CancelLogId = Guid.NewGuid(),
            DocumentId = document.DocumentId,
            CancelReason = request.CancelReason.Trim(),
            CancelledBy = string.IsNullOrWhiteSpace(request.CancelledBy) ? "system" : request.CancelledBy.Trim(),
            CancelledAt = DateTime.UtcNow
        });

        _context.DocumentAuditLogs.Add(CreateAuditLog(
            document.DocumentId,
            "Cancelled",
            new { Status = oldStatus },
            new { Status = CancelledStatus },
            request.CancelledBy));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Document cancelled: DocumentId={DocumentId}, DocumentNo={DocumentNo}", document.DocumentId, document.DocumentNo);

        return MapDocument(document);
    }

    public async Task<DocumentResponse?> GetDocumentDetailAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
        {
            throw new ValidationException("DocumentId is required.");
        }

        var document = await _context.Documents
            .AsNoTracking()
            .Include(d => d.DocumentItems)
            .SingleOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);

        return document is null ? null : MapDocument(document);
    }

    private static void ValidateCreateReceiptRequest(CreateReceiptRequest request)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ValidationException("CompanyId is required.");
        }

        if (request.SourceId == Guid.Empty)
        {
            throw new ValidationException("SourceId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceType))
        {
            throw new ValidationException("SourceType is required.");
        }

        if (request.Items.Count == 0)
        {
            throw new ValidationException("At least one item is required.");
        }
    }

    private async Task<Customer?> GetCustomerAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        if (!customerId.HasValue)
        {
            return null;
        }

        var customer = await _context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CustomerId == customerId.Value, cancellationToken);

        if (customer is null)
        {
            throw new NotFoundException("Customer not found.");
        }

        return customer;
    }

    private static (decimal SubTotal, decimal VatAmount, decimal GrandTotal, List<DocumentItem> ItemEntities) BuildItemsAndTotals(
        IEnumerable<CreateReceiptItemRequest> items)
    {
        decimal subTotal = 0;
        decimal vatAmount = 0;
        var entities = new List<DocumentItem>();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemName))
            {
                throw new ValidationException("ItemName is required for all items.");
            }

            if (item.Quantity <= 0)
            {
                throw new ValidationException("Quantity must be greater than 0.");
            }

            if (item.UnitPrice < 0 || item.VatRate < 0)
            {
                throw new ValidationException("UnitPrice and VatRate must be non-negative.");
            }

            var amount = decimal.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var itemVatAmount = decimal.Round(amount * item.VatRate / 100m, 2, MidpointRounding.AwayFromZero);

            subTotal += amount;
            vatAmount += itemVatAmount;

            entities.Add(new DocumentItem
            {
                DocumentItemId = Guid.NewGuid(),
                ItemCode = item.ItemCode,
                ItemName = item.ItemName.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = amount,
                VatRate = item.VatRate,
                VatAmount = itemVatAmount
            });
        }

        subTotal = decimal.Round(subTotal, 2, MidpointRounding.AwayFromZero);
        vatAmount = decimal.Round(vatAmount, 2, MidpointRounding.AwayFromZero);
        var grandTotal = decimal.Round(subTotal + vatAmount, 2, MidpointRounding.AwayFromZero);

        return (subTotal, vatAmount, grandTotal, entities);
    }

    private static void ApplyCompanySnapshot(Document document, Company company)
    {
        document.CompanyNameSnapshot = company.CompanyName;
        document.CompanyTaxIdSnapshot = company.TaxId;
        document.CompanyBranchNoSnapshot = company.BranchNo;
        document.CompanyAddressSnapshot = company.Address;
    }

    private static void ApplyReceiverSnapshot(Document document, Customer? customer)
    {
        if (customer is not null)
        {
            document.CustomerNameSnapshot = customer.CustomerName;
            document.CustomerTaxIdSnapshot = customer.TaxId;
            document.CustomerBranchNoSnapshot = customer.BranchNo;
            document.CustomerAddressSnapshot = customer.Address;
        }
    }

    private static void CopyReceiverSnapshotFromReceipt(Document target, Document source)
    {
        target.CustomerNameSnapshot = source.CustomerNameSnapshot;
        target.CustomerTaxIdSnapshot = source.CustomerTaxIdSnapshot;
        target.CustomerBranchNoSnapshot = source.CustomerBranchNoSnapshot;
        target.CustomerAddressSnapshot = source.CustomerAddressSnapshot;
    }

    private static DocumentAuditLog CreateAuditLog(Guid documentId, string action, object? oldValue, object? newValue, string? createdBy = null)
    {
        return new DocumentAuditLog
        {
            AuditLogId = Guid.NewGuid(),
            DocumentId = documentId,
            Action = action,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue),
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static DocumentResponse MapDocument(Document document)
    {
        return new DocumentResponse
        {
            DocumentId = document.DocumentId,
            CompanyId = document.CompanyId,
            CustomerId = document.CustomerId,
            CompanyNameSnapshot = document.CompanyNameSnapshot,
            CompanyTaxIdSnapshot = document.CompanyTaxIdSnapshot,
            CompanyBranchNoSnapshot = document.CompanyBranchNoSnapshot,
            CompanyAddressSnapshot = document.CompanyAddressSnapshot,
            CustomerNameSnapshot = document.CustomerNameSnapshot,
            CustomerTaxIdSnapshot = document.CustomerTaxIdSnapshot,
            CustomerBranchNoSnapshot = document.CustomerBranchNoSnapshot,
            CustomerAddressSnapshot = document.CustomerAddressSnapshot,
            SourceType = document.SourceType,
            SourceId = document.SourceId,
            SourceNo = document.SourceNo,
            RunningYearMonth = document.RunningYearMonth,
            RunningNumber = document.RunningNumber,
            DocumentNo = document.DocumentNo,
            DocumentType = document.DocumentType,
            Status = document.Status,
            IssueDate = document.IssueDate,
            DueDate = document.DueDate,
            SubTotal = document.SubTotal,
            VatAmount = document.VatAmount,
            GrandTotal = document.GrandTotal,
            Remark = document.Remark,
            ReferenceDocumentId = document.ReferenceDocumentId,
            TaxInvoiceIssued = document.TaxInvoiceIssued,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Items = document.DocumentItems.Select(item => new DocumentItemResponse
            {
                DocumentItemId = item.DocumentItemId,
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Amount,
                VatRate = item.VatRate,
                VatAmount = item.VatAmount
            }).ToList()
        };
    }

}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
