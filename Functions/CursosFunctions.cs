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
using HttpMultipartParser; 

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
/// - Responder preguntas sobre cursos con AI
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

    [Function("AskQuestionOptions")]
    public async Task<HttpResponseData> HandleAskQuestionOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/cursos/{cursoId}/ask-question")] HttpRequestData req, 
        string twinId, string cursoId)
    {
        _logger.LogInformation("🌐 OPTIONS preflight request for twins/{TwinId}/cursos/{CursoId}/ask-question", twinId, cursoId);
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
            TwinFx.Agents.CrearCursoRequest? createRequest;
            try
            {
                createRequest = JsonSerializer.Deserialize<TwinFx.Agents.CrearCursoRequest>(requestBody, new JsonSerializerOptions
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
                createRequest.Metadatos = new TwinFx.Agents.MetadatosCurso
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
            createRequest.Curso.htmlDetails = aiResponse.htmlDetails;
            createRequest.Curso.textoDetails = aiResponse.textoDetails;

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

    /// </summary>
    [Function("GetCursosAIByTwin")]
    public async Task<HttpResponseData> GetCursosAIByTwin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cursosAI")] HttpRequestData req,
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
            var cursos = await cursosService.GetCursosAIByTwinIdAsync(twinId);

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

            var updateRequest = JsonSerializer.Deserialize<TwinFx.Agents.CrearCursoRequest>(requestBody, new JsonSerializerOptions
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
            TwinFx.Agents.CapituloRequest? createRequest;
            try
            {
                createRequest = JsonSerializer.Deserialize<TwinFx.Agents.CapituloRequest>(requestBody, new JsonSerializerOptions
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
                curso.CursoData.Curso.Capitulos = new List<TwinFx.Agents.CapituloRequest>();
            }

            // Convertir CreateCapituloRequest a CapituloRequest
            var capitulo = new TwinFx.Agents.CapituloRequest
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

    /// <summary>
    /// Responder pregunta sobre un curso usando AI
    /// POST /api/twins/{twinId}/cursos/{cursoId}/ask-question
    /// </summary>
    [Function("AnswerCourseQuestion")]
    public async Task<HttpResponseData> AnswerCourseQuestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cursos/{cursoId}/ask-question")] HttpRequestData req,
        string twinId, string cursoId)
    {
        _logger.LogInformation("🤖❓ AnswerCourseQuestion function triggered for Twin ID: {TwinId}, Course ID: {CursoId}", twinId, cursoId);
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

            _logger.LogInformation("🤖 Question request body received: {Length} characters", requestBody.Length);

            // Parsear el JSON del request
            CourseQuestionRequest? questionRequest;
            try
            {
                questionRequest = JsonSerializer.Deserialize<CourseQuestionRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ Invalid JSON format in request body");
                return await CreateErrorResponse(req, $"Invalid JSON format: {ex.Message}", HttpStatusCode.BadRequest);
            }

            if (questionRequest == null || string.IsNullOrEmpty(questionRequest.Question))
            {
                _logger.LogError("❌ Question field is required");
                return await CreateErrorResponse(req, "Question field is required in request body", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("🤖 Processing course question: {Question}", questionRequest.Question.Length > 100 ? 
                questionRequest.Question.Substring(0, 100) + "..." : questionRequest.Question);

            // Crear el objeto TwinCursosAIInfo para el agente AI
            var twinCursosAIInfo = new TwinFx.Agents.TwinCursosAIInfo
            {
                TwinId = twinId,
                Titulo = questionRequest.Titulo,
                CursoId = cursoId,
                CapituloId = questionRequest.CapituloId, // Opcional
                Question = questionRequest.Question,
                Context  = questionRequest.Context, 
                Answer = ""
            };

            // Usar CursosAgentAI para responder la pregunta

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var SearchLogger = loggerFactory.CreateLogger<CursosSearchIndex>();
            CursosAgentAI cursoAI = new CursosAgentAI(SearchLogger, _configuration);

            var aiResponse = await cursoAI.AnswerCourseQuestionAsync(twinCursosAIInfo);

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var successResult = new
            {
                success = true,
                twinId = twinId,
                cursoId = cursoId,
                capituloId = questionRequest.CapituloId,
                question = questionRequest.Question,
                answer = aiResponse.Answer,
                context = aiResponse.Context,
                processingTimeMs = processingTime,
                answeredAt = DateTime.UtcNow,
                message = "Course question answered successfully by AI Twin expert"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(successResult, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            }));

            _logger.LogInformation("✅ Course question answered successfully in {ProcessingTime}ms", processingTime);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error answering course question for Twin: {TwinId}, Course: {CursoId}", twinId, cursoId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Procesar documento de curso con análisis de capítulos
    /// POST /api/twins/{twinId}/cursos/{cursoId}/upload-document/{filePath}
    /// </summary>
    [Function("UploadCourseDocument")]
    public async Task<HttpResponseData> UploadCourseDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cursos/{cursoId}/upload-document/{*filePath}")] HttpRequestData req,
        string twinId, string cursoId, string filePath)
    {
        _logger.LogInformation("📚📄 UploadCourseDocument function triggered for Twin ID: {TwinId}, Curso ID: {CursoId}", twinId, cursoId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId))
            {
                _logger.LogError("❌ Twin ID and Curso ID parameters are required");
                return await CreateErrorResponse(req, "Twin ID and Curso ID parameters are required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogError("❌ File path parameter is required");
                return await CreateErrorResponse(req, "File path parameter is required", HttpStatusCode.BadRequest);
            }

            var contentType = req.Headers.GetValues("Content-Type")?.FirstOrDefault() ?? "";
            if (!contentType.StartsWith("multipart/form-data"))
            {
                _logger.LogError("❌ Content-Type must be multipart/form-data");
                return await CreateErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
            }

            // Extraer boundary de multipart
            var boundary = ExtractBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                _logger.LogError("❌ Invalid multipart boundary");
                return await CreateErrorResponse(req, "Invalid multipart boundary in Content-Type header", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("📚📄 Processing course document upload for Twin ID: {TwinId}, Curso ID: {CursoId}", twinId, cursoId);
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();
            _logger.LogInformation("📄 Received multipart data: {Size} bytes", bodyBytes.Length);

            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            
            // Buscar el archivo del documento
            var documentPart = parts.FirstOrDefault(p => p.Name == "document" || p.Name == "file" || p.Name == "pdf");
            if (documentPart == null || documentPart.Data == null || documentPart.Data.Length == 0)
            {
                return await CreateErrorResponse(req, "No document file data found in request. Expected field name: 'document', 'file', or 'pdf'", HttpStatusCode.BadRequest);
            }

            // Buscar los datos de configuración del curso
            var courseConfigPart = parts.FirstOrDefault(p => p.Name == "courseConfig" || p.Name == "config");
            DocumentoClassRequest? documentoClase = null;
            
            if (courseConfigPart != null && courseConfigPart.Data != null && courseConfigPart.Data.Length > 0)
            {
                try
                {
                    var configJson = System.Text.Encoding.UTF8.GetString(courseConfigPart.Data);
                    documentoClase = JsonSerializer.Deserialize<DocumentoClassRequest>(configJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    _logger.LogInformation("📋 Course configuration parsed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to parse course configuration, using defaults");
                }
            }

            // Usar configuración por defecto si no se proporcionó
            if (documentoClase == null)
            {
                documentoClase = new DocumentoClassRequest
                {
                    Nombre = "Documento de Curso",
                    Descripcion = "Análisis automático de documento educativo",
                    TieneIndice = true, // Asumir que tiene índice por defecto
                    PaginaInicioIndice = null // Detección automática
                };
                _logger.LogInformation("📋 Using default course configuration");
            }

            string fileName = documentPart.FileName ?? "course_document.pdf";
            var documentBytes = documentPart.Data;
            var completePath = filePath.Trim();

            _logger.LogInformation("📂 Using path from URL: {CompletePath}", completePath);

            // Extraer directorio del path completo
            var directory = Path.GetDirectoryName(completePath)?.Replace("\\", "/") ?? "";
            if (string.IsNullOrEmpty(directory))
            {
                directory = filePath;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                return await CreateErrorResponse(req, "Invalid path: filename cannot be extracted from the provided URL path", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("📂 Final upload details: Directory='{Directory}', FileName='{FileName}', CompletePath='{CompletePath}'", 
                directory, fileName, completePath);
            _logger.LogInformation("📏 Document size: {Size} bytes", documentBytes.Length);

            // Validar que sea un archivo PDF
            if (!IsValidPdfFile(fileName, documentBytes))
            {
                return await CreateErrorResponse(req, "Invalid document format. Only PDF files are supported for course documents", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("📤 STEP 1: Uploading document to DataLake...");
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            var mimeType = "application/pdf";

            directory = filePath;
            using var documentStream = new MemoryStream(documentBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                directory,
                fileName,
                documentStream,
                mimeType);

            if (!uploadSuccess)
            {
                _logger.LogError("❌ Failed to upload document to DataLake");
                return await CreateErrorResponse(req, "Failed to upload document to storage", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation("✅ Document uploaded successfully to DataLake");

            // PASO 2: Procesar el documento con AI usando CursosAgent
            _logger.LogInformation("🤖 STEP 2: Processing document with AI Course analysis...");

            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cursosAgentLogger = loggerFactory.CreateLogger<CursosAgent>();
                var cursosAgent = new CursosAgent(cursosAgentLogger, _configuration);

                // Llamar al método BuildClassWithDocumentAI
                var aiNewAiCurso = await cursosAgent.BuildClassWithDocumentAI(
                    twinId,
                    documentoClase, 
                    twinId.ToLowerInvariant(), 
                    directory, 
                    fileName, 
                    cursoId);

                 var CosmosCurso = CreateCursosService();
                var ResponseCosmos = CosmosCurso.CreateCursoAIAsync(aiNewAiCurso);
                _logger.LogInformation("✅ AI course analysis completed successfully");

                // Generar SAS URL para el documento
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                var responseData = new CourseDocumentUploadResult
                {
                    Success = true,
                    Message = "Course document uploaded and analyzed successfully",
                    TwinId = twinId,
                    CursoId = cursoId,
                    FilePath = completePath,
                    FileName = fileName,
                    Directory = directory,
                    ContainerName = twinId.ToLowerInvariant(),
                    DocumentUrl = sasUrl,
                    FileSize = documentBytes.Length,
                    MimeType = mimeType,
                    UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                    DocumentConfig = documentoClase,
                    AiAnalysisResult = "OK"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation("✅ Course document upload and analysis completed successfully in {Time:F2} seconds", 
                    responseData.ProcessingTimeSeconds);
                return response;
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "⚠️ Document uploaded successfully but AI analysis failed");

                // Aún así devolver éxito de upload pero con error de AI
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                var responseData = new CourseDocumentUploadResult
                {
                    Success = true,
                    Message = "Document uploaded successfully but AI analysis failed",
                    TwinId = twinId,
                    CursoId = cursoId,
                    FilePath = completePath,
                    FileName = fileName,
                    Directory = directory,
                    ContainerName = twinId.ToLowerInvariant(),
                    DocumentUrl = sasUrl,
                    FileSize = documentBytes.Length,
                    MimeType = mimeType,
                    UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                    DocumentConfig = documentoClase,
                    AiAnalysisResult = $"{{\"success\": false, \"errorMessage\": \"{aiEx.Message}\"}}"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error uploading course document for Twin: {TwinId}, Curso: {CursoId}", twinId, cursoId);
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

    /// <summary>
    /// Extrae el boundary del Content-Type header
    /// </summary>
    private string ExtractBoundary(string contentType)
    {
        var boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        if (boundaryIndex == -1) return string.Empty;

        var boundary = contentType.Substring(boundaryIndex + 9);
        return boundary.Trim('"', ' ');
    }

    /// <summary>
    /// Parsea datos multipart/form-data usando HttpMultipartParser
    /// </summary>
    private async Task<List<MultipartData>> ParseMultipartDataAsync(byte[] bodyBytes, string boundary)
    {
        var parts = new List<MultipartData>();
        
        try
        {
            using var stream = new MemoryStream(bodyBytes);
            var parser = MultipartFormDataParser.Parse(stream, boundary);
            
            // Procesar archivos
            foreach (var file in parser.Files)
            {
                using var fileStream = new MemoryStream();
                await file.Data.CopyToAsync(fileStream);
                
                var part = new MultipartData
                {
                    Name = file.Name,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Data = fileStream.ToArray()
                };
                
                parts.Add(part);
                _logger.LogInformation("✅ File part added: {Name}, FileName: {FileName}, Size: {Size} bytes", 
                    part.Name, part.FileName, part.Data.Length);
            }
            
            // Procesar parámetros de texto
            foreach (var parameter in parser.Parameters)
            {
                var part = new MultipartData
                {
                    Name = parameter.Name,
                    ContentType = "text/plain",
                    Data = System.Text.Encoding.UTF8.GetBytes(parameter.Data)
                };
                
                parts.Add(part);
                _logger.LogInformation("✅ Parameter part added: {Name}, Value: {Value}", 
                    part.Name, parameter.Data.Length > 100 ? parameter.Data.Substring(0, 100) + "..." : parameter.Data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error parsing multipart data with HttpMultipartParser");
        }

        _logger.LogInformation("📋 Total parts parsed: {Count}", parts.Count);
        return parts;
    }
    
    /// <summary>
    /// Valida que el archivo sea un PDF válido
    /// </summary>
    private bool IsValidPdfFile(string fileName, byte[] fileData)
    {
        if (string.IsNullOrEmpty(fileName) || fileData == null || fileData.Length == 0)
            return false;

        // Verificar extensión
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return false;

        // Verificar header de PDF
        if (fileData.Length < 4)
            return false;

        var pdfHeader = System.Text.Encoding.ASCII.GetString(fileData.Take(4).ToArray());
        return pdfHeader == "%PDF";
    }
}

/// <summary>
/// Resultado del upload y análisis de documento de curso
/// </summary>
public class CourseDocumentUploadResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public string CursoId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string DocumentUrl { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string UploadDate { get; set; } = string.Empty;
    public double ProcessingTimeSeconds { get; set; }
    public DocumentoClassRequest? DocumentConfig { get; set; }
    public string AiAnalysisResult { get; set; } = string.Empty;
}

/// <summary>
/// Datos de una parte multipart
/// </summary>
public class MultipartData
{
    public string Name { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Request para hacer una pregunta sobre un curso
/// </summary>
public class CourseQuestionRequest
{
    /// <summary>
    /// La pregunta sobre el curso
    /// </summary>
    public string Question { get; set; } = string.Empty;


    public string Titulo { get; set; } = string.Empty;

    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// ID del capítulo específico (opcional)
    /// </summary>
    public int CapituloId { get; set; }
}