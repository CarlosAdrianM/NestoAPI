using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class LineaPedidoPicking
    {
        public int Id { get; set; }
        public byte TipoLinea { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        public int CantidadReservada { get; set; }
        public DateTime FechaEntrega { get; set; }
        public bool EsSobrePedido { get; set; } = true;
        public bool Borrar { get; set; } = false;
        public DateTime FechaModificacion { get; set; }
        public bool EsPedidoEspecial { get; set; }
        public decimal BaseImponibleEntrega { 
            get
            {
                if (Cantidad != 0)
                {
                    return BaseImponible / Cantidad * CantidadReservada;
                }
                else
                {
                    return 0;
                }
            }
        }
    }
}