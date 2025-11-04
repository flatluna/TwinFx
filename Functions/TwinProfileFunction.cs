using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TwinFx.Services;
using TwinFx.Models;
using TwinAgentsLibrary.Models;

namespace TwinFx.Functions;

public class TwinProfileFunction
{
    private readonly ILogger<TwinProfileFunction> _logger;
    private readonly IConfiguration _configuration;
    private readonly ProfileCosmosDB _cosmosService;

    public TwinProfileFunction(ILogger<TwinProfileFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _cosmosService = _configuration.CreateProfileCosmosService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ProfileCosmosDB>());
    }

    // OPTIONS handler for specific profile ID routes
    [Function("TwinProfileByIdOptions")]
    public async Task<HttpResponseData> HandleOptionsById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twin-profiles/id/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for twin-profiles/id/{id}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for base profiles route
    [Function("TwinProfileBaseOptions")]
    public async Task<HttpResponseData> HandleBaseOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twin-profiles")] HttpRequestData req)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twin-profiles");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetTwinProfileById")]
    public async Task<HttpResponseData> GetTwinProfileById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-profiles/id/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("?? GetTwinProfileById function triggered");

        try
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Looking up Twin profile for ID: {id}");

            // Use injected Cosmos DB service
            var profile = await _cosmosService.GetProfileById(id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (profile == null)
            {
                _logger.LogInformation($"?? No profile found for Twin ID: {id}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin profile not found",
                    twinId = id
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation($"? Twin profile found for ID: {id}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    profile = profile,
                    twinId = id
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting Twin profile");

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
    [Function("GetTwinProfilesBySubscriptionId")]
    public async Task<HttpResponseData> GetTwinProfilesBySubscriptionId(
       [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-profiles/subscription/{id}")] HttpRequestData req,
       string id)
    {
        _logger.LogInformation("?? GetTwinProfilesBySubscriptionId function triggered");

        try
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("? Subscription ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Subscription ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Looking up Twin profiles for Subscription ID: {id}");

            // Use injected Cosmos DB service
            var profiles  = await _cosmosService.GetProfilesBySubscriptionIDAsync(id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (profiles == null)
            {
                _logger.LogInformation($"?? No profile found for Twin ID: {id}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin profile not found",
                    twinId = id
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation($"? Twin profile found for ID: {id}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    profile = profiles,
                    twinId = id
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting Twin profile");

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
    [Function("CreateTwinProfile")]
    public async Task<HttpResponseData> CreateTwinProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-profiles")] HttpRequestData req)
    {
        _logger.LogInformation("?? CreateTwinProfile function triggered");

        try
        {
            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body: {requestBody}");

            // Parse JSON request
            var profileData = JsonSerializer.Deserialize<TwinProfileData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (profileData == null)
            {
                _logger.LogError("? Failed to parse Twin profile data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid profile data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(profileData.TwinId))
            {
                _logger.LogError("? Twin ID is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Creating Twin profile for ID: {profileData.TwinId}");

            // Use injected Cosmos DB service
            // Check if profile already exists
            var existingProfile = await _cosmosService.GetProfileById(profileData.TwinId);
            if (existingProfile != null)
            {
                _logger.LogWarning($"?? Twin profile already exists for ID: {profileData.TwinId}");
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                AddCorsHeaders(conflictResponse, req);
                await conflictResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin profile already exists",
                    existingProfile = existingProfile
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return conflictResponse;
            }

            // Set default values if not provided
            if (string.IsNullOrEmpty(profileData.CountryID))
                profileData.CountryID = "US"; // Default to US

            if (string.IsNullOrEmpty(profileData.TwinName))
                profileData.TwinName = $"Twin_{profileData.TwinId}";

            // Create the profile
            var success = await _cosmosService.CreateProfileAsync(profileData);

            var response = req.CreateResponse(success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"? Twin profile created successfully for ID: {profileData.TwinId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    profile = profileData,
                    message = "Twin profile created successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"? Failed to create Twin profile for ID: {profileData.TwinId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to create Twin profile in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating Twin profile");

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

    [Function("UpdateTwinProfile")]
    public async Task<HttpResponseData> UpdateTwinProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twin-profiles/id/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("?? UpdateTwinProfile function triggered");

        try
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("? Twin ID parameter is required");
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
            _logger.LogInformation($"?? Request body: {requestBody}");

            // Parse JSON request
            var updateData = JsonSerializer.Deserialize<TwinProfileData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateData == null)
            {
                _logger.LogError("? Failed to parse Twin profile update data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid profile update data format"
                }));
                return badResponse;
            }

            // Ensure the Twin ID matches
            updateData.TwinId = id;

            _logger.LogInformation($"?? Updating Twin profile for ID: {id}");

            // Use injected Cosmos DB service
            var success = await _cosmosService.UpdateProfileAsync(updateData);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"? Twin profile updated successfully for ID: {id}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    profile = updateData,
                    message = "Twin profile updated successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"? Failed to update Twin profile for ID: {id}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to update Twin profile in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating Twin profile");

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
        var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };
        
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