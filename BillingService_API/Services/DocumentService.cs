using System.Text.Json;
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
            PaymentMethodSnapshot = request.PaymentMethodSnapshot?.Trim(),
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
        var autoPdfFile = await CreateAutoDocumentPdfFileAsync(document, company.CompanyName, template, cancellationToken);
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
        string companyName,
        DocumentTemplate template,
        CancellationToken cancellationToken)
    {
        var receiptsDir = ResolveReceiptPdfDirectory();
        Directory.CreateDirectory(receiptsDir);

        var fileName = $"{document.DocumentNo}.pdf";
        var filePath = Path.Combine(receiptsDir, fileName);
        var pdfBytes = DocumentPdfBuilder.Build(document, companyName, document.DocumentType, template);
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
            PaymentMethodSnapshot = receipt.PaymentMethodSnapshot,
            ReferenceDocumentId = receipt.DocumentId,
            ReferenceDocumentNoSnapshot = receipt.DocumentNo,
            ReferenceIssueDateSnapshot = receipt.IssueDate,
            ReferenceDocumentTypeSnapshot = receipt.DocumentType,
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
                LineNo = sourceItem.LineNo,
                ItemCode = sourceItem.ItemCode,
                UnitName = sourceItem.UnitName,
                ItemName = sourceItem.ItemName,
                Quantity = sourceItem.Quantity,
                UnitPrice = sourceItem.UnitPrice,
                Amount = sourceItem.Amount,
                VatRate = sourceItem.VatRate,
                VatAmount = sourceItem.VatAmount,
                DiscountAmount = sourceItem.DiscountAmount,
                NetAmount = sourceItem.NetAmount,
                ItemRemark = sourceItem.ItemRemark
            });
        }

        receipt.TaxInvoiceIssued = true;

        _context.Documents.Add(taxInvoice);
        _context.DocumentAuditLogs.Add(CreateAuditLog(taxInvoice.DocumentId, "Created", null, new { taxInvoice.DocumentNo, taxInvoice.Status }));
        var autoPdfFile = await CreateAutoDocumentPdfFileAsync(taxInvoice, company.CompanyName, template, cancellationToken);
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

        var autoPdfFile = await CreateAutoDocumentPdfFileAsync(creditNote, company.CompanyName, creditNoteTemplate, cancellationToken);
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
                LineNo = item.LineNo ?? entities.Count + 1,
                ItemCode = item.ItemCode,
                UnitName = item.UnitName,
            ItemName = item.ItemName.Trim(),
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
                Amount = amount,
                VatRate = item.VatRate,
                VatAmount = itemVatAmount,
                DiscountAmount = item.DiscountAmount ?? 0m,
                NetAmount = decimal.Round(amount - (item.DiscountAmount ?? 0m), 2, MidpointRounding.AwayFromZero),
                ItemRemark = item.ItemRemark
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
        document.CompanyEmailSnapshot = company.Email;
        document.CompanyPhoneSnapshot = company.Phone;
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
            document.CustomerEmailSnapshot = customer.Email;
            document.CustomerPhoneSnapshot = customer.Phone;
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
        target.CustomerEmailSnapshot = source.CustomerEmailSnapshot;
        target.CustomerPhoneSnapshot = source.CustomerPhoneSnapshot;
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
        target.CustomerEmailSnapshot ??= customer.Email;
        target.CustomerPhoneSnapshot ??= customer.Phone;
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
            CompanyEmailSnapshot = source.CompanyEmailSnapshot,
            CompanyPhoneSnapshot = source.CompanyPhoneSnapshot,
            CustomerNameSnapshot = source.CustomerNameSnapshot,
            CustomerTypeSnapshot = source.CustomerTypeSnapshot,
            CustomerTaxIdSnapshot = source.CustomerTaxIdSnapshot,
            CustomerBranchNoSnapshot = source.CustomerBranchNoSnapshot,
            CustomerAddressSnapshot = source.CustomerAddressSnapshot,
            CustomerPostalCodeSnapshot = source.CustomerPostalCodeSnapshot,
            CustomerEmailSnapshot = source.CustomerEmailSnapshot,
            CustomerPhoneSnapshot = source.CustomerPhoneSnapshot,
            PaymentMethodSnapshot = source.PaymentMethodSnapshot,
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceNo = source.SourceNo,
            ReferenceDocumentId = source.DocumentId,
            ReferenceDocumentNoSnapshot = source.DocumentNo,
            ReferenceIssueDateSnapshot = source.IssueDate,
            ReferenceDocumentTypeSnapshot = source.DocumentType,
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
                LineNo = sourceItem.LineNo,
                ItemCode = sourceItem.ItemCode,
                UnitName = sourceItem.UnitName,
                ItemName = sourceItem.ItemName,
                Quantity = decimal.Negate(sourceItem.Quantity),
                UnitPrice = sourceItem.UnitPrice,
                Amount = decimal.Negate(sourceItem.Amount),
                VatRate = sourceItem.VatRate,
                VatAmount = decimal.Negate(sourceItem.VatAmount),
                DiscountAmount = sourceItem.DiscountAmount,
                NetAmount = sourceItem.NetAmount.HasValue ? decimal.Negate(sourceItem.NetAmount.Value) : null,
                ItemRemark = sourceItem.ItemRemark
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
            CompanyEmailSnapshot = document.CompanyEmailSnapshot,
            CompanyPhoneSnapshot = document.CompanyPhoneSnapshot,
            CustomerNameSnapshot = document.CustomerNameSnapshot,
            CustomerTypeSnapshot = document.CustomerTypeSnapshot,
            CustomerTaxIdSnapshot = document.CustomerTaxIdSnapshot,
            CustomerBranchNoSnapshot = document.CustomerBranchNoSnapshot,
            CustomerAddressSnapshot = document.CustomerAddressSnapshot,
            CustomerPostalCodeSnapshot = document.CustomerPostalCodeSnapshot,
            CustomerEmailSnapshot = document.CustomerEmailSnapshot,
            CustomerPhoneSnapshot = document.CustomerPhoneSnapshot,
            PaymentMethodSnapshot = document.PaymentMethodSnapshot,
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
            ReferenceDocumentNoSnapshot = document.ReferenceDocumentNoSnapshot,
            ReferenceIssueDateSnapshot = document.ReferenceIssueDateSnapshot,
            ReferenceDocumentTypeSnapshot = document.ReferenceDocumentTypeSnapshot,
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
                LineNo = item.LineNo,
                ItemCode = item.ItemCode,
                UnitName = item.UnitName,
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Amount,
                VatRate = item.VatRate,
                VatAmount = item.VatAmount,
                DiscountAmount = item.DiscountAmount,
                NetAmount = item.NetAmount,
                ItemRemark = item.ItemRemark
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
