using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;
using TwinFx.Services;
using TwinFx.Models;
using JsonSerializer = System.Text.Json.JsonSerializer; 

namespace TwinFx.Agents;

/// <summary>
/// Agente especializado en procesamiento AI de documentos de vehículos
/// ========================================================================
/// 
/// Este agente utiliza AI para:
/// - Extracción inteligente de índices de documentos de vehículos
/// - Análisis de contenido estructurado de documentos automotrices
/// - Generación de índices automatizados con páginas específicas
/// 
/// Author: TwinFx Project
/// Date: January 2025
/// </summary>
public class CursosAgentAI
{
    private readonly ILogger<CursosAgentAI> _logger;
    private readonly IConfiguration _configuration;
    private Kernel? _kernel;
    public AiTokrens _aiTokrens = new AiTokrens();
    private readonly CursosSearchIndex _cursoSearchIndex;

    public CursosAgentAI(  ILogger<CursosSearchIndex> loggerSearch, IConfiguration configuration)
    {
        // Create internal logger for this agent to ensure _logger is not null
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<CursosAgentAI>();

        // Also keep configuration and search index initialization
        _configuration = configuration;
        _cursoSearchIndex = new CursosSearchIndex(loggerSearch, configuration);

        
    }

     

    public async Task<List<CapituloRequest>> ProduceAiClassFromDocument(List<DocumentoTextoContenido> documentChapters)
    {
        var startTime = DateTime.UtcNow;
        List<CapituloRequest> chapterResults = new List<CapituloRequest>();

        try
        {
            // Validar entrada
            if (documentChapters == null || documentChapters.Count == 0)
            {
                _logger.LogWarning("⚠️ No document chapters provided for AI class production");
                return new List<CapituloRequest>(); }

            // Inicializar Semantic Kernel
            await InitializeKernelAsync();

            _logger.LogInformation("📚 Starting AI class production for {ChapterCount} chapters", documentChapters.Count);

            foreach (var chapter in documentChapters)
            {
                _logger.LogInformation("📖 Processing chapter: {ChapterTitle} with {TokenCount} tokens", 
                    chapter.TituloCapitulo, chapter.TotalTokens);

                // 1) Extraer todo el texto del capítulo en un solo string
                string totalText = string.Join(" ", chapter.LineasExtraidas);

                // 2) Crear prompt para generar contenido educativo
                var educationPrompt = $@"
Eres un profesor experto que crea contenido educativo de alta calidad. Analiza este capítulo y genera contenido educativo completo.

CAPÍTULO A ANALIZAR:
===================
Título: {chapter.TituloCapitulo}
Páginas: {chapter.PaginaDe} - {chapter.PaginaA}

CONTENIDO COMPLETO DEL CAPÍTULO:
=============================
{totalText}

INSTRUCCIONES PARA GENERAR CONTENIDO EDUCATIVO:
=============================================

Vas a crear contenido educativo completo para este capítulo que incluya TODOS los campos necesarios para actualizar una clase CapituloRequest.

Los campos adicionales que debes generar son:
- Resumen Ejecutivo del capítulo  
- Explicación del profesor en texto plano para conversión a voz
- Explicación del profesor en HTML profesional con colores
- Quiz educativo basado SOLO en el contenido del capítulo
- Ejemplos prácticos relacionados al contenido

FORMATO DE RESPUESTA JSON (sin ```json):
{{
  ""titulo"": ""{chapter.TituloCapitulo}"",
  ""descripcion"": ""Descripción detallada del contenido de este capítulo"",
  ""numeroCapitulo"": 1,
  ""duracionMinutos"": ""Duración estimada de estudio en minutos colo numeros ejemplo 3"",
  ""transcript"": ""Contenido principal del capítulo organizado"",
  ""notas"": ""Notas importantes del capítulo"",
  ""comentarios"": ""Comentarios educativos sobre el capítulo"",
  ""puntuacion"": 4,
  ""tags"": [""tag1"", ""tag2"", ""tag3""],
  ""resumenEjecutivo"": ""Resumen completo del capítulo con puntos clave"",
  ""explicacionProfesorTexto"": ""Hola alumnos, en este capítulo estamos viendo... [explicación detallada como profesor para conversión a voz]"",
  ""explicacionProfesorHTML"": ""<h2 style='color: #2E8B57;'>Hola alumnos, en este capítulo estamos viendo...</h2> [explicación detallada como profesor con HTML profesional y colores]"",
  ""quiz"": [
    {{
      ""pregunta"": ""¿Cuál es el concepto principal de...?"",
      ""opciones"": [""A) Opción 1"", ""B) Opción 2"", ""C) Opción 3"", ""D) Opción 4""],
      ""respuestaCorrecta"": ""A"",
      ""explicacion"": ""Explicación de por qué esta respuesta es correcta""
    }}
  ],
  ""ejemplos"": [
    {{
      ""titulo"": ""Ejemplo 1: Concepto Práctico"",
      ""descripcion"": ""Descripción del ejemplo"",
      ""aplicacion"": ""Cómo aplicar este concepto""
    }}
  ]
}}

REGLAS CRÍTICAS PARA GENERAR EL CONTENIDO:
========================================

📚 **Para el Quiz:**
- Genera SOLO preguntas basadas en el texto del capítulo proporcionado
- NO inventes preguntas de tu conocimiento general del tema
- Si el texto es pequeño, genera menos preguntas (mínimo 3, máximo 20)
- Las preguntas deben ser específicas del contenido real del capítulo

🎨 **Para la explicación HTML:**
- Usa colores educativos profesionales (#2E8B57, #4169E1, #FF6347)
- Incluye títulos, subtítulos con estilos
- Que se vea muy profesional y atractivo

🗣️ **Para la explicación de texto:**
- Versión limpia sin HTML para conversión posterior a voz
- Lenguaje natural y fluido como si fuera un profesor real

📊 **Para todos los campos:**
- Basa TODO el contenido en el texto del capítulo proporcionado
- NO inventes información que no esté en el texto
- Sé específico y preciso con el contenido real del capítulo

IMPORTANTE: 
- No uses ```json al inicio o final
- Todos los campos deben coincidir con la estructura de la clase CapituloRequest
- El contenido debe ser educativo, profesional y motivacional
- Todo en español";

                var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(educationPrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 6000, // Aumentado para contenido educativo completo
                        ["temperature"] = 0.7 // Temperatura media para creatividad educativa
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var aiResponse = response.Content ?? "{}";
                
                // Limpiar respuesta
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                CapituloRequest Chapter = JsonConvert.DeserializeObject<CapituloRequest>(aiResponse);
                // Crear resultado del capítulo
                Chapter.TotalTokens = _aiTokrens.GetTokenCount(Chapter.Transcript);
                Chapter.Transcript = totalText;

                // 📚 INDEXAR CAPÍTULO EN SEARCH INDEX document-capitulos
                try
                {
                    _logger.LogInformation("📚 Indexing chapter in document-capitulos search index: {ChapterTitle}", Chapter.Titulo);
                    
                    // Crear instancia del CursosSearchIndex
                    var cursosSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosSearchIndex>();
                    var cursosSearchIndex = new CursosSearchIndex(cursosSearchLogger, _configuration);

                    // Convertir CapituloRequest a CapituloSearchRequest para indexación
                    var capituloSearchRequest = new CapituloSearchRequest
                    {
                        TotalTokens = Chapter.TotalTokens,
                        Titulo = Chapter.Titulo ?? "",
                        Descripcion = Chapter.Descripcion,
                        NumeroCapitulo = Chapter.NumeroCapitulo,
                        Transcript = Chapter.Transcript,
                        Notas = Chapter.Notas,
                        Comentarios = Chapter.Comentarios,
                        DuracionMinutos = Chapter.DuracionMinutos,
                        Tags = Chapter.Tags,
                        Puntuacion = Chapter.Puntuacion,
                        CursoId = Chapter.CursoId ?? "",
                        TwinId = Chapter.TwinId ?? "",
                        DocumentId = $"doc-{Chapter.CursoId}-{DateTime.UtcNow:yyyyMMdd}", // Generar DocumentId basado en curso y fecha
                        Completado = Chapter.Completado,
                        ResumenEjecutivo = Chapter.ResumenEjecutivo,
                        ExplicacionProfesorTexto = Chapter.ExplicacionProfesorTexto,
                        ExplicacionProfesorHTML = Chapter.ExplicacionProfesorHTML
                    };

                    // Indexar el capítulo en el índice de búsqueda
                    var indexResult = await cursosSearchIndex.IndexChapterAnalysisAsync(capituloSearchRequest, 0.0);

                    if (indexResult.Success)
                    {
                        _logger.LogInformation("✅ Chapter indexed successfully in document-capitulos: DocumentId={DocumentId}", indexResult.DocumentId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to index chapter in document-capitulos: {Error}", indexResult.Error);
                    }
                }
                catch (Exception indexEx)
                {
                    _logger.LogWarning(indexEx, "⚠️ Failed to index chapter in document-capitulos search index, continuing with main flow");
                    // No fallamos toda la operación por esto
                }

                chapterResults.Add(Chapter);

                _logger.LogInformation("✅ Chapter processed successfully: {ChapterTitle}", chapter.TituloCapitulo);
            }

            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Resultado final
            var finalResult = new
            {
                success = true,
                totalChapters = documentChapters.Count,
                chapters = chapterResults,
                processingTimeMs = processingTimeMs,
                completedAt = DateTime.UtcNow
            };

            _logger.LogInformation("✅ AI class production completed successfully in {ProcessingTime}ms for {ChapterCount} chapters", 
                processingTimeMs, documentChapters.Count);

            return chapterResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during AI class production");
            
            // Respuesta de error en formato JSON
            var errorResponse = new
            {
                success = false,
                errorMessage = ex.Message,
                totalChapters = documentChapters?.Count ?? 0,
                processedChapters = chapterResults.Count,
                extractedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            };

            return new List<CapituloRequest>();
        }
    }

    public async Task<List<CapituloRequest>> ExtarctDataFromClass(string IndexData, List<DocumentPage> documentPages)
    {
        try
        {
            _logger.LogInformation("🚗📄 Starting text extraction based on index data");

            // Deserializar el índice JSON
            DocumentoClase documento = JsonConvert.DeserializeObject<DocumentoClase>(IndexData);
            
            if (documento?.Indice == null || documento.Indice.Count == 0)
            {
                _logger.LogWarning("⚠️ No index data found in IndexData");
                 return new List<CapituloRequest>();
            }

            var documentoContenidos = new List<DocumentoTextoContenido>();

            // Procesar cada entrada del índice
            foreach (var indice in documento.Indice)
            {
                _logger.LogInformation("📖 Processing chapter: {Titulo} (Pages {PaginaDe}-{PaginaA})", 
                    indice.Titulo, indice.PaginaDe, indice.PaginaA);
                string AllText= string.Empty;
                var contenido = new DocumentoTextoContenido
                {
                    TituloCapitulo = indice.Titulo,
                    PaginaDe = indice.PaginaDe,
                    PaginaA = indice.PaginaA,
                    LineasExtraidas = new List<string>()
                };

                // Extraer todas las líneas de las páginas especificadas
                foreach (var page in documentPages)
                {
                    if (page.PageNumber >= indice.PaginaDe && page.PageNumber <= indice.PaginaA)
                    {
                        _logger.LogDebug("📄 Extracting lines from page {PageNumber}", page.PageNumber);
                        
                        // Agregar todas las líneas de esta página
                        if (page.LinesText != null && page.LinesText.Count > 0)
                        {
                            contenido.LineasExtraidas.AddRange(page.LinesText);
                        }
                        foreach(var line in page.LinesText)
                        {
                            AllText += line + " ";
                        }
                    }
                }

                _logger.LogInformation("✅ Extracted {LineCount} lines for chapter: {Titulo}", 
                    contenido.LineasExtraidas.Count, indice.Titulo);
                int totalTokens = 0;
                totalTokens = _aiTokrens.GetTokenCount(AllText);
                contenido.TotalTokens = totalTokens;
                documentoContenidos.Add(contenido);
            }
            List<CapituloRequest> Capituloscurso = await ProduceAiClassFromDocument(documentoContenidos);


            return Capituloscurso;
            // Serializar el resultado sonSerializer.Serialize(documentoContenidos, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during text extraction from class");
            
            var errorResponse = new
            {
                success = false,
                errorMessage = ex.Message,
                processedAt = DateTime.UtcNow
            };

            return new List<CapituloRequest>();
        }
    }
    public async Task<CursoSeleccionado> BuildfullCurseWithAi(string CurseIndex)
    {

        _logger.LogInformation("🤖 Building full course with AI from index");

        try
        {
            // Validar entrada
            if (string.IsNullOrEmpty(CurseIndex))
            {
                _logger.LogWarning("⚠️ CurseIndex parameter is empty");
                return new CursoSeleccionado();
            }

            // Inicializar Semantic Kernel
            await InitializeKernelAsync();

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();

            var coursePrompt = $@"
Eres un experto educativo que crea contenido completo de cursos basado en índices existentes.
Analiza este índice de curso y genera los datos básicos del curso.

ÍNDICE DEL CURSO A ANALIZAR:
============================
{CurseIndex}

INSTRUCCIONES:
==============
Basándote ÚNICAMENTE en el índice proporcionado, crea los datos del curso.
NO inventes información que no esté relacionada con los capítulos del índice.
Enfócate en los temas que aparecen en el índice para definir el curso.

FORMATO DE RESPUESTA JSON (sin ```json):
{{
  ""nombreClase"": ""Nombre del curso basado en los títulos del índice"",
  ""instructor"": ""Twin Class AI"",
  ""plataforma"": ""AI"",
  ""categoria"": ""Categoría inferida de los temas del índice"",
  ""duracion"": ""Estimación basada en el número de capítulos"",
  ""requisitos"": ""Requisitos básicos según los temas del índice"",
  ""loQueAprendere"": ""pon en un solo texto con comas lo ue el estudianet aprendera no una lista "",
  ""precio"": ""$0"",
  ""recursos"": ""Recursos sugeridos para los temas"",
  ""idioma"": ""Spanish"",
  ""fechaInicio"": """",
  ""fechaFin"": """",
  ""objetivosdeAprendizaje"": ""Objetivos específicos basados en el contenido"",
  ""habilidadesCompetencias"": ""Habilidades que se desarrollarán"",
  ""prerequisitos"": ""Prerequisitos necesarios para el curso"",
  ""etiquetas"": ""Etiquetas relevantes basadas en los temas"",
  ""notasPersonales"": ""Notas sobre la estructura del curso"",
  ""htmlDetails"": ""Detalles en HTML profesional con colores educativos, listas, grids , indice todo el contenido explicado""
  ""textoDetails"": ""Detalles en texto plano para conversión a voz""


}}

REGLAS:
- Usa SOLO la información del índice
- NO inventes temas que no aparezcan
- Mantén coherencia con los capítulos listados
- Todo en español
- NO uses ```json al inicio o final
- Responde SOLO con JSON válido";

            chatHistory.AddUserMessage(coursePrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 2000,
                    ["temperature"] = 0.3
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "{}";
            
            // Limpiar respuesta
            aiResponse = aiResponse.Trim().Trim('`');
            if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                aiResponse = aiResponse.Substring(4).Trim();
            }

            // Convertir a CursoSeleccionado usando Newtonsoft.Json
            CursoSeleccionado cursoDetalles = JsonConvert.DeserializeObject<CursoSeleccionado>(aiResponse);

           

            // Valores por defecto si es null
            if (cursoDetalles == null)
            {
                cursoDetalles = new CursoSeleccionado();
            }
            
            // Asegurar valores por defecto
            cursoDetalles.Instructor = cursoDetalles.Instructor ?? "Twin Class AI";
            cursoDetalles.Plataforma = cursoDetalles.Plataforma ?? "AI";
            cursoDetalles.Categoria = cursoDetalles.Categoria ?? "AI Created";
            cursoDetalles.Precio = cursoDetalles.Precio ?? "$0";
            cursoDetalles.Idioma = cursoDetalles.Idioma ?? "Spanish";
            cursoDetalles.FechaInicio = cursoDetalles.FechaInicio ?? "";
            cursoDetalles.FechaFin = cursoDetalles.FechaFin ?? "";

            _logger.LogInformation("✅ Course built successfully: {CourseName}", cursoDetalles.NombreClase);
            return cursoDetalles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error building course with AI");
            
            // Retornar curso básico en caso de error
            return new CursoSeleccionado
            {
                NombreClase = "Curso generado automáticamente",
                Instructor = "Twin Class AI",
                Plataforma = "AI",
                Categoria = "AI Created",
                Duracion = "Por definir",
                Precio = "$0",
                Idioma = "Spanish",
                FechaInicio = "",
                FechaFin = "",
                LoQueAprendere = "Contenido basado en el índice proporcionado",
                ObjetivosdeAprendizaje = "Objetivos educativos",
                HabilidadesCompetencias = "Habilidades prácticas",
                Requisitos = "Conocimientos básicos",
                Prerequisitos = "Ninguno específico",
                Recursos = "Recursos educativos",
                Etiquetas = "educación, curso, AI",
                NotasPersonales = $"Error: {ex.Message}"
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
            _logger.LogInformation("🔧 Initializing Semantic Kernel for CarsAgentAI");

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

            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized successfully for CarsAgentAI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for CarsAgentAI");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Responde preguntas específicas sobre un curso usando AI
    /// </summary>
    /// <param name="twinCursosAIInfo">Información de la pregunta sobre el curso</param>
    /// <returns>Respuesta inteligente basada en el contenido del curso</returns>
    public async Task<TwinCursosAIInfo> AnswerCourseQuestionAsync(TwinCursosAIInfo twinCursosAIInfo)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation("🤖 Starting AI course question answering for Twin ID: {TwinId}, Course ID: {CursoId}", 
                twinCursosAIInfo.TwinId, twinCursosAIInfo.CursoId);
            
            // Inicializar Semantic Kernel
            await InitializeKernelAsync();
            
            // PASO 1: Obtener el curso completo desde CosmosDB
            _logger.LogInformation("📚 STEP 1: Retrieving complete course from database...");
            
            // Crear configuración directa para evitar problemas de tipos
            var cosmosConfig = new LocalCosmosDbSettings
            {
                Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
                Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
                DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            };

            // Crear el servicio manualmente
            var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosCosmosDbService>();
            var cursosService = CreateCursosService(cosmosConfig);
            
            var cursos = await cursosService.GetCursosAIByTwinIdAndIDAsync(twinCursosAIInfo.TwinId, twinCursosAIInfo.CursoId);
            
            if (cursos == null || cursos.Count == 0)
            {
                _logger.LogWarning("⚠️ Course not found: TwinId={TwinId}, CursoId={CursoId}", 
                    twinCursosAIInfo.TwinId, twinCursosAIInfo.CursoId);
                
                twinCursosAIInfo.Answer = "Lo siento, no pude encontrar el curso especificado en tu perfil educativo. Por favor verifica el ID del curso.";
                twinCursosAIInfo.Context = "Curso no encontrado en la base de datos";
                return twinCursosAIInfo;
            }
            
            var curso = cursos.First();
            _logger.LogInformation("✅ Course retrieved: {CourseName} with {ChapterCount} chapters", 
                curso.NombreClase, curso.Capitulos?.Count ?? 0);
            
            // PASO 2: Preparar el contexto completo del curso para AI
            _logger.LogInformation("🎓 STEP 2: Preparing complete course context for AI analysis...");
            
            var courseContext = PrepareCompleteCourseContext(curso);
            
            // PASO 3: Generar respuesta inteligente usando AI
            _logger.LogInformation("🤖 STEP 3: Generating intelligent response with AI...");
            
            var aiAnswer = await GenerateIntelligentCourseAnswer(
                twinCursosAIInfo.Question, 
                courseContext, 
                curso,
                twinCursosAIInfo.TwinId);
            
            twinCursosAIInfo.Answer = aiAnswer.Answer;
            twinCursosAIInfo.Context = aiAnswer.Context;
            
            var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("✅ Course question answered successfully in {ProcessingTime}ms", processingTimeMs);
            
            return twinCursosAIInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error answering course question for Twin: {TwinId}, Course: {CursoId}", 
                twinCursosAIInfo.TwinId, twinCursosAIInfo.CursoId);
            
            twinCursosAIInfo.Answer = "Lo siento, ocurrió un error al procesar tu pregunta sobre el curso. Por favor intenta nuevamente.";
            twinCursosAIInfo.Context = $"Error: {ex.Message}";
            return twinCursosAIInfo;
        }
    }
    
    /// <summary>
    /// Prepara el contexto completo del curso para análisis AI
    /// </summary>
    private string PrepareCompleteCourseContext(CursoSeleccionado curso)
    {
        var context = new StringBuilder();
        
        context.AppendLine("=== INFORMACIÓN COMPLETA DEL CURSO ===");
        context.AppendLine($"Nombre del Curso: {curso.NombreClase}");
        context.AppendLine($"Instructor: {curso.Instructor}");
        context.AppendLine($"Plataforma: {curso.Plataforma}");
        context.AppendLine($"Categoría: {curso.Categoria}");
        context.AppendLine($"Duración: {curso.Duracion}");
        context.AppendLine($"Idioma: {curso.Idioma}");
        context.AppendLine($"Precio: {curso.Precio}");
        context.AppendLine();
        
        context.AppendLine("=== OBJETIVOS Y CONTENIDO ===");
        context.AppendLine($"Lo que aprenderé: {curso.LoQueAprendere}");
        context.AppendLine($"Objetivos de Aprendizaje: {curso.ObjetivosdeAprendizaje}");
        context.AppendLine($"Habilidades y Competencias: {curso.HabilidadesCompetencias}");
        context.AppendLine($"Requisitos: {curso.Requisitos}");
        context.AppendLine($"Prerequisitos: {curso.Prerequisitos}");
        context.AppendLine($"Recursos: {curso.Recursos}");
        context.AppendLine();
        
        context.AppendLine("=== INFORMACIÓN PERSONAL ===");
        context.AppendLine($"Etiquetas: {curso.Etiquetas}");
        context.AppendLine($"Notas Personales: {curso.NotasPersonales}");
        context.AppendLine();
        
        // PASO CRÍTICO: Agregar solo Título y Transcript de cada capítulo para ahorrar tokens
        if (curso.Capitulos != null && curso.Capitulos.Count > 0)
        {
            context.AppendLine("=== CONTENIDO DE CAPÍTULOS (TÍTULO Y TRANSCRIPT) ===");
            context.AppendLine($"Total de Capítulos: {curso.Capitulos.Count}");
            context.AppendLine();
            
            foreach (var capitulo in curso.Capitulos)
            {
                context.AppendLine($"--- CAPÍTULO {capitulo.NumeroCapitulo} ---");
                context.AppendLine($"Título: {capitulo.Titulo}");
                
                // Solo incluir transcript (contenido principal) para ahorrar tokens
                if (!string.IsNullOrEmpty(capitulo.Transcript))
                {
                    context.AppendLine("Contenido:");
                    context.AppendLine(capitulo.Transcript);
                }
                
                context.AppendLine(); // Separador entre capítulos
            }
        }
        else
        {
            context.AppendLine("=== SIN CAPÍTULOS DISPONIBLES ===");
            context.AppendLine("Este curso no tiene capítulos detallados disponibles.");
        }
        
        _logger.LogInformation("📄 Course context prepared: {ContextLength} characters, {ChapterCount} chapters", 
            context.Length, curso.Capitulos?.Count ?? 0);
        
        return context.ToString();
    }
    
    /// <summary>
    /// Genera respuesta inteligente usando AI con el contexto completo del curso
    /// </summary>
    private async Task<(string Answer, string Context)> GenerateIntelligentCourseAnswer(
        string question, 
        string courseContext, 
        CursoSeleccionado curso,
        string twinId)
    {
        try
        {
            AiTokrens tokens = new AiTokrens();
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            int TotalTokens = tokens.GetTokenCount(courseContext);
            var searchQuery = new SearchQuery
            {
                SearchText = question,
                TwinId = twinId,
                UseVectorSearch = true,  // Usar búsqueda vectorial
                UseSemanticSearch = false, // Usar búsqueda semántica 
                UseHybridSearch = false,  // Combinación de ambas
                Top = 5,  // Obtener top 5 resultados más relevantes
                Page = 1,
                SuccessfulOnly = true  // Solo entradas procesadas exitosamente
            };

            CursoSearchResult SearchCurso = new CursoSearchResult();
            if ( TotalTokens > 1000)
            {
                // Recortar el contexto si es demasiado grande
                courseContext = courseContext.Substring(0, 8000);
                SearchCurso = await _cursoSearchIndex.SearchCursoAsync(searchQuery);

                foreach(var item in SearchCurso.Results)
                {
                    courseContext += item.Transcript + "\n";
                }
                _logger.LogWarning("⚠️ Course context too large, truncated to 8000 characters");
                
            }


            var expertPrompt = $@"
Eres un Twin experto en educación y análisis de cursos. Tu especialidad es ayudar a estudiantes con preguntas específicas sobre sus cursos seleccionados.

CONTEXTO COMPLETO DEL CURSO:
===========================
{courseContext}

PREGUNTA DEL ESTUDIANTE:
=======================
{question}

INSTRUCCIONES PARA TU RESPUESTA:
===============================

🎓 **Tu Rol:**
- Eres un Twin experto educativo especializado en este curso específico
- Conoces TODO el contenido del curso y sus capítulos
- Tu objetivo es proporcionar respuestas precisas y educativas

📚 **Reglas para Responder:**
- USA ÚNICAMENTE la información del curso proporcionado arriba
- NO inventes información que no esté en el contexto del curso
- Si la pregunta no puede ser respondida con la información disponible, dilo claramente
- Cita capítulos específicos cuando sea relevante
- Sé específico y preciso con referencias al contenido real

🎯 **Formato de Respuesta:**
- Respuesta clara y educativa en español
- Referencias específicas a capítulos cuando sea apropiado
- Ejemplos del contenido real del curso si están disponibles
- Sugerencias prácticas basadas en el material del curso

✅ **Ejemplos de Buenas Respuestas:**
- ""Según el Capítulo 3 sobre [tema], el concepto se explica como...""
- ""En el contenido del curso se menciona que..."":
- ""Basándome en los objetivos de aprendizaje del curso...""

❌ **Evita:**
- Información general de internet
- Conceptos que no estén en el curso
- Respuestas vagas sin referencias al contenido

Tu respuesta:
QUiero que respondas en formato HTML con colores, ecuaciones bien puestas, texto con fonts 
de tamanos diferentes, con titulos, etc. grids, listas

Usa HTML en tu respuesta pero no muestres el ```html por que dana el view

Eres experto solo en este tema no respondas de nungun otro tema eres el Twin de cursos 

IMPORTANTE: Tu respuesta debe demostrar que conoces específicamente ESTE curso y su contenido, no conocimiento general del tema.
NOTA IMPORTANTE: NO inventes capitulos quen o existen ni datos que no existen responde basado en lo ue te di solamente
Responde la pregunta del estudiante ahora:";

            chatHistory.AddUserMessage(expertPrompt);
            
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 3000,
                    ["temperature"] = 0.3 // Temperatura baja para precisión
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiAnswer = response.Content ?? "No pude generar una respuesta adecuada.";
            
            // Preparar contexto de respuesta
            var responseContext = $"Curso: {curso.NombreClase} | Twin ID: {twinId} | Capítulos: {curso.Capitulos?.Count ?? 0} | Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            
            _logger.LogInformation("✅ AI answer generated successfully for course question");
            
            return (aiAnswer, responseContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating AI answer for course question");
            
            return (
                "Lo siento, ocurrió un error al procesar tu pregunta. Como Twin experto en tu curso, te recomiendo intentar reformular la pregunta o contactar al soporte técnico.",
                $"Error en procesamiento AI: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Crea una instancia de CursosCosmosDbService con configuración manual
    /// </summary>
    private CursosCosmosDbService CreateCursosService(LocalCosmosDbSettings cosmosConfig)
    {
        // Crear configuración compatible usando el approach del Functions
        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
        {
            Endpoint = cosmosConfig.Endpoint,
            Key = cosmosConfig.Key,
            DatabaseName = cosmosConfig.DatabaseName
        });

        var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosCosmosDbService>();
        return new CursosCosmosDbService(serviceLogger, cosmosOptions);
    }
}

/// <summary>
/// Clase que contiene el texto extraído de un capítulo basado en el índice
/// </summary>
public class DocumentoTextoContenido
{
    /// <summary>
    /// Título del capítulo del índice
    /// </summary>
    public string TituloCapitulo { get; set; } = string.Empty;

    /// <summary>
    /// Página de inicio del capítulo
    /// </summary>
    public int PaginaDe { get; set; }

    /// <summary>
    /// Página de fin del capítulo
    /// </summary>
    public int PaginaA { get; set; }

    /// <summary>
    /// Lista de todas las líneas extraídas de las páginas del capítulo
    /// </summary>
    public List<string> LineasExtraidas { get; set; } = new List<string>();

    public int TotalTokens { get; set; } = 0;
}

public class Indice
{
    public string Titulo { get; set; }
    public int PaginaDe { get; set; }
    public int PaginaA { get; set; }
}

public class DocumentoClase
{
    public List<Indice> Indice { get; set; }
}

public class TwinCursosAIInfo
{
    public string CursoId { get; set; }
    public string TwinId { get; set; }
    public string CapituloId { get; set; }
    public string Question { get; set; }

    public string Context { get; set; }

    public string Answer { get; set; }

    

}

/// <summary>
/// Configuración de Azure Cosmos DB para CursosAgentAI
/// </summary>
public class LocalCosmosDbSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}