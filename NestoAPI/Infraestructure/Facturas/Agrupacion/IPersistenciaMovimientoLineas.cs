using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#729: lo que el motor de agrupación manda a la base de datos al mover líneas de unos pedidos a otro.
    /// EF6 no deja cambiar <c>Número</c> en una entidad seguida (es parte de la clave de LinPedidoVta): el movimiento va por
    /// SQL, como hace prdAgruparAlbaranesVta a través de vstAgruparAlbaranesVta, y los pedidos del grupo se sueltan del
    /// contexto para que no quede ninguna entidad con la clave vieja. En los tests se sustituye por un falso.
    /// </summary>
    internal interface IPersistenciaMovimientoLineas
    {
        /// <summary>
        /// Guarda lo pendiente del contexto (p. ej. el contacto del destino que cambia <see cref="DestinoAgrupacion"/>), suelta
        /// del contexto los <paramref name="pedidosDelGrupo"/> con sus líneas, pasa en la BD las líneas <paramref name="ordenes"/>
        /// (Nº Orden) al pedido <paramref name="destino"/> con sus PedidosEspeciales y Ubicaciones, y marca el destino como
        /// agrupado. Sin órdenes, solo marca el destino.
        /// </summary>
        void Mover(string empresa, int destino, IReadOnlyCollection<int> ordenes, IEnumerable<CabPedidoVta> pedidosDelGrupo);
    }
}
