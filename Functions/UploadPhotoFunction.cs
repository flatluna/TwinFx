using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Services;
using TwinFx.Agents;
using TwinFx.Models;
using HtmlAgilityPack;

namespace TwinFx.Functions
{
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

                byte[] photoBytes;
                try
                {
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
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var photoId = Guid.NewGuid().ToString();
                var fileExtension = GetFileExtensionFromBytes(photoBytes);

                string fileName;
                string filePath;

                if (!string.IsNullOrEmpty(metadata.FilePath))
                {
                    filePath = metadata.FilePath;
                    fileName = Path.GetFileName(filePath);
                }
                else
                {
                    fileName = !string.IsNullOrEmpty(uploadRequest.FileName)
                        ? Path.GetFileNameWithoutExtension(uploadRequest.FileName) + "." + fileExtension
                        : $"{photoId}.{fileExtension}";
                    filePath = $"photos/{photoId}/{fileName}";
                }

                _logger.LogInformation($"📂 Using filename: {fileName}, FilePath: {filePath}");

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

                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var category = query["category"];
                var search = query["search"];

                _logger.LogInformation($"📋 Getting photos for Twin ID: {twinId}, Category: {category}, Search: {search}");
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

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");
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
                    FilePath = existingPhoto.FilePath,
                    FileName = existingPhoto.FileName,
                    FileSize = existingPhoto.FileSize,
                    MimeType = existingPhoto.MimeType,
                    UploadDate = existingPhoto.UploadDate
                };

