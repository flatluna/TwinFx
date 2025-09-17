using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Agents;

namespace TwinFx.Functions;

/// <summary>
/// Azure Function para el Agente de Diario AI
/// Proporciona endpoint para hacer preguntas inteligentes sobre el diario del Twin
/// usando búsqueda semántica y AI
/// </summary>
public class DiaryAIFunction
{
    private readonly ILogger<DiaryAIFunction> _logger;
    private readonly IConfiguration _configuration;

    public DiaryAIFunction(ILogger<DiaryAIFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // ========================================
    // OPTIONS HANDLER FOR CORS
    // ========================================
    [Function("DiaryAIOptions")]
    public async Task<HttpResponseData> HandleDiaryAIOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/ask")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/diary/ask", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // DIARY AI QUESTION ENDPOINT
    // ========================================
    [Function("AskDiaryAI")]
    public async Task<HttpResponseData> AskDiaryAI(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/diary/ask")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? AskDiaryAI function triggered for Twin ID: {TwinId}", twinId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Leer el cuerpo de la petición
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            if (string.IsNullOrEmpty(requestBody))
            {
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            DiaryAIRequest? diaryRequest;
            try
            {
                diaryRequest = JsonSerializer.Deserialize<DiaryAIRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "?? Invalid JSON in request body");
                return await CreateErrorResponse(req, "Invalid JSON format in request body", HttpStatusCode.BadRequest);
            }

            if (diaryRequest?.Question == null || string.IsNullOrEmpty(diaryRequest.Question.Trim()))
            {
                return await CreateErrorResponse(req, "Question is required and cannot be empty", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("? Processing diary question: {Question}", diaryRequest.Question);

            // Crear instancia del AgentDiaryAI con logger apropiado
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var diaryAgentLogger = loggerFactory.CreateLogger<AgentDiaryAI>();
            var diaryAgent = new AgentDiaryAI(diaryAgentLogger, _configuration);

            // Procesar la pregunta usando el agente
            var result = await diaryAgent.ProcessDiaryQuestionAsync(diaryRequest.Question, twinId);

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            _logger.LogInformation("? Diary AI response completed in {ProcessingTime}ms: {Summary}", 
                processingTime, result.GetSummary());

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync(JsonSerializer.Serialize(new DiaryAIFunctionResponse
            {
                Success = result.Success,
                TwinId = twinId,
                Question = diaryRequest.Question,
                Answer = result.Answer,
                Error = result.Error,
                SearchInfo = new DiarySearchInfo
                {
                    TotalResults = result.TotalResults,
                    SearchType = result.SearchType,
                    ProcessingTimeMs = result.ProcessingTimeMs,
                    AverageRelevanceScore = result.GetAverageRelevanceScore(),
                    ReferencedEntryIds = result.GetReferencedEntryIds()
                },
                ProcessingTimeMs = processingTime,
                ProcessedAt = DateTime.UtcNow
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in AskDiaryAI for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Endpoint de información sobre el estado del servicio de DiaryAI
    /// </summary>
    [Function("DiaryAIStatus")]
    public async Task<HttpResponseData> DiaryAIStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/diary/status")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? DiaryAIStatus function triggered for Twin ID: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Crear instancia del AgentDiaryAI para verificar el estado con logger apropiado
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var diaryAgentLogger = loggerFactory.CreateLogger<AgentDiaryAI>();
            var diaryAgent = new AgentDiaryAI(diaryAgentLogger, _configuration);

            // Crear respuesta de estado
            var statusResponse = new DiaryAIStatusResponse
            {
                Success = true,
                TwinId = twinId,
                ServiceAvailable = true, // El agente se inicializó correctamente
                Message = "Diary AI service is available and ready to answer questions about your diary",
                Capabilities = new List<string>
                {
                    "Semantic search in diary content",
                    "Vector-based similarity search",
                    "AI-powered contextual responses",
                    "HTML formatted answers",
                    "Multi-entry analysis and correlation"
                },
                SupportedQuestionTypes = new List<string>
                {
                    "Activities and experiences",
                    "Dates and timeline questions",
                    "Emotional patterns and moods",
                    "Travel and location memories",
                    "People and relationships mentioned",
                    "Goals and achievements tracking"
                },
                CheckedAt = DateTime.UtcNow
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync(JsonSerializer.Serialize(statusResponse, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error checking DiaryAI status for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, $"Error checking service status: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// OPTIONS handler para el endpoint de estado
    /// </summary>
    [Function("DiaryAIStatusOptions")]
    public async Task<HttpResponseData> HandleDiaryAIStatusOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/status")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/diary/status", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Crea una respuesta de error con CORS headers
    /// </summary>
    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);
        response.Headers.Add("Content-Type", "application/json");
        
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            success = false,
            error = errorMessage,
            processedAt = DateTime.UtcNow
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        
        return response;
    }

    /// <summary>
    /// Agrega headers CORS a la respuesta
    /// </summary>
    private void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
        var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

        var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };

        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
        }
        else
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
        }

        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
        response.Headers.Add("Access-Control-Max-Age", "3600");
    }
}

// ========================================
// REQUEST/RESPONSE MODELS
// ========================================

/// <summary>
/// Request model para hacer preguntas al DiaryAI
/// </summary>
public class DiaryAIRequest
{
    /// <summary>
    /// Pregunta sobre el diario del Twin
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Configuraciones opcionales de búsqueda
    /// </summary>
    public DiarySearchOptions? SearchOptions { get; set; }
}

/// <summary>
/// Opciones de búsqueda para el DiaryAI
/// </summary>
public class DiarySearchOptions
{
    /// <summary>
    /// Usar búsqueda vectorial (por defecto: true)
    /// </summary>
    public bool UseVectorSearch { get; set; } = true;

    /// <summary>
    /// Usar búsqueda semántica (por defecto: true)
    /// </summary>
    public bool UseSemanticSearch { get; set; } = true;

    /// <summary>
    /// Número máximo de resultados a considerar (por defecto: 5)
    /// </summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// Solo considerar entradas procesadas exitosamente (por defecto: true)
    /// </summary>
    public bool SuccessfulOnly { get; set; } = true;
}

/// <summary>
/// Respuesta completa del endpoint DiaryAI
/// </summary>
public class DiaryAIFunctionResponse
{
    /// <summary>
    /// Indica si la operación fue exitosa
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// ID del Twin
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Pregunta original del usuario
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Respuesta generada por el AI Agent
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Mensaje de error si Success = false
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Información sobre la búsqueda realizada
    /// </summary>
    public DiarySearchInfo SearchInfo { get; set; } = new();

    /// <summary>
    /// Tiempo de procesamiento total en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Fecha y hora cuando se procesó la pregunta
    /// </summary>
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// Información sobre la búsqueda realizada en el diario
/// </summary>
public class DiarySearchInfo
{
    /// <summary>
    /// Total de resultados encontrados
    /// </summary>
    public int TotalResults { get; set; }

    /// <summary>
    /// Tipo de búsqueda utilizada
    /// </summary>
    public string SearchType { get; set; } = string.Empty;

    /// <summary>
    /// Tiempo de procesamiento de la búsqueda en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Puntuación promedio de relevancia
    /// </summary>
    public double AverageRelevanceScore { get; set; }

    /// <summary>
    /// IDs de las entradas del diario que se usaron como contexto
    /// </summary>
    public List<string> ReferencedEntryIds { get; set; } = new();
}

/// <summary>
/// Respuesta del endpoint de estado del DiaryAI
/// </summary>
public class DiaryAIStatusResponse
{
    /// <summary>
    /// Indica si el servicio está disponible
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// ID del Twin
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Si el servicio de DiaryAI está disponible
    /// </summary>
    public bool ServiceAvailable { get; set; }

    /// <summary>
    /// Mensaje descriptivo del estado
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Capacidades del servicio
    /// </summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// Tipos de preguntas soportadas
    /// </summary>
    public List<string> SupportedQuestionTypes { get; set; } = new();

    /// <summary>
    /// Fecha y hora de la verificación
    /// </summary>
    public DateTime CheckedAt { get; set; }
}