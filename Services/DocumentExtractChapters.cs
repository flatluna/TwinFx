using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinFx.Agents;

namespace TwinFx.Services
{
    public class DocumentExtractChapters
    {
        public List<ExractedChapterData> ExtractChapters(List<ChapterIndex> Chapters, 
            string TwinID,
            List<DocumentPage> DocumentPages)
        {
            var extractedChapters = new List<ExractedChapterData>();

            try
            {
                if (Chapters == null || !Chapters.Any() || DocumentPages == null || !DocumentPages.Any())
                {
                    return extractedChapters;
                }

                // Initialize token service for counting tokens
                var tokenService = new AiTokrens();

                // Process each chapter in the index
                for (int i = 0; i < Chapters.Count; i++)
                {
                    var currentChapter = Chapters[i];
                    var nextChapter = (i + 1 < Chapters.Count) ? Chapters[i + 1] : null;

                    try
                    {
                        // Extract text content for this chapter based on page range
                        var chapterText = ExtractChapterTextFromPages(DocumentPages, currentChapter.PageFrom, currentChapter.PageTo);

                        if (string.IsNullOrEmpty(chapterText))
                        {
                            continue; // Skip if no content found
                        }

                        // Create the main chapter data object
                        var extractedChapter = CreateExtractedChapterData(
                            currentChapter,
                            chapterText,
                            TwinID,
                            tokenService);

                        // Process subchapters if they exist
                        if (currentChapter.Subchapters != null && currentChapter.Subchapters.Any())
                        {
                            Console.WriteLine($"📖 Processing chapter '{currentChapter.ChapterTitle}' with {currentChapter.Subchapters.Count} subchapters");

                            // Extract each subchapter and add to the chapter's SubChapters list
                            foreach (var subChapterTitle in currentChapter.Subchapters)
                            {
                                var subChapterText = ExtractSubChapterText(chapterText, subChapterTitle, currentChapter.Subchapters);
                                
                                if (!string.IsNullOrEmpty(subChapterText))
                                {
                                    var extractedSubChapter = CreateExtractedSubChapterData(
                                        subChapterTitle,
                                        subChapterText,
                                        extractedChapter.ChapterID,
                                        currentChapter.PageFrom,
                                        currentChapter.PageTo,
                                        tokenService);

                                    extractedChapter.SubChapters.Add(extractedSubChapter);
                                }
                                else
                                {
                                    Console.WriteLine($"⚠️ No content found for subchapter: '{subChapterTitle}'");
                                }
                            }

                            Console.WriteLine($"✅ Extracted {extractedChapter.SubChapters.Count} subchapters for chapter '{currentChapter.ChapterTitle}'");
                        }
                        else
                        {
                            Console.WriteLine($"📄 Processing chapter '{currentChapter.ChapterTitle}' without subchapters");
                            
                            // No subchapters - create a single subchapter entry with the whole chapter content
                            var mainSubChapter = CreateExtractedSubChapterData(
                                currentChapter.ChapterTitle, // Use chapter title as subchapter title
                                chapterText, // Use full chapter text
                                extractedChapter.ChapterID,
                                currentChapter.PageFrom,
                                currentChapter.PageTo,
                                tokenService);

                            extractedChapter.SubChapters.Add(mainSubChapter);
                        }

                        // Add the chapter (with its subchapters) to the result list
                        extractedChapters.Add(extractedChapter);
                        
                        Console.WriteLine($"✅ Chapter '{currentChapter.ChapterTitle}' processed successfully with {extractedChapter.SubChapters.Count} subchapter(s)");
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue processing other chapters
                        Console.WriteLine($"❌ Error processing chapter '{currentChapter.ChapterTitle}': {ex.Message}");
                        continue;
                    }
                }

                Console.WriteLine($"🎉 Extraction completed: {extractedChapters.Count} chapters processed with {extractedChapters.Sum(c => c.SubChapters.Count)} total subchapters");
                return extractedChapters;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in ExtractChapters: {ex.Message}");
                return extractedChapters;
            }
        }

