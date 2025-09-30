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
        public async Task<CursoCreadoAI> BuildCursoAsync(CursoBuildData buildData, string TwinID)
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
                // Apply the policy to the HttpClient  
                builder.Services.AddHttpClient("SemanticKernelClient")
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                    .ConfigureHttpClient(client =>
                    {
                        client.Timeout = TimeSpan.FromMinutes(5);
                    });
                var endpoint = Environment.GetEnvironmentVariable("AzureOpenAI:Endpoint") ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                var apiKey = Environment.GetEnvironmentVariable("AzureOpenAI:ApiKey") ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
                var deployment = "gpt-5-mini";
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint or api key not configured in environment variables");
                }

                builder.AddAzureOpenAIChatCompletion(deploymentName: deployment, endpoint: endpoint, apiKey: apiKey);
                var kernel = builder.Build();

                var CursoSeCreo = await RunCursoAsync((Kernel)kernel);
                CursoSeCreo.TwinID = TwinID;
                CursoSeCreo.id = Guid.NewGuid().ToString();
                await SaveCursoBuildAsync(CursoSeCreo);

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
        public async Task<CursoCreadoAI> RunCursoAsync(Kernel kernel)
        {
            if (kernel == null) throw new ArgumentNullException(nameof(kernel));
            BingSearch bing = new BingSearch(_loggerSearch!, _configuration!);
            // Build the prompt according to the user's specification
            var topicsText = (ListaTopicos != null && ListaTopicos.Any()) ? string.Join(", ", ListaTopicos) : "";
            var SearchResult = await bing.BingSearchGlobalAsync("Buscame toda la informacion posible sobr este tem " +
                " esto creando un curso en linea ayudame a enconbtrar informacion detallada el tema es " +
                "" +  Descripcion + " Temas: " + topicsText + " Importante contesta con este idioma :" +
                " : " + Idioma);
            var responsePrompt = $@"Eres un experto en Nombre = {NombreClase}. Usa esta Descripcion para crear un curso detallado con todo tu conocimiento.

Descripcion:
{Descripcion}
Usa este idioma para crear toda tu respuesta:  Idioma = {Idioma}
Esto encontre en internet que te puede ayudar a crear el curso : {SearchResult.Respuesta}
 Adiciona todo el conocimiento que tienes de lo que has aprendido por anos mucho detalle dame has lo maximo
 Crea un total maximo de  {CantidadPaginas} páginas.

Quiero que lo crees con estos temas: {topicsText}

1)Importante primero vas a crear una lista de temas basado en lo que se te pidio pero extiendelo a unos 10 temas mas 
elabora el libro basado en esos 10 temas o mas piensa que el estudianet quiere un curso ocmpleto. 

