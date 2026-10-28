using NestoAPI.Models;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: estrategia de agrupación de FIN DE MES (lo que hacían prdAgruparAlbaranesVta y el Nesto viejo).
    /// Decide QUÉ pedidos se reúnen y CUÁL es el destino; el movimiento lo hace <see cref="IMotorAgrupacionPedidos"/>.
    /// </summary>
    public interface IEstrategiaAgrupacionFinDeMes
    {
        /// <summary>
        /// Un grupo por (empresa, cliente) con los pedidos de periodo FDM, sin P.O. y no agrupados que tengan alguna línea
        /// en albarán con fecha de albarán igual o anterior a <paramref name="fechaCorte"/>. Con <paramref name="cliente"/>
        /// solo ese cliente; con <paramref name="contacto"/> (exige cliente) solo los pedidos de ese contacto.
        /// </summary>
        IEnumerable<GrupoPedidosFinDeMes> SeleccionarGrupos(string empresa, DateTime fechaCorte, string cliente = null, string contacto = null);

        /// <summary>
        /// El destino: entre los pedidos del grupo que no tienen albaranes posteriores al corte (si no, la factura se los
        /// llevaría), el del contacto del cliente principal; si no, el más antiguo con el contacto principal en cabecera.
        /// <c>null</c> si todos tienen albaranes posteriores al corte.
        /// </summary>
        CabPedidoVta ElegirDestino(GrupoPedidosFinDeMes grupo, DateTime fechaCorte);
    }
}