        /// <summary>
        /// Extract text content from pages within the specified page range
        /// </summary>
        private string ExtractChapterTextFromPages(List<DocumentPage> documentPages, int pageFrom, int pageTo)
        {
            var chapterText = new StringBuilder();

            try
            {
                // Find all pages within the specified range
                var pagesInRange = documentPages
                    .Where(p => p.PageNumber >= pageFrom && p.PageNumber <= pageTo)
                    .OrderBy(p => p.PageNumber);

                foreach (var page in pagesInRange)
                {
                    chapterText.AppendLine($"\n=== PÁGINA {page.PageNumber} ===");
                    
                    if (page.LinesText != null && page.LinesText.Any())
                    {
                        foreach (var line in page.LinesText)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                chapterText.AppendLine(line);
                            }
                        }
                    }
                }

                return chapterText.ToString().Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting chapter text from pages {pageFrom}-{pageTo}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Extract text for a specific subchapter from the chapter content
        /// Enhanced with single-line pattern matching strategy as suggested by user
        /// </summary>
        private string ExtractSubChapterText(string chapterText, string subChapterTitle, List<string> allSubChapters)
        {
            try
            {
                if (string.IsNullOrEmpty(chapterText) || string.IsNullOrEmpty(subChapterTitle))
                {
                    return string.Empty;
                }

                // NUEVA ESTRATEGIA: Convertir todo a una sola línea para búsqueda más precisa
                Console.WriteLine($"🔍 === SINGLE-LINE PATTERN MATCHING ===");
                Console.WriteLine($"📖 Looking for: '{subChapterTitle}'");
                Console.WriteLine($"📋 All subchapters: {string.Join(", ", allSubChapters.Select(s => $"'{s}'"))}");

                // Convertir todo el texto a una línea continua, manteniendo espacios entre palabras
                var singleLineText = ConvertToSingleLine(chapterText);
                Console.WriteLine($"📝 Single line text length: {singleLineText.Length} characters");
                Console.WriteLine($"📄 First 300 chars: {(singleLineText.Length > 300 ? singleLineText.Substring(0, 300) + "..." : singleLineText)}");

                // Buscar el título del subcapítulo actual
                var currentSubChapterIndex = allSubChapters.IndexOf(subChapterTitle);
                
                // Encontrar la posición de inicio del subcapítulo actual
                int startPosition = FindSubChapterPositionInSingleLine(singleLineText, subChapterTitle);
                
                if (startPosition == -1)
                {
                    Console.WriteLine($"❌ Subchapter not found in single line: '{subChapterTitle}'");
                    return string.Empty;
                }

                // Encontrar la posición de fin (inicio del siguiente subcapítulo)
                int endPosition = singleLineText.Length;
                
                // Buscar el siguiente subcapítulo en la lista
                for (int subIdx = currentSubChapterIndex + 1; subIdx < allSubChapters.Count; subIdx++)
                {
                    var nextSubChapterTitle = allSubChapters[subIdx];
                    int nextPosition = FindSubChapterPositionInSingleLine(singleLineText, nextSubChapterTitle);
                    
                    if (nextPosition > startPosition)
                    {
                        endPosition = nextPosition;
                        Console.WriteLine($"✅ Found next subchapter '{nextSubChapterTitle}' at position {nextPosition}");
                        break;
                    }
                }

                // Extraer el contenido entre las posiciones
                if (endPosition <= startPosition)
                {
                    Console.WriteLine($"⚠️ Invalid positions: start={startPosition}, end={endPosition}");
                    return string.Empty;
                }

                // Encontrar el final del título actual para no incluirlo en el contenido
                int titleEndPosition = FindTitleEndPosition(singleLineText, subChapterTitle, startPosition);
                
                var extractedContent = singleLineText.Substring(titleEndPosition, endPosition - titleEndPosition).Trim();
                
                // Limpiar el contenido extraído y restaurar formato legible
                var cleanedContent = CleanAndFormatExtractedContent(extractedContent);
                
                Console.WriteLine($"✅ Extracted content length: {cleanedContent.Length} characters");
                Console.WriteLine($"📄 First 200 chars of content: {(cleanedContent.Length > 200 ? cleanedContent.Substring(0, 200) + "..." : cleanedContent)}");
                Console.WriteLine($"🔚 === END SINGLE-LINE MATCHING ===\n");
                
                return cleanedContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in single-line extraction for '{subChapterTitle}': {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Convert multi-line text to single line while preserving word boundaries
        /// </summary>
        private string ConvertToSingleLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Agregar espacio entre líneas para evitar que se peguen las palabras
                if (result.Length > 0)
                    result.Append(" ");
                    
                result.Append(trimmedLine);
            }

            return result.ToString();
        }

        /// <summary>
        /// Find the position of a subchapter title in single-line text using multiple strategies
        /// Enhanced with better precision and debugging
        /// </summary>
        private int FindSubChapterPositionInSingleLine(string singleLineText, string subChapterTitle)
        {
            Console.WriteLine($"🔍 === DETAILED SUBCHAPTER SEARCH ===");
            Console.WriteLine($"📖 Searching for: '{subChapterTitle}'");
            Console.WriteLine($"📝 Text length: {singleLineText.Length} characters");

            // ESTRATEGIA 1: Búsqueda directa del título completo (case-insensitive)
            int position = singleLineText.IndexOf(subChapterTitle, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                Console.WriteLine($"✅ STRATEGY 1 SUCCESS: Direct match at position {position}");
                Console.WriteLine($"📄 Context: '{GetContextAroundPosition(singleLineText, position, subChapterTitle.Length + 20)}'");
                return position;
            }

            // ESTRATEGIA 2: Normalizar y buscar
            var normalizedText = NormalizeTextForSearch(singleLineText);
            var normalizedTitle = NormalizeTextForSearch(subChapterTitle);
            
            position = normalizedText.IndexOf(normalizedTitle, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                int originalPosition = MapToOriginalPosition(singleLineText, normalizedText, position);
                Console.WriteLine($"✅ STRATEGY 2 SUCCESS: Normalized match at position {originalPosition}");
                Console.WriteLine($"📄 Context: '{GetContextAroundPosition(singleLineText, originalPosition, subChapterTitle.Length + 20)}'");
                return originalPosition;
            }

            // ESTRATEGIA 3: Búsqueda sin números al inicio
            var titleWithoutNumbers = RemoveLeadingNumbers(normalizedTitle);
            if (!string.IsNullOrEmpty(titleWithoutNumbers) && titleWithoutNumbers != normalizedTitle)
            {
                position = normalizedText.IndexOf(titleWithoutNumbers, StringComparison.OrdinalIgnoreCase);
                if (position >= 0)
                {
                    int originalPosition = MapToOriginalPosition(singleLineText, normalizedText, position);
                    Console.WriteLine($"✅ STRATEGY 3 SUCCESS: Match without numbers at position {originalPosition}");
                    Console.WriteLine($"📄 Context: '{GetContextAroundPosition(singleLineText, originalPosition, titleWithoutNumbers.Length + 20)}'");
                    return originalPosition;
                }
            }

            // ESTRATEGIA 4: Búsqueda de patrones numerados específicos
            var numberMatch = System.Text.RegularExpressions.Regex.Match(subChapterTitle, @"^(\d+)\.\s*(.+)");
            if (numberMatch.Success)
            {
                var number = numberMatch.Groups[1].Value;
                var titlePart = numberMatch.Groups[2].Value.Trim();
                
                Console.WriteLine($"🔍 STRATEGY 4: Looking for numbered pattern '{number}. {titlePart}'");
                
                // Buscar el patrón exacto
                var patterns = new[]
                {
                    $@"{number}\.\s+{System.Text.RegularExpressions.Regex.Escape(titlePart)}",
                    $@"{number}\s*\.\s*{System.Text.RegularExpressions.Regex.Escape(titlePart)}",
                    $@"{number}\.{System.Text.RegularExpressions.Regex.Escape(titlePart)}"
                };

                foreach (var pattern in patterns)
                {
                    var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var match = regex.Match(singleLineText);
                    
                    if (match.Success)
                    {
                        Console.WriteLine($"✅ STRATEGY 4 SUCCESS: Pattern match at position {match.Index}");
                        Console.WriteLine($"📄 Matched pattern: '{pattern}'");
                        Console.WriteLine($"📄 Context: '{GetContextAroundPosition(singleLineText, match.Index, match.Length + 20)}'");
                        return match.Index;
                    }
                }
            }

            // ESTRATEGIA 5: Búsqueda palabra por palabra (más flexible)
            var titleWords = normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                           .Where(w => w.Length > 2)
                                           .ToList();

            if (titleWords.Count > 0)
            {
                Console.WriteLine($"🔍 STRATEGY 5: Word-by-word search with {titleWords.Count} words: {string.Join(", ", titleWords)}");
                
                position = FindWordSequencePosition(normalizedText, titleWords);
                if (position >= 0)
                {
                    int originalPosition = MapToOriginalPosition(singleLineText, normalizedText, position);
                    Console.WriteLine($"✅ STRATEGY 5 SUCCESS: Word sequence match at position {originalPosition}");
                    Console.WriteLine($"📄 Context: '{GetContextAroundPosition(singleLineText, originalPosition, 30)}'");
                    return originalPosition;
                }
            }

            Console.WriteLine($"❌ ALL STRATEGIES FAILED for '{subChapterTitle}'");
            Console.WriteLine($"📄 First 500 chars of text: '{(singleLineText.Length > 500 ? singleLineText.Substring(0, 500) + "..." : singleLineText)}'");
            return -1;
        }

        /// <summary>
        /// Get context around a position for debugging purposes
        /// </summary>
        private string GetContextAroundPosition(string text, int position, int contextLength)
        {
            int start = Math.Max(0, position - 20);
            int length = Math.Min(contextLength + 40, text.Length - start);
            
            if (start + length > text.Length)
                length = text.Length - start;
                
            return text.Substring(start, length);
        }

        /// <summary>
        /// Find position of word sequence in text
        /// </summary>
        private int FindWordSequencePosition(string text, List<string> words)
        {
            for (int i = 0; i <= text.Length - 50; i++)
            {
                var window = text.Substring(i, Math.Min(200, text.Length - i));
                int matchedWords = words.Count(word => window.Contains(word, StringComparison.OrdinalIgnoreCase));
                
                if (matchedWords >= Math.Max(1, words.Count * 0.8)) // 80% de palabras deben coincidir
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Map a position in normalized text back to original text
        /// </summary>
        private int MapToOriginalPosition(string originalText, string normalizedText, int normalizedPosition)
        {
            if (normalizedText.Length == 0)
                return 0;
                
            double ratio = (double)normalizedPosition / normalizedText.Length;
            int approximatePosition = (int)(ratio * originalText.Length);
            
            return Math.Max(0, Math.Min(approximatePosition, originalText.Length - 1));
        }

        /// <summary>
        /// Find where the title ends to exclude it from content
        /// Enhanced to be more precise and avoid losing important content
        /// </summary>
        private int FindTitleEndPosition(string singleLineText, string subChapterTitle, int titleStartPosition)
        {
            // ESTRATEGIA MEJORADA: Ser más preciso con el final del título
            
            // Normalizar el título para encontrar exactamente dónde termina
            var normalizedTitle = NormalizeTextForSearch(subChapterTitle);
            var normalizedText = NormalizeTextForSearch(singleLineText);
            
            // Encontrar la posición exacta del título normalizado en el texto normalizado
            int normalizedTitlePosition = normalizedText.IndexOf(normalizedTitle, StringComparison.OrdinalIgnoreCase);
            
            if (normalizedTitlePosition >= 0)
            {
                // Calcular el final del título normalizado
                int normalizedTitleEnd = normalizedTitlePosition + normalizedTitle.Length;
                
                // Mapear de vuelta al texto original
                int approximateTitleEnd = MapToOriginalPosition(singleLineText, normalizedText, normalizedTitleEnd);
                
                // Buscar el primer carácter no-espacio después del título
                for (int i = approximateTitleEnd; i < singleLineText.Length; i++)
                {
                    if (!char.IsWhiteSpace(singleLineText[i]))
                    {
                        // Encontrado el inicio del contenido real
                        Console.WriteLine($"✅ Title ends at position {i}, content starts with: '{singleLineText.Substring(i, Math.Min(50, singleLineText.Length - i))}'");
                        return i;
                    }
                }
            }
            
            // FALLBACK: Método alternativo - buscar patrones específicos
            
            // Para títulos numerados como "1. Social Security"
            var numberMatch = System.Text.RegularExpressions.Regex.Match(subChapterTitle, @"^(\d+)\.\s*(.+)");
            if (numberMatch.Success)
            {
                var number = numberMatch.Groups[1].Value;
                var titleText = numberMatch.Groups[2].Value.Trim();
                
                // Buscar el patrón completo en el texto original
                var pattern = $@"{number}\.\s*{System.Text.RegularExpressions.Regex.Escape(titleText)}";
                var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var match = regex.Match(singleLineText.Substring(titleStartPosition));
                
                if (match.Success)
                {
                    int titleEnd = titleStartPosition + match.Index + match.Length;
                    
                    // Saltar espacios adicionales después del título
                    while (titleEnd < singleLineText.Length && char.IsWhiteSpace(singleLineText[titleEnd]))
                    {
                        titleEnd++;
                    }
                    
                    Console.WriteLine($"✅ Numbered title pattern found, content starts at position {titleEnd}");
                    return titleEnd;
                }
            }
            
            // FALLBACK FINAL: Método conservador
            // Buscar el título como está escrito y encontrar dónde termina
            int directTitlePos = singleLineText.IndexOf(subChapterTitle, titleStartPosition, StringComparison.OrdinalIgnoreCase);
            if (directTitlePos >= 0)
            {
                int titleEnd = directTitlePos + subChapterTitle.Length;
                
                // Saltar espacios después del título
                while (titleEnd < singleLineText.Length && char.IsWhiteSpace(singleLineText[titleEnd]))
                {
                    titleEnd++;
                }
                
                Console.WriteLine($"✅ Direct title match found, content starts at position {titleEnd}");
                return titleEnd;
            }
            
            // Si todo falla, usar una estimación conservadora
            int conservativeEnd = titleStartPosition + Math.Min(subChapterTitle.Length + 10, singleLineText.Length - titleStartPosition);
            Console.WriteLine($"⚠️ Using conservative estimation, content starts at position {conservativeEnd}");
            return conservativeEnd;
        }

        /// <summary>
        /// Clean extracted content and restore readable formatting
        /// </summary>
        private string CleanAndFormatExtractedContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // Limpiar espacios múltiples
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ");
            
            // Restaurar saltos de línea para mejor legibilidad
            content = content.Replace(". ", ".\n");
            content = content.Replace("? ", "?\n");
            content = content.Replace("! ", "!\n");
            
            // Limpiar líneas vacías múltiples
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\n\s*\n", "\n");
            
            return content.Trim();
        }

        /// <summary>
        /// Normalize text for search (improved version)
        /// </summary>
        private string NormalizeTextForSearch(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Normalizar espacios y eliminar puntuación que puede interferir
            var normalized = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
            normalized = normalized.Replace(".", "").Replace(",", "").Replace(":", "").Replace(";", "");
            
            return normalized.Trim();
        }

        /// <summary>
        /// Remove leading numbers from text for flexible matching
        /// </summary>
        private string RemoveLeadingNumbers(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Remove Roman numerals at the beginning
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[IVXLCDM]+\.?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Remove Arabic numbers at the beginning
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\d+\.?\s*", "");
            
            // Remove letters followed by a dot at the beginning
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[A-Z]\.?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return text.Trim();
        }

        /// <summary>
        /// Create an ExractedChapterData object from the extracted information
        /// Updated to create only chapter-level data without subchapter details
        /// </summary>
        private ExractedChapterData CreateExtractedChapterData(
            ChapterIndex chapter,
            string chapterText,
            string twinID,
            AiTokrens tokenService)
        {
            try
            {
                // Count tokens for chapter text
                var chapterTokens = tokenService.GetTokenCount(chapterText ?? "");

                // Determine page ranges
                int fromPageChapter = chapter.PageFrom;
                int toPageChapter = chapter.PageTo;

                return new ExractedChapterData
                {
                    // Basic identifiers
                    id = Guid.NewGuid().ToString(),
                    TwinID = twinID,
                    ChapterID = Guid.NewGuid().ToString(),

                    // Chapter information
                    ChapterTitle = chapter.ChapterTitle ?? string.Empty,
                    TextChapter = chapterText ?? string.Empty,
                    FromPageChapter = fromPageChapter,
                    ToPageChapter = toPageChapter,
                    TotalTokensChapter = chapterTokens,

                    // Initialize empty list of subchapters
                    SubChapters = new List<ExractedSubChapterData>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating ExractedChapterData: {ex.Message}");
                
                // Return a minimal object in case of error
                return new ExractedChapterData
                {
                    id = Guid.NewGuid().ToString(),
                    TwinID = twinID,
                    ChapterID = Guid.NewGuid().ToString(),
                    ChapterTitle = chapter.ChapterTitle ?? string.Empty,
                    SubChapters = new List<ExractedSubChapterData>()
                };
            }
        }

        /// <summary>
        /// Create an ExractedSubChapterData object from the extracted subchapter information
        /// </summary>
        private ExractedSubChapterData CreateExtractedSubChapterData(
            string subChapterTitle,
            string subChapterText,
            string chapterID,
            int fromPage,
            int toPage,
            AiTokrens tokenService)
        {
            try
            {
                // Count tokens for subchapter text
                var subChapterTokens = tokenService.GetTokenCount(subChapterText ?? "");

                return new ExractedSubChapterData
                {
                    // Basic identifiers
                    id = Guid.NewGuid().ToString(),
                    ChapterID = chapterID,

                    // Subchapter information
                    SubChapter = subChapterTitle ?? string.Empty,
                    TitleSub = subChapterTitle ?? string.Empty,
                    SubChapterText = subChapterText ?? string.Empty,
                    TotalTokensSub = subChapterTokens,
                    FromPageSub = fromPage,
                    ToPageSub = toPage,

                    // Chapter-level token count (for reference)
                    TotalTokensChapter = subChapterTokens // This could be updated later with full chapter tokens if needed
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating ExractedSubChapterData for '{subChapterTitle}': {ex.Message}");
                
                // Return a minimal object in case of error
                return new ExractedSubChapterData
                {
                    id = Guid.NewGuid().ToString(),
                    ChapterID = chapterID,
                    SubChapter = subChapterTitle ?? string.Empty,
                    TitleSub = subChapterTitle ?? string.Empty,
                    SubChapterText = string.Empty,
                    TotalTokensSub = 0,
                    FromPageSub = fromPage,
                    ToPageSub = toPage,
                    TotalTokensChapter = 0
                };
            }
        }
    }

    public class ExractedChapterData
    {
        [System.Text.Json.Serialization.JsonPropertyName("chapter")]
        public string ChapterTitle { get; set; } = string.Empty;

        public string id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
         
        public string ChapterID { get; set; } = string.Empty;
        public string TextChapter { get; set; } = string.Empty;
        public int FromPageChapter { get; set; }
        public int ToPageChapter { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("totalTokens")]
        public int TotalTokensChapter { get; set; }
         
        public List<ExractedSubChapterData> SubChapters { get; set; } = new List<ExractedSubChapterData>();

    }


    public class ExractedSubChapterData
    { 
        public string id { get; set; } = string.Empty; 

        public string SubChapter { get; set; } = string.Empty;

        public string ChapterID { get; set; } = string.Empty;  

        [System.Text.Json.Serialization.JsonPropertyName("totalTokens")]
        public int TotalTokensChapter { get; set; }

        public string TitleSub { get; set; } = string.Empty;
        public string SubChapterText { get; set; } = string.Empty;
        public int TotalTokensSub { get; set; }
        public int FromPageSub { get; set; }
        public int ToPageSub { get; set; }

    }
}
