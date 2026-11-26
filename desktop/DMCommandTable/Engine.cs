using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace DMCommandTable;

internal sealed class Engine : IDisposable
{
    private Process? process;
    private HttpClient? client;
    public string BootstrapToken { get; private set; } = "";
    public const long CampaignLimit = 50 * 1024 * 1024;

    public async Task StartAsync()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "engine");
        var executable = Path.Combine(folder, "node.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("The bundled engine is missing. Extract the complete desktop package.");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        info.ArgumentList.Add(Path.Combine(folder, "launch.mjs"));
        info.ArgumentList.Add("--desktop");
        info.ArgumentList.Add("--no-browser");
        info.Environment["DM_COMMAND_TABLE_DESKTOP_PORT"] = port.ToString();
        var bootstrap = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            const string prefix = "First-time registration token: ";
            if (args.Data?.StartsWith(prefix, StringComparison.Ordinal) == true)
                bootstrap.TrySetResult(args.Data[prefix.Length..]);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) Debug.WriteLine(args.Data);
        };
        process.Exited += (_, _) => bootstrap.TrySetException(new InvalidOperationException("The local engine exited during startup."));
        if (!process.Start()) throw new InvalidOperationException("Could not start the local engine.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        BootstrapToken = await bootstrap.Task.WaitAsync(TimeSpan.FromSeconds(45));
        client = new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer()
        })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(75)
        };
    }

    public async Task<HttpResponseMessage> SendAsync(string method, string route, HttpContent? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = body };
        var response = await (client ?? throw new InvalidOperationException("The local engine is unavailable."))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            string? message;
            try { message = JsonNode.Parse(text)?["error"]?.GetValue<string>(); }
            catch (System.Text.Json.JsonException) { message = null; }
            throw new InvalidOperationException(message ?? $"The local engine returned HTTP {(int)response.StatusCode}.");
        }
    }

    public async Task<JsonObject> JsonAsync(string method, string route, JsonObject? body = null)
    {
        using var response = await SendAsync(method, route, body is null ? null : JsonContent.Create(body));
        return (JsonObject?)await response.Content.ReadFromJsonAsync<JsonNode>() ?? new JsonObject();
    }

    public void Dispose()
    {
        client?.Dispose();
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(2500)) process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}
