using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Único sitio que lee y parsea el parámetro AgenciasEnCuarentena (NestoAPI#607): lista de NOMBRES
    /// de agencia separados por comas (p. ej. "Sending, Correos Express"), sin distinguir mayúsculas.
    /// Lo usan la puerta de perfiles (<see cref="Perfiles.GateAgenciasActivasPorCuarentena"/>) y el
    /// comparador de selección (<see cref="Tarifas.ComparadorAgenciasFactory.ParaSeleccion"/>).
    ///
    /// La cuarentena es GENERAL: vale solo la fila del usuario «(defecto)» de la empresa por defecto
    /// (ParametrosUsuario, no hay tabla de parámetros generales en el edmx); las filas de cada usuario
    /// no cuentan aquí.
    /// </summary>
    public static class CuarentenaAgencias
    {
        public const string USUARIO_GENERAL = "(defecto)";
        public const string CLAVE = "AgenciasEnCuarentena";

        /// <summary>Valor crudo del parámetro; null si no existe.</summary>
        public static string LeerValor(NVEntities db)
        {
            ParametroUsuario parametro = db.ParametrosUsuario.FirstOrDefault(p =>
                p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && p.Usuario == USUARIO_GENERAL
                && p.Clave == CLAVE);
            return parametro?.Valor;
        }

        public static IEnumerable<string> ParsearNombres(string valor) =>
            (valor ?? string.Empty).Split(',').Select(n => n.Trim()).Where(n => n.Length > 0);

        /// <summary>
        /// Números de las agencias en cuarentena: las de <paramref name="agencias"/> cuyo nombre (sin el
        /// relleno del char) está en el parámetro. Un mismo nombre en varias empresas da varios números.
        /// </summary>
        public static ISet<int> Numeros(IEnumerable<AgenciaTransporte> agencias, string valorCuarentena)
        {
            var nombres = new HashSet<string>(ParsearNombres(valorCuarentena), StringComparer.OrdinalIgnoreCase);
            return new HashSet<int>((agencias ?? Enumerable.Empty<AgenciaTransporte>())
                .Where(a => nombres.Contains((a.Nombre ?? string.Empty).Trim()))
                .Select(a => a.Numero));
        }
    }
}
