using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using TwinFx.Services;

namespace TwinFx.Services.Tests
{
    [TestClass()]
    public class CapitulosAISearchIndexTests
    {
        private ILogger<CapitulosAISearchIndex>? _logger;
        private IConfiguration? _configuration;

        [TestInitialize]
        public void Setup()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            var provider = services.BuildServiceProvider();
            _logger = provider.GetRequiredService<ILogger<CapitulosAISearchIndex>>();

            var configBuilder = new ConfigurationBuilder();
            configBuilder.SetBasePath(System.IO.Directory.GetCurrentDirectory());
            configBuilder.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
            configBuilder.AddEnvironmentVariables();
            _configuration = configBuilder.Build();

            Console.WriteLine("Setup completed for CapitulosAISearchIndexTests");
        }

        [TestMethod]
        public async Task CreateCapitulosAiIndexAsyncTest()
        {
            // Arrange
            Assert.IsNotNull(_logger, "Logger should not be null");
            Assert.IsNotNull(_configuration, "Configuration should not be null");

            var service = new CapitulosAISearchIndex(_logger!, _configuration!);

            // Act
            var result = await service.CreateCapitulosAiIndexAsync();

            // Assert basic
            Assert.IsNotNull(result, "Result should not be null");

            Console.WriteLine($"Service IsAvailable: {service.IsAvailable}");
            Console.WriteLine($"Result Success: {result.Success}");
            if (!string.IsNullOrEmpty(result.Error)) Console.WriteLine($"Error: {result.Error}");
            if (!string.IsNullOrEmpty(result.Message)) Console.WriteLine($"Message: {result.Message}");

            // If Azure Search is not configured, CreateCapitulosAiIndexAsync is expected to return Success=false and an error message.
            if (!service.IsAvailable)
            {
                Assert.IsFalse(result.Success, "When service is not available result.Success should be false");
                Assert.IsFalse(string.IsNullOrEmpty(result.Error), "When service is not available an error message should be provided");
            }
            else
            {
                // When service is available, index creation should succeed
                Assert.IsTrue(result.Success, "Index creation should succeed when Azure Search is configured");
                Assert.AreEqual("capitulos-ai-index", result.IndexName, "IndexName should be 'capitulos-ai-index' when creation succeeds");
            }

            Console.WriteLine("CreateCapitulosAiIndexAsyncTest completed");
        }
    }
}
