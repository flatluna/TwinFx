using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using System.Text;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Agents;

namespace TwinFx.Services;

/// <summary>
/// Azure AI Search Service specifically designed for Books indexing and search
/// ========================================================================
/// 
/// This service creates and manages a search index optimized for books and literature with:
/// - Vector search capabilities using Azure OpenAI embeddings
/// - Semantic search for natural language queries about books
/// - Full-text search across book descriptions and AI analysis
/// - Book metadata-based search and filtering
/// - AI-generated content search and filtering
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class BooksSearchIndex
{
    private readonly ILogger<BooksSearchIndex> _logger;
    private readonly IConfiguration _configuration;
    private readonly SearchIndexClient? _indexClient;
    private readonly AzureOpenAIClient? _azureOpenAIClient;
    private readonly EmbeddingClient? _embeddingClient;

    // Configuration constants
    private const string BooksIndexName = "books-literature-index";
    private const string VectorSearchProfile = "books-vector-profile";
    private const string HnswAlgorithmConfig = "books-hnsw-config";
    private const string VectorizerConfig = "books-vectorizer";
    private const string SemanticConfig = "books-semantic-config";
    private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions

    // Configuration keys
    private readonly string? _searchEndpoint;
    private readonly string? _searchApiKey;
    private readonly string? _openAIEndpoint;
    private readonly string? _openAIApiKey;
    private readonly string? _embeddingDeployment;

    public BooksSearchIndex(ILogger<BooksSearchIndex> logger, IConfiguration configuration)
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
                _logger.LogInformation("📚 Books Search Index client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error initializing Azure Search client for Books Index");
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Azure Search credentials not found for Books Index");
        }

        // Initialize Azure OpenAI client for embeddings
        if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
        {
            try
            {
                _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                _logger.LogInformation("📚 Azure OpenAI embedding client initialized for Books Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for Books Index");
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Azure OpenAI credentials not found for Books Index");
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
    /// Check if the books search service is available
    /// </summary>
    public bool IsAvailable => _indexClient != null;

    /// <summary>
    /// Create the books search index with vector and semantic search capabilities
    /// </summary>
    public async Task<BooksIndexResult> CreateBooksIndexAsync()
    {
        try
        {
            if (!IsAvailable)
            {
                return new BooksIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("📚 Creating Books Search Index: {IndexName}", BooksIndexName);

            // Define search fields based on the requested schema: TituloLibro, Author, ISBN, DescripcionAI, detailHTMLReport, contenidoVector, TwinID, BookID
            var fields = new List<SearchField>
            {
                // Primary identification field
                new SimpleField("id", SearchFieldDataType.String)
                {
                    IsKey = true,
                    IsFilterable = true,
                    IsSortable = true
                },
                
                // Success (for indexing status)
                new SimpleField("Success", SearchFieldDataType.Boolean)
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // BookID (equivalent to BookMainData.Id)
                new SearchableField("BookID")
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // TwinID field for filtering by specific twin
                new SearchableField("TwinID")
                {
                    IsFilterable = true,
                    IsFacetable = true,
                    IsSortable = true
                },
                
                // TituloLibro (book title)
                new SearchableField("TituloLibro")
                {
                    IsFilterable = true,
                    IsFacetable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // Author (book author)
                new SearchableField("Author")
                {
                    IsFilterable = true,
                    IsFacetable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // ISBN (book ISBN)
                new SearchableField("ISBN")
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // DescripcionAI (AI-generated book description)
                new SearchableField("DescripcionAI")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // detailHTMLReport (detailed HTML report of book analysis)
                new SearchableField("detailHTMLReport")
                {
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // ProcessingTimeMS (processing time in milliseconds)
                new SimpleField("ProcessingTimeMS", SearchFieldDataType.Double)
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
                
            
                
                // Genero (book genre)
                new SearchableField("Genero")
                {
                    IsFilterable = true,
                    IsFacetable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
              
                
                // Combined content field for vector search (contenidoCompleto)
                new SearchableField("contenidoCompleto")
                {
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // Vector field for semantic similarity search (contenidoVector as requested)
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
                TitleField = new SemanticField("TituloLibro")
            };

            // Content fields for semantic ranking
            prioritizedFields.ContentFields.Add(new SemanticField("DescripcionAI"));
            prioritizedFields.ContentFields.Add(new SemanticField("detailHTMLReport"));
            prioritizedFields.ContentFields.Add(new SemanticField("contenidoCompleto"));

            // Keywords fields for semantic ranking
            prioritizedFields.KeywordsFields.Add(new SemanticField("Author"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("ISBN")); 
            prioritizedFields.KeywordsFields.Add(new SemanticField("Genero"));

            semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

            // Create the books search index
            var index = new SearchIndex(BooksIndexName, fields)
            {
                VectorSearch = vectorSearch,
                SemanticSearch = semanticSearch
            };

            var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("✅ Books Index '{IndexName}' created successfully", BooksIndexName);

            return new BooksIndexResult
            {
                Success = true,
                Message = $"Books Index '{BooksIndexName}' created successfully",
                IndexName = BooksIndexName,
                FieldsCount = fields.Count,
                HasVectorSearch = true,
                HasSemanticSearch = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating Books Index");
            return new BooksIndexResult
            {
                Success = false,
                Error = $"Error creating Books Index: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Index a book comprehensive analysis result in Azure AI Search from BookMain data
    /// </summary>
    public async Task<BooksIndexResult> IndexBookAnalysisFromBookMainAsync(BookMain bookMain, string twinId, double processingTimeMs = 0.0)
    {
        try
        {
            if (!IsAvailable)
            {
                return new BooksIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("📚 Indexing book analysis for BookID: {BookId}, TwinID: {TwinId}", bookMain.id, twinId);

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), BooksIndexName, new AzureKeyCredential(_searchApiKey!));

            // Generate unique document ID
            var documentId = await GenerateUniqueBookDocumentId(bookMain.id, twinId);

            // Generate embeddings for vector search
            var combinedContent = BuildCompleteBookContent(bookMain);
            var embeddings = await GenerateEmbeddingsAsync(combinedContent);

            // Create search document
            var document = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["Success"] = true,
                ["BookID"] = bookMain.id,
                ["TwinID"] = twinId,
                ["TituloLibro"] = bookMain.titulo ?? "Sin título",
                ["Author"] = bookMain.autor ?? "Autor desconocido",
                ["ISBN"] = bookMain.isbn ?? "Sin ISBN",
                ["DescripcionAI"] = bookMain.datosIA?.DescripcionAI ?? bookMain.descripcion ?? "Sin descripción",
                ["detailHTMLReport"] = bookMain.datosIA?.detailHTMLReport ?? "<div>Reporte no disponible</div>",
                ["ProcessingTimeMS"] = processingTimeMs,
                ["AnalyzedAt"] = DateTimeOffset.UtcNow, 
                ["Genero"] = bookMain.genero ?? "Sin género", 
                ["contenidoCompleto"] = combinedContent
            };

            // Add vector embeddings if available
            if (embeddings != null)
            {
                document["contenidoVector"] = embeddings;
            }

            // Upload document to search index
            var uploadResult = await searchClient.UploadDocumentsAsync(new[] { document });
            
            var firstResult = uploadResult.Value.Results.FirstOrDefault();
            bool indexSuccess = firstResult != null && firstResult.Succeeded;

            if (indexSuccess)
            {
                _logger.LogInformation("✅ Book analysis indexed successfully: DocumentId={DocumentId}", documentId);
                return new BooksIndexResult
                {
                    Success = true,
                    Message = "Book analysis indexed successfully",
                    IndexName = BooksIndexName,
                    DocumentId = documentId,
                    HasVectorSearch = embeddings != null,
                    HasSemanticSearch = true
                };
            }
            else
            {
                var errorMessage = firstResult?.ErrorMessage ?? "Unknown indexing error";
                _logger.LogError("❌ Failed to index book analysis: {Error}", errorMessage);
                return new BooksIndexResult
                {
                    Success = false,
                    Error = $"Failed to index book analysis: {errorMessage}",
                    DocumentId = documentId
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error indexing book analysis");
            return new BooksIndexResult
            {
                Success = false,
                Error = $"Error indexing book analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get book by ID from search index
    /// </summary>
    public async Task<BooksIndexGetResult> GetBookByIdAsync(string bookId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new BooksIndexGetResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("📚 Getting book by ID: {BookId} for TwinId: {TwinId}", bookId, twinId);

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), BooksIndexName, new AzureKeyCredential(_searchApiKey!));

            // Build search options with filters
            var searchOptions = new SearchOptions
            {
                Filter = $"BookID eq '{bookId}' and TwinID eq '{twinId}'",
                Size = 1,
                IncludeTotalCount = true
            };

            // Add field selection for the response
            var fieldsToSelect = new[]
            {
                "id", "BookID", "TwinID", "Success", "TituloLibro", "Author", "ISBN",
                "DescripcionAI", "detailHTMLReport", "ProcessingTimeMS", "AnalyzedAt",
                "Editorial", "Genero", "AñoPublicacion", "contenidoCompleto"
            };
            foreach (var field in fieldsToSelect)
            {
                searchOptions.Select.Add(field);
            }

            // Perform the search using wildcard query to match any document with the filters
            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);

            // Process results
            BooksSearchResultItem? bookItem = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                bookItem = new BooksSearchResultItem
                {
                    Id = result.Document.GetString("id") ?? string.Empty,
                    BookID = result.Document.GetString("BookID") ?? string.Empty,
                    TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                    TituloLibro = result.Document.GetString("TituloLibro") ?? string.Empty,
                    Author = result.Document.GetString("Author") ?? string.Empty,
                    ISBN = result.Document.GetString("ISBN") ?? string.Empty,
                    DescripcionAI = result.Document.GetString("DescripcionAI") ?? string.Empty,
                    DetailHTMLReport = result.Document.GetString("detailHTMLReport") ?? string.Empty,
                    Success = result.Document.GetBoolean("Success") ?? false,
                    ProcessingTimeMS = result.Document.GetDouble("ProcessingTimeMS") ?? 0.0,
                    AnalyzedAt = result.Document.GetDateTimeOffset("AnalyzedAt") ?? DateTimeOffset.MinValue,
                    Editorial = result.Document.GetString("Editorial") ?? string.Empty,
                    Genero = result.Document.GetString("Genero") ?? string.Empty,
                    AñoPublicacion = result.Document.GetInt32("AñoPublicacion") ?? 0,
                    SearchScore = result.Score ?? 0.0
                };
                
                // We only expect one result due to the specific filter
                break;
            }

            if (bookItem != null)
            {
                _logger.LogInformation("✅ Book found for BookID: {BookId}", bookId);
                
                return new BooksIndexGetResult
                {
                    Success = true,
                    BookID = bookId,
                    TwinID = twinId,
                    BookSearchResults = bookItem,
                    Message = $"Book retrieved successfully for ID {bookId}"
                };
            }
            else
            {
                _logger.LogInformation("📭 No book found for BookID: {BookId}, TwinID: {TwinId}", bookId, twinId);
                
                return new BooksIndexGetResult
                {
                    Success = false,
                    BookID = bookId,
                    TwinID = twinId,
                    Error = $"No book found with ID {bookId}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting book for BookID: {BookId}, TwinID: {TwinId}", bookId, twinId);
            
            return new BooksIndexGetResult
            {
                Success = false,
                BookID = bookId,
                TwinID = twinId,
                Error = $"Error retrieving book: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Generate embeddings for text content using Azure OpenAI
    /// </summary>
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

            _logger.LogDebug("🔢 Generating embeddings for text: {Length} characters", text.Length);

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
    /// Build complete content for vector search from BookMain data
    /// </summary>
    private static string BuildCompleteBookContent(BookMain bookMain)
    {
        var content = new StringBuilder();
        
        content.AppendLine("INFORMACIÓN BÁSICA DEL LIBRO:");
        content.AppendLine($"Título: {bookMain.titulo ?? "Sin título"}");
        content.AppendLine($"Autor: {bookMain.autor ?? "Autor desconocido"}");
        content.AppendLine($"Editorial: {bookMain.editorial ?? "Sin editorial"}");
        content.AppendLine($"Año: {bookMain.añoPublicacion}");
        content.AppendLine($"ISBN: {bookMain.isbn ?? "Sin ISBN"}");
        content.AppendLine($"Género: {bookMain.genero ?? "Sin género"}");
        content.AppendLine();
        
        content.AppendLine("DESCRIPCIÓN:");
        content.AppendLine(bookMain.descripcion ?? "Sin descripción");
        content.AppendLine();

        if (bookMain.datosIA != null)
        {
            content.AppendLine("ANÁLISIS AI:");
            content.AppendLine(bookMain.datosIA.DescripcionAI ?? "Sin análisis AI");
            content.AppendLine();

            if (bookMain.datosIA.INFORMACIÓN_TÉCNICA != null)
            {
                content.AppendLine("INFORMACIÓN TÉCNICA:");
                content.AppendLine($"Título original: {bookMain.datosIA.INFORMACIÓN_TÉCNICA.Título_original ?? "N/A"}");
                content.AppendLine($"Idioma original: {bookMain.datosIA.INFORMACIÓN_TÉCNICA.Idioma_original ?? "N/A"}");
                content.AppendLine($"Primera publicación: {bookMain.datosIA.INFORMACIÓN_TÉCNICA.Primera_publicación ?? "N/A"}");
                content.AppendLine();
            }

            if (bookMain.datosIA.CONTENIDO_Y_TESIS_PRINCIPAL != null)
            {
                content.AppendLine("CONTENIDO Y TESIS:");
                content.AppendLine(bookMain.datosIA.CONTENIDO_Y_TESIS_PRINCIPAL.Idea_central ?? "N/A");
                content.AppendLine();
            }
        }

        if (bookMain.tags != null && bookMain.tags.Any())
        {
            content.AppendLine("TAGS:");
            content.AppendLine(string.Join(", ", bookMain.tags));
            content.AppendLine();
        }

        if (!string.IsNullOrEmpty(bookMain.opiniones))
        {
            content.AppendLine("OPINIONES:");
            content.AppendLine(bookMain.opiniones);
        }
        
        return content.ToString();
    }

    /// <summary>
    /// Generate unique document ID for book analysis
    /// </summary>
    private async Task<string> GenerateUniqueBookDocumentId(string bookId, string twinId)
    {
        var baseId = $"book-{bookId}-{twinId}";
        var documentId = baseId;
        var counter = 1;

        // Check if document exists and increment counter if needed
        while (await BookDocumentExistsAsync(documentId))
        {
            documentId = $"{baseId}-{counter}";
            counter++;
        }

        return documentId;
    }

    /// <summary>
    /// Check if a book document exists in the search index
    /// </summary>
    private async Task<bool> BookDocumentExistsAsync(string documentId)
    {
        try
        {
            if (!IsAvailable) return false;

            var searchClient = new SearchClient(new Uri(_searchEndpoint!), BooksIndexName, new AzureKeyCredential(_searchApiKey!));
            
            var response = await searchClient.GetDocumentAsync<Dictionary<string, object>>(documentId);
            return response != null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error checking book document existence for {DocumentId}", documentId);
            return false;
        }
    }

    /// <summary>
    /// Search books using text query with semantic and vector search
    /// </summary>
    public async Task<BooksSearchResult> SearchBooksAsync(string query, string? twinId = null, int top = 10)
    {
        try
        {
            if (!IsAvailable)
            {
                return new BooksSearchResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("📚 Searching books with query: {Query}, TwinId: {TwinId}", query, twinId);

            var searchClient = new SearchClient(new Uri(_searchEndpoint!), BooksIndexName, new AzureKeyCredential(_searchApiKey!));

            var searchOptions = new SearchOptions
            {
                Size = top,
                IncludeTotalCount = true,
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new()
                {
                    SemanticConfigurationName = SemanticConfig,
                    QueryCaption = new(QueryCaptionType.Extractive),
                    QueryAnswer = new(QueryAnswerType.Extractive)
                }
            };

            // Add filter for specific twin if provided
            if (!string.IsNullOrEmpty(twinId))
            {
                searchOptions.Filter = $"TwinID eq '{twinId}'";
            }

            // Select relevant fields
            var fieldsToSelect = new[]
            {
                "id", "BookID", "TwinID", "TituloLibro", "Author", "ISBN",
                "DescripcionAI", "detailHTMLReport", "Editorial", "Genero", "AñoPublicacion"
            };
            foreach (var field in fieldsToSelect)
            {
                searchOptions.Select.Add(field);
            }

            var searchResponse = await searchClient.SearchAsync<SearchDocument>(query, searchOptions);
            var results = new List<BooksSearchResultItem>();

            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                var bookItem = new BooksSearchResultItem
                {
                    Id = result.Document.GetString("id") ?? string.Empty,
                    BookID = result.Document.GetString("BookID") ?? string.Empty,
                    TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                    TituloLibro = result.Document.GetString("TituloLibro") ?? string.Empty,
                    Author = result.Document.GetString("Author") ?? string.Empty,
                    ISBN = result.Document.GetString("ISBN") ?? string.Empty,
                    DescripcionAI = result.Document.GetString("DescripcionAI") ?? string.Empty,
                    DetailHTMLReport = result.Document.GetString("detailHTMLReport") ?? string.Empty,
                    Editorial = result.Document.GetString("Editorial") ?? string.Empty,
                    Genero = result.Document.GetString("Genero") ?? string.Empty,
                    AñoPublicacion = result.Document.GetInt32("AñoPublicacion") ?? 0,
                    SearchScore = result.Score ?? 0.0
                };

                results.Add(bookItem);
            }

            _logger.LogInformation("✅ Found {Count} books for query: {Query}", results.Count, query);

            return new BooksSearchResult
            {
                Success = true,
                Results = results,
                TotalCount = searchResponse.Value.TotalCount ?? 0,
                Query = query,
                Message = $"Found {results.Count} books"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error searching books with query: {Query}", query);
            return new BooksSearchResult
            {
                Success = false,
                Error = $"Error searching books: {ex.Message}",
                Query = query
            };
        }
    }
}

/// <summary>
/// Result class for books index operations
/// </summary>
public class BooksIndexResult
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
/// Individual book search result item
/// </summary>
public class BooksSearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string BookID { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string TituloLibro { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string DescripcionAI { get; set; } = string.Empty;
    public string DetailHTMLReport { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double ProcessingTimeMS { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string Editorial { get; set; } = string.Empty;
    public string Genero { get; set; } = string.Empty;
    public int AñoPublicacion { get; set; }
    public double SearchScore { get; set; }
}

/// <summary>
/// Result class for getting a specific book by ID
/// </summary>
public class BooksIndexGetResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string BookID { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public BooksSearchResultItem? BookSearchResults { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result class for book search operations
/// </summary>
public class BooksSearchResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<BooksSearchResultItem> Results { get; set; } = new();
    public long TotalCount { get; set; }
    public string Query { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}