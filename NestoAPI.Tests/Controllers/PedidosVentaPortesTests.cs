using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Issue #218: la casilla "Añadir portes" de PlantillaVenta debe respetarse en el servidor,
    /// pero solo Almacén/Compras pueden suprimir portes; REI/ALC nunca los añaden.
    /// Tests de la lógica pura <see cref="PedidosVentaController.DebeAnadirPortes"/>.
    /// </summary>
    [TestClass]
    public class PedidosVentaPortesTests
    {
        private const string ALG = Constantes.Almacenes.ALGETE;
        private const string REI = Constantes.Almacenes.REINA;
        private const string ALC = Constantes.Almacenes.ALCOBENDAS;

        // --- Usuario AUTORIZADO (Almacén/Compras) en ALG: la casilla manda ---

        [TestMethod]
        public void DebeAnadirPortes_AutorizadoDesmarcaEnAlgete_NoAnadePortes()
        {
            // Almacén/Compras desmarca "Añadir portes" en ALG → se respeta: no añade
            Assert.IsFalse(PedidosVentaController.DebeAnadirPortes(puedeSuprimirPortes: true, anadirPortesSolicitado: false, almacen: ALG));
        }

        [TestMethod]
        public void DebeAnadirPortes_AutorizadoMarcaEnAlgete_AnadePortes()
        {
            // Almacén/Compras marca "Añadir portes" en ALG → añade
            Assert.IsTrue(PedidosVentaController.DebeAnadirPortes(puedeSuprimirPortes: true, anadirPortesSolicitado: true, almacen: ALG));
        }

        // --- Usuario NO autorizado: aunque desmarque, lleva portes en ALG ---

        [TestMethod]
        public void DebeAnadirPortes_NoAutorizadoDesmarcaEnAlgete_AnadePortesIgualmente()
        {
            // Un usuario sin permiso (o cliente que manda false sin derecho) igualmente lleva portes
            Assert.IsTrue(PedidosVentaController.DebeAnadirPortes(puedeSuprimirPortes: false, anadirPortesSolicitado: false, almacen: ALG));
        }

        [TestMethod]
        public void DebeAnadirPortes_NoAutorizadoMarcaEnAlgete_AnadePortes()
        {
            Assert.IsTrue(PedidosVentaController.DebeAnadirPortes(puedeSuprimirPortes: false, anadirPortesSolicitado: true, almacen: ALG));
        }

        // --- REI/ALC: nunca llevan portes, aunque se pidan y aunque no pueda suprimir ---

        [DataTestMethod]
        [DataRow(REI, true, true)]    // autorizado pidiendo portes en REI
        [DataRow(REI, false, true)]   // no autorizado pidiendo portes en REI
        [DataRow(ALC, true, true)]    // autorizado pidiendo portes en ALC
        [DataRow(ALC, false, true)]   // no autorizado pidiendo portes en ALC
        public void DebeAnadirPortes_AlmacenSinPortes_NuncaAnade(string almacen, bool puedeSuprimir, bool solicitado)
        {
            Assert.IsFalse(PedidosVentaController.DebeAnadirPortes(puedeSuprimir, solicitado, almacen));
        }

        // --- Retrocompatibilidad: cliente que no envía nada (default true) en ALG → añade ---

        [TestMethod]
        public void DebeAnadirPortes_DefaultTrueEnAlgete_AnadePortes()
        {
            // Equivale a un cliente legacy (NestoApp/TiendasNuevaVision) que no manda el flag:
            // el DTO trae AnadirPortes=true por defecto → comportamiento de siempre.
            Assert.IsTrue(PedidosVentaController.DebeAnadirPortes(puedeSuprimirPortes: true, anadirPortesSolicitado: true, almacen: ALG));
        }
    }
}
