using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ข้อมูลผู้รับใบกำกับภาษี
/// </summary>
public partial class TaxProfile
{
    /// <summary>
    /// รหัสข้อมูลภาษี UUID
    /// </summary>
    public Guid TaxProfileId { get; set; }

    /// <summary>
    /// บริษัทเจ้าของข้อมูลภาษี
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// ชื่อบุคคลหรือบริษัท
    /// </summary>
    public string TaxProfileName { get; set; } = null!;

    /// <summary>
    /// เลขประจำตัวผู้เสียภาษี
    /// </summary>
    public string? TaxId { get; set; }

    /// <summary>
    /// เลขสาขา
    /// </summary>
    public string? BranchNo { get; set; }

    /// <summary>
    /// ที่อยู่สำหรับออกเอกสารภาษี
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// ชื่อผู้ติดต่อ
    /// </summary>
    public string? ContactName { get; set; }

    /// <summary>
    /// อีเมลรับเอกสาร
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// เบอร์โทรศัพท์
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// เป็นข้อมูลเริ่มต้นหรือไม่
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// สถานะการใช้งาน
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// วันที่สร้างข้อมูล
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// วันที่แก้ไขล่าสุด
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
