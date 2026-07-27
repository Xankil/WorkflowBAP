using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace WorkflowBAP
{
    public static class OpenBeeXmlReader
    {
        public static OpenBeeDocument Read(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Le fichier XML est introuvable.",
                    filePath);
            }

            XDocument xml = XDocument.Load(filePath);

            XElement root = xml.Element("document");

            if (root == null)
            {
                throw new InvalidOperationException(
                    "La balise racine document est absente.");
            }

            OpenBeeDocument document = new OpenBeeDocument
            {
                DocumentId = GetElementValue(root, "id"),
                Dossier = GetIndexValue(root, "Dossier"),
                NumeroFacture = GetIndexValue(
                    root,
                    "Numéro de facture"),
                BonAPayer = GetIndexValue(
                    root,
                    "Bon à payer"),
                RaisonRefus = GetIndexValue(
                    root,
                    "Raison refus")
            };

            Validate(document);

            return document;
        }

        private static string GetElementValue(
            XElement root,
            string elementName)
        {
            return (root.Element(elementName)?.Value ?? string.Empty)
                .Trim();
        }

        private static string GetIndexValue(
            XElement root,
            string indexName)
        {
            XElement indexes = root.Element("indexes");

            if (indexes == null)
                return string.Empty;

            XElement item = indexes
                .Elements("item")
                .FirstOrDefault(element =>
                    string.Equals(
                        (string)element.Attribute("name"),
                        indexName,
                        StringComparison.OrdinalIgnoreCase));

            return (item?.Value ?? string.Empty).Trim();
        }

        private static void Validate(OpenBeeDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.Dossier))
            {
                throw new InvalidOperationException(
                    "Le champ Dossier est absent du XML.");
            }

            if (string.IsNullOrWhiteSpace(document.NumeroFacture))
            {
                throw new InvalidOperationException(
                    "Le champ Numéro de facture est absent du XML.");
            }

            bool estOui = string.Equals(
                document.BonAPayer,
                "Oui",
                StringComparison.OrdinalIgnoreCase);

            bool estNon = string.Equals(
                document.BonAPayer,
                "Non",
                StringComparison.OrdinalIgnoreCase);

            if (!estOui && !estNon)
            {
                throw new InvalidOperationException(
                    "Le champ Bon à payer doit contenir Oui ou Non. "
                    + "Valeur reçue : "
                    + document.BonAPayer);
            }

            if (estNon &&
                string.IsNullOrWhiteSpace(document.RaisonRefus))
            {
                throw new InvalidOperationException(
                    "La raison du refus est obligatoire.");
            }
        }
    }
}