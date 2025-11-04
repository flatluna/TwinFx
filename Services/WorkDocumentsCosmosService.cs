using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Service class for managing Work Documents in Cosmos DB.
/// Handles CRUD operations for resume documents and work-related data.
/// Container: TwinWork, PartitionKey: TwinID
/// </summary>
public class WorkDocumentsCosmosService
{
    private readonly ILogger<WorkDocumentsCosmosService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly IConfiguration _configuration;

    public WorkDocumentsCosmosService(ILogger<WorkDocumentsCosmosService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        var accountName = configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ??
                         configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";

        var databaseName = configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ??
                          configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

        var endpoint = $"https://{accountName}.documents.azure.com:443/";
        var key = configuration.GetValue<string>("Values:COSMOS_KEY") ??
                 configuration.GetValue<string>("COSMOS_KEY");

        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
        }

        _client = new CosmosClient(endpoint, key);
        _database = _client.GetDatabase(databaseName);

        _logger.LogInformation("? Work Documents Cosmos Service initialized successfully");
    }

    /// <summary>
    /// Create a new work document in the TwinWork container
    /// </summary>
    /// <param name="workDocument">Work document to create</param>
    /// <returns>True if created successfully</returns>
    public async Task<bool> SaveWorkDocumentAsync(Dictionary<string, object?> workDocument)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            // Extract TwinID for partition key
            var twinId = workDocument.GetValueOrDefault("TwinID")?.ToString() ?? 
                        throw new ArgumentException("TwinID is required");

            // Use UpsertItemAsync to handle both create and update scenarios
            await workContainer.UpsertItemAsync(workDocument, new PartitionKey(twinId));

            _logger.LogInformation("?? Work document saved/updated successfully: {DocumentType} for Twin: {TwinId}",
                workDocument.GetValueOrDefault("documentType"), twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to save/update work document for Twin: {TwinId}",
                workDocument.GetValueOrDefault("TwinID"));
            return false;
        }
    }

    /// <summary>
    /// Get all work documents for a specific Twin ID
    /// </summary>
    /// <param name="twinId">Twin ID to get documents for</param>
    /// <returns>List of work documents</returns>
    public async Task<List<Dictionary<string, object?>>> GetWorkDocumentsByTwinIdAsync(string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = workContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var workDocuments = new List<Dictionary<string, object?>>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                workDocuments.AddRange(response);
            }

            _logger.LogInformation("?? Found {Count} work documents for Twin ID: {TwinId}", workDocuments.Count, twinId);
            return workDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get work documents for Twin ID: {TwinId}", twinId);
            return new List<Dictionary<string, object?>>();
        }
    }

    /// <summary>
    /// Get a specific work document by ID for a Twin
    /// </summary>
    /// <param name="documentId">Document ID to retrieve</param>
    /// <param name="twinId">Twin ID (used as partition key)</param>
    /// <returns>Work document if found, null otherwise</returns>
    public async Task<Dictionary<string, object?>?> GetWorkDocumentByIdAsync(string documentId, string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            var response = await workContainer.ReadItemAsync<Dictionary<string, object?>>(
                documentId,
                new PartitionKey(twinId)
            );

            var workDocument = response.Resource;
            _logger.LogInformation("?? Work document retrieved successfully: {DocumentId} for Twin: {TwinId}", 
                documentId, twinId);
            return workDocument;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("?? Work document not found: {DocumentId} for Twin: {TwinId}", documentId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get work document by ID {DocumentId} for Twin: {TwinId}", 
                documentId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Update an existing work document
    /// </summary>
    /// <param name="documentId">Document ID to update</param>
    /// <param name="twinId">Twin ID (partition key)</param>
    /// <param name="updatedDocument">Updated document data</param>
    /// <returns>True if updated successfully</returns>
    public async Task<bool> UpdateWorkDocumentAsync(string documentId, string twinId, Dictionary<string, object?> updatedDocument)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            // Ensure the document has the correct ID and TwinID
            updatedDocument["id"] = documentId;
            updatedDocument["TwinID"] = twinId;
            updatedDocument["updatedAt"] = DateTime.UtcNow.ToString("O");

            await workContainer.UpsertItemAsync(updatedDocument, new PartitionKey(twinId));

            _logger.LogInformation("?? Work document updated successfully: {DocumentId} for Twin: {TwinId}", 
                documentId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update work document: {DocumentId} for Twin: {TwinId}", 
                documentId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Delete a work document
    /// </summary>
    /// <param name="documentId">Document ID to delete</param>
    /// <param name="twinId">Twin ID (partition key)</param>
    /// <returns>True if deleted successfully</returns>
    public async Task<bool> DeleteWorkDocumentAsync(string documentId, string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            await workContainer.DeleteItemAsync<Dictionary<string, object?>>(
                documentId,
                new PartitionKey(twinId)
            );

            _logger.LogInformation("?? Work document deleted successfully: {DocumentId} for Twin: {TwinId}", 
                documentId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to delete work document: {DocumentId} for Twin: {TwinId}", 
                documentId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Get work documents filtered by document type
    /// </summary>
    /// <param name="twinId">Twin ID to filter by</param>
    /// <param name="documentType">Document type to filter by (e.g., "resume", "resume_optimization")</param>
    /// <returns>List of filtered work documents</returns>
    public async Task<List<Dictionary<string, object?>>> GetWorkDocumentsByTypeAsync(string twinId, string documentType)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.TwinID = @twinId AND c.documentType = @documentType ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@documentType", documentType);

            var iterator = workContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var workDocuments = new List<Dictionary<string, object?>>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                workDocuments.AddRange(response);
            }

            _logger.LogInformation("?? Found {Count} work documents of type '{DocumentType}' for Twin ID: {TwinId}", 
                workDocuments.Count, documentType, twinId);
            return workDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get work documents by type for Twin ID: {TwinId}", twinId);
            return new List<Dictionary<string, object?>>();
        }
    }

    /// <summary>
    /// Get work documents with advanced filtering and pagination
    /// </summary>
    /// <param name="twinId">Twin ID to filter by</param>
    /// <param name="query">Work document query parameters</param>
    /// <returns>List of filtered work documents</returns>
    public async Task<List<Dictionary<string, object?>>> GetFilteredWorkDocumentsAsync(string twinId, WorkDocumentQuery query)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            // Build dynamic SQL query based on filters
            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (!string.IsNullOrEmpty(query.DocumentType))
            {
                conditions.Add("c.documentType = @documentType");
                parameters["@documentType"] = query.DocumentType;
            }

            if (!string.IsNullOrEmpty(query.FileName))
            {
                conditions.Add("CONTAINS(LOWER(c.fileName), LOWER(@fileName))");
                parameters["@fileName"] = query.FileName;
            }

            if (!string.IsNullOrEmpty(query.FullName))
            {
                conditions.Add("CONTAINS(LOWER(c.fullName), LOWER(@fullName))");
                parameters["@fullName"] = query.FullName;
            }

            if (query.ProcessedFrom.HasValue)
            {
                conditions.Add("c.processedAt >= @processedFrom");
                parameters["@processedFrom"] = query.ProcessedFrom.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            if (query.ProcessedTo.HasValue)
            {
                conditions.Add("c.processedAt <= @processedTo");
                parameters["@processedTo"] = query.ProcessedTo.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            if (query.Success.HasValue)
            {
                conditions.Add("c.success = @success");
                parameters["@success"] = query.Success.Value;
            }

            // Build ORDER BY clause
            var orderBy = query.SortBy?.ToLowerInvariant() switch
            {
                "filename" => "c.fileName",
                "fullname" => "c.fullName",
                "processedat" => "c.processedAt",
                "createdat" => "c.createdAt",
                _ => "c.createdAt"
            };

            var orderDirection = query.SortDirection?.ToUpperInvariant() == "ASC" ? "ASC" : "DESC";

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY {orderBy} {orderDirection}";

            // Add pagination if specified
            if (query.Page > 1 || query.PageSize < 1000)
            {
                var offset = (query.Page - 1) * query.PageSize;
                sql += $" OFFSET {offset} LIMIT {query.PageSize}";
            }

            var cosmosQuery = new QueryDefinition(sql);
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = workContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var workDocuments = new List<Dictionary<string, object?>>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                workDocuments.AddRange(response);
            }

            _logger.LogInformation("?? Found {Count} filtered work documents for Twin ID: {TwinId}", 
                workDocuments.Count, twinId);
            return workDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get filtered work documents for Twin ID: {TwinId}", twinId);
            return new List<Dictionary<string, object?>>();
        }
    }

    /// <summary>
    /// Get work document statistics for a Twin
    /// </summary>
    /// <param name="twinId">Twin ID to get statistics for</param>
    /// <returns>Work document statistics</returns>
    public async Task<WorkDocumentStats> GetWorkDocumentStatsAsync(string twinId)
    {
        try
        {
            var workDocuments = await GetWorkDocumentsByTwinIdAsync(twinId);

            var stats = new WorkDocumentStats
            {
                TotalDocuments = workDocuments.Count,
                SuccessfulDocuments = workDocuments.Count(d => 
                    d.TryGetValue("success", out var success) && success is bool successBool && successBool),
                FailedDocuments = workDocuments.Count(d => 
                    d.TryGetValue("success", out var success) && success is bool successBool && !successBool),
                DocumentsByType = workDocuments
                    .Where(d => d.TryGetValue("documentType", out var type) && type != null)
                    .GroupBy(d => d["documentType"]!.ToString()!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RecentDocuments = workDocuments
                    .Where(d => d.TryGetValue("createdAt", out var created) && created != null)
                    .OrderByDescending(d => d["createdAt"])
                    .Take(5)
                    .Select(d => new WorkDocumentSummary
                    {
                        Id = d.GetValueOrDefault("id")?.ToString() ?? "",
                        DocumentType = d.GetValueOrDefault("documentType")?.ToString() ?? "",
                        FileName = d.GetValueOrDefault("fileName")?.ToString() ?? "",
                        FullName = d.GetValueOrDefault("fullName")?.ToString() ?? "",
                        ProcessedAt = TryParseDateTime(d.GetValueOrDefault("processedAt")),
                        Success = d.TryGetValue("success", out var success) && success is bool successBool && successBool
                    })
                    .ToList(),
                LastProcessedDate = workDocuments
                    .Where(d => d.TryGetValue("processedAt", out var processed) && processed != null)
                    .Select(d => TryParseDateTime(d["processedAt"]))
                    .Where(date => date.HasValue)
                    .OrderByDescending(date => date)
                    .FirstOrDefault()
            };

            _logger.LogInformation("?? Generated work document statistics for Twin ID: {TwinId} - Total: {Total}", 
                twinId, stats.TotalDocuments);
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get work document statistics for Twin ID: {TwinId}", twinId);
            return new WorkDocumentStats
            {
                TotalDocuments = 0,
                DocumentsByType = new Dictionary<string, int>(),
                RecentDocuments = new List<WorkDocumentSummary>()
            };
        }
    }

    /// <summary>
    /// Search work documents by text content
    /// </summary>
    /// <param name="twinId">Twin ID to search within</param>
    /// <param name="searchTerm">Text to search for</param>
    /// <param name="limit">Maximum number of results</param>
    /// <returns>List of matching work documents</returns>
    public async Task<List<Dictionary<string, object?>>> SearchWorkDocumentsAsync(string twinId, string searchTerm, int limit = 20)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");

            var query = new QueryDefinition(@"
                SELECT * FROM c 
                WHERE c.TwinID = @twinId 
                AND (
                    CONTAINS(LOWER(c.fullName), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.fileName), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.summary), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.currentJobTitle), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.currentCompany), LOWER(@searchTerm))
                )
                ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@searchTerm", searchTerm);

            var iterator = workContainer.GetItemQueryIterator<Dictionary<string, object?>>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = limit }
            );

            var workDocuments = new List<Dictionary<string, object?>>();
            var itemsProcessed = 0;

            while (iterator.HasMoreResults && itemsProcessed < limit)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    if (itemsProcessed >= limit) break;
                    workDocuments.Add(item);
                    itemsProcessed++;
                }
            }

            _logger.LogInformation("?? Found {Count} work documents matching '{SearchTerm}' for Twin ID: {TwinId}", 
                workDocuments.Count, searchTerm, twinId);
            return workDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to search work documents for Twin ID: {TwinId}", twinId);
            return new List<Dictionary<string, object?>>();
        }
    }

    // Helper method to safely parse DateTime
    private static DateTime? TryParseDateTime(object? value)
    {
        if (value == null) return null;
        
        try
        {
            if (value is DateTime dateTime)
                return dateTime;
            
            if (value is string dateString && DateTime.TryParse(dateString, out var parsedDate))
                return parsedDate;
            
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                var elementString = jsonElement.GetString();
                if (!string.IsNullOrEmpty(elementString) && DateTime.TryParse(elementString, out var elementDate))
                    return elementDate;
            }
        }
        catch (Exception)
        {
            // Silent fail for parsing
        }
        
        return null;
    }
}

