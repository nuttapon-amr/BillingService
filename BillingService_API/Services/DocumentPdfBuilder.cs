using DataAccess.Models.BillingService;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BillingDocument = DataAccess.Models.BillingService.Document;

namespace BillingService_API.Services;

internal static class DocumentPdfBuilder
{
    private const string ThaiFontFamily = "Tahoma";
    private const string ThaiBoldFontFamily = "Tahoma";

    private static bool _initialized;

    public static byte[] Build(BillingDocument document, string companyName, string title, DocumentTemplate template)
    {
        EnsureInitialized();

        var documentTitle = ResolveThaiDocumentTitle(document.DocumentType, title);
        var logoPath = ResolveLogoPath(template.LogoPath);
        var logoAvailable = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath);
        var checklist = DocumentComplianceRules.GetMandatoryFields(document.DocumentType);

        var pdf = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontFamily(ThaiFontFamily).FontSize(9).FontColor(Colors.Black));

                page.Header().Element(header => BuildHeader(header, document, companyName, documentTitle, template, logoPath, logoAvailable));
                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Element(content => BuildPartiesSection(content, document, companyName));
                    column.Item().Element(content => BuildItemsSection(content, document));
                    column.Item().Element(content => BuildSummarySection(content, document));
                    column.Item().Element(content => BuildNotesSection(content, document, template, checklist));
                    column.Item().Element(content => BuildApprovalSection(content));
                });

                page.Footer().PaddingTop(6).Column(footer =>
                {
                    footer.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    footer.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem()
                            .Text($"Template: {template.TemplateName ?? "-"}")
                            .FontSize(7)
                            .FontFamily(ThaiFontFamily)
                            .FontColor(Colors.Grey.Darken1);

                        row.RelativeItem()
                            .AlignRight()
                            .Text($"สร้างเมื่อ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                            .FontSize(7)
                            .FontFamily(ThaiFontFamily)
                            .FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        });

        return pdf.GeneratePdf();
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = true;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
        _initialized = true;
    }

    private static void BuildHeader(IContainer container, BillingDocument document, string companyName, string documentTitle, DocumentTemplate template, string? logoPath, bool logoAvailable)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(6);

                column.Item().Row(row =>
                {
                    row.ConstantItem(78)
                        .Height(66)
                        .Border(1)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(4)
                        .AlignMiddle()
                        .AlignCenter()
                        .Element(logoContainer =>
                        {
                            if (logoAvailable && logoPath is not null)
                            {
                                logoContainer.Image(logoPath).FitArea();
                            }
                            else
                            {
                                logoContainer.Text("โลโก้").FontSize(11).SemiBold().FontFamily(ThaiBoldFontFamily);
                            }
                        });

                    row.ConstantItem(10);

                    row.RelativeItem().Column(textColumn =>
                    {
                        textColumn.Item().Text(companyName).FontSize(14).SemiBold().FontFamily(ThaiBoldFontFamily);
                        textColumn.Item().Text(documentTitle).FontSize(12).SemiBold().FontFamily(ThaiBoldFontFamily);
                        textColumn.Item().Text(template.HeaderText ?? "-").FontSize(8).FontFamily(ThaiFontFamily);
                    });

                    row.ConstantItem(10);

                    row.ConstantItem(215).Column(meta =>
                    {
                        meta.Item().Text($"เลขที่เอกสาร: {document.DocumentNo}").FontSize(8).FontFamily(ThaiFontFamily).AlignRight();
                        meta.Item().Text($"วันที่ออก: {document.IssueDate:yyyy-MM-dd}").FontSize(8).FontFamily(ThaiFontFamily).AlignRight();
                        meta.Item().Text($"สถานะ: {document.Status}").FontSize(8).FontFamily(ThaiFontFamily).AlignRight();
                        meta.Item().Text($"ประเภท: {document.DocumentType}").FontSize(8).FontFamily(ThaiFontFamily).AlignRight();
                    });
                });

                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("เลขที่เอกสาร: ").SemiBold().FontFamily(ThaiBoldFontFamily);
                        text.Span(document.DocumentNo);
                    });

                    row.RelativeItem().Text(text =>
                    {
                        text.Span("ประเภทเอกสาร: ").SemiBold().FontFamily(ThaiBoldFontFamily);
                        text.Span(documentTitle);
                    });

                    row.RelativeItem().Text(text =>
                    {
                        text.Span("รหัสต้นทาง: ").SemiBold().FontFamily(ThaiBoldFontFamily);
                        text.Span(FormatOrDash(document.SourceNo));
                    });
                });
            });
    }

    private static void BuildPartiesSection(IContainer container, BillingDocument document, string companyName)
    {
        container.Column(column =>
        {
            column.Spacing(6);

            column.Item().Text("ข้อมูลคู่สัญญา").FontSize(11).SemiBold().FontFamily(ThaiBoldFontFamily);

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(inner => BuildInfoCard(inner, "ข้อมูลผู้ออกเอกสาร", new[]
                {
                    ("ชื่อบริษัท", companyName),
                    ("เลขผู้เสียภาษี", FormatOrDash(document.CompanyTaxIdSnapshot)),
                    ("สาขา", FormatOrDash(document.CompanyBranchNoSnapshot)),
                    ("ที่อยู่", FormatOrDash(document.CompanyAddressSnapshot))
                }));

                row.ConstantItem(10);

                row.RelativeItem().Element(inner => BuildInfoCard(inner, "ข้อมูลผู้รับเอกสาร", new[]
                {
                    ("ชื่อลูกค้า", FormatOrDash(document.CustomerNameSnapshot)),
                    ("ประเภทลูกค้า", ResolveCustomerTypeDisplay(document.CustomerTypeSnapshot)),
                    ("เลขผู้เสียภาษี", FormatOrDash(document.CustomerTaxIdSnapshot)),
                    ("สาขา", FormatOrDash(document.CustomerBranchNoSnapshot)),
                    ("รหัสไปรษณีย์", FormatOrDash(document.CustomerPostalCodeSnapshot)),
                    ("ที่อยู่", FormatOrDash(document.CustomerAddressSnapshot))
                }));
            });
        });
    }

    private static void BuildItemsSection(IContainer container, BillingDocument document)
    {
        var hasVat = HasVat(document);
        var vatHeader = document.DocumentType == "RC" && !hasVat ? "VAT (ถ้ามี)" : "VAT";

        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("รายการสินค้า/บริการ").FontSize(11).SemiBold().FontFamily(ThaiBoldFontFamily);

                column.Item().Background(Colors.BlueGrey.Darken3).PaddingVertical(6).PaddingHorizontal(6).Row(row =>
                {
                    row.ConstantItem(22).Text("ลำดับ").FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily);
                    row.RelativeItem(3).Text("รายการ").FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily);
                    row.ConstantItem(42).Text("จำนวน").FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily).AlignRight();
                    row.ConstantItem(70).Text("ราคา/หน่วยรวม VAT").FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily).AlignRight();
                    row.ConstantItem(60).Text("มูลค่า").FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily).AlignRight();
                    row.ConstantItem(46).Text(vatHeader).FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily).AlignRight();
                    row.ConstantItem(64).Text("รวมรายการ").FontColor(Colors.White).SemiBold().FontSize(8).FontFamily(ThaiBoldFontFamily).AlignRight();
                });

                if (document.DocumentItems.Count == 0)
                {
                    column.Item().PaddingVertical(6).Text("ไม่พบรายการสินค้า/บริการ").FontFamily(ThaiFontFamily);
                    return;
                }

                var rows = document.DocumentItems.ToList();
                for (var i = 0; i < rows.Count; i++)
                {
                    var item = rows[i];
                    column.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(6).PaddingHorizontal(6).Row(row =>
                    {
                        row.ConstantItem(22).Text((i + 1).ToString()).FontFamily(ThaiFontFamily);
                        row.RelativeItem(3).Text(item.ItemName).FontFamily(ThaiFontFamily);
                        row.ConstantItem(42).Text(item.Quantity.ToString("0.##")).FontFamily(ThaiFontFamily).AlignRight();
                        row.ConstantItem(70).Text(item.UnitPrice.ToString("0.00")).FontFamily(ThaiFontFamily).AlignRight();
                        row.ConstantItem(60).Text(item.Amount.ToString("0.00")).FontFamily(ThaiFontFamily).AlignRight();
                        row.ConstantItem(46).Text(hasVat ? item.VatAmount.ToString("0.00") : "-").FontFamily(ThaiFontFamily).AlignRight();
                        row.ConstantItem(64).Text((item.Amount + item.VatAmount).ToString("0.00")).FontFamily(ThaiFontFamily).AlignRight();
                    });
                }
            });
    }

    private static void BuildSummarySection(IContainer container, BillingDocument document)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(left => BuildReferenceCard(left, document));
            row.ConstantItem(10);
            row.ConstantItem(200).Element(right => BuildSummaryCard(right, document));
        });
    }

    private static void BuildNotesSection(IContainer container, BillingDocument document, DocumentTemplate template, IReadOnlyList<string> complianceFields)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(left => BuildNotesCard(left, document, template, complianceFields));
            row.ConstantItem(10);
            row.RelativeItem().Element(right => BuildComplianceCard(right, document.DocumentType, complianceFields));
        });
    }

    private static void BuildApprovalSection(IContainer container)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(12);
                column.Item().Text("การอนุมัติ/ลงนาม").FontSize(11).SemiBold().FontFamily(ThaiBoldFontFamily);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().PaddingTop(22).LineHorizontal(1).LineColor(Colors.Grey.Darken2);
                        col.Item().Text("ลงชื่อผู้มีอำนาจ").FontSize(8).FontFamily(ThaiFontFamily).AlignCenter();
                    });

                    row.ConstantItem(20);

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().PaddingTop(22).LineHorizontal(1).LineColor(Colors.Grey.Darken2);
                        col.Item().Text("ผู้รับเอกสาร").FontSize(8).FontFamily(ThaiFontFamily).AlignCenter();
                    });
                });
            });
    }

    private static void BuildInfoCard(IContainer container, string title, IEnumerable<(string Label, string Value)> rows)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(title).FontSize(10).SemiBold().FontFamily(ThaiBoldFontFamily);

                foreach (var (label, value) in rows)
                {
                    column.Item().Row(row =>
                    {
                        row.ConstantItem(92).Text($"{label}:").FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                        row.RelativeItem().Text(value).FontSize(8).FontFamily(ThaiFontFamily);
                    });
                }
            });
    }

    private static void BuildReferenceCard(IContainer container, BillingDocument document)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("อ้างอิงและหมายเหตุ").FontSize(10).SemiBold().FontFamily(ThaiBoldFontFamily);
                column.Item().Text($"อ้างอิงเอกสาร: {FormatOrDash(document.SourceNo)}").FontSize(8).FontFamily(ThaiFontFamily);
                column.Item().Text($"หมายเหตุ: {FormatOrDash(document.Remark)}").FontSize(8).FontFamily(ThaiFontFamily);
                if (!string.IsNullOrWhiteSpace(document.ReferenceDocumentId?.ToString()))
                {
                    column.Item().Text($"เอกสารอ้างอิง UUID: {document.ReferenceDocumentId}").FontSize(8).FontFamily(ThaiFontFamily);
                }
                if (string.Equals(document.DocumentType, "CN", StringComparison.OrdinalIgnoreCase))
                {
                    column.Item().Text($"เลขเอกสารต้นฉบับ: {FormatOrDash(document.OriginalDocumentNoSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                    column.Item().Text($"วันที่เอกสารต้นฉบับ: {FormatDateOrDash(document.OriginalIssueDateSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                    column.Item().Text($"ประเภทเอกสารต้นฉบับ: {FormatOrDash(document.OriginalDocumentTypeSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                    column.Item().Text($"ยอดเดิม: {FormatMoneyOrDash(document.OriginalSubTotalSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                    column.Item().Text($"VAT เดิม: {FormatMoneyOrDash(document.OriginalVatAmountSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                    column.Item().Text($"ยอดรวมเดิม: {FormatMoneyOrDash(document.OriginalGrandTotalSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                    column.Item().Text($"เหตุผลใบลดหนี้: {FormatOrDash(document.CreditNoteReasonSnapshot)}").FontSize(8).FontFamily(ThaiFontFamily);
                }
                column.Item().Text($"ประเภทธุรกรรม: {document.SourceType}").FontSize(8).FontFamily(ThaiFontFamily);
            });
    }

    private static void BuildSummaryCard(IContainer container, BillingDocument document)
    {
        var hasVat = HasVat(document);

        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("สรุปยอด").FontSize(10).SemiBold().FontFamily(ThaiBoldFontFamily);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("ยอดก่อนภาษี").FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                    row.ConstantItem(80).AlignRight().Text(document.SubTotal.ToString("0.00")).FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                });

                if (document.DocumentType == "RC" && !hasVat)
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text("ภาษีมูลค่าเพิ่ม").FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                        row.ConstantItem(80).AlignRight().Text("ไม่คิด VAT").FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                    });
                }
                else
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text("ภาษีมูลค่าเพิ่ม").FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                        row.ConstantItem(80).AlignRight().Text(document.VatAmount.ToString("0.00")).FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                    });
                }

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("ยอดสุทธิ").FontSize(8).SemiBold().FontFamily(ThaiBoldFontFamily);
                    row.ConstantItem(80).AlignRight().Text(document.GrandTotal.ToString("0.00")).FontSize(9).SemiBold().FontFamily(ThaiBoldFontFamily);
                });
            });
    }

    private static void BuildNotesCard(IContainer container, BillingDocument document, DocumentTemplate template, IReadOnlyList<string> complianceFields)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("รายละเอียดเอกสาร").FontSize(10).SemiBold().FontFamily(ThaiBoldFontFamily);
                column.Item().Text($"หัวข้อเทมเพลต: {FormatOrDash(template.TemplateName)}").FontSize(8).FontFamily(ThaiFontFamily);
                column.Item().Text($"ส่วนหัว: {FormatOrDash(template.HeaderText)}").FontSize(8).FontFamily(ThaiFontFamily);
                column.Item().Text($"ส่วนท้าย: {FormatOrDash(template.FooterText)}").FontSize(8).FontFamily(ThaiFontFamily);
                column.Item().Text($"โลโก้: {FormatOrDash(template.LogoPath)}").FontSize(8).FontFamily(ThaiFontFamily);
                column.Item().Text("ราคาที่บันทึกในเอกสารเป็นราคารวม VAT").FontSize(8).SemiBold().FontFamily(ThaiFontFamily);
                column.Item().Text($"ชนิดเอกสาร: {ResolveThaiDocumentTitle(document.DocumentType, document.DocumentType)}").FontSize(8).FontFamily(ThaiFontFamily);
                column.Item().Text($"วันที่จัดทำรายงาน: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC").FontSize(8).FontFamily(ThaiFontFamily);
            });
    }

    private static void BuildComplianceCard(IContainer container, string documentType, IReadOnlyList<string> complianceFields)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("รายการบังคับ").FontSize(10).SemiBold().FontFamily(ThaiBoldFontFamily);
                column.Item().Text(ResolveThaiDocumentTitle(documentType, documentType)).FontSize(8).FontFamily(ThaiFontFamily);

                foreach (var field in complianceFields)
                {
                    column.Item().Text($"• {field}").FontSize(8).FontFamily(ThaiFontFamily);
                }
            });
    }

    private static string ResolveThaiDocumentTitle(string documentType, string fallback)
    {
        return documentType switch
        {
            "RC" => "ใบเสร็จรับเงิน",
            "TI" => "ใบกำกับภาษี",
            "CN" => "ใบลดหนี้",
            _ => fallback
        };
    }

    private static string ResolveLogoPath(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return string.Empty;
        }

        var trimmed = logoPath.Trim();
        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            return trimmed;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, trimmed),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", trimmed)),
            Path.Combine(Directory.GetCurrentDirectory(), trimmed),
            Path.Combine(Directory.GetCurrentDirectory(), "BillingService_API", trimmed)
        };

        if (File.Exists(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static string FormatOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static string FormatDateOrDash(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    private static string FormatMoneyOrDash(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("0.00") : "-";
    }

    private static string ResolveCustomerTypeDisplay(string? customerType)
    {
        if (string.IsNullOrWhiteSpace(customerType))
        {
            return "-";
        }

        return customerType.Trim().ToUpperInvariant() switch
        {
            "CORPORATE" => "นิติบุคคล",
            "INDIVIDUAL" => "บุคคลธรรมดา",
            _ => customerType.Trim()
        };
    }

    private static bool HasVat(BillingDocument document)
    {
        return document.VatAmount != 0 || document.DocumentItems.Any(item => item.VatAmount != 0);
    }
}
