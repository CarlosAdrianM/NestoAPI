using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>Sugerencia 1482 (Manuel, Compras, 26/11/26): una fila de ProveedoresProducto para la ficha de producto de Nesto.</summary>
    public class ReferenciaProveedorProductoDTO
    {
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string ReferenciaProveedor { get; set; }
        public short Orden { get; set; }
        /// <summary>El de menor Orden (como el proveedor habitual de compras.md). Con empate, el primero por proveedor.</summary>
        public bool EsHabitual { get; set; }
    }

    /// <summary>Un movimiento de ExtractoProducto (empresas 1 y 3) para la ventana «Extracto del producto» de Nesto.</summary>
    public class MovimientoExtractoProductoDTO
    {
        public int NumeroOrden { get; set; }
        public string Empresa { get; set; }
        public DateTime Fecha { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
        /// <summary>El saldo tras este movimiento, acumulado del más antiguo al más nuevo (dentro del filtro de almacén).</summary>
        public int Saldo { get; set; }
        /// <summary>A coste: solo Compras, Dirección e Informática; a los demás, null.</summary>
        public decimal? Importe { get; set; }
        public string Texto { get; set; }
        public string Diario { get; set; }
        public int? Albaran { get; set; }
        /// <summary>Solo Compras, Dirección e Informática; a los demás, null.</summary>
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string Cliente { get; set; }
        public string ContactoCliente { get; set; }
        public string NombreCliente { get; set; }
        public int? Pedido { get; set; }
        public int? LineaPedido { get; set; }
        public int? Traspaso { get; set; }
        /// <summary>ExtractoProducto no guarda el número de asiento: solo la marca «Asiento Automático».</summary>
        public bool AsientoAutomatico { get; set; }
        public string Delegacion { get; set; }
        public string FormaVenta { get; set; }
        /// <summary>Sin el dominio (NUEVAVISION\Manuel → Manuel).</summary>
        public string Usuario { get; set; }
    }

    public class ExtractoProductoFichaDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Null = todos los almacenes.</summary>
        public string Almacen { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        /// <summary>Suma de las cantidades anteriores a <see cref="Desde"/> (del filtro de almacén).</summary>
        public int SaldoInicial { get; set; }
        /// <summary>El saldo al final de <see cref="Hasta"/>: SaldoInicial + lo del periodo.</summary>
        public int SaldoFinal { get; set; }
        /// <summary>Movimientos del periodo (todas las páginas).</summary>
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int Tamano { get; set; }
        /// <summary>Más nuevo primero.</summary>
        public List<MovimientoExtractoProductoDTO> Movimientos { get; set; } = new List<MovimientoExtractoProductoDTO>();
    }

    /// <summary>Lo que el lector devuelve de una página: los movimientos y las sumas para el saldo.</summary>
    public class PaginaExtractoProducto
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int SaldoInicial { get; set; }
        public int SumaPeriodo { get; set; }
        public int Total { get; set; }
        /// <summary>Suma de las cantidades de los movimientos del periodo MÁS NUEVOS que los de esta página (las páginas anteriores).</summary>
        public int SumaMasNuevos { get; set; }
        public List<MovimientoExtractoProductoDTO> Movimientos { get; set; } = new List<MovimientoExtractoProductoDTO>();
    }

    /// <summary>LAS REGLAS (puras) de quién ve qué en la ficha de producto (sugerencia 1482).</summary>
    public static class PermisosFichaProducto
    {
        private static readonly string[] GRUPOS_EXTRACTO =
        {
            Constantes.GruposSeguridad.COMPRAS,
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA,
            Constantes.GruposSeguridad.TIENDAS
        };

        public const string MENSAJE_SIN_PERMISO_EXTRACTO =
            "No tiene permiso para ver el extracto del producto: es para Compras, Almacén, Administración, Dirección, Informática y Tiendas.";
        public const string MENSAJE_SIN_PERMISO_PROVEEDORES =
            "No tiene permiso para ver de quién se compra el producto: es para Compras, Dirección e Informática.";

        /// <summary>
        /// De quién se compra (proveedor, su referencia) y a qué coste: Compras, Dirección e Informática (Carlos 18/11/26, la misma regla
        /// que la próxima entrada #869). Nunca un cliente de la tienda.
        /// </summary>
        public static bool PuedeVerProveedoresYCostes(IPrincipal usuario) =>
            usuario != null && !NestoAPI.Infraestructure.Seguridad.ValidadorAccesoCliente.EsClienteTienda(usuario) && NestoAPI.Controllers.ComprasPlazosProveedoresController.PuedeVer(usuario);

        /// <summary>El extracto: empleados de Compras, Almacén, Administración, Dirección, Informática y Tiendas. Ni vendedores de NestoApp ni clientes.</summary>
        public static bool PuedeVerExtracto(IPrincipal usuario) =>
            usuario != null && !NestoAPI.Infraestructure.Seguridad.ValidadorAccesoCliente.EsClienteTienda(usuario)
            && GRUPOS_EXTRACTO.Any(g => usuario.IsInRoleSinDominio(g));
    }

    /// <summary>LAS REGLAS (puras) del extracto: saldo acumulado, habitual, fechas y tamaño de página.</summary>
    public static class ReglaExtractoProducto
    {
        public const int TAMANO_POR_DEFECTO = 200;
        public const int TAMANO_MAXIMO = 500;
        public const int MESES_POR_DEFECTO = 12;

        /// <summary>
        /// Saldo de cada movimiento de la página (que viene del más nuevo al más antiguo). El más nuevo de TODO el periodo tiene el saldo final;
        /// cada uno, el saldo final menos lo de los más nuevos que él (los de las páginas anteriores y los de antes en esta página).
        /// </summary>
        public static void CalcularSaldos(int saldoInicial, int sumaPeriodo, int sumaMasNuevos, IList<MovimientoExtractoProductoDTO> paginaMasNuevoPrimero)
        {
            int saldo = saldoInicial + sumaPeriodo - sumaMasNuevos;
            foreach (MovimientoExtractoProductoDTO movimiento in paginaMasNuevoPrimero ?? new List<MovimientoExtractoProductoDTO>())
            {
                movimiento.Saldo = saldo;
                saldo -= movimiento.Cantidad;
            }
        }

        /// <summary>Marca el habitual: el de menor Orden (con empate, el primero de la lista, que viene por Orden y proveedor).</summary>
        public static List<ReferenciaProveedorProductoDTO> MarcarHabitual(IEnumerable<ReferenciaProveedorProductoDTO> filas)
        {
            List<ReferenciaProveedorProductoDTO> lista = (filas ?? Enumerable.Empty<ReferenciaProveedorProductoDTO>())
                .OrderBy(f => f.Orden).ThenBy(f => f.Proveedor, StringComparer.Ordinal).ToList();
            for (int i = 0; i < lista.Count; i++)
            {
                lista[i].EsHabitual = i == 0;
            }
            return lista;
        }

        public static int Tamano(int? tamano) =>
            tamano == null || tamano <= 0 ? TAMANO_POR_DEFECTO : Math.Min(tamano.Value, TAMANO_MAXIMO);

        public static int Pagina(int? pagina) => pagina == null || pagina < 1 ? 1 : pagina.Value;

        /// <summary>Sin fechas: los últimos 12 meses hasta hoy. Días completos, los dos incluidos.</summary>
        public static (DateTime desde, DateTime hasta) Periodo(DateTime? desde, DateTime? hasta, DateTime hoy)
        {
            DateTime fin = (hasta ?? hoy).Date;
            DateTime inicio = (desde ?? fin.AddMonths(-MESES_POR_DEFECTO)).Date;
            return (inicio, fin);
        }

        public static string SinDominio(string usuario) =>
            string.IsNullOrWhiteSpace(usuario) ? null : usuario.Substring(usuario.IndexOf('\\') + 1).Trim();

        /// <summary>El extracto de la casa es el de la empresa y su espejo (1 y 3: el stock suma las dos).</summary>
        public static string Espejo(string empresa) =>
            empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO ? Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO : empresa;
    }

    public interface ILectorFichaProducto
    {
        /// <summary>Null si el producto no existe en la empresa.</summary>
        List<ReferenciaProveedorProductoDTO> LeerReferenciasProveedor(string empresa, string producto);

        /// <summary>Null si el producto no existe en la empresa.</summary>
        PaginaExtractoProducto LeerExtracto(string empresa, string producto, string almacen, DateTime desde, DateTime hasta, int pagina, int tamano);
    }

    /// <summary>Solo lectura con NOLOCK (NestoConnection). Referencias: una consulta. Extracto: una ida con dos lotes (sumas y página).</summary>
    public class LectorFichaProductoSql : ILectorFichaProducto
    {
        internal const string SQL_REFERENCIAS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(pp.[Nº Proveedor]) AS Proveedor, NULLIF(RTRIM(pp.ReferenciaProv), '') AS Referencia, pp.Orden,
       (SELECT TOP 1 RTRIM(pr.Nombre) FROM Proveedores pr WITH (NOLOCK)
         WHERE pr.Empresa = pp.Empresa AND pr.[Número] = pp.[Nº Proveedor]
         ORDER BY CASE WHEN pr.Contacto = pp.ContactoProveedor THEN 0 ELSE 1 END, pr.Contacto) AS NombreProveedor
FROM Productos p WITH (NOLOCK)
     LEFT JOIN ProveedoresProducto pp WITH (NOLOCK) ON pp.Empresa = p.Empresa AND pp.[Nº Producto] = p.[Número]
WHERE p.Empresa = @empresa AND p.[Número] = @producto
ORDER BY pp.Orden, pp.[Nº Proveedor], pp.ContactoProveedor";

        // El filtro común de los movimientos del producto (empresa y su espejo, almacén opcional).
        private const string FILTRO = "e.Empresa IN (@empresa, @espejo) AND e.[Número] = @producto AND (@almacen IS NULL OR e.[Almacén] = @almacen)";

        // Sin OFFSET/FETCH ni SUM() OVER (ORDER BY): la BD va con un nivel de compatibilidad antiguo (no tiene TRY_CONVERT). ROW_NUMBER sí.
        private const string ORDEN = "ROW_NUMBER() OVER (ORDER BY e.Fecha DESC, e.[Fecha Modificación] DESC, e.Empresa DESC, e.[Nº Orden] DESC)";

        internal const string SQL_EXTRACTO = @"
SELECT (SELECT TOP 1 RTRIM(p.Nombre) FROM Productos p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.[Número] = @producto) AS Nombre,
       (SELECT COUNT(*) FROM Productos p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.[Número] = @producto) AS Existe,
       (SELECT ISNULL(SUM(CAST(e.Cantidad AS int)), 0) FROM ExtractoProducto e WITH (NOLOCK) WHERE " + FILTRO + @" AND e.Fecha < @desde) AS SaldoInicial,
       (SELECT ISNULL(SUM(CAST(e.Cantidad AS int)), 0) FROM ExtractoProducto e WITH (NOLOCK) WHERE " + FILTRO + @" AND e.Fecha >= @desde AND e.Fecha < @hasta) AS SumaPeriodo,
       (SELECT COUNT(*) FROM ExtractoProducto e WITH (NOLOCK) WHERE " + FILTRO + @" AND e.Fecha >= @desde AND e.Fecha < @hasta) AS Total,
       (SELECT ISNULL(SUM(m.Cantidad), 0) FROM (SELECT CAST(e.Cantidad AS int) AS Cantidad, " + ORDEN + @" AS Fila
          FROM ExtractoProducto e WITH (NOLOCK) WHERE " + FILTRO + @" AND e.Fecha >= @desde AND e.Fecha < @hasta) m
         WHERE m.Fila < @primeraFila) AS SumaMasNuevos;

SELECT m.NumeroOrden, m.Empresa, m.Fecha, m.Almacen, m.Cantidad, m.Importe, m.Texto, m.Diario, m.Albaran, m.Proveedor,
       (SELECT TOP 1 RTRIM(pr.Nombre) FROM Proveedores pr WITH (NOLOCK)
         WHERE pr.Empresa IN (m.Empresa, @empresa) AND pr.[Número] = m.Proveedor
         ORDER BY CASE WHEN pr.Empresa = m.Empresa THEN 0 ELSE 1 END, CASE WHEN pr.Contacto = m.ContactoProveedor THEN 0 ELSE 1 END, pr.Contacto) AS NombreProveedor,
       m.Cliente, m.ContactoCliente,
       (SELECT TOP 1 RTRIM(c.Nombre) FROM Clientes c WITH (NOLOCK)
         WHERE c.Empresa IN (m.Empresa, @empresa) AND c.[Nº Cliente] = m.Cliente
         ORDER BY CASE WHEN c.Empresa = m.Empresa THEN 0 ELSE 1 END, CASE WHEN c.Contacto = m.ContactoCliente THEN 0 ELSE 1 END, c.Contacto) AS NombreCliente,
       m.Pedido, m.LineaPedido, m.Traspaso, m.AsientoAutomatico, m.Delegacion, m.FormaVenta, m.Usuario
FROM (SELECT e.[Nº Orden] AS NumeroOrden, RTRIM(e.Empresa) AS Empresa, e.Fecha, RTRIM(e.[Almacén]) AS Almacen, CAST(e.Cantidad AS int) AS Cantidad,
             CAST(e.Importe AS decimal(19,4)) AS Importe, RTRIM(e.Texto) AS Texto, RTRIM(e.Diario) AS Diario, e.[Albarán] AS Albaran,
             NULLIF(RTRIM(e.[NºProveedor]), '') AS Proveedor, e.ContactoProveedor, NULLIF(RTRIM(e.[Nº Cliente]), '') AS Cliente,
             NULLIF(RTRIM(e.ContactoCliente), '') AS ContactoCliente, e.[NºPedido] AS Pedido, e.LinPedido AS LineaPedido, e.[NºTraspaso] AS Traspaso,
             e.[Asiento Automático] AS AsientoAutomatico, RTRIM(e.[Delegación]) AS Delegacion, RTRIM(e.[Forma Venta]) AS FormaVenta,
             RTRIM(e.Usuario) AS Usuario, " + ORDEN + @" AS Fila
      FROM ExtractoProducto e WITH (NOLOCK)
      WHERE " + FILTRO + @" AND e.Fecha >= @desde AND e.Fecha < @hasta) m
WHERE m.Fila BETWEEN @primeraFila AND @ultimaFila
ORDER BY m.Fila";

        private readonly string cadenaConexion;

        public LectorFichaProductoSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion ?? throw new ArgumentNullException(nameof(cadenaConexion));
        }

        public static LectorFichaProductoSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new LectorFichaProductoSql(origen.ConnectionString);
        }

        public List<ReferenciaProveedorProductoDTO> LeerReferenciasProveedor(string empresa, string producto)
        {
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            using (SqlCommand comando = new SqlCommand(SQL_REFERENCIAS, conexion))
            {
                comando.Parameters.AddWithValue("@empresa", empresa);
                comando.Parameters.AddWithValue("@producto", producto);
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    bool existe = false;
                    List<ReferenciaProveedorProductoDTO> filas = new List<ReferenciaProveedorProductoDTO>();
                    while (lector.Read())
                    {
                        existe = true;
                        if (lector.IsDBNull(1))
                        {
                            continue; // el producto sin ninguna fila en ProveedoresProducto
                        }
                        filas.Add(new ReferenciaProveedorProductoDTO
                        {
                            Proveedor = lector.GetString(1),
                            ReferenciaProveedor = lector.IsDBNull(2) ? null : lector.GetString(2),
                            Orden = lector.GetInt16(3),
                            NombreProveedor = lector.IsDBNull(4) ? null : lector.GetString(4)
                        });
                    }
                    return existe ? filas : null;
                }
            }
        }

        public PaginaExtractoProducto LeerExtracto(string empresa, string producto, string almacen, DateTime desde, DateTime hasta, int pagina, int tamano)
        {
            int primeraFila = (pagina - 1) * tamano + 1;
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            using (SqlCommand comando = new SqlCommand(SQL_EXTRACTO, conexion))
            {
                comando.Parameters.AddWithValue("@empresa", empresa);
                comando.Parameters.AddWithValue("@espejo", ReglaExtractoProducto.Espejo(empresa));
                comando.Parameters.AddWithValue("@producto", producto);
                comando.Parameters.AddWithValue("@almacen", (object)almacen ?? DBNull.Value);
                comando.Parameters.AddWithValue("@desde", desde.Date);
                comando.Parameters.AddWithValue("@hasta", hasta.Date.AddDays(1));
                comando.Parameters.AddWithValue("@primeraFila", primeraFila);
                comando.Parameters.AddWithValue("@ultimaFila", primeraFila + tamano - 1);
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    if (!lector.Read() || lector.GetInt32(1) == 0)
                    {
                        return null;
                    }
                    PaginaExtractoProducto resultado = new PaginaExtractoProducto
                    {
                        Producto = producto,
                        Nombre = lector.IsDBNull(0) ? null : lector.GetString(0),
                        SaldoInicial = lector.GetInt32(2),
                        SumaPeriodo = lector.GetInt32(3),
                        Total = lector.GetInt32(4),
                        SumaMasNuevos = lector.GetInt32(5)
                    };
                    lector.NextResult();
                    while (lector.Read())
                    {
                        resultado.Movimientos.Add(new MovimientoExtractoProductoDTO
                        {
                            NumeroOrden = lector.GetInt32(0),
                            Empresa = lector.GetString(1),
                            Fecha = lector.GetDateTime(2),
                            Almacen = Texto(lector, 3),
                            Cantidad = lector.GetInt32(4),
                            Importe = lector.IsDBNull(5) ? (decimal?)null : lector.GetDecimal(5),
                            Texto = Texto(lector, 6),
                            Diario = Texto(lector, 7),
                            Albaran = lector.IsDBNull(8) ? (int?)null : lector.GetInt32(8),
                            Proveedor = Texto(lector, 9),
                            NombreProveedor = Texto(lector, 10),
                            Cliente = Texto(lector, 11),
                            ContactoCliente = Texto(lector, 12),
                            NombreCliente = Texto(lector, 13),
                            Pedido = lector.IsDBNull(14) ? (int?)null : lector.GetInt32(14),
                            LineaPedido = lector.IsDBNull(15) ? (int?)null : lector.GetInt32(15),
                            Traspaso = lector.IsDBNull(16) ? (int?)null : lector.GetInt32(16),
                            AsientoAutomatico = !lector.IsDBNull(17) && lector.GetBoolean(17),
                            Delegacion = Texto(lector, 18),
                            FormaVenta = Texto(lector, 19),
                            Usuario = ReglaExtractoProducto.SinDominio(Texto(lector, 20))
                        });
                    }
                    return resultado;
                }
            }
        }

        private static string Texto(SqlDataReader lector, int columna) => lector.IsDBNull(columna) ? null : lector.GetString(columna);
    }
}
