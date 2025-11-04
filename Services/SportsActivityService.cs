using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Models;
using System.Text.Json;
using TwinAgentsLibrary.Models;

namespace TwinFx.Services
{
    /// <summary>
    /// Service for managing sports activities in Cosmos DB TwinSports container.
    /// Container: TwinSports, PartitionKey: TwinID
    /// </summary>
    public class SportsActivityService
    {
        private readonly ILogger<SportsActivityService> _logger;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _sportsContainer;

        public SportsActivityService(ILogger<SportsActivityService> logger, IConfiguration configuration)
        {
            _logger = logger;

            // TRIPLE FALLBACK STRATEGY - GUARANTEED TO WORK
            var cosmosEndpoint = "";
            var key = "";
            var databaseName = "TwinHumanDB";

            _logger.LogInformation("?? SPORTS SERVICE - STARTING TRIPLE FALLBACK CONFIGURATION STRATEGY...");

            // STRATEGY 1: Standard Azure Functions configuration
            try
            {
                cosmosEndpoint = configuration?.GetValue<string>("Values:COSMOS_ENDPOINT") ?? 
                                configuration?.GetValue<string>("COSMOS_ENDPOINT") ?? "";
                key = configuration?.GetValue<string>("Values:COSMOS_KEY") ?? 
                     configuration?.GetValue<string>("COSMOS_KEY") ?? "";
                databaseName = configuration?.GetValue<string>("Values:COSMOS_DATABASE_NAME") ?? 
                              configuration?.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

                if (!string.IsNullOrEmpty(cosmosEndpoint) && !string.IsNullOrEmpty(key))
                {
                    _logger.LogInformation("? SPORTS - STRATEGY 1 SUCCESS: Configuration system loaded successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "?? SPORTS - STRATEGY 1 FAILED: Configuration system failed");
            }

            // STRATEGY 2: Direct file reading fallback
            if (string.IsNullOrEmpty(cosmosEndpoint) || string.IsNullOrEmpty(key))
            {
                _logger.LogInformation("?? SPORTS - STRATEGY 2: Attempting direct file reading...");
                try
                {
                    var possiblePaths = new[]
                    {
                        Path.Combine(Directory.GetCurrentDirectory(), "local.settings.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local.settings.json"),
                        @"C:\Users\twinadmin\source\repos\TwinFx\local.settings.json"
                    };

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            var json = File.ReadAllText(path);
                            var settings = JsonSerializer.Deserialize<JsonElement>(json);
                            
                            if (settings.TryGetProperty("Values", out var values))
                            {
                                if (string.IsNullOrEmpty(cosmosEndpoint) && values.TryGetProperty("COSMOS_ENDPOINT", out var endpoint))
                                {
                                    cosmosEndpoint = endpoint.GetString() ?? "";
                                }
                                
                                if (string.IsNullOrEmpty(key) && values.TryGetProperty("COSMOS_KEY", out var keyElement))
                                {
                                    key = keyElement.GetString() ?? "";
                                }
                            }
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(cosmosEndpoint) && !string.IsNullOrEmpty(key))
                    {
                        _logger.LogInformation("? SPORTS - STRATEGY 2 SUCCESS: File reading successful");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "?? SPORTS - STRATEGY 2 FAILED: File reading failed");
                }
            }

            // STRATEGY 3: HARDCODED EMERGENCY FALLBACK
            if (string.IsNullOrEmpty(cosmosEndpoint) || string.IsNullOrEmpty(key))
            {
                _logger.LogWarning("?? SPORTS - STRATEGY 3: Using emergency hardcoded configuration");
                cosmosEndpoint = "https://flatbitdb.documents.azure.com:443/";
                key = "Ct9Ql75OjsfX2aB9iZ3JS4HjkKiKCdUHc9CraZE5K4lUwBxnhkD9ESNn5YIthoD0jJxHYjXBZp6qCsuzaAGNiw==";
                databaseName = "TwinHumanDB";
            }

            // Extract account name from endpoint
            string accountName = "flatbitdb";
            if (!string.IsNullOrEmpty(cosmosEndpoint))
            {
                try
                {
                    var uri = new Uri(cosmosEndpoint);
                    accountName = uri.Host.Split('.')[0];
                }
                catch { }
            }

            _logger.LogInformation("?? SPORTS - FINAL COSMOS DB Configuration:");
            _logger.LogInformation("   • Account Name: {AccountName}", accountName);
            _logger.LogInformation("   • Database Name: {DatabaseName}", databaseName);
            _logger.LogInformation("   • Endpoint: {Endpoint}", cosmosEndpoint);
            _logger.LogInformation("   • Key Found: {KeyFound}", !string.IsNullOrEmpty(key));

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(cosmosEndpoint))
            {
                _logger.LogError("? SPORTS - ALL STRATEGIES FAILED");
                throw new InvalidOperationException("COSMOS configuration could not be found using any fallback strategy.");
            }

            _client = new CosmosClient(cosmosEndpoint, key);
            _database = _client.GetDatabase(databaseName);
            _sportsContainer = _database.GetContainer("TwinAllSports");

            _logger.LogInformation("? Sports Activity Service initialized successfully");
        }

