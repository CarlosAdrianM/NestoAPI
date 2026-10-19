using ClosedXML.Excel;
using Hangfire;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>NestoAPI#674: un producto activo (Estado &gt;= 0) con el código de su ficha.</summary>
    public class ProductoActivoCodigoBarras
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public short Estado { get; set; }
        /// <summary>Productos.CodBarras (el principal); null o vacío si no tiene.</summary>
        public string CodBarras { get; set; }
        /// <summary>Los ficticios (portes, servicios…) no tienen envase: no cuentan como «sin código».</summary>
        public bool Ficticio { get; set; }
    }

    /// <summary>NestoAPI#674: un código activo de ProductosCodigosBarras (NestoAPI#605).</summary>
    public class CodigoActivoTabla
    {
        public string Producto { get; set; }
        public string Codigo { get; set; }
    }

    /// <summary>NestoAPI#674: un código guardado que no es un GTIN válido.</summary>
    public class CodigoNoValidoInforme
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Codigo { get; set; }
        /// <summary>Ficha (Productos.CodBarras) o Tabla (ProductosCodigosBarras).</summary>
        public string Donde { get; set; }
        /// <summary>No le queda ningún código válido: al leer cuenta como «sin código».</summary>
        public bool SinNingunoValido { get; set; }
    }

    /// <summary>NestoAPI#674: un código válido que está activo en varios productos activos.</summary>
    public class CodigoCompartidoInforme
    {
        public string Codigo { get; set; }
        public List<ProductoActivoCodigoBarras> Productos { get; set; } = new List<ProductoActivoCodigoBarras>();
    }

    /// <summary>
    /// NestoAPI#674/Ariadna#43: un producto cuyo código el almacén ha marcado como que «no se lee» (al teclearlo, «¿Por qué?
    /// No se lee»; tabla ProductosMotivoTecleo). Salen todos los marcados, no solo los de la semana: siguen sin leerse hasta
    /// que Compras cambie el código.
    /// </summary>
    public class CodigoQueNoSeLeeInforme
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>El código válido que tenía el producto al marcarlo.</summary>
        public string Codigo { get; set; }
        /// <summary>Cuántas veces se ha contestado (normalmente una: se pregunta una vez por producto).</summary>
        public int Veces { get; set; }
        /// <summary>Cuándo se marcó (la última vez).</summary>
        public DateTime? Ultima { get; set; }
        /// <summary>Quién lo marcó (la última vez).</summary>
        public string Usuario { get; set; }
        /// <summary>Entradas tecleadas de este producto en la semana del informe.</summary>
        public int TecleadasSemana { get; set; }
    }

    /// <summary>
    /// NestoAPI#674/Ariadna#43: un producto que un usuario ha tecleado en la semana (PreparacionEscaneos con Metodo TECLA, en
    /// el recorrido y en el packing de Ariadna).
    /// </summary>
    public class EntradaTecleadaInforme
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Usuario { get; set; }
        /// <summary>Entradas (lecturas o cantidades) tecleadas.</summary>
        public int Veces { get; set; }
        public int Unidades { get; set; }
        public DateTime? Ultima { get; set; }
        /// <summary>El producto tiene algún código válido: se podría haber leído.</summary>
        public bool TieneCodigoValido { get; set; }
        /// <summary>Lo que se contestó al «¿Por qué?» (No se lee / No lleva código / Otro); null si no se preguntó.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>Ariadna#43: lo tecleado en la semana por un usuario, para ver si baja.</summary>
    public class TecleadoPorUsuarioInforme
    {
        public string Usuario { get; set; }
        public int Entradas { get; set; }
        public int Unidades { get; set; }
        public int Productos { get; set; }
        /// <summary>De las entradas, las de productos con código válido (las que se podrían haber leído).</summary>
        public int EntradasConCodigo { get; set; }
    }

    /// <summary>NestoAPI#674: el informe semanal de códigos de barras para Compras.</summary>
    public class InformeCodigosBarras
    {
        public DateTime Fecha { get; set; }
        public int ProductosActivos { get; set; }
        /// <summary>Sin ningún código (ni en la ficha ni en la tabla). Sin los ficticios.</summary>
        public List<ProductoActivoCodigoBarras> SinCodigo { get; set; } = new List<ProductoActivoCodigoBarras>();
        public List<CodigoNoValidoInforme> CodigosNoValidos { get; set; } = new List<CodigoNoValidoInforme>();
        public List<CodigoCompartidoInforme> Compartidos { get; set; } = new List<CodigoCompartidoInforme>();
        /// <summary>Ariadna#43: los productos marcados como «No se lee».</summary>
        public List<CodigoQueNoSeLeeInforme> NoSeLeen { get; set; } = new List<CodigoQueNoSeLeeInforme>();
        /// <summary>Ariadna#43: lo tecleado en la semana, por producto y usuario.</summary>
        public List<EntradaTecleadaInforme> Tecleadas { get; set; } = new List<EntradaTecleadaInforme>();

        /// <summary>Ariadna#43: <see cref="Tecleadas"/> resumido por usuario (el que más teclea, primero).</summary>
        public List<TecleadoPorUsuarioInforme> TecleadasPorUsuario => Tecleadas
            .GroupBy(t => t.Usuario?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TecleadoPorUsuarioInforme
            {
                Usuario = g.Key,
                Entradas = g.Sum(t => t.Veces),
                Unidades = g.Sum(t => t.Unidades),
                Productos = g.Select(t => t.Producto?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                EntradasConCodigo = g.Where(t => t.TieneCodigoValido).Sum(t => t.Veces)
            })
            .OrderByDescending(u => u.Entradas)
            .ThenBy(u => u.Usuario, StringComparer.OrdinalIgnoreCase)
            .ToList();

        /// <summary>Ariadna#43: los días que mira el informe hacia atrás para lo tecleado.</summary>
        public const int DIAS_TECLEADAS = 7;

        /// <summary>Productos (no ficticios) con códigos, pero ninguno válido: al leer cuentan como «sin código».</summary>
        public int SoloConNoValidos => CodigosNoValidos.Where(c => c.SinNingunoValido).Select(c => c.Producto).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        public const string DONDE_FICHA = "Ficha";
        public const string DONDE_TABLA = "Tabla";

        /// <summary>
        /// Clasifica los productos activos y sus códigos. Sin base de datos: es la regla, testeable. Ariadna#43:
        /// <paramref name="motivosTecleo"/> (ProductosMotivoTecleo) da los que «no se leen» y <paramref name="tecleadas"/> (lo
        /// tecleado en la semana) la sección de tecleados.
        /// </summary>
        public static InformeCodigosBarras Construir(IEnumerable<ProductoActivoCodigoBarras> productos, IEnumerable<CodigoActivoTabla> codigosTabla, DateTime fecha,
            IEnumerable<MotivoTecleoGuardado> motivosTecleo = null, IEnumerable<EntradaTecleadaInforme> tecleadas = null)
        {
            List<ProductoActivoCodigoBarras> activos = (productos ?? Enumerable.Empty<ProductoActivoCodigoBarras>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            ILookup<string, string> tabla = (codigosTabla ?? Enumerable.Empty<CodigoActivoTabla>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Producto) && !string.IsNullOrWhiteSpace(c.Codigo))
                .ToLookup(c => c.Producto.Trim(), c => c.Codigo.Trim(), StringComparer.OrdinalIgnoreCase);

            var informe = new InformeCodigosBarras { Fecha = fecha, ProductosActivos = activos.Count };
            // Ariadna#43: los productos que tienen algún código válido (para saber si lo tecleado se podía haber leído)
            var conCodigoValido = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // código válido → productos que lo tienen activo
            var porCodigo = new Dictionary<string, List<ProductoActivoCodigoBarras>>(StringComparer.OrdinalIgnoreCase);

            foreach (ProductoActivoCodigoBarras producto in activos)
            {
                string numero = producto.Producto.Trim();
                var codigos = new List<KeyValuePair<string, string>>();
                if (!string.IsNullOrWhiteSpace(producto.CodBarras))
                {
                    codigos.Add(new KeyValuePair<string, string>(producto.CodBarras.Trim(), DONDE_FICHA));
                }
                foreach (string codigo in tabla[numero])
                {
                    if (!codigos.Any(c => string.Equals(c.Key, codigo, StringComparison.OrdinalIgnoreCase)))
                    {
                        codigos.Add(new KeyValuePair<string, string>(codigo, DONDE_TABLA));
                    }
                }

                if (!codigos.Any())
                {
                    if (!producto.Ficticio)
                    {
                        informe.SinCodigo.Add(producto);
                    }
                    continue;
                }

                bool algunoValido = codigos.Any(c => ValidadorGtin.EsCodigoValido(c.Key));
                if (algunoValido)
                {
                    _ = conCodigoValido.Add(numero);
                }
                foreach (KeyValuePair<string, string> codigo in codigos)
                {
                    if (!ValidadorGtin.EsCodigoValido(codigo.Key))
                    {
                        informe.CodigosNoValidos.Add(new CodigoNoValidoInforme
                        {
                            Producto = numero,
                            Nombre = producto.Nombre?.Trim(),
                            Familia = producto.Familia?.Trim(),
                            Codigo = codigo.Key,
                            Donde = codigo.Value,
                            SinNingunoValido = !algunoValido && !producto.Ficticio
                        });
                        continue;
                    }
                    if (!porCodigo.TryGetValue(codigo.Key, out List<ProductoActivoCodigoBarras> suyos))
                    {
                        suyos = new List<ProductoActivoCodigoBarras>();
                        porCodigo[codigo.Key] = suyos;
                    }
                    suyos.Add(producto);
                }
            }

            informe.Compartidos = porCodigo
                .Where(kv => kv.Value.Count > 1)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new CodigoCompartidoInforme { Codigo = kv.Key, Productos = kv.Value })
                .ToList();
            AnadirTecleos(informe, activos, conCodigoValido, motivosTecleo, tecleadas);
            return informe;
        }

        /// <summary>Ariadna#43: las secciones «No se leen» y «Tecleados».</summary>
        private static void AnadirTecleos(InformeCodigosBarras informe, List<ProductoActivoCodigoBarras> activos, HashSet<string> conCodigoValido,
            IEnumerable<MotivoTecleoGuardado> motivosTecleo, IEnumerable<EntradaTecleadaInforme> tecleadas)
        {
            Dictionary<string, string> nombres = activos.ToDictionary(p => p.Producto.Trim(), p => p.Nombre?.Trim(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, MotivoTecleoGuardado> motivos = (motivosTecleo ?? Enumerable.Empty<MotivoTecleoGuardado>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Producto))
                .GroupBy(m => m.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.FechaModificacion).First(), StringComparer.OrdinalIgnoreCase);
            List<EntradaTecleadaInforme> semana = (tecleadas ?? Enumerable.Empty<EntradaTecleadaInforme>())
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Producto))
                .ToList();

            foreach (EntradaTecleadaInforme t in semana)
            {
                t.Producto = t.Producto.Trim();
                t.Usuario = t.Usuario?.Trim();
                t.Nombre = t.Nombre?.Trim() ?? (nombres.TryGetValue(t.Producto, out string nombre) ? nombre : null);
                t.TieneCodigoValido = conCodigoValido.Contains(t.Producto);
                t.Motivo = motivos.TryGetValue(t.Producto, out MotivoTecleoGuardado motivo) ? TextoMotivo(motivo.Motivo) : null;
            }
            informe.Tecleadas = semana
                .OrderBy(t => t.Usuario, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(t => t.Veces)
                .ThenBy(t => t.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();

            informe.NoSeLeen = motivos.Values
                .Where(m => string.Equals(m.Motivo?.Trim(), MotivosTecleo.NO_SE_LEE, StringComparison.OrdinalIgnoreCase))
                .Select(m => new CodigoQueNoSeLeeInforme
                {
                    Producto = m.Producto.Trim(),
                    Nombre = m.Nombre?.Trim() ?? (nombres.TryGetValue(m.Producto.Trim(), out string nombre) ? nombre : null),
                    Codigo = m.Codigo?.Trim(),
                    Veces = m.Veces,
                    Ultima = m.FechaModificacion,
                    Usuario = m.Usuario?.Trim(),
                    TecleadasSemana = semana.Where(t => string.Equals(t.Producto, m.Producto.Trim(), StringComparison.OrdinalIgnoreCase)).Sum(t => t.Veces)
                })
                .OrderByDescending(n => n.TecleadasSemana)
                .ThenBy(n => n.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Ariadna#43: NOSELEE → «No se lee», como lo vio el mozo.</summary>
        public static string TextoMotivo(string motivo)
        {
            switch (motivo?.Trim().ToUpperInvariant())
            {
                case MotivosTecleo.NO_SE_LEE:
                    return "No se lee";
                case MotivosTecleo.SIN_CODIGO:
                    return "No lleva código";
                case MotivosTecleo.OTRO:
                    return "Otro";
                default:
                    return motivo?.Trim();
            }
        }
    }

    public interface IRepositorioInformeCodigosBarras
    {
        /// <summary>Los productos activos (Estado &gt;= 0) de la empresa, con el código de su ficha.</summary>
        List<ProductoActivoCodigoBarras> LeerProductosActivos(string empresa);
        /// <summary>Los códigos activos de ProductosCodigosBarras de productos activos.</summary>
        List<CodigoActivoTabla> LeerCodigosActivosTabla(string empresa);
        /// <summary>Ariadna#43: ProductosMotivoTecleo entera (vacía si aún no se ha lanzado el script).</summary>
        List<MotivoTecleoGuardado> LeerMotivosTecleo(string empresa);
        /// <summary>Ariadna#43: lo tecleado (Metodo TECLA) desde <paramref name="desde"/>, por producto y usuario.</summary>
        List<EntradaTecleadaInforme> LeerTecleadas(string empresa, DateTime desde);
    }

    /// <summary>NestoAPI#674: ADO.NET con NOLOCK (como el correo de proveedores vencidos, #606): se lee todo una vez por semana.</summary>
    public class RepositorioInformeCodigosBarrasSql : IRepositorioInformeCodigosBarras
    {
        private const int TIMEOUT_SEGUNDOS = 60;

        internal const string SQL_PRODUCTOS = @"
SELECT RTRIM(p.[Número]), RTRIM(p.Nombre), RTRIM(p.Familia), ISNULL(p.Estado, 0), RTRIM(p.CodBarras), p.Ficticio
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.Estado >= 0";

        internal const string SQL_CODIGOS_TABLA = @"
SELECT RTRIM(c.Producto), RTRIM(c.Codigo)
FROM ProductosCodigosBarras c WITH (NOLOCK)
     JOIN Productos p WITH (NOLOCK) ON p.Empresa = c.Empresa AND p.[Número] = c.Producto
WHERE c.Empresa = @empresa AND c.Activo = 1 AND p.Estado >= 0";

        // Ariadna#43: sin el script (la tabla no existe) no falla: no hay ninguno marcado
        internal const string SQL_MOTIVOS_TECLEO = @"
IF OBJECT_ID('dbo.ProductosMotivoTecleo') IS NOT NULL
    SELECT RTRIM(m.Producto), RTRIM(p.Nombre), RTRIM(m.Motivo), RTRIM(m.Codigo), m.Veces, RTRIM(m.Usuario), m.FechaModificacion
    FROM ProductosMotivoTecleo m WITH (NOLOCK)
         LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = m.Empresa AND p.[Número] = m.Producto
    WHERE m.Empresa = @empresa";

        // Ariadna#43: lo tecleado (no los «deshacer», que van en negativo)
        internal const string SQL_TECLEADAS = @"
SELECT RTRIM(e.Producto), RTRIM(p.Nombre), RTRIM(e.Usuario), COUNT(*), CAST(SUM(e.Cantidad) AS int), MAX(e.FechaEscaneo)
FROM PreparacionEscaneos e WITH (NOLOCK)
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = e.Empresa AND p.[Número] = e.Producto
WHERE e.Empresa = @empresa AND e.Metodo = '" + CasadorEscaneos.METODO_TECLADO + @"' AND e.Cantidad > 0 AND e.FechaEscaneo >= @desde
GROUP BY e.Producto, p.Nombre, e.Usuario";

        private readonly string cadenaConexion;

        public RepositorioInformeCodigosBarrasSql(string cadenaConexion)
        {
            this.cadenaConexion = string.IsNullOrWhiteSpace(cadenaConexion) ? throw new ArgumentNullException(nameof(cadenaConexion)) : cadenaConexion;
        }

        public static RepositorioInformeCodigosBarrasSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"]
                ?? throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            return new RepositorioInformeCodigosBarrasSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = "NestoAPI-InformeCodigosBarras"
            }.ConnectionString);
        }

        public List<ProductoActivoCodigoBarras> LeerProductosActivos(string empresa)
        {
            var productos = new List<ProductoActivoCodigoBarras>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_PRODUCTOS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        productos.Add(new ProductoActivoCodigoBarras
                        {
                            Producto = Cadena(lector, 0),
                            Nombre = Cadena(lector, 1),
                            Familia = Cadena(lector, 2),
                            Estado = Convert.ToInt16(lector.GetValue(3), CultureInfo.InvariantCulture),
                            CodBarras = Cadena(lector, 4),
                            Ficticio = !lector.IsDBNull(5) && lector.GetBoolean(5)
                        });
                    }
                }
            }
            return productos;
        }

        public List<CodigoActivoTabla> LeerCodigosActivosTabla(string empresa)
        {
            var codigos = new List<CodigoActivoTabla>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_CODIGOS_TABLA, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        codigos.Add(new CodigoActivoTabla { Producto = Cadena(lector, 0), Codigo = Cadena(lector, 1) });
                    }
                }
            }
            return codigos;
        }

        public List<MotivoTecleoGuardado> LeerMotivosTecleo(string empresa)
        {
            var motivos = new List<MotivoTecleoGuardado>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_MOTIVOS_TECLEO, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        motivos.Add(new MotivoTecleoGuardado
                        {
                            Producto = Cadena(lector, 0),
                            Nombre = Cadena(lector, 1),
                            Motivo = Cadena(lector, 2),
                            Codigo = Cadena(lector, 3),
                            Veces = lector.IsDBNull(4) ? 0 : Convert.ToInt32(lector.GetValue(4), CultureInfo.InvariantCulture),
                            Usuario = Cadena(lector, 5),
                            FechaModificacion = lector.GetDateTime(6)
                        });
                    }
                }
            }
            return motivos;
        }

        public List<EntradaTecleadaInforme> LeerTecleadas(string empresa, DateTime desde)
        {
            var tecleadas = new List<EntradaTecleadaInforme>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_TECLEADAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        tecleadas.Add(new EntradaTecleadaInforme
                        {
                            Producto = Cadena(lector, 0),
                            Nombre = Cadena(lector, 1),
                            Usuario = Cadena(lector, 2),
                            Veces = Convert.ToInt32(lector.GetValue(3), CultureInfo.InvariantCulture),
                            Unidades = lector.IsDBNull(4) ? 0 : Convert.ToInt32(lector.GetValue(4), CultureInfo.InvariantCulture),
                            Ultima = lector.IsDBNull(5) ? (DateTime?)null : lector.GetDateTime(5)
                        });
                    }
                }
            }
            return tecleadas;
        }

        private static string Cadena(SqlDataReader lector, int indice) => lector.IsDBNull(indice) ? null : lector.GetString(indice);
    }

    /// <summary>Dependencias del job, inyectables para testearlo sin BD ni SMTP.</summary>
    internal class DependenciasInformeCodigosBarras
    {
        public IRepositorioInformeCodigosBarras Repositorio { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        public DateTime Ahora { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// NestoAPI#674 (decisión de Carlos, 19/10/26): correo semanal a Compras con los productos activos sin código de barras,
    /// con un código que no es un GTIN válido y con un código compartido por varios productos. Lleva ya las secciones de los
    /// códigos que «no se leen» y de las entradas tecleadas, que llegarán con Ariadna#43 (hasta entonces, vacías). Solo con el
    /// interruptor <see cref="CLAVE_INTERRUPTOR"/> a true en Web.config.
    /// </summary>
    public static class InformeCodigosBarrasJobsService
    {
        public const string ID_JOB = "informe-codigos-barras";
        public const string CRON = "10 8 * * 1"; // los lunes a las 8:10
        public const string CLAVE_INTERRUPTOR = "CodigosBarras:InformeSemanal";
        private static readonly string DIAS_TECLEADAS_TEXTO = $"{InformeCodigosBarras.DIAS_TECLEADAS} días";
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>Web.config <c>CodigosBarras:InformeSemanal</c> = true. Sin la clave, apagado.</summary>
        public static bool Activo() =>
            string.Equals(ConfigurationManager.AppSettings[CLAVE_INTERRUPTOR]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static void Procesar()
        {
            try
            {
                if (!Activo())
                {
                    return;
                }
                _ = Procesar(new DependenciasInformeCodigosBarras
                {
                    Repositorio = RepositorioInformeCodigosBarrasSql.DesdeConfiguracion(),
                    Correo = new ServicioCorreoElectronico(),
                    Ahora = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Códigos de barras #674] Error en el informe semanal a Compras: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job. Devuelve true si ha mandado el correo.</summary>
        internal static bool Procesar(DependenciasInformeCodigosBarras deps)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            InformeCodigosBarras informe = InformeCodigosBarras.Construir(
                deps.Repositorio.LeerProductosActivos(empresa),
                deps.Repositorio.LeerCodigosActivosTabla(empresa),
                deps.Ahora,
                deps.Repositorio.LeerMotivosTecleo(empresa),
                deps.Repositorio.LeerTecleadas(empresa, deps.Ahora.Date.AddDays(-InformeCodigosBarras.DIAS_TECLEADAS)));
            using (MailMessage correo = ConstruirCorreo(informe))
            {
                if (!deps.Correo.EnviarCorreoSMTP(correo))
                {
                    ElmahHelper.Log(new Exception("[Códigos de barras #674] No se ha podido mandar el informe semanal a Compras."));
                    return false;
                }
            }
            return true;
        }

        internal static MailMessage ConstruirCorreo(InformeCodigosBarras informe)
        {
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Códigos de barras ({informe.Fecha.ToString("dd/MM/yyyy", es)}): {informe.SinCodigo.Count} sin código, " +
                    $"{informe.CodigosNoValidos.Count} no {(informe.CodigosNoValidos.Count == 1 ? "válido" : "válidos")}, " +
                    $"{informe.Compartidos.Count} {(informe.Compartidos.Count == 1 ? "compartido" : "compartidos")}",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.COMPRAS);
            mail.Body = GenerarHtml(informe);
            mail.Attachments.Add(new Attachment(new MemoryStream(GenerarExcel(informe)), $"CodigosBarras_{informe.Fecha:yyyy-MM-dd}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            return mail;
        }

        internal static string GenerarHtml(InformeCodigosBarras informe)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<p>Estado de los códigos de barras de los productos activos. Un código que no es un código de barras válido " +
                "(EAN-8, EAN-13, UPC-A o ITF-14 con su dígito de control) no casa con ninguna lectura del escáner y el producto cuenta " +
                "como «sin código». La lista completa va en el Excel adjunto.</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine($"<li>Productos activos: {informe.ProductosActivos}.</li>");
            sb.AppendLine($"<li>Sin ningún código: {informe.SinCodigo.Count} (sin contar los ficticios).</li>");
            sb.AppendLine($"<li>Códigos no válidos: {informe.CodigosNoValidos.Count}; {informe.SoloConNoValidos} " +
                $"{(informe.SoloConNoValidos == 1 ? "producto se queda" : "productos se quedan")} sin ningún código válido.</li>");
            sb.AppendLine($"<li>Códigos compartidos por varios productos: {informe.Compartidos.Count}.</li>");
            sb.AppendLine("</ul>");

            sb.AppendLine("<h2>Códigos no válidos</h2>");
            if (informe.CodigosNoValidos.Any())
            {
                sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
                sb.AppendLine("<tr style='background:#eee'><th>Producto</th><th>Nombre</th><th>Familia</th><th>Código</th><th>Dónde</th><th>¿Le queda otro válido?</th></tr>");
                foreach (CodigoNoValidoInforme c in informe.CodigosNoValidos)
                {
                    sb.Append("<tr>")
                        .Append($"<td>{Html(c.Producto)}</td><td>{Html(c.Nombre)}</td><td>{Html(c.Familia)}</td>")
                        .Append($"<td>«{Html(c.Codigo)}»</td><td>{Html(c.Donde)}</td><td>{(c.SinNingunoValido ? "No" : "Sí")}</td>")
                        .AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }
            else
            {
                sb.AppendLine("<p>Ninguno.</p>");
            }

            sb.AppendLine("<h2>Códigos compartidos por varios productos</h2>");
            if (informe.Compartidos.Any())
            {
                sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
                sb.AppendLine("<tr style='background:#eee'><th>Código</th><th>Productos</th></tr>");
                foreach (CodigoCompartidoInforme c in informe.Compartidos)
                {
                    sb.Append("<tr>")
                        .Append($"<td>{Html(c.Codigo)}</td>")
                        .Append($"<td>{string.Join("<br>", c.Productos.Select(p => $"{Html(p.Producto)} {Html(p.Nombre)}"))}</td>")
                        .AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
                sb.AppendLine("<p>Si es el mismo envase para varios productos a propósito (los guantes), se puede dejar así.</p>");
            }
            else
            {
                sb.AppendLine("<p>Ninguno.</p>");
            }

            sb.AppendLine("<h2>Productos sin código</h2>");
            sb.AppendLine(informe.SinCodigo.Any()
                ? $"<p>{informe.SinCodigo.Count} productos activos no tienen ningún código. La lista va en la hoja «Sin código» del Excel.</p>"
                : "<p>Ninguno.</p>");

            sb.AppendLine("<h2>Códigos que no se leen</h2>");
            if (informe.NoSeLeen.Any())
            {
                sb.AppendLine("<p>Productos que el almacén ha tenido que teclear porque su código no se lee (ni con el lector ni con la cámara). " +
                    "Conviene cambiarles el código o pegarles nuestra etiqueta.</p>");
                sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
                sb.AppendLine("<tr style='background:#eee'><th>Producto</th><th>Nombre</th><th>Código</th><th>Marcado</th><th>Por</th><th>Tecleado esta semana</th></tr>");
                foreach (CodigoQueNoSeLeeInforme n in informe.NoSeLeen)
                {
                    sb.Append("<tr>")
                        .Append($"<td>{Html(n.Producto)}</td><td>{Html(n.Nombre)}</td><td>{Html(n.Codigo)}</td>")
                        .Append($"<td>{(n.Ultima.HasValue ? n.Ultima.Value.ToString("dd/MM/yyyy", es) : string.Empty)}</td><td>{Html(n.Usuario)}</td>")
                        .Append($"<td>{n.TecleadasSemana}</td>")
                        .AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }
            else
            {
                sb.AppendLine("<p>Ninguno.</p>");
            }

            sb.AppendLine("<h2>Productos tecleados en el almacén</h2>");
            if (informe.Tecleadas.Any())
            {
                sb.AppendLine($"<p>Lo tecleado en Ariadna (recorrido y packing) en los últimos {DIAS_TECLEADAS_TEXTO}, por usuario. " +
                    "«Con código» son las entradas de productos que tienen un código válido: se podrían haber leído. El detalle por producto va en la hoja «Tecleados» del Excel.</p>");
                sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
                sb.AppendLine("<tr style='background:#eee'><th>Usuario</th><th>Entradas</th><th>Unidades</th><th>Productos</th><th>Con código</th></tr>");
                foreach (TecleadoPorUsuarioInforme u in informe.TecleadasPorUsuario)
                {
                    sb.Append("<tr>")
                        .Append($"<td>{Html(u.Usuario)}</td><td>{u.Entradas}</td><td>{u.Unidades}</td><td>{u.Productos}</td><td>{u.EntradasConCodigo}</td>")
                        .AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }
            else
            {
                sb.AppendLine("<p>Ninguno.</p>");
            }

            sb.AppendLine("<p style='color:#666'>Se calcula cada lunes en el momento de mandar el correo (NestoAPI#674).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        internal static byte[] GenerarExcel(InformeCodigosBarras informe)
        {
            using (var libro = new XLWorkbook())
            {
                Hoja(libro, "Sin código", new[] { "Producto", "Nombre", "Familia", "Estado" },
                    informe.SinCodigo.Select(p => new object[] { p.Producto?.Trim(), p.Nombre?.Trim(), p.Familia?.Trim(), (int)p.Estado }));
                Hoja(libro, "Código no válido", new[] { "Producto", "Nombre", "Familia", "Código", "Dónde", "Le queda otro válido" },
                    informe.CodigosNoValidos.Select(c => new object[] { c.Producto, c.Nombre, c.Familia, c.Codigo, c.Donde, c.SinNingunoValido ? "No" : "Sí" }));
                Hoja(libro, "Compartidos", new[] { "Código", "Producto", "Nombre", "Familia" },
                    informe.Compartidos.SelectMany(c => c.Productos.Select(p => new object[] { c.Codigo, p.Producto?.Trim(), p.Nombre?.Trim(), p.Familia?.Trim() })));
                Hoja(libro, "No se leen", new[] { "Producto", "Nombre", "Código", "Veces", "Marcado", "Por", "Tecleado esta semana" },
                    informe.NoSeLeen.Select(n => new object[] { n.Producto, n.Nombre, n.Codigo, n.Veces, n.Ultima, n.Usuario, n.TecleadasSemana }));
                Hoja(libro, "Tecleados", new[] { "Usuario", "Producto", "Nombre", "Entradas", "Unidades", "Última", "Tiene código válido", "Por qué" },
                    informe.Tecleadas.Select(t => new object[] { t.Usuario, t.Producto, t.Nombre, t.Veces, t.Unidades, t.Ultima, t.TieneCodigoValido ? "Sí" : "No", t.Motivo }));
                using (var stream = new MemoryStream())
                {
                    libro.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        private static void Hoja(XLWorkbook libro, string nombre, string[] cabeceras, IEnumerable<object[]> filas)
        {
            IXLWorksheet hoja = libro.Worksheets.Add(nombre);
            for (int i = 0; i < cabeceras.Length; i++)
            {
                hoja.Cell(1, i + 1).Value = cabeceras[i];
            }
            hoja.Row(1).Style.Font.Bold = true;
            int fila = 2;
            foreach (object[] valores in filas)
            {
                for (int i = 0; i < valores.Length; i++)
                {
                    IXLCell celda = hoja.Cell(fila, i + 1);
                    switch (valores[i])
                    {
                        case int numero:
                            celda.Value = numero;
                            break;
                        case DateTime fecha:
                            celda.Value = fecha;
                            break;
                        case null:
                            break;
                        default:
                            // Los códigos como texto: un EAN como número pierde los ceros de delante y sale en notación científica
                            celda.Value = valores[i].ToString();
                            break;
                    }
                }
                fila++;
            }
            hoja.SheetView.FreezeRows(1);
            _ = hoja.Range(1, 1, Math.Max(1, fila - 1), cabeceras.Length).SetAutoFilter();
            _ = hoja.Columns().AdjustToContents();
        }

        private static string Html(string texto) => WebUtility.HtmlEncode(texto?.Trim() ?? string.Empty);
    }
}
