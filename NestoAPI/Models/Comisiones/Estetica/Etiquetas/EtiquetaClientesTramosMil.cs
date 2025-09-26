using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica.Etiquetas
{
    public class EtiquetaClientesTramosMil : EtiquetaComisionClientesBase, IEtiquetaComisionClientes
    {
        private const decimal IMPORTE_TRAMOS = 1000;
        private readonly IServicioComisionesAnuales _servicio;

        public EtiquetaClientesTramosMil(IServicioComisionesAnuales servicioComisiones)
        {
            _servicio = servicioComisiones;
        }

        public override string Nombre => "Tramos de mil";

        public override decimal Comision
        {
            get => Math.Round(Recuento * Tipo, 2, MidpointRounding.AwayFromZero);
            set => throw new Exception("No se puede fijar manualmente la comisión por tramos de mil");
        }

        public override bool EsComisionAcumulada => false;

        public override object Clone()
        {
            return new EtiquetaClientesTramosMil(_servicio)
            {
                Recuento = Recuento,
                Tipo = Tipo
            };
        }

        public override List<ClienteVenta> LeerClientesDetalle(string vendedor, int anno, int mes)
        {
            return _servicio.LeerClientesConVenta(vendedor, anno, mes);
        }

        public override int LeerClientesMes(string vendedor, int anno, int mes)
        {
            var clientesTotales = LeerClientesDetalle(vendedor, anno, mes);
            var clientesComisionables = clientesTotales.Where(c => c.Venta >= IMPORTE_TRAMOS);
            var totalClientes = clientesComisionables
                .Select(c => (int)Math.Floor(c.Venta / IMPORTE_TRAMOS))
                .Sum();

            // Aquí hay que restar las que ya hemos pagado de meses anteriores
            var listaVendedores = _servicio.ListaVendedores(vendedor);
            var comisionesAnno = _servicio.LeerComisionesAnualesResumenMes(listaVendedores, anno);
            var yaHanComisionado = (int)comisionesAnno.Where(c => c.Etiqueta == Nombre && c.Mes < mes).Sum(c => c.Venta); // en la tabla la columna se llama Venta aunque sea Recuento
            return totalClientes - yaHanComisionado;
        }

        public override decimal SetTipo(TramoComision tramo)
        {
            return 4.0M; // 5 euros por cliente nuevo
        }

        public override string UnidadCifra => "clientes";

    }
}