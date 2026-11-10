using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.PruebasEntrega;
using NestoAPI.Models;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): el justificante de entrega de CTT (API oficial de POD), pedido en el momento.
    ///   GET cls/pods/{código de envío}?client_center_code={centro}&amp;hash={MD5 hex minúsculas de código + centro + CP destino, UTF-8}
    ///   -> 200 application/pdf («Justificante de entrega»); 400/401/404 en error. Probado el 10/11/26 con un envío real.
    /// Quién recibe, su DNI y la hora salen del seguimiento (evento 2100/2110: detail.signee_name, signee_id), que es opcional:
    /// si falla, se da el PDF igual.
    /// </summary>
    public class PruebaEntregaCTT : IProveedorPruebaEntrega
    {
        internal const string RUTA_POD = "cls/pods/";
        internal const string MENSAJE_SIN_CODIGO = "El envío no tiene código de CTT: no se puede pedir la prueba de entrega.";
        internal const string MENSAJE_AUN_NO = "CTT todavía no tiene el justificante de entrega de este envío. Inténtelo más tarde.";
        internal const string MENSAJE_CUPO = "CTT ha cortado por exceso de consultas. Inténtelo de nuevo en unos minutos.";
        private static readonly string[] CODIGOS_ENTREGADO = { "2100", "2110" };

        private readonly IDescargaBinariaCTT descarga;
        private readonly IClienteRestCTT cliente;
        private readonly ConfiguracionCTT config;

        public PruebaEntregaCTT(IDescargaBinariaCTT descarga, IClienteRestCTT cliente, ConfiguracionCTT config)
        {
            this.descarga = descarga ?? throw new ArgumentNullException(nameof(descarga));
            this.cliente = cliente;
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>La de producción: el mismo cliente y el mismo token que el resto de CTT.</summary>
        public static PruebaEntregaCTT DeProduccion()
        {
            var configuracion = new ConfiguracionCTT();
            var clienteRest = new ClienteRestCTT(configuracion);
            return new PruebaEntregaCTT(clienteRest, clienteRest, configuracion);
        }

        public async Task<PruebaEntregaAgencia> ObtenerAsync(EnviosAgencia envio)
        {
            string codigo = envio.CodigoBarras?.Trim();
            if (string.IsNullOrEmpty(codigo))
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_SIN_CODIGO);
            }
            if (string.IsNullOrWhiteSpace(config.ClientCenterCode))
            {
                throw new AgenciaRemotaException("CTT: falta el código de centro de cliente (CTT:ClientCenterCode en Web.config).");
            }
            string centro = config.ClientCenterCode.Trim();
            string hash = Hash(codigo, centro, CodigoPostalDestino(envio, config));
            RespuestaBinariaCTT pod = await descarga.DescargarAsync(
                $"{RUTA_POD}{Uri.EscapeDataString(codigo)}?client_center_code={Uri.EscapeDataString(centro)}&hash={hash}", "Prueba de entrega").ConfigureAwait(false);
            if (pod.Codigo == 404)
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_AUN_NO);
            }
            if (pod.Codigo == 429)
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_CUPO);
            }
            if (!pod.Exito)
            {
                throw new AgenciaRemotaException($"CTT no ha devuelto el justificante de entrega del envío {codigo}. {pod.Error}");
            }
            if (pod.Contenido == null || pod.Contenido.Length == 0 || !DescargadorDocumentosPruebaEntrega.EsDocumentoAdmitido(pod.TipoContenido ?? "application/pdf"))
            {
                throw new AgenciaRemotaException($"CTT ha respondido al justificante de entrega del envío {codigo} con algo que no es un PDF ({pod.TipoContenido}).");
            }

            var prueba = new PruebaEntregaAgencia
            {
                Disponible = true,
                Documento = pod.Contenido,
                TipoDocumento = pod.TipoContenido ?? "application/pdf",
                FechaHora = envio.FechaEntrega
            };
            await CompletarConSeguimiento(prueba, codigo).ConfigureAwait(false);
            return prueba;
        }

        /// <summary>Quién recibió, su DNI y la hora, del evento de entrega del seguimiento. Opcional: nunca rompe la prueba.</summary>
        private async Task CompletarConSeguimiento(PruebaEntregaAgencia prueba, string codigo)
        {
            if (cliente == null)
            {
                return;
            }
            try
            {
                RespuestaCTT respuesta = await cliente.EnviarAsync(HttpMethod.Get,
                    AgenciaRemotaCTT.RUTA_SEGUIMIENTO + codigo + "?view=APITRACK&showItems=false", null, "Seguimiento (prueba de entrega)").ConfigureAwait(false);
                if (respuesta == null || !respuesta.Exito)
                {
                    return;
                }
                DatosEntregaCTT datos = LeerEntrega(respuesta.Json?["data"]?["shipping_history"]?["events"] as JArray);
                if (datos == null)
                {
                    return;
                }
                prueba.FechaHora = datos.FechaHora ?? prueba.FechaHora;
                prueba.Receptor = datos.Receptor;
                prueba.DniReceptor = datos.Dni;
            }
            catch (Exception)
            {
                // El PDF ya lleva quién firmó: los datos sueltos son un extra.
            }
        }

        internal class DatosEntregaCTT
        {
            public DateTime? FechaHora { get; set; }
            public string Receptor { get; set; }
            public string Dni { get; set; }
        }

        /// <summary>El último evento de entrega (2100 o 2110): su fecha, detail.signee_name y detail.signee_id. Null si no hay.</summary>
        internal static DatosEntregaCTT LeerEntrega(JArray eventos)
        {
            JToken entrega = eventos?
                .Where(e => CODIGOS_ENTREGADO.Contains((e["code"]?.ToString() ?? string.Empty).Trim()))
                .OrderBy(e => e["event_date"]?.ToString())
                .LastOrDefault();
            if (entrega == null)
            {
                return null;
            }
            return new DatosEntregaCTT
            {
                FechaHora = AgenciaRemotaCTT.LeerFecha(entrega["event_date"]?.ToString()),
                Receptor = Limpio(entrega["detail"]?["signee_name"]?.ToString()),
                Dni = Limpio(entrega["detail"]?["signee_id"]?.ToString())
            };
        }

        /// <summary>
        /// El CP de destino tal como lo tiene CTT (el del manifiesto): el del envío, o el de nuestro almacén en una recogida en
        /// origen (ahí el destinatario somos nosotros).
        /// </summary>
        internal static string CodigoPostalDestino(EnviosAgencia envio, ConfiguracionCTT config)
        {
            if (envio.Retorno == AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN)
            {
                return Direcciones.CodigoPostal.ParaCTT(config.Remitente.CodigoPostal, config.Remitente.Pais ?? "ES");
            }
            string cp = (envio.CodPostal ?? string.Empty).Trim();
            string pais = MapeadorTipoServicioCTT.PaisDesdeCodigoPostal(cp, Direcciones.CodigoPostal.PaisIso(envio.Pais));
            return Direcciones.CodigoPostal.ParaCTT(cp, pais);
        }

        /// <summary>MD5 en hexadecimal minúsculas de (código de envío + código de centro + CP de destino), en UTF-8.</summary>
        internal static string Hash(string codigoEnvio, string centro, string codigoPostal)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes((codigoEnvio ?? string.Empty) + (centro ?? string.Empty) + (codigoPostal ?? string.Empty)));
                var texto = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    texto.Append(b.ToString("x2"));
                }
                return texto.ToString();
            }
        }

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }
}
