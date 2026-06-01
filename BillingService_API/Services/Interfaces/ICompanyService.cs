using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;

namespace BillingService_API.Services.Interfaces;

public interface ICompanyService
{
    Task<CompanyResponse?> GetCompanyByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyResponse> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<CompanyResponse> UpdateCompanyAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default);
}
