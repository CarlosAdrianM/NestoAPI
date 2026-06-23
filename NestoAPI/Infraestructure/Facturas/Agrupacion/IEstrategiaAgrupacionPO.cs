using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 2): estrategia de agrupación por P.O. (Purchase Order).
    /// Decide QUÉ pedidos agrupar y CUÁL es el destino; el movimiento lo ejecuta
    /// <see cref="IMotorAgrupacionPedidos"/>. Separada del núcleo para poder añadir en el
    /// futuro otras estrategias (p.ej. FDM) reutilizando el mismo motor.
    /// </summary>
    public interface IEstrategiaAgrupacionPO
    {
        /// <summary>
        /// Detecta los grupos de pedidos por PO listos para agrupar en una empresa:
        /// tuplas <c>(Empresa, Nº_Cliente, SuPedido)</c> con <c>MantenerJunto = true</c>,
        /// aún no agrupados, cuyos pedidos tienen TODAS sus líneas en albarán.
        /// </summary>
        IEnumerable<GrupoPedidosPO> SeleccionarGrupos(string empresa);

        /// <summary>
        /// Elige el pedido destino de un grupo: el que tenga el contacto
        /// <c>ClientePrincipal = 1</c>; si ninguno lo tiene, el más antiguo (menor número),
        /// ajustando su contacto de cabecera al del cliente principal para que el deudor de
        /// la factura sea el principal.
        /// </summary>
        CabPedidoVta ElegirDestino(GrupoPedidosPO grupo);
    }
}
