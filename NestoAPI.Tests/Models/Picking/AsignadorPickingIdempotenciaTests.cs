using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#406 — el picking tiene que ser idempotente frente a dos ejecuciones solapadas.
    ///
    /// El 25/08/2026 el picking se sacó dos veces sobre los mismos pedidos dentro de la ventana
    /// que va del Rellenar() (lee las líneas con Picking null) al SaveChanges del finalizador.
    /// Las dos pasadas procesaron las mismas líneas: el número de picking se pisó y no se notó,
    /// pero cada pasada reservó SU ubicación, así que las líneas acabaron con el doble de
    /// unidades ubicadas. Como el SP del packing suma las ubicaciones de cada línea, la hoja
    /// salió con el doble y el almacén habría servido de más sin facturarlo.
    ///
    /// La huella que lo delató: el picking 99326 quedó consumido y sin usar, y el 99327 tenía
    /// sus 14 líneas con dos ubicaciones cada una.
    /// </summary>
    [TestClass]
    public class AsignadorPickingIdempotenciaTests
    {
        private const int PICKING_ACTUAL = 99327;

        [TestMethod]
        public void YaTienePickingDeOtraPasada_LineaSinAsignar_SeProcesa()
        {
            // El caso normal: el rellenador solo trae líneas con Picking null o 0.
            Assert.IsFalse(AsignadorPicking.YaTienePickingDeOtraPasada(new LinPedidoVta { Picking = null }, PICKING_ACTUAL));
            Assert.IsFalse(AsignadorPicking.YaTienePickingDeOtraPasada(new LinPedidoVta { Picking = 0 }, PICKING_ACTUAL));
        }

        [TestMethod]
        public void YaTienePickingDeOtraPasada_LineaDeLaMismaPasada_SeProcesa()
        {
            // Reentrada legítima: la propia ejecución vuelve a tocar una línea que ya numeró.
            // Si esto se tratara como "de otra pasada", el picking se saltaría sus propias líneas.
            LinPedidoVta linea = new LinPedidoVta { Picking = PICKING_ACTUAL };

            Assert.IsFalse(AsignadorPicking.YaTienePickingDeOtraPasada(linea, PICKING_ACTUAL));
        }

        [TestMethod]
        public void YaTienePickingDeOtraPasada_LineaYaAsignadaPorOtraEjecucion_SeSalta()
        {
            // El caso del 25/08: mientras esta pasada trabajaba, otra asignó la línea. Volver a
            // ubicarla es lo que duplicaba las unidades.
            LinPedidoVta linea = new LinPedidoVta { Picking = 99326 };

            Assert.IsTrue(AsignadorPicking.YaTienePickingDeOtraPasada(linea, PICKING_ACTUAL));
        }

        [TestMethod]
        public void YaTienePickingDeOtraPasada_LineaDeUnPickingAnterior_SeSalta()
        {
            // Una línea ya servida en un picking viejo tampoco se vuelve a coger, aunque llegue
            // hasta aquí por lo que sea.
            LinPedidoVta linea = new LinPedidoVta { Picking = 12345 };

            Assert.IsTrue(AsignadorPicking.YaTienePickingDeOtraPasada(linea, PICKING_ACTUAL));
        }
    }
}
