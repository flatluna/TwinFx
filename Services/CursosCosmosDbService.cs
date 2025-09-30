using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Agents;

namespace TwinFx.Services;

/// <summary>
/// Service class for managing Courses in Cosmos DB
/// Container: TwinCursos, PartitionKey: TwinID
/// ========================================================================
/// 
/// Proporciona operaciones CRUD completas para cursos con:
/// - Gestión de cursos con CrearCursoRequest class
/// - Sin mapeo - usa JsonSerializer directamente
/// - ID único y PartitionKey con TwinID
/// - Timestamps automáticos
/// 
/// Author: TwinFx Project
/// Date: January 2025
/// </summary>
public class CursosCosmosDbService
{
    private readonly ILogger<CursosCosmosDbService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly Container _cursosContainer;

    public CursosCosmosDbService(ILogger<CursosCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions)
    {
        _logger = logger;
        var cosmosSettings = cosmosOptions.Value;

        _logger.LogInformation("📚 Initializing Cursos Cosmos DB Service");
        _logger.LogInformation($"   • Endpoint: {cosmosSettings.Endpoint}");
        _logger.LogInformation($"   • Database: {cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   • Container: TwinCursos");

        if (string.IsNullOrEmpty(cosmosSettings.Key))
        {
            _logger.LogError("❌ COSMOS_KEY is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration");
        }

        if (string.IsNullOrEmpty(cosmosSettings.Endpoint))
        {
            _logger.LogError("❌ COSMOS_ENDPOINT is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration");
        }

        try
        {
            _client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
            _database = _client.GetDatabase(cosmosSettings.DatabaseName);
            _cursosContainer = _database.GetContainer("TwinCursos");
            
            _logger.LogInformation("✅ Cursos Cosmos DB Service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Cursos Cosmos DB client");
            throw;
        }
    }

