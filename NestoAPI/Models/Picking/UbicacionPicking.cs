using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class UbicacionPicking
    {
        public int Id { get; set; }
        public int CopiaId { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public int CantidadNueva { get; set; }
        public int Estado { get; set; }
        public int EstadoNuevo { get; set; }
        public int LineaPedidoVentaId { get; set; }
    }
}