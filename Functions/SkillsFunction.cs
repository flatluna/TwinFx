using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Agents;
using TwinFx.Services;
using static TwinFx.Agents.SkillsAgent;

namespace TwinFx.Functions;

/// <summary>
/// Azure Functions for managing skills records
/// ========================================================================
/// 
/// Provides RESTful API endpoints for skills management:
/// - Create new skill records
/// - Update existing skill records
/// - Advanced skill tracking and validation
/// 
/// All operations are secured and include proper CORS handling.
/// Uses SkillsCosmosDB service for data persistence.
/// 
/// Author: TwinFx Project
/// Date: January 2025
/// </summary>
public class SkillsFunction
{
    private readonly ILogger<SkillsFunction> _logger;
    private readonly SkillsCosmosDB _skillsCosmosDB;
    private readonly SkillsAgent _skillsAgent;

    public SkillsFunction(ILogger<SkillsFunction> logger, SkillsCosmosDB skillsCosmosDB, SkillsAgent skillsAgent)
    {
        _logger = logger;
        _skillsCosmosDB = skillsCosmosDB;
        _skillsAgent = skillsAgent;
    }

    // ========================================
    // OPTIONS HANDLERS FOR CORS
    // ========================================

    /// <summary>
    /// OPTIONS handler for skills routes
    /// </summary>
    [Function("SkillsOptions")]
    public async Task<HttpResponseData> HandleSkillsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/skills")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🎯 OPTIONS preflight request for twins/{TwinId}/skills", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// OPTIONS handler for specific skill routes
    /// </summary>
    [Function("SkillByIdOptions")]
    public async Task<HttpResponseData> HandleSkillByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/skills/{skillId}")] HttpRequestData req,
        string twinId, string skillId)
    {
        _logger.LogInformation("🎯 OPTIONS preflight request for twins/{TwinId}/skills/{SkillId}", twinId, skillId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// OPTIONS handler for skills learning search routes
    /// </summary>
    [Function("SkillsLearningSearchOptions")]
    public async Task<HttpResponseData> HandleSkillsLearningSearchOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/skills/search-learning")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🎯 OPTIONS preflight request for twins/{TwinId}/skills/search-learning", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    // ========================================
    // MAIN SKILL ENDPOINTS
    // ========================================

    /// <summary>
    /// Create a new skill record
    /// POST /api/twins/{twinId}/skills
    /// </summary>
    [Function("CreateSkill")]
    public async Task<HttpResponseData> CreateSkill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/skills")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🎯 CreateSkill function triggered for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var skillRequest = JsonSerializer.Deserialize<SkillPostRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (skillRequest == null)
            {
                _logger.LogError("❌ Failed to parse skill creation request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid skill data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(skillRequest.Name))
            {
                _logger.LogError("❌ Skill name is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Skill name is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(skillRequest.Category))
            {
                _logger.LogError("❌ Skill category is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Skill category is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(skillRequest.Level))
            {
                _logger.LogError("❌ Skill level is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Skill level is required"
                }));
                return badResponse;
            }

            // Set TwinID from URL parameter
            skillRequest.TwinID = twinId;

            // Initialize lists if null
            skillRequest.Certifications ??= new List<string>();
            skillRequest.Projects ??= new List<string>();
            skillRequest.LearningPath ??= new List<string>();
            skillRequest.AISuggestions ??= new List<string>();
            skillRequest.Tags ??= new List<string>();

            _logger.LogInformation("🎯 Creating skill: {SkillName} ({Category}) for Twin ID: {TwinId}", 
                skillRequest.Name, skillRequest.Category, twinId);

            // Save skill using SkillsCosmosDB service
            var skillId = await _skillsCosmosDB.SaveSkillAsync(skillRequest);

            if (string.IsNullOrEmpty(skillId))
            {
                _logger.LogError("❌ Failed to save skill to database");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Failed to save skill to database"
                }));
                return errorResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation("✅ Skill created successfully: {SkillName} with ID: {SkillId}", 
                skillRequest.Name, skillId);

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                skill = skillRequest,
                skillId = skillId,
                message = "Skill created successfully"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating skill record");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Update an existing skill record
    /// PUT /api/twins/{twinId}/skills/{skillId}
    /// </summary>
    [Function("UpdateSkill")]
    public async Task<HttpResponseData> UpdateSkill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/skills/{skillId}")] HttpRequestData req,
        string twinId, string skillId)
    {
        _logger.LogInformation("🔄 UpdateSkill function triggered for Skill: {SkillId}, Twin: {TwinId}", 
            skillId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(skillId))
            {
                _logger.LogError("❌ Twin ID and Skill ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Skill ID parameters are required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            // Parse JSON request
            var skillRequest = JsonSerializer.Deserialize<SkillPostRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (skillRequest == null)
            {
                _logger.LogError("❌ Failed to parse skill update request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid skill update data format"
                }));
                return badResponse;
            }

            // Set required IDs
            skillRequest.id = skillId;
            skillRequest.TwinID = twinId;

            // Initialize lists if null
            skillRequest.Certifications ??= new List<string>();
            skillRequest.Projects ??= new List<string>();
            skillRequest.LearningPath ??= new List<string>();
            skillRequest.AISuggestions ??= new List<string>();
            skillRequest.Tags ??= new List<string>();

            _logger.LogInformation("🔄 Updating skill: {SkillId} ({SkillName}) for Twin ID: {TwinId}", 
                skillId, skillRequest.Name, twinId);

            // Update skill using SkillsCosmosDB service
            var updatedSkillId = await _skillsCosmosDB.UpdateSkillAsync(skillRequest);

            if (string.IsNullOrEmpty(updatedSkillId))
            {
                _logger.LogError("❌ Failed to update skill in database - skill may not exist");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Skill not found or failed to update"
                }));
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation("✅ Skill updated successfully: {SkillName} with ID: {SkillId}", 
                skillRequest.Name, updatedSkillId);

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                skill = skillRequest,
                skillId = updatedSkillId,
                message = "Skill updated successfully"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating skill record");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Get all skills for a specific Twin
    /// GET /api/twins/{twinId}/skills
    /// </summary>
    [Function("GetSkillsByTwinId")]
    public async Task<HttpResponseData> GetSkillsByTwinId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/skills")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🔍 GetSkillsByTwinId function triggered for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("🔍 Getting all skills for Twin ID: {TwinId}", twinId);

            // Get skills using SkillsCosmosDB service
            var skills = await _skillsCosmosDB.GetSkillsByTwinIdAsync(twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation("✅ Found {Count} skills for Twin ID: {TwinId}", skills.Count, twinId);

            // Calculate statistics
            var statistics = new
            {
                totalSkills = skills.Count,
                skillsByCategory = skills.GroupBy(s => s.Category).ToDictionary(g => g.Key, g => g.Count()),
                skillsByLevel = skills.GroupBy(s => s.Level).ToDictionary(g => g.Key, g => g.Count()),
                averageExperience = skills.Count > 0 ? skills.Average(s => s.ExperienceYears) : 0,
                validatedSkills = skills.Count(s => s.Validated),
                skillsWithCertifications = skills.Count(s => s.Certifications?.Count > 0),
                skillsWithProjects = skills.Count(s => s.Projects?.Count > 0),
                totalCertifications = skills.Sum(s => s.Certifications?.Count ?? 0),
                totalProjects = skills.Sum(s => s.Projects?.Count ?? 0)
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                skills = skills,
                twinId = twinId,
                totalSkills = skills.Count,
                statistics = statistics,
                message = $"Retrieved {skills.Count} skills for Twin {twinId}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting skills for Twin");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Get a specific skill by ID for a Twin
    /// GET /api/twins/{twinId}/skills/{skillId}
    /// </summary>
    [Function("GetSkillById")]
    public async Task<HttpResponseData> GetSkillById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/skills/{skillId}")] HttpRequestData req,
        string twinId, string skillId)
    {
        _logger.LogInformation("🔍 GetSkillById function triggered for Skill: {SkillId}, Twin: {TwinId}", 
            skillId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(skillId))
            {
                _logger.LogError("❌ Twin ID and Skill ID parameters are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID and Skill ID parameters are required"
                }));
                return badResponse;
            }

            _logger.LogInformation("🔍 Getting skill {SkillId} for Twin ID: {TwinId}", skillId, twinId);

            // Get skill using SkillsCosmosDB service
            var skill = await _skillsCosmosDB.GetSkillByTwinIdAndIdAsync(twinId, skillId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            if (skill == null)
            {
                _logger.LogInformation("⚠️ Skill not found: {SkillId} for Twin ID: {TwinId}", skillId, twinId);
                response = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(response, req);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Skill not found",
                    skillId = skillId,
                    twinId = twinId
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else
            {
                _logger.LogInformation("✅ Skill found: {SkillName}", skill.Name);
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    skill = skill,
                    skillId = skillId,
                    twinId = twinId,
                    message = "Skill retrieved successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting skill by ID");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message
            }));
            
            return errorResponse;
        }
    }

    /// <summary>
    /// Search for learning resources using Bing Grounding with Azure AI Agents
    /// POST /api/twins/{twinId}/skills/search-learning
    /// </summary>
    [Function("SearchSkillLearningResources")]
    public async Task<HttpResponseData> SearchSkillLearningResources(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/skills/search-learning")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🔍 SearchSkillLearningResources function triggered for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("❌ Request body is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Request body with search query is required"
                }));
                return badResponse;
            }

            // Parse JSON request
            var searchRequest = JsonSerializer.Deserialize<SkillLearningSearchRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (searchRequest == null || string.IsNullOrEmpty(searchRequest.SearchQuery))
            {
                _logger.LogError("❌ Failed to parse learning search request or search query is empty");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid search request format or search query is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("🔍 Searching for learning resources: {SearchQuery} for Twin ID: {TwinId}", 
                searchRequest.SearchQuery, twinId);

            // Use SkillsAgent to search for learning resources
            var searchResult = await _skillsAgent.BingSearchForLearningAsync(searchRequest.SearchQuery);

            if (!searchResult.Success)
            {
                _logger.LogError("❌ Failed to search for learning resources: {Error}", searchResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = searchResult.ErrorMessage ?? "Failed to search for learning resources",
                    searchQuery = searchRequest.SearchQuery,
                    twinId = twinId
                }));
                return errorResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            _logger.LogInformation("✅ Learning resources search completed successfully: {SearchQuery}", searchRequest.SearchQuery);

            // Calculate statistics about the found resources
            var learningStats = new
            {
                totalCourses = searchResult.LearningResources?.CursosOnline?.Count ?? 0,
                totalBooks = searchResult.LearningResources?.LibrosRecomendados?.Count ?? 0,
                totalVideos = searchResult.LearningResources?.VideosTutoriales?.Count ?? 0,
                totalWebsites = searchResult.LearningResources?.SitiosEducativos?.Count ?? 0,
                totalTools = searchResult.LearningResources?.HerramientasPractica?.Count ?? 0,
                totalCertifications = searchResult.LearningResources?.Certificaciones?.Count ?? 0,
                totalCommunities = searchResult.LearningResources?.Comunidades?.Count ?? 0,
                learningPathSteps = searchResult.LearningResources?.RutaAprendizaje?.Count ?? 0,
                keywords = searchResult.LearningResources?.PalabrasClave ?? new List<string>()
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                searchQuery = searchRequest.SearchQuery,
                twinId = twinId,
                learningResources = new
                {
                    topic = searchResult.LearningResources?.TopicoAprendizaje ?? searchRequest.SearchQuery,
                    onlineCourses = searchResult.LearningResources?.CursosOnline ?? new List<CursoOnline>(),
                    recommendedBooks = searchResult.LearningResources?.LibrosRecomendados ?? new List<LibroRecomendado>(),
                    videoTutorials = searchResult.LearningResources?.VideosTutoriales ?? new List<VideoTutorial>(),
                    educationalWebsites = searchResult.LearningResources?.SitiosEducativos ?? new List<SitioEducativo>(),
                    practiceTools = searchResult.LearningResources?.HerramientasPractica ?? new List<HerramientaPractica>(),
                    certifications = searchResult.LearningResources?.Certificaciones ?? new List<Certificacion>(),
                    communities = searchResult.LearningResources?.Comunidades ?? new List<ComunidadAprendizaje>(),
                    learningPath = searchResult.LearningResources?.RutaAprendizaje ?? new List<PasoAprendizaje>(),
                    keywords = searchResult.LearningResources?.PalabrasClave ?? new List<string>(),
                    summary = searchResult.LearningResources?.ResumenGeneral ?? "Learning resources found",
                    htmlContent = searchResult.LearningResources?.HtmlCompleto ?? ""
                },
                statistics = learningStats,
                processedAt = searchResult.ProcessedAt,
                message = $"Found comprehensive learning resources for '{searchRequest.SearchQuery}'"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error searching for learning resources");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = ex.Message,
                twinId = twinId
            }));
            
            return errorResponse;
        }
    }

    // ========================================
    // CORS HELPER METHOD
    // ========================================

    /// <summary>
    /// Add CORS headers to response
    /// </summary>
    /// <param name="response">HTTP response</param>
    /// <param name="request">HTTP request</param>
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
/// Request model for skill learning search
/// </summary>
public class SkillLearningSearchRequest
{
    /// <summary>
    /// The search query for learning resources (e.g., "Python programming", "Machine Learning", "UX Design")
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Preferred language for resources (e.g., "Spanish", "English", "Both")
    /// </summary>
    public string? PreferredLanguage { get; set; }

    /// <summary>
    /// Optional: Skill level (e.g., "Beginner", "Intermediate", "Advanced")
    /// </summary>
    public string? SkillLevel { get; set; }

    /// <summary>
    /// Optional: Resource types to focus on (e.g., "Courses", "Books", "Videos", "All")
    /// </summary>
    public string? ResourceType { get; set; }
}