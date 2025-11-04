using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using System.Net;
using System.Text.Json;
using TwinFx.Agents;
using static TwinFx.Functions.WebSearchResponse;

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions para servicios web y búsquedas inteligentes
/// ========================================================================
/// 
/// Proporciona endpoints para:
/// - Búsquedas web inteligentes usando Azure AI Agents con Bing Grounding
/// - Servicios web generales del sistema TwinFx
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class WebServicesFunctions
{
    private readonly ILogger<WebServicesFunctions> _logger;
    private readonly AiWebSearchAgent _aiWebSearchAgent;

    public WebServicesFunctions(ILogger<WebServicesFunctions> logger, AiWebSearchAgent aiWebSearchAgent)
    {
        _logger = logger;
        _aiWebSearchAgent = aiWebSearchAgent;
    }

    // ========================================
    // CORS OPTIONS HANDLERS
    // ========================================
    
    [Function("WebServicesOptions")]
    public async Task<HttpResponseData> HandleWebServicesOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "webservices")] HttpRequestData req)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for webservices");
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("WebSearchOptions")]
    public async Task<HttpResponseData> HandleWebSearchOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "webservices/search")] HttpRequestData req)
    {
        _logger.LogInformation("🔍 OPTIONS preflight request for webservices/search");
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // WEB SEARCH ENDPOINTS
    // ========================================

    /// <summary>
    /// Realizar búsqueda web inteligente usando Azure AI Agents con Bing Grounding
    /// POST /api/webservices/search
    /// </summary>
    [Function("AiWebSearch")]
    public async Task<HttpResponseData> AiWebSearch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webservices/search")] HttpRequestData req)
    {
        _logger.LogInformation("🔍 AiWebSearch function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            // Parse request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body received: {RequestBody}", requestBody);

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("⚠️ Empty request body received");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Request body is required",
                    message = "Debe proporcionar un cuerpo de solicitud con el prompt del usuario"
                }));
                return errorResponse;
            }

            // Deserialize request
            WebSearchRequest? searchRequest;
            try
            {
                searchRequest = JsonSerializer.Deserialize<WebSearchRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "❌ Error deserializing request body");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Invalid JSON format",
                    message = "El formato JSON del request no es válido"
                }));
                return errorResponse;
            }

            // Validate request
            if (searchRequest == null || string.IsNullOrEmpty(searchRequest.UserPrompt))
            {
                _logger.LogWarning("⚠️ Invalid search request - missing userPrompt");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "UserPrompt is required",
                    message = "El campo 'userPrompt' es obligatorio"
                }));
                return errorResponse;
            }

            _logger.LogInformation("🔍 Processing web search for prompt: {UserPrompt}", searchRequest.UserPrompt);

            // Call AiWebSearchAgent
            // var searchResults = await _aiWebSearchAgent.BingGroundingSearchAsync(searchRequest.UserPrompt);

            var searchResults = await _aiWebSearchAgent.GoolgSearch(searchRequest.UserPrompt);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("✅ Web search completed successfully in {ProcessingTime}ms", processingTimeMs);

            // Create successful response
            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(successResponse, req);
            
            var responseData = new WebSearchResponse
            {
                Success = true,
                UserPrompt = searchRequest.UserPrompt,
                SearchResults = searchResults,
                ProcessingTimeMs = processingTimeMs,
                ProcessedAt = DateTime.UtcNow,
                RequestId = searchRequest.RequestId,
                Disclaimer = "Esta información proviene de búsquedas web y ha sido procesada por IA. Verifica la información en fuentes oficiales antes de tomar decisiones importantes."
            };

            await successResponse.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing web search request");
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new WebSearchResponse
            {
                Success = false,
                Error = ex.Message,
                ProcessingTimeMs = processingTimeMs,
                ProcessedAt = DateTime.UtcNow,
                SearchResults = new SearchResults(),
                Disclaimer = "Hubo un error técnico procesando tu búsqueda. Por favor intenta nuevamente."
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            return errorResponse;
        }
    }

    /// </summary>
    [Function("AiWebSearchOnly")]
    public async Task<HttpResponseData> AiWebSearchOnly(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webservices/searchonly")] HttpRequestData req)
    {
        _logger.LogInformation("🔍 AiWebSearch function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            // Parse request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body received: {RequestBody}", requestBody);

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("⚠️ Empty request body received");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Request body is required",
                    message = "Debe proporcionar un cuerpo de solicitud con el prompt del usuario"
                }));
                return errorResponse;
            }

            // Deserialize request
            WebSearchRequest? searchRequest;
            try
            {
                searchRequest = JsonSerializer.Deserialize<WebSearchRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "❌ Error deserializing request body");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Invalid JSON format",
                    message = "El formato JSON del request no es válido"
                }));
                return errorResponse;
            }

            // Validate request
            if (searchRequest == null || string.IsNullOrEmpty(searchRequest.UserPrompt))
            {
                _logger.LogWarning("⚠️ Invalid search request - missing userPrompt");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "UserPrompt is required",
                    message = "El campo 'userPrompt' es obligatorio"
                }));
                return errorResponse;
            }

            _logger.LogInformation("🔍 Processing web search for prompt: {UserPrompt}", searchRequest.UserPrompt);

            // Call AiWebSearchAgent
            // var searchResults = await _aiWebSearchAgent.BingGroundingSearchAsync(searchRequest.UserPrompt);
            var searchResults= await _aiWebSearchAgent.BingGroundingSearchAsync(searchRequest.UserPrompt);
           // var searchResults = await _aiWebSearchAgent.GoolgSearchOnly(searchRequest.UserPrompt);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("✅ Web search completed successfully in {ProcessingTime}ms", processingTimeMs);

            // Create successful response
            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(successResponse, req);

            

            await successResponse.WriteStringAsync(JsonSerializer.Serialize(searchResults, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing web search request");
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);

            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new WebSearchResponse
            {
                Success = false,
                Error = ex.Message,
                ProcessingTimeMs = processingTimeMs,
                ProcessedAt = DateTime.UtcNow,
                SearchResults = new SearchResults(),
                Disclaimer = "Hubo un error técnico procesando tu búsqueda. Por favor intenta nuevamente."
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            return errorResponse;
        }
    }
    [Function("AiGoogleSearch")]
    public async Task<HttpResponseData> AiGoogleSearch(
     [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googleservices/search")] HttpRequestData req)
    {
        _logger.LogInformation("🔍 AiWebSearch function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            // Parse request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body received: {RequestBody}", requestBody);

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("⚠️ Empty request body received");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Request body is required",
                    message = "Debe proporcionar un cuerpo de solicitud con el prompt del usuario"
                }));
                return errorResponse;
            }

            // Deserialize request
            WebSearchRequest? searchRequest;
            try
            {
                searchRequest = JsonSerializer.Deserialize<WebSearchRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "❌ Error deserializing request body");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Invalid JSON format",
                    message = "El formato JSON del request no es válido"
                }));
                return errorResponse;
            }

            // Validate request
            if (searchRequest == null || string.IsNullOrEmpty(searchRequest.UserPrompt))
            {
                _logger.LogWarning("⚠️ Invalid search request - missing userPrompt");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "UserPrompt is required",
                    message = "El campo 'userPrompt' es obligatorio"
                }));
                return errorResponse;
            }

            _logger.LogInformation("🔍 Processing web search for prompt: {UserPrompt}", searchRequest.UserPrompt);

            // Call AiWebSearchAgent
          

            var searchResults = await _aiWebSearchAgent.GoolgSearchSimple(searchRequest.UserPrompt);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("✅ Web search completed successfully in {ProcessingTime}ms", processingTimeMs);

            // Create successful response
            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(successResponse, req);

            var responseData = new GoogleSearchMyResults
            {
                Success = true,
                UserPrompt = searchRequest.UserPrompt,
                SearchResults = searchResults,
                ProcessingTimeMs = processingTimeMs,
                ProcessedAt = DateTime.UtcNow,
                RequestId = searchRequest.RequestId,
                Disclaimer = "Esta información proviene de búsquedas web y ha sido procesada por IA. Verifica la información en fuentes oficiales antes de tomar decisiones importantes."
            };

            await successResponse.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing web search request");
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);

            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new WebSearchResponse
            {
                Success = false,
                Error = ex.Message,
                ProcessingTimeMs = processingTimeMs,
                ProcessedAt = DateTime.UtcNow,
                SearchResults = new SearchResults(),
                Disclaimer = "Hubo un error técnico procesando tu búsqueda. Por favor intenta nuevamente."
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            return errorResponse;
        }
    }

    // ========================================
    // ORIGINAL FUNCTION (PRESERVED)
    // ========================================

    [Function("WebServicesFunctions")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Add CORS headers to the response
    /// </summary>
    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-requested-with");
        response.Headers.Add("Access-Control-Max-Age", "86400");
    }
}

