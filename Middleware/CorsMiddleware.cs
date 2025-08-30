using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace TwinFx;

/// <summary>
/// Enhanced CORS middleware for Azure Functions isolated worker model
/// Handles CORS headers for all HTTP-triggered functions with comprehensive logging
/// </summary>
public class CorsMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // Get the HTTP request data
        var httpRequestData = await context.GetHttpRequestDataAsync();
        
        if (httpRequestData != null)
        {
            Console.WriteLine($"?? CORS Middleware: {httpRequestData.Method} {httpRequestData.Url}");
            
            // Get origin from headers
            var origin = httpRequestData.Headers
                .FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                .Value?.FirstOrDefault();
            
            Console.WriteLine($"?? Origin: {origin ?? "None"}");

            // Handle preflight OPTIONS requests
            if (httpRequestData.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("?? Handling OPTIONS preflight request");
                
                var response = httpRequestData.CreateResponse();
                AddCorsHeaders(response, origin);
                response.StatusCode = System.Net.HttpStatusCode.OK;
                
                // Set empty body for OPTIONS response
                await response.WriteStringAsync("");
                
                var invocationResult = context.GetInvocationResult();
                invocationResult.Value = response;
                
                Console.WriteLine("? OPTIONS response sent with CORS headers");
                return;
            }
        }

        // Continue with the function execution
        await next(context);

        // Add CORS headers to the response
        var httpResponse = context.GetInvocationResult()?.Value as HttpResponseData;
        if (httpResponse != null && httpRequestData != null)
        {
            var origin = httpRequestData.Headers
                .FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                .Value?.FirstOrDefault();
                
            AddCorsHeaders(httpResponse, origin);
            Console.WriteLine("? CORS headers added to response");
        }
    }

    private static void AddCorsHeaders(HttpResponseData response, string? origin)
    {
        // List of allowed origins
        var allowedOrigins = new[]
        {
            "http://localhost:3000",
            "http://localhost:5173",
            "http://127.0.0.1:3000",
            "http://127.0.0.1:5173"
        };

        // Always allow CORS for development
        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
            Console.WriteLine($"?? CORS Origin Set: {origin}");
        }
        else
        {
            // For development, allow all origins
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            Console.WriteLine("?? CORS Origin Set: * (wildcard)");
        }

        // Add comprehensive CORS headers
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, DNT, Cache-Control, X-Mx-ReqToken, Keep-Alive, X-Requested-With, If-Modified-Since, x-api-key");
        response.Headers.Add("Access-Control-Max-Age", "86400");
        response.Headers.Add("Access-Control-Allow-Credentials", "false");
        response.Headers.Add("Access-Control-Expose-Headers", "Content-Length, Content-Range");
        
        Console.WriteLine("?? All CORS headers added successfully");
    }
}