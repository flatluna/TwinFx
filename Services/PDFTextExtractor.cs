using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using TwinFx.Agents;

namespace TwinFx.Services
{ 

    public   class DocumentSectionExtractor
    {
        // Firma requerida por ti  
        public   List<SectionResult> ExtractSectionsFromDocument(string textWithPages, List<ChapterIndex> index)
        {
            var results = new List<SectionResult>();
            if (string.IsNullOrWhiteSpace(textWithPages) || index == null || index.Count == 0)
                return results;

            // 1) Parsear páginas marcadas con "=== PÁGINA N ==="  
            var pages = ParsePages(textWithPages);
            if (pages.Count == 0)
            {
                pages[1] = textWithPages;
            }

            // 2) Concatenar páginas y registrar offset de inicio por página  
            var pageStarts = new SortedDictionary<int, int>();
            var sb = new StringBuilder();
            foreach (var kv in pages.OrderBy(k => k.Key))
            {
                pageStarts[kv.Key] = sb.Length;
                sb.Append(kv.Value);
                sb.Append("\n\n"); // separador artificial  
            }
            string concat = sb.ToString();

            // 3) Normalizar versiones de comparación del concat (para búsquedas alternativas)  
            // We'll primarily search in the original concat, but we also generate a whitespace-collapsed variant for some needle variants.  
            string concatCollapsed = CollapseWhitespace(concat);

            // 4) Encontrar posiciones de capítulos según el índice  
            var chapterPositions = new List<(ChapterIndex chapterIndex, int pos)>();
            foreach (var ch in index)
            {
                int foundPos = FindBestPositionVariants(concat, concatCollapsed, ch.ChapterTitle);
                if (foundPos < 0)
                {
                    // Intentar sin numeración  
                    string chNoNum = StripLeadingNumbering(ch.ChapterTitle);
                    foundPos = FindBestPositionVariants(concat, concatCollapsed, chNoNum);
                }
                if (foundPos >= 0)
                    chapterPositions.Add((ch, foundPos));
            }
            // ordenar por posición  
            chapterPositions = chapterPositions.OrderBy(c => c.pos).ToList();

            // 5) Para cada capítulo detectado, localizar subcapítulos y extraer textos  
            for (int ci = 0; ci < chapterPositions.Count; ci++)
            {
                var chEntry = chapterPositions[ci];
                int chapterStart = chEntry.pos;
                int chapterEnd = (ci + 1 < chapterPositions.Count) ? chapterPositions[ci + 1].pos : concat.Length;

                // Determinar la primera subposición dentro del capítulo (para separar encabezado del capítulo)  
                int firstSubInChapter = int.MaxValue;
                foreach (var s in chEntry.chapterIndex.Subchapters)
                {
                    int sPos = FindBestPositionVariantsInRange(concat, concatCollapsed, s, chapterStart, chapterEnd);
                    if (sPos < 0)
                    {
                        var sNoNum = StripLeadingNumbering(s);
                        sPos = FindBestPositionVariantsInRange(concat, concatCollapsed, sNoNum, chapterStart, chapterEnd);
                    }
                    if (sPos >= 0 && sPos < firstSubInChapter) firstSubInChapter = sPos;
                }
                int headerEnd = (firstSubInChapter == int.MaxValue) ? chapterEnd : firstSubInChapter;
                string chapterHeaderText = string.Empty;
                if (headerEnd > chapterStart)
                    chapterHeaderText = concat.Substring(chapterStart, headerEnd - chapterStart).Trim();

                // Para cada subcapítulo del capítulo, extraer su rango y texto  
                for (int si = 0; si < chEntry.chapterIndex.Subchapters.Count; si++)
                {
                    var rawSubTitle = chEntry.chapterIndex.Subchapters[si];

                    // Buscar posición preferente dentro del capítulo  
                    int subPos = FindBestPositionVariantsInRange(concat, concatCollapsed, rawSubTitle, chapterStart, chapterEnd);
                    if (subPos < 0)
                    {
                        var subNoNum = StripLeadingNumbering(rawSubTitle);
                        subPos = FindBestPositionVariantsInRange(concat, concatCollapsed, subNoNum, chapterStart, chapterEnd);
                    }
                    // si aún no se encontró, intento global  
                    if (subPos < 0)
                    {
                        subPos = FindBestPositionVariants(concat, concatCollapsed, rawSubTitle);
                        if (subPos < 0)
                        {
                            var subNoNum = StripLeadingNumbering(rawSubTitle);
                            subPos = FindBestPositionVariants(concat, concatCollapsed, subNoNum);
                        }
                    }
                    if (subPos < 0) continue; // no se encontró el subcapítulo; lo ignoramos  

                    // determinar posición del siguiente subcapítulo dentro del mismo capítulo (si existe)  
                    int nextSubPos = chapterEnd;
                    for (int sj = si + 1; sj < chEntry.chapterIndex.Subchapters.Count; sj++)
                    {
                        var other = chEntry.chapterIndex.Subchapters[sj];
                        int posOther = FindBestPositionVariantsInRange(concat, concatCollapsed, other, chapterStart, chapterEnd);
                        if (posOther < 0)
                        {
                            posOther = FindBestPositionVariantsInRange(concat, concatCollapsed, StripLeadingNumbering(other), chapterStart, chapterEnd);
                        }
                        if (posOther > subPos && posOther < nextSubPos) nextSubPos = posOther;
                    }

                    int subStart = subPos;
                    int subEnd = nextSubPos;
                    if (subEnd <= subStart) subEnd = Math.Min(subStart + 1, concat.Length);

                    // Extraer cuerpo del subcapítulo  
                    string subBody = concat.Substring(subStart, Math.Max(0, subEnd - subStart)).Trim();

                    // Formar texto combinado: encabezado del capítulo (si existe) + cuerpo del subcapítulo  
                    string combined = string.IsNullOrWhiteSpace(chapterHeaderText) ? subBody : (chapterHeaderText + "\n\n" + subBody);

                    int fromPage = GetPageForPosition(pageStarts, subStart);
                    int toPage = GetPageForPosition(pageStarts, Math.Max(0, subEnd - 1));

                    var sc = new SubChapter
                    {
                        Chapter = chEntry.chapterIndex.ChapterTitle,
                        Ttitle = StripLeadingNumbering(rawSubTitle),
                        Text = CleanupExtractedText(combined, rawSubTitle),
                        FromPage = fromPage,
                        ToPage = toPage
                    };
                    sc.TotalTokens = CountTokens(sc.Text);

                    var sr = new SectionResult
                    {
                        Chapter = chEntry.chapterIndex.ChapterTitle,
                        FromPage = fromPage,
                        ToPage = toPage,
                        Subchapter = sc
                    };

                    results.Add(sr);
                }
            }

            // Orden final por página de inicio y título  
            return results.OrderBy(r => r.FromPage).ThenBy(r => r.Subchapter.Ttitle).ToList();
        }

