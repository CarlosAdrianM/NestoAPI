using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Lee en <c>LogModificaciones</c> si alguien ha tecleado a mano el <c>Productos.PrecioMedio</c> de un producto
    /// después de la última pasada del SP (Issue #547: 22007, sombra del 08/11/26; 9,95 puesto a mano el 06/11/26
    /// por NUEVAVISION\Reina, cuando el SP y el C# dan 1,7412). SOLO LECTURA.
    /// </summary>
    public interface ILectorEdicionesPrecioMedio
    {
        /// <summary>
        /// La última modificación de <c>Productos.PrecioMedio</c> del producto en la empresa hecha por una persona
        /// (no por <see cref="EdicionesPrecioMedio.USUARIOS_DEL_SISTEMA"/>) después de la última pasada del SP de esa
        /// empresa (o, si no hay ninguna, en los últimos <see cref="EdicionesPrecioMedio.DIAS_SIN_PASADA"/> días), y
        /// siempre que el sistema no la haya vuelto a escribir después. Nulo si no hay.
        /// </summary>
        EdicionManualPrecioMedio UltimaEdicionAMano(string empresa, string producto);
    }

    /// <summary>Una fila de <c>LogModificaciones</c> de <c>Productos.PrecioMedio</c> escrita por una persona.</summary>
    public sealed class EdicionManualPrecioMedio
    {
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }

        /// <summary><c>LogModificaciones.ValorAntiguo</c> tal cual (texto, sin espacios): lo que había antes de teclearlo.</summary>
        public string ValorAntiguo { get; set; }
    }

    /// <summary>Reglas comunes de las ediciones a mano del precio medio.</summary>
    public static class EdicionesPrecioMedio
    {
        /// <summary>
        /// Usuarios que escriben el precio medio sin que nadie lo teclee: el job semanal del SP
        /// (<c>prdActualizarPreciosMediosDeTodosLosProductos</c> y sus triggers) y la API (incremental, #547 corte c).
        /// </summary>
        public static readonly IReadOnlyList<string> USUARIOS_DEL_SISTEMA = new[]
        {
            @"NUEVAVISION\Administrador",
            @"NUEVAVISION\RDS2016$"
        };

        /// <summary>Si no se encuentra ninguna pasada del SP en LogModificaciones, se mira este número de días hacia atrás.</summary>
        public const int DIAS_SIN_PASADA = 30;

        public static bool EsUsuarioDelSistema(string usuario)
        {
            string limpio = usuario?.Trim();
            return USUARIOS_DEL_SISTEMA.Any(u => string.Equals(u, limpio, StringComparison.OrdinalIgnoreCase));
        }
    }
}
