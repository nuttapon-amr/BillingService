using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace DataAccess.Models.BillingService;

public partial class BillingServiceContext : DbContext
{
    public BillingServiceContext()
    {
    }

    public BillingServiceContext(DbContextOptions<BillingServiceContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentAuditLog> DocumentAuditLogs { get; set; }

    public virtual DbSet<DocumentCancelLog> DocumentCancelLogs { get; set; }

    public virtual DbSet<DocumentFile> DocumentFiles { get; set; }

    public virtual DbSet<DocumentItem> DocumentItems { get; set; }

    public virtual DbSet<DocumentRunningNumber> DocumentRunningNumbers { get; set; }

    public virtual DbSet<DocumentStatus> DocumentStatuses { get; set; }

    public virtual DbSet<DocumentTemplate> DocumentTemplates { get; set; }

    public virtual DbSet<DocumentType> DocumentTypes { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.CompanyId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("ข้อมูลบริษัทที่ระบบออกเอกสารแทน"));

            entity.HasIndex(e => e.CompanyCode, "UK_Companies_CompanyCode").IsUnique();

            entity.Property(e => e.CompanyId).HasComment("รหัสบริษัท UUID");
            entity.Property(e => e.Address)
                .HasComment("ที่อยู่บริษัท")
                .HasColumnType("text");
            entity.Property(e => e.BranchNo)
                .HasMaxLength(10)
                .HasComment("รหัสสาขา เช่น 00000 สำนักงานใหญ่");
            entity.Property(e => e.CompanyCode)
                .HasMaxLength(20)
                .HasComment("รหัสบริษัท เช่น AMR, EVC");
            entity.Property(e => e.CompanyName)
                .HasMaxLength(255)
                .HasComment("ชื่อบริษัทผู้ออกเอกสาร");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างข้อมูล")
                .HasColumnType("datetime");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasComment("อีเมลรับเอกสาร");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasComment("สถานะการใช้งาน");
            entity.Property(e => e.Phone)
                .HasMaxLength(50)
                .HasComment("เบอร์โทรศัพท์");
            entity.Property(e => e.TaxId)
                .HasMaxLength(20)
                .HasComment("เลขประจำตัวผู้เสียภาษี");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่แก้ไขล่าสุด")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("ข้อมูลลูกค้าผู้รับใบเสร็จหรือใบกำกับภาษี"));

            entity.Property(e => e.CustomerId).HasComment("รหัสลูกค้า UUID");
            entity.Property(e => e.Address)
                .HasComment("ที่อยู่ลูกค้า")
                .HasColumnType("text");
            entity.Property(e => e.BranchNo)
                .HasMaxLength(10)
                .HasComment("รหัสสาขาลูกค้า");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างข้อมูล")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerName)
                .HasMaxLength(255)
                .HasComment("ชื่อลูกค้า");
            entity.Property(e => e.CustomerType)
                .HasMaxLength(20)
                .HasDefaultValueSql("'INDIVIDUAL'")
                .HasComment("ประเภทลูกค้า: INDIVIDUAL หรือ CORPORATE");
            entity.Property(e => e.DeletedAt)
                .HasComment("เวลาโดนลบ")
                .HasColumnType("datetime");
            entity.Property(e => e.DeletedBy).HasComment("UserId คนที่ลบ (UUID)");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasComment("อีเมลลูกค้า");
            entity.Property(e => e.IsDeleted).HasComment("0 = active, 1 = deleted");
            entity.Property(e => e.Phone)
                .HasMaxLength(50)
                .HasComment("เบอร์โทรลูกค้า");
            entity.Property(e => e.PostalCode)
                .HasMaxLength(10)
                .HasComment("รหัสไปรษณีย์");
            entity.Property(e => e.TaxId)
                .HasMaxLength(20)
                .HasComment("เลขประจำตัวผู้เสียภาษีของลูกค้า");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่แก้ไขล่าสุด")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.DocumentId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("เอกสารทั้งหมด เช่น ใบเสร็จ ใบกำกับภาษี ใบลดหนี้ ใบเพิ่มหนี้"));

            entity.HasIndex(e => e.Status, "FK_Documents_DocumentStatuses");

            entity.HasIndex(e => e.DocumentType, "FK_Documents_DocumentTypes");

            entity.HasIndex(e => e.CompanyId, "IX_Documents_CompanyId");

            entity.HasIndex(e => new { e.CompanyId, e.SourceType, e.SourceNo }, "IX_Documents_Company_SourceNo");

            entity.HasIndex(e => new { e.CompanyId, e.Status, e.IssueDate }, "IX_Documents_Company_Status_IssueDate");

            entity.HasIndex(e => e.CustomerId, "IX_Documents_CustomerId");

            entity.HasIndex(e => e.IssueDate, "IX_Documents_IssueDate");

            entity.HasIndex(e => e.ReferenceDocumentId, "IX_Documents_ReferenceDocumentId");

            entity.HasIndex(e => new { e.SourceType, e.SourceId }, "IX_Documents_Source");

            entity.HasIndex(e => new { e.CompanyId, e.DocumentType, e.DocumentNo }, "UK_Documents_DocumentNo").IsUnique();

            entity.HasIndex(e => new { e.ReferenceDocumentId, e.DocumentType }, "UX_Documents_ReferenceDocument_Type").IsUnique();

            entity.Property(e => e.DocumentId).HasComment("รหัสเอกสาร UUID");
            entity.Property(e => e.CompanyAddressSnapshot)
                .HasComment("ที่อยู่บริษัท ณ วันที่ออกเอกสาร")
                .HasColumnType("text");
            entity.Property(e => e.CompanyBranchNoSnapshot)
                .HasMaxLength(10)
                .HasComment("สาขาบริษัท ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CompanyId).HasComment("บริษัทผู้ออกเอกสาร");
            entity.Property(e => e.CompanyNameSnapshot)
                .HasMaxLength(255)
                .HasComment("ชื่อบริษัท ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CompanyTaxIdSnapshot)
                .HasMaxLength(20)
                .HasComment("เลขผู้เสียภาษีบริษัท ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างข้อมูล")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerAddressSnapshot)
                .HasComment("ที่อยู่ ณ วันที่ออกเอกสาร")
                .HasColumnType("text");
            entity.Property(e => e.CustomerBranchNoSnapshot)
                .HasMaxLength(10)
                .HasComment("สาขา ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CustomerId).HasComment("ลูกค้าผู้รับเอกสาร");
            entity.Property(e => e.CustomerNameSnapshot)
                .HasMaxLength(255)
                .HasComment("ชื่อลูกค้า ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CustomerPostalCodeSnapshot)
                .HasMaxLength(10)
                .HasComment("รหัสไปรษณีย์ ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CustomerTaxIdSnapshot)
                .HasMaxLength(20)
                .HasComment("เลขผู้เสียภาษี ณ วันที่ออกเอกสาร");
            entity.Property(e => e.CustomerTypeSnapshot)
                .HasMaxLength(20)
                .HasComment("ประเภทผู้เสียภาษี ณ วันที่ออกเอกสาร (INDIVIDUAL/CORPORATE)");
            entity.Property(e => e.DocumentNo)
                .HasMaxLength(50)
                .HasComment("เลขที่เอกสาร เช่น AMR-RC-202605-000001");
            entity.Property(e => e.DocumentType)
                .HasMaxLength(20)
                .HasComment("ประเภทเอกสาร RC=ใบเสร็จ, TI=ใบกำกับภาษี, CN=ใบลดหนี้, DN=ใบเพิ่มหนี้");
            entity.Property(e => e.DueDate)
                .HasComment("วันครบกำหนด ถ้ามี")
                .HasColumnType("datetime");
            entity.Property(e => e.GrandTotal)
                .HasPrecision(18, 2)
                .HasComment("ยอดรวมสุทธิ");
            entity.Property(e => e.IssueDate)
                .HasComment("วันที่ออกเอกสาร")
                .HasColumnType("datetime");
            entity.Property(e => e.ReferenceDocumentId).HasComment("เอกสารอ้างอิง เช่น TI อ้างอิง RC เดิม");
            entity.Property(e => e.Remark)
                .HasComment("หมายเหตุ")
                .HasColumnType("text");
            entity.Property(e => e.RunningNumber).HasComment("เลข Running ของเอกสาร");
            entity.Property(e => e.RunningYearMonth)
                .HasMaxLength(6)
                .IsFixedLength()
                .HasComment("ปีเดือนที่ใช้รันเลขเอกสาร");
            entity.Property(e => e.SourceId).HasComment("รหัสข้อมูลต้นทาง");
            entity.Property(e => e.SourceNo)
                .HasMaxLength(100)
                .HasComment("เลขอ้างอิงจากระบบต้นทาง");
            entity.Property(e => e.SourceType)
                .HasMaxLength(50)
                .HasComment("ประเภทข้อมูลต้นทาง เช่น ChargingTransaction, Subscription");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Issued'")
                .HasComment("สถานะเอกสาร เช่น Draft, Issued, Cancelled");
            entity.Property(e => e.SubTotal)
                .HasPrecision(18, 2)
                .HasComment("ยอดก่อนภาษี");
            entity.Property(e => e.TaxInvoiceIssued).HasComment("ออกใบกำกับภาษีแล้วหรือยัง");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่แก้ไขล่าสุด")
                .HasColumnType("datetime");
            entity.Property(e => e.VatAmount)
                .HasPrecision(18, 2)
                .HasComment("ยอดภาษีมูลค่าเพิ่ม");

            entity.HasOne(d => d.Company).WithMany(p => p.Documents)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Documents_Companies");

            entity.HasOne(d => d.Customer).WithMany(p => p.Documents)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_Documents_Customers");

            entity.HasOne(d => d.DocumentTypeNavigation).WithMany(p => p.Documents)
                .HasForeignKey(d => d.DocumentType)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Documents_DocumentTypes");

            entity.HasOne(d => d.ReferenceDocument).WithMany(p => p.InverseReferenceDocument)
                .HasForeignKey(d => d.ReferenceDocumentId)
                .HasConstraintName("FK_Documents_ReferenceDocument");

            entity.HasOne(d => d.StatusNavigation).WithMany(p => p.Documents)
                .HasForeignKey(d => d.Status)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Documents_DocumentStatuses");
        });

        modelBuilder.Entity<DocumentAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("ประวัติการเปลี่ยนแปลงเอกสาร"));

            entity.HasIndex(e => e.Action, "IX_DocumentAuditLogs_Action");

            entity.HasIndex(e => e.DocumentId, "IX_DocumentAuditLogs_DocumentId");

            entity.Property(e => e.AuditLogId).HasComment("รหัส Audit Log UUID");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasComment("Action เช่น Created, Issued, Cancelled, PdfGenerated");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่ทำรายการ")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(100)
                .HasComment("ผู้ทำรายการ");
            entity.Property(e => e.DocumentId).HasComment("รหัสเอกสารที่เกี่ยวข้อง");
            entity.Property(e => e.NewValue)
                .HasComment("ข้อมูลหลังเปลี่ยน")
                .HasColumnType("json");
            entity.Property(e => e.OldValue)
                .HasComment("ข้อมูลก่อนเปลี่ยน")
                .HasColumnType("json");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentAuditLogs)
                .HasForeignKey(d => d.DocumentId)
                .HasConstraintName("FK_DocumentAuditLogs_Documents");
        });

        modelBuilder.Entity<DocumentCancelLog>(entity =>
        {
            entity.HasKey(e => e.CancelLogId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("ประวัติการยกเลิกเอกสาร"));

            entity.HasIndex(e => e.DocumentId, "FK_DocumentCancelLogs_Documents");

            entity.Property(e => e.CancelLogId).HasComment("รหัสประวัติการยกเลิก UUID");
            entity.Property(e => e.CancelReason)
                .HasComment("เหตุผลการยกเลิก")
                .HasColumnType("text");
            entity.Property(e => e.CancelledAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่ยกเลิก")
                .HasColumnType("datetime");
            entity.Property(e => e.CancelledBy)
                .HasMaxLength(100)
                .HasComment("ผู้ยกเลิก");
            entity.Property(e => e.DocumentId).HasComment("เอกสารที่ถูกยกเลิก");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentCancelLogs)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentCancelLogs_Documents");
        });

        modelBuilder.Entity<DocumentFile>(entity =>
        {
            entity.HasKey(e => e.DocumentFileId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("ไฟล์ PDF/XML ของเอกสาร"));

            entity.HasIndex(e => e.DocumentId, "IX_DocumentFiles_DocumentId");

            entity.Property(e => e.DocumentFileId).HasComment("รหัสไฟล์เอกสาร UUID");
            entity.Property(e => e.DocumentId).HasComment("รหัสเอกสาร");
            entity.Property(e => e.FileHash)
                .HasMaxLength(255)
                .HasComment("Hash สำหรับตรวจสอบไฟล์");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasComment("ชื่อไฟล์");
            entity.Property(e => e.FileType)
                .HasMaxLength(20)
                .HasComment("ประเภทไฟล์ เช่น PDF, XML");
            entity.Property(e => e.FileUrl)
                .HasComment("ตำแหน่งไฟล์หรือ URL")
                .HasColumnType("text");
            entity.Property(e => e.GeneratedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างไฟล์")
                .HasColumnType("datetime");
            entity.Property(e => e.GeneratedBy)
                .HasMaxLength(100)
                .HasComment("ผู้สร้างไฟล์");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasComment("เป็นไฟล์ล่าสุดหรือไม่");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentFiles)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentFiles_Documents");
        });

        modelBuilder.Entity<DocumentItem>(entity =>
        {
            entity.HasKey(e => e.DocumentItemId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("รายการสินค้า/บริการในเอกสาร"));

            entity.HasIndex(e => e.DocumentId, "IX_DocumentItems_DocumentId");

            entity.Property(e => e.DocumentItemId).HasComment("รหัสรายการเอกสาร UUID");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasComment("ยอดรวมก่อน VAT");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างข้อมูล")
                .HasColumnType("datetime");
            entity.Property(e => e.DocumentId).HasComment("รหัสเอกสาร");
            entity.Property(e => e.ItemCode)
                .HasMaxLength(50)
                .HasComment("รหัสสินค้า/บริการ");
            entity.Property(e => e.ItemName)
                .HasMaxLength(255)
                .HasComment("ชื่อสินค้า/บริการ");
            entity.Property(e => e.Quantity)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'1.00'")
                .HasComment("จำนวน");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 2)
                .HasComment("ราคาต่อหน่วย");
            entity.Property(e => e.VatAmount)
                .HasPrecision(18, 2)
                .HasComment("ยอด VAT ของรายการ");
            entity.Property(e => e.VatRate)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("'7.00'")
                .HasComment("อัตราภาษีมูลค่าเพิ่ม");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentItems)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentItems_Documents");
        });

        modelBuilder.Entity<DocumentRunningNumber>(entity =>
        {
            entity.HasKey(e => e.RunningId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("ตารางควบคุมเลขรันเอกสาร แยกตามบริษัท ประเภทเอกสาร และเดือน"));

            entity.HasIndex(e => e.DocumentType, "FK_DocumentRunningNumbers_DocumentTypes");

            entity.HasIndex(e => new { e.CompanyId, e.DocumentType, e.YearMonth }, "IX_DocumentRunningNumbers_Lookup").IsUnique();

            entity.Property(e => e.RunningId).HasComment("รหัส Running UUID");
            entity.Property(e => e.CompanyId).HasComment("บริษัทเจ้าของเลขเอกสาร");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างข้อมูล")
                .HasColumnType("datetime");
            entity.Property(e => e.CurrentNumber).HasComment("เลขล่าสุดที่ถูกใช้");
            entity.Property(e => e.DocumentType)
                .HasMaxLength(20)
                .HasComment("ประเภทเอกสาร เช่น RC, TI, CN, DN");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่แก้ไขล่าสุด")
                .HasColumnType("datetime");
            entity.Property(e => e.YearMonth)
                .HasMaxLength(6)
                .IsFixedLength()
                .HasComment("ปีเดือนสำหรับรันเลข เช่น 202605");

            entity.HasOne(d => d.Company).WithMany(p => p.DocumentRunningNumbers)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentRunningNumbers_Companies");

            entity.HasOne(d => d.DocumentTypeNavigation).WithMany(p => p.DocumentRunningNumbers)
                .HasForeignKey(d => d.DocumentType)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentRunningNumbers_DocumentTypes");
        });

        modelBuilder.Entity<DocumentStatus>(entity =>
        {
            entity.HasKey(e => e.StatusCode).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("สถานะเอกสาร"));

            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasComment("รหัสสถานะ");
            entity.Property(e => e.StatusName)
                .HasMaxLength(100)
                .HasComment("ชื่อสถานะ");
        });

        modelBuilder.Entity<DocumentTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId).HasName("PRIMARY");

            entity.ToTable(tb => tb.HasComment("Template เอกสารแยกตามบริษัทและประเภทเอกสาร"));

            entity.HasIndex(e => e.CompanyId, "FK_DocumentTemplates_Companies");

            entity.HasIndex(e => e.DocumentType, "FK_DocumentTemplates_DocumentTypes");

            entity.Property(e => e.TemplateId).HasComment("รหัส Template UUID");
            entity.Property(e => e.CompanyId).HasComment("บริษัทเจ้าของ Template");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่สร้างข้อมูล")
                .HasColumnType("datetime");
            entity.Property(e => e.DocumentType)
                .HasMaxLength(20)
                .HasComment("ประเภทเอกสาร เช่น RC, TI");
            entity.Property(e => e.FooterText)
                .HasComment("ข้อความท้ายเอกสาร")
                .HasColumnType("text");
            entity.Property(e => e.HeaderText)
                .HasComment("ข้อความส่วนหัวเอกสาร")
                .HasColumnType("text");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasComment("สถานะการใช้งาน");
            entity.Property(e => e.IsDefault).HasComment("เป็น Template หลักหรือไม่");
            entity.Property(e => e.LogoPath)
                .HasComment("Path รูปโลโก้")
                .HasColumnType("text");
            entity.Property(e => e.TemplateName)
                .HasMaxLength(255)
                .HasComment("ชื่อ Template");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasComment("วันที่แก้ไขล่าสุด")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Company).WithMany(p => p.DocumentTemplates)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentTemplates_Companies");

            entity.HasOne(d => d.DocumentTypeNavigation).WithMany(p => p.DocumentTemplates)
                .HasForeignKey(d => d.DocumentType)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DocumentTemplates_DocumentTypes");
        });

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.HasKey(e => e.DocumentTypeCode).HasName("PRIMARY");

            entity.Property(e => e.DocumentTypeCode).HasMaxLength(20);
            entity.Property(e => e.DocumentTypeName).HasMaxLength(100);
            entity.Property(e => e.IsVatDocument).HasComment("เป็นเอกสารภาษีหรือไม่");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
