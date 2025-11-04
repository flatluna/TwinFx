using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Agents
{
     public class CursosAiBuilder
    {
        private Kernel? _kernel;

        private readonly ILogger<AgenteHomes> _logger;
        private readonly IConfiguration _configuration;
        private ILogger<BingSearch>? _loggerSearch;

        public CursosAiBuilder(
            ILogger<AgenteHomes> logger,
            IConfiguration configuration
             )
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _loggerSearch = loggerFactory.CreateLogger<BingSearch>();

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _logger.LogInformation("?? CursosAiBuilder initialized for course generation");
        }
        /// <summary>
        /// Calls the provided Semantic Kernel chat completion service to generate a detailed course
        /// using the builder's properties. Returns the raw AI response string.
        /// </summary>
        public async Task<CursoCreadoAI> BuildCursoAsync(CursoBuildData buildData, string TwinID, string type)
        {
            if (buildData == null) throw new ArgumentNullException(nameof(buildData));

            try
            {
                // Map build data into this builder instance
                this.NombreClase = buildData.NombreClase ?? string.Empty;
                this.Descripcion = buildData.Descripcion ?? string.Empty;
                this.CantidadCapitulos = buildData.CantidadCapitulos;
                this.CantidadPaginas = buildData.CantidadPaginas;
                this.ListaTopicos = buildData.ListaTopicos ?? new List<string>();
                this.Idioma = buildData.Idioma ?? "es";

                // Create kernel builder using environment variables (keeps this class standalone)
                IKernelBuilder builder = Kernel.CreateBuilder();

                // IMPORTANT: Configure HttpClient with extended timeouts for gpt-5-mini (large content generation)
                builder.Services.AddHttpClient("SemanticKernelClient")
                    .SetHandlerLifetime(TimeSpan.FromMinutes(15)) // Extended handler lifetime
                    .ConfigureHttpClient(client =>
                    {
                        client.Timeout = TimeSpan.FromMinutes(15); // Extended timeout for course generation (15 minutes)
                    });

                string deployment = "";
                if(type == "gpt5")
                {
                    deployment = "gpt-5-mini";
                }
                else
                {
                    deployment = "gpt4mini";
                }

                var endpoint = Environment.GetEnvironmentVariable("AzureOpenAI:Endpoint") ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                var apiKey = Environment.GetEnvironmentVariable("AzureOpenAI:ApiKey") ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
                 
                CursoCreadoAI CursoSeCreo = new CursoCreadoAI();
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint or api key not configured in environment variables");
                }
                try
                {


                    // Configure Azure OpenAI with custom HttpClient settings for long-running operations
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: deployment,
                        endpoint: endpoint,
                        apiKey: apiKey,
                        serviceId: "course-generator-openai",
                        httpClient: CreateLongRunningHttpClient());

                    var kernel = builder.Build();
                     var Capitulos = await GenerateChapterStructureAsync(kernel);
                     CursoSeCreo = await RunCursoAsync((Kernel)kernel, Capitulos);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to build kernel or generate course");
                    throw;
                }
                CursoSeCreo.TwinID = TwinID;
                CursoSeCreo.id = Guid.NewGuid().ToString();
                await SaveCursoBuildAsync(CursoSeCreo);
                await IndexCursoToCapitulosAIAsync(CursoSeCreo);
                return CursoSeCreo;
            }
            catch (Exception ex)
            {
                // Return a simple JSON error structure to avoid breaking callers
                var error = new { success = false, error = ex.Message };
                return  new CursoCreadoAI
                {
                    NombreClase = "Error",
                    Descripcion = JsonConvert.SerializeObject(error)
                };
            }
        }


        public async Task<TwinCursosAIInfo> AnswerCourseCapituloQuestionAsync(string CursoID,
            int CapituloID, string TwinID, string Question )
        {
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("🤖 Starting AI chapter question answering for Twin ID: {TwinId}, Course ID: {CursoId}, Chapter ID: {CapituloId}", 
                    TwinID, CursoID, CapituloID);

                // Inicializar Semantic Kernel
                await InitializeKernelAsync();

                // PASO 1: Obtener el curso completo desde CosmosDB
                _logger.LogInformation("📚 STEP 1: Retrieving complete course from database...");
                
                var cosmosSettings = Microsoft.Extensions.Options.Options.Create(new TwinFx.Models.CosmosDbSettings
                {
                    Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? string.Empty,
                    Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? string.Empty,
                    DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
                });

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var serviceLogger = loggerFactory.CreateLogger<TwinFx.Services.CursosAiBuildCosmosDB>();
                var cursosService = new TwinFx.Services.CursosAiBuildCosmosDB(serviceLogger, cosmosSettings);

                // Obtener el curso específico por TwinID y CursoID
                var curso = await cursosService.GetCursosByTwinIdAndIDAsync(TwinID, CursoID);
                
                if (curso == null)
                {
                    _logger.LogWarning("⚠️ Course not found: TwinId={TwinId}, CursoId={CursoId}", TwinID, CursoID);
                    
                    return new TwinCursosAIInfo
                    {
                        TwinId = TwinID,
                        CursoId = CursoID,
                        CapituloId = CapituloID,
                        Question = Question,
                        Answer = "Lo siento, no pude encontrar el curso especificado en tu perfil educativo. Por favor verifica el ID del curso.",
                        Context = "Curso no encontrado en la base de datos"
                    };
                }

                _logger.LogInformation("✅ Course retrieved: {CourseName} with {ChapterCount} chapters", 
                    curso.NombreClase, curso.Capitulos?.Count ?? 0);

                // PASO 2: Buscar el capítulo específico por ID
                _logger.LogInformation("📖 STEP 2: Finding specific chapter by ID...");
                
                CapituloCreadoAI? capitulo = null;
                if (curso.Capitulos != null)
                {
                    capitulo = curso.Capitulos.FirstOrDefault(cap => cap.id == CapituloID.ToString());
                }

                if (capitulo == null)
                {
                    _logger.LogWarning("⚠️ Chapter not found: CapituloId={CapituloId} in Course={CursoId}", CapituloID, CursoID);
                    
                    return new TwinCursosAIInfo
                    {
                        TwinId = TwinID,
                        CursoId = CursoID,
                        CapituloId = CapituloID,
                        Question = Question,
                        Answer = "Lo siento, no pude encontrar el capítulo especificado en este curso. Por favor verifica el ID del capítulo.",
                        Context = "Capítulo no encontrado en el curso"
                    };
                }

                _logger.LogInformation("✅ Chapter found: {ChapterTitle}", capitulo.Titulo);

                // PASO 3: Generar respuesta inteligente usando OpenAI
                _logger.LogInformation("🤖 STEP 3: Generating intelligent response with OpenAI...");
                
                var aiAnswer = await GenerateChapterAnswerAsync(Question, curso, capitulo, TwinID);

                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("✅ Chapter question answered successfully in {ProcessingTime}ms", processingTime);

                return new TwinCursosAIInfo
                {
                    TwinId = TwinID,
                    CursoId = CursoID,
                    CapituloId = CapituloID,
                    Question = Question,
                    Answer = aiAnswer.Answer,
                    Context = aiAnswer.Context
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error answering chapter question for Twin: {TwinId}, Course: {CursoId}, Chapter: {CapituloId}", 
                    TwinID, CursoID, CapituloID);
                
                return new TwinCursosAIInfo
                {
                    TwinId = TwinID,
                    CursoId = CursoID,
                    CapituloId = CapituloID,
                    Question = Question,
                    Answer = "Lo siento, ocurrió un error al procesar tu pregunta sobre este capítulo. Por favor intenta nuevamente.",
                    Context = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Genera respuesta inteligente sobre un capítulo específico usando OpenAI
        /// </summary>
        private async Task<(string Answer, string Context)> GenerateChapterAnswerAsync(
            string question, 
            CursoCreadoAI curso, 
            CapituloCreadoAI capitulo,
            string twinId)
        {
            try
            {
                _logger.LogInformation("🤖 Initializing OpenAI for chapter question answering...");

                var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();

                var expertPrompt = $@"
Eres un Twin experto en educación especializado en el curso ""{curso.NombreClase}"". Tu rol es responder preguntas específicas sobre los capítulos del curso con información precisa y educativa.

INFORMACIÓN COMPLETA DEL CURSO:
==============================
Nombre del Curso: {curso.NombreClase}
Descripción: {curso.Descripcion}
Idioma: {curso.Idioma}
Duración Estimada: {curso.DuracionEstimada}

CAPÍTULO ESPECÍFICO CONSULTADO:
==============================
Título del Capítulo: {capitulo.Titulo}
Página: {capitulo.Pagina}

OBJETIVOS DEL CAPÍTULO:
{string.Join("\n", capitulo.Objetivos.Select(obj => $"• {obj}"))}

CONTENIDO COMPLETO DEL CAPÍTULO:
===============================
{capitulo.Contenido}

RESUMEN DEL CAPÍTULO:
===================
{capitulo.Resumen}

EJEMPLOS DEL CAPÍTULO:
====================
{string.Join("\n", capitulo.Ejemplos.Select(ej => $"• {ej}"))}

PREGUNTA DEL ESTUDIANTE:
=======================
{question}

INSTRUCCIONES PARA TU RESPUESTA:
===============================

🎓 **Tu Rol:**
- Eres un Twin experto educativo especializado en este curso específico
- Conoces TODO el contenido del capítulo consultado
- Tu objetivo es proporcionar respuestas precisas basadas ÚNICAMENTE en el contenido del capítulo

📚 **Reglas para Responder:**
- USA ÚNICAMENTE la información del capítulo proporcionado arriba
- NO inventes información que no esté en el contenido del capítulo
- Si la pregunta no puede ser respondida con la información disponible del capítulo, dilo claramente
- Sé específico y preciso con referencias al contenido real del capítulo
- Mantén un tono educativo y profesional
- Responde con tu conocimiento que tienes sobre el tema además de la información que se le pasó

🎨 **Formato de Respuesta HTML:**
- Responde en HTML profesional con colores educativos
- Usa títulos, subtítulos, listas, y elementos visuales
- Incluye colores profesionales como #2E8B57 (verde), #4169E1 (azul), #FF6347 (naranja)
- Diseño responsive y atractivo para estudiantes
- NO incluyas las etiquetas ```html al inicio o final

✅ **Estructura Recomendada:**
- Título de la respuesta relacionado con la pregunta
- Explicación detallada basada en el contenido del capítulo
- Referencias específicas a objetivos, ejemplos o resumen cuando sea relevante
- Conclusión educativa

IMPORTANTE: 
- Responde en español (idioma: {curso.Idioma})
- Basa TODO tu conocimiento en el contenido del capítulo proporcionado
- Especifica que responde con su conocimiento especializado en el tema además de la información que se le pasó
- Especifica que tu respuesta se basa en este capítulo específico del curso
- Responde solo lo que se te pregunta. Usa la informacion del capitulo para contestar.
- Si te dicen hola , como estas preguntas genericas contesta solamente esta pregunat o comentario 
- Se breve en tus respuestas no des todo el capitulo solo contesta lo que se te pregunta
- Usa tu conocimiento para contestar pero no inventes cosas que no existen
- en caso de diagramas usa graficos, fotos, imagenes, lineas para responder. USa emojis para que quede muy profesional
- no uses urls para la imagen usa tu logica para crear charts con lineas, cuadros, circulos etc.
Responde ahora con tu conocimiento especializado en este capítulo:";

                chatHistory.AddUserMessage(expertPrompt);
                
                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_completion_tokens"] = 3000,
                        ["temperature"] = 0.3 // Temperatura baja para precisión
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var aiAnswer = response.Content ?? "No pude generar una respuesta adecuada.";
                
                // Preparar contexto de respuesta
                var responseContext = $"Curso: {curso.NombreClase} | Capítulo: {capitulo.Titulo} | Página: {capitulo.Pagina} | Twin ID: {twinId} | Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                
                _logger.LogInformation("✅ AI answer generated successfully for chapter question");
                
                return (aiAnswer, responseContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating AI answer for chapter question");
                
                return (
                    "<div style='color: #dc3545; padding: 20px; border: 1px solid #dc3545; border-radius: 5px;'><h3>Error de Procesamiento</h3><p>Lo siento, ocurrió un error al procesar tu pregunta sobre este capítulo. Como Twin experto en tu curso, te recomiendo intentar reformular la pregunta o contactar al soporte técnico.</p></div>",
                    $"Error en procesamiento AI: {ex.Message}"
                );
            }
        }
        /// <summary>
        /// Creates an HttpClient specifically configured for long-running OpenAI operations
        /// </summary>
        private static HttpClient CreateLongRunningHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // 15 minutes timeout for course generation
            return httpClient;
        }

        /// Inicializa Semantic Kernel para operaciones de AI
        /// </summary>
        private async Task InitializeKernelAsync()
        {
            if (_kernel != null)
                return; // Ya está inicializado

            try
            {
                _logger.LogInformation("?? Initializing Semantic Kernel for CursosAiBuilder");

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

                _logger.LogInformation("? Semantic Kernel initialized successfully for CursosAiBuilder");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to initialize Semantic Kernel for CursosAiBuilder");
                throw;
            }

            await Task.CompletedTask;
        }

        public string NombreClase { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int CantidadCapitulos { get; set; } = 0;
        public int CantidadPaginas { get; set; } = 0;
        public List<string> ListaTopicos { get; set; } = new();

        public string Idioma { get; set; } = "es";

        /// <summary>
        /// Calls the provided Semantic Kernel chat completion service to generate a detailed course
        /// using the builder's properties. Returns the raw AI response string.
        /// </summary>
        public async Task<CursoCreadoAI> RunCursoAsync(Kernel kernel, CapitulosSet Capitulos)
        {
            if (kernel == null) throw new ArgumentNullException(nameof(kernel));
            if (Capitulos == null) throw new ArgumentNullException(nameof(Capitulos));

            BingSearch bing = new BingSearch(_loggerSearch!, _configuration!);
            
            // Build the prompt according to the user's specification
            var topicsText = (ListaTopicos != null && ListaTopicos.Any()) ? string.Join(", ", ListaTopicos) : "";
            var SearchResult = await bing.BingSearchGlobalLearnAsync("Buscame toda la informacion possible sobr este tem " +
                " esto creando un curso en linea ayudame a enconbtrar informacion detallada el tema es " +
                "" + Descripcion + " Temas: " + topicsText + " Importante contesta con este idioma :" +
                " : " + Idioma);

            // Crear estructura base del curso
            var cursoCompleto = new CursoCreadoAI
            {
                NombreClase = this.NombreClase,
                Descripcion = this.Descripcion,
                Idioma = this.Idioma,
                DuracionEstimada = "Por definir",
                Etiquetas = new List<string> { "AI", "Generated", "Course" },
                Capitulos = new List<CapituloCreadoAI>()
            };

            _logger.LogInformation("🚀 Starting individual chapter generation for {ChapterCount} chapters", Capitulos.Capitulos.Count);

            // LOOP: Generar cada capítulo individualmente
            int paginaActual = 1;
            foreach (var capituloBasico in Capitulos.Capitulos)
            {
                _logger.LogInformation("📚 Generating content for chapter: {ChapterTitle}", capituloBasico.Titulo);

                var responsePrompt = $@"Eres un experto profesor en {NombreClase}.

CONTEXTO DEL CURSO COMPLETO:
============================
Nombre del Curso: {NombreClase}
Descripción General: {Descripcion}
Idioma: {Idioma}
Información de Internet: {SearchResult.Respuesta}

CAPÍTULO ESPECÍFICO A DESARROLLAR:
=================================
Título: {capituloBasico.Titulo}
Objetivos: {string.Join(", ", capituloBasico.Objetivos)}
Descripción: {capituloBasico.Descripcion}

INSTRUCCIONES PARA ESTE CAPÍTULO:
================================
- Este capítulo forma parte del curso completo sobre ""{Descripcion}"")
- Desarrolla COMPLETAMENTE este capítulo específico con todo detalle
- Incluye contenido extenso, ejemplos prácticos, gráficas y diagramas
- Asegúrate de que el capítulo sea MUY COMPLETO y educativo

CONTENIDO REQUERIDO:
===================
- Contenido extenso con explicaciones detalladas 
- HTML profesional con colores, grids, listas, gráficas e imágenes
- EN tu respuesta de HTML genera imagenes que muestren al estudiante lo que estas ensenando 
  ftoos imagenes, diagramas. Has el tema ocmpleto del capitulo terminalo piensa como estudiante. 
- Ejemplos prácticos específicos del tema
- Quizzes educativos (5-8 preguntas)
- Diagramas y gráficas explicativas en HTML
- Referencias a gráficos, tablas y elementos visuales

FORMATO DE RESPUESTA JSON (sin ```json):
{{
  ""Titulo"": ""{capituloBasico.Titulo}"",
  ""Objetivos"": {JsonConvert.SerializeObject(capituloBasico.Objetivos)},
  ""Contenido"": ""Contenido extenso y detallado del capítulo con explicaciones completas..."",
  ""ContenidoHTML"": ""<div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; border-radius: 10px;'><h1 style='color: white; text-align: center;'>📚 {capituloBasico.Titulo}</h1><div style='background: white; margin: 20px 0; padding: 20px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'><h2 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px;'>🎯 Objetivos de Aprendizaje</h2><ul style='list-style-type: none; padding: 0;'>[Lista de objetivos con iconos]</ul></div><div style='display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin: 20px 0;'><div style='background: #f8f9fa; padding: 15px; border-left: 4px solid #28a745; border-radius: 5px;'><h3 style='color: #28a745; margin-top: 0;'>📊 Gráfica Conceptual</h3><p>Aquí incluir descripción de gráfica o diagrama relacionado</p></div><div style='background: #f8f9fa; padding: 15px; border-left: 4px solid #dc3545; border-radius: 5px;'><h3 style='color: #dc3545; margin-top: 0;'>🔍 Ejemplo Práctico</h3><p>Ejemplo específico del tema</p></div></div></div>"",
  ""Ejemplos"": [
    ""Ejemplo 1: Aplicación práctica específica"",
    ""Ejemplo 2: Caso de estudio real"",
    ""Ejemplo 3: Ejercicio paso a paso""
  ],
  ""Resumen"": ""Resumen completo del capítulo con puntos clave aprendidos"",
  ""Pagina"": {paginaActual},
  ""Quizes"": [
    {{
      ""Pregunta"": ""Pregunta específica sobre el contenido del capítulo"",
      ""Opciones"": [""a) Opción 1"", ""b) Opción 2"", ""c) Opción 3"", ""d) Opción 4""],
      ""RespuestaCorrecta"": ""a) Opción 1"",
      ""Explicacion"": ""Explicación detallada de por qué esta respuesta es correcta""
    }}
  ]
}}

