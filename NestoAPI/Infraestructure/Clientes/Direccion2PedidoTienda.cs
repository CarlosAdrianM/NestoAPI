using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>La Dirección 2 de la entrega de un pedido de la tienda online y su tipo de lugar.</summary>
    public sealed class Direccion2PedidoTiendaDTO
    {
        /// <summary>En mayúsculas, como Clientes.Direccion2 («3A», «LOCAL · RÓTULO: VENUS ESTILISTAS», «3A · RÓTULO: ESTÉTICA GOJI»); null si nada.</summary>
        public string Direccion2 { get; set; }
        /// <summary>«Local» si la dirección trae empresa (company); si no, null. Nunca se inventa «Piso».</summary>
        public string TipoLugar { get; set; }
    }

    /// <summary>
    /// NestoAPI#878 (Carlos 26/11/26, parte del importador): en PrestaShop la «Dirección complementaria» (<c>address2</c>, opcional, la
    /// rellena un tercio de los pedidos) y la empresa (<c>company</c>, casi la mitad) son lo que el repartidor necesita además de la calle.
    /// LA REGLA (pura):
    /// <list type="bullet">
    /// <item><see cref="Componer"/>: <c>address2</c> en mayúsculas es la Dirección 2 de la entrega; si <c>company</c> viene relleno y no está ya
    /// contenido en <c>address2</c> (sin mayúsculas ni tildes), se añade «· RÓTULO: COMPANY» como en el alta de clientes
    /// (<see cref="Direccion2Cliente"/>: «LOCAL · RÓTULO: …» si no hay <c>address2</c>); el tipo de lugar, «Local» con company y vacío si no.
    /// Como mucho <see cref="Direccion2Cliente.LONGITUD"/> caracteres.</item>
    /// <item>El importador de pedidos web (CanalesExternos de Nesto, no esta API) la deja en los comentarios del pedido en UNA línea
    /// «DIRECCIÓN 2: …» (<see cref="LineaComentario"/>), en lugar de la línea suelta con <c>address2</c> de antes; <see cref="LeerDeComentarios"/>
    /// la lee de forma tolerante (sin mayúsculas ni tildes, espacios alrededor de los dos puntos). La marca «TOTAL PEDIDO:» de
    /// <see cref="VigenciaPedidoTienda"/> no cambia.</item>
    /// </list>
    /// </summary>
    public static class Direccion2PedidoTienda
    {
        public const string ETIQUETA = "DIRECCIÓN 2:";
        private const string ROTULO = "RÓTULO: ";

        private static readonly CultureInfo ESPANOL = new CultureInfo("es-ES");
        private static readonly Regex ESPACIOS = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex LINEA = new Regex(@"^[ \t]*DIRECCI[OÓ]N[ \t]*2[ \t]*:[ \t]*(?<texto>[^\r\n]*?)[ \t]*\r?$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Direccion2PedidoTiendaDTO Componer(string address2, string company)
        {
            string direccion2 = Limpiar(address2);
            string empresa = Limpiar(company);
            string tipo = empresa == null ? null : TiposLugar.LOCAL;
            if (empresa != null && (direccion2 == null || !SinTildes(direccion2).Contains(SinTildes(empresa))))
            {
                string rotulo = ROTULO + empresa;
                direccion2 = direccion2 == null ? "LOCAL" + Direccion2Cliente.SEPARADOR_ROTULO + rotulo : direccion2 + Direccion2Cliente.SEPARADOR_ROTULO + rotulo;
            }
            if (direccion2 != null && direccion2.Length > Direccion2Cliente.LONGITUD)
            {
                direccion2 = direccion2.Substring(0, Direccion2Cliente.LONGITUD).TrimEnd();
            }
            return new Direccion2PedidoTiendaDTO { Direccion2 = direccion2, TipoLugar = direccion2 == null ? null : tipo };
        }

        /// <summary>«DIRECCIÓN 2: 3A · RÓTULO: ESTÉTICA GOJI»; null si no hay Dirección 2 (entonces no se escribe la línea).</summary>
        public static string LineaComentario(string direccion2)
        {
            string limpio = Limpiar(direccion2);
            return limpio == null ? null : ETIQUETA + " " + limpio;
        }

        /// <summary>La Dirección 2 de la primera línea «DIRECCIÓN 2: …» de los comentarios (en mayúsculas); null si no la hay o va vacía.</summary>
        public static string LeerDeComentarios(string comentarios)
        {
            if (string.IsNullOrWhiteSpace(comentarios))
            {
                return null;
            }
            foreach (Match m in LINEA.Matches(comentarios))
            {
                string texto = Limpiar(m.Groups["texto"].Value);
                if (texto != null)
                {
                    return texto.Length > Direccion2Cliente.LONGITUD ? texto.Substring(0, Direccion2Cliente.LONGITUD).TrimEnd() : texto;
                }
            }
            return null;
        }

        /// <summary>El tipo de lugar que se deduce de una Dirección 2 leída: «Local» si lleva rótulo; si no, null.</summary>
        public static string TipoLugarDe(string direccion2)
            => direccion2 != null && SinTildes(direccion2).Contains(SinTildes(ROTULO.Trim())) ? TiposLugar.LOCAL : null;

        private static string Limpiar(string texto)
            => string.IsNullOrWhiteSpace(texto) ? null : ESPACIOS.Replace(texto.Trim(), " ").ToUpper(ESPANOL);

        private static string SinTildes(string texto)
        {
            string descompuesto = (texto ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
            return new string(descompuesto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        }
    }
}
