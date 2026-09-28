using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NestoAPI.Infraestructure.PreciosMedios;
using Newtonsoft.Json;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Fixture de un producto real para los tests de caracterización del precio medio (Issue #547, §3).
    ///
    /// Sacada de producción en SOLO LECTURA el 28/09/26 con la consulta del plan (líneas de LinPedidoCmp
    /// TipoLínea 1 de E + espejo, stock de ExtractoProducto a cada fecha de albarán, Productos.PrecioMedio) y
    /// ajustada para representar el estado que dejó el SP el domingo 27/09/26 (ejecución de 07:10 a 12:09):
    /// las compras facturadas después (45396) figuran como estaban ese día. Cada JSON lleva su «nota».
    ///
    /// Los ficheros van como EmbeddedResource en NestoAPI.Tests.csproj (Fixtures\*.json).
    /// </summary>
    public sealed class FixturePrecioMedio
    {
        /// <summary>
        /// Coste de entrada que se pone a las líneas facturadas: la calculadora las recalcula antes de leerlas,
        /// así que si alguna vez usara este valor el resultado saltaría a la vista.
        /// </summary>
        public const decimal COSTE_CENTINELA = 9999.9999m;

        public string Producto { get; set; }
        public string Empresa { get; set; }
        public string EmpresaEspejo { get; set; }
        public string Nota { get; set; }
        public bool Ficticio { get; set; }
        public DateTime? FechaUltimoMontaje { get; set; }
        public DateTime? FechaUltimaRecepcion { get; set; }
        public MotivoPrecioMedioNoProcesado MotivoEsperado { get; set; }

        /// <summary>Productos.PrecioMedio que dejó el SP el 27/09/26.</summary>
        public decimal PrecioMedioSP { get; set; }

        /// <summary>Líneas de compra; su «coste» es el LinPedidoCmp.Coste que dejó el SP el 27/09/26.</summary>
        public List<LineaCompraPrecioMedio> Compras { get; set; }

        public List<StockFecha> StockHasta { get; set; }

        public sealed class StockFecha
        {
            public DateTime Fecha { get; set; }
            public int Stock { get; set; }
        }

        public static FixturePrecioMedio Cargar(string producto)
        {
            Assembly ensamblado = typeof(FixturePrecioMedio).Assembly;
            string nombre = ensamblado.GetManifestResourceNames()
                .SingleOrDefault(n => n.EndsWith(".PreciosMedios.Fixtures." + producto + ".json", StringComparison.Ordinal));
            if (nombre == null)
            {
                throw new InvalidOperationException("No está la fixture del producto " + producto + " (¿falta el EmbeddedResource?)");
            }
            using (Stream flujo = ensamblado.GetManifestResourceStream(nombre))
            using (StreamReader lector = new StreamReader(flujo))
            {
                return JsonConvert.DeserializeObject<FixturePrecioMedio>(lector.ReadToEnd(),
                    new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Unspecified });
            }
        }

        /// <summary>Líneas que el SP recalcula: facturadas (estado 4, con factura y fecha de albarán).</summary>
        public static bool EsLineaFacturada(LineaCompraPrecioMedio linea)
        {
            return linea.Estado == 4 && linea.NumeroFactura.HasValue && linea.FechaAlbaran.HasValue;
        }

        /// <summary>
        /// Historial para la calculadora. Las líneas facturadas llevan <see cref="COSTE_CENTINELA"/> como coste de
        /// entrada (se tienen que recalcular); las demás, el coste real (el SP lo lee tal cual como media anterior).
        /// El stock a una fecha que no esté en la fixture es un error de la fixture, no un 0.
        /// </summary>
        public HistorialPrecioMedio CrearHistorial()
        {
            Dictionary<DateTime, int> stock = StockHasta.ToDictionary(s => s.Fecha, s => s.Stock);
            return new HistorialPrecioMedio
            {
                Empresa = Empresa,
                EmpresaEspejo = EmpresaEspejo,
                Producto = Producto,
                Ficticio = Ficticio,
                FechaUltimoMontaje = FechaUltimoMontaje,
                FechaUltimaRecepcion = FechaUltimaRecepcion,
                Compras = Compras.Select(l => new LineaCompraPrecioMedio
                {
                    Empresa = l.Empresa,
                    NumeroFactura = l.NumeroFactura,
                    FechaAlbaran = l.FechaAlbaran,
                    NumeroAlbaran = l.NumeroAlbaran,
                    NumeroOrden = l.NumeroOrden,
                    Cantidad = l.Cantidad,
                    BaseImponible = l.BaseImponible,
                    Coste = EsLineaFacturada(l) ? COSTE_CENTINELA : l.Coste,
                    Estado = l.Estado
                }).ToList(),
                StockHasta = fecha =>
                {
                    if (!stock.TryGetValue(fecha, out int cantidad))
                    {
                        throw new InvalidOperationException("La fixture de " + Producto + " no tiene el stock a " + fecha.ToString("o"));
                    }
                    return cantidad;
                }
            };
        }
    }
}
