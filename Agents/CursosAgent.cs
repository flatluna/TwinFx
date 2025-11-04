using Microsoft.Azure.Cosmos.Core;
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
using TwinFx.Services;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agente especializado en gestión inteligente de cursos educativos
    /// ========================================================================
    /// 
    /// Este agente utiliza AI para:
    /// - Procesamiento inteligente de datos de cursos
    /// - Validación y enriquecimiento de información educativa
    /// - Generación de recomendaciones basadas en perfil de aprendizaje
    /// - Análisis de contenido educativo y valor del curso
    /// - Solo responde preguntas relacionadas con educación y cursos del Twin
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class CursosAgent
    {
        private readonly ILogger<CursosAgent> _logger;
        private readonly IConfiguration _configuration;
        private Kernel? _kernel;

        public CursosAgent(ILogger<CursosAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            _logger.LogInformation("📚 CursosAgent initialized for intelligent course management");
        }

        /// <summary>
        /// Procesa la creación inteligente de un nuevo curso con análisis y recomendaciones AI
        /// </summary>
        /// <param name="cursoRequest">Datos del curso a crear</param>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>Respuesta inteligente con análisis del curso y recomendaciones</returns>
        public async Task<CursoDetalles> ProcessCreateCursoAsync(CrearCursoRequest cursoRequest, string twinId)
        {
            _logger.LogInformation("📚 Processing intelligent course creation for Twin ID: {TwinId}", twinId);
            _logger.LogInformation("📚 Course: {NombreClase}", cursoRequest.Curso?.NombreClase);

            var startTime = DateTime.UtcNow;

            try
            {
                // Validar inputs básicos
                if (cursoRequest?.Curso == null || string.IsNullOrEmpty(twinId))
                {
                    return null;
                }

                // Inicializar Semantic Kernel
                await InitializeKernelAsync();
                
                // Generar análisis inteligente del curso con AI
                var aiAnalysis = await GenerateCreateCourseAnalysisAsync(cursoRequest, twinId);
                
                var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Course analysis completed successfully in {ProcessingTime}ms", processingTimeMs);
                
                return aiAnalysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing course creation for Twin: {TwinId}", twinId);
                return null;
            }
        }

        /// <summary>
        /// Genera análisis inteligente del curso con AI
        /// </summary>
        private async Task<CursoDetalles> GenerateCreateCourseAnalysisAsync(CrearCursoRequest cursoRequest, string twinId)
        {
            try
            {
                _logger.LogInformation("🤖 Generating intelligent course analysis with AI");

                var coursePrompt = $@"
Eres un experto analista educativo especializado en evaluación de cursos y desarrollo profesional. 
Vas a analizar un curso recién seleccionado por un Twin para generar un análisis comprensivo e insights educativos útiles.

DATOS DEL CURSO SELECCIONADO:
==============================
📚 Información del Curso:
• Nombre: {cursoRequest.Curso?.NombreClase ?? "No especificado"}
• Instructor: {cursoRequest.Curso?.Instructor ?? "No especificado"}
• Plataforma: {cursoRequest.Curso?.Plataforma ?? "No especificado"}
• Categoría: {cursoRequest.Curso?.Categoria ?? "No especificado"}
• Duración: {cursoRequest.Curso?.Duracion ?? "No especificado"}
• Idioma: {cursoRequest.Curso?.Precio ?? "No especificado"}
• Precio: {cursoRequest.Curso?.Idioma ?? "No especificado"}

📅 Cronograma:
• Fecha de Inicio: {cursoRequest.Curso?.FechaInicio ?? "No especificado"}
• Fecha de Fin: {cursoRequest.Curso?.FechaFin ?? "No especificado"}

🎯 Contenido Educativo:
• Lo que aprenderé: {cursoRequest.Curso?.LoQueAprendere ?? "No especificado"}
• Objetivos de Aprendizaje: {cursoRequest.Curso?.ObjetivosdeAprendizaje ?? "No especificado"}
• Habilidades y Competencias: {cursoRequest.Curso?.HabilidadesCompetencias ?? "No especificado"}
• Requisitos: {cursoRequest.Curso?.Requisitos ?? "No especificado"}
• Prerequisitos: {cursoRequest.Curso?.Prerequisitos ?? "No especificado"}

💡 Recursos:
• Recursos disponibles: {cursoRequest.Curso?.Recursos ?? "No especificado"}

🏷️ Información Personal:
• Etiquetas: {cursoRequest.Curso?.Etiquetas ?? "No especificado"}
• Notas Personales: {cursoRequest.Curso?.NotasPersonales ?? "No especificado"}

🔍 Enlaces:
• Enlace del Curso: {cursoRequest.Curso?.Enlaces?.EnlaceClase ?? "No especificado"}
• Enlace del Instructor: {cursoRequest.Curso?.Enlaces?.EnlaceInstructor ?? "No especificado"}
• Enlace de la Plataforma: {cursoRequest.Curso?.Enlaces?.EnlacePlataforma ?? "No especificado"}
• Enlace de la Categoría: {cursoRequest.Curso?.Enlaces?.EnlaceCategoria ?? "No especificado"}

📊 Metadatos:
• Fecha de Selección: {cursoRequest.Metadatos?.FechaSeleccion:yyyy-MM-dd HH:mm}
• Estado: {cursoRequest.Metadatos?.EstadoCurso ?? "seleccionado"}
• Origen de Búsqueda: {cursoRequest.Metadatos?.OrigenBusqueda ?? "manual"}
• Consulta Original: {cursoRequest.Metadatos?.ConsultaOriginal ?? "No especificada"}

INSTRUCCIONES PARA EL ANÁLISIS:
===============================

Genera un análisis educativo comprensivo que debe incluir:

1. **htmlDetails**: Análisis detallado en formato HTML atractivo con:
   - Header con colores educativos (#2E8B57, #4169E1, #FF6347)
   - Sección de objetivos de aprendizaje y competencias
   - Análisis del instructor y plataforma
   - Evaluación de requisitos y prerequisitos
   - Análisis de duración y cronograma
   - Análisis de precio y relación calidad-valor
   - Recursos educativos disponibles
   - Tabla con información detallada del curso
   - Recomendaciones de estudio con íconos educativos
   - Plan de seguimiento del progreso
   - Footer con información de selección

2. **textDetails**: Resumen ejecutivo en texto plano que incluya:
   - Evaluación general del valor educativo del curso
   - Relevancia para el desarrollo profesional/personal
   - Puntos fuertes identificados
   - Insights educativos (nivel de dificultad, tiempo de dedicación, aplicabilidad práctica)
   - Recomendaciones personalizadas
   - Plan de acción sugerido

FORMATO DE RESPUESTA REQUERIDO:
===============================

Debes responder ÚNICAMENTE con un objeto JSON válido con la siguiente estructura exacta:

{{
  ""htmlDetails"": ""<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto;'>HTML completo con análisis detallado y atractivo del curso</div>"",
  ""textDetails"": ""  en texto plano del análisis del curso con todos los insights y recomendaciones""
}}

IMPORTANTE:
- Responde SOLO con JSON válido, sin comillas al inicio o final (```json o ```)
- Enfócate en el valor educativo y desarrollo profesional
- Usa colores que inspiren aprendizaje en el HTML (#2E8B57, #4169E1, #FF6347, #32CD32)
- Incluye íconos educativos en el HTML (📚📝🎓💡🔍⭐📊🎯)
- Incluye recomendaciones prácticas para maximizar el aprendizaje
- Mantén un tono motivacional y profesional
- Todo el texto debe estar en español
- Analiza el potencial de crecimiento que ofrece este curso
- El HTML debe ser responsive y usar cards modernas";

                var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(coursePrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 4000,
                        ["temperature"] = 0.3 // Temperatura moderada para análisis educativo creativo
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var aiResponse = response.Content ?? "";
                CursoDetalles cursoDetalles = JsonConvert.DeserializeObject<CursoDetalles>(aiResponse);
                _logger.LogInformation("✅ AI course analysis generated successfully");
                return cursoDetalles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during AI course analysis");
                CursoDetalles detalles = new CursoDetalles();
                return detalles;
            }
        }
        public async Task<CursoSeleccionado> BuildClassWithDocumentAI(string TwinID,
            DocumentoClassRequest DocumentoClase,
             string containerName,
             string filePath,
             string fileName,
             string cursoId)
        {
            _logger.LogInformation("📚📄 Starting Course Document analysis for: {FileName}, CursoId: {CursoId}", fileName, cursoId);

            var startTime = DateTime.UtcNow;

            try
            {
                // PASO 1: Generar SAS URL para acceso al documento
                _logger.LogInformation("🔗 STEP 1: Generating SAS URL for document access...");

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
                        cursoId,
                        processedAt = DateTime.UtcNow
                    };
                    _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                    return  new CursoSeleccionado();
                }

                _logger.LogInformation("✅ SAS URL generated successfully");

                // PASO 2: Análisis con Document Intelligence
                _logger.LogInformation("🧠 STEP 2: Extracting data with Document Intelligence...");

                // Inicializar DocumentIntelligenceService
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, _configuration);

                var documentAnalysis = await documentIntelligenceService.AnalyzeDocumentWithPagesAsync(sasUrl);

                if (!documentAnalysis.Success)
                {
                    var errorResult = new
                    {
                        success = false,
                        errorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}",
                        containerName,
                        filePath,
                        fileName,
                        cursoId,
                        processedAt = DateTime.UtcNow
                    };
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return new CursoSeleccionado();
                }

                _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars",
                    documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

                // PASO 3: Extracción de capítulos con el contenido del curso
                _logger.LogInformation("📚 STEP 3: Extracting course chapters and content...");

                var classPages = ExtractDataFromClass(documentAnalysis, DocumentoClase);

                // PASO 4: Procesamiento con AI especializado en análisis educativo
                _logger.LogInformation("🤖 STEP 4: Processing with AI specialized in educational content analysis...");

                var AiCursoCreated = await ProcessCourseContentWithAI(fileName, filePath,
                    TwinID, documentAnalysis, classPages, DocumentoClase);

                

                // PASO 5: Actualizar el índice de búsqueda de cursos (opcional)
                _logger.LogInformation("🔍 STEP 5: Updating course content in search index...");
                try
                 {
                    // Crear instancia del CursosSearchIndex
                    var cursosSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CursosSearchIndex>();
                    var cursosSearchIndex = new CursosSearchIndex(cursosSearchLogger, _configuration);

                    // Llamar al método de actualización del contenido del curso
                    // var updateResult = await cursosSearchIndex.UpdateCourseContentIndex(aiAnalysisResult, cursoId, containerName);

                    _logger.LogInformation("✅ Course content index update attempted");
                }
                catch (Exception indexEx)
                {
                    _logger.LogWarning(indexEx, "⚠️ Failed to update course content in search index, continuing with main flow");
                    // No fallar toda la operación por esto
                }

                var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Resultado exitoso


                return AiCursoCreated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing course document {FileName}", fileName);

                var errorResult = new
                {
                    success = false,
                    errorMessage = ex.Message,
                    containerName,
                    filePath,
                    fileName,
                    cursoId,
                    processedAt = DateTime.UtcNow
                };

                return new CursoSeleccionado();
            }
        }

        /// <summary>
        /// Procesa el contenido del curso con AI para extraer índice estructurado
        /// </summary>
        private async Task<CursoSeleccionado> ProcessCourseContentWithAI(
            string FileName, string Path, string TwinID,
            DocumentAnalysisResult documentAnalysis, 
            List<DocumentPage> classPages, 
            DocumentoClassRequest documentoClase)
        {
            try
            {
                // Asegurar que el kernel esté inicializado
                await InitializeKernelAsync();
                
                var chatCompletion = _kernel!.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                // PASO 1: Combinar todo el texto de todas las páginas en un solo string
                var sb = new StringBuilder();
                sb.AppendLine("=== CONTENIDO COMPLETO DEL DOCUMENTO ===");
                sb.AppendLine($"Documento: {documentoClase.Nombre ?? "Curso no especificado"}");
                sb.AppendLine($"Total de Páginas: {documentoClase.NumeroPaginas}");
                sb.AppendLine();

                foreach (var chapter in classPages)
                {
                    sb.AppendLine($"=== Pagina: {chapter.PageNumber} ===");
                    
                    foreach (var page in chapter.LinesText)
                    {

                        sb.AppendLine(page);
                        sb.AppendLine(); // Separador entre páginas
                    }
                    sb.AppendLine(); // Separador entre capítulos
                }

                string fullDocumentText = sb.ToString();

                _logger.LogInformation("📄 Combined document text: {TextLength} characters from {ChapterCount} sections", 
                    fullDocumentText.Length, classPages.Count);

                // PASO 2: Crear prompt para extracción de índice únicamente
                var prompt = $@"
Analiza este documento educativo completo y extrae SOLAMENTE el índice/tabla de contenido en formato JSON.

CONTENIDO COMPLETO DEL DOCUMENTO:
================================
{fullDocumentText}

INSTRUCCIONES PARA EXTRACCIÓN DE ÍNDICE:
=======================================

Tu única tarea es extraer el índice del documento y crear una lista JSON estructurada.

🎯 **OBJETIVO:**
- Identifica todos los títulos, capítulos, secciones y subsecciones
- Determina la página de inicio y fin de cada sección
- Crea un índice completo y organizado

📋 **FORMATO DE RESPUESTA REQUERIDO:**

Responde ÚNICAMENTE con JSON válido en esta estructura exacta:

{{
  ""indice"": [
    {{

      ""titulo"": ""Nombre del capítulo o sección"",
      ""paginaDe"": 1,
      ""paginaA"": 5
    }},
    {{

      ""titulo"": ""Siguiente capítulo o sección"",
      ""paginaDe"": 6,
      ""paginaA"": 12
    }}
  ]
}}

🚨 **REGLAS CRÍTICAS:**
- NO generes HTML, resúmenes o análisis
- NO uses markdown (```json o ```
- SOLO extrae el índice en JSON
- Identifica páginas de inicio y fin reales del contenido
- Usa títulos exactos como aparecen en el documento
- Organiza en orden secuencial de páginas
- Si no hay índice visible, crea uno basado en la estructura del contenido

🔍 **BUSCA EN EL DOCUMENTO:**
- Títulos principales y subtítulos
- Numeración de capítulos (1., 2., I., II., etc.)
- Secciones claramente definidas
- Cambios de tema o contenido
- Referencias a números de página

Extrae el índice completo ahora:";

                history.AddUserMessage(prompt);
                
                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 4000,
                        ["temperature"] = 0.2 // Temperatura muy baja para extracción precisa
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

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var SearchLogger = loggerFactory.CreateLogger<CursosSearchIndex>();
                CursosAgentAI cursoAI = new CursosAgentAI(SearchLogger, _configuration);
                _logger.LogInformation("✅ AI index extraction completed successfully");
                _logger.LogInformation("📊 AI Response Length: {Length} characters", aiResponse.Length);
               
           
                
                List<CapituloRequest> Capituloscurso = await cursoAI.ExtarctDataFromClass(aiResponse, documentAnalysis.DocumentPages);

                int NumeroCapitulo = 0;
                foreach (var capitulo in Capituloscurso)
                {
                    capitulo.NumeroCapitulo = NumeroCapitulo + 1;
                }


                var CursoCompleto = await cursoAI.BuildfullCurseWithAi(aiResponse);

                CursoCompleto.Capitulos = Capituloscurso;
                CursoCompleto.Nombre = documentoClase.Nombre;
                CursoCompleto.Descripcion = documentoClase.Descripcion;
                CursoCompleto.Notas = documentoClase.Notas;
                CursoCompleto.NumeroPaginas = documentoClase.NumeroPaginas;
                CursoCompleto.TieneIndice = documentoClase.TieneIndice;
                CursoCompleto.PaginaInicioIndice = documentoClase.PaginaInicioIndice;
                CursoCompleto.PaginaFinIndice = documentoClase.PaginaFinIndice;
                CursoCompleto.PathArchivo = Path;
                CursoCompleto.NombreArchivo = FileName;
                CursoCompleto.TwinID = TwinID;
                CursoCompleto.FechaCreacion = DateTime.UtcNow;
                CursoCompleto.FechaUltimaModificacion = DateTime.UtcNow;  

                return CursoCompleto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI index extraction processing");
                
                // Retornar JSON de error
                var errorResponse = new
                {
                    success = false,
                    errorMessage = ex.Message,
                    indice = new object[0]
                };

                return null;
            }
        }


        
        public async Task<CursoDetalles> ProcessUpdateCursoAsync(CrearCursoRequest cursoRequest, string twinId)
        {
            _logger.LogInformation("📚 Processing intelligent course update for Twin ID: {TwinId}", twinId);
            _logger.LogInformation("📚 Course: {NombreClase}", cursoRequest.Curso?.NombreClase);

            var startTime = DateTime.UtcNow;

            try
            {
                // Validar inputs básicos
                if (cursoRequest?.Curso == null || string.IsNullOrEmpty(twinId))
                {
                    CursoDetalles detalles = new CursoDetalles();
                    return detalles;
                }

                // Inicializar Semantic Kernel
                await InitializeKernelAsync();
                
                // Generar análisis inteligente del curso actualizado con AI
                var aiAnalysis = await GenerateUpdateCourseAnalysisAsync(cursoRequest, twinId);
                
                var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Course update analysis completed successfully in {ProcessingTime}ms", processingTimeMs);
                
                return aiAnalysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing course update for Twin: {TwinId}", twinId);
                CursoDetalles detalles = new CursoDetalles();
                return detalles;
            }
        }

        /// <summary>
        /// Genera análisis inteligente del curso actualizado con AI
        /// </summary>
        private async Task<CursoDetalles> GenerateUpdateCourseAnalysisAsync(CrearCursoRequest cursoRequest, string twinId)
        {
            try
            {
                _logger.LogInformation("🤖 Generating intelligent course update analysis with AI");

                var updatePrompt = $@"
Eres un experto analista educativo especializado en evaluación de cursos y desarrollo profesional. 
Vas a analizar un curso que ha sido ACTUALIZADO por un Twin para generar un análisis comprensivo e insights educativos útiles.

DATOS DEL CURSO ACTUALIZADO:
==============================
📚 Información del Curso:
• Nombre: {cursoRequest.Curso?.NombreClase ?? "No especificado"}
• Instructor: {cursoRequest.Curso?.Instructor ?? "No especificado"}
• Plataforma: {cursoRequest.Curso?.Plataforma ?? "No especificado"}
• Categoría: {cursoRequest.Curso?.Categoria ?? "No especificado"}
• Duración: {cursoRequest.Curso?.Duracion ?? "No especificado"}
• Idioma: {cursoRequest.Curso?.Precio ?? "No especificado"}
• Precio: {cursoRequest.Curso?.Idioma ?? "No especificado"}

📅 Cronograma:
• Fecha de Inicio: {cursoRequest.Curso?.FechaInicio ?? "No especificado"}
• Fecha de Fin: {cursoRequest.Curso?.FechaFin ?? "No especificado"}

🎯 Contenido Educativo:
• Lo que aprenderé: {cursoRequest.Curso?.LoQueAprendere ?? "No especificado"}
• Objetivos de Aprendizaje: {cursoRequest.Curso?.ObjetivosdeAprendizaje ?? "No especificado"}
• Habilidades y Competencias: {cursoRequest.Curso?.HabilidadesCompetencias ?? "No especificado"}
• Requisitos: {cursoRequest.Curso?.Requisitos ?? "No especificado"}
• Prerequisitos: {cursoRequest.Curso?.Prerequisitos ?? "No especificado"}

💡 Recursos:
• Recursos disponibles: {cursoRequest.Curso?.Recursos ?? "No especificado"}

🏷️ Información Personal:
• Etiquetas: {cursoRequest.Curso?.Etiquetas ?? "No especificado"}
• Notas Personales: {cursoRequest.Curso?.NotasPersonales ?? "No especificado"}

🔍 Enlaces:
• Enlace del Curso: {cursoRequest.Curso?.Enlaces?.EnlaceClase ?? "No especificado"}
• Enlace del Instructor: {cursoRequest.Curso?.Enlaces?.EnlaceInstructor ?? "No especificado"}
• Enlace de la Plataforma: {cursoRequest.Curso?.Enlaces?.EnlacePlataforma ?? "No especificado"}
• Enlace de la Categoría: {cursoRequest.Curso?.Enlaces?.EnlaceCategoria ?? "No especificado"}

📊 Metadatos:
• Fecha de Selección: {cursoRequest.Metadatos?.FechaSeleccion:yyyy-MM-dd HH:mm}
• Estado: {cursoRequest.Metadatos?.EstadoCurso ?? "seleccionado"}
• Origen de Búsqueda: {cursoRequest.Metadatos?.OrigenBusqueda ?? "manual"}
• Consulta Original: {cursoRequest.Metadatos?.ConsultaOriginal ?? "No especificada"}
• Fecha de Actualización: {DateTime.UtcNow:yyyy-MM-dd HH:mm}

INSTRUCCIONES PARA EL ANÁLISIS DE ACTUALIZACIÓN:
===============================================

Responde en JSON válido con exactamente esta estructura:

{{
  ""htmlDetails"": ""HTML completo con análisis detallado del curso actualizado"",
  ""textDetails"": ""Resumen ejecutivo en texto plano del curso actualizado""
}}

El htmlDetails debe incluir:
- Header con colores educativos actualizados (#2E8B57, #4169E1, #FF6347, #9C27B0)
- Banner de CURSO ACTUALIZADO prominente
- Sección de objetivos de aprendizaje y competencias actualizados
- Análisis del instructor y plataforma con información actual
- Evaluación de requisitos y prerequisitos actualizados
- Análisis de duración y cronograma revisado
- Análisis de precio y relación calidad-valor actualizada
- Recursos educativos disponibles (nuevos y existentes)
- Integración de etiquetas y notas personales del estudiante
- Tabla con información detallada y actualizada del curso
- Recomendaciones de estudio adaptadas a las actualizaciones
- Plan de seguimiento del progreso revisado
- Footer con información de actualización

El textDetails debe incluir:
- Evaluación de las mejoras realizadas en el curso
- Impacto de las actualizaciones en el valor educativo
- Relevancia actualizada para el desarrollo profesional/personal
- Nuevos puntos fuertes identificados
- Insights educativos actualizados
- Recomendaciones personalizadas mejoradas
- Plan de acción revisado
- Análisis de valor agregado

IMPORTANTE:
- Responde SOLO con JSON válido, sin comillas al inicio o final
- Enfócate en las MEJORAS y ACTUALIZACIONES del curso
- Resalta el valor agregado de las personalizaciones (etiquetas, notas)
- Usa colores que inspiren aprendizaje continuo en el HTML
- Incluye íconos educativos en el HTML (📚📝🎓💡🔍⭐📊🎯🔄✨)
- Mantén un tono motivacional y profesional con énfasis en progreso
- Todo el texto debe estar en español
- Analiza cómo las actualizaciones potencian el crecimiento educativo";

                var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(updatePrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 4000,
                        ["temperature"] = 0.3 // Temperatura moderada para análisis educativo creativo
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var aiResponse = response.Content ?? "";
                CursoDetalles cursoDetalles = JsonConvert.DeserializeObject<CursoDetalles>(aiResponse);
                _logger.LogInformation("✅ AI course update analysis generated successfully");
                return cursoDetalles;
            }
            catch (Exception ex)
            {
                CursoDetalles detalles = new CursoDetalles();
                return detalles;
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
                _logger.LogInformation("🔧 Initializing Semantic Kernel for CursosAgent");

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

                _logger.LogInformation("✅ Semantic Kernel initialized successfully for CursosAgent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for CursosAgent");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Genera respuesta de fallback cuando hay errores
        /// </summary>
        private string GenerateFallbackResponse(CrearCursoRequest? cursoRequest, string errorMessage, string twinId)
        {
            var htmlFallback = $@"<div style=""background: linear-gradient(135deg, #2E8B57 0%, #228B22 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;"">
                <h3 style=""color: #fff; margin: 0 0 15px 0;"">📚 Curso Registrado</h3>
                
                <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                    <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">📋 Detalles del Curso</h4>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Curso:</strong> {cursoRequest?.Curso?.NombreClase ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Instructor:</strong> {cursoRequest?.Curso?.Instructor ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Plataforma:</strong> {cursoRequest?.Curso?.Plataforma ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Duración:</strong> {cursoRequest?.Curso?.Duracion ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Precio:</strong> {cursoRequest?.Curso?.Precio ?? "No especificado"}</p>
                </div>

                <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                    <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">✅ Estado</h4>
                    <p style=""margin: 0; line-height: 1.6;"">Tu curso ha sido registrado exitosamente en tu portafolio educativo.</p>
                    {(string.IsNullOrEmpty(errorMessage) ? "" : $@"<p style=""margin: 5px 0; line-height: 1.6; color: #ffeb3b;""><small>Nota: {errorMessage}</small></p>")}
                </div>
                
                <div style=""margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;"">
                    📚 ID: {cursoRequest?.CursoId ?? "generado"} • 👤 Twin: {twinId} • 📅 {DateTime.UtcNow:yyyy-MM-dd HH:mm}
                </div>
            </div>";

            var textFallback = $@"Curso registrado exitosamente: {cursoRequest?.Curso?.NombreClase ?? "No especificado"}
            
Instructor: {cursoRequest?.Curso?.Instructor ?? "No especificado"}
Plataforma: {cursoRequest?.Curso?.Plataforma ?? "No especificado"}
Duración: {cursoRequest?.Curso?.Duracion ?? "No especificado"}
Precio: {cursoRequest?.Curso?.Precio ?? "No especificado"}

Estado: Tu curso ha sido registrado exitosamente en tu portafolio educativo.
{(string.IsNullOrEmpty(errorMessage) ? "" : $"Nota: {errorMessage}")}

ID: {cursoRequest?.CursoId ?? "generado"} • Twin: {twinId} • Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                htmlDetails = htmlFallback,
                textDetails = textFallback
            });
        }

        /// <summary>
        /// Genera respuesta de fallback para actualización cuando hay errores
        /// </summary>
        private string GenerateUpdateFallbackResponse(CrearCursoRequest? cursoRequest, string errorMessage, string twinId)
        {
            var htmlFallback = $@"<div style=""background: linear-gradient(135deg, #9C27B0 0%, #673AB7 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;"">
                <h3 style=""color: #fff; margin: 0 0 15px 0;"">🔄 Curso Actualizado</h3>
                
                <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                    <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">📋 Detalles del Curso Actualizado</h4>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Curso:</strong> {cursoRequest?.Curso?.NombreClase ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Instructor:</strong> {cursoRequest?.Curso?.Instructor ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Plataforma:</strong> {cursoRequest?.Curso?.Plataforma ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Duración:</strong> {cursoRequest?.Curso?.Duracion ?? "No especificado"}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Precio:</strong> {cursoRequest?.Curso?.Precio ?? "No especificado"}</p>
                    {(!string.IsNullOrEmpty(cursoRequest?.Curso?.Etiquetas) ? $@"<p style=""margin: 5px 0; line-height: 1.6;""><strong>Etiquetas:</strong> {cursoRequest.Curso.Etiquetas}</p>" : "")}
                    {(!string.IsNullOrEmpty(cursoRequest?.Curso?.NotasPersonales) ? $@"<p style=""margin: 5px 0; line-height: 1.6;""><strong>Notas:</strong> {cursoRequest.Curso.NotasPersonales}</p>" : "")}
                </div>

                <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                    <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">✅ Estado de Actualización</h4>
                    <p style=""margin: 0; line-height: 1.6;"">Tu curso ha sido actualizado exitosamente en tu portafolio educativo.</p>
                    {(string.IsNullOrEmpty(errorMessage) ? "" : $@"<p style=""margin: 5px 0; line-height: 1.6; color: #ffeb3b;""><small>Nota: {errorMessage}</small></p>")}
                </div>
                
                <div style=""margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;"">
                    📚 ID: {cursoRequest?.CursoId ?? "generado"} • 👤 Twin: {twinId} • 🔄 Actualizado: {DateTime.UtcNow:yyyy-MM-dd HH:mm}
                </div>
            </div>";

            var textFallback = $@"Curso actualizado exitosamente: {cursoRequest?.Curso?.NombreClase ?? "No especificado"}

Instructor: {cursoRequest?.Curso?.Instructor ?? "No especificado"}
Plataforma: {cursoRequest?.Curso?.Plataforma ?? "No especificado"}
Duración: {cursoRequest?.Curso?.Duracion ?? "No especificado"}
Precio: {cursoRequest?.Curso?.Precio ?? "No especificado"}
{(!string.IsNullOrEmpty(cursoRequest?.Curso?.Etiquetas) ? $"Etiquetas: {cursoRequest.Curso.Etiquetas}" : "")}
{(!string.IsNullOrEmpty(cursoRequest?.Curso?.NotasPersonales) ? $"Notas Personales: {cursoRequest.Curso.NotasPersonales}" : "")}

Estado: Tu curso ha sido actualizado exitosamente en tu portafolio educativo.
{(string.IsNullOrEmpty(errorMessage) ? "" : $"Nota: {errorMessage}")}

ID: {cursoRequest?.CursoId ?? "generado"} • Twin: {twinId} • Actualizado: {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                htmlDetails = htmlFallback,
                textDetails = textFallback
            });
        }

        public List<DocumentPage> ExtractDataFromClass(DocumentAnalysisResult DataExtracted, DocumentoClassRequest DocumentoClase)
        {
            _logger.LogInformation("📚 Starting improved course content extraction from document with {Pages} pages", DataExtracted.TotalPages);

            var classPages = new List<DocumentPage>();


            try
            {
                // PASO 1: Usar DocumentPages directamente si están disponibles
                if (DataExtracted.DocumentPages != null && DataExtracted.DocumentPages.Count > 0)
                {
                    _logger.LogInformation("📄 Using direct DocumentPages: {PageCount} pages", DataExtracted.DocumentPages.Count);

                    // PASO 2: Encontrar/extraer las páginas del índice usando la información proporcionada
                    classPages = FindIndexPageFromDocumentPages(DataExtracted.DocumentPages, DocumentoClase);
                    


                  
                }
                else
                {
                    // Fallback: procesar como un solo documento si no hay DocumentPages
                    _logger.LogInformation("📄 Fallback: Creating single document from text content");
                    classPages = new List<DocumentPage>();
                }

                _logger.LogInformation("✅ Improved course content extraction completed successfully: {ChapterCount} chapters", classPages.Count);
                
                return classPages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in improved course content extraction");
                return classPages;
            }
        }

        /// <summary>
        /// Encuentra la página del índice usando DocumentPages y extrae solo las páginas del índice si está especificado
        /// </summary>
        private List<DocumentPage> FindIndexPageFromDocumentPages(List<DocumentPage> documentPages, DocumentoClassRequest documentoClase)
        {
            try
            {
                // PASO 1: Si se especificó que tiene índice con páginas de inicio y fin, extraer solo esas páginas
                if (documentoClase.TieneIndice && 
                    documentoClase.PaginaInicioIndice.HasValue && 
                    documentoClase.PaginaFinIndice.HasValue)
                {
                    int paginaInicio = documentoClase.PaginaInicioIndice.Value;
                    int paginaFin = documentoClase.PaginaFinIndice.Value;
                    
                    _logger.LogInformation("📋 Extracting index pages from {StartPage} to {EndPage}", paginaInicio, paginaFin);
                    
                    // Filtrar solo las páginas del índice
                    var indexPages = documentPages
                        .Where(p => p.PageNumber >= paginaInicio && p.PageNumber <= paginaFin)
                        .OrderBy(p => p.PageNumber)
                        .ToList();
                    
                    if (indexPages.Count > 0)
                    {
                        _logger.LogInformation("✅ Successfully extracted {PageCount} index pages", indexPages.Count);
                        return indexPages;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ No pages found in specified index range {StartPage}-{EndPage}", paginaInicio, paginaFin);
                        return new List<DocumentPage>();
                    }
                }
                
                // PASO 2: Si solo se especificó página de inicio del índice (comportamiento original)
                if (documentoClase.TieneIndice && documentoClase.PaginaInicioIndice.HasValue)
                {
                    int specifiedPage = documentoClase.PaginaInicioIndice.Value;
                    var targetPage = documentPages.FirstOrDefault(p => p.PageNumber == specifiedPage);
                    if (targetPage != null)
                    {
                        _logger.LogInformation("📋 Using specified index page: {PageNumber}", specifiedPage);
                        // Retornar todas las páginas ya que no se especificó fin
                        return documentPages;
                    }
                }

                // PASO 3: Buscar indicadores de índice en las primeras páginas (comportamiento original)
                var indexIndicators = new[]
                {
                    "table of contents", "contents", "índice", "indice", "contenido",
                    "chapter", "capítulo", "capitulo", "section", "sección"
                };

                // Buscar solo en las primeras 10 páginas
                var pagesToSearch = Math.Min(10, documentPages.Count);
                
                for (int i = 0; i < pagesToSearch; i++)
                {
                    var page = documentPages[i];
                    var pageText = string.Join(" ", page.LinesText).ToLowerInvariant();
                    
                    // Verificar indicadores de índice
                    bool hasIndexIndicator = indexIndicators.Any(indicator => pageText.Contains(indicator));
                    
                    // Verificar estructura de índice (múltiples líneas con números de página)
                    bool hasIndexStructure = HasIndexStructureInLines(page.LinesText);
                    
                    if (hasIndexIndicator || hasIndexStructure)
                    {
                        _logger.LogInformation("📋 Index found on page {PageNumber}", page.PageNumber);
                        // Retornar todas las páginas ya que se encontró el índice automáticamente
                        return documentPages;
                    }
                }

                // No se encontró índice, retornar lista vacía para indicar que no hay índice
                _logger.LogWarning("⚠️ No index found in document");
                return new List<DocumentPage>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding index page from DocumentPages");
                return new List<DocumentPage>();
            }
        }

        /// <summary>
        /// Verifica si las líneas tienen estructura de índice
        /// </summary>
        private bool HasIndexStructureInLines(List<string> lines)
        {
            int linesWithPageNumbers = 0;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;
                
                // Buscar patrones típicos de índice:
                // "Chapter 1...........5", "Introduction.....3", "1. Introduction    5"
                if (System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, @".*\.{2,}.*\d+\s*$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, @".*\s+\d+\s*$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, @"^\d+[\.\s]+.*\s+\d+\s*$"))
                {
                    linesWithPageNumbers++;
                }
            }

            return linesWithPageNumbers >= 3; // Al menos 3 líneas con estructura de índice
        }

        /// <summary>
        /// Extrae la estructura de capítulos desde DocumentPages
        /// </summary>
        private List<ChapterInfo> ExtractChapterStructureFromPages(List<TwinFx.Services.DocumentPage> documentPages, int indexStartPage)
        {
            var chapters = new List<ChapterInfo>();
            
            try
            {
                // Examinar las páginas del índice (usualmente 1-3 páginas)
                int pagesToCheck = Math.Min(3, documentPages.Count - indexStartPage);
                
                for (int i = 0; i < pagesToCheck; i++)
                {
                    var page = documentPages[indexStartPage + i];
                    var pageChapters = ParseMainChaptersFromLines(page.LinesText);
                    chapters.AddRange(pageChapters);
                }

                // Filtrar solo capítulos de alto nivel (no subíndices)
                chapters = FilterMainChaptersOnly(chapters);

                // Ordenar por página de inicio y calcular páginas de fin
                chapters = chapters.OrderBy(c => c.StartPage).ToList();
                
                for (int i = 0; i < chapters.Count; i++)
                {
                    if (i < chapters.Count - 1)
                    {
                        chapters[i].EndPage = chapters[i + 1].StartPage - 1;
                    }
                    else
                    {
                        // Para el último capítulo, necesitamos estimar la página final
                        // Podrías usar el total de páginas del documento o dejarlo abierto
                        chapters[i].EndPage = chapters[i].StartPage + 50; // Estimación temporal
                    }
                }

                _logger.LogInformation("✅ Chapter structure extraction completed: {ChapterCount} main chapters found", chapters.Count);
                return chapters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting chapter structure from index pages");
                return chapters;
            }
        }

        /// <summary>
        /// Parsea solo los capítulos principales desde las líneas del índice
        /// </summary>
        private List<ChapterInfo> ParseMainChaptersFromLines(List<string> lines)
        {
            var chapters = new List<ChapterInfo>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                // Patrones para capítulos principales (de alto nivel):
                var mainChapterPatterns = new[]
                {
                    @"^(Chapter|Capítulo|Cap\.?)\s*(\d+)[:\.\s]+(.*?)\.{2,}(\d+)", // "Chapter 1: Title.....5"
                    @"^(\d+)[\.\s]+(.*?)\.{2,}(\d+)",                            // "1. Title.....5"
                    @"^([A-Za-z][^\.]{3,}?)\.{2,}(\d+)\s*$",                     // "Introduction.....5" (sin número inicial)
                    @"^([A-Z][A-Z\s]{2,})\s+(\d+)\s*$"                          // "INTRODUCTION 5" (mayúsculas)
                };

                foreach (var pattern in mainChapterPatterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, pattern);
                    if (match.Success)
                    {
                        try
                        {
                            string chapterName;
                            int pageNumber;

                            if (pattern.Contains("Chapter|Capítulo"))
                            {
                                // "Chapter 1: Title.....5"
                                chapterName = $"{match.Groups[1].Value} {match.Groups[2].Value}: {match.Groups[3].Value}";
                                pageNumber = int.Parse(match.Groups[4].Value);
                            }
                            else if (pattern.Contains(@"^(\d+)"))
                            {
                                // "1. Title.....5"
                                chapterName = $"{match.Groups[1].Value}. {match.Groups[2].Value}";
                                pageNumber = int.Parse(match.Groups[3].Value);
                            }
                            else if (match.Groups.Count == 3)
                            {
                                // "Introduction.....5" o "INTRODUCTION 5"
                                chapterName = match.Groups[1].Value.Trim();
                                pageNumber = int.Parse(match.Groups[2].Value);
                            }
                            else
                            {
                                continue;
                            }

                            // Validar que el número de página sea razonable
                            if (pageNumber > 0 && pageNumber < 10000)
                            {
                                chapters.Add(new ChapterInfo
                                {
                                    ChapterName = chapterName.Trim(),
                                    StartPage = pageNumber,
                                    EndPage = pageNumber
                                });
                            }
                            
                            break; // Si encontró un match, no probar otros patrones
                        }
                        catch (Exception)
                        {
                            continue; // Continuar con el siguiente patrón si hay error
                        }
                    }
                }
            }

            return chapters;
        }

        /// <summary>
        /// Filtra solo los capítulos principales, eliminando subíndices
        /// </summary>
        private List<ChapterInfo> FilterMainChaptersOnly(List<ChapterInfo> allChapters)
        {
            var mainChapters = new List<ChapterInfo>();

            foreach (var chapter in allChapters)
            {
                var chapterName = chapter.ChapterName.ToLowerInvariant();
                
                // Excluir patrones típicos de subíndices:
                bool isSubIndex = 
                    chapterName.Contains("1.1") || chapterName.Contains("2.1") ||  // "1.1 Subtopic"
                    chapterName.Contains("1.a") || chapterName.Contains("a.") ||   // "1.a Subtopic"
                    System.Text.RegularExpressions.Regex.IsMatch(chapterName, @"\d+\.\d+") || // Cualquier x.y
                    chapterName.StartsWith("    ") || chapterName.StartsWith("\t") ||         // Indentado
                    (chapterName.Length < 3); // Muy corto, probablemente no es un capítulo

                // Incluir solo si NO es un subíndice
                if (!isSubIndex)
                {
                    mainChapters.Add(chapter);
                }
            }

            return mainChapters;
        }

        /// <summary>
        /// Extrae la información de un capítulo usando DocumentPages
        /// </summary>
        private List<DocumentClassPages> ExtractChapterContentFromPages(List<TwinFx.Services.DocumentPage> documentPages, ChapterInfo chapterInfo)
        {
            var chapterPages = new List<DocumentClassPages>();

            try
            {
                foreach (var page in documentPages)
                {
                    if (page.PageNumber >= chapterInfo.StartPage && page.PageNumber <= chapterInfo.EndPage)
                    {
                        var documentPage = new DocumentClassPages
                        {
                            PageNumber = page.PageNumber,
                            PageLines = page.LinesText
                        };

                        chapterPages.Add(documentPage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting content for chapter {ChapterName} from pages", chapterInfo.ChapterName);
            }

            return chapterPages;
        }

        /// <summary>
        /// Procesa documento sin índice usando DocumentPages
        /// </summary>
        private Dictionary<string, List<DocumentClassPages>> ProcessDocumentWithoutIndexFromPages(List<DocumentPage> documentPages)
        {
            var classPages = new Dictionary<string, List<DocumentClassPages>>();
            var allPages = new List<DocumentClassPages>();

            foreach (var page in documentPages)
            {
                allPages.Add(new DocumentClassPages
                {
                    PageNumber = page.PageNumber,
                    PageLines = page.LinesText
                });
            }

            classPages["Complete Document"] = allPages;
            return classPages;
        }

        /// <summary>
        /// Procesa documento como texto fallback
        /// </summary>
        private Dictionary<string, List<DocumentClassPages>> ProcessDocumentAsTextFallback(string textContent, int totalPages)
        {
            var classPages = new Dictionary<string, List<DocumentClassPages>>();
            var allPages = new List<DocumentClassPages>();

            // Dividir el contenido en páginas aproximadas
            var pageLength = textContent.Length / Math.Max(1, totalPages);
            
            for (int i = 0; i < totalPages; i++)
            {
                int startIndex = i * pageLength;
                int endIndex = Math.Min((i + 1) * pageLength, textContent.Length);
                
                if (startIndex < textContent.Length)
                {
                    string pageContent = textContent.Substring(startIndex, endIndex - startIndex);
                    var lines = pageContent.Split('\n', StringSplitOptions.None).ToList();
                    
                    allPages.Add(new DocumentClassPages
                    {
                        PageNumber = i + 1,
                        PageLines = lines
                    });
                }
            }

            classPages["Complete Document"] = allPages;
            return classPages;
        }

        /// <summary>
        /// Extrae la estructura de capítulos desde las páginas específicas del índice
        /// </summary>
        private List<ChapterInfo> ExtractChapterStructureFromIndexPages(List<DocumentPage> indexPages)
        {
            var chapters = new List<ChapterInfo>();
            
            try
            {
                _logger.LogInformation("📋 Extracting chapter structure from {PageCount} index pages", indexPages.Count);
                
                // Procesar todas las páginas del índice proporcionadas
                foreach (var page in indexPages)
                {
                    var pageChapters = ParseMainChaptersFromLines(page.LinesText);
                    chapters.AddRange(pageChapters);
                    
                    _logger.LogInformation("📄 Processed index page {PageNumber}: found {ChapterCount} chapters", 
                        page.PageNumber, pageChapters.Count);
                }

                // Filtrar solo capítulos de alto nivel (no subíndices)
                chapters = FilterMainChaptersOnly(chapters);

                // Ordenar por página de inicio y calcular páginas de fin
                chapters = chapters.OrderBy(c => c.StartPage).ToList();
                
                for (int i = 0; i < chapters.Count; i++)
                {
                    if (i < chapters.Count - 1)
                    {
                        chapters[i].EndPage = chapters[i + 1].StartPage - 1;
                    }
                    else
                    {
                        // Para el último capítulo, necesitamos estimar la página final
                        // Podrías usar el total de páginas del documento o dejarlo abierto
                        chapters[i].EndPage = chapters[i].StartPage + 50; // Estimación temporal
                    }
                }

                _logger.LogInformation("✅ Chapter structure extraction completed: {ChapterCount} main chapters found", chapters.Count);
                return chapters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting chapter structure from index pages");
                return chapters;
            }
        }
    }
    public class CrearCursoRequest
    {
        [JsonProperty("curso")]
        public CursoSeleccionado Curso { get; set; }

        [JsonProperty("metadatos")]
        public MetadatosCurso Metadatos { get; set; }


        [JsonProperty("cursoId")]
        public string CursoId { get; set; }

        [JsonProperty("twinId")]
        public string TwinId { get; set; }
    }

    public class CursoSeleccionado
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

        [JsonProperty("objetivosdeAprendizaje")]
        public string ObjetivosdeAprendizaje { get; set; }

        [JsonProperty("habilidadesCompetencias")]
        public string HabilidadesCompetencias { get; set; }

        [JsonProperty("prerequisitos")]
        public string Prerequisitos { get; set; }

        [JsonProperty("enlaces")]
        public Enlaces Enlaces { get; set; }


        [JsonProperty("etiquetas")]
        public string Etiquetas { get; set; }

        [JsonProperty("NotasPersonales")]
        public string NotasPersonales { get; set; }

        [JsonProperty("htmlDetails")]
        public string htmlDetails { get; set; }

        [JsonProperty("textoDetails")]
        public string textoDetails { get; set; }

        [JsonProperty("capitulos")]
        public List<CapituloRequest> Capitulos { get; set; }

        public string? Nombre { get; set; }

        /// <summary>
        /// Descripción del contenido del documento
        /// </summary>
        public string? Descripcion { get; set; }

        public string PathArchivo { get; set; }
        public string TwinID { get; set; }
        public string id { get; set; }

        public DateTime FechaCreacion { get; set; }

        public DateTime? FechaUltimaModificacion { get; set; }


        /// <summary>
        /// Notas personales del usuario
        /// </summary>
        public string? Notas { get; set; }

        /// <summary>
        /// Número total de páginas del documento
        /// </summary>
        public int? NumeroPaginas { get; set; }

        /// <summary>
        /// Indica si el documento tiene índice
        /// </summary>
        public bool TieneIndice { get; set; }

        /// <summary>
        /// Página donde comienza el índice (solo si TieneIndice = true)
        /// </summary>
        public int? PaginaInicioIndice { get; set; }

        public int? PaginaFinIndice { get; set; }

        // Información del archivo
        /// <summary>
        /// Nombre original del archivo
        /// </summary>
        public string NombreArchivo { get; set; } = string.Empty;

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string TipoArchivo { get; set; } = string.Empty;

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long TamanoArchivo { get; set; }
    }

    public class MetadatosCurso
    {
        [JsonProperty("fechaSeleccion")]
        public DateTime FechaSeleccion { get; set; }

        [JsonProperty("estadoCurso")]
        public string EstadoCurso { get; set; } // "seleccionado", "en_progreso", "completado"

        [JsonProperty("origenBusqueda")]
        public string OrigenBusqueda { get; set; } // "ia_search", "manual", "recomendacion"

        [JsonProperty("consultaOriginal")]
        public string ConsultaOriginal { get; set; }
    }

    public class CursoDetalles
    {
        public string htmlDetails { get; set; }
        public string textoDetails { get; set; }
    }

    public class  CapituloRequest
    {

        public int TotalTokens { get; set; } = 0;


        public int TotalTokensInput { get; set; } = 0;


        public int TotalTokensOutput { get; set; } = 0;
        // Campos básicos del capítulo
        public string Titulo { get; set; }
        public string? Descripcion { get; set; }
        public int NumeroCapitulo { get; set; }

        // Contenido del capítulo
        public string? Transcript { get; set; }
        public string? Notas { get; set; }
        public string? Comentarios { get; set; }

        // Metadatos
        public int? DuracionMinutos { get; set; }
        public List<string>? Tags { get; set; } = new List<string>();

        // Evaluación inicial (opcional)
        public int? Puntuacion { get; set; } // 1-5 estrellas

        // Identificadores de relación
        public string CursoId { get; set; }
        public string TwinId { get; set; }

        // Estado inicial
        public bool Completado { get; set; } = false;

        // ✨ NUEVOS CAMPOS GENERADOS POR AI ✨
        // =================================
        
        /// <summary>
        /// Resumen ejecutivo del capítulo generado por AI
        /// </summary>
        public string? ResumenEjecutivo { get; set; }
        
        /// <summary>
        /// Explicación detallada del profesor en texto plano para conversión a voz
        /// </summary>
        public string? ExplicacionProfesorTexto { get; set; }
        
        /// <summary>
        /// Explicación detallada del profesor en formato HTML con estilos profesionales
        /// </summary>
        public string? ExplicacionProfesorHTML { get; set; }
        
        /// <summary>
        /// Quiz educativo generado por AI basado en el contenido del capítulo
        /// </summary>
        public List<PreguntaQuiz>? Quiz { get; set; } = new List<PreguntaQuiz>();
        
        /// <summary>
        /// Ejemplos prácticos generados por AI para ayudar al estudiante
        /// </summary>
        public List<EjemploPractico>? Ejemplos { get; set; } = new List<EjemploPractico>();

        // Timestamps se generan automáticamente en el backend
        // public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        // public DateTime? FechaUltimaModificacion { get; set; }

        // Arrays de archivos/documentos (inicialmente vacíos)
        // public List<NotebookCapitulo> Notebooks { get; set; } = new List<NotebookCapitulo>();
        // public List<DocumentoCapitulo> Documentos { get; set; } = new List<DocumentoCapitulo>();
    }

    /// <summary>
    /// Clase para representar una pregunta del quiz generada por AI
    /// </summary>
    public class PreguntaQuiz
    {
        public string Pregunta { get; set; } = string.Empty;
        public List<string> Opciones { get; set; } = new List<string>();
        public string RespuestaCorrecta { get; set; } = string.Empty;
        public string Explicacion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Clase para representar un ejemplo práctico generado por AI
    /// </summary>
    public class EjemploPractico
    {
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Aplicacion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Información de un capítulo
    /// </summary>
    public class ChapterInfo
    {
        public string ChapterName { get; set; } = string.Empty;
        public int StartPage { get; set; }
        public int EndPage { get; set; }
    }

    public class DocumentoClassRequest
    {
        /// <summary>
        /// Nombre personalizado del curso (opcional)
        /// </summary>
        public string? Nombre { get; set; }

        /// <summary>
        /// Descripción del contenido del documento
        /// </summary>
        public string? Descripcion { get; set; }

        /// <summary>
        /// Notas personales del usuario
        /// </summary>
        public string? Notas { get; set; }

        /// <summary>
        /// Número total de páginas del documento
        /// </summary>
        public int? NumeroPaginas { get; set; }

        /// <summary>
        /// Indica si el documento tiene índice
        /// </summary>
        public bool TieneIndice { get; set; }

        /// <summary>
        /// Página donde comienza el índice (solo si TieneIndice = true)
        /// </summary>
        public int? PaginaInicioIndice { get; set; }

        public int? PaginaFinIndice { get; set; }

        // Información del archivo
        /// <summary>
        /// Nombre original del archivo
        /// </summary>
        public string NombreArchivo { get; set; } = string.Empty;

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string TipoArchivo { get; set; } = string.Empty;

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long TamanoArchivo { get; set; }

        public List<CapituloRequest> CapitulosAI { get; set; } = new List<CapituloRequest>();
    }

    public class DocumentClassPages
    {
        public int PageNumber { get; set; }
        public List<string> PageLines { get; set; } = new List<string>();
    }
}
