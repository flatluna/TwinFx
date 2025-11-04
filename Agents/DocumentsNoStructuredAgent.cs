using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure;
using Azure.Identity;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using TwinFx.Services;
using static TwinFx.Services.NoStructuredServices;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace TwinFx.Agents
{
    public class DocumentsNoStructuredAgent
    {
        private readonly ILogger<DocumentsNoStructuredAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;
        string DeploymentName = "";
        public DocumentsNoStructuredAgent(ILogger<DocumentsNoStructuredAgent> logger, IConfiguration configuration,
            string Model)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Get Azure OpenAI configuration
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ??
                              configuration["AzureOpenAI:Endpoint"] ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");

                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ??
                            configuration["AzureOpenAI:ApiKey"] ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

                var deploymentName = Model ?? configuration["Values:AzureOpenAI:DeploymentName"] ??
                                    configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";
                //  deploymentName = "gpt-5-mini
                deploymentName = Model;
                DeploymentName = Model;

                _logger.LogInformation("🔧 Using Azure OpenAI configuration:");
                _logger.LogInformation("   • Endpoint: {Endpoint}", endpoint);
                _logger.LogInformation("   • Deployment: {DeploymentName}", deploymentName);
                _logger.LogInformation("   • Auth: API Key");

                // Initialize Azure OpenAI clients using API Key authentication
                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _chatClient = _azureClient.GetChatClient(deploymentName);

                // Initialize Semantic Kernel for AI processing (for compatibility with existing code)
                var builder = Kernel.CreateBuilder();

                // Create HttpClient with extended timeout for large document processing
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(20); // 20 minutes timeout for large documents

                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: deploymentName,
                    endpoint: endpoint,
                    apiKey: apiKey,
                    httpClient: httpClient);

                _kernel = builder.Build();

                _logger.LogInformation("✅ Azure OpenAI clients initialized successfully with API Key authentication");
                _logger.LogInformation("✅ Semantic Kernel initialized successfully with extended timeout (20 minutes)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize DocumentsNoStructuredAgent");
                throw;
            }
        }

        /// <summary>
        /// Extrae datos de documentos no estructurados utilizando Azure Document Intelligence y AI
        /// </summary>
        /// <param name="twinID">Nombre del contenedor (TwinID)</param>
        /// <param name="filePath">Ruta del archivo dentro del contenedor</param>
        /// <param name="fileName">Nombre del archivo</param>
        /// <param name="estructura">Estructura del documento (e.g., "no-estructurado")</param>
        /// <param name="subcategoria">Subcategoría del documento (e.g., "general", "contratos", "manuales")</param>
        /// <returns>Resultado del procesamiento del documento no estructurado</returns>
        public async Task<UnstructuredDocumentResult> ExtractDocumentDataAsync(
            int PaginaIniciaIndice,
            int PaginaTerminaIndice,
            bool TieneIndex,
            bool Translation,
            string Language,
            string twinID,
            string filePath,
            string fileName,
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {
            _logger.LogInformation("📄 Starting unstructured document data extraction for: {FileName}", fileName);
            _logger.LogInformation("📂 Container: {Container}, Path: {Path}", twinID, filePath);
            _logger.LogInformation("🏗️ Document metadata: Estructura={Estructura}, Subcategoria={Subcategoria}", estructura, subcategoria);

            var result = new UnstructuredDocumentResult
            {
                Success = false,
                ContainerName = twinID,
                FilePath = filePath,
                FileName = fileName,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // STEP 1: Generate SAS URL for Document Intelligence access
                _logger.LogInformation("🔗 STEP 1: Generating SAS URL for document access...");

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinID);
                var fullFilePath = $"{filePath}/{fileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(2));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    result.ErrorMessage = "Failed to generate SAS URL for document access";
                    _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                    return result;
                }

                result.DocumentUrl = sasUrl;
                _logger.LogInformation("✅ SAS URL generated successfully");

                // STEP 2: Extract data using Document Intelligence
                _logger.LogInformation("🤖 STEP 2: Extracting data with Document Intelligence...");

                var documentAnalysis = await _documentIntelligenceService.AnalyzeDocumentWithPagesAsync(sasUrl);

                if (!documentAnalysis.Success)
                {
                    result.ErrorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}";
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return result;
                }

                result.RawTextContent = documentAnalysis.TextContent;
                result.TotalPages = documentAnalysis.TotalPages;
                result.DocumentPages = documentAnalysis.DocumentPages;
                result.Tables = documentAnalysis.Tables;

                _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars",
                    documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

                // STEP 3: Process with AI for intelligent data extraction
                _logger.LogInformation("🧠 STEP 3: Processing with AI for intelligent data extraction...");

                UnstructuredDocumentAIResult aiResult = new UnstructuredDocumentAIResult();
                
                if (Translation)
                {
                    // PASO 3A: Traducir el documento si se solicita
                    _logger.LogInformation("🌐 STEP 3A: Translating document to language: {Language}", Language);
                    var translatedDocument = await TranslateDocumentAnalysisAsync(documentAnalysis, Language);

                    if (translatedDocument.Success)
                    {
                        // Usar el documento traducido para el procesamiento
                        documentAnalysis = translatedDocument;
                        _logger.LogInformation("✅ Document translated successfully");
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Translation failed, continuing with original document");
                    }
                }
                TieneIndex = true;
                if (TieneIndex)
                {
                    // Si tiene índice, usar el método normal que busca índice en las primeras 5 páginas
                    aiResult = await ProcessWithAI(
                        sasUrl,
                        filePath,
                       fileName,
                       PaginaIniciaIndice,
                       PaginaTerminaIndice,
                       documentAnalysis,
                       twinID,
                       estructura,
                       subcategoria);
                }
                else
                {
                    // Si no tiene índice, crear uno automáticamente con todas las páginas
                    aiResult = await CreateIndexWithAI(documentAnalysis, twinID, estructura, subcategoria);
                }

                if (!aiResult.Success)
                {
                    result.ErrorMessage = $"AI processing failed: {aiResult.ErrorMessage}";
                    _logger.LogError("❌ AI processing failed: {Error}", aiResult.ErrorMessage);
                    return result;
                }

                // Populate result with AI-processed data
                result.Success = true;
                result.ExtractedContent = aiResult.ExtractedContent;
                result.StructuredData = aiResult.StructuredData;
                result.KeyInsights = aiResult.KeyInsights;
                result.ExecutiveSummary = aiResult.ExecutiveSummary;
                result.HtmlOutput = aiResult.HtmlOutput;
                result.RawAIResponse = aiResult.RawAIResponse;
                // **NUEVO: Propagar capítulos extraídos**
                //  result.ExtractedChapters = aiResult.ExtractedChapters;

                _logger.LogInformation("✅ Unstructured document processing completed successfully");
                _logger.LogInformation("📊 Results: {Pages} pages processed, {Insights} insights extracted, {Chapters} chapters processed",
                    result.TotalPages, result.KeyInsights.Count, result.ExtractedChapters.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting data from unstructured document {FileName}", fileName);

                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public async Task<string> AnswerSearchQuestion(string Idioma, string Question, string TwinID, string FileName)
        {
            try
            {
                if (FileName == "null")
                {
                    FileName = "Global";
                }
                _logger.LogInformation("🔍 Starting search question answering for Question: {Question}, TwinID: {TwinID}, FileName: {FileName}", 
                    Question, TwinID, FileName);

                // PASO 1: Buscar capítulos relevantes usando el DocumentsNoStructuredIndex
                _logger.LogInformation("📚 STEP 1: Searching relevant chapters using DocumentsNoStructuredIndex...");
                
                var indexLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DocumentsNoStructuredIndex>();
                var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

                var relevantChapters = await documentsIndex.AnswerSearchUserQuestionAsync(Question, TwinID, FileName);
                
                if (relevantChapters == null || relevantChapters.Count == 0)
                {
                    return @"<div style='padding: 20px; background-color: #f8f9fa; border-left: 4px solid #ffc107; font-family: Arial, sans-serif;'>
                        <h3 style='color: #856404; margin-top: 0;'>🤖 Hola, soy tu Agente Inteligente</h3>
                        <p style='color: #856404;'>No pude encontrar información relevante para tu pregunta en el archivo <strong>" + FileName + @"</strong>.</p>
                        <p style='color: #856404;'>Estoy especializado en responder preguntas sobre el contenido de este documento específico.</p>
                        <p style='color: #856404;'>Por favor, intenta reformular tu pregunta o asegúrate de que se relacione con el contenido del archivo.</p>
                    </div>";
                }

                _logger.LogInformation("✅ Found {ChapterCount} relevant chapters", relevantChapters.Count);

                // PASO 2: Concatenar el contenido de los capítulos encontrados
                _logger.LogInformation("📝 STEP 2: Concatenating chapter content...");
                
                var contentBuilder = new StringBuilder();
                var fileNames = new HashSet<string>();
                var chapterTitles = new List<string>();

                foreach (var chapter in relevantChapters)
                {
                    if (!string.IsNullOrEmpty(chapter.FileName))
                    {
                        fileNames.Add(chapter.FileName);
                    }

                    if (!string.IsNullOrEmpty(chapter.ChapterTitle))
                    {
                        chapterTitles.Add(chapter.ChapterTitle);
                    }

                    // Usar el texto del subcapítulo si está disponible, sino el del capítulo principal
                    var textToUse = !string.IsNullOrEmpty(chapter.TextSub) ? chapter.TextSub : chapter.TextChapter;
                    
                    if (!string.IsNullOrEmpty(textToUse))
                    {
                        contentBuilder.AppendLine($"\n=== CAPÍTULO: {chapter.ChapterTitle} ===");
                        contentBuilder.AppendLine(" Pagina  From Page - : " + chapter.FromPageSub +
                            " To Page: " + chapter.ToPageChapter);
                        if (!string.IsNullOrEmpty(chapter.TitleSub))
                        {
                            contentBuilder.AppendLine($"Subcapítulo: {chapter.TitleSub}");
                        }
                        contentBuilder.AppendLine(textToUse);
                        contentBuilder.AppendLine();
                    }
                }

                var concatenatedContent = contentBuilder.ToString();
                var primaryFileName = fileNames.FirstOrDefault() ?? FileName;

                _logger.LogInformation("📊 Content prepared: {ContentLength} characters from {ChapterCount} chapters", 
                    concatenatedContent.Length, relevantChapters.Count);

                // PASO 3: Generar respuesta usando Semantic Kernel y OpenAI
                _logger.LogInformation("🤖 STEP 3: Generating AI response using Semantic Kernel...");

                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var aiPrompt = $@"
Eres un Agente Inteligente especializado en responder preguntas sobre el contenido de documentos.

ANSWER ALWAYS IN THIS LANGUAGE {Idioma}
CONTESTA SIEMPRE EN ESTE IDIOMA {Idioma}
PREGUNTA DEL USUARIO:
{Question}

INFORMACIÓN SOBRE EL ARCHIVO:
Archivo analizado: {primaryFileName}
Capítulos encontrados: {string.Join(", ", chapterTitles)}
TwinID: {TwinID}

CONTENIDO RELEVANTE ENCONTRADO:
{concatenatedContent}

INSTRUCCIONES PARA TU RESPUESTA:

1) RESPONDE LA PREGUNTA usando ÚNICAMENTE la información del contenido proporcionado arriba
2) Busca la respuesta específica en el texto que viene del archivo {primaryFileName}
3) En caso de que no se encuentre la respuesta específica en el contenido, indícalo claramente
4) Analiza la pregunta y responde SOLO sobre los temas relacionados con los capítulos: {string.Join(", ", chapterTitles)}
5) Si la pregunta es genérica tipo '¿cómo estás?', contesta amigablemente y explica que eres un Agente Inteligente especializado en responder preguntas sobre el archivo {primaryFileName}

