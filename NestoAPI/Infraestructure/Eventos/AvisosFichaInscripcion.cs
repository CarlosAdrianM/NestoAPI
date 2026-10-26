using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// Sugerencia 776 (Reina): al inscribir desde la casa se avisa (sin bloquear) si a la ficha de la clienta le falta el
    /// teléfono o el correo electrónico, para que la tienda lo complete y pueda avisarla del evento. No se piden datos nuevos.
    /// <para>Teléfono: el de la dirección de la inscripción (Clientes.Teléfono) o el de cualquiera de sus personas de contacto.
    /// Vale si hay un fijo o un móvil español (<see cref="Telefono"/>) o un número internacional (+… / 00…).</para>
    /// <para>Correo: el de cualquier persona de contacto del cliente (de cualquier dirección), que es donde lo buscan los
    /// correos automáticos (cupón, recordatorio). Puede haber varios separados por «;» o «,»: basta uno válido.</para>
    /// </summary>
    public static class AvisosFichaInscripcion
    {
        public const string FALTA_TELEFONO =
            "A la ficha de la clienta le falta el teléfono: añádalo para poder avisarla del evento.";
        public const string FALTA_CORREO =
            "A la ficha de la clienta le falta el correo electrónico: añádalo para poder avisarla del evento.";
        public const string FALTAN_AMBOS =
            "A la ficha de la clienta le faltan el teléfono y el correo electrónico: añádalos para poder avisarla del evento.";

        /// <summary>Una persona de contacto, con lo que importa aquí.</summary>
        public class PersonaContacto
        {
            public string Contacto { get; set; }
            public string Telefono { get; set; }
            public string Correo { get; set; }
        }

        /// <param name="contacto">La dirección de la inscripción (sin espacios).</param>
        /// <param name="telefonoFicha">Clientes.Teléfono de esa dirección.</param>
        /// <param name="personas">Las personas de contacto del cliente (todas sus direcciones).</param>
        public static List<string> Calcular(string contacto, string telefonoFicha, IEnumerable<PersonaContacto> personas)
        {
            List<PersonaContacto> lista = (personas ?? Enumerable.Empty<PersonaContacto>()).Where(p => p != null).ToList();
            bool conTelefono = TieneTelefono(telefonoFicha)
                || lista.Any(p => p.Contacto?.Trim() == contacto?.Trim() && TieneTelefono(p.Telefono));
            bool conCorreo = lista.Any(p => TieneCorreo(p.Correo));
            return Textos(!conTelefono, !conCorreo);
        }

        public static List<string> Textos(bool faltaTelefono, bool faltaCorreo)
        {
            if (faltaTelefono && faltaCorreo)
            {
                return new List<string> { FALTAN_AMBOS };
            }
            if (faltaTelefono)
            {
                return new List<string> { FALTA_TELEFONO };
            }
            return faltaCorreo ? new List<string> { FALTA_CORREO } : new List<string>();
        }

        /// <summary>Algún fijo (9…) o móvil (6/7/8…) de 9 cifras, o un número internacional (+… o 00…) con al menos 9 cifras.</summary>
        public static bool TieneTelefono(string telefonos)
        {
            if (string.IsNullOrWhiteSpace(telefonos))
            {
                return false;
            }
            var telefono = new Telefono(telefonos);
            if (telefono.FijoUnico() != string.Empty || telefono.MovilUnico() != string.Empty)
            {
                return true;
            }
            return telefonos.Split('/').Select(t => t.Trim()).Any(t =>
                (t.StartsWith("+") || t.StartsWith("00")) && t.Count(char.IsDigit) >= 9);
        }

        public static bool TieneCorreo(string correos)
        {
            return !string.IsNullOrWhiteSpace(correos) && correos
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(c => ComercialesPedido.EsCorreoValido(c.Trim()));
        }
    }
}
