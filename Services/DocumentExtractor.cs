// Wrapper for your JSON root: { "index": [ ... ] }  
using System.Text.Json;
using System.Text.RegularExpressions;
using TwinFx.Agents;
using TwinFx.Services;

public class IndexWrapper
{
    public List<ChapterIndex> Index { get; set; } = new List<ChapterIndex>();
}

// Result for each extracted section  
public class SectionResult
{
    public string Chapter { get; set; } = string.Empty;
    public int FromPage { get; set; }
    public int ToPage { get; set; }
    public SubChapter Subchapter { get; set; } = new SubChapter();
}

public class SubChapter
{
    [System.Text.Json.Serialization.JsonPropertyName("chapter")]
    public string Chapter { get; set; } = string.Empty;

    public string Ttitle { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public int TotalTokens { get; set; }
    public int FromPage { get; set; }
    public int ToPage { get; set; }
}

// Simple extractor: finds each subchapter heading and takes the text until the next heading  
public class DocumentExtractor
{
    public List<SectionResult> ExtractSections(string fullText, List<ChapterIndex> index)
    {
        var results = new List<SectionResult>();
        if (string.IsNullOrWhiteSpace(fullText) || index == null || index.Count == 0)
            return results;

        try
        {
            // Initialize token counter service
            AiTokrens tokenService = new AiTokrens();
            
            // Process each chapter individually
            for (int chapterIndex = 0; chapterIndex < index.Count; chapterIndex++)
            {
                var currentChapter = index[chapterIndex];
                var nextChapter = (chapterIndex + 1 < index.Count) ? index[chapterIndex + 1] : null;
                
                // Generate unique ChapterID
                string chapterID = Guid.NewGuid().ToString();
                
                // Check if chapter has subchapters
                bool hasSubchapters = currentChapter.Subchapters != null && currentChapter.Subchapters.Any();
                
                if (hasSubchapters)
                {
                    // Chapter HAS subchapters - process each subchapter section by section
                    Console.WriteLine($"📚 Processing chapter with subchapters: {currentChapter.ChapterTitle}");
                    
                    // First, find the current chapter's position in the text
                    var chapterPosition = fullText.IndexOf(currentChapter.ChapterTitle, StringComparison.OrdinalIgnoreCase);
                    if (chapterPosition == -1)
                    {
                        var cleanChapterTitle = CleanHeadingForSearch(currentChapter.ChapterTitle);
                        chapterPosition = FindAlternativeHeading(fullText, cleanChapterTitle, currentChapter.ChapterTitle);
                    }
                    
                    if (chapterPosition >= 0)
                    {
                        // Find where this chapter ends (start of next chapter or end of document)
                        int chapterEndPos = fullText.Length;
                        if (nextChapter != null)
                        {
                            var nextChapterPosition = fullText.IndexOf(nextChapter.ChapterTitle, StringComparison.OrdinalIgnoreCase);
                            if (nextChapterPosition == -1)
                            {
                                var cleanNextTitle = CleanHeadingForSearch(nextChapter.ChapterTitle);
                                nextChapterPosition = FindAlternativeHeading(fullText, cleanNextTitle, nextChapter.ChapterTitle);
                            }
                            if (nextChapterPosition > chapterPosition)
                            {
                                chapterEndPos = nextChapterPosition;
                            }
                        }
                        
                        // Extract the full chapter text to work within
                        var chapterText = fullText.Substring(chapterPosition, chapterEndPos - chapterPosition);
                        
                        // Find positions of each subchapter within this chapter text
                        var foundPositions = new List<(int Position, string Subchapter, string OriginalHeading)>();

                        foreach (var subchapter in currentChapter.Subchapters)
                        {
                            // Search for the subchapter heading in the chapter text (case-insensitive)
                            var position = chapterText.IndexOf(subchapter, StringComparison.OrdinalIgnoreCase);
                            
                            if (position >= 0)
                            {
                                foundPositions.Add((position, subchapter, subchapter));
                                Console.WriteLine($"✅ Found: '{subchapter}' at position {position} within chapter");
                            }
                            else
                            {
                                // Try alternative search patterns if direct match fails
                                var cleanSubchapter = CleanHeadingForSearch(subchapter);
                                var alternativePosition = FindAlternativeHeading(chapterText, cleanSubchapter, subchapter);
                                
                                if (alternativePosition >= 0)
                                {
                                    foundPositions.Add((alternativePosition, subchapter, subchapter));
                                    Console.WriteLine($"✅ Found alternative: '{subchapter}' at position {alternativePosition} within chapter");
                                }
                                else
                                {
                                    Console.WriteLine($"❌ Not found: '{subchapter}' within chapter");
                                }
                            }
                        }

                        // Sort by position within chapter text to maintain document order
                        foundPositions = foundPositions.OrderBy(x => x.Position).ToList();

                        // Extract text between positions for each subchapter
                        for (int i = 0; i < foundPositions.Count; i++)
                        {
                            var current = foundPositions[i];
                            var startPos = current.Position;
                            
                            // Find the end position (start of next subchapter or end of chapter)
                            var endPos = (i + 1 < foundPositions.Count) 
                                ? foundPositions[i + 1].Position 
                                : chapterText.Length;

                            // Extract the text between start and end
                            var sectionText = chapterText.Substring(startPos, endPos - startPos);
                            
                            // Remove the heading itself from the beginning
                            var headingLength = current.OriginalHeading.Length;
                            if (sectionText.Length > headingLength)
                            {
                                sectionText = sectionText.Substring(headingLength).TrimStart();
                            }

                            // Count tokens for this section
                            var tokenCount = tokenService.GetTokenCount(sectionText);
                            
                            // Calculate actual page numbers using improved detection
                            var absoluteStartPos = chapterPosition + startPos;
                            var (actualStartPage, actualEndPage) = CalculateAbsolutePageNumbers(fullText, sectionText, absoluteStartPos);

                            // Create SubChapter object with extracted data
                            var subChapter = new SubChapter
                            {
                                Chapter = currentChapter.ChapterTitle,
                                Ttitle = current.Subchapter,
                                Text = sectionText.Trim(),
                                TotalTokens = tokenCount,
                                FromPage = actualStartPage,
                                ToPage = actualEndPage
                            };

                            // Create SectionResult
                            results.Add(new SectionResult
                            {
                                Chapter = currentChapter.ChapterTitle,
                                FromPage = actualStartPage,
                                ToPage = actualEndPage,
                                Subchapter = subChapter
                            });

                            Console.WriteLine($"📄 Extracted subchapter: {current.Subchapter} - {sectionText.Length} characters, {tokenCount} tokens, Pages {actualStartPage}-{actualEndPage}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Chapter not found in text: {currentChapter.ChapterTitle}");
                    }
                }
                else
                {
                    // Chapter has NO subchapters - extract full chapter text
                    Console.WriteLine($"📚 Processing chapter with no subchapters: {currentChapter.ChapterTitle}");
                    
                    // Find current chapter position in text
                    var chapterPosition = fullText.IndexOf(currentChapter.ChapterTitle, StringComparison.OrdinalIgnoreCase);
                    if (chapterPosition == -1)
                    {
                        // Try alternative search for chapter title
                        var cleanChapterTitle = CleanHeadingForSearch(currentChapter.ChapterTitle);
                        chapterPosition = FindAlternativeHeading(fullText, cleanChapterTitle, currentChapter.ChapterTitle);
                    }
                    
                    if (chapterPosition >= 0)
                    {
                        // Find where this chapter ends (start of next chapter or end of document)
                        int chapterEndPos = fullText.Length;
                        
                        if (nextChapter != null)
                        {
                            var nextChapterPosition = fullText.IndexOf(nextChapter.ChapterTitle, StringComparison.OrdinalIgnoreCase);
                            if (nextChapterPosition == -1)
                            {
                                var cleanNextTitle = CleanHeadingForSearch(nextChapter.ChapterTitle);
                                nextChapterPosition = FindAlternativeHeading(fullText, cleanNextTitle, nextChapter.ChapterTitle);
                            }
                            
                            if (nextChapterPosition > chapterPosition)
                            {
                                chapterEndPos = nextChapterPosition;
                            }
                        }
                        
                        // Extract the full chapter text
                        var chapterText = fullText.Substring(chapterPosition, chapterEndPos - chapterPosition);
                        
                        // Remove the chapter title from the beginning
                        var titleLength = currentChapter.ChapterTitle.Length;
                        if (chapterText.Length > titleLength)
                        {
                            chapterText = chapterText.Substring(titleLength).TrimStart();
                        }
                        
                        // Count tokens for the entire chapter
                        var chapterTokenCount = tokenService.GetTokenCount(chapterText);
                        
                        // Calculate actual page numbers using improved detection
                        var (actualStartPage, actualEndPage) = CalculateAbsolutePageNumbers(fullText, chapterText, chapterPosition);

                        // Create a SubChapter object with chapter data (since there are no actual subchapters)
                        var chapterAsSubchapter = new SubChapter
                        {
                            Chapter = currentChapter.ChapterTitle,
                            Ttitle = currentChapter.ChapterTitle, // Use chapter title as subchapter title
                            Text = chapterText.Trim(),
                            TotalTokens = chapterTokenCount,
                            FromPage = actualStartPage,
                            ToPage = actualEndPage
                        };

                        // Create SectionResult for the chapter
                        results.Add(new SectionResult
                        {
                            Chapter = currentChapter.ChapterTitle,
                            FromPage = actualStartPage,
                            ToPage = actualEndPage,
                            Subchapter = chapterAsSubchapter
                        });

                        Console.WriteLine($"📄 Extracted full chapter: {currentChapter.ChapterTitle} - {chapterText.Length} characters, {chapterTokenCount} tokens, Pages {actualStartPage}-{actualEndPage}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Chapter not found in text: {currentChapter.ChapterTitle}");
                    }
                }
            }

            Console.WriteLine($"✅ Successfully processed {index.Count} chapters, extracted {results.Count} sections");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error extracting sections: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Clean heading for alternative search patterns
    /// </summary>
    private string CleanHeadingForSearch(string heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
            return "";

        // Remove common prefixes and normalize for search
        var cleaned = heading.Trim();
        
        // Remove numbers, letters, and punctuation at the start
        cleaned = Regex.Replace(cleaned, @"^[a-zA-Z0-9]+[\.\)\s]*", "", RegexOptions.IgnoreCase);
        
        return cleaned.Trim();
    }

    /// <summary>
    /// Find alternative heading patterns when direct search fails
    /// </summary>
    private int FindAlternativeHeading(string fullText, string cleanHeading, string originalHeading)
    {
        // Try searching for the clean version
        var position = fullText.IndexOf(cleanHeading, StringComparison.OrdinalIgnoreCase);
        if (position >= 0)
            return position;

        // Try searching for individual words
        var words = originalHeading.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            // Look for lines containing most of the words
            var lines = fullText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var matchingWords = words.Count(word => 
                    line.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                
                // If most words match (at least 70%), consider it a match
                if ((double)matchingWords / words.Length >= 0.7)
                {
                    return fullText.IndexOf(line, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return -1; // Not found
    }

    /// <summary>
    /// Extract actual page numbers from text content using page markers
    /// </summary>
    /// <param name="text">Text content that may contain page markers</param>
    /// <returns>Tuple with start page and end page numbers</returns>
    private (int StartPage, int EndPage) ExtractActualPageNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (1, 1);

        var pageNumbers = new List<int>();
        
        // Look for page markers in format "=== PÁGINA X ===" or similar patterns
        var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Match patterns like "=== PÁGINA 5 ===" or "=== PAGE 5 ==="
            var pageMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, 
                @"===\s*(P[ÁA]GINA|PAGE)\s+(\d+)\s*===", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (pageMatch.Success && int.TryParse(pageMatch.Groups[2].Value, out int pageNum))
            {
                pageNumbers.Add(pageNum);
            }
            
            // Also match simpler patterns like "Page 5" or "Página 5"
            var simplePageMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, 
                @"(P[ÁA]GINA|PAGE)\s+(\d+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (simplePageMatch.Success && int.TryParse(simplePageMatch.Groups[2].Value, out int simplePageNum))
            {
                pageNumbers.Add(simplePageNum);
            }
        }
        
        if (pageNumbers.Count > 0)
        {
            // Remove duplicates and sort
            pageNumbers = pageNumbers.Distinct().OrderBy(p => p).ToList();
            return (pageNumbers.First(), pageNumbers.Last());
        }
        
        // Fallback to word count estimation if no page markers found
        var estimatedWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var estimatedPages = Math.Max(1, (int)Math.Ceiling(estimatedWords / 250.0));
        return (1, estimatedPages);
    }

    /// <summary>
    /// Calculate absolute page numbers based on position in full document
    /// </summary>
    /// <param name="fullText">Complete document text</param>
    /// <param name="sectionText">Section text to locate</param>
    /// <param name="absoluteStartPos">Absolute position in document</param>
    /// <returns>Tuple with start page and end page numbers</returns>
    private (int StartPage, int EndPage) CalculateAbsolutePageNumbers(string fullText, string sectionText, int absoluteStartPos)
    {
        // First try to get actual page numbers from the section text
        var (sectionStartPage, sectionEndPage) = ExtractActualPageNumbers(sectionText);
        
        // If we found actual page markers, use them
        if (sectionStartPage > 1 || sectionEndPage > 1)
        {
            return (sectionStartPage, sectionEndPage);
        }
        
        // Fallback: Calculate based on position in document and page markers before this position
        var textBeforeCurrent = fullText.Substring(0, absoluteStartPos);
        var pageNumbersBeforeCurrent = new List<int>();
        
        var lines = textBeforeCurrent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var pageMatch = System.Text.RegularExpressions.Regex.Match(line.Trim(), 
                @"===\s*(P[ÁA]GINA|PAGE)\s+(\d+)\s*===", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (pageMatch.Success && int.TryParse(pageMatch.Groups[2].Value, out int pageNum))
            {
                pageNumbersBeforeCurrent.Add(pageNum);
            }
        }
        
        int startPage = pageNumbersBeforeCurrent.Count > 0 ? pageNumbersBeforeCurrent.Max() : 1;
        
        // Calculate end page based on content length
        var estimatedWords = sectionText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var estimatedPages = Math.Max(1, (int)Math.Ceiling(estimatedWords / 250.0));
        int endPage = startPage + estimatedPages - 1;
        
        return (startPage, endPage);
    }
}