using BillingService_API.DTOs.Requests;
using BillingService_API.Services;
using BillingService_API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BillingService_API.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentService documentService, ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    [HttpGet("receipts/{documentId:guid}")]
    public async Task<IActionResult> GetReceiptDetail(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _documentService.GetReceiptDetailAsync(documentId, cancellationToken);
            if (response is null)
            {
                return NotFound();
            }

            return Ok(response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> CreateReceipt([FromBody] CreateReceiptRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var response = await _documentService.CreateReceiptAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("receipts/{documentId:guid}/pdf")]
    public async Task<IActionResult> GetReceiptPdf(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _documentService.GetReceiptPdfContentAsync(documentId, cancellationToken);
            return File(response.Content, "application/pdf", response.FileName);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("tax-invoices/{documentId:guid}/pdf")]
    public async Task<IActionResult> GetTaxInvoicePdf(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _documentService.GetTaxInvoicePdfContentAsync(documentId, cancellationToken);
            return File(response.Content, "application/pdf", response.FileName);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
    
    [HttpPost("{documentId:guid}/tax-invoice")]
    public async Task<IActionResult> CreateTaxInvoice(Guid documentId, [FromBody] CreateTaxInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var response = await _documentService.CreateTaxInvoiceFromReceiptAsync(documentId, request, cancellationToken);
            return Ok(response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPost("{documentId:guid}/cancel")]
    public async Task<IActionResult> CancelDocument(Guid documentId, [FromBody] CancelDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var response = await _documentService.CancelDocumentAsync(documentId, request, cancellationToken);
            return Ok(response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }
}
