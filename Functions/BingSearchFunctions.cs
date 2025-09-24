using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions para búsquedas inteligentes con Bing Search y Azure OpenAI
    /// Utiliza BingSearch service para proporcionar respuestas mejoradas por IA
    /// </summary>
    public class BingSearchFunctions
    {
        private readonly ILogger<BingSearchFunctions> _logger;
        private readonly BingSearch _bingSearchService;
        private readonly IConfiguration _configuration;

        public BingSearchFunctions(ILogger<BingSearchFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Initialize BingSearch Service
            var bingSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<BingSearch>();
            _bingSearchService = new BingSearch(bingSearchLogger, configuration);
        }

        /// <summary>
        /// Helper method to add CORS headers to HTTP context
        /// </summary>
        private static void AddCorsHeaders(HttpRequest req)
        {
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Max-Age", "86400");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Credentials", "false");
        }

        // ===== OPTIONS HANDLERS FOR CORS =====

        /// <summary>
        /// Handle CORS preflight for /api/twins/search/{twinId}
        /// </summary>
        [Function("BingSearchOptions")]
        public IActionResult HandleBingSearchOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/search/{twinId}")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("🔍 Handling OPTIONS request for /api/twins/search/{TwinId}", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/search
        /// </summary>
        [Function("GlobalSearchOptions")]
        public IActionResult HandleGlobalSearchOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "search")] HttpRequest req)
        {
            _logger.LogInformation("🔍 Handling OPTIONS request for /api/search");
            AddCorsHeaders(req);
            return new OkResult();
        }

        // ===== SEARCH ENDPOINTS =====

        /// <summary>
        /// Búsqueda inteligente con contexto de Twin
        /// GET /api/twins/search/{twinId}?question={question}
        /// </summary>
        [Function("IntelligentSearchWithTwin")]
        public async Task<IActionResult> IntelligentSearchWithTwin(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/search/{twinId}")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("🔍 Starting intelligent search for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Obtener la pregunta del query string
                var question = req.Query["question"].FirstOrDefault();
                
                if (string.IsNullOrEmpty(question))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Question parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("🔍 Processing search question: {Question} for Twin: {TwinId}", question, twinId);

                // Procesar la búsqueda usando el BingSearch service
                var result = await _bingSearchService.ProcessIntelligentSearchAsync(question, twinId);

                if (!result.Success)
                {
                    var badRequestResponse = new BadRequestObjectResult(new
                    {
                        success = false,
                        error = result.Error,
                        twinId = twinId,
                        question = question,
                        processingTimeMs = result.ProcessingTimeMs
                    });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("✅ Search completed successfully for Twin: {TwinId}, ProcessingTime: {Time}ms", 
                    twinId, result.ProcessingTimeMs);

                return new OkObjectResult(new
                {
                    success = true,
                    data = new
                    {
                        question = result.Question,
                        cursoBusquedaResults = result.CursoBusqueda,
                        enhancedAnswer = result.EnhancedAnswer,
                        summary = result.Summary,
                        keyInsights = result.KeyInsights,
                        recommendedActions = result.RecommendedActions,
                        disclaimer = result.Disclaimer
                    },
                    twinId = result.TwinId,
                    processingTimeMs = result.ProcessingTimeMs,
                    processedAt = result.ProcessedAt,
                    message = "Intelligent search completed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in intelligent search for Twin ID: {TwinId}", twinId);
                var errorResponse = new ObjectResult(new { 
                    error = ex.Message,
                    twinId = twinId
                })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        /// <summary>
        /// Búsqueda inteligente global (sin contexto de Twin específico)
        /// GET /api/search?question={question}
        /// </summary>
        [Function("GlobalIntelligentSearch")]
        public async Task<IActionResult> GlobalIntelligentSearch(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search")] HttpRequest req)
        {
            _logger.LogInformation("🔍 Starting global intelligent search");
            AddCorsHeaders(req);

            try
            {
                // Obtener la pregunta del query string
                var question = req.Query["question"].FirstOrDefault();
                
                if (string.IsNullOrEmpty(question))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Question parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("🔍 Processing global search question: {Question}", question);

                // Procesar la búsqueda usando el BingSearch service (sin twinId)
                var result = await _bingSearchService.ProcessIntelligentSearchAsync(question);

                if (!result.Success)
                {
                    var badRequestResponse = new BadRequestObjectResult(new
                    {
                        success = false,
                        error = result.Error,
                        question = question,
                        processingTimeMs = result.ProcessingTimeMs
                    });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("✅ Global search completed successfully, ProcessingTime: {Time}ms", 
                    result.ProcessingTimeMs);

                return new OkObjectResult(new
                {
                    success = true,
                    data = new
                    {
                        question = result.Question,
                        cursoBusquedaResults = result.CursoBusqueda,
                        enhancedAnswer = result.EnhancedAnswer,
                        summary = result.Summary,
                        keyInsights = result.KeyInsights,
                        recommendedActions = result.RecommendedActions,
                        disclaimer = result.Disclaimer
                    },
                    processingTimeMs = result.ProcessingTimeMs,
                    processedAt = result.ProcessedAt,
                    message = "Global intelligent search completed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in global intelligent search");
                var errorResponse = new ObjectResult(new { 
                    error = ex.Message
                })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        /// <summary>
        /// Búsqueda POST con cuerpo de petición (para preguntas más complejas)
        /// POST /api/twins/search/{twinId}
        /// </summary>
        [Function("IntelligentSearchPost")]
        public async Task<IActionResult> IntelligentSearchPost(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/search/{twinId}")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("🔍 Starting POST intelligent search for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Leer el cuerpo de la petición
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Request body is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                SearchRequest? searchRequest;
                try
                {
                    // Configurar opciones JSON
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                    };
                    
                    searchRequest = JsonSerializer.Deserialize<SearchRequest>(requestBody, jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Invalid JSON in request body");
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                if (searchRequest == null || string.IsNullOrEmpty(searchRequest.Question))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Question is required in request body" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("🔍 Processing POST search question: {Question} for Twin: {TwinId}", 
                    searchRequest.Question, twinId);

                // Procesar la búsqueda usando el BingSearch service
                var result = await _bingSearchService.ProcessIntelligentSearchAsync(searchRequest.Question, twinId);

                if (!result.Success)
                {
                    var badRequestResponse = new BadRequestObjectResult(new
                    {
                        success = false,
                        error = result.Error,
                        twinId = twinId,
                        question = searchRequest.Question,
                        processingTimeMs = result.ProcessingTimeMs
                    });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("✅ POST search completed successfully for Twin: {TwinId}, ProcessingTime: {Time}ms", 
                    twinId, result.ProcessingTimeMs);

                return new OkObjectResult(new
                {
                    success = true,
                    data = new
                    {
                        question = result.Question,
                        searchResults = result.CursoBusqueda,
                        enhancedAnswer = result.EnhancedAnswer,
                        summary = result.Summary,
                        keyInsights = result.KeyInsights,
                        recommendedActions = result.RecommendedActions,
                        disclaimer = result.Disclaimer
                    },
                    twinId = result.TwinId,
                    processingTimeMs = result.ProcessingTimeMs,
                    processedAt = result.ProcessedAt,
                    message = "POST intelligent search completed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in POST intelligent search for Twin ID: {TwinId}", twinId);
                var errorResponse = new ObjectResult(new { 
                    error = ex.Message,
                    twinId = twinId
                })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }
    }

    /// <summary>
    /// Modelo para peticiones de búsqueda POST
    /// </summary>
    public class SearchRequest
    {
        /// <summary>
        /// Pregunta o consulta de búsqueda (requerida)
        /// </summary>
        public string Question { get; set; } = string.Empty;
    }
}