using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Services;
using TwinFx.Agents;

namespace TwinFx.Functions;

public class UploadDocumentFunction
{
    private readonly ILogger<UploadDocumentFunction> _logger;
    private readonly IConfiguration _configuration;

    public UploadDocumentFunction(ILogger<UploadDocumentFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("UploadDocumentOptions")]
    public async Task<HttpResponseData> HandleUploadOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "upload-document/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for upload-document/{twinId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UploadDocument")]
    public async Task<HttpResponseData> UploadDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload-document/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? UploadDocument function triggered");
        var startTime = DateTime.UtcNow; // Track processing time

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Processing document upload for Twin ID: {twinId}");

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body length: {requestBody.Length} characters");

            // Parse JSON request
            var uploadRequest = JsonSerializer.Deserialize<UploadDocumentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (uploadRequest == null)
            {
                _logger.LogError("? Failed to parse upload request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid upload request data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(uploadRequest.FileName))
            {
                _logger.LogError("? File name is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "File name is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.FileContent))
            {
                _logger.LogError("? File content is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "File content is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.DocumentType))
            {
                _logger.LogError("? Document type is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Document type is required (e.g., Invoice, DriversLicense, Contract, Education)"
                }));
                return badResponse;
            }

            // Validate EducationId for Education documents
            if (uploadRequest.DocumentType.ToUpperInvariant() == "EDUCATION" && string.IsNullOrEmpty(uploadRequest.EducationId))
            {
                _logger.LogError("? Education ID is required for Education documents");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Education ID is required when Document Type is 'Education'. Please provide the education record ID to update."
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Upload details: {uploadRequest.FileName}, DocumentType: {uploadRequest.DocumentType}, Container: {uploadRequest.ContainerName}, Path: {uploadRequest.FilePath}, EducationId: {uploadRequest.EducationId ?? "N/A"}");

            // Create DataLake client factory
            var dataLakeFactory = new DataLakeClientFactory(
                LoggerFactory.Create(builder => builder.AddConsole()),
                _configuration);
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            // Test connection first
            var connectionTest = await dataLakeClient.TestConnectionAsync();
            if (!connectionTest)
            {
                _logger.LogError("? Failed to connect to Azure Storage");
                var connectionErrorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                AddCorsHeaders(connectionErrorResponse, req);
                await connectionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to connect to Azure Storage. Please check configuration."
                }));
                return connectionErrorResponse;
            }

            // Convert base64 file content to bytes
            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(uploadRequest.FileContent);
                _logger.LogInformation($"?? File size: {fileBytes.Length} bytes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to decode base64 file content");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid base64 file content"
                }));
                return badResponse;
            }

            // Determine file path
            var filePath = !string.IsNullOrEmpty(uploadRequest.FilePath) 
                ? Path.Combine(uploadRequest.FilePath, uploadRequest.FileName).Replace("\\", "/")
                : uploadRequest.FileName;

            // Ensure documents directory if no specific path provided
            if (string.IsNullOrEmpty(uploadRequest.FilePath))
            {
                filePath = $"documents/{uploadRequest.FileName}";
            }

            _logger.LogInformation($"?? Final file path: {filePath}");

            // Determine MIME type
            var mimeType = GetMimeType(uploadRequest.FileName);
            _logger.LogInformation($"??? MIME type: {mimeType}");
            _logger.LogInformation($"?? Using directory-first upload pattern for better performance");

            // Parse file path into directory and filename for the new pattern
            var directoryPath = Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? "";
            var fileName = Path.GetFileName(filePath);
            
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogError("? Invalid file path - no filename found: {FilePath}", filePath);
                var invalidPathResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(invalidPathResponse, req);
                await invalidPathResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid file path - no filename found"
                }));
                return invalidPathResponse;
            }

            _logger.LogInformation($"?? Parsed path - Directory: '{directoryPath}', File: '{fileName}'");

            // Use the directory-first pattern with stream
            using var fileStream = new MemoryStream(fileBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(), // fileSystemName (must be lowercase for Data Lake Gen2)
                directoryPath,             // directoryName
                fileName,                  // fileName
                fileStream,                // fileData as Stream
                mimeType                   // mimeType
            );

            if (!uploadSuccess)
            {
                _logger.LogError("? Failed to upload file to DataLake");
                var uploadErrorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(uploadErrorResponse, req);
                await uploadErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to upload file to storage"
                }));
                return uploadErrorResponse;
            }

            _logger.LogInformation($"? File uploaded successfully: {filePath}");

            // Process document with AI to extract data and convert to structured format
            _logger.LogInformation("?? Processing document with AI for data extraction...");
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<ProcessDocumentDataAgent>();
                var processAgent = new ProcessDocumentDataAgent(agentLogger, _configuration);
                
                // Extract educationId only for Education documents
                string? educationId = null;
                if (uploadRequest.DocumentType.ToUpperInvariant() == "EDUCATION")
                {
                    educationId = uploadRequest.EducationId;
                    _logger.LogInformation("?? Extracted EducationId for Education document: {EducationId}", educationId ?? "NULL");
                }

                // Call the ProcessAiDocuments method with documentType and educationId
                var aiResult = await processAgent.ProcessAiDocuments(
                    twinId.ToLowerInvariant(),    // containerName (file system name)
                    directoryPath,                // filePath (directory within file system)
                    fileName,                     // fileName
                    uploadRequest.DocumentType,   // documentType
                    educationId                   // educationId (only for Education documents)
                );

                if (aiResult.Success)
                {
                    _logger.LogInformation("? AI document processing completed successfully");
                    _logger.LogInformation($"   ?? Document Type: {uploadRequest.DocumentType}");
                    _logger.LogInformation($"   ?? Processed text: {aiResult.ProcessedText.Length} characters");
                    _logger.LogInformation($"   ?? Document Intelligence: {(aiResult.ExtractionResult?.Success == true ? "?" : "?")}");
                    _logger.LogInformation($"   ?? AI Agent: {(aiResult.AgentResult?.Success == true ? "?" : "?")}");
                    
                    if (uploadRequest.DocumentType.ToUpperInvariant() == "INVOICE")
                    {
                        _logger.LogInformation($"   ?? Invoice Analysis: {(aiResult.InvoiceAnalysisResult?.Success == true ? "?" : "?")}");
                    }
                    else if (uploadRequest.DocumentType.ToUpperInvariant() == "EDUCATION")
                    {
                        _logger.LogInformation($"   ?? Education Analysis: Processed for education document");
                        if (!string.IsNullOrEmpty(uploadRequest.EducationId))
                        {
                            _logger.LogInformation($"   ?? Education record ID: {uploadRequest.EducationId}");
                            _logger.LogInformation($"   ?? Ready for updating TwinEducation container");
                            // TODO: In the future, pass educationId to ProcessDocumentDataAgent for direct education record update
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"   ?? Document Analysis: Processed for {uploadRequest.DocumentType} document");
                    }
                }
                else
                {
                    _logger.LogWarning($"?? AI document processing failed: {aiResult.ErrorMessage}");
                }
            }
            catch (Exception aiEx)
            {
                _logger.LogError(aiEx, "? Error during AI document processing");
                // Continue with the upload response even if AI processing fails
            }

            // Get file info to extract metadata
            var fileInfo = await dataLakeClient.GetFileInfoAsync(filePath);
            
            // Generate SAS URL for access
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

            _logger.LogInformation($"? File uploaded successfully: {filePath}");

            // Calculate processing time
            var processingTime = DateTime.UtcNow - startTime;
            
            var processingMessage = uploadRequest.DocumentType.ToUpperInvariant() switch
            {
                "EDUCATION" => $"El documento de educación ha sido procesado por la IA y guardado en el registro {uploadRequest.EducationId}",
                "INVOICE" => "El archivo de factura ha sido procesado por la IA exitosamente",
                _ => "El archivo ha sido procesado por la IA exitosamente"
            };
            
            _logger.LogInformation($"? Complete processing finished in {processingTime.TotalSeconds:F2} seconds");

            // Create simplified response for UI
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new UploadDocumentResponse
            {
                Success = true,
                TwinId = twinId,
                FileName = fileName,
                DocumentType = uploadRequest.DocumentType,
                EducationId = uploadRequest.EducationId, // Include EducationId in response
                FilePath = filePath,
                ContainerName = twinId.ToLowerInvariant(),
                FileSize = fileBytes.Length,
                MimeType = mimeType,
                Url = sasUrl,
                UploadedAt = DateTime.UtcNow,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = processingMessage,
                Metadata = fileInfo?.Metadata
            };


            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "? Error uploading document after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error durante el procesamiento del documento"
            }));
            
            return errorResponse;
        }
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            _ => "application/octet-stream"
        };
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

