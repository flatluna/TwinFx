using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using TwinFx.Agents;
using static TwinFx.Services.NoStructuredServices;
using Microsoft.JSInterop;

namespace TwinFx.Services
{
    /// <summary>
    /// Azure AI Search Service for No-Structured Documents indexing and search
    /// ========================================================================
    /// 
    /// This service creates and manages a search index optimized for no-structured documents with:
    /// - Vector search capabilities using Azure OpenAI embeddings for CapituloExtraido
    /// - Semantic search for natural language queries about document chapters
    /// - Full-text search across chapter content, summaries, and Q&A
    /// - Chapter-based search and filtering
    /// - Document processing tracking and filtering
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class DocumentsNoStructuredIndex
    {
        private readonly ILogger<DocumentsNoStructuredIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Configuration constants
        private const string IndexName = "no-structured-index";
        private const string VectorSearchProfile = "no-structured-vector-profile";
        private const string HnswAlgorithmConfig = "no-structured-hnsw-config";
        private const string SemanticConfig = "no-structured-semantic-config";
        private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions

        // Configuration keys
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;

        public DocumentsNoStructuredIndex(ILogger<DocumentsNoStructuredIndex> logger, IConfiguration configuration)
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
                    _logger.LogInformation("📄 No-Structured Documents Search Index client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure Search client for No-Structured Documents Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure Search credentials not found for No-Structured Documents Index");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("🤖 Azure OpenAI embedding client initialized for No-Structured Documents Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for No-Structured Documents Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure OpenAI credentials not found for No-Structured Documents Index");
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
        /// Check if the no-structured documents search service is available
        /// </summary>
        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Create the no-structured documents search index with vector and semantic search capabilities
        /// Based on the updated ExractedChapterSubsIndex class structure for document chapters and subchapters
        /// </summary>
        public async Task<NoStructuredIndexResult> CreateNoStructuredIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Creating No-Structured Documents Search Index: {IndexName}", IndexName);

                // Define search fields based on the ExractedChapterSubsIndex class structure
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Chapter identification from ExractedChapterSubsIndex
                    new SearchableField("ChapterTitle")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // TwinID from ExractedChapterSubsIndex
                    new SearchableField("TwinID")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },
                    
