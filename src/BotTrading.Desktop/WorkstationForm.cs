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
    private readonly bool _smokeTest;
    private bool _smokeAppInfoReceived;
    private bool _smokeAssetsReceived;
    private bool _smokePaperReceived;
    private bool _smokeBlockedExternalNavigation;

    public WorkstationForm(Workstation app, bool smokeTest = false)
    {
        _app = app;
        _smokeTest = smokeTest;
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
            string userData = _smokeTest
                ? Path.Combine(Path.GetTempPath(), $"BotTrading-CI-WebView-{Environment.ProcessId}")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
                if (!safe)
                {
                    e.Cancel = true;
                    if (_smokeTest) _smokeBlockedExternalNavigation = true;
                }
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
            if (_smokeTest) _ = RunWindowsSmokeAsync();
        }
        catch (Exception ex)
        {
            if (_smokeTest)
            {
                Environment.ExitCode = 1;
                System.Diagnostics.Trace.WriteLine(ex);
            }
            else MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "BotTrading", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task RunWindowsSmokeAsync()
    {
        try
        {
            // Assert real WinForms -> WebView2 -> bundled React -> native bridge roundtrip.
            // A compiled bundle alone does not satisfy this check.
            const string script = "(function() { return location.origin === 'https://bottrading.local' && " +
                "Boolean(document.querySelector('.data-toolbar')) && " +
                "Boolean(document.querySelector('.dual-cards')) && " +
                "document.body.textContent.includes('SIM.A') && " +
                "Boolean(document.querySelector('h1')?.textContent?.includes('Explorador de mercado')) && " +
                "!document.querySelector('.error-banner'); })()";
            bool ready = false;
            for (int attempt = 0; attempt < 80; attempt++)
            {
                await Task.Delay(250);
                if (_browser.CoreWebView2 is null || IsDisposed) break;
                string response = await _browser.CoreWebView2.ExecuteScriptAsync(script);
                if (response == "true" && _smokeAppInfoReceived && _smokeAssetsReceived)
                {
                    ready = true;
                    break;
                }
            }
            if (!ready) throw new InvalidOperationException("React or the WebView2 bridge failed to initialize before the smoke timeout.");
            var core = _browser.CoreWebView2 ??
                throw new InvalidOperationException("WebView2 was disposed before screenshot capture.");
            async Task WaitForScreenAsync(string js, string screen, Func<bool>? nativeGate = null)
            {
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    await Task.Delay(200);
                    if (await core.ExecuteScriptAsync(js) == "true" && (nativeGate is null || nativeGate()))
                        return;
                }
                throw new InvalidOperationException($"Windows smoke did not reach expected {screen} screen.");
            }

            await core.ExecuteScriptAsync("document.querySelector('button[title=\\\"Laboratorio de estrategias\\\"]')?.click();");
            await WaitForScreenAsync(
                "Boolean(document.querySelector('select[aria-label=\\\"Tipo de media de estrategia\\\"]')) && " +
                "document.querySelector('h1')?.textContent?.includes('Laboratorio de estrategias')",
                "strategy lab");
            await core.ExecuteScriptAsync(
                "(function(){const s=document.querySelector('select[aria-label=\\\"Tipo de media de estrategia\\\"]');" +
                "s.value='EMA';s.dispatchEvent(new Event('change',{bubbles:true}));})()");
            await WaitForScreenAsync(
                "document.querySelector('select[aria-label=\\\"Tipo de media de estrategia\\\"]')?.value === 'EMA'",
                "EMA selection");
            await core.ExecuteScriptAsync(
                "Array.from(document.querySelectorAll('button')).find(b=>b.textContent?.includes('Ejecutar backtest'))?.click();");
            await WaitForScreenAsync(
                "Boolean(document.querySelector('.results-panel .kpi-grid')) && " +
                "document.querySelector('.results-panel')?.textContent?.includes('EMA') && " +
                "document.querySelector('.results-panel')?.textContent?.includes('CAPITAL FINAL') && " +
                "!document.querySelector('.error-banner')",
                "EMA backtest on synthetic 420-candle history");
            await core.ExecuteScriptAsync("document.querySelector('button[title=\\\"Paper Trading\\\"]')?.click();");
            await WaitForScreenAsync(
                "document.querySelector('h1')?.textContent?.includes('Cartera simulada') && " +
                "Boolean(document.querySelector('.positions')) && !document.querySelector('.error-banner')",
                "paper portfolio", () => _smokePaperReceived);
            await core.ExecuteScriptAsync(
                "Array.from(document.querySelectorAll('button')).find(b=>b.textContent?.includes('Comprar (simulado)'))?.click();");
            await WaitForScreenAsync(
                "document.querySelector('.positions')?.textContent?.includes('SIM.A') && " +
                "document.querySelector('.positions')?.textContent?.includes('1 uds.') && " +
                "!document.querySelector('.error-banner')",
                "simulated buy and portfolio display");
            var positions = _app.PaperSnapshot().Positions;
            if (positions.Count != 1 || positions[0].Quantity != 1 || positions[0].Symbol != "SIM.A")
                throw new InvalidOperationException("Native simulated portfolio did not persist the UI buy.");
            // The navigation is deliberately denied by the native host before a network request.
            // An untrusted page must never become a bridge-capable app origin.
            await core.ExecuteScriptAsync("window.location.assign('https://outside-origin.invalid/');");
            await WaitForScreenAsync(
                "location.origin === 'https://bottrading.local' && " +
                "document.querySelector('h1')?.textContent?.includes('Cartera simulada')",
                "blocked cross-origin navigation", () => _smokeBlockedExternalNavigation);

            var screenshot = Path.Combine(AppContext.BaseDirectory, "webview2-smoke.png");
            await using (var output = File.Create(screenshot))
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
            if (new FileInfo(screenshot).Length < 1_000)
                throw new InvalidDataException("WebView2 produced an invalid smoke screenshot.");
            System.Diagnostics.Trace.WriteLine("BotTrading Windows WebView2 smoke PASSED.");
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"BotTrading smoke FAILED: {ex}");
            Environment.ExitCode = 1;
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(Close));
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
            if (_smokeTest && action == "app.info") _smokeAppInfoReceived = true;
            if (_smokeTest && action == "assets.list") _smokeAssetsReceived = true;
            if (_smokeTest && action == "paper.snapshot") _smokePaperReceived = true;
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
        decimal Cost(string key) => args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal() : 5m;
        decimal AllocationPct() => args.TryGetProperty("maxAllocationPct", out var value) &&
            value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 100m;
        string StrategyKind()
        {
            string? kind = args.TryGetProperty("strategyKind", out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString() : "SMA";
            return kind is "SMA" or "EMA" ? kind : throw new ArgumentException("Unknown strategy kind.");
        }
        return action switch
        {
            "app.info" => new { version = "0.1.0", mode = "offline-first", execution = "simulation-only", source = "WebView2 bridge" },
            "assets.list" => _app.ListSeries(),
            "experiments.list" => _app.ListExperiments(),
            "experiments.compare" => _app.CompareExperiments(Str("leftId"), Str("rightId")),
            "experiments.save" => await _app.SaveExperimentAsync(Str("symbol"), Int("fastPeriod"), Int("slowPeriod"), StrategyKind(), Cost("feeBps"), Cost("slippageBps"), AllocationPct()),
            "experiments.delete" => await DeleteExperimentAsync(Str("id")),
            "workspace.exportBackup" => await ExportBackupWithDialogAsync(),
            "workspace.restoreBackup" => await RestoreBackupWithDialogAsync(),
            "assets.series" => _app.GetSeries(Str("symbol")),
            "assets.quality" => await Task.Run(() => _app.AnalyzeQuality(Str("symbol"),
                args.TryGetProperty("endDate", out var qualityDate) && qualityDate.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(qualityDate.GetString()!) : null)),
            "assets.importCsv" => await ImportWithDialogAsync(Str("symbol")),
            "assets.download" => await _app.DownloadAsync(Str("symbol")),
            "news.importCsv" => await ImportNewsWithDialogAsync(),
            "news.list" => _app.NewsSnapshot(Str("symbol"),
                args.TryGetProperty("endDate", out var newsDate) && newsDate.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(newsDate.GetString()!) : null),
            "patterns.find" => await Task.Run(() => _app.FindPatterns(Str("symbol"), Int("window"), Int("maxResults"),
                args.TryGetProperty("endDate", out var endDate) && endDate.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(endDate.GetString()!) : null,
                args.TryGetProperty("targetSymbol", out var target) && target.ValueKind == JsonValueKind.String ?
                    target.GetString() : null)),
            "backtest.run" => _app.Backtest(Str("symbol"), Int("fastPeriod"), Int("slowPeriod"), strategyKind: StrategyKind(),
                feeBps: Cost("feeBps"), slippageBps: Cost("slippageBps"), maxAllocationPct: AllocationPct()),
            "backtest.optimize" => await Task.Run(() => _app.Optimize(Str("symbol"), StrategyKind(), Cost("feeBps"), Cost("slippageBps"), AllocationPct())),
            "simulation.dca" => await Task.Run(() => _app.SimulateDca(Str("symbol"),
                args.TryGetProperty("monthlyContribution", out var contribution) && contribution.ValueKind == JsonValueKind.Number ?
                    contribution.GetDecimal() : 100m,
                args.TryGetProperty("startDate", out var startDate) && startDate.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(startDate.GetString()!) : null,
                args.TryGetProperty("endDate", out var endDca) && endDca.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(endDca.GetString()!) : null)),
            "strategy.validate" => await Task.Run(() => _app.ValidateOutOfSample(Str("symbol"), StrategyKind(), Cost("feeBps"), Cost("slippageBps"), AllocationPct())),
            "strategy.costSensitivity" => await Task.Run(() => _app.AnalyzeCostSensitivity(
                Str("symbol"), Int("fastPeriod"), Int("slowPeriod"), StrategyKind(), AllocationPct())),
            "strategy.walkForward" => await Task.Run(() => _app.ValidateWalkForward(Str("symbol"), StrategyKind(), Cost("feeBps"), Cost("slippageBps"), AllocationPct())),
            "replay.snapshot" => _app.ReplaySnapshot(),
            "replay.clear" => await ClearReplayAsync(),
            "replay.start" => await _app.StartReplayAsync(Str("symbol"), DateOnly.Parse(Str("startDate"))),
            "replay.submit" => await _app.SubmitReplayOrderAsync(Str("side"), Int("quantity"),
                args.TryGetProperty("orderType", out var orderType) && orderType.ValueKind == JsonValueKind.String ?
                    orderType.GetString() ?? "MARKET" : "MARKET",
                args.TryGetProperty("triggerPrice", out var trigger) && trigger.ValueKind == JsonValueKind.Number ?
                    trigger.GetDecimal() : null,
                args.TryGetProperty("goodThrough", out var goodThrough) && goodThrough.ValueKind == JsonValueKind.String ?
                    DateOnly.Parse(goodThrough.GetString()!) : null),
            "replay.cancel" => await _app.CancelReplayOrderAsync(Int("sequence")),
            "replay.advance" => await _app.AdvanceReplayAsync(Int("steps")),
            "paper.snapshot" => _app.PaperSnapshot(),
            "paper.order" => await _app.PaperOrderAsync(Str("symbol"), Str("side"), Int("quantity")),
            "paper.reset" => await _app.ResetPaperAsync(),
            _ => throw new ArgumentException($"Unknown bridge command '{action}'.")
        };
    }

    private async Task<object?> ClearReplayAsync()
    {
        await _app.ClearReplayAsync();
        return null;
    }

    private async Task<object?> DeleteExperimentAsync(string id)
    {
        await _app.DeleteExperimentAsync(id);
        return null;
    }

    private async Task<object> ExportBackupWithDialogAsync()
    {
        using var picker = new SaveFileDialog
        {
            Title = "Export verified BotTrading workspace backup",
            Filter = "JSON backups (*.json)|*.json",
            FileName = $"BotTrading-backup-{DateTime.Now:yyyy-MM-dd}.json",
            OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return new { cancelled = true };
        await _app.ExportBackupAsync(picker.FileName);
        return new { cancelled = false };
    }

    private async Task<object> RestoreBackupWithDialogAsync()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Restore a BotTrading workspace backup",
            Filter = "JSON backups (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return new { cancelled = true };
        await _app.RestoreBackupAsync(picker.FileName);
        return new { cancelled = false };
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

    private async Task<object> ImportNewsWithDialogAsync()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Import news research CSV",
            Filter = "CSV files (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        return picker.ShowDialog(this) == DialogResult.OK
            ? new { cancelled = false, count = await _app.ImportNewsCsvAsync(picker.FileName) }
            : new { cancelled = true, count = 0 };
    }

    private void Respond(string id, bool ok, object? result, object? error)
    {
        if (_browser.CoreWebView2 is null || IsDisposed) return;
        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, result, error }, _json));
    }
}
