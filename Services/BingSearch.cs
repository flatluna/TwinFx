using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Services;

/// <summary>
/// Servicio especializado en búsquedas inteligentes con Bing y Azure OpenAI
/// ========================================================================
/// 
/// Este servicio utiliza:
/// - Búsqueda en Bing usando Azure AI Agents con Bing Grounding
/// - Búsqueda directa con Bing Search API v7 como fallback
/// - Semantic Kernel con Azure OpenAI para perfeccionar respuestas
/// - Procesamiento inteligente de resultados de búsqueda
/// - Respuestas contextualizadas y mejoradas por IA
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class BingSearch
{
    private readonly ILogger<BingSearch> _logger;
    private readonly IConfiguration _configuration;
    private Kernel? _kernel;
    private static readonly HttpClient _httpClient = new HttpClient();

    // Bing Search API configuration (Direct HTTP approach)
    private const string BING_SEARCH_API_KEY = "ac0e3b90ba204e6ea437e4f87680998c";
    private const string BING_SEARCH_URL = "https://api.bing.microsoft.com/v7.0/search";

    // Azure AI Foundry configuration for Bing Grounding
    private const string PROJECT_ENDPOINT = "https://twinet-resource.services.ai.azure.com/api/projects/twinet";
    private const string MODEL_DEPLOYMENT_NAME = "gpt4mini";
    private const string BING_CONNECTION_ID = "/subscriptions/bf5f11e8-1b22-4e27-b55e-8542ff6dec42/resourceGroups/rg-jorgeluna-7911/providers/Microsoft.CognitiveServices/accounts/twinet-resource/projects/twinet/connections/twinbing";

    public BingSearch(ILogger<BingSearch> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _logger.LogInformation("🔍 BingSearch service initialized for intelligent web search with AI enhancement");
    }

    /// <summary>
    /// Realiza una búsqueda inteligente combinando Bing Search y Azure OpenAI
    /// </summary>
    /// <param name="question">Pregunta del usuario</param>
    /// <param name="twinId">ID del Twin (opcional)</param>
    /// <returns>Respuesta inteligente mejorada por IA</returns>
    public async Task<BingSearchResponse> ProcessIntelligentSearchAsync(string question, string? twinId = null)
    {
        _logger.LogInformation("🔍 Processing intelligent search for question: {Question}", question);
        var startTime = DateTime.UtcNow;

        try
        {
            // Validar inputs básicos
            if (string.IsNullOrEmpty(question))
            {
                return new BingSearchResponse
                {
                    Success = false,
                    Error = "Question parameter is required",
                    Question = question,
                    TwinId = twinId
                };
            }

            // Inicializar Semantic Kernel
            await InitializeKernelAsync();

            // STEP 1: Realizar búsqueda en Bing
            _logger.LogInformation("🔍 Performing Bing search for: {Question}", question);
            var searchResults = await BingSearchAsync(question);

            if (searchResults == null || string.IsNullOrEmpty(searchResults.Respuesta))
            {
                _logger.LogWarning("⚠️ No search results found for question: {Question}", question);
                return CreateNoResultsResponse(question, twinId, "No se encontraron resultados en la búsqueda");
            }

            // STEP 2: Procesar los resultados con Azure OpenAI
            _logger.LogInformation("🤖 Processing search results with Azure OpenAI");
         //   var enhancedResponse = await EnhanceSearchResultsWithAI(question, searchResults.Respuesta ?? "", twinId);

           var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("✅ Intelligent search completed successfully in {Time}ms", processingTimeMs);

            return new BingSearchResponse
            {
                Success = true,
                Question = question,
                TwinId = twinId,
                CursoBusqueda = searchResults, 
                ProcessingTimeMs = processingTimeMs,
                ProcessedAt = DateTime.UtcNow,
                Disclaimer = "Esta información proviene de fuentes web y ha sido procesada por IA. Verifica la información en fuentes oficiales antes de tomar decisiones importantes."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in intelligent search for question: {Question}", question);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return new BingSearchResponse
            {
                Success = false,
                Error = ex.Message,
                Question = question,
                TwinId = twinId,
                ProcessingTimeMs = processingTimeMs,
                EnhancedAnswer = $"""
                ❌ **Error procesando tu búsqueda**
                
                🔴 **Error:** {ex.Message}
                
                💡 **Sugerencias:**
                • Intenta reformular tu pregunta
                • Asegúrate de que la consulta sea específica
                • Contacta al soporte técnico si el problema persiste
                """,
                Disclaimer = "Hubo un error técnico procesando tu búsqueda. Por favor intenta nuevamente."
            };
        }
    }

    /// <summary>
    /// Realizar búsqueda en Bing usando Azure AI Agents con Bing Grounding o API directa
    /// </summary>
    /// <param name="searchQuery">Término de búsqueda</param>
    /// <returns>Resultados de búsqueda formateados</returns>
    public async Task<CursoBusqueda> BingSearchAsync(string searchQuery)
    {
        _logger.LogInformation("🔍 Performing Bing Search for: {SearchQuery}", searchQuery);

        try
        {
            if (string.IsNullOrEmpty(searchQuery))
            {
                return null;
            }

            // Try Azure AI Agents with Bing Grounding first
            try
            {
                return await BingGroundingSearchLearnAsync(searchQuery);
            }
            catch (Exception groundingEx)
            {
                // Check if it's the specific AML connections error
                if (groundingEx.Message.Contains("AML connections are required") ||
                    groundingEx.Message.Contains("missing_required_parameter"))
                {
                    _logger.LogInformation("💡 AML connections not configured for Bing Grounding - using direct Bing API fallback");
                }
                else
                {
                    _logger.LogWarning(groundingEx, "⚠️ Bing Grounding failed, falling back to direct Bing Search API");
                }

                // For now, return null since direct API doesn't return CursoBusqueda
                // You might want to implement conversion from direct API to CursoBusqueda later
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ All Bing Search methods failed for query: {SearchQuery}", searchQuery);
            return null;
        }
    }
    public async Task<GlobalResponse> BingSearchGlobalAsync(string searchQuery)
    {
        _logger.LogInformation("🔍 Performing Bing Search for: {SearchQuery}", searchQuery);

        try
        {
            if (string.IsNullOrEmpty(searchQuery))
            {
                return null;
            }

            // Try Azure AI Agents with Bing Grounding first
            try
            {
                return await BingGroundingSearchGlobalLearnAsync(searchQuery);
            }
            catch (Exception groundingEx)
            {
                // Check if it's the specific AML connections error
                if (groundingEx.Message.Contains("AML connections are required") ||
                    groundingEx.Message.Contains("missing_required_parameter"))
                {
                    _logger.LogInformation("💡 AML connections not configured for Bing Grounding - using direct Bing API fallback");
                }
                else
                {
                    _logger.LogWarning(groundingEx, "⚠️ Bing Grounding failed, falling back to direct Bing Search API");
                }

                // For now, return null since direct API doesn't return CursoBusqueda
                // You might want to implement conversion from direct API to CursoBusqueda later
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ All Bing Search methods failed for query: {SearchQuery}", searchQuery);
            return null;
        }
    }

    /// <summary>
    /// Búsqueda usando Azure AI Agents con Bing Grounding
    /// </summary>
    private async Task<CursoBusqueda> BingGroundingSearchLearnAsync(string searchQuery)
    {
        _logger.LogInformation("🔧 Attempting Bing Grounding Search with Azure AI Agents");
        CursoBusqueda cursosEncontrado = new CursoBusqueda();
        // Step 1: Create a client object
        var agentClient = new PersistentAgentsClient(PROJECT_ENDPOINT, new DefaultAzureCredential());

        // Step 2: Create an Agent with the Grounding with Bing search tool enabled
        var bingGroundingTool = new BingGroundingToolDefinition(
            new BingGroundingSearchToolParameters(
                [new BingGroundingSearchConfiguration(BING_CONNECTION_ID)]
            )
        );

        var agent = await agentClient.Administration.CreateAgentAsync(
            model: MODEL_DEPLOYMENT_NAME,
            name: "general-search-agent",
            instructions: "Use the bing grounding tool to search for comprehensive information. Provide detailed, accurate information with sources. Focus on current and relevant information.",
            tools: [bingGroundingTool]
        );

        // Step 3: Create a thread and run
        var thread = await agentClient.Threads.CreateThreadAsync();
        var enhancementPrompt = $$$"""
                🤖 **Asistente Inteligente de Búsqueda Web**
                Eres un experto analista que procesa información web y proporciona respuestas estructuradas y útiles.

                **CONTEXTO DE BÚSQUEDA:**
                Pregunta del usuario: "{{{searchQuery}}}"
                
 

                **INSTRUCCIONES:**
                Analiza los resultados de búsqueda y proporciona una respuesta estructurada en JSON con esta estructura exacta:

                {
                {  
                  "cursosEncontrados":[ {  
                    "nombreClase": "Nombre de la Clase",  
                    "instructor": "Nombre del Instructor",  
                    "plataforma": "Plataforma (ej. Coursera, Udemy)",  
                    "categoria": "Categoría (ej. Programación, Marketing)",  
                    "duracion": "Duración (ej. 4 semanas, 10 horas)",  
                    "requisitos": "Requisitos previos (ej. Conocimientos básicos de programación)",  
                    "loQueAprendere": "Lo que aprenderé (ej. Fundamentos de Python)",  
                    "precio": "Precio (ej. $49, gratuito)",  
                    "recursos": "Recursos adicionales (ej. libros, artículos)",  
                    "idioma": "Idioma de instrucción (ej. Español, Inglés)",  
                    "fechaInicio": "Fecha de inicio (ej. 01/01/2024)",  
                    "fechaFin": "Fecha de finalización (ej. 31/01/2024)", 
                    "habilidadesCompetencias":"",
                    "ObjetivosdeAprendizaje:"",
                    "Prerequisitos","",
                    "enlaces": {  
                      "enlaceClase": "https://enlace-a-la-clase.com",  
                      "enlaceInstructor": "https://enlace-al-instructor.com",  
                      "enlacePlataforma": "https://enlace-a-la-plataforma.com",  
                      "enlaceCategoria": "https://enlace-a-la-categoria.com"  
                    }  ],
                  }, 
                  "htmlDetalles": "<div>HTML estructurado con los detalles de las clases</div>",
                  "respuesta": "Respuesta completa y detallada basada en los resultados de búsqueda",  
                  "resumen": "Resumen ejecutivo de 2-3 líneas con los puntos más importantes",  
                  "puntosClaves": [  
                    "Punto clave 1: Información relevante extraída.",  
                    "Punto clave 2: Otro dato importante.",  
                    "Punto clave 3: Conclusión o información adicional."  
                  ],  
                  "enlaces": [  
                    "https://fuente1.com",  
                    "https://fuente2.com"  
                  ],  
                  "accionesRecomendadas": [  
                    "Acción 1: Sugerencia práctica basada en la información.",  
                    "Acción 2: Próximo paso recomendado.",  
                    "Acción 3: Recurso adicional o verificación sugerida."  
                  ]  
                }  

                **CRITERIOS DE CALIDAD:**
                - Usa SOLO la información encontrada en los resultados de búsqueda
                - Si la información es limitada, menciona qué aspectos necesitan más investigación
                - Incluye datos específicos, fechas, números cuando estén disponibles
                - Cita fuentes cuando sea relevante
                - Proporciona contexto útil para entender la información
                - Sugiere acciones prácticas basadas en los hallazgos
                - dame los urls de los cursos encvontrados
                - no adiciones mas comentarios todo en JSON solamente
                - busca cursos gratis en MIT, Standford
                - quiero unas 5 opciones gratis 5 5 que cuesten
                - busca por Prerequisitos y por Habilidades y Competencias
                - cuales son Objetivos de Aprendizaje?
                - no inventes nada si no encuentras los datos que se te pidieron esta ok entiendo.
                - no comiences con ```json o termines con ```
               

                **FORMATO:**
                Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después.
                """;
        var message = await agentClient.Messages.CreateMessageAsync(
            thread.Value.Id,
            MessageRole.User,
            $"Search for comprehensive information about: {searchQuery}. Include relevant details," +
            $" current information, and provide sources when possible very imprtant " +
            $"give me all the links for each class., instructor, platform, category," +
            $" duration, requirements, what I will learn, price, resources, language. start date and end date" +          
            enhancementPrompt);

        var run = await agentClient.Runs.CreateRunAsync(thread.Value.Id, agent.Value.Id);

        // Step 4: Wait for the agent to complete
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            run = await agentClient.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
        }
        while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress);

        if (run.Value.Status != RunStatus.Completed)
        {
            throw new InvalidOperationException($"Bing Grounding run failed: {run.Value.LastError?.Message}");
        }

        // Step 5: Retrieve and process the messages
        var messages = agentClient.Messages.GetMessagesAsync(
            threadId: thread.Value.Id,
            order: ListSortOrder.Ascending
        );

        var searchResults = new List<string>();

        await foreach (var threadMessage in messages)
        {
            if (threadMessage.Role != MessageRole.User)
            {
                foreach (var contentItem in threadMessage.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                    {
                        string response = textItem.Text;

                        if (textItem.Annotations != null)
                        {
                            foreach (var annotation in textItem.Annotations)
                            {
                                if (annotation is MessageTextUriCitationAnnotation urlAnnotation)
                                {
                                    response = response.Replace(urlAnnotation.Text,
                                        $" [{urlAnnotation.UriCitation.Title}]({urlAnnotation.UriCitation.Uri})");
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(response))
                        {
                            searchResults.Add(response);
                        }
                    }
                }
            }
        }

        // Clean up resources
        try
        {
            await agentClient.Threads.DeleteThreadAsync(threadId: thread.Value.Id);
            await agentClient.Administration.DeleteAgentAsync(agentId: agent.Value.Id);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogWarning(cleanupEx, "⚠️ Warning during cleanup of Azure AI Agent resources");
        }

        if (searchResults.Count == 0)
        {
            throw new InvalidOperationException("No results found in Bing Grounding search");
        }
        try
        {
            cursosEncontrado = JsonConvert.DeserializeObject<CursoBusqueda>(searchResults[0]);
        }
        catch(Exception ex)
        {

        }
         
        return cursosEncontrado;
    }


    private async Task<GlobalResponse> BingGroundingSearchGlobalLearnAsync(string searchQuery)
    {
        _logger.LogInformation("🔧 Attempting Bing Grounding Search with Azure AI Agents");
        GlobalResponse AIResponse = new GlobalResponse();
        // Step 1: Create a client object
        var agentClient = new PersistentAgentsClient(PROJECT_ENDPOINT, new DefaultAzureCredential());

        // Step 2: Create an Agent with the Grounding with Bing search tool enabled
        var bingGroundingTool = new BingGroundingToolDefinition(
            new BingGroundingSearchToolParameters(
                [new BingGroundingSearchConfiguration(BING_CONNECTION_ID)]
            )
        );

        var agent = await agentClient.Administration.CreateAgentAsync(
            model: MODEL_DEPLOYMENT_NAME,
            name: "general-search-agent",
            instructions: "Use the bing grounding tool to search for comprehensive information. Provide detailed, accurate information with sources. Focus on current and relevant information.",
            tools: [bingGroundingTool]
        );

        // Step 3: Create a thread and run
        var thread = await agentClient.Threads.CreateThreadAsync();
        var enhancementPrompt = $$$"""
                🤖 **Asistente Inteligente de Búsqueda Web**
                Eres un experto analista que procesa información web y proporciona respuestas estructuradas y útiles.

                **CONTEXTO DE BÚSQUEDA:**
                Pregunta del usuario: "{{{searchQuery}}}"
                Responde con texto completo y detallado
                
 

                **INSTRUCCIONES:**
                Analiza los resultados de búsqueda y proporciona una respuesta estructurada en JSON con esta estructura exacta:

                {
                 "Respuesta": "Respuesta completa y detallada basada en los resultados de búsqueda",
                }
                 

                **FORMATO:**
                Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después.
                """;
        var message = await agentClient.Messages.CreateMessageAsync(
            thread.Value.Id,
            MessageRole.User,
            $"Search for comprehensive information about: {searchQuery}. Include relevant details" +
            
            enhancementPrompt);

        var run = await agentClient.Runs.CreateRunAsync(thread.Value.Id, agent.Value.Id);

        // Step 4: Wait for the agent to complete
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            run = await agentClient.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
        }
        while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress);

        if (run.Value.Status != RunStatus.Completed)
        {
            throw new InvalidOperationException($"Bing Grounding run failed: {run.Value.LastError?.Message}");
        }

        // Step 5: Retrieve and process the messages
        var messages = agentClient.Messages.GetMessagesAsync(
            threadId: thread.Value.Id,
            order: ListSortOrder.Ascending
        );

        var searchResults = new List<string>();

        await foreach (var threadMessage in messages)
        {
            if (threadMessage.Role != MessageRole.User)
            {
                foreach (var contentItem in threadMessage.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                    {
                        string response = textItem.Text;

                        if (textItem.Annotations != null)
                        {
                            foreach (var annotation in textItem.Annotations)
                            {
                                if (annotation is MessageTextUriCitationAnnotation urlAnnotation)
                                {
                                    response = response.Replace(urlAnnotation.Text,
                                        $" [{urlAnnotation.UriCitation.Title}]({urlAnnotation.UriCitation.Uri})");
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(response))
                        {
                            searchResults.Add(response);
                        }
                    }
                }
            }
        }

        // Clean up resources
        try
        {
            await agentClient.Threads.DeleteThreadAsync(threadId: thread.Value.Id);
            await agentClient.Administration.DeleteAgentAsync(agentId: agent.Value.Id);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogWarning(cleanupEx, "⚠️ Warning during cleanup of Azure AI Agent resources");
        }

        if (searchResults.Count == 0)
        {
            throw new InvalidOperationException("No results found in Bing Grounding search");
        }
        try
        {
            AIResponse= JsonConvert.DeserializeObject<GlobalResponse>(searchResults[0]);
        }
        catch (Exception ex)
        {

        }

        return AIResponse;
    }

    /// <summary>
    /// Formatear los resultados de Bing Grounding en texto estructurado
    /// </summary>

    /// Formatear los resultados de Bing Search API directa
    /// </summary>
    private string FormatBingDirectSearchResults(BingSearchApiResponse searchResult, string originalQuery)
    {
        try
        {
            var sb = new StringBuilder();

            sb.AppendLine("🔍 RESULTADOS DE BÚSQUEDA EN BING");
            sb.AppendLine("=" + new string('=', 40));
            sb.AppendLine($"📖 Búsqueda: {originalQuery}");
            sb.AppendLine($"📊 Resultados: {searchResult.WebPages?.Value?.Count ?? 0}");
            sb.AppendLine($"🤖 Powered by: Bing Search API v7");
            sb.AppendLine($"📅 Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();

            if (searchResult.WebPages?.Value != null && searchResult.WebPages.Value.Count > 0)
            {
                sb.AppendLine("📋 PRINCIPALES RESULTADOS:");
                sb.AppendLine("-" + new string('-', 30));

                for (int i = 0; i < Math.Min(searchResult.WebPages.Value.Count, 5); i++)
                {
                    var item = searchResult.WebPages.Value[i];
                    sb.AppendLine($"[{i + 1}] {item.Name}");
                    sb.AppendLine($"🔗 {item.Url}");
                    sb.AppendLine($"📄 {item.Snippet}");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error formatting Bing Direct Search results");
            return $"Error formateando los resultados de Bing: {ex.Message}";
        }
    }

    /// <summary>
    /// Crear mensaje cuando no hay resultados de Bing Search
    /// </summary>
    private string CreateBingNoResultsMessage(string originalQuery, string reason)
    {
        return $"""
            🔍 BING SEARCH - SIN RESULTADOS
            ================================
            
            📖 Búsqueda: {originalQuery}
            📊 Estado: {reason}
            💡 Acción: Usando conocimiento interno del AI
            
            📚 FUENTES ALTERNATIVAS RECOMENDADAS:
            - Sitios web oficiales relacionados con el tema
            - Bibliotecas digitales y bases de datos académicas
            - Búsqueda directa en Microsoft Bing
            - Consulta a expertos en el área específica
            
            📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
            """;
    }

    /// <summary>
    /// Crear respuesta cuando no hay resultados
    /// </summary>
    private BingSearchResponse CreateNoResultsResponse(string question, string? twinId, string reason)
    {
        return new BingSearchResponse
        {
            Success = false,
            Question = question,
            TwinId = twinId,
            Error = reason,
            EnhancedAnswer = $"""
                🔍 **Búsqueda realizada sin resultados**
                
                **Consulta:** {question}
                **Estado:** {reason}
                
                **Recomendaciones:**
                • Intenta reformular tu pregunta con términos más específicos
                • Verifica la ortografía de palabras clave
                • Usa sinónimos o términos alternativos
                • Consulta fuentes especializadas en el tema
                
                **Fuentes sugeridas:**
                • Sitios web oficiales
                • Bibliotecas digitales
                • Bases de datos académicas
                • Expertos en el área específica
                """,
            Summary = "No se encontraron resultados para la búsqueda especificada.",
            KeyInsights = new List<string>
            {
                "La búsqueda no produjo resultados relevantes",
                "Se recomienda reformular la consulta",
                "Considera usar fuentes especializadas"
            },
            RecommendedActions = new List<string>
            {
                "Reformula la pregunta con términos más específicos",
                "Verifica la ortografía y usa sinónimos",
                "Consulta fuentes especializadas en el tema"
            },
            Disclaimer = "No se encontraron resultados en la búsqueda web. Intenta con términos diferentes o consulta fuentes especializadas."
        };
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
            _logger.LogInformation("🔧 Initializing Semantic Kernel for BingSearch");

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

            _logger.LogInformation("🔍 Using deployment: {DeploymentName} for search enhancement", deploymentName);

            // Agregar Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Construir el kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized successfully for BingSearch with {DeploymentName}", deploymentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for BingSearch");
            throw;
        }

        await Task.CompletedTask;
    }
}

// ========================================
// RESPONSE MODELS
// ========================================

/// <summary>
/// Respuesta del servicio BingSearch
/// </summary>
public class BingSearchResponse
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
    /// Pregunta original del usuario
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// ID del Twin (opcional)
    /// </summary>
    public string? TwinId { get; set; }

    /// <summary>
    /// Resultados brutos de la búsqueda en Bing
    /// </summary>
    public CursoBusqueda CursoBusqueda { get; set; } = new CursoBusqueda();

    /// <summary>
    /// Respuesta mejorada y procesada por IA
    /// </summary>
    public string EnhancedAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Resumen ejecutivo de la información encontrada
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Insights clave extraídos de la información
    /// </summary>
    public List<string> KeyInsights { get; set; } = new List<string>();

    /// <summary>
    /// Acciones recomendadas basadas en la información
    /// </summary>
    public List<string> RecommendedActions { get; set; } = new List<string>();

    /// <summary>
    /// Tiempo de procesamiento en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Fecha y hora cuando se procesó la búsqueda
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Disclaimer sobre la información proporcionada
    /// </summary>
    public string Disclaimer { get; set; } = string.Empty;

    /// <summary>
    /// Obtiene un resumen de la respuesta para logging
    /// </summary>
    public string GetSummary()
    {
        if (!Success)
        {
            return $"❌ Error: {Error}";
        }

        return $"✅ Success: Search processed, {ProcessingTimeMs:F0}ms";
    }

    /// <summary>
    /// Determina si la respuesta contiene información útil
    /// </summary>
    public bool HasUsefulContent => Success && !string.IsNullOrEmpty(EnhancedAnswer) && CursoBusqueda != null && !string.IsNullOrEmpty(CursoBusqueda.Respuesta);
}

/// <summary>
/// Resultado mejorado de búsqueda procesado por IA
/// </summary>
public class EnhancedSearchResult
{
    /// <summary>
    /// Respuesta completa y detallada
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Resumen ejecutivo
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Insights clave
    /// </summary>
    public List<string> KeyInsights { get; set; } = new List<string>();

    /// <summary>
    /// Acciones recomendadas
    /// </summary>
    public List<string> RecommendedActions { get; set; } = new List<string>();
}

// ========================================
// BING SEARCH API RESPONSE MODELS
// ========================================

/// <summary>
/// Bing Search API Response Model
/// </summary>
public class BingSearchApiResponse
{
    public BingWebPages? WebPages { get; set; }
}

/// <summary>
/// Bing Search WebPages Model
/// </summary>
public class BingWebPages
{
    public string? WebSearchUrl { get; set; }
    public int TotalEstimatedMatches { get; set; }
    public List<BingSearchItem>? Value { get; set; }
}

/// <summary>
/// Bing Search Item Model
/// </summary>
public class BingSearchItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? DisplayUrl { get; set; }
    public string? Snippet { get; set; }
    public DateTime DateLastCrawled { get; set; }
    public string? Language { get; set; }
    public bool IsFamilyFriendly { get; set; }
}
 
public class CursoBusqueda
{
    [JsonProperty("cursosEncontrados")]
    public List<Curso> CursosEcontrados { get; set; } = new List<Curso>();

    [JsonProperty("htmlDetalles")]
    public string HtmlDetalles { get; set; }

    [JsonProperty("respuesta")]
    public string Respuesta { get; set; }

    [JsonProperty("resumen")]
    public string Resumen { get; set; }

    [JsonProperty("puntosClaves")]
    public List<string> PuntosClaves { get; set; }

    [JsonProperty("enlaces")]
    public List<string> Enlaces { get; set; }

    [JsonProperty("accionesRecomendadas")]
    public List<string> AccionesRecomendadas { get; set; }
}

public class Curso
{
    [JsonProperty("nombreClase")]
    public string NombreClase { get; set; }

    [JsonProperty("instructor")]
    public string Instructor { get; set; }

    [JsonProperty("plataforma")]
    public string Plataforma { get; set; }

    [JsonProperty("categoria")]
    public string Categoria { get; set; }

    [JsonProperty("duracion")]
    public string Duracion { get; set; }

    [JsonProperty("requisitos")]
    public string Requisitos { get; set; }

    [JsonProperty("loQueAprendere")]
    public string LoQueAprendere { get; set; }

    [JsonProperty("precio")]
    public string Precio { get; set; }

    [JsonProperty("recursos")]
    public string Recursos { get; set; }

    [JsonProperty("idioma")]
    public string Idioma { get; set; }

    [JsonProperty("fechaInicio")]
    public string FechaInicio { get; set; }

    [JsonProperty("fechaFin")]
    public string FechaFin { get; set; }


    [JsonProperty("habilidadesCompetencias")]
    public string HabilidadesCompetencias { get; set; }


    [JsonProperty("prerequisitos")]
    public string Prerequisitos { get; set; }

    [JsonProperty("enlaces")]
    public Enlaces Enlaces { get; set; }
    [JsonProperty("objetivosdeAprendizaje")]
    public string ObjetivosdeAprendizaje { get; set; }
}

public class GlobalResponse
{

    public string Respuesta { get; set; }
}

public class Enlaces
{
    [JsonProperty("enlaceClase")]
    public string EnlaceClase { get; set; }

    [JsonProperty("enlaceInstructor")]
    public string EnlaceInstructor { get; set; }

    [JsonProperty("enlacePlataforma")]
    public string EnlacePlataforma { get; set; }

    [JsonProperty("enlaceCategoria")]
    public string EnlaceCategoria { get; set; }
}