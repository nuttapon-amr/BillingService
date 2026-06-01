using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace BillingService_API.Services;

public class DocumentNumberService : IDocumentNumberService
{
    private readonly BillingServiceContext _context;
    private readonly ILogger<DocumentNumberService> _logger;

    public DocumentNumberService(BillingServiceContext context, ILogger<DocumentNumberService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<(int RunningNumber, string YearMonth, string DocumentNo)> GenerateDocumentNumberAsync(
        Guid companyId,
        string companyCode,
        string documentType,
        DateTime issueDate,
        CancellationToken cancellationToken = default)
    {
        var yearMonth = issueDate.ToString("yyyyMM");

        var runningNumber = await LockAndGetRunningNumberAsync(companyId, documentType, yearMonth, cancellationToken);
        var documentNo = $"{companyCode}-{documentType}-{yearMonth}-{runningNumber:D6}";

        _logger.LogInformation(
            "Generated document no {DocumentNo} for CompanyId {CompanyId}, Type {DocumentType}",
            documentNo,
            companyId,
            documentType);

        return (runningNumber, yearMonth, documentNo);
    }

    private async Task<int> LockAndGetRunningNumberAsync(
        Guid companyId,
        string documentType,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        var running = await _context.DocumentRunningNumbers
            .FromSqlInterpolated($@"
                SELECT *
                FROM DocumentRunningNumbers
                WHERE CompanyId = {companyId}
                  AND DocumentType = {documentType}
                  AND YearMonth = {yearMonth}
                FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (running is null)
        {
            try
            {
                _context.DocumentRunningNumbers.Add(new DocumentRunningNumber
                {
                    RunningId = Guid.NewGuid(),
                    CompanyId = companyId,
                    DocumentType = documentType,
                    YearMonth = yearMonth,
                    CurrentNumber = 0
                });
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                _logger.LogDebug(
                    "Running row already created by concurrent transaction: CompanyId={CompanyId}, Type={DocumentType}, YearMonth={YearMonth}",
                    companyId,
                    documentType,
                    yearMonth);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to initialize running number row for CompanyId={CompanyId}, Type={DocumentType}, YearMonth={YearMonth}",
                    companyId,
                    documentType,
                    yearMonth);
                throw new ValidationException("Cannot initialize running document number. Please verify company/document type master data.");
            }

            running = await _context.DocumentRunningNumbers
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM DocumentRunningNumbers
                    WHERE CompanyId = {companyId}
                      AND DocumentType = {documentType}
                      AND YearMonth = {yearMonth}
                    FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (running is null)
            {
                throw new ValidationException("Document running number configuration was not found.");
            }
        }

        running.CurrentNumber += 1;
        await _context.SaveChangesAsync(cancellationToken);

        return running.CurrentNumber;
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
    {
        return ex.InnerException is MySqlException { Number: 1062 };
    }
}
