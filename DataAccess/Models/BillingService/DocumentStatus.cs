using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// สถานะเอกสาร
/// </summary>
public partial class DocumentStatus
{
    /// <summary>
    /// รหัสสถานะ
    /// </summary>
    public string StatusCode { get; set; } = null!;

    /// <summary>
    /// ชื่อสถานะ
    /// </summary>
    public string StatusName { get; set; } = null!;

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
