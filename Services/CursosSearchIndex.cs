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
using TwinFx.Services;
using TwinFx.Agents;

namespace TwinFx.Services
{
    /// <summary>
    /// Azure AI Search Service specifically designed for Courses/Education indexing and search
    /// ========================================================================
    /// 
    /// This service creates and manages a search index optimized for courses and education with:
    /// - Vector search capabilities using Azure OpenAI embeddings
    /// - Semantic search for natural language queries about courses
    /// - Full-text search across course descriptions and details
    /// - Course specifications-based search and filtering
    /// - Course status tracking and filtering
    /// 
    /// Author: TwinFx Project
    /// Date: January 16, 2025
    /// </summary>
    public class CursosSearchIndex
    {
        private readonly ILogger<CursosSearchIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Configuration constants
        private const string CursosIndexName = "cursos-index";
        private const string VectorSearchProfile = "cursos-vector-profile";
        private const string HnswAlgorithmConfig = "cursos-hnsw-config";
        private const string VectorizerConfig = "cursos-vectorizer";
        private const string SemanticConfig = "cursos-semantic-config";
        private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions

        // Configuration keys
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;

        public CursosSearchIndex(ILogger<CursosSearchIndex> logger, IConfiguration configuration)
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
                    _logger.LogInformation("📚 Cursos Search Index client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure Search client for Cursos Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure Search credentials not found for Cursos Index");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("📚 Azure OpenAI embedding client initialized for Cursos Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for Cursos Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure OpenAI credentials not found for Cursos Index");
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
        /// Check if the cursos search service is available
        /// </summary>
        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Create the cursos search index with vector and semantic search capabilities
        /// </summary>
        public async Task<CursosIndexResult> CreateCursosIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new CursosIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📚 Creating Cursos Search Index: {IndexName}", CursosIndexName);

                // Define search fields based on the requested schema: nombreClase, etiquetas, textoDetalles, id, TwinID, textoVector
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
                    
                    // CourseID (equivalent to Course.Id)
                    new SearchableField("CourseID")
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
                    
