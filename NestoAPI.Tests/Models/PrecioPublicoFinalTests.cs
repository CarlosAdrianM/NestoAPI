using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// Contrato del campo <c>PrestashopProductos.PVP_IVA_Incluido</c>. Tiene tres modos y uno de
    /// ellos es un sentinel, así que conviene que estén fijados por tests: este campo alimenta el
    /// precio de venta del cliente PUBLICO_FINAL, y un valor mal interpretado se convierte en un
    /// precio real de un pedido.
    ///
    ///   · positivo → precio público con IVA, fijado a mano
    ///   · NULL     → el público se deriva del PVP con el descuento por defecto (30 %)
    ///   · -1       → público = profesional
    ///
    /// Desde el cutover de precios (26/08/2026, módulo NestoSync 1.4.0) NestoAPI es EL DUEÑO del
    /// cálculo: los tres modos se resuelven en local y por el bus solo viajan los dos precios
    /// absolutos (profesional y público). El modo es información interna de Nesto; cuando un
    /// sistema externo publica sus precios, la intención se deduce con InferirModoPrecioPublico.
    /// </summary>
    [TestClass]
    public class PrecioPublicoFinalTests
    {
        [TestMethod]
        public void ResolverPrecioPublicoFinal_ValorPositivo_LoDevuelveTalCual()
        {
            // Precio fijado a mano: se sirve tal cual, sin fórmulas.
            Assert.AreEqual(29.95M, ProductoDTO.ResolverPrecioPublicoFinal(29.95M));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Nulo_NoEsUnPrecio()
        {
            // El caso mayoritario (10.006 de 10.322 productos el 25/08/2026): el público se
            // calcula del PVP con el 30 %.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(null),
                "NULL es una intención (regla general del 30 %), no un precio");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Sentinel_NoEsUnPrecio()
        {
            Assert.IsNull(
                ProductoDTO.ResolverPrecioPublicoFinal(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL),
                "El sentinel es una intención (público = profesional), no un precio");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Cero_NoEsUnPrecio()
        {
            // Comportamiento de siempre: el 0 nunca viajó como precio.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(0M));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_CualquierNegativo_NoViajaComoPrecio()
        {
            // REGRESIÓN: antes bastaba con ser distinto de 0 para devolverse como precio. Un -1
            // habría salido como precio público en la ficha y, dividido por el IVA en la plantilla
            // de PUBLICO_FINAL, como precio de venta de -0,83 €.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-1M));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-5M));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-0.01M));
        }

        // ===== Cálculo del precio público desde el PVP =====

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_IvaGeneral_AplicaElDescuentoDelTreintaYElIva()
        {
            // PVP / 0,7 × 1,21. El profesional es el público MENOS el 30 %, así que se divide;
            // multiplicar por 1,30 daría un 9,9 % menos.
            Assert.AreEqual(17.29M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_IvaReducido_UsaElDiez()
        {
            Assert.AreEqual(15.71M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 10M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_ProductoExento_NoSumaIva()
        {
            // Hay 82 productos vivos exentos. El atajo "si no es R10, 1,21" que usan otros puntos
            // del código les habría añadido un 21 % que no les corresponde.
            Assert.AreEqual(14.29M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 0M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_Superreducido_UsaElCuatro()
        {
            Assert.AreEqual(14.86M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 4M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_RedondeaAlAlzaComoPrestashop()
        {
            // PrestaShop usa PS_PRICE_ROUND_MODE = HALF_UP, que es AwayFromZero: el céntimo tiene
            // que coincidir con el que muestra la web o el mostrador cobraría distinto.
            // 7,25 / 0,7 = 10,357142... × 1,21 = 12,53214... → 12,53
            Assert.AreEqual(12.53M, ProductoDTO.CalcularPrecioPublicoDesdePvp(7.25M, 21M));
            // Caso que cae justo en el medio: 2,893424... redondea al alza, no a par
            Assert.AreEqual(2.90M, ProductoDTO.CalcularPrecioPublicoDesdePvp(1.6757M, 21M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_SiempreMayorQueElProfesionalConIva()
        {
            // Invariante de negocio: el público nunca puede salir por debajo del profesional, o
            // estaríamos vendiendo en tienda más barato que a los profesionales.
            decimal pvp = 12.50M;
            decimal profesionalConIva = pvp * 1.21M;

            Assert.IsTrue(ProductoDTO.CalcularPrecioPublicoDesdePvp(pvp, 21M) > profesionalConIva);
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_MismoQueProfesional_NoAplicaElTreinta()
        {
            // El sentinel -1: público = profesional + IVA. Aplicarle el 30 % lo dejaría un 42,86 %
            // por encima de lo que muestra la web.
            Assert.AreEqual(12.10M,
                ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M, mismoQueProfesional: true));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_MismoQueProfesional_EsMasBaratoQueElModoNormal()
        {
            decimal conDescuento = ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M);
            decimal mismoPrecio = ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M, mismoQueProfesional: true);

            Assert.IsTrue(mismoPrecio < conDescuento,
                "El modo 'mismo precio' nunca puede salir mas caro que el que lleva el 30 %");
        }

        // ===== Inferencia del modo al recibir precios de fuera (PrestaShop, Odoo) =====
        //
        // La operación inversa: del par (público, PVP) que llega por el bus se deduce la intención
        // que hay que guardar en PVP_IVA_Incluido. Tolerancia de DOS CÉNTIMOS (decidida el
        // 26/08/2026): PHP y C# pueden redondear con distintos decimales por el camino.

        [TestMethod]
        public void InferirModoPrecioPublico_ElDerivadoExacto_GuardaNull()
        {
            // PVP 10, IVA 21: derivado = 17,29. Es la regla general → NULL.
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(17.29M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_ElDerivadoConDosCentimosDeBaile_SigueSiendoNull()
        {
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(17.31M, 10M, 21M));
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(17.27M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_IgualQueElProfesional_GuardaElSentinel()
        {
            // PVP 10, IVA 21: profesional con IVA = 12,10. Público igual → -1.
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(12.10M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_IgualQueElProfesionalConDosCentimos_SigueSiendoSentinel()
        {
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(12.12M, 10M, 21M));
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(12.08M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_TresCentimosYaNoEsIgual_GuardaElPrecio()
        {
            // La tolerancia son DOS céntimos, sin rangos generosos: desconocido = precio fijo.
            Assert.AreEqual(17.32M, ProductoDTO.InferirModoPrecioPublico(17.32M, 10M, 21M));
            Assert.AreEqual(12.13M, ProductoDTO.InferirModoPrecioPublico(12.13M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_PrecioQueNoSaleDeNingunaFormula_GuardaElPrecio()
        {
            Assert.AreEqual(29.95M, ProductoDTO.InferirModoPrecioPublico(29.95M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_ProductoExento_CompararSinIva()
        {
            // Un curso exento: PVP 715, derivado = 715/0,7 = 1.021,43; profesional = 715.
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(1021.43M, 715M, 0M));
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(715M, 715M, 0M));
        }

        // ===== Contrato de serialización con los consumidores =====

        /// <summary>
        /// El módulo de PrestaShop distingue (estilo <c>array_key_exists</c>) entre clave con
        /// valor, clave presente con null (= no tocar el texto de la tienda) y clave AUSENTE.
        /// Si algún día se serializara con <c>WhenWritingNull</c> u opciones que omitan nulls,
        /// los textos dejarían de comportarse como "no tocar" SIN NINGÚN ERROR VISIBLE. Este test
        /// fija que las claves viajan presentes, porque el publisher usa los defaults de
        /// System.Text.Json y no hay nada explícito que lo garantice.
        /// </summary>
        [TestMethod]
        public void MensajeProductos_TextosNulos_LasClavesViajanPresentesConNull()
        {
            var mensaje = new NestoAPI.Models.Sincronizacion.ProductoSyncMessage
            {
                Tabla = "Productos",
                Source = "Nesto",
                Producto = "17404",
                NombrePersonalizado = null,
                Descripcion = null,
                DescripcionBreve = null
            };

            // Exactamente como en GooglePubSubEventPublisher: como object y sin opciones.
            string json = System.Text.Json.JsonSerializer.Serialize((object)mensaje);

            StringAssert.Contains(json, "\"NombrePersonalizado\":null");
            StringAssert.Contains(json, "\"Descripcion\":null");
            StringAssert.Contains(json, "\"DescripcionBreve\":null");
        }

        [TestMethod]
        public void InferirModoPrecioPublico_LaIdaYLaVueltaCierran()
        {
            // Round-trip: lo que Nesto publica con un modo, al volver de la tienda se infiere como
            // ESE MISMO modo. Si esto se rompe, cada ciclo de sincronización cambiaría el modo.
            decimal pvp = 24.6M;
            decimal iva = 21M;

            decimal publicadoDerivado = ProductoDTO.CalcularPrecioPublicoDesdePvp(pvp, iva);
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(publicadoDerivado, pvp, iva));

            decimal publicadoMismo = ProductoDTO.CalcularPrecioPublicoDesdePvp(pvp, iva, mismoQueProfesional: true);
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(publicadoMismo, pvp, iva));
        }
    }
}
