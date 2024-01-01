using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica.Etiquetas
{
    public class EtiquetaClientesTramosMil : IEtiquetaComisionClientes
    {
        private const decimal IMPORTE_TRAMOS = 1000;
        private readonly IServicioComisionesAnuales _servicio;

        public EtiquetaClientesTramosMil(IServicioComisionesAnuales servicioComisiones)
        {
            _servicio = servicioComisiones;
        }

        public int Recuento { get; set; }

        public string Nombre => "Tramos de mil";

        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get => Math.Round(Recuento * Tipo, 2, MidpointRounding.AwayFromZero);
            set => throw new Exception("No se puede fijar manualmente la comisión por tramos de mil");
        }

        public bool EsComisionAcumulada => false;

        public object Clone() => new EtiquetaClientesTramosMil(_servicio)
        {
            Recuento = this.Recuento,
            Tipo = this.Tipo
        };

        public List<ClienteVenta> LeerClientesDetalle(string vendedor, int anno, int mes)
        {
            return _servicio.LeerClientesConVenta(vendedor, anno, mes);
        }

        public int LeerClientesMes(string vendedor, int anno, int mes)
        {
            var clientesTotales = LeerClientesDetalle(vendedor, anno, mes);
            var clientesComisionables = clientesTotales.Where(c => c.Venta >= IMPORTE_TRAMOS);
            var totalClientes = clientesComisionables
                .Select(c => (int)Math.Floor(c.Venta / IMPORTE_TRAMOS))
                .Sum();

            // Aquí hay que restar las que ya hemos pagado de meses anteriores

            return totalClientes;
        }

        public decimal SetTipo(TramoComision tramo) => 4.0M; // 5 euros por cliente nuevo
    }
}