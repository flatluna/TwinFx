using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TwinFx.Models;

namespace TwinFx.Services
{
    /// <summary>
    /// CosmosDB service for handling unstructured documents like InvoiceDocument
    /// Container: TwinInvoices, PartitionKey: TwinID
    /// </summary>
    public class UnStructuredDocumentsCosmosDB
    {
        private readonly ILogger<UnStructuredDocumentsCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly string _databaseName = "TwinHumanDB";
        private readonly string _invoicesContainerName = "TwinInvoices";

        public UnStructuredDocumentsCosmosDB(ILogger<UnStructuredDocumentsCosmosDB> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                var cosmosEndpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"];
                var cosmosKey = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"];

                if (string.IsNullOrEmpty(cosmosEndpoint) || string.IsNullOrEmpty(cosmosKey))
                {
                    throw new InvalidOperationException("COSMOS_ENDPOINT and COSMOS_KEY are required in configuration");
                }

                _cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
                _logger.LogInformation("✅ UnStructuredDocumentsCosmosDB initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize UnStructuredDocumentsCosmosDB");
                throw;
            }
        }

        /// <summary>
        /// Save InvoiceDocument to TwinInvoices container
        /// </summary>
        /// <param name="invoiceDocument">InvoiceDocument to save</param>
        /// <returns>True if saved successfully, false otherwise</returns>
        public async Task<bool> SaveInvoiceDocumentAsync(InvoiceDocument invoiceDocument)
        {
            try
            { 

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                // Ensure timestamps are set
                if (invoiceDocument.CreatedAt == DateTime.MinValue)
                {
                    invoiceDocument.CreatedAt = DateTime.UtcNow;
                }
                if (invoiceDocument.ProcessedAt == DateTime.MinValue)
                {
                    invoiceDocument.ProcessedAt = DateTime.UtcNow;
                }

                // Ensure ID is set
                if (string.IsNullOrEmpty(invoiceDocument.id))
                {
                    invoiceDocument.id = Guid.NewGuid().ToString();
                }

                // Save the document
                var response = await container.CreateItemAsync(invoiceDocument, new PartitionKey(invoiceDocument.TwinID));

              

                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogWarning("⚠️ InvoiceDocument with ID {DocumentId} already exists in TwinInvoices container", invoiceDocument.id);
                
                // Try to update instead
                try
                {
                    return await UpdateInvoiceDocumentAsync(invoiceDocument);
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "❌ Failed to update existing InvoiceDocument: {DocumentId}", invoiceDocument.id);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving InvoiceDocument to CosmosDB - FileName: {FileName}, TwinID: {TwinID}", 
                    invoiceDocument.FileName, invoiceDocument.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Update existing InvoiceDocument in TwinInvoices container
        /// </summary>
        /// <param name="invoiceDocument">InvoiceDocument to update</param>
        /// <returns>True if updated successfully, false otherwise</returns>
        public async Task<bool> UpdateInvoiceDocumentAsync(InvoiceDocument invoiceDocument)
        {
            try
            {
                _logger.LogInformation("🔄 Updating InvoiceDocument in CosmosDB - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    invoiceDocument.id, invoiceDocument.TwinID);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                // Update processed timestamp
                invoiceDocument.ProcessedAt = DateTime.UtcNow;

                var response = await container.ReplaceItemAsync(invoiceDocument, invoiceDocument.id, new PartitionKey(invoiceDocument.TwinID));

                _logger.LogInformation("✅ InvoiceDocument updated successfully - DocumentId: {DocumentId}, StatusCode: {StatusCode}, RequestCharge: {RequestCharge}",
                    response.Resource.id, response.StatusCode, response.RequestCharge);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating InvoiceDocument in CosmosDB - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    invoiceDocument.id, invoiceDocument.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Get InvoiceDocument by ID and TwinID
        /// </summary>
        /// <param name="documentId">Document ID</param>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <returns>InvoiceDocument if found, null otherwise</returns>
        public async Task<InvoiceDocument?> GetInvoiceDocumentByIdAsync(string documentId, string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Getting InvoiceDocument by ID - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var response = await container.ReadItemAsync<InvoiceDocument>(documentId, new PartitionKey(twinId));

                _logger.LogInformation("✅ InvoiceDocument found - DocumentId: {DocumentId}, FileName: {FileName}", 
                    response.Resource.id, response.Resource.FileName);

                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ InvoiceDocument not found - DocumentId: {DocumentId}, TwinID: {TwinID}", documentId, twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting InvoiceDocument by ID - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);
                throw;
            }
        }

        /// <summary>
        /// Get InvoiceDocument by ID and TwinID using SQL query
        /// Alternative to GetInvoiceDocumentByIdAsync that uses SQL query instead of ReadItemAsync
        /// </summary>
        /// <param name="documentId">Document ID</param>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <returns>InvoiceDocument if found, null otherwise</returns>
        public async Task<InvoiceDocument?> GetInvoiceDocumentByIdWithQueryAsync(string documentId, string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Getting InvoiceDocument by ID and TwinID using SQL query - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.id = @documentId")
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@documentId", documentId);

                var iterator = container.GetItemQueryIterator<InvoiceDocument>(query);
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var document = response.FirstOrDefault();
                    
                    if (document != null)
                    {
                        _logger.LogInformation("✅ InvoiceDocument found via query - DocumentId: {DocumentId}, FileName: {FileName}", 
                            document.id, document.FileName);

                        // Generate SAS URL for the document
                        try
                        {
                            _logger.LogInformation("🔗 Generating SAS URL for document access via DataLake");
                            
                            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                            // Construct the full file path from FilePath and FileName
                            string fullFilePath;
                            if (!string.IsNullOrEmpty(document.FilePath) && !string.IsNullOrEmpty(document.FileName))
                            {
                                // Combine FilePath and FileName
                                fullFilePath = $"{document.FilePath.TrimEnd('/')}/{document.FileName}";
                            }
                            else if (!string.IsNullOrEmpty(document.FileName))
                            {
                                // Use FileName only if FilePath is empty
                                fullFilePath = document.FileName;
                            }
                            else
                            {
                                // Fallback to using the document ID
                                fullFilePath = $"documents/{documentId}";
                                _logger.LogWarning("⚠️ No FileName or FilePath found, using fallback path: {FallbackPath}", fullFilePath);
                            }

                            _logger.LogInformation("📁 Attempting to generate SAS URL for path: {FilePath}", fullFilePath);

                            // Generate SAS URL valid for 24 hours
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                            
                            if (!string.IsNullOrEmpty(sasUrl))
                            {
                                document.FileURL = sasUrl;
                                _logger.LogInformation("✅ SAS URL generated successfully for file: {FilePath}", fullFilePath);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Could not generate SAS URL for file: {FilePath}", fullFilePath);
                            }
                        }
                        catch (Exception sasEx)
                        {
                            _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for document: {DocumentId}, continuing without it", documentId);
                            // Continue without SAS URL - not critical for document retrieval
                        }

                        return document;
                    }
                }

                _logger.LogWarning("⚠️ InvoiceDocument not found via query - DocumentId: {DocumentId}, TwinID: {TwinID}", documentId, twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting InvoiceDocument by ID via query - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);
                throw;
            }
        }

        /// <summary>
        /// Get InvoiceDocument metadata only for a specific TwinID (lightweight query)
        /// Returns only basic metadata fields without invoiceData or AI analysis fields
        /// </summary>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <returns>List of InvoiceMetadata objects</returns>
        public async Task<List<InvoiceMetadata>> GetInvoiceDocumentsMetadataByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Getting InvoiceDocuments metadata for TwinID: {TwinID}", twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var query = new QueryDefinition(@"
                    SELECT 
                        c.id,
                        c.TwinID, 
                        c.FileName,
                        c.FilePath,
                        c.CreatedAt,
                        c.Source,
                        c.ProcessedAt,
                        c.Success,
                        c.ErrorMessage,
                        c.TotalPages,
                        c.VendorName,
                        c.VendorNameConfidence,
                        c.CustomerName,
                        c.CustomerNameConfidence,
                        c.InvoiceNumber,
                        c.InvoiceDate,
                        c.DueDate,
                        c.SubTotal,
                        c.SubTotalConfidence,
                        c.TotalTax,
                        c.InvoiceTotal,
                        c.InvoiceTotalConfidence,
                        c.LineItemsCount,
                        c.TablesCount,
                        c.RawFieldsCount
                    FROM c 
                    WHERE c.TwinID = @twinId 
                    ORDER BY c.ProcessedAt DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var metadataList = new List<InvoiceMetadata>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var document in response)
                    {
                        var metadata = InvoiceMetadata.FromCosmosDocument(document);
                        metadataList.Add(metadata);
                    }
                }

