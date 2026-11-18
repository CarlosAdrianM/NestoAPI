using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Infraestructure.Agencias.Perfiles;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Sugerencia 1329 (18/11/26): ¿el estado de los envíos de esta agencia avanza SOLO (de Tramitado a Entregado, Incidentado
    /// o Devuelto) o se queda en Tramitado para siempre? Sin que avance solo, «lleva días tramitado» no dice nada: Canteras
    /// (operativa por correo, sin integración) no pasa nunca a Entregado y llenaba Retrasados durante 60 días.
    ///
    /// <para><b>LA REGLA</b>: una agencia tiene seguimiento automático si (1) el job de seguimiento la sabe consultar, que es
    /// exactamente <see cref="IFabricaAgenciasRemotas.AgenciasConSeguimiento"/> (perfiles con <see cref="IPerfilConSeguimiento"/>
    /// que pasan la puerta de activas: ni en cuarentena ni sombra), o (2) su perfil declara <see cref="IPerfilConEstadosPropios"/>
    /// (el reparto propio: el repartidor cierra los envíos desde Ariadna). Todas las demás (Canteras, la GLS de la empresa
    /// espejo, Glovo, Sending, Correos Express, cualquier agencia sin perfil o en cuarentena) NO tienen seguimiento automático.
    /// No hay ninguna agencia escrita aquí: una agencia nueva con su perfil de seguimiento entra sola.</para>
    /// </summary>
    public static class ReglaSeguimientoAutomaticoAgencia
    {
        private static readonly Lazy<IReadOnlyCollection<int>> ConEstadosPropiosDelRegistro = new Lazy<IReadOnlyCollection<int>>(
            () => RegistroAgencias.PorReflexionSinPuerta().ConCapacidad<IPerfilConEstadosPropios>());

        /// <summary>
        /// Las agencias con <see cref="IPerfilConEstadosPropios"/>. Del registro SIN puerta (no lee la BD): si una agencia así
        /// tiene envíos, es que se usa.
        /// </summary>
        public static IReadOnlyCollection<int> AgenciasConEstadosPropios => ConEstadosPropiosDelRegistro.Value;

        /// <summary>
        /// Las agencias con seguimiento automático: las que consulta el job (<paramref name="agenciasConSeguimiento"/>, la
        /// misma lista que usa <see cref="SeguimientoEnviosJobsService"/>) más las de estados propios.
        /// </summary>
        public static ISet<int> AgenciasConSeguimientoAutomatico(IEnumerable<int> agenciasConSeguimiento, IEnumerable<int> agenciasConEstadosPropios)
        {
            var resultado = new HashSet<int>(agenciasConSeguimiento ?? Enumerable.Empty<int>());
            resultado.UnionWith(agenciasConEstadosPropios ?? Enumerable.Empty<int>());
            return resultado;
        }

        /// <summary>Lo mismo leído de un registro de perfiles (para los tests de la regla con los perfiles reales).</summary>
        public static ISet<int> AgenciasConSeguimientoAutomatico(RegistroAgencias registro)
            => AgenciasConSeguimientoAutomatico(registro?.ConCapacidad<IPerfilConSeguimiento>(), registro?.ConCapacidad<IPerfilConEstadosPropios>());

        /// <summary>¿Avanza solo el estado de los envíos de esta agencia?</summary>
        public static bool TieneSeguimientoAutomatico(int agencia, ISet<int> agenciasConSeguimientoAutomatico)
            => agenciasConSeguimientoAutomatico != null && agenciasConSeguimientoAutomatico.Contains(agencia);
    }
}
