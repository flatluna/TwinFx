using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions for managing job opportunities (OportunidadEmpleo)
/// Provides CRUD operations for tracking job applications with comprehensive validation
/// 
/// Endpoints:
/// - POST   /api/twins/{twinId}/opportunities - Create new job opportunity
/// - GET    /api/twins/{twinId}/opportunities - Get all opportunities with filtering
/// - GET    /api/twins/{twinId}/opportunities/{jobId} - Get specific opportunity
/// - PUT    /api/twins/{twinId}/opportunities/{jobId} - Update opportunity
/// - DELETE /api/twins/{twinId}/opportunities/{jobId} - Delete opportunity
/// - GET    /api/twins/{twinId}/opportunities/stats - Get opportunity statistics
/// 
/// Validation Rules:
/// - empresa: required, min 2 characters
/// - puesto: required, min 2 characters  
/// - twinId: required
/// - contactoEmail: valid email format (if provided)
/// - estado: must be one of: aplicado, entrevista, esperando, rechazado, aceptado
/// 
/// Features:
/// - Multi-tenant support via twinId
/// - Comprehensive filtering and pagination
/// - Statistics by application status
/// - Full CORS support
/// - Detailed validation with Spanish error messages
/// - Integration with TwinFx ecosystem
/// </summary>
public class OpportunitiesFunctions
{
    private readonly ILogger<OpportunitiesFunctions> _logger;
    private readonly IConfiguration _configuration;

