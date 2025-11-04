using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using System.Web;
using TwinFx.Services;
using Microsoft.Azure.Cosmos;

namespace TwinFx.Functions;

public class ListDocumentsFunction
{
    private readonly ILogger<ListDocumentsFunction> _logger;
    private readonly IConfiguration _configuration;

    public ListDocumentsFunction(ILogger<ListDocumentsFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for CORS preflight requests
    [Function("ListDocumentsOptions")]
    public async Task<HttpResponseData> HandleOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "list-documents/{twinId}/{directoryName?}")] HttpRequestData req,
        string twinId,
        string? directoryName = null)
    {
        _logger.LogInformation($"🚀 OPTIONS preflight request for list-documents/{twinId}/{directoryName ?? "root"}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetDocumentFromCosmosDBOptions")]
    public async Task<HttpResponseData> HandleCosmosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "document-cosmos/{twinId}/{documentType}/{documentId}")] HttpRequestData req,
        string twinId,
        string documentType,
        string documentId)
    {
        _logger.LogInformation($"🚀 OPTIONS preflight request for document-cosmos/{twinId}/{documentType}/{documentId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetAllDocumentsFromCosmosDBOptions")]
    public async Task<HttpResponseData> HandleAllDocumentsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "documents-cosmos/{twinId}/{documentType}")] HttpRequestData req,
        string twinId,
        string documentType)
    {
        _logger.LogInformation($"🚀 OPTIONS preflight request for documents-cosmos/{twinId}/{documentType}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("ListDocuments")]
    public async Task<HttpResponseData> ListDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "list-documents/{twinId}/{directoryName?}")] HttpRequestData req,
        string twinId,
        string? directoryName = null)
    {
        _logger.LogInformation("🚀 ListDocuments function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ListDocumentsResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Decode URL-encoded directory name if provided (handles multiple encoding levels)
            directoryName = DecodeDirectoryName(directoryName);
            
            if (!string.IsNullOrEmpty(directoryName))
            {
                _logger.LogInformation($"🔓 Decoded directory name: '{directoryName}'");
            }

            // Get optional query parameters
            var queryString = req.Url.Query;
            var includeMetadata = bool.Parse(GetQueryParameter(queryString, "includeMetadata") ?? "true");

            _logger.LogInformation($"📂 Listing documents for Twin ID: {twinId}, directory: '{directoryName ?? "root"}', includeMetadata: {includeMetadata}");

            // Create DataLake client factory
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Test connection first
            var connectionTest = await dataLakeClient.TestConnectionAsync();
            if (!connectionTest)
            {
                _logger.LogError("❌ Failed to connect to Azure Storage");
                var connectionErrorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                AddCorsHeaders(connectionErrorResponse, req);
                await connectionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new ListDocumentsResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to connect to Azure Storage. Please check configuration."
                }));
                return connectionErrorResponse;
            }

            // List files in the specified directory using the new method
            var files = await dataLakeClient.ListFilesInDirectoryAsync(directoryName ?? "");

            _logger.LogInformation($"✅ Found {files.Count} files for Twin ID: {twinId} in directory: '{directoryName ?? "root"}'");

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new ListDocumentsResponse
            {
                Success = true,
                TwinId = twinId,
                TotalFiles = files.Count,
                Directory = directoryName,
                Files = includeMetadata ? files.Select(f => new DocumentInfo
                {
                    Name = f.Name,
                    Size = f.Size,
                    ContentType = f.ContentType,
                    LastModified = f.LastModified,
                    CreatedOn = f.CreatedOn,
                    ETag = f.ETag,
                    Url = f.Url,
                    Metadata = f.Metadata
                }).ToList() : files.Select(f => new DocumentInfo
                {
                    Name = f.Name,
                    Size = f.Size,
                    ContentType = f.ContentType,
                    LastModified = f.LastModified
                }).ToList(),
                RetrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error listing documents");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ListDocumentsResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetDocumentNames")]
    public async Task<HttpResponseData> GetDocumentNames(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "document-names/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🚀 GetDocumentNames function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DocumentNamesResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Get optional query parameters
            var queryString = req.Url.Query;
            var prefix = GetQueryParameter(queryString, "prefix") ?? "";

            _logger.LogInformation($"📝 Getting document names for Twin ID: {twinId}, prefix: '{prefix}'");

            // Create DataLake client factory
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Test connection first
            var connectionTest = await dataLakeClient.TestConnectionAsync();
            if (!connectionTest)
            {
                _logger.LogError("❌ Failed to connect to Azure Storage");
                var connectionErrorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                AddCorsHeaders(connectionErrorResponse, req);
                await connectionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new DocumentNamesResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to connect to Azure Storage. Please check configuration."
                }));
                return connectionErrorResponse;
            }

            // List files and extract only names
            var files = await dataLakeClient.ListFilesAsync(prefix);
            var fileNames = files.Select(f => f.Name).ToList();

            _logger.LogInformation($"✅ Found {fileNames.Count} document names for Twin ID: {twinId}");

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new DocumentNamesResponse
            {
                Success = true,
                TwinId = twinId,
                TotalFiles = fileNames.Count,
                Prefix = prefix,
                FileNames = fileNames,
                RetrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting document names");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DocumentNamesResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetStorageStatistics")]
    public async Task<HttpResponseData> GetStorageStatistics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "storage-stats/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🚀 GetStorageStatistics function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new StorageStatsResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"📊 Getting storage statistics for Twin ID: {twinId}");

            // Create DataLake client factory
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Get storage statistics
            var stats = await dataLakeClient.GetStorageStatisticsAsync();

            _logger.LogInformation($"✅ Retrieved storage statistics for Twin ID: {twinId}");

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new StorageStatsResponse
            {
                Success = true,
                TwinId = twinId,
                Statistics = stats,
                RetrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting storage statistics");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new StorageStatsResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetDocumentFromCosmosDB")]
    public async Task<HttpResponseData> GetDocumentFromCosmosDB(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "document-cosmos/{twinId}/{documentType}/{documentId}")] HttpRequestData req,
        string twinId,
        string documentType,
        string documentId)
    {
        _logger.LogInformation("🚀 GetDocumentFromCosmosDB function triggered for Twin ID: {TwinId}, document type: {DocumentType}, ID: {DocumentId}", twinId, documentType, documentId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                _logger.LogError("❌ Document ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Document ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(documentType))
            {
                _logger.LogError("❌ Document type parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Document type parameter is required"
                }));
                return badResponse;
            }

            // Determine container name based on document type
            var containerName = GetContainerNameByDocumentType(documentType);
            if (string.IsNullOrEmpty(containerName))
            {
                _logger.LogError("❌ Unsupported document type: {DocumentType}", documentType);
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentResponse
                {
                    Success = false,
                    ErrorMessage = $"Unsupported document type: {documentType}. Supported types: Invoice, Factura, DriversLicense, LicenciaManejo"
                }));
                return badResponse;
            }

            _logger.LogInformation($"📄 Searching document in {containerName} container with ID: {documentId}");

            // Get document from the determined container
            var document = await GetDocumentByIdFromCosmosAsync(documentId, containerName);

            if (document == null)
            {
                _logger.LogWarning($"⚠️ Document not found with ID: {documentId} in container: {containerName}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentResponse
                {
                    Success = false,
                    ErrorMessage = $"Document with ID '{documentId}' not found in {containerName} container"
                }));
                return notFoundResponse;
            }

            _logger.LogInformation($"✅ Found document: {documentId} in container: {containerName}");

            // Generate SAS URL for the document from DataLake
            string? sasUrl = null;
            try
            {
                _logger.LogInformation($"🔗 Generating SAS URL for document in DataLake");
                
                // Create DataLake client to get SAS URL
                var serviceProvider = new ServiceCollection()
                    .AddLogging(builder => builder.AddConsole())
                    .BuildServiceProvider();
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Extract file path from document - use filePath if available, otherwise construct from fileName
                string filePath = !string.IsNullOrEmpty(document.FilePath) ? document.FilePath : documentId;
                
                // Clean the file path by removing twin ID prefix if present
                filePath = CleanFilePath(filePath, twinId);
                
                // If we have a fileName but no filePath, try to construct based on document type
                if (string.IsNullOrEmpty(document.FilePath) && !string.IsNullOrEmpty(document.FileName))
                {
                    if (documentType.ToLowerInvariant().Contains("invoice") || documentType.ToLowerInvariant().Contains("factura"))
                    {
                        filePath = $"documents/{document.FileName}"; // Typical path for invoices
                    }
                    else if (documentType.ToLowerInvariant().Contains("license") || documentType.ToLowerInvariant().Contains("licencia"))
                    {
                        filePath = $"documents/{document.FileName}"; // Typical path for licenses
                    }
                    else
                    {
                        filePath = document.FileName; // Fallback to just filename
                    }
                }

                // Generate SAS URL valid for 24 hours
                sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
                
                if (!string.IsNullOrEmpty(sasUrl))
                {
                    _logger.LogInformation($"✅ Generated SAS URL for file: {filePath}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Could not generate SAS URL for file: {filePath}");
                }
            }
            catch (Exception sasEx)
            {
                _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL, continuing without it");
                // Continue without SAS URL - not critical for the response
            }

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new CosmosDocumentResponse
            {
                Success = true,
                TwinId = twinId,
                DocumentId = documentId,
                DocumentType = documentType,
                ContainerName = containerName,
                Document = document, // Now returns the optimized summary instead of full document
                SasUrl = sasUrl,
                RetrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error retrieving document from Cosmos DB");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetAllDocumentsFromCosmosDB")]
    public async Task<HttpResponseData> GetAllDocumentsFromCosmosDB(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents-cosmos/{twinId}/{documentType}")] HttpRequestData req,
        string twinId,
        string documentType)
    {
        _logger.LogInformation("🚀 GetAllDocumentsFromCosmosDB function triggered for Twin ID: {TwinId}, document type: {DocumentType}", twinId, documentType);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentsListResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(documentType))
            {
                _logger.LogError("❌ Document type parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentsListResponse
                {
                    Success = false,
                    ErrorMessage = "Document type parameter is required"
                }));
                return badResponse;
            }

            // Determine container name based on document type
            var containerName = GetContainerNameByDocumentType(documentType);
            if (string.IsNullOrEmpty(containerName))
            {
                _logger.LogError("❌ Unsupported document type: {DocumentType}", documentType);
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentsListResponse
                {
                    Success = false,
                    ErrorMessage = $"Unsupported document type: {documentType}. Supported types: Invoice, Factura, DriversLicense, LicenciaManejo"
                }));
                return badResponse;
            }

            // Get query parameters for filtering/pagination
            var queryString = req.Url.Query;
            var limit = int.TryParse(GetQueryParameter(queryString, "limit"), out var parsedLimit) ? parsedLimit : 50;
            var offset = int.TryParse(GetQueryParameter(queryString, "offset"), out var parsedOffset) ? parsedOffset : 0;

            _logger.LogInformation($"📄 Searching all documents in {containerName} container for Twin ID: {twinId}, limit: {limit}, offset: {offset}");

            // Get all documents from the determined container
            var documents = await GetAllDocumentsFromCosmosAsync(twinId, containerName, limit, offset);

            _logger.LogInformation($"✅ Found {documents.Count} documents for Twin ID: {twinId} in container: {containerName}");

            // Generate SAS URLs for each document
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Add SAS URLs to documents
            foreach (var document in documents)
            {
                try
                {
                    // Extract file path from document - use filePath if available, otherwise construct from fileName
                    string filePath = !string.IsNullOrEmpty(document.FilePath) ? document.FilePath : document.Id;
                    
                    // Clean the file path by removing twin ID prefix if present
                    filePath = CleanFilePath(filePath, twinId);
                    
                    // If we have a fileName but no filePath, try to construct based on document type
                    if (string.IsNullOrEmpty(document.FilePath) && !string.IsNullOrEmpty(document.FileName))
                    {
                        if (documentType.ToLowerInvariant().Contains("invoice") || documentType.ToLowerInvariant().Contains("factura"))
                        {
                            filePath = $"documents/{document.FileName}"; // Typical path for invoices
                        }
                        else if (documentType.ToLowerInvariant().Contains("license") || documentType.ToLowerInvariant().Contains("licencia"))
                        {
                            filePath = $"documents/{document.FileName}"; // Typical path for licenses
                        }
                        else
                        {
                            filePath = document.FileName; // Fallback to just filename
                        }
                    }

                    // Generate SAS URL valid for 24 hours
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
                    
                    if (!string.IsNullOrEmpty(sasUrl))
                    {
                        // Add SAS URL to document (we'll need to add this property to CosmosDocumentSummary)
                        document.SasUrl = sasUrl;
                        _logger.LogInformation($"✅ Generated SAS URL for file: {filePath}");
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ Could not generate SAS URL for file: {filePath}");
                    }
                }
                catch (Exception sasEx)
                {
                    _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for document: {DocumentId}, continuing without it", document.Id);
                    // Continue without SAS URL for this document - not critical
                }
            }

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new CosmosDocumentsListResponse
            {
                Success = true,
                TwinId = twinId,
                DocumentType = documentType,
                ContainerName = containerName,
                TotalDocuments = documents.Count,
                Documents = documents,
                Limit = limit,
                Offset = offset,
                RetrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error retrieving documents from Cosmos DB");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new CosmosDocumentsListResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Get all documents from Cosmos DB container for a specific Twin ID
    /// </summary>
    public async Task<List<CosmosDocumentSummary>> GetAllDocumentsFromCosmosAsync(string twinId, string containerName, int limit = 50, int offset = 0)
    {
        try
        {
            _logger.LogInformation("🔍 Searching for all documents in {ContainerName} container for Twin ID: {TwinId}", containerName, twinId);
            
            var cosmosClient = new Microsoft.Azure.Cosmos.CosmosClient(
                $"https://{_configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb"}.documents.azure.com:443/",
                _configuration.GetValue<string>("COSMOS_KEY") ?? throw new InvalidOperationException("COSMOS_KEY is required"));
            
            var database = cosmosClient.GetDatabase("TwinHumanDB");
            
            // First, check if container exists
            try
            {
                var container = database.GetContainer(containerName);
                
                // Test container access
                await container.ReadContainerAsync();
                _logger.LogInformation("✅ Container {ContainerName} exists and is accessible", containerName);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError("❌ Container {ContainerName} does not exist in database {DatabaseName}", containerName, database.Id);
                throw new InvalidOperationException($"Container '{containerName}' does not exist in Cosmos DB database. Please create the container first.");
            }

            var containerRef = database.GetContainer(containerName);

            // Query to get all documents for the Twin ID with pagination
            var query = new Microsoft.Azure.Cosmos.QueryDefinition(@"
                SELECT 
                    c.id, c.TwinID, c.fileName, c.filePath, c.createdAt, c.source, c.processedAt, 
                    c.success, c.errorMessage, c.totalPages, c.vendorName, c.vendorNameConfidence,
                    c.customerName, c.customerNameConfidence, c.invoiceNumber, c.invoiceDate, c.dueDate,
                    c.subTotal, c.subTotalConfidence, c.totalTax, c.invoiceTotal, c.invoiceTotalConfidence,
                    c.lineItemsCount, c.tablesCount, c.rawFieldsCount,
                    c.aiExecutiveSummaryHtml, c.aiExecutiveSummaryText, c.aiTextSummary, c.aiHtmlOutput,
                    c.aiTextReport, c.aiTablesContent, c.aiStructuredData, c.aiProcessedText,
                    c.aiCompleteSummary, c.aiCompleteInsights, c.hasAiExecutiveSummary,
                    c.hasAiInvoiceAnalysis, c.hasAiCompleteAnalysis, c.aiDataFieldsCount
                FROM c 
                WHERE c.TwinID = @twinId
                ORDER BY c.processedAt DESC
                OFFSET @offset LIMIT @limit")
                .WithParameter("@twinId", twinId)
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var iterator = containerRef.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var documents = new List<CosmosDocumentSummary>();
            
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var document in response)
                {
                    documents.Add(CosmosDocumentSummary.FromCosmosDocument(document));
                }
            }

            _logger.LogInformation("✅ Found {DocumentCount} documents for Twin ID: {TwinId} in container: {ContainerName}", documents.Count, twinId, containerName);
            return documents;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.SubStatusCode == 1003)
        {
            _logger.LogError("❌ Container '{ContainerName}' does not exist. Error: {ErrorMessage}", containerName, ex.Message);
            throw new InvalidOperationException($"Cosmos DB container '{containerName}' does not exist. Please create the container first or check your configuration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error querying documents from Cosmos DB container: {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Determine Cosmos DB container name based on document type
    /// </summary>
    /// <param name="documentType">Type of document (Invoice, Factura, DriversLicense, LicenciaManejo)</param>
    /// <returns>Container name or null if unsupported type</returns>
    public static string? GetContainerNameByDocumentType(string documentType)
    {
        return documentType.ToLowerInvariant() switch
        {
            "invoice" or "factura" or "invoices" => "TwinInvoices",
            "driverslicense" or "licenciamanejo" or "licensia" => "TwinDriversLicense",
            _ => null
        };
    }

    /// <summary>
    /// Get essential document data from Cosmos DB container by ID
    /// </summary>
    public async Task<CosmosDocumentSummary?> GetDocumentByIdFromCosmosAsync(string documentId, string containerName)
    {
        try
        {
            _logger.LogInformation("🔍 Searching for document summary across all partitions in {ContainerName} container", containerName);
            
            var cosmosClient = new Microsoft.Azure.Cosmos.CosmosClient(
                $"https://{_configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb"}.documents.azure.com:443/",
                _configuration.GetValue<string>("COSMOS_KEY") ?? throw new InvalidOperationException("COSMOS_KEY is required"));
            
            var database = cosmosClient.GetDatabase("TwinHumanDB");
            
            // First, check if container exists
            try
            {
                var container = database.GetContainer(containerName);
                
                // Test container access
                await container.ReadContainerAsync();
                _logger.LogInformation("✅ Container {ContainerName} exists and is accessible", containerName);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError("❌ Container {ContainerName} does not exist in database {DatabaseName}", containerName, database.Id);
                throw new InvalidOperationException($"Container '{containerName}' does not exist in Cosmos DB database. Please create the container first.");
            }

            var containerRef = database.GetContainer(containerName);

            // Query to get only essential fields instead of full document
            var query = new Microsoft.Azure.Cosmos.QueryDefinition(@"
                SELECT 
                    c.id, c.TwinID, c.fileName, c.filePath, c.createdAt, c.source, c.processedAt, 
                    c.success, c.errorMessage, c.totalPages, c.vendorName, c.vendorNameConfidence,
                    c.customerName, c.customerNameConfidence, c.invoiceNumber, c.invoiceDate, c.dueDate,
                    c.subTotal, c.subTotalConfidence, c.totalTax, c.invoiceTotal, c.invoiceTotalConfidence,
                    c.lineItemsCount, c.tablesCount, c.rawFieldsCount,
                    c.aiExecutiveSummaryHtml, c.aiExecutiveSummaryText, c.aiTextSummary, c.aiHtmlOutput,
                    c.aiTextReport, c.aiTablesContent, c.aiStructuredData, c.aiProcessedText,
                    c.aiCompleteSummary, c.aiCompleteInsights, c.hasAiExecutiveSummary,
                    c.hasAiInvoiceAnalysis, c.hasAiCompleteAnalysis, c.aiDataFieldsCount
                FROM c 
                WHERE c.id = @documentId")
                .WithParameter("@documentId", documentId);

            var iterator = containerRef.GetItemQueryIterator<Dictionary<string, object?>>(query);
            
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                var document = response.FirstOrDefault();
                if (document != null)
                {
                    _logger.LogInformation("✅ Found document summary with ID: {DocumentId} in container: {ContainerName}", documentId, containerName);
                    return CosmosDocumentSummary.FromCosmosDocument(document);
                }
            }

            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.SubStatusCode == 1003)
        {
            _logger.LogError("❌ Container '{ContainerName}' does not exist. Error: {ErrorMessage}", containerName, ex.Message);
            throw new InvalidOperationException($"Cosmos DB container '{containerName}' does not exist. Please create the container first or check your configuration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error querying document summary from Cosmos DB container: {ContainerName}", containerName);
            throw;
        }
    }

    public static string? GetQueryParameter(string queryString, string parameterName)
    {
        if (string.IsNullOrEmpty(queryString))
            return null;

        // Remove leading '?' if present
        if (queryString.StartsWith("?"))
            queryString = queryString.Substring(1);

        var parameters = queryString.Split('&')
            .Select(param => param.Split('='))
            .Where(pair => pair.Length == 2)
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.OrdinalIgnoreCase);

        return parameters.TryGetValue(parameterName, out var value) ? value : null;
    }

    /// <summary>
    /// Safely decode URL-encoded directory path, handling multiple encoding levels
    /// </summary>
    /// <param name="directoryName">The URL-encoded directory name</param>
    /// <returns>Decoded directory name with normalized path separators</returns>
    public static string? DecodeDirectoryName(string? directoryName)
    {
        if (string.IsNullOrEmpty(directoryName))
            return directoryName;

        try
        {
            string decoded = directoryName;
            
            // Decode up to 3 levels of URL encoding (safety limit)
            for (int i = 0; i < 3 && decoded.Contains("%"); i++)
            {
                var previousDecoded = decoded;
                decoded = Uri.UnescapeDataString(decoded);
                
                // Break if no change (prevents infinite loop)
                if (decoded == previousDecoded)
                    break;
            }
            
            // Normalize path separators to forward slashes
            decoded = decoded.Replace("\\", "/");
            
            // Remove any leading/trailing slashes for consistency
            decoded = decoded.Trim('/');
            
            return string.IsNullOrEmpty(decoded) ? null : decoded;
        }
        catch (Exception)
        {
            // If decoding fails, return the original value
            return directoryName;
        }
    }

    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        // Get origin from request headers
        var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
        var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;
        
        // Allow specific origins for development
        var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };
        
        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
        }
        else
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
        }
        
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
        response.Headers.Add("Access-Control-Max-Age", "3600");
    }

    /// <summary>
    /// Helper method to remove twin ID from file path if present
    /// </summary>
    /// <param name="filePath">Original file path from Cosmos DB</param>
    /// <param name="twinId">Twin ID to remove from path</param>
    /// <returns>Clean path without twin ID prefix</returns>
    private static string CleanFilePath(string filePath, string twinId)
    {
        if (string.IsNullOrEmpty(filePath))
            return filePath;

        // Remove twin ID prefix if present (handles both with and without trailing slash)
        var twinIdPrefix = $"{twinId}/";
        if (filePath.StartsWith(twinIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return filePath.Substring(twinIdPrefix.Length);
        }

        // Also check without slash
        if (filePath.StartsWith(twinId, StringComparison.OrdinalIgnoreCase) && 
            filePath.Length > twinId.Length && 
            filePath[twinId.Length] == '/')
        {
            return filePath.Substring(twinId.Length + 1);
        }

        return filePath;
    }
}

