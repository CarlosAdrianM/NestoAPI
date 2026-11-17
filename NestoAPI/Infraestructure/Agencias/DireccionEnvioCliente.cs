using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#874: la dirección y las observaciones del envío ya con la Dirección 2 del cliente.</summary>
    public sealed class DireccionEnvioCompuesta
    {
        public string Direccion { get; internal set; }
        public string Observaciones { get; internal set; }
        /// <summary>La Dirección 2 no cabe en ningún sitio: hay que ponerla a mano (de usted). Null si ha cabido o no había.</summary>
        public string Aviso { get; internal set; }
        /// <summary>Ha ido a las observaciones porque en la dirección no cabía.</summary>
        public bool EnObservaciones { get; internal set; }
    }

    /// <summary>
    /// NestoAPI#874 (Carlos 17/11/26): el rótulo, el piso o la nave tienen que llegar SIEMPRE al repartidor. Regla pura y única
    /// para todo envío que sale de la ficha del cliente:
    /// <list type="number">
    /// <item>Dirección del envío (80) = Dirección + «, » + Dirección 2, si cabe.</item>
    /// <item>Si no, la Dirección 2 va a Observaciones (80) delante de lo que ya hubiera (con « · »), sin quitar nada.</item>
    /// <item>Si tampoco cabe, todo queda como estaba y se devuelve <see cref="DireccionEnvioCompuesta.Aviso"/>.</item>
    /// </list>
    /// Sin Dirección 2, dirección y observaciones salen intactas (ni siquiera se recortan).
    /// </summary>
    public static class DireccionEnvioCliente
    {
        public const int LONGITUD_DIRECCION = 80;
        public const int LONGITUD_OBSERVACIONES = 80;
        internal const string SEPARADOR_DIRECCION = ", ";
        internal const string SEPARADOR_OBSERVACIONES = " · ";

        public static DireccionEnvioCompuesta Componer(string direccion, string direccion2, string observaciones)
        {
            var resultado = new DireccionEnvioCompuesta { Direccion = direccion, Observaciones = observaciones };
            string d2 = direccion2?.Trim();
            if (string.IsNullOrEmpty(d2))
            {
                return resultado;
            }
            string calle = direccion?.Trim() ?? string.Empty;
            string conDireccion2 = calle.Length == 0 ? d2 : calle + SEPARADOR_DIRECCION + d2;
            if (conDireccion2.Length <= LONGITUD_DIRECCION)
            {
                resultado.Direccion = conDireccion2;
                return resultado;
            }
            string obs = observaciones?.Trim() ?? string.Empty;
            string conObservaciones = obs.Length == 0 ? d2 : d2 + SEPARADOR_OBSERVACIONES + obs;
            if (conObservaciones.Length <= LONGITUD_OBSERVACIONES)
            {
                resultado.Observaciones = conObservaciones;
                resultado.EnObservaciones = true;
                return resultado;
            }
            resultado.Aviso = MensajeNoCabe(d2);
            return resultado;
        }

        public static string MensajeNoCabe(string direccion2)
            => $"La dirección 2 del cliente («{direccion2?.Trim()}») no cabe en la dirección ni en las observaciones del envío: " +
               "añádala a mano para que le llegue al repartidor.";

        /// <summary>Núcleo para quien rellena el envío desde la ficha (lee la Dirección 2 y compone).</summary>
        public static DireccionEnvioCompuesta DesdeFicha(NVEntities db, string empresa, string cliente, string contacto, string direccion, string observaciones)
            => Componer(direccion, Direccion2ClienteSql.Leer(db, empresa, cliente, contacto), observaciones);
    }
}
