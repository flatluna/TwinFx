using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TwinFx.Agents;
using TwinFx.Functions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Services
{
    public class PhotosAI
    {
        private readonly ILogger<FamilyFotosAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly ChatClient _chatClient;
        private static readonly HttpClient client = new HttpClient();

        public PhotosAI(ILogger<FamilyFotosAgent> logger, IConfiguration configuration)
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
        public PhotosAI()
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
        public async Task<AnalisisResidencial> AnalyzePhotoAsync(string sasUrl, PhotoFormData photoContext,
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

                    var ImageResponse = JsonConvert.DeserializeObject<AnalisisResidencial>(aiResponse);
                    // Parsear la respuesta de IA y crear el objeto ImageAI estructurado


                    _logger.LogInformation("✅ Photo analysis completed successfully for family photo");
                    return ImageResponse;
                }
                else
                {
                    throw new Exception("Empty response received from Azure OpenAI Vision");
                }
            }
            catch (Exception ex)
            {
                AnalisisResidencial NoExist = new AnalisisResidencial();
                NoExist.DescripcionGenerica = "No se pudo analizar la imagen.";
                return NoExist;
            }
        }
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
7. Importante trata de extraer toda l ainformacion que se te pide en el JSON y esa misma 
copiala en el html pero con formaot html visual ,colorido, con titulos oclores.
COPIA TODO 

**INSTRUCCIONES CRÍTICAS PARA HTML:**
- NO uses caracteres \n - usa HTML limpio con tags apropiados
- HTML EXTENSO y DETALLADO específico para espacios residenciales
- Usa colores PROFESIONALES relacionados con arquitectura: grises, blancos, marrones, azules
- Incluye listas HTML (<ul>, <ol>, <li>) para catalogar elementos
- Usa grids y flexbox para layouts tipo catálogo de diseño
- Termina con una REFLEXIÓN sobre el diseño del espacio
- Enfócate en: arquitectura, decoración, funcionalidad, estilo, materiales
- Incorpora terminología técnica de diseño de interiores y arquitectura
- Basado en los muebles , espacios y alturas calcula los metros cuadrados del lugar no inventes
- es importante me des un tipo_de_espacio aun si es un guess
- so no saves deja los espacios en blanco en el json pero contesta todo el json 
solo has un calculo aproximado

