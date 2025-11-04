using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Services;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions para la gestión de índices y búsqueda de análisis comprensivos del diario
    /// Proporciona endpoints RESTful para acceder a los análisis de Azure AI Search:
    /// - Obtener análisis comprensivo por DiaryEntryId y TwinId
    /// - Buscar análisis comprensivos con filtros avanzados
    /// - Gestionar índices de Azure AI Search
    /// 
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class DiaryFunctionsIndex
    {
        private readonly ILogger<DiaryFunctionsIndex> _logger;
        private readonly DiarySearchIndex _diarySearchIndex;
        private readonly IConfiguration _configuration;

        public DiaryFunctionsIndex(ILogger<DiaryFunctionsIndex> logger, 
            DiarySearchIndex diarySearchIndex, 
            IConfiguration configuration)
        {
            _logger = logger;
            _diarySearchIndex = diarySearchIndex;
            _configuration = configuration;
        }

        // ========================================  
        // OPTIONS HANDLERS FOR CORS  
        // ========================================  
        [Function("DiaryAnalysisOptions")]
        public async Task<HttpResponseData> HandleDiaryAnalysisOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/{entryId}/analysis")] HttpRequestData req, string twinId, string entryId)
        {
            return await HandleOptions(req, twinId, "diary/{entryId}/analysis");
        }

        [Function("DiarySearchAnalysisOptions")]
        public async Task<HttpResponseData> HandleDiarySearchAnalysisOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/search/analysis")] HttpRequestData req, string twinId)
        {
            return await HandleOptions(req, twinId, "diary/search/analysis");
        }

        [Function("DiaryIndexManagementOptions")]
        public async Task<HttpResponseData> HandleDiaryIndexManagementOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/index")] HttpRequestData req, string twinId)
        {
            return await HandleOptions(req, twinId, "diary/index");
        }

        private async Task<HttpResponseData> HandleOptions(HttpRequestData req, string twinId, string route)
        {
            _logger.LogInformation("🔍 OPTIONS preflight request for twins/{TwinId}/{Route}", twinId, route);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        // ========================================  
        
        /// Buscar análisis comprensivos con filtros avanzados
        /// GET /api/twins/{twinId}/diary/search/analysis?q={searchTerm}&dateFrom={date}&dateTo={date}&successfulOnly={bool}
        /// </summary>
        [Function("SearchDiaryAnalysis")]
        public async Task<HttpResponseData> SearchDiaryAnalysis([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/diary/search/analysis")] HttpRequestData req, string twinId)
        {
            _logger.LogInformation("🔍 SearchDiaryAnalysis function triggered for TwinId: {TwinId}", twinId);
            
            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, "TwinId parameter is required", HttpStatusCode.BadRequest);
                }

                // Parse query parameters
                var query = ParseDiarySearchQuery(req, twinId);

                // Perform the search
                var searchResult = await _diarySearchIndex.SearchDiaryAnalysisAsync(query);

                var response = req.CreateResponse(searchResult.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                if (searchResult.Success)
                {
                    var responseData = new
                    {
                        success = true,
                        twinId = twinId,
                        results = searchResult.Results.Select(r => new
                        {
                            id = r.Id,
                            diaryEntryId = r.DiaryEntryId,
                            executiveSummary = r.ExecutiveSummary,
                            success = r.Success,
                            processingTimeMs = r.ProcessingTimeMs,
                            analyzedAt = r.AnalyzedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                            errorMessage = r.ErrorMessage,
                            searchScore = r.SearchScore,
                            highlights = r.Highlights
                        }),
                        pagination = new
                        {
                            totalCount = searchResult.TotalCount,
                            page = searchResult.Page,
                            pageSize = searchResult.PageSize
                        },
                        searchInfo = new
                        {
                            query = searchResult.SearchQuery,
                            searchType = searchResult.SearchType
                        }
                    };

                    await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    }));

                    _logger.LogInformation("✅ Diary analysis search completed: {Count} results found for TwinId: {TwinId}", 
                        searchResult.Results.Count, twinId);
                }
                else
                {
                    var errorResponse = new
                    {
                        success = false,
                        twinId = twinId,
                        error = searchResult.Error ?? "Search operation failed",
                        message = "Could not perform diary analysis search"
                    };

                    await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                    }));

                    _logger.LogWarning("⚠️ Diary analysis search failed for TwinId: {TwinId}, Error: {Error}", 
                        twinId, searchResult.Error);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching diary analysis for TwinId: {TwinId}", twinId);
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Gestionar índices de Azure AI Search para análisis de diario
        /// GET /api/twins/{twinId}/diary/index - Check index status
        /// POST /api/twins/{twinId}/diary/index - Create or update index
        /// </summary>
        [Function("ManageDiaryIndex")]
        public async Task<HttpResponseData> ManageDiaryIndex([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twins/{twinId}/diary/index")] HttpRequestData req, string twinId)
        {
            _logger.LogInformation("🔧 ManageDiaryIndex function triggered for TwinId: {TwinId}, Method: {Method}", 
                twinId, req.Method);
            
            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, "TwinId parameter is required", HttpStatusCode.BadRequest);
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    // Check index status
                    var isAvailable = _diarySearchIndex.IsAvailable;
                    
                    var statusResponse = new
                    {
                        success = true,
                        twinId = twinId,
                        indexStatus = new
                        {
                            isAvailable = isAvailable,
                            indexName = "diary-analysis-index",
                            message = isAvailable 
                                ? "Diary analysis search index is available and ready" 
                                : "Diary analysis search index is not available"
                        }
                    };

                    await response.WriteStringAsync(JsonSerializer.Serialize(statusResponse, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    }));

                    _logger.LogInformation("✅ Index status checked for TwinId: {TwinId}, Available: {Available}", 
                        twinId, isAvailable);
                }
                else if (req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    // Create or update index
                    var indexResult = await _diarySearchIndex.CreateDiaryIndexAsync();
                    
                    response = req.CreateResponse(indexResult.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
                    AddCorsHeaders(response, req);
                    response.Headers.Add("Content-Type", "application/json");

                    var indexResponse = new
                    {
                        success = indexResult.Success,
                        twinId = twinId,
                        indexOperation = new
                        {
                            indexName = indexResult.IndexName,
                            fieldsCount = indexResult.FieldsCount,
                            hasVectorSearch = indexResult.HasVectorSearch,
                            hasSemanticSearch = indexResult.HasSemanticSearch,
                            message = indexResult.Message,
                            error = indexResult.Error
                        }
                    };

                    await response.WriteStringAsync(JsonSerializer.Serialize(indexResponse, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    }));

                    _logger.LogInformation("✅ Index operation completed for TwinId: {TwinId}, Success: {Success}", 
                        twinId, indexResult.Success);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error managing diary index for TwinId: {TwinId}", twinId);
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        // ========================================  
        // HELPER METHODS  
        // ========================================  

        /// <summary>
        /// Parse query parameters for diary analysis search
        /// </summary>
        private SearchQuery ParseDiarySearchQuery(HttpRequestData req, string twinId)
        {
            var query = new SearchQuery
            {
                TwinId = twinId
            };

            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

                // Search text
                if (!string.IsNullOrEmpty(queryParams["q"]) || !string.IsNullOrEmpty(queryParams["search"]))
                {
                    query.SearchText = queryParams["q"] ?? queryParams["search"];
                }

                // Date range filters
                if (DateTime.TryParse(queryParams["dateFrom"], out var dateFrom))
                {
                    query.DateFrom = dateFrom;
                }
                if (DateTime.TryParse(queryParams["dateTo"], out var dateTo))
                {
                    query.DateTo = dateTo;
                }

                // Success filter
                if (bool.TryParse(queryParams["successfulOnly"], out var successfulOnly))
                {
                    query.SuccessfulOnly = successfulOnly;
                }

                // Search type preferences
                if (bool.TryParse(queryParams["useVectorSearch"], out var useVectorSearch))
                {
                    query.UseVectorSearch = useVectorSearch;
                }
                if (bool.TryParse(queryParams["useSemanticSearch"], out var useSemanticSearch))
                {
                    query.UseSemanticSearch = useSemanticSearch;
                }
                if (bool.TryParse(queryParams["useHybridSearch"], out var useHybridSearch))
                {
                    query.UseHybridSearch = useHybridSearch;
                }

                // Pagination
                if (int.TryParse(queryParams["top"], out var top) && top > 0)
                {
                    query.Top = Math.Min(top, 50); // Max 50 items per page
                }
                if (int.TryParse(queryParams["page"], out var page) && page > 0)
                {
                    query.Page = page;
                }

                _logger.LogInformation("🔍 Parsed search query: SearchText='{SearchText}', TwinId='{TwinId}', Page={Page}, Top={Top}", 
                    query.SearchText?.Substring(0, Math.Min(query.SearchText.Length, 50)), 
                    query.TwinId, query.Page, query.Top);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing search query parameters, using defaults");
            }

            return query;
        }

        /// <summary>
        /// Create error response with CORS headers
        /// </summary>
        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                error = errorMessage,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            return response;
        }

        /// <summary>
        /// Add CORS headers to response
        /// </summary>
        private void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            var allowedOrigins = new[] { 
                "http://localhost:5173", 
                "http://localhost:3000", 
                "http://127.0.0.1:5173", 
                "http://127.0.0.1:3000",
                "https://localhost:5173",
                "https://localhost:3000"
            };

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
}