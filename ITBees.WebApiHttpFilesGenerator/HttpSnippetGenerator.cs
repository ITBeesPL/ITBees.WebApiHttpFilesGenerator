using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace ITBees.WebApiHttpFilesGenerator;

public class HttpSnippetGenerator
{
    public Dictionary<string, List<HttpSnippet>> GenerateHttpSnippetsPerController(Assembly assembly)
    {
        var controllers = assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => !t.IsGenericType || t.IsConstructedGenericType)
            .ToList();

        var result = new Dictionary<string, List<HttpSnippet>>();

        foreach (var controllerType in controllers)
        {
            var controllerName = controllerType.Name;
            var snippetList = BuildSnippetsForController(controllerType);
            result[controllerName] = snippetList;
        }

        return result;
    }

    private List<HttpSnippet> BuildSnippetsForController(Type controllerType)
    {
        var baseRoute = GetControllerRoute(controllerType);

        var actionMethods = controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.CustomAttributes.Any(a => IsHttpMethodAttribute(a.AttributeType)))
            .ToList();

        var snippets = new List<HttpSnippet>();

        foreach (var method in actionMethods)
        {
            var snippet = BuildSnippetForAction(method, baseRoute);
            if (snippet.Method == "GET" || snippet.Method == "DELETE")
            {
                var methodParams = method.GetParameters();
                foreach (var p in methodParams)
                {
                    if (p.GetCustomAttribute<FromBodyAttribute>() == null && !IsRouteParameter(method, p))
                    {
                        snippet.QueryParams[p.Name] = GenerateQueryValue(p.ParameterType, p.Name);
                    }
                }
            }
            snippets.Add(snippet);
        }

        return snippets;
    }

    private HttpSnippet BuildSnippetForAction(MethodInfo method, string baseRoute)
    {
        var httpMethodAttr = method
            .GetCustomAttributes()
            .FirstOrDefault(a => IsHttpMethodAttribute(a.GetType()));

        var httpMethod = ResolveHttpMethod(httpMethodAttr.GetType());
        var methodRoute = GetMethodRoute(httpMethodAttr);
        var combinedRoute = CombineRoutes(baseRoute, methodRoute, method.DeclaringType?.Name);

        var snippet = new HttpSnippet
        {
            Method = httpMethod,
            RawPath = $"{{{{OperatorApi_HostAddress}}}}/{combinedRoute}"
        };

        snippet.Headers["Accept"] = "application/json";
        snippet.Headers["Content-Type"] = "application/json";
        snippet.Headers["Authorization"] = "bearer {{value}}";

        var fromBodyParam = method.GetParameters()
            .FirstOrDefault(p => p.GetCustomAttribute<FromBodyAttribute>() != null);

        if (fromBodyParam != null)
        {
            snippet.Body = GenerateJsonForType(fromBodyParam.ParameterType);
        }

        return snippet;
    }

    private bool IsRouteParameter(MethodInfo method, ParameterInfo param)
    {
        var httpAttr = method.GetCustomAttributes().FirstOrDefault(a => IsHttpMethodAttribute(a.GetType()));
        if (httpAttr == null) return false;

        var route = GetMethodRoute(httpAttr);
        if (route.Contains($"{{{param.Name}}}", StringComparison.OrdinalIgnoreCase)) return true;
        if (param.GetCustomAttribute<FromRouteAttribute>() != null) return true;
        return false;
    }

    private string GenerateQueryValue(Type t, string paramName)
    {
        switch (paramName.ToLowerInvariant())
        {
            case "page":
                return "1";
            case "pagesize":
                return "25";
            case "sortcolumn":
                return "Id";
            case "sortorder":
                return "Descending";
        }

        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(Guid)) return "00000000-0000-0000-0000-000000000000";
        if (t == typeof(string)) return "stringValue";
        if (t == typeof(bool)) return "false";
        if (t.IsEnum) return Enum.GetNames(t).FirstOrDefault() ?? "EnumValue";
        if (t == typeof(DateTime)) return "2024-01-01T00:00:00";

        // Handle DateOnly
        if (t.FullName == "System.DateOnly") return "2024-01-01";

        if (t == typeof(int) || t == typeof(long) || t == typeof(short)
            || t == typeof(decimal) || t == typeof(float) || t == typeof(double))
        {
            return "0";
        }
        return "stringValue";
    }

    private string GenerateJsonForType(Type type, int depth = 0)
    {
        if (depth > 5) return "{ \"_recursiveLimit\": true }";
        if (IsSimpleType(type)) return GenerateSimpleValue(type);

        var sb = new StringBuilder();
        sb.AppendLine("{");

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();

        for (int i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            var propName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);
            var comma = (i < props.Count - 1) ? "," : "";

            if (!IsSimpleType(prop.PropertyType))
            {
                var nestedJson = GenerateJsonForType(prop.PropertyType, depth + 1);
                var indentedNested = IndentJson(nestedJson, 2);
                sb.AppendLine($"  \"{propName}\": {indentedNested}{comma}");
            }
            else
            {
                var val = GenerateSimpleValue(prop.PropertyType);
                sb.AppendLine($"  \"{propName}\": {val}{comma}");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private bool IsSimpleType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.FullName == "System.DateOnly") return true;
        return t.IsPrimitive
               || t == typeof(string)
               || t == typeof(decimal)
               || t == typeof(DateTime)
               || t == typeof(Guid)
               || t.IsEnum;
    }

    private string GenerateSimpleValue(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return "\"stringValue\"";
        if (t == typeof(bool)) return "false";
        if (t.IsEnum)
        {
            var names = Enum.GetNames(t);
            return names.Length > 0 ? $"\"{names[0]}\"" : "\"EnumValue\"";
        }
        if (t == typeof(DateTime)) return "\"2024-01-01T00:00:00\"";
        if (t == typeof(Guid)) return "\"00000000-0000-0000-0000-000000000000\"";

        // DateOnly
        if (t.FullName == "System.DateOnly") return "\"2024-01-01\"";

        if (t == typeof(ushort) || t == typeof(short) || t == typeof(int) || t == typeof(long)
            || t == typeof(float) || t == typeof(double) || t == typeof(decimal))
        {
            return "0";
        }
        return "\"\"";
    }

    private string IndentJson(string json, int spaces)
    {
        var prefix = new string(' ', spaces);
        var lines = json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var indentedLines = lines.Select(line => prefix + line);
        return string.Join(Environment.NewLine, indentedLines);
    }

    private bool IsHttpMethodAttribute(Type t)
    {
        return t == typeof(HttpGetAttribute)
               || t == typeof(HttpPostAttribute)
               || t == typeof(HttpPutAttribute)
               || t == typeof(HttpDeleteAttribute)
               || t == typeof(HttpPatchAttribute);
    }

    private string ResolveHttpMethod(Type t)
    {
        if (t == typeof(HttpGetAttribute)) return "GET";
        if (t == typeof(HttpPostAttribute)) return "POST";
        if (t == typeof(HttpPutAttribute)) return "PUT";
        if (t == typeof(HttpDeleteAttribute)) return "DELETE";
        if (t == typeof(HttpPatchAttribute)) return "PATCH";
        return "GET";
    }

    private string GetControllerRoute(Type controllerType)
    {
        var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
        if (routeAttr != null && !string.IsNullOrWhiteSpace(routeAttr.Template))
        {
            return routeAttr.Template.TrimStart('/');
        }

        var name = controllerType.Name;
        if (name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 10);
        return name.ToLower();
    }

    private string GetMethodRoute(Attribute httpMethodAttribute)
    {
        var prop = httpMethodAttribute
            .GetType()
            .GetProperty("Template", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null)
        {
            var val = prop.GetValue(httpMethodAttribute) as string;
            return val?.Trim('/') ?? string.Empty;
        }
        return string.Empty;
    }

    private string CombineRoutes(string baseRoute, string methodRoute, string controllerName)
    {
        if (string.IsNullOrWhiteSpace(controllerName))
            controllerName = "UnknownController";
        if (controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
            controllerName = controllerName.Substring(0, controllerName.Length - 10);

        var lowercaseName = controllerName.ToLower();
        baseRoute = baseRoute.Replace("[controller]", lowercaseName);

        if (!string.IsNullOrEmpty(methodRoute))
        {
            if (!baseRoute.EndsWith("/"))
                baseRoute += "/";
            baseRoute += methodRoute;
        }
        return baseRoute;
    }
}
