using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;

namespace BillingService_API.Services.Interfaces;

public interface ICustomerService
{
    Task<CustomerResponse?> GetCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerResponse> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
    Task DeleteCustomerAsync(Guid customerId, Guid? deletedBy, CancellationToken cancellationToken = default);
}