public class ListDocumentsResponse
{
    public bool Success { get; set; }
    public string? TwinId { get; set; }
    public int TotalFiles { get; set; }
    public string? Directory { get; set; }
    public List<DocumentInfo>? Files { get; set; }
    public DateTime? RetrievedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DocumentNamesResponse
{
    public bool Success { get; set; }
    public string? TwinId { get; set; }
    public int TotalFiles { get; set; }
    public string? Prefix { get; set; }
    public List<string>? FileNames { get; set; }
    public DateTime? RetrievedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StorageStatsResponse
{
    public bool Success { get; set; }
    public string? TwinId { get; set; }
    public StorageStatistics? Statistics { get; set; }
    public DateTime? RetrievedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CosmosDocumentResponse
{
    public bool Success { get; set; }
    public string? TwinId { get; set; }
    public string? DocumentId { get; set; }
    public string? DocumentType { get; set; }
    public string? ContainerName { get; set; }
    public CosmosDocumentSummary? Document { get; set; }
    public string? SasUrl { get; set; }
    public DateTime? RetrievedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CosmosDocumentsListResponse
{
    public bool Success { get; set; }
    public string? TwinId { get; set; }
    public string? DocumentType { get; set; }
    public string? ContainerName { get; set; }
    public int TotalDocuments { get; set; }
    public List<CosmosDocumentSummary>? Documents { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
    public DateTime? RetrievedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DocumentInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public DateTime CreatedOn { get; set; }
    public string ETag { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public IDictionary<string, string>? Metadata { get; set; }
}

public class CosmosDocumentSummary
{
    // Basic document info
    public string Id { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Document Intelligence data
    public int TotalPages { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public float VendorNameConfidence { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public float CustomerNameConfidence { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal SubTotal { get; set; }
    public float SubTotalConfidence { get; set; }
    public decimal TotalTax { get; set; }
    public decimal InvoiceTotal { get; set; }
    public float InvoiceTotalConfidence { get; set; }
    public int LineItemsCount { get; set; }
    public int TablesCount { get; set; }
    public int RawFieldsCount { get; set; }

    // AI Generated data
    public string AiExecutiveSummaryHtml { get; set; } = string.Empty;
    public string AiExecutiveSummaryText { get; set; } = string.Empty;
    public string AiTextSummary { get; set; } = string.Empty;
    public string AiHtmlOutput { get; set; } = string.Empty;
    public string AiTextReport { get; set; } = string.Empty;
    public string AiTablesContent { get; set; } = string.Empty;
    public string AiStructuredData { get; set; } = string.Empty;
    public string AiProcessedText { get; set; } = string.Empty;
    public string AiCompleteSummary { get; set; } = string.Empty;
    public string AiCompleteInsights { get; set; } = string.Empty;

    // AI metadata flags
    public bool HasAiExecutiveSummary { get; set; }
    public bool HasAiInvoiceAnalysis { get; set; }
    public bool HasAiCompleteAnalysis { get; set; }
    public int AiDataFieldsCount { get; set; }

    // SAS URL for document access
    public string? SasUrl { get; set; }

    /// <summary>
    /// Create a document summary from Cosmos DB document dictionary
    /// </summary>
    public static CosmosDocumentSummary FromCosmosDocument(Dictionary<string, object?> document)
    {
        return new CosmosDocumentSummary
        {
            // Basic info
            Id = GetStringValue(document, "id"),
            TwinId = GetStringValue(document, "TwinID"),
            FileName = GetStringValue(document, "fileName"),
            FilePath = GetStringValue(document, "filePath"),
            CreatedAt = GetDateTimeValue(document, "createdAt"),
            Source = GetStringValue(document, "source"),
            ProcessedAt = GetDateTimeValue(document, "processedAt"),
            Success = GetBoolValue(document, "success"),
            ErrorMessage = GetStringValue(document, "errorMessage"),

            // Document Intelligence
            TotalPages = GetIntValue(document, "totalPages"),
            VendorName = GetStringValue(document, "vendorName"),
            VendorNameConfidence = GetFloatValue(document, "vendorNameConfidence"),
            CustomerName = GetStringValue(document, "customerName"),
            CustomerNameConfidence = GetFloatValue(document, "customerNameConfidence"),
            InvoiceNumber = GetStringValue(document, "invoiceNumber"),
            InvoiceDate = GetNullableDateTimeValue(document, "invoiceDate"),
            DueDate = GetNullableDateTimeValue(document, "dueDate"),
            SubTotal = GetDecimalValue(document, "subTotal"),
            SubTotalConfidence = GetFloatValue(document, "subTotalConfidence"),
            TotalTax = GetDecimalValue(document, "totalTax"),
            InvoiceTotal = GetDecimalValue(document, "invoiceTotal"),
            InvoiceTotalConfidence = GetFloatValue(document, "invoiceTotalConfidence"),
            LineItemsCount = GetIntValue(document, "lineItemsCount"),
            TablesCount = GetIntValue(document, "tablesCount"),
            RawFieldsCount = GetIntValue(document, "rawFieldsCount"),

            // AI Data - using GetValidJsonOrNull for fields that might contain invalid JSON
            AiExecutiveSummaryHtml = GetStringValue(document, "aiExecutiveSummaryHtml"),
            AiExecutiveSummaryText = GetStringValue(document, "aiExecutiveSummaryText"),
            AiTextSummary = GetStringValue(document, "aiTextSummary"),
            AiHtmlOutput = GetStringValue(document, "aiHtmlOutput"),
            AiTextReport = GetStringValue(document, "aiTextReport"),
            AiTablesContent = GetValidJsonOrNull(document, "aiTablesContent"), // Fix JSON parsing error
            AiStructuredData = GetValidJsonOrNull(document, "aiStructuredData"), // Fix JSON parsing error
            AiProcessedText = GetStringValue(document, "aiProcessedText"),
            AiCompleteSummary = GetStringValue(document, "aiCompleteSummary"),
            AiCompleteInsights = GetValidJsonOrNull(document, "aiCompleteInsights"), // Fix JSON parsing error

            // AI Metadata
            HasAiExecutiveSummary = GetBoolValue(document, "hasAiExecutiveSummary"),
            HasAiInvoiceAnalysis = GetBoolValue(document, "hasAiInvoiceAnalysis"),
            HasAiCompleteAnalysis = GetBoolValue(document, "hasAiCompleteAnalysis"),
            AiDataFieldsCount = GetIntValue(document, "aiDataFieldsCount")
        };
    }

    private static string GetStringValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                return jsonElement.GetString() ?? string.Empty;
            return value.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Helper method to validate and clean JSON fields, ensuring they contain valid JSON or null
    /// </summary>
    private static string GetValidJsonOrNull(Dictionary<string, object?> data, string key)
    {
        var stringValue = GetStringValue(data, key);
        
        if (string.IsNullOrEmpty(stringValue))
            return string.Empty;
            
        // Check if it's already valid JSON
        try
        {
            JsonDocument.Parse(stringValue);
            return stringValue; // It's valid JSON, return as-is
        }
        catch (JsonException)
        {
            // It's not valid JSON, wrap it as a text field in a JSON object
            try
            {
                var textObject = new { text = stringValue, type = "plain_text", timestamp = DateTime.UtcNow.ToString("O") };
                return JsonSerializer.Serialize(textObject);
            }
            catch
            {
                // If even that fails, return empty string
                return string.Empty;
            }
        }
    }

    private static int GetIntValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetInt32();
            if (int.TryParse(value.ToString(), out var result))
                return result;
        }
        return 0;
    }

    private static float GetFloatValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetSingle();
            if (float.TryParse(value.ToString(), out var result))
                return result;
        }
        return 0f;
    }

    private static decimal GetDecimalValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetDecimal();
            if (decimal.TryParse(value.ToString(), out var result))
                return result;
        }
        return 0m;
    }

    private static bool GetBoolValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.True)
                return true;
            if (value is JsonElement jsonElement2 && jsonElement2.ValueKind == JsonValueKind.False)
                return false;
            if (bool.TryParse(value.ToString(), out var result))
                return result;
        }
        return false;
    }

    private static DateTime GetDateTimeValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(jsonElement.GetString(), out var result))
                    return result;
            }
            if (DateTime.TryParse(value.ToString(), out var dateResult))
                return dateResult;
        }
        return DateTime.MinValue;
    }

    private static DateTime? GetNullableDateTimeValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(jsonElement.GetString(), out var result))
                    return result;
            }
            if (DateTime.TryParse(value.ToString(), out var dateResult))
                return dateResult;
        }
        return null;
    }
}