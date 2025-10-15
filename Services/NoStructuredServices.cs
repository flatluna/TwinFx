using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinFx.Agents;
using TwinFx.Services;

namespace TwinFx.Services
{
    public class NoStructuredServices
    {
        public List<CapituloExtraido> ExtaeCapitulos(DocumentoIndice documento, string containerName = "", string estructura = "no-estructurado", string subcategoria = "general")
        {
            var capitulosExtraidos = new List<CapituloExtraido>();

            try
            {
                if (documento == null || documento.Indice == null || documento.Indice.Count == 0)
                {
                    return capitulosExtraidos;
                }

                foreach (var indiceItem in documento.Indice)
                {
                    try
                    {
                        // Crear instancia de AiTokrens para calcular tokens
                        AiTokrens tokens = new AiTokrens();

                        // Crear un nuevo CapituloExtraido basado en IndiceItem
                        var capituloExtraido = new CapituloExtraido
                        {
                            // Datos básicos del capítulo
                            Titulo = indiceItem.Titulo,
                            NumeroCapitulo = indiceItem.NumeroCapitulo,
                            PaginaDe = indiceItem.PaginaDe,
                            PaginaA = indiceItem.PaginaA,
                            Nivel = indiceItem.Nivel,

                            // Contenido extraído del IndiceItem
                            TextoCompleto = indiceItem.Texto ?? string.Empty,
                            TextoCompletoHTML = indiceItem.TextoHTML ?? string.Empty,

                            // Generar resumen básico si no existe contenido específico
                            ResumenEjecutivo = !string.IsNullOrEmpty(indiceItem.Texto) 
                                ? GenerateBasicSummary(indiceItem.Texto) 
                                : $"Capítulo {indiceItem.NumeroCapitulo}: {indiceItem.Titulo}",

                            // Metadatos automáticos
                            TwinID = containerName,
                            CapituloID = Guid.NewGuid().ToString(),
                            Estructura = estructura,
                            Subcategoria = subcategoria,
                            ProcessedAt = DateTime.UtcNow,

                            // Generar DocumentID único para agrupar capítulos del mismo documento
                            DocumentID = DateTime.Now.ToFileTime() + "_" + Guid.NewGuid().ToString(),

                            // Calcular tokens usando AiTokrens
                            TotalTokens = tokens.GetTokenCount(indiceItem.Texto ?? ""),

                            // Inicializar lista vacía de preguntas frecuentes
                            PreguntasFrecuentes = new List<PreguntaFrecuente>()
                        };

                        capitulosExtraidos.Add(capituloExtraido);
                    }
                    catch (Exception)
                    {
                        // Continue processing other chapters if one fails
                        continue;
                    }
                }

                return capitulosExtraidos;
            }
            catch (Exception)
            {
                return capitulosExtraidos;
            }
        }
        public async Task<CapituloDocumento> ExtractCapituloDataWithAI(
            Kernel _kernel,
            CapituloIndice currentChapter,
            CapituloIndice nextChapter, int IniciaIndex, int TerminaIndex,
            List<DocumentPage> DocumentPages)
        {
            try
            {
                if (currentChapter == null || DocumentPages == null || DocumentPages.Count == 0)
                {
                    return null;
                }

                // PASO 1: Calcular el rango correcto de páginas del capítulo
                var chapterContent = new StringBuilder();
                var pagesToInclude = new List<DocumentPage>();

                // Determinar la página de inicio y fin del capítulo actual
                int startPage = currentChapter.PaginaDe;
                int endPage;

                if (nextChapter != null)
                {
                    // Si hay siguiente capítulo, terminar una página antes del siguiente
                    endPage = nextChapter.PaginaDe - 1;
                }
                else
                {
                    // Si es el último capítulo, incluir todas las páginas restantes
                    endPage = DocumentPages.Max(p => p.PageNumber);
                }

                // Encontrar páginas relevantes dentro del rango calculado
                foreach (var page in DocumentPages)
                {
                    if (page.PageNumber >= startPage && page.PageNumber <= endPage)
                    {
                        pagesToInclude.Add(page);
                    }
                }

                // PASO 2: Construir el contenido del capítulo
                foreach (var page in pagesToInclude)
                {
                    chapterContent.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                    if (page.LinesText != null && page.LinesText.Count > 0)
                    {
                        foreach (var line in page.LinesText)
                        {
                            chapterContent.AppendLine(line);
                        }
                    }
                }

                string allChapterContent = chapterContent.ToString();

                if (string.IsNullOrWhiteSpace(allChapterContent))
                {
                    return null;
                }

                var prompt = $@"Objetivo: Extraer los subtemas o secciones de un capítulo específico de un documento o libro.

REGLA CRITICA PRINCIPAL: DEBES INCLUIR TODO EL TEXTO DEL CAPITULO PALABRA POR PALABRA 
NADA PUEDE QUEDAR FUERA - CADA ORACION, CADA PARRAFO, CADA PALABRA DEBE ESTAR EN ALGUN SUBTEMA
SI EL CAPITULO TIENE 5000 PALABRAS, LOS SUBTEMAS JUNTOS DEBEN TENER 5000 PALABRAS

IMPORTANTE: Crea solo sub tema, o subsecciones que sean necesarias no inventes cosas analiza bien el Capitulo.
Contenido: Cada subtema debe incluir:
Un nombre adecuado.
Todo el texto del subtema.
Un título correspondiente.
Una breve descripción del subtema.

REGLAS FUNDAMENTALES PARA EL CONTENIDO:
1. COBERTURA COMPLETA: Cada palabra del capítulo debe aparecer en algún subtema
2. SIN OMISIONES: No puedes resumir, acortar o parafrasear el texto original
3. DISTRIBUCION LOGICA: Divide el texto en secciones lógicas pero SIN PERDER CONTENIDO
4. VERIFICACION: Al final cuenta las palabras para asegurar que coincidan
5. CONTENIDO LITERAL: Copia el texto exactamente como aparece en el capítulo

REGLAS CRITICAS PARA EL HTML EN JSON:
1. NO uses concatenación de strings con + en el HTML
2. Escribe el HTML completo en UNA SOLA línea dentro de comillas dobles
3. Escapa TODAS las comillas dobles dentro del HTML usando \""
4. NO uses caracteres especiales como +, \n, \r en el JSON
5. El HTML debe ser UNA cadena continua sin saltos de línea

EJEMPLO DE JSON CORRECTO (sin concatenación):
{{
  ""capitulo"": {{
    ""Total_Palabras_Capitulo"": 4500,
    ""Total_Subtemas_Capitulo"": 5,
    ""Total_Palabras_Subtemas"": 4500,
    ""titulo"": ""INTRODUCCION A LA REGRESION LINEAL"",
    ""subtemas"": [
      {{
        ""Total_Palabras_Subtema"": 800,
        ""title"": ""Definición de Regresión Lineal"",
        ""texto"": ""La regresión lineal es un método estadístico que permite modelar la relación entre una variable dependiente y una o más variables independientes. Se utiliza para predecir el valor de la variable dependiente a partir de las independientes. Es una de las técnicas más fundamentales en el análisis estadístico y el aprendizaje automático."",
        ""descripcion"": ""Este subtema explica qué es la regresión lineal y su propósito en la estadística."",
        ""html"": ""<h2 style=\""color:#1f4e79; background-color:#e7f1ff; padding:10px; margin-bottom:15px; border-left:5px solid #1f4e79;\"">DEFINICION DE REGRESION LINEAL</h2><p style=\""color:#333; background-color:#f8f9fa; padding:15px; border-radius:5px; line-height:1.6;\"">La regresión lineal es un método estadístico que permite modelar la relación entre una variable dependiente y una o más variables independientes.</p><p style=\""color:#333; background-color:#fff3cd; padding:10px; border-radius:5px; border-left:4px solid #ffc107;\"">Se utiliza para predecir el valor de la variable dependiente a partir de las independientes.</p>""
      }},
      {{
        ""Total_Palabras_Subtema"": 950,
        ""title"": ""Modelo de Regresión Lineal Simple"",
        ""texto"": ""El modelo de regresión lineal simple se expresa como: Y = β0 + β1X + ε, donde Y es la variable dependiente, X es la variable independiente, β0 es la intersección con el eje Y, β1 es la pendiente de la línea de regresión, y ε representa el término de error. Este modelo asume una relación lineal entre las variables."",
        ""descripcion"": ""Este subtema describe la fórmula matemática básica de un modelo de regresión lineal simple."",
        ""html"": ""<h2 style=\""color:#2e8b57; background-color:#f0fff0; padding:12px; margin-bottom:15px; border-radius:8px;\"">MODELO DE REGRESIÓN LINEAL SIMPLE</h2><div style=\""background-color:#fff; padding:20px; border:2px solid #2e8b57; border-radius:10px;\""><p style=\""color:#2c3e50; font-size:16px; line-height:1.7;\"">El modelo de regresión lineal simple se expresa como:</p><div style=\""background-color:#e8f5e8; padding:15px; text-align:center; font-family:monospace; font-size:18px; border-radius:5px; margin:10px 0;\"">Y = β0 + β1X + ε</div><p style=\""color:#34495e; margin-top:15px;\"">Donde Y es la variable dependiente, X es la variable independiente, β0 es la intersección y β1 es la pendiente.</p></div>""
      }},
      {{
        ""Total_Palabras_Subtema"": 1100,
        ""title"": ""Supuestos de la Regresión Lineal"",
        ""texto"": ""La regresión lineal se basa en varios supuestos fundamentales: linealidad (la relación entre variables es lineal), independencia de errores (los residuos son independientes), homocedasticidad (varianza constante de los errores), normalidad de los errores (los residuos siguen una distribución normal), y ausencia de multicolinealidad en regresión múltiple."",
        ""descripcion"": ""Este subtema revisa los supuestos matemáticos necesarios para que la regresión lineal sea válida."",
        ""html"": ""<h2 style=\""color:#8b0000; background-color:#ffe4e1; padding:12px; border-bottom:3px solid #8b0000; margin-bottom:20px;\"">SUPUESTOS DE LA REGRESIÓN LINEAL</h2><ul style=\""background-color:#f5f5f5; padding:20px; border-radius:8px; list-style-type:none;\""><li style=\""background-color:#fff; margin:10px 0; padding:12px; border-left:4px solid #8b0000; border-radius:4px;\""><strong style=\""color:#8b0000;\"">Linealidad:</strong> La relación entre variables es lineal</li><li style=\""background-color:#fff; margin:10px 0; padding:12px; border-left:4px solid #ff6347; border-radius:4px;\""><strong style=\""color:#ff6347;\"">Independencia:</strong> Los errores son independientes</li><li style=\""background-color:#fff; margin:10px 0; padding:12px; border-left:4px solid #ffa500; border-radius:4px;\""><strong style=\""color:#ffa500;\"">Homocedasticidad:</strong> Varianza constante de errores</li></ul>""
      }},
      {{
        ""Total_Palabras_Subtema"": 850,
        ""title"": ""Interpretación de Coeficientes"",
        ""texto"": ""Los coeficientes en regresión lineal tienen interpretaciones específicas. El coeficiente β0 (intersección) representa el valor esperado de Y cuando X=0. El coeficiente β1 (pendiente) indica el cambio promedio en Y por cada unidad de cambio en X. Un β1 positivo indica relación directa, mientras que uno negativo indica relación inversa."",
        ""descripcion"": ""Este subtema explica cómo interpretar los coeficientes obtenidos en el modelo de regresión."",
        ""html"": ""<h2 style=\""color:#4169e1; background-color:#f0f8ff; padding:15px; text-align:center; border-radius:10px; margin-bottom:20px;\"">INTERPRETACIÓN DE COEFICIENTES</h2><div style=\""display:grid; grid-template-columns:1fr 1fr; gap:15px; margin:20px 0;\""><div style=\""background-color:#e6f3ff; padding:15px; border-radius:8px; border:2px solid #4169e1;\""><h3 style=\""color:#4169e1; margin-top:0;\"">β0 (Intersección)</h3><p style=\""color:#2c3e50;\"">Valor esperado de Y cuando X=0</p></div><div style=\""background-color:#fff0e6; padding:15px; border-radius:8px; border:2px solid #ff8c00;\""><h3 style=\""color:#ff8c00; margin-top:0;\"">β1 (Pendiente)</h3><p style=\""color:#2c3e50;\"">Cambio promedio en Y por unidad de X</p></div></div>""
      }},
      {{
        ""Total_Palabras_Subtema"": 800,
        ""title"": ""Evaluación del Modelo"",
        ""texto"": ""La evaluación del modelo de regresión lineal se realiza mediante varios indicadores: el coeficiente de determinación R², que mide la proporción de varianza explicada; el error estándar de la estimación; las pruebas de significancia de los coeficientes; y el análisis de residuos para verificar los supuestos del modelo."",
        ""descripcion"": ""Este subtema cubre las métricas y técnicas para evaluar la calidad del modelo de regresión."",
        ""html"": ""<h2 style=\""color:#9932cc; background-color:#f8f0ff; padding:12px; border:2px dashed #9932cc; margin-bottom:15px;\"">EVALUACIÓN DEL MODELO</h2><div style=\""background-color:#ffffff; padding:20px; box-shadow:0 4px 8px rgba(0,0,0,0.1); border-radius:10px;\""><h3 style=\""color:#9932cc; border-bottom:2px solid #9932cc; padding-bottom:5px;\"">Métricas Principales:</h3><p style=\""background-color:#f0e6ff; padding:10px; border-radius:5px; margin:10px 0;\""><strong>R²:</strong> Proporción de varianza explicada por el modelo</p><p style=\""background-color:#e6f0ff; padding:10px; border-radius:5px; margin:10px 0;\""><strong>Error Estándar:</strong> Medida de precisión de las predicciones</p><p style=\""background-color:#ffe6f0; padding:10px; border-radius:5px; margin:10px 0;\""><strong>Análisis de Residuos:</strong> Verificación de supuestos del modelo</p></div>""
      }}
    ]
  }}
}}

INSTRUCCIONES CRITICAS PARA ASEGURAR CONTENIDO COMPLETO:
1. ANTES DE EMPEZAR: Cuenta aproximadamente cuántas palabras tiene el contenido total del capítulo
2. DURANTE EL PROCESO: Divide ese contenido en subtemas lógicos SIN OMITIR NADA
3. TEXTO LITERAL: En el campo texto de cada subtema, pon exactamente el texto como aparece, no lo resumas
4. DISTRIBUCION COMPLETA: Asegúrate que cada oración del capítulo aparezca en algún subtema
5. VERIFICACION FINAL: Suma las palabras de todos los subtemas y debe coincidir con el total del capítulo

CONTEO DE PALABRAS:
- Total_Palabras_Capitulo: Debe ser el conteo real de palabras del contenido proporcionado
- Total_Palabras_Subtemas: Debe ser exactamente igual al Total_Palabras_Capitulo
- Total_Palabras_Subtema: Para cada subtema, debe ser el conteo real de su texto

IMPORTANTE NO ME DES COMENTARIOS AL FINAL USA SOLO JSON VALIDO
Nunca comiences el JSON con ```json o JSON
Es importante que mantengas el idioma original del texto no lo cambies.
Es vital que el JSON generado sea válido y que contenga palabra por palabra todo el texto.

MUY IMPORTANTE - REGLAS PARA EL HTML:
NUNCA hagas esto:
- html: <h2>TITULO</h2> + <p>Texto</p>
- Concatenación con + en JSON

SIEMPRE haz esto:
- html: <h2 style=\""color:#1f4e79;\"">TITULO</h2><p style=\""color:#333;\"">Texto completo en una sola línea</p>
- Todo el HTML en UNA cadena continua

CONTENIDO DE LAS PAGINAS:
{allChapterContent}

TITULO DEL CAPITULO: {currentChapter.Titulo}

VERIFICACION FINAL OBLIGATORIA:
1) Es JSON válido?
2) Contiene TODO el texto del capítulo palabra por palabra?
3) El Total_Palabras_Capitulo coincide con Total_Palabras_Subtemas?
4) NO usa concatenación de strings con +?
5) Cada oración del contenido original aparece en algún subtema?

SI NO CUMPLES ESTAS 5 VERIFICACIONES, EL RESULTADO SERA INCORRECTO

Tu respuesta aqui:=>";
              

                // PASO 4: Necesitarías inicializar el kernel y llamar a OpenAI aquí
                // Por ahora, como esta clase no tiene acceso directo a Semantic Kernel,
                // devolvemos el contenido extraído manualmente
                
                // Buscar el título del capítulo en el contenido y extraer desde ahí
                var lines = allChapterContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var extractedContent = new StringBuilder();
                bool foundStart = false;
                
                string chapterTitle = currentChapter.Titulo?.Trim();
                string nextChapterTitle = nextChapter?.Titulo?.Trim();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Buscar el inicio del capítulo por título
                    if (!foundStart && IsChapterTitleMatch(trimmedLine, chapterTitle))
                    {
                        foundStart = true;
                        extractedContent.AppendLine(line);
                        continue;
                    }

                    // Si encontramos el siguiente capítulo, parar
                    if (foundStart && !string.IsNullOrEmpty(nextChapterTitle) && 
                        IsChapterTitleMatch(trimmedLine, nextChapterTitle))
                    {
                        break;
                    }

                    // Si encontramos el inicio, agregar todas las líneas siguientes
                    if (foundStart)
                    {
                        extractedContent.AppendLine(line);
                    }
                }
                 
             
                
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();
                history.AddUserMessage(prompt);

                // Medir tiempo de procesamiento AI
                var startTime = DateTime.UtcNow;
                var response = await chatCompletion.GetChatMessageContentAsync(history);
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                var aiResponse = response.Content ?? "";

                // Limpiar respuesta
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                Root capitulo = JsonConvert.DeserializeObject<Root>(aiResponse);
                AiTokrens tokens = new AiTokrens();
                capitulo.Capitulo.TextoCompleto = allChapterContent;
                capitulo.Capitulo.TotalTokens = tokens.GetTokenCount(capitulo.Capitulo.TextoCompleto ?? "");
                capitulo.Capitulo.TimeSeconds = (int)Math.Round(processingTime / 1000);

                capitulo.Capitulo.PaginaDe = currentChapter.PaginaDe;
                if(nextChapter != null)
                {
                    capitulo.Capitulo.PaginaA = nextChapter.PaginaDe - 1;
                }
                else
                {
                    capitulo.Capitulo.PaginaA = currentChapter.PaginaA;
                }
                 
                return capitulo.Capitulo;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public string ExtractCapituloData(CapituloIndice currentChapter, CapituloIndice nextChapter, string allPagesContent)
        {
            try
            {
                if (currentChapter == null || string.IsNullOrEmpty(allPagesContent))
                {
                    return string.Empty;
                }

                // Dividir todo el contenido en líneas para procesar
                var allLines = allPagesContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var chapterContent = new StringBuilder();
                bool foundStart = false;

                // Buscar el título del capítulo en el contenido
                string chapterTitle = currentChapter.Titulo?.Trim();
                if (string.IsNullOrEmpty(chapterTitle))
                {
                    return string.Empty;
                }

                // Determinar el título del siguiente capítulo para saber dónde terminar
                string nextChapterTitle = nextChapter?.Titulo?.Trim();

                // Extraer contenido del capítulo basado en títulos
                foreach (var line in allLines)
                {
                    var trimmedLine = line.Trim();

                    // Buscar el inicio del capítulo por título (comparación flexible)
                    if (!foundStart && IsChapterTitleMatch(trimmedLine, chapterTitle))
                    {
                        foundStart = true;
                        chapterContent.AppendLine(line); // Incluir la línea del título
                        continue;
                    }

                    // Si ya encontramos el inicio y llegamos al siguiente capítulo, parar
                    if (foundStart && !string.IsNullOrEmpty(nextChapterTitle) && 
                        IsChapterTitleMatch(trimmedLine, nextChapterTitle))
                    {
                        break;
                    }

                    // Si encontramos el inicio, agregar todas las líneas siguientes
                    if (foundStart)
                    {
                        chapterContent.AppendLine(line);
                    }
                }

                return chapterContent.ToString().Trim();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Verifica si una línea coincide con el título del capítulo (comparación flexible)
        /// </summary>
        private bool IsChapterTitleMatch(string line, string chapterTitle)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(chapterTitle))
                return false;

            // Normalizar ambas cadenas para comparación
            var normalizedLine = NormalizeText(line);
            var normalizedTitle = NormalizeText(chapterTitle);

            // Verificar coincidencia exacta
            if (normalizedLine.Equals(normalizedTitle, StringComparison.OrdinalIgnoreCase))
                return true;

            // Verificar si la línea contiene el título (para casos donde hay números romanos, etc.)
            if (normalizedLine.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase))
                return true;

            // Verificar coincidencia sin números romanos o números al inicio
            var lineWithoutNumbers = RemoveLeadingNumbers(normalizedLine);
            var titleWithoutNumbers = RemoveLeadingNumbers(normalizedTitle);

            if (lineWithoutNumbers.Equals(titleWithoutNumbers, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Normaliza el texto removiendo caracteres especiales y espacios extras
        /// </summary>
        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Remover caracteres especiales comunes y normalizar espacios
            var normalized = text.Trim()
                      .Replace(".", "")
                      .Replace(",", "")
                      .Replace(":", "")
                      .Replace(";", "")
                      .Replace("\"", "")
                      .Replace("'", "")
                      .Replace("-", " ")
                      .Replace("_", " ")
                      .Replace("\t", " ");

            // Reemplazar múltiples espacios por uno solo usando Regex
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

            return normalized.Trim();
        }

        /// <summary>
        /// Remueve números romanos y arábigos del inicio del texto
        /// </summary>
        private string RemoveLeadingNumbers(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Remover números romanos al inicio (I, II, III, IV, V, etc.)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[IVXLCDM]+\.?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Remover números arábigos al inicio (1, 2, 3, etc.)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\d+\.?\s*", "");
            
            // Remover letras seguidas de punto al inicio (A., B., etc.)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[A-Z]\.?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return text.Trim();
        }
        /// <summary>
        /// Genera un resumen básico del texto proporcionado
        /// </summary>
        private string GenerateBasicSummary(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return "Sin contenido disponible";

            // Si el texto es corto, devolverlo completo
            if (texto.Length <= 200)
                return texto.Trim();

            // Si es largo, tomar las primeras 200 caracteres y agregar puntos suspensivos
            var summary = texto.Substring(0, Math.Min(200, texto.Length)).Trim();
            if (summary.Length == 200)
                summary += "...";

            return summary;
        }

        public class CapituloDocumento
        {
            public int  Total_Palabras_Capitulo { get; set; }

            public string TwinID { get; set; }

            public string CapituloID { get; set; }
            public string DocumentID { get; set; }
            public int TimeSeconds { get; set; }

            public int NumeroCapitulo { get; set; }

            public int Total_Palabras_Subtemas { get; set; }
            public int Total_Subtemas_Capitulo { get; set; }
            public string Titulo { get; set; }
            public List<Subtema> Subtemas { get; set; }

            public string TextoCompleto { get; set; }

            public int PaginaDe { get; set; }

            public int PaginaA { get; set; }

            public int TotalTokens { get; set; }
        }

        public class Subtema
        {
            public string TwinID { get; set; }

            public int CapituloTimeSeconds { get; set; }

            public string SubtemaID { get; set; }

            public int Total_Subtemas_Capitulo { get; set; }
            public string TextoCompleto { get; set; }

            public int CapituloPaginaDe { get; set; }

            public int CapituloPaginaA { get; set; }

            public int CapituloTotalTokens { get; set; }
            public string CapituloID { get; set; }
            public string DocumentID { get; set; }
            public int Total_Palabras_Subtema { get; set; }
            public string Title { get; set; }
            public string Texto { get; set; }
            public string Descripcion { get; set; }
            public string Html { get; set; }

            public int TotalTokensCapitulo { get; set; }

            public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        }

        public class Root
        {
            public CapituloDocumento Capitulo { get; set; }
        }
        /// <summary>
        /// Calcula tokens aproximados de un texto usando AiTokrens
        /// </summary>
        private int CalculateTokens(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return 0;

            try
            {
                AiTokrens tokens = new AiTokrens();
                return tokens.GetTokenCount(texto);
            }
            catch
            {
                // Fallback: estimación simple si AiTokrens falla
                return texto.Length / 4;
            }
        }
    }
}
