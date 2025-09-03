namespace TwinFx.Models;

// ========================================================================================
// RESPONSE MODELS FOR WORK DOCUMENTS API
// ========================================================================================

/// <summary>
/// Response model for GET /api/twins/{twinId}/work endpoint
/// Contains structured work documents data optimized for UI display
/// </summary>
public class GetWorkDocumentsResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Twin ID used in the request
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of work documents found for this Twin
    /// </summary>
    public int TotalDocuments { get; set; }
    
    /// <summary>
    /// Array of structured work document data objects, ordered by date (newest first)
    /// </summary>
    public List<WorkDocumentData> WorkDocuments { get; set; } = new();
}

/// <summary>
/// Structured work document data optimized for UI display
/// </summary>
public class WorkDocumentData
{
    // ==================== DOCUMENT METADATA ====================
    
    /// <summary>
    /// Unique document identifier in Cosmos DB
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of work document (resume, cover_letter, etc.)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Original uploaded filename
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Upload timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string UploadedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing completion timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string ProcessedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of days since upload (useful for "X days ago" display)
    /// </summary>
    public int DaysAgo { get; set; }
    
    // ==================== QUICK ACCESS DATA ====================
    
    /// <summary>
    /// Full name extracted from document (for resumes)
    /// </summary>
    public string? FullName { get; set; }
    
    /// <summary>
    /// Email extracted from document (for resumes)
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Current job title (for resumes)
    /// </summary>
    public string? CurrentJobTitle { get; set; }
    
    /// <summary>
    /// Current company (for resumes)
    /// </summary>
    public string? CurrentCompany { get; set; }
    
    /// <summary>
    /// Professional summary/objective from document
    /// </summary>
    public string? Summary { get; set; }
    
    /// <summary>
    /// Executive summary (HTML format) for resumes
    /// </summary>
    public string? ExecutiveSummary { get; set; }
    
    // ==================== STATISTICS ====================
    
    /// <summary>
    /// Document processing statistics
    /// </summary>
    public WorkDocumentStats Stats { get; set; } = new();
    
    // ==================== FILE ACCESS ====================
    
    /// <summary>
    /// SAS URL for direct access to the document file
    /// Valid for 24 hours, allows UI to download/view the original document
    /// </summary>
    public string? FileUrl { get; set; }
    
    // ==================== COMPLETE DATA ====================
    
    /// <summary>
    /// Complete structured document data for detailed view
    /// Contains all extracted information in nested object format
    /// </summary>
    public object? FullDocumentData { get; set; }
}

/// <summary>
/// Work document statistics for dashboard display
/// </summary>
public class WorkDocumentStats
{
    /// <summary>
    /// Number of work experience entries (for resumes)
    /// </summary>
    public int WorkExperience { get; set; }
    
    /// <summary>
    /// Number of education entries (for resumes)
    /// </summary>
    public int Education { get; set; }
    
    /// <summary>
    /// Number of skills listed (for resumes)
    /// </summary>
    public int Skills { get; set; }
    
    /// <summary>
    /// Number of certifications (for resumes)
    /// </summary>
    public int Certifications { get; set; }
    
    /// <summary>
    /// Number of projects (for resumes)
    /// </summary>
    public int Projects { get; set; }
    
    /// <summary>
    /// Number of awards/achievements (for resumes)
    /// </summary>
    public int Awards { get; set; }
}

// ========================================================================================
// RESPONSE MODELS FOR RESUME DATA
// ========================================================================================

/// <summary>
/// Response model for GET /api/twins/{twinId}/work/resumes endpoint
/// Contains formatted resume data optimized for UI display
/// </summary>
public class GetResumesFormattedResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Twin ID used in the request
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of resumes found for this Twin
    /// </summary>
    public int TotalResumes { get; set; }
    
    /// <summary>
    /// Array of formatted resume data objects, ordered by date (newest first)
    /// </summary>
    public List<FormattedResumeData> Resumes { get; set; } = new();
}

/// <summary>
/// Formatted resume data optimized for UI display
/// Contains both summary information for quick display and complete data for detailed views
/// </summary>
public class FormattedResumeData
{
    // ==================== DOCUMENT METADATA ====================
    
