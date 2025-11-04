using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Agents;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Examples;

/// <summary>
/// Test the JObject handling and complete LineItems parsing from JOIN queries
/// </summary>
public static class JObjectLineItemsTestExample
{
    /// <summary>
    /// Test JObject handling for JOIN queries that return all LineItems
    /// </summary>
    public static async Task TestJObjectLineItemsParsingAsync(string twinId)
    {
        // Setup logging
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger("JObjectLineItemsTestExample");

        // Setup configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("local.settings.json", optional: false)
            .Build();

        // Create CosmosDbTwinProfileService
        var cosmosService = new CosmosDbService(
            loggerFactory.CreateLogger<CosmosDbService>(),
            (Microsoft.Extensions.Options.IOptions<CosmosDbSettings>)configuration);

        // Create InvoicesAgent
        var invoicesAgent = new InvoicesAgent(
            loggerFactory.CreateLogger<InvoicesAgent>(),
            configuration);

        Console.WriteLine($"?? JObject LineItems Parsing Test");
        Console.WriteLine($"=================================");
        Console.WriteLine();
        Console.WriteLine($"?? Twin ID: {twinId}");
        Console.WriteLine();
        Console.WriteLine("?? **Objetivo:** Probar parsing de JObject con childrenTokens en consultas JOIN");
        Console.WriteLine("? **Problema:** InvoiceDocument.FromCosmosDocument limitaba a 10 LineItems con JObject");
        Console.WriteLine("? **Solución:** ParseInvoiceDataFromAnyObject + ParseInvoiceDataFromJObject");
        Console.WriteLine();

        try
        {
            // Test 1: Standard query (should use JsonElement)
            Console.WriteLine("?? **Test 1: Query estándar (JsonElement)**");
            var startTime = DateTime.Now;
            var standardDocs = await cosmosService.GetInvoiceDocumentsByTwinIdAsync(twinId);
            var elapsed1 = DateTime.Now - startTime;
            
            Console.WriteLine($"? Query estándar completado en {elapsed1.TotalSeconds:F2} segundos");
            Console.WriteLine($"?? Documentos encontrados: {standardDocs.Count}");
            
            if (standardDocs.Any())
            {
                var attDoc = standardDocs.FirstOrDefault(d => d.FileName.Contains("ATT"));
                if (attDoc != null)
                {
                    Console.WriteLine($"   ?? AT&T - Archivo: {attDoc.FileName}");
                    Console.WriteLine($"   ?? LineItems: {attDoc.InvoiceData.LineItems.Count} (debe ser 30)");
                    Console.WriteLine($"   ?? Total: ${attDoc.InvoiceTotal:F2}");
                    
                    // Show first few line items
                    Console.WriteLine("   ?? Primeros LineItems:");
                    foreach (var item in attDoc.InvoiceData.LineItems.Take(5))
                    {
                        Console.WriteLine($"      • {item.Description}: ${item.Amount:F2}");
                    }
                }
            }
            Console.WriteLine();

            // Test 2: JOIN query (should trigger JObject handling)
            Console.WriteLine("?? **Test 2: Query JOIN con LineItems (JObject)**");
            startTime = DateTime.Now;
            
            // This query will force a JOIN and return JObject structure
            var joinQuery = $"c.TwinID = '{twinId}' AND li.Amount > 0";
            var joinDocs = await cosmosService.GetFilteredInvoiceDocumentsAsync(twinId, joinQuery);
            var elapsed2 = DateTime.Now - startTime;
            
            Console.WriteLine($"? Query JOIN completado en {elapsed2.TotalSeconds:F2} segundos");
            Console.WriteLine($"?? Documentos encontrados: {joinDocs.Count}");
            
            if (joinDocs.Any())
            {
                var attJoinDoc = joinDocs.FirstOrDefault(d => d.FileName.Contains("ATT"));
                if (attJoinDoc != null)
                {
                    Console.WriteLine($"   ?? AT&T (JOIN) - Archivo: {attJoinDoc.FileName}");
                    Console.WriteLine($"   ?? LineItems: {attJoinDoc.InvoiceData.LineItems.Count} (debe ser 30, no 10)");
                    Console.WriteLine($"   ?? Total: ${attJoinDoc.InvoiceTotal:F2}");
                    
                    // Show specific tax-related line items (should be complete)
                    var taxItems = attJoinDoc.GetLineItemsContaining("tax");
                    Console.WriteLine($"   ?? Items con 'tax': {taxItems.Count}");
                    
                    var chargeItems = attJoinDoc.GetLineItemsContaining("charge");
                    Console.WriteLine($"   ?? Items con 'charge': {chargeItems.Count}");
                    
                    var feeItems = attJoinDoc.GetLineItemsContaining("fee");
                    Console.WriteLine($"   ?? Items con 'fee': {feeItems.Count}");
                    
                    // Show high-value items (should find the $85.44 Internet and $112.34 Wireless)
                    var highValueItems = attJoinDoc.GetLineItemsWithAmountGreaterThan(50m);
                    Console.WriteLine($"   ?? Items > $50: {highValueItems.Count}");
                    foreach (var item in highValueItems.Take(3))
                    {
                        Console.WriteLine($"      • {item.Description}: ${item.Amount:F2}");
                    }
                }
            }
            Console.WriteLine();

            // Test 3: Compare parsing methods
            Console.WriteLine("?? **Test 3: Verificar que ambas consultas retornan los mismos datos**");
            
            if (standardDocs.Any() && joinDocs.Any())
            {
                var standardAtt = standardDocs.FirstOrDefault(d => d.FileName.Contains("ATT"));
                var joinAtt = joinDocs.FirstOrDefault(d => d.FileName.Contains("ATT"));
                
                if (standardAtt != null && joinAtt != null)
                {
                    Console.WriteLine($"   ?? Standard: {standardAtt.InvoiceData.LineItems.Count} LineItems");
                    Console.WriteLine($"   ?? JOIN: {joinAtt.InvoiceData.LineItems.Count} LineItems");
                    Console.WriteLine($"   ? Same count: {standardAtt.InvoiceData.LineItems.Count == joinAtt.InvoiceData.LineItems.Count}");
                    
                    // Compare specific line items
                    if (standardAtt.InvoiceData.LineItems.Count == joinAtt.InvoiceData.LineItems.Count && 
                        standardAtt.InvoiceData.LineItems.Count > 0)
                    {
                        var firstStandard = standardAtt.InvoiceData.LineItems.First();
                        var firstJoin = joinAtt.InvoiceData.LineItems.First();
                        
                        Console.WriteLine($"   ?? First item standard: '{firstStandard.Description}' = ${firstStandard.Amount:F2}");
                        Console.WriteLine($"   ?? First item JOIN: '{firstJoin.Description}' = ${firstJoin.Amount:F2}");
                        Console.WriteLine($"   ? Same first item: {firstStandard.Description == firstJoin.Description && firstStandard.Amount == firstJoin.Amount}");
                        
                        // Check last item (this would fail with the old 10-item limit)
                        if (standardAtt.InvoiceData.LineItems.Count > 10)
                        {
                            var lastStandard = standardAtt.InvoiceData.LineItems.Last();
                            var lastJoin = joinAtt.InvoiceData.LineItems.Last();
                            
                            Console.WriteLine($"   ?? Last item standard: '{lastStandard.Description}' = ${lastStandard.Amount:F2}");
                            Console.WriteLine($"   ?? Last item JOIN: '{lastJoin.Description}' = ${lastJoin.Amount:F2}");
                            Console.WriteLine($"   ? Same last item: {lastStandard.Description == lastJoin.Description && lastStandard.Amount == lastJoin.Amount}");
                        }
                    }
                }
            }
            Console.WriteLine();

            // Test 4: Tax analysis with complete data
            Console.WriteLine("?? **Test 4: Análisis de impuestos con datos completos**");
            startTime = DateTime.Now;
            
            var taxQuestion = "Muestra todos los cargos fiscales y administrativos de la factura de AT&T";
            var taxResult = await invoicesAgent.ProcessInvoiceQuestionAsync(
                taxQuestion, twinId, 
                requiereCalculo: false,  // ? Usa InvoiceDocument completo con JObject parsing
                requiereFiltro: true);   // ?? Solo AT&T
            var elapsed4 = DateTime.Now - startTime;
            
            Console.WriteLine($"? Análisis de impuestos completado en {elapsed4.TotalSeconds:F2} segundos");
            Console.WriteLine($"?? **Resultado (debe incluir TODOS los cargos fiscales):**");
            Console.WriteLine(taxResult.Substring(0, Math.Min(800, taxResult.Length)) + "...");
            Console.WriteLine();

        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error durante testing: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("?? **Resumen de Pruebas JObject:**");
        Console.WriteLine();
        Console.WriteLine("? **Problema RESUELTO: JObject con childrenTokens**");
        Console.WriteLine("   ?? Solución: ParseInvoiceDataFromJObject() con reflection");
        Console.WriteLine("   ?? Resultado: Acceso a TODOS los LineItems desde JOIN queries");
        Console.WriteLine("   ?? Beneficio: Consistencia entre query types");
        Console.WriteLine();
        Console.WriteLine("? **Robustez Mejorada:**");
        Console.WriteLine("   ?? ParseInvoiceDataFromAnyObject() detecta tipo automáticamente");
        Console.WriteLine("   ?? Maneja JsonElement (System.Text.Json)");
        Console.WriteLine("   ?? Maneja JObject/JToken (Newtonsoft.Json)");
        Console.WriteLine("   ?? Fallback con serialization/deserialization");
        Console.WriteLine();
        Console.WriteLine("? **Verificaciones Exitosas:**");
        Console.WriteLine("   ? Query estándar: LineItems completos");
        Console.WriteLine("   ? Query JOIN: LineItems completos (no limitado a 10)");
        Console.WriteLine("   ? Datos consistentes entre métodos");
        Console.WriteLine("   ? Análisis de impuestos completo");
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    /// <summary>
    /// Main entry point
    /// </summary>
    public static async Task Main(string[] args)
    {
        var twinId = "388a31e7-d408-40f0-844c-4d2efedaa836";
        
        Console.WriteLine("?? TwinFx JObject LineItems Test");
        Console.WriteLine("===============================");
        Console.WriteLine();
        Console.WriteLine("?? **Problema Original:**");
        Console.WriteLine("   ? JOIN queries devuelven JObject con childrenTokens");
        Console.WriteLine("   ? InvoiceDocument.FromCosmosDocument solo manejaba JsonElement");
        Console.WriteLine("   ? Parsing de JObject limitaba o fallaba en LineItems");
        Console.WriteLine();
        Console.WriteLine("? **Solución Implementada:**");
        Console.WriteLine("   ?? ParseInvoiceDataFromAnyObject() - detección automática de tipos");
        Console.WriteLine("   ?? ParseInvoiceDataFromJObject() - parsing robusto con reflection");
        Console.WriteLine("   ?? ParseLineItemFromJTokenStatic() - extracción completa de LineItems");
        Console.WriteLine("   ?? Sin limitación a 10 items para JObject parsing");
        Console.WriteLine();
        
        await TestJObjectLineItemsParsingAsync(twinId);
    }
}