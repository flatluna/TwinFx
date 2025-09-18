using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinFx.Services;
using TwinFx.Models;
using System.Text;
using System.Text.Json;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions para operaciones CRUD de Viviendas/Hogares
    /// Container: TwinHomes, PartitionKey: TwinID
    /// </summary>
    public class HomesFunctions
    {
        private readonly ILogger<HomesFunctions> _logger;
        private readonly HomesCosmosDbService _homesService;
        private readonly IConfiguration _configuration;

        public HomesFunctions(ILogger<HomesFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Initialize Homes service
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? "",
                Key = configuration["Values:COSMOS_KEY"] ?? "",
                DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            // Create specific logger for HomesCosmosDbService
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var homesLogger = loggerFactory.CreateLogger<HomesCosmosDbService>();

            _homesService = new HomesCosmosDbService(
                homesLogger,
                cosmosOptions,
                configuration);
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
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda
        /// </summary>
        [Function("HomesOptions")]
        public IActionResult HandleHomesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda/{homeId}
        /// </summary>
        [Function("HomeByIdOptions")]
        public IActionResult HandleHomeByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda/{homeId}")] HttpRequest req,
            string twinId,
            string homeId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda/{HomeId}", twinId, homeId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda/{homeId}/marcar-principal
        /// </summary>
        [Function("HomeMarcarPrincipalOptions")]
        public IActionResult HandleHomeMarcarPrincipalOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda/{homeId}/marcar-principal")] HttpRequest req,
            string twinId,
            string homeId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda/{HomeId}/marcar-principal", twinId, homeId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda/principal
        /// </summary>
        [Function("HomePrincipalOptions")]
        public IActionResult HandleHomePrincipalOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda/principal")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda/principal", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda/tipo/{tipo}
        /// </summary>
        [Function("HomesByTipoOptions")]
        public IActionResult HandleHomesByTipoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda/tipo/{tipo}")] HttpRequest req,
            string twinId,
            string tipo)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda/tipo/{Tipo}", twinId, tipo);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda/{homeId}/with-ai
        /// </summary>
        [Function("HomeByIdWithAIOptions")]
        public IActionResult HandleHomeByIdWithAIOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda/{homeId}/with-ai")] HttpRequest req,
            string twinId,
            string homeId)
        {
            _logger.LogInformation("🔧 Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda/{HomeId}/with-ai", twinId, homeId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/lugares-vivienda/{homeId}/photos
        /// </summary>
        [Function("HomePhotosOptions")]
        public IActionResult HandleHomePhotosOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/lugares-vivienda/{homeId}/photos")] HttpRequest req,
            string twinId,
            string homeId)
        {
            _logger.LogInformation("📸 Handling OPTIONS request for /api/twins/{TwinId}/lugares-vivienda/{HomeId}/photos", twinId, homeId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        // ===== CRUD OPERATIONS =====

        /// <summary>
        /// Crear nuevo lugar de vivienda
        /// POST /api/twins/{twinId}/lugares-vivienda
        /// </summary>
        [Function("CreateHome")]
        public async Task<IActionResult> CreateHome(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/lugares-vivienda")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Creating new home for Twin: {TwinId}", twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("?? Request body received: {RequestBody}", requestBody);

                var homeData = JsonConvert.DeserializeObject<HomeData>(requestBody);

                if (homeData == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid home data provided" });
                }

                // Asegurar que el TwinID coincida con el parámetro de la ruta
                homeData.TwinID = twinId;

                // Validar campos requeridos
                if (string.IsNullOrEmpty(homeData.Tipo))
                {
                    return new BadRequestObjectResult(new { error = "Tipo is required (actual, pasado, mudanza)" });
                }

                if (string.IsNullOrEmpty(homeData.Direccion))
                {
                    return new BadRequestObjectResult(new { error = "Direccion is required" });
                }

                if (string.IsNullOrEmpty(homeData.Ciudad))
                {
                    return new BadRequestObjectResult(new { error = "Ciudad is required" });
                }

                if (string.IsNullOrEmpty(homeData.Estado))
                {
                    return new BadRequestObjectResult(new { error = "Estado is required" });
                }

                if (string.IsNullOrEmpty(homeData.TipoPropiedad))
                {
                    return new BadRequestObjectResult(new { error = "TipoPropiedad is required" });
                }

                // Generar ID si no se proporciona
                if (string.IsNullOrEmpty(homeData.Id))
                {
                    homeData.Id = Guid.NewGuid().ToString();
                }

                // Configurar campos de sistema
                homeData.FechaCreacion = DateTime.UtcNow;
                homeData.FechaActualizacion = DateTime.UtcNow;

                var success = await _homesService.CreateLugarAsync(homeData);

                if (success)
                {
                    _logger.LogInformation("? Home created successfully in Cosmos DB");

                    var responseData = new
                    {
                        message = "Home created successfully",
                        id = homeData.Id,
                        homeData = homeData
                    };

                    return new OkObjectResult(responseData);
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error creating home for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener todos los lugares de vivienda de un twin
        /// GET /api/twins/{twinId}/lugares-vivienda
        /// </summary>
        [Function("GetHomesByTwinId")]
        public async Task<IActionResult> GetHomesByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/lugares-vivienda")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting homes for Twin: {TwinId}", twinId);

                // Check if there are query parameters for filtering
                var tipoFilter = req.Query["tipo"].FirstOrDefault();
                var ciudadFilter = req.Query["ciudad"].FirstOrDefault();
                var estadoFilter = req.Query["estado"].FirstOrDefault();
                var esPrincipalFilter = req.Query["esPrincipal"].FirstOrDefault();

                List<HomeData> homes;

                if (!string.IsNullOrEmpty(tipoFilter) || !string.IsNullOrEmpty(ciudadFilter) ||
                    !string.IsNullOrEmpty(estadoFilter) || !string.IsNullOrEmpty(esPrincipalFilter))
                {
                    // Use filtered query
                    var query = new HomeQuery
                    {
                        Tipo = tipoFilter,
                        Ciudad = ciudadFilter,
                        Estado = estadoFilter,
                        EsPrincipal = bool.TryParse(esPrincipalFilter, out var principal) ? principal : null
                    };

                    homes = await _homesService.GetFilteredLugaresAsync(twinId, query);
                }
                else
                {
                    // Get all homes
                    homes = await _homesService.GetLugaresByTwinIdAsync(twinId);
                }

                var response = new
                {
                    twinId = twinId,
                    count = homes.Count,
                    homes = homes
                };

                _logger.LogInformation("? Returning {Count} homes for Twin: {TwinId}", homes.Count, twinId);
                return new OkObjectResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting homes for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener lugares por tipo (actual/pasado/mudanza)
        /// GET /api/twins/{twinId}/lugares-vivienda/tipo/{tipo}
        /// </summary>
        [Function("GetHomesByTipo")]
        public async Task<IActionResult> GetHomesByTipo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/lugares-vivienda/tipo/{tipo}")] HttpRequest req,
            string twinId,
            string tipo)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting homes by type '{Tipo}' for Twin: {TwinId}", tipo, twinId);

                // Validar tipo
                var tiposValidos = new[] { "actual", "pasado", "mudanza" };
                if (!tiposValidos.Contains(tipo.ToLowerInvariant()))
                {
                    return new BadRequestObjectResult(new { error = "Tipo must be one of: actual, pasado, mudanza" });
                }

                var homes = await _homesService.GetLugaresByTipoAsync(twinId, tipo);

                var response = new
                {
                    twinId = twinId,
                    tipo = tipo,
                    count = homes.Count,
                    homes = homes
                };

                _logger.LogInformation("? Returning {Count} homes of type '{Tipo}' for Twin: {TwinId}", homes.Count, tipo, twinId);
                return new OkObjectResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting homes by type '{Tipo}' for Twin: {TwinId}", tipo, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener lugar específico por ID
        /// GET /api/twins/{twinId}/lugares-vivienda/{homeId}
        /// </summary>
        [Function("GetHomeById")]
        public async Task<IActionResult> GetHomeById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/lugares-vivienda/{homeId}")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting home: {HomeId} for Twin: {TwinId}", homeId, twinId);

                var home = await _homesService.GetLugarByIdAsync(homeId, twinId);

                if (home != null)
                {
                    return new OkObjectResult(home);
                }
                else
                {
                    return new NotFoundObjectResult(new { error = "Home not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting home: {HomeId} for Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener vivienda principal
        /// GET /api/twins/{twinId}/lugares-vivienda/principal
        /// </summary>
        [Function("GetHomePrincipal")]
        public async Task<IActionResult> GetHomePrincipal(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/lugares-vivienda/principal")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting principal home for Twin: {TwinId}", twinId);

                var home = await _homesService.GetViviendaPrincipalAsync(twinId);

                if (home != null)
                {
                    return new OkObjectResult(home);
                }
                else
                {
                    return new NotFoundObjectResult(new { error = "No principal home found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting principal home for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Actualizar lugar existente
        /// PUT /api/twins/{twinId}/lugares-vivienda/{homeId}
        /// </summary>
        [Function("UpdateHome")]
        public async Task<IActionResult> UpdateHome(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/lugares-vivienda/{homeId}")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Updating home: {HomeId} for Twin: {TwinId}", homeId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("?? Request body received: {RequestBody}", requestBody);

                var homeData = JsonConvert.DeserializeObject<HomeData>(requestBody);

                if (homeData == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid home data provided" });
                }

                // Asegurar que los IDs coincidan con los parámetros de la ruta
                homeData.Id = homeId;
                homeData.TwinID = twinId;

                // Validar campos requeridos
                if (string.IsNullOrEmpty(homeData.Tipo))
                {
                    return new BadRequestObjectResult(new { error = "Tipo is required (actual, pasado, mudanza)" });
                }

                if (string.IsNullOrEmpty(homeData.Direccion))
                {
                    return new BadRequestObjectResult(new { error = "Direccion is required" });
                }

                if (string.IsNullOrEmpty(homeData.Ciudad))
                {
                    return new BadRequestObjectResult(new { error = "Ciudad is required" });
                }

                if (string.IsNullOrEmpty(homeData.Estado))
                {
                    return new BadRequestObjectResult(new { error = "Estado is required" });
                }

                var success = await _homesService.UpdateLugarAsync(homeData);

                if (success)
                {
                    _logger.LogInformation("? Home updated successfully in Cosmos DB");

                    var responseData = new
                    {
                        message = "Home updated successfully",
                        homeData = homeData
                    };

                    return new OkObjectResult(responseData);
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error updating home: {HomeId} for Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Marcar vivienda como principal
        /// PUT /api/twins/{twinId}/lugares-vivienda/{homeId}/marcar-principal
        /// </summary>
        [Function("MarcarHomePrincipal")]
        public async Task<IActionResult> MarcarHomePrincipal(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/lugares-vivienda/{homeId}/marcar-principal")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Marking home as principal: {HomeId} for Twin: {TwinId}", homeId, twinId);

                var success = await _homesService.MarcarComoPrincipalAsync(homeId, twinId);

                if (success)
                {
                    var responseData = new
                    {
                        message = "Home marked as principal successfully",
                        homeId = homeId,
                        twinId = twinId
                    };

                    return new OkObjectResult(responseData);
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error marking home as principal: {HomeId} for Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Eliminar lugar
        /// DELETE /api/twins/{twinId}/lugares-vivienda/{homeId}
        /// </summary>
        [Function("DeleteHome")]
        public async Task<IActionResult> DeleteHome(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/lugares-vivienda/{homeId}")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("??? Deleting home: {HomeId} for Twin: {TwinId}", homeId, twinId);

                var success = await _homesService.DeleteLugarAsync(homeId, twinId);

                if (success)
                {
                    return new OkObjectResult(new { message = "Home deleted successfully" });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error deleting home: {HomeId} for Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener lugar específico por ID con análisis AI
        /// GET /api/twins/{twinId}/lugares-vivienda/{homeId}/with-ai
        /// </summary>
        [Function("GetHomeByIdWithAI")]
        public async Task<IActionResult> GetHomeByIdWithAI(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/lugares-vivienda/{homeId}/with-ai")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("🔍 Getting home with AI analysis: {HomeId} for Twin: {TwinId}", homeId, twinId);

                var home = await _homesService.GetLugarByIdWithAIAnalysisAsync(homeId, twinId);

                if (home != null)
                {
                    var response = new
                    {
                        twinId = twinId,
                        homeId = homeId,
                        hasAIAnalysis = home.AIAnalysis != null,
                        home = home
                    };

                    _logger.LogInformation("✅ Home retrieved with AI analysis: {Direccion} (AI: {HasAI})",
                        home.Direccion, home.AIAnalysis != null);
                    return new OkObjectResult(response);
                }
                else
                {
                    return new NotFoundObjectResult(new { error = "Home not found", homeId = homeId, twinId = twinId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting home with AI analysis: {HomeId} for Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Subir múltiples fotos para una casa específica
        /// POST /api/twins/{twinId}/lugares-vivienda/{homeId}/photos
        /// </summary>
        [Function("UploadHomePhotos")]
        public async Task<IActionResult> UploadHomePhotos(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/lugares-vivienda/{homeId}/photos")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("📸 Uploading photos for Home: {HomeId}, Twin: {TwinId}", homeId, twinId);

                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(homeId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID and Home ID parameters are required" });
                }

                // Verificar que la casa existe
                var home = await _homesService.GetLugarByIdAsync(homeId, twinId);
                if (home == null)
                {
                    return new NotFoundObjectResult(new { error = "Home not found" });
                }

                // Procesar el JSON request body que contiene las URLs de las fotos
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    return new BadRequestObjectResult(new { error = "Request body is required" });
                }

                HomePhotoUploadRequest? uploadRequest;
                try
                {
                    uploadRequest = JsonConvert.DeserializeObject<HomePhotoUploadRequest>(requestBody);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                }

                if (uploadRequest?.PhotoUrls == null || !uploadRequest.PhotoUrls.Any())
                {
                    return new BadRequestObjectResult(new { error = "PhotoUrls array is required and must contain at least one URL" });
                }

                _logger.LogInformation("📸 Processing {Count} photo URLs for home {HomeId}", uploadRequest.PhotoUrls.Count, homeId);

                // Setup DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                var uploadResults = new List<HomePhotoUploadResult>();
                var successfulUploads = 0;

                // Procesar cada URL de foto
                for (int i = 0; i < uploadRequest.PhotoUrls.Count; i++)
                {
                    var photoUrl = uploadRequest.PhotoUrls[i];

                    try
                    {
                        _logger.LogInformation("📸 Processing photo {Index}/{Total}: {Url}", i + 1, uploadRequest.PhotoUrls.Count, photoUrl);

                        var result = await ProcessHomePhotoUrl(photoUrl, twinId, homeId, dataLakeClient, i + 1);
                        uploadResults.Add(result);

                        if (result.Success)
                        {
                            successfulUploads++;
                            _logger.LogInformation("✅ Successfully uploaded photo {Index}: {FileName}", i + 1, result.FileName);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload photo {Index}: {Error}", i + 1, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing photo {Index}: {Url}", i + 1, photoUrl);

                        uploadResults.Add(new HomePhotoUploadResult
                        {
                            Success = false,
                            ErrorMessage = $"Unexpected error: {ex.Message}",
                            PhotoUrl = photoUrl
                        });
                    }
                }

                var responseData = new HomePhotosUploadResponse
                {
                    Success = true,
                    TwinId = twinId,
                    HomeId = homeId,
                    TotalPhotos = uploadRequest.PhotoUrls.Count,
                    SuccessfulUploads = successfulUploads,
                    FailedUploads = uploadRequest.PhotoUrls.Count - successfulUploads,
                    Results = uploadResults,
                    Message = $"Processed {uploadRequest.PhotoUrls.Count} photos: {successfulUploads} successful, {uploadRequest.PhotoUrls.Count - successfulUploads} failed"
                };

                _logger.LogInformation("📸 Photo upload completed: {Successful}/{Total} successful",
                    successfulUploads, uploadRequest.PhotoUrls.Count);

                return new OkObjectResult(responseData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error uploading photos for Home: {HomeId}, Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener todas las fotos de una casa específica
        /// GET /api/twins/{twinId}/lugares-vivienda/{homeId}/photos
        /// </summary>
        [Function("GetHomePhotos")]
        public async Task<IActionResult> GetHomePhotos(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/lugares-vivienda/{homeId}/photos")] HttpRequest req,
            string twinId,
            string homeId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("📸 Getting photos for Home: {HomeId}, Twin: {TwinId}", homeId, twinId);

                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(homeId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID and Home ID parameters are required" });
                }

                // Verificar que la casa existe
                var home = await _homesService.GetLugarByIdAsync(homeId, twinId);
                if (home == null)
                {
                    return new NotFoundObjectResult(new { error = "Home not found" });
                }

                // Setup DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Construir el path del directorio de fotos
                var photosDirectoryPath = $"{homeId}/photos/";

                _logger.LogInformation("📸 Listing photos in directory: {Directory}", photosDirectoryPath);

                // Obtener lista de archivos en el directorio de fotos
                var photoFiles = await GetHomePhotosFromDirectory(dataLakeClient, photosDirectoryPath, twinId, homeId);

                var responseData = new HomePhotosListResponse
                {
                    Success = true,
                    TwinId = twinId,
                    HomeId = homeId,
                    TotalPhotos = photoFiles.Count,
                    Photos = photoFiles,
                    Message = $"Found {photoFiles.Count} photos for home {homeId}"
                };

                _logger.LogInformation("📸 Found {Count} photos for home {HomeId}", photoFiles.Count, homeId);

                return new OkObjectResult(responseData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting photos for Home: {HomeId}, Twin: {TwinId}", homeId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // ===== HELPER METHODS FOR HOME PHOTOS =====

        /// <summary>
        /// Procesa una URL de foto individual para una casa
        /// </summary>
        private async Task<HomePhotoUploadResult> ProcessHomePhotoUrl(string photoUrl, string twinId, string homeId, DataLakeClient dataLakeClient, int photoIndex)
        {
            try
            {
                // Validar la URL
                if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri))
                {
                    return new HomePhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid URL format",
                        PhotoUrl = photoUrl
                    };
                }

                // Descargar la imagen de la URL
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync(photoUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return new HomePhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to download image: HTTP {response.StatusCode}",
                        PhotoUrl = photoUrl
                    };
                }

                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                if (imageBytes.Length == 0)
                {
                    return new HomePhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Downloaded image is empty",
                        PhotoUrl = photoUrl
                    };
                }

                // Validar que sea una imagen válida
                var fileExtension = GetFileExtensionFromBytes(imageBytes);
                if (string.IsNullOrEmpty(fileExtension))
                {
                    fileExtension = GetFileExtensionFromUrl(photoUrl);
                }

                if (!IsValidImageFile(fileExtension, imageBytes))
                {
                    return new HomePhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "File is not a valid image format",
                        PhotoUrl = photoUrl
                    };
                }

                // Generar nombre único para el archivo
                var fileName = $"home_photo_{photoIndex}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
                var filePath = $"{homeId}/photos/{fileName}";

                _logger.LogInformation("📸 Uploading to DataLake: Container={Container}, Path={Path}, Size={Size} bytes",
                    twinId.ToLowerInvariant(), filePath, imageBytes.Length);

                // Subir al Data Lake
                using var fileStream = new MemoryStream(imageBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(),
                    $"{homeId}/photos",
                    fileName,
                    fileStream,
                    GetMimeTypeFromExtension(fileExtension)
                );

                if (!uploadSuccess)
                {
                    return new HomePhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to upload to Data Lake storage",
                        PhotoUrl = photoUrl
                    };
                }

                // Generar SAS URL para acceso temporal (24 horas)
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

                return new HomePhotoUploadResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = filePath,
                    SasUrl = sasUrl ?? string.Empty,
                    FileSize = imageBytes.Length,
                    MimeType = GetMimeTypeFromExtension(fileExtension),
                    PhotoUrl = photoUrl,
                    UploadedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing home photo URL: {Url}", photoUrl);
                return new HomePhotoUploadResult
                {
                    Success = false,
                    ErrorMessage = $"Processing error: {ex.Message}",
                    PhotoUrl = photoUrl
                };
            }
        }

        /// <summary>
        /// Obtiene todas las fotos de un directorio específico de una casa
        /// </summary>
        private async Task<List<HomePhotoInfo>> GetHomePhotosFromDirectory(DataLakeClient dataLakeClient, string directoryPath, string twinId, string homeId)
        {
            try
            {
                _logger.LogInformation("📸 Fetching home photos from directory: {Directory}", directoryPath);

                // Obtener lista de archivos del directorio
                var files = await dataLakeClient.ListFilesAsync(directoryPath);

                var photoList = new List<HomePhotoInfo>();

                foreach (var file in files)
                {
                    try
                    {
                        // Verificar que sea un archivo de imagen válido
                        if (!IsValidImageFileByName(file.Name))
                        {
                            _logger.LogDebug("📸 Skipping non-image file: {FileName}", file.Name);
                            continue;
                        }

                        // Generar SAS URL para el archivo (24 horas)
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(file.Name, TimeSpan.FromHours(24));

                        var photoInfo = new HomePhotoInfo
                        {
                            FileName = Path.GetFileName(file.Name),
                            FilePath = file.Name,
                            SasUrl = sasUrl ?? string.Empty,
                            FileSize = file.Size,
                            MimeType = GetMimeTypeFromExtension(Path.GetExtension(file.Name)),
                            CreatedAt = file.CreatedOn,
                            LastModified = file.LastModified,
                            TwinId = twinId,
                            HomeId = homeId
                        };

                        photoList.Add(photoInfo);

                        _logger.LogDebug("📸 Added home photo: {FileName}, Size: {Size} bytes", photoInfo.FileName, photoInfo.FileSize);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error processing home photo file {FileName}, skipping", file.Name);
                        continue;
                    }
                }

                // Ordenar por fecha de creación (más recientes primero)
                photoList = photoList.OrderByDescending(p => p.CreatedAt).ToList();

                _logger.LogInformation("📸 Successfully processed {Count} home photos from directory", photoList.Count);

                return photoList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting home photos from directory: {Directory}", directoryPath);
                return new List<HomePhotoInfo>();
            }
        }

        /// <summary>
        /// Verifica si un archivo es una imagen válida basándose en su nombre
        /// </summary>
        private bool IsValidImageFileByName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

            return validExtensions.Contains(extension);
        }

        /// <summary>
        /// Valida si el archivo es una imagen válida
        /// </summary>
        private bool IsValidImageFile(string extension, byte[] fileData)
        {
            if (string.IsNullOrEmpty(extension) || fileData == null || fileData.Length == 0)
                return false;

            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            if (!validExtensions.Contains(extension.ToLowerInvariant()))
                return false;

            if (fileData.Length < 4)
                return false;

            // Verificar magic numbers
            if (fileData[0] == 0xFF && fileData[1] == 0xD8 && fileData[2] == 0xFF) return true; // JPEG
            if (fileData[0] == 0x89 && fileData[1] == 0x50 && fileData[2] == 0x4E && fileData[3] == 0x47) return true; // PNG
            if (fileData[0] == 0x47 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x38) return true; // GIF
            if (fileData.Length >= 12 && fileData[0] == 0x52 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x46 &&
                fileData[8] == 0x57 && fileData[9] == 0x45 && fileData[10] == 0x42 && fileData[11] == 0x50) return true; // WEBP
            if (fileData[0] == 0x42 && fileData[1] == 0x4D) return true; // BMP

            return true; // Allow other formats for now
        }

        /// <summary>
        /// Obtiene la extensión del archivo basándose en los magic numbers
        /// </summary>
        private string GetFileExtensionFromBytes(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return ".jpg";

            if (fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF) return ".jpg"; // JPEG
            if (fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47) return ".png"; // PNG
            if (fileBytes.Length >= 6 && fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46) return ".gif"; // GIF
            if (fileBytes.Length >= 12 && fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 &&
                fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50) return ".webp"; // WEBP
            if (fileBytes.Length >= 2 && fileBytes[0] == 0x42 && fileBytes[1] == 0x4D) return ".bmp"; // BMP

            return ".jpg"; // Default fallback
        }

        /// <summary>
        /// Obtiene la extensión del archivo desde la URL
        /// </summary>
        private string GetFileExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var extension = Path.GetExtension(uri.LocalPath);

                if (!string.IsNullOrEmpty(extension))
                {
                    return extension.ToLowerInvariant();
                }
            }
            catch
            {
                // Ignore errors
            }

            return ".jpg"; // Default
        }

        /// <summary>
        /// Obtiene el MIME type basándose en la extensión
        /// </summary>
        private string GetMimeTypeFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
        }
    }

    // ===== HOME PHOTOS REQUEST/RESPONSE MODELS =====

    /// <summary>
    /// Request model para subir fotos de una casa desde URLs
    /// </summary>
    public class HomePhotoUploadRequest
    {
        /// <summary>
        /// Lista de URLs de fotos para descargar y subir
        /// </summary>
        public List<string> PhotoUrls { get; set; } = new List<string>();
    }

    /// <summary>
    /// Resultado del procesamiento de una foto individual de una casa
    /// </summary>
    public class HomePhotoUploadResult
    {
        /// <summary>
        /// Indica si la subida fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// URL original de la foto
        /// </summary>
        public string PhotoUrl { get; set; } = string.Empty;

        /// <summary>
        /// Nombre del archivo generado
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Ruta completa del archivo en Data Lake
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// URL SAS para acceso temporal al archivo (24 horas)
        /// </summary>
        public string SasUrl { get; set; } = string.Empty;

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Fecha y hora de subida
        /// </summary>
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// Mensaje de error si Success = false
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta completa del endpoint de subida de fotos de casa
    /// </summary>
    public class HomePhotosUploadResponse
    {
        /// <summary>
        /// Indica si la operación general fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// ID del Twin
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// ID de la casa
        /// </summary>
        public string HomeId { get; set; } = string.Empty;

        /// <summary>
        /// Número total de fotos procesadas
        /// </summary>
        public int TotalPhotos { get; set; }

        /// <summary>
        /// Número de subidas exitosas
        /// </summary>
        public int SuccessfulUploads { get; set; }

        /// <summary>
        /// Número de subidas fallidas
        /// </summary>
        public int FailedUploads { get; set; }

        /// <summary>
        /// Resultados detallados de cada foto
        /// </summary>
        public List<HomePhotoUploadResult> Results { get; set; } = new List<HomePhotoUploadResult>();

        /// <summary>
        /// Mensaje descriptivo del resultado
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Información de una foto individual de una casa
    /// </summary>
    public class HomePhotoInfo
    {
        /// <summary>
        /// Nombre del archivo
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Ruta completa del archivo en Data Lake
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// URL SAS para acceso temporal al archivo (24 horas)
        /// </summary>
        public string SasUrl { get; set; } = string.Empty;

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de creación del archivo
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Fecha de última modificación
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// ID del Twin propietario
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// ID de la casa
        /// </summary>
        public string HomeId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta del endpoint para listar fotos de una casa
    /// </summary>
    public class HomePhotosListResponse
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
        /// ID de la casa
        /// </summary>
        public string HomeId { get; set; } = string.Empty;

        /// <summary>
        /// Número total de fotos encontradas
        /// </summary>
        public int TotalPhotos { get; set; }

        /// <summary>
        /// Lista de fotos con información detallada
        /// </summary>
        public List<HomePhotoInfo> Photos { get; set; } = new List<HomePhotoInfo>();

        /// <summary>
        /// Mensaje descriptivo del resultado
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}