using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using TwinAgentsLibrary.Models;
using static TwinAgentsLibrary.Models.SemistructuredDocument;

namespace TwinFxTests.Services
{
    public class Semistructured_Index
    {
        private readonly ILogger<Semistructured_Index> _logger;
        private readonly IConfiguration _configuration;
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Index configuration constants
        private const string IndexName = "semistructured_documents_index";
        private const int EmbeddingDimensions = 1536;
        private const string VectorSearchProfile = "myHnswProfile";
        private const string HnswAlgorithmConfig = "myHnsw";
        private const string SemanticConfig = "my-semantic-config";

        // Configuration keys
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;

        public bool IsAvailable => _indexClient != null;

        public Semistructured_Index(ILogger<Semistructured_Index> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Load Azure Search configuration with fallback to Values section (Azure Functions format)
            _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
            _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

            // Load Azure OpenAI configuration
            _openAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT") ?? GetConfigurationValue("AzureOpenAI:Endpoint");
            _openAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY") ?? GetConfigurationValue("AzureOpenAI:ApiKey");
            _embeddingDeployment = "text-embedding-3-large";

            // Initialize Azure Search client
            if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
            {
                try
                {
                    var credential = new AzureKeyCredential(_searchApiKey);
                    _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                    _logger.LogInformation("✅ Semistructured Index service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure Search client for Semistructured Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure Search credentials not found - Semistructured Index service unavailable");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("🤖 Azure OpenAI embedding client initialized for Semistructured Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for Semistructured Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure OpenAI credentials not found for Semistructured Index");
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
        /// Create the semistructured_documents_index with vector and semantic search capabilities
        /// </summary>
        public async Task<SemistructuredIndexResult> CreateSemistructuredDocumentsIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new SemistructuredIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Creating Semistructured Documents Search Index: {IndexName}", IndexName);

                // Define search fields based on the SemistructuredDocument class schema
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // TwinId field
                    new SearchableField("twinId")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // Document type field
                    new SearchableField("documentType")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // File name field
                    new SearchableField("fileName")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Processed timestamp field
                    new SimpleField("processedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },

                    // Plain text report field
                    new SearchableField("reporteTextoPlano")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Vector field for semantic similarity search (1536 dimensions)
                    new SearchField("reporteTextoPlanoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions,
                        VectorSearchProfileName = VectorSearchProfile
                    },

                    // Optional: File path in storage
                    new SearchableField("filePath")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Optional: Container name in storage
                    new SearchableField("containerName")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Optional: File size in bytes
                    new SimpleField("fileSize", SearchFieldDataType.Int64)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },

                    // Optional: MIME type of the file
                    new SearchableField("mimeType")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Optional: Processing status
                    new SearchableField("processingStatus")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Optional: Additional metadata as searchable text
                    new SearchableField("metadata")
                    {
                        IsFilterable = false,
                        IsFacetable = false
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
                    TitleField = new SemanticField("fileName")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("reporteTextoPlano"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("documentType"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("twinId"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

                // Create the semistructured documents search index
                var index = new SearchIndex(IndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ Semistructured Documents Index '{IndexName}' created successfully", IndexName);

                return new SemistructuredIndexResult
                {
                    Success = true,
                    Message = $"Semistructured Documents Index '{IndexName}' created successfully",
                    IndexName = IndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating Semistructured Documents Index");
                return new SemistructuredIndexResult
                {
                    Success = false,
                    Error = $"Error creating Semistructured Documents Index: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Upload a semistructured document to the Azure AI Search index with vector embeddings
        /// This method will upsert (update or insert) the document into the search index
        /// </summary>
        /// <param name="document">The semistructured document to upload</param>
        /// <returns>Result indicating success or failure of the upload operation</returns>
        public async Task<SemistructuredUploadResult> UploadDocumentToIndexAsync(SemistructuredDocument document)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new SemistructuredUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        DocumentId = document.Id
                    };
                }

                _logger.LogInformation("📄 Uploading semistructured document to index: {DocumentId} - {FileName}", 
                    document.Id, document.FileName);

                // Validate document
                if (!document.IsValid())
                {
                    return new SemistructuredUploadResult
                    {
                        Success = false,
                        Error = "Document validation failed - missing required fields",
                        DocumentId = document.Id
                    };
                }

                // Create search client for the semistructured documents index
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate embeddings for the reporteTextoPlano field using Azure OpenAI
                float[]? embeddings = null;
                if (!string.IsNullOrEmpty(document.ReporteTextoPlano))
                {
                    embeddings = await GenerateEmbeddingsAsync(document.ReporteTextoPlano);
                    if (embeddings != null)
                    {
                        document.ReporteTextoPlanoVector = embeddings;
                        _logger.LogInformation("✅ Generated {Dimensions} dimensional vector embeddings for document {DocumentId}", 
                            embeddings.Length, document.Id);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to generate embeddings for document {DocumentId}, continuing without vector search capability", 
                            document.Id);
                    }
                }

                // Convert document to search document format
                var searchDocument = document.ToSearchDocument();

                // Upload document to search index using MergeOrUpload (upsert operation)
                var documents = new[] { new SearchDocument(searchDocument) };
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

                // Check for errors
                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (!errors.Any())
                {
                    _logger.LogInformation("✅ Semistructured document uploaded successfully: DocumentId={DocumentId}, TwinId={TwinId}, FileName={FileName}", 
                        document.Id, document.TwinId, document.FileName);
                        
                    return new SemistructuredUploadResult
                    {
                        Success = true,
                        Message = $"Document '{document.FileName}' uploaded successfully to semistructured index",
                        IndexName = IndexName,
                        DocumentId = document.Id,
                        HasVectorEmbeddings = embeddings != null,
                        VectorDimensions = embeddings?.Length ?? 0
                    };
                }
                else
                {
                    var error = errors.First();
                    _logger.LogError("❌ Error uploading semistructured document {DocumentId}: {Error}", 
                        document.Id, error.ErrorMessage);
                    return new SemistructuredUploadResult
                    {
                        Success = false,
                        Error = $"Error uploading document: {error.ErrorMessage}",
                        DocumentId = document.Id
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error uploading semistructured document: {DocumentId}", document.Id);
                return new SemistructuredUploadResult
                {
                    Success = false,
                    Error = $"Error uploading document: {ex.Message}",
                    DocumentId = document.Id
                };
            }
        }

        /// <summary>
        /// Upload multiple semistructured documents to the Azure AI Search index in batch
        /// </summary>
        /// <param name="documents">List of semistructured documents to upload</param>
        /// <returns>Result with batch upload statistics</returns>
        public async Task<SemistructuredBatchUploadResult> UploadDocumentBatchToIndexAsync(IEnumerable<SemistructuredDocument> documents)
        {
            var documentList = documents.ToList();
            var results = new List<SemistructuredUploadResult>();
            
            try
            {
                if (!IsAvailable)
                {
                    return new SemistructuredBatchUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        TotalDocuments = documentList.Count,
                        SuccessfulUploads = 0,
                        FailedUploads = documentList.Count,
                        Results = results
                    };
                }

                _logger.LogInformation("📄 Starting batch upload of {Count} semistructured documents", documentList.Count);

                // Process documents in parallel (but limit concurrency to avoid overwhelming the service)
                var semaphore = new SemaphoreSlim(5, 5); // Limit to 5 concurrent uploads
                var uploadTasks = documentList.Select(async document =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        return await UploadDocumentToIndexAsync(document);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                results = (await Task.WhenAll(uploadTasks)).ToList();

                var successfulUploads = results.Count(r => r.Success);
                var failedUploads = results.Count(r => !r.Success);

                _logger.LogInformation("📊 Batch upload completed: {Successful} successful, {Failed} failed out of {Total} documents", 
                    successfulUploads, failedUploads, documentList.Count);

                return new SemistructuredBatchUploadResult
                {
                    Success = failedUploads == 0,
                    Message = $"Batch upload completed: {successfulUploads} successful, {failedUploads} failed",
                    TotalDocuments = documentList.Count,
                    SuccessfulUploads = successfulUploads,
                    FailedUploads = failedUploads,
                    Results = results,
                    Error = failedUploads > 0 ? $"{failedUploads} documents failed to upload" : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during batch upload of semistructured documents");
                return new SemistructuredBatchUploadResult
                {
                    Success = false,
                    Error = $"Batch upload error: {ex.Message}",
                    TotalDocuments = documentList.Count,
                    SuccessfulUploads = 0,
                    FailedUploads = documentList.Count,
                    Results = results
                };
            }
        }

        /// <summary>
        /// Generate embeddings for text content using Azure OpenAI
        /// </summary>
        /// <param name="text">Text to generate embeddings for</param>
        /// <returns>Float array of embeddings or null if generation fails</returns>
        private async Task<float[]?> GenerateEmbeddingsAsync(string text)
        {
            try
            {
                if (_embeddingClient == null)
                {
                    _logger.LogWarning("⚠️ Embedding client not available, skipping vector generation");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("⚠️ Text content is empty, skipping vector generation");
                    return null;
                }

                // Truncate text if too long (Azure OpenAI has token limits)
                if (text.Length > 8000)
                {
                    text = text.Substring(0, 8000);
                    _logger.LogInformation("📏 Text truncated to 8000 characters for embedding generation");
                }

                _logger.LogDebug("🤖 Generating embeddings for text: {Length} characters", text.Length);

                var embeddingOptions = new EmbeddingGenerationOptions
                {
                    Dimensions = EmbeddingDimensions
                };

                var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
                var embeddings = embedding.Value.ToFloats().ToArray();

                _logger.LogInformation("✅ Generated embedding vector with {Dimensions} dimensions", embeddings.Length);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to generate embeddings, continuing without vector search");
                return null;
            }
        }

        /// <summary>
        /// Perform hybrid search on semistructured documents using vector, semantic, and full-text search
        /// </summary>
        /// <param name="query">Search query text</param>
        /// <param name="searchOptions">Additional search options for filtering and configuration</param>
        /// <returns>Collection of search documents with scores and metadata</returns>
        public async Task<IEnumerable<SemistructuredSearchDocument>> SearchDocumentAsync(string? query = null, SemistructuredSearchOptions? searchOptions = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogWarning("⚠️ Azure Search service not available");
                    return new List<SemistructuredSearchDocument>();
                }

                searchOptions ??= new SemistructuredSearchOptions();

                _logger.LogInformation("🔍 Performing hybrid search on semistructured documents: Query='{Query}', TwinId='{TwinId}'", 
                    query?.Substring(0, Math.Min(query.Length, 50)), searchOptions.TwinId);

                // Create search client
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Configure search options
                var options = new SearchOptions
                {
                    QueryType = SearchQueryType.Semantic,
                    Size = searchOptions.Size,
                    Skip = Math.Max(0, (searchOptions.Page - 1) * searchOptions.Size),
                    IncludeTotalCount = true,
                    SemanticSearch = new SemanticSearchOptions
                    {
                        SemanticConfigurationName = SemanticConfig,
                        QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                        QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
                    }
                };

                // Add field selection
                var fieldsToSelect = new[]
                {
                    "id", "twinId", "documentType", "fileName", "processedAt", 
                    "reporteTextoPlano", "filePath", "containerName", "fileSize", 
                    "mimeType", "processingStatus", "metadata"
                };
                foreach (var field in fieldsToSelect)
                {
                    options.Select.Add(field);
                }

                // Add filters
                var filters = new List<string>();
                if (!string.IsNullOrEmpty(searchOptions.TwinId))
                {
                    filters.Add($"twinId eq '{searchOptions.TwinId.Replace("'", "''")}'");
                }
                

                if (filters.Any())
                {
                    options.Filter = string.Join(" and ", filters);
                }

                // Configure vector search if can be generated
                float[]? searchVector = null;
                if (!string.IsNullOrEmpty(query))
                {
                    searchVector = await GenerateEmbeddingsAsync(query);
                }

                // Add vector search configuration
                if (searchVector != null && searchVector.Length > 0)
                {
                    var vectorQuery = new VectorizedQuery(searchVector)
                    {
                        KNearestNeighborsCount = Math.Max(searchOptions.Size, 50), // Get more candidates for re-ranking
                        Fields = { "reporteTextoPlanoVector" } // Use the vector field from our index
                    };

                    options.VectorSearch = new VectorSearchOptions();
                    options.VectorSearch.Queries.Add(vectorQuery);

                    _logger.LogInformation("✅ Vector search configured with {Dimensions} dimensional vector", searchVector.Length);
                }

                // Determine search text
                string searchText = string.IsNullOrEmpty(query) ? "*" : query;

                // Perform the search
                var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, options);

                // Process results
                var searchDocuments = new List<SemistructuredSearchDocument>();

                if (searchResponse.HasValue)
                {
                    await foreach (var result in searchResponse.Value.GetResultsAsync())
                    {
                        var document = new SemistructuredSearchDocument
                        {
                            Score = result.Score ?? 0.0,
                            Document = CreateSemistructuredDocumentFromSearchResult(result.Document),
                            Highlights = new List<string>(),
                            RerankerScore = result.SemanticSearch?.RerankerScore
                        };

                        // Extract semantic search highlights if available
                        if (result.SemanticSearch?.Captions != null)
                        {
                            document.Highlights = result.SemanticSearch.Captions
                                .Select(caption => ExtractCaptionText(caption))
                                .Where(text => !string.IsNullOrWhiteSpace(text))
                                .ToList();
                        }

                        searchDocuments.Add(document);
                    }

                    _logger.LogInformation("✅ Hybrid search completed: Found {Count} documents", searchDocuments.Count);
                }

                return searchDocuments;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error performing hybrid search on semistructured documents");
                return new List<SemistructuredSearchDocument>();
            }
        }

        /// <summary>
        /// Extract caption text from semantic search results
        /// </summary>
        /// <param name="caption">Caption object from semantic search</param>
        /// <returns>Extracted text content</returns>
        private static string ExtractCaptionText(object caption)
        {
            if (caption == null) return string.Empty;

            var captionType = caption.GetType();

            // Try to get Text property first
            var textProperty = captionType.GetProperty("Text");
            if (textProperty != null)
            {
                var textValue = textProperty.GetValue(caption) as string;
                if (!string.IsNullOrWhiteSpace(textValue)) return textValue;
            }

            // Try to get Highlights property as fallback
            var highlightsProperty = captionType.GetProperty("Highlights");
            if (highlightsProperty != null)
            {
                var highlightsValue = highlightsProperty.GetValue(caption);
                if (highlightsValue is IEnumerable<string> highlightSequence)
                {
                    return string.Join(" ", highlightSequence);
                }
                if (highlightsValue != null)
                {
                    return highlightsValue.ToString() ?? string.Empty;
                }
            }

            return caption.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Create a SemistructuredDocument from Azure Search result
        /// </summary>
        /// <param name="searchDocument">Search document from Azure Search</param>
        /// <returns>Populated SemistructuredDocument</returns>
        private static SemistructuredDocument CreateSemistructuredDocumentFromSearchResult(SearchDocument searchDocument)
        {
            var document = new SemistructuredDocument
            {
                Id = searchDocument.GetString("id") ?? string.Empty,
                TwinId = searchDocument.GetString("twinId") ?? string.Empty,
                DocumentType = searchDocument.GetString("documentType") ?? string.Empty,
                FileName = searchDocument.GetString("fileName") ?? string.Empty,
                ProcessedAt = searchDocument.GetDateTimeOffset("processedAt") ?? DateTimeOffset.MinValue,
                ReporteTextoPlano = searchDocument.GetString("reporteTextoPlano") ?? string.Empty,
                FilePath = searchDocument.GetString("filePath"),
                ContainerName = searchDocument.GetString("containerName"),
                MimeType = searchDocument.GetString("mimeType"),
                ProcessingStatus = searchDocument.GetString("processingStatus")
            };

            // Handle fileSize as nullable long
            if (searchDocument.TryGetValue("fileSize", out var fileSizeValue) && fileSizeValue != null)
            {
                if (long.TryParse(fileSizeValue.ToString(), out var fileSize))
                {
                    document.FileSize = fileSize;
                }
            }

            // Parse metadata back from text if available
            var metadataText = searchDocument.GetString("metadata");
            if (!string.IsNullOrEmpty(metadataText))
            {
                document.Metadata = new Dictionary<string, object>();
                var metadataPairs = metadataText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in metadataPairs)
                {
                    var colonIndex = pair.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < pair.Length - 1)
                    {
                        var key = pair.Substring(0, colonIndex);
                        var value = pair.Substring(colonIndex + 1);
                        document.Metadata[key] = value;
                    }
                }
            }

            return document;
        }
    }

    /// <summary>
    
        /// <summary>
        /// Get the full file path for storage operations
        /// </summary>
      
    }

    /// <summary>
    /// Result class for semistructured index operations
    /// </summary>
    public class SemistructuredIndexResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? IndexName { get; set; }
        public int FieldsCount { get; set; }
        public bool HasVectorSearch { get; set; }
        public bool HasSemanticSearch { get; set; }
    }

    /// <summary>
    /// Result class for semistructured document upload operations
    /// </summary>
    public class SemistructuredUploadResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? IndexName { get; set; }
        public string DocumentId { get; set; } = string.Empty;
        public bool HasVectorEmbeddings { get; set; }
        public int VectorDimensions { get; set; }
    }

    /// <summary>
    /// Result class for batch upload operations
    /// </summary>
    public class SemistructuredBatchUploadResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public int TotalDocuments { get; set; }
        public int SuccessfulUploads { get; set; }
        public int FailedUploads { get; set; }
        public List<SemistructuredUploadResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Search document result containing the document and search metadata
    /// </summary>
   

    /// <summary>
    /// Configuration options for semistructured document search
    /// </summary>
    