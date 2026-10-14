using NestoAPI.Infraestructure.IndicadoresStock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.IndicadoresStock
{
    /// <summary>Un movimiento de ExtractoProducto de mentira.</summary>
    internal class MovimientoExtracto
    {
        public string Empresa { get; set; } = "1";
        public string Producto { get; set; }
        public string Almacen { get; set; } = "ALG";
        public DateTime Fecha { get; set; }
        public int Cantidad { get; set; }
        public string Diario { get; set; } = "SYS";
        public decimal Importe { get; set; }
        public int Orden { get; set; }
    }

    /// <summary>Una línea de LinPedidoVta de mentira.</summary>
    internal class LineaVenta
    {
        public string Empresa { get; set; } = "1";
        public int Pedido { get; set; }
        public int Orden { get; set; }
        public string Producto { get; set; }
        public string Almacen { get; set; } = "ALG";
        public int TipoLinea { get; set; } = 1;
        public int Estado { get; set; } = 4;
        public int Cantidad { get; set; }
        public DateTime FechaEntrega { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public int Albaran { get; set; }
    }

    internal class ProductoEnMemoria : ProductoIndicadorStock
    {
        public bool Ficticio { get; set; }
    }

    /// <summary>
    /// NestoAPI#629: un extracto pequeño en memoria con las mismas reglas que las consultas de <see cref="RepositorioIndicadoresStockSql"/>
    /// (empresas 1 y 3, sin EXT, sin ficticios, venta = estado >= 2 sin MTP ni MMP, tramos mensuales). Guardar borra el mes y
    /// reinserta, como la transacción de verdad.
    /// </summary>
    internal class RepositorioIndicadoresStockEnMemoria : IRepositorioIndicadoresStock
    {
        public List<ProductoEnMemoria> Productos { get; } = new List<ProductoEnMemoria>();
        public List<MovimientoExtracto> Extracto { get; } = new List<MovimientoExtracto>();
        public List<LineaVenta> Lineas { get; } = new List<LineaVenta>();
        /// <summary>La tabla IndicadoresStock.</summary>
        public List<FilaIndicadoresStock> Tabla { get; } = new List<FilaIndicadoresStock>();
        /// <summary>Los meses en el orden en que se han guardado.</summary>
        public List<DateTime> MesesGuardados { get; } = new List<DateTime>();
        public bool TablaExiste { get; set; } = true;
        public Func<DateTime, Exception> FalloStock { get; set; }
        public Func<DateTime, Exception> FalloServicio { get; set; }
        public Func<DateTime, Exception> FalloGuardar { get; set; }

        private static readonly string[] EMPRESAS = { "1", "3" };

        public Task<bool> ExisteTabla() => Task.FromResult(TablaExiste);

        public Task<List<ProductoIndicadorStock>> LeerProductos()
        {
            return Task.FromResult(Productos.Where(p => !p.Ficticio).Cast<ProductoIndicadorStock>().ToList());
        }

        private IEnumerable<MovimientoExtracto> ExtractoValido()
        {
            HashSet<string> reales = new HashSet<string>(Productos.Where(p => !p.Ficticio).Select(p => p.Producto));
            return Extracto.Where(e => EMPRESAS.Contains(e.Empresa) && e.Almacen != "EXT" && reales.Contains(e.Producto));
        }

        public Task<List<MovimientoStockTramo>> LeerStock(DateTime mes)
        {
            Exception fallo = FalloStock?.Invoke(mes);
            if (fallo != null)
            {
                throw fallo;
            }
            DateTime fin = mes.AddMonths(1);
            DateTime inicioVentana = mes.AddMonths(-11);
            List<MovimientoStockTramo> filas = ExtractoValido()
                .Where(e => e.Fecha < fin)
                .GroupBy(e => new { e.Producto, e.Almacen, Tramo = e.Fecha < inicioVentana ? -1 : Meses(inicioVentana, e.Fecha) })
                .Select(g => new MovimientoStockTramo
                {
                    Producto = g.Key.Producto,
                    Almacen = g.Key.Almacen,
                    Tramo = g.Key.Tramo,
                    Cantidad = g.Sum(e => e.Cantidad),
                    PrimeraFecha = g.Min(e => e.Fecha),
                    UnidadesCompradas = g.Where(e => e.Diario == "_ALBCOMP").Sum(e => e.Cantidad),
                    ImporteCompras = g.Where(e => e.Diario == "_ALBCOMP").Sum(e => e.Cantidad < 0 ? -Math.Abs(e.Importe) : Math.Abs(e.Importe)),
                    UnidadesBajas = g.Where(e => e.Diario.StartsWith("Merma") || e.Diario == "Robo").Sum(e => e.Cantidad)
                })
                .ToList();
            return Task.FromResult(filas);
        }

        private IEnumerable<LineaVenta> VentasValidas()
        {
            HashSet<string> enDemanda = new HashSet<string>(Productos
                .Where(p => !p.Ficticio && p.Grupo != "MTP" && p.SubGrupo != "MMP")
                .Select(p => p.Producto));
            return Lineas.Where(l => EMPRESAS.Contains(l.Empresa) && l.TipoLinea == 1 && l.Estado >= 2 && l.FechaAlbaran.HasValue
                && enDemanda.Contains(l.Producto));
        }

        public Task<List<VentaTramo>> LeerVentas(DateTime mes)
        {
            DateTime desde = mes.AddMonths(-23);
            DateTime fin = mes.AddMonths(1);
            List<VentaTramo> filas = VentasValidas()
                .Where(l => l.FechaAlbaran.Value >= desde && l.FechaAlbaran.Value < fin)
                .GroupBy(l => new { l.Producto, l.Almacen, Tramo = Meses(desde, l.FechaAlbaran.Value) })
                .Select(g => new VentaTramo
                {
                    Producto = g.Key.Producto,
                    Almacen = g.Key.Almacen,
                    Tramo = g.Key.Tramo,
                    UnidadesNetas = g.Sum(l => l.Cantidad),
                    UnidadesSalida = g.Where(l => l.Cantidad > 0).Sum(l => l.Cantidad)
                })
                .ToList();
            return Task.FromResult(filas);
        }

        public Task<List<ServicioAlmacen>> LeerServicio(DateTime mes)
        {
            Exception fallo = FalloServicio?.Invoke(mes);
            if (fallo != null)
            {
                throw fallo;
            }
            DateTime fin = mes.AddMonths(1);
            List<LineaVenta> delMes = Lineas
                .Where(l => EMPRESAS.Contains(l.Empresa) && l.TipoLinea == 1 && l.Estado >= 2 && l.Cantidad > 0
                    && l.FechaAlbaran.HasValue && l.FechaAlbaran.Value >= mes && l.FechaAlbaran.Value < fin)
                .ToList();
            List<ServicioAlmacen> filas = delMes
                .GroupBy(l => l.Almacen)
                .Select(g => new ServicioAlmacen
                {
                    Almacen = g.Key,
                    Lineas = g.Count(),
                    SumaDias = g.Sum(l => (long)(l.FechaAlbaran.Value.Date - l.FechaEntrega.Date).TotalDays),
                    LineasCompletas = g.Count(l => !Lineas.Any(o => o.Empresa == l.Empresa && o.Pedido == l.Pedido && o.Producto == l.Producto
                        && o.FechaEntrega == l.FechaEntrega && o.Orden != l.Orden && o.TipoLinea == 1 && o.Cantidad > 0 && o.Estado >= -1
                        && (o.Estado < 2 || o.Albaran != l.Albaran)))
                })
                .ToList();
            return Task.FromResult(filas);
        }

        public Task<List<MovimientoProductoComprado>> LeerMovimientosProductosComprados(DateTime mes)
        {
            DateTime fin = mes.AddMonths(1);
            List<MovimientoExtracto> delMes = Extracto
                .Where(e => EMPRESAS.Contains(e.Empresa) && e.Almacen != "EXT" && e.Fecha >= mes && e.Fecha < fin)
                .ToList();
            HashSet<string> comprados = new HashSet<string>(delMes.Where(e => e.Diario == "_ALBCOMP" && e.Cantidad > 0).Select(e => e.Producto));
            return Task.FromResult(delMes
                .Where(e => comprados.Contains(e.Producto))
                .Select(e => new MovimientoProductoComprado
                {
                    Producto = e.Producto,
                    Almacen = e.Almacen,
                    Fecha = e.Fecha,
                    Empresa = e.Empresa,
                    Orden = e.Orden,
                    Cantidad = e.Cantidad,
                    EsCompra = e.Diario == "_ALBCOMP" && e.Cantidad > 0
                })
                .ToList());
        }

        public Task Guardar(DateTime mes, IList<FilaIndicadoresStock> filas)
        {
            Exception fallo = FalloGuardar?.Invoke(mes);
            if (fallo != null)
            {
                throw fallo;
            }
            _ = Tabla.RemoveAll(f => f.Mes == mes);
            Tabla.AddRange(filas.Select(f =>
            {
                f.FechaCalculo = new DateTime(2026, 10, 1);
                return f;
            }));
            MesesGuardados.Add(mes);
            return Task.CompletedTask;
        }

        public Task<List<FilaIndicadoresStock>> Leer(DateTime desde, DateTime hasta, string almacen)
        {
            return Task.FromResult(Tabla
                .Where(f => f.Mes >= desde && f.Mes <= hasta && (string.IsNullOrWhiteSpace(almacen) || f.Almacen == almacen))
                .OrderBy(f => f.Mes)
                .ToList());
        }

        /// <summary>Lo que hace DATEDIFF(month, desde, fecha).</summary>
        private static int Meses(DateTime desde, DateTime fecha) => ((fecha.Year - desde.Year) * 12) + fecha.Month - desde.Month;
    }
}
