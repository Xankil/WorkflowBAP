namespace WorkflowBAP.Sage
{
    public sealed class InvoiceUpdateResult
    {
        public string NumeroFacture { get; set; }

        public string BonAPayer { get; set; }

        public string RaisonRefus { get; set; }

        public int NombreEcrituresTrouvees { get; set; }

        public int NombreEcrituresModifiees { get; set; }

        public bool FactureTrouvee =>
            NombreEcrituresTrouvees > 0;
    }
}