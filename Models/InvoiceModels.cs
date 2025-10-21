using CsvHelper.Configuration.Attributes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinFx.Models;

/// <summary>
/// Invoice record data structure for CSV export and analysis
/// </summary>
public class InvoiceRecord
{
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
    
    // Flattened LineItems for CSV - hasta 10 line items
    public string LineItem1_Description { get; set; } = string.Empty;
    public decimal LineItem1_Amount { get; set; }
    public double LineItem1_Quantity { get; set; }
    public decimal LineItem1_UnitPrice { get; set; }
    
    public string LineItem2_Description { get; set; } = string.Empty;
    public decimal LineItem2_Amount { get; set; }
    public double LineItem2_Quantity { get; set; }
    public decimal LineItem2_UnitPrice { get; set; }
    
    public string LineItem3_Description { get; set; } = string.Empty;
    public decimal LineItem3_Amount { get; set; }
    public double LineItem3_Quantity { get; set; }
    public decimal LineItem3_UnitPrice { get; set; }
    
    public string LineItem4_Description { get; set; } = string.Empty;
    public decimal LineItem4_Amount { get; set; }
    public double LineItem4_Quantity { get; set; }
    public decimal LineItem4_UnitPrice { get; set; }
    
    public string LineItem5_Description { get; set; } = string.Empty;
    public decimal LineItem5_Amount { get; set; }
    public double LineItem5_Quantity { get; set; }
    public decimal LineItem5_UnitPrice { get; set; }
    
    public string LineItem6_Description { get; set; } = string.Empty;
    public decimal LineItem6_Amount { get; set; }
    public double LineItem6_Quantity { get; set; }
    public decimal LineItem6_UnitPrice { get; set; }
    
    public string LineItem7_Description { get; set; } = string.Empty;
    public decimal LineItem7_Amount { get; set; }
    public double LineItem7_Quantity { get; set; }
    public decimal LineItem7_UnitPrice { get; set; }
    
    public string LineItem8_Description { get; set; } = string.Empty;
    public decimal LineItem8_Amount { get; set; }
    public double LineItem8_Quantity { get; set; }
    public decimal LineItem8_UnitPrice { get; set; }
    
    public string LineItem9_Description { get; set; } = string.Empty;
    public decimal LineItem9_Amount { get; set; }
    public double LineItem9_Quantity { get; set; }
    public decimal LineItem9_UnitPrice { get; set; }
    
    public string LineItem10_Description { get; set; } = string.Empty;
    public decimal LineItem10_Amount { get; set; }
    public double LineItem10_Quantity { get; set; }
    public decimal LineItem10_UnitPrice { get; set; }
    
    // Complete invoice data structure (NOT serialized to CSV for now)
    [System.Text.Json.Serialization.JsonIgnore]
    [Ignore]
    public StructuredInvoiceData InvoiceData { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonIgnore]
    [Ignore]
    public List<ExtractedTable> Tables { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonIgnore]
    [Ignore]
    public Dictionary<string, DocumentFieldInfo> RawDocumentFields { get; set; } = new();

