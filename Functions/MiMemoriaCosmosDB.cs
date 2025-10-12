using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Functions
{
    /// <summary>
    /// Service class for managing MiMemoria in Cosmos DB
    /// Container: TwinMiMemoria, PartitionKey: TwinId
    /// ========================================================================
    /// 
    /// Proporciona operaciones CRUD completas para memorias con:
    /// - Gestión de memorias con MiMemoria class
    /// - ID único y PartitionKey con TwinId
    /// - Timestamps automáticos
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class MiMemoriaCosmosDB
    {
        private readonly ILogger<MiMemoriaCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _memoriasContainer;

        public MiMemoriaCosmosDB(ILogger<MiMemoriaCosmosDB> logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            var cosmosSettings = cosmosOptions.Value;

            _logger.LogInformation("🧠 Initializing MiMemoria Cosmos DB Service");
            _logger.LogInformation($"   🔗 Endpoint: {cosmosSettings.Endpoint}");
            _logger.LogInformation($"   💾 Database: {cosmosSettings.DatabaseName}");
            _logger.LogInformation($"   📦 Container: TwinMiMemoria");

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
                _memoriasContainer = _database.GetContainer("TwinMiMemoria");
                
                _logger.LogInformation("✅ MiMemoria Cosmos DB Service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize MiMemoria Cosmos DB client");
                throw;
            }
        }

        /// <summary>
        /// Crear una nueva memoria
        /// </summary>
        public async Task<bool> CreateMemoriaAsync(MiMemoria memoria)
        {
            try
            {
                _logger.LogInformation("🧠 Creating memoria: {Titulo} for Twin: {TwinId}", 
                    memoria.Titulo, memoria.TwinID);

                // Generar ID si no se proporciona
                if (string.IsNullOrEmpty(memoria.id))
                {
                    memoria.id = Guid.NewGuid().ToString();
                }

                // Asegurar timestamps
                memoria.FechaCreacion = DateTime.UtcNow;
                memoria.FechaActualizacion = DateTime.UtcNow;

                // Convertir a diccionario para Cosmos DB
                var memoriaDict = ConvertMemoriaToDict(memoria);
                
                await _memoriasContainer.CreateItemAsync(memoriaDict, new PartitionKey(memoria.TwinID));

                _logger.LogInformation("✅ Memoria created successfully: {Titulo} (ID: {Id})", 
                    memoria.Titulo, memoria.id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create memoria: {Titulo} for Twin: {TwinId}", 
                    memoria.Titulo, memoria.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Obtener memoria por ID
        /// </summary>

        public async Task<MiMemoria?> GetMemoriaByIdAsync(string memoriaId, string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Getting memoria by ID: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @memoriaId AND c.TwinID = @twinID")
                    .WithParameter("@memoriaId", memoriaId)
                    .WithParameter("@twinID", twinId);

                var iterator = _memoriasContainer.GetItemQueryIterator<MiMemoria>(query);

                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var memoria = response.FirstOrDefault(); // Obtiene el primer elemento de la respuesta  

                    if (memoria != null)
                    {
                        // 📸 Generar SAS URLs para las fotos de la memoria    
                        if (memoria.Photos != null && memoria.Photos.Any())
                        {
                            _logger.LogDebug("📸 Processing {PhotoCount} photos for Memoria: {MemoriaId}", memoria.Photos.Count, memoria.id);
                            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                            foreach (var photo in memoria.Photos)
                            {
                                try
                                {
                                    // Construir la ruta completa del archivo    
                                    string photoPath;
                                    if (!string.IsNullOrEmpty(photo.Path))
                                    {
                                        // Si ya tiene path, usarlo    
                                        photoPath = $"{photo.Path.TrimEnd('/')}/{photo.FileName}";
                                    }
                                    else
                                    {
                                        // Si no tiene path, usar el path por defecto    
                                        photoPath = $"MiMemoria/{photo.FileName}";
                                    }
                                    _logger.LogDebug("📸 Generating SAS URL for photo: {PhotoPath}", photoPath);

                                    // Generar SAS URL (válida por 24 horas)    
                                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(photoPath, TimeSpan.FromHours(24));

                                    if (!string.IsNullOrEmpty(sasUrl))
                                    {
                                        photo.Url = sasUrl;
                                        _logger.LogDebug("✅ SAS URL generated successfully for photo: {FileName}", photo.FileName);
                                    }
                                    else
                                    {
                                        photo.Url = "";
                                        _logger.LogWarning("⚠️ Failed to generate SAS URL for photo: {FileName}", photo.FileName);
                                    }

                                    // Asegurar que el ContainerName esté configurado    
                                    if (string.IsNullOrEmpty(photo.ContainerName))
                                    {
                                        photo.ContainerName = twinId.ToLowerInvariant();
                                    }

                                    // Asegurar que el Path esté configurado    
                                    if (string.IsNullOrEmpty(photo.Path))
                                    {
                                        photo.Path = "MiMemoria";
                                    }
                                }
                                catch (Exception photoEx)
                                {
                                    _logger.LogWarning(photoEx, "⚠️ Error processing photo {FileName} for memoria {MemoriaId}", photo.FileName, memoria.id);
                                    // En caso de error, asegurar que la foto tenga valores por defecto    
                                    photo.Url = "";
                                    if (string.IsNullOrEmpty(photo.ContainerName))
                                    {
                                        photo.ContainerName = twinId.ToLowerInvariant();
                                    }
                                    if (string.IsNullOrEmpty(photo.Path))
                                    {
                                        photo.Path = "MiMemoria";
                                    }
                                }
                            }
                            _logger.LogInformation("📸 Processed {PhotoCount} photos for memoria: {MemoriaTitle}", memoria.Photos.Count, memoria.Titulo);
                        }

                        _logger.LogInformation("✅ Memoria retrieved successfully: {Titulo}", memoria.Titulo);
                        return memoria;
                    }
                    else
                    {
                        _logger.LogInformation("📝 Memoria not found: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
                        return null;
                    }
                }
                else
                {
                    _logger.LogInformation("📝 No results found for MemoriaId: {MemoriaId} and TwinId: {TwinId}", memoriaId, twinId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting memoria by ID: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
                return null;
            }
        }
        /// <summary>
        /// Obtener todas las memorias de un Twin
        /// </summary>
        public async Task<List<MiMemoria>> GetMemoriasByTwinIdAsync(string twinId)
        {
            
             try
            {
                _logger.LogInformation("🧠 Getting memorias for Twin: {TwinId}", twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinID ORDER BY c.FechaActualizacion DESC")
                    .WithParameter("@twinID", twinId);

                var iterator = _memoriasContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var memorias = new List<MiMemoria>();

                // Setup DataLake client para generar SAS URLs para las fotos
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();

                    foreach (var item in response)
                    {
                        try
                        {
                            // Convierte el item a JSON string  
                            string jsonString = JsonConvert.SerializeObject(item);

                            // Deserializa el string a un objeto MiMemoria  
                            MiMemoria memoria = JsonConvert.DeserializeObject<MiMemoria>(jsonString);

                            // 📸 NUEVO: Generar SAS URLs para las fotos de la memoria
                            if (memoria.Photos != null && memoria.Photos.Any())
                            {
                                _logger.LogDebug("📸 Processing {PhotoCount} photos for Memoria: {MemoriaId}", 
                                    memoria.Photos.Count, memoria.id);

                                foreach (var photo in memoria.Photos)
                                {
                                    try
                                    {
                                        // Construir la ruta completa del archivo
                                        string photoPath;
                                        if (!string.IsNullOrEmpty(photo.Path))
                                        {
                                            // Si ya tiene path, usarlo
                                            photoPath = $"{photo.Path.TrimEnd('/')}/{photo.FileName}";
                                        }
                                        else
                                        {
                                            // Si no tiene path, usar el path por defecto
                                            photoPath = $"MiMemoria/{photo.FileName}";
                                        }

                                        _logger.LogDebug("📸 Generating SAS URL for photo: {PhotoPath}", photoPath);

                                        // Generar SAS URL (válida por 24 horas)
                                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(photoPath, TimeSpan.FromHours(24));
                                        
                                        if (!string.IsNullOrEmpty(sasUrl))
                                        {
                                            photo.Url = sasUrl;
                                            _logger.LogDebug("✅ SAS URL generated successfully for photo: {FileName}", photo.FileName);
                                        }
                                        else
                                        {
                                            photo.Url = "";
                                            _logger.LogWarning("⚠️ Failed to generate SAS URL for photo: {FileName}", photo.FileName);
                                        }

                                        // Asegurar que el ContainerName esté configurado
                                        if (string.IsNullOrEmpty(photo.ContainerName))
                                        {
                                            photo.ContainerName = twinId.ToLowerInvariant();
                                        }

                                        // Asegurar que el Path esté configurado
                                        if (string.IsNullOrEmpty(photo.Path))
                                        {
                                            photo.Path = "MiMemoria";
                                        }
                                    }
                                    catch (Exception photoEx)
                                    {
                                        _logger.LogWarning(photoEx, "⚠️ Error processing photo {FileName} for memoria {MemoriaId}",
                                            photo.FileName, memoria.id);
                                        
                                        // En caso de error, asegurar que la foto tenga valores por defecto
                                        photo.Url = "";
                                        if (string.IsNullOrEmpty(photo.ContainerName))
                                        {
                                            photo.ContainerName = twinId.ToLowerInvariant();
                                        }
                                        if (string.IsNullOrEmpty(photo.Path))
                                        {
                                            photo.Path = "MiMemoria";
                                        }
                                    }
                                }

                                _logger.LogInformation("📸 Processed {PhotoCount} photos for memoria: {MemoriaTitle}", 
                                    memoria.Photos.Count, memoria.Titulo);
                            }

                            // Agrega la memoria a la lista  
                            memorias.Add(memoria);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error converting document to MiMemoria: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} memorias for Twin: {TwinId}", memorias.Count, twinId);
                return memorias;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving memorias for Twin: {TwinId}", twinId);
                throw; // Opcional: vuelve a lanzar la excepción si es necesario  
            }
        }




        /// <summary>
        /// Actualizar memoria existente
        /// </summary>
        public async Task<bool> UpdateMemoriaAsync(MiMemoria memoria)
        {
            try
            {
                _logger.LogInformation("📝 Updating memoria: {Id} for Twin: {TwinId}", 
                    memoria.id, memoria.TwinID);

                // Actualizar timestamp de modificación
                memoria.FechaActualizacion = DateTime.UtcNow;
                memoria.Version++;

                // Convertir a diccionario para Cosmos DB
                var memoriaDict = ConvertMemoriaToDict(memoria);
                
                await _memoriasContainer.UpsertItemAsync(memoriaDict, new PartitionKey(memoria.TwinID));

                _logger.LogInformation("✅ Memoria updated successfully: {Titulo}", memoria.Titulo);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update memoria: {Id} for Twin: {TwinId}", 
                    memoria.id, memoria.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Eliminar memoria
        /// </summary>
        public async Task<bool> DeleteMemoriaAsync(string memoriaId, string twinId)
        {
            try
            {
                _logger.LogInformation("🗑️ Deleting memoria: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);

                await _memoriasContainer.DeleteItemAsync<Dictionary<string, object?>>(
                    memoriaId,
                    new PartitionKey(twinId)
                );

                _logger.LogInformation("✅ Memoria deleted successfully: {MemoriaId}", memoriaId);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Memoria not found for deletion: {MemoriaId}", memoriaId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting memoria: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
                return false;
            }
        }

        /// <summary>
        /// Buscar memorias por término de búsqueda
        /// </summary>
        public async Task<List<MiMemoria>> SearchMemoriasAsync(string twinId, string searchTerm, int limit = 20)
        {
            try
            {
                _logger.LogInformation("🔍 Searching memorias for Twin: {TwinId}, term: '{SearchTerm}'", twinId, searchTerm);

                var sql = @"
                    SELECT * FROM c 
                    WHERE c.TwinId = @twinId 
                    AND (
                        CONTAINS(LOWER(c.Titulo), LOWER(@searchTerm))
                        OR CONTAINS(LOWER(c.Contenido), LOWER(@searchTerm))
                        OR CONTAINS(LOWER(c.Categoria), LOWER(@searchTerm))
                        OR CONTAINS(LOWER(c.Ubicacion), LOWER(@searchTerm))
                    )
                    ORDER BY c.FechaActualizacion DESC
                    OFFSET 0 LIMIT @limit";

                var query = new QueryDefinition(sql)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@searchTerm", searchTerm)
                    .WithParameter("@limit", limit);

                var iterator = _memoriasContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var memorias = new List<MiMemoria>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        try
                        {
                            var memoria = ConvertDictToMemoria(item);
                            memorias.Add(memoria);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error converting search result to MiMemoria: {Id}", 
                                item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("✅ Found {Count} memorias matching search term", memorias.Count);
                return memorias;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching memorias for Twin: {TwinId}", twinId);
                return new List<MiMemoria>();
            }
        }

        // ========================================
        // HELPER METHODS
        // ========================================

        /// <summary>
        /// Convertir MiMemoria a diccionario para Cosmos DB
        /// </summary>
        private Dictionary<string, object?> ConvertMemoriaToDict(MiMemoria memoria)
        {
            return new Dictionary<string, object?>
            {
                ["id"] = memoria.id,
                ["TwinID"] = memoria.TwinID,
                ["Titulo"] = memoria.Titulo,
                ["Contenido"] = memoria.Contenido,
                ["Categoria"] = memoria.Categoria,
                ["Tipo"] = memoria.Tipo,
                ["Importancia"] = memoria.Importancia,
                ["Fecha"] = memoria.Fecha,
                ["FechaCreacion"] = memoria.FechaCreacion.ToString("O"),
                ["FechaActualizacion"] = memoria.FechaActualizacion.ToString("O"),
                ["Ubicacion"] = memoria.Ubicacion,
                ["Personas"] = memoria.Personas,
                ["Etiquetas"] = memoria.Etiquetas,
                ["Multimedia"] = memoria.Multimedia,
                ["Version"] = memoria.Version,
                ["Photos"] = memoria.Photos // ✅ AGREGADO: Incluir las fotos
            };
        }

        /// <summary>
        /// Convertir diccionario de Cosmos DB a MiMemoria
        /// </summary>
        private MiMemoria ConvertDictToMemoria(Dictionary<string, object?> data)
        {
            T GetValue<T>(string key, T defaultValue = default!)
            {
                if (!data.TryGetValue(key, out var value) || value == null)
                    return defaultValue;

                try
                {
                    if (value is T directValue)
                        return directValue;

                    if (value is JsonElement jsonElement)
                    {
                        var type = typeof(T);
                        if (type == typeof(string))
                            return (T)(object)(jsonElement.GetString() ?? string.Empty);
                        if (type == typeof(int))
                            return (T)(object)jsonElement.GetInt32();
                        if (type == typeof(DateTime))
                        {
                            if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                                return (T)(object)dateTime;
                            return defaultValue;
                        }
                        if (type == typeof(List<string>))
                        {
                            var list = new List<string>();
                            if (jsonElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in jsonElement.EnumerateArray())
                                {
                                    list.Add(item.GetString() ?? string.Empty);
                                }
                            }
                            return (T)(object)list;
                        }
                    }

                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            };

            List<string> GetStringList(string key)
            {
                if (!data.TryGetValue(key, out var value) || value == null)
                    return new List<string>();

                if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                    return element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();
                
                if (value is IEnumerable<object> enumerable)
                    return enumerable.Select(item => item?.ToString() ?? string.Empty).ToList();

                return new List<string>();
            }

            return new MiMemoria
            {
                id = GetValue("id", string.Empty),
                TwinID = GetValue("TwinID", string.Empty), // ✅ MANTENIDO: TwinID sin cambiar
                Titulo = GetValue("Titulo", string.Empty),
                Contenido = GetValue("Contenido", string.Empty),
                Categoria = GetValue("Categoria", string.Empty),
                Tipo = GetValue("Tipo", string.Empty),
                Importancia = GetValue("Importancia", string.Empty),
                Fecha = GetValue("Fecha", string.Empty),
                FechaCreacion = GetValue("FechaCreacion", DateTime.UtcNow),
                FechaActualizacion = GetValue("FechaActualizacion", DateTime.UtcNow),
                Ubicacion = GetValue("Ubicacion", string.Empty),
                Personas = GetStringList("Personas"),
                Etiquetas = GetStringList("Etiquetas"),
                Multimedia = GetStringList("Multimedia"),
                Version = GetValue("Version", 1),
                Photos = new List<Photo>() // ✅ INICIALIZAR: Photos se llenará con JsonConvert.DeserializeObject
            };
        }
    }
     
public class MiMemoria
    {
        [JsonProperty("id")]
        public string id { get; set; }

        [JsonProperty("TwinID")]
        [Required]
        public string TwinID { get; set; }

        [JsonProperty("titulo")]
        [Required]
        [StringLength(200)]
        public string Titulo { get; set; }

        [JsonProperty("contenido")]
        [Required]
        public string Contenido { get; set; } // Rich text (HTML)  

        [JsonProperty("categoria")]
        [Required]
        public string Categoria { get; set; } // 'personal', 'trabajo', 'familia', 'aprendizaje', 'ideas', 'viajes', 'otros'  

        [JsonProperty("tipo")]
        [Required]
        public string Tipo { get; set; } // 'evento', 'nota', 'idea', 'logro', 'recordatorio'  

        [JsonProperty("importancia")]
        [Required]
        public string Importancia { get; set; } // 'alta', 'media', 'baja'  

        [JsonProperty("fecha")]
        [Required]
        public string Fecha { get; set; } // Fecha del evento (string)  

        [JsonProperty("fechaCreacion")]
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        [JsonProperty("fechaActualizacion")]
        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [JsonProperty("ubicacion")]
        public string Ubicacion { get; set; }

        [JsonProperty("personas")]
        public List<string> Personas { get; set; } = new List<string>();

        [JsonProperty("etiquetas")]
        public List<string> Etiquetas { get; set; } = new List<string>();

        [JsonProperty("multimedia")]
        public List<string> Multimedia { get; set; } = new List<string>();

        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("photos")]
        public List<Photo> Photos { get; set; } = new List<Photo>();
    }

    public class Photo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("ContainerName")]
        public string ContainerName { get; set; }

        [JsonProperty("FileName")]
        public string FileName { get; set; }

        [JsonProperty("Path")]
        public string Path { get; set; }

        [JsonProperty("Url")]
        public string Url { get; set; }

        [JsonProperty("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("UpdatedAt")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("Title")]
        public string Title { get; set; }

        [JsonProperty("Description")]
        public string Description { get; set; }

        [JsonProperty("ImageAI")]
        public ImageAI ImageAI { get; set; }
    }

    public class ImageAI
    {
        [JsonProperty("detailsHTML")]
        public string DetailsHTML { get; set; }

        [JsonProperty("descripcionGenerica")]
        public string DescripcionGenerica { get; set; }

        [JsonProperty("totalTokensDescripcionDetallada")]
        public int TotalTokensDescripcionDetallada { get; set; }

        [JsonProperty("descripcionDetallada")]
        public string DescripcionDetallada { get; set; }

        [JsonProperty("descripcion_visual_detallada")]
        public DescripcionVisualDetallada DescripcionVisualDetallada { get; set; }

        [JsonProperty("contexto_emocional")]
        public ContextoEmocional ContextoEmocional { get; set; }

        [JsonProperty("elementos_temporales")]
        public ElementosTemporales ElementosTemporales { get; set; }

        [JsonProperty("detalles_memorables")]
        public DetallesMemorables DetallesMemorables { get; set; }

        [JsonProperty("id")]
        public string? id { get; set; }

        [JsonProperty("TwinID")]
        public string? TwinID { get; set; }


        [JsonProperty("url")]
        public string? Url { get; set; }

        [JsonProperty("path")]
        public string? Path { get; set; }


        [JsonProperty("fileName")]
        public string? FileName { get; set; }


        [JsonProperty("fecha")]
        public string? Fecha { get; set; }


        [JsonProperty("hora")]
        public string? Hora { get; set; }


        [JsonProperty("category")]
        public string? Category { get; set; }


        [JsonProperty("eventType")]
        public string? EventType { get; set; }


        [JsonProperty("descripcionUsuario")]
        public string? DescripcionUsuario { get; set; }


        [JsonProperty("places")]
        public string? Places { get; set; }



        [JsonProperty("people")]
        public string? People { get; set; }


        [JsonProperty("etiquetas")]
        public string? Etiquetas { get; set; }
    }

    public class DescripcionVisualDetallada
    {
        [JsonProperty("personas")]
        public Personas Personas { get; set; }

        [JsonProperty("objetos")]
        public List<Objeto> Objetos { get; set; } = new List<Objeto>();

        [JsonProperty("escenario")]
        public Escenario Escenario { get; set; }

        [JsonProperty("colores")]
        public Colores Colores { get; set; }
    }

    public class Personas
    {
        [JsonProperty("cantidad")]
        public int Cantidad { get; set; }

        [JsonProperty("detalles")]
        public List<DetallePersona> Detalles { get; set; } = new List<DetallePersona>();
    }

    public class DetallePersona
    {
        [JsonProperty("rol")]
        public string Rol { get; set; }

        [JsonProperty("edad_aproximada")]
        public string EdadAproximada { get; set; }

        [JsonProperty("expresion")]
        public string Expresion { get; set; }

        [JsonProperty("vestimenta")]
        public string Vestimenta { get; set; }

        [JsonProperty("pose")]
        public string Pose { get; set; }
    }

    public class Objeto
    {
        [JsonProperty("tipo")]
        public string Tipo { get; set; }

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; }
    }

    public class Escenario
    {
        [JsonProperty("ubicacion")]
        public string Ubicacion { get; set; }

        [JsonProperty("tipo_de_lugar")]
        public string TipoDeLugar { get; set; }

        [JsonProperty("ambiente")]
        public string Ambiente { get; set; }
    }

    public class Colores
    {
        [JsonProperty("paleta_dominante")]
        public List<Color> PaletaDominante { get; set; } = new List<Color>();

        [JsonProperty("iluminacion")]
        public string Iluminacion { get; set; }

        [JsonProperty("atmosfera")]
        public string Atmosfera { get; set; }
    }

    public class Color
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; }

        [JsonProperty("hex")]
        public string Hex { get; set; }
    }

    // Nuevas clases para las secciones adicionales
    public class ContextoEmocional
    {
        [JsonProperty("estado_de_animo_percibido")]
        public string EstadoDeAnimoPercibido { get; set; }

        [JsonProperty("tipo_de_evento")]
        public string TipoDeEvento { get; set; }

        [JsonProperty("emociones_transmitidas_por_las_personas")]
        public List<string> EmocionesTrasmitidasPorLasPersonas { get; set; } = new List<string>();

        [JsonProperty("ambiente_general")]
        public string AmbienteGeneral { get; set; }
    }

    public class ElementosTemporales
    {
        [JsonProperty("epoca_aproximada")]
        public string EpocaAproximada { get; set; }

        [JsonProperty("estacion_del_ano")]
        public string EstacionDelAno { get; set; }

        [JsonProperty("momento_del_dia")]
        public string MomentoDelDia { get; set; }
    }

    public class DetallesMemorables
    {
        [JsonProperty("elementos_unicos_o_especiales")]
        public List<string> ElementosUnicosOEspeciales { get; set; } = new List<string>();

        [JsonProperty("objetos_con_valor_sentimental")]
        public List<ObjetoSentimental> ObjetosConValorSentimental { get; set; } = new List<ObjetoSentimental>();

        [JsonProperty("caracteristicas_que_hacen_esta_foto_memorable")]
        public List<string> CaracteristicasQueHacenEstaFotoMemorable { get; set; } = new List<string>();

        [JsonProperty("contexto_que_puede_ayudar_a_recordar_el_momento")]
        public string ContextoQuePuedeAyudarARecordarElMomento { get; set; }
    }

    public class ObjetoSentimental
    {
        [JsonProperty("objeto")]
        public string Objeto { get; set; }

        [JsonProperty("posible_valor")]
        public string PosibleValor { get; set; }
    }

}
