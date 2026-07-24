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
    /// usa el cliente, pero leído aquí server-side.
    /// </summary>
    public class GateAgenciasActivasPorCuarentena : IGateAgenciasActivas
    {
        // El parámetro es "general": se guarda por usuario, con una fila de usuario (defecto) que es
        // la que vale como valor común. Vive en ParametrosUsuario (no hay tabla de parámetros
        // generales en el edmx). Se lee la del usuario (defecto) de la empresa por defecto.
        internal const string USUARIO_GENERAL = "(defecto)";
        internal const string CLAVE_CUARENTENA = "AgenciasEnCuarentena";

        private readonly ISet<int> _activas;

        public GateAgenciasActivasPorCuarentena(NVEntities db)
            : this(db.AgenciasTransportes.ToList(), LeerValorCuarentena(db))
        {
        }

        // Núcleo puro (sin BBDD) para poder testear la lógica de cuarentena.
        internal GateAgenciasActivasPorCuarentena(IEnumerable<AgenciaTransporte> agencias, string valorCuarentena)
        {
            _activas = CalcularActivas(agencias, valorCuarentena);
        }

        public bool EstaActiva(int agenciaId) => _activas.Contains(agenciaId);

        internal static ISet<int> CalcularActivas(IEnumerable<AgenciaTransporte> agencias, string valorCuarentena)
        {
            var cuarentena = new HashSet<string>(ParsearNombres(valorCuarentena), StringComparer.OrdinalIgnoreCase);
            return new HashSet<int>((agencias ?? Enumerable.Empty<AgenciaTransporte>())
                .Where(a => !cuarentena.Contains((a.Nombre ?? string.Empty).Trim()))
                .Select(a => a.Numero));
        }

        internal static IEnumerable<string> ParsearNombres(string valor) =>
            (valor ?? string.Empty).Split(',').Select(n => n.Trim()).Where(n => n.Length > 0);

        private static string LeerValorCuarentena(NVEntities db)
        {
            ParametroUsuario parametro = db.ParametrosUsuario.FirstOrDefault(p =>
                p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && p.Usuario == USUARIO_GENERAL
                && p.Clave == CLAVE_CUARENTENA);
            return parametro?.Valor;
        }
    }
}