                var cosmosService = _configuration.CreateCosmosService(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());
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
                    CreatedAt = existingPhoto.UploadDate,
                    ProcessedAt = DateTime.UtcNow
                };

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

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
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

        [Function("SimpleUploadPhotoOptions")]
        public async Task<HttpResponseData> HandleSimpleUploadPhotoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/simple-upload-photo")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"📸 OPTIONS preflight request for simple-upload-photo/{twinId}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SimpleUploadPhoto")]
        public async Task<HttpResponseData> SimpleUploadPhoto(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/simple-upload-photo/{*filePath}")] HttpRequestData req,
            string twinId,
            string filePath)
        {
            _logger.LogInformation("📸 SimpleUploadPhoto function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
                if (contentTypeHeader.Key == null || !contentTypeHeader.Value.Any(v => v.Contains("multipart/form-data")))
                {
                    return await CreateErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
                }

                var contentType = contentTypeHeader.Value.FirstOrDefault() ?? "";
                var boundary = GetBoundary(contentType);
                if (string.IsNullOrEmpty(boundary))
                {
                    return await CreateErrorResponse(req, "Invalid boundary in multipart/form-data", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation($"📸 Processing simple photo upload for Twin ID: {twinId}");
                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                var bodyBytes = memoryStream.ToArray();
                _logger.LogInformation($"📄 Received multipart data: {bodyBytes.Length} bytes");

                var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                var photoPart = parts.FirstOrDefault(p => p.Name == "photo" || p.Name == "file" || p.Name == "image");
                if (photoPart == null || photoPart.Data == null || photoPart.Data.Length == 0)
                {
                    return await CreateErrorResponse(req, "No photo file data found in request. Expected field name: 'photo', 'file', or 'image'", HttpStatusCode.BadRequest);
                }

                // 🔧 DECODIFICAR EL FILE PATH DE LA URL
                if (string.IsNullOrEmpty(filePath))
                {
                    return await CreateErrorResponse(req, "File path is required in the URL. Use format: /twins/{twinId}/simple-upload-photo/{path}", HttpStatusCode.BadRequest);
                }

                // ✅ Decodificar URL encoded path (ej: homes%2F88e1b605-657a-4210-aa24-c417437b607e%2Fphotos%2FExterior)
                var decodedFilePath = System.Web.HttpUtility.UrlDecode(filePath);
                _logger.LogInformation($"📂 Original path from URL: {filePath}");
                _logger.LogInformation($"📂 Decoded path: {decodedFilePath}");

                // ✅ Incorporar lógica de fileName personalizado del FormData
                var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
                var customFileName = fileNamePart?.StringValue;
                var fileName = customFileName ?? photoPart.FileName ?? $"photo_{Guid.NewGuid()}";
                fileName = fileName.Replace('\"', '_'); // Reemplazar comillas dobles para evitar problemas en el nombre de archivo

                _logger.LogInformation($"📏 Photo details: Size={photoPart.Data.Length} bytes, OriginalName={photoPart.FileName}, FinalName={fileName}");

                // Validar que sea una imagen
                if (!IsValidImageFile(fileName, photoPart.Data))
                {
                    return await CreateErrorResponse(req, "Invalid image file format. Supported formats: JPG, PNG, GIF, WEBP, BMP", HttpStatusCode.BadRequest);
                }

                // Detectar extensión del archivo
                var detectedExtension = GetFileExtensionFromBytes(photoPart.Data);
                var fileExtension = Path.GetExtension(fileName);
                var cleanFileName = Path.GetFileNameWithoutExtension(fileName) + (string.IsNullOrEmpty(fileExtension) ? $".{detectedExtension}" : fileExtension);

                _logger.LogInformation($"📤 STEP 1: Uploading photo to DataLake...");
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var mimeType = GetMimeTypeFromExtension(detectedExtension);

                // ✅ Subir el archivo usando el path decodificado como directorio
                using var photoStream = new MemoryStream(photoPart.Data);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(),
                    decodedFilePath,
                    cleanFileName,
                    photoStream,
                    mimeType);

                if (!uploadSuccess)
                {
                    _logger.LogError("❌ Failed to upload photo to DataLake");
                    return await CreateErrorResponse(req, "Failed to upload photo to storage", HttpStatusCode.InternalServerError);
                }

                var completePath = $"{decodedFilePath}/{cleanFileName}";
                _logger.LogInformation("✅ Photo uploaded successfully to DataLake");
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

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
                var timeTakenPart = parts.FirstOrDefault(p => p.Name == "TimeTaken");
                var pathPart = parts.FirstOrDefault(p => p.Name == "Path");

                // Crear PhotoFormData con los datos extraídos del FormData
                var photoFormData = new PhotoFormData
                {
                    FileName = cleanFileName,
                    TimeTaken = timeTakenPart?.StringValue ?? "",
                    Path = pathPart?.StringValue ?? decodedFilePath,
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

                ImageAI? imageAI = null;
                try
                {
                    _logger.LogInformation("🤖 Starting AI photo analysis...");

                    // Crear instancia del FamilyFotosAgent con configuración e logger adecuados
                    var familyFotosLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TwinFx.Agents.FamilyFotosAgent>();
                    var familyFotosAgent = new TwinFx.Agents.FamilyFotosAgent(familyFotosLogger, _configuration);

                    PhotosAI AiPhoto = new PhotosAI();
                    var ResultsPhoto = await AiPhoto.AnalyzePhotoAsync(sasUrl, photoFormData, "Homes", userDescription);
                    // ✅ LLAMAR AL ANÁLISIS DE IA CON LA URL SAS Y LOS DATOS DE LA FOTO CORRECTOS

                    ResultsPhoto.TwinID = twinId;
                    ResultsPhoto.id = Guid.NewGuid().ToString();
                    ResultsPhoto.FileName = cleanFileName;
                    ResultsPhoto.FilePath = decodedFilePath;
                    ResultsPhoto.FileURL = sasUrl;

                    // *** NUEVA FUNCIONALIDAD: Guardar AnalisisResidencial en Cosmos DB ***
                    try
                    {
                        var photosService = CreatePhotosService();
                        var saveSuccess = await photosService.SaveAnalisisResidencialAsync(ResultsPhoto);

                        if (saveSuccess)
                        {
                            _logger.LogInformation("✅ AnalisisResidencial saved successfully to Cosmos DB with ID: {AnalisisId}", ResultsPhoto.id);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to save AnalisisResidencial to Cosmos DB, but continuing with photo upload");
                        }
                    }
                    catch (Exception cosmosEx)
                    {
                        _logger.LogWarning(cosmosEx, "⚠️ Cosmos DB save failed for AnalisisResidencial, but continuing with photo upload");
                    }
                }
                catch (Exception aiEx)
                {
                    _logger.LogWarning(aiEx, "⚠️ AI photo analysis failed, but continuing with photo upload");
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                var responseData = new SimpleUploadPhotoResponse
                {
                    Success = true,
                    Message = "Photo uploaded successfully",
                    TwinId = twinId,
                    FilePath = completePath,
                    FileName = cleanFileName,
                    Directory = decodedFilePath,
                    ContainerName = twinId.ToLowerInvariant(),
                    PhotoUrl = sasUrl,
                    FileSize = photoPart.Data.Length,
                    MimeType = mimeType,
                    UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
                };
                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
                _logger.LogInformation($"✅ Simple photo upload completed successfully in {responseData.ProcessingTimeSeconds:F2} seconds");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in simple photo upload");
                return await CreateErrorResponse(req, $"Upload failed: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        // ========================================
        // HELPER METHODS FOR AI ANALYSIS
        // ========================================

        /// <summary>
        /// Crear instancia del servicio FamilyFotoscosmosDB
        /// </summary>
        private FamilyFotoscosmosDB CreateFamilyPhotosService()
        {
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
                Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
                DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<FamilyFotoscosmosDB>();
            return new FamilyFotoscosmosDB(serviceLogger, cosmosOptions, _configuration);
        }

        /// <summary>
        /// Crear instancia del servicio PhotosCosmosDB
        /// </summary>
        private PhotosCosmosDB CreatePhotosService()
        {
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
                Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
                DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhotosCosmosDB>();
            return new PhotosCosmosDB(serviceLogger, cosmosOptions, _configuration);
        }
 

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync(JsonSerializer.Serialize(new SimpleUploadPhotoResponse
            {
                Success = false,
                ErrorMessage = errorMessage
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return response;
        }

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

        private async Task<List<MultipartFormDataPart>> ParseMultipartDataAsync(byte[] data, string boundary)
        {
            var parts = new List<MultipartFormDataPart>();
            var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
            var endBoundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "--");

            var position = 0;
            var boundaryIndex = FindBytes(data, position, boundaryBytes);
            if (boundaryIndex == -1) return parts;

            position = boundaryIndex + boundaryBytes.Length;
            while (position < data.Length)
            {
                if (position + 1 < data.Length && data[position] == '\r' && data[position + 1] == '\n')
                    position += 2;

                var headersEndBytes = Encoding.UTF8.GetBytes("\r\n\r\n");
                var headersEnd = FindBytes(data, position, headersEndBytes);
                if (headersEnd == -1) break;

                var headersLength = headersEnd - position;
                var headersBytes = new byte[headersLength];
                Array.Copy(data, position, headersBytes, 0, headersLength);
                var headers = Encoding.UTF8.GetString(headersBytes);
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
                StringValue = content.Length > 0 && IsTextContent(contentType) ? Encoding.UTF8.GetString(content) : null
            };
        }

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

        private static bool IsTextContent(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return true;
            return contentType.StartsWith("text/") ||
                   contentType.Contains("application/x-www-form-urlencoded") ||
                   contentType.Contains("application/json");
        }

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

        private static string ExtractCountryFromLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return "";

            var parts = location.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length > 1)
            {
                var lastPart = parts.Last();
                if (lastPart.Length == 2 || lastPart.Equals("USA", StringComparison.OrdinalIgnoreCase))
                    return lastPart;
            }
            return "";
        }

        private static string ExtractPlaceFromLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return "";

            var parts = location.Split(',').Select(p => p.Trim()).ToArray();
            return parts.First();
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

        // ========================================
        // HOME INSURANCE FUNCTIONS
        // ========================================

        [Function("UploadHomeInsuranceOptions")]
        public async Task<HttpResponseData> HandleUploadHomeInsuranceOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/upload-home-insurance/{*filePath}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🏠🛡️ OPTIONS preflight request for upload-home-insurance/{twinId}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }
 
        /// <summary>
        /// Valida si el archivo es un PDF válido
        /// </summary>
       
        [Function("GetPhotosByPathOptions")]
        public async Task<HttpResponseData> HandleGetPhotosByPathOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/photos/by-path/{*path}")] HttpRequestData req,
            string twinId, string path)
        {
            _logger.LogInformation($"📸 OPTIONS preflight request for get photos by path/{twinId}/{path}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetPhotosByPath")]
        public async Task<HttpResponseData> GetPhotosByPath(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/photos/by-path/{*path}")] HttpRequestData req,
            string twinId,
            string path)
        {
            _logger.LogInformation("📸 GetPhotosByPath function triggered");
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

                if (string.IsNullOrEmpty(path))
                {
                    _logger.LogError("❌ Path parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAnalisisResidencialByPathResponse
                    {
                        Success = false,
                        ErrorMessage = "Path parameter is required"
                    }));
                    return badResponse;
                }

                // Decodificar URL encoded path
                var decodedPath = System.Web.HttpUtility.UrlDecode(path);
                _logger.LogInformation($"📋 Getting photos by path for Twin ID: {twinId}, Path: {decodedPath}");

                // Crear instancia del servicio PhotosCosmosDB
                var photosService = CreatePhotosService();
                var analisisResult = await photosService.GetAnalisisByTwinIdAndPathAsync(twinId, decodedPath);

                if (analisisResult == null || !analisisResult.Any())
                {
                    _logger.LogWarning($"⚠️ No analysis found for Twin ID: {twinId}");
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    AddCorsHeaders(response, req);
                    response.Headers.Add("Content-Type", "application/json");
                    var emptyResponseData = new GetAnalisisResidencialByPathResponse
                    {
                        Success = true,
                        AnalisisResidencial = new List<AnalisisResidencialSummary>(),
                        TotalCount = 0,
                        Path = decodedPath,
                        TwinId = twinId,
                        Message = $"No se encontraron análisis para el path '{decodedPath}'"
                    };
                    await response.WriteStringAsync(JsonSerializer.Serialize(emptyResponseData, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));
                    return response;
                }

                // Filtrar análisis por path
           

                // Generar SAS URLs para las fotos encontradas
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                List<AnalisisResidencialSummary> FotosList = new List<AnalisisResidencialSummary>();
                foreach (var analisis in analisisResult)
                {
                    AnalisisResidencialSummary Summary = new AnalisisResidencialSummary();
                    try
                    {
                        if (!string.IsNullOrEmpty(analisis.FilePath) && !string.IsNullOrEmpty(analisis.FileName))
                        {
                            var completePath = $"{analisis.FilePath}/{analisis.FileName}";
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));
                            analisis.FileURL = sasUrl;
                            Summary.FileURL = sasUrl;
                            Summary.FilePath = analisis.FilePath;
                            Summary.FileName = analisis.FileName;
                            Summary.id = analisis.id;
                            Summary.TipoEspacio = analisis.AnalisisArquitectonico.TipoDeEspacio;
                            Summary.DetailsHtml = analisis.DetailsHtml;
                            FotosList.Add(Summary);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to generate SAS URL for analysis: {AnalisisId}", analisis.id);
                    }
                }

                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(successResponse, req);
                successResponse.Headers.Add("Content-Type", "application/json");
                var responseData = new GetAnalisisResidencialByPathResponse
                {
                    Success = true,
                    AnalisisResidencial = FotosList,
                    TotalCount = analisisResult.Count,
                    Path = decodedPath,
                    TwinId = twinId,
                    Message = $"Se encontraron {analisisResult.Count} análisis residenciales en el path '{decodedPath}'"
                };
                await successResponse.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting photos by path");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAnalisisResidencialByPathResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Message = "Error obteniendo los análisis por path",
                    Path = path ?? "",
                    TwinId = twinId ?? ""
                }));
                return errorResponse;
            }
        }
    }

    public class UploadPhotoRequest
    {
        public string PhotoData { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public PhotoMetadataRequest Metadata { get; set; } = new();
    }

    public class PhotoMetadataRequest
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("date_taken")]
        public string DateTaken { get; set; } = string.Empty;
        [JsonPropertyName("location")]
        public string? Location { get; set; }
        [JsonPropertyName("country")]
        public string? Country { get; set; }
        [JsonPropertyName("place")]
        public string? Place { get; set; }
        [JsonPropertyName("people_in_photo")]
        public string? PeopleInPhoto { get; set; }
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;
        [JsonPropertyName("tags")]
        public string? Tags { get; set; }
        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }
        [JsonPropertyName("filename")]
        public string? FileName { get; set; }
        [JsonPropertyName("file_size")]
        public long? FileSize { get; set; }
        [JsonPropertyName("uploaded_at")]
        public string? UploadedAt { get; set; }
        [JsonPropertyName("photo_url")]
        public string? PhotoUrl { get; set; }
        [JsonPropertyName("photo_id")]
        public string? PhotoId { get; set; }
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }
    }

    public class UploadPhotoResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string PhotoId { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public TwinFx.Agents.PhotoMetadata? Metadata { get; set; }
    }

    public class GetPhotosResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public List<TwinFx.Agents.PhotoMetadata> Photos { get; set; } = new();
        public int TotalCount { get; set; }
    }

    public class FamilyPhotosResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public List<FamilyPhotoItem> Photos { get; set; } = new();
        public int Count { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? SearchTerm { get; set; }
        public string? Category { get; set; }
    }

    public class FamilyPhotoItem
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string DateTaken { get; set; } = string.Empty;
        public string? Location { get; set; }
        public string? Country { get; set; }
        public string? Place { get; set; }
        public string? PeopleInPhoto { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Tags { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string UploadDate { get; set; } = string.Empty;
        public string Type { get; set; } = "photo";
        public string PhotoUrl { get; set; } = string.Empty;
    }

    public class UpdatePhotoResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public TwinFx.Agents.PhotoMetadata? Photo { get; set; }
        public string PhotoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
    }

    public class GetPhotoByIdResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public TwinFx.Agents.PhotoMetadata? Photo { get; set; }
        public string PhotoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
    }

    public class SimpleUploadPhotoResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string UploadDate { get; set; } = string.Empty;
        public double ProcessingTimeSeconds { get; set; }
    }

    public class MultipartFormDataPart
    {
        public string Name { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string? StringValue { get; set; }
    }

    public class GetPhotosByPathResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public List<PhotoFileItem> Photos { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalFilesInPath { get; set; }
        public string Path { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public bool Recursive { get; set; }
        public string[] FileTypesFilter { get; set; } = Array.Empty<string>();
    }

    public class PhotoFileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public string CreatedOn { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class UploadHomeInsuranceResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string DocumentUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string UploadDate { get; set; } = string.Empty;
        public double ProcessingTimeSeconds { get; set; }
        public string AiAnalysisResult { get; set; } = string.Empty;
    }

    public class HomeInsuranceUploadResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string DocumentUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string UploadDate { get; set; } = string.Empty;
        public double ProcessingTimeSeconds { get; set; }
        public string AiAnalysisResult { get; set; } = string.Empty;
    }

    public class GetAnalisisResidencialByPathResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public List<AnalisisResidencialSummary> AnalisisResidencial { get; set; } = new();
        public int TotalCount { get; set; }
        public string Path { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
    }
}
