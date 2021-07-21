using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.PedidosVenta
{
    public class ParametroStringIntPedido
    {
        public string Empresa { get; set; }
        public int NumeroPedidoOriginal { get; set; }
        public PedidoVentaDTO PedidoAmpliacion { get; set; }
    }
}