FORMATO DE RESPUESTA EN HTML:
- Usa HTML con colores profesionales y atractivos
- Incluye títulos con estilos (<h2>, <h3> con colores)
- Usa grids, tablas o listas cuando sea apropiado
- Aplica diferentes fondos y colores para distinguir secciones
- Usa negritas, cursivas y otros formatos para destacar información importante
- Incluye emojis relevantes para hacer la respuesta más amigable
- Asegúrate de que sea fácil de leer y visualmente atractivo
- NO incluyas ```html al inicio o final de tu respuesta

EJEMPLO DE ESTRUCTURA:
<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px;'>
    <h2 style='color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px;'>🤖 Respuesta del Agente Inteligente</h2>
    
    <div style='background-color: #e8f4fd; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <h3 style='color: #2980b9; margin-top: 0;'>📋 Información encontrada en: {primaryFileName}</h3>
        [Tu respuesta aquí]
    </div>
    
    <div style='background-color: #f8f9fa; padding: 10px; border-left: 4px solid #28a745; margin-top: 20px;'>
        <strong style='color: #155724;'>📚 Fuente:</strong> Capítulos analizados del documento
    </div>
</div>

IMPORTANTE:
- Sé específico y preciso
- Cita los capítulos cuando sea relevante y subcapitulos con titulos
- No crees informacion que no esta en el documento 
- NO inventes información que no esté en el contenido
- Mantén un tono profesional pero amigable
- Si no puedes responder con la información disponible, sé honesto al respecto
- Asegurate de incluir las paginas donde estan las respuestas y cada capitulo en detalle.
- usa colores, listas, grid bullets muy profesional
- Pon titulos etc. 

CONTESTA EN ESTE IDIOMA  : {Idioma}:";

                history.AddUserMessage(aiPrompt);

                var executionSettings = new PromptExecutionSettings();
                if(DeploymentName == "gpt-5-mini")
                {
                    executionSettings = new PromptExecutionSettings
                    {
                        ExtensionData = new Dictionary<string, object>
                        {
                            ["'max_completion_tokens"] = 45000,
                            ["'reasoning_effort'"] = "medium"

                        }
                    };
                }
                else
                {
                    executionSettings = new PromptExecutionSettings
                    {
                        ExtensionData = new Dictionary<string, object>
                        {
                            ["'max_completion"] = 15000,
                            

                        }
                    };

                }


                    var response = await chatCompletion.GetChatMessageContentAsync(history, executionSettings, _kernel);
                var aiResponse = response.Content ?? "";

                if (string.IsNullOrEmpty(aiResponse))
                {
                    return @"<div style='padding: 20px; background-color: #fff3cd; border-left: 4px solid #ffc107; font-family: Arial, sans-serif;'>
                        <h3 style='color: #856404; margin-top: 0;'>⚠️ Sin respuesta</h3>
                        <p style='color: #856404;'>Lo siento, no pude generar una respuesta adecuada para tu pregunta.</p>
                        <p style='color: #856404;'>Por favor, intenta reformular la pregunta de manera más específica.</p>
                    </div>";
                }

                _logger.LogInformation("✅ AI response generated successfully, length: {ResponseLength} characters", aiResponse.Length);

                return aiResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error answering search question: {Question}", Question);

                return $@"<div style='padding: 20px; background-color: #f8d7da; border-left: 4px solid #dc3545; font-family: Arial, sans-serif;'>
                    <h3 style='color: #721c24; margin-top: 0;'>❌ Error del Sistema</h3>
                    <p style='color: #721c24;'>Ocurrió un error al procesar tu pregunta.</p>
                    <p style='color: #721c24;'>Como tu Agente Inteligente especializado en el archivo <strong>{FileName}</strong>, te recomiendo intentar nuevamente con una pregunta más específica.</p>
                    <p style='color: #6c757d; font-size: 0.9em;'>Error técnico: {ex.Message}</p>
                </div>";
            }
        }

        /// <summary>
        /// Procesa el documento con AI para extraer información estructurada
        /// </summary>
        private async Task<UnstructuredDocumentAIResult> ProcessWithAI(
           
            string SASURL,
            string PathName,
            string fileName,
            int PaginaIniciaIndex,
            int PaginaTerminaIndex,
            DocumentAnalysisResult documentAnalysis,
            string twinID = "",
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {

            var indexLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DocumentsNoStructuredIndex>();
            var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

            try
            {
                // Construir contenido SOLO de las primeras 5 páginas para extracción de índice
                var pagesContent = new StringBuilder();
                var pagesToAnalyze = Math.Min(100, documentAnalysis.DocumentPages.Count);

                _logger.LogInformation("📚 Analyzing first {Pages} pages for index extraction", pagesToAnalyze);

                for (int i = 0; i < pagesToAnalyze; i++)
                {
                    var page = documentAnalysis.DocumentPages[i];
                    pagesContent.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                    pagesContent.AppendLine(string.Join("\n", page.LinesText));
                }

                // Incluir tablas solo de las primeras 5 páginas si existen
                var tablesContent = documentAnalysis.Tables.Count > 0
                    ? DocumentIntelligenceService.GetSimpleTablesAsText(documentAnalysis.Tables)
                    : "No se encontraron tablas estructuradas.";

                string AllData = pagesContent.ToString();
                var prompt = $@"
Analiza este documento no estructurado y EXTRAE ESPECÍFICAMENTE EL ÍNDICE del documento.

CONTENIDO DEL DOCUMENTO POR PÁGINAS:
{AllData}

TABLAS ENCONTRADAS:
{tablesContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}

INSTRUCCIONES ESPECÍFICAS PARA EXTRACCIÓN DE ÍNDICE:
=================================================== 

Look at the data and create an index with two levels:
1) Find Chapters based on hierarchy.
2) Find Subchapters within each chapter.  
3) Copy the names of the chapters and subchapters exactly as they appear in the text word by word.
Answer me in poor json no ocmplexx just json no 
4) Keep subchapters inside their respective chapters.
5) IMPORTANT: If you find a chapter without subchapters, include it with an empty subchapters array.
6) IMPORTANT: Try to create subchapters only if they are clearly defined in the text. 
do not invent new subchapters only those needed. DO nmot make a subchapter for every sentence.
Extract Pages Fro To for each Chapter ti is important to have the pages range for each chapter.
For istance this is incorrect;
""subchapters"": [
        ""Model:"",
        ""OLS objective: minimize RSS = sum_i (y_i - beta0 - beta1 x_i)^2."",
        ""Closed-form solution (derivation sketch):"",
        ""Interpretation:"",
        ""Assessing fit:"",
        ""Simple example (numeric):"",
        ""Inference (brief):"",
        ""Practice:"",
        ""Exercises:""

IMPORTANT CHapters do not need to have subchapters if they do not have one 
do not make every compect. sentence etc. a subchapter.
do you best effort to identify subchapters if they do not exist
do not invent them.
only if it looks like a subchapter.
EJEMPLO:
{{
  ""Index"": [
    {{
      ""chapter"": ""I. VISIÓN GENERAL"",
        ""pageFrom"": 1,
        ""pageTo"": 5,
      ""subchapters"": [
        ""1. INTRODUCCIÓN"",
        ""2. ALCANCE""
      ]
    }},
    {{
      ""chapter"": ""II. CONTRATACIÓN"",
        ""pageFrom"": 6,
        ""pageTo"": 10,
      ""subchapters"": [
        ""1. PROCESO DE SELECCIÓN"",
        ""2. REQUISITOS""
      ]
    }}
  ]
}}
Use the sample and do it perfect no errors
```json"
;

                // Create a list of chat messages using OpenAI types
                var messages = new List<OpenAI.Chat.ChatMessage>
    {
#pragma warning disable OPENAI001 // DeveloperChatMessage is for evaluation purposes only
        new DeveloperChatMessage(@"You are an AI assistant that helps people find information."),
#pragma warning restore OPENAI001
        new UserChatMessage(prompt)
    };

                // Configure ChatCompletionOptions with reasoning effort
                var options = new ChatCompletionOptions();

                // Set max output tokens using the workaround
                options
                  .GetType()
                  .GetProperty(
                      "SerializedAdditionalRawData",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                  .SetValue(options, new System.Collections.Generic.Dictionary<string, BinaryData>());
                options.MaxOutputTokenCount = 120000;
                options.SetNewMaxCompletionTokensPropertyEnabled();

                // Set reasoning effort using reflection (for o1 models)
                try
                {
                    var reasoningEffortProperty = options.GetType().GetProperty("ReasoningEffort");
                    if (reasoningEffortProperty != null)
                    {
                        reasoningEffortProperty.SetValue(options, "low");
                        _logger.LogInformation("✅ Reasoning effort set to 'medium'");
                    }
                    else
                    {
                        // Alternative: Set it in the SerializedAdditionalRawData
                        var additionalData = (Dictionary<string, BinaryData>)options.GetType()
                            .GetProperty("SerializedAdditionalRawData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                            .GetValue(options)!;
                        additionalData["reasoning_effort"] = BinaryData.FromString("\"medium\"");
                        _logger.LogInformation("✅ Reasoning effort set to 'medium' via SerializedAdditionalRawData");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Could not set reasoning effort, continuing without it");
                }

                // Create the chat completion request using the new ChatClient
                ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options);

                var aiResponse = completion.Content[0].Text ?? "{}";

                // Clean response of any markdown formatting
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                IndexWrapper wrapper;
                _logger.LogInformation("📝 AI Response Length: {Length} characters", aiResponse.Length);
                try
                {
                    wrapper = JsonSerializer.Deserialize<IndexWrapper>(aiResponse, opts);
                    var chapters = new List<ChapterIndex>();
                    var extractor = new DocumentExtractor();

                    DocumentSectionExtractor Extract = new DocumentSectionExtractor();
                    var IndexString = JsonSerializer.Serialize(wrapper.Index);
                    DocumentExtractChapters documentExtractChapters = new DocumentExtractChapters();

                    var CahptersList = documentExtractChapters.ExtractChapters(  wrapper.Index,
                        twinID  , documentAnalysis.DocumentPages);
                    string FilePath = PathName + "/" + fileName; 
                  //  var sections = Extract.ExtractSectionsFromDocument(AllData, wrapper.Index);


                 //   var SectionsStrig = JsonSerializer.Serialize(sections);
                    PDfDocumentNoStructured pdfDoc = new PDfDocumentNoStructured();
                    pdfDoc.ChapterList = new List<ExractedChapterIndex>();

                    // Initialize token counter and AiTokens service
                    AiTokrens tokenService = new AiTokrens();
                    int totalDocumentTokens = 0;
                    
                    // Process CahptersList instead of sections
                    foreach(var Chapter in CahptersList)
                    {
                        // Check if this chapter already exists in the list
                        var existingChapter = pdfDoc.ChapterList.FirstOrDefault(c => c.ChapterTitle == Chapter.ChapterTitle);

                        if (existingChapter != null)
                        {
                            // Chapter exists, update existing chapter data
                            
                            // Add all subchapters from the extracted chapter
                            foreach (var subChapter in Chapter.SubChapters)
                            {
                                var newSubChapter = new TwinFx.Agents.SubChapter
                                {
                                    Chapter = Chapter.ChapterTitle,
                                    Ttitle = subChapter.TitleSub,
                                    Text = subChapter.SubChapterText,
                                    TotalTokens = subChapter.TotalTokensSub,
                                    FromPage = subChapter.FromPageSub,
                                    ToPage = subChapter.ToPageSub
                                };
                                existingChapter.Subchapters.Add(newSubChapter);
                            }

                            // Update chapter-level text and tokens
                            if (!string.IsNullOrEmpty(Chapter.TextChapter))
                            {
                                if (string.IsNullOrEmpty(existingChapter.Text))
                                {
                                    existingChapter.Text = Chapter.TextChapter;
                                }
                                else
                                {
                                    existingChapter.Text += "\n\n" + Chapter.TextChapter;
                                }
                            }

                            // Update page ranges
                            if (existingChapter.FromPage == 0 || Chapter.FromPageChapter < existingChapter.FromPage)
                            {
                                existingChapter.FromPage = Chapter.FromPageChapter;
                            }
                            if (Chapter.ToPageChapter > existingChapter.ToPage)
                            {
                                existingChapter.ToPage = Chapter.ToPageChapter;
                            }

                            // Update total tokens
                            existingChapter.TotalTokens += Chapter.TotalTokensChapter;
                            
                            Console.WriteLine($"📖 Updated existing chapter '{Chapter.ChapterTitle}' with {Chapter.SubChapters.Count} subchapters - Total Tokens: {existingChapter.TotalTokens}");
                        }
                        else
                        {
                            // Create new chapter from ExractedChapterData
                            var newChapter = new ExractedChapterIndex
                            {
                                ChapterTitle = Chapter.ChapterTitle,
                                ChapterID = Chapter.ChapterID,
                                Text = Chapter.TextChapter,
                                FromPage = Chapter.FromPageChapter,
                                ToPage = Chapter.ToPageChapter,
                                TotalTokens = Chapter.TotalTokensChapter,
                                Subchapters = new List<TwinFx.Agents.SubChapter>()
                            };

                            // Convert all subchapters
                            foreach (var subChapter in Chapter.SubChapters)
                            {
                                var newSubChapter = new TwinFx.Agents.SubChapter
                                {
                                    Chapter = Chapter.ChapterTitle,
                                    Ttitle = subChapter.TitleSub,
                                    Text = subChapter.SubChapterText,
                                    TotalTokens = subChapter.TotalTokensSub,
                                    FromPage = subChapter.FromPageSub,
                                    ToPage = subChapter.ToPageSub
                                };
                                newChapter.Subchapters.Add(newSubChapter);
                            }

                            // Add to chapter list
                            pdfDoc.ChapterList.Add(newChapter);

                            Console.WriteLine($"📚 Created new chapter '{Chapter.ChapterTitle}' with {Chapter.SubChapters.Count} subchapters - Pages: {newChapter.FromPage}-{newChapter.ToPage}, Tokens: {newChapter.TotalTokens}");
                        }

                        // Add to total document tokens
                        totalDocumentTokens += Chapter.TotalTokensChapter;
                    }

                    // Set total tokens for the document
                    pdfDoc.TotalTokens = totalDocumentTokens;

                    string JsonPDF = JsonSerializer.Serialize(pdfDoc);
                    Console.WriteLine($"✅ Processing complete:");
                    Console.WriteLine($"   📋 Total Chapters: {pdfDoc.ChapterList.Count}");
                    Console.WriteLine($"   📄 Total Subchapters: {pdfDoc.ChapterList.Sum(c => c.Subchapters.Count)}");
                    Console.WriteLine($"   🔢 Total Document Tokens: {pdfDoc.TotalTokens}");
                     await documentsIndex.UploadDocumentTOIndex(pdfDoc, fileName,  
                         PathName,
                         twinID,
                         subcategoria);
                    // Log chapter summary
                    foreach (var chapter in pdfDoc.ChapterList)
                    {
                        Console.WriteLine($"   📚 Chapter: '{chapter.ChapterTitle}' - Subchapters: {chapter.Subchapters.Count} - Tokens: {chapter.TotalTokens}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("JSON parse error: " + ex.Message);

                }


                // Parse the AI response - Use the full response as it should contain the index structure
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (aiData == null)
                {
                    throw new InvalidOperationException("Failed to deserialize AI response");
                }
               
                var hasIndex = aiData.GetValueOrDefault("tieneIndice")?.ToString()?.ToLower() == "true";
                var executiveSummary = $"Análisis de índice completado. {(hasIndex ? "Índice encontrado" : "No se encontró índice")}";

                // **NUEVO: Si se encontró índice, extraer contenido de capítulos automáticamente**
                var extractedContent = ExtractContentData(aiData);
                var capitulosExtraidos = new List<CapituloDocumento>();

                if (hasIndex && extractedContent.Indice.Count > 0)
                {
                    _logger.LogInformation("🚀 STEP 4: Index found! Automatically extracting chapter content with AI...");

                    try
                    {
                        // Ejecutar ExtractChapterContentWithAI automáticamente pasando containerName, estructura y subcategoria
                        capitulosExtraidos = await ExtractChapterContentWithAI(
                            PaginaIniciaIndex, PaginaTerminaIndex,
                            documentAnalysis, extractedContent.Indice, twinID, estructura, subcategoria);

                        _logger.LogInformation("✅ Chapter content extraction completed: {ProcessedChapters}/{TotalChapters} chapters processed successfully",
                            capitulosExtraidos.Count, extractedContent.Indice.Count);

                        // **NUEVO: STEP 5 - Indexar capítulos extraídos en Azure Search**
                        if (capitulosExtraidos.Count > 0)
                        {
                            _logger.LogInformation("📄 STEP 5: Indexing extracted chapters in no-structured-index...");

                            try
                            {
                                // Crear instancia del DocumentsNoStructuredIndex

                                // Indexar todos los capítulos extraídos
                                var indexResults =
                                 await documentsIndex.IndexMultipleCapitulosAsync(capitulosExtraidos, subcategoria, twinID);

                                var successCount = indexResults.Count(r => r.Success);
                                var failureCount = indexResults.Count(r => !r.Success);
                                _logger.LogInformation("✅ Indexing completed: {SuccessCount}/{TotalCount} chapters indexed successfully",
                                    successCount, capitulosExtraidos.Count);
                            }
                            catch (Exception indexEx)
                            {
                                _logger.LogWarning(indexEx, "⚠️ Failed to index chapters in no-structured-index, continuing with main flow");
                                // No fallar todo el proceso si la indexación falla
                            }
                        }
                    }
                    catch (Exception chapterEx)
                    {
                        _logger.LogWarning(chapterEx, "⚠️ Failed to extract chapter content automatically, continuing with index only");
                        // No fallar todo el proceso si la extracción de capítulos falla
                    }
                }

                return new UnstructuredDocumentAIResult
                {
                    Success = true,
                    ExtractedContent = extractedContent,
                    StructuredData = new StructuredDocumentData(), // Empty for index extraction
                    KeyInsights = new List<DocumentInsightData>(), // Empty for index extraction
                    ExecutiveSummary = executiveSummary,
                    HtmlOutput = GenerateIndexHtmlOutput(aiData),
                    RawAIResponse = aiResponse,
                    // **NUEVO: Agregar capítulos extraídos al resultado**
                    ExtractedChapters = capitulosExtraidos
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI document processing");
                return new UnstructuredDocumentAIResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        /// <summary>
        /// Crea un índice automáticamente usando AI cuando el documento no tiene uno
        /// </summary>
        /// <param name="documentAnalysis">Resultado del análisis del documento con todas las páginas</param>
        /// <param name="containerName">Nombre del contenedor (TwinID)</param>
        /// <param name="estructura">Estructura del documento</param>
        /// <param name="subcategoria">Subcategoría del documento</param>
        /// <returns>Resultado del procesamiento con índice generado automáticamente</returns>
        private async Task<UnstructuredDocumentAIResult> CreateIndexWithAI(
            DocumentAnalysisResult documentAnalysis,
            string containerName = "",
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                // Construir contenido de TODAS las páginas para generar índice automático
                var allPagesContent = new StringBuilder();

                _logger.LogInformation("🔍 Creating automatic index from all {Pages} pages", documentAnalysis.DocumentPages.Count);

                foreach (var page in documentAnalysis.DocumentPages)
                {
                    allPagesContent.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                    allPagesContent.AppendLine(string.Join("\n", page.LinesText));
                }

                // Incluir tablas si existen
                var tablesContent = documentAnalysis.Tables.Count > 0
                    ? DocumentIntelligenceService.GetSimpleTablesAsText(documentAnalysis.Tables)
                    : "No se encontraron tablas estructuradas.";

                string PagesData = allPagesContent.ToString();
                var prompt2 = $@"Analiza el documento completo y genera un índice automático sencillo por capítulos/secciones (no por párrafo ni por cada tema pequeño).  
Detecta el idioma del documento y genera el índice en ese mismo idioma.  
 

NOTAS DEL HTML:
*************** IMPORTANTE *********
1) Los titulos haslos mas grandes y en bold
2) Usa listas con bullets o numeradas para los items
3) Usa grids si es necesario
4) Usa colores amigables
5) USa background diferente para distinguir las orciones
6) adicioan espacios entre oraciones 
7) Formatea el texto para que sea facil de leer como un libro o un documento word.
8) USa tablas si es nesesairio
9) Asegurete de extraer las paginas de a es muy importante contar con en que paginas esta el contenido del
capitulo para poder encontrarlo. El texto te indica en que pagina esta todo. No lo omitas.
TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}  