        /// <summary>
        /// Create a new sports activity
        /// </summary>
        public async Task<bool> CreateSportsActivityAsync(SportsActivityData activityData)
        {
            try
            {
                // Generate ID BEFORE creating the dictionary and saving to Cosmos DB
                if (string.IsNullOrEmpty(activityData.id))
                {
                    activityData.id = Guid.NewGuid().ToString();
                }

                var activityDict = activityData.ToDict();
                await _sportsContainer.CreateItemAsync(activityDict );
                
                _logger.LogInformation("✅⚽ Sports activity created successfully: {TipoActividad} on {Fecha} for Twin: {TwinID}",
                    activityData.TipoActividad, activityData.Fecha, activityData.TwinID);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create sports activity: {TipoActividad} on {Fecha} for Twin: {TwinID}",
                    activityData.TipoActividad, activityData.Fecha, activityData.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Get all sports activities for a twin
        /// </summary>
        public async Task<List<SportsActivityData>> GetSportsActivitiesByTwinIdAsync(string twinId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.twinID = @twinId ORDER BY c.fecha DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = _sportsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var activities = new List<SportsActivityData>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        try
                        {
                            var activity = SportsActivityData.FromDict(item);
                            activities.Add(activity);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "?? Error converting document to SportsActivityData: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("????? Found {Count} sports activities for Twin ID: {TwinId}", activities.Count, twinId);
                return activities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to get sports activities for Twin ID: {TwinId}", twinId);
                return new List<SportsActivityData>();
            }
        }

        /// <summary>
        /// Get a specific sports activity by ID and TwinID
        /// </summary>
        public async Task<SportsActivityData?> GetSportsActivityByIdAsync(string activityId, string twinId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @activityId AND c.TwinID = @twinId")
                    .WithParameter("@activityId", activityId)
                    .WithParameter("@twinId", twinId);

                var iterator = _sportsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var activityDocument = response.FirstOrDefault();
                    
                    if (activityDocument != null)
                    {
                        var activity = SportsActivityData.FromDict(activityDocument);
                        _logger.LogInformation("????? Sports activity retrieved successfully: {TipoActividad} on {Fecha}", 
                            activity.TipoActividad, activity.Fecha);
                        return activity;
                    }
                }

                _logger.LogWarning("?? Sports activity not found: {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to get sports activity by ID {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return null;
            }
        }

        /// <summary>
        /// Update an existing sports activity
        /// </summary>
        public async Task<bool> UpdateSportsActivityAsync(SportsActivityData activityData)
        {
            try
            {
                // Update the fechaActualizacion
                activityData.FechaActualizacion = DateTime.UtcNow.ToString("O");

                var activityDict = activityData.ToDict();
                await _sportsContainer.UpsertItemAsync(activityDict );

                _logger.LogInformation("????? Sports activity updated successfully: {TipoActividad} on {Fecha} for Twin: {TwinID}",
                    activityData.TipoActividad, activityData.Fecha, activityData.TwinID);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to update sports activity: {Id} for Twin: {TwinID}",
                    activityData.id, activityData.TwinID);
                return false;
            }
        }

