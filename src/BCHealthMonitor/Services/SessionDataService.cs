using System.Net.Http.Headers;
using System.Text.Json;
using BCHealthMonitor.Configuration;
using BCHealthMonitor.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BCHealthMonitor.Services;

public interface ISessionDataService
{
    Task<SessionCounts> GetSessionCountsAsync();
}

public class SessionDataService : ISessionDataService
{
    private readonly ILogger<SessionDataService> _logger;
    private readonly HealthMonitorOptions _options;
    private readonly HttpClient _httpClient;
    private readonly string _instanceName;

    public SessionDataService(
        ILogger<SessionDataService> logger,
        IOptions<HealthMonitorOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("BCApi");
        _instanceName = _options.BCInstance.Name;
    }

    public async Task<SessionCounts> GetSessionCountsAsync()
    {
        // Try SQL first (fastest, most detailed)
        var result = await TryGetFromSqlAsync();
        if (result.IsValid)
        {
            _logger.LogDebug("Session counts from SQL: {Total} total ({WebClient} web client, {WebService} web service, {Background} background)",
                result.Total, result.WebClient, result.WebService, result.Background);
            return result;
        }

        // Fallback to BC API
        result = await TryGetFromApiAsync();
        if (result.IsValid)
        {
            _logger.LogDebug("Session counts from API: {Total} total ({WebClient} web client, {WebService} web service, {Background} background)",
                result.Total, result.WebClient, result.WebService, result.Background);
            return result;
        }

        // Last resort: Performance counters (total only)
        result = await TryGetFromPerfCounterAsync();
        if (result.IsValid)
        {
            _logger.LogDebug("Session counts from PerfCounter: {Total} total (type breakdown unavailable)", result.Total);
            return result;
        }

        _logger.LogWarning("All session data sources failed");
        return SessionCounts.Empty("none", "All session data sources failed");
    }

    private async Task<SessionCounts> TryGetFromSqlAsync()
    {
        if (string.IsNullOrEmpty(_options.BCInstance.SqlConnectionString))
        {
            return SessionCounts.Empty("sql", "No SQL connection string configured");
        }

        try
        {
            // Determine which databases to query
            IEnumerable<string?> databasesToQuery = _options.BCInstance.TenantDatabases != null && _options.BCInstance.TenantDatabases.Count > 0
                ? _options.BCInstance.TenantDatabases
                : new List<string?> { null }; // null means use connection string as-is (single database)

            // Query all tenant databases in parallel
            var tasks = databasesToQuery.Select(tenantDb => QueryTenantDatabaseAsync(tenantDb)).ToList();
            var results = await Task.WhenAll(tasks);

            // Aggregate results from all tenants
            var webClient = results.Sum(r => r.webClient);
            var webService = results.Sum(r => r.webService);
            var background = results.Sum(r => r.background);

            return SessionCounts.FromSql(webClient, webService, background);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get session counts from SQL");
            return SessionCounts.Empty("sql", ex.Message);
        }
    }

