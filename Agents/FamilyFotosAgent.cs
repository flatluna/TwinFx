using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinFx.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.AI.OpenAI;
using Azure;

namespace TwinFx.Agents
{
    public class FamilyFotosAgent
    {
        private readonly ILogger<FamilyFotosAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly ChatClient _chatClient;
        private static readonly HttpClient client = new HttpClient();

        public FamilyFotosAgent(ILogger<FamilyFotosAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            try
            {
                // Configurar Azure OpenAI para análisis de imágenes
                var endpoint = _configuration["Values:AzureOpenAI:Endpoint"] ?? _configuration["AzureOpenAI:Endpoint"] ?? "";
                var apiKey = _configuration["Values:AzureOpenAI:ApiKey"] ?? _configuration["AzureOpenAI:ApiKey"] ?? "";
                var visionModelName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? _configuration["AzureOpenAI:DeploymentName"] ?? "gpt4mini";
                visionModelName = "gpt-5-mini";
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint and API key are required for photo analysis");
                }

                _azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _chatClient = _azureOpenAIClient.GetChatClient(visionModelName);

                _logger.LogInformation("✅ FamilyFotosAgent initialized successfully with model: {ModelName}", visionModelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize FamilyFotosAgent");
                throw;
            }
        }

        // Parameterless constructor for backward compatibility
        public FamilyFotosAgent()
        {
            // Create internal logger and configuration for this agent
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<FamilyFotosAgent>();
            
            // Initialize configuration with proper configuration sources
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();
            _configuration = configBuilder.Build();
            
            try
            {
                // Configurar Azure OpenAI para análisis de imágenes
                var endpoint = _configuration["Values:AzureOpenAI:Endpoint"] ?? _configuration["AzureOpenAI:Endpoint"] ?? "";
                var apiKey = _configuration["Values:AzureOpenAI:ApiKey"] ?? _configuration["AzureOpenAI:ApiKey"] ?? "";
                var visionModelName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? _configuration["AzureOpenAI:DeploymentName"] ?? "gpt4mini";
                
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint and API key are required for photo analysis");
                }

                _azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _chatClient = _azureOpenAIClient.GetChatClient(visionModelName);

                _logger.LogInformation("✅ FamilyFotosAgent initialized successfully with model: {ModelName}", visionModelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize FamilyFotosAgent");
                throw;
            }
        }

