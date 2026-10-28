using BotTrading.Application;
using BotTrading.Core;

namespace BotTrading.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool smoke = Environment.GetEnvironmentVariable("BOTTRADING_SMOKE_TEST") == "1";
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, "Local\\BotTrading-QuantWorkstation-SingleInstance", out bool created);
        if (!created)
        {
            if (smoke) { Environment.ExitCode = 1; return; }
            MessageBox.Show("BotTrading is already running in this Windows session.", "BotTrading", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        try
        {
            var store = smoke ? new WorkspaceStore(Path.Combine(Path.GetTempPath(),
                $"BotTrading-CI-Smoke-{Environment.ProcessId}")) : null;
            if (smoke && store is not null)
            {
                // Real deterministic OHLC fixtures, isolated from the user's Windows profile.
                // Exercise actual C# trading and persistence via the real WebView2 UI.
                var dates = Enumerable.Range(0, 420).Select(i => new DateOnly(2024, 1, 2).AddDays(i)).ToArray();
                MarketSeries BuildFixture(string symbol, decimal start, int cycle) =>
                    new(symbol, $"Synthetic CI fixture {symbol}", dates.Select((date, i) =>
                    {
                        var price = start + (i % cycle) + (i / cycle % 2 == 0 ? 0m : -15m);
                        return new Candle(date, price, price + 2m, price - 2m, price, 1000m);
                    }).ToArray());
                store.Save(new([BuildFixture("SIM.A", 100m, 40), BuildFixture("SIM.B", 80m, 27)],
                    new PaperAccount(10_000m, [], [])));
            }
            using var workstation = new Workstation(store);
            System.Windows.Forms.Application.Run(new WorkstationForm(workstation, smoke));
        }
        catch (Exception exception)
        {
            if (smoke)
            {
                Environment.ExitCode = 1;
                System.Diagnostics.Trace.WriteLine(exception);
                return;
            }
            MessageBox.Show($"Unable to start the workstation.\n\n{exception.Message}", "BotTrading",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
