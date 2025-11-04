using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinFx.Agents;
using TwinFx.Services;

namespace TwinFx.Functions
{
    public  class StructuredDocumentsFunction
    {
        private readonly ILogger<StructuredDocumentsFunction> _logger;
        private readonly IConfiguration _configuration;

        public StructuredDocumentsFunction(ILogger<StructuredDocumentsFunction> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [Function("GetCSVFileByIdOptions")]
        public async Task<HttpResponseData> HandleGetCSVFileByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-csv-file/{twinId}/{csvFileId}")] HttpRequestData req,
            string twinId,
            string csvFileId)
        {
            _logger.LogInformation($"📊 OPTIONS preflight request for get-csv-file/{twinId}/{csvFileId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetCSVFileById")]
        public async Task<HttpResponseData> GetCSVFileById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-csv-file/{twinId}/{csvFileId}")] HttpRequestData req,
            string twinId,
            string csvFileId)
        {
            _logger.LogInformation("📊 GetCSVFileById function triggered for TwinId: {TwinId}, CsvFileId: {CsvFileId}", twinId, csvFileId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(csvFileId))
                {
                    _logger.LogError("❌ CSV File ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "CSV File ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving complete CSV file data for TwinId: {TwinId}, CsvFileId: {CsvFileId}", twinId, csvFileId);

                // Create StructuredDocumentsCosmosDB service instance
                var cosmosService = CreateStructuredDocumentsCosmosService();
                
                // Get complete CSV file data by ID (including all records)
                var csvFileData = await cosmosService.GetCSVFileDataAsync(csvFileId, twinId);

                if (csvFileData == null)
                {
                    _logger.LogWarning("⚠️ CSV file not found for TwinId: {TwinId}, CsvFileId: {CsvFileId}", twinId, csvFileId);
                    
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = $"CSV file with ID '{csvFileId}' not found for Twin '{twinId}'",
                        TwinId = twinId,
                        CsvFileId = csvFileId,
                        RetrievedAt = DateTime.UtcNow
                    }));
                    return notFoundResponse;
                }

