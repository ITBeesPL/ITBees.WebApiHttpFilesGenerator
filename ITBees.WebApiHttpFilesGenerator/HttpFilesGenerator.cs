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

        if (newJson is JsonObject newObj && oldJson is JsonObject oldObj)
        {
            foreach (var kv in newObj.ToList())
            {
                var propName = kv.Key;
                var newVal = kv.Value;

                if (!oldObj.ContainsKey(propName))
                    continue;

                var oldVal = oldObj[propName];

                // Both are objects: go deeper
                if (newVal is JsonObject && oldVal is JsonObject)
                {
                    newObj[propName] = MergeJsonKeepingOnlyNewFields(
                        newVal.ToJsonString(),
                        oldVal.ToJsonString()
                    );
                }
                // new is object, old is string => try to parse old string as object, then merge
                else if (newVal is JsonObject newValObj && oldVal is JsonValue oldValString)
                {
                    try
                    {
                        var parsedOldVal = JsonNode.Parse(oldValString.ToJsonString());
                        if (parsedOldVal is JsonObject oldValObj)
                        {
                            // attempt a deeper merge if user changed data
                            var merged =
                                MergeJsonKeepingOnlyNewFields(newValObj.ToJsonString(), oldValObj.ToJsonString());
                            newObj[propName] = JsonNode.Parse(merged);
                        }
                        else
                        {
                            // keep new object
                        }
                    }
                    catch
                    {
                        // keep new object
                    }
                }
                // new is string, old is object => keep old object
                else if (newVal is JsonValue && oldVal is JsonObject oldValObj2)
                {
                    var clone = JsonNode.Parse(oldValObj2.ToJsonString());
                    newObj[propName] = clone;
                }
                // fallback => clone old
                else
                {
                    var oldValClone = JsonNode.Parse(oldVal.ToJsonString());
                    newObj[propName] = oldValClone;
                }
            }

            return newObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        return newBody;
    }
}