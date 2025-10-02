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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using TwinFx.Agents;

namespace TwinFx.Services
{
    /// <summary>
    /// Lightweight Azure Search service for chapter AI index (capitulos-ai-index)
    /// Only stores: id, TwinID, capituloId, Contenido
    /// Also supports a textoVector field for embeddings
    /// </summary>
    public class CapitulosAISearchIndex
    {
        private readonly ILogger<CapitulosAISearchIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;
        private const string IndexName = "capitulos-ai-index";
        private const int EmbeddingDimensions = 1536;

        public CapitulosAISearchIndex(ILogger<CapitulosAISearchIndex> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
            _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

            // Load Azure OpenAI configuration for embeddings
            _openAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT") ?? GetConfigurationValue("AzureOpenAI:Endpoint");
            _openAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY") ?? GetConfigurationValue("AzureOpenAI:ApiKey");
            _embeddingDeployment = "text-embedding-3-large";

            if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
            {
                try
                {
                    var credential = new AzureKeyCredential(_searchApiKey);
                    _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                    _logger.LogInformation("Capitulos AI SearchIndex client initialized");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing CapitulosAISearchIndex client");
                }
            }
            else
            {
                _logger.LogWarning("Azure Search configuration not found for CapitulosAISearchIndex");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("Azure OpenAI embedding client initialized for CapitulosAISearchIndex");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing Azure OpenAI client for CapitulosAISearchIndex");
                }
            }
            else
            {
                _logger.LogWarning("Azure OpenAI credentials not found for CapitulosAISearchIndex");
            }
        }

        private string? GetConfigurationValue(string key, string? defaultValue = null)
        {
            var value = _configuration.GetValue<string>(key);
            if (string.IsNullOrEmpty(value)) value = _configuration.GetValue<string>($"Values:{key}");
            return !string.IsNullOrEmpty(value) ? value : defaultValue;
        }

        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Generate embeddings for text content using Azure OpenAI
        /// </summary>
        private async Task<float[]?> GenerateEmbeddingsAsync(string text)
        {
            try
            {
                if (_embeddingClient == null)
                {
                    _logger.LogWarning("Embedding client not available, skipping vector generation");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Text content is empty, skipping vector generation");
                    return null;
                }

                // Truncate text if too long (Azure OpenAI has token limits)
                if (text.Length > 8000)
                {
                    text = text.Substring(0, 8000);
                    _logger.LogInformation("Text truncated to 8000 characters for embedding generation");
                }

                _logger.LogDebug("Generating embeddings for text: {Length} characters", text.Length);

                var embeddingOptions = new EmbeddingGenerationOptions
                {
                    Dimensions = EmbeddingDimensions
                };

                var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
                var embeddings = embedding.Value.ToFloats().ToArray();

                _logger.LogInformation("Generated embedding vector with {Dimensions} dimensions", embeddings.Length);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embeddings, continuing without vector search");
                return null;
            }
        }

