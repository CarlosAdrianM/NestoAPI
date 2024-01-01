using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica.Etiquetas
{
    public class EtiquetaClientesNuevos : IEtiquetaComisionClientes
    {
        private const decimal IMPORTE_MINIMO_COMISION = 300;
        private readonly IServicioComisionesAnuales _servicio;

        public EtiquetaClientesNuevos(IServicioComisionesAnuales servicioComisiones)
        {
            _servicio = servicioComisiones;
        }

        public int Recuento { get; set; }

        public string Nombre => "Clientes nuevos";

        public decimal Tipo { get; set; }
        public decimal Comision { 
            get => Math.Round(Recuento * Tipo, 2, MidpointRounding.AwayFromZero); 
            set => throw new Exception("No se puede fijar manualmente la comisión por clientes nuevos"); 
        }
        public bool EsComisionAcumulada => false;

        public object Clone() => new EtiquetaClientesNuevos(_servicio)
        {
            Recuento = this.Recuento,
            Tipo = this.Tipo
        };

        public List<ClienteVenta> LeerClientesDetalle(string vendedor, int anno, int mes)
        {
            return _servicio.LeerClientesNuevosConVenta(vendedor, anno, mes);
        }

        public int LeerClientesMes(string vendedor, int anno, int mes)
        {
            var clientesTotales = LeerClientesDetalle(vendedor, anno, mes);
            var clientesComisionables = clientesTotales.Where(c => c.Venta >= IMPORTE_MINIMO_COMISION);
            var totalClientes = clientesComisionables.Count();
            
            var listaVendedores = _servicio.ListaVendedores(vendedor);
            var comisionesAnno = _servicio.LeerComisionesAnualesResumenMes(listaVendedores, anno);
            var yaHanComisionado = (int)comisionesAnno.Where(c => c.Etiqueta == Nombre && c.Mes < mes).Sum(c => c.Venta); // en la tabla la columna se llama Venta aunque sea Recuento
            return totalClientes - yaHanComisionado;
        }

        public decimal SetTipo(TramoComision tramo) => 10.0M; // 10 euros por cliente nuevo
    }
}