SUPER IMPORTATE estas creando un arhcivo JSON no debes usar caracteres que lo rompan hazlo bien:
Eliminar los caracteres de escape: Debes quitar los \ que preceden a los elementos HTML en los campos textoHTML. Por ejemplo, cambia \<h2> a <h2>.

Validar el formato del JSON: Asegúrate de que todas las cadenas de texto estén correctamente delimitadas y de que no haya comillas o caracteres innecesarios que rompan la estructura del JSON.

Verificar el uso de caracteres especiales: Asegúrate de que los caracteres especiales como & en &amp; estén correctamente escapados en el contexto de HTML, pero no en el contexto del JSON en sí.

*************** IMPORTANTE *********

Muy importante:
Evita este error: '+' is invalid after a value. Expected either ',', '}}', or ']'. Path: $.indice | LineNumber: 14 | BytePositionInLine: 16.
texto y textoHTML tienes que copiar todo el texto del capitulo que has creado. Este texto se usara para que el lector lea el capitulo. El html es par ahaacerlo amigable pero no me escibas un sumario o recortes el texto.
Al final este documento todos los indices deben de tener 
100% todo el texto que te estoy dando em allPagesContent ==> CONTENIDO COMPLETO DEL DOCUMENTO:
 ""texto"": ""Esta sección introduce la regresión lineal."",  
 ""textoHTML"": ""<h2>Introducción</h2><p>Esta sección introduce la regresión lineal.</p>"", 
