using System;
using System.IO;
using NLog;
using WorkflowBAP.Sage;

namespace WorkflowBAP
{
    public sealed class WorkflowProcessor
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private readonly WorkflowSettings _settings;

        public WorkflowProcessor(WorkflowSettings settings)
        {
            _settings = settings
                ?? throw new ArgumentNullException(nameof(settings));
        }

        public WorkflowRunResult Run()
        {
            WorkflowRunResult result =
                new WorkflowRunResult();

            EnsureDirectories();

            Logger.Info(
                "Début du traitement. Source : {0}",
                _settings.SourceDirectory);

            ProcessWorkingDirectory(result);
            ImportSourceFiles(result);
            ProcessWorkingDirectory(result);

            return result;
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(
                _settings.WorkingDirectory);

            Directory.CreateDirectory(
                _settings.ProcessedDirectory);

            Directory.CreateDirectory(
                _settings.ErrorDirectory);

            if (!Directory.Exists(_settings.SourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Le répertoire source OpenBee est inaccessible : "
                    + _settings.SourceDirectory);
            }
        }

        private void ImportSourceFiles(
            WorkflowRunResult result)
        {
            string[] sourceFiles = Directory.GetFiles(
                _settings.SourceDirectory,
                "*.xml",
                SearchOption.TopDirectoryOnly);

            Logger.Info(
                "{0} fichier(s) XML trouvé(s) dans le répertoire OpenBee.",
                sourceFiles.Length);

            foreach (string sourceFile in sourceFiles)
            {
                try
                {
                    if (!IsFileOldEnough(sourceFile))
                    {
                        result.IgnoredCount++;

                        Logger.Info(
                            "Fichier ignoré car trop récent : {0}",
                            Path.GetFileName(sourceFile));

                        continue;
                    }

                    if (!CanOpenExclusively(sourceFile))
                    {
                        result.IgnoredCount++;

                        Logger.Warn(
                            "Fichier encore utilisé par OpenBee : {0}",
                            Path.GetFileName(sourceFile));

                        continue;
                    }

                    CopyAndRemoveSourceFile(sourceFile);
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;

                    Logger.Error(
                        ex,
                        "Impossible de récupérer le fichier source {0}",
                        sourceFile);
                }
            }
        }

        private void CopyAndRemoveSourceFile(
            string sourceFile)
        {
            string fileName =
                Path.GetFileName(sourceFile);

            string processingFile =
                sourceFile + ".processing";

            File.Move(
                sourceFile,
                processingFile);

            try
            {
                string localFile =
                    GetUniqueFilePath(
                        _settings.WorkingDirectory,
                        fileName);

                string temporaryLocalFile =
                    localFile + ".tmp";

                File.Copy(
                    processingFile,
                    temporaryLocalFile,
                    false);

                File.Move(
                    temporaryLocalFile,
                    localFile);

                File.Delete(processingFile);

                Logger.Info(
                    "Fichier récupéré depuis OpenBee : {0}",
                    fileName);
            }
            catch
            {
                TryRestoreSourceFile(
                    sourceFile,
                    processingFile);

                throw;
            }
        }

        private static void TryRestoreSourceFile(
            string sourceFile,
            string processingFile)
        {
            try
            {
                if (File.Exists(processingFile) &&
                    !File.Exists(sourceFile))
                {
                    File.Move(
                        processingFile,
                        sourceFile);
                }
            }
            catch
            {
                // L'erreur initiale reste prioritaire.
            }
        }

        private void ProcessWorkingDirectory(
            WorkflowRunResult result)
        {
            string[] localFiles = Directory.GetFiles(
                _settings.WorkingDirectory,
                "*.xml",
                SearchOption.TopDirectoryOnly);

            foreach (string localFile in localFiles)
            {
                ProcessOneFile(
                    localFile,
                    result);
            }
        }

