namespace NestoAPI.Models.Comisiones.Estetica.Etiquetas
{
    public class EtiquetaUnionLaserJefeVentas : EtiquetaUnionLaser
    {
        public EtiquetaUnionLaserJefeVentas(IServicioComisionesAnuales servicio)
            : base(servicio) { }

        private new const decimal TIPO_FIJO_UNIONLASER = .02M;
        public override decimal SetTipo(TramoComision tramo) => TIPO_FIJO_UNIONLASER + tramo.TipoExtra;
    }
}