    public OpportunitiesFunctions(ILogger<OpportunitiesFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // ========================================================================================
    // CREATE JOB OPPORTUNITY
    // ========================================================================================

    /// <summary>
    /// Create a new job opportunity
    /// POST /api/twins/{twinId}/opportunities
    /// </summary>
    [Function("CreateJobOpportunity")]
    public async Task<HttpResponseData> CreateJobOpportunity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/opportunities")] HttpRequestData req,
        string twinId
    )
    {
        _logger.LogInformation("💼 CreateJobOpportunity function triggered for Twin: {TwinId}", twinId);

        try
        {
            // Validate twinId parameter
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            var createRequest = JsonSerializer.Deserialize<CreateJobOpportunityRequest>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (createRequest == null)
            {
                return await CreateErrorResponse(req, "Invalid request format", HttpStatusCode.BadRequest);
            }

            // Validate the request
            var validationResults = ValidateRequest(createRequest);
            if (validationResults.Count > 0)
            {
                var errorMessage = string.Join("; ", validationResults.Select(v => v.ErrorMessage));
                return await CreateErrorResponse(req, $"Validation failed: {errorMessage}", HttpStatusCode.BadRequest);
            }

            // Create job opportunity object
            var jobOpportunity = new TwinFx.Models.JobOpportunityData
            {
                Id = Guid.NewGuid().ToString(),
                Empresa = createRequest.Empresa.Trim(),
                URLCompany = createRequest.URLCompany?.Trim(),
                Puesto = createRequest.Puesto.Trim(),
                Descripcion = createRequest.Descripcion?.Trim(),
                Responsabilidades = createRequest.Responsabilidades?.Trim(),
                HabilidadesRequeridas = createRequest.HabilidadesRequeridas?.Trim(),
                Salario = createRequest.Salario?.Trim(),
                Beneficios = createRequest.Beneficios?.Trim(),
                Ubicacion = createRequest.Ubicacion?.Trim(),
                FechaAplicacion = createRequest.FechaAplicacion,
                Estado = createRequest.Estado,
                ContactoNombre = createRequest.ContactoNombre?.Trim(),
                ContactoEmail = createRequest.ContactoEmail?.Trim(),
                ContactoTelefono = createRequest.ContactoTelefono?.Trim(),
                Notas = createRequest.Notas?.Trim(),
                TwinID = twinId, // Use TwinID instead of UsuarioId
                UsuarioId = twinId, // Keep for backward compatibility
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };

            // Save to Cosmos DB using specialized Opportunities service
            var opportunitiesService = new OpportunitiesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpportunitiesCosmosDbService>(),
                _configuration);

            var success = await opportunitiesService.CreateJobOpportunityAsync(jobOpportunity);

            if (!success)
            {
                return await CreateErrorResponse(req, "Failed to create job opportunity", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation("✅ Job opportunity created successfully: {JobId} - {Puesto} at {Empresa}", 
                jobOpportunity.Id, jobOpportunity.Puesto, jobOpportunity.Empresa);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.Created);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new JobOpportunityResponse
            {
                Success = true,
                Message = "Job opportunity created successfully",
                Data = jobOpportunity,
                ProcessedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating job opportunity for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// OPTIONS handler for create job opportunity
    /// </summary>
    [Function("CreateJobOpportunityOptions")]
    public async Task<HttpResponseData> HandleCreateJobOpportunityOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/opportunities")] HttpRequestData req,
        string twinId
    )
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for create job opportunity: twins/{TwinId}/opportunities", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================================================================
    // GET JOB OPPORTUNITIES (LIST WITH FILTERING)
    // ========================================================================================

    /// <summary>
    /// Get job opportunities for a twin with optional filtering and pagination
    /// GET /api/twins/{twinId}/opportunities?estado=aplicado&empresa=Microsoft&page=1&pageSize=20
    /// </summary>
    [Function("GetJobOpportunities")]
    public async Task<HttpResponseData> GetJobOpportunities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/opportunities")] HttpRequestData req,
        string twinId
    )
    {
        _logger.LogInformation("📋 GetJobOpportunities function triggered for Twin: {TwinId}", twinId);

        try
        {
            // Validate twinId parameter
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Parse query parameters
            var query = ParseQueryParameters(req);

            // Create specialized Opportunities Cosmos DB service
            var opportunitiesService = new OpportunitiesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpportunitiesCosmosDbService>(),
                _configuration);

            // Get job opportunities
            var opportunities = await opportunitiesService.GetJobOpportunitiesByTwinIdAsync(twinId, query);

            // Get statistics
            var stats = await opportunitiesService.GetJobOpportunityStatsByTwinIdAsync(twinId);

            _logger.LogInformation("📊 Retrieved {Count} job opportunities for Twin: {TwinId}", opportunities.Count, twinId);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new GetJobOpportunitiesResponse
            {
                Success = true,
                Message = $"Found {opportunities.Count} job opportunities for Twin ID: {twinId}",
                UsuarioId = twinId, // Keep for compatibility
                TotalOpportunities = opportunities.Count,
                Opportunities = opportunities,
                Stats = stats,
                ProcessedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting job opportunities for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    // ========================================================================================
    // GET SINGLE JOB OPPORTUNITY
    // ========================================================================================

    /// <summary>
    /// Get a specific job opportunity by ID
    /// GET /api/twins/{twinId}/opportunities/{jobId}
    /// </summary>
    [Function("GetJobOpportunity")]
    public async Task<HttpResponseData> GetJobOpportunity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/opportunities/{jobId}")] HttpRequestData req,
        string twinId,
        string jobId
    )
    {
        _logger.LogInformation("📄 GetJobOpportunity function triggered for Twin: {TwinId}, Job: {JobId}", twinId, jobId);

        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(jobId))
            {
                return await CreateErrorResponse(req, "Job ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Create specialized Opportunities Cosmos DB service
            var opportunitiesService = new OpportunitiesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpportunitiesCosmosDbService>(),
                _configuration);

            // Get job opportunity
            var jobOpportunity = await opportunitiesService.GetJobOpportunityByIdAsync(jobId, twinId);

            if (jobOpportunity == null)
            {
                return await CreateErrorResponse(req, "Job opportunity not found", HttpStatusCode.NotFound);
            }

            _logger.LogInformation("✅ Job opportunity retrieved: {Puesto} at {Empresa}", jobOpportunity.Puesto, jobOpportunity.Empresa);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new JobOpportunityResponse
            {
                Success = true,
                Message = "Job opportunity retrieved successfully",
                Data = jobOpportunity,
                ProcessedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting job opportunity {JobId} for Twin: {TwinId}", jobId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// OPTIONS handler for individual job opportunity operations (GET, PUT, DELETE)
    /// </summary>
    [Function("JobOpportunityByIdOptions")]
    public async Task<HttpResponseData> HandleJobOpportunityByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/opportunities/{jobId}")] HttpRequestData req,
        string twinId,
        string jobId
    )
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for job opportunity operations: twins/{TwinId}/opportunities/{JobId}", twinId, jobId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================================================================
    // UPDATE JOB OPPORTUNITY
    // ========================================================================================

    /// <summary>
    /// Update an existing job opportunity
    /// PUT /api/twins/{twinId}/opportunities/{jobId}
    /// </summary>
    [Function("UpdateJobOpportunity")]
    public async Task<HttpResponseData> UpdateJobOpportunity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/opportunities/{jobId}")] HttpRequestData req,
        string twinId,
        string jobId
    )
    {
        _logger.LogInformation("✏️ UpdateJobOpportunity function triggered for Twin: {TwinId}, Job: {JobId}", twinId, jobId);

        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(jobId))
            {
                return await CreateErrorResponse(req, "Job ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
            }

            var updateRequest = JsonSerializer.Deserialize<UpdateJobOpportunityRequest>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (updateRequest == null)
            {
                return await CreateErrorResponse(req, "Invalid request format", HttpStatusCode.BadRequest);
            }

            // Create specialized Opportunities Cosmos DB service
            var opportunitiesService = new OpportunitiesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpportunitiesCosmosDbService>(),
                _configuration);

            // Get existing job opportunity
            var existingJob = await opportunitiesService.GetJobOpportunityByIdAsync(jobId, twinId);
            if (existingJob == null)
            {
                return await CreateErrorResponse(req, "Job opportunity not found", HttpStatusCode.NotFound);
            }

            // Update only provided fields
            if (!string.IsNullOrEmpty(updateRequest.Empresa))
                existingJob.Empresa = updateRequest.Empresa.Trim();
            
            if (updateRequest.URLCompany != null)
                existingJob.URLCompany = updateRequest.URLCompany.Trim();
            
            if (!string.IsNullOrEmpty(updateRequest.Puesto))
                existingJob.Puesto = updateRequest.Puesto.Trim();
            
            if (updateRequest.Descripcion != null)
                existingJob.Descripcion = updateRequest.Descripcion.Trim();
            
            if (updateRequest.Responsabilidades != null)
                existingJob.Responsabilidades = updateRequest.Responsabilidades.Trim();
            
            if (updateRequest.HabilidadesRequeridas != null)
                existingJob.HabilidadesRequeridas = updateRequest.HabilidadesRequeridas.Trim();
            
            if (updateRequest.Salario != null)
                existingJob.Salario = updateRequest.Salario.Trim();
            
            if (updateRequest.Beneficios != null)
                existingJob.Beneficios = updateRequest.Beneficios.Trim();
            
            if (updateRequest.Ubicacion != null)
                existingJob.Ubicacion = updateRequest.Ubicacion.Trim();
            
            if (updateRequest.FechaAplicacion.HasValue)
                existingJob.FechaAplicacion = updateRequest.FechaAplicacion;
            
            if (updateRequest.Estado.HasValue)
                existingJob.Estado = updateRequest.Estado.Value;
            
            if (updateRequest.ContactoNombre != null)
                existingJob.ContactoNombre = updateRequest.ContactoNombre.Trim();
            
            if (updateRequest.ContactoEmail != null)
                existingJob.ContactoEmail = updateRequest.ContactoEmail.Trim();
            
            if (updateRequest.ContactoTelefono != null)
                existingJob.ContactoTelefono = updateRequest.ContactoTelefono.Trim();
            
            if (updateRequest.Notas != null)
                existingJob.Notas = updateRequest.Notas.Trim();

            // Validate the updated job
            var validationResults = ValidateJobOpportunity(existingJob);
            if (validationResults.Count > 0)
            {
                var errorMessage = string.Join("; ", validationResults.Select(v => v.ErrorMessage));
                return await CreateErrorResponse(req, $"Validation failed: {errorMessage}", HttpStatusCode.BadRequest);
            }

            // Update timestamp
            existingJob.FechaActualizacion = DateTime.UtcNow;

            // Save changes
            var success = await opportunitiesService.UpdateJobOpportunityAsync(existingJob);

            if (!success)
            {
                return await CreateErrorResponse(req, "Failed to update job opportunity", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation("✅ Job opportunity updated successfully: {JobId} - {Puesto} at {Empresa}", 
                existingJob.Id, existingJob.Puesto, existingJob.Empresa);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new JobOpportunityResponse
            {
                Success = true,
                Message = "Job opportunity updated successfully",
                Data = existingJob,
                ProcessedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating job opportunity {JobId} for Twin: {TwinId}", jobId, twinId);
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    // ========================================================================================
    // DELETE JOB OPPORTUNITY
    // ========================================================================================

    /// <summary>
    /// Delete a job opportunity
    /// DELETE /api/twins/{twinId}/opportunities/{jobId}
    /// </summary>
    [Function("DeleteJobOpportunity")]
    public async Task<HttpResponseData> DeleteJobOpportunity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/opportunities/{jobId}")] HttpRequestData req,
        string twinId,
        string jobId
    )
    {
        _logger.LogInformation("🗑️ DeleteJobOpportunity function triggered for Twin: {TwinId}, Job: {JobId}", twinId, jobId);

        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(jobId))
            {
                return await CreateErrorResponse(req, "Job ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Create specialized Opportunities Cosmos DB service
            var opportunitiesService = new OpportunitiesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpportunitiesCosmosDbService>(),
                _configuration);

            // Check if job opportunity exists first
            var existingJob = await opportunitiesService.GetJobOpportunityByIdAsync(jobId, twinId);
            if (existingJob == null)
            {
                return await CreateErrorResponse(req, "Job opportunity not found", HttpStatusCode.NotFound);
            }

            // Delete the job opportunity
            var success = await opportunitiesService.DeleteJobOpportunityAsync(jobId, twinId);

            if (!success)
            {
                return await CreateErrorResponse(req, "Failed to delete job opportunity", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation("✅ Job opportunity deleted successfully: {JobId} - {Puesto} at {Empresa}", 
                existingJob.Id, existingJob.Puesto, existingJob.Empresa);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new JobOpportunityResponse
            {
                Success = true,
                Message = "Job opportunity deleted successfully",
                Data = null,
                ProcessedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting job opportunity {JobId} for Twin: {TwinId}", jobId, twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    // ========================================================================================
    // GET JOB OPPORTUNITY STATISTICS
    // ========================================================================================

    /// <summary>
    /// Get job opportunity statistics by status
    /// GET /api/twins/{twinId}/opportunities/stats
    /// </summary>
    [Function("GetJobOpportunityStats")]
    public async Task<HttpResponseData> GetJobOpportunityStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/opportunities/stats")] HttpRequestData req,
        string twinId
    )
    {
        _logger.LogInformation("📊 GetJobOpportunityStats function triggered for Twin: {TwinId}", twinId);

        try
        {
            // Validate twinId parameter
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Create specialized Opportunities Cosmos DB service
            var opportunitiesService = new OpportunitiesCosmosDbService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpportunitiesCosmosDbService>(),
                _configuration);

            // Get statistics
            var stats = await opportunitiesService.GetJobOpportunityStatsByTwinIdAsync(twinId);

            _logger.LogInformation("📈 Job opportunity stats retrieved for Twin: {TwinId} - Total: {Total}", twinId, stats.Total);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"Job opportunity statistics for Twin ID: {twinId}",
                twinId = twinId,
                stats = stats,
                processedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting job opportunity stats for Twin: {TwinId}", twinId);
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// OPTIONS handler for job opportunity statistics
    /// </summary>
    [Function("GetJobOpportunityStatsOptions")]
    public async Task<HttpResponseData> HandleGetJobOpportunityStatsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/opportunities/stats")] HttpRequestData req,
        string twinId
    )
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for job opportunity stats: twins/{TwinId}/opportunities/stats", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================================================================
    // HELPER METHODS
    // ========================================================================================

    /// <summary>
    /// Parse query parameters for filtering and pagination
    /// </summary>
    private JobOpportunityQuery ParseQueryParameters(HttpRequestData req)
    {
        var query = new JobOpportunityQuery();

        try
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            // Parse estado filter
            if (Enum.TryParse<TwinFx.Models.JobApplicationStatus>(queryParams["estado"], true, out var estado))
            {
                query.Estado = estado;
            }

            // Parse text filters
            query.Empresa = queryParams["empresa"];
            query.Puesto = queryParams["puesto"];
            query.Ubicacion = queryParams["ubicacion"];

            // Parse date filters
            if (DateTime.TryParse(queryParams["fechaDesde"], out var fechaDesde))
            {
                query.FechaDesde = fechaDesde;
            }

            if (DateTime.TryParse(queryParams["fechaHasta"], out var fechaHasta))
            {
                query.FechaHasta = fechaHasta;
            }

            // Parse pagination
            if (int.TryParse(queryParams["page"], out var page) && page > 0)
            {
                query.Page = page;
            }

            if (int.TryParse(queryParams["pageSize"], out var pageSize) && pageSize > 0 && pageSize <= 100)
            {
                query.PageSize = pageSize;
            }

            // Parse sorting
            query.SortBy = queryParams["sortBy"] ?? "fechaCreacion";
            query.SortDirection = queryParams["sortDirection"] ?? "desc";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error parsing query parameters, using defaults");
        }

        return query;
    }   

    /// <summary>
    /// Validate CreateJobOpportunityRequest using data annotations
    /// </summary>
    private List<ValidationResult> ValidateRequest(CreateJobOpportunityRequest request)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, true);
        return results;
    }

    /// <summary>
    /// Validate JobOpportunityData using data annotations
    /// </summary>
    private List<ValidationResult> ValidateJobOpportunity(TwinFx.Models.JobOpportunityData jobOpportunity)
    {
        var context = new ValidationContext(jobOpportunity);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(jobOpportunity, context, results, true);
        return results;
    }

    /// <summary>
    /// Create a standardized error response for HTTP functions
    /// </summary>
    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);

        var errorResponse = new
        {
            success = false,
            message = errorMessage,
            processedAt = DateTime.UtcNow
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, options));

        return response;
    }

    /// <summary>
    /// Add CORS headers to the response
    /// </summary>
    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData req)
    {
        // Allow from any origin (update with stricter policy if needed)
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");

        // Expose headers to client (if needed)
        response.Headers.Add("Access-Control-Expose-Headers", "Content-Disposition");
    }
}