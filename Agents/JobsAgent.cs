using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Agente especializado en optimización de currículums para oportunidades laborales específicas
/// Utiliza AI para analizar resumes existentes y ofertas de trabajo para crear versiones mejoradas
/// 
/// Funcionalidades principales:
/// - Análisis comparativo entre resume y job opportunity
/// - Generación de resume optimizado con AI/LLM
/// - Formato HTML profesional con estilos y colores
/// - Recomendaciones específicas para mejorar matching
/// - Integración completa con TwinFx ecosystem
/// </summary>
public class JobsAgent
{
    private readonly ILogger<JobsAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly WorkDocumentsCosmosService _workDocumentsService;
    private readonly CosmosDbService _cosmosService;
    private Kernel? _kernel;

    public JobsAgent(ILogger<JobsAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Initialize Work Documents Service for TwinWork container operations using compatibility extension
        _workDocumentsService = _configuration.CreateWorkDocumentsService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<WorkDocumentsCosmosService>());

        // Initialize Cosmos DB Service for Job Opportunities operations using compatibility extension
        _cosmosService = _configuration.CreateCosmosService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>());

        _logger.LogInformation("?? JobsAgent initialized successfully ");
    }

    /// <summary>
    /// Procesa una solicitud de optimización de resume para una oportunidad laboral específica
    /// Combina datos del resume existente con la descripción del job para crear un resume mejorado
    /// </summary>
    /// <param name="twinId">ID del Twin (usuario)</param>
    /// <param name="resumeId">ID del documento de resume en TwinWork container</param>
    /// <param name="jobOpportunityId">ID de la oportunidad laboral en TwinJobOpportunities container</param>
    /// <param name="userInstructions">Instrucciones específicas del usuario sobre cómo optimizar el resume</param>
    /// <returns>Resultado del procesamiento con el resume optimizado</returns>
    public async Task<JobOptimizationResult> OptimizeResumeForJobAsync(
        string twinId, 
        string resumeId, 
        string jobOpportunityId, 
        string userInstructions)
    {
        try
        {
            _logger.LogInformation("?? Starting resume optimization for Twin: {TwinId}", twinId);
            _logger.LogInformation("?? Resume ID: {ResumeId}, Job ID: {JobId}", resumeId, jobOpportunityId);

            var result = new JobOptimizationResult
            {
                Success = false,
                TwinId = twinId,
                ResumeId = resumeId,
                JobOpportunityId = jobOpportunityId,
                UserInstructions = userInstructions,
                ProcessedAt = DateTime.UtcNow
            };

            // STEP 1: Retrieve resume document from TwinWork container
            _logger.LogInformation("?? STEP 1: Retrieving resume document from TwinWork container...");
            
            var resumeDocument = await _workDocumentsService.GetWorkDocumentByIdAsync(resumeId, twinId);
            if (resumeDocument == null)
            {
                result.ErrorMessage = $"Resume document not found with ID: {resumeId}";
                _logger.LogError("? Resume document not found: {ResumeId}", resumeId);
                return result;
            }

            _logger.LogInformation("? Resume document retrieved successfully");
            result.OriginalResumeData = resumeDocument;

            // STEP 2: Retrieve job opportunity from TwinJobOpportunities container
            _logger.LogInformation("?? STEP 2: Retrieving job opportunity from TwinJobOpportunities container...");
            
            var jobOpportunity = await _cosmosService.GetJobOpportunityByIdAsync(jobOpportunityId, twinId);
            if (jobOpportunity == null)
            {
                result.ErrorMessage = $"Job opportunity not found with ID: {jobOpportunityId}";
                _logger.LogError("? Job opportunity not found: {JobOpportunityId}", jobOpportunityId);
                return result;
            }

            _logger.LogInformation("? Job opportunity retrieved: {Puesto} at {Empresa}", 
                jobOpportunity.Puesto, jobOpportunity.Empresa);
            result.JobOpportunityData = jobOpportunity;

            // STEP 3: Generate optimized resume using AI/LLM
            _logger.LogInformation("?? STEP 3: Generating optimized resume using AI/LLM...");

            var optimizedResumeHtml = await GenerateOptimizedResumeAsync(
                resumeDocument, 
                jobOpportunity, 
                userInstructions, 
                twinId);

            if (string.IsNullOrEmpty(optimizedResumeHtml))
            {
                result.ErrorMessage = "Failed to generate optimized resume with AI";
                _logger.LogError("? AI processing failed to generate optimized resume");
                return result;
            }

            _logger.LogInformation("? AI processing completed successfully");
            result.OptimizedResumeHtml = optimizedResumeHtml;
            result.AiProcessingSuccess = true;

            // STEP 4: Save optimized resume to Cosmos DB (optional)
            _logger.LogInformation("?? STEP 4: Saving optimization result...");
            var saveSuccess = await SaveOptimizationResultAsync(result);
            if (saveSuccess)
            {
                _logger.LogInformation("? Optimization result saved to Cosmos DB");
                result.CosmosDbSaveSuccess = true;
            }
            else
            {
                _logger.LogWarning("?? Failed to save optimization result, but continuing");
            }

            _logger.LogInformation("?? Resume optimization completed successfully!");
            
            // Mark as completely successful
            result.Success = true;
            result.Message = "Resume optimized successfully for the target job opportunity";

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in OptimizeResumeForJobAsync");

            return new JobOptimizationResult
            {
                Success = false,
                TwinId = twinId,
                ResumeId = resumeId,
                JobOpportunityId = jobOpportunityId,
                UserInstructions = userInstructions,
                ErrorMessage = $"Processing error: {ex.Message}",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Genera un resume optimizado usando AI/LLM basado en el resume existente y la oportunidad laboral
    /// Utiliza Semantic Kernel con Azure OpenAI para crear un resume HTML mejorado
    /// </summary>
    /// <param name="resumeDocument">Documento de resume existente</param>
    /// <param name="jobOpportunity">Datos de la oportunidad laboral</param>
    /// <param name="userInstructions">Instrucciones específicas del usuario</param>
    /// <param name="twinId">Twin ID para contexto y logging</param>
    /// <returns>Resume optimizado en formato HTML</returns>
    private async Task<string?> GenerateOptimizedResumeAsync(
        Dictionary<string, object?> resumeDocument,
        JobOpportunityData jobOpportunity,
        string userInstructions,
        string twinId)
    {
        try
        {
            _logger.LogInformation("?? Starting AI processing for resume optimization");

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Extract resume data for AI analysis
            var resumeDataForAI = ExtractResumeDataForAI(resumeDocument);
            var jobDataForAI = ExtractJobDataForAI(jobOpportunity);

            var optimizationPrompt = $"""
Eres un experto en recursos humanos y optimización de currículums con amplia experiencia en matching de candidatos.

Tu tarea es crear un NUEVO RESUME OPTIMIZADO que maximice las oportunidades de obtener la posición específica.

?? RESUME ACTUAL:
{resumeDataForAI}

?? OPORTUNIDAD LABORAL OBJETIVO:
{jobDataForAI}

?? INSTRUCCIONES ESPECÍFICAS DEL USUARIO:
{userInstructions}

?? INSTRUCCIONES PARA LA OPTIMIZACIÓN:

1. **ANÁLISIS COMPARATIVO**:
   - Identifica gaps entre el resume actual y los requisitos del job
   - Encuentra fortalezas del candidato que aplican al puesto
   - Detecta keywords importantes que faltan o están poco destacadas

2. **OPTIMIZACIÓN ESTRATÉGICA**:
   - Reorganiza secciones para destacar experiencia más relevante
   - Reformula descripciones para usar keywords del job posting
   - Ajusta el enfoque para alinearse con los requisitos específicos
   - Mantén la veracidad - NO inventes experiencias falsas

3. **MEJORAS ESPECÍFICAS**:
   - Summary/Objetivo: Crear uno específico para esta posición
   - Experiencia: Reorganizar y reformular para destacar logros relevantes
   - Skills: Priorizar y agrupar las habilidades más importantes para el job
   - Educación: Destacar aspectos educativos relevantes al puesto
   - Proyectos/Certificaciones: Resaltar los más pertinentes

4. **FORMATO HTML PROFESIONAL**:
   - Usa HTML5 con CSS inline para máxima compatibilidad
   - Aplica una paleta de colores profesional y moderna
   - Crea layout responsive y visualmente atractivo
   - Incluye secciones bien organizadas con iconos/emojis apropiados
   - Usa tipografías legibles y jerarquía visual clara

5. **ELEMENTOS VISUALES REQUERIDOS**:
   - Header con información de contacto estilizado
   - Sección de resumen ejecutivo destacada
   - Timeline visual para experiencia laboral
   - Grid o tabla para habilidades técnicas
   - Secciones claramente diferenciadas con colores
   - Uso estratégico de negritas, colores y espaciado

6. **KEYWORDS Y ATS OPTIMIZATION**:
   - Integra keywords del job posting de manera natural
   - Usa sinónimos y variaciones de términos técnicos
   - Asegura buena densidad de keywords sin sobreoptimización

?? REGLAS CRÍTICAS:
- NO inventes experiencias, logros o habilidades falsas
- Mantén fechas y datos factuales del resume original
- Solo reorganiza, reformula y optimiza contenido existente
- Enfócate en destacar lo más relevante para el puesto específico
- Crea un documento que sea genuino pero optimately presentado
- IMPORTANTE: Genera un resume que este alineado a la descripcion del trabajo de la emoresa, las habilidades. Explica por que esta persona estaria perfecta para la empresa.

?? FORMATO DE SALIDA:
Responde ÚNICAMENTE con el HTML completo del resume optimizado.
Incluye estilos CSS inline para una presentación profesional.
No incluyas explicaciones adicionales, solo el HTML final.

El resume debe verse profesional, moderno y específicamente optimizado para esta oportunidad laboral.
""";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();

            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(optimizationPrompt);

            // Create execution settings for optimization task
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 6000 }, // Large token limit for complete resume generation
                    { "temperature", 0.2 }  // Low temperature for consistent, professional output
                }
            };

            // Get the AI response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var optimizedResumeHtml = response.Content?.Trim();

            if (string.IsNullOrEmpty(optimizedResumeHtml))
            {
                _logger.LogError("? AI response was empty");
                return null;
            }

            _logger.LogInformation("? AI optimization response received");
            _logger.LogInformation("?? Optimized resume HTML length: {Length} characters", optimizedResumeHtml.Length);

            // Clean the response (remove markdown if present)
            var cleanedHtml = CleanHtmlResponse(optimizedResumeHtml);

            _logger.LogInformation("?? Cleaned AI response for final output");

            return cleanedHtml;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in AI processing for resume optimization");
            return null;
        }
    }

    /// <summary>
    /// Extrae datos del resume para análisis de AI en formato legible
    /// </summary>
    private string ExtractResumeDataForAI(Dictionary<string, object?> resumeDocument)
    {
        try
        {
            var resumeInfo = new List<string>();

            // Basic information
            resumeInfo.Add("=== INFORMACIÓN BÁSICA ===");
            AddIfExists(resumeInfo, "Nombre completo", resumeDocument.GetValueOrDefault("fullName"));
            AddIfExists(resumeInfo, "Email", resumeDocument.GetValueOrDefault("email"));
            AddIfExists(resumeInfo, "Teléfono", resumeDocument.GetValueOrDefault("phoneNumber"));
            AddIfExists(resumeInfo, "Dirección", resumeDocument.GetValueOrDefault("address"));
            AddIfExists(resumeInfo, "LinkedIn", resumeDocument.GetValueOrDefault("linkedin"));

            // Professional summary
            resumeInfo.Add("\n=== RESUMEN PROFESIONAL ===");
            AddIfExists(resumeInfo, "Resumen", resumeDocument.GetValueOrDefault("summary"));
            AddIfExists(resumeInfo, "Resumen ejecutivo", resumeDocument.GetValueOrDefault("executiveSummary"));

            // Current position
            resumeInfo.Add("\n=== POSICIÓN ACTUAL ===");
            AddIfExists(resumeInfo, "Puesto actual", resumeDocument.GetValueOrDefault("currentJobTitle"));
            AddIfExists(resumeInfo, "Empresa actual", resumeDocument.GetValueOrDefault("currentCompany"));

            // Skills
            resumeInfo.Add("\n=== HABILIDADES ===");
            if (resumeDocument.TryGetValue("skillsList", out var skillsValue))
            {
                var skills = ExtractSkillsList(skillsValue);
                if (skills.Any())
                {
                    resumeInfo.Add($"Habilidades: {string.Join(", ", skills)}");
                }
            }

            // Stats
            resumeInfo.Add("\n=== ESTADÍSTICAS ===");
            AddIfExists(resumeInfo, "Experiencias laborales", resumeDocument.GetValueOrDefault("totalWorkExperience"));
            AddIfExists(resumeInfo, "Estudios", resumeDocument.GetValueOrDefault("totalEducation"));
            AddIfExists(resumeInfo, "Certificaciones", resumeDocument.GetValueOrDefault("totalCertifications"));
            AddIfExists(resumeInfo, "Proyectos", resumeDocument.GetValueOrDefault("totalProjects"));
            AddIfExists(resumeInfo, "Premios", resumeDocument.GetValueOrDefault("totalAwards"));

            // Detailed resume data if available
            if (resumeDocument.TryGetValue("resumeData", out var resumeDataValue) && resumeDataValue != null)
            {
                var detailedData = ExtractDetailedResumeData(resumeDataValue);
                if (!string.IsNullOrEmpty(detailedData))
                {
                    resumeInfo.Add("\n=== DATOS DETALLADOS DEL RESUME ===");
                    resumeInfo.Add(detailedData);
                }
            }

            return string.Join("\n", resumeInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error extracting resume data for AI");
            return "Error extracting resume data";
        }
    }

    /// <summary>
    /// Extrae datos de la oportunidad laboral para análisis de AI
    /// </summary>
    private string ExtractJobDataForAI(JobOpportunityData jobOpportunity)
    {
        try
        {
            var jobInfo = new List<string>();

            jobInfo.Add("=== INFORMACIÓN DE LA EMPRESA ===");
            jobInfo.Add($"Empresa: {jobOpportunity.Empresa ?? "No especificado"}");
            AddIfExists(jobInfo, "URL de la empresa", jobOpportunity.URLCompany ?? "No especificado");
            AddIfExists(jobInfo, "Ubicación", jobOpportunity.Ubicacion ?? "No especificado");

            jobInfo.Add("\n=== DETALLES DEL PUESTO ===");
            jobInfo.Add($"Puesto: {jobOpportunity.Puesto ?? "No especificado"}");
            AddIfExists(jobInfo, "Descripción", jobOpportunity.Descripcion ?? "No especificado");
            AddIfExists(jobInfo, "Responsabilidades", jobOpportunity.Responsabilidades ?? "No especificado");
            AddIfExists(jobInfo, "Habilidades requeridas", jobOpportunity.HabilidadesRequeridas ?? "No especificado");

            jobInfo.Add("\n=== COMPENSACIÓN Y BENEFICIOS ===");
            AddIfExists(jobInfo, "Salario", jobOpportunity.Salario);
            AddIfExists(jobInfo, "Beneficios", jobOpportunity.Beneficios);

            jobInfo.Add("\n=== INFORMACIÓN DE CONTACTO ===");
            AddIfExists(jobInfo, "Contacto", jobOpportunity.ContactoNombre);
            AddIfExists(jobInfo, "Email de contacto", jobOpportunity.ContactoEmail);
            AddIfExists(jobInfo, "Teléfono de contacto", jobOpportunity.ContactoTelefono);

            jobInfo.Add("\n=== INFORMACIÓN ADICIONAL ===");
            AddIfExists(jobInfo, "Estado de aplicación", jobOpportunity.Estado.ToString());
            AddIfExists(jobInfo, "Fecha de aplicación", jobOpportunity.FechaAplicacion?.ToString("yyyy-MM-dd"));
            AddIfExists(jobInfo, "Notas", jobOpportunity.Notas);

            return string.Join("\n", jobInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error extracting job data for AI");
            return "Error extracting job opportunity data";
        }
    }

    /// <summary>
    /// Extrae datos detallados del resume desde resumeData anidado
    /// </summary>
    private string ExtractDetailedResumeData(object resumeDataValue)
    {
        try
        {
            var detailedInfo = new List<string>();

            // Convert resumeDataValue to a usable format
            var resumeDataJson = ConvertToJson(resumeDataValue);
            if (string.IsNullOrEmpty(resumeDataJson))
                return string.Empty;

            using var document = JsonDocument.Parse(resumeDataJson);
            var root = document.RootElement;

            // Check if we have a "resume" property
            if (root.TryGetProperty("resume", out var resume))
            {
                // Extract work experience
                if (resume.TryGetProperty("work_experience", out var workExp) && workExp.ValueKind == JsonValueKind.Array)
                {
                    detailedInfo.Add("\n--- EXPERIENCIA LABORAL ---");
                    foreach (var job in workExp.EnumerateArray())
                    {
                        var jobTitle = GetJsonProperty(job, "job_title");
                        var company = GetJsonProperty(job, "company");
                        var duration = GetJsonProperty(job, "duration");
                        
                        detailedInfo.Add($"• {jobTitle} en {company} ({duration})");
                        
                        if (job.TryGetProperty("responsibilities", out var responsibilities) && 
                            responsibilities.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var resp in responsibilities.EnumerateArray())
                            {
                                detailedInfo.Add($"  - {resp.GetString()}");
                            }
                        }
                    }
                }

                // Extract education
                if (resume.TryGetProperty("education", out var education) && education.ValueKind == JsonValueKind.Array)
                {
                    detailedInfo.Add("\n--- EDUCACIÓN ---");
                    foreach (var edu in education.EnumerateArray())
                    {
                        var degree = GetJsonProperty(edu, "degree");
                        var institution = GetJsonProperty(edu, "institution");
                        var year = GetJsonProperty(edu, "graduation_year");
                        var location = GetJsonProperty(edu, "location");
                        
                        detailedInfo.Add($"• {degree} - {institution} ({year}) - {location}");
                    }
                }

                // Extract certifications
                if (resume.TryGetProperty("certifications", out var certifications) && certifications.ValueKind == JsonValueKind.Array)
                {
                    detailedInfo.Add("\n--- CERTIFICACIONES ---");
                    foreach (var cert in certifications.EnumerateArray())
                    {
                        var title = GetJsonProperty(cert, "title");
                        var org = GetJsonProperty(cert, "issuing_organization");
                        var date = GetJsonProperty(cert, "date_issued");
                        
                        detailedInfo.Add($"• {title} - {org} ({date})");
                    }
                }

                // Extract projects
                if (resume.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
                {
                    detailedInfo.Add("\n--- PROYECTOS ---");
                    foreach (var project in projects.EnumerateArray())
                    {
                        var name = GetJsonProperty(project, "project_name");
                        var description = GetJsonProperty(project, "description");
                        var duration = GetJsonProperty(project, "duration");
                        
                        detailedInfo.Add($"• {name} ({duration})");
                        if (!string.IsNullOrEmpty(description))
                        {
                            detailedInfo.Add($"  Descripción: {description}");
                        }
                        
                        if (project.TryGetProperty("technologies", out var technologies) && 
                            technologies.ValueKind == JsonValueKind.Array)
                        {
                            var techs = technologies.EnumerateArray().Select(t => t.GetString()).Where(t => !string.IsNullOrEmpty(t));
                            detailedInfo.Add($"  Tecnologías: {string.Join(", ", techs)}");
                        }
                    }
                }
            }

            return string.Join("\n", detailedInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error extracting detailed resume data");
            return string.Empty;
        }
    }

    /// <summary>
    /// Guarda el resultado de la optimización en Cosmos DB para futura referencia
    /// </summary>
    private async Task<bool> SaveOptimizationResultAsync(JobOptimizationResult result)
    {
        try
        {
            _logger.LogInformation("?? Saving resume optimization result to Cosmos DB");

            // Create optimization document for Cosmos DB
            var optimizationDocument = new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid().ToString(), // Generate unique ID for the optimization
                ["TwinID"] = result.TwinId, // Partition key
                ["documentType"] = "resume_optimization",
                ["originalResumeId"] = result.ResumeId,
                ["jobOpportunityId"] = result.JobOpportunityId,
                ["userInstructions"] = result.UserInstructions,
                ["success"] = result.Success,
                ["optimizedResumeHtml"] = result.OptimizedResumeHtml,
                ["optimizationAnalysis"] = new Dictionary<string, object?>
                {
                    ["aiProcessingSuccess"] = result.AiProcessingSuccess,
                    ["cosmosDbSaveSuccess"] = result.CosmosDbSaveSuccess,
                    ["processedAt"] = result.ProcessedAt.ToString("O"),
                    ["jobTitle"] = result.JobOpportunityData?.Puesto ?? "",
                    ["company"] = result.JobOpportunityData?.Empresa ?? "",
                    ["resumeOwner"] = result.OriginalResumeData?.GetValueOrDefault("fullName")?.ToString() ?? ""
                },
                ["createdAt"] = DateTime.UtcNow.ToString("O"),
                ["processedAt"] = result.ProcessedAt.ToString("O"),
                ["type"] = "resume_optimization"
            };

            // Save to Cosmos DB TwinWork container (as it's resume-related)
            var success = await _workDocumentsService.SaveWorkDocumentAsync(optimizationDocument);

            if (success)
            {
                _logger.LogInformation("? Resume optimization result saved successfully");
                return true;
            }
            else
            {
                _logger.LogError("? Failed to save resume optimization result");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error saving resume optimization result");
            return false;
        }
    }

    /// <summary>
    /// Initialize Semantic Kernel for AI processing
    /// Follows the same pattern as other agents (InvoicesAgent, WorkAgent, etc.)
    /// </summary>
    private async Task InitializeKernelAsync()
    {
        if (_kernel != null)
            return; // Already initialized

        try
        {
            _logger.LogInformation("?? Initializing Semantic Kernel for JobsAgent...");

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

            _logger.LogInformation("?? Azure OpenAI Configuration:");
            _logger.LogInformation("   ?? Endpoint: {Endpoint}", endpoint);
            _logger.LogInformation("   ?? Deployment: {DeploymentName}", deploymentName);

            // Add Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Build the kernel
            _kernel = builder.Build();

            _logger.LogInformation("? Semantic Kernel initialized successfully for JobsAgent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize Semantic Kernel for JobsAgent");
            throw;
        }

        await Task.CompletedTask;
    }

    // Helper methods

    /// <summary>
    /// Añade información al list si existe y no está vacía
    /// </summary>
    private static void AddIfExists(List<string> list, string label, object? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
        {
            list.Add($"{label}: {value}");
        }
    }

    /// <summary>
    /// Extrae lista de skills desde varios formatos posibles
    /// </summary>
    private List<string> ExtractSkillsList(object? skillsValue)
    {
        try
        {
            if (skillsValue is JsonElement skillsElement && skillsElement.ValueKind == JsonValueKind.Array)
            {
                return skillsElement.EnumerateArray()
                    .Select(s => s.GetString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            else if (skillsValue is IEnumerable<object> skillsEnumerable)
            {
                return skillsEnumerable
                    .Select(s => s?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error extracting skills list");
        }

        return new List<string>();
    }

    /// <summary>
    /// Convierte objeto a JSON string
    /// </summary>
    private string ConvertToJson(object? obj)
    {
        try
        {
            if (obj is string s)
                return s;
            
            if (obj is JsonElement je)
                return je.GetRawText();
            
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Obtiene propiedad de JsonElement de manera segura
    /// </summary>
    private static string GetJsonProperty(JsonElement element, string propertyName)
    {
        try
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : 
                       property.ValueKind == JsonValueKind.Number ? property.ToString() : "";
            }
        }
        catch { }
        
        return string.Empty;
    }

    /// <summary>
    /// Limpia respuesta HTML del AI removiendo markdown y artifacts
    /// </summary>
    private static string CleanHtmlResponse(string htmlResponse)
    {
        if (string.IsNullOrEmpty(htmlResponse))
            return htmlResponse;

        var cleaned = htmlResponse.Trim();

        // Remove markdown HTML formatting
        if (cleaned.StartsWith("```html"))
            cleaned = cleaned.Substring(7);
        if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring(3);
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);

        // Remove any leading/trailing whitespace
        cleaned = cleaned.Trim();

        return cleaned;
    }
}

/// <summary>
/// Resultado del procesamiento de optimización de resume
/// </summary>
public class JobOptimizationResult
{
    public bool Success { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string ResumeId { get; set; } = string.Empty;
    public string JobOpportunityId { get; set; } = string.Empty;
    public string UserInstructions { get; set; } = string.Empty;
    public Dictionary<string, object?>? OriginalResumeData { get; set; }
    public JobOpportunityData? JobOpportunityData { get; set; }
    public string? OptimizedResumeHtml { get; set; }
    public bool AiProcessingSuccess { get; set; }
    public bool CosmosDbSaveSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public DateTime ProcessedAt { get; set; }
}