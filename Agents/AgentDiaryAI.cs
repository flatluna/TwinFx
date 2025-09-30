using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Agente especializado en responder preguntas sobre el diario del Twin usando búsqueda semántica
/// ========================================================================
/// 
/// Este agente utiliza Azure AI Search con capacidades vectoriales para:
/// - Búsqueda semántica en el contenido completo del diario
/// - Respuestas contextuales basadas en entradas del diario
/// - Análisis inteligente de patrones y correlaciones en el diario
/// - Solo responde preguntas relacionadas con el diario del Twin
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class AgentDiaryAI
{
    private readonly ILogger<AgentDiaryAI> _logger;
    private readonly IConfiguration _configuration;
    private readonly DiarySearchIndex _diarySearchIndex;
    private Kernel? _kernel;

    public AgentDiaryAI(ILogger<AgentDiaryAI> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Crear logger específico para DiarySearchIndex
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var diarySearchLogger = loggerFactory.CreateLogger<DiarySearchIndex>();
        _diarySearchIndex = new DiarySearchIndex(diarySearchLogger, configuration);
        
        _logger.LogInformation("?? AgentDiaryAI initialized for diary-specific question answering");
    }

    /// <summary>
    /// Procesa una pregunta sobre el diario del Twin usando búsqueda semántica y AI
    /// </summary>
    /// <param name="question">Pregunta del usuario sobre su diario</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Respuesta inteligente basada en el contenido del diario</returns>
    public async Task<DiaryAIResponse> ProcessDiaryQuestionAsync(string question, string twinId)
    {
        _logger.LogInformation("?? Processing diary question for Twin ID: {TwinId}", twinId);
        _logger.LogInformation("? Question: {Question}", question);

        try
        {
            // Validar inputs
            if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(twinId))
            {
                return new DiaryAIResponse
                {
                    Success = false,
                    Error = "Question and TwinId are required",
                    TwinId = twinId,
                    Question = question
                };
            }

            // Verificar disponibilidad del servicio de búsqueda
            if (!_diarySearchIndex.IsAvailable)
            {
                return new DiaryAIResponse
                {
                    Success = false,
                    Error = "Diary search service not available",
                    TwinId = twinId,
                    Question = question,
                    Answer = "? **Servicio de búsqueda del diario no disponible**\n\nEl servicio de Azure AI Search no está configurado correctamente."
                };
            }

            // PASO 1: Realizar búsqueda semántica en el vector del diario
            _logger.LogInformation("?? Step 1: Performing semantic search in diary content");
            
            var searchQuery = new SearchQuery
            {
                SearchText = question,
                TwinId = twinId,
                UseVectorSearch = true,  // Usar búsqueda vectorial
                UseSemanticSearch = true, // Usar búsqueda semántica 
                UseHybridSearch = false,  // Combinación de ambas
                Top = 5,  // Obtener top 5 resultados más relevantes
                Page = 1,
                SuccessfulOnly = true  // Solo entradas procesadas exitosamente
            };

            var searchResult = await _diarySearchIndex.SearchDiaryAnalysisAsync(searchQuery);

            if (!searchResult.Success)
            {
                _logger.LogWarning("?? Diary search failed: {Error}", searchResult.Error);
                return new DiaryAIResponse
                {
                    Success = false,
                    Error = $"Search failed: {searchResult.Error}",
                    TwinId = twinId,
                    Question = question,
                    Answer = $"? **Error en búsqueda del diario**\n\n?? **Error:** {searchResult.Error}"
                };
            }

            if (searchResult.Results.Count == 0)
            {
                _logger.LogInformation("?? No diary entries found for the question");
                return new DiaryAIResponse
                {
                    Success = true,
                    TwinId = twinId,
                    Question = question,
                    Answer = """
                    ?? **No se encontraron entradas relevantes del diario**
                    
                    ? **Tu pregunta:** {question}
                    
                    ?? **Posibles razones:**
                    • No hay entradas del diario que coincidan con tu pregunta
                    • Las entradas del diario aún no han sido procesadas
                    • La pregunta podría ser muy específica
                    
                    ?? **Sugerencias:**
                    • Intenta con una pregunta más general
                    • Revisa si tienes entradas en tu diario
                    • Verifica que las fechas de tus entradas estén en el rango esperado
                    """.Replace("{question}", question),
                    SearchResults = searchResult.Results,
                    TotalResults = 0
                };
            }

            _logger.LogInformation("? Found {Count} relevant diary entries", searchResult.Results.Count);

            // PASO 2: Preparar contexto con los resultados de búsqueda
            var diaryContext = BuildDiaryContext(searchResult.Results);
            
            // PASO 3: Inicializar Semantic Kernel para generar respuesta con IA
            await InitializeKernelAsync();
            
            // PASO 4: Generar respuesta inteligente usando AI
            var aiResponse = await GenerateAIResponseAsync(question, twinId, diaryContext, searchResult.Results);

            return new DiaryAIResponse
            {
                Success = true,
                TwinId = twinId,
                Question = question,
                Answer = aiResponse,
                SearchResults = searchResult.Results,
                TotalResults = searchResult.TotalCount,
                SearchType = searchResult.SearchType,
                ProcessingTimeMs = DateTime.UtcNow.Subtract(DateTime.UtcNow.AddMilliseconds(-searchResult.Results.Sum(r => r.ProcessingTimeMs))).TotalMilliseconds
            };

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error processing diary question for Twin: {TwinId}", twinId);
            return new DiaryAIResponse
            {
                Success = false,
                Error = ex.Message,
                TwinId = twinId,
                Question = question,
                Answer = $"""
                ? **Error procesando tu pregunta sobre el diario**
                
                ? **Tu pregunta:** {question}
                ?? **Error:** {ex.Message}
                
                ?? **Sugerencias:**
                • Verifica tu conexión a internet
                • Intenta con una pregunta más simple
                • Contacta al soporte técnico si el problema persiste
                """
            };
        }
    }

    /// <summary>
    /// Construye el contexto del diario basado en los resultados de búsqueda
    /// </summary>
    private string BuildDiaryContext(List<DiarySearchResultItem> searchResults)
    {
        var contextBuilder = new StringBuilder();
        
        contextBuilder.AppendLine("?? **CONTENIDO RELEVANTE DEL DIARIO:**");
        contextBuilder.AppendLine();

        for (int i = 0; i < searchResults.Count; i++)
        {
            var result = searchResults[i];
            
            contextBuilder.AppendLine($"?? **Entrada del Diario {i + 1}:**");
            contextBuilder.AppendLine($"?? **ID:** {result.DiaryEntryId}");
            contextBuilder.AppendLine($"?? **Analizado:** {result.AnalyzedAt:yyyy-MM-dd HH:mm}");
            contextBuilder.AppendLine($"? **Relevancia:** {result.SearchScore:F2}");
            
            if (!string.IsNullOrEmpty(result.ExecutiveSummary))
            {
                contextBuilder.AppendLine($"?? **Resumen Ejecutivo:**");
                contextBuilder.AppendLine(result.ExecutiveSummary);
            }
            
            // Agregar highlights si están disponibles
            if (result.Highlights.Any())
            {
                contextBuilder.AppendLine($"?? **Fragmentos Relevantes:**");
                foreach (var highlight in result.Highlights.Take(3)) // Top 3 highlights
                {
                    contextBuilder.AppendLine($"• {highlight}");
                }
            }
            
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("?????????????????????????????????????");
            contextBuilder.AppendLine();
        }

        return contextBuilder.ToString();
    }

    /// <summary>
    /// Genera respuesta inteligente usando Azure OpenAI con el contexto del diario
    /// </summary>
    private async Task<string> GenerateAIResponseAsync(string question, string twinId, string diaryContext, List<DiarySearchResultItem> searchResults)
    {
        try
        {
            _logger.LogInformation("?? Generating AI response based on diary context");

            var diaryPrompt = $"""
            ?? **Agente Especializado del Diario Personal**

            Eres un agente de IA especializado EXCLUSIVAMENTE en responder preguntas sobre el diario personal del Twin.

            ?? **REGLAS FUNDAMENTALES:**
            
            1. **SOLO RESPONDES SOBRE EL DIARIO** - No respondas preguntas que no estén relacionadas con el contenido del diario
            2. **USA INFORMACIÓN REAL** - Solo usa la información proporcionada del diario real del usuario
            3. **SÉ PRECISO Y ESPECÍFICO** - Basa tus respuestas en datos concretos del diario
            4. **FORMATO HTML ELEGANTE** - Usa HTML con estilos inline para presentación profesional
            5. **MANTÉN PRIVACIDAD** - Eres el Twin digital de este usuario, habla en primera persona

            ?? **Twin ID:** {twinId}
            ? **Pregunta del Usuario:** {question}

            {diaryContext}

            ?? **ESTADÍSTICAS DE BÚSQUEDA:**
            • Total de entradas analizadas: {searchResults.Count}
            • Entradas procesadas exitosamente: {searchResults.Count(r => r.Success)}
            • Puntuación promedio de relevancia: {(searchResults.Any() ? searchResults.Average(r => r.SearchScore) : 0):F2}

            ?? **INSTRUCCIONES PARA RESPONDER:**

            1. **Analiza la pregunta** y determina si está relacionada con el diario
            2. **Si NO es sobre el diario**, responde: "? Lo siento, solo puedo responder preguntas sobre tu diario personal."
            3. **Si SÍ es sobre el diario**, usa ÚNICAMENTE la información proporcionada arriba
            4. **Crea una respuesta HTML elegante** con:
               - Encabezado con emoji y título relevante
               - Secciones bien organizadas con colores
               - Información específica del diario
               - Referencias a entradas específicas cuando sea relevante
               - Conclusiones basadas en los datos reales

            ?? **FORMATO DE RESPUESTA HTML:**
            ```html
            <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
                <h3 style="color: #fff; margin: 0 0 15px 0;">?? [Título relacionado con la pregunta del diario]</h3>
                
                <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                    <h4 style="color: #e3f2fd; margin: 0 0 10px 0;">?? Análisis de tu Diario</h4>
                    <p style="margin: 0; line-height: 1.6;">[Respuesta basada en el contenido del diario]</p>
                </div>

                [Más secciones según sea necesario]

                <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                    ?? Basado en {searchResults.Count} entradas relevantes de tu diario • ?? Twin: {twinId}
                </div>
            </div>
            ```

            ?? **EJEMPLOS DE RESPUESTAS APROPIADAS:**

            ? **PREGUNTA VÁLIDA:** "¿Qué actividades hice la semana pasada?"
            ? Analiza las entradas del diario y presenta las actividades encontradas

            ? **PREGUNTA VÁLIDA:** "¿Cuándo fue la última vez que hice ejercicio?"
            ? Busca en las entradas referencias a ejercicio y proporciona fechas específicas

            ? **PREGUNTA INVÁLIDA:** "¿Qué hora es?"
            ? Responde que solo puedes ayudar con preguntas sobre el diario

            ?? **IMPORTANTE:**
            - Sé personal pero profesional
            - Usa "tu/tus" cuando te refieras a las actividades del usuario
            - Sé específico con fechas, nombres y detalles del diario
            - Si no tienes información suficiente, dilo claramente
            - Nunca inventes información que no esté en los resultados de búsqueda
            """;

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(diaryPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 4000,
                    ["temperature"] = 0.3 // Temperatura baja para respuestas más precisas y consistentes
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "No se pudo generar respuesta sobre el diario.";
            
            _logger.LogInformation("? AI diary response generated successfully");
            return aiResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating AI diary response");
            
            // Respuesta de fallback
            return $"""
            <div style="background: linear-gradient(135deg, #ff6b6b 0%, #ee5a52 100%); padding: 20px; border-radius: 15px; color: white; font-family: Arial, sans-serif;">
                <h3 style="color: #ffe66d; margin: 0 0 15px 0;">? Error generando respuesta del diario</h3>
                
                <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                    <p style="margin: 0; line-height: 1.6;">
                        Lo siento, tuve un problema técnico generando una respuesta inteligente sobre tu diario.
                    </p>
                    <p style="margin: 10px 0 0 0; font-size: 14px; opacity: 0.9;">
                        ?? Encontré {searchResults.Count} entradas relevantes en tu diario, pero no pude procesarlas con IA.
                    </p>
                </div>
                
                <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                    ?? Error: {ex.Message} • ?? Twin: {twinId}
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
            _logger.LogInformation("?? Initializing Semantic Kernel for AgentDiaryAI");

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

            _logger.LogInformation("? Semantic Kernel initialized successfully for AgentDiaryAI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize Semantic Kernel for AgentDiaryAI");
            throw;
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Respuesta del AgentDiaryAI
/// </summary>
public class DiaryAIResponse
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
    /// Pregunta original del usuario
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Respuesta generada por el AI Agent
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Resultados de búsqueda que sirvieron de contexto
    /// </summary>
    public List<DiarySearchResultItem> SearchResults { get; set; } = new();

    /// <summary>
    /// Total de resultados encontrados en la búsqueda
    /// </summary>
    public int TotalResults { get; set; }

    /// <summary>
    /// Tipo de búsqueda utilizada (Vector, Semantic, FullText)
    /// </summary>
    public string SearchType { get; set; } = string.Empty;

    /// <summary>
    /// Tiempo de procesamiento en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Fecha y hora cuando se procesó la pregunta
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

        return $"? Success: {TotalResults} entries found, {SearchType} search, {ProcessingTimeMs:F0}ms";
    }

    /// <summary>
    /// Determina si la respuesta contiene información útil
    /// </summary>
    public bool HasUsefulContent => Success && TotalResults > 0 && !string.IsNullOrEmpty(Answer);

    /// <summary>
    /// Obtiene los DiaryEntryIds de los resultados de búsqueda
    /// </summary>
    public List<string> GetReferencedEntryIds()
    {
        return SearchResults.Select(r => r.DiaryEntryId).Where(id => !string.IsNullOrEmpty(id)).ToList();
    }

    /// <summary>
    /// Obtiene la puntuación promedio de relevancia de los resultados
    /// </summary>
    public double GetAverageRelevanceScore()
    {
        return SearchResults.Any() ? SearchResults.Average(r => r.SearchScore) : 0.0;
    }
}