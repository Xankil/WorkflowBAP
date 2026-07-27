using System;
using System.Globalization;
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
                throw new InvalidDataException(
                    "La balise racine document est absente.");
            }

            OpenBeeDocument document = new OpenBeeDocument
            {
                DocumentId = GetElementValue(root, "id"),
                Dossier = GetIndexValue(root, "Dossier"),
                NumeroFacture = GetIndexValue(
                    root,
                    "Numéro de facture"),
                TotalTtc = ParseTotalTtc(
                    GetIndexValue(root, "Total TTC")),
                Sens = NormalizeSens(
                    GetIndexValue(root, "Sens")),
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

        private static decimal ParseTotalTtc(
            string valeur)
        {
            if (string.IsNullOrWhiteSpace(valeur))
            {
                throw new InvalidDataException(
                    "Le champ Total TTC est absent du XML.");
            }

            string valeurNormalisee = valeur
                .Trim()
                .Replace(" ", string.Empty)
                .Replace(" ", string.Empty)
                .Replace(" ", string.Empty);

            decimal montant;

            if (!decimal.TryParse(
                    valeurNormalisee,
                    NumberStyles.Number,
                    CultureInfo.GetCultureInfo("fr-FR"),
                    out montant)
                &&
                !decimal.TryParse(
                    valeurNormalisee,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out montant))
            {
                throw new InvalidDataException(
                    "Le champ Total TTC ne contient pas un montant valide. "
                    + "Valeur reçue : "
                    + valeur);
            }

            montant = Math.Abs(montant);

            if (montant <= 0m)
            {
                throw new InvalidDataException(
                    "Le champ Total TTC doit être strictement positif. "
                    + "Valeur reçue : "
                    + valeur);
            }

            return montant;
        }

        private static string NormalizeSens(
            string valeur)
        {
            if (string.Equals(
                    valeur?.Trim(),
                    "Facture",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Facture";
            }

            if (string.Equals(
                    valeur?.Trim(),
                    "Avoir",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Avoir";
            }

            throw new InvalidDataException(
                "Le champ Sens doit contenir Facture ou Avoir. "
                + "Valeur reçue : "
                + (valeur ?? string.Empty));
        }

        private static void Validate(OpenBeeDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.Dossier))
            {
                throw new InvalidDataException(
                    "Le champ Dossier est absent du XML.");
            }

            if (string.IsNullOrWhiteSpace(document.NumeroFacture))
            {
                throw new InvalidDataException(
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
                throw new InvalidDataException(
                    "Le champ Bon à payer doit contenir Oui ou Non. "
                    + "Valeur reçue : "
                    + document.BonAPayer);
            }

            if (estNon &&
                string.IsNullOrWhiteSpace(document.RaisonRefus))
            {
                throw new InvalidDataException(
                    "La raison du refus est obligatoire.");
            }
        }
    }
}
