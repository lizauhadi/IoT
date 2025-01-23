# AGENT APP
## Wstęp

Niniejszy projekt implementuje system połączenia urządzeń OPC UA z chmurą Azure IoT Hub, umożliwiając monitorowanie parametrów produkcyjnych oraz zarządzanie zdarzeniami i alarmami.

## Połączenie z serwerem OPC UA
#### Jak uruchomić aplikację?



- Otwórz plik IoT12.sln.
- Kliknij przycisk Run, aby uruchomić aplikację.

#### Jak połączyć się z serwerem OPC UA?

- Musisz podać URL serwera OPC UA w formacie opc.tcp://localhost:4840/.

- Edytując plik appsettings.json, znajdujący się w IoT12/bin/Debug/net6.0/appsettings.json

Agent jest konfigurowany poprzez plik appsettings.json, 
który zawiera kluczowe parametry niezbędne do połączenia z Azure IoT Hub i serwerem OPC UA.

```json
{
  "ServerConnectionString": "opc.tcp://localhost:4840/",
  "AzureDevicesConnectionStrings": [
    "HostName=YourIoTHub.azure-devices.net;DeviceId=Device1;SharedAccessKey=..."
  ],
  "TelemetrySendingDelayInMs": 5000,
  "ErrorCheckingDelayInMs": 2000,
  "ProductionRateCheckingDelayInMs": 2000
}

```
- Agent automatycznie synchronizuje się z chmurą.

Odczytywane parametry:

```cs
		{
			ProductionStatus,
			ProductionRate,
			Temperature,
			GoodCount,
			BadCount,
			DeviceError
	 }
```

#### Direct Methods

Agent obsługuje bezpośrednie wywołania metod z Azure IoT Hub, które umożliwiają kontrolę urządzeń OPC UA. 
Zaimplementowane metody:

- EmergencyStop – zatrzymuje produkcję w razie awarii.
- ResetErrorStatus – resetuje status błędów urządzenia.

Przykład rejestracji metod:

```cs
private static async Task RegisterDirectMethodsAsync()
{
    await deviceClient!.SetMethodHandlerAsync("EmergencyStop", EmergencyStop, null);
    await deviceClient!.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatus, null);
}


```

Przykład obsługi metody EmergencyStop:

```cs
private static async Task<MethodResponse> EmergencyStop(MethodRequest methodRequest, object userContext)
{
    Console.WriteLine("Direct Method: EmergencyStop received.");
    emergencyStopTriggered = true;
    return new MethodResponse(Encoding.UTF8.GetBytes("Emergency stop executed"), 200);
}
```

#### Device Twin

Aplikacja wykorzystuje mechanizm Device Twin do synchronizacji konfiguracji między chmurą a agentem OPC UA.
Zaimplementowane metody:

- Desired Properties (konfiguracja z chmury do urządzenia):
  - productionRate - pożądana wartość wydajności produkcji.
  - telemetryInterval - interwał przesyłania danych telemetrycznych.
- Reported Properties (status urządzenia raportowany do chmury):
  - productionRate - aktualna wartość wydajności.
  - deviceError - status błędów urządzenia.

Przykład raportowania właściwości:

```cs
private static async Task ReportPropertyAsync(string propertyName, object value)
{
    var reportedProperties = new TwinCollection
    {
        [propertyName] = value
    };

    await deviceClient!.UpdateReportedPropertiesAsync(reportedProperties);
    Console.WriteLine($"Updated reported property: {propertyName} = {value}");
}

```