    /// <summary>
    /// Crear un nuevo curso
    /// </summary>
    public async Task<string?> CreateCursoAsync(CrearCursoRequest cursoRequest)
    {
        try
        {
            _logger.LogInformation("📚 Creating course for Twin ID: {TwinId}", cursoRequest.TwinId);
            _logger.LogInformation("📚 Course: {NombreClase}", cursoRequest.Curso?.NombreClase);

            // Verificar que tenemos los datos requeridos
            if (string.IsNullOrEmpty(cursoRequest.TwinId))
            {
                _logger.LogError("❌ TwinId is required");
                return null;
            }

            if (string.IsNullOrEmpty(cursoRequest.CursoId))
            {
                cursoRequest.CursoId = Guid.NewGuid().ToString();
                _logger.LogInformation("📚 Generated new CursoId: {CursoId}", cursoRequest.CursoId);
            }

            // Crear el documento para Cosmos DB
            var cursoDocument = new CursoDocument
            {
                id = cursoRequest.CursoId,
                TwinID = cursoRequest.TwinId,
                CursoData = cursoRequest,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _cursosContainer.CreateItemAsync(cursoDocument, new PartitionKey(cursoRequest.TwinId));

            _logger.LogInformation("✅ Course created successfully with ID: {CursoId} for Twin: {TwinId}", 
                cursoDocument.id, cursoRequest.TwinId);

            return cursoDocument.id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create course for Twin: {TwinId}", cursoRequest.TwinId);
            return null;
        }
    }

    public async Task<string?> CreateCursoAIAsync(CursoSeleccionado cursoRequest)
    {
        try
        {
           var _cursosContainer = _database.GetContainer("TwinCursosAI");

            // Verificar que tenemos los datos requeridos
            if (string.IsNullOrEmpty(cursoRequest.TwinID))
            {
                _logger.LogError("❌ TwinId is required");
                return null;
            }

            if (string.IsNullOrEmpty(cursoRequest.id))
            {
               cursoRequest.id = Guid.NewGuid().ToString();
                _logger.LogInformation("📚 Generated new CursoId: {CursoId}", cursoRequest.id);
            }

            
            await _cursosContainer.CreateItemAsync(cursoRequest, new PartitionKey(cursoRequest.TwinID));
             
            return cursoRequest.id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create course for Twin: {TwinId}", cursoRequest.TwinID);
            return null;
        }
    }


    /// <summary>
    /// Obtener todos los cursos de un twin
    /// </summary>
    public async Task<List<CursoDocument>> GetCursosByTwinIdAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("📚 Getting all courses for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.CreatedAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _cursosContainer.GetItemQueryIterator<CursoDocument>(query);
            var cursos = new List<CursoDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                cursos.AddRange(response);
            }

            _logger.LogInformation("✅ Retrieved {Count} courses for Twin ID: {TwinId}", cursos.Count, twinId);
            return cursos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting courses for Twin: {TwinId}", twinId);
            return new List<CursoDocument>();
        }
    }


    /// <summary>
    /// Obtener todos los cursos de un twin
    /// </summary>
    public async Task<List<CursoSeleccionado>> GetCursosAIByTwinIdAsync(string twinId)
    {
        try
        {
            var _cursosContainer = _database.GetContainer("TwinCursosAI");
            _logger.LogInformation("📚 Getting all courses for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.CreatedAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _cursosContainer.GetItemQueryIterator<CursoSeleccionado>(query);
            var cursos = new List<CursoSeleccionado>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                cursos.AddRange(response);
            }

            _logger.LogInformation("✅ Retrieved {Count} courses for Twin ID: {TwinId}", cursos.Count, twinId);
            return cursos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting courses for Twin: {TwinId}", twinId);
            return new List<CursoSeleccionado>();
        }
    }
    public async Task<List<CursoSeleccionado>> GetCursosAIByTwinIdAndIDAsync(string twinId, string CursoID)
    {
        try
        {
            var _cursosContainer = _database.GetContainer("TwinCursosAI");
            _logger.LogInformation("📚 Getting specific course for Twin ID: {TwinId} and Course ID: {CursoID}", twinId, CursoID);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.id = @cursoId")
                .WithParameter("@twinId", twinId)
                .WithParameter("@cursoId", CursoID);

            var iterator = _cursosContainer.GetItemQueryIterator<CursoSeleccionado>(query);
            var cursos = new List<CursoSeleccionado>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                cursos.AddRange(response);
            }

            _logger.LogInformation("✅ Retrieved {Count} courses for Twin ID: {TwinId} and Course ID: {CursoID}", cursos.Count, twinId, CursoID);
            return cursos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting course for Twin: {TwinId} and Course ID: {CursoID}", twinId, CursoID);
            return new List<CursoSeleccionado>();
        }
    }

    /// <summary>
    /// Obtener curso específico por ID
    /// </summary>
    public async Task<CursoDocument?> GetCursoByIdAsync(string cursoId, string twinId)
    {
        try
        {
            _logger.LogInformation("📚 Getting course by ID: {CursoId} for Twin: {TwinId}", cursoId, twinId);

            var response = await _cursosContainer.ReadItemAsync<CursoDocument>(cursoId, new PartitionKey(twinId));
            
            _logger.LogInformation("✅ Course retrieved successfully: {CursoId}", cursoId);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("⚠️ Course not found: {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting course by ID: {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Actualizar curso existente
    /// </summary>
    public async Task<bool> UpdateCursoAsync(string cursoId, CrearCursoRequest cursoRequest, string twinId)
    {
        try
        {
            _logger.LogInformation("📚 Updating course: {CursoId} for Twin: {TwinId}", cursoId, twinId);

            // Obtener el documento existente
            var existingDoc = await GetCursoByIdAsync(cursoId, twinId);
            if (existingDoc == null)
            {
                _logger.LogWarning("⚠️ Course not found for update: {CursoId}", cursoId);
                return false;
            }

            try
            {
                // ✅ NUEVO: Generar análisis AI para la actualización
                _logger.LogInformation("🤖 Generating AI analysis for course update: {CursoId}", cursoId);
                
                // Crear instancia del CursosAgent
                var cursosAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosAgent>();
                var configuration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();
                
                var cursosAgent = new CursosAgent(cursosAgentLogger, configuration);
                
                // Generar análisis inteligente del curso actualizado
                var aiAnalysisJson = await cursosAgent.ProcessUpdateCursoAsync(cursoRequest, twinId);
                
                // Almacenar el análisis AI en htmlDetails
                if (cursoRequest.Curso != null)
                {
                    try
                    {
                        cursoRequest.Curso.htmlDetails =aiAnalysisJson.htmlDetails;
                        cursoRequest.Curso.textoDetails = aiAnalysisJson.textoDetails;
                        // Limpiar antes de asignar
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogWarning(jsonEx, "⚠️ Failed to parse AI analysis JSON, using raw response");
                        // Si falla el parsing, usar la respuesta completa
                        cursoRequest.Curso.htmlDetails = ""; // Limpiar antes de asignar
                    }
                    
                    _logger.LogInformation("✅ AI analysis stored in htmlDetails for course: {CursoId}", cursoId);
                }
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "⚠️ Failed to generate AI analysis for course update: {CursoId}. Continuing with update...", cursoId);
                
                // Si falla el análisis AI, usar un JSON de fallback simple
                if (cursoRequest.Curso != null)
                {
                    var fallbackHtml = $@"<div style=""background: linear-gradient(135deg, #9C27B0 0%, #673AB7 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;"">
                        <h3 style=""color: #fff; margin: 0 0 15px 0;"">🔄 Curso Actualizado</h3>
                        <p><strong>Curso:</strong> {cursoRequest.Curso?.NombreClase ?? "No especificado"}</p>
                        <p><strong>Estado:</strong> Tu curso ha sido actualizado exitosamente.</p>
                        <p><strong>Nota:</strong> Análisis AI no disponible en este momento.</p>
                        </div>";
                    
                    var fallbackJson = JsonSerializer.Serialize(new
                    {
                        htmlDetails = fallbackHtml,
                        textDetails = "Curso actualizado exitosamente. Análisis AI no disponible."
                    });
                    
                    try
                    {
                        using (var document = JsonDocument.Parse(fallbackJson))
                        {
                            if (document.RootElement.TryGetProperty("htmlDetails", out var htmlDetailsElement))
                            {
                                cursoRequest.Curso.htmlDetails = htmlDetailsElement.GetString() ?? fallbackHtml;
                            }
                            else
                            {
                                cursoRequest.Curso.htmlDetails = fallbackHtml;
                            }
                        }
                    }
                    catch
                    {
                        cursoRequest.Curso.htmlDetails = fallbackHtml;
                    }
                }
            }

            // Actualizar los datos manteniendo el createdAt original
            existingDoc.CursoData = cursoRequest;
            existingDoc.UpdatedAt = DateTime.UtcNow;

            await _cursosContainer.UpsertItemAsync(existingDoc, new PartitionKey(twinId));

            _logger.LogInformation("✅ Course updated successfully with AI analysis: {CursoId}", cursoId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update course: {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Eliminar curso
    /// </summary>
    public async Task<bool> DeleteCursoAsync(string cursoId, string twinId)
    {
        try
        {
            _logger.LogInformation("🗑️ Deleting course: {CursoId} for Twin: {TwinId}", cursoId, twinId);

            await _cursosContainer.DeleteItemAsync<CursoDocument>(cursoId, new PartitionKey(twinId));

            _logger.LogInformation("✅ Course deleted successfully: {CursoId}", cursoId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("⚠️ Course not found for deletion: {CursoId}", cursoId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting course: {CursoId} for Twin: {TwinId}", cursoId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Actualizar el estado de un curso
    /// </summary>
    public async Task<bool> UpdateCourseStatusAsync(string cursoId, string twinId, string newStatus)
    {
        try
        {
            _logger.LogInformation("📚 Updating course status: {CursoId} to {Status}", cursoId, newStatus);

            var existingDoc = await GetCursoByIdAsync(cursoId, twinId);
            if (existingDoc == null)
            {
                _logger.LogWarning("⚠️ Course not found for status update: {CursoId}", cursoId);
                return false;
            }

            // Actualizar el estado del curso
            if (existingDoc.CursoData.Metadatos != null)
            {
                existingDoc.CursoData.Metadatos.EstadoCurso = newStatus;
                existingDoc.UpdatedAt = DateTime.UtcNow;

                await _cursosContainer.UpsertItemAsync(existingDoc, new PartitionKey(twinId));

                _logger.LogInformation("✅ Course status updated successfully: {CursoId} -> {Status}", cursoId, newStatus);
                return true;
            }

            _logger.LogWarning("⚠️ Course metadata not found for status update: {CursoId}", cursoId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update course status: {CursoId}", cursoId);
            return false;
        }
    }
}

/// <summary>
/// Documento de curso para Cosmos DB con metadatos
/// </summary>
public class CursoDocument
{
    public string id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public CrearCursoRequest CursoData { get; set; } = new CrearCursoRequest();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}