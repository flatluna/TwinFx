using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Services; 

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions para gestión de documentos de Mortgage/Hipoteca
/// Proporciona endpoints para subir y procesar documentos de hipoteca usando AI
/// </summary>
public class MortgageFunctions
{
    private readonly ILogger<MortgageFunctions> _logger;
    private readonly HomesCosmosDbService _homesService;
    private readonly IConfiguration _configuration;

    public MortgageFunctions(ILogger<MortgageFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Initialize Homes service
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new Models.CosmosDbSettings
        {
            Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? "",
            Key = configuration["Values:COSMOS_KEY"] ?? "",
            DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });

        // Create specific logger for HomesCosmosDbService
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var homesLogger = loggerFactory.CreateLogger<HomesCosmosDbService>();

        _homesService = new HomesCosmosDbService(
            homesLogger,
            cosmosOptions,
            configuration);
    }

    // ========================================
    // OPTIONS HANDLERS FOR CORS
    // ========================================

    [Function("UploadHomeMortgageOptions")]
    public async Task<HttpResponseData> HandleUploadHomeMortgageOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/upload-home-mortgage/{*filePath}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"???? OPTIONS preflight request for upload-home-mortgage/{twinId}");
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // UPLOAD HOME MORTGAGE FUNCTION
    // ========================================

    [Function("UploadHomeMortgage")]
    public async Task<HttpResponseData> UploadHomeMortgage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/{homeId}/upload-home-mortgage/{*filePath}")] HttpRequestData req,
        string twinId, string homeId,
        string filePath)
    {
        _logger.LogInformation("???? UploadHomeMortgage function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                return await CreateHomeMortgageErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(homeId))
            {
                _logger.LogError("? Home ID parameter is required");
                return await CreateHomeMortgageErrorResponse(req, "Home ID parameter is required", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(filePath))
            {
                return await CreateHomeMortgageErrorResponse(req, "File path is required in the URL. Use format: /twins/{twinId}/{homeId}/upload-home-mortgage/{path}", HttpStatusCode.BadRequest);
            }

            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (contentTypeHeader.Key == null || !contentTypeHeader.Value.Any(v => v.Contains("multipart/form-data")))
            {
                return await CreateHomeMortgageErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeHeader.Value.FirstOrDefault() ?? "";
            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                return await CreateHomeMortgageErrorResponse(req, "Invalid boundary in multipart/form-data", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"???? Processing home mortgage document upload for Twin ID: {twinId}, Home ID: {homeId}");
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();
            _logger.LogInformation($"?? Received multipart data: {bodyBytes.Length} bytes");

            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            var documentPart = parts.FirstOrDefault(p => p.Name == "document" || p.Name == "file" || p.Name == "pdf");
            if (documentPart == null || documentPart.Data == null || documentPart.Data.Length == 0)
            {
                return await CreateHomeMortgageErrorResponse(req, "No document file data found in request. Expected field name: 'document', 'file', or 'pdf'", HttpStatusCode.BadRequest);
            }

            string fileName = documentPart.FileName ?? "mortgage_document.pdf";
            var documentBytes = documentPart.Data;
            var completePath = filePath.Trim();

            _logger.LogInformation($"?? Using path from URL: {completePath}");

            // Extraer directorio del path completo
            var directory = Path.GetDirectoryName(completePath)?.Replace("\\", "/") ?? "";
            if (string.IsNullOrEmpty(directory))
            {
                directory = filePath;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                return await CreateHomeMortgageErrorResponse(req, "Invalid path: filename cannot be extracted from the provided URL path", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"?? Final upload details: Directory='{directory}', FileName='{fileName}', CompletePath='{completePath}'");
            _logger.LogInformation($"?? Document size: {documentBytes.Length} bytes");

            // Validar que sea un archivo PDF
            if (!IsValidPdfFile(fileName, documentBytes))
            {
                return await CreateHomeMortgageErrorResponse(req, "Invalid document format. Only PDF files are supported for mortgage documents", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"?? STEP 1: Uploading document to DataLake...");
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            var mimeType = "application/pdf";

            directory = filePath;
            using var documentStream = new MemoryStream(documentBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                directory,
                fileName,
                documentStream,
                mimeType);

            if (!uploadSuccess)
            {
                _logger.LogError("? Failed to upload document to DataLake");
                return await CreateHomeMortgageErrorResponse(req, "Failed to upload document to storage", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation("? Document uploaded successfully to DataLake");

            // PASO 2: Procesar el documento con AI usando AgentMortgage
            _logger.LogInformation($"?? STEP 2: Processing document with AI Mortgage analysis...");

            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentMortgageLogger = loggerFactory.CreateLogger<TwinFx.Agents.AgentMortgage>();
                var agentMortgage = new TwinFx.Agents.AgentMortgage(agentMortgageLogger, _configuration);
                
                // PASO 2.2: Llamar al método AiHomeMortgage pasando el homeId
                var aiAnalysisResult = await agentMortgage.AiHomeMortgage(twinId, directory, fileName, homeId);

                _logger.LogInformation("? AI mortgage analysis completed successfully");

                // Generar SAS URL para el documento
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                var responseData = new MortgageDocumentUploadResult
                {
                    Success = true,
                    Message = "Home mortgage document uploaded and analyzed successfully",
                    TwinId = twinId,
                    HomeId = homeId,
                    FilePath = completePath,
                    FileName = fileName,
                    Directory = directory,
                    ContainerName = twinId.ToLowerInvariant(),
                    DocumentUrl = sasUrl,
                    FileSize = documentBytes.Length,
                    MimeType = mimeType,
                    UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                    AiAnalysisResult = aiAnalysisResult
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation($"? Home mortgage upload and analysis completed successfully in {responseData.ProcessingTimeSeconds:F2} seconds");
                return response;
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "?? Document uploaded successfully but AI analysis failed");

                // Aún así devolver éxito de upload pero con error de AI
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                var responseData = new MortgageDocumentUploadResult
                {
                    Success = true,
                    Message = "Document uploaded successfully but AI analysis failed",
                    TwinId = twinId,
                    HomeId = homeId,
                    FilePath = completePath,
                    FileName = fileName,
                    Directory = directory,
                    ContainerName = twinId.ToLowerInvariant(),
                    DocumentUrl = sasUrl,
                    FileSize = documentBytes.Length,
                    MimeType = mimeType,
                    UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                    AiAnalysisResult = $"{{\"success\": false, \"errorMessage\": \"{aiEx.Message}\"}}"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in home mortgage upload");
            return await CreateHomeMortgageErrorResponse(req, $"Upload failed: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    // ========================================
    // GET HOME MORTGAGE LIST FUNCTION
    // ========================================

    [Function("GetHomeMortgageList")]
    public async Task<HttpResponseData> GetHomeMortgageList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/{homeId}/mortgage-list")] HttpRequestData req,
        string twinId, string homeId)
    {
        _logger.LogInformation("?? GetHomeMortgageList function triggered for Twin: {TwinId}, Home: {HomeId}", twinId, homeId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return errorResponse;
            }

            if (string.IsNullOrEmpty(homeId))
            {
                _logger.LogError("? Home ID parameter is required");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Home ID parameter is required"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return errorResponse;
            }

            // Inicializar MortgageCosmosDbService
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? "",
                Key = _configuration["Values:COSMOS_KEY"] ?? "",
                DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var mortgageCosmosLogger = loggerFactory.CreateLogger<MortgageCosmosDbService>();
            var mortgageCosmosService = new MortgageCosmosDbService(mortgageCosmosLogger, cosmosOptions, _configuration);

            // Obtener lista de documentos de hipoteca
            var mortgageDocuments = await mortgageCosmosService.GetMortgageDocumentsByHomeIdAsync(twinId, homeId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);

            await response.WriteStringAsync(JsonSerializer.Serialize(mortgageDocuments, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Mortgage documents list retrieved successfully for Twin: {TwinId}, Home: {HomeId}, Count: {Count}", 
                twinId, homeId, mortgageDocuments.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error retrieving mortgage documents list for Twin: {TwinId}, Home: {HomeId}", twinId, homeId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = $"Failed to retrieve mortgage documents: {ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return errorResponse;
        }
    }

    // ========================================
    // HELPER METHODS (copiados exactamente de HomeDocumentsFunctions)
    // ========================================

    private async Task<HttpResponseData> CreateHomeMortgageErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync(JsonSerializer.Serialize(new MortgageDocumentUploadResult
        {
            Success = false,
            ErrorMessage = errorMessage
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return response;
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

    private static bool IsValidPdfFile(string fileName, byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            return false;

        // Verificar extensión del archivo
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".pdf")
            return false;

        // Verificar magic numbers para PDF (%PDF)
        if (fileBytes.Length >= 4)
        {
            // PDF files start with %PDF
            if (fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && fileBytes[2] == 0x44 && fileBytes[3] == 0x46)
                return true;
        }

        return false;
    }

    private static bool IsTextContent(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return true;
        return contentType.StartsWith("text/") ||
               contentType.Contains("application/x-www-form-urlencoded") ||
               contentType.Contains("application/json");
    }
}

// ========================================
// RESPONSE MODELS ESPECÍFICOS PARA MORTGAGE
// ========================================

/// <summary>
/// Resultado del upload de documento de hipoteca/mortgage
/// </summary>
public class MortgageDocumentUploadResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string HomeId { get; set; } = string.Empty;
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