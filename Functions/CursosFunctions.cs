using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using TwinFx.Agents;
using TwinFx.Services;
using TwinFx.Models;

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions para gestión de cursos educativos
/// ========================================================================
/// 
/// Proporciona endpoints para:
/// - Crear nuevos cursos
/// - Obtener cursos por Twin ID
/// - Actualizar cursos existentes
/// - Eliminar cursos
/// - Cambiar estado de cursos
/// 
/// Author: TwinFx Project
/// Date: January 2025
/// </summary>
public class CursosFunctions
{
    private readonly ILogger<CursosFunctions> _logger;
    private readonly IConfiguration _configuration;

    public CursosFunctions(ILogger<CursosFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // ========================================
    // OPTIONS HANDLERS FOR CORS
    // ========================================

    [Function("CursosOptions")]
    public async Task<HttpResponseData> HandleCursosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/cursos")] HttpRequestData req, 
        string twinId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/cursos", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("CursosIdOptions")]
    public async Task<HttpResponseData> HandleCursosIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/cursos/{cursoId}")] HttpRequestData req, 
        string twinId, string cursoId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/cursos/{CursoId}", twinId, cursoId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("CapitulosOptions")]
    public async Task<HttpResponseData> HandleCapitulosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/cursos/{cursoId}/capitulos")] HttpRequestData req, 
        string twinId, string cursoId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/cursos/{CursoId}/capitulos", twinId, cursoId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // CRUD OPERATIONS
    // ========================================

    /// <summary>
    /// Crear nuevo curso
    /// POST /api/twins/{twinId}/cursos
    /// </summary>
    [Function("CreateCurso")]
    public async Task<HttpResponseData> CreateCurso(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cursos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📚 CreateCurso function triggered for Twin ID: {TwinId}", twinId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Leer el cuerpo de la solicitud
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("❌ Request body is required");
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("📚 Request body received: {Length} characters", requestBody.Length);

            // Parsear el JSON del request
            CrearCursoRequest? createRequest;
            try
            {
                createRequest = JsonSerializer.Deserialize<CrearCursoRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ Invalid JSON format in request body");
                return await CreateErrorResponse(req, $"Invalid JSON format: {ex.Message}", HttpStatusCode.BadRequest);
            }

            if (createRequest == null)
            {
                _logger.LogError("❌ Failed to parse request body");
                return await CreateErrorResponse(req, "Failed to parse request body", HttpStatusCode.BadRequest);
            }

            // Validar datos básicos del curso
            if (createRequest.Curso == null)
            {
                _logger.LogError("❌ Curso data is required");
                return await CreateErrorResponse(req, "Curso data is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(createRequest.Curso.NombreClase))
            {
                _logger.LogError("❌ Course name (nombreClase) is required");
                return await CreateErrorResponse(req, "Course name (nombreClase) is required", HttpStatusCode.BadRequest);
            }

            // Asignar Twin ID y generar ID del curso si no se proporcionó
            createRequest.TwinId = twinId;
            if (string.IsNullOrEmpty(createRequest.CursoId))
            {
                createRequest.CursoId = Guid.NewGuid().ToString();
            }

            // Establecer metadatos por defecto si no se proporcionan
            if (createRequest.Metadatos == null)
            {
                createRequest.Metadatos = new MetadatosCurso
                {
                    FechaSeleccion = DateTime.UtcNow,
                    EstadoCurso = "seleccionado",
                    OrigenBusqueda = "manual",
                    ConsultaOriginal = $"Curso: {createRequest.Curso.NombreClase}"
                };
            }

            _logger.LogInformation("📚 Creating course: {NombreClase} for Twin: {TwinId}", 
                createRequest.Curso.NombreClase, twinId);

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Usar CursosAgent para procesar el curso creado
            var cursosAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosAgent>();
            var cursosAgent = new CursosAgent(cursosAgentLogger, _configuration);
            var aiResponse = await cursosAgent.ProcessCreateCursoAsync(createRequest, twinId);
            // Crear servicio de Cosmos DB
            var cursosService = CreateCursosService();
            createRequest.Curso.htmlDetails = aiResponse.HtmlDetails;
            createRequest.Curso.textoDetails = aiResponse.TextDetails;

            // Crear el curso en la base de datos
            var cursoId = await cursosService.CreateCursoAsync(createRequest);

            if (string.IsNullOrEmpty(cursoId))
            {
                _logger.LogError("❌ Failed to create course in database");
                return await CreateErrorResponse(req, "Failed to create course in database", HttpStatusCode.InternalServerError);
            }

            // NUEVO - Indexar el análisis completo en CursosSearchIndex
            _logger.LogInformation("📚 Step: Indexing comprehensive course analysis in search index");
            try
            {
                // Crear instancia del CursosSearchIndex
                var cursosSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosSearchIndex>();
                var cursosSearchIndex = new CursosSearchIndex(cursosSearchLogger, _configuration);

                // Indexar el análisis completo en el índice de búsqueda
                var indexResult = await cursosSearchIndex.IndexCourseAnalysisAsync(
                    createRequest, 
                    twinId, 
                    processingTime);

                if (indexResult.Success)
                {
                    _logger.LogInformation("✅ Course analysis indexed successfully in search index: DocumentId={DocumentId}", indexResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to index course analysis in search index: {Error}", indexResult.Error);
                    // No fallamos toda la operación por esto, solo logueamos la advertencia
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing course analysis in search index");
                // No fallamos toda la operación por esto
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var successResult = new
            {
                success = true,
                cursoId = cursoId,
                twinId = twinId,
                curso = createRequest,
                aiResponse = aiResponse,
                processingTimeMs = processingTime,
                createdAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(successResult, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Course created successfully: {CursoId} in {ProcessingTime}ms", 
                cursoId, processingTime);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating course for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Obtener todos los cursos de un Twin
    /// GET /api/twins/{twinId}/cursos
    /// </summary>
    [Function("GetCursosByTwin")]
    public async Task<HttpResponseData> GetCursosByTwin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cursos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📚 Getting courses for Twin ID: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            var cursosService = CreateCursosService();
            var cursos = await cursosService.GetCursosByTwinIdAsync(twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var result = new
            {
                success = true,
                twinId = twinId,
                cursos = cursos,
                count = cursos.Count,
                retrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Retrieved {Count} courses for Twin: {TwinId}", cursos.Count, twinId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting courses for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Obtener curso específico por ID
    /// GET /api/twins/{twinId}/cursos/{cursoId}
    /// </summary>
    [Function("GetCursoById")]
    public async Task<HttpResponseData> GetCursoById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cursos/{cursoId}")] HttpRequestData req,
        string twinId, string cursoId)
    {
        _logger.LogInformation("📚 Getting course {CursoId} for Twin: {TwinId}", cursoId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId))
            {
                return await CreateErrorResponse(req, "Twin ID and Course ID parameters are required", HttpStatusCode.BadRequest);
            }

            var cursosService = CreateCursosService();
            var curso = await cursosService.GetCursoByIdAsync(cursoId, twinId);

            if (curso == null)
            {
                return await CreateErrorResponse(req, "Course not found", HttpStatusCode.NotFound);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var result = new
            {
                success = true,
                twinId = twinId,
                cursoId = cursoId,
                curso = curso,
                retrievedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Course retrieved successfully: {CursoId}", cursoId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting course {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Actualizar curso existente
    /// PUT /api/twins/{twinId}/cursos/{cursoId}
    /// </summary>
    [Function("UpdateCurso")]
    public async Task<HttpResponseData> UpdateCurso(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/cursos/{cursoId}")] HttpRequestData req,
        string twinId, string cursoId)
    {
        _logger.LogInformation("📚 Updating course {CursoId} for Twin: {TwinId}", cursoId, twinId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId))
            {
                return await CreateErrorResponse(req, "Twin ID and Course ID parameters are required", HttpStatusCode.BadRequest);
            }

            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            var updateRequest = JsonSerializer.Deserialize<CrearCursoRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                return await CreateErrorResponse(req, "Failed to parse request body", HttpStatusCode.BadRequest);
            }

            // Asegurar que los IDs sean consistentes
            updateRequest.TwinId = twinId;
            updateRequest.CursoId = cursoId;

            var cursosService = CreateCursosService();
            var success = await cursosService.UpdateCursoAsync(cursoId, updateRequest, twinId);

            if (!success)
            {
                return await CreateErrorResponse(req, "Failed to update course or course not found", HttpStatusCode.NotFound);
            }

            // NUEVO - Indexar el análisis actualizado en CursosSearchIndex
            _logger.LogInformation("📚 Step: Indexing updated course analysis in search index");
            try
            {
                // Crear instancia del CursosSearchIndex
                var cursosSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosSearchIndex>();
                var cursosSearchIndex = new CursosSearchIndex(cursosSearchLogger, _configuration);

                // Indexar el análisis actualizado en el índice de búsqueda
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var indexResult = await cursosSearchIndex.IndexCourseAnalysisAsync(
                    updateRequest, 
                    twinId, 
                    processingTime);

                if (indexResult.Success)
                {
                    _logger.LogInformation("✅ Updated course analysis indexed successfully in search index: DocumentId={DocumentId}", indexResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to index updated course analysis in search index: {Error}", indexResult.Error);
                    // No fallamos toda la operación por esto, solo logueamos la advertencia
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing updated course analysis in search index");
                // No fallamos toda la operación por esto
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var result = new
            {
                success = true,
                twinId = twinId,
                cursoId = cursoId,
                message = "Course updated successfully",
                updatedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Course updated successfully: {CursoId}", cursoId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating course {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Eliminar curso
    /// DELETE /api/twins/{twinId}/cursos/{cursoId}
    /// </summary>
    [Function("DeleteCurso")]
    public async Task<HttpResponseData> DeleteCurso(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/cursos/{cursoId}")] HttpRequestData req,
        string twinId, string cursoId)
    {
        _logger.LogInformation("🗑️ Deleting course {CursoId} for Twin: {TwinId}", cursoId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId))
            {
                return await CreateErrorResponse(req, "Twin ID and Course ID parameters are required", HttpStatusCode.BadRequest);
            }

            var cursosService = CreateCursosService();
            var success = await cursosService.DeleteCursoAsync(cursoId, twinId);

            if (!success)
            {
                return await CreateErrorResponse(req, "Course not found", HttpStatusCode.NotFound);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var result = new
            {
                success = true,
                twinId = twinId,
                cursoId = cursoId,
                message = "Course deleted successfully",
                deletedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Course deleted successfully: {CursoId}", cursoId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting course {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Actualizar estado de un curso
    /// PATCH /api/twins/{twinId}/cursos/{cursoId}/status
    /// </summary>
    [Function("UpdateCursoStatus")]
    public async Task<HttpResponseData> UpdateCursoStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "twins/{twinId}/cursos/{cursoId}/status")] HttpRequestData req,
        string twinId, string cursoId)
    {
        _logger.LogInformation("📚 Updating status for course {CursoId} for Twin: {TwinId}", cursoId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId))
            {
                return await CreateErrorResponse(req, "Twin ID and Course ID parameters are required", HttpStatusCode.BadRequest);
            }

            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            var statusRequest = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (statusRequest == null || !statusRequest.TryGetValue("estadoCurso", out var newStatus) || string.IsNullOrEmpty(newStatus))
            {
                return await CreateErrorResponse(req, "estadoCurso field is required", HttpStatusCode.BadRequest);
            }

            // Validar estados permitidos
            var allowedStatuses = new[] { "seleccionado", "en_progreso", "completado", "pausado", "cancelado" };
            if (!allowedStatuses.Contains(newStatus))
            {
                return await CreateErrorResponse(req, $"Invalid status. Allowed values: {string.Join(", ", allowedStatuses)}", HttpStatusCode.BadRequest);
            }

            var cursosService = CreateCursosService();
            var success = await cursosService.UpdateCourseStatusAsync(cursoId, twinId, newStatus);

            if (!success)
            {
                return await CreateErrorResponse(req, "Failed to update course status or course not found", HttpStatusCode.NotFound);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var result = new
            {
                success = true,
                twinId = twinId,
                cursoId = cursoId,
                newStatus = newStatus,
                message = "Course status updated successfully",
                updatedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Course status updated successfully: {CursoId} -> {Status}", cursoId, newStatus);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating course status {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Crear nuevo capítulo para un curso
    /// POST /api/twins/{twinId}/cursos/{cursoId}/capitulos
    /// </summary>
    [Function("CreateCapitulo")]
    public async Task<HttpResponseData> CreateCapitulo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cursos/{cursoId}/capitulos")] HttpRequestData req,
        string twinId, string cursoId)
    {
        _logger.LogInformation("📖 CreateCapitulo function triggered for Course ID: {CursoId}, Twin ID: {TwinId}", cursoId, twinId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId))
            {
                _logger.LogError("❌ Twin ID and Course ID parameters are required");
                return await CreateErrorResponse(req, "Twin ID and Course ID parameters are required", HttpStatusCode.BadRequest);
            }

            // Leer el cuerpo de la solicitud
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("❌ Request body is required");
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("📖 Request body received: {Length} characters", requestBody.Length);

            // Parsear el JSON del request
            CapituloRequest? createRequest;
            try
            {
                createRequest = JsonSerializer.Deserialize<CapituloRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ Invalid JSON format in request body");
                return await CreateErrorResponse(req, $"Invalid JSON format: {ex.Message}", HttpStatusCode.BadRequest);
            }

            if (createRequest == null)
            {
                _logger.LogError("❌ Failed to parse request body");
                return await CreateErrorResponse(req, "Failed to parse request body", HttpStatusCode.BadRequest);
            }

            // Validar datos básicos del capítulo
            if (string.IsNullOrEmpty(createRequest.Titulo))
            {
                _logger.LogError("❌ Chapter title is required");
                return await CreateErrorResponse(req, "Chapter title is required", HttpStatusCode.BadRequest);
            }

            // Asignar IDs de relación
            createRequest.CursoId = cursoId;
            createRequest.TwinId = twinId;

            _logger.LogInformation("📖 Creating chapter: {Titulo} for Course: {CursoId}", createRequest.Titulo, cursoId);

            var cursosService = CreateCursosService();

            // 1) Leer el curso por TwinID y CursoId
            var curso = await cursosService.GetCursoByIdAsync(cursoId, twinId);
            if (curso == null)
            {
                _logger.LogError("❌ Course not found: {CursoId} for Twin: {TwinId}", cursoId, twinId);
                return await CreateErrorResponse(req, "Course not found", HttpStatusCode.NotFound);
            }

            // 2) Agregar el capítulo a la lista de capítulos
            if (curso.CursoData.Curso.Capitulos == null)
            {
                curso.CursoData.Curso.Capitulos = new List<CapituloRequest>();
            }

            // Convertir CreateCapituloRequest a CapituloRequest
            var capitulo = new CapituloRequest
            {
                Titulo = createRequest.Titulo,
                Descripcion = createRequest.Descripcion,
                NumeroCapitulo = createRequest.NumeroCapitulo,
                Transcript = createRequest.Transcript,
                Notas = createRequest.Notas,
                Comentarios = createRequest.Comentarios,
                DuracionMinutos = createRequest.DuracionMinutos,
                Tags = createRequest.Tags ?? new List<string>(),
                Puntuacion = createRequest.Puntuacion,
                CursoId = cursoId,
                TwinId = twinId,
                Completado = createRequest.Completado
            };

            // Agregar el capítulo a la lista
            curso.CursoData.Curso.Capitulos.Add(capitulo);

            // Actualizar el curso en la base de datos
            var success = await cursosService.UpdateCursoAsync(cursoId, curso.CursoData, twinId);

            if (!success)
            {
                _logger.LogError("❌ Failed to update course with new chapter");
                return await CreateErrorResponse(req, "Failed to update course with new chapter", HttpStatusCode.InternalServerError);
            }

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var response = req.CreateResponse(HttpStatusCode.Created);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var successResult = new
            {
                success = true,
                cursoId = cursoId,
                twinId = twinId,
                capitulo = capitulo,
                processingTimeMs = processingTime,
                createdAt = DateTime.UtcNow,
                message = "Chapter created successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(successResult, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Chapter created successfully: {Titulo} for Course: {CursoId} in {ProcessingTime}ms", 
                createRequest.Titulo, cursoId, processingTime);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating chapter for Course: {CursoId}, Twin: {TwinId}", cursoId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    private CursosCosmosDbService CreateCursosService()
    {
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
        {
            Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
            Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
            DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });

        var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosCosmosDbService>();
        return new CursosCosmosDbService(serviceLogger, cosmosOptions);
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);
        response.Headers.Add("Content-Type", "application/json");
        
        var errorResult = new
        {
            success = false,
            errorMessage = errorMessage,
            timestamp = DateTime.UtcNow
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(errorResult));
        return response;
    }

    private void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With");
        response.Headers.Add("Access-Control-Max-Age", "86400");
        response.Headers.Add("Access-Control-Allow-Credentials", "false");
    }
}

/// <summary>
/// Request para crear un nuevo cap 