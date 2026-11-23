
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RealEstateMetricsConsole.Models;
using RealEstateMetricsConsole.Services;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
Console.OutputEncoding = Encoding.UTF8;

const string csvName = "real_estate_data.csv";
const string apiUrl = "http://127.0.0.1:8000";

var csvPath = FindFile(csvName);

Console.WriteLine("==============================================================");
Console.WriteLine("       МЕТРИКИ МОДЕЛИ ПРОГНОЗА СТОИМОСТИ НЕДВИЖИМОСТИ");
Console.WriteLine("==============================================================");
Console.WriteLine($"CSV: {csvName}");
Console.WriteLine($"API: {apiUrl}");
Console.WriteLine();

if (csvPath is null)
{
    Fail($"Не найден {csvName}. Положите его в папку проекта или рядом с ней.");
    return;
}

try
{
    Console.WriteLine("[1/5] Чтение и предобработка исходного CSV...");

    var listings = CsvService.LoadAndPrepare(csvPath);

    Console.WriteLine(
        $"      После фильтрации TRY, продажи и ценовых выбросов: {listings.Count:N0} объектов.");

    using var http = new HttpClient
    {
        BaseAddress = new Uri(apiUrl),
        Timeout = TimeSpan.FromMinutes(10)
    };

    Console.WriteLine("[2/5] Проверка существующего FastAPI и загрузка сведений о готовой модели...");

    var health = await http.GetFromJsonAsync<JsonElement>("/health");

    if (!health.TryGetProperty("status", out var status) ||
        status.GetString() != "ok")
    {
        throw new InvalidOperationException("FastAPI не подтвердил готовность.");
    }

    var modelInfo = await http.GetFromJsonAsync<ModelInfo>("/model/info")
        ?? throw new InvalidOperationException("API не вернул /model/info.");

    Console.WriteLine(
        $"      API доступен. Уже обученная модель: {modelInfo.best_model_name}.");

    Console.WriteLine("[3/5] Получение прогнозов через существующий model_pipeline.joblib...");

    var predictions = new List<double>(listings.Count);

    const int batchSize = 500;

    for (int offset = 0; offset < listings.Count; offset += batchSize)
    {
        var batch = listings
            .Skip(offset)
            .Take(batchSize)
            .Select(ToApiRow)
            .ToList();

        using var response = await http.PostAsJsonAsync(
            "/predict/batch",
            new { rows = batch });

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"API вернул {(int)response.StatusCode}: {body}");
        }

        var result = JsonSerializer.Deserialize<List<PredictionResult>>(
            body,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException(
                "API вернул пустой список прогнозов.");

        if (result.Count != batch.Count)
        {
            throw new InvalidOperationException(
                $"Количество прогнозов ({result.Count}) не совпадает " +
                $"с количеством объектов в пакете ({batch.Count}).");
        }

        predictions.AddRange(result.Select(x => x.predicted_price));

        int done = Math.Min(offset + batch.Count, listings.Count);

        Console.Write($"      Обработано: {done:N0}/{listings.Count:N0}\r");
    }

    Console.WriteLine();

    Console.WriteLine("[4/5] Расчёт метрик по фактическим ценам и прогнозам...");

    var actual = listings
        .Select(x => x.ActualPrice)
        .ToList();

    var metrics = MetricsService.Calculate(actual, predictions);

    PrintDatasetMetrics(metrics);

    Console.WriteLine("[5/5] Результаты обучения, выбора модели и тестирования из API:");

    PrintApiMetrics(modelInfo);

    PrintLargestErrors(listings, predictions);

    var output = Path.Combine(
        Path.GetDirectoryName(csvPath)!,
        "result_real_estate_samples.csv");

    await SaveResults(output, listings, predictions);

    Console.WriteLine();
    Console.WriteLine($"Таблица фактических цен и прогнозов сохранена: {output}");
    Console.WriteLine(
        "Готово. Модели повторно не обучались — использован готовый pipeline из API.");
}
catch (HttpRequestException ex)
{
    Fail(
        $"Не удалось подключиться к FastAPI ({apiUrl}). " +
        $"Сначала запустите Python API. Подробности: {ex.Message}");
}
catch (Exception ex)
{
    Fail(ex.Message);
}


// Преобразование объекта CSV в строку для FastAPI.
// Некорректные и выходящие за допустимые пределы числа отправляются как null.
static object ToApiRow(Listing x)
{
    return new
    {
        sub_type = EmptyToNull(x.SubType),
        heating_type = EmptyToNull(x.HeatingType),

        size = GetValidNumber(x.Size, 0.000001, 5000),
        room_count = GetValidNumber(x.RoomCount, 0, 20),
        building_age = GetValidNumber(x.BuildingAge, 0, 100),
        total_floor_count = GetValidNumber(x.TotalFloorCount, 1, 100),
        tom = GetValidNumber(x.Tom, 0, 3650),
        listing_days = GetValidNumber(x.ListingDays, 0, 3650)
    };
}