/// <summary>
/// Request model for document upload
/// </summary>
public class UploadDocumentRequest
{
    /// <summary>
    /// File name with extension
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Base64 encoded file content
    /// </summary>
    public string FileContent { get; set; } = string.Empty;

    /// <summary>
    /// Type of document being uploaded (e.g., "Invoice", "DriversLicense", "Contract", "Education", etc.)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// Education ID for Education documents (required when DocumentType is "Education")
    /// Used to update specific education record in TwinEducation container
    /// </summary>
    public string? EducationId { get; set; }

    /// <summary>
    /// Optional container name (defaults to twinId if not provided)
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Optional file path within container (defaults to "documents/" if not provided)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Optional MIME type (auto-detected if not provided)
    /// </summary>
    public string? MimeType { get; set; }
}

/// <summary>
/// Response model for document upload
/// </summary>
public class UploadDocumentResponse
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
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Uploaded file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Type of document that was uploaded
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// Education ID for Education documents (when applicable)
    /// </summary>
    public string? EducationId { get; set; }

    /// <summary>
    /// File path in storage
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Container name in storage
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// MIME type of the file
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// SAS URL for accessing the file
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// When the file was uploaded
    /// </summary>
    public DateTime UploadedAt { get; set; }

    /// <summary>
    /// File metadata from storage
    /// </summary>
    public IDictionary<string, string>? Metadata { get; set; }
}