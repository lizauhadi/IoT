using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Opc.UaFx;
using Opc.UaFx.Client;

namespace IoT12
{
    public class IoTDeviceReader
    {
        private static string opcUaEndpoint = "opc.tcp://localhost:4840";
        private static string iotHubConnectionString = "HostName=Stanislaw-Sahan-Project.azure-devices.net;DeviceId=PC;SharedAccessKey=...";

        private static DeviceClient? deviceClient;

        public async Task StartAsync()
        {
            Console.WriteLine("Starting IoT Device Reader...");

            deviceClient = DeviceClient.CreateFromConnectionString(iotHubConnectionString, TransportType.Mqtt);
            await RegisterDirectMethodsAsync();

            using var client = new OpcClient(opcUaEndpoint);
            client.Connect();
            Console.WriteLine("Connected to OPC UA server.");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => cts.Cancel();

            while (!cts.Token.IsCancellationRequested)
            {
                var telemetryData = new
                {
                    productionStatus = client.ReadNode("ns=2;s=Machine/ProductionStatus").Value,
                    goodCount = client.ReadNode("ns=2;s=Machine/GoodCount").Value,
                    badCount = client.ReadNode("ns=2;s=Machine/BadCount").Value,
                    temperature = client.ReadNode("ns=2;s=Machine/Temperature").Value
                };

                await SendToIoTHub(telemetryData);
                await Task.Delay(5000, cts.Token);
            }
        }

        private static async Task SendToIoTHub(object data)
        {
            string messageString = JsonConvert.SerializeObject(data);
            var message = new Message(System.Text.Encoding.UTF8.GetBytes(messageString));
            await deviceClient!.SendEventAsync(message);
            Console.WriteLine($"Sent to IoT Hub: {messageString}");
        }

        private static async Task RegisterDirectMethodsAsync()
        {
            await deviceClient!.SetMethodHandlerAsync("EmergencyStop", (req, ctx) => Task.FromResult(new MethodResponse(0)), null);
        }
    }
}
