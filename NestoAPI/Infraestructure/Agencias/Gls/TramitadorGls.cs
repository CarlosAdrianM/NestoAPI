using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>Plaza de destino que asigna GLS al grabar el envío (la que Nesto pide aparte con GetPlazaXCP para la etiqueta).</summary>
    public class PlazaDestinoGls
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Nemonico { get; set; }
        public string Telefono { get; set; }
        public string Mail { get; set; }
        public string Tsp { get; set; }
    }

    /// <summary>NestoAPI#552 (fase 2): la respuesta de GrabaServicios, interpretada como lo hace AgenciaASM.LlamadaWebService.</summary>
    public class ResultadoGrabaServiciosGls
    {
        /// <summary>Resultado return="0": GLS ha grabado el envío ahora.</summary>
        public bool Exito { get; set; }
        /// <summary>
        /// GLS dice que el código de barras ya existe (error -33): el envío YA estaba grabado. No es un fallo, es el estado
        /// al que se quería llegar (AgenciaASM.RespuestaYaTramitada, casos del 28/08/2026).
        /// </summary>
        public bool YaRegistrado { get; set; }
        /// <summary>El atributo return de Resultado (null si la respuesta no llega a tenerlo).</summary>
        public string CodigoRetorno { get; set; }
        /// <summary>Texto del error (de GLS o, si no lo da, el de la tabla de AgenciaASM). Null con éxito.</summary>
        public string Error { get; set; }
        /// <summary>Número de expedición de GLS (atributo codexp).</summary>
        public string CodigoExpedicion { get; set; }
        /// <summary>Identificador del envío en GLS (atributo uid).</summary>
        public string Uid { get; set; }
        public PlazaDestinoGls Plaza { get; set; }

        /// <summary>El envío está en GLS (grabado ahora o de antes): se puede cerrar en nuestra BD.</summary>
        public bool Aceptado => Exito || YaRegistrado;
    }

    /// <summary>NestoAPI#552 (fase 2): lectura de la respuesta de GrabaServicios. Portado de AgenciaASM (Nesto).</summary>
    public static class RespuestaGrabaServiciosGls
    {
        public const string RETORNO_YA_EXISTE = "-33";

        // Solo el -33 pelado, anclado: «Error -330» es otro error distinto (AgenciaASM._codigoYaExistePelado).
        private static readonly Regex CodigoYaExistePelado = new Regex(@"^error\s*-0*33$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ResultadoGrabaServiciosGls Interpretar(string respuesta)
        {
            XElement envio = LeerNodoEnvio(respuesta, PeticionGls.OPERACION_GRABA_SERVICIOS, out string errorLectura);
            if (envio == null)
            {
                return Fallo(errorLectura);
            }

            string retorno = Hijo(envio, "Resultado")?.Attribute("return")?.Value?.Trim();
            if (retorno == null)
            {
                return Fallo("Respuesta de GLS con formato inesperado: falta Resultado/return.");
            }

            var interpretado = new ResultadoGrabaServiciosGls
            {
                CodigoRetorno = retorno,
                CodigoExpedicion = Vacio(envio.Attribute("codexp")?.Value),
                Uid = Vacio(envio.Attribute("uid")?.Value),
                Plaza = LeerPlaza(Hijo(envio, "PlazaDestino"))
            };

            if (retorno == "0")
            {
                interpretado.Exito = true;
                return interpretado;
            }

            string errorGls = Vacio(Hijo(envio, "Errores")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Error")?.Value);
            interpretado.Error = errorGls ?? MensajeError(retorno);
            interpretado.YaRegistrado = retorno == RETORNO_YA_EXISTE || EsYaRegistrado(interpretado.Error);
            return interpretado;
        }

        /// <summary>
        /// NestoAPI#552: el nodo Envio de la respuesta de una operación del WS b2b (GrabaServicios, Anula), o null con el
        /// motivo en <paramref name="error"/> si no se puede leer (vacía, XML roto, SOAP Fault, sin {operación}Result o
        /// sin Envio).
        /// </summary>
        internal static XElement LeerNodoEnvio(string respuesta, string operacion, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                error = "GLS devolvió una respuesta vacía.";
                return null;
            }

            XDocument documento;
            try
            {
                documento = XDocument.Parse(respuesta.TrimStart('﻿', ' ', '\r', '\n', '\t'));
            }
            catch (XmlException ex)
            {
                error = "Error al interpretar la respuesta XML de GLS: " + ex.Message;
                return null;
            }

            XElement resultado = documento.Descendants(PeticionGls.Ns + (operacion + "Result")).FirstOrDefault();
            if (resultado == null || !resultado.HasElements)
            {
                string fallo = documento.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value?.Trim();
                error = string.IsNullOrEmpty(fallo)
                    ? $"Respuesta de GLS con formato inesperado: falta {operacion}Result."
                    : "GLS respondió con un error SOAP: " + fallo;
                return null;
            }

            XElement envio = resultado.Descendants().FirstOrDefault(e => e.Name.LocalName == "Envio");
            if (envio == null)
            {
                error = "Respuesta de GLS con formato inesperado: falta el nodo Envio.";
            }
            return envio;
        }

        /// <summary>
        /// AgenciaASM.RespuestaYaTramitada:«Ya existe el código de barras» (con o sin tildes, como lo dé GLS o la tabla) o
        /// «Error -33» pelado. NO se perdona «Ya existe el albarán»: podría ser el -70, que es otra cosa.
        /// </summary>
        public static bool EsYaRegistrado(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return false;
            }
            string recortado = texto.Trim();
            return CultureInfo.InvariantCulture.CompareInfo.IndexOf(recortado, "ya existe el codigo de barras",
                       CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                || CodigoYaExistePelado.IsMatch(recortado);
        }

        /// <summary>La tabla de AgenciaASM.calcularMensajeError, para cuando GLS no manda el texto del error.</summary>
        public static string MensajeError(string codigo)
        {
            switch (codigo?.Trim())
            {
                case "-33": return "Ya existe el código de barras de la expedición";
                case "-69": return "No se pudo canalizar el envío";
                case "-70": return "Ya se ha enviado este pedido para esta fecha y cliente";
                case "-108": return "El nombre del remitente debe tener al menos tres caracteres";
                case "-109": return "La dirección del remitente debe tener al menos tres caracteres";
                case "-110": return "La población del remitente debe tener al menos tres caracteres";
                case "-111": return "El código postal del remitente debe tener al menos cuatro caracteres";
                case "-119": return "Error no controlado por el webservice de la agencia";
                case "-128": return "El nombre del destinatario debe tener al menos tres caracteres";
                case "-129": return "La dirección del destinatario debe tener al menos tres caracteres";
                case "-130": return "La población del destinatario debe tener al menos tres caracteres";
                case "-131": return "El código postal del destinatario debe tener al menos cuatro caracteres";
                default: return $"El código de error {codigo} de GLS no está controlado";
            }
        }

        private static PlazaDestinoGls LeerPlaza(XElement plaza)
        {
            if (plaza == null || !plaza.HasElements)
            {
                return null;
            }
            return new PlazaDestinoGls
            {
                Codigo = Vacio(Hijo(plaza, "Codigo")?.Value),
                Nombre = Vacio(Hijo(plaza, "Nombre")?.Value),
                Nemonico = Vacio(Hijo(plaza, "Nemonico")?.Value),
                Telefono = Vacio(Hijo(plaza, "Telefono")?.Value),
                Mail = Vacio(Hijo(plaza, "Mail")?.Value),
                Tsp = Vacio(Hijo(plaza, "TSP")?.Value)
            };
        }

        private static ResultadoGrabaServiciosGls Fallo(string error) => new ResultadoGrabaServiciosGls { Error = error };

        internal static XElement Hijo(XElement padre, string nombre) => padre?.Elements().FirstOrDefault(e => e.Name.LocalName == nombre);

        internal static string Vacio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    /// <summary>Una llamada a GrabaServicios: lo que se mandó, lo que contestó GLS y cómo se ha entendido.</summary>
    public class TramitacionGls
    {
        public string Url { get; set; }
        public string Peticion { get; set; }
        /// <summary>La respuesta cruda ("" si no llegó).</summary>
        public string Respuesta { get; set; }
        /// <summary>Null si no llegó respuesta (ver <see cref="Fallo"/>).</summary>
        public ResultadoGrabaServiciosGls Resultado { get; set; }
        /// <summary>Fallo de comunicación (sin conexión, timeout, HTTP de error sin XML). Null si GLS contestó.</summary>
        public AgenciaRemotaException Fallo { get; set; }
    }

    /// <summary>
    /// NestoAPI#552 (fase 2): graba un envío en GLS desde la API, con la misma petición que AgenciaASM (<see cref="PeticionGls"/>)
    /// salvo la fecha, que es la del día de tramitación (decisión de Carlos, 16/10/26; Nesto manda la de creación). Solo habla
    /// con GLS: ni toca la BD ni cierra el envío (eso es ConfirmarTramitacion, el segundo paso, igual que hoy).
    /// </summary>
    public class TramitadorGls
    {
        /// <summary>Prefijo del código de barras de BusinessParcel (servicio 96), que tiene su propia cuenta (AgenciaASM).</summary>
        public const string PREFIJO_CODIGO_BARRAS_BUSINESSPARCEL = "6119714";

        private readonly IClienteGrabaServiciosGls _cliente;

        public TramitadorGls(IClienteGrabaServiciosGls cliente)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        }

        public async Task<TramitacionGls> TramitarAsync(EnviosAgencia envio, Empresa empresa, string identificadorAgencia, DateTime fechaTramitacion)
        {
            string soap = PeticionGls.ConstruirSoap(PeticionGls.ConstruirServicios(envio, empresa, identificadorAgencia, fechaTramitacion.Date));
            var tramitacion = new TramitacionGls { Url = _cliente.Url, Peticion = soap, Respuesta = string.Empty };
            try
            {
                tramitacion.Respuesta = await _cliente.EnviarAsync(soap).ConfigureAwait(false) ?? string.Empty;
            }
            catch (AgenciaRemotaException ex)
            {
                tramitacion.Fallo = ex;
                return tramitacion;
            }
            tramitacion.Resultado = RespuestaGrabaServiciosGls.Interpretar(tramitacion.Respuesta);
            return tramitacion;
        }

        /// <summary>
        /// El código de barras de un envío de GLS, como AgenciaASM.calcularCodigoBarras: prefijo (el de BusinessParcel con el
        /// servicio 96; si no, AgenciasTransporte.PrefijoCodigoBarras) + número del envío con 7 cifras. Null si no hay prefijo.
        /// </summary>
        public static string CalcularCodigoBarras(EnviosAgencia envio, string prefijoAgencia)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            string prefijo = envio.Servicio == PeticionGls.SERVICIO_BUSINESSPARCEL ? PREFIJO_CODIGO_BARRAS_BUSINESSPARCEL : prefijoAgencia?.Trim();
            return string.IsNullOrEmpty(prefijo) ? null : prefijo + envio.Numero.ToString("D7", CultureInfo.InvariantCulture);
        }
    }
}
