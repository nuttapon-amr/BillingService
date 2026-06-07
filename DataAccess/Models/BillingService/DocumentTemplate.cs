using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// Template เอกสารแยกตามบริษัทและประเภทเอกสาร
/// </summary>
public partial class DocumentTemplate
{
    /// <summary>
    /// รหัส Template UUID
    /// </summary>
    public Guid TemplateId { get; set; }

    /// <summary>
    /// บริษัทเจ้าของ Template
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// ประเภทเอกสาร เช่น RC, TI
    /// </summary>
    public string DocumentType { get; set; } = null!;

    /// <summary>
    /// ชื่อ Template
    /// </summary>
    public string TemplateName { get; set; } = null!;

    /// <summary>
    /// Path รูปโลโก้
    /// </summary>
    public string? LogoPath { get; set; }

    /// <summary>
    /// ข้อความส่วนหัวเอกสาร
    /// </summary>
    public string? HeaderText { get; set; }

    /// <summary>
    /// ข้อความท้ายเอกสาร
    /// </summary>
    public string? FooterText { get; set; }

    /// <summary>
    /// เป็น Template หลักหรือไม่
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

    public virtual DocumentType DocumentTypeNavigation { get; set; } = null!;
}
