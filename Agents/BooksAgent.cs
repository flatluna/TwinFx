using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Agents;

/// <summary>
/// Agente especializado en consultas inteligentes sobre libros
/// ========================================================================
/// 
/// Este agente utiliza Azure OpenAI y Semantic Kernel (GPT-4/GPT-5) para:
/// - Proporcionar información detallada sobre libros
/// - Generar reseñas y análisis de contenido
/// - Ofrecer información técnica sobre libros específicos
/// - Solo responde preguntas relacionadas con libros y literatura
/// - NO inventa información - se disculpa si no conoce un libro
/// - Búsqueda en Google para información adicional
/// - Búsqueda en Bing con Azure AI Agents para información web actualizada
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class BooksAgent
{
    private readonly ILogger<BooksAgent> _logger;
    private readonly IConfiguration _configuration;
    private Kernel? _kernel;
    private static readonly HttpClient _httpClient = new HttpClient();

    // Google Search API configuration
    private const string GOOGLE_API_KEY = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo";
    // Using general web search instead of CS curriculum specific search
    private const string GOOGLE_SEARCH_ENGINE_ID = "009462381166450434430:efzto9ihp2e";
    private const string GOOGLE_SEARCH_URL = "https://www.googleapis.com/customsearch/v1";

    // Bing Search API configuration (Direct HTTP approach)
    private const string BING_SEARCH_API_KEY = "ac0e3b90ba204e6ea437e4f87680998c";
    private const string BING_SEARCH_URL = "https://api.bing.microsoft.com/v7.0/search";

    // Azure AI Foundry configuration for Bing Grounding
    private const string PROJECT_ENDPOINT = "https://twinet-resource.services.ai.azure.com/api/projects/twinet";
    private const string MODEL_DEPLOYMENT_NAME = "gpt4mini";
    private const string BING_CONNECTION_ID = "/subscriptions/bf5f11e8-1b22-4e27-b55e-8542ff6dec42/resourceGroups/rg-jorgeluna-7911/providers/Microsoft.CognitiveServices/accounts/twinet-resource/projects/twinet/connections/twinbing";

    public BooksAgent(ILogger<BooksAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _logger.LogInformation("📚 BooksAgent initialized for intelligent book information queries with Bing Search");
    }

    /// <summary>
    /// Realizar búsqueda en Google usando Google Search API
    /// </summary>
    /// <param name="searchQuery">Término de búsqueda</param>
    /// <returns>Resultados de búsqueda de Google</returns>
    public async Task<string> GoogleSearchAsync(string searchQuery)
    {
        _logger.LogInformation("🔍 Performing Google Search for: {SearchQuery}", searchQuery);

        try
        {
            if (string.IsNullOrEmpty(searchQuery))
            {
                return "Error: Search query cannot be empty";
            }

            // Construir URL para Google Custom Search API
            var searchUrl = $"{GOOGLE_SEARCH_URL}?key={GOOGLE_API_KEY}&cx={GOOGLE_SEARCH_ENGINE_ID}&q={Uri.EscapeDataString(searchQuery)}&num=10";

            _logger.LogInformation("📡 Making request to Google Search API");

            // Realizar petición HTTP GET
            var response = await _httpClient.GetAsync(searchUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("❌ Google Search API request failed: {StatusCode} - {ReasonPhrase}",
                    response.StatusCode, response.ReasonPhrase);
                return CreateNoResultsMessage(searchQuery, $"API request failed - {response.StatusCode}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("✅ Google Search API response received successfully");

            // Parsear la respuesta JSON
            var searchResult = JsonSerializer.Deserialize<GoogleSearchResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (searchResult?.Items == null || searchResult.Items.Count == 0)
            {
                _logger.LogInformation("📭 No search results found for query: {SearchQuery}", searchQuery);

                // Check if this is due to a restricted search engine (like CS curriculum)
                if (searchResult?.SearchInformation?.TotalResults == "0")
                {
                    var contextInfo = "";
                    if (jsonContent.Contains("CS Curriculum") || jsonContent.Contains("lectures") || jsonContent.Contains("assignments"))
                    {
                        contextInfo = " (El motor de búsqueda está configurado para contenido académico específico)";
                    }

                    return CreateNoResultsMessage(searchQuery, $"Sin resultados en Google Custom Search{contextInfo}");
                }

                return CreateNoResultsMessage(searchQuery, "Sin resultados");
            }

            // Formatear los resultados
            var formattedResults = FormatGoogleSearchResults(searchResult, searchQuery);

            _logger.LogInformation("✅ Google Search completed successfully - {Count} results found",
                searchResult.Items.Count);

            return formattedResults;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "🌐 HTTP error during Google Search for query: {SearchQuery}", searchQuery);
            return CreateNoResultsMessage(searchQuery, $"Error de conexión: {httpEx.Message}");
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "📄 JSON parsing error in Google Search response for query: {SearchQuery}", searchQuery);
            return CreateNoResultsMessage(searchQuery, $"Error procesando respuesta: {jsonEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Unexpected error during Google Search for query: {SearchQuery}", searchQuery);
            return CreateNoResultsMessage(searchQuery, $"Error inesperado: {ex.Message}");
        }
    }

    /// <summary>
    /// Crear mensaje cuando no hay resultados de Google Search
    /// </summary>
    private string CreateNoResultsMessage(string originalQuery, string reason)
    {
        return $"""
            <div style="background: linear-gradient(135deg, #ffa726 0%, #ff9800 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 15px rgba(0,0,0,0.1);">
                <h3 style="color: #fff; margin: 0 0 15px 0; display: flex; align-items: center;">
                    🔍 Google Search - Sin Resultados
                </h3>
                
                <div style="background: rgba(255,255,255,0.1); padding: 12px; border-radius: 8px; margin: 15px 0;">
                    <p style="margin: 0; font-size: 14px;"><strong>📖 Búsqueda:</strong> {originalQuery}</p>
                    <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>📊 Estado:</strong> {reason}</p>
                    <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>💡 Acción:</strong> Usando conocimiento interno del AI</p>
                </div>
                
                <div style="background: rgba(255,255,255,0.1); padding: 12px; border-radius: 8px; margin: 15px 0;">
                    <h4 style="color: #fff; margin: 0 0 10px 0; font-size: 16px;">📚 Fuentes Alternativas Recomendadas:</h4>
                    <ul style="margin: 0; padding-left: 20px; font-size: 14px; line-height: 1.6;">
                        <li>Amazon (información de libros y reseñas)</li>
                        <li>Goodreads (reseñas de lectores)</li>
                        <li>Casa del Libro / Gandhi (precios en español)</li>
                        <li>Bibliotecas universitarias</li>
                        <li>Google Books (vista previa)</li>
                    </ul>
                </div>

                <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                    🔍 Búsqueda realizada • 📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
                </div>
            </div>
            """;
    }

    /// <summary>
    /// Formatear los resultados de Google Search en HTML
    /// </summary>
    private string FormatGoogleSearchResults(GoogleSearchResponse searchResult, string originalQuery)
    {
        try
        {
            var resultsHtml = $"""
                <div style="background: linear-gradient(135deg, #4285f4 0%, #34a853 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 15px rgba(0,0,0,0.1);">
                    <h3 style="color: #fff; margin: 0 0 15px 0; display: flex; align-items: center;">
                        🔍 Resultados de Búsqueda en Google
                    </h3>
                    
                    <div style="background: rgba(255,255,255,0.1); padding: 12px; border-radius: 8px; margin: 15px 0;">
                        <p style="margin: 0; font-size: 14px;"><strong>📖 Búsqueda:</strong> {originalQuery}</p>
                        <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>📊 Resultados encontrados:</strong> {searchResult.SearchInformation?.TotalResults ?? "0"}</p>
                        <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>⏱️ Tiempo de búsqueda:</strong> {searchResult.SearchInformation?.SearchTime ?? "N/A"} segundos</p>
                    </div>
                """;

            if (searchResult.Items != null && searchResult.Items.Count > 0)
            {
                resultsHtml += """
                    <div style="background: rgba(255,255,255,0.9); padding: 15px; border-radius: 10px; margin: 15px 0; color: #333;">
                        <h4 style="color: #1a73e8; margin: 0 0 15px 0;">📋 Principales Resultados:</h4>
                    """;

                for (int i = 0; i < Math.Min(searchResult.Items.Count, 5); i++)
                {
                    var item = searchResult.Items[i];
                    resultsHtml += $"""
                        <div style="border-left: 3px solid #4285f4; padding-left: 12px; margin: 12px 0; background: #f8f9fa; padding: 10px; border-radius: 5px;">
                            <h5 style="margin: 0 0 5px 0; color: #1a73e8;">
                                <a href="{item.Link}" target="_blank" style="color: #1a73e8; text-decoration: none;">{item.Title}</a>
                            </h5>
                            <p style="margin: 0 0 5px 0; font-size: 13px; color: #34a853;">{item.DisplayLink}</p>
                            <p style="margin: 0; font-size: 14px; color: #5f6368; line-height: 1.4;">{item.Snippet}</p>
                        </div>
                        """;
                }

                resultsHtml += "</div>";
            }

            resultsHtml += $"""
                    <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                        🔍 Búsqueda realizada con Google Custom Search API • 📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
                    </div>
                </div>
                """;

            return resultsHtml;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error formatting Google Search results");
            return $"Error formateando los resultados de Google: {ex.Message}";
        }
    }

    /// <summary>
    /// Procesa consultas inteligentes sobre libros usando Azure OpenAI
    /// </summary>
    /// <param name="question">Pregunta del usuario sobre libros</param>
    /// <param name="twinId">ID del Twin</param>
    /// <returns>Respuesta inteligente con información del libro</returns>
    public async Task<BooksAIResponse> ProcessBookQuestionAsync(string question, string twinId)
    {
        _logger.LogInformation("📚 Processing intelligent book question for Twin ID: {TwinId}", twinId);
        _logger.LogInformation("📖 Question: {Question}", question);

        var startTime = DateTime.UtcNow;

        try
        {
            // Validar inputs básicos
            if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(twinId))
            {
                return new BooksAIResponse
                {
                    Success = false,
                    Error = "Question and TwinId are required",
                    TwinId = twinId,
                    Question = question
                };
            }

            // Inicializar Semantic Kernel
            await InitializeKernelAsync();

            // Generar respuesta inteligente sobre el libro
            var bookAnalysisResponse = await GenerateBookAnalysisAsync(question, twinId);
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Convertir BookInformation a string JSON con caracteres UTF-8 sin escape
            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string bookData = JsonSerializer.Serialize(bookAnalysisResponse.BookInformation, jsonOptions);
            
            _logger.LogInformation("✅ Book analysis completed successfully");

            return new BooksAIResponse
            {
                Success = true,
                TwinId = twinId,
                Question = question,
                Answer = bookAnalysisResponse.Answer,
                BookInformation = bookAnalysisResponse.BookInformation != null ?
                                   JsonSerializer.Serialize(bookAnalysisResponse.BookInformation, jsonOptions) :
                                   "No information available",
                Disclaimer = bookAnalysisResponse.Disclaimer,
                ProcessingTimeMs = processingTimeMs
            };

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing book question for Twin: {TwinId}", twinId);
            return new BooksAIResponse
            {
                Success = false,
                Error = ex.Message,
                TwinId = twinId,
                Question = question,
                ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                Answer = $"""
                ❌ **Error procesando tu consulta sobre libros**
                
                🔴 **Error:** {ex.Message}
                
                💡 **Sugerencias:**
                • Intenta reformular tu pregunta sobre el libro
                • Asegúrate de incluir el título y autor si es posible
                • Contacta al soporte técnico si el problema persiste
                """,
                Disclaimer = "Hubo un error técnico procesando tu consulta. Por favor intenta nuevamente."
            };
        }
    }

    /// <summary>
    /// Genera análisis inteligente sobre libros usando Azure OpenAI
    /// </summary>
    private async Task<BookAnalysisResult> GenerateBookAnalysisAsync(string question, string twinId)
    {
        try
        {
            _logger.LogInformation("🤖 Generating intelligent book analysis with Azure OpenAI");

            // STEP 1: Try both Google and Bing Search for comprehensive context
            string searchContext = "";
            try
            {
                _logger.LogInformation("🔍 Attempting web search for additional book context");

                // Try Bing Search first (better for books and current information)
                string bingResults = "";
                try
                {
                    bingResults = await BingSearchAsync($"libro \"{question}\" precio editorial reseña información actualizada");
                    if (!string.IsNullOrEmpty(bingResults) && !bingResults.Contains("Sin Resultados"))
                    {
                        searchContext += $"\n\n🔍 **CONTEXTO DE BING SEARCH:**\n{bingResults}\n";
                        _logger.LogInformation("✅ Bing Search provided additional context");
                    }
                }
                catch (Exception bingEx)
                {
                    _logger.LogWarning(bingEx, "⚠️ Bing Search failed, trying Google fallback");
                }

                // Try Google Search as additional source if available
                if (string.IsNullOrEmpty(searchContext))
                {
                    try
                    {
                        var googleResults = await GoogleSearchAsync($"libro \"{question}\" precio editorial reseña información");
                        if (!string.IsNullOrEmpty(googleResults) && !googleResults.Contains("Sin Resultados"))
                        {
                            searchContext = $"\n\n📚 **CONTEXTO DE GOOGLE SEARCH:**\n{googleResults}\n";
                            _logger.LogInformation("✅ Google Search provided additional context as fallback");
                        }
                    }
                    catch (Exception googleEx)
                    {
                        _logger.LogWarning(googleEx, "⚠️ Google Search también falló");
                    }
                }

                // If no results from either search
                if (string.IsNullOrEmpty(searchContext))
                {
                    _logger.LogInformation("📭 No useful results from web search, proceeding with AI knowledge only");
                    searchContext = "\n\n📚 **NOTA:** No se encontró información adicional en web search, usando conocimiento interno del AI.\n";
                }
            }
            catch (Exception searchEx)
            {
                _logger.LogWarning(searchEx, "⚠️ Web search failed, continuing with AI knowledge only");
                searchContext = "\n\n📚 **NOTA:** Búsqueda web no disponible, usando conocimiento interno del AI.\n";
            }

            // STEP 2: Create enhanced prompt with search context
            var bookPrompt = $$$"""  
📚 **Especialista en Literatura y Análisis de Libros**  
Eres un experto literario y analista de libros con conocimiento enciclopédico sobre literatura mundial, reseñas críticas y análisis académico.
IMPORTANTE: Usa estos datos como informacion principal y complementa con tu propio conocimento de est libro. Da tu propia opinion del libro
****Esta es informacion del WEB ***
{{{searchContext}}}

**** Termina informacion del WEB ****

📋 **INSTRUCCIONES ESPECÍFICAS:** 
Contesta en JSON puro. NO comiences con ```json ni termines con ```. 
Usa exactamente estos nombres de propiedades en el JSON:
{  
    "INFORMACIÓN_TÉCNICA": {  
        "Título_original": "Homo Deus: A Brief History of Tomorrow",  
        "Título_en_español": "Homo Deus: Breve historia del mañana",  
        "Autor": "Yuval Noah Harari",  
        "Idioma_original": "Hebreo",  
        "Primera_publicación": "2015",  
        "Editorial_principal": "Debate",  
        "ISBN": [  
            "9788499926711",  
            "9788499926643"  
        ],  
        "Páginas": "Variable según la edición (aproximadamente 400 páginas)",  
        "Formatos": ["Tapa blanda", "eBook"],  
        "Duración_audiolibro": "Variable según la edición",  
        "Fecha_de_publicación": "Varias ediciones, entre 2016 y 2022",  
        "Portada": "La portada suele mostrar el título 'Homo Deus' en letras prominentes con el subtítulo 'Breve historia del mañana', y en la edición de Debate el diseño es bastante sobrio, con colores oscuros."  
    },  
    "RESEÑAS_CRÍTICAS": {  
        "Bestseller_internacional": "Sí, con más de un millón de ejemplares vendidos.",  
        "Evaluación": "Muy bien evaluado por críticos y lectores.",  
        "Elogios": [  
            "Prosa clara, fresca y libre de prejuicios.",  
            "Estimulante y provoca reflexión profunda sobre el futuro de la humanidad.",  
            "Elogiado por intelectuales como Kazuo Ishiguro y Daniel Kahneman."  
        ]  
    },  
    "CONTENIDO_Y_TESIS_PRINCIPAL": {  
        "Idea_central": "Examen del futuro de la humanidad en una era dominada por la tecnología, la inteligencia artificial y los avances biotecnológicos.",  
        "Tecnologías_clave": [  
            "Inteligencia Artificial",  
            "Biotecnología"  
        ],  
        "Conceptos_principales": [  
            "Futurismo",  
            "Ética tecnológica",  
            "Evolución humana"  
        ]  
    },  
    "RECEPCIÓN_GENERAL": {  
        "Aspectos_elogiados": [  
            "Capacidad de entrelazar ciencia, filosofía y futurismo."  
        ],  
        "Aspectos_criticados": [],  
        "Recomendación": "Altamente recomendado para aquellos interesados en el futuro de la humanidad y la tecnología."  
    },  
    DescripcionAI"",
    "INFORMACIÓN_PRÁCTICA": {  
        "Precio_orientativo": {  
            "Tapa_dura": "",  
            "Rústica": "299 pesos MXN aproximadamente",  
            "eBook": ""  
        },
        portadaURL = [
            "https://m.media-amazon.com/images/I/41X7b6m8HkL._SX331_BO1,204,203,200_.jpg",
            "https://images-na.ssl-images-amazon.com/images/I/51s+u4r3JDL._SX331_BO1,204,203,200_.jpg"
        ],
        "Disponibilidad": [  
            "Amazon",  
            "Casa del Libro",  
            "Porrúa"  
        ],  
        "Público_recomendado": "Lectores interesados en filosofía, ciencia y futurismo.",  
        "Traducciones": "Disponible en múltiples idiomas, incluyendo inglés y español."  
    },  
    "detailHTMLReport": "<div>HTML report with all book details, colorful design, grids, titles, etc.</div>"  
}  

🔍 **PREGUNTA DEL USUARIO:** {{{question}}}
IMPORTANTE: En descrirpcion de AI da una descripcion completa del libro, tu opinion, pero no inventes nada solo lo que opinas y leiste del search.
Asegurate de adicionar el DescripcionAI al HTML toda la data tiene que estar en el HTML para que el usuario vea el libro muy profesional.
2) Lista los urls que te da el search de la portada
⚠️ **IMPORTANTE:**
- Si conoces el libro, proporciona información completa y detallada
- Si NO conoces el libro específico, sé honesto y responde: "Me disculpo, pero no tengo información suficiente sobre este libro específico."
- NO inventes datos sobre libros que no conoces
- Usa la información de web search si está disponible arriba
- Si hay información de precios actual de web search, úsala
- Crea un detailHTMLReport visualmente atractivo con colores y diseño profesional

Responde SOLO con el JSON válido:
""";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(bookPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_completion_tokens"] = 40000,
                    ["temperature"] = 0.2  // Lower temperature for more factual responses
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "";

            _logger.LogInformation("✅ AI book analysis response generated successfully");

            // Clean up the response to ensure it's pure JSON
            var cleanResponse = aiResponse.Trim();
            if (cleanResponse.StartsWith("```json"))
            {
                cleanResponse = cleanResponse.Substring(7);
            }
            if (cleanResponse.EndsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
            }
            cleanResponse = cleanResponse.Trim();

            Book book;
            try
            {
                book = System.Text.Json.JsonSerializer.Deserialize<Book>(cleanResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "⚠️ Failed to parse AI response as JSON, creating fallback response");

                // Create a fallback response indicating the AI couldn't provide structured data
                book = new Book
                {
                    INFORMACIÓN_TÉCNICA = new InformacionTecnica
                    {
                        Título_en_español = "Información no disponible",
                        Autor = "Me disculpo, pero no tengo información suficiente sobre este libro específico.",
                        Editorial_principal = "No disponible",
                        Páginas = "No disponible",
                        Formatos = new List<string> { "Consulta fuentes especializadas" },
                        Duración_audiolibro = "No disponible"
                    },
                    RESEÑAS_CRÍTICAS = new ReseñasCriticas
                    {
                        
                    },
                    CONTENIDO_Y_TESIS_PRINCIPAL = new ContenidoYtesisPrincipal
                    {
                        Idea_central = "Me disculpo, pero no tengo información suficiente sobre este libro específico.",
                       
                        Conceptos_principales = new List<string> { "Consulta fuentes especializadas" }
                    },
                    RECEPCIÓN_GENERAL = new RecepcionGeneral
                    {
                        Aspectos_elogiados = new List<string> { "No disponible" },
                        Aspectos_criticados = new List<string> { "No disponible" },
                        Recomendación = "Te recomiendo consultar bibliotecas, librerías o bases de datos académicas para información precisa sobre este libro."
                    },
                    INFORMACIÓN_PRÁCTICA = new InformacionPractica
                    {
                        Precio_orientativo = new PrecioOrientativo
                        {
                            Tapa_dura = "Consulta librerías",
                            Rústica = "Consulta librerías",
                            eBook = "Consulta plataformas digitales"
                        },
                        Disponibilidad = new List<string> { "Consulta librerías locales", "Bibliotecas", "Plataformas digitales" },
                        Público_recomendado = "No disponible",
                        Traducciones = "No disponible"
                    },
                    detailHTMLReport = $"""
                        <div style="background: linear-gradient(135deg, #ff6b6b 0%, #ee5a52 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;">
                            <h3 style="color: #fff; margin: 0 0 15px 0;">📚 Información del Libro No Disponible</h3>
                            
                            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                                <h4 style="color: #ffe66d; margin: 0 0 10px 0;">🔍 Búsqueda Realizada</h4>
                                <p style="margin: 0; line-height: 1.6;"><strong>Consulta:</strong> {question}</p>
                                <p style="margin: 5px 0 0 0; line-height: 1.6;"><strong>Estado:</strong> Me disculpo, pero no tengo información suficiente sobre este libro específico.</p>
                            </div>

                            <div style="background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;">
                                <h4 style="color: #ffe66d; margin: 0 0 10px 0;">💡 Recomendaciones</h4>
                                <ul style="margin: 0; padding-left: 20px; line-height: 1.6;">
                                    <li>Consulta bibliotecas públicas y universitarias</li>
                                    <li>Visita librerías especializadas</li>
                                    <li>Busca en bases de datos académicas (JSTOR, Project MUSE)</li>
                                    <li>Revisa sitios especializados (Goodreads, LibraryThing)</li>
                                    <li>Consulta reseñas en medios reconocidos</li>
                                </ul>
                            </div>

                            <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                                📚 Búsqueda realizada con honestidad • 📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
                            </div>
                        </div>
                        """
                };
            }

            // Process and return the analysis result
            var analysisResult = ProcessBookAnalysisResponse(book);

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during AI book analysis");

            // Respuesta de fallback
            return new BookAnalysisResult
            {
                Answer = $"""
                ❌ **Error procesando la consulta sobre el libro**
                
                🔴 **Error técnico:** {ex.Message}
                
                💡 **Sugerencias:**
                • Intenta reformular tu pregunta sobre el libro
                • Verifica que el título y autor estén correctos
                • Consulta fuentes especializadas como bibliotecas o librerías
                
                📚 **Recursos recomendados:**
                • Bibliotecas públicas y universitarias
                • Bases de datos académicas (JSTOR, Project MUSE)
                • Sitios especializados (Goodreads, LibraryThing)
                • Reseñas en medios reconocidos (NYT, Guardian, etc.)
                """,
                BookInformation = null,
                Disclaimer = "Hubo un problema técnico. Por favor verifica la información en fuentes oficiales como bibliotecas o editoriales."
            };
        }
    }

    /// <summary>
    /// Procesa la respuesta de AI y extrae información estructurada
    /// </summary>
    private BookAnalysisResult ProcessBookAnalysisResponse(Book bookInfo)
    {
        try
        {

            // Extraer disclaimer específico
            var disclaimer = """
                📝 **Disclaimer:** Esta información proviene de fuentes públicas y análisis de IA. 
                Para datos precisos (ISBN, precios actuales, disponibilidad), consulta directamente 
                con bibliotecas, librerías o las editoriales oficiales. Recomendamos verificar 
                la información en fuentes primarias antes de tomar decisiones de compra o académicas.
                """;

            return new BookAnalysisResult
            {
                Answer = "OK",
                BookInformation = bookInfo,
                Disclaimer = disclaimer
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing book analysis response");

            return new BookAnalysisResult
            {
                Answer = "Error procesando la información del libro. Por favor intenta nuevamente.",
                BookInformation = new Book(),
                Disclaimer = "Error técnico. Consulta fuentes especializadas para información precisa."
            };
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
            _logger.LogInformation("🔧 Initializing Semantic Kernel for BooksAgent");

            // Crear kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Obtener configuración de Azure OpenAI
            var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ??
                          _configuration.GetValue<string>("AzureOpenAI:Endpoint") ??
                          throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

            var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ??
                        _configuration.GetValue<string>("AzureOpenAI:ApiKey") ??
                        throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

            // Usar GPT-5 como se especificó en los requerimientos
            var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ??
                                _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ??
                                "gpt-5-mini";

            _logger.LogInformation("📚 Using deployment: {DeploymentName} for book analysis", deploymentName);
            deploymentName = "gpt4mini"; // Forzar uso de GPT-5
            // Agregar Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Construir el kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized successfully for BooksAgent with {DeploymentName}", deploymentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for BooksAgent");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Realizar búsqueda en Bing usando Bing Search API v7
    /// </summary>
    /// <param name="searchQuery">Término de búsqueda</param>
    /// <returns>Resultados de búsqueda con Grounding o Bing API directa</returns>
    public async Task<string> BingSearchAsync(string searchQuery)
    {
        _logger.LogInformation("🔍 Performing Bing Search for: {SearchQuery}", searchQuery);

        try
        {
            if (string.IsNullOrEmpty(searchQuery))
            {
                return "Error: Search query cannot be empty";
            }

            // Try Azure AI Agents with Bing Grounding first
            try
            {
                searchQuery = "busca tambien el ISBN del linbro y dame fotos varias de la portada en sites de USA , " + searchQuery;
                return await BingGroundingSearchAsync(searchQuery);
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

                // Fallback to direct Bing Search API
                return await BingDirectSearchAsync(searchQuery);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ All Bing Search methods failed for query: {SearchQuery}", searchQuery);
            return CreateBingNoResultsMessage(searchQuery, $"Error inesperado: {ex.Message}");
        }
    }

    /// <summary>
    /// Búsqueda usando Azure AI Agents con Bing Grounding
    /// </summary>
    private async Task<string> BingGroundingSearchAsync(string searchQuery)
    {
        _logger.LogInformation("🔧 Attempting Bing Grounding Search with Azure AI Agents");

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
            name: "books-search-agent",
            instructions: "Use the bing grounding tool to search for book information. Provide detailed, accurate information about books including prices, reviews, and availability. Focus on Spanish language sources when possible.",
            tools: [bingGroundingTool]
        );

        // Step 3: Create a thread and run
        var thread = await agentClient.Threads.CreateThreadAsync();

        var message = await agentClient.Messages.CreateMessageAsync(
            thread.Value.Id,
            MessageRole.User,
            $"Search for detailed information about: {searchQuery}. Include book details, prices, reviews, and availability if it's a book. Provide information in Spanish when possible.");

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

        return FormatBingGroundingResults(searchResults, searchQuery);
    }

    /// <summary>
    /// Búsqueda directa usando Bing Search API v7
    /// </summary>
    private async Task<string> BingDirectSearchAsync(string searchQuery)
    {
        _logger.LogInformation("🔧 Using direct Bing Search API v7 as fallback");

        // Configure HTTP client for Bing Search
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", BING_SEARCH_API_KEY);

        // Construir URL para Bing Search API
        var searchUrl = $"{BING_SEARCH_URL}?q={Uri.EscapeDataString(searchQuery)}&count=10&mkt=es-ES";

        // Realizar petición HTTP GET
        var response = await client.GetAsync(searchUrl);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Bing Search API request failed: {response.StatusCode} - {response.ReasonPhrase}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync();

        // Parsear la respuesta JSON
        var searchResult = JsonSerializer.Deserialize<BingSearchResponse>(jsonContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (searchResult?.WebPages?.Value == null || searchResult.WebPages.Value.Count == 0)
        {
            throw new InvalidOperationException("No results found in direct Bing Search");
        }

        return FormatBingDirectSearchResults(searchResult, searchQuery);
    }

    /// <summary>
    /// Formatear los resultados de Bing Search API directa en HTML
    /// </summary>
    private string FormatBingDirectSearchResults(BingSearchResponse searchResult, string originalQuery)
    {
        try
        {
            var resultsHtml = $"""
                <div style="background: linear-gradient(135deg, #00a1f1 0%, #0078d4 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 15px rgba(0,0,0,0.1);">
                    <h3 style="color: #fff; margin: 0 0 15px 0; display: flex; align-items: center;">
                        🔍 Resultados de Búsqueda en Bing
                    </h3>
                    
                    <div style="background: rgba(255,255,255,0.1); padding: 12px; border-radius: 8px; margin: 15px 0;">
                        <p style="margin: 0; font-size: 14px;"><strong>📖 Búsqueda:</strong> {originalQuery}</p>
                        <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>📊 Resultados encontrados:</strong> {searchResult.WebPages?.Value?.Count ?? 0}</p>
                        <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>🤖 Powered by:</strong> Bing Search API v7</p>
                    </div>
                """;

            if (searchResult.WebPages?.Value != null && searchResult.WebPages.Value.Count > 0)
            {
                resultsHtml += """
                    <div style="background: rgba(255,255,255,0.9); padding: 15px; border-radius: 10px; margin: 15px 0; color: #333;">
                        <h4 style="color: #0078d4; margin: 0 0 15px 0;">📋 Principales Resultados:</h4>
                    """;

                for (int i = 0; i < Math.Min(searchResult.WebPages.Value.Count, 5); i++)
                {
                    var item = searchResult.WebPages.Value[i];
                    resultsHtml += $"""
                        <div style="border-left: 3px solid #00a1f1; padding-left: 12px; margin: 12px 0; background: #f8f9fa; padding: 10px; border-radius: 5px;">
                            <h5 style="margin: 0 0 5px 0; color: #0078d4;">
                                <a href="{item.Url}" target="_blank" style="color: #0078d4; text-decoration: none;">{item.Name}</a>
                            </h5>
                            <p style="margin: 0 0 5px 0; font-size: 13px; color: #00a1f1;">{item.DisplayUrl}</p>
                            <p style="margin: 0; font-size: 14px; color: #5f6368; line-height: 1.4;">{item.Snippet}</p>
                        </div>
                        """;
                }

                resultsHtml += "</div>";
            }

            resultsHtml += $"""
                    <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                        🔍 Búsqueda realizada con Bing Search API v7 • 📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
                    </div>
                </div>
                """;

            return resultsHtml;
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
            <div style="background: linear-gradient(135deg, #00a1f1 0%, #0078d4 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 15px rgba(0,0,0,0.1);">
                <h3 style="color: #fff; margin: 0 0 15px 0; display: flex; align-items: center;">
                    🔍 Bing Search - Sin Resultados
                </h3>
                
                <div style="background: rgba(255,255,255,0.1); padding: 12px; border-radius: 8px; margin: 15px 0;">
                    <p style="margin: 0; font-size: 14px;"><strong>📖 Búsqueda:</strong> {originalQuery}</p>
                    <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>📊 Estado:</strong> {reason}</p>
                    <p style="margin: 5px 0 0 0; font-size: 14px;"><strong>💡 Acción:</strong> Usando conocimiento interno del AI</p>
                </div>
                
                <div style="background: rgba(255,255,255,0.1); padding: 12px; border-radius: 8px; margin: 15px 0;">
                    <h4 style="color: #fff; margin: 0 0 10px 0; font-size: 16px;">📚 Fuentes Alternativas Recomendadas:</h4>
                    <ul style="margin: 0; padding-left: 20px; font-size: 14px; line-height: 1.6;">
                        <li>Amazon (información de libros y reseñas)</li>
                        <li>Goodreads (reseñas de lectores)</li>
                        <li>Casa del Libro / Gandhi (precios en español)</li>
                        <li>Bibliotecas universitarias</li>
                        <li>Microsoft Bing (búsqueda directa)</li>
                    </ul>
                </div>

                <div style="margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;">
                    🔍 Búsqueda realizada con Bing Search • 📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
                </div>
            </div>
            """;
    }

    /// <summary>
    /// Formatear los resultados de Bing Grounding en texto plano para procesamiento por AI
    /// </summary>
    private string FormatBingGroundingResults(List<string> searchResults, string originalQuery)
    {
        try
        {
            var sb = new System.Text.StringBuilder();

            // Header with search information
            sb.AppendLine("🔍 RESULTADOS DE BÚSQUEDA CON BING GROUNDING");
            sb.AppendLine("=" + new string('=', 50));
            sb.AppendLine($"📖 Búsqueda realizada: {originalQuery}");
            sb.AppendLine($"📊 Resultados encontrados: {searchResults.Count}");
            sb.AppendLine($"🤖 Fuente: Azure AI Agents + Bing Grounding");
            sb.AppendLine($"📅 Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();

            if (searchResults.Count > 0)
            {
                sb.AppendLine("📋 INFORMACIÓN ENCONTRADA CON CITATIONS:");
                sb.AppendLine("-" + new string('-', 40));

                for (int i = 0; i < searchResults.Count; i++)
                {
                    var result = searchResults[i];
                    sb.AppendLine($"[RESULTADO {i + 1}]");
                    sb.AppendLine(result);

                    if (i < searchResults.Count - 1)
                    {
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                }
            }
            else
            {
                sb.AppendLine("❌ No se encontraron resultados específicos.");
            }

            sb.AppendLine();
            sb.AppendLine("ℹ️ SOBRE BING GROUNDING:");
            sb.AppendLine("Los resultados incluyen citations automáticas con enlaces a las fuentes originales.");
            sb.AppendLine("La información es procesada por AI con referencias verificables.");
            sb.AppendLine("Esta información debe ser verificada en fuentes primarias antes de ser utilizada.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error formatting Bing Grounding results");
            return $"""
                ❌ ERROR AL FORMATEAR RESULTADOS DE BING GROUNDING
                Error: {ex.Message}
                Búsqueda original: {originalQuery}
                Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
                
                Recomendación: Usar fuentes alternativas como bibliotecas, librerías especializadas o bases de datos académicas.
                """;
        }
    }
}

// ========================================
// BING SEARCH RESPONSE MODELS  
// ========================================

/// <summary>
/// Bing Search API Response Model
/// </summary>
public class BingSearchResponse
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

// ========================================
// GOOGLE SEARCH RESPONSE MODELS
// ========================================

/// <summary>
/// Google Search API Response Model
/// </summary>
public class GoogleSearchResponse
{
    public GoogleSearchInformation? SearchInformation { get; set; }
    public List<GoogleSearchItem>? Items { get; set; }
}

/// <summary>
/// Google Search Information Model
/// </summary>
public class GoogleSearchInformation
{
    public string? SearchTime { get; set; }
    public string? FormattedSearchTime { get; set; }
    public string? TotalResults { get; set; }
    public string? FormattedTotalResults { get; set; }
}

/// <summary>
/// Google Search Item Model
/// </summary>
public class GoogleSearchItem
{
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? HtmlTitle { get; set; }
    public string? Link { get; set; }
    public string? DisplayLink { get; set; }
    public string? Snippet { get; set; }
    public string? HtmlSnippet { get; set; }
    public string? CacheId { get; set; }
    public string? FormattedUrl { get; set; }
    public string? HtmlFormattedUrl { get; set; }
}

// ========================================
// EXISTING MODELS Y RESPONSE CLASSES
// ========================================

/// <summary>
/// Respuesta del BooksAgent
/// </summary>
public class BooksAIResponse
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
    /// Respuesta detallada generada por AI
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Información estructurada del libro
    /// </summary>
    public string BookInformation { get; set; } = string.Empty;

    /// <summary>
    /// Disclaimer sobre la información proporcionada
    /// </summary>
    public string Disclaimer { get; set; } = string.Empty;

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
            return $"❌ Error: {Error}";
        }

        return $"✅ Success: Book query processed, {ProcessingTimeMs:F0}ms";
    }

    /// <summary>
    /// Determina si la respuesta contiene información útil
    /// </summary>
    public bool HasUsefulContent => Success && !string.IsNullOrEmpty(Answer) && !string.IsNullOrEmpty(BookInformation);
}

/// <summary>
/// Resultado del análisis de libro
/// </summary>
/// 

public class BookMain
{

    public string id { get; set; }
    public string titulo { get; set; }
    public string autor { get; set; }
    public string isbn { get; set; }    
    public int añoPublicacion { get; set; }  // Cambio de string a int
    public int calificacion { get; set; }
    public DateTime createdAt { get; set; }
    public Book datosIA { get; set; }
    public string descripcion { get; set; }
    public string editorial { get; set; }
    public string estado { get; set; }
    public string fechaFin { get; set; }
    public string fechaInicio { get; set; }
    public string fechaLectura { get; set; }
    public string fechaPrestamo { get; set; }
    public string formato { get; set; }
    public string genero { get; set; } 
    public string opiniones { get; set; }
    public int paginas { get; set; }
    public string portada { get; set; }
    public string prestadoA { get; set; }
    public bool recomendado { get; set; }
    public List<string> tags { get; set; }
    public DateTime updatedAt { get; set; }
}
public class Book
{
    public InformacionTecnica INFORMACIÓN_TÉCNICA { get; set; }
    public ReseñasCriticas RESEÑAS_CRÍTICAS { get; set; }
    public ContenidoYtesisPrincipal CONTENIDO_Y_TESIS_PRINCIPAL { get; set; }
    public RecepcionGeneral RECEPCIÓN_GENERAL { get; set; }
    public InformacionPractica INFORMACIÓN_PRÁCTICA { get; set; }
    public string DescripcionAI { get; set; } // Agregado para la descripción AI  
    public string detailHTMLReport { get; set; }

    public List<BookNote> BookNotes { get; set; } = new List<BookNote>();
}

public class InformacionTecnica
{
    public string Título_original { get; set; }
    public string Título_en_español { get; set; }
    public string Autor { get; set; }
    public string Idioma_original { get; set; }
    public string Primera_publicación { get; set; }
    public string Editorial_principal { get; set; }
    public List<string> ISBN { get; set; } // Agregado para ISBN  
    public string Páginas { get; set; }
    public List<string> Formatos { get; set; }
    public string Duración_audiolibro { get; set; }
    public string Fecha_de_publicación { get; set; } // Agregado para la fecha de publicación  
    public string Portada { get; set; } // Agregado para la descripción de la portada  
}

public class ReseñasCriticas
{
    public string Bestseller_internacional { get; set; } // Agregado para indicar si es bestseller  
    public string Evaluación { get; set; }
    public List<string> Elogios { get; set; } // Agregado para elogios  
}

public class ContenidoYtesisPrincipal
{
    public string Idea_central { get; set; }
    public List<string> Obras_clave { get; set; } // Agregado para las obras clave  
    public List<string> Conceptos_principales { get; set; }
}

public class RecepcionGeneral
{
    public List<string> Aspectos_elogiados { get; set; }
    public List<string> Aspectos_criticados { get; set; }
    public string Recomendación { get; set; }
}

public class InformacionPractica
{
    public PrecioOrientativo Precio_orientativo { get; set; }
    public List<string> portadaURL { get; set; } // Agregado para URLs de portada  
    public List<string> Disponibilidad { get; set; }
    public string Público_recomendado { get; set; }
    public string Traducciones { get; set; }
}

public class PrecioOrientativo
{
    public string Tapa_dura { get; set; }
    public string Rústica { get; set; }
    public string eBook { get; set; }
}

public class BookNote
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("partitionKey")]
    public string PartitionKey { get; set; } // userId para particionado en CosmosDB

    [JsonProperty("bookId")]
    [Required]
    public string BookId { get; set; }

    [JsonProperty("userId")]
    [Required]
    public string UserId { get; set; }

    [JsonProperty("tipo")]
    [Required]
    public BookNoteType Tipo { get; set; }

    [JsonProperty("titulo")]
    [Required]
    [StringLength(200)]
    public string Titulo { get; set; }

    [JsonProperty("contenido")]
    [Required]
    public string Contenido { get; set; } // Rich text content (HTML)

    [JsonProperty("capitulo")]
    public string Capitulo { get; set; } // "Capítulo 5", "Introducción", etc.

    [JsonProperty("pagina")]
    public int? Pagina { get; set; }

    [JsonProperty("ubicacion")]
    public string Ubicacion { get; set; } // "Mitad del capítulo", "Final del libro"

    [JsonProperty("fecha")]
    [Required]
    public DateTime Fecha { get; set; }

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new List<string>();

    [JsonProperty("destacada")]
    public bool Destacada { get; set; } = false;

    [JsonProperty("color")]
    public string Color { get; set; } = "#3B82F6"; // Color por defecto azul

    // === METADATOS ===
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tipos de notas disponibles
/// </summary>
public enum BookNoteType
{
    [JsonProperty("Nota")]
    Nota,

    [JsonProperty("Cita")]
    Cita,

    [JsonProperty("Reflexión")]
    Reflexion,

    [JsonProperty("Resumen")]
    Resumen,

    [JsonProperty("Pregunta")]
    Pregunta,

    [JsonProperty("Conexión")]
    Conexion
}
 
public class BookAnalysisResult
{
    /// <summary>
    /// Respuesta completa del análisis
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Información específica del libro extraída
    /// </summary>
    public Book BookInformation { get; set; } = new Book();

    /// <summary>
    /// Disclaimer sobre la información
    /// </summary>
    public string Disclaimer { get; set; } = string.Empty;
}