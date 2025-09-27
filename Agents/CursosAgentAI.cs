using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Text.Json;
using TwinFx.Services;
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

    public CursosAgentAI(ILogger<CursosAgentAI> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        _logger.LogInformation("🚗🤖 CarsAgentAI initialized for intelligent document processing");
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

    public async Task<List<DocumentoTextoContenido>> ExtarctDataFromClass(string IndexData, List<DocumentPage> documentPages)
    {
        try
        {
            _logger.LogInformation("🚗📄 Starting text extraction based on index data");

            // Deserializar el índice JSON
            DocumentoClase documento = JsonConvert.DeserializeObject<DocumentoClase>(IndexData);
            
            if (documento?.Indice == null || documento.Indice.Count == 0)
            {
                _logger.LogWarning("⚠️ No index data found in IndexData");
                 return new List<DocumentoTextoContenido>();
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
           await ProduceAiClassFromDocument(documentoContenidos);


            return documentoContenidos;
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

            return new List<DocumentoTextoContenido>();
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