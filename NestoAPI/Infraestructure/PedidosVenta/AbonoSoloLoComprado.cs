using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#891: lo que un cliente tiene facturado y abonado de un producto (sin límite de tiempo).</summary>
    public class CompradoParaAbono
    {
        public string Producto { get; set; }
        /// <summary>Unidades facturadas al cliente (líneas en factura con cantidad positiva).</summary>
        public int Facturadas { get; set; }
        /// <summary>Unidades ya abonadas en factura (líneas en factura con cantidad negativa, en positivo).</summary>
        public int Abonadas { get; set; }
        /// <summary>Unidades negativas de OTROS pedidos del cliente aún sin facturar (-1..2): abonos ya en marcha.</summary>
        public int AbonadasSinFacturar { get; set; }
    }

    /// <summary>NestoAPI#891: una línea de abono (cantidad negativa de un producto) del pedido.</summary>
    public class LineaAbonoProducto
    {
        public string Producto { get; set; }
        /// <summary>Las unidades que abona, en positivo.</summary>
        public int Unidades { get; set; }
    }

    /// <summary>NestoAPI#891: un producto que se abona por encima de lo que el cliente tiene comprado.</summary>
    public class FaltaAbonoProducto
    {
        public string Producto { get; set; }
        public int Abona { get; set; }
        public int Facturadas { get; set; }
        public int Abonadas { get; set; }
        public int AbonadasSinFacturar { get; set; }
        public int Disponibles => Math.Max(0, Facturadas - Abonadas - AbonadasSinFacturar);
        public string Texto { get; set; }
    }

    /// <summary>
    /// NestoAPI#891 (Carlos 24/11/26, aviso de Verifactu de la RV2600189): «No podemos dejar hacer un abono de algo que no nos ha comprado».
    /// LA REGLA (pura, lo DECIDIDO en la issue): una línea de abono (cantidad negativa de un producto) solo cabe si el cliente tiene
    /// facturado ese producto en cantidad suficiente, descontando lo ya abonado (en factura y en otros pedidos aún sin facturar), sin
    /// límite de tiempo. Lo que la issue deja «por definir» (dónde frena, cliente o cliente + contacto, empresas, excepciones y quién se
    /// la salta) NO está decidido: hoy la regla solo se mide EN SOMBRA (<see cref="SombraAbonoSoloLoComprado"/>), sin frenar nada.
    /// </summary>
    public static class ReglaAbonoSoloLoComprado
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>
        /// Las líneas de abono del pedido que mira la regla, sumadas por producto: de producto, cantidad negativa, sin facturar ni en
        /// presupuesto (-1..2). Fuera las del cupón y las de cheque regalo (son descuentos, no devoluciones). Las cuentas contables y
        /// los textos no son de producto.
        /// </summary>
        public static List<LineaAbonoProducto> LineasAbono(IEnumerable<LineaPedidoVentaDTO> lineas, Func<string, bool> esDescuento)
        {
            return (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad < 0
                    && !string.IsNullOrWhiteSpace(l.Producto)
                    && l.estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.estado <= Constantes.EstadosLineaVenta.ALBARAN
                    && (esDescuento == null || !esDescuento(l.Producto.Trim())))
                .GroupBy(l => l.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new LineaAbonoProducto { Producto = g.Key, Unidades = -g.Sum(l => l.Cantidad) })
                .ToList();
        }

        /// <summary>Los productos que se abonan por encima de lo comprado; vacía si todo cabe.</summary>
        public static List<FaltaAbonoProducto> Evaluar(IEnumerable<LineaAbonoProducto> abonos, IEnumerable<CompradoParaAbono> comprado)
        {
            Dictionary<string, CompradoParaAbono> porProducto = (comprado ?? Enumerable.Empty<CompradoParaAbono>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Producto))
                .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new CompradoParaAbono
                {
                    Producto = g.Key,
                    Facturadas = g.Sum(c => c.Facturadas),
                    Abonadas = g.Sum(c => c.Abonadas),
                    AbonadasSinFacturar = g.Sum(c => c.AbonadasSinFacturar)
                }, StringComparer.OrdinalIgnoreCase);
            var faltas = new List<FaltaAbonoProducto>();
            foreach (LineaAbonoProducto abono in (abonos ?? Enumerable.Empty<LineaAbonoProducto>()).Where(a => a != null && a.Unidades > 0))
            {
                porProducto.TryGetValue(abono.Producto.Trim(), out CompradoParaAbono c);
                var falta = new FaltaAbonoProducto
                {
                    Producto = abono.Producto.Trim(),
                    Abona = abono.Unidades,
                    Facturadas = c?.Facturadas ?? 0,
                    Abonadas = c?.Abonadas ?? 0,
                    AbonadasSinFacturar = c?.AbonadasSinFacturar ?? 0
                };
                if (falta.Abona <= falta.Facturadas - falta.Abonadas - falta.AbonadasSinFacturar)
                {
                    continue;
                }
                falta.Texto = Texto(falta);
                faltas.Add(falta);
            }
            return faltas;
        }

        /// <summary>«Del 41752 este cliente tiene facturadas 3 y ya abonadas 3: no se pueden abonar 1 más.»</summary>
        public static string Texto(FaltaAbonoProducto f)
        {
            string facturadas = f.Facturadas == 0 ? "no tiene ninguna facturada" : $"tiene facturadas {N(f.Facturadas)}";
            string abonadas = f.Abonadas > 0 ? $" y ya abonadas {N(f.Abonadas)}" : string.Empty;
            string enMarcha = f.AbonadasSinFacturar > 0 ? $" ({N(f.AbonadasSinFacturar)} más en otros pedidos sin facturar)" : string.Empty;
            string cuantas = f.Disponibles == 0 ? $"no se puede abonar {UnidadOUnidades(f.Abona)}" : $"solo se pueden abonar {N(f.Disponibles)} de {N(f.Abona)}";
            return $"Del {f.Producto} este cliente {facturadas}{abonadas}{enMarcha}: {cuantas}.";
        }

        public static string Mensaje(IEnumerable<FaltaAbonoProducto> faltas)
        {
            List<FaltaAbonoProducto> lista = (faltas ?? Enumerable.Empty<FaltaAbonoProducto>()).ToList();
            return lista.Any()
                ? "No se puede abonar lo que el cliente no ha comprado. " + string.Join(" ", lista.Select(f => f.Texto ?? Texto(f)))
                : null;
        }

        private static string N(int n) => n.ToString("0", es);
        private static string UnidadOUnidades(int n) => n == 1 ? "1 unidad" : $"{N(n)} unidades";
    }

    /// <summary>NestoAPI#891: lo comprado y abonado por el cliente de esos productos. Sustituible en los tests.</summary>
    public interface ILectorCompradoParaAbono
    {
        List<CompradoParaAbono> Leer(IEnumerable<string> empresas, string cliente, int pedidoQueSeGuarda, IEnumerable<string> productos);
    }

    /// <summary>NestoAPI#891: UNA consulta de solo lectura sobre LinPedidoVta (factura = Estado 4), sin límite de tiempo.</summary>
    public class LectorCompradoParaAbonoSql : ILectorCompradoParaAbono
    {
        internal const string SQL = @"
SELECT RTRIM(l.Producto) AS Producto,
    CAST(ISNULL(SUM(CASE WHEN l.Estado = 4 AND l.Cantidad > 0 THEN l.Cantidad ELSE 0 END), 0) AS int) AS Facturadas,
    CAST(ISNULL(SUM(CASE WHEN l.Estado = 4 AND l.Cantidad < 0 THEN -l.Cantidad ELSE 0 END), 0) AS int) AS Abonadas,
    CAST(ISNULL(SUM(CASE WHEN l.Estado BETWEEN -1 AND 2 AND l.Cantidad < 0 AND l.Número <> @pedido THEN -l.Cantidad ELSE 0 END), 0) AS int) AS AbonadasSinFacturar
FROM dbo.LinPedidoVta l WITH (NOLOCK)
WHERE l.[Nº Cliente] = @cliente AND l.TipoLinea = 1 AND l.Empresa IN ({0}) AND l.Producto IN ({1})
GROUP BY RTRIM(l.Producto)";

        private readonly NVEntities db;

        public LectorCompradoParaAbonoSql(NVEntities db)
        {
            this.db = db;
        }

        public List<CompradoParaAbono> Leer(IEnumerable<string> empresas, string cliente, int pedidoQueSeGuarda, IEnumerable<string> productos)
        {
            List<string> listaEmpresas = (empresas ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToList();
            List<string> listaProductos = (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            if (!listaEmpresas.Any() || !listaProductos.Any() || string.IsNullOrWhiteSpace(cliente))
            {
                return new List<CompradoParaAbono>();
            }
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@cliente", cliente.Trim()),
                new SqlParameter("@pedido", pedidoQueSeGuarda)
            };
            parametros.AddRange(listaEmpresas.Select((e, i) => new SqlParameter($"@e{i}", e)));
            parametros.AddRange(listaProductos.Select((p, i) => new SqlParameter($"@p{i}", p)));
            string sql = string.Format(SQL,
                string.Join(", ", listaEmpresas.Select((e, i) => $"@e{i}")),
                string.Join(", ", listaProductos.Select((p, i) => $"@p{i}")));
            return db.Database.SqlQuery<CompradoParaAbono>(sql, parametros.Cast<object>().ToArray()).ToList();
        }
    }

    /// <summary>
    /// NestoAPI#891, EN SOMBRA (mientras no se decide dónde frena): tras guardar un pedido con líneas de abono, si alguna pasa de lo
    /// comprado, una línea en ELMAH con el pedido, el usuario y el texto de la regla. Mide sobre el cliente entero y las empresas 1 y 3
    /// (la lectura más amplia: solo apunta lo que no cabría de ninguna manera). Nunca lanza, nunca frena ni cambia el pedido; sin líneas
    /// de abono no lee nada.
    /// </summary>
    public static class SombraAbonoSoloLoComprado
    {
        internal static readonly string[] EMPRESAS = { Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO };

        /// <returns>Las faltas apuntadas (vacía si nada o si falla), para los tests.</returns>
        public static List<FaltaAbonoProducto> Apuntar(PedidoVentaDTO pedido, Func<string, bool> esDescuento, ILectorCompradoParaAbono lector,
            Action<Exception> apuntar)
        {
            try
            {
                if (pedido == null || lector == null)
                {
                    return new List<FaltaAbonoProducto>();
                }
                List<LineaAbonoProducto> abonos = ReglaAbonoSoloLoComprado.LineasAbono(pedido.Lineas, esDescuento);
                if (!abonos.Any())
                {
                    return new List<FaltaAbonoProducto>();
                }
                List<CompradoParaAbono> comprado = lector.Leer(EMPRESAS, pedido.cliente, pedido.numero, abonos.Select(a => a.Producto));
                List<FaltaAbonoProducto> faltas = ReglaAbonoSoloLoComprado.Evaluar(abonos, comprado);
                if (faltas.Any())
                {
                    apuntar?.Invoke(new Exception($"[Abono de lo no comprado #891, sombra] Pedido {pedido.empresa?.Trim()}/{pedido.numero} del cliente " +
                        $"{pedido.cliente?.Trim()}/{pedido.contacto?.Trim()} (usuario {pedido.Usuario}): {ReglaAbonoSoloLoComprado.Mensaje(faltas)}"));
                }
                return faltas;
            }
            catch (Exception ex)
            {
                try
                {
                    apuntar?.Invoke(new Exception($"[Abono de lo no comprado #891, sombra] No se ha podido comprobar el pedido {pedido?.numero}: {ex.Message}", ex));
                }
                catch
                {
                    // La sombra nunca rompe el guardado
                }
                return new List<FaltaAbonoProducto>();
            }
        }
    }
}
