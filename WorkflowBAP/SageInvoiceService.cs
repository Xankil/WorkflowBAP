using System;
using System.Diagnostics;
using Objets100cLib;

namespace WorkflowBAP.Sage
{
    public sealed class SageInvoiceService
    {
        private const int IndexBonAPayer = 3;
        private const int IndexRaisonRefus = 4;

        private const int LongueurBonAPayer = 3;
        private const int LongueurRaisonRefus = 69;

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
            bool estBonAPayer,
            string raisonRefus)
        {
            if (string.IsNullOrWhiteSpace(numeroFacture))
            {
                throw new ArgumentException(
                    "Le numéro de facture est obligatoire.",
                    nameof(numeroFacture));
            }

            numeroFacture = numeroFacture.Trim();

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
                "Recherche Sage ciblée : facture={0}",
                numeroFacture);

            IBICollection ecritures =
                RechercherEcrituresParReference(numeroFacture);

            chronometreRecherche.Stop();

            Logger.Info(
                "Recherche Sage terminée : facture={0}, " +
                "écritures retournées={1}, durée={2} ms",
                numeroFacture,
                ecritures.Count,
                chronometreRecherche.ElapsedMilliseconds);

            int nombreEcrituresTrouvees = 0;
            int nombreEcrituresModifiees = 0;

            var chronometreModification = Stopwatch.StartNew();

            foreach (IBOEcriture3 ecriture in ecritures)
            {
                /*
                 * Vérification défensive :
                 * QueryPredicate doit déjà avoir filtré les écritures,
                 * mais on vérifie tout de même la référence retournée.
                 */
                string referenceFacture =
                    ecriture.EC_RefPiece?.Trim() ?? string.Empty;

                if (!referenceFacture.Equals(
                        numeroFacture,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn(
                        "Écriture ignorée après filtrage Sage : " +
                        "facture demandée={0}, référence trouvée={1}, EC_No={2}",
                        numeroFacture,
                        referenceFacture,
                        ecriture.EC_No);

                    continue;
                }

                nombreEcrituresTrouvees++;

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

                if (!modificationNecessaire)
                {
                    Logger.Debug(
                        "Écriture déjà à jour : facture={0}, EC_No={1}",
                        numeroFacture,
                        ecriture.EC_No);

                    continue;
                }

                ecriture.InfoLibre[IndexBonAPayer] =
                    valeurBonAPayer;

                ecriture.InfoLibre[IndexRaisonRefus] =
                    valeurRaisonRefus;

                ecriture.Write();

                nombreEcrituresModifiees++;

                Logger.Debug(
                    "Écriture mise à jour : facture={0}, EC_No={1}",
                    numeroFacture,
                    ecriture.EC_No);
            }

            chronometreModification.Stop();
            chronometreTotal.Stop();

            Logger.Info(
                "Mise à jour Sage terminée : facture={0}, " +
                "trouvées={1}, modifiées={2}, " +
                "durée recherche={3} ms, " +
                "durée modification={4} ms, " +
                "durée totale={5} ms",
                numeroFacture,
                nombreEcrituresTrouvees,
                nombreEcrituresModifiees,
                chronometreRecherche.ElapsedMilliseconds,
                chronometreModification.ElapsedMilliseconds,
                chronometreTotal.ElapsedMilliseconds);

            return new InvoiceUpdateResult
            {
                NumeroFacture = numeroFacture,
                BonAPayer = valeurBonAPayer,
                RaisonRefus = valeurRaisonRefus,
                NombreEcrituresTrouvees = nombreEcrituresTrouvees,
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
    }
}