        // ---------- Helpers ----------  

        private static SortedDictionary<int, string> ParsePages(string input)
        {
            var pageDict = new SortedDictionary<int, string>();
            if (string.IsNullOrWhiteSpace(input)) return pageDict;

            var pageRegex = new Regex(@"===\s*PÁGINA\s*(\d+)\s*===", RegexOptions.IgnoreCase);
            var matches = pageRegex.Matches(input);
            if (matches.Count == 0)
            {
                pageDict[1] = input;
                return pageDict;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                int pageNum = int.Parse(matches[i].Groups[1].Value);
                int start = matches[i].Index + matches[i].Length;
                int end = (i + 1 < matches.Count) ? matches[i + 1].Index : input.Length;
                string pageText = input.Substring(start, end - start).Trim();
                pageDict[pageNum] = pageText;
            }
            return pageDict;
        }

        // Intenta varias variantes del needle (con y sin numeración, con newlines->espacio, etc.)  
        private static int FindBestPositionVariants(string haystack, string haystackCollapsed, string needle)
        {
            if (string.IsNullOrWhiteSpace(needle) || string.IsNullOrWhiteSpace(haystack)) return -1;

            // variantes del needle a probar (orden de preferencia)  
            var variants = new List<string>();

            variants.Add(needle); // tal cual (frecuentemente coincide)  
            variants.Add(StripLeadingNumbering(needle)); // sin numeración (INTRODUCTION en lugar de "1. INTRODUCTION")  
            variants.Add(CollapseWhitespace(needle)); // colapsando espacios y returns  
            variants.Add(needle.Replace("\n", " ").Replace("\r", " ")); // newlines -> espacios  
            variants.Add(Regex.Replace(needle, @"\s+", " ")); // collapse whitespace alternativa  

            // buscar en haystack (original)  
            foreach (var v in variants.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                int pos = haystack.IndexOf(v, StringComparison.InvariantCultureIgnoreCase);
                if (pos >= 0) return pos;
            }

            // buscar en haystackCollapsed (para coincidencias sin saltos de línea)  
            string needleCollapsed = CollapseWhitespace(needle);
            if (!string.IsNullOrWhiteSpace(haystackCollapsed) && !string.IsNullOrWhiteSpace(needleCollapsed))
            {
                int posCollapsed = haystackCollapsed.IndexOf(needleCollapsed, StringComparison.InvariantCultureIgnoreCase);
                if (posCollapsed >= 0)
                {
                    // mapear posición en haystackCollapsed a posición aproximada en haystack original  
                    // estrategia: buscar la aparición del fragmento (primeros 8-12 caracteres significativos) en el original  
                    string sample = GetSampleForMapping(needleCollapsed);
                    if (!string.IsNullOrEmpty(sample))
                    {
                        int mapPos = haystack.IndexOf(sample, StringComparison.InvariantCultureIgnoreCase);
                        if (mapPos >= 0) return mapPos;
                    }
                    // fallback: no se puede mapear exactamente, devolver -1  
                }
            }

            return -1;
        }