**FORMATO DE RESPUESTA JSON:**
{{
  ""descripcionGenerica"": ""Descripción técnica del espacio residencial"",
  ""detailsHTML"": ""<div>HTML extenso y detallado para análisis arquitectónico</div>"",
  ""analisis_arquitectonico"": {{
    ""tipo_de_espacio"": ""sala/cocina/dormitorio/baño/exterior/etc"",
    ""estilo_arquitectonico"": ""moderno/clásico/contemporáneo/minimalista/etc"",
    ""TotalMetrosCuadrados"": ""Área aproximada en metros cuadrados"",
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
IMPORTANTE: No inventes nada, si no ves muebles o cuadros u objetos no existen no los inventes
solo describe lo que ves eso es todo.

Responde ÚNICAMENTE con el JSON válido, sin texto adicional antes o después.
";

            return prompt;
        }


    }  
  
public class AnalisisResidencial
    {

        [JsonProperty("id")]
        public string? id { get; set; }


        [JsonProperty("TwinID")]
        public string? TwinID { get; set; }


        [JsonProperty("fileName")]
        public string? FileName { get; set; }


        [JsonProperty("filePath")]
        public string? FilePath { get; set; }


        [JsonProperty("fileURL")]
        public string? FileURL { get; set; }

        [JsonProperty("descripcionGenerica")]
        public string? DescripcionGenerica { get; set; }

        [JsonProperty("detailsHTML")]
        public string? DetailsHtml { get; set; }

        [JsonProperty("analisis_arquitectonico")]
        public AnalisisArquitectonico? AnalisisArquitectonico { get; set; }

        [JsonProperty("elementos_decorativos")]
        public ElementosDecorativos? ElementosDecorativos { get; set; }

        [JsonProperty("analisis_espacial")]
        public AnalisisEspacial? AnalisisEspacial { get; set; }

        [JsonProperty("caracteristicas_tecnicas")]
        public CaracteristicasTecnicas? CaracteristicasTecnicas { get; set; }

        [JsonProperty("evaluacion_general")]
        public EvaluacionGeneral? EvaluacionGeneral { get; set; }
    }

    public class AnalisisResidencialSummary
    {

        [JsonProperty("id")]
        public string? id { get; set; }


        [JsonProperty("TwinID")]
        public string? TwinID { get; set; }


        [JsonProperty("TipoEspacio")]
        public string? TipoEspacio { get; set; }


        [JsonProperty("fileName")]
        public string? FileName { get; set; }


        [JsonProperty("filePath")]
        public string? FilePath { get; set; }


        [JsonProperty("fileURL")]
        public string? FileURL { get; set; }

        [JsonProperty("descripcionGenerica")]
        public string? DescripcionGenerica { get; set; }

        [JsonProperty("detailsHTML")]
        public string? DetailsHtml { get; set; } 
    }
    public class AnalisisArquitectonico
    {
        [JsonProperty("tipo_de_espacio")]
        public string? TipoDeEspacio { get; set; }

        [JsonProperty("estilo_arquitectonico")]
        public string? EstiloArquitectonico { get; set; }

        [JsonProperty("elementos_estructurales")]
        public List<ElementoEstructural>? ElementosEstructurales { get; set; }

        [JsonProperty("distribucion")]
        public string? Distribucion { get; set; }

        [JsonProperty("accesibilidad")]
        public string? Accesibilidad { get; set; }

        [JsonProperty("TotalMetrosCuadrados")]
        public string? TotalMetrosCuadrados { get; set; } 
    }

    public class ElementoEstructural
    {
        [JsonProperty("elemento")]
        public string? Elemento { get; set; }

        [JsonProperty("material")]
        public string? Material { get; set; }

        [JsonProperty("descripcion")]
        public string? Descripcion { get; set; }

        [JsonProperty("estado")]
        public string? Estado { get; set; }
    }

    public class ElementosDecorativos
    {
        [JsonProperty("mobiliario")]
        public List<MobiliarioItem>? Mobiliario { get; set; }

        [JsonProperty("decoracion")]
        public List<DecoracionItem>? Decoracion { get; set; }

        [JsonProperty("textiles")]
        public List<TextilItem>? Textiles { get; set; }
    }

    public class MobiliarioItem
    {
        [JsonProperty("tipo")]
        public string? Tipo { get; set; }

        [JsonProperty("material")]
        public string? Material { get; set; }

        [JsonProperty("color")]
        public string? Color { get; set; }

        [JsonProperty("estilo")]
        public string? Estilo { get; set; }

        [JsonProperty("ubicacion")]
        public string? Ubicacion { get; set; }

        [JsonProperty("estado")]
        public string? Estado { get; set; }
    }

    public class DecoracionItem
    {
        [JsonProperty("elemento")]
        public string? Elemento { get; set; }

        [JsonProperty("descripcion")]
        public string? Descripcion { get; set; }

        [JsonProperty("material_color")]
        public string? MaterialColor { get; set; }

        [JsonProperty("funcion")]
        public string? Funcion { get; set; }
    }

    public class TextilItem
    {
        [JsonProperty("tipo")]
        public string? Tipo { get; set; }

        [JsonProperty("material")]
        public string? Material { get; set; }

        [JsonProperty("patron")]
        public string? Patron { get; set; }

        [JsonProperty("colores")]
        public List<string>? Colores { get; set; }
    }

    public class AnalisisEspacial
    {
        [JsonProperty("iluminacion")]
        public Iluminacion? Iluminacion { get; set; }

        [JsonProperty("colores_dominantes")]
        public List<ColorDominante>? ColoresDominantes { get; set; }

        [JsonProperty("sensacion_espacial")]
        public string? SensacionEspacial { get; set; }

        [JsonProperty("funcionalidad")]
        public string? Funcionalidad { get; set; }

        [JsonProperty("flujo_circulacion")]
        public string? FlujoCirculacion { get; set; }
    }

    public class Iluminacion
    {
        [JsonProperty("tipo_principal")]
        public string? TipoPrincipal { get; set; }

        [JsonProperty("fuentes_luz")]
        public List<string>? FuentesLuz { get; set; }

        [JsonProperty("calidad")]
        public string? Calidad { get; set; }

        [JsonProperty("ambiente_creado")]
        public string? AmbienteCreado { get; set; }
    }

    public class ColorDominante
    {
        [JsonProperty("color")]
        public string? Color { get; set; }

        [JsonProperty("hex")]
        public string? Hex { get; set; }

        [JsonProperty("superficie")]
        public string? Superficie { get; set; }

        [JsonProperty("porcentaje_aproximado")]
        public string? PorcentajeAproximado { get; set; }
    }

    public class CaracteristicasTecnicas
    {
        [JsonProperty("suelos")]
        public Suelos? Suelos { get; set; }

        [JsonProperty("paredes")]
        public Paredes? Paredes { get; set; }

        [JsonProperty("techo")]
        public Techo? Techo { get; set; }

        [JsonProperty("ventanas_puertas")]
        public List<VentanaPuerta>? VentanasPuertas { get; set; }

        [JsonProperty("instalaciones_visibles")]
        public List<string>? InstalacionesVisibles { get; set; }
    }

    public class Suelos
    {
        [JsonProperty("material")]
        public string? Material { get; set; }

        [JsonProperty("acabado")]
        public string? Acabado { get; set; }

        [JsonProperty("patron")]
        public string? Patron { get; set; }

        [JsonProperty("estado")]
        public string? Estado { get; set; }
    }

    public class Paredes
    {
        [JsonProperty("acabado")]
        public string? Acabado { get; set; }

        [JsonProperty("color_base")]
        public string? ColorBase { get; set; }

        [JsonProperty("textura")]
        public string? Textura { get; set; }

        [JsonProperty("detalles")]
        public string? Detalles { get; set; }
    }

    public class Techo
    {
        [JsonProperty("altura_aproximada")]
        public string? AlturaAproximada { get; set; }

        [JsonProperty("acabado")]
        public string? Acabado { get; set; }

        [JsonProperty("iluminacion_integrada")]
        public string? IluminacionIntegrada { get; set; }

        [JsonProperty("elementos_especiales")]
        public string? ElementosEspeciales { get; set; }
    }

    public class VentanaPuerta
    {
        [JsonProperty("tipo")]
        public string? Tipo { get; set; }

        [JsonProperty("material")]
        public string? Material { get; set; }

        [JsonProperty("estilo")]
        public string? Estilo { get; set; }

        [JsonProperty("tratamiento")]
        public string? Tratamiento { get; set; }
    }

    public class EvaluacionGeneral
    {
        [JsonProperty("nivel_mantenimiento")]
        public string? NivelMantenimiento { get; set; }

        [JsonProperty("coherencia_estilo")]
        public string? CoherenciaEstilo { get; set; }

        [JsonProperty("aprovechamiento_espacio")]
        public string? AprovechamientoEspacio { get; set; }

        [JsonProperty("valor_estetico")]
        public string? ValorEstetico { get; set; }

        [JsonProperty("habitabilidad")]
        public string? Habitabilidad { get; set; }
    }
}
