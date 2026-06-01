using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ข้อมูลบริษัทที่ระบบออกเอกสารแทน
/// </summary>
public partial class Company
{
    /// <summary>
    /// รหัสบริษัท UUID
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// รหัสบริษัท เช่น AMR, EVC
    /// </summary>
    public string CompanyCode { get; set; } = null!;

    /// <summary>
    /// ชื่อบริษัทผู้ออกเอกสาร
    /// </summary>
    public string CompanyName { get; set; } = null!;

    /// <summary>
    /// เลขประจำตัวผู้เสียภาษี
    /// </summary>
    public string? TaxId { get; set; }

    /// <summary>
    /// รหัสสาขา เช่น 00000 สำนักงานใหญ่
    /// </summary>
    public string? BranchNo { get; set; }

    /// <summary>
    /// ที่อยู่บริษัท
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// อีเมลรับเอกสาร
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// เบอร์โทรศัพท์
    /// </summary>
    public string? Phone { get; set; }

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

    public virtual ICollection<DocumentRunningNumber> DocumentRunningNumbers { get; set; } = new List<DocumentRunningNumber>();

    public virtual ICollection<DocumentTemplate> DocumentTemplates { get; set; } = new List<DocumentTemplate>();

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