// Возвращает число, если оно корректное и находится в заданном диапазоне.
// Иначе возвращает null.
static double? GetValidNumber(
    object? value,
    double min,
    double max)
{
    if (value is null)
        return null;

    double number;

    try
    {
        number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
    catch
    {
        return null;
    }

    if (!double.IsFinite(number))
        return null;

    if (number < min || number > max)
        return null;

    return number;
}


static string? EmptyToNull(string? value)
{
    return string.IsNullOrWhiteSpace(value) || value == "<br>"
        ? null
        : value.Trim();
}


static string? FindFile(string name)
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);

    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, name);

        if (File.Exists(candidate))
            return Path.GetFullPath(candidate);

        current = current.Parent;
    }

    var outputCandidate = Path.Combine(AppContext.BaseDirectory, name);

    return File.Exists(outputCandidate)
        ? Path.GetFullPath(outputCandidate)
        : null;
}


static void PrintDatasetMetrics(Metrics m)
{
    Console.WriteLine();
    Console.WriteLine("МЕТРИКИ ПО CSV (прогноз каждого объекта получен от API)");
    Console.WriteLine(new string('-', 65));

    Console.WriteLine($"Количество объектов: {m.Count:N0}");
    Console.WriteLine($"MAE  = mean(|y - ŷ|):             {m.Mae:N2} TRY");
    Console.WriteLine($"RMSE = sqrt(mean((y - ŷ)^2)):     {m.Rmse:N2} TRY");
    Console.WriteLine($"MAPE = mean(|(y - ŷ) / y|)*100:   {m.Mape:F2}%");
    Console.WriteLine($"R²:                               {m.R2:F4}");
    Console.WriteLine($"Средняя фактическая цена:         {m.MeanActual:N2} TRY");

    Console.WriteLine(
        $"Среднее смещение (прогноз-факт):  " +
        $"{m.MeanSignedError:+#,##0.00;-#,##0.00;0.00} TRY");
}


static void PrintApiMetrics(ModelInfo info)
{
    Console.WriteLine($"Лучшая модель: {info.best_model_name}");
    Console.WriteLine("Сравнение моделей на validation:");

    Console.WriteLine(
        $"{"Модель",-22} {"MAE",14} {"RMSE",14} {"MAPE",10} {"R²",10}");

    foreach (var row in info.metrics_validation)
    {
        string Get(string key) =>
            row.TryGetValue(key, out var value)
                ? JsonValue(value)
                : "—";

        Console.WriteLine(
            $"{Get("model"),-22} " +
            $"{Get("MAE"),14} " +
            $"{Get("RMSE"),14} " +
            $"{Get("MAPE"),10} " +
            $"{Get("R2"),10}");
    }

    Console.WriteLine(
        "Метрики лучшей модели на test из сохранённого артефакта:");

    foreach (var kv in info.metrics_test_best_model)
    {
        Console.WriteLine($"  {kv.Key,-8}: {kv.Value:N4}");
    }
}


static string JsonValue(object value)
{
    if (value is JsonElement e)
    {
        return e.ValueKind == JsonValueKind.Number
            ? e.GetDouble().ToString("N2")
            : e.ToString();
    }

    return Convert.ToString(value, CultureInfo.CurrentCulture) ?? "—";
}


static void PrintLargestErrors(
    IReadOnlyList<Listing> rows,
    IReadOnlyList<double> predictions)
{
    Console.WriteLine();
    Console.WriteLine("ПЯТЬ НАИБОЛЬШИХ ОШИБОК ПРОГНОЗА:");

    var worst = rows
        .Select((r, i) => new
        {
            Row = r,
            Prediction = predictions[i],
            Error = Math.Abs(r.ActualPrice - predictions[i])
        })
        .OrderByDescending(x => x.Error)
        .Take(5);

    foreach (var x in worst)
    {
        Console.WriteLine(
            $"  CSV-строка {x.Row.SourceRow}: " +
            $"факт {x.Row.ActualPrice:N0} TRY; " +
            $"прогноз {x.Prediction:N0} TRY; " +
            $"|ошибка| {x.Error:N0} TRY");
    }
}


static async Task SaveResults(
    string path,
    IReadOnlyList<Listing> rows,
    IReadOnlyList<double> predictions)
{
    await using var writer = new StreamWriter(
        path,
        false,
        new UTF8Encoding(true));

    await writer.WriteLineAsync(
        "source_csv_row;actual_price;predicted_price;absolute_error;model_source");

    for (int i = 0; i < rows.Count; i++)
    {
        var r = rows[i];

        await writer.WriteLineAsync(
            $"{r.SourceRow};" +
            $"{r.ActualPrice.ToString(CultureInfo.InvariantCulture)};" +
            $"{predictions[i].ToString(CultureInfo.InvariantCulture)};" +
            $"{Math.Abs(r.ActualPrice - predictions[i]).ToString(CultureInfo.InvariantCulture)};" +
            "FastAPI model_pipeline.joblib");
    }
}


static void Fail(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ОШИБКА: " + message);
    Console.ResetColor();
}
