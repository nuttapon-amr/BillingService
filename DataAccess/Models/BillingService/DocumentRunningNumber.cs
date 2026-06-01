using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ตารางควบคุมเลขรันเอกสาร แยกตามบริษัท ประเภทเอกสาร และเดือน
/// </summary>
public partial class DocumentRunningNumber
{
    /// <summary>
    /// รหัส Running UUID
    /// </summary>
    public Guid RunningId { get; set; }

    /// <summary>
    /// บริษัทเจ้าของเลขเอกสาร
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// ประเภทเอกสาร เช่น RC, TI, CN, DN
    /// </summary>
    public string DocumentType { get; set; } = null!;

    /// <summary>
    /// ปีเดือนสำหรับรันเลข เช่น 202605
    /// </summary>
    public string YearMonth { get; set; } = null!;

    /// <summary>
    /// เลขล่าสุดที่ถูกใช้
    /// </summary>
    public int CurrentNumber { get; set; }

    /// <summary>
    /// วันที่สร้างข้อมูล
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// วันที่แก้ไขล่าสุด
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual DocumentType DocumentTypeNavigation { get; set; } = null!;
}
