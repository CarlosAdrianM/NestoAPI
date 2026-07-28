using System.Collections.Generic;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: persistencia del registro de facturas subidas a Amazon en la tabla
    /// dbo.AmazonFacturasSubidas. Único punto de acceso a la tabla (SQL crudo).
    /// </summary>
    public interface IAlmacenFacturasAmazon
    {
        AmazonFacturaSubida Obtener(string empresa, int pedido);

        /// <summary>Filas de los pedidos indicados (para pintar el estado en el grid de Nesto).</summary>
        IReadOnlyList<AmazonFacturaSubida> ObtenerVarias(string empresa, IReadOnlyCollection<int> pedidos);

        /// <summary>Inserta el registro o, si el pedido ya tenía (resubida), lo actualiza y
        /// resetea el resultado.</summary>
        void Registrar(AmazonFacturaSubida fila);

        /// <summary>Filas en estado ENVIADA, pendientes de conocer el resultado del feed.</summary>
        IReadOnlyList<AmazonFacturaSubida> ObtenerPendientesResultado();

        void ActualizarResultado(int id, string estado, string resultado);
    }
}
