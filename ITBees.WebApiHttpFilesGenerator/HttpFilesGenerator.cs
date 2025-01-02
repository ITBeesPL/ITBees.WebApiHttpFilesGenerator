using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ITBees.WebApiHttpFilesGenerator;

public class HttpFilesGenerator
{
    public static void RegenerateHttpFiles(Assembly sourceAssembly, string? sourceCodeProjectPath = null,
        string? outputFolderName = null)
    {
        if (string.IsNullOrEmpty(sourceCodeProjectPath))
        {
        }

        if (string.IsNullOrEmpty(outputFolderName))
        {
            outputFolderName = "HttpApi";
        }

        var targetProjectFolderPath = outputFolderName;
        var assembly = sourceAssembly;

        var generator = new HttpSnippetGenerator();

        var snippetsPerController = generator.GenerateHttpSnippetsPerController(assembly);

        var outputDirectory = Path.Combine(targetProjectFolderPath, "HttpApi");
        Directory.CreateDirectory(outputDirectory);

        foreach (var kv in snippetsPerController)
        {
            var controllerName = kv.Key;
            var newSnippets = kv.Value;

            var fileName = $"{controllerName}.http";
            var filePath = Path.Combine(outputDirectory, fileName);

            var oldSnippets = HttpFileParser.ParseHttpFile(filePath);
            var dictOld = oldSnippets.ToDictionary(s => s.RouteKey, s => s);

            var finalSnippets = new List<HttpSnippet>(newSnippets);

            foreach (var snippet in finalSnippets)
            {
                if (dictOld.TryGetValue(snippet.RouteKey, out var oldSnippet))
                {
                    foreach (var kvp in snippet.QueryParams.ToList())
                    {
                        if (oldSnippet.QueryParams.TryGetValue(kvp.Key, out var oldVal))
                        {
                            snippet.QueryParams[kvp.Key] = oldVal;
                        }
                    }

                    if ((snippet.Method == "POST" || snippet.Method == "PUT" || snippet.Method == "PATCH")
                        && !string.IsNullOrWhiteSpace(oldSnippet.Body))
                    {
                        snippet.Body = MergeJsonKeepingOnlyNewFields(snippet.Body, oldSnippet.Body);
                    }
                }
            }

            var finalContent = HttpFileSerializer.SerializeSnippets(finalSnippets);
            File.WriteAllText(filePath, finalContent);
            Console.WriteLine($"Generated/Updated: {fileName}");
        }
    }

    static string MergeJsonKeepingOnlyNewFields(string newBody, string oldBody)
    {
        JsonNode newJson, oldJson;
        try
        {
            newJson = JsonNode.Parse(newBody);
        }
        catch
        {
            return oldBody;
        }

        try
        {
            oldJson = JsonNode.Parse(oldBody);
        }
        catch
        {
            return newBody;
        }

        if (newJson is JsonObject newObj && oldJson is JsonObject oldObj)
        {
            foreach (var kv in newObj.ToList())
            {
                var propName = kv.Key;
                var newVal = kv.Value;

                if (oldObj.ContainsKey(propName))
                {
                    var oldVal = oldObj[propName];

                    // Obiekt + Obiekt => rekurencja
                    if (newVal is JsonObject && oldVal is JsonObject)
                    {
                        newObj[propName] = MergeJsonKeepingOnlyNewFields(
                            newVal.ToJsonString(),
                            oldVal.ToJsonString()
                        );
                    }
                    // Nowy = obiekt, stary = string => zostaw nowy obiekt
                    else if (newVal is JsonObject && oldVal is JsonValue)
                    {
                        // nic nie robimy
                    }
                    // Nowy = string, stary = obiekt => zostaw stary obiekt
                    else if (newVal is JsonValue && oldVal is JsonObject)
                    {
                        var oldValClone = JsonNode.Parse(oldVal.ToJsonString());
                        newObj[propName] = oldValClone;
                    }
                    // Standard -> klonujemy starego
                    else
                    {
                        var oldValClone = JsonNode.Parse(oldVal.ToJsonString());
                        newObj[propName] = oldValClone;
                    }
                }
            }

            return newObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        return newBody;
    }
}