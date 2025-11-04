using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using TwinAgentsLibrary.Models;
using TwinFx.Agents; // Add this for PhotoFormData and FamilyFotosAgent
using TwinFx.Functions;
using TwinFx.Models;
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

/// <summary>
/// Photo category enumeration
/// </summary>
public enum PhotoCategory
{
    Familia,
    Eventos,
    Vacaciones,
    Celebraciones,
    Cotidiano
}

/// <summary>
/// Photo form data model
/// </summary>
public class PhotoFormData
{

    public string FileName { get; set; } = ""; 
    public string TimeTaken { get; set; } = "";

    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public string DateTaken { get; set; } = "";
    public string Location { get; set; } = "";
    public string Country { get; set; } = "";
    public string Place { get; set; } = "";
    public string PeopleInPhoto { get; set; } = "";
    public string Tags { get; set; } = "";
    public PhotoCategory Category { get; set; } = PhotoCategory.Familia;
    public string EventType { get; set; } = "";
}

public class TwinFamilyFunctions
{
    private readonly ILogger<TwinFamilyFunctions> _logger;
    private readonly IConfiguration _configuration;
    private readonly CosmosDbService _cosmosService;
    private readonly Services.PicturesFamilySearchIndex _picturesFamilySearchIndex;

    public TwinFamilyFunctions(ILogger<TwinFamilyFunctions> logger, IConfiguration configuration, CosmosDbService cosmosService, Services.PicturesFamilySearchIndex picturesFamilySearchIndex)
    {
        _logger = logger;
        _configuration = configuration;
        _cosmosService = cosmosService;
        _picturesFamilySearchIndex = picturesFamilySearchIndex;
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

    // OPTIONS handler for family photos route
    [Function("FamilyPhotosOptions")]
    public async Task<HttpResponseData> HandleFamilyPhotosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family-photos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for twins/{twinId}/family-photos");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // OPTIONS handler for family photos search route
    [Function("SearchFamilyPicturesOptions")]
    public async Task<HttpResponseData> HandleSearchFamilyPicturesOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/search-family-pictures")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"🔍 OPTIONS preflight request for twins/{twinId}/search-family-pictures");

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

            // Unwrap CosmosService.CreateFamilyAsync to handle exceptions directly
            var success = false;
            try
            {
                success = await _cosmosService.CreateFamilyAsync(familyData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating family member in Cosmos DB");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Error creating family member in database"
                }));
                return errorResponse;
            }

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

            // Use injected Cosmos DB service
            var familyMembers = await _cosmosService.GetFamilyByTwinIdAsync(twinId);

            // Create DataLake client to generate SAS URLs for family photos
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
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

            // Use injected Cosmos DB service
            var familyMember = await _cosmosService.GetFamilyByIdAsync(familyId, twinId);

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

            // Use injected Cosmos DB service
            var success = await _cosmosService.UpdateFamilyAsync(updateData);

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

            // Use injected Cosmos DB service
            var success = await _cosmosService.DeleteFamilyAsync(familyId, twinId);

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

            // Extraer familyId del FormData si no está en la URL or está vacío
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

            // Extraer nombre de archivo personalizado del FormData o usar el del archivo
            var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
            var customFileName = fileNamePart?.StringValue;
            var fileName = customFileName ?? photoPart.FileName ?? $"photo_{actualFamilyId}";

            _logger.LogInformation($"📏 Photo details: Size={photoPart.Data.Length} bytes, OriginalName={photoPart.FileName}, FinalName={fileName}");
            _logger.LogInformation($"👥 Using Family ID: {actualFamilyId}");

            // Validar que sea una imagen
            if (!IsValidImageFile(fileName, photoPart.Data))
            {
                return await CreateErrorResponse(req, "Invalid image file format", HttpStatusCode.BadRequest);
            }

            // Subir el archivo a DataLake  
            var dataLakeClient = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole())).CreateClient(twinId);
            var fileExtension = Path.GetExtension(fileName);
            var cleanFileName = Path.GetFileNameWithoutExtension(fileName) + fileExtension;
            var filePath = $"familyPhotos";

            _logger.LogInformation($"📂 Upload path: {filePath}");

            using var photoStream = new MemoryStream(photoPart.Data);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                $"familyPhotos",
                fileName,
                photoStream,
                GetMimeTypeFromExtension(fileExtension)
            );

            if (!uploadSuccess)
            {
                return await CreateErrorResponse(req, "Failed to upload photo to storage", HttpStatusCode.InternalServerError);
            }
            filePath = $"familyPhotos/{fileName}";
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

            // ✅ EXTRAER CADA CAMPO INDIVIDUALMENTE DEL FORMDATA
            var descriptionPart = parts.FirstOrDefault(p => p.Name == "Description");
            var dateTakenPart = parts.FirstOrDefault(p => p.Name == "DateTaken");
            var locationPart = parts.FirstOrDefault(p => p.Name == "Location");
            var countryPart = parts.FirstOrDefault(p => p.Name == "Country");
            var placePart = parts.FirstOrDefault(p => p.Name == "Place");
            var peopleInPhotoPart = parts.FirstOrDefault(p => p.Name == "PeopleInPhoto");
            var tagsPart = parts.FirstOrDefault(p => p.Name == "Tags");
            var categoryPart = parts.FirstOrDefault(p => p.Name == "Category");
            var eventTypePart = parts.FirstOrDefault(p => p.Name == "EventType");
            var fileNameFormPart = parts.FirstOrDefault(p => p.Name == "FileName");
            var timeTakenPart = parts.FirstOrDefault(p => p.Name == "TimeTaken");
            var pathPart = parts.FirstOrDefault(p => p.Name == "Path");

            // Crear PhotoFormData con los datos extraídos del FormData
            var photoFormData = new PhotoFormData
            {
                FileName = fileNameFormPart?.StringValue ?? cleanFileName,
                TimeTaken = timeTakenPart?.StringValue ?? "",
                Path = pathPart?.StringValue ?? "familyPhotos",
                Description = descriptionPart?.StringValue ?? "",
                DateTaken = dateTakenPart?.StringValue ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Location = locationPart?.StringValue ?? "",
                Country = countryPart?.StringValue ?? "",
                Place = placePart?.StringValue ?? "",
                PeopleInPhoto = peopleInPhotoPart?.StringValue ?? "",
                Tags = tagsPart?.StringValue ?? "",
                Category = Enum.TryParse<PhotoCategory>(categoryPart?.StringValue ?? "Familia", true, out var cat) ? cat : PhotoCategory.Familia,
                EventType = eventTypePart?.StringValue ?? ""
            };

            _logger.LogInformation("📋 PhotoFormData extracted from FormData fields: Description='{Description}', DateTaken='{DateTaken}', Category='{Category}', PeopleInPhoto='{PeopleInPhoto}'", 
                photoFormData.Description, photoFormData.DateTaken, photoFormData.Category, photoFormData.PeopleInPhoto);

            // *** FUNCIONALIDAD: Extraer userDescription del formulario ***
            var userDescriptionPart = parts.FirstOrDefault(p => p.Name == "userDescription");
            var userDescription = userDescriptionPart?.StringValue ?? photoFormData.Description;

            _logger.LogInformation("🤖 Starting AI photo analysis...");

            // Crear instancia del FamilyFotosAgent con configuración e logger adecuados
            var familyFotosLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Agents.FamilyFotosAgent>();
            var familyFotosAgent = new Agents.FamilyFotosAgent(familyFotosLogger, _configuration);
            
            ImageAI? imageAI = null;
            try
            {
                // ✅ LLAMAR AL ANÁLISIS DE IA CON LA URL SAS Y LOS DATOS DE LA FOTO CORRECTOS
                imageAI = await familyFotosAgent.AnalyzePhotoAsync(sasUrl, photoFormData, "Family", userDescription);
                _logger.LogInformation("✅ AI photo analysis completed successfully");

                // *** NUEVA FUNCIONALIDAD: Guardar ImageAI en Cosmos DB ***
                if (imageAI != null)
                {
                    // Asignar TwinID e ID al objeto ImageAI
                    imageAI.TwinID = twinId;
                    imageAI.Fecha = photoFormData.DateTaken;
                    imageAI.Hora = photoFormData.TimeTaken;
                    if (string.IsNullOrEmpty(imageAI.id))
                    {
                        imageAI.id = Guid.NewGuid().ToString();
                    }

                    // ✅ NUEVO: Asignar FileName y Url para poder generar SAS URLs más tarde
                    imageAI.FileName = cleanFileName; // Nombre del archivo subido
                    imageAI.Url = sasUrl; // URL SAS inicial (se regenerará cuando sea necesario)

                    // Crear instancia del servicio FamilyFotoscosmoDB
                    var familyPhotosService = CreateFamilyPhotosService();
                    imageAI.Path = "familyPhotos";
                    // Agregar using
                    
                    
                    // En el método donde quieras hacer la conversión:
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(imageAI.DetailsHTML);
                    imageAI.DescripcionDetallada = htmlDoc.DocumentNode.InnerText;
                    
                    // Calcular tokens aproximados (estimación: 1 token ≈ 4 caracteres)
                    imageAI.TotalTokensDescripcionDetallada = (int)Math.Ceiling(imageAI.DescripcionDetallada.Length / 4.0);

                    imageAI.Category = photoFormData.Category.ToString();
                    imageAI.EventType = photoFormData.EventType;
                    imageAI.DescripcionUsuario = photoFormData.Description;
                    imageAI.Places = photoFormData.Place;
                    imageAI.People = photoFormData.PeopleInPhoto;
                    imageAI.Etiquetas = photoFormData.Tags;
                    // Guardar el análisis de ImageAI en Cosmos DB
                    bool saveSuccess = true;
                    // saveSuccess = await familyPhotosService.SaveImageAIAsync(imageAI);

                    if (saveSuccess)
                    {
                        _logger.LogInformation("✅ ImageAI analysis saved successfully to Cosmos DB with ID: {ImageAIId}", imageAI.id);
                        
                        // *** NUEVA FUNCIONALIDAD: Indexar la foto en Azure AI Search ***
                        try
                        {
                            _logger.LogInformation("🔍 Starting Azure Search indexing for picture: {PictureId}", imageAI.id);
                            var indexResult = await IndexPictureUploadDocumentAsync(imageAI, (DateTime.UtcNow - startTime).TotalMilliseconds);
                            
                            if (indexResult.Success)
                            {
                                _logger.LogInformation("✅ Picture indexed successfully in Azure Search: DocumentId={DocumentId}", indexResult.DocumentId);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Failed to index picture in Azure Search: {Error}", indexResult.Error);
                            }
                        }
                        catch (Exception indexEx)
                        {
                            _logger.LogWarning(indexEx, "⚠️ Azure Search indexing failed, but continuing with photo upload");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save ImageAI analysis to Cosmos DB, but continuing with photo upload");
                    }
                }
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "⚠️ AI photo analysis failed, continuing without analysis");
                // Continuar sin análisis si falla
            }

            // 🚀 NUEVA FUNCIONALIDAD: Indexar foto en Azure AI Search
            

            _logger.LogInformation($"✅ Photo uploaded successfully in {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new
            {
                photoUrl = sasUrl,
                fileName = cleanFileName,
                filePath = filePath,
                familyId = actualFamilyId,
                photoFormData = photoFormData, // ✅ AGREGADO: Datos del formulario
                userDescription = userDescription,
                aiAnalysis = imageAI != null ? new
                {
                    descripcionGenerica = imageAI.DescripcionGenerica,
                    detailsHTML = imageAI.DetailsHTML,
                    success = true
                } : (object)new { success = false, message = "AI analysis failed" },
                processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in UploadFamilyPhoto");
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Obtener todas las fotos familiares (ImageAI) de un Twin
    /// GET /api/twins/{twinId}/family-photos
    /// </summary>
    [Function("GetFamilyPhotosByTwinId")]
    public async Task<HttpResponseData> GetFamilyPhotosByTwinId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/family-photos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📸 GetFamilyPhotosByTwinId function triggered for Twin: {TwinId}", twinId);

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

            _logger.LogInformation("📸 Getting all family photos for Twin ID: {TwinId}", twinId);

            // Crear instancia del servicio FamilyFotoscosmosDB
            var familyPhotosService = CreateFamilyPhotosService();

            // Obtener todas las fotos familiares usando el método que acabamos de crear
           // var photos = await familyPhotosService.GetAllPhotosByTwinIdAsync(twinId);
           var photos = await _picturesFamilySearchIndex.GetAllFamilyPicturesByTwinIdAsync(twinId);

            // 📸 NUEVO: Generar SAS URLs para cada foto usando DataLake
            if (photos.Count > 0)
            {
                _logger.LogInformation("📸 Generating SAS URLs for {PhotoCount} family photos", photos.Count);
                
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                foreach (var photo in photos)
                {
                    try
                    {
                        // Solo generar SAS URL si FileName está presente
                        if (!string.IsNullOrEmpty(photo.FileName))
                        {
                            // Construir la ruta completa del archivo: familyPhotos/{fileName}
                            var photoPath = $"familyPhotos/{photo.FileName}";
                            
                            _logger.LogDebug("📸 Generating SAS URL for photo: {PhotoPath}", photoPath);

                            // Generar SAS URL (válida por 24 horas)
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(photoPath, TimeSpan.FromHours(24));
                            
                            if (!string.IsNullOrEmpty(sasUrl))
                            {
                                photo.Url = sasUrl;
                                _logger.LogDebug("✅ SAS URL generated successfully for photo: {FileName}", photo.FileName);
                            }
                            else
                            {
                                photo.Url = "";
                                _logger.LogWarning("⚠️ Failed to generate SAS URL for photo: {FileName}", photo.FileName);
                            }
                        }
                        else
                        {
                            photo.Url = "";
                            _logger.LogDebug("📸 No FileName found for photo ID: {PhotoId}", photo.id);
                        }
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogWarning(photoEx, "⚠️ Error generating SAS URL for photo {PhotoId} with FileName {FileName}",
                            photo.id, photo.FileName);
                        
                        // En caso de error, asegurar que la URL esté vacía
                        photo.Url = "";
                    }
                }

                _logger.LogInformation("📸 Processed {PhotoCount} photos for SAS URL generation", photos.Count);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation("✅ Retrieved {Count} family photos for Twin ID: {TwinId}", photos.Count, twinId);

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                twinId = twinId,
                photos = photos,
                count = photos.Count,
                message = $"Retrieved {photos.Count} family photos for Twin {twinId}",
                retrievedAt = DateTime.UtcNow
            }, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting family photos for Twin: {TwinId}", twinId);

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
    /// Buscar fotos familiares usando búsqueda semántica híbrida
    /// GET /api/twins/{twinId}/search-family-pictures?query={searchText}
    /// </summary>
    [Function("SearchFamilyPictures")]
    public async Task<HttpResponseData> SearchFamilyPictures(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/search-family-pictures")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🔍 SearchFamilyPictures function triggered for Twin: {TwinId}", twinId);

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

            // Obtener parámetros de query string
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var searchText = query["query"] ?? query["searchText"];

            if (string.IsNullOrEmpty(searchText))
            {
                _logger.LogError("❌ Search query parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Search query parameter is required. Use ?query=your_search_text"
                }));
                return badResponse;
            }

            _logger.LogInformation("🔍 Searching family pictures for Twin ID: {TwinId} with query: {SearchText}", twinId, searchText);

            // Crear consulta de búsqueda
            var searchQuery = new PicturesFamilySearchQuery
            {
                SearchText = searchText,
                TwinId = twinId,
                UseHybridSearch = true, // Usar búsqueda híbrida por defecto
                Top = 20,
                Page = 1
            };

            // Realizar búsqueda usando el servicio
            var searchResult = await _picturesFamilySearchIndex.SearchFamilyPicturesFormerAsync(searchQuery);

            if (searchResult == null)
            {
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Errors"
                }));
                return errorResponse;
            }

           

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

           
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                query = searchText,
                AiResponse = searchResult,
                twinId = twinId,
                searchedAt = DateTime.UtcNow
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error searching family pictures for Twin: {TwinId}", twinId);

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

    // ========================================
    // HELPER METHODS
    // ========================================

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

    private static string GetMimeTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "web" or "webp" => "image/webp",
            "bmp" => "image/bmp",
            "tiff" or "tif" => "image/tiff",
            _ => "image/jpeg"
        };
    }

    /// <summary>
    /// Indexar el análisis de una foto familiar en Azure AI Search
    /// </summary>
    /// <param name="imageAI">Análisis de ImageAI de la foto</param>
    /// <param name="processingTimeMs">Tiempo de procesamiento en milisegundos</param>
    /// <returns>Resultado de la indexación</returns>
    public async Task<Services.PicturesFamilyIndexResult> IndexPictureUploadDocumentAsync(ImageAI imageAI, double processingTimeMs = 0.0)
    {
        try
        {
            _logger.LogInformation("📸 Starting picture indexing for PictureId: {PictureId}, TwinId: {TwinId}", 
                imageAI.id, imageAI.TwinID);

            // Validar que ImageAI tenga los datos necesarios
            if (imageAI == null)
            {
                _logger.LogError("❌ ImageAI is null, cannot index picture");
                return new Services.PicturesFamilyIndexResult
                {
                    Success = false,
                    Error = "ImageAI data is null"
                };
            }

            if (string.IsNullOrEmpty(imageAI.TwinID))
            {
                _logger.LogError("❌ TwinID is required for picture indexing");
                return new Services.PicturesFamilyIndexResult
                {
                    Success = false,
                    Error = "TwinID is required for picture indexing"
                };
            }

            // Llamar al servicio de indexación
            var indexResult = await _picturesFamilySearchIndex.IndexPictureUploadDocumentAsync(imageAI, processingTimeMs);

            if (indexResult.Success)
            {
                _logger.LogInformation("✅ Picture indexed successfully in Azure Search: DocumentId={DocumentId}, PictureId={PictureId}", 
                    indexResult.DocumentId, imageAI.id);
            }
            else
            {
                _logger.LogWarning("⚠️ Picture indexing failed: {Error}", indexResult.Error);
            }

            return indexResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error indexing picture in Azure Search for PictureId: {PictureId}", imageAI?.id);
            return new Services.PicturesFamilyIndexResult
            {
                Success = false,
                Error = $"Error indexing picture: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Crear instancia del servicio FamilyFotoscosmosDB
    /// </summary>
    private Services.FamilyFotoscosmosDB CreateFamilyPhotosService()
    {
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
        {
            Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
            Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
            DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });

        var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Services.FamilyFotoscosmosDB>();
        return new Services.FamilyFotoscosmosDB(serviceLogger, cosmosOptions, _configuration);
    }

    /// <summary>
    /// OPTIONS handler para actualizar fotos familiares
    /// </summary>
    [Function("UpdateFamilyPhotoOptions")]
    public async Task<HttpResponseData> HandleUpdateFamilyPhotoOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family-photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for twins/{twinId}/family-photos/{photoId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Actualizar análisis de ImageAI de una foto familiar existente
    /// PUT /api/twins/{twinId}/family-photos/{photoId}
    /// </summary>
    [Function("UpdateFamilyPhoto")]
    public async Task<HttpResponseData> UpdateFamilyPhoto(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/family-photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
        _logger.LogInformation("📸 UpdateFamilyPhoto function triggered for PhotoId: {PhotoId}, Twin: {TwinId}", photoId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(photoId))
            {
                _logger.LogError("❌ Twin ID and Photo ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Photo ID parameters are required"
                }));
                return badResponse;
            }

            // Leer el cuerpo de la petición
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("❌ Request body is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Request body is required"
                }));
                return badResponse;
            }

            // Parsear JSON request
            ImageAI? imageAI;
            try
            {
                imageAI = JsonSerializer.Deserialize<ImageAI>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "⚠️ Invalid JSON in request body");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid JSON format in request body"
                }));
                return badResponse;
            }

            if (imageAI == null)
            {
                _logger.LogError("❌ Failed to parse ImageAI data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid ImageAI data format"
                }));
                return badResponse;
            }

            // Asegurar que los IDs sean consistentes
            imageAI.id = photoId;
            imageAI.TwinID = twinId;

            _logger.LogInformation($"📸 Updating family photo {photoId} for Twin ID: {twinId}");

            // Crear instancia del servicio FamilyFotoscosmoDB
            var familyPhotosService = CreateFamilyPhotosService();

            // Actualizar el análisis de ImageAI
            var success = await familyPhotosService.UpdateImageAIAsync(imageAI);

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (success)
            {
                _logger.LogInformation($"✅ Family photo updated successfully: {photoId} for Twin: {twinId}");

                // *** NUEVA FUNCIONALIDAD: Re-indexar la foto actualizada en Azure AI Search ***
                try
                {
                    _logger.LogInformation("🔍 Re-indexing updated photo in Azure Search: {PhotoId}", photoId);
                    var indexResult = await IndexPictureUploadDocumentAsync(imageAI, 0.0);
                    
                    if (indexResult.Success)
                    {
                        _logger.LogInformation("✅ Photo re-indexed successfully in Azure Search: DocumentId={DocumentId}", indexResult.DocumentId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to re-index photo in Azure Search: {Error}", indexResult.Error);
                    }
                }
                catch (Exception indexEx)
                {
                    _logger.LogWarning(indexEx, "⚠️ Azure Search re-indexing failed, but update was successful");
                }

                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    photoId = photoId,
                    twinId = twinId,
                    imageAI = imageAI,
                    message = "Family photo updated successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError($"❌ Failed to update family photo: {photoId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to update family photo in database",
                    photoId = photoId,
                    twinId = twinId
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating family photo: {PhotoId} for Twin: {TwinId}", photoId, twinId);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message,
                photoId = photoId,
                twinId = twinId
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// OPTIONS handler para eliminar fotos familiares
    /// </summary>
    [Function("DeleteFamilyPhotoOptions")]
    public async Task<HttpResponseData> HandleDeleteFamilyPhotoOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family-photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
         var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Eliminar una foto familiar del índice de Azure AI Search
    /// DELETE /api/twins/{twinId}/family-photos/{photoId}
    /// </summary>
    [Function("DeleteFamilyPhoto")]
    public async Task<HttpResponseData> DeleteFamilyPhoto(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/family-photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
        _logger.LogInformation("🗑️ DeleteFamilyPhoto function triggered for PhotoId: {PhotoId}, Twin: {TwinId}", photoId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(photoId))
            {
                _logger.LogError("❌ Twin ID and Photo ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Photo ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("🗑️ Deleting family photo {PhotoId} for Twin ID: {TwinId}", photoId, twinId);

            // Generar el document ID que se usaría en el índice
            // Basado en el patrón que usa GenerateUniquePictureDocumentId: "picture-{pictureId}-{twinId}"
            var documentId = $"picture-{photoId}-{twinId}";

            // Llamar al servicio para eliminar el documento del índice de Azure Search
            var deleteResult = await _picturesFamilySearchIndex.DeletePictureDocumentAsync(photoId);

            var response = req.CreateResponse(deleteResult.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (deleteResult.Success)
            {
                _logger.LogInformation("✅ Family photo deleted successfully from search index: PhotoId={PhotoId}, DocumentId={DocumentId}", 
                    photoId, documentId);

                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    photoId = photoId,
                    documentId = documentId,
                    twinId = twinId,
                    indexName = deleteResult.IndexName,
                    message = "Family photo deleted successfully from search index",
                    deletedAt = DateTime.UtcNow
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogError("❌ Failed to delete family photo from search index: PhotoId={PhotoId}, Error={Error}", 
                    photoId, deleteResult.Error);

                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    photoId = photoId,
                    documentId = documentId,
                    twinId = twinId,
                    errorMessage = deleteResult.Error ?? "Failed to delete family photo from search index"
                }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting family photo: {PhotoId} for Twin: {TwinId}", photoId, twinId);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                photoId = photoId,
                twinId = twinId,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }
}