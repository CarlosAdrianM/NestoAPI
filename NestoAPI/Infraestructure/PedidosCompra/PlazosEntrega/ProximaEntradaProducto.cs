using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega
{
    /// <summary>NestoAPI#869: una línea de compra en el aire (LinPedidoCmp -1/1, Enviado) del producto en el almacén.</summary>
    public class LineaCompraEnElAire
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public int Cantidad { get; set; }
        /// <summary>LinPedidoCmp.Usuario: «Correo de seguimiento» (#857) o «Fecha recalculada (vencida)» (#865) si la puso la API.</summary>
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#869: respuesta de GET api/Productos/ProximaEntrada. Solo lectura.</summary>
    public class ProximaEntradaProductoDTO
    {
        public const string TIPO_PEDIDO = "Pedido";
        public const string TIPO_ORIENTATIVA = "Orientativa";
        public const string TIPO_SIN_DATOS = "SinDatos";

        public string Producto { get; set; }
        public string Almacen { get; set; }
        /// <summary><see cref="TIPO_PEDIDO"/>, <see cref="TIPO_ORIENTATIVA"/> o <see cref="TIPO_SIN_DATOS"/>.</summary>
        public string Tipo { get; set; }
        /// <summary>La llegada estimada (null sin datos o con un pedido sin fecha).</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>CERTEZA_* de <see cref="PartidaFechaEntregaAgenciaDTO"/> (CasiSegura, Aproximada, Orientativa); null sin fecha.</summary>
        public string Certeza { get; set; }
        /// <summary>«Prácticamente segura», «Aproximada», «Orientativa» (<see cref="TextosCertezaFechaEntrega.Etiqueta"/>).</summary>
        public string EtiquetaCerteza { get; set; }
        /// <summary>La frase para la ficha, de usted.</summary>
        public string Texto { get; set; }
        /// <summary>Sin datos: por qué.</summary>
        public string Motivo { get; set; }

        // Caso 1: pedido de compra en el aire
        public string EmpresaPedido { get; set; }
        public int? Pedido { get; set; }
        public string ProveedorPedido { get; set; }
        public string NombreProveedorPedido { get; set; }
        /// <summary>Unidades del pedido elegido que llegan esa fecha.</summary>
        public int CantidadPedido { get; set; }
        /// <summary>Todas las unidades en el aire del producto en el almacén (todos los pedidos).</summary>
        public int CantidadEnElAire { get; set; }
        /// <summary>Cuántos pedidos distintos hay en el aire.</summary>
        public int PedidosEnElAire { get; set; }
        /// <summary>La fecha la puso un correo de seguimiento de la agencia (#857) y sigue vigente.</summary>
        public bool PorCorreoSeguimiento { get; set; }
        public string AgenciaSeguimiento { get; set; }
        /// <summary>La fecha la puso el recálculo de vencidas (#865): la prevista había pasado.</summary>
        public bool Recalculada { get; set; }
        /// <summary>La fecha del pedido ya ha pasado y nadie la ha cambiado aún.</summary>
        public bool PrevistaPasada { get; set; }

        // Caso 2 (y datos del proveedor habitual en cualquier caso)
        public string ProveedorHabitual { get; set; }
        public string NombreProveedorHabitual { get; set; }
        /// <summary>Hoy + el P80 (propio con ≥5 recepciones, si no el genérico).</summary>
        public DateTime? FechaSiSePidieraHoy { get; set; }
        /// <summary>Cuándo tocaría el próximo pedido habitual al proveedor (cadencia − días desde el último, mínimo mañana).</summary>
        public DateTime? FechaProximoPedidoHabitual { get; set; }
        /// <summary>La de <see cref="ReglaFechaOrientativaSinPedir"/>: el próximo pedido habitual + el P80 (la que usa el motor #853).</summary>
        public DateTime? LlegadaConPedidoHabitual { get; set; }
        public int? CadenciaDias { get; set; }
        public int? PlazoDias { get; set; }
        public bool PlazoPropio { get; set; }
        public DateTime? UltimoPedidoProveedor { get; set; }
    }

    /// <summary>NestoAPI#869: lo que lee la próxima entrada (sustituible en los tests).</summary>
    public interface IRepositorioProximaEntrada
    {
        /// <summary>Null si el producto no existe; si existe, si es ficticio.</summary>
        bool? EsFicticio(string empresa, string producto);
        /// <summary>Las líneas en el aire (empresas 1 + 3) del producto en el almacén.</summary>
        List<LineaCompraEnElAire> EnElAire(string empresa, string producto, string almacen);
        /// <summary>Proveedor → nombre.</summary>
        Dictionary<string, string> NombresProveedores(string empresa, IEnumerable<string> proveedores);
    }

    /// <summary>
    /// NestoAPI#869: la próxima entrada de compra de un producto, PURA. Orden: (1) pedido en el aire con su FechaRecepción (la más temprana;
    /// certeza por <see cref="ReglaSeguimientoAgencia"/> como en el motor #853), (2) sin pedido, la orientativa de
    /// <see cref="ReglaFechaOrientativaSinPedir"/> (con sus ingredientes, <see cref="ReglasFechasOrientativas.Ingredientes"/>) y el «si se
    /// pidiera hoy» (<see cref="ReglaFechaRecepcionEstimada"/> sin DíasEnServir), (3) nada, con el motivo. No inventa reglas.
    /// </summary>
    public static class ReglaProximaEntrada
    {
        public const string MOTIVO_FICTICIO = "Es un producto ficticio: no se compra.";
        public const string MOTIVO_SIN_PROVEEDOR = "No hay pedido al proveedor y no se sabe a qué proveedor se compra este producto.";
        public const string MOTIVO_SIN_PLAZOS = "No hay pedido al proveedor y aún no hay plazos de proveedores calculados.";
        public const string MOTIVO_SIN_PLAZO_PROVEEDOR = "No hay pedido al proveedor y no hay datos de cuánto tarda {0}";

        public static ProximaEntradaProductoDTO Calcular(DateTime hoy, string producto, string almacen, IEnumerable<LineaCompraEnElAire> lineas,
            IEnumerable<CorreoAplicadoPedidoCompra> correos, PlazosProveedores plazos, string proveedorHabitual,
            IDictionary<string, string> nombres)
        {
            hoy = hoy.Date;
            var resultado = new ProximaEntradaProductoDTO
            {
                Producto = producto?.Trim(),
                Almacen = almacen?.Trim(),
                ProveedorHabitual = string.IsNullOrWhiteSpace(proveedorHabitual) ? null : proveedorHabitual.Trim()
            };
            resultado.NombreProveedorHabitual = Nombre(nombres, resultado.ProveedorHabitual);

            List<LineaCompraEnElAire> enElAire = (lineas ?? Enumerable.Empty<LineaCompraEnElAire>()).Where(l => l != null && l.Cantidad > 0).ToList();
            CompletarOrientativa(resultado, hoy, plazos);

            if (enElAire.Any())
            {
                resultado.Tipo = ProximaEntradaProductoDTO.TIPO_PEDIDO;
                resultado.CantidadEnElAire = enElAire.Sum(l => l.Cantidad);
                resultado.PedidosEnElAire = enElAire.Select(l => (l.Empresa?.Trim(), l.Pedido)).Distinct().Count();
                // La próxima: la fecha más temprana (las que no tienen fecha, al final)
                LineaCompraEnElAire primera = enElAire
                    .OrderBy(l => l.FechaRecepcion.HasValue ? 0 : 1)
                    .ThenBy(l => l.FechaRecepcion ?? DateTime.MaxValue)
                    .ThenBy(l => l.Pedido)
                    .First();
                resultado.EmpresaPedido = primera.Empresa?.Trim();
                resultado.Pedido = primera.Pedido;
                resultado.ProveedorPedido = primera.Proveedor?.Trim();
                resultado.NombreProveedorPedido = Nombre(nombres, resultado.ProveedorPedido);
                List<LineaCompraEnElAire> delPedido = enElAire.Where(l => l.Pedido == primera.Pedido
                    && string.Equals(l.Empresa?.Trim(), primera.Empresa?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && l.FechaRecepcion?.Date == primera.FechaRecepcion?.Date).ToList();
                resultado.CantidadPedido = delPedido.Sum(l => l.Cantidad);

                if (primera.FechaRecepcion.HasValue)
                {
                    DateTime fecha = primera.FechaRecepcion.Value.Date;
                    resultado.Fecha = fecha;
                    CorreoAplicadoPedidoCompra vigente = ReglaSeguimientoAgencia.Vigente(correos, primera.Empresa, primera.Pedido, fecha);
                    resultado.PorCorreoSeguimiento = vigente != null;
                    resultado.AgenciaSeguimiento = vigente?.Agencia;
                    resultado.Recalculada = delPedido.Any(l => string.Equals(l.Usuario?.Trim(), RastroFechaRecalculada.USUARIO, StringComparison.OrdinalIgnoreCase));
                    resultado.PrevistaPasada = fecha < hoy;
                    // Como el motor (#853): con seguimiento vigente y fecha por llegar, prácticamente segura; si no, aproximada.
                    resultado.Certeza = resultado.PorCorreoSeguimiento && !resultado.PrevistaPasada
                        ? PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA
                        : PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA;
                    resultado.EtiquetaCerteza = TextosCertezaFechaEntrega.Etiqueta(resultado.Certeza);
                }
                resultado.Texto = TextosProximaEntrada.Pedido(resultado, hoy);
                return resultado;
            }

            if (resultado.ProveedorHabitual == null)
            {
                return SinDatos(resultado, MOTIVO_SIN_PROVEEDOR);
            }
            if (plazos == null || !plazos.TieneDatos)
            {
                return SinDatos(resultado, MOTIVO_SIN_PLAZOS);
            }
            if (!resultado.FechaSiSePidieraHoy.HasValue && !resultado.LlegadaConPedidoHabitual.HasValue)
            {
                string motivo = string.Format(MOTIVO_SIN_PLAZO_PROVEEDOR, NombreParaTexto(resultado.ProveedorHabitual, resultado.NombreProveedorHabitual));
                return SinDatos(resultado, motivo.EndsWith(".") ? motivo : motivo + ".");
            }
            resultado.Tipo = ProximaEntradaProductoDTO.TIPO_ORIENTATIVA;
            // La fecha principal es la del motor (#853), si la hay; si no (proveedor sin cadencia), la de «si se pidiera hoy».
            resultado.Fecha = resultado.LlegadaConPedidoHabitual ?? resultado.FechaSiSePidieraHoy;
            resultado.Certeza = PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA;
            resultado.EtiquetaCerteza = TextosCertezaFechaEntrega.Etiqueta(resultado.Certeza);
            resultado.Texto = TextosProximaEntrada.Orientativa(resultado, hoy);
            return resultado;
        }

        public static ProximaEntradaProductoDTO Ficticio(string producto, string almacen) => SinDatos(new ProximaEntradaProductoDTO
        {
            Producto = producto?.Trim(),
            Almacen = almacen?.Trim()
        }, MOTIVO_FICTICIO);

        private static void CompletarOrientativa(ProximaEntradaProductoDTO resultado, DateTime hoy, PlazosProveedores plazos)
        {
            if (resultado.ProveedorHabitual == null || plazos == null || !plazos.TieneDatos)
            {
                return;
            }
            PlazoProveedor propio = plazos.DeProveedor(resultado.ProveedorHabitual);
            PlazoProveedor generico = plazos.Generico;
            FechaRecepcionEstimada siHoy = ReglaFechaRecepcionEstimada.Calcular(hoy, 0, propio, generico);
            resultado.FechaSiSePidieraHoy = siHoy.FechaRegla;
            resultado.PlazoPropio = propio != null && propio.TienePlazoPropio;
            resultado.PlazoDias = siHoy.FechaRegla.HasValue ? (siHoy.FechaRegla.Value - hoy).Days : (int?)null;
            resultado.UltimoPedidoProveedor = propio?.UltimoPedido?.Date;
            resultado.CadenciaDias = propio?.IntervaloPedidosMediano;
            IngredientesFechaOrientativa ingredientes = ReglasFechasOrientativas.Ingredientes(hoy, propio, generico);
            if (ingredientes != null)
            {
                resultado.FechaProximoPedidoHabitual = ingredientes.FechaPedidoPrevista;
                resultado.LlegadaConPedidoHabitual = ingredientes.FechaLlegadaOrientativa;
            }
        }

        private static ProximaEntradaProductoDTO SinDatos(ProximaEntradaProductoDTO resultado, string motivo)
        {
            resultado.Tipo = ProximaEntradaProductoDTO.TIPO_SIN_DATOS;
            resultado.Fecha = null;
            resultado.Certeza = null;
            resultado.EtiquetaCerteza = null;
            resultado.Motivo = motivo;
            resultado.Texto = motivo;
            return resultado;
        }

        private static string Nombre(IDictionary<string, string> nombres, string proveedor) =>
            proveedor != null && nombres != null && nombres.TryGetValue(proveedor, out string nombre) && !string.IsNullOrWhiteSpace(nombre)
                ? nombre.Trim()
                : null;

        internal static string NombreParaTexto(string proveedor, string nombre) =>
            !string.IsNullOrWhiteSpace(nombre) ? nombre.Trim() : $"el proveedor {proveedor}";
    }

    /// <summary>NestoAPI#869: las frases de la ficha (de usted). Los calificativos son los de <see cref="TextosCertezaFechaEntrega"/>.</summary>
    public static class TextosProximaEntrada
    {
        public const string TEXTO_RECALCULADA = "recalculada, la prevista había pasado";
        public const string TEXTO_PREVISTA_PASADA = "la prevista ya ha pasado y no hay noticias";

        /// <summary>«27/11» (o «27/11/2027» si es de otro año que hoy).</summary>
        public static string Dia(DateTime dia, DateTime hoy)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            int anio = hoy.Year;
            return dia.Year == anio ? dia.ToString("dd/MM", es) : dia.ToString("dd/MM/yyyy", es);
        }

        /// <summary>
        /// «Pedido 220856: 27/11 (fecha prácticamente segura: ya enviado por UPS)», «Pedido 220856: 27/11 (fecha aproximada, aún sin
        /// seguimiento de la agencia)», «Pedido 220856: 27/11 (recalculada, la prevista había pasado; fecha aproximada)»,
        /// «Pedido 220856: 10/11 (la prevista ya ha pasado y no hay noticias)» o «Pedido 220856: sin fecha de recepción».
        /// </summary>
        public static string Pedido(ProximaEntradaProductoDTO p, DateTime hoy)
        {
            string cabecera = $"Pedido {p.Pedido}";
            if (!p.Fecha.HasValue)
            {
                return $"{cabecera}: sin fecha de recepción";
            }
            string detalle;
            if (p.PrevistaPasada)
            {
                detalle = TEXTO_PREVISTA_PASADA;
            }
            else if (p.Certeza == PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA)
            {
                detalle = $"{TextosCertezaFechaEntrega.TEXTO_CASI_SEGURA}: {TextosCertezaFechaEntrega.Envio(p.AgenciaSeguimiento)}";
            }
            else if (p.Recalculada)
            {
                detalle = $"{TEXTO_RECALCULADA}; fecha aproximada";
            }
            else
            {
                detalle = TextosCertezaFechaEntrega.TEXTO_APROXIMADA;
            }
            string texto = $"{cabecera}: {Dia(p.Fecha.Value, hoy)} ({detalle})";
            if (p.PedidosEnElAire > 1)
            {
                texto += $". Hay {p.PedidosEnElAire} pedidos en camino ({p.CantidadEnElAire} unidades)";
            }
            return texto;
        }

        /// <summary>
        /// «Orientativa: no hay pedido; si se pidiera hoy, llegaría hacia el 03/12. El próximo pedido habitual a COSMÉTICA X sería hacia el
        /// 20/11 y llegaría hacia el 08/12 (orientativo, no es un compromiso)».
        /// </summary>
        public static string Orientativa(ProximaEntradaProductoDTO p, DateTime hoy)
        {
            var partes = new List<string>();
            string inicio = $"{TextosCertezaFechaEntrega.ETIQUETA_ORIENTATIVA}: no hay pedido";
            partes.Add(p.FechaSiSePidieraHoy.HasValue
                ? $"{inicio}; si se pidiera hoy, llegaría hacia el {Dia(p.FechaSiSePidieraHoy.Value, hoy)}."
                : $"{inicio}.");
            if (p.FechaProximoPedidoHabitual.HasValue && p.LlegadaConPedidoHabitual.HasValue)
            {
                string proveedor = ReglaProximaEntrada.NombreParaTexto(p.ProveedorHabitual, p.NombreProveedorHabitual);
                partes.Add($"El próximo pedido habitual a {proveedor} sería hacia el {Dia(p.FechaProximoPedidoHabitual.Value, hoy)} " +
                    $"y llegaría hacia el {Dia(p.LlegadaConPedidoHabitual.Value, hoy)} ({TextosCertezaFechaEntrega.TEXTO_ORIENTATIVA}).");
            }
            else
            {
                partes[0] = partes[0].TrimEnd('.') + $" ({TextosCertezaFechaEntrega.TEXTO_ORIENTATIVA}).";
            }
            return string.Join(" ", partes);
        }
    }

    /// <summary>
    /// NestoAPI#869: GET api/Productos/ProximaEntrada. Lee las compras en el aire, los correos aplicados (#857), los plazos (#853, con caché)
    /// y el proveedor habitual con <see cref="ILectorCertezaFechaEntrega"/> (el mismo lector que la certeza del motor) y decide con
    /// <see cref="ReglaProximaEntrada"/>.
    /// </summary>
    public class ServicioProximaEntradaProducto
    {
        private readonly IRepositorioProximaEntrada repositorio;
        private readonly ILectorCertezaFechaEntrega lector;
        private readonly Func<DateTime> hoy;

        public ServicioProximaEntradaProducto(IRepositorioProximaEntrada repositorio, ILectorCertezaFechaEntrega lector, Func<DateTime> hoy)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        /// <summary>Null si el producto no existe.</summary>
        public ProximaEntradaProductoDTO Calcular(string empresa, string producto, string almacen)
        {
            empresa = CachePlazosProveedores.EmpresaPlazos(empresa);
            producto = producto?.Trim();
            almacen = string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant();
            bool? ficticio = repositorio.EsFicticio(empresa, producto);
            if (!ficticio.HasValue)
            {
                return null;
            }
            if (ficticio.Value)
            {
                return ReglaProximaEntrada.Ficticio(producto, almacen);
            }

            List<LineaCompraEnElAire> lineas = repositorio.EnElAire(empresa, producto, almacen) ?? new List<LineaCompraEnElAire>();
            List<CorreoAplicadoPedidoCompra> correos = lineas.Any()
                ? lector.CorreosAplicados(lineas.Select(l => l.Pedido).Distinct()) ?? new List<CorreoAplicadoPedidoCompra>()
                : new List<CorreoAplicadoPedidoCompra>();
            PlazosProveedores plazos = lector.Plazos(empresa);
            Dictionary<string, string> habituales = lector.ProveedorHabitual(empresa, new[] { producto }) ?? new Dictionary<string, string>();
            habituales.TryGetValue(producto, out string habitual);

            IEnumerable<string> proveedores = lineas.Select(l => l.Proveedor?.Trim()).Concat(new[] { habitual?.Trim() })
                .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, string> nombres = proveedores.Any()
                ? repositorio.NombresProveedores(empresa, proveedores) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();

            return ReglaProximaEntrada.Calcular(hoy(), producto, almacen, lineas, correos, plazos, habitual, nombres);
        }
    }

    /// <summary>NestoAPI#869: <see cref="IRepositorioProximaEntrada"/> con ADO.NET (<c>Enlist=false</c>, NOLOCK, solo lectura).</summary>
    public class RepositorioProximaEntradaSql : IRepositorioProximaEntrada
    {
        private const int TIMEOUT = 15;

        internal const string SQL_FICTICIO = "SELECT CAST(Ficticio AS bit) FROM Productos WITH (NOLOCK) WHERE Empresa = @empresa AND [Número] = @producto";

        internal const string SQL_EN_EL_AIRE = @"
SELECT RTRIM(l.Empresa), l.[Número], RTRIM(c.[NºProveedor]), CAST(l.[FechaRecepción] AS datetime), CAST(ISNULL(l.Cantidad, 0) AS int), RTRIM(l.Usuario)
FROM LinPedidoCmp l WITH (NOLOCK)
INNER JOIN CabPedidoCmp c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
WHERE l.Empresa IN (@empresa, @espejo) AND l.Producto = @producto AND l.[Almacén] = @almacen AND l.[TipoLínea] = '1'
  AND l.Estado IN (-1, 1) AND l.Enviado = 1";

        internal const string SQL_NOMBRES = @"
SELECT RTRIM(p.[Número]), MIN(RTRIM(p.Nombre))
FROM Proveedores p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.[Número] IN ({0}) AND (p.ProveedorPrincipal = 1 OR NOT EXISTS
    (SELECT 1 FROM Proveedores q WITH (NOLOCK) WHERE q.Empresa = p.Empresa AND q.[Número] = p.[Número] AND q.ProveedorPrincipal = 1))
GROUP BY p.[Número]";

        private readonly string cadenaConexion;

        public RepositorioProximaEntradaSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static RepositorioProximaEntradaSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioProximaEntradaSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = RepositorioPlazosProveedores.NOMBRE_APLICACION
            }.ConnectionString);
        }

        public bool? EsFicticio(string empresa, string producto)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_FICTICIO, conexion) { CommandTimeout = TIMEOUT })
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
                comando.Parameters.Add("@producto", SqlDbType.VarChar, 15).Value = producto ?? string.Empty;
                conexion.Open();
                object valor = comando.ExecuteScalar();
                return valor == null ? (bool?)null : valor != DBNull.Value && Convert.ToBoolean(valor);
            }
        }

        public List<LineaCompraEnElAire> EnElAire(string empresa, string producto, string almacen)
        {
            var resultado = new List<LineaCompraEnElAire>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_EN_EL_AIRE, conexion) { CommandTimeout = TIMEOUT })
            {
                Empresas(comando, empresa);
                comando.Parameters.Add("@producto", SqlDbType.VarChar, 15).Value = producto ?? string.Empty;
                comando.Parameters.Add("@almacen", SqlDbType.VarChar, 3).Value = almacen ?? Constantes.Almacenes.ALGETE;
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        resultado.Add(new LineaCompraEnElAire
                        {
                            Empresa = lector.IsDBNull(0) ? null : lector.GetString(0),
                            Pedido = lector.GetInt32(1),
                            Proveedor = lector.IsDBNull(2) ? null : lector.GetString(2),
                            FechaRecepcion = lector.IsDBNull(3) ? (DateTime?)null : lector.GetDateTime(3),
                            Cantidad = lector.GetInt32(4),
                            Usuario = lector.IsDBNull(5) ? null : lector.GetString(5)
                        });
                    }
                }
            }
            return resultado;
        }

        public Dictionary<string, string> NombresProveedores(string empresa, IEnumerable<string> proveedores)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = (proveedores ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!lista.Any())
            {
                return resultado;
            }
            string sql = string.Format(SQL_NOMBRES, string.Join(", ", lista.Select((p, i) => "@v" + i)));
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT })
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
                for (int i = 0; i < lista.Count; i++)
                {
                    comando.Parameters.Add("@v" + i, SqlDbType.VarChar, 10).Value = lista[i];
                }
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        if (!lector.IsDBNull(0) && !lector.IsDBNull(1))
                        {
                            resultado[lector.GetString(0)] = lector.GetString(1);
                        }
                    }
                }
            }
            return resultado;
        }

        private static void Empresas(SqlCommand comando, string empresa)
        {
            comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
            comando.Parameters.Add("@espejo", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
        }
    }
}
