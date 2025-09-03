using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TwinFx.Services;

namespace TwinFx.Functions;

/// <summary>
/// Request model for family photo upload
/// </summary>
public class FamilyPhotoUploadRequest
{
    /// <summary>
    /// Base64 encoded photo data
    /// </summary>
    [JsonPropertyName("photoData")]
    public string PhotoData { get; set; } = string.Empty;

    /// <summary>
    /// Original filename from the UI upload
    /// </summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }
}

public class TwinFamilyFunctions
{
    private readonly ILogger<TwinFamilyFunctions> _logger;
    private readonly IConfiguration _configuration;

    public TwinFamilyFunctions(ILogger<TwinFamilyFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for family routes
    [Function("FamilyOptions")]
    public async Task<HttpResponseData> HandleFamilyOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"👨‍👩‍👧‍👦 OPTIONS preflight request for twins/{twinId}/family");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for specific family routes
    [Function("FamilyByIdOptions")]
    public async Task<HttpResponseData> HandleFamilyByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family/{familyId}")] HttpRequestData req,
        string twinId, string familyId)
    {
        _logger.LogInformation($"👤 OPTIONS preflight request for twins/{twinId}/family/{familyId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for family photo upload
    [Function("FamilyPhotoUploadOptions")]
    public async Task<HttpResponseData> HandleFamilyPhotoUploadOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family/{familyId}/upload-photo")] HttpRequestData req,
        string twinId, string familyId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for family photo upload: twins/{twinId}/family/{familyId}/upload-photo");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("CreateFamily")]
    public async Task<HttpResponseData> CreateFamily(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/family")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("👨‍👩‍👧‍👦 CreateFamily function triggered");

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
            var familyData = JsonSerializer.Deserialize<FamilyData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Log the deserialized object as string for debugging
            if (familyData != null)
            {
                var serializedFamilyData = JsonSerializer.Serialize(familyData, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });
                _logger.LogInformation($"📋 Deserialized FamilyData: {serializedFamilyData}");
            }
            else
            {
                _logger.LogWarning("⚠️ FamilyData deserialized to null");
            }

            if (familyData == null)
            {
                _logger.LogError("❌ Failed to parse family data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid family data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(familyData.Nombre))
            {
                _logger.LogError("❌ Family member name is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Family member name is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(familyData.Parentesco))
            {
                _logger.LogError("❌ Parentesco (relationship) is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Parentesco (relationship) is required"
                }));
                return badResponse;
            }

            // Set Twin ID and generate family ID if not provided
            familyData.TwinID = twinId;
            if (string.IsNullOrEmpty(familyData.Id))
            {
                familyData.Id = Guid.NewGuid().ToString();
            }

            _logger.LogInformation($"👨‍👩‍👧‍👦 Creating family member: {familyData.Nombre} {familyData.Apellido} ({familyData.Parentesco}) for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Create the family member record
            var success = await cosmosService.CreateFamilyAsync(familyData);

            var response = req.CreateResponse(success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Family member created successfully: {familyData.Nombre} {familyData.Apellido} ({familyData.Parentesco})");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    family = familyData,
                    message = "Family member created successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to create family member: {familyData.Nombre} {familyData.Apellido} ({familyData.Parentesco})");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to create family member in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating family member");

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

    [Function("GetFamilyByTwinId")]
    public async Task<HttpResponseData> GetFamilyByTwinId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/family")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("👨‍👩‍👧‍👦 GetFamilyByTwinId function triggered");

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

            _logger.LogInformation($"👨‍👩‍👧‍👦 Getting family members for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Get family members by Twin ID
            var familyMembers = await cosmosService.GetFamilyByTwinIdAsync(twinId);

            // Create DataLake client to generate SAS URLs for family photos
            var dataLakeFactory = new DataLakeClientFactory(LoggerFactory.Create(b => b.AddConsole()), _configuration);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Process each family member to generate SAS URLs for their photos
            foreach (var familyMember in familyMembers)
            {
                try
                {
                    // Try different extensions for photo_{familyId}.{ext}
                    var photoExtensions = new[] { "JPG", "jpg", "PNG", "png", "JPEG", "jpeg", "gif", "GIF", "webp", "WEBP" };
                    string? foundPhotoPath = null;
                    
                    foreach (var ext in photoExtensions)
                    {
                        var photoPath = $"familia/{familyMember.Id}/photo_{familyMember.Id}.{ext}";
                        
                        try
                        {
                            var fileInfo = await dataLakeClient.GetFileInfoAsync(photoPath);
                            if (fileInfo != null)
                            {
                                foundPhotoPath = photoPath;
                                break; // Found the photo, stop searching
                            }
                        }
                        catch
                        {
                            // File doesn't exist with this extension, try next
                            continue;
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(foundPhotoPath))
                    {
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(foundPhotoPath, TimeSpan.FromHours(24));
                        familyMember.UrlFoto = sasUrl ?? "";
                        _logger.LogInformation($"📸 Generated SAS URL for family member {familyMember.Nombre}: {foundPhotoPath}");
                    }
                    else
                    {
                        familyMember.UrlFoto = "";
                        _logger.LogInformation($"📭 No photo found for family member {familyMember.Nombre}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"⚠️ Failed to generate SAS URL for family member {familyMember.Nombre} ({familyMember.Id})");
                    familyMember.UrlFoto = "";
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation($"✅ Found {familyMembers.Count} family members for Twin ID: {twinId}");
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                family = familyMembers,
                twinId = twinId,
                count = familyMembers.Count
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting family members");

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

    [Function("GetFamilyById")]
    public async Task<HttpResponseData> GetFamilyById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/family/{familyId}")] HttpRequestData req,
        string twinId, string familyId)
    {
        _logger.LogInformation("👤 GetFamilyById function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(familyId))
            {
                _logger.LogError("❌ Twin ID and Family ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Family ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"👤 Getting family member {familyId} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Get family member by ID
            var familyMember = await cosmosService.GetFamilyByIdAsync(familyId, twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (familyMember == null)
            {
                _logger.LogInformation($"📭 No family member found with ID: {familyId} for Twin ID: {twinId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Family member not found",
                    familyId = familyId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation($"✅ Family member found: {familyMember.Nombre} {familyMember.Apellido} ({familyMember.Parentesco})");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    family = familyMember,
                    familyId = familyId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting family member by ID");

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

    [Function("UpdateFamily")]
    public async Task<HttpResponseData> UpdateFamily(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/family/{familyId}")] HttpRequestData req,
        string twinId, string familyId)
    {
        _logger.LogInformation("👤 UpdateFamily function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(familyId))
            {
                _logger.LogError("❌ Twin ID and Family ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Family ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");
            _logger.LogInformation($"🔍 RAW Request body: {requestBody}");

            // Parse JSON request
            var updateData = JsonSerializer.Deserialize<FamilyData>(requestBody, new JsonSerializerOptions
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
                _logger.LogError("❌ Failed to parse family update data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid family update data format"
                }));
                return badResponse;
            }

            // Ensure the IDs match
            updateData.Id = familyId;
            updateData.TwinID = twinId;

            _logger.LogInformation($"👤 Updating family member {familyId}: {updateData.Nombre} {updateData.Apellido} ({updateData.Parentesco}) for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Update the family member
            var success = await cosmosService.UpdateFamilyAsync(updateData);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Family member updated successfully: {updateData.Nombre} {updateData.Apellido} ({updateData.Parentesco})");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    family = updateData,
                    message = "Family member updated successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to update family member: {familyId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to update family member in database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating family member");

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

    [Function("DeleteFamily")]
    public async Task<HttpResponseData> DeleteFamily(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/family/{familyId}")] HttpRequestData req,
        string twinId, string familyId)
    {
        _logger.LogInformation("🗑️ DeleteFamily function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(familyId))
            {
                _logger.LogError("❌ Twin ID and Family ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Family ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"🗑️ Deleting family member {familyId} for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Delete the family member
            var success = await cosmosService.DeleteFamilyAsync(familyId, twinId);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Family member deleted successfully: {familyId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    familyId = familyId,
                    twinId = twinId,
                    message = "Family member deleted successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to delete family member: {familyId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to delete family member from database"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting family member");

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

    [Function("UploadFamilyPhoto")]
    public async Task<HttpResponseData> UploadFamilyPhoto(
     [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/family/{familyId}/upload-photo")] HttpRequestData req,
     string twinId, string familyId)
    {
        _logger.LogInformation("📸 UploadFamilyPhoto function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            // Validar parámetros de entrada  
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Validar Content-Type usando la API correcta de headers
            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (contentTypeHeader.Key == null || !contentTypeHeader.Value.Any(v => v.Contains("multipart/form-data")))
            {
                return await CreateErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeHeader.Value.FirstOrDefault() ?? "";
            var boundary = GetBoundary(contentType);

            if (string.IsNullOrEmpty(boundary))
            {
                return await CreateErrorResponse(req, "Invalid boundary", HttpStatusCode.BadRequest);
            }

            // Leer el cuerpo como bytes para preservar datos binarios
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();

            // Procesar el contenido multipart  
            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            var photoPart = parts.FirstOrDefault(p => p.Name == "photo");

            if (photoPart == null || photoPart.Data == null || photoPart.Data.Length == 0)
            {
                return await CreateErrorResponse(req, "No file data found in request", HttpStatusCode.BadRequest);
            }

            // Extraer familyId del FormData si no está en la URL o está vacío
            string actualFamilyId = familyId;
            if (string.IsNullOrEmpty(familyId) || familyId == "{familyId}")
            {
                var familyIdPart = parts.FirstOrDefault(p => p.Name == "familyMemberId" || p.Name == "familyId");
                if (familyIdPart != null && !string.IsNullOrEmpty(familyIdPart.StringValue))
                {
                    actualFamilyId = familyIdPart.StringValue;
                    _logger.LogInformation($"📋 Family ID extracted from FormData: {actualFamilyId}");
                }
                else
                {
                    return await CreateErrorResponse(req, "Family ID not found in URL path or FormData", HttpStatusCode.BadRequest);
                }
            }

            var photoBytes = photoPart.Data;

            // Extraer nombre de archivo personalizado del FormData o usar el del archivo
            var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
            var customFileName = fileNamePart?.StringValue;
            var fileName = customFileName ?? photoPart.FileName ?? $"photo_{actualFamilyId}";

            _logger.LogInformation($"📏 Photo details: Size={photoBytes.Length} bytes, OriginalName={photoPart.FileName}, FinalName={fileName}");
            _logger.LogInformation($"👥 Using Family ID: {actualFamilyId}");

            // Validar que sea una imagen
            if (!IsValidImageFile(fileName, photoBytes))
            {
                return await CreateErrorResponse(req, "Invalid image file format", HttpStatusCode.BadRequest);
            }

            // Subir el archivo a DataLake  
            var dataLakeClient = new DataLakeClientFactory(LoggerFactory.Create(b => b.AddConsole()), _configuration).CreateClient(twinId);
            var fileExtension = Path.GetExtension(fileName);
            var cleanFileName = Path.GetFileNameWithoutExtension(fileName) + fileExtension;
            var filePath = $"familia/{actualFamilyId}/{cleanFileName}";

            _logger.LogInformation($"📂 Upload path: {filePath}");

            // Borrar cualquier archivo existente con el mismo nombre base pero diferente extensión
            try
            {
                var baseFileName = Path.GetFileNameWithoutExtension(cleanFileName);
                var extensionsToCheck = new[] { "JPG", "jpg", "PNG", "png", "JPEG", "jpeg", "gif", "GIF", "webp", "WEBP", "bmp", "BMP" };
                
                foreach (var ext in extensionsToCheck)
                {
                    var existingFilePath = $"familia/{actualFamilyId}/{baseFileName}.{ext}";
                    
                    try
                    {
                        var existingFileInfo = await dataLakeClient.GetFileInfoAsync(existingFilePath);
                        if (existingFileInfo != null)
                        {
                            var deleteSuccess = await dataLakeClient.DeleteFileAsync(existingFilePath);
                            if (deleteSuccess)
                            {
                                _logger.LogInformation($"🗑️ Deleted existing file: {existingFilePath}");
                            }
                            else
                            {
                                _logger.LogWarning($"⚠️ Failed to delete existing file: {existingFilePath}");
                            }
                        }
                    }
                    catch
                    {
                        // File doesn't exist with this extension, continue
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Warning: Could not clean existing files, but continuing with upload");
            }

            using var photoStream = new MemoryStream(photoBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                $"familia/{actualFamilyId}",
                cleanFileName,
                photoStream,
                GetMimeTypeFromExtension(fileExtension)
            );

            if (!uploadSuccess)
            {
                return await CreateErrorResponse(req, "Failed to upload photo to storage", HttpStatusCode.InternalServerError);
            }

            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

            _logger.LogInformation($"✅ Photo uploaded successfully in {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new
            {
                photoUrl = sasUrl,
                fileName = cleanFileName,
                filePath = filePath,
                familyId = actualFamilyId
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in UploadFamilyPhoto");
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<List<MultipartFormDataPart>> ParseMultipartDataAsync(byte[] bodyBytes, string boundary)
    {
        var parts = new List<MultipartFormDataPart>();
        var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
        var parts_list = SplitByteArray(bodyBytes, boundaryBytes);

        foreach (var partBytes in parts_list)
        {
            if (partBytes.Length == 0)
                continue;

            // Find header/content separation (\r\n\r\n)
            var headerSeparator = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };
            var headerEndIndex = FindBytePattern(partBytes, headerSeparator);

            if (headerEndIndex == -1)
                continue;

            var headerBytes = partBytes.Take(headerEndIndex).ToArray();
            var contentBytes = partBytes.Skip(headerEndIndex + 4).ToArray();

            // Remove trailing CRLF and boundary markers
            while (contentBytes.Length > 0 &&
                   (contentBytes[contentBytes.Length - 1] == 0x0A ||
                    contentBytes[contentBytes.Length - 1] == 0x0D ||
                    contentBytes[contentBytes.Length - 1] == 0x2D)) // dash character
            {
                Array.Resize(ref contentBytes, contentBytes.Length - 1);
            }

            var headers = Encoding.UTF8.GetString(headerBytes);
            var part = new MultipartFormDataPart();

            // Parse Content-Disposition header
            var dispositionMatch = Regex.Match(headers, @"Content-Disposition:\s*form-data;\s*name=""([^""]+)""(?:;\s*filename=""([^""]+)"")?", RegexOptions.IgnoreCase);
            if (dispositionMatch.Success)
            {
                part.Name = dispositionMatch.Groups[1].Value;
                if (dispositionMatch.Groups[2].Success)
                    part.FileName = dispositionMatch.Groups[2].Value;
            }

            // Parse Content-Type header
            var contentTypeMatch = Regex.Match(headers, @"Content-Type:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (contentTypeMatch.Success)
                part.ContentType = contentTypeMatch.Groups[1].Value.Trim();

            // Set data based on content type
            if (!string.IsNullOrEmpty(part.FileName) ||
                (!string.IsNullOrEmpty(part.ContentType) && part.ContentType.StartsWith("image/")))
            {
                part.Data = contentBytes;
            }
            else
            {
                part.StringValue = Encoding.UTF8.GetString(contentBytes).Trim();
            }

            parts.Add(part);
        }

        return parts;
    }

    private List<byte[]> SplitByteArray(byte[] source, byte[] separator)
    {
        var result = new List<byte[]>();
        var index = 0;

        while (index < source.Length)
        {
            var nextIndex = FindBytePattern(source, separator, index);
            if (nextIndex == -1)
            {
                if (index < source.Length)
                    result.Add(source.Skip(index).ToArray());
                break;
            }

            if (nextIndex > index)
                result.Add(source.Skip(index).Take(nextIndex - index).ToArray());

            index = nextIndex + separator.Length;
        }

        return result;
    }

    private int FindBytePattern(byte[] source, byte[] pattern, int startIndex = 0)
    {
        for (int i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private bool IsValidImageFile(string fileName, byte[] fileData)
    {
        if (string.IsNullOrEmpty(fileName) || fileData == null || fileData.Length == 0)
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

        if (!validExtensions.Contains(extension))
            return false;

        // Check file signature (magic numbers)
        if (fileData.Length < 4)
            return false;

        // JPEG: FF D8 FF
        if (fileData[0] == 0xFF && fileData[1] == 0xD8 && fileData[2] == 0xFF)
            return true;

        // PNG: 89 50 4E 47
        if (fileData[0] == 0x89 && fileData[1] == 0x50 && fileData[2] == 0x4E && fileData[3] == 0x47)
            return true;

        // GIF: 47 49 46 38
        if (fileData[0] == 0x47 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x38)
            return true;

        // BMP: 42 4D
        if (fileData[0] == 0x42 && fileData[1] == 0x4D)
            return true;

        return true; // Allow other formats for now
    }

    private string GetBoundary(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return string.Empty;

        var boundaryMatch = Regex.Match(contentType, @"boundary=([^;]+)", RegexOptions.IgnoreCase);
        return boundaryMatch.Success ? boundaryMatch.Groups[1].Value.Trim('"', ' ') : string.Empty;
    }

    public class MultipartFormDataPart
    {
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public byte[]? Data { get; set; }
        public string StringValue { get; set; } = string.Empty;
    }
    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            success = false,
            errorMessage
        }));

        return response;
    }
     
     
    public class MultipartPart
    {
        public string Name { get; set; } = string.Empty;
        public byte[]? Data { get; set; }
        public string? FileName { get; set; }
    }
 
    private static string ExtractBoundary(string contentType)
    {
        var boundary = contentType.Split(';')
                                  .FirstOrDefault(x => x.Trim().StartsWith("boundary="))
                                  ?.Split('=')[1]
                                  ?.Trim('"');
        return boundary;
    }
     

    private class MultipartData
    {
        public string Name { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? Value { get; set; }
        public byte[]? Data { get; set; }
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

    private static string GetFileExtensionFromBytes(byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            return "jpg";

        // JPEG signature
        if (fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF)
            return "jpg";
        
        // PNG signature
        if (fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
            return "png";
        
        // GIF signature
        if (fileBytes.Length >= 6 && fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46)
            return "gif";

        // WEBP signature (RIFF format)
        if (fileBytes.Length >= 12 && fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 && 
            fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50)
            return "webp";
        
        return "jpg"; // Default fallback
    }

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
}