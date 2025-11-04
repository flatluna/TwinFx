using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwinFx.Functions;
using TwinFx.Models;
using System.Linq;
using Newtonsoft.Json;

namespace TwinFx.Services
{
    /// <summary>
    /// Service class for managing Family Photos ImageAI in Cosmos DB
    /// Container: TwinFamilyFotos, PartitionKey: TwinID
    /// ========================================================================
    /// 
    /// Proporciona operaciones para guardar análisis de ImageAI de fotos familiares:
    /// - Guardar análisis de ImageAI 
    /// - Obtener todas las fotos de un Twin
    /// - ID único y PartitionKey con TwinID
    /// - Timestamps automáticos
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class FamilyFotoscosmosDB
    {
        private readonly ILogger<FamilyFotoscosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _familyPhotosContainer;

        public FamilyFotoscosmosDB(ILogger<FamilyFotoscosmosDB> logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            
            // Handle potential null cosmosOptions
            CosmosDbSettings cosmosSettings;
            if (cosmosOptions?.Value == null)
            {
                _logger.LogError("❌ CosmosDbSettings not found in configuration. Creating from configuration values...");
                
                // Fallback: create from configuration directly
                cosmosSettings = new CosmosDbSettings
                {
                    Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? configuration["COSMOS_ENDPOINT"] ?? "",
                    Key = configuration["Values:COSMOS_KEY"] ?? configuration["COSMOS_KEY"] ?? "",
                    DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
                };
            }
            else
            {
                cosmosSettings = cosmosOptions.Value;
            }

            _logger.LogInformation("📸 Initializing Family Photos Cosmos DB Service");
            _logger.LogInformation($"   🔗 Endpoint: {cosmosSettings.Endpoint}");
            _logger.LogInformation($"   💾 Database: {cosmosSettings.DatabaseName}");
            _logger.LogInformation($"   📦 Container: TwinFamilyFotos");

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
                _familyPhotosContainer = _database.GetContainer("TwinFamilyFotos");
                
                _logger.LogInformation("✅ Family Photos Cosmos DB Service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Family Photos Cosmos DB client");
                throw;
            }
        }

