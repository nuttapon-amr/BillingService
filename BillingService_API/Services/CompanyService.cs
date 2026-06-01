using BillingService_API.DTOs.Requests;
using BillingService_API.DTOs.Responses;
using BillingService_API.Services.Interfaces;
using DataAccess.Models.BillingService;
using Microsoft.EntityFrameworkCore;

namespace BillingService_API.Services;

public class CompanyService : ICompanyService
{
    private readonly BillingServiceContext _context;

    public CompanyService(BillingServiceContext context)
    {
        _context = context;
    }

    public async Task<CompanyResponse?> GetCompanyByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ValidationException("CompanyId is required.");
        }

        var entity = await _context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<CompanyResponse> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.CompanyId, request.CompanyCode, request.CompanyName);

        var companyCode = request.CompanyCode.Trim();
        var existed = await _context.Companies
            .AsNoTracking()
            .AnyAsync(c => c.CompanyCode == companyCode, cancellationToken);

        if (existed)
        {
            throw new ConflictException("CompanyCode already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new Company
        {
            CompanyId = request.CompanyId,
            CompanyCode = companyCode,
            CompanyName = request.CompanyName.Trim(),
            TaxId = NormalizeNullable(request.TaxId),
            BranchNo = NormalizeNullable(request.BranchNo),
            Address = NormalizeNullable(request.Address),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Companies.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task<CompanyResponse> UpdateCompanyAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ValidationException("CompanyId is required.");
        }

        ValidateRequest(request.CompanyId, request.CompanyCode, request.CompanyName);

        if (request.CompanyId != companyId)
        {
            throw new ValidationException("CompanyId in route and body must match.");
        }

        var entity = await _context.Companies.SingleOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException("Company not found.");
        }

        var companyCode = request.CompanyCode.Trim();
        var duplicated = await _context.Companies
            .AsNoTracking()
            .AnyAsync(c => c.CompanyId != entity.CompanyId && c.CompanyCode == companyCode, cancellationToken);

        if (duplicated)
        {
            throw new ConflictException("CompanyCode already exists.");
        }

        entity.CompanyCode = companyCode;
        entity.CompanyName = request.CompanyName.Trim();
        entity.TaxId = NormalizeNullable(request.TaxId);
        entity.BranchNo = NormalizeNullable(request.BranchNo);
        entity.Address = NormalizeNullable(request.Address);
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    private static void ValidateRequest(Guid companyId, string? companyCode, string? companyName)
    {
        if (companyId == Guid.Empty)
        {
            throw new ValidationException("CompanyId is required.");
        }

        if (string.IsNullOrWhiteSpace(companyCode))
        {
            throw new ValidationException("CompanyCode is required.");
        }

        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ValidationException("CompanyName is required.");
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

    private static CompanyResponse Map(Company entity)
    {
        return new CompanyResponse
        {
            CompanyId = entity.CompanyId,
            CompanyCode = entity.CompanyCode,
            CompanyName = entity.CompanyName,
            TaxId = entity.TaxId,
            BranchNo = entity.BranchNo,
            Address = entity.Address,
            IsActive = entity.IsActive ?? true,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
