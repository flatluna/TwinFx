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
using TwinFx.Services;

namespace TwinFx.Services;

/// <summary>
/// Azure AI Search Service specifically designed for Homes/Properties indexing and search
/// ========================================================================
/// 
/// This service creates and manages a search index optimized for homes and properties with:
/// - Vector search capabilities using Azure OpenAI embeddings
/// - Semantic search for natural language queries about properties
/// - Full-text search across home descriptions and features
/// - Address-based search and filtering
/// - Property status tracking and filtering
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class HomesSearchIndex
{
    private readonly ILogger<HomesSearchIndex> _logger;
    private readonly IConfiguration _configuration;
    private readonly SearchIndexClient? _indexClient;
    private readonly AzureOpenAIClient? _azureOpenAIClient;
    private readonly EmbeddingClient? _embeddingClient;

    // Configuration constants
    private const string HomesIndexName = "homes-properties-index";
    private const string VectorSearchProfile = "homes-vector-profile";
    private const string HnswAlgorithmConfig = "homes-hnsw-config";
    private const string VectorizerConfig = "homes-vectorizer";
    private const string SemanticConfig = "homes-semantic-config";
    private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions

    // Configuration keys
    private readonly string? _searchEndpoint;
    private readonly string? _searchApiKey;
    private readonly string? _openAIEndpoint;
    private readonly string? _openAIApiKey;
    private readonly string? _embeddingDeployment;

    public HomesSearchIndex(ILogger<HomesSearchIndex> logger, IConfiguration configuration)
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
                _logger.LogInformation("?? Homes Search Index client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error initializing Azure Search client for Homes Index");
            }
        }
        else
        {
            _logger.LogWarning("?? Azure Search credentials not found for Homes Index");
        }

        // Initialize Azure OpenAI client for embeddings
        if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
        {
            try
            {
                _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                _logger.LogInformation("?? Azure OpenAI embedding client initialized for Homes Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error initializing Azure OpenAI client for Homes Index");
            }
        }
        else
        {
            _logger.LogWarning("?? Azure OpenAI credentials not found for Homes Index");
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
    /// Check if the homes search service is available
    /// </summary>
    public bool IsAvailable => _indexClient != null;

    /// <summary>
    /// Create the homes search index with vector and semantic search capabilities
    /// </summary>
    public async Task<HomesIndexResult> CreateHomesIndexAsync()
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("🏠 Creating Homes Search Index: {IndexName}", HomesIndexName);

            // Define search fields based EXACTLY on DiarySearchIndex but adapted for Homes
            var fields = new List<SearchField>
            {
                // Primary identification field
                new SimpleField("id", SearchFieldDataType.String)
                {
                    IsKey = true,
                    IsFilterable = true,
                    IsSortable = true
                },
                
                // Success (equivalent to DiaryComprehensiveAnalysisResult.Success)
                new SimpleField("Success", SearchFieldDataType.Boolean)
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // HomeId (equivalent to DiaryEntryId)
                new SearchableField("HomeId")
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // TwinId field for filtering by specific twin
                new SearchableField("TwinId")
                {
                    IsFilterable = true,
                    IsFacetable = true,
                    IsSortable = true
                },
                
                // ExecutiveSummary (equivalent to DiaryComprehensiveAnalysisResult.ExecutiveSummary)
                new SearchableField("ExecutiveSummary")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // DetailedHtmlReport (equivalent to DiaryComprehensiveAnalysisResult.DetailedHtmlReport)
                new SearchableField("DetailedHtmlReport")
                {
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // ProcessingTimeMs (equivalent to DiaryComprehensiveAnalysisResult.ProcessingTimeMs)
                new SimpleField("ProcessingTimeMs", SearchFieldDataType.Double)
                {
                    IsFilterable = true,
                    IsSortable = true
                },
                
                // AnalyzedAt field for timestamp filtering
                new SimpleField("AnalyzedAt", SearchFieldDataType.DateTimeOffset)
                {
                    IsFilterable = true,
                    IsSortable = true,
                    IsFacetable = true
                },
                
                // MetadataKeys (equivalent to DiaryComprehensiveAnalysisResult.Metadata as keys)
                new SearchableField("MetadataKeys")
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // MetadataValues (equivalent to DiaryComprehensiveAnalysisResult.Metadata as values)
                new SearchableField("MetadataValues")
                {
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: Address field (comprehensive address search)
                new SearchableField("Address")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: Status field (active, sold, rented, etc.)
                new SearchableField("Status")
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // NEW: HomeInsurance field (home insurance documents and information)
                new SearchableField("HomeInsurance")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: HomeTitle field (property title documents and information)
                new SearchableField("HomeTitle")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: Inspections field (home inspection reports and information)
                new SearchableField("Inspections")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: Invoices field (home-related invoices and billing information)
                new SearchableField("Invoices")
                {
                    IsFilterable = true,
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
            prioritizedFields.ContentFields.Add(new SemanticField("Address"));

            // Keywords fields for semantic ranking
            prioritizedFields.KeywordsFields.Add(new SemanticField("HomeId"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("MetadataKeys"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("MetadataValues"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("Status"));

            semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

            // Create the homes search index
            var index = new SearchIndex(HomesIndexName, fields)
            {
                VectorSearch = vectorSearch,
                SemanticSearch = semanticSearch
            };

            var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("✅ Homes Index '{IndexName}' created successfully", HomesIndexName);

            return new HomesIndexResult
            {
                Success = true,
                Message = $"Homes Index '{HomesIndexName}' created successfully",
                IndexName = HomesIndexName,
                FieldsCount = fields.Count,
                HasVectorSearch = true,
                HasSemanticSearch = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating Homes Index");
            return new HomesIndexResult
            {
                Success = false,
                Error = $"Error creating Homes Index: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Index a home/property in Azure AI Search
    /// This method will update existing documents with the same HomeId instead of creating duplicates
    /// </summary>
    public async Task<HomesIndexResult> IndexHomeAsync(HomeData homeData)
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("🏡 Indexing home: {Id} - {Direccion}, {Ciudad}",
                homeData.Id, homeData.Direccion, homeData.Ciudad);

            // Create search client for the homes index
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));

            // Check if document already exists with this HomeId
            var existingDocumentId = await FindExistingDocumentIdAsync(searchClient, homeData.Id);
            var documentId = existingDocumentId ?? homeData.Id;
            
            var isUpdate = existingDocumentId != null;
            
            _logger.LogInformation(isUpdate 
                ? "🔄 Updating existing document with ID: {DocumentId} for HomeId: {HomeId}"
                : "✨ Creating new document with ID: {DocumentId} for HomeId: {HomeId}",
                documentId, homeData.Id);

            // Build comprehensive content for vector search
            var contenidoCompleto = BuildCompleteHomeContent(homeData);

            // Generate embeddings for the complete content
            float[]? embeddings = null;
            if (_embeddingClient != null)
            {
                embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);
            }

            // Build comprehensive address field
            var address = BuildCompleteAddress(homeData);

            // Determine property status
            var status = DeterminePropertyStatus(homeData);

            // Create search document using EXACT same structure as DiarySearchIndex
            var searchDocument = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["Success"] = true, // Always true for successfully indexed homes
                ["HomeId"] = homeData.Id, // Equivalent to DiaryEntryId
                ["TwinId"] = homeData.TwinID,
                ["ExecutiveSummary"] = BuildHomeSummary(homeData), // Equivalent to ExecutiveSummary
                ["DetailedHtmlReport"] = BuildHomeHtmlReport(homeData), // Equivalent to DetailedHtmlReport
                ["ProcessingTimeMs"] = 0.0, // Placeholder for processing time
                ["AnalyzedAt"] = DateTime.UtcNow, // Use current time for indexing timestamp
                ["MetadataKeys"] = BuildMetadataKeys(homeData), // Home-specific metadata keys
                ["MetadataValues"] = BuildMetadataValues(homeData), // Home-specific metadata values
                ["Address"] = address, // NEW: Comprehensive address field
                ["Status"] = status, // NEW: Property status field
                ["contenidoCompleto"] = contenidoCompleto
            };

            // Handle document fields differently for new vs existing documents
            if (isUpdate)
            {
                // For existing documents, get current values of document fields to preserve them
                var existingDocumentFields = await GetExistingDocumentFieldsAsync(searchClient, documentId);
                
                // Only add document fields if they don't exist or are empty in the current document
                if (string.IsNullOrEmpty(existingDocumentFields.HomeInsurance))
                    searchDocument["HomeInsurance"] = "";
                if (string.IsNullOrEmpty(existingDocumentFields.HomeTitle))
                    searchDocument["HomeTitle"] = "";
                if (string.IsNullOrEmpty(existingDocumentFields.Inspections))
                    searchDocument["Inspections"] = "";
                if (string.IsNullOrEmpty(existingDocumentFields.Invoices))
                    searchDocument["Invoices"] = "";
                    
                _logger.LogInformation("🔄 Preserving existing document fields for update");
            }
            else
            {
                // For new documents, initialize all document fields as empty
                searchDocument["HomeInsurance"] = ""; // NEW: Home insurance documents (empty by default)
                searchDocument["HomeTitle"] = ""; // NEW: Property title documents (empty by default)
                searchDocument["Inspections"] = ""; // NEW: Home inspection reports (empty by default)
                searchDocument["Invoices"] = ""; // NEW: Home-related invoices (empty by default)
                
                _logger.LogInformation("✨ Initializing document fields for new document");
            }

            // Add vector embeddings if available
            if (embeddings != null)
            {
                searchDocument["contenidoVector"] = embeddings;
            }

            // Use MergeOrUploadDocumentsAsync to update existing or create new
            var documents = new[] { new SearchDocument(searchDocument) };
            var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("✅ Home {Action} successfully: HomeId={HomeId}, TwinId={TwinId}, Address={Address}", 
                    isUpdate ? "updated" : "indexed", homeData.Id, homeData.TwinID, address);
                    
                return new HomesIndexResult
                {
                    Success = true,
                    Message = $"Home '{homeData.Direccion}' {(isUpdate ? "updated" : "indexed")} successfully",
                    IndexName = HomesIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("❌ Error {Action} home {Id}: {Error}", 
                    isUpdate ? "updating" : "indexing", homeData.Id, error.ErrorMessage);
                return new HomesIndexResult
                {
                    Success = false,
                    Error = $"Error {(isUpdate ? "updating" : "indexing")} home: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error indexing home: {Id}", homeData.Id);
            return new HomesIndexResult
            {
                Success = false,
                Error = $"Error indexing home: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Index a comprehensive home analysis result in Azure AI Search
    /// This method will update existing documents with the same HomeId instead of creating duplicates
    /// </summary>
    public async Task<HomesIndexResult> IndexHomeAnalysisAsync(HomeComprehensiveAnalysisResult analysisResult, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("🏡 Indexing home analysis: {Id} - Success: {Success}",
                analysisResult.HomeId, analysisResult.Success);

            // Create search client for the homes index
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));

            // Check if document already exists with this HomeId
            var existingDocumentId = await FindExistingDocumentIdByHomeIdAsync(searchClient, analysisResult.HomeId);
            var documentId = existingDocumentId ?? await GenerateUniqueHomeDocumentId(analysisResult);
            
            var isUpdate = existingDocumentId != null;
            
            _logger.LogInformation(isUpdate 
                ? "🔄 Updating existing document with ID: {DocumentId} for HomeId: {HomeId}"
                : "✨ Creating new document with ID: {DocumentId} for HomeId: {HomeId}",
                documentId, analysisResult.HomeId);

            // Build comprehensive content for vector search
            var contenidoCompleto = BuildCompleteAnalysisContent(analysisResult);

            // Generate embeddings for the complete content
            float[]? embeddings = null;
            if (_embeddingClient != null)
            {
                embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);
            }

            // Create search document using exact same structure as DiarySearchIndex
            var searchDocument = new Dictionary<string, object>
            {
                ["id"] = documentId, // Use the determined document ID (existing or new)
                ["Success"] = analysisResult.Success,
                ["HomeId"] = analysisResult.HomeId, // Equivalent to DiaryEntryId
                ["TwinId"] = twinId,
                ["ExecutiveSummary"] = analysisResult.ExecutiveSummary,
                ["DetailedHtmlReport"] = analysisResult.DetailedHtmlReport,
                ["ProcessingTimeMs"] = analysisResult.ProcessingTimeMs,
                ["AnalyzedAt"] = DateTime.UtcNow, // Use current time for indexing timestamp
                ["Address"] = analysisResult.Address ?? "", // NEW: Comprehensive address field
                ["Status"] = analysisResult.Status ?? "", // NEW: Property status field
                ["contenidoCompleto"] = contenidoCompleto
            };

            // Handle document fields differently for new vs existing documents
            if (isUpdate)
            {
                // For existing documents, get current values of document fields to preserve them
                var existingDocumentFields = await GetExistingDocumentFieldsAsync(searchClient, documentId);
                
                // Only add document fields if they don't exist or are empty in the current document
                if (string.IsNullOrEmpty(existingDocumentFields.HomeInsurance))
                    searchDocument["HomeInsurance"] = "";
                if (string.IsNullOrEmpty(existingDocumentFields.HomeTitle))
                    searchDocument["HomeTitle"] = "";
                if (string.IsNullOrEmpty(existingDocumentFields.Inspections))
                    searchDocument["Inspections"] = "";
                if (string.IsNullOrEmpty(existingDocumentFields.Invoices))
                    searchDocument["Invoices"] = "";
                    
                _logger.LogInformation("🔄 Preserving existing document fields for analysis update");
            }
            else
            {
                // For new documents, initialize all document fields as empty
                searchDocument["HomeInsurance"] = ""; // NEW: Home insurance documents (empty by default)
                searchDocument["HomeTitle"] = ""; // NEW: Property title documents (empty by default)
                searchDocument["Inspections"] = ""; // NEW: Home inspection reports (empty by default)
                searchDocument["Invoices"] = ""; // NEW: Home-related invoices (empty by default)
                
                _logger.LogInformation("✨ Initializing document fields for new analysis document");
            }

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

            // Use MergeOrUploadDocumentsAsync to update existing or create new
            var documents = new[] { new SearchDocument(searchDocument) };
            var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("✅ Home analysis {Action} successfully: HomeId={HomeId}, DocumentId={DocumentId}, TwinId={TwinId}", 
                    isUpdate ? "updated" : "indexed", analysisResult.HomeId, documentId, twinId);
                    
                return new HomesIndexResult
                {
                    Success = true,
                    Message = $"Home analysis for '{analysisResult.HomeId}' {(isUpdate ? "updated" : "indexed")} successfully",
                    IndexName = HomesIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("❌ Error {Action} home analysis {Id}: {Error}", 
                    isUpdate ? "updating" : "indexing", analysisResult.HomeId, error.ErrorMessage);
                return new HomesIndexResult
                {
                    Success = false,
                    Error = $"Error {(isUpdate ? "updating" : "indexing")} home analysis: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error indexing home analysis: {Id}", analysisResult.HomeId);
            return new HomesIndexResult
            {
                Success = false,
                Error = $"Error indexing home analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Parse AI response and index the comprehensive home analysis
    /// </summary>
    public async Task<HomesIndexResult> IndexHomeAnalysisFromAIResponseAsync(string aiJsonResponse, string homeId, string twinId, double processingTimeMs = 0.0)
    {
        try
        {
            _logger.LogInformation("?? Parsing and indexing home analysis from AI response: {HomeId}", homeId);

            // Parse the AI response into a structured result
            var analysisResult = HomeComprehensiveAnalysisResult.ParseFromAIResponse(aiJsonResponse, homeId);
            
            // Set processing time
            analysisResult.ProcessingTimeMs = processingTimeMs;

            // Index the analysis result
            return await IndexHomeAnalysisAsync(analysisResult, twinId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error parsing and indexing AI response for home: {HomeId}", homeId);
            return new HomesIndexResult
            {
                Success = false,
                Error = $"Error parsing and indexing AI response: {ex.Message}",
                DocumentId = homeId
            };
        }
    }

    /// <summary>
    /// Get home by ID from search index
    /// </summary>
    public async Task<HomesGetResult> GetHomeByIdAsync(string homeId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesGetResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Getting home by ID: {HomeId} for TwinId: {TwinId}", homeId, twinId);

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));

            // Build search options with filters
            var searchOptions = new SearchOptions
            {
                Filter = $"HomeId eq '{homeId}' and TwinId eq '{twinId}'",
                Size = 1,
                IncludeTotalCount = true
            };

            // Add field selection for the response
            var fieldsToSelect = new[]
            {
                "id", "HomeId", "TwinId", "Success", "ExecutiveSummary", "DetailedHtmlReport",
                "ProcessingTimeMs", "AnalyzedAt", "MetadataKeys", "MetadataValues",
                "contenidoCompleto", "HomeInsurance", "Address", "Status"
            };
            foreach (var field in fieldsToSelect)
            {
                searchOptions.Select.Add(field);
            }

            // Perform the search using wildcard query to match any document with the filters
            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);

            // Process results
            HomesSearchResultItem? homeItem = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                homeItem = new HomesSearchResultItem
                {
                    Id = result.Document.GetString("id") ?? string.Empty,
                    HomeId = result.Document.GetString("HomeId") ?? string.Empty,
                    TwinId = result.Document.GetString("TwinId") ?? string.Empty,
                    ExecutiveSummary = result.Document.GetString("ExecutiveSummary") ?? string.Empty,
                    Success = result.Document.GetBoolean("Success") ?? false,
                    ProcessingTimeMs = result.Document.GetDouble("ProcessingTimeMs") ?? 0.0,
                    AnalyzedAt = result.Document.GetDateTimeOffset("AnalyzedAt") ?? DateTimeOffset.MinValue,
                    DetailedHtmlReport = result.Document.GetString("DetailedHtmlReport") ?? string.Empty,
                    Address = result.Document.GetString("Address") ?? string.Empty,
                    Status = result.Document.GetString("Status") ?? string.Empty,
                    HomeInsurance = result.Document.GetString("HomeInsurance") ?? string.Empty
                };
                
                // We only expect one result due to the specific filter
                break;
            }

            if (homeItem != null)
            {
                _logger.LogInformation("? Home found for HomeId: {HomeId}", homeId);
                
                return new HomesGetResult
                {
                    Success = true,
                    HomeId = homeId,
                    TwinId = twinId,
                    Home = homeItem,
                    Message = $"Home retrieved successfully for ID {homeId}"
                };
            }
            else
            {
                _logger.LogInformation("?? No home found for HomeId: {HomeId}, TwinId: {TwinId}", homeId, twinId);
                
                return new HomesGetResult
                {
                    Success = false,
                    HomeId = homeId,
                    TwinId = twinId,
                    Error = $"No home found with ID {homeId}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting home for HomeId: {HomeId}, TwinId: {TwinId}", homeId, twinId);
            
            return new HomesGetResult
            {
                Success = false,
                HomeId = homeId,
                TwinId = twinId,
                Error = $"Error retrieving home: {ex.Message}"
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

            _logger.LogDebug("?? Generating embeddings for text: {Length} characters", text.Length);

            var embeddingOptions = new EmbeddingGenerationOptions
            {
                Dimensions = EmbeddingDimensions
            };

            var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
            var embeddings = embedding.Value.ToFloats().ToArray();

            _logger.LogDebug("? Generated embeddings: {Dimensions} dimensions", embeddings.Length);
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error generating embeddings, using fallback");
            return null;
        }
    }

    /// <summary>
    /// Build complete content for vector search by combining all relevant fields
    /// </summary>
    private static string BuildCompleteHomeContent(HomeData home)
    {
        var content = new List<string>();

        // Basic property information
        content.Add($"Propiedad: {home.TipoPropiedad}");
        content.Add($"Tipo: {home.Tipo}");
        content.Add($"Dirección: {home.Direccion}");
        content.Add($"Ciudad: {home.Ciudad}");
        content.Add($"Estado: {home.Estado}");
        
        if (!string.IsNullOrEmpty(home.CodigoPostal))
            content.Add($"Código postal: {home.CodigoPostal}");

        if (!string.IsNullOrEmpty(home.Vecindario))
            content.Add($"Vecindario: {home.Vecindario}");

        // Property characteristics
        if (home.AreaTotal > 0)
            content.Add($"Área total: {home.AreaTotal} pies cuadrados");

        if (home.Habitaciones > 0)
            content.Add($"Habitaciones: {home.Habitaciones}");

        if (home.Banos > 0)
            content.Add($"Baños: {home.Banos}");

        if (home.MedioBanos > 0)
            content.Add($"Medio baños: {home.MedioBanos}");

        if (home.AnoConstruction > 0)
            content.Add($"Año de construcción: {home.AnoConstruction}");

        // Financial information
        if (home.ValorEstimado.HasValue)
            content.Add($"Valor estimado: ${home.ValorEstimado:N0}");

        // Systems and features
        if (!string.IsNullOrEmpty(home.Calefaccion))
            content.Add($"Calefacción: {home.Calefaccion}");

        if (!string.IsNullOrEmpty(home.AireAcondicionado))
            content.Add($"Aire acondicionado: {home.AireAcondicionado}");

        // Additional features
        if (home.CaracteristicasTerreno.Any())
            content.Add($"Características del terreno: {string.Join(", ", home.CaracteristicasTerreno)}");

        if (home.AspectosPositivos.Any())
            content.Add($"Aspectos positivos: {string.Join(", ", home.AspectosPositivos)}");

        if (home.AspectosNegativos.Any())
            content.Add($"Aspectos negativos: {string.Join(", ", home.AspectosNegativos)}");

        // Description
        if (!string.IsNullOrEmpty(home.Descripcion))
            content.Add($"Descripción: {home.Descripcion}");

        // Status information
        content.Add($"Es principal: {(home.EsPrincipal ? "Sí" : "No")}");
        
        if (!string.IsNullOrEmpty(home.FechaInicio))
            content.Add($"Fecha de inicio: {home.FechaInicio}");

        if (!string.IsNullOrEmpty(home.FechaFin))
            content.Add($"Fecha de fin: {home.FechaFin}");

        return string.Join(". ", content);
    }

    /// <summary>
    /// Build complete content for vector search by combining all relevant analysis fields
    /// </summary>
    private static string BuildCompleteAnalysisContent(HomeComprehensiveAnalysisResult result)
    {
        var content = new List<string>();

        // Core analysis content
        if (!string.IsNullOrEmpty(result.ExecutiveSummary))
            content.Add($"Resumen ejecutivo: {result.ExecutiveSummary}");

        if (!string.IsNullOrEmpty(result.DetailedHtmlReport))
            content.Add($"Reporte detallado HTML: {StripHtmlTags(result.DetailedHtmlReport)}");

        if (!string.IsNullOrEmpty(result.DetailedTextReport))
            content.Add($"Reporte detallado: {result.DetailedTextReport}");

        // Property information
        content.Add($"Análisis exitoso: {(result.Success ? "Sí" : "No")}");
        content.Add($"Tiempo de procesamiento: {result.ProcessingTimeMs:F2} ms");
        content.Add($"Fecha de análisis: {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
        content.Add($"Home ID: {result.HomeId}");

        // Address and status
        if (!string.IsNullOrEmpty(result.Address))
            content.Add($"Dirección: {result.Address}");

        if (!string.IsNullOrEmpty(result.Status))
            content.Add($"Estado: {result.Status}");

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
    /// Find existing document ID by HomeId to avoid duplicates
    /// </summary>
    private async Task<string?> FindExistingDocumentIdAsync(SearchClient searchClient, string homeId)
    {
        try
        {
            var searchOptions = new SearchOptions
            {
                Filter = $"HomeId eq '{homeId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                var existingId = result.Document.GetString("id");
                _logger.LogDebug("?? Found existing document with ID: {DocumentId} for HomeId: {HomeId}", 
                    existingId, homeId);
                return existingId;
            }

            _logger.LogDebug("?? No existing document found for HomeId: {HomeId}", homeId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error searching for existing document for HomeId: {HomeId}", homeId);
            return null;
        }
    }

    /// <summary>
    /// Find existing document ID by HomeId to avoid duplicates in analysis index
    /// </summary>
    private async Task<string?> FindExistingDocumentIdByHomeIdAsync(SearchClient searchClient, string homeId)
    {
        try
        {
            var searchOptions = new SearchOptions
            {
                Filter = $"HomeId eq '{homeId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                var existingId = result.Document.GetString("id");
                _logger.LogDebug("?? Found existing analysis document with ID: {DocumentId} for HomeId: {HomeId}", 
                    existingId, homeId);
                return existingId;
            }

            _logger.LogDebug("?? No existing analysis document found for HomeId: {HomeId}", homeId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error searching for existing analysis document for HomeId: {HomeId}", homeId);
            return null;
        }
    }

    /// <summary>
    /// Generate a unique document ID for new home analysis entries
    /// Uses HomeId as base to ensure uniqueness while allowing updates
    /// </summary>
    private async Task<string> GenerateUniqueHomeDocumentId(HomeComprehensiveAnalysisResult result)
    {
        // For new documents, use a simple format: home_analysis_id format
        var baseId = $"home_analysis_{result.HomeId}";
        
        // Check if this exact ID exists (shouldn't happen with proper HomeId uniqueness)
        if (await HomeDocumentExistsAsync(baseId))
        {
            // Fallback: add timestamp for absolute uniqueness
            return $"{baseId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        }
        
        return baseId;
    }

    /// <summary>
    /// Check if a home analysis document with the given ID already exists
    /// </summary>
    private async Task<bool> HomeDocumentExistsAsync(string documentId)
    {
        try
        {
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));
            
            var searchOptions = new SearchOptions
            {
                Filter = $"id eq '{documentId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                return true; // Document exists
            }

            return false; // Document doesn't exist
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error checking home document existence for ID: {DocumentId}", documentId);
            return false; // Assume it doesn't exist on error
        }
    }

    /// <summary>
    /// Delete a home from the search index
    /// </summary>
    public async Task<HomesIndexResult> DeleteHomeAsync(string homeId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("??? Deleting home from index: {Id}", homeId);

            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));

            var document = new SearchDocument { ["id"] = homeId };
            var deleteResult = await searchClient.DeleteDocumentsAsync(new[] { document });

            var errors = deleteResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("? Home deleted from index: {Id}", homeId);
                return new HomesIndexResult
                {
                    Success = true,
                    Message = $"Home '{homeId}' deleted from index",
                    DocumentId = homeId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("? Error deleting home {Id}: {Error}", homeId, error.ErrorMessage);
                return new HomesIndexResult
                {
                    Success = false,
                    Error = $"Error deleting home: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting home from index: {Id}", homeId);
            return new HomesIndexResult
            {
                Success = false,
                Error = $"Error deleting home: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Search homes using vector, semantic, or full-text search
    /// </summary>
    public async Task<HomesSearchResult> SearchHomesAsync(HomesSearchQuery query)
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesSearchResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Searching homes: '{Query}'",
                query.SearchText?.Substring(0, Math.Min(query.SearchText.Length, 50)));

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));

            // Build search options
            var searchOptions = new SearchOptions
            {
                Size = query.Top,
                Skip = (query.Page - 1) * query.Top,
                IncludeTotalCount = true
            };

            // Add field selection using exact field names from index
            var fieldsToSelect = new[]
            {
                "id", "HomeId", "ExecutiveSummary", "Success", "ProcessingTimeMs",
                "AnalyzedAt", "DetailedHtmlReport", "TwinId", "Address", "Status"
            };
            foreach (var field in fieldsToSelect)
            {
                searchOptions.Select.Add(field);
            }

            // Add filters
            var filters = new List<string>();

            // Twin filter
            if (!string.IsNullOrEmpty(query.TwinId))
            {
                filters.Add($"TwinId eq '{query.TwinId}'");
            }

            // Property status filter
            if (!string.IsNullOrEmpty(query.Status))
            {
                filters.Add($"Status eq '{query.Status}'");
            }

            // Date range filter using AnalyzedAt field (when home was indexed)
            if (query.DateFrom.HasValue)
            {
                filters.Add($"AnalyzedAt ge {query.DateFrom.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
            if (query.DateTo.HasValue)
            {
                filters.Add($"AnalyzedAt le {query.DateTo.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }

            // Success filter (only successfully processed homes)
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
            var results = new List<HomesSearchResultItem>();
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                var item = new HomesSearchResultItem
                {
                    Id = result.Document.GetString("id") ?? string.Empty,
                    HomeId = result.Document.GetString("HomeId") ?? string.Empty, // Changed from DiaryEntryId
                    TwinId = result.Document.GetString("TwinId") ?? string.Empty,
                    ExecutiveSummary = result.Document.GetString("ExecutiveSummary") ?? string.Empty,
                    Success = result.Document.GetBoolean("Success") ?? false,
                    ProcessingTimeMs = result.Document.GetDouble("ProcessingTimeMs") ?? 0.0,
                    AnalyzedAt = result.Document.GetDateTimeOffset("AnalyzedAt") ?? DateTimeOffset.MinValue,
                    DetailedHtmlReport = result.Document.GetString("DetailedHtmlReport") ?? string.Empty,
                    Address = result.Document.GetString("Address") ?? string.Empty, // NEW
                    Status = result.Document.GetString("Status") ?? string.Empty, // NEW
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

            _logger.LogInformation("? Homes search completed: {Count} results found", results.Count);

            return new HomesSearchResult
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
            _logger.LogError(ex, "? Error searching homes");
            return new HomesSearchResult
            {
                Success = false,
                Error = $"Error searching homes: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Build comprehensive address from HomeData components
    /// </summary>
    private static string BuildCompleteAddress(HomeData home)
    {
        var addressParts = new List<string>();

        if (!string.IsNullOrEmpty(home.Direccion))
            addressParts.Add(home.Direccion);

        if (!string.IsNullOrEmpty(home.Ciudad))
            addressParts.Add(home.Ciudad);

        if (!string.IsNullOrEmpty(home.Estado))
            addressParts.Add(home.Estado);

        if (!string.IsNullOrEmpty(home.CodigoPostal))
            addressParts.Add(home.CodigoPostal);

        return string.Join(", ", addressParts);
    }

    /// <summary>
    /// Determine property status based on HomeData
    /// </summary>
    private static string DeterminePropertyStatus(HomeData home)
    {
        // Logic to determine status based on property data
        if (home.EsPrincipal)
            return "principal";

        if (!string.IsNullOrEmpty(home.FechaFin))
            return "pasado";

        return home.Tipo switch
        {
            "actual" => "activo",
            "pasado" => "historico",
            "inversion" => "inversion",
            "vacacional" => "vacacional",
            _ => "activo"
        };
    }

    /// <summary>
    /// Build home summary (equivalent to ExecutiveSummary)
    /// </summary>
    private static string BuildHomeSummary(HomeData home)
    {
        var summary = new List<string>();

        summary.Add($"Propiedad {home.TipoPropiedad} en {home.Ciudad}, {home.Estado}");
        
        if (home.AreaTotal > 0)
            summary.Add($"con {home.AreaTotal} pies cuadrados");
            
        if (home.Habitaciones > 0)
            summary.Add($"{home.Habitaciones} habitaciones");
            
        if (home.Banos > 0)
            summary.Add($"{home.Banos} baños");

        if (home.ValorEstimado.HasValue)
            summary.Add($"valorada en ${home.ValorEstimado:N0}");

        if (home.EsPrincipal)
            summary.Add("Es la vivienda principal del Twin");

        return string.Join(", ", summary) + ".";
    }

    /// <summary>
    /// Build home HTML report (equivalent to DetailedHtmlReport)
    /// </summary>
    private static string BuildHomeHtmlReport(HomeData home)
    {
        return $@"
        <div style=""background: linear-gradient(135deg, #2ecc71 0%, #27ae60 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;"">
            <h3 style=""color: #fff; margin: 0 0 15px 0;"">?? {home.TipoPropiedad} en {home.Ciudad}</h3>
            
            <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">?? Información de la Propiedad</h4>
                <p style=""margin: 5px 0; line-height: 1.6;""><strong>Dirección:</strong> {home.Direccion}</p>
                <p style=""margin: 5px 0; line-height: 1.6;""><strong>Ciudad:</strong> {home.Ciudad}, {home.Estado} {home.CodigoPostal}</p>
                <p style=""margin: 5px 0; line-height: 1.6;""><strong>Tipo:</strong> {home.TipoPropiedad} ({home.Tipo})</p>
                <p style=""margin: 5px 0; line-height: 1.6;""><strong>Área:</strong> {home.AreaTotal} pies²</p>
                <p style=""margin: 5px 0; line-height: 1.6;""><strong>Habitaciones:</strong> {home.Habitaciones} | <strong>Baños:</strong> {home.Banos}</p>
            </div>

            <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">? Estado</h4>
                <p style=""margin: 0; line-height: 1.6;"">
                    {(home.EsPrincipal ? "Vivienda principal del Twin" : "Propiedad secundaria")}
                    {(home.ValorEstimado.HasValue ? $" - Valorada en ${home.ValorEstimado:N0}" : "")}
                </p>
            </div>
            
            <div style=""margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;"">
                ?? ID: {home.Id} • ?? Twin: {home.TwinID}
            </div>
        </div>";
    }

    /// <summary>
    /// Build metadata keys for the home
    /// </summary>
    private static string BuildMetadataKeys(HomeData home)
    {
        var keys = new List<string>();
        
        if (home.ValorEstimado.HasValue) keys.Add("valorEstimado");
        if (home.EsPrincipal) keys.Add("esPrincipal");
        if (home.AnoConstruction > 0) keys.Add("anoConstruction");
        if (!string.IsNullOrEmpty(home.Vecindario)) keys.Add("vecindario");
        if (home.WalkScore.HasValue) keys.Add("walkScore");
        if (home.BikeScore.HasValue) keys.Add("bikeScore");
        
        return string.Join(", ", keys);
    }

    /// <summary>
    /// Build metadata values for the home  
    /// </summary>
    private static string BuildMetadataValues(HomeData home)
    {
        var values = new List<string>();
        
        if (home.ValorEstimado.HasValue) values.Add($"${home.ValorEstimado:N0}");
        if (home.EsPrincipal) values.Add("principal");
        if (home.AnoConstruction > 0) values.Add(home.AnoConstruction.ToString());
        if (!string.IsNullOrEmpty(home.Vecindario)) values.Add(home.Vecindario);
        if (home.WalkScore.HasValue) values.Add($"walk:{home.WalkScore}");
        if (home.BikeScore.HasValue) values.Add($"bike:{home.BikeScore}");
        
        return string.Join(" ", values);
    }

    /// <summary>
    /// Get existing document fields to preserve during updates
    /// </summary>
    private async Task<ExistingDocumentFields> GetExistingDocumentFieldsAsync(SearchClient searchClient, string documentId)
    {
        try
        {
            var searchOptions = new SearchOptions
            {
                Filter = $"id eq '{documentId}'",
                Size = 1,
                Select = { "HomeInsurance", "HomeTitle", "Inspections", "Invoices" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                return new ExistingDocumentFields
                {
                    HomeInsurance = result.Document.GetString("HomeInsurance") ?? "",
                    HomeTitle = result.Document.GetString("HomeTitle") ?? "",
                    Inspections = result.Document.GetString("Inspections") ?? "",
                    Invoices = result.Document.GetString("Invoices") ?? ""
                };
            }

            // Return empty fields if document not found (shouldn't happen in normal flow)
            _logger.LogWarning("⚠️ Document not found when trying to preserve fields: {DocumentId}", documentId);
            return new ExistingDocumentFields();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error getting existing document fields for ID: {DocumentId}, will use empty fields", documentId);
            return new ExistingDocumentFields();
        }
    }

    /// <summary>
    /// Actualiza solo el campo HomeInsurance en el documento existente del índice de búsqueda
    /// </summary>
    /// <param name="homeInsuranceAnalysis">Análisis HTML del seguro de casa</param>
    /// <param name="homeId">ID de la casa</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Resultado de la operación de actualización</returns>
    public async Task<HomesIndexResult> HomeIndexInsuranceUpdate(string homeInsuranceAnalysis, string homeId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new HomesIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("🏠📊 Updating HomeInsurance field for HomeId: {HomeId}, TwinId: {TwinId}", homeId, twinId);

            // Crear cliente de búsqueda
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), HomesIndexName, new AzureKeyCredential(_searchApiKey!));

            // PASO 1: Buscar el documento existente por HomeId y TwinId
            _logger.LogInformation("🔍 Searching for existing document with HomeId: {HomeId}", homeId);
            
            var searchOptions = new SearchOptions
            {
                Filter = $"HomeId eq '{homeId}' and TwinId eq '{twinId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            string? documentId = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                documentId = result.Document.GetString("id");
                _logger.LogInformation("✅ Found existing document: {DocumentId}", documentId);
                break;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                return new HomesIndexResult
                {
                    Success = false,
                    Error = $"No existing document found for HomeId: {homeId} and TwinId: {twinId}"
                };
            }

            // PASO 2: Actualizar solo el campo HomeInsurance manteniendo los demás campos
            _logger.LogInformation("📝 Updating HomeInsurance field in document: {DocumentId}", documentId);

            var updateDocument = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["HomeInsurance"] = homeInsuranceAnalysis
            };

            // Usar MergeDocumentsAsync para actualizar solo el campo especificado
            var documents = new[] { new SearchDocument(updateDocument) };
            var uploadResult = await searchClient.MergeDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("✅ HomeInsurance field updated successfully: DocumentId={DocumentId}, HomeId={HomeId}", 
                    documentId, homeId);
                    
                return new HomesIndexResult
                {
                    Success = true,
                    Message = $"HomeInsurance field updated successfully for HomeId '{homeId}'",
                    IndexName = HomesIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("❌ Error updating HomeInsurance field for HomeId {HomeId}: {Error}", 
                    homeId, error.ErrorMessage);
                return new HomesIndexResult
                {
                    Success = false,
                    Error = $"Error updating HomeInsurance field: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating HomeInsurance field for HomeId: {HomeId}", homeId);
            return new HomesIndexResult
            {
                Success = false,
                Error = $"Error updating HomeInsurance field: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Result class for homes index operations
/// </summary>
public class HomesIndexResult
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
/// Query parameters for homes search
/// </summary>
public class HomesSearchQuery
{
    public string? SearchText { get; set; }
    public string? TwinId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool SuccessfulOnly { get; set; } = false;
    public string? Status { get; set; } // NEW: Property status filter
    public bool UseVectorSearch { get; set; } = true;
    public bool UseSemanticSearch { get; set; } = false;
    public bool UseHybridSearch { get; set; } = false;
    public int Top { get; set; } = 10;
    public int Page { get; set; } = 1;
}

/// <summary>
/// Result class for homes search operations
/// </summary>
public class HomesSearchResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<HomesSearchResultItem> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
}

/// <summary>
/// Individual home search result item (equivalent to DiarySearchResultItem)
/// </summary>
public class HomesSearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string HomeId { get; set; } = string.Empty; // Equivalent to DiaryEntryId
    public string TwinId { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double ProcessingTimeMs { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DetailedHtmlReport { get; set; }
    public string Address { get; set; } = string.Empty; // NEW: Comprehensive address
    public string Status { get; set; } = string.Empty; // NEW: Property status

    public string HomeInsurance { get; set; } = string.Empty; // NEW: Property status


    public string HomeTitle { get; set; } = string.Empty; // NEW: Property status


    public List<string> Inspections { get; set; } = new List<string>(); // NEW: Property status
    public double SearchScore { get; set; }
    public List<string> Highlights { get; set; } = new();
}

/// <summary>
/// Result class for getting a specific home by ID (equivalent to DiaryAnalysisResponse)
/// </summary>
public class HomesGetResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string HomeId { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public HomesSearchResultItem? Home { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of comprehensive home analysis (equivalent to DiaryComprehensiveAnalysisResult)
/// </summary>
public class HomeComprehensiveAnalysisResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string HomeId { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public string DetailedHtmlReport { get; set; } = string.Empty;
    public string DetailedTextReport { get; set; } = string.Empty; // detalleTexto from JSON
    public double ProcessingTimeMs { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    // NEW: Home-specific fields
    public string? Address { get; set; }
    public string? Status { get; set; }
    
    /// <summary>
    /// Parse the comprehensive analysis response from AI JSON
    /// </summary>
    public static HomeComprehensiveAnalysisResult ParseFromAIResponse(string aiJsonResponse, string homeId)
    {
        try
        {
            // Clean the response
            var cleanResponse = aiJsonResponse.Trim();
            if (cleanResponse.StartsWith("```json"))
            {
                cleanResponse = cleanResponse.Substring(7);
            }
            if (cleanResponse.EndsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
            }
            cleanResponse = cleanResponse.Trim();

            // Parse JSON
            var analysisData = JsonSerializer.Deserialize<Dictionary<string, object>>(cleanResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var result = new HomeComprehensiveAnalysisResult
            {
                Success = true,
                HomeId = homeId,
                ExecutiveSummary = GetStringProperty(analysisData, "executiveSummary", "Análisis no disponible"),
                DetailedHtmlReport = GetStringProperty(analysisData, "detailedHtmlReport", "<div>Reporte no disponible</div>"),
                DetailedTextReport = GetStringProperty(analysisData, "detalleTexto", "Reporte detallado no disponible"),
                ProcessingTimeMs = 0.0 // Will be set by caller
            };

            // Parse metadata if available
            if (analysisData?.TryGetValue("metadata", out var metadataObj) == true && metadataObj is JsonElement metadataElement)
            {
                result.Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataElement.GetRawText()) ?? new();
                
                // Extract Address and Status from metadata if available
                if (result.Metadata.TryGetValue("address", out var addressObj))
                    result.Address = addressObj?.ToString();
                    
                if (result.Metadata.TryGetValue("status", out var statusObj))
                    result.Status = statusObj?.ToString();
            }

            return result;
        }
        catch (Exception ex)
        {
            return new HomeComprehensiveAnalysisResult
            {
                Success = false,
                ErrorMessage = $"Error parsing AI response: {ex.Message}",
                HomeId = homeId,
                ExecutiveSummary = "Error al procesar análisis",
                DetailedHtmlReport = "<div style='color: red;'>Error procesando análisis de la propiedad</div>",
                DetailedTextReport = "Error procesando análisis",
                ProcessingTimeMs = 0.0,
                Metadata = new Dictionary<string, object> { ["error"] = "parsing_failed" }
            };
        }
    }

    /// <summary>
    /// Helper method to safely get string properties from parsed data
    /// </summary>
    private static string GetStringProperty(Dictionary<string, object>? data, string key, string defaultValue = "")
    {
        if (data?.TryGetValue(key, out var value) != true || value == null)
            return defaultValue;

        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            return jsonElement.GetString() ?? defaultValue;

        return value.ToString() ?? defaultValue;
    }
}

/// <summary>
/// Helper class to store existing document fields values
/// </summary>
public class ExistingDocumentFields
{
    public string HomeInsurance { get; set; } = string.Empty;
    public string HomeTitle { get; set; } = string.Empty;
    public string Inspections { get; set; } = string.Empty;
    public string Invoices { get; set; } = string.Empty;
}