        /// <summary>
        /// Create the simple capitulos-ai-index with only the four fields required by the user.
        /// Also adds a textoVector field for embeddings.
        /// </summary>
        public async Task<CapitulosIndexResult> CreateCapitulosAiIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new CapitulosIndexResult { Success = false, Error = "Azure Search service not available" };
                }

                _logger.LogInformation("Creating index {IndexName}", IndexName);

                // Configuration constants for vector search
                const string VectorSearchProfile = "capitulos-ai-vector-profile";
                const string HnswAlgorithmConfig = "capitulos-ai-hnsw-config";

                var fields = new List<SearchField>
                {
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // TwinID for filtering
                    new SearchableField("TwinID")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // capituloId
                    new SearchableField("capituloId")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Contenido (searchable text)
                    new SearchableField("Contenido")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // textoVector for embeddings - FIXED: Added VectorSearchProfileName
                    new SearchField("textoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions,
                        VectorSearchProfileName = VectorSearchProfile
                    }
                };

                // Configure vector search - REQUIRED for vector fields
                var vectorSearch = new VectorSearch();

                // Add HNSW algorithm configuration
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswAlgorithmConfig));

                // Add vector search profile - REQUIRED for textoVector field
                vectorSearch.Profiles.Add(new VectorSearchProfile(VectorSearchProfile, HnswAlgorithmConfig));

                // Create the index with vector search configuration
                var index = new SearchIndex(IndexName, fields)
                {
                    VectorSearch = vectorSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);

                _logger.LogInformation("Index {IndexName} created or updated", IndexName);

                return new CapitulosIndexResult
                {
                    Success = true,
                    IndexName = IndexName,
                    Message = "Index created or updated"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating capitulos-ai-index");
                return new CapitulosIndexResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Index a single capitulo document into capitulos-ai-index. Only the four fields are used plus optional textoVector.
        /// </summary>
        public async Task<CapitulosIndexResult> UploadIndexCapituloAsync(CapitulosAiDocument doc)
        {
            try
            {
                if (!IsAvailable) return new CapitulosIndexResult { Success = false, Error = "Azure Search service not available" };

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var document = new Dictionary<string, object>
                {
                    ["id"] = string.IsNullOrWhiteSpace(doc.Id) ? Guid.NewGuid().ToString() : doc.Id,
                    ["TwinID"] = doc.TwinID ?? string.Empty,
                    ["capituloId"] = doc.CapituloId ?? string.Empty,
                    ["Contenido"] = doc.Contenido ?? string.Empty
                };

                if (doc.TextoVector != null && doc.TextoVector.Length > 0)
                {
                    document["textoVector"] = doc.TextoVector;
                }

                var uploadResult = await searchClient.UploadDocumentsAsync(new[] { document });
                var first = uploadResult.Value.Results.FirstOrDefault();
                bool ok = first != null && first.Succeeded;

                if (ok)
                {
                    return new CapitulosIndexResult { Success = true, DocumentId = document["id"].ToString(), IndexName = IndexName };
                }

                var errorMessage = first?.ErrorMessage ?? "Unknown error";
                return new CapitulosIndexResult { Success = false, Error = errorMessage };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing capitulo document");
                return new CapitulosIndexResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Index documents from CursoCreadoAI into capitulos-ai-index with automatic vector generation
        /// Creates one document per chapter, extracting id, TwinID, capituloId, Contenido, and textoVector
        /// </summary>
        public async Task<List<CapitulosIndexResult>> IndexCursoCreadoAIAsync(CursoCreadoAI cursoCreadoAI)
        {
            var results = new List<CapitulosIndexResult>();

            try
            {
                if (!IsAvailable)
                {
                    results.Add(new CapitulosIndexResult { Success = false, Error = "Azure Search service not available" });
                    return results;
                }

                if (cursoCreadoAI?.Capitulos == null || !cursoCreadoAI.Capitulos.Any())
                {
                    results.Add(new CapitulosIndexResult { Success = false, Error = "No chapters found in CursoCreadoAI" });
                    return results;
                }

                _logger.LogInformation("Starting indexing of {ChapterCount} chapters from CursoCreadoAI into {IndexName}", 
                    cursoCreadoAI.Capitulos.Count, IndexName);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                foreach (var capitulo in cursoCreadoAI.Capitulos)
                {
                    try
                    {
                        _logger.LogInformation("Processing chapter: {ChapterTitle} (Chapter {ChapterNumber})", 
                            capitulo.Titulo, capitulo.Pagina);

                        // Build complete content for vector generation
                        var contenidoCompleto = BuildCompleteChapterContent(capitulo);

                        // Generate embeddings for the complete content
                        var embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);

                        // Create unique document ID
                        var guid = Guid.NewGuid().ToString("N");
                        var shortGuid = guid.Substring(0, 8);
                        var documentId = $"cap-{cursoCreadoAI.TwinID}-{cursoCreadoAI.id}-{capitulo.Pagina}-{shortGuid}";

                        // Create search document
                        var document = new Dictionary<string, object>
                        {
                            ["id"] = documentId,
                            ["TwinID"] = cursoCreadoAI.TwinID ?? string.Empty,
                            ["capituloId"] = $"{cursoCreadoAI.id}-cap-{capitulo.Pagina}",
                            ["Contenido"] = contenidoCompleto
                        };

                        // Add vector embeddings if available
                        if (embeddings != null && embeddings.Length > 0)
                        {
                            document["textoVector"] = embeddings;
                        }

                        // Upload document to search index
                        var uploadResult = await searchClient.UploadDocumentsAsync(new[] { document });
                        var first = uploadResult.Value.Results.FirstOrDefault();
                        bool success = first != null && first.Succeeded;

                        if (success)
                        {
                            _logger.LogInformation("Chapter indexed successfully: {ChapterTitle} -> DocumentId={DocumentId}", 
                                capitulo.Titulo, documentId);
                            
                            results.Add(new CapitulosIndexResult 
                            { 
                                Success = true, 
                                DocumentId = documentId, 
                                IndexName = IndexName,
                                Message = $"Chapter '{capitulo.Titulo}' indexed successfully"
                            });
                        }
                        else
                        {
                            var errorMessage = first?.ErrorMessage ?? "Unknown indexing error";
                            _logger.LogError("Failed to index chapter: {ChapterTitle} - Error: {Error}", 
                                capitulo.Titulo, errorMessage);
                            
                            results.Add(new CapitulosIndexResult 
                            { 
                                Success = false, 
                                Error = $"Failed to index chapter '{capitulo.Titulo}': {errorMessage}",
                                DocumentId = documentId
                            });
                        }
                    }
                    catch (Exception chapterEx)
                    {
                        _logger.LogError(chapterEx, "Error processing chapter: {ChapterTitle}", capitulo.Titulo ?? "Unknown");
                        
                        results.Add(new CapitulosIndexResult 
                        { 
                            Success = false, 
                            Error = $"Error processing chapter '{capitulo.Titulo}': {chapterEx.Message}"
                        });
                    }
                }

                var successCount = results.Count(r => r.Success);
                var failureCount = results.Count(r => !r.Success);

                _logger.LogInformation("Completed indexing CursoCreadoAI: {SuccessCount} chapters indexed successfully, {FailureCount} failed", 
                    successCount, failureCount);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing CursoCreadoAI into capitulos-ai-index");
                
                results.Add(new CapitulosIndexResult 
                { 
                    Success = false, 
                    Error = $"Error indexing CursoCreadoAI: {ex.Message}"
                });
                
                return results;
            }
        }

        /// <summary>
        /// Build complete content for vector search from chapter data
        /// Combines all relevant text fields from a chapter into a single searchable text
        /// </summary>
        private static string BuildCompleteChapterContent(CapituloCreadoAI capitulo)
        {
            var content = new StringBuilder();
            
            content.AppendLine("TITULO DEL CAPITULO:");
            content.AppendLine(capitulo.Titulo ?? "");
            content.AppendLine();
            
            if (capitulo.Objetivos != null && capitulo.Objetivos.Any())
            {
                content.AppendLine("OBJETIVOS:");
                foreach (var objetivo in capitulo.Objetivos)
                {
                    content.AppendLine($"- {objetivo}");
                }
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.Contenido))
            {
                content.AppendLine("CONTENIDO:");
                content.AppendLine(capitulo.Contenido);
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.ContenidoHTML))
            {
                // Remove HTML tags for vector generation, keep only text content
                var htmlContent = System.Text.RegularExpressions.Regex.Replace(capitulo.ContenidoHTML, "<.*?>", " ");
                content.AppendLine("CONTENIDO ADICIONAL:");
                content.AppendLine(htmlContent);
                content.AppendLine();
            }
            
            if (capitulo.Ejemplos != null && capitulo.Ejemplos.Any())
            {
                content.AppendLine("EJEMPLOS:");
                foreach (var ejemplo in capitulo.Ejemplos)
                {
                    content.AppendLine($"- {ejemplo}");
                }
                content.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(capitulo.Resumen))
            {
                content.AppendLine("RESUMEN:");
                content.AppendLine(capitulo.Resumen);
                content.AppendLine();
            }
            
            if (capitulo.Quizes != null && capitulo.Quizes.Any())
            {
                content.AppendLine("PREGUNTAS Y RESPUESTAS:");
                foreach (var quiz in capitulo.Quizes)
                {
                    content.AppendLine($"Pregunta: {quiz.Pregunta}");
                    if (quiz.Opciones != null)
                    {
                        foreach (var opcion in quiz.Opciones)
                        {
                            content.AppendLine($"  {opcion}");
                        }
                    }
                    content.AppendLine($"Respuesta Correcta: {quiz.RespuestaCorrecta}");
                    content.AppendLine($"Explicacion: {quiz.Explicacion}");
                    content.AppendLine();
                }
            }
            
            return content.ToString();
        }
    }

    public class CapitulosAiDocument
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string CapituloId { get; set; } = string.Empty;
        public string Contenido { get; set; } = string.Empty;
        public float[]? TextoVector { get; set; }
    }

    public class CapitulosIndexResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? IndexName { get; set; }
        public string? DocumentId { get; set; }
    }
}