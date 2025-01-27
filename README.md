
# AGENT APP

# Instrukcja uruchomienia i konfiguracji projektu IoT

Poniższa instrukcja opisuje, jak uruchomić i skonfigurować Twój projekt **IoT**, który służy do:

- **Odczytu parametrów** z serwera **OPC UA**.  
- **Wysyłania danych** do **Azure IoT Hub**.  
- **Obsługi Direct Methods** i **Device Twin**.  
- **Integracji z usługami chmurowymi**, takimi jak:
  - **Azure Stream Analytics**,  
  - **Service Bus**,  
  - **Azure Functions**.  
- Ewentualnie – **wysyłania powiadomień e-mail** za pomocą **Azure Communication Services**.

#  Wstęp

Projekt **IoT** umożliwia:

- **Połączenie z serwerem OPC UA** i zbieranie parametrów produkcyjnych, takich jak:  
  - `ProductionStatus`  
  - `ProductionRate`  
  - `Temperature`  
  - `GoodCount`  
  - `BadCount`  
  - `DeviceError`  

- **Przesyłanie tych danych** do **Azure IoT Hub** w postaci wiadomości **D2C** (*Device-to-Cloud*).  
- **Zarządzanie urządzeniami z chmury** – poprzez **Direct Methods**, np.:
  - `EmergencyStop`  
  - `ResetErrorStatus`  

- **Synchronizację konfiguracji** z **Azure** dzięki **Device Twin**.  
- **Możliwość rozbudowy w chmurze**, np. za pomocą:
  - **Azure Stream Analytics**  
  - **Azure Functions**  
  Umożliwia to automatyczne wywoływanie akcji (*EmergencyStop*), wykrywanie anomalii w produkcji, monitorowanie błędów itp.

- *(Opcjonalnie)* **Wysyłanie e-maili alarmowych**, jeśli w projekcie dodane są dane do **Azure Communication Services**.


#  Pobranie i uruchomienie projektu

### Pobierz projekt z GitHub

