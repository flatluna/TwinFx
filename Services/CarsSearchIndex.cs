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

namespace TwinFx.Services;

/// <summary>
/// Azure AI Search Service specifically designed for Cars/Vehicles indexing and search
/// ========================================================================
/// 
/// This service creates and manages a search index optimized for cars and vehicles with:
/// - Vector search capabilities using Azure OpenAI embeddings
/// - Semantic search for natural language queries about vehicles
/// - Full-text search across car descriptions and features
/// - Vehicle specifications-based search and filtering
/// - Car status tracking and filtering
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class CarsSearchIndex
{
    private readonly ILogger<CarsSearchIndex> _logger;
    private readonly IConfiguration _configuration;
    private readonly SearchIndexClient? _indexClient;
    private readonly AzureOpenAIClient? _azureOpenAIClient;
    private readonly EmbeddingClient? _embeddingClient;

    // Configuration constants
    private const string CarsIndexName = "cars-vehicles-index";
    private const string VectorSearchProfile = "cars-vector-profile";
    private const string HnswAlgorithmConfig = "cars-hnsw-config";
    private const string VectorizerConfig = "cars-vectorizer";
    private const string SemanticConfig = "cars-semantic-config";
    private const int EmbeddingDimensions = 1536; // text-embedding-ada-002 dimensions

    // Configuration keys
    private readonly string? _searchEndpoint;
    private readonly string? _searchApiKey;
    private readonly string? _openAIEndpoint;
    private readonly string? _openAIApiKey;
    private readonly string? _embeddingDeployment;

    public CarsSearchIndex(ILogger<CarsSearchIndex> logger, IConfiguration configuration)
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
                _logger.LogInformation("?? Cars Search Index client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error initializing Azure Search client for Cars Index");
            }
        }
        else
        {
            _logger.LogWarning("?? Azure Search credentials not found for Cars Index");
        }

        // Initialize Azure OpenAI client for embeddings
        if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
        {
            try
            {
                _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                _logger.LogInformation("?? Azure OpenAI embedding client initialized for Cars Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error initializing Azure OpenAI client for Cars Index");
            }
        }
        else
        {
            _logger.LogWarning("?? Azure OpenAI credentials not found for Cars Index");
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
    /// Check if the cars search service is available
    /// </summary>
    public bool IsAvailable => _indexClient != null;

    /// <summary>
    /// Create the cars search index with vector and semantic search capabilities
    /// </summary>
    public async Task<CarsIndexResult> CreateCarsIndexAsync()
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Creating Cars Search Index: {IndexName}", CarsIndexName);

            // Define search fields based on the requested schema: CarID, TwinID, ExecutiveSummary, DetaileHTMLReport, ProcessingTimeMS, MetadataValues, contenidoCompleto, contenidoVector, carInsurance, carLoan, carTitle
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
                
                // CarID (equivalent to CarData.Id)
                new SearchableField("CarID")
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
                
                // ExecutiveSummary (car analysis summary)
                new SearchableField("ExecutiveSummary")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // DetaileHTMLReport (detailed HTML report of car analysis) - note the typo "Detaile" as requested
                new SearchableField("DetaileHTMLReport")
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
                
                // MetadataKeys (car metadata as keys)
                new SearchableField("MetadataKeys")
                {
                    IsFilterable = true,
                    IsFacetable = true
                },
                
                // MetadataValues (car metadata as values) - as requested
                new SearchableField("MetadataValues")
                {
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: carInsurance field (car insurance documents and information)
                new SearchableField("carInsurance")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: carLoan field (car loan documents and financing information)
                new SearchableField("carLoan")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // NEW: carTitle field (car title documents and ownership information)
                new SearchableField("carTitle")
                {
                    IsFilterable = true,
                    AnalyzerName = LexicalAnalyzerName.EsLucene
                },
                
                // Combined content field for vector search (contenidoCompleto as requested)
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
                TitleField = new SemanticField("ExecutiveSummary")
            };

            // Content fields for semantic ranking
            prioritizedFields.ContentFields.Add(new SemanticField("DetaileHTMLReport"));
            prioritizedFields.ContentFields.Add(new SemanticField("contenidoCompleto"));
            prioritizedFields.ContentFields.Add(new SemanticField("carInsurance"));
            prioritizedFields.ContentFields.Add(new SemanticField("carLoan"));
            prioritizedFields.ContentFields.Add(new SemanticField("carTitle"));

            // Keywords fields for semantic ranking
            prioritizedFields.KeywordsFields.Add(new SemanticField("CarID"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("MetadataKeys"));
            prioritizedFields.KeywordsFields.Add(new SemanticField("MetadataValues"));

            semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

            // Create the cars search index
            var index = new SearchIndex(CarsIndexName, fields)
            {
                VectorSearch = vectorSearch,
                SemanticSearch = semanticSearch
            };

            var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("? Cars Index '{IndexName}' created successfully", CarsIndexName);

            return new CarsIndexResult
            {
                Success = true,
                Message = $"Cars Index '{CarsIndexName}' created successfully",
                IndexName = CarsIndexName,
                FieldsCount = fields.Count,
                HasVectorSearch = true,
                HasSemanticSearch = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating Cars Index");
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error creating Cars Index: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Index a car comprehensive analysis result in Azure AI Search from AI response JSON
    /// </summary>
    public async Task<CarsIndexResult> IndexCarAnalysisFromAIResponseAsync(string aiJsonResponse, string carId, string twinId, double processingTimeMs = 0.0)
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Indexing car analysis for CarID: {CarId}, TwinID: {TwinId}", carId, twinId);

            // Parse AI response to extract components
            var (executiveSummary, detailedHtmlReport, detalleTexto, metadata) = ParseAIResponse(aiJsonResponse);

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));

            // Generate unique document ID
            var documentId = await GenerateUniqueCarDocumentId(carId, twinId);

            // Generate embeddings for vector search
            var combinedContent = BuildCompleteCarContent(executiveSummary, detalleTexto, metadata);
            var embeddings = await GenerateEmbeddingsAsync(combinedContent);

            // Build metadata for search
            var metadataKeys = BuildCarMetadataKeys(metadata);
            var metadataValues = BuildCarMetadataValues(metadata);

            // Create search document
            var document = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["Success"] = true,
                ["CarID"] = carId,
                ["TwinID"] = twinId,
                ["ExecutiveSummary"] = executiveSummary,
                ["DetaileHTMLReport"] = detailedHtmlReport,
                ["ProcessingTimeMS"] = processingTimeMs,
                ["AnalyzedAt"] = DateTimeOffset.UtcNow,
                ["MetadataKeys"] = metadataKeys,
                ["MetadataValues"] = metadataValues,
                ["carInsurance"] = "", // Inicialmente vacío, se actualizará cuando se procesen documentos de seguro
                ["carLoan"] = "", // Inicialmente vacío, se actualizará cuando se procesen documentos de préstamo
                ["carTitle"] = "", // Inicialmente vacío, se actualizará cuando se procesen documentos de título
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
                _logger.LogInformation("? Car analysis indexed successfully: DocumentId={DocumentId}", documentId);
                return new CarsIndexResult
                {
                    Success = true,
                    Message = "Car analysis indexed successfully",
                    IndexName = CarsIndexName,
                    DocumentId = documentId,
                    HasVectorSearch = embeddings != null,
                    HasSemanticSearch = true
                };
            }
            else
            {
                var errorMessage = firstResult?.ErrorMessage ?? "Unknown indexing error";
                _logger.LogError("? Failed to index car analysis: {Error}", errorMessage);
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"Failed to index car analysis: {errorMessage}",
                    DocumentId = documentId
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error indexing car analysis");
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error indexing car analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get car by ID from search index
    /// </summary>
    public async Task<CarsIndexGetResult> GetCarByIdAsync(string carId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexGetResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("?? Getting car by ID: {CarId} for TwinId: {TwinId}", carId, twinId);

            // Create search client
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));

            // Build search options with filters
            var searchOptions = new SearchOptions
            {
                Filter = $"CarID eq '{carId}' and TwinID eq '{twinId}'",
                Size = 1,
                IncludeTotalCount = true
            };

            // Add field selection for the response
            var fieldsToSelect = new[]
            {
                "id", "CarID", "TwinID", "Success", "ExecutiveSummary", "DetaileHTMLReport",
                "ProcessingTimeMS", "AnalyzedAt", "MetadataKeys", "MetadataValues",
                "contenidoCompleto", "carInsurance", "carLoan", "carTitle"
            };
            foreach (var field in fieldsToSelect)
            {
                searchOptions.Select.Add(field);
            }

            // Perform the search using wildcard query to match any document with the filters
            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);

            // Process results
            CarsSearchResultItem? carItem = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                carItem = new CarsSearchResultItem
                {
                    Id = result.Document.GetString("id") ?? string.Empty,
                    CarID = result.Document.GetString("CarID") ?? string.Empty,
                    TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                    ExecutiveSummary = result.Document.GetString("ExecutiveSummary") ?? string.Empty,
                    Success = result.Document.GetBoolean("Success") ?? false,
                    ProcessingTimeMS = result.Document.GetDouble("ProcessingTimeMS") ?? 0.0,
                    AnalyzedAt = result.Document.GetDateTimeOffset("AnalyzedAt") ?? DateTimeOffset.MinValue,
                    DetaileHTMLReport = result.Document.GetString("DetaileHTMLReport") ?? string.Empty,
                    CarInsurance = result.Document.GetString("carInsurance") ?? string.Empty,
                    CarLoan = result.Document.GetString("carLoan") ?? string.Empty,
                    CarTitle = result.Document.GetString("carTitle") ?? string.Empty,
                    SearchScore = result.Score ?? 0.0
                };
                
                // We only expect one result due to the specific filter
                break;
            }

            if (carItem != null)
            {
                _logger.LogInformation("? Car found for CarID: {CarId}", carId);
                
                return new CarsIndexGetResult
                {
                    Success = true,
                    CarID = carId,
                    TwinID = twinId,
                    CarSearchResults = carItem,
                    Message = $"Car retrieved successfully for ID {carId}"
                };
            }
            else
            {
                _logger.LogInformation("?? No car found for CarID: {CarId}, TwinID: {TwinId}", carId, twinId);
                
                return new CarsIndexGetResult
                {
                    Success = false,
                    CarID = carId,
                    TwinID = twinId,
                    Error = $"No car found with ID {carId}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting car for CarID: {CarId}, TwinID: {TwinId}", carId, twinId);
            
            return new CarsIndexGetResult
            {
                Success = false,
                CarID = carId,
                TwinID = twinId,
                Error = $"Error retrieving car: {ex.Message}"
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
                _logger.LogWarning("?? Embedding client not available, skipping vector generation");
                return null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("?? Text content is empty, skipping vector generation");
                return null;
            }

            // Truncate text if too long (Azure OpenAI has token limits)
            if (text.Length > 8000)
            {
                text = text.Substring(0, 8000);
                _logger.LogInformation("?? Text truncated to 8000 characters for embedding generation");
            }

            _logger.LogDebug("?? Generating embeddings for text: {Length} characters", text.Length);

            var embeddingOptions = new EmbeddingGenerationOptions
            {
                Dimensions = EmbeddingDimensions
            };

            var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
            var embeddings = embedding.Value.ToFloats().ToArray();

            _logger.LogInformation("? Generated embedding vector with {Dimensions} dimensions", embeddings.Length);
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Failed to generate embeddings, continuing without vector search");
            return null;
        }
    }

    /// <summary>
    /// Parse AI response JSON to extract components
    /// </summary>
    private (string executiveSummary, string detailedHtmlReport, string detalleTexto, Dictionary<string, object> metadata) ParseAIResponse(string aiJsonResponse)
    {
        try
        {
            // Clean response
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

            var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(cleanResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (responseData == null)
            {
                throw new InvalidOperationException("Failed to parse AI response JSON");
            }

            var executiveSummary = GetStringFromElement(responseData, "executiveSummary", "Análisis completo de vehículo procesado por AI");
            var detailedHtmlReport = GetStringFromElement(responseData, "detailedHtmlReport", "<div>Reporte de análisis de vehículo</div>");
            var detalleTexto = GetStringFromElement(responseData, "detalleTexto", "{}");
            
            var metadata = new Dictionary<string, object>();
            if (responseData.TryGetValue("metadata", out var metadataObj))
            {
                if (metadataObj is JsonElement metadataElement && metadataElement.ValueKind == JsonValueKind.Object)
                {
                    metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataElement.GetRawText()) ?? new Dictionary<string, object>();
                }
            }

            return (executiveSummary, detailedHtmlReport, detalleTexto, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error parsing AI response, using fallback values");
            return ("Análisis de vehículo procesado", "<div>Reporte generado automáticamente</div>", "{}", new Dictionary<string, object>());
        }
    }

    /// <summary>
    /// Helper method to safely extract string from JsonElement
    /// </summary>
    private string GetStringFromElement(Dictionary<string, object> data, string key, string defaultValue)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? defaultValue;
            }
            return value?.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Build complete content for vector search
    /// </summary>
    private static string BuildCompleteCarContent(string executiveSummary, string detalleTexto, Dictionary<string, object> metadata)
    {
        var content = new StringBuilder();
        
        content.AppendLine("RESUMEN EJECUTIVO:");
        content.AppendLine(executiveSummary);
        content.AppendLine();
        
        content.AppendLine("DETALLES TÉCNICOS:");
        content.AppendLine(detalleTexto);
        content.AppendLine();
        
        if (metadata.Any())
        {
            content.AppendLine("METADATOS:");
            foreach (var kvp in metadata)
            {
                content.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
        }
        
        return content.ToString();
    }

    /// <summary>
    /// Build metadata keys for search filtering
    /// </summary>
    private static string BuildCarMetadataKeys(Dictionary<string, object> metadata)
    {
        if (metadata == null || !metadata.Any())
            return "vehicleType,makeModel,year,vehicleValue";
            
        var keys = metadata.Keys.ToList();
        keys.AddRange(new[] { "vehicleType", "makeModel", "year", "vehicleValue" });
        
        return string.Join(",", keys.Distinct());
    }

    /// <summary>
    /// Build metadata values for search content
    /// </summary>
    private static string BuildCarMetadataValues(Dictionary<string, object> metadata)
    {
        if (metadata == null || !metadata.Any())
            return "Vehículo analizado con AI";
            
        var values = metadata.Values.Select(v => v?.ToString() ?? "").Where(v => !string.IsNullOrEmpty(v));
        
        return string.Join(" ", values);
    }

    /// <summary>
    /// Generate unique document ID for car analysis
    /// </summary>
    private async Task<string> GenerateUniqueCarDocumentId(string carId, string twinId)
    {
        var baseId = $"car-{carId}-{twinId}";
        var documentId = baseId;
        var counter = 1;

        // Check if document exists and increment counter if needed
        while (await CarDocumentExistsAsync(documentId))
        {
            documentId = $"{baseId}-{counter}";
            counter++;
        }

        return documentId;
    }

    /// <summary>
    /// Check if a car document exists in the search index
    /// </summary>
    private async Task<bool> CarDocumentExistsAsync(string documentId)
    {
        try
        {
            if (!IsAvailable) return false;

            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));
            
            var response = await searchClient.GetDocumentAsync<Dictionary<string, object>>(documentId);
            return response != null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error checking car document existence for {DocumentId}", documentId);
            return false;
        }
    }

    /// <summary>
    /// Actualiza solo el campo carInsurance en el documento existente del índice de búsqueda
    /// </summary>
    /// <param name="carInsuranceAnalysis">Análisis HTML del seguro de auto</param>
    /// <param name="carId">ID del auto</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Resultado de la operación de actualización</returns>
    public async Task<CarsIndexResult> CarIndexInsuranceUpdate(string carInsuranceAnalysis, string carId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("???? Updating carInsurance field for CarID: {CarId}, TwinID: {TwinId}", carId, twinId);

            // Crear cliente de búsqueda
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));

            // PASO 1: Buscar el documento existente por CarID y TwinID
            _logger.LogInformation("?? Searching for existing document with CarID: {CarId}", carId);
            
            var searchOptions = new SearchOptions
            {
                Filter = $"CarID eq '{carId}' and TwinID eq '{twinId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            string? documentId = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                documentId = result.Document.GetString("id");
                _logger.LogInformation("? Found existing document: {DocumentId}", documentId);
                break;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"No existing document found for CarID: {carId} and TwinID: {twinId}"
                };
            }

            // PASO 2: Actualizar solo el campo carInsurance manteniendo los demás campos
            _logger.LogInformation("?? Updating carInsurance field in document: {DocumentId}", documentId);

            var updateDocument = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["carInsurance"] = carInsuranceAnalysis
            };

            // Usar MergeDocumentsAsync para actualizar solo el campo especificado
            var documents = new[] { new SearchDocument(updateDocument) };
            var uploadResult = await searchClient.MergeDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("? carInsurance field updated successfully: DocumentId={DocumentId}, CarID={CarId}", 
                    documentId, carId);
                    
                return new CarsIndexResult
                {
                    Success = true,
                    Message = $"carInsurance field updated successfully for CarID '{carId}'",
                    IndexName = CarsIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("? Error updating carInsurance field for CarID {CarId}: {Error}", 
                    carId, error.ErrorMessage);
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"Error updating carInsurance field: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating carInsurance field for CarID: {CarId}", carId);
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error updating carInsurance field: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Actualiza solo el campo carLoan en el documento existente del índice de búsqueda
    /// </summary>
    /// <param name="carLoanAnalysis">Análisis HTML del préstamo de auto</param>
    /// <param name="carId">ID del auto</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Resultado de la operación de actualización</returns>
    public async Task<CarsIndexResult> CarIndexLoanUpdate(string carLoanAnalysis, string carId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("???? Updating carLoan field for CarID: {CarId}, TwinID: {TwinId}", carId, twinId);

            // Crear cliente de búsqueda
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));

            // PASO 1: Buscar el documento existente por CarID y TwinID
            _logger.LogInformation("?? Searching for existing document with CarID: {CarId}", carId);
            
            var searchOptions = new SearchOptions
            {
                Filter = $"CarID eq '{carId}' and TwinID eq '{twinId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            string? documentId = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                documentId = result.Document.GetString("id");
                _logger.LogInformation("? Found existing document: {DocumentId}", documentId);
                break;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"No existing document found for CarID: {carId} and TwinID: {twinId}"
                };
            }

            // PASO 2: Actualizar solo el campo carLoan manteniendo los demás campos
            _logger.LogInformation("?? Updating carLoan field in document: {DocumentId}", documentId);

            var updateDocument = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["carLoan"] = carLoanAnalysis
            };

            // Usar MergeDocumentsAsync para actualizar solo el campo especificado
            var documents = new[] { new SearchDocument(updateDocument) };
            var uploadResult = await searchClient.MergeDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("? carLoan field updated successfully: DocumentId={DocumentId}, CarID={CarId}", 
                    documentId, carId);
                    
                return new CarsIndexResult
                {
                    Success = true,
                    Message = $"carLoan field updated successfully for CarID '{carId}'",
                    IndexName = CarsIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("? Error updating carLoan field for CarID {CarId}: {Error}", 
                    carId, error.ErrorMessage);
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"Error updating carLoan field: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating carLoan field for CarID: {CarId}", carId);
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error updating carLoan field: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Actualiza solo el campo carTitle en el documento existente del índice de búsqueda
    /// </summary>
    /// <param name="carTitleAnalysis">Análisis HTML del título de auto</param>
    /// <param name="carId">ID del auto</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Resultado de la operación de actualización</returns>
    public async Task<CarsIndexResult> CarIndexTitleUpdate(string carTitleAnalysis, string carId, string twinId)
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            _logger.LogInformation("???? Updating carTitle field for CarID: {CarId}, TwinID: {TwinId}", carId, twinId);

            // Crear cliente de búsqueda
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));

            // PASO 1: Buscar el documento existente por CarID y TwinID
            _logger.LogInformation("?? Searching for existing document with CarID: {CarId}", carId);
            
            var searchOptions = new SearchOptions
            {
                Filter = $"CarID eq '{carId}' and TwinID eq '{twinId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            string? documentId = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                documentId = result.Document.GetString("id");
                _logger.LogInformation("? Found existing document: {DocumentId}", documentId);
                break;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"No existing document found for CarID: {carId} and TwinID: {twinId}"
                };
            }

            // PASO 2: Actualizar solo el campo carTitle manteniendo los demás campos
            _logger.LogInformation("?? Updating carTitle field in document: {DocumentId}", documentId);

            var updateDocument = new Dictionary<string, object>
            {
                ["id"] = documentId,
                ["carTitle"] = carTitleAnalysis
            };

            // Usar MergeDocumentsAsync para actualizar solo el campo especificado
            var documents = new[] { new SearchDocument(updateDocument) };
            var uploadResult = await searchClient.MergeDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("? carTitle field updated successfully: DocumentId={DocumentId}, CarID={CarId}", 
                    documentId, carId);
                    
                return new CarsIndexResult
                {
                    Success = true,
                    Message = $"carTitle field updated successfully for CarID '{carId}'",
                    IndexName = CarsIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("? Error updating carTitle field for CarID {CarId}: {Error}", 
                    carId, error.ErrorMessage);
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"Error updating carTitle field: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating carTitle field for CarID: {CarId}", carId);
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error updating carTitle field: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Método genérico para actualizar múltiples campos de documentos de un vehículo en el índice de búsqueda
    /// </summary>
    /// <param name="carId">ID del auto</param>
    /// <param name="twinId">ID del Twin</param>
    /// <param name="fieldsToUpdate">Diccionario con los campos y valores a actualizar</param>
    /// <returns>Resultado de la operación de actualización</returns>
    public async Task<CarsIndexResult> CarIndexUpdateFields(string carId, string twinId, Dictionary<string, string> fieldsToUpdate)
    {
        try
        {
            if (!IsAvailable)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "Azure Search service not available"
                };
            }

            if (fieldsToUpdate == null || !fieldsToUpdate.Any())
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "No fields to update provided"
                };
            }

            _logger.LogInformation("???? Updating multiple fields for CarID: {CarId}, TwinID: {TwinId}, Fields: {FieldNames}", 
                carId, twinId, string.Join(", ", fieldsToUpdate.Keys));

            // Crear cliente de búsqueda
            var searchClient = new SearchClient(new Uri(_searchEndpoint!), CarsIndexName, new AzureKeyCredential(_searchApiKey!));

            // PASO 1: Buscar el documento existente por CarID y TwinID
            _logger.LogInformation("?? Searching for existing document with CarID: {CarId}", carId);
            
            var searchOptions = new SearchOptions
            {
                Filter = $"CarID eq '{carId}' and TwinID eq '{twinId}'",
                Size = 1,
                Select = { "id" }
            };

            var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            
            string? documentId = null;
            
            await foreach (var result in searchResponse.Value.GetResultsAsync())
            {
                documentId = result.Document.GetString("id");
                _logger.LogInformation("? Found existing document: {DocumentId}", documentId);
                break;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"No existing document found for CarID: {carId} and TwinID: {twinId}"
                };
            }

            // PASO 2: Construir documento de actualización con los campos especificados
            _logger.LogInformation("?? Updating multiple fields in document: {DocumentId}", documentId);

            var updateDocument = new Dictionary<string, object>
            {
                ["id"] = documentId
            };

            // Agregar los campos a actualizar
            foreach (var field in fieldsToUpdate)
            {
                updateDocument[field.Key] = field.Value;
            }

            // Usar MergeDocumentsAsync para actualizar solo los campos especificados
            var documents = new[] { new SearchDocument(updateDocument) };
            var uploadResult = await searchClient.MergeDocumentsAsync(documents);

            var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

            if (!errors.Any())
            {
                _logger.LogInformation("? Multiple fields updated successfully: DocumentId={DocumentId}, CarID={CarId}, Fields={FieldNames}", 
                    documentId, carId, string.Join(", ", fieldsToUpdate.Keys));
                    
                return new CarsIndexResult
                {
                    Success = true,
                    Message = $"Fields [{string.Join(", ", fieldsToUpdate.Keys)}] updated successfully for CarID '{carId}'",
                    IndexName = CarsIndexName,
                    DocumentId = documentId
                };
            }
            else
            {
                var error = errors.First();
                _logger.LogError("? Error updating multiple fields for CarID {CarId}: {Error}", 
                    carId, error.ErrorMessage);
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"Error updating fields: {error.ErrorMessage}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating multiple fields for CarID: {CarId}", carId);
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error updating fields: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Ejemplo de método de conveniencia para actualizar análisis de seguro de auto específico
    /// Combina la búsqueda del auto con la actualización del campo de seguro
    /// </summary>
    /// <param name="carId">ID del auto</param>
    /// <param name="twinId">ID del Twin</param>
    /// <param name="insuranceAnalysisHtml">HTML del análisis de seguro</param>
    /// <returns>Resultado de la operación</returns>
    public async Task<CarsIndexResult> UpdateCarInsuranceAnalysisAsync(string carId, string twinId, string insuranceAnalysisHtml)
    {
        try
        {
            _logger.LogInformation("???? Starting car insurance analysis update for CarID: {CarId}", carId);

            // Primero verificar que el auto existe en el índice
            var existingCar = await GetCarByIdAsync(carId, twinId);
            
            if (!existingCar.Success)
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = $"Car not found in search index: {existingCar.Error}"
                };
            }

            // Actualizar el campo de seguro
            var updateResult = await CarIndexInsuranceUpdate(insuranceAnalysisHtml, carId, twinId);
            
            if (updateResult.Success)
            {
                _logger.LogInformation("? Car insurance analysis updated successfully for CarID: {CarId}", carId);
            }
            
            return updateResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating car insurance analysis for CarID: {CarId}", carId);
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error updating car insurance analysis: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Ejemplo de método para actualizar múltiples campos de documentos de vehículo de una vez
    /// Útil para procesamiento de múltiples documentos
    /// </summary>
    /// <param name="carId">ID del auto</param>
    /// <param name="twinId">ID del Twin</param>
    /// <param name="insuranceHtml">HTML del análisis de seguro (opcional)</param>
    /// <param name="loanHtml">HTML del análisis de préstamo (opcional)</param>
    /// <param name="titleHtml">HTML del análisis de título (opcional)</param>
    /// <returns>Resultado de la operación</returns>
    public async Task<CarsIndexResult> UpdateCarDocumentsAsync(string carId, string twinId, 
        string? insuranceHtml = null, string? loanHtml = null, string? titleHtml = null)
    {
        try
        {
            var fieldsToUpdate = new Dictionary<string, string>();
            
            if (!string.IsNullOrEmpty(insuranceHtml))
                fieldsToUpdate["carInsurance"] = insuranceHtml;
                
            if (!string.IsNullOrEmpty(loanHtml))
                fieldsToUpdate["carLoan"] = loanHtml;
                
            if (!string.IsNullOrEmpty(titleHtml))
                fieldsToUpdate["carTitle"] = titleHtml;

            if (!fieldsToUpdate.Any())
            {
                return new CarsIndexResult
                {
                    Success = false,
                    Error = "No document updates provided"
                };
            }

            _logger.LogInformation("???? Updating {Count} car document fields for CarID: {CarId}", 
                fieldsToUpdate.Count, carId);

            return await CarIndexUpdateFields(carId, twinId, fieldsToUpdate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating car documents for CarID: {CarId}", carId);
            return new CarsIndexResult
            {
                Success = false,
                Error = $"Error updating car documents: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Result class for cars index operations
/// </summary>
public class CarsIndexResult
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
/// Individual car search result item (equivalent to HomesSearchResultItem)
/// </summary>
public class CarsSearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string CarID { get; set; } = string.Empty; // Equivalent to HomeId
    public string TwinID { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double ProcessingTimeMS { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DetaileHTMLReport { get; set; }
    public string CarInsurance { get; set; } = string.Empty; // Car insurance documents
    public string CarLoan { get; set; } = string.Empty; // Car loan documents
    public string CarTitle { get; set; } = string.Empty; // Car title documents
    public double SearchScore { get; set; } 
}



public class CursosSearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string CursoEntryId { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double ProcessingTimeMs { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Transcript { get; set; }
    public double SearchScore { get; set; }
    public List<string> Highlights { get; set; } = new();
}
/// <summary>
/// Result class for getting a specific car by ID (equivalent to HomesGetResult)
/// </summary>
public class CarsIndexGetResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string CarID { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public CarsSearchResultItem? CarSearchResults { get; set; }
    public string Message { get; set; } = string.Empty;
}