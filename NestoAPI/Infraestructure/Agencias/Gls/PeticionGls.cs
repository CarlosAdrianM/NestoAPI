using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>
    /// NestoAPI#552 (fase 1, sombra): la petición GrabaServicios de GLS (ASM), construida en la API exactamente como la
    /// construye hoy Nesto en AgenciaASM.construirXMLdeSalida. En esta fase NO se envía: solo se compara con las
    /// peticiones reales que Nesto deja en AgenciasLlamadasWeb (<see cref="SombraPeticionesGls"/>). Fase 2: es la que
    /// manda <see cref="TramitadorGls"/> cuando la API tramita GLS (interruptor Agencias:GlsTramitarPorApi).
    /// </summary>
    public static class PeticionGls
    {
        public static readonly XNamespace Ns = "http://www.asmred.com/";
        public const string OPERACION_GRABA_SERVICIOS = "GrabaServicios";
        public const string URL_GRABA_SERVICIOS = "https://www.asmred.com/WebSrvs/b2b.asmx?op=" + OPERACION_GRABA_SERVICIOS;

        /// <summary>
        /// NestoAPI#552 (fase 3): la operación Anula del mismo WS b2b. OJO: Nesto nunca la ha usado (en Nesto un envío de
        /// GLS ya tramitado no se anula desde el programa) y no hay ninguna llamada suya guardada en AgenciasLlamadasWeb;
        /// el contrato sale de la documentación del WS b2b de GLS y hay que confirmarlo con una llamada de prueba antes de
        /// encender Agencias:GlsAnularPorApi.
        /// </summary>
        public const string OPERACION_ANULA = "Anula";
        public const string URL_ANULA = "https://www.asmred.com/WebSrvs/b2b.asmx?op=" + OPERACION_ANULA;

        /// <summary>BusinessParcel (servicio 96) va con su propia cuenta, no con la de la agencia (AgenciaASM).</summary>
        public const short SERVICIO_BUSINESSPARCEL = 96;
        public const string IDENTIFICADOR_BUSINESSPARCEL = "6fb665f2-15a2-4478-9804-c1556fc1f272";

        /// <summary>Empresa espejo: sus envíos salen con Nueva Visión de remitente (datos fijos en AgenciaASM).</summary>
        private const string EMPRESA_ESPEJO = "3";

        public static string Identificador(EnviosAgencia envio, string identificadorAgencia)
            => envio.Servicio == SERVICIO_BUSINESSPARCEL ? IDENTIFICADOR_BUSINESSPARCEL : identificadorAgencia;

        /// <summary>El nodo Servicios (lo que va dentro de docIn).</summary>
        /// <param name="identificadorAgencia">AgenciasTransporte.Identificador de la agencia del envío.</param>
        /// <param name="fecha">
        /// La fecha que se manda a GLS. Sin ella, la del envío (EnviosAgencia.Fecha), que es lo que manda Nesto y con lo
        /// que compara la sombra. Fase 2 (Carlos, 16/10/26): cuando tramita la API, se manda la del DÍA DE TRAMITACIÓN.
        /// </param>
        public static XElement ConstruirServicios(EnviosAgencia envio, Empresa empresa, string identificadorAgencia, DateTime? fecha = null)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            if (empresa == null) throw new ArgumentNullException(nameof(empresa));

            bool espejo = empresa.Número?.Trim() == EMPRESA_ESPEJO;

            return new XElement(Ns + "Servicios",
                new XAttribute("uidcliente", Identificador(envio, identificadorAgencia) ?? string.Empty),
                new XElement(Ns + "Envio",
                    new XAttribute("codbarras", envio.CodigoBarras ?? string.Empty),
                    // Nesto usa ToShortDateString, que en los puestos es dd/MM/yy (20.550 de 20.621 peticiones guardadas).
                    E("Fecha", (fecha ?? envio.Fecha).ToString("dd/MM/yy", CultureInfo.InvariantCulture)),
                    E("Portes", "P"),
                    E("Servicio", envio.Servicio),
                    E("Horario", envio.Horario),
                    E("Bultos", envio.Bultos),
                    E("Peso", 1),
                    E("Retorno", envio.Retorno),
                    E("Pod", "N"),
                    new XElement(Ns + "Remite",
                        E("Plaza", null),
                        E("Nombre", espejo ? "Nueva Visión" : empresa.Nombre?.Trim()),
                        E("Direccion", espejo ? "c/ Río Tiétar, 11" : empresa.Dirección?.Trim()),
                        E("Poblacion", espejo ? "Algete" : empresa.Población?.Trim()),
                        E("Provincia", espejo ? "Madrid" : empresa.Provincia?.Trim()),
                        E("Pais", 34),
                        E("CP", espejo ? "28119" : empresa.CodPostal?.Trim()),
                        E("Telefono", espejo ? "916281914" : empresa.Teléfono?.Trim()),
                        E("Movil", null),
                        E("Email", espejo ? "logistica@nuevavision.es" : empresa.Email?.Trim()),
                        E("Observaciones", null)),
                    new XElement(Ns + "Destinatario",
                        E("Codigo", null),
                        E("Plaza", null),
                        E("Nombre", envio.Nombre?.Normalize()),
                        E("Direccion", envio.Direccion),
                        E("Poblacion", envio.Poblacion),
                        E("Provincia", envio.Provincia),
                        E("Pais", envio.Pais),
                        E("CP", envio.CodPostal),
                        E("Telefono", envio.Telefono),
                        E("Movil", envio.Movil),
                        E("Email", envio.Email),
                        E("Observaciones", envio.Observaciones),
                        E("ATT", envio.Atencion)),
                    new XElement(Ns + "Referencias",
                        new XElement(Ns + "Referencia", new XAttribute("tipo", "C"), $"{envio.Cliente?.Trim()}/{envio.Pedido}")),
                    new XElement(Ns + "Importes",
                        E("Debidos", 0),
                        E("Reembolso", envio.Reembolso.ToString(CultureInfo.InvariantCulture))),
                    new XElement(Ns + "Seguro", new XAttribute("tipo", string.Empty),
                        E("Descripcion", null),
                        E("Importe", null)),
                    new XElement(Ns + "DevuelveAdicionales",
                        new XElement(Ns + "PlazaDestino"))));
        }

        /// <summary>
        /// NestoAPI#552 (fase 3): el nodo Servicios de una anulación: la misma cuenta (uidcliente) con la que se grabó el
        /// envío y su código de barras, que es lo que lo identifica en GLS.
        /// </summary>
        /// <param name="codigoBarras">EnviosAgencia.CodigoBarras (se manda sin espacios).</param>
        /// <param name="uidCliente">El de <see cref="Identificador"/> para ese envío: el mismo que en GrabaServicios.</param>
        public static XElement ConstruirAnulacion(string codigoBarras, string uidCliente)
        {
            if (string.IsNullOrWhiteSpace(codigoBarras)) throw new ArgumentNullException(nameof(codigoBarras));
            return new XElement(Ns + "Servicios",
                new XAttribute("uidcliente", uidCliente ?? string.Empty),
                new XElement(Ns + "Envio",
                    new XAttribute("codbarras", codigoBarras.Trim())));
        }

        /// <summary>
        /// El sobre SOAP completo, con el mismo texto que monta AgenciaASM.LlamadaWebService. La operación es GrabaServicios
        /// salvo que se diga otra (fase 3: Anula, con el mismo sobre).
        /// </summary>
        public static string ConstruirSoap(XElement servicios, string operacion = OPERACION_GRABA_SERVICIOS)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body>" +
                "<" + operacion + " xmlns=\"http://www.asmred.com/\">" +
                "<docIn>" + servicios + "</docIn>" +
                "</" + operacion + ">" +
                "</soap:Body>" +
                "</soap:Envelope>";
        }

        /// <summary>
        /// El nodo Servicios de una petición guardada en AgenciasLlamadasWeb.CuerpoLlamada, o null si ese cuerpo no es
        /// una petición GrabaServicios (las consultas de seguimiento también se guardan con Agencia = ASM).
        /// </summary>
        public static XElement ServiciosDeLaPeticion(string cuerpo)
        {
            if (string.IsNullOrWhiteSpace(cuerpo))
            {
                return null;
            }
            string texto = cuerpo.TrimStart('﻿', ' ', '\r', '\n', '\t');
            if (!texto.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }
            try
            {
                // PreserveWhitespace: un campo que solo lleva saltos de línea (unas Observaciones «\r\n\r\n») es lo que
                // se mandó; sin él, Parse lo deja vacío.
                XDocument documento = XDocument.Parse(texto, LoadOptions.PreserveWhitespace);
                // Fase 3: una anulación también lleva un nodo Servicios, pero no es una petición de grabar.
                if (documento.Descendants(Ns + OPERACION_ANULA).Any())
                {
                    return null;
                }
                return documento.Descendants(Ns + "Servicios").FirstOrDefault();
            }
            catch (XmlException)
            {
                return null;
            }
        }

        private static XElement E(string nombre, object valor) => new XElement(Ns + nombre, valor ?? string.Empty);
    }
}