- Link do repozytorium: [https://github.com/lizauhadi/IoT](https://github.com/lizauhadi/IoT)  
- Możesz pobrać kod jako **ZIP** lub sklonować repozytorium:  
  ```bash
  git clone https://github.com/lizauhadi/IoT
- Możesz pobrać kod jako **ZIP** lub sklonować repozytorium:  
  ```bash
  git clone ..
  
### Otwórz projekt w Visual Studio

1. Upewnij się, że masz zainstalowane **Visual Studio** (*2022 lub nowsze*) bądź inne środowisko **.NET**.  
2. Otwórz plik rozwiązania *(np. `IoT.sln`, jeśli tak się nazywa w repozytorium)*.

### Zbuduj i uruchom

1. Wybierz przycisk **"Start"** lub **"Run"** *(zielony trójkąt)* w **Visual Studio**.  
2. Jeżeli wszystko przebiegnie poprawnie, aplikacja powinna uruchomić się w oknie konsoli.

## Jak połączyć się z serwerem OPC UA?

- Musisz podać URL serwera OPC UA w formacie opc.tcp://localhost:4840/.

- Edytując plik appsettings.json, znajdujący się w IoT12/bin/Debug/net6.0/appsettings.json

Agent jest konfigurowany poprzez plik appsettings.json, 
który zawiera kluczowe parametry niezbędne do połączenia z Azure IoT Hub i serwerem OPC UA.

```json
{
	"ServerConnectionString": "opc.tcp://localhost:4840/",
	"AzureDevicesConnectionStrings": [
	"HostName=YourIoTHub.azure-devices.net;DeviceId=Device1;SharedAccessKey=..."
	"HostName=YourIoTHub.azure-devices.net;DeviceId=Device1;SharedAccessKey=..."
	"..."
	],

	"AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=iotstorage12;AccountKey=...",
	"IoTHubConnectionString": "HostName=Stanislaw-Sahan-Project.azure-devices.net;SharedAccessKey=...",
    	"ServiceBusConnectionString": "Endpoint=sb://servicebusiot12.servicebus.windows.net/;SharedAccessKeyName..."
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



## Odczyt i zapis danych

Po uruchomieniu aplikacja wykonuje następujące kroki:

1. Odczytuje strukturę serwera **OPC UA**, identyfikując urządzenia.
2. Co określony interwał  zbiera dane telemetryczne z węzłów urządzeń, obejmujące m.in.:
   - **Status produkcji**,  
   - **Szybkość produkcji**,  
   - **Temperaturę**,  
   - **Liczbę dobrych i złych produktów**,  
   - **Błędy urządzenia**.
3. Przesyła zebrane dane do **Azure IoT Hub**.
4. Jeśli pojawiły się nowe błędy (np. deviceError != 0), wysyła wiadomość o błędzie.
### Przykład wiadomości telemetrii (JSON):

```json
{
  "deviceName": "Device 1",
  "productionStatus": 1,
  "goodCount": 297,
  "badCount": 33,
  "temperature": 69.75,
  "EventProcessedUtcTime": "2025-01-24T14:10:25.5587594Z",
  "IoTHub": {
    "ConnectionDeviceId": "Device1",
    "EnqueuedTime": "2025-01-24T13:53:26.5530000Z"
  }
```
}
### Przykład wiadomości o bledzie):
```json
{
  "errorName": "PowerFailure, SensorFailure",
  "newErrors": 2,
  "deviceName": "Device 1",
  "currentErrors": "'Power Failure' 'Sensor Failure'",
  "currentErrorCode": 6
}
```


## Direct Methods

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

### Device Twin

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


## Obliczenia w chmurze (Azure Stream Analytics)

Aplikacja integruje się z **Azure Stream Analytics**, realizując następujące analizy:

### Monitorowanie jakości produkcji (KPI)
- Obliczenie procentu dobrych produktów względem całkowitej produkcji w **5-minutowych** oknach czasowych.  
- Wyniki są przechowywane w **Azure Blob Storage**.

### Analiza temperatury
- Średnia, minimalna i maksymalna temperatura urządzeń w **5-minutowych** oknach czasowych.

### Monitorowanie błędów urządzeń
- Detekcja, gdy liczba błędów przekroczy określony próg w **1-minutowym** oknie czasowym.

## Funkcjonalności dodatkowe

### Powiadomienia e-mail
Aplikacja może wysyłać powiadomienia w przypadku awarii urządzeń do określonych odbiorców.

### Dynamiczna zmiana konfiguracji
Możliwość edycji konfiguracji za pomocą interaktywnego menu ustawień.


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
##  Rozwiązywanie problemów

### Brak połączenia z OPC UA
- Sprawdź, czy serwer działa na `opc.tcp://localhost:4840/` *(albo innym porcie)*.  
- Upewnij się, że zapora *(firewall)* nie blokuje portu.

### Zbyt mała liczba AzureDevicesConnectionStrings
- Jeśli masz **4 urządzenia** na **OPC UA**, musisz mieć co najmniej **4 łańcuchy połączeń**.  
- W przeciwnym wypadku pojawi się błąd:  
  ```plaintext
  Insufficient device connections

### Nie wysyła się e-mail

- Upewnij się, że dane **ACS** *(CommunicationServicesConnectionString, Sender)* są prawidłowe.  
- Sprawdź, czy masz włączoną usługę e-mail w **Azure Communication Services**.

## #Zawieszanie się aplikacji

- Zamknij konsolę i uruchom ponownie.  
- Sprawdź, czy plik konfiguracyjny nie ma błędów składniowych.

### Device Twin nie odzwierciedla zmian

- Sprawdź w logach, czy **Agent** zgłasza jakieś błędy przy próbie **UpdateReportedProperties**.  
- Zweryfikuj poprawność **IoTHubConnectionString** i uprawnień do **Device Twin**.

---

## 13. Podsumowanie

Projekt **IoT** łączy serwer **OPC UA** z chmurą **Azure**, umożliwiając:

- **Zbieranie i wysyłanie telemetrii** *(ProductionStatus, Temperature, GoodCount itp.)* do **IoT Hub**.  
- **Obsługę metod** typu `EmergencyStop` i `ResetErrorStatus` z poziomu **IoT Explorer** (*Direct Methods*).  
- **Synchronizację parametrów** poprzez **Device Twin** (*Desired/Reported*).  
- **Rozbudowę** w **Azure Stream Analytics**, np. do:
  - obliczeń **KPI**,  
  - wykrywania anomalii,  
  - liczenia błędów.  
- *(Opcjonalnie)* **Powiadomienia mailowe** przez **Azure Communication Services**.

Dzięki temu masz elastyczną platformę do monitorowania oraz sterowania urządzeniami przemysłowymi w czasie rzeczywistym.  
Konfiguracja może być dynamicznie zmieniana zarówno w pliku **appsettings.json**, jak i *(w niektórych wersjach projektu)* poprzez interaktywne menu w konsoli.

---

# Wskazówki dodatkowe

- Jeśli chcesz korzystać z automatycznych wywołań *(np. `EmergencyStop` przy dużej liczbie błędów)*, zaimplementuj w chmurze **Azure Functions**, które będą nasłuchiwać komunikatów z **Service Bus**.  
- Pamiętaj, że nazwy kontenerów/ścieżek w **Azure Blob Storage** *(jeśli zapisywane są wyniki ASA)* możesz dowolnie zmieniać w konfiguracji usługi **Azure Stream Analytics**.  
- W przypadku integracji z innymi usługami *(np. Logic Apps, Power BI)* pamiętaj o właściwej polityce uwierzytelniania i uprawnień (**Role Assignments** w Azure).

**Powodzenia w dalszym rozwijaniu projektu IoT!** 🚀



