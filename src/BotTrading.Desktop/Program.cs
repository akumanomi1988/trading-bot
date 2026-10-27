using BotTrading.Application;

namespace BotTrading.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, "Local\\BotTrading-QuantWorkstation-SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("BotTrading is already running in this Windows session.", "BotTrading", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        try
        {
            using var workstation = new Workstation();
            Application.Run(new WorkstationForm(workstation));
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Unable to start the workstation.\n\n{exception.Message}", "BotTrading",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
