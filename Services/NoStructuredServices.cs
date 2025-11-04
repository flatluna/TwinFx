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

                // 1) Obtener el número de veces que el string contiene 700 caracteres
               
               var  tokens = new AiTokrens();

                int TotalTokens = tokens.GetTokenCount(allChapterContent);
                int numberOfSubchapters = CalculateSubchaptersBasedOnLength(TotalTokens, 700);
                // 2) Crear nuevo prompt simple basado en el número calculado
                var prompt = CreateSimpleSubdivisionPrompt(allChapterContent, currentChapter.Titulo, numberOfSubchapters);

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
                string TotalChapterText = extractedContent.ToString().Trim();

                string promptSimple = $@"Eres un asistente que extrae
                subtemas de capítulos de libros o documentos.";

                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();
              //  history.AddUserMessage(prompt);
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
               
                capitulo.Capitulo.TextoCompleto = TotalChapterText;
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

        /// <summary>
        /// Calcula el número de subcapítulos basado en la longitud del contenido
        /// Divide por 700 caracteres para obtener el número de secciones
        /// </summary>
        /// <param name="content">Contenido del capítulo</param>
        /// <param name="charsPerSection">Número de caracteres por sección (700)</param>
        /// <returns>Número de subcapítulos a crear</returns>
        private int CalculateSubchaptersBasedOnLength(int TotalTokens, int charsPerSection)
        {
             

            int numberOfSections = (int)Math.Ceiling((double)TotalTokens / charsPerSection);

            // Mínimo 1 subcapítulo, máximo 15 para mantener manejable
            return Math.Max(1, Math.Min(15, numberOfSections));
        }

        /// <summary>
        /// Crea un prompt simple para dividir el capítulo en N subcapítulos
        /// </summary>
        /// <param name="content">Contenido completo del capítulo</param>
        /// <param name="chapterTitle">Título del capítulo</param>
        /// <param name="numberOfSubchapters">Número de subcapítulos a crear</param>
        /// <returns>Prompt optimizado para la división</returns>
        private string CreateSimpleSubdivisionPrompt(string content, string chapterTitle, int numberOfSubchapters)
        {
            return $@"Divide este capítulo en exactamente {numberOfSubchapters} subcapítulos.

REGLAS SIMPLES:
1. Divide el texto en {numberOfSubchapters} partes iguales
2. Copia el texto EXACTAMENTE como aparece, palabra por palabra
3. Crea un título descriptivo para cada subcapítulo
4. NO resumas ni cambies el texto original
5. Cada subcapítulo debe tener aproximadamente {content.Length / numberOfSubchapters} caracteres

TITULO DEL CAPITULO: {chapterTitle}

CONTENIDO A DIVIDIR:
{content}

FORMATO JSON REQUERIDO:
{{
  ""capitulo"": {{
    ""titulo"": ""{chapterTitle}"",
    ""Total_Subcapitulos"": {numberOfSubchapters},
    ""subtemas"": [
      {{
        ""title"": ""Título descriptivo del subcapítulo 1"",
        ""texto"": ""Texto exacto del subcapítulo copiado palabra por palabra"",
        ""descripcion"": ""Breve descripción del contenido de este subcapítulo""
      }},
      {{
        ""title"": ""Título descriptivo del subcapítulo 2"",
        ""texto"": ""Texto exacto del subcapítulo copiado palabra por palabra"",
        ""descripcion"": ""Breve descripción del contenido de este subcapítulo""
      }}
    ]
  }}
}}

IMPORTANTE:
- Responde SOLO con JSON válido
- NO uses ```json al inicio
- Divide en exactamente {numberOfSubchapters} subcapítulos
- Copia TODO el texto sin omitir nada
- Cada palabra del contenido original debe aparecer en algún subcapítulo";
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
