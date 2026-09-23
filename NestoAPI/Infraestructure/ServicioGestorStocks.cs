using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    public class ServicioGestorStocks : IServicioGestorStocks
    {
        private NVEntities db;

        public ServicioGestorStocks()
        {
            db = new NVEntities();
        }

        public int Stock(string producto)
        {
            int stockExtracto = db.ExtractosProducto
                .Where(e => e.Número == producto && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            int stockRepo = db.PreExtrProductos.Where(e => e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
            return stockExtracto + stockRepo;
        }
        public int Stock(string producto, string almacen)
        {
            return db.ExtractosProducto
                .Where(e => e.Almacén == almacen && e.Número == producto)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }

        public int UnidadesDisponiblesTodosLosAlmacenes(string producto)
        {
            int stock = db.ExtractosProducto
                .Where(e => e.Número == producto && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            int pendienteReposicion = db.PreExtrProductos
                .Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) &&
                    e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum();
            int pendientes = db.LinPedidoVtas
                .Where(l =>
                    l.Producto == producto && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE 
                    && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            return stock - pendientes + pendienteReposicion;
        }

        public int UnidadesPendientesEntregar(string producto)
        {
            return (int)db.LinPedidoVtas.Where(l =>
            l.Producto == producto &&
            l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
            .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }
        /// <summary>
        /// NestoAPI#517: las mismas cuentas que Stock(producto, almacen), UnidadesPendientesEntregarAlmacen y
        /// UnidadesDisponiblesTodosLosAlmacenes, para todos los productos a la vez: 4 consultas agrupadas.
        /// </summary>
        public ResumenStocksProductos LeerResumenStocks(IEnumerable<string> productos)
        {
            var resumen = new ResumenStocksProductos();
            List<string> lista = (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct()
                .ToList();
            if (!lista.Any())
            {
                return resumen;
            }
            List<string> sedes = Constantes.Sedes.ListaSedes;

            var stocks = db.ExtractosProducto
                .Where(e => lista.Contains(e.Número))
                .GroupBy(e => new { e.Número, e.Almacén })
                .Select(g => new { g.Key.Número, g.Key.Almacén, Cantidad = g.Sum(e => (int)e.Cantidad) })
                .ToList();
            foreach (var s in stocks)
            {
                ResumenStocksProductos.Sumar(resumen.StockAlmacen, ResumenStocksProductos.Clave(s.Número, s.Almacén), s.Cantidad);
                if (s.Almacén != null && sedes.Contains(s.Almacén.Trim()))
                {
                    ResumenStocksProductos.Sumar(resumen.StockSedes, ResumenStocksProductos.Clave(s.Número), s.Cantidad);
                }
            }

            var pendientes = db.LinPedidoVtas
                .Where(l => lista.Contains(l.Producto)
                    && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .GroupBy(l => new { l.Producto, l.Almacén })
                .Select(g => new { g.Key.Producto, g.Key.Almacén, Cantidad = g.Sum(l => (int?)l.Cantidad) ?? 0 })
                .ToList();
            foreach (var p in pendientes)
            {
                ResumenStocksProductos.Sumar(resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(p.Producto, p.Almacén), p.Cantidad);
                ResumenStocksProductos.Sumar(resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(p.Producto), p.Cantidad);
            }

            var reposiciones = db.PreExtrProductos
                .Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
                    && lista.Contains(e.Producto.Número) && e.NºTraspaso != null && e.NºTraspaso > 0)
                .GroupBy(e => e.Producto.Número)
                .Select(g => new { Número = g.Key, Cantidad = g.Sum(e => (int)e.Cantidad) })
                .ToList();
            foreach (var r in reposiciones)
            {
                ResumenStocksProductos.Sumar(resumen.PendienteReposicion, ResumenStocksProductos.Clave(r.Número), r.Cantidad);
            }
            return resumen;
        }

        public int UnidadesPendientesEntregarAlmacen(string producto, string almacen)
        {
            return (int)db.LinPedidoVtas.Where(l =>
            l.Almacén == almacen && l.Producto == producto &&
            l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
            .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }
    }
}