using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#405 — red de seguridad antes de imprimir el packing.
    ///
    /// El SP suma las ubicaciones reservadas de cada línea, así que una línea con ubicaciones de
    /// más sale en la hoja con más cantidad de la pedida y el almacén sirve de más sin que nadie
    /// lo note. El 25/08/2026 se detectó de casualidad, mirando una hoja impresa; sin esto, la
    /// siguiente vez se va en el camión.
    /// </summary>
    [TestClass]
    public class PackingUbicacionesDescuadradasTests
    {
        private static InformesService.LineaConUbicaciones Linea(int pedido, string producto, int pedidas, int reservadas, bool esFicticio = false)
            => new InformesService.LineaConUbicaciones
            {
                Pedido_ = pedido,
                Producto = producto,
                Pedido = pedidas,
                Ubicado = reservadas,
                EsFicticio = esFicticio
            };

        [TestMethod]
        public void ErroresDeUbicaciones_TodoCuadra_NoHayErrores()
        {
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924799, "37156", 4, 4),
                Linea(924799, "44702", 9, 9)
            };

            Assert.AreEqual(0, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }

        [TestMethod]
        public void ErroresDeUbicaciones_ReservadoElDoble_LoDetectaConLosNumeros()
        {
            // El caso real del picking 99327.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924799, "37156", 4, 8),
                Linea(924799, "44702", 9, 18)
            };

            List<string> errores = InformesService.ErroresDeUbicacionesDelPicking(lineas);

            Assert.AreEqual(2, errores.Count);
            StringAssert.Contains(errores[0], "37156");
            StringAssert.Contains(errores[0], "pedidas 4");
            StringAssert.Contains(errores[0], "reservadas 8");
        }

        [TestMethod]
        public void ErroresDeUbicaciones_MenosReservadoQuePedido_NoEsError()
        {
            // Una línea con "Recoger", servida a medias o sin ubicar del todo tiene MENOS ubicado
            // que pedido, y es perfectamente normal: si esto contara como error, el packing se
            // bloquearía a diario.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924799, "37156", 10, 4),
                Linea(924799, "44702", 9, 0)
            };

            Assert.AreEqual(0, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }

        [TestMethod]
        public void ErroresDeUbicaciones_LineaConCantidadNegativaSinReservar_NoEsError()
        {
            // El caso real del picking 99357 (28/08/2026): el pedido 924947 llevaba el cupón de
            // descuento "TiCKET" con cantidad -1 y, como es ficticio, 0 ubicaciones reservadas.
            // Con la comparación cruda 0 > -1 daba error y el almacén no podía imprimir la hoja.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924947, "24391", 2, 2),
                Linea(924947, "TiCKET", -1, 0, esFicticio: true)
            };

            Assert.AreEqual(0, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }

        [TestMethod]
        public void ErroresDeUbicaciones_DevolucionDeProductoRealSinReservar_NoEsError()
        {
            // Las devoluciones se meten como línea negativa de un producto REAL, que no es
            // ficticio: por eso no basta con saltarse los ficticios. El caso del pedido 924672
            // (21/08/2026), con el cartucho 12633 a -3.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924672, "12633", -3, 0, esFicticio: false)
            };

            Assert.AreEqual(0, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }

        [TestMethod]
        public void ErroresDeUbicaciones_ProductoFicticioConAlgoReservado_NoEsError()
        {
            // Un ficticio no tiene stock ni se ubica: pase lo que pase con las ubicaciones, no
            // hay nada que cuadrar y no debe frenar la hoja del almacén.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924947, "TiCKET", 1, 3, esFicticio: true)
            };

            Assert.AreEqual(0, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }

        [TestMethod]
        public void ErroresDeUbicaciones_ProductoRealConDeMas_SigueSiendoError()
        {
            // El caso que motivó el guard no se puede haber perdido por el camino: producto real,
            // cantidad positiva y más reservado que pedido.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924799, "37156", 4, 8, esFicticio: false)
            };

            Assert.AreEqual(1, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }

        [TestMethod]
        public void ErroresDeUbicaciones_UnaUnidadDeMas_TambienSeDetecta()
        {
            // No hace falta que sea el doble: reservar más de lo pedido nunca es legítimo.
            List<InformesService.LineaConUbicaciones> lineas = new List<InformesService.LineaConUbicaciones>
            {
                Linea(924333, "18004", 3, 4)
            };

            Assert.AreEqual(1, InformesService.ErroresDeUbicacionesDelPicking(lineas).Count);
        }
    }
}
