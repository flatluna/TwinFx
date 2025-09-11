using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Agents;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions for managing travel records
/// ========================================================================
/// 
/// Provides RESTful API endpoints for travel management:
/// - Create new travel records
/// - Retrieve travel records with filtering
/// - Update existing travel records
/// - Delete travel records
/// - Advanced search and analytics
/// 
/// All operations are secured and include proper CORS handling.
/// Uses TravelAgent for business logic and data persistence.
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class TravelFunction
{
    private readonly ILogger<TravelFunction> _logger;
    private readonly IConfiguration _configuration;

    public TravelFunction(ILogger<TravelFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for travel routes
    [Function("TravelOptions")]
    public async Task<HttpResponseData> HandleTravelOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/travels", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for specific travel routes
    [Function("TravelByIdOptions")]
    public async Task<HttpResponseData> HandleTravelByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{travelId}")] HttpRequestData req,
        string twinId, string travelId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/travels/{TravelId}", twinId, travelId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for itinerary routes
    [Function("ItineraryOptions")]
    public async Task<HttpResponseData> HandleItineraryOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{travelId}/itinerarios")] HttpRequestData req,
        string twinId, string travelId)
    {
        _logger.LogInformation("??? OPTIONS preflight request for twins/{TwinId}/travels/{TravelId}/itinerarios", twinId, travelId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for specific itinerary routes
    [Function("ItineraryByIdOptions")]
    public async Task<HttpResponseData> HandleItineraryByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}")] HttpRequestData req,
        string twinId, string travelId, string itineraryId)
    {
        _logger.LogInformation("??? OPTIONS preflight request for twins/{TwinId}/travels/{TravelId}/itinerarios/{ItineraryId}", twinId, travelId, itineraryId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Create a new travel record
    /// </summary>
    [Function("CreateTravel")]
    public async Task<HttpResponseData> CreateTravel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/travels")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? CreateTravel function triggered for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
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
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var createRequest = JsonSerializer.Deserialize<CreateTravelRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                _logger.LogError("? Failed to parse travel creation request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid travel data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(createRequest.Titulo))
            {
                _logger.LogError("? Travel title is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Travel title is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(createRequest.Descripcion))
            {
                _logger.LogError("? Travel description is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Travel description is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Creating travel: {Title} for Twin ID: {TwinId}", 
                createRequest.Titulo, twinId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Create the travel record
            var result = await travelAgent.CreateTravelAsync(twinId, createRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Travel created successfully: {Title}", createRequest.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    travel = result.Data,
                    message = result.Message
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to create travel: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating travel record");

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

    /// <summary>
    /// Get travel records for a specific Twin with optional filtering
    /// </summary>
    [Function("GetTravelsByTwinId")]
    public async Task<HttpResponseData> GetTravelsByTwinId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/travels")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? GetTravelsByTwinId function triggered for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
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

            // Parse query parameters
            var query = ParseTravelQuery(req);

            _logger.LogInformation("?? Getting travels for Twin ID: {TwinId} with query filters", twinId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Get travel records
            var result = await travelAgent.GetTravelsByTwinIdAsync(twinId, query);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation("? Found {Count} travel records for Twin ID: {TwinId}", 
                result.TotalTravels, twinId);

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = result.Success,
                travels = result.Travels,
                stats = result.Stats,
                twinId = result.TwinID,
                totalTravels = result.TotalTravels,
                message = result.Message
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting travel records");

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

    /// <summary>
    /// Get a specific travel record by ID
    /// </summary>
    [Function("GetTravelById")]
    public async Task<HttpResponseData> GetTravelById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/travels/{travelId}")] HttpRequestData req,
        string twinId, string travelId)
    {
        _logger.LogInformation("?? GetTravelById function triggered for Travel: {TravelId}, Twin: {TwinId}", 
            travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId))
            {
                _logger.LogError("? Twin ID and Travel ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Travel ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Getting travel {TravelId} for Twin ID: {TwinId}", travelId, twinId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Get travel record by ID
            var result = await travelAgent.GetTravelByIdAsync(travelId, twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (!result.Success)
            {
                _logger.LogInformation("?? Travel not found: {TravelId} for Twin ID: {TwinId}", travelId, twinId);
                response = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(response, req);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation("? Travel found: {Title}", result.Data?.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    travel = result.Data,
                    message = result.Message,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting travel record by ID");

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

    /// <summary>
    /// Update an existing travel record
    /// </summary>
    [Function("UpdateTravel")]
    public async Task<HttpResponseData> UpdateTravel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/travels/{travelId}")] HttpRequestData req,
        string twinId, string travelId)
    {
        _logger.LogInformation("?? UpdateTravel function triggered for Travel: {TravelId}, Twin: {TwinId}", 
            travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId))
            {
                _logger.LogError("? Twin ID and Travel ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Travel ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var updateRequest = JsonSerializer.Deserialize<UpdateTravelRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                _logger.LogError("? Failed to parse travel update request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid travel update data format"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Updating travel {TravelId} for Twin ID: {TwinId}", travelId, twinId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Update the travel record
            var result = await travelAgent.UpdateTravelAsync(twinId, travelId, updateRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Travel updated successfully: {Title}", result.Data?.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    travel = result.Data,
                    message = result.Message
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to update travel: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating travel record");

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

    /// <summary>
    /// Delete a travel record
    /// </summary>
    [Function("DeleteTravel")]
    public async Task<HttpResponseData> DeleteTravel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/travels/{travelId}")] HttpRequestData req,
        string twinId, string travelId)
    {
        _logger.LogInformation("??? DeleteTravel function triggered for Travel: {TravelId}, Twin: {TwinId}", 
            travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId))
            {
                _logger.LogError("? Twin ID and Travel ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Travel ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("??? Deleting travel {TravelId} for Twin ID: {TwinId}", travelId, twinId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Delete the travel record
            var result = await travelAgent.DeleteTravelAsync(travelId, twinId);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Travel deleted successfully: {TravelId}", travelId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    travel = result.Data,
                    message = result.Message,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to delete travel: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting travel record");

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

    /// <summary>
    /// Create a new itinerary for a travel record
    /// </summary>
    [Function("CreateItinerary")]
    public async Task<HttpResponseData> CreateItinerary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/travels/{travelId}/itinerarios")] HttpRequestData req,
        string twinId, string travelId)
    {
        _logger.LogInformation("??? CreateItinerary function triggered for Travel: {TravelId}, Twin: {TwinId}", 
            travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId))
            {
                _logger.LogError("? Twin ID and Travel ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Travel ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var createRequest = JsonSerializer.Deserialize<CreateItineraryRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                _logger.LogError("? Failed to parse itinerary creation request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid itinerary data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(createRequest.Titulo))
            {
                _logger.LogError("? Itinerary title is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Itinerary title is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("??? Creating itinerary: {Title} for Travel: {TravelId}", 
                createRequest.Titulo, travelId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Create the itinerary
            var result = await travelAgent.CreateItineraryAsync(travelId, twinId, createRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Itinerary created successfully: {Title}", createRequest.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    itinerary = result.Data,
                    travel = result.TravelData,
                    message = result.Message
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to create itinerary: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating itinerary");

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

    /// <summary>
    /// Update an existing itinerary within a travel record
    /// </summary>
    [Function("UpdateItinerary")]
    public async Task<HttpResponseData> UpdateItinerary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}")] HttpRequestData req,
        string twinId, string travelId, string itineraryId)
    {
        _logger.LogInformation("?? UpdateItinerary function triggered for Itinerary: {ItinerarioId}, Travel: {TravelId}, Twin: {TwinId}", 
            itineraryId, travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId) || string.IsNullOrEmpty(itineraryId))
            {
                _logger.LogError("? Twin ID, Travel ID and Itinerary ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Travel ID and Itinerary ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var updateRequest = JsonSerializer.Deserialize<UpdateItineraryRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                _logger.LogError("? Failed to parse itinerary update request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid itinerary update data format"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Updating itinerary: {ItinerarioId} for Travel: {TravelId}", itineraryId, travelId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Update the itinerary
            var result = await travelAgent.UpdateItineraryAsync(travelId, twinId, itineraryId, updateRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Itinerary updated successfully: {ItinerarioId}", itineraryId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    itinerary = result.Data,
                    travel = result.TravelData,
                    message = result.Message,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to update itinerary: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating itinerary");

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

    // OPTIONS handler for booking routes
    [Function("BookingOptions")]
    public async Task<HttpResponseData> HandleBookingOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}/bookings")] HttpRequestData req,
        string twinId, string travelId, string itineraryId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/travels/{TravelId}/itinerarios/{ItineraryId}/bookings", twinId, travelId, itineraryId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for specific booking routes
    [Function("BookingByIdOptions")]
    public async Task<HttpResponseData> HandleBookingByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}/bookings/{bookingId}")] HttpRequestData req,
        string twinId, string travelId, string itineraryId, string bookingId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/travels/{TravelId}/itinerarios/{ItineraryId}/bookings/{BookingId}", twinId, travelId, itineraryId, bookingId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Get all bookings for a specific itinerary
    /// </summary>
    [Function("GetBookings")]
    public async Task<HttpResponseData> GetBookings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}/bookings")] HttpRequestData req,
        string twinId, string travelId, string itineraryId)
    {
        _logger.LogInformation("?? GetBookings function triggered for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
            itineraryId, travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId) || string.IsNullOrEmpty(itineraryId))
            {
                _logger.LogError("? Twin ID, Travel ID and Itinerary ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Travel ID and Itinerary ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Getting bookings for Itinerary: {ItineraryId}", itineraryId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Get bookings
            var result = await travelAgent.GetBookingsByItineraryAsync(travelId, twinId, itineraryId);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Retrieved {Count} bookings for Itinerary: {ItineraryId}", 
                    result.TotalBookings, itineraryId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    bookings = result.Bookings,
                    totalBookings = result.TotalBookings,
                    message = result.Message,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to get bookings: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting bookings");

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

    /// <summary>
    /// Create a new booking within an itinerary
    /// </summary>
    [Function("CreateBooking")]
    public async Task<HttpResponseData> CreateBooking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}/bookings")] HttpRequestData req,
        string twinId, string travelId, string itineraryId)
    {
        _logger.LogInformation("?? CreateBooking function triggered for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
            itineraryId, travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId) || string.IsNullOrEmpty(itineraryId))
            {
                _logger.LogError("? Twin ID, Travel ID and Itinerary ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Travel ID and Itinerary ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var createRequest = JsonSerializer.Deserialize<CreateBookingRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                _logger.LogError("? Failed to parse booking creation request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid booking data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(createRequest.Titulo))
            {
                _logger.LogError("? Booking title is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Booking title is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Creating booking: {Title} for Itinerary: {ItineraryId}", 
                createRequest.Titulo, itineraryId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Create the booking
            var result = await travelAgent.CreateBookingAsync(twinId, itineraryId, travelId, createRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Booking created successfully: {Title}", createRequest.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    booking = result.Data,
                    itinerary = result.ItineraryData,
                    message = result.Message
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to create booking: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating booking");

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

    /// <summary>
    /// Update an existing booking within an itinerary
    /// </summary>
    [Function("UpdateBooking")]
    public async Task<HttpResponseData> UpdateBooking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}/bookings/{bookingId}")] HttpRequestData req,
        string twinId, string travelId, string itineraryId, string bookingId)
    {
        _logger.LogInformation("?? UpdateBooking function triggered for Booking: {BookingId}, Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
            bookingId, itineraryId, travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId) || string.IsNullOrEmpty(itineraryId) || string.IsNullOrEmpty(bookingId))
            {
                _logger.LogError("? All ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Travel ID, Itinerary ID and Booking ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var updateRequest = JsonSerializer.Deserialize<UpdateBookingRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                _logger.LogError("? Failed to parse booking update request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid booking update data format"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Updating booking: {BookingId} for Itinerary: {ItineraryId}", 
                bookingId, itineraryId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Update the booking
            var result = await travelAgent.UpdateBookingAsync(twinId, itineraryId, travelId, bookingId, updateRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Booking updated successfully: {BookingId}", bookingId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    booking = result.Data,
                    itinerary = result.ItineraryData,
                    message = result.Message,
                    bookingId = bookingId,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to update booking: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    bookingId = bookingId,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating booking");

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

    /// <summary>
    /// Delete a booking from an itinerary
    /// </summary>
    [Function("DeleteBooking")]
    public async Task<HttpResponseData> DeleteBooking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/travels/{travelId}/itinerarios/{itineraryId}/bookings/{bookingId}")] HttpRequestData req,
        string twinId, string travelId, string itineraryId, string bookingId)
    {
        _logger.LogInformation("??? DeleteBooking function triggered for Booking: {BookingId}, Itinerary: {ItinerarioId}, Travel: {TravelId}, Twin: {TwinId}", 
            bookingId, itineraryId, travelId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(travelId) || string.IsNullOrEmpty(itineraryId) || string.IsNullOrEmpty(bookingId))
            {
                _logger.LogError("? All ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Travel ID, Itinerary ID and Booking ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("??? Deleting booking: {BookingId} from Itinerary: {ItinerarioId}", 
                bookingId, itineraryId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Delete the booking
            var result = await travelAgent.DeleteBookingAsync(twinId, itineraryId, travelId, bookingId);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Booking deleted successfully: {BookingId}", bookingId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    booking = result.Data,
                    itinerary = result.ItineraryData,
                    message = result.Message,
                    bookingId = bookingId,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to delete booking: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    bookingId = bookingId,
                    itineraryId = itineraryId,
                    travelId = travelId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting booking");

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

    /// <summary>
    /// Create a new daily activity within an itinerary
    /// </summary>
    [Function("CreateDailyActivity")]
    public async Task<HttpResponseData> CreateDailyActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/travels/{viajeId}/itinerarios/{itinerarioId}/actividades-diarias")] HttpRequestData req,
        string twinId, string viajeId, string itinerarioId)
    {
        _logger.LogInformation("?? CreateDailyActivity function triggered for Itinerary: {ItinerarioId}, Travel: {ViajeId}, Twin: {TwinId}", 
            itinerarioId, viajeId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(viajeId) || string.IsNullOrEmpty(itinerarioId))
            {
                _logger.LogError("? Twin ID, Viaje ID and Itinerary ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Viaje ID and Itinerary ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var createRequest = JsonSerializer.Deserialize<CreateDailyActivityRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                _logger.LogError("? Failed to parse daily activity creation request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid daily activity data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(createRequest.Titulo))
            {
                _logger.LogError("? Daily activity title is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Daily activity title is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Creating daily activity: {Title} for Itinerary: {ItinerarioId}", 
                createRequest.Titulo, itinerarioId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Create the daily activity
            var result = await travelAgent.CreateDailyActivityAsync(viajeId, twinId, itinerarioId, createRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Daily activity created successfully: {Title}", createRequest.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    activity = result.Data,
                    itinerary = result.ItineraryData,
                    message = result.Message
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to create daily activity: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating daily activity");

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

    // OPTIONS handler for daily activities routes
    [Function("DailyActivityOptions")]
    public async Task<HttpResponseData> HandleDailyActivityOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{viajeId}/itinerarios/{itinerarioId}/actividades-diarias")] HttpRequestData req,
        string twinId, string viajeId, string itinerarioId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/travels/{ViajeId}/itinerarios/{ItinerarioId}/actividades-diarias", twinId, viajeId, itinerarioId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Get all daily activities for a specific itinerary
    /// </summary>
    [Function("GetDailyActivities")]
    public async Task<HttpResponseData> GetDailyActivities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/travels/{viajeId}/itinerarios/{itinerarioId}/actividades-diarias")] HttpRequestData req,
        string twinId, string viajeId, string itinerarioId)
    {
        _logger.LogInformation("?? GetDailyActivities function triggered for Itinerary: {ItinerarioId}, Travel: {ViajeId}, Twin: {TwinId}", 
            itinerarioId, viajeId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(viajeId) || string.IsNullOrEmpty(itinerarioId))
            {
                _logger.LogError("? Twin ID, Viaje ID and Itinerary ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Viaje ID and Itinerary ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Getting daily activities for Itinerary: {ItinerarioId}", itinerarioId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Get daily activities
            var result = await travelAgent.GetDailyActivitiesByItineraryAsync(viajeId, twinId, itinerarioId);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Retrieved {Count} daily activities for Itinerary: {ItinerarioId}", 
                    result.TotalActivities, itinerarioId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    activities = result.Activities,
                    totalActivities = result.TotalActivities,
                    message = result.Message,
                    itinerarioId = itinerarioId,
                    viajeId = viajeId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to get daily activities: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    itinerarioId = itinerarioId,
                    viajeId = viajeId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting daily activities");

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

    /// <summary>
    /// Get a specific daily activity by ID
    /// </summary>
    [Function("GetDailyActivityById")]
    public async Task<HttpResponseData> GetDailyActivityById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/travels/{viajeId}/itinerarios/{itinerarioId}/actividades-diarias/{activityId}")] HttpRequestData req,
        string twinId, string viajeId, string itinerarioId, string activityId)
    {
        _logger.LogInformation("?? GetDailyActivityById function triggered for Activity: {ActivityId}, Itinerary: {ItinerarioId}, Travel: {ViajeId}, Twin: {TwinId}", 
            activityId, itinerarioId, viajeId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(viajeId) || string.IsNullOrEmpty(itinerarioId) || string.IsNullOrEmpty(activityId))
            {
                _logger.LogError("? All ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Viaje ID, Itinerary ID and Activity ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Getting daily activity: {ActivityId} for Itinerary: {ItinerarioId}", 
                activityId, itinerarioId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Get specific daily activity
            var result = await travelAgent.GetDailyActivityByIdAsync(viajeId, twinId, itinerarioId, activityId);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Daily activity found: {Title}", result.Data?.Titulo);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    activity = result.Data,
                    message = result.Message,
                    activityId = activityId,
                    itinerarioId = itinerarioId,
                    viajeId = viajeId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Daily activity not found: {ActivityId}", activityId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    activityId = activityId,
                    itinerarioId = itinerarioId,
                    viajeId = viajeId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting daily activity by ID");

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

    // OPTIONS handler for daily activities by ID routes
    [Function("DailyActivityByIdOptions")]
    public async Task<HttpResponseData> HandleDailyActivityByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/travels/{viajeId}/itinerarios/{itinerarioId}/actividades-diarias/{activityId}")] HttpRequestData req,
        string twinId, string viajeId, string itinerarioId, string activityId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/travels/{ViajeId}/itinerarios/{ItinerarioId}/actividades-diarias/{ActivityId}", 
            twinId, viajeId, itinerarioId, activityId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Update an existing daily activity within an itinerary
    /// </summary>
    [Function("UpdateDailyActivity")]
    public async Task<HttpResponseData> UpdateDailyActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/travels/{viajeId}/itinerarios/{itinerarioId}/actividades-diarias/{activityId}")] HttpRequestData req,
        string twinId, string viajeId, string itinerarioId, string activityId)
    {
        _logger.LogInformation("?? UpdateDailyActivity function triggered for Activity: {ActivityId}, Itinerary: {ItinerarioId}, Travel: {ViajeId}, Twin: {TwinId}", 
            activityId, itinerarioId, viajeId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(viajeId) || string.IsNullOrEmpty(itinerarioId) || string.IsNullOrEmpty(activityId))
            {
                _logger.LogError("? All ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID, Viaje ID, Itinerary ID and Activity ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("?? Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var updateRequest = JsonSerializer.Deserialize<UpdateDailyActivityRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                _logger.LogError("? Failed to parse daily activity update request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid daily activity update data format"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Updating daily activity: {ActivityId} for Itinerary: {ItinerarioId}", 
                activityId, itinerarioId);

            // Create TravelAgent with proper logger
            var travelAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TravelAgent>();
            using var travelAgent = new TravelAgent(travelAgentLogger, _configuration);

            // Update the daily activity
            var result = await travelAgent.UpdateDailyActivityAsync(viajeId, twinId, itinerarioId, activityId, updateRequest);

            var response = req.CreateResponse(result.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (result.Success)
            {
                _logger.LogInformation("? Daily activity updated successfully: {ActivityId}", activityId);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    activity = result.Data,
                    itinerary = result.ItineraryData,
                    message = result.Message,
                    activityId = activityId,
                    itinerarioId = itinerarioId,
                    viajeId = viajeId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("? Failed to update daily activity: {Message}", result.Message);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = result.Message,
                    activityId = activityId,
                    itinerarioId = itinerarioId,
                    viajeId = viajeId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating daily activity");

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

    // OPTIONS handler for activity photo upload
    [Function("ActivityPhotoUploadOptions")]
    public async Task<HttpResponseData> HandleActivityPhotoUploadOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/activities/{activityId}/upload-photo")] HttpRequestData req,
        string twinId, string activityId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for activity photo upload: twins/{TwinId}/activities/{ActivityId}/upload-photo", 
            twinId, activityId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Upload a photo for a daily activity
    /// </summary>
    [Function("UploadActivityPhoto")]
    public async Task<HttpResponseData> UploadActivityPhoto(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/activities/{activityId}/upload-photo")] HttpRequestData req,
        string twinId, string activityId)
    {
        _logger.LogInformation("?? UploadActivityPhoto function triggered for Activity: {ActivityId}, Twin: {TwinId}", 
            activityId, twinId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                return await CreateActivityPhotoErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(activityId))
            {
                _logger.LogError("? Activity ID parameter is required");
                return await CreateActivityPhotoErrorResponse(req, "Activity ID parameter is required", HttpStatusCode.BadRequest);
            }

            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (contentTypeHeader.Key == null || !contentTypeHeader.Value.Any(v => v.Contains("multipart/form-data")))
            {
                return await CreateActivityPhotoErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeHeader.Value.FirstOrDefault() ?? "";
            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                return await CreateActivityPhotoErrorResponse(req, "Invalid boundary in multipart/form-data", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("?? Processing activity photo upload for Twin ID: {TwinId}, Activity ID: {ActivityId}", twinId, activityId);
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();
            _logger.LogInformation("?? Received multipart data: {Length} bytes", bodyBytes.Length);

            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            var photoPart = parts.FirstOrDefault(p => p.Name == "photo" || p.Name == "file" || p.Name == "image");
            if (photoPart == null || photoPart.Data == null || photoPart.Data.Length == 0)
            {
                return await CreateActivityPhotoErrorResponse(req, "No photo file data found in request. Expected field name: 'photo', 'file', or 'image'", HttpStatusCode.BadRequest);
            }

            var photoBytes = photoPart.Data;
            var fileNamePart = parts.FirstOrDefault(p => p.Name == "filename" || p.Name == "fileName");

            // Ruta simplificada para actividades
            var photoPath = $"Activities/{activityId}/fotos";
            var customFileName = fileNamePart?.StringValue?.Trim();

            string fileName;
            if (!string.IsNullOrEmpty(customFileName))
            {
                fileName = customFileName;
            }
            else if (!string.IsNullOrEmpty(photoPart.FileName))
            {
                fileName = photoPart.FileName;
            }
            else
            {
                var fileExtension = GetFileExtensionFromBytes(photoBytes);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                fileName = $"activity_{activityId}_{timestamp}.{fileExtension}";
            }

            var detectedExtension = GetFileExtensionFromBytes(photoBytes);
            if (!fileName.Contains('.'))
            {
                fileName += $".{detectedExtension}";
            }

            var fullFilePath = $"{photoPath}/{fileName}";
            _logger.LogInformation("?? Photo details: Size={Size} bytes, Path={Path}, FileName={FileName}, FullPath={FullPath}", 
                photoBytes.Length, photoPath, fileName, fullFilePath);

            if (!IsValidImageFile(fileName, photoBytes))
            {
                return await CreateActivityPhotoErrorResponse(req, "Invalid image file format. Supported formats: JPG, PNG, GIF, WEBP, BMP", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("?? STEP 1: Uploading activity photo to DataLake...");
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            var mimeType = GetMimeTypeFromExtension(detectedExtension);

            using var photoStream = new MemoryStream(photoBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                photoPath,
                fileName,
                photoStream,
                mimeType);

            if (!uploadSuccess)
            {
                _logger.LogError("? Failed to upload activity photo to DataLake");
                return await CreateActivityPhotoErrorResponse(req, "Failed to upload photo to storage", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation("? Activity photo uploaded successfully to DataLake");
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            var responseData = new ActivityPhotoUploadResponse
            {
                Success = true,
                Message = "Activity photo uploaded successfully",
                TwinId = twinId,
                ActivityId = activityId,
                FilePath = fullFilePath,
                FileName = fileName,
                Directory = photoPath,
                ContainerName = twinId.ToLowerInvariant(),
                PhotoUrl = sasUrl,
                FileSize = photoBytes.Length,
                MimeType = mimeType,
                UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
            };
            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
            _logger.LogInformation("? Activity photo upload completed successfully in {ProcessingTime:F2} seconds", responseData.ProcessingTimeSeconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in activity photo upload");
            return await CreateActivityPhotoErrorResponse(req, $"Upload failed: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    // OPTIONS handler for getting activity photos
    [Function("GetActivityPhotosOptions")]
    public async Task<HttpResponseData> HandleGetActivityPhotosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/activities/{activityId}/photos")] HttpRequestData req,
        string twinId, string activityId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for getting activity photos: twins/{TwinId}/activities/{ActivityId}/photos", 
            twinId, activityId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Get all photos for a specific activity
    /// </summary>
    [Function("GetActivityPhotos")]
    public async Task<HttpResponseData> GetActivityPhotos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/activities/{activityId}/photos")] HttpRequestData req,
        string twinId, string activityId)
    {
        _logger.LogInformation("?? GetActivityPhotos function triggered for Activity: {ActivityId}, Twin: {TwinId}", 
            activityId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
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

            if (string.IsNullOrEmpty(activityId))
            {
                _logger.LogError("? Activity ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Activity ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("?? Getting photos for Activity: {ActivityId}, Twin: {TwinId}", activityId, twinId);

            // Create DataLake client
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Get photos from the activity's photo directory
            var photoPath = $"Activities/{activityId}/fotos";
            var files = await dataLakeClient.ListFilesAsync(photoPath);

            // Filter for image files only
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var photoFiles = files.Where(file => imageExtensions.Any(ext => 
                                        file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                                 .ToList();

            var photos = new List<object>();
            foreach (var file in photoFiles)
            {
                try
                {
                    // Generate SAS URL for each photo (24 hours expiration)
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(file.Name, TimeSpan.FromHours(24));
                    
                    photos.Add(new
                    {
                        fileName = Path.GetFileName(file.Name),
                        filePath = file.Name,
                        fileSize = file.Size,
                        mimeType = file.ContentType,
                        lastModified = file.LastModified.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        photoUrl = sasUrl,
                        directory = photoPath
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "?? Failed to generate SAS URL for photo: {FileName}", file.Name);
                    
                    // Add photo without SAS URL rather than skip it
                    photos.Add(new
                    {
                        fileName = Path.GetFileName(file.Name),
                        filePath = file.Name,
                        fileSize = file.Size,
                        mimeType = file.ContentType,
                        lastModified = file.LastModified.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        photoUrl = (string?)null,
                        directory = photoPath,
                        error = "Failed to generate access URL"
                    });
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new
            {
                success = true,
                twinId = twinId,
                activityId = activityId,
                directory = photoPath,
                totalPhotos = photos.Count,
                photos = photos,
                message = $"Retrieved {photos.Count} photos for activity {activityId}"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Retrieved {Count} photos for Activity: {ActivityId}", photos.Count, activityId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting activity photos");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message,
                twinId = twinId,
                activityId = activityId
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Create error response for activity photo uploads
    /// </summary>
    private async Task<HttpResponseData> CreateActivityPhotoErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync(JsonSerializer.Serialize(new ActivityPhotoUploadResponse
        {
            Success = false,
            ErrorMessage = errorMessage
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return response;
    }

    /// <summary>
    /// Get boundary from content type for multipart parsing
    /// </summary>
    private static string GetBoundary(string contentType)
    {
        var elements = contentType.Split(' ');
        var element = elements.FirstOrDefault(e => e.StartsWith("boundary="));
        if (element != null)
        {
            return element.Substring("boundary=".Length).Trim('"');
        }
        return "";
    }

    /// <summary>
    /// Parse multipart form data
    /// </summary>
    private async Task<List<MultipartFormDataPart>> ParseMultipartDataAsync(byte[] data, string boundary)
    {
        var parts = new List<MultipartFormDataPart>();
        var boundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary);
        var endBoundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary + "--");

        var position = 0;
        var boundaryIndex = FindBytes(data, position, boundaryBytes);
        if (boundaryIndex == -1) return parts;

        position = boundaryIndex + boundaryBytes.Length;
        while (position < data.Length)
        {
            if (position + 1 < data.Length && data[position] == '\r' && data[position + 1] == '\n')
                position += 2;

            var headersEndBytes = System.Text.Encoding.UTF8.GetBytes("\r\n\r\n");
            var headersEnd = FindBytes(data, position, headersEndBytes);
            if (headersEnd == -1) break;

            var headersLength = headersEnd - position;
            var headersBytes = new byte[headersLength];
            Array.Copy(data, position, headersBytes, 0, headersLength);
            var headers = System.Text.Encoding.UTF8.GetString(headersBytes);
            position = headersEnd + 4;

            var nextBoundaryIndex = FindBytes(data, position, boundaryBytes);
            if (nextBoundaryIndex == -1) break;

            var contentLength = nextBoundaryIndex - position - 2;
            if (contentLength > 0)
            {
                var contentBytes = new byte[contentLength];
                Array.Copy(data, position, contentBytes, 0, contentLength);
                var part = ParseMultipartPart(headers, contentBytes);
                if (part != null)
                {
                    parts.Add(part);
                }
            }
            position = nextBoundaryIndex + boundaryBytes.Length;
            if (position < data.Length && data[position] == '-' && position + 1 < data.Length && data[position + 1] == '-')
                break;
        }
        return parts;
    }

    /// <summary>
    /// Parse individual multipart part
    /// </summary>
    private MultipartFormDataPart? ParseMultipartPart(string headers, byte[] content)
    {
        var lines = headers.Split('\n');
        string? name = null;
        string? fileName = null;
        string? contentType = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"name=""([^""]+)""");
                if (nameMatch.Success)
                    name = nameMatch.Groups[1].Value;

                var filenameMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"filename=""([^""]+)""");
                if (filenameMatch.Success)
                    fileName = filenameMatch.Groups[1].Value;
            }
            else if (trimmed.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            {
                contentType = trimmed.Substring("Content-Type:".Length).Trim();
            }
        }
        if (string.IsNullOrEmpty(name)) return null;
        return new MultipartFormDataPart
        {
            Name = name,
            FileName = fileName,
            ContentType = contentType,
            Data = content,
            StringValue = content.Length > 0 && IsTextContent(contentType) ? System.Text.Encoding.UTF8.GetString(content) : null
        };
    }

    /// <summary>
    /// Find byte pattern in array
    /// </summary>
    private static int FindBytes(byte[] data, int startPosition, byte[] pattern)
    {
        for (int i = startPosition; i <= data.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }
        return -1;
    }

    /// <summary>
    /// Check if content type is text
    /// </summary>
    private static bool IsTextContent(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return true;
        return contentType.StartsWith("text/") ||
               contentType.Contains("application/x-www-form-urlencoded") ||
               contentType.Contains("application/json");
    }

    /// <summary>
    /// Validate image file
    /// </summary>
    private static bool IsValidImageFile(string fileName, byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        if (!validExtensions.Contains(extension))
            return false;

        var detectedExtension = GetFileExtensionFromBytes(fileBytes);
        var validDetectedExtensions = new[] { "jpg", "jpeg", "png", "gif", "webp", "bmp" };
        return validDetectedExtensions.Contains(detectedExtension);
    }

    /// <summary>
    /// Get file extension from byte signature
    /// </summary>
    private static string GetFileExtensionFromBytes(byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            return "jpg";

        if (fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF)
            return "jpg";

        if (fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
            return "png";

        if (fileBytes.Length >= 6 && fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46)
            return "gif";

        if (fileBytes.Length >= 12 && fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 &&
            fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50)
            return "webp";

        return "jpg";
    }

    /// <summary>
    /// Get MIME type from file extension
    /// </summary>
    private static string GetMimeTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "tiff" or "tif" => "image/tiff",
            _ => "image/jpeg"
        };
    }

    /// <summary>
    /// Parse query parameters for travel filtering
    /// </summary>
    /// <param name="req">HTTP request data</param>
    /// <returns>Travel query object</returns>
    private TravelQuery ParseTravelQuery(HttpRequestData req)
    {
        var query = new TravelQuery();

        try
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            // Status filter
            if (queryParams["estado"] != null && Enum.TryParse<TravelStatus>(queryParams["estado"], true, out var estado))
            {
                query.Estado = estado;
            }

            // Travel type filter
            if (queryParams["tipoViaje"] != null && Enum.TryParse<TravelType>(queryParams["tipoViaje"], true, out var tipoViaje))
            {
                query.TipoViaje = tipoViaje;
            }

            // Country filter
            if (!string.IsNullOrEmpty(queryParams["paisDestino"]))
            {
                query.PaisDestino = queryParams["paisDestino"];
            }

            // City filter
            if (!string.IsNullOrEmpty(queryParams["ciudadDestino"]))
            {
                query.CiudadDestino = queryParams["ciudadDestino"];
            }

            // Date range filters
            if (DateTime.TryParse(queryParams["fechaDesde"], out var fechaDesde))
            {
                query.FechaDesde = fechaDesde;
            }

            if (DateTime.TryParse(queryParams["fechaHasta"], out var fechaHasta))
            {
                query.FechaHasta = fechaHasta;
            }

            // Rating filter
            if (int.TryParse(queryParams["calificacionMin"], out var calificacionMin))
            {
                query.CalificacionMin = calificacionMin;
            }

            // Budget filter
            if (decimal.TryParse(queryParams["presupuestoMax"], out var presupuestoMax))
            {
                query.PresupuestoMax = presupuestoMax;
            }

            // Search term
            if (!string.IsNullOrEmpty(queryParams["searchTerm"]))
            {
                query.SearchTerm = queryParams["searchTerm"];
            }

            // Pagination
            if (int.TryParse(queryParams["page"], out var page) && page > 0)
            {
                query.Page = page;
            }

            if (int.TryParse(queryParams["pageSize"], out var pageSize) && pageSize > 0)
            {
                query.PageSize = Math.Min(pageSize, 100); // Max 100 items per page
            }

            // Sorting
            if (!string.IsNullOrEmpty(queryParams["sortBy"]))
            {
                query.SortBy = queryParams["sortBy"];
            }

            if (!string.IsNullOrEmpty(queryParams["sortDirection"]))
            {
                query.SortDirection = queryParams["sortDirection"];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error parsing query parameters, using defaults");
        }

        return query;
    }

    /// <summary>
    /// Add CORS headers to response
    /// </summary>
    /// <param name="response">HTTP response</param>
    /// <param name="request">HTTP request</param>
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

/// <summary>
/// Response model for activity photo upload operations
/// </summary>
public class ActivityPhotoUploadResponse : BasePhotoUploadResponse
{
    public string ActivityId { get; set; } = string.Empty;
}