                    // nombreClase (course name - primary course identifier)
                    new SearchableField("nombreClase")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // etiquetas (course tags/categories)
                    new SearchableField("etiquetas")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // textoDetalles (detailed course information and analysis)
                    new SearchableField("textoDetalles")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // ProcessingTimeMS (processing time in milliseconds)
                    new SimpleField("ProcessingTimeMS", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // CreatedAt field for timestamp filtering
                    new SimpleField("CreatedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },
                    
                    // EstadoCurso (course status)
                    new SearchableField("EstadoCurso")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    // Institucion (educational institution)
                    new SearchableField("Institucion")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Instructor (course instructor)
                    new SearchableField("Instructor")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Duracion (course duration)
                    new SearchableField("Duracion")
                    {
                        IsFilterable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Nivel (course level: beginner, intermediate, advanced)
                    new SearchableField("Nivel")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    // Combined content field for vector search (contenidoCompleto)
                    new SearchableField("contenidoCompleto")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Vector field for semantic similarity search (textoVector as requested)
                    new SearchField("textoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
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
                    TitleField = new SemanticField("nombreClase")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("textoDetalles"));
                prioritizedFields.ContentFields.Add(new SemanticField("contenidoCompleto"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("etiquetas"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("Institucion"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("Instructor"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("Nivel"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

                // Create the cursos search index
                var index = new SearchIndex(CursosIndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ Cursos Index '{IndexName}' created successfully", CursosIndexName);

                return new CursosIndexResult
                {
                    Success = true,
                    Message = $"Cursos Index '{CursosIndexName}' created successfully",
                    IndexName = CursosIndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating Cursos Index");
                return new CursosIndexResult
                {
                    Success = false,
                    Error = $"Error creating Cursos Index: {ex.Message}"
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
                    _logger.LogInformation("📚 Text truncated to 8000 characters for embedding generation");
                }

                _logger.LogDebug("📚 Generating embeddings for text: {Length} characters", text.Length);

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
        /// Build complete content for vector search
        /// </summary>
        private static string BuildCompleteCourseContent(string nombreClase, string textoDetalles, string etiquetas, string institucion, string instructor)
        {
            var content = new StringBuilder();
            
            content.AppendLine("NOMBRE DEL CURSO:");
            content.AppendLine(nombreClase ?? "");
            content.AppendLine();
            
            content.AppendLine("DETALLES DEL CURSO:");
            content.AppendLine(textoDetalles ?? "");
            content.AppendLine();
            
            content.AppendLine("ETIQUETAS:");
            content.AppendLine(etiquetas ?? "");
            content.AppendLine();
            
            if (!string.IsNullOrEmpty(institucion))
            {
                content.AppendLine("INSTITUCIÓN:");
                content.AppendLine(institucion);
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(instructor))
            {
                content.AppendLine("INSTRUCTOR:");
                content.AppendLine(instructor);
                content.AppendLine();
            }
            
            return content.ToString();
        }

        /// <summary>
        /// Index a course analysis in Azure AI Search from course data
        /// </summary>
        public async Task<CursosIndexResult> IndexCourseAnalysisAsync(TwinFx.Agents.CrearCursoRequest courseRequest, string twinId, double processingTimeMs = 0.0)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new CursosIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📚 Indexing course analysis for CourseID: {CourseId}, TwinID: {TwinId}", courseRequest.CursoId, twinId);

                // Create search client
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), CursosIndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate unique document ID
                var documentId = await GenerateUniqueCourseDocumentId(courseRequest.CursoId, twinId);

                // Extract course data
                var nombreClase = courseRequest.Curso?.NombreClase ?? "";
                var textoDetalles = courseRequest.Curso?.textoDetails ?? "";
                var etiquetas = courseRequest.Curso?.Etiquetas ?? "";
                var institucion = courseRequest.Curso?.Plataforma ?? ""; // Using Plataforma as institution
                var instructor = courseRequest.Curso?.Instructor ?? "";
                var estadoCurso = courseRequest.Metadatos?.EstadoCurso ?? "seleccionado";
                var duracion = courseRequest.Curso?.Duracion ?? "";
                var nivel = courseRequest.Curso?.Categoria ?? ""; // Using Categoria as level

                // Generate embeddings for vector search
                var combinedContent = BuildCompleteCourseContent(nombreClase, textoDetalles, etiquetas, institucion, instructor);
                var embeddings = await GenerateEmbeddingsAsync(combinedContent);

                // Create search document
                var document = new Dictionary<string, object>
                {
                    ["id"] = documentId,
                    ["Success"] = true,
                    ["CourseID"] = courseRequest.CursoId ?? "",
                    ["TwinID"] = twinId,
                    ["nombreClase"] = nombreClase,
                    ["etiquetas"] = etiquetas,
                    ["textoDetalles"] = textoDetalles,
                    ["ProcessingTimeMS"] = processingTimeMs,
                    ["CreatedAt"] = DateTimeOffset.UtcNow,
                    ["EstadoCurso"] = estadoCurso,
                    ["Institucion"] = institucion,
                    ["Instructor"] = instructor,
                    ["Duracion"] = duracion,
                    ["Nivel"] = nivel,
                    ["contenidoCompleto"] = combinedContent
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
                    _logger.LogInformation("✅ Course analysis indexed successfully: DocumentId={DocumentId}", documentId);
                    return new CursosIndexResult
                    {
                        Success = true,
                        Message = "Course analysis indexed successfully",
                        IndexName = CursosIndexName,
                        DocumentId = documentId,
                        HasVectorSearch = embeddings != null,
                        HasSemanticSearch = true
                    };
                }
                else
                {
                    var errorMessage = firstResult?.ErrorMessage ?? "Unknown indexing error";
                    _logger.LogError("❌ Failed to index course analysis: {Error}", errorMessage);
                    return new CursosIndexResult
                    {
                        Success = false,
                        Error = $"Failed to index course analysis: {errorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing course analysis");
                return new CursosIndexResult
                {
                    Success = false,
                    Error = $"Error indexing course analysis: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Generate unique document ID for course analysis
        /// </summary>
        private async Task<string> GenerateUniqueCourseDocumentId(string courseId, string twinId)
        {
            var baseId = $"curso-{courseId}-{twinId}";
            var documentId = baseId;
            var counter = 1;

            // Check if document exists and increment counter if needed
            while (await CourseDocumentExistsAsync(documentId))
            {
                documentId = $"{baseId}-{counter}";
                counter++;
            }

            return documentId;
        }

        /// <summary>
        /// Check if a course document exists in the search index
        /// </summary>
        private async Task<bool> CourseDocumentExistsAsync(string documentId)
        {
            try
            {
                if (!IsAvailable) return false;

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), CursosIndexName, new AzureKeyCredential(_searchApiKey!));
                
                var response = await searchClient.GetDocumentAsync<Dictionary<string, object>>(documentId);
                return response != null;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error checking course document existence for {DocumentId}", documentId);
                return false;
            }
        }
    }

    /// <summary>
    /// Result class for cursos index operations
    /// </summary>
    public class CursosIndexResult
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
    /// Individual course search result item (equivalent to CarsSearchResultItem)
    /// </summary>
    public class CursosSearchResultItem
    {
        public string Id { get; set; } = string.Empty;
        public string CourseID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string NombreClase { get; set; } = string.Empty;
        public string Etiquetas { get; set; } = string.Empty;
        public string TextoDetalles { get; set; } = string.Empty;
        public bool Success { get; set; }
        public double ProcessingTimeMS { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string EstadoCurso { get; set; } = string.Empty;
        public string Institucion { get; set; } = string.Empty;
        public string Instructor { get; set; } = string.Empty;
        public string Duracion { get; set; } = string.Empty;
        public string Nivel { get; set; } = string.Empty;
        public double SearchScore { get; set; }
    }

    /// <summary>
    /// Result class for getting a specific course by ID (equivalent to CarsIndexGetResult)
    /// </summary>
    public class CursosIndexGetResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string CourseID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public CursosSearchResultItem? CourseSearchResults { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
