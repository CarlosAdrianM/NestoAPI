using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System.Text.Json;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// Contrato del campo <c>PrestashopProductos.PVP_IVA_Incluido</c>, compartido con el módulo
    /// NestoSync de PrestaShop (v1.4.0). Tiene tres modos y uno de ellos es un sentinel, así que
    /// conviene que estén fijados por tests: este campo alimenta el precio de venta del cliente
    /// PUBLICO_FINAL, y un valor mal interpretado se convierte en un precio real de un pedido.
    ///
    ///   · positivo → precio público con IVA, tal cual
    ///   · NULL     → el público lleva el descuento por defecto (30 %)
    ///   · -1       → público = profesional
    ///
    /// NestoAPI solo sirve el valor positivo; los otros dos se los pregunta a PrestaShop, que es
    /// quien deriva product.price y por tanto el dueño de ese cálculo.
    /// </summary>
    [TestClass]
    public class PrecioPublicoFinalTests
    {
        [TestMethod]
        public void ResolverPrecioPublicoFinal_ValorPositivo_LoDevuelveTalCual()
        {
            // Caso "dos precios distintos": el módulo escribe el público con IVA y ese es el bueno.
            Assert.AreEqual(29.95M, ProductoDTO.ResolverPrecioPublicoFinal(29.95M));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Nulo_PideElPrecioAPrestashop()
        {
            // El caso mayoritario (10.006 de 10.322 productos el 25/08/2026). El 30 % NO se replica
            // aquí a propósito: es un parámetro de negocio y tiene un único dueño, el módulo.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(null),
                "Con NULL hay que preguntar a PrestaShop, no inventarse el descuento");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Sentinel_PideElPrecioAPrestashop()
        {
            // Tentación descartada: calcularlo aquí como PVP × (1+IVA). NestoAPI simplifica el IVA
            // a "1,10 si R10, si no 1,21", y hay 88 productos exentos y 49 al 4 % que saldrían
            // inflados hasta un 21 %. El módulo usa el IVA real de las reglas fiscales.
            Assert.IsNull(
                ProductoDTO.ResolverPrecioPublicoFinal(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL),
                "El sentinel NO es un precio: lo deriva el módulo y se lee del webservice");
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
            // de PUBLICO_FINAL, como precio de venta de -0,83 €. El módulo trata cualquier negativo
            // como el sentinel, así que aquí ninguno puede pasar.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-1M));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-5M));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-0.01M));
        }

        // ===== Cálculo local, para cuando PrestaShop no da precio =====

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_IvaGeneral_AplicaElDescuentoDelTreintaYElIva()
        {
            // Réplica de la fórmula del módulo: PVP / 0,7 × 1,21. El profesional es el público
            // MENOS el 30 %, así que se divide; multiplicar por 1,30 daría un 9,9 % menos.
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
            // Si PrestaShop no responde para un producto marcado con el sentinel -1, el fallback
            // tiene que respetar SU modo: público = profesional + IVA. Aplicarle el 30 % lo dejaría
            // un 42,86 % por encima de lo que muestra la web.
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

        // ===== Contrato de serialización con el módulo =====

        /// <summary>
        /// El módulo distingue con <c>array_key_exists</c> (no <c>isset</c>) tres estados:
        /// clave con valor, clave presente con null (= modo 30 %) y clave AUSENTE (= no tocar nada).
        ///
        /// Si algún día se serializara con <c>WhenWritingNull</c> u opciones que omitan nulls, el
        /// módulo leería "no cambiar" y los precios dejarían de actualizarse SIN NINGÚN ERROR
        /// VISIBLE. Este test es la única cosa que impide ese fallo silencioso, porque el publisher
        /// serializa con los defaults de System.Text.Json y no hay nada explícito que lo fije.
        /// </summary>
        [TestMethod]
        public void MensajePrestashop_ConPvpNulo_LaClaveViajaPresenteConNull()
        {
            var mensaje = new PrestashopProductoSyncMessage
            {
                Tabla = "PrestashopProductos",
                Source = "Nesto",
                Producto = "12345",
                PVP_IVA_Incluido = null
            };

            // Se serializa EXACTAMENTE como en GooglePubSubEventPublisher: el mensaje llega como
            // object y sin JsonSerializerOptions.
            string json = JsonSerializer.Serialize((object)mensaje);

            StringAssert.Contains(json, "\"PVP_IVA_Incluido\":null",
                "La clave tiene que viajar presente con null: si se omite, el módulo entiende " +
                "'no tocar' y deja de actualizar precios sin avisar");
        }

        [TestMethod]
        public void MensajePrestashop_NombreDeLaClave_EsPascalCaseConGuionesBajos()
        {
            var mensaje = new PrestashopProductoSyncMessage { Producto = "12345", PVP_IVA_Incluido = 29.95M };

            string json = JsonSerializer.Serialize((object)mensaje);

            StringAssert.Contains(json, "\"PVP_IVA_Incluido\":29.95",
                "El módulo busca la clave por ese nombre exacto: ni camelCase ni sin guiones bajos");
        }

        [TestMethod]
        public void MensajePrestashop_ConSentinel_ViajaComoMenosUno()
        {
            var mensaje = new PrestashopProductoSyncMessage
            {
                Producto = "12345",
                PVP_IVA_Incluido = Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL
            };

            string json = JsonSerializer.Serialize((object)mensaje);

            StringAssert.Contains(json, "\"PVP_IVA_Incluido\":-1");
        }
    }
}
