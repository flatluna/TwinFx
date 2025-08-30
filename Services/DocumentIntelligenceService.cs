﻿using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using TwinFx.Services;

namespace TwinFx.Services;

/// <summary>
/// Azure Document Intelligence Service for processing invoices and documents
/// ========================================================================
/// 
/// Service specializing in extracting structured data from invoices using Azure AI Document Intelligence.
/// Complements ProcessDocumentDataAgent by providing OCR and structured field extraction.
/// 
/// Features:
/// - Azure AI Document Intelligence integration
/// - Prebuilt invoice model support
/// - Structured field extraction with confidence scores
/// - Multiple document format support (PDF, images)
/// - Comprehensive error handling and logging
/// - Integration with TwinFx ecosystem
/// 
/// Capabilities:
/// 1. Invoice data extraction with prebuilt models
/// 2. Custom document analysis
/// 3. Table and field extraction
/// 4. Confidence scoring for reliability
/// 5. Multiple output formats
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class DocumentIntelligenceService
{
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private readonly IConfiguration _configuration;
    private readonly DocumentIntelligenceClient _client;
    private readonly DataLakeClientFactory _dataLakeFactory;
    private readonly CosmosDbTwinProfileService _cosmosService;

    public DocumentIntelligenceService(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger<DocumentIntelligenceService>();
        _configuration = configuration;

        try
        {
            // Get configuration values
            var endpoint = GetConfigurationValue("DocumentIntelligence:Endpoint");
            var apiKey = GetConfigurationValue("DocumentIntelligence:ApiKey");

            _logger.LogInformation("🚀 Initializing Document Intelligence Service");
            _logger.LogInformation($"🔧 Using endpoint: {endpoint}");

            // Initialize Azure Document Intelligence client
            var credential = new AzureKeyCredential(apiKey);
            _client = new DocumentIntelligenceClient(new Uri(endpoint), credential);

            // Initialize DataLake client factory
            _dataLakeFactory = new DataLakeClientFactory(loggerFactory, _configuration);

            // Initialize Cosmos DB service
            var cosmosLogger = loggerFactory.CreateLogger<CosmosDbTwinProfileService>();
            _cosmosService = new CosmosDbTwinProfileService(cosmosLogger, _configuration);

            _logger.LogInformation("✅ Document Intelligence Service initialized successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Document Intelligence Service");
            throw;
        }
    }

    /// <summary>
    /// Extract structured data from invoice using prebuilt invoice model
    /// </summary>
    /// <param name="containerName">Container name where the document is stored</param>
    /// <param name="filePath">Path to the document within the container</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Structured invoice data with confidence scores</returns>
    public async Task<InvoiceAnalysisResult> ExtractInvoiceDataAsync(string containerName, string filePath, string fileName)
    {
        _logger.LogInformation($"📄 Starting invoice data extraction from container: {containerName}, path: {filePath}, file: {fileName}");

        try
        {
            // Step 1: Test DataLake configuration first
            _logger.LogInformation("🔍 Step 1: Testing DataLake configuration...");
            await TestDataLakeConfigurationAsync(containerName);

            // Step 2: Create DataLake client for the container
            _logger.LogInformation("🔧 Step 2: Creating DataLake client...");
            var dataLakeClient = _dataLakeFactory.CreateClient(containerName);
            
            // Step 3: Test the connection
            _logger.LogInformation("🧪 Step 3: Testing DataLake connection...");
            var connectionSuccess = await dataLakeClient.TestConnectionAsync();
            if (!connectionSuccess)
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "DataLake connection test failed. Please check Azure Storage credentials.",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}/{fileName}"
                };
            }

            filePath = filePath + "/" + fileName;
            
            // Step 4: Get SAS URL for the document
            _logger.LogInformation("🔗 Step 4: Generating SAS URL...");
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(1));
            
            if (string.IsNullOrEmpty(sasUrl))
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = $"Could not generate SAS URL for file: {filePath} in container: {containerName}",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}"
                };
            }

            _logger.LogInformation($"🔗 Generated SAS URL for document: {filePath}");

            // Create analyze document content for URI
            var analyzeRequest = new AnalyzeDocumentContent
            {
                UrlSource = new Uri(sasUrl)
            };

            // Analyze document using prebuilt invoice model
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-invoice", 
                analyzeRequest);

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Documents.Count} document(s)");

            // Process the first document (invoices typically have one document)
            if (result.Documents.Count == 0)
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "No documents found in the provided file",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}"
                };
            }

            var document = result.Documents[0];
            var invoiceData = ExtractInvoiceFields(document);

            // Also extract tables if present
            var tables = ExtractTables(result);

            var analysisResult = new InvoiceAnalysisResult
            {
                Success = true,
                InvoiceData = invoiceData,
                Tables = tables,
                RawDocumentFields = document.Fields.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new DocumentFieldInfo
                    {
                        Value = GetFieldValue(kvp.Value),
                        Confidence = kvp.Value.Confidence ?? 0.0f,
                        FieldType = GetFieldType(kvp.Value)
                    }),
                ProcessedAt = DateTime.UtcNow,
                SourceUri = $"{containerName}/{filePath}",
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Invoice extraction completed successfully with {invoiceData.LineItems.Count} line items");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error extracting invoice data from {containerName}/{filePath}");

            return new InvoiceAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = $"{containerName}/{filePath}"
            };
        }
    }

    /// <summary>
    /// Extract structured data from invoice using file stream
    /// </summary>
    /// <param name="documentStream">Stream containing the invoice document</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Structured invoice data with confidence scores</returns>
    public async Task<InvoiceAnalysisResult> ExtractInvoiceDataAsync(Stream documentStream, string fileName)
    {
        _logger.LogInformation($"📄 Starting invoice data extraction from file: {fileName}");

        try
        {
            // Analyze document using prebuilt invoice model
            var content = BinaryData.FromStream(documentStream);
            var analyzeRequest = new AnalyzeDocumentContent()
            {
                Base64Source = content
            };

            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-invoice", 
                analyzeRequest);

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Documents.Count} document(s)");

            // Process the first document (invoices typically have one document)
            if (result.Documents.Count == 0)
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "No documents found in the provided file",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = fileName
                };
            }

            var document = result.Documents[0];
            var invoiceData = ExtractInvoiceFields(document);

            // Also extract tables if present
            var tables = ExtractTables(result);

            var analysisResult = new InvoiceAnalysisResult
            {
                Success = true,
                InvoiceData = invoiceData,
                Tables = tables,
                RawDocumentFields = document.Fields.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new DocumentFieldInfo
                    {
                        Value = GetFieldValue(kvp.Value),
                        Confidence = kvp.Value.Confidence ?? 0.0f,
                        FieldType = GetFieldType(kvp.Value)
                    }),
                ProcessedAt = DateTime.UtcNow,
                SourceUri = fileName,
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Invoice extraction completed successfully with {invoiceData.LineItems.Count} line items");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error extracting invoice data from {fileName}");

            return new InvoiceAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = fileName
            };
        }
    }

    /// <summary>
    /// Analyze any document type using layout model
    /// </summary>
    /// <param name="documentUri">URI to the document</param>
    /// <returns>General document analysis result</returns>
    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(Uri documentUri)
    {
        _logger.LogInformation($"📄 Starting general document analysis from URI: {documentUri}");

        try
        {
            // Create analyze document content for URI
            var analyzeRequest = new AnalyzeDocumentContent
            {
                UrlSource = documentUri
            };

            // Analyze document using layout model for general structure
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-layout", 
                analyzeRequest);

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Pages?.Count ?? 0} page(s)");

            // Extract text content and tables
            var textContent = ExtractTextContent(result);
            var tables = ExtractTables(result);

            var analysisResult = new DocumentAnalysisResult
            {
                Success = true,
                TextContent = textContent,
                Tables = tables,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentUri.ToString(),
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Document analysis completed successfully with {tables.Count} table(s)");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error analyzing document from {documentUri}");

            return new DocumentAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentUri.ToString()
            };
        }
    }

    /// <summary>
    /// Extract invoice-specific fields from analyzed document
    /// </summary>
    /// <param name="document">Analyzed document from Document Intelligence</param>
    /// <returns>Structured invoice data</returns>
    private StructuredInvoiceData ExtractInvoiceFields(AnalyzedDocument document)
    {
        var invoiceData = new StructuredInvoiceData();

        try
        {
            // Extract vendor information
            if (document.Fields.TryGetValue("VendorName", out var vendorNameField))
            {
                invoiceData.VendorName = GetStringFieldValue(vendorNameField);
                invoiceData.VendorNameConfidence = vendorNameField.Confidence ?? 0.0f;
            }

            if (document.Fields.TryGetValue("VendorAddress", out var vendorAddressField))
            {
                invoiceData.VendorAddress = GetStringFieldValue(vendorAddressField);
            }

            // Extract customer information
            if (document.Fields.TryGetValue("CustomerName", out var customerNameField))
            {
                invoiceData.CustomerName = GetStringFieldValue(customerNameField);
                invoiceData.CustomerNameConfidence = customerNameField.Confidence ?? 0.0f;
            }

            if (document.Fields.TryGetValue("CustomerAddress", out var customerAddressField))
            {
                invoiceData.CustomerAddress = GetStringFieldValue(customerAddressField);
            }

            // Extract invoice metadata
            if (document.Fields.TryGetValue("InvoiceId", out var invoiceIdField))
            {
                invoiceData.InvoiceNumber = GetStringFieldValue(invoiceIdField);
            }

            if (document.Fields.TryGetValue("InvoiceDate", out var invoiceDateField))
            {
                invoiceData.InvoiceDate = GetDateFieldValue(invoiceDateField);
            }

            if (document.Fields.TryGetValue("DueDate", out var dueDateField))
            {
                invoiceData.DueDate = GetDateFieldValue(dueDateField);
            }

            // Extract financial totals
            if (document.Fields.TryGetValue("SubTotal", out var subTotalField))
            {
                invoiceData.SubTotal = GetCurrencyFieldValue(subTotalField);
                invoiceData.SubTotalConfidence = subTotalField.Confidence ?? 0.0f;
            }

            if (document.Fields.TryGetValue("TotalTax", out var totalTaxField))
            {
                invoiceData.TotalTax = GetCurrencyFieldValue(totalTaxField);
            }

            if (document.Fields.TryGetValue("InvoiceTotal", out var invoiceTotalField))
            {
                invoiceData.InvoiceTotal = GetCurrencyFieldValue(invoiceTotalField);
                invoiceData.InvoiceTotalConfidence = invoiceTotalField.Confidence ?? 0.0f;
            }

            // Extract line items
            if (document.Fields.TryGetValue("Items", out var itemsField) && IsListField(itemsField))
            {
                foreach (var itemField in GetFieldList(itemsField))
                {
                    if (IsDictionaryField(itemField))
                    {
                        var lineItem = ExtractLineItem(GetFieldDictionary(itemField));
                        if (lineItem != null)
                        {
                            invoiceData.LineItems.Add(lineItem);
                        }
                    }
                }
            }

            _logger.LogInformation($"📊 Extracted {invoiceData.LineItems.Count} line items from invoice");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting some invoice fields");
        }

        return invoiceData;
    }

    /// <summary>
    /// Extract line item from document field dictionary
    /// </summary>
    /// <param name="itemFields">Dictionary of fields for a line item</param>
    /// <returns>Structured line item data</returns>
    private InvoiceLineItem? ExtractLineItem(IReadOnlyDictionary<string, DocumentField> itemFields)
    {
        try
        {
            var lineItem = new InvoiceLineItem();

            if (itemFields.TryGetValue("Description", out var descriptionField))
            {
                lineItem.Description = GetStringFieldValue(descriptionField);
                lineItem.DescriptionConfidence = descriptionField.Confidence ?? 0.0f;
            }

            if (itemFields.TryGetValue("Quantity", out var quantityField))
            {
                lineItem.Quantity = GetNumberFieldValue(quantityField);
            }

            if (itemFields.TryGetValue("UnitPrice", out var unitPriceField))
            {
                lineItem.UnitPrice = GetCurrencyFieldValue(unitPriceField);
            }

            if (itemFields.TryGetValue("Amount", out var amountField))
            {
                lineItem.Amount = GetCurrencyFieldValue(amountField);
                lineItem.AmountConfidence = amountField.Confidence ?? 0.0f;
            }

            // Only return line item if we have at least description or amount
            if (!string.IsNullOrEmpty(lineItem.Description) || lineItem.Amount > 0)
            {
                return lineItem;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting line item");
        }

        return null;
    }

    /// <summary>
    /// Extract tables from the document analysis result
    /// </summary>
    /// <param name="result">Document analysis result</param>
    /// <returns>List of extracted tables</returns>
    private List<ExtractedTable> ExtractTables(AnalyzeResult result)
    {
        var tables = new List<ExtractedTable>();

        try
        {
            if (result.Tables != null)
            {
                for (int tableIndex = 0; tableIndex < result.Tables.Count; tableIndex++)
                {
                    var table = result.Tables[tableIndex];
                    var extractedTable = new ExtractedTable
                    {
                        RowCount = table.RowCount,
                        ColumnCount = table.ColumnCount
                    };

                    // Create simple table representation directly from Azure table
                    extractedTable.AsSimpleTable = ConvertAzureTableToSimple(table, tableIndex);

                    tables.Add(extractedTable);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting tables");
        }

        return tables;
    }

    /// <summary>
    /// Convert Azure table to SimpleTable format for easy reading
    /// </summary>
    /// <param name="azureTable">Azure table from Document Intelligence</param>
    /// <param name="tableIndex">Index of table for naming</param>
    /// <returns>Simple table with rows and columns</returns>
    private static SimpleTable ConvertAzureTableToSimple(DocumentTable azureTable, int tableIndex)
    {
        var simpleTable = new SimpleTable
        {
            TableName = $"Table {tableIndex + 1}"
        };

        try
        {
            if (azureTable.Cells.Count == 0)
            {
                return simpleTable;
            }

            // Create a 2D array to organize cells by position
            var grid = new string[azureTable.RowCount, azureTable.ColumnCount];
            
            // Fill the grid with cell content directly from Azure table
            foreach (var cell in azureTable.Cells)
            {
                if (cell.RowIndex < azureTable.RowCount && cell.ColumnIndex < azureTable.ColumnCount)
                {
                    grid[cell.RowIndex, cell.ColumnIndex] = cell.Content ?? string.Empty;
                }
            }

            // Extract headers (first row)
            if (azureTable.RowCount > 0)
            {
                for (int col = 0; col < azureTable.ColumnCount; col++)
                {
                    var headerText = grid[0, col] ?? $"Column {col + 1}";
                    simpleTable.Headers.Add(headerText);
                }
            }

            // Extract data rows (skip first row if it's headers)
            int startRow = azureTable.RowCount > 1 ? 1 : 0;
            for (int row = startRow; row < azureTable.RowCount; row++)
            {
                var rowData = new List<string>();
                for (int col = 0; col < azureTable.ColumnCount; col++)
                {
                    rowData.Add(grid[row, col] ?? string.Empty);
                }
                
                // Only add row if it has some content
                if (rowData.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    simpleTable.Rows.Add(rowData);
                }
            }

            // If no headers were detected, use the first data row as headers
            if (simpleTable.Headers.All(h => string.IsNullOrWhiteSpace(h)) && simpleTable.Rows.Count > 0)
            {
                simpleTable.Headers = simpleTable.Rows[0];
                simpleTable.Rows.RemoveAt(0);
            }
        }
        catch (Exception)
        {
            // Return empty table structure if conversion fails
            simpleTable.Headers = Enumerable.Range(1, azureTable.ColumnCount)
                .Select(i => $"Column {i}")
                .ToList();
        }

        return simpleTable;
    }

    /// <summary>
    /// Extract text content from document analysis result
    /// </summary>
    /// <param name="result">Document analysis result</param>
    /// <returns>Extracted text content</returns>
    private string ExtractTextContent(AnalyzeResult result)
    {
        try
        {
            if (!string.IsNullOrEmpty(result.Content))
            {
                return result.Content;
            }

            // Fallback: concatenate text from pages
            var textBuilder = new StringBuilder();
            if (result.Pages != null)
            {
                foreach (var page in result.Pages)
                {
                    if (page.Lines != null)
                    {
                        foreach (var line in page.Lines)
                        {
                            textBuilder.AppendLine(line.Content);
                        }
                    }
                }
            }

            return textBuilder.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting text content");
            return string.Empty;
        }
    }

    /// <summary>
    /// Helper method to get string value from document field
    /// </summary>
    private static string GetStringFieldValue(DocumentField field)
    {
        if (field.ValueString != null)
            return field.ValueString;
        
        return field.Content ?? string.Empty;
    }

    /// <summary>
    /// Helper method to get date value from document field
    /// </summary>
    private static DateTime? GetDateFieldValue(DocumentField field)
    {
        return field.ValueDate?.DateTime;
    }

    /// <summary>
    /// Helper method to get currency value from document field
    /// </summary>
    private static decimal GetCurrencyFieldValue(DocumentField field)
    {
        return field.ValueCurrency?.Amount is not null ? (decimal)field.ValueCurrency.Amount : 0m;
    }

    /// <summary>
    /// Helper method to get number value from document field
    /// </summary>
    private static double GetNumberFieldValue(DocumentField field)
    {
        // Try different number types
        if (field.ValueDouble != null) return field.ValueDouble.Value;
        
        // Try to parse from string content if available
        if (!string.IsNullOrEmpty(field.Content) && double.TryParse(field.Content, out var parsed))
        {
            return parsed;
        }
        
        return 0.0;
    }

    /// <summary>
    /// Helper method to check if field is a list
    /// </summary>
    private static bool IsListField(DocumentField field)
    {
        return field.ValueList != null && field.ValueList.Count > 0;
    }

    /// <summary>
    /// Helper method to get list from field
    /// </summary>
    private static IEnumerable<DocumentField> GetFieldList(DocumentField field)
    {
        return field.ValueList ?? Array.Empty<DocumentField>();
    }

    /// <summary>
    /// Helper method to check if field is a dictionary
    /// </summary>
    private static bool IsDictionaryField(DocumentField field)
    {
        return field.ValueDictionary != null;
    }

    /// <summary>
    /// Helper method to get dictionary from field
    /// </summary>
    private static IReadOnlyDictionary<string, DocumentField> GetFieldDictionary(DocumentField field)
    {
        return field.ValueDictionary ?? new Dictionary<string, DocumentField>();
    }

    /// <summary>
    /// Helper method to get field type as string
    /// </summary>
    private static string GetFieldType(DocumentField field)
    {
        if (field.ValueString != null) return "String";
        if (field.ValueDate != null) return "Date";
        if (field.ValueTime != null) return "Time";
        if (field.ValuePhoneNumber != null) return "PhoneNumber";
        if (field.ValueDouble != null) return "Double";
        if (field.ValueCurrency != null) return "Currency";
        if (field.ValueAddress != null) return "Address";
        if (field.ValueBoolean != null) return "Boolean";
        if (field.ValueCountryRegion != null) return "CountryRegion";
        if (field.ValueList != null) return "List";
        if (field.ValueDictionary != null) return "Dictionary";
        return "Unknown";
    }

    /// <summary>
    /// Helper method to get generic field value as string
    /// </summary>
    private static string GetFieldValue(DocumentField field)
    {
        if (field.ValueString != null) return field.ValueString;
        if (field.ValueDate != null) return field.ValueDate.Value.ToString("yyyy-MM-dd");
        if (field.ValueTime != null) return field.ValueTime.Value.ToString();
        if (field.ValuePhoneNumber != null) return field.ValuePhoneNumber;
        if (field.ValueDouble != null) return field.ValueDouble.Value.ToString("F2");
        if (field.ValueCurrency != null) return $"{field.ValueCurrency.CurrencySymbol ?? "$"}{field.ValueCurrency.Amount:F2}";
        if (field.ValueAddress != null) return field.ValueAddress.ToString() ?? string.Empty;
        if (field.ValueBoolean != null) return field.ValueBoolean.Value.ToString();
        if (field.ValueCountryRegion != null) return field.ValueCountryRegion;
        if (field.ValueList != null) return $"List with {field.ValueList.Count} items";
        if (field.ValueDictionary != null) return $"Dictionary with {field.ValueDictionary.Count} fields";
        
        return field.Content ?? string.Empty;
    }

    /// <summary>
    /// Get configuration value with error handling
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <returns>Configuration value</returns>
    private string GetConfigurationValue(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is not set");
        }
        return value;
    }

    /// <summary>
    /// Get simple table data as a formatted string for easy reading
    /// </summary>
    /// <param name="tables">List of extracted tables</param>
    /// <returns>Formatted string representation of all tables</returns>
    public static string GetSimpleTablesAsText(List<ExtractedTable> tables)
    {
        var result = new StringBuilder();

        foreach (var table in tables)
        {
            result.AppendLine($"=== {table.AsSimpleTable.TableName} ===");
            result.AppendLine($"Rows: {table.AsSimpleTable.RowCount}, Columns: {table.AsSimpleTable.ColumnCount}");
            result.AppendLine();

            // Add headers
            if (table.AsSimpleTable.Headers.Count > 0)
            {
                result.AppendLine("Headers:");
                result.AppendLine(string.Join(" | ", table.AsSimpleTable.Headers));
                result.AppendLine(new string('-', string.Join(" | ", table.AsSimpleTable.Headers).Length));
            }

            // Add data rows
            foreach (var row in table.AsSimpleTable.Rows)
            {
                result.AppendLine(string.Join(" | ", row));
            }

            result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// Get simple table data as JSON for easy processing
    /// </summary>
    /// <param name="tables">List of extracted tables</param>
    /// <returns>JSON representation of simple tables</returns>
    public static string GetSimpleTablesAsJson(List<ExtractedTable> tables)
    {
        var simpleTables = tables.Select(t => t.AsSimpleTable).ToList();
        return JsonSerializer.Serialize(simpleTables, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Test Azure Storage connection with detailed diagnostics
    /// </summary>
    /// <param name="containerName">Container name to test</param>
    /// <returns>Diagnostic result with connection status and details</returns>
    public async Task<StorageDiagnosticResult> TestStorageConnectionAsync(string containerName)
    {
        var result = new StorageDiagnosticResult { ContainerName = containerName };
        
        try
        {
            _logger.LogInformation("🧪 Starting comprehensive Azure Storage connection test...");
            
            // Step 1: Check configuration
            var accountName = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_NAME");
            var accountKey = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_KEY");
            
            result.AccountName = accountName ?? "NULL";
            result.HasAccountKey = !string.IsNullOrWhiteSpace(accountKey);
            result.AccountKeyLength = accountKey?.Length ?? 0;
            
            _logger.LogInformation($"📋 Configuration Check:");
            _logger.LogInformation($"   • Account Name: {result.AccountName}");
            _logger.LogInformation($"   • Has Account Key: {result.HasAccountKey}");
            _logger.LogInformation($"   • Account Key Length: {result.AccountKeyLength}");
            
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
            {
                result.Success = false;
                result.ErrorMessage = "Missing Azure Storage credentials in configuration";
                result.Suggestions.Add("Check AZURE_STORAGE_ACCOUNT_NAME and AZURE_STORAGE_ACCOUNT_KEY in local.settings.json");
                return result;
            }
            
            // Step 2: Test DataLake client creation
            _logger.LogInformation("🔧 Step 2: Testing DataLake client creation...");
            try
            {
                var dataLakeClient = _dataLakeFactory.CreateClient(containerName);
                result.ClientCreated = true;
                _logger.LogInformation("✅ DataLake client created successfully");
                
                // Step 3: Test connection
                _logger.LogInformation("🔗 Step 3: Testing Azure Storage connection...");
                var connectionSuccess = await dataLakeClient.TestConnectionAsync();
                result.ConnectionTested = true;
                result.ConnectionSuccess = connectionSuccess;
                
                if (connectionSuccess)
                {
                    _logger.LogInformation("✅ Azure Storage connection successful!");
                    result.Success = true;
                    
                    // Step 4: Test container operations
                    _logger.LogInformation("📦 Step 4: Testing container operations...");
                    try
                    {
                        var files = await dataLakeClient.ListFilesAsync();
                        result.ContainerAccessible = true;
                        result.FilesFound = files?.Count ?? 0;
                        _logger.LogInformation($"✅ Container accessible, found {result.FilesFound} files");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Container operations failed");
                        result.ContainerAccessible = false;
                        result.Suggestions.Add($"Container '{containerName}' may not exist or be accessible");
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Azure Storage connection test failed";
                    result.Suggestions.Add("Verify Azure Storage account name and key are correct");
                    result.Suggestions.Add("Check if storage account exists and is accessible");
                    result.Suggestions.Add("Verify network connectivity to Azure Storage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DataLake client creation failed");
                result.ClientCreated = false;
                result.Success = false;
                result.ErrorMessage = ex.Message;
                
                if (ex.Message.Contains("account information"))
                {
                    result.Suggestions.Add("Azure Storage credentials are invalid or expired");
                    result.Suggestions.Add("Verify the storage account key in local.settings.json");
                    result.Suggestions.Add("Check if the storage account name is correct");
                }
                else if (ex.Message.Contains("authentication"))
                {
                    result.Suggestions.Add("Authentication failed - check storage account key");
                }
                else
                {
                    result.Suggestions.Add("Unexpected error during client creation");
                }
            }
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Storage diagnostic test failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Suggestions.Add("Unexpected error during diagnostic test");
        }
        
        // Log final results
        _logger.LogInformation("📊 Storage Diagnostic Results:");
        _logger.LogInformation($"   🎯 Overall Success: {result.Success}");
        _logger.LogInformation($"   🔧 Client Created: {result.ClientCreated}");
        _logger.LogInformation($"   🔗 Connection Tested: {result.ConnectionTested}");
        _logger.LogInformation($"   ✅ Connection Success: {result.ConnectionSuccess}");
        _logger.LogInformation($"   📦 Container Accessible: {result.ContainerAccessible}");
        _logger.LogInformation($"   📄 Files Found: {result.FilesFound}");
        
        if (!result.Success)
        {
            _logger.LogError($"❌ Error: {result.ErrorMessage}");
            _logger.LogInformation("💡 Suggestions:");
            foreach (var suggestion in result.Suggestions)
            {
                _logger.LogInformation($"   • {suggestion}");
            }
        }
        
        return result;
    }

    /// <summary>
    /// Test DataLake configuration and provide diagnostic information
    /// </summary>
    /// <param name="containerName">Container name to test</param>
    private async Task TestDataLakeConfigurationAsync(string containerName)
    {
        try
        {
            _logger.LogInformation("🔍 Diagnosing DataLake configuration...");
            
            // Check configuration values
            var accountName = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_NAME");
            var accountKey = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_KEY");
            
            _logger.LogInformation($"📋 Configuration Analysis:");
            _logger.LogInformation($"   • Account Name: {accountName ?? "NULL"}");
            _logger.LogInformation($"   • Account Key Length: {accountKey?.Length ?? 0} characters");
            _logger.LogInformation($"   • Container Name: {containerName}");
            
            if (string.IsNullOrWhiteSpace(accountName))
            {
                _logger.LogError("❌ AZURE_STORAGE_ACCOUNT_NAME is missing or empty");
            }
            
            if (string.IsNullOrWhiteSpace(accountKey))
            {
                _logger.LogError("❌ AZURE_STORAGE_ACCOUNT_KEY is missing or empty");
            }
            
            if (accountKey?.Length < 50)
            {
                _logger.LogWarning("⚠️ Account key seems too short - might be invalid");
            }
            
            _logger.LogInformation("✅ Configuration diagnostic completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during configuration diagnostic");
        }
    }
}

/// <summary>
/// Result of invoice analysis using Document Intelligence
/// </summary>
public class InvoiceAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public StructuredInvoiceData InvoiceData { get; set; } = new();
    public List<ExtractedTable> Tables { get; set; } = new();
    public Dictionary<string, DocumentFieldInfo> RawDocumentFields { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
    public string SourceUri { get; set; } = string.Empty;
    public int TotalPages { get; set; }
}

/// <summary>
/// Result of general document analysis
/// </summary>
public class DocumentAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public List<ExtractedTable> Tables { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
    public string SourceUri { get; set; } = string.Empty;
    public int TotalPages { get; set; }
}

/// <summary>
/// Structured invoice data extracted from Document Intelligence
/// </summary>
public class StructuredInvoiceData
{
    // Vendor Information
    public string VendorName { get; set; } = string.Empty;
    public float VendorNameConfidence { get; set; }
    public string VendorAddress { get; set; } = string.Empty;

    // Customer Information
    public string CustomerName { get; set; } = string.Empty;
    public float CustomerNameConfidence { get; set; }
    public string CustomerAddress { get; set; } = string.Empty;

    // Invoice Metadata
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }

    // Financial Totals
    public decimal SubTotal { get; set; }
    public float SubTotalConfidence { get; set; }
    public decimal TotalTax { get; set; }
    public decimal InvoiceTotal { get; set; }
    public float InvoiceTotalConfidence { get; set; }

    // Line Items
    public List<InvoiceLineItem> LineItems { get; set; } = new();
}

/// <summary>
/// Individual line item from invoice
/// </summary>
public class InvoiceLineItem
{
    public string Description { get; set; } = string.Empty;
    public float DescriptionConfidence { get; set; }
    public double Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
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
/// Result of Azure Storage diagnostic test
/// </summary>
public class StorageDiagnosticResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string ContainerName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool HasAccountKey { get; set; }
    public int AccountKeyLength { get; set; }
    public bool ClientCreated { get; set; }
    public bool ConnectionTested { get; set; }
    public bool ConnectionSuccess { get; set; }
    public bool ContainerAccessible { get; set; }
    public int FilesFound { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}