/// <summary>
/// Query parameters for filtering work documents
/// </summary>
public class WorkDocumentQuery
{
    /// <summary>
    /// Filter by document type (e.g., "resume", "resume_optimization")
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>
    /// Filter by file name (partial match)
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Filter by full name (partial match)
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Filter by processed date from
    /// </summary>
    public DateTime? ProcessedFrom { get; set; }

    /// <summary>
    /// Filter by processed date to
    /// </summary>
    public DateTime? ProcessedTo { get; set; }

    /// <summary>
    /// Filter by success status
    /// </summary>
    public bool? Success { get; set; }

    /// <summary>
    /// Sort by field name
    /// </summary>
    public string? SortBy { get; set; } = "createdAt";

    /// <summary>
    /// Sort direction (ASC/DESC)
    /// </summary>
    public string? SortDirection { get; set; } = "DESC";

    /// <summary>
    /// Page number for pagination (1-based)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size for pagination
    /// </summary>
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Work document statistics
/// </summary>
public class WorkDocumentStats
{
    /// <summary>
    /// Total number of work documents
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// Number of successfully processed documents
    /// </summary>
    public int SuccessfulDocuments { get; set; }

    /// <summary>
    /// Number of failed documents
    /// </summary>
    public int FailedDocuments { get; set; }

    /// <summary>
    /// Count of documents by type
    /// </summary>
    public Dictionary<string, int> DocumentsByType { get; set; } = new();

    /// <summary>
    /// Recent documents (last 5)
    /// </summary>
    public List<WorkDocumentSummary> RecentDocuments { get; set; } = new();

    /// <summary>
    /// Last processed date
    /// </summary>
    public DateTime? LastProcessedDate { get; set; }
}

/// <summary>
/// Summary of a work document
/// </summary>
public class WorkDocumentSummary
{
    /// <summary>
    /// Document ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Document type
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// File name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Full name extracted from document
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// When the document was processed
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Whether processing was successful
    /// </summary>
    public bool Success { get; set; }
}