// ========================================
// REQUEST/RESPONSE MODELS
// ========================================

/// <summary>
/// Request model para búsqueda web inteligente
/// </summary>
public class WebSearchRequest
{
    /// <summary>
    /// Prompt del usuario con la consulta de búsqueda
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// ID único de la solicitud (opcional)
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Metadatos adicionales (opcional)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Response model para búsqueda web inteligente
/// </summary>
public class WebSearchResponse
{
    /// <summary>
    /// Indica si la operación fue exitosa
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Mensaje de error si Success = false
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Prompt original del usuario
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Resultados de la búsqueda web
    /// </summary>
    public SearchResults SearchResults { get; set; } = new SearchResults();

    /// <summary>
    /// Tiempo de procesamiento en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Fecha y hora cuando se procesó la búsqueda
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ID único de la solicitud
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Disclaimer sobre la información proporcionada
    /// </summary>
    public string Disclaimer { get; set; } = string.Empty;

    /// <summary>
    /// Obtiene un resumen de la respuesta para logging
    /// </summary>
    public string GetSummary()
    {
        if (!Success)
        {
            return $"❌ Error: {Error}";
        }

        return $"✅ Success: Web search processed, {ProcessingTimeMs:F0}ms";
    }

    public class GoogleSearchMyResults
    {
        /// <summary>
        /// Indica si la operación fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Mensaje de error si Success = false
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Prompt original del usuario
        /// </summary>
        public string UserPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Resultados de la búsqueda web
        /// </summary>
        public GoogleSearchResults SearchResults { get; set; } = new GoogleSearchResults();

        /// <summary>
        /// Tiempo de procesamiento en milisegundos
        /// </summary>
        public double ProcessingTimeMs { get; set; }

        /// <summary>
        /// Fecha y hora cuando se procesó la búsqueda
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ID único de la solicitud
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Disclaimer sobre la información proporcionada
        /// </summary>
        public string Disclaimer { get; set; } = string.Empty;

        /// <summary>
        /// Obtiene un resumen de la respuesta para logging
        /// </summary>
        public string GetSummary()
        {
            if (!Success)
            {
                return $"❌ Error: {Error}";
            }

            return $"✅ Success: Web search processed, {ProcessingTimeMs:F0}ms";
        }
    }
}