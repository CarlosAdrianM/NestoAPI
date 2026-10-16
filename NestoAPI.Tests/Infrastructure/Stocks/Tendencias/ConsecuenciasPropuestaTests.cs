using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>
    /// Sugerencia 644 (Manuel): la frase de qué pasa al aplicar cada acción. Tiene que decir lo que hace de verdad
    /// ServicioDecisionesPropuestasStock al «Aplicar»; si se cambia lo que escribe una acción, hay que cambiar su frase.
    /// </summary>
    [TestClass]
    public class ConsecuenciasPropuestaTests
    {
        private static PropuestaStockConDecision Propuesta(string accion, int? maximoActual = 59, int? minimoActual = 39,
            int? maximoPropuesto = 88, int? minimoPropuesto = 68, int pedidoCompraAbierto = 0)
            => new PropuestaStockConDecision
            {
                Producto = "17404",
                Almacen = "ALG",
                Accion = accion,
                StockMaximoActual = maximoActual,
                StockMinimoActual = minimoActual,
                StockMaximoPropuesto = maximoPropuesto,
                StockMinimoPropuesto = minimoPropuesto,
                PedidoCompraAbierto = pedidoCompraAbierto
            };

        [TestMethod]
        public void Ajustar_DiceElMaximoYElMinimoDeAntesYDeDespues()
        {
            Assert.AreEqual("El máximo de ALG pasa de 59 a 88 y el mínimo de 39 a 68.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR)));
        }

        [TestMethod]
        public void Ajustar_SoloCambiaElMaximo_LoDice()
        {
            Assert.AreEqual("El máximo de ALG pasa de 59 a 88; el mínimo se queda en 39.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR, minimoPropuesto: 39)));
        }

        [TestMethod]
        public void Ajustar_SoloCambiaElMinimo_LoDice()
        {
            Assert.AreEqual("El mínimo de ALG pasa de 39 a 68; el máximo se queda en 59.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR, maximoPropuesto: 59)));
        }

        [TestMethod]
        public void Ajustar_SinControlDeStock_SeCrea()
        {
            // Como el servicio: sin control de stock en el almacén, «Aplicar» lo crea.
            Assert.AreEqual("Se crea el control de stock de ALG con máximo 88 y mínimo 68.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR, maximoActual: null, minimoActual: null)));
        }

        [TestMethod]
        public void Ajustar_LosMismosValores_NoCambiaNada()
        {
            Assert.AreEqual("El máximo y el mínimo de ALG ya son 59/39: no cambia nada.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR, maximoPropuesto: 59, minimoPropuesto: 39)));
        }

        [TestMethod]
        public void Ajustar_SinPropuesta_HayQueIndicarlos()
        {
            StringAssert.StartsWith(ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR, maximoPropuesto: null, minimoPropuesto: null)),
                "La propuesta no trae máximo y mínimo");
        }

        [TestMethod]
        public void AccionesDeLosPropios_EscribenElMaximoYElMinimoComoUnAjuste()
        {
            // NestoAPI#655: «Ajustar el siguiente lote» y «Caduca antes de venderse» se aplican como un «Ajustar máx/mín».
            string esperado = "El máximo de ALG pasa de 59 a 88 y el mínimo de 39 a 68.";
            Assert.AreEqual(esperado, ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.AJUSTAR_LOTE)));
            Assert.AreEqual(esperado, ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.CADUCA)));
        }

        [TestMethod]
        public void PotenciarVentas_DiceElAjusteYLaAlternativaDeObjetivoYFechaDeRevision()
        {
            string texto = ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.POTENCIAR));

            StringAssert.StartsWith(texto, "El máximo de ALG pasa de 59 a 88 y el mínimo de 39 a 68.");
            StringAssert.Contains(texto, "«Potenciar» fija un objetivo de venta (unidades al mes) y una fecha de revisión (por defecto, dentro de 6 meses)");
        }

        [TestMethod]
        public void PasarAEstado1_TodosLosAlmacenesA0YEstado1()
        {
            Assert.AreEqual("El producto pasa a estado 1 (sobre pedido) y el máximo y el mínimo de todos los almacenes quedan a 0 (en ALG, ahora 59/39).",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.ESTADO_1, maximoPropuesto: 0, minimoPropuesto: 0)));
        }

        [TestMethod]
        public void PasarAEstado1_SinMaximos_NoDiceLosDeAhora()
        {
            Assert.AreEqual("El producto pasa a estado 1 (sobre pedido) y el máximo y el mínimo de todos los almacenes quedan a 0.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.ESTADO_1, maximoActual: 0, minimoActual: 0)));
        }

        [TestMethod]
        public void Liquidar_MaximoYMinimoA0YElOutletAMano()
        {
            // Lo que hace el servicio: solo pone a 0 el máximo y el mínimo del almacén; no cambia el estado ni mete en el Outlet.
            string texto = ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.LIQUIDAR, maximoPropuesto: 0, minimoPropuesto: 0));

            StringAssert.StartsWith(texto, "El máximo y el mínimo de ALG pasan de 59/39 a 0 (deja de reponerse). El producto sigue en el estado que tiene.");
            StringAssert.Contains(texto, "lo hace usted a mano");
            StringAssert.Contains(texto, "ponga el producto en estado 4");
        }

        [TestMethod]
        public void Liquidar_YaA0_LoDice()
        {
            StringAssert.StartsWith(ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.LIQUIDAR, maximoActual: 0, minimoActual: 0)),
                "El máximo y el mínimo de ALG ya están a 0.");
        }

        [TestMethod]
        public void Liquidar_SinControlDeStock_NoSeCambiaNadaEnElStock()
        {
            StringAssert.StartsWith(ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.LIQUIDAR, maximoActual: null, minimoActual: null)),
                "No hay control de stock en ALG: no se cambia nada en el stock.");
        }

        [TestMethod]
        public void PararCompras_SoloQuedaAnotadoConLasUnidadesPedidas()
        {
            Assert.AreEqual("Solo queda anotado: no se cambia nada automáticamente, hay que hacerlo a mano. " +
                "Hay 120 uds en pedidos de compra abiertos: hay que reducirlos o anularlos usted en el pedido.",
                ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.PARAR_COMPRAS, pedidoCompraAbierto: 120)));
        }

        [TestMethod]
        public void PararCompras_SinUnidadesPedidas_SoloQuedaAnotado()
        {
            StringAssert.StartsWith(ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.PARAR_COMPRAS)),
                "Solo queda anotado: no se cambia nada automáticamente, hay que hacerlo a mano.");
        }

        [TestMethod]
        public void MantenerYNada_NoCambiaNada()
        {
            Assert.AreEqual("No cambia nada.", ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.MANTENER)));
            Assert.AreEqual("No cambia nada.", ConsecuenciasPropuesta.Texto(Propuesta(AccionesPropuesta.NADA)));
        }

        [TestMethod]
        public void AccionDesconocida_SoloQuedaAnotado()
        {
            Assert.AreEqual("«Revisar»: no se cambia nada automáticamente, hay que hacerlo a mano.",
                ConsecuenciasPropuesta.Texto(Propuesta("Revisar")));
        }

        [TestMethod]
        public void SinAccion_Null()
        {
            Assert.IsNull(ConsecuenciasPropuesta.Texto(Propuesta(null)));
            Assert.IsNull(ConsecuenciasPropuesta.Texto(null));
        }

        [TestMethod]
        public void LaPropiedadConsecuencia_EsLaFrase()
        {
            PropuestaStockConDecision propuesta = Propuesta(AccionesPropuesta.AJUSTAR);

            Assert.AreEqual(ConsecuenciasPropuesta.Texto(propuesta), propuesta.Consecuencia);
        }

        [TestMethod]
        public void Filtro_TextoConComodines_SeEscapanParaElLike()
        {
            FiltroPropuestasStock filtro = new FiltroPropuestasStock(texto: " 50%_[x] ");

            Assert.AreEqual("50%_[x]", filtro.Texto);
            Assert.AreEqual("%50\\%\\_\\[x]%", filtro.PatronTexto);
        }

        [TestMethod]
        public void Filtro_TodoVacio_EstaVacio()
        {
            FiltroPropuestasStock filtro = new FiltroPropuestasStock(" ", "", null, "  ");

            Assert.IsTrue(filtro.EstaVacio);
            Assert.IsNull(filtro.PatronTexto);
        }
    }
}
