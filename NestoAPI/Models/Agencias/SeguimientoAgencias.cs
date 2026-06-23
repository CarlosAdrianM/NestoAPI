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
    }

    internal class SeguimientoGls : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "ASM";
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento) && !string.IsNullOrEmpty(d.CodigoPostal)
                ? $"https://mygls.gls-spain.es/e/{d.CodigoSeguimiento}/{d.CodigoPostal}"
                : null;
    }

    internal class SeguimientoOnTime : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "OnTime";
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
    }

    internal class SeguimientoCorreosExpress : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "Correos Express";
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://s.correosexpress.com/c?n={d.CodigoSeguimiento}"
                : null;
    }

    internal class SeguimientoSending : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "Sending";
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.Identificador) && !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://info.sending.es/fgts/pub/locNumServ.seam?cliente={d.Identificador}&localizador={d.CodigoSeguimiento}"
                : null;
    }

    internal class SeguimientoInnovatrans : IEstrategiaSeguimientoAgencia
    {
        public string Nombre => "Innovatrans";
        // Portal TIP-SA: id fijo de cliente (028040028040) + albarán (CodigoSeguimiento) de DataTrans.
        public string ConstruirUrl(DatosSeguimientoEnvio d)
            => !string.IsNullOrEmpty(d.CodigoSeguimiento)
                ? $"https://aplicaciones.tip-sa.com/cliente/datos_env.php?id=028040028040{d.CodigoSeguimiento.Trim()}"
                : null;
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
                new SeguimientoInnovatrans()
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
    }
}
