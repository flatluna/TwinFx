using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Agent for processing work-related documents, particularly resumes/CVs
/// Handles Document Intelligence extraction and AI-powered data structuring
/// </summary>
public class WorkAgent
{
    private readonly ILogger<WorkAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly DocumentIntelligenceService _documentIntelligenceService;
    private readonly CosmosDbTwinProfileService _cosmosService;
    private Kernel? _kernel;

    public WorkAgent(ILogger<WorkAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Initialize Document Intelligence Service
        _documentIntelligenceService = new DocumentIntelligenceService(
            LoggerFactory.Create(builder => builder.AddConsole()),
            _configuration);
            
        // Initialize Cosmos DB Service
        _cosmosService = new CosmosDbTwinProfileService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
            _configuration);
        
        _logger.LogInformation("🔧 WorkAgent initialized successfully");
    }

    /// <summary>
    /// Process a resume document through the complete pipeline:
    /// 1. Upload resume and get SAS URL
    /// 2. Extract data using Document Intelligence
    /// 3. Structure data using AI/LLM
    /// 4. Save to Cosmos DB TwinWork container
    /// </summary>
    /// <param name="containerName">DataLake container name (usually twinId)</param>
    /// <param name="filePath">Path within the container</param>
    /// <param name="fileName">Resume file name</param>
    /// <param name="twinId">Twin ID for the person</param>
    /// <returns>Structured resume data result</returns>
    public async Task<WorkProcessingResult> ProcessResumeAsync(string containerName, string filePath, string fileName, string twinId)
    {
        try
        {
            _logger.LogInformation("🔥 Starting Resume Processing Pipeline for Twin: {TwinId}", twinId);
            _logger.LogInformation("📄 Processing resume: {FileName} in {ContainerName}/{FilePath}", fileName, containerName, filePath);

            var result = new WorkProcessingResult
            {
                Success = false,
                TwinId = twinId,
                FileName = fileName,
                ContainerName = containerName,
                FilePath = filePath,
                ProcessedAt = DateTime.UtcNow
            };

            // STEP 1: Generate SAS URL for Document Intelligence
            _logger.LogInformation("📤 STEP 1: Generating SAS URL for resume document...");
            
            var dataLakeFactory = new DataLakeClientFactory(
                LoggerFactory.Create(builder => builder.AddConsole()), 
                _configuration);
            var dataLakeClient = dataLakeFactory.CreateClient(containerName);
            
            // Generate SAS URL valid for 2 hours (plenty of time for processing)
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync($"{filePath}/{fileName}", TimeSpan.FromHours(2));
            
            if (string.IsNullOrEmpty(sasUrl))
            {
                _logger.LogError("❌ Failed to generate SAS URL for resume document");
                result.ErrorMessage = "Failed to generate SAS URL for resume document";
                return result;
            }

            _logger.LogInformation("✅ SAS URL generated successfully for Document Intelligence processing");
            result.SasUrl = sasUrl;

            // STEP 2: Extract data using Document Intelligence
            _logger.LogInformation("🧠 STEP 2: Extracting resume data using Document Intelligence...");

            var documentAnalysisResult = await _documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
    
            if (!documentAnalysisResult.Success || string.IsNullOrEmpty(documentAnalysisResult.TextContent))
            {
                _logger.LogError("❌ Document Intelligence failed to extract text from resume");
                result.ErrorMessage = "Document Intelligence failed to extract text from resume";
                return result;
            }   

            _logger.LogInformation("✅ Document Intelligence extraction completed");
            _logger.LogInformation("📄 Extracted text length: {TextLength} characters", documentAnalysisResult.TextContent.Length);
            
            result.ExtractedText = documentAnalysisResult.TextContent;
            result.DocumentIntelligenceSuccess = true;

            // STEP 3: Structure data using AI/LLM with Semantic Kernel
            _logger.LogInformation("🤖 STEP 3: Structuring resume data using AI/LLM...");
            
            var structuredData = await ProcessResumeWithAIAsync(documentAnalysisResult.TextContent, twinId);
            
            if (structuredData == null)
            {
                _logger.LogError("❌ AI processing failed to structure resume data");
                result.ErrorMessage = "AI processing failed to structure resume data";
                return result;
            }

            _logger.LogInformation("✅ AI processing completed successfully");
            result.StructuredData = structuredData;
            result.AiProcessingSuccess = true;

            // STEP 4: Save to Cosmos DB TwinWork container
            _logger.LogInformation("💾 STEP 4: Saving structured resume data to Cosmos DB TwinWork container...");
            
            var cosmosDbSaved = await SaveWorkDocumentAsync(structuredData, twinId, fileName, filePath, containerName, sasUrl);
            
            if (!cosmosDbSaved)
            {
                _logger.LogError("❌ Failed to save resume data to Cosmos DB TwinWork container");
                result.ErrorMessage = "Failed to save resume data to Cosmos DB TwinWork container";
                return result;
            }

            _logger.LogInformation("✅ Cosmos DB save completed successfully");
            result.CosmosDbSaveSuccess = true;

            _logger.LogInformation("🎯 Resume processing completed successfully through all 4 steps!");
            _logger.LogInformation("📊 Final result: Document Intelligence ✅, AI Processing ✅, Cosmos DB ✅");

            // Mark as completely successful
            result.Success = true;
            result.Message = "All steps completed successfully: Resume uploaded, text extracted, structured with AI, and saved to Cosmos DB";

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in ProcessResumeAsync");
            
            return new WorkProcessingResult
            {
                Success = false,
                TwinId = twinId,
                FileName = fileName,
                ErrorMessage = $"Processing error: {ex.Message}",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Process resume text using AI/LLM to extract structured data
    /// Uses Semantic Kernel with Azure OpenAI to analyze and structure resume content
    /// </summary>
    /// <param name="extractedText">Raw text extracted from resume document</param>
    /// <param name="twinId">Twin ID for context and logging</param>
    /// <returns>Structured resume data in required JSON format</returns>
    private async Task<ResumeStructuredData?> ProcessResumeWithAIAsync(string extractedText, string twinId)
    {
        try
        {
            _logger.LogInformation("🧠 Starting AI processing of resume text for Twin: {TwinId}", twinId);
            _logger.LogInformation("📝 Text to process: {TextLength} characters", extractedText.Length);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            var resumeProcessingPrompt = $$"""
Eres un experto analizador de currículums/resumes. Tu tarea es extraer información estructurada de un resume en texto y convertirla al formato JSON específico requerido.

TEXTO DEL RESUME A ANALIZAR:
{{extractedText}}

INSTRUCCIONES IMPORTANTES:
1. Analiza cuidadosamente todo el texto del resume
2. Extrae SOLO información que esté presente en el texto
3. NO inventes datos que no aparezcan en el resume
4. Para datos faltantes, usa valores vacíos o arrays vacíos
5. Para fechas, utiliza el formato que aparezca en el resume
6. Para salarios y beneficios, extrae SOLO si está explícitamente mencionado
7. Mantén la información original sin modificaciones
8: Crea un resumen ejecutivo del CV/resume en HTML con análisis detallado, fortalezas, experiencia clave y recomendaciones profesionales

FORMATO JSON REQUERIDO:
{
  "resume": {
    "executive_summary": "HTML detallado con análisis profesional completo del candidato, incluyendo fortalezas, experiencia clave, competencias principales y recomendaciones",
    "personal_information": {
      "full_name": "",
      "address": "",
      "phone_number": "",
      "email": "",
      "linkedin": ""
    },
    "summary": "",
    "skills": [],
    "education": [
      {
        "degree": "",
        "institution": "",
        "graduation_year": 0,
        "location": ""
      }
    ],
    "work_experience": [
      {
        "job_title": "",
        "company": "",
        "duration": "",
        "responsibilities": []
      }
    ],
    "salaries": [
      {
        "job_title": "",
        "company": "",
        "base_salary": 0,
        "currency": "USD",
        "year": 0
      }
    ],
    "benefits": [
      {
        "job_title": "",
        "company": "",
        "benefits": []
      }
    ],
    "certifications": [
      {
        "title": "",
        "issuing_organization": "",
        "date_issued": ""
      }
    ],
    "projects": [
      {
        "project_name": "",
        "description": "",
        "technologies": [],
        "duration": ""
      }
    ],
    "awards": [
      {
        "title": "",
        "organization": "",
        "year": 0
      }
    ],
    "professional_associations": [
      {
        "organization": "",
        "role": "",
        "duration": ""
      }
    ]
  }
}

REGLAS ESPECÍFICAS:
- Si no hay información de salarios en el resume, deja el array "salaries" vacío
- Si no hay información de beneficios específicos, deja el array "benefits" vacío
- Para "graduation_year" y "year" usa números enteros, 0 si no está disponible
- Para "base_salary" usa números decimales, 0 si no está disponible
- Para arrays de strings como "skills", "responsibilities", "technologies", "benefits", usa arrays vacíos [] si no hay información
- Mantén la información en el idioma original del resume

RESPONDE ÚNICAMENTE CON EL JSON VÁLIDO, SIN TEXTO ADICIONAL, SIN MARKDOWN, SIN EXPLICACIONES.
""";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            
            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(resumeProcessingPrompt);

            // Create execution settings
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 4000 }, // Large token limit for complete resume processing
                    { "temperature", 0.1 }  // Low temperature for consistent, factual extraction
                }
            };

            // Get the AI response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content?.Trim();
            
            if (string.IsNullOrEmpty(aiResponse))
            {
                _logger.LogError("❌ AI response was empty");
                return null;
            }

            _logger.LogInformation("✅ AI response received, attempting to parse JSON");
            _logger.LogInformation("📄 AI response length: {ResponseLength} characters", aiResponse.Length);

            // Clean the response (remove markdown if present)
            var cleanedResponse = CleanJsonResponse(aiResponse);
            
            _logger.LogInformation("🧹 Cleaned AI response for JSON parsing");

            // Parse the JSON response
            var structuredData = JsonSerializer.Deserialize<ResumeStructuredData>(cleanedResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            });

            if (structuredData?.Resume == null)
            {
                _logger.LogError("❌ Failed to deserialize structured resume data or resume data is null");
                _logger.LogInformation("🔍 Cleaned response for debugging: {CleanedResponse}", cleanedResponse);
                return null;
            }

            _logger.LogInformation("✅ Successfully structured resume data");
            _logger.LogInformation("👤 Extracted name: {FullName}", structuredData.Resume.PersonalInformation.FullName);
            _logger.LogInformation("🎓 Education entries: {EducationCount}", structuredData.Resume.Education.Count);
            _logger.LogInformation("💼 Work experience entries: {WorkCount}", structuredData.Resume.WorkExperience.Count);
            _logger.LogInformation("🛠️ Skills count: {SkillsCount}", structuredData.Resume.Skills.Count);
            _logger.LogInformation("🏆 Certifications: {CertCount}", structuredData.Resume.Certifications.Count);

            return structuredData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in AI processing of resume text");
            return null;
        }
    }

    /// <summary>
    /// Initialize Semantic Kernel for AI processing
    /// Follows the same pattern as other agents (InvoicesAgent, PhotosAgent, etc.)
    /// </summary>
    private async Task InitializeKernelAsync()
    {
        if (_kernel != null)
            return; // Already initialized

        try
        {
            _logger.LogInformation("🔧 Initializing Semantic Kernel for WorkAgent...");

            // Create kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Get Azure OpenAI configuration
            var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                          _configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? 
                          throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

            var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                        _configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? 
                        throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

            var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                                _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ?? 
                                "gpt4mini";

            _logger.LogInformation("🔑 Azure OpenAI Configuration:");
            _logger.LogInformation("   📡 Endpoint: {Endpoint}", endpoint);
            _logger.LogInformation("   🚀 Deployment: {DeploymentName}", deploymentName);

            // Add Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Build the kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized successfully for WorkAgent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for WorkAgent");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Clean AI response to ensure valid JSON parsing
    /// Removes markdown formatting and common AI response artifacts
    /// </summary>
    /// <param name="aiResponse">Raw AI response</param>
    /// <returns>Cleaned JSON string</returns>
    private static string CleanJsonResponse(string aiResponse)
    {
        if (string.IsNullOrEmpty(aiResponse))
            return aiResponse;

        var cleaned = aiResponse.Trim();

        // Remove markdown JSON formatting
        if (cleaned.StartsWith("```json"))
            cleaned = cleaned.Substring(7);
        if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring(3);
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);

        // Remove any leading/trailing whitespace
        cleaned = cleaned.Trim();

        // Remove any text before the first { or after the last }
        var firstBrace = cleaned.IndexOf('{');
        var lastBrace = cleaned.LastIndexOf('}');
        
        if (firstBrace >= 0 && lastBrace >= 0 && lastBrace > firstBrace)
        {
            cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return cleaned;
    }

    /// <summary>
    /// Save structured resume data to Cosmos DB TwinWork container
    /// Follows the same pattern as SavePhotoDocumentAsync and other save methods
    /// </summary>
    /// <param name="resumeData">Structured resume data from AI processing</param>
    /// <param name="twinId">Twin ID (used as partition key)</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="filePath">File path in DataLake</param>
    /// <param name="containerName">DataLake container name</param>
    /// <param name="sasUrl">SAS URL for the document</param>
    /// <returns>True if saved successfully</returns>
    private async Task<bool> SaveWorkDocumentAsync(ResumeStructuredData resumeData, string twinId, string fileName, string filePath, string containerName, string sasUrl)
    {
        try
        {
            _logger.LogInformation("💾 Saving work document to Cosmos DB TwinWork container for Twin: {TwinId}", twinId);

            // Create work document for Cosmos DB with structured nested objects
            var workDocument = new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid().ToString(), // Generate unique ID for the work document
                ["TwinID"] = twinId, // Partition key
                ["documentType"] = "resume",
                ["fileName"] = fileName,
                ["filePath"] = filePath,
                ["containerName"] = containerName,
                ["sasUrl"] = sasUrl,
                ["processedAt"] = DateTime.UtcNow.ToString("O"),
                ["createdAt"] = DateTime.UtcNow.ToString("O"),
                ["success"] = true,
                
                // ✅ Store the complete structured resume data as NESTED OBJECTS (not string)
                ["resumeData"] = new Dictionary<string, object?>
                {
                    ["resume"] = new Dictionary<string, object?>
                    {
                        ["executive_summary"] = resumeData.Resume.ExecutiveSummary,
                        ["personal_information"] = new Dictionary<string, object?>
                        {
                            ["full_name"] = resumeData.Resume.PersonalInformation.FullName,
                            ["address"] = resumeData.Resume.PersonalInformation.Address,
                            ["phone_number"] = resumeData.Resume.PersonalInformation.PhoneNumber,
                            ["email"] = resumeData.Resume.PersonalInformation.Email,
                            ["linkedin"] = resumeData.Resume.PersonalInformation.LinkedIn
                        },
                        ["summary"] = resumeData.Resume.Summary,
                        ["skills"] = resumeData.Resume.Skills,
                        ["education"] = resumeData.Resume.Education.Select(e => new Dictionary<string, object?>
                        {
                            ["degree"] = e.Degree,
                            ["institution"] = e.Institution,
                            ["graduation_year"] = e.GraduationYear,
                            ["location"] = e.Location
                        }).ToList(),
                        ["work_experience"] = resumeData.Resume.WorkExperience.Select(w => new Dictionary<string, object?>
                        {
                            ["job_title"] = w.JobTitle,
                            ["company"] = w.Company,
                            ["duration"] = w.Duration,
                            ["responsibilities"] = w.Responsibilities
                        }).ToList(),
                        ["salaries"] = resumeData.Resume.Salaries.Select(s => new Dictionary<string, object?>
                        {
                            ["job_title"] = s.JobTitle,
                            ["company"] = s.Company,
                            ["base_salary"] = s.BaseSalary,
                            ["currency"] = s.Currency,
                            ["year"] = s.Year
                        }).ToList(),
                        ["benefits"] = resumeData.Resume.Benefits.Select(b => new Dictionary<string, object?>
                        {
                            ["job_title"] = b.JobTitle,
                            ["company"] = b.Company,
                            ["benefits"] = b.Benefits
                        }).ToList(),
                        ["certifications"] = resumeData.Resume.Certifications.Select(c => new Dictionary<string, object?>
                        {
                            ["title"] = c.Title,
                            ["issuing_organization"] = c.IssuingOrganization,
                            ["date_issued"] = c.DateIssued
                        }).ToList(),
                        ["projects"] = resumeData.Resume.Projects.Select(p => new Dictionary<string, object?>
                        {
                            ["project_name"] = p.ProjectName,
                            ["description"] = p.Description,
                            ["technologies"] = p.Technologies,
                            ["duration"] = p.Duration
                        }).ToList(),
                        ["awards"] = resumeData.Resume.Awards.Select(a => new Dictionary<string, object?>
                        {
                            ["title"] = a.Title,
                            ["organization"] = a.Organization,
                            ["year"] = a.Year
                        }).ToList(),
                        ["professional_associations"] = resumeData.Resume.ProfessionalAssociations.Select(pa => new Dictionary<string, object?>
                        {
                            ["organization"] = pa.Organization,
                            ["role"] = pa.Role,
                            ["duration"] = pa.Duration
                        }).ToList()
                    }
                },
                
                // Extract key information for easy querying (following InvoiceDocument pattern)
                ["fullName"] = resumeData.Resume.PersonalInformation.FullName,
                ["email"] = resumeData.Resume.PersonalInformation.Email,
                ["phoneNumber"] = resumeData.Resume.PersonalInformation.PhoneNumber,
                ["address"] = resumeData.Resume.PersonalInformation.Address,
                ["linkedin"] = resumeData.Resume.PersonalInformation.LinkedIn,
                
                // Work experience summary
                ["totalWorkExperience"] = resumeData.Resume.WorkExperience.Count,
                ["currentJobTitle"] = resumeData.Resume.WorkExperience.FirstOrDefault()?.JobTitle ?? "",
                ["currentCompany"] = resumeData.Resume.WorkExperience.FirstOrDefault()?.Company ?? "",
                
                // Education summary
                ["totalEducation"] = resumeData.Resume.Education.Count,
                ["highestDegree"] = resumeData.Resume.Education.FirstOrDefault()?.Degree ?? "",
                ["lastInstitution"] = resumeData.Resume.Education.FirstOrDefault()?.Institution ?? "",
                
                // Skills and certifications
                ["totalSkills"] = resumeData.Resume.Skills.Count,
                ["skillsList"] = resumeData.Resume.Skills, // Store as array, not JSON string
                ["totalCertifications"] = resumeData.Resume.Certifications.Count,
                
                // Projects and awards
                ["totalProjects"] = resumeData.Resume.Projects.Count,
                ["totalAwards"] = resumeData.Resume.Awards.Count,
                
                // Salary and benefits information
                ["hasSalaryInfo"] = resumeData.Resume.Salaries.Any(),
                ["hasBenefitsInfo"] = resumeData.Resume.Benefits.Any(),
                
                // Professional associations
                ["totalAssociations"] = resumeData.Resume.ProfessionalAssociations.Count,
                
                // Summary information
                ["summary"] = resumeData.Resume.Summary,
                
                // ⭐ NEW: Executive Summary for quick UI access
                ["executiveSummary"] = resumeData.Resume.ExecutiveSummary,
                
                // Type identifier for queries
                ["type"] = "work_document"
            };

            // Save to Cosmos DB TwinWork container (following the same pattern as SavePhotoDocumentAsync)
            var success = await _cosmosService.SaveWorkDocumentAsync(workDocument);

            if (success)
            {
                _logger.LogInformation("✅ Work document saved successfully to Cosmos DB");
                _logger.LogInformation("👤 Resume for: {FullName} with {WorkCount} work experiences and {EducationCount} education records", 
                    resumeData.Resume.PersonalInformation.FullName, 
                    resumeData.Resume.WorkExperience.Count,
                    resumeData.Resume.Education.Count);
                return true;
            }
            else
            {
                _logger.LogError("❌ Failed to save work document to Cosmos DB");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error saving work document to Cosmos DB");
            return false;
        }
    }
}

/// <summary>
/// Result of work document processing
/// </summary>
public class WorkProcessingResult
{
    public bool Success { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? SasUrl { get; set; }
    public string? ExtractedText { get; set; }
    public bool DocumentIntelligenceSuccess { get; set; }
    public ResumeStructuredData? StructuredData { get; set; }
    public bool AiProcessingSuccess { get; set; }
    public bool CosmosDbSaveSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// Structured resume data model matching the required JSON format
/// </summary>
public class ResumeStructuredData
{
    [JsonPropertyName("resume")]
    public ResumeData Resume { get; set; } = new();

    // Metadata and summary fields (populated when read from Cosmos DB)
    public string Id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string SasUrl { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool Success { get; set; }

    // Aggregate/summarized fields for UI and queries
    public int TotalCertifications { get; set; }
    public int TotalProjects { get; set; }
    public int TotalAwards { get; set; }
    public bool HasSalaryInfo { get; set; }
    public bool HasBenefitsInfo { get; set; }
    public int TotalAssociations { get; set; }

    // Convenience summary fields (may duplicate nested resume values)
    public string Summary { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
}

public class ResumeData
{
    [JsonPropertyName("executive_summary")]
    public string ExecutiveSummary { get; set; } = string.Empty;

    [JsonPropertyName("personal_information")]
    public PersonalInformation PersonalInformation { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("skills")]
    public List<string> Skills { get; set; } = new();

    [JsonPropertyName("education")]
    public List<EducationItem> Education { get; set; } = new();

    [JsonPropertyName("work_experience")]
    public List<WorkExperience> WorkExperience { get; set; } = new();

    [JsonPropertyName("salaries")]
    public List<SalaryInfo> Salaries { get; set; } = new();

    [JsonPropertyName("benefits")]
    public List<BenefitInfo> Benefits { get; set; } = new();

    [JsonPropertyName("certifications")]
    public List<Certification> Certifications { get; set; } = new();

    [JsonPropertyName("projects")]
    public List<Project> Projects { get; set; } = new();

    [JsonPropertyName("awards")]
    public List<Award> Awards { get; set; } = new();

    [JsonPropertyName("professional_associations")]
    public List<ProfessionalAssociation> ProfessionalAssociations { get; set; } = new();
}

public class PersonalInformation
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("linkedin")]
    public string LinkedIn { get; set; } = string.Empty;
}

public class EducationItem
{
    [JsonPropertyName("degree")]
    public string Degree { get; set; } = string.Empty;

    [JsonPropertyName("institution")]
    public string Institution { get; set; } = string.Empty;

    [JsonPropertyName("graduation_year")]
    public int GraduationYear { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
}

public class WorkExperience
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("responsibilities")]
    public List<string> Responsibilities { get; set; } = new();
}

public class SalaryInfo
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("base_salary")]
    public decimal BaseSalary { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

public class BenefitInfo
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("benefits")]
    public List<string> Benefits { get; set; } = new();
}

public class Certification
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("issuing_organization")]
    public string IssuingOrganization { get; set; } = string.Empty;

    [JsonPropertyName("date_issued")]
    public string DateIssued { get; set; } = string.Empty;
}

public class Project
{
    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("technologies")]
    public List<string> Technologies { get; set; } = new();

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;
}

public class Award
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

public class ProfessionalAssociation
{
    [JsonPropertyName("organization")]
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;
}