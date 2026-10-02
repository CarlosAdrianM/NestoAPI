using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Sugerencia 462 de Novedades (Laura, 02/10/26): un pedido que solo lleva cursos tiene que ir en la
    /// serie CV, porque prdCrearFacturaVta no factura cursos fuera de CV (salvo RC y la empresa 3) ni nada
    /// que no sea curso dentro de CV. Todo pedido nace en NV, así que si nadie la cambiaba a mano la
    /// factura fallaba. Aquí se pasa sola a CV, con la misma condición que el procedimiento.
    /// </summary>
    public static class SerieSegunLineas
    {
        private const string EMPRESA_SIN_REGLA_DE_CURSOS = "3";

        /// <summary>
        /// Devuelve CV si el pedido está en NV y todas sus líneas con grupo son cursos (las líneas sin
        /// grupo, como portes, no cuentan, igual que en el procedimiento). En cualquier otro caso
        /// devuelve la serie que tenía: no se toca ninguna otra serie ni un pedido mixto.
        /// </summary>
        public static string Resolver(string serieActual, string empresa, IEnumerable<LinPedidoVta> lineas)
        {
            if (serieActual?.Trim() != Constantes.Series.SERIE_POR_DEFECTO
                || empresa?.Trim() == EMPRESA_SIN_REGLA_DE_CURSOS
                || lineas == null)
            {
                return serieActual;
            }

            var grupos = lineas
                .Where(l => l.Grupo != null)
                .Select(l => l.Grupo.Trim())
                .ToList();

            bool soloCursos = grupos.Any() && grupos.All(g => g == Constantes.Productos.GRUPO_CURSOS);
            return soloCursos ? Constantes.Series.SERIE_CURSOS : serieActual;
        }
    }
}
