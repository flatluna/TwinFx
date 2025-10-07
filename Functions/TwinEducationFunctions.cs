using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Services;

namespace TwinFx.Functions;

public class TwinEducationFunctions
{
    private readonly ILogger<TwinEducationFunctions> _logger;
    private readonly IConfiguration _configuration;

    public TwinEducationFunctions(ILogger<TwinEducationFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for education routes
    [Function("EducationOptions")]
    public async Task<HttpResponseData> HandleEducationOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/education")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"🎓 OPTIONS preflight request for twins/{twinId}/education");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for specific education routes
    [Function("EducationByIdOptions")]
    public async Task<HttpResponseData> HandleEducationByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/education/{educationId}")] HttpRequestData req,
        string twinId, string educationId)
    {
        _logger.LogInformation($"📚 OPTIONS preflight request for twins/{twinId}/education/{educationId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("CreateEducation")]
    public async Task<HttpResponseData> CreateEducation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/education")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🎓 CreateEducation function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");
            _logger.LogInformation($"🔍 RAW Request body: {requestBody}");

            // Parse JSON request
            var educationData = JsonSerializer.Deserialize<EducationData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Log the deserialized object as string for debugging
            if (educationData != null)
            {
                var serializedEducationData = JsonSerializer.Serialize(educationData, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });
                _logger.LogInformation($"📋 Deserialized EducationData: {serializedEducationData}");
            }
            else
            {
                _logger.LogWarning("⚠️ EducationData deserialized to null");
            }

            if (educationData == null)
            {
                _logger.LogError("❌ Failed to parse education data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid education data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(educationData.Institution))
            {
                _logger.LogError("❌ Institution is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Institution is required"
                }));
                return badResponse;
            }

            // Set Twin ID and generate education ID if not provided
            educationData.TwinID = twinId;
            if (string.IsNullOrEmpty(educationData.Id))
            {
                educationData.Id = Guid.NewGuid().ToString();
            }

            // Set default CountryID if not provided
            if (string.IsNullOrEmpty(educationData.CountryID))
            {
                educationData.CountryID = "US"; // Default to US
            }

            _logger.LogInformation($"🎓 Creating education record: {educationData.Institution} {educationData.EducationType} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Create the education record
            var success = await cosmosService.CreateEducationAsync(educationData);

            var response = req.CreateResponse(success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Education record created successfully: {educationData.Institution} {educationData.EducationType}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    education = educationData,
                    message = "Education record created successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to create education record: {educationData.Institution} {educationData.EducationType}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to create education record in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating education record");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetEducationByTwinId")]
    public async Task<HttpResponseData> GetEducationByTwinId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/education")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🎓 GetEducationByTwinId function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"🎓 Getting education records for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Get education records by Twin ID
            var educationRecords = await cosmosService.GetEducationsByTwinIdAsync(twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");
            var data = Newtonsoft.Json.JsonConvert.SerializeObject(educationRecords);
         

            _logger.LogInformation($"✅ Found {educationRecords.Count} education records for Twin ID: {twinId}");
            await response.WriteStringAsync(data); 

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting education records");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetEducationById")]
    public async Task<HttpResponseData> GetEducationById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/education/{educationId}")] HttpRequestData req,
        string twinId, string educationId)
    {
        _logger.LogInformation("📚 GetEducationById function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(educationId))
            {
                _logger.LogError("❌ Twin ID and Education ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Education ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"📚 Getting education record {educationId} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // First, get all education records to find the CountryID for the specific education record
            var allEducationRecords = await cosmosService.GetEducationsByTwinIdAsync(twinId);
            var targetEducation = allEducationRecords.FirstOrDefault(e => e.Id == educationId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (targetEducation == null)
            {
                _logger.LogInformation($"📭 No education record found with ID: {educationId} for Twin ID: {twinId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Education record not found",
                    educationId = educationId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation($"✅ Education record found: {targetEducation.Institution} {targetEducation.EducationType}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    education = targetEducation,
                    educationId = educationId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting education record by ID");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("UpdateEducation")]
    public async Task<HttpResponseData> UpdateEducation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/education/{educationId}")] HttpRequestData req,
        string twinId, string educationId)
    {
        _logger.LogInformation("📚 UpdateEducation function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(educationId))
            {
                _logger.LogError("❌ Twin ID and Education ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Education ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");
            _logger.LogInformation($"🔍 RAW Request body: {requestBody}");

            // Parse JSON request
            var updateData = JsonSerializer.Deserialize<EducationData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Log the deserialized object as string for debugging
            if (updateData != null)
            {
                var serializedUpdateData = JsonSerializer.Serialize(updateData, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });
                _logger.LogInformation($"📋 Deserialized UpdateData: {serializedUpdateData}");
            }
            else
            {
                _logger.LogWarning("⚠️ UpdateData deserialized to null");
            }

            if (updateData == null)
            {
                _logger.LogError("❌ Failed to parse education update data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid education update data format"
                }));
                return badResponse;
            }

            // Ensure the IDs match
            updateData.Id = educationId;
            updateData.TwinID = twinId;

            // Set default CountryID if not provided
            if (string.IsNullOrEmpty(updateData.CountryID))
            {
                updateData.CountryID = "US"; // Default to US
            }

            _logger.LogInformation($"📚 Updating education record {educationId}: {updateData.Institution} {updateData.EducationType} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Update the education record
            var success = await cosmosService.UpdateEducationAsync(updateData);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Education record updated successfully: {updateData.Institution} {updateData.EducationType}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    education = updateData,
                    message = "Education record updated successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to update education record: {educationId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to update education record in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating education record");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("DeleteEducation")]
    public async Task<HttpResponseData> DeleteEducation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/education/{educationId}")] HttpRequestData req,
        string twinId, string educationId)
    {
        _logger.LogInformation("🗑️ DeleteEducation function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(educationId))
            {
                _logger.LogError("❌ Twin ID and Education ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Education ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"🗑️ Deleting education record {educationId} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // First, get all education records to find the CountryID for the specific education record
            var allEducationRecords = await cosmosService.GetEducationsByTwinIdAsync(twinId);
            var targetEducation = allEducationRecords.FirstOrDefault(e => e.Id == educationId);

            if (targetEducation == null)
            {
                _logger.LogWarning($"⚠️ Education record {educationId} not found for Twin ID: {twinId}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Education record not found"
                }));
                return notFoundResponse;
            }

            // Delete the education record using the CountryID from the found record
            var success = await cosmosService.DeleteEducationAsync(educationId, targetEducation.CountryID);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Education record deleted successfully: {educationId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    educationId = educationId,
                    twinId = twinId,
                    message = "Education record deleted successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to delete education record: {educationId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to delete education record from database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting education record");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        // Get origin from request headers
        var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
        var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;
        
        // Allow specific origins for development
        var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:5174", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:5174", "http://127.0.0.1:3000" };
        
        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
        }
        else
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
        }
        
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
        response.Headers.Add("Access-Control-Max-Age", "3600");
    }
}