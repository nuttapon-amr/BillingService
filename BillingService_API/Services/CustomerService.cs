using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;
using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;

namespace BillingService_API.Services;

public class CustomerService : ICustomerService
{
    private static readonly HashSet<string> AllowedCustomerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INDIVIDUAL",
        "CORPORATE"
    };

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
        ValidateCreateRequest(request);

        var existingCustomer = await _context.Customers
            .SingleOrDefaultAsync(c => c.CustomerId == request.CustomerId, cancellationToken);

        var now = DateTime.UtcNow;
        Customer entity;

        if (existingCustomer is null)
        {
            entity = new Customer
            {
                CustomerId = request.CustomerId,
                CreatedAt = now
            };
            _context.Customers.Add(entity);
        }
        else
        {
            if (!existingCustomer.IsDeleted)
            {
                throw new ConflictException("CustomerId already exists.");
            }

            entity = existingCustomer;
        }

        entity.CustomerName = request.CustomerName.Trim();
        entity.CustomerType = NormalizeCustomerType(request.CustomerType);
        entity.TaxId = NormalizeNullable(request.TaxId);
        entity.BranchNo = NormalizeNullable(request.BranchNo);
        entity.Address = NormalizeNullable(request.Address);
        entity.PostalCode = NormalizeNullable(request.PostalCode);
        entity.Email = NormalizeNullable(request.Email);
        entity.Phone = NormalizeNullable(request.Phone);
        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;
        entity.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task<CustomerResponse> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ValidateUpdateRequest(customerId, request);

        var entity = await _context.Customers
            .SingleOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException("Customer not found.");
        }

        entity.CustomerName = request.CustomerName.Trim();
        entity.CustomerType = NormalizeCustomerType(request.CustomerType);
        entity.TaxId = NormalizeNullable(request.TaxId);
        entity.BranchNo = NormalizeNullable(request.BranchNo);
        entity.Address = NormalizeNullable(request.Address);
        entity.PostalCode = NormalizeNullable(request.PostalCode);
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

    private static void ValidateCreateRequest(CreateCustomerRequest request)
    {
        if (request.CustomerId == Guid.Empty)
        {
            throw new ValidationException("CustomerId is required.");
        }

        ValidateRequestCore(request.CustomerName, request.CustomerType);
    }

    private static void ValidateUpdateRequest(Guid customerId, UpdateCustomerRequest request)
    {
        if (customerId == Guid.Empty)
        {
            throw new ValidationException("CustomerId is required.");
        }

        ValidateRequestCore(request.CustomerName, request.CustomerType);
    }

    private static void ValidateRequestCore(string? customerName, string? customerType)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new ValidationException("CustomerName is required.");
        }

        if (string.IsNullOrWhiteSpace(customerType))
        {
            throw new ValidationException("CustomerType is required.");
        }

        if (!AllowedCustomerTypes.Contains(customerType.Trim()))
        {
            throw new ValidationException("CustomerType must be either INDIVIDUAL or CORPORATE.");
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

    private static string NormalizeCustomerType(string value) => value.Trim().ToUpperInvariant();

    private static CustomerResponse Map(Customer entity)
    {
        return new CustomerResponse
        {
            CustomerId = entity.CustomerId,
            CustomerName = entity.CustomerName,
            CustomerType = entity.CustomerType,
            TaxId = entity.TaxId,
            BranchNo = entity.BranchNo,
            Address = entity.Address,
            PostalCode = entity.PostalCode,
            Email = entity.Email,
            Phone = entity.Phone,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