                    // ChapterID from ExractedChapterSubsIndex
                    new SearchableField("ChapterID")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },
                    
                    // Document metadata from ExractedChapterSubsIndex
                    new SimpleField("TotalTokensDocument", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // File information from ExractedChapterSubsIndex
                    new SearchableField("FileName")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },
                    
                    new SearchableField("FilePath")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    // Chapter content from ExractedChapterSubsIndex
                    new SearchableField("TextChapter")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Chapter page range from ExractedChapterSubsIndex
                    new SimpleField("FromPageChapter", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    new SimpleField("ToPageChapter", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Chapter total tokens from ExractedChapterSubsIndex
                    new SimpleField("TotalTokens", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Subchapter information from ExractedChapterSubsIndex
                    new SearchableField("TitleSub")
                    {
                        IsFilterable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Subchapter content from ExractedChapterSubsIndex (main searchable content)
                    new SearchableField("TextSub")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Subchapter token count from ExractedChapterSubsIndex
                    new SimpleField("TotalTokensSub", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Subchapter page range from ExractedChapterSubsIndex
                    new SimpleField("FromPageSub", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    new SimpleField("ToPageSub", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Processing timestamp
                    new SimpleField("DateCreated", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },
                    
                    // Combined content field for comprehensive search
                    new SearchableField("ContenidoCompleto")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Vector field for semantic similarity search
                    new SearchField("ContenidoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions,
                        VectorSearchProfileName = VectorSearchProfile
                    }
                };

                // Configure vector search
                var vectorSearch = new VectorSearch();

                // Add HNSW algorithm configuration
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswAlgorithmConfig));

                // Add vector search profile
                vectorSearch.Profiles.Add(new VectorSearchProfile(VectorSearchProfile, HnswAlgorithmConfig));

                // Configure semantic search
                var semanticSearch = new SemanticSearch();
                var prioritizedFields = new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("TitleSub")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("TextSub"));
                prioritizedFields.ContentFields.Add(new SemanticField("TextChapter"));
                prioritizedFields.ContentFields.Add(new SemanticField("ContenidoCompleto"));
                prioritizedFields.ContentFields.Add(new SemanticField("ChapterTitle"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("ChapterID"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("TwinID"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("FileName"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

                // Create the no-structured documents search index
                var index = new SearchIndex(IndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ No-Structured Documents Index '{IndexName}' created successfully", IndexName);

                return new NoStructuredIndexResult
                {
                    Success = true,
                    Message = $"No-Structured Documents Index '{IndexName}' created successfully",
                    IndexName = IndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating No-Structured Documents Index");
                return new NoStructuredIndexResult
                {
                    Success = false,
                    Error = $"Error creating No-Structured Documents Index: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Index a CapituloExtraido in Azure AI Search
        /// </summary>
        public async Task<NoStructuredIndexResult> IndexCapituloExtaidoAsync(CapituloDocumento capitulo)
        {
            
            capitulo.CapituloID = DateTime.UtcNow.ToFileTime().ToString() + "-" + Guid.NewGuid().ToString();
            try
            {
                
                if (!IsAvailable)
                {
                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

              
                // Create search client for the no-structured documents index
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate unique document ID
                var documentId = !string.IsNullOrEmpty(capitulo.CapituloID) 
                    ? capitulo.CapituloID 
                    : $"cap_{capitulo.TwinID}_{capitulo.NumeroCapitulo}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                // **NUEVO: Generate DocumentID for grouping chapters from the same document**
                // Extract the base document identifier from the capitulo (assuming filename or document source)
                var documentSourceId = ExtractDocumentSourceId(capitulo);
                int subtema = 0;
                foreach (var sub in capitulo.Subtemas)
                {
                   // sub.DocumentID = capitulo.DocumentID;

                    // Build comprehensive content for vector search
                    var contenidoCompleto = sub.Texto;

                    // Build combined Q&A content


                    // Generate embeddings for the complete content
                    float[]? embeddings = null;
                    if (_embeddingClient != null)
                    {
                        embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);
                    }
                    string id = Guid.NewGuid().ToString();
                    subtema = subtema + 1;
                    // Create search document based on CapituloExtraido structure
                    var searchDocument = new Dictionary<string, object>
                    {
                        ["id"] = id,
                        ["TwinID"] = capitulo.TwinID ?? "",
                        ["CapituloID"] = capitulo.CapituloID,
                        ["DocumentID"] = documentSourceId,
                        ["Total_Subtemas_Capitulo"] = capitulo.Total_Subtemas_Capitulo,
                        ["TextoCompleto"] = capitulo.TextoCompleto ?? "",
                        ["CapituloPaginaDe"] = capitulo.PaginaDe,
                        ["CapituloPaginaA"] = capitulo.PaginaA,
                        ["CapituloTotalTokens"] = capitulo.TotalTokens,
                        ["Total_Palabras_Subtema"] = capitulo.Total_Palabras_Subtemas,
                        ["TitleSubCapitulo"] = sub.Title ?? "",
                        ["TextoSubCapitulo"] = sub.Texto ?? "",
                        ["Descripcion"] = $"Capítulo del documento: {sub.Descripcion}",
                        ["Html"] = sub.Html ?? "",
                        ["TotalTokensCapitulo"] = capitulo.TotalTokens,
                        ["CapituloTimeSeconds"] = capitulo.TimeSeconds,
                        ["DateCreated"] = DateTimeOffset.UtcNow,
                        ["SubtemaID"] = capitulo.CapituloID + "-#" + subtema,
                        ["ContenidoCompleto"] = contenidoCompleto
                    };

                    // Add vector embeddings if available
                    if (embeddings != null)
                    {
                        searchDocument["ContenidoVector"] = embeddings;
                    }

                    // Upload document to search index
                    var documents = new[] { new SearchDocument(searchDocument) };
                    var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

                    var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();
 
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing chapter: {CapituloID}", capitulo.CapituloID);
                return new NoStructuredIndexResult
                {
                    Success = false,
                    Error = $"Error indexing chapter: {ex.Message}"
                };
            }

            return new NoStructuredIndexResult
            {
                Success = true,
                Message = $"Chapter '{capitulo.Titulo}' indexed successfully",
                IndexName = IndexName,
                DocumentId = capitulo.DocumentID
            };
        }

        /// <summary>
        /// Index multiple CapituloExtraido from a document processing result
        /// </summary>
        public async Task<List<NoStructuredIndexResult>> IndexMultipleCapitulosAsync(List<CapituloDocumento> capitulos, string TwinID)
        {
            var results = new List<NoStructuredIndexResult>();

            if (!capitulos.Any())
            {
                _logger.LogWarning("⚠️ No chapters provided for indexing");
                return results;
            }

            _logger.LogInformation("📚 Starting bulk indexing of {ChapterCount} chapters", capitulos.Count);
            string DocumentoID = DateTime.Now.ToFileTime().ToString() + "-" + Guid.NewGuid();
            foreach (var capitulo in capitulos)
            {
                try
                {
                    capitulo.DocumentID = DocumentoID;
                    capitulo.TwinID = TwinID;
                    var result = await IndexCapituloExtaidoAsync(capitulo);
                    results.Add(result);

                    if (result.Success)
                    {
                        _logger.LogInformation("✅ Chapter indexed: {Title}", capitulo.Titulo);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to index chapter: {Title} - {Error}", capitulo.Titulo, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error indexing chapter: {Title}", capitulo.Titulo);
                    results.Add(new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = $"Error indexing chapter '{capitulo.Titulo}': {ex.Message}",
                        DocumentId = capitulo.CapituloID
                    });
                }
            }

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            _logger.LogInformation("📊 Bulk indexing completed: {SuccessCount} successful, {FailureCount} failed",
                successCount, failureCount);

            return results;
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

                // Truncate text if too long for embeddings
                if (text.Length > 8000)
                {
                    text = text.Substring(0, 8000);
                    _logger.LogDebug("✂️ Text truncated to 8000 characters for embedding generation");
                }

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
                _logger.LogWarning(ex, "⚠️ Error generating embeddings, continuing without vector search");
                return null;
            }
        }

        /// <summary>
        /// Build complete content for vector search by combining all relevant fields from CapituloExtraido
        /// </summary>
        private static string BuildCompleteChapterContent(CapituloExtraido capitulo)
        {
            var content = new List<string>();

            // Document classification
            content.Add($"Estructura: {capitulo.Estructura}");
            content.Add($"Subcategoría: {capitulo.Subcategoria}");

            // Chapter identification
            content.Add($"Capítulo: {capitulo.Titulo}");
            content.Add($"Número: {capitulo.NumeroCapitulo}");
            content.Add($"Páginas: {capitulo.PaginaDe} - {capitulo.PaginaA}");
            content.Add($"Nivel: {capitulo.Nivel}");

            // Main content
            if (!string.IsNullOrEmpty(capitulo.ResumenEjecutivo))
                content.Add($"Resumen: {capitulo.ResumenEjecutivo}");

            if (!string.IsNullOrEmpty(capitulo.TextoCompleto))
                content.Add($"Contenido: {capitulo.TextoCompleto}");

            // Q&A content
            if (capitulo.PreguntasFrecuentes?.Any() == true)
            {
                content.Add("Preguntas y respuestas:");
                foreach (var qa in capitulo.PreguntasFrecuentes)
                {
                    content.Add($"P: {qa.Pregunta}");
                    content.Add($"R: {qa.Respuesta}");
                }
            }

            // Metadata
            content.Add($"Tokens: {capitulo.TotalTokens}");
            content.Add($"Procesado: {capitulo.ProcessedAt:yyyy-MM-dd}");

            return string.Join(". ", content);
        }

        /// <summary>
        /// Build Q&A content for search from PreguntasFrecuentes
        /// </summary>
        private static string BuildQAContent(List<PreguntaFrecuente> preguntas)
        {
            if (preguntas?.Any() != true)
                return "";

            var qaContent = new List<string>();

            foreach (var qa in preguntas)
            {
                qaContent.Add($"Pregunta: {qa.Pregunta}");
                qaContent.Add($"Respuesta: {qa.Respuesta}");
                
                if (!string.IsNullOrEmpty(qa.Categoria))
                    qaContent.Add($"Categoría: {qa.Categoria}");
            }

            return string.Join(". ", qaContent);
        }

        /// <summary>
        /// Search chapters by Estructura and TwinID with semantic search capabilities
        /// </summary>
        /// <param name="estructura">Document structure type to filter by</param>
        /// <param name="twinId">Twin ID to filter by</param>
        /// <param name="searchQuery">Optional search query for content search</param>
        /// <param name="top">Maximum number of results to return</param>
        /// <returns>Search results containing matching chapters</returns>
        public async Task<NoStructuredSearchResult> SearchByEstructuraAndTwinAsync(
            string estructura, 
            string twinId, 
            string? searchQuery = null, 
            int top = 1000)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Searching chapters by Estructura: {Estructura}, TwinID: {TwinId}, Query: {Query}", 
                    estructura, twinId, searchQuery);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

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

                // Build filter for Estructura and TwinID
                var filterParts = new List<string>();
                
                if (!string.IsNullOrEmpty(estructura))
                {
                    filterParts.Add($"Estructura eq '{estructura.Replace("'", "''")}'");
                }
                
                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // Select relevant fields
                var fieldsToSelect = new[]
                {
                    "id", "DocumentID", "CapituloID", "TwinID", "Estructura", "Subcategoria", "Titulo", 
                    "NumeroCapitulo", "PaginaDe", "PaginaA", "Nivel", "TotalTokens",
                    "TextoCompleto", "TextoCompletoHTML", "ResumenEjecutivo", "PreguntasFrecuentes", 
                    "ProcessedAt"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Use search query if provided, otherwise use "*" to get all matching documents
                var searchText = !string.IsNullOrEmpty(searchQuery) ? searchQuery : "*";

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
                var chapterResults = new List<NoStructuredSearchResultItem>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new NoStructuredSearchResultItem
                    {
                        Id = result.Document.GetString("id") ?? string.Empty,
                        DocumentID = result.Document.GetString("DocumentID") ?? string.Empty,
                        CapituloID = result.Document.GetString("CapituloID") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        SubtemaID = result.Document.GetString("SubtemaID") ?? string.Empty,
                        
                        // Campos del capítulo
                        Total_Subtemas_Capitulo = result.Document.GetInt32("Total_Subtemas_Capitulo") ?? 0,
                        TextoCompleto = result.Document.GetString("TextoCompleto") ?? string.Empty,
                        CapituloPaginaDe = result.Document.GetInt32("CapituloPaginaDe") ?? 0,
                        CapituloPaginaA = result.Document.GetInt32("CapituloPaginaA") ?? 0,
                        CapituloTotalTokens = result.Document.GetInt32("CapituloTotalTokens") ?? 0,
                        CapituloTimeSeconds = result.Document.GetInt32("CapituloTimeSeconds") ?? 0,
                        
                        // Campos del subtema
                        Total_Palabras_Subtema = result.Document.GetInt32("Total_Palabras_Subtema") ?? 0,
                        TitleSubCapitulo = result.Document.GetString("Nombre") ?? string.Empty,
                        TextoSubCapitulo = result.Document.GetString("Texto") ?? string.Empty,
                        Descripcion = result.Document.GetString("Descripcion") ?? string.Empty,
                        Html = result.Document.GetString("Html") ?? string.Empty,
                        TotalTokensCapitulo = result.Document.GetInt32("TotalTokensCapitulo") ?? 0,
                        DateCreated = result.Document.GetDateTimeOffset("DateCreated") ?? DateTimeOffset.MinValue,
                        
                        // Campos de búsqueda y compatibilidad hacia atrás
                        Titulo = result.Document.GetString("Titulo") ?? string.Empty,
                        NumeroCapitulo = result.Document.GetString("NumeroCapitulo") ?? string.Empty,
                        PaginaDe = result.Document.GetInt32("PaginaDe") ?? 0,
                        PaginaA = result.Document.GetInt32("PaginaA") ?? 0,
                        Nivel = result.Document.GetInt32("Nivel") ?? 1,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,
                        TextoCompletoHTML = result.Document.GetString("TextoCompletoHTML") ?? string.Empty,
                        ResumenEjecutivo = result.Document.GetString("ResumenEjecutivo") ?? string.Empty,
                        PreguntasFrecuentes = result.Document.GetString("PreguntasFrecuentes") ?? string.Empty,
                        ProcessedAt = result.Document.GetDateTimeOffset("ProcessedAt") ?? DateTimeOffset.MinValue,
                        SearchScore = result.Score ?? 0.0
                    };

                    // Extract semantic captions if available
                    if (result.SemanticSearch?.Captions?.Any() == true)
                    {
                        chapterItem.Highlights = result.SemanticSearch.Captions
                            .Select(c => ExtractCaptionText(c))
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }

                    chapterResults.Add(chapterItem);
                }

                // **NUEVO: Agrupar capítulos por DocumentID para crear documentos únicos**
                var groupedDocuments = chapterResults
                    .GroupBy(chapter => chapter.DocumentID)
                    .Select(group => new NoStructuredDocument
                    {
                        DocumentID = group.Key,
                        TwinID = group.First().TwinID, 
                        TotalChapters = group.Count(),
                        TotalTokens = group.Sum(c => c.TotalTokens),
                        TotalPages = group.Any() ? group.Max(c => c.PaginaA) - group.Min(c => c.PaginaDe) + 1 : 0,
                        ProcessedAt = group.Max(c => c.ProcessedAt),
                        SearchScore = group.Max(c => c.SearchScore), // Use highest score as document score
                        Capitulos = group.OrderBy(c => c.PaginaDe).ThenBy(c => c.NumeroCapitulo).ToList()
                    })
                    .OrderByDescending(doc => doc.SearchScore)
                    .ToList();

                _logger.LogInformation("✅ Found {ChapterCount} chapters grouped into {DocumentCount} documents for Estructura: {Estructura}, TwinID: {TwinId}", 
                    chapterResults.Count, groupedDocuments.Count, estructura, twinId);

                return new NoStructuredSearchResult
                {
                    Success = true,
                    Documents = groupedDocuments,
                    TotalChapters = searchResponse.Value.TotalCount ?? 0,
                    TotalDocuments = groupedDocuments.Count,
                    SearchQuery = searchQuery ?? "*",
                    SearchType = "FilterByEstructuraAndTwin",
                    Message = $"Found {groupedDocuments.Count} documents with {chapterResults.Count} total chapters for structure '{estructura}' and Twin '{twinId}'"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching chapters by Estructura: {Estructura}, TwinID: {TwinId}", estructura, twinId);
                return new NoStructuredSearchResult
                {
                    Success = false,
                    Error = $"Error searching chapters: {ex.Message}",
                    SearchType = "FilterByEstructuraAndTwin"
                };
            }
        }

        /// <summary>
        /// Get a specific document with all its chapters by TwinID and DocumentID
        /// </summary>
        /// <param name="twinId">Twin ID to filter by</param>
        /// <param name="documentId">Specific DocumentID to retrieve</param>
        /// <returns>Single document with all its chapters or null if not found</returns>
        public async Task<NoStructuredDocument?> GetDocumentByTwinIdAndDocumentIdAsync(
            string twinId,
            string documentId)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogError("❌ Azure Search service not available");
                    return null;
                }

                _logger.LogInformation("📄 Getting document by TwinID: {TwinId}, DocumentID: {DocumentId}", 
                    twinId, documentId);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = 1000, // Get all chapters for this document
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for exact TwinID and DocumentID match
                var filter = $"TwinID eq '{twinId.Replace("'", "''")}' and DocumentID eq '{documentId.Replace("'", "''")}'";
                searchOptions.Filter = filter;

                // Select all relevant fields including the new index structure
                var fieldsToSelect = new[]
                {
                    "id", "TwinID", "SubtemaID", "CapituloID", "DocumentID", "Total_Subtemas_Capitulo",
                    "TextoCompleto", "CapituloPaginaDe", "CapituloPaginaA", "CapituloTotalTokens",
                    "Total_Palabras_Subtema", "CapituloTimeSeconds", "TitleSubCapitulo", "TextoSubCapitulo", "Descripcion",
                    "Html", "TotalTokensCapitulo", "DateCreated"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Use "*" to get all matching documents for this specific DocumentID
                var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
                var chapterResults = new List<NoStructuredSearchResultItem>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new NoStructuredSearchResultItem
                    {
                        Id = result.Document.GetString("id") ?? string.Empty,
                        DocumentID = result.Document.GetString("DocumentID") ?? string.Empty,
                        CapituloID = result.Document.GetString("CapituloID") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        SubtemaID = result.Document.GetString("SubtemaID") ?? string.Empty,
                        Total_Subtemas_Capitulo = result.Document.GetInt32("Total_Subtemas_Capitulo") ?? 0,
                        TextoCompleto = result.Document.GetString("TextoCompleto") ?? string.Empty,
                        CapituloPaginaDe = result.Document.GetInt32("CapituloPaginaDe") ?? 0,
                        CapituloPaginaA = result.Document.GetInt32("CapituloPaginaA") ?? 0,
                        CapituloTotalTokens = result.Document.GetInt32("CapituloTotalTokens") ?? 0,
                        CapituloTimeSeconds = result.Document.GetInt32("CapituloTimeSeconds") ?? 0,
                        Total_Palabras_Subtema = result.Document.GetInt32("Total_Palabras_Subtema") ?? 0,
                        TitleSubCapitulo = result.Document.GetString("TitleSubCapitulo") ?? string.Empty,
                        TextoSubCapitulo = result.Document.GetString("TextoSubCapitulo") ?? string.Empty,
                        Descripcion = result.Document.GetString("Descripcion") ?? string.Empty,
                        Html = result.Document.GetString("Html") ?? string.Empty,
                        TotalTokensCapitulo = result.Document.GetInt32("TotalTokensCapitulo") ?? 0,
                        DateCreated = result.Document.GetDateTimeOffset("DateCreated") ?? DateTimeOffset.MinValue,
                        SearchScore = result.Score ?? 0.0
                    };

                    // Extract semantic captions if available
                    if (result.SemanticSearch?.Captions?.Any() == true)
                    {
                        chapterItem.Highlights = result.SemanticSearch.Captions
                            .Select(c => ExtractCaptionText(c))
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }

                    chapterResults.Add(chapterItem);
                }

                if (!chapterResults.Any())
                {
                    _logger.LogInformation("📭 No document found for TwinID: {TwinId}, DocumentID: {DocumentId}", 
                        twinId, documentId);
                    return null;
                }

                // Create single document with all its chapters
                var document = new NoStructuredDocument
                {
                    DocumentID = documentId,
                    TwinID = chapterResults.First().TwinID,
                    TotalChapters = chapterResults.Count,
                    TotalTokens = chapterResults.Sum(c => c.TotalTokensCapitulo),
                    TotalPages = chapterResults.Any() ? chapterResults.Max(c => c.CapituloPaginaA) - chapterResults.Min(c => c.CapituloPaginaDe) + 1 : 0,
                    ProcessedAt = chapterResults.Max(c => c.DateCreated),
                    SearchScore = chapterResults.Max(c => c.SearchScore),
                    Capitulos = chapterResults.OrderBy(c => c.CapituloPaginaDe).ThenBy(c => c.SubtemaID).ToList()
                };

                _logger.LogInformation("✅ Found document with {ChapterCount} subtemas for TwinID: {TwinId}, DocumentID: {DocumentId}", 
                    document.TotalChapters, twinId, documentId);

                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting document by TwinID: {TwinId}, DocumentID: {DocumentId}", twinId, documentId);
                return null;
            }
        }

        /// <summary>
        /// Search documents metadata by Estructura and TwinID - Returns only document summaries without chapter content
        /// </summary>
        /// <param name="estructura">Document structure type to filter by</param>
        /// <param name="twinId">Twin ID to filter by</param>
        /// <param name="searchQuery">Optional search query for content search</param>
        /// <param name="top">Maximum number of results to return</param>
        /// <returns>Search results containing only document metadata</returns>
        public async Task<NoStructuredSearchMetadataResult> SearchDocumentMetadataByEstructuraAndTwinAsync(
            string estructura, 
            string twinId, 
            string? searchQuery = null, 
            int top = 1000)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredSearchMetadataResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Searching document metadata by Estructura: {Estructura}, TwinID: {TwinId}, Query: {Query}", 
                    estructura, twinId, searchQuery);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

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

                // Build filter for Estructura and TwinID
                var filterParts = new List<string>();
                

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // Select only minimal fields for metadata - NO chapter content
                var fieldsToSelect = new[]
                {
                    "id", "DocumentID", "TwinID",   
                    "Total_Subtemas_Capitulo",
                    "TotalTokensCapitulo"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Use search query if provided, otherwise use "*" to get all matching documents
                var searchText = !string.IsNullOrEmpty(searchQuery) ? searchQuery : "*";

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
                var chapterResults = new List<NoStructuredSearchResultItem>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new NoStructuredSearchResultItem
                    {
                        Id = result.Document.GetString("id") ?? string.Empty,
                        DocumentID = result.Document.GetString("DocumentID") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty, 
                        TotalTokens = result.Document.GetInt32("TotalTokensCapitulo") ?? 0,
                        Total_Subtemas_Capitulo = result.Document.GetInt32("Total_Subtemas_Capitulo") ?? 0,
                        SearchScore = result.Score ?? 0.0
                        // NOTE: NO incluimos Titulo, TextoCompleto, ResumenEjecutivo, etc. para mantener la respuesta ligera
                    };

                    chapterResults.Add(chapterItem);
                }

                // **NUEVO: Agrupar capítulos por DocumentID para crear metadatos de documentos únicos**
                var groupedDocumentsMetadata = chapterResults
                    .GroupBy(chapter => chapter.DocumentID)
                    .Select(group => new NoStructuredDocumentMetadata
                    {
                        DocumentID = group.Key,
                        TwinID = group.First().TwinID, 
                        TotalChapters = group.Count(),
                        TotalTokens = group.Sum(c => c.TotalTokens),
                        TotalPages = group.Any() ? group.Max(c => c.PaginaA) - group.Min(c => c.PaginaDe) + 1 : 0, 
                        SearchScore = group.Max(c => c.SearchScore) // Use highest score as document score
                    })
                    .OrderByDescending(doc => doc.SearchScore)
                    .ToList();

                _logger.LogInformation("✅ Found {ChapterCount} chapters grouped into {DocumentCount} document metadata for Estructura: {Estructura}, TwinID: {TwinId}", 
                    chapterResults.Count, groupedDocumentsMetadata.Count, estructura, twinId);

                return new NoStructuredSearchMetadataResult
                {
                    Success = true,
                    Documents = groupedDocumentsMetadata,
                    TotalChapters = searchResponse.Value.TotalCount ?? 0,
                    TotalDocuments = groupedDocumentsMetadata.Count,
                    SearchQuery = searchQuery ?? "*",
                    SearchType = "FilterByEstructuraAndTwin_MetadataOnly",
                    Message = $"Found {groupedDocumentsMetadata.Count} documents metadata with {chapterResults.Count} total chapters for structure '{estructura}' and Twin '{twinId}'"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching document metadata by Estructura: {Estructura}, TwinID: {TwinId}", estructura, twinId);
                return new NoStructuredSearchMetadataResult
                {
                    Success = false,
                    Error = $"Error searching document metadata: {ex.Message}",
                    SearchType = "FilterByEstructuraAndTwin_MetadataOnly"
                };
            }
        }

        /// <summary>
        /// Delete a complete document and all its chapters by DocumentID
        /// </summary>
        /// <param name="documentId">DocumentID to delete</param>
        /// <param name="twinId">Optional TwinID for additional validation</param>
        /// <returns>Result indicating success and number of deleted chapters</returns>
        public async Task<NoStructuredDeleteResult> DeleteDocumentByDocumentIdAsync(
            string documentId,
            string? twinId = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredDeleteResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("🗑️ Deleting document with DocumentID: {DocumentId}, TwinID: {TwinId}", 
                    documentId, twinId ?? "Any");

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Step 1: Find all chapters belonging to this document
                var searchOptions = new SearchOptions
                {
                    Size = 1000, // Get all chapters for this document
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for DocumentID and optionally TwinID
                var filterParts = new List<string>
                {
                    $"DocumentID eq '{documentId.Replace("'", "''")}'"
                };

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                searchOptions.Filter = string.Join(" and ", filterParts);

                // Select only the id field to minimize data transfer
                searchOptions.Select.Add("id");
                searchOptions.Select.Add("CapituloID");
                
                searchOptions.Select.Add("TwinID");

                // Search for all chapters to delete
                var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
                var chaptersToDelete = new List<string>();
                var chapterInfos = new List<(string Id, string Title, string TwinId)>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterId = result.Document.GetString("id");
                    var chapterTwinId = result.Document.GetString("TwinID") ?? "Unknown";
                    
                    if (!string.IsNullOrEmpty(chapterId))
                    {
                        chaptersToDelete.Add(chapterId);
                        chapterInfos.Add((chapterId, "Subtema", chapterTwinId));
                    }
                }

                if (!chaptersToDelete.Any())
                {
                    _logger.LogInformation("📭 No chapters found for DocumentID: {DocumentId}", documentId);
                    return new NoStructuredDeleteResult
                    {
                        Success = true,
                        DocumentId = documentId,
                        DeletedChaptersCount = 0,
                        Message = $"No chapters found for DocumentID '{documentId}'"
                    };
                }

                _logger.LogInformation("🔍 Found {ChapterCount} chapters to delete for DocumentID: {DocumentId}", 
                    chaptersToDelete.Count, documentId);

                // Log chapter details
                foreach (var (id, title, chapterTwinId) in chapterInfos)
                {
                    _logger.LogDebug("  📖 Chapter: {ChapterId} - {Title} (TwinID: {TwinId})", id, title, chapterTwinId);
                }

                // Step 2: Delete all chapters in batches
                var deleteActions = chaptersToDelete.Select(id => IndexDocumentsAction.Delete("id", id)).ToList();
                
                var batchSize = 100; // Azure Search batch limit
                var totalDeleted = 0;
                var errors = new List<string>();

                for (int i = 0; i < deleteActions.Count; i += batchSize)
                {
                    var batch = deleteActions.Skip(i).Take(batchSize).ToList();
                    
                    try
                    {
                        var batchResult = await searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Create(batch.ToArray()));
                        
                        var successfulDeletes = batchResult.Value.Results.Count(r => r.Succeeded);
                        var failedDeletes = batchResult.Value.Results.Where(r => !r.Succeeded).ToList();
                        
                        totalDeleted += successfulDeletes;

                        if (failedDeletes.Any())
                        {
                            foreach (var failed in failedDeletes)
                            {
                                var errorMsg = $"Failed to delete chapter {failed.Key}: {failed.ErrorMessage}";
                                errors.Add(errorMsg);
                                _logger.LogWarning("⚠️ {ErrorMessage}", errorMsg);
                            }
                        }

                        _logger.LogInformation("🗑️ Batch {BatchNumber}: Deleted {SuccessCount}/{TotalCount} chapters", 
                            (i / batchSize) + 1, successfulDeletes, batch.Count);
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Error deleting batch {(i / batchSize) + 1}: {ex.Message}";
                        errors.Add(errorMsg);
                        _logger.LogError(ex, "❌ {ErrorMessage}", errorMsg);
                    }
                }

                var finalResult = new NoStructuredDeleteResult
                {
                    Success = totalDeleted > 0,
                    DocumentId = documentId,
                    DeletedChaptersCount = totalDeleted,
                    TotalChaptersFound = chaptersToDelete.Count,
                    Message = errors.Any() 
                        ? $"Deleted {totalDeleted}/{chaptersToDelete.Count} chapters with {errors.Count} errors"
                        : $"Successfully deleted all {totalDeleted} chapters for document '{documentId}'",
                    Errors = errors
                };

                if (finalResult.Success)
                {
                    _logger.LogInformation("✅ Document deletion completed: DocumentID={DocumentId}, Deleted={DeletedCount}/{TotalCount}", 
                        documentId, totalDeleted, chaptersToDelete.Count);
                }
                else
                {
                    _logger.LogError("❌ Document deletion failed: DocumentID={DocumentId}, Deleted={DeletedCount}/{TotalCount}", 
                        documentId, totalDeleted, chaptersToDelete.Count);
                }

                return finalResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting document by DocumentID: {DocumentId}", documentId);
                return new NoStructuredDeleteResult
                {
                    Success = false,
                    Error = $"Error deleting document: {ex.Message}",
                    DocumentId = documentId
                };
            }
        }

        /// <summary>
        /// Extract DocumentID for grouping chapters from the same document
        /// </summary>
        /// <param name="capitulo">Chapter to extract document ID from</param>
        /// <returns>Document ID for grouping purposes</returns>
        private static string ExtractDocumentSourceId(CapituloDocumento capitulo)
        {
            // Priority 1: Use existing DocumentID if available (from CapituloExtraido)
            if (!string.IsNullOrEmpty(capitulo.DocumentID))
            {
                return capitulo.DocumentID;
            }

            // Priority 2: Generate based on TwinID + Estructura + Subcategoria + ProcessedAt date

            var documentId = capitulo.DocumentID;
            
            // Clean any invalid characters for search index
            documentId = documentId.Replace(" ", "_").Replace("-", "_").ToLowerInvariant();
            
            return documentId;
        }

        /// <summary>
        /// Helper method to extract text from semantic search captions
        /// </summary>
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

            // Fall back to Highlights property
            var highlightsProperty = captionType.GetProperty("Highlights");
            if (highlightsProperty != null)
            {
                var highlightsValue = highlightsProperty.GetValue(caption);
                if (highlightsValue is IEnumerable<string> highlights)
                {
                    return string.Join(" ", highlights);
                }
                if (highlightsValue != null)
                {
                    return highlightsValue.ToString() ?? string.Empty;
                }
            }

            return caption.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Index an ExractedChapterSubsIndex in Azure AI Search
        /// </summary>
        public async Task<NoStructuredIndexResult> IndexExractedChapterSubsIndexAsync(ExractedChapterSubsIndex chapterSubsIndex)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                // Create search client for the no-structured documents index
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate unique document ID
                var documentId = !string.IsNullOrEmpty(chapterSubsIndex.id) 
                    ? chapterSubsIndex.id 
                    : $"chap_{chapterSubsIndex.TwinID}_{chapterSubsIndex.ChapterID}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                // Build comprehensive content for vector search
                var contenidoCompleto = BuildCompleteContent(chapterSubsIndex);

                // Generate embeddings for the complete content
                float[]? embeddings = null;
                if (_embeddingClient != null)
                {
                    embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);
                }

                // Create search document based on ExractedChapterSubsIndex structure
                var searchDocument = new Dictionary<string, object>
                {
                    ["id"] = documentId,
                    ["ChapterTitle"] = chapterSubsIndex.ChapterTitle ?? "",
                    ["TwinID"] = chapterSubsIndex.TwinID ?? "",
                    ["ChapterID"] = chapterSubsIndex.ChapterID ?? "",
                    ["TotalTokensDocument"] = chapterSubsIndex.TotalTokensDocument,
                    ["FileName"] = chapterSubsIndex.FileName ?? "",
                    ["FilePath"] = chapterSubsIndex.FilePath ?? "",
                    ["TextChapter"] = chapterSubsIndex.TextChapter ?? "",
                    ["FromPageChapter"] = chapterSubsIndex.FromPageChapter,
                    ["ToPageChapter"] = chapterSubsIndex.ToPageChapter,
                    ["TotalTokens"] = chapterSubsIndex.TotalTokens,
                    ["TitleSub"] = chapterSubsIndex.TitleSub ?? "",
                    ["TextSub"] = chapterSubsIndex.TextSub ?? "",
                    ["TotalTokensSub"] = chapterSubsIndex.TotalTokensSub,
                    ["FromPageSub"] = chapterSubsIndex.FromPageSub,
                    ["ToPageSub"] = chapterSubsIndex.ToPageSub,
                    ["DateCreated"] = DateTimeOffset.UtcNow,
                    ["ContenidoCompleto"] = contenidoCompleto
                };

                // Add vector embeddings if available
                if (embeddings != null)
                {
                    searchDocument["ContenidoVector"] = embeddings;
                }

                // Upload document to search index
                var documents = new[] { new SearchDocument(searchDocument) };
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (errors.Any())
                {
                    var errorMessages = errors.Select(e => e.ErrorMessage).ToList();
                    _logger.LogError("❌ Error indexing chapter: {ChapterTitle} - Errors: {Errors}", 
                        chapterSubsIndex.ChapterTitle, string.Join(", ", errorMessages));
                    
                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = $"Error indexing chapter: {string.Join(", ", errorMessages)}"
                    };
                }

                _logger.LogInformation("✅ ExractedChapterSubsIndex indexed successfully: {ChapterTitle}", chapterSubsIndex.ChapterTitle);

                return new NoStructuredIndexResult
                {
                    Success = true,
                    Message = $"Chapter '{chapterSubsIndex.ChapterTitle}' indexed successfully",
                    IndexName = IndexName,
                    DocumentId = documentId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing ExractedChapterSubsIndex: {ChapterID}", chapterSubsIndex.ChapterID);
                return new NoStructuredIndexResult
                {
                    Success = false,
                    Error = $"Error indexing chapter: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Index multiple ExractedChapterSubsIndex from a document processing result
        /// </summary>
        public async Task<List<NoStructuredIndexResult>> IndexMultipleExractedChapterSubsIndexAsync(List<ExractedChapterSubsIndex> chaptersSubsIndex, string twinID)
        {
            var results = new List<NoStructuredIndexResult>();

            if (!chaptersSubsIndex.Any())
            {
                _logger.LogWarning("⚠️ No chapters provided for indexing");
                return results;
            }

            _logger.LogInformation("📚 Starting bulk indexing of {ChapterCount} chapters with subchapters", chaptersSubsIndex.Count);

            foreach (var chapterSubsIndex in chaptersSubsIndex)
            {
                try
                {
                    // Ensure TwinID is set
                    if (string.IsNullOrEmpty(chapterSubsIndex.TwinID))
                    {
                        chapterSubsIndex.TwinID = twinID;
                    }

                    var result = await IndexExractedChapterSubsIndexAsync(chapterSubsIndex);
                    results.Add(result);

                    if (result.Success)
                    {
                        _logger.LogInformation("✅ Chapter indexed: {Title}", chapterSubsIndex.ChapterTitle);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to index chapter: {Title} - {Error}", chapterSubsIndex.ChapterTitle, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error indexing chapter: {Title}", chapterSubsIndex.ChapterTitle);
                    results.Add(new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = $"Error indexing chapter '{chapterSubsIndex.ChapterTitle}': {ex.Message}",
                        DocumentId = chapterSubsIndex.ChapterID
                    });
                }
            }

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            _logger.LogInformation("📊 Bulk indexing completed: {SuccessCount} successful, {FailureCount} failed",
                successCount, failureCount);

            return results;
        }

        /// <summary>
        /// Build complete content for vector search by combining all relevant fields from ExractedChapterSubsIndex
        /// </summary>
        private static string BuildCompleteContent(ExractedChapterSubsIndex chapterSubsIndex)
        {
            var content = new List<string>();

            // Document information
            if (!string.IsNullOrEmpty(chapterSubsIndex.FileName))
                content.Add($"Archivo: {chapterSubsIndex.FileName}");

            if (!string.IsNullOrEmpty(chapterSubsIndex.FilePath))
                content.Add($"Ruta: {chapterSubsIndex.FilePath}");

            // Chapter information
            if (!string.IsNullOrEmpty(chapterSubsIndex.ChapterTitle))
                content.Add($"Capítulo: {chapterSubsIndex.ChapterTitle}");

            if (!string.IsNullOrEmpty(chapterSubsIndex.TextChapter))
                content.Add($"Contenido del capítulo: {chapterSubsIndex.TextChapter}");

            content.Add($"Páginas del capítulo: {chapterSubsIndex.FromPageChapter} - {chapterSubsIndex.ToPageChapter}");

            // Subchapter information
            if (!string.IsNullOrEmpty(chapterSubsIndex.TitleSub))
                content.Add($"Subcapítulo: {chapterSubsIndex.TitleSub}");

            if (!string.IsNullOrEmpty(chapterSubsIndex.TextSub))
                content.Add($"Contenido del subcapítulo: {chapterSubsIndex.TextSub}");

            content.Add($"Páginas del subcapítulo: {chapterSubsIndex.FromPageSub} - {chapterSubsIndex.ToPageSub}");

            // Metadata
            content.Add($"Tokens del documento: {chapterSubsIndex.TotalTokensDocument}");
            content.Add($"Tokens del capítulo: {chapterSubsIndex.TotalTokens}");
            content.Add($"Tokens del subcapítulo: {chapterSubsIndex.TotalTokensSub}");

            return string.Join(". ", content);
        }
    }

    /// <summary>
    /// Result class for no-structured documents index operations
    /// </summary>
    public class NoStructuredIndexResult
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
    /// Document summary that groups chapters by DocumentID
    /// </summary>
    public class NoStructuredDocument
    {
        public string DocumentID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string Estructura { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public int TotalChapters { get; set; }
        public int TotalTokens { get; set; }
        public int TotalPages { get; set; }
        public DateTimeOffset ProcessedAt { get; set; }
        public double SearchScore { get; set; }
        public List<NoStructuredSearchResultItem> Capitulos { get; set; } = new();
        
        /// <summary>
        /// Get document title based on the first chapter or generate one
        /// </summary>
        public string DocumentTitle => Capitulos.Any() ? 
            $"Documento {Estructura} - {Subcategoria}" : 
            "Documento sin título";
    }

    /// <summary>
    /// Document metadata summary without chapters content - for lightweight responses
    /// </summary>
    public class NoStructuredDocumentMetadata
    {
        public string DocumentID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string Estructura { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public int TotalChapters { get; set; }
        public int TotalTokens { get; set; }
        public int TotalPages { get; set; }
        public DateTimeOffset ProcessedAt { get; set; }
        public double SearchScore { get; set; }
        
        /// <summary>
        /// Get document title based on structure and subcategory
        /// </summary>
        public string DocumentTitle => $"Documento {Estructura} - {Subcategoria}";
    }

    /// <summary>
    /// Search result class for no-structured documents - NEW VERSION with grouped documents
    /// </summary>
    public class NoStructuredSearchResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<NoStructuredDocument> Documents { get; set; } = new();
        public long TotalChapters { get; set; }
        public int TotalDocuments { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string SearchType { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    /// <summary>
    /// Search result class for no-structured documents metadata - lightweight version
    /// </summary>
    public class NoStructuredSearchMetadataResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<NoStructuredDocumentMetadata> Documents { get; set; } = new();
        public long TotalChapters { get; set; }
        public int TotalDocuments { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string SearchType { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    /// <summary>
    /// Result class for document deletion operations
    /// </summary>
    public class NoStructuredDeleteResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string DocumentId { get; set; } = string.Empty;
        public int DeletedChaptersCount { get; set; }
        public int TotalChaptersFound { get; set; }
        public string? Message { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Individual search result item for no-structured documents
    /// </summary>
    public class NoStructuredSearchResultItem
    {
        public string Id { get; set; } = string.Empty;
        public string DocumentID { get; set; } = string.Empty;
        public string CapituloID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string SubtemaID { get; set; } = string.Empty;
        
        // Campos del capítulo
        public int Total_Subtemas_Capitulo { get; set; } = 0;
        public string TextoCompleto { get; set; } = string.Empty;
        public int CapituloPaginaDe { get; set; }
        public int CapituloPaginaA { get; set; }
        public int CapituloTotalTokens { get; set; }
        public int CapituloTimeSeconds { get; set; }
        
        // Campos del subtema
        public int Total_Palabras_Subtema { get; set; }
        public string TitleSubCapitulo { get; set; } = string.Empty;
        public string TextoSubCapitulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
        public int TotalTokensCapitulo { get; set; }
        public DateTimeOffset DateCreated { get; set; }
        
        // Campos de búsqueda y compatibilidad hacia atrás
        public string Titulo { get; set; } = string.Empty;
        public string NumeroCapitulo { get; set; } = string.Empty;
        public int PaginaDe { get; set; }
        public int PaginaA { get; set; }
        public int Nivel { get; set; }
        public int TotalTokens { get; set; }
        public string TextoCompletoHTML { get; set; } = string.Empty;
        public string ResumenEjecutivo { get; set; } = string.Empty;
        public string PreguntasFrecuentes { get; set; } = string.Empty;
        public DateTimeOffset ProcessedAt { get; set; }
        public double SearchScore { get; set; }
        public List<string> Highlights { get; set; } = new();
    }

    /// <summary>
    /// New structure for chapter and subchapter indexing in no-structured documents
    /// </summary>
    public class ExractedChapterSubsIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("chapter")]
        public string ChapterTitle { get; set; } = string.Empty;

        public string id { get; set; } = string.Empty;

        public string TwinID { get; set; } = string.Empty;
        public int TotalTokensDocument { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string ChapterID { get; set; } = string.Empty;

        public string TextChapter { get; set; } = string.Empty;

        public int FromPageChapter { get; set; }
        public int ToPageChapter { get; set; } 

        // This field is not present in your JSON so it will default to 0.  
        // If you don't need it, you can remove it.  
        [System.Text.Json.Serialization.JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }

        /// <summary>
        /// Gets or sets the title of the subchapter.
        /// </summary>
        public string TitleSub { get; set; } = string.Empty;

        public string TextSub { get; set; } = string.Empty;
        public int TotalTokensSub { get; set; }
        public int FromPageSub { get; set; }
        public int ToPageSub { get; set; }
    }
}
