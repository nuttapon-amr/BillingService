using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// เอกสารทั้งหมด เช่น ใบเสร็จ ใบกำกับภาษี ใบลดหนี้ ใบเพิ่มหนี้
/// </summary>
public partial class Document
{
    /// <summary>
    /// รหัสเอกสาร UUID
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// บริษัทผู้ออกเอกสาร
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// ชื่อบริษัท ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CompanyNameSnapshot { get; set; }

    /// <summary>
    /// เลขผู้เสียภาษีบริษัท ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CompanyTaxIdSnapshot { get; set; }

    /// <summary>
    /// สาขาบริษัท ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CompanyBranchNoSnapshot { get; set; }

    /// <summary>
    /// ที่อยู่บริษัท ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CompanyAddressSnapshot { get; set; }

    /// <summary>
    /// อีเมลบริษัท ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CompanyEmailSnapshot { get; set; }

    /// <summary>
    /// เบอร์โทรบริษัท ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CompanyPhoneSnapshot { get; set; }

    /// <summary>
    /// ลูกค้าผู้รับเอกสาร
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// ชื่อลูกค้า ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerNameSnapshot { get; set; }

    /// <summary>
    /// ประเภทผู้เสียภาษี ณ วันที่ออกเอกสาร (INDIVIDUAL/CORPORATE)
    /// </summary>
    public string? CustomerTypeSnapshot { get; set; }

    /// <summary>
    /// เลขผู้เสียภาษี ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerTaxIdSnapshot { get; set; }

    /// <summary>
    /// สาขา ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerBranchNoSnapshot { get; set; }

    /// <summary>
    /// ที่อยู่ ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerAddressSnapshot { get; set; }

    /// <summary>
    /// รหัสไปรษณีย์ ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerPostalCodeSnapshot { get; set; }

    /// <summary>
    /// อีเมลลูกค้า ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerEmailSnapshot { get; set; }

    /// <summary>
    /// เบอร์โทรลูกค้า ณ วันที่ออกเอกสาร
    /// </summary>
    public string? CustomerPhoneSnapshot { get; set; }

    /// <summary>
    /// วิธีชำระเงิน ณ วันที่ออกเอกสาร
    /// </summary>
    public string? PaymentMethodSnapshot { get; set; }

    /// <summary>
    /// ประเภทข้อมูลต้นทาง เช่น ChargingTransaction, Subscription
    /// </summary>
    public string SourceType { get; set; } = null!;

    /// <summary>
    /// รหัสข้อมูลต้นทาง
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// เลขอ้างอิงจากระบบต้นทาง
    /// </summary>
    public string? SourceNo { get; set; }

    /// <summary>
    /// เอกสารอ้างอิง เช่น TI อ้างอิง RC เดิม
    /// </summary>
    public Guid? ReferenceDocumentId { get; set; }

    /// <summary>
    /// เลขเอกสารอ้างอิงแบบ snapshot สำหรับแสดงใน PDF
    /// </summary>
    public string? ReferenceDocumentNoSnapshot { get; set; }

    /// <summary>
    /// วันที่เอกสารอ้างอิงแบบ snapshot
    /// </summary>
    public DateTime? ReferenceIssueDateSnapshot { get; set; }

    /// <summary>
    /// ประเภทเอกสารอ้างอิงแบบ snapshot
    /// </summary>
    public string? ReferenceDocumentTypeSnapshot { get; set; }

    /// <summary>
    /// ประเภทเอกสาร RC=ใบเสร็จ, TI=ใบกำกับภาษี, CN=ใบลดหนี้, DN=ใบเพิ่มหนี้
    /// </summary>
    public string DocumentType { get; set; } = null!;

    /// <summary>
    /// เลขที่เอกสาร เช่น AMR-RC-202605-000001
    /// </summary>
    public string DocumentNo { get; set; } = null!;

    /// <summary>
    /// ปีเดือนที่ใช้รันเลขเอกสาร
    /// </summary>
    public string RunningYearMonth { get; set; } = null!;

    /// <summary>
    /// เลข Running ของเอกสาร
    /// </summary>
    public int RunningNumber { get; set; }

    /// <summary>
    /// วันที่ออกเอกสาร
    /// </summary>
    public DateTime IssueDate { get; set; }

    /// <summary>
    /// วันครบกำหนด ถ้ามี
    /// </summary>
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// ยอดก่อนภาษี
    /// </summary>
    public decimal SubTotal { get; set; }

    /// <summary>
    /// ยอดภาษีมูลค่าเพิ่ม
    /// </summary>
    public decimal VatAmount { get; set; }

    /// <summary>
    /// ยอดรวมสุทธิ
    /// </summary>
    public decimal GrandTotal { get; set; }

    /// <summary>
    /// สถานะเอกสาร เช่น Draft, Issued, Cancelled
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// หมายเหตุ
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// วันที่สร้างข้อมูล
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// วันที่แก้ไขล่าสุด
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// ออกใบกำกับภาษีแล้วหรือยัง
    /// </summary>
    public bool TaxInvoiceIssued { get; set; }

    /// <summary>
    /// เลขเอกสารต้นฉบับสำหรับใบลดหนี้
    /// </summary>
    public string? OriginalDocumentNoSnapshot { get; set; }

    /// <summary>
    /// วันที่ออกเอกสารต้นฉบับ
    /// </summary>
    public DateTime? OriginalIssueDateSnapshot { get; set; }

    /// <summary>
    /// ประเภทเอกสารต้นฉบับ
    /// </summary>
    public string? OriginalDocumentTypeSnapshot { get; set; }

    /// <summary>
    /// ยอดก่อนภาษีของเอกสารต้นฉบับ
    /// </summary>
    public decimal? OriginalSubTotalSnapshot { get; set; }

    /// <summary>
    /// ยอด VAT ของเอกสารต้นฉบับ
    /// </summary>
    public decimal? OriginalVatAmountSnapshot { get; set; }

    /// <summary>
    /// ยอดรวมสุทธิของเอกสารต้นฉบับ
    /// </summary>
    public decimal? OriginalGrandTotalSnapshot { get; set; }

    /// <summary>
    /// เหตุผลการออกใบลดหนี้
    /// </summary>
    public string? CreditNoteReasonSnapshot { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<DocumentAuditLog> DocumentAuditLogs { get; set; } = new List<DocumentAuditLog>();

    public virtual ICollection<DocumentCancelLog> DocumentCancelLogs { get; set; } = new List<DocumentCancelLog>();

    public virtual ICollection<DocumentFile> DocumentFiles { get; set; } = new List<DocumentFile>();

    public virtual ICollection<DocumentItem> DocumentItems { get; set; } = new List<DocumentItem>();

    public virtual DocumentType DocumentTypeNavigation { get; set; } = null!;

    public virtual ICollection<Document> InverseReferenceDocument { get; set; } = new List<Document>();

    public virtual Document? ReferenceDocument { get; set; }

    public virtual DocumentStatus StatusNavigation { get; set; } = null!;
}
