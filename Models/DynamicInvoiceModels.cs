using System.Dynamic;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace TwinFx.Models;

/// <summary>
/// Dynamic invoice record that adapts to the maximum number of LineItems
/// NO LIMITATION: Supports unlimited LineItems instead of fixed 10
/// </summary>
public class DynamicInvoiceRecord
{
    // Basic invoice fields (same as InvoiceRecord)
    public string Id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public decimal VendorNameConfidence { get; set; }
    public string VendorAddress { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal CustomerNameConfidence { get; set; }
    public string CustomerAddress { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal SubTotalConfidence { get; set; }
    public decimal TotalTax { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal InvoiceTotalConfidence { get; set; }
    public int LineItemsCount { get; set; }
    public int TablesCount { get; set; }
    public int RawFieldsCount { get; set; }
    
    // ? DYNAMIC LineItems: No limit, adapts to maximum found
    public Dictionary<string, object> DynamicLineItems { get; set; } = new();
    
    /// <summary>
    /// Convert InvoiceDocument to DynamicInvoiceRecord with ALL LineItems
    /// </summary>
    public static DynamicInvoiceRecord FromInvoiceDocument(InvoiceDocument document, int maxLineItems)
    {
        var record = new DynamicInvoiceRecord
        {
            Id = document.id,
            TwinID = document.TwinID,
            FileName = document.FileName,
            CreatedAt = document.CreatedAt,
            VendorName = document.VendorName,
            VendorNameConfidence = (decimal)document.VendorNameConfidence,
            VendorAddress = document.InvoiceData.VendorAddress,
            CustomerName = document.CustomerName,
            CustomerNameConfidence = (decimal)document.CustomerNameConfidence,
            CustomerAddress = document.InvoiceData.CustomerAddress,
            InvoiceNumber = document.InvoiceNumber,
            InvoiceDate = document.InvoiceDate,
            DueDate = document.InvoiceData.DueDate ?? DateTime.MinValue,
            SubTotal = document.SubTotal,
            SubTotalConfidence = (decimal)document.SubTotalConfidence,
            TotalTax = document.TotalTax,
            InvoiceTotal = document.InvoiceTotal,
            InvoiceTotalConfidence = (decimal)document.InvoiceTotalConfidence,
            LineItemsCount = document.LineItemsCount,
            TablesCount = document.TablesCount,
            RawFieldsCount = document.RawFieldsCount
        };

        // ? ADD ALL LineItems dynamically
        for (int i = 0; i < document.InvoiceData.LineItems.Count; i++)
        {
            var lineItem = document.InvoiceData.LineItems[i];
            var itemNumber = i + 1;
            
            record.DynamicLineItems[$"LineItem{itemNumber}_Description"] = lineItem.Description;
            record.DynamicLineItems[$"LineItem{itemNumber}_Amount"] = lineItem.Amount;
            record.DynamicLineItems[$"LineItem{itemNumber}_Quantity"] = lineItem.Quantity;
            record.DynamicLineItems[$"LineItem{itemNumber}_UnitPrice"] = lineItem.UnitPrice;
        }
        
        // ? FILL empty columns for consistency (CSV needs same column count)
        for (int i = document.InvoiceData.LineItems.Count; i < maxLineItems; i++)
        {
            var itemNumber = i + 1;
            record.DynamicLineItems[$"LineItem{itemNumber}_Description"] = "";
            record.DynamicLineItems[$"LineItem{itemNumber}_Amount"] = 0m;
            record.DynamicLineItems[$"LineItem{itemNumber}_Quantity"] = 0.0;
            record.DynamicLineItems[$"LineItem{itemNumber}_UnitPrice"] = 0m;
        }

        return record;
    }
    
    /// <summary>
    /// Convert to flat dictionary for CSV serialization
    /// </summary>
    public Dictionary<string, object> ToFlatDictionary()
    {
        var dict = new Dictionary<string, object>
        {
            ["Id"] = Id,
            ["TwinID"] = TwinID,
            ["FileName"] = FileName,
            ["CreatedAt"] = CreatedAt,
            ["VendorName"] = VendorName,
            ["VendorNameConfidence"] = VendorNameConfidence,
            ["VendorAddress"] = VendorAddress,
            ["CustomerName"] = CustomerName,
            ["CustomerNameConfidence"] = CustomerNameConfidence,
            ["CustomerAddress"] = CustomerAddress,
            ["InvoiceNumber"] = InvoiceNumber,
            ["InvoiceDate"] = InvoiceDate,
            ["DueDate"] = DueDate,
            ["SubTotal"] = SubTotal,
            ["SubTotalConfidence"] = SubTotalConfidence,
            ["TotalTax"] = TotalTax,
            ["InvoiceTotal"] = InvoiceTotal,
            ["InvoiceTotalConfidence"] = InvoiceTotalConfidence,
            ["LineItemsCount"] = LineItemsCount,
            ["TablesCount"] = TablesCount,
            ["RawFieldsCount"] = RawFieldsCount
        };
        
        // Add all dynamic LineItems
        foreach (var kvp in DynamicLineItems.OrderBy(x => x.Key))
        {
            dict[kvp.Key] = kvp.Value;
        }
        
        return dict;
    }
}

/// <summary>
/// CSV Generator for dynamic invoice records with variable LineItem columns
/// </summary>
public static class DynamicInvoiceCsvGenerator
{
    /// <summary>
    /// Generate CSV with dynamic columns based on maximum LineItems
    /// ? NO LIMITS: Supports 30+ LineItems for AT&T, 9 for Microsoft, etc.
    /// </summary>
    public static string GenerateDynamicCsv(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
            return "";

        // ? STEP 1: Calculate maximum LineItems across all invoices
        var maxLineItems = invoices.Max(i => i.InvoiceData.LineItems.Count);
        
        Console.WriteLine($"?? Dynamic CSV: Max LineItems found = {maxLineItems}");
        
        // ? STEP 2: Convert all invoices to dynamic records
        var dynamicRecords = invoices.Select(doc => 
            DynamicInvoiceRecord.FromInvoiceDocument(doc, maxLineItems)).ToList();
        
        // ? STEP 3: Generate CSV with all dynamic columns
        using var stringWriter = new StringWriter();
        using var csv = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);
        
        // Write headers
        var firstRecord = dynamicRecords.First();
        var flatDict = firstRecord.ToFlatDictionary();
        
        // Write header row
        foreach (var key in flatDict.Keys.OrderBy(k => k))
        {
            csv.WriteField(key);
        }
        csv.NextRecord();
        
        // Write data rows
        foreach (var record in dynamicRecords)
        {
            var recordDict = record.ToFlatDictionary();
            foreach (var key in flatDict.Keys.OrderBy(k => k))
            {
                csv.WriteField(recordDict.GetValueOrDefault(key, ""));
            }
            csv.NextRecord();
        }
        
        var csvContent = stringWriter.ToString();
        
        Console.WriteLine($"? Dynamic CSV generated: {dynamicRecords.Count} invoices x {flatDict.Count} columns");
        Console.WriteLine($"?? LineItem columns: {maxLineItems * 4} (Description, Amount, Quantity, UnitPrice)");
        
        return csvContent;
    }
    
    /// <summary>
    /// Analyze the dynamic CSV structure for debugging
    /// </summary>
    public static string AnalyzeDynamicCsvStructure(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
            return "No invoices to analyze";

        var maxLineItems = invoices.Max(i => i.InvoiceData.LineItems.Count);
        var analysis = new StringBuilder();
        
        analysis.AppendLine($"?? **Dynamic CSV Structure Analysis**");
        analysis.AppendLine($"=====================================");
        analysis.AppendLine();
        analysis.AppendLine($"?? **Statistics:**");
        analysis.AppendLine($"   • Total invoices: {invoices.Count}");
        analysis.AppendLine($"   • Maximum LineItems: {maxLineItems}");
        analysis.AppendLine($"   • Basic invoice fields: 21");
        analysis.AppendLine($"   • Dynamic LineItem fields: {maxLineItems * 4}");
        analysis.AppendLine($"   • Total CSV columns: {21 + (maxLineItems * 4)}");
        analysis.AppendLine();
        
        analysis.AppendLine($"?? **Per Invoice Analysis:**");
        foreach (var invoice in invoices.OrderByDescending(i => i.InvoiceData.LineItems.Count))
        {
            var percentage = (double)invoice.InvoiceData.LineItems.Count / maxLineItems * 100;
            analysis.AppendLine($"   • {invoice.FileName}: {invoice.InvoiceData.LineItems.Count} items ({percentage:F1}% of max)");
        }
        analysis.AppendLine();
        
        analysis.AppendLine($"?? **Benefits vs Fixed 10-Column Approach:**");
        var attInvoice = invoices.FirstOrDefault(i => i.FileName.Contains("ATT"));
        if (attInvoice != null)
        {
            var attItems = attInvoice.InvoiceData.LineItems.Count;
            var oldCoverage = Math.Min(10, attItems) / (double)attItems * 100;
            var newCoverage = 100.0;
            
            analysis.AppendLine($"   • AT&T Coverage - Old: {oldCoverage:F1}% (10/{attItems})");
            analysis.AppendLine($"   • AT&T Coverage - New: {newCoverage:F1}% ({attItems}/{attItems})");
            analysis.AppendLine($"   • Improvement: +{newCoverage - oldCoverage:F1}% data coverage");
        }
        
        return analysis.ToString();
    }
}