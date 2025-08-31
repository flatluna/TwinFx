using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Services;
using TwinFx.Agents;

namespace TwinFx.Functions;

public class UploadPhotoFunction
{
    private readonly ILogger<UploadPhotoFunction> _logger;
    private readonly IConfiguration _configuration;

    public UploadPhotoFunction(ILogger<UploadPhotoFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("UploadPhotoWithMetadataOptions")]
    public async Task<HttpResponseData> HandleUploadPhotoOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/upload-photo-with-metadata")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for upload-photo-with-metadata/{twinId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UploadPhotoWithMetadata")]
    public async Task<HttpResponseData> UploadPhotoWithMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/upload-photo-with-metadata")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📸 UploadPhotoWithMetadata function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            // SIMPLE: Solo lee JSON con base64 (como tu ejemplo sugiere)
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");

            var uploadRequest = JsonSerializer.Deserialize<UploadPhotoRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (uploadRequest == null || string.IsNullOrEmpty(uploadRequest.PhotoData))
            {
                _logger.LogError("❌ PhotoData required");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            // Convert base64 to bytes (simple)
            byte[] photoBytes;
            try
            {
                // Remove data URL prefix if present (data:image/jpeg;base64,...)
                var photoData = uploadRequest.PhotoData;
                if (photoData.Contains(','))
                {
                    photoData = photoData.Split(',')[1];
                }
                photoBytes = Convert.FromBase64String(photoData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Invalid base64");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var metadata = uploadRequest.Metadata;
            if (string.IsNullOrEmpty(metadata.Category) || string.IsNullOrEmpty(metadata.DateTaken))
            {
                _logger.LogError("❌ Category and DateTaken required");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"📏 Photo size: {photoBytes.Length} bytes");

            // Upload logic (simple)
            var dataLakeFactory = new DataLakeClientFactory(LoggerFactory.Create(b => b.AddConsole()), _configuration);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            var photoId = Guid.NewGuid().ToString();
            var fileExtension = GetFileExtensionFromBytes(photoBytes);
            
            // Use filePath from metadata (which includes the filename), or generate default
            string fileName;
            string filePath;
            
            if (!string.IsNullOrEmpty(metadata.FilePath))
            {
                // Use the complete path from metadata: "familia/general/SemanasAfore.png"
                filePath = metadata.FilePath;
                fileName = Path.GetFileName(filePath); // Extract "SemanasAfore.png"
            }
            else
            {
                // Fallback: use original filename or generate one
                fileName = !string.IsNullOrEmpty(uploadRequest.FileName) 
                    ? Path.GetFileNameWithoutExtension(uploadRequest.FileName) + "." + fileExtension
                    : $"{photoId}.{fileExtension}";
                filePath = $"photos/{photoId}/{fileName}";
            }

            _logger.LogInformation($"📂 Using filename: {fileName}, FilePath: {filePath}");

            // Upload
            using var photoStream = new MemoryStream(photoBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? "",
                Path.GetFileName(filePath),
                photoStream,
                GetMimeTypeFromExtension(fileExtension));

            if (!uploadSuccess)
            {
                _logger.LogError("❌ Upload failed");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Save metadata (simple)
            var photoMetadata = new TwinFx.Agents.PhotoMetadata
            {
                PhotoId = photoId,
                TwinId = twinId,
                Description = metadata.Description ?? "",
                DateTaken = metadata.DateTaken,
                Location = metadata.Location ?? "",
                PeopleInPhoto = metadata.PeopleInPhoto ?? "",
                Category = metadata.Category,
                Tags = metadata.Tags ?? "",
                FilePath = filePath,
                FileName = fileName,
                FileSize = photoBytes.Length,
                MimeType = GetMimeTypeFromExtension(fileExtension),
                UploadDate = DateTime.UtcNow
            };

            // Save to Cosmos (simple)
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var photosAgentLogger = loggerFactory.CreateLogger<PhotosAgent>();
                var photosAgent = new PhotosAgent(photosAgentLogger, _configuration);
                await photosAgent.SavePhotoMetadataAsync(photoMetadata);
                _logger.LogInformation("✅ Saved to Cosmos");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Cosmos save failed");
            }

            // Response (simple)
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            await response.WriteStringAsync(JsonSerializer.Serialize(new UploadPhotoResponse
            {
                Success = true,
                PhotoId = photoId,
                PhotoUrl = sasUrl,
                Metadata = photoMetadata,
                ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                Message = "Foto subida exitosamente"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadPhotoResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetPhotosOptions")]
    public async Task<HttpResponseData> HandleGetPhotosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/photos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for get photos/{twinId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetPhotos")]
    public async Task<HttpResponseData> GetPhotos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/photos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📸 GetPhotos function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetPhotosResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var category = query["category"];
            var search = query["search"];

            _logger.LogInformation($"📋 Getting photos for Twin ID: {twinId}, Category: {category}, Search: {search}");

            // Get photos using PhotosAgent
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var photosAgentLogger = loggerFactory.CreateLogger<PhotosAgent>();
            var photosAgent = new PhotosAgent(photosAgentLogger, _configuration);
            
            var photosResult = await photosAgent.GetPhotosAsync(twinId, category, search);
            
            if (!photosResult.Success)
            {
                _logger.LogWarning($"⚠️ Failed to get photos: {photosResult.ErrorMessage}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetPhotosResponse
                {
                    Success = false,
                    ErrorMessage = photosResult.ErrorMessage
                }));
                return errorResponse;
            }

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetPhotosResponse
            {
                Success = true,
                Photos = photosResult.Photos,
                TotalCount = photosResult.Photos.Count,
                Message = $"Se encontraron {photosResult.Photos.Count} fotos"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting photos");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetPhotosResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                Message = "Error obteniendo las fotos"
            }));
            
            return errorResponse;
        }
    }

    [Function("GetFamilyPhotosOptions")]
    public async Task<HttpResponseData> HandleGetFamilyPhotosOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/family-photos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for family-photos/{twinId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetFamilyPhotos")]
    public async Task<HttpResponseData> GetFamilyPhotos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/family-photos")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("👨‍👩‍👧‍👦 GetFamilyPhotos function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new FamilyPhotosResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var searchTerm = query["searchTerm"];
            var category = query["category"] ?? "Familia"; // Default to "Familia"
            var limit = int.TryParse(query["limit"], out var l) ? l : 50;
            var offset = int.TryParse(query["offset"], out var o) ? o : 0;

            _logger.LogInformation($"📋 Getting family photos for Twin ID: {twinId}, Category: {category}, Search: {searchTerm}, Limit: {limit}, Offset: {offset}");

            // Get photos using PhotosAgent with "Familia" category filter
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var photosAgentLogger = loggerFactory.CreateLogger<PhotosAgent>();
            var photosAgent = new PhotosAgent(photosAgentLogger, _configuration);
            
            var photosResult = await photosAgent.GetPhotosAsync(twinId, category, searchTerm);
            
            if (!photosResult.Success)
            {
                _logger.LogWarning($"⚠️ Failed to get family photos: {photosResult.ErrorMessage}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new FamilyPhotosResponse
                {
                    Success = false,
                    ErrorMessage = photosResult.ErrorMessage
                }));
                return errorResponse;
            }

            // Generate SAS URLs for each photo
            var dataLakeFactory = new DataLakeClientFactory(LoggerFactory.Create(b => b.AddConsole()), _configuration);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            
            var familyPhotos = new List<FamilyPhotoItem>();
            
            // Apply pagination
            var paginatedPhotos = photosResult.Photos.Skip(offset).Take(limit);
            
            foreach (var photo in paginatedPhotos)
            {
                try
                {
                    // Generate SAS URL for each photo (24 hour expiry)
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(photo.FilePath, TimeSpan.FromHours(24));
                    
                    var familyPhoto = new FamilyPhotoItem
                    {
                        Id = photo.PhotoId,
                        TwinID = photo.TwinId,
                        Description = photo.Description,
                        DateTaken = photo.DateTaken,
                        Location = photo.Location,
                        // Parse location into country/place if available
                        Country = ExtractCountryFromLocation(photo.Location),
                        Place = ExtractPlaceFromLocation(photo.Location),
                        PeopleInPhoto = photo.PeopleInPhoto,
                        Category = photo.Category,
                        Tags = photo.Tags,
                        FilePath = photo.FilePath,
                        FileName = photo.FileName,
                        FileSize = photo.FileSize,
                        MimeType = photo.MimeType,
                        UploadDate = photo.UploadDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        Type = "photo",
                        PhotoUrl = sasUrl
                    };
                    
                    familyPhotos.Add(familyPhoto);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to generate SAS URL for photo: {PhotoId}", photo.PhotoId);
                    // Continue with other photos, don't break the entire response
                }
            }

            // Create response in the exact format you specified
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            var responseData = new FamilyPhotosResponse
            {
                Success = true,
                TwinId = twinId,
                Photos = familyPhotos,
                Count = familyPhotos.Count,
                SearchTerm = searchTerm,
                Category = category,
                Message = "Photos retrieved successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation($"✅ Retrieved {familyPhotos.Count} family photos for Twin ID: {twinId}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting family photos");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new FamilyPhotosResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                Message = "Error retrieving family photos"
            }));
            
            return errorResponse;
        }
    }

    [Function("PhotoByIdOptions")]
    public async Task<HttpResponseData> HandlePhotoByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
        _logger.LogInformation($"📸 OPTIONS preflight request for photo operations/{twinId}/{photoId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UpdatePhoto")]
    public async Task<HttpResponseData> UpdatePhoto(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
        _logger.LogInformation("📸 UpdatePhoto function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(photoId))
            {
                _logger.LogError("❌ Twin ID and Photo ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID and Photo ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");

            // Parse JSON request
            var updateData = JsonSerializer.Deserialize<PhotoMetadataRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateData == null)
            {
                _logger.LogError("❌ Failed to parse photo update data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid photo update data format"
                }));
                return badResponse;
            }

            _logger.LogInformation($"📸 Updating photo {photoId} for Twin ID: {twinId}");

            // Get the existing photo from Cosmos DB first
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var photosAgentLogger = loggerFactory.CreateLogger<PhotosAgent>();
            var photosAgent = new PhotosAgent(photosAgentLogger, _configuration);
            
            var existingPhotosResult = await photosAgent.GetPhotosAsync(twinId);
            if (!existingPhotosResult.Success)
            {
                _logger.LogError("❌ Failed to retrieve existing photos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to retrieve existing photo"
                }));
                return errorResponse;
            }

            var existingPhoto = existingPhotosResult.Photos.FirstOrDefault(p => p.PhotoId == photoId);
            if (existingPhoto == null)
            {
                _logger.LogWarning($"⚠️ Photo {photoId} not found for Twin ID: {twinId}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
                {
                    Success = false,
                    ErrorMessage = "Photo not found"
                }));
                return notFoundResponse;
            }

            // Update the photo metadata with new values, preserving only changed fields
            var updatedMetadata = new TwinFx.Agents.PhotoMetadata
            {
                Country = updateData.Country,
                PhotoId = existingPhoto.PhotoId,
                TwinId = existingPhoto.TwinId,
                Description = updateData.Description ?? existingPhoto.Description,
                DateTaken = !string.IsNullOrEmpty(updateData.DateTaken) ? updateData.DateTaken : existingPhoto.DateTaken,
                Location = !string.IsNullOrEmpty(updateData.Location) ? updateData.Location : 
                          (!string.IsNullOrEmpty(updateData.Place) && !string.IsNullOrEmpty(updateData.Country) ? 
                           $"{updateData.Place}, {updateData.Country}" : existingPhoto.Location),
                PeopleInPhoto = updateData.PeopleInPhoto ?? existingPhoto.PeopleInPhoto,
                Category = !string.IsNullOrEmpty(updateData.Category) ? updateData.Category : existingPhoto.Category,
                Tags = updateData.Tags ?? existingPhoto.Tags,
                FilePath = existingPhoto.FilePath, // Keep original file path
                FileName = existingPhoto.FileName, // Keep original file name
                FileSize = existingPhoto.FileSize, // Keep original file size
                MimeType = existingPhoto.MimeType, // Keep original mime type
                UploadDate = existingPhoto.UploadDate // Keep original upload date
               
            };

            // ✅ FIXED: Use Cosmos DB service directly for UPDATE/UPSERT instead of SavePhotoMetadataAsync
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Create PhotoDocument for Cosmos DB update
            var photoDocument = new PhotoDocument
            {
                Id = updatedMetadata.PhotoId,
                TwinId = updatedMetadata.TwinId,
                PhotoId = updatedMetadata.PhotoId,
                Description = updatedMetadata.Description,
                DateTaken = updatedMetadata.DateTaken,
                Location = updatedMetadata.Location,
                PeopleInPhoto = updatedMetadata.PeopleInPhoto,
                Category = updatedMetadata.Category,
                Tags = updatedMetadata.Tags?.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>(),
                FilePath = updatedMetadata.FilePath,
                FileName = updatedMetadata.FileName,
                FileSize = updatedMetadata.FileSize,
                MimeType = updatedMetadata.MimeType,
                UploadDate = updatedMetadata.UploadDate,
                Country = updatedMetadata.Country,
                CreatedAt = existingPhoto.UploadDate, // Keep original creation time
                ProcessedAt = DateTime.UtcNow // Update processed time
            };

            // ✅ Use UPSERT operation via Cosmos DB service
            var updateSuccess = await cosmosService.SavePhotoDocumentAsync(photoDocument);
            
            if (!updateSuccess)
            {
                _logger.LogError($"❌ Failed to update photo metadata in Cosmos DB");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to update photo metadata in database"
                }));
                return errorResponse;
            }

            // Generate SAS URL for the response
            var dataLakeFactory = new DataLakeClientFactory(LoggerFactory.Create(b => b.AddConsole()), _configuration);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(existingPhoto.FilePath, TimeSpan.FromHours(24));
            updatedMetadata.SasUrl = sasUrl;

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation($"✅ Photo metadata updated successfully: {photoId}");
            await response.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
            {
                Success = true,
                Photo = updatedMetadata,
                PhotoId = photoId,
                TwinId = twinId,
                Message = "Photo metadata updated successfully"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating photo metadata");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdatePhotoResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    [Function("GetPhotoById")]
    public async Task<HttpResponseData> GetPhotoById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/photos/{photoId}")] HttpRequestData req,
        string twinId, string photoId)
    {
        _logger.LogInformation("📸 GetPhotoById function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(photoId))
            {
                _logger.LogError("❌ Twin ID and Photo ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetPhotoByIdResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID and Photo ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"📸 Getting photo {photoId} for Twin ID: {twinId}");

            // Get photos using PhotosAgent
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var photosAgentLogger = loggerFactory.CreateLogger<PhotosAgent>();
            var photosAgent = new PhotosAgent(photosAgentLogger, _configuration);
            
            var photosResult = await photosAgent.GetPhotosAsync(twinId);
            
            if (!photosResult.Success)
            {
                _logger.LogWarning($"⚠️ Failed to get photos: {photosResult.ErrorMessage}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetPhotoByIdResponse
                {
                    Success = false,
                    ErrorMessage = photosResult.ErrorMessage
                }));
                return errorResponse;
            }

            // Find the specific photo
            var photo = photosResult.Photos.FirstOrDefault(p => p.PhotoId == photoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (photo == null)
            {
                _logger.LogInformation($"📭 No photo found with ID: {photoId} for Twin ID: {twinId}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new GetPhotoByIdResponse
                {
                    Success = false,
                    ErrorMessage = "Photo not found",
                    PhotoId = photoId,
                    TwinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation($"✅ Photo found: {photo.FileName}");
                await response.WriteStringAsync(JsonSerializer.Serialize(new GetPhotoByIdResponse
                {
                    Success = true,
                    Photo = photo,
                    PhotoId = photoId,
                    TwinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting photo by ID");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetPhotoByIdResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Extract country from location string (simple heuristic)
    /// </summary>
    private static string ExtractCountryFromLocation(string location)
    {
        if (string.IsNullOrEmpty(location))
            return "";
        
        // Simple logic: if location contains commas, take the last part as country
        var parts = location.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length > 1)
        {
            // Check if last part looks like a country/state code
            var lastPart = parts.Last();
            if (lastPart.Length == 2 || lastPart.Equals("USA", StringComparison.OrdinalIgnoreCase))
                return lastPart;
        }
        
        return ""; // Return empty if can't determine
    }

    /// <summary>
    /// Extract place from location string (simple heuristic)
    /// </summary>
    private static string ExtractPlaceFromLocation(string location)
    {
        if (string.IsNullOrEmpty(location))
            return "";
        
        // Simple logic: take the first part before comma as place
        var parts = location.Split(',').Select(p => p.Trim()).ToArray();
        return parts.First();
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

    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
        var origin = originHeader.Value?.FirstOrDefault();
        
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
/// Request model for photo upload with metadata
/// </summary>
public class UploadPhotoRequest
{
    /// <summary>
    /// Base64 encoded photo data
    /// </summary>
    public string PhotoData { get; set; } = string.Empty;

    /// <summary>
    /// Original filename from the UI upload
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Photo metadata
    /// </summary>
    public PhotoMetadataRequest Metadata { get; set; } = new();
}

/// <summary>
/// Photo metadata for request
/// </summary>
public class PhotoMetadataRequest
{
    /// <summary>
    /// Describe qué está pasando en la foto
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Fecha tomada (format: yyyy-mm-dd or mm/dd/yyyy)
    /// </summary>
    [JsonPropertyName("date_taken")]
    public string DateTaken { get; set; } = string.Empty;

    /// <summary>
    /// Ej: Casa de los abuelos, Miami, FL
    /// </summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>
    /// País
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>
    /// Lugar específico
    /// </summary>
    [JsonPropertyName("place")]
    public string? Place { get; set; }

    /// <summary>
    /// Ej: Mamá, Papá, María, Juan (separados por comas)
    /// </summary>
    [JsonPropertyName("people_in_photo")]
    public string? PeopleInPhoto { get; set; }

    /// <summary>
    /// Ej: Familia, Viajes, Trabajo, etc.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Ej: cumpleaños, sorpresa, felicidad (separadas por comas)
    /// </summary>
    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    /// <summary>
    /// Tipo de evento
    /// </summary>
    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    /// <summary>
    /// Nombre del archivo
    /// </summary>
    [JsonPropertyName("filename")]
    public string? FileName { get; set; }

    /// <summary>
    /// Tamaño del archivo en bytes
    /// </summary>
    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }

    /// <summary>
    /// Fecha de subida
    /// </summary>
    [JsonPropertyName("uploaded_at")]
    public string? UploadedAt { get; set; }

    /// <summary>
    /// URL de la foto
    /// </summary>
    [JsonPropertyName("photo_url")]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// ID de la foto
    /// </summary>
    [JsonPropertyName("photo_id")]
    public string? PhotoId { get; set; }

    /// <summary>
    /// Ruta completa personalizada en Data Lake (ej: "vacaciones/2025/playa.jpg")
    /// </summary>
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }
}

/// <summary>
/// Response model for photo upload
/// </summary>
public class UploadPhotoResponse
{
    /// <summary>
    /// Whether the upload was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Total processing time in seconds
    /// </summary>
    public double ProcessingTimeSeconds { get; set; }

    /// <summary>
    /// Generated photo ID
    /// </summary>
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>
    /// SAS URL for accessing the photo
    /// </summary>
    public string PhotoUrl { get; set; } = string.Empty;

    /// <summary>
    /// Photo metadata using the shared PhotoMetadata class from Agents namespace
    /// </summary>
    public TwinFx.Agents.PhotoMetadata? Metadata { get; set; }
}

/// <summary>
/// Response model for getting photos
/// </summary>
public class GetPhotosResponse
{
    /// <summary>
    /// Whether the request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if request failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// List of photos with metadata and URLs using the shared PhotoMetadata class from Agents namespace
    /// </summary>
    public List<TwinFx.Agents.PhotoMetadata> Photos { get; set; } = new();

    /// <summary>
    /// Total count of photos found
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Response model for family photos
/// </summary>
public class FamilyPhotosResponse
{
    /// <summary>
    /// Whether the request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if request failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// List of family photos
    /// </summary>
    public List<FamilyPhotoItem> Photos { get; set; } = new();

    /// <summary>
    /// Count of photos in this response
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Search term used (if any)
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Category filter used (if any)
    /// </summary>
    public string? Category { get; set; }
}

/// <summary>
/// Family photo item with all required fields for UI
/// </summary>
public class FamilyPhotoItem
{
    /// <summary>
    /// Photo ID (UUID)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID who owns the photo
    /// </summary>
    public string TwinID { get; set; } = string.Empty;

    /// <summary>
    /// Photo description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Date when photo was taken
    /// </summary>
    public string DateTaken { get; set; } = string.Empty;

    /// <summary>
    /// Location where photo was taken
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Country extracted from location
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Place extracted from location
    /// </summary>
    public string? Place { get; set; }

    /// <summary>
    /// People in the photo
    /// </summary>
    public string? PeopleInPhoto { get; set; }

    /// <summary>
    /// Photo category
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Photo tags (comma-separated)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// File path in storage (e.g., "fotos/Familia/cumpleanos/family_dinner.jpg")
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// File name (e.g., "family_dinner.jpg")
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// MIME type (e.g., "image/jpeg")
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Upload date (ISO 8601 format)
    /// </summary>
    public string UploadDate { get; set; } = string.Empty;

    /// <summary>
    /// Type (always "photo")
    /// </summary>
    public string Type { get; set; } = "photo";

    /// <summary>
    /// SAS URL for direct access to the photo
    /// </summary>
    public string PhotoUrl { get; set; } = string.Empty;
}

/// <summary>
/// Response model for updating photo metadata
/// </summary>
public class UpdatePhotoResponse
{
    /// <summary>
    /// Whether the update was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if update failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Updated photo metadata
    /// </summary>
    public TwinFx.Agents.PhotoMetadata? Photo { get; set; }

    /// <summary>
    /// Photo ID that was updated
    /// </summary>
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
}

/// <summary>
/// Response model for getting photo by ID
/// </summary>
public class GetPhotoByIdResponse
{
    /// <summary>
    /// Whether the request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if request failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Photo metadata with SAS URL
    /// </summary>
    public TwinFx.Agents.PhotoMetadata? Photo { get; set; }

    /// <summary>
    /// Photo ID that was requested
    /// </summary>
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
}