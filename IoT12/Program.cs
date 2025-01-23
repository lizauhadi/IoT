using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IoT_Project;
using Microsoft.Azure.Devices.Shared;

internal static class ProgramEntryPoint
{
    private static DeviceClient? deviceClient;
    private static bool emergencyStopTriggered = false;
    private static int telemetryInterval = 2000; // Domyślny interwał wysyłania danych

    private static async Task Main(string[] args)
    {
        try
        {
            var settings = AppSettings.GetSettings();
            deviceClient = DeviceClient.CreateFromConnectionString(settings.AzureDevicesConnectionStrings[0], TransportType.Mqtt);

            await RegisterDirectMethodsAsync();
            await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);
            await ReportInitialPropertiesAsync();

            using (var client = new OpcClient(settings.ServerConnectionString))
            {
                Console.WriteLine("Łączenie z OPC UA...");
                client.Connect();
                Console.WriteLine("Połączono z OPC UA.");

                var connections = settings.AzureDevicesConnectionStrings;
                var devices = ConnectDevicesWithIoTDevices(client, connections);

                while (true)
                {
                    foreach (var device in devices)
                    {
                        if (emergencyStopTriggered)
                        {
                            Console.WriteLine("Emergency Stop active. Pausing telemetry...");
                            await Task.Delay(5000);
                            continue;
                        }

                        string deviceId = device.Attribute(OpcAttribute.DisplayName).Value?.ToString() ?? "UnknownDevice";
                        var telemetryData = new
                        {
                            deviceId = deviceId,
                            productionStatus = GetIntValue(client.ReadNode($"ns=2;s={deviceId}/ProductionStatus")),
                            temperature = GetDoubleValue(client.ReadNode($"ns=2;s={deviceId}/Temperature")),
                            timestamp = DateTime.UtcNow
                        };

                        await SendDataToIoTHub(connections[devices.IndexOf(device)], telemetryData);
                        await Task.Delay(telemetryInterval);
                    }
                }
            }
        }
        catch (OpcException ex)
        {
            Console.WriteLine("Serwer OPC UA jest offline.");
            Console.WriteLine(ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Nieznany błąd: {ex.Message}");
        }
    }

    private static async Task RegisterDirectMethodsAsync()
    {
        await deviceClient!.SetMethodHandlerAsync("EmergencyStop", EmergencyStop, null);
        await deviceClient!.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatus, null);
        Console.WriteLine("Direct methods registered.");
    }

    private static async Task<MethodResponse> EmergencyStop(MethodRequest methodRequest, object userContext)
    {
        Console.WriteLine("Direct Method: EmergencyStop received.");
        emergencyStopTriggered = true;
        await ReportPropertyAsync("emergencyStop", true);
        string responsePayload = JsonSerializer.Serialize(new { message = "Emergency Stop activated" });
        return new MethodResponse(Encoding.UTF8.GetBytes(responsePayload), 200);
    }

    private static async Task<MethodResponse> ResetErrorStatus(MethodRequest methodRequest, object userContext)
    {
        Console.WriteLine("Direct Method: ResetErrorStatus received.");
        emergencyStopTriggered = false;
        await ReportPropertyAsync("emergencyStop", false);
        string responsePayload = JsonSerializer.Serialize(new { message = "Error status reset" });
        return new MethodResponse(Encoding.UTF8.GetBytes(responsePayload), 200);
    }

    private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
    {
        Console.WriteLine("Received desired properties update:");
        Console.WriteLine(desiredProperties.ToJson());

        if (desiredProperties.Contains("telemetryInterval"))
        {
            telemetryInterval = desiredProperties["telemetryInterval"];
            Console.WriteLine($"Telemetry interval updated to: {telemetryInterval} ms");
            await ReportPropertyAsync("telemetryInterval", telemetryInterval);
        }
    }

    private static async Task ReportInitialPropertiesAsync()
    {
        var reportedProperties = new TwinCollection
        {
            ["emergencyStop"] = emergencyStopTriggered,
            ["telemetryInterval"] = telemetryInterval
        };

        await deviceClient!.UpdateReportedPropertiesAsync(reportedProperties);
        Console.WriteLine("Initial reported properties sent to IoT Hub.");
    }

    private static async Task ReportPropertyAsync(string propertyName, object value)
    {
        var reportedProperties = new TwinCollection
        {
            [propertyName] = value
        };

        await deviceClient!.UpdateReportedPropertiesAsync(reportedProperties);
        Console.WriteLine($"Updated reported property: {propertyName} = {value}");
    }

    private static async Task SendDataToIoTHub(string connectionString, object telemetryData)
    {
        try
        {
            using var deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);

            var jsonOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                MaxDepth = 128,
                WriteIndented = true
            };

            string jsonMessage = JsonSerializer.Serialize(telemetryData, jsonOptions);
            var message = new Message(Encoding.UTF8.GetBytes(jsonMessage));

            await deviceClient.SendEventAsync(message);
            Console.WriteLine($"Wysłano dane: {jsonMessage}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd wysyłania danych: {ex.Message}");
        }
    }

    private static int GetIntValue(OpcValue nodeValue)
    {
        if (nodeValue.Value == null) return 0;
        return int.TryParse(nodeValue.Value.ToString(), out int value) ? value : 0;
    }

    private static double GetDoubleValue(OpcValue nodeValue)
    {
        if (nodeValue.Value == null) return 0.0;
        return double.TryParse(nodeValue.Value.ToString(), out double value) ? value : 0.0;
    }

    private static List<OpcNodeInfo> ConnectDevicesWithIoTDevices(OpcClient client, List<string> connections)
    {
        List<OpcNodeInfo> devices = BrowseDevices(client);
        if (devices.Count == 0)
            throw new Exception("Nie znaleziono urządzeń.");
        else if (devices.Count > connections.Count)
            throw new Exception($"Brakuje {devices.Count - connections.Count} połączeń do IoT Hub.");

        Console.WriteLine($"Znaleziono {devices.Count} urządzeń.");
        return devices;
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

    private static bool IsDeviceNode(OpcNodeInfo nodeInfo)
    {
        string pattern = @"^Device \d+$";
        Regex correctName = new Regex(pattern);
        string nodeName = nodeInfo.Attribute(OpcAttribute.DisplayName).Value?.ToString();
        return correctName.IsMatch(nodeName);
    }
}
