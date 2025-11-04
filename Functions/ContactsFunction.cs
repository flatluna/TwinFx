using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsLibrary.Models;
using TwinFx.Services;

namespace TwinFx.Functions;

public class ContactsFunction
{
    private readonly ILogger<ContactsFunction> _logger;
    private readonly IConfiguration _configuration;

    public ContactsFunction(ILogger<ContactsFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for contacts routes
    [Function("ContactsOptions")]
    public async Task<HttpResponseData> HandleContactsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/contacts")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for twins/{twinId}/contacts");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for specific contact routes
    [Function("ContactByIdOptions")]
    public async Task<HttpResponseData> HandleContactByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/contacts/{contactId}")] HttpRequestData req,
        string twinId, string contactId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for twins/{twinId}/contacts/{contactId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("CreateContact")]
    public async Task<HttpResponseData> CreateContact(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/contacts")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? CreateContact function triggered");

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
            _logger.LogInformation($"?? Request body length: {requestBody.Length} characters");

            // Parse JSON request
            var contactData = JsonSerializer.Deserialize<ContactData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (contactData == null)
            {
                _logger.LogError("? Failed to parse contact data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid contact data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(contactData.Nombre))
            {
                _logger.LogError("? Contact name is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Contact name is required"
                }));
                return badResponse;
            }

            // Set Twin ID and generate contact ID if not provided
            contactData.TwinID = twinId;
            if (string.IsNullOrEmpty(contactData.Id))
            {
                contactData.Id = Guid.NewGuid().ToString();
            }

            _logger.LogInformation($"?? Creating contact: {contactData.Nombre} {contactData.Apellido} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Create the contact
            var success = await cosmosService.CreateContactAsync(contactData);

            var response = req.CreateResponse(success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"? Contact created successfully: {contactData.Nombre} {contactData.Apellido}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    contact = contactData,
                    message = "Contact created successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"? Failed to create contact: {contactData.Nombre} {contactData.Apellido}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to create contact in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error creating contact");

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

    [Function("GetContactsByTwinId")]
    public async Task<HttpResponseData> GetContactsByTwinId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/contacts")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? GetContactsByTwinId function triggered");

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

            _logger.LogInformation($"?? Getting contacts for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Get contacts by Twin ID
            var contacts = await cosmosService.GetContactsByTwinIdAsync(twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation($"? Found {contacts.Count} contacts for Twin ID: {twinId}");
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                contacts = contacts,
                twinId = twinId,
                count = contacts.Count
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting contacts");

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

    [Function("GetContactById")]
    public async Task<HttpResponseData> GetContactById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/contacts/{contactId}")] HttpRequestData req,
        string twinId, string contactId)
    {
        _logger.LogInformation("?? GetContactById function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(contactId))
            {
                _logger.LogError("? Twin ID and Contact ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Contact ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Getting contact {contactId} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Get contact by ID
            var contact = await cosmosService.GetContactByIdAsync(contactId, twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (contact == null)
            {
                _logger.LogInformation($"?? No contact found with ID: {contactId} for Twin ID: {twinId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Contact not found",
                    contactId = contactId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation($"? Contact found: {contact.Nombre} {contact.Apellido}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    contact = contact,
                    contactId = contactId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting contact by ID");

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

    [Function("UpdateContact")]
    public async Task<HttpResponseData> UpdateContact(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/contacts/{contactId}")] HttpRequestData req,
        string twinId, string contactId)
    {
        _logger.LogInformation("?? UpdateContact function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(contactId))
            {
                _logger.LogError("? Twin ID and Contact ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Contact ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body length: {requestBody.Length} characters");

            // Parse JSON request
            var updateData = JsonSerializer.Deserialize<ContactData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateData == null)
            {
                _logger.LogError("? Failed to parse contact update data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid contact update data format"
                }));
                return badResponse;
            }

            // Ensure the IDs match
            updateData.Id = contactId;
            updateData.TwinID = twinId;

            _logger.LogInformation($"?? Updating contact {contactId}: {updateData.Nombre} {updateData.Apellido} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Update the contact
            var success = await cosmosService.UpdateContactAsync(updateData);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"? Contact updated successfully: {updateData.Nombre} {updateData.Apellido}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    contact = updateData,
                    message = "Contact updated successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"? Failed to update contact: {contactId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to update contact in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error updating contact");

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

    [Function("DeleteContact")]
    public async Task<HttpResponseData> DeleteContact(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/contacts/{contactId}")] HttpRequestData req,
        string twinId, string contactId)
    {
        _logger.LogInformation("??? DeleteContact function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(contactId))
            {
                _logger.LogError("? Twin ID and Contact ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Contact ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"??? Deleting contact {contactId} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = _configuration.CreateCosmosService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

            // Delete the contact
            var success = await cosmosService.DeleteContactAsync(contactId, twinId);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"? Contact deleted successfully: {contactId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    contactId = contactId,
                    twinId = twinId,
                    message = "Contact deleted successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"? Failed to delete contact: {contactId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to delete contact from database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting contact");

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