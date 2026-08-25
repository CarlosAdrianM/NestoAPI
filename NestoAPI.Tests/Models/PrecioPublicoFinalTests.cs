using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// Contrato del campo <c>PrestashopProductos.PVP_IVA_Incluido</c>, que escribe el módulo
    /// NestoSync de PrestaShop. Tiene tres modos y uno de ellos es un sentinel, así que conviene
    /// que estén fijados por tests: este campo alimenta el precio de venta del cliente
    /// PUBLICO_FINAL, y un valor mal interpretado se convierte en un precio real de un pedido.
    ///
    ///   · positivo → precio público con IVA, tal cual
    ///   · NULL     → el público lleva el 30 % de descuento por defecto; la regla vive SOLO en el
    ///                módulo de PrestaShop, así que hay que preguntárselo (devolvemos null)
    ///   · -1       → público = profesional, se calcula aquí
    /// </summary>
    [TestClass]
    public class PrecioPublicoFinalTests
    {
        private const string IVA_GENERAL = "G21";
        private const string IVA_REDUCIDO = "R10";

        [TestMethod]
        public void ResolverPrecioPublicoFinal_ValorPositivo_LoDevuelveTalCual()
        {
            // Caso "dos precios distintos": el módulo escribe el público con IVA y manda ese.
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(29.95M, 12.50M, IVA_GENERAL);

            Assert.AreEqual(29.95M, resultado);
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Nulo_PideElPrecioAPrestashop()
        {
            // El caso mayoritario (10.006 de 10.322 productos el 25/08/2026). El 30 % NO se replica
            // aquí a propósito: es un parámetro de negocio y vive en un solo sitio, el módulo.
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(null, 12.50M, IVA_GENERAL);

            Assert.IsNull(resultado, "Con NULL hay que preguntar a PrestaShop, no inventarse el descuento");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Sentinel_CalculaElProfesionalConIva()
        {
            // -1 = "público igual que profesional": 12,50 + 21 % = 15,13 (12,625 redondeado al alza).
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(
                Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL, 12.50M, IVA_GENERAL);

            Assert.AreEqual(15.13M, resultado);
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_SentinelConIvaReducido_UsaElDiez()
        {
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(
                Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL, 20M, IVA_REDUCIDO);

            Assert.AreEqual(22M, resultado);
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_SentinelNoSeCalculaConElFallback()
        {
            // Se resuelve EN LOCAL, no delegando en PrestaShop: LeerPrecioPublicoFinalDesdePrestashop
            // devuelve 0 si la tienda no responde, y ese 0 acaba siendo un precio de venta de 0 €
            // para PUBLICO_FINAL. Devolver un valor (y no null) es lo que evita esa ruta.
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(
                Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL, 10M, IVA_GENERAL);

            Assert.IsNotNull(resultado, "El sentinel NO puede acabar preguntando a PrestaShop");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Cero_NoEsUnPrecio()
        {
            // Comportamiento de siempre: el 0 nunca viajó como precio.
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(0M, 12.50M, IVA_GENERAL);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_NegativoQueNoEsElSentinel_TampocoViajaComoPrecio()
        {
            // REGRESIÓN: antes bastaba con ser distinto de 0 para devolverse como precio, así que un
            // -5 tecleado por error salía como precio público y, dividido por el IVA, como precio de
            // venta NEGATIVO en la plantilla de PUBLICO_FINAL.
            decimal? resultado = ProductoDTO.ResolverPrecioPublicoFinal(-5M, 12.50M, IVA_GENERAL);

            Assert.IsNull(resultado, "Un negativo que no sea el sentinel cae al fallback, no se sirve");
        }

        [TestMethod]
        public void FactorIva_SoloElReducidoBajaAlDiez()
        {
            Assert.AreEqual(1.10M, ProductoDTO.FactorIva(IVA_REDUCIDO));
            Assert.AreEqual(1.21M, ProductoDTO.FactorIva(IVA_GENERAL));
            Assert.AreEqual(1.21M, ProductoDTO.FactorIva(null), "Sin IVA en la ficha, el general");
            Assert.AreEqual(1.10M, ProductoDTO.FactorIva(" R10 "), "El char de la BD viene con relleno");
        }
    }
}
