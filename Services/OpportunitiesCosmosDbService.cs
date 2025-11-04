using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Service class for managing Job Opportunities in Cosmos DB.
/// Handles CRUD operations for job opportunity data and application tracking.
/// Container: TwinJobOpportunities, PartitionKey: TwinID
/// </summary>
public class OpportunitiesCosmosDbService
{
    private readonly ILogger<OpportunitiesCosmosDbService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly IConfiguration _configuration;

    public OpportunitiesCosmosDbService(ILogger<OpportunitiesCosmosDbService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        var accountName = configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ??
                         configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";

        var databaseName = configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ??
                          configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

        var endpoint = $"https://{accountName}.documents.azure.com:443/";
        var key = configuration.GetValue<string>("Values:COSMOS_KEY") ??
                 configuration.GetValue<string>("COSMOS_KEY");

        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
        }

        _client = new CosmosClient(endpoint, key);
        _database = _client.GetDatabase(databaseName);

        _logger.LogInformation("? Opportunities Cosmos Service initialized successfully");
    }

    /// <summary>
    /// Create a new job opportunity in the TwinJobOpportunities container
    /// </summary>
    /// <param name="jobOpportunity">Job opportunity to create</param>
    /// <returns>True if created successfully</returns>
    public async Task<bool> CreateJobOpportunityAsync(JobOpportunityData jobOpportunity)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var jobDict = jobOpportunity.ToDict();
            await jobContainer.CreateItemAsync(jobDict, new PartitionKey(jobOpportunity.TwinID));

            _logger.LogInformation("?? Job opportunity created successfully: {Puesto} at {Empresa} for Twin: {TwinID}",
                jobOpportunity.Puesto, jobOpportunity.Empresa, jobOpportunity.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to create job opportunity: {Puesto} at {Empresa} for Twin: {TwinID}",
                jobOpportunity.Puesto, jobOpportunity.Empresa, jobOpportunity.TwinID);
            return false;
        }
    }

    /// <summary>
    /// Get all job opportunities for a specific Twin ID with optional filtering
    /// </summary>
    /// <param name="twinId">Twin ID to get opportunities for</param>
    /// <param name="query">Query parameters for filtering and pagination</param>
    /// <returns>List of job opportunities</returns>
    public async Task<List<JobOpportunityData>> GetJobOpportunitiesByTwinIdAsync(string twinId, JobOpportunityQuery query)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            // Build dynamic SQL query based on filters
            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (query.Estado.HasValue)
            {
                conditions.Add("c.estado = @estado");
                parameters["@estado"] = query.Estado.Value.ToString().ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(query.Empresa))
            {
                conditions.Add("CONTAINS(LOWER(c.empresa), LOWER(@empresa))");
                parameters["@empresa"] = query.Empresa;
            }

            if (!string.IsNullOrEmpty(query.Puesto))
            {
                conditions.Add("CONTAINS(LOWER(c.puesto), LOWER(@puesto))");
                parameters["@puesto"] = query.Puesto;
            }

            if (!string.IsNullOrEmpty(query.Ubicacion))
            {
                conditions.Add("CONTAINS(LOWER(c.ubicacion), LOWER(@ubicacion))");
                parameters["@ubicacion"] = query.Ubicacion;
            }

            if (query.FechaDesde.HasValue)
            {
                conditions.Add("c.fechaAplicacion >= @fechaDesde");
                parameters["@fechaDesde"] = query.FechaDesde.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            if (query.FechaHasta.HasValue)
            {
                conditions.Add("c.fechaAplicacion <= @fechaHasta");
                parameters["@fechaHasta"] = query.FechaHasta.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            // Build ORDER BY clause
            var orderBy = query.SortBy?.ToLowerInvariant() switch
            {
                "empresa" => "c.empresa",
                "puesto" => "c.puesto",
                "fechaaplicacion" => "c.fechaAplicacion",
                "fechacreacion" => "c.fechaCreacion",
                _ => "c.fechaCreacion"
            };

            var orderDirection = query.SortDirection?.ToUpperInvariant() == "ASC" ? "ASC" : "DESC";

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY {orderBy} {orderDirection}";

            // Add pagination if specified
            if (query.Page > 1 || query.PageSize < 1000)
            {
                var offset = (query.Page - 1) * query.PageSize;
                sql += $" OFFSET {offset} LIMIT {query.PageSize}";
            }

            var cosmosQuery = new QueryDefinition(sql);
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var jobOpportunities = new List<JobOpportunityData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var jobOpportunity = JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} job opportunities for Twin ID: {TwinId}", jobOpportunities.Count, twinId);
            return jobOpportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get job opportunities for Twin ID: {TwinId}", twinId);
            return new List<JobOpportunityData>();
        }
    }

    /// <summary>
    /// Get a specific job opportunity by ID for a Twin
    /// </summary>
    /// <param name="jobId">Job opportunity ID to retrieve</param>
    /// <param name="twinId">Twin ID (used as partition key)</param>
    /// <returns>Job opportunity if found, null otherwise</returns>
    public async Task<JobOpportunityData?> GetJobOpportunityByIdAsync(string jobId, string twinId)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var response = await jobContainer.ReadItemAsync<Dictionary<string, object?>>(
                jobId,
                new PartitionKey(twinId)
            );

            var jobOpportunity = JobOpportunityData.FromDict(response.Resource);
            _logger.LogInformation("?? Job opportunity retrieved successfully: {Puesto} at {Empresa}",
                jobOpportunity.Puesto, jobOpportunity.Empresa);
            return jobOpportunity;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("?? Job opportunity not found: {JobId} for Twin: {TwinId}", jobId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get job opportunity by ID {JobId} for Twin: {TwinId}", jobId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Update an existing job opportunity
    /// </summary>
    /// <param name="jobOpportunity">Updated job opportunity data</param>
    /// <returns>True if updated successfully</returns>
    public async Task<bool> UpdateJobOpportunityAsync(JobOpportunityData jobOpportunity)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var jobDict = jobOpportunity.ToDict();
            jobDict["fechaActualizacion"] = DateTime.UtcNow.ToString("O");

            await jobContainer.UpsertItemAsync(jobDict, new PartitionKey(jobOpportunity.TwinID));

            _logger.LogInformation("?? Job opportunity updated successfully: {Puesto} at {Empresa} for Twin: {TwinID}",
                jobOpportunity.Puesto, jobOpportunity.Empresa, jobOpportunity.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update job opportunity: {Id} for Twin: {TwinID}",
                jobOpportunity.Id, jobOpportunity.TwinID);
            return false;
        }
    }

    /// <summary>
    /// Delete a job opportunity
    /// </summary>
    /// <param name="jobId">Job opportunity ID to delete</param>
    /// <param name="twinId">Twin ID (partition key)</param>
    /// <returns>True if deleted successfully</returns>
    public async Task<bool> DeleteJobOpportunityAsync(string jobId, string twinId)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            await jobContainer.DeleteItemAsync<Dictionary<string, object?>>(
                jobId,
                new PartitionKey(twinId)
            );

            _logger.LogInformation("?? Job opportunity deleted successfully: {JobId} for Twin: {TwinId}", jobId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to delete job opportunity: {JobId} for Twin: {TwinId}", jobId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Get job opportunity statistics by status for a Twin
    /// </summary>
    /// <param name="twinId">Twin ID to get statistics for</param>
    /// <returns>Job opportunity statistics</returns>
    public async Task<JobOpportunityStats> GetJobOpportunityStatsByTwinIdAsync(string twinId)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId")
                .WithParameter("@twinId", twinId);

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var jobOpportunities = new List<JobOpportunityData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var jobOpportunity = JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            var stats = new JobOpportunityStats
            {
                Total = jobOpportunities.Count,
                Aplicado = jobOpportunities.Count(j => j.Estado == JobApplicationStatus.Aplicado),
                Entrevista = jobOpportunities.Count(j => j.Estado == JobApplicationStatus.Entrevista),
                Esperando = jobOpportunities.Count(j => j.Estado == JobApplicationStatus.Esperando),
                Rechazado = jobOpportunities.Count(j => j.Estado == JobApplicationStatus.Rechazado),
                Aceptado = jobOpportunities.Count(j => j.Estado == JobApplicationStatus.Aceptado)
            };

            _logger.LogInformation("?? Generated job opportunity statistics for Twin ID: {TwinId} - Total: {Total}",
                twinId, stats.Total);
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get job opportunity statistics for Twin ID: {TwinId}", twinId);
            return new JobOpportunityStats
            {
                Total = 0,
                Aplicado = 0,
                Entrevista = 0,
                Esperando = 0,
                Rechazado = 0,
                Aceptado = 0
            };
        }
    }

    /// <summary>
    /// Get job opportunities by application status for a Twin
    /// </summary>
    /// <param name="twinId">Twin ID to filter by</param>
    /// <param name="status">Application status to filter by</param>
    /// <returns>List of job opportunities with the specified status</returns>
    public async Task<List<JobOpportunityData>> GetJobOpportunitiesByStatusAsync(string twinId, JobApplicationStatus status)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.TwinID = @twinId AND c.estado = @estado ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@estado", status.ToString().ToLowerInvariant());

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var jobOpportunities = new List<JobOpportunityData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var jobOpportunity = JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} job opportunities with status '{Status}' for Twin ID: {TwinId}",
                jobOpportunities.Count, status, twinId);
            return jobOpportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get job opportunities by status for Twin ID: {TwinId}", twinId);
            return new List<JobOpportunityData>();
        }
    }

    /// <summary>
    /// Search job opportunities by text content
    /// </summary>
    /// <param name="twinId">Twin ID to search within</param>
    /// <param name="searchTerm">Text to search for</param>
    /// <param name="limit">Maximum number of results</param>
    /// <returns>List of matching job opportunities</returns>
    public async Task<List<JobOpportunityData>> SearchJobOpportunitiesAsync(string twinId, string searchTerm, int limit = 20)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var query = new QueryDefinition(@"
                SELECT * FROM c 
                WHERE c.TwinID = @twinId 
                AND (
                    CONTAINS(LOWER(c.empresa), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.puesto), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.descripcion), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.responsabilidades), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.habilidadesRequeridas), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.ubicacion), LOWER(@searchTerm)) OR
                    CONTAINS(LOWER(c.notas), LOWER(@searchTerm))
                )
                ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@searchTerm", searchTerm);

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = limit }
            );

            var jobOpportunities = new List<JobOpportunityData>();
            var itemsProcessed = 0;

            while (iterator.HasMoreResults && itemsProcessed < limit)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    if (itemsProcessed >= limit) break;

                    try
                    {
                        var jobOpportunity = JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                        itemsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} job opportunities matching '{SearchTerm}' for Twin ID: {TwinId}",
                jobOpportunities.Count, searchTerm, twinId);
            return jobOpportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to search job opportunities for Twin ID: {TwinId}", twinId);
            return new List<JobOpportunityData>();
        }
    }

    /// <summary>
    /// Get recent job opportunities for a Twin (last N opportunities)
    /// </summary>
    /// <param name="twinId">Twin ID to get opportunities for</param>
    /// <param name="count">Number of recent opportunities to retrieve</param>
    /// <returns>List of recent job opportunities</returns>
    public async Task<List<JobOpportunityData>> GetRecentJobOpportunitiesAsync(string twinId, int count = 10)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId);

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = count }
            );

            var jobOpportunities = new List<JobOpportunityData>();
            var itemsProcessed = 0;

            while (iterator.HasMoreResults && itemsProcessed < count)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    if (itemsProcessed >= count) break;

                    try
                    {
                        var jobOpportunity = JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                        itemsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} recent job opportunities for Twin ID: {TwinId}",
                jobOpportunities.Count, twinId);
            return jobOpportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get recent job opportunities for Twin ID: {TwinId}", twinId);
            return new List<JobOpportunityData>();
        }
    }

    /// <summary>
    /// Update job opportunity status
    /// </summary>
    /// <param name="jobId">Job opportunity ID</param>
    /// <param name="twinId">Twin ID (partition key)</param>
    /// <param name="newStatus">New application status</param>
    /// <param name="notes">Optional notes about the status change</param>
    /// <returns>True if updated successfully</returns>
    public async Task<bool> UpdateJobOpportunityStatusAsync(string jobId, string twinId, JobApplicationStatus newStatus, string? notes = null)
    {
        try
        {
            // First get the existing job opportunity
            var existingJob = await GetJobOpportunityByIdAsync(jobId, twinId);
            if (existingJob == null)
            {
                _logger.LogWarning("?? Job opportunity not found for status update: {JobId}", jobId);
                return false;
            }

            // Update status and notes
            existingJob.Estado = newStatus;
            existingJob.FechaActualizacion = DateTime.UtcNow;
            
            if (!string.IsNullOrEmpty(notes))
            {
                existingJob.Notas = string.IsNullOrEmpty(existingJob.Notas) 
                    ? notes 
                    : $"{existingJob.Notas}\n[{DateTime.UtcNow:yyyy-MM-dd HH:mm}] {notes}";
            }

            // Save the updated job opportunity
            var success = await UpdateJobOpportunityAsync(existingJob);

            if (success)
            {
                _logger.LogInformation("?? Job opportunity status updated: {JobId} -> {NewStatus}", jobId, newStatus);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update job opportunity status: {JobId} for Twin: {TwinId}", jobId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Get job opportunities by company for a Twin
    /// </summary>
    /// <param name="twinId">Twin ID to filter by</param>
    /// <param name="companyName">Company name to filter by</param>
    /// <returns>List of job opportunities for the specified company</returns>
    public async Task<List<JobOpportunityData>> GetJobOpportunitiesByCompanyAsync(string twinId, string companyName)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.TwinID = @twinId AND CONTAINS(LOWER(c.empresa), LOWER(@empresa)) ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@empresa", companyName);

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var jobOpportunities = new List<JobOpportunityData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var jobOpportunity = JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} job opportunities for company '{Company}' and Twin ID: {TwinId}",
                jobOpportunities.Count, companyName, twinId);
            return jobOpportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get job opportunities by company for Twin ID: {TwinId}", twinId);
            return new List<JobOpportunityData>();
        }
    }
}