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
            var fechaHoy = DateTime.Today;
            var annoActual = fechaHoy.Year;
            var annoAnterior = fechaHoy.Year - 1;

            DateTime fechaDesdeActual, fechaDesdeAnterior, fechaHastaActual, fechaHastaAnterior;

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

            fechaDesdeAnterior = fechaDesdeActual.AddYears(-1);
            fechaHastaAnterior = fechaHastaActual.AddYears(-1);

            // Cargamos datos para el año actual y el anterior (ambos rangos)
            var datos = from l in _db.LinPedidoVtas
                        where (l.Empresa == "1" || l.Empresa == "3")
                              && l.Grupo != null
                              && l.Fecha_Albarán != null
                              && l.Nº_Cliente == clienteId
                              && l.Fecha_Albarán >= fechaDesdeAnterior
                        join f in _db.Familias on new { l.Empresa, l.Familia }
                            equals new { f.Empresa, Familia = f.Número }
                        join g in _db.GruposProductoes on new { l.Empresa, l.Grupo }
                            equals new { g.Empresa, Grupo = g.Número }
                            // LEFT JOIN Subgrupo
                        join s in _db.SubGruposProductoes.Where(x => x.Empresa == "1")
                            on new { l.Grupo, l.SubGrupo }
                            equals new { s.Grupo, SubGrupo = s.Número } into subgrupos
                        from s in subgrupos.DefaultIfEmpty()
                            // LEFT JOIN Producto
                        join p in _db.Productos.Where(x => x.Empresa == "1")
                            on l.Producto equals p.Número into productos
                        from p in productos.DefaultIfEmpty()
                        select new
                        {
                            Fecha = l.Fecha_Albarán.Value,
                            Base = l.Base_Imponible,
                            Grupo = g.Descripción,
                            Subgrupo = s != null ? s.Descripción : "Sin subgrupo",
                            Familia = f.Descripción
                        };

            // Aplicamos lógica de comparativa
            var agrupado = datos.ToList()
                .Where(x => x.Fecha <= fechaHastaActual) // excluye posibles fechas futuras
                .GroupBy(x =>
                {
                    string clave = agruparPor == "grupo" ? x.Grupo : agruparPor == "subgrupo" ? x.Subgrupo : agruparPor == "familia" ? x.Familia : "Otro";
                    return clave.Trim();
                })
                .Select(g =>
                {
                    var ventaActual = g
                        .Where(x => x.Fecha >= fechaDesdeActual && x.Fecha <= fechaHastaActual)
                        .Sum(x => x.Base);

                    var ventaAnterior = g
                        .Where(x => x.Fecha >= fechaDesdeAnterior && x.Fecha <= fechaHastaAnterior)
                        .Sum(x => x.Base);

                    return new ComparativaVentaDto
                    {
                        Nombre = g.Key,
                        VentaAnnoActual = ventaActual,
                        VentaAnnoAnterior = ventaAnterior
                    };
                })
                .OrderByDescending(x => x.VentaAnnoActual + x.VentaAnnoAnterior)
                .ToList();

            return new ComparativaVentasResponseDto
            {
                FechaDesdeActual = fechaDesdeActual,
                FechaHastaActual = fechaHastaActual,
                FechaDesdeAnterior = fechaDesdeAnterior,
                FechaHastaAnterior = fechaHastaAnterior,
                Datos = agrupado
            };
        }
    }

}
