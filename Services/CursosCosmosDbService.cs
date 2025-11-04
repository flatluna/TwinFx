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
    public async Task<List<CursoSeleccionado>> GetCursosAIByTwinIdAndIDAsync(string twinId, string CursoID, string Titulo)
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

    public async Task<List<CursoSeleccionado>> GetCursosAIByTwinIdAndTituloAsync(string twinId,
        string id,
        string titulo)
    {
        id = "fe953cc8-2960-4647-a5d1-b93b8589d57d";
        try
        {
            var _cursosContainer = _database.GetContainer("TwinCursosAI");
            _logger.LogInformation("📚 Getting courses with chapter title for Twin ID: {TwinId} and Chapter Title: {Titulo}", twinId, titulo);

            // Query para buscar cursos que contengan capítulos con el título especificado
            var sql = @"  
                  SELECT VALUE c  
                  FROM c  
                  WHERE c.id = @id 
                    AND EXISTS (  
                      SELECT VALUE cap FROM cap IN c.capitulos WHERE CONTAINS(LOWER(cap.Titulo), LOWER(@titulo))  
                    )";

               var query = new QueryDefinition(sql)
              .WithParameter("@id", id)
             .WithParameter("@titulo", titulo.Trim());


            var iterator = _cursosContainer.GetItemQueryIterator<CursoSeleccionado>(query);
            var cursos = new List<CursoSeleccionado>();
            int iterationCount = 0;


            using var iterator2 = _cursosContainer.GetItemQueryIterator<CursoSeleccionado>(
                query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(twinId) });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync();
                foreach (var curso in page)
                {
                    
                }
            }

            while (iterator.HasMoreResults)
            {
                iterationCount++;
                _logger.LogInformation("📊 Reading iteration {Iteration}...", iterationCount);
                
                var response = await iterator.ReadNextAsync();
                
                _logger.LogInformation("📋 Response details: Count={Count}, RequestCharge={RequestCharge}, StatusCode={StatusCode}", 
                    response.Count, response.RequestCharge, response.StatusCode);

                if (response.Resource != null)
                {
                    var resourceList = response.Resource.ToList();
                    _logger.LogInformation("📦 Resource items found: {ResourceCount}", resourceList.Count);
                    
                    foreach (var item in resourceList)
                    {
                        // 🔍 DEBUGGING: Obtener el JSON string del item ANTES de verificar si es null
                        var currentIndex = cursos.Count; // Índice del item actual
                        var resourceArray = response.Resource.ToArray();
                        string rawJson = "{}";
                        
                        try
                        {
                            if (currentIndex < resourceArray.Length)
                            {
                                var rawJsonObject = resourceArray[currentIndex];
                                rawJson = JsonSerializer.Serialize(rawJsonObject);
                                var manualDeserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<CursoSeleccionado>(rawJson);
                                if (manualDeserialized != null)
                                {
                                    _logger.LogInformation("✅ Manual deserialization successful: ID={CourseId}, Name={CourseName}",
                                        manualDeserialized.id, manualDeserialized.NombreClase);
                                    cursos.Add(manualDeserialized);
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Manual deserialization returned null");
                                }
                            }
                        }
                        catch (Exception jsonEx)
                        {
                            _logger.LogError(jsonEx, "❌ Error getting JSON for item at index {Index}", currentIndex);
                        }

                        if (item != null)
                        {
                            _logger.LogInformation("✅ Found course: ID={CourseId}, Name={CourseName}", 
                                item.id ?? "NULL", item.NombreClase ?? "NULL");

                            // 🔍 DEBUGGING ADICIONAL: Verificar propiedades específicas que podrían estar causando problemas
                            _logger.LogInformation("🔧 DEBUG Item Details:");
                            _logger.LogInformation("   TwinID: {TwinID}", item.TwinID ?? "NULL");
                            _logger.LogInformation("   Instructor: {Instructor}", item.Instructor ?? "NULL");
                            _logger.LogInformation("   Plataforma: {Plataforma}", item.Plataforma ?? "NULL");
                            _logger.LogInformation("   Capitulos Count: {CapitulosCount}", item.Capitulos?.Count ?? 0);
                            
                            // Verificar si Capitulos es null o tiene datos
                            if (item.Capitulos == null)
                            {
                                _logger.LogWarning("⚠️ Capitulos property is NULL for course {CourseId}", item.id);
                            }
                            else if (item.Capitulos.Count == 0)
                            {
                                _logger.LogWarning("⚠️ Capitulos property is empty for course {CourseId}", item.id);
                            }
                            else
                            {
                                _logger.LogInformation("✅ Capitulos property has {Count} chapters for course {CourseId}", 
                                    item.Capitulos.Count, item.id);
                                
                                // Mostrar detalles del primer capítulo para debugging
                                var firstChapter = item.Capitulos.FirstOrDefault();
                                if (firstChapter != null)
                                {
                                    _logger.LogInformation("🔧 DEBUG First Chapter: Titulo='{Titulo}', NumeroCapitulo={Numero}", 
                                        firstChapter.Titulo ?? "NULL", firstChapter.NumeroCapitulo);
                                }
                            }

                            cursos.Add(item);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Found null item in response - serialization issue detected!");
                            
                            // 🔍 DEBUGGING: Intentar deserializar manualmente con Newtonsoft.Json usando el JSON obtenido arriba
                            try
                            {
                                if (!string.IsNullOrEmpty(rawJson) && rawJson != "{}")
                                {
                                    _logger.LogInformation("🔧 Attempting manual deserialization with Newtonsoft.Json...");
                                    
                                    var manualDeserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<CursoSeleccionado>(rawJson);
                                    if (manualDeserialized != null)
                                    {
                                        _logger.LogInformation("✅ Manual deserialization successful: ID={CourseId}, Name={CourseName}", 
                                            manualDeserialized.id, manualDeserialized.NombreClase);
                                        cursos.Add(manualDeserialized);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("⚠️ Manual deserialization returned null");
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ No valid JSON available for manual deserialization");
                                }
                            }
                            catch (Exception deserEx)
                            {
                                _logger.LogError(deserEx, "❌ Manual deserialization also failed for JSON: {Json}", 
                                    rawJson.Length > 200 ? rawJson.Substring(0, 200) + "..." : rawJson);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ Response.Resource is null for iteration {Iteration}", iterationCount);
                }
            }

            _logger.LogInformation("✅ Query completed: Retrieved {Count} courses with chapter title for Twin ID: {TwinId} and Chapter Title: {Titulo}", 
                cursos.Count, twinId, titulo);

            // DEBUGGING: Si no se encontraron resultados, probar una consulta más simple
            if (cursos.Count == 0)
            {
                _logger.LogInformation("🔍 No results found. Trying simplified query for debugging...");
                
                var simpleQuery = new QueryDefinition("SELECT c.id, c.nombreClase, c.TwinID FROM c WHERE c.TwinID = @twinId")
                    .WithParameter("@twinId", twinId);
                
                var simpleIterator = _cursosContainer.GetItemQueryIterator<dynamic>(simpleQuery);
                var foundAny = false;
                
                while (simpleIterator.HasMoreResults)
                {
                    var simpleResponse = await simpleIterator.ReadNextAsync();
                    if (simpleResponse.Resource?.Any() == true)
                    {
                        foundAny = true;
                        _logger.LogInformation("🔍 Found {Count} courses for TwinID (without chapter filter)", simpleResponse.Count);
                        
                        // 🔍 Intentar query alternativo usando JsonSerializer diferente
                        var alternativeQuery = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId")
                            .WithParameter("@twinId", twinId);
                        
                        var alternativeIterator = _cursosContainer.GetItemQueryIterator<Dictionary<string, object>>(alternativeQuery);
                        
                        while (alternativeIterator.HasMoreResults)
                        {
                            var altResponse = await alternativeIterator.ReadNextAsync();
                            _logger.LogInformation("🔧 DEBUG Alternative query found {Count} items", altResponse.Count);
                            
                            foreach (var item in altResponse.Resource)
                            {
                                if (item != null && item.ContainsKey("capitulos"))
                                {
                                    _logger.LogInformation("🔧 DEBUG Course has capitulos property: {HasCapitulos}", 
                                        item["capitulos"] != null);
                                    break; // Solo verificar el primero
                                }
                            }
                            break; // Solo verificar la primera página
                        }
                        
                        break;
                    }
                }
                
                if (!foundAny)
                {
                    _logger.LogWarning("⚠️ No courses found at all for TwinID: {TwinId}. Check if TwinID exists in container.", twinId);
                }
                else
                {
                    _logger.LogInformation("🔍 Courses exist for TwinID, but none match the chapter title filter: {Titulo}", titulo);
                }
            }

            return cursos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting course with chapter title for Twin: {TwinId} and Chapter Title: {Titulo}", twinId, titulo);
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

    /// <summary>
    /// Método de debugging para verificar si existen cursos con capítulos
    /// </summary>
    public async Task<List<CursoSeleccionado>> DebugGetCursosWithChaptersAsync(string twinId)
    {
        try
        {
            var _cursosContainer = _database.GetContainer("TwinCursosAI");
            _logger.LogInformation("🔧 DEBUG: Getting all courses with chapters for Twin ID: {TwinId}", twinId);

            // Query simple para obtener todos los cursos del Twin
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId")
                .WithParameter("@twinId", twinId);

            var iterator = _cursosContainer.GetItemQueryIterator<CursoSeleccionado>(query);
            var cursos = new List<CursoSeleccionado>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                cursos.AddRange(response);
            }

            _logger.LogInformation("🔧 DEBUG: Found {Count} total courses for TwinID: {TwinId}", cursos.Count, twinId);

            // Analizar cada curso y sus capítulos
            foreach (var curso in cursos)
            {
                _logger.LogInformation("🔧 DEBUG: Course ID={CourseId}, Name={CourseName}", 
                    curso.id ?? "NULL", curso.NombreClase ?? "NULL");
                
                if (curso.Capitulos == null)
                {
                    _logger.LogInformation("🔧 DEBUG: Course {CourseId} has NULL capitulos", curso.id);
                }
                else if (curso.Capitulos.Count == 0)
                {
                    _logger.LogInformation("🔧 DEBUG: Course {CourseId} has empty capitulos list", curso.id);
                }
                else
                {
                    _logger.LogInformation("🔧 DEBUG: Course {CourseId} has {ChapterCount} chapters:", 
                        curso.id, curso.Capitulos.Count);
                    
                    for (int i = 0; i < Math.Min(curso.Capitulos.Count, 3); i++) // Solo mostrar primeros 3
                    {
                        var cap = curso.Capitulos[i];
                        _logger.LogInformation("🔧 DEBUG:   Chapter {Index}: Titulo='{Titulo}', NumeroCapitulo={Numero}", 
                            i + 1, cap.Titulo ?? "NULL", cap.NumeroCapitulo);
                    }
                    
                    if (curso.Capitulos.Count > 3)
                    {
                        _logger.LogInformation("🔧 DEBUG:   ... and {MoreCount} more chapters", 
                            curso.Capitulos.Count - 3);
                    }
                }
            }

            return cursos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in debug method for Twin: {TwinId}", twinId);
            return new List<CursoSeleccionado>();
        }
    }

    /// <summary>
    /// Método alternativo que usa Dictionary<string, object> para evitar problemas de deserialización
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCursosAIRawByTwinIdAndTituloAsync(string twinId, string titulo)
    {
        try
        {
            var _cursosContainer = _database.GetContainer("TwinCursosAI");
            _logger.LogInformation("🔧 RAW METHOD: Getting courses with chapter title for Twin ID: {TwinId} and Chapter Title: {Titulo}", twinId, titulo);

            // Query idéntico pero usando Dictionary<string, object>
            var query = new QueryDefinition(@"
                SELECT c 
                FROM c 
                WHERE c.TwinID = @twinId 
                AND EXISTS(
                    SELECT VALUE cap 
                    FROM cap IN c.capitulos 
                    WHERE CONTAINS(LOWER(cap.Titulo), LOWER(@titulo))
                )")
                .WithParameter("@twinId", twinId)
                .WithParameter("@titulo", titulo);

            var iterator = _cursosContainer.GetItemQueryIterator<Dictionary<string, object>>(query);
            var cursosRaw = new List<Dictionary<string, object>>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                
                _logger.LogInformation("🔧 RAW: Response details: Count={Count}, RequestCharge={RequestCharge}", 
                    response.Count, response.RequestCharge);

                if (response.Resource != null)
                {
                    foreach (var rawItem in response.Resource)
                    {
                        if (rawItem != null)
                        {
                            _logger.LogInformation("🔧 RAW: Found raw item with keys: {Keys}", 
                                string.Join(", ", rawItem.Keys));
                            
                            // Verificar si tiene las propiedades esperadas
                            if (rawItem.ContainsKey("id"))
                                _logger.LogInformation("🔧 RAW: id = {Id}", rawItem["id"]);
                            if (rawItem.ContainsKey("nombreClase"))
                                _logger.LogInformation("🔧 RAW: nombreClase = {NombreClase}", rawItem["nombreClase"]);
                            if (rawItem.ContainsKey("TwinID"))
                                _logger.LogInformation("🔧 RAW: TwinID = {TwinID}", rawItem["TwinID"]);
                            if (rawItem.ContainsKey("capitulos"))
                            {
                                var capitulos = rawItem["capitulos"];
                                if (capitulos != null)
                                {
                                    _logger.LogInformation("🔧 RAW: capitulos type = {Type}", capitulos.GetType().Name);
                                    if (capitulos is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
                                    {
                                        _logger.LogInformation("🔧 RAW: capitulos array length = {Length}", jsonElement.GetArrayLength());
                                    }
                                    else if (capitulos is System.Collections.ICollection collection)
                                    {
                                        _logger.LogInformation("🔧 RAW: capitulos collection count = {Count}", collection.Count);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("🔧 RAW: capitulos is null!");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("🔧 RAW: capitulos property not found!");
                            }

                            cursosRaw.Add(rawItem);
                        }
                        else
                        {
                            _logger.LogWarning("🔧 RAW: Found null raw item");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("🔧 RAW: Response.Resource is null");
                }
            }

            _logger.LogInformation("🔧 RAW: Completed - Retrieved {Count} raw courses", cursosRaw.Count);
            return cursosRaw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in raw method for Twin: {TwinId} and Title: {Titulo}", twinId, titulo);
            return new List<Dictionary<string, object>>();
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