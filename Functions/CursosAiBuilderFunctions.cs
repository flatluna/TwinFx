using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TwinFx.Agents;
using TwinFx.Services;
using TwinFx.Models;
using System.Collections.Generic;

namespace TwinFx.Functions
{
    public class CursosAiBuilderFunctions
    {
        private readonly ILogger<CursosAiBuilderFunctions> _logger;
        private readonly IConfiguration _configuration;

        public CursosAiBuilderFunctions(ILogger<CursosAiBuilderFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [Function("BuildCurso")]
        public async Task<IActionResult> BuildCurso(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/{type}/cursos/agent/build")] HttpRequestData req,
            string twinId, string type)
        {
            _logger.LogInformation("🔧 BuildCurso function triggered for Twin ID: {TwinId}", twinId);

            try
            {
                var body = await new System.IO.StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrEmpty(body))
                {
                    var errorResponse = new ObjectResult(new
                    {
                        error = "Invalid body",
                        twinId = twinId
                    })
                    {
                        StatusCode = 500
                    };

                    return errorResponse;
                }

                CursoBuildData? buildData = null;
                try
                {
                    buildData = JsonSerializer.Deserialize<CursoBuildData>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON in BuildCurso request");
                    var errorResponse = new ObjectResult(new
                    {
                        error = ex.Message,
                        twinId = twinId
                    })
                    {
                        StatusCode = 500
                    };

                    return errorResponse;
                }

                if (buildData == null)
                {
                    var errorResponse = new ObjectResult(new
                    {
                        error = "ex.Message",
                        twinId = twinId
                    })
                    {
                        StatusCode = 500
                    };

                    return errorResponse;
                }

                // Map TwinId into buildData (if provided route param)
                buildData.TwinID = twinId;

                var loggerFactoryLocal = LoggerFactory.Create(b => b.AddConsole());
                var agentLogger = loggerFactoryLocal.CreateLogger<TwinFx.Agents.AgenteHomes>();
                var builder = new CursosAiBuilder(agentLogger, _configuration);
                var aiResult = await builder.BuildCursoAsync(buildData, twinId, type);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");
                return new OkObjectResult(new
                {
                    success = true,
                    clase = aiResult,
                    message = "Intelligent search completed successfully"
                });
                
            }
            catch (Exception ex)
            {
                var errorResponse = new ObjectResult(new
                {
                    error = ex.Message,
                    twinId = twinId
                })
                {
                    StatusCode = 500
                };
               
                return errorResponse;
            }
        }

        [Function("GetCursosBuildByTwin")]
        public async Task<IActionResult> GetCursosBuildByTwin(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cursos/agent/build")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔍 GetCursosBuildByTwin triggered for Twin ID: {TwinId}", twinId);

            try
            {
                // Prepare cosmos options from configuration
                var cosmosOptions = Options.Create(new CosmosDbSettings
                {
                    Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? string.Empty,
                    Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? string.Empty,
                    DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
                });

                var loggerFactoryLocal = LoggerFactory.Create(b => b.AddConsole());
                var serviceLogger = loggerFactoryLocal.CreateLogger<CursosAiBuildCosmosDB>();
                var service = new CursosAiBuildCosmosDB(serviceLogger, cosmosOptions);

                var cursos = await service.GetCursosByTwinIdAsync(twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    twinId = twinId,
                    cursos = cursos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cursos for twin {TwinId}", twinId);
                var errorResponse = new ObjectResult(new
                {
                    success = false,
                    error = ex.Message,
                    twinId = twinId
                }) { StatusCode = 500 };
                return errorResponse;
            }
        }

        /// <summary>
        /// Responder pregunta sobre un capítulo específico de un curso AI usando OpenAI
        /// POST /api/twins/{twinId}/cursos/{cursoId}/capitulos/{capituloId}/ask-question
        /// </summary>
        [Function("AnswerCourseCapituloQuestion")]
        public async Task<HttpResponseData> AnswerCourseCapituloQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cursos/{cursoId}/capitulos/{capituloId}/ask-question")] HttpRequestData req,
            string twinId, string cursoId, int capituloId)
        {
            _logger.LogInformation("🤖❓ AnswerCourseCapituloQuestion function triggered for Twin ID: {TwinId}, Course ID: {CursoId}, Chapter ID: {CapituloId}", twinId, cursoId, capituloId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(cursoId) )
                {
                    _logger.LogError("❌ Twin ID, Course ID and Chapter ID parameters are required");
                    return await CreateErrorResponse(req, "Twin ID, Course ID and Chapter ID parameters are required", HttpStatusCode.BadRequest);
                }

                // Leer el cuerpo de la solicitud para obtener la pregunta
                var requestBody = await req.ReadAsStringAsync();
                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is required");
                    return await CreateErrorResponse(req, "Request body is required", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation("🤖 Question request body received: {Length} characters", requestBody.Length);

                // Parsear el JSON del request para obtener la pregunta
                ChapterQuestionRequest? questionRequest;
                try
                {
                    questionRequest = JsonSerializer.Deserialize<ChapterQuestionRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "❌ Invalid JSON format in request body");
                    return await CreateErrorResponse(req, $"Invalid JSON format: {ex.Message}", HttpStatusCode.BadRequest);
                }

                if (questionRequest == null || string.IsNullOrEmpty(questionRequest.Question))
                {
                    _logger.LogError("❌ Question field is required");
                    return await CreateErrorResponse(req, "Question field is required in request body", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation("🤖 Processing chapter question: {Question}", questionRequest.Question.Length > 100 ? 
                    questionRequest.Question.Substring(0, 100) + "..." : questionRequest.Question);

                // Crear instancia del CursosAiBuilder y llamar al método
                var loggerFactoryLocal = LoggerFactory.Create(b => b.AddConsole());
                var agentLogger = loggerFactoryLocal.CreateLogger<TwinFx.Agents.AgenteHomes>();
                var builder = new CursosAiBuilder(agentLogger, _configuration);
                
                var aiResponse = await builder.AnswerCourseCapituloQuestionAsync(cursoId, capituloId, twinId, questionRequest.Question);

                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var successResult = new
                {
                    success = true,
                    twinId = twinId,
                    cursoId = cursoId,
                    capituloId = capituloId,
                    question = questionRequest.Question,
                    answer = aiResponse.Answer,
                    context = aiResponse.Context,
                    processingTimeMs = processingTime,
                    answeredAt = DateTime.UtcNow,
                    message = "Chapter question answered successfully by AI"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(successResult, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true 
                }));

                _logger.LogInformation("✅ Chapter question answered successfully in {ProcessingTime}ms", processingTime);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error answering chapter question for Twin: {TwinId}, Course: {CursoId}, Chapter: {CapituloId}", 
                    twinId, cursoId, capituloId);
                return await CreateErrorResponse(req, ex.Message, HttpStatusCode.InternalServerError);
            }
        }

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");
            var payload = new { success = false, error = errorMessage };
            await response.WriteStringAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return response;
        }

        private void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With");
            response.Headers.Add("Access-Control-Max-Age", "86400");
            response.Headers.Add("Access-Control-Allow-Credentials", "false");
        }
    }

    /// <summary>
    /// Request para hacer una pregunta sobre un capítulo específico
    /// </summary>
    public class ChapterQuestionRequest
    {
        /// <summary>
        /// La pregunta sobre el capítulo
        /// </summary>
        public string Question { get; set; } = string.Empty;
    }
}
