using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TwinFx.Models;

/// <summary>
/// Multipart form data part for parsing uploads - SHARED ACROSS ALL FUNCTIONS
/// </summary>
public class MultipartFormDataPart
{
    public string Name { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Data { get; set; }
    public string? StringValue { get; set; }
}

/// <summary>
/// Base response for photo upload operations
/// </summary>
public class BasePhotoUploadResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string UploadDate { get; set; } = string.Empty;
    public double ProcessingTimeSeconds { get; set; }
}