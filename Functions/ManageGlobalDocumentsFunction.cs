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

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Function for managing global unstructured documents
    /// Handles contracts, policies, procedures, auto insurance, home insurance, etc.
    /// </summary>
    public class ManageGlobalDocumentsFunction
    {
        private readonly ILogger<ManageGlobalDocumentsFunction> _logger;
        private readonly IConfiguration _configuration;

        public ManageGlobalDocumentsFunction(ILogger<ManageGlobalDocumentsFunction> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// OPTIONS handler for CORS preflight requests
        /// </summary>
        [Function("ManageGlobalDocumentsOptions")]
        public async Task<HttpResponseData> HandleManageGlobalDocumentsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/global-documents")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"?? OPTIONS preflight request for global documents/{twinId}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Upload and process global unstructured documents
        /// POST /api/twins/{twinId}/global-documents
        /// </summary>
        [Function("UploadGlobalDocument")]
        public async Task<HttpResponseData> UploadGlobalDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/global-documents")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? UploadGlobalDocument function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID parameter is required");
                    return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                // Validate Content-Type for multipart/form-data
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

                _logger.LogInformation($"?? Processing global document upload for Twin ID: {twinId}");

                // Read multipart body as bytes to preserve binary data
                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                var bodyBytes = memoryStream.ToArray();

                _logger.LogInformation($"?? Received multipart data: {bodyBytes.Length} bytes");

                // Parse multipart form data
                var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                var filePart = parts.FirstOrDefault(p => p.Name == "file" || p.Name == "document");

                if (filePart == null || filePart.Data == null || filePart.Data.Length == 0)
                {
                    return await CreateErrorResponse(req, "No document file data found in request. Expected field name: 'file' or 'document'", HttpStatusCode.BadRequest);
                }

                var fileBytes = filePart.Data;

                // Extract additional metadata from form data
                var pathPart = parts.FirstOrDefault(p => p.Name == "path");
                var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
                var documentTypePart = parts.FirstOrDefault(p => p.Name == "documentType");
                var categoryPart = parts.FirstOrDefault(p => p.Name == "category");
                var descriptionPart = parts.FirstOrDefault(p => p.Name == "description");

                var documentPath = pathPart?.StringValue?.Trim() ?? "global-documents";
                var fileName = fileNamePart?.StringValue?.Trim() ?? filePart.FileName ?? $"document_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                var documentType = documentTypePart?.StringValue?.Trim() ?? "GLOBAL_DOCUMENT";
                var category = categoryPart?.StringValue?.Trim() ?? "GENERAL";
                var description = descriptionPart?.StringValue?.Trim() ?? "";

                // Ensure file has proper extension
                if (!fileName.Contains('.') && !string.IsNullOrEmpty(filePart.FileName))
                {
                    var originalExtension = Path.GetExtension(filePart.FileName);
                    if (!string.IsNullOrEmpty(originalExtension))
                    {
                        fileName += originalExtension;
                    }
                    else
                    {
                        fileName += ".pdf"; // Default to PDF for documents
                    }
                }

                // Clean up path (remove leading/trailing slashes, ensure forward slashes)
                documentPath = documentPath.Trim('/', '\\').Replace('\\', '/');
                if (string.IsNullOrEmpty(documentPath))
                {
                    documentPath = "global-documents"; // Default path
                }

                var fullFilePath = $"{documentPath}/{fileName}";

                _logger.LogInformation($"?? Document details: Size={fileBytes.Length} bytes, Path={documentPath}, FileName={fileName}, FullPath={fullFilePath}");
                _logger.LogInformation($"?? Document metadata: Type={documentType}, Category={category}, Description={description}");

                // Validate document file format
                if (!IsValidDocumentFile(fileName, fileBytes))
                {
                    return await CreateErrorResponse(req, "Invalid document file format. Supported formats: PDF, DOC, DOCX, TXT, RTF", HttpStatusCode.BadRequest);
                }

                // Upload document to DataLake
                _logger.LogInformation($"?? STEP 1: Uploading document to DataLake...");
                
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                var mimeType = GetMimeTypeFromExtension(Path.GetExtension(fileName));
                
                // Upload using the DataLake client
                using var documentStream = new MemoryStream(fileBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(), // Use twinId as container/filesystem name
                    documentPath,              // Directory path
                    fileName,                  // File name
                    documentStream,            // File data
                    mimeType);                 // MIME type

                if (!uploadSuccess)
                {
                    _logger.LogError("? Failed to upload document to DataLake");
                    return await CreateErrorResponse(req, "Failed to upload document to storage", HttpStatusCode.InternalServerError);
                }

                _logger.LogInformation("? Document uploaded successfully to DataLake");

                // Generate SAS URL for immediate access
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));

                // Create response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                var responseData = new UploadGlobalDocumentResponse
                {
                    Success = true,
                    Message = "Document uploaded successfully",
                    TwinId = twinId,
                    FilePath = fullFilePath,
                    FileName = fileName,
                    Directory = documentPath,
                    ContainerName = twinId.ToLowerInvariant(),
                    DocumentUrl = sasUrl,
                    DocumentType = documentType,
                    Category = category,
                    Description = description,
                    FileSize = fileBytes.Length,
                    MimeType = mimeType,
                    UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
                };

                // STEP 2: Process document with GlobalDocumentsAgent
                _logger.LogInformation($"?? STEP 2: Processing document with GlobalDocumentsAgent...");
                
                try
                {
                    var globalAgent = new GlobalDocumentsAgent(
                        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<GlobalDocumentsAgent>(),
                        _configuration);

                    var agentResult = await globalAgent.ProcessGlobalDocumentAsync(
                        containerName: twinId.ToLowerInvariant(),
                        filePath: documentPath,
                        fileName: fileName,
                        documentType: documentType,
                        category: category,
                        description: description);

                    if (agentResult.Success)
                    {
                        _logger.LogInformation("? GlobalDocumentsAgent processing completed successfully");
                        _logger.LogInformation("?? Agent Results: {Pages} pages, {Tables} tables, {Insights} insights", 
                            agentResult.PageData.Count, agentResult.TableData.Count, agentResult.KeyInsights.Count);

                        // Include agent results in response
                        responseData.AgentProcessing = new GlobalDocumentAgentResponse
                        {
                            Success = true,
                            TotalPages = agentResult.TotalPages,
                            ExecutiveSummary = agentResult.ExecutiveSummary,
                            PageDataCount = agentResult.PageData.Count,
                            TableDataCount = agentResult.TableData.Count,
                            KeyInsightsCount = agentResult.KeyInsights.Count,
                            ProcessedAt = agentResult.ProcessedAt,
                            StructuredData = agentResult.StructuredData
                        };
                    }
                    else
                    {
                        _logger.LogWarning("?? GlobalDocumentsAgent processing failed: {Error}", agentResult.ErrorMessage);
                        
                        // Include error in response but don't fail the upload
                        responseData.AgentProcessing = new GlobalDocumentAgentResponse
                        {
                            Success = false,
                            ErrorMessage = agentResult.ErrorMessage,
                            ProcessedAt = DateTime.UtcNow
                        };
                    }
                }
                catch (Exception agentEx)
                {
                    _logger.LogError(agentEx, "? Error in GlobalDocumentsAgent processing");
                    
                    // Include agent error in response but don't fail the upload
                    responseData.AgentProcessing = new GlobalDocumentAgentResponse
                    {
                        Success = false,
                        ErrorMessage = $"Agent processing failed: {agentEx.Message}",
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Update processing time to include agent processing
                responseData.ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds;

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation($"? Global document upload completed successfully in {responseData.ProcessingTimeSeconds:F2} seconds");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in global document upload");
                return await CreateErrorResponse(req, $"Upload failed: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// OPTIONS handler for CORS preflight requests - Get Documents
        /// </summary>
        [Function("GetGlobalDocumentsOptions")]
        public async Task<HttpResponseData> HandleGetGlobalDocumentsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/global-documents/list")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"?? OPTIONS preflight request for get global documents/{twinId}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// OPTIONS handler for CORS preflight requests - Get Categories
        /// </summary>
        [Function("GetGlobalDocumentCategoriesOptions")]
        public async Task<HttpResponseData> HandleGetGlobalDocumentCategoriesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/global-documents/categories")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"?? OPTIONS preflight request for get global document categories/{twinId}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Get all global documents for a Twin, optionally filtered by category
        /// GET /api/twins/{twinId}/global-documents/list?category={category}&limit={limit}&offset={offset}
        /// </summary>
        [Function("GetGlobalDocuments")]
        public async Task<HttpResponseData> GetGlobalDocuments(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/global-documents/list")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? GetGlobalDocuments function triggered for Twin: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID parameter is required");
                    return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                // Get query parameters
                var queryString = req.Url.Query;
                var category = GetQueryParameter(queryString, "category");
                var limit = int.TryParse(GetQueryParameter(queryString, "limit"), out var parsedLimit) ? parsedLimit : 50;
                var offset = int.TryParse(GetQueryParameter(queryString, "offset"), out var parsedOffset) ? parsedOffset : 0;

                _logger.LogInformation("?? Getting global documents for Twin: {TwinId}, Category: {Category}, Limit: {Limit}, Offset: {Offset}",
                    twinId, category ?? "ALL", limit, offset);

                // Create GlobalDocumentsAgent
                var globalAgent = new GlobalDocumentsAgent(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<GlobalDocumentsAgent>(),
                    _configuration);

                // Get documents using the agent
                var result = await globalAgent.GetGlobalDocumentsByTwinIdAsync(twinId, category, limit, offset);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);

                var responseData = new GetGlobalDocumentsResponse
                {
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage,
                    TwinId = result.TwinId,
                    Category = result.Category,
                    TotalDocuments = result.TotalDocuments,
                    Documents = result.DocumentsMain,
                    Limit = result.Limit,
                    Offset = result.Offset,
                    RetrievedAt = result.RetrievedAt
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation($"? Successfully retrieved {result.TotalDocuments} global documents for Twin: {twinId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting global documents for Twin: {TwinId}", twinId);
                return await CreateErrorResponse(req, $"Failed to get global documents: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Get document categories for a Twin with document counts
        /// GET /api/twins/{twinId}/global-documents/categories
        /// </summary>
        [Function("GetGlobalDocumentCategories")]
        public async Task<HttpResponseData> GetGlobalDocumentCategories(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/global-documents/categories")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? GetGlobalDocumentCategories function triggered for Twin: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID parameter is required");
                    return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                // Create GlobalDocumentsAgent
                var globalAgent = new GlobalDocumentsAgent(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<GlobalDocumentsAgent>(),
                    _configuration);

                // ? Get the complete documents list instead of just categories
                var documentsResult = await globalAgent.GetGlobalDocumentsByTwinIdAsync(twinId, null, 100, 0);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);

                // ? Return the same structure as GetGlobalDocuments
                var responseData = new GetGlobalDocumentsResponse
                {
                    Success = documentsResult.Success,
                    ErrorMessage = documentsResult.ErrorMessage,
                    TwinId = documentsResult.TwinId,
                    Category = documentsResult.Category,
                    TotalDocuments = documentsResult.TotalDocuments,
                    Documents = documentsResult.DocumentsMain, 
                    Offset = documentsResult.Offset,
                    RetrievedAt = documentsResult.RetrievedAt
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation($"? Successfully retrieved {documentsResult.TotalDocuments} global documents for Twin: {twinId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting global document categories for Twin: {TwinId}", twinId);
                return await CreateErrorResponse(req, $"Failed to get document categories: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Create error response with CORS headers
        /// </summary>
        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            
            await response.WriteStringAsync(JsonSerializer.Serialize(new UploadGlobalDocumentResponse
            {
                Success = false,
                ErrorMessage = errorMessage
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            
            return response;
        }

        /// <summary>
        /// Get query parameter from URL
        /// </summary>
        private static string? GetQueryParameter(string queryString, string parameterName)
        {
            if (string.IsNullOrEmpty(queryString))
                return null;

            // Remove leading '?' if present
            if (queryString.StartsWith("?"))
                queryString = queryString.Substring(1);

            var parameters = queryString.Split('&')
                .Select(param => param.Split('='))
                .Where(pair => pair.Length == 2)
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0]),
                    pair => Uri.UnescapeDataString(pair[1]),
                    StringComparer.OrdinalIgnoreCase);

            return parameters.TryGetValue(parameterName, out var value) ? value : null;
        }

        /// <summary>
        /// Extract boundary from Content-Type header
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
        /// Parse multipart form data (using the same pattern as TwinFamilyFunctions)
        /// </summary>
        private Task<List<GlobalDocumentPart>> ParseMultipartDataAsync(byte[] bodyBytes, string boundary)
        {
            var parts = new List<GlobalDocumentPart>();
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
                var part = new GlobalDocumentPart();

                // Parse Content-Disposition header
                var dispositionMatch = System.Text.RegularExpressions.Regex.Match(headers, @"Content-Disposition:\s*form-data;\s*name=""([^""]+)""(?:;\s*filename=""([^""]+)"")?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (dispositionMatch.Success)
                {
                    part.Name = dispositionMatch.Groups[1].Value;
                    if (dispositionMatch.Groups[2].Success)
                        part.FileName = dispositionMatch.Groups[2].Value;
                }

                // Parse Content-Type header
                var contentTypeMatch = System.Text.RegularExpressions.Regex.Match(headers, @"Content-Type:\s*([^\r\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (contentTypeMatch.Success)
                    part.ContentType = contentTypeMatch.Groups[1].Value.Trim();

                // Set data based on content type
                if (!string.IsNullOrEmpty(part.FileName) ||
                    (!string.IsNullOrEmpty(part.ContentType) && 
                     (part.ContentType.StartsWith("application/") || part.ContentType.StartsWith("text/"))))
                {
                    part.Data = contentBytes;
                }
                else
                {
                    part.StringValue = Encoding.UTF8.GetString(contentBytes).Trim();
                }

                parts.Add(part);
            }

            return Task.FromResult(parts);
        }

        /// <summary>
        /// Split byte array by separator
        /// </summary>
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

        /// <summary>
        /// Find byte pattern in data starting from a position
        /// </summary>
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

        /// <summary>
        /// Validate document file format
        /// </summary>
        private static bool IsValidDocumentFile(string fileName, byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return false;

            // Check file extension
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var validExtensions = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".pages" };
            
            if (!validExtensions.Contains(extension))
                return false;

            // Check file signature for common document formats
            return IsValidDocumentSignature(fileBytes, extension);
        }

        /// <summary>
        /// Check document file signature (magic numbers)
        /// </summary>
        private static bool IsValidDocumentSignature(byte[] fileBytes, string extension)
        {
            if (fileBytes == null || fileBytes.Length < 8)
                return false;

            return extension switch
            {
                ".pdf" => fileBytes.Length >= 4 && 
                         fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && 
                         fileBytes[2] == 0x44 && fileBytes[3] == 0x46, // %PDF
                ".doc" => fileBytes.Length >= 8 && 
                         fileBytes[0] == 0xD0 && fileBytes[1] == 0xCF && 
                         fileBytes[2] == 0x11 && fileBytes[3] == 0xE0, // OLE2 signature
                ".docx" => fileBytes.Length >= 4 && 
                          fileBytes[0] == 0x50 && fileBytes[1] == 0x4B && 
                          fileBytes[2] == 0x03 && fileBytes[3] == 0x04, // ZIP signature (DOCX is a ZIP)
                ".txt" => true, // Text files don't have specific signatures
                ".rtf" => fileBytes.Length >= 5 && 
                         Encoding.ASCII.GetString(fileBytes, 0, 5) == @"{\rtf",
                _ => true // Allow other extensions for now
            };
        }

        /// <summary>
        /// Get MIME type from file extension
        /// </summary>
        private static string GetMimeTypeFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".rtf" => "application/rtf",
                ".odt" => "application/vnd.oasis.opendocument.text",
                ".pages" => "application/x-iwork-pages-sffpages",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Add CORS headers to response
        /// </summary>
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

        /// <summary>
        /// Helper method to convert typed lists to object lists for JSON serialization
        /// ? IMPROVED: Preserves complex nested structures correctly
        /// </summary>
        private static List<object> ConvertToObjectList<T>(List<T>? list)
        {
            if (list == null) return new List<object>();
            
            // ? Instead of simple cast, ensure proper serialization
            var result = new List<object>();
            foreach (var item in list)
            {
                if (item != null)
                {
                    // For complex objects, ensure they are properly converted to dictionaries
                    if (item is DocumentPageData pageData)
                    {
                        result.Add(new Dictionary<string, object?>
                        {
                            ["pageNumber"] = pageData.PageNumber,
                            ["content"] = pageData.Content,
                            ["keyPoints"] = pageData.KeyPoints,
                            ["entities"] = new Dictionary<string, object?>
                            {
                                ["names"] = pageData.Entities.Names,
                                ["dates"] = pageData.Entities.Dates,
                                ["amounts"] = pageData.Entities.Amounts,
                                ["locations"] = pageData.Entities.Locations
                            }
                        });
                    }
                    else if (item is DocumentTableData tableData)
                    {
                        result.Add(new Dictionary<string, object?>
                        {
                            ["tableNumber"] = tableData.TableNumber,
                            ["pageNumber"] = tableData.PageNumber,
                            ["title"] = tableData.Title,
                            ["headers"] = tableData.Headers,
                            ["rows"] = tableData.Rows,
                            ["summary"] = tableData.Summary
                        });
                    }
                    else if (item is DocumentInsight insight)
                    {
                        result.Add(new Dictionary<string, object?>
                        {
                            ["type"] = insight.Type,
                            ["title"] = insight.Title,
                            ["description"] = insight.Description,
                            ["value"] = insight.Value,
                            ["importance"] = insight.Importance
                        });
                    }
                    else
                    {
                        // For other types, use simple cast
                        result.Add(item);
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// Helper method to convert StructuredData to proper object for JSON serialization
        /// ? PRESERVES: All nested object structures correctly
        /// </summary>
        private static object? ConvertStructuredDataToObject(object? structuredData)
        {
            if (structuredData == null) return null;

            // If it's already a GlobalDocumentStructuredData, convert it properly
            if (structuredData is GlobalDocumentStructuredData globalStructuredData)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = globalStructuredData.Success,
                    ["errorMessage"] = globalStructuredData.ErrorMessage,
                    ["documentType"] = globalStructuredData.DocumentType,
                    ["category"] = globalStructuredData.Category,
                    ["description"] = globalStructuredData.Description,
                    ["executiveSummary"] = globalStructuredData.ExecutiveSummary,
                    ["htmlOutput"] = globalStructuredData.HtmlOutput,
                    ["rawAIResponse"] = globalStructuredData.RawAIResponse,
                    ["processedAt"] = globalStructuredData.ProcessedAt,
                    // Convert nested page data
                    ["pageData"] = globalStructuredData.PageData.Select(p => new Dictionary<string, object?>
                    {
                        ["pageNumber"] = p.PageNumber,
                        ["content"] = p.Content,
                        ["keyPoints"] = p.KeyPoints,
                        ["entities"] = new Dictionary<string, object?>
                        {
                            ["names"] = p.Entities.Names,
                            ["dates"] = p.Entities.Dates,
                            ["amounts"] = p.Entities.Amounts,
                            ["locations"] = p.Entities.Locations
                        }
                    }).ToList(),
                    // Convert nested table data
                    ["tableData"] = globalStructuredData.TableData.Select(t => new Dictionary<string, object?>
                    {
                        ["tableNumber"] = t.TableNumber,
                        ["pageNumber"] = t.PageNumber,
                        ["title"] = t.Title,
                        ["headers"] = t.Headers,
                        ["rows"] = t.Rows,
                        ["summary"] = t.Summary
                    }).ToList(),
                    // Convert nested key insights
                    ["keyInsights"] = globalStructuredData.KeyInsights.Select(i => new Dictionary<string, object?>
                    {
                        ["type"] = i.Type,
                        ["title"] = i.Title,
                        ["description"] = i.Description,
                        ["value"] = i.Value,
                        ["importance"] = i.Importance
                    }).ToList()
                };
            }

            // If it's already a dictionary or other object, return as-is
            return structuredData;
        }
    }

    /// <summary>
    /// Represents a part of multipart form data for global documents
    /// </summary>
    public class GlobalDocumentPart
    {
        /// <summary>
        /// Form field name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Original filename (for file uploads)
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// Content type of the part
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// Binary data of the part
        /// </summary>
        public byte[]? Data { get; set; }

        /// <summary>
        /// String value for text form fields
        /// </summary>
        public string? StringValue { get; set; }
    }

    /// <summary>
    /// Response model for global document upload
    /// </summary>
    public class UploadGlobalDocumentResponse
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
        /// Twin ID (used as container name)
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// Complete file path in storage
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// File name only
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Directory path in storage
        /// </summary>
        public string Directory { get; set; } = string.Empty;

        /// <summary>
        /// Container/FileSystem name (same as TwinId in lowercase)
        /// </summary>
        public string ContainerName { get; set; } = string.Empty;

        /// <summary>
        /// SAS URL for accessing the document (24-hour expiration)
        /// </summary>
        public string DocumentUrl { get; set; } = string.Empty;

        /// <summary>
        /// Document type (CONTRACTS, POLICIES, PROCEDURES, AUTO_INSURANCE, HOME_INSURANCE, etc.)
        /// </summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>
        /// Document category for classification
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Document description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// MIME type of the uploaded file
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Upload date (ISO 8601 format)
        /// </summary>
        public string UploadDate { get; set; } = string.Empty;

        /// <summary>
        /// Total processing time in seconds
        /// </summary>
        public double ProcessingTimeSeconds { get; set; }

        /// <summary>
        /// Results from GlobalDocumentsAgent processing
        /// </summary>
        public GlobalDocumentAgentResponse? AgentProcessing { get; set; }
    }

    /// <summary>
    /// Response from GlobalDocumentsAgent processing
    /// </summary>
    public class GlobalDocumentAgentResponse
    {
        /// <summary>
        /// Whether the agent processing was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if agent processing failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Total number of pages processed
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Executive summary of the document
        /// </summary>
        public string ExecutiveSummary { get; set; } = string.Empty;

        /// <summary>
        /// Number of pages with structured data
        /// </summary>
        public int PageDataCount { get; set; }

        /// <summary>
        /// Number of tables extracted
        /// </summary>
        public int TableDataCount { get; set; }

        /// <summary>
        /// Number of key insights generated
        /// </summary>
        public int KeyInsightsCount { get; set; }

        /// <summary>
        /// When the agent processing was completed
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// Complete structured data from the agent (optional for detailed responses)
        /// </summary>
        public GlobalDocumentStructuredData? StructuredData { get; set; }
    }

    /// <summary>
    /// Represents processed page data from the global document
    /// </summary>
    public class GlobalDocumentPageData
    {
        /// <summary>
        /// Page number
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Text content of the page
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Images or other media on the page
        /// </summary>
        public List<GlobalDocumentImageData> Images { get; set; } = new List<GlobalDocumentImageData>();
    }

    /// <summary>
    /// Represents processed image data from the global document
    /// </summary>
    public class GlobalDocumentImageData
    {
        /// <summary>
        /// Image source URL or path
        /// </summary>
        public string Src { get; set; } = string.Empty;

        /// <summary>
        /// Alternative text description of the image
        /// </summary>
        public string Alt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents processed table data from the global document
    /// </summary>
    public class GlobalDocumentTableData
    {
        /// <summary>
        /// Table rows
        /// </summary>
        public List<GlobalDocumentTableRow> Rows { get; set; } = new List<GlobalDocumentTableRow>();
    }

    /// <summary>
    /// Represents a row in processed table data
    /// </summary>
    public class GlobalDocumentTableRow
    {
        /// <summary>
        /// Cell values in the row
        /// </summary>
        public List<string> Cells { get; set; } = new List<string>();
    }

    /// <summary>
    /// Response for getting global documents list
    /// </summary>
    public class GetGlobalDocumentsResponse
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
        /// Twin ID that was searched
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// Category filter applied (null if no filter)
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Total number of documents found
        /// </summary>
        public int TotalDocuments { get; set; }

        /// <summary>
        /// List of global documents
        /// </summary>
        public List<Document> Documents { get; set; } = new();

        /// <summary>
        /// Maximum number of documents requested
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Number of documents skipped
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// When the documents were retrieved
        /// </summary>
        public DateTime RetrievedAt { get; set; }
    }

    /// <summary>
    /// Information about a global document for UI display
    /// </summary>
    public class GlobalDocumentInfo
    {
        /// <summary>
        /// Document ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Twin ID (partition key)
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// Document type in Cosmos DB (always "global_document")
        /// </summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>
        /// Original file name
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// File path in storage
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Complete file path including container
        /// </summary>
        public string FullFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Container name (same as TwinId)
        /// </summary>
        public string ContainerName { get; set; } = string.Empty;

        /// <summary>
        /// SAS URL for document access
        /// </summary>
        public string DocumentUrl { get; set; } = string.Empty;

        /// <summary>
        /// Original document type (CONTRACTS, POLICIES, etc.)
        /// </summary>
        public string OriginalDocumentType { get; set; } = string.Empty;

        /// <summary>
        /// Document category (CASA_VIVIENDA, SEGUROS, etc.)
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Document description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Total number of pages
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Raw text content extracted from document
        /// </summary>
        public string TextContent { get; set; } = string.Empty;

        /// <summary>
        /// When document was processed
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// When document was created in system
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Whether processing was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Complete structured data from AI processing
        /// </summary>
        public object? StructuredData { get; set; }

        /// <summary>
        /// Executive summary of the document
        /// </summary>
        public string ExecutiveSummary { get; set; } = string.Empty;

        /// <summary>
        /// Page-by-page data with key points and entities
        /// </summary>
        public List<object> PageData { get; set; } = new();

        /// <summary>
        /// Extracted tables with headers and rows
        /// </summary>
        public List<object> TableData { get; set; } = new();

        /// <summary>
        /// Key insights extracted by AI
        /// </summary>
        public List<object> KeyInsights { get; set; } = new();

        /// <summary>
        /// Complete HTML output for document visualization
        /// </summary>
        public string? HtmlOutput { get; set; }

        /// <summary>
        /// Raw AI response from processing
        /// </summary>
        public string? RawAIResponse { get; set; }

        /// <summary>
        /// When AI processing was completed
        /// </summary>
        public DateTime AiProcessedAt { get; set; }

        /// <summary>
        /// Number of pages with structured data
        /// </summary>
        public int PageCount { get; set; }

        /// <summary>
        /// Number of tables extracted
        /// </summary>
        public int TableCount { get; set; }

        /// <summary>
        /// Number of key insights generated
        /// </summary>
        public int InsightCount { get; set; }

        /// <summary>
        /// Whether document has executive summary
        /// </summary>
        public bool HasExecutiveSummary { get; set; }

        /// <summary>
        /// Whether document has HTML output
        /// </summary>
        public bool HasHtmlOutput { get; set; }
    }

    /// <summary>
    /// Response for getting global document categories
    /// </summary>
    public class GetGlobalDocumentCategoriesResponse
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
        /// Twin ID that was searched
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// Total number of categories found
        /// </summary>
        public int TotalCategories { get; set; }

        /// <summary>
        /// List of categories with document counts
        /// </summary>
        public List<DocumentCategoryResponse> Categories { get; set; } = new();

        /// <summary>
        /// When the categories were retrieved
        /// </summary>
        public DateTime RetrievedAt { get; set; }
    }

    /// <summary>
    /// Category information for UI display
    /// </summary>
    public class DocumentCategoryResponse
    {
        /// <summary>
        /// Category name
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Number of documents in this category
        /// </summary>
        public int DocumentCount { get; set; }
    }
}