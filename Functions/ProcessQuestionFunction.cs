using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TwinFx.Clients;

namespace TwinFx.Functions;

public class ProcessQuestionFunction
{
    private readonly ILogger<ProcessQuestionFunction> _logger;
    private readonly IConfiguration _configuration;

    public ProcessQuestionFunction(ILogger<ProcessQuestionFunction> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // OPTIONS handler for CORS preflight requests
    [Function("ProcessQuestionOptions")]
    public async Task<HttpResponseData> HandleOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "ProcessQuestion")] HttpRequestData req)
    {
        _logger.LogInformation("?? OPTIONS preflight request for ProcessQuestion");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("ProcessQuestion")]
    public async Task<HttpResponseData> ProcessQuestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ProcessQuestion")] HttpRequestData req)
    {
        _logger.LogInformation("?? ProcessQuestion function triggered");

        try
        {
            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body: {requestBody}");

            // Parse JSON request
            var requestData = JsonSerializer.Deserialize<ProcessQuestionRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (requestData == null)
            {
                _logger.LogError("? Failed to parse request body");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ProcessQuestionResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid request format"
                }));
                return badResponse;
            }

            // Validate required parameters
            if (string.IsNullOrEmpty(requestData.Question))
            {
                _logger.LogError("? Question parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ProcessQuestionResponse
                {
                    Success = false,
                    ErrorMessage = "Question parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(requestData.TwinId))
            {
                _logger.LogError("? TwinId parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ProcessQuestionResponse
                {
                    Success = false,
                    ErrorMessage = "TwinId parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Processing question: '{requestData.Question}' for Twin ID: {requestData.TwinId}");

            // Create a logger for TwinAgentClient
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .BuildServiceProvider();
            var twinAgentLogger = serviceProvider.GetRequiredService<ILogger<TwinAgentClient>>();

            // Create TwinAgentClient and process the question
            var client = new TwinAgentClient(twinAgentLogger, _configuration);
            var result = await client.ProcessHumanQuestionWithIntentionRouting(
                requestData.Question, 
                requestData.TwinId, 
                useChatClient: true);

            _logger.LogInformation($"? Question processed successfully with {result.Length} characters");

            // Create successful response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");
            
            var responseData = new ProcessQuestionResponse
            {
                Success = true,
                Result = result,
                Question = requestData.Question,
                TwinId = requestData.TwinId,
                ProcessedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error processing question");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ProcessQuestionResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            }));
            
            return errorResponse;
        }
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

public class ProcessQuestionRequest
{
    public string Question { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
}

public class ProcessQuestionResponse
{
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Question { get; set; }
    public string? TwinId { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}