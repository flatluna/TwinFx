using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Service class for managing Diary entries in Cosmos DB
/// Container: TwinDiary, PartitionKey: TwinID
/// ========================================================================
/// 
/// Proporciona operaciones CRUD completas para entradas de diario con:
/// - Gestión de entradas de diario con tipado fuerte
/// - Filtrado avanzado por múltiples criterios
/// - Estadísticas y análisis de actividades
/// - Paginación y ordenamiento
/// - Soft delete para preservar datos
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class DiaryCosmosDbService
{
    private readonly ILogger<DiaryCosmosDbService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly Container _diaryContainer;

    public DiaryCosmosDbService(ILogger<DiaryCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions)
    {
        _logger = logger;
        var cosmosSettings = cosmosOptions.Value;

        _logger.LogInformation("📔 Initializing Diary Cosmos DB Service");
        _logger.LogInformation($"   🔗 Endpoint: {cosmosSettings.Endpoint}");
        _logger.LogInformation($"   💾 Database: {cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   📦 Container: TwinDiary");

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
            _diaryContainer = _database.GetContainer("TwinDiary");
            
            _logger.LogInformation("✅ Diary Cosmos DB Service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Diary Cosmos DB client");
            throw;
        }
    }

    // ========================================
    // CRUD OPERATIONS
    // ========================================

    /// <summary>
    /// Crear una nueva entrada de diario
    /// </summary>
    public async Task<bool> CreateDiaryEntryAsync(DiaryEntry entry)
    {
        try
        {
            _logger.LogInformation("📔 Creating diary entry: {Title} for Twin: {TwinId}", 
                entry.Titulo, entry.TwinId);

            // Generar ID si no se proporciona
            if (string.IsNullOrEmpty(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString();
            }

            // Asegurar timestamps
            entry.FechaCreacion = DateTime.UtcNow;
            entry.FechaModificacion = DateTime.UtcNow;

            // Convertir a diccionario para Cosmos DB
            var entryDict = ConvertDiaryEntryToDict(entry);
            
            await _diaryContainer.CreateItemAsync(entryDict, new PartitionKey(entry.TwinId));

            _logger.LogInformation("✅ Diary entry created successfully: {Title} (ID: {Id})", 
                entry.Titulo, entry.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create diary entry: {Title} for Twin: {TwinId}", 
                entry.Titulo, entry.TwinId);
            return false;
        }
    }

    /// <summary>
    /// Obtener entrada de diario por ID
    /// </summary>
    public async Task<DiaryEntry?> GetDiaryEntryByIdAsync(string entryId, string twinId)
    {
        try
        {
            _logger.LogInformation("🔍 Getting diary entry by ID: {EntryId} for Twin: {TwinId}", entryId, twinId);

            var response = await _diaryContainer.ReadItemAsync<Dictionary<string, object?>>(
                entryId,
                new PartitionKey(twinId)
            );

            var entry = ConvertDictToDiaryEntry(response.Resource);
            
            // Verificar si está eliminado (soft delete)
            if (entry.Eliminado)
            {
                _logger.LogInformation("🗑️ Diary entry {EntryId} is soft deleted", entryId);
                return null;
            }

            _logger.LogInformation("✅ Diary entry retrieved successfully: {Title}", entry.Titulo);
            return entry;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("📔 Diary entry not found: {EntryId} for Twin: {TwinId}", entryId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting diary entry by ID: {EntryId} for Twin: {TwinId}", entryId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Obtener todas las entradas de diario de un Twin con filtros opcionales
    /// </summary>
    public async Task<(List<DiaryEntry> entries, DiaryStats stats)> GetDiaryEntriesAsync(string twinId, DiaryEntryQuery query)
    {
        try
        {
            _logger.LogInformation("📔 Getting diary entries for Twin: {TwinId} with filters", twinId);

            // Construir query SQL dinámico
            var (sql, parameters) = BuildDiaryQuery(twinId, query);
            
            _logger.LogInformation("🔍 Executing query: {Sql}", sql);

            var cosmosQuery = new QueryDefinition(sql);
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter(param.Key, param.Value);
            }

            var iterator = _diaryContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var entries = new List<DiaryEntry>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var entry = ConvertDictToDiaryEntry(item);
                        // Excluir entradas eliminadas (soft delete)
                        if (!entry.Eliminado)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to DiaryEntry: {Id}", 
                            item.GetValueOrDefault("id"));
                    }
                }
            }

            // Aplicar paginación en memoria (para simplificar)
            var totalEntries = entries.Count;
            var pagedEntries = entries
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            // Calcular estadísticas
            var stats = await CalculateDiaryStatsAsync(entries);

            _logger.LogInformation("✅ Retrieved {Count} diary entries (total: {Total}) for Twin: {TwinId}", 
                pagedEntries.Count, totalEntries, twinId);

            return (pagedEntries, stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting diary entries for Twin: {TwinId}", twinId);
            return (new List<DiaryEntry>(), new DiaryStats());
        }
    }

    /// <summary>
    /// Actualizar entrada de diario existente
    /// </summary>
    public async Task<bool> UpdateDiaryEntryAsync(DiaryEntry entry)
    {
        try
        {
            _logger.LogInformation("📝 Updating diary entry: {Id} for Twin: {TwinId}", 
                entry.Id, entry.TwinId);

            // Actualizar timestamp de modificación
            entry.FechaModificacion = DateTime.UtcNow;
            entry.Version++;

            // Convertir a diccionario para Cosmos DB
            var entryDict = ConvertDiaryEntryToDict(entry);
            
            await _diaryContainer.UpsertItemAsync(entryDict, new PartitionKey(entry.TwinId));

            _logger.LogInformation("✅ Diary entry updated successfully: {Title}", entry.Titulo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update diary entry: {Id} for Twin: {TwinId}", 
                entry.Id, entry.TwinId);
            return false;
        }
    }

    /// <summary>
    /// Eliminar entrada de diario (soft delete)
    /// </summary>
    public async Task<bool> DeleteDiaryEntryAsync(string entryId, string twinId)
    {
        try
        {
            _logger.LogInformation("🗑️ Soft deleting diary entry: {EntryId} for Twin: {TwinId}", entryId, twinId);

            // Obtener la entrada existente
            var entry = await GetDiaryEntryByIdAsync(entryId, twinId);
            if (entry == null)
            {
                _logger.LogWarning("⚠️ Diary entry not found for deletion: {EntryId}", entryId);
                return false;
            }

            // Marcar como eliminado (soft delete)
            entry.Eliminado = true;
            entry.FechaModificacion = DateTime.UtcNow;
            entry.Version++;

            // Actualizar en Cosmos DB
            return await UpdateDiaryEntryAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting diary entry: {EntryId} for Twin: {TwinId}", entryId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Búsqueda de texto libre en entradas de diario
    /// </summary>
    public async Task<List<DiaryEntry>> SearchDiaryEntriesAsync(string twinId, string searchTerm, int limit = 20)
    {
        try
        {
            _logger.LogInformation("🔍 Searching diary entries for Twin: {TwinId}, term: '{SearchTerm}'", twinId, searchTerm);

            var sql = @"
                SELECT * FROM c 
                WHERE c.TwinId = @twinId 
                AND c.eliminado = false
                AND (
                    CONTAINS(LOWER(c.titulo), LOWER(@searchTerm))
                    OR CONTAINS(LOWER(c.descripcion), LOWER(@searchTerm))
                    OR CONTAINS(LOWER(c.tipoActividad), LOWER(@searchTerm))
                    OR CONTAINS(LOWER(c.ubicacion), LOWER(@searchTerm))
                )
                ORDER BY c.fechaModificacion DESC
                OFFSET 0 LIMIT @limit";

            var query = new QueryDefinition(sql)
                .WithParameter("@twinId", twinId)
                .WithParameter("@searchTerm", searchTerm)
                .WithParameter("@limit", limit);

            var iterator = _diaryContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var entries = new List<DiaryEntry>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var entry = ConvertDictToDiaryEntry(item);
                        entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting search result to DiaryEntry: {Id}", 
                            item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} diary entries matching search term", entries.Count);
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error searching diary entries for Twin: {TwinId}", twinId);
            return new List<DiaryEntry>();
        }
    }

    /// <summary>
    /// Obtener estadísticas generales del diario de un Twin
    /// </summary>
    public async Task<DiaryStats> GetDiaryStatsAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("📊 Calculating diary stats for Twin: {TwinId}", twinId);

            // Obtener todas las entradas no eliminadas
            var query = new DiaryEntryQuery { PageSize = int.MaxValue };
            var (entries, _) = await GetDiaryEntriesAsync(twinId, query);

            return await CalculateDiaryStatsAsync(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error calculating diary stats for Twin: {TwinId}", twinId);
            return new DiaryStats();
        }
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Construir query SQL dinámico basado en filtros
    /// </summary>
    private (string sql, Dictionary<string, object> parameters) BuildDiaryQuery(string twinId, DiaryEntryQuery query)
    {
        var conditions = new List<string> { "c.TwinID = @twinId", "c.eliminado = false" };
        var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

        // Filtros por tipo de actividad
        if (!string.IsNullOrEmpty(query.TipoActividad))
        {
            conditions.Add("c.tipoActividad = @tipoActividad");
            parameters["@tipoActividad"] = query.TipoActividad;
        }

        // Filtros por rango de fechas
        if (query.FechaDesde.HasValue)
        {
            conditions.Add("c.fecha >= @fechaDesde");
            parameters["@fechaDesde"] = query.FechaDesde.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        if (query.FechaHasta.HasValue)
        {
            conditions.Add("c.fecha <= @fechaHasta");
            parameters["@fechaHasta"] = query.FechaHasta.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        // Filtro por ubicación
        if (!string.IsNullOrEmpty(query.Ubicacion))
        {
            conditions.Add("CONTAINS(LOWER(c.ubicacion), LOWER(@ubicacion))");
            parameters["@ubicacion"] = query.Ubicacion;
        }

        // Filtro por estado emocional
        if (!string.IsNullOrEmpty(query.EstadoEmocional))
        {
            conditions.Add("c.estadoEmocional = @estadoEmocional");
            parameters["@estadoEmocional"] = query.EstadoEmocional;
        }

        // Filtro por nivel mínimo de energía
        if (query.NivelEnergiaMin.HasValue)
        {
            conditions.Add("c.nivelEnergia >= @nivelEnergiaMin");
            parameters["@nivelEnergiaMin"] = query.NivelEnergiaMin.Value;
        }

        // Filtro por gasto máximo
        if (query.GastoMaximo.HasValue)
        {
            conditions.Add("c.gastoTotal <= @gastoMaximo");
            parameters["@gastoMaximo"] = query.GastoMaximo.Value;
        }

        // Búsqueda de texto libre
        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            conditions.Add(@"(
                CONTAINS(LOWER(c.titulo), LOWER(@searchTerm))
                OR CONTAINS(LOWER(c.descripcion), LOWER(@searchTerm))
                OR CONTAINS(LOWER(c.tipoActividad), LOWER(@searchTerm))
            )");
            parameters["@searchTerm"] = query.SearchTerm;
        }

        // Construir ORDER BY
        var orderBy = query.SortBy?.ToLowerInvariant() switch
        {
            "fecha" => "c.fecha",
            "fechacreacion" => "c.fechaCreacion",
            "titulo" => "c.titulo",
            "gastoTotal" => "c.gastoTotal",
            "nivelEnergia" => "c.nivelEnergia",
            _ => "c.fecha"
        };

        var orderDirection = query.SortDirection?.ToUpperInvariant() == "ASC" ? "ASC" : "DESC";

        var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY {orderBy} {orderDirection}";

        return (sql, parameters);
    }

    /// <summary>
    /// Calcular estadísticas del diario
    /// </summary>
    private async Task<DiaryStats> CalculateDiaryStatsAsync(List<DiaryEntry> entries)
    {
        return await Task.FromResult(new DiaryStats
        {
            TotalEntries = entries.Count,
            ByActivityType = entries
                .Where(e => !string.IsNullOrEmpty(e.TipoActividad))
                .GroupBy(e => e.TipoActividad)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByEmotionalState = entries
                .Where(e => !string.IsNullOrEmpty(e.EstadoEmocional))
                .GroupBy(e => e.EstadoEmocional)
                .ToDictionary(g => g.Key, g => g.Count()),
            TotalSpent = entries.Sum(e => e.GastoTotal ?? 0),
            AverageEnergyLevel = entries.Where(e => e.NivelEnergia.HasValue).Any() 
                ? entries.Where(e => e.NivelEnergia.HasValue).Average(e => e.NivelEnergia!.Value) 
                : 0,
            TotalCaloriesBurned = entries.Sum(e => e.CaloriasQuemadas ?? 0),
            TotalHoursWorked = entries.Sum(e => e.HorasTrabajadas ?? 0),
            SpendingByCategory = new Dictionary<string, decimal>
            {
                ["Comida"] = entries.Sum(e => e.CostoComida ?? 0),
                ["Viaje"] = entries.Sum(e => e.CostoViaje ?? 0),
                ["Entretenimiento"] = entries.Sum(e => e.CostoEntretenimiento ?? 0),
                ["Ejercicio"] = entries.Sum(e => e.CostoEjercicio ?? 0),
                ["Estudio"] = entries.Sum(e => e.CostoEstudio ?? 0),
                ["Salud"] = entries.Sum(e => e.CostoSalud ?? 0)
            },
            TopLocations = entries
                .Where(e => !string.IsNullOrEmpty(e.Ubicacion))
                .GroupBy(e => e.Ubicacion)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList(),
            MostRecentEntry = entries.Any() ? entries.Max(e => e.FechaModificacion) : null,
            OldestEntry = entries.Any() ? entries.Min(e => e.FechaCreacion) : null
        });
    }

    /// <summary>
    /// Convertir DiaryEntry a diccionario para Cosmos DB
    /// </summary>
    private Dictionary<string, object?> ConvertDiaryEntryToDict(DiaryEntry entry)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = entry.Id,
            ["TwinID"] = entry.TwinId,
            ["type"] = entry.Version, // Importante: usar "version" en lugar de "Type" para compatibilidad
            ["version"] = entry.Version,
            ["eliminado"] = entry.Eliminado,
            
            // Información básica
            ["titulo"] = entry.Titulo,
            ["descripcion"] = entry.Descripcion,
            ["fecha"] = entry.Fecha.ToString("O"),
            ["fechaCreacion"] = entry.FechaCreacion.ToString("O"),
            ["fechaModificacion"] = entry.FechaModificacion.ToString("O"),
            
            // Categorización
            ["tipoActividad"] = entry.TipoActividad,
            ["labelActividad"] = entry.LabelActividad,
            
            // Ubicación
            ["ubicacion"] = entry.Ubicacion,
            ["latitud"] = entry.Latitud,
            ["longitud"] = entry.Longitud,
            
            // Nuevos campos de ubicación detallada y contacto
            ["pais"] = entry.Pais,
            ["ciudad"] = entry.Ciudad,
            ["estadoProvincia"] = entry.Estado,
            ["codigoPostal"] = entry.CodigoPostal,
            ["direccionEspecifica"] = entry.DireccionEspecifica,
            ["telefono"] = entry.Telefono,
            ["website"] = entry.Website,
            ["distritoColonia"] = entry.DistritoColonia,
            
            // Estado emocional y energía
            ["estadoEmocional"] = entry.EstadoEmocional,
            ["nivelEnergia"] = entry.NivelEnergia,
            
            // Campos comerciales
            ["gastoTotal"] = entry.GastoTotal,
            ["productosComprados"] = entry.ProductosComprados,
            ["tiendaLugar"] = entry.TiendaLugar,
            ["metodoPago"] = entry.MetodoPago,
            ["categoriaCompra"] = entry.CategoriaCompra,
            ["satisfaccionCompra"] = entry.SatisfaccionCompra,
            
            // Campos gastronómicos
            ["costoComida"] = entry.CostoComida,
            ["restauranteLugar"] = entry.RestauranteLugar,
            ["tipoCocina"] = entry.TipoCocina,
            ["platosOrdenados"] = entry.PlatosOrdenados,
            ["calificacionComida"] = entry.CalificacionComida,
            ["ambienteComida"] = entry.AmbienteComida,
            ["recomendariaComida"] = entry.RecomendariaComida,
            
            // Campos de viaje
            ["costoViaje"] = entry.CostoViaje,
            ["destinoViaje"] = entry.DestinoViaje,
            ["transporteViaje"] = entry.TransporteViaje,
            ["propositoViaje"] = entry.PropositoViaje,
            ["calificacionViaje"] = entry.CalificacionViaje,
            ["duracionViaje"] = entry.DuracionViaje,
            
            // Campos de entretenimiento
            ["costoEntretenimiento"] = entry.CostoEntretenimiento,
            ["calificacionEntretenimiento"] = entry.CalificacionEntretenimiento,
            ["tipoEntretenimiento"] = entry.TipoEntretenimiento,
            ["tituloNombre"] = entry.TituloNombre,
            ["lugarEntretenimiento"] = entry.LugarEntretenimiento,
            
            // Campos de ejercicio
            ["costoEjercicio"] = entry.CostoEjercicio,
            ["energiaPostEjercicio"] = entry.EnergiaPostEjercicio,
            ["caloriasQuemadas"] = entry.CaloriasQuemadas,
            ["tipoEjercicio"] = entry.TipoEjercicio,
            ["duracionEjercicio"] = entry.DuracionEjercicio,
            ["intensidadEjercicio"] = entry.IntensidadEjercicio,
            ["lugarEjercicio"] = entry.LugarEjercicio,
            ["rutinaEspecifica"] = entry.RutinaEspecifica,
            
            // Campos de estudio
            ["costoEstudio"] = entry.CostoEstudio,
            ["dificultadEstudio"] = entry.DificultadEstudio,
            ["estadoAnimoPost"] = entry.EstadoAnimoPost,
            ["materiaTema"] = entry.MateriaTema,
            ["materialEstudio"] = entry.MaterialEstudio,
            ["duracionEstudio"] = entry.DuracionEstudio,
            ["progresoEstudio"] = entry.ProgresoEstudio,
            
            // Campos de trabajo
            ["horasTrabajadas"] = entry.HorasTrabajadas,
            ["proyectoPrincipal"] = entry.ProyectoPrincipal,
            ["reunionesTrabajo"] = entry.ReunionesTrabajo,
            ["logrosHoy"] = entry.LogrosHoy,
            ["desafiosTrabajo"] = entry.DesafiosTrabajo,
            ["moodTrabajo"] = entry.MoodTrabajo,
            
            // Campos de salud
            ["costoSalud"] = entry.CostoSalud,
            ["tipoConsulta"] = entry.TipoConsulta,
            ["profesionalCentro"] = entry.ProfesionalCentro,
            ["motivoConsulta"] = entry.MotivoConsulta,
            ["tratamientoRecetado"] = entry.TratamientoRecetado,
            ["proximaCita"] = entry.ProximaCita?.ToString("O"),
            
            // Campos de comunicación
            ["contactoLlamada"] = entry.ContactoLlamada,
            ["duracionLlamada"] = entry.DuracionLlamada,
            ["motivoLlamada"] = entry.MotivoLlamada,
            ["temasConversacion"] = entry.TemasConversacion,
            ["tipoLlamada"] = entry.TipoLlamada,
            ["seguimientoLlamada"] = entry.SeguimientoLlamada,
            
            // Personas presentes
            ["participantes"] = entry.Participantes,
            
            // Archivo adjunto
            ["pathFile"] = entry.PathFile,
            
            // SAS URL no se guarda en BD, se genera dinámicamente
            ["sasUrl"] = ""
        };
    }

    /// <summary>
    /// Convertir diccionario de Cosmos DB a DiaryEntry
    /// </summary>
    private DiaryEntry ConvertDictToDiaryEntry(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
            {
                _logger.LogDebug("🔍 Key '{Key}' not found or is null", key);
                return defaultValue;
            }

            try
            {
                _logger.LogDebug("🔍 Processing key '{Key}': value={Value}, type={Type}", key, value, value?.GetType().Name);

                // Handle nullable types properly
                var targetType = typeof(T);
                var underlyingType = Nullable.GetUnderlyingType(targetType);
                var isNullable = underlyingType != null;

                // For nullable types, if the value is explicitly null, return null
                if (isNullable && value == null)
                {
                    return default!;
                }

                // Tipo directo
                if (value is T directValue)
                {
                    _logger.LogDebug("✅ Direct conversion for '{Key}': {Value}", key, directValue);
                    return directValue;
                }

                // Handle numeric conversions more robustly
                if (targetType == typeof(decimal?) || targetType == typeof(decimal))
                {
                    if (value is double doubleVal)
                        return (T)(object)(decimal)doubleVal;
                    if (value is float floatVal)
                        return (T)(object)(decimal)floatVal;
                    if (value is int intVal)
                        return (T)(object)(decimal)intVal;
                    if (value is long longVal)
                        return (T)(object)(decimal)longVal;
                }

                if (targetType == typeof(int?) || targetType == typeof(int))
                {
                    if (value is double doubleVal)
                        return (T)(object)(int)doubleVal;
                    if (value is float floatVal)
                        return (T)(object)(int)floatVal;
                    if (value is decimal decimalVal)
                        return (T)(object)(int)decimalVal;
                    if (value is long longVal)
                        return (T)(object)(int)longVal;
                }

                // JsonElement (System.Text.Json)
                if (value is JsonElement jsonElement)
                {
                    var result = ParseJsonElement<T>(jsonElement, defaultValue);
                    _logger.LogDebug("📄 JsonElement conversion for '{Key}': {Value} -> {Result}", key, jsonElement, result);
                    return result;
                }

                // JToken (Newtonsoft.Json)
                if (value is JToken jToken)
                {
                    var result = ParseJToken<T>(jToken, defaultValue);
                    _logger.LogDebug("📄 JToken conversion for '{Key}': {Value} -> {Result}", key, jToken, result);
                    return result;
                }

                // Conversión directa usando el tipo subyacente para nullable
                var conversionType = underlyingType ?? targetType;
                var convertedValue = Convert.ChangeType(value, conversionType);
                _logger.LogDebug("🔄 Direct ChangeType for '{Key}': {Value} -> {Result}", key, value, convertedValue);
                return (T)convertedValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing key '{Key}' (value: {Value}, type: {Type})", 
                    key, value, value?.GetType().Name);
                return defaultValue;
            }
        };

        return new DiaryEntry
        {
            Id = GetValue("id", string.Empty),
            TwinId = GetValue("TwinID", string.Empty), // ✅ CORREGIDO: Buscar "TwinID" no "TwinId"
            Type = GetValue("type", "diary_entry"),
            Version = GetValue("version", 1),
            Eliminado = GetValue("eliminado", false),
            
            // Información básica
            Titulo = GetValue("titulo", string.Empty),
            Descripcion = GetValue("descripcion", string.Empty),
            Fecha = GetValue("fecha", DateTime.UtcNow),
            FechaCreacion = GetValue("fechaCreacion", DateTime.UtcNow),
            FechaModificacion = GetValue("fechaModificacion", DateTime.UtcNow),
            
            // Categorización
            TipoActividad = GetValue("tipoActividad", string.Empty),
            LabelActividad = GetValue("labelActividad", string.Empty),
            
            // Ubicación
            Ubicacion = GetValue("ubicacion", string.Empty),
            Latitud = GetValue<double?>("latitud"),
            Longitud = GetValue<double?>("longitud"),
            
            // Nuevos campos de ubicación detallada y contacto
            Pais = GetValue("pais", string.Empty),
            Ciudad = GetValue("ciudad", string.Empty),
            Estado = GetValue("estadoProvincia", string.Empty),
            CodigoPostal = GetValue("codigoPostal", string.Empty),
            DireccionEspecifica = GetValue("direccionEspecifica", string.Empty),
            Telefono = GetValue("telefono", string.Empty),
            Website = GetValue("website", string.Empty),
            DistritoColonia = GetValue("distritoColonia", string.Empty),
            
            // Estado emocional y energía
            EstadoEmocional = GetValue("estadoEmocional", string.Empty),
            NivelEnergia = GetValue<int?>("nivelEnergia"),
            
            // Campos comerciales
            GastoTotal = GetValue<decimal?>("gastoTotal"),
            ProductosComprados = GetValue("productosComprados", string.Empty),
            TiendaLugar = GetValue("tiendaLugar", string.Empty),
            MetodoPago = GetValue("metodoPago", string.Empty),
            CategoriaCompra = GetValue("categoriaCompra", string.Empty),
            SatisfaccionCompra = GetValue<int?>("satisfaccionCompra"),
            
            // Campos gastronómicos
            CostoComida = GetValue<decimal?>("costoComida"),
            RestauranteLugar = GetValue("restauranteLugar", string.Empty),
            TipoCocina = GetValue("tipoCocina", string.Empty),
            PlatosOrdenados = GetValue("platosOrdenados", string.Empty),
            CalificacionComida = GetValue<int?>("calificacionComida"),
            AmbienteComida = GetValue("ambienteComida", string.Empty),
            RecomendariaComida = GetValue<bool?>("recomendariaComida"),
            
            // Campos de viaje
            CostoViaje = GetValue<decimal?>("costoViaje"),
            DestinoViaje = GetValue("destinoViaje", string.Empty),
            TransporteViaje = GetValue("transporteViaje", string.Empty),
            PropositoViaje = GetValue("propositoViaje", string.Empty),
            CalificacionViaje = GetValue<int?>("calificacionViaje"),
            DuracionViaje = GetValue<int?>("duracionViaje"),
            
            // Campos de entretenimiento
            CostoEntretenimiento = GetValue<decimal?>("costoEntretenimiento"),
            CalificacionEntretenimiento = GetValue<int?>("calificacionEntretenimiento"),
            TipoEntretenimiento = GetValue("tipoEntretenimiento", string.Empty),
            TituloNombre = GetValue("tituloNombre", string.Empty),
            LugarEntretenimiento = GetValue("lugarEntretenimiento", string.Empty),
            
            // Campos de ejercicio
            CostoEjercicio = GetValue<decimal?>("costoEjercicio"),
            EnergiaPostEjercicio = GetValue<int?>("energiaPostEjercicio"),
            CaloriasQuemadas = GetValue<int?>("caloriasQuemadas"),
            TipoEjercicio = GetValue("tipoEjercicio", string.Empty),
            DuracionEjercicio = GetValue<int?>("duracionEjercicio"),
            IntensidadEjercicio = GetValue<int?>("intensidadEjercicio"),
            LugarEjercicio = GetValue("lugarEjercicio", string.Empty),
            RutinaEspecifica = GetValue("rutinaEspecifica", string.Empty),
            
            // Campos de estudio
            CostoEstudio = GetValue<decimal?>("costoEstudio"),
            DificultadEstudio = GetValue<int?>("dificultadEstudio"),
            EstadoAnimoPost = GetValue<int?>("estadoAnimoPost"),
            MateriaTema = GetValue("materiaTema", string.Empty),
            MaterialEstudio = GetValue("materialEstudio", string.Empty),
            DuracionEstudio = GetValue<int?>("duracionEstudio"),
            ProgresoEstudio = GetValue<int?>("progresoEstudio"),
            
            // Campos de trabajo
            HorasTrabajadas = GetValue<int?>("horasTrabajadas"),
            ProyectoPrincipal = GetValue("proyectoPrincipal", string.Empty),
            ReunionesTrabajo = GetValue<int?>("reunionesTrabajo"),
            LogrosHoy = GetValue("logrosHoy", string.Empty),
            DesafiosTrabajo = GetValue("desafiosTrabajo", string.Empty),
            MoodTrabajo = GetValue<int?>("moodTrabajo"),
            
            // Campos de salud
            CostoSalud = GetValue<decimal?>("costoSalud"),
            TipoConsulta = GetValue("tipoConsulta", string.Empty),
            ProfesionalCentro = GetValue("profesionalCentro", string.Empty),
            MotivoConsulta = GetValue("motivoConsulta", string.Empty),
            TratamientoRecetado = GetValue("tratamientoRecetado", string.Empty),
            ProximaCita = GetValue<DateTime?>("proximaCita"),
            
            // Campos de comunicación
            ContactoLlamada = GetValue("contactoLlamada", string.Empty),
            DuracionLlamada = GetValue<int?>("duracionLlamada"),
            MotivoLlamada = GetValue("motivoLlamada", string.Empty),
            TemasConversacion = GetValue("temasConversacion", string.Empty),
            TipoLlamada = GetValue("tipoLlamada", string.Empty),
            SeguimientoLlamada = GetValue<bool?>("seguimientoLlamada"),
            
            // Personas presentes
            Participantes = GetValue("participantes", string.Empty),
            
            // Archivo adjunto
            PathFile = GetValue("pathFile", string.Empty),
            
            // SAS URL se generará dinámicamente, no se almacena en BD
            SasUrl = string.Empty
        };
    }

    /// <summary>
    /// Parse JsonElement to target type
    /// </summary>
    private T ParseJsonElement<T>(JsonElement jsonElement, T defaultValue)
    {
        try
        {
            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            var isNullable = underlyingType != null;
            var actualType = underlyingType ?? targetType;

            // Handle null for nullable types
            if (jsonElement.ValueKind == JsonValueKind.Null)
            {
                return isNullable ? default! : defaultValue;
            }

            if (actualType == typeof(string))
            {
                return (T)(object)(jsonElement.GetString() ?? defaultValue?.ToString() ?? string.Empty);
            }
            
            if (actualType == typeof(int))
            {
                if (jsonElement.TryGetInt32(out var intValue))
                    return (T)(object)intValue;
                
                // Try parsing as double and convert to int
                if (jsonElement.TryGetDouble(out var doubleValue))
                    return (T)(object)(int)doubleValue;
                    
                return defaultValue;
            }
            
            if (actualType == typeof(decimal))
            {
                if (jsonElement.TryGetDecimal(out var decimalValue))
                    return (T)(object)decimalValue;
                
                // Try parsing as double and convert to decimal
                if (jsonElement.TryGetDouble(out var doubleValue))
                    return (T)(object)(decimal)doubleValue;
                    
                return defaultValue;
            }
            
            if (actualType == typeof(double))
            {
                return jsonElement.TryGetDouble(out var doubleValue) ? (T)(object)doubleValue : defaultValue;
            }
            
            if (actualType == typeof(bool))
            {
                if (jsonElement.ValueKind == JsonValueKind.True)
                    return (T)(object)true;
                if (jsonElement.ValueKind == JsonValueKind.False)
                    return (T)(object)false;
                return defaultValue;
            }
            
            if (actualType == typeof(DateTime))
            {
                return jsonElement.TryGetDateTime(out var dateValue) ? (T)(object)dateValue : defaultValue;
            }

            // Try generic conversion
            var stringValue = jsonElement.GetString();
            if (!string.IsNullOrEmpty(stringValue))
            {
                return (T)Convert.ChangeType(stringValue, actualType);
            }

            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error in ParseJsonElement for type {Type}, value: {Value}", typeof(T).Name, jsonElement.ToString());
            return defaultValue;
        }
    }

    /// <summary>
    /// Parse JToken to target type
    /// </summary>
    private T ParseJToken<T>(JToken jToken, T defaultValue)
    {
        try
        {
            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            var isNullable = underlyingType != null;
            var actualType = underlyingType ?? targetType;

            // Handle null for nullable types
            if (jToken.Type == JTokenType.Null)
            {
                return isNullable ? default! : defaultValue;
            }

            if (actualType == typeof(string))
            {
                return (T)(object)(jToken.ToString() ?? defaultValue?.ToString() ?? string.Empty);
            }

            if (actualType == typeof(int))
            {
                if (jToken.Type == JTokenType.Integer)
                    return (T)(object)jToken.Value<int>();
                if (jToken.Type == JTokenType.Float)
                    return (T)(object)(int)jToken.Value<double>();
                return defaultValue;
            }

            if (actualType == typeof(decimal))
            {
                if (jToken.Type == JTokenType.Integer || jToken.Type == JTokenType.Float)
                    return (T)(object)jToken.Value<decimal>();
                return defaultValue;
            }

            if (actualType == typeof(double))
            {
                if (jToken.Type == JTokenType.Integer || jToken.Type == JTokenType.Float)
                    return (T)(object)jToken.Value<double>();
                return defaultValue;
            }

            if (actualType == typeof(bool))
            {
                if (jToken.Type == JTokenType.Boolean)
                    return (T)(object)jToken.Value<bool>();
                return defaultValue;
            }

            if (actualType == typeof(DateTime))
            {
                if (jToken.Type == JTokenType.Date)
                    return (T)(object)jToken.Value<DateTime>();
                if (jToken.Type == JTokenType.String && DateTime.TryParse(jToken.ToString(), out var dateValue))
                    return (T)(object)dateValue;
                return defaultValue;
            }
            
            // Use generic conversion
            return jToken.ToObject<T>() ?? defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error in ParseJToken for type {Type}, value: {Value}", typeof(T).Name, jToken.ToString());
            return defaultValue;
        }
    }
}