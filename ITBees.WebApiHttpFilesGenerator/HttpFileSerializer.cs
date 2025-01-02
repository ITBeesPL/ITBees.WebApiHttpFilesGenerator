using System.Text;

namespace ITBees.WebApiHttpFilesGenerator;

public static class HttpFileSerializer
{
    public static string SerializeSnippets(List<HttpSnippet> snippets)
    {
        var sb = new StringBuilder();
        foreach (var sn in snippets)
        {
            sb.AppendLine("###");
            sb.AppendLine($"{sn.Method} {BuildFullPath(sn)}");

            // headers
            foreach (var kv in sn.Headers)
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }

            sb.AppendLine(); // blank line
            if (!string.IsNullOrWhiteSpace(sn.Body))
            {
                sb.Append(sn.Body);
                if (!sn.Body.EndsWith("\n"))
                    sb.AppendLine();
            }
            sb.AppendLine(); // extra blank line
        }

        return sb.ToString();
    }

    private static string BuildFullPath(HttpSnippet sn)
    {
        var basePart = sn.RawPath.Split('?')[0];
        if (sn.QueryParams.Count == 0)
            return basePart;

        var q = string.Join("&", sn.QueryParams.Select(kv => $"{kv.Key}={kv.Value}"));
        return basePart + "?" + q;
    }
}