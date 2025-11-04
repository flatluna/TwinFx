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
        {
            Console.WriteLine("❌ ExtractSections: Input validation failed - fullText or index is null/empty");
            return results;
        }

        try
        {
            // Initialize token counter service
            AiTokrens tokenService = new AiTokrens();
            
            Console.WriteLine($"🚀 ExtractSections: Starting extraction for {index.Count} chapters");
            Console.WriteLine($"📄 Document length: {fullText.Length:N0} characters");
            
            // Split document by page markers for better handling
            var pageBlocks = SplitDocumentByPages(fullText);
            Console.WriteLine($"📋 Document has {pageBlocks.Count} page blocks");

            // DEBUG: Show chapter titles we're looking for
            Console.WriteLine("🔍 Chapter titles to find:");
            for (int i = 0; i < index.Count; i++)
            {
                Console.WriteLine($"  {i + 1:D2}: \"{index[i].ChapterTitle}\"");
            }
            Console.WriteLine();

            // Process each chapter individually
            for (int chapterIndex = 0; chapterIndex < index.Count; chapterIndex++)
            {
                var currentChapter = index[chapterIndex];
                var nextChapter = (chapterIndex + 1 < index.Count) ? index[chapterIndex + 1] : null;
                
                Console.WriteLine($"\n📚 [{chapterIndex + 1}/{index.Count}] Processing chapter: \"{currentChapter.ChapterTitle}\"");
                
                // Find chapter content using page-aware extraction
                var chapterContent = ExtractChapterContentByPages(pageBlocks, currentChapter, nextChapter);
                
                if (!string.IsNullOrEmpty(chapterContent))
                {
                    Console.WriteLine($"   ✅ Found chapter content: {chapterContent.Length:N0} characters");
                    
                    // Check if chapter has subchapters
                    bool hasSubchapters = currentChapter.Subchapters != null && currentChapter.Subchapters.Any();
                    Console.WriteLine($"   📋 Has subchapters: {hasSubchapters} ({currentChapter.Subchapters?.Count ?? 0} subchapters)");
                    
                    if (hasSubchapters)
                    {
                        // Process subchapters within this chapter
                        ProcessChapterWithSubchaptersPageAware(currentChapter, chapterContent, tokenService, results);
                    }
                    else
                    {
                        // Process chapter without subchapters
                        ProcessChapterWithoutSubchaptersPageAware(currentChapter, chapterContent, tokenService, results);
                    }
                }
                else
                {
                    Console.WriteLine($"   ❌ Chapter content NOT FOUND: \"{currentChapter.ChapterTitle}\"");
                    
                    // DEBUG: Try to find the chapter title in any page
                    DebugFindChapterInPages(pageBlocks, currentChapter.ChapterTitle);
                }
            }

            Console.WriteLine($"\n✅ ExtractSections completed: Processed {index.Count} chapters, extracted {results.Count} sections");
            
            // Summary of results
            if (results.Count == 0)
            {
                Console.WriteLine("❌ WARNING: No sections were extracted!");
                Console.WriteLine("📋 This suggests that chapter titles in the JSON don't match the document format");
            }
            else
            {
                var foundChapters = results.Select(r => r.Chapter).Distinct().ToList();
                Console.WriteLine($"📋 Successfully extracted {foundChapters.Count} unique chapters:");
                foundChapters.ForEach(c => Console.WriteLine($"  ✅ {c}"));
                
                if (foundChapters.Count < index.Count)
                {
                    var missingChapters = index.Select(c => c.ChapterTitle).Except(foundChapters).ToList();
                    Console.WriteLine($"📋 Missing {missingChapters.Count} chapters:");
                    missingChapters.ForEach(c => Console.WriteLine($"  ❌ {c}"));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in ExtractSections: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }

        return results;
    }

    /// <summary>
    /// Split document into page blocks for better processing
    /// </summary>
    private List<PageBlock> SplitDocumentByPages(string fullText)
    {
        var pageBlocks = new List<PageBlock>();
        var lines = fullText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        PageBlock currentPage = null;
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Check if this is a page marker
            var pageMatch = Regex.Match(trimmedLine, @"===\s*PÁGINA\s+(\d+)\s*===", RegexOptions.IgnoreCase);
            if (pageMatch.Success)
            {
                // Save previous page if exists
                if (currentPage != null)
                {
                    pageBlocks.Add(currentPage);
                }
                
                // Start new page
                currentPage = new PageBlock
                {
                    PageNumber = int.Parse(pageMatch.Groups[1].Value),
                    Content = new List<string>()
                };
            }
            else if (currentPage != null)
            {
                // Add line to current page
                currentPage.Content.Add(trimmedLine);
            }
        }
        
        // Add last page
        if (currentPage != null)
        {
            pageBlocks.Add(currentPage);
        }
        
        return pageBlocks;
    }

    /// <summary>
    /// Extract chapter content using page-aware search
    /// </summary>
    private string ExtractChapterContentByPages(List<PageBlock> pageBlocks, ChapterIndex currentChapter, ChapterIndex? nextChapter)
    {
        Console.WriteLine($"   🔍 Searching for chapter in {pageBlocks.Count} pages...");
        
        // Find the page where this chapter starts
        int startPageIndex = -1;
        int startLineIndex = -1;
        
        for (int pageIndex = 0; pageIndex < pageBlocks.Count; pageIndex++)
        {
            var page = pageBlocks[pageIndex];
            for (int lineIndex = 0; lineIndex < page.Content.Count; lineIndex++)
            {
                var line = page.Content[lineIndex];
                
                if (IsChapterTitleMatch(line, currentChapter.ChapterTitle))
                {
                    startPageIndex = pageIndex;
                    startLineIndex = lineIndex;
                    Console.WriteLine($"   ✅ Found chapter start at Page {page.PageNumber}, Line {lineIndex + 1}: \"{line}\"");
                    break;
                }
            }
            if (startPageIndex >= 0) break;
        }
        
        if (startPageIndex < 0)
        {
            Console.WriteLine($"   ❌ Chapter title not found: \"{currentChapter.ChapterTitle}\"");
            return string.Empty;
        }
        
        // Find where this chapter ends (start of next chapter or end of document)
        int endPageIndex = pageBlocks.Count - 1;
        int endLineIndex = pageBlocks[endPageIndex].Content.Count - 1;
        
        if (nextChapter != null)
        {
            for (int pageIndex = startPageIndex; pageIndex < pageBlocks.Count; pageIndex++)
            {
                var page = pageBlocks[pageIndex];
                int searchStartLine = (pageIndex == startPageIndex) ? startLineIndex + 1 : 0;
                
                for (int lineIndex = searchStartLine; lineIndex < page.Content.Count; lineIndex++)
                {
                    var line = page.Content[lineIndex];
                    
                    if (IsChapterTitleMatch(line, nextChapter.ChapterTitle))
                    {
                        endPageIndex = pageIndex;
                        endLineIndex = lineIndex - 1;
                        Console.WriteLine($"   🔚 Found chapter end at Page {page.PageNumber}, Line {lineIndex}: Next chapter found");
                        goto EndSearch;
                    }
                }
            }
            EndSearch:;
        }
        
        // Extract content between start and end
        var contentLines = new List<string>();
        
        for (int pageIndex = startPageIndex; pageIndex <= endPageIndex; pageIndex++)
        {
            var page = pageBlocks[pageIndex];
            int startLine = (pageIndex == startPageIndex) ? startLineIndex : 0;
            int endLine = (pageIndex == endPageIndex) ? Math.Min(endLineIndex, page.Content.Count - 1) : page.Content.Count - 1;
            
            // Add page marker for reference
            contentLines.Add($"=== PÁGINA {page.PageNumber} ===");
            
            for (int lineIndex = startLine; lineIndex <= endLine; lineIndex++)
            {
                if (lineIndex < page.Content.Count)
                {
                    contentLines.Add(page.Content[lineIndex]);
                }
            }
        }
        
        var chapterContent = string.Join("\n", contentLines);
        Console.WriteLine($"   📏 Extracted content: {chapterContent.Length} characters from page {pageBlocks[startPageIndex].PageNumber} to {pageBlocks[endPageIndex].PageNumber}");
        
        return chapterContent;
    }

    /// <summary>
    /// Check if a line matches a chapter title using flexible comparison
    /// </summary>
    private bool IsChapterTitleMatch(string line, string chapterTitle)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(chapterTitle))
            return false;

        // Strategy 1: Direct exact match
        if (string.Equals(line.Trim(), chapterTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // Strategy 2: Contains match
        if (line.IndexOf(chapterTitle, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Strategy 3: Normalized comparison
        var normalizedLine = NormalizeForComparison(line);
        var normalizedTitle = NormalizeForComparison(chapterTitle);
        if (string.Equals(normalizedLine, normalizedTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Normalize text for comparison (remove extra spaces, punctuation)
    /// </summary>
    private string NormalizeForComparison(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        
        // Remove extra spaces and punctuation, keep only letters, numbers, and single spaces
        var normalized = Regex.Replace(text, @"[^\w\s]", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Process chapter with subchapters using page-aware extraction
    /// </summary>
    private void ProcessChapterWithSubchaptersPageAware(ChapterIndex currentChapter, string chapterContent, 
        AiTokrens tokenService, List<SectionResult> results)
    {
        Console.WriteLine($"   📋 Processing {currentChapter.Subchapters.Count} subchapters...");
        
        var lines = chapterContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var foundSubchapters = new List<(int LineIndex, string Title)>();
        
        // Find all subchapters within this chapter
        foreach (var subchapter in currentChapter.Subchapters)
        {
            Console.WriteLine($"      🔍 Looking for subchapter: \"{subchapter}\"");
            
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                
                // Look for subchapter title patterns
                if (IsSubchapterTitleMatch(line, subchapter))
                {
                    foundSubchapters.Add((i, subchapter));
                    Console.WriteLine($"      ✅ Found: '{subchapter}' at line {i + 1}: \"{line}\"");
                    break;
                }
            }
        }

        // Sort by line position
        foundSubchapters = foundSubchapters.OrderBy(x => x.LineIndex).ToList();
        Console.WriteLine($"   📊 Found {foundSubchapters.Count}/{currentChapter.Subchapters.Count} subchapters");
        
        // Extract content for each subchapter
        for (int i = 0; i < foundSubchapters.Count; i++)
        {
            var current = foundSubchapters[i];
            int startLine = current.LineIndex;
            int endLine = (i + 1 < foundSubchapters.Count) 
                ? foundSubchapters[i + 1].LineIndex - 1 
                : lines.Count - 1;
            
            // Extract subchapter text (skip the title line)
            var subchapterLines = lines.Skip(startLine + 1).Take(endLine - startLine).ToList();
            var subchapterText = string.Join("\n", subchapterLines);
            
            // Count tokens
            var tokenCount = tokenService.GetTokenCount(subchapterText);
            
            // Extract page numbers from content
            var (startPage, endPage) = ExtractPageNumbersFromContent(subchapterText);

            // Create SubChapter object
            var subChapter = new SubChapter
            {
                Chapter = currentChapter.ChapterTitle,
                Ttitle = current.Title,
                Text = subchapterText.Trim(),
                TotalTokens = tokenCount,
                FromPage = startPage,
                ToPage = endPage
            };

            // Create SectionResult
            results.Add(new SectionResult
            {
                Chapter = currentChapter.ChapterTitle,
                FromPage = startPage,
                ToPage = endPage,
                Subchapter = subChapter
            });

            Console.WriteLine($"      📄 Extracted: {current.Title} - {subchapterText.Length} chars, {tokenCount} tokens, Pages {startPage}-{endPage}");
        }
    }

    /// <summary>
    /// Check if a line matches a subchapter title
    /// </summary>
    private bool IsSubchapterTitleMatch(string line, string subchapterTitle)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(subchapterTitle))
            return false;

        // Look for patterns like "1. INTRODUCTION", "2. KEY POINTS", etc.
        var trimmedLine = line.Trim();
        
        // Direct match
        if (trimmedLine.Equals(subchapterTitle, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Contains match
        if (trimmedLine.IndexOf(subchapterTitle, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        
        // Pattern matching for numbered sections
        var numberPattern = @"^\d+\.\s*(.+)";
        var match = Regex.Match(trimmedLine, numberPattern);
        if (match.Success)
        {
            var titlePart = match.Groups[1].Value.Trim();
            if (titlePart.Equals(subchapterTitle.Replace("1. ", "").Replace("2. ", "").Replace("3. ", "").Replace("4. ", ""), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// Process chapter without subchapters using page-aware extraction
    /// </summary>
    private void ProcessChapterWithoutSubchaptersPageAware(ChapterIndex currentChapter, string chapterContent, 
        AiTokrens tokenService, List<SectionResult> results)
    {
        Console.WriteLine($"   📄 Processing chapter without subchapters...");
        
        // Remove the chapter title line from the beginning
        var lines = chapterContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        
        // Find and remove the title line
        for (int i = 0; i < Math.Min(3, lines.Count); i++) // Check first 3 lines
        {
            if (IsChapterTitleMatch(lines[i], currentChapter.ChapterTitle))
            {
                lines.RemoveAt(i);
                break;
            }
        }
        
        var chapterText = string.Join("\n", lines);
        var chapterTokenCount = tokenService.GetTokenCount(chapterText);
        
        // Extract page numbers from content
        var (startPage, endPage) = ExtractPageNumbersFromContent(chapterContent);

        // Create SubChapter object with chapter data
        var chapterAsSubchapter = new SubChapter
        {
            Chapter = currentChapter.ChapterTitle,
            Ttitle = currentChapter.ChapterTitle,
            Text = chapterText.Trim(),
            TotalTokens = chapterTokenCount,
            FromPage = startPage,
            ToPage = endPage
        };

        // Create SectionResult
        results.Add(new SectionResult
        {
            Chapter = currentChapter.ChapterTitle,
            FromPage = startPage,
            ToPage = endPage,
            Subchapter = chapterAsSubchapter
        });

        Console.WriteLine($"   📄 Extracted full chapter: {chapterText.Length} chars, {chapterTokenCount} tokens, Pages {startPage}-{endPage}");
    }

    /// <summary>
    /// Extract page numbers from content that contains page markers
    /// </summary>
    private (int StartPage, int EndPage) ExtractPageNumbersFromContent(string content)
    {
        var pageNumbers = new List<int>();
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var pageMatch = Regex.Match(line.Trim(), @"===\s*PÁGINA\s+(\d+)\s*===", RegexOptions.IgnoreCase);
            if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out int pageNum))
            {
                pageNumbers.Add(pageNum);
            }
        }
        
        if (pageNumbers.Count > 0)
        {
            pageNumbers = pageNumbers.Distinct().OrderBy(p => p).ToList();
            return (pageNumbers.First(), pageNumbers.Last());
        }
        
        return (1, 1); // Default fallback
    }

    /// <summary>
    /// Debug helper to find chapter in pages
    /// </summary>
    private void DebugFindChapterInPages(List<PageBlock> pageBlocks, string chapterTitle)
    {
        Console.WriteLine($"      🔍 Debugging search for: \"{chapterTitle}\"");
        
        var words = chapterTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                               .Where(w => w.Length > 2)
                               .ToArray();
        
        foreach (var page in pageBlocks)
        {
            for (int lineIndex = 0; lineIndex < page.Content.Count; lineIndex++)
            {
                var line = page.Content[lineIndex];
                
                // Check if line contains any significant words from the title
                var foundWords = words.Count(word => line.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                if (foundWords > 0)
                {
                    Console.WriteLine($"         Page {page.PageNumber}, Line {lineIndex + 1}: \"{line}\" (matches {foundWords}/{words.Length} words)");
                }
            }
        }
    }

    /// <summary>
    /// Helper class for page blocks
    /// </summary>
    private class PageBlock
    {
        public int PageNumber { get; set; }
        public List<string> Content { get; set; } = new List<string>();
    }
}