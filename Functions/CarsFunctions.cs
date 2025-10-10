using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinFx.Services;
using TwinFx.Models;
using TwinFx.Agents;
using System.Text;
using System.Text.Json;
using System.Net;
using SystemTextJsonException = System.Text.Json.JsonException; 
namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions para operaciones CRUD de Vehículos
    /// Container: TwinCars, PartitionKey: TwinID
    /// </summary>
    public class CarsFunctions
    {
        private readonly ILogger<CarsFunctions> _logger;
        private readonly CarsCosmosDbService _carsService;
        private readonly CarsAgent _carsAgent;
        private readonly IConfiguration _configuration;

        public CarsFunctions(ILogger<CarsFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Initialize Cars service
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? "",
                Key = configuration["Values:COSMOS_KEY"] ?? "",
                DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            // Create specific logger for CarsCosmosDbService
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var carsLogger = loggerFactory.CreateLogger<CarsCosmosDbService>();

            _carsService = new CarsCosmosDbService(
                carsLogger,
                cosmosOptions,
                configuration);

            // Initialize Cars Agent
            var carsAgentLogger = loggerFactory.CreateLogger<CarsAgent>();
            _carsAgent = new CarsAgent(carsAgentLogger, configuration);
        }

        /// <summary>
        /// Helper method to add CORS headers to HTTP context
        /// </summary>
        private static void AddCorsHeaders(HttpRequest req)
        {
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Max-Age", "86400");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Credentials", "false");
        }

        // ===== OPTIONS HANDLERS FOR CORS =====

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/cars
        /// </summary>
        [Function("CarsOptions")]
        public IActionResult HandleCarsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/cars")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/cars", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/cars/{carId}
        /// </summary>
        [Function("CarsIdOptions")]
        public IActionResult HandleCarsIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/cars/{carId}")] HttpRequest req,
            string twinId, string carId)
        {
            _logger.LogInformation("?? Handling OPTIONS request for /api/twins/{TwinId}/cars/{CarId}", twinId, carId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        // ========================================
        // OPTIONS HANDLERS FOR CORS
        // ========================================

        [Function("UploadCarInsuranceOptions")]
        public async Task<HttpResponseData> HandleUploadCarInsuranceOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/upload-car-insurance/{*filePath}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🚗🛡️ OPTIONS preflight request for upload-car-insurance/{twinId}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        // ===== CRUD OPERATIONS =====

        /// <summary>
        /// Obtener todos los vehículos de un Twin
        /// GET /api/twins/{twinId}/cars
        /// </summary>
        [Function("GetCarsByTwin")]
        public async Task<IActionResult> GetCarsByTwin(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cars")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Getting cars for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                }

                var cars = await _carsService.GetCarsByTwinIdAsync(twinId);

                _logger.LogInformation("? Retrieved {Count} cars for Twin ID: {TwinId}", cars.Count, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = cars,
                    count = cars.Count,
                    message = $"Retrieved {cars.Count} cars for Twin {twinId}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting cars for Twin ID: {TwinId}", twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Obtener vehículo específico por ID
        /// GET /api/twins/{twinId}/cars/{carId}
        /// </summary>
        [Function("GetCarById")]
        public async Task<IActionResult> GetCarById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cars/{carId}")] HttpRequest req,
            string twinId, string carId)
        {
            _logger.LogInformation("?? Getting car by ID: {CarId} for Twin: {TwinId}", carId, twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(carId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID and Car ID parameters are required" });
                }

                var car = await _carsService.GetCarByIdAsync(carId, twinId);

                if (car == null)
                {
                    return new NotFoundObjectResult(new { error = $"Car with ID {carId} not found for Twin {twinId}" });
                }

                // ?? DEBUG: Agregar logging para verificar los valores de precios
                _logger.LogInformation("?? DEBUG - Car financial data: OriginalListPrice={OriginalListPrice}, ListPrice={ListPrice}, CurrentPrice={CurrentPrice}, ActualPaidPrice={ActualPaidPrice}, MonthlyPayment={MonthlyPayment}",
                    car.OriginalListPrice, car.ListPrice, car.CurrentPrice, car.ActualPaidPrice, car.MonthlyPayment);

                _logger.LogInformation("? Retrieved car: {Make} {Model} {Year} for Twin: {TwinId}",
                    car.Make, car.Model, car.Year, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = car,
                    message = $"Car {car.Make} {car.Model} {car.Year} retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting car by ID: {CarId} for Twin: {TwinId}", carId, twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Crear nuevo vehículo
        /// POST /api/twins/{twinId}/cars
        /// </summary>
        [Function("CreateCar")]
        public async Task<IActionResult> CreateCar(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cars")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Creating new car for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                }

                // Leer el cuerpo de la petición
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    return new BadRequestObjectResult(new { error = "Request body is required" });
                }

                CreateCarRequest? carRequest;
                try
                {
                    carRequest = System.Text.Json.JsonSerializer.Deserialize<CreateCarRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (SystemTextJsonException ex)
                {
                    _logger.LogWarning(ex, "?? Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                }

                if (carRequest == null)
                {
                    return new BadRequestObjectResult(new { error = "Car request data is required" });
                }

                // Validar campos básicos requeridos
                if (string.IsNullOrEmpty(carRequest.Make))
                {
                    return new BadRequestObjectResult(new { error = "Make is required" });
                }

                if (string.IsNullOrEmpty(carRequest.Model))
                {
                    return new BadRequestObjectResult(new { error = "Model is required" });
                }

                if (carRequest.Year < 1886 || carRequest.Year > DateTime.Now.Year + 1)
                {
                    return new BadRequestObjectResult(new { error = "Year must be valid" });
                }

                if (string.IsNullOrEmpty(carRequest.LicensePlate))
                {
                    return new BadRequestObjectResult(new { error = "License plate is required" });
                }

                _logger.LogInformation("?? Processing car creation: {Make} {Model} {Year}",
                    carRequest.Make, carRequest.Model, carRequest.Year);

                // Procesar la creación usando el agente
                var result = await _carsAgent.ProcessCreateCarAsync(carRequest, twinId);

                if (!result.Success)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        error = result.Error,
                        validationErrors = result.ValidationErrors,
                        processingTimeMs = result.ProcessingTimeMs
                    });
                }

                _logger.LogInformation("? Car created successfully: {CarId} for Twin: {TwinId}",
                    result.CarData?.Id, twinId);

                return new ObjectResult(new
                {
                    success = true,
                    data = result.CarData,
                    aiResponse = result.AIResponse,
                    validationResults = result.ValidationResults,
                    processingTimeMs = result.ProcessingTimeMs,
                    message = $"Car {result.CarData?.Make} {result.CarData?.Model} {result.CarData?.Year} created successfully"
                })
                {
                    StatusCode = 201
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error creating car for Twin: {TwinId}", twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Actualizar vehículo existente
        /// PUT /api/twins/{twinId}/cars/{carId}
        /// </summary>
        [Function("UpdateCar")]
        public async Task<IActionResult> UpdateCar(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/cars/{carId}")] HttpRequest req,
            string twinId, string carId)
        {
            _logger.LogInformation("?? Updating car: {CarId} for Twin: {TwinId}", carId, twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(carId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID and Car ID parameters are required" });
                }

                // Leer el cuerpo de la petición
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    return new BadRequestObjectResult(new { error = "Request body is required" });
                }

                CarData? carData;
                try
                {
                    carData = System.Text.Json.JsonSerializer.Deserialize<CarData>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (SystemTextJsonException ex)
                {
                    _logger.LogWarning(ex, "?? Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                }

                if (carData == null)
                {
                    return new BadRequestObjectResult(new { error = "Car data is required" });
                }

                // Asegurar que los IDs coincidan
                carData.Id = carId;
                carData.TwinID = twinId;
                carData.UpdatedAt = DateTime.UtcNow;

                var result = _carsAgent.ProcessUpdateCarAsync(carData, twinId);


                _logger.LogInformation("? Car updated successfully: {Make} {Model} {Year} for Twin: {TwinId}",
                    carData.Make, carData.Model, carData.Year, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = carData,
                    message = $"Car {carData.Make} {carData.Model} {carData.Year} updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error updating car: {CarId} for Twin: {TwinId}", carId, twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Eliminar vehículo
        /// DELETE /api/twins/{twinId}/cars/{carId}
        /// </summary>
        [Function("DeleteCar")]
        public async Task<IActionResult> DeleteCar(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/cars/{carId}")] HttpRequest req,
            string twinId, string carId)
        {
            _logger.LogInformation("??? Deleting car: {CarId} for Twin: {TwinId}", carId, twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(carId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID and Car ID parameters are required" });
                }

                // Primero obtener el vehículo para logging
                var existingCar = await _carsService.GetCarByIdAsync(carId, twinId);

                var success = await _carsService.DeleteCarAsync(carId, twinId);

                if (!success)
                {
                    return new ObjectResult(new { error = "Failed to delete car" })
                    {
                        StatusCode = 500
                    };
                }

                _logger.LogInformation("? Car deleted successfully: {CarId} for Twin: {TwinId}", carId, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    message = existingCar != null
                        ? $"Car {existingCar.Make} {existingCar.Model} {existingCar.Year} deleted successfully"
                        : $"Car {carId} deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error deleting car: {CarId} for Twin: {TwinId}", carId, twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Filtrar vehículos with parámetros de consulta
        /// GET /api/twins/{twinId}/cars/filter
        /// </summary>
        [Function("FilterCars")]
        public async Task<IActionResult> FilterCars(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cars/filter")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Filtering cars for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                }

                // Construir parámetros de consulta desde query string
                var query = new CarQuery
                {
                    Make = req.Query["make"].FirstOrDefault(),
                    Model = req.Query["model"].FirstOrDefault(),
                    BodyStyle = req.Query["bodyStyle"].FirstOrDefault(),
                    FuelType = req.Query["fuelType"].FirstOrDefault(),
                    Condition = req.Query["condition"].FirstOrDefault(),
                    Estado = req.Query["estado"].FirstOrDefault(),
                    SortBy = req.Query["sortBy"].FirstOrDefault() ?? "createdAt",
                    SortDirection = req.Query["sortDirection"].FirstOrDefault() ?? "DESC"
                };

                // Parsear parámetros numéricos
                if (int.TryParse(req.Query["yearMin"].FirstOrDefault(), out var yearMin))
                    query.YearMin = yearMin;

                if (int.TryParse(req.Query["yearMax"].FirstOrDefault(), out var yearMax))
                    query.YearMax = yearMax;

                if (decimal.TryParse(req.Query["priceMin"].FirstOrDefault(), out var priceMin))
                    query.PriceMin = priceMin;

                if (decimal.TryParse(req.Query["priceMax"].FirstOrDefault(), out var priceMax))
                    query.PriceMax = priceMax;

                if (long.TryParse(req.Query["mileageMax"].FirstOrDefault(), out var mileageMax))
                    query.MileageMax = mileageMax;

                var cars = await _carsService.GetFilteredCarsAsync(twinId, query);

                _logger.LogInformation("? Filtered cars: found {Count} results for Twin ID: {TwinId}",
                    cars.Count, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = cars,
                    count = cars.Count,
                    filters = query,
                    message = $"Found {cars.Count} cars matching filters for Twin {twinId}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error filtering cars for Twin ID: {TwinId}", twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Obtener estadísticas de vehículos
        /// GET /api/twins/{twinId}/cars/statistics
        /// </summary>
        [Function("GetCarStatistics")]
        public async Task<IActionResult> GetCarStatistics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/cars/statistics")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("?? Getting car statistics for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                }

                var statistics = await _carsService.GetCarStatisticsAsync(twinId);

                _logger.LogInformation("? Car statistics calculated for Twin ID: {TwinId} - {TotalCars} cars, ${TotalValue:N0} total value",
                    twinId, statistics.TotalCars, statistics.TotalValue);

                return new OkObjectResult(new
                {
                    success = true,
                    data = statistics,
                    message = $"Statistics calculated for {statistics.TotalCars} cars"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting car statistics for Twin ID: {TwinId}", twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Crear múltiples vehículos en lote
        /// POST /api/twins/{twinId}/cars/batch
        /// </summary>
        [Function("CreateCarsBatch")]
        public async Task<IActionResult> CreateCarsBatch(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/cars/batch")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("???? Creating cars batch for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                }

                // Leer el cuerpo de la petición
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    return new BadRequestObjectResult(new { error = "Request body is required" });
                }

                List<CreateCarRequest>? carRequests;
                try
                {
                    carRequests = System.Text.Json.JsonSerializer.Deserialize<List<CreateCarRequest>>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (SystemTextJsonException ex)
                {
                    _logger.LogWarning(ex, "?? Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                }

                if (carRequests == null || carRequests.Count == 0)
                {
                    return new BadRequestObjectResult(new { error = "At least one car request is required" });
                }

                var results = new List<object>();
                var successCount = 0;
                var errorCount = 0;

                foreach (var carRequest in carRequests)
                {
                    try
                    {
                        // Validar campos básicos
                        if (string.IsNullOrEmpty(carRequest.Make) || string.IsNullOrEmpty(carRequest.Model) ||
                            carRequest.Year < 1886 || carRequest.Year > DateTime.Now.Year + 1 ||
                            string.IsNullOrEmpty(carRequest.LicensePlate))
                        {
                            results.Add(new
                            {
                                success = false,
                                error = "Invalid car data: Make, Model, Year, and License Plate are required",
                                car = $"{carRequest.Make} {carRequest.Model} {carRequest.Year}"
                            });
                            errorCount++;
                            continue;
                        }

                        var result = await _carsAgent.ProcessCreateCarAsync(carRequest, twinId);

                        if (result.Success)
                        {
                            results.Add(new
                            {
                                success = true,
                                carId = result.CarData?.Id,
                                car = $"{result.CarData?.Make} {result.CarData?.Model} {result.CarData?.Year}"
                            });
                            successCount++;
                        }
                        else
                        {
                            results.Add(new
                            {
                                success = false,
                                error = result.Error,
                                car = $"{carRequest.Make} {carRequest.Model} {carRequest.Year}"
                            });
                            errorCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "? Error processing car in batch: {Make} {Model}", carRequest.Make, carRequest.Model);
                        results.Add(new
                        {
                            success = false,
                            error = ex.Message,
                            car = $"{carRequest.Make} {carRequest.Model} {carRequest.Year}"
                        });
                        errorCount++;
                    }
                }

                _logger.LogInformation("? Batch creation completed: {SuccessCount} successful, {ErrorCount} errors",
                    successCount, errorCount);

                return new OkObjectResult(new
                {
                    success = true,
                    results = results,
                    summary = new
                    {
                        totalRequests = carRequests.Count,
                        successCount = successCount,
                        errorCount = errorCount
                    },
                    message = $"Batch creation completed: {successCount} successful, {errorCount} errors"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in cars batch creation for Twin: {TwinId}", twinId);
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        // ========================================
        // UPLOAD CAR INSURANCE FUNCTION
        // ========================================

        [Function("UploadCarInsurance")]
        public async Task<HttpResponseData> UploadCarInsurance(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/{carId}/upload-car-insurance/{*filePath}")] HttpRequestData req,
            string twinId,
            string carId,
            string filePath)
        {
            _logger.LogInformation("🚗🛡️ UploadCarInsurance function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    return await CreateCarInsuranceErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                if (string.IsNullOrEmpty(carId))
                {
                    _logger.LogError("❌ Car ID parameter is required");
                    return await CreateCarInsuranceErrorResponse(req, "Car ID parameter is required", HttpStatusCode.BadRequest);
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    return await CreateCarInsuranceErrorResponse(req, "File path is required in the URL. Use format: /twins/{twinId}/{carId}/upload-car-insurance/{path}", HttpStatusCode.BadRequest);
                }

                var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
                if (contentTypeHeader.Key == null || !contentTypeHeader.Value.Any(v => v.Contains("multipart/form-data")))
                {
                    return await CreateCarInsuranceErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
                }

                var contentType = contentTypeHeader.Value.FirstOrDefault() ?? "";
                var boundary = GetBoundary(contentType);
                if (string.IsNullOrEmpty(boundary))
                {
                    return await CreateCarInsuranceErrorResponse(req, "Invalid boundary in multipart/form-data", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation($"🚗🛡️ Processing car insurance document upload for Twin ID: {twinId}, Car ID: {carId}");
                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                var bodyBytes = memoryStream.ToArray();
                _logger.LogInformation($"📄 Received multipart data: {bodyBytes.Length} bytes");

                var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                var documentPart = parts.FirstOrDefault(p => p.Name == "document" || p.Name == "file" || p.Name == "pdf");
                if (documentPart == null || documentPart.Data == null || documentPart.Data.Length == 0)
                {
                    return await CreateCarInsuranceErrorResponse(req, "No document file data found in request. Expected field name: 'document', 'file', or 'pdf'", HttpStatusCode.BadRequest);
                }

                string fileName = documentPart.FileName ?? "car_insurance_document.pdf";
                var documentBytes = documentPart.Data;
                var completePath = filePath.Trim();

                _logger.LogInformation($"📂 Using path from URL: {completePath}");

                // Extraer directorio del path completo
                var directory = Path.GetDirectoryName(completePath)?.Replace("\\", "/") ?? "";
                if (string.IsNullOrEmpty(directory))
                {
                    directory = filePath;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    return await CreateCarInsuranceErrorResponse(req, "Invalid path: filename cannot be extracted from the provided URL path", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation($"📂 Final upload details: Directory='{directory}', FileName='{fileName}', CompletePath='{completePath}'");
                _logger.LogInformation($"📏 Document size: {documentBytes.Length} bytes");

                // Validar que sea un archivo PDF
                if (!IsValidPdfFile(fileName, documentBytes))
                {
                    return await CreateCarInsuranceErrorResponse(req, "Invalid document format. Only PDF files are supported for car insurance documents", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation($"📤 STEP 1: Uploading document to DataLake...");
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var mimeType = "application/pdf";

                directory = filePath;
                using var documentStream = new MemoryStream(documentBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(),
                    directory,
                    fileName,
                    documentStream,
                    mimeType);

                if (!uploadSuccess)
                {
                    _logger.LogError("❌ Failed to upload document to DataLake");
                    return await CreateCarInsuranceErrorResponse(req, "Failed to upload document to storage", HttpStatusCode.InternalServerError);
                }

                _logger.LogInformation("✅ Document uploaded successfully to DataLake");

                // PASO 2: Procesar el documento con AI usando CarsAgent
                _logger.LogInformation($"🤖 STEP 2: Processing document with AI CarInsurance analysis...");

                try
                {
                    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var carsAgentLogger = loggerFactory.CreateLogger<TwinFx.Agents.CarsAgent>();
                    var carsAgent = new TwinFx.Agents.CarsAgent(carsAgentLogger, _configuration);

                    // PASO 2.2: Llamar al método AiCarInsurance pasando el carId
                    var aiAnalysisResult = await carsAgent.AiCarInsurance(twinId, directory, fileName, carId);

                    _logger.LogInformation("✅ AI analysis completed successfully");

                    // Generar SAS URL para el documento
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    AddCorsHeaders(response, req);
                    var responseData = new CarsDocumentUploadResult
                    {
                        Success = true,
                        Message = "Car insurance document uploaded and analyzed successfully",
                        TwinId = twinId,
                        CarId = carId,
                        FilePath = completePath,
                        FileName = fileName,
                        Directory = directory,
                        ContainerName = twinId.ToLowerInvariant(),
                        DocumentUrl = sasUrl,
                        FileSize = documentBytes.Length,
                        MimeType = mimeType,
                        UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                        AiAnalysisResult = aiAnalysisResult
                    };

                    await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));

                    _logger.LogInformation($"✅ Car insurance upload and analysis completed successfully in {responseData.ProcessingTimeSeconds:F2} seconds");
                    return response;
                }
                catch (Exception aiEx)
                {
                    _logger.LogWarning(aiEx, "⚠️ Document uploaded successfully but AI analysis failed");

                    // Aún así devolver éxito de upload pero con error de AI
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(completePath, TimeSpan.FromHours(24));

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    AddCorsHeaders(response, req);
                    var responseData = new CarsDocumentUploadResult
                    {
                        Success = true,
                        Message = "Document uploaded successfully but AI analysis failed",
                        TwinId = twinId,
                        CarId = carId,
                        FilePath = completePath,
                        FileName = fileName,
                        Directory = directory,
                        ContainerName = twinId.ToLowerInvariant(),
                        DocumentUrl = sasUrl,
                        FileSize = documentBytes.Length,
                        MimeType = mimeType,
                        UploadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds,
                        AiAnalysisResult = $"{{\"success\": false, \"errorMessage\": \"{aiEx.Message}\"}}"
                    };

                    await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));

                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in car insurance upload");
                return await CreateCarInsuranceErrorResponse(req, $"Upload failed: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        // ========================================
        // HELPER METHODS FOR CAR INSURANCE
        // ========================================

        private async Task<HttpResponseData> CreateCarInsuranceErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new CarsDocumentUploadResult
            {
                Success = false,
                ErrorMessage = errorMessage
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return response;
        }

        // ========================================
        // HELPER METHODS FOR MULTIPART DATA AND PDF VALIDATION
        // ========================================

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Value?.FirstOrDefault();

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

        private async Task<List<MultipartFormDataPart>> ParseMultipartDataAsync(byte[] data, string boundary)
        {
            var parts = new List<MultipartFormDataPart>();
            var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);

            var position = 0;
            var boundaryIndex = FindBytes(data, position, boundaryBytes);
            if (boundaryIndex == -1) return parts;

            position = boundaryIndex + boundaryBytes.Length;
            while (position < data.Length)
            {
                if (position + 1 < data.Length && data[position] == '\r' && data[position + 1] == '\n')
                    position += 2;

                var headersEndBytes = Encoding.UTF8.GetBytes("\r\n\r\n");
                var headersEnd = FindBytes(data, position, headersEndBytes);
                if (headersEnd == -1) break;

                var headersLength = headersEnd - position;
                var headersBytes = new byte[headersLength];
                Array.Copy(data, position, headersBytes, 0, headersLength);
                var headers = Encoding.UTF8.GetString(headersBytes);
                position = headersEnd + 4;

                var nextBoundaryIndex = FindBytes(data, position, boundaryBytes);
                if (nextBoundaryIndex == -1) break;

                var contentLength = nextBoundaryIndex - position - 2;
                if (contentLength > 0)
                {
                    var contentBytes = new byte[contentLength];
                    Array.Copy(data, position, contentBytes, 0, contentLength);
                    var part = ParseMultipartPart(headers, contentBytes);
                    if (part != null)
                    {
                        parts.Add(part);
                    }
                }
                position = nextBoundaryIndex + boundaryBytes.Length;
                if (position < data.Length && data[position] == '-' && position + 1 < data.Length && data[position + 1] == '-')
                    break;
            }
            return parts;
        }

        private static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(' ');
            var element = elements.FirstOrDefault(e => e.StartsWith("boundary="));
            if (element != null)
            {
                return element.Substring("boundary=".Length).Trim('"');
            }
            return "";
        }

        private static int FindBytes(byte[] data, int startPosition, byte[] pattern)
        {
            for (int i = startPosition; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private MultipartFormDataPart? ParseMultipartPart(string headers, byte[] content)
        {
            var lines = headers.Split('\n');
            string? name = null;
            string? fileName = null;
            string? contentType = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                {
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"name=""([^""]+)""");
                    if (nameMatch.Success)
                        name = nameMatch.Groups[1].Value;

                    var filenameMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"filename=""([^""]+)""");
                    if (filenameMatch.Success)
                        fileName = filenameMatch.Groups[1].Value;
                }
                else if (trimmed.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = trimmed.Substring("Content-Type:".Length).Trim();
                }
            }
            if (string.IsNullOrEmpty(name)) return null;
            return new MultipartFormDataPart
            {
                Name = name,
                FileName = fileName,
                ContentType = contentType,
                Data = content,
                StringValue = content.Length > 0 && IsTextContent(contentType) ? Encoding.UTF8.GetString(content) : null
            };
        }

        private static bool IsValidPdfFile(string fileName, byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return false;

            // Verificar extensión del archivo
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension != ".pdf")
                return false;

            // Verificar magic numbers para PDF (%PDF)
            if (fileBytes.Length >= 4)
            {
                // PDF files start with %PDF
                if (fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && fileBytes[2] == 0x44 && fileBytes[3] == 0x46)
                    return true;
            }

            return false;
        }

        private static bool IsTextContent(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return true;
            return contentType.StartsWith("text/") ||
                   contentType.Contains("application/x-www-form-urlencoded") ||
                   contentType.Contains("application/json");
        }
    }

    // ========================================
    // RESPONSE MODELS FOR CAR DOCUMENTS
    // ========================================

    public class CarsDocumentUploadResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string CarId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string DocumentUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string UploadDate { get; set; } = string.Empty;
        public double ProcessingTimeSeconds { get; set; }
        public string AiAnalysisResult { get; set; } = string.Empty;
    }
}