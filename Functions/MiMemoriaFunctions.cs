using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TwinFx.Models;

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions para gestión de memorias
/// ========================================================================
/// 
/// Proporciona endpoints para:
/// - Crear nuevas memorias
/// - Obtener memorias por Twin ID
/// - Actualizar memorias existentes
/// - Eliminar memorias
/// - Buscar memorias
/// 
/// Author: TwinFx Project
/// Date: January 2025
/// </summary>
public class MiMemoriaFunctions
{
    private readonly ILogger<MiMemoriaFunctions> _logger;
    private readonly IConfiguration _configuration;

    public MiMemoriaFunctions(ILogger<MiMemoriaFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // ========================================
    // OPTIONS HANDLERS FOR CORS
    // ========================================

    [Function("MemoriasOptions")]
    public IActionResult HandleMemoriasOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/memorias")] HttpRequest req, 
        string twinId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/memorias", twinId);
        AddCorsHeaders(req);
        return new OkResult();
    }

    [Function("MemoriasIdOptions")]
    public IActionResult HandleMemoriasIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/memorias/{memoriaId}")] HttpRequest req, 
        string twinId, string memoriaId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/memorias/{MemoriaId}", twinId, memoriaId);
        AddCorsHeaders(req);
        return new OkResult();
    }

    [Function("MemoriasSearchOptions")]
    public IActionResult HandleMemoriasSearchOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/memorias/search")] HttpRequest req, 
        string twinId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/memorias/search", twinId);
        AddCorsHeaders(req);
        return new OkResult();
    }

    // ========================================
    // CRUD OPERATIONS
    // ========================================

    /// <summary>
    /// Crear nueva memoria
    /// POST /api/twins/{twinId}/memorias
    /// </summary>
    [Function("CreateMemoria")]
    public async Task<IActionResult> CreateMemoria(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/memorias")] HttpRequest req,
        string twinId)
    {
        _logger.LogInformation("🧠 CreateMemoria function triggered for Twin ID: {TwinId}", twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID parameter is required" });
            }

            // Leer el cuerpo de la solicitud
            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("❌ Request body is required");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Request body is required" });
            }

            _logger.LogInformation("🧠 Request body received: {Length} characters", requestBody.Length);

            // Parsear el JSON del request
            MiMemoria? memoria;
            try
            {
                memoria = JsonSerializer.Deserialize<MiMemoria>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ Invalid JSON format in request body");
                return new BadRequestObjectResult(new { success = false, errorMessage = $"Invalid JSON format: {ex.Message}" });
            }

            if (memoria == null)
            {
                _logger.LogError("❌ Failed to parse request body");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Failed to parse request body" });
            }

            // Validar datos básicos de la memoria
            if (string.IsNullOrEmpty(memoria.Titulo))
            {
                _logger.LogError("❌ Memory title (Titulo) is required");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Memory title (Titulo) is required" });
            }

            if (string.IsNullOrEmpty(memoria.Contenido))
            {
                _logger.LogError("❌ Memory content (Contenido) is required");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Memory content (Contenido) is required" });
            }

            if (string.IsNullOrEmpty(memoria.Categoria))
            {
                _logger.LogError("❌ Memory category (Categoria) is required");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Memory category (Categoria) is required" });
            }

            // Asignar Twin ID y generar ID de la memoria si no se proporcionó
            memoria.TwinID = twinId;
            if (string.IsNullOrEmpty(memoria.id))
            {
                memoria.id = Guid.NewGuid().ToString();
            }

            // Establecer valores por defecto si no se proporcionan
            if (string.IsNullOrEmpty(memoria.Tipo))
                memoria.Tipo = "nota";

            if (string.IsNullOrEmpty(memoria.Importancia))
                memoria.Importancia = "media";

            if (string.IsNullOrEmpty(memoria.Fecha))
                memoria.Fecha = DateTime.UtcNow.ToString("yyyy-MM-dd");

            _logger.LogInformation("🧠 Creating memory: {Titulo} for Twin: {TwinId}", memoria.Titulo, twinId);

            // Crear el servicio de Cosmos DB
            var memoriasService = CreateMemoriasService();

            // Crear la memoria en la base de datos
            var success = await memoriasService.CreateMemoriaAsync(memoria);

            if (!success)
            {
                _logger.LogError("❌ Failed to create memory in database");
                return new ObjectResult(new { success = false, errorMessage = "Failed to create memory in database" })
                {
                    StatusCode = 500
                };
            }

            _logger.LogInformation("✅ Memory created successfully: {MemoriaId}", memoria.id);

            return new ObjectResult(new
            {
                success = true,
                memoriaId = memoria.id,
                twinId = twinId,
                memoria = memoria,
                message = "Memory created successfully",
                createdAt = DateTime.UtcNow
            })
            {
                StatusCode = 201
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating memory for Twin: {TwinId}", twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// Obtener todas las memorias de un Twin
    /// GET /api/twins/{twinId}/memorias
    /// </summary>
    [Function("GetMemoriasByTwin")]
    public async Task<IActionResult> GetMemoriasByTwin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/memorias")] HttpRequest req,
        string twinId)
    {
        _logger.LogInformation("🧠 Getting memories for Twin ID: {TwinId}", twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID parameter is required" });
            }

            var memoriasService = CreateMemoriasService();
            var memorias = await memoriasService.GetMemoriasByTwinIdAsync(twinId);

            _logger.LogInformation("✅ Retrieved {Count} memories for Twin ID: {TwinId}", memorias.Count, twinId);

            return new OkObjectResult(new
            {
                success = true,
                twinId = twinId,
                memorias = memorias,
                count = memorias.Count,
                message = $"Retrieved {memorias.Count} memories for Twin {twinId}",
                retrievedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting memories for Twin: {TwinId}", twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// Obtener memoria específica por ID
    /// GET /api/twins/{twinId}/memorias/{memoriaId}
    /// </summary>
    [Function("GetMemoriaById")]
    public async Task<IActionResult> GetMemoriaById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/memorias/{memoriaId}")] HttpRequest req,
        string twinId, string memoriaId)
    {
        _logger.LogInformation("🧠 Getting memory {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(memoriaId))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID and Memory ID parameters are required" });
            }

            var memoriasService = CreateMemoriasService();
            var memoria = await memoriasService.GetMemoriaByIdAsync(memoriaId, twinId);

            if (memoria == null)
            {
                return new NotFoundObjectResult(new { success = false, errorMessage = $"Memory with ID {memoriaId} not found for Twin {twinId}" });
            }

            _logger.LogInformation("✅ Retrieved memory: {Titulo} for Twin: {TwinId}", memoria.Titulo, twinId);

            return new OkObjectResult(new
            {
                success = true,
                twinId = twinId,
                memoriaId = memoriaId,
                memoria = memoria,
                message = $"Memory '{memoria.Titulo}' retrieved successfully",
                retrievedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting memory by ID: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// Actualizar memoria existente
    /// PUT /api/twins/{twinId}/memorias/{memoriaId}
    /// </summary>
    [Function("UpdateMemoria")]
    public async Task<IActionResult> UpdateMemoria(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/memorias/{memoriaId}")] HttpRequest req,
        string twinId, string memoriaId)
    {
        _logger.LogInformation("📝 Updating memory {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(memoriaId))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID and Memory ID parameters are required" });
            }

            // Leer el cuerpo de la petición
            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            if (string.IsNullOrEmpty(requestBody))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Request body is required" });
            }

            MiMemoria? memoria;
            try
            {
                memoria = JsonSerializer.Deserialize<MiMemoria>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "⚠️ Invalid JSON in request body");
                return new BadRequestObjectResult(new { success = false, errorMessage = "Invalid JSON format in request body" });
            }

            if (memoria == null)
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Memory data is required" });
            }

            // Asegurar que los IDs sean consistentes
            memoria.TwinID = twinId;
            memoria.id = memoriaId;

            var memoriasService = CreateMemoriasService();
            var success = await memoriasService.UpdateMemoriaAsync(memoria);

            if (!success)
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to update memory or memory not found" })
                {
                    StatusCode = 500
                };
            }

            _logger.LogInformation("✅ Memory updated successfully: {Titulo} for Twin: {TwinId}", memoria.Titulo, twinId);

            return new OkObjectResult(new
            {
                success = true,
                twinId = twinId,
                memoriaId = memoriaId,
                memoria = memoria,
                message = $"Memory '{memoria.Titulo}' updated successfully",
                updatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating memory: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// Eliminar memoria
    /// DELETE /api/twins/{twinId}/memorias/{memoriaId}
    /// </summary>
    [Function("DeleteMemoria")]
    public async Task<IActionResult> DeleteMemoria(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/memorias/{memoriaId}")] HttpRequest req,
        string twinId, string memoriaId)
    {
        _logger.LogInformation("🗑️ Deleting memory {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(memoriaId))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID and Memory ID parameters are required" });
            }

            var memoriasService = CreateMemoriasService();

            // Primero obtener la memoria para logging
            var existingMemoria = await memoriasService.GetMemoriaByIdAsync(memoriaId, twinId);

            var success = await memoriasService.DeleteMemoriaAsync(memoriaId, twinId);

            if (!success)
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to delete memory or memory not found" })
                {
                    StatusCode = 500
                };
            }

            _logger.LogInformation("✅ Memory deleted successfully: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);

            return new OkObjectResult(new
            {
                success = true,
                twinId = twinId,
                memoriaId = memoriaId,
                message = existingMemoria != null 
                    ? $"Memory '{existingMemoria.Titulo}' deleted successfully"
                    : $"Memory {memoriaId} deleted successfully",
                deletedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting memory: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// Buscar memorias por término de búsqueda
    /// GET /api/twins/{twinId}/memorias/search?q={searchTerm}&limit={limit}
    /// </summary>
    [Function("SearchMemorias")]
    public async Task<IActionResult> SearchMemorias(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/memorias/search")] HttpRequest req,
        string twinId)
    {
        _logger.LogInformation("🔍 Searching memories for Twin ID: {TwinId}", twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID parameter is required" });
            }

            // Obtener parámetros de búsqueda
            var searchTerm = req.Query["q"].FirstOrDefault();
            if (string.IsNullOrEmpty(searchTerm))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Search term parameter 'q' is required" });
            }

            var limitStr = req.Query["limit"].FirstOrDefault();
            var limit = int.TryParse(limitStr, out var l) ? l : 20;

            _logger.LogInformation("🔍 Searching memories with term: '{SearchTerm}', limit: {Limit}", searchTerm, limit);

            var memoriasService = CreateMemoriasService();
            var memorias = await memoriasService.SearchMemoriasAsync(twinId, searchTerm, limit);

            _logger.LogInformation("✅ Found {Count} memories matching search term for Twin: {TwinId}", memorias.Count, twinId);

            return new OkObjectResult(new
            {
                success = true,
                twinId = twinId,
                searchTerm = searchTerm,
                limit = limit,
                memorias = memorias,
                count = memorias.Count,
                message = $"Found {memorias.Count} memories matching '{searchTerm}'",
                searchedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error searching memories for Twin: {TwinId}", twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    private MiMemoriaCosmosDB CreateMemoriasService()
    {
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
        {
            Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
            Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
            DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });

        var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MiMemoriaCosmosDB>();
        return new MiMemoriaCosmosDB(serviceLogger, cosmosOptions, _configuration);
    }

    private static void AddCorsHeaders(HttpRequest req)
    {
        req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
        req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
        req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With");
        req.HttpContext.Response.Headers.TryAdd("Access-Control-Max-Age", "86400");
        req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Credentials", "false");
    }
}