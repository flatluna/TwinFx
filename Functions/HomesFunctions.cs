using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinFx.Services;
using TwinFx.Models;
using System.Text;

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

        public HomesFunctions(ILogger<HomesFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            
            // Initialize Homes service
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? "",
                Key = configuration["Values:COSMOS_KEY"] ?? "",
                DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });
            
            _homesService = new HomesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesCosmosDbService>(),
                cosmosOptions);
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

                var response = new { 
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

                var response = new { 
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
    }
}