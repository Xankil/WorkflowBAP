using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Objets100cLib;

namespace WorkflowBAP.Sage
{
    public sealed class SageInvoiceService
    {
        private const string NomInfoLibreBonAPayer = "Bon à payer";
        private const string NomInfoLibreRaisonRefus = "Raison refus";
        private const string NomInfoLibreIdDms = "idDMS";

        private const int LongueurNumeroFactureSage = 17;
        private const int LongueurBonAPayer = 3;
        private const int LongueurRaisonRefus = 69;
        private const int LongueurIdDms = 15;

        private const double ToleranceMontant = 0.01d;

        private static readonly NLog.Logger Logger =
            NLog.LogManager.GetCurrentClassLogger();

        private readonly BSCPTAApplication100c _application;

        public SageInvoiceService(BSCPTAApplication100c application)
        {
            _application = application
                ?? throw new ArgumentNullException(nameof(application));

            if (!_application.IsOpen)
            {
                throw new InvalidOperationException(
                    "La société Sage doit être ouverte avant d'utiliser le service.");
            }
        }

        public InvoiceUpdateResult UpdateBonAPayer(
            string documentId,
            string numeroFacture,
            string fournisseur,
            decimal totalTtc,
            string sens,
            bool estBonAPayer,
            string raisonRefus)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                throw new ArgumentException(
                    "L'ID OpenBee est obligatoire.",
                    nameof(documentId));
            }

            if (documentId.Trim().Length > LongueurIdDms)
            {
                throw new ArgumentException(
                    "L'ID OpenBee dépasse les "
                    + LongueurIdDms
                    + " caractères disponibles dans l'information libre Sage "
                    + "'"
                    + NomInfoLibreIdDms
                    + "'. Valeur reçue : "
                    + documentId.Trim(),
                    nameof(documentId));
            }

            if (string.IsNullOrWhiteSpace(numeroFacture))
            {
                throw new ArgumentException(
                    "Le numéro de facture est obligatoire.",
                    nameof(numeroFacture));
            }

            if (string.IsNullOrWhiteSpace(fournisseur))
            {
                throw new ArgumentException(
                    "Le fournisseur est obligatoire.",
                    nameof(fournisseur));
            }