                // Create response with complete CSV data
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    CsvFileId = csvFileId,
                    File = csvFileData,
                    TotalRecords = csvFileData.Records?.Count ?? 0,
                    TotalColumns = csvFileData.TotalColumns,
                    RetrievedAt = DateTime.UtcNow,
                    Message = $"Retrieved CSV file '{csvFileData.FileName}' successfully with {csvFileData.Records?.Count ?? 0} records",
                    Note = "This response contains complete CSV data including all records."
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Successfully retrieved complete CSV file for TwinId: {TwinId}, CsvFileId: {CsvFileId}, Records: {RecordCount}", 
                    twinId, csvFileId, csvFileData.Records?.Count ?? 0);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving CSV file for TwinId: {TwinId}, CsvFileId: {CsvFileId}", twinId, csvFileId);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    CsvFileId = csvFileId,
                    RetrievedAt = DateTime.UtcNow
                }));

                return errorResponse;
            }
        }

        [Function("GetCSVFilesMetadataOptions")]
        public async Task<HttpResponseData> HandleGetCSVFilesMetadataOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-csv-files-metadata/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"📊 OPTIONS preflight request for get-csv-files-metadata/{twinId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetCSVFilesMetadata")]
        public async Task<HttpResponseData> GetCSVFilesMetadata(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-csv-files-metadata/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🚀 GetCSVFilesMetadata function triggered for TwinId: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving CSV files metadata (without records) for TwinId: {TwinId}", twinId);

                // Create StructuredDocumentsCosmosDB service instance
                var cosmosService = CreateStructuredDocumentsCosmosService();
                
                // Get CSV files metadata for the specified TwinId (without records for faster access)
                var csvMetadata = await cosmosService.GetCSVFilesMetadataByTwinIdAsync(twinId);

                // Create response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    TotalFiles = csvMetadata.Count,
                    Files = csvMetadata,
                    RetrievedAt = DateTime.UtcNow,
                    Message = $"Retrieved {csvMetadata.Count} CSV files metadata successfully",
                    Note = "This response contains only metadata (no CSV records) for faster access. Use get-csv-files endpoint to retrieve full data including records."
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Successfully retrieved {Count} CSV files metadata for TwinId: {TwinId}", csvMetadata.Count, twinId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving CSV files metadata for TwinId: {TwinId}", twinId);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    RetrievedAt = DateTime.UtcNow
                }));

                return errorResponse;
            }
        }

        [Function("GetCSVFilesOptions")]
        public async Task<HttpResponseData> HandleGetCSVFilesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-csv-files/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"📊 OPTIONS preflight request for get-csv-files/{twinId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetCSVFiles")]
        public async Task<HttpResponseData> GetCSVFiles(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-csv-files/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📊 GetCSVFiles function triggered for TwinId: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving CSV files for TwinId: {TwinId}", twinId);

                // Create StructuredDocumentsCosmosDB service instance
                var cosmosService = CreateStructuredDocumentsCosmosService();
                
                // Get CSV files for the specified TwinId
                var csvFiles = await cosmosService.GetCSVFilesByTwinIdAsync(twinId);

                // Create response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    TotalFiles = csvFiles.Count,
                    Files = csvFiles,
                    RetrievedAt = DateTime.UtcNow,
                    Message = $"Retrieved {csvFiles.Count} CSV files successfully"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Successfully retrieved {Count} CSV files for TwinId: {TwinId}", csvFiles.Count, twinId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving CSV files for TwinId: {TwinId}", twinId);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    RetrievedAt = DateTime.UtcNow
                }));

                return errorResponse;
            }
        }

        [Function("UploadDocumentCSV")]
        public async Task<HttpResponseData> UploadDocumentCSV(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload-document-csv/{twinId}")] HttpRequestData req,
        string twinId)
        {
            _logger.LogInformation("📊 UploadDocumentCSV function triggered");
            var startTime = DateTime.UtcNow; // Track processing time

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"📊 Processing CSV document upload for Twin ID: {twinId}");

                // Read request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");

                // Parse JSON request
                var uploadRequest = JsonSerializer.Deserialize<UploadDocumentRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (uploadRequest == null)
                {
                    _logger.LogError("❌ Failed to parse upload request data");
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
                    _logger.LogError("❌ File name is required");
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
                    _logger.LogError("❌ File content is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                    {
                        Success = false,
                        ErrorMessage = "File content is required"
                    }));
                    return badResponse;
                }

                // Ensure document type is CSV
                string documentType = "CSV";
                _logger.LogInformation($"📊 Upload details: {uploadRequest.FileName}, DocumentType: {documentType}, Container: {uploadRequest.ContainerName}, Path: {uploadRequest.FilePath}");

                // Create DataLake client factory
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Test connection first
                var connectionTest = await dataLakeClient.TestConnectionAsync();
                if (!connectionTest)
                {
                    _logger.LogError("❌ Failed to connect to Azure Storage");
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
                    _logger.LogInformation($"📊 File size: {fileBytes.Length} bytes");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to decode base64 file content");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid base64 file content"
                    }));
                    return badResponse;
                }

                // Determine file path for CSV files
                var filePath = "estructurado/CSV";

                // Ensure CSV directory if no specific path provided
                if (string.IsNullOrEmpty(uploadRequest.FilePath))
                {
                    filePath = $"csv/{uploadRequest.FileName}";
                }

                _logger.LogInformation($"📊 Final file path: {filePath}");

                // Determine MIME type for CSV
                var mimeType = GetMimeType(uploadRequest.FileName);
                _logger.LogInformation($"📄 MIME type: {mimeType}");

                // Parse file path into directory and filename
                var directoryPath = filePath;
                var fileName = uploadRequest.FileName;

                if (string.IsNullOrEmpty(fileName))
                {
                    
                    var invalidPathResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(invalidPathResponse, req);
                    await invalidPathResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid file path - no filename found"
                    }));
                    return invalidPathResponse;
                }

          
                // Upload file to DataLake
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
                    _logger.LogError("❌ Failed to upload file to DataLake");
                    var uploadErrorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(uploadErrorResponse, req);
                    await uploadErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to upload file to storage"
                    }));
                    return uploadErrorResponse;
                }

                
                ProcessAiDocumentsResult? aiResult = null;
                try
                {
                    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var agentLogger = loggerFactory.CreateLogger<StructuredDocumentsAgent>();
                    var structuredAgent = new StructuredDocumentsAgent(agentLogger, _configuration);

                    // Call the ProcessAiCSVDocuments method
                    aiResult = await structuredAgent.ProcessAiCSVDocuments(
                        twinId.ToLowerInvariant(),    // containerName (file system name)
                        directoryPath,                // filePath (directory within file system)
                        fileName,                     // fileName
                        documentType,                 // documentType = "CSV"
                        null                          // educationId (not applicable for CSV)
                    );

                    if (aiResult.Success)
                    {
                        _logger.LogInformation("✅ CSV processing completed successfully");
                        _logger.LogInformation($"   📊 Document Type: {documentType}");
                        _logger.LogInformation($"   📄 Processed text: {aiResult.ProcessedText?.Length ?? 0} characters");
                        _logger.LogInformation($"   💾 CSV data saved to CosmosDB successfully");
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ CSV processing failed: {aiResult.ErrorMessage}");
                    }
                }
                catch (Exception aiEx)
                {
                    _logger.LogError(aiEx, "❌ Error during CSV processing with StructuredDocumentsAgent");
                    // Continue with the upload response even if CSV processing fails
                }

                // Get file info to extract metadata
                var fileInfo = await dataLakeClient.GetFileInfoAsync(filePath);

                // Generate SAS URL for access
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                var processingMessage = aiResult?.Success == true 
                    ? "El archivo CSV ha sido procesado por la IA y guardado en CosmosDB exitosamente"
                    : "El archivo CSV ha sido subido exitosamente";

                _logger.LogInformation($"✅ Complete processing finished in {processingTime.TotalSeconds:F2} seconds");

                // Create response for UI
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UploadDocumentResponse
                {
                    Success = true,
                    TwinId = twinId,
                    FileName = fileName,
                    DocumentType = documentType,
                    EducationId = null, // Not applicable for CSV
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
                _logger.LogError(ex, "❌ Error uploading CSV document after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadDocumentResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error durante el procesamiento del documento CSV"
                }));

                return errorResponse;
            }
        }

        [Function("AnalyzeCSVFileOptions")]
        public async Task<HttpResponseData> HandleAnalyzeCSVFileOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "analyze-csv-file/{twinId}/{fileName}/{fileId}")] HttpRequestData req,
            string twinId,
            string fileName,
            string fileId)
        {
            _logger.LogInformation($"📊 OPTIONS preflight request for analyze-csv-file/{twinId}/{fileName}/{fileId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("AnalyzeCSVFile")]
        public async Task<HttpResponseData> AnalyzeCSVFile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analyze-csv-file/{twinId}/{fileName}/{fileId}")] HttpRequestData req,
            string twinId,
            string fileName,
            string fileId)
        {
            _logger.LogInformation("🚀 AnalyzeCSVFile function triggered for TwinId: {TwinId}, FileName: {FileName}, FileId: {FileId}", twinId, fileName, fileId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    _logger.LogError("❌ File name parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "File name parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(fileId))
                {
                    _logger.LogError("❌ File ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "File ID parameter is required"
                    }));
                    return badResponse;
                }

                if (fileId == "empty")

                {
                    fileId = "";

                }

                    // Read request body to get the question
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"📄 Request body length: {requestBody.Length} characters");

                // Parse JSON request
                var analysisRequest = JsonSerializer.Deserialize<AnalyzeCSVRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (analysisRequest == null || string.IsNullOrEmpty(analysisRequest.Question))
                {
                    _logger.LogError("❌ Question parameter is required in request body");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Question parameter is required in request body",
                        ExpectedFormat = new { question = "¿Cuál es el total de ventas del último mes?" }
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🤖 Starting CSV analysis for TwinId: {TwinId}, FileName: {FileName}, FileId: {FileId}, Question: {Question}", 
                    twinId, fileName, fileId, analysisRequest.Question);

                // Create StructuredDocumentAICoder instance
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var aiCoderLogger = loggerFactory.CreateLogger<StructuredDocumentAICoder>();
                var structuredAICoder = new StructuredDocumentAICoder(aiCoderLogger, _configuration);

                // Call the AnalyzeCSVFileUsingAzureAIAgentAsync method
                // Parameters: FileId, FileName, PathFile, ContainerName, question
                // - ContainerName = twinId (always)
                // - PathFile = "estructurado" (always for structured documents)
                var analysisResult = await structuredAICoder.AnalyzeCSVFileUsingAzureAIAgentAsync(
                    fileId,          // FileId - NEW PARAMETER
                    fileName,        // FileName
                    "estructurado",  // PathFile (fixed path for structured documents)
                    twinId,          // ContainerName (uses twinId)
                    analysisRequest.Question // question
                );

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                _logger.LogInformation("✅ CSV analysis completed successfully in {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    FileName = fileName,
                    FileId = fileId,
                    Question = analysisRequest.Question,
                    AnalysisResult = analysisResult,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    ProcessedAt = DateTime.UtcNow,
                    Message = "CSV analysis completed successfully using Azure AI Agent with Code Interpreter",
                    ContainerPath = "estructurado",
                    Note = "This analysis was performed using Azure AI Agent with Python Code Interpreter for advanced data analysis and visualizations."
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
                _logger.LogError(ex, "❌ Error analyzing CSV file after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    FileName = fileName,
                    FileId = fileId,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    ProcessedAt = DateTime.UtcNow,
                    Message = "Error durante el análisis del archivo CSV con Azure AI Agent"
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Create StructuredDocumentsCosmosDB service instance
        /// </summary>
        private StructuredDocumentsCosmosDB CreateStructuredDocumentsCosmosService()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<StructuredDocumentsCosmosDB>();
            return new StructuredDocumentsCosmosDB(cosmosLogger, _configuration);
        }

        private static string GetMimeType(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

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
    /// Request model for CSV analysis
    /// </summary>
    public class AnalyzeCSVRequest
    {
        public string Question { get; set; } = string.Empty;
    }
}
