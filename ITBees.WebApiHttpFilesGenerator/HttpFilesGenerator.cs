using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ITBees.WebApiHttpFilesGenerator;

public class HttpFilesGenerator
{
    private static readonly string DefaultPrivateUserJson = @"{
  ""dev"": {
    ""adminLogin"": """",
    ""adminPass"": """",
    ""value"": """",
    ""tokenExpirationDate"": ""2025-01-01T09:39:54Z"",
    ""operatorLogin"": """",
    ""operatorPass"": """",
    ""valueOperator"": """",
    ""tokenOperatorExpirationDate"": ""2025-01-02T17:12:05Z"",
    ""userLogin"": """",
    ""userPass"": """"
  },
  ""uat"": {
    ""adminLogin"": """",
    ""adminPass"": """",
    ""value"": """",
    ""tokenExpirationDate"": ""2025-01-01T09:39:54Z"",
    ""operatorLogin"": """",
    ""operatorPass"": """",
    ""valueOperator"": """",
    ""tokenOperatorExpirationDate"": ""2025-01-02T17:12:05Z"",
    ""userLogin"": """",
    ""userPass"": """"
  },
  ""prod"": {
    ""OperatorApi_HostAddress"": """",
    ""adminLogin"": ""kuba@skb.pl"",
    ""adminPass"": ""Fdsjk192Fdsjk192!@"",
    ""value"": """"
  }
}";

    private static readonly string DefaultHttpEnvJson = @"{
  ""dev"": {
    ""OperatorApi_HostAddress"": ""https://localhost:5023"",
    ""adminLogin"": """",
    ""adminPass"": """",
    ""operatorLogin"": """",
    ""operatorPass"": """",
    ""userLogin"": """",
    ""userPass"": """"
  },
  ""uat"": {
    ""OperatorApi_HostAddress"": ""https://localhost:5024"",
    ""adminLogin"": """",
    ""adminPass"": """",
    ""operatorLogin"": """",
    ""operatorPass"": """",
    ""userLogin"": """",
    ""userPass"": """"
  },
  ""prod"": {
    ""OperatorApi_HostAddress"": ""https://localhost:5025"",
    ""adminLogin"": """",
    ""adminPass"": """",
    ""operatorLogin"": """",
    ""operatorPass"": """",
    ""userLogin"": """",
    ""userPass"": """"
  }
}";

    /// <summary>
    /// Runs file generator
    /// </summary>
    /// <param name="sourceAssembly">Assembly to scan for controllers.</param>
    /// <param name="sourceCodeProjectPath">Optional: folder path where .http files should be generated.</param>
    /// <param name="outputFolderName">Optional: subdirectory name (defaults to 'HttpApi').</param>
    public static void RegenerateHttpFiles(Assembly sourceAssembly, string? sourceCodeProjectPath = null,
        string? outputFolderName = null)
    {
        // 1) Determine where to place our files
        if (string.IsNullOrEmpty(sourceCodeProjectPath))
        {
            sourceCodeProjectPath = Directory.GetCurrentDirectory();
        }

        if (string.IsNullOrEmpty(outputFolderName))
        {
            outputFolderName = "HttpApi";
        }

        var outputDirectory = Path.Combine(sourceCodeProjectPath, outputFolderName);
        Directory.CreateDirectory(outputDirectory);

        // 2) Create or overwrite the environment files if they do not exist.
        EnsureEnvironmentFilesExist(outputDirectory);

        // 3) Reflect over controllers in the specified assembly and generate .http files
        var generator = new HttpSnippetGenerator();
        var snippetsPerController = generator.GenerateHttpSnippetsPerController(sourceAssembly);

        foreach (var kv in snippetsPerController)
        {
            var controllerName = kv.Key;
            var newSnippets = kv.Value;

            var fileName = $"{controllerName}.http";
            var filePath = Path.Combine(outputDirectory, fileName);

            var oldSnippets = HttpFileParser.ParseHttpFile(filePath);
            var dictOld = oldSnippets.ToDictionary(s => s.RouteKey, s => s);

            var finalSnippets = new List<HttpSnippet>(newSnippets);

            // Merge old values (queries, JSON merges) into new snippet
            foreach (var snippet in finalSnippets)
            {
                if (dictOld.TryGetValue(snippet.RouteKey, out var oldSnippet))
                {
                    // Merge query params from old
                    foreach (var kvp in snippet.QueryParams.ToList())
                    {
                        if (oldSnippet.QueryParams.TryGetValue(kvp.Key, out var oldVal))
                        {
                            snippet.QueryParams[kvp.Key] = oldVal;
                        }
                    }

                    // Merge body from old if POST/PUT/PATCH
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

    /// <summary>
    /// Checks if environment files exist in the output directory; if not, writes default content.
    /// </summary>
    private static void EnsureEnvironmentFilesExist(string outputDirectory)
    {
        // 1) http-client.private.env.json
        var privateEnvFilePath = Path.Combine(outputDirectory, "http-client.private.env.json");
        if (!File.Exists(privateEnvFilePath))
        {
            File.WriteAllText(privateEnvFilePath, DefaultPrivateUserJson);
            Console.WriteLine("Created http-client.private.env.json with default content.");
        }
        else
        {
            Console.WriteLine("http-client.private.env.json already exists. Skipping overwrite.");
        }

        // 2) http-client.env.json.user
        var envUserFilePath = Path.Combine(outputDirectory, "http-client.env.json.user");
        if (!File.Exists(envUserFilePath))
        {
            File.WriteAllText(envUserFilePath, DefaultPrivateUserJson);
            Console.WriteLine("Created http-client.env.json.user with default content.");
        }
        else
        {
            Console.WriteLine("http-client.env.json.user already exists. Skipping overwrite.");
        }

        // 3) http-client.env.json
        var envJsonFilePath = Path.Combine(outputDirectory, "http-client.env.json");
        if (!File.Exists(envJsonFilePath))
        {
            File.WriteAllText(envJsonFilePath, DefaultHttpEnvJson);
            Console.WriteLine("Created http-client.env.json with default content.");
        }
        else
        {
            Console.WriteLine("http-client.env.json already exists. Skipping overwrite.");
        }
    }

    private static string MergeJsonKeepingOnlyNewFields(string newBody, string oldBody)
    {
        JsonNode newJson;
        JsonNode oldJson;

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

        // If either is not an object, return newBody
        if (newJson is not JsonObject newObj || oldJson is not JsonObject oldObj)
            return newBody;

        // Walk through each key in newObj
        foreach (var kv in newObj.ToList())
        {
            var propName = kv.Key;
            var newVal = kv.Value;
            if (!oldObj.ContainsKey(propName))
                continue;

            var oldVal = oldObj[propName];

            // Both new and old are objects => merge them recursively
            if (newVal is JsonObject newValObj && oldVal is JsonObject oldValObj)
            {
                var merged = MergeJsonKeepingOnlyNewFields(newValObj.ToJsonString(), oldValObj.ToJsonString());
                // Try parsing the merged result back to a JsonNode
                try
                {
                    var mergedNode = JsonNode.Parse(merged);
                    newObj[propName] = mergedNode;
                }
                catch
                {
                    newObj[propName] = newVal;
                }
            }
            // new is object, old is string => keep the newly generated object 
            else if (newVal is JsonObject && oldVal is JsonValue)
            {
                // We do nothing; newVal stays in newObj
            }
            // new is string, old is object => keep the old object
            else if (newVal is JsonValue && oldVal is JsonObject oldObjVal)
            {
                var clone = JsonNode.Parse(oldObjVal.ToJsonString());
                newObj[propName] = clone;
            }
            // Otherwise -> keep old as-is
            else
            {
                // But clone it to avoid "parent" conflicts
                var oldValClone = JsonNode.Parse(oldVal.ToJsonString());
                newObj[propName] = oldValClone;
            }
        }

        return newObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}