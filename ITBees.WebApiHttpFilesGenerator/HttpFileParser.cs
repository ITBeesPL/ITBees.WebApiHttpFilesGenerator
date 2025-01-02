using System.Text;

namespace ITBees.WebApiHttpFilesGenerator;

public static class HttpFileParser
{
    public static List<HttpSnippet> ParseHttpFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<HttpSnippet>();

        var lines = File.ReadAllLines(filePath);
        var snippets = new List<HttpSnippet>();

        HttpSnippet currentSnippet = null;
        var bodyBuilder = new StringBuilder();
        bool inHeaders = false;
        bool inBody = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Start of snippet?
            if (line.StartsWith("###"))
            {
                // finalize previous
                if (currentSnippet != null)
                {
                    currentSnippet.Body = bodyBuilder.ToString();
                    snippets.Add(currentSnippet);
                }

                currentSnippet = null;
                bodyBuilder.Clear();
                inHeaders = false;
                inBody = false;
                continue;
            }

            // If line is e.g. "GET {{OperatorApi_HostAddress}}/customer"
            if ((line.StartsWith("GET ") || line.StartsWith("POST ") ||
                 line.StartsWith("PUT ") || line.StartsWith("DELETE ") ||
                 line.StartsWith("PATCH ")) && currentSnippet == null)
            {
                currentSnippet = new HttpSnippet();
                var spaceIndex = line.IndexOf(' ');
                currentSnippet.Method = line.Substring(0, spaceIndex).Trim();
                currentSnippet.RawPath = line.Substring(spaceIndex + 1).Trim();

                inHeaders = true;
                inBody = false;
                continue;
            }

            // empty line => end of headers, start body
            if (string.IsNullOrWhiteSpace(line) && currentSnippet != null && inHeaders)
            {
                inHeaders = false;
                inBody = true;
                continue;
            }

            if (inHeaders && currentSnippet != null)
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var headerName = line.Substring(0, colonIndex).Trim();
                    var headerValue = line.Substring(colonIndex + 1).Trim();
                    currentSnippet.Headers[headerName] = headerValue;
                }
            }
            else if (inBody && currentSnippet != null)
            {
                bodyBuilder.AppendLine(line);
            }
        }

        // finalize last snippet if file didn't end with ###
        if (currentSnippet != null)
        {
            currentSnippet.Body = bodyBuilder.ToString();
            snippets.Add(currentSnippet);
        }

        // parse query params
        foreach (var s in snippets)
        {
            ParseQueryParams(s);
        }

        return snippets;
    }

    private static void ParseQueryParams(HttpSnippet sn)
    {
        var parts = sn.RawPath.Split('?');
        if (parts.Length > 1)
        {
            var queryString = parts[1];
            var segments = queryString.Split('&');
            foreach (var seg in segments)
            {
                var eqIndex = seg.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = seg.Substring(0, eqIndex);
                    var val = seg.Substring(eqIndex + 1);
                    sn.QueryParams[key] = val;
                }
            }
        }
    }
}