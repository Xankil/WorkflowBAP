using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Objets100cLib;

namespace WorkflowBAP.Sage
{
    public sealed class SageInvoiceService
    {
        private const int IndexBonAPayer = 3;
        private const int IndexRaisonRefus = 4;

        private const int LongueurNumeroFactureSage = 17;
        private const int LongueurBonAPayer = 3;
        private const int LongueurRaisonRefus = 69;

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
            string numeroFacture,
            decimal totalTtc,
            string sens,
            bool estBonAPayer,
            string raisonRefus)
        {
            if (string.IsNullOrWhiteSpace(numeroFacture))
            {
                throw new ArgumentException(
                    "Le numéro de facture est obligatoire.",
                    nameof(numeroFacture));
            }

            if (totalTtc <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalTtc),
                    "Le montant TTC doit être strictement positif.");
            }

            bool estFacture = GetEstFacture(sens);

            string numeroFactureOpenBee =
                numeroFacture.Trim();

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
                "Recherche Sage ciblée : facture OpenBee={0}, "
                + "référence Sage={1}, TTC={2:F2}, sens={3}",
                numeroFactureOpenBee,
                numeroFactureSage,
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

                string diagnostic = string.Format(
                    CultureInfo.GetCultureInfo("fr-FR"),
                    "EC_No={0}, journal={1}, type journal={2}, "
                    + "date={3:dd/MM/yyyy}, pièce={4}, montant={5:F2}, "
                    + "sens={6}, montant OK={7}, sens OK={8}, "
                    + "journal Achats OK={9}",
                    ecriture.EC_No,
                    GetJournalNumero(ecriture),
                    GetJournalTypeDescription(journal),
                    ecriture.Date,
                    ecriture.EC_Piece,
                    montantSage,
                    ecriture.EC_Sens,
                    montantCorrespond,
                    sensCorrespond,
                    journalAchatCorrespond);

                diagnosticsLignesTiers.Add(diagnostic);

                Logger.Debug(
                    "Contrôle ligne tiers Sage : {0}",
                    diagnostic);

                if (montantCorrespond
                    && sensCorrespond
                    && journalAchatCorrespond)
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
                "Pièce Sage sélectionnée : facture OpenBee={0}, "
                + "référence Sage={1}, TTC XML={2:F2}, sens XML={3}, "
                + "journal={4}, type journal={5}, date={6:dd/MM/yyyy}, "
                + "pièce={7}, montant tiers Sage={8:F2}, "
                + "sens tiers Sage={9}, lignes={10}",
                numeroFactureOpenBee,
                numeroFactureSage,
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

                    miseAJour.Ecriture.InfoLibre[IndexBonAPayer] =
                        valeurBonAPayer;

                    miseAJour.Ecriture.InfoLibre[IndexRaisonRefus] =
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
                "Mise à jour Sage terminée : facture OpenBee={0}, "
                + "référence Sage={1}, TTC={2:F2}, sens={3}, pièce={4}, "
                + "trouvées={5}, modifiées={6}, "
                + "durée recherche={7} ms, durée modification={8} ms, "
                + "durée totale={9} ms",
                numeroFactureOpenBee,
                numeroFactureSage,
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
                    Convert.ToString(
                        ecriture.InfoLibre[IndexBonAPayer])
                    ?.Trim() ?? string.Empty;

                string ancienneRaisonRefus =
                    Convert.ToString(
                        ecriture.InfoLibre[IndexRaisonRefus])
                    ?.Trim() ?? string.Empty;

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
                    miseAJour.Ecriture.InfoLibre[IndexBonAPayer] =
                        miseAJour.AncienBonAPayer;

                    miseAJour.Ecriture.InfoLibre[IndexRaisonRefus] =
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

        private static string GetPieceDescription(
            IBOEcriture3 ecriture)
        {
            return string.Format(
                CultureInfo.GetCultureInfo("fr-FR"),
                "EC_No={0}, journal={1}, type journal={2}, "
                + "date={3:dd/MM/yyyy}, pièce={4}, montant={5:F2}, sens={6}",
                ecriture.EC_No,
                GetJournalNumero(ecriture),
                GetJournalTypeDescription(ecriture.Journal),
                ecriture.Date,
                ecriture.EC_Piece,
                Math.Abs(ecriture.EC_Montant),
                ecriture.EC_Sens);
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