    /// <summary>
    /// Unique document identifier in Cosmos DB
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Original uploaded filename
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Upload timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string UploadedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing completion timestamp formatted as "yyyy-MM-dd HH:mm:ss UTC"
    /// </summary>
    public string ProcessedAt { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of days since upload (useful for "X days ago" display)
    /// </summary>
    public int DaysAgo { get; set; }
    
    // ==================== PERSONAL INFORMATION (QUICK ACCESS) ====================
    
    /// <summary>
    /// Personal information extracted from resume for quick display
    /// </summary>
    public PersonalInfoSummary PersonalInfo { get; set; } = new();
    
    // ==================== PROFESSIONAL SUMMARY ====================
    
    /// <summary>
    /// Professional summary/objective from resume, or "No summary available" if empty
    /// </summary>
    public string ProfessionalSummary { get; set; } = string.Empty;
    
    // ==================== EXECUTIVE SUMMARY ====================
    
    /// <summary>
    /// AI-generated executive summary of the resume in HTML format
    /// Provides a comprehensive overview with detailed analysis for UI display
    /// </summary>
    public string ExecutiveSummary { get; set; } = string.Empty;
    
    // ==================== DASHBOARD STATISTICS ====================
    
    /// <summary>
    /// Numeric statistics for dashboard display (counts of various resume sections)
    /// </summary>
    public ResumeStats Stats { get; set; } = new();
    
    // ==================== SKILLS PREVIEW ====================
    
    /// <summary>
    /// First 10 skills from resume for preview display (full list in FullResumeData)
    /// </summary>
    public List<string> TopSkills { get; set; } = new();
    
    // ==================== STATUS INDICATORS ====================
    
    /// <summary>
    /// Boolean indicators for UI badges, icons, and status displays
    /// </summary>
    public ResumeStatus Status { get; set; } = new();
    
    // ==================== COMPLETE STRUCTURED DATA ====================
    
    /// <summary>
    /// Complete structured resume data for detailed view
    /// Contains all extracted information in nested object format
    /// </summary>
    public object? FullResumeData { get; set; }

    // ==================== FILE ACCESS ====================
    
    /// <summary>
    /// SAS URL for direct access to the resume file
    /// Valid for 24 hours, allows UI to download/view the original resume document
    /// </summary>
    public string? FileUrl { get; set; }
}

/// <summary>
/// Personal information summary for quick display in UI cards/lists
/// </summary>
public class PersonalInfoSummary
{
    /// <summary>
    /// Full name extracted from resume
    /// </summary>
    public string FullName { get; set; } = string.Empty;
    
    /// <summary>
    /// Email address extracted from resume
    /// </summary>
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Phone number extracted from resume
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Current position formatted as "Job Title at Company" or individual components if only one available
    /// Returns "Not specified" if neither job title nor company are available
    /// </summary>
    public string CurrentPosition { get; set; } = string.Empty;
}

/// <summary>
/// Resume statistics for dashboard display
/// All counts represent the number of items in each resume section
/// </summary>
public class ResumeStats
{
    /// <summary>
    /// Number of work experience entries in resume
    /// </summary>
    public int WorkExperience { get; set; }
    
    /// <summary>
    /// Number of education entries in resume
    /// </summary>
    public int Education { get; set; }
    
    /// <summary>
    /// Number of skills listed in resume
    /// </summary>
    public int Skills { get; set; }
    
    /// <summary>
    /// Number of certifications in resume
    /// </summary>
    public int Certifications { get; set; }
    
    /// <summary>
    /// Number of projects listed in resume
    /// </summary>
    public int Projects { get; set; }
    
    /// <summary>
    /// Number of awards/achievements in resume
    /// </summary>
    public int Awards { get; set; }
}

/// <summary>
/// Resume status indicators for UI badges, icons, and conditional displays
/// </summary>
public class ResumeStatus
{
    /// <summary>
    /// True if resume has both full name and email (considered complete basic info)
    /// </summary>
    public bool IsComplete { get; set; }
    
    /// <summary>
    /// True if resume contains work experience entries
    /// </summary>
    public bool HasWorkExperience { get; set; }
    
    /// <summary>
    /// True if resume contains education entries
    /// </summary>
    public bool HasEducation { get; set; }
    
    /// <summary>
    /// True if resume contains skills
    /// </summary>
    public bool HasSkills { get; set; }
    
    /// <summary>
    /// True if resume contains certifications
    /// </summary>
    public bool HasCertifications { get; set; }
}