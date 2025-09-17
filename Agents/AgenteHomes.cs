using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Agente especializado en gestión inteligente de casas/viviendas del Twin
/// ========================================================================
/// 
/// Este agente utiliza AI para:
/// - Procesamiento inteligente de datos de casas/viviendas
/// - Validación y enriquecimiento de información de propiedades
/// - Generación de recomendaciones basadas en datos
/// - Análisis comparativo de propiedades
/// - Solo responde preguntas relacionadas con casas/viviendas del Twin
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class AgenteHomes
{
    private readonly ILogger<AgenteHomes> _logger;
    private readonly IConfiguration _configuration;
    private readonly HomesCosmosDbService _homesService;
    private Kernel? _kernel;

    public AgenteHomes(ILogger<AgenteHomes> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Inicializar HomesCosmosDbService
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new TwinFx.Models.CosmosDbSettings
        {
            Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? configuration["COSMOS_ENDPOINT"] ?? "",
            Key = configuration["Values:COSMOS_KEY"] ?? configuration["COSMOS_KEY"] ?? "",
            DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });
        
        var homesLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesCosmosDbService>();
        _homesService = new HomesCosmosDbService(homesLogger, cosmosOptions);
        
        _logger.LogInformation("?? AgenteHomes initialized for intelligent home management");
    }

    /// <summary>
    /// Procesa la creación inteligente de una nueva casa con validación y enriquecimiento de datos
    /// </summary>
    /// <param name="homeRequest">Datos de la casa a crear</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Respuesta inteligente con la casa creada y recomendaciones</returns>
    public async Task<HomesAIResponse> ProcessCreateHomeAsync(CreateHomeRequest homeRequest, string twinId)
    {
        _logger.LogInformation("?? Processing intelligent home creation for Twin ID: {TwinId}", twinId);
        _logger.LogInformation("?? Home: {Direccion}, {Ciudad}, {Estado}", homeRequest.Direccion, homeRequest.Ciudad, homeRequest.Estado);

        try
        {
            // Validar inputs básicos
            if (homeRequest == null || string.IsNullOrEmpty(twinId))
            {
                return new HomesAIResponse
                {
                    Success = false,
                    Error = "Home request and TwinId are required",
                    TwinId = twinId,
                    Operation = "CreateHome"
                };
            }

            // PASO 1: Validar y enriquecer datos con AI
            _logger.LogInformation("?? Step 1: Validating and enriching home data with AI");
            await InitializeKernelAsync();
            
            var validationResult = await ValidateAndEnrichHomeDataAsync(homeRequest, twinId);
            
            if (!validationResult.IsValid)
            {
                return new HomesAIResponse
                {
                    Success = false,
                    Error = validationResult.ValidationErrors.FirstOrDefault() ?? "Invalid home data",
                    TwinId = twinId,
                    Operation = "CreateHome",
                    ValidationErrors = validationResult.ValidationErrors
                };
            }

            // PASO 2: Crear HomeData usando los datos enriquecidos
            var homeData = BuildHomeDataFromRequest(validationResult.EnrichedRequest, twinId);

            // PASO 3: Verificar reglas de negocio (ej: solo una casa principal)
            if (homeData.EsPrincipal)
            {
                var existingPrincipal = await _homesService.GetViviendaPrincipalAsync(twinId);
                if (existingPrincipal != null)
                {
                    _logger.LogInformation("?? Principal home already exists, will be updated automatically");
                }
            }

            // PASO 4: Crear la casa en Cosmos DB
            var createSuccess = await _homesService.CreateLugarAsync(homeData);
            
            if (!createSuccess)
            {
                return new HomesAIResponse
                {
                    Success = false,
                    Error = "Failed to create home in database",
                    TwinId = twinId,
                    Operation = "CreateHome"
                };
            }

            // PASO 5: Generar respuesta inteligente con recomendaciones
            var aiResponse = await GenerateCreateHomeResponseAsync(homeData, validationResult, twinId);

            _logger.LogInformation("? Home created successfully: {Id}", homeData.Id);

            return new HomesAIResponse
            {
                Success = true,
                TwinId = twinId,
                Operation = "CreateHome",
                HomeData = homeData,
                AIResponse = aiResponse,
                ValidationResults = validationResult,
                ProcessingTimeMs = DateTime.UtcNow.Subtract(DateTime.UtcNow.AddMilliseconds(-100)).TotalMilliseconds
            };

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error processing home creation for Twin: {TwinId}", twinId);
            return new HomesAIResponse
            {
                Success = false,
                Error = ex.Message,
                TwinId = twinId,
                Operation = "CreateHome",
                AIResponse = $"""
                ? **Error procesando la creación de tu casa**
                
                ?? **Error:** {ex.Message}
                
                ?? **Sugerencias:**
                • Verifica que todos los campos requeridos estén completos
                • Intenta nuevamente con datos válidos
                • Contacta al soporte técnico si el problema persiste
                """
            };
        }
    }

    /// <summary>
    /// Valida y enriquece los datos de la casa usando AI
    /// </summary>
    private async Task<HomeValidationResult> ValidateAndEnrichHomeDataAsync(CreateHomeRequest request, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Validating and enriching home data with AI");

            var validationPrompt = $"""
            ?? **Especialista en Validación de Propiedades Inmobiliarias**

            Eres un experto en validación y enriquecimiento de datos de propiedades inmobiliarias.

            ?? **DATOS DE LA PROPIEDAD A VALIDAR:**
            
            ?? **Información Básica:**
            • Dirección: {request.Direccion}
            • Ciudad: {request.Ciudad}
            • Estado: {request.Estado}
            • Código Postal: {request.CodigoPostal}
            • Tipo: {request.Tipo} (actual/pasado/inversion/vacacional)
            • Tipo de Propiedad: {request.TipoPropiedad}
            
            ?? **Detalles de la Propiedad:**
            • Área Total: {request.AreaTotal} pies cuadrados
            • Habitaciones: {request.Habitaciones}
            • Baños: {request.Banos}
            • Medio Baños: {request.MedioBanos}
            • Año de Construcción: {request.AnoConstructorcion}
            • Es Principal: {request.EsPrincipal}
            
            ?? **TAREAS DE VALIDACIÓN:**

            1. **VALIDAR CAMPOS REQUERIDOS:**
               - Dirección no puede estar vacía
               - Ciudad y Estado son obligatorios
               - Tipo debe ser: actual, pasado, inversion, o vacacional
               - TipoPropiedad debe ser: casa, apartamento, condominio, townhouse, u otro

            2. **VALIDAR LÓGICA DE DATOS:**
               - Área total debe ser > 0 si se proporciona
               - Habitaciones debe ser >= 0
               - Baños debe ser >= 0
               - Año de construcción debe ser entre 1800 y {DateTime.Now.Year + 5}

            3. **SUGERENCIAS DE ENRIQUECIMIENTO:**
               - Si falta código postal, sugerir búsqueda
               - Si falta año de construcción, sugerir rango probable
               - Verificar consistencia entre área y número de habitaciones

            ?? **FORMATO DE RESPUESTA:**
            VALIDATION_STATUS: [VALID|INVALID]
            VALIDATION_ERRORS: [lista de errores separados por |, o NONE si no hay errores]
            ENRICHMENT_SUGGESTIONS: [sugerencias de mejora separadas por |, o NONE]
            ESTIMATED_VALUE_RANGE: [rango estimado de valor si es posible]
            NEIGHBORHOOD_SCORE: [puntuación del vecindario 1-10 si es conocido, o UNKNOWN]
            PROPERTY_INSIGHTS: [insights sobre la propiedad]

            ?? **IMPORTANTE:**
            - Sé estricto con validaciones críticas
            - Proporciona sugerencias constructivas
            - Mantén el enfoque en datos inmobiliarios precisos
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
            
            _logger.LogInformation("? AI validation response generated successfully");
            return ParseValidationResponse(aiResponse, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error during AI validation");
            
            // Fallback validation
            return new HomeValidationResult
            {
                IsValid = ValidateBasicRequirements(request),
                ValidationErrors = ValidateBasicRequirements(request) ? new List<string>() : new List<string> { "Datos básicos incompletos" },
                EnrichedRequest = request,
                EnrichmentSuggestions = new List<string>(),
                PropertyInsights = "Validación básica aplicada (AI no disponible)"
            };
        }
    }

    /// <summary>
    /// Parsea la respuesta de validación de AI
    /// </summary>
    private HomeValidationResult ParseValidationResponse(string aiResponse, CreateHomeRequest originalRequest)
    {
        try
        {
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            bool isValid = true;
            var validationErrors = new List<string>();
            var enrichmentSuggestions = new List<string>();
            string propertyInsights = "";
            string estimatedValueRange = "";
            string neighborhoodScore = "";

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
                else if (trimmedLine.StartsWith("PROPERTY_INSIGHTS:", StringComparison.OrdinalIgnoreCase))
                {
                    propertyInsights = trimmedLine.Substring("PROPERTY_INSIGHTS:".Length).Trim();
                }
                else if (trimmedLine.StartsWith("ESTIMATED_VALUE_RANGE:", StringComparison.OrdinalIgnoreCase))
                {
                    estimatedValueRange = trimmedLine.Substring("ESTIMATED_VALUE_RANGE:".Length).Trim();
                }
                else if (trimmedLine.StartsWith("NEIGHBORHOOD_SCORE:", StringComparison.OrdinalIgnoreCase))
                {
                    neighborhoodScore = trimmedLine.Substring("NEIGHBORHOOD_SCORE:".Length).Trim();
                }
            }

            return new HomeValidationResult
            {
                IsValid = isValid,
                ValidationErrors = validationErrors,
                EnrichedRequest = originalRequest, // Por ahora usamos el original, se puede enriquecer más adelante
                EnrichmentSuggestions = enrichmentSuggestions,
                PropertyInsights = propertyInsights,
                EstimatedValueRange = estimatedValueRange,
                NeighborhoodScore = neighborhoodScore
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error parsing AI validation response");
            return new HomeValidationResult
            {
                IsValid = ValidateBasicRequirements(originalRequest),
                ValidationErrors = new List<string> { "Error en validación AI" },
                EnrichedRequest = originalRequest,
                EnrichmentSuggestions = new List<string>(),
                PropertyInsights = "Error procesando validación"
            };
        }
    }

    /// <summary>
    /// Validación básica de requerimientos mínimos
    /// </summary>
    private bool ValidateBasicRequirements(CreateHomeRequest request)
    {
        if (string.IsNullOrEmpty(request.Direccion) || 
            string.IsNullOrEmpty(request.Ciudad) || 
            string.IsNullOrEmpty(request.Estado) ||
            string.IsNullOrEmpty(request.TipoPropiedad))
        {
            return false;
        }

        var validTipos = new[] { "actual", "pasado", "inversion", "vacacional" };
        if (!validTipos.Contains(request.Tipo?.ToLowerInvariant()))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Construye HomeData desde CreateHomeRequest
    /// </summary>
    private HomeData BuildHomeDataFromRequest(CreateHomeRequest request, string twinId)
    {
        return new HomeData
        {
            Id = Guid.NewGuid().ToString(),
            TwinID = twinId,
            Tipo = request.Tipo ?? "actual",
            Direccion = request.Direccion ?? "",
            Ciudad = request.Ciudad ?? "",
            Estado = request.Estado ?? "",
            CodigoPostal = request.CodigoPostal ?? "",
            FechaInicio = request.FechaInicio ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            FechaFin = request.FechaFin,
            EsPrincipal = request.EsPrincipal,
            TipoPropiedad = request.TipoPropiedad ?? "",
            AreaTotal = request.AreaTotal,
            Habitaciones = request.Habitaciones,
            Banos = request.Banos,
            MedioBanos = request.MedioBanos,
            AnoConstruction = request.AnoConstructorcion,
            Descripcion = request.Descripcion ?? "",
            ValorEstimado = request.ValorEstimado,
            FechaCreacion = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow,
            Type = "home"
        };
    }

    /// <summary>
    /// Genera respuesta HTML inteligente para la creación de casa
    /// </summary>
    private async Task<string> GenerateCreateHomeResponseAsync(HomeData homeData, HomeValidationResult validationResult, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Generating intelligent create home response");

            var responsePrompt = $"""
            ?? **Especialista en Gestión de Propiedades Inmobiliarias**

            Has ayudado exitosamente a crear una nueva propiedad para el Twin.

            ?? **PROPIEDAD CREADA:**
            • ID: {homeData.Id}
            • Dirección: {homeData.Direccion}
            • Ciudad: {homeData.Ciudad}, {homeData.Estado} {homeData.CodigoPostal}
            • Tipo: {homeData.Tipo}
            • Tipo de Propiedad: {homeData.TipoPropiedad}
            • Área Total: {homeData.AreaTotal} pies cuadrados
            • Habitaciones: {homeData.Habitaciones} | Baños: {homeData.Banos}
            • Es Principal: {(homeData.EsPrincipal ? "Sí" : "No")}
            • Año de Construcción: {homeData.AnoConstruction}

            ?? **RESULTADOS DE VALIDACIÓN:**
            • Estado: {(validationResult.IsValid ? "? Válida" : "? Con errores")}
            • Insights: {validationResult.PropertyInsights}
            • Valor Estimado: {validationResult.EstimatedValueRange}
            • Puntuación del Vecindario: {validationResult.NeighborhoodScore}

            ?? **CREAR RESPUESTA HTML ELEGANTE:**

            Crea una respuesta HTML profesional que incluya:
            1. **Confirmación de creación exitosa** con detalles de la propiedad
            2. **Resumen visual** de características principales
            3. **Insights y recomendaciones** basados en la validación AI
            4. **Próximos pasos sugeridos** para el usuario
            5. **Información sobre la gestión de la propiedad**

            ?? **FORMATO DE RESPUESTA HTML:**
            Usa estilos inline con colores apropiados para bienes raíces (verdes, azules, dorados).
            Incluye secciones bien organizadas y emojis relevantes.
            Mantén un tono profesional pero amigable.

            ?? **IMPORTANTE:**
            - Enfócate en el valor que esta propiedad aporta al portafolio del Twin
            - Proporciona insights útiles sobre la gestión inmobiliaria
            - Sugiere próximos pasos relevantes
            """;

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(responsePrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 3000,
                    ["temperature"] = 0.4 // Temperatura media para respuestas creativas pero precisas
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "Respuesta HTML no generada.";
            
            _logger.LogInformation("? AI create home response generated successfully");
            return aiResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating AI create home response");
            
            // Respuesta de fallback
            return $"""
            <div style="background: linear-gradient(135deg, #2ecc71 0%, #27ae60 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
                <h3 style="color: #fff; margin: 0 0 15px 0;">?? Casa Creada Exitosamente</h3>
                
                <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                    <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">?? Detalles de la Propiedad</h4>
                    <p style="margin: 5px 0; line-height: 1.6;"><strong>Dirección:</strong> {homeData.Direccion}</p>
                    <p style="margin: 5px 0; line-height: 1.6;"><strong>Ciudad:</strong> {homeData.Ciudad}, {homeData.Estado}</p>
                    <p style="margin: 5px 0; line-height: 1.6;"><strong>Tipo:</strong> {homeData.TipoPropiedad} ({homeData.Tipo})</p>
                    <p style="margin: 5px 0; line-height: 1.6;"><strong>Área:</strong> {homeData.AreaTotal} pies²</p>
                    <p style="margin: 5px 0; line-height: 1.6;"><strong>Habitaciones:</strong> {homeData.Habitaciones} | <strong>Baños:</strong> {homeData.Banos}</p>
                </div>

                <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                    <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">? Estado</h4>
                    <p style="margin: 0; line-height: 1.6;">
                        Tu propiedad ha sido registrada exitosamente en tu portafolio inmobiliario.
                        {(homeData.EsPrincipal ? " Se ha marcado como tu vivienda principal." : "")}
                    </p>
                </div>
                
                <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                    ?? ID: {homeData.Id} • ?? Twin: {twinId}
                </div>
            </div>
            """;
        }
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
            _logger.LogInformation("?? Initializing Semantic Kernel for AgenteHomes");

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

            _logger.LogInformation("? Semantic Kernel initialized successfully for AgenteHomes");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize Semantic Kernel for AgenteHomes");
            throw;
        }

        await Task.CompletedTask;
    }
}

// ========================================
// MODELS Y RESPONSE CLASSES
// ========================================

/// <summary>
/// Request para crear una nueva casa
/// </summary>
public class CreateHomeRequest
{
    // Información básica requerida
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Estado { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Tipo { get; set; } // actual, pasado, inversion, vacacional
    public string? TipoPropiedad { get; set; } // casa, apartamento, condominio, townhouse, otro
    
    // Fechas
    public string? FechaInicio { get; set; } // YYYY-MM-DD
    public string? FechaFin { get; set; } // YYYY-MM-DD | null
    
    // Características de la propiedad
    public bool EsPrincipal { get; set; } = false;
    public double AreaTotal { get; set; } = 0;
    public int Habitaciones { get; set; } = 0;
    public int Banos { get; set; } = 0;
    public int MedioBanos { get; set; } = 0;
    public int AnoConstructorcion { get; set; } = 0;
    
    // Información adicional
    public string? Descripcion { get; set; }
    public double? ValorEstimado { get; set; }
    
    // Información financiera
    public double? ImpuestosPrediales { get; set; }
    public double? HoaFee { get; set; }
    public bool TieneHOA { get; set; } = false;
    
    // Ubicación y características
    public string? Vecindario { get; set; }
    public List<string> AspectosPositivos { get; set; } = new();
    public List<string> AspectosNegativos { get; set; } = new();
    public List<string> Fotos { get; set; } = new();
}

/// <summary>
/// Resultado de validación de datos de casa
/// </summary>
public class HomeValidationResult
{
    public bool IsValid { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public CreateHomeRequest EnrichedRequest { get; set; } = new();
    public List<string> EnrichmentSuggestions { get; set; } = new();
    public string PropertyInsights { get; set; } = "";
    public string EstimatedValueRange { get; set; } = "";
    public string NeighborhoodScore { get; set; } = "";
}

/// <summary>
/// Respuesta del AgenteHomes
/// </summary>
public class HomesAIResponse
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
    /// Datos de la casa procesada
    /// </summary>
    public HomeData? HomeData { get; set; }

    /// <summary>
    /// Respuesta HTML generada por AI
    /// </summary>
    public string AIResponse { get; set; } = string.Empty;

    /// <summary>
    /// Resultados de validación
    /// </summary>
    public HomeValidationResult? ValidationResults { get; set; }

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
    /// Obtiene un resumen de la respuesta para logging
    /// </summary>
    public string GetSummary()
    {
        if (!Success)
        {
            return $"? Error: {Error}";
        }

        return $"? Success: {Operation} completed, {ProcessingTimeMs:F0}ms";
    }

    /// <summary>
    /// Determina si la respuesta contiene información útil
    /// </summary>
    public bool HasUsefulContent => Success && HomeData != null && !string.IsNullOrEmpty(AIResponse);
}