using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ข้อมูลลูกค้าผู้รับใบเสร็จหรือใบกำกับภาษี
/// </summary>
public partial class Customer
{
    /// <summary>
    /// รหัสลูกค้า UUID
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// ชื่อลูกค้า
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// ประเภทลูกค้า: INDIVIDUAL หรือ CORPORATE
    /// </summary>
    public string CustomerType { get; set; } = null!;

    /// <summary>
    /// เลขประจำตัวผู้เสียภาษีของลูกค้า
    /// </summary>
    public string? TaxId { get; set; }

    /// <summary>
    /// รหัสสาขาลูกค้า
    /// </summary>
    public string? BranchNo { get; set; }

    /// <summary>
    /// ที่อยู่ลูกค้า
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// รหัสไปรษณีย์
    /// </summary>
    public string? PostalCode { get; set; }

    /// <summary>
    /// อีเมลลูกค้า
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// เบอร์โทรลูกค้า
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// วันที่สร้างข้อมูล
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// วันที่แก้ไขล่าสุด
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 0 = active, 1 = deleted
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// เวลาโดนลบ
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// UserId คนที่ลบ (UUID)
    /// </summary>
    public Guid? DeletedBy { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
