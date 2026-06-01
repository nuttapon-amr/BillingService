using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// ไฟล์ PDF/XML ของเอกสาร
/// </summary>
public partial class DocumentFile
{
    /// <summary>
    /// รหัสไฟล์เอกสาร UUID
    /// </summary>
    public Guid DocumentFileId { get; set; }

    /// <summary>
    /// รหัสเอกสาร
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// ประเภทไฟล์ เช่น PDF, XML
    /// </summary>
    public string FileType { get; set; } = null!;

    /// <summary>
    /// ชื่อไฟล์
    /// </summary>
    public string FileName { get; set; } = null!;

    /// <summary>
    /// ตำแหน่งไฟล์หรือ URL
    /// </summary>
    public string FileUrl { get; set; } = null!;

    /// <summary>
    /// Hash สำหรับตรวจสอบไฟล์
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// วันที่สร้างไฟล์
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// ผู้สร้างไฟล์
    /// </summary>
    public string? GeneratedBy { get; set; }

    /// <summary>
    /// เป็นไฟล์ล่าสุดหรือไม่
    /// </summary>
    public bool? IsActive { get; set; }

    public virtual Document Document { get; set; } = null!;
}
