using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwinFx.Models;
using TwinFx.Services;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions para la gestión de fotos del diario
    /// Proporciona endpoints para subir múltiples fotos de URLs a entries de diario específicas
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class DiaryPhotosFunctions
    {
        private readonly ILogger<DiaryPhotosFunctions> _logger;
        private readonly IConfiguration _configuration;
        private readonly DiaryCosmosDbService _diaryService;

        public DiaryPhotosFunctions(ILogger<DiaryPhotosFunctions> logger, IConfiguration configuration, 
            DiaryCosmosDbService diaryService)
        {
            _logger = logger;
            _configuration = configuration;
            _diaryService = diaryService;
        }

        // ========================================
        // OPTIONS HANDLERS FOR CORS
        // ========================================
        [Function("DiaryPhotosOptions")]
        public async Task<HttpResponseData> HandleDiaryPhotosOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/{diaryId}/photos")] HttpRequestData req, string twinId, string diaryId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for twins/{TwinId}/diary/{DiaryId}/photos", twinId, diaryId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        // ========================================
        // PHOTO UPLOAD ENDPOINT
        // ========================================
        [Function("UploadDiaryPhotos")]
        public async Task<HttpResponseData> UploadDiaryPhotos([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/diary/{diaryId}/photos")] HttpRequestData req, string twinId, string diaryId)
        {
            _logger.LogInformation("?? UploadDiaryPhotos function triggered for DiaryId: {DiaryId}, Twin: {TwinId}", diaryId, twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(diaryId))
                {
                    return await CreateErrorResponse(req, "Twin ID and Diary ID parameters are required", HttpStatusCode.BadRequest);
                }

                // Verificar que la entrada del diario existe
                var diaryEntry = await _diaryService.GetDiaryEntryByIdAsync(diaryId, twinId);
                if (diaryEntry == null)
                {
                    return await CreateErrorResponse(req, "Diary entry not found", HttpStatusCode.NotFound);
                }

                // Procesar el JSON request body que contiene las URLs de las fotos
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
                }

                PhotoUploadRequest? uploadRequest;
                try
                {
                    uploadRequest = JsonSerializer.Deserialize<PhotoUploadRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "?? Invalid JSON in request body");
                    return await CreateErrorResponse(req, "Invalid JSON format in request body", HttpStatusCode.BadRequest);
                }

                if (uploadRequest?.PhotoUrls == null || !uploadRequest.PhotoUrls.Any())
                {
                    return await CreateErrorResponse(req, "PhotoUrls array is required and must contain at least one URL", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation("?? Processing {Count} photo URLs for diary entry {DiaryId}", uploadRequest.PhotoUrls.Count, diaryId);

                // Setup DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                var uploadResults = new List<PhotoUploadResult>();
                var successfulUploads = 0;

                // Procesar cada URL de foto
                for (int i = 0; i < uploadRequest.PhotoUrls.Count; i++)
                {
                    var photoUrl = uploadRequest.PhotoUrls[i];
                    
                    try
                    {
                        _logger.LogInformation("?? Processing photo {Index}/{Total}: {Url}", i + 1, uploadRequest.PhotoUrls.Count, photoUrl);

                        var result = await ProcessPhotoUrl(photoUrl, twinId, diaryId, dataLakeClient, i + 1);
                        uploadResults.Add(result);

                        if (result.Success)
                        {
                            successfulUploads++;
                            _logger.LogInformation("? Successfully uploaded photo {Index}: {FileName}", i + 1, result.FileName);
                        }
                        else
                        {
                            _logger.LogWarning("?? Failed to upload photo {Index}: {Error}", i + 1, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "? Error processing photo {Index}: {Url}", i + 1, photoUrl);
                        
                        uploadResults.Add(new PhotoUploadResult
                        {
                            Success = false,
                            ErrorMessage = $"Unexpected error: {ex.Message}",
                            PhotoUrl = photoUrl
                        });
                    }
                }

                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("?? Photo upload completed: {Successful}/{Total} successful in {ProcessingTime}ms", 
                    successfulUploads, uploadRequest.PhotoUrls.Count, processingTime);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new DiaryPhotosUploadResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DiaryId = diaryId,
                    TotalPhotos = uploadRequest.PhotoUrls.Count,
                    SuccessfulUploads = successfulUploads,
                    FailedUploads = uploadRequest.PhotoUrls.Count - successfulUploads,
                    Results = uploadResults,
                    ProcessingTimeMs = processingTime,
                    Message = $"Processed {uploadRequest.PhotoUrls.Count} photos: {successfulUploads} successful, {uploadRequest.PhotoUrls.Count - successfulUploads} failed"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in UploadDiaryPhotos for DiaryId: {DiaryId}, TwinId: {TwinId}", diaryId, twinId);
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        // ========================================
        // PHOTO RETRIEVAL ENDPOINT
        // ========================================
        [Function("GetDiaryPhotos")]
        public async Task<HttpResponseData> GetDiaryPhotos([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/diary/{diaryId}/photos")] HttpRequestData req, string twinId, string diaryId)
        {
            _logger.LogInformation("?? GetDiaryPhotos function triggered for DiaryId: {DiaryId}, Twin: {TwinId}", diaryId, twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(diaryId))
                {
                    return await CreateErrorResponse(req, "Twin ID and Diary ID parameters are required", HttpStatusCode.BadRequest);
                }

                // Verificar que la entrada del diario existe
                var diaryEntry = await _diaryService.GetDiaryEntryByIdAsync(diaryId, twinId);
                if (diaryEntry == null)
                {
                    return await CreateErrorResponse(req, "Diary entry not found", HttpStatusCode.NotFound);
                }

                // Setup DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Construir el path del directorio de fotos
                var photosDirectoryPath = $"diary/{diaryId}/photos/";
                
                _logger.LogInformation("?? Listing photos in directory: {Directory}", photosDirectoryPath);

                // Obtener lista de archivos en el directorio de fotos
                var photoFiles = await GetPhotosFromDirectory(dataLakeClient, photosDirectoryPath, twinId);

                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                _logger.LogInformation("?? Found {Count} photos in {ProcessingTime}ms", photoFiles.Count, processingTime);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new DiaryPhotosListResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DiaryId = diaryId,
                    TotalPhotos = photoFiles.Count,
                    Photos = photoFiles,
                    ProcessingTimeMs = processingTime,
                    Message = $"Found {photoFiles.Count} photos for diary entry {diaryId}"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in GetDiaryPhotos for DiaryId: {DiaryId}, TwinId: {TwinId}", diaryId, twinId);
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        // ========================================
        // HELPER METHODS
        // ========================================
        
        /// <summary>
        /// Procesa una URL de foto individual, la descarga y la sube al Data Lake
        /// </summary>
        private async Task<PhotoUploadResult> ProcessPhotoUrl(string photoUrl, string twinId, string diaryId, DataLakeClient dataLakeClient, int photoIndex)
        {
            try
            {
                // Validar la URL
                if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri))
                {
                    return new PhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid URL format",
                        PhotoUrl = photoUrl
                    };
                }

                // Descargar la imagen de la URL
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30); // 30 segundo timeout

                var response = await httpClient.GetAsync(photoUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return new PhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to download image: HTTP {response.StatusCode}",
                        PhotoUrl = photoUrl
                    };
                }

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                
                if (imageBytes.Length == 0)
                {
                    return new PhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Downloaded image is empty",
                        PhotoUrl = photoUrl
                    };
                }

                // Validar que sea una imagen válida
                var fileExtension = GetFileExtensionFromBytes(imageBytes);
                if (string.IsNullOrEmpty(fileExtension))
                {
                    // Intentar obtener extensión de la URL
                    fileExtension = GetFileExtensionFromUrl(photoUrl);
                }

                if (!IsValidImageFile(fileExtension, imageBytes))
                {
                    return new PhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "File is not a valid image format",
                        PhotoUrl = photoUrl
                    };
                }

                // Generar nombre único para el archivo
                var fileName = $"photo_{photoIndex}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
                var filePath = $"diary/{diaryId}/photos/{fileName}";

                _logger.LogInformation("?? Uploading to DataLake: Container={Container}, Path={Path}, Size={Size} bytes", 
                    twinId.ToLowerInvariant(), filePath, imageBytes.Length);

                // Subir al Data Lake
                using var fileStream = new MemoryStream(imageBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(),
                    $"diary/{diaryId}/photos",
                    fileName,
                    fileStream,
                    GetMimeTypeFromExtension(fileExtension)
                );

                if (!uploadSuccess)
                {
                    return new PhotoUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to upload to Data Lake storage",
                        PhotoUrl = photoUrl
                    };
                }

                // Generar SAS URL para acceso temporal
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

                return new PhotoUploadResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = filePath,
                    SasUrl = sasUrl ?? string.Empty,
                    FileSize = imageBytes.Length,
                    MimeType = GetMimeTypeFromExtension(fileExtension),
                    PhotoUrl = photoUrl,
                    UploadedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error processing photo URL: {Url}", photoUrl);
                return new PhotoUploadResult
                {
                    Success = false,
                    ErrorMessage = $"Processing error: {ex.Message}",
                    PhotoUrl = photoUrl
                };
            }
        }

        /// <summary>
        /// Obtiene todas las fotos de un directorio específico en Data Lake
        /// </summary>
        private async Task<List<DiaryPhotoInfo>> GetPhotosFromDirectory(DataLakeClient dataLakeClient, string directoryPath, string twinId)
        {
            try
            {
                _logger.LogInformation("?? Fetching files from directory: {Directory}", directoryPath);

                // Obtener lista de archivos del directorio
                var files = await dataLakeClient.ListFilesAsync(directoryPath);
                
                var photoList = new List<DiaryPhotoInfo>();

                foreach (var file in files)
                {
                    try
                    {
                        // Verificar que sea un archivo de imagen válido
                        if (!IsValidImageFileByName(file.Name))
                        {
                            _logger.LogDebug("?? Skipping non-image file: {FileName}", file.Name);
                            continue;
                        }

                        // Generar SAS URL para el archivo
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(file.Name, TimeSpan.FromHours(24));

                        var photoInfo = new DiaryPhotoInfo
                        {
                            FileName = Path.GetFileName(file.Name),
                            FilePath = file.Name,
                            SasUrl = sasUrl ?? string.Empty,
                            FileSize = file.Size,
                            MimeType = GetMimeTypeFromExtension(Path.GetExtension(file.Name)),
                            CreatedAt = file.CreatedOn,
                            LastModified = file.LastModified,
                            TwinId = twinId
                        };

                        photoList.Add(photoInfo);
                        
                        _logger.LogDebug("?? Added photo: {FileName}, Size: {Size} bytes", photoInfo.FileName, photoInfo.FileSize);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error processing file {FileName}, skipping", file.Name);
                        continue;
                    }
                }

                // Ordenar por fecha de creación (más recientes primero)
                photoList = photoList.OrderByDescending(p => p.CreatedAt).ToList();

                _logger.LogInformation("?? Successfully processed {Count} photos from directory", photoList.Count);
                
                return photoList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting photos from directory: {Directory}", directoryPath);
                return new List<DiaryPhotoInfo>();
            }
        }

        /// <summary>
        /// Verifica si un archivo es una imagen válida basándose en su nombre
        /// </summary>
        private bool IsValidImageFileByName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            
            return validExtensions.Contains(extension);
        }

        /// <summary>
        /// Valida si el archivo es una imagen válida
        /// </summary>
        private bool IsValidImageFile(string extension, byte[] fileData)
        {
            if (string.IsNullOrEmpty(extension) || fileData == null || fileData.Length == 0)
                return false;

            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            if (!validExtensions.Contains(extension.ToLowerInvariant()))
                return false;

            if (fileData.Length < 4)
                return false;

            // Verificar magic numbers
            if (fileData[0] == 0xFF && fileData[1] == 0xD8 && fileData[2] == 0xFF) return true; // JPEG
            if (fileData[0] == 0x89 && fileData[1] == 0x50 && fileData[2] == 0x4E && fileData[3] == 0x47) return true; // PNG
            if (fileData[0] == 0x47 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x38) return true; // GIF
            if (fileData.Length >= 12 && fileData[0] == 0x52 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x46 && 
                fileData[8] == 0x57 && fileData[9] == 0x45 && fileData[10] == 0x42 && fileData[11] == 0x50) return true; // WEBP
            if (fileData[0] == 0x42 && fileData[1] == 0x4D) return true; // BMP

            return true; // Allow other formats for now
        }

        /// <summary>
        /// Obtiene la extensión del archivo basándose en los magic numbers
        /// </summary>
        private string GetFileExtensionFromBytes(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return ".jpg";

            if (fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF) return ".jpg"; // JPEG
            if (fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47) return ".png"; // PNG
            if (fileBytes.Length >= 6 && fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46) return ".gif"; // GIF
            if (fileBytes.Length >= 12 && fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 && 
                fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50) return ".webp"; // WEBP
            if (fileBytes.Length >= 2 && fileBytes[0] == 0x42 && fileBytes[1] == 0x4D) return ".bmp"; // BMP

            return ".jpg"; // Default fallback
        }

        /// <summary>
        /// Obtiene la extensión del archivo desde la URL
        /// </summary>
        private string GetFileExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var extension = Path.GetExtension(uri.LocalPath);
                
                if (!string.IsNullOrEmpty(extension))
                {
                    return extension.ToLowerInvariant();
                }
            }
            catch
            {
                // Ignore errors
            }

            return ".jpg"; // Default
        }

        /// <summary>
        /// Obtiene el MIME type basándose en la extensión
        /// </summary>
        private string GetMimeTypeFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".web" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
        }

        /// <summary>
        /// Crea una respuesta de error con CORS headers
        /// </summary>
        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");
            
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage
            }));
            
            return response;
        }

        /// <summary>
        /// Agrega headers CORS a la respuesta
        /// </summary>
        private void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

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

    // ========================================
    // REQUEST/RESPONSE MODELS
    // ========================================

    /// <summary>
    /// Request model para subir fotos desde URLs
    /// </summary>
    public class PhotoUploadRequest
    {
        /// <summary>
        /// Lista de URLs de fotos para descargar y subir
        /// </summary>
        public List<string> PhotoUrls { get; set; } = new List<string>();
    }

    /// <summary>
    /// Resultado del procesamiento de una foto individual
    /// </summary>
    public class PhotoUploadResult
    {
        /// <summary>
        /// Indica si la subida fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// URL original de la foto
        /// </summary>
        public string PhotoUrl { get; set; } = string.Empty;

        /// <summary>
        /// Nombre del archivo generado
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Ruta completa del archivo en Data Lake
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// URL SAS para acceso temporal al archivo
        /// </summary>
        public string SasUrl { get; set; } = string.Empty;

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Fecha y hora de subida
        /// </summary>
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// Mensaje de error si Success = false
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta completa del endpoint de subida de fotos
    /// </summary>
    public class DiaryPhotosUploadResponse
    {
        /// <summary>
        /// Indica si la operación general fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// ID del Twin
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// ID del diary entry
        /// </summary>
        public string DiaryId { get; set; } = string.Empty;

        /// <summary>
        /// Número total de fotos procesadas
        /// </summary>
        public int TotalPhotos { get; set; }

        /// <summary>
        /// Número de subidas exitosas
        /// </summary>
        public int SuccessfulUploads { get; set; }

        /// <summary>
        /// Número de subidas fallidas
        /// </summary>
        public int FailedUploads { get; set; }

        /// <summary>
        /// Resultados detallados de cada foto
        /// </summary>
        public List<PhotoUploadResult> Results { get; set; } = new List<PhotoUploadResult>();

        /// <summary>
        /// Tiempo de procesamiento en milisegundos
        /// </summary>
        public double ProcessingTimeMs { get; set; }

        /// <summary>
        /// Mensaje descriptivo del resultado
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Información de una foto individual del diario
    /// </summary>
    public class DiaryPhotoInfo
    {
        /// <summary>
        /// Nombre del archivo
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Ruta completa del archivo en Data Lake
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// URL SAS para acceso temporal al archivo
        /// </summary>
        public string SasUrl { get; set; } = string.Empty;

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de creación del archivo
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Fecha de última modificación
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// ID del Twin propietario
        /// </summary>
        public string TwinId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta del endpoint para listar fotos de un diary entry
    /// </summary>
    public class DiaryPhotosListResponse
    {
        /// <summary>
        /// Indica si la operación fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// ID del Twin
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// ID del diary entry
        /// </summary>
        public string DiaryId { get; set; } = string.Empty;

        /// <summary>
        /// Número total de fotos encontradas
        /// </summary>
        public int TotalPhotos { get; set; }

        /// <summary>
        /// Lista de fotos con información detallada
        /// </summary>
        public List<DiaryPhotoInfo> Photos { get; set; } = new List<DiaryPhotoInfo>();

        /// <summary>
        /// Tiempo de procesamiento en milisegundos
        /// </summary>
        public double ProcessingTimeMs { get; set; }

        /// <summary>
        /// Mensaje descriptivo del resultado
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}