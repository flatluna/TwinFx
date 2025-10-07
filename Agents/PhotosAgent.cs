using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Services;
using TwinFx.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TwinFx.Agents;

/// <summary>
/// Photo metadata for storage and transfer
/// </summary>
public class PhotoMetadata
{
    /// <summary>
    /// Unique photo ID
    /// </summary>
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID who owns the photo
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Photo description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Date when photo was taken
    /// </summary>
    public string DateTaken { get; set; } = string.Empty;

    /// <summary>
    /// Location where photo was taken
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// People in the photo
    /// </summary>
    public string PeopleInPhoto { get; set; } = string.Empty;

    /// <summary>
    /// Photo category
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Photo tags
    /// </summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>
    /// File path in storage
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// File name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Country where photo was taken
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// MIME type
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Upload date
    /// </summary>
    public DateTime UploadDate { get; set; }

    /// <summary>
    /// SAS URL for accessing the photo (24-hour expiration)
    /// </summary>
    public string? SasUrl { get; set; }
}

public class PhotosAgent
{
    private readonly ILogger<PhotosAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly CosmosDbService _cosmosService;
    private readonly DataLakeClientFactory _dataLakeFactory;
    private Kernel? _kernel;

    public PhotosAgent(ILogger<PhotosAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Use compatibility extension methods
        _cosmosService = _configuration.CreateCosmosService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
        
        _logger.LogInformation("📸 PhotosAgent initialized");
    }

