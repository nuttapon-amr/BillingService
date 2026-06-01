using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ประวัติการยกเลิกเอกสาร
/// </summary>
public partial class DocumentCancelLog
{
    /// <summary>
    /// รหัสประวัติการยกเลิก UUID
    /// </summary>
    public Guid CancelLogId { get; set; }

    /// <summary>
    /// เอกสารที่ถูกยกเลิก
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// เหตุผลการยกเลิก
    /// </summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// ผู้ยกเลิก
    /// </summary>
    public string? CancelledBy { get; set; }

    /// <summary>
    /// วันที่ยกเลิก
    /// </summary>
    public DateTime CancelledAt { get; set; }

    public virtual Document Document { get; set; } = null!;
}
