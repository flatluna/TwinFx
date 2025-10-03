using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using TwinFx.Models;
using TwinFx.Agents;

namespace TwinFx.Services
{
    /// <summary>
    /// Service class for managing Skills in Cosmos DB
    /// Container: TwinContainer, PartitionKey: TwinID
    /// ========================================================================
    /// 
    /// Proporciona operaciones para habilidades con:
    /// - Gestión de habilidades con SkillPostRequest class
    /// - ID único y PartitionKey con TwinID
    /// - Timestamps automáticos
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class SkillsCosmosDB
    {
        private readonly ILogger<SkillsCosmosDB> _logger;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _skillsContainer;

        public SkillsCosmosDB(ILogger<SkillsCosmosDB> logger, IOptions<CosmosDbSettings> cosmosOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var cosmosSettings = cosmosOptions?.Value ?? throw new ArgumentNullException(nameof(cosmosOptions));

            _logger.LogInformation("🎯 Initializing Skills Cosmos DB Service");
            _logger.LogInformation($"   • Endpoint: {cosmosSettings.Endpoint}");
            _logger.LogInformation($"   • Database: {cosmosSettings.DatabaseName}");
            _logger.LogInformation($"   • Container: TwinContainer");

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
                _skillsContainer = _database.GetContainer("TwinSkills");
                
                _logger.LogInformation("✅ Skills Cosmos DB Service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Skills Cosmos DB client");
                throw;
            }
        }

        /// <summary>
        /// Guardar una habilidad en Cosmos DB
        /// </summary>
        /// <param name="skillRequest">Los datos de la habilidad a guardar</param>
        /// <returns>ID del documento guardado o null si falló</returns>
        public async Task<string?> SaveSkillAsync(SkillPostRequest skillRequest)
        {
            try
            {
                if (skillRequest == null)
                {
                    _logger.LogError("❌ SkillPostRequest is null");
                    return null;
                }

                if (string.IsNullOrEmpty(skillRequest.TwinID))
                {
                    _logger.LogError("❌ TwinID is required in SkillPostRequest");
                    return null;
                }

                // Generar ID si no existe
                if (string.IsNullOrEmpty(skillRequest.id))
                {
                    skillRequest.id = Guid.NewGuid().ToString();
                    _logger.LogInformation("🆔 Generated new skill ID: {Id}", skillRequest.id);
                }

                // Establecer timestamps si no están configurados
                var currentDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
                if (string.IsNullOrEmpty(skillRequest.DateAdded))
                {
                    skillRequest.DateAdded = currentDate;
                }
                skillRequest.LastUpdated = currentDate;

                _logger.LogInformation("🎯 Saving skill for Twin ID: {TwinId}", skillRequest.TwinID);
                _logger.LogInformation("🎯 Skill Name: {SkillName}, Category: {Category}", 
                    skillRequest.Name, skillRequest.Category);

                // Crear el documento en Cosmos DB usando TwinID como partition key
                var response = await _skillsContainer.CreateItemAsync(
                    skillRequest, 
                    new PartitionKey(skillRequest.TwinID)
                );

                _logger.LogInformation("✅ Skill saved successfully with ID: {Id}, RU consumed: {RequestCharge}", 
                    skillRequest.id, response.RequestCharge);

                return skillRequest.id;
            }
            catch (CosmosException cosmosEx)
            {
                _logger.LogError(cosmosEx, "❌ Cosmos DB error saving skill for Twin ID: {TwinId}. Status: {Status}, Message: {Message}", 
                    skillRequest?.TwinID, cosmosEx.StatusCode, cosmosEx.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error saving skill for Twin ID: {TwinId}", 
                    skillRequest?.TwinID);
                return null;
            }
        }

