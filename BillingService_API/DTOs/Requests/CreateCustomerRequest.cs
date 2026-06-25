using System.ComponentModel.DataAnnotations;

namespace BillingService_API.DTOs.Requests;

public class CreateCustomerRequest
{
    [Required]
    public Guid CustomerId { get; set; }

    [MaxLength(255)]
    public string? CustomerName { get; set; }

    [MaxLength(20)]
    public string CustomerType { get; set; } = "INDIVIDUAL";

    [MaxLength(20)]
    public string? TaxId { get; set; }

    [MaxLength(10)]
    public string? BranchNo { get; set; }

    public string? Address { get; set; }

    [MaxLength(10)]
    public string? PostalCode { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }
}
