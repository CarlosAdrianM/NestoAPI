using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Perfiles
{
    /// <summary>
    /// NestoAPI#258: decide qué agencias están ACTIVAS. La reflexión de <see cref="RegistroAgencias"/>
    /// descubre TODAS las clases de perfil que existan en el ensamblado; esta puerta filtra las que
    /// no queremos usar ahora (aunque su clase exista). Así, activar/desactivar una agencia se hace
    /// desde la BBDD y no tocando código.
    /// </summary>
    public interface IGateAgenciasActivas
    {
        bool EstaActiva(int agenciaId);
    }

    /// <summary>
    /// Puerta por el parámetro AgenciasEnCuarentena (lista de NOMBRES de agencia separados por comas;
    /// hoy "Sending, Correos Express"): una agencia está activa si tiene fila en AgenciasTransporte y
    /// su nombre NO está en cuarentena. Para excluir otra (p.ej. OnTime) basta añadirla a la
    /// cuarentena desde la ventana de mantenimiento de agencias de Nesto. Es el MISMO criterio que ya
    /// usa el cliente, pero leído aquí server-side (lectura y parseo en <see cref="CuarentenaAgencias"/>,
    /// compartidos con el comparador de selección, NestoAPI#607).
    /// </summary>
    public class GateAgenciasActivasPorCuarentena : IGateAgenciasActivas
    {
        private readonly ISet<int> _activas;

        public GateAgenciasActivasPorCuarentena(NVEntities db)
            : this(db.AgenciasTransportes.ToList(), CuarentenaAgencias.LeerValor(db))
        {
        }

        // Núcleo puro (sin BBDD) para poder testear la lógica de cuarentena.
        internal GateAgenciasActivasPorCuarentena(IEnumerable<AgenciaTransporte> agencias, string valorCuarentena)
        {
            _activas = CalcularActivas(agencias, valorCuarentena);
        }

        public bool EstaActiva(int agenciaId) => _activas.Contains(agenciaId);

        /// <summary>
        /// Activa = existe en AgenciasTransporte, no está en cuarentena y NO es sombra. NestoAPI#493: una
        /// agencia sombra (CTT mientras solo compite en el comparador) puede tener ya su perfil con
        /// gestión remota y seguimiento preparados; sin esta exclusión, en cuanto existiera el perfil
        /// el poll de seguimiento y Tramitar la tratarían como una agencia real.
        /// </summary>
        internal static ISet<int> CalcularActivas(IEnumerable<AgenciaTransporte> agencias, string valorCuarentena)
        {
            List<AgenciaTransporte> lista = (agencias ?? Enumerable.Empty<AgenciaTransporte>()).ToList();
            ISet<int> enCuarentena = CuarentenaAgencias.Numeros(lista, valorCuarentena);
            return new HashSet<int>(lista
                .Where(a => !a.EsSombra)
                .Where(a => !enCuarentena.Contains(a.Numero))
                .Select(a => a.Numero));
        }
    }
}
