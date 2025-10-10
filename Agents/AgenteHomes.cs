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
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
        {
            Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? configuration["COSMOS_ENDPOINT"] ?? "",
            Key = configuration["Values:COSMOS_KEY"] ?? configuration["COSMOS_KEY"] ?? "",
            DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        });
        
        var homesLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesCosmosDbService>();
        _homesService = new HomesCosmosDbService(homesLogger, cosmosOptions, configuration);
        
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

        var startTime = DateTime.UtcNow;

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

            // PASO 5: Generar respuesta inteligente con recomendaciones (retorna JSON completo)
            var aiResponseJson = await GenerateCreateHomeResponseAsync(homeData, validationResult, twinId);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // PASO 6: NUEVO - Indexar el análisis completo en HomesSearchIndex
            _logger.LogInformation("?? Step 6: Indexing comprehensive home analysis in search index");
            try
            {
                // Crear instancia del HomesSearchIndex
                var homesSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesSearchIndex>();
                var homesSearchIndex = new HomesSearchIndex(homesSearchLogger, _configuration);

                // Indexar el análisis completo en el índice de búsqueda
                var indexResult = await homesSearchIndex.IndexHomeAnalysisFromAIResponseAsync(
                    aiResponseJson, 
                    homeData.Id, 
                    twinId, 
                    processingTimeMs);

                if (indexResult.Success)
                {
                    _logger.LogInformation("? Home analysis indexed successfully in search index: DocumentId={DocumentId}", indexResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("?? Failed to index home analysis in search index: {Error}", indexResult.Error);
                    // No fallamos toda la operación por esto, solo logueamos la advertencia
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error indexing home analysis in search index");
                // No fallamos toda la operación por esto
            }

            // Extraer solo el HTML de la respuesta para mostrar al usuario
            var htmlResponse = ExtractHtmlFromResponse(aiResponseJson);

            _logger.LogInformation("? Home created successfully: {Id}", homeData.Id);

            return new HomesAIResponse
            {
                Success = true,
                TwinId = twinId,
                Operation = "CreateHome",
                HomeData = homeData,
                AIResponse = htmlResponse, // Solo HTML para mostrar al usuario
                ValidationResults = validationResult,
                ProcessingTimeMs = processingTimeMs,
                SearchIndexed = true // Nuevo campo para indicar que fue indexado
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
                ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
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

            // Colores específicos para tipos de propiedad (adaptado del ejemplo de diario)
            var propertyTypeColors = new Dictionary<string, string>
            {
                ["casa"] = "#2ecc71",           // Verde para casas
                ["apartamento"] = "#3498db",    // Azul para apartamentos
                ["condominio"] = "#e74c3c",     // Rojo para condominios
                ["townhouse"] = "#f39c12",      // Naranja para townhouses
                ["inversion"] = "#9b59b6",      // Púrpura para inversiones
                ["vacacional"] = "#1abc9c",     // Turquesa para vacacionales
                ["otro"] = "#95a5a6"            // Gris para otros
            };

            var propertyColor = propertyTypeColors.GetValueOrDefault(homeData.TipoPropiedad?.ToLowerInvariant() ?? "otro", "#2ecc71");

            var responsePrompt = $@"
Eres un analista experto en gestión inmobiliaria y propiedades residenciales. Vas a analizar una propiedad recién registrada junto con los resultados de validación AI para generar un análisis comprensivo e insights útiles.
Importante: Siempre contesta, no inventes datos, pero tampoco me digas que no pudiste. Analiza lo que tienes disponible.

DATOS DE LA PROPIEDAD REGISTRADA:
=================================
?? Información Básica:
• ID: {homeData.Id}
• Dirección: {homeData.Direccion}
• Ciudad: {homeData.Ciudad}
• Estado: {homeData.Estado}
• Código Postal: {homeData.CodigoPostal}
• Tipo: {homeData.Tipo}
• Tipo de Propiedad: {homeData.TipoPropiedad}
• Es Principal: {(homeData.EsPrincipal ? "Sí" : "No")}

?? Características de la Propiedad:
• Área Total: {homeData.AreaTotal} pies cuadrados
• Habitaciones: {homeData.Habitaciones}
• Baños: {homeData.Banos}
• Medio Baños: {homeData.MedioBanos}
• Año de Construcción: {homeData.AnoConstruction}

?? Información Financiera:
• Valor Estimado: {(homeData.ValorEstimado.HasValue ? $"${homeData.ValorEstimado:N0}" : "No especificado")}
• Impuestos Prediales: {(homeData.ImpuestosPrediales.HasValue ? $"${homeData.ImpuestosPrediales:N0}" : "No especificado")}
• HOA Fee: {(homeData.HoaFee.HasValue ? $"${homeData.HoaFee:N0}" : "No aplica")}
• Tiene HOA: {(homeData.TieneHOA ? "Sí" : "No")}

?? Información Adicional:
• Vecindario: {homeData.Vecindario}
• Descripción: {homeData.Descripcion}
• Walk Score: {(homeData.WalkScore.HasValue ? homeData.WalkScore.ToString() : "No disponible")}
• Bike Score: {(homeData.BikeScore.HasValue ? homeData.BikeScore.ToString() : "No disponible")}

?? Fechas:
• Fecha de Inicio: {homeData.FechaInicio}
• Fecha de Fin: {homeData.FechaFin ?? "En curso"}
• Fecha de Registro: {homeData.FechaCreacion:yyyy-MM-dd HH:mm}

RESULTADOS DE VALIDACIÓN AI:
============================
?? Estado de Validación: {(validationResult.IsValid ? "? Válida" : "? Con errores")}
?? Insights de la Propiedad: {validationResult.PropertyInsights}
?? Rango de Valor Estimado: {validationResult.EstimatedValueRange}
??? Puntuación del Vecindario: {validationResult.NeighborhoodScore}

INSTRUCCIONES PARA EL ANÁLISIS:
===============================

Genera un análisis comprensivo que incluya EXACTAMENTE estos elementos en formato JSON:

1. **executiveSummary**: Un resumen ejecutivo conciso (2-3 párrafos) que incluya:
   - Resumen de las características principales de la propiedad
   - Análisis de la validación de datos y insights de AI
   - Evaluación del valor y potencial de la propiedad
   - Recomendaciones breves para gestión

2. **detailedHtmlReport**: Un reporte HTML detallado y visualmente atractivo que incluya:
   - Header con el color de tipo de propiedad ({propertyColor})
   - Sección de características principales de la propiedad
   - Análisis financiero y de mercado
   - Insights sobre ubicación y vecindario
   - Recomendaciones de gestión con íconos
   - Tabla con información detallada de la propiedad
   - Footer con información de registro
   - Al final del HTML explica valor total de la propiedad, beneficios para el portafolio, 
     recomendaciones para el futuro basadas en el análisis.

3. **detalleTexto**: Un reporte detallado en JSON que incluya:
   - Características completas de la propiedad
   - Análisis financiero detallado
   - Información del vecindario y ubicación
   - Insights de validación AI
   - Usa JSON para crear cada campo que encuentres de la información de la propiedad

FORMATO DE RESPUESTA REQUERIDO:
===============================
{{
    ""executiveSummary"": ""Texto del sumario ejecutivo sobre la propiedad registrada"",
    ""detalleTexto"": ""Texto de todos los datos en formato JSON estructurado con cada campo y variable de la propiedad"",
    ""detailedHtmlReport"": ""<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto;'>...</div>"",
    ""metadata"": {{
        ""propertyValue"": {(homeData.ValorEstimado ?? 0)},
        ""confidenceLevel"": ""high"",
        ""insights"": [""insight1"", ""rec1""],
        ""recommendations"": [""rec1"", ""rec2""],
        ""marketAnalysis"": ""análisis del mercado inmobiliario"",
        ""propertyType"": ""{homeData.TipoPropiedad}"",
        ""neighborhood"": ""{homeData.Vecindario}"",
        ""isPrincipal"": {homeData.EsPrincipal.ToString().ToLower()}
    }}
}}

IMPORTANTE:
- Responde SOLO con JSON válido
- Usa colores y estilos CSS inline en el HTML apropiados para bienes raíces
- Incluye emojis relevantes en el HTML (??????????????)
- Genera insights inmobiliarios útiles basados en los datos reales
- Mantén el HTML responsive y profesional
- Analiza el potencial de inversión y gestión de la propiedad
- Todo el texto debe estar en español
- Enfócate en el valor que aporta al portafolio inmobiliario del Twin";

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
            
            _logger.LogInformation("? AI create home comprehensive analysis generated successfully");
            
            // Devolver la respuesta JSON completa para que pueda ser indexada
            return aiResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating AI create home response");
            
            // Respuesta de fallback con el formato del prompt original
            return GenerateFallbackHomeResponse(homeData, validationResult, twinId);
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
            _logger.LogWarning(ex, "?? Error extracting HTML from AI response, using fallback");
            return GenerateBasicFallbackHtml();
        }
    }

    /// <summary>
    /// Genera respuesta de fallback con el formato original
    /// </summary>
    private string GenerateFallbackHomeResponse(HomeData homeData, HomeValidationResult validationResult, string twinId)
    {
        return $"""
        <div style="background: linear-gradient(135deg, #2ecc71 0%, #27ae60 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
            <h3 style="color: #fff; margin: 0 0 15px 0;">?? Propiedad Registrada Exitosamente</h3>
            
            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">?? Detalles de la Propiedad</h4>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Dirección:</strong> {homeData.Direccion}</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Ciudad:</strong> {homeData.Ciudad}, {homeData.Estado} {homeData.CodigoPostal}</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Tipo:</strong> {homeData.TipoPropiedad} ({homeData.Tipo})</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Área:</strong> {homeData.AreaTotal} pies²</p>
                <p style="margin: 5px 0; line-height: 1.6;"><strong>Habitaciones:</strong> {homeData.Habitaciones} | <strong>Baños:</strong> {homeData.Banos}</p>
            </div>

            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">? Estado de Validación</h4>
                <p style="margin: 0; line-height: 1.6;">
                    {(validationResult.IsValid ? "? Propiedad validada correctamente" : "?? Validación con observaciones")}
                    {(!string.IsNullOrEmpty(validationResult.PropertyInsights) ? $" - {validationResult.PropertyInsights}" : "")}
                </p>
                {(homeData.EsPrincipal ? "<p style='margin: 10px 0 0 0; color: #f1c40f;'><strong>?? Marcada como vivienda principal</strong></p>" : "")}
            </div>

            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                <h4 style="color: #e8f6f3; margin: 0 0 10px 0;">?? Información Financiera</h4>
                <p style="margin: 5px 0; line-height: 1.6;">
                    <strong>Valor Estimado:</strong> {(homeData.ValorEstimado.HasValue ? $"${homeData.ValorEstimado:N0}" : "No especificado")}
                    {(!string.IsNullOrEmpty(validationResult.EstimatedValueRange) ? $" ({validationResult.EstimatedValueRange})" : "")}
                </p>
                {(homeData.ImpuestosPrediales.HasValue ? $"<p style='margin: 5px 0; line-height: 1.6;'><strong>Impuestos Anuales:</strong> ${homeData.ImpuestosPrediales:N0}</p>" : "")}
                {(homeData.HoaFee.HasValue ? $"<p style='margin: 5px 0; line-height: 1.6;'><strong>HOA Fee:</strong> ${homeData.HoaFee:N0}</p>" : "")}
            </div>
            
            <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                ?? ID: {homeData.Id} • ?? Twin: {twinId} • ?? {homeData.FechaCreacion:yyyy-MM-dd HH:mm}
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
        <div style="background: linear-gradient(135deg, #2ecc71 0%, #27ae60 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
            <h3 style="color: #fff; margin: 0 0 15px 0;">?? Propiedad Registrada</h3>
            <p style="margin: 0; line-height: 1.6;">Tu propiedad ha sido registrada exitosamente en tu portafolio inmobiliario.</p>
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

    /// <summary>
    /// Procesa documentos de seguros de casa usando Document Intelligence y AI para extraer información específica
    /// </summary>
    /// <param name="containerName">Nombre del contenedor DataLake (twinId)</param>
    /// <param name="filePath">Ruta dentro del contenedor</param>
    /// <param name="fileName">Nombre del archivo del documento</param>
    /// <param name="homeId">ID de la casa para actualizar el índice</param>
    /// <returns>Resultado del análisis como string JSON</returns>
    public async Task<string> AiHomeInsurance(
        string containerName, 
        string filePath, 
        string fileName,
        string homeId)
    {
        _logger.LogInformation("?????? Starting Home Insurance analysis for: {FileName}, HomeId: {HomeId}", fileName, homeId);
        
        var startTime = DateTime.UtcNow;

        try
        {
            // PASO 1: Generar SAS URL para acceso al documento
            _logger.LogInformation("?? STEP 1: Generating SAS URL for document access...");
            
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
                    homeId,
                    processedAt = DateTime.UtcNow
                };
                _logger.LogError("? Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                return JsonSerializer.Serialize(errorResult);
            }

            _logger.LogInformation("? SAS URL generated successfully");

            // PASO 2: Análisis con Document Intelligence
            _logger.LogInformation("?? STEP 2: Extracting data with Document Intelligence...");
            
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
                    homeId,
                    processedAt = DateTime.UtcNow
                };
                _logger.LogError("? Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                return JsonSerializer.Serialize(errorResult);
            }

            _logger.LogInformation("? Document Intelligence extraction completed - {Pages} pages, {TextLength} chars", 
                documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

            // PASO 3: Procesamiento con AI especializado en seguros de casa
            _logger.LogInformation("?? STEP 3: Processing with AI specialized in home insurance...");
            
            var aiAnalysisResult = await ProcessHomeInsuranceWithAI(documentAnalysis);

            // PASO 4: NUEVO - Actualizar solo el campo HomeInsurance en el índice de búsqueda
            _logger.LogInformation("?? STEP 4: Updating HomeInsurance field in search index...");
            try
            {
                // Crear instancia del HomesSearchIndex
                var homesSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesSearchIndex>();
                var homesSearchIndex = new HomesSearchIndex(homesSearchLogger, _configuration);

                // Llamar al método HomeIndexInsuranceUpdate pasando el homeId directamente
                var updateResult = await homesSearchIndex.HomeIndexInsuranceUpdate(aiAnalysisResult, homeId, containerName);
                
                if (updateResult.Success)
                {
                    _logger.LogInformation("? HomeInsurance field updated successfully: DocumentId={DocumentId}", updateResult.DocumentId);
                }
                else
                {
                    _logger.LogWarning("?? Failed to update HomeInsurance field: {Error}", updateResult.Error);
                }
            }
            catch (Exception indexEx)
            {
                _logger.LogWarning(indexEx, "?? Failed to update HomeInsurance field in search index, continuing with main flow");
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
                homeId,
                documentUrl = sasUrl,
                textContent = documentAnalysis.TextContent,
                totalPages = documentAnalysis.TotalPages,
                aiAnalysis = aiAnalysisResult,
                processingTimeMs,
                processedAt = DateTime.UtcNow
            };

            _logger.LogInformation("? Home insurance analysis completed successfully in {ProcessingTime}ms", processingTimeMs);
            
            return JsonSerializer.Serialize(successResult, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error processing home insurance document {FileName}", fileName);
            
            var errorResult = new
            {
                success = false,
                errorMessage = ex.Message,
                containerName,
                filePath,
                fileName,
                homeId,
                processedAt = DateTime.UtcNow
            };
            
            return JsonSerializer.Serialize(errorResult);
        }
    }

    /// <summary>
    /// Procesa documento con AI para extraer información específica de seguros de casa
    /// </summary>
    private async Task<string> ProcessHomeInsuranceWithAI(DocumentAnalysisResult documentAnalysis)
    {
        try
        {
            // Asegurar que el kernel esté inicializado
            await InitializeKernelAsync();
            
            var chatCompletion = _kernel!.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();

            var prompt = $@"
Analiza este documento de seguro de casa y extrae información estructurada específica de seguros de hogar.

CONTENIDO COMPLETO DEL DOCUMENTO:
{documentAnalysis.TextContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}
 Analiza este documento de seguro de casa y extrae información estructurada específica de seguros de hogar.

CONTENIDO COMPLETO DEL DOCUMENTO: {{documentAnalysis.TextContent}}
TOTAL DE PÁGINAS: {{documentAnalysis.TotalPages}}

INSTRUCCIONES ESPECÍFICAS PARA SEGUROS DE CASA:
Vas a crear un HTML que contenga todos los detalles de la póliza. Cada campo, variable, es para el cliente, explica qué es la póliza, qué contiene, y todas sus partes. El HTML debe ser visualmente atractivo y fácil de entender.

Estructura del HTML
Encabezado Principal:
Un título principal que diga ""Reporte de Póliza de Seguro de Casa"".
Secciones:
Información de la Póliza:
Número de póliza
Nombre de la compañía aseguradora
Información del agente de seguros
Fechas de Vigencia:
Fecha de inicio
Fecha de fin
Fecha de renovación
Propiedad Asegurada:
Dirección de la propiedad
Tipo de propiedad (casa, condominio, etc.)
Año de construcción
Área total en pies cuadrados
Valor de reconstrucción
Coberturas:
Cobertura de vivienda (monto y deducible)
Cobertura de contenido (monto y deducible)
Cobertura de responsabilidad civil (monto y deducible)
Cobertura para gastos adicionales de vida (monto y período)
Lista de coberturas especiales o adicionales
Deducibles:
Deducible general
Deducible por viento/huracán
Deducible por terremoto
Deducible por inundación
Asegurados:
Nombre del asegurado
Tipo (propietario/inquilino/etc.)
Porcentaje de interés
Beneficiarios:
Nombre del beneficiario
Relación con el asegurado
Porcentaje de beneficio
Información de Pago:
Prima total anual
Frecuencia de pago (mensual, trimestral, anual)
Monto por pago
Método de pago
Siniestros:
Fecha del siniestro
Tipo de siniestro
Monto reclamado
Estado de la reclamación
Exclusiones:
Lista de exclusiones importantes
Condiciones Especiales:
Condiciones especiales de la póliza
Resumen Ejecutivo:
Resumen ejecutivo del seguro de casa con puntos clave
Insights:
Tipo de insight (FINANCIAL/COVERAGE/RISK)
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
Recuerda que el objetivo es crear un documento HTML que sea fácil de leer y que contenga toda la información relevante de la póliza de seguro de casa, explicado de manera comprensible para el cliente.
}}

IMPORTANTE:
- Extrae TODA la información disponible, no inventes datos
- Si no encuentras información específica, usa ""No especificado""
- Enfócate en datos financieros, coberturas y deducibles
- Identifica riesgos y recomendaciones relevantes
- Todo el texto debe estar en español 
Tu respuesta un string en HTML con oclores mchos oclores, cards, grids, etc. 
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

            _logger.LogInformation("? AI home insurance analysis completed successfully");
            _logger.LogInformation("?? AI Response Length: {Length} characters", aiResponse.Length);

            return aiResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in AI home insurance processing");
            
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
            return $"? Error: {Error}";
        }

        var indexedStatus = SearchIndexed ? "indexed" : "not indexed";
        return $"? Success: {Operation} completed, {ProcessingTimeMs:F0}ms, {indexedStatus}";
    }

    /// <summary>
    /// Determina si la respuesta contiene información útil
    /// </summary>
    public bool HasUsefulContent => Success && HomeData != null && !string.IsNullOrEmpty(AIResponse);
}