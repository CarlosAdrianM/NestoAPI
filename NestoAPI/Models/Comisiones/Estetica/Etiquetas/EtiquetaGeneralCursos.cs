using System;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Estetica.Etiquetas
{
    public class EtiquetaGeneralCursos : EtiquetaComisionVentaAcumuladaBase
    {
        public EtiquetaGeneralCursos(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {
        }

        public override string Nombre => "General Cursos";

        // No aplicar filtro por vendedor (comportamiento original)
        protected override bool DebeAplicarFiltroVendedor => false;

        protected override Expression<Func<vstLinPedidoVtaComisione, bool>> PredicadoFiltro()
        {
            return l => l.Grupo == "Cursos";
        }

        public override object Clone()
        {
            return new EtiquetaGeneralCursos(_servicioComisiones)
            {
                Venta = Venta,
                Tipo = Tipo,
                Comision = Comision,
                FaltaParaSalto = FaltaParaSalto,
                InicioTramo = InicioTramo,
                FinalTramo = FinalTramo,
                BajaSaltoMesSiguiente = BajaSaltoMesSiguiente,
                Proyeccion = Proyeccion,
                VentaAcumulada = VentaAcumulada,
                ComisionAcumulada = ComisionAcumulada,
                TipoConseguido = TipoConseguido,
                EstrategiaUtilizada = EstrategiaUtilizada,
                TipoCorrespondePorTramo = TipoCorrespondePorTramo,
                TipoRealmenteAplicado = TipoRealmenteAplicado,
                MotivoEstrategia = MotivoEstrategia,
                ComisionSinEstrategia = ComisionSinEstrategia
            };
        }
    }
}