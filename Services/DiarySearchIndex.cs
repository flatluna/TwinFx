using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using System.Text.Json;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Azure AI Search Service specifically designed for DiaryComprehensiveAnalysisResult indexing and search
/// ========================================================================
/// 
/// This service creates and manages a search index optimized for comprehensive diary analysis results with:
/// - Vector search capabilities using Azure OpenAI embeddings
/// - Semantic search for natural language queries
/// - Full-text search across analysis content
/// - HTML report indexing and search
/// - Executive summary vector search
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class DiarySearchIndex
{
    private readonly ILogger<DiarySearchIndex> _logger;
    private readonly IConfiguration _configuration;
    private readonly SearchIndexClient? _indexClient;
    private readonly AzureOpenAIClient? _azureOpenAIClient;
    private readonly EmbeddingClient? _embeddingClient;
    
    // Configuration constants
    private const string DiaryIndexName = "diary-analysis-index";
    private const string VectorSearchProfile = "diary-analysis-vector-profile";
    private const string HnswAlgorithmConfig = "diary-analysis-hnsw-config";
    private const string VectorizerConfig = "diary-analysis-vectorizer";
    private const string SemanticConfig = "diary-analysis-semantic-config";
    private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions
    
    // Configuration keys
    private readonly string? _searchEndpoint;
    private readonly string? _searchApiKey;
    private readonly string? _openAIEndpoint;
    private readonly string? _openAIApiKey;
    private readonly string? _embeddingDeployment;

    public DiarySearchIndex(ILogger<DiarySearchIndex> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Load Azure Search configuration
        _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
        _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

        // Load Azure OpenAI configuration
        _openAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT") ?? GetConfigurationValue("AzureOpenAI:Endpoint");
        _openAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY") ?? GetConfigurationValue("AzureOpenAI:ApiKey");
        _embeddingDeployment = GetConfigurationValue("AZURE_OPENAI_EMBEDDING_DEPLOYMENT", "text-embedding-ada-002");

        // Initialize Azure Search client
        if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
        {
            try
            {
                var credential = new AzureKeyCredential(_searchApiKey);
                _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                _logger.LogInformation("📋 Diary Analysis Search Index client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error initializing Azure Search client for Diary Analysis Index");
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Azure Search credentials not found for Diary Analysis Index");
        }

        // Initialize Azure OpenAI client for embeddings
        if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
        {
            try
            {
                _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                _logger.LogInformation("🤖 Azure OpenAI embedding client initialized for Diary Analysis Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for Diary Analysis Index");
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Azure OpenAI credentials not found for Diary Analysis Index");
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
    /// Check if the diary analysis search service is available
    /// </summary>
    public bool IsAvailable => _indexClient != null;

    /// <summary>
    /// Create the diary analysis search index with vector and semantic search capabilities
    /// </summary>
    public async Task<DiaryIndexResult> CreateDiaryIndexAsync()
    {
        try
        {
            if (!IsAvailable)
            {
                return new DiaryIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("📋 Creating Diary Analysis Search Index: {IndexName}", DiaryIndexName);

            // Define search fields based EXACTLY on DiaryComprehensiveAnalysisResult properties from Models
            var fields = new List<SearchField>
            {
                // Primary identification field
                new SimpleField("id", SearchFieldDataType.String) 
                { 
                    IsKey = true, 
                    IsFilterable = true, 
                    IsSortable = true 
                },
                
                // DiaryComprehensiveAnalysisResult.Success
                new SimpleField("Success", SearchFieldDataType.Boolean) 
                { 
                    IsFilterable = true, 
                    IsFacetable = true 
                },
                
                // DiaryComprehensiveAnalysisResult.ErrorMessage
                
                
                // DiaryComprehensiveAnalysisResult.DiaryEntryId
                new SearchableField("DiaryEntryId") 
                { 
                    IsFilterable = true, 
                    IsFacetable = true 
                },
                
                // DiaryComprehensiveAnalysisResult.ExecutiveSummary
                new SearchableField("ExecutiveSummary") 
                { 
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // DiaryComprehensiveAnalysisResult.DetailedHtmlReport
                new SearchableField("DetailedHtmlReport") 
                { 
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // DiaryComprehensiveAnalysisResult.ProcessingTimeMs
                new SimpleField("ProcessingTimeMs", SearchFieldDataType.Double) 
                { 
                    IsFilterable = true, 
                    IsSortable = true 
                },
                 
                
                // DiaryComprehensiveAnalysisResult.Metadata (as keys and values for search)
                new SearchableField("MetadataKeys") 
                { 
                    IsFilterable = true, 
                    IsFacetable = true 
                },
                new SearchableField("MetadataValues") 
                { 
                    AnalyzerName = LexicalAnalyzerName.EsLucene 
                },
                
                // Combined content field for vector search
                new SearchableField("contenidoCompleto") 
                { 
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // Vector field for semantic similarity search
                new SearchField("contenidoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = EmbeddingDimensions,
                    VectorSearchProfileName = VectorSearchProfile
                }
            };

            // Configure vector search (simplified version without vectorizer)
            var vectorSearch = new VectorSearch();
            
            // Add HNSW algorithm configuration (simplified)
            vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswAlgorithmConfig));
            
            // Add vector search profile (manual embeddings only)
            vectorSearch.Profiles.Add(new VectorSearchProfile(VectorSearchProfile, HnswAlgorithmConfig));

            // Configure semantic search
            var semanticSearch = new SemanticSearch();
            var prioritizedFields = new SemanticPrioritizedFields
            {
                TitleField = new SemanticField("ExecutiveSummary")
            };
            
            // Content fields for semantic ranking
            prioritizedFields.ContentFields.Add(new SemanticField("DetailedHtmlReport"));
            prioritizedFields.ContentFields.Add(new SemanticField("contenidoCompleto"));
            
            // Keywords fields for semantic ranking
            prioritizedFields.KeywordsFields.Add(new SemanticField("DiaryEntryId"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("MetadataKeys"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("MetadataValues"));
            
            semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

            // Create the diary analysis search index
            var index = new SearchIndex(DiaryIndexName, fields)
            {
                VectorSearch = vectorSearch,
                SemanticSearch = semanticSearch
            };

            var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("✅ Diary Analysis Index '{IndexName}' created successfully", DiaryIndexName);
            
            return new DiaryIndexResult
            {
                Success = true,
                Message = $"Diary Analysis Index '{DiaryIndexName}' created successfully",
                IndexName = DiaryIndexName,
                FieldsCount = fields.Count,
                HasVectorSearch = true,
                HasSemanticSearch = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating Diary Analysis Index");
            return new DiaryIndexResult
            {
                Success = false,
                Error = $"Error creating Diary Analysis Index: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Index a diary comprehensive analysis result in Azure AI Search
    /// </summary>
    public async Task<DiaryIndexResult> IndexDiaryAnalysisAsync(DiaryComprehensiveAnalysisResult analysisResult)
    {
        try
        {
            if (!IsAvailable)
            {
                return new DiaryIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("📝 Indexing diary analysis: {Id} - Success: {Success}", 
                analysisResult.DiaryEntryId, analysisResult.Success);

            // Create search client for the diary analysis index
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), DiaryIndexName, new AzureKeyCredential(_searchApiKey!));

            // Build comprehensive content for vector search
            var contenidoCompleto = BuildCompleteAnalysisContent(analysisResult);

            // Generate embeddings for the complete content
            float[]? embeddings = null;
            if (_embeddingClient != null)
            {
                embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);
            }

            // Create search document using exact DiaryComprehensiveAnalysisResult field names from Models
            var searchDocument = new Dictionary<string, object>
            {
                ["id"] = GenerateDocumentId(analysisResult),
                ["Success"] = analysisResult.Success,
                ["DiaryEntryId"] = analysisResult.DiaryEntryId,
                ["ExecutiveSummary"] = analysisResult.ExecutiveSummary,
                ["DetailedHtmlReport"] = analysisResult.DetailedHtmlReport,
                ["ProcessingTimeMs"] = analysisResult.ProcessingTimeMs,
                ["AnalyzedAt"] = analysisResult.AnalyzedAt,
                ["contenidoCompleto"] = contenidoCompleto
            };

            // Add optional error message
            if (!string.IsNullOrEmpty(analysisResult.ErrorMessage))
            {
                searchDocument["ErrorMessage"] = analysisResult.ErrorMessage;
            }

            // Add metadata fields
            if (analysisResult.Metadata.Any())
            {
                searchDocument["MetadataKeys"] = string.Join(", ", analysisResult.Metadata.Keys);
                searchDocument["MetadataValues"] = string.Join(" ", analysisResult.Metadata.Values.Select(v => v?.ToString() ?? ""));
            }

            // Add vector embeddings if available
            if (embeddings != null)
            {
                searchDocument["contenidoVector"] = embeddings;
            }

            // Upload the document
            var documents = new[] { new SearchDocument(searchDocument) };
            var uploadResult = await searchClient.UploadDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();
            
            if (!errors.Any())
            {
                _logger.LogInformation("✅ Diary analysis indexed successfully: {Id}", analysisResult.DiaryEntryId);
                return new DiaryIndexResult
                {
                    Success = true,
                    Message = $"Diary analysis for entry '{analysisResult.DiaryEntryId}' indexed successfully",
                    IndexName = DiaryIndexName,
                    DocumentId = analysisResult.DiaryEntryId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("❌ Error indexing diary analysis {Id}: {Error}", analysisResult.DiaryEntryId, error.ErrorMessage);
                return new DiaryIndexResult
                {
                    Success = false,
                    Error = $"Error indexing diary analysis: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error indexing diary analysis: {Id}", analysisResult.DiaryEntryId);
            return new DiaryIndexResult
            {
                Success = false,
                Error = $"Error indexing diary analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Search diary analysis results using vector, semantic, or full-text search
    /// </summary>
    public async Task<DiarySearchResult> SearchDiaryAnalysisAsync(DiarySearchQuery query)
    {
        try
        {
            if (!IsAvailable)
            {
                return new DiarySearchResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("🔍 Searching diary analysis: '{Query}'", 
                query.SearchText?.Substring(0, Math.Min(query.SearchText.Length, 50)));

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), DiaryIndexName, new AzureKeyCredential(_searchApiKey!));

            // Build search options
            var searchOptions = new SearchOptions
            {
                Size = query.Top,
                Skip = (query.Page - 1) * query.Top,
                IncludeTotalCount = true
            };

            // Add field selection using exact field names from Models
            var fieldsToSelect = new[] 
            { 
                "id", "DiaryEntryId", "ExecutiveSummary", "Success", "ProcessingTimeMs", 
                "AnalyzedAt", "ErrorMessage", "DetailedHtmlReport" 
            };
            foreach (var field in fieldsToSelect)
            {
                searchOptions.Select.Add(field);
            }

            // Add filters
            var filters = new List<string>();

            // Date range filter using AnalyzedAt field
            if (query.DateFrom.HasValue)
            {
                filters.Add($"AnalyzedAt ge {query.DateFrom.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
            if (query.DateTo.HasValue)
            {
                filters.Add($"AnalyzedAt le {query.DateTo.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }

            // Success filter
            if (query.SuccessfulOnly)
            {
                filters.Add("Success eq true");
            }

            // Combine filters
            if (filters.Any())
            {
                searchOptions.Filter = string.Join(" and ", filters);
            }

            // Configure search type based on query
            string? searchText = null;
            
            if (!string.IsNullOrEmpty(query.SearchText))
            {
                if (query.UseSemanticSearch)
                {
                    // Configure semantic search
                    searchOptions.QueryType = SearchQueryType.Semantic;
                    searchOptions.SemanticSearch = new SemanticSearchOptions
                    {
                        SemanticConfigurationName = SemanticConfig,
                        QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                        QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
                    };
                    searchText = query.SearchText;
                }
                else if (query.UseVectorSearch && _embeddingClient != null)
                {
                    // Configure vector search
                    var queryEmbeddings = await GenerateEmbeddingsAsync(query.SearchText);
                    if (queryEmbeddings != null)
                    {
                        var vectorQuery = new VectorizedQuery(queryEmbeddings)
                        {
                            KNearestNeighborsCount = query.Top * 2, // Get more candidates for reranking
                            Fields = { "contenidoVector" }
                        };

                        searchOptions.VectorSearch = new VectorSearchOptions();
                        searchOptions.VectorSearch.Queries.Add(vectorQuery);
                    }
                    
                    // For hybrid search, also include text search
                    if (query.UseHybridSearch)
                    {
                        searchText = query.SearchText;
                    }
                }
                else
                {
                    // Use full-text search
                    searchText = query.SearchText;
                }
            }

            // Perform the search
            var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);

            // Process results
            var results = new List<DiarySearchResultItem>();
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                var item = new DiarySearchResultItem
                {
                    Id = result.Document.GetString("id"),
                    DiaryEntryId = result.Document.GetString("DiaryEntryId"),
                    ExecutiveSummary = result.Document.GetString("ExecutiveSummary"),
                    Success = result.Document.GetBoolean("Success") ?? false,
                    ProcessingTimeMs = result.Document.GetDouble("ProcessingTimeMs") ?? 0.0,
                    AnalyzedAt = result.Document.GetDateTimeOffset("AnalyzedAt") ?? DateTimeOffset.MinValue,
                    ErrorMessage = result.Document.GetString("ErrorMessage"),
                    DetailedHtmlReport = result.Document.GetString("DetailedHtmlReport"),
                    SearchScore = result.Score ?? 0.0
                };

                // Add semantic search highlights if available
                if (result.SemanticSearch?.Captions?.Any() == true)
                {
                    item.Highlights = result.SemanticSearch.Captions
                        .Select(c => c.Text ?? "")
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
                }

                results.Add(item);
            }

            _logger.LogInformation("✅ Diary analysis search completed: {Count} results found", results.Count);

            return new DiarySearchResult
            {
                Success = true,
                Results = results,
                TotalCount = (int)(searchResponse.Value.TotalCount ?? 0),
                Page = query.Page,
                PageSize = query.Top,
                SearchQuery = query.SearchText ?? "",
                SearchType = query.UseVectorSearch ? "Vector" : 
                           query.UseSemanticSearch ? "Semantic" : "FullText"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error searching diary analysis");
            return new DiarySearchResult
            {
                Success = false,
                Error = $"Error searching diary analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Delete a diary analysis from the search index
    /// </summary>
    public async Task<DiaryIndexResult> DeleteDiaryAnalysisAsync(string diaryEntryId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new DiaryIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("🗑️ Deleting diary analysis from index: {Id}", diaryEntryId);

            var searchClient = new SearchClient(new Uri(_searchEndpoint!), DiaryIndexName, new AzureKeyCredential(_searchApiKey!));

            var documentId = GenerateDocumentId(new DiaryComprehensiveAnalysisResult { DiaryEntryId = diaryEntryId });
            var document = new SearchDocument { ["id"] = documentId };
            var deleteResult = await searchClient.DeleteDocumentsAsync(new[] { document });

            var errors = deleteResult.Value.Results.Where(r => !r.Succeeded).ToList();
            
            if (!errors.Any())
            {
                _logger.LogInformation("✅ Diary analysis deleted from index: {Id}", diaryEntryId);
                return new DiaryIndexResult
                {
                    Success = true,
                    Message = $"Diary analysis '{diaryEntryId}' deleted from index",
                    DocumentId = diaryEntryId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("❌ Error deleting diary analysis {Id}: {Error}", diaryEntryId, error.ErrorMessage);
                return new DiaryIndexResult
                {
                    Success = false,
                    Error = $"Error deleting diary analysis: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting diary analysis from index: {Id}", diaryEntryId);
            return new DiaryIndexResult
            {
                Success = false,
                Error = $"Error deleting diary analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Generate embeddings using Azure OpenAI
    /// </summary>
    private async Task<float[]?> GenerateEmbeddingsAsync(string text)
    {
        try
        {
            if (_embeddingClient == null || string.IsNullOrEmpty(text))
            {
                return null;
            }

            _logger.LogDebug("🤖 Generating embeddings for text: {Length} characters", text.Length);

            var embeddingOptions = new EmbeddingGenerationOptions 
            { 
                Dimensions = EmbeddingDimensions 
            };

            var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
            var embeddings = embedding.Value.ToFloats().ToArray();

            _logger.LogDebug("✅ Generated embeddings: {Dimensions} dimensions", embeddings.Length);
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error generating embeddings, using fallback");
            return null;
        }
    }

    /// <summary>
    /// Build complete content for vector search by combining all relevant fields
    /// </summary>
    private static string BuildCompleteAnalysisContent(DiaryComprehensiveAnalysisResult result)
    {
        var content = new List<string>();

        // Core analysis content
        if (!string.IsNullOrEmpty(result.ExecutiveSummary)) 
            content.Add($"Resumen ejecutivo: {result.ExecutiveSummary}");
        
        if (!string.IsNullOrEmpty(result.DetailedHtmlReport)) 
            content.Add($"Reporte detallado HTML: {StripHtmlTags(result.DetailedHtmlReport)}");
        
        // Analysis metadata
        content.Add($"Análisis exitoso: {(result.Success ? "Sí" : "No")}");
        content.Add($"Tiempo de procesamiento: {result.ProcessingTimeMs:F2} ms");
        content.Add($"Fecha de análisis: {result.AnalyzedAt:yyyy-MM-dd HH:mm}");
        content.Add($"Entry ID: {result.DiaryEntryId}");
        
        // Error information if applicable
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            content.Add($"Error: {result.ErrorMessage}");
        }
        
        // Metadata
        if (result.Metadata.Any())
        {
            foreach (var metadata in result.Metadata)
            {
                content.Add($"{metadata.Key}: {metadata.Value}");
            }
        }

        return string.Join(". ", content);
    }

    /// <summary>
    /// Build analysis summary for search purposes
    /// </summary>
    private static string BuildAnalysisSummary(DiaryComprehensiveAnalysisResult result)
    {
        var summary = new List<string>();

        summary.Add($"Análisis de entrada {result.DiaryEntryId}");
        
        if (result.Success)
        {
            summary.Add("procesado exitosamente");
        }
        else
        {
            summary.Add("falló el procesamiento");
        }

        summary.Add($"en {result.ProcessingTimeMs:F0}ms");

        if (!string.IsNullOrEmpty(result.ExecutiveSummary))
        {
            var shortSummary = result.ExecutiveSummary.Length > 100 
                ? result.ExecutiveSummary.Substring(0, 100) + "..." 
                : result.ExecutiveSummary;
            summary.Add(shortSummary);
        }

        return string.Join(" ", summary);
    }

    /// <summary>
    /// Generate a unique document ID for the analysis result
    /// </summary>
    private static string GenerateDocumentId(DiaryComprehensiveAnalysisResult result)
    {
        return $"analysis_{result.DiaryEntryId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    /// <summary>
    /// Strip HTML tags from content for search indexing
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Simple HTML tag removal - for production consider using HtmlAgilityPack
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Trim();
    }

    /// <summary>
    /// Extract text content from receipt data object
    /// </summary>
    private static string ExtractReceiptText(object receiptData)
    {
        if (receiptData == null)
            return string.Empty;

        try
        {
            // Convert to JSON string and extract meaningful text
            var jsonString = JsonSerializer.Serialize(receiptData);
            
            // For now, return the JSON string - in production you might want to parse specific fields
            return StripJsonFormatting(jsonString);
        }
        catch
        {
            return receiptData.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Strip JSON formatting for better search indexing
    /// </summary>
    private static string StripJsonFormatting(string json)
    {
        if (string.IsNullOrEmpty(json))
            return string.Empty;

        // Simple JSON cleanup for search purposes
        return json.Replace("{", " ")
                  .Replace("}", " ")
                  .Replace("[", " ")
                  .Replace("]", " ")
                  .Replace("\"", " ")
                  .Replace(",", " ")
                  .Replace(":", " ")
                  .Trim();
    }
}

// Result classes for diary analysis search operations

/// <summary>
/// Result class for diary analysis index operations
/// </summary>
public class DiaryIndexResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? IndexName { get; set; }
    public string? DocumentId { get; set; }
    public int FieldsCount { get; set; }
    public bool HasVectorSearch { get; set; }
    public bool HasSemanticSearch { get; set; }
}

/// <summary>
/// Query parameters for diary analysis search
/// </summary>
public class DiarySearchQuery
{
    public string? SearchText { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool SuccessfulOnly { get; set; } = false;
    public bool UseVectorSearch { get; set; } = true;
    public bool UseSemanticSearch { get; set; } = false;
    public bool UseHybridSearch { get; set; } = false;
    public int Top { get; set; } = 10;
    public int Page { get; set; } = 1;
}

/// <summary>
/// Result class for diary analysis search operations
/// </summary>
public class DiarySearchResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<DiarySearchResultItem> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
}

/// <summary>
/// Individual diary analysis search result item
/// </summary>
public class DiarySearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string DiaryEntryId { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double ProcessingTimeMs { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DetailedHtmlReport { get; set; }
    public double SearchScore { get; set; }
    public List<string> Highlights { get; set; } = new();
}