using NestoAPI.Models;
using System;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#873 (pedido 929555, 16/11/26): lo que se lee del pedido guardado, en UNA consulta
    /// (<see cref="LectorVersionPedidoSql"/>), para calcular su versión (<see cref="VersionPedidoVenta"/>) y saber quién lo tocó
    /// el último. Las propiedades se llaman como las columnas de la consulta.
    /// </summary>
    public class DatosVersionPedido
    {
        /// <summary>CabPedidoVta.RowVersion: cambia con cualquier UPDATE de la cabecera, lo haga quien lo haga.</summary>
        public byte[] RowVersionCabecera { get; set; }
        public string UsuarioCabecera { get; set; }
        public DateTime? FechaCabecera { get; set; }

        /// <summary>Cuántas líneas tiene y la mayor RowVersion de ellas: un alta o un cambio sube la mayor; una baja cambia el número.</summary>
        public int Lineas { get; set; }
        public byte[] RowVersionLineas { get; set; }
        public string UsuarioLinea { get; set; }
        public DateTime? FechaLinea { get; set; }

        /// <summary>Prepagos (sin RowVersion): cuántos, la suma de sus Id (una baja y un alta) y la huella de Id, importe, factura, cuenta y concepto.</summary>
        public int Prepagos { get; set; }
        public long? SumaIdPrepagos { get; set; }
        public int? HuellaPrepagos { get; set; }
        public string UsuarioPrepago { get; set; }
        public DateTime? FechaPrepago { get; set; }

        /// <summary>Efectos del pedido, igual que los prepagos (Id, vencimiento, importe, forma de pago y CCC).</summary>
        public int Efectos { get; set; }
        public long? SumaIdEfectos { get; set; }
        public int? HuellaEfectos { get; set; }
        public string UsuarioEfecto { get; set; }
        public DateTime? FechaEfecto { get; set; }
    }

    /// <summary>NestoAPI#873: quién y cuándo tocó el pedido por última vez (de lo que se ve en <see cref="DatosVersionPedido"/>).</summary>
    public class UltimaModificacionPedido
    {
        public string Usuario { get; set; }
        public DateTime? Fecha { get; set; }
    }

    /// <summary>
    /// NestoAPI#873: la versión del pedido, una cadena opaca que cambia con CUALQUIER guardado de la cabecera, de cualquier línea
    /// (alta, baja o cambio), de los prepagos o de los efectos, lo haga la API, Nesto viejo o un procedimiento. LA REGLA, pura: la
    /// calcula solo con lo leído en una consulta.
    /// - El GET de un pedido la lee ANTES de leer el pedido: si alguien guarda entre medias, la versión es la vieja y el siguiente
    ///   PUT da 409 (de más, nunca de menos).
    /// - El PUT con Version distinta de la de ahora → 409 sin guardar nada (<see cref="HayConflicto"/>). Sin Version, como antes.
    /// - Un guardado solo puede quitar los prepagos que no trae si manda la Version y coincide (<see cref="PuedeQuitarPrepagosQueNoVienen"/>).
    /// </summary>
    public static class VersionPedidoVenta
    {
        public const string CODIGO_CONFLICTO = "PEDIDO_CAMBIADO";
        public const string TIPO_AVISO_PREPAGO_CONSERVADO = "PrepagoConservado";

        /// <summary>La versión (32 caracteres hexadecimales) o null si no hay datos (el pedido no existe o no se ha podido leer).</summary>
        public static string Calcular(DatosVersionPedido datos)
        {
            if (datos == null)
            {
                return null;
            }
            string texto = string.Join("|",
                "c:" + Hex(datos.RowVersionCabecera),
                "l:" + datos.Lineas.ToString(CultureInfo.InvariantCulture) + ":" + Hex(datos.RowVersionLineas),
                "p:" + datos.Prepagos.ToString(CultureInfo.InvariantCulture) + ":" + Numero(datos.SumaIdPrepagos) + ":" + Numero(datos.HuellaPrepagos),
                "e:" + datos.Efectos.ToString(CultureInfo.InvariantCulture) + ":" + Numero(datos.SumaIdEfectos) + ":" + Numero(datos.HuellaEfectos));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
                return Hex(hash.Take(16).ToArray());
            }
        }

        /// <summary>
        /// Si el guardado viene con una versión vieja. Sin versión del cliente (NestoApp, TiendasNuevaVision, Nesto viejo) o sin
        /// versión actual (no se ha podido leer), no hay conflicto: como antes.
        /// </summary>
        public static bool HayConflicto(string versionCliente, string versionActual)
        {
            if (string.IsNullOrWhiteSpace(versionCliente) || string.IsNullOrWhiteSpace(versionActual))
            {
                return false;
            }
            return !string.Equals(versionCliente.Trim(), versionActual.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Si el guardado puede quitar los prepagos sin factura que no trae. Solo si mandó la versión y coincidía con la guardada
        /// (si no coincidiera, ya habría sido un 409): entonces vio todos los que hay. Sin versión, el guardado pudo hacerse con una
        /// copia de antes de que alguien metiera el prepago (pedido 929555) y no se quita ninguno.
        /// </summary>
        public static bool PuedeQuitarPrepagosQueNoVienen(bool versionComprobada) => versionComprobada;

        /// <summary>Quién y cuándo tocó el pedido por última vez: lo más reciente de cabecera, líneas, prepagos y efectos.</summary>
        public static UltimaModificacionPedido UltimaModificacion(DatosVersionPedido datos)
        {
            if (datos == null)
            {
                return null;
            }
            var candidatos = new[]
            {
                new UltimaModificacionPedido { Usuario = datos.UsuarioCabecera, Fecha = datos.FechaCabecera },
                new UltimaModificacionPedido { Usuario = datos.UsuarioLinea, Fecha = datos.FechaLinea },
                new UltimaModificacionPedido { Usuario = datos.UsuarioPrepago, Fecha = datos.FechaPrepago },
                new UltimaModificacionPedido { Usuario = datos.UsuarioEfecto, Fecha = datos.FechaEfecto }
            };
            UltimaModificacionPedido ultima = candidatos.Where(c => c.Fecha.HasValue).OrderByDescending(c => c.Fecha.Value).FirstOrDefault();
            return ultima == null ? null : new UltimaModificacionPedido { Usuario = SinDominio(ultima.Usuario), Fecha = ultima.Fecha };
        }

        /// <summary>
        /// El 409, de usted: «El pedido 929555 lo ha modificado Aida a las 15:14 desde que usted lo abrió. Recárguelo y vuelva a
        /// hacer los cambios.» Si no fue hoy, con el día; si fue quien guarda, lo dice; sin quién, sin nombre.
        /// </summary>
        public static string MensajeConflicto(int numero, UltimaModificacionPedido ultima, string usuarioQueGuarda, DateTime hoy)
        {
            const string RECARGUELO = "Recárguelo y vuelva a hacer los cambios.";
            string quien = SinDominio(ultima?.Usuario);
            string cuando = Cuando(ultima?.Fecha, hoy);
            if (string.IsNullOrEmpty(quien))
            {
                return string.IsNullOrEmpty(cuando)
                    ? $"El pedido {numero} ha cambiado desde que usted lo abrió. {RECARGUELO}"
                    : $"El pedido {numero} ha cambiado {cuando} desde que usted lo abrió. {RECARGUELO}";
            }
            if (string.Equals(quien, SinDominio(usuarioQueGuarda), StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(cuando)
                    ? $"El pedido {numero} lo ha modificado usted mismo (desde otra ventana o con otra acción) después de abrirlo. {RECARGUELO}"
                    : $"El pedido {numero} lo ha modificado usted mismo {cuando} (desde otra ventana o con otra acción) después de abrirlo. {RECARGUELO}";
            }
            return string.IsNullOrEmpty(cuando)
                ? $"El pedido {numero} lo ha modificado {quien} desde que usted lo abrió. {RECARGUELO}"
                : $"El pedido {numero} lo ha modificado {quien} {cuando} desde que usted lo abrió. {RECARGUELO}";
        }

        /// <summary>
        /// El aviso de un prepago que el guardado no trae y no se quita (sin versión): «No se ha quitado el prepago de 9.728,47 €
        /// («por TRN pedido 929555»): …».
        /// </summary>
        public static string MensajePrepagoConservado(decimal importe, string concepto)
        {
            string conConcepto = string.IsNullOrWhiteSpace(concepto) ? string.Empty : $" («{concepto.Trim()}»)";
            return $"No se ha quitado el prepago de {importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €{conConcepto}: " +
                "este guardado no sabía que existía (puede que lo metiera otra persona mientras tenía el pedido abierto). " +
                "Si de verdad hay que quitarlo, recargue el pedido y vuelva a guardarlo.";
        }

        /// <summary>«NUEVAVISION\Aida» → «Aida».</summary>
        public static string SinDominio(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string limpio = usuario.Trim();
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1).Trim() : limpio;
        }

        private static string Cuando(DateTime? fecha, DateTime hoy)
        {
            if (!fecha.HasValue)
            {
                return null;
            }
            return fecha.Value.Date == hoy.Date
                ? $"a las {fecha.Value:HH:mm}"
                : $"el {fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} a las {fecha.Value:HH:mm}";
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "-";
            }
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                _ = sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string Numero(long? valor) => valor.HasValue ? valor.Value.ToString(CultureInfo.InvariantCulture) : "-";

        private static string Numero(int? valor) => valor.HasValue ? valor.Value.ToString(CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>NestoAPI#873: lee lo que hace falta para la versión del pedido. Null si no existe.</summary>
    public interface ILectorVersionPedido
    {
        DatosVersionPedido Leer(string empresa, int numero);
    }

    /// <summary>
    /// NestoAPI#873: UNA consulta (READ COMMITTED: nada de NOLOCK, la versión no puede ser de algo sin confirmar) sobre
    /// CabPedidoVta, LinPedidoVta, Prepagos y EfectosPedidoVenta.
    /// </summary>
    public class LectorVersionPedidoSql : ILectorVersionPedido
    {
        internal const string SQL = @"
SELECT
    c.RowVersion AS RowVersionCabecera, c.Usuario AS UsuarioCabecera, c.[Fecha Modificación] AS FechaCabecera,
    l.Lineas, l.RowVersionLineas, ul.Usuario AS UsuarioLinea, ul.[Fecha Modificación] AS FechaLinea,
    p.Prepagos, p.SumaIdPrepagos, p.HuellaPrepagos, up.Usuario AS UsuarioPrepago, up.FechaModificacion AS FechaPrepago,
    e.Efectos, e.SumaIdEfectos, e.HuellaEfectos, ue.Usuario AS UsuarioEfecto, ue.FechaModificacion AS FechaEfecto
FROM dbo.CabPedidoVta c
OUTER APPLY (SELECT COUNT(*) AS Lineas, MAX(x.RowVersion) AS RowVersionLineas
             FROM dbo.LinPedidoVta x WHERE x.Empresa = c.Empresa AND x.Número = c.Número) l
OUTER APPLY (SELECT TOP 1 x.Usuario, x.[Fecha Modificación]
             FROM dbo.LinPedidoVta x WHERE x.Empresa = c.Empresa AND x.Número = c.Número
             ORDER BY x.[Fecha Modificación] DESC) ul
OUTER APPLY (SELECT COUNT(*) AS Prepagos, SUM(CAST(x.Id AS bigint)) AS SumaIdPrepagos,
                    CHECKSUM_AGG(BINARY_CHECKSUM(x.Id, x.Importe, x.Factura, x.CuentaContable, x.ConceptoAdicional)) AS HuellaPrepagos
             FROM dbo.Prepagos x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número) p
OUTER APPLY (SELECT TOP 1 x.Usuario, x.FechaModificacion
             FROM dbo.Prepagos x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número
             ORDER BY x.FechaModificacion DESC) up
OUTER APPLY (SELECT COUNT(*) AS Efectos, SUM(CAST(x.Id AS bigint)) AS SumaIdEfectos,
                    CHECKSUM_AGG(BINARY_CHECKSUM(x.Id, x.FechaVencimiento, x.Importe, x.FormaPago, x.CCC)) AS HuellaEfectos
             FROM dbo.EfectosPedidoVenta x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número) e
OUTER APPLY (SELECT TOP 1 x.Usuario, x.FechaModificacion
             FROM dbo.EfectosPedidoVenta x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número
             ORDER BY x.FechaModificacion DESC) ue
WHERE c.Empresa = @empresa AND c.Número = @numero";

        private readonly NVEntities db;

        public LectorVersionPedidoSql(NVEntities db)
        {
            this.db = db;
        }

        public DatosVersionPedido Leer(string empresa, int numero)
        {
            return db.Database.SqlQuery<DatosVersionPedido>(SQL,
                new SqlParameter("@empresa", (empresa ?? string.Empty).Trim()),
                new SqlParameter("@numero", numero)).FirstOrDefault();
        }
    }
}
