using System;
using System.Collections.Generic;

namespace DataAccess.Models.BillingService;

public partial class DocumentType
{
    public string DocumentTypeCode { get; set; } = null!;

    public string DocumentTypeName { get; set; } = null!;

    /// <summary>
    /// เป็นเอกสารภาษีหรือไม่
    /// </summary>
    public bool IsVatDocument { get; set; }

    public virtual ICollection<DocumentRunningNumber> DocumentRunningNumbers { get; set; } = new List<DocumentRunningNumber>();

    public virtual ICollection<DocumentTemplate> DocumentTemplates { get; set; } = new List<DocumentTemplate>();

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