        private static int FindBestPositionVariantsInRange(string haystack, string haystackCollapsed, string needle, int rangeStart, int rangeEnd)
        {
            if (rangeStart < 0) rangeStart = 0;
            if (rangeEnd > haystack.Length) rangeEnd = haystack.Length;
            if (rangeStart >= rangeEnd) return -1;

            string sub = haystack.Substring(rangeStart, rangeEnd - rangeStart);
            string subCollapsed = CollapseWhitespace(sub);

            int pos = FindBestPositionVariants(sub, subCollapsed, needle);
            return pos >= 0 ? (rangeStart + pos) : -1;
        }

        private static string StripLeadingNumbering(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s ?? string.Empty;
            // Quitar patrones al inicio: "1.", "1.1", "I.", "I.", "A."  
            s = Regex.Replace(s, @"^\s*([0-9]+(\.[0-9]+)*)[\.\)]?\s*", "", RegexOptions.Compiled);
            s = Regex.Replace(s, @"^\s*[IVXLCDM]+\.\s*", "", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            s = Regex.Replace(s, @"^\s*[A-Z]\.\s*", "", RegexOptions.Compiled);
            s = s.Trim();
            return s;
        }

        private static string CollapseWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            // Reemplaza cualquier secuencia de whitespace por un solo espacio y trim  
            string r = Regex.Replace(s, @"\s+", " ");
            return r.Trim();
        }

        private static string GetSampleForMapping(string collapsed)
        {
            if (string.IsNullOrWhiteSpace(collapsed)) return string.Empty;
            // devolver una muestra significativa (hasta 30 chars) para mapear de la versión colapsada al original  
            var tokens = collapsed.Split(' ').Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
            if (tokens.Length == 0) return string.Empty;
            // tomar las primeras 3 palabras o hasta 30 caracteres  
            var sample = string.Join(" ", tokens.Take(3));
            if (sample.Length > 30) sample = sample.Substring(0, 30);
            return sample;
        }

        private static int GetPageForPosition(SortedDictionary<int, int> pageStarts, int pos)
        {
            if (pageStarts == null || pageStarts.Count == 0) return 1;
            int lastPage = pageStarts.Keys.Min();
            foreach (var kv in pageStarts)
            {
                if (kv.Value <= pos) lastPage = kv.Key;
                else break;
            }
            return lastPage;
        }

        private static string CleanupExtractedText(string block, string subTitle)
        {
            if (string.IsNullOrWhiteSpace(block)) return string.Empty;
            string res = block.Trim();

            // Si el bloque empieza con el título, intentar eliminarlo para dejar sólo contenido  
            if (!string.IsNullOrEmpty(subTitle))
            {
                var normSub = StripLeadingNumbering(subTitle).Trim();
                if (!string.IsNullOrEmpty(normSub))
                {
                    int idx = res.IndexOf(normSub, StringComparison.InvariantCultureIgnoreCase);
                    if (idx == 0)
                    {
                        res = res.Substring(normSub.Length).TrimStart(new char[] { ' ', '\r', '\n', '\t', ':' });
                    }
                }
            }

            // Normalizar saltos de línea  
            res = Regex.Replace(res, @"\r\n|\r|\n", "\n");
            res = Regex.Replace(res, @"\n{3,}", "\n\n"); // no dejar más de 2 saltos seguidos  
            return res.Trim();
        }

        private static int CountTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var tokens = Regex.Split(text.Trim(), @"\s+").Where(t => !string.IsNullOrWhiteSpace(t));
            return tokens.Count();
        }
    }
}