        /// <summary>
        /// Delete a sports activity
        /// </summary>
        public async Task<bool> DeleteSportsActivityAsync(string activityId, string twinId)
        {
            try
            {
                await _sportsContainer.DeleteItemAsync<Dictionary<string, object?>>(
                    activityId,
                    new PartitionKey(twinId)
                );

                _logger.LogInformation("????? Sports activity deleted successfully: {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to delete sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return false;
            }
        }

        /// <summary>
        /// Get sports activities within a date range
        /// </summary>
        public async Task<List<SportsActivityData>> GetSportsActivitiesByDateRangeAsync(string twinId, DateTime startDate, DateTime endDate)
        {
            try
            {
                var query = new QueryDefinition(@"
                    SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.fecha >= @startDate 
                    AND c.fecha <= @endDate 
                    ORDER BY c.fecha DESC")
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@startDate", startDate.ToString("yyyy-MM-dd"))
                    .WithParameter("@endDate", endDate.ToString("yyyy-MM-dd"));

                var iterator = _sportsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var activities = new List<SportsActivityData>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        try
                        {
                            var activity = SportsActivityData.FromDict(item);
                            activities.Add(activity);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "?? Error converting document to SportsActivityData: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("????? Found {Count} sports activities between {StartDate} and {EndDate} for Twin ID: {TwinId}", 
                    activities.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), twinId);
                return activities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to get sports activities by date range for Twin ID: {TwinId}", twinId);
                return new List<SportsActivityData>();
            }
        }

        /// <summary>
        /// Get comprehensive statistics for sports activities
        /// </summary>
        public async Task<SportsActivityStats> GetSportsActivityStatsAsync(string twinId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var whereClause = "c.TwinID = @twinId";
                var queryDef = new QueryDefinition($"SELECT * FROM c WHERE {whereClause}")
                    .WithParameter("@twinId", twinId);

                // Add date range filter if provided
                if (startDate.HasValue && endDate.HasValue)
                {
                    whereClause += " AND c.fecha >= @startDate AND c.fecha <= @endDate";
                    queryDef = new QueryDefinition($"SELECT * FROM c WHERE {whereClause}")
                        .WithParameter("@twinId", twinId)
                        .WithParameter("@startDate", startDate.Value.ToString("yyyy-MM-dd"))
                        .WithParameter("@endDate", endDate.Value.ToString("yyyy-MM-dd"));
                }

                var iterator = _sportsContainer.GetItemQueryIterator<Dictionary<string, object?>>(queryDef);
                var stats = new SportsActivityStats();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        try
                        {
                            var activity = SportsActivityData.FromDict(item);
                            
                            // Update statistics
                            stats.TotalActividades++;
                            stats.TotalMinutos += activity.DuracionMinutos;
                            
                            if (activity.Calorias.HasValue)
                            {
                                stats.TotalCalorias += activity.Calorias.Value;
                            }
                            
                            if (activity.Pasos.HasValue)
                            {
                                stats.TotalPasos += activity.Pasos.Value;
                            }
                            
                            if (activity.DistanciaKm.HasValue)
                            {
                                stats.TotalDistanciaKm += activity.DistanciaKm.Value;
                            }

                            // Track activity types
                            if (stats.ActividadesPorTipo.ContainsKey(activity.TipoActividad))
                            {
                                stats.ActividadesPorTipo[activity.TipoActividad]++;
                            }
                            else
                            {
                                stats.ActividadesPorTipo[activity.TipoActividad] = 1;
                            }

                            // Track most recent activity
                            if (DateTime.TryParse(activity.Fecha, out var activityDate))
                            {
                                if (stats.UltimaActividad == null || activityDate > stats.UltimaActividad)
                                {
                                    stats.UltimaActividad = activityDate;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "?? Error processing sports activity for stats: {Id}", item.GetValueOrDefault("id"));
                        }
                    }
                }

                // Calculate averages
                if (stats.TotalActividades > 0)
                {
                    stats.PromedioMinutosPorActividad = (double)stats.TotalMinutos / stats.TotalActividades;
                    stats.PromedioCaloriasPorActividad = (double)stats.TotalCalorias / stats.TotalActividades;
                }

                _logger.LogInformation("?? Sports activity stats calculated for Twin: {TwinId} - Total Activities: {Total}", 
                    twinId, stats.TotalActividades);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to get sports activity stats for Twin: {TwinId}", twinId);
                return new SportsActivityStats();
            }
        }
    }
}