        /// <summary>
        /// Actualizar una habilidad existente en Cosmos DB
        /// </summary>
        /// <param name="skillRequest">Los datos de la habilidad a actualizar</param>
        /// <returns>ID del documento actualizado o null si falló</returns>
        public async Task<string?> UpdateSkillAsync(SkillPostRequest skillRequest)
        {
            try
            {
                if (skillRequest == null)
                {
                    _logger.LogError("❌ SkillPostRequest is null");
                    return null;
                }

                if (string.IsNullOrEmpty(skillRequest.id))
                {
                    _logger.LogError("❌ Skill ID is required for update");
                    return null;
                }

                if (string.IsNullOrEmpty(skillRequest.TwinID))
                {
                    _logger.LogError("❌ TwinID is required in SkillPostRequest");
                    return null;
                }

                // Actualizar timestamp
                skillRequest.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd");

                _logger.LogInformation("🔄 Updating skill with ID: {Id} for Twin ID: {TwinId}", 
                    skillRequest.id, skillRequest.TwinID);

                // Actualizar el documento en Cosmos DB
                var response = await _skillsContainer.ReplaceItemAsync(
                    skillRequest,
                    skillRequest.id,
                    new PartitionKey(skillRequest.TwinID)
                );

                _logger.LogInformation("✅ Skill updated successfully with ID: {Id}, RU consumed: {RequestCharge}", 
                    skillRequest.id, response.RequestCharge);

                return skillRequest.id;
            }
            catch (CosmosException cosmosEx)
            {
                _logger.LogError(cosmosEx, "❌ Cosmos DB error updating skill with ID: {Id} for Twin ID: {TwinId}. Status: {Status}, Message: {Message}", 
                    skillRequest?.id, skillRequest?.TwinID, cosmosEx.StatusCode, cosmosEx.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error updating skill with ID: {Id} for Twin ID: {TwinId}", 
                    skillRequest?.id, skillRequest?.TwinID);
                return null;
            }
        }

        /// <summary>
        /// Obtener todas las habilidades de un Twin por TwinID
        /// </summary>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>Lista de habilidades del Twin</returns>
        public async Task<List<SkillPostRequest>> GetSkillsByTwinIdAsync(string twinId)
        {
            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return new List<SkillPostRequest>();
                }

                _logger.LogInformation("🔍 Getting all skills for Twin ID: {TwinId}", twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.LastUpdated DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = _skillsContainer.GetItemQueryIterator<SkillPostRequest>(query);
                var skills = new List<SkillPostRequest>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    skills.AddRange(response);
                    _logger.LogDebug("📄 Retrieved batch of {Count} skills, RU consumed: {RequestCharge}", 
                        response.Count, response.RequestCharge);
                }

                 foreach (var skill in skills)
                {
                    if(skill.WhatLearned == null)
                    {
                        skill.WhatLearned = new List<NewLearning>();
                    }
                }
                    _logger.LogInformation("✅ Retrieved {Count} skills for Twin ID: {TwinId}", skills.Count, twinId);
                return skills;
            }
            catch (CosmosException cosmosEx)
            {
                _logger.LogError(cosmosEx, "❌ Cosmos DB error getting skills for Twin ID: {TwinId}. Status: {Status}, Message: {Message}", 
                    twinId, cosmosEx.StatusCode, cosmosEx.Message);
                return new List<SkillPostRequest>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error getting skills for Twin ID: {TwinId}", twinId);
                return new List<SkillPostRequest>();
            }
        }

        /// <summary>
        /// Obtener una habilidad específica por TwinID e ID de habilidad
        /// </summary>
        /// <param name="twinId">ID del Twin</param>
        /// <param name="skillId">ID de la habilidad</param>
        /// <returns>Habilidad específica o null si no se encuentra</returns>
        public async Task<SkillPostRequest?> GetSkillByTwinIdAndIdAsync(string twinId, string skillId)
        {
            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return null;
                }

                if (string.IsNullOrEmpty(skillId))
                {
                    _logger.LogError("❌ Skill ID is required");
                    return null;
                }

                _logger.LogInformation("🔍 Getting skill by ID: {SkillId} for Twin ID: {TwinId}", skillId, twinId);

                var response = await _skillsContainer.ReadItemAsync<SkillPostRequest>(skillId, new PartitionKey(twinId));
                
                _logger.LogInformation("✅ Skill retrieved successfully: {SkillId}, RU consumed: {RequestCharge}", 
                    skillId, response.RequestCharge);
                
                return response.Resource;
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("⚠️ Skill not found: {SkillId} for Twin ID: {TwinId}", skillId, twinId);
                return null;
            }
            catch (CosmosException cosmosEx)
            {
                _logger.LogError(cosmosEx, "❌ Cosmos DB error getting skill with ID: {SkillId} for Twin ID: {TwinId}. Status: {Status}, Message: {Message}", 
                    skillId, twinId, cosmosEx.StatusCode, cosmosEx.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error getting skill with ID: {SkillId} for Twin ID: {TwinId}", 
                    skillId, twinId);
                return null;
            }
        }
    }
}