Datos disponibles:  
CONTENIDO COMPLETO DEL DOCUMENTO: {PagesData}  
TABLAS ENCONTRADAS: {tablesContent}  
TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}  
  
Reglas principales:  
- Cubre todas las páginas: no dejar ninguna sin asignar.  
- Crea capítulos o secciones lógicas; máximo 2 páginas por capítulo (1–2). Si una página tiene mucho contenido, puede ser un capítulo propio.  
- No crear un capítulo por párrafo. Agrupar por secciones naturales o cambios de tema.  
- Usa títulos cortos y descriptivos basados en el contenido; no inventar texto.  
- Los números de página deben ser secuenciales y completos.  
- Actualiza totalCapitulos con el número real de capítulos creados.  
- Puede usar subniveles solo si es necesario.  
- La salida requerida debe incluir todo el texto que pertenece a cada capítulo, que usaremos para resumirlo y analizarlo con AI.  
  
Respuesta únicamente en JSON válido (sin markdown) y en el idioma original del documento.  
  
INSTRUCCIÓN IMPORTANTE:  
Detecta en que pagina estuvo el índice y ponlo en paginaDelIndice. PagiaDe - paginaA
Después de copiar el texto de cada capítulo, crea una versión HTML que se vea colorida, con títulos, subtítulos, grids, listas y bullets en el idioma original del documento.  
 Tienes que crear un JSON limpio evoita errores como este:
'0x0A' is an invalid escapable character within a JSON string. The string should be correctly escaped.  
Estructura JSON esperada (ejemplo):  
{{  
    ""tieneIndice"": true,  
    ""indiceGeneradoAutomaticamente"": true,  
    ""paginasAnalizadas"": {documentAnalysis.TotalPages},  
    ""indiceEncontrado"": {{  
        ""paginaDelIndice"": 1,  
        ""tipoIndice"": ""Índice Generado Automáticamente"",  
        ""totalCapitulos"": 5  
    }},  
    ""indice"": [  
        {{  
            ""titulo"": ""Título capítulo 1"",  
            ""texto"": ""Este capítulo trata de... [todo el texto del capítulo aquí]"",  
            ""textoHTML"": ""<h2>Título capítulo 1</h2><p>[texto en HTML del capítulo aquí]</p>"",  
            ""paginaDe"": 1,  
            ""paginaA"": 2,  
            ""nivel"": 1,  
            ""numeroCapitulo"": ""1""  
        }}  
        // más capítulos...  
    ],  
    ""observaciones"": ""Índice creado automáticamente; capítulos de máximo 2 páginas."",  
    ""estructuraDetectada"": ""Estructura generada automáticamente"",  
    ""metadatos"": {{  
        ""documentoTieneCapitulos"": true,  
        ""formatoNumerico"": ""1, 2, 3..."",  
        ""tieneSubsecciones"": false,  
        ""paginasTotalesDelDocumento"": {documentAnalysis.TotalPages}  
    }}  
}}  
 IMPORTANTE usa exactamente esta estructura JSON, sin cambiar nombres de campos.
 
Reglas de formato JSON:  
- Usa comillas dobles para claves y valores de tipo cadena.  
- Escapa comillas dobles dentro de valores usando una barra invertida (\).  
- Usa comas para separar elementos en objetos y arrays.  
- Cierra todos los objetos y arrays correctamente.  
  Remove concatenation operators: The + signs should not be present in the JSON. The JSON should simply have complete strings without any operators.
