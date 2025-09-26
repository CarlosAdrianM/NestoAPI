namespace NestoAPI.Models.Comisiones
{
    public abstract class EtiquetaComisionBase : IEtiquetaComision
    {
        public abstract string Nombre { get; }
        public decimal Tipo { get; set; }
        public abstract decimal Comision { get; set; }
        public abstract decimal SetTipo(TramoComision tramo);
        public abstract bool EsComisionAcumulada { get; }

        // Propiedades comunes con implementación por defecto
        public decimal CifraAnual { get; set; }
        public decimal ComisionAnual { get; set; }
        public decimal PorcentajeAnual { get; set; }

        // Propiedad virtual con implementación por defecto que se puede sobrescribir
        public virtual string UnidadCifra => "€";

        public abstract object Clone();
    }
}
