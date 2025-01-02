namespace ITBees.WebApiHttpFilesGenerator;

public class HttpSnippet
{
    public string Method { get; set; }              // e.g. GET, POST, PUT, DELETE, PATCH
    public string RawPath { get; set; }             // e.g. "{{OperatorApi_HostAddress}}/customer?guid=000..."
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    public string Body { get; set; } = "";          // The raw body (e.g. JSON for POST)
    public Dictionary<string, string> QueryParams { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Key used to identify the snippet ignoring query string. 
    /// E.g. "GET:{{OperatorApi_HostAddress}}/customer"
    /// </summary>
    public string RouteKey
    {
        get
        {
            var pathNoQuery = RawPath.Split('?')[0];
            return Method.ToUpper() + ":" + pathNoQuery;
        }
    }

    /// <summary>
    /// Rebuilds RawPath with updated QueryParams.
    /// </summary>
    public string BuildFullPath()
    {
        var basePart = RawPath.Split('?')[0];
        if (QueryParams.Count == 0)
            return basePart;

        var q = string.Join("&", QueryParams.Select(kv => $"{kv.Key}={kv.Value}"));
        return basePart + "?" + q;
    }
}