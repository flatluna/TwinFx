using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using TwinFx.Functions; // Para acceder a MiMemoria y modelos relacionados

namespace TwinFx.Agents
{
    /// <summary>
    /// Agent para análisis de fotos de memorias usando Azure OpenAI Vision
    /// Genera descripciones detalladas e información estructurada de las imágenes
    /// </summary>
    public class MiMemoriaAgent
    {
        private static readonly HttpClient client = new HttpClient();
        private readonly ILogger<MiMemoriaAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly ChatClient _chatClient;

        public MiMemoriaAgent(ILogger<MiMemoriaAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Configurar Azure OpenAI para análisis de imágenes
                var endpoint = _configuration["Values:AzureOpenAI:Endpoint"] ?? _configuration["AzureOpenAI:Endpoint"] ?? "";
                var apiKey = _configuration["Values:AzureOpenAI:ApiKey"] ?? _configuration["AzureOpenAI:ApiKey"] ?? "";
                var visionModelName = _configuration["Values:AZURE_OPENAI_VISION_MODEL"] ?? _configuration["AZURE_OPENAI_VISION_MODEL"] ?? "gpt-4o-mini";
                visionModelName = "gpt-5-mini"; // Temporalmente forzar uso de gpt5mini hasta que gpt-4o-mini soporte imágenes
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint and API key are required for photo analysis");
                }

                _azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _chatClient = _azureOpenAIClient.GetChatClient(visionModelName);

                _logger.LogInformation("✅ MiMemoriaAgent initialized successfully with model: {ModelName}", visionModelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize MiMemoriaAgent");
                throw;
            }
        }

        /// <summary>
        /// Analiza una foto usando la URL SAS y genera una descripción detallada
        /// Retorna la información estructurada en el formato ImageAI esperado
        /// </summary>
        /// <param name="sasUrl">URL SAS de la foto en Data Lake</param>
        /// <param name="memoriaContext">Contexto de la memoria para personalizar el análisis</param>
        /// <returns>Objeto ImageAI con análisis completo de la imagen</returns>
        public async Task<ImageAI> AnalyzePhotoAsync(string sasUrl, MiMemoria memoriaContext,
            string UserDescription)
        {
            _logger.LogInformation("🔍 Starting photo analysis for memory: {MemoriaId} - {Titulo}", memoriaContext.id, memoriaContext.Titulo);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validar entrada
                if (string.IsNullOrEmpty(sasUrl))
                {
                    throw new ArgumentException("SAS URL is required for photo analysis");
                }

                // Descargar la imagen usando la URL SAS
                byte[] imageBytes;
                string mediaType = "image/jpeg";
                
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    var httpResponse = await httpClient.GetAsync(sasUrl);
                    
                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        throw new Exception($"Failed to download image from SAS URL: HTTP {httpResponse.StatusCode}");
                    }

