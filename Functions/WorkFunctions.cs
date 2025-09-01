using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwinFx.Services;
using TwinFx.Agents;
using TwinFx.Models;

namespace TwinFx.Functions;

public class WorkFunctions
{
    private readonly ILogger<WorkFunctions> _logger;
    private readonly IConfiguration _configuration;

    public WorkFunctions(ILogger<WorkFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for work routes
    [Function("WorkResumeUploadOptions")]
    public async Task<HttpResponseData> HandleWorkResumeUploadOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/work/upload-resume")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for work resume upload: twins/{twinId}/work/upload-resume");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Upload and process resume document - Complete orchestrator
    /// 1. Upload resume to DataLake (work/resume path)
    /// 2. Extract data using Document Intelligence
    /// 3. Structure data using AI/LLM
    /// 4. Save to Cosmos DB TwinWork container
    /// </summary>
    [Function("UploadResume")]
    public async Task<HttpResponseData> UploadResume(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/work/upload-resume")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? UploadResume orchestrator function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            // Validate input parameters
            if (string.IsNullOrEmpty(twinId))
            {
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

            _logger.LogInformation($"?? Processing resume upload for Twin ID: {twinId}");

            // Read multipart body as bytes to preserve binary data
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();

            _logger.LogInformation($"?? Received multipart data: {bodyBytes.Length} bytes");

            // Parse multipart form data
            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            var resumePart = parts.FirstOrDefault(p => p.Name == "resume" || p.Name == "file" || p.Name == "document");

            if (resumePart == null || resumePart.Data == null || resumePart.Data.Length == 0)
            {
                return await CreateErrorResponse(req, "No resume file data found in request. Expected field name: 'resume', 'file', or 'document'", HttpStatusCode.BadRequest);
            }

            var resumeBytes = resumePart.Data;

            // Extract custom filename from FormData or use the uploaded filename
            var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
            var customFileName = fileNamePart?.StringValue;
            var fileName = customFileName ?? resumePart.FileName ?? $"resume_{DateTime.UtcNow:yyyyMMdd_HHmms}";

            // Ensure proper file extension
            if (!HasValidResumeExtension(fileName))
            {
                fileName += ".pdf"; // Default to PDF if no extension
            }

            _logger.LogInformation($"?? Resume details: Size={resumeBytes.Length} bytes, OriginalName={resumePart.FileName}, FinalName={fileName}");

            // Validate resume file format
            if (!IsValidResumeFile(fileName, resumeBytes))
            {
                return await CreateErrorResponse(req, "Invalid resume file format. Supported formats: PDF, DOC, DOCX, TXT", HttpStatusCode.BadRequest);
            }

            // STEP 1: Upload resume to DataLake
            _logger.LogInformation($"?? STEP 1: Uploading resume to DataLake...");
            
            var dataLakeClient = new DataLakeClientFactory(LoggerFactory.Create(b => b.AddConsole()), _configuration).CreateClient(twinId);
            var fileExtension = Path.GetExtension(fileName);
            var cleanFileName = Path.GetFileNameWithoutExtension(fileName) + fileExtension;
            
            // Define the work/resume path as specified
            var filePath = "work/resume";
            var fullPath = $"{filePath}/{cleanFileName}";

            _logger.LogInformation($"?? Upload path: {fullPath} (container: {twinId})");

            // Delete any existing resume files in the work/resume directory to keep only the latest
            try
            {
                var existingFiles = await dataLakeClient.ListFilesInDirectoryAsync(filePath);
                foreach (var existingFile in existingFiles)
                {
                    if (IsResumeFile(existingFile.Name))
                    {
                        try
                        {
                            var deleteSuccess = await dataLakeClient.DeleteFileAsync($"{filePath}/{existingFile.Name}");
                            if (deleteSuccess)
                            {
                                _logger.LogInformation($"??? Deleted existing resume file: {existingFile.Name}");
                            }
                            else
                            {
                                _logger.LogWarning($"?? Failed to delete existing resume file: {existingFile.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"?? Error deleting existing resume file: {existingFile.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "?? Warning: Could not clean existing resume files, but continuing with upload");
            }

            // Upload the new resume file
            using var resumeStream = new MemoryStream(resumeBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                filePath,
                cleanFileName,
                resumeStream,
                GetMimeTypeFromExtension(fileExtension)
            );

            if (!uploadSuccess)
            {
                return await CreateErrorResponse(req, "Failed to upload resume to DataLake storage", HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation($"? Resume uploaded successfully to DataLake: {fullPath}");

            // STEPS 2,3,4: Process resume using WorkAgent
            _logger.LogInformation($"?? STEPS 2,3,4: Processing resume with WorkAgent...");
            
            var workAgent = new WorkAgent(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<WorkAgent>(),
                _configuration);

            // Execute the complete WorkAgent pipeline
            var processingResult = await workAgent.ProcessResumeAsync(twinId, filePath, cleanFileName, twinId);

            if (!processingResult.Success)
            {
                _logger.LogError($"? WorkAgent processing failed: {processingResult.ErrorMessage}");
                return await CreateErrorResponse(req, 
                    $"Resume upload succeeded, but processing failed: {processingResult.ErrorMessage}", 
                    HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation($"? Complete resume processing completed successfully in {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");

            // Create success response with comprehensive results
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Resume uploaded and processed successfully",
                twinId = twinId,
                fileName = cleanFileName,
                filePath = fullPath,
                processingResult = new
                {
                    documentIntelligenceSuccess = processingResult.DocumentIntelligenceSuccess,
                    aiProcessingSuccess = processingResult.AiProcessingSuccess,
                    cosmosDbSaveSuccess = processingResult.CosmosDbSaveSuccess,
                    extractedDataSummary = processingResult.StructuredData?.Resume != null ? new
                    {
                        fullName = processingResult.StructuredData.Resume.PersonalInformation.FullName,
                        email = processingResult.StructuredData.Resume.PersonalInformation.Email,
                        totalWorkExperience = processingResult.StructuredData.Resume.WorkExperience.Count,
                        totalEducation = processingResult.StructuredData.Resume.Education.Count,
                        totalSkills = processingResult.StructuredData.Resume.Skills.Count,
                        totalCertifications = processingResult.StructuredData.Resume.Certifications.Count
                    } : null
                },
                processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                processedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in UploadResume orchestrator");
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Get all work documents for a twin with structured formatting
    /// </summary>
    [Function("GetWorkDocuments")]
    public async Task<HttpResponseData> GetWorkDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/work")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📋 GetWorkDocuments function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"📋 Getting work documents for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Get work documents by Twin ID (raw data from Cosmos DB)
            var rawWorkDocuments = await cosmosService.GetWorkDocumentsByTwinIdAsync(twinId);

            if (!rawWorkDocuments.Any())
            {
                _logger.LogInformation($"📋 No work documents found for Twin ID: {twinId}");
                
                var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(emptyResponse, req);
                await emptyResponse.WriteAsJsonAsync(new GetWorkDocumentsResponse
                {
                    Success = true,
                    Message = "No work documents found for this Twin ID",
                    TwinId = twinId,
                    TotalDocuments = 0,
                    WorkDocuments = new List<WorkDocumentData>()
                });
                return emptyResponse;
            }

            // Create DataLake client factory for generating SAS URLs
            var dataLakeFactory = new DataLakeClientFactory(
                LoggerFactory.Create(builder => builder.AddConsole()), 
                _configuration);

            // Format work documents for UI display with SAS URLs
            var formattedWorkDocuments = new List<WorkDocumentData>();

            foreach (var workDoc in rawWorkDocuments)
            {
                try
                {
                    var formattedDoc = await FormatWorkDocumentForUIAsync(workDoc, dataLakeFactory, twinId);
                    formattedWorkDocuments.Add(formattedDoc);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error formatting work document: {DocumentId}", workDoc.GetValueOrDefault("id"));
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation($"✅ Found and formatted {formattedWorkDocuments.Count} work documents for Twin ID: {twinId}");

            await response.WriteStringAsync(JsonSerializer.Serialize(new GetWorkDocumentsResponse
            {
                Success = true,
                Message = $"Found {formattedWorkDocuments.Count} work document(s) for Twin ID: {twinId}",
                TwinId = twinId,
                TotalDocuments = formattedWorkDocuments.Count,
                WorkDocuments = formattedWorkDocuments
            }, new JsonSerializerOptions { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true // Pretty JSON for UI
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting work documents");
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Get formatted resumes for UI display - beautified version ordered by date
    /// Route: GET /api/twins/{twinId}/work/resumes
    /// </summary>
    [Function("GetResumesFormatted")]
    public async Task<HttpResponseData> GetResumesFormatted(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/work/resumes")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📄 GetResumesFormatted function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"📄 Getting formatted resumes for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Get work documents by Twin ID (already ordered by createdAt DESC)
            var workDocuments = await cosmosService.GetWorkDocumentsByTwinIdAsync(twinId);

            // Filter only resume documents and format them beautifully
            var resumeDocuments = workDocuments
                .Where(doc => doc.ContainsKey("documentType") && doc["documentType"]?.ToString() == "resume")
                .ToList();

            if (!resumeDocuments.Any())
            {
                _logger.LogInformation($"📄 No resumes found for Twin ID: {twinId}");
                
                var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(emptyResponse, req);
                await emptyResponse.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "No resumes found for this Twin ID",
                    twinId = twinId,
                    totalResumes = 0,
                    resumes = new List<object>()
                });
                return emptyResponse;
            }

            // Format resumes for beautiful UI display
            var formattedResumes = new List<object>();

            // Create DataLake client factory for generating SAS URLs
            var dataLakeFactory = new DataLakeClientFactory(
                LoggerFactory.Create(builder => builder.AddConsole()), 
                _configuration);

            foreach (var resumeDoc in resumeDocuments)
            {
                try
                {
                    var formattedResume = await FormatResumeForUIAsync(resumeDoc, dataLakeFactory, twinId);
                    // 🔍 Convert to JSON string for UI to see how to handle the data
                    var formattedResumeJsonString = JsonSerializer.Serialize(formattedResume, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, 
                        WriteIndented = true 
                    });
                    formattedResumes.Add(formattedResumeJsonString);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error formatting resume document: {DocumentId}", resumeDoc.GetValueOrDefault("id"));
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation($"✅ Found and formatted {formattedResumes.Count} resumes for Twin ID: {twinId}");
            
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Found {formattedResumes.Count} resume(s) for Twin ID: {twinId}",
                twinId = twinId,
                totalResumes = formattedResumes.Count,
                resumes = formattedResumes
            }, new JsonSerializerOptions { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true // Pretty JSON for UI
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting formatted resumes");
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// OPTIONS handler for resumes route
    /// </summary>
    [Function("GetResumesFormattedOptions")]
    public async Task<HttpResponseData> HandleGetResumesFormattedOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/work/resumes")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for formatted resumes: twins/{twinId}/work/resumes");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// OPTIONS handler for work documents route
    /// </summary>
    [Function("GetWorkDocumentsOptions")]
    public async Task<HttpResponseData> HandleGetWorkDocumentsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/work")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for work documents: twins/{twinId}/work");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Format a resume document for beautiful UI display (synchronous version)
    /// </summary>
    /// <param name="resumeDoc">Raw resume document from Cosmos DB</param>
    /// <returns>Formatted resume object for UI</returns>
    private object FormatResumeForUI(Dictionary<string, object?> resumeDoc)
    {
        try
        {
            // Extract basic document information
            var documentId = resumeDoc.GetValueOrDefault("id")?.ToString() ?? "";
            var fileName = resumeDoc.GetValueOrDefault("fileName")?.ToString() ?? "";
            var createdAt = DateTime.TryParse(resumeDoc.GetValueOrDefault("createdAt")?.ToString(), out var createdDate) 
                ? createdDate : DateTime.MinValue;
            var processedAt = DateTime.TryParse(resumeDoc.GetValueOrDefault("processedAt")?.ToString(), out var processedDate) 
                ? processedDate : DateTime.MinValue;

            // Extract summary fields (already extracted during save)
            var fullName = resumeDoc.GetValueOrDefault("fullName")?.ToString() ?? "";
            var email = resumeDoc.GetValueOrDefault("email")?.ToString() ?? "";
            var phoneNumber = resumeDoc.GetValueOrDefault("phoneNumber")?.ToString() ?? "";
            var currentJobTitle = resumeDoc.GetValueOrDefault("currentJobTitle")?.ToString() ?? "";
            var currentCompany = resumeDoc.GetValueOrDefault("currentCompany")?.ToString() ?? "";
            var summary = resumeDoc.GetValueOrDefault("summary")?.ToString() ?? "";
            
            // Extract counts
            var totalWorkExperience = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalWorkExperience") ?? 0);
            var totalEducation = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalEducation") ?? 0);
            var totalSkills = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalSkills") ?? 0);
            var totalCertifications = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalCertifications") ?? 0);
            var totalProjects = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalProjects") ?? 0);
            var totalAwards = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalAwards") ?? 0);

            // Extract skills list (stored as array)
            var skillsList = new List<string>();
            if (resumeDoc.TryGetValue("skillsList", out var skillsValue))
            {
                if (skillsValue is JsonElement skillsElement && skillsElement.ValueKind == JsonValueKind.Array)
                {
                    skillsList = skillsElement.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
                else if (skillsValue is IEnumerable<object> skillsEnumerable)
                {
                    skillsList = skillsEnumerable.Select(s => s?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
            }

            // Extract detailed resume data if available
            object? detailedData = null;
            if (resumeDoc.TryGetValue("resumeData", out var resumeDataValue))
            {
                detailedData = resumeDataValue; // This will be the structured nested object
            }

            // Create beautifully formatted response
            return new
            {
                // Document metadata
                documentId = documentId,
                fileName = fileName,
                uploadedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                processedAt = processedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                daysAgo = Math.Max(0, (DateTime.UtcNow - createdAt).Days),
                
                // Personal information (quick access)
                personalInfo = new
                {
                    fullName = fullName,
                    email = email,
                    phoneNumber = phoneNumber,
                    currentPosition = !string.IsNullOrEmpty(currentJobTitle) && !string.IsNullOrEmpty(currentCompany) 
                        ? $"{currentJobTitle} at {currentCompany}" 
                        : currentJobTitle ?? currentCompany ?? "Not specified"
                },
                
                // Professional summary
                professionalSummary = !string.IsNullOrEmpty(summary) ? summary : "No summary available",
                
                // Quick stats for dashboard display
                stats = new
                {
                    workExperience = totalWorkExperience,
                    education = totalEducation,
                    skills = totalSkills,
                    certifications = totalCertifications,
                    projects = totalProjects,
                    awards = totalAwards
                },
                
                // Top skills preview (first 10 for UI)
                topSkills = skillsList.Take(10).ToList(),
                
                // Status indicators
                status = new
                {
                    isComplete = !string.IsNullOrEmpty(fullName) && !string.IsNullOrEmpty(email),
                    hasWorkExperience = totalWorkExperience > 0,
                    hasEducation = totalEducation > 0,
                    hasSkills = totalSkills > 0,
                    hasCertifications = totalCertifications > 0
                },
                
                // Full structured data (for detailed view)
                fullResumeData = detailedData
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error in FormatResumeForUI for document: {DocumentId}", resumeDoc.GetValueOrDefault("id"));
            
            // Return minimal format if formatting fails
            return new
            {
                documentId = resumeDoc.GetValueOrDefault("id")?.ToString() ?? "",
                fileName = resumeDoc.GetValueOrDefault("fileName")?.ToString() ?? "Unknown",
                error = "Error formatting resume data",
                rawData = resumeDoc // Include raw data for debugging
            };
        }
    }

    /// <summary>
    /// Asynchronous version of FormatResumeForUI to allow SAS URL generation
    /// FIXED: Now passes ALL fields from Cosmos DB without complex nested formatting
    /// </summary>
    private async Task<object> FormatResumeForUIAsync(Dictionary<string, object?> resumeDoc, DataLakeClientFactory dataLakeFactory, string twinId)
    {
        try
        {
            _logger.LogInformation("📄 Formatting resume for UI with CLEAN data conversion");

            // 🎯 Extract ALL basic fields from Cosmos DB
            var result = new Dictionary<string, object?>();
            
            // Copy ALL basic fields from resumeDoc to result (except resumeData which we'll convert)
            foreach (var kvp in resumeDoc)
            {
                if (kvp.Key != "resumeData") // Skip resumeData - we'll convert it properly
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            // 🔥 NEW: Convert complex resumeData to clean ResumeDetails class
            ResumeDetails? cleanResumeData = null;
            if (resumeDoc.TryGetValue("resumeData", out var resumeDataValue) && resumeDataValue != null)
            {
                _logger.LogInformation("📄 Converting complex resumeData to clean ResumeDetails class");
                
                cleanResumeData = ResumeDataConverter.ConvertToResumeDetails(resumeDataValue);
                
                if (cleanResumeData != null)
                {
                    _logger.LogInformation("✅ Successfully converted resumeData to clean structured class");
                    _logger.LogInformation($"📊 Clean data stats: {cleanResumeData.WorkExperience.Count} work experiences, {cleanResumeData.Education.Count} education entries, {cleanResumeData.Skills.Count} skills");
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to convert resumeData to clean class, keeping original");
                }
            }

            // Add the clean resume data to result
            if (cleanResumeData != null)
            {
                result["resumeData"] = cleanResumeData; // Clean structured class instead of complex JSON
            }

            // 🔗 Generate SAS URL for file access
            string? fileSasUrl = null;
            var fileName = resumeDoc.GetValueOrDefault("fileName")?.ToString() ?? "";
            var filePath = resumeDoc.GetValueOrDefault("filePath")?.ToString() ?? "";

            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                    var fullFilePath = $"{filePath}/{fileName}";
                    fileSasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                    
                    if (!string.IsNullOrEmpty(fileSasUrl))
                    {
                        _logger.LogInformation("🔗 Generated SAS URL for resume file access");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error generating SAS URL for resume file");
                }
            }

            // Add the SAS URL as fileUrl for UI access
            if (!string.IsNullOrEmpty(fileSasUrl))
            {
                result["fileUrl"] = fileSasUrl;
            }

            _logger.LogInformation("📄 Successfully formatted resume with CLEAN structured data for UI");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in FormatResumeForUIAsync");
            
            // Return error with original data for debugging
            return new
            {
                error = "Error formatting resume data",
                errorDetails = ex.Message,
                rawData = resumeDoc // Include original data for debugging
            };
        }
    }

    /// <summary>
    /// Get all work documents for a twin (raw data, no formatting)
    /// </summary>
    [Function("GetWorkDocumentsRaw")]
    public async Task<HttpResponseData> GetWorkDocumentsRaw(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/work/raw")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📂 GetWorkDocumentsRaw function triggered");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"📂 Retrieving raw work documents for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Get work documents by Twin ID (raw data from Cosmos DB)
            var rawWorkDocuments = await cosmosService.GetWorkDocumentsByTwinIdAsync(twinId);

            if (!rawWorkDocuments.Any())
            {
                _logger.LogInformation($"📂 No work documents found for Twin ID: {twinId}");
                
                var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(emptyResponse, req);
                await emptyResponse.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "No work documents found for this Twin ID",
                    twinId = twinId,
                    totalDocuments = 0,
                    workDocuments = new List<object>()
                });
                return emptyResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation($"✅ Found {rawWorkDocuments.Count} work document(s) for Twin ID: {twinId}");

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Found {rawWorkDocuments.Count} work document(s) for Twin ID: {twinId}",
                twinId = twinId,
                totalDocuments = rawWorkDocuments.Count,
                workDocuments = rawWorkDocuments
            }, new JsonSerializerOptions { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true // Pretty JSON for UI
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting raw work documents");
            return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Create an error response with a standardized JSON format
    /// </summary>
    /// <param name="req">The HttpRequestData object</param>
    /// <param name="errorMessage">The error message to include in the response</param>
    /// <param name="statusCode">The HTTP status code for the response</param>
    /// <returns>A task that represents the asynchronous operation, with a HttpResponseData result containing the error response</returns>
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

    /// <summary>
    /// Add CORS headers to the response based on the request
    /// Allows flexible development with multiple localhost origins
    /// </summary>
    /// <param name="response">The HttpResponseData object to modify</param>
    /// <param name="request">The HttpRequestData object for retrieving origin information</param>
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
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            ".rtf" => "application/rtf",
            _ => "application/octet-stream"
        };
    }

    // Helper methods for multipart parsing
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
            var dispositionMatch = Regex.Match(headers, @"Content-Disposition:\s*form-data;\s*name=""([^""]+)""(?:;\s*filename=""([^""]+"")?", RegexOptions.IgnoreCase);
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
                (!string.IsNullOrEmpty(part.ContentType) && (part.ContentType.StartsWith("application/") || part.ContentType.StartsWith("text/"))))
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

    private static bool HasValidResumeExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var validExtensions = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf" };
        return validExtensions.Contains(extension);
    }

    private static bool IsValidResumeFile(string fileName, byte[] fileData)
    {
        if (string.IsNullOrEmpty(fileName) || fileData == null || fileData.Length == 0)
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var validExtensions = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf" };

        if (!validExtensions.Contains(extension))
            return false;

        // Check file signature (magic numbers) for common formats
        if (fileData.Length < 4)
            return false;

        // PDF: %PDF
        if (fileData.Length >= 4 && 
            fileData[0] == 0x25 && fileData[1] == 0x50 && fileData[2] == 0x44 && fileData[3] == 0x46)
            return true;

        // DOC: D0CF11E0A1B11AE1
        if (fileData.Length >= 8 &&
            fileData[0] == 0xD0 && fileData[1] == 0xCF && fileData[2] == 0x11 && fileData[3] == 0xE0 &&
            fileData[4] == 0xA1 && fileData[5] == 0xB1 && fileData[6] == 0x1A && fileData[7] == 0xE1)
            return true;

        // DOCX: PK (ZIP format)
        if (fileData.Length >= 2 && fileData[0] == 0x50 && fileData[1] == 0x4B)
            return true;

        // For TXT and RTF, allow any content (they don't have specific signatures)
        if (extension == ".txt" || extension == ".rtf")
            return true;

        return false; // Unknown format
    }

    private static bool IsResumeFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var resumeExtensions = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf" };
        return resumeExtensions.Contains(extension);
    }

    private string GetBoundary(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return string.Empty;

        var boundaryMatch = Regex.Match(contentType, @"boundary=([^;]+)", RegexOptions.IgnoreCase);
        return boundaryMatch.Success ? boundaryMatch.Groups[1].Value.Trim('"', ' ') : string.Empty;
    }

    /// <summary>
    /// Format a work document for UI display with SAS URL generation
    /// </summary>
    /// <param name="workDoc">Raw work document from Cosmos DB</param>
    /// <param name="dataLakeFactory">DataLake client factory for generating SAS URLs</param>
    /// <param name="twinId">Twin ID for identifying the correct container</param>
    /// <returns>Formatted work document object for UI</returns>
    private async Task<WorkDocumentData> FormatWorkDocumentForUIAsync(Dictionary<string, object?> workDoc, DataLakeClientFactory dataLakeFactory, string twinId)
    {
        try
        {
            // Extract basic document information
            var documentId = workDoc.GetValueOrDefault("id")?.ToString() ?? "";
            var fileName = workDoc.GetValueOrDefault("fileName")?.ToString() ?? "";
            var filePath = workDoc.GetValueOrDefault("filePath")?.ToString() ?? "";
            var createdAt = DateTime.TryParse(workDoc.GetValueOrDefault("createdAt")?.ToString(), out var createdDate) 
                ? createdDate : DateTime.MinValue;
            var processedAt = DateTime.TryParse(workDoc.GetValueOrDefault("processedAt")?.ToString(), out var processedDate) 
                ? processedDate : DateTime.MinValue;

            // Extract summary fields (already extracted during save)
            var fullName = workDoc.GetValueOrDefault("fullName")?.ToString() ?? "";
            var email = workDoc.GetValueOrDefault("email")?.ToString() ?? "";
            var phoneNumber = workDoc.GetValueOrDefault("phoneNumber")?.ToString() ?? "";
            var currentJobTitle = workDoc.GetValueOrDefault("currentJobTitle")?.ToString() ?? "";
            var currentCompany = workDoc.GetValueOrDefault("currentCompany")?.ToString() ?? "";
            var summary = workDoc.GetValueOrDefault("summary")?.ToString() ?? "";
            var executiveSummary = workDoc.GetValueOrDefault("executiveSummary")?.ToString() ?? "";
            
            // Extract counts
            var totalWorkExperience = Convert.ToInt32(workDoc.GetValueOrDefault("totalWorkExperience") ?? 0);
            var totalEducation = Convert.ToInt32(workDoc.GetValueOrDefault("totalEducation") ?? 0);
            var totalSkills = Convert.ToInt32(workDoc.GetValueOrDefault("totalSkills") ?? 0);
            var totalCertifications = Convert.ToInt32(workDoc.GetValueOrDefault("totalCertifications") ?? 0);
            var totalProjects = Convert.ToInt32(workDoc.GetValueOrDefault("totalProjects") ?? 0);
            var totalAwards = Convert.ToInt32(workDoc.GetValueOrDefault("totalAwards") ?? 0);

            // Extract skills list (stored as array)
            var skillsList = new List<string>();
            if (workDoc.TryGetValue("skillsList", out var skillsValue))
            {
                if (skillsValue is JsonElement skillsElement && skillsElement.ValueKind == JsonValueKind.Array)
                {
                    skillsList = skillsElement.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
                else if (skillsValue is IEnumerable<object> skillsEnumerable)
                {
                    skillsList = skillsEnumerable.Select(s => s?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
            }

            // Generate SAS URL for the document file
            string? fileSasUrl = null;
            try
            {
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var fullFilePath = $"{filePath}/{fileName}";
                fileSasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                
                if (!string.IsNullOrEmpty(fileSasUrl))
                {
                    _logger.LogInformation("🔗 Generated SAS URL for document: {DocumentId} ({FileName})", documentId, fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error generating SAS URL for document: {DocumentId} ({FileName})", documentId, fileName);
            }

            // Create formatted response
            return new WorkDocumentData
            {
                // Document metadata
                DocumentId = documentId,
                FileName = fileName,
                UploadedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                ProcessedAt = processedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                DaysAgo = Math.Max(0, (DateTime.UtcNow - createdDate).Days),
                
                // Quick access data (for resumes)
                FullName = fullName,
                Email = email,
                CurrentJobTitle = currentJobTitle,
                CurrentCompany = currentCompany,
                Summary = summary,
                ExecutiveSummary = executiveSummary,
                
                // Statistics
                Stats = new WorkDocumentStats
                {
                    WorkExperience = totalWorkExperience,
                    Education = totalEducation,
                    Skills = totalSkills,
                    Certifications = totalCertifications,
                    Projects = totalProjects,
                    Awards = totalAwards
                },
                
                // File access
                FileUrl = fileSasUrl,
                
                // Complete data (if available)
                FullDocumentData = workDoc
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error formatting work document: {DocumentId}", workDoc.GetValueOrDefault("id"));
            
            // Return error details for debugging
            return new WorkDocumentData
            {
                DocumentId = workDoc.GetValueOrDefault("id")?.ToString() ?? "",
                FileName = workDoc.GetValueOrDefault("fileName")?.ToString() ?? "Unknown",
                FileUrl = null,
                FullDocumentData = workDoc // Include raw data for debugging
            };
        }
    }

    public class MultipartFormDataPart
    {
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public byte[]? Data { get; set; }
        public string StringValue { get; set; } = string.Empty;
    }
}

// ========================================================================================
// RESPONSE MODELS FOR API DOCUMENTATION AND TYPE SAFETY
// Added for UI developer clarity - describes the exact structure returned by GetResumesFormatted
// ========================================================================================

/// <summary>
/// Response model for GET /api/twins/{twinId}/work/resumes endpoint
/// Contains formatted resume data optimized for UI display
/// </summary>
public class GetResumesFormattedResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Twin ID used in the request
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of resumes found for this Twin
    /// </summary>
    public int TotalResumes { get; set; }
    
    /// <summary>
    /// Array of formatted resume data objects, ordered by date (newest first)
    /// </summary>
    public List<FormattedResumeData> Resumes { get; set; } = new();
}

/// <summary>
/// Formatted resume data optimized for UI display
/// Contains both summary information for quick display and complete data for detailed views
/// </summary>
public class FormattedResumeData
{
    // ==================== DOCUMENT METADATA ====================
    
    /// <summary>
    /// Unique document identifier in Cosmos DB
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Original uploaded filename
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Upload timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string UploadedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing completion timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string ProcessedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of days since upload (useful for "X days ago" display)
    /// </summary>
    public int DaysAgo { get; set; }
    
    // ==================== PERSONAL INFORMATION (QUICK ACCESS) ====================
    
    /// <summary>
    /// Personal information extracted from resume for quick display
    /// </summary>
    public PersonalInfoSummary PersonalInfo { get; set; } = new();
    
    // ==================== PROFESSIONAL SUMMARY ====================
    
    /// <summary>
    /// Professional summary/objective from resume, or "No summary available" if empty
    /// </summary>
    public string ProfessionalSummary { get; set; } = string.Empty;
    
    // ==================== EXECUTIVE SUMMARY ====================
    
    /// <summary>
    /// AI-generated executive summary of the resume in HTML format
    /// Provides a comprehensive overview with detailed analysis for UI display
    /// </summary>
    public string ExecutiveSummary { get; set; } = string.Empty;
    
    // ==================== DASHBOARD STATISTICS ====================
    
    /// <summary>
    /// Numeric statistics for dashboard display (counts of various resume sections)
    /// </summary>
    public ResumeStats Stats { get; set; } = new();
    
    // ==================== SKILLS PREVIEW ====================
    
    /// <summary>
    /// First 10 skills from resume for preview display (full list in FullResumeData)
    /// </summary>
    public List<string> TopSkills { get; set; } = new();
    
    // ==================== STATUS INDICATORS ====================
    
    /// <summary>
    /// Boolean indicators for UI badges, icons, and status displays
    /// </summary>
    public ResumeStatus Status { get; set; } = new();
    
    // ==================== COMPLETE STRUCTURED DATA ====================
    
    /// <summary>
    /// Complete structured resume data for detailed view
    /// Contains all extracted information in nested object format
    /// </summary>
    public object? FullResumeData { get; set; }

    // ==================== FILE ACCESS ====================
    
    /// <summary>
    /// SAS URL for direct access to the resume file
    /// Valid for 24 hours, allows UI to download/view the original resume document
    /// </summary>
    public string? FileUrl { get; set; }
}

/// <summary>
/// Personal information summary for quick display in UI cards/lists
/// </summary>
public class PersonalInfoSummary
{
    /// <summary>
    /// Full name extracted from resume
    /// </summary>
    public string FullName { get; set; } = string.Empty;
    
    /// <summary>
    /// Email address extracted from resume
    /// </summary>
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Phone number extracted from resume
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Current position formatted as "Job Title at Company" or individual components if only one available
    /// Returns "Not specified" if neither job title nor company are available
    /// </summary>
    public string CurrentPosition { get; set; } = string.Empty;
}

/// <summary>
/// Resume statistics for dashboard display
/// All counts represent the number of items in each resume section
/// </summary>
public class ResumeStats
{
    /// <summary>
    /// Number of work experience entries in resume
    /// </summary>
    public int WorkExperience { get; set; }
    
    /// <summary>
    /// Number of education entries in resume
    /// </summary>
    public int Education { get; set; }
    
    /// <summary>
    /// Number of skills listed in resume
    /// </summary>
    public int Skills { get; set; }
    
    /// <summary>
    /// Number of certifications in resume
    /// </summary>
    public int Certifications { get; set; }
    
    /// <summary>
    /// Number of projects listed in resume
    /// </summary>
    public int Projects { get; set; }
    
    /// <summary>
    /// Number of awards/achievements in resume
    /// </summary>
    public int Awards { get; set; }
}

/// <summary>
/// Resume status indicators for UI badges, icons, and conditional displays
/// </summary>
public class ResumeStatus
{
    /// <summary>
    /// True if resume has both full name and email (considered complete basic info)
    /// </summary>
    public bool IsComplete { get; set; }
    
    /// <summary>
    /// True if resume contains work experience entries
    /// </summary>
    public bool HasWorkExperience { get; set; }
    
    /// <summary>
    /// True if resume contains education entries
    /// </summary>
    public bool HasEducation { get; set; }
    
    /// <summary>
    /// True if resume contains skills
    /// </summary>
    public bool HasSkills { get; set; }
    
    /// <summary>
    /// True if resume contains certifications
    /// </summary>
    public bool HasCertifications { get; set; }
}

// ========================================================================================
// RESPONSE MODELS FOR WORK DOCUMENTS
// ========================================================================================

/// <summary>
/// Response model for GET /api/twins/{twinId}/work endpoint
/// Contains structured work documents data optimized for UI display
/// </summary>
public class GetWorkDocumentsResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Twin ID used in the request
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of work documents found for this Twin
    /// </summary>
    public int TotalDocuments { get; set; }
    
    /// <summary>
    /// Array of structured work document data objects, ordered by date (newest first)
    /// </summary>
    public List<WorkDocumentData> WorkDocuments { get; set; } = new();
}

/// <summary>
/// Structured work document data optimized for UI display
/// </summary>
public class WorkDocumentData
{
    // ==================== DOCUMENT METADATA ====================
    
    /// <summary>
    /// Unique document identifier in Cosmos DB
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of work document (resume, cover_letter, etc.)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Original uploaded filename
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Upload timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string UploadedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing completion timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string ProcessedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of days since upload (useful for "X days ago" display)
    /// </summary>
    public int DaysAgo { get; set; }
    
    // ==================== QUICK ACCESS DATA ====================
    
    /// <summary>
    /// Full name extracted from document (for resumes)
    /// </summary>
    public string? FullName { get; set; }
    
    /// <summary>
    /// Email extracted from document (for resumes)
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Current job title (for resumes)
    /// </summary>
    public string? CurrentJobTitle { get; set; }
    
    /// <summary>
    /// Current company (for resumes)
    /// </summary>
    public string? CurrentCompany { get; set; }
    
    /// <summary>
    /// Professional summary/objective from document
    /// </summary>
    public string? Summary { get; set; }
    
    /// <summary>
    /// Executive summary (HTML format) for resumes
    /// </summary>
    public string? ExecutiveSummary { get; set; }
    
    // ==================== STATISTICS ====================
    
    /// <summary>
    /// Document processing statistics
    /// </summary>
    public WorkDocumentStats Stats { get; set; } = new();
    
    // ==================== FILE ACCESS ====================
    
    /// <summary>
    /// SAS URL for direct access to the document file
    /// Valid for 24 hours, allows UI to download/view the original document
    /// </summary>
    public string? FileUrl { get; set; }
    
    // ==================== COMPLETE DATA ====================
    
    /// <summary>
    /// Complete structured document data for detailed view
    /// Contains all extracted information in nested object format
    /// </summary>
    public object? FullDocumentData { get; set; }
}

/// <summary>
/// Work document statistics for dashboard display
/// </summary>
public class WorkDocumentStats
{
    /// <summary>
    /// Number of work experience entries (for resumes)
    /// </summary>
    public int WorkExperience { get; set; }
    
    /// <summary>
    /// Number of education entries (for resumes)
    /// </summary>
    public int Education { get; set; }
    
    /// <summary>
    /// Number of skills listed (for resumes)
    /// </summary>
    public int Skills { get; set; }
    
    /// <summary>
    /// Number of certifications (for resumes)
    /// </summary>
    public int Certifications { get; set; }
    
    /// <summary>
    /// Number of projects (for resumes)
    /// </summary>
    public int Projects { get; set; }
    
    /// <summary>
    /// Number of awards/achievements (for resumes)
    /// </summary>
    public int Awards { get; set; }
}