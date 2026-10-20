using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#650: las sugerencias privadas (Novedades.Privada = 1). Solo las ven quien la crea y
    /// Dirección / Informática; para los demás no existen (ni en las listas, ni en el buscador, ni sus
    /// comentarios, imágenes o adjuntos, ni les llega ningún aviso de ellas). No admiten «Me gusta» y
    /// van detrás de las públicas en la lista.
    ///
    /// <para>Se calcula una vez por petición a partir de la lista de privadas (pocas filas). Si esa lista
    /// no se ha podido leer (fallo de la BD; que la columna aún no exista NO es un fallo: entonces no hay
    /// ninguna privada), se cierra por seguridad: quien no es Dirección / Informática solo ve las
    /// novedades con versión y sus propias sugerencias, y nada que se pida por id.</para>
    /// </summary>
    public sealed class PrivacidadNovedades
    {
        public const string ERROR_VOTO_PRIVADA =
            "Las sugerencias privadas no admiten «Me gusta». Si quiere que otros compañeros puedan apoyarla, pídanos que la hagamos pública.";
        public const string ERROR_SIN_COLUMNA =
            "Todavía no se pueden enviar sugerencias privadas. Envíela como pública o inténtelo de nuevo más tarde.";

        private readonly Dictionary<int, SugerenciaPrivadaFila> privadas;
        private readonly string usuario;

        private PrivacidadNovedades(IEnumerable<SugerenciaPrivadaFila> privadas, string usuario, bool revisa, bool noSeHaPodidoLeer)
        {
            this.privadas = (privadas ?? Enumerable.Empty<SugerenciaPrivadaFila>())
                .Where(p => p != null)
                .GroupBy(p => p.Id)
                .ToDictionary(g => g.Key, g => g.First());
            this.usuario = usuario;
            Revisa = revisa;
            NoSeHaPodidoLeer = noSeHaPodidoLeer;
        }

        /// <summary>Las privadas leídas de la BD (lista vacía si no hay o si la columna aún no existe).</summary>
        public static PrivacidadNovedades Desde(IEnumerable<SugerenciaPrivadaFila> privadas, string usuario, bool revisa) =>
            new PrivacidadNovedades(privadas, usuario, revisa, false);

        /// <summary>No se ha podido leer cuáles son privadas: se cierra por seguridad.</summary>
        public static PrivacidadNovedades SinPoderLeer(string usuario, bool revisa) =>
            new PrivacidadNovedades(null, usuario, revisa, true);

        /// <summary>La misma, con una privada más (la que se acaba de crear, para no volver a leer de la BD).</summary>
        public PrivacidadNovedades ConOtraPrivada(SugerenciaPrivadaFila nueva) =>
            new PrivacidadNovedades(privadas.Values.Concat(new[] { nueva }), usuario, Revisa, NoSeHaPodidoLeer);

        /// <summary>Dirección / Informática: lo ven todo.</summary>
        public bool Revisa { get; }

        public bool NoSeHaPodidoLeer { get; }

        /// <summary>Si hay alguna privada (o no se sabe): si no, no hace falta mirar nada más.</summary>
        public bool HayQueMirar => NoSeHaPodidoLeer || privadas.Count > 0;

        public bool EsPrivada(int novedadId) => privadas.ContainsKey(novedadId);

        /// <summary>Lo que se pide por id (detalle, comentarios, imagen, adjuntos, votar, comentar).</summary>
        public bool PuedeVer(int novedadId)
        {
            if (Revisa)
            {
                return true;
            }
            if (NoSeHaPodidoLeer)
            {
                return false;
            }
            return !privadas.TryGetValue(novedadId, out SugerenciaPrivadaFila privada) || EsSuAutor(privada.SugeridaPor, usuario);
        }

        /// <summary>
        /// En las listas y el buscador. Sin poder leer las privadas, las novedades con versión y las sugerencias
        /// propias (las de los demás, por si acaso, no).
        /// </summary>
        public bool PuedeVerEnLista(int novedadId, string version, string sugeridaPor)
        {
            if (Revisa)
            {
                return true;
            }
            if (NoSeHaPodidoLeer)
            {
                return !string.IsNullOrWhiteSpace(version) || EsSuAutor(sugeridaPor, usuario);
            }
            return PuedeVer(novedadId);
        }

        /// <summary>
        /// Avisos (menciones, actividad, respuestas): de una privada solo se avisa a su autor y a los de
        /// Dirección / Informática (<paramref name="quienesRevisan"/>: usuarios del dominio sin el dominio,
        /// se piden solo si hacen falta). La clave puede venir con dominio («NUEVAVISION\Carlos») o ser el
        /// UserName de la app.
        /// </summary>
        public bool PuedeRecibirAviso(int novedadId, string clave, Func<IEnumerable<string>> quienesRevisan)
        {
            if (!NoSeHaPodidoLeer && !EsPrivada(novedadId))
            {
                return true;
            }
            string sinDominio = SinDominio(clave);
            if (sinDominio.Length == 0)
            {
                return false;
            }
            if (privadas.TryGetValue(novedadId, out SugerenciaPrivadaFila privada)
                && (string.Equals(SinDominio(privada.SugeridaPor), sinDominio, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SinDominio(privada.SugeridaNombre), sinDominio, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return (quienesRevisan?.Invoke() ?? Enumerable.Empty<string>())
                .Any(m => string.Equals(SinDominio(m), sinDominio, StringComparison.OrdinalIgnoreCase));
        }

        private static bool EsSuAutor(string sugeridaPor, string usuario) =>
            !string.IsNullOrWhiteSpace(usuario)
            && string.Equals(sugeridaPor?.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string SinDominio(string usuario)
        {
            string limpio = (usuario ?? string.Empty).Trim();
            return limpio.Substring(limpio.IndexOf('\\') + 1).Trim();
        }
    }
}
