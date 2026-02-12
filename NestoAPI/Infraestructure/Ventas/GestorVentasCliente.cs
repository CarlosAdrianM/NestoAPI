using NestoAPI.Models;
using NestoAPI.Models.Ventas;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure.Ventas
{
    public class GestorVentasCliente
    {
        private readonly NVEntities _db;

        public GestorVentasCliente(NVEntities db)
        {
            _db = db;
        }

        public ComparativaVentasResponseDto ObtenerComparativaVentas(string clienteId, string modoComparativa, string agruparPor)
        {
            var fechas = CalcularRangoFechas(modoComparativa);

            var datos = ObtenerDatosBaseVentas(clienteId, fechas.FechaDesdeAnterior);

            var agrupado = datos.ToList()
                .Where(x => x.Fecha <= fechas.FechaHastaActual)
                .GroupBy(x =>
                {
                    string clave = agruparPor == "grupo" ? x.Grupo : agruparPor == "subgrupo" ? x.Subgrupo : agruparPor == "familia" ? x.Familia : "Otro";
                    return clave.Trim();
                })
                .Select(g =>
                {
                    var lineasActuales = g
                        .Where(x => x.Fecha >= fechas.FechaDesdeActual && x.Fecha <= fechas.FechaHastaActual);

                    var lineasAnteriores = g
                        .Where(x => x.Fecha >= fechas.FechaDesdeAnterior && x.Fecha <= fechas.FechaHastaAnterior);

                    return new ComparativaVentaDto
                    {
                        Nombre = g.Key,
                        VentaAnnoActual = lineasActuales.Sum(x => x.Base),
                        VentaAnnoAnterior = lineasAnteriores.Sum(x => x.Base),
                        UnidadesAnnoActual = lineasActuales.Sum(x => x.Cantidad),
                        UnidadesAnnoAnterior = lineasAnteriores.Sum(x => x.Cantidad)
                    };
                })
                .OrderByDescending(x => x.VentaAnnoActual + x.VentaAnnoAnterior)
                .ToList();

            return new ComparativaVentasResponseDto
            {
                FechaDesdeActual = fechas.FechaDesdeActual,
                FechaHastaActual = fechas.FechaHastaActual,
                FechaDesdeAnterior = fechas.FechaDesdeAnterior,
                FechaHastaAnterior = fechas.FechaHastaAnterior,
                Datos = agrupado
            };
        }

        public ComparativaVentasResponseDto ObtenerDetalleVentasProducto(string clienteId, string filtro, string modoComparativa, string agruparPor)
        {
            var fechas = CalcularRangoFechas(modoComparativa);

            var datos = ObtenerDatosBaseVentas(clienteId, fechas.FechaDesdeAnterior);

            var datosFiltrados = datos.ToList()
                .Where(x => x.Fecha <= fechas.FechaHastaActual)
                .Where(x =>
                {
                    string campo = agruparPor == "grupo" ? x.Grupo : agruparPor == "subgrupo" ? x.Subgrupo : x.Familia;
                    return campo != null && campo.Trim() == filtro.Trim();
                });

            var agrupado = datosFiltrados
                .GroupBy(x => new { ProductoId = x.ProductoId.Trim(), x.ProductoNombre })
                .Select(g =>
                {
                    var lineasActuales = g
                        .Where(x => x.Fecha >= fechas.FechaDesdeActual && x.Fecha <= fechas.FechaHastaActual);

                    var lineasAnteriores = g
                        .Where(x => x.Fecha >= fechas.FechaDesdeAnterior && x.Fecha <= fechas.FechaHastaAnterior);

                    return new ComparativaVentaDto
                    {
                        Nombre = g.Key.ProductoId + " - " + (g.Key.ProductoNombre ?? "").Trim(),
                        VentaAnnoActual = lineasActuales.Sum(x => x.Base),
                        VentaAnnoAnterior = lineasAnteriores.Sum(x => x.Base),
                        UnidadesAnnoActual = lineasActuales.Sum(x => x.Cantidad),
                        UnidadesAnnoAnterior = lineasAnteriores.Sum(x => x.Cantidad)
                    };
                })
                .OrderByDescending(x => x.VentaAnnoActual + x.VentaAnnoAnterior)
                .ToList();

            return new ComparativaVentasResponseDto
            {
                FechaDesdeActual = fechas.FechaDesdeActual,
                FechaHastaActual = fechas.FechaHastaActual,
                FechaDesdeAnterior = fechas.FechaDesdeAnterior,
                FechaHastaAnterior = fechas.FechaHastaAnterior,
                Datos = agrupado
            };
        }

        internal RangoFechas CalcularRangoFechas(string modoComparativa)
        {
            var fechaHoy = DateTime.Today;
            DateTime fechaDesdeActual, fechaHastaActual;

            if (modoComparativa == "ultimos12meses")
            {
                fechaDesdeActual = fechaHoy.AddYears(-1).Date.AddDays(1);
                fechaHastaActual = fechaHoy.Date;
            }
            else // "anual"
            {
                fechaDesdeActual = new DateTime(fechaHoy.Year, 1, 1);
                fechaHastaActual = fechaHoy.Date;
            }

            return new RangoFechas
            {
                FechaDesdeActual = fechaDesdeActual,
                FechaHastaActual = fechaHastaActual,
                FechaDesdeAnterior = fechaDesdeActual.AddYears(-1),
                FechaHastaAnterior = fechaHastaActual.AddYears(-1)
            };
        }

        private IQueryable<DatosVentaLinea> ObtenerDatosBaseVentas(string clienteId, DateTime fechaDesdeAnterior)
        {
            return from l in _db.LinPedidoVtas
                   where (l.Empresa == "1" || l.Empresa == "3")
                         && l.Grupo != null
                         && l.Fecha_Albarán != null
                         && l.Nº_Cliente == clienteId
                         && l.Fecha_Albarán >= fechaDesdeAnterior
                   join f in _db.Familias on new { l.Empresa, l.Familia }
                       equals new { f.Empresa, Familia = f.Número }
                   join g in _db.GruposProductoes on new { l.Empresa, l.Grupo }
                       equals new { g.Empresa, Grupo = g.Número }
                   join s in _db.SubGruposProductoes.Where(x => x.Empresa == "1")
                       on new { l.Grupo, l.SubGrupo }
                       equals new { s.Grupo, SubGrupo = s.Número } into subgrupos
                   from s in subgrupos.DefaultIfEmpty()
                   join p in _db.Productos.Where(x => x.Empresa == "1")
                       on l.Producto equals p.Número into productos
                   from p in productos.DefaultIfEmpty()
                   select new DatosVentaLinea
                   {
                       Fecha = l.Fecha_Albarán.Value,
                       Base = l.Base_Imponible,
                       Cantidad = l.Cantidad ?? 0,
                       Grupo = g.Descripción,
                       Subgrupo = s != null ? s.Descripción : "Sin subgrupo",
                       Familia = f.Descripción,
                       ProductoId = l.Producto,
                       ProductoNombre = p != null ? p.Nombre : ""
                   };
        }

        internal class RangoFechas
        {
            public DateTime FechaDesdeActual { get; set; }
            public DateTime FechaHastaActual { get; set; }
            public DateTime FechaDesdeAnterior { get; set; }
            public DateTime FechaHastaAnterior { get; set; }
        }

        internal class DatosVentaLinea
        {
            public DateTime Fecha { get; set; }
            public decimal Base { get; set; }
            public short Cantidad { get; set; }
            public string Grupo { get; set; }
            public string Subgrupo { get; set; }
            public string Familia { get; set; }
            public string ProductoId { get; set; }
            public string ProductoNombre { get; set; }
        }
    }
}