        private void ProcessOneFile(
            string localFile,
            WorkflowRunResult runResult)
        {
            string fileName =
                Path.GetFileName(localFile);

            OpenBeeDocument document = null;

            try
            {
                Logger.Info(
                    "Début du traitement du fichier {0}",
                    fileName);

                document =
                    OpenBeeXmlReader.Read(localFile);

                Logger.Info(
                    "XML lu : ID={0}, dossier={1}, facture={2}, "
                    + "TTC={3:F2}, sens={4}, BAP={5}",
                    document.DocumentId,
                    document.Dossier,
                    document.NumeroFacture,
                    document.TotalTtc,
                    document.Sens,
                    document.BonAPayer);

                string companyFile =
                    _settings.GetCompanyFile(
                        document.Dossier);

                if (!File.Exists(companyFile))
                {
                    throw new FileNotFoundException(
                        "Le fichier société Sage est introuvable.",
                        companyFile);
                }

                bool estBonAPayer =
                    string.Equals(
                        document.BonAPayer,
                        "Oui",
                        StringComparison.OrdinalIgnoreCase);

                InvoiceUpdateResult updateResult;

                using (SageConnection sage =
                       new SageConnection())
                {
                    sage.Open(
                        companyFile,
                        _settings.SageUser,
                        _settings.SagePassword);

                    SageInvoiceService service =
                        new SageInvoiceService(
                            sage.Application);

                    updateResult =
                        service.UpdateBonAPayer(
                            document.NumeroFacture,
                            document.TotalTtc,
                            document.Sens,
                            estBonAPayer,
                            document.RaisonRefus);
                }

                if (!updateResult.FactureTrouvee)
                {
                    throw new InvalidOperationException(
                        "Aucune écriture Sage trouvée pour la facture "
                        + document.NumeroFacture
                        + " dans la société "
                        + document.Dossier
                        + ".");
                }

                string destinationFile =
                    MoveToDirectory(
                        localFile,
                        _settings.ProcessedDirectory);

                runResult.SuccessCount++;

                Logger.Info(
                    "Traitement réussi : fichier={0}, dossier={1}, facture={2}, "
                    + "TTC={3:F2}, sens={4}, écritures trouvées={5}, "
                    + "écritures modifiées={6}, destination={7}",
                    fileName,
                    document.Dossier,
                    document.NumeroFacture,
                    document.TotalTtc,
                    document.Sens,
                    updateResult.NombreEcrituresTrouvees,
                    updateResult.NombreEcrituresModifiees,
                    destinationFile);
            }
            catch (Exception ex)
            {
                runResult.ErrorCount++;

                Logger.Error(
                    ex,
                    "Échec du traitement : fichier={0}, dossier={1}, facture={2}",
                    fileName,
                    document?.Dossier ?? "inconnu",
                    document?.NumeroFacture ?? "inconnue");

                TryMoveToErrorDirectory(
                    localFile,
                    document,
                    ex);
            }
        }

        private void TryMoveToErrorDirectory(
            string localFile,
            OpenBeeDocument document,
            Exception exception)
        {
            try
            {
                if (!File.Exists(localFile))
                    return;

                string destinationFile =
                    MoveToDirectory(
                        localFile,
                        _settings.ErrorDirectory);

                string errorFile =
                    destinationFile + ".error.txt";

                string content =
                    "Date : "
                    + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                    + Environment.NewLine
                    + "Fichier : "
                    + Path.GetFileName(destinationFile)
                    + Environment.NewLine
                    + "ID OpenBee : "
                    + (document?.DocumentId ?? "inconnu")
                    + Environment.NewLine
                    + "Dossier : "
                    + (document?.Dossier ?? "inconnu")
                    + Environment.NewLine
                    + "Facture : "
                    + (document?.NumeroFacture ?? "inconnue")
                    + Environment.NewLine
                    + "Total TTC : "
                    + (document == null
                        ? "inconnu"
                        : document.TotalTtc.ToString(
                            "F2",
                            System.Globalization.CultureInfo.GetCultureInfo("fr-FR")))
                    + Environment.NewLine
                    + "Sens : "
                    + (document?.Sens ?? "inconnu")
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Erreur :"
                    + Environment.NewLine
                    + exception;

                File.WriteAllText(
                    errorFile,
                    content);

                Logger.Info(
                    "Fichier déplacé vers ERREURS : {0}",
                    destinationFile);
            }
            catch (Exception moveException)
            {
                Logger.Fatal(
                    moveException,
                    "Impossible de déplacer le fichier en erreur : {0}",
                    localFile);
            }
        }

        private bool IsFileOldEnough(
            string filePath)
        {
            DateTime lastWrite =
                File.GetLastWriteTime(filePath);

            return (
                DateTime.Now - lastWrite
            ).TotalSeconds >=
                   _settings.MinimumFileAgeSeconds;
        }

        private static bool CanOpenExclusively(
            string filePath)
        {
            try
            {
                using (FileStream stream =
                       new FileStream(
                           filePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.None))
                {
                    return stream.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string MoveToDirectory(
            string sourceFile,
            string destinationDirectory)
        {
            string destinationFile =
                GetUniqueFilePath(
                    destinationDirectory,
                    Path.GetFileName(sourceFile));

            File.Move(
                sourceFile,
                destinationFile);

            return destinationFile;
        }

        private static string GetUniqueFilePath(
            string directory,
            string fileName)
        {
            string destination =
                Path.Combine(
                    directory,
                    fileName);

            if (!File.Exists(destination))
                return destination;

            string name =
                Path.GetFileNameWithoutExtension(fileName);

            string extension =
                Path.GetExtension(fileName);

            return Path.Combine(
                directory,
                name
                + "_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                + extension);
        }
    }
}