                _logger.LogInformation("✅ Found {MetadataCount} InvoiceDocuments metadata for TwinID: {TwinID}", metadataList.Count, twinId);

                return metadataList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting InvoiceDocuments metadata for TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Get all InvoiceDocuments for a specific TwinID
        /// </summary>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <returns>List of InvoiceDocuments</returns>
        public async Task<List<InvoiceDocument>> GetInvoiceDocumentsByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Getting all InvoiceDocuments for TwinID: {TwinID}", twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.processedAt DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<InvoiceDocument>(query);
                var documents = new List<InvoiceDocument>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    documents.AddRange(response);
                }

                _logger.LogInformation("✅ Found {DocumentCount} InvoiceDocuments for TwinID: {TwinID}", documents.Count, twinId);

                return documents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting InvoiceDocuments for TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Delete InvoiceDocument by ID and TwinID
        /// </summary>
        /// <param name="documentId">Document ID</param>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <returns>True if deleted successfully, false otherwise</returns>
        public async Task<bool> DeleteInvoiceDocumentAsync(string documentId, string twinId)
        {
            try
            {
                _logger.LogInformation("🗑️ Deleting InvoiceDocument - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var response = await container.DeleteItemAsync<InvoiceDocument>(documentId, new PartitionKey(twinId));

                _logger.LogInformation("✅ InvoiceDocument deleted successfully - DocumentId: {DocumentId}, StatusCode: {StatusCode}", 
                    documentId, response.StatusCode);

                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ InvoiceDocument not found for deletion - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting InvoiceDocument - DocumentId: {DocumentId}, TwinID: {TwinID}", 
                    documentId, twinId);
                return false;
            }
        }

        /// <summary>
        /// Search InvoiceDocuments with custom SQL filter
        /// </summary>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <param name="sqlFilter">SQL WHERE clause filter</param>
        /// <returns>List of filtered InvoiceDocuments</returns>
        public async Task<List<InvoiceDocument>> GetFilteredInvoiceDocumentsAsync(string twinId, string sqlFilter)
        {
            try
            {
                _logger.LogInformation("🔍 Getting filtered InvoiceDocuments for TwinID: {TwinID}, Filter: {SqlFilter}", 
                    twinId, sqlFilter);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var query = new QueryDefinition($"SELECT * FROM c WHERE {sqlFilter} ORDER BY c.processedAt DESC");

                var iterator = container.GetItemQueryIterator<InvoiceDocument>(query);
                var documents = new List<InvoiceDocument>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    documents.AddRange(response);
                }

                _logger.LogInformation("✅ Found {DocumentCount} filtered InvoiceDocuments for TwinID: {TwinID}", 
                    documents.Count, twinId);

                return documents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting filtered InvoiceDocuments for TwinID: {TwinID}, Filter: {SqlFilter}", 
                    twinId, sqlFilter);
                throw;
            }
        }

        /// <summary>
        /// Check if TwinInvoices container exists and create if needed
        /// </summary>
        /// <returns>True if container exists or was created successfully</returns>
        public async Task<bool> EnsureContainerExistsAsync()
        {
            try
            {
                _logger.LogInformation("🔧 Ensuring TwinInvoices container exists");

                var database = _cosmosClient.GetDatabase(_databaseName);
                
                var containerProperties = new ContainerProperties
                {
                    Id = _invoicesContainerName,
                    PartitionKeyPath = "/TwinID",
                    DefaultTimeToLive = -1 // No automatic expiration
                };

                var response = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);

                _logger.LogInformation("✅ TwinInvoices container ready - StatusCode: {StatusCode}, RequestCharge: {RequestCharge}", 
                    response.StatusCode, response.RequestCharge);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error ensuring TwinInvoices container exists");
                return false;
            }
        }

        /// <summary>
        /// Get count of InvoiceDocuments for a specific TwinID
        /// </summary>
        /// <param name="twinId">Twin ID</param>
        /// <returns>Count of documents</returns>
        public async Task<int> GetInvoiceDocumentCountAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("📊 Getting InvoiceDocument count for TwinID: {TwinID}", twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.TwinID = @twinId")
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<int>(query);
                var count = 0;

                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    count = response.FirstOrDefault();
                }

                _logger.LogInformation("✅ InvoiceDocument count for TwinID {TwinID}: {Count}", twinId, count);

                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting InvoiceDocument count for TwinID: {TwinID}", twinId);
                return 0;
            }
        }

        /// <summary>
        /// Get all InvoiceDocuments for a specific TwinID filtered by VendorName
        /// Returns complete InvoiceDocument objects with all data for the specified vendor
        /// </summary>
        /// <param name="twinId">Twin ID (partition key)</param>
        /// <param name="vendorName">Vendor name to filter by (case-insensitive partial match)</param>
        /// <returns>List of InvoiceDocuments for the specified vendor</returns>
        public async Task<List<InvoiceDocument>> GetInvoiceDocumentsByVendorNameAsync(string twinId, string vendorName)
        {
            try
            {
                _logger.LogInformation("🔍 Getting InvoiceDocuments for TwinID: {TwinID} and VendorName: {VendorName}", 
                    twinId, vendorName);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_invoicesContainerName);

                // Use CONTAINS with LOWER for case-insensitive partial matching
                var query = new QueryDefinition(@"
                    SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND CONTAINS(LOWER(c.VendorName), LOWER(@vendorName))
                    ORDER BY c.ProcessedAt DESC")
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@vendorName", vendorName);

                var iterator = container.GetItemQueryIterator<InvoiceDocument>(query);
                var documents = new List<InvoiceDocument>();

                // Create DataLake client for SAS URL generation
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    
                    foreach (var document in response)
                    {
                        // Generate SAS URL for each document
                        try
                        {
                            _logger.LogDebug("🔗 Generating SAS URL for document: {DocumentId}", document.id);

                            // Construct the full file path from FilePath and FileName
                            string fullFilePath;
                            if (!string.IsNullOrEmpty(document.FilePath) && !string.IsNullOrEmpty(document.FileName))
                            {
                                // Combine FilePath and FileName
                                fullFilePath = $"{document.FilePath.TrimEnd('/')}/{document.FileName}";
                            }
                            else if (!string.IsNullOrEmpty(document.FileName))
                            {
                                // Use FileName only if FilePath is empty
                                fullFilePath = document.FileName;
                            }
                            else
                            {
                                // Fallback to using the document ID
                                fullFilePath = $"documents/{document.id}";
                                _logger.LogWarning("⚠️ No FileName or FilePath found for document {DocumentId}, using fallback path: {FallbackPath}", 
                                    document.id, fullFilePath);
                            }

                            // Generate SAS URL valid for 24 hours
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                            
                            if (!string.IsNullOrEmpty(sasUrl))
                            {
                                document.FileURL = sasUrl;
                                _logger.LogDebug("✅ SAS URL generated successfully for document: {DocumentId}", document.id);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Could not generate SAS URL for document: {DocumentId}, file: {FilePath}", 
                                    document.id, fullFilePath);
                            }
                        }
                        catch (Exception sasEx)
                        {
                            _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for document: {DocumentId}, continuing without it", document.id);
                            // Continue without SAS URL - not critical for document retrieval
                        }

                        documents.Add(document);
                    }
                }

                _logger.LogInformation("✅ Found {DocumentCount} InvoiceDocuments for TwinID: {TwinID} and VendorName: {VendorName}", 
                    documents.Count, twinId, vendorName);

                return documents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting InvoiceDocuments for TwinID: {TwinID} and VendorName: {VendorName}", 
                    twinId, vendorName);
                throw;
            }
        }
    }

    /// <summary>
    /// Lightweight invoice metadata class for listing and summary operations
    /// Contains only basic metadata without invoiceData or AI analysis fields
    /// </summary>
    public class InvoiceMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int TotalPages { get; set; }
        
        // Basic invoice fields
        public string VendorName { get; set; } = string.Empty;
        public float VendorNameConfidence { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public float CustomerNameConfidence { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal SubTotal { get; set; }
        public float SubTotalConfidence { get; set; }
        public decimal TotalTax { get; set; }
        public decimal InvoiceTotal { get; set; }
        public float InvoiceTotalConfidence { get; set; }
        public int LineItemsCount { get; set; }
        public int TablesCount { get; set; }
        public int RawFieldsCount { get; set; }

        /// <summary>
        /// Create InvoiceMetadata from Cosmos DB dictionary
        /// </summary>
        public static InvoiceMetadata FromCosmosDocument(Dictionary<string, object?> data)
        {
            // Debug logging to see what we're receiving
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: FromCosmosDocument called with {data.Count} keys");
            foreach (var kvp in data.Take(5)) // Log first 5 keys for debugging
            {
                var valueType = kvp.Value?.GetType().Name ?? "null";
                var valueStr = kvp.Value?.ToString() ?? "null";
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Key: {kvp.Key}, Type: {valueType}, Value: {(valueStr.Length > 50 ? valueStr.Substring(0, 50) + "..." : valueStr)}");
            }

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

            static T ConvertValue<T>(object value, T defaultValue)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ConvertValue - Input type: {value?.GetType().Name}, Target type: {typeof(T).Name}, Value: {value}");

                    if (value is T directValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Direct conversion successful: {directValue}");
                        return directValue;
                    }

                    if (value is JsonElement jsonElement)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: JsonElement detected, ValueKind: {jsonElement.ValueKind}");
                        
                        var type = typeof(T);
                        if (type == typeof(string))
                        {
                            var result = (T)(object)(jsonElement.GetString() ?? string.Empty);
                            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: String conversion: {result}");
                            return result;
                        }
                        if (type == typeof(int))
                        {
                            var result = (T)(object)jsonElement.GetInt32();
                            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Int conversion: {result}");
                            return result;
                        }
                        if (type == typeof(decimal))
                        {
                            var result = (T)(object)jsonElement.GetDecimal();
                            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Decimal conversion: {result}");
                            return result;
                        }
                        if (type == typeof(float))
                        {
                            var result = (T)(object)(float)jsonElement.GetDecimal();
                            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Float conversion: {result}");
                            return result;
                        }
                        if (type == typeof(bool))
                        {
                            var result = (T)(object)jsonElement.GetBoolean();
                            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Bool conversion: {result}");
                            return result;
                        }
                    }

                    var convertedValue = (T)Convert.ChangeType(value, typeof(T));
                    System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Convert.ChangeType successful: {convertedValue}");
                    return convertedValue;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ DEBUG: ConvertValue failed - {ex.Message}, returning default: {defaultValue}");
                    return defaultValue;
                }
            }

            var result = new InvoiceMetadata
            {
                Id = GetValue<string>("id"),
                TwinID = GetValue<string>("TwinID"),
                FileName = GetValue<string>("FileName"),
                FilePath = GetValue<string>("FilePath"),
                CreatedAt = GetDateTime("CreatedAt"),
                Source = GetValue<string>("Source"),
                ProcessedAt = GetDateTime("ProcessedAt"),
                Success = GetValue<bool>("Success"),
                ErrorMessage = GetValue<string?>("ErrorMessage"),
                TotalPages = GetValue<int>("TotalPages"),
                VendorName = GetValue<string>("VendorName"),
                VendorNameConfidence = GetValue<float>("VendorNameConfidence"),
                CustomerName = GetValue<string>("CustomerName"),
                CustomerNameConfidence = GetValue<float>("CustomerNameConfidence"),
                InvoiceNumber = GetValue<string>("InvoiceNumber"),
                InvoiceDate = GetDateTime("InvoiceDate"),
                DueDate = GetDateTime("DueDate"),
                SubTotal = GetValue<decimal>("SubTotal"),
                SubTotalConfidence = GetValue<float>("SubTotalConfidence"),
                TotalTax = GetValue<decimal>("TotalTax"),
                InvoiceTotal = GetValue<decimal>("InvoiceTotal"),
                InvoiceTotalConfidence = GetValue<float>("InvoiceTotalConfidence"),
                LineItemsCount = GetValue<int>("LineItemsCount"),
                TablesCount = GetValue<int>("TablesCount"),
                RawFieldsCount = GetValue<int>("RawFieldsCount")
            };

            System.Diagnostics.Debug.WriteLine($"✅ DEBUG: Created InvoiceMetadata - Id: {result.Id}, VendorName: {result.VendorName}, InvoiceTotal: {result.InvoiceTotal}");
            return result;
        }

        /// <summary>
        /// Get formatted date range string
        /// </summary>
        public string GetFormattedDateRange()
        {
            if (InvoiceDate == DateTime.MinValue)
                return "Sin fecha";
            
            return InvoiceDate.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Get summary line for display
        /// </summary>
        public string GetSummaryLine()
        {
            return $"{VendorName} | {GetFormattedDateRange()} | ${InvoiceTotal:F2} | {LineItemsCount} items";
        }
    }
}
