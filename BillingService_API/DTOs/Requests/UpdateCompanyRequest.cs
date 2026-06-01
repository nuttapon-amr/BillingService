using System.ComponentModel.DataAnnotations;

namespace BillingService_API.DTOs.Requests;

public class UpdateCompanyRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [MaxLength(20)]
    public string CompanyCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? TaxId { get; set; }

    [MaxLength(10)]
    public string? BranchNo { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; }
}
