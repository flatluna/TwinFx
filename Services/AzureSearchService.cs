using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure.AI.OpenAI;

namespace TwinFx.Services;

/// <summary>
/// Azure AI Search Service for creating and managing search indexes
/// Translated from Python version to .NET with configuration from local.settings.json
/// </summary>
public class AzureSearchService
{
    private readonly ILogger<AzureSearchService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SearchIndexClient? _indexClient;
    private readonly string? _endpoint;
    private readonly string? _apiKey;

    // Azure OpenAI configuration for embeddings
    private readonly string? _azureOpenAIEndpoint;
    private readonly string? _azureOpenAIApiKey;
    private readonly string? _azureOpenAIEmbeddingDeployment;
    private const string EmbeddingModelName = "text-embedding-ada-002";
    private const int EmbeddingDimensions = 1536; // Dimensions for text-embedding-ada-002

    public AzureSearchService(ILogger<AzureSearchService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Load Azure Search configuration from local.settings.json
        _endpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
        _apiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

        // Load Azure OpenAI configuration for embeddings
        _azureOpenAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT");
        _azureOpenAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY");
        _azureOpenAIEmbeddingDeployment = GetConfigurationValue("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME", "text-embedding-ada-002");

        if (!string.IsNullOrEmpty(_endpoint) && !string.IsNullOrEmpty(_apiKey))
        {
            try
            {
                var credential = new AzureKeyCredential(_apiKey);
                _indexClient = new SearchIndexClient(new Uri(_endpoint), credential);
                _logger.LogInformation("? Azure Search client initialized successfully");
                _logger.LogInformation("?? Endpoint: {Endpoint}", _endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error initializing Azure Search client");
            }
        }
        else
        {
            _logger.LogWarning("?? Azure Search credentials not found in configuration");
        }
    }

    /// <summary>
    /// Get configuration value with fallback to Values section (Azure Functions format)
    /// </summary>
    private string? GetConfigurationValue(string key, string? defaultValue = null)
    {
        // Try direct key first
        var value = _configuration.GetValue<string>(key);
        
        // Try Values section if not found (Azure Functions format)
        if (string.IsNullOrEmpty(value))
        {
            value = _configuration.GetValue<string>($"Values:{key}");
        }
        
        return !string.IsNullOrEmpty(value) ? value : defaultValue;
    }

    /// <summary>
    /// Check if Azure Search is available
    /// </summary>
    public bool IsAvailable => _indexClient != null;

    /// <summary>
    /// Create a search index for twin documents with vector support
    /// </summary>
    public async Task<SearchIndexResult> CreateTwinDocumentsIndexAsync(string indexName)
    {
        try
        {
            if (!IsAvailable)
            {
                return new SearchIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Creating Azure Search index: {IndexName}", indexName);

            // Define index fields
            var fields = new List<SearchField>
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsSortable = true, IsFilterable = true, IsFacetable = true },
                new SearchableField("twinId") { IsFilterable = true },
                new SearchableField("documentType") { IsFilterable = true },
                new SearchableField("fileName") { IsFilterable = true },
                new SimpleField("processedAt", SearchFieldDataType.DateTimeOffset) { IsSortable = true, IsFilterable = true },
                
                // Text field for storing the plain text report
                new SearchableField("reporteTextoPlano"),
                
                // Vector field for storing the plain text report embedding
                new SearchField("reporteTextoPlanoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = EmbeddingDimensions,
                    VectorSearchProfileName = "myHnswProfile"
                }
            };

            // Configure vector search with simplified approach
            var vectorSearch = new VectorSearch();
            
            // Add HNSW algorithm configuration
            vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("myHnsw"));
            
            // Add vector search profile
            vectorSearch.Profiles.Add(new VectorSearchProfile("myHnswProfile", "myHnsw"));

            // Configure semantic search with proper constructor
            var semanticSearch = new SemanticSearch();
            var prioritizedFields = new SemanticPrioritizedFields
            {
                TitleField = new SemanticField("fileName")
            };
            prioritizedFields.ContentFields.Add(new SemanticField("reporteTextoPlano"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("documentType"));
            
            semanticSearch.Configurations.Add(new SemanticConfiguration("my-semantic-config", prioritizedFields));

            // Create the search index
            var index = new SearchIndex(indexName, fields)
            {
                VectorSearch = vectorSearch,
                SemanticSearch = semanticSearch
            };

            try
            {
                var result = await _indexClient.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("? Index '{IndexName}' created successfully", indexName);
                
                return new SearchIndexResult
                {
                    Success = true,
                    Message = $"Index '{indexName}' created successfully",
                    IndexName = indexName,
                    FieldsCount = fields.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error creating index: {IndexName}", indexName);
                return new SearchIndexResult
                {
                    Success = false,
                    Error = $"Error creating index: {ex.Message}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Unexpected error creating index: {IndexName}", indexName);
            return new SearchIndexResult
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Delete a search index
    /// </summary>
    public async Task<SearchIndexResult> DeleteIndexAsync(string indexName)
    {
        try
        {
            if (!IsAvailable)
            {
                return new SearchIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("??? Deleting Azure Search index: {IndexName}", indexName);

            await _indexClient!.DeleteIndexAsync(indexName);
            _logger.LogInformation("? Index '{IndexName}' deleted successfully", indexName);

            return new SearchIndexResult
            {
                Success = true,
                Message = $"Index '{indexName}' deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting index: {IndexName}", indexName);
            return new SearchIndexResult
            {
                Success = false,
                Error = $"Error deleting index: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// List all available search indexes
    /// </summary>
    public async Task<SearchIndexListResult> ListIndexesAsync()
    {
        try
        {
            if (!IsAvailable)
            {
                return new SearchIndexListResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Listing Azure Search indexes");

            var indexes = new List<string>();
            await foreach (var index in _indexClient!.GetIndexesAsync())
            {
                indexes.Add(index.Name);
            }

            _logger.LogInformation("? Found {Count} indexes", indexes.Count);

            return new SearchIndexListResult
            {
                Success = true,
                Indexes = indexes,
                Count = indexes.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error listing indexes");
            return new SearchIndexListResult
            {
                Success = false,
                Error = $"Error listing indexes: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Check if an index exists
    /// </summary>
    public async Task<bool> IndexExistsAsync(string indexName)
    {
        try
        {
            if (!IsAvailable)
                return false;

            await _indexClient!.GetIndexAsync(indexName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generate embeddings for text using Azure OpenAI
    /// Note: This is a placeholder implementation. In production, you would call Azure OpenAI API
    /// </summary>
    public async Task<float[]> GenerateEmbeddingsAsync(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(_azureOpenAIEndpoint) || string.IsNullOrEmpty(_azureOpenAIApiKey))
            {
                _logger.LogWarning("? Azure OpenAI credentials not found in configuration");
                return CreateDummyEmbeddings(); // Return dummy embeddings as fallback
            }

            _logger.LogInformation("?? Generating embeddings for text: {TextLength} characters", text.Length);
            
            // TODO: Implement actual Azure OpenAI embeddings API call
            // For now, return dummy embeddings to make the service functional
            _logger.LogWarning("?? Using dummy embeddings - implement actual Azure OpenAI API call for production");
            
            // Simulate async operation
            await Task.Delay(100);
            
            var embeddings = CreateDummyEmbeddings();
            _logger.LogInformation("? Generated embeddings successfully: {Dimensions} dimensions", embeddings.Length);
            
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating embeddings");
            // Return dummy embeddings as fallback
            return CreateDummyEmbeddings();
        }
    }

    /// <summary>
    /// Create dummy embeddings for testing purposes
    /// </summary>
    private static float[] CreateDummyEmbeddings()
    {
        var random = new Random();
        var embeddings = new float[EmbeddingDimensions];
        for (int i = 0; i < EmbeddingDimensions; i++)
        {
            embeddings[i] = (float)(random.NextDouble() * 2.0 - 1.0); // Values between -1 and 1
        }
        return embeddings;
    }

    /// <summary>
    /// Upload documents to an Azure AI Search index
    /// </summary>
    public async Task<SearchDocumentResult> UploadDocumentsAsync(string indexName, List<Dictionary<string, object>> documents)
    {
        try
        {
            if (!IsAvailable)
            {
                return new SearchDocumentResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            if (!await IndexExistsAsync(indexName))
            {
                _logger.LogError("? Index '{IndexName}' does not exist", indexName);
                return new SearchDocumentResult
                {
                    Success = false,
                    Error = $"Index '{indexName}' does not exist"
                };
            }

            _logger.LogInformation("?? Uploading {Count} documents to index '{IndexName}'", documents.Count, indexName);

            // Create search client for the index
            var searchClient = new SearchClient(new Uri(_endpoint!), indexName, new AzureKeyCredential(_apiKey!));

            // Convert documents to SearchDocument objects
            var searchDocuments = documents.Select(doc => new SearchDocument(doc)).ToList();

            // Upload documents
            var result = await searchClient.UploadDocumentsAsync(searchDocuments);

            // Check results
            var errors = result.Value.Results.Where(r => !r.Succeeded).ToList();
            
            if (!errors.Any())
            {
                _logger.LogInformation("? Successfully uploaded {Count} documents to index '{IndexName}'", documents.Count, indexName);
                return new SearchDocumentResult
                {
                    Success = true,
                    Message = $"Successfully uploaded {documents.Count} documents",
                    Count = documents.Count
                };
            }
            else
            {
                _logger.LogError("? Errors uploading documents: {ErrorCount} failed", errors.Count);
                return new SearchDocumentResult
                {
                    Success = false,
                    Error = $"Errors uploading documents: {errors.Count} failed",
                    CountSuccess = documents.Count - errors.Count,
                    CountErrors = errors.Count,
                    Errors = errors.Select(e => e.ErrorMessage ?? "Unknown error").ToList()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error uploading documents to index: {IndexName}", indexName);
            return new SearchDocumentResult
            {
                Success = false,
                Error = $"Error uploading documents: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Perform a vector search using embeddings
    /// </summary>
    public async Task<SearchVectorResult> VectorSearchAsync(string indexName, string queryText, int top = 5)
    {
        try
        {
            if (!IsAvailable)
            {
                return new SearchVectorResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            if (!await IndexExistsAsync(indexName))
            {
                _logger.LogError("? Index '{IndexName}' does not exist", indexName);
                return new SearchVectorResult
                {
                    Success = false,
                    Error = $"Index '{indexName}' does not exist"
                };
            }

            _logger.LogInformation("?? Performing vector search in index '{IndexName}' for: {QueryText}", indexName, queryText.Length > 50 ? queryText[..50] + "..." : queryText);

            // Generate embeddings for the query
            var queryEmbeddings = await GenerateEmbeddingsAsync(queryText);

            if (queryEmbeddings == null || queryEmbeddings.Length == 0)
            {
                return new SearchVectorResult
                {
                    Success = false,
                    Error = "Failed to generate embeddings for query"
                };
            }

            // Create search client for the index
            var searchClient = new SearchClient(new Uri(_endpoint!), indexName, new AzureKeyCredential(_apiKey!));

            // Create vector query
            var vectorQuery = new VectorizedQuery(queryEmbeddings)
            {
                KNearestNeighborsCount = top,
                Fields = { "reporteTextoPlanoVector" }
            };

            // Perform vector search
            var searchOptions = new SearchOptions
            {
                Size = top
            };
            
            searchOptions.VectorSearch = new();
            searchOptions.VectorSearch.Queries.Add(vectorQuery);
            
            searchOptions.Select.Add("id");
            searchOptions.Select.Add("twinId");
            searchOptions.Select.Add("documentType");
            searchOptions.Select.Add("fileName");
            searchOptions.Select.Add("processedAt");
            searchOptions.Select.Add("reporteTextoPlano");

            var response = await searchClient.SearchAsync<SearchDocument>(null, searchOptions);

            // Process results
            var documents = new List<Dictionary<string, object>>();
            await foreach (var result in response.Value.GetResultsAsync())
            {
                var doc = new Dictionary<string, object>();
                foreach (var kvp in result.Document)
                {
                    doc[kvp.Key] = kvp.Value ?? "";
                }
                // Add search score
                doc["@search.score"] = result.Score ?? 0.0;
                documents.Add(doc);
            }

            _logger.LogInformation("? Vector search found {Count} results", documents.Count);

            return new SearchVectorResult
            {
                Success = true,
                Count = documents.Count,
                Results = documents
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error performing vector search in index: {IndexName}", indexName);
            return new SearchVectorResult
            {
                Success = false,
                Error = $"Error performing vector search: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Result class for search index operations
/// </summary>
public class SearchIndexResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? IndexName { get; set; }
    public int FieldsCount { get; set; }
}

/// <summary>
/// Result class for listing indexes
/// </summary>
public class SearchIndexListResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> Indexes { get; set; } = new();
    public int Count { get; set; }
}

/// <summary>
/// Result class for document upload operations
/// </summary>
public class SearchDocumentResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public int Count { get; set; }
    public int CountSuccess { get; set; }
    public int CountErrors { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result class for vector search operations
/// </summary>
public class SearchVectorResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int Count { get; set; }
    public List<Dictionary<string, object>> Results { get; set; } = new();
}