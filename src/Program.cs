namespace MHUpkManager
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    WriteStartupCrashLog(ex);
            };

            Application.ThreadException += (_, args) => WriteStartupCrashLog(args.Exception);

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                WriteStartupCrashLog(ex);
                throw;
            }
        }

        private static void WriteStartupCrashLog(Exception ex)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "MHUpkManager_StartupLogs");
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.WriteAllText(path, ex.ToString());
            }
            catch
            {
            }
        }
    }
}
