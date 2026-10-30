using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.ChequesRegalo;
using CodigoPostal = NestoAPI.Infraestructure.Direcciones.CodigoPostal;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.EnlacesDescarga
{
    /// <summary>NestoAPI#737: los documentos que se pueden mandar con un enlace de descarga.</summary>
    public static class TiposDocumentoDescarga
    {
        public const string PEDIDO = "Pedido";
        public const string FACTURA = "Factura";

        /// <summary>«pedido», «PEDIDO»… → <see cref="PEDIDO"/>; lo que no sea ni pedido ni factura, null.</summary>
        public static string Normalizar(string tipo)
        {
            string t = tipo?.Trim();
            if (string.Equals(t, PEDIDO, StringComparison.OrdinalIgnoreCase))
            {
                return PEDIDO;
            }
            return string.Equals(t, FACTURA, StringComparison.OrdinalIgnoreCase) ? FACTURA : null;
        }
    }

    /// <summary>NestoAPI#737: una fila de dbo.EnlacesDescargaDocumentos (Scripts/Issue737_EnlacesDescargaDocumentos.sql).</summary>
    public class EnlaceDescargaDocumento
    {
        public string Token { get; set; }
        public string Tipo { get; set; }
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public DateTime Creado { get; set; }
        public DateTime Caduca { get; set; }
        public string Usuario { get; set; }
        public int Descargas { get; set; }
        public DateTime? UltimaDescarga { get; set; }
    }

    /// <summary>NestoAPI#737: lo que se guarda y se lee de los enlaces (detrás de una interfaz para probarlo sin BD).</summary>
    public interface IRepositorioEnlacesDescarga
    {
        Task Guardar(EnlaceDescargaDocumento enlace);
        /// <summary>El enlace de ese token, caducado o no. Null si no existe.</summary>
        Task<EnlaceDescargaDocumento> Leer(string token);
        /// <summary>Una descarga más (y cuándo fue la última).</summary>
        Task RegistrarDescarga(string token, DateTime cuando);
    }

    /// <summary>NestoAPI#737: lo que hace falta del documento para el mensaje de WhatsApp.</summary>
    public class DatosDocumentoEnlace
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Clientes.Pais (ISO-2; en blanco = España).</summary>
        public string Pais { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Los teléfonos tal cual están en la ficha y en las personas de contacto (varios por campo con «/»).</summary>
        public List<string> Telefonos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#737: lo que el enlace necesita de la BD y de QuestPDF (aparte para que los tests no toquen ni una cosa ni la otra).</summary>
    public interface IProveedorDocumentosEnlace
    {
        /// <summary>
        /// Los datos del pedido o de la factura. Null si no existe. Lanza <see cref="NestoBusinessException"/> (403) si es una
        /// factura de una serie que no se puede descargar (como GET api/Facturas).
        /// </summary>
        Task<DatosDocumentoEnlace> LeerDatos(string tipo, string empresa, string numero);
        /// <summary>El PDF del documento con QuestPDF (el mismo de la tienda online y del envío por correo).</summary>
        Task<byte[]> Pdf(string tipo, string empresa, string numero);
    }

    /// <summary>NestoAPI#737: lo que contesta <c>GET /d/{token}</c>: el PDF o, si no se puede, la página de usted.</summary>
    public class DescargaDocumento
    {
        public HttpStatusCode Estado { get; set; }
        public byte[] Pdf { get; set; }
        public string NombreFichero { get; set; }
        public string Html { get; set; }
        public bool EsPdf => Pdf != null;
    }

    /// <summary>
    /// NestoAPI#737: el token de los enlaces de descarga: 32 bytes aleatorios (256 bits, RNGCryptoServiceProvider) en Base64Url,
    /// 43 caracteres. A diferencia del de seguimiento (#744, firmado y sin tabla), este no lleva nada dentro: lo que abre está en la
    /// tabla, que también guarda la caducidad, quién lo creó y cuántas veces se ha descargado. No es adivinable ni enumerable.
    /// </summary>
    public static class TokenEnlaceDescarga
    {
        public const int BYTES = 32;
        public const int LONGITUD = 43;

        public static string Nuevo()
        {
            byte[] datos = new byte[BYTES];
            using (var aleatorio = new RNGCryptoServiceProvider())
            {
                aleatorio.GetBytes(datos);
            }
            return Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>¿Tiene la forma de un token? Lo que no la tiene ni se busca en la BD.</summary>
        public static bool FormatoValido(string token)
            => token != null && token.Length == LONGITUD
               && token.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_');
    }

    /// <summary>
    /// NestoAPI#737: los móviles de un cliente en formato internacional para WhatsApp (solo cifras, sin «+»): 34 + 9 cifras si es
    /// de España (empiezan por 6 o 7), 351 + 9 si es de Portugal (empiezan por 9). Lo que ya viene con «+» o «00» se respeta. Los
    /// fijos y lo que no se sabe formatear (un número de otro país sin prefijo) se descartan. Sin repetir y en el orden en que vienen.
    /// </summary>
    public static class MovilesWhatsApp
    {
        private static readonly char[] Separadores = { '/', ';', ',', '|' };

        public static List<string> Internacionales(IEnumerable<string> telefonos, string pais)
        {
            var moviles = new List<string>();
            foreach (string campo in telefonos ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(campo))
                {
                    continue;
                }
                foreach (string telefono in campo.Split(Separadores, StringSplitOptions.RemoveEmptyEntries))
                {
                    string movil = Internacional(telefono, pais);
                    if (movil != null && !moviles.Contains(movil))
                    {
                        moviles.Add(movil);
                    }
                }
            }
            return moviles;
        }

        /// <summary>El móvil en formato internacional, o null si es un fijo o no se sabe de qué país es.</summary>
        public static string Internacional(string telefono, string pais)
        {
            string texto = telefono?.Trim() ?? string.Empty;
            string cifras = CodigoPostal.Digitos(texto);
            bool conPrefijo = texto.StartsWith("+");
            if (!conPrefijo && cifras.StartsWith("00"))
            {
                conPrefijo = true;
                cifras = cifras.Substring(2);
            }

            if (conPrefijo)
            {
                if (cifras.StartsWith("34"))
                {
                    return EsMovilEspanol(cifras.Substring(2)) ? cifras : null;
                }
                if (cifras.StartsWith("351"))
                {
                    return EsMovilPortugues(cifras.Substring(3)) ? cifras : null;
                }
                // Otro país: no sabemos distinguir fijo de móvil; si tiene forma de número internacional, vale
                return cifras.Length >= 10 && cifras.Length <= 15 ? cifras : null;
            }

            string iso = CodigoPostal.PaisIso(pais);
            if (iso == string.Empty || iso == CodigoPostal.ESPANA)
            {
                if (EsMovilEspanol(cifras))
                {
                    return "34" + cifras;
                }
                // Ya con el 34 delante pero sin «+» (34600123456)
                return cifras.Length == 11 && cifras.StartsWith("34") && EsMovilEspanol(cifras.Substring(2)) ? cifras : null;
            }
            if (iso == CodigoPostal.PORTUGAL)
            {
                if (EsMovilPortugues(cifras))
                {
                    return "351" + cifras;
                }
                return cifras.Length == 12 && cifras.StartsWith("351") && EsMovilPortugues(cifras.Substring(3)) ? cifras : null;
            }
            return null;
        }

        private static bool EsMovilEspanol(string cifras) => cifras.Length == 9 && (cifras[0] == '6' || cifras[0] == '7');
        private static bool EsMovilPortugues(string cifras) => cifras.Length == 9 && cifras[0] == '9';
    }

    /// <summary>
    /// NestoAPI#737 (sugerencias 866/867): el enlace de descarga del pedido o de la factura para mandarlo por WhatsApp Web
    /// (Nesto#570). <see cref="Crear"/> lo da de alta (solo empleados: lo controla el controlador) con caducidad de
    /// <see cref="DIAS_CADUCIDAD"/> días y devuelve, además de la URL, el texto de usted y los móviles del cliente;
    /// <see cref="Descargar"/> es lo que hay detrás de <c>GET /d/{token}</c>, sin usuario: el PDF, o la página amable si el
    /// enlace no existe o ha caducado.
    /// </summary>
    public class GestorEnlacesDescarga
    {
        public const int DIAS_CADUCIDAD = 30;
        public const string RUTA = "d/";
        internal const string MOTIVO_TIPO = "El tipo de documento tiene que ser «Pedido» o «Factura».";
        internal const string MOTIVO_NUMERO = "Falta el número del documento.";
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        private readonly IRepositorioEnlacesDescarga repositorio;
        private readonly IProveedorDocumentosEnlace proveedor;
        private readonly Func<DateTime> ahora;
        private readonly Func<string> urlBase;
        private readonly Func<string> nuevoToken;

        public GestorEnlacesDescarga(IRepositorioEnlacesDescarga repositorio, IProveedorDocumentosEnlace proveedor,
            Func<DateTime> ahora = null, Func<string> urlBase = null, Func<string> nuevoToken = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.proveedor = proveedor ?? throw new ArgumentNullException(nameof(proveedor));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.urlBase = urlBase ?? (() => MiniaturasChequeRegalo.UrlPublicaApi);
            this.nuevoToken = nuevoToken ?? TokenEnlaceDescarga.Nuevo;
        }

        public async Task<EnlaceDescargaDTO> Crear(PeticionEnlaceDescargaDTO peticion, string usuario)
        {
            string tipo = TiposDocumentoDescarga.Normalizar(peticion?.Tipo);
            if (tipo == null)
            {
                throw new NestoBusinessException(MOTIVO_TIPO) { StatusCode = HttpStatusCode.BadRequest };
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string numero = peticion.Numero?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(numero) || numero.Length > 20)
            {
                throw new NestoBusinessException(MOTIVO_NUMERO) { StatusCode = HttpStatusCode.BadRequest };
            }
            if (tipo == TiposDocumentoDescarga.PEDIDO && (!int.TryParse(numero, NumberStyles.None, CultureInfo.InvariantCulture, out int pedido) || pedido <= 0))
            {
                throw new NestoBusinessException($"«{numero}» no es un número de pedido.") { StatusCode = HttpStatusCode.BadRequest };
            }

            DatosDocumentoEnlace datos = await proveedor.LeerDatos(tipo, empresa, numero).ConfigureAwait(false);
            if (datos == null)
            {
                throw new NestoBusinessException($"No existe {(tipo == TiposDocumentoDescarga.PEDIDO ? "el pedido" : "la factura")} {numero}.")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }

            DateTime creado = ahora();
            var enlace = new EnlaceDescargaDocumento
            {
                Token = nuevoToken(),
                Tipo = tipo,
                Empresa = empresa,
                Numero = numero,
                Creado = creado,
                Caduca = creado.AddDays(DIAS_CADUCIDAD),
                Usuario = string.IsNullOrWhiteSpace(usuario) ? null : usuario.Trim()
            };
            await repositorio.Guardar(enlace).ConfigureAwait(false);

            string url = Url(enlace.Token);
            return new EnlaceDescargaDTO
            {
                Url = url,
                Caduca = enlace.Caduca,
                Tipo = tipo,
                Numero = numero,
                Cliente = datos.Cliente?.Trim(),
                Contacto = datos.Contacto?.Trim(),
                Nombre = datos.Nombre?.Trim(),
                Importe = Math.Round(datos.Importe, 2, MidpointRounding.AwayFromZero),
                Moviles = MovilesWhatsApp.Internacionales(datos.Telefonos, datos.Pais),
                Texto = TextoWhatsApp(tipo, numero, datos.Nombre, datos.Importe, url, enlace.Caduca, creado)
            };
        }

        public string Url(string token) => (urlBase() ?? string.Empty).Trim().TrimEnd('/') + "/" + RUTA + token;

        /// <summary>
        /// «Buenos días, Peluquería Ana: le enviamos su pedido 929498, de 1.234,56 €. Puede descargarlo en PDF desde este enlace:
        /// https://…/d/… (disponible hasta el 29/11/2026). Un saludo, Nueva Visión.» De usted y con tildes.
        /// </summary>
        public static string TextoWhatsApp(string tipo, string numero, string nombre, decimal importe, string url, DateTime caduca, DateTime ahora)
        {
            bool esPedido = tipo == TiposDocumentoDescarga.PEDIDO;
            string saludo = Saludo(ahora);
            string quien = string.IsNullOrWhiteSpace(nombre) ? string.Empty : ", " + nombre.Trim();
            string documento = esPedido ? "su pedido" : "su factura";
            string cuanto = importe == 0 ? string.Empty : ", de " + Math.Round(importe, 2, MidpointRounding.AwayFromZero).ToString("N2", Espanol) + " €";
            return $"{saludo}{quien}:\n\n" +
                   $"Le enviamos {documento} {numero?.Trim()}{cuanto}. Puede descargar{(esPedido ? "lo" : "la")} en PDF desde este enlace:\n" +
                   $"{url}\n\n" +
                   $"El enlace estará disponible hasta el {caduca.ToString("dd/MM/yyyy", Espanol)}.\n\n" +
                   $"Puede descargar nuestra app para Android, con sus pedidos y facturas:\n{EnlaceApp(esPedido)}\n\n" +
                   "Un saludo,\nNueva Visión";
        }

        /// <summary>La ficha de TNV en Google Play con el referrer de la campaña (Play Console y Firebase la atribuyen).</summary>
        internal static string EnlaceApp(bool esPedido)
            => "https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas&referrer=utm_source%3Dwhatsapp%26utm_medium%3Dmensaje%26utm_campaign%3D"
               + (esPedido ? "envio_pedido" : "envio_factura");

        internal static string Saludo(DateTime ahora)
            => ahora.Hour < 14 ? "Buenos días" : ahora.Hour < 21 ? "Buenas tardes" : "Buenas noches";

        public async Task<DescargaDocumento> Descargar(string token)
        {
            string limpio = token?.Trim();
            if (!TokenEnlaceDescarga.FormatoValido(limpio))
            {
                return PaginaEnlacesDescarga.NoEncontrado();
            }
            EnlaceDescargaDocumento enlace = await repositorio.Leer(limpio).ConfigureAwait(false);
            if (enlace == null)
            {
                return PaginaEnlacesDescarga.NoEncontrado();
            }
            DateTime momento = ahora();
            if (enlace.Caduca <= momento)
            {
                return PaginaEnlacesDescarga.Caducado(enlace);
            }

            byte[] pdf;
            try
            {
                pdf = await proveedor.Pdf(enlace.Tipo, enlace.Empresa, enlace.Numero).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                // El documento ya no existe (o ya no se puede descargar): para el cliente, lo mismo que un enlace malo
                return PaginaEnlacesDescarga.NoEncontrado();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[NestoAPI#737] No se ha podido generar el PDF del enlace de descarga de {enlace.Tipo} {enlace.Numero} (empresa {enlace.Empresa}): {ex.Message}", ex));
                return PaginaEnlacesDescarga.Error();
            }
            if (pdf == null || pdf.Length == 0)
            {
                // El documento ya no existe (p. ej. el pedido se ha borrado)
                return PaginaEnlacesDescarga.NoEncontrado();
            }

            try
            {
                await repositorio.RegistrarDescarga(enlace.Token, momento).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Que no contarla no impida al cliente descargar su documento
                ElmahHelper.Log(new Exception($"[NestoAPI#737] No se ha podido contar la descarga de {enlace.Tipo} {enlace.Numero}: {ex.Message}", ex));
            }
            return new DescargaDocumento
            {
                Estado = HttpStatusCode.OK,
                Pdf = pdf,
                NombreFichero = NombreFichero(enlace.Tipo, enlace.Numero)
            };
        }

        internal static string NombreFichero(string tipo, string numero)
            => $"{(tipo == TiposDocumentoDescarga.FACTURA ? "Factura" : "Pedido")}_{new string((numero ?? string.Empty).Trim().Where(char.IsLetterOrDigit).ToArray())}.pdf";
    }

    /// <summary>NestoAPI#737: las páginas de cuando no se puede descargar (de usted, con la marca de la del seguimiento #744).</summary>
    public static class PaginaEnlacesDescarga
    {
        public const string CORREO = Constantes.Correos.CORREO_ADMON;

        public static DescargaDocumento NoEncontrado()
            => Pagina(HttpStatusCode.NotFound, "Documento no encontrado", "No encontramos este documento",
                "El enlace no es correcto o el documento ya no está disponible. Compruebe que lo ha copiado entero o póngase en " +
                "contacto con nosotros y se lo enviaremos de nuevo.");

        public static DescargaDocumento Caducado(EnlaceDescargaDocumento enlace)
        {
            string que = enlace?.Tipo == TiposDocumentoDescarga.FACTURA ? "su factura " : "su pedido ";
            return Pagina((HttpStatusCode)410, "Enlace caducado", "Este enlace ha caducado",
                $"Los enlaces de descarga están disponibles {GestorEnlacesDescarga.DIAS_CADUCIDAD} días. Póngase en contacto con nosotros " +
                $"y le enviaremos de nuevo {que}{enlace?.Numero?.Trim()}.");
        }

        public static DescargaDocumento Error()
            => Pagina(HttpStatusCode.InternalServerError, "No disponible", "No hemos podido preparar el documento",
                "Ha surgido un problema al preparar el PDF. Inténtelo de nuevo dentro de unos minutos o póngase en contacto con nosotros " +
                "y se lo enviaremos.");

        private static DescargaDocumento Pagina(HttpStatusCode estado, string titulo, string encabezado, string texto)
        {
            string H(string t) => PaginaHtmlSeguimientoReparto.H(t);
            string cuerpo =
                "<div class=\"estado estado-pendiente\"><h1>" + H(encabezado) + "</h1><p>" + H(texto) + "</p></div>" +
                "<div class=\"contacto destacado\"><p>¿Necesita ayuda? Estamos a su disposición:</p>" +
                "<p><a href=\"tel:" + PaginaHtmlSeguimientoReparto.TELEFONO_ENLACE + "\">" + PaginaHtmlSeguimientoReparto.TELEFONO + "</a> · " +
                "<a href=\"" + PaginaHtmlSeguimientoReparto.WHATSAPP + "\">WhatsApp</a> · " +
                "<a href=\"mailto:" + CORREO + "\">" + CORREO + "</a></p></div>";
            return new DescargaDocumento { Estado = estado, Html = PaginaHtmlSeguimientoReparto.Documento(titulo, cuerpo) };
        }
    }
}
