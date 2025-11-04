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
    /// Azure Functions para operaciones CRUD de Alimentos
    /// Container: TwinAlimentos, PartitionKey: TwinID
    /// </summary>
    public class FoodFunctions
    {
        private readonly ILogger<FoodFunctions> _logger;
        private readonly CosmosDbService _cosmosService;

        public FoodFunctions(ILogger<FoodFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _cosmosService = configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());
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

        /// <summary>
        /// Handle CORS preflight for /api/foods
        /// </summary>
        [Function("FoodsOptions")]
        public IActionResult HandleFoodsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods")] HttpRequest req)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods");
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/foods/{foodId}  
        /// </summary>
        [Function("FoodsByFoodIdOptions")]
        public IActionResult HandleFoodsByFoodIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/{foodId}")] HttpRequest req,
            string foodId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods/{FoodId}", foodId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/foods/{twinId}
        /// </summary>
        [Function("FoodsTwinIdOptions")]
        public IActionResult HandleFoodsTwinIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/{twinId}")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods/{TwinId}", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/foods/{twinId}/{foodId}
        /// </summary>
        [Function("FoodsByIdOptions")]
        public IActionResult HandleFoodsByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/{twinId}/{foodId}")] HttpRequest req,
            string twinId,
            string foodId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods/{TwinId}/{FoodId}", twinId, foodId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/foods/{twinId}/filter
        /// </summary>
        [Function("FoodsFilterOptions")]
        public IActionResult HandleFoodsFilterOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/{twinId}/filter")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods/{TwinId}/filter", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/foods/{twinId}/stats
        /// </summary>
        [Function("FoodsStatsOptions")]
        public IActionResult HandleFoodsStatsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/{twinId}/stats")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods/{TwinId}/stats", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/foods/{twinId}/search
        /// </summary>
        [Function("FoodsSearchOptions")]
        public IActionResult HandleFoodsSearchOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/{twinId}/search")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/foods/{TwinId}/search", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Crear un nuevo alimento
        /// POST /api/foods
        /// </summary>
        [Function("CreateFood")]
        public async Task<IActionResult> CreateFood(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods")] HttpRequest req)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Creating new food");

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("?? Request body received: {RequestBody}", requestBody);

                var foodData = JsonConvert.DeserializeObject<Models.FoodData>(requestBody);

                if (foodData == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid food data provided" });
                }

                // Log the deserialized food data for debugging
                _logger.LogInformation("?? Deserialized food data: NombreAlimento={NombreAlimento}, Proteinas={Proteinas}, Carbohidratos={Carbohidratos}, Grasas={Grasas}, Fibra={Fibra}", 
                    foodData.NombreAlimento, foodData.Proteinas, foodData.Carbohidratos, foodData.Grasas, foodData.Fibra);

                // Validar campos requeridos
                if (string.IsNullOrEmpty(foodData.TwinID))
                {
                    return new BadRequestObjectResult(new { error = "TwinID is required" });
                }

                if (string.IsNullOrEmpty(foodData.NombreAlimento))
                {
                    return new BadRequestObjectResult(new { error = "NombreAlimento is required" });
                }

                if (string.IsNullOrEmpty(foodData.Categoria))
                {
                    return new BadRequestObjectResult(new { error = "Categoria is required" });
                }

                if (foodData.CaloriasPor100g < 0)
                {
                    return new BadRequestObjectResult(new { error = "CaloriasPor100g must be greater than or equal to 0" });
                }

                // ? Normalizar valores nutricionales - garantizar que no sean null
                foodData.Proteinas = foodData.Proteinas ?? 0.0;
                foodData.Carbohidratos = foodData.Carbohidratos ?? 0.0;
                foodData.Grasas = foodData.Grasas ?? 0.0;
                foodData.Fibra = foodData.Fibra ?? 0.0;

                // Generar ID si no se proporciona
                if (string.IsNullOrEmpty(foodData.Id))
                {
                    foodData.Id = Guid.NewGuid().ToString();
                }

                // Configurar campos de sistema
                foodData.FechaCreacion = DateTime.UtcNow.ToString("O");
                foodData.FechaActualizacion = DateTime.UtcNow.ToString("O");

                _logger.LogInformation("?? Sending to Cosmos: Proteinas={Proteinas}, Carbohidratos={Carbohidratos}, Grasas={Grasas}, Fibra={Fibra}", 
                    foodData.Proteinas, foodData.Carbohidratos, foodData.Grasas, foodData.Fibra);

                var success = await _cosmosService.CreateFoodAsync(foodData);

                if (success)
                {
                    _logger.LogInformation("? Food created successfully in Cosmos DB");

                    // Verificar que la respuesta no contiene nulls
                    var responseData = new 
                    { 
                        message = "Food created successfully", 
                        id = foodData.Id,
                        foodData = new
                        {
                            id = foodData.Id,
                            twinID = foodData.TwinID,
                            nombreAlimento = foodData.NombreAlimento,
                            categoria = foodData.Categoria,
                            caloriasPor100g = foodData.CaloriasPor100g,
                            proteinas = foodData.Proteinas, // Nunca null
                            carbohidratos = foodData.Carbohidratos, // Nunca null
                            grasas = foodData.Grasas, // Nunca null
                            fibra = foodData.Fibra, // Nunca null
                            caloriasUnidadComun = foodData.CaloriasUnidadComun,
                            unidadComun = foodData.UnidadComun,
                            cantidadComun = foodData.CantidadComun,
                            descripcion = foodData.Descripcion,
                            fechaCreacion = foodData.FechaCreacion,
                            fechaActualizacion = foodData.FechaActualizacion,
                            type = foodData.Type
                        }
                    };

                    _logger.LogInformation("?? Returning response: {Response}", JsonConvert.SerializeObject(responseData));
                    return new OkObjectResult(responseData);
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error creating food");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener todos los alimentos de un twin
        /// GET /api/foods/{twinId}
        /// </summary>
        [Function("GetFoodsByTwinId")]
        public async Task<IActionResult> GetFoodsByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/{twinId}")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting foods for Twin: {TwinId}", twinId);

                var foods = await _cosmosService.GetFoodsByTwinIdAsync(twinId);

                _logger.LogInformation("?? Retrieved {Count} foods from Cosmos DB", foods.Count);

                // Log primera food para debugging si existe
                if (foods.Count > 0)
                {
                    var firstFood = foods[0];
                    _logger.LogInformation("?? First food example: {NombreAlimento}, Proteinas={Proteinas}, Carbohidratos={Carbohidratos}, Grasas={Grasas}, Fibra={Fibra}", 
                        firstFood.NombreAlimento, firstFood.Proteinas, firstFood.Carbohidratos, firstFood.Grasas, firstFood.Fibra);
                }

                // Asegurar que ningún campo nutricional sea null antes de enviar respuesta
                var sanitizedFoods = foods.Select(food => new
                {
                    id = food.Id,
                    twinID = food.TwinID,
                    nombreAlimento = food.NombreAlimento,
                    categoria = food.Categoria,
                    caloriasPor100g = food.CaloriasPor100g,
                    proteinas = food.Proteinas ?? 0.0, // ? Garantizar no-null
                    carbohidratos = food.Carbohidratos ?? 0.0, // ? Garantizar no-null
                    grasas = food.Grasas ?? 0.0, // ? Garantizar no-null
                    fibra = food.Fibra ?? 0.0, // ? Garantizar no-null
                    caloriasUnidadComun = food.CaloriasUnidadComun,
                    unidadComun = food.UnidadComun,
                    cantidadComun = food.CantidadComun,
                    descripcion = food.Descripcion,
                    fechaCreacion = food.FechaCreacion,
                    fechaActualizacion = food.FechaActualizacion,
                    type = food.Type
                }).ToList();

                var response = new { 
                    twinId = twinId,
                    count = sanitizedFoods.Count,
                    foods = sanitizedFoods
                };

                _logger.LogInformation("? Returning sanitized response with {Count} foods", sanitizedFoods.Count);
                return new OkObjectResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting foods for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener un alimento específico por ID
        /// GET /api/foods/{twinId}/{foodId}
        /// </summary>
        [Function("GetFoodById")]
        public async Task<IActionResult> GetFoodById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/{twinId}/{foodId}")] HttpRequest req,
            string twinId,
            string foodId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting food: {FoodId} for Twin: {TwinId}", foodId, twinId);

                var food = await _cosmosService.GetFoodByIdAsync(foodId, twinId);

                if (food != null)
                {
                    return new OkObjectResult(food);
                }
                else
                {
                    return new NotFoundObjectResult(new { error = "Food not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting food: {FoodId} for Twin: {TwinId}", foodId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Actualizar un alimento existente (URL alternativa para compatibilidad con frontend)
        /// PUT /api/foods/{foodId}?twinID={twinID}
        /// </summary>
        [Function("UpdateFoodAlt")]
        public async Task<IActionResult> UpdateFoodAlt(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "foods/{foodId}")] HttpRequest req,
            string foodId)
        {
            try
            {
                AddCorsHeaders(req);
                
                // Get twinID from query parameter
                var twinId = req.Query["twinID"].FirstOrDefault();
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "twinID query parameter is required" });
                }

                _logger.LogInformation("?? Updating food: {FoodId} for Twin: {TwinId} (alt route)", foodId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("?? Request body received: {RequestBody}", requestBody);

                var foodData = JsonConvert.DeserializeObject<Models.FoodData>(requestBody);

                if (foodData == null)
                {
                    _logger.LogError("? Failed to deserialize food data from request body");
                    return new BadRequestObjectResult(new { error = "Invalid food data provided" });
                }

                // Log the deserialized food data for debugging
                _logger.LogInformation("?? Deserialized food data: NombreAlimento={NombreAlimento}, Proteinas={Proteinas}, Carbohidratos={Carbohidratos}, Grasas={Grasas}, Fibra={Fibra}", 
                    foodData.NombreAlimento, foodData.Proteinas, foodData.Carbohidratos, foodData.Grasas, foodData.Fibra);

                // Asegurar que los IDs coincidan con los parámetros
                foodData.Id = foodId;
                foodData.TwinID = twinId;

                // Validar campos requeridos
                if (string.IsNullOrEmpty(foodData.NombreAlimento))
                {
                    return new BadRequestObjectResult(new { error = "NombreAlimento is required" });
                }

                if (string.IsNullOrEmpty(foodData.Categoria))
                {
                    return new BadRequestObjectResult(new { error = "Categoria is required" });
                }

                if (foodData.CaloriasPor100g < 0)
                {
                    return new BadRequestObjectResult(new { error = "CaloriasPor100g must be greater than or equal to 0" });
                }

                // ? Normalizar valores nutricionales - garantizar que no sean null
                foodData.Proteinas = foodData.Proteinas ?? 0.0;
                foodData.Carbohidratos = foodData.Carbohidratos ?? 0.0;
                foodData.Grasas = foodData.Grasas ?? 0.0;
                foodData.Fibra = foodData.Fibra ?? 0.0;

                // Update timestamp
                foodData.FechaActualizacion = DateTime.UtcNow.ToString("O");

                _logger.LogInformation("?? Sending to Cosmos: Proteinas={Proteinas}, Carbohidratos={Carbohidratos}, Grasas={Grasas}, Fibra={Fibra}", 
                    foodData.Proteinas, foodData.Carbohidratos, foodData.Grasas, foodData.Fibra);

                var success = await _cosmosService.UpdateFoodAsync(foodData);

                if (success)
                {
                    _logger.LogInformation("? Food updated successfully in Cosmos DB");

                    // Verificar que la respuesta no contiene nulls
                    var responseData = new 
                    { 
                        message = "Food updated successfully",
                        foodData = new
                        {
                            id = foodData.Id,
                            twinID = foodData.TwinID,
                            nombreAlimento = foodData.NombreAlimento,
                            categoria = foodData.Categoria,
                            caloriasPor100g = foodData.CaloriasPor100g,
                            proteinas = foodData.Proteinas, // Nunca null
                            carbohidratos = foodData.Carbohidratos, // Nunca null
                            grasas = foodData.Grasas, // Nunca null
                            fibra = foodData.Fibra, // Nunca null
                            caloriasUnidadComun = foodData.CaloriasUnidadComun,
                            unidadComun = foodData.UnidadComun,
                            cantidadComun = foodData.CantidadComun,
                            descripcion = foodData.Descripcion,
                            fechaCreacion = foodData.FechaCreacion,
                            fechaActualizacion = foodData.FechaActualizacion,
                            type = foodData.Type
                        }
                    };

                    _logger.LogInformation("?? Returning response: {Response}", JsonConvert.SerializeObject(responseData));
                    return new OkObjectResult(responseData);
                }
                else
                {
                    _logger.LogError("? Failed to update food in Cosmos DB");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error updating food: {FoodId}", foodId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Actualizar un alimento existente (ruta estándar)
        /// PUT /api/foods/{twinId}/{foodId}
        /// </summary>
        [Function("UpdateFood")]
        public async Task<IActionResult> UpdateFood(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "foods/{twinId}/{foodId}")] HttpRequest req,
            string twinId,
            string foodId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Updating food: {FoodId} for Twin: {TwinId}", foodId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var foodData = JsonConvert.DeserializeObject<Models.FoodData>(requestBody);

                if (foodData == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid food data provided" });
                }

                // Asegurar que los IDs coincidan con los parámetros de la ruta
                foodData.Id = foodId;
                foodData.TwinID = twinId;

                // Validar campos requeridos
                if (string.IsNullOrEmpty(foodData.NombreAlimento))
                {
                    return new BadRequestObjectResult(new { error = "NombreAlimento is required" });
                }

                if (string.IsNullOrEmpty(foodData.Categoria))
                {
                    return new BadRequestObjectResult(new { error = "Categoria is required" });
                }

                if (foodData.CaloriasPor100g < 0)
                {
                    return new BadRequestObjectResult(new { error = "CaloriasPor100g must be greater than or equal to 0" });
                }

                // Update timestamp
                foodData.FechaActualizacion = DateTime.UtcNow.ToString("O");

                var success = await _cosmosService.UpdateFoodAsync(foodData);

                if (success)
                {
                    return new OkObjectResult(new { 
                        message = "Food updated successfully",
                        foodData = foodData
                    });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error updating food: {FoodId} for Twin: {TwinId}", foodId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Eliminar un alimento (URL alternativa para compatibilidad con frontend)
        /// DELETE /api/foods/{foodId}?twinID={twinID}
        /// </summary>
        [Function("DeleteFoodAlt")]
        public async Task<IActionResult> DeleteFoodAlt(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "foods/{foodId}")] HttpRequest req,
            string foodId)
        {
            try
            {
                AddCorsHeaders(req);
                
                // Get twinID from query parameter
                var twinId = req.Query["twinID"].FirstOrDefault();
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "twinID query parameter is required" });
                }

                _logger.LogInformation("??? Deleting food: {FoodId} for Twin: {TwinId} (alt route)", foodId, twinId);

                var success = await _cosmosService.DeleteFoodAsync(foodId, twinId);

                if (success)
                {
                    return new OkObjectResult(new { message = "Food deleted successfully" });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error deleting food: {FoodId}", foodId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Eliminar un alimento (ruta estándar)
        /// DELETE /api/foods/{twinId}/{foodId}
        /// </summary>
        [Function("DeleteFood")]
        public async Task<IActionResult> DeleteFood(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "foods/{twinId}/{foodId}")] HttpRequest req,
            string twinId,
            string foodId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("??? Deleting food: {FoodId} for Twin: {TwinId}", foodId, twinId);

                var success = await _cosmosService.DeleteFoodAsync(foodId, twinId);

                if (success)
                {
                    return new OkObjectResult(new { message = "Food deleted successfully" });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error deleting food: {FoodId} for Twin: {TwinId}", foodId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener alimentos con filtros avanzados
        /// GET /api/foods/{twinId}/filter?categoria=Frutas&caloriasMin=50&caloriasMax=200&nombre=banana&orderBy=calorias&orderDirection=DESC&page=1&pageSize=20
        /// </summary>
        [Function("GetFilteredFoods")]
        public async Task<IActionResult> GetFilteredFoods(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/{twinId}/filter")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting filtered foods for Twin: {TwinId}", twinId);

                var query = new FoodQuery
                {
                    Categoria = req.Query["categoria"].FirstOrDefault(),
                    NombreContiene = req.Query["nombre"].FirstOrDefault(),
                    OrderBy = req.Query["orderBy"].FirstOrDefault() ?? "nombreAlimento",
                    OrderDirection = req.Query["orderDirection"].FirstOrDefault() ?? "ASC"
                };

                // Parsear filtros de calorías
                if (double.TryParse(req.Query["caloriasMin"].FirstOrDefault(), out var caloriasMin))
                {
                    query.CaloriasMin = caloriasMin;
                }

                if (double.TryParse(req.Query["caloriasMax"].FirstOrDefault(), out var caloriasMax))
                {
                    query.CaloriasMax = caloriasMax;
                }

                // Parsear paginación
                if (int.TryParse(req.Query["page"].FirstOrDefault(), out var page))
                {
                    query.Page = page;
                }

                if (int.TryParse(req.Query["pageSize"].FirstOrDefault(), out var pageSize))
                {
                    query.PageSize = pageSize;
                }

                var foods = await _cosmosService.GetFilteredFoodsAsync(twinId, query);

                return new OkObjectResult(new { 
                    twinId = twinId,
                    filters = query,
                    count = foods.Count,
                    foods = foods
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting filtered foods for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Obtener estadísticas nutricionales
        /// GET /api/foods/{twinId}/stats
        /// </summary>
        [Function("GetFoodStats")]
        public async Task<IActionResult> GetFoodStats(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/{twinId}/stats")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Getting food stats for Twin: {TwinId}", twinId);

                var stats = await _cosmosService.GetFoodStatsAsync(twinId);

                return new OkObjectResult(new { 
                    twinId = twinId,
                    stats = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting food stats for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Buscar alimentos por nombre
        /// GET /api/foods/{twinId}/search?q=banana&limit=10
        /// </summary>
        [Function("SearchFoods")]
        public async Task<IActionResult> SearchFoods(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/{twinId}/search")] HttpRequest req,
            string twinId)
        {
            try
            {
                AddCorsHeaders(req);
                _logger.LogInformation("?? Searching foods for Twin: {TwinId}", twinId);

                var searchTerm = req.Query["q"].FirstOrDefault();
                if (string.IsNullOrEmpty(searchTerm))
                {
                    return new BadRequestObjectResult(new { error = "Search term 'q' is required" });
                }

                var limitStr = req.Query["limit"].FirstOrDefault();
                var limit = 20; // Default
                if (!string.IsNullOrEmpty(limitStr) && int.TryParse(limitStr, out var parsedLimit))
                {
                    limit = Math.Max(1, Math.Min(100, parsedLimit)); // Entre 1 y 100
                }

                var foods = await _cosmosService.SearchFoodsByNameAsync(twinId, searchTerm, limit);

                return new OkObjectResult(new { 
                    twinId = twinId,
                    searchTerm = searchTerm,
                    limit = limit,
                    count = foods.Count,
                    foods = foods
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error searching foods for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}