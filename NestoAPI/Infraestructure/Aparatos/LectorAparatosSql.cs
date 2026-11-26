using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Aparatos
{
    /// <summary>Qué se lee: un aparato (producto + serie) o todos los del alcance.</summary>
    public class FiltroAparatos
    {
        /// <summary>Con producto: solo ese (la historia). Sin él: el alcance del informe.</summary>
        public string Producto { get; set; }
        /// <summary>Con serie: solo esa (comparada recortada y en mayúsculas).</summary>
        public string NumeroSerie { get; set; }
        /// <summary>Informe: solo los 36 productos de ProductosAlquiler (la lista de la ventana).</summary>
        public bool SoloListaAlquileres { get; set; }
        /// <summary>Informe: leer también el stock contable (ExtractoProducto, empresas 1 y 3).</summary>
        public bool ConStock { get; set; }
    }

    public interface ILectorAparatos
    {
        Task<DatosAparatos> Leer(FiltroAparatos filtro);
    }

    /// <summary>
    /// NestoAPI#913: SOLO LECTURA (NOLOCK), una consulta por fuente (productos, registros, ventas, compras, inmovilizados,
    /// NumerosSerie y, en el informe, el stock), todas con el mismo alcance de productos: el producto pedido; o la lista de la
    /// ventana (ProductosAlquiler); o los que tienen <c>NecesitaNumSerie</c>, están en la lista o tienen registro en Alquileres.
    /// </summary>
    public class LectorAparatosSql : ILectorAparatos
    {
        private readonly NVEntities db;

        public LectorAparatosSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string ALCANCE_PRODUCTO = "p.[Número] = @producto";
        internal const string ALCANCE_LISTA = "EXISTS (SELECT 1 FROM ProductosAlquiler pa WITH (NOLOCK) WHERE pa.[Número] = p.[Número])";
        internal const string ALCANCE_TODO = "(p.NecesitaNumSerie = 1 OR EXISTS (SELECT 1 FROM ProductosAlquiler pa WITH (NOLOCK) WHERE pa.[Número] = p.[Número])" +
            " OR EXISTS (SELECT 1 FROM CabAlquileres ca WITH (NOLOCK) WHERE ca.Producto = p.[Número]))";

        internal static string Alcance(FiltroAparatos filtro)
        {
            if (!string.IsNullOrWhiteSpace(filtro.Producto))
            {
                return ALCANCE_PRODUCTO;
            }
            return filtro.SoloListaAlquileres ? ALCANCE_LISTA : ALCANCE_TODO;
        }

        private static string Productos(string alcance) => $"(SELECT p.[Número] FROM Productos p WITH (NOLOCK) WHERE p.Empresa = '1' AND {alcance})";

        private static string Serie(string columna, FiltroAparatos filtro)
            => string.IsNullOrWhiteSpace(filtro.NumeroSerie) ? string.Empty : $" AND UPPER(LTRIM(RTRIM({columna}))) = @serie";

        internal static string SqlProductos(FiltroAparatos f) => @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, CAST(ISNULL(p.NecesitaNumSerie, 0) AS bit) AS NecesitaNumSerie,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM ProductosAlquiler pa WITH (NOLOCK) WHERE pa.[Número] = p.[Número]) THEN 1 ELSE 0 END AS bit) AS EnListaAlquileres
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = '1' AND " + Alcance(f);

        internal static string SqlRegistros(FiltroAparatos f) => @"