                    imageBytes = await httpResponse.Content.ReadAsByteArrayAsync();
                    mediaType = httpResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                }

                if (imageBytes.Length == 0)
                {
                    throw new Exception("Downloaded image is empty");
                }

                _logger.LogInformation("📸 Image downloaded successfully: {Size} bytes, Type: {MediaType}", imageBytes.Length, mediaType);

                // Crear el prompt personalizado para el análisis
                var analysisPrompt = CreatePhotoAnalysisPrompt(memoriaContext, UserDescription);

                // Crear mensaje con imagen para Azure OpenAI Vision
                var imageMessage = ChatMessage.CreateUserMessage(
                    ChatMessageContentPart.CreateTextPart(analysisPrompt),
                    ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mediaType)
                );

                _logger.LogInformation("🤖 Sending image to Azure OpenAI Vision for analysis...");

                // Configurar opciones del chat
                var chatOptions = new ChatCompletionOptions
                {
                     
                };

                // Llamar a Azure OpenAI Vision API
                var visionResponse = await _chatClient.CompleteChatAsync(new[] { imageMessage }, chatOptions);

                if (visionResponse?.Value?.Content?.Count > 0)
                {
                    var aiResponse = visionResponse.Value.Content[0].Text;
                    var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    _logger.LogInformation("✅ Received response from Azure OpenAI Vision, length: {Length} chars in {ProcessingTime}ms", 
                        aiResponse.Length, processingTime);

                    // Parsear la respuesta de IA y crear el objeto ImageAI estructurado
                    var imageAI = ParseAIResponseToImageAI(aiResponse, memoriaContext);
                    
                    _logger.LogInformation("✅ Photo analysis completed successfully for memory: {MemoriaId}", memoriaContext.id);
                    return imageAI;
                }
                else
                {
                    throw new Exception("Empty response received from Azure OpenAI Vision");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing photo for memory: {MemoriaId}", memoriaContext.id);
                
                // Retornar estructura básica en caso de error
                return new ImageAI
                {
                    DetailsHTML = $"<div>Error al analizar la imagen: {ex.Message}</div>",
                    DescripcionGenerica = "Error en el análisis de la imagen",
                    DescripcionVisualDetallada = new DescripcionVisualDetallada
                    {
                        Personas = new Personas { Cantidad = 0, Detalles = new List<DetallePersona>() },
                        Objetos = new List<Objeto>(),
                        Escenario = new Escenario { Ubicacion = "No determinado", TipoDeLugar = "Error", Ambiente = "No analizado" },
                        Colores = new Colores { PaletaDominante = new List<Color>(), Iluminacion = "No determinada", Atmosfera = "No determinada" }
                    },
                    ContextoEmocional = new ContextoEmocional 
                    { 
                        EstadoDeAnimoPercibido = "Error en análisis", 
                        TipoDeEvento = "Error",
                        EmocionesTrasmitidasPorLasPersonas = new List<string>(),
                        AmbienteGeneral = "No analizado"
                    },
                    ElementosTemporales = new ElementosTemporales
                    {
                        EpocaAproximada = "No determinada",
                        EstacionDelAno = "No identificable",
                        MomentoDelDia = "No identificable"
                    },
                    DetallesMemorables = new DetallesMemorables
                    {
                        ElementosUnicosOEspeciales = new List<string>(),
                        ObjetosConValorSentimental = new List<ObjetoSentimental>(),
                        CaracteristicasQueHacenEstaFotoMemorable = new List<string>(),
                        ContextoQuePuedeAyudarARecordarElMomento = "Error en el análisis"
                    },
                    id = null
                };
            }
        }

        /// <summary>
        /// Crea el prompt personalizado para análisis de fotos basado en el contexto de la memoria
        /// </summary>
        private string CreatePhotoAnalysisPrompt(MiMemoria memoriaContext,   string UserDescription)
        {
            var prompt = @"
Eres un experto analista de imágenes especializado en crear descripciones detalladas y cálidas para memorias personales. 

**CONTEXTO DE LA MEMORIA:**
-  Importane: Usa esta descripcion de la foto del usuario e incluyela en ttu descripcion en 
el HTML y en todas partes. Por ejmeplo nombrs personas, animales, cosas, colroes, etc. **** " 
+ UserDescription +@" **** FIN DESCRIPCION USUARIO ####
- Título: " + memoriaContext.Titulo + @"
- Categoría: " + memoriaContext.Categoria + @"
- Contenido: " + memoriaContext.Contenido + @"
- Fecha: " + memoriaContext.Fecha + @"
- Ubicación: " + (memoriaContext.Ubicacion ?? "No especificada") + @"
- Personas mencionadas: " + (memoriaContext.Personas?.Any() == true ? string.Join(", ", memoriaContext.Personas) : "Ninguna") + @"
- Etiquetas: " + (memoriaContext.Etiquetas?.Any() == true ? string.Join(", ", memoriaContext.Etiquetas) : "Ninguna") + @",

**TU TAREA:**
Analiza esta imagen y proporciona una respuesta en formato JSON que incluya:

1. **descripcionGenerica**: Una descripción breve y cálida (máximo 200 caracteres) que capture la esencia de la imagen
2. **detailsHTML**: Un análisis completo en HTML LIMPIO (sin \n) con estilo CSS inline vibrante y colorido
3. **descripcion_visual_detallada**: Un objeto JSON estructurado con personas, objetos, escenario, colores
4. **contexto_emocional**: Análisis emocional de la imagen
5. **elementos_temporales**: Información temporal de la escena
6. **detalles_memorables**: Elementos que hacen especial esta imagen

**INSTRUCCIONES CRÍTICAS PARA HTML:**
- NO uses caracteres \n - usa HTML limpio con tags apropiados
- HTML EXTENSO y DETALLADO con múltiples secciones
- Usa colores VIBRANTES y diseño moderno
- Incluye listas HTML (<ul>, <ol>, <li>)
- Usa grids y flexbox para layouts atractivos
- Termina con una POESÍA sobre la foto
- Máximo detalle sobre todo lo que ves en la imagen

**EJEMPLO COMPLETO DEL FORMATO ESPERADO:**
{
  ""descripcionGenerica"": ""Un robot 3D blanco sentado con ojos azules que transmite cercanía tecnológica y ternura en un fondo minimalista que resalta su figura amigable."",
  ""detailsHTML"": ""<div style='font-family: \""Inter\"", \""Segoe UI\"", sans-serif; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; padding: 30px 20px;'><div style='max-width: 1000px; margin: 0 auto; background: white; border-radius: 20px; box-shadow: 0 20px 60px rgba(0,0,0,0.15); padding: 40px; position: relative; overflow: hidden;'><div style='position: absolute; top: -50%; right: -50%; width: 100%; height: 100%; background: radial-gradient(circle, rgba(102,126,234,0.1) 0%, transparent 70%);'></div><header style='text-align: center; margin-bottom: 40px; position: relative; z-index: 2;'><h1 style='margin: 0; font-size: 36px; font-weight: 800; background: linear-gradient(45deg, #ff6b6b, #4ecdc4, #45b7d1); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text;'>📸 Análisis Visual Completo</h1><p style='margin: 10px 0 0; color: #666; font-size: 18px; font-weight: 500;'>Descripción detallada y cálida de tu memoria</p><div style='width: 100px; height: 4px; background: linear-gradient(90deg, #ff6b6b, #4ecdc4); margin: 20px auto; border-radius: 2px;'></div></header><div style='display: grid; grid-template-columns: 1fr 1fr; gap: 30px; margin-bottom: 30px;'><section style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; border-radius: 15px; padding: 25px; box-shadow: 0 10px 30px rgba(102,126,234,0.3);'><h2 style='margin: 0 0 15px; font-size: 24px; display: flex; align-items: center;'><span style='margin-right: 10px;'>🎨</span>Descripción General</h2><p style='margin: 0; line-height: 1.6; font-size: 16px;'>[DESCRIPCIÓN DETALLADA Y EXTENSA DE LA IMAGEN - MÍNIMO 3 ORACIONES]</p></section><section style='background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); color: white; border-radius: 15px; padding: 25px; box-shadow: 0 10px 30px rgba(240,147,251,0.3);'><h2 style='margin: 0 0 15px; font-size: 24px; display: flex; align-items: center;'><span style='margin-right: 10px;'>👥</span>Personas y Sujetos</h2><p style='margin: 0; line-height: 1.6; font-size: 16px;'>[DESCRIPCIÓN DETALLADA DE PERSONAS O SUJETOS PRINCIPALES]</p></section></div><section style='background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%); color: white; border-radius: 15px; padding: 25px; margin-bottom: 30px; box-shadow: 0 10px 30px rgba(79,172,254,0.3);'><h2 style='margin: 0 0 20px; font-size: 24px; display: flex; align-items: center;'><span style='margin-right: 10px;'>🧩</span>Objetos y Elementos Visuales</h2><ul style='margin: 0; padding-left: 0; list-style: none; display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px;'><li style='background: rgba(255,255,255,0.2); padding: 15px; border-radius: 10px; backdrop-filter: blur(10px);'><strong style='display: block; margin-bottom: 5px;'>Objeto Principal:</strong>[DESCRIPCIÓN DEL OBJETO PRINCIPAL]</li><li style='background: rgba(255,255,255,0.2); padding: 15px; border-radius: 10px; backdrop-filter: blur(10px);'><strong style='display: block; margin-bottom: 5px;'>Elementos Secundarios:</strong>[DESCRIPCIÓN DE ELEMENTOS SECUNDARIOS]</li></ul></section><div style='display: grid; grid-template-columns: 1fr 1fr; gap: 30px; margin-bottom: 30px;'><section style='background: linear-gradient(135deg, #fa709a 0%, #fee140 100%); color: white; border-radius: 15px; padding: 25px; box-shadow: 0 10px 30px rgba(250,112,154,0.3);'><h2 style='margin: 0 0 15px; font-size: 24px; display: flex; align-items: center;'><span style='margin-right: 10px;'>🏠</span>Ambiente y Escenario</h2><p style='margin: 0; line-height: 1.6; font-size: 16px;'>[DESCRIPCIÓN DETALLADA DEL AMBIENTE Y ESCENARIO]</p></section><section style='background: linear-gradient(135deg, #a8edea 0%, #fed6e3 100%); color: #333; border-radius: 15px; padding: 25px; box-shadow: 0 10px 30px rgba(168,237,234,0.3);'><h2 style='margin: 0 0 15px; font-size: 24px; display: flex; align-items: center; color: #333;'><span style='margin-right: 10px;'>🎨</span>Paleta de Colores</h2><ul style='margin: 0; padding-left: 0; list-style: none;'><li style='margin-bottom: 10px; padding: 10px; background: rgba(255,255,255,0.7); border-radius: 8px; display: flex; align-items: center;'><div style='width: 20px; height: 20px; background: #FFFFFF; border-radius: 50%; margin-right: 10px; border: 2px solid #ddd;'></div><strong>[COLOR DOMINANTE]</strong></li><li style='margin-bottom: 10px; padding: 10px; background: rgba(255,255,255,0.7); border-radius: 8px;'><strong>Iluminación:</strong> [TIPO DE ILUMINACIÓN]</li><li style='padding: 10px; background: rgba(255,255,255,0.7); border-radius: 8px;'><strong>Atmósfera:</strong> [DESCRIPCIÓN DE ATMÓSFERA]</li></ul></section></div><section style='background: linear-gradient(135deg, #ffecd2 0%, #fcb69f 100%); color: #333; border-radius: 15px; padding: 25px; margin-bottom: 30px; box-shadow: 0 10px 30px rgba(255,236,210,0.3);'><h2 style='margin: 0 0 20px; font-size: 24px; display: flex; align-items: center;'><span style='margin-right: 10px;'>🌟</span>Contexto Emocional y Temporal</h2><div style='display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 20px;'><div style='background: rgba(255,255,255,0.8); padding: 20px; border-radius: 12px;'><h3 style='margin: 0 0 10px; color: #e74c3c; font-size: 18px;'>😊 Estado Emocional</h3><p style='margin: 0; font-size: 14px;'>[ESTADO EMOCIONAL PERCIBIDO]</p></div><div style='background: rgba(255,255,255,0.8); padding: 20px; border-radius: 12px;'><h3 style='margin: 0 0 10px; color: #3498db; font-size: 18px;'>⏰ Época</h3><p style='margin: 0; font-size: 14px;'>[ÉPOCA APROXIMADA]</p></div><div style='background: rgba(255,255,255,0.8); padding: 20px; border-radius: 12px;'><h3 style='margin: 0 0 10px; color: #f39c12; font-size: 18px;'>🌅 Momento</h3><p style='margin: 0; font-size: 14px;'>[MOMENTO DEL DÍA]</p></div></div></section><section style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; border-radius: 15px; padding: 25px; margin-bottom: 30px; box-shadow: 0 10px 30px rgba(102,126,234,0.3);'><h2 style='margin: 0 0 20px; font-size: 24px; display: flex; align-items: center;'><span style='margin-right: 10px;'>✨</span>Elementos Memorables</h2><ol style='margin: 0; padding-left: 20px; line-height: 1.8; font-size: 16px;'><li style='margin-bottom: 10px;'>[ELEMENTO MEMORABLE 1]</li><li style='margin-bottom: 10px;'>[ELEMENTO MEMORABLE 2]</li><li style='margin-bottom: 10px;'>[ELEMENTO MEMORABLE 3]</li></ol></section><footer style='background: linear-gradient(135deg, #ff9a9e 0%, #fecfef 100%); color: #333; border-radius: 15px; padding: 30px; text-align: center; box-shadow: 0 10px 30px rgba(255,154,158,0.3);'><h2 style='margin: 0 0 20px; font-size: 28px; display: flex; align-items: center; justify-content: center;'><span style='margin-right: 10px;'>🎭</span>Poesía de la Memoria</h2><div style='font-style: italic; font-size: 18px; line-height: 1.8; max-width: 600px; margin: 0 auto;'><p style='margin: 0 0 15px;'>[VERSO POÉTICO 1 - DESCRIBE LA ESENCIA DE LA IMAGEN]</p><p style='margin: 0 0 15px;'>[VERSO POÉTICO 2 - CONECTA CON LA MEMORIA]</p><p style='margin: 0 0 15px;'>[VERSO POÉTICO 3 - EMOCIONES QUE TRANSMITE]</p><p style='margin: 0; font-weight: 600; color: #e74c3c;'>[VERSO FINAL - MENSAJE INSPIRADOR]</p></div></footer></div></div>"",
  ""descripcion_visual_detallada"": {
    ""personas"": {
      ""cantidad"": 0,
      ""detalles"": []
    },
    ""objetos"": [
      {
        ""tipo"": ""robot_3d"",
        ""descripcion"": ""Figura robótica antropomorfa de acabado blanco brillante con articulaciones visibles""
      }
    ],
    ""escenario"": {
      ""ubicacion"": ""estudio digital controlado"",
      ""tipo_de_lugar"": ""escena minimalista con fondo neutro"",
      ""ambiente"": ""limpio, moderno y tecnológico sin distracciones""
    },
    ""colores"": {
      ""paleta_dominante"": [
        {
          ""nombre"": ""blanco brillante"",
          ""hex"": ""#FFFFFF""
        },
        {
          ""nombre"": ""azul luminoso"",
          ""hex"": ""#3498DB""
        }
      ],
      ""iluminacion"": ""iluminación uniforme y suave que resalta las formas"",
      ""atmosfera"": ""moderna, pulcra y amigable con toque futurista""
    }
  },
  ""contexto_emocional"": {
    ""estado_de_animo_percibido"": ""amistoso, curioso y sereno"",
    ""tipo_de_evento"": ""presentación tecnológica o render artístico"",
    ""emociones_transmitidas_por_las_personas"": [],
    ""ambiente_general"": ""acogedor dentro de un contexto tecnológico""
  },
  ""elementos_temporales"": {
    ""epoca_aproximada"": ""contemporánea con estilo 3D moderno"",
    ""estacion_del_ano"": ""no identificable por ser imagen digital"",
    ""momento_del_dia"": ""no identificable por iluminación controlada""
  },
  ""detalles_memorables"": {
    ""elementos_unicos_o_especiales"": [
      ""ojos azules luminosos que actúan como punto focal emocional"",
      ""acabado blanco brillante con texturas realistas"",
      ""pose de saludo que transmite cercanía""
    ],
    ""objetos_con_valor_sentimental"": [],
    ""caracteristicas_que_hacen_esta_foto_memorable"": [
      ""la simplicidad compositiva que enfatiza la figura principal"",
      ""la pose amigable que facilita conexión emocional"",
      ""el contraste perfecto entre figura y fondo""
    ],
    ""contexto_que_puede_ayudar_a_recordar_el_momento"": ""imagen representativa de avances en diseño 3D y robótica amigable""
  }
}

