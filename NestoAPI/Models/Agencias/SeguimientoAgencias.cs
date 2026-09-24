using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// NestoAPI#240: cálculo polimórfico de la URL de seguimiento de un envío, en un único punto por
    /// agencia (estrategia por agencia + registro). Sustituye a los dos <c>switch (AgenciaNombre)</c>
    /// duplicados de <see cref="EnvioAgenciaDTO"/> y <see cref="UltimoEnvioClienteDTO"/>. Vive en Models
    /// (lógica pura de strings, sin dependencia a Infraestructure) para que los DTO puedan delegar.
    /// </summary>
    public class DatosSeguimientoEnvio
    {
        public string AgenciaNombre { get; set; }
        /// <summary>Identificador del cliente en la agencia (lo usa Sending).</summary>
        public string Identificador { get; set; }
        /// <summary>Albarán/localizador del envío (CodigoBarras o NumeroSeguimiento según el DTO).</summary>
        public string CodigoSeguimiento { get; set; }
        public string CodigoPostal { get; set; }
        public string Cliente { get; set; }
        public int Pedido { get; set; }
    }

    public interface IEstrategiaSeguimientoAgencia
    {
        string Nombre { get; }
        /// <summary>URL de seguimiento, o null si no hay datos suficientes para construirla.</summary>
        string ConstruirUrl(DatosSeguimientoEnvio datos);
        // NestoAPI#258 slice (a): cada agencia declara UNA vez sus identificadores por canal
        // externo; los canales de Nesto los consumen del DTO en vez de re-parsear el enlace.
        /// <summary>Id del transportista en Prestashop, o null si no vende por ese canal.</summary>
        string TransportistaPrestashop { get; }
        /// <summary>
        /// NestoAPI#417: valor a enviar como "nº de seguimiento" a Prestashop. Para las agencias
        /// del transportista genérico 160 (GLS/Innovatrans), cuya plantilla de URL en la tienda
        /// está vacía, es el ENLACE de seguimiento completo SIN esquema (la plantilla antepone
        /// "https://"); para las que tienen transportista propio con plantilla correcta
        /// (CEX/Sending), el número pelado. Null si la agencia no vende por ese canal.
        /// </summary>
        string TrackingPrestashop(DatosSeguimientoEnvio datos);
        /// <summary>
        /// CarrierCode para confirmar envíos en Amazon MFN, o null. OBLIGATORIO en España desde 2021:
        /// sin él Amazon acepta el feed pero NO la confirmación (24/09/26, CTT no se marcaba). Un
        /// transportista que no está en la lista de Amazon va como "Other" y su nombre en CarrierName.
        /// </summary>
        string CarrierCodeAmazon { get; }
        /// <summary>CarrierName para confirmar envíos en Amazon MFN, o null.</summary>
        string CarrierNameAmazon { get; }
        /// <summary>ShippingMethod para confirmar envíos en Amazon MFN, o null.</summary>
        string ShippingMethodAmazon { get; }
        /// <summary>
        /// Nesto#482 (22/09/26): números de AgenciasTransporte que atiende esta estrategia. Une el
        /// perfil de la agencia (por id) con su seguimiento (por nombre) para que un test pueda
        /// exigir que TODA agencia con perfil declare sus datos de canal: la de CTT no confirmaba en
        /// Amazon porque Nesto tenía un respaldo por enlace que no la conocía.
        /// </summary>
        IReadOnlyCollection<int> AgenciasId { get; }
    }

    internal static class CodigosAmazon
    {
        /// <summary>Transportista que no está en la lista de Amazon: el nombre va en CarrierName.</summary>
        internal const string OTRO = "Other";
    }

    internal class SeguimientoGls : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "ASM";
        public IReadOnlyCollection<int> AgenciasId => new[] { 1, 2, 3, 5 };
        // GLS e Innovatrans comparten el transportista genérico 160 de Prestashop
        public string TransportistaPrestashop => "160";
        public string CarrierCodeAmazon => "GLS";
        public string CarrierNameAmazon => "GLS";
        public string ShippingMethodAmazon => "Business Parcel";
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento) && !string.IsNullOrEmpty(d.CodigoPostal)
                ? $"https://mygls.gls-spain.es/e/{d.CodigoSeguimiento}/{d.CodigoPostal}"
                : null;
        // Transportista genérico sin plantilla: viaja el enlace entero (sin esquema). Si faltan
        // datos para el enlace (p. ej. sin CP), mejor el número pelado que nada.
        public string TrackingPrestashop(DatosSeguimientoEnvio d)
            => SeguimientoUrl.SinEsquema(ConstruirUrl(d)) ?? d.CodigoSeguimiento?.Trim();
    }

    internal class SeguimientoOnTime : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "OnTime";
        public IReadOnlyCollection<int> AgenciasId => new[] { 4, 6 };
        // OnTime no se usa en tienda online ni marketplaces
        public string TransportistaPrestashop => null;
        public string CarrierCodeAmazon => null;
        public string CarrierNameAmazon => null;
        public string ShippingMethodAmazon => null;
        public string ConstruirUrl(DatosSeguimientoEnvio d)
        {
            // OnTime no usa el código de seguimiento, sino cliente+pedido. Guarda estricta
            // (cliente informado y pedido > 0): para datos válidos produce la misma URL de antes.
            if (string.IsNullOrEmpty(d.Cliente) || d.Pedido <= 0)
            {
                return null;
            }
            string referencia = WebUtility.UrlEncode(d.Cliente.Trim() + "-" + d.Pedido);
            return $"https://ontimegts.alertran.net/gts/pub/clielocserv.seam?cliente=02890107&referencia={referencia}";
        }
        public string TrackingPrestashop(DatosSeguimientoEnvio d) => null;
    }

    internal class SeguimientoCorreosExpress : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "Correos Express";
        public IReadOnlyCollection<int> AgenciasId => new[] { Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, 9 };
        public string TransportistaPrestashop => "105";
        public string CarrierCodeAmazon => "Correos Express";
        public string CarrierNameAmazon => "Correos Express";
        public string ShippingMethodAmazon => "ePaq";
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://s.correosexpress.com/c?n={d.CodigoSeguimiento}"
                : null;
        // Transportista propio (105) con plantilla correcta: el número pelado, como siempre.
        public string TrackingPrestashop(DatosSeguimientoEnvio d) => d.CodigoSeguimiento?.Trim();
    }

    internal class SeguimientoSending : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "Sending";
        public IReadOnlyCollection<int> AgenciasId => new[] { Constantes.Agencias.AGENCIA_SENDING };
        public string TransportistaPrestashop => "103";
        public string CarrierCodeAmazon => CodigosAmazon.OTRO;
        public string CarrierNameAmazon => "Sending";
        public string ShippingMethodAmazon => "Send Exprés";
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.Identificador) && !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://info.sending.es/fgts/pub/locNumServ.seam?cliente={d.Identificador}&localizador={d.CodigoSeguimiento}"
                : null;
        // Transportista propio (103) con plantilla correcta: el número pelado, como siempre.
        public string TrackingPrestashop(DatosSeguimientoEnvio d) => d.CodigoSeguimiento?.Trim();
    }

    internal class SeguimientoInnovatrans : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "Innovatrans";
        public IReadOnlyCollection<int> AgenciasId => new[] { Constantes.Agencias.AGENCIA_INNOVATRANS };
        public string TransportistaPrestashop => "160";
        public string CarrierCodeAmazon => CodigosAmazon.OTRO;
        public string CarrierNameAmazon => "Innovatrans";
        public string ShippingMethodAmazon => "Estándar";
        // Portal TIP-SA: id fijo de cliente (028040028040) + albarán (CodigoSeguimiento) de DataTrans.
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://aplicaciones.tip-sa.com/cliente/datos_env.php?id=028040028040{d.CodigoSeguimiento.Trim()}"
                : null;
        // Comparte el transportista genérico 160: viaja el enlace entero (sin esquema).
        public string TrackingPrestashop(DatosSeguimientoEnvio d)
            => SeguimientoUrl.SinEsquema(ConstruirUrl(d)) ?? d.CodigoSeguimiento?.Trim();
    }

    internal class SeguimientoCTT : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "CTT";
        public IReadOnlyCollection<int> AgenciasId => new[] { Constantes.Agencias.AGENCIA_CTT };
        public string TransportistaPrestashop => "160";
        // 24/09/26: "Other" hasta confirmar el código exacto de la lista de Amazon (Seller Central lo
        // enseña como «CTTExpress» y entonces pide un «Servicio de envío» de su lista). Con "Other" la
        // confirmación se acepta siempre; con el código bueno Amazon valida además el seguimiento.
        public string CarrierCodeAmazon => CodigosAmazon.OTRO;
        public string CarrierNameAmazon => "CTT Express";
        // 24/09/26 (Enrique): el servicio que usamos es «CTT 48h». Para CTTExpress Seller Central no
        // tiene lista de servicios: marca «Otra» y se teclea el nombre.
        public string ShippingMethodAmazon => "CTT 48h";
        // Localizador público de CTT Express; sc = shipping_code (el albarán de 22 dígitos que
        // guardamos en CodigoSeguimiento). NestoAPI#493: verificar con un envío real al salir a producción.
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://www.cttexpress.com/localizador-de-envios?sc={d.CodigoSeguimiento.Trim()}"
                : null;
        public string TrackingPrestashop(DatosSeguimientoEnvio d)
            => SeguimientoUrl.SinEsquema(ConstruirUrl(d)) ?? d.CodigoSeguimiento?.Trim();
    }

    internal static class SeguimientoUrl
    {
        /// <summary>Quita el "https://" inicial: la plantilla del transportista genérico de
        /// Prestashop ya lo antepone al componer el enlace con el tracking.</summary>
        internal static string SinEsquema(string url)
            => url != null && url.StartsWith("https://") ? url.Substring("https://".Length) : url;
    }

    public static class RegistroSeguimientoAgencias
    {
        private static readonly Dictionary<string, IEstrategiaSeguimientoAgencia> PorNombre =
            new IEstrategiaSeguimientoAgencia[]
            {
                new SeguimientoGls(),
                new SeguimientoOnTime(),
                new SeguimientoCorreosExpress(),
                new SeguimientoSending(),
                new SeguimientoInnovatrans(),
                new SeguimientoCTT()
            }.ToDictionary(e => e.Nombre);

        /// <summary>¿Hay una estrategia de seguimiento para esa agencia?</summary>
        public static bool AgenciaConocida(string agenciaNombre)
            => agenciaNombre != null && PorNombre.ContainsKey(agenciaNombre);

        /// <summary>
        /// URL de seguimiento para los datos dados, o null si la agencia no se conoce o no hay datos
        /// suficientes. El "sin URL" (cadena vacía, null, texto de error) lo decide cada DTO.
        /// </summary>
        public static string ConstruirUrl(DatosSeguimientoEnvio datos)
            => datos?.AgenciaNombre != null && PorNombre.TryGetValue(datos.AgenciaNombre, out IEstrategiaSeguimientoAgencia estrategia)
                ? estrategia.ConstruirUrl(datos)
                : null;

        /// <summary>Estrategia de la agencia, o null si no está registrada (NestoAPI#258).</summary>
        /// <summary>Todas las estrategias registradas (para la guardia de cobertura por agencia).</summary>
        public static IReadOnlyCollection<IEstrategiaSeguimientoAgencia> Todas => PorNombre.Values.ToList();

        public static IEstrategiaSeguimientoAgencia Obtener(string agenciaNombre)
            => agenciaNombre != null && PorNombre.TryGetValue(agenciaNombre, out IEstrategiaSeguimientoAgencia estrategia)
                ? estrategia
                : null;
    }
}
