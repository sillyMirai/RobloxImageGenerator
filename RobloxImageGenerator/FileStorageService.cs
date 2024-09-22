using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class FileStorageService
{
    private readonly ILogger<FileStorageService> _logger;
    private readonly string _storagePath;
    private const int maxFiles = 10;

    public FileStorageService(ILogger<FileStorageService> logger)
    {
        _logger = logger;
        _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "PlayerData");

        if (!Directory.Exists(_storagePath))
        {
            try
            {
                Directory.CreateDirectory(_storagePath);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to create directory: {_storagePath}. Error: {ex.Message}");
                throw;
            }
        }
    }

    private List<string> generationFiles(string playerDirectory)
    {
        return Directory.GetFiles(playerDirectory).OrderBy(f => File.GetCreationTime(f)).ToList();
    }

    public async Task SaveGenerationDataAsync(string playerName, object generationData)
    {
        try
        {
            string playerDirectory = Path.Combine(_storagePath, playerName);

            if (!Directory.Exists(playerDirectory))
            {
                Directory.CreateDirectory(playerDirectory);
            }

            var files = generationFiles(playerDirectory);
            var generationCount = files.Count;
            var filePath = Path.Combine(playerDirectory, $"Generation_{generationCount}.json");

            var serializedGeneration = JsonConvert.SerializeObject(generationData);
            using (var streamWriter = File.CreateText(filePath))
            {
                await streamWriter.WriteAsync(serializedGeneration);
            }

            files = generationFiles(playerDirectory);

            // Ensure the number of files does not exceed the maximum limit
            if (files.Count > maxFiles)
            {
                var filesToDelete = files.Count - maxFiles;
                for (int i = 0; i < filesToDelete; i++)
                {
                    File.Delete(files[i]);
                }

                files = generationFiles(playerDirectory);

                // Rename remaining files to maintain sequential indexing
                for (int i = 0; i < files.Count; i++)
                {
                    var newFileName = Path.Combine(playerDirectory, $"Generation_{i}.json");
                    if (files[i] != newFileName)
                    {
                        File.Move(files[i], newFileName);
                        files[i] = newFileName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error Occurred While Saving Data: {ex.Message}");
        }
    }

    public async Task<Generation[]> GetAllGenerationsAsync(string playerName)
    {
        var playerDirectory = Path.Combine(_storagePath, playerName);

        try
        {
            if (!Directory.Exists(playerDirectory))
            {
                Directory.CreateDirectory(playerDirectory);
            }

            var files = Directory.GetFiles(playerDirectory, "*.json");
            var generations = new List<Generation>();

            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file);
                try
                {
                    var generation = JsonConvert.DeserializeObject<Generation>(content);
                    generations.Add(generation);
                }
                catch (JsonException ex)
                {
                    _logger.LogError($"Error deserializing JSON in file {file}: {ex.Message}");
                }
            }

            return generations.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error Occurred While Reading Data: {ex.Message}");
            return null;
        }
    }

    public bool DeleteGenerationAsync(string playerName, string generationName)
    {
        var playerDirectory = Path.Combine(_storagePath, playerName);
        var filePath = Path.Combine(playerDirectory, $"{generationName}.json");

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);

                // Rename remaining files to maintain sequential indexing
                var files = Directory.GetFiles(playerDirectory, "*.json").OrderBy(f => f).ToList();

                for (int i = 0; i < files.Count; i++)
                {
                    var newFileName = Path.Combine(playerDirectory, $"Generation_{i}.json");
                    if (files[i] != newFileName)
                    {
                        File.Move(files[i], newFileName);
                    }
                }

                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error Occurred While Deleting File: {ex.Message}");
            return false;
        }
    }

}

public class MetaDataConverter : JsonConverter<MetaData>
{
    public override MetaData ReadJson(JsonReader reader, Type objectType, MetaData existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var metaString = (string)JToken.Load(reader);
        return JsonConvert.DeserializeObject<MetaData>(metaString);
    }

    public override void WriteJson(JsonWriter writer, MetaData value, JsonSerializer serializer)
    {
        var metaString = JsonConvert.SerializeObject(value);
        writer.WriteValue(metaString);
    }
}

public class MetaData
{
    public long Timestamp { get; set; }
    public string Style { get; set; }
    public List<int> Size { get; set; }
}

public class ImageData
{
    public List<long> Pixels { get; set; }
    public bool IsNSFW { get; set; }
}


 public class Generation
{
    [JsonConverter(typeof(MetaDataConverter))]
    public MetaData Meta { get; set; }
    public List<ImageData> Images { get; set; }
}
