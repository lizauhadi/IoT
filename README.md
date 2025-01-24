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
	"AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=iotstorage12;AccountKey=...",
	"IoTHubConnectionString": "HostName=Stanislaw-Sahan-Project.azure-devices.net;SharedAccessKey=...",
    	"ServiceBusConnectionString": "Endpoint=sb://servicebusiot12.servicebus.windows.net/;SharedAccessKeyName..."
}
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

## Kalkulacje i logika biznesowa
### Kalkulacje

Obliczenia realizowane są za pomocą usługi Azure Stream Analytics, która pobiera dane z Azure IoT Hub, przetwarza je w czasie rzeczywistym i zapisuje wyniki

Przykładowe input:
```json
  {
    "$id": "1",
    "deviceId": "Device 1",
    "productionStatus": 1,
    "productionRate": 100,
    "temperature": 78.93910571183508,
    "goodCount": 426,
    "badCount": 51,
    "deviceError": 0,
    "timestamp": "2025-01-24T13:50:49.7664406Z",
    "EventProcessedUtcTime": "2025-01-24T14:10:25.5587594Z",
    "PartitionId": 1,
    "EventEnqueuedUtcTime": "2025-01-24T13:53:26.6350000Z",
    "IoTHub": {
      "MessageId": null,
      "CorrelationId": null,
      "ConnectionDeviceId": "PC",
      "ConnectionDeviceGenerationId": "638723808486828191",
      "EnqueuedTime": "2025-01-24T13:53:26.5530000Z"
    }
  },
```

#### KPI

Obliczenie procentu dobrej produkcji w stosunku do całkowitej liczby wyprodukowanych jednostek, grupując dane w 5-minutowych oknach czasowych.

Zapytanie Stream Analytics:

```sql
SELECT
    deviceId,
    SUM(goodCount)*100 / 
        (CASE WHEN SUM(goodCount + badCount)=0 THEN 1 ELSE SUM(goodCount+badCount) END)
        AS GoodProdRate,
    System.Timestamp AS WindowEnd
INTO
    [iotoutput]
FROM 
    [Stanislaw-Sahan-Project]
GROUP BY
    deviceId,
    TumblingWindow(minute, 5);
```

Wynik:
```json
	{"deviceId":"Device 3","GoodProdRate":89.89799921537858,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 5","GoodProdRate":90.55636998254799,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 6","GoodProdRate":90.57108242469816,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 9","GoodProdRate":90.50514342124451,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 4","GoodProdRate":65.43863873505097,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 1","GoodProdRate":90.13360177698023,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 7","GoodProdRate":66.67608339454206,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 2","GoodProdRate":90.800603385664,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 8","GoodProdRate":90.63291987183065,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
```

#### Temperatura

Co 5 minut obliczanie średniej, minimalnej i maksymalnej temperatury w oknie 5-minutowym, grupując dane według urządzenia.

Zapytanie Stream Analytics:

```sql
SELECT 
    deviceId, 
    AVG(temperature) AS AvgTemperature, 
    MIN(temperature) AS MinTemperature, 
    MAX(temperature) AS MaxTemperature, 
    System.Timestamp AS WindowEnd
INTO 
    [iot-results12]
FROM 
    [Stanislaw-Sahan-Project]
GROUP BY 
    deviceId, 
    TumblingWindow(minute, 5);
```

Wynik:
```json
	{"deviceId":"Device 3","AvgTemperature":76.39213329928445,"MinTemperature":60.83953856693877,"MaxTemperature":89.96528286932829,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 5","AvgTemperature":98.22770241806559,"MinTemperature":74.61926566156387,"MaxTemperature":120.21346090491838,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 6","AvgTemperature":99.27588037413507,"MinTemperature":68.32834215595443,"MaxTemperature":126.46205137121848,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 9","AvgTemperature":-50.0,"MinTemperature":-647.0,"MaxTemperature":991.0,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 4","AvgTemperature":-364.8,"MinTemperature":-992.0,"MaxTemperature":766.0,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 1","AvgTemperature":99.83657179258485,"MinTemperature":61.315771331086985,"MaxTemperature":140.74511517194918,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 7","AvgTemperature":-146.0,"MinTemperature":-898.0,"MaxTemperature":713.0,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 2","AvgTemperature":92.58790627287969,"MinTemperature":68.34821833683682,"MaxTemperature":130.46539883145113,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
	{"deviceId":"Device 8","AvgTemperature":80.96619409719057,"MinTemperature":62.150231752343764,"MaxTemperature":111.53411103611177,"WindowEnd":"2025-01-24T14:25:00.0000000Z"}
```

#### Device Errors

Wykrywanie sytuacji, gdy urządzenie napotyka więcej niż 3 błędy w ciągu 1 minuty.

Zapytanie Stream Analytics:

```sql
SELECT
    deviceId,
    COUNT(*) AS ErrorCount,
    System.Timestamp AS WindowEnd
INTO
    [errorsout]
FROM
    [Stanislaw-Sahan-Project]
WHERE
    deviceError > 0
GROUP BY
    deviceId,
    TumblingWindow(minute, 1)
HAVING
    COUNT(*) > 3;
```
Wynik:
```json
	[{"deviceId":"Device 9","ErrorCount":47,"WindowEnd":"2025-01-24T14:40:00.0000000Z"}]
```
