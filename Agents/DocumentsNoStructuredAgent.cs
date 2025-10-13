using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using TwinFx.Services;

namespace TwinFx.Agents
{
    public class DocumentsNoStructuredAgent
    {
        private readonly ILogger<DocumentsNoStructuredAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;

        public DocumentsNoStructuredAgent(ILogger<DocumentsNoStructuredAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Initialize Semantic Kernel for AI processing
                var builder = Kernel.CreateBuilder();
                
                // Add Azure OpenAI chat completion
                var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                var apiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                var deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("✅ Semantic Kernel initialized successfully");
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
        /// <param name="containerName">Nombre del contenedor (TwinID)</param>
        /// <param name="filePath">Ruta del archivo dentro del contenedor</param>
        /// <param name="fileName">Nombre del archivo</param>
        /// <param name="estructura">Estructura del documento (e.g., "no-estructurado")</param>
        /// <param name="subcategoria">Subcategoría del documento (e.g., "general", "contratos", "manuales")</param>
        /// <returns>Resultado del procesamiento del documento no estructurado</returns>
        public async Task<UnstructuredDocumentResult> ExtractDocumentDataAsync(
            string containerName,
            string filePath,
            string fileName,
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {
            _logger.LogInformation("📄 Starting unstructured document data extraction for: {FileName}", fileName);
            _logger.LogInformation("📂 Container: {Container}, Path: {Path}", containerName, filePath);
            _logger.LogInformation("🏗️ Document metadata: Estructura={Estructura}, Subcategoria={Subcategoria}", estructura, subcategoria);

            var result = new UnstructuredDocumentResult
            {
                Success = false,
                ContainerName = containerName,
                FilePath = filePath,
                FileName = fileName,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // STEP 1: Generate SAS URL for Document Intelligence access
                _logger.LogInformation("🔗 STEP 1: Generating SAS URL for document access...");
                
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(containerName);
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
                
                var aiResult = await ProcessWithAI(documentAnalysis, containerName, estructura, subcategoria);
                
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
                result.ExtractedChapters = aiResult.ExtractedChapters;

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

        /// <summary>
        /// Procesa el documento con AI para extraer información estructurada
        /// </summary>
        private async Task<UnstructuredDocumentAIResult> ProcessWithAI(
            DocumentAnalysisResult documentAnalysis, 
            string containerName = "", 
            string estructura = "no-estructurado", 
            string subcategoria = "general")
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                // Construir contenido SOLO de las primeras 5 páginas para extracción de índice
                var pagesContent = new StringBuilder();
                var pagesToAnalyze = Math.Min(5, documentAnalysis.DocumentPages.Count);
                
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

                var prompt = $@"
Analiza este documento no estructurado y EXTRAE ESPECÍFICAMENTE EL ÍNDICE del documento.

CONTENIDO DEL DOCUMENTO POR PÁGINAS:
{pagesContent}

TABLAS ENCONTRADAS:
{tablesContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}

INSTRUCCIONES ESPECÍFICAS PARA EXTRACCIÓN DE ÍNDICE:
===================================================

ANÁLISIS DE LAS PRIMERAS 5 PÁGINAS:
Tu tarea principal es encontrar y extraer el ÍNDICE o TABLA DE CONTENIDOS del documento que normalmente aparece en las primeras páginas.

BUSCA PATRONES DE ÍNDICE COMO:
- ""Índice"", ""Tabla de Contenidos"", ""Contents"", ""Contenido""
- ""Capítulo"", ""Chapter"", ""Sección"", ""Section""
- Números de página al final de cada línea
- Estructura jerárquica con números o letras
- Puntos suspensivos (....) entre título y página
- Formato típico: ""Título .................. Página""

EXTRAE CADA ENTRADA DEL ÍNDICE CON:
- Título exacto del capítulo/sección
- Página de inicio 
- Página de fin (si está especificada, sino usar página de inicio)

IMPORTANTE: 
1. BUSCA SOLO en las primeras 5 páginas del documento
2. NO inventes capítulos que no existan
3. SI NO encuentras un índice claro, responde con array vacío
4. Extrae EXACTAMENTE los títulos como aparecen en el índice
5. Los números de página deben ser números enteros válidos

FORMATO DE RESPUESTA JSON (sin ```json ni ```):
{{
    ""tieneIndice"": true,
    ""paginasAnalizadas"": {pagesToAnalyze},
    ""indiceEncontrado"": {{
        ""paginaDelIndice"": 2,
        ""tipoIndice"": ""Tabla de Contenidos"",
        ""totalCapitulos"": 10
    }},
    ""indice"": [
        {{
            ""titulo"": ""Introducción"",
            ""paginaDe"": 1,
            ""paginaA"": 5,
            ""nivel"": 1,
            ""numeroCapitulo"": ""1""
        }},
        {{
            ""titulo"": ""Marco Teórico"",
            ""paginaDe"": 6,
            ""paginaA"": 25,
            ""nivel"": 1,
            ""numeroCapitulo"": ""2""
        }},
        {{
            ""titulo"": ""2.1 Conceptos Fundamentales"",
            ""paginaDe"": 6,
            ""paginaA"": 15,
            ""nivel"": 2,
            ""numeroCapitulo"": ""2.1""
        }}
    ],
    ""observaciones"": ""Descripción de cómo se encontró el índice o por qué no se encontró"",
    ""estructuraDetectada"": ""Descripción del tipo de estructura del índice encontrado"",
    ""metadatos"": {{
        ""documentoTieneCapitulos"": true,
        ""formatoNumerico"": ""1, 2, 3..."",
        ""tieneSubsecciones"": true,
        ""paginasTotalesDelDocumento"": {documentAnalysis.TotalPages}
    }}
}}

EJEMPLOS DE PATRONES A BUSCAR:
=============================

PATRÓN 1 - Típico:
Índice
1. Introducción ............................ 1
2. Marco Teórico ........................... 5
3. Metodología ............................. 15

PATRÓN 2 - Con subsecciones:
Tabla de Contenidos
Capítulo 1: Introducción ................... 1
1.1 Antecedentes ........................... 2
1.2 Objetivos .............................. 4
Capítulo 2: Desarrollo ..................... 6

PATRÓN 3 - Simple:
CONTENIDO
Presentación ............................... 3
Historia ................................... 7
Análisis ................................... 12

REGLAS CRÍTICAS:
===============
- Si NO encuentras un índice claro, pon ""tieneIndice"": false e ""indice"": []
- NO inventes capítulos basándote en el contenido general
- SOLO extrae lo que está explícitamente listado como índice
- Números de página deben ser coherentes y crecientes
- Si encuentras páginas duplicadas o incoherentes, márcalo en observaciones

RESPONDE ÚNICAMENTE EN JSON VÁLIDO SIN MARKDOWN:";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Clean response of any markdown formatting
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }
           
                _logger.LogInformation("📝 AI Response Length: {Length} characters", aiResponse.Length);

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
                var capitulosExtraidos = new List<CapituloExtraido>();



                if (hasIndex && extractedContent.Indice.Count > 0)
                {
                    _logger.LogInformation("🚀 STEP 4: Index found! Automatically extracting chapter content with AI...");
                    
                    try
                    {
                        // Ejecutar ExtractChapterContentWithAI automáticamente pasando containerName, estructura y subcategoria
                        capitulosExtraidos = await ExtractChapterContentWithAI(documentAnalysis, extractedContent.Indice, containerName, estructura, subcategoria);
                        
                        _logger.LogInformation("✅ Chapter content extraction completed: {ProcessedChapters}/{TotalChapters} chapters processed successfully", 
                            capitulosExtraidos.Count, extractedContent.Indice.Count);

                        // **NUEVO: STEP 5 - Indexar capítulos extraídos en Azure Search**
                        if (capitulosExtraidos.Count > 0)
                        {
                            _logger.LogInformation("📄 STEP 5: Indexing extracted chapters in no-structured-index...");
                            
                            try
                            {
                                // Crear instancia del DocumentsNoStructuredIndex
                                var indexLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DocumentsNoStructuredIndex>();
                                var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

                                // Indexar todos los capítulos extraídos
                                var indexResults = await documentsIndex.IndexMultipleCapitulosAsync(capitulosExtraidos);
                                
                                var successCount = indexResults.Count(r => r.Success);
                                var failureCount = indexResults.Count(r => !r.Success);
                                
                                _logger.LogInformation("✅ Chapter indexing completed: {SuccessCount}/{TotalCount} chapters indexed successfully", 
                                    successCount, capitulosExtraidos.Count);
                                
                                if (failureCount > 0)
                                {
                                    _logger.LogWarning("⚠️ {FailureCount} chapters failed to index", failureCount);
                                }
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
        /// Extrae el contenido completo de cada capítulo basado en el índice y procesa con OpenAI
        /// </summary>
        /// <param name="documentAnalysis">Resultado del análisis del documento con todas las páginas</param>
        /// <param name="extractedIndex">Índice extraído con capítulos y páginas</param>
        /// <param name="containerName">Nombre del contenedor (TwinID)</param>
        /// <param name="estructura">Estructura del documento</param>
        /// <param name="subcategoria">Subcategoría del documento</param>
        /// <returns>Lista de capítulos procesados con contenido, resumen, tokens y preguntas</returns>
        public async Task<List<CapituloExtraido>> ExtractChapterContentWithAI(
            DocumentAnalysisResult documentAnalysis, 
            List<CapituloIndice> extractedIndex,
            string containerName = "",
            string estructura = "no-estructurado",
            string subcategoria = "general")
        {
            _logger.LogInformation("📚 Starting chapter content extraction for {ChapterCount} chapters", extractedIndex.Count);
            _logger.LogInformation("🏗️ Document metadata: Estructura={Estructura}, Subcategoria={Subcategoria}", estructura, subcategoria);
            
            var resultChapters = new List<CapituloExtraido>();
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();

            foreach (var indexChapter in extractedIndex)
            {
                try
                {
                    _logger.LogInformation("📖 Processing chapter: {ChapterTitle} (Pages {PageFrom}-{PageTo})", 
                        indexChapter.Titulo, indexChapter.PaginaDe, indexChapter.PaginaA);

                    // PASO 1: Extraer todo el texto del capítulo basado en las páginas del índice
                    var chapterText = ExtractChapterTextFromPages(documentAnalysis.DocumentPages, 
                        indexChapter.PaginaDe, indexChapter.PaginaA);

                    if (string.IsNullOrWhiteSpace(chapterText))
                    {
                        _logger.LogWarning("⚠️ No content found for chapter: {ChapterTitle}", indexChapter.Titulo);
                        continue;
                    }

                    // PASO 2: Contar tokens del texto del capítulo
                    AiTokrens tokens = new AiTokrens();
                    var tokenCount = tokens.GetTokenCount(chapterText);
                    _logger.LogInformation("📊 Chapter text extracted: {TextLength} chars, {TokenCount} tokens", 
                        chapterText.Length, tokenCount);

                    // PASO 3: Procesar con OpenAI para generar resumen, preguntas, etc.
                    var processedChapter = await ProcessChapterWithOpenAI(indexChapter, chapterText, tokenCount, containerName, estructura, subcategoria);

                    if (processedChapter != null)
                    {
                        resultChapters.Add(processedChapter);
                        _logger.LogInformation("✅ Chapter processed successfully: {ChapterTitle}", indexChapter.Titulo);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to process chapter with AI: {ChapterTitle}", indexChapter.Titulo);
                    }
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

                var educationPrompt = $@"
Eres un profesor experto que analiza capítulos de documentos y crea contenido educativo de alta calidad.

INFORMACIÓN DEL CAPÍTULO:
========================
Título: {indexChapter.Titulo}
Número de Capítulo: {indexChapter.NumeroCapitulo}
Páginas: {indexChapter.PaginaDe} - {indexChapter.PaginaA}
Nivel: {indexChapter.Nivel}
Tokens estimados: {tokenCount}

CONTENIDO COMPLETO DEL CAPÍTULO:
===============================
{chapterText}

INSTRUCCIONES PARA EL ANÁLISIS:
==============================

Tu tarea es analizar este capítulo y generar contenido educativo completo que incluya:

1. **Resumen Ejecutivo**: Un resumen conciso pero completo del capítulo (2-3 párrafos)
2. **Preguntas Frecuentes**: 15 preguntas con sus respuestas sobre el contenido del capítulo

IMPORTANTE: 
- Basa TODO tu análisis ÚNICAMENTE en el contenido proporcionado del capítulo
- NO inventes información que no esté en el texto
- Las preguntas deben ser específicas del contenido real del capítulo
- Las respuestas deben basarse exclusivamente en la información del texto
IMportante. El textoCompletoHTML debe de tener todo el texto una copia exacta en 
esspanol. PEro quiero ver listas, grids, tablas,
mejora la estructura de la presentacion del contenido en HTML.
Usa colores, bolds, analiza el texto y ve como mejorar 
los oclors. No cambies el contendido mejora como se ve muy profesional 

FORMATO DE RESPUESTA JSON (sin ```json ni ```):
{{
    ""titulo"": ""{indexChapter.Titulo}"",
    ""numeroCapitulo"": ""{indexChapter.NumeroCapitulo}"",
    ""paginaDe"": {indexChapter.PaginaDe},
    ""paginaA"": {indexChapter.PaginaA},
    ""nivel"": {indexChapter.Nivel},
    ""totalTokens"": {tokenCount},
    ""textoCompleto"": ""Todo el texto del capítulo en un solo string"",
    ""textoCompletoHTML"": ""Todo el texto del capitulo en html, colores listas grids, numeros de pagina, super profesional"",
    ""resumenEjecutivo"": ""Resumen ejecutivo completo del capítulo con los puntos clave y conceptos principales"",
    ""preguntasFrecuentes"": [
        {{
            ""pregunta"": ""¿Cuál es el concepto principal tratado en este capítulo?"",
            ""respuesta"": ""Respuesta basada exclusivamente en el contenido del capítulo""
        }},
        {{
            ""pregunta"": ""¿Qué aspectos específicos se abordan en esta sección?"",
            ""respuesta"": ""Respuesta detallada basada en el texto del capítulo""
        }},
        {{
            ""pregunta"": ""¿Cuáles son los puntos clave mencionados?"",
            ""respuesta"": ""Lista de puntos clave extraídos del contenido""
        }},
        {{
            ""pregunta"": ""¿Qué información específica proporciona este capítulo?"",
            ""respuesta"": ""Información específica encontrada en el texto""
        }},
        {{
            ""pregunta"": ""¿Cómo se estructura la información en este capítulo?"",
            ""respuesta"": ""Descripción de la estructura basada en el contenido""
        }},
        {{
            ""pregunta"": ""¿Qué ejemplos o casos se mencionan?"",
            ""respuesta"": ""Ejemplos específicos encontrados en el texto""
        }},
        {{
            ""pregunta"": ""¿Cuáles son las conclusiones principales?"",
            ""respuesta"": ""Conclusiones basadas en el contenido del capítulo""
        }},
        {{
            ""pregunta"": ""¿Qué metodología o enfoque se presenta?"",
            ""respuesta"": ""Metodología descrita en el capítulo""
        }},
        {{
            ""pregunta"": ""¿Qué datos o cifras importantes se mencionan?"",
            ""respuesta"": ""Datos específicos encontrados en el texto""
        }},
        {{
            ""pregunta"": ""¿Cómo se relaciona este capítulo con el tema general?"",
            ""respuesta"": ""Relación basada en el contexto del capítulo""
        }},
        {{
            ""pregunta"": ""¿Qué definiciones importantes se proporcionan?"",
            ""respuesta"": ""Definiciones específicas del texto""
        }},
        {{
            ""pregunta"": ""¿Cuáles son las implicaciones mencionadas?"",
            ""respuesta"": ""Implicaciones descritas en el capítulo""
        }},
        {{
            ""pregunta"": ""¿Qué recomendaciones o sugerencias se hacen?"",
            ""respuesta"": ""Recomendaciones específicas del texto""
        }},
        {{
            ""pregunta"": ""¿Qué limitaciones o consideraciones se mencionan?"",
            ""respuesta"": ""Limitaciones identificadas en el contenido""
        }},
        {{
            ""pregunta"": ""¿Cuál es la relevancia práctica de este capítulo?"",
            ""respuesta"": ""Aplicación práctica basada en el contenido""
        }}
    ]
}}

REGLAS CRÍTICAS:
================
- El textoCompleto debe contener TODO el texto del capítulo en un solo string
- El resumen debe capturar la esencia del capítulo en 2-3 párrafos máximo
- Las 15 preguntas deben cubrir diferentes aspectos del contenido
- TODAS las respuestas deben basarse ÚNICAMENTE en el texto proporcionado
- NO inventes información que no esté en el capítulo
- Si no hay suficiente información para alguna pregunta, responde ""No se especifica en este capítulo""

RESPONDE ÚNICAMENTE EN JSON VÁLIDO SIN MARKDOWN:";

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
                    // **NUEVO: Asignar TwinID, CapituloID, DocumentID, Estructura y Subcategoria automáticamente**
                    chapterResult.TwinID = containerName; // containerName es el TwinID
                    chapterResult.CapituloID = Guid.NewGuid().ToString(); // Generar ID único
                    chapterResult.Estructura = estructura; // Asignar estructura del documento
                    chapterResult.Subcategoria = subcategoria; // Asignar subcategoría del documento
                    
                    // **NUEVO: Generar DocumentID para agrupar capítulos del mismo documento**
                    var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
                    chapterResult.DocumentID = $"{containerName}_{estructura}_{subcategoria}_{dateStr}".Replace(" ", "_").Replace("-", "_").ToLowerInvariant();

                    // Asegurar que el texto completo esté incluido
                    if (string.IsNullOrWhiteSpace(chapterResult.TextoCompleto))
                    {
                        chapterResult.TextoCompleto = chapterText;
                    }

                    _logger.LogInformation("✅ Chapter processed with AI: {Title}, Questions: {QuestionCount}, TwinID: {TwinID}, CapituloID: {CapituloID}, DocumentID: {DocumentID}, Estructura: {Estructura}, Subcategoria: {Subcategoria}", 
                        chapterResult.Titulo, chapterResult.PreguntasFrecuentes?.Count ?? 0, chapterResult.TwinID, chapterResult.CapituloID, chapterResult.DocumentID, chapterResult.Estructura, chapterResult.Subcategoria);

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
        public List<CapituloExtraido> ExtractedChapters { get; set; } = new();
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
        public string Importance { get; set; } = string.Empty; // HIGH, MEDIUM, LOW
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
    /// Representa un capítulo extraído y procesado con AI
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
        /// Número de capítulo como aparece en el índice
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
        /// Nivel jerárquico del capítulo (1=principal, 2=subsección, etc.)
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
                   $"🔢 Tokens: {TotalTokens:N0}\n" +
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

    #endregion
}