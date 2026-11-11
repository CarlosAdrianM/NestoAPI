using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using System;
using System.Collections.Generic;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Feria online 12-16/11/26 (Carlos 11/11/26): aviso de «pago al contado» al crear un pedido con productos de una campaña.
    /// </summary>
    [TestClass]
    public class AvisoCondicionesCampanaTests
    {
        private static readonly DateTime DIA_FERIA = new DateTime(2026, 11, 14, 17, 30, 0);

        private static List<ProductoCampanaContado> Feria(string textoAviso = null) => new List<ProductoCampanaContado>
        {
            new ProductoCampanaContado { Campana = "FERIA-ONLINE-1126 ", Empresa = "1  ", Producto = "45383          ",
                FechaDesde = new DateTime(2026, 11, 12), FechaHasta = new DateTime(2026, 11, 16), TextoAviso = textoAviso },
            new ProductoCampanaContado { Campana = "FERIA-ONLINE-1126", Empresa = "1", Producto = "23532",
                FechaDesde = new DateTime(2026, 11, 12), FechaHasta = new DateTime(2026, 11, 16) }
        };

        private static AvisoCondicionesCampanaPeticion Pedido(string formaPago, string plazos, params string[] productos) =>
            new AvisoCondicionesCampanaPeticion { Empresa = "1", FormaPago = formaPago, PlazosPago = plazos, Productos = new List<string>(productos) };

        [TestMethod]
        public void Decidir_ProductoDeCampanaVigenteYAPlazo_Avisa()
        {
            AvisoCondicionesCampanaRespuesta respuesta = AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA,
                Pedido("RCB", "1/30", "45383 ", "99999"));

            Assert.IsTrue(respuesta.Avisar);
            Assert.AreEqual(AvisoCondicionesCampana.MENSAJE_POR_DEFECTO, respuesta.Mensaje);
            Assert.AreEqual("CONTADO", respuesta.PlazosPagoPropuestos);
            Assert.AreEqual("FERIA-ONLINE-1126", respuesta.Campana);
            CollectionAssert.AreEqual(new[] { "45383" }, respuesta.Productos);
        }

        [TestMethod]
        public void Decidir_MezclaDeProductos_DevuelveSoloLosDeCampanaSinRepetir()
        {
            AvisoCondicionesCampanaRespuesta respuesta = AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA,
                Pedido("TRN", "1/30", "11111", "23532", "45383", "23532"));

            Assert.IsTrue(respuesta.Avisar);
            CollectionAssert.AreEqual(new[] { "23532", "45383" }, respuesta.Productos);
            Assert.AreEqual("FERIA-ONLINE-1126", respuesta.Campana);
        }

        [TestMethod]
        public void Decidir_SinProductosDeCampana_NoAvisa()
        {
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("RCB", "1/30", "11111", "22222")).Avisar);
        }

        [TestMethod]
        public void Decidir_SinProductosOSinPeticion_NoAvisa()
        {
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("RCB", "1/30")).Avisar);
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, null).Avisar);
        }

        [TestMethod]
        public void Decidir_VigenciaPorDiaInclusive()
        {
            Assert.IsTrue(AvisoCondicionesCampana.Decidir(Feria(), new DateTime(2026, 11, 12, 0, 1, 0), Pedido("RCB", "1/30", "45383")).Avisar);
            Assert.IsTrue(AvisoCondicionesCampana.Decidir(Feria(), new DateTime(2026, 11, 16, 23, 59, 0), Pedido("RCB", "1/30", "45383")).Avisar);
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), new DateTime(2026, 11, 11, 23, 59, 0), Pedido("RCB", "1/30", "45383")).Avisar);
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), new DateTime(2026, 11, 17), Pedido("RCB", "1/30", "45383")).Avisar);
        }

        [TestMethod]
        public void Decidir_YaAlContado_NoAvisa()
        {
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("EFC", "CONTADO  ", "45383")).Avisar);
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("RCB", "CR", "45383")).Avisar);
        }

        [TestMethod]
        public void Decidir_AplazameOPrepago_NoAvisa()
        {
            // Aplázame (tienda y app) entra como TRN / PRE.
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("TRN", "PRE", "45383")).Avisar);
            AvisoCondicionesCampanaPeticion conPrepago = Pedido("TRN", "1/30", "45383");
            conPrepago.TienePrepago = true;
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, conPrepago).Avisar);
        }

        [TestMethod]
        public void Decidir_Tarjeta_NoAvisa()
        {
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("TAR ", "1/30", "45383")).Avisar);
        }

        [TestMethod]
        public void Decidir_OtraEmpresa_NoAvisa()
        {
            AvisoCondicionesCampanaPeticion pedido = Pedido("RCB", "1/30", "45383");
            pedido.Empresa = "3";
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, pedido).Avisar);
        }

        [TestMethod]
        public void Decidir_SinEmpresa_UsaLaPorDefecto()
        {
            AvisoCondicionesCampanaPeticion pedido = Pedido("RCB", "1/30", "45383");
            pedido.Empresa = null;
            Assert.IsTrue(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, pedido).Avisar);
        }

        [TestMethod]
        public void Decidir_ConTextoPropio_UsaEseTexto()
        {
            AvisoCondicionesCampanaRespuesta respuesta = AvisoCondicionesCampana.Decidir(Feria("Texto de la campaña."), DIA_FERIA,
                Pedido("RCB", "1/30", "45383"));
            Assert.AreEqual("Texto de la campaña.", respuesta.Mensaje);
        }

        [TestMethod]
        public void Decidir_SinFilas_NoAvisa()
        {
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(null, DIA_FERIA, Pedido("RCB", "1/30", "45383")).Avisar);
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(new List<ProductoCampanaContado>(), DIA_FERIA, Pedido("RCB", "1/30", "45383")).Avisar);
        }

        [TestMethod]
        public void CampanasCondicionContado_SiFallaLaLectura_NoLanzaYQuedaVacio()
        {
            try
            {
                CampanasCondicionContado.Configurar(() => throw new InvalidOperationException("sin BD"));
                Assert.AreEqual(0, CampanasCondicionContado.Actuales.Count);
            }
            finally
            {
                CampanasCondicionContado.Reiniciar();
            }
        }

        [TestMethod]
        public void Controlador_LeeLaCacheYDecide()
        {
            try
            {
                CampanasCondicionContado.ConfigurarPara(Feria().ToArray());
                var controlador = new PedidosVentaCondicionesCampanaController(() => DIA_FERIA);

                var resultado = controlador.PostAvisoCondicionesCampana(Pedido("RCB", "1/30", "45383")) as OkNegotiatedContentResult<AvisoCondicionesCampanaRespuesta>;

                Assert.IsNotNull(resultado);
                Assert.IsTrue(resultado.Content.Avisar);
            }
            finally
            {
                CampanasCondicionContado.Reiniciar();
            }
        }

        // Carlos 11/11/26: el pronto pago también es contado.
        private static List<PlazoProntoPago> PlazosConPP() => new List<PlazoProntoPago>
        {
            new PlazoProntoPago { Empresa = "1  ", Plazos = "PP  ", DtoProntoPago = 0.03M },
            new PlazoProntoPago { Empresa = "1", Plazos = "PP2", DtoProntoPago = 0.02M },
            new PlazoProntoPago { Empresa = "3", Plazos = "PP9", DtoProntoPago = 0.05M }
        };

        [TestMethod]
        public void Decidir_PlazosConProntoPago_NoAvisa()
        {
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("RCB", "PP", "45383"), PlazosConPP()).Avisar);
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("EFC", "pp2 ", "45383"), PlazosConPP()).Avisar);
        }

        [TestMethod]
        public void Decidir_PlazosConProntoPagoDeOtraEmpresa_Avisa()
        {
            Assert.IsTrue(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, Pedido("RCB", "PP9", "45383"), PlazosConPP()).Avisar);
        }

        [TestMethod]
        public void Decidir_DescuentoPPPuestoEnElPedido_NoAvisa()
        {
            AvisoCondicionesCampanaPeticion pedido = Pedido("RCB", "1/30", "45383");
            pedido.DescuentoPP = 0.03M;
            Assert.IsFalse(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, pedido).Avisar);
        }

        [TestMethod]
        public void Decidir_SinProntoPagoYA30Dias_AvisaComoHoy()
        {
            AvisoCondicionesCampanaPeticion pedido = Pedido("RCB", "1/30", "45383");
            pedido.DescuentoPP = 0;
            Assert.IsTrue(AvisoCondicionesCampana.Decidir(Feria(), DIA_FERIA, pedido, PlazosConPP()).Avisar);
        }

        [TestMethod]
        public void PlazosProntoPago_SiFallaLaLectura_NoLanzaYQuedaVacio()
        {
            try
            {
                PlazosProntoPago.Configurar(() => throw new InvalidOperationException("sin BD"));
                Assert.AreEqual(0, PlazosProntoPago.Actuales.Count);
            }
            finally
            {
                PlazosProntoPago.Reiniciar();
            }
        }

        [TestMethod]
        public void Controlador_PlazosConProntoPagoEnLaTabla_NoAvisa()
        {
            try
            {
                CampanasCondicionContado.ConfigurarPara(Feria().ToArray());
                PlazosProntoPago.ConfigurarPara(PlazosConPP().ToArray());
                var controlador = new PedidosVentaCondicionesCampanaController(() => DIA_FERIA);

                var resultado = controlador.PostAvisoCondicionesCampana(Pedido("RCB", "PP", "45383")) as OkNegotiatedContentResult<AvisoCondicionesCampanaRespuesta>;

                Assert.IsNotNull(resultado);
                Assert.IsFalse(resultado.Content.Avisar);
            }
            finally
            {
                CampanasCondicionContado.Reiniciar();
                PlazosProntoPago.Reiniciar();
            }
        }

        [TestMethod]
        public void Controlador_SinTabla_NoAvisa()
        {
            CampanasCondicionContado.Reiniciar();
            var controlador = new PedidosVentaCondicionesCampanaController(() => DIA_FERIA);

            var resultado = controlador.PostAvisoCondicionesCampana(Pedido("RCB", "1/30", "45383")) as OkNegotiatedContentResult<AvisoCondicionesCampanaRespuesta>;

            Assert.IsNotNull(resultado);
            Assert.IsFalse(resultado.Content.Avisar);
        }
    }
}