            if (totalTtc <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalTtc),
                    "Le montant TTC doit être strictement positif.");
            }

            bool estFacture = GetEstFacture(sens);

            string idOpenBee =
                documentId.Trim();

            string numeroFactureOpenBee =
                numeroFacture.Trim();

            string fournisseurOpenBee =
                fournisseur.Trim();

            string numeroFactureSage = LimiterTexte(
                numeroFactureOpenBee,
                LongueurNumeroFactureSage);

            double montantTtcRecherche =
                Math.Abs(Convert.ToDouble(
                    totalTtc,
                    CultureInfo.InvariantCulture));

            EcritureSensType sensSageRecherche = estFacture
                ? EcritureSensType.EcritureSensTypeCredit
                : EcritureSensType.EcritureSensTypeDebit;

            string valeurBonAPayer = LimiterTexte(
                estBonAPayer ? "Oui" : "Non",
                LongueurBonAPayer);

            string valeurRaisonRefus = estBonAPayer
                ? string.Empty
                : LimiterTexte(
                    raisonRefus?.Trim() ?? string.Empty,
                    LongueurRaisonRefus);

            if (!estBonAPayer &&
                string.IsNullOrWhiteSpace(valeurRaisonRefus))
            {
                throw new ArgumentException(
                    "Une raison de refus est obligatoire lorsque le Bon à payer vaut Non.",
                    nameof(raisonRefus));
            }

            var chronometreTotal = Stopwatch.StartNew();
            var chronometreRecherche = Stopwatch.StartNew();

            Logger.Info(
                "Recherche Sage ciblée : ID OpenBee={0}, "
                + "facture OpenBee={1}, référence Sage={2}, "
                + "fournisseur OpenBee={3}, TTC={4:F2}, sens={5}",
                idOpenBee,
                numeroFactureOpenBee,
                numeroFactureSage,
                fournisseurOpenBee,
                totalTtc,
                estFacture ? "Facture" : "Avoir");

            IBICollection collection =
                RechercherEcrituresParReference(numeroFactureSage);

            List<IBOEcriture3> ecritures =
                new List<IBOEcriture3>();

            foreach (IBOEcriture3 ecriture in collection)
            {
                string referenceFacture =
                    ecriture.EC_RefPiece?.Trim() ?? string.Empty;

                if (!referenceFacture.Equals(
                        numeroFactureSage,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn(
                        "Écriture ignorée après filtrage Sage : "
                        + "référence demandée={0}, référence trouvée={1}, EC_No={2}",
                        numeroFactureSage,
                        referenceFacture,
                        ecriture.EC_No);

                    continue;
                }

                ecritures.Add(ecriture);
            }

            chronometreRecherche.Stop();

            Logger.Info(
                "Recherche Sage terminée : facture OpenBee={0}, "
                + "référence Sage={1}, écritures retournées={2}, durée={3} ms",
                numeroFactureOpenBee,
                numeroFactureSage,
                ecritures.Count,
                chronometreRecherche.ElapsedMilliseconds);

            if (ecritures.Count == 0)
            {
                throw new InvalidOperationException(
                    "Aucune écriture Sage trouvée pour la référence "
                    + numeroFactureSage
                    + " issue de la facture OpenBee "
                    + numeroFactureOpenBee
                    + ".");
            }

            List<IBOEcriture3> lignesTiers =
                new List<IBOEcriture3>();

            List<string> diagnosticsLignesTiers =
                new List<string>();

            foreach (IBOEcriture3 ecriture in ecritures)
            {
                if (ecriture.Tiers == null)
                    continue;

                IBOJournal3 journal =
                    ecriture.Journal;

                IBOTiers3 tiers =
                    ecriture.Tiers;

                string fournisseurSage =
                    tiers.CT_Intitule?.Trim()
                    ?? string.Empty;

                double montantSage =
                    Math.Abs(ecriture.EC_Montant);

                bool montantCorrespond =
                    Math.Abs(montantSage - montantTtcRecherche)
                    <= ToleranceMontant;

                bool sensCorrespond =
                    ecriture.EC_Sens == sensSageRecherche;

                bool journalAchatCorrespond =
                    journal != null
                    && journal.JO_Type
                    == JournalType.JournalTypeAchat;

                bool fournisseurCorrespond =
                    string.Equals(
                        fournisseurSage,
                        fournisseurOpenBee,
                        StringComparison.Ordinal);

                string idDmsSage =
                    GetInformationLibreTexte(
                        ecriture,
                        NomInfoLibreIdDms);

                bool idDmsRenseigne =
                    !string.IsNullOrEmpty(idDmsSage);

                bool idDmsCorrespond =
                    !idDmsRenseigne
                    || string.Equals(
                        idDmsSage,
                        idOpenBee,
                        StringComparison.Ordinal);

                string diagnostic = string.Format(
                    CultureInfo.GetCultureInfo("fr-FR"),
                    "EC_No={0}, journal={1}, type journal={2}, "
                    + "tiers={3}, fournisseur Sage={4}, "
                    + "idDMS Sage={5}, date={6:dd/MM/yyyy}, "
                    + "pièce={7}, montant={8:F2}, sens={9}, "
                    + "montant OK={10}, sens OK={11}, "
                    + "journal Achats OK={12}, fournisseur OK={13}, "
                    + "idDMS contrôlé={14}, idDMS OK={15}",
                    ecriture.EC_No,
                    GetJournalNumero(ecriture),
                    GetJournalTypeDescription(journal),
                    tiers.CT_Num,
                    fournisseurSage,
                    string.IsNullOrEmpty(idDmsSage)
                        ? "<vide>"
                        : idDmsSage,
                    ecriture.Date,
                    ecriture.EC_Piece,
                    montantSage,
                    ecriture.EC_Sens,
                    montantCorrespond,
                    sensCorrespond,
                    journalAchatCorrespond,
                    fournisseurCorrespond,
                    idDmsRenseigne,
                    idDmsCorrespond);

                diagnosticsLignesTiers.Add(diagnostic);

                Logger.Debug(
                    "Contrôle ligne tiers Sage : {0}",
                    diagnostic);

                if (montantCorrespond
                    && sensCorrespond
                    && journalAchatCorrespond
                    && fournisseurCorrespond
                    && idDmsCorrespond)
                {
                    lignesTiers.Add(ecriture);
                }
            }

            if (lignesTiers.Count == 0)
            {
                throw new InvalidOperationException(
                    "Aucune ligne tiers Sage ne correspond aux critères : "
                    + "référence="
                    + numeroFactureSage
                    + ", TTC="
                    + totalTtc.ToString(
                        "F2",
                        CultureInfo.GetCultureInfo("fr-FR"))
                    + ", sens attendu="
                    + (estFacture ? "crédit (Facture)" : "débit (Avoir)")
                    + ", type de journal attendu=Achats"
                    + ", fournisseur attendu="
                    + fournisseurOpenBee
                    + ", ID OpenBee attendu lorsque idDMS est renseigné="
                    + idOpenBee
                    + ". Lignes tiers contrôlées : "
                    + (diagnosticsLignesTiers.Count == 0
                        ? "aucune"
                        : string.Join(" | ", diagnosticsLignesTiers)));
            }

            if (lignesTiers.Count > 1)
            {
                List<string> correspondances =
                    new List<string>();

                foreach (IBOEcriture3 ligneTiers in lignesTiers)
                {
                    correspondances.Add(
                        GetPieceDescription(ligneTiers));
                }

                throw new InvalidOperationException(
                    "Rapprochement Sage ambigu : "
                    + lignesTiers.Count
                    + " lignes tiers correspondent à la référence "
                    + numeroFactureSage
                    + ", au TTC "
                    + totalTtc.ToString(
                        "F2",
                        CultureInfo.GetCultureInfo("fr-FR"))
                    + " et au sens "
                    + (estFacture ? "Facture" : "Avoir")
                    + " dans un journal de type Achats"
                    + " pour le fournisseur "
                    + fournisseurOpenBee
                    + " et l'ID OpenBee "
                    + idOpenBee
                    + ". Mise à jour annulée. Correspondances : "
                    + string.Join(" | ", correspondances));
            }

            IBOEcriture3 ligneTiersSelectionnee =
                lignesTiers[0];

            PieceComptableKey pieceSelectionnee =
                PieceComptableKey.FromEcriture(
                    ligneTiersSelectionnee);

            List<IBOEcriture3> ecrituresPiece =
                new List<IBOEcriture3>();

            foreach (IBOEcriture3 ecriture in ecritures)
            {
                if (pieceSelectionnee.Matches(ecriture))
                {
                    ecrituresPiece.Add(ecriture);
                }
            }

            if (ecrituresPiece.Count == 0)
            {
                throw new InvalidOperationException(
                    "La pièce Sage sélectionnée ne contient aucune écriture. "
                    + GetPieceDescription(ligneTiersSelectionnee));
            }

            Logger.Info(
                "Pièce Sage sélectionnée : ID OpenBee={0}, "
                + "idDMS Sage={1}, facture OpenBee={2}, "
                + "référence Sage={3}, fournisseur OpenBee={4}, "
                + "fournisseur Sage={5}, TTC XML={6:F2}, sens XML={7}, "
                + "journal={8}, type journal={9}, date={10:dd/MM/yyyy}, "
                + "pièce={11}, montant tiers Sage={12:F2}, "
                + "sens tiers Sage={13}, lignes={14}",
                idOpenBee,
                GetInformationLibreTexte(
                    ligneTiersSelectionnee,
                    NomInfoLibreIdDms),
                numeroFactureOpenBee,
                numeroFactureSage,
                fournisseurOpenBee,
                GetFournisseurDescription(
                    ligneTiersSelectionnee),
                totalTtc,
                estFacture ? "Facture" : "Avoir",
                pieceSelectionnee.JournalNumero,
                GetJournalTypeDescription(
                    ligneTiersSelectionnee.Journal),
                pieceSelectionnee.Date,
                pieceSelectionnee.NumeroPiece,
                Math.Abs(ligneTiersSelectionnee.EC_Montant),
                ligneTiersSelectionnee.EC_Sens,
                ecrituresPiece.Count);

            List<EcritureUpdate> misesAJour =
                PreparerMisesAJour(
                    ecrituresPiece,
                    valeurBonAPayer,
                    valeurRaisonRefus);

            int nombreEcrituresModifiees = 0;
            var chronometreModification = Stopwatch.StartNew();

            try
            {
                foreach (EcritureUpdate miseAJour in misesAJour)
                {
                    if (!miseAJour.ModificationNecessaire)
                    {
                        Logger.Debug(
                            "Écriture déjà à jour : pièce={0}, EC_No={1}",
                            pieceSelectionnee.NumeroPiece,
                            miseAJour.Ecriture.EC_No);

                        continue;
                    }

                    miseAJour.Ecriture.InfoLibre[NomInfoLibreBonAPayer] =
                        valeurBonAPayer;

                    miseAJour.Ecriture.InfoLibre[NomInfoLibreRaisonRefus] =
                        valeurRaisonRefus;

                    miseAJour.Ecriture.Write();
                    miseAJour.EcritureModifiee = true;
                    nombreEcrituresModifiees++;

                    Logger.Debug(
                        "Écriture mise à jour : pièce={0}, EC_No={1}",
                        pieceSelectionnee.NumeroPiece,
                        miseAJour.Ecriture.EC_No);
                }
            }
            catch (Exception modificationException)
            {
                Logger.Error(
                    modificationException,
                    "Échec pendant la mise à jour de la pièce {0}. "
                    + "Tentative de restauration des écritures déjà modifiées.",
                    pieceSelectionnee.NumeroPiece);

                RestaurerMisesAJour(misesAJour);
                throw;
            }

            chronometreModification.Stop();
            chronometreTotal.Stop();

            Logger.Info(
                "Mise à jour Sage terminée : ID OpenBee={0}, "
                + "facture OpenBee={1}, référence Sage={2}, "
                + "fournisseur={3}, TTC={4:F2}, sens={5}, pièce={6}, "
                + "trouvées={7}, modifiées={8}, durée recherche={9} ms, "
                + "durée modification={10} ms, durée totale={11} ms",
                idOpenBee,
                numeroFactureOpenBee,
                numeroFactureSage,
                fournisseurOpenBee,
                totalTtc,
                estFacture ? "Facture" : "Avoir",
                pieceSelectionnee.NumeroPiece,
                ecrituresPiece.Count,
                nombreEcrituresModifiees,
                chronometreRecherche.ElapsedMilliseconds,
                chronometreModification.ElapsedMilliseconds,
                chronometreTotal.ElapsedMilliseconds);

            return new InvoiceUpdateResult
            {
                NumeroFacture = numeroFactureOpenBee,
                BonAPayer = valeurBonAPayer,
                RaisonRefus = valeurRaisonRefus,
                NombreEcrituresTrouvees = ecrituresPiece.Count,
                NombreEcrituresModifiees = nombreEcrituresModifiees
            };
        }

        private IBICollection RechercherEcrituresParReference(
            string numeroFacture)
        {
            /*
             * BSCPTAApplication100c implémente IPredicateBuilder.
             * Le prédicat est transformé par les Objets Métiers
             * en une recherche ciblée sur la table des écritures.
             */
            IPredicateBuilder predicateBuilder =
                (IPredicateBuilder)_application;

            IPredicateComparison predicate =
                (IPredicateComparison)predicateBuilder.Create(
                    ePredicateType.PredicateTypeComparison);

            predicate.Key = "EC_RefPiece";

            predicate.PredicateTypeComparison =
                ePredicateTypeComparison.PredicateTypeComparisonEqual;

            predicate.Values.Add(numeroFacture);

            /*
             * EC_No sert uniquement à ordonner les quelques écritures
             * retournées par Sage.
             */
            return _application.FactoryEcriture.QueryPredicate(
                predicate,
                "EC_No");
        }

        private static List<EcritureUpdate> PreparerMisesAJour(
            IEnumerable<IBOEcriture3> ecritures,
            string valeurBonAPayer,
            string valeurRaisonRefus)
        {
            List<EcritureUpdate> misesAJour =
                new List<EcritureUpdate>();

            foreach (IBOEcriture3 ecriture in ecritures)
            {
                string ancienBonAPayer =
                    GetInformationLibreTexte(
                        ecriture,
                        NomInfoLibreBonAPayer);

                string ancienneRaisonRefus =
                    GetInformationLibreTexte(
                        ecriture,
                        NomInfoLibreRaisonRefus);

                bool modificationNecessaire =
                    !string.Equals(
                        ancienBonAPayer,
                        valeurBonAPayer,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    !string.Equals(
                        ancienneRaisonRefus,
                        valeurRaisonRefus,
                        StringComparison.Ordinal);

                misesAJour.Add(
                    new EcritureUpdate
                    {
                        Ecriture = ecriture,
                        AncienBonAPayer = ancienBonAPayer,
                        AncienneRaisonRefus = ancienneRaisonRefus,
                        ModificationNecessaire = modificationNecessaire
                    });
            }

            return misesAJour;
        }

        private static void RestaurerMisesAJour(
            IList<EcritureUpdate> misesAJour)
        {
            for (int index = misesAJour.Count - 1;
                 index >= 0;
                 index--)
            {
                EcritureUpdate miseAJour =
                    misesAJour[index];

                if (!miseAJour.EcritureModifiee)
                    continue;

                try
                {
                    miseAJour.Ecriture.InfoLibre[NomInfoLibreBonAPayer] =
                        miseAJour.AncienBonAPayer;

                    miseAJour.Ecriture.InfoLibre[NomInfoLibreRaisonRefus] =
                        miseAJour.AncienneRaisonRefus;

                    miseAJour.Ecriture.Write();

                    Logger.Warn(
                        "Écriture restaurée après erreur : EC_No={0}",
                        miseAJour.Ecriture.EC_No);
                }
                catch (Exception restaurationException)
                {
                    Logger.Fatal(
                        restaurationException,
                        "Impossible de restaurer l'écriture EC_No={0} "
                        + "après une erreur de mise à jour.",
                        miseAJour.Ecriture.EC_No);
                }
            }
        }

        private static bool GetEstFacture(
            string sens)
        {
            if (string.Equals(
                    sens?.Trim(),
                    "Facture",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(
                    sens?.Trim(),
                    "Avoir",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new ArgumentException(
                "Le sens doit contenir Facture ou Avoir.",
                nameof(sens));
        }

        private static string GetJournalNumero(
            IBOEcriture3 ecriture)
        {
            return ecriture.Journal?.JO_Num?.Trim()
                ?? string.Empty;
        }

        private static string GetJournalTypeDescription(
            IBOJournal3 journal)
        {
            return journal == null
                ? "aucun"
                : journal.JO_Type.ToString();
        }

        private static string GetFournisseurDescription(
            IBOEcriture3 ecriture)
        {
            if (ecriture.Tiers == null)
                return "aucun";

            return string.Format(
                "{0} ({1})",
                ecriture.Tiers.CT_Intitule?.Trim()
                    ?? string.Empty,
                ecriture.Tiers.CT_Num?.Trim()
                    ?? string.Empty);
        }

        private static string GetPieceDescription(
            IBOEcriture3 ecriture)
        {
            return string.Format(
                CultureInfo.GetCultureInfo("fr-FR"),
                "EC_No={0}, journal={1}, type journal={2}, "
                + "fournisseur={3}, date={4:dd/MM/yyyy}, pièce={5}, "
                + "montant={6:F2}, sens={7}",
                ecriture.EC_No,
                GetJournalNumero(ecriture),
                GetJournalTypeDescription(ecriture.Journal),
                GetFournisseurDescription(ecriture),
                ecriture.Date,
                ecriture.EC_Piece,
                Math.Abs(ecriture.EC_Montant),
                ecriture.EC_Sens);
        }

        private static string GetInformationLibreTexte(
            IBOEcriture3 ecriture,
            string nomInformationLibre)
        {
            try
            {
                return Convert.ToString(
                           ecriture.InfoLibre[nomInformationLibre])
                       ?.Trim()
                       ?? string.Empty;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Impossible de lire l'information libre Sage '"
                    + nomInformationLibre
                    + "' sur l'écriture EC_No="
                    + ecriture.EC_No
                    + ". Vérifiez que cette information libre existe "
                    + "avec exactement ce nom dans la société Sage.",
                    exception);
            }
        }

        private static string LimiterTexte(
            string valeur,
            int longueurMax)
        {
            if (string.IsNullOrEmpty(valeur))
            {
                return string.Empty;
            }

            return valeur.Length <= longueurMax
                ? valeur
                : valeur.Substring(0, longueurMax);
        }

        private sealed class PieceComptableKey
        {
            public string JournalNumero { get; private set; }

            public DateTime Date { get; private set; }

            public string NumeroPiece { get; private set; }

            public static PieceComptableKey FromEcriture(
                IBOEcriture3 ecriture)
            {
                return new PieceComptableKey
                {
                    JournalNumero = GetJournalNumero(ecriture),
                    Date = ecriture.Date,
                    NumeroPiece = Convert.ToString(
                        ecriture.EC_Piece)
                        ?.Trim() ?? string.Empty
                };
            }

            public bool Matches(
                IBOEcriture3 ecriture)
            {
                return string.Equals(
                           JournalNumero,
                           GetJournalNumero(ecriture),
                           StringComparison.OrdinalIgnoreCase)
                       && Date == ecriture.Date
                       && string.Equals(
                           NumeroPiece,
                           Convert.ToString(ecriture.EC_Piece)
                               ?.Trim() ?? string.Empty,
                           StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class EcritureUpdate
        {
            public IBOEcriture3 Ecriture { get; set; }

            public string AncienBonAPayer { get; set; }

            public string AncienneRaisonRefus { get; set; }

            public bool ModificationNecessaire { get; set; }

            public bool EcritureModifiee { get; set; }
        }
    }
}