IMPORTANTE:
- El contenido debe ser EXTENSO y MUY DETALLADO
- Incluye diagramas, gráficas y elementos visuales en el HTML
- Usa colores profesionales y diseño atractivo
- Todo en {Idioma}
- NO uses ```json al inicio o final
- Asegúrate de que sea un capítulo COMPLETO y educativo";

                try
                {
                    var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                    var chatHistory = new ChatHistory();
                    chatHistory.AddUserMessage(responsePrompt);

                    var executionSettings = new PromptExecutionSettings
                    {
                        ExtensionData = new Dictionary<string, object>
                        {
                            ["max_completion_tokens"] = 8000, // Aumentado para contenido extenso 
                            ["timeout"] = TimeSpan.FromMinutes(5).TotalSeconds // 5 minutos por capítulo
                        }
                    };

                    _logger.LogInformation("🔄 Generating chapter content: {ChapterTitle}", capituloBasico.Titulo);

                    var response = await chatCompletionService.GetChatMessageContentAsync(
                        chatHistory,
                        executionSettings,
                        kernel);

                    var aiResponse = response.Content ?? "{}";
                    
                    // Limpiar respuesta
                    aiResponse = aiResponse.Trim().Trim('`');
                    if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                    {
                        aiResponse = aiResponse.Substring(4).Trim();
                    }

                    var capituloCompleto = JsonConvert.DeserializeObject<CapituloCreadoAI>(aiResponse);
                    
                    if (capituloCompleto != null)
                    {
                        string Instructions = "Read this and search for content like: Pictures, videos , images, " +
                            " documentaries, that can help learn this topic. This is the content of this curse chapter: "
                            + capituloCompleto.Contenido;

                        var SearchResultNew = await bing.BingSearchCapitulosLearnAsync(Instructions);
                        capituloCompleto.SearchCapitulo = SearchResultNew;
                        capituloCompleto.id = Guid.NewGuid().ToString();
                        cursoCompleto.Capitulos.Add(capituloCompleto);
                        _logger.LogInformation("✅ Chapter generated successfully: {ChapterTitle}", capituloBasico.Titulo);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to deserialize chapter content for: {ChapterTitle}", capituloBasico.Titulo);
                        
                        // Crear capítulo de fallback
                        var fallbackChapter = new CapituloCreadoAI
                        {
                            Titulo = capituloBasico.Titulo,
                            Objetivos = capituloBasico.Objetivos,
                            Contenido = $"Contenido del capítulo sobre {capituloBasico.Titulo}. {capituloBasico.Descripcion}",
                            ContenidoHTML = $"<h2>{capituloBasico.Titulo}</h2><p>{capituloBasico.Descripcion}</p>",
                            Ejemplos = new List<string> { "Ejemplo básico del tema" },
                            Resumen = $"Resumen del capítulo {capituloBasico.Titulo}",
                            Pagina = paginaActual,
                            Quizes = new List<PreguntaQuizAI>()
                        };

                         
                        cursoCompleto.Capitulos.Add(fallbackChapter);
                    }

                    paginaActual += 3; // Incrementar páginas por capítulo
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error generating chapter: {ChapterTitle}", capituloBasico.Titulo);
                    
                    // Agregar capítulo básico para no fallar todo el proceso
                    var errorChapter = new CapituloCreadoAI
                    {
                        Titulo = capituloBasico.Titulo,
                        Objetivos = capituloBasico.Objetivos,
                        Contenido = $"Error generando contenido para {capituloBasico.Titulo}: {ex.Message}",
                        ContenidoHTML = $"<h2>Error</h2><p>No se pudo generar contenido para este capítulo</p>",
                        Ejemplos = new List<string>(),
                        Resumen = "Error en generación",
                        Pagina = paginaActual,
                        Quizes = new List<PreguntaQuizAI>()
                    };
                    cursoCompleto.Capitulos.Add(errorChapter);
                    paginaActual += 1;
                }
            }

            // Agregar búsqueda de Bing al final
            try
            {
                string Objetivos = "";
                foreach (var capitulo in cursoCompleto.Capitulos)
                {
                    foreach (var objetivo in capitulo.Objetivos)
                    {
                        Objetivos = Objetivos + objetivo + " ";
                    }
                }

                Objetivos = " : Search for information related to this AI generated class which has this objectives " +
                      "find me a list of links to learn more about the topic : " +
                      Descripcion + "  Training Objectives : " + Objetivos +
                      " contesta con este idioma : " + Idioma;
                      
                var SearchResultNew = await bing.BingSearchAsync(Objetivos);
                cursoCompleto.CursosInternet = SearchResultNew;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Warning: Exception during Bing search for course enhancement");
            }

            _logger.LogInformation("✅ Course generation completed: {ChapterCount} chapters generated", cursoCompleto.Capitulos.Count);
            return cursoCompleto;
        }
        /// <summary>
        /// Método intermedio que genera solo la estructura básica de capítulos (nombre, objetivos, descripción genérica)
        /// SIN elaborar el contenido completo de cada capítulo
        /// </summary>
        public async Task<CapitulosSet> GenerateChapterStructureAsync(Kernel kernel)
        {
            if (kernel == null) throw new ArgumentNullException(nameof(kernel));

            var topicsText = (ListaTopicos != null && ListaTopicos.Any()) ? string.Join(", ", ListaTopicos) : "";
            
            var responsePrompt = $@"Eres un experto en {NombreClase}. 

Descripción del curso:
{Descripcion}

Idioma de respuesta: {Idioma}

Tópicos específicos a incluir: {topicsText}

INSTRUCCIONES:
- Crea EXACTAMENTE {CantidadCapitulos} capítulos basados en la descripción y tópicos
- Para cada capítulo genera SOLO:
  * Nombre del Capítulo (título descriptivo)
  * Objetivos (3-5 objetivos de aprendizaje específicos)
  * Descripción Genérica (resumen breve de qué trata el capítulo)
- NO elabores contenido detallado, ejemplos, quizzes o HTML
- NO generes texto extenso, solo la estructura básica
- Ve la descripocion y los topicos y genera los apitulos en forma secuencial siempre empieza con una Introduccion el tema
, Historia y despues lso capitulos

FORMATO DE RESPUESTA JSON (sin ```json):
{{   
  ""Capitulos"": [
    {{
      ""Titulo"": ""Nombre del Capítulo 1"",
      ""Objetivos"": [
        ""Objetivo 1 específico del capítulo"",
        ""Objetivo 2 específico del capítulo"",
        ""Objetivo 3 específico del capítulo""
      ],
      ""Descripcion"": ""Descripción genérica breve de qué trata este capítulo""
    }},
    {{
      ""Titulo"": ""Nombre del Capítulo 2"",
      ""Objetivos"": [
        ""Objetivo 1 específico del capítulo"",
        ""Objetivo 2 específico del capítulo"",
        ""Objetivo 3 específico del capítulo""
      ],
      ""Descripcion"": ""Descripción genérica breve de qué trata este capítulo""
    }}
  ]
}}

IMPORTANTE:
- Responde SOLO con JSON válido
- NO agregues contenido elaborado
- Solo estructura básica de capítulos
- Mantén todo en {Idioma}
- Genera exactamente {CantidadCapitulos} capítulos";

            try
            {
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(responsePrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_completion_tokens"] = 20000, // Limitado para estructura básica
                      
                        ["timeout"] = TimeSpan.FromMinutes(20).TotalSeconds // 2 minutos máximo
                    }
                };

                _logger.LogInformation("📋 Generating basic chapter structure for {ChapterCount} chapters", CantidadCapitulos);

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    kernel);

                var aiResponse = response.Content ?? "{}";
                
                // Limpiar respuesta
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                var capitulosEstructura = JsonConvert.DeserializeObject<CapitulosSet>(aiResponse);
                
                if (capitulosEstructura == null)
                {
                    _logger.LogError("❌ Failed to deserialize chapter structure from AI");
                    return new CapitulosSet { Capitulos = new List<CapituloBasico>() };
                }

                _logger.LogInformation("✅ Chapter structure generated: {ChapterCount} chapters created", 
                    capitulosEstructura.Capitulos?.Count ?? 0);

                return capitulosEstructura;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating chapter structure");
                return new CapitulosSet { Capitulos = new List<CapituloBasico>() };
            }
        } 
        /// <summary>
        /// Persist generated CursoBuildData into Cosmos DB (container TwinCursosAIBuild)
        /// </summary>
        private async Task SaveCursoBuildAsync(CursoCreadoAI data)
        {
            try
            {
                if (data == null)
                {
                    _logger.LogWarning("No build data to save");
                    return;
                }

                // Build cosmos settings from configuration (same pattern used elsewhere)
                var cosmosSettings = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
                {
                    Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? string.Empty,
                    Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? string.Empty,
                    DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
                });

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var serviceLogger = loggerFactory.CreateLogger<TwinFx.Services.CursosAiBuildCosmosDB>();

                var service = new TwinFx.Services.CursosAiBuildCosmosDB(serviceLogger, cosmosSettings);
                var id = await service.SaveCursoBuildAsync(data);
                if (!string.IsNullOrEmpty(id))
                {
                    _logger.LogInformation("CursoBuildData saved with id {Id}", id);
                }
                else
                {
                    _logger.LogWarning("Failed to save CursoBuildData to CosmosDB");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while saving CursoBuildData to CosmosDB");
            }
        }

        /// <summary>
        /// Index the generated course into capitulos-ai-index using CapitulosAISearchIndex
        /// </summary>
        private async Task IndexCursoToCapitulosAIAsync(CursoCreadoAI cursoCreadoAI)
        {
            try
            {
                if (cursoCreadoAI == null)
                {
                    _logger.LogWarning("📚 No course data to index");
                    return;
                }

                _logger.LogInformation("📚 Starting indexing of course '{CourseName}' into capitulos-ai-index", cursoCreadoAI.NombreClase);

                // Create CapitulosAISearchIndex instance
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var searchLogger = loggerFactory.CreateLogger<TwinFx.Services.CapitulosAISearchIndex>();
                var capitulosSearchIndex = new TwinFx.Services.CapitulosAISearchIndex(searchLogger, _configuration);

                if (!capitulosSearchIndex.IsAvailable)
                {
                    _logger.LogWarning("📚 CapitulosAISearchIndex service not available, skipping indexing");
                    return;
                }

                // Index the complete course with all chapters
                var indexResults = await capitulosSearchIndex.IndexCursoCreadoAIAsync(cursoCreadoAI);

                // Log results
                var successCount = indexResults.Count(r => r.Success);
                var failureCount = indexResults.Count(r => !r.Success);

                _logger.LogInformation("📚 Course indexing completed: {SuccessCount} chapters indexed successfully, {FailureCount} failed", 
                    successCount, failureCount);

                if (failureCount > 0)
                {
                    _logger.LogWarning("📚 Some chapters failed to index:");
                    foreach (var failedResult in indexResults.Where(r => !r.Success))
                    {
                        _logger.LogWarning("📚 Failed: {Error}", failedResult.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "📚 Exception while indexing course to capitulos-ai-index");
            }
        }
    }

    //////////////////// Clases for Data of the Class ////////
    ///


    public class CursoBuildData
    {
        [JsonPropertyName("NombreClase")]
        public string NombreClase { get; set; }

        [JsonPropertyName("Idioma")]
        public string Idioma { get; set; }

        [JsonPropertyName("Descripcion")]
        public string Descripcion { get; set; }

        [JsonPropertyName("CantidadCapitulos")]
        public int CantidadCapitulos { get; set; }

        [JsonPropertyName("CantidadPaginas")]
        public int CantidadPaginas { get; set; }

        [JsonPropertyName("ListaTopicos")]
        public List<string> ListaTopicos { get; set; } = new List<string>();

        [JsonPropertyName("TwinID")]
        public string TwinID { get; set; }

        [JsonPropertyName("id")]
        public string id { get; set; }

       
    }

    public class CursoCreadoAI
    {

        public CursoBusqueda CursosInternet { get; set; } = new CursoBusqueda();

        [JsonPropertyName("Idioma")]
        public string Idioma { get; set; }

        [JsonPropertyName("NombreClase")]
        public string NombreClase { get; set; }

        [JsonPropertyName("Descripcion")]
        public string Descripcion { get; set; }

        [JsonPropertyName("Capitulos")]
        public List<CapituloCreadoAI> Capitulos { get; set; } = new List<CapituloCreadoAI>();

        [JsonPropertyName("DuracionEstimada")]
        public string DuracionEstimada { get; set; }

        [JsonPropertyName("Etiquetas")]
        public List<string> Etiquetas { get; set; } = new List<string>();

        [JsonPropertyName("TwinID")]
        public string TwinID { get; set; }

        [JsonPropertyName("id")]
        public string id { get; set; }
    }

    public class IndexAI
    {
        [JsonPropertyName("IndexNumero")]
        public string IndexNumero { get; set; }


        [JsonPropertyName("Titulo")]
        public string Titulo { get; set; }

        [JsonPropertyName("Pagina")]
        public int Pagina { get; set; }
    }

    public class PreguntaQuizAI
    {
        [JsonPropertyName("Pregunta")]
        public string Pregunta { get; set; } = string.Empty;


        [JsonPropertyName("Opciones")]
        public List<string> Opciones { get; set; } = new List<string>();

        [JsonPropertyName("RespuestaCorrecta")]
        public string RespuestaCorrecta { get; set; } = string.Empty;

        [JsonPropertyName("Explicacion")]
        public string Explicacion { get; set; } = string.Empty;
    }

    public class CapituloCreadoAI
    {
        [JsonPropertyName("Titulo")]
        public string Titulo { get; set; }


        [JsonPropertyName("id")]
        public string id { get; set; } = Guid.NewGuid().ToString();


        [JsonPropertyName("Objetivos")]
        public List<string> Objetivos { get; set; } = new List<string>();

        [JsonPropertyName("Contenido")]
        public string Contenido { get; set; }


        [JsonPropertyName("ContenidoHTML")]
        public string ContenidoHTML { get; set; }



        [JsonPropertyName("Ejemplos")]
        public List<string> Ejemplos { get; set; } = new List<string>();

        [JsonPropertyName("Resumen")]
        public string Resumen { get; set; }

        [JsonPropertyName("Pagina")]
        public int Pagina { get; set; }

        [JsonPropertyName("Quizes")]
        public List<PreguntaQuizAI> Quizes { get; set; } = new List<PreguntaQuizAI>();

        public CursoCapituloBusqueda SearchCapitulo { get; set; } = new CursoCapituloBusqueda();
    }

    /// <summary>
    /// Clase para la estructura básica de capítulos (solo estructura, sin contenido elaborado)
    /// </summary>
    public class CapitulosSet
    {
        [JsonPropertyName("Capitulos")]
        public List<CapituloBasico> Capitulos { get; set; } = new List<CapituloBasico>();
    }

    /// <summary>
    /// Capítulo básico con solo estructura (titulo, objetivos, descripción genérica)
    /// </summary>
    public class CapituloBasico
    {
        [JsonPropertyName("Titulo")]
        public string Titulo { get; set; } = string.Empty;

        [JsonPropertyName("Objetivos")]
        public List<string> Objetivos { get; set; } = new List<string>();

        [JsonPropertyName("Descripcion")]
        public string Descripcion { get; set; } = string.Empty;
    }
    //////////////

}
