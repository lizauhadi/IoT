using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System.Text;
using System.Text.RegularExpressions;
using IoT_Project;
using Newtonsoft.Json;

internal static class ProgramEntryPoint
{
    private static readonly Dictionary<string, DeviceClient> deviceClients = new();
    private static readonly Dictionary<string, int> productionRates = new();
    private static readonly Dictionary<string, int> deviceErrors = new();
    private static readonly Dictionary<string, bool> emergencyStops = new();

    private static int telemetryInterval = 2000; // Domyślny interwał

    private static async Task Main(string[] args)
    {
        try
        {
            var settings = AppSettings.GetSettings();

            using (var client = new OpcClient(settings.ServerConnectionString))
            {
                Console.WriteLine("Łączenie z OPC UA...");
                client.Connect();
                Console.WriteLine("Połączono z OPC UA.");

                var devices = ConnectDevicesWithIoTDevices(client, settings.AzureDevicesConnectionStrings);

                foreach (var (device, connectionString) in devices.Zip(settings.AzureDevicesConnectionStrings))
                {
                    string deviceId = device.Attribute(OpcAttribute.DisplayName).Value?.ToString() ?? "UnknownDevice";
                    var deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);
                    
                    deviceClients[deviceId] = deviceClient;
                    productionRates[deviceId] = 0;
                    deviceErrors[deviceId] = 0;
                    emergencyStops[deviceId] = false;

                    await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, deviceId);
                    await RegisterDirectMethodsAsync(deviceClient);
                    await ReportInitialPropertiesAsync(deviceId);
                }

                while (true)
                {
                    foreach (var device in devices)
                    {
                        string deviceId = device.Attribute(OpcAttribute.DisplayName).Value?.ToString() ?? "UnknownDevice";

                        if (emergencyStops[deviceId])
                        {
                            Console.WriteLine($"{deviceId}: Emergency Stop active. Pausing telemetry...");
                            await Task.Delay(5000);
                            continue;
                        }

                        productionRates[deviceId] = GetIntValue(client.ReadNode($"ns=2;s={deviceId}/ProductionRate"));
                        deviceErrors[deviceId] = GetIntValue(client.ReadNode($"ns=2;s={deviceId}/DeviceError"));

                        var telemetryData = new
                        {
                            deviceId,
                            productionStatus = GetIntValue(client.ReadNode($"ns=2;s={deviceId}/ProductionStatus")),
                            productionRate = productionRates[deviceId],
                            temperature = GetDoubleValue(client.ReadNode($"ns=2;s={deviceId}/Temperature")),
                            goodCount = GetIntValue(client.ReadNode($"ns=2;s={deviceId}/GoodCount")),
                            badCount = GetIntValue(client.ReadNode($"ns=2;s={deviceId}/BadCount")),
                            deviceError = deviceErrors[deviceId],
                            timestamp = DateTime.UtcNow
                        };

                        Console.WriteLine($"OPC UA Read ({deviceId}): {telemetryData.productionStatus} - Temp: {telemetryData.temperature}");

                        await ReportPropertyAsync(deviceId, "ProductionRate", productionRates[deviceId]);
                        await ReportPropertyAsync(deviceId, "DeviceError", deviceErrors[deviceId]);
                        await SendDataToIoTHub(deviceId, telemetryData);
                        await Task.Delay(telemetryInterval);
                    }
                }
            }
        }
        catch (OpcException ex)
        {
            Console.WriteLine($"Serwer OPC UA jest offline. Błąd: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Nieznany błąd: {ex.Message}");
        }
    }

    private static async Task RegisterDirectMethodsAsync(DeviceClient deviceClient)
    {
        await deviceClient.SetMethodHandlerAsync("EmergencyStop", EmergencyStop, deviceClient);
        await deviceClient.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatus, deviceClient);
        Console.WriteLine("Direct methods registered.");
    }
    
    private static List<OpcNodeInfo> BrowseDevices(OpcClient client)
    {
        var objectFolder = client.BrowseNode(OpcObjectTypes.ObjectsFolder);
        var devices = new List<OpcNodeInfo>();

        foreach (var childNode in objectFolder.Children())
        {
            if (IsDeviceNode(childNode))
                devices.Add(childNode);
        }

        return devices;
    }
    
    private static List<OpcNodeInfo> ConnectDevicesWithIoTDevices(OpcClient client, List<string> connections)
    {
        List<OpcNodeInfo> devices = BrowseDevices(client);
        if (devices.Count == 0) 
            throw new Exception("Nie znaleziono urządzeń w OPC UA.");
    
        if (devices.Count > connections.Count) 
            throw new Exception($"Brakuje {devices.Count - connections.Count} połączeń do IoT Hub.");

        Console.WriteLine($"Znaleziono {devices.Count} urządzeń.");
        return devices;
    }
    
    private static async Task<MethodResponse> EmergencyStop(MethodRequest methodRequest, object userContext)
    {
        var deviceClient = (DeviceClient)userContext;
        string deviceId = deviceClients.FirstOrDefault(x => x.Value == deviceClient).Key;

        Console.WriteLine($"{deviceId}: EmergencyStop received.");
        emergencyStops[deviceId] = true;
        await ReportPropertyAsync(deviceId, "emergencyStop", true);
        string responsePayload = JsonConvert.SerializeObject(new { message = "Emergency Stop activated" });
        return new MethodResponse(Encoding.UTF8.GetBytes(responsePayload), 200);
    }

    private static async Task<MethodResponse> ResetErrorStatus(MethodRequest methodRequest, object userContext)
    {
        var deviceClient = (DeviceClient)userContext;
        string deviceId = deviceClients.FirstOrDefault(x => x.Value == deviceClient).Key;

        Console.WriteLine($"{deviceId}: ResetErrorStatus received.");
        emergencyStops[deviceId] = false;
        await ReportPropertyAsync(deviceId, "emergencyStop", false);
        string responsePayload = JsonConvert.SerializeObject(new { message = "Error status reset" });
        return new MethodResponse(Encoding.UTF8.GetBytes(responsePayload), 200);
    }

    private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
    {
        string deviceId = (string)userContext;
        Console.WriteLine($"{deviceId}: Received desired properties update.");
        Console.WriteLine(desiredProperties.ToJson());

        if (desiredProperties.Contains("ProductionRate"))
        {
            productionRates[deviceId] = desiredProperties["ProductionRate"];
            Console.WriteLine($"{deviceId}: ProductionRate updated to {productionRates[deviceId]}");
            await ReportPropertyAsync(deviceId, "ProductionRate", productionRates[deviceId]);
        }
    }

    private static async Task ReportInitialPropertiesAsync(string deviceId)
    {
        var reportedProperties = new TwinCollection
        {
            ["emergencyStop"] = emergencyStops[deviceId],
            ["telemetryInterval"] = telemetryInterval,
            ["ProductionRate"] = productionRates[deviceId],
            ["DeviceError"] = deviceErrors[deviceId]
        };

        await deviceClients[deviceId].UpdateReportedPropertiesAsync(reportedProperties);
        Console.WriteLine($"{deviceId}: Initial reported properties sent.");
    }

    private static async Task ReportPropertyAsync(string deviceId, string propertyName, object value)
    {
        var reportedProperties = new TwinCollection { [propertyName] = value };
        await deviceClients[deviceId].UpdateReportedPropertiesAsync(reportedProperties);
        Console.WriteLine($"{deviceId}: Updated reported property {propertyName} = {value}");
    }

    private static async Task SendDataToIoTHub(string deviceId, object telemetryData)
    {
        try
        {
            var jsonMessage = JsonConvert.SerializeObject(telemetryData);
            var message = new Message(Encoding.UTF8.GetBytes(jsonMessage));
            await deviceClients[deviceId].SendEventAsync(message);
            Console.WriteLine($"{deviceId}: Sent data {jsonMessage}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{deviceId}: Error sending data: {ex.Message}");
        }
    }
    
    private static bool IsDeviceNode(OpcNodeInfo nodeInfo)
    {
        if (nodeInfo == null) return false;

        string nodeName = nodeInfo.Attribute(OpcAttribute.DisplayName).Value?.ToString() ?? "";
        return Regex.IsMatch(nodeName, @"^Device \d+$"); // Sprawdza, czy nazwa pasuje do "Device X"
    }

    
    private static int GetIntValue(OpcValue nodeValue) => int.TryParse(nodeValue.Value?.ToString(), out int value) ? value : 0;
    private static double GetDoubleValue(OpcValue nodeValue) => double.TryParse(nodeValue.Value?.ToString(), out double value) ? value : 0.0;
}
