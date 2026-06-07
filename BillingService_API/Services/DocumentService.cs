using System.Text.Json;
using System.Text;
using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;
using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using MySqlConnector;

namespace BillingService_API.Services;

public class DocumentService : IDocumentService
{
    private const string IssuedStatus = "Issued";
    private const string CancelledStatus = "Cancelled";
    private const string ReceiptDocumentType = "RC";
    private const string TaxInvoiceDocumentType = "TI";
    private const string CreditNoteDocumentType = "CN";

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

        if (!request.CustomerId.HasValue)
        {
            throw new ValidationException("CustomerId is required for receipt.");
        }

        var customer = await GetCustomerAsync(request.CustomerId, cancellationToken);
        ValidateReceiptCustomer(customer);
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

    public async Task<DocumentResponse?> GetReceiptDetailAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
        {
            throw new ValidationException("documentId is required.");
        }

        var receipt = await _context.Documents
            .AsNoTracking()
            .Include(d => d.DocumentItems)
            .SingleOrDefaultAsync(d => d.DocumentId == documentId && d.DocumentType == ReceiptDocumentType, cancellationToken);

        return receipt is null ? null : MapDocument(receipt);
    }

    public async Task<(byte[] Content, string FileName)> GetReceiptPdfContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await GetDocumentPdfContentByTypeAsync(documentId, ReceiptDocumentType, "DocumentId", cancellationToken);
    }

    public async Task<(byte[] Content, string FileName)> GetTaxInvoicePdfContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await GetDocumentPdfContentByTypeAsync(documentId, TaxInvoiceDocumentType, "DocumentId", cancellationToken);
    }

    public async Task<(byte[] Content, string FileName)> GetDocumentPdfContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await GetDocumentPdfContentByIdAsync(documentId, "DocumentId", cancellationToken);
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
        var pdfBytes = BuildTemplatePdfBytes(document, companyName, title, template);
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

    private async Task<(byte[] Content, string FileName)> GetDocumentPdfContentByIdAsync(
        Guid documentId,
        string idName,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new ValidationException($"{idName} is required.");
        }

        var documentExists = await _context.Documents
            .AsNoTracking()
            .AnyAsync(d => d.DocumentId == documentId, cancellationToken);

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

    private async Task<DocumentTemplate> GetDefaultTemplateOrFallbackAsync(Guid companyId, string documentType, CancellationToken cancellationToken)
    {
        var template = await _context.DocumentTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(t =>
                t.CompanyId == companyId &&
                t.DocumentType == documentType &&
                t.IsDefault &&
                t.IsActive != false, cancellationToken);

        if (template is not null)
        {
            return template;
        }

        return new DocumentTemplate
        {
            TemplateId = Guid.NewGuid(),
            CompanyId = companyId,
            DocumentType = documentType,
            TemplateName = $"{documentType} Auto Template",
            LogoPath = null,
            HeaderText = null,
            FooterText = null,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
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

    private static byte[] BuildTemplatePdfBytes(Document document, string companyName, string title, DocumentTemplate template)
    {
        return DocumentPdfBuilder.Build(document, companyName, title, template);
    }

    private static string EscapePdfText(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static string ResolveLogoDisplayText(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return "-";
        }

        var trimmed = logoPath.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return $"LogoUrl: {trimmed}";
        }

        if (Path.IsPathRooted(trimmed))
        {
            return $"LogoPath: {trimmed}";
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, trimmed);
        if (File.Exists(localPath))
        {
            return $"LogoPath: {localPath}";
        }

        return $"LogoPath: {trimmed}";
    }

    private static void AddPdfGraphicsHeader(List<string> lines)
    {
        lines.Add("0.15 0.2 0.28 rg");
        lines.Add("0.15 0.2 0.28 RG");
        lines.Add("0.8 w");
        lines.Add("0.96 0.96 0.96 rg");
    }

    private static void AddPdfRect(List<string> lines, double x, double y, double width, double height, bool stroke = true, bool fill = false)
    {
        lines.Add($"{FormatPdfNumber(x)} {FormatPdfNumber(y)} {FormatPdfNumber(width)} {FormatPdfNumber(height)} re {(fill && stroke ? "B" : fill ? "f" : "S")}");
    }

    private static void AddPdfFilledRect(List<string> lines, double x, double y, double width, double height, string rgb)
    {
        lines.Add($"{rgb} rg");
        lines.Add($"{FormatPdfNumber(x)} {FormatPdfNumber(y)} {FormatPdfNumber(width)} {FormatPdfNumber(height)} re f");
        lines.Add("0 0 0 rg");
    }

    private static void AddPdfLine(List<string> lines, double x1, double y1, double x2, double y2, double width)
    {
        lines.Add($"{FormatPdfNumber(width)} w");
        lines.Add($"{FormatPdfNumber(x1)} {FormatPdfNumber(y1)} m {FormatPdfNumber(x2)} {FormatPdfNumber(y2)} l S");
        lines.Add("0.8 w");
    }

    private static void AddPdfText(
        List<string> lines,
        double x,
        double y,
        int fontSize,
        string text,
        bool bold = false,
        bool alignRight = false,
        int maxChars = 60)
    {
        var font = bold ? "/F2" : "/F1";
        var safeText = EscapePdfText(TruncateText(text, maxChars));
        var effectiveX = alignRight ? x - (safeText.Length * fontSize * 0.42) : x;
        lines.Add($"BT {font} {fontSize} Tf {FormatPdfNumber(effectiveX)} {FormatPdfNumber(y)} Td ({safeText}) Tj ET");
    }

    private static void AddSectionHeader(List<string> lines, double x, double y, double width, string title)
    {
        AddPdfFilledRect(lines, x, y, width, 18, "0.88 0.90 0.93");
        AddPdfRect(lines, x, y, width, 18);
        AddPdfText(lines, x + 6, y + 5, 8, title, bold: true, maxChars: 42);
    }

    private static void AddKeyValueBlock(List<string> lines, double x, double yTop, double width, IEnumerable<(string Label, string Value)> rows)
    {
        var y = yTop;
        foreach (var (label, value) in rows)
        {
            AddPdfText(lines, x + 6, y, 8, $"{label}:", bold: true, maxChars: 16);
            AddPdfText(lines, x + 70, y, 8, value, maxChars: 30);
            y -= 14;
        }

        AddPdfRect(lines, x, y + 2, width, yTop - y + 2);
    }

    private static void AddItemsTable(List<string> lines, Document document)
    {
        var left = 42.0;
        var top = 480.0;
        var tableWidth = 510.0;
        var rowHeight = 16.0;

        var columns = new[]
        {
            (Title: "No", X: left, Width: 22.0),
            (Title: "Item", X: left + 22, Width: 200.0),
            (Title: "Qty", X: left + 222, Width: 40.0),
            (Title: "Unit", X: left + 262, Width: 56.0),
            (Title: "Amount", X: left + 318, Width: 70.0),
            (Title: "VAT", X: left + 388, Width: 50.0),
            (Title: "Line Total", X: left + 438, Width: 114.0)
        };

        AddPdfFilledRect(lines, left, top, tableWidth, rowHeight, "0.15 0.2 0.28");
        AddPdfRect(lines, left, top, tableWidth, rowHeight);
        foreach (var column in columns)
        {
            AddPdfText(lines, column.X + 2, top + 5, 7, column.Title, bold: true, maxChars: 18);
        }

        var rowY = top - rowHeight;
        var items = document.DocumentItems.Take(6).ToList();
        if (items.Count == 0)
        {
            AddPdfRect(lines, left, rowY, tableWidth, rowHeight);
            AddPdfText(lines, left + 8, rowY + 5, 8, "No items found.", maxChars: 48);
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            AddPdfRect(lines, left, rowY, tableWidth, rowHeight);
            AddPdfText(lines, columns[0].X + 2, rowY + 5, 7, (index + 1).ToString(), maxChars: 4);
            AddPdfText(lines, columns[1].X + 2, rowY + 5, 7, FormatOrDash(item.ItemName), maxChars: 36);
            AddPdfText(lines, columns[2].X + 2, rowY + 5, 7, item.Quantity.ToString("0.##"), maxChars: 8);
            AddPdfText(lines, columns[3].X + 2, rowY + 5, 7, item.UnitPrice.ToString("0.00"), maxChars: 10);
            AddPdfText(lines, columns[4].X + 2, rowY + 5, 7, item.Amount.ToString("0.00"), maxChars: 12);
            AddPdfText(lines, columns[5].X + 2, rowY + 5, 7, item.VatAmount.ToString("0.00"), maxChars: 10);
            AddPdfText(lines, columns[6].X + 2, rowY + 5, 7, (item.Amount + item.VatAmount).ToString("0.00"), maxChars: 12);
            rowY -= rowHeight;
        }

        if (document.DocumentItems.Count > items.Count)
        {
            AddPdfText(lines, left, rowY - 2, 7, $"Showing first {items.Count} of {document.DocumentItems.Count} items.", maxChars: 44);
        }
    }

    private static void AddSummaryBox(List<string> lines, Document document)
    {
        var x = 360.0;
        var y = 292.0;
        var width = 192.0;
        var height = 82.0;

        AddPdfRect(lines, x, y, width, height);
        AddPdfFilledRect(lines, x, y + 66, width, 16, "0.94 0.95 0.97");
        AddPdfText(lines, x + 8, y + 71, 8, "Financial Summary", bold: true, maxChars: 24);

        var rows = new[]
        {
            ("Sub Total", document.SubTotal),
            ("VAT", document.VatAmount),
            ("Grand Total", document.GrandTotal)
        };

        var rowY = y + 48;
        foreach (var row in rows)
        {
            AddPdfText(lines, x + 8, rowY, 8, row.Item1, bold: true, maxChars: 12);
            AddPdfText(lines, x + 120, rowY, 8, row.Item2.ToString("0.00"), alignRight: true, maxChars: 14);
            rowY -= 15;
        }
    }

    private static void AddNotesBlock(List<string> lines, Document document, string footer)
    {
        var x = 42.0;
        var y = 148.0;
        var width = 244.0;
        var height = 62.0;

        AddPdfRect(lines, x, y, width, height);
        AddPdfText(lines, x + 8, y + 46, 8, $"Reference: {FormatOrDash(document.SourceNo)}", maxChars: 36);
        AddPdfText(lines, x + 8, y + 32, 8, $"Remark: {FormatOrDash(document.Remark)}", maxChars: 36);
        AddPdfText(lines, x + 8, y + 18, 8, $"Footer: {footer}", maxChars: 36);
        AddPdfText(lines, x + 8, y + 6, 7, $"Type: {document.DocumentType} | Status: {document.Status}", maxChars: 42);
    }

    private static void AddSignatureBlock(List<string> lines)
    {
        var x = 308.0;
        var y = 148.0;
        var width = 244.0;
        var height = 62.0;

        AddPdfRect(lines, x, y, width, height);
        AddPdfLine(lines, x + 18, y + 22, x + 104, y + 22, 0.5);
        AddPdfLine(lines, x + 132, y + 22, x + 218, y + 22, 0.5);
        AddPdfText(lines, x + 26, y + 10, 7, "Authorized Signature", maxChars: 22);
        AddPdfText(lines, x + 140, y + 10, 7, "Receiver Acknowledgement", maxChars: 22);
    }

    private static string FormatOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static string TruncateText(string text, int maxChars)
    {
        if (maxChars <= 0 || text.Length <= maxChars)
        {
            return text;
        }

        if (maxChars <= 1)
        {
            return text[..1];
        }

        return text[..(maxChars - 1)] + "…";
    }

    private static string FormatPdfNumber(double value)
    {
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<DocumentResponse> CreateTaxInvoiceFromReceiptAsync(Guid documentId, CreateTaxInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
        {
            throw new ValidationException("DocumentId is required.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await _context.Documents
            .Include(d => d.DocumentItems)
            .SingleOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);

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

        var existingTaxInvoice = await _context.Documents
            .SingleOrDefaultAsync(d =>
                d.ReferenceDocumentId == documentId &&
                d.DocumentType == TaxInvoiceDocumentType,
                cancellationToken);

        if (existingTaxInvoice is not null && existingTaxInvoice.Status != CancelledStatus)
        {
            throw new ConflictException("This receipt already has a tax invoice.");
        }

        if (existingTaxInvoice is not null && existingTaxInvoice.Status == CancelledStatus && existingTaxInvoice.ReferenceDocumentId.HasValue)
        {
            _logger.LogInformation(
                "Clearing reference from cancelled tax invoice DocumentId={TaxInvoiceId} to allow reissue for ReceiptId={ReceiptId}",
                existingTaxInvoice.DocumentId,
                documentId);

            existingTaxInvoice.ReferenceDocumentId = null;
        }

        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == receipt.CompanyId, cancellationToken);

        if (company is null)
        {
            throw new NotFoundException("Company not found.");
        }

        var customer = receipt.CustomerId.HasValue
            ? await _context.Customers
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.CustomerId == receipt.CustomerId.Value, cancellationToken)
            : null;

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
        ApplyReceiverSnapshotFallback(taxInvoice, customer);

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

        try
        {
            _logger.LogInformation(
                "Saving tax invoice DocumentNo={DocumentNo} for ReceiptId={ReceiptId}",
                taxInvoice.DocumentNo,
                documentId);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                ex,
                "Failed to save tax invoice DocumentNo={DocumentNo} for ReceiptId={ReceiptId}",
                taxInvoice.DocumentNo,
                documentId);

            if (IsDuplicateKey(ex))
            {
                throw new ConflictException(
                    "Unable to create tax invoice because a duplicate record already exists. If this receipt had a cancelled tax invoice created before the cancellation fix, the old reference must be cleared first.");
            }

            throw new ValidationException("Unable to create tax invoice due to a database constraint failure.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("Tax invoice created: DocumentId={DocumentId}, ReferenceReceiptId={ReceiptId}", taxInvoice.DocumentId, documentId);

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

        if (document.DocumentType != TaxInvoiceDocumentType)
        {
            throw new ConflictException("Cancel is allowed only for tax invoices (TI).");
        }

        if (document.Status == CancelledStatus)
        {
            throw new ConflictException("Document is already cancelled.");
        }

        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == document.CompanyId, cancellationToken);

        if (company is null)
        {
            throw new NotFoundException("Company not found.");
        }

        var originalReceiptId = document.ReferenceDocumentId;
        var oldStatus = document.Status;
        var cancelReason = request.CancelReason.Trim();
        var cancelledBy = string.IsNullOrWhiteSpace(request.CancelledBy) ? "system" : request.CancelledBy.Trim();

        document.Status = CancelledStatus;
        // Release the receipt reference so the same receipt can be re-issued with a new tax invoice
        // after cancellation. The cancelled invoice remains traceable through the credit note and audit logs.
        document.ReferenceDocumentId = null;

        Document? receipt = null;
        if (originalReceiptId.HasValue)
        {
            receipt = await _context.Documents
                .SingleOrDefaultAsync(d => d.DocumentId == originalReceiptId.Value && d.DocumentType == ReceiptDocumentType, cancellationToken);

            if (receipt is not null)
            {
                receipt.TaxInvoiceIssued = false;
                _context.DocumentAuditLogs.Add(CreateAuditLog(
                    receipt.DocumentId,
                    "TaxInvoiceIssued",
                    new { TaxInvoiceIssued = true },
                    new { TaxInvoiceIssued = false },
                    cancelledBy));
            }
        }

        var creditNoteTemplate = await GetDefaultTemplateOrFallbackAsync(document.CompanyId, CreditNoteDocumentType, cancellationToken);
        var creditNoteDate = DateTime.UtcNow;
        var (runningNumber, yearMonth, documentNo) = await _documentNumberService.GenerateDocumentNumberAsync(
            document.CompanyId,
            company.CompanyCode,
            CreditNoteDocumentType,
            creditNoteDate,
            cancellationToken);

        var creditNote = BuildCreditNoteFromCancelledTaxInvoice(
            document,
            documentNo,
            yearMonth,
            runningNumber,
            creditNoteDate,
            cancelReason);

        _context.Documents.Add(creditNote);

        _context.DocumentCancelLogs.Add(new DocumentCancelLog
        {
            CancelLogId = Guid.NewGuid(),
            DocumentId = document.DocumentId,
            CancelReason = cancelReason,
            CancelledBy = cancelledBy,
            CancelledAt = creditNoteDate
        });

        _context.DocumentAuditLogs.Add(CreateAuditLog(
            document.DocumentId,
            "Cancelled",
            new { Status = oldStatus, ReferenceDocumentId = originalReceiptId },
            new { Status = CancelledStatus, ReferenceDocumentId = (Guid?)null },
            cancelledBy));

        _context.DocumentAuditLogs.Add(CreateAuditLog(
            creditNote.DocumentId,
            "Created",
            null,
            new { creditNote.DocumentNo, creditNote.Status, creditNote.ReferenceDocumentId },
            cancelledBy));

        var autoPdfFile = await CreateAutoDocumentPdfFileAsync(creditNote, "Credit Note", company.CompanyName, creditNoteTemplate, cancellationToken);
        _context.DocumentFiles.Add(autoPdfFile);
        _context.DocumentAuditLogs.Add(CreateAuditLog(
            creditNote.DocumentId,
            "PdfGenerated",
            null,
            new { autoPdfFile.DocumentFileId, autoPdfFile.FileName, autoPdfFile.FileUrl, autoPdfFile.FileHash },
            cancelledBy));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Tax invoice cancelled: DocumentId={DocumentId}, DocumentNo={DocumentNo}, CreditNoteId={CreditNoteId}, CreditNoteNo={CreditNoteNo}",
            document.DocumentId,
            document.DocumentNo,
            creditNote.DocumentId,
            creditNote.DocumentNo);

        return MapDocument(creditNote);
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

            var grossAmount = decimal.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var itemVatAmount = item.VatRate <= 0
                ? 0m
                : decimal.Round(grossAmount * item.VatRate / (100m + item.VatRate), 2, MidpointRounding.AwayFromZero);
            var amount = decimal.Round(grossAmount - itemVatAmount, 2, MidpointRounding.AwayFromZero);

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

    private static void ValidateReceiptCustomer(Customer? customer)
    {
        if (customer is null)
        {
            throw new NotFoundException("Customer not found.");
        }

        var missingFields = new List<string>();

        if (string.IsNullOrWhiteSpace(customer.TaxId))
        {
            missingFields.Add("TaxId");
        }

        if (string.IsNullOrWhiteSpace(customer.BranchNo))
        {
            missingFields.Add("BranchNo");
        }

        if (string.IsNullOrWhiteSpace(customer.Address))
        {
            missingFields.Add("Address");
        }

        if (string.IsNullOrWhiteSpace(customer.PostalCode))
        {
            missingFields.Add("PostalCode");
        }

        if (missingFields.Count > 0)
        {
            throw new ValidationException(
                $"Customer data is incomplete for receipt. Missing fields: {string.Join(", ", missingFields)}.");
        }
    }

    private static void ApplyReceiverSnapshot(Document document, Customer? customer)
    {
        if (customer is not null)
        {
            document.CustomerNameSnapshot = customer.CustomerName;
            document.CustomerTypeSnapshot = customer.CustomerType;
            document.CustomerTaxIdSnapshot = customer.TaxId;
            document.CustomerBranchNoSnapshot = customer.BranchNo;
            document.CustomerAddressSnapshot = customer.Address;
            document.CustomerPostalCodeSnapshot = customer.PostalCode;
        }
    }

    private static void CopyReceiverSnapshotFromReceipt(Document target, Document source)
    {
        target.CustomerNameSnapshot = source.CustomerNameSnapshot;
        target.CustomerTypeSnapshot = source.CustomerTypeSnapshot;
        target.CustomerTaxIdSnapshot = source.CustomerTaxIdSnapshot;
        target.CustomerBranchNoSnapshot = source.CustomerBranchNoSnapshot;
        target.CustomerAddressSnapshot = source.CustomerAddressSnapshot;
        target.CustomerPostalCodeSnapshot = source.CustomerPostalCodeSnapshot;
    }

    private static void ApplyReceiverSnapshotFallback(Document target, Customer? customer)
    {
        if (customer is null)
        {
            return;
        }

        target.CustomerNameSnapshot ??= customer.CustomerName;
        target.CustomerTypeSnapshot ??= customer.CustomerType;
        target.CustomerTaxIdSnapshot ??= customer.TaxId;
        target.CustomerBranchNoSnapshot ??= customer.BranchNo;
        target.CustomerAddressSnapshot ??= customer.Address;
        target.CustomerPostalCodeSnapshot ??= customer.PostalCode;
    }

    private static Document BuildCreditNoteFromCancelledTaxInvoice(
        Document source,
        string documentNo,
        string runningYearMonth,
        int runningNumber,
        DateTime issueDate,
        string cancelReason)
    {
        var creditNote = new Document
        {
            DocumentId = Guid.NewGuid(),
            CompanyId = source.CompanyId,
            CustomerId = source.CustomerId,
            CompanyNameSnapshot = source.CompanyNameSnapshot,
            CompanyTaxIdSnapshot = source.CompanyTaxIdSnapshot,
            CompanyBranchNoSnapshot = source.CompanyBranchNoSnapshot,
            CompanyAddressSnapshot = source.CompanyAddressSnapshot,
            CustomerNameSnapshot = source.CustomerNameSnapshot,
            CustomerTypeSnapshot = source.CustomerTypeSnapshot,
            CustomerTaxIdSnapshot = source.CustomerTaxIdSnapshot,
            CustomerBranchNoSnapshot = source.CustomerBranchNoSnapshot,
            CustomerAddressSnapshot = source.CustomerAddressSnapshot,
            CustomerPostalCodeSnapshot = source.CustomerPostalCodeSnapshot,
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceNo = source.SourceNo,
            ReferenceDocumentId = source.DocumentId,
            OriginalDocumentNoSnapshot = source.DocumentNo,
            OriginalIssueDateSnapshot = source.IssueDate,
            OriginalDocumentTypeSnapshot = source.DocumentType,
            OriginalSubTotalSnapshot = source.SubTotal,
            OriginalVatAmountSnapshot = source.VatAmount,
            OriginalGrandTotalSnapshot = source.GrandTotal,
            CreditNoteReasonSnapshot = cancelReason,
            DocumentType = CreditNoteDocumentType,
            DocumentNo = documentNo,
            RunningYearMonth = runningYearMonth,
            RunningNumber = runningNumber,
            IssueDate = issueDate,
            SubTotal = decimal.Negate(source.SubTotal),
            VatAmount = decimal.Negate(source.VatAmount),
            GrandTotal = decimal.Negate(source.GrandTotal),
            Status = IssuedStatus,
            Remark = cancelReason,
            TaxInvoiceIssued = false
        };

        foreach (var sourceItem in source.DocumentItems)
        {
            creditNote.DocumentItems.Add(new DocumentItem
            {
                DocumentItemId = Guid.NewGuid(),
                ItemCode = sourceItem.ItemCode,
                ItemName = sourceItem.ItemName,
                Quantity = decimal.Negate(sourceItem.Quantity),
                UnitPrice = sourceItem.UnitPrice,
                Amount = decimal.Negate(sourceItem.Amount),
                VatRate = sourceItem.VatRate,
                VatAmount = decimal.Negate(sourceItem.VatAmount)
            });
        }

        return creditNote;
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
            CustomerTypeSnapshot = document.CustomerTypeSnapshot,
            CustomerTaxIdSnapshot = document.CustomerTaxIdSnapshot,
            CustomerBranchNoSnapshot = document.CustomerBranchNoSnapshot,
            CustomerAddressSnapshot = document.CustomerAddressSnapshot,
            CustomerPostalCodeSnapshot = document.CustomerPostalCodeSnapshot,
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
            OriginalDocumentNoSnapshot = document.OriginalDocumentNoSnapshot,
            OriginalIssueDateSnapshot = document.OriginalIssueDateSnapshot,
            OriginalDocumentTypeSnapshot = document.OriginalDocumentTypeSnapshot,
            OriginalSubTotalSnapshot = document.OriginalSubTotalSnapshot,
            OriginalVatAmountSnapshot = document.OriginalVatAmountSnapshot,
            OriginalGrandTotalSnapshot = document.OriginalGrandTotalSnapshot,
            CreditNoteReasonSnapshot = document.CreditNoteReasonSnapshot,
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

    private static bool IsDuplicateKey(DbUpdateException ex)
    {
        return ex.InnerException is MySqlException { Number: 1062 };
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
