using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class EtiquetaLisap : IEtiquetaComision
    {
        public string Nombre
        {
            get
            {
                return "Lisap";
            }
        }

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get
            {
                return Math.Round(Venta * Tipo, 2);
            }
            set
            {
                throw new Exception("La comisión de Lisap no se puede fijar manualmente");
            }
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            throw new System.NotImplementedException();
        }

        public IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            throw new System.NotImplementedException();
        }

        public decimal SetTipo(TramoComision tramo)
        {
            throw new System.NotImplementedException();
        }
    }
}