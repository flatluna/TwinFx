using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwinFx.Agents;
using TwinFx.Models;
using TwinFx.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;
using JsonSerializer = System.Text.Json.JsonSerializer;

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
        string twinId
    )
    {
        _logger.LogInformation(
            $"?? OPTIONS preflight request for work resume upload: twins/{twinId}/work/upload-resume"
        );

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
        string twinId
    )
    {
        _logger.LogInformation("?? UploadResume orchestrator function triggered");
        var startTime = DateTime.UtcNow;

        try {
            // Validate input parameters
            if (string.IsNullOrEmpty(twinId)) {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            // Validate Content-Type for multipart/form-data
            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (contentTypeHeader.Key == null || !contentTypeHeader.Value.Any(v => v.Contains("multipart/form-data"))) {
                return await CreateErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeHeader.Value.FirstOrDefault() ?? "";
            var boundary = GetBoundary(contentType);

            if (string.IsNullOrEmpty(boundary)) {
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

            if (resumePart == null || resumePart.Data == null || resumePart.Data.Length == 0) {
                return await CreateErrorResponse(req, "No resume file data found in request. Expected field name: 'resume', 'file', or 'document'", HttpStatusCode.BadRequest);
            }

            var resumeBytes = resumePart.Data;

            // Extract custom filename from FormData or use the uploaded filename
            var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
            var customFileName = fileNamePart?.StringValue;
            var fileName = customFileName ?? resumePart.FileName ?? $"resume_{DateTime.UtcNow:yyyyMMdd_HHmms}";

            // Ensure proper file extension
            if (!HasValidResumeExtension(fileName)) {
                fileName += ".pdf"; // Default to PDF if no extension
            }

            _logger.LogInformation($"?? Resume details: Size={resumeBytes.Length} bytes, OriginalName={resumePart.FileName}, FinalName={fileName}");

            // Validate resume file format
            if (!IsValidResumeFile(fileName, resumeBytes)) {
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
            try {
                var existingFiles = await dataLakeClient.ListFilesInDirectoryAsync(filePath);
                foreach (var existingFile in existingFiles) {
                    if (IsResumeFile(existingFile.Name)) {
                        try {
                            var deleteSuccess = await dataLakeClient.DeleteFileAsync($"{filePath}/{existingFile.Name}");
                            if (deleteSuccess) {
                                _logger.LogInformation($"??? Deleted existing resume file: {existingFile.Name}");
                            }
                            else
                            {
                                _logger.LogWarning($"?? Failed to delete existing resume file: {existingFile.Name}");
                            }
                        }
                        catch (Exception ex) {
                            _logger.LogWarning(ex, $"?? Error deleting existing resume file: {existingFile.Name}");
                        }
                    }
                }
            }
            catch (Exception ex) {
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

            if (!uploadSuccess) {
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

            if (!processingResult.Success) {
                _logger.LogError($"? WorkAgent processing failed: {processingResult.ErrorMessage}");
                return await CreateErrorResponse(req, 
                    $"Resume upload succeeded, but processing failed: {processingResult.ErrorMessage}", 
                    HttpStatusCode.InternalServerError);
            }

            _logger.LogInformation($"? Complete resume processing completed successfully in {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");

            // Create success response with comprehensive results
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync(JsonSerializer.Serialize(new
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
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
 
             return response;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "? Error in UploadResume orchestrator");
            return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary> 

    /// <summary>
    /// Get formatted resumes for UI display - beautified version ordered by date
    /// Route: GET /api/twins/{twinId}/work/resumes
    /// </summary>
    [Function("GetResumesFormatted")]
    public async Task<HttpResponseData> GetResumesFormatted(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/work/resumes")] HttpRequestData req,
        string twinId
    )
    {
        _logger.LogInformation("📄 GetResumesFormatted function triggered");

        try {
            if (string.IsNullOrEmpty(twinId)) {
                return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
            }

            _logger.LogInformation($"📄 Getting formatted resumes for Twin ID: {twinId}");

            // Create Cosmos DB service
            var cosmosService = new CosmosDbTwinProfileService(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
                _configuration);

            // Get work documents by Twin ID (already ordered by createdAt DESC)
            var workDocuments = await cosmosService.GetWorkDocumentsByTwinIdAsync(twinId);

            // Ensure SAS URLs are available for each document: try DB first, then generate from DataLake if missing
            try
            {
                var dataLakeFactory = new DataLakeClientFactory(LoggerFactory.Create(builder => builder.AddConsole()), _configuration);

                foreach (var doc in workDocuments)
                {
                    try
                    {
                        // If SAS already present, skip



                        try
                        {
                            var dataLakeClient = dataLakeFactory.CreateClient(doc.ContainerName);
                            var full = $"{doc.FilePath}/{doc.FileName}";
                            var generated = await dataLakeClient.GenerateSasUrlAsync(full, TimeSpan.FromHours(24));
                            if (!string.IsNullOrEmpty(generated))
                            {
                                doc.SasUrl = generated;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Could not generate SAS URL for work document {Id}", doc.Id);
                        }

                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogWarning(innerEx, "⚠️ Error while ensuring SAS URL for work document {Id}", doc?.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error creating DataLakeClientFactory to generate SAS URLs");
            }

            // Convert to JSON string  
            string jsonString = System.Text.Json.JsonSerializer.Serialize(workDocuments, new JsonSerializerOptions { WriteIndented = true });
            // Deserialize JSON to ResumeStructuredData  
              

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json"); 
            
            // FIXED: Use WriteAsJsonAsync directly instead of manual serialization
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Found {workDocuments.Count} resume(s) for Twin ID: {twinId}",
                twinId = twinId,
                totalResumes = workDocuments.Count,
                resumes = workDocuments
            }, new System.Text.Json.JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true // Pretty JSON for UI
            }));

            return response;
        }
        catch (Exception ex) {
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
        string twinId
    )
    {
        _logger.LogInformation(
            $"?? OPTIONS preflight request for formatted resumes: twins/{twinId}/work/resumes"
        );

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
        string twinId
    )
    {
        _logger.LogInformation(
            $"?? OPTIONS preflight request for work documents: twins/{twinId}/work"
        );

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
        try {
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
            if (resumeDoc.TryGetValue("skillsList", out var skillsValue)) {
                if (skillsValue is JsonElement skillsElement && skillsElement.ValueKind == JsonValueKind.Array) {
                    skillsList = skillsElement.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
                else if (skillsValue is IEnumerable<object> skillsEnumerable) {
                    skillsList = skillsEnumerable.Select(s => s?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
            }

            // Extract detailed resume data if available
            object? detailedData = null;
            if (resumeDoc.TryGetValue("resumeData", out var resumeDataValue)) {
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
        catch (Exception ex) {
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
    /// Try to parse resumeData into ResumeStructuredData using System.Text.Json
    /// Handles multiple storage formats (JsonElement, Dictionary, or JSON string)
    /// </summary>
    private ResumeStructuredData? ParseStructuredResumeFromDoc(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (!resumeDoc.TryGetValue("resumeData", out var resumeDataValue) || resumeDataValue == null)
                return null;

            string? json = null;

            if (resumeDataValue is JsonElement element) {
                // resumeData might already be a JsonElement
                json = element.GetRawText();
            }
            else if (resumeDataValue is string s) {
                json = s;
            }
            else {
                // Try to serialize the object (Dictionary<string, object?>) to JSON
                try {
                    json = JsonSerializer.Serialize(resumeDataValue, new JsonSerializerOptions { WriteIndented = false });
                }
                catch {
                    // ignore
                }
            }

            if (string.IsNullOrEmpty(json))
                return null;

            // If the stored structure wraps resume under "resumeData": { "resume": { ... } }
            // or directly under object, ensure we pass the inner object to the type
            // Try direct deserialize to ResumeStructuredData
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
            try {
                var structured = JsonSerializer.Deserialize<ResumeStructuredData>(json, options);
                if (structured != null && structured.Resume != null)
                    return structured;
            }
            catch { }

            // Some documents store resumeData as an object with a "resume" property
            try {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("resume", out var resumeElem)) {
                    var resumeJson = resumeElem.GetRawText();
                    var structured2 = JsonSerializer.Deserialize<ResumeStructuredData>("{\"resume\": " + resumeJson + "}", options);
                    if (structured2 != null && structured2.Resume != null)
                        return structured2;

                    // Try deserialize the inner object directly into ResumeData
                    var inner = JsonSerializer.Deserialize<ResumeData>(resumeJson, options);
                    if (inner != null)
                        return new ResumeStructuredData { Resume = inner };
                }
            }
            catch { }

            return null;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Failed to parse structured resume data");
            return null;
        }
    }

    private ResumeMainClass? ParseResumeMainClassFromDoc(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (!resumeDoc.TryGetValue("resumeData", out var resumeDataValue) || resumeDataValue == null)
                return null;

            // Convert various possible types to JSON string
            string json = string.Empty;

            if (resumeDataValue is JsonElement je) {
                json = je.GetRawText();
            }
            else if (resumeDataValue is string s) {
                json = s;
            }
            else {
                // try serialize object (Dictionary)
                json = JsonSerializer.Serialize(resumeDataValue);
            }

            if (string.IsNullOrEmpty(json))
                return null;

            // Use Newtonsoft to deserialize robustly, handle nested tokens
            var resumeMain = Newtonsoft.Json.JsonConvert.DeserializeObject<ResumeMainClass>(json);
            return resumeMain;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Failed to parse ResumeMainClass from resumeData");
            return null;
        }
    }

    /// <summary>
    /// Asynchronous version of FormatResumeForUI to allow SAS URL generation
    /// FIXED: Returns clean, flat data structure for easy UI consumption with structured arrays
    /// </summary>
    private async Task<FormattedResumeData> FormatResumeForUIAsync(Dictionary<string, object?> resumeDoc, DataLakeClientFactory dataLakeFactory, string twinId)
     {
         try {
             _logger.LogInformation("📄 Formatting resume for UI with CLEAN flat data structure and structured arrays");

             // Try to parse structured resume into typed model first (most reliable)
             var resumeMain = ParseResumeMainClassFromDoc(resumeDoc);

             // If parsing failed, try one-line conversion of the whole document using Newtonsoft
             if (resumeMain == null)
             {
                 try
                 {
                     var json = Newtonsoft.Json.JsonConvert.SerializeObject(resumeDoc);
                     var attempt = Newtonsoft.Json.JsonConvert.DeserializeObject<ResumeMainClass>(json);
                     if (attempt?.Resume != null)
                     {
                         resumeMain = attempt;
                     }
                 }
                 catch (Exception ex)
                 {
                     _logger.LogDebug(ex, "Debug: Newtonsoft one-line conversion of resumeDoc failed, will fallback to manual extraction");
                 }
             }

             List<object> workExperience;
             List<object> education;
             List<object> certifications;
             List<object> awards;

             if (resumeMain?.Resume != null) {
                 workExperience = resumeMain.Resume.WorkExperience.Select(w => new
                 {
                     jobTitle = w.JobTitle ?? string.Empty,
                     company = w.Company ?? string.Empty,
                     duration = w.Duration ?? string.Empty,
                     responsibilities = w.Responsibilities ?? new List<string>()
                 } as object).ToList();

                 education = resumeMain.Resume.Education.Select(e => new
                 {
                     degree = e.Degree ?? string.Empty,
                     institution = e.Institution ?? string.Empty,
                     graduationYear = e.GraduationYear,
                     location = e.Location ?? string.Empty
                 } as object).ToList();

                 certifications = resumeMain.Resume.Certifications.Select(c => new
                 {
                     title = c.Title ?? string.Empty,
                     issuingOrganization = c.IssuingOrganization ?? string.Empty,
                     dateIssued = c.DateIssued ?? string.Empty
                 } as object).ToList();

                 awards = resumeMain.Resume.Awards.Select(a => new
                 {
                     title = a.Title ?? string.Empty,
                     organization = a.Organization ?? string.Empty,
                     year = a.Year
                 } as object).ToList();
             }
             else {
                 // Fallback to previous extraction (handles Dictionary or JsonElement)
                 workExperience = ExtractWorkExperienceArray(resumeDoc);
                 education = ExtractEducationArray(resumeDoc);
                 certifications = ExtractCertificationsArray(resumeDoc);
                 awards = ExtractAwardsArray(resumeDoc);
             }

            // 🎯 Extract ALL basic fields from Cosmos DB in a clean, flat structure and map to FormattedResumeData
            var documentId = resumeDoc.GetValueOrDefault("id")?.ToString() ?? "";
            var fileName = resumeDoc.GetValueOrDefault("fileName")?.ToString() ?? "";
            var createdAt = DateTime.TryParse(resumeDoc.GetValueOrDefault("createdAt")?.ToString(), out var createdDate) ? createdDate : DateTime.MinValue;
            var processedAt = DateTime.TryParse(resumeDoc.GetValueOrDefault("processedAt")?.ToString(), out var processedDate) ? processedDate : DateTime.MinValue;

            var fullName = resumeDoc.GetValueOrDefault("fullName")?.ToString() ?? "";
            var email = resumeDoc.GetValueOrDefault("email")?.ToString() ?? "";
            var phoneNumber = resumeDoc.GetValueOrDefault("phoneNumber")?.ToString() ?? "";
            var currentJobTitle = resumeDoc.GetValueOrDefault("currentJobTitle")?.ToString() ?? "";
            var currentCompany = resumeDoc.GetValueOrDefault("currentCompany")?.ToString() ?? "";
            var summary = resumeDoc.GetValueOrDefault("summary")?.ToString() ?? "";

            var totalWorkExperience = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalWorkExperience") ?? 0);
            var totalEducation = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalEducation") ?? 0);
            var totalSkills = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalSkills") ?? 0);
            var totalCertifications = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalCertifications") ?? 0);
            var totalProjects = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalProjects") ?? 0);
            var totalAwards = Convert.ToInt32(resumeDoc.GetValueOrDefault("totalAwards") ?? 0);

            // Generate SAS URL if possible
            string? fileSas = null;
            try
            {
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var filePath = resumeDoc.GetValueOrDefault("filePath")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(fileName))
                {
                    var full = $"{filePath}/{fileName}";
                    fileSas = await dataLakeClient.GenerateSasUrlAsync(full, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error generating SAS URL for resume {DocId}", documentId);
            }

            var formatted = new FormattedResumeData
             {
                DocumentId = documentId,
                FileName = fileName,
                UploadedAt = createdAt == DateTime.MinValue ? "" : createdAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                ProcessedAt = processedAt == DateTime.MinValue ? "" : processedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                DaysAgo = Math.Max(0, (DateTime.UtcNow - createdDate).Days),
                PersonalInfo = new PersonalInfoSummary
                {
                    FullName = fullName,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    CurrentPosition = !string.IsNullOrEmpty(currentJobTitle) && !string.IsNullOrEmpty(currentCompany) ? $"{currentJobTitle} at {currentCompany}" : (currentJobTitle ?? currentCompany ?? "Not specified")
                },
                ProfessionalSummary = !string.IsNullOrEmpty(summary) ? summary : "No summary available",
                ExecutiveSummary = resumeDoc.GetValueOrDefault("executiveSummary")?.ToString() ?? "",
                Stats = new ResumeStats
                {
                    WorkExperience = totalWorkExperience,
                    Education = totalEducation,
                    Skills = totalSkills,
                    Certifications = totalCertifications,
                    Projects = totalProjects,
                    Awards = totalAwards
                },
                TopSkills = ExtractSkillsList(resumeDoc).Take(10).ToList(),
                Status = new ResumeStatus
                {
                    IsComplete = !string.IsNullOrEmpty(fullName) && !string.IsNullOrEmpty(email),
                    HasWorkExperience = totalWorkExperience > 0,
                    HasEducation = totalEducation > 0,
                    HasSkills = totalSkills > 0,
                    HasCertifications = totalCertifications > 0
                },
                FullResumeData = ExtractExperienceDetails(resumeDoc),
                FileUrl = fileSas
             };

            return formatted;
         }
         catch (Exception ex) {
             _logger.LogWarning(ex, "⚠️ Error in FormatResumeForUIAsync for document: {DocumentId}", resumeDoc.GetValueOrDefault("id"));
             return new FormattedResumeData { DocumentId = resumeDoc.GetValueOrDefault("id")?.ToString() ?? "", FileName = resumeDoc.GetValueOrDefault("fileName")?.ToString() ?? "", FullResumeData = resumeDoc };
         }
     }

    /// <summary>
    /// Extract work experience array from resume document (handles multiple formats)
    /// </summary>
    private List<object> ExtractWorkExperienceArray(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (resumeDoc.TryGetValue("workExperience", out var weValue)) {
                if (weValue is JsonElement weElement && weElement.ValueKind == JsonValueKind.Array) {
                    // JsonElement array
                    return weElement.EnumerateArray().Select(we => new
                    {
                        jobTitle = we.GetProperty("jobTitle").GetString() ?? "",
                        company = we.GetProperty("company").GetString() ?? "",
                        duration = we.GetProperty("duration").GetString() ?? "",
                        responsibilities = we.GetProperty("responsibilities").EnumerateArray().Select(r => r.GetString() ?? "").ToList()
                    } as object).ToList();
                }
                else if (weValue is IEnumerable<object> weEnumerable) {
                    // Enumerable<object> (e.g., List<object>)
                    return weEnumerable.Select(we => new
                    {
                        jobTitle = (we as IDictionary<string, object?>)["jobTitle"]?.ToString() ?? "",
                        company = (we as IDictionary<string, object?>)["company"]?.ToString() ?? "",
                        duration = (we as IDictionary<string, object?>)["duration"]?.ToString() ?? "",
                        responsibilities = ((we as IDictionary<string, object?>)["responsibilities"] as IEnumerable<object>)?.Select(r => r?.ToString() ?? "").ToList() ?? new List<string>()
                    } as object).ToList();
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Error extracting work experience array");
        }

        return new List<object>();
    }

    /// <summary>
    /// Extract education array from resume document (handles multiple formats)
    /// </summary>
    private List<object> ExtractEducationArray(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (resumeDoc.TryGetValue("education", out var eduValue)) {
                if (eduValue is JsonElement eduElement && eduElement.ValueKind == JsonValueKind.Array) {
                    // JsonElement array
                    return eduElement.EnumerateArray().Select(ed => new
                    {
                        degree = ed.GetProperty("degree").GetString() ?? "",
                        institution = ed.GetProperty("institution").GetString() ?? "",
                        graduationYear = ed.GetProperty("graduationYear").GetInt32(),
                        location = ed.GetProperty("location").GetString() ?? ""
                    } as object).ToList();
                }
                else if (eduValue is IEnumerable<object> eduEnumerable) {
                    // Enumerable<object> (e.g., List<object>)
                    return eduEnumerable.Select(ed => new
                    {
                        degree = (ed as IDictionary<string, object?>)["degree"]?.ToString() ?? "",
                        institution = (ed as IDictionary<string, object?>)["institution"]?.ToString() ?? "",
                        graduationYear = Convert.ToInt32((ed as IDictionary<string, object?>)["graduationYear"] ?? 0),
                        location = (ed as IDictionary<string, object?>)["location"]?.ToString() ?? ""
                    } as object).ToList();
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Error extracting education array");
        }

        return new List<object>();
    }

    /// <summary>
    /// Extract certifications array from resume document (handles multiple formats)
    /// </summary>
    private List<object> ExtractCertificationsArray(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (resumeDoc.TryGetValue("certifications", out var certValue)) {
                if (certValue is JsonElement certElement && certElement.ValueKind == JsonValueKind.Array) {
                    // JsonElement array
                    return certElement.EnumerateArray().Select(c => new
                    {
                        title = c.GetProperty("title").GetString() ?? "",
                        issuingOrganization = c.GetProperty("issuingOrganization").GetString() ?? "",
                        dateIssued = c.GetProperty("dateIssued").GetString() ?? ""
                    } as object).ToList();
                }
                else if (certValue is IEnumerable<object> certEnumerable) {
                    // Enumerable<object> (e.g., List<object>)
                    return certEnumerable.Select(c => new
                    {
                        title = (c as IDictionary<string, object?>)["title"]?.ToString() ?? "",
                        issuingOrganization = (c as IDictionary<string, object?>)["issuingOrganization"]?.ToString() ?? "",
                        dateIssued = (c as IDictionary<string, object?>)["dateIssued"]?.ToString() ?? ""
                    } as object).ToList();
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Error extracting certifications array");
        }

        return new List<object>();
    }

    /// <summary>
    /// Extract awards array from resume document (handles multiple formats)
    /// </summary>
    private List<object> ExtractAwardsArray(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (resumeDoc.TryGetValue("awards", out var awardsValue)) {
                if (awardsValue is JsonElement awardsElement && awardsElement.ValueKind == JsonValueKind.Array) {
                    // JsonElement array
                    return awardsElement.EnumerateArray().Select(a => new
                    {
                        title = a.GetProperty("title").GetString() ?? "",
                        organization = a.GetProperty("organization").GetString() ?? "",
                        year = a.GetProperty("year").GetInt32()
                    } as object).ToList();
                }
                else if (awardsValue is IEnumerable<object> awardsEnumerable) {
                    // Enumerable<object> (e.g., List<object>)
                    return awardsEnumerable.Select(a => new
                    {
                        title = (a as IDictionary<string, object?>)["title"]?.ToString() ?? "",
                        organization = (a as IDictionary<string, object?>)["organization"]?.ToString() ?? "",
                        year = Convert.ToInt32((a as IDictionary<string, object?>)["year"] ?? 0)
                    } as object).ToList();
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Error extracting awards array");
        }

        return new List<object>();
    }

    /// <summary>
    /// Extract skills list from resume document (handles multiple formats)
    /// </summary>
    private List<string> ExtractSkillsList(Dictionary<string, object?> resumeDoc)
    {
        try {
            if (resumeDoc.TryGetValue("skills", out var skillsValue)) {
                if (skillsValue is JsonElement skillsElement && skillsElement.ValueKind == JsonValueKind.Array) {
                    // JsonElement array
                    return skillsElement.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
                else if (skillsValue is IEnumerable<object> skillsEnumerable) {
                    // Enumerable<object> (e.g., List<object>)
                    return skillsEnumerable.Select(s => s?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Error extracting skills list");
        }

        return new List<string>();
    }

    /// <summary>
    /// Extract experience details from resume document (handles multiple storage formats)
    /// </summary>
    private object ExtractExperienceDetails(Dictionary<string, object?> resumeDoc)
    {
        try {
            // First, try to use the strongly typed resume main class parser
            var resumeMain = ParseResumeMainClassFromDoc(resumeDoc);

            if (resumeMain?.Resume != null) {
                // If parsing succeeded, return the structured experience data
                return new
                {
                    workExperience = resumeMain.Resume.WorkExperience.Select(we => new
                    {
                        jobTitle = we.JobTitle,
                        company = we.Company,
                        duration = we.Duration,
                        responsibilities = we.Responsibilities
                    }),
                    education = resumeMain.Resume.Education.Select(e => new
                    {
                        degree = e.Degree,
                        institution = e.Institution,
                        graduationYear = e.GraduationYear,
                        location = e.Location
                    }),
                    certifications = resumeMain.Resume.Certifications.Select(c => new
                    {
                        title = c.Title,
                        issuingOrganization = c.IssuingOrganization,
                        dateIssued = c.DateIssued
                    }),
                    awards = resumeMain.Resume.Awards.Select(a => new
                    {
                        title = a.Title,
                        organization = a.Organization,
                        year = a.Year
                    })
                };
            }

            // If parsing as main class failed, fallback to manual extraction
            var workExperienceArray = ExtractWorkExperienceArray(resumeDoc);
            var educationArray = ExtractEducationArray(resumeDoc);
            var certificationsArray = ExtractCertificationsArray(resumeDoc);
            var awardsArray = ExtractAwardsArray(resumeDoc);

            return new
            {
                workExperience = workExperienceArray,
                education = educationArray,
                certifications = certificationsArray,
                awards = awardsArray
            };
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Error extracting experience details");
            return new
            {
                workExperience = new List<object>(),
                education = new List<object>(),
                certifications = new List<object>(),
                awards = new List<object>()
            };
        }
    }

    /// <summary>
    /// Create a standardized error response for HTTP functions
    /// </summary>
    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        AddCorsHeaders(response, req);

        var errorResponse = new
        {
            success = false,
            message = errorMessage
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, options));

        return response;
    }

    // Helper: check valid file extension for resume
    private static bool HasValidResumeExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf" };
        return valid.Contains(ext);
    }

    // Helper: basic file signature checks for common resume formats
    private static bool IsValidResumeFile(string fileName, byte[] fileData)
    {
        if (string.IsNullOrEmpty(fileName) || fileData == null || fileData.Length == 0) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext == ".pdf")
        {
            // PDF: %PDF
            return fileData.Length >= 4 && fileData[0] == 0x25 && fileData[1] == 0x50 && fileData[2] == 0x44 && fileData[3] == 0x46;
        }

        if (ext == ".doc")
        {
            // DOC (OLE): D0 CF 11 E0
            return fileData.Length >= 8 && fileData[0] == 0xD0 && fileData[1] == 0xCF && fileData[2] == 0x11 && fileData[3] == 0xE0;
        }

        if (ext == ".docx")
        {
            // DOCX is ZIP-based: PK
            return fileData.Length >= 2 && fileData[0] == 0x50 && fileData[1] == 0x4B;
        }

        // For txt/rtf accept presence of content
        if (ext == ".txt" || ext == ".rtf")
            return fileData.Length > 0;

        return false;
    }

    private static bool IsResumeFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf" };
        return valid.Contains(ext);
    }

    private static string GetMimeTypeFromExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return "application/octet-stream";
        extension = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            ".rtf" => "application/rtf",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Format a work document for UI display with SAS URL generation
    /// </summary>
    private async Task<WorkDocumentData> FormatWorkDocumentForUIAsync(Dictionary<string, object?> workDoc, DataLakeClientFactory dataLakeFactory, string twinId)
    {
        try
        {
            var documentId = workDoc.GetValueOrDefault("id")?.ToString() ?? "";
            var fileName = workDoc.GetValueOrDefault("fileName")?.ToString() ?? "";
            var createdAt = DateTime.TryParse(workDoc.GetValueOrDefault("createdAt")?.ToString(), out var createdDate) ? createdDate : DateTime.MinValue;
            var processedAt = DateTime.TryParse(workDoc.GetValueOrDefault("processedAt")?.ToString(), out var processedDate) ? processedDate : DateTime.MinValue;

            var fullName = workDoc.GetValueOrDefault("fullName")?.ToString() ?? "";
            var email = workDoc.GetValueOrDefault("email")?.ToString() ?? "";
            var currentJobTitle = workDoc.GetValueOrDefault("currentJobTitle")?.ToString() ?? "";
            var currentCompany = workDoc.GetValueOrDefault("currentCompany")?.ToString() ?? "";
            var summary = workDoc.GetValueOrDefault("summary")?.ToString() ?? "";

            // Try generate SAS URL
            string? fileSas = null;
            try
            {
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var filePath = workDoc.GetValueOrDefault("filePath")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(fileName))
                {
                    var full = $"{filePath}/{fileName}";
                    fileSas = await dataLakeClient.GenerateSasUrlAsync(full, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error generating SAS URL for work document {DocId}", documentId);
            }

            return new WorkDocumentData
            {
                DocumentId = documentId,
                FileName = fileName,
                UploadedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                ProcessedAt = processedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                DaysAgo = Math.Max(0, (DateTime.UtcNow - createdDate).Days),
                FullName = fullName,
                Email = email,
                CurrentJobTitle = currentJobTitle,
                CurrentCompany = currentCompany,
                Summary = summary,
                ExecutiveSummary = workDoc.GetValueOrDefault("executiveSummary")?.ToString() ?? "",
                Stats = new WorkDocumentStats
                {
                    WorkExperience = Convert.ToInt32(workDoc.GetValueOrDefault("totalWorkExperience") ?? 0),
                    Education = Convert.ToInt32(workDoc.GetValueOrDefault("totalEducation") ?? 0),
                    Skills = Convert.ToInt32(workDoc.GetValueOrDefault("totalSkills") ?? 0),
                    Certifications = Convert.ToInt32(workDoc.GetValueOrDefault("totalCertifications") ?? 0),
                    Projects = Convert.ToInt32(workDoc.GetValueOrDefault("totalProjects") ?? 0),
                    Awards = Convert.ToInt32(workDoc.GetValueOrDefault("totalAwards") ?? 0)
                },
                FileUrl = fileSas,
                FullDocumentData = workDoc
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error formatting work document for UI");
            return new WorkDocumentData { DocumentId = workDoc.GetValueOrDefault("id")?.ToString() ?? "", FileName = workDoc.GetValueOrDefault("fileName")?.ToString() ?? "", FullDocumentData = workDoc };
        }
    }

    /// <summary>
    /// Extract boundary string from Content-Type header value
    /// </summary>
    private string? GetBoundary(string contentType)
    {
        // Use a safe regex to extract boundary parameter
        var matches = Regex.Matches(contentType, "boundary=\"?([0-9A-Za-z_'()\\-]+)\"?", RegexOptions.IgnoreCase);
        return matches.FirstOrDefault()?.Groups[1]?.Value;
    }

    /// <summary>
    /// Parse multipart form data asynchronously
    /// </summary>
    private async Task<List<MultipartFormDataSection>> ParseMultipartDataAsync(byte[] bodyBytes, string boundary)
    {
        var sections = new List<MultipartFormDataSection>();

        using (var stream = new MemoryStream(bodyBytes))
        using (var reader = new StreamReader(stream))
        {
            string? header = null;
            var part = new MultipartFormDataSection();

            while ((header = await reader.ReadLineAsync()) != null)
            {
                // Check for boundary
                if (header.Trim() == "--" + boundary)
                {
                    // If part already has data, add to sections
                    if (!string.IsNullOrEmpty(part.Name))
                    {
                        sections.Add(part);
                        part = new MultipartFormDataSection();
                    }
                    continue;
                }

                // Parse Content-Disposition header
                if (header.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                {
                    var disposition = header.Substring(header.IndexOf(':') + 1).Trim();
                    var kvp = disposition.Split(';').Select(p => p.Trim()).ToArray();
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in kvp)
                    {
                        if (item.Contains('='))
                        {
                            var parts = item.Split('=');
                            var key = parts[0].Trim();
                            var val = string.Join("=", parts.Skip(1)).Trim(' ', '"');
                            dict[key] = val;
                        }
                    }

                    if (dict.TryGetValue("name", out var name)) part.Name = name;
                    if (dict.TryGetValue("filename", out var filename)) part.FileName = filename;
                    continue;
                }

                // Read part data
                if (header == "")
                {
                    var data = await reader.ReadToEndAsync();
                    part.Data = Encoding.UTF8.GetBytes(data);
                    break;
                }
            }

            // Add last part if any
            if (!string.IsNullOrEmpty(part.Name))
            {
                sections.Add(part);
            }
        }

        return sections;
    }

    // Helper classes
    private class MultipartFormDataSection
    {
        public string Name { get; set; } = null!;
        public string? FileName { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string StringValue => Encoding.UTF8.GetString(Data);
    }

    /// <summary>
    /// Add CORS headers to the response
    /// </summary>
    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData req)
    {
        // Allow from any origin (update with stricter policy if needed)
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        
        // Expose headers to client (if needed)
        response.Headers.Add("Access-Control-Expose-Headers", "Content-Disposition");
    }
}