using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Agents;

namespace TwinFx.Functions;

/// <summary>
/// Azure Function para el Agente de Homes AI
/// Proporciona endpoints para gestión inteligente de casas/viviendas del Twin
/// usando procesamiento inteligente y validación con AI
/// </summary>
public class AgentHomesFunction
{
    private readonly ILogger<AgentHomesFunction> _logger;
    private readonly IConfiguration _configuration;

    public AgentHomesFunction(ILogger<AgentHomesFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // ========================================
    // OPTIONS HANDLER FOR CORS
    // ========================================
    [Function("AgentHomesOptions")]
    public async Task<HttpResponseData> HandleAgentHomesOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/homes/agent")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/homes/agent", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("AgentHomesCreateOptions")]
    public async Task<HttpResponseData> HandleAgentHomesCreateOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/homes/agent/create")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/homes/agent/create", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // AGENT HOMES ENDPOINTS
    // ========================================

    /// <summary>
    /// Crear nueva casa con procesamiento inteligente usando AI
    /// POST /api/twins/{twinId}/homes/agent/create
    /// </summary>
    [Function("AgentCreateHome")]
    public async Task<HttpResponseData> AgentCreateHome(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/homes/agent/create")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? AgentCreateHome function triggered for Twin ID: {TwinId}", twinId);
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