    /// <summary>
    /// Create InvoiceRecord from Cosmos DB dictionary with dynamic LineItem columns
    /// </summary>
    public static InvoiceRecord FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (data.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    if (value is JsonElement jsonElement)
                    {
                        var type = typeof(T);
                        if (type == typeof(string))
                            return (T)(object)(jsonElement.GetString() ?? string.Empty);
                        if (type == typeof(DateTime))
                        {
                            if (jsonElement.ValueKind == JsonValueKind.String)
                            {
                                var dateStr = jsonElement.GetString();
                                if (DateTime.TryParse(dateStr, out var parsedDate))
                                    return (T)(object)parsedDate;
                            }
                            return defaultValue;
                        }
                        if (type == typeof(decimal))
                            return (T)(object)jsonElement.GetDecimal();
                        if (type == typeof(int))
                            return (T)(object)jsonElement.GetInt32();
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        // Parse LineItems directly from Cosmos DB JSON structure
        List<InvoiceLineItem> ParseLineItemsFromCosmosData(Dictionary<string, object?> data)
        {
            var lineItems = new List<InvoiceLineItem>();
            
            try
            {
                // Debug: Log what we're looking for
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Looking for invoiceData in data with {data.Count} keys");
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Available keys: {string.Join(", ", data.Keys)}");
                
                // Try to get LineItems from invoiceData.LineItems
                if (data.TryGetValue("invoiceData", out var invoiceDataValue))
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Found invoiceData, type: {invoiceDataValue?.GetType().Name}");
                    
                    // Handle different types of JSON objects from Cosmos DB
                    if (invoiceDataValue is JsonElement invoiceDataElement)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: invoiceData is JsonElement, ValueKind: {invoiceDataElement.ValueKind}");
                        lineItems = ParseLineItemsFromJsonElement(invoiceDataElement);
                    }
                    else if (invoiceDataValue != null)
                    {
                        // Handle JObject (Newtonsoft.Json) or other object types
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: invoiceData is {invoiceDataValue.GetType().Name}, trying to parse as JObject...");
                        
                        // Check if it's a JObject (Newtonsoft.Json)
                        var invoiceDataType = invoiceDataValue.GetType();
                        if (invoiceDataType.Name == "JObject" || invoiceDataType.Namespace?.Contains("Newtonsoft") == true)
                        {
                            lineItems = ParseLineItemsFromJObject(invoiceDataValue);
                        }
                        else
                        {
                            // Try converting to JsonElement as fallback
                            try
                            {
                                var jsonString = JsonSerializer.Serialize(invoiceDataValue);
                                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Serialized invoiceData: {(jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString)}");
                                
                                var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonString);
                                lineItems = ParseLineItemsFromJsonElement(jsonElement);
                            }
                            catch (Exception convEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Conversion failed: {convEx.Message}");
                            }
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🔍 DEBUG: invoiceData key not found in data");
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - just return empty list
                System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing LineItems: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Stack trace: {ex.StackTrace}");
            }
            
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Final result: {lineItems.Count} LineItems parsed");
            return lineItems;
        }

        // Parse LineItems from JsonElement (System.Text.Json)
        List<InvoiceLineItem> ParseLineItemsFromJsonElement(JsonElement invoiceDataElement)
        {
            var lineItems = new List<InvoiceLineItem>();
            
            try
            {
                if (invoiceDataElement.TryGetProperty("LineItems", out var lineItemsElement) && 
                    lineItemsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemElement in lineItemsElement.EnumerateArray())
                    {
                        var lineItem = new InvoiceLineItem();
                        
                        if (itemElement.TryGetProperty("Description", out var desc))
                            lineItem.Description = desc.GetString() ?? "";
                        if (itemElement.TryGetProperty("DescriptionConfidence", out var descConf))
                            lineItem.DescriptionConfidence = (float)descConf.GetDecimal();
                        if (itemElement.TryGetProperty("Quantity", out var qty))
                            lineItem.Quantity = qty.GetDouble();
                        if (itemElement.TryGetProperty("UnitPrice", out var unitPrice))
                            lineItem.UnitPrice = unitPrice.GetDecimal();
                        if (itemElement.TryGetProperty("Amount", out var amount))
                            lineItem.Amount = amount.GetDecimal();
                        if (itemElement.TryGetProperty("AmountConfidence", out var amtConf))
                            lineItem.AmountConfidence = (float)amtConf.GetDecimal();
                        
                        lineItems.Add(lineItem);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Successfully parsed {lineItems.Count} LineItems from JsonElement");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing JsonElement LineItems: {ex.Message}");
            }
            
            return lineItems;
        }

        // Parse LineItems from JObject (Newtonsoft.Json)
        List<InvoiceLineItem> ParseLineItemsFromJObject(object jObjectValue)
        {
            var lineItems = new List<InvoiceLineItem>();
            
            try
            {
                // Use reflection to access JObject properties
                var jObjectType = jObjectValue.GetType();
                var indexer = jObjectType.GetProperty("Item", new[] { typeof(string) });
                
                if (indexer != null)
                {
                    var lineItemsToken = indexer.GetValue(jObjectValue, new object[] { "LineItems" });
                    
                    if (lineItemsToken != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Found LineItems in JObject, type: {lineItemsToken.GetType().Name}");
                        
                        // Try to get the array elements
                        var lineItemsTokenType = lineItemsToken.GetType();
                        if (lineItemsTokenType.Name == "JArray")
                        {
                            // Use reflection to iterate through JArray
                            var countProperty = lineItemsTokenType.GetProperty("Count");
                            var itemIndexer = lineItemsTokenType.GetProperty("Item", new[] { typeof(int) });
                            
                            if (countProperty != null && itemIndexer != null)
                            {
                                var count = (int)(countProperty.GetValue(lineItemsToken) ?? 0);
                                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: JArray has {count} elements");
                                
                                for (int i = 0; i < count; i++)
                                {
                                    var itemToken = itemIndexer.GetValue(lineItemsToken, new object[] { i });
                        
                                    if (itemToken != null)
                                    {
                                        var lineItem = ParseLineItemFromJToken(itemToken);
                                        if (lineItem != null)
                                        {
                                            lineItems.Add(lineItem);
                                        }
                                    }
                                }
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Successfully parsed {lineItems.Count} LineItems from JObject");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("🔍 DEBUG: LineItems not found in JObject");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing JObject LineItems: {ex.Message}");
            }
            
            return lineItems;
        }

        // Parse individual LineItem from JToken
        InvoiceLineItem? ParseLineItemFromJToken(object jToken)
        {
            try
            {
                var lineItem = new InvoiceLineItem();
                var jTokenType = jToken.GetType();
                var indexer = jTokenType.GetProperty("Item", new[] { typeof(string) });
                
                if (indexer != null)
                {
                    // Helper function to get value from JToken
                    T GetJTokenValue<T>(string propertyName, T defaultValue = default!)
                    {
                        try
                        {
                            var tokenValue = indexer.GetValue(jToken, new object[] { propertyName });
                            if (tokenValue != null)
                            {
                                // Get the Value property from JValue
                                var valueProperty = tokenValue.GetType().GetProperty("Value");
                                if (valueProperty != null)
                                {
                                    var value = valueProperty.GetValue(tokenValue);
                                    if (value != null)
                                    {
                                        return (T)Convert.ChangeType(value, typeof(T));
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Return default value on any error
                        }
                        return defaultValue;
                    }
                    
                    lineItem.Description = GetJTokenValue<string>("Description", "");
                    lineItem.DescriptionConfidence = GetJTokenValue<float>("DescriptionConfidence", 0f);
                    lineItem.Quantity = GetJTokenValue<double>("Quantity", 0.0);
                    lineItem.UnitPrice = GetJTokenValue<decimal>("UnitPrice", 0m);
                    lineItem.Amount = GetJTokenValue<decimal>("Amount", 0m);
                    lineItem.AmountConfidence = GetJTokenValue<float>("AmountConfidence", 0f);
                    
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Parsed LineItem - Description: '{lineItem.Description}', Amount: {lineItem.Amount}");
                    
                    return lineItem;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing JToken LineItem: {ex.Message}");
            }
            
            return null;
        }

        // Parse and create the invoice record with basic fields
        var lineItems = ParseLineItemsFromCosmosData(data);
        
        var record = new InvoiceRecord
        {
            Id = GetValue("id", GetValue("fileName", "")),
            TwinID = GetValue<string>("TwinID"),
            FileName = GetValue<string>("fileName"),
            CreatedAt = GetValue("createdAt", DateTime.MinValue),
            VendorName = GetValue<string>("vendorName"),
            VendorNameConfidence = GetValue<decimal>("vendorNameConfidence"),
            VendorAddress = GetValue<string>("vendorAddress"),
            CustomerName = GetValue<string>("customerName"),
            CustomerNameConfidence = GetValue<decimal>("customerNameConfidence"),
            CustomerAddress = GetValue<string>("customerAddress"),
            InvoiceNumber = GetValue<string>("invoiceNumber"),
            InvoiceDate = GetValue("invoiceDate", DateTime.MinValue),
            DueDate = GetValue("dueDate", DateTime.MinValue),
            SubTotal = GetValue<decimal>("subTotal"),
            SubTotalConfidence = GetValue<decimal>("subTotalConfidence"),
            TotalTax = GetValue<decimal>("totalTax"),
            InvoiceTotal = GetValue<decimal>("invoiceTotal"),
            InvoiceTotalConfidence = GetValue<decimal>("invoiceTotalConfidence"),
            LineItemsCount = GetValue<int>("lineItemsCount"),
            TablesCount = GetValue<int>("tablesCount"),
            RawFieldsCount = GetValue<int>("rawFieldsCount")
        };

        // Try to get VendorAddress and CustomerAddress from invoiceData
        try
        {
            if (data.TryGetValue("invoiceData", out var invoiceDataValue) && invoiceDataValue is JsonElement invoiceDataElement)
            {
                if (invoiceDataElement.TryGetProperty("VendorAddress", out var vendorAddr))
                    record.VendorAddress = vendorAddr.GetString() ?? "";
                if (invoiceDataElement.TryGetProperty("CustomerAddress", out var customerAddr))
                    record.CustomerAddress = customerAddr.GetString() ?? "";
            }
        }
        catch
        {
            // Ignore parsing errors for addresses
        }

        // Dynamically assign LineItems to columns - only create what exists
        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Starting LineItem assignment for {lineItems.Count} items");
        
        for (int i = 0; i < Math.Min(lineItems.Count, 10); i++)
        {
            var lineItem = lineItems[i];
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Assigning LineItem {i + 1} - Description: '{lineItem.Description}', Amount: {lineItem.Amount}");
            
            // Direct assignment instead of reflection for better debugging
            switch (i)
            {
                case 0:
                    record.LineItem1_Description = lineItem.Description;
                    record.LineItem1_Amount = lineItem.Amount;
                    record.LineItem1_Quantity = lineItem.Quantity;
                    record.LineItem1_UnitPrice = lineItem.UnitPrice;
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Set LineItem1 - Description: '{record.LineItem1_Description}', Amount: {record.LineItem1_Amount}");
                    break;
                case 1:
                    record.LineItem2_Description = lineItem.Description;
                    record.LineItem2_Amount = lineItem.Amount;
                    record.LineItem2_Quantity = lineItem.Quantity;
                    record.LineItem2_UnitPrice = lineItem.UnitPrice;
                    break;
                case 2:
                    record.LineItem3_Description = lineItem.Description;
                    record.LineItem3_Amount = lineItem.Amount;
                    record.LineItem3_Quantity = lineItem.Quantity;
                    record.LineItem3_UnitPrice = lineItem.UnitPrice;
                    break;
                case 3:
                    record.LineItem4_Description = lineItem.Description;
                    record.LineItem4_Amount = lineItem.Amount;
                    record.LineItem4_Quantity = lineItem.Quantity;
                    record.LineItem4_UnitPrice = lineItem.UnitPrice;
                    break;
                case 4:
                    record.LineItem5_Description = lineItem.Description;
                    record.LineItem5_Amount = lineItem.Amount;
                    record.LineItem5_Quantity = lineItem.Quantity;
                    record.LineItem5_UnitPrice = lineItem.UnitPrice;
                    break;
                case 5:
                    record.LineItem6_Description = lineItem.Description;
                    record.LineItem6_Amount = lineItem.Amount;
                    record.LineItem6_Quantity = lineItem.Quantity;
                    record.LineItem6_UnitPrice = lineItem.UnitPrice;
                    break;
                case 6:
                    record.LineItem7_Description = lineItem.Description;
                    record.LineItem7_Amount = lineItem.Amount;
                    record.LineItem7_Quantity = lineItem.Quantity;
                    record.LineItem7_UnitPrice = lineItem.UnitPrice;
                    break;
                case 7:
                    record.LineItem8_Description = lineItem.Description;
                    record.LineItem8_Amount = lineItem.Amount;
                    record.LineItem8_Quantity = lineItem.Quantity;
                    record.LineItem8_UnitPrice = lineItem.UnitPrice;
                    break;
                case 8:
                    record.LineItem9_Description = lineItem.Description;
                    record.LineItem9_Amount = lineItem.Amount;
                    record.LineItem9_Quantity = lineItem.Quantity;
                    record.LineItem9_UnitPrice = lineItem.UnitPrice;
                    break;
                case 9:
                    record.LineItem10_Description = lineItem.Description;
                    record.LineItem10_Amount = lineItem.Amount;
                    record.LineItem10_Quantity = lineItem.Quantity;
                    record.LineItem10_UnitPrice = lineItem.UnitPrice;
                    break;
            }
        }

        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Final record - LineItem1_Description: '{record.LineItem1_Description}', LineItem1_Amount: {record.LineItem1_Amount}");
        
        return record;
    }
}

/// <summary>
/// Structured invoice data extracted from Document Intelligence
/// </summary> 

public class StructuredInvoiceData
{
    // Vendor Information  
    [JsonPropertyName("vendorName")]
    public string VendorName { get; set; } = string.Empty;

    [JsonPropertyName("vendorNameConfidence")]
    public float VendorNameConfidence { get; set; }

    [JsonPropertyName("vendorAddress")]
    public string VendorAddress { get; set; } = string.Empty;

    // Customer Information  
    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("customerNameConfidence")]
    public float CustomerNameConfidence { get; set; }

    [JsonPropertyName("customerAddress")]
    public string CustomerAddress { get; set; } = string.Empty;

    // Invoice Metadata  
    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("invoiceDate")]
    public DateTime? InvoiceDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime? DueDate { get; set; }

    // Financial Totals  
    [JsonPropertyName("subTotal")]
    public decimal SubTotal { get; set; }

    [JsonPropertyName("subTotalConfidence")]
    public float SubTotalConfidence { get; set; }

    [JsonPropertyName("totalTax")]
    public decimal TotalTax { get; set; }

    [JsonPropertyName("invoiceTotal")]
    public decimal InvoiceTotal { get; set; }

    [JsonPropertyName("invoiceTotalConfidence")]
    public float InvoiceTotalConfidence { get; set; }

    // Line Items  
    [JsonPropertyName("lineItems")]
    public List<InvoiceLineItem> LineItems { get; set; } = new();
}

public class InvoiceLineItem
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("descriptionConfidence")]
    public float DescriptionConfidence { get; set; }

    [JsonPropertyName("quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("amountConfidence")]
    public float AmountConfidence { get; set; }
}
/// <summary>
/// Simple table data structure with rows and columns (no coordinates)
/// </summary>
public class SimpleTable
{
    public string TableName { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public int RowCount => Rows.Count;
    public int ColumnCount => Headers.Count;
}

/// <summary>
/// Extracted table structure (simplified)
/// </summary>
public class ExtractedTable
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    
    // Simple table representation
    public SimpleTable AsSimpleTable { get; set; } = new();
}

/// <summary>
/// Information about a document field
/// </summary>
public class DocumentFieldInfo
{
    public string Value { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string FieldType { get; set; } = string.Empty;
}

/// <summary>
/// Complete invoice document from Cosmos DB for LLM analysis
/// This class represents the full document structure without flattening
/// </summary>
public class InvoiceDocument
{
    [JsonPropertyName("id")]
    public string id { get; set; } = string.Empty;

    [JsonPropertyName("twinID")]
    public string TwinID { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("processedAt")]
    public DateTime ProcessedAt { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    // Basic invoice fields  
    [JsonPropertyName("vendorName")]
    public string VendorName { get; set; } = string.Empty;

    [JsonPropertyName("vendorNameConfidence")]
    public float VendorNameConfidence { get; set; }

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("customerNameConfidence")]
    public float CustomerNameConfidence { get; set; }

    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("invoiceDate")]
    public DateTime InvoiceDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    [JsonPropertyName("subTotal")]
    public decimal SubTotal { get; set; }

    [JsonPropertyName("subTotalConfidence")]
    public float SubTotalConfidence { get; set; }

    [JsonPropertyName("totalTax")]
    public decimal TotalTax { get; set; }

    [JsonPropertyName("invoiceTotal")]
    public decimal InvoiceTotal { get; set; }

    [JsonPropertyName("invoiceTotalConfidence")]
    public float InvoiceTotalConfidence { get; set; }

    [JsonPropertyName("lineItemsCount")]
    public int LineItemsCount { get; set; }

    [JsonPropertyName("tablesCount")]
    public int TablesCount { get; set; }

    [JsonPropertyName("rawFieldsCount")]
    public int RawFieldsCount { get; set; }

    // Complete structured invoice data with LineItems as a list  
    [JsonPropertyName("invoiceData")]
    public StructuredInvoiceData InvoiceData { get; set; } = new();

    // AI processing fields (optional, for enhanced analysis)  
    [JsonPropertyName("aiExecutiveSummaryHtml")]
    public string? AiExecutiveSummaryHtml { get; set; }

    [JsonPropertyName("aiExecutiveSummaryText")]
    public string? AiExecutiveSummaryText { get; set; }

    [JsonPropertyName("aiTextSummary")]
    public string? AiTextSummary { get; set; }

    [JsonPropertyName("aiHtmlOutput")]
    public string? AiHtmlOutput { get; set; }

    [JsonPropertyName("aiTextReport")]
    public string? AiTextReport { get; set; }

    [JsonPropertyName("aiTablesContent")]
    public string? AiTablesContent { get; set; }

    [JsonPropertyName("aiStructuredData")]
    public string? AiStructuredData { get; set; }

    [JsonPropertyName("aiProcessedText")]
    public string? AiProcessedText { get; set; }

    [JsonPropertyName("aiCompleteSummary")]
    public string? AiCompleteSummary { get; set; }

    [JsonPropertyName("aiCompleteInsights")]
    public string? AiCompleteInsights { get; set; }


    [JsonPropertyName("fileURL")]
    public string? FileURL { get; set; }
    /// <summary>
    /// Create InvoiceDocument from Cosmos DB dictionary
    /// </summary>
    public static InvoiceDocument FromCosmosDocument(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!) =>
            data.TryGetValue(key, out var value) && value != null 
                ? ConvertValue<T>(value, defaultValue) 
                : defaultValue;

        DateTime GetDateTime(string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return DateTime.MinValue;

            if (value is DateTime dateTime)
                return dateTime;

            if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(element.GetString(), out var parsed))
                    return parsed;
            }

            if (value is string str && DateTime.TryParse(str, out var parsedStr))
                return parsedStr;

            return DateTime.MinValue;
        }

        StructuredInvoiceData GetInvoiceData()
        {
            if (!data.TryGetValue("invoiceData", out var invoiceDataValue) || invoiceDataValue == null)
                return new StructuredInvoiceData();

            // ⭐ FIXED: Use robust parsing for different object types
            return ParseInvoiceDataFromAnyObject(invoiceDataValue);
        }

        static T ConvertValue<T>(object value, T defaultValue)
        {
            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(int))
                        return (T)(object)jsonElement.GetInt32();
                    if (type == typeof(decimal))
                        return (T)(object)jsonElement.GetDecimal();
                    if (type == typeof(float))
                        return (T)(object)(float)jsonElement.GetDecimal();
                    if (type == typeof(bool))
                        return (T)(object)jsonElement.GetBoolean();
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return new InvoiceDocument
        {
            id = GetValue<string>("id"),
            TwinID = GetValue<string>("TwinID"),
            FileName = GetValue<string>("fileName"),
            FilePath = GetValue<string>("filePath"),
            CreatedAt = GetDateTime("createdAt"),
            Source = GetValue<string>("source"),
            ProcessedAt = GetDateTime("processedAt"),
            Success = GetValue<bool>("success"),
            ErrorMessage = GetValue<string?>("errorMessage"),
            TotalPages = GetValue<int>("totalPages"),
            VendorName = GetValue<string>("vendorName"),
            VendorNameConfidence = GetValue<float>("vendorNameConfidence"),
            CustomerName = GetValue<string>("customerName"),
            CustomerNameConfidence = GetValue<float>("customerNameConfidence"),
            InvoiceNumber = GetValue<string>("invoiceNumber"),
            InvoiceDate = GetDateTime("invoiceDate"),
            DueDate = GetDateTime("dueDate"),
            SubTotal = GetValue<decimal>("subTotal"),
            SubTotalConfidence = GetValue<float>("subTotalConfidence"),
            TotalTax = GetValue<decimal>("totalTax"),
            InvoiceTotal = GetValue<decimal>("invoiceTotal"),
            InvoiceTotalConfidence = GetValue<float>("invoiceTotalConfidence"),
            LineItemsCount = GetValue<int>("lineItemsCount"),
            TablesCount = GetValue<int>("tablesCount"),
            RawFieldsCount = GetValue<int>("rawFieldsCount"),
            InvoiceData = GetInvoiceData(),
            
            // AI fields
            AiExecutiveSummaryHtml = GetValue<string?>("aiExecutiveSummaryHtml"),
            AiExecutiveSummaryText = GetValue<string?>("aiExecutiveSummaryText"),
            AiTextSummary = GetValue<string?>("aiTextSummary"),
            AiHtmlOutput = GetValue<string?>("aiHtmlOutput"),
            AiTextReport = GetValue<string?>("aiTextReport"),
            AiTablesContent = GetValue<string?>("aiTablesContent"),
            AiStructuredData = GetValue<string?>("aiStructuredData"),
            AiProcessedText = GetValue<string?>("aiProcessedText"),
            AiCompleteSummary = GetValue<string?>("aiCompleteSummary"),
            AiCompleteInsights = GetValue<string?>("aiCompleteInsights")
        };
    }

    /// <summary>
    /// Parse StructuredInvoiceData from JsonElement
    /// </summary>
    private static StructuredInvoiceData ParseInvoiceDataFromJson(JsonElement jsonElement)
    {
        var invoiceData = new StructuredInvoiceData();

        if (jsonElement.TryGetProperty("VendorName", out var vendorName))
            invoiceData.VendorName = vendorName.GetString() ?? string.Empty;

        if (jsonElement.TryGetProperty("VendorAddress", out var vendorAddress))
            invoiceData.VendorAddress = vendorAddress.GetString() ?? string.Empty;

        if (jsonElement.TryGetProperty("CustomerName", out var customerName))
            invoiceData.CustomerName = customerName.GetString() ?? string.Empty;

        if (jsonElement.TryGetProperty("CustomerAddress", out var customerAddress))
            invoiceData.CustomerAddress = customerAddress.GetString() ?? string.Empty;

        if (jsonElement.TryGetProperty("InvoiceNumber", out var invoiceNumber))
            invoiceData.InvoiceNumber = invoiceNumber.GetString() ?? string.Empty;

        if (jsonElement.TryGetProperty("InvoiceDate", out var invoiceDate) && DateTime.TryParse(invoiceDate.GetString(), out var parsedInvoiceDate))
            invoiceData.InvoiceDate = parsedInvoiceDate;

        if (jsonElement.TryGetProperty("DueDate", out var dueDate) && DateTime.TryParse(dueDate.GetString(), out var parsedDueDate))
            invoiceData.DueDate = parsedDueDate;

        if (jsonElement.TryGetProperty("SubTotal", out var subTotal))
            invoiceData.SubTotal = subTotal.GetDecimal();

        if (jsonElement.TryGetProperty("TotalTax", out var totalTax))
            invoiceData.TotalTax = totalTax.GetDecimal();

        if (jsonElement.TryGetProperty("InvoiceTotal", out var invoiceTotal))
            invoiceData.InvoiceTotal = invoiceTotal.GetDecimal();

        // Parse LineItems array
        if (jsonElement.TryGetProperty("LineItems", out var lineItemsElement) && lineItemsElement.ValueKind == JsonValueKind.Array)
        {
            invoiceData.LineItems = new List<InvoiceLineItem>();
            
            foreach (var itemElement in lineItemsElement.EnumerateArray())
            {
                var lineItem = new InvoiceLineItem();
                
                if (itemElement.TryGetProperty("Description", out var desc))
                    lineItem.Description = desc.GetString() ?? string.Empty;
                
                if (itemElement.TryGetProperty("DescriptionConfidence", out var descConf))
                    lineItem.DescriptionConfidence = (float)descConf.GetDecimal();
                
                if (itemElement.TryGetProperty("Quantity", out var qty))
                    lineItem.Quantity = qty.GetDouble();
                
                if (itemElement.TryGetProperty("UnitPrice", out var unitPrice))
                    lineItem.UnitPrice = unitPrice.GetDecimal();
                
                if (itemElement.TryGetProperty("Amount", out var amount))
                    lineItem.Amount = amount.GetDecimal();
                
                if (itemElement.TryGetProperty("AmountConfidence", out var amtConf))
                    lineItem.AmountConfidence = (float)amtConf.GetDecimal();
                
                invoiceData.LineItems.Add(lineItem);
            }
        }

        return invoiceData;
    }

    /// <summary>
    /// Convert to JSON string for LLM analysis (clean format)
    /// </summary>
    public string ToJsonForLLM()
    {
        var llmData = new
        {
            id,
            TwinID,
            FileName,
            FilePath,
            CreatedAt = CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ProcessedAt = ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            Success,
            TotalPages,
            VendorName,
            VendorNameConfidence,
            CustomerName,
            CustomerNameConfidence,
            InvoiceNumber,
            InvoiceDate = InvoiceData.InvoiceDate?.ToString("yyyy-MM-dd") ?? "",
            DueDate = InvoiceData.DueDate?.ToString("yyyy-MM-dd") ?? "",
            SubTotal,
            SubTotalConfidence,
            TotalTax,
            InvoiceTotal,
            InvoiceTotalConfidence,
            LineItemsCount,
            TablesCount,
            RawFieldsCount,
            InvoiceData = new
            {
                InvoiceData.VendorName,
                InvoiceData.VendorAddress,
                InvoiceData.CustomerName,
                InvoiceData.CustomerAddress,
                InvoiceData.InvoiceNumber,
                InvoiceDate = InvoiceData.InvoiceDate?.ToString("yyyy-MM-dd") ?? "",
                DueDate = InvoiceData.DueDate?.ToString("yyyy-MM-dd") ?? "",
                InvoiceData.SubTotal,
                InvoiceData.TotalTax,
                InvoiceData.InvoiceTotal,
                LineItems = InvoiceData.LineItems.Select(li => new
                {
                    li.Description,
                    li.DescriptionConfidence,
                    li.Quantity,
                    li.UnitPrice,
                    li.Amount,
                    li.AmountConfidence
                }).ToList()
            }
        };

        return JsonSerializer.Serialize(llmData, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Get LineItems that match a description filter
    /// </summary>
    public List<InvoiceLineItem> GetLineItemsContaining(string searchTerm, bool ignoreCase = true)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return InvoiceData.LineItems
            .Where(li => !string.IsNullOrEmpty(li.Description) && li.Description.Contains(searchTerm, comparison))
            .ToList();
    }

    /// <summary>
    /// Get LineItems with amount greater than specified value
    /// </summary>
    public List<InvoiceLineItem> GetLineItemsWithAmountGreaterThan(decimal minAmount)
    {
        return InvoiceData.LineItems
            .Where(li => li.Amount > minAmount)
            .ToList();
    }

    /// <summary>
    /// Get LineItems with amount between specified values
    /// </summary>
    public List<InvoiceLineItem> GetLineItemsWithAmountBetween(decimal minAmount, decimal maxAmount)
    {
        return InvoiceData.LineItems
            .Where(li => li.Amount >= minAmount && li.Amount <= maxAmount)
            .ToList();
    }

    /// <summary>
    /// Get summary statistics for the invoice
    /// </summary>
    public InvoiceSummary GetSummary()
    {
        return new InvoiceSummary
        {
            TotalLineItems = InvoiceData.LineItems.Count,
            TotalAmount = InvoiceTotal,
            LineItemAmountSum = InvoiceData.LineItems.Sum(li => li.Amount),
            HighestLineItemAmount = InvoiceData.LineItems.Any() ? InvoiceData.LineItems.Max(li => li.Amount) : 0,
            LowestLineItemAmount = InvoiceData.LineItems.Any() ? InvoiceData.LineItems.Min(li => li.Amount) : 0,
            AverageLineItemAmount = InvoiceData.LineItems.Any() ? InvoiceData.LineItems.Average(li => li.Amount) : 0,
            LineItemsWithDescription = InvoiceData.LineItems.Count(li => !string.IsNullOrEmpty(li.Description)),
            LineItemsWithZeroAmount = InvoiceData.LineItems.Count(li => li.Amount == 0),
            PositiveLineItems = InvoiceData.LineItems.Count(li => li.Amount > 0),
            NegativeLineItems = InvoiceData.LineItems.Count(li => li.Amount < 0)
        };
    }

    /// <summary>
    /// Parse StructuredInvoiceData from any object type (JsonElement, JObject, etc.)
    /// ROBUST: Handles both System.Text.Json and Newtonsoft.Json from Cosmos DB queries
    /// </summary>
    private static StructuredInvoiceData ParseInvoiceDataFromAnyObject(object invoiceDataValue)
    {
        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ParseInvoiceDataFromAnyObject - Input type: {invoiceDataValue?.GetType().Name}");
        
        if (invoiceDataValue == null)
            return new StructuredInvoiceData();

        // Handle JsonElement (System.Text.Json)
        if (invoiceDataValue is JsonElement jsonElementValue)
        {
            System.Diagnostics.Debug.WriteLine("🔍 DEBUG: Using JsonElement parser");
            return ParseInvoiceDataFromJson(jsonElementValue);
        }

        // Handle JObject/JToken (Newtonsoft.Json) - common in JOIN queries
        var objectType = invoiceDataValue.GetType();
        if (objectType.Name == "JObject" || objectType.Name.StartsWith("J") && objectType.Namespace?.Contains("Newtonsoft") == true)
        {
            System.Diagnostics.Debug.WriteLine("🔍 DEBUG: Using JObject parser for Newtonsoft.Json");
            return ParseInvoiceDataFromJObject(invoiceDataValue);
        }

        // Try converting through JSON serialization as fallback
        try
        {
            System.Diagnostics.Debug.WriteLine("🔍 DEBUG: Using serialization fallback");
            var serialized = JsonSerializer.Serialize(invoiceDataValue);
            var deserializedElement = JsonSerializer.Deserialize<JsonElement>(serialized);
            return ParseInvoiceDataFromJson(deserializedElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Serialization fallback failed: {ex.Message}");
            return new StructuredInvoiceData();
        }
    }

    /// <summary>
    /// Parse StructuredInvoiceData from JObject (Newtonsoft.Json)
    /// ROBUST: Handles complex JObject structures with childrenTokens
    /// </summary>
    private static StructuredInvoiceData ParseInvoiceDataFromJObject(object jObjectValue)
    {
        var invoiceData = new StructuredInvoiceData();
        
        try
        {
            var jObjectType = jObjectValue.GetType();
            var indexer = jObjectType.GetProperty("Item", new[] { typeof(string) });
            
            if (indexer == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ DEBUG: JObject indexer not found");
                return invoiceData;
            }

            // Helper function to get JToken value safely
            T GetJObjectValue<T>(string propertyName, T defaultValue = default!)
            {
                try
                {
                    var tokenValue = indexer.GetValue(jObjectValue, new object[] { propertyName });
                    if (tokenValue != null)
                    {
                        var valueProperty = tokenValue.GetType().GetProperty("Value");
                        if (valueProperty != null)
                        {
                            var value = valueProperty.GetValue(tokenValue);
                            if (value != null)
                            {
                                if (typeof(T) == typeof(string))
                                    return (T)(object)(value.ToString() ?? defaultValue?.ToString() ?? "");
                                
                                return (T)Convert.ChangeType(value, typeof(T));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Error getting JObject value for {propertyName}: {ex.Message}");
                }
                return defaultValue;
            }

            // Parse basic invoice data fields
            invoiceData.VendorName = GetJObjectValue<string>("VendorName", "");
            invoiceData.VendorAddress = GetJObjectValue<string>("VendorAddress", "");
            invoiceData.CustomerName = GetJObjectValue<string>("CustomerName", "");
            invoiceData.CustomerAddress = GetJObjectValue<string>("CustomerAddress", "");
            invoiceData.InvoiceNumber = GetJObjectValue<string>("InvoiceNumber", "");
            
            // Parse dates
            var invoiceDateStr = GetJObjectValue<string>("InvoiceDate", "");
            if (DateTime.TryParse(invoiceDateStr, out var parsedInvoiceDate))
                invoiceData.InvoiceDate = parsedInvoiceDate;
                
            var dueDateStr = GetJObjectValue<string>("DueDate", "");
            if (DateTime.TryParse(dueDateStr, out var parsedDueDate))
                invoiceData.DueDate = parsedDueDate;

            // Parse financial fields
            invoiceData.SubTotal = GetJObjectValue<decimal>("SubTotal", 0m);
            invoiceData.TotalTax = GetJObjectValue<decimal>("TotalTax", 0m);
            invoiceData.InvoiceTotal = GetJObjectValue<decimal>("InvoiceTotal", 0m);

            // ⭐ CRITICAL: Parse ALL LineItems from JObject
            try
            {
                var lineItemsToken = indexer.GetValue(jObjectValue, new object[] { "LineItems" });
                
                if (lineItemsToken != null)
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Found LineItems in JObject, type: {lineItemsToken.GetType().Name}");
                    
                    var lineItemsTokenType = lineItemsToken.GetType();
                    if (lineItemsTokenType.Name == "JArray")
                    {
                        // Parse JArray using reflection
                        var countProperty = lineItemsTokenType.GetProperty("Count");
                        var itemIndexer = lineItemsTokenType.GetProperty("Item", new[] { typeof(int) });
                        
                        if (countProperty != null && itemIndexer != null)
                        {
                            var count = (int)(countProperty.GetValue(lineItemsToken) ?? 0);
                            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: JArray has {count} LineItems - PARSING ALL");
                            
                            invoiceData.LineItems = new List<InvoiceLineItem>();
                            
                            // ⭐ PARSE ALL ITEMS, NO LIMIT TO 10
                            for (int i = 0; i < count; i++)
                            {
                                var itemToken = itemIndexer.GetValue(lineItemsToken, new object[] { i });
                                
                                if (itemToken != null)
                                {
                                    var lineItem = ParseLineItemFromJTokenStatic(itemToken);
                                    if (lineItem != null)
                                    {
                                        invoiceData.LineItems.Add(lineItem);
                                    }
                                }
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Successfully parsed {invoiceData.LineItems.Count} LineItems from JObject");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ DEBUG: LineItems not found in JObject");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing LineItems from JObject: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing JObject: {ex.Message}");
        }
        
        return invoiceData;
    }

    /// <summary>
    /// Parse individual LineItem from JToken with robust error handling
    /// </summary>
    private static InvoiceLineItem? ParseLineItemFromJTokenStatic(object jToken)
    {
        try
        {
            var lineItem = new InvoiceLineItem();
            var jTokenType = jToken.GetType();
            var indexer = jTokenType.GetProperty("Item", new[] { typeof(string) });
            
            if (indexer == null)
                return null;

            // Helper function to get value from JToken safely
            T GetJTokenValue<T>(string propertyName, T defaultValue = default!)
            {
                try
                {
                    var tokenValue = indexer.GetValue(jToken, new object[] { propertyName });
                    if (tokenValue != null)
                    {
                        var valueProperty = tokenValue.GetType().GetProperty("Value");
                        if (valueProperty != null)
                        {
                            var value = valueProperty.GetValue(tokenValue);
                            if (value != null)
                            {
                                if (typeof(T) == typeof(string))
                                    return (T)(object)(value.ToString() ?? defaultValue?.ToString() ?? "");
                                
                                return (T)Convert.ChangeType(value, typeof(T));
                            }
                        }
                    }
                }
                catch
                {
                    // Return default value on any error
                }
                return defaultValue;
            }
            
            lineItem.Description = GetJTokenValue<string>("Description", "");
            lineItem.DescriptionConfidence = GetJTokenValue<float>("DescriptionConfidence", 0f);
            lineItem.Quantity = GetJTokenValue<double>("Quantity", 0.0);
            lineItem.UnitPrice = GetJTokenValue<decimal>("UnitPrice", 0m);
            lineItem.Amount = GetJTokenValue<decimal>("Amount", 0m);
            lineItem.AmountConfidence = GetJTokenValue<float>("AmountConfidence", 0f);
            
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Parsed LineItem - Description: '{lineItem.Description}', Amount: {lineItem.Amount}");
            
            return lineItem;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ DEBUG: Error parsing JToken LineItem: {ex.Message}");
            return null;
        }
    }
}
/// <summary>
/// Summary statistics for an invoice
/// </summary>
public class InvoiceSummary
{
    public int TotalLineItems { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal LineItemAmountSum { get; set; }
    public decimal HighestLineItemAmount { get; set; }
    public decimal LowestLineItemAmount { get; set; }
    public decimal AverageLineItemAmount { get; set; }
    public int LineItemsWithDescription { get; set; }
    public int LineItemsWithZeroAmount { get; set; }
    public int PositiveLineItems { get; set; }
    public int NegativeLineItems { get; set; }
}