- Usa solamente HTML que json pueda leer
Correct the field names: Ensure that the JSON field names match the expected properties que te di.  
";



                history.AddUserMessage(prompt2);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";

                // Clean response of any markdown formatting
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                _logger.LogInformation("📝 AI Response Length for auto-generated index: {Length} characters", aiResponse.Length);

                // Parse the AI response
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                var DocumentIndex = JsonSerializer.Deserialize<DocumentoIndice>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (aiData == null)
                {
                    throw new InvalidOperationException("Failed to deserialize AI response for auto-generated index");
                }

                var executiveSummary = "Índice creado automáticamente. El documento se dividió en capítulos de máximo 2 páginas.";

                // Extract content data with auto-generated index

                var capitulosExtraidos = new List<CapituloDocumento>();

                if (DocumentIndex.Indice.Count > 0)
                {
                    _logger.LogInformation("🚀 STEP 4: Auto-generated index created! Extracting chapter content with AI...");

                    try
                    {
                        // Ejecutar ExtractChapterContentWithAI con el índice generado automáticamente
                        //   capitulosExtraidos = await ExtractChapterContentWithAI(documentAnalysis, extractedContent.Indice, containerName, estructura, subcategoria);
                        NoStructuredServices DocServices = new NoStructuredServices();

                        //    capitulosExtraidos = DocServices.ExtaeCapitulos(DocumentIndex, containerName);
                        // STEP 5 - Indexar capítulos extraídos en Azure Search
                        if (capitulosExtraidos.Count > 0)
                        {
                            _logger.LogInformation("📄 STEP 5: Indexing auto-generated chapters in no-structured-index...");

                            try
                            {
                                // Crear instancia del DocumentsNoStructuredIndex
                                var indexLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DocumentsNoStructuredIndex>();
                                var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

                                // Indexar todos los capítulos extraídos
                                //     var indexResults = await documentsIndex.IndexMultipleCapitulosAsync(capitulosExtraidos);

                                //     var successCount = indexResults.Count(r => r.Success);
                                //    var failureCount = indexResults.Count(r => !r.Success);


                            }
                            catch (Exception indexEx)
                            {
                                _logger.LogWarning(indexEx, "⚠️ Failed to index auto-generated chapters in no-structured-index, continuing with main flow");
                                // No fallar todo el proceso si la indexación falla
                            }
                        }
                    }
                    catch (Exception chapterEx)
                    {
                        _logger.LogWarning(chapterEx, "⚠️ Failed to extract auto-generated chapter content, continuing with index only");
                        // No fallar todo el proceso si la extracción de capítulos falla
                    }
                }

                return new UnstructuredDocumentAIResult
                {
                    Success = true,
                    StructuredData = new StructuredDocumentData(), // Empty for index extraction
                    KeyInsights = new List<DocumentInsightData>(), // Empty for index extraction
                    ExecutiveSummary = executiveSummary,
                    HtmlOutput = GenerateIndexHtmlOutput(aiData),
                    RawAIResponse = aiResponse,
                    ExtractedChapters = capitulosExtraidos
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating automatic index with AI");
                return new UnstructuredDocumentAIResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Extrae el documento original sin procesar (texto completo) de las páginas del documento
        /// </summary>
        private string ExtractOriginalDocumentText(List<DocumentPage> documentPages)
        {
            var originalText = new StringBuilder();

            foreach (var page in documentPages)
            {
                originalText.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                if (page.LinesText != null && page.LinesText.Count > 0)
                {
                    foreach (var line in page.LinesText)
                    {
                        originalText.AppendLine(line);
                    }
                }
            }

            return originalText.ToString();
        }

        /// <summary>
        /// Procesa el documento completo sin estructurar con AI para tareas como resumen, generación de índice, etc.
        /// </summary>
        public async Task<UnstructuredDocumentAIResult> ProcessFullDocumentWithAI(
            string containerName,
            string filePath,
            string fileName,
            string estructura = "no-estructurado",
            string subcategoria = "general",
            string language = "es")
        {
            var result = new UnstructuredDocumentAIResult
            {
                Success = false,
                ExtractedContent = new ExtractedContentData(),
                StructuredData = new StructuredDocumentData(),
                KeyInsights = new List<DocumentInsightData>(),
                ExecutiveSummary = "",
                HtmlOutput = "",
                RawAIResponse = ""
            };

            try
            {
                // Obtener el documento original sin procesar
                _logger.LogInformation("📥 Retrieving original document text for processing...");

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                var fullFilePath = $"{filePath}/{fileName}";
                var originalDocumentText = await dataLakeClient.DownloadTextFileAsync(fullFilePath);

                if (string.IsNullOrEmpty(originalDocumentText))
                {
                    result.ErrorMessage = "Failed to download the original document text";
                    _logger.LogError("❌ Failed to download the document: {FullFilePath}", fullFilePath);
                    return result;
                }

                // Procesar el documento original con AI
                _logger.LogInformation("🤖 Processing full document with AI for tasks like summarization, index generation, etc. Text length: {Length}", originalDocumentText.Length);

                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                // Prompt para análisis completo del documento
                var prompt = $@"
Eres un experto analizando documentos y extrayendo información clave.

INFORMACIÓN DEL DOCUMENTO:
====================================================
Título: {fileName}
Contenedor: {containerName}
Ruta: {filePath}
Estructura: {estructura}
Subcategoría: {subcategoria}
Idioma: {language}

CONTENIDO DEL DOCUMENTO:
{originalDocumentText}

INSTRUCCIONES:
====================================================
1. ANALIZA el documento completo proporcionado.
2. EXTRAER la siguiente información:
   - Un resumen ejecutivo completo.
   - Un índice estructurado con capítulos y secciones.
   - Datos clave como nombres, fechas, cifras, etc.
   - 15 preguntas frecuentes con respuestas sobre el contenido.
2. EXTRAER la siguiente información en secciones:
   - Resumen Ejecutivo
   - Índice Estructurado
   - Datos Clave (nombres, fechas, cifras) 
3. GENERAR el contenido en formato JSON estructurado.
4. no incluyas frases innecesarias, solo responde con el JSON solicitado.
4. Incluir una tabla con metadatos clave del documento.
5. El índice debe contener capítulos y subcapítulos si es posible.

FORMATO DE RESPUESTA JSON (sin ```json ni ```):
{{
    ""titulo"": ""Análisis Completo del Documento"",
    ""resumenEjecutivo"": ""Resumen ejecutivo completo aquí..."",
    ""indice"": [ 
        {{
            ""titulo"": ""Introducción"",
            ""paginaDe"": 1,
            ""paginaA"": 5,
            ""nivel"": 1,
            ""numeroCapitulo"": ""1""
        }}
    ],
    ""datosClave"": {{
        ""nombres"": [""Nombre Ejemplo""],
        ""fechas"": [""2023-01-01""],
        ""cantidades"": [1000],
        ""monedas"": [""USD"", ""EUR""]
    }},
    ""preguntasFrecuentes"": [
        {{
            ""pregunta"": ""¿Cuál es el objetivo del documento?"",
            ""respuesta"": ""El objetivo es..."",
            ""categoria"": ""General"",
            ""dificultad"": ""Básico""
        }}
    ],
    ""htmlReporte"": ""<h1>Reporte de Análisis</h1><p>Resumen aquí...</p>"",
    ""metadatos"": {{
        ""paginaDelIndice"": 2,
        ""tipoIndice"": ""Tabla de Contenidos"",
        ""totalCapitulos"": 10,
        ""documentoTieneCapitulos"": true,
        ""formatoNumerico"": ""1, 2, 3..."",
        ""tieneSubsecciones"": true,
        ""paginasTotalesDelDocumento"": 0
    }}
}}

REGLAS:
====================
- El índice debe ser estructurado jerárquicamente.
- Los capítulos no deben exceder de 2 páginas cada uno.
- No inventes información, usa solo el contenido del documento.
- Las preguntas y respuestas deben ser específicas y relevantes.

RESPONDE ÚNICAMENTE EN JSON VÁLIDO SIN MARKDOWN:";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";

                // Limpiar respuesta
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                // Deserializar usando System.Text.Json
                var fullDocumentResult = JsonSerializer.Deserialize<UnstructuredDocumentAIResult>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (fullDocumentResult != null)
                {
                    result.Success = true;
                    result.ExtractedContent = fullDocumentResult.ExtractedContent;
                    result.StructuredData = fullDocumentResult.StructuredData;
                    result.KeyInsights = fullDocumentResult.KeyInsights;
                    result.ExecutiveSummary = fullDocumentResult.ExecutiveSummary;
                    result.HtmlOutput = fullDocumentResult.HtmlOutput;
                    result.RawAIResponse = aiResponse;

                    _logger.LogInformation("✅ Full document processed successfully: {Title}", fullDocumentResult.ExecutiveSummary);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to deserialize AI response for full document processing");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing full document with AI");
            }

            return result;
        }


        /// <summary>
        /// Extrae el contenido completo de cada capítulo basado en el índice y procesa con OpenAI
        /// </summary>
        /// <param name="documentAnalysis">Resultado del análisis del documento con todas las páginas</param>
        /// <param name="extractedIndex">Índice extraído con capítulos y páginas</param>
        /// <param name="containerName">Nombre del contenedor (TwinID)</param>
        /// <param name="estructura">Estructura del documento</param>
        /// <param name="subcategoria">Subcategoría del documento</param>
        /// <returns>Lista de capítulos procesados con contenido, resumen, tokens y preguntas</returns>
        public async Task<List<CapituloDocumento>> ExtractChapterContentWithAI(
            int PaginaIniciaIndex,
            int PaginaTerminaIndice,
            DocumentAnalysisResult documentAnalysis,
            List<CapituloIndice> extractedIndex,
            string containerName = "",
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {
            _logger.LogInformation("📚 Starting chapter content extraction for {ChapterCount} chapters", extractedIndex.Count);
            _logger.LogInformation("🏗️ Document metadata: Estructura={Estructura}, Subcategoria={Subcategoria}", estructura, subcategoria);
            StringBuilder allPagesContent = new StringBuilder();
            var resultChapters = new List<CapituloDocumento>();
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            foreach (var page in documentAnalysis.DocumentPages)
            {
                allPagesContent.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                allPagesContent.AppendLine(string.Join("\n", page.LinesText));
            }

            // Para todos los capítulos del índice
            for (int i = 0; i < extractedIndex.Count; i++)
            {
                var currentChapter = extractedIndex[i];
                var nextChapter = (i + 1 < extractedIndex.Count) ? extractedIndex[i + 1] : null;
                NoStructuredServices DocServices = new NoStructuredServices();
                CapituloDocumento chapter = await DocServices.ExtractCapituloDataWithAI(
                    _kernel,
                    currentChapter,
                    nextChapter,
                   PaginaIniciaIndex,
                   PaginaTerminaIndice,
                    documentAnalysis.DocumentPages);


                resultChapters.Add(chapter);
            }
            foreach (var indexChapter in extractedIndex)
            {
                try
                {
                    _logger.LogInformation("📖 Processing chapter: {ChapterTitle} (Pages {PageFrom}-{PageTo})",
                        indexChapter.Titulo, indexChapter.PaginaDe, indexChapter.PaginaA);

                    // PASO 1: Extraer todo el texto del capítulo basado en las páginas del índice
                    // var chapterText = ExtractChapterTextFromPages(documentAnalysis.DocumentPages, 
                    //     indexChapter.PaginaDe, indexChapter.PaginaA);


                    // PASO 2: Contar tokens del texto del capítulo
                    AiTokrens tokens = new AiTokrens();
                    //  var tokenCount = tokens.GetTokenCount(chapterText);

                    // PASO 3: Procesar con OpenAI para generar resumen, preguntas, etc.
                    //  var processedChapter = await ProcessChapterWithOpenAI(indexChapter, chapterText, tokenCount, containerName, estructura, subcategoria);

                    //      if (processedChapter != null)

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error processing chapter: {ChapterTitle}", indexChapter.Titulo);
                }
            }

            _logger.LogInformation("✅ Chapter content extraction completed: {ProcessedCount}/{TotalCount} chapters",
                resultChapters.Count, extractedIndex.Count);

            return resultChapters;
        }

        /// <summary>
        /// Extrae el texto completo de un capítulo basado en el rango de páginas
        /// </summary>
        private string ExtractChapterTextFromPages(List<DocumentPage> documentPages, int pageFrom, int pageTo)
        {
            var chapterText = new StringBuilder();

            foreach (var page in documentPages)
            {
                if (page.PageNumber >= pageFrom && page.PageNumber <= pageTo)
                {
                    chapterText.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                    if (page.LinesText != null && page.LinesText.Count > 0)
                    {
                        foreach (var line in page.LinesText)
                        {
                            chapterText.AppendLine(line);
                        }
                    }
                }
            }

            return chapterText.ToString();
        }

        /// <summary>
        /// Cuenta los tokens aproximados en un texto (implementación simple)
        /// </summary>
        private int CountTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // Implementación simple: aproximadamente 4 caracteres por token para español
            // Esta es una estimación, idealmente usarías la biblioteca tiktoken de OpenAI
            var estimatedTokens = text.Length / 4;

            // También contamos palabras como métrica adicional
            var wordCount = text.Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

            // Usamos el promedio entre estimación por caracteres y por palabras
            return (int)Math.Round((estimatedTokens + wordCount) / 2.0);
        }

        /// <summary>
        /// Procesa un capítulo individual con OpenAI para generar contenido educativo
        /// </summary>
        private async Task<CapituloExtraido?> ProcessChapterWithOpenAI(
            CapituloIndice indexChapter,
            string chapterText,
            int tokenCount,
            string containerName = "",
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                // Prompt simplificado para reducir tiempo de procesamiento
                var educationPrompt = $@"
Analiza este capítulo y genera contenido educativo:

1) Primero detecta el Idioma del texto
2) Usa este idioma para generar lo que se pide. No camies el idioma 
3) iMportante conservar todo el texto. 
CAPÍTULO:
{chapterText}

Genera SOLO:
1. textoCompleto: Todo el texto del capítulo
2. textoCompletoHTML: El texto en HTML profesional con colores, fonts, espacios, muy atractivo
3. resumenEjecutivo: Resumen de 2-3 párrafos
4: Reglas del HTML:
- Usa colores llamativos
- Usa espacios entre horaciones
- Respeta el idiona oroginal
- Usa bullets , guiones etc. para que se vea mejor 
- usa diferente background para distinguir las orciones
- usa titulos con bold y colores y fotns más grandes
- Los links de www ponlos en azul

