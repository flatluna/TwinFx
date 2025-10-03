using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using TwinFx.Services;
using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Newtonsoft.Json;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agent for processing skill-related documents and generating content indexes
    /// </summary>
    public class SkillsAgent
    {
        private readonly ILogger<SkillsAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private Kernel? _kernel;

        // Azure AI Foundry configuration for Bing Grounding
        private const string PROJECT_ENDPOINT = "https://twinet-resource.services.ai.azure.com/api/projects/twinet";
        private const string MODEL_DEPLOYMENT_NAME = "gpt4mini";
        private const string BING_CONNECTION_ID = "/subscriptions/bf5f11e8-1b22-4e27-b55e-8542ff6dec42/resourceGroups/rg-jorgeluna-7911/providers/Microsoft.CognitiveServices/accounts/twinet-resource/projects/twinet/connections/twinbing";

        public SkillsAgent(ILogger<SkillsAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize SkillsAgent");
                throw;
            }
        }

        /// <summary>
        /// Initialize Semantic Kernel for AI processing
        /// </summary>
        private async Task InitializeKernelAsync()
        {
            if (_kernel != null) return;

            try
            {
                var builder = Kernel.CreateBuilder();
                
                // Add Azure OpenAI chat completion
                var endpoint = _configuration["AzureOpenAI:Endpoint"] ?? 
                              _configuration["Values:AzureOpenAI:Endpoint"] ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                
                var apiKey = _configuration["AzureOpenAI:ApiKey"] ?? 
                            _configuration["Values:AzureOpenAI:ApiKey"] ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                
                var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? 
                                    _configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                    "gpt-4";

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                
                _logger.LogInformation("✅ Semantic Kernel initialized successfully for SkillsAgent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel");
                throw;
            }
        }

        /// <summary>
        /// Search for learning resources using Bing Grounding with Azure AI Agents
        /// Finds links, images, videos, books, websites, and educational content for skill development
        /// </summary>
        /// <param name="searchQuery">Learning topic or skill to search for</param>
        /// <returns>Comprehensive learning resources found online</returns>
        public async Task<SkillLearningSearchResult> BingSearchForLearningAsync(string searchQuery)
        {
            _logger.LogInformation("🔍 Searching for learning resources: {SearchQuery}", searchQuery);

            try
            {
                if (string.IsNullOrEmpty(searchQuery))
                {
                    return new SkillLearningSearchResult
                    {
                        Success = false,
                        ErrorMessage = "Search query cannot be empty",
                        SearchQuery = searchQuery
                    };
                }

                // Use Bing Grounding Search for comprehensive learning resources
                var searchResult = await BingGroundingSearchAsync(searchQuery);
                
                return new SkillLearningSearchResult
                {
                    Success = true,
                    SearchQuery = searchQuery,
                    LearningResources = searchResult,
                    ProcessedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in Bing search for learning resources: {SearchQuery}", searchQuery);
                
                return new SkillLearningSearchResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    SearchQuery = searchQuery,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Search using Azure AI Agents with Bing Grounding specifically for learning resources
        /// </summary>
        private async Task<SkillLearningResources> BingGroundingSearchAsync(string searchQuery)
        {
            _logger.LogInformation("🔧 Attempting Bing Grounding Search with Azure AI Agents for learning resources");

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
                name: "skills-learning-agent",
                instructions: "You are a specialized educational resource finder for skill development. Search for comprehensive learning materials including online courses, tutorials, books, videos, practice resources, and educational websites. Focus on practical learning resources that help people develop new skills or improve existing ones.",
                tools: [bingGroundingTool]
            );

            // Step 3: Create a thread and run
            var thread = await agentClient.Threads.CreateThreadAsync();

            var enhancementPrompt = $"""
                🎓 **Especialista en Recursos de Aprendizaje para Desarrollo de Habilidades**
                
                Busca recursos educativos completos para aprender esta habilidad o tema.
                
                **TEMA DE APRENDIZAJE:**
                {searchQuery}
                
                **RECURSOS QUE NECESITO ENCONTRAR:**
                📚 Cursos online (Coursera, Udemy, edX, Khan Academy, etc.)
                📖 Libros y ebooks recomendados
                🎥 Videos tutoriales y conferencias
                🔗 Sitios web educativos especializados
                📄 Documentación técnica y guías
                🛠️ Herramientas de práctica y ejercicios
                🏆 Certificaciones disponibles
                👥 Comunidades y foros de aprendizaje

                **INSTRUCCIONES:**
                Busca en internet y proporciona una respuesta en JSON con esta estructura exacta:

                "topicoAprendizaje": "Nombre del tema o habilidad",
                "cursosOnline": [
                  "titulo": "Nombre del curso",
                  "instructor": "Nombre del instructor",
                  "plataforma": "Coursera/Udemy/edX/etc.",
                  "url": "https://ejemplo.com/curso",
                  "precio": "Gratuito/$99/etc.",
                  "duracion": "4 semanas/20 horas",
                  "nivel": "Principiante/Intermedio/Avanzado",
                  "idioma": "Español/Inglés",
                  "certificacion": "Sí/No",
                  "descripcion": "Qué aprenderás en este curso"
                ],
                "librosRecomendados": [
                  "titulo": "Título del libro",
                  "autor": "Nombre del autor",
                  "url": "https://amazon.com/libro o sitio donde comprarlo",
                  "precio": "$25/Gratuito",
                  "formato": "Físico/Digital/Ambos",
                  "año": "2023",
                  "descripcion": "De qué trata el libro y por qué es útil"
                ],
                "videosTutoriales": [
                  "titulo": "Título del video",
                  "canal": "Nombre del canal de YouTube",
                  "url": "https://youtube.com/watch?v=...",
                  "duracion": "15 minutos",
                  "nivel": "Principiante/Intermedio/Avanzado",
                  "descripcion": "Qué enseña el video"
                ],
                "sitiosEducativos": [
                  "nombre": "Nombre del sitio",
                  "url": "https://sitio.com",
                  "tipo": "Documentación/Tutorial/Blog/Wiki",
                  "acceso": "Gratuito/Premium",
                  "descripcion": "Qué tipo de contenido ofrece"
                ],
                "herramientasPractica": [
                  "nombre": "Nombre de la herramienta",
                  "url": "https://herramienta.com",
                  "tipo": "Ejercicios/Simulador/IDE/Plataforma",
                  "acceso": "Gratuito/Freemium/Pago",
                  "descripcion": "Cómo ayuda a practicar la habilidad"
                ],
                "certificaciones": [
                  "nombre": "Nombre de la certificación",
                  "organizacion": "Quién la otorga",
                  "url": "https://certificacion.com",
                  "costo": "$200/Gratuito",
                  "validez": "2 años/Permanente",
                  "requisitos": "Qué se necesita para obtenerla"
                ],
                "comunidades": [
                  "nombre": "Nombre de la comunidad",
                  "plataforma": "Reddit/Discord/Stack Overflow/LinkedIn",
                  "url": "https://comunidad.com",
                  "miembros": "50K usuarios/No especificado",
                  "descripcion": "Qué tipo de ayuda y discusiones ofrece"
                ],
                "rutaAprendizaje": [
                  "paso": 1,
                  "titulo": "Fundamentos básicos",
                  "recursos": ["Curso X", "Libro Y"],
                  "tiempoEstimado": "2 semanas"
                ],
                "palabrasClave": ["palabra1", "palabra2", "habilidad"],
                "resumenGeneral": "Resumen de todos los recursos encontrados y cómo pueden ayudar a aprender esta habilidad",
                "htmlCompleto": "<div>HTML con todos los recursos organizados de forma visual y atractiva</div>"

                **CRITERIOS IMPORTANTES:**
                ✅ Busca recursos REALES que existan en internet
                ✅ Prioriza contenido de calidad y actualizado
                ✅ Incluye opciones gratuitas y de pago
                ✅ Cubre diferentes niveles (principiante a avanzado)
                ✅ Incluye recursos en español e inglés
                ✅ Encuentra herramientas prácticas para aplicar el conocimiento
                ❌ NO inventes URLs o recursos que no existan
                ❌ NO incluyas contenido desactualizado o irrelevante

                **FORMATO:**
                Responde ÚNICAMENTE con el JSON válido, sin ```json al inicio o final.
                """;

            var message = await agentClient.Messages.CreateMessageAsync(
                thread.Value.Id,
                MessageRole.User,
                $"Find comprehensive learning resources for: {searchQuery}. " +
                "Search for online courses, books, video tutorials, educational websites, practice tools, " +
                "certifications, and learning communities. Focus on practical resources that help develop this skill. " +
                "Include both free and paid options, and resources in Spanish and English." +
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
                throw new InvalidOperationException("No learning resources found in Bing Grounding search");
            }

            // Parse the JSON response
            SkillLearningResources learningResources;
            try
            {
                learningResources = JsonConvert.DeserializeObject<SkillLearningResources>(searchResults[0]);
                _logger.LogInformation("✅ Learning resources found: {CourseCount} courses, {BookCount} books, {VideoCount} videos", 
                    learningResources?.CursosOnline?.Count ?? 0,
                    learningResources?.LibrosRecomendados?.Count ?? 0,
                    learningResources?.VideosTutoriales?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to parse learning resources, creating fallback response");
                learningResources = new SkillLearningResources
                {
                    TopicoAprendizaje = searchQuery,
                    ResumenGeneral = "No se pudieron procesar los recursos de aprendizaje encontrados correctamente",
                    PalabrasClave = new List<string> { "aprendizaje", "habilidades", "recursos" }
                };
            }

            return learningResources ?? new SkillLearningResources();
        }

        /// <summary>
        /// Process a PDF document to extract all content and generate a comprehensive index
        /// with page-by-page content analysis for skill development purposes
        /// </summary>
        /// <param name="containerName">DataLake container name (twinId)</param>
        /// <param name="filePath">Path within the container</param>
        /// <param name="fileName">Document file name</param>
        /// <param name="documentType">Type of document (SKILL_DOCUMENT, LEARNING_MATERIAL, etc.)</param>
        /// <param name="category">Document category (PROGRAMMING, DESIGN, BUSINESS, etc.)</param>
        /// <param name="description">Document description</param>
        /// <returns>Structured skill document analysis with comprehensive index</returns>
        public async Task<SkillDocumentResult> ProcessSkillDocumentAsync(
            string containerName, 
            string filePath, 
            string fileName,
            string documentType = "SKILL_DOCUMENT", 
            string category = "GENERAL",
            string description = "")
        {
            _logger.LogInformation("🎯 Starting Skill Document Processing for: {FileName}", fileName);
            _logger.LogInformation("📋 Document Type: {DocumentType}, Category: {Category}", documentType, category);

            var result = new SkillDocumentResult
            {
                Success = false,
                ContainerName = containerName,
                FilePath = filePath,
                FileName = fileName,
                DocumentType = documentType,
                Category = category,
                Description = description,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // STEP 1: Generate SAS URL for Document Intelligence access
                _logger.LogInformation("📤 STEP 1: Generating SAS URL for document access...");
                
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
                _logger.LogInformation("📄 STEP 2: Extracting data with Document Intelligence...");
                
                var documentAnalysis = await _documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
                
                if (!documentAnalysis.Success)
                {
                    result.ErrorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}";
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return result;
                }

                result.TextContent = documentAnalysis.TextContent;
                result.TotalPages = documentAnalysis.TotalPages;
                _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars", 
                    documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

                // STEP 3: Process with AI to create comprehensive index
                _logger.LogInformation("🤖 STEP 3: Creating comprehensive content index with AI...");
                
                var indexResult = await GenerateDocumentIndexWithAI(documentAnalysis, documentType, category, description);
                
                if (!indexResult.Success)
                {
                    result.ErrorMessage = $"AI index generation failed: {indexResult.ErrorMessage}";
                    _logger.LogError("❌ AI index generation failed: {Error}", indexResult.ErrorMessage);
                    return result;
                }

                // Populate result with index data
                result.Success = true;
                result.DocumentIndex = indexResult;
                result.IndexSummary = indexResult.IndexSummary;
                result.PageIndexes = indexResult.PageIndexes;
                result.TopicSections = indexResult.TopicSections;
                result.LearningObjectives = indexResult.LearningObjectives;
                result.SkillMappings = indexResult.SkillMappings;

                _logger.LogInformation("✅ Skill document processing completed successfully");
                _logger.LogInformation("📊 Results: {Pages} pages indexed, {Topics} topic sections, {Skills} skill mappings", 
                    result.PageIndexes.Count, result.TopicSections.Count, result.SkillMappings.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing skill document {FileName}", fileName);
                
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Generate comprehensive document index using AI
        /// </summary>
        private async Task<SkillDocumentIndex> GenerateDocumentIndexWithAI(
            DocumentAnalysisResult documentAnalysis, 
            string documentType, 
            string category, 
            string description)
        {
            try
            {
                await InitializeKernelAsync();
                var chatCompletion = _kernel!.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var prompt = $@"
Analiza este documento de habilidades/aprendizaje y crea un índice completo y detallado que ayude a organizar el contenido para el desarrollo de habilidades.

DESCRIPCIÓN DEL DOCUMENTO: {description}
TIPO DE DOCUMENTO: {documentType}
CATEGORÍA: {category}

CONTENIDO COMPLETO DEL DOCUMENTO:
{documentAnalysis.TextContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}

INSTRUCCIONES ESPECÍFICAS PARA CREAR EL ÍNDICE:

1. **Análisis por Páginas**: Para cada página, extrae:
   - Contenido principal de la página
   - Conceptos clave y términos importantes
   - Habilidades específicas mencionadas
   - Ejemplos prácticos o ejercicios
   - Referencias a otros temas

2. **Secciones Temáticas**: Identifica y organiza el contenido en secciones lógicas:
   - Título de la sección
   - Páginas que abarca
   - Resumen del contenido
   - Nivel de dificultad (Principiante/Intermedio/Avanzado)
   - Prerrequisitos necesarios

3. **Objetivos de Aprendizaje**: Para cada sección, define:
   - Qué aprenderá el estudiante
   - Habilidades que desarrollará
   - Competencias que adquirirá
   - Aplicaciones prácticas

4. **Mapeo de Habilidades**: Relaciona el contenido con habilidades específicas:
   - Habilidades técnicas (hard skills)
   - Habilidades blandas (soft skills)
   - Nivel de competencia requerido
   - Tiempo estimado de aprendizaje

5. **Índice de Contenido**: Crea un índice navegable que incluya:
   - Estructura jerárquica del contenido
   - Palabras clave por página
   - Conexiones entre temas
   - Rutas de aprendizaje sugeridas

IMPORTANTE: Responde ÚNICAMENTE en formato JSON válido, sin markdown:

{{
    ""documentType"": ""{documentType}"",
    ""category"": ""{category}"",
    ""description"": ""{description}"",
    ""indexSummary"": ""resumen ejecutivo del contenido y estructura del documento"",
    ""totalPages"": {documentAnalysis.TotalPages},
    ""pageIndexes"": [
        {{
            ""pageNumber"": 1,
            ""title"": ""título principal de la página"",
            ""mainContent"": ""contenido principal resumido"",
            ""keyConcepts"": [""concepto1"", ""concepto2"", ""concepto3""],
            ""skillsReferenced"": [""habilidad1"", ""habilidad2""],
            ""practicalExamples"": [""ejemplo1"", ""ejemplo2""],
            ""difficultyLevel"": ""Principiante/Intermedio/Avanzado"",
            ""estimatedReadingTime"": 10,
            ""keywords"": [""palabra1"", ""palabra2"", ""palabra3""]
        }}
    ],
    ""topicSections"": [
        {{
            ""sectionNumber"": 1,
            ""title"": ""título de la sección temática"",
            ""startPage"": 1,
            ""endPage"": 5,
            ""summary"": ""resumen del contenido de la sección"",
            ""difficultyLevel"": ""Principiante"",
            ""prerequisites"": [""prerequisito1"", ""prerequisito2""],
            ""mainTopics"": [""tema1"", ""tema2"", ""tema3""],
            ""estimatedStudyTime"": 120
        }}
    ],
    ""learningObjectives"": [
        {{
            ""objectiveId"": ""OBJ001"",
            ""description"": ""descripción del objetivo de aprendizaje"",
            ""relatedPages"": [1, 2, 3],
            ""skillsToAcquire"": [""habilidad específica""],
            ""competencyLevel"": ""Básico/Intermedio/Avanzado"",
            ""assessmentCriteria"": [""criterio1"", ""criterio2""]
        }}
    ],
    ""skillMappings"": [
        {{
            ""skillName"": ""nombre de la habilidad"",
            ""skillType"": ""Technical/Soft/Mixed"",
            ""pages"": [1, 2, 3],
            ""proficiencyLevel"": ""Beginner/Intermediate/Advanced/Expert"",
            ""learningPath"": [""paso1"", ""paso2"", ""paso3""],
            ""practiceOpportunities"": [""ejercicio1"", ""proyecto1""],
            ""relatedSkills"": [""habilidad relacionada1"", ""habilidad relacionada2""]
        }}
    ],
    ""contentStructure"": {{
        ""chapters"": [
            {{
                ""chapterNumber"": 1,
                ""title"": ""título del capítulo"",
                ""pages"": [1, 2, 3],
                ""subsections"": [
                    {{
                        ""title"": ""subsección"",
                        ""pages"": [1, 2]
                    }}
                ]
            }}
        ],
        ""appendices"": [
            {{
                ""title"": ""título del apéndice"",
                ""pages"": [50, 51]
            }}
        ]
    }},
    ""navigationIndex"": [
        {{
            ""term"": ""término o concepto"",
            ""pages"": [1, 5, 10],
            ""context"": ""contexto donde aparece"",
            ""importance"": ""High/Medium/Low""
        }}
    ],
    ""learningPaths"": [
        {{
            ""pathName"": ""ruta de aprendizaje sugerida"",
            ""description"": ""descripción de la ruta"",
            ""targetAudience"": ""audiencia objetivo"",
            ""estimatedDuration"": ""tiempo estimado"",
            ""sequence"": [
                {{
                    ""step"": 1,
                    ""title"": ""título del paso"",
                    ""pages"": [1, 2, 3],
                    ""objectives"": [""objetivo del paso""]
                }}
            ]
        }}
    ]
}}
";

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

                // Parse the AI response
                var indexData = System.Text.Json.JsonSerializer.Deserialize<SkillDocumentIndex>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (indexData == null)
                {
                    throw new InvalidOperationException("Failed to deserialize AI response to skill document index");
                }

                indexData.Success = true;
                indexData.RawAIResponse = aiResponse;
                indexData.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation("✅ AI index generation completed successfully");
                _logger.LogInformation("📊 Index data: {Pages} pages, {Sections} sections, {Skills} skills", 
                    indexData.PageIndexes?.Count ?? 0, 
                    indexData.TopicSections?.Count ?? 0, 
                    indexData.SkillMappings?.Count ?? 0);

                return indexData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI index generation");
                return new SkillDocumentIndex
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }
    }

    public class SkillPostRequest
    {
        public string? id { get; set; }                    // Para actualizaciones

        public string? TwinID { get; set; }                    // Para actualizaciones
        public string Name { get; set; }                   // Nombre de la habilidad
        public string Category { get; set; }               // Categoría
        public string Level { get; set; }                  // "Principiante", "Intermedio", etc.
        public string Description { get; set; }            // Descripción
        public int ExperienceYears { get; set; }           // Años de experiencia
        public List<string> Certifications { get; set; }   // Lista de certificaciones
        public List<string> Projects { get; set; }         // Lista de proyectos
        public List<string> LearningPath { get; set; }     // Ruta de aprendizaje
        public List<string> AISuggestions { get; set; }    // Sugerencias de IA
        public List<string> Tags { get; set; }             // Etiquetas
        public string DateAdded { get; set; }              // Fecha formato "yyyy-MM-dd"
        public string LastUpdated { get; set; }            // Fecha formato "yyyy-MM-dd"
        public bool Validated { get; set; }                // Si está validada

        public List<NewLearning> WhatLearned { get; set; }
    }

    public class NewLearning
    {

        public string id { get; set; }
        public string Name { get; set; }

        public string Description { get; set; }


        public string Content { get; set; }

        public DateTime DateCreated { get; set; }

        public DateTime DateUpdated { get; set; }
    }

    // ========================================
    // LEARNING SEARCH RESPONSE MODELS
    // ========================================

    /// <summary>
    /// Result of searching for learning resources using Bing
    /// </summary>
    public class SkillLearningSearchResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public SkillLearningResources? LearningResources { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    /// <summary>
    /// Comprehensive learning resources found for a skill or topic
    /// </summary>
    public class SkillLearningResources
    {
        [JsonProperty("topicoAprendizaje")]
        public string TopicoAprendizaje { get; set; } = string.Empty;

        [JsonProperty("cursosOnline")]
        public List<CursoOnline> CursosOnline { get; set; } = new List<CursoOnline>();

        [JsonProperty("librosRecomendados")]
        public List<LibroRecomendado> LibrosRecomendados { get; set; } = new List<LibroRecomendado>();

        [JsonProperty("videosTutoriales")]
        public List<VideoTutorial> VideosTutoriales { get; set; } = new List<VideoTutorial>();

        [JsonProperty("sitiosEducativos")]
        public List<SitioEducativo> SitiosEducativos { get; set; } = new List<SitioEducativo>();

        [JsonProperty("herramientasPractica")]
        public List<HerramientaPractica> HerramientasPractica { get; set; } = new List<HerramientaPractica>();

        [JsonProperty("certificaciones")]
        public List<Certificacion> Certificaciones { get; set; } = new List<Certificacion>();

        [JsonProperty("comunidades")]
        public List<ComunidadAprendizaje> Comunidades { get; set; } = new List<ComunidadAprendizaje>();

        [JsonProperty("rutaAprendizaje")]
        public List<PasoAprendizaje> RutaAprendizaje { get; set; } = new List<PasoAprendizaje>();

        [JsonProperty("palabrasClave")]
        public List<string> PalabrasClave { get; set; } = new List<string>();

        [JsonProperty("resumenGeneral")]
        public string ResumenGeneral { get; set; } = string.Empty;

        [JsonProperty("htmlCompleto")]
        public string HtmlCompleto { get; set; } = string.Empty;
    }

    public class CursoOnline
    {
        [JsonProperty("titulo")]
        public string Titulo { get; set; } = string.Empty;

        [JsonProperty("instructor")]
        public string Instructor { get; set; } = string.Empty;

        [JsonProperty("plataforma")]
        public string Plataforma { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("precio")]
        public string Precio { get; set; } = string.Empty;

        [JsonProperty("duracion")]
        public string Duracion { get; set; } = string.Empty;

        [JsonProperty("nivel")]
        public string Nivel { get; set; } = string.Empty;

        [JsonProperty("idioma")]
        public string Idioma { get; set; } = string.Empty;

        [JsonProperty("certificacion")]
        public string Certificacion { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }

    public class LibroRecomendado
    {
        [JsonProperty("titulo")]
        public string Titulo { get; set; } = string.Empty;

        [JsonProperty("autor")]
        public string Autor { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("precio")]
        public string Precio { get; set; } = string.Empty;

        [JsonProperty("formato")]
        public string Formato { get; set; } = string.Empty;

        [JsonProperty("año")]
        public string Año { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }

    public class VideoTutorial
    {
        [JsonProperty("titulo")]
        public string Titulo { get; set; } = string.Empty;

        [JsonProperty("canal")]
        public string Canal { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("duracion")]
        public string Duracion { get; set; } = string.Empty;

        [JsonProperty("nivel")]
        public string Nivel { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }

    public class SitioEducativo
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("tipo")]
        public string Tipo { get; set; } = string.Empty;

        [JsonProperty("acceso")]
        public string Acceso { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }

    public class HerramientaPractica
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("tipo")]
        public string Tipo { get; set; } = string.Empty;

        [JsonProperty("acceso")]
        public string Acceso { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }

    public class Certificacion
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonProperty("organizacion")]
        public string Organizacion { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("costo")]
        public string Costo { get; set; } = string.Empty;

        [JsonProperty("validez")]
        public string Validez { get; set; } = string.Empty;

        [JsonProperty("requisitos")]
        public string Requisitos { get; set; } = string.Empty;
    }

    public class ComunidadAprendizaje
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonProperty("plataforma")]
        public string Plataforma { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("miembros")]
        public string Miembros { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }

    public class PasoAprendizaje
    {
        [JsonProperty("paso")]
        public int Paso { get; set; }

        [JsonProperty("titulo")]
        public string Titulo { get; set; } = string.Empty;

        [JsonProperty("recursos")]
        public List<string> Recursos { get; set; } = new List<string>();

        [JsonProperty("tiempoEstimado")]
        public string TiempoEstimado { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of skill document processing with comprehensive index
    /// </summary>
    public class SkillDocumentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? DocumentUrl { get; set; }
        public string TextContent { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public DateTime ProcessedAt { get; set; }
        
        // Index data from AI processing
        public SkillDocumentIndex? DocumentIndex { get; set; }
        public string IndexSummary { get; set; } = string.Empty;
        public List<PageIndex> PageIndexes { get; set; } = new();
        public List<TopicSection> TopicSections { get; set; } = new();
        public List<LearningObjective> LearningObjectives { get; set; } = new();
        public List<SkillMapping> SkillMappings { get; set; } = new();

        /// <summary>
        /// Get full path of the document
        /// </summary>
        public string FullPath => $"{ContainerName}/{FilePath}/{FileName}";
    }

    /// <summary>
    /// Comprehensive index of skill document content generated by AI
    /// </summary>
    public class SkillDocumentIndex
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IndexSummary { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public List<PageIndex> PageIndexes { get; set; } = new();
        public List<TopicSection> TopicSections { get; set; } = new();
        public List<LearningObjective> LearningObjectives { get; set; } = new();
        public List<SkillMapping> SkillMappings { get; set; } = new();
        public ContentStructure? ContentStructure { get; set; }
        public List<NavigationIndex> NavigationIndex { get; set; } = new();
        public List<LearningPath> LearningPaths { get; set; } = new();
        public string? RawAIResponse { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    /// <summary>
    /// Index information for a specific page
    /// </summary>
    public class PageIndex
    {
        public int PageNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string MainContent { get; set; } = string.Empty;
        public List<string> KeyConcepts { get; set; } = new();
        public List<string> SkillsReferenced { get; set; } = new();
        public List<string> PracticalExamples { get; set; } = new();
        public string DifficultyLevel { get; set; } = string.Empty;
        public int EstimatedReadingTime { get; set; }
        public List<string> Keywords { get; set; } = new();
    }

    /// <summary>
    /// Thematic section of the document
    /// </summary>
    public class TopicSection
    {
        public int SectionNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public int StartPage { get; set; }
        public int EndPage { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string DifficultyLevel { get; set; } = string.Empty;
        public List<string> Prerequisites { get; set; } = new();
        public List<string> MainTopics { get; set; } = new();
        public int EstimatedStudyTime { get; set; }
    }

    /// <summary>
    /// Learning objective defined for the document
    /// </summary>
    public class LearningObjective
    {
        public string ObjectiveId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<int> RelatedPages { get; set; } = new();
        public List<string> SkillsToAcquire { get; set; } = new();
        public string CompetencyLevel { get; set; } = string.Empty;
        public List<string> AssessmentCriteria { get; set; } = new();
    }

    /// <summary>
    /// Mapping of specific skills to document content
    /// </summary>
    public class SkillMapping
    {
        public string SkillName { get; set; } = string.Empty;
        public string SkillType { get; set; } = string.Empty; // Technical/Soft/Mixed
        public List<int> Pages { get; set; } = new();
        public string ProficiencyLevel { get; set; } = string.Empty;
        public List<string> LearningPath { get; set; } = new();
        public List<string> PracticeOpportunities { get; set; } = new();
        public List<string> RelatedSkills { get; set; } = new();
    }

    /// <summary>
    /// Structure of the document content
    /// </summary>
    public class ContentStructure
    {
        public List<Chapter> Chapters { get; set; } = new();
        public List<Appendix> Appendices { get; set; } = new();
    }

    public class Chapter
    {
        public int ChapterNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<int> Pages { get; set; } = new();
        public List<Subsection> Subsections { get; set; } = new();
    }

    public class Subsection
    {
        public string Title { get; set; } = string.Empty;
        public List<int> Pages { get; set; } = new();
    }

    public class Appendix
    {
        public string Title { get; set; } = string.Empty;
        public List<int> Pages { get; set; } = new();
    }

    /// <summary>
    /// Navigation index for quick content location
    /// </summary>
    public class NavigationIndex
    {
        public string Term { get; set; } = string.Empty;
        public List<int> Pages { get; set; } = new();
        public string Context { get; set; } = string.Empty;
        public string Importance { get; set; } = string.Empty; // High/Medium/Low
    }

    /// <summary>
    /// Suggested learning path through the document
    /// </summary>
    public class LearningPath
    {
        public string PathName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetAudience { get; set; } = string.Empty;
        public string EstimatedDuration { get; set; } = string.Empty;
        public List<LearningStep> Sequence { get; set; } = new();
    }

    public class LearningStep
    {
        public int Step { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<int> Pages { get; set; } = new();
        public List<string> Objectives { get; set; } = new();
    }
}
