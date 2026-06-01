namespace BillingService_API.Services.Interfaces;

public interface IDocumentNumberService
{
    Task<(int RunningNumber, string YearMonth, string DocumentNo)> GenerateDocumentNumberAsync(
        Guid companyId,
        string companyCode,
        string documentType,
        DateTime issueDate,
        CancellationToken cancellationToken = default);
}
