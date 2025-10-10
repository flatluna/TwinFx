using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Agente especializado en gestión inteligente de vehículos del Twin
/// ========================================================================
/// 
/// Este agente utiliza AI para:
/// - Procesamiento inteligente de datos de vehículos
/// - Validación y enriquecimiento de información de automóviles
/// - Generación de recomendaciones basadas en datos del vehículo
/// - Análisis comparativo de vehículos
/// - Solo responde preguntas relacionadas con vehículos del Twin
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class CarsAgent
{
    private readonly ILogger<CarsAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly CarsCosmosDbService _carsService;
    private Kernel? _kernel;

    public CarsAgent(ILogger<CarsAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Inicializar CarsCosmosDbService
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
        {
            Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? configuration["COSMOS_ENDPOINT"] ?? "",
            Key = configuration["Values:COSMOS_KEY"] ?? configuration["COSMOS_KEY"] ?? "",
            DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });
        
        var carsLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CarsCosmosDbService>();
        _carsService = new CarsCosmosDbService(carsLogger, cosmosOptions, configuration);
        
        _logger.LogInformation("🚗 CarsAgent initialized for intelligent vehicle management");
    }

    /// <summary>
    /// Procesa la creación inteligente de un nuevo vehículo con validación y enriquecimiento de datos
    /// </summary>
    /// <param name="carRequest">Datos del vehículo a crear</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Respuesta inteligente con el vehículo creado y recomendaciones</returns>
    public async Task<CarsAIResponse> ProcessCreateCarAsync(CreateCarRequest carRequest, string twinId)
    {
        _logger.LogInformation("🚗 Processing intelligent car creation for Twin ID: {TwinId}", twinId);
        _logger.LogInformation("🚗 Car: {Make} {Model} {Year}", carRequest.Make, carRequest.Model, carRequest.Year);

        var startTime = DateTime.UtcNow;

        try
        {
            // Validar inputs básicos
            if (carRequest == null || string.IsNullOrEmpty(twinId))
            {
                return new CarsAIResponse
                {
                    Success = false,
                    Error = "Car request and TwinId are required",
                    TwinId = twinId,
                    Operation = "CreateCar"
                };
            }

            // PASO 1: Validar y enriquecer datos con AI
            _logger.LogInformation("🔍 Step 1: Validating and enriching car data with AI");
            await InitializeKernelAsync();
            
            var validationResult = await ValidateAndEnrichCarDataAsync(carRequest, twinId);
            
            if (!validationResult.IsValid)
            {
                return new CarsAIResponse
                {
                    Success = false,
                    Error = validationResult.ValidationErrors.FirstOrDefault() ?? "Invalid car data",
                    TwinId = twinId,
                    Operation = "CreateCar",
                    ValidationErrors = validationResult.ValidationErrors
                };
            }

            // PASO 2: Crear CarData usando los datos enriquecidos
            var carData = BuildCarDataFromRequest(validationResult.EnrichedRequest, twinId);

            // PASO 3: Crear el vehículo en Cosmos DB
            var createSuccess = await _carsService.CreateCarAsync(carData);
            
            if (!createSuccess)
            {
                return new CarsAIResponse
                {
                    Success = false,
                    Error = "Failed to create car in database",
                    TwinId = twinId,
                    Operation = "CreateCar"
                };
            }

            // PASO 4: Generar respuesta inteligente con recomendaciones
            var aiResponseJson = await GenerateCreateCarResponseAsync(carData, validationResult, twinId);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // PASO 5: NUEVO - Indexar el análisis completo en CarsSearchIndex
            _logger.LogInformation("🚗 Step 5: Indexing comprehensive car analysis in search index");
            try
            {
                // Crear instancia del CarsSearchIndex
                var carsSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CarsSearchIndex>();
                var carsSearchIndex = new CarsSearchIndex(carsSearchLogger, _configuration);

                // Indexar el análisis completo en el índice de búsqueda
                var indexResult = await carsSearchIndex.IndexCarAnalysisFromAIResponseAsync(
                    aiResponseJson, 
                    carData.Id, 
                    twinId, 
                    processingTimeMs);

                if (indexResult.Success)
                {
                    _logger.LogInformation("✅ Car analysis indexed successfully in search index: DocumentId={DocumentId}", indexResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to index car analysis in search index: {Error}", indexResult.Error);
                    // No fallamos toda la operación por esto, solo logueamos la advertencia
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing car analysis in search index");
                // No fallamos toda la operación por esto
            }

            // Extraer solo el HTML de la respuesta para mostrar al usuario
            var htmlResponse = ExtractHtmlFromResponse(aiResponseJson);

            _logger.LogInformation("✅ Car created successfully: {Id}", carData.Id);

            return new CarsAIResponse
            {
                Success = true,
                TwinId = twinId,
                Operation = "CreateCar",
                CarData = carData,
                AIResponse = htmlResponse,
                ValidationResults = validationResult,
                ProcessingTimeMs = processingTimeMs,
                SearchIndexed = true // Nuevo campo para indicar que fue indexado
            };

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing car creation for Twin: {TwinId}", twinId);
            return new CarsAIResponse
            {
                Success = false,
                Error = ex.Message,
                TwinId = twinId,
                Operation = "CreateCar",
                ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                AIResponse = $"""
                ❌ **Error procesando la creación de tu vehículo**
                
                🔴 **Error:** {ex.Message}
                
                💡 **Sugerencias:**
                • Verifica que todos los campos requeridos estén completos
                • Intenta nuevamente con datos válidos
                • Contacta al soporte técnico si el problema persiste
                """
            };
        }
    }

    public async Task<CarsAIResponse> ProcessUpdateCarAsync(CarData carData, string twinId)
    {
         
        var startTime = DateTime.UtcNow;

        try
        {
           

            // PASO 1: Validar y enriquecer datos con AI
            _logger.LogInformation("🔍 Step 1: Validating and enriching car data with AI");
            await InitializeKernelAsync();

            CarValidationResult validationResult = new CarValidationResult();

            validationResult.IsValid = true;
            validationResult.ValidationErrors = new List<string>();
            

            if (!validationResult.IsValid)
            {
                return new CarsAIResponse
                {
                    Success = false,
                    Error = validationResult.ValidationErrors.FirstOrDefault() ?? "Invalid car data",
                    TwinId = twinId,
                    Operation = "CreateCar",
                    ValidationErrors = validationResult.ValidationErrors
                };
            }

            // PASO 2: Crear CarData usando los datos enriquecidos
          
            // PASO 3: Crear el vehículo en Cosmos DB
            var createSuccess = await _carsService.UpdateCarAsync(carData);

            if (!createSuccess)
            {
                return new CarsAIResponse
                {
                    Success = false,
                    Error = "Failed to create car in database",
                    TwinId = twinId,
                    Operation = "CreateCar"
                };
            }
           
            // PASO 4: Generar respuesta inteligente con recomendaciones
            var aiResponseJson = await GenerateCreateCarResponseAsync(carData, validationResult, twinId);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // PASO 5: NUEVO - Indexar el análisis completo en CarsSearchIndex
            _logger.LogInformation("🚗 Step 5: Indexing comprehensive car analysis in search index");
            try
            {
                // Crear instancia del CarsSearchIndex
                var carsSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CarsSearchIndex>();
                var carsSearchIndex = new CarsSearchIndex(carsSearchLogger, _configuration);

                // Indexar el análisis completo en el índice de búsqueda
                var indexResult = await carsSearchIndex.IndexCarAnalysisFromAIResponseAsync(
                    aiResponseJson,
                    carData.Id,
                    twinId,
                    processingTimeMs);

                if (indexResult.Success)
                {
                    _logger.LogInformation("✅ Car analysis indexed successfully in search index: DocumentId={DocumentId}", indexResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to index car analysis in search index: {Error}", indexResult.Error);
                    // No fallamos toda la operación por esto, solo logueamos la advertencia
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing car analysis in search index");
                // No fallamos toda la operación por esto
            }

            // Extraer solo el HTML de la respuesta para mostrar al usuario
            var htmlResponse = ExtractHtmlFromResponse(aiResponseJson);

            _logger.LogInformation("✅ Car created successfully: {Id}", carData.Id);

            return new CarsAIResponse
            {
                Success = true,
                TwinId = twinId,
                Operation = "CreateCar",
                CarData = carData,
                AIResponse = htmlResponse,
                ValidationResults = validationResult,
                ProcessingTimeMs = processingTimeMs,
                SearchIndexed = true // Nuevo campo para indicar que fue indexado
            };

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing car creation for Twin: {TwinId}", twinId);
            return new CarsAIResponse
            {
                Success = false,
                Error = ex.Message,
                TwinId = twinId,
                Operation = "CreateCar",
                ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                AIResponse = $"""
                ❌ **Error procesando la creación de tu vehículo**
                
                🔴 **Error:** {ex.Message}
                
                💡 **Sugerencias:**
                • Verifica que todos los campos requeridos estén completos
                • Intenta nuevamente con datos válidos
                • Contacta al soporte técnico si el problema persiste
                """
            };
        }
    }

    /// <summary>
    /// Valida y enriquece los datos del vehículo usando AI
    /// </summary>
    private async Task<CarValidationResult> ValidateAndEnrichCarDataAsync(CreateCarRequest request, string twinId)
    {
        try
        {
            _logger.LogInformation("🔍 Validating and enriching car data with AI");

            var validationPrompt = $"""
            🚗 **Especialista en Validación de Vehículos Automotores**

            Eres un experto en validación y enriquecimiento de datos de vehículos automotrices.

            🚗 **DATOS DEL VEHÍCULO A VALIDAR:**
            
            📋 **Información Básica:**
            • Marca: {request.Make}
            • Modelo: {request.Model}
            • Año: {request.Year}
            • Trim: {request.Trim}
            • Placa: {request.LicensePlate}
            • Estado de la Placa: {request.PlateState}
            • VIN: {request.Vin}
            
            🔧 **Especificaciones Técnicas:**
            • Transmisión: {request.Transmission}
            • Tipo de Combustible: {request.FuelType}
            • Tracción: {request.Drivetrain}
            • Millaje: {request.Mileage} {request.MileageUnit}
            • Cilindros: {request.Cylinders}
            
            🎨 **Apariencia:**
            • Color Exterior: {request.ExteriorColor}
            • Color Interior: {request.InteriorColor}
            • Estilo de Carrocería: {request.BodyStyle}
            
            💰 **Información Financiera:**
            • Precio de Lista Original: {request.OriginalListPrice}
            • Precio de Lista: {request.ListPrice}
            • Precio Actual: {request.CurrentPrice}
            • Precio Actual/Pagado: {request.ActualPaidPrice}
            • Pago Mensual: {request.MonthlyPayment}
            • Estado de Propiedad: {request.Estado}
            • Condición: {request.Condition}

            🔍 **TAREAS DE VALIDACIÓN:**

            1. **VALIDAR CAMPOS REQUERIDOS:**
               - Marca no puede estar vacía
               - Modelo no puede estar vacía
               - Año debe ser válido (1886 - {DateTime.Now.Year + 1})
               - Placa no puede estar vacía

            2. **VALIDAR LÓGICA DE DATOS:**
               - Año debe ser realista para la marca/modelo
               - Millaje debe ser razonable para el año del vehículo
               - Precio debe ser > 0 si se proporciona
               - Cilindros debe ser entre 1 y 16 si se especifica

            3. **SUGERENCIAS DE ENRIQUECIMIENTO:**
               - Validar consistencia marca/modelo/año
               - Sugerir características típicas del vehículo
               - Verificar rangos de precios para el mercado

            🔧 **FORMATO DE RESPUESTA:**
            VALIDATION_STATUS: [VALID|INVALID]
            VALIDATION_ERRORS: [lista de errores separados por |, o NONE si no hay errores]
            ENRICHMENT_SUGGESTIONS: [sugerencias de mejora separadas por |, o NONE]
            ESTIMATED_VALUE_RANGE: [rango estimado de valor si es posible]
            MARKET_SCORE: [puntuación del mercado 1-10 si es conocido, o UNKNOWN]
            VEHICLE_INSIGHTS: [insights sobre el vehículo]

            🚗 **IMPORTANTE:**
            - Sé estricto con validaciones críticas
            - Proporciona sugerencias constructivas sobre vehículos
            - Mantén el enfoque en datos automotrices precisos
            """;

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(validationPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 2000,
                    ["temperature"] = 0.3 // Temperatura baja para validación precisa
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "";
            
            _logger.LogInformation("✅ AI validation response generated successfully");
            return ParseValidationResponse(aiResponse, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during AI validation");
            
            // Fallback validation
            return new CarValidationResult
            {
                IsValid = ValidateBasicRequirements(request),
                ValidationErrors = ValidateBasicRequirements(request) ? new List<string>() : new List<string> { "Datos básicos incompletos" },
                EnrichedRequest = request,
                EnrichmentSuggestions = new List<string>(),
                VehicleInsights = "Validación básica aplicada (AI no disponible)"
            };
        }
    }

    /// <summary>
    /// Parsea la respuesta de validación de AI
    /// </summary>
    private CarValidationResult ParseValidationResponse(string aiResponse, CreateCarRequest originalRequest)
    {
        try
        {
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            bool isValid = true;
            var validationErrors = new List<string>();
            var enrichmentSuggestions = new List<string>();
            string vehicleInsights = "";
            string estimatedValueRange = "";
            string marketScore = "";

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("VALIDATION_STATUS:", StringComparison.OrdinalIgnoreCase))
                {
                    var status = trimmedLine.Substring("VALIDATION_STATUS:".Length).Trim();
                    isValid = status.Equals("VALID", StringComparison.OrdinalIgnoreCase);
                }
                else if (trimmedLine.StartsWith("VALIDATION_ERRORS:", StringComparison.OrdinalIgnoreCase))
                {
                    var errors = trimmedLine.Substring("VALIDATION_ERRORS:".Length).Trim();
                    if (!errors.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        validationErrors = errors.Split('|').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList();
                    }
                }
                else if (trimmedLine.StartsWith("ENRICHMENT_SUGGESTIONS:", StringComparison.OrdinalIgnoreCase))
                {
                    var suggestions = trimmedLine.Substring("ENRICHMENT_SUGGESTIONS:".Length).Trim();
                    if (!suggestions.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        enrichmentSuggestions = suggestions.Split('|').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    }
                }
                else if (trimmedLine.StartsWith("VEHICLE_INSIGHTS:", StringComparison.OrdinalIgnoreCase))
                {
                    vehicleInsights = trimmedLine.Substring("VEHICLE_INSIGHTS:".Length).Trim();
                }
                else if (trimmedLine.StartsWith("ESTIMATED_VALUE_RANGE:", StringComparison.OrdinalIgnoreCase))
                {
                    estimatedValueRange = trimmedLine.Substring("ESTIMATED_VALUE_RANGE:".Length).Trim();
                }
                else if (trimmedLine.StartsWith("MARKET_SCORE:", StringComparison.OrdinalIgnoreCase))
                {
                    marketScore = trimmedLine.Substring("MARKET_SCORE:".Length).Trim();
                }
            }

            return new CarValidationResult
            {
                IsValid = isValid,
                ValidationErrors = validationErrors,
                EnrichedRequest = originalRequest,
                EnrichmentSuggestions = enrichmentSuggestions,
                VehicleInsights = vehicleInsights,
                EstimatedValueRange = estimatedValueRange,
                MarketScore = marketScore
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error parsing AI validation response");
            return new CarValidationResult
            {
                IsValid = ValidateBasicRequirements(originalRequest),
                ValidationErrors = new List<string> { "Error en validación AI" },
                EnrichedRequest = originalRequest,
                EnrichmentSuggestions = new List<string>(),
                VehicleInsights = "Error procesando validación"
            };
        }
    }

    /// <summary>
    /// Validación básica de requerimientos mínimos
    /// </summary>
    private bool ValidateBasicRequirements(CreateCarRequest request)
    {
        if (string.IsNullOrEmpty(request.Make) || 
            string.IsNullOrEmpty(request.Model) || 
            request.Year < 1886 || request.Year > DateTime.Now.Year + 1 ||
            string.IsNullOrEmpty(request.LicensePlate))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Construye CarData desde CreateCarRequest
    /// </summary>
    private CarData BuildCarDataFromRequest(CreateCarRequest request, string twinId)
    {
        return new CarData
        {
            Id = Guid.NewGuid().ToString(),
            TwinID = twinId,
            StockNumber = request.StockNumber,
            Make = request.Make ?? "",
            Model = request.Model ?? "",
            Year = request.Year,
            Trim = request.Trim,
            SubModel = request.SubModel,
            BodyStyle = request.BodyStyle,
            Doors = request.Doors,
            LicensePlate = request.LicensePlate ?? "",
            PlateState = request.PlateState,
            Vin = request.Vin,
            Transmission = request.Transmission,
            Drivetrain = request.Drivetrain,
            FuelType = request.FuelType,
            EngineDescription = request.EngineDescription,
            Cylinders = request.Cylinders,
            EngineDisplacementLiters = request.EngineDisplacementLiters,
            Mileage = request.Mileage,
            MileageUnit = request.MileageUnit,
            OdometerStatus = request.OdometerStatus,
            ExteriorColor = request.ExteriorColor,
            InteriorColor = request.InteriorColor,
            Upholstery = request.Upholstery,
            Condition = request.Condition,
            StockStatus = request.StockStatus,
            HasOpenRecalls = request.HasOpenRecalls,
            HasAccidentHistory = request.HasAccidentHistory,
            IsCertifiedPreOwned = request.IsCertifiedPreOwned,
            DateAcquired = request.DateAcquired,
            DateListed = request.DateListed,
            AcquisitionSource = request.AcquisitionSource,
            AddressComplete = request.AddressComplete,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode,
            Country = request.Country,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ParkingLocation = request.ParkingLocation,
            OriginalListPrice = request.OriginalListPrice,
            ListPrice = request.ListPrice,
            CurrentPrice = request.CurrentPrice,
            ActualPaidPrice = request.ActualPaidPrice,
            EstimatedTax = request.EstimatedTax,
            EstimatedRegistrationFee = request.EstimatedRegistrationFee,
            DealerProcessingFee = request.DealerProcessingFee,
            MonthlyPayment = request.MonthlyPayment,
            Apr = request.Apr,
            TermMonths = request.TermMonths,
            DownPayment = request.DownPayment,
            StandardFeatures = request.StandardFeatures ?? new List<string>(),
            OptionalFeatures = request.OptionalFeatures ?? new List<string>(),
            SafetyFeatures = request.SafetyFeatures ?? new List<string>(),
            TitleBrand = request.TitleBrand,
            HasLien = request.HasLien,
            LienHolder = request.LienHolder,
            LienAmount = request.LienAmount,
            TitleState = request.TitleState,
            WarrantyType = request.WarrantyType,
            WarrantyStart = request.WarrantyStart,
            WarrantyEnd = request.WarrantyEnd,
            WarrantyProvider = request.WarrantyProvider,
            Photos = request.Photos ?? new List<string>(),
            VideoUrl = request.VideoUrl,
            InternalNotes = request.InternalNotes,
            Description = request.Description,
            Estado = request.Estado,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = request.CreatedBy,
            Type = "car"
        };
    }

    /// <summary>
    /// Genera respuesta HTML inteligente para la creación de vehículo
    /// </summary>
    private async Task<string> GenerateCreateCarResponseAsync(CarData carData, CarValidationResult validationResult, string twinId)
    {
        try
        {
            _logger.LogInformation("🤖 Generating intelligent create car response");

            // Colores específicos para tipos de vehículo
            var vehicleTypeColors = new Dictionary<string, string>
            {
                ["sedan"] = "#3498db",          // Azul para sedanes
                ["suv"] = "#2ecc71",            // Verde para SUVs
                ["truck"] = "#e74c3c",          // Rojo para trucks
                ["coupe"] = "#9b59b6",          // Púrpura para coupes
                ["hatchback"] = "#f39c12",      // Naranja para hatchbacks
                ["convertible"] = "#1abc9c",    // Turquesa para convertibles
                ["wagon"] = "#95a5a6",          // Gris para wagons
                ["van"] = "#34495e",            // Gris oscuro para vans
                ["other"] = "#2c3e50"           // Azul oscuro para otros
            };

            var vehicleColor = vehicleTypeColors.GetValueOrDefault(carData.BodyStyle?.ToLowerInvariant() ?? "other", "#3498db");

            var responsePrompt = $@"
Eres un analista experto en gestión automotriz y vehículos. Vas a analizar un vehículo recién registrado junto con los resultados de validación AI para generar un análisis comprensivo e insights útiles.
Importante: Siempre contesta, no inventes datos, pero tampoco me digas que no pudiste. Analiza lo que tienes disponible.

DATOS DEL VEHÍCULO REGISTRADO:
==============================
🚗 Información Básica:
• ID: {carData.Id}
• Marca: {carData.Make}
• Modelo: {carData.Model}
• Año: {carData.Year}
• Trim: {carData.Trim}
• Placa: {carData.LicensePlate} ({carData.PlateState})
• VIN: {carData.Vin}
• Estilo de Carrocería: {carData.BodyStyle}

🔧 Especificaciones Técnicas:
• Transmisión: {carData.Transmission}
• Tracción: {carData.Drivetrain}
• Tipo de Combustible: {carData.FuelType}
• Motor: {carData.EngineDescription}
• Cilindros: {carData.Cylinders}
• Millaje: {carData.Mileage} {carData.MileageUnit}

🎨 Apariencia:
• Color Exterior: {carData.ExteriorColor}
• Color Interior: {carData.InteriorColor}
• Tapicería: {carData.Upholstery}
• Puertas: {carData.Doors}

💰 Información Financiera:
• Precio de Lista Original: {(carData.OriginalListPrice.HasValue ? $"${carData.OriginalListPrice:N0}" : "No especificado")}
• Precio de Lista: {(carData.ListPrice.HasValue ? $"${carData.ListPrice:N0}" : "No especificado")}
• Precio Actual: {(carData.CurrentPrice.HasValue ? $"${carData.CurrentPrice:N0}" : "No especificado")}
• Precio Actual/Pagado: {(carData.ActualPaidPrice.HasValue ? $"${carData.ActualPaidPrice:N0}" : "No especificado")}
• Pago Mensual: {(carData.MonthlyPayment.HasValue ? $"${carData.MonthlyPayment:N0}" : "No aplica")}
• APR: {(carData.Apr.HasValue ? $"{carData.Apr:P2}" : "No especificado")}

📍 Ubicación y Estado:
• Condición: {carData.Condition}
• Estado de Propiedad: {carData.Estado}
• Ubicación: {carData.City}, {carData.State}
• Estado del Stock: {carData.StockStatus}

🔍 Información de Seguridad:
• Recalls Abiertos: {(carData.HasOpenRecalls ? "Sí" : "No")}
• Historial de Accidentes: {(carData.HasAccidentHistory ? "Sí" : "No")}
• Certificado Pre-Owned: {(carData.IsCertifiedPreOwned ? "Sí" : "No")}

📅 Fechas:
• Fecha de Adquisición: {carData.DateAcquired?.ToString("yyyy-MM-dd") ?? "No especificada"}
• Fecha de Registro: {carData.CreatedAt:yyyy-MM-dd HH:mm}

RESULTADOS DE VALIDACIÓN AI:
============================
✅ Estado de Validación: {(validationResult.IsValid ? "✅ Válido" : "❌ Con errores")}
🔍 Insights del Vehículo: {validationResult.VehicleInsights}
💰 Rango de Valor Estimado: {validationResult.EstimatedValueRange}
📊 Puntuación del Mercado: {validationResult.MarketScore}

INSTRUCCIONES PARA EL ANÁLISIS:
===============================

Genera un análisis comprensivo que incluya EXACTAMENTE estos elementos en formato JSON:

1. **executiveSummary**: Un resumen ejecutivo conciso (2-3 párrafos) que incluya:
   - Resumen de las características principales del vehículo
   - Análisis de la validación de datos y insights de AI
   - Evaluación del valor y potencial del vehículo
   - Recomendaciones breves para gestión

2. **detailedHtmlReport**: Un reporte HTML detallado y visualmente atractivo que incluya:
   - Header con el color de tipo de vehículo ({vehicleColor})
   - Sección de especificaciones principales del vehículo
   - Análisis financiero y de mercado
   - Información de ubicación y condición
   - Recomendaciones de mantenimiento con íconos
   - Tabla con información detallada del vehículo
   - Footer con información de registro
   - Al final del HTML explica valor total del vehículo, beneficios para el portafolio automotriz, 
     recomendaciones para el futuro basadas en el análisis.

3. **detalleTexto**: Un reporte detallado en JSON que incluya:
   - Especificaciones completas del vehículo
   - Análisis financiero detallado
   - Información de ubicación y condición
   - Insights de validación AI
   - Usa JSON para crear cada campo que encuentres de la información del vehículo

FORMATO DE RESPUESTA REQUERIDO:
===============================
{{
    ""executiveSummary"": ""Texto del sumario ejecutivo sobre el vehículo registrado"",
    ""detalleTexto"": ""Texto de todos los datos en formato JSON estructurado con cada campo y variable del vehículo"",
    ""detailedHtmlReport"": ""<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto;'>...</div>"",
    ""metadata"": {{
        ""vehicleValue"": {(carData.CurrentPrice ?? 0)},
        ""confidenceLevel"": ""high"",
        ""insights"": [""insight1"", ""rec1""],
        ""recommendations"": [""rec1"", ""rec2""],
        ""marketAnalysis"": ""análisis del mercado automotriz"",
        ""vehicleType"": ""{carData.BodyStyle}"",
        ""makeModel"": ""{carData.Make} {carData.Model}"",
        ""year"": {carData.Year}
    }}
}}

IMPORTANTE:
- Responde SOLO con JSON válido
- Usa colores y estilos CSS inline en el HTML apropiados para vehículos
- Incluye emojis relevantes en el HTML (🚗🔧💰📊🔍⚡🛡️)
- Genera insights automotrices útiles basados en los datos reales
- Mantén el HTML responsive y profesional
- Analiza el potencial de inversión y gestión del vehículo
- Todo el texto debe estar en español
- Enfócate en el valor que aporta al portafolio automotriz del Twin";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(responsePrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 4000,  // Incrementado para análisis completo
                    ["temperature"] = 0.3   // Temperatura baja para análisis preciso
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "{}";
            
            _logger.LogInformation("✅ AI create car comprehensive analysis generated successfully");
            
            // Devolver la respuesta JSON completa
            return aiResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating AI create car response");
            
            // Respuesta de fallback
            return GenerateFallbackCarResponse(carData, validationResult, twinId);
        }
    }

    /// <summary>
    /// Extrae solo la parte HTML de la respuesta JSON para mostrar al usuario
    /// </summary>
    private string ExtractHtmlFromResponse(string jsonResponse)
    {
        try
        {
            // Limpiar la respuesta
            var cleanResponse = jsonResponse.Trim();
            if (cleanResponse.StartsWith("```json"))
            {
                cleanResponse = cleanResponse.Substring(7);
            }
            if (cleanResponse.EndsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
            }
            cleanResponse = cleanResponse.Trim();

            // Parsear JSON
            var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(cleanResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Extraer el HTML
            if (responseData?.TryGetValue("detailedHtmlReport", out var htmlObj) == true)
            {
                if (htmlObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                {
                    return jsonElement.GetString() ?? GenerateBasicFallbackHtml();
                }
                return htmlObj.ToString() ?? GenerateBasicFallbackHtml();
            }

            return GenerateBasicFallbackHtml();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting HTML from AI response, using fallback");
            return GenerateBasicFallbackHtml();
        }
    }

    /// <summary>
    /// Genera respuesta de fallback
    /// </summary>
    private string GenerateFallbackCarResponse(CarData carData, CarValidationResult validationResult, string twinId)
    {
        return $"""
        <div style="background: linear-gradient(135deg, #3498db 0%, #2980b9 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
            <h3 style="color: #fff; margin: 0 0 15px 0;">🚗 Vehículo Registrado Exitosamente</h3>
            
            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">📋 Detalles del Vehículo</h4>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Vehículo:</strong> {carData.Make} {carData.Model} {carData.Year}</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Placa:</strong> {carData.LicensePlate} ({carData.PlateState})</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Estilo:</strong> {carData.BodyStyle}</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Millaje:</strong> {carData.Mileage} {carData.MileageUnit}</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Combustible:</strong> {carData.FuelType}</p>
            </div>

            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">✅ Estado de Validación</h4>
                <p style="margin: 0; line-height: 1.6;">
                    {(validationResult.IsValid ? "✅ Vehículo validado correctamente" : "⚠️ Validación con observaciones")}
                    {(!string.IsNullOrEmpty(validationResult.VehicleInsights) ? $" - {validationResult.VehicleInsights}" : "")}
                </p>
            </div>

            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">💰 Información Financiera</h4>
                <p style="margin: 5px 0; line-height: 1.6;">
                    <strong>Precio Original:</strong> {(carData.OriginalListPrice.HasValue ? $"${carData.OriginalListPrice:N0}" : "No especificado")}
                </p>
                <p style="margin: 5px 0; line-height: 1.6;">
                    <strong>Precio Actual:</strong> {(carData.CurrentPrice.HasValue ? $"${carData.CurrentPrice:N0}" : "No especificado")}
                </p>
                <p style="margin: 5px 0; line-height: 1.6;">
                    <strong>Precio Pagado:</strong> {(carData.ActualPaidPrice.HasValue ? $"${carData.ActualPaidPrice:N0}" : "No especificado")}
                </p>
                <p style="margin: 5px 0; line-height: 1.6;">
                    <strong>Pago Mensual:</strong> {(carData.MonthlyPayment.HasValue ? $"${carData.MonthlyPayment:N0}" : "No aplica")}
                    {(!string.IsNullOrEmpty(validationResult.EstimatedValueRange) ? $" ({validationResult.EstimatedValueRange})" : "")}
                </p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Estado:</strong> {carData.Estado}</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Condición:</strong> {carData.Condition}</p>
            </div>
            
            <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                🚗 ID: {carData.Id} • 👤 Twin: {twinId} • 📅 {carData.CreatedAt:yyyy-MM-dd HH:mm}
            </div>
        </div>
        """;
    }

    /// <summary>
    /// Genera HTML básico de fallback
    /// </summary>
    private string GenerateBasicFallbackHtml()
    {
        return """
        <div style="background: linear-gradient(135deg, #3498db 0%, #2980b9 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
            <h3 style="color: #fff; margin: 0 0 15px 0;">🚗 Vehículo Registrado</h3>
            <p style="margin: 0; line-height: 1.6;">Tu vehículo ha sido registrado exitosamente en tu portafolio automotriz.</p>
        </div>
        """;
    }

    /// <summary>
    /// Inicializa Semantic Kernel para operaciones de AI
    /// </summary>
    private async Task InitializeKernelAsync()
    {
        if (_kernel != null)
            return; // Ya está inicializado

        try
        {
            _logger.LogInformation("🔧 Initializing Semantic Kernel for CarsAgent");

            // Crear kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Obtener configuración de Azure OpenAI
            var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                          _configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? 
                          throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

            var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                        _configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? 
                        throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

            var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                                _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ?? 
                                "gpt4mini";

            // Agregar Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Construir el kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized successfully for CarsAgent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for CarsAgent");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Procesa documentos de seguros de auto usando Document Intelligence y AI para extraer información específica
    /// </summary>
    /// <param name="containerName">Nombre del contenedor DataLake (twinId)</param>
    /// <param name="filePath">Ruta dentro del contenedor</param>
    /// <param name="fileName">Nombre del archivo del documento</param>
    /// <param name="carId">ID del auto para actualizar el índice</param>
    /// <returns>Resultado del análisis como string JSON</returns>
    public async Task<string> AiCarInsurance(
        string containerName, 
        string filePath, 
        string fileName,
        string carId)
    {
        _logger.LogInformation("🚗🛡️📄 Starting Car Insurance analysis for: {FileName}, CarId: {CarId}", fileName, carId);
        
        var startTime = DateTime.UtcNow;

        try
        {
            // PASO 1: Generar SAS URL para acceso al documento
            _logger.LogInformation("🔗 STEP 1: Generating SAS URL for document access...");
            
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(containerName);
            var fullFilePath = $"{filePath}/{fileName}";
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(2));

            if (string.IsNullOrEmpty(sasUrl))
            {
                var errorResult = new
                {
                    success = false,
                    errorMessage = "Failed to generate SAS URL for document access",
                    containerName,
                    filePath,
                    fileName,
                    carId,
                    processedAt = DateTime.UtcNow
                };
                _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                return JsonSerializer.Serialize(errorResult);
            }

            _logger.LogInformation("✅ SAS URL generated successfully");

            // PASO 2: Análisis con Document Intelligence
            _logger.LogInformation("🧠 STEP 2: Extracting data with Document Intelligence...");
            
            // Inicializar DocumentIntelligenceService
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, _configuration);
            
            var documentAnalysis = await documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
            
            if (!documentAnalysis.Success)
            {
                var errorResult = new
                {
                    success = false,
                    errorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}",
                    containerName,
                    filePath,
                    fileName,
                    carId,
                    processedAt = DateTime.UtcNow
                };
                _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                return JsonSerializer.Serialize(errorResult);
            }

            _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars", 
                documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

            // PASO 3: Procesamiento con AI especializado en seguros de auto
            _logger.LogInformation("🤖 STEP 3: Processing with AI specialized in car insurance...");
            
            var aiAnalysisResult = await ProcessCarInsuranceWithAI(documentAnalysis);

            // PASO 4: NUEVO - Actualizar solo el campo carInsurance en el índice de búsqueda
            _logger.LogInformation("🔍 STEP 4: Updating carInsurance field in search index...");
            try
            {
                // Crear instancia del CarsSearchIndex
                var carsSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CarsSearchIndex>();
                var carsSearchIndex = new CarsSearchIndex(carsSearchLogger, _configuration);

                // Llamar al método CarIndexInsuranceUpdate pasando el carId directamente
                var updateResult = await carsSearchIndex.CarIndexInsuranceUpdate(aiAnalysisResult, carId, containerName);
                
                if (updateResult.Success)
                {
                    _logger.LogInformation("✅ carInsurance field updated successfully: DocumentId={DocumentId}", updateResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to update carInsurance field: {Error}", updateResult.Error);
                }
            }
            catch (Exception indexEx)
            {
                _logger.LogWarning(indexEx, "⚠️ Failed to update carInsurance field in search index, continuing with main flow");
                // No fallar toda la operación por esto
            }

            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Resultado exitoso
            var successResult = new
            {
                success = true,
                containerName,
                filePath,
                fileName,
                carId,
                documentUrl = sasUrl,
                textContent = documentAnalysis.TextContent,
                totalPages = documentAnalysis.TotalPages,
                aiAnalysis = aiAnalysisResult,
                processingTimeMs,
                processedAt = DateTime.UtcNow
            };

            _logger.LogInformation("✅ Car insurance analysis completed successfully in {ProcessingTime}ms", processingTimeMs);
            
            return JsonSerializer.Serialize(successResult, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing car insurance document {FileName}", fileName);
            
            var errorResult = new
            {
                success = false,
                errorMessage = ex.Message,
                containerName,
                filePath,
                fileName,
                carId,
                processedAt = DateTime.UtcNow
            };
            
            return JsonSerializer.Serialize(errorResult);
        }
    }

    /// <summary>
    /// Procesa documento con AI para extraer información específica de seguros de auto
    /// </summary>
    private async Task<string> ProcessCarInsuranceWithAI(DocumentAnalysisResult documentAnalysis)
    {
        try
        {
            // Asegurar que el kernel esté inicializado
            await InitializeKernelAsync();
            
            var chatCompletion = _kernel!.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();

            var prompt = $@"
Analiza este documento de seguro de auto y extrae información estructurada específica de seguros de vehículos.

CONTENIDO COMPLETO DEL DOCUMENTO:
{documentAnalysis.TextContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}

INSTRUCCIONES ESPECÍFICAS PARA SEGUROS DE AUTO:
Vas a crear un HTML que contenga todos los detalles de la póliza de auto. Cada campo, variable, es para el cliente, explica qué es la póliza, qué contiene, y todas sus partes. El HTML debe ser visualmente atractivo y fácil de entender.

Estructura del HTML
Encabezado Principal:
Un título principal que diga ""Reporte de Póliza de Seguro de Auto"".
Secciones:
Información de la Póliza:
Número de póliza
Nombre de la compañía aseguradora
Información del agente de seguros
Fechas de Vigencia:
Fecha de inicio
Fecha de fin
Fecha de renovación
Vehículo Asegurado:
Marca, modelo, año del vehículo
VIN (Número de Identificación del Vehículo)
Placa del vehículo
Valor del vehículo
Coberturas de Auto:
Cobertura de responsabilidad civil (monto y deducible)
Cobertura de colisión (monto y deducible)
Cobertura comprensiva (monto y deducible)
Cobertura de lesiones personales (PIP)
Cobertura de automovilista sin seguro/con seguro insuficiente
Asistencia en carretera
Deducibles:
Deducible de colisión
Deducible comprensivo
Deducible de vidrios
Asegurados:
Nombre del asegurado principal
Conductores autorizados
Edad y experiencia de conducción
Información de Pago:
Prima total anual
Descuentos aplicados
Frecuencia de pago (mensual, semestral, anual)
Monto por pago
Método de pago
Historial de Siniestros:
Fecha del siniestro
Tipo de siniestro (accidente, robo, vandalismo, etc.)
Monto reclamado
Estado de la reclamación
Exclusiones:
Lista de exclusiones importantes del seguro
Condiciones Especiales:
Condiciones especiales de la póliza de auto
Beneficios Adicionales:
Servicios adicionales incluidos (grúa, auto de reemplazo, etc.)
Resumen Ejecutivo:
Resumen ejecutivo del seguro de auto con puntos clave
Insights:
Tipo de insight (COVERAGE/FINANCIAL/RISK/DISCOUNT)
Título del insight
Descripción detallada
Valor específico
Importancia (HIGH/MEDIUM/LOW)
Recomendaciones:
Lista de recomendaciones basadas en el análisis
Alertas:
Alertas importantes sobre la póliza
Diseño del HTML
Utiliza grids y listas para organizar la información de forma clara y accesible.
Emplea colores llamativos para resaltar secciones importantes.
Incluye iconos y emojis relevantes para hacer el informe más visual y atractivo.
Cada sección debe ser claramente etiquetada y explicada para que el cliente entienda cada parte de la póliza.
Recuerda que el objetivo es crear un documento HTML que sea fácil de leer y que contenga toda la información relevante de la póliza de seguro de auto, explicado de manera comprensible para el cliente.

IMPORTANTE:
- Extrae TODA la información disponible, no inventes datos
- Si no encuentras información específica, usa ""No especificado""
- Enfócate en datos financieros, coberturas y deducibles de auto
- Identifica riesgos y recomendaciones relevantes para vehículos
- Todo el texto debe estar en español 
Tu respuesta un string en HTML con colores, muchos colores, cards, grids, etc.
";

            history.AddUserMessage(prompt);
            
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 4000,
                    ["temperature"] = 0.2 // Temperatura muy baja para análisis preciso
                }
            };

            var response = await chatCompletion.GetChatMessageContentAsync(
                history,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "{}";
            
            // Limpiar respuesta de cualquier formato markdown
            aiResponse = aiResponse.Trim().Trim('`');
            if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                aiResponse = aiResponse.Substring(4).Trim();
            }

            _logger.LogInformation("✅ AI car insurance analysis completed successfully");
            _logger.LogInformation("📊 AI Response Length: {Length} characters", aiResponse.Length);

            return aiResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in AI car insurance processing");
            
            // Retornar error en formato JSON
            var errorResponse = new
            {
                success = false,
                errorMessage = ex.Message,
                processedAt = DateTime.UtcNow
            };
            
            return JsonSerializer.Serialize(errorResponse);
        }
    }
}

// ========================================
// MODELS Y RESPONSE CLASSES
// ========================================

/// <summary>
/// Request para crear un nuevo vehículo
/// </summary>
public class CreateCarRequest
{
    // Información básica requerida
    public string? StockNumber { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int Year { get; set; }
    public string? Trim { get; set; }
    public string? SubModel { get; set; }
    public string? BodyStyle { get; set; }
    public int? Doors { get; set; }
    public string? LicensePlate { get; set; }
    public string? PlateState { get; set; }
    public string? Vin { get; set; }

    // Especificaciones técnicas
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }
    public string? FuelType { get; set; }
    public string? EngineDescription { get; set; }
    public int? Cylinders { get; set; }
    public decimal? EngineDisplacementLiters { get; set; }
    public long? Mileage { get; set; }
    public string? MileageUnit { get; set; }
    public string? OdometerStatus { get; set; }

    // Colores y apariencia
    public string? ExteriorColor { get; set; }
    public string? InteriorColor { get; set; }
    public string? Upholstery { get; set; }

    // Estado y condición
    public string? Condition { get; set; }
    public string? StockStatus { get; set; }
    public bool HasOpenRecalls { get; set; }
    public bool HasAccidentHistory { get; set; }
    public bool IsCertifiedPreOwned { get; set; }

    // Fechas y adquisición
    public DateTime? DateAcquired { get; set; }
    public DateTime? DateListed { get; set; }
    public string? AcquisitionSource { get; set; }

    // Ubicación
    public string? AddressComplete { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ParkingLocation { get; set; }

    // Información financiera
    public decimal? OriginalListPrice { get; set; } // Precio de Lista Original
    public decimal? ListPrice { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? ActualPaidPrice { get; set; } // Precio Actual/Pagado
    public decimal? EstimatedTax { get; set; }
    public decimal? EstimatedRegistrationFee { get; set; }
    public decimal? DealerProcessingFee { get; set; }

    // Finanziamiento
    public decimal? MonthlyPayment { get; set; }
    public decimal? Apr { get; set; }
    public int? TermMonths { get; set; }
    public decimal? DownPayment { get; set; }

    // Características
    public List<string>? StandardFeatures { get; set; }
    public List<string>? OptionalFeatures { get; set; }
    public List<string>? SafetyFeatures { get; set; }

    // Título
    public string? TitleBrand { get; set; }
    public bool HasLien { get; set; }
    public string? LienHolder { get; set; }
    public decimal? LienAmount { get; set; }
    public string? TitleState { get; set; }

    // Garantía
    public string? WarrantyType { get; set; }
    public DateTime? WarrantyStart { get; set; }
    public DateTime? WarrantyEnd { get; set; }
    public string? WarrantyProvider { get; set; }

    // Multimedia
    public List<string>? Photos { get; set; }
    public string? VideoUrl { get; set; }

    // Notas
    public string? InternalNotes { get; set; }
    public string? Description { get; set; }

    // Estado de propiedad
    public string? Estado { get; set; } // Propio, Financiado, Arrendado, Vendido

    // Metadatos
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Resultado de validación de datos de vehículo
/// </summary>
public class CarValidationResult
{
    public bool IsValid { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public CreateCarRequest EnrichedRequest { get; set; } = new();
    public List<string> EnrichmentSuggestions { get; set; } = new();
    public string VehicleInsights { get; set; } = "";
    public string EstimatedValueRange { get; set; } = "";
    public string MarketScore { get; set; } = "";
}

/// <summary>
/// Respuesta del CarsAgent
/// </summary>
public class CarsAIResponse
{
    /// <summary>
    /// Indica si la operación fue exitosa
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Mensaje de error si Success = false
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// ID del Twin
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de operación realizada
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Datos del vehículo procesado
    /// </summary>
    public CarData? CarData { get; set; }

    /// <summary>
    /// Respuesta HTML generada por AI
    /// </summary>
    public string AIResponse { get; set; } = string.Empty;

    /// <summary>
    /// Resultados de validación
    /// </summary>
    public CarValidationResult? ValidationResults { get; set; }

    /// <summary>
    /// Errores de validación
    /// </summary>
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Tiempo de procesamiento en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Fecha y hora cuando se procesó la operación
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indica si el análisis fue indexado en el search index
    /// </summary>
    public bool SearchIndexed { get; set; } = false;

    /// <summary>
    /// Obtiene un resumen de la respuesta para logging
    /// </summary>
    public string GetSummary()
    {
        if (!Success)
        {
            return $"❌ Error: {Error}";
        }

        var indexedStatus = SearchIndexed ? "indexed" : "not indexed";
        return $"✅ Success: {Operation} completed, {ProcessingTimeMs:F0}ms, {indexedStatus}";
    }

    /// <summary>
    /// Determina si la respuesta contiene información útil
    /// </summary>
    public bool HasUsefulContent => Success && CarData != null && !string.IsNullOrEmpty(AIResponse);
}