using System.Text.Json;
using BotTrading.Application;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BotTrading.Desktop;

public sealed class WorkstationForm : Form
{
    private const string AppOrigin = "https://bottrading.local";
    private readonly Workstation _app;
    private readonly WebView2 _browser;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public WorkstationForm(Workstation app)
    {
        _app = app;
        Text = "BotTrading · Quant Workstation";
        MinimumSize = new(940, 650);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(12, 17, 29);
        _browser = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_browser);
        Shown += async (_, _) => await InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BotTrading", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _browser.EnsureCoreWebView2Async(environment);
            var core = _browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("BOTTRADING_DEV_URL") is not null;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
            core.NavigationStarting += (_, e) =>
            {
                var url = e.Uri;
                bool dev = Environment.GetEnvironmentVariable("BOTTRADING_DEV_URL") is not null;
                bool safe = Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                    (parsed.GetLeftPart(UriPartial.Authority) == AppOrigin ||
                     (dev && parsed.GetLeftPart(UriPartial.Authority) == "http://127.0.0.1:5173"));
                if (!safe) e.Cancel = true;
            };
            core.WebMessageReceived += OnMessageAsync;
            string? devUrl = Environment.GetEnvironmentVariable("BOTTRADING_DEV_URL");
            if (!string.IsNullOrEmpty(devUrl))
            {
                if (devUrl != "http://127.0.0.1:5173/") throw new InvalidOperationException("Unexpected development origin.");
                core.Navigate(devUrl);
            }
            else
            {
                string bundle = Path.Combine(AppContext.BaseDirectory, "web");
                if (!File.Exists(Path.Combine(bundle, "index.html")))
                    throw new FileNotFoundException("React bundle missing. Run scripts/build.ps1 before starting the desktop app.");
                core.SetVirtualHostNameToFolderMapping("bottrading.local", bundle, CoreWebView2HostResourceAccessKind.DenyCors);
                core.Navigate(AppOrigin + "/index.html");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "BotTrading", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async void OnMessageAsync(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string id = "";
        try
        {
            if (e.Source is null || !Uri.TryCreate(e.Source, UriKind.Absolute, out var source)) return;
            bool dev = Environment.GetEnvironmentVariable("BOTTRADING_DEV_URL") == "http://127.0.0.1:5173/";
            if (source.GetLeftPart(UriPartial.Authority) != AppOrigin &&
                !(dev && source.GetLeftPart(UriPartial.Authority) == "http://127.0.0.1:5173")) return;
            string message = e.WebMessageAsJson;
            if (message.Length > 8192) throw new ArgumentException("Request size exceeds 8 KiB.");
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetString() ?? throw new ArgumentException("Request ID missing.");
            if (id.Length is < 1 or > 64) throw new ArgumentException("Invalid request ID.");
            string action = root.GetProperty("type").GetString() ?? "";
            var args = root.TryGetProperty("payload", out var payload) ? payload : default;
            object? result = await ExecuteAsync(action, args);
            Respond(id, true, result, null);
        }
        catch (Exception ex)
        {
            Respond(id, false, null, new { code = "REQUEST_FAILED", message = ex.Message });
        }
    }

    private async Task<object?> ExecuteAsync(string action, JsonElement args)
    {
        string Str(string key) => args.GetProperty(key).GetString() ?? throw new ArgumentException($"'{key}' is required.");
        int Int(string key) => args.GetProperty(key).GetInt32();
        return action switch
        {
            "app.info" => new { version = "0.1.0", mode = "offline-first", execution = "simulation-only", source = "WebView2 bridge" },
            "assets.list" => _app.ListSeries(),
            "assets.series" => _app.GetSeries(Str("symbol")),
            "assets.importCsv" => await ImportWithDialogAsync(Str("symbol")),
            "assets.download" => await _app.DownloadAsync(Str("symbol")),
            "patterns.find" => await Task.Run(() => _app.FindPatterns(Str("symbol"), Int("window"), Int("maxResults"),
                args.TryGetProperty("endDate", out var endDate) && endDate.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(endDate.GetString()!) : null,
                args.TryGetProperty("targetSymbol", out var target) && target.ValueKind == JsonValueKind.String ?
                    target.GetString() : null)),
            "backtest.run" => _app.Backtest(Str("symbol"), Int("fastPeriod"), Int("slowPeriod")),
            "backtest.optimize" => await Task.Run(() => _app.Optimize(Str("symbol"))),
            "strategy.validate" => await Task.Run(() => _app.ValidateOutOfSample(Str("symbol"))),
            "strategy.walkForward" => await Task.Run(() => _app.ValidateWalkForward(Str("symbol"))),
            "paper.snapshot" => _app.PaperSnapshot(),
            "paper.order" => await _app.PaperOrderAsync(Str("symbol"), Str("side"), Int("quantity")),
            "paper.reset" => await _app.ResetPaperAsync(),
            _ => throw new ArgumentException($"Unknown bridge command '{action}'.")
        };
    }

    private async Task<object> ImportWithDialogAsync(string symbol)
    {
        using var picker = new OpenFileDialog
        {
            Title = "Import daily OHLC CSV",
            Filter = "CSV files (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) == DialogResult.OK)
            return await _app.ImportCsvAsync(picker.FileName, symbol);
        return new { cancelled = true };
    }

    private void Respond(string id, bool ok, object? result, object? error)
    {
        if (_browser.CoreWebView2 is null || IsDisposed) return;
        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, result, error }, _json));
    }
}