    /// <summary>
    /// Process photo-related questions and searches
    /// </summary>
    public async Task<string> ProcessPhotoQuestionAsync(string question, string twinId, bool requiresAnalysis = false, bool requiresFiltering = false)
    {
        try
        {
            _logger.LogInformation("📸 Processing photo question: {Question} for Twin ID: {TwinId}, RequiresAnalysis: {RequiresAnalysis}, RequiresFiltering: {RequiresFiltering}", 
                question, twinId, requiresAnalysis, requiresFiltering);

            List<PhotoMetadata> photoDocuments;

            if (requiresFiltering)
            {
                // Apply filters based on the question
                var searchParams = await ExtractSearchParametersAsync(question, twinId);
                photoDocuments = await GetFilteredPhotosAsync(twinId, searchParams);
            }
            else
            {
                // Get all photos
                var allPhotosResult = await GetPhotosAsync(twinId);
                photoDocuments = allPhotosResult.Success ? allPhotosResult.Photos : new List<PhotoMetadata>();
            }
            
            if (photoDocuments.Count == 0)
            {
                return $"📭 No se encontraron fotos para el Twin ID: {twinId}" + 
                       (requiresFiltering ? " con los filtros especificados" : "");
            }

            string rawResult;
            
            if (requiresAnalysis)
            {
                // Complex analysis with AI
                _logger.LogInformation("🧮 Using AI for complex photo analysis");
                rawResult = await GenerateAIPhotoAnalysisAsync(photoDocuments, question);
            }
            else
            {
                // Simple display of photo information
                _logger.LogInformation("📊 Using simple photo display without complex analysis");
                rawResult = await GenerateDirectPhotoResponseAsync(photoDocuments, question);
            }

            // Enhance the response to make it more user-friendly
            var enhancedResult = await EnhancePhotoResponseWithAIAsync(rawResult, question, photoDocuments);

            var finalResult = $"""
📸 **Análisis de Fotos**

{enhancedResult}

📈 **Resumen:**
   • Twin ID: {twinId}
   • Total de fotos: {photoDocuments.Count}
   • Categorías: {GetCategoriesFromPhotos(photoDocuments)}
   • Rango de fechas: {GetDateRangeFromPhotos(photoDocuments)}
   • Tamaño total: {FormatFileSize(photoDocuments.Sum(p => p.FileSize))}
   • Filtros aplicados: {(requiresFiltering ? "Sí" : "No")}
   • Análisis avanzado: {(requiresAnalysis ? "Sí" : "No")}
""";

            _logger.LogInformation("✅ Photo question processed successfully");
            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing photo question");
            return $"❌ Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Save photo metadata to Cosmos DB
    /// </summary>
    public async Task<PhotoSaveResult> SavePhotoMetadataAsync(PhotoMetadata metadata)
    {
        try
        {
            _logger.LogInformation("💾 Saving photo metadata to Cosmos DB for photo: {PhotoId}", metadata.PhotoId);

            // Create document for Cosmos DB using top-level PhotoDocument
            var photoDocument = new TwinFx.Services.PhotoDocument
            {
                Id = metadata.PhotoId,
                TwinId = metadata.TwinId,
                PhotoId = metadata.PhotoId,
                Description = metadata.Description,
                DateTaken = metadata.DateTaken,
                Location = metadata.Location,
                PeopleInPhoto = metadata.PeopleInPhoto,
                Category = metadata.Category,
                Tags = metadata.Tags?.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>(),
                FilePath = metadata.FilePath,
                FileName = metadata.FileName,
                FileSize = metadata.FileSize,
                Country = metadata.Country ?? "",
                MimeType = metadata.MimeType,
                UploadDate = metadata.UploadDate,
                CreatedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow
            };

            // Save to Cosmos DB (using the same pattern as invoices)
            var success = await _cosmosService.SavePhotoDocumentAsync(photoDocument);

            if (success)
            {
                _logger.LogInformation("✅ Photo metadata saved successfully to Cosmos DB");
                return new PhotoSaveResult { Success = true };
            }
            else
            {
                _logger.LogError("❌ Failed to save photo metadata to Cosmos DB");
                return new PhotoSaveResult 
                { 
                    Success = false, 
                    ErrorMessage = "Failed to save to Cosmos DB" 
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error saving photo metadata");
            return new PhotoSaveResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    /// <summary>
    /// Get photos for a twin with optional filtering
    /// </summary>
    public async Task<PhotosResult> GetPhotosAsync(string twinId, string? category = null, string? search = null)
    {
        try
        {
            _logger.LogInformation("📋 Getting photos for Twin ID: {TwinId}, Category: {Category}, Search: {Search}", 
                twinId, category, search);

            // Get photos from Cosmos DB (returns object list)
            var photoObjects = await _cosmosService.GetPhotoDocumentsByTwinIdAsync(twinId);
            
            // Convert objects to PhotoDocument
            var photoDocuments = ConvertObjectsToPhotoDocuments(photoObjects);

            // Apply filters if specified
            if (!string.IsNullOrEmpty(category))
            {
                photoDocuments = photoDocuments.Where(p => 
                    p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLowerInvariant();
                photoDocuments = photoDocuments.Where(p => 
                    p.Description.ToLowerInvariant().Contains(searchLower) ||
                    p.Location.ToLowerInvariant().Contains(searchLower) ||
                    p.PeopleInPhoto.ToLowerInvariant().Contains(searchLower) ||
                    p.Tags.Any(t => t.ToLowerInvariant().Contains(searchLower)) ||
                    p.Category.ToLowerInvariant().Contains(searchLower)).ToList();
            }

            // Convert to PhotoMetadata and generate SAS URLs
            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            var photos = new List<PhotoMetadata>();

            foreach (var doc in photoDocuments)
            {
                try
                {
                    // Generate SAS URL for each photo
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(doc.FilePath, TimeSpan.FromHours(24));
                    
                    var metadata = new PhotoMetadata
                    {
                        PhotoId = doc.PhotoId,
                        TwinId = doc.TwinId,
                        Description = doc.Description,
                        DateTaken = doc.DateTaken,
                        Location = doc.Location,
                        PeopleInPhoto = doc.PeopleInPhoto,
                        Category = doc.Category,
                        Tags = string.Join(", ", doc.Tags),
                        FilePath = doc.FilePath,
                        FileName = doc.FileName,
                        FileSize = doc.FileSize,
                        Country = doc.Country,
                        MimeType = doc.MimeType,
                        UploadDate = doc.UploadDate,
                        SasUrl = sasUrl  // ✅ Store the SAS URL
                    };

                    photos.Add(metadata);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to generate SAS URL for photo: {PhotoId}", doc.PhotoId);
                    
                    // Add photo without SAS URL rather than skip it entirely
                    var metadata = new PhotoMetadata
                    {
                        PhotoId = doc.PhotoId,
                        TwinId = doc.TwinId,
                        Description = doc.Description,
                        DateTaken = doc.DateTaken,
                        Location = doc.Location,
                        PeopleInPhoto = doc.PeopleInPhoto,
                        Category = doc.Category,
                        Tags = string.Join(", ", doc.Tags),
                        FilePath = doc.FilePath,
                        FileName = doc.FileName,
                        FileSize = doc.FileSize,
                        Country = doc.Country,
                        MimeType = doc.MimeType,
                        UploadDate = doc.UploadDate,
                        SasUrl = null  // No SAS URL available
                    };

                    photos.Add(metadata);
                }
            }

            _logger.LogInformation("✅ Retrieved {Count} photos for Twin ID: {TwinId}", photos.Count, twinId);
            
            return new PhotosResult 
            { 
                Success = true, 
                Photos = photos 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting photos for Twin ID: {TwinId}", twinId);
            return new PhotosResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message, 
                Photos = new List<PhotoMetadata>() 
            };
        }
    }

    /// <summary>
    /// Get filtered photos based on search parameters
    /// </summary>
    private async Task<List<PhotoMetadata>> GetFilteredPhotosAsync(string twinId, PhotoSearchParameters searchParams)
    {
        try
        {
            // ✅ NEW: Try smart SQL filtering first, fallback to in-memory filtering
            List<TwinFx.Services.PhotoDocument> photoDocuments;
            
            // Check if we have meaningful search criteria for SQL filtering
            if (HasMeaningfulSearchCriteria(searchParams))
            {
                _logger.LogInformation("🔍 Using smart Cosmos DB SQL filtering");
                
                // Generate smart SQL filter using OpenAI
                var sqlFilter = await GenerateCosmosDBPhotoFilterAsync(searchParams, twinId);
                
                // Get filtered photos directly from Cosmos DB
                var photoObjects = await _cosmosService.GetFilteredPhotoDocumentsAsync(twinId, null, sqlFilter);
                photoDocuments = ConvertObjectsToPhotoDocuments(photoObjects);
                
                _logger.LogInformation("✅ Cosmos DB filtering returned {Count} photos", photoDocuments.Count);
            }
            else
            {
                _logger.LogInformation("📊 Using fallback in-memory filtering");
                
                // Fallback to getting all photos first
                var allPhotosResult = await GetPhotosAsync(twinId);
                if (!allPhotosResult.Success)
                    return new List<PhotoMetadata>();

                var filteredPhotos = allPhotosResult.Photos.AsEnumerable();

                // Apply category filter
                if (!string.IsNullOrEmpty(searchParams.Category))
                {
                    filteredPhotos = filteredPhotos.Where(p => 
                        p.Category.Equals(searchParams.Category, StringComparison.OrdinalIgnoreCase));
                }

                // Apply text search filter
                if (!string.IsNullOrEmpty(searchParams.SearchText))
                {
                    var searchLower = searchParams.SearchText.ToLowerInvariant();
                    filteredPhotos = filteredPhotos.Where(p => 
                        p.Description.ToLowerInvariant().Contains(searchLower) ||
                        p.Location.ToLowerInvariant().Contains(searchLower) ||
                        p.PeopleInPhoto.ToLowerInvariant().Contains(searchLower) ||
                        p.Tags.ToLowerInvariant().Contains(searchLower));
                }

                // Apply date filter
                if (searchParams.FromDate.HasValue)
                {
                    filteredPhotos = filteredPhotos.Where(p => 
                        DateTime.TryParse(p.DateTaken, out var photoDate) && photoDate >= searchParams.FromDate.Value);
                }

                if (searchParams.ToDate.HasValue)
                {
                    filteredPhotos = filteredPhotos.Where(p => 
                        DateTime.TryParse(p.DateTaken, out var photoDate) && photoDate <= searchParams.ToDate.Value);
                }

                // Apply people filter
                if (!string.IsNullOrEmpty(searchParams.PeopleInPhoto))
                {
                    var peopleLower = searchParams.PeopleInPhoto.ToLowerInvariant();
                    filteredPhotos = filteredPhotos.Where(p => 
                        p.PeopleInPhoto.ToLowerInvariant().Contains(peopleLower));
                }

                return filteredPhotos.ToList();
            }

            // Convert PhotoDocuments to PhotoMetadata with SAS URLs
            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            var photos = new List<PhotoMetadata>();

            foreach (var doc in photoDocuments)
            {
                try
                {
                    // Generate SAS URL for each photo
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(doc.FilePath, TimeSpan.FromHours(24));
                    
                    var metadata = new PhotoMetadata
                    {
                        PhotoId = doc.PhotoId,
                        TwinId = doc.TwinId,
                        Description = doc.Description,
                        DateTaken = doc.DateTaken,
                        Location = doc.Location,
                        PeopleInPhoto = doc.PeopleInPhoto,
                        Category = doc.Category,
                        Tags = string.Join(", ", doc.Tags),
                        FilePath = doc.FilePath,
                        FileName = doc.FileName,
                        FileSize = doc.FileSize,
                        Country = doc.Country,
                        MimeType = doc.MimeType,
                        UploadDate = doc.UploadDate,
                        SasUrl = sasUrl
                    };

                    photos.Add(metadata);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to generate SAS URL for photo: {PhotoId}", doc.PhotoId);
                    
                    // Add photo without SAS URL rather than skip it entirely
                    var metadata = new PhotoMetadata
                    {
                        PhotoId = doc.PhotoId,
                        TwinId = doc.TwinId,
                        Description = doc.Description,
                        DateTaken = doc.DateTaken,
                        Location = doc.Location,
                        PeopleInPhoto = doc.PeopleInPhoto,
                        Category = doc.Category,
                        Tags = string.Join(", ", doc.Tags),
                        FilePath = doc.FilePath,
                        FileName = doc.FileName,
                        FileSize = doc.FileSize,
                        Country = doc.Country,
                        MimeType = doc.MimeType,
                        UploadDate = doc.UploadDate,
                        SasUrl = null
                    };

                    photos.Add(metadata);
                }
            }

            return photos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error filtering photos");
            return new List<PhotoMetadata>();
        }
    }

    /// <summary>
    /// Extract search parameters from natural language question using AI
    /// </summary>
    private async Task<PhotoSearchParameters> ExtractSearchParametersAsync(string question, string twinId)
    {
        try
        {
            _logger.LogInformation("🧠 Extracting search parameters from question: {Question}", question);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            var extractionPrompt = $$"""
Analiza la siguiente pregunta sobre fotos y extrae los parámetros de búsqueda de manera inteligente:

PREGUNTA: {{question}}

Extrae los siguientes parámetros si están presentes en la pregunta:

1. CATEGORÍA: Busca palabras como "familia", "viajes", "trabajo", "vacaciones", etc.
2. TEXTO DE BÚSQUEDA: Palabras clave principales para buscar en descripción, tags y otros campos
3. PERSONAS: Nombres de personas mencionadas en la pregunta (incluye variaciones como "Karla", "karlita")
4. FECHAS: Rangos de fechas o fechas específicas
5. UBICACIÓN: Lugares mencionados

⭐ **REGLAS ESPECIALES PARA PERSONAS:**
- Si encuentras nombres como "Karla", también considera variaciones como "karlita"
- Si encuentras frases como "de chiquita", "de pequeña", agrégalas al texto de búsqueda
- Para búsquedas de personas, extrae tanto el nombre como descriptivos relacionados

🎯 **EJEMPLOS ESPECÍFICOS:**
- "Encuentra fotos de Karla de chiquita" → {"peopleInPhoto": "Karla", "searchText": "karla karlita chiquita pequeña"}
- "Busca a Juan cuando era niño" → {"peopleInPhoto": "Juan", "searchText": "juan niño pequeño"}
- "Fotos de María de joven" → {"peopleInPhoto": "María", "searchText": "maría maria joven"}

FORMATO DE RESPUESTA (JSON):
{
  "category": "categoría encontrada o null",
  "searchText": "palabras clave para búsqueda amplia o null", 
  "peopleInPhoto": "personas mencionadas o null",
  "fromDate": "fecha desde en formato yyyy-mm-dd o null",
  "toDate": "fecha hasta en formato yyyy-mm-dd o null",
  "location": "ubicación mencionada o null"
}

EJEMPLOS COMPLETOS:
- "Muéstrame fotos de familia" → {"category": "familia"}
- "Fotos de vacaciones en Miami" → {"category": "vacaciones", "location": "Miami"}
- "Encuentra fotos de Karla de chiquita" → {"peopleInPhoto": "Karla", "searchText": "karla karlita chiquita pequeña"}
- "Busca a María cuando era adolescente" → {"peopleInPhoto": "María", "searchText": "maría maria adolescente teenager joven"}
- "Fotos con Pedro del año pasado" → {"peopleInPhoto": "Pedro", "fromDate": "2024-01-01", "toDate": "2024-12-31"}

Responde ÚNICAMENTE con el JSON válido, sin explicaciones adicionales.
""";
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(extractionPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 400 },  // Increased for more complex extraction
                    { "temperature", 0.1 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var jsonResponse = response.Content?.Trim() ?? "{}";
            
            // Clean any markdown formatting
            if (jsonResponse.StartsWith("```json"))
                jsonResponse = jsonResponse.Substring(7).Trim();
            if (jsonResponse.StartsWith("```"))
                jsonResponse = jsonResponse.Substring(3).Trim();
            if (jsonResponse.EndsWith("```"))
                jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3).Trim();

            // Parse the JSON response
            var searchParams = JsonSerializer.Deserialize<PhotoSearchParameters>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new PhotoSearchParameters();

            _logger.LogInformation("✅ Extracted enhanced search parameters: {Parameters}", JsonSerializer.Serialize(searchParams));
            return searchParams;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error extracting search parameters");
            return new PhotoSearchParameters(); // Return empty parameters
        }
    }

    /// <summary>
    /// Generate AI-powered photo analysis
    /// </summary>
    private async Task<string> GenerateAIPhotoAnalysisAsync(List<PhotoMetadata> photos, string question)
    {
        try
        {
            _logger.LogInformation("🤖 Generating AI photo analysis for {Count} photos", photos.Count);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Create comprehensive photo data for AI analysis
            var photoDataJson = JsonSerializer.Serialize(photos.Select(p => new
            {
                PhotoId = p.PhotoId,
                Description = p.Description,
                DateTaken = p.DateTaken,
                Location = p.Location,
                PeopleInPhoto = p.PeopleInPhoto,
                Category = p.Category,
                Tags = p.Tags,
                FileName = p.FileName,
                FileSize = p.FileSize,
                UploadDate = p.UploadDate,
                SasUrl = p.SasUrl  // ✅ Include SAS URL in the data sent to AI
            }), new JsonSerializerOptions { WriteIndented = true });

            var analysisPrompt = $"""
Eres un analista experto de colecciones de fotos. El usuario ha hecho esta pregunta sobre sus fotos:

PREGUNTA: {question}

DATOS DE FOTOS (JSON):
{photoDataJson}

INSTRUCCIONES CRÍTICAS PARA MOSTRAR FOTOS:
1. Analiza los datos de las fotos en detalle
2. Responde la pregunta específica del usuario
3. Presenta los datos de manera clara y organizada en formato HTML profesional
4. ✅ **IMPORTANTE: Muestra las fotos usando las SAS URLs proporcionadas**
5. ✅ **SOLO incluye columnas con datos relevantes (no vacías)**
6. ✅ **Headers con texto OSCURO y fondo claro para buena legibilidad**
7. Utiliza colores, emojis y formato HTML elegante
8. Crea tablas y listas cuando sea apropiado
9. Destaca patrones interesantes en la colección de fotos
10. Incluye insights sobre las fotos (frecuencia por categoría, lugares favoritos, etc.)
11. Mantén un tono personal y amigable
12. Si hay fotos de personas específicas, menciona los patrones

🖼️ **FORMATO PARA MOSTRAR FOTOS:**
Para cada foto que tenga SasUrl, incluye la imagen así:
```html
<img src="[SasUrl]" alt="[FileName]" style="width: 120px; height: 120px; object-fit: cover; border-radius: 8px; margin: 5px; box-shadow: 0 2px 8px rgba(0,0,0,0.15);" title="[Description] - [DateTaken]">
```

📊 **REGLAS PARA TABLAS INTELIGENTES:**
- ✅ **SIEMPRE incluir**: 📸 Foto, 🏷️ Categoría, 📅 Fecha, 📁 Archivo
- ✅ **Incluir SOLO si hay datos**: 📝 Descripción, 📍 Ubicación, 👥 Personas, 🏷️ Tags
- ❌ **NO incluir columnas vacías o con muy pocos datos**

📊 **EJEMPLO CORRECTO - Headers Legibles:**
```html
<table style="width: 100%; border-collapse: collapse; font-family: 'Segoe UI', Arial, sans-serif; margin: 20px 0;">
<tr style="background: #f0f2f5;">
    <th style="padding: 12px; border: 1px solid #ddd; color: #2c3e50; font-weight: 600; text-align: center;">📸 Foto</th>
    <th style="padding: 12px; border: 1px solid #ddd; color: #2c3e50; font-weight: 600;">🏷️ Categoría</th>
    <th style="padding: 12px; border: 1px solid #ddd; color: #2c3e50; font-weight: 600;">📅 Fecha</th>
    <th style="padding: 12px; border: 1px solid #ddd; color: #2c3e50; font-weight: 600;">📁 Archivo</th>
    <!-- SOLO incluir si tienen datos relevantes -->
    <th style="padding: 12px; border: 1px solid #ddd; color: #2c3e50; font-weight: 600;">📝 Descripción</th>
</tr>
<!-- Repetir filas para cada foto -->
</table>
```

🎨 **CAMPOS DISPONIBLES REALES:**
Según el JSON de Cosmos DB, estos son los campos disponibles:
- ✅ **photoId**: ID único
- ✅ **description**: Descripción (puede estar vacía "")
- ✅ **dateTaken**: Fecha (formato: "2025-08-29")
- ✅ **location**: Ubicación (puede estar vacía "")
- ✅ **peopleInPhoto**: Personas (puede estar vacía "")
- ✅ **category**: Categoría (ej: "Familia")
- ✅ **tags**: Array de tags (puede estar vacío [])
- ✅ **fileName**: Nombre del archivo
- ✅ **fileSize**: Tamaño en bytes
- ✅ **mimeType**: Tipo (ej: "image/png")

TIPOS DE ANÁLISIS QUE PUEDES HACER:
- Análisis por categorías (familia, viajes, trabajo, etc.)
- Análisis temporal (fotos por mes/año, tendencias)
- Análisis de ubicaciones (lugares más fotografiados)
- Análisis de personas (quién aparece más en las fotos)
- Análisis de tags (temas más frecuentes)
- Estadísticas de almacenamiento (tamaños, formatos)

FORMATO DE RESPUESTA:
- Usa HTML con estilos CSS inline para colores y formato
- ✅ **Headers con color oscuro (#2c3e50) y fondo claro (#f0f2f5)**
- ✅ **SIEMPRE incluye las miniaturas de fotos cuando SasUrl esté disponible**
- ✅ **SOLO columnas con datos útiles - no mostrar campos vacíos**
- Crea visualizaciones de datos con tablas inteligentes
- Usa listas para organizar información
- Incluye emojis relevantes para fotos
- Destaca información importante con colores y negritas

Responde directamente con el análisis HTML completo y detallado con las fotos mostradas y SOLO las columnas relevantes.
""";
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(analysisPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 4000 },
                    { "temperature", 0.3 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var analysisResult = response.Content ?? "No se pudo generar análisis de fotos.";
            
            _logger.LogInformation("✅ AI photo analysis generated successfully");
            return analysisResult.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating AI photo analysis");
            return GenerateBasicPhotoSummary(photos, question);
        }
    }

    /// <summary>
    /// Generate direct photo response without complex AI analysis
    /// </summary>
    private async Task<string> GenerateDirectPhotoResponseAsync(List<PhotoMetadata> photos, string question)
    {
        try
        {
            _logger.LogInformation("📊 Generating direct photo response for {Count} photos", photos.Count);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            var photoContext = GeneratePhotoContextWithUrls(photos);
            
            var directPrompt = $"""
Eres un organizador de fotos experto. El usuario ha hecho esta pregunta sobre sus fotos:

PREGUNTA: {question}

CONTEXTO DE FOTOS:
{photoContext}

INSTRUCCIONES CRÍTICAS:
1. Presenta las fotos encontradas de manera clara y organizada
2. IMPORTANTE: Muestra las fotos usando las SAS URLs proporcionadas
3. SOLO incluye columnas con datos relevantes (no vacías)
4. Headers con texto OSCURO y fondo claro para buena legibilidad
5. Usa formato HTML profesional con colores y estilos
6. Organiza las fotos por categorías, fechas o como sea más relevante
7. Usa emojis relevantes para hacer la respuesta más amigable
8. Destaca información importante
9. Mantén un tono personal pero profesional
10. Si hay muchas fotos, organízalas en grupos lógicos

FORMATO PARA MOSTRAR FOTOS:
Para cada foto que tenga SAS URL, incluye la imagen así:
<img src="[SAS_URL]" alt="[FileName]" style="width: 120px; height: 120px; object-fit: cover; border-radius: 8px; margin: 5px; box-shadow: 0 2px 8px rgba(0,0,0,0.15);" title="[Description] - [DateTaken]">

REGLAS PARA COLUMNAS INTELIGENTES:
- SIEMPRE incluir: 📸 Foto, 🏷️ Categoría, 📅 Fecha, 📁 Archivo
- Incluir SOLO si hay datos: 📝 Descripción, 📍 Ubicación, 👥 Personas, 🏷️ Tags
- NO incluir columnas vacías o con muy pocos datos

Responde directamente con la presentación HTML de las fotos incluyendo las imágenes y SOLO las columnas relevantes.
""";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(directPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 3500 },
                    { "temperature", 0.2 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var directResult = response.Content ?? "No se pudo generar respuesta directa de fotos.";
            
            _logger.LogInformation("✅ Direct photo response generated successfully");
            return directResult.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating direct photo response");
            return GenerateBasicPhotoSummary(photos, question);
        }
    }

    /// <summary>
    /// Generate Cosmos DB filter SQL for photo search using OpenAI
    /// </summary>
    private async Task<string> GenerateCosmosDBPhotoFilterAsync(PhotoSearchParameters searchParams, string twinId)
    {
        try
        {
            _logger.LogInformation("🧠 Generando filtro SQL para Cosmos DB para fotos");

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Build the user query description from search parameters
            var queryDescription = BuildQueryDescription(searchParams);

            var filterPrompt = $$"""
Genera un filtro SQL para Cosmos DB para buscar fotos basado en los criterios de búsqueda.

CRITERIOS DE BÚSQUEDA: {{queryDescription}}

ESTRUCTURA DEL DOCUMENTO DE FOTOS EN COSMOS DB:
{
  "id": "string",
  "TwinID": "string",
  "photoId": "string",
  "description": "Foto de karlita de chiquita",
  "dateTaken": "2012-02-29",
  "location": "Tomada en virginia",
  "peopleInPhoto": "Karla Ruiz",
  "category": "Familia",
  "tags": ["karla", "teenager"],
  "fileName": "101018-210043-1.jpg",
  "filePath": "fotos/familia/reuniones/101018-210043-1.jpg",
  "fileSize": 93209,
  "mimeType": "image/jpeg",
  "uploadDate": "2025-08-29T17:33:09Z",
  "createdAt": "2025-08-29T17:33:09Z",
  "processedAt": "2025-08-29T17:33:09Z"
}

IMPORTANTE: Responde ÚNICAMENTE con la cláusula WHERE completa sin formato markdown.
""";
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(filterPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 500 },
                    { "temperature", 0.1 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var sqlFilter = response.Content?.Trim() ?? $"c.TwinID = '{twinId}'";
            
            // Clean any markdown formatting
            if (sqlFilter.StartsWith("```sql"))
                sqlFilter = sqlFilter.Substring(6).Trim();
            if (sqlFilter.StartsWith("```"))
                sqlFilter = sqlFilter.Substring(3).Trim();
            if (sqlFilter.EndsWith("```"))
                sqlFilter = sqlFilter.Substring(0, sqlFilter.Length - 3).Trim();
            
            sqlFilter = sqlFilter.Replace("\r", "").Replace("\n", " ").Trim();
            
            _logger.LogInformation("✅ Generated photo SQL filter: {SqlFilter}", sqlFilter);
            return sqlFilter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating Cosmos DB photo filter");
            return $"c.TwinID = '{twinId}'"; // Fallback to basic filter
        }
    }

    /// <summary>
    /// Check if search parameters have meaningful criteria for SQL filtering
    /// </summary>
    private static bool HasMeaningfulSearchCriteria(PhotoSearchParameters searchParams)
    {
        return !string.IsNullOrEmpty(searchParams.SearchText) ||
               !string.IsNullOrEmpty(searchParams.Category) ||
               !string.IsNullOrEmpty(searchParams.PeopleInPhoto) ||
               !string.IsNullOrEmpty(searchParams.Location) ||
               searchParams.FromDate.HasValue ||
               searchParams.ToDate.HasValue;
    }

    /// <summary>
    /// Build query description from search parameters for AI processing
    /// </summary>
    private string BuildQueryDescription(PhotoSearchParameters searchParams)
    {
        var descriptions = new List<string>();

        if (!string.IsNullOrEmpty(searchParams.SearchText))
            descriptions.Add($"texto general: '{searchParams.SearchText}'");
        
        if (!string.IsNullOrEmpty(searchParams.Category))
            descriptions.Add($"categoría: '{searchParams.Category}'");
        
        if (!string.IsNullOrEmpty(searchParams.PeopleInPhoto))
            descriptions.Add($"personas: '{searchParams.PeopleInPhoto}'");
        
        if (!string.IsNullOrEmpty(searchParams.Location))
            descriptions.Add($"ubicación: '{searchParams.Location}'");
        
        if (searchParams.FromDate.HasValue)
            descriptions.Add($"fecha desde: {searchParams.FromDate.Value:yyyy-MM-dd}");
        
        if (searchParams.ToDate.HasValue)
            descriptions.Add($"fecha hasta: {searchParams.ToDate.Value:yyyy-MM-dd}");

        return descriptions.Any() ? string.Join(", ", descriptions) : "búsqueda general";
    }

    /// <summary>
    /// Initialize Semantic Kernel for AI operations
    /// </summary>
    private async Task InitializeKernelAsync()
    {
        if (_kernel != null)
            return; // Already initialized

        try
        {
            // Create kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Get Azure OpenAI configuration
            var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                          _configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? 
                          throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

            var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                        _configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? 
                        throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

            var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                                _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ?? 
                                "gpt4mini";

            // Add Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Build the kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized for PhotosAgent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for PhotosAgent");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Generate a brief summary of the photos using basic information
    /// </summary>
    private string GenerateBasicPhotoSummary(List<PhotoMetadata> photos, string question)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"👀 **Resumen Rápido de Fotos** para la pregunta: *{question}*");
        sb.AppendLine();

        foreach (var photo in photos)
        {
            sb.AppendLine($"- 📸 Foto ID: {photo.PhotoId}");
            sb.AppendLine($"  - Descripción: {photo.Description}");
            sb.AppendLine($"  - Fecha: {photo.DateTaken}");
            sb.AppendLine($"  - Ubicación: {photo.Location}");
            sb.AppendLine($"  - Personas: {photo.PeopleInPhoto}");
            sb.AppendLine($"  - Categoría: {photo.Category}");
            sb.AppendLine($"  - Tags: {photo.Tags}");
            sb.AppendLine($"  - Tamaño: {FormatFileSize(photo.FileSize)}");
            sb.AppendLine($"  - URL Acceso: {photo.SasUrl}");
            sb.AppendLine();
        }

        sb.AppendLine($"📊 **Total de fotos analizadas:** {photos.Count}");
        
        return sb.ToString();
    }

    /// <summary>
    /// Enhance the photo response using AI for better readability and insights
    /// </summary>
    private async Task<string> EnhancePhotoResponseWithAIAsync(string rawResponse, string question, List<PhotoMetadata> photos)
    {
        try
        {
            _logger.LogInformation("✨ Enhancing photo response with AI");

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            var enhancementPrompt = $$"""
Eres un asistente experto en presentación de fotos. Mejora la siguiente respuesta sobre fotos, haciéndola más legible y atractiva:

PREGUNTA: {{question}}

RESPUESTA RAW:
{{rawResponse}}

Instrucciones:
- Usa un formato más limpio y organizado
- Agrega emojis relevantes
- Resalta información importante
- Incluye tablas o listas si es necesario
- Asegúrate de que los enlaces a las fotos sean clicables

Responde únicamente con la respuesta mejorada.
""";
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(enhancementPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 2000 },
                    { "temperature", 0.5 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var enhancedResponse = response.Content ?? "No se pudo mejorar la respuesta de fotos.";
            
            _logger.LogInformation("✅ Photo response enhanced successfully");
            return enhancedResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error enhancing photo response");
            return rawResponse; // Return the original response if enhancement fails
        }
    }

    /// <summary>
    /// Get a comma-separated list of unique categories from the photos
    /// </summary>
    private string GetCategoriesFromPhotos(List<PhotoMetadata> photos)
    {
        return string.Join(", ", photos.Select(p => p.Category).Distinct());
    }

    /// <summary>
    /// Get the date range (min and max dates) from the photos
    /// </summary>
    private string GetDateRangeFromPhotos(List<PhotoMetadata> photos)
    {
        var dates = photos.Select(p => DateTime.Parse(p.DateTaken)).ToList();
        var minDate = dates.Min();
        var maxDate = dates.Max();

        return $"{minDate:dd MMM yyyy} - {maxDate:dd MMM yyyy}";
    }

    /// <summary>
    /// Format file size from bytes to a human-readable string
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Generate photo context summary with SAS URLs for AI processing
    /// </summary>
    private string GeneratePhotoContextWithUrls(List<PhotoMetadata> photos)
    {
        if (!photos.Any())
            return "No hay fotos disponibles para analizar.";

        var context = new StringBuilder();
        context.AppendLine($"📸 COLECCIÓN DE {photos.Count} FOTOS:");
        context.AppendLine();

        // Category analysis
        var categoryGroups = photos.GroupBy(p => p.Category).OrderByDescending(g => g.Count());
        context.AppendLine("🏷️ CATEGORÍAS:");
        foreach (var group in categoryGroups)
        {
            context.AppendLine($"   • {group.Key}: {group.Count()} fotos");
        }
        context.AppendLine();

        // Individual photos with SAS URLs
        context.AppendLine("📷 FOTOS INDIVIDUALES (con URLs para mostrar):");
        var photosWithUrls = photos.Where(p => !string.IsNullOrEmpty(p.SasUrl)).ToList();
        var photosWithoutUrls = photos.Where(p => string.IsNullOrEmpty(p.SasUrl)).ToList();
        
        foreach (var photo in photosWithUrls)
        {
            context.AppendLine($"   📸 {photo.FileName}");
            context.AppendLine($"      • ID: {photo.PhotoId}");
            context.AppendLine($"      • SAS URL: {photo.SasUrl}");
            context.AppendLine($"      • Descripción: {photo.Description}");
            context.AppendLine($"      • Fecha: {photo.DateTaken}");
            context.AppendLine($"      • Ubicación: {photo.Location}");
            context.AppendLine($"      • Personas: {photo.PeopleInPhoto}");
            context.AppendLine($"      • Categoría: {photo.Category}");
            context.AppendLine($"      • Tags: {photo.Tags}");
            context.AppendLine();
        }

        if (photosWithoutUrls.Any())
        {
            context.AppendLine($"⚠️ {photosWithoutUrls.Count} fotos sin URL disponible:");
            foreach (var photo in photosWithoutUrls)
            {
                context.AppendLine($"   • {photo.FileName} - {photo.Description}");
            }
            context.AppendLine();
        }

        // Date range
        var validDates = photos.Where(p => DateTime.TryParse(p.DateTaken, out _)).ToList();
        if (validDates.Any())
        {
            context.AppendLine($"📅 RANGO DE FECHAS: {GetDateRangeFromPhotos(photos)}");
            context.AppendLine();
        }

        // Location analysis
        var locations = photos.Where(p => !string.IsNullOrEmpty(p.Location))
                             .GroupBy(p => p.Location)
                             .OrderByDescending(g => g.Count())
                             .Take(5);
        if (locations.Any())
        {
            context.AppendLine("📍 UBICACIONES PRINCIPALES:");
            foreach (var location in locations)
            {
                context.AppendLine($"   • {location.Key}: {location.Count()} fotos");
            }
            context.AppendLine();
        }

        // People analysis
        var allPeople = photos.Where(p => !string.IsNullOrEmpty(p.PeopleInPhoto))
                             .SelectMany(p => p.PeopleInPhoto.Split(',').Select(person => person.Trim()))
                             .Where(person => !string.IsNullOrEmpty(person))
                             .GroupBy(person => person)
                             .OrderByDescending(g => g.Count())
                             .Take(5);
        if (allPeople.Any())
        {
            context.AppendLine("👥 PERSONAS MÁS FOTOGRAFIADAS:");
            foreach (var person in allPeople)
            {
                context.AppendLine($"   • {person.Key}: {person.Count()} fotos");
            }
            context.AppendLine();
        }

        // Storage statistics
        var totalSize = photos.Sum(p => p.FileSize);
        context.AppendLine($"💾 TAMAÑO TOTAL: {FormatFileSize(totalSize)}");
        context.AppendLine($"📏 TAMAÑO PROMEDIO: {FormatFileSize(totalSize / photos.Count)}");
        context.AppendLine($"🔗 FOTOS CON URL: {photosWithUrls.Count}/{photos.Count}");

        return context.ToString();
    }

    /// <summary>
    /// Convert a list of PhotoDocument objects to a list of PhotoDocument (no conversion needed)
    /// </summary>
    private List<TwinFx.Services.PhotoDocument> ConvertObjectsToPhotoDocuments(List<TwinFx.Services.PhotoDocument> photoDocuments)
    {
        // No conversion needed since they're already PhotoDocument objects
        return photoDocuments;
    }
}

/// <summary>
/// Photo search parameters
/// </summary>
public class PhotoSearchParameters
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("searchText")]
    public string? SearchText { get; set; }

    [JsonPropertyName("peopleInPhoto")]
    public string? PeopleInPhoto { get; set; }

    [JsonPropertyName("fromDate")]
    public DateTime? FromDate { get; set; }

    [JsonPropertyName("toDate")]
    public DateTime? ToDate { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }
}

/// <summary>
/// Result for photo save operation
/// </summary>
public class PhotoSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result for getting photos
/// </summary>
public class PhotosResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PhotoMetadata> Photos { get; set; } = new();
}