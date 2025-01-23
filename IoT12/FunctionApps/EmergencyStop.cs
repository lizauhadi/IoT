using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Devices;
using Newtonsoft.Json;
using IoT_Project;

public static class EmergencyStop
{
    // Pobranie connection string do IoT Hub z ustawień środowiskowych
    private static readonly string connectionString = Environment.GetEnvironmentVariable("IoTHubConnectionString");

    [FunctionName("EmergencyStop")]
    public static async Task Run(
        [ServiceBusTrigger("emergency-stop-queue", Connection = "ServiceBusConnectionString")] string message,
        ILogger log)
    {
        try
        {
            log.LogInformation($"Received message: {message}");

            // Deserializacja wiadomości z kolejki
            dynamic data = JsonConvert.DeserializeObject(message);
            string deviceId = data?.ConnectionDeviceId;
            int errorCount = data?.ErrorCount ?? 0;

            log.LogInformation($"Checking emergency stop for device: {deviceId}, Errors: {errorCount}");

            // Sprawdzenie warunku zatrzymania awaryjnego
            if (!string.IsNullOrEmpty(deviceId) && errorCount > 3)
            {
                using var serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
                var methodInvocation = new CloudToDeviceMethod("EmergencyStop");

                log.LogInformation($"Invoking EmergencyStop on {deviceId}...");
                var response = await serviceClient.InvokeDeviceMethodAsync(deviceId, methodInvocation);

                log.LogInformation($"Emergency Stop triggered for {deviceId}. Response status: {response.Status}");
            }
            else
            {
                log.LogWarning($"Skipping EmergencyStop for {deviceId}. Error count: {errorCount}");
            }
        }
        catch (Exception ex)
        {
            log.LogError($"Error processing EmergencyStop for message: {message}. Exception: {ex.Message}");
        }
    }
}