    private async Task<(int webClient, int webService, int background)> QueryTenantDatabaseAsync(string? tenantDb)
    {
        var webClient = 0;
        var webService = 0;
        var background = 0;

        var connString = tenantDb != null
            ? _options.BCInstance.SqlConnectionString.Replace("{database}", tenantDb, StringComparison.OrdinalIgnoreCase)
            : _options.BCInstance.SqlConnectionString;

        await using var connection = new SqlConnection(connString);
        await connection.OpenAsync();

        _logger.LogDebug("Querying Active Session table in database: {Database}",
            tenantDb ?? connection.Database);

        // Query active sessions grouped by client type
        const string query = @"
            SELECT [Client Type], COUNT(*) as SessionCount
            FROM [dbo].[Active Session]
            GROUP BY [Client Type]";

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            // Client Type is int in standard BC/NAV schema
            // The column type is 'int' and uses the ClientType enum values
            var clientTypeValue = reader.GetValue(0);
            var count = reader.GetInt32(1);

            // BC/NAV ClientType enum mapping:
            // 0=Windows, 1=SharePoint, 2=Web, 3=SOAP, 4=OData, 5=ODataV4,
            // 6=Background, 7=NAS, 8=Tablet, 9=Phone, 10=Desktop, 11=Management, 12=API
            var isWebClient = clientTypeValue switch
            {
                0 => true,  // Windows (legacy NAV client)
                2 => true,  // Web (browser-based)
                8 => true,  // Tablet
                9 => true,  // Phone
                10 => true, // Desktop
                "WebClient" => true,   // String fallback (custom BC versions)
                "Windows" => true,
                "Web" => true,
                "Tablet" => true,
                "Phone" => true,
                "Desktop" => true,
                _ => false
            };

            var isWebService = clientTypeValue switch
            {
                3 => true,  // SOAP
                4 => true,  // OData
                5 => true,  // ODataV4
                12 => true, // API
                "SOAP" => true,        // String fallback
                "OData" => true,
                "ODataV3" => true,
                "ODataV4" => true,
                "API" => true,
                "WebService" => true,
                _ => false
            };

            var isBackground = clientTypeValue switch
            {
                6 => true,  // Background
                7 => true,  // NAS (NAV Application Server)
                11 => true, // Management
                "Background" => true,  // String fallback
                "NAS" => true,
                "Management" => true,
                _ => false
            };

            if (isWebClient)
            {
                webClient += count;
            }
            else if (isWebService)
            {
                webService += count;
            }
            else if (isBackground)
            {
                background += count;
            }
            else
            {
                // Unknown type - log it and count as web client for safety
                _logger.LogWarning("Unknown client type in Active Session table: {ClientType} (Type: {TypeName}), counting as WebClient",
                    clientTypeValue, clientTypeValue?.GetType().Name);
                webClient += count;
            }
        }

        return (webClient, webService, background);
    }

    private async Task<SessionCounts> TryGetFromApiAsync()
    {
        try
        {
            var url = $"{_options.BCInstance.BaseUrl}/api/microsoft/runtime/v1.0/sessions";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                return SessionCounts.Empty("api", $"API returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            var webClient = 0;
            var webService = 0;
            var background = 0;

            if (json.RootElement.TryGetProperty("value", out var sessions))
            {
                foreach (var session in sessions.EnumerateArray())
                {
                    if (session.TryGetProperty("clientType", out var clientTypeProp))
                    {
                        var clientType = clientTypeProp.GetString();
                        switch (clientType)
                        {
                            case "WebClient":
                                webClient++;
                                break;
                            case "WebService":
                                webService++;
                                break;
                            case "Background":
                                background++;
                                break;
                        }
                    }
                }
            }

            return SessionCounts.FromApi(webClient, webService, background);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get session counts from BC API");
            return SessionCounts.Empty("api", ex.Message);
        }
    }

    private Task<SessionCounts> TryGetFromPerfCounterAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(SessionCounts.Empty("perfcounter", "Performance counters only available on Windows"));
        }

        try
        {
            // Try instance-specific category first (most common pattern)
            var categoryName = $"Microsoft Dynamics 365 Business Central: {_instanceName}";
            string? instanceParameter = null;

            if (!PerformanceCounterCategory.Exists(categoryName))
            {
                // Fallback to generic category (older BC versions or different installation)
                categoryName = "Microsoft Dynamics 365 Business Central";

                if (!PerformanceCounterCategory.Exists(categoryName))
                {
                    _logger.LogDebug(
                        "BC performance counter category not found. Tried: " +
                        "'Microsoft Dynamics 365 Business Central: {InstanceName}' and " +
                        "'Microsoft Dynamics 365 Business Central'",
                        _instanceName);
                    return Task.FromResult(SessionCounts.Empty("perfcounter",
                        "Performance counter category not found"));
                }

                // For generic category, need to specify instance name as parameter
                instanceParameter = _instanceName;
            }

            // BC uses "# Active Sessions" counter (not "# Open Sessions")
            using var counter = new PerformanceCounter(
                categoryName,
                "# Active Sessions",
                instanceParameter ?? "",
                readOnly: true);

            var total = (int)counter.NextValue();

            _logger.LogDebug(
                "Retrieved {Total} sessions from performance counter category '{Category}'{Instance}",
                total,
                categoryName,
                instanceParameter != null ? $" (instance: {instanceParameter})" : "");

            return Task.FromResult(SessionCounts.FromPerfCounter(total));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get session counts from performance counters");
            return Task.FromResult(SessionCounts.Empty("perfcounter", ex.Message));
        }
    }
}