SELECT RTRIM(c.Empresa) AS Empresa, c.[Número] AS Numero, RTRIM(c.Cliente) AS Cliente, RTRIM(c.Contacto) AS Contacto, RTRIM(cl.Nombre) AS NombreCliente,
       RTRIM(c.Producto) AS Producto, RTRIM(c.Inmovilizado) AS Inmovilizado, c.Cuotas, c.FechaEntrega, c.FechaSeñal AS FechaSenal,
       CAST(c.ImporteSeñal AS decimal(19, 4)) AS ImporteSenal, RTRIM(c.NumeroSerie) AS NumeroSerie, RTRIM(c.NombreProducto) AS NombreProducto,
       RTRIM(c.Familia) AS Familia, CAST(c.Importe AS decimal(19, 4)) AS Importe, c.CabPedidoVta, RTRIM(c.RutaContrato) AS RutaContrato,
       c.Comentarios, RTRIM(c.Usuario) AS Usuario, c.FechaModificación AS FechaModificacion
FROM CabAlquileres c WITH (NOLOCK)
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.Cliente AND cl.Contacto = c.Contacto
WHERE c.Producto IN " + Productos(Alcance(f)) + Serie("c.NumeroSerie", f);

        internal static string SqlVentas(FiltroAparatos f) => @"
SELECT RTRIM(l.Empresa) AS Empresa, l.[Número] AS Pedido, RTRIM(l.Producto) AS Producto, RTRIM(l.NumSerie) AS NumeroSerie,
       CAST(l.Cantidad AS int) AS Cantidad, l.[Fecha Albarán] AS FechaAlbaran, l.[Nº Albarán] AS Albaran, RTRIM(l.[Nº Factura]) AS Factura,
       RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Contacto) AS Contacto, RTRIM(cl.Nombre) AS NombreCliente, RTRIM(l.Texto) AS Texto,
       RTRIM(l.[Almacén]) AS Almacen
FROM LinPedidoVta l WITH (NOLOCK)
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.Empresa = l.Empresa AND cl.[Nº Cliente] = l.[Nº Cliente] AND cl.Contacto = l.Contacto
WHERE l.Estado >= 2 AND l.Cantidad <> 0 AND l.NumSerie IS NOT NULL AND LTRIM(RTRIM(l.NumSerie)) <> ''
  AND l.Producto IN " + Productos(Alcance(f)) + Serie("l.NumSerie", f);

        internal static string SqlCompras(FiltroAparatos f) => @"
SELECT RTRIM(l.Empresa) AS Empresa, l.[Número] AS Pedido, RTRIM(l.Producto) AS Producto, RTRIM(l.NumSerie) AS NumeroSerie,
       CAST(l.Cantidad AS int) AS Cantidad, ISNULL(l.[FechaAlbarán], l.[FechaRecepción]) AS Fecha, l.[NºAlbarán] AS Albaran,
       RTRIM(l.[NºProveedor]) AS Proveedor, RTRIM(pr.Nombre) AS NombreProveedor, RTRIM(l.Texto) AS Texto, RTRIM(l.[Almacén]) AS Almacen
FROM LinPedidoCmp l WITH (NOLOCK)
LEFT JOIN Proveedores pr WITH (NOLOCK) ON pr.Empresa = l.Empresa AND pr.[Número] = l.[NºProveedor] AND pr.Contacto = l.Contacto
WHERE l.Estado >= 2 AND l.Cantidad <> 0 AND l.NumSerie IS NOT NULL AND LTRIM(RTRIM(l.NumSerie)) <> ''
  AND l.Producto IN " + Productos(Alcance(f)) + Serie("l.NumSerie", f);

        /// <summary>
        /// Los inmovilizados (826 en total: se leen todos en el informe; en la historia, los que nombran la serie). La serie y la
        /// referencia se sacan del texto en C# (<see cref="ConstructorMovimientosAparato"/>).
        /// </summary>
        internal static string SqlInmovilizados(FiltroAparatos f) => @"
SELECT RTRIM(i.Empresa) AS Empresa, RTRIM(i.[Número]) AS Numero, RTRIM(i.Descripción) AS Descripcion, i.Estado, i.FechaCreación AS FechaCreacion
FROM Inmovilizados i WITH (NOLOCK)
WHERE i.Descripción IS NOT NULL" + (string.IsNullOrWhiteSpace(f.NumeroSerie) ? string.Empty : " AND UPPER(i.Descripción) LIKE @serieLike ESCAPE '!'");

        internal static string SqlNumerosSerie(FiltroAparatos f) => @"
