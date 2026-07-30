using System;
using System.IO;
using NLog;

namespace WorkflowBAP
{
    internal static class Program
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private static int Main()
        {
            try
            {
                InitialiserRepertoireLogs();
                ConfigurerGestionErreursGlobales();

                Version version =
                    typeof(Program).Assembly
                        .GetName()
                        .Version;

                Logger.Info(
                    "Démarrage WorkflowBAP V{0}",
                    version.ToString(2));

                string applicationDirectory =
                    AppDomain.CurrentDomain.BaseDirectory;

                string configFile = Path.Combine(
                    applicationDirectory,
                    "Config",
                    "config.json");

                WorkflowSettings settings =
                    WorkflowSettings.Load(configFile);

                WorkflowProcessor processor =
                    new WorkflowProcessor(settings);

                WorkflowRunResult result =
                    processor.Run();

                Logger.Info(
                    "Fin du traitement : {0} succès, {1} erreur(s), {2} fichier(s) ignoré(s)",
                    result.SuccessCount,
                    result.ErrorCount,
                    result.IgnoredCount);

                return result.ErrorCount == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                Logger.Fatal(
                    ex,
                    "Erreur générale non récupérable dans WorkflowBAP");

                WriteEmergencyLog(ex);

                return 1;
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        private static void InitialiserRepertoireLogs()
        {
            string logsDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Logs");

            Directory.CreateDirectory(logsDirectory);
        }

        private static void ConfigurerGestionErreursGlobales()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, args) =>
                {
                    Exception exception =
                        args.ExceptionObject as Exception;

                    Logger.Fatal(
                        exception,
                        "Exception globale non interceptée");

                    if (exception != null)
                        WriteEmergencyLog(exception);
                };
        }

        private static void WriteEmergencyLog(Exception exception)
        {
            try
            {
                string filePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Logs",
                    "emergency.log");

                File.AppendAllText(
                    filePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + Environment.NewLine
                    + exception
                    + Environment.NewLine
                    + new string('-', 80)
                    + Environment.NewLine);
            }
            catch
            {
                // Dernier niveau de secours :
                // aucune exception ne doit ressortir d'ici.
            }
        }
    }
}
