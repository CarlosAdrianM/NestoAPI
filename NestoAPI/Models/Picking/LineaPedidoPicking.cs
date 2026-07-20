using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class LineaPedidoPicking
    {
        public int Id { get; set; }
        public int NumeroPedido { get; set; }
        public byte TipoLinea { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public int CantidadRecogida { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
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

        /// <summary>
        /// NestoAPI#314: total CON IVA (y recargo de equivalencia, que Total ya incluye) de lo que
        /// sale en esta tanda, prorrateado por la cantidad reservada igual que BaseImponibleEntrega.
        /// El aviso de picking lo usa para decir cuánto dinero tiene que preparar el cliente.
        /// </summary>
        public decimal TotalEntrega
        {
            get
            {
                return Cantidad != 0 ? Total / Cantidad * CantidadReservada : 0;
            }
        }
    }
}