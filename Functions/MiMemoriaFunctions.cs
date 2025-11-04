using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Services; 

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
/// - Subir fotos a memorias
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

    /// <summary>
    /// Subir foto a memoria específica
    /// POST /api/twins/{twinId}/memorias/{memoriaId}/{description}/upload-photo/{*filePath}
    /// </summary>
    [Function("UploadMemoriaPhoto")]
    public async Task<IActionResult> UploadMemoriaPhoto(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/memorias/{memoriaId}/{description}/upload-photo/{*filePath}")] HttpRequest req,
        string twinId, string memoriaId, string description, string filePath)
    {
        _logger.LogInformation("📸 UploadMemoriaPhoto function triggered for Memory: {MemoriaId}, Twin: {TwinId}, Description: {Description}, FilePath: {FilePath}", 
            memoriaId, twinId, description, filePath);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(memoriaId) || string.IsNullOrEmpty(filePath))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID, Memory ID, and File Path parameters are required" });
            }

            // Verificar que la memoria existe
            var memoriasService = CreateMemoriasService();
            var memoria = await memoriasService.GetMemoriaByIdAsync(memoriaId, twinId);
            if (memoria == null)
            {
                return new NotFoundObjectResult(new { success = false, errorMessage = $"Memory with ID {memoriaId} not found for Twin {twinId}" });
            }

            // Verificar que se envió un archivo
            if (!req.HasFormContentType || req.Form.Files.Count == 0)
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "No file uploaded. Please send the image as form data." });
            }

            var uploadedFile = req.Form.Files[0];
            if (uploadedFile.Length == 0)
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Uploaded file is empty" });
            }

            // Validar que sea una imagen
            var allowedContentTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp", "image/bmp" };
            if (!allowedContentTypes.Contains(uploadedFile.ContentType?.ToLowerInvariant()))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = $"Invalid file type. Allowed types: {string.Join(", ", allowedContentTypes)}" });
            }

            _logger.LogInformation("📸 Processing photo upload: {FileName}, Size: {Size} bytes, ContentType: {ContentType}", 
                uploadedFile.FileName, uploadedFile.Length, uploadedFile.ContentType);

            // Configurar DataLake client
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Generar nombre único para el archivo
            var fileExtension = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension))
            {
                fileExtension = GetExtensionFromContentType(uploadedFile.ContentType);
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fileName = uploadedFile.FileName;
            
            // Usar el path exacto proporcionado en la URL (contenedor es TwinID)
            var containerName = twinId.ToLowerInvariant();
            var fullFilePath = filePath; ;

            _logger.LogInformation("📸 Uploading to DataLake: Container={Container}, Path={Path}", containerName, fullFilePath);

            // Subir archivo al Data Lake
            using var fileStream = uploadedFile.OpenReadStream();
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                containerName,
                fullFilePath,
                fileName,
                fileStream,
                uploadedFile.ContentType ?? "image/jpeg"
            );

            if (!uploadSuccess)
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to upload file to storage" })
                {
                    StatusCode = 500
                };
            }

            // Generar SAS URL para acceso temporal (24 horas)
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));

            // Crear objeto Photo para añadir a la memoria
            var photo = new Photo
            {
                Id = Guid.NewGuid().ToString(),
                ContainerName = containerName,
                FileName = fileName,
                Path = Path.GetDirectoryName(fullFilePath)?.Replace('\\', '/') ?? "",
                Url = sasUrl ?? "",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Title = "",
                Description = Uri.UnescapeDataString(description), // Decodificar URL encoding
                ImageAI = new ImageAI() // Se llenará posteriormente con análisis de IA
            };

            // Añadir foto a la memoria
            if (memoria.Photos == null)
            {
                memoria.Photos = new List<Photo>();
            }
            memoria.Photos.Add(photo);

            // Actualizar memoria en la base de datos
            var updateSuccess = await memoriasService.UpdateMemoriaAsync(memoria);
            if (!updateSuccess)
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to update memory with photo information" })
                {
                    StatusCode = 500
                };
            }

            _logger.LogInformation("✅ Photo uploaded successfully: {FileName} to memory: {MemoriaId}", fileName, memoriaId);

            return new ObjectResult(new
            {
                success = true,
                twinId = twinId,
                memoriaId = memoriaId,
                photo = new
                {
                    id = photo.Id,
                    fileName = photo.FileName,
                    filePath = fullFilePath,
                    containerName = photo.ContainerName,
                    sasUrl = photo.Url,
                    description = photo.Description,
                    uploadedAt = photo.CreatedAt
                },
                message = $"Photo '{fileName}' uploaded successfully to memory '{memoria.Titulo}'",
                uploadedAt = DateTime.UtcNow
            })
            {
                StatusCode = 201
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error uploading photo to memory: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// ANALYSIS
    /// Procesar análisis de IA para una foto específica de una memoria
    /// POST /api/twins/{twinId}/memorias/{memoriaId}/photos/{photoId}/analyze
    /// </summary>
    [Function("AnalyzeMemoriaPhoto")]
    public async Task<IActionResult> AnalyzeMemoriaPhoto(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/memorias/{memoriaId}/photos/{photoId}/analyze")] HttpRequest req,
        string twinId, string memoriaId, string photoId)
    {
        _logger.LogInformation("🔍 AnalyzeMemoriaPhoto function triggered for Photo: {PhotoId}, Memory: {MemoriaId}, Twin: {TwinId}", 
            photoId, memoriaId, twinId);
        AddCorsHeaders(req);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(memoriaId) || string.IsNullOrEmpty(photoId))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID, Memory ID, and Photo ID parameters are required" });
            }

            // Obtener la memoria y verificar que existe
            var memoriasService = CreateMemoriasService();
            var memoria = await memoriasService.GetMemoriaByIdAsync(memoriaId, twinId);
            if (memoria == null)
            {
                return new NotFoundObjectResult(new { success = false, errorMessage = $"Memory with ID {memoriaId} not found for Twin {twinId}" });
            }

            // Verificar que se envió un archivo
            if (!req.HasFormContentType || req.Form.Files.Count == 0)
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "No file uploaded. Please send the image as form data." });
            }

            var uploadedFile = req.Form.Files[0];
            if (uploadedFile.Length == 0)
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = "Uploaded file is empty" });
            }

            // Validar que sea una imagen
            var allowedContentTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp", "image/bmp" };
            if (!allowedContentTypes.Contains(uploadedFile.ContentType?.ToLowerInvariant()))
            {
                return new BadRequestObjectResult(new { success = false, errorMessage = $"Invalid file type. Allowed types: {string.Join(", ", allowedContentTypes)}" });
            }

            _logger.LogInformation("📸 Processing photo for analysis: {FileName}, Size: {Size} bytes, ContentType: {ContentType}", 
                uploadedFile.FileName, uploadedFile.Length, uploadedFile.ContentType);

            // Configurar DataLake client
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Generar nombre único para el archivo
            var fileExtension = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension))
            {
                fileExtension = GetExtensionFromContentType(uploadedFile.ContentType);
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fileName = uploadedFile.FileName ?? $"memoria_{memoriaId}_{photoId}_{timestamp}{fileExtension}";
            
            // Usar contenedor TwinID y path por defecto para análisis
            var containerName = twinId.ToLowerInvariant();
            var filePath = "MiMemoria"; // Path por defecto para análisis
            var fullFilePath = filePath; ;
            string userDescription = null;
            if (req.Form.ContainsKey("description"))
            {
                userDescription = req.Form["description"].ToString()?.Trim();
            }
            _logger.LogInformation("📸 Uploading to DataLake for analysis: Container={Container}, Path={Path}", containerName, fullFilePath);

            // Subir archivo al Data Lake
            using var fileStream = uploadedFile.OpenReadStream();
            fileStream.Position = 0; // Asegurar que el stream está al inicio
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                containerName,
                filePath,
                fileName,
                fileStream,
                uploadedFile.ContentType ?? "image/jpeg"
            );

            if (!uploadSuccess)
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to upload file to storage for analysis" })
                {
                    StatusCode = 500
                };
            }

            fullFilePath = $"{filePath}/{fileName}";
            // Generar SAS URL para acceso temporal (24 horas)
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
            
            if (string.IsNullOrEmpty(sasUrl))
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to generate SAS URL for uploaded file" })
                {
                    StatusCode = 500
                };
            }

            _logger.LogInformation("📸 File uploaded successfully, generated SAS URL for analysis");

            // Crear instancia del agente de análisis de IA
            var memoriaAgent = new Agents.MiMemoriaAgent(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Agents.MiMemoriaAgent>(),
                _configuration);

            // Ejecutar análisis de IA usando el SAS URL
            var imageAI = await memoriaAgent.AnalyzePhotoAsync(sasUrl, memoria, userDescription);

            // Buscar si ya existe una foto con este photoId en la memoria
            Photo? photo = memoria.Photos?.FirstOrDefault(p => p.Id == photoId);
            
            if (photo == null)
            {
                // Si no existe, crear nueva foto
                photo = new Photo
                {
                    Id = photoId,
                    ContainerName = containerName,
                    FileName = fileName,
                    Path = filePath,
                    Url = sasUrl,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Title = "",
                    Description = "Foto analizada con IA",
                    ImageAI = imageAI
                };

                // Añadir foto a la memoria
                if (memoria.Photos == null)
                {
                    memoria.Photos = new List<Photo>();
                }
                memoria.Photos.Add(photo);
            }
            else
            {
                // Si existe, actualizar con el análisis de IA
                photo.ImageAI = imageAI;
                photo.UpdatedAt = DateTime.UtcNow;
                photo.Url = sasUrl; // Actualizar con nuevo SAS URL
                photo.FileName = fileName; // Actualizar nombre del archivo
            }

            // Guardar cambios en la base de datos
            var updateSuccess = await memoriasService.UpdateMemoriaAsync(memoria);
            if (!updateSuccess)
            {
                return new ObjectResult(new { success = false, errorMessage = "Failed to save AI analysis results" })
                {
                    StatusCode = 500
                };
            }

            _logger.LogInformation("✅ AI analysis completed successfully for photo: {PhotoId}", photoId);

            return new OkObjectResult(new
            {
                success = true,
                twinId = twinId,
                memoriaId = memoriaId,
                photoId = photoId,
                analysis = new
                {
                    descripcionGenerica = imageAI.DescripcionGenerica,
                    detailsHTML = imageAI.DetailsHTML,
                    descripcionVisualDetallada = imageAI.DescripcionVisualDetallada,
                    contextoEmocional = imageAI.ContextoEmocional,
                    elementosTemporales = imageAI.ElementosTemporales,
                    detallesMemorables = imageAI.DetallesMemorables
                },
                photo = new
                {
                    id = photo.Id,
                    fileName = photo.FileName,
                    filePath = fullFilePath,
                    containerName = photo.ContainerName,
                    sasUrl = photo.Url,
                    description = photo.Description,
                    updatedAt = photo.UpdatedAt
                },
                message = $"AI analysis completed for photo '{photo.FileName}'",
                analyzedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error analyzing photo: {PhotoId} for memory: {MemoriaId}", photoId, memoriaId);
            return new ObjectResult(new { success = false, errorMessage = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// OPTIONS handler para el endpoint de análisis de fotos
    /// </summary>
    [Function("AnalyzeMemoriaPhotoOptions")]
    public IActionResult HandleAnalyzeMemoriaPhotoOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/memorias/{memoriaId}/photos/{photoId}/analyze")] HttpRequest req,
        string twinId, string memoriaId, string photoId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for analyze photo endpoint");
        AddCorsHeaders(req);
        return new OkResult();
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Obtener extensión de archivo basada en el content type
    /// </summary>
    private string GetExtensionFromContentType(string? contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }

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