            CreateHomeRequest? homeRequest;
            try
            {
                homeRequest = JsonSerializer.Deserialize<CreateHomeRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "?? Invalid JSON in request body");
                return await CreateErrorResponse(req, "Invalid JSON format in request body", HttpStatusCode.BadRequest);
            }

            if (homeRequest == null)
            {
                return await CreateErrorResponse(req, "Home request data is required", HttpStatusCode.BadRequest);
            }

            // Validar campos básicos requeridos
            if (string.IsNullOrEmpty(homeRequest.Direccion))
            {
                return await CreateErrorResponse(req, "Direccion is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(homeRequest.Ciudad))
            {
                return await CreateErrorResponse(req, "Ciudad is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(homeRequest.Estado))
            {
                return await CreateErrorResponse(req, "Estado is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(homeRequest.TipoPropiedad))
            {
                return await CreateErrorResponse(req, "TipoPropiedad is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("?? Processing home creation: {Direccion}, {Ciudad}, {Estado}", 
                homeRequest.Direccion, homeRequest.Ciudad, homeRequest.Estado);

            // Crear instancia del AgenteHomes
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var homesAgentLogger = loggerFactory.CreateLogger<AgenteHomes>();
            var homesAgent = new AgenteHomes(homesAgentLogger, _configuration);

            // Procesar la creación de casa usando el agente
            var result = await homesAgent.ProcessCreateHomeAsync(homeRequest, twinId);

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            _logger.LogInformation("? Agent Home creation completed in {ProcessingTime}ms: {Summary}", 
                processingTime, result.GetSummary());

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync(JsonSerializer.Serialize(new AgentHomesCreateResponse
            {
                Success = result.Success,
                TwinId = twinId,
                Operation = "CreateHome",
                HomeData = result.HomeData,
                AIResponse = result.AIResponse,
                Error = result.Error,
                ValidationInfo = result.ValidationResults != null ? new ValidationInfo
                {
                    IsValid = result.ValidationResults.IsValid,
                    ValidationErrors = result.ValidationResults.ValidationErrors,
                    EnrichmentSuggestions = result.ValidationResults.EnrichmentSuggestions,
                    PropertyInsights = result.ValidationResults.PropertyInsights,
                    EstimatedValueRange = result.ValidationResults.EstimatedValueRange,
                    NeighborhoodScore = result.ValidationResults.NeighborhoodScore
                } : null,
                ProcessingTimeMs = processingTime,
                ProcessedAt = DateTime.UtcNow
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in AgentCreateHome for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Endpoint de información sobre el estado del servicio de AgenteHomes
    /// GET /api/twins/{twinId}/homes/agent/status
    /// </summary>
    [Function("AgentHomesStatus")]
    public async Task<HttpResponseData> AgentHomesStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/homes/agent/status")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? AgentHomesStatus function triggered for Twin ID: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Crear respuesta de estado
            var statusResponse = new AgentHomesStatusResponse
            {
                Success = true,
                TwinId = twinId,
                ServiceAvailable = true,
                Message = "Agent Homes service is available and ready to manage your properties intelligently",
                Capabilities = new List<string>
                {
                    "AI-powered property validation and enrichment",
                    "Intelligent property data processing",
                    "Business rules validation (principal home management)",
                    "Property insights and recommendations",
                    "Real estate market analysis integration",
                    "Automated property portfolio management"
                },
                SupportedOperations = new List<string>
                {
                    "Create new property with AI validation",
                    "Update existing property with recommendations",
                    "Property portfolio analysis",
                    "Market value estimation",
                    "Neighborhood scoring and insights",
                    "Property management recommendations"
                },
                PropertyTypes = new List<string>
                {
                    "casa", "apartamento", "condominio", "townhouse", "otro"
                },
                PropertyCategories = new List<string>
                {
                    "actual", "pasado", "inversion", "vacacional"
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
            _logger.LogError(ex, "? Error checking AgentHomes status for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, $"Error checking service status: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// OPTIONS handler para el endpoint de estado
    /// </summary>
    [Function("AgentHomesStatusOptions")]
    public async Task<HttpResponseData> HandleAgentHomesStatusOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/homes/agent/status")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/homes/agent/status", twinId);
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
/// Respuesta completa del endpoint AgentCreateHome
/// </summary>
public class AgentHomesCreateResponse
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
    /// Tipo de operación realizada
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Datos de la casa creada
    /// </summary>
    public TwinFx.Services.HomeData? HomeData { get; set; }

    /// <summary>
    /// Respuesta HTML generada por el AI Agent
    /// </summary>
    public string AIResponse { get; set; } = string.Empty;

    /// <summary>
    /// Mensaje de error si Success = false
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Información de validación
    /// </summary>
    public ValidationInfo? ValidationInfo { get; set; }

    /// <summary>
    /// Tiempo de procesamiento total en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Fecha y hora cuando se procesó la operación
    /// </summary>
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// Información de validación para la respuesta
/// </summary>
public class ValidationInfo
{
    /// <summary>
    /// Si la validación fue exitosa
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Errores de validación encontrados
    /// </summary>
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Sugerencias de enriquecimiento
    /// </summary>
    public List<string> EnrichmentSuggestions { get; set; } = new();

    /// <summary>
    /// Insights sobre la propiedad
    /// </summary>
    public string PropertyInsights { get; set; } = string.Empty;

    /// <summary>
    /// Rango de valor estimado
    /// </summary>
    public string EstimatedValueRange { get; set; } = string.Empty;

    /// <summary>
    /// Puntuación del vecindario
    /// </summary>
    public string NeighborhoodScore { get; set; } = string.Empty;
}

/// <summary>
/// Respuesta del endpoint de estado del AgenteHomes
/// </summary>
public class AgentHomesStatusResponse
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
    /// Si el servicio de AgenteHomes está disponible
    /// </summary>
    public bool ServiceAvailable { get; set; }

    /// <summary>
    /// Mensaje descriptivo del estado
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Capacidades del servicio de AI
    /// </summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// Operaciones soportadas
    /// </summary>
    public List<string> SupportedOperations { get; set; } = new();

    /// <summary>
    /// Tipos de propiedades soportadas
    /// </summary>
    public List<string> PropertyTypes { get; set; } = new();

    /// <summary>
    /// Categorías de propiedades soportadas
    /// </summary>
    public List<string> PropertyCategories { get; set; } = new();

    /// <summary>
    /// Fecha y hora de la verificación
    /// </summary>
    public DateTime CheckedAt { get; set; }
}