**INSTRUCCIONES ESPECÍFICAS:**
- El detailsHTML debe ser EXTENSO con múltiples secciones detalladas
- Usa colores VIBRANTES: gradientes, azules, rosas, verdes, naranjas
- NO uses \n - solo HTML limpio con tags apropiados
- Incluye LISTAS HTML (<ul>, <ol>, <li>) para organizar información
- Usa CSS Grid y Flexbox para layouts modernos
- Termina SIEMPRE con una poesía de 4 versos sobre la imagen
- Conecta el análisis con el contexto de la memoria cuando sea relevante
- Para colores, incluye códigos HEX específicos
- El análisis debe ser EXTENSO, cálido y personal
- IMPORTANTE: Reemplaza TODOS los placeholders [DESCRIPCIÓN...] con contenido real basado en la imagen

Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después.
";

            return prompt;
        }

        /// <summary>
        /// Parsea la respuesta de IA y crea el objeto ImageAI estructurado
        /// </summary>
        /// 
        public  async Task<string> GoolgSearch(string Question)
        {
            string apiKey = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo"; // Replace with your API key  
            string searchEngineId = "b07503c9152af4456"; // Replace with your Search Engine ID  
            string query =  Question; // Replace with your search query  
            string Response = "";
            string url = $"https://www.googleapis.com/customsearch/v1?key={apiKey}&cx={searchEngineId}&q={Uri.EscapeDataString(query)}";

            try
            {
                var response = await client.GetStringAsync(url);
                Console.WriteLine(response);
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("Request error: " + e.Message);
            }

            return Response;
        }
        private ImageAI ParseAIResponseToImageAI(string aiResponse, MiMemoria memoriaContext)
        {
            try
            {
                _logger.LogDebug("🔍 Parsing AI response to ImageAI structure...");

                // Intentar parsear directamente como JSON
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(aiResponse);

                var imageAI = new ImageAI
                {
                    DescripcionGenerica = jsonResponse.GetProperty("descripcionGenerica").GetString() ?? "",
                    DetailsHTML = jsonResponse.GetProperty("detailsHTML").GetString() ?? "",
                    DescripcionVisualDetallada = ParseDescripcionVisualDetallada(jsonResponse.GetProperty("descripcion_visual_detallada")),
                    ContextoEmocional = ParseContextoEmocional(jsonResponse),
                    ElementosTemporales = ParseElementosTemporales(jsonResponse),
                    DetallesMemorables = ParseDetallesMemorables(jsonResponse),
                    id = null
                };

                _logger.LogDebug("✅ Successfully parsed AI response to ImageAI structure");
                return imageAI;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to parse AI response as JSON, creating fallback structure");
                
                // Crear estructura de fallback con el contenido de la respuesta
                return new ImageAI
                {
                    DescripcionGenerica = aiResponse.Length > 200 ? aiResponse.Substring(0, 200) + "..." : aiResponse,
                    DetailsHTML = $"<div style='font-family: \"Segoe UI\", Roboto, Arial, sans-serif; padding:20px; color:#333;'><p>{aiResponse}</p></div>",
                    DescripcionVisualDetallada = new DescripcionVisualDetallada
                    {
                        Personas = new Personas { Cantidad = 0, Detalles = new List<DetallePersona>() },
                        Objetos = new List<Objeto> { new Objeto { Tipo = "imagen", Descripcion = "Imagen analizada" } },
                        Escenario = new Escenario { Ubicacion = memoriaContext.Ubicacion ?? "No especificado", TipoDeLugar = memoriaContext.Categoria, Ambiente = "Determinado por contexto" },
                        Colores = new Colores { PaletaDominante = new List<Color>(), Iluminacion = "No determinada", Atmosfera = "No determinada" }
                    },
                    ContextoEmocional = new ContextoEmocional 
                    { 
                        EstadoDeAnimoPercibido = "No determinado", 
                        TipoDeEvento = "Análisis fallido",
                        EmocionesTrasmitidasPorLasPersonas = new List<string>(),
                        AmbienteGeneral = "No analizado"
                    },
                    ElementosTemporales = new ElementosTemporales
                    {
                        EpocaAproximada = "No determinada",
                        EstacionDelAno = "No identificable",
                        MomentoDelDia = "No identificable"
                    },
                    DetallesMemorables = new DetallesMemorables
                    {
                        ElementosUnicosOEspeciales = new List<string>(),
                        ObjetosConValorSentimental = new List<ObjetoSentimental>(),
                        CaracteristicasQueHacenEstaFotoMemorable = new List<string>(),
                        ContextoQuePuedeAyudarARecordarElMomento = "No disponible"
                    },
                    id = null
                };
            }
        }

        /// <summary>
        /// Parsea la descripción visual detallada del JSON de respuesta
        /// </summary>
        private DescripcionVisualDetallada ParseDescripcionVisualDetallada(JsonElement jsonElement)
        {
            try
            {
                var descripcion = new DescripcionVisualDetallada();

                // Parsear personas
                if (jsonElement.TryGetProperty("personas", out var personasElement))
                {
                    descripcion.Personas = new Personas
                    {
                        Cantidad = personasElement.GetProperty("cantidad").GetInt32(),
                        Detalles = new List<DetallePersona>()
                    };

                    if (personasElement.TryGetProperty("detalles", out var detallesElement))
                    {
                        foreach (var detalle in detallesElement.EnumerateArray())
                        {
                            descripcion.Personas.Detalles.Add(new DetallePersona
                            {
                                Rol = detalle.GetProperty("rol").GetString() ?? "",
                                EdadAproximada = detalle.GetProperty("edad_aproximada").GetString() ?? "",
                                Expresion = detalle.GetProperty("expresion").GetString() ?? "",
                                Vestimenta = detalle.GetProperty("vestimenta").GetString() ?? "",
                                Pose = detalle.GetProperty("pose").GetString() ?? ""
                            });
                        }
                    }
                }

                // Parsear objetos
                if (jsonElement.TryGetProperty("objetos", out var objetosElement))
                {
                    descripcion.Objetos = new List<Objeto>();
                    foreach (var objeto in objetosElement.EnumerateArray())
                    {
                        descripcion.Objetos.Add(new Objeto
                        {
                            Tipo = objeto.GetProperty("tipo").GetString() ?? "",
                            Descripcion = objeto.GetProperty("descripcion").GetString() ?? ""
                        });
                    }
                }

                // Parsear escenario
                if (jsonElement.TryGetProperty("escenario", out var escenarioElement))
                {
                    descripcion.Escenario = new Escenario
                    {
                        Ubicacion = escenarioElement.GetProperty("ubicacion").GetString() ?? "",
                        TipoDeLugar = escenarioElement.GetProperty("tipo_de_lugar").GetString() ?? "",
                        Ambiente = escenarioElement.GetProperty("ambiente").GetString() ?? ""
                    };
                }

                // Parsear colores (con propiedades extendidas)
                if (jsonElement.TryGetProperty("colores", out var coloresElement))
                {
                    descripcion.Colores = new Colores 
                    { 
                        PaletaDominante = new List<Color>(),
                        Iluminacion = coloresElement.TryGetProperty("iluminacion", out var ilumElement) ? ilumElement.GetString() ?? "" : "",
                        Atmosfera = coloresElement.TryGetProperty("atmosfera", out var atmosElement) ? atmosElement.GetString() ?? "" : ""
                    };
                    
                    if (coloresElement.TryGetProperty("paleta_dominante", out var paletaElement))
                    {
                        foreach (var color in paletaElement.EnumerateArray())
                        {
                            descripcion.Colores.PaletaDominante.Add(new Color
                            {
                                Nombre = color.GetProperty("nombre").GetString() ?? "",
                                Hex = color.GetProperty("hex").GetString() ?? ""
                            });
                        }
                    }
                }

                return descripcion;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing detailed visual description, returning default structure");
                return new DescripcionVisualDetallada
                {
                    Personas = new Personas { Cantidad = 0, Detalles = new List<DetallePersona>() },
                    Objetos = new List<Objeto>(),
                    Escenario = new Escenario(),
                    Colores = new Colores { PaletaDominante = new List<Color>() }
                };
            }
        }

        /// <summary>
        /// Parsea el contexto emocional del JSON de respuesta
        /// </summary>
        private ContextoEmocional ParseContextoEmocional(JsonElement jsonResponse)
        {
            try
            {
                if (jsonResponse.TryGetProperty("contexto_emocional", out var contextoElement))
                {
                    var contexto = new ContextoEmocional
                    {
                        EstadoDeAnimoPercibido = contextoElement.GetProperty("estado_de_animo_percibido").GetString() ?? "",
                        TipoDeEvento = contextoElement.GetProperty("tipo_de_evento").GetString() ?? "",
                        AmbienteGeneral = contextoElement.GetProperty("ambiente_general").GetString() ?? "",
                        EmocionesTrasmitidasPorLasPersonas = new List<string>()
                    };

                    if (contextoElement.TryGetProperty("emociones_transmitidas_por_las_personas", out var emocionesElement))
                    {
                        foreach (var emocion in emocionesElement.EnumerateArray())
                        {
                            contexto.EmocionesTrasmitidasPorLasPersonas.Add(emocion.GetString() ?? "");
                        }
                    }

                    return contexto;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing contexto emocional");
            }

            return new ContextoEmocional 
            { 
                EstadoDeAnimoPercibido = "No determinado", 
                TipoDeEvento = "No especificado",
                EmocionesTrasmitidasPorLasPersonas = new List<string>(),
                AmbienteGeneral = "No analizado"
            };
        }

        /// <summary>
        /// Parsea los elementos temporales del JSON de respuesta
        /// </summary>
        private ElementosTemporales ParseElementosTemporales(JsonElement jsonResponse)
        {
            try
            {
                if (jsonResponse.TryGetProperty("elementos_temporales", out var temporalesElement))
                {
                    return new ElementosTemporales
                    {
                        EpocaAproximada = temporalesElement.GetProperty("epoca_aproximada").GetString() ?? "",
                        EstacionDelAno = temporalesElement.GetProperty("estacion_del_ano").GetString() ?? "",
                        MomentoDelDia = temporalesElement.GetProperty("momento_del_dia").GetString() ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing elementos temporales");
            }

            return new ElementosTemporales
            {
                EpocaAproximada = "No determinada",
                EstacionDelAno = "No identificable",
                MomentoDelDia = "No identificable"
            };
        }

        /// <summary>
        /// Parsea los detalles memorables del JSON de respuesta
        /// </summary>
        private DetallesMemorables ParseDetallesMemorables(JsonElement jsonResponse)
        {
            try
            {
                if (jsonResponse.TryGetProperty("detalles_memorables", out var detallesElement))
                {
                    var detalles = new DetallesMemorables
                    {
                        ElementosUnicosOEspeciales = new List<string>(),
                        ObjetosConValorSentimental = new List<ObjetoSentimental>(),
                        CaracteristicasQueHacenEstaFotoMemorable = new List<string>(),
                        ContextoQuePuedeAyudarARecordarElMomento = detallesElement.TryGetProperty("contexto_que_puede_ayudar_a_recordar_el_momento", out var contextoElement) ? contextoElement.GetString() ?? "" : ""
                    };

                    // Parsear elementos únicos
                    if (detallesElement.TryGetProperty("elementos_unicos_o_especiales", out var unicosElement))
                    {
                        foreach (var elemento in unicosElement.EnumerateArray())
                        {
                            detalles.ElementosUnicosOEspeciales.Add(elemento.GetString() ?? "");
                        }
                    }

                    // Parsea objetos con valor sentimental
                    if (detallesElement.TryGetProperty("objetos_con_valor_sentimental", out var sentimentalElement))
                    {
                        foreach (var objeto in sentimentalElement.EnumerateArray())
                        {
                            detalles.ObjetosConValorSentimental.Add(new ObjetoSentimental
                            {
                                Objeto = objeto.GetProperty("objeto").GetString() ?? "",
                                PosibleValor = objeto.GetProperty("posible_valor").GetString() ?? ""
                            });
                        }
                    }

                    // Parsear características memorables
                    if (detallesElement.TryGetProperty("caracteristicas_que_hacen_esta_foto_memorable", out var caracteristicasElement))
                    {
                        foreach (var caracteristica in caracteristicasElement.EnumerateArray())
                        {
                            detalles.CaracteristicasQueHacenEstaFotoMemorable.Add(caracteristica.GetString() ?? "");
                        }
                    }

                    return detalles;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing detalles memorables");
            }

            return new DetallesMemorables
            {
                ElementosUnicosOEspeciales = new List<string>(),
                ObjetosConValorSentimental = new List<ObjetoSentimental>(),
                CaracteristicasQueHacenEstaFotoMemorable = new List<string>(),
                ContextoQuePuedeAyudarARecordarElMomento = "No disponible"
            };
        }
    }
}
