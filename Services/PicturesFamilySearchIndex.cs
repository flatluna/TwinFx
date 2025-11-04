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
using Newtonsoft.Json;
using TwinFx.Functions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Identity.Client;
using TwinAgentsLibrary.Models;

namespace TwinFx.Services
{
    /// <summary>
    /// Azure AI Search Service specifically designed for Pictures Family indexing and search
    /// ========================================================================
    /// 
    /// This service creates and manages a search index optimized for family pictures with:
    /// - Vector search capabilities using Azure OpenAI embeddings
    /// - Semantic search for natural language queries about family photos
    /// - Full-text search across picture descriptions and details
    /// - Family picture content analysis and indexing
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class PicturesFamilySearchIndex
    {
        private readonly ILogger<PicturesFamilySearchIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Configuration constants
        private const string PicturesFamilyIndexName = "pictures-family-index";
        private const string VectorSearchProfile = "pictures-family-vector-profile";
        private const string HnswAlgorithmConfig = "pictures-family-hnsw-config";
        private const string SemanticConfig = "pictures-family-semantic-config";
        private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions

        // Configuration keys
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;

        public PicturesFamilySearchIndex(ILogger<PicturesFamilySearchIndex> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Load Azure Search configuration
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
                    _logger.LogInformation("📸 Pictures Family Search Index client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure Search client for Pictures Family Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure Search credentials not found for Pictures Family Index");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("📸 Azure OpenAI embedding client initialized for Pictures Family Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for Pictures Family Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure OpenAI credentials not found for Pictures Family Index");
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
        /// Check if the pictures family search service is available
        /// </summary>
        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Create the pictures family search index with vector and semantic search capabilities
        /// </summary>
        public async Task<PicturesFamilyIndexResult> CreatePicturesFamilyIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new PicturesFamilyIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📸 Creating Pictures Family Search Index: {IndexName}", PicturesFamilyIndexName);

                // Definition of search fields based on PictureFamilyIndexContent schema
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Picture identification fields
                    new SearchableField("pictureId")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    new SearchableField("filename")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    new SearchableField("path")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    // Picture content fields
                    new SearchableField("descripcionGenerica")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Combined content field for vector search
                    new SearchableField("pictureContent")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                       // Combined content field for vector search
                    new SearchableField("pictureContentHTML")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Context from DetallesMemorables
                    new SearchableField("contextoRecordatorio")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // TwinID field for filtering by specific twin
                    new SearchableField("TwinID")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },
                    
                    // TotalTokens field for numeric filtering and sorting
                    new SimpleField("TotalTokens", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },
                    
                    // CreatedAt field for timestamp filtering
                    new SimpleField("CreatedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Fecha de la foto
                    new SearchableField("fecha")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Hora de la foto
                    new SearchableField("hora")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Categoría de la foto
                    new SearchableField("category")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Tipo de evento
                    new SearchableField("eventType")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Descripción del usuario
                    new SearchableField("descripcionUsuario")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Lugares en la foto
                    new SearchableField("places")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Personas en la foto
                    new SearchableField("people")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // ✅ NUEVOS CAMPOS AGREGADOS: Etiquetas de la foto
                    new SearchableField("etiquetas")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Vector field for semantic similarity search
                    new SearchField("textoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
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
                    TitleField = new SemanticField("filename")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("descripcionGenerica"));
                prioritizedFields.ContentFields.Add(new SemanticField("pictureContent"));
                prioritizedFields.ContentFields.Add(new SemanticField("contextoRecordatorio"));
                // ✅ NUEVOS CAMPOS: Agregar nuevos campos de contenido para búsqueda semántica
                prioritizedFields.ContentFields.Add(new SemanticField("descripcionUsuario"));
                prioritizedFields.ContentFields.Add(new SemanticField("people"));
                prioritizedFields.ContentFields.Add(new SemanticField("places"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("path"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("TwinID"));
                // ✅ NUEVOS CAMPOS: Agregar nuevos campos de palabras clave para búsqueda semántica
                prioritizedFields.KeywordsFields.Add(new SemanticField("category"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("eventType"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("etiquetas"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("fecha"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("hora"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

                // Create the pictures family search index
                var index = new SearchIndex(PicturesFamilyIndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ Pictures Family Index '{IndexName}' created successfully", PicturesFamilyIndexName);

                return new PicturesFamilyIndexResult
                {
                    Success = true,
                    Message = $"Pictures Family Index '{PicturesFamilyIndexName}' created successfully",
                    IndexName = PicturesFamilyIndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating Pictures Family Index");
                return new PicturesFamilyIndexResult
                {
                    Success = false,
                    Error = $"Error creating Pictures Family Index: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Index a picture analysis in Azure AI Search from ImageAI data
        /// </summary>
        public async Task<PicturesFamilyIndexResult> IndexPictureUploadDocumentAsync(ImageAI imageAI, double processingTimeMs = 0.0)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new PicturesFamilyIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📸 Indexing picture analysis for PictureID: {PictureId}, TwinID: {TwinId}", 
                    imageAI.id, imageAI.TwinID);

                // Create search client
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), PicturesFamilyIndexName, new AzureKeyCredential(_searchApiKey!));

                // Create PictureFamilyIndexContent from ImageAI
                var indexContent = CreatePictureFamilyIndexContent(imageAI);

                // Generate embeddings for vector search
                var embeddings = await GenerateEmbeddingsAsync(indexContent.PictureContent);

                // Generate unique document ID
                var documentId = await GenerateUniquePictureDocumentId(indexContent.PictureId, indexContent.TwinID);

                // Create search document
                var document = new Dictionary<string, object>
                {
                    ["id"] = documentId,
                    ["pictureId"] = indexContent.PictureId,
                    ["filename"] = indexContent.Filename, 
                    ["TotalTokens"] = indexContent.TotalTokens,
                    ["path"] = indexContent.Path,
                    ["descripcionGenerica"] = indexContent.DescripcionGenerica,
                    ["pictureContent"] = indexContent.PictureContent,
                    ["contextoRecordatorio"] = indexContent.ContextoRecordatorio,
                    ["TwinID"] = indexContent.TwinID,
                    ["CreatedAt"] = DateTimeOffset.UtcNow,
                    ["pictureContentHTML"] = indexContent.pictureContentHTML,
                    // ✅ NUEVOS CAMPOS AGREGADOS del ImageAI
                    ["fecha"] = imageAI.Fecha ?? string.Empty,
                    ["hora"] = imageAI.Hora ?? string.Empty,
                    ["category"] = imageAI.Category ?? string.Empty,
                    ["eventType"] = imageAI.EventType ?? string.Empty,
                    ["descripcionUsuario"] = imageAI.DescripcionUsuario ?? string.Empty,
                    ["places"] = imageAI.Places ?? string.Empty,
                    ["people"] = imageAI.People ?? string.Empty,
                    ["etiquetas"] = imageAI.Etiquetas ?? string.Empty
                };

                // Add vector embeddings if available
                if (embeddings != null)
                {
                    document["textoVector"] = embeddings;
                }

                // Upload document to search index
                var uploadResult = await searchClient.UploadDocumentsAsync(new[] { document });
                
                var firstResult = uploadResult.Value.Results.FirstOrDefault();
                bool indexSuccess = firstResult != null && firstResult.Succeeded;

                if (indexSuccess)
                {
                    _logger.LogInformation("✅ Picture analysis indexed successfully: DocumentId={DocumentId}", documentId);
                    return new PicturesFamilyIndexResult
                    {
                        Success = true,
                        Message = "Picture analysis indexed successfully",
                        IndexName = PicturesFamilyIndexName,
                        DocumentId = documentId,
                        HasVectorSearch = embeddings != null,
                        HasSemanticSearch = true
                    };
                }
                else
                {
                    var errorMessage = firstResult?.ErrorMessage ?? "Unknown indexing error";
                    _logger.LogError("❌ Failed to index picture analysis: {Error}", errorMessage);
                    return new PicturesFamilyIndexResult
                    {
                        Success = false,
                        Error = $"Failed to index picture analysis: {errorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing picture analysis");
                return new PicturesFamilyIndexResult
                {
                    Success = false,
                    Error = $"Error indexing picture analysis: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Create PictureFamilyIndexContent from ImageAI data
        /// </summary>
        private PictureFamilyIndexContent CreatePictureFamilyIndexContent(ImageAI imageAI)
        {
            var indexContent = new PictureFamilyIndexContent
            {
                PictureId = imageAI.id ?? Guid.NewGuid().ToString(),
                Filename = imageAI.FileName ?? "",
                Path = imageAI.Path ?? "",
                DescripcionGenerica = imageAI.DescripcionDetallada?? "",
                TwinID = imageAI.TwinID ?? "",
                TotalTokens = imageAI.TotalTokensDescripcionDetallada, 
                DetallesMemorables = imageAI.DetallesMemorables,
                pictureContentHTML = imageAI.DetailsHTML
            };

            // Build PictureContent combining DescripcionGenerica and ContextoQuePuedeAyudarARecordarElMomento
            var pictureContentBuilder = new StringBuilder();
            
            if (!string.IsNullOrEmpty(indexContent.DescripcionGenerica))
            {
                pictureContentBuilder.AppendLine(indexContent.DescripcionGenerica);
            }
            
            if (indexContent.DetallesMemorables?.ContextoQuePuedeAyudarARecordarElMomento != null)
            {
                pictureContentBuilder.AppendLine(indexContent.DetallesMemorables.ContextoQuePuedeAyudarARecordarElMomento);
            }

            indexContent.PictureContent = pictureContentBuilder.ToString().Trim();

            return indexContent;
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
                    _logger.LogInformation("📸 Text truncated to 8000 characters for embedding generation");
                }

                _logger.LogDebug("📸 Generating embeddings for text: {Length} characters", text.Length);

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
        /// Generate unique document ID for picture analysis
        /// </summary>
        private async Task<string> GenerateUniquePictureDocumentId(string pictureId, string twinId)
        {
            var baseId = $"picture-{pictureId}-{twinId}";
            var documentId = baseId;
            var counter = 1;

            // Check if document exists and increment counter if needed
            while (await PictureDocumentExistsAsync(documentId))
            {
                documentId = $"{baseId}-{counter}";
                counter++;
            }

            return documentId;
        }

        /// <summary>
        /// Check if a picture document exists in the search index
        /// </summary>
        private async Task<bool> PictureDocumentExistsAsync(string documentId)
        {
            try
            {
                if (!IsAvailable) return false;

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), PicturesFamilyIndexName, new AzureKeyCredential(_searchApiKey!));
                
                var response = await searchClient.GetDocumentAsync<Dictionary<string, object>>(documentId);
                return response != null;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error checking picture document existence for {DocumentId}", documentId);
                return false;
            }
        }





        /////////////////
        /// NEw Version
        /// SearchFamilyPicturesAsync



        






        ////////////////////////////////////

        /// <summary>
        /// Búsqueda semántica híbrida de fotos familiares usando embeddings y búsqueda textual
        /// </summary>
        /// <param name="query">Consulta de búsqueda con opciones configurables</param>
        /// <returns>Resultado de búsqueda con fotos familiares encontradas</returns>
        public async Task<FotosAiSearchResponse> SearchFamilyPicturesFormerAsync(PicturesFamilySearchQuery query)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new FotosAiSearchResponse
                    {
                         HtmlResponse = "<p>Azure Search service not available</p>",
                    };
                }

                _logger.LogInformation("📸 Searching family pictures: '{Query}' for Twin: {TwinId}",
                    query.SearchText?.Substring(0, Math.Min(query.SearchText?.Length ?? 0, 50)), query.TwinId);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), PicturesFamilyIndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = query.Top,
                    Skip = Math.Max(0, (query.Page - 1) * query.Top),
                    IncludeTotalCount = true
                };

                // Campos que queremos recuperar de fotos familiares
                var fieldsToSelect = new[]
                {
                    "id", "pictureId", "filename", "path", "descripcionGenerica", "pictureContent",
                    "contextoRecordatorio", "TwinID", "TotalTokens", "CreatedAt",
                    // ✅ NUEVOS CAMPOS AGREGADOS para búsqueda
                    "fecha", "hora", "category", "eventType", "descripcionUsuario", "places", "people", "etiquetas"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Filtros sencillos
                var filterParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(query.TwinId))
                    filterParts.Add($"TwinID eq '{query.TwinId.Replace("'", "''")}'");
                if (query.MinTokens.HasValue)
                    filterParts.Add($"TotalTokens ge {query.MinTokens.Value}");
                if (query.MaxTokens.HasValue)
                    filterParts.Add($"TotalTokens le {query.MaxTokens.Value}");
                if (filterParts.Any())
                    searchOptions.Filter = string.Join(" and ", filterParts);

                string searchText = query.SearchText ?? string.Empty;

                // Configurar búsqueda semántica/híbrida
                if (query.UseSemanticSearch || query.UseHybridSearch)
                {
                    searchOptions.QueryType = SearchQueryType.Semantic;
                    searchOptions.SemanticSearch = new SemanticSearchOptions
                    {
                        SemanticConfigurationName = SemanticConfig
                    };
                }
                else
                {
                    searchOptions.QueryType = SearchQueryType.Simple;
                }

                // MEJORADO: Búsqueda vectorial inteligente basada en contenido
                bool shouldUseVectorSearch = ShouldUseVectorSearch(query.SearchText);
                query.UseVectorSearch = true;
                if (shouldUseVectorSearch && query.UseVectorSearch)
                {
                    // Si ya tienes el embedding en query.Vector, úsalo; si no, genera uno
                    float[]? embedding = query.Vector;
                    if (embedding == null || embedding.Length == 0)
                    {
                        if (!string.IsNullOrWhiteSpace(query.SearchText))
                        {
                            embedding = await GenerateEmbeddingsAsync(query.SearchText);
                        }
                        else
                        {
                            throw new InvalidOperationException("SearchText cannot be null or empty when generating embeddings.");
                        }
                    }

                    if (embedding != null && embedding.Length > 0)
                    {
                        // Crear la consulta vectorial
                        var vectorQuery = new VectorizedQuery(embedding)
                        {
                            KNearestNeighborsCount = Math.Max(1, query.Top * 2) // Top*2 para candidatos a re-rank
                        };
                        vectorQuery.Fields.Add("textoVector"); // nombre del campo vector en el índice

                        searchOptions.VectorSearch = new VectorSearchOptions();
                        searchOptions.VectorSearch.Queries.Add(vectorQuery);

                        // Si no queremos combinar con texto (solo vector), usar '*'
                        if (!query.UseHybridSearch && string.IsNullOrWhiteSpace(searchText))
                        {
                            searchText = "*";
                        }
                        // Si es híbrido, dejamos searchText para combinar señales semánticas/textuales
                    }
                    else
                    {
                        throw new InvalidOperationException("No se pudo generar el embedding para la búsqueda vectorial.");
                    }
                }
                else
                {
                    // Usar solo búsqueda textual para nombres específicos y búsquedas exactas
                    _logger.LogInformation("🔍 Using text-only search for specific name search: {SearchText}", searchText);
                }

                // Ejecutar la búsqueda
                var response = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);

                var result = new PicturesFamilySearchResult
                {
                    Success = true,
                    TotalCount = response.Value.TotalCount ?? 0,
                    Page = query.Page,
                    PageSize = query.Top,
                    SearchType = GetSearchType(query)
                };

                // Extraer respuestas semánticas si existen
                if (response.Value.SemanticSearch?.Answers != null)
                {
                    result.SearchQuery = string.Join(" ", response.Value.SemanticSearch.Answers
                        .Select(a => ExtractAnswerText(a))
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                }
                else
                {
                    result.SearchQuery = query.SearchText ?? string.Empty;
                }

                var pictures = new List<PicturesFamilySearchResultItem>();

                await foreach (var r in response.Value.GetResultsAsync())
                {
                    var doc = r.Document;
                    var item = new PicturesFamilySearchResultItem
                    {
                        Id = doc.GetString("id") ?? string.Empty,
                        PictureId = doc.GetString("pictureId") ?? string.Empty,
                        Filename = doc.GetString("filename") ?? string.Empty,
                        Path = doc.GetString("path") ?? string.Empty,
                        DescripcionGenerica = doc.GetString("descripcionGenerica") ?? string.Empty,
                        PictureContent = doc.GetString("pictureContent") ?? string.Empty,
                        ContextoRecordatorio = doc.GetString("contextoRecordatorio") ?? string.Empty,
                        TwinID = doc.GetString("TwinID") ?? string.Empty,
                        TotalTokens = doc.GetInt32("TotalTokens") ?? 0,
                        CreatedAt = doc.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
                        SearchScore = r.Score ?? 0.0
                    };

                    // Captions (semantic) -> Highlights
                    if (r.SemanticSearch?.Captions?.Any() == true)
                    {
                        var captionsObjects = r.SemanticSearch.Captions.Cast<object>();
                        item.Highlights = captionsObjects
                            .Select(ExtractCaptionText)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }

                    pictures.Add(item);
                }
                 
                var AiResponse =  await FotosAiSearchJsonAsync(pictures, query); 

                _logger.LogInformation("✅ Family pictures search completed: {Count} results found", pictures.Count);
                return AiResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching family pictures");
                return new FotosAiSearchResponse
                {
                    HtmlResponse = $"Error searching family pictures: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Método para procesar y enriquecer los resultados de búsqueda usando AI
        /// </summary>
        /// <param name="pictures">Lista de resultados de búsqueda de fotos familiares</param>
        /// <param name="query">Consulta de búsqueda original</param>
        /// <returns>Respuesta completa deserializada del análisis AI</returns>
        public async Task<FotosAiSearchCompleteResponse> FotosAiSearchAsync(List<PicturesFamilySearchResultItem> pictures, PicturesFamilySearchQuery query)
        {
            try
            {
                _logger.LogInformation("🤖 FotosAiSearch: Processing {Count} pictures with AI for query: {SearchText}", 
                    pictures.Count, query.SearchText);

                // Inicializar Semantic Kernel si no está inicializado
                await InitializeSemanticKernelAsync();

                if (_kernel == null)
                {
                    _logger.LogWarning("⚠️ Semantic Kernel not available, returning basic response");
                    return GenerateBasicFotosAiResponse(pictures, query);
                }

                // Construir el contenido combinado para la consulta AI
                var combinedContent = BuildCombinedContentForAI(pictures);

                var aiPrompt = $$$"""
🤖 **Asistente Experto en Análisis de Fotos Familiares**

Eres un analista especializado en colecciones de fotos familiares. El usuario ha realizado una búsqueda semántica y necesitas proporcionar una respuesta en formato JSON estructurado.
Importante en la respuesta HTML no pongas el url de la foto solo en el json en picturesFound.
⚠️ **INSTRUCCIÓN CRÍTICA DE FILTRADO:**
SOLO incluye en "picturesFound" aquellas fotos que REALMENTE respondan y coincidan con la búsqueda del usuario. 
- Si el usuario busca "fotos con sol" pero ninguna foto contiene sol o luz solar, NO incluyas ninguna foto.
- Si busca "personas en la playa" pero no hay fotos de playa, NO incluyas fotos solo porque tengan personas.
- Si busca "niños jugando" pero las fotos son de adultos o no muestran juegos, NO las incluyas.
- Si NO encuentras fotos que coincidan con la búsqueda, deja el array "picturesFound" VACÍO: []
- ANALIZA CUIDADOSAMENTE el contenido de cada foto antes de incluirla.
- NO incluyas fotos simplemente porque están en la lista recibida.

**PREGUNTA DEL USUARIO:** "{{{query.SearchText}}}"

**DATOS DE FOTOS ENCONTRADAS:**
{{{combinedContent}}}

***** pon el URL tambien en picturesFound"" del json

**FORMATO DE RESPUESTA REQUERIDO (JSON):** Nunca respondas en otro formato que no sea JSON. Nunca comienzes con ```json
El JSON debe tener la siguiente estructura:
 
{
  "htmlResponse": "<div style='font-family: Segoe UI, Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px;'>HTML COMPLETO CON ANÁLISIS VISUAL</div>",
  "picturesFound": [
    {
      "pictureId": "ID de la foto",
      "filename": "nombre del archivo",
      "path": "ruta de almacenamiento",
      "descripcionGenerica": "descripción de la foto",
      "pictureContent": "contenido completo",
      "contextoRecordatorio": "contexto memorable",
      "totalTokens": 123,
      "url":"",
      "searchScore": 0.95,
      "createdAt": "2025-01-01",
      "highlights": ["texto destacado 1", "texto destacado 2"]
    }
  ],
  "searchSummary": {
    "totalFound": 5,
    "searchQuery": "consulta original",
    "searchType": "Hybrid",
    "analysisInsights": [
      "Insight 1 sobre los patrones encontrados",
      "Insight 2 sobre el contenido",
      "Insight 3 sobre fechas o contextos"
    ],
    "recommendations": [
      "Recomendación 1 para el usuario",
      "Recomendación 2 basada en los resultados"
    ]
  }
}

**INSTRUCCIONES ESPECÍFICAS:**

1. **FILTRADO INTELIGENTE DE FOTOS:**
   - Lee DETENIDAMENTE la pregunta del usuario y el contenido de cada foto
   - SOLO incluye fotos que contengan elementos específicos mencionados en la búsqueda
   - Si la búsqueda es sobre "perros" NO incluyas fotos de gatos
   - Si buscan "cumpleaños" NO incluyas fotos normales de familia sin celebración
   - Si buscan "navidad" NO incluyas fotos que no tengan elementos navideños
   - PREFIERE un array vacío [] antes que incluir fotos irrelevantes

2. **HTML Response (htmlResponse):**
   - Si NO encuentras fotos relevantes, explica claramente por qué no hay resultados
   - Si encuentras fotos, genera HTML completo y profesional con estilos CSS inline
   - Usa colores cálidos y acogedores para fotos familiares
   - Incluye análisis inteligente de la consulta del usuario
   - Estructura: título, análisis de búsqueda, galería de fotos, resumen
   - Emojis relevantes: 📸 👨‍👩‍👧‍👦 🏠 🎉 ❤️ 🌟
   - Diseño responsive con CSS Grid/Flexbox

3. **Pictures Found (picturesFound):**
   - Array SOLO con fotos que coincidan exactamente con la búsqueda
   - Si no hay coincidencias, usar array vacío: []
   - Incluir todos los campos disponibles de cada foto RELEVANTE
   - Mantener los datos originales sin modificación

4. **Search Summary (searchSummary):**
   - Estadísticas PRECISAS de la búsqueda (solo fotos relevantes)
   - Insights inteligentes sobre patrones encontrados (o explicar por qué no hay resultados)
   - Recomendaciones prácticas para el usuario

5. **Análisis Inteligente:** 
- Respone la pregunta del usuario con análisis profundo
- Si no hay resultados, explica claramente por qué

 
**EJEMPLO DE HTML STRUCTURE CUANDO NO HAY RESULTADOS:**
```htmlsol indicva que no se encontro ninguna foto con esa pregunta
 
```
incluye Descripcion de la foto completa  con tu explciuacion de que observas en la foto? Responde la pregunat claramente no incluyas el URL de la foto 
si hay barias fotos solo da un comentario suamrio de todas las fotos lo que encontraste pero incluyelas 
en la lista todas aquellas fotos que satisfagan la pregunta de usuario humano. 
**EJEMPLO CUANDO SÍ HAY RESULTADOS:**
```html
<div style='font-family: Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px;'>  
  <h2 style='color: #2c3e50; border-bottom: 2px solid #e74c3e; padding-bottom: 8px; font-size: 24px;'>📸 Resultados: Encuentros Familiares de Daniel</h2>  
  <p style='color: #666; font-size: 14px;'>Se han encontrado un una, dos, tres o x fotos con Luis   </p>  

  <div style='margin: 20px 0;'>  
    <p style='margin: 10px 0; font-weight: bold; color: #2c3e50; font-size: 16px;'>Encuentro Familiar - Issaquah, WA</p>  
    <p style='margin: 0; font-size: 12px; color: #666;'> {Descripcion de la foto completa }</p>  
  </div>  
</div>  ```
Manten tu repuesta simple y profesional. Anade lo que encontraste en la o las fotos. Lista las fotos que se dan la respuesta
pero no icluyas fotos que no tienen nada que ver con la pregunta. Si no hay fotos que coincidan, explica por qué y sugiere nuevas búsquedas.
SIEMPRE incluye las imágenes cuando tengas URL SAS disponible.
""";

                var chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(aiPrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 8000, // Incrementado para respuesta JSON más completa
                        ["temperature"] = 0.3  // Temperatura más baja para JSON más preciso
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var jsonResponse = response.Content ?? "{}";
                
                // Limpiar el JSON response de posibles markdown
                var cleanResponse = jsonResponse.Trim();
                if (cleanResponse.StartsWith("```json"))
                {
                    cleanResponse = cleanResponse.Substring(7);
                }
                if (cleanResponse.StartsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(3);
                }
                if (cleanResponse.EndsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                }
                cleanResponse = cleanResponse.Trim();

                _logger.LogInformation("✅ AI JSON response generated successfully with {Length} characters", cleanResponse.Length);
                
                // Deserializar la respuesta JSON completa del AI
                try
                {
                    var aiCompleteResponse = JsonConvert.DeserializeObject<FotosAiSearchCompleteResponse>(cleanResponse);
                    
                    if (aiCompleteResponse != null)
                    {
                        _logger.LogInformation("✅ Successfully deserialized AI response to FotosAiSearchCompleteResponse");
                        return aiCompleteResponse;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ AI response deserialized to null, creating fallback response");
                        return GenerateFallbackFotosAiResponse(pictures, query, "Respuesta AI deserializada a null");
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogWarning(jsonEx, "⚠️ Error deserializing AI JSON response, creating fallback response");
                    return GenerateFallbackFotosAiResponse(pictures, query, $"Error JSON: {jsonEx.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en FotosAiSearchAsync");
                return GenerateErrorFotosAiResponse(ex.Message, query);
            }
        }

        /// <summary>
        /// Inicializar Semantic Kernel para operaciones de AI
        /// </summary>
        private Kernel? _kernel;

        private async Task InitializeSemanticKernelAsync()
        {
            if (_kernel != null)
                return; // Ya está inicializado

            try
            {
                _logger.LogInformation("🔧 Initializing Semantic Kernel for FotosAiSearch");

                // Crear kernel builder
                IKernelBuilder builder = Kernel.CreateBuilder();

                // Obtener configuración de Azure OpenAI
                var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ??
                              _configuration.GetValue<string>("AzureOpenAI:Endpoint") ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

                var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ??
                            _configuration.GetValue<string>("AzureOpenAI:ApiKey") ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

                var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ??
                                    _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ??
                                    "gpt4mini";

                _logger.LogInformation("🔍 Using deployment: {DeploymentName} for FotosAiSearch", deploymentName);

                // Agregar Azure OpenAI chat completion
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: deploymentName,
                    endpoint: endpoint,
                    apiKey: apiKey);

                // Construir el kernel
                _kernel = builder.Build();

                _logger.LogInformation("✅ Semantic Kernel initialized successfully for FotosAiSearch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for FotosAiSearch");
                _kernel = null; // Asegurar que quede como null si falla
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Construir contenido combinado para enviar al AI
        /// </summary>
        private string BuildCombinedContentForAI(List<PicturesFamilySearchResultItem> pictures)
        {
            if (!pictures.Any())
                return "No se encontraron fotos para analizar.";

            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine($"📊 TOTAL DE FOTOS ENCONTRADAS: {pictures.Count}");
            contentBuilder.AppendLine();

            foreach (var picture in pictures.Take(10)) // Limitar a 10 fotos para no exceder tokens
            {
                contentBuilder.AppendLine($"📸 FOTO {pictures.IndexOf(picture) + 1}:");
                contentBuilder.AppendLine($"   • ID: {picture.PictureId}");
                contentBuilder.AppendLine($"   • Archivo: {picture.Filename}");
                contentBuilder.AppendLine($"   • Ruta: {picture.Path}");
                contentBuilder.AppendLine($"   • Descripción: {picture.DescripcionGenerica}");
                
                if (!string.IsNullOrEmpty(picture.PictureContent))
                {
                    contentBuilder.AppendLine($"   • Contenido: {picture.PictureContent}");
                }
                
                if (!string.IsNullOrEmpty(picture.ContextoRecordatorio))
                {
                    contentBuilder.AppendLine($"   • Contexto: {picture.ContextoRecordatorio}");
                }

                contentBuilder.AppendLine($"   • Tokens: {picture.TotalTokens}");
                contentBuilder.AppendLine($"   • Score: {picture.SearchScore:F2}");
                contentBuilder.AppendLine($"   • Fecha: {picture.CreatedAt:yyyy-MM-dd}");
                
                if (picture.Highlights.Any())
                {
                    contentBuilder.AppendLine($"   • Highlights: {string.Join(", ", picture.Highlights)}");
                }

                // NUEVO: Agregar URL SAS para mostrar la imagen
                if (!string.IsNullOrEmpty(picture.PictureURL))
                {
                    contentBuilder.AppendLine($"   • URL SAS: {picture.PictureURL}");
                    contentBuilder.AppendLine($"   • ✅ IMAGEN DISPONIBLE PARA MOSTRAR");
                }
                else
                {
                    contentBuilder.AppendLine($"   • ❌ Sin URL SAS disponible");
                }
                
                contentBuilder.AppendLine();
            }

            if (pictures.Count > 10)
            {
                contentBuilder.AppendLine($"... y {pictures.Count - 10} fotos adicionales encontradas.");
            }

            return contentBuilder.ToString();
        }

        /// <summary>
        /// Generar respuesta básica de fallback cuando Semantic Kernel no está disponible
        /// </summary>
        private FotosAiSearchCompleteResponse GenerateBasicFotosAiResponse(List<PicturesFamilySearchResultItem> pictures, PicturesFamilySearchQuery query)
        {
            var basicHtml = $"""
<div style="font-family: 'Segoe UI', Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px;">
    <h2 style="color: #2c3e50; border-bottom: 3px solid #e74c3c; padding-bottom: 10px;">
        📸 Resultados de Búsqueda: {query.SearchText}
    </h2>
    
    <div style="background: #f8f9fa; padding: 15px; border-radius: 8px; margin: 20px 0;">
        <p style="margin: 0; color: #495057;">
            Se encontraron <strong>{pictures.Count}</strong> fotos familiares para tu búsqueda.
        </p>
    </div>
    
    <div style="display: grid; gap: 15px;">
        {string.Join("", pictures.Take(5).Select((p, i) => $"""
        <div style="background: white; border: 1px solid #dee2e6; border-radius: 8px; padding: 15px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);">
            <h4 style="color: #495057; margin: 0 0 10px 0;">{p.Filename}</h4>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Descripción:</strong> {p.DescripcionGenerica}</p>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Score:</strong> {p.SearchScore:F2}</p>
        </div>
        """))}
    </div>
</div>
""";

            return new FotosAiSearchCompleteResponse
            {
                HtmlResponse = basicHtml,
                PicturesFound = pictures.Select(p => new PictureFoundAiResponse
                {
                    PictureId = p.PictureId,
                    Filename = p.Filename,
                    Path = p.Path,
                    DescripcionGenerica = p.DescripcionGenerica,
                    PictureContent = p.PictureContent,
                    ContextoRecordatorio = p.ContextoRecordatorio,
                    TotalTokens = p.TotalTokens,
                    SearchScore = p.SearchScore,
                    CreatedAt = p.CreatedAt.ToString("yyyy-MM-dd"),
                    Highlights = p.Highlights
                }).ToList(),
                SearchSummary = new SearchSummaryAiResponse
                {
                    TotalFound = pictures.Count,
                    SearchQuery = query.SearchText ?? "",
                    SearchType = GetSearchType(query),
                    AnalysisInsights = new List<string> { "Análisis básico de fotos familiares", "Resultados ordenados por relevancia" },
                    Recommendations = new List<string> { "Refinar búsqueda para mejores resultados", "Explorar fotos relacionadas" }
                }
            };
        }

        /// <summary>
        /// Generar respuesta de fallback cuando ocurre un error en parsing
        /// </summary>
        private FotosAiSearchCompleteResponse GenerateFallbackFotosAiResponse(List<PicturesFamilySearchResultItem> pictures, PicturesFamilySearchQuery query, string errorMessage)
        {
            var fallbackHtml = $"""
<div style="font-family: 'Segoe UI', Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px;">
    <h2 style="color: #2c3e50; border-bottom: 3px solid #f39c12; padding-bottom: 10px;">
        📸 Resultados de Búsqueda (Modo Básico)
    </h2>
    
    <div style="background: linear-gradient(135deg, #f39c12 0%, #e67e22 100%); color: white; padding: 15px; border-radius: 8px; margin: 20px 0;">
        <p style="margin: 0; opacity: 0.9;">⚠️ Análisis AI no disponible: {errorMessage}</p>
        <p style="margin: 5px 0 0 0; font-size: 14px; opacity: 0.8;">Mostrando resultados básicos para tu búsqueda: "{query.SearchText}"</p>
    </div>
    
    <div style="background: #f8f9fa; padding: 15px; border-radius: 8px; margin: 20px 0;">
        <p style="margin: 0; color: #495057;">
            Se encontraron <strong>{pictures.Count}</strong> fotos familiares.
        </p>
    </div>
    
    <div style="display: grid; gap: 15px;">
        {string.Join("", pictures.Take(8).Select((p, i) => $"""
        <div style="background: white, border: 1px solid #dee2e6; border-radius: 8px; padding: 15px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);">
            <h4 style="color: #495057; margin: 0 0 10px 0;">📷 {p.Filename}</h4>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Descripción:</strong> {p.DescripcionGenerica}</p>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Contexto:</strong> {p.ContextoRecordatorio}</p>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Score:</strong> {p.SearchScore:F2}</p>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Tokens:</strong> {p.TotalTokens}</p>
        </div>
        """))}
    </div>
</div>
""";

            return new FotosAiSearchCompleteResponse
            {
                HtmlResponse = fallbackHtml,
                PicturesFound = pictures.Select(p => new PictureFoundAiResponse
                {
                    PictureId = p.PictureId,
                    Filename = p.Filename,
                    Path = p.Path,
                    DescripcionGenerica = p.DescripcionGenerica,
                    PictureContent = p.PictureContent,
                    ContextoRecordatorio = p.ContextoRecordatorio,
                    TotalTokens = p.TotalTokens,
                    SearchScore = p.SearchScore,
                    CreatedAt = p.CreatedAt.ToString("yyyy-MM-dd"),
                    Highlights = p.Highlights
                }).ToList(),
                SearchSummary = new SearchSummaryAiResponse
                {
                    TotalFound = pictures.Count,
                    SearchQuery = query.SearchText ?? "",
                    SearchType = GetSearchType(query),
                    AnalysisInsights = new List<string> { $"Error en análisis AI: {errorMessage}", "Resultados mostrados sin análisis inteligente" },
                    Recommendations = new List<string> { "Intentar nuevamente la búsqueda", "Verificar configuración del servicio AI" }
                }
            };
        }

        /// <summary>
        /// Generar respuesta de error
        /// </summary>
        private FotosAiSearchCompleteResponse GenerateErrorFotosAiResponse(string errorMessage, PicturesFamilySearchQuery query)
        {
            var errorHtml = $"""
<div style="font-family: Arial, sans-serif; max-width: 800px; margin: 20px auto; padding: 20px;">
    <div style="background: linear-gradient(135deg, #ff6b6b 0%, #ee5a52 100%); color: white; padding: 20px; border-radius: 10px;">
        <h3 style="margin: 0 0 10px 0;">❌ Error en Análisis de Fotos</h3>
        <p style="margin: 0; opacity: 0.9;">No se pudo procesar tu búsqueda: "{query.SearchText}"</p>
        <p style="margin: 10px 0 0 0; font-size: 14px; opacity: 0.8;">Error: {errorMessage}</p>
    </div>
</div>
""";

            return new FotosAiSearchCompleteResponse
            {
                HtmlResponse = errorHtml,
                PicturesFound = new List<PictureFoundAiResponse>(),
                SearchSummary = new SearchSummaryAiResponse
                {
                    TotalFound = 0,
                    SearchQuery = query.SearchText ?? "",
                    SearchType = "Error",
                    AnalysisInsights = new List<string> { $"Error crítico: {errorMessage}" },
                    Recommendations = new List<string> { "Intentar con una consulta diferente", "Contactar soporte técnico" }
                }
            };
        }

        /// <summary>
        /// Método para procesar y enriquecer los resultados de búsqueda usando AI (versión que retorna JSON completo)
        /// </summary>
        /// <param name="pictures">Lista de resultados de búsqueda de fotos familiares</param>
        /// <param name="query">Consulta de búsqueda original</param>
        /// <returns>Respuesta JSON estructurada completa del análisis AI</returns>
        public async Task<FotosAiSearchResponse> FotosAiSearchJsonAsync(List<PicturesFamilySearchResultItem> pictures, PicturesFamilySearchQuery query)
        {
            try
            {
                _logger.LogInformation("🤖 FotosAiSearchJson: Processing {Count} pictures with AI for query: {SearchText}", 
                    pictures.Count, query.SearchText);

                // NUEVO: Generar SAS URLs para cada foto antes de enviar al AI
                await GenerateSasUrlsForPicturesAsync(pictures, query.TwinId);

                // Llamar al método principal que ahora retorna FotosAiSearchCompleteResponse
                var aiCompleteResponse = await FotosAiSearchAsync(pictures, query);

                // Convertir FotosAiSearchCompleteResponse a FotosAiSearchResponse (para compatibilidad)
                return new FotosAiSearchResponse
                {
                    HtmlResponse = aiCompleteResponse.HtmlResponse,
                    PicturesFound = aiCompleteResponse.PicturesFound.Select(p => new PictureFoundResponse
                    {
                        PictureId = p.PictureId,
                        Filename = p.Filename,
                        Path = p.Path,
                        DescripcionGenerica = p.DescripcionGenerica,
                        PictureContent = p.PictureContent,
                        ContextoRecordatorio = p.ContextoRecordatorio,
                        TotalTokens = p.TotalTokens,
                        SearchScore = p.SearchScore,
                        CreatedAt = p.CreatedAt,
                        URL = p.Url,
                        Highlights = p.Highlights
                    }).ToList(),
                    SearchSummary = new SearchSummaryResponse
                    {
                        TotalFound = aiCompleteResponse.SearchSummary.TotalFound,
                        SearchQuery = aiCompleteResponse.SearchSummary.SearchQuery,
                        SearchType = aiCompleteResponse.SearchSummary.SearchType,
                        AnalysisInsights = aiCompleteResponse.SearchSummary.AnalysisInsights,
                        Recommendations = aiCompleteResponse.SearchSummary.Recommendations
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in FotosAiSearchJsonAsync");
                
                // Respuesta de error estructurada
                return new FotosAiSearchResponse
                {
                    HtmlResponse = GenerateErrorHtmlResponse(ex.Message, query),
                    PicturesFound = new List<PictureFoundResponse>(),
                    SearchSummary = new SearchSummaryResponse
                    {
                        TotalFound = 0,
                        SearchQuery = query.SearchText ?? "",
                        SearchType = "Error",
                        AnalysisInsights = new List<string> { $"Error en análisis: {ex.Message}" },
                        Recommendations = new List<string> { "Intentar con una consulta diferente", "Verificar configuración del servicio" }
                    }
                };
            }
        }

        /// <summary>
        /// Generar respuesta HTML de error (mantener para compatibilidad con otros métodos)
        /// </summary>
        private string GenerateErrorHtmlResponse(string errorMessage, PicturesFamilySearchQuery query)
        {
            return $"""
<div style="font-family: Arial, sans-serif; max-width: 800px; margin: 20px auto; padding: 20px;">
    <div style="background: linear-gradient(135deg, #ff6b6b 0%, #ee5a52 100%); color: white; padding: 20px; border-radius: 10px;">
        <h3 style="margin: 0 0 10px 0;">❌ Error en Análisis de Fotos</h3>
        <p style="margin: 0; opacity: 0.9;">No se pudo procesar tu búsqueda: "{query.SearchText}"</p>
        <p style="margin: 10px 0 0 0; font-size: 14px; opacity: 0.8;">Error: {errorMessage}</p>
    </div>
</div>
""";
        }

        /// <summary>
        /// Método helper para procesar y enriquecer los resultados de búsqueda
        /// </summary>
        private async Task<string> ProcessSearchResultsAsync(List<PicturesFamilySearchResultItem> results, PicturesFamilySearchQuery query)
        {
            string ResponseHTML = "";
            try
            {
                _logger.LogInformation("🔄 Processing {Count} search results with AI enhancement", results.Count);

                // Llamar al método FotosAiSearchAsync para generar análisis AI (ahora retorna objeto completo)
                var aiResponse = await FotosAiSearchAsync(results, query);
                
                // Extraer solo el HTML para mantener compatibilidad con el código existente
                ResponseHTML = aiResponse.HtmlResponse;

                // Aquí podrías almacenar el análisis AI completo en algun lugar si es necesario
                // Por ejemplo, en una propiedad adicional de los resultados
                
                _logger.LogInformation("✅ Search results processed successfully with AI analysis");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to process search results with AI, continuing without enhancement");
                
                // Generar HTML básico de fallback
                ResponseHTML = $"""
<div style="font-family: 'Segoe UI', Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px;">
    <h2 style="color: #2c3e50; border-bottom: 3px solid #e74c3c; padding-bottom: 10px;">
        📸 Resultados de Búsqueda: {query.SearchText}
    </h2>
    
    <div style="background: #f8f9fa; padding: 15px; border-radius: 8px; margin: 20px 0;">
        <p style="margin: 0; color: #495057;">
            Se encontraron <strong>{results.Count}</strong> fotos familiares para tu búsqueda.
        </p>
    </div>
    
    <div style="display: grid; gap: 15px;">
        {string.Join("", results.Take(5).Select((p, i) => $"""
        <div style="background: white; border: 1px solid #dee2e6; border-radius: 8px; padding: 15px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);">
            <h4 style="color: #495057; margin: 0 0 10px 0;">{p.Filename}</h4>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Descripción:</strong> {p.DescripcionGenerica}</p>
            <p style="margin: 5px 0; color: #6c757d;"><strong>Score:</strong> {p.SearchScore:F2}</p>
        </div>
        """))}
    </div>
</div>
""";
            }

            return ResponseHTML;
        }

        /// <summary>
        /// Determinar el tipo de búsqueda utilizada
        /// </summary>
        private string GetSearchType(PicturesFamilySearchQuery query)
        {
            var types = new List<string>();
            if (query.UseVectorSearch) types.Add("Vector");
            if (query.UseSemanticSearch) types.Add("Semantic");
            if (query.UseHybridSearch) types.Add("Hybrid");
            
            return types.Count > 0 ? string.Join("+", types) : "Simple";
        }

        /// <summary>
        /// Extraer texto de un Caption (robusto)
        /// </summary>
        private static string ExtractCaptionText(object caption)
        {
            if (caption == null) return string.Empty;
            var t = caption.GetType();

            var propText = t.GetProperty("Text");
            if (propText != null)
            {
                var v = propText.GetValue(caption) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            var propHighlights = t.GetProperty("Highlights");
            if (propHighlights != null)
            {
                var v = propHighlights.GetValue(caption);
                if (v is IEnumerable<string> seq) return string.Join(" ", seq);
                if (v != null) return v.ToString() ?? string.Empty;
            }

            return caption.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Extraer texto de una Answer (robusto)
        /// </summary>
        private static string ExtractAnswerText(object answer)
        {
            if (answer == null) return string.Empty;
            var t = answer.GetType();

            var propHighlights = t.GetProperty("Highlights");
            if (propHighlights != null)
            {
                var v = propHighlights.GetValue(answer);
                if (v is IEnumerable<string> seq) return string.Join(" ", seq);
                if (v != null) return v.ToString() ?? string.Empty;
            }

            var propText = t.GetProperty("Text");
            if (propText != null)
            {
                var v = propText.GetValue(answer) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            return answer.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Determinar si se debe usar búsqueda vectorial basado en el contenido de la consulta
        /// </summary>
        private bool ShouldUseVectorSearch(string? searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return false;

            var searchLower = searchText.ToLowerInvariant();

            // NO usar búsqueda vectorial para nombres específicos exactos
            var specificNames = new[] 
            { 
                "jorge", "maría", "maria", "pedro", "juan", "angeles", "daniel", "daniuel",
                "carlos", "luis", "ana", "sofia", "lucia", "fernando", "miguel", "antonio"
            };

            // Si la búsqueda es principalmente un nombre específico, usar búsqueda textual
            if (specificNames.Any(name => 
                searchLower.Contains($"encuentra a {name}") || 
                searchLower.Contains($"busca a {name}") ||
                searchLower.Contains($"fotos de {name}") ||
                searchLower.Contains($"buscar {name}") ||
                searchLower.Equals(name)))
            {
                _logger.LogInformation("🎯 Detected specific name search, preferring text search over vector search");
                return false;
            }

            // NO usar búsqueda vectorial para búsquedas muy específicas
            var exactSearchPatterns = new[]
            {
                "encuentra a ", "busca a ", "dame fotos de ", "muestra fotos de ",
                "fotos con ", "imágenes de ", "buscar a ", "encontrar a "
            };

            if (exactSearchPatterns.Any(pattern => searchLower.StartsWith(pattern)))
            {
                _logger.LogInformation("🎯 Detected specific search pattern, preferring text search");
                return false;
            }

            // SÍ usar búsqueda vectorial para consultas conceptuales o descriptivas
            var conceptualPatterns = new[]
            {
                "fotos familiares", "momentos felices", "celebraciones", "vacaciones",
                "cumpleaños", "navidad", "reuniones", "eventos", "actividades",
                "recuerdos", "momentos especiales", "familia reunida"
            };

            if (conceptualPatterns.Any(pattern => searchLower.Contains(pattern)))
            {
                _logger.LogInformation("🔍 Detected conceptual search, using vector search");
                return true;
            }

            // Para búsquedas cortas y específicas, preferir texto
            if (searchText.Split(' ').Length <= 3 && 
                !searchLower.Contains("fotos") && 
                !searchLower.Contains("imágenes"))
            {
                _logger.LogInformation("🎯 Short specific search detected, preferring text search");
                return false;
            }

            // Por defecto, usar búsqueda vectorial para consultas más largas y descriptivas
            return true;
        }

        /// <summary>
        /// Generar SAS URLs para las fotos en la lista de resultados
        /// </summary>
        private async Task GenerateSasUrlsForPicturesAsync(List<PicturesFamilySearchResultItem> pictures, string? twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogWarning("⚠️ Twin ID is null or empty, cannot generate SAS URLs");
                return;
            }

            try
            {
                // Crear el DataLake client factory
                var dataLakeFactory = _configuration.CreateDataLakeFactory(
                    LoggerFactory.Create(builder => builder.AddConsole()));
                
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                _logger.LogInformation("📸 Generating SAS URLs for {Count} pictures", pictures.Count);

                foreach (var picture in pictures)
                {
                    try
                    {
                        // Solo generar SAS URL si FileName está presente
                        if (!string.IsNullOrEmpty(picture.Filename))
                        {
                            // Construir la ruta completa del archivo: familyPhotos/{fileName}
                            var photoPath = $"familyPhotos/{picture.Filename}";
                            
                            _logger.LogDebug("📸 Generating SAS URL for photo: {PhotoPath}", photoPath);

                            // Generar SAS URL (válida por 24 horas)
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(photoPath, TimeSpan.FromHours(24));
                            
                            if (!string.IsNullOrEmpty(sasUrl))
                            {
                                picture.PictureURL = sasUrl;
                                _logger.LogDebug("✅ SAS URL generated successfully for photo: {FileName}", picture.Filename);
                            }
                            else
                            {
                                picture.PictureURL = "";
                                _logger.LogWarning("⚠️ Failed to generate SAS URL for photo: {FileName}", picture.Filename);
                            }
                        }
                        else
                        {
                            picture.PictureURL = "";
                            _logger.LogDebug("📸 No FileName found for picture ID: {PictureId}", picture.PictureId);
                        }
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogWarning(photoEx, "⚠️ Error generating SAS URL for picture {PictureId} with FileName {FileName}",
                            picture.PictureId, picture.Filename);
                        
                        // En caso de error, asegurar que la URL esté vacía
                        picture.PictureURL = "";
                    }
                }

                _logger.LogInformation("📸 Processed {PhotoCount} photos for SAS URL generation", pictures.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GenerateSasUrlsForPicturesAsync");
                
                // En caso de error general, asegurar que todas las URLs estén vacías
                foreach (var picture in pictures)
                {
                    picture.PictureURL = "";
                }
            }
        }

        /// <summary>
        /// Delete a picture document from the pictures family search index by ID
        /// </summary>
        /// <param name="documentId">The document ID to delete from the search index</param>
        /// <returns>Result indicating success or failure of the deletion operation</returns>
        public async Task<PicturesFamilyIndexResult> DeletePictureDocumentAsync(string documentId)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new PicturesFamilyIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("🗑️ Deleting picture document from pictures-family-index: {DocumentId}", documentId);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), PicturesFamilyIndexName, new AzureKeyCredential(_searchApiKey!));

                // Create document with only the ID for deletion
                var document = new SearchDocument { ["id"] = documentId };
                var deleteResult = await searchClient.DeleteDocumentsAsync(new[] { document });

                var errors = deleteResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (!errors.Any())
                {
                    _logger.LogInformation("✅ Picture document deleted from index: {DocumentId}", documentId);
                    return new PicturesFamilyIndexResult
                    {
                        Success = true,
                        Message = $"Picture document '{documentId}' deleted from pictures-family-index",
                        DocumentId = documentId,
                        IndexName = PicturesFamilyIndexName
                    };
                }
                else
                {
                    var error = errors.First();
                    _logger.LogError("❌ Error deleting picture document {DocumentId}: {Error}", documentId, error.ErrorMessage);
                    return new PicturesFamilyIndexResult
                    {
                        Success = false,
                        Error = $"Error deleting picture document: {error.ErrorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting picture document from index: {DocumentId}", documentId);
                return new PicturesFamilyIndexResult
                {
                    Success = false,
                    Error = $"Error deleting picture document: {ex.Message}",
                    DocumentId = documentId
                };
            }
        }

        /// <summary>
        /// Get all family pictures from the pictures-family-index for a specific TwinId
        /// </summary>
        /// <param name="twinId">The Twin ID to filter pictures for</param>
        /// <returns>List of all family pictures for the specified Twin</returns>
        public async Task<List<FamilyPictureDocument>> GetAllFamilyPicturesByTwinIdAsync(string twinId)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogWarning("⚠️ Azure Search service not available");
                    return new List<FamilyPictureDocument>();
                }

                if (string.IsNullOrWhiteSpace(twinId))
                {
                    _logger.LogWarning("⚠️ TwinId parameter is required");
                    return new List<FamilyPictureDocument>();
                }

                _logger.LogInformation("📸 Getting all family pictures for TwinId: {TwinId}", twinId);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), PicturesFamilyIndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Filter = $"TwinID eq '{twinId.Replace("'", "''")}'", // Filter by TwinID
                    Size = 1000, // Get up to 1000 pictures (adjust as needed)
                    IncludeTotalCount = true
                };

                // Add all fields to select
                var fieldsToSelect = new[]
                {
                    "id", "pictureId", "filename", "path", "descripcionGenerica", "pictureContent",
                    "pictureContentHTML", "contextoRecordatorio", "TwinID", "TotalTokens", "CreatedAt",
                    // ✅ NUEVOS CAMPOS AGREGADOS para selección
                    "fecha", "hora", "category", "eventType", "descripcionUsuario", "places", "people", "etiquetas"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Order by creation date descending (newest first) - Only for non-semantic queries
                if (searchOptions.QueryType != SearchQueryType.Semantic)
                {
                    searchOptions.OrderBy.Add("CreatedAt desc");
                }

                // Execute search with wildcard to get all documents matching the filter
                var response = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);

                var familyPictures = new List<FamilyPictureDocument>();

                await foreach (var result in response.Value.GetResultsAsync())
                {
                    var doc = result.Document;
                    var picture = new FamilyPictureDocument
                    {
                        SearchScore = result.Score ?? 1.0,
                        id = doc.GetString("id") ?? string.Empty,
                        PictureId = doc.GetString("pictureId") ?? string.Empty,
                        FileName = doc.GetString("filename") ?? string.Empty,
                        Path = doc.GetString("path") ?? string.Empty,
                        DescripcionGenerica = doc.GetString("descripcionGenerica") ?? string.Empty,
                        PictureContent = doc.GetString("pictureContent") ?? string.Empty,
                        PictureContentHTML = doc.GetString("pictureContentHTML") ?? string.Empty,
                        ContextoRecordatorio = doc.GetString("contextoRecordatorio") ?? string.Empty,
                        TwinID = doc.GetString("TwinID") ?? string.Empty,
                        TotalTokens = doc.GetInt32("TotalTokens") ?? 0,
                        CreatedAt = doc.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
                        // ✅ NUEVOS CAMPOS AGREGADOS del índice pictures-family-index
                        Fecha = doc.GetString("fecha") ?? string.Empty,
                        Hora = doc.GetString("hora") ?? string.Empty,
                        Category = doc.GetString("category") ?? string.Empty,
                        EventType = doc.GetString("eventType") ?? string.Empty,
                        DescripcionUsuario = doc.GetString("descripcionUsuario") ?? string.Empty,
                        Places = doc.GetString("places") ?? string.Empty,
                        People = doc.GetString("people") ?? string.Empty,
                        Etiquetas = doc.GetString("etiquetas") ?? string.Empty
                    };

                    familyPictures.Add(picture);
                }

                _logger.LogInformation("✅ Retrieved {Count} family pictures for TwinId: {TwinId}", familyPictures.Count, twinId);
                return familyPictures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting all family pictures for TwinId: {TwinId}", twinId);
                return new List<FamilyPictureDocument>();
            }
        }
    }

    /// <summary>
    /// Picture Family Index Content class for indexing picture data
    /// </summary>
    public class PictureFamilyIndexContent
    {
        public string PictureId { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;

        public int TotalTokens { get; set; } = 0;

        [JsonProperty("descripcionGenerica")]
        public string DescripcionGenerica { get; set; } = string.Empty;

        [JsonProperty("pictureContentHTML")]
        public string pictureContentHTML { get; set; } = string.Empty;

        public DetallesMemorables? DetallesMemorables { get; set; }
        
        /// <summary>
        /// Combined content field that sums DescripcionGenerica + ContextoQuePuedeAyudarARecordarElMomento
        /// </summary>
        public string PictureContent { get; set; } = string.Empty;
        
        public string TwinID { get; set; } = string.Empty;
        
        /// <summary>
        /// Convenience property to access the context from DetallesMemorables
        /// </summary>
        public string ContextoRecordatorio => DetallesMemorables?.ContextoQuePuedeAyudarARecordarElMomento ?? string.Empty;
    }

    /// <summary>
    /// Result class for pictures family index operations
    /// </summary>
    public class PicturesFamilyIndexResult
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
    /// Resultado de búsqueda de fotos familiares
    /// </summary>
    public class PicturesFamilySearchResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<PicturesFamilySearchResultItem> Results { get; set; } = new();

        public string? AiAnalysisHtml { get; set; }
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string SearchType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Elemento individual de resultado de búsqueda de fotos familiares
    /// </summary>
    public class PicturesFamilySearchResultItem
    {
        public string Id { get; set; } = string.Empty;
        public string PictureId { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string DescripcionGenerica { get; set; } = string.Empty;
        public string PictureContent { get; set; } = string.Empty;
        public string ContextoRecordatorio { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public int TotalTokens { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public double SearchScore { get; set; }
        public List<string> Highlights { get; set; } = new();

        public string PictureURL { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta JSON estructurada completa del análisis AI de fotos familiares
    /// </summary>
    public class FotosAiSearchResponse
    {
        [JsonProperty("htmlResponse")]
        public string HtmlResponse { get; set; } = string.Empty;

        [JsonProperty("picturesFound")]
        public List<PictureFoundResponse> PicturesFound { get; set; } = new();

        [JsonProperty("searchSummary")]
        public SearchSummaryResponse SearchSummary { get; set; } = new();
    }

    /// <summary>
    /// Información de una foto encontrada en la respuesta del análisis AI (versión original para compatibilidad)
    /// </summary>
    public class PictureFoundResponse
    {
        [JsonProperty("pictureId")]
        public string PictureId { get; set; } = string.Empty;

        [JsonProperty("filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string URL { get; set; } = string.Empty;

        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("descripcionGenerica")]
        public string DescripcionGenerica { get; set; } = string.Empty;

        [JsonProperty("pictureContent")]
        public string PictureContent { get; set; } = string.Empty;

        [JsonProperty("contextoRecordatorio")]
        public string ContextoRecordatorio { get; set; } = string.Empty;

        [JsonProperty("totalTokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("searchScore")]
        public double SearchScore { get; set; }

        [JsonProperty("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonProperty("highlights")]
        public List<string> Highlights { get; set; } = new();
    }

    /// <summary>
    /// Resumen de la búsqueda y análisis en la respuesta AI
    /// </summary>
    public class SearchSummaryResponse
    {
        [JsonProperty("totalFound")]
        public int TotalFound { get; set; }

        [JsonProperty("searchQuery")]
        public string SearchQuery { get; set; } = string.Empty;

        [JsonProperty("searchType")]
        public string SearchType { get; set; } = string.Empty;

        [JsonProperty("analysisInsights")]
        public List<string> AnalysisInsights { get; set; } = new();

        [JsonProperty("recommendations")]
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Respuesta completa del análisis AI de fotos familiares que se deserializa directamente del JSON de AI
    /// Esta clase representa exactamente la estructura JSON que retorna el AI en FotosAiSearchAsync
    /// </summary>
    public class FotosAiSearchCompleteResponse
    {
        [JsonProperty("htmlResponse")]
        public string HtmlResponse { get; set; } = string.Empty;

        [JsonProperty("picturesFound")]
        public List<PictureFoundAiResponse> PicturesFound { get; set; } = new();

        [JsonProperty("searchSummary")]
        public SearchSummaryAiResponse SearchSummary { get; set; } = new();
    }

    /// <summary>
    /// Información de una foto encontrada en la respuesta JSON del AI
    /// </summary>
    public class PictureFoundAiResponse
    {
        [JsonProperty("pictureId")]
        public string PictureId { get; set; } = string.Empty;

        [JsonProperty("filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("descripcionGenerica")]
        public string DescripcionGenerica { get; set; } = string.Empty;

        [JsonProperty("pictureContent")]
        public string PictureContent { get; set; } = string.Empty;

        [JsonProperty("contextoRecordatorio")]
        public string ContextoRecordatorio { get; set; } = string.Empty;

        [JsonProperty("totalTokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("searchScore")]
        public double SearchScore { get; set; }

        [JsonProperty("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonProperty("highlights")]
        public List<string> Highlights { get; set; } = new();
    }

    /// <summary>
    /// Resumen de búsqueda en la respuesta JSON del AI
    /// </summary>
    public class SearchSummaryAiResponse
    {
        [JsonProperty("totalFound")]
        public int TotalFound { get; set; }

        [JsonProperty("searchQuery")]
        public string SearchQuery { get; set; } = string.Empty;

        [JsonProperty("searchType")]
        public string SearchType { get; set; } = string.Empty;

        [JsonProperty("analysisInsights")]
        public List<string> AnalysisInsights { get; set; } = new();

        [JsonProperty("recommendations")]
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Represents a family picture document from the pictures-family-index
    /// Contains all the fields available in the Azure Search index
    /// </summary>
    public class FamilyPictureDocument
    {
        [JsonProperty("@search.score")]
        public double SearchScore { get; set; }

        [JsonProperty("id")]
        public string id { get; set; } = string.Empty;

        [JsonProperty("pictureId")]
        public string PictureId { get; set; } = string.Empty;

        [JsonProperty("filename")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("descripcionGenerica")]
        public string DescripcionGenerica { get; set; } = string.Empty;

        [JsonProperty("pictureContent")]
        public string PictureContent { get; set; } = string.Empty;

        [JsonProperty("pictureContentHTML")]
        public string PictureContentHTML { get; set; } = string.Empty;

        [JsonProperty("contextoRecordatorio")]
        public string ContextoRecordatorio { get; set; } = string.Empty;

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("TotalTokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("CreatedAt")]
        public DateTimeOffset CreatedAt { get; set; }

        // ✅ NUEVOS CAMPOS AGREGADOS del índice pictures-family-index
        [JsonProperty("fecha")]
        public string Fecha { get; set; } = string.Empty;

        [JsonProperty("hora")]
        public string Hora { get; set; } = string.Empty;

        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty;

        [JsonProperty("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonProperty("descripcionUsuario")]
        public string DescripcionUsuario { get; set; } = string.Empty;

        [JsonProperty("places")]
        public string Places { get; set; } = string.Empty;

        [JsonProperty("people")]
        public string People { get; set; } = string.Empty;

        [JsonProperty("etiquetas")]
        public string Etiquetas { get; set; } = string.Empty;
    }
}
