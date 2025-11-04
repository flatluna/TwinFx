using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwinFx.Models;
using TwinFx.Services;
using Newtonsoft.Json;
using System.Linq;

namespace TwinFx.Services
{
    /// <summary>
    /// Service class for managing Photos Analysis in Cosmos DB
    /// Container: TwinPhotos, PartitionKey: TwinID
    /// ========================================================================
    /// 
    /// Proporciona operaciones para guardar análisis de AnalisisResidencial de fotos:
    /// - Guardar análisis de AnalisisResidencial 
    /// - Obtener todas las fotos de un Twin
    /// - ID único y PartitionKey con TwinID
    /// - Timestamps automáticos
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class PhotosCosmosDB
    {
        private readonly ILogger<PhotosCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _photosContainer;

        public PhotosCosmosDB(ILogger<PhotosCosmosDB> logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration)
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

            _logger.LogInformation("📸 Initializing Photos Cosmos DB Service");
            _logger.LogInformation($"   🔗 Endpoint: {cosmosSettings.Endpoint}");
            _logger.LogInformation($"   💾 Database: {cosmosSettings.DatabaseName}");
            _logger.LogInformation($"   📦 Container: TwinPhotos");

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
                _photosContainer = _database.GetContainer("TwinPhotos");
                
                _logger.LogInformation("✅ Photos Cosmos DB Service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Photos Cosmos DB client");
                throw;
            }
        }

        /// <summary>
        /// Guardar análisis de AnalisisResidencial en Cosmos DB
        /// </summary>
        public async Task<bool> SaveAnalisisResidencialAsync(AnalisisResidencial analisisResidencial)
        {
            try
            {
                _logger.LogInformation("🏠 Saving AnalisisResidencial for TwinID: {TwinID}", analisisResidencial.TwinID);

                // Generar ID si no se proporciona
                if (string.IsNullOrEmpty(analisisResidencial.id))
                {
                    analisisResidencial.id = Guid.NewGuid().ToString();
                }

                // Asegurar que TwinID esté presente
                if (string.IsNullOrEmpty(analisisResidencial.TwinID))
                {
                    throw new ArgumentException("TwinID is required for AnalisisResidencial");
                }

                // Convertir a diccionario para Cosmos DB
                var analisisDict = ConvertAnalisisResidencialToDict(analisisResidencial);
                
                await _photosContainer.CreateItemAsync(analisisDict, new PartitionKey(analisisResidencial.TwinID));

                _logger.LogInformation("✅ AnalisisResidencial saved successfully with ID: {Id}", analisisResidencial.id);
                return true;
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Manejo específico para conflictos (por ejemplo, ID duplicado)
                _logger.LogWarning("⚠️ Conflict occurred while saving AnalisisResidencial: {Message}", cosmosEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to save AnalisisResidencial for TwinID: {TwinID}", analisisResidencial.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Obtener todos los análisis de un Twin ordenados por fecha de creación
        /// </summary>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>Lista de todos los análisis AnalisisResidencial del Twin</returns>
        public async Task<List<AnalisisResidencial>> GetAllAnalisisByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("🏠 Getting all residential analysis for Twin: {TwinId}", twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinID ORDER BY c.createdAt DESC")
                    .WithParameter("@twinID", twinId);

                var iterator = _photosContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var analisis = new List<AnalisisResidencial>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();

                    foreach (var item in response)
                    {
                        try
                        {
                            // Convertir el item a JSON string
                            string jsonString = JsonConvert.SerializeObject(item);

                            // Deserializar el string a un objeto AnalisisResidencial
                            AnalisisResidencial analisisItem = JsonConvert.DeserializeObject<AnalisisResidencial>(jsonString);

                            if (analisisItem != null)
                            {
                                // Agregar el análisis a la lista
                                analisis.Add(analisisItem);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error converting document to AnalisisResidencial: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} residential analysis for Twin: {TwinId}", analisis.Count, twinId);
                return analisis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving residential analysis for Twin: {TwinId}", twinId);
                return new List<AnalisisResidencial>();
            }
        }

        /// <summary>
        /// Obtener un análisis específico por ID
        /// </summary>
        /// <param name="analisisId">ID del análisis</param>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>AnalisisResidencial del análisis o null si no se encuentra</returns>
        public async Task<AnalisisResidencial?> GetAnalisisByIdAsync(string analisisId, string twinId)
        {
            try
            {
                _logger.LogInformation("🏠 Getting residential analysis by ID: {AnalisisId} for Twin: {TwinId}", analisisId, twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @analisisId AND c.TwinID = @twinID")
                    .WithParameter("@analisisId", analisisId)
                    .WithParameter("@twinID", twinId);

                var iterator = _photosContainer.GetItemQueryIterator<AnalisisResidencial>(query);

                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var analisis = response.FirstOrDefault();

                    if (analisis != null)
                    {
                        _logger.LogInformation("✅ Residential analysis retrieved successfully: {AnalisisId}", analisisId);
                        return analisis;
                    }
                    else
                    {
                        _logger.LogInformation("🏠 Residential analysis not found: {AnalisisId} for Twin: {TwinId}", analisisId, twinId);
                        return null;
                    }
                }
                else
                {
                    _logger.LogInformation("🏠 No results found for AnalisisId: {AnalisisId} and TwinId: {TwinId}", analisisId, twinId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting residential analysis by ID: {AnalisisId} for Twin: {TwinId}", analisisId, twinId);
                return null;
            }
        }

        /// <summary>
        /// Obtener análisis residenciales de un Twin filtrados por path específico
        /// </summary>
        /// <param name="twinId">ID del Twin</param>
        /// <param name="filePath">Path específico para filtrar (ej: "homes/88e1b605-657a-4210-aa24-c417437b607e/photos/Exterior")</param>
        /// <returns>Lista de análisis AnalisisResidencial del Twin filtrados por path</returns>
        public async Task<List<AnalisisResidencial>> GetAnalisisByTwinIdAndPathAsync(string twinId, string filePath)
        {
            try
            {
                _logger.LogInformation("🏠🔍 Getting residential analysis for Twin: {TwinId} and Path: {FilePath}", twinId, filePath);

                // Usar STARTSWITH para encontrar análisis que comiencen con el path especificado
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.TwinID = @twinID AND STARTSWITH(c.filePath, @filePath) ORDER BY c.createdAt DESC")
                    .WithParameter("@twinID", twinId)
                    .WithParameter("@filePath", filePath);

                var iterator = _photosContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var analisis = new List<AnalisisResidencial>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();

                    foreach (var item in response)
                    {
                        try
                        {
                            // Convertir el item a JSON string
                            string jsonString = JsonConvert.SerializeObject(item);

                            // Deserializar el string a un objeto AnalisisResidencial
                            AnalisisResidencial analisisItem = JsonConvert.DeserializeObject<AnalisisResidencial>(jsonString);

                            if (analisisItem != null)
                            {
                                // Agregar el análisis a la lista
                                analisis.Add(analisisItem);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error converting document to AnalisisResidencial: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} residential analysis for Twin: {TwinId} and Path: {FilePath}", 
                    analisis.Count, twinId, filePath);
                return analisis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving residential analysis for Twin: {TwinId} and Path: {FilePath}", 
                    twinId, filePath);
                return new List<AnalisisResidencial>();
            }
        }

        // ========================================
        // HELPER METHODS
        // ========================================

        /// <summary>
        /// Convertir AnalisisResidencial a diccionario para Cosmos DB
        /// </summary>
        private Dictionary<string, object?> ConvertAnalisisResidencialToDict(AnalisisResidencial analisis)
        {
            return new Dictionary<string, object?>
            {
                ["id"] = analisis.id,
                ["TwinID"] = analisis.TwinID,
                ["fileName"] = analisis.FileName,
                ["filePath"] = analisis.FilePath,
                ["fileURL"] = analisis.FileURL,
                ["descripcionGenerica"] = analisis.DescripcionGenerica,
                ["detailsHTML"] = analisis.DetailsHtml,
                ["analisis_arquitectonico"] = analisis.AnalisisArquitectonico,
                ["elementos_decorativos"] = analisis.ElementosDecorativos,
                ["analisis_espacial"] = analisis.AnalisisEspacial,
                ["caracteristicas_tecnicas"] = analisis.CaracteristicasTecnicas,
                ["evaluacion_general"] = analisis.EvaluacionGeneral,
                ["createdAt"] = DateTime.UtcNow.ToString("O"),
                ["updatedAt"] = DateTime.UtcNow.ToString("O")
            };
        }
    }
}
