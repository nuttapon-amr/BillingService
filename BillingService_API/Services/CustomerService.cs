using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;
using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;

namespace BillingService_API.Services;

public class CustomerService : ICustomerService
{
    private readonly BillingServiceContext _context;

    public CustomerService(BillingServiceContext context)
    {
        _context = context;
    }

    public async Task<CustomerResponse?> GetCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            throw new ValidationException("CustomerId is required.");
        }

        var entity = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.CustomerId, request.CustomerName);

        var duplicated = await _context.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerId == request.CustomerId, cancellationToken);

        if (duplicated)
        {
            throw new ConflictException("CustomerId already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new Customer
        {
            CustomerId = request.CustomerId,
            CustomerName = request.CustomerName.Trim(),
            TaxId = NormalizeNullable(request.TaxId),
            BranchNo = NormalizeNullable(request.BranchNo),
            Address = NormalizeNullable(request.Address),
            Email = NormalizeNullable(request.Email),
            Phone = NormalizeNullable(request.Phone),
            IsDeleted = false,
            DeletedAt = null,
            DeletedBy = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Customers.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task<CustomerResponse> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(customerId, request.CustomerName);

        var entity = await _context.Customers
            .SingleOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException("Customer not found.");
        }

        entity.CustomerName = request.CustomerName.Trim();
        entity.TaxId = NormalizeNullable(request.TaxId);
        entity.BranchNo = NormalizeNullable(request.BranchNo);
        entity.Address = NormalizeNullable(request.Address);
        entity.Email = NormalizeNullable(request.Email);
        entity.Phone = NormalizeNullable(request.Phone);
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task DeleteCustomerAsync(Guid customerId, Guid? deletedBy, CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            throw new ValidationException("CustomerId is required.");
        }

        if (deletedBy.HasValue && deletedBy.Value == Guid.Empty)
        {
            throw new ValidationException("DeletedBy must be a valid Guid.");
        }

        var entity = await _context.Customers
            .SingleOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException("Customer not found.");
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = deletedBy;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateRequest(Guid customerId, string? customerName)
    {
        if (customerId == Guid.Empty)
        {
            throw new ValidationException("CustomerId is required.");
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new ValidationException("CustomerName is required.");
        }
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static CustomerResponse Map(Customer entity)
    {
        return new CustomerResponse
        {
            CustomerId = entity.CustomerId,
            CustomerName = entity.CustomerName,
            TaxId = entity.TaxId,
            BranchNo = entity.BranchNo,
            Address = entity.Address,
            Email = entity.Email,
            Phone = entity.Phone,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
