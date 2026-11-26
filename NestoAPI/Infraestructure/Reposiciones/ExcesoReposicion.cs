using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // Sugerencia 1429 (Manuel, Compras, y Carlos, 26/11/26): una tienda con exceso de stock de una familia (los esmaltes de
    // Faby en Alcobendas) lo devuelve a Algete con una reposición tienda → ALG sin ir producto a producto.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>POST api/Reposiciones/Exceso: de qué tienda, a dónde y qué productos se miran (al menos un filtro).</summary>
    public class PeticionExcesoReposicionDTO
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        /// <summary>La tienda que devuelve: REI o ALC.</summary>
        public string Origen { get; set; }
        /// <summary>Opcional: por defecto Algete.</summary>
        public string Destino { get; set; }
        /// <summary>Código o nombre de la familia (sin distinguir mayúsculas ni espacios de relleno).</summary>
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public List<string> Productos { get; set; }
        /// <summary>
        /// Sugerencia 1429 (Manuel, 26/11/26): el nombre del producto contiene este texto (sin mayúsculas; varias palabras = todas,
        /// en cualquier orden). También vale como filtro de selección.
        /// </summary>
        public string TextoNombre { get; set; }
        /// <summary>El nombre NO contiene este texto (p. ej. «PERMANENTE»; varias palabras = fuera si tiene cualquiera).</summary>
        public string TextoNombreExcluir { get; set; }
        /// <summary>Solo productos con Stock &gt; este número (el stock de la línea: ya sin lo comprometido en otra reposición).</summary>
        public int? StockMinimo { get; set; }
        /// <summary>1..60: solo productos sin ninguna venta (albarán o factura, cantidad &gt; 0) en la tienda de origen en esos meses.</summary>
        public int? SinVentasEnMeses { get; set; }
        /// <summary>Si viene, sustituye al máximo de la ficha: exceso = Stock − DejarEnTienda (con el mismo tope del pendiente).</summary>
        public int? DejarEnTienda { get; set; }
    }

    public class LineaExcesoReposicionDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Código de la familia (Productos.Familia).</summary>
        public string Familia { get; set; }
        public string NombreFamilia { get; set; }
        /// <summary>Stock en la tienda: ExtractoProducto menos lo que ya está comprometido para salir en otra reposición.</summary>
        public int Stock { get; set; }
        /// <summary>Lo que ya está metido en otra reposición de salida sin contabilizar (ya descontado de <see cref="Stock"/>).</summary>
        public int EnOtraReposicion { get; set; }
        /// <summary>Pedidos de venta pendientes (-1..1) de ese producto en la tienda.</summary>
        public int Pendiente { get; set; }
        /// <summary>El «Stock Máximo (actual)» de la ficha para la tienda (ControlesStock); 0 si no tiene fila.</summary>
        public int StockMaximo { get; set; }
        /// <summary>Sin fila en ControlesStock para la tienda: se trata como máximo 0 (se devuelve entero).</summary>
        public bool SinMaximo { get; set; }
        public int Exceso { get; set; }
        /// <summary>
        /// Solo cuando se pide <see cref="PeticionExcesoReposicionDTO.SinVentasEnMeses"/>: la última venta del producto en la tienda
        /// (fecha del albarán). Null si no consta ninguna (o no se pidió).
        /// </summary>
        public DateTime? UltimaVenta { get; set; }
    }

    public class ResultadoExcesoReposicionDTO
    {
        public bool Ensayo { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public List<LineaExcesoReposicionDTO> Lineas { get; set; } = new List<LineaExcesoReposicionDTO>();
        public int NumeroLineas { get; set; }
        public int TotalUnidades { get; set; }
        /// <summary>Cuántas de las líneas son de productos sin máximo puesto en la tienda.</summary>
        public int LineasSinMaximo { get; set; }
        /// <summary>De verdad: la reposición creada (la misma respuesta que POST api/Reposiciones). Null en el ensayo o sin exceso.</summary>
        public ReposicionEnPreparacionDTO Reposicion { get; set; }
        public string Mensaje { get; set; }
        /// <summary>Los filtros aplicados, en una línea de texto («Filtros: familia FABY; el nombre lleva «ESMALTE»; …»).</summary>
        public string Filtros { get; set; }
    }

    /// <summary>Una fila de la lectura: producto con stock en la tienda y sus cifras.</summary>
    public class FilaExcesoReposicion
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string NombreFamilia { get; set; }
        public int Stock { get; set; }
        public int EnOtraReposicion { get; set; }
        public int Pendiente { get; set; }
        /// <summary>Null: el producto no tiene fila en ControlesStock para la tienda.</summary>
        public int? StockMaximo { get; set; }
        /// <summary>Solo si se pidió «sin ventas»: la fecha del último albarán de venta en la tienda. Null = ninguna.</summary>
        public DateTime? UltimaVenta { get; set; }
    }

    /// <summary>El filtro ya normalizado (mayúsculas, sin espacios; null = no filtra).</summary>
    public class FiltroExcesoReposicion
    {
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public List<string> Productos { get; set; } = new List<string>();
        /// <summary>Palabras que tiene que llevar el nombre (todas), en mayúsculas.</summary>
        public List<string> PalabrasNombre { get; set; } = new List<string>();
        /// <summary>Palabras que el nombre no puede llevar (ninguna), en mayúsculas.</summary>
        public List<string> PalabrasExcluir { get; set; } = new List<string>();
        public int? StockMinimo { get; set; }
        public int? SinVentasEnMeses { get; set; }
        public int? DejarEnTienda { get; set; }
    }

    /// <summary>
    /// LA REGLA (pura) del exceso de una tienda. Exceso = min(Stock − StockMaximo, Stock − Pendiente), solo si es > 0. Sin fila
    /// de control de stock = máximo 0 (Carlos 26/11/26: entra todo lo disponible, marcado <c>SinMaximo</c>). Ficticios fuera (lo
    /// filtra la lectura); lo anulado NO se excluye (lo anulado con stock es justo lo que conviene devolver).
    /// </summary>
    public static class ReglaExcesoReposicion
    {
        public const int MAXIMO_LINEAS = 500;
        public const int MAXIMO_PRODUCTOS_PETICION = 1000;
        public const int MAXIMO_MESES_SIN_VENTAS = 60;
        public const int MAXIMO_PALABRAS = 10;

        internal static readonly string[] TIENDAS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        internal static readonly string[] ALMACENES_DESTINO =
        {
            Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS
        };

        /// <summary>Valida y normaliza la petición. NestoBusinessException (400) con el motivo, de usted.</summary>
        public static FiltroExcesoReposicion Normalizar(PeticionExcesoReposicionDTO peticion)
        {
            if (peticion == null)
            {
                throw new NestoBusinessException("Faltan los datos: la tienda de origen y qué productos (familia, grupo, subgrupo, texto del nombre o lista de productos).");
            }
            string origen = Limpio(peticion.Origen);
            if (origen == null || !TIENDAS.Contains(origen))
            {
                throw new NestoBusinessException($"El exceso solo se devuelve desde una tienda ({string.Join(" o ", TIENDAS)}).");
            }
            string destino = Limpio(peticion.Destino) ?? Constantes.Almacenes.ALGETE;
            if (!ALMACENES_DESTINO.Contains(destino))
            {
                throw new NestoBusinessException($"El destino tiene que ser {string.Join(", ", ALMACENES_DESTINO)}.");
            }
            if (destino == origen)
            {
                throw new NestoBusinessException("El almacén de origen y el de destino no pueden ser el mismo.");
            }
            List<string> productos = (peticion.Productos ?? new List<string>())
                .Select(Limpio)
                .Where(p => p != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var filtro = new FiltroExcesoReposicion
            {
                Empresa = Limpio(peticion.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Origen = origen,
                Destino = destino,
                Familia = Limpio(peticion.Familia),
                Grupo = Limpio(peticion.Grupo),
                Subgrupo = Limpio(peticion.Subgrupo),
                Productos = productos,
                PalabrasNombre = Palabras(peticion.TextoNombre),
                PalabrasExcluir = Palabras(peticion.TextoNombreExcluir),
                StockMinimo = peticion.StockMinimo,
                SinVentasEnMeses = peticion.SinVentasEnMeses,
                DejarEnTienda = peticion.DejarEnTienda
            };
            if (filtro.Familia == null && filtro.Grupo == null && filtro.Subgrupo == null && !productos.Any() && !filtro.PalabrasNombre.Any())
            {
                throw new NestoBusinessException("Indique qué productos quiere devolver: una familia, un grupo, un subgrupo, un texto del nombre o una lista de productos.");
            }
            if (filtro.PalabrasNombre.Count + filtro.PalabrasExcluir.Count > MAXIMO_PALABRAS)
            {
                throw new NestoBusinessException($"Como mucho {MAXIMO_PALABRAS} palabras entre el texto del nombre y el que se excluye.");
            }
            if (filtro.StockMinimo < 0)
            {
                throw new NestoBusinessException("El stock mínimo no puede ser negativo.");
            }
            if (filtro.SinVentasEnMeses.HasValue && (filtro.SinVentasEnMeses < 1 || filtro.SinVentasEnMeses > MAXIMO_MESES_SIN_VENTAS))
            {
                throw new NestoBusinessException($"Los meses sin ventas tienen que ir de 1 a {MAXIMO_MESES_SIN_VENTAS}.");
            }
            if (filtro.DejarEnTienda < 0)
            {
                throw new NestoBusinessException("Lo que se deja en la tienda no puede ser negativo.");
            }
            if (productos.Count > MAXIMO_PRODUCTOS_PETICION)
            {
                throw new NestoBusinessException($"Ha indicado {productos.Count} productos: como mucho {MAXIMO_PRODUCTOS_PETICION} por petición.");
            }
            return filtro;
        }

        /// <summary>Exceso de un producto: min(stock − máximo, stock − pendiente), nunca negativo. Sin máximo = 0.</summary>
        public static int Exceso(int stock, int pendiente, int? maximo)
        {
            int max = Math.Max(0, maximo ?? 0);
            int exceso = Math.Min(stock - max, stock - Math.Max(0, pendiente));
            return exceso > 0 ? exceso : 0;
        }

        /// <summary>Las líneas con exceso &gt; 0, ordenadas por familia y nombre. 400 si pasan de <see cref="MAXIMO_LINEAS"/>.</summary>
        public static List<LineaExcesoReposicionDTO> Calcular(IEnumerable<FilaExcesoReposicion> filas)
        {
            return Calcular(filas, null, DateTime.Today);
        }

        /// <summary>
        /// Lo mismo con los filtros de la sugerencia 1429 (Y lógico): texto del nombre (todas las palabras), texto excluido (ninguna),
        /// stock mínimo (Stock &gt; N), sin ventas en N meses (última venta anterior a <paramref name="hoy"/> − N meses, o ninguna) y
        /// «dejar en la tienda», que sustituye al máximo de la ficha.
        /// </summary>
        public static List<LineaExcesoReposicionDTO> Calcular(IEnumerable<FilaExcesoReposicion> filas, FiltroExcesoReposicion filtro, DateTime hoy)
        {
            int? dejar = filtro?.DejarEnTienda;
            bool conVentas = filtro?.SinVentasEnMeses != null;
            List<LineaExcesoReposicionDTO> lineas = (filas ?? Enumerable.Empty<FilaExcesoReposicion>())
                .Where(f => !string.IsNullOrWhiteSpace(f.Producto))
                .Where(f => PasaFiltros(f, filtro, hoy))
                .Select(f => new LineaExcesoReposicionDTO
                {
                    Producto = f.Producto.Trim(),
                    Nombre = f.Nombre?.Trim(),
                    Familia = f.Familia?.Trim(),
                    NombreFamilia = f.NombreFamilia?.Trim(),
                    Stock = f.Stock,
                    EnOtraReposicion = f.EnOtraReposicion,
                    Pendiente = f.Pendiente,
                    StockMaximo = Math.Max(0, f.StockMaximo ?? 0),
                    SinMaximo = !f.StockMaximo.HasValue,
                    Exceso = Exceso(f.Stock, f.Pendiente, dejar ?? f.StockMaximo),
                    UltimaVenta = conVentas ? f.UltimaVenta : null
                })
                .Where(l => l.Exceso > 0)
                .OrderBy(l => l.NombreFamilia ?? l.Familia ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.Nombre ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.Producto, StringComparer.Ordinal)
                .ToList();
            if (lineas.Count > MAXIMO_LINEAS)
            {
                throw new NestoBusinessException($"Saldrían {lineas.Count} referencias con exceso y como mucho se pueden mandar {MAXIMO_LINEAS} " +
                    "en una reposición: acote el filtro (por familia, grupo o subgrupo).");
            }
            return lineas;
        }

        /// <summary>¿Pasa la fila los filtros de nombre, stock y ventas? (Familia, grupo, subgrupo y productos los aplica la lectura.)</summary>
        public static bool PasaFiltros(FilaExcesoReposicion fila, FiltroExcesoReposicion filtro, DateTime hoy)
        {
            if (filtro == null)
            {
                return true;
            }
            string nombre = (fila.Nombre ?? string.Empty).ToUpperInvariant();
            if ((filtro.PalabrasNombre ?? new List<string>()).Any(p => !nombre.Contains(p)))
            {
                return false;
            }
            if ((filtro.PalabrasExcluir ?? new List<string>()).Any(p => nombre.Contains(p)))
            {
                return false;
            }
            if (filtro.StockMinimo.HasValue && fila.Stock <= filtro.StockMinimo.Value)
            {
                return false;
            }
            if (filtro.SinVentasEnMeses.HasValue && fila.UltimaVenta.HasValue
                && fila.UltimaVenta.Value.Date >= hoy.Date.AddMonths(-filtro.SinVentasEnMeses.Value))
            {
                return false;
            }
            return true;
        }

        /// <summary>Los filtros aplicados, en una línea, para la respuesta.</summary>
        public static string TextoFiltros(FiltroExcesoReposicion filtro)
        {
            if (filtro == null)
            {
                return null;
            }
            var partes = new List<string>();
            if (filtro.Familia != null)
            {
                partes.Add($"familia {filtro.Familia}");
            }
            if (filtro.Grupo != null)
            {
                partes.Add($"grupo {filtro.Grupo}");
            }
            if (filtro.Subgrupo != null)
            {
                partes.Add($"subgrupo {filtro.Subgrupo}");
            }
            if (filtro.Productos != null && filtro.Productos.Any())
            {
                partes.Add(filtro.Productos.Count == 1 ? $"el producto {filtro.Productos[0]}" : $"{filtro.Productos.Count} productos de la lista");
            }
            if (filtro.PalabrasNombre != null && filtro.PalabrasNombre.Any())
            {
                partes.Add("el nombre lleva " + Enumerar(filtro.PalabrasNombre, " y "));
            }
            if (filtro.PalabrasExcluir != null && filtro.PalabrasExcluir.Any())
            {
                partes.Add("el nombre no lleva " + Enumerar(filtro.PalabrasExcluir, " ni "));
            }
            if (filtro.StockMinimo.HasValue)
            {
                partes.Add($"más de {Unidades(filtro.StockMinimo.Value)} en la tienda");
            }
            if (filtro.SinVentasEnMeses.HasValue)
            {
                partes.Add(filtro.SinVentasEnMeses == 1
                    ? $"sin ventas en {NombreAlmacen(filtro.Origen)} en el último mes"
                    : $"sin ventas en {NombreAlmacen(filtro.Origen)} en los últimos {filtro.SinVentasEnMeses} meses");
            }
            partes.Add(filtro.DejarEnTienda.HasValue
                ? $"se dejan {Unidades(filtro.DejarEnTienda.Value)} de cada referencia en la tienda"
                : "se deja en la tienda el máximo de la ficha");
            return "Filtros: " + string.Join("; ", partes) + ".";
        }

        private static string Enumerar(List<string> palabras, string ultimo)
        {
            List<string> citadas = palabras.Select(p => $"«{p}»").ToList();
            return citadas.Count == 1
                ? citadas[0]
                : string.Join(", ", citadas.Take(citadas.Count - 1)) + ultimo + citadas.Last();
        }

        /// <summary>El resultado completo (sin la reposición creada).</summary>
        public static ResultadoExcesoReposicionDTO Resultado(FiltroExcesoReposicion filtro, IEnumerable<FilaExcesoReposicion> filas, bool ensayo)
        {
            return Resultado(filtro, filas, ensayo, DateTime.Today);
        }

        public static ResultadoExcesoReposicionDTO Resultado(FiltroExcesoReposicion filtro, IEnumerable<FilaExcesoReposicion> filas, bool ensayo, DateTime hoy)
        {
            List<LineaExcesoReposicionDTO> lineas = Calcular(filas, filtro, hoy);
            var resultado = new ResultadoExcesoReposicionDTO
            {
                Ensayo = ensayo,
                Origen = filtro.Origen,
                Destino = filtro.Destino,
                Lineas = lineas,
                NumeroLineas = lineas.Count,
                TotalUnidades = lineas.Sum(l => l.Exceso),
                // Con «dejar en la tienda» el máximo de la ficha no cuenta: ninguna se devuelve entera por no tenerlo.
                LineasSinMaximo = filtro.DejarEnTienda.HasValue ? 0 : lineas.Count(l => l.SinMaximo),
                Filtros = TextoFiltros(filtro)
            };
            resultado.Mensaje = Mensaje(resultado, NombreAlmacen(filtro.Origen), NombreAlmacen(filtro.Destino), creada: false);
            return resultado;
        }

        public static string Mensaje(ResultadoExcesoReposicionDTO r, string nombreOrigen, string nombreDestino, bool creada)
        {
            if (r.NumeroLineas == 0)
            {
                return $"No hay exceso en {nombreOrigen} de esos productos: no hay nada que mandar a {nombreDestino}.";
            }
            string cuanto = $"{Unidades(r.TotalUnidades)} de {Referencias(r.NumeroLineas)}";
            string sinMaximo = r.LineasSinMaximo == 0
                ? string.Empty
                : $" De ellas, {r.LineasSinMaximo} sin máximo puesto en la tienda, que se devuelven enteras.";
            return creada
                ? $"Se ha creado la reposición de {nombreOrigen} a {nombreDestino} con {cuanto}.{sinMaximo} Queda en preparación hasta que se envíe."
                : $"Se mandarían {cuanto} de {nombreOrigen} a {nombreDestino}.{sinMaximo}";
        }

        internal static string NombreAlmacen(string almacen)
        {
            switch (almacen)
            {
                case Constantes.Almacenes.ALGETE: return "Algete";
                case Constantes.Almacenes.REINA: return "Reina";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                default: return almacen;
            }
        }

        private static string Unidades(int n) => n == 1 ? "1 unidad" : $"{n.ToString("N0", new CultureInfo("es-ES"))} unidades";

        private static string Referencias(int n) => n == 1 ? "1 referencia" : $"{n.ToString("N0", new CultureInfo("es-ES"))} referencias";

        /// <summary>Las palabras de un texto, en mayúsculas y sin repetir.</summary>
        internal static List<string> Palabras(string texto)
        {
            return (texto ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string Limpio(string texto)
        {
            string t = texto?.Trim();
            return string.IsNullOrEmpty(t) ? null : t.ToUpperInvariant();
        }
    }

    /// <summary>La lectura del cálculo (una consulta).</summary>
    public interface ILectorExcesoReposicion
    {
        /// <summary>Los productos no ficticios del filtro con stock &gt; 0 en el origen, con su pendiente y su máximo.</summary>
        Task<List<FilaExcesoReposicion>> Leer(FiltroExcesoReposicion filtro);
    }

    /// <summary>
    /// UNA consulta de solo lectura (NOLOCK) con el MISMO criterio que prdRellenarReposicionStock2 (su bloque «forzar envío de
    /// excedentes de una familia», Scripts/Issue577_PropuestaReposicionSinBloqueo.sql): stock = ExtractoProducto del origen de la
    /// empresa, su espejo y la 4 menos lo comprometido para salir sin contabilizar (en preparación en el diario de salida del
    /// origen, o salida numerada); pendiente = LinPedidoVta -1..1 del origen de la empresa y su espejo (sin el pedido 724488);
    /// máximo = ControlesStock.StockMáximo de la empresa y el origen.
    /// </summary>
    public class LectorExcesoReposicionSql : ILectorExcesoReposicion
    {
        private readonly NVEntities db;

        public LectorExcesoReposicionSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string SQL_INICIO = @"
DECLARE @espejo char(3) = (SELECT [IVA por defecto] FROM Empresas WITH (NOLOCK) WHERE [Número] = @empresa);
WITH candidatos AS (
    SELECT p.[Número] AS Producto, p.Nombre, p.Familia, f.Descripción AS NombreFamilia
    FROM Productos p WITH (NOLOCK)
    LEFT JOIN Familias f WITH (NOLOCK) ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
    WHERE p.Empresa = @empresa AND p.Ficticio = 0
      AND (@familia IS NULL OR UPPER(RTRIM(LTRIM(p.Familia))) = @familia OR UPPER(RTRIM(LTRIM(f.Descripción))) = @familia)
      AND (@grupo IS NULL OR UPPER(RTRIM(LTRIM(p.Grupo))) = @grupo)
      AND (@subgrupo IS NULL OR UPPER(RTRIM(LTRIM(p.SubGrupo))) = @subgrupo)";

        internal const string SQL_FIN = SQL_CTES + SQL_SELECT_SIN_VENTAS;

        internal const string SQL_CTES = @"
), stock AS (
    SELECT e.[Número] AS Producto, SUM(e.Cantidad) AS Cantidad
    FROM ExtractoProducto e WITH (NOLOCK)
    WHERE e.Empresa IN (@empresa, @espejo, '4') AND e.[Almacén] = @origen AND e.[Número] IN (SELECT Producto FROM candidatos)
    GROUP BY e.[Número]
), salida AS (
    SELECT x.Producto, SUM(x.Cantidad) AS Cantidad
    FROM (
        SELECT pe.[Número] AS Producto, -pe.Cantidad AS Cantidad
        FROM PreExtrProducto pe WITH (NOLOCK)
        WHERE ISNULL(pe.NºTraspaso, 0) <> 0 AND pe.Cantidad < 0 AND pe.[Almacén] = @origen
        UNION ALL
        SELECT pe.[Número], pe.Cantidad
        FROM PreExtrProducto pe WITH (NOLOCK)
        INNER JOIN Almacenes o WITH (NOLOCK) ON o.Empresa = pe.Empresa AND o.DiarioSalidaRep = pe.Diario AND o.[Número] <> pe.[Almacén]
        WHERE ISNULL(pe.NºTraspaso, 0) = 0 AND pe.Estado >= 0 AND pe.Cantidad > 0 AND o.[Número] = @origen
          AND pe.[Almacén] IN ('ALG', 'REI', 'ALC')
    ) x
    WHERE x.Producto IN (SELECT Producto FROM candidatos)
    GROUP BY x.Producto
), pendiente AS (
    SELECT l.Producto, SUM(l.Cantidad) AS Cantidad
    FROM LinPedidoVta l WITH (NOLOCK)
    WHERE l.Empresa IN (@empresa, @espejo) AND l.[Almacén] = @origen AND l.Estado BETWEEN -1 AND 1 AND l.[Número] <> 724488
      AND l.Producto IN (SELECT Producto FROM candidatos)
    GROUP BY l.Producto
)";

        /// <summary>Sugerencia 1429: la última venta en la tienda (albarán o factura, cantidad &gt; 0; las empresas del stock).</summary>
        internal const string SQL_VENTAS = @", ventas AS (
    SELECT l.Producto, MAX(l.[Fecha Albarán]) AS UltimaVenta
    FROM LinPedidoVta l WITH (NOLOCK)
    WHERE l.Empresa IN (@empresa, @espejo, '4') AND l.[Almacén] = @origen AND l.Estado >= 2 AND l.Cantidad > 0
      AND l.Producto IN (SELECT Producto FROM candidatos)
    GROUP BY l.Producto
)";

        internal const string SQL_SELECT_INICIO = @"
SELECT RTRIM(c.Producto) AS Producto, RTRIM(c.Nombre) AS Nombre, RTRIM(c.Familia) AS Familia, RTRIM(c.NombreFamilia) AS NombreFamilia,
       CAST(ISNULL(s.Cantidad, 0) - ISNULL(sa.Cantidad, 0) AS int) AS Stock,
       CAST(ISNULL(sa.Cantidad, 0) AS int) AS EnOtraReposicion,
       CAST(ISNULL(pd.Cantidad, 0) AS int) AS Pendiente,
       CAST(cs.StockMáximo AS int) AS StockMaximo,";

        internal const string SQL_SELECT_FROM = @"
FROM candidatos c
LEFT JOIN stock s ON s.Producto = c.Producto
LEFT JOIN salida sa ON sa.Producto = c.Producto
LEFT JOIN pendiente pd ON pd.Producto = c.Producto
LEFT JOIN ControlesStock cs WITH (NOLOCK) ON cs.Empresa = @empresa AND cs.[Almacén] = @origen AND cs.[Número] = c.Producto";

        internal const string SQL_SELECT_WHERE = @"
WHERE ISNULL(s.Cantidad, 0) - ISNULL(sa.Cantidad, 0) > @stockMinimo";

        internal const string SQL_SELECT_SIN_VENTAS = SQL_SELECT_INICIO + @"
       CAST(NULL AS datetime) AS UltimaVenta" + SQL_SELECT_FROM + SQL_SELECT_WHERE;

        internal const string SQL_SELECT_CON_VENTAS = SQL_SELECT_INICIO + @"
       v.UltimaVenta" + SQL_SELECT_FROM + @"
LEFT JOIN ventas v ON v.Producto = c.Producto" + SQL_SELECT_WHERE;

        /// <summary>La consulta con un parámetro por producto de la lista (si la hay).</summary>
        internal static string Sql(int numeroProductos)
        {
            return Sql(numeroProductos, 0, 0, false);
        }

        /// <summary>
        /// La consulta con un parámetro por producto (@p0…), por palabra del nombre (@n0…, LIKE) y por palabra excluida (@x0…, NOT
        /// LIKE); la última venta solo si se pide «sin ventas» (si no, no se lee LinPedidoVta de más).
        /// </summary>
        internal static string Sql(int numeroProductos, int numeroPalabras, int numeroExcluidas, bool conVentas)
        {
            var sql = new System.Text.StringBuilder(SQL_INICIO);
            if (numeroProductos > 0)
            {
                string lista = string.Join(", ", Enumerable.Range(0, numeroProductos).Select(i => "@p" + i));
                _ = sql.Append($"\n      AND p.[Número] IN ({lista})");
            }
            for (int i = 0; i < numeroPalabras; i++)
            {
                _ = sql.Append($"\n      AND UPPER(p.Nombre) LIKE @n{i} ESCAPE '!'");
            }
            for (int i = 0; i < numeroExcluidas; i++)
            {
                _ = sql.Append($"\n      AND (p.Nombre IS NULL OR UPPER(p.Nombre) NOT LIKE @x{i} ESCAPE '!')");
            }
            _ = sql.Append(SQL_CTES);
            _ = conVentas ? sql.Append(SQL_VENTAS).Append(SQL_SELECT_CON_VENTAS) : sql.Append(SQL_SELECT_SIN_VENTAS);
            return sql.ToString();
        }

        /// <summary>El valor del LIKE: «%PALABRA%» con los comodines de la palabra escapados (con «!»).</summary>
        internal static string Like(string palabra)
        {
            string escapada = (palabra ?? string.Empty).Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");
            return "%" + escapada + "%";
        }

        public async Task<List<FilaExcesoReposicion>> Leer(FiltroExcesoReposicion filtro)
        {
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = filtro.Empresa },
                new SqlParameter("@origen", SqlDbType.Char, 3) { Value = filtro.Origen },
                new SqlParameter("@familia", SqlDbType.NVarChar, 100) { Value = (object)filtro.Familia ?? DBNull.Value },
                new SqlParameter("@grupo", SqlDbType.NVarChar, 50) { Value = (object)filtro.Grupo ?? DBNull.Value },
                new SqlParameter("@subgrupo", SqlDbType.NVarChar, 50) { Value = (object)filtro.Subgrupo ?? DBNull.Value },
                new SqlParameter("@stockMinimo", SqlDbType.Int) { Value = Math.Max(0, filtro.StockMinimo ?? 0) }
            };
            List<string> productos = filtro.Productos ?? new List<string>();
            for (int i = 0; i < productos.Count; i++)
            {
                parametros.Add(new SqlParameter("@p" + i, SqlDbType.Char, 15) { Value = productos[i] });
            }
            List<string> palabras = filtro.PalabrasNombre ?? new List<string>();
            for (int i = 0; i < palabras.Count; i++)
            {
                parametros.Add(new SqlParameter("@n" + i, SqlDbType.NVarChar, 120) { Value = Like(palabras[i]) });
            }
            List<string> excluidas = filtro.PalabrasExcluir ?? new List<string>();
            for (int i = 0; i < excluidas.Count; i++)
            {
                parametros.Add(new SqlParameter("@x" + i, SqlDbType.NVarChar, 120) { Value = Like(excluidas[i]) });
            }
            int? tiempoAnterior = db.Database.CommandTimeout;
            db.Database.CommandTimeout = Math.Max(tiempoAnterior ?? 0, 60);
            try
            {
                string sql = Sql(productos.Count, palabras.Count, excluidas.Count, filtro.SinVentasEnMeses.HasValue);
                return await db.Database.SqlQuery<FilaExcesoReposicion>(sql, parametros.Cast<object>().ToArray())
                    .ToListAsync().ConfigureAwait(false);
            }
            finally
            {
                db.Database.CommandTimeout = tiempoAnterior;
            }
        }
    }

    /// <summary>
    /// Sugerencia 1429: calcula el exceso y, de verdad, crea la reposición por el MISMO camino que POST api/Reposiciones
    /// (<see cref="IServicioPreparacionReposicion.Crear"/>, con sus 409). Mismos permisos que crear una a mano, también en el ensayo.
    /// </summary>
    public class ServicioExcesoReposicion
    {
        private readonly ILectorExcesoReposicion lector;
        private readonly IServicioPreparacionReposicion preparacion;
        private readonly IPermisoRellenarReposicionManual permisoManual;

        public ServicioExcesoReposicion(ILectorExcesoReposicion lector, IServicioPreparacionReposicion preparacion,
            IPermisoRellenarReposicionManual permisoManual)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.preparacion = preparacion ?? throw new ArgumentNullException(nameof(preparacion));
            this.permisoManual = permisoManual ?? throw new ArgumentNullException(nameof(permisoManual));
        }

        /// <exception cref="UnauthorizedAccessException">403: cliente de la tienda online o sin permiso de crear reposiciones a mano.</exception>
        /// <exception cref="NestoBusinessException">400 (petición, tope de líneas) o lo que dé la creación (409…).</exception>
        public async Task<ResultadoExcesoReposicionDTO> Calcular(PeticionExcesoReposicionDTO peticion, bool ensayo, IPrincipal usuario)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(usuario))
            {
                throw new UnauthorizedAccessException(ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO);
            }
            if (!permisoManual.Puede(usuario))
            {
                throw new UnauthorizedAccessException(PermisoRellenarReposicionManual.MENSAJE_SIN_PERMISO);
            }
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(peticion);
            // El mismo criterio que la creación (ServicioPreparacionReposicion.ComprobarPuedeEscribir): la gente de la tienda
            // (AlmacénPedidoVta), Almacén o Dirección. Las tiendas no tienen control de ubicaciones.
            if (!preparacion.PuedeEscribir(usuario, filtro.Empresa, filtro.Origen))
            {
                throw new UnauthorizedAccessException($"Las reposiciones de {filtro.Origen} las prepara la gente de ese almacén (o Almacén y Dirección). " +
                    "Su usuario no tiene ese almacén como almacén de pedidos.");
            }

            List<FilaExcesoReposicion> filas = await lector.Leer(filtro).ConfigureAwait(false);
            ResultadoExcesoReposicionDTO resultado = ReglaExcesoReposicion.Resultado(filtro, filas, ensayo);
            if (ensayo || resultado.NumeroLineas == 0)
            {
                return resultado;
            }

            resultado.Reposicion = await preparacion.Crear(new CrearReposicionDTO
            {
                Empresa = filtro.Empresa,
                Origen = filtro.Origen,
                Destino = filtro.Destino,
                Lineas = resultado.Lineas.Select(l => new LineaCrearReposicionDTO { Producto = l.Producto, Cantidad = l.Exceso }).ToList()
            }, usuario).ConfigureAwait(false);
            resultado.Mensaje = ReglaExcesoReposicion.Mensaje(resultado, ReglaExcesoReposicion.NombreAlmacen(filtro.Origen),
                ReglaExcesoReposicion.NombreAlmacen(filtro.Destino), creada: true);
            return resultado;
        }
    }
}
