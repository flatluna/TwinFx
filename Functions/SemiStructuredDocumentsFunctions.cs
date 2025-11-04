using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Services;
using TwinFx.Models;
using TwinFxTests.Services;
using TwinAgentsLibrary.Models;
using static TwinAgentsLibrary.Models.SemistructuredDocument;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions for semi-structured document analysis
    /// Provides AI-powered invoice analysis using Azure AI Agents with Code Interpreter
    /// </summary>
    public class SemiStructuredDocumentsFunctions
    {
        private readonly ILogger<SemiStructuredDocumentsFunctions> _logger;
        private readonly IConfiguration _configuration;

        public SemiStructuredDocumentsFunctions(ILogger<SemiStructuredDocumentsFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // ========================================
        // OPTIONS HANDLERS FOR CORS
        // ========================================

        [Function("AiInvoicesAnalysisOptions")]
        public async Task<HttpResponseData> HandleAiInvoicesAnalysisOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "ai-invoices-analysis/{twinId}/{vendorName}")] HttpRequestData req,
            string twinId,
            string vendorName)
        {
            _logger.LogInformation("🤖 OPTIONS preflight request for ai-invoices-analysis/{TwinId}/{VendorName}", twinId, vendorName);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SearchSemistructuredDocumentsOptions")]
        public async Task<HttpResponseData> HandleSearchSemistructuredDocumentsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "search-semistructured-documents/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔍 OPTIONS preflight request for search-semistructured-documents/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        // ========================================
        // AI INVOICE ANALYSIS ENDPOINTS
        // ========================================

        /// <summary>
        /// Analyze invoices from a specific vendor using Azure AI Agent with Code Interpreter
        /// POST /api/ai-invoices-analysis/{twinId}/{vendorName}
        /// Body: { "question": "¿Cuáles son los servicios más costosos de AT&T y crear un histograma?", "fileID": "optional-existing-file-id" }
        /// </summary>
        [Function("AiInvoicesAnalysis")]
        public async Task<HttpResponseData> AiInvoicesAnalysis(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai-invoices-analysis/{twinId}/{vendorName}")] HttpRequestData req,
            string twinId,
            string vendorName)
        {
            _logger.LogInformation("🤖 AiInvoicesAnalysis function triggered for TwinId: {TwinId}, VendorName: {VendorName}",
                twinId, vendorName);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate parameters
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

                if (string.IsNullOrEmpty(vendorName))
                {
                    _logger.LogError("❌ Vendor name parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Vendor name parameter is required"
                    }));
                    return badResponse;
                }

                // Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📄 Request body length: {Length} characters", requestBody.Length);

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Request body with question is required",
                        ExpectedFormat = new
                        {
                            question = "¿Cuáles son los servicios más costosos y crear un histograma?",
                            fileID = "optional-existing-file-id-or-null"
                        }
                    }));
                    return badResponse;
                }

                // Parse JSON request
                AnalysisRequest? analysisRequest;
                try
                {
                    analysisRequest = JsonSerializer.Deserialize<AnalysisRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "❌ Invalid JSON format in request body");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Invalid JSON format in request body",
                        Details = jsonEx.Message,
                        ExpectedFormat = new
                        {
                            question = "¿Cuáles son los servicios más costosos y crear un histograma?",
                            fileID = "optional-existing-file-id-or-null"
                        }
                    }));
                    return badResponse;
                }

                if (analysisRequest == null || string.IsNullOrEmpty(analysisRequest.Question))
                {
                    _logger.LogError("❌ Question parameter is required in request body");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Question parameter is required in request body",
                        ExpectedFormat = new
                        {
                            question = "¿Cuáles son los servicios más costosos y crear un histograma?",
                            fileID = "optional-existing-file-id-or-null"
                        }
                    }));
                    return badResponse;
                }

                // Decode URL-encoded vendor name
                var decodedVendorName = Uri.UnescapeDataString(vendorName);

                // Extract fileID from request (optional parameter)
                var fileID = analysisRequest.FileID ?? "null";

                _logger.LogInformation("🤖 Starting AI invoice analysis for TwinId: {TwinId}, VendorName: {VendorName}, Question: {Question}, FileID: {FileID}",
                    twinId, decodedVendorName, analysisRequest.Question, fileID);

                // Create InvoicesAI service instance
                var invoicesAI = CreateInvoicesAIService();

                // Call the AiInvoicesAgent method with FileID parameter
                var analysisResult = await invoicesAI.AiInvoicesAgent(twinId, decodedVendorName, analysisRequest.Question, fileID);

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                _logger.LogInformation("✅ AI invoice analysis completed successfully in {ProcessingTime:F2} seconds",
                    processingTime.TotalSeconds);

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    VendorName = decodedVendorName,
                    Question = analysisRequest.Question,
                    FileID = analysisResult.FileID, // Return the actual FileID used (new or existing)
                    AnalysisResult = analysisResult,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    ProcessedAt = DateTime.UtcNow,
                    Message = "AI invoice analysis completed successfully using Azure AI Agent with Code Interpreter",
                    Features = new
                    {
                        AzureAIAgent = "Code Interpreter enabled",
                        Languages = "Python, pandas, matplotlib",
                        Capabilities = "Data analysis, visualizations, statistical insights",
                        Scope = $"Invoice LineItems analysis for {decodedVendorName}",
                        FileOptimization = fileID == "null" ? "New file uploaded" : "Existing file reused"
                    },
                    Note = "This analysis was performed using Azure AI Agent with Python Code Interpreter for advanced invoice data analysis, visualizations, and financial insights."
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
                _logger.LogError(ex, "❌ Error in AI invoice analysis after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    VendorName = vendorName,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    ProcessedAt = DateTime.UtcNow,
                    Note = "Error occurred during AI invoice analysis. Please check the parameters and try again."
                }));

                return errorResponse;
            }
        }

        // ========================================
        // SEMISTRUCTURED DOCUMENTS SEARCH ENDPOINTS
        // ========================================

        /// <summary>
        /// Search semistructured documents using hybrid search with vector, semantic, and full-text capabilities
        /// POST /api/search-semistructured-documents/{twinId}
        /// Body: { "query": "find invoices from Microsoft", "documentType": "Invoice", "size": 20, "page": 1 }
        /// </summary>
        [Function("SearchSemistructuredDocuments")]
        public async Task<HttpResponseData> SearchSemistructuredDocuments(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "search-semistructured-documents/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔍 SearchSemistructuredDocuments function triggered for TwinId: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate parameters
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

                // Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📄 Request body length: {Length} characters", requestBody.Length);

                // Parse JSON request (optional body)
                SemistructuredSearchRequest? searchRequest = null;
                if (!string.IsNullOrEmpty(requestBody))
                {
                    try
                    {
                        searchRequest = JsonSerializer.Deserialize<SemistructuredSearchRequest>(requestBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "❌ Invalid JSON format in request body");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                        {
                            Success = false,
                            ErrorMessage = "Invalid JSON format in request body",
                            Details = jsonEx.Message,
                            ExpectedFormat = new
                            {
                                query = "find invoices from Microsoft",
                                documentType = "Invoice",
                                size = 20,
                                page = 1
                            }
                        }));
                        return badResponse;
                    }
                }

                // Set defaults if no request body provided
                searchRequest ??= new SemistructuredSearchRequest();

                _logger.LogInformation("🔍 Starting semistructured documents search for TwinId: {TwinId}, Query: {Query}, DocumentType: {DocumentType}",
                    twinId, searchRequest.Query ?? "null", searchRequest.DocumentType ?? "all");

                // Create Semistructured_Index service instance
                var semistructuredIndex = CreateSemistructuredIndexService();

                // Configure search options
                var searchOptions = new SemistructuredSearchOptions
                {
                    TwinId = twinId,
                    DocumentType = searchRequest.DocumentType,
                    Size = Math.Max(1, Math.Min(searchRequest.Size, 100)), // Limit between 1 and 100
                    Page = Math.Max(1, searchRequest.Page),
                    UseVectorSearch = searchRequest.UseVectorSearch,
                    UseSemanticSearch = searchRequest.UseSemanticSearch,
                    UseHybridSearch = searchRequest.UseHybridSearch
                };

                // Perform the search
                var searchResults = await semistructuredIndex.SearchDocumentAsync(searchRequest.Query, searchOptions);

                // Convert results to list for response processing
                var resultsList = searchResults.ToList();

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                _logger.LogInformation("✅ Semistructured documents search completed successfully in {ProcessingTime:F2} seconds",
                    processingTime.TotalSeconds);

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    Query = searchRequest.Query,
                    Documents = resultsList,
                   
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
                _logger.LogError(ex, "❌ Error in semistructured documents search after {ProcessingTime:F2} seconds", 
                    processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    ProcessedAt = DateTime.UtcNow,
                    Note = "Error occurred during semistructured documents search. Please check the parameters and try again."
                }));

                return errorResponse;
            }
        }

        // ========================================
        // HELPER METHODS
        // ========================================

        /// <summary>
        /// Create InvoicesAI service instance with proper configuration
        /// </summary>
        private InvoicesAI CreateInvoicesAIService()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var invoicesAILogger = loggerFactory.CreateLogger<InvoicesAI>();
            return new InvoicesAI(invoicesAILogger, _configuration);
        }

        /// <summary>
        /// Create Semistructured_Index service instance with proper configuration
        /// </summary>
        private Semistructured_Index CreateSemistructuredIndexService()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var semistructuredIndexLogger = loggerFactory.CreateLogger<Semistructured_Index>();
            return new Semistructured_Index(semistructuredIndexLogger, _configuration);
        }

        /// <summary>
        /// Add CORS headers to response
        /// </summary>
        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            // Get origin from request headers
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            // Allow specific origins for development
            var allowedOrigins = new[] {
                "http://localhost:5173",
                "http://localhost:3000",
                "http://127.0.0.1:5173",
                "http://127.0.0.1:3000",
                "https://localhost:7071"  // Azure Functions local development
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

    /// <summary>
    /// Request model for AI invoice analysis
    /// </summary>
    public class AnalysisRequest
    {
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// Optional FileID for reusing existing uploaded files.
        /// If null, empty, or "null", a new file will be uploaded.
        /// </summary>
        public string? FileID { get; set; } = "null";
    }

    /// <summary>
    /// Request model for semistructured documents search
    /// </summary>
    public class SemistructuredSearchRequest
    {
        /// <summary>
        /// Search query text (optional)
        /// </summary>
        public string? Query { get; set; }

        /// <summary>
        /// Filter by document type (e.g., "Invoice", "Contract", "Report")
        /// </summary>
        public string? DocumentType { get; set; }

        /// <summary>
        /// Maximum number of results to return (default: 10, max: 100)
        /// </summary>
        public int Size { get; set; } = 10;

        /// <summary>
        /// Page number for pagination (1-based, default: 1)
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Whether to use vector search (default: true)
        /// </summary>
        public bool UseVectorSearch { get; set; } = true;

        /// <summary>
        /// Whether to use semantic search (default: true)
        /// </summary>
        public bool UseSemanticSearch { get; set; } = true;

        /// <summary>
        /// Whether to use hybrid search combining vector, semantic, and full-text (default: true)
        /// </summary>
        public bool UseHybridSearch { get; set; } = true;
    }
}
