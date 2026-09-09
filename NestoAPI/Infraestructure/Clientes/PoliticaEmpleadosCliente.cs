using NestoAPI.Models;
using System;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#464: número de empleados de un centro, recogido al meter el rapport (Nesto#469,
    /// NestoApp#170) para la campaña de contratación de los alumnos del SEPE.
    ///
    /// <para>Reglas puras, sin base de datos. La de "¿a quién se le pregunta?" vive SOLO aquí: los
    /// clientes pintan la combo si <see cref="PreguntarEmpleados"/> es true y no saben por qué. El
    /// día que se pregunte en otra provincia, o se deje de preguntar cuando el dato tenga menos de
    /// N meses, se cambia en este sitio.</para>
    /// </summary>
    public static class PoliticaEmpleadosCliente
    {
        /// <summary>0 = sin empleados, 1..4 exactos, 5 = "5 o más".</summary>
        public const byte MAXIMO = 5;

        /// <summary>Hoy: centros de la Comunidad de Madrid (código postal que empieza por 28).</summary>
        public static bool PreguntarEmpleados(string codigoPostal)
        {
            return codigoPostal != null && codigoPostal.Trim().StartsWith("28");
        }

        public static bool EsValorValido(byte? empleados)
        {
            return !empleados.HasValue || empleados.Value <= MAXIMO;
        }

        /// <summary>
        /// Null si no hay nada que objetar; si no, el motivo para el cliente.
        /// </summary>
        public static string MotivoParaNoGuardar(byte? empleados)
        {
            return EsValorValido(empleados)
                ? null
                : "El número de empleados tiene que estar entre 0 (sin empleados) y 5 (5 o más).";
        }

        /// <summary>
        /// Aplica a la ficha lo que dijo el vendedor en el rapport. Con null no se toca nada (un
        /// rapport por teléfono puede no dar pie a preguntarlo). Con valor, se guarda si cambia y
        /// se refresca la fecha SIEMPRE: confirmar que sigue igual también es información.
        /// Devuelve true si la ficha ha cambiado en algo.
        /// </summary>
        public static bool AplicarAlCliente(Cliente principal, byte? empleadosRapport, DateTime ahora, string usuario)
        {
            if (principal == null || !empleadosRapport.HasValue)
            {
                return false;
            }
            if (principal.Empleados != empleadosRapport.Value)
            {
                principal.Empleados = empleadosRapport.Value;
            }
            principal.EmpleadosFecha = ahora;
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                principal.Usuario = usuario;
            }
            return true;
        }
    }
}
