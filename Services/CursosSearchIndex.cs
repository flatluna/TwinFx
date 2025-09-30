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
using Microsoft.Extensions.Azure;

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
    /// - Document chapters indexing for course content analysis
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
        private const string DocumentCapitulosIndexName = "document-capitulos";
        private const string VectorSearchProfile = "cursos-vector-profile";
        private const string CapitulosVectorSearchProfile = "capitulos-vector-profile";
        private const string HnswAlgorithmConfig = "cursos-hnsw-config";
        private const string CapitulosHnswAlgorithmConfig = "capitulos-hnsw-config";
        private const string VectorizerConfig = "cursos-vectorizer";
        private const string SemanticConfig = "cursos-semantic-config";
        private const string CapitulosSemanticConfig = "capitulos-semantic-config";
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
            _embeddingDeployment = "text-embedding-3-large";

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
        /// Create the document-capitulos search index with vector and semantic search capabilities
        /// </summary>
        public async Task<CursosIndexResult> CreateDocumentCapitulosIndexAsync()
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

                _logger.LogInformation("📚 Creating Document Capitulos Search Index: {IndexName}", DocumentCapitulosIndexName);

                // Define search fields based on CapituloRequest class
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // DocumentId field
                    new SearchableField("DocumentId")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // TwinID field for filtering by specific twin
                    new SearchableField("TwinId")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // CursoId field for filtering by specific course
                    new SearchableField("CursoId")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Total tokens field
                    new SimpleField("TotalTokens", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Título del capítulo (main searchable field)
                    new SearchableField("Titulo")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Descripción del capítulo
                    new SearchableField("Descripcion")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Número del capítulo
                    new SimpleField("NumeroCapitulo", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },

                    // Transcript (contenido del capítulo)
                    new SearchableField("Transcript")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Notas del capítulo
                    new SearchableField("Notas")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Comentarios
                    new SearchableField("Comentarios")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Duración en minutos
                    new SimpleField("DuracionMinutos", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Tags (etiquetas del capítulo)
                    new SearchableField("Tags")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Puntuación (1-5 estrellas)
                    new SimpleField("Puntuacion", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // Estado de completado
                    new SimpleField("Completado", SearchFieldDataType.Boolean)
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Resumen ejecutivo generado por AI
                    new SearchableField("ResumenEjecutivo")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Explicación del profesor en texto
                    new SearchableField("ExplicacionProfesorTexto")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Explicación del profesor en HTML
                    new SearchableField("ExplicacionProfesorHTML")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // CreatedAt field for timestamp filtering
                    new SimpleField("CreatedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },

                    // Vector field for semantic similarity search (textoVector as requested)
                    new SearchField("textoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions,
                        VectorSearchProfileName = CapitulosVectorSearchProfile
                    }
                };

                // Configure vector search for capitulos
                var vectorSearch = new VectorSearch();

                // Add HNSW algorithm configuration for capitulos
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(CapitulosHnswAlgorithmConfig));

                // Add vector search profile for capitulos
                vectorSearch.Profiles.Add(new VectorSearchProfile(CapitulosVectorSearchProfile, CapitulosHnswAlgorithmConfig));

                // Configure semantic search for capitulos
                var semanticSearch = new SemanticSearch();
                var prioritizedFields = new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("Titulo")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("Descripcion"));
                prioritizedFields.ContentFields.Add(new SemanticField("Transcript"));
                prioritizedFields.ContentFields.Add(new SemanticField("ResumenEjecutivo"));
                prioritizedFields.ContentFields.Add(new SemanticField("ExplicacionProfesorTexto"));
                prioritizedFields.ContentFields.Add(new SemanticField("Notas"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("Tags"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("CursoId"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(CapitulosSemanticConfig, prioritizedFields));

                // Create the document-capitulos search index
                var index = new SearchIndex(DocumentCapitulosIndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ Document Capitulos Index '{IndexName}' created successfully", DocumentCapitulosIndexName);

                return new CursosIndexResult
                {
                    Success = true,
                    Message = $"Document Capitulos Index '{DocumentCapitulosIndexName}' created successfully",
                    IndexName = DocumentCapitulosIndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating Document Capitulos Index");
                return new CursosIndexResult
                {
                    Success = false,
                    Error = $"Error creating Document Capitulos Index: {ex.Message}"
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
        /// Build complete content for vector search from chapter data
        /// </summary>
        private static string BuildCompleteChapterContent(CapituloSearchRequest capitulo)
        {
            var content = new StringBuilder();
            
            content.AppendLine("TÍTULO DEL CAPÍTULO:");
            content.AppendLine(capitulo.Titulo ?? "");
            content.AppendLine();
            
            if (!string.IsNullOrEmpty(capitulo.Descripcion))
            {
                content.AppendLine("DESCRIPCIÓN:");
                content.AppendLine(capitulo.Descripcion);
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.Transcript))
            {
                content.AppendLine("CONTENIDO DEL CAPÍTULO:");
                content.AppendLine(capitulo.Transcript);
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.ResumenEjecutivo))
            {
                content.AppendLine("RESUMEN EJECUTIVO:");
                content.AppendLine(capitulo.ResumenEjecutivo);
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.ExplicacionProfesorTexto))
            {
                content.AppendLine("EXPLICACIÓN DEL PROFESOR:");
                content.AppendLine(capitulo.ExplicacionProfesorTexto);
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.Notas))
            {
                content.AppendLine("NOTAS:");
                content.AppendLine(capitulo.Notas);
                content.AppendLine();
            }
            
            if (capitulo.Tags != null && capitulo.Tags.Count > 0)
            {
                content.AppendLine("ETIQUETAS:");
                content.AppendLine(string.Join(", ", capitulo.Tags));
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
        /// Index a chapter analysis in Azure AI Search from chapter data
        /// </summary>
        public async Task<CursosIndexResult> IndexChapterAnalysisAsync(CapituloSearchRequest capitulo, double processingTimeMs = 0.0)
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

                _logger.LogInformation("📚 Indexing chapter analysis for CursoId: {CursoId}, TwinID: {TwinId}, Chapter: {ChapterTitle}", 
                    capitulo.CursoId, capitulo.TwinId, capitulo.Titulo);

                // Create search client
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), DocumentCapitulosIndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate unique document ID
                var documentId = await GenerateUniqueChapterDocumentId(capitulo.DocumentId, capitulo.TwinId, capitulo.NumeroCapitulo);

                // Generate embeddings for vector search
                var combinedContent = BuildCompleteChapterContent(capitulo);
                var embeddings = await GenerateEmbeddingsAsync(combinedContent);

                // Create search document
                var document = new Dictionary<string, object>
                {
                    ["id"] = documentId,
                    ["DocumentId"] = capitulo.DocumentId ?? "",
                    ["TwinId"] = capitulo.TwinId ?? "",
                    ["CursoId"] = capitulo.CursoId ?? "",
                    ["TotalTokens"] = capitulo.TotalTokens,
                    ["Titulo"] = capitulo.Titulo ?? "",
                    ["Descripcion"] = capitulo.Descripcion ?? "",
                    ["NumeroCapitulo"] = capitulo.NumeroCapitulo,
                    ["Transcript"] = capitulo.Transcript ?? "",
                    ["Notas"] = capitulo.Notas ?? "",
                    ["Comentarios"] = capitulo.Comentarios ?? "",
                    ["DuracionMinutos"] = capitulo.DuracionMinutos ?? 0,
                    ["Tags"] = capitulo.Tags != null ? string.Join(", ", capitulo.Tags) : "",
                    ["Puntuacion"] = capitulo.Puntuacion ?? 0,
                    ["Completado"] = capitulo.Completado,
                    ["ResumenEjecutivo"] = capitulo.ResumenEjecutivo ?? "",
                    ["ExplicacionProfesorTexto"] = capitulo.ExplicacionProfesorTexto ?? "",
                    ["ExplicacionProfesorHTML"] = capitulo.ExplicacionProfesorHTML ?? "",
                    ["CreatedAt"] = DateTimeOffset.UtcNow
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
                    _logger.LogInformation("✅ Chapter analysis indexed successfully: DocumentId={DocumentId}", documentId);
                    return new CursosIndexResult
                    {
                        Success = true,
                        Message = "Chapter analysis indexed successfully",
                        IndexName = DocumentCapitulosIndexName,
                        DocumentId = documentId,
                        HasVectorSearch = embeddings != null,
                        HasSemanticSearch = true
                    };
                }
                else
                {
                    var errorMessage = firstResult?.ErrorMessage ?? "Unknown indexing error";
                    _logger.LogError("❌ Failed to index chapter analysis: {Error}", errorMessage);
                    return new CursosIndexResult
                    {
                        Success = false,
                        Error = $"Failed to index chapter analysis: {errorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing chapter analysis");
                return new CursosIndexResult
                {
                    Success = false,
                    Error = $"Error indexing chapter analysis: {ex.Message}"
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

        ///////////////////
        ////////////////////////
        ///

        public async Task<CursoSearchResult> SearchCurso2Async(SearchQuery query)
        {
            var options = new SearchOptions
            {
                Size = query.Top,
                Skip = Math.Max(0, (query.Page - 1) * query.Top),
                IncludeTotalCount = true
            };

            // Campos que queremos recuperar (ajusta si necesitas otros)  
            var fieldsToSelect = new[]
            {
                "id", "CursoId", "DocumentId", "ResumenEjecutivo", "Completado", "Puntuacion",
                "CreatedAt", "Transcript", "ExplicacionProfesorHTML",
                "Titulo", "Descripcion", "NumeroCapitulo", "Notas", "Comentarios",
                "DuracionMinutos", "Tags", "TwinId"
            };
            foreach (var f in fieldsToSelect) options.Select.Add(f);

            // Filtros simples  
            var filterParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.TwinId))
                filterParts.Add($"TwinId eq '{query.TwinId.Replace("'", "''")}'");
            if (query.SuccessfulOnly)
                filterParts.Add("Completado eq true");
            if (filterParts.Any())
                options.Filter = string.Join(" and ", filterParts);

            string searchText = query.SearchText ?? string.Empty;

            // Semantic / Hybrid  
            if (query.UseSemanticSearch || query.UseHybridSearch)
            {
                options.QueryType = SearchQueryType.Semantic;
                options.SemanticSearch = new SemanticSearchOptions
                {
                    SemanticConfigurationName = SemanticConfig
                };
            }
            else
            {
                options.QueryType = SearchQueryType.Simple;
            }

            // Vector search (usando VectorSearchOptions / VectorizedQuery como en tu snippet)  
            if (query.UseVectorSearch)
            {
                // Si ya tienes el embedding en query.Vector, úsalo; si no, genera uno.  
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
                    // Crea la consulta vectorial  
                    var vectorQuery = new VectorizedQuery(embedding)
                    {
                        KNearestNeighborsCount = Math.Max(1, query.Top * 2) // por ejemplo Top*2 para candidatos a re-rank  
                    };
                    vectorQuery.Fields.Add("textoVector"); // nombre del campo vector en tu índice  

                    options.VectorSearch = new VectorSearchOptions();
                    options.VectorSearch.Queries.Add(vectorQuery);

                    // Si no quieres combinar con texto (solo vector), usa '*'  
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

            var searchClient = new SearchClient(new Uri(_searchEndpoint!), DocumentCapitulosIndexName, new AzureKeyCredential(_searchApiKey!));

            
            var response = await searchClient.SearchAsync<SearchDocument>(searchText, options);

            var final = new CursoSearchResult
            {
                TotalCount = response.Value.TotalCount ?? 0,
                Page = query.Page,
                PageSize = query.Top
            };

            // Extraer respuestas semánticas si existen (seguro-dependiente del SDK)  
            if (response.Value.SemanticSearch?.Answers != null)
            {
                final.SearchQuery = string.Join(" ", response.Value.SemanticSearch.Answers
                    .Select(a => ExtractAnswerText(a))
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            else
            {
                final.SearchQuery = string.Empty;
            }

            var results = new List<CursosSearchResultItem>();

            await foreach (var r in response.Value.GetResultsAsync())
            {
                var doc = r.Document;
                var item = new CursosSearchResultItem
                {
                    Id = doc.GetString("id") ?? string.Empty,
                    CursoEntryId = doc.GetString("CursoId") ?? doc.GetString("DocumentId") ?? string.Empty,
                    ExecutiveSummary = doc.GetString("ResumenEjecutivo") ?? string.Empty,
                    Success = doc.GetBoolean("Completado") ?? false,
                    ProcessingTimeMs = doc.GetDouble("Puntuacion") ?? 0.0,
                    AnalyzedAt = doc.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
                    Transcript = doc.GetString("Transcript") ?? doc.GetString("ExplicacionProfesorHTML"),
                    SearchScore = r.Score ?? 0.0
                };

                // Captions (semantic) -> Highlights: usamos reflexión para ser robustos a diferencias de SDK  
                if (r.SemanticSearch?.Captions?.Any() == true)
                {
                    var captionsObjects = r.SemanticSearch.Captions.Cast<object>();
                    item.Highlights = captionsObjects
                        .Select(ExtractCaptionText)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                }

                results.Add(item);
            }

            final.Results = results;
            return final;
        }

        /// <summary>
        /// ///////////////////////
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public async Task<CursoSearchResult> SearchCursoAsync(SearchQuery query)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new CursoSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("🔍 Searching chapters (document-capitulos): '{Query}'",
                    query.SearchText?.Substring(0, Math.Min(query.SearchText?.Length ?? 0, 50)));

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), DocumentCapitulosIndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = 5,
                    QueryType = SearchQueryType.Semantic,
                    IncludeTotalCount = true

                };
                    

                // select useful fields from the capitulos index
                var fieldsToSelect = new[]
                {
                    "id", "DocumentId", "TwinId", "CursoId", "Titulo", "Descripcion", "Transcript",
                    "ResumenEjecutivo", "NumeroCapitulo", "Tags", "CreatedAt", "Puntuacion", "Completado"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }
                Console.WriteLine("\nQuery 2: Semantic query (no captions, no answers) for 'walking distance to live music'.");
                var semanticOptions = new SearchOptions
                {
                    Size = 5,
                    QueryType = SearchQueryType.Semantic,
                    SemanticSearch = new SemanticSearchOptions
                    {
                        SemanticConfigurationName = CapitulosSemanticConfig
                    },
                    IncludeTotalCount = true,
                    Select = { "Transcript" }
                };

               var CursoResult =  await RunQuery(searchClient, query.SearchText, semanticOptions);
                // Run Azure Cognitive Search
                 
 
                return CursoResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching chapters (document-capitulos)");
                return new CursoSearchResult
                {
                    Success = false,
                    Error = $"Error searching chapters: {ex.Message}"
                };
            }
        }

        public static async Task<CursoSearchResult> RunQuery(
        SearchClient client,
        string searchText,
        SearchOptions options,
        bool showCaptions = false,
        bool showAnswers = false)
        {
            CursoSearchResult Cursos = new CursoSearchResult();
            try
            {
                var response = await client.SearchAsync<SearchDocument>(searchText, options);

                if (showAnswers && response.Value.SemanticSearch?.Answers != null)
                {
                    Console.WriteLine("Extractive Answers:");
                    foreach (var answer in response.Value.SemanticSearch.Answers)
                    {
                        Console.WriteLine($"  {answer.Highlights}");
                    }
                    Console.WriteLine(new string('-', 40));
                }

                await foreach (var result in response.Value.GetResultsAsync())
                {
                    var doc = result.Document;
                    CursosSearchResultItem item = new CursosSearchResultItem();
                    item.Transcript = doc.GetString("Transcript") ?? "";
                    // Print captions first if available
                    if (showCaptions && result.SemanticSearch?.Captions != null)
                    {
                        foreach (var caption in result.SemanticSearch.Captions)
                        {
                            Console.WriteLine($"Caption: {caption.Highlights}");
                        }
                    }
                   
                
                    // Print @search.rerankerScore if available
                    if (result.SemanticSearch != null && result.SemanticSearch.RerankerScore.HasValue)
                    {
                        item.Success = true;
                        item.SearchScore = result.Score ?? 0.0;
                        Cursos.Results.Add(item); 
                    }
                     
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying index: {ex.Message}");
            }

            return Cursos;
        }
        /// <summary>
        /// Generate unique document ID for chapter analysis
        /// </summary>
        private async Task<string> GenerateUniqueChapterDocumentId(string documentId, string twinId, int numeroCapitulo)
        {
            var baseId = $"capitulo-{documentId}-{twinId}-{numeroCapitulo}";
            var uniqueDocumentId = baseId;
            var counter = 1;

            // Check if document exists and increment counter if needed
            while (await ChapterDocumentExistsAsync(uniqueDocumentId))
            {
                uniqueDocumentId = $"{baseId}-{counter}";
                counter++;
            }

            return uniqueDocumentId;
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

        /// <summary>
        /// Check if a chapter document exists in the search index
        /// </summary>
        private async Task<bool> ChapterDocumentExistsAsync(string documentId)
        {
            try
            {
                if (!IsAvailable) return false;

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), DocumentCapitulosIndexName, new AzureKeyCredential(_searchApiKey!));
                
                var response = await searchClient.GetDocumentAsync<Dictionary<string, object>>(documentId);
                return response != null;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error checking chapter document existence for {DocumentId}", documentId);
                return false;
            }
        }

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

        // Helper: extrae texto de una Answer (robusta)  
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

        // TODO: Implementa esta función para llamar a tu generador de embeddings (Azure OpenAI u otro)  
     
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
    /// </summary 
    /// <summary>
    /// Result class for getting a specific course by ID (equivalent to CarsIndexGetResult)
    /// </summary>
    public class CursoIndexGetResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string CourseID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public CursosSearchResultItem? CourseSearchResults { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CursoSearchResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<CursosSearchResultItem> Results { get; set; } = new();
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string SearchType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request class for chapter indexing (based on CapituloRequest but adapted for search)
    /// </summary>
    public class CapituloSearchRequest
    {
        public int TotalTokens { get; set; } = 0;
        
        // Campos básicos del capítulo
        public string Titulo { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int NumeroCapitulo { get; set; }

        // Contenido del capítulo
        public string? Transcript { get; set; }
        public string? Notas { get; set; }
        public string? Comentarios { get; set; }

        // Metadatos
        public int? DuracionMinutos { get; set; }
        public List<string>? Tags { get; set; } = new List<string>();

        // Evaluación inicial (opcional)
        public int? Puntuacion { get; set; } // 1-5 estrellas

        // Identificadores de relación
        public string CursoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty; // NEW FIELD as requested

        // Estado inicial
        public bool Completado { get; set; } = false;

        // ✨ NUEVOS CAMPOS GENERADOS POR AI ✨
        /// <summary>
        /// Resumen ejecutivo del capítulo generado por AI
        /// </summary>
        public string? ResumenEjecutivo { get; set; }
        
        /// <summary>
        /// Explicación detallada del profesor en texto plano para conversión a voz
        /// </summary>
        public string? ExplicacionProfesorTexto { get; set; }
        
        /// <summary>
        /// Explicación detallada del profesor en formato HTML con estilos profesionales
        /// </summary>
        public string? ExplicacionProfesorHTML { get; set; }

        // NOTE: Quiz and Ejemplos are excluded as requested
        // textoVector will be generated automatically during indexing
    }
}
