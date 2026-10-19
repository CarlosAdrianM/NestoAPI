using ClosedXML.Excel;
using Hangfire;
using NestoAPI.Models;
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
    /// NestoAPI#674: un código que el almacén ha marcado como que «no se lee» con el escáner. Llegará con Ariadna#43; hasta
    /// entonces la sección del informe sale vacía.
    /// </summary>
    public class CodigoQueNoSeLeeInforme
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Codigo { get; set; }
        public int Veces { get; set; }
        public DateTime? Ultima { get; set; }
    }

    /// <summary>
    /// NestoAPI#674: un producto que el usuario ha tenido que teclear (no se ha podido leer su código). Llegará con Ariadna#43;
    /// hasta entonces la sección del informe sale vacía.
    /// </summary>
    public class EntradaTecleadaInforme
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Usuario { get; set; }
        public int Veces { get; set; }
        public DateTime? Ultima { get; set; }
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
        /// <summary>Ariadna#43 (pendiente): de momento, siempre vacía.</summary>
        public List<CodigoQueNoSeLeeInforme> NoSeLeen { get; set; } = new List<CodigoQueNoSeLeeInforme>();
        /// <summary>Ariadna#43 (pendiente): de momento, siempre vacía.</summary>
        public List<EntradaTecleadaInforme> Tecleadas { get; set; } = new List<EntradaTecleadaInforme>();

        /// <summary>Productos (no ficticios) con códigos, pero ninguno válido: al leer cuentan como «sin código».</summary>
        public int SoloConNoValidos => CodigosNoValidos.Where(c => c.SinNingunoValido).Select(c => c.Producto).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        public const string DONDE_FICHA = "Ficha";
        public const string DONDE_TABLA = "Tabla";

        /// <summary>Clasifica los productos activos y sus códigos. Sin base de datos: es la regla, testeable.</summary>
        public static InformeCodigosBarras Construir(IEnumerable<ProductoActivoCodigoBarras> productos, IEnumerable<CodigoActivoTabla> codigosTabla, DateTime fecha)
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
            return informe;
        }
    }

    public interface IRepositorioInformeCodigosBarras
    {
        /// <summary>Los productos activos (Estado &gt;= 0) de la empresa, con el código de su ficha.</summary>
        List<ProductoActivoCodigoBarras> LeerProductosActivos(string empresa);
        /// <summary>Los códigos activos de ProductosCodigosBarras de productos activos.</summary>
        List<CodigoActivoTabla> LeerCodigosActivosTabla(string empresa);
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
                deps.Ahora);
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
            sb.AppendLine(informe.NoSeLeen.Any()
                ? $"<p>{informe.NoSeLeen.Count} códigos marcados desde el almacén. La lista va en el Excel.</p>"
                : "<p>Ninguno. (El almacén podrá marcarlos desde Ariadna cuando esté hecho Ariadna#43.)</p>");

            sb.AppendLine("<h2>Productos tecleados en el almacén</h2>");
            sb.AppendLine(informe.Tecleadas.Any()
                ? $"<p>{informe.Tecleadas.Count} productos se han tenido que teclear. La lista va en el Excel.</p>"
                : "<p>Ninguno. (Llegará con Ariadna#43.)</p>");

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
                Hoja(libro, "No se leen", new[] { "Producto", "Nombre", "Código", "Veces", "Última" },
                    informe.NoSeLeen.Select(n => new object[] { n.Producto, n.Nombre, n.Codigo, n.Veces, n.Ultima }));
                Hoja(libro, "Tecleados", new[] { "Producto", "Nombre", "Usuario", "Veces", "Última" },
                    informe.Tecleadas.Select(t => new object[] { t.Producto, t.Nombre, t.Usuario, t.Veces, t.Ultima }));
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
