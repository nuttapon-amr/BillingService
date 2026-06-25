using System.Drawing;
using System.Globalization;
using DataAccess.Models.BillingService;
using FastReport;
using FastReport.Export.PdfSimple;
using FastReport.Utils;
using BillingDocument = DataAccess.Models.BillingService.Document;

namespace BillingService_API.Services;

internal static class DocumentPdfBuilder
{
    private const string ThaiFontFamily = "TH SarabunPSK";
    private const string FontsFolderName = "fonts";
    private const string FontsSubFolderName = "sarabun";
    private const string ReportTemplatesFolderName = "ReportTemplates";
    private const string RegularFontFileName = "THSarabun.ttf";
    private const string BoldFontFileName = "THSarabun Bold.ttf";
    private const string ItalicFontFileName = "THSarabun Italic.ttf";
    private const string BoldItalicFontFileName = "THSarabun Bold Italic.ttf";

    private static bool _initialized;
    private static int _objectCounter;

    public static byte[] Build(BillingDocument document, string companyName, string title, DocumentTemplate template)
    {
        EnsureInitialized();

        using var report = CreateReport(document, companyName, title, template);
        using var output = new MemoryStream();
        report.Prepare();
        report.Export(new PDFSimpleExport(), output);
        return output.ToArray();
    }

    internal static Report CreateReport(BillingDocument document, string companyName, string title, DocumentTemplate template)
    {
        var documentType = (document.DocumentType ?? string.Empty).Trim().ToUpperInvariant();
        var isReceipt = documentType == "RC";
        var isTaxInvoice = documentType == "TI";
        var isCreditNote = documentType == "CN";
        var hasVat = HasVat(document);
        var documentTitle = ResolveDocumentTitle(documentType, title, hasVat);
        var logoPath = ResolveLogoPath(template.LogoPath);
        var logoAvailable = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath);
        var itemRows = BuildItemRows(document, hasVat);

        if (isTaxInvoice || isReceipt)
        {
            var templateReport = TryCreateTemplateReport(document, companyName, template, documentType, documentTitle, itemRows, logoPath, logoAvailable, hasVat);
            if (templateReport is not null)
            {
                return templateReport;
            }
        }

        var report = new Report();
        report.ScriptLanguage = Language.CSharp;
        report.RegisterData(itemRows, "Items");

        var page = new ReportPage
        {
            Name = "Page1",
            PaperWidth = 210,
            PaperHeight = 297,
            LeftMargin = 8,
            RightMargin = 8,
            TopMargin = 8,
            BottomMargin = 8
        };
        report.Pages.Add(page);

        BuildPageHeader(page, document, companyName, documentTitle, template, logoPath, logoAvailable, isReceipt, isTaxInvoice, isCreditNote, hasVat);

        if (isReceipt)
        {
            BuildReceiptTitle(page, document, hasVat);
            BuildReceiptItems(report, page, hasVat);
            BuildReceiptSummary(page, document, hasVat);
        }
        else if (isTaxInvoice)
        {
            BuildTaxInvoiceTitle(page, document, companyName);
            BuildTaxInvoiceItems(report, page);
            BuildTaxInvoiceSummary(page, document, companyName, template);
        }
        else
        {
            BuildCreditNoteTitle(page, document);
            BuildCreditNoteItems(report, page);
            BuildCreditNoteSummary(page, document, companyName, template);
        }

