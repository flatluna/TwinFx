using Azure.Storage.Files.DataLake;
using Azure.Storage.Files.DataLake.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace TwinFx.Services;

/// <summary>
/// Azure Data Lake Storage Gen2 Client
/// Handles file operations for twin-specific file systems in Azure Data Lake Storage
/// </summary>
public class DataLakeClient
{
    private readonly ILogger<DataLakeClient> _logger;
    private readonly string _twinId;
    private readonly string _fileSystemName;
    private readonly string _accountName;
    private readonly string _accountKey;
    private readonly DataLakeServiceClient _dataLakeServiceClient;

    /// <summary>
    /// Initialize DataLake client for a specific twin
    /// </summary>
    /// <param name="twinId">The twin identifier to create file system-specific storage</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="configuration">Configuration for Azure Storage credentials</param>
    public DataLakeClient(string twinId, ILogger<DataLakeClient> logger, IConfiguration configuration)
    {
        _logger = logger;
        _twinId = twinId;
        _fileSystemName = twinId.ToLowerInvariant(); // File system names must be lowercase

        try
        {
            // Get credentials from configuration with detailed logging
            _logger.LogInformation("🔍 Reading Azure Data Lake Storage configuration...");
            
            _accountName = configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_NAME") 
                           ?? configuration["AZURE_STORAGE_ACCOUNT_NAME"]
                           ?? throw new ArgumentException("AZURE_STORAGE_ACCOUNT_NAME not found in configuration");
            
            _accountKey = configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_KEY") 
                          ?? configuration["AZURE_STORAGE_ACCOUNT_KEY"]
                          ?? throw new ArgumentException("AZURE_STORAGE_ACCOUNT_KEY not found in configuration");

            _logger.LogInformation("✅ Configuration values loaded:");
            _logger.LogInformation("   📦 Account Name: {AccountName}", _accountName);
            _logger.LogInformation("   🔑 Account Key: {KeyLength} characters", _accountKey?.Length ?? 0);

            // Validate credentials
            if (string.IsNullOrWhiteSpace(_accountName))
            {
                throw new ArgumentException("Azure Storage Account Name is empty or whitespace");
            }

            if (string.IsNullOrWhiteSpace(_accountKey))
            {
                throw new ArgumentException("Azure Storage Account Key is empty or whitespace");
            }

            // Initialize Data Lake service client
            var serviceUri = $"https://{_accountName}.dfs.core.windows.net";
            var credential = new Azure.Storage.StorageSharedKeyCredential(_accountName, _accountKey);
            
            _logger.LogInformation("🔗 Creating DataLakeServiceClient with service URI: {ServiceUri}", serviceUri);
            _dataLakeServiceClient = new DataLakeServiceClient(new Uri(serviceUri), credential);

            // Ensure file system exists asynchronously
            _ = Task.Run(async () => await EnsureFileSystemExistsAsync());

            _logger.LogInformation("✅ DataLake client initialized for Twin: {TwinId}", _twinId);
            _logger.LogInformation("   • File System: {FileSystemName}", _fileSystemName);
            _logger.LogInformation("   • Storage Account: {AccountName}", _accountName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize DataLake client");
            _logger.LogError("🔍 Debug Information:");
            _logger.LogError("   • Twin ID: {TwinId}", twinId);
            _logger.LogError("   • File System Name: {FileSystemName}", _fileSystemName);
            _logger.LogError("   • Account Name from config: {AccountName}", 
                configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_NAME") ?? "NULL");
            _logger.LogError("   • Account Key available: {HasKey}", 
                !string.IsNullOrEmpty(configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_KEY")));
            
            throw;
        }
    }

    /// <summary>
    /// Create file system if it doesn't exist
    /// </summary>
    private async Task EnsureFileSystemExistsAsync()
    {
        try
        {
            _logger.LogInformation("🔍 Ensuring file system exists: {FileSystemName}", _fileSystemName);
            
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            
            var response = await fileSystemClient.CreateIfNotExistsAsync();
            
            if (response != null)
            {
                _logger.LogInformation("✅ Created file system: {FileSystemName}", _fileSystemName);
            }
            else
            {
                _logger.LogInformation("✅ File system already exists: {FileSystemName}", _fileSystemName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error ensuring file system exists: {FileSystemName}", _fileSystemName);
            _logger.LogError("🔍 File system creation failed with error: {ErrorMessage}", ex.Message);
            
            if (ex.Message.Contains("account information") || ex.Message.Contains("authentication"))
            {
                _logger.LogError("💡 Suggestion: Check Azure Storage account name and key in configuration");
                _logger.LogError("💡 Current account: {AccountName}", _accountName);
                _logger.LogError("💡 Key length: {KeyLength}", _accountKey?.Length ?? 0);
            }
        }
    }

    /// <summary>
    /// Upload file content to the twin's file system using directory-first pattern
    /// </summary>
    /// <param name="fileContent">File content as bytes</param>
    /// <param name="filePath">Path within the file system (e.g., "documents/file.pdf")</param>
    /// <param name="mimeType">MIME type of the file</param>
    /// <returns>True if upload successful, False otherwise</returns>
    public async Task<bool> UploadFileAsync(byte[] fileContent, string filePath, string? mimeType = null)
    {
        try
        {
            // Parse file path into directory and filename
            var directoryPath = Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? "";
            var fileName = Path.GetFileName(filePath);

            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogError("❌ Invalid file path: {FilePath}", filePath);
                return false;
            }

            // Use the directory-first pattern as suggested
            return await UploadFileAsync(_fileSystemName, directoryPath, fileName, new MemoryStream(fileContent), mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error uploading file {FilePath} to file system {FileSystemName}", filePath, _fileSystemName);
            return false;
        }
    }

    /// <summary>
    /// Upload file using the proper directory-first pattern (following your suggested approach)
    /// </summary>
    /// <param name="fileSystemName">File system name</param>
    /// <param name="directoryName">Directory name within the file system</param>
    /// <param name="fileName">File name</param>
    /// <param name="fileData">File data as stream</param>
    /// <param name="mimeType">Optional MIME type</param>
    /// <returns>True if upload successful, False otherwise</returns>
    public async Task<bool> UploadFileAsync(string fileSystemName, string directoryName, string fileName, Stream fileData, string? mimeType = null)
    {
        try
        {
            var serviceClient = _dataLakeServiceClient;
            
            // Ensure file system exists
            await EnsureFileSystemExistsAsync();
            
            // Get file system client
            DataLakeFileSystemClient fileSystemClient = serviceClient.GetFileSystemClient(fileSystemName);
            
            // Set stream position to beginning
            fileData.Position = 0;
            
            // Create upload options
            var uploadOptions = new DataLakeFileUploadOptions();
            
            if (!string.IsNullOrEmpty(mimeType))
            {
                uploadOptions.HttpHeaders = new PathHttpHeaders
                {
                    ContentType = mimeType
                };
            }

            uploadOptions.Metadata = new Dictionary<string, string>
            {
                ["twinId"] = _twinId,
                ["uploadedAt"] = DateTime.UtcNow.ToString("O"),
                ["source"] = "TwinAgent"
            };

            DataLakeFileClient fileClient;
            
            // Get directory client (create directory if it doesn't exist) or use root
            if (!string.IsNullOrEmpty(directoryName))
            {
                DataLakeDirectoryClient directoryClient = fileSystemClient.GetDirectoryClient(directoryName);
                await directoryClient.CreateIfNotExistsAsync();
                
                // Get file client from directory (following your pattern)
                fileClient = directoryClient.GetFileClient(fileName);
            }
            else
            {
                // If no directory specified, get file client directly from file system root
                fileClient = fileSystemClient.GetFileClient(fileName);
            }
            
            // Upload file using the provided stream (following your exact pattern)
            await fileClient.UploadAsync(fileData, uploadOptions);
            
            // Verify the contents of the file (following your pattern)
            PathProperties properties = await fileClient.GetPropertiesAsync();
            
            _logger.LogInformation("✅ Successfully uploaded {FileName} to directory {DirectoryName} in file system {FileSystemName}", 
                fileName, directoryName ?? "root", fileSystemName);
            _logger.LogInformation("   📊 File size: {Size} bytes", properties.ContentLength);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error uploading file {FileName} to directory {DirectoryName} in file system {FileSystemName}", 
                fileName, directoryName ?? "root", fileSystemName);
            return false;
        }
    }

    /// <summary>
    /// Upload a base64 encoded photo to the profile/picture directory
    /// </summary>
    /// <param name="photoBase64">Base64 encoded photo data</param>
    /// <param name="fileExtension">File extension (e.g., "jpg", "png")</param>
    /// <returns>Upload result with success status and file info</returns>
    public async Task<PhotoUploadResult> UploadPhotoBase64Async(string photoBase64, string fileExtension)
    {
        try
        {
            // Decode base64 data
            var photoData = Convert.FromBase64String(photoBase64);

            // Generate filename with timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"profile_{timestamp}.{fileExtension}";
            var filePath = $"profile/picture/{filename}";

            // Determine MIME type
            var mimeTypeMap = new Dictionary<string, string>
            {
                ["jpg"] = "image/jpeg",
                ["jpeg"] = "image/jpeg",
                ["png"] = "image/png",
                ["gif"] = "image/gif",
                ["webp"] = "image/webp"
            };
            var mimeType = mimeTypeMap.GetValueOrDefault(fileExtension.ToLowerInvariant(), "application/octet-stream");

            // Upload the file
            var success = await UploadFileAsync(photoData, filePath, mimeType);

            if (success)
            {
                var sasUrl = await GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
                
                return new PhotoUploadResult
                {
                    Success = true,
                    FilePath = filePath,
                    FileName = filename,
                    ContainerName = _fileSystemName,
                    Url = sasUrl,
                    MimeType = mimeType,
                    Size = photoData.Length
                };
            }
            else
            {
                return new PhotoUploadResult
                {
                    Success = false,
                    Error = "Upload failed"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error uploading base64 photo");
            return new PhotoUploadResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get URL for the main profile photo
    /// </summary>
    /// <returns>SAS URL for the profile photo</returns>
    public async Task<string> GetProfilePhotoUrlAsync()
    {
        var profilePath = "profile/picture/profile.jpg";
        return await GenerateSasUrlAsync(profilePath, TimeSpan.FromHours(24));
    }

    /// <summary>
    /// Delete a file from the file system
    /// </summary>
    /// <param name="filePath">Path to the file within the file system</param>
    /// <returns>True if deletion successful, False otherwise</returns>
    public async Task<bool> DeleteFileAsync(string filePath)
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var fileClient = fileSystemClient.GetFileClient(filePath);

            await fileClient.DeleteIfExistsAsync();
            _logger.LogInformation("✅ Successfully deleted {FilePath} from file system {FileSystemName}", filePath, _fileSystemName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting file {FilePath} from file system {FileSystemName}", filePath, _fileSystemName);
            return false;
        }
    }

    /// <summary>
    /// Generate a SAS URL for accessing a file
    /// </summary>
    /// <param name="filePath">Path to the file within the file system</param>
    /// <param name="validFor">How long the URL should be valid</param>
    /// <returns>SAS URL for the file</returns>
    public async Task<string> GenerateSasUrlAsync(string filePath, TimeSpan validFor)
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var fileClient = fileSystemClient.GetFileClient(filePath);

            // Check if file exists
            var exists = await fileClient.ExistsAsync();
            if (!exists.Value)
            {
                _logger.LogWarning("⚠️ File {FilePath} does not exist", filePath);
                return string.Empty;
            }

            // Generate SAS token with read permissions
            var sasBuilder = new DataLakeSasBuilder
            {
                FileSystemName = _fileSystemName,
                Path = filePath,
                Resource = "f", // file
                ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
            };

            sasBuilder.SetPermissions(DataLakeSasPermissions.Read);

            var credential = new Azure.Storage.StorageSharedKeyCredential(_accountName, _accountKey);
            var sasToken = sasBuilder.ToSasQueryParameters(credential);
            
            var sasUrl = $"{fileClient.Uri}?{sasToken}";
            return sasUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating SAS URL for {FilePath}", filePath);
            return string.Empty;
        }
    }
    
    /// <summary>
    /// List files in the file system with optional prefix filter
    /// </summary>
    /// <param name="prefix">Optional prefix to filter files</param>
    /// <returns>List of file information</returns>
    public async Task<List<BlobFileInfo>> ListFilesAsync(string prefix = "")
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var fileInfos = new List<BlobFileInfo>();

            await foreach (var pathItem in fileSystemClient.GetPathsAsync(prefix))
            {
                if (!pathItem.IsDirectory.GetValueOrDefault())
                {
                    var fileClient = fileSystemClient.GetFileClient(pathItem.Name);
                    
                    // Get detailed properties for files
                    try
                    {
                        var properties = await fileClient.GetPropertiesAsync();
                        
                        fileInfos.Add(new BlobFileInfo
                        {
                            Name = pathItem.Name,
                            Size = pathItem.ContentLength ?? 0,
                            ContentType = properties.Value.ContentType ?? "application/octet-stream",
                            LastModified = pathItem.LastModified.DateTime,
                            ETag = pathItem.ETag.ToString(),
                            Metadata = properties.Value.Metadata,
                            Url = fileClient.Uri.ToString(),
                            CreatedOn = properties.Value.CreatedOn.DateTime
                        });
                    }
                    catch (Exception ex)
                    {
                        // If we can't get detailed properties, use basic info
                        _logger.LogWarning("⚠️ Could not get detailed properties for {FileName}: {Error}", pathItem.Name, ex.Message);
                        
                        fileInfos.Add(new BlobFileInfo
                        {
                            Name = pathItem.Name,
                            Size = pathItem.ContentLength ?? 0,
                            ContentType = "application/octet-stream",
                            LastModified = pathItem.LastModified.DateTime,
                            ETag = pathItem.ETag.ToString(),
                            Metadata = new Dictionary<string, string>(),
                            Url = fileClient.Uri.ToString(),
                            CreatedOn = pathItem.LastModified.DateTime
                        });
                    }
                }
            }

            _logger.LogInformation("✅ Listed {Count} files with prefix '{Prefix}' from file system {FileSystemName}", 
                fileInfos.Count, prefix, _fileSystemName);
            
            return fileInfos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error listing files with prefix '{Prefix}' from file system {FileSystemName}", prefix, _fileSystemName);
            return new List<BlobFileInfo>();
        }
    }

    /// <summary>
    /// Download a file from the file system
    /// </summary>
    /// <param name="filePath">Path to the file within the file system</param>
    /// <returns>File content as byte array, or null if not found</returns>
    public async Task<byte[]?> DownloadFileAsync(string filePath)
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var fileClient = fileSystemClient.GetFileClient(filePath);

            var exists = await fileClient.ExistsAsync();
            if (!exists.Value)
            {
                _logger.LogWarning("⚠️ File {FilePath} does not exist in file system {FileSystemName}", filePath, _fileSystemName);
                return null;
            }

            var response = await fileClient.ReadAsync();
            using var memoryStream = new MemoryStream();
            await response.Value.Content.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error downloading file {FilePath} from file system {FileSystemName}", filePath, _fileSystemName);
            return null;
        }
    }

    /// <summary>
    /// Get metadata for a specific file
    /// </summary>
    /// <param name="filePath">Path to the file within the file system</param>
    /// <returns>File properties and metadata</returns>
    public async Task<BlobFileInfo?> GetFileInfoAsync(string filePath)
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var fileClient = fileSystemClient.GetFileClient(filePath);

            var exists = await fileClient.ExistsAsync();
            if (!exists.Value)
            {
                return null;
            }

            var properties = await fileClient.GetPropertiesAsync();
            
            return new BlobFileInfo
            {
                Name = filePath,
                Size = properties.Value.ContentLength,
                ContentType = properties.Value.ContentType ?? "application/octet-stream",
                LastModified = properties.Value.LastModified.DateTime,
                ETag = properties.Value.ETag.ToString(),
                Metadata = properties.Value.Metadata,
                Url = fileClient.Uri.ToString(),
                CreatedOn = properties.Value.CreatedOn.DateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting file info for {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Upload multiple files in batch
    /// </summary>
    /// <param name="files">Dictionary of file path to file content</param>
    /// <param name="mimeType">Default MIME type for all files</param>
    /// <returns>Results of each upload operation</returns>
    public async Task<Dictionary<string, bool>> UploadFilesAsync(Dictionary<string, byte[]> files, string? mimeType = null)
    {
        var results = new Dictionary<string, bool>();
        var tasks = files.Select(async kvp =>
        {
            var success = await UploadFileAsync(kvp.Value, kvp.Key, mimeType);
            return new KeyValuePair<string, bool>(kvp.Key, success);
        });

        var completedTasks = await Task.WhenAll(tasks);
        
        foreach (var result in completedTasks)
        {
            results[result.Key] = result.Value;
        }

        return results;
    }

    /// <summary>
    /// Copy a file to another location within the same file system
    /// </summary>
    /// <param name="sourcePath">Source file path</param>
    /// <param name="destinationPath">Destination file path</param>
    /// <returns>True if copy successful</returns>
    public async Task<bool> CopyFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var sourceFileClient = fileSystemClient.GetFileClient(sourcePath);
            var destinationFileClient = fileSystemClient.GetFileClient(destinationPath);

            var sourceExists = await sourceFileClient.ExistsAsync();
            if (!sourceExists.Value)
            {
                _logger.LogWarning("⚠️ Source file {SourcePath} does not exist", sourcePath);
                return false;
            }

            // Download source file and upload to destination
            var sourceContent = await DownloadFileAsync(sourcePath);
            if (sourceContent != null)
            {
                var sourceProperties = await sourceFileClient.GetPropertiesAsync();
                var contentType = sourceProperties.Value.ContentType;
                
                var success = await UploadFileAsync(sourceContent, destinationPath, contentType);
                
                if (success)
                {
                    _logger.LogInformation("✅ Successfully copied {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error copying file from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Get storage statistics for the twin's file system
    /// </summary>
    /// <returns>Storage usage statistics</returns>
    public async Task<StorageStatistics> GetStorageStatisticsAsync()
    {
        try
        {
            var files = await ListFilesAsync();
            var totalSize = files.Sum(f => f.Size);
            var filesByType = files.GroupBy(f => Path.GetExtension(f.Name).ToLowerInvariant())
                                  .ToDictionary(g => g.Key, g => g.Count());

            return new StorageStatistics
            {
                TotalFiles = files.Count,
                TotalSize = totalSize,
                FilesByType = filesByType,
                ContainerName = _fileSystemName,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting storage statistics");
            return new StorageStatistics
            {
                TotalFiles = 0,
                TotalSize = 0,
                FilesByType = new Dictionary<string, int>(),
                ContainerName = _fileSystemName,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Test the Azure Data Lake Storage connection
    /// </summary>
    /// <returns>True if connection is successful, false otherwise</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            _logger.LogInformation("🧪 Testing Azure Data Lake Storage connection...");
            
            // Try to get service properties as a connection test
            var serviceProperties = await _dataLakeServiceClient.GetPropertiesAsync();
            
            _logger.LogInformation("✅ Connection test successful!");
            _logger.LogInformation("   📊 Service version: {Version}", serviceProperties.Value.DefaultServiceVersion);
            _logger.LogInformation("   🔧 CORS rules: {CorsCount}", serviceProperties.Value.Cors?.Count ?? 0);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Connection test failed");
            _logger.LogError("🔍 Error details: {ErrorMessage}", ex.Message);
            
            if (ex.Message.Contains("account information") || ex.Message.Contains("authentication"))
            {
                _logger.LogError("💡 This appears to be an authentication issue");
                _logger.LogError("💡 Please verify:");
                _logger.LogError("   • Storage account name: {AccountName}", _accountName);
                _logger.LogError("   • Storage account key length: {KeyLength} characters", _accountKey?.Length ?? 0);
                _logger.LogError("   • Storage account exists and is accessible");
                _logger.LogError("   • Data Lake Storage Gen2 is enabled on the storage account");
            }
            
            return false;
        }
    }

    /// <summary>
    /// List files in a specific directory using the directory-first pattern
    /// </summary>
    /// <param name="directoryName">Directory name to list files from</param>
    /// <returns>List of file information in the specified directory</returns>
    public async Task<List<BlobFileInfo>> ListFilesInDirectoryAsync(string directoryName = "")
    {
        try
        {
            var fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(_fileSystemName);
            var fileInfos = new List<BlobFileInfo>();

            _logger.LogInformation("📂 Listing files in directory: '{DirectoryName}' for file system: {FileSystemName}", 
                directoryName, _fileSystemName);

            // Use the GetPathsAsync pattern with directory enumeration
            IAsyncEnumerator<PathItem> enumerator = 
                fileSystemClient.GetPathsAsync(directoryName).GetAsyncEnumerator();

            await enumerator.MoveNextAsync();
            PathItem item = enumerator.Current;

            while (item != null)
            {
                // Only process files, not directories
                if (!item.IsDirectory.GetValueOrDefault())
                {
                    var fileClient = fileSystemClient.GetFileClient(item.Name);
                    
                    // Get detailed properties for files
                    try
                    {
                        var properties = await fileClient.GetPropertiesAsync();
                        
                        fileInfos.Add(new BlobFileInfo
                        {
                            Name = item.Name,
                            Size = item.ContentLength ?? 0,
                            ContentType = properties.Value.ContentType ?? "application/octet-stream",
                            LastModified = item.LastModified.DateTime,
                            ETag = item.ETag.ToString(),
                            Metadata = properties.Value.Metadata,
                            Url = fileClient.Uri.ToString(),
                            CreatedOn = properties.Value.CreatedOn.DateTime
                        });
                    }
                    catch (Exception ex)
                    {
                        // If we can't get detailed properties, use basic info
                        _logger.LogWarning("⚠️ Could not get detailed properties for {FileName}: {Error}", item.Name, ex.Message);
                        
                        fileInfos.Add(new BlobFileInfo
                        {
                            Name = item.Name,
                            Size = item.ContentLength ?? 0,
                            ContentType = "application/octet-stream",
                            LastModified = item.LastModified.DateTime,
                            ETag = item.ETag.ToString(),
                            Metadata = new Dictionary<string, string>(),
                            Url = fileClient.Uri.ToString(),
                            CreatedOn = item.LastModified.DateTime
                        });
                    }
                }

                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                item = enumerator.Current;
            }

            await enumerator.DisposeAsync();

            _logger.LogInformation("✅ Listed {Count} files in directory '{DirectoryName}' from file system {FileSystemName}", 
                fileInfos.Count, directoryName, _fileSystemName);
            
            return fileInfos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error listing files in directory '{DirectoryName}' from file system {FileSystemName}", 
                directoryName, _fileSystemName);
            return new List<BlobFileInfo>();
        }
    }
}

/// <summary>
/// Result of photo upload operation
/// </summary>
public class PhotoUploadResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Information about a file
/// </summary>
public class BlobFileInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public DateTime CreatedOn { get; set; }
    public string ETag { get; set; } = string.Empty;
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Storage usage statistics
/// </summary>
public class StorageStatistics
{
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public Dictionary<string, int> FilesByType { get; set; } = new();
    public string ContainerName { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Format total size in human readable format
    /// </summary>
    public string FormattedTotalSize
    {
        get
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = TotalSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}

/// <summary>
/// Factory for creating DataLake clients
/// </summary>
public class DataLakeClientFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;

    public DataLakeClientFactory(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _loggerFactory = loggerFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Create a DataLake client for a specific twin
    /// </summary>
    /// <param name="twinId">Twin identifier</param>
    /// <returns>Configured DataLake client</returns>
    public DataLakeClient CreateClient(string twinId)
    {
        var logger = _loggerFactory.CreateLogger<DataLakeClient>();
        return new DataLakeClient(twinId, logger, _configuration);
    }
}

/// <summary>
/// Extension methods for DataLake operations
/// </summary>
public static class DataLakeExtensions
{
    /// <summary>
    /// Upload a text file to the data lake
    /// </summary>
    /// <param name="client">DataLake client</param>
    /// <param name="content">Text content</param>
    /// <param name="filePath">File path in file system</param>
    /// <param name="encoding">Text encoding (default: UTF-8)</param>
    /// <returns>Upload success</returns>
    public static async Task<bool> UploadTextFileAsync(this DataLakeClient client, string content, string filePath, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var bytes = encoding.GetBytes(content);
        return await client.UploadFileAsync(bytes, filePath, "text/plain");
    }

    /// <summary>
    /// Download a text file from the data lake
    /// </summary>
    /// <param name="client">DataLake client</param>
    /// <param name="filePath">File path in file system</param>
    /// <param name="encoding">Text encoding (default: UTF-8)</param>
    /// <returns>Text content or null if not found</returns>
    public static async Task<string?> DownloadTextFileAsync(this DataLakeClient client, string filePath, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var bytes = await client.DownloadFileAsync(filePath);
        return bytes != null ? encoding.GetString(bytes) : null;
    }

    /// <summary>
    /// Upload a JSON object to the data lake
    /// </summary>
    /// <param name="client">DataLake client</param>
    /// <param name="obj">Object to serialize</param>
    /// <param name="filePath">File path in file system</param>
    /// <returns>Upload success</returns>
    public static async Task<bool> UploadJsonAsync<T>(this DataLakeClient client, T obj, string filePath)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        return await client.UploadTextFileAsync(json, filePath);
    }

    /// <summary>
    /// Download and deserialize a JSON object from the data lake
    /// </summary>
    /// <param name="client">DataLake client</param>
    /// <param name="filePath">File path in file system</param>
    /// <returns>Deserialized object or default if not found</returns>
    public static async Task<T?> DownloadJsonAsync<T>(this DataLakeClient client, string filePath)
    {
        var json = await client.DownloadTextFileAsync(filePath);
        if (string.IsNullOrEmpty(json))
            return default;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }
}