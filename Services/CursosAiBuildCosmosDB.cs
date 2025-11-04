using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using TwinFx.Models;
using TwinFx.Agents;
using System.Collections.Generic;
using System.Linq;

namespace TwinFx.Services
{
    /// <summary>
    /// Simple Cosmos DB service to save CursoBuildData into container TwinCursosAIBuild
    /// </summary>
    public class CursosAiBuildCosmosDB
    {
        private readonly ILogger<CursosAiBuildCosmosDB> _logger;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _container;

        public CursosAiBuildCosmosDB(ILogger<CursosAiBuildCosmosDB> logger, IOptions<CosmosDbSettings> cosmosOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var cosmosSettings = cosmosOptions?.Value ?? throw new ArgumentNullException(nameof(cosmosOptions));

            _logger.LogInformation("Initializing CursosAiBuildCosmosDB");

            if (string.IsNullOrEmpty(cosmosSettings.Key))
            {
                _logger.LogError("COSMOS_KEY is required but not found in configuration");
                throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration");
            }

            if (string.IsNullOrEmpty(cosmosSettings.Endpoint))
            {
                _logger.LogError("COSMOS_ENDPOINT is required but not found in configuration");
                throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration");
            }

            _client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
            _database = _client.GetDatabase(cosmosSettings.DatabaseName);
            _container = _database.GetContainer("TwinCursosAIBuild");

            _logger.LogInformation("CursosAiBuildCosmosDB initialized for container TwinCursosAIBuild");
        }

        /// <summary>
        /// Saves the CursoBuildData into Cosmos DB. Returns the generated or existing id, or null on failure.
        /// TwinID from the buildData is used as the partition key.
        /// </summary>
        public async Task<string?> SaveCursoBuildAsync(CursoCreadoAI buildData)
        {
            try
            {
                if (buildData == null)
                {
                    _logger.LogError("Build data is null");
                    return null;
                }

                if (string.IsNullOrEmpty(buildData.TwinID))
                {
                    _logger.LogError("TwinID is required on buildData");
                    return null;
                }

                if (string.IsNullOrEmpty(buildData.id))
                {
                    buildData.id = Guid.NewGuid().ToString();
                    _logger.LogInformation("Generated new build id: {Id}", buildData.id);
                }

                await _container.CreateItemAsync(buildData, new PartitionKey(buildData.TwinID));

                _logger.LogInformation("Saved CursoBuildData with id {Id} for Twin {TwinId}", buildData.id, buildData.TwinID);
                return buildData.id;
            }
            catch (CosmosException cex)
            {
                _logger.LogError(cex, "Cosmos error saving CursoBuildData for Twin: {TwinId}", buildData?.TwinID);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CursoBuildData for Twin: {TwinId}", buildData?.TwinID);
                return null;
            }
        }

        /// <summary>
        /// Retrieves all CursoCreadoAI documents for a given TwinID from the TwinCursosAIBuild container.
        /// </summary>
        public async Task<List<CursoCreadoAI>> GetCursosByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("Getting CursoCreadoAI records for TwinID: {TwinId}", twinId);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.id DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = _container.GetItemQueryIterator<CursoCreadoAI>(query);
                var results = new List<CursoCreadoAI>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.Resource);
                }

                _logger.LogInformation("Retrieved {Count} CursoCreadoAI records for TwinID: {TwinId}", results.Count, twinId);
                return results;
            }
            catch (CosmosException cex)
            {
                _logger.LogError(cex, "Cosmos error retrieving CursoCreadoAI for TwinID: {TwinId}", twinId);
                return new List<CursoCreadoAI>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving CursoCreadoAI for TwinID: {TwinId}", twinId);
                return new List<CursoCreadoAI>();
            }
        }

        /// <summary>
        /// Retrieves a specific CursoCreadoAI document for a given TwinID and CursoID from the TwinCursosAIBuild container.
        /// </summary>
        public async Task<CursoCreadoAI?> GetCursosByTwinIdAndIDAsync(string twinId, string CursoID)
        {
            try
            {
                _logger.LogInformation("Getting specific CursoCreadoAI record for TwinID: {TwinId} and CursoID: {CursoID}", twinId, CursoID);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.id = @cursoId")
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@cursoId", CursoID);

                var iterator = _container.GetItemQueryIterator<CursoCreadoAI>(query);
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    if (response.Resource.Any())
                    {
                        var curso = response.Resource.First();
                        _logger.LogInformation("Retrieved CursoCreadoAI record for TwinID: {TwinId} and CursoID: {CursoID}", twinId, CursoID);
                        return curso;
                    }
                }

                _logger.LogInformation("No CursoCreadoAI record found for TwinID: {TwinId} and CursoID: {CursoID}", twinId, CursoID);
                return null;
            }
            catch (CosmosException cex)
            {
                _logger.LogError(cex, "Cosmos error retrieving CursoCreadoAI for TwinID: {TwinId} and CursoID: {CursoID}", twinId, CursoID);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving CursoCreadoAI for TwinID: {TwinId} and CursoID: {CursoID}", twinId, CursoID);
                return null;
            }
        }
    }
}