        return report;
    }

    private static Report? TryCreateTemplateReport(
        BillingDocument document,
        string companyName,
        DocumentTemplate template,
        string documentType,
        string documentTitle,
        IEnumerable<PdfItemRow> itemRows,
        string? logoPath,
        bool logoAvailable,
        bool hasVat)
    {
        var templatePath = ResolveTemplatePath(documentType);
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            return null;
        }

        try
        {
            var report = new Report();
            report.ScriptLanguage = Language.CSharp;
            report.Load(templatePath);
            report.RegisterData(itemRows, "Items");

            if (report.GetDataSource("Items") is { } dataSource)
            {
                dataSource.Enabled = true;
            }

            if (documentType == "TI")
            {
                BindTiTemplate(report, document, companyName, template, documentTitle, logoPath, logoAvailable, hasVat);
            }
            else if (documentType == "RC")
            {
                BindRcTemplate(report, document, companyName, template, logoPath, logoAvailable, hasVat);
            }
            else
            {
                BindTaxInvoiceTemplate(report, document, companyName, template, documentTitle, logoPath, logoAvailable, hasVat);
            }
            return report;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveTemplatePath(string documentType)
    {
        var fileName = documentType switch
        {
            "TI" => "TI.frx",
            "RC" => "RC.frx",
            "CN" => "CN.frx",
            _ => null
        };

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, ReportTemplatesFolderName, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), ReportTemplatesFolderName, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "BillingService_API", ReportTemplatesFolderName, fileName)
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void BindTiTemplate(
        Report report,
        BillingDocument document,
        string companyName,
        DocumentTemplate template,
        string documentTitle,
        string? logoPath,
        bool logoAvailable,
        bool hasVat)
    {
        SetText(report, "TiCompanyThai", FormatOrDash(companyName));
        SetText(report, "TiCompanyName", FormatOrDash(companyName));
        SetText(report, "TiCompanyLine1", FormatOrDash(document.CompanyAddressSnapshot));
        SetText(report, "TiCompanyLine2", $"เลขประจำตัวผู้เสียภาษี / Tax ID {FormatOrDash(document.CompanyTaxIdSnapshot)}");
        SetText(report, "TiCompanyLine3", $"สาขา / Branch {FormatOrDash(document.CompanyBranchNoSnapshot)}");
        SetText(report, "TiPage", "Page 1/1");
        SetText(report, "TiHandwrittenNo", FormatOrDash(document.DocumentNo));
        SetText(report, "TiTitleTh", "ใบกำกับภาษี");
        SetText(report, "TiTitleEn", "TAX INVOICE");
        SetText(report, "TiOriginal", "ต้นฉบับ/ORIGINAL");

        SetText(report, "TiBillingLabel", "ที่อยู่ผู้รับเอกสาร | Address of Billing");
        SetText(report, "TiBillingName", FormatOrDash(document.CustomerNameSnapshot));
        SetText(report, "TiBillingAddress", FormatOrDash(document.CustomerAddressSnapshot));
        SetText(report, "TiBillingPostal", "-");
        SetText(report, "TiBillingPhone", "-");
        SetText(report, "TiBillingTaxInvoiceLabel", "ชื่อผู้รับใบกำกับภาษี | Tax Invoice issued for");
        SetText(report, "TiBillingTaxInvoiceName", FormatOrDash(document.CustomerNameSnapshot));
        SetText(report, "TiBillingTaxInvoiceAddress", FormatOrDash(document.CustomerAddressSnapshot));

        SetText(report, "TiInvoiceNoLabel", "เลขที่เอกสาร | Invoice No.");
        SetText(report, "TiInvoiceNoValue", FormatOrDash(document.DocumentNo));
        SetText(report, "TiDateLabel", "วันที่ | Date");
        SetText(report, "TiDateValue", FormatDateOrDash(document.IssueDate));
        SetText(report, "TiTermLabel", "เงื่อนไขการชำระเงิน | Term of Payment");
        SetText(report, "TiTermValue", "0 วัน");
        SetText(report, "TiRefLabel", "อ้างถึงใบแจ้งหนี้เลขที่ | Ref. Invoice No.");
        SetText(report, "TiRefValue", FormatOrDash(document.ReferenceDocumentNoSnapshot));

        SetText(report, "TiPurchaseLabel", "เลขที่ใบสั่งซื้อ / สัญญา\nPurchase Order / Contract No.");
        SetText(report, "TiQuotationLabel", "เลขที่ใบเสนอราคา\nQuotation No.");
        SetText(report, "TiSalesLabel", "พนักงานขาย\nSales Person");
        SetText(report, "TiProjectLabel", "โครงการ / Project");
        SetText(report, "TiDepartmentLabel", "แผนก / Department");

        SetText(report, "HeaderLine1", "ลำดับที่\nItem");
        SetText(report, "HeaderLine2", "รายการ\nDescription");
        SetText(report, "HeaderLine3", "จำนวน\nQuantity");
        SetText(report, "HeaderLine4", "ราคา / หน่วย\nUnit Price");
        SetText(report, "HeaderLine5", "ส่วนลด\nDiscount");
        SetText(report, "HeaderLine6", "จำนวนเงิน (บาท)\nAmount");

        SetText(report, "TiWordsLabel", "จำนวนเงิน (ตัวอักษร)");
        SetText(report, "TiWordsValue", FormatThaiBahtText(document.GrandTotal));
        SetText(report, "TiRemarkLabel", "หมายเหตุ");
        SetText(report, "TiRemarkText", FormatOrDash(document.Remark));
        SetText(report, "TiReceivedTh", "ข้าพเจ้าได้รับสินค้าตามรายการข้างต้นโดยถูกต้องและเรียบร้อย");
        SetText(report, "TiReceivedEn", "Received the above mentioned goods order and condition.");
        SetText(report, "TiForLabel", "ในนาม :");
        SetText(report, "TiForValue", FormatOrDash(companyName));
        SetText(report, "TiForLabelEn", "For :");
        SetText(report, "TiForValueEn", FormatOrDash(companyName));
        SetText(report, "TiReceiverLabel", "ผู้รับ\nReceiver");
        SetText(report, "TiDeliveryLabel", "ผู้ส่ง\nDelivery");
        SetText(report, "TiAuthorizedLabel", "ผู้มีอำนาจลงนาม\nAuthorized Signature");
        SetText(report, "TiDateReceiver", "วันที่ / Date __________________");
        SetText(report, "TiDateDelivery", "วันที่ / Date __________________");
        SetText(report, "TiDateAuthorized", "วันที่ / Date __________________");
        SetText(report, "TiRemarkTh", "หมายเหตุ : โปรดสั่งจ่ายเช็คขีดคร่อมในนาม บริษัท เอเอ็มอาร์ เอเซีย จำกัด (มหาชน)");
        SetText(report, "TiRemarkEn", "Remark  Please pay by cross cheque to AMR ASIA PUBLIC COMPANY LIMITED A/C. Payee Only.");

        ApplyTiTemplateFonts(report);
        AddTiTemplateLogo(report, logoPath, logoAvailable);
    }

    private static void BindRcTemplate(
        Report report,
        BillingDocument document,
        string companyName,
        DocumentTemplate template,
        string? logoPath,
        bool logoAvailable,
        bool hasVat)
    {
        // RC template renders from static content in RC.frx.
        // Data binding will be wired up once object names are finalized.
        AddTemplateLogo(report, logoPath, logoAvailable);
    }

    private static void BindTaxInvoiceTemplate(
        Report report,
        BillingDocument document,
        string companyName,
        DocumentTemplate template,
        string documentTitle,
        string? logoPath,
        bool logoAvailable,
        bool hasVat)
    {
        var headerLines = BuildCompanyHeaderLines(document, companyName, template);

        ClearPlaceholder(report, "HeaderLogo");
        SetText(report, "HeaderCompany", headerLines.FirstOrDefault() ?? FormatOrDash(companyName));
        SetText(report, "HeaderCompany2", headerLines.Count > 1 ? string.Join(Environment.NewLine, headerLines.Skip(1)) : string.Empty);
        SetText(report, "HeaderDocNo", $"เลขที่เอกสาร {FormatOrDash(document.DocumentNo)}");
        SetText(report, "HeaderPage", "Page 1/1");
        SetText(report, "HeaderTitleTh", documentTitle);
        var documentType = (document.DocumentType ?? string.Empty).Trim().ToUpperInvariant();
        var titleEn = documentType switch
        {
            "RC" => hasVat ? "Receipt/Tax Invoice" : "Receipt",
            "CN" => "Credit Note",
            "DN" => "Debit Note",
            _ => "Tax Invoice"
        };
        SetText(report, "HeaderTitleEn", titleEn);
        SetText(report, "HeaderOriginal", "ต้นฉบับ/ORIGINAL");

        SetText(report, "BillingLabel", "ชื่อลูกค้า\nName");
        SetText(report, "BillingName", FormatOrDash(document.CustomerNameSnapshot));
        SetText(report, "BillingAddress", FormatOrDash(document.CustomerAddressSnapshot));
        SetText(report, "BillingPostal", $"เลขบัตรประชาชน : {FormatOrDash(document.CustomerTaxIdSnapshot)}");
        SetText(report, "BillingPhone", "โทรศัพท์\nTelephone");

        SetText(report, "InfoDocNoLabel", "เลขที่เอกสาร\nDocument No.");
        SetText(report, "InfoDocNoValue", FormatOrDash(document.DocumentNo));
        SetText(report, "InfoIssueDateLabel", "วันที่ออก\nIssue Date");
        SetText(report, "InfoIssueDateValue", FormatDateOrDash(document.IssueDate));
        SetText(report, "InfoTermLabel", "เงื่อนไขการชำระเงิน\nTerm of Payment");
        SetText(report, "InfoTermValue", "0 วัน");
        SetText(report, "InfoRefLabel", "อ้างถึงใบแจ้งหนี้เลขที่\nRef. Invoice No.");
        SetText(report, "InfoRefValue", FormatOrDash(document.ReferenceDocumentNoSnapshot));

        SetText(report, "PurchaseTh", "เลขที่ใบสั่งซื้อ / สัญญา");
        SetText(report, "PurchaseEn", "Purchase Order / Contract No.");
        SetText(report, "QuotationTh", "เลขที่ใบเสนอราคา");
        SetText(report, "QuotationEn", "Quotation No.");
        SetText(report, "SalesTh", "พนักงานขาย");
        SetText(report, "SalesEn", "Sales Person");
        SetText(report, "ProjectLabel", "โครงการ / Project");
        SetText(report, "DepartmentLabel", "แผนก / Department");

        SetText(report, "Col1", "ลำดับที่\r\nItem");
        SetText(report, "Col2", "รายการ\r\nDescription");
        SetText(report, "Col3", "จำนวน\r\nQuantity");
        SetText(report, "Col4", "ราคา / หน่วย\r\nUnit Price");
        SetText(report, "Col5", "ส่วนลด\r\nDiscount");
        SetText(report, "Col6", "จำนวนเงิน (บาท)\r\nAmount");

        SetText(report, "RemarkLabel", "หมายเหตุ");
        SetText(report, "RemarkValue", FormatOrDash(document.Remark));
        SetText(report, "TotalLabel1", "รวมราคา\nAmount");
        SetText(report, "TotalValue1", FormatMoneyOrDash(document.SubTotal));
        SetText(report, "TotalLabel2", "ส่วนลด\nDiscount");
        var discount = document.DocumentItems.Sum(item => item.DiscountAmount ?? 0m);

        SetText(report, "TotalValue2", FormatMoneyOrDash(discount));
        SetText(report, "TotalLabel3", "มูลค่าสินค้า\nNet Amount");
        SetText(report, "TotalValue3", FormatMoneyOrDash(document.SubTotal - discount));
        SetText(report, "TotalLabel4", "ภาษีมูลค่าเพิ่ม\nVAT 7.00%");
        SetText(report, "TotalValue4", hasVat ? FormatMoneyOrDash(document.VatAmount) : "-");
        SetText(report, "TotalLabel5", "ยอดเงินสุทธิ\nNet Amount");
        SetText(report, "TotalValue5", FormatMoneyOrDash(document.GrandTotal));

        SetText(report, "WordsLabel", "จำนวนเงิน (ตัวอักษร)");
        SetText(report, "WordsValue", FormatThaiBahtText(document.GrandTotal));
        SetText(report, "NarrativeTh", "ข้าพเจ้าได้รับสินค้าตามรายการข้างต้นโดยถูกต้องและเรียบร้อย");
        SetText(report, "NarrativeFor", "ในนาม :");
        SetText(report, "NarrativeCompany", FormatOrDash(GetPrimaryHeaderLine(template, companyName)));
        SetText(report, "NarrativeEn", "Received the above mentioned goods order and condition.");
        SetText(report, "NarrativeForEn", "For :");
        SetText(report, "NarrativeCompanyEn", FormatOrDash(document.CompanyNameSnapshot));

        SetText(report, "SignCap1", "ผู้รับ\nReceiver");
        SetText(report, "SignCap2", "ผู้ส่ง\nDelivery");
        SetText(report, "SignCap3", "ผู้อนุมัติ\nAuthorized Signature");

        SetText(report, "SignDate1", "วันที่ / Date");
        SetText(report, "SignDate2", "วันที่ / Date");
        SetText(report, "SignDate3", "วันที่ / Date");

        SetText(report, "FooterTh", $"หมายเหตุ  {FormatOrDash(template.FooterText)}");
        SetText(report, "FooterEn", "Remark  Please pay by cross cheque to AMR ASIA PUBLIC COMPANY LIMITED A/C. Payee Only,Interest at 1.25% per month will be charged on overdue account.");

        ApplyTaxInvoiceTemplateFonts(report);
        AddTemplateLogo(report, logoPath, logoAvailable);
    }

    private static void AddTiTemplateLogo(Report report, string? logoPath, bool logoAvailable)
    {
        if (!logoAvailable || string.IsNullOrWhiteSpace(logoPath))
        {
            return;
        }

        try
        {
            if (report.FindObject("TiLogoFallback") is TextObject logoFallback && logoFallback.Parent is BandBase parentBand)
            {
                parentBand.Objects.Add(CreatePicture(
                    "TiTemplateLogo",
                    logoFallback.Bounds.Left / Units.Millimeters,
                    logoFallback.Bounds.Top / Units.Millimeters,
                    logoFallback.Bounds.Width / Units.Millimeters,
                    logoFallback.Bounds.Height / Units.Millimeters,
                    logoPath));
                logoFallback.Visible = false;
            }
        }
        catch
        {
            // Keep the template renderable even when the logo cannot be loaded.
        }
    }

    private static void AddTemplateLogo(Report report, string? logoPath, bool logoAvailable)
    {
        if (!logoAvailable || string.IsNullOrWhiteSpace(logoPath))
        {
            return;
        }

        try
        {
            if (report.FindObject("HeaderLogo") is TextObject headerLogo && headerLogo.Parent is BandBase parentBand)
            {
                parentBand.Objects.Add(CreatePicture(
                    "TemplateLogo",
                    headerLogo.Bounds.Left / Units.Millimeters,
                    headerLogo.Bounds.Top / Units.Millimeters,
                    headerLogo.Bounds.Width / Units.Millimeters,
                    headerLogo.Bounds.Height / Units.Millimeters,
                    logoPath));
                ClearPlaceholder(report, "HeaderLogo");
                return;
            }
        }
        catch
        {
            // Keep the template renderable even when the logo cannot be loaded.
        }
    }

    private static void ApplyTaxInvoiceTemplateFonts(Report report)
    {
        SetFont(report, "HeaderCompany", 13, true);
        SetFont(report, "HeaderCompany2", 8);
        SetFont(report, "HeaderDocNo", 9, true);
        SetFont(report, "HeaderPage", 8);
        SetFont(report, "HeaderTitleTh", 12, true);
        SetFont(report, "HeaderTitleEn", 12, true);
        SetFont(report, "HeaderOriginal", 8);

        SetFont(report, "BillingLabel", 12, true);
        SetFont(report, "BillingName", 12);
        SetFont(report, "BillingAddress", 12);
        SetFont(report, "BillingPostal", 12);
        SetFont(report, "BillingPhone", 12, true);

        SetFont(report, "InfoDocNoLabel", 10, true);
        SetFont(report, "InfoDocNoValue", 10);
        SetFont(report, "InfoIssueDateLabel", 10, true);
        SetFont(report, "InfoIssueDateValue", 10);
        SetFont(report, "InfoTermLabel", 10, true);
        SetFont(report, "InfoTermValue", 10);
        SetFont(report, "InfoRefLabel", 10, true);
        SetFont(report, "InfoRefValue", 10);

        SetFont(report, "PurchaseTh", 10, true);
        SetFont(report, "PurchaseEn", 10);
        SetFont(report, "QuotationTh", 10, true);
        SetFont(report, "QuotationEn", 10);
        SetFont(report, "SalesTh", 10, true);
        SetFont(report, "SalesEn", 10);
        SetFont(report, "ProjectLabel", 10, true);
        SetFont(report, "DepartmentLabel", 10, true);

        SetFont(report, "Col1", 10, true);
        SetFont(report, "Col2", 10, true);
        SetFont(report, "Col3", 10, true);
        SetFont(report, "Col4", 10, true);
        SetFont(report, "Col5", 10, true);
        SetFont(report, "Col6", 10, true);

        SetFont(report, "ItemNo", 10);
        SetFont(report, "ItemDesc", 10);
        SetFont(report, "ItemQty", 10);
        SetFont(report, "ItemUnit", 10);
        SetFont(report, "ItemDiscount", 10);
        SetFont(report, "ItemAmount", 10);

        SetFont(report, "RemarkLabel", 12, true);
        SetFont(report, "RemarkValue", 12);
        SetFont(report, "TotalLabel1", 10, true);
        SetFont(report, "TotalValue1", 10);
        SetFont(report, "TotalLabel2", 10, true);
        SetFont(report, "TotalValue2", 10);
        SetFont(report, "TotalLabel3", 10, true);
        SetFont(report, "TotalValue3", 10);
        SetFont(report, "TotalLabel4", 10, true);
        SetFont(report, "TotalValue4", 10);
        SetFont(report, "TotalLabel5", 10, true);
        SetFont(report, "TotalValue5", 10, true);

        SetFont(report, "WordsLabel", 12, true);
        SetFont(report, "WordsValue", 12);
        SetFont(report, "NarrativeTh", 12);
        SetFont(report, "NarrativeFor", 12);
        SetFont(report, "NarrativeCompany", 12);
        SetFont(report, "NarrativeEn", 12);
        SetFont(report, "NarrativeForEn", 12);
        SetFont(report, "NarrativeCompanyEn", 12);

        SetFont(report, "SignCap1", 12);
        SetFont(report, "SignCap2", 12);
        SetFont(report, "SignCap3", 12);
        SetFont(report, "SignDate1", 12);
        SetFont(report, "SignDate2", 12);
        SetFont(report, "SignDate3", 12);

        SetFont(report, "FooterTh", 9);
        SetFont(report, "FooterEn", 9);
    }

    private static void ApplyTiTemplateFonts(Report report)
    {
        SetFont(report, "TiCompanyThai", 11, true);
        SetFont(report, "TiCompanyName", 10);
        SetFont(report, "TiCompanyLine1", 8.5f);
        SetFont(report, "TiCompanyLine2", 8.5f);
        SetFont(report, "TiCompanyLine3", 8.5f);
        SetFont(report, "TiPage", 8.5f);
        SetFont(report, "TiHandwrittenNo", 10);
        SetFont(report, "TiTitleTh", 12, true);
        SetFont(report, "TiTitleEn", 12, true);
        SetFont(report, "TiOriginal", 8.5f);
        SetFont(report, "TiBillingLabel", 9.5f, true);
        SetFont(report, "TiBillingName", 9.5f);
        SetFont(report, "TiBillingAddress", 9.5f);
        SetFont(report, "TiBillingPostal", 9.5f);
        SetFont(report, "TiBillingPhone", 9.5f);
        SetFont(report, "TiBillingTaxInvoiceLabel", 9.5f, true);
        SetFont(report, "TiBillingTaxInvoiceName", 9.5f);
        SetFont(report, "TiBillingTaxInvoiceAddress", 9.5f);
        SetFont(report, "TiInvoiceNoLabel", 9f, true);
        SetFont(report, "TiInvoiceNoValue", 9f);
        SetFont(report, "TiDateLabel", 9f, true);
        SetFont(report, "TiDateValue", 9f);
        SetFont(report, "TiTermLabel", 9f, true);
        SetFont(report, "TiTermValue", 9f);
        SetFont(report, "TiRefLabel", 9f, true);
        SetFont(report, "TiRefValue", 9f);
        SetFont(report, "TiPurchaseLabel", 8.5f, true);
        SetFont(report, "TiQuotationLabel", 8.5f, true);
        SetFont(report, "TiSalesLabel", 8.5f, true);
        SetFont(report, "TiProjectLabel", 8.5f, true);
        SetFont(report, "TiDepartmentLabel", 8.5f, true);
        SetFont(report, "HeaderLine1", 8.5f, true);
        SetFont(report, "HeaderLine2", 8.5f, true);
        SetFont(report, "HeaderLine3", 8.5f, true);
        SetFont(report, "HeaderLine4", 8.5f, true);
        SetFont(report, "HeaderLine5", 8.5f, true);
        SetFont(report, "HeaderLine6", 8.5f, true);
        SetFont(report, "LineNo", 8.5f);
        SetFont(report, "Description", 8.5f);
        SetFont(report, "Quantity", 8.5f);
        SetFont(report, "UnitPrice", 8.5f);
        SetFont(report, "Discount", 8.5f);
        SetFont(report, "Amount", 8.5f);
        SetFont(report, "TiWordsLabel", 8.5f, true);
        SetFont(report, "TiWordsValue", 8.5f);
        SetFont(report, "TiRemarkLabel", 8.5f, true);
        SetFont(report, "TiRemarkText", 8.5f);
        SetFont(report, "TiReceivedTh", 8.5f);
        SetFont(report, "TiReceivedEn", 8.5f);
        SetFont(report, "TiForLabel", 8.5f);
        SetFont(report, "TiForValue", 8.5f);
        SetFont(report, "TiForLabelEn", 8.5f);
        SetFont(report, "TiForValueEn", 8.5f);
        SetFont(report, "TiReceiverLabel", 8.5f, true);
        SetFont(report, "TiDeliveryLabel", 8.5f, true);
        SetFont(report, "TiAuthorizedLabel", 8.5f, true);
        SetFont(report, "TiDateReceiver", 8.5f);
        SetFont(report, "TiDateDelivery", 8.5f);
        SetFont(report, "TiDateAuthorized", 8.5f);
        SetFont(report, "TiRemarkTh", 8f);
        SetFont(report, "TiRemarkEn", 8f);
    }

    private static void SetText(Report report, string objectName, string text)
    {
        if (report.FindObject(objectName) is TextObject textObject)
        {
            textObject.Text = text;
        }
    }

    private static void SetFont(Report report, string objectName, float size, bool bold = false)
    {
        if (report.FindObject(objectName) is TextObject textObject)
        {
            textObject.Font = new Font(ThaiFontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private static void ClearPlaceholder(Report report, string objectName)
    {
        if (report.FindObject(objectName) is TextObject textObject)
        {
            textObject.Text = string.Empty;
            textObject.Border.Lines = BorderLines.None;
        }
    }

    private static void BuildPageHeader(
        ReportPage page,
        BillingDocument document,
        string companyName,
        string documentTitle,
        DocumentTemplate template,
        string? logoPath,
        bool logoAvailable,
        bool isReceipt,
        bool isTaxInvoice,
        bool isCreditNote,
        bool hasVat)
    {
        if (isReceipt)
        {
            page.PageHeader = new PageHeaderBand
            {
                Name = "PageHeader",
                Height = 0
            };
            return;
        }

        if (isTaxInvoice)
        {
            var tiHeader = new PageHeaderBand
            {
                Name = "PageHeader",
                Height = 0
            };
            page.PageHeader = tiHeader;
            return;
        }

        var header = new PageHeaderBand
        {
            Name = "PageHeader",
            Height = Units.Millimeters * 28
        };
        page.PageHeader = header;

        if (logoAvailable && logoPath is not null)
        {
            try
            {
                header.Objects.Add(CreatePicture("Logo", 0, 0, 18, 18, logoPath));
            }
            catch (Exception)
            {
                header.Objects.Add(CreateText("LogoFallback", "AMR ASIA", 0, 1, 18, 8, 8, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
            }
        }
        else
        {
            header.Objects.Add(CreateText("LogoFallback", "AMR ASIA", 0, 1, 18, 8, 8, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        }

        var headerLines = BuildCompanyHeaderLines(document, companyName, template);
        var leftX = 22d;

        header.Objects.Add(CreateText("CompanyName", headerLines[0], leftX, 1, 120, 5, isReceipt ? 14 : 12, bold: true));
        for (var index = 1; index < headerLines.Count; index++)
        {
            header.Objects.Add(CreateText($"CompanyLine{index}", headerLines[index], leftX, 1 + (index * 3.2d), 150, 3.2d, isReceipt ? 12 : 8));
        }

        if (isReceipt)
        {
            header.Objects.Add(CreateText("DocNo", FormatOrDash(document.DocumentNo), 148, 0, 54, 4.5, 10, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocPage", "Page 1 of 1", 154, 4, 48, 4, 8, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocTitleTh", hasVat ? "ใบเสร็จรับเงิน/ใบกำกับภาษี" : "ใบเสร็จรับเงิน", 122, 8, 74, 5, 13, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocTitleEn", hasVat ? "Receipt/Tax Invoice" : "Receipt/Tax Invoice", 122, 13, 74, 5, 13, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocOriginal", "ต้นฉบับ/ORIGINAL", 122, 18, 74, 4, 9, align: HorzAlign.Right));
        }
        else if (isTaxInvoice)
        {
            header.Objects.Add(CreateText("DocNo", $"เลขที่เอกสาร {document.DocumentNo}", 154, 0, 48, 4.5, 10, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocPage", "Page 1 of 1", 154, 4, 48, 4, 8, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocTitleTh", "ใบกำกับภาษี", 138, 8, 64, 5, 13, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocTitleEn", "TAX INVOICE", 138, 13, 64, 5, 13, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocOriginal", "ต้นฉบับ/ORIGINAL", 138, 18, 64, 4, 9, align: HorzAlign.Right));
        }
        else
        {
            header.Objects.Add(CreateText("DocNo", $"เลขที่เอกสาร {document.DocumentNo}", 154, 0, 48, 4.5, 10, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocPage", "Page 1 of 1", 154, 4, 48, 4, 8, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocTitleTh", "ใบลดหนี้", 138, 8, 64, 5, 13, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocTitleEn", "CREDIT NOTE", 138, 13, 64, 5, 13, bold: true, align: HorzAlign.Right));
            header.Objects.Add(CreateText("DocOriginal", "ต้นฉบับ/ORIGINAL", 138, 18, 64, 4, 9, align: HorzAlign.Right));
        }

        header.Objects.Add(CreateLine("HeaderRule", 0, 22.5, 194, 0.5));
    }

    private static void BuildReceiptTitle(ReportPage page, BillingDocument document, bool hasVat)
    {
        var title = new ReportTitleBand
        {
            Name = "ReceiptTitle",
            Height = Units.Millimeters * 70
        };
        page.ReportTitle = title;

        var headerLines = BuildCompanyHeaderLines(document, document.CompanyNameSnapshot ?? string.Empty, new DocumentTemplate
        {
            TemplateName = "Receipt Header"
        });

        title.Objects.Add(CreateText("HeaderLogo", "AMR\r\nASIA", 0, 0, 18, 18, 8, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        title.Objects.Add(CreateText("HeaderCompany", headerLines.FirstOrDefault() ?? FormatOrDash(document.CompanyNameSnapshot), 22, 1, 120, 5, 14, bold: true));
        for (var index = 1; index < headerLines.Count; index++)
        {
            title.Objects.Add(CreateText($"HeaderLine{index}", headerLines[index], 22, 4 + (index * 3), 150, 3, 8));
        }

        title.Objects.Add(CreateText("HeaderDocNo", $"เลขที่เอกสาร {FormatOrDash(document.DocumentNo)}", 150, 0, 52, 4.5, 10, bold: true, align: HorzAlign.Right));
        title.Objects.Add(CreateText("HeaderPage", "Page 1/1", 150, 4, 52, 4, 8, align: HorzAlign.Right));
        title.Objects.Add(CreateText("HeaderTitleTh", hasVat ? "ใบเสร็จรับเงิน/ใบกำกับภาษี" : "ใบเสร็จรับเงิน", 128, 8, 74, 5, 13, bold: true, align: HorzAlign.Right));
        title.Objects.Add(CreateText("HeaderTitleEn", hasVat ? "Receipt/Tax Invoice" : "Receipt", 128, 13, 74, 5, 13, bold: true, align: HorzAlign.Right));
        title.Objects.Add(CreateText("HeaderOriginal", "ต้นฉบับ/ORIGINAL", 128, 18, 74, 4, 9, align: HorzAlign.Right));
        title.Objects.Add(CreateLine("HeaderRule", 0, 22.5, 194, 0.5));

        title.Objects.Add(CreateText("AddressLabel", "ที่อยู่ผู้รับเอกสาร | Address of Billing", 0, 25, 120, 4, 12, bold: true));
        title.Objects.Add(CreateText("AddressName", FormatOrDash(document.CustomerNameSnapshot), 0, 29, 120, 4, 12));
        title.Objects.Add(CreateText("AddressLine", FormatOrDash(document.CustomerAddressSnapshot), 0, 33, 120, 4, 12));
        title.Objects.Add(CreateText("AddressPostal", FormatOrDash(document.CustomerPostalCodeSnapshot), 0, 37, 120, 4, 12));

        title.Objects.Add(CreateText("TaxInvoiceLabel", "ชื่อผู้รับใบกำกับภาษี | Tax Invoice issued for", 0, 42, 120, 4, 12, bold: true));
        title.Objects.Add(CreateText("TaxInvoiceName", FormatOrDash(document.CustomerNameSnapshot), 0, 46, 120, 4, 12));
        title.Objects.Add(CreateText("TaxInvoiceAddress", FormatOrDash(document.CustomerAddressSnapshot), 0, 50, 120, 4, 12));
        title.Objects.Add(CreateText("TaxInvoicePostal", FormatOrDash(document.CustomerPostalCodeSnapshot), 0, 54, 120, 4, 12));

        title.Objects.Add(CreateText("SourceNo", $"เลขที่สัญญาผู้เช่าซื้อรถยนต์ {FormatOrDash(document.SourceNo)}", 0, 58, 150, 4, 12));
        title.Objects.Add(CreateText("SourceNoTag", "ต้นฉบับ", 175, 58, 18, 4, 12, bold: true, align: HorzAlign.Right));
        title.Objects.Add(CreateLine("ReceiptRule", 0, 62.5, 194, 0.5));
        AddTiTableHeader(title, 64, 8.5);
    }

    private static void BuildTaxInvoiceTitle(ReportPage page, BillingDocument document, string companyName)
    {
        var title = new ReportTitleBand
        {
            Name = "TaxInvoiceTitle",
            Height = Units.Millimeters * 54
        };
        page.ReportTitle = title;

        title.Objects.Add(CreateText("TiLogoFallback", "AMR\r\nASIA", 0, 24, 18, 12, 8, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        title.Objects.Add(CreateText("TiBillingLabel", "ที่อยู่ผู้รับเอกสาร | Address of Billing", 0, 0, 124, 4, 9.5, bold: true));
        title.Objects.Add(CreateText("TiBillingName", FormatOrDash(document.CustomerNameSnapshot), 0, 4, 124, 4, 9.5));
        title.Objects.Add(CreateText("TiBillingAddress", FormatOrDash(document.CustomerAddressSnapshot), 0, 8, 124, 4, 9.5));
        title.Objects.Add(CreateText("TiBillingTaxInvoiceLabel", "ชื่อผู้รับใบกำกับภาษี | Tax Invoice issued for", 0, 14, 124, 4, 9.5, bold: true));
        title.Objects.Add(CreateText("TiBillingTaxInvoiceName", FormatOrDash(document.CustomerNameSnapshot), 0, 18, 124, 4, 9.5));
        title.Objects.Add(CreateText("TiBillingTaxInvoiceAddress", FormatOrDash(document.CustomerAddressSnapshot), 0, 22, 124, 4, 9.5));
        title.Objects.Add(CreateText("TiCompanyName", FormatOrDash(companyName), 22, 24, 110, 5, 12, bold: true));
        title.Objects.Add(CreateText("TiCompanyLine1", FormatOrDash(document.CompanyAddressSnapshot), 22, 28, 110, 4, 8.5));
        title.Objects.Add(CreateText("TiCompanyLine2", $"เลขประจำตัวผู้เสียภาษี {FormatOrDash(document.CompanyTaxIdSnapshot)}", 22, 32, 110, 4, 8.5));
        title.Objects.Add(CreateText("TiHandwrittenNo", FormatOrDash(document.DocumentNo), 132, 24, 62, 4, 8.5, align: HorzAlign.Right));
        title.Objects.Add(CreateText("TiTitleTh", "ใบเสร็จรับเงิน/ใบกำกับภาษี | Receipt/Tax Invoice", 114, 28, 80, 6, 12.5, bold: true, align: HorzAlign.Right));
        title.Objects.Add(CreateText("TiOriginal", "ต้นฉบับ / ORIGINAL", 132, 34, 62, 4, 8.5, align: HorzAlign.Right));
        title.Objects.Add(CreateText("TiDocNo", $"เลขที่เอกสาร {FormatOrDash(document.DocumentNo)}", 126, 24, 68, 4, 8.5, align: HorzAlign.Right));
        title.Objects.Add(CreateText("TiTaxId", $"เลขประจำตัวผู้เสียภาษี {FormatOrDash(document.CompanyTaxIdSnapshot)}", 126, 28, 68, 4, 8.5, align: HorzAlign.Right));
        title.Objects.Add(CreateText("TiPage", "Page 1 of 1", 126, 32, 68, 4, 8.5, align: HorzAlign.Right));
        title.Objects.Add(CreateText("TiBranch", $"สาขา {FormatOrDash(document.CompanyBranchNoSnapshot)}", 126, 36, 68, 4, 8.5, align: HorzAlign.Right));
        title.Objects.Add(CreateLine("TiRule", 0, 40, 194, 0.5));
        AddTiTableHeader(title, 42, 8.5);
    }

    private static void BuildCreditNoteTitle(ReportPage page, BillingDocument document)
    {
        var title = new ReportTitleBand
        {
            Name = "CreditNoteTitle",
            Height = Units.Millimeters * 42
        };
        page.ReportTitle = title;

        title.Objects.Add(CreateText("CnTitle", "ใบลดหนี้ / CREDIT NOTE", 0, 2, 194, 5, 14, bold: true, align: HorzAlign.Right));
        title.Objects.Add(CreateText("CnRef", $"อ้างถึงใบแจ้งหนี้เลขที่ {FormatOrDash(document.ReferenceDocumentNoSnapshot)}", 0, 7, 194, 4, 9, align: HorzAlign.Right));
        title.Objects.Add(CreateText("CnOriginal", $"เลขที่เอกสารต้นฉบับ {FormatOrDash(document.OriginalDocumentNoSnapshot)}", 0, 11, 194, 4, 9, align: HorzAlign.Right));
        title.Objects.Add(CreateText("CnReason", $"เหตุผล: {FormatOrDash(document.CreditNoteReasonSnapshot)}", 0, 15, 194, 4, 9, align: HorzAlign.Right));
        title.Objects.Add(CreateText("CnCustomer", FormatOrDash(document.CustomerNameSnapshot), 0, 20, 120, 4, 9, bold: true));
        title.Objects.Add(CreateText("CnDocNo", FormatOrDash(document.DocumentNo), 140, 20, 54, 4, 9, bold: true, align: HorzAlign.Right));
        AddTiTableHeader(title, 32, 8);
    }

    private static void AddTiTableHeader(BandBase band, double topMm, double fontSize)
    {
        band.Objects.Add(CreateText("HeaderLine1", "เลขที่สัญญา\nContract No.", 0, topMm, 28, 8, fontSize, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        band.Objects.Add(CreateText("HeaderLine2", "รายละเอียด\nDescription", 28, topMm, 91, 8, fontSize, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        band.Objects.Add(CreateText("HeaderLine3", "กำหนดชำระเงิน\nDue Date", 119, topMm, 21, 8, fontSize, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        band.Objects.Add(CreateText("HeaderLine4", "จำนวนเงิน\nAmount", 140, topMm, 18, 8, fontSize, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        band.Objects.Add(CreateText("HeaderLine5", "ภาษีมูลค่าเพิ่ม\nVAT", 158, topMm, 16, 8, fontSize, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        band.Objects.Add(CreateText("HeaderLine6", "จำนวนเงินรวมภาษี\nTotal Amount", 174, topMm, 20, 8, fontSize, bold: true, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
    }

    private static void BuildReceiptItems(Report report, ReportPage page, bool hasVat)
    {
        var data = new DataBand
        {
            Name = "ReceiptItems",
            Height = Units.Millimeters * 13,
            DataSource = report.GetDataSource("Items"),
            KeepTogether = false
        };
        page.Bands.Add(data);

        data.Objects.Add(CreateText("LineNo", "[Items.LineNo]", 0, 0, 16, 13, 12, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Description", "[Items.Description]", 16, 0, 97, 13, 12, wordWrap: true, border: true));
        data.Objects.Add(CreateText("Quantity", "[Items.Quantity]", 113, 0, 22, 13, 12, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("UnitPrice", "[Items.UnitPrice]", 135, 0, 31, 13, 12, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Discount", "[Items.Discount]", 166, 0, 12, 13, 12, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Total", hasVat ? "[Items.Total]" : "[Items.Amount]", 178, 0, 16, 13, 12, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
    }

    private static void BuildTaxInvoiceItems(Report report, ReportPage page)
    {
        var data = new DataBand
        {
            Name = "TaxInvoiceItems",
            Height = Units.Millimeters * 13,
            DataSource = report.GetDataSource("Items"),
            KeepTogether = false
        };
        page.Bands.Add(data);

        data.Objects.Add(CreateText("ContractNo", "[Items.ContractNo]", 0, 0, 28, 13, 8.5, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Description", "[Items.Description]", 28, 0, 91, 13, 8.5, wordWrap: true, border: true));
        data.Objects.Add(CreateText("DueDate", "[Items.DueDate]", 119, 0, 21, 13, 8.5, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Amount", "[Items.Amount]", 140, 0, 18, 13, 8.5, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Vat", "[Items.Vat]", 158, 0, 16, 13, 8.5, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("TotalAmount", "[Items.TotalAmount]", 174, 0, 20, 13, 8.5, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
    }

    private static void BuildCreditNoteItems(Report report, ReportPage page)
    {
        var data = new DataBand
        {
            Name = "CreditNoteItems",
            Height = Units.Millimeters * 13,
            DataSource = report.GetDataSource("Items"),
            KeepTogether = false
        };
        page.Bands.Add(data);

        data.Objects.Add(CreateText("LineNo", "[Items.LineNo]", 0, 0, 16, 13, 8, align: HorzAlign.Center, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Description", "[Items.Description]", 16, 0, 97, 13, 8, wordWrap: true, border: true));
        data.Objects.Add(CreateText("Quantity", "[Items.Quantity]", 113, 0, 22, 13, 8, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("UnitPrice", "[Items.UnitPrice]", 135, 0, 31, 13, 8, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Discount", "[Items.Discount]", 166, 0, 12, 13, 8, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
        data.Objects.Add(CreateText("Amount", "[Items.Amount]", 178, 0, 16, 13, 8, align: HorzAlign.Right, valign: VertAlign.Center, border: true));
    }

    private static IEnumerable<PdfItemRow> BuildItemRows(BillingDocument document, bool hasVat)
    {
        return document.DocumentItems
            .OrderBy(item => item.LineNo ?? int.MaxValue)
            .Select((item, index) =>
            {
                var description = item.ItemName;
                if (!string.IsNullOrWhiteSpace(item.ItemRemark))
                {
                    description += Environment.NewLine + item.ItemRemark;
                }

                return new PdfItemRow(
                    ContractNo: FormatOrDash(document.SourceNo),
                    LineNo: (item.LineNo ?? index + 1).ToString(),
                    Description: description,
                    DueDate: FormatDateOrDash(document.DueDate ?? document.IssueDate),
                    Quantity: item.Quantity.ToString("0.00", CultureInfo.InvariantCulture),
                    UnitPrice: item.UnitPrice.ToString("N2", CultureInfo.InvariantCulture),
                    Discount: FormatMoneyOrDash(item.DiscountAmount),
                    Amount: FormatMoneyValue(item.Amount, item.DiscountAmount),
                    Vat: FormatMoneyOrDash(item.VatAmount),
                    Total: hasVat ? FormatMoneyOrDash(item.Amount + item.VatAmount) : FormatMoneyValue(item.Amount, item.DiscountAmount),
                    TotalAmount: FormatMoneyOrDash(item.Amount + item.VatAmount));
            })
            .ToList();
    }

    private static void BuildReceiptSummary(ReportPage page, BillingDocument document, bool hasVat)
    {
        var summary = new ReportSummaryBand
        {
            Name = "ReceiptSummary",
            Height = Units.Millimeters * 92,
            PrintOnBottom = true
        };
        page.ReportSummary = summary;

        summary.Objects.Add(AddBox("ReceiptWordsBox", 0, 0, 194, 9));
        summary.Objects.Add(CreateText("ReceiptWordsLabel", "จำนวนเงิน (ตัวอักษร)", 2, 2, 44, 4, 12, bold: true));
        summary.Objects.Add(CreateText("ReceiptWordsValue", FormatThaiBahtText(document.GrandTotal), 46, 2, 146, 4, 12, bold: true));
        summary.Objects.Add(AddBox("ReceiptTotalBox", 129, 9, 65, 30));
        summary.Objects.Add(CreateText("ReceiptTotalLabel", "รวมราคา", 131, 10, 37, 4, 10, bold: true));
        summary.Objects.Add(CreateText("ReceiptTotalLabelEn", "Amount", 150, 10, 18, 4, 10));
        summary.Objects.Add(CreateText("ReceiptTotalValue", FormatMoneyOrDash(document.SubTotal), 168, 10, 24, 4, 10, align: HorzAlign.Right));
        summary.Objects.Add(CreateText("ReceiptVatLabel", "ภาษีมูลค่าเพิ่ม", 131, 15, 37, 4, 10, bold: true));
        summary.Objects.Add(CreateText("ReceiptVatLabelEn", "VAT", 154, 15, 14, 4, 10));
        summary.Objects.Add(CreateText("ReceiptVatValue", hasVat ? FormatMoneyOrDash(document.VatAmount) : "-", 168, 15, 24, 4, 10, align: HorzAlign.Right));
        summary.Objects.Add(CreateText("ReceiptGrandLabel", "ยอดเงินสุทธิ", 131, 20, 37, 4, 10, bold: true));
        summary.Objects.Add(CreateText("ReceiptGrandLabelEn", "Net Amount", 150, 20, 18, 4, 10));
        summary.Objects.Add(CreateText("ReceiptGrandValue", FormatMoneyOrDash(document.GrandTotal), 168, 20, 24, 4, 10, bold: true, align: HorzAlign.Right));

        summary.Objects.Add(CreateText("ReceiptNarrativeTh", "ข้าพเจ้าได้รับสินค้าตามรายการข้างต้นโดยถูกต้องและเรียบร้อย", 0, 34, 118, 4, 12));
        summary.Objects.Add(CreateText("ReceiptNarrativeIn", "ในนาม :", 118, 34, 20, 4, 12));
        summary.Objects.Add(CreateText("ReceiptNarrativeFor", FormatOrDash(document.CompanyNameSnapshot), 138, 34, 56, 4, 12));
        summary.Objects.Add(CreateText("ReceiptNarrativeEn", "Received the above mentioned goods order and condition.", 0, 39, 118, 4, 12));
        summary.Objects.Add(CreateText("ReceiptNarrativeForEn", "For :", 118, 39, 20, 4, 12));
        summary.Objects.Add(CreateText("ReceiptNarrativeCompany", FormatOrDash(document.CompanyNameSnapshot), 138, 39, 56, 4, 12));

        BuildSignatureBoxes(summary, 0, 48, 194, 18, includeDelivery: true, includeCashier: true);
        summary.Objects.Add(CreateText("ReceiptFooterTh", "หมายเหตุ  โปรดสั่งจ่ายเช็คขีดคร่อมในนาม บริษัท เอเอ็มอาร์ เอเซีย จำกัด (มหาชน) และขีดฆ่า “หรือผู้ถือ” ออก", 0, 68, 194, 4, 9));
        summary.Objects.Add(CreateText("ReceiptFooterEn", "Remark  Please pay by cross cheque to AMR ASIA PUBLIC COMPANY LIMITED A/C. Payee Only,Interest at 1.25% per month will be charged on overdue account.", 0, 72, 194, 4, 9));
    }

    private static void BuildTaxInvoiceSummary(ReportPage page, BillingDocument document, string companyName, DocumentTemplate template)
    {
        var summary = new ReportSummaryBand
        {
            Name = "TaxInvoiceSummary",
            Height = Units.Millimeters * 64,
            PrintOnBottom = true
        };
        page.ReportSummary = summary;

        summary.Objects.Add(AddBox("TiPaymentBox", 0, 0, 194, 10));
        summary.Objects.Add(CreateText("TiPaymentPaidBy", "ชำระโดย | Paid By", 31, 1, 29, 4, 8.5, bold: true, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentDate", "ลงวันที่ | Date", 60, 1, 24, 4, 8.5, bold: true, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentCheque", "เช็คเลขที่ | Cheque No.", 84, 1, 40, 4, 8.5, bold: true, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentBank", "ธนาคาร | Bank", 124, 1, 32, 4, 8.5, bold: true, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentBranch", "สาขา | Branch", 156, 1, 38, 4, 8.5, bold: true, align: HorzAlign.Center));
        summary.Objects.Add(CreateLine("TiPaymentSplit1", 30, 0, 0.5, 10));
        summary.Objects.Add(CreateLine("TiPaymentSplit2", 59, 0, 0.5, 10));
        summary.Objects.Add(CreateLine("TiPaymentSplit3", 83, 0, 0.5, 10));
        summary.Objects.Add(CreateLine("TiPaymentSplit4", 123, 0, 0.5, 10));
        summary.Objects.Add(CreateLine("TiPaymentSplit5", 155, 0, 0.5, 10));

        summary.Objects.Add(CreateText("TiPaymentPaidByValue", FormatOrDash(document.PaymentMethodSnapshot), 31, 5, 29, 4, 8.5, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentDateValue", FormatDateOrDash(document.IssueDate), 60, 5, 24, 4, 8.5, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentChequeValue", "-", 84, 5, 40, 4, 8.5, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentBankValue", "-", 124, 5, 32, 4, 8.5, align: HorzAlign.Center));
        summary.Objects.Add(CreateText("TiPaymentBranchValue", "-", 156, 5, 38, 4, 8.5, align: HorzAlign.Center));

        summary.Objects.Add(CreateText("TiCashierLabel", "ผู้รับเงิน | Cashier", 0, 13, 40, 4, 9.5, bold: true));
        summary.Objects.Add(CreateText("TiAuthorizedLabel", "ผู้มีอำนาจลงนาม | Authorized Signature", 88, 13, 70, 4, 9.5, bold: true));
        summary.Objects.Add(CreateLine("TiCashierLine", 41, 15, 41, 0.5));
        summary.Objects.Add(CreateLine("TiAuthorizedLine", 160, 15, 34, 0.5));
        summary.Objects.Add(CreateText("TiCashierDate", "วันที่ / Date __________________", 0, 19, 70, 4, 8.5));
        summary.Objects.Add(CreateText("TiAuthorizedDate", "วันที่ / Date __________________", 126, 19, 68, 4, 8.5));

        summary.Objects.Add(AddBox("TiTotalBox", 128, 27, 66, 16));
        summary.Objects.Add(CreateText("TiTotalAmountLabel", "รวมราคา  Amount", 131, 29, 34, 4, 8.5));
        summary.Objects.Add(CreateText("TiTotalAmountValue", FormatMoneyOrDash(document.SubTotal), 163, 29, 28, 4, 8.5, align: HorzAlign.Right));
        summary.Objects.Add(CreateText("TiTotalVatLabel", "ภาษีมูลค่าเพิ่ม  VAT", 131, 33, 34, 4, 8.5));
        summary.Objects.Add(CreateText("TiTotalVatValue", FormatMoneyOrDash(document.VatAmount), 163, 33, 28, 4, 8.5, align: HorzAlign.Right));
        summary.Objects.Add(CreateText("TiTotalNetLabel", "ยอดเงินสุทธิ  Net Amount", 131, 37, 34, 4, 8.5, bold: true));
        summary.Objects.Add(CreateText("TiTotalNetValue", FormatMoneyOrDash(document.GrandTotal), 163, 37, 28, 4, 8.5, bold: true, align: HorzAlign.Right));

        summary.Objects.Add(CreateText("TiRemarkLabel", "หมายเหตุ : การชำระเงินจะสมบูรณ์ต่อเมื่อบริษัทได้รับชำระเรียบร้อยแล้ว", 0, 44, 194, 4, 8.5));
        summary.Objects.Add(CreateText("TiRemarkEn", "Payment by Cheque not valid till the cheque is honored", 0, 48, 194, 4, 8.5));
    }

    private static void BuildCreditNoteSummary(ReportPage page, BillingDocument document, string companyName, DocumentTemplate template)
    {
        var summary = new ReportSummaryBand
        {
            Name = "CreditNoteSummary",
            Height = Units.Millimeters * 72,
            PrintOnBottom = true
        };
        page.ReportSummary = summary;

        summary.Objects.Add(CreateText("CnRemarkLabel", "หมายเหตุ", 0, 0, 20, 4, 12, bold: true));
        summary.Objects.Add(CreateText("CnRemarkValue", FormatOrDash(document.Remark), 20, 0, 174, 4, 12));
        summary.Objects.Add(CreateText("CnReasonLabel", "เหตุผลใบลดหนี้", 0, 5, 30, 4, 12, bold: true));
        summary.Objects.Add(CreateText("CnReasonValue", FormatOrDash(document.CreditNoteReasonSnapshot), 30, 5, 164, 4, 12));
        summary.Objects.Add(CreateText("CnWordsLabel", "จำนวนเงิน (ตัวอักษร)", 0, 10, 45, 4, 12, bold: true));
        summary.Objects.Add(CreateText("CnWordsValue", FormatThaiBahtText(document.GrandTotal), 45, 10, 149, 4, 12));

        AddSummaryRow(summary, "CnAmountRow", 131, 16, 61, "รวมราคา", "Amount", document.SubTotal);
        AddSummaryRow(summary, "CnVatRow", 131, 23, 61, "ภาษีมูลค่าเพิ่ม", "VAT 7.00%", document.VatAmount);
        AddSummaryRow(summary, "CnGrandRow", 131, 30, 61, "ยอดเงินสุทธิ", "Net Amount", document.GrandTotal, true);

        BuildSignatureBoxes(summary, 0, 42, 194, 17, includeDelivery: true, includeCashier: true);
        summary.Objects.Add(CreateText("CnFooter", $"หมายเหตุ  {FormatOrDash(template.FooterText)}", 0, 61, 194, 3.5, 9));
    }

    private static void BuildSignatureBoxes(BandBase band, double leftMm, double topMm, double totalWidthMm, double boxHeightMm, bool includeDelivery, bool includeCashier)
    {
        var gapMm = 6d;
        var count = (includeCashier ? 1 : 0) + (includeDelivery ? 1 : 0) + 1;
        var boxWidth = count == 3 ? (totalWidthMm - (gapMm * 2)) / 3d : (totalWidthMm - gapMm) / 2d;

        if (includeCashier)
        {
            AddSignatureBox(band, leftMm, topMm, boxWidth, boxHeightMm, "ผู้รับเงิน | Cashier");
            leftMm += boxWidth + gapMm;
        }

        if (includeDelivery)
        {
            AddSignatureBox(band, leftMm, topMm, boxWidth, boxHeightMm, "ผู้ส่ง | Delivery");
            leftMm += boxWidth + gapMm;
        }

        AddSignatureBox(band, leftMm, topMm, boxWidth, boxHeightMm, "ผู้มีอำนาจลงนาม | Authorized Signature");
    }

    private static void AddSignatureBox(BandBase band, double leftMm, double topMm, double widthMm, double heightMm, string caption)
    {
        band.Objects.Add(AddBox("SignatureBox", leftMm, topMm, widthMm, heightMm));
        band.Objects.Add(CreateLine("SignatureLine", leftMm + 3, topMm + 8, widthMm - 6, 0.4));
        band.Objects.Add(CreateText("SignatureCaption", caption, leftMm, topMm + 9, widthMm, 4, 12, align: HorzAlign.Center));
        band.Objects.Add(CreateText("SignatureDate", "วันที่ / Date __________________", leftMm + 2, topMm + heightMm - 6, widthMm - 4, 4, 12));
    }

    private static void AddSummaryRow(BandBase band, string name, double leftMm, double topMm, double widthMm, string labelTh, string labelEn, decimal value, bool bold = false)
    {
        band.Objects.Add(CreateText($"{name}_Label", $"{labelTh}  {labelEn}", leftMm, topMm, widthMm - 24, 6, 10, bold: bold, valign: VertAlign.Center));
        band.Objects.Add(CreateText($"{name}_Value", value.ToString("N2", CultureInfo.InvariantCulture), leftMm + widthMm - 24, topMm, 24, 6, 10, bold: bold, align: HorzAlign.Right, valign: VertAlign.Center));
    }

    private static TextObject CreateText(string name, string text, double leftMm, double topMm, double widthMm, double heightMm, double fontSize, bool bold = false, HorzAlign align = HorzAlign.Left, VertAlign valign = VertAlign.Top, bool wordWrap = false, bool border = false)
    {
        var obj = new TextObject
        {
            Name = $"{name}_{Interlocked.Increment(ref _objectCounter)}",
            Text = text,
            Font = new Font(ThaiFontFamily, (float)fontSize, bold ? FontStyle.Bold : FontStyle.Regular),
            HorzAlign = align,
            VertAlign = valign,
            WordWrap = wordWrap,
            Clip = true,
            Bounds = new RectangleF(
                (float)(Units.Millimeters * leftMm),
                (float)(Units.Millimeters * topMm),
                (float)(Units.Millimeters * widthMm),
                (float)(Units.Millimeters * heightMm))
        };

        if (border)
        {
            obj.Border.Lines = BorderLines.All;
            obj.Border.Width = 1;
            obj.Border.Color = Color.Black;
        }

        return obj;
    }

    private static ShapeObject AddBox(string name, double leftMm, double topMm, double widthMm, double heightMm)
    {
        return new ShapeObject
        {
            Name = $"{name}_{Interlocked.Increment(ref _objectCounter)}",
            Shape = ShapeKind.Rectangle,
            FillColor = Color.White,
            Border =
            {
                Lines = BorderLines.All,
                Width = 1,
                Color = Color.Black
            },
            Bounds = new RectangleF(
                (float)(Units.Millimeters * leftMm),
                (float)(Units.Millimeters * topMm),
                (float)(Units.Millimeters * widthMm),
                (float)(Units.Millimeters * heightMm))
        };
    }

    private static LineObject CreateLine(string name, double leftMm, double topMm, double widthMm, double heightMm)
    {
        return new LineObject
        {
            Name = $"{name}_{Interlocked.Increment(ref _objectCounter)}",
            Border =
            {
                Lines = BorderLines.Top,
                Width = 1,
                Color = Color.Black
            },
            Bounds = new RectangleF(
                (float)(Units.Millimeters * leftMm),
                (float)(Units.Millimeters * topMm),
                (float)(Units.Millimeters * widthMm),
                (float)(Units.Millimeters * heightMm))
        };
    }

    private static PictureObject CreatePicture(string name, double leftMm, double topMm, double widthMm, double heightMm, string logoPath)
    {
        var picture = new PictureObject
        {
            Name = $"{name}_{Interlocked.Increment(ref _objectCounter)}",
            Bounds = new RectangleF(
                (float)(Units.Millimeters * leftMm),
                (float)(Units.Millimeters * topMm),
                (float)(Units.Millimeters * widthMm),
                (float)(Units.Millimeters * heightMm)),
            ShouldDisposeImage = true
        };

        picture.Image = Image.FromFile(logoPath);
        // SizeMode type lives in FastReport.Compat which has no direct NuGet reference — set via reflection
        var sizeModeProperty = picture.GetType().GetProperty("SizeMode");
        if (sizeModeProperty != null)
        {
            var zoomValue = Enum.Parse(sizeModeProperty.PropertyType, "Zoom");
            sizeModeProperty.SetValue(picture, zoomValue);
        }
        return picture;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        TryRegisterFontIfExists(RegularFontFileName);
        TryRegisterFontIfExists(BoldFontFileName);
        TryRegisterFontIfExists(ItalicFontFileName);
        TryRegisterFontIfExists(BoldItalicFontFileName);
        _initialized = true;
    }

    private static void TryRegisterFontIfExists(string fileName)
    {
        var fontPath = ResolveFontPath(fileName);
        if (File.Exists(fontPath))
        {
            try
            {
                FontManager.AddFont(fontPath);
            }
            catch (Exception ex) when (IsGraphicsInitializationFailure(ex))
            {
                // libgdiplus is not available in some macOS runtime contexts.
                // Keep the report generation path alive and rely on installed system fonts.
            }
        }
    }

    private static bool IsGraphicsInitializationFailure(Exception exception)
    {
        if (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return true;
        }

        if (exception is TypeInitializationException typeInitializationException && typeInitializationException.InnerException is not null)
        {
            return IsGraphicsInitializationFailure(typeInitializationException.InnerException);
        }

        return false;
    }

    private static string ResolveFontPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, FontsFolderName, FontsSubFolderName, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), FontsFolderName, FontsSubFolderName, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "BillingService_API", FontsFolderName, FontsSubFolderName, fileName)
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string? ResolveLogoPath(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        var trimmed = logoPath.Trim();
        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            return trimmed;
        }

        var direct = Path.Combine(AppContext.BaseDirectory, trimmed);
        if (File.Exists(direct))
        {
            return direct;
        }

        var current = Path.Combine(Directory.GetCurrentDirectory(), trimmed);
        if (File.Exists(current))
        {
            return current;
        }

        return null;
    }

    private static bool HasVat(BillingDocument document)
    {
        return document.VatAmount > 0m;
    }

    private static string ResolveDocumentTitle(string documentType, string fallback, bool hasVat)
    {
        return documentType switch
        {
            "RC" => hasVat ? "ใบเสร็จรับเงิน/ใบกำกับภาษี" : "ใบเสร็จรับเงิน",
            "TI" => "ใบกำกับภาษี",
            "CN" => "ใบลดหนี้",
            "DN" => "ใบเพิ่มหนี้",
            _ => fallback
        };
    }

    private static IReadOnlyList<string> BuildCompanyHeaderLines(BillingDocument document, string companyName, DocumentTemplate template)
    {
        var lines = new List<string>();
        var headerText = template.HeaderText?.Trim();

        if (!string.IsNullOrWhiteSpace(headerText))
        {
            lines.AddRange(headerText.Split(new[] { '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                lines.Add(companyName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(document.CompanyAddressSnapshot))
            {
                lines.Add(document.CompanyAddressSnapshot.Trim());
            }

            var contactParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(document.CompanyPhoneSnapshot))
            {
                contactParts.Add($"Tel {document.CompanyPhoneSnapshot.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(document.CompanyEmailSnapshot))
            {
                contactParts.Add(document.CompanyEmailSnapshot.Trim());
            }

            if (contactParts.Count > 0)
            {
                lines.Add(string.Join(" | ", contactParts));
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(FormatOrDash(companyName));
        }

        return lines;
    }

    private static string GetPrimaryHeaderLine(DocumentTemplate template, string fallbackCompanyName)
    {
        if (!string.IsNullOrWhiteSpace(template.HeaderText))
        {
            var firstLine = template.HeaderText.Split(new[] { '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                return firstLine;
            }
        }

        return fallbackCompanyName;
    }

    private static string FormatOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static string FormatDateOrDash(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatMoneyOrDash(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("N2", CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatMoneyValue(decimal amount, decimal? discountAmount)
    {
        return (amount - (discountAmount ?? 0m)).ToString("N2", CultureInfo.InvariantCulture);
    }

    private static string FormatThaiBahtText(decimal amount)
    {
        var integerPart = decimal.ToInt64(decimal.Truncate(amount));
        return $"{ConvertIntegerToThaiText(integerPart)}บาทถ้วน";
    }

    private static string ConvertIntegerToThaiText(long number)
    {
        if (number == 0)
        {
            return "ศูนย์";
        }

        if (number < 0)
        {
            return $"ลบ{ConvertIntegerToThaiText(Math.Abs(number))}";
        }

        var digits = new[] { "ศูนย์", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า" };
        var positions = new[] { "", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน", "ล้าน" };
        var result = new List<string>();
        var position = 0;
        var remaining = number;

        while (remaining > 0)
        {
            var digit = (int)(remaining % 10);
            if (digit > 0)
            {
                if (position == 0)
                {
                    result.Insert(0, digit == 1 && number > 10 ? "เอ็ด" : digits[digit]);
                }
                else if (position == 1)
                {
                    if (digit == 1)
                    {
                        result.Insert(0, "สิบ");
                    }
                    else if (digit == 2)
                    {
                        result.Insert(0, "ยี่สิบ");
                    }
                    else
                    {
                        result.Insert(0, digits[digit] + "สิบ");
                    }
                }
                else
                {
                    result.Insert(0, digits[digit] + positions[position]);
                }
            }

            position++;
            remaining /= 10;
        }

        return string.Concat(result);
    }

    private sealed record PdfItemRow(
        string ContractNo,
        string LineNo,
        string Description,
        string DueDate,
        string Quantity,
        string UnitPrice,
        string Discount,
        string Amount,
        string Vat,
        string Total,
        string TotalAmount);
}
