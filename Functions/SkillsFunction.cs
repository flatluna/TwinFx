using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TwinFx.Functions;

public class SkillsFunction
{
    private readonly ILogger<SkillsFunction> _logger;

    public SkillsFunction(ILogger<SkillsFunction> logger)
    {
        _logger = logger;
    }

    [Function("SkillsFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}