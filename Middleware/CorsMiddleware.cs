using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using System.Net;

namespace TwinFx;

/// <summary>
/// Middleware CORS simplificado y robusto para Azure Functions
/// Garantiza que todas las respuestas tengan headers CORS apropiados
/// </summary>
public class CorsMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        Console.WriteLine("?? CorsMiddleware: Starting CORS processing");

        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData == null)
        {
            Console.WriteLine("?? CorsMiddleware: No HTTP request data found, skipping CORS");
            await next(context);
            return;
        }

        var method = httpRequestData.Method;
        var url = httpRequestData.Url;
        Console.WriteLine($"?? CorsMiddleware: Processing {method} {url}");

        // Get origin header
        var origin = GetOriginFromRequest(httpRequestData);
        Console.WriteLine($"?? CorsMiddleware: Request origin: {origin ?? "None"}");

        // Handle OPTIONS preflight requests immediately
        if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("?? CorsMiddleware: Handling OPTIONS preflight request");
            
            var optionsResponse = httpRequestData.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(optionsResponse, origin);
            
            // Set empty body for OPTIONS response
            await optionsResponse.WriteStringAsync("");
            
            context.GetInvocationResult().Value = optionsResponse;
            Console.WriteLine("? CorsMiddleware: OPTIONS response sent with CORS headers");
            return; // Don't call next() for OPTIONS
        }

        // For non-OPTIONS requests, continue processing
        Console.WriteLine("?? CorsMiddleware: Continuing to function execution");
        await next(context);

        // Add CORS headers to response after function execution
        var response = context.GetInvocationResult()?.Value as HttpResponseData;
        if (response != null)
        {
            AddCorsHeaders(response, origin);
            Console.WriteLine("? CorsMiddleware: CORS headers added to response");
        }
        else
        {
            Console.WriteLine("?? CorsMiddleware: No HTTP response found to add CORS headers");
        }
    }

    private static string? GetOriginFromRequest(HttpRequestData request)
    {
        try
        {
            // Try to get Origin header
            if (request.Headers.TryGetValues("Origin", out var originValues))
            {
                return originValues.FirstOrDefault();
            }

            // Fallback: try case variations
            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "origin", StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value.FirstOrDefault();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? CorsMiddleware: Error getting origin: {ex.Message}");
            return null;
        }
    }

    private static void AddCorsHeaders(HttpResponseData response, string? origin)
    {
        try
        {
            // List of allowed origins for development
            var allowedOrigins = new[]
            {
                "http://localhost:3000",
                "http://localhost:5173", 
                "http://127.0.0.1:3000",
                "http://127.0.0.1:5173",
                "https://localhost:3000",
                "https://localhost:5173"
            };

            // Determine which origin to use
            string allowOrigin = "*";
            if (!string.IsNullOrEmpty(origin))
            {
                if (allowedOrigins.Contains(origin))
                {
                    allowOrigin = origin;
                    Console.WriteLine($"? CorsMiddleware: Using specific origin: {origin}");
                }
                else
                {
                    Console.WriteLine($"?? CorsMiddleware: Origin {origin} not in allowed list, using wildcard");
                }
            }
            else
            {
                Console.WriteLine("? CorsMiddleware: No origin specified, using wildcard");
            }

            // Add CORS headers - use simple Add method (will replace if exists)
            var headers = response.Headers;
            
            // Remove any existing CORS headers first to avoid conflicts
            RemoveHeaderIfExists(headers, "Access-Control-Allow-Origin");
            RemoveHeaderIfExists(headers, "Access-Control-Allow-Methods");
            RemoveHeaderIfExists(headers, "Access-Control-Allow-Headers");
            RemoveHeaderIfExists(headers, "Access-Control-Max-Age");
            RemoveHeaderIfExists(headers, "Access-Control-Allow-Credentials");

            // Add fresh CORS headers
            headers.Add("Access-Control-Allow-Origin", allowOrigin);
            headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
            headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With, Cache-Control");
            headers.Add("Access-Control-Max-Age", "86400");
            headers.Add("Access-Control-Allow-Credentials", "false");

            Console.WriteLine($"? CorsMiddleware: All CORS headers added successfully with origin: {allowOrigin}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? CorsMiddleware: Error adding CORS headers: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private static void RemoveHeaderIfExists(HttpHeadersCollection headers, string headerName)
    {
        try
        {
            // Check if header exists and remove it
            var existingHeaders = headers.Where(h => string.Equals(h.Key, headerName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var header in existingHeaders)
            {
                headers.Remove(header.Key);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? CorsMiddleware: Could not remove header {headerName}: {ex.Message}");
        }
    }
}