using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Devices;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Shared;

public static class DecreaseProductionRate
{
    private static readonly string connectionString = Environment.GetEnvironmentVariable("IoTHubConnectionString");

    [FunctionName("DecreaseProductionRate")]
    public static async Task Run(
        [ServiceBusTrigger("production-rate-queue", Connection = "AzureWebJobsStorage")] string message,
        ILogger log)
    {
        try
        {
            dynamic data = JsonConvert.DeserializeObject(message);
            string deviceId = data.ConnectionDeviceId;
            double productionQuality = data.ProductionQuality;

            log.LogInformation($"Otrzymano wiadomość dla urządzenia: {deviceId}, Jakość produkcji: {productionQuality}");

            if (productionQuality < 90)
            {
                using var serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
                var registryManager = RegistryManager.CreateFromConnectionString(connectionString);

                // Pobranie Twin urządzenia
                var twin = await registryManager.GetTwinAsync(deviceId);
                
                if (twin.Properties.Desired.Contains("productionRate"))
                {
                    int currentRate = (int)twin.Properties.Desired["productionRate"];
                    int newRate = Math.Max(currentRate - 10, 10);  // Zapobieganie spadkowi poniżej 10%

                    var twinPatch = new Twin();
                    twinPatch.Properties.Desired["productionRate"] = newRate;

                    await registryManager.UpdateTwinAsync(deviceId, twinPatch, twin.ETag);

                    log.LogInformation($"Zmieniono productionRate dla urządzenia {deviceId} na {newRate}");
                }
                else
                {
                    log.LogWarning($"Właściwość productionRate nie istnieje dla urządzenia {deviceId}");
                }
            }
            else
            {
                log.LogInformation($"Jakość produkcji {productionQuality}% dla {deviceId} jest w normie.");
            }
        }
        catch (Exception ex)
        {
            log.LogError($"Błąd przetwarzania wiadomości dla urządzenia: {ex.Message}");
        }
    }
}