IMPORTANTE: Todo el texto que uses tiene que ser en el origen del idioma
JSON (sin ```):
{{
    ""textoCompleto"": ""Todo el texto del capítulo"",
    ""textoCompletoHTML"": ""HTML profesional con colores y formato"",
    ""resumenEjecutivo"": ""Resumen ejecutivo del capítulo"" Siempre usa el idioma original

}}";

                history.AddUserMessage(educationPrompt);

                var response = await chatCompletion.GetChatMessageContentAsync(history);
                var aiResponse = response.Content ?? "{}";

                // Limpiar respuesta
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                // Deserializar usando System.Text.Json
                var chapterResult = JsonSerializer.Deserialize<CapituloExtraido>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (chapterResult != null)
                {
                    // Asignar datos directamente desde los parámetros (más rápido que pedírselos a AI)
                    chapterResult.Titulo = indexChapter.Titulo;
                    chapterResult.NumeroCapitulo = indexChapter.NumeroCapitulo;
                    chapterResult.PaginaDe = indexChapter.PaginaDe;
                    chapterResult.PaginaA = indexChapter.PaginaA;
                    chapterResult.Nivel = indexChapter.Nivel;
                    chapterResult.TotalTokens = tokenCount;

                    // Asignar TwinID, CapituloID, DocumentID, Estructura y Subcategoria automáticamente
                    chapterResult.TwinID = containerName; // containerName es el TwinID
                    chapterResult.CapituloID = Guid.NewGuid().ToString(); // Generar ID único
                    chapterResult.Estructura = estructura; // Asignar estructura del documento
                    chapterResult.Subcategoria = subcategoria; // Asignar subcategoría del documento

                    // Generar DocumentID para agrupar capítulos del mismo documento
                    var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
                    chapterResult.DocumentID = DateTime.Now.ToFileTime() + "_" + Guid.NewGuid().ToString();

                    // Asegurar que el texto completo esté incluido
                    if (string.IsNullOrWhiteSpace(chapterResult.TextoCompleto))
                    {
                        chapterResult.TextoCompleto = chapterText;
                    }

                    _logger.LogInformation("✅ Chapter processed with AI: {Title}, TwinID: {TwinID}, CapituloID: {CapituloID}, DocumentID: {DocumentID}, Estructura: {Estructura}, Subcategoria: {Subcategoria}",
                        chapterResult.Titulo, chapterResult.TwinID, chapterResult.CapituloID, chapterResult.DocumentID, chapterResult.Estructura, chapterResult.Subcategoria);

                    return chapterResult;
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to deserialize AI response for chapter: {Title}", indexChapter.Titulo);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing chapter with OpenAI: {Title}", indexChapter.Titulo);
                return null;
            }
        }

        /// <summary>
        /// Extrae los datos del contenido específicamente para índices
        /// </summary>
        private ExtractedContentData ExtractContentData(Dictionary<string, object> aiData)
        {
            var data = new ExtractedContentData();

            try
            {
                // Extract index-specific data from the root level (since the full response is the index structure)
                if (aiData.TryGetValue("tieneIndice", out var tieneIndiceObj))
                {
                    data.TieneIndice = tieneIndiceObj?.ToString()?.ToLower() == "true";
                }

                if (aiData.TryGetValue("indiceEncontrado", out var indiceEncontradoObj) && indiceEncontradoObj is JsonElement indiceElement)
                {
                    data.IndiceEncontrado = ExtractIndiceInformation(indiceElement);
                }

                if (aiData.TryGetValue("indice", out var indiceArrayObj) && indiceArrayObj is JsonElement arrayElement)
                {
                    data.Indice = ExtractCapitulosIndice(arrayElement);
                }

                data.Observaciones = aiData.GetValueOrDefault("observaciones")?.ToString() ?? "";
                data.EstructuraDetectada = aiData.GetValueOrDefault("estructuraDetectada")?.ToString() ?? "";

                if (aiData.TryGetValue("metadatos", out var metadatosObj) && metadatosObj is JsonElement metadatosElement)
                {
                    data.Metadatos = ExtractMetadatosIndice(metadatosElement);
                }

                // Set document type as index-based document
                data.DocumentType = data.TieneIndice ? "Documento con Índice" : "Documento sin Índice";
                data.MainTopic = data.TieneIndice ? "Análisis de Índice de Documento" : "Documento sin estructura de índice detectada";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error extracting content data, using defaults");
                data.DocumentType = "Documento Procesado";
                data.MainTopic = "Análisis de Documento";
            }

            return data;
        }

        private IndiceInformation? ExtractIndiceInformation(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;

            return new IndiceInformation
            {
                PaginaDelIndice = GetIntFromElement(element, "paginaDelIndice"),
                TipoIndice = GetStringFromElement(element, "tipoIndice"),
                TotalCapitulos = GetIntFromElement(element, "totalCapitulos")
            };
        }

        private List<CapituloIndice> ExtractCapitulosIndice(JsonElement element)
        {
            var capitulos = new List<CapituloIndice>();

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    capitulos.Add(new CapituloIndice
                    {
                        Titulo = GetStringFromElement(item, "titulo"),
                        PaginaDe = GetIntFromElement(item, "paginaDe"),
                        PaginaA = GetIntFromElement(item, "paginaA"),
                        Nivel = GetIntFromElement(item, "nivel"),
                        NumeroCapitulo = GetStringFromElement(item, "numeroCapitulo")
                    });
                }
            }

            return capitulos;
        }

        private MetadatosIndice? ExtractMetadatosIndice(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;

            return new MetadatosIndice
            {
                DocumentoTieneCapitulos = GetBoolFromElement(element, "documentoTieneCapitulos"),
                FormatoNumerico = GetStringFromElement(element, "formatoNumerico"),
                TieneSubsecciones = GetBoolFromElement(element, "tieneSubsecciones"),
                PaginasTotalesDelDocumento = GetIntFromElement(element, "paginasTotalesDelDocumento")
            };
        }

        /// <summary>
        /// Genera un output HTML para mostrar el índice extraído
        /// </summary>
        private string GenerateIndexHtmlOutput(Dictionary<string, object> aiData)
        {
            try
            {
                var tieneIndice = aiData.GetValueOrDefault("tieneIndice")?.ToString()?.ToLower() == "true";
                var observaciones = aiData.GetValueOrDefault("observaciones")?.ToString() ?? "";

                var html = new StringBuilder();
                html.Append(@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background-color: #f8f9fa; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 20px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        h1 { color: #2c3e50; text-align: center; margin-bottom: 30px; }
        h2 { color: #3498db; border-bottom: 2px solid #3498db; padding-bottom: 10px; }
        .index-found { background-color: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 15px 0; }
        .index-not-found { background-color: #f8d7da; border: 1px solid #f5c6cb; padding: 15px; border-radius: 5px; margin: 15px 0; }
        .chapter { margin: 10px 0; padding: 10px; background: #f8f9fa; border-left: 4px solid #3498db; }
        .chapter-title { font-weight: bold; color: #2c3e50; }
        .chapter-pages { color: #7f8c8d; font-size: 0.9em; }
        .level-1 { margin-left: 0; }
        .level-2 { margin-left: 20px; border-left-color: #e67e22; }
        .level-3 { margin-left: 40px; border-left-color: #e74c3c; }
        table { border-collapse: collapse; width: 100%; margin: 20px 0; }
        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }
        th { background-color: #3498db; color: white; }
        .observations { background-color: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>📚 Análisis de Índice del Documento</h1>");

                if (tieneIndice)
                {
                    html.Append("<div class='index-found'><strong>✅ Índice Encontrado</strong><br>Se ha detectado un índice en el documento.</div>");

                    // Extract index information
                    if (aiData.TryGetValue("indiceEncontrado", out var indiceInfo) && indiceInfo is JsonElement indiceElement)
                    {
                        var paginaIndice = GetIntFromJsonElement(indiceElement, "paginaDelIndice");
                        var tipoIndice = GetStringFromJsonElement(indiceElement, "tipoIndice");
                        var totalCapitulos = GetIntFromJsonElement(indiceElement, "totalCapitulos");

                        html.Append($@"
                        <h2>📋 Información del Índice</h2>
                        <table>
                            <tr><th>Tipo de Índice</th><td>{tipoIndice}</td></tr>
                            <tr><th>Página del Índice</th><td>{paginaIndice}</td></tr>
                            <tr><th>Total de Capítulos</th><td>{totalCapitulos}</td></tr>
                        </table>");
                    }

                    // Extract chapters
                    if (aiData.TryGetValue("indice", out var indiceArray) && indiceArray is JsonElement arrayElement && arrayElement.ValueKind == JsonValueKind.Array)
                    {
                        html.Append("<h2>📖 Capítulos Encontrados</h2>");

                        foreach (var item in arrayElement.EnumerateArray())
                        {
                            var titulo = GetStringFromJsonElement(item, "titulo");
                            var paginaDe = GetIntFromJsonElement(item, "paginaDe");
                            var paginaA = GetIntFromJsonElement(item, "paginaA");
                            var nivel = GetIntFromJsonElement(item, "nivel");
                            var numeroCapitulo = GetStringFromJsonElement(item, "numeroCapitulo");

                            html.Append($@"
                            <div class='chapter level-{nivel}'>
                                <div class='chapter-title'>{numeroCapitulo}. {titulo}</div>
                                <div class='chapter-pages'>Páginas: {paginaDe} - {paginaA}</div>
                            </div>");
                        }
                    }
                }
                else
                {
                    html.Append("<div class='index-not-found'><strong>❌ Índice No Encontrado</strong><br>No se pudo detectar un índice claro en las primeras páginas del documento.</div>");
                }

                if (!string.IsNullOrEmpty(observaciones))
                {
                    html.Append($"<div class='observations'><strong>🔍 Observaciones:</strong><br>{observaciones}</div>");
                }

                html.Append(@"
    </div>
</body>
</html>");

                return html.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error generating HTML output for index");
                return "<div style='padding: 20px; color: red;'>Error generando reporte HTML del índice</div>";
            }
        }

        #region Helper Methods

        private string GetStringFromElement(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? "";
            }
            return "";
        }

        private int GetIntFromElement(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            return 0;
        }

        private bool GetBoolFromElement(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return false;
        }

        private string GetStringFromJsonElement(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? "";
            }
            return "";
        }

        private int GetIntFromJsonElement(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            return 0;
        }

        #endregion

        /// <summary>
        /// Traduce el contenido completo de un DocumentAnalysisResult a otro idioma usando OpenAI
        /// </summary>
        /// <param name="documentAnalysis">Resultado del análisis del documento a traducir</param>
        /// <param name="targetLanguage">Código del idioma destino (ej: "es", "en", "fr")</param>
        /// <returns>DocumentAnalysisResult con el contenido traducido</returns>
        public async Task<DocumentAnalysisResult> TranslateDocumentAnalysisAsync(
            DocumentAnalysisResult documentAnalysis,
            string targetLanguage)
        {
            _logger.LogInformation("🌐 Starting translation of document analysis to language: {Language}", targetLanguage);

            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var translatedResult = new DocumentAnalysisResult
                {
                    Success = documentAnalysis.Success,
                    ErrorMessage = documentAnalysis.ErrorMessage,
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = documentAnalysis.SourceUri,
                    TotalPages = documentAnalysis.TotalPages,
                    DocumentPages = new List<DocumentPage>(),
                    Tables = new List<ExtractedTable>()
                };

                // STEP 1: Traducir el contenido principal de texto
                _logger.LogInformation("📝 Translating main text content...");
                if (!string.IsNullOrEmpty(documentAnalysis.TextContent))
                {
                    var history = new ChatHistory();
                    var textPrompt = $@"
Traduce el siguiente texto completo al idioma '{targetLanguage}'. 
Mantén el formato original, incluyendo saltos de línea y estructura.
NO agregues explicaciones adicionales, SOLO devuelve el texto traducido.
En la primera lineas especifica el idioma original del texto y el idioma al que se traduce.

TEXTO A TRADUCIR:
{documentAnalysis.TextContent}

INSTRUCCIONES:
- Traduce TODO el contenido al idioma especificado
- Mantén la estructura original del texto
- Conserva números de página y separadores
- NO agregues comentarios ni explicaciones
- Devuelve ÚNICAMENTE el texto traducido";

                    history.AddUserMessage(textPrompt);
                    var textResponse = await chatCompletion.GetChatMessageContentAsync(history);
                    translatedResult.TextContent = textResponse.Content ?? documentAnalysis.TextContent;
                }

                // STEP 2: Traducir cada página individualmente
                _logger.LogInformation("📄 Translating individual pages...");
                foreach (var page in documentAnalysis.DocumentPages)
                {
                    var translatedPage = new DocumentPage
                    {
                        PageNumber = page.PageNumber,
                        TotalTokens = page.TotalTokens,
                        TargetLanguage = targetLanguage,
                        LinesText = new List<string>()
                    };

                    if (page.LinesText != null && page.LinesText.Count > 0)
                    {
                        var history = new ChatHistory();
                        var linesText = string.Join("\n", page.LinesText);

                        var pagePrompt = $@"
Traduce cada línea del siguiente texto al idioma '{targetLanguage}'.
Mantén cada línea por separado y en el mismo orden.
NO agregues explicaciones, SOLO devuelve las líneas traducidas una por línea.

LÍNEAS A TRADUCIR:
{linesText}

INSTRUCCIONES:
IMPORTANTE: 
En la primera línea especifica el idioma original del texto y el idioma al que se traduce.
- Traduce línea por línea manteniendo el orden
- respeta caracteres especiales como acentos, ñ, ü, etc.
- Conserva líneas vacías si las hay
- NO agregues comentarios
- Devuelve ÚNICAMENTE las líneas traducidas";

                        history.AddUserMessage(pagePrompt);
                        var pageResponse = await chatCompletion.GetChatMessageContentAsync(history);

                        if (!string.IsNullOrEmpty(pageResponse.Content))
                        {
                            translatedPage.LinesText = pageResponse.Content.Split('\n').ToList();
                        }
                        else
                        {
                            translatedPage.LinesText = page.LinesText; // Fallback to original
                        }
                    }

                    translatedResult.DocumentPages.Add(translatedPage);
                }

                // STEP 3: Traducir tablas
                _logger.LogInformation("🗂️ Translating tables...");
                foreach (var table in documentAnalysis.Tables)
                {
                    var translatedTable = new ExtractedTable
                    {
                        RowCount = table.RowCount,
                        ColumnCount = table.ColumnCount,
                        AsSimpleTable = new SimpleTable
                        {
                            Headers = new List<string>(),
                            Rows = new List<List<string>>()
                        }
                    };

                    // Note: RowCount and ColumnCount are read-only properties that are calculated automatically

                    // Traducir headers
                    if (table.AsSimpleTable.Headers != null && table.AsSimpleTable.Headers.Count > 0)
                    {
                        var history = new ChatHistory();
                        var headersText = string.Join(" | ", table.AsSimpleTable.Headers);

                        var headersPrompt = $@"
Traduce los siguientes encabezados de tabla al idioma '{targetLanguage}'.
Separa cada encabezado traducido con ' | '.
NO agregues explicaciones, SOLO devuelve los encabezados traducidos separados por ' | '.

ENCABEZADOS A TRADUCIR:
{headersText}

INSTRUCCIONES:
- Mantén el mismo número de encabezados
- Separa con ' | ' exactamente como en el original
- NO agregues comentarios";

                        history.AddUserMessage(headersPrompt);
                        var headersResponse = await chatCompletion.GetChatMessageContentAsync(history);

                        if (!string.IsNullOrEmpty(headersResponse.Content))
                        {
                            translatedTable.AsSimpleTable.Headers = headersResponse.Content.Split(" | ").ToList();
                        }
                        else
                        {
                            translatedTable.AsSimpleTable.Headers = table.AsSimpleTable.Headers;
                        }
                    }

                    // Traducir filas
                    if (table.AsSimpleTable.Rows != null && table.AsSimpleTable.Rows.Count > 0)
                    {
                        foreach (var row in table.AsSimpleTable.Rows)
                        {
                            var history = new ChatHistory();
                            var rowText = string.Join(" | ", row);

                            var rowPrompt = $@"
Traduce la siguiente fila de tabla al idioma '{targetLanguage}'.
Separa cada celda traducida con ' | '.
NO agregues explicaciones, SOLO devuelve las celdas traducidas separadas por ' | '.

FILA A TRADUCIR:
{rowText}

INSTRUCCIONES:
- Mantén el mismo número de celdas
- Separa con ' | ' exactamente como en el original
- NO agregues comentarios";

                            history.AddUserMessage(rowPrompt);
                            var rowResponse = await chatCompletion.GetChatMessageContentAsync(history);

                            if (!string.IsNullOrEmpty(rowResponse.Content))
                            {
                                translatedTable.AsSimpleTable.Rows.Add(rowResponse.Content.Split(" | ").ToList());
                            }
                            else
                            {
                                translatedTable.AsSimpleTable.Rows.Add(row); // Fallback to original
                            }
                        }
                    }

                    translatedResult.Tables.Add(translatedTable);
                }

                _logger.LogInformation("✅ Document translation completed successfully to language: {Language}", targetLanguage);
                _logger.LogInformation("📊 Translation stats: {Pages} pages, {Tables} tables translated",
                    translatedResult.DocumentPages.Count, translatedResult.Tables.Count);

                return translatedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error translating document analysis to language: {Language}", targetLanguage);

                // Return original document with error information
                return new DocumentAnalysisResult
                {
                    Success = false,
                    ErrorMessage = $"Translation failed: {ex.Message}",
                    TextContent = documentAnalysis.TextContent,
                    Tables = documentAnalysis.Tables,
                    DocumentPages = documentAnalysis.DocumentPages,
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = documentAnalysis.SourceUri,
                    TotalPages = documentAnalysis.TotalPages
                };
            }
        }
    }

    #region Result Classes

    /// <summary>
    /// Resultado del procesamiento de documentos no estructurados
    /// </summary>
    public class UnstructuredDocumentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? DocumentUrl { get; set; }
        public string RawTextContent { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public List<DocumentPage> DocumentPages { get; set; } = new();
        public List<ExtractedTable> Tables { get; set; } = new();
        public DateTime ProcessedAt { get; set; }

        // AI Processing Results
        public ExtractedContentData ExtractedContent { get; set; } = new();
        public StructuredDocumentData StructuredData { get; set; } = new();
        public List<DocumentInsightData> KeyInsights { get; set; } = new();
        public string ExecutiveSummary { get; set; } = string.Empty;
        public string HtmlOutput { get; set; } = string.Empty;
        public string? RawAIResponse { get; set; }

        /// <summary>
        /// Lista de capítulos extraídos y procesados con AI (cuando se encuentra un índice)
        /// </summary>
        public List<CapituloExtraido> ExtractedChapters { get; set; } = new();

        /// <summary>
        /// Get full path of the document
        /// </summary>
        public string FullPath => $"{ContainerName}/{FilePath}/{FileName}";

        /// <summary>
        /// Get comprehensive summary of processing results
        /// </summary>
        public string GetComprehensiveSummary()
        {
            if (!Success)
            {
                return $"❌ Processing failed: {ErrorMessage}";
            }

            return $"✅ Successfully processed: {FileName}\n" +
                   $"📍 Location: {FullPath}\n" +
                   $"📄 Pages: {TotalPages}\n" +
                   $"📊 Tables: {Tables.Count}\n" +
                   $"💡 Insights: {KeyInsights.Count}\n" +
                   $"📋 Document Type: {ExtractedContent.DocumentType}\n" +
                   $"🎯 Main Topic: {ExtractedContent.MainTopic}\n" +
                   $"📚 Has Index: {(ExtractedContent.TieneIndice ? "Sí" : "No")}\n" +
                   $"📖 Index Chapters: {ExtractedContent.Indice.Count}\n" +
                   $"📚 Processed Chapters: {ExtractedChapters.Count}\n" +
                   $"📅 Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC";
        }
    }

    /// <summary>
    /// Resultado del procesamiento con AI
    /// </summary>
    public class UnstructuredDocumentAIResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public ExtractedContentData ExtractedContent { get; set; } = new();
        public StructuredDocumentData StructuredData { get; set; } = new();
        public List<DocumentInsightData> KeyInsights { get; set; } = new();
        public string ExecutiveSummary { get; set; } = string.Empty;
        public string HtmlOutput { get; set; } = string.Empty;
        public string? RawAIResponse { get; set; }

        /// <summary>
        /// Lista de capítulos extraídos y procesados con AI (cuando se encuentra un índice)
        /// </summary>
        public List<CapituloDocumento> ExtractedChapters { get; set; } = new();
    }

    /// <summary>
    /// Contenido extraído del documento
    /// </summary>
    public class ExtractedContentData
    {
        public string MainTopic { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public List<string> KeyDates { get; set; } = new();
        public List<string> KeyNames { get; set; } = new();
        public List<string> KeyNumbers { get; set; } = new();
        public List<string> KeyAddresses { get; set; } = new();
        public List<string> KeyPhones { get; set; } = new();
        public List<string> KeyEmails { get; set; } = new();
        public List<ImportantSectionData> ImportantSections { get; set; } = new();

        // New fields for index extraction
        public bool TieneIndice { get; set; } = false;
        public IndiceInformation? IndiceEncontrado { get; set; }
        public List<CapituloIndice> Indice { get; set; } = new();
        public string Observaciones { get; set; } = string.Empty;
        public string EstructuraDetectada { get; set; } = string.Empty;
        public MetadatosIndice? Metadatos { get; set; }
    }

    /// <summary>
    /// Información sobre el índice encontrado
    /// </summary>
    public class IndiceInformation
    {
        public int PaginaDelIndice { get; set; }
        public string TipoIndice { get; set; } = string.Empty;
        public int TotalCapitulos { get; set; }
    }

    /// <summary>
    /// Representa un capítulo o sección en el índice
    /// </summary>
    public class CapituloIndice
    {
        public string Titulo { get; set; } = string.Empty;
        public int PaginaDe { get; set; }
        public int PaginaA { get; set; }
        public int Nivel { get; set; } = 1; // 1 = principal, 2 = subsección, etc.
        public string NumeroCapitulo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Metadatos adicionales del índice
    /// </summary>
    public class MetadatosIndice
    {
        public bool DocumentoTieneCapitulos { get; set; }
        public string FormatoNumerico { get; set; } = string.Empty;
        public bool TieneSubsecciones { get; set; }
        public int PaginasTotalesDelDocumento { get; set; }
    }

    /// <summary>
    /// Datos estructurados del documento
    /// </summary>
    public class StructuredDocumentData
    {
        public string Summary { get; set; } = string.Empty;
        public DocumentEntitiesData Entities { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Entidades extraídas del documento
    /// </summary>
    public class DocumentEntitiesData
    {
        public List<string> Organizations { get; set; } = new();
        public List<string> People { get; set; } = new();
        public List<string> Locations { get; set; } = new();
        public List<string> Dates { get; set; } = new();
        public List<string> Amounts { get; set; } = new();
    }

    /// <summary>
    /// Insight extraído del documento
    /// </summary>
    public class DocumentInsightData
    {
        public string Insight { get; set; } = string.Empty;
        public string Importance { get; set; } = "MEDIUM"; // HIGH, MEDIUM, LOW
        public string Category { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sección importante del documento
    /// </summary>
    public class ImportantSectionData
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int Page { get; set; }
    }

    /// <summary>
    /// Representa un capítulo extraído y procesado with AI
    /// </summary>
    public class CapituloExtraido
    {
        /// <summary>
        /// Título del capítulo extraído del índice
        /// </summary>
        /// 
        public string Estructura { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;

        public string CapituloID { get; set; } = string.Empty;

        public string TwinID { get; set; } = string.Empty;

        /// <summary>
        /// DocumentID for grouping chapters from the same document
        /// </summary>
        public string DocumentID { get; set; } = string.Empty;

        /// <summary>
        /// número de capítulo como aparece en el índice
        /// </summary>
        public string NumeroCapitulo { get; set; } = string.Empty;

        /// <summary>
        /// Página de inicio del capítulo
        /// </summary>
        public int PaginaDe { get; set; }

        /// <summary>
        /// Página de fin del capítulo
        /// </summary>
        public int PaginaA { get; set; }

        /// <summary>
        /// Nivel jerárquico del capítulo (1=principal,   2=subsección, etc.)
        /// </summary>
        public int Nivel { get; set; } = 1;

        /// <summary>
        /// Número total de tokens estimados en el capítulo
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Todo el texto del capítulo extraído en un solo string
        /// </summary>
        public string TextoCompleto { get; set; } = string.Empty;

        public string TextoCompletoHTML { get; set; } = string.Empty;

        /// <summary>
        /// Resumen ejecutivo del capítulo generado por AI
        /// </summary>
        public string ResumenEjecutivo { get; set; } = string.Empty;

        /// <summary>
        /// Lista de 15 preguntas frecuentes con respuestas sobre el capítulo
        /// </summary>
        public List<PreguntaFrecuente> PreguntasFrecuentes { get; set; } = new List<PreguntaFrecuente>();

        /// <summary>
        /// Fecha y hora cuando fue procesado el capítulo
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Obtiene un resumen del procesamiento del capítulo
        /// </summary>
        public string GetProcessingSummary()
        {
            return $"📖 Capítulo: {Titulo}\n" +
                   $"📄 Páginas: {PaginaDe}-{PaginaA}\n" +
                   $"🔢 Tokens: {PreguntasFrecuentes.Count}\n" +
                   $"❓ Preguntas: {PreguntasFrecuentes.Count}\n" +
                   $"📊 Nivel: {Nivel}\n" +
                   $"⏰ Procesado: {ProcessedAt:yyyy-MM-dd HH:mm}";
        }
    }

    /// <summary>
    /// Representa una pregunta frecuente con su respuesta sobre un capítulo
    /// </summary>
    public class PreguntaFrecuente
    {
        /// <summary>
        /// La pregunta generada por AI sobre el contenido del capítulo
        /// </summary>
        public string Pregunta { get; set; } = string.Empty;

        /// <summary>
        /// La respuesta basada en el contenido del capítulo
        /// </summary>
        public string Respuesta { get; set; } = string.Empty;

        /// <summary>
        /// Categoría de la pregunta (opcional)
        /// </summary>
        public string Categoria { get; set; } = string.Empty;

        /// <summary>
        /// Nivel de dificultad de la pregunta (Básico, Intermedio, Avanzado)
        /// </summary>
        public string Dificultad { get; set; } = "Básico";

        /// <summary>
        /// Obtiene una representación de texto de la pregunta y respuesta
        /// </summary>
        public string GetFormattedQA()
        {
            return $"❓ **Pregunta:** {Pregunta}\n" +
                   $"💡 **Respuesta:** {Respuesta}";
        }
    }

    public class DocumentoIndice
    {
        [JsonPropertyName("tieneIndice")]
        public bool TieneIndice { get; set; }

        [JsonPropertyName("indiceGeneradoAutomaticamente")]
        public bool IndiceGeneradoAutomaticamente { get; set; }

        [JsonPropertyName("paginasAnalizadas")]
        public int PaginasAnalizadas { get; set; }

        [JsonPropertyName("indiceEncontrado")]
        public IndiceEncontrado IndiceEncontrado { get; set; } = new IndiceEncontrado();

        [JsonPropertyName("indice")]
        public List<IndiceItem> Indice { get; set; } = new List<IndiceItem>();

        [JsonPropertyName("observaciones")]
        public string Observaciones { get; set; } = string.Empty;

        [JsonPropertyName("estructuraDetectada")]
        public string EstructuraDetectada { get; set; } = string.Empty;

        [JsonPropertyName("metadatos")]
        public Metadatos Metadatos { get; set; } = new Metadatos();
    }

    public class IndiceEncontrado
    {
        [JsonPropertyName("paginaDelIndice")]
        public int PaginaDelIndice { get; set; }

        [JsonPropertyName("tipoIndice")]
        public string TipoIndice { get; set; } = string.Empty;

        [JsonPropertyName("totalCapitulos")]
        public int TotalCapitulos { get; set; }
    }

    public class IndiceItem
    {
        [JsonPropertyName("titulo")]
        public string Titulo { get; set; } = string.Empty;

        [JsonPropertyName("texto")]
        public string Texto { get; set; } = string.Empty;

        [JsonPropertyName("textoHTML")]
        public string TextoHTML { get; set; } = string.Empty;

        [JsonPropertyName("paginaDe")]
        public int PaginaDe { get; set; }

        [JsonPropertyName("paginaA")]
        public int PaginaA { get; set; }

        [JsonPropertyName("nivel")]
        public int Nivel { get; set; }

        [JsonPropertyName("numeroCapitulo")]
        public string NumeroCapitulo { get; set; } = string.Empty;
    }

    public class PDfDocumentNoStructured
    {

        public List<ExractedChapterIndex> ChapterList { get; set; } = new List<ExractedChapterIndex>();

        public int TotalTokens { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        public DateTime DateModified { get; set; } = DateTime.UtcNow;


    }

    public class ChapterIndex
    {
        [JsonPropertyName("chapter")]
        public string ChapterTitle { get; set; } = string.Empty;
        [JsonPropertyName("pageFrom")]
        public int PageFrom { get; set; }

        [JsonPropertyName("pageTo")]
        public int PageTo { get; set; }


        [JsonPropertyName("subchapters")]
        public List<string> Subchapters { get; set; } = new List<string>();

        // This field is not present in your JSON so it will default to 0.  
        // If you don't need it, you can remove it.  
        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }
    }


    public class ExractedChapterIndex
    {
        [JsonPropertyName("chapter")]
        public string ChapterTitle { get; set; } = string.Empty;

        public string id { get; set; } = string.Empty;
        public int TotalTokensDocument { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string ChapterID { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public int FromPage { get; set; }
        public int ToPage { get; set; }
        [JsonPropertyName("subchapters")]
        public List<SubChapter> Subchapters { get; set; } = new List<SubChapter>();

        // This field is not present in your JSON so it will default to 0.  
        // If you don't need it, you can remove it.  
        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }
    }

    public class ExractedChapterSubsIndex
    {
        [JsonPropertyName("chapter")]
        public string ChapterTitle { get; set; } = string.Empty;


        public string TwinID { get; set; } = string.Empty;
        public int TotalTokensDocument { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string ChapterID { get; set; } = string.Empty;

        public string TextChapter { get; set; } = string.Empty;

        public int FromPageChapter { get; set; }
        public int ToPageChapter { get; set; }

        // This field is not present in your JSON so it will default to 0.  
        // If you don't need it, you can remove it.  
        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }

        /// <summary>
        /// Gets or sets the title of the subchapter.
        /// </summary>
        public string TitleSub { get; set; }

        public string TextSub { get; set; }
        public int TotalTokensSub { get; set; }
        public int FromPageSub { get; set; }
        public int ToPageSub { get; set; }


    }
    public class SubChapter
    {
        [System.Text.Json.Serialization.JsonPropertyName("chapter")]
        public string Chapter { get; set; }

        public string Ttitle { get; set; }

        public string Text { get; set; }
        public int TotalTokens { get; set; }
        public int FromPage { get; set; }
        public int ToPage { get; set; }




    }
    public class Metadatos
    {
        [JsonPropertyName("documentoTieneCapitulos")]
        public bool DocumentoTieneCapitulos { get; set; }

        [JsonPropertyName("formatoNumerico")]
        public string FormatoNumerico { get; set; } = string.Empty;

        [JsonPropertyName("tieneSubsecciones")]
        public bool TieneSubsecciones { get; set; }

        [JsonPropertyName("paginasTotalesDelDocumento")]
        public int PaginasTotalesDelDocumento { get; set; }
    }
}

#endregion