        /// <summary>
        /// Analiza una foto usando la URL SAS y genera una descripción detallada
        /// Retorna la información estructurada en el formato ImageAI esperado
        /// </summary>
        /// <param name="sasUrl">URL SAS de la foto en Data Lake</param>
        /// <param name="photoContext">Contexto de la memoria para personalizar el análisis</param>
        /// <returns>Objeto ImageAI con análisis completo de la imagen</returns>
        public async Task<ImageAI> AnalyzePhotoAsync(string sasUrl, PhotoFormData photoContext,
            string PromptName,
            string UserDescription)
        {
            _logger.LogInformation("🔍 Starting photo analysis for family photo");
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
                string analysisPrompt = "";
                if (PromptName == "Homes")
                {
                    analysisPrompt = CreatePhotoAnalysisPromptHomes(photoContext, UserDescription);
                }
                else
                {
                    analysisPrompt =    CreatePhotoAnalysisPrompt(photoContext, UserDescription);
                }
                    // Crear el prompt personalizado para el análisis
                    

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
                       if (PromptName == "Homes")
                        {

                        }
                    // Parsear la respuesta de IA y crear el objeto ImageAI estructurado
                    var imageAI = ParseAIResponseToImageAI(aiResponse, photoContext);
                    
                    _logger.LogInformation("✅ Photo analysis completed successfully for family photo");
                    return imageAI;
                }
                else
                {
                    throw new Exception("Empty response received from Azure OpenAI Vision");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing photo");

                // Retornar estructura básica en caso de error
                return new ImageAI
                {
                    DetailsHTML = $"<div style='color: red; padding: 20px;'>Error al analizar la imagen: {ex.Message}</div>",
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
        /// Crea el prompt personalizado para análisis de fotos basado en el contexto de PhotoFormData
        /// </summary>
        private string CreatePhotoAnalysisPrompt(PhotoFormData photoContext, string UserDescription)
        {
            var prompt = $@"
Eres un experto analista de imágenes especializado en crear descripciones detalladas y cálidas para fotos familiares. 

**CONTEXTO DE LA FOTO FAMILIAR:** 
- Descripción adicional: {photoContext.Description}
- Fecha tomada: {photoContext.DateTaken}
- Ubicación: {photoContext.Location}
- País: {photoContext.Country}
- Lugar específico: {photoContext.Place}
- Personas en la foto: {photoContext.PeopleInPhoto}
- Etiquetas: {photoContext.Tags}
- Categoría: {photoContext.Category}
- Tipo de evento: {photoContext.EventType}

**TU TAREA:**
Analiza esta imagen familiar y proporciona una respuesta en formato JSON que incluya:

1. **descripcionGenerica**: Una descripción breve y cálida (máximo 200 caracteres) que capture la esencia de la imagen familiar
2. **detailsHTML**: Un análisis completo en HTML LIMPIO (sin \n) con estilo CSS inline vibrante y colorido específico para fotos familiares
3. **descripcion_visual_detallada**: Un objeto JSON estructurado con personas, objetos, escenario, colores
4. **contexto_emocional**: Análisis emocional de la imagen familiar
5. **elementos_temporales**: Información temporal de la escena
6. **detalles_memorables**: Elementos que hacen especial esta imagen familiar

**INSTRUCCIONES CRÍTICAS PARA HTML:**
- NO uses caracteres \n - usa HTML limpio con tags apropiados
- HTML EXTENSO y DETALLADO específico para contexto familiar
- Usa colores VIBRANTES y diseño moderno
- Incluye listas HTML (<ul>, <ol>, <li>)
- Usa grids y flexbox para layouts atractivos
- Termina con una POESÍA sobre la foto familiar
- Enfócate en aspectos familiares: vínculos, emociones, momentos especiales
- Incorpora la información proporcionada del contexto familiar

**FORMATO DE RESPUESTA JSON:**
{{
  ""descripcionGenerica"": ""Descripción cálida de la foto familiar"",
  ""detailsHTML"": ""<div>HTML extenso y detallado para foto familiar</div>"",
  ""descripcion_visual_detallada"": {{
    ""personas"": {{
      ""cantidad"": 0,
      ""detalles"": []
    }},
    ""objetos"": [],
    ""escenario"": {{
      ""ubicacion"": ""{photoContext.Location}"",
      ""tipo_de_lugar"": ""{photoContext.Place}"",
      ""ambiente"": ""ambiente familiar detectado""
    }},
    ""colores"": {{
      ""paleta_dominante"": [],
      ""iluminacion"": ""iluminación detectada"",
      ""atmosfera"": ""atmósfera familiar""
    }}
  }},
  ""contexto_emocional"": {{
    ""estado_de_animo_percibido"": ""estado emocional familiar"",
    ""tipo_de_evento"": ""{photoContext.EventType}"",
    ""emociones_transmitidas_por_las_personas"": [],
    ""ambiente_general"": ""ambiente familiar general""
  }},
  ""elementos_temporales"": {{
    ""epoca_aproximada"": ""época basada en {photoContext.DateTaken}"",
    ""estacion_del_ano"": ""estación detectada"",
    ""momento_del_dia"": ""momento del día detectado""
  }},
  ""detalles_memorables"": {{
    ""elementos_unicos_o_especiales"": [],
    ""objetos_con_valor_sentimental"": [],
    ""caracteristicas_que_hacen_esta_foto_memorable"": [],
    ""contexto_que_puede_ayudar_a_recordar_el_momento"": ""contexto familiar específico""
  }}
}}

**INSTRUCCIONES ESPECÍFICAS:**
- El detailsHTML debe ser EXTENSO con múltiples secciones familiares
- Usa colores VIBRANTES: gradientes, azules, rosas, verdes, naranjas
- NO uses \n - solo HTML limpio con tags apropiados
- Incluye LISTAS HTML (<ul>, <ol>, <li>) para organizar información familiar
- Usa CSS Grid y Flexbox para layouts modernos
- Termina SIEMPRE con una poesía de 4 versos sobre la imagen familiar
- Conecta el análisis con el contexto familiar proporcionado
- Para colores, incluye códigos HEX específicos
- El análisis debe ser EXTENSO, cálido y personal
- Enfócate en vínculos familiares, emociones y momentos especiales

Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después.
";

            return prompt;
        }

        /// <summary>
        /// Crea el prompt personalizado para análisis de fotos de casas y espacios interiores/exteriores
        /// </summary>
        private string CreatePhotoAnalysisPromptHomes(PhotoFormData photoContext, string UserDescription)
        {
            var prompt = $@"
Eres un experto analista de imágenes especializado en describir espacios residenciales, decoración de interiores y arquitectura doméstica.

**CONTEXTO DE LA FOTO DE CASA:** 
- Descripción adicional: {photoContext.Description}
- Fecha tomada: {photoContext.DateTaken}
- Ubicación: {photoContext.Location}
- País: {photoContext.Country}
- Lugar específico: {photoContext.Place}
- Etiquetas: {photoContext.Tags}
- Categoría: {photoContext.Category}
- Tipo de evento: {photoContext.EventType}
- Descripción del usuario: {UserDescription}

**TU TAREA:**
Analiza esta imagen de espacio residencial y proporciona una respuesta en formato JSON que incluya:

1. **descripcionGenerica**: Una descripción breve y técnica (máximo 200 caracteres) del espacio arquitectónico
2. **detailsHTML**: Un análisis completo en HTML LIMPIO (sin \n) con estilo CSS inline específico para espacios residenciales
3. **analisis_arquitectonico**: Análisis detallado de elementos arquitectónicos y de diseño
4. **elementos_decorativos**: Catalogación de muebles, decoración y elementos estéticos
5. **analisis_espacial**: Información sobre distribución, iluminación y funcionalidad del espacio
6. **caracteristicas_tecnicas**: Aspectos técnicos como materiales, acabados, instalaciones

**INSTRUCCIONES CRÍTICAS PARA HTML:**
- NO uses caracteres \n - usa HTML limpio con tags apropiados
- HTML EXTENSO y DETALLADO específico para espacios residenciales
- Usa colores PROFESIONALES relacionados con arquitectura: grises, blancos, marrones, azules
- Incluye listas HTML (<ul>, <ol>, <li>) para catalogar elementos
- Usa grids y flexbox para layouts tipo catálogo de diseño
- Termina con una REFLEXIÓN sobre el diseño del espacio
- Enfócate en: arquitectura, decoración, funcionalidad, estilo, materiales
- Incorpora terminología técnica de diseño de interiores y arquitectura

**FORMATO DE RESPUESTA JSON:**
{{
  ""descripcionGenerica"": ""Descripción técnica del espacio residencial"",
  ""detailsHTML"": ""<div>HTML extenso y detallado para análisis arquitectónico</div>"",
  ""analisis_arquitectonico"": {{
    ""tipo_de_espacio"": ""sala/cocina/dormitorio/baño/exterior/etc"",
    ""estilo_arquitectonico"": ""moderno/clásico/contemporáneo/minimalista/etc"",
    ""elementos_estructurales"": [
      {{
        ""elemento"": ""nombre del elemento"",
        ""material"": ""material utilizado"",
        ""descripcion"": ""descripción detallada"",
        ""estado"": ""nuevo/usado/renovado/etc""
      }}
    ],
    ""distribucion"": ""análisis de la distribución espacial"",
    ""accesibilidad"": ""características de accesibilidad""
  }},
  ""elementos_decorativos"": {{
    ""mobiliario"": [
      {{
        ""tipo"": ""sofá/mesa/silla/cama/etc"",
        ""material"": ""madera/metal/tela/etc"",
        ""color"": ""color dominante con código HEX"",
        ""estilo"": ""moderno/vintage/clásico/etc"",
        ""ubicacion"": ""posición en el espacio"",
        ""estado"": ""condición del mueble""
      }}
    ],
    ""decoracion"": [
      {{
        ""elemento"": ""cuadro/planta/alfombra/cortina/etc"",
        ""descripcion"": ""descripción detallada"",
        ""material_color"": ""material y color"",
        ""funcion"": ""decorativa/funcional/ambas""
      }}
    ],
    ""textiles"": [
      {{
        ""tipo"": ""cortinas/alfombras/cojines/etc"",
        ""material"": ""algodón/lana/sintético/etc"",
        ""patron"": ""liso/estampado/texturado"",
        ""colores"": [""#codigo1"", ""#codigo2""]
      }}
    ]
  }},
  ""analisis_espacial"": {{
    ""iluminacion"": {{
      ""tipo_principal"": ""natural/artificial/mixta"",
      ""fuentes_luz"": [""ventanas"", ""lámparas"", ""focos""],
      ""calidad"": ""excelente/buena/regular/deficiente"",
      ""ambiente_creado"": ""acogedor/profesional/relajante/etc""
    }},
    ""colores_dominantes"": [
      {{
        ""color"": ""nombre del color"",
        ""hex"": ""#código"",
        ""superficie"": ""paredes/suelo/techo/muebles"",
        ""porcentaje_aproximado"": ""30%/50%/etc""
      }}
    ],
    ""sensacion_espacial"": ""amplio/acogedor/minimalista/recargado/etc"",
    ""funcionalidad"": ""evaluación de la funcionalidad del espacio"",
    ""flujo_circulacion"": ""análisis del movimiento en el espacio""
  }},
  ""caracteristicas_tecnicas"": {{
    ""suelos"": {{
      ""material"": ""madera/cerámica/mármol/etc"",
      ""acabado"": ""brillante/mate/texturado"",
      ""patron"": ""tablones/baldosas/continuo"",
      ""estado"": ""nuevo/conservado/desgastado""
    }},
    ""paredes"": {{
      ""acabado"": ""pintura/papel/revestimiento/ladrillo"",
      ""color_base"": ""color con código HEX"",
      ""textura"": ""lisa/rugosa/con relieve"",
      ""detalles"": ""molduras/zócalos/marcos""
    }},
    ""techo"": {{
      ""altura_aproximada"": ""alta/media/baja"",
      ""acabado"": ""liso/con vigas/decorativo"",
      ""iluminacion_integrada"": ""sí/no"",
      ""elementos_especiales"": ""ventiladores/claraboyas/etc""
    }},
    ""ventanas_puertas"": [
      {{
        ""tipo"": ""ventana/puerta"",
        ""material"": ""madera/aluminio/PVC"",
        ""estilo"": ""moderna/clásica/rústica"",
        ""tratamiento"": ""cortinas/persianas/desnuda""
      }}
    ],
    ""instalaciones_visibles"": [
      ""electricidad"", ""climatización"", ""fontanería"", ""etc""
    ]
  }},
  ""evaluacion_general"": {{
    ""nivel_mantenimiento"": ""excelente/bueno/regular/deficiente"",
    ""coherencia_estilo"": ""muy coherente/coherente/mixto/incoherente"",
    ""aprovechamiento_espacio"": ""óptimo/bueno/mejorable/deficiente"",
    ""valor_estetico"": ""muy alto/alto/medio/bajo"",
    ""habitabilidad"": ""excelente/buena/aceptable/mejorable""
  }}
}}

**INSTRUCCIONES ESPECÍFICAS:**
- El detailsHTML debe ser EXTENSO con secciones técnicas de arquitectura/decoración
- Usa colores PROFESIONALES: #2C3E50, #34495E, #7F8C8D, #BDC3C7, #ECF0F1
- NO uses \n - solo HTML limpio con tags apropiados
- Incluye LISTAS HTML (<ul>, <ol>, <li>) para catalogar elementos
- Usa CSS Grid y Flexbox para layouts tipo catálogo profesional
- Termina SIEMPRE con una reflexión técnica sobre el diseño del espacio
- Conecta el análisis con el contexto de ubicación proporcionado
- Para colores, SIEMPRE incluye códigos HEX específicos
- El análisis debe ser TÉCNICO, detallado y profesional
- Enfócate en: arquitectura, materiales, funcionalidad, estética, habitabilidad
- Identifica estilos arquitectónicos y de decoración específicos
- Evalúa calidad de acabados y estado de conservación

Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después.
";

            return prompt;
        }

        /// <summary>
        /// Crea una respuesta mock para testing mientras se configura Azure OpenAI Vision
        /// </summary>
        private string CreateMockAIResponse(PhotoFormData photoContext, string userDescription)
        {
            return JsonSerializer.Serialize(new
            {
                descripcionGenerica = $"Una hermosa foto familiar capturada en {photoContext.Location} que muestra {userDescription}",
                detailsHTML = $@"<div style='font-family: ""Inter"", ""Segoe UI"", sans-serif; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; padding: 30px 20px;'>
<div style='max-width: 1000px; margin: 0 auto; background: white; border-radius: 20px; box-shadow: 0 20px 60px rgba(0,0,0,0.15); padding: 40px;'>
<header style='text-align: center; margin-bottom: 40px;'>
<h1 style='margin: 0; font-size: 36px; font-weight: 800; background: linear-gradient(45deg, #ff6b6b, #4ecdc4, #45b7d1); -webkit-background-clip: text; -webkit-text-fill-color: transparent;'>📸 Análisis de Foto Familiar</h1>
<p style='margin: 10px 0 0; color: #666; font-size: 18px;'>Capturando momentos especiales en familia</p>
</header>
<section style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; border-radius: 15px; padding: 25px; margin-bottom: 30px;'>
<h2 style='margin: 0 0 15px; font-size: 24px;'>🏠 Descripción Familiar</h2>
<p style='margin: 0; line-height: 1.6;'>{userDescription} - Una imagen que refleja la calidez y unión familiar en {photoContext.Location}.</p>
</section>
<section style='background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); color: white; border-radius: 15px; padding: 25px; margin-bottom: 30px;'>
<h2 style='margin: 0 0 15px; font-size: 24px;'>👨‍👩‍👧‍👦 Contexto Familiar</h2>
<ul style='margin: 0; padding-left: 20px;'>
<li>Fecha: {photoContext.DateTaken}</li>
<li>Lugar: {photoContext.Location}, {photoContext.Country}</li>
<li>Personas: {photoContext.PeopleInPhoto}</li>
<li>Evento: {photoContext.EventType}</li>
<li>Categoría: {photoContext.Category}</li>
</ul>
</section>
<footer style='background: linear-gradient(135deg, #ff9a9e 0%, #fecfef 100%); color: #333; border-radius: 15px; padding: 30px; text-align: center;'>
<h2 style='margin: 0 0 20px; font-size: 28px;'>💕 Poesía Familiar</h2>
<div style='font-style: italic; font-size: 18px; line-height: 1.8;'>
<p>En esta imagen se refleja el amor,</p>
<p>Momentos únicos llenos de calor,</p>
<p>La familia unida en armonía,</p>
<p>Creando recuerdos que duran toda la vida.</p>
</div>
</footer>
</div>
</div>",
                descripcion_visual_detallada = new
                {
                    personas = new
                    {
                        cantidad = 0,
                        detalles = new object[0]
                    },
                    objetos = new object[0],
                    escenario = new
                    {
                        ubicacion = photoContext.Location,
                        tipo_de_lugar = photoContext.Place,
                        ambiente = "ambiente familiar acogedor"
                    },
                    colores = new
                    {
                        paleta_dominante = new object[0],
                        iluminacion = "iluminación natural cálida",
                        atmosfera = "atmósfera familiar alegre"
                    }
                },
                contexto_emocional = new
                {
                    estado_de_animo_percibido = "alegría familiar",
                    tipo_de_evento = photoContext.EventType,
                    emociones_transmitidas_por_las_personas = new string[0],
                    ambiente_general = "ambiente familiar positivo"
                },
                elementos_temporales = new
                {
                    epoca_aproximada = $"época contemporánea - {photoContext.DateTaken}",
                    estacion_del_ano = "no determinable",
                    momento_del_dia = "no especificado"
                },
                detalles_memorables = new
                {
                    elementos_unicos_o_especiales = new string[0],
                    objetos_con_valor_sentimental = new object[0],
                    caracteristicas_que_hacen_esta_foto_memorable = new string[0],
                    contexto_que_puede_ayudar_a_recordar_el_momento = $"Momento familiar especial en {photoContext.Location}"
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> GoolgSearch(string Question)
        {
            string apiKey = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo"; // Replace with your API key  
            string searchEngineId = "b07503c9152af4456"; // Replace with your Search Engine ID  
            string query = Question; // Replace with your search query  
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

        private ImageAI ParseAIResponseToImageAI(string aiResponse, PhotoFormData photoContext)
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
                        Escenario = new Escenario { Ubicacion = photoContext.Location ?? "No especificado", TipoDeLugar = photoContext.Place ?? "", Ambiente = "Determinado por contexto" },
                        Colores = new Colores { PaletaDominante = new List<Color>(), Iluminacion = "No determinada", Atmosfera = "No determinada" }
                    },
                    ContextoEmocional = new ContextoEmocional
                    {
                        EstadoDeAnimoPercibido = "No determinado",
                        TipoDeEvento = photoContext.EventType ?? "",
                        EmocionesTrasmitidasPorLasPersonas = new List<string>(),
                        AmbienteGeneral = "No analizado"
                    },
                    ElementosTemporales = new ElementosTemporales
                    {
                        EpocaAproximada = photoContext.DateTaken ?? "No determinada",
                        EstacionDelAno = "No identificable",
                        MomentoDelDia = "No identificable"
                    },
                    DetallesMemorables = new DetallesMemorables
                    {
                        ElementosUnicosOEspeciales = new List<string>(),
                        ObjetosConValorSentimental = new List<ObjetoSentimental>(),
                        CaracteristicasQueHacenEstaFotoMemorable = new List<string>(),
                        ContextoQuePuedeAyudarARecordarElMomento = $"Foto familiar en {photoContext.Location}"
                    },
                    id = null
                };
            }
        }

        // Helper methods for parsing JSON elements - copy from MiMemoriaAgent
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
