using BotTrading.Application;

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
