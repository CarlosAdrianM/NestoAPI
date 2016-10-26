using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class RellenadorUbicacionesService : IRellenadorUbicacionesService
    {
        private NVEntities db = new NVEntities();

        public List<UbicacionPicking> Rellenar(List<PedidoPicking> pedidos)
        {
            List<LineaPedidoPicking> lineas = pedidos.SelectMany(l => l.Lineas).ToList();
            List<string> productos = lineas.GroupBy(l => l.Producto).Select(l => l.Key).ToList();
            List<UbicacionPicking> ubicaciones = db.Ubicaciones.Where(u => u.Estado == 0 || u.Estado == 2).Join(productos, u => new { producto = u.Número }, p => new { producto = p }, (u, p) => new UbicacionPicking
            {
                Id = u.NºOrden,
                Producto = u.Número,
                Cantidad = u.Cantidad,
                CantidadNueva = u.Cantidad,
                Estado = (int)u.Estado,
                EstadoNuevo = (int)u.Estado
            }).ToList();
            return ubicaciones;
        }
    }
}