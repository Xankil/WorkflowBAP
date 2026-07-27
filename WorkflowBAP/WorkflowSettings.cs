using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WorkflowBAP
{
    public sealed class WorkflowSettings
    {
        public string SourceDirectory { get; set; }
        public string WorkingDirectory { get; set; }
        public string ProcessedDirectory { get; set; }
        public string ErrorDirectory { get; set; }

        public string SageUser { get; set; }
        public string SagePassword { get; set; }

        public int MinimumFileAgeSeconds { get; set; } = 60;

        public Dictionary<string, string> CompanyMappings { get; set; }

        public static WorkflowSettings Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Le fichier config.json est introuvable.",
                    filePath);
            }

            string json = File.ReadAllText(filePath);

            WorkflowSettings settings =
                JsonConvert.DeserializeObject<WorkflowSettings>(json);

            if (settings == null)
            {
                throw new InvalidOperationException(
                    "Le fichier config.json est invalide.");
            }

            settings.Validate();

            return settings;
        }

        public string GetCompanyFile(string dossier)
        {
            if (string.IsNullOrWhiteSpace(dossier))
            {
                throw new InvalidOperationException(
                    "Le champ Dossier est vide.");
            }

            if (CompanyMappings == null)
            {
                throw new InvalidOperationException(
                    "Aucune correspondance société n'est configurée.");
            }

            foreach (KeyValuePair<string, string> mapping in CompanyMappings)
            {
                if (string.Equals(
                    mapping.Key.Trim(),
                    dossier.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Value;
                }
            }

            throw new KeyNotFoundException(
                "Aucune correspondance Sage définie pour : " + dossier);
        }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceDirectory))
                throw new InvalidOperationException(
                    "SourceDirectory est obligatoire.");

            if (string.IsNullOrWhiteSpace(WorkingDirectory))
                throw new InvalidOperationException(
                    "WorkingDirectory est obligatoire.");

            if (string.IsNullOrWhiteSpace(ProcessedDirectory))
                throw new InvalidOperationException(
                    "ProcessedDirectory est obligatoire.");

            if (string.IsNullOrWhiteSpace(ErrorDirectory))
                throw new InvalidOperationException(
                    "ErrorDirectory est obligatoire.");

            if (CompanyMappings == null || CompanyMappings.Count == 0)
                throw new InvalidOperationException(
                    "CompanyMappings doit contenir au moins une société.");
        }
    }
}