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
    public class CursosAiBuilderFunctionscs
    {
        private readonly ILogger<CursosAiBuilderFunctionscs> _logger;
        private readonly IConfiguration _configuration;

        public CursosAiBuilderFunctionscs(ILogger<CursosAiBuilderFunctionscs> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [Function("BuildCurso")]
        public async Task<IActionResult> BuildCurso(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cursos/agent/build")] HttpRequestData req,
            string twinId)
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
                var aiResult = await builder.BuildCursoAsync(buildData, twinId);

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
}
