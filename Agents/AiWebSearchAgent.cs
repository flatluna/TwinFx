using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agente especializado en búsquedas web inteligentes usando Azure AI Agents con Bing Grounding
    /// ========================================================================
    /// 
    /// Este agente utiliza Azure AI Agents con Bing Grounding para:
    /// - Realizar búsquedas web comprensivas sobre cualquier tema
    /// - Proporcionar información actualizada y relevante con fuentes
    /// - Procesar consultas de usuarios y retornar respuestas estructuradas
    /// - Utilizar Bing Search con grounding para información confiable
    /// 
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class AiWebSearchAgent
    {
        private static readonly HttpClient client = new HttpClient();
        private readonly ILogger<AiWebSearchAgent> _logger;
        private readonly IConfiguration _configuration;
        private Kernel? _kernel;
        
        // Azure AI Foundry configuration for Bing Grounding
        private const string PROJECT_ENDPOINT = "https://twinet-resource.services.ai.azure.com/api/projects/twinet";
        private const string MODEL_DEPLOYMENT_NAME = "gpt4mini";
        private const string BING_CONNECTION_ID = "/subscriptions/bf5f11e8-1b22-4e27-b55e-8542ff6dec42/resourceGroups/rg-jorgeluna-7911/providers/Microsoft.CognitiveServices/accounts/twinet-resource/projects/twinet/connections/twinbing";

        public AiWebSearchAgent(ILogger<AiWebSearchAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _logger.LogInformation("🌐 AiWebSearchAgent initialized for intelligent web search with Bing Grounding");
        }

        /// <summary>
        /// Realiza una búsqueda web inteligente usando Azure AI Agents con Bing Grounding
        /// Recibe el prompt del usuario con lo que quiere buscar
        /// </summary>
        /// <param name="userPrompt">Prompt del usuario con la consulta de búsqueda</param>
        /// <returns>Respuesta estructurada con información web encontrada en formato JSON</returns>
        public async Task<string> BingGroundingSearchAsync(string userPrompt)
        {
            SearchResults Search = new SearchResults();
            _logger.LogInformation("🔧 Attempting Bing Grounding Search with Azure AI Agents");
            _logger.LogInformation("🔍 User prompt: {UserPrompt}", userPrompt);

            try
            {
                // Validar entrada
                if (string.IsNullOrEmpty(userPrompt))
                {
                    _logger.LogWarning("⚠️ Empty user prompt provided");
                    return "⚠️ Empty user prompt provided";
                }

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
                    name: "web-search-agent",
                    instructions: "Use the bing grounding tool to search for comprehensive web information. Provide detailed, accurate information with sources. Focus on current and relevant information. Always include citations and links when available.",
                    tools: [bingGroundingTool]
                );

                _logger.LogInformation("✅ Azure AI Agent created successfully");

                // Step 3: Create a thread and run with enhanced prompt
                var thread = await agentClient.Threads.CreateThreadAsync();

                var message = await agentClient.Messages.CreateMessageAsync(
                    thread.Value.Id,
                    MessageRole.User,
                    $"Search for comprehensive web information about: {userPrompt}. Provide detailed, accurate information with sources and citations.");

                var run = await agentClient.Runs.CreateRunAsync(thread.Value.Id, agent.Value.Id);

                _logger.LogInformation("🚀 Search run initiated, waiting for completion...");

                // Step 4: Wait for the agent to complete
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                    run = await agentClient.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
                }
                while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress);

                if (run.Value.Status != RunStatus.Completed)
                {
                    var errorMessage = $"Bing Grounding run failed: {run.Value.LastError?.Message}";
                    _logger.LogError("❌ {ErrorMessage}", errorMessage);
                    return errorMessage;
                }

                _logger.LogInformation("✅ Search run completed successfully");

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

                                // Process annotations and citations
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

                // Step 6: Clean up resources
                try
                {
                    await agentClient.Threads.DeleteThreadAsync(threadId: thread.Value.Id);
                    await agentClient.Administration.DeleteAgentAsync(agentId: agent.Value.Id);
                    _logger.LogInformation("🧹 Azure AI Agent resources cleaned up successfully");
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "⚠️ Warning during cleanup of Azure AI Agent resources");
                }

                // Step 7: Validate and return results
                if (searchResults.Count == 0)
                {
                    var noResultsMessage = "No se encontraron resultados en la búsqueda web";
                    _logger.LogWarning("📭 {NoResultsMessage} for prompt: {UserPrompt}", noResultsMessage, userPrompt);
                    return "No se encontraron resultados en la búsqueda web";
                }
                var aiResponse = searchResults[0];
                //  var searchResultsData = JsonConvert.DeserializeObject<SearchResults>(aiResponse);
                return aiResponse;
            }

                 
                 
                 
          
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during web search for prompt: {UserPrompt}", userPrompt);
                return ex.Message;
            }
        }

        
        public async Task<SearchResults> GoolgSearch(string Question)
        {
            SearchResults searchResultsData = new SearchResults();
            string apiKey = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo"; // Replace with your API key  
            string searchEngineId = "b07503c9152af4456"; // Replace with your Search Engine ID  
            string query = Question; // Replace with your search query  
            string Response = "";
            string url = $"https://www.googleapis.com/customsearch/v1?key={apiKey}&cx={searchEngineId}&q={Uri.EscapeDataString(query)}";

            try
            {
                var response = await client.GetStringAsync(url);
                var AIResponse =  await AnalyzeGoogleSearch(Question, response);
                searchResultsData = JsonConvert.DeserializeObject<SearchResults>(AIResponse);
                Console.WriteLine(response);
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("Request error: " + e.Message);
            }

            return searchResultsData;
        }


        public async Task<string> GoolgSearchOnly(string Question)
        {
            SearchResults searchResultsData = new SearchResults();
            string apiKey = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo"; // Replace with your API key  
            string searchEngineId = "b07503c9152af4456"; // Replace with your Search Engine ID  
            string query = Question; // Replace with your search query  
            string Response = "";
            string url = $"https://www.googleapis.com/customsearch/v1?key={apiKey}&cx={searchEngineId}&q={Uri.EscapeDataString(query)}";

            try
            {
                var response = await client.GetStringAsync(url);
               return response;
               
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("Request error: " + e.Message);
            }

            return "Search FAILED";
        }
        public async Task<GoogleSearchResults> GoolgSearchSimple(string Question)
        {
            GoogleSearchResults searchResultsData = new GoogleSearchResults();
            string apiKey = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo"; // Replace with your API key  
            string searchEngineId = "b07503c9152af4456"; // Replace with your Search Engine ID  
            string query = Question; // Replace with your search query  
            string Response = "";
            string url = $"https://www.googleapis.com/customsearch/v1?key={apiKey}&cx={searchEngineId}&q={Uri.EscapeDataString(query)}";

            try
            {
                var response = await client.GetStringAsync(url);
                searchResultsData = await AnalyzeGoogleSearchSimple(Question, response);
                string SinipetsData = "";


                Console.WriteLine(response);
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("Request error: " + e.Message);
            }

            return searchResultsData;
        }

        public async Task<string> AnalyzeGoogleSearch(string Question, string Results)
        {
            _logger.LogInformation("🔍 Starting Google Search analysis for: {Question}", Question);

            try
            {
                // Validar entrada
                if (string.IsNullOrEmpty(Question) || string.IsNullOrEmpty(Results))
                {
                    return CreateErrorSearchResults("Question and Results are required");
                }

                // Inicializar el contador de tokens
                var tokenCounter = new TwinFx.Services.AiTokrens();

                // Contar tokens en Results original
                int originalResultsTokens = tokenCounter.GetTokenCount(Results);
                _logger.LogInformation("📊 Original Results tokens: {TokenCount}", originalResultsTokens);

                // Inicializar Semantic Kernel
                await InitializeKernelAsync();

                // Deserializar y extraer solo los datos importantes para AI
                var searchResponse = JsonConvert.DeserializeObject<GoogleSearchResults>(Results);
                var simplifiedData = ExtractEssentialSearchData(searchResponse);
                var searchDataJson = JsonConvert.SerializeObject(simplifiedData, Formatting.Indented);

                // Contar tokens en datos simplificados
                int simplifiedDataTokens = tokenCounter.GetTokenCount(searchDataJson);
                _logger.LogInformation("📊 Simplified Data tokens: {TokenCount}", simplifiedDataTokens);
                _logger.LogInformation("📈 Token reduction: {OriginalTokens} → {SimplifiedTokens} ({ReductionPercent:F1}% reduction)", 
                    originalResultsTokens, simplifiedDataTokens, 
                    originalResultsTokens > 0 ? ((originalResultsTokens - simplifiedDataTokens) * 100.0 / originalResultsTokens) : 0);
                
                // Crear prompt para análisis de resultados de Google con datos simplificados
                var analysisPrompt = CreateGoogleAnalysisPrompt(Question, simplifiedData);

                var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(analysisPrompt);

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

                _logger.LogInformation("✅ Google Search analysis completed successfully");

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

                // Validate JSON response
                try
                {
                    var testParse = JsonSerializer.Deserialize<SearchResults>(cleanResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    return cleanResponse;
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogWarning(jsonEx, "⚠️ Failed to parse AI response as JSON, creating fallback response");
                    return CreateFallbackSearchResults(Question, aiResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing Google Search results for: {Question}", Question);
                return CreateErrorSearchResults($"Error analyzing search results: {ex.Message}");
            }
        }
        public async Task<GoogleSearchResults> AnalyzeGoogleSearchSimple(string Question, string Results)
        {
            _logger.LogInformation("🔍 Starting Google Search analysis for: {Question}", Question);

            try
            {
                // Validar entrada
                if (string.IsNullOrEmpty(Question) || string.IsNullOrEmpty(Results))
                {
                    return null;
                }

                // Inicializar el contador de tokens
                var tokenCounter = new TwinFx.Services.AiTokrens();

                // Contar tokens en Results original
                int originalResultsTokens = tokenCounter.GetTokenCount(Results);
                _logger.LogInformation("📊 Original Results tokens: {TokenCount}", originalResultsTokens);

                // Inicializar Semantic Kernel
                await InitializeKernelAsync();

                // Deserializar y extraer solo los datos importantes para AI
                var searchResponse = JsonConvert.DeserializeObject<GoogleSearchResults>(Results);

                // *** NUEVA LÓGICA LÍNEA 234: Generar respuesta unificada usando Semantic Kernel ***
                if (searchResponse?.Items != null && searchResponse.Items.Any())
                {
                    // Extraer todos los snippets de los resultados de búsqueda
                    var allSnippets = string.Join("\n\n", searchResponse.Items
                        .Where(item => !string.IsNullOrEmpty(item.Snippet))
                        .Select((item, index) => $"[Resultado {index + 1}] {item.Title}\n{item.Snippet}\nFuente: {item.DisplayLink}")
                    );
                    var datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    // Crear prompt simple para generar respuesta unificada
                    var unifiedResponsePrompt = $@"
Eres un experto analista de información. Te han dado una pregunta y resultados de búsqueda de Google.
El dia de hoy es:  {datetime} . Usa esa fecha para indicar fecha y hora.
Importante: No inventes nada que no esté en los resultados. PEro trata de poner todo lo que encuentres en detalle
PREGUNTA DEL USUARIO:
{Question}

RESULTADOS DE BÚSQUEDAS:
{allSnippets}

INSTRUCCIONES:

IMPORTANTE: No digas ex presidente Trump o Ex XXXX a menos que el texto lo indique asi no inventes nada
Crea una respuesta unificada y coherente que responda la pregunta del usuario basándote ÚNICAMENTE en los resultados de búsqueda proporcionados.
No inventes nada como por ejemplo el expresidente xxxx por que no sabes si es o no presidente aun. 
SOlo pon lo qu ves .
FORMATO DE RESPUESTA:
- Respuesta clara y directa
- Integra la información de múltiples fuentes
- Menciona las fuentes cuando sea relevante
- Máximo 300 palabras
- En español
Importante: La respuesta la quiero en formato HTML , 
quiero que pongas todos los links no solo tres todos explicando y dando un resumen al principio.
usa un background elegante que no sea muy claro que sea profesionalcon listas, negritas, cursivas, colores, imágenes y formato visual atractivo.
Nunca uses el ```html al principio es problematico

IMPORTANTE: Quiero que veas la pregunta y la respuesta si esat contine iformacion tipo:
Sexual, ofenciba, racista, etc contesta que no puedes buscar informacion de este tipo en forma correcta y profesional
Responde de manera natural y conversacional:";

                    // Obtener respuesta de Semantic Kernel
                    var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                    var chatHistory = new ChatHistory();
                    chatHistory.AddUserMessage(unifiedResponsePrompt);

                    var executionSettings = new PromptExecutionSettings
                    {
                        ExtensionData = new Dictionary<string, object>
                        {
                            ["max_completion_tokens"] = 500,
                            ["temperature"] = 0.3
                        }
                    };

                    var response = await chatCompletionService.GetChatMessageContentAsync(
                        chatHistory,
                        executionSettings,
                        _kernel);

                    var unifiedResponse = response.Content ?? "";
                    searchResponse.ResponseHTML = unifiedResponse;
                    // Agregar la respuesta unificada al primer item como campo adicional
                 
                    _logger.LogInformation("✅ Unified response generated successfully using Semantic Kernel");
                }

                return searchResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing Google Search results for: {Question}", Question);
                return null;
            }
        }

        /// <summary>
        /// Extrae solo los datos esenciales de Google Search para análisis de AI
        /// Optimizado para extraer TODAS las imágenes disponibles
        /// </summary>
        private SimpleGoogleSearchData ExtractEssentialSearchData(GoogleSearchResults searchResponse)
        {
            var simplifiedData = new SimpleGoogleSearchData
            {
                TotalResults = searchResponse?.SearchInformation?.TotalResults ?? "0",
                SearchTime = searchResponse?.SearchInformation?.SearchTime.ToString() ?? "0",
                Results = new List<SimpleSearchItem>()
            };

            if (searchResponse?.Items != null)
            {
                foreach (var item in searchResponse.Items.Take(5)) // Limitar a 10 resultados
                {
                    var simpleItem = new SimpleSearchItem
                    {
                        Title = item.Title ?? "",
                        Link = item.Link ?? "",
                        Snippet = item.Snippet ?? "",
                        DisplayLink = item.DisplayLink ?? "",
                        Images = new List<string>()
                    };

                    // EXTRACCIÓN COMPLETA DE IMÁGENES DE TODAS LAS FUENTES DISPONIBLES
                    if (item.PageMap != null)
                    {
                        // 1. Extraer de cse_thumbnail (thumbnails de Google)
                        if (item.PageMap.CseThumbnail != null)
                        {
                            foreach (var thumbnail in item.PageMap.CseThumbnail)
                            {
                                if (!string.IsNullOrEmpty(thumbnail.Src) && 
                                    !simpleItem.Images.Contains(thumbnail.Src))
                                {
                                    simpleItem.Images.Add(thumbnail.Src);
                                    _logger.LogDebug("🖼️ Added cse_thumbnail: {Src}", thumbnail.Src);
                                }
                            }
                        }

                        // 2. Extraer de cse_image (imágenes principales)
                        if (item.PageMap.CseImage != null)
                        {
                            foreach (var cseImage in item.PageMap.CseImage)
                            {
                                if (!string.IsNullOrEmpty(cseImage.Src) && 
                                    !simpleItem.Images.Contains(cseImage.Src) &&
                                    !cseImage.Src.StartsWith("x-raw-image://")) // Filtrar imágenes raw no utilizables
                                {
                                    simpleItem.Images.Add(cseImage.Src);
                                    _logger.LogDebug("🖼️ Added cse_image: {Src}", cseImage.Src);
                                }
                            }
                        }

                        // 3. Extraer de metatags (og:image y otras imágenes de metadatos)
                        if (item.PageMap.Metatags != null)
                        {
                            foreach (var metaTag in item.PageMap.Metatags)
                            {
                                // Extraer og:image
                                if (!string.IsNullOrEmpty(metaTag.OgImage) && 
                                    !simpleItem.Images.Contains(metaTag.OgImage))
                                {
                                    simpleItem.Images.Add(metaTag.OgImage);
                                    _logger.LogDebug("🖼️ Added og:image: {OgImage}", metaTag.OgImage);
                                }

                                // Extraer twitter:image
                                if (!string.IsNullOrEmpty(metaTag.TwitterImage) && 
                                    !simpleItem.Images.Contains(metaTag.TwitterImage))
                                {
                                    simpleItem.Images.Add(metaTag.TwitterImage);
                                    _logger.LogDebug("🖼️ Added twitter:image: {TwitterImage}", metaTag.TwitterImage);
                                }

                                // Buscar otras posibles imágenes en metadatos usando reflexión
                                var properties = typeof(MetaTag).GetProperties();
                                foreach (var prop in properties)
                                {
                                    var value = prop.GetValue(metaTag)?.ToString();
                                    if (!string.IsNullOrEmpty(value) && 
                                        IsImageUrl(value) && 
                                        !simpleItem.Images.Contains(value))
                                    {
                                        simpleItem.Images.Add(value);
                                        _logger.LogDebug("🖼️ Added meta image ({PropName}): {Value}", prop.Name, value);
                                    }
                                }
                            }
                        }
                    }

                    // 4. Buscar URLs de imágenes en el snippet del resultado
                    if (!string.IsNullOrEmpty(item.Snippet))
                    {
                        var imageUrlsInSnippet = ExtractImageUrlsFromText(item.Snippet);
                        foreach (var imgUrl in imageUrlsInSnippet)
                        {
                            if (!simpleItem.Images.Contains(imgUrl))
                            {
                                simpleItem.Images.Add(imgUrl);
                                _logger.LogDebug("🖼️ Added snippet image: {ImgUrl}", imgUrl);
                            }
                        }
                    }

                    // 5. Buscar URLs de imágenes en el título (raro, pero posible)
                    if (!string.IsNullOrEmpty(item.Title))
                    {
                        var imageUrlsInTitle = ExtractImageUrlsFromText(item.Title);
                        foreach (var imgUrl in imageUrlsInTitle)
                        {
                            if (!simpleItem.Images.Contains(imgUrl))
                            {
                                simpleItem.Images.Add(imgUrl);
                                _logger.LogDebug("🖼️ Added title image: {ImgUrl}", imgUrl);
                            }
                        }
                    }

                    // Log del total de imágenes encontradas para este resultado
                    _logger.LogInformation("📊 Result '{Title}': Found {ImageCount} images", 
                        item.Title ?? "Unknown", simpleItem.Images.Count);

                    simplifiedData.Results.Add(simpleItem);
                }
            }

            // Log del total general
            var totalImages = simplifiedData.Results.Sum(r => r.Images.Count);
            _logger.LogInformation("🖼️ TOTAL IMAGES EXTRACTED: {TotalImages} across {ResultCount} results", 
                totalImages, simplifiedData.Results.Count);

            return simplifiedData;
        }

        /// <summary>
        /// Determina si una URL es una imagen válida basándose en la extensión
        /// </summary>
        private bool IsImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.ToLowerInvariant();
                
                // Extensiones de imagen comunes
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff", ".tif" };
                
                return imageExtensions.Any(ext => path.EndsWith(ext)) ||
                       url.Contains("image") ||
                       url.Contains("photo") ||
                       url.Contains("picture") ||
                       url.Contains("img") ||
                       url.Contains("thumbnail");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extrae URLs de imágenes de un texto usando patrones regex
        /// </summary>
        private List<string> ExtractImageUrlsFromText(string text)
        {
            var imageUrls = new List<string>();
            
            if (string.IsNullOrEmpty(text)) return imageUrls;
            
            try
            {
                // Patrón regex para encontrar URLs de imágenes
                var urlPattern = @"https?://[^\s]+\.(?:jpg|jpeg|png|gif|webp|bmp|svg|ico|tiff|tif)(?:\?[^\s]*)?";
                var matches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var url = match.Value;
                    if (!imageUrls.Contains(url))
                    {
                        imageUrls.Add(url);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error extracting image URLs from text");
            }
            
            return imageUrls;
        }

        /// <summary>
        /// Actualiza el prompt para usar los datos simplificados
        /// </summary>
        private string CreateGoogleAnalysisPrompt(string question, SimpleGoogleSearchData searchData)
        {
            // Convertir los datos simplificados a JSON string para el prompt
            var searchDataJson = JsonConvert.SerializeObject(searchData, Formatting.Indented);

            return $@"🌐 **Especialista en Análisis de Búsquedas Web**

Eres un experto analista que procesa resultados de búsqueda de Google y proporciona respuestas estructuradas y comprensivas.

**CONSULTA ORIGINAL DEL USUARIO:**
{question}

**DATOS SIMPLIFICADOS DE GOOGLE SEARCH:**
{searchDataJson}

**INSTRUCCIONES:**
Analiza los resultados de búsqueda de Google y proporciona una respuesta estructurada en JSON con esta estructura EXACTA:
Quiero por lo menos unos 6 links y resultados

{{
  ""resumenEjecutivo"": ""Resumen ejecutivo de 2-3 párrafos con los hallazgos más importantes basados en los resultados de Google"",
  ""htmlDetalles"": ""<div style='font-family: Arial, sans-serif; max-width: 900px; margin: 0 auto; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border-radius: 15px; color: white;'><h2 style='color: #fff; text-align: center; margin-bottom: 30px;'>🌐 ANÁLISIS DE RESULTADOS DE GOOGLE</h2><div style='background: rgba(255,255,255,0.1); padding: 20px; border-radius: 10px; margin: 15px 0;'><h3 style='color: #e8f4f8; margin: 0 0 15px 0; display: flex; align-items: center;'><span style='margin-right: 10px;'>🔍</span>Información Encontrada</h3><div style='background: rgba(255,255,255,0.95); color: #333; padding: 20px; border-radius: 8px; line-height: 1.6;'>[INFORMACIÓN DETALLADA EXTRAÍDA DE GOOGLE CON COLORES, LISTAS, GRIDS Y FORMATO VISUAL ATRACTIVO]</div></div><div style='background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 15px 0;'><h3 style='color: #e8f4f8; margin: 0 0 15px 0;'>🔗 Fuentes de Google</h3><ul style='list-style: none; padding: 0; margin: 0;'>[LISTA DE FUENTES EXTRAÍDAS DE GOOGLE CON ENLACES]</ul></div></div>"",
  ""resultadosBusqueda"": [
    {{ 
      ""titulo"": ""Título extraído de Google"",
      ""contenido"": ""Contenido detallado basado en snippet y descripción de Google"",
      ""fuente"": ""Dominio/sitio web de Google"",
      ""url"": ""URL real extraída de Google"",
      ""relevancia"": ""alta/media/baja"",
      ""fechaPublicacion"": ""Fecha si está disponible"",
      ""precios"": ""Precios mencionados en los resultados si aplica"",
      ""categoria"": ""categoría del contenido"",
      ""fotos"": [""URLs de imágenes extraídas de Google""]
    }}
  ] 
}}

**CRITERIOS DE CALIDAD:**
✅ Usa SOLO la información encontrada en los datos simplificados de Google proporcionados
✅ Extrae URLs reales de los resultados de Google
✅ Incluye datos específicos, fechas, números cuando estén disponibles en Google
✅ Analiza snippets y títulos de Google para extraer información relevante
✅ Crea HTML visualmente atractivo basado en la información de Google
✅ Evalúa la confiabilidad basándose en los dominios de Google
✅ Extrae precios, fechas y datos específicos de los resultados de Google
✅ Incluye todas las URLs de imágenes disponibles en el campo 'Images' de cada resultado
IMPORTANTE: Extrae todas las fotos posibles de cada resultado usando el campo 'Images'

❌ NO inventes URLs o datos que no estén en los resultados de Google
❌ NO incluyas información no relacionada con los resultados proporcionados
❌ NO comiences con ```json o termines con ```
❌ NO agregues información que no esté en los resultados de Google

**FORMATO:**
Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después;";
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
                _logger.LogInformation("🔧 Initializing Semantic Kernel for AiWebSearchAgent");

                // Crear kernel builder
                IKernelBuilder builder = Kernel.CreateBuilder();

                // Obtener configuración de Azure OpenAI
                var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ??
                              _configuration.GetValue<string>("AzureOpenAI:Endpoint") ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

                var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ??
                            _configuration.GetValue<string>("AzureOpenAI:ApiKey") ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

                // Usar GPT-5 mini como se especificó
                var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ??
                                    _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ??
                                    "gpt-5-mini";
                deploymentName = "gpt4mini";
                _logger.LogInformation("📚 Using deployment: {DeploymentName} for search analysis", deploymentName);

                // Agregar Azure OpenAI chat completion
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: deploymentName,
                    endpoint: endpoint,
                    apiKey: apiKey);

                // Construir el kernel
                _kernel = builder.Build();

                _logger.LogInformation("✅ Semantic Kernel initialized successfully for AiWebSearchAgent with {DeploymentName}", deploymentName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for AiWebSearchAgent");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Crea el prompt para análisis de resultados de Google Search
        /// </summary>
        private string CreateGoogleAnalysisPrompt(string question, string results)
        {
            return $@"🌐 **Especialista en Análisis de Búsquedas Web**

Eres un experto analista que procesa resultados de búsqueda de Google y proporciona respuestas estructuradas y comprensivas.

**CONSULTA ORIGINAL DEL USUARIO:**
{question}

**RESULTADOS DE GOOGLE SEARCH:**
{results}

**INSTRUCCIONES:**
Analiza los resultados de búsqueda de Google y proporciona una respuesta estructurada en JSON con esta estructura EXACTA:
Quiero por lo menos unos 6 links y resultados
{{
  ""resumenEjecutivo"": ""Resumen ejecutivo de 2-3 párrafos con los hallazgos más importantes basados en los resultados de Google"",
  ""htmlDetalles"": ""<div style='font-family: Arial, sans-serif; max-width: 900px; margin: 0 auto; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border-radius: 15px; color: white;'><h2 style='color: #fff; text-align: center; margin-bottom: 30px;'>🌐 ANÁLISIS DE RESULTADOS DE GOOGLE</h2><div style='background: rgba(255,255,255,0.1); padding: 20px; border-radius: 10px; margin: 15px 0;'><h3 style='color: #e8f4f8; margin: 0 0 15px 0; display: flex; align-items: center;'><span style='margin-right: 10px;'>🔍</span>Información Encontrada</h3><div style='background: rgba(255,255,255,0.95); color: #333; padding: 20px; border-radius: 8px; line-height: 1.6;'>[INFORMACIÓN DETALLADA EXTRAÍDA DE GOOGLE CON COLORES, LISTAS, GRIDS Y FORMATO VISUAL ATRACTIVO]</div></div><div style='background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 15px 0;'><h3 style='color: #e8f4f8; margin: 0 0 15px 0;'>🔗 Fuentes de Google</h3><ul style='list-style: none; padding: 0; margin: 0;'>[LISTA DE FUENTES EXTRAÍDAS DE GOOGLE CON ENLACES]</ul></div></div>"",
  ""resultadosBusqueda"": [
    {{ 
      ""titulo"": ""Título extraído de Google"",
      ""contenido"": ""Contenido detallado basado en snippet y descripción de Google"",
      ""fuente"": ""Dominio/sitio web de Google"",
      ""url"": ""URL real extraída de Google"",
      ""relevancia"": ""alta/media/baja"",
      ""fechaPublicacion"": ""Fecha si está disponible"",
      ""precios"": ""Precios mencionados en los resultados si aplica"",
      ""categoria"": ""categoría del contenido"",
      ""fotos"": [""URLs de imágenes si están disponibles en Google""]
    }}
  ] 
}}

**CRITERIOS DE CALIDAD:**
✅ Usa SOLO la información encontrada en los resultados de Google proporcionados
✅ Extrae URLs reales de los resultados de Google
✅ Incluye datos específicos, fechas, números cuando estén disponibles en Google
✅ Analiza snippets y títulos de Google para extraer información relevante
✅ Crea HTML visualmente atractivo basado en la información de Google
✅ Evalúa la confiabilidad basándose en los dominios de Google
✅ Extrae precios, fechas y datos específicos de los resultados de Google
✅ Identifica y extrae URLs de imágenes si están disponibles
IMPORTANTE: Extrae todas las fotos posibles de cada link 

❌ NO inventes URLs o datos que no estén en los resultados de Google
❌ NO incluyas información no relacionada con los resultados proporcionados
❌ NO comiences con ```json o termines con ```
❌ NO agregues información que no esté en los resultados de Google

**FORMATO:**
Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después;";
        }

        /// <summary>
        /// Crea una respuesta de error en formato JSON
        /// </summary>
        private string CreateErrorSearchResults(string errorMessage)
        {
            var errorResponse = new SearchResults
            {
                ResumenEjecutivo = $"Error en el análisis: {errorMessage}",
                HtmlDetalles = $"""
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background: linear-gradient(135deg, #ff6b6b 0%, #ee5a52 100%); border-radius: 15px; color: white;'>
                        <h2 style='color: #fff; text-align: center; margin-bottom: 20px;'>❌ Error en el Análisis</h2>
                        <div style='background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px;'>
                            <p style='margin: 0; font-size: 16px; line-height: 1.6;'><strong>Error:</strong> {errorMessage}</p>
                            <p style='margin: 10px 0 0 0; font-size: 14px;'><strong>Fecha:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>
                        </div>
                    </div>
                    """,
                ResultadosBusqueda = new List<ResultadoBusqueda>(),
                LinksYFuentes = new List<LinkFuente>(),
                DatosEspecificos = new DatosEspecificos
                {
                    Fechas = new List<string>(),
                    Numeros = new List<string>(),
                    Estadisticas = new List<string>(),
                    Precios = new List<string>(),
                    Ubicaciones = new List<string>()
                },
                AnalisisContexto = new AnalisisContexto
                {
                    Tendencias = "Error en análisis",
                    Impacto = "No disponible",
                    Perspectivas = "Error",
                    Actualidad = "No analizable"
                },
                Recomendaciones = new List<string> { "Intenta reformular tu consulta", "Verifica los resultados de Google", "Contacta al soporte técnico" },
                PalabrasClave = new List<string>(),
                NivelConfianza = "bajo",
                Metadatos = new Dictionary<string, object>
                {
                    ["totalFuentes"] = 0,
                    ["tiempoConsulta"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
                    ["consultaOriginal"] = "",
                    ["categoria"] = "error"
                }
            };

            return JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        /// <summary>
        /// Crea una respuesta de fallback cuando la respuesta AI no es JSON válido
        /// </summary>
        private string CreateFallbackSearchResults(string question, string aiResponse)
        {
            var fallbackResponse = new SearchResults
            {
                ResumenEjecutivo = "Se procesó la información pero en formato no estructurado.",
                HtmlDetalles = $"""
                    <div style='font-family: Arial, sans-serif; max-width: 900px; margin: 0 auto; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border-radius: 15px; color: white;'>
                        <h2 style='color: #fff; text-align: center; margin-bottom: 30px;'>🌐 ANÁLISIS DE BÚSQUEDA GOOGLE</h2>
                        <div style='background: rgba(255,255,255,0.1); padding: 20px; border-radius: 10px; margin: 15px 0;'>
                            <h3 style='color: #e8f4f8; margin: 0 0 15px 0; display: flex; align-items: center;'>
                                <span style='margin-right: 10px;'>🔍</span>Consulta: {question}
                            </h3>
                            <div style='background: rgba(255,255,255,0.95); color: #333; padding: 20px; border-radius: 8px; line-height: 1.6; white-space: pre-wrap;'>
                                {aiResponse.Replace("<", "&lt;").Replace(">", "&gt;")}
                            </div>
                        </div>
                    </div>
                    """,
                ResultadosBusqueda = new List<ResultadoBusqueda>
                {
                    new ResultadoBusqueda
                    {
                        Titulo = "Análisis de búsqueda Google",
                        Contenido = aiResponse,
                        Fuente = "Análisis AI de resultados Google",
                        Url = "",
                        Relevancia = "alta",
                        FechaPublicacion = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        Categoria = "análisis búsqueda",
                        Fotos = new string[0]
                    }
                },
                LinksYFuentes = new List<LinkFuente>(),
                DatosEspecificos = new DatosEspecificos
                {
                    Fechas = new List<string> { DateTime.UtcNow.ToString("yyyy-MM-dd") },
                    Numeros = new List<string>(),
                    Estadisticas = new List<string>(),
                    Precios = new List<string>(),
                    Ubicaciones = new List<string>()
                },
                AnalisisContexto = new AnalisisContexto
                {
                    Tendencias = "Información procesada con AI",
                    Impacto = "Relevante para la consulta",
                    Perspectivas = "Basado en resultados Google",
                    Actualidad = "Procesado en tiempo real"
                },
                Recomendaciones = new List<string>
                {
                    "Revisar la información detallada",
                    "Verificar datos en fuentes primarias",
                    "Considerar búsquedas adicionales"
                },
                PalabrasClave = question.Split(' ').Take(5).ToList(),
                NivelConfianza = "medio",
                Metadatos = new Dictionary<string, object>
                {
                    ["totalFuentes"] = 1,
                    ["tiempoConsulta"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
                    ["consultaOriginal"] = question,
                    ["categoria"] = "análisis fallback"
                }
            };

            return JsonSerializer.Serialize(fallbackResponse, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
    }

    public class ResultadoBusqueda
    {
        public string Titulo { get; set; }
        public string Contenido { get; set; }
        public string Fuente { get; set; }
        public string Url { get; set; }
        public string Relevancia { get; set; }
        public string FechaPublicacion { get; set; }
        public string Precios { get; set; }
        public string Categoria { get; set; }

        public string[] Fotos { get; set; }
    }

    public class LinkFuente
    {
        public string Titulo { get; set; }
        public string Url { get; set; }
        public string Descripcion { get; set; }
        public string TipoFuente { get; set; }
        public string Confiabilidad { get; set; }
    }

    public class DatosEspecificos
    {
        public List<string> Fechas { get; set; }
        public List<string> Numeros { get; set; }
        public List<string> Estadisticas { get; set; }
        public List<string> Precios { get; set; }
        public List<string> Ubicaciones { get; set; }
    }

    public class AnalisisContexto
    {
        public string Tendencias { get; set; }
        public string Impacto { get; set; }
        public string Perspectivas { get; set; }
        public string Actualidad { get; set; }
    }

    public class SearchResults
    {
        public string ResumenEjecutivo { get; set; }
        public string HtmlDetalles { get; set; }
        public List<ResultadoBusqueda> ResultadosBusqueda { get; set; }
        public List<LinkFuente> LinksYFuentes { get; set; }
        public DatosEspecificos DatosEspecificos { get; set; }
        public AnalisisContexto AnalisisContexto { get; set; }
        public List<string> Recomendaciones { get; set; }
        public List<string> PalabrasClave { get; set; }
        public string NivelConfianza { get; set; }
        public Dictionary<string, object> Metadatos { get; set; }
    }

    public class GoogleSearchResults
    {
        public string Kind { get; set; }

        public string ResponseHTML { get; set; }
        public Url Url { get; set; }
        public Queries Queries { get; set; }
        public Context Context { get; set; }
        public SearchInformation SearchInformation { get; set; }
        public Spelling Spelling { get; set; }
        public List<Item> Items { get; set; }
    }

    public class Url
    {
        public string Type { get; set; }
        public string Template { get; set; }
    }

    public class Queries
    {
        public List<Request> Request { get; set; }
        public List<NextPage> NextPage { get; set; }
    }

    public class Request
    {
        public string Title { get; set; }
        public string TotalResults { get; set; }
        public string SearchTerms { get; set; }
        public int Count { get; set; }
        public int StartIndex { get; set; }
        public string InputEncoding { get; set; }
        public string OutputEncoding { get; set; }
        public string Safe { get; set; }
        public string Cx { get; set; }
    }

    public class NextPage : Request
    {
        // Inherits all properties from Request  
    }

    public class Context
    {
        public string Title { get; set; }
    }

    public class SearchInformation
    {
        public double SearchTime { get; set; }
        public string FormattedSearchTime { get; set; }
        public string TotalResults { get; set; }
        public string FormattedTotalResults { get; set; }
    }

    public class Spelling
    {
        public string CorrectedQuery { get; set; }
        public string HtmlCorrectedQuery { get; set; }
    }

    public class Item
    {
        public string Kind { get; set; }
        public string Title { get; set; }
        public string HtmlTitle { get; set; }
        public string Link { get; set; }
        public string DisplayLink { get; set; }
        public string Snippet { get; set; }
        public string HtmlSnippet { get; set; }
        public string FormattedUrl { get; set; }
        public string HtmlFormattedUrl { get; set; }
        public PageMap PageMap { get; set; }
    }

    public class PageMap
    {
        [JsonProperty("hcard")]
        public List<HCard> HCard { get; set; }
        
        [JsonProperty("metatags")]
        public List<MetaTag> Metatags { get; set; }
        
        [JsonProperty("cse_thumbnail")]
        public List<CseThumbnail> CseThumbnail { get; set; }
        
        [JsonProperty("cse_image")]
        public List<CseImage> CseImage { get; set; }
    }

    public class HCard
    {
        [JsonProperty("fn")]
        public string Fn { get; set; }
    }

    public class MetaTag
    {
        [JsonProperty("referrer")]
        public string Referrer { get; set; }
        
        [JsonProperty("og:image")]
        public string OgImage { get; set; }
        
        [JsonProperty("theme-color")]
        public string ThemeColor { get; set; }
        
        [JsonProperty("og:image:width")]
        public string OgImageWidth { get; set; }
        
        [JsonProperty("og:type")]
        public string OgType { get; set; }
        
        [JsonProperty("viewport")]
        public string Viewport { get; set; }
        
        [JsonProperty("og:title")]
        public string OgTitle { get; set; }
        
        [JsonProperty("og:image:height")]
        public string OgImageHeight { get; set; }
        
        [JsonProperty("format-detection")]
        public string FormatDetection { get; set; }
        
        [JsonProperty("og:description")]
        public string OgDescription { get; set; }
        
        [JsonProperty("twitter:card")]
        public string TwitterCard { get; set; }
        
        [JsonProperty("og:site_name")]
        public string OgSiteName { get; set; }
        
        [JsonProperty("twitter:site")]
        public string TwitterSite { get; set; }
        
        [JsonProperty("twitter:image")]
        public string TwitterImage { get; set; }
        
        [JsonProperty("apple-itunes-app")]
        public string AppleItunesApp { get; set; }
        
        [JsonProperty("application-name")]
        public string ApplicationName { get; set; }
        
        [JsonProperty("apple-mobile-web-app-title")]
        public string AppleMobileWebAppTitle { get; set; }
        
        [JsonProperty("google")]
        public string Google { get; set; }
        
        [JsonProperty("og:locale")]
        public string OgLocale { get; set; }
        
        [JsonProperty("og:url")]
        public string OgUrl { get; set; }
        
        [JsonProperty("mobile-web-app-capable")]
        public string MobileWebAppCapable { get; set; }
        
        [JsonProperty("moddate")]
        public string ModDate { get; set; }
        
        [JsonProperty("creator")]
        public string Creator { get; set; }
        
        [JsonProperty("creationdate")]
        public string CreationDate { get; set; }
        
        [JsonProperty("producer")]
        public string Producer { get; set; }
    }

    public class CseThumbnail
    {
        [JsonProperty("src")]
        public string Src { get; set; }
        
        [JsonProperty("width")]
        public string Width { get; set; }
        
        [JsonProperty("height")]
        public string Height { get; set; }
    }

    public class CseImage
    {
        [JsonProperty("src")]
        public string Src { get; set; }
    }

    // ========================================
    // SIMPLIFIED GOOGLE SEARCH CLASSES - OPTIMIZED FOR AI ANALYSIS
    // ========================================

    /// <summary>
    /// Datos simplificados de Google Search optimizados para análisis de AI
    /// Contiene solo la información esencial para generar respuestas inteligentes
    /// </summary>
    public class SimpleGoogleSearchData
    {
        public string TotalResults { get; set; } = "0";
        public string SearchTime { get; set; } = "0";
        public List<SimpleSearchItem> Results { get; set; } = new List<SimpleSearchItem>();
    }

    /// <summary>
    /// Elemento de búsqueda simplificado con solo los datos más importantes
    /// </summary>
    public class SimpleSearchItem
    {
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
        public string Snippet { get; set; } = "";
        public string DisplayLink { get; set; } = "";
        public List<string> Images { get; set; } = new List<string>();
    }
}
