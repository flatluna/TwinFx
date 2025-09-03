using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using TwinFx.Services;

namespace TwinFx.Plugins;

/// <summary>
/// Manage Pictures plugin for Azure Data Lake photo management
/// Provides photo search, listing, and management capabilities for Twin profiles
/// </summary>
public sealed class ManagePicturesPlugin
{
    private readonly ILogger<ManagePicturesPlugin> _logger;
    private readonly IConfiguration _configuration;
    private readonly DataLakeClientFactory _dataLakeFactory;

    public ManagePicturesPlugin(ILogger<ManagePicturesPlugin>? logger = null, IConfiguration? configuration = null)
    {
        _logger = logger ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ManagePicturesPlugin>();
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _dataLakeFactory = new DataLakeClientFactory(loggerFactory, _configuration);
    }

    /// <summary>
    /// Handle photo queries for a specific twin, finding and listing their photos
    /// </summary>
    [KernelFunction, Description("Search and manage photos for a specific twin ID using Azure Data Lake storage")]
    public async Task<string> HandlePhotoQuery(
        [Description("The user's message about photos")] string userInput,
        [Description("The twin ID to search for photos")] string twinId)
    {
        try
        {
            _logger.LogInformation("?? Starting photo query for twin: {TwinId} with input: {UserInput}", twinId, userInput);

            // Initialize the DataLake client for this twin
            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            
            // Get photo files from the profile/picture directory
            var photoFiles = await GetPhotoFilesAsync(dataLakeClient, twinId);
            
            if (!photoFiles.Any())
            {
                var noPhotosMessage = $"?? **No hay fotos disponibles para este Twin**\n\n" +
                                     $"?? **Twin ID:** {twinId}\n" +
                                     $"?? **Directorio:** profile/picture/\n" +
                                     $"?? **Resultado:** No se encontraron archivos de imagen\n\n" +
                                     $"?? **Sugerencias:**\n" +
                                     $"   • Subir fotos al directorio profile/picture/\n" +
                                     $"   • Verificar que los archivos sean imágenes (.jpg, .png, .gif, .webp)\n" +
                                     $"   • Comprobar los permisos de acceso al almacenamiento";

                _logger.LogInformation("?? No photos found for twin: {TwinId}", twinId);
                return noPhotosMessage;
            }

            // Format the response with photo information
            var response = FormatPhotoResponse(userInput, twinId, photoFiles);
            
            _logger.LogInformation("? Found {Count} photos for twin: {TwinId}", photoFiles.Count, twinId);
            
            return response;
        }
        catch (Exception ex)
        {
            var errorMessage = $"? Lo siento, tuve un problema al buscar tus fotos: {ex.Message}";
            _logger.LogError(ex, "? Error in HandlePhotoQuery for twin: {TwinId}", twinId);
            
            return errorMessage;
        }
    }

    /// <summary>
    /// Get detailed photo information including metadata
    /// </summary>
    [KernelFunction, Description("Get comprehensive photo information with metadata for a specific twin")]
    public async Task<string> GetPhotoDetails(
        [Description("The twin ID to get photo details for")] string twinId,
        [Description("Optional specific photo filename to get details for")] string? fileName = null)
    {
        try
        {
            _logger.LogInformation("?? Getting photo details for twin: {TwinId}, file: {FileName}", twinId, fileName ?? "all");

            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            
            if (!string.IsNullOrEmpty(fileName))
            {
                // Get details for specific file
                var filePath = $"profile/picture/{fileName}";
                var fileInfo = await dataLakeClient.GetFileInfoAsync(filePath);
                
                if (fileInfo == null)
                {
                    return $"? **Archivo no encontrado**\n\n" +
                           $"?? **Archivo:** {fileName}\n" +
                           $"?? **Twin ID:** {twinId}\n" +
                           $"?? **Ruta:** {filePath}";
                }

                return FormatSinglePhotoDetails(fileInfo, twinId);
            }
            else
            {
                // Get details for all photos
                var photoFiles = await GetPhotoFilesAsync(dataLakeClient, twinId);
                
                if (!photoFiles.Any())
                {
                    return $"?? **No hay fotos para analizar**\n\n?? **Twin ID:** {twinId}";
                }

                return FormatAllPhotoDetails(photoFiles, twinId);
            }
        }
        catch (Exception ex)
        {
            return $"? **Error obteniendo detalles de fotos**\n\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Error:** {ex.Message}";
        }
    }

    /// <summary>
    /// Upload a new photo for a twin
    /// </summary>
    [KernelFunction, Description("Upload a new photo for a twin using base64 encoded data")]
    public async Task<string> UploadPhoto(
        [Description("The twin ID to upload photo for")] string twinId,
        [Description("Base64 encoded photo data")] string photoBase64,
        [Description("File extension (jpg, png, gif, webp)")] string fileExtension)
    {
        try
        {
            _logger.LogInformation("?? Uploading photo for twin: {TwinId}, extension: {Extension}", twinId, fileExtension);

            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            
            // Upload the photo
            var uploadResult = await dataLakeClient.UploadPhotoBase64Async(photoBase64, fileExtension);
            
            if (uploadResult.Success)
            {
                return $"? **Foto subida exitosamente**\n\n" +
                       $"?? **Archivo:** {uploadResult.FileName}\n" +
                       $"?? **Ruta:** {uploadResult.FilePath}\n" +
                       $"?? **Twin ID:** {twinId}\n" +
                       $"?? **Tamaño:** {FormatFileSize(uploadResult.Size)}\n" +
                       $"?? **Tipo:** {uploadResult.MimeType}\n" +
                       $"?? **URL:** {uploadResult.Url}\n" +
                       $"?? **Subido:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            }
            else
            {
                return $"? **Error subiendo foto**\n\n" +
                       $"?? **Twin ID:** {twinId}\n" +
                       $"?? **Error:** {uploadResult.Error ?? "Error desconocido"}";
            }
        }
        catch (Exception ex)
        {
            return $"? **Error en subida de foto**\n\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Error:** {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a photo for a twin
    /// </summary>
    [KernelFunction, Description("Delete a specific photo for a twin")]
    public async Task<string> DeletePhoto(
        [Description("The twin ID to delete photo for")] string twinId,
        [Description("The filename of the photo to delete")] string fileName)
    {
        try
        {
            _logger.LogInformation("??? Deleting photo for twin: {TwinId}, file: {FileName}", twinId, fileName);

            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            var filePath = $"profile/picture/{fileName}";
            
            // Check if file exists first
            var fileInfo = await dataLakeClient.GetFileInfoAsync(filePath);
            if (fileInfo == null)
            {
                return $"? **Archivo no encontrado**\n\n" +
                       $"?? **Archivo:** {fileName}\n" +
                       $"?? **Twin ID:** {twinId}\n" +
                       $"?? **Ruta:** {filePath}";
            }

            // Delete the file
            var success = await dataLakeClient.DeleteFileAsync(filePath);
            
            if (success)
            {
                return $"? **Foto eliminada exitosamente**\n\n" +
                       $"?? **Archivo:** {fileName}\n" +
                       $"?? **Ruta:** {filePath}\n" +
                       $"?? **Twin ID:** {twinId}\n" +
                       $"?? **Eliminado:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            }
            else
            {
                return $"? **Error eliminando foto**\n\n" +
                       $"?? **Archivo:** {fileName}\n" +
                       $"?? **Twin ID:** {twinId}";
            }
        }
        catch (Exception ex)
        {
            return $"? **Error en eliminación de foto**\n\n" +
                   $"?? **Archivo:** {fileName}\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Error:** {ex.Message}";
        }
    }

    /// <summary>
    /// Get storage statistics for a twin's photos
    /// </summary>
    [KernelFunction, Description("Get storage statistics for a twin's photo collection")]
    public async Task<string> GetPhotoStatistics(
        [Description("The twin ID to get photo statistics for")] string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting photo statistics for twin: {TwinId}", twinId);

            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            var stats = await dataLakeClient.GetStorageStatisticsAsync();
            
            // Filter for image files only
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff" };
            var imageFiles = stats.FilesByType.Where(kvp => imageExtensions.Contains(kvp.Key.ToLowerInvariant()))
                                              .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            var totalImageCount = imageFiles.Values.Sum();
            
            return $"?? **Estadísticas de Fotos**\n\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Total de imágenes:** {totalImageCount}\n" +
                   $"?? **Total de archivos:** {stats.TotalFiles}\n" +
                   $"?? **Espacio total usado:** {stats.FormattedTotalSize}\n" +
                   $"?? **Contenedor:** {stats.ContainerName}\n" +
                   $"?? **Última actualización:** {stats.LastUpdated:yyyy-MM-dd HH:mm} UTC\n\n" +
                   $"?? **Tipos de imagen encontrados:**\n" +
                   string.Join("\n", imageFiles.Select(kvp => $"   • {kvp.Key}: {kvp.Value} archivo(s)"));
        }
        catch (Exception ex)
        {
            return $"? **Error obteniendo estadísticas**\n\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Error:** {ex.Message}";
        }
    }

    /// <summary>
    /// Get photo files from the DataLake client
    /// </summary>
    private async Task<List<PhotoFileInfo>> GetPhotoFilesAsync(DataLakeClient dataLakeClient, string twinId)
    {
        try
        {
            // Get files from the profile/picture directory
            var files = await dataLakeClient.ListFilesAsync("profile/picture/");
            
            // Filter for image files and convert to PhotoFileInfo
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff" };
            var photoFiles = files.Where(file => imageExtensions.Any(ext => 
                                        file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                                 .Select(file => new PhotoFileInfo
                                 {
                                     FileName = Path.GetFileName(file.Name),
                                     FilePath = file.Name,
                                     Size = file.Size,
                                     ContentType = file.ContentType,
                                     LastModified = file.LastModified,
                                     Url = file.Url,
                                     TwinId = twinId
                                 })
                                 .ToList();

            return photoFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting photo files for twin: {TwinId}", twinId);
            return new List<PhotoFileInfo>();
        }
    }

    /// <summary>
    /// Format the photo response similar to the Python version
    /// </summary>
    private static string FormatPhotoResponse(string userInput, string twinId, List<PhotoFileInfo> photoFiles)
    {
        var response = $"?? **Búsqueda de Fotos Completada**\n\n";
        response += $"?? **Consulta:** {userInput}\n";
        response += $"?? **Twin ID:** {twinId}\n";
        response += $"?? **Fotos encontradas:** {photoFiles.Count}\n\n";

        foreach (var photo in photoFiles.Take(10)) // Limit to 10 photos
        {
            response += $"?? **Archivo**: {photo.FileName}\n";
            response += $"?? **Ruta**: {photo.FilePath}\n";
            response += $"?? **Tamaño**: {FormatFileSize(photo.Size)}\n";
            response += $"?? **Tipo**: {photo.ContentType}\n";
            response += $"?? **Modificado**: {photo.LastModified:yyyy-MM-dd HH:mm}\n";
            
            if (!string.IsNullOrEmpty(photo.Url))
            {
                response += $"?? **URL**: {photo.Url}\n";
            }
            
            response += "\n";
        }

        if (photoFiles.Count > 10)
        {
            response += $"... y {photoFiles.Count - 10} fotos más\n\n";
        }

        response += $"? **Estado:** Búsqueda completada exitosamente\n";
        response += $"?? **Directorio:** profile/picture/\n";

        return response;
    }

    /// <summary>
    /// Format details for a single photo
    /// </summary>
    private static string FormatSinglePhotoDetails(BlobFileInfo fileInfo, string twinId)
    {
        return $"?? **Detalles de Foto**\n\n" +
               $"?? **Archivo:** {Path.GetFileName(fileInfo.Name)}\n" +
               $"?? **Ruta completa:** {fileInfo.Name}\n" +
               $"?? **Twin ID:** {twinId}\n" +
               $"?? **Tamaño:** {FormatFileSize(fileInfo.Size)}\n" +
               $"?? **Tipo MIME:** {fileInfo.ContentType}\n" +
               $"?? **Creado:** {fileInfo.CreatedOn:yyyy-MM-dd HH:mm} UTC\n" +
               $"?? **Modificado:** {fileInfo.LastModified:yyyy-MM-dd HH:mm} UTC\n" +
               $"??? **ETag:** {fileInfo.ETag}\n" +
               $"?? **URL:** {fileInfo.Url}\n\n" +
               $"?? **Metadata:**\n" +
               string.Join("\n", fileInfo.Metadata.Select(kvp => $"   • {kvp.Key}: {kvp.Value}"));
    }

    /// <summary>
    /// Format details for all photos
    /// </summary>
    private static string FormatAllPhotoDetails(List<PhotoFileInfo> photoFiles, string twinId)
    {
        var totalSize = photoFiles.Sum(p => p.Size);
        var response = $"?? **Resumen de Todas las Fotos**\n\n";
        response += $"?? **Twin ID:** {twinId}\n";
        response += $"?? **Total de fotos:** {photoFiles.Count}\n";
        response += $"?? **Tamaño total:** {FormatFileSize(totalSize)}\n";
        response += $"?? **Foto más reciente:** {photoFiles.Max(p => p.LastModified):yyyy-MM-dd HH:mm}\n\n";

        var grouped = photoFiles.GroupBy(p => Path.GetExtension(p.FileName).ToLowerInvariant())
                               .OrderByDescending(g => g.Count());

        response += $"?? **Por tipo de archivo:**\n";
        foreach (var group in grouped)
        {
            var totalSizeForType = group.Sum(p => p.Size);
            response += $"   • {group.Key}: {group.Count()} archivo(s) - {FormatFileSize(totalSizeForType)}\n";
        }

        return response;
    }

    /// <summary>
    /// Format file size in human readable format
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Search photos by specific criteria
    /// </summary>
    [KernelFunction, Description("Search photos by name, date, or other criteria for a specific twin")]
    public async Task<string> SearchPhotosByCriteria(
        [Description("The twin ID to search photos for")] string twinId,
        [Description("Search term to find in filename (optional)")] string? searchTerm = null,
        [Description("Number of days back to search (optional, e.g., 30 for last 30 days)")] int? daysBack = null,
        [Description("Minimum file size in MB (optional)")] decimal? minSizeMB = null,
        [Description("Maximum number of results to return")] int maxResults = 10)
    {
        try
        {
            _logger.LogInformation("?? Searching photos with criteria for twin: {TwinId}, term: {SearchTerm}, days: {DaysBack}, size: {MinSize}", 
                twinId, searchTerm, daysBack, minSizeMB);

            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            
            // Get all photo files first
            var allPhotoFiles = await GetPhotoFilesAsync(dataLakeClient, twinId);
            
            if (!allPhotoFiles.Any())
            {
                return $"?? **No hay fotos disponibles**\n\n?? **Twin ID:** {twinId}";
            }

            // Apply search criteria
            var filteredPhotos = allPhotoFiles.AsEnumerable();

            // Filter by search term in filename
            if (!string.IsNullOrEmpty(searchTerm))
            {
                filteredPhotos = filteredPhotos.Where(photo => 
                    photo.FileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    photo.FilePath.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            // Filter by date (last N days)
            if (daysBack.HasValue)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysBack.Value);
                filteredPhotos = filteredPhotos.Where(photo => photo.LastModified >= cutoffDate);
            }

            // Filter by minimum file size
            if (minSizeMB.HasValue)
            {
                var minSizeBytes = (long)(minSizeMB.Value * 1024 * 1024);
                filteredPhotos = filteredPhotos.Where(photo => photo.Size >= minSizeBytes);
            }

            // Order by most recent first and limit results
            var resultPhotos = filteredPhotos
                .OrderByDescending(photo => photo.LastModified)
                .Take(maxResults)
                .ToList();

            if (!resultPhotos.Any())
            {
                return $"?? **No se encontraron fotos con los criterios especificados**\n\n" +
                       $"?? **Twin ID:** {twinId}\n" +
                       $"?? **Término de búsqueda:** {searchTerm ?? "N/A"}\n" +
                       $"?? **Días atrás:** {daysBack?.ToString() ?? "N/A"}\n" +
                       $"?? **Tamaño mínimo:** {minSizeMB?.ToString() ?? "N/A"} MB\n" +
                       $"?? **Total de fotos disponibles:** {allPhotoFiles.Count}";
            }

            // Format the response with search criteria
            var response = $"?? **Búsqueda de Fotos Específica**\n\n";
            response += $"?? **Twin ID:** {twinId}\n";
            response += $"?? **Término de búsqueda:** {searchTerm ?? "Todos"}\n";
            response += $"?? **Período:** {(daysBack.HasValue ? $"Últimos {daysBack} días" : "Todo el tiempo")}\n";
            response += $"?? **Tamaño mínimo:** {(minSizeMB.HasValue ? $"{minSizeMB} MB" : "Sin límite")}\n";
            response += $"?? **Resultados encontrados:** {resultPhotos.Count} de {allPhotoFiles.Count} fotos totales\n\n";

            foreach (var photo in resultPhotos)
            {
                response += $"?? **Archivo**: {photo.FileName}\n";
                response += $"?? **Ruta**: {photo.FilePath}\n";
                response += $"?? **Tamaño**: {FormatFileSize(photo.Size)}\n";
                response += $"?? **Tipo**: {photo.ContentType}\n";
                response += $"?? **Modificado**: {photo.LastModified:yyyy-MM-dd HH:mm}\n";
                
                if (!string.IsNullOrEmpty(photo.Url))
                {
                    response += $"?? **URL**: {photo.Url}\n";
                }
                
                response += "\n";
            }

            response += $"? **Estado:** Búsqueda completada exitosamente\n";
            
            return response;
        }
        catch (Exception ex)
        {
            var errorMessage = $"? Error buscando fotos con criterios específicos: {ex.Message}";
            _logger.LogError(ex, "? Error in SearchPhotosByCriteria for twin: {TwinId}", twinId);
            
            return errorMessage;
        }
    }

    /// <summary>
    /// Find a specific photo by exact filename
    /// </summary>
    [KernelFunction, Description("Find a specific photo by exact filename for a twin")]
    public async Task<string> FindPhotoByName(
        [Description("The twin ID to search photos for")] string twinId,
        [Description("Exact filename to search for")] string fileName)
    {
        try
        {
            _logger.LogInformation("?? Finding specific photo for twin: {TwinId}, file: {FileName}", twinId, fileName);

            var dataLakeClient = _dataLakeFactory.CreateClient(twinId);
            
            // Try to get the specific file info directly
            var filePath = $"profile/picture/{fileName}";
            var fileInfo = await dataLakeClient.GetFileInfoAsync(filePath);
            
            if (fileInfo != null)
            {
                // File found directly
                return FormatSinglePhotoDetails(fileInfo, twinId);
            }

            // If not found directly, search in all photos
            var allPhotos = await GetPhotoFilesAsync(dataLakeClient, twinId);
            var matchingPhoto = allPhotos.FirstOrDefault(photo => 
                photo.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (matchingPhoto != null)
            {
                return $"?? **Foto Encontrada**\n\n" +
                       $"?? **Archivo:** {matchingPhoto.FileName}\n" +
                       $"?? **Ruta completa:** {matchingPhoto.FilePath}\n" +
                       $"?? **Twin ID:** {twinId}\n" +
                       $"?? **Tamaño:** {FormatFileSize(matchingPhoto.Size)}\n" +
                       $"?? **Tipo:** {matchingPhoto.ContentType}\n" +
                       $"?? **Modificado:** {matchingPhoto.LastModified:yyyy-MM-dd HH:mm}\n" +
                       $"?? **URL:** {matchingPhoto.Url}";
            }

            return $"? **Foto no encontrada**\n\n" +
                   $"?? **Archivo buscado:** {fileName}\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Directorio:** profile/picture/\n" +
                   $"?? **Sugerencia:** Verifica que el nombre del archivo sea exacto";
        }
        catch (Exception ex)
        {
            return $"? **Error buscando foto específica**\n\n" +
                   $"?? **Archivo:** {fileName}\n" +
                   $"?? **Twin ID:** {twinId}\n" +
                   $"?? **Error:** {ex.Message}";
        }
    }
}

/// <summary>
/// Information about a photo file
/// </summary>
public class PhotoFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string Url { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
}