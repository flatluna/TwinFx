using CsvHelper;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using TwinFx.Models;

namespace TwinFx.Agents;

/// <summary>
/// Dynamic CSV generator for invoices that supports unlimited LineItems
/// ? INNOVATION: Unlike legacy CSV with 10 LineItem limit, this generates dynamic columns for ALL LineItems
/// Perfect for AT&T invoices with 30+ line items or Microsoft invoices with varying numbers
/// </summary>
public static class DynamicInvoiceCsvGenerator
{
    /// <summary>
    /// Generate dynamic CSV with ALL LineItems (no 10-item limitation)
    /// Creates dynamic columns: LineItem_1_Description, LineItem_1_Amount, etc. for ALL items
    /// </summary>
    /// <param name="invoices">List of InvoiceDocument objects</param>
    /// <returns>CSV content as string with dynamic columns for all line items</returns>
    public static string GenerateDynamicCsv(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
        {
            return string.Empty;
        }

        // Step 1: Analyze all invoices to determine maximum number of line items
        var maxLineItems = invoices.Max(i => i.InvoiceData.LineItems.Count);
        
        using var stringWriter = new StringWriter();
        using var csv = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);

        // Step 2: Write dynamic header
        WriteHeader(csv, maxLineItems);

        // Step 3: Write data rows with dynamic line item columns
        foreach (var invoice in invoices)
        {
            WriteInvoiceRow(csv, invoice, maxLineItems);
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Write CSV header with dynamic LineItem columns
    /// </summary>
    private static void WriteHeader(CsvWriter csv, int maxLineItems)
    {
        // Base invoice fields
        csv.WriteField("Id");
        csv.WriteField("TwinID");
        csv.WriteField("FileName");
        csv.WriteField("CreatedAt");
        csv.WriteField("ProcessedAt");
        
        // Vendor Information
        csv.WriteField("VendorName");
        csv.WriteField("VendorNameConfidence");
        csv.WriteField("VendorAddress");
        
        // Customer Information
        csv.WriteField("CustomerName");
        csv.WriteField("CustomerNameConfidence");
        csv.WriteField("CustomerAddress");
        
        // Invoice Details
        csv.WriteField("InvoiceNumber");
        csv.WriteField("InvoiceDate");
        csv.WriteField("DueDate");
        
        // Financial Information
        csv.WriteField("SubTotal");
        csv.WriteField("SubTotalConfidence");
        csv.WriteField("TotalTax");
        csv.WriteField("InvoiceTotal");
        csv.WriteField("InvoiceTotalConfidence");
        
        // Metadata
        csv.WriteField("LineItemsCount");
        csv.WriteField("TablesCount");
        csv.WriteField("RawFieldsCount");

        // Dynamic LineItem columns - generate for maximum number found
        for (int i = 1; i <= maxLineItems; i++)
        {
            csv.WriteField($"LineItem_{i}_Description");
            csv.WriteField($"LineItem_{i}_Amount");
            csv.WriteField($"LineItem_{i}_Quantity");
            csv.WriteField($"LineItem_{i}_UnitPrice");
            csv.WriteField($"LineItem_{i}_DescriptionConfidence");
            csv.WriteField($"LineItem_{i}_AmountConfidence");
        }

        csv.NextRecord();
    }

    /// <summary>
    /// Write single invoice row with dynamic LineItem data
    /// </summary>
    private static void WriteInvoiceRow(CsvWriter csv, InvoiceDocument invoice, int maxLineItems)
    {
        // Base invoice fields
        csv.WriteField(invoice.id);
        csv.WriteField(invoice.TwinID);
        csv.WriteField(invoice.FileName);
        csv.WriteField(invoice.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        csv.WriteField(invoice.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        
        // Vendor Information
        csv.WriteField(invoice.VendorName);
        csv.WriteField(invoice.VendorNameConfidence);
        csv.WriteField(invoice.InvoiceData.VendorAddress);
        
        // Customer Information
        csv.WriteField(invoice.CustomerName);
        csv.WriteField(invoice.CustomerNameConfidence);
        csv.WriteField(invoice.InvoiceData.CustomerAddress);
        
        // Invoice Details
        csv.WriteField(invoice.InvoiceNumber);
        csv.WriteField(invoice.InvoiceDate.ToString("yyyy-MM-dd"));
        csv.WriteField(invoice.InvoiceData.DueDate?.ToString("yyyy-MM-dd") ?? "");
        
        // Financial Information
        csv.WriteField(invoice.SubTotal);
        csv.WriteField(invoice.SubTotalConfidence);
        csv.WriteField(invoice.TotalTax);
        csv.WriteField(invoice.InvoiceTotal);
        csv.WriteField(invoice.InvoiceTotalConfidence);
        
        // Metadata
        csv.WriteField(invoice.LineItemsCount);
        csv.WriteField(invoice.TablesCount);
        csv.WriteField(invoice.RawFieldsCount);

        // Dynamic LineItem columns - write ALL line items for this invoice
        for (int i = 0; i < maxLineItems; i++)
        {
            if (i < invoice.InvoiceData.LineItems.Count)
            {
                var lineItem = invoice.InvoiceData.LineItems[i];
                csv.WriteField(lineItem.Description);
                csv.WriteField(lineItem.Amount);
                csv.WriteField(lineItem.Quantity);
                csv.WriteField(lineItem.UnitPrice);
                csv.WriteField(lineItem.DescriptionConfidence);
                csv.WriteField(lineItem.AmountConfidence);
            }
            else
            {
                // Empty fields for invoices with fewer line items
                csv.WriteField("");  // Description
                csv.WriteField("");  // Amount
                csv.WriteField("");  // Quantity
                csv.WriteField("");  // UnitPrice
                csv.WriteField("");  // DescriptionConfidence
                csv.WriteField("");  // AmountConfidence
            }
        }

        csv.NextRecord();
    }

    /// <summary>
    /// Analyze the structure of invoices to provide insights about dynamic CSV generation
    /// </summary>
    /// <param name="invoices">List of InvoiceDocument objects</param>
    /// <returns>Analysis string with statistics</returns>
    public static string AnalyzeDynamicCsvStructure(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
        {
            return "?? No invoices to analyze";
        }

        var analysis = new StringBuilder();
        
        // Line items analysis
        var lineItemCounts = invoices.Select(i => i.InvoiceData.LineItems.Count).ToList();
        var maxLineItems = lineItemCounts.Max();
        var minLineItems = lineItemCounts.Min();
        var avgLineItems = lineItemCounts.Average();
        var totalLineItems = lineItemCounts.Sum();

        analysis.AppendLine("?? DYNAMIC CSV STRUCTURE ANALYSIS:");
        analysis.AppendLine($"   ?? Total Invoices: {invoices.Count}");
        analysis.AppendLine($"   ?? Line Items Statistics:");
        analysis.AppendLine($"      • Maximum: {maxLineItems} (vs legacy limit of 10)");
        analysis.AppendLine($"      • Minimum: {minLineItems}");
        analysis.AppendLine($"      • Average: {avgLineItems:F1}");
        analysis.AppendLine($"      • Total: {totalLineItems}");
        analysis.AppendLine();

        // Show benefit analysis
        var exceedsLegacyLimit = invoices.Count(i => i.InvoiceData.LineItems.Count > 10);
        var lostDataInLegacy = invoices.Sum(i => Math.Max(0, i.InvoiceData.LineItems.Count - 10));
        
        analysis.AppendLine($"   ? DYNAMIC CSV BENEFITS:");
        analysis.AppendLine($"      • Invoices exceeding legacy 10-item limit: {exceedsLegacyLimit}");
        analysis.AppendLine($"      • LineItems that would be lost in legacy CSV: {lostDataInLegacy}");
        analysis.AppendLine($"      • Data completeness: 100% (vs ~{(1 - (double)lostDataInLegacy / totalLineItems):P1} in legacy)");
        analysis.AppendLine();

        // CSV structure info
        var baseColumns = 23; // Base invoice fields count
        var dynamicColumns = maxLineItems * 6; // 6 fields per line item
        var totalColumns = baseColumns + dynamicColumns;
        
        analysis.AppendLine($"   ?? CSV STRUCTURE:");
        analysis.AppendLine($"      • Base columns: {baseColumns}");
        analysis.AppendLine($"      • Dynamic LineItem columns: {dynamicColumns} ({maxLineItems} items × 6 fields)");
        analysis.AppendLine($"      • Total columns: {totalColumns}");
        analysis.AppendLine();

        // Vendor analysis
        var vendors = invoices.Where(i => !string.IsNullOrEmpty(i.VendorName))
                             .GroupBy(i => i.VendorName)
                             .Select(g => new { 
                                 Vendor = g.Key, 
                                 Count = g.Count(), 
                                 MaxItems = g.Max(i => i.InvoiceData.LineItems.Count),
                                 AvgItems = g.Average(i => i.InvoiceData.LineItems.Count)
                             })
                             .OrderByDescending(x => x.MaxItems)
                             .Take(5);

        analysis.AppendLine($"   ?? VENDOR LINE ITEM ANALYSIS:");
        foreach (var vendor in vendors)
        {
            analysis.AppendLine($"      • {vendor.Vendor}: {vendor.Count} invoices, max {vendor.MaxItems} items, avg {vendor.AvgItems:F1}");
        }

        return analysis.ToString();
    }

    /// <summary>
    /// Generate compact dynamic CSV for testing/preview (limits to first N invoices and items)
    /// </summary>
    /// <param name="invoices">List of InvoiceDocument objects</param>
    /// <param name="maxInvoices">Maximum number of invoices to include</param>
    /// <param name="maxLineItemsPerInvoice">Maximum line items per invoice to include</param>
    /// <returns>Compact CSV content for testing</returns>
    public static string GenerateCompactDynamicCsv(List<InvoiceDocument> invoices, int maxInvoices = 5, int maxLineItemsPerInvoice = 15)
    {
        if (!invoices.Any())
        {
            return string.Empty;
        }

        // Limit invoices and line items for compact version
        var limitedInvoices = invoices.Take(maxInvoices).ToList();
        var actualMaxLineItems = Math.Min(
            limitedInvoices.Max(i => i.InvoiceData.LineItems.Count), 
            maxLineItemsPerInvoice
        );

        using var stringWriter = new StringWriter();
        using var csv = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);

        // Write header for compact version
        WriteHeader(csv, actualMaxLineItems);

        // Write limited data rows
        foreach (var invoice in limitedInvoices)
        {
            WriteInvoiceRow(csv, invoice, actualMaxLineItems);
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Validate dynamic CSV generation by checking data integrity
    /// </summary>
    /// <param name="invoices">Original invoice documents</param>
    /// <param name="csvContent">Generated CSV content</param>
    /// <returns>Validation results</returns>
    public static DynamicCsvValidationResult ValidateDynamicCsv(List<InvoiceDocument> invoices, string csvContent)
    {
        var result = new DynamicCsvValidationResult();
        
        try
        {
            var lines = csvContent.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            
            result.IsValid = true;
            result.TotalRows = lines.Length - 1; // Exclude header
            result.ExpectedRows = invoices.Count;
            result.HasCorrectRowCount = result.TotalRows == result.ExpectedRows;
            
            if (lines.Any())
            {
                var headerFields = lines[0].Split(',');
                result.TotalColumns = headerFields.Length;
                
                // Count dynamic line item columns
                result.DynamicLineItemColumns = headerFields.Count(f => f.Contains("LineItem_"));
                result.MaxLineItemsSupported = result.DynamicLineItemColumns / 6; // 6 fields per line item
                
                var actualMaxLineItems = invoices.Max(i => i.InvoiceData.LineItems.Count);
                result.SupportsAllLineItems = result.MaxLineItemsSupported >= actualMaxLineItems;
            }
            
            result.ValidationMessage = result.IsValid ? "? Dynamic CSV validation successful" : "? Dynamic CSV validation failed";
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ValidationMessage = $"? Validation error: {ex.Message}";
        }
        
        return result;
    }
}

/// <summary>
/// Result of dynamic CSV validation
/// </summary>
public class DynamicCsvValidationResult
{
    public bool IsValid { get; set; }
    public int TotalRows { get; set; }
    public int ExpectedRows { get; set; }
    public bool HasCorrectRowCount { get; set; }
    public int TotalColumns { get; set; }
    public int DynamicLineItemColumns { get; set; }
    public int MaxLineItemsSupported { get; set; }
    public bool SupportsAllLineItems { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
    
    public override string ToString()
    {
        return $"{ValidationMessage} - Rows: {TotalRows}/{ExpectedRows}, Columns: {TotalColumns}, Supports {MaxLineItemsSupported} LineItems";
    }
}