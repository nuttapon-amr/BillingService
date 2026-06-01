using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ประวัติการเปลี่ยนแปลงเอกสาร
/// </summary>
public partial class DocumentAuditLog
{
    /// <summary>
    /// รหัส Audit Log UUID
    /// </summary>
    public Guid AuditLogId { get; set; }

    /// <summary>
    /// รหัสเอกสารที่เกี่ยวข้อง
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>
    /// Action เช่น Created, Issued, Cancelled, PdfGenerated
    /// </summary>
    public string Action { get; set; } = null!;

    /// <summary>
    /// ข้อมูลก่อนเปลี่ยน
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// ข้อมูลหลังเปลี่ยน
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// ผู้ทำรายการ
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// วันที่ทำรายการ
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public virtual Document? Document { get; set; }
}
