using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

/// <summary>
/// รายการสินค้า/บริการในเอกสาร
/// </summary>
public partial class DocumentItem
{
    /// <summary>
    /// รหัสรายการเอกสาร UUID
    /// </summary>
    public Guid DocumentItemId { get; set; }

    /// <summary>
    /// รหัสเอกสาร
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// ลำดับรายการในเอกสาร
    /// </summary>
    public int? LineNo { get; set; }

    /// <summary>
    /// รหัสสินค้า/บริการ
    /// </summary>
    public string? ItemCode { get; set; }

    /// <summary>
    /// หน่วยนับ
    /// </summary>
    public string? UnitName { get; set; }

    /// <summary>
    /// ชื่อสินค้า/บริการ
    /// </summary>
    public string ItemName { get; set; } = null!;

    /// <summary>
    /// จำนวน
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// ราคาต่อหน่วย
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// ยอดรวมก่อน VAT
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// อัตราภาษีมูลค่าเพิ่ม
    /// </summary>
    public decimal VatRate { get; set; }

    /// <summary>
    /// ยอด VAT ของรายการ
    /// </summary>
    public decimal VatAmount { get; set; }

    /// <summary>
    /// ส่วนลดต่อรายการ
    /// </summary>
    public decimal? DiscountAmount { get; set; }

    /// <summary>
    /// ยอดสุทธิหลังหักส่วนลด
    /// </summary>
    public decimal? NetAmount { get; set; }

    /// <summary>
    /// หมายเหตุของรายการ
    /// </summary>
    public string? ItemRemark { get; set; }

    /// <summary>
    /// วันที่สร้างข้อมูล
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public virtual Document Document { get; set; } = null!;
}