SELECT RTRIM(n.Producto) AS Producto, RTRIM(n.NumSerie) AS NumeroSerie
FROM NumerosSerie n WITH (NOLOCK)
WHERE n.Producto IN " + Productos(Alcance(f)) + Serie("n.NumSerie", f);

        internal static string SqlStock(FiltroAparatos f) => @"
SELECT RTRIM(e.[Número]) AS Producto, RTRIM(e.[Almacén]) AS Almacen, CAST(SUM(e.Cantidad) AS int) AS Cantidad
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Empresa IN ('1', '3') AND e.[Número] IN " + Productos(Alcance(f)) + @"
GROUP BY e.[Número], e.[Almacén]
HAVING SUM(e.Cantidad) <> 0";

        /// <summary>«%SERIE%» con los comodines escapados («!»).</summary>
        internal static string Like(string texto)
            => "%" + (texto ?? string.Empty).Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![") + "%";

        private object[] Parametros(FiltroAparatos f)
        {
            var parametros = new List<SqlParameter>();
            if (!string.IsNullOrWhiteSpace(f.Producto))
            {
                parametros.Add(new SqlParameter("@producto", System.Data.SqlDbType.Char, 15) { Value = f.Producto.Trim() });
            }
            if (!string.IsNullOrWhiteSpace(f.NumeroSerie))
            {
                string serie = ReglaSituacionAparato.Clave(f.NumeroSerie);
                parametros.Add(new SqlParameter("@serie", System.Data.SqlDbType.VarChar, 30) { Value = serie });
                parametros.Add(new SqlParameter("@serieLike", System.Data.SqlDbType.VarChar, 40) { Value = Like(serie) });
            }
            return parametros.Cast<object>().ToArray();
        }

        private Task<List<T>> Consulta<T>(string sql, FiltroAparatos f)
        {
            // Los parámetros no se pueden reutilizar entre comandos: unos nuevos en cada consulta.
            object[] todos = Parametros(f);
            object[] usados = todos.Where(p => sql.Contains(((SqlParameter)p).ParameterName)).ToArray();
            return db.Database.SqlQuery<T>(sql, usados).ToListAsync();
        }

        public async Task<DatosAparatos> Leer(FiltroAparatos filtro)
        {
            filtro = filtro ?? new FiltroAparatos();
            int? tiempoAnterior = db.Database.CommandTimeout;
            db.Database.CommandTimeout = Math.Max(tiempoAnterior ?? 0, 60);
            try
            {
                var datos = new DatosAparatos
                {
                    Productos = await Consulta<ProductoAparatoFila>(SqlProductos(filtro), filtro).ConfigureAwait(false),
                    Registros = await Consulta<RegistroAparatoDTO>(SqlRegistros(filtro), filtro).ConfigureAwait(false),
                    Ventas = await Consulta<VentaAparatoFila>(SqlVentas(filtro), filtro).ConfigureAwait(false),
                    Compras = await Consulta<CompraAparatoFila>(SqlCompras(filtro), filtro).ConfigureAwait(false),
                    Inmovilizados = await Consulta<InmovilizadoAparatoFila>(SqlInmovilizados(filtro), filtro).ConfigureAwait(false),
                    NumerosSerie = await Consulta<NumeroSerieFila>(SqlNumerosSerie(filtro), filtro).ConfigureAwait(false)
                };
                if (filtro.ConStock)
                {
                    datos.Stock = await Consulta<StockAparatoFila>(SqlStock(filtro), filtro).ConfigureAwait(false);
                }
                return datos;
            }
            finally
            {
                db.Database.CommandTimeout = tiempoAnterior;
            }
        }
    }
}
