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
• Idioma: {cursoRequest.Curso?.Idioma ?? "No especificado"}
• Precio: {cursoRequest.Curso?.Precio ?? "No especificado"}

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

        /// <summary>
        /// Procesa la actualización inteligente de un curso con análisis y recomendaciones AI
        /// </summary>
        /// <param name="cursoRequest">Datos del curso a actualizar</param>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>Respuesta inteligente con análisis del curso actualizado</returns>
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
• Idioma: {cursoRequest.Curso?.Idioma ?? "No especificado"}
• Precio: {cursoRequest.Curso?.Precio ?? "No especificado"}

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
        public string HtmlDetails { get; set; }
        public string TextDetails { get; set; }
    }

    public class  CapituloRequest
    {
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

        // Timestamps se generan automáticamente en el backend
        // public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        // public DateTime? FechaUltimaModificacion { get; set; }

        // Arrays de archivos/documentos (inicialmente vacíos)
        // public List<NotebookCapitulo> Notebooks { get; set; } = new List<NotebookCapitulo>();
        // public List<DocumentoCapitulo> Documentos { get; set; } = new List<DocumentoCapitulo>();
    }
}