        /// <summary>
        /// Guardar análisis de ImageAI en Cosmos DB
        /// </summary>
        public async Task<bool> SaveImageAIAsync(ImageAI imageAI)
        {
            try
            {
                _logger.LogInformation("📸 Saving ImageAI analysis for TwinID: {TwinID}", imageAI.TwinID);

                // Generar ID si no se proporciona
                if (string.IsNullOrEmpty(imageAI.id))
                {
                    imageAI.id = Guid.NewGuid().ToString();
                }

                // Asegurar que TwinID esté presente
                if (string.IsNullOrEmpty(imageAI.TwinID))
                {
                    throw new ArgumentException("TwinID is required for ImageAI");
                }

                // Convertir a diccionario para Cosmos DB
                var imageAIDict = ConvertImageAIToDict(imageAI);
                
                await _familyPhotosContainer.CreateItemAsync(imageAIDict, new PartitionKey(imageAI.TwinID));

                _logger.LogInformation("✅ ImageAI analysis saved successfully with ID: {Id}", imageAI.id);
                return true;
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Manejo específico para conflictos (por ejemplo, ID duplicado)
                _logger.LogWarning("⚠️ Conflict occurred while saving ImageAI analysis: {Message}", cosmosEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to save ImageAI analysis for TwinID: {TwinID}", imageAI.TwinID);
                return false;
            }
        }
      
        /// <summary>
        /// Obtener todas las fotos (ImageAI) de un Twin ordenadas por fecha de creación
        /// </summary>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>Lista de todas las fotos ImageAI del Twin</returns>
        public async Task<List<ImageAI>> GetAllPhotosByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("📸 Getting all family photos for Twin: {TwinId}", twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinID ORDER BY c.createdAt DESC")
                    .WithParameter("@twinID", twinId);

                var iterator = _familyPhotosContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var photos = new List<ImageAI>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();

                    foreach (var item in response)
                    {
                        try
                        {
                            // Convertir el item a JSON string
                            string jsonString = JsonConvert.SerializeObject(item);

                            // Deserializar el string a un objeto ImageAI
                            ImageAI photo = JsonConvert.DeserializeObject<ImageAI>(jsonString);

                            if (photo != null)
                            {
                                // Agregar la foto a la lista
                                photos.Add(photo);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error converting document to ImageAI: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} family photos for Twin: {TwinId}", photos.Count, twinId);
                return photos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving family photos for Twin: {TwinId}", twinId);
                return new List<ImageAI>();
            }
        }

        /// <summary>
        /// Obtener una foto específica por ID
        /// </summary>
        /// <param name="photoId">ID de la foto</param>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>ImageAI de la foto o null si no se encuentra</returns>
        public async Task<ImageAI?> GetPhotoByIdAsync(string photoId, string twinId)
        {
            try
            {
                _logger.LogInformation("📸 Getting family photo by ID: {PhotoId} for Twin: {TwinId}", photoId, twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @photoId AND c.TwinID = @twinID")
                    .WithParameter("@photoId", photoId)
                    .WithParameter("@twinID", twinId);

                var iterator = _familyPhotosContainer.GetItemQueryIterator<ImageAI>(query);

                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var photo = response.FirstOrDefault();

                    if (photo != null)
                    {
                        _logger.LogInformation("✅ Family photo retrieved successfully: {PhotoId}", photoId);
                        return photo;
                    }
                    else
                    {
                        _logger.LogInformation("📸 Family photo not found: {PhotoId} for Twin: {TwinId}", photoId, twinId);
                        return null;
                    }
                }
                else
                {
                    _logger.LogInformation("📸 No results found for PhotoId: {PhotoId} and TwinId: {TwinId}", photoId, twinId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting family photo by ID: {PhotoId} for Twin: {TwinId}", photoId, twinId);
                return null;
            }
        }

        /// <summary>
        /// Actualizar análisis de ImageAI existente en Cosmos DB
        /// </summary>
        /// <param name="imageAI">Objeto ImageAI con los datos actualizados</param>
        /// <returns>True si la actualización fue exitosa, False en caso contrario</returns>
        public async Task<bool> UpdateImageAIAsync(ImageAI imageAI)
        {
            try
            {
                _logger.LogInformation("📸 Updating ImageAI analysis for ID: {Id}, TwinID: {TwinID}", imageAI.id, imageAI.TwinID);

                // Validar que el ID esté presente
                if (string.IsNullOrEmpty(imageAI.id))
                {
                    _logger.LogError("❌ ImageAI ID is required for update operation");
                    throw new ArgumentException("ImageAI ID is required for update operation");
                }

                // Asegurar que TwinID esté presente
                if (string.IsNullOrEmpty(imageAI.TwinID))
                {
                    _logger.LogError("❌ TwinID is required for ImageAI update");
                    throw new ArgumentException("TwinID is required for ImageAI update");
                }

                // Verificar que el documento existe antes de actualizar
                var existingPhoto = await GetPhotoByIdAsync(imageAI.id, imageAI.TwinID);
                if (existingPhoto == null)
                {
                    _logger.LogWarning("⚠️ ImageAI with ID {Id} not found for TwinID {TwinID}", imageAI.id, imageAI.TwinID);
                    return false;
                }

                // Convertir a diccionario para Cosmos DB con timestamp de actualización
                var imageAIDict = ConvertImageAIToDict(imageAI);
                imageAIDict["updatedAt"] = DateTime.UtcNow.ToString("O"); // Actualizar timestamp

                // Usar ReplaceItemAsync para actualizar el documento existente
                await _familyPhotosContainer.ReplaceItemAsync(imageAIDict, imageAI.id, new PartitionKey(imageAI.TwinID));

                _logger.LogInformation("✅ ImageAI analysis updated successfully with ID: {Id}", imageAI.id);
                return true;
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Manejo específico para documento no encontrado
                _logger.LogWarning("⚠️ ImageAI with ID {Id} not found for update: {Message}", imageAI.id, cosmosEx.Message);
                return false;
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                // Manejo específico para condiciones de precondición fallidas (etag mismatch)
                _logger.LogWarning("⚠️ Precondition failed while updating ImageAI analysis: {Message}", cosmosEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update ImageAI analysis for ID: {Id}, TwinID: {TwinID}", imageAI.id, imageAI.TwinID);
                return false;
            }
        }

        // ========================================
        // HELPER METHODS
        // ========================================

        /// <summary>
        /// Convertir ImageAI a diccionario para Cosmos DB
        /// </summary>
        private Dictionary<string, object?> ConvertImageAIToDict(ImageAI imageAI)
        {
            return new Dictionary<string, object?>
            {
                ["id"] = imageAI.id,
                ["category"] = imageAI.Category,
                ["eventType"] = imageAI.EventType,
                ["TwinID"] = imageAI.TwinID,
                ["hora"] = imageAI.Hora,
                ["path"] = imageAI.Path,
                ["people"] = imageAI.People,
                ["places"] = imageAI.Places,
                ["etiquetas"] = imageAI.Etiquetas,
                ["fecha"] = imageAI.Fecha,
                ["detailsHTML"] = imageAI.DetailsHTML,
                ["descripcionGenerica"] = imageAI.DescripcionGenerica,
                ["descripcionUsuario"] = imageAI.DescripcionUsuario,
                ["descripcion_visual_detallada"] = imageAI.DescripcionVisualDetallada,
                ["contexto_emocional"] = imageAI.ContextoEmocional,
                ["elementos_temporales"] = imageAI.ElementosTemporales,
                ["detalles_memorables"] = imageAI.DetallesMemorables,
                ["url"] = imageAI.Url, // ✅ AGREGADO: URL de la imagen
                ["fileName"] = imageAI.FileName, // ✅ AGREGADO: Nombre del archivo
                ["createdAt"] = DateTime.UtcNow.ToString("O"),
                ["updatedAt"] = DateTime.UtcNow.ToString("O")
            };
        }
    }
}
