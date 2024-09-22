using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

public class ImageGenerationService
{
    private readonly string pythonScripts;
    private readonly SemaphoreSlim generationSemaphore;

    public ImageGenerationService()
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        string projectDirectory = Directory.GetParent(currentDirectory).Parent.Parent.FullName;
        pythonScripts = Path.Combine(projectDirectory, "PythonScripts");

        generationSemaphore = new SemaphoreSlim(2);
    }

    private string GetGenerationParameters(List<dynamic> generationParams)
    {
        List<string> argumentList = new List<string>();

        foreach (JsonElement jsonElement in generationParams)
        {
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.String:
                    argumentList.Add($"\"{jsonElement.GetString()}\"");
                    break;
                case JsonValueKind.Number:
                    argumentList.Add(jsonElement.GetRawText());
                    break;
                case JsonValueKind.Array:
                    foreach (var subElement in jsonElement.EnumerateArray())
                    {
                        argumentList.Add(subElement.GetRawText());
                    }
                    break;
            }
        }

        return string.Join(" ", argumentList);
    }

    public async Task<List<(List<long>, bool)>> GenerateImage(List<dynamic> generationParams, ILogger logger)
    {
        await generationSemaphore.WaitAsync();

        try
        {
            string pythonScriptPath = Path.Combine(pythonScripts, "ImageGen.py");
            string arguments = GetGenerationParameters(generationParams);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "python", // Or the full path to the python executable if not in PATH
                Arguments = $"{pythonScriptPath} {arguments}",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using (Process process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                StringBuilder outputBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.Append(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    logger.LogError($"{DateTime.Now} Error has occurred while generating image.\nExit code: {process.ExitCode}");
                    return null;
                }

                string output = outputBuilder.ToString();

                try
                {
                    var response = JsonSerializer.Deserialize<List<ImageData>>(output);
                    var results = new List<(List<long>, bool)>();

                    foreach (var image in response)
                    {
                        results.Add((image.Pixels, image.IsNSFW));
                    }

                    return results;
                }
                catch (JsonException ex)
                {
                    logger.LogError($"Failed to deserialize image generation response: {ex.Message}");
                    return null;
                }
            }
        }
        finally
        {
            generationSemaphore.Release(); 
        }
    }
}