Quiero que el curso este con mucho detalle casi como si fuera un libro usa todo tu conocimiento y experiencia.
las paginas que te pidan si es posible de por lo menos 30 lineas por pagina.
INSTRUCCIONES: 
- Genera un capitulo por cada tema. 
- Genera un índice con  los capítulos, cada capítulo con objetivos claros, contenido, ejemplos prácticos y un resumen.
- Distribuye el contenido considerando un total aproximado de {CantidadPaginas} páginas entre los capítulos.
- Usa un lenguaje claro y profesional, todo en español.
- Responde SOLO con el contenido del curso en formato JSON que incluya al menos: nombreClase, descripcion, capacitulos (lista con título, objetivos, contenido, ejemplos, resumen), duracionEstimada, y etiquetas.
- En cada capitulo quiero un por lo menos unos de 3 a 10 preguntas dependiengo de la ocmplejidad del tem pero quiero muchas preguntas no solo unacon preguntas de opción múltiple, respuestas correctas y explicaciones.
- Imortante siempre dale al estidante 4 opciones para responder el quiz todas las preguntas rwquirem, opciones, 
respuesta correcta y la explicacion. Importante esto
- IMPORTANTE: Quiero el Contenido texto identido en un formato HTML dentro de ContenidoHTML. QUiero que uses
colores, grids, listas, fotos, imagenes, imogis, etc. Que se vea super profesional.
Este es un ejemplo de como quiero la respuesta en JSON:
- Este es maximo numero de capitulos que puedes crear  {CantidadCapitulos}.
   {{  
    ""NombreClase"": ""Cocina Mexicana"",  
    ""Descripcion"": ""Aprende a preparar platillos tradicionales de la cocina mexicana, desde salsas hasta postres."",  
    ""DuracionEstimada"": ""15 horas"",  
    ""Etiquetas"": [  
        ""cocina"",  
        ""mexicana"",  
        ""recetas"",  
        ""gastronomía""  
    ],   
    ""Capitulos"": [  
        {{  
            ""Titulo"": ""Introducción a la Cocina Mexicana"",  
            ""Objetivos"": [  
                ""Conocer la historia de la cocina mexicana."",  
                ""Identificar los ingredientes básicos utilizados en la cocina mexicana."",  
                ""Aprender técnicas de cocina esenciales.""  
            ],  
            ""Contenido"": ""En este capítulo se explora la rica historia de la cocina mexicana, así como los ingredientes fundamentales que la componen."",  
            ""ContenidoHTML"":""<div>"",
            ""Ejemplos"": [  
                ""Uso del maíz en diferentes platillos."",  
                ""Importancia del chile en la gastronomía mexicana.""  
            ],  
            ""Resumen"": ""Se presentó una visión general de la cocina mexicana y sus ingredientes clave."",  
            ""Pagina"": 1,  
            ""Quizes"": [  
                {{  
                    ""Pregunta"": ""¿Cuál es un ingrediente básico en la cocina mexicana?"",  
                    ""Opciones"": [  
                        ""a) Arroz"",  
                        ""b) Maíz"",  
                        ""c) Pasta"",  
                        ""d) Quinoa""  
                    ],  
                    ""RespuestaCorrecta"": ""b) Maíz"",  
                    ""Explicacion"": ""El maíz es fundamental en la cocina mexicana y se utiliza en muchos platillos, como tortillas y tamales.""  
                }}  
            ]  
        }},  
        {{  
            ""Titulo"": ""Salsas y Guarniciones"",  
            ""Objetivos"": [  
                ""Aprender a preparar salsas tradicionales."",  
                ""Conocer las guarniciones más comunes en la mesa mexicana.""  
            ],  
            ""Contenido"": ""Se detallan las recetas de salsas como el guacamole y la salsa verde, así como guarniciones típicas."",  
            ""Ejemplos"": [  
                ""Receta de guacamole."",  
                ""Preparación de salsa roja.""  
            ],  
            ""Resumen"": ""Se aprendieron las técnicas para hacer salsas y guarniciones esenciales en la cocina mexicana."",  
            ""Pagina"": 5,  
            ""Quizes"": [  
                {{  
                    ""Pregunta"": ""¿Qué salsa se utiliza comúnmente en los tacos?"",  
                    ""Opciones"": [  
                        ""a) Salsa de soya"",  
                        ""b) Salsa verde"",  
                        ""c) Salsa barbacoa"",  
                        ""d) Salsa de mango""  
                    ],  
                    ""RespuestaCorrecta"": ""b) Salsa verde"",  
                    ""Explicacion"": ""La salsa verde es una de las salsas más populares en los tacos, hecha a base de tomatillos.""  
                }}  
            ]  
        }},  
        {{  
            ""Titulo"": ""Platillos Principales"",  
            ""Objetivos"": [  
                ""Preparar platillos típicos como tacos y enchiladas."",  
                ""Comprender la variedad de sabores en los platillos mexicanos.""  
            ],  
            ""Contenido"": ""Este capítulo se centra en la elaboración de platillos como tacos al pastor y enchiladas."",  
            ""Ejemplos"": [  
                ""Receta de tacos al pastor."",  
                ""Preparación de enchiladas rojas.""  
            ],  
            ""Resumen"": ""Se exploraron recetas de platillos principales que son fundamentales en la cocina mexicana."",  
            ""Pagina"": 10,  
            ""Quizes"": [  
                {{  
                    ""Pregunta"": ""¿Cuál de los siguientes platillos es típico de la cocina mexicana?"",  
                    ""Opciones"": [  
                        ""a) Sushi"",  
                        ""b) Pizza"",  
                        ""c) Tacos"",  
                        ""d) Croissant""  
                    ],  
                    ""RespuestaCorrecta"": ""c) Tacos"",  
                    ""Explicacion"": ""Los tacos son un platillo tradicional mexicano que consiste en una tortilla rellena de diversos ingredientes.""  
                }}  
            ]  
        }},  
        {{  
            ""Titulo"": ""Postres Mexicanos"",  
            ""Objetivos"": [  
                ""Aprender a preparar postres tradicionales."",  
                ""Descubrir la variedad de dulces en la cultura mexicana.""  
            ],  
            ""Contenido"": ""Se presentan recetas de postres como flan y tres leches, así como la importancia de los dulces en las festividades."",  
            ""Ejemplos"": [  
                ""Receta de flan."",  
                ""Preparación de pastel de tres leches.""  
            ],  
            ""Resumen"": ""Se culmina el curso con la elaboración de deliciosos postres mexicanos."",  
            ""Pagina"": 15,  
            ""Quizes"": [  
                {{  
                    ""Pregunta"": ""¿Cuál es un postre típico mexicano?"",  
                    ""Opciones"": [  
                        ""a) Flan"",  
                        ""b) Cheesecake"",  
                        ""c) Tiramisú"",  
                        ""d) Brownie""  
                    ],  
                    ""RespuestaCorrecta"": ""a) Flan"",  
                    ""Explicacion"": ""El flan es un postre tradicional en México, hecho a base de huevo y leche, muy popular en las celebraciones.""  
                }}  
            ]  
        }}  
    ]  
}}  
"; try
            {

                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(responsePrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_completion_tokens"] = 20000,

                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    kernel);



                var aiResponse = response.Content ?? "{}";
                CursoBuildData curso = new CursoBuildData();
                curso.ListaTopicos = this.ListaTopicos;
                curso.CantidadPaginas = this.CantidadPaginas;
                // Deserializar el JSON a un objeto CursoBuildData  
                var cursoCreado = JsonConvert.DeserializeObject<CursoCreadoAI>(aiResponse);
                string Objetivos = "";
                foreach (var capitulo in cursoCreado.Capitulos)
                {
                    foreach (var objetivo in capitulo.Objetivos)
                    {
                        Objetivos = Objetivos + objetivo;
                    }

                }

                Objetivos = " : Search for information related to this AI generated clas which has this objetives " +
                      "find me a list of links to learn more about the topic : "

                      + curso.Descripcion + "  Training Objetives : " + Objetivos + 
                      " contesta con este idioma : " + Idioma;
                var SearchResultNew = await bing.BingSearchAsync(Objetivos);
                cursoCreado.CursosInternet = SearchResultNew;
                return cursoCreado;
            }
            catch(Exception ex)
            {
                return null;
            }
        }

        // Convenience wrapper to match requested method name OPENAI
        public Task<CursoCreadoAI> OPENAI(Kernel kernel) => RunCursoAsync(kernel);

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
                var cosmosSettings = Microsoft.Extensions.Options.Options.Create(new TwinFx.Models.CosmosDbSettings
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
    }


    //////////////

}
