using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#785 (03/11/26, pedido 929609 / envío 250432): una etiqueta pendiente creada antes del picking guarda reembolso 0 y
    /// al registrarla se reutilizaba tal cual. El núcleo de la decisión y el cálculo con las líneas reales del 929609.
    /// </summary>
    [TestClass]
    public class ReembolsoEtiquetaPendienteTests
    {
        private const int ENVIO = 250432;
        private const int PEDIDO = 929609;

        // ---------------------------------------------------------------- el cálculo del 929609

        internal static CabPedidoVta Pedido929609() => new CabPedidoVta
        {
            Empresa = "1  ",
            Número = PEDIDO,
            Nº_Cliente = "19682     ",
            Contacto = "0  ",
            Forma_Pago = "EFC",
            PlazosPago = "CONTADO   ",
            CCC = null,
            IVA = "G21",
            NotaEntrega = false
        };

        /// <summary>Las líneas del 929609 tal como estaban con el picking 99982 (en curso), con la comisión contra reembolso de 3 €.</summary>
        internal static List<LinPedidoVta> Lineas929609(short estado = Constantes.EstadosLineaVenta.EN_CURSO, int picking = 99982)
        {
            decimal[] totales = { 54.3774m, 2.5410m, 36.7840m, 0m, 36.8324m, 3.6300m };
            decimal[] bases = { 44.94m, 2.10m, 30.40m, 0m, 30.44m, 3.00m };
            return totales.Select((t, i) => new LinPedidoVta
            {
                Empresa = "1  ",
                Número = PEDIDO,
                Estado = estado,
                Picking = picking,
                Total = t,
                Base_Imponible = bases[i]
            }).ToList();
        }

        private static IServicioPedidosVenta ServicioSinEfectos()
        {
            IServicioPedidosVenta servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.CargarEfectosPedido(A<string>._, A<int>._)).Returns(new List<EfectoPedidoVenta>());
            return servicio;
        }

        [TestMethod]
        public void Calculo_929609_ConPicking_Da13416ConLaComision()
        {
            decimal reembolso = GestorEnviosAgencia.ImporteReembolso(Pedido929609(), Lineas929609(), ServicioSinEfectos());

            Assert.AreEqual(134.16m, reembolso);
        }

        [TestMethod]
        public void Calculo_929609_SinPicking_Da0()
        {
            // Así se creó la etiqueta pendiente: esperando la reposición de Reina, sin líneas con picking.
            decimal reembolso = GestorEnviosAgencia.ImporteReembolso(Pedido929609(),
                Lineas929609(Constantes.EstadosLineaVenta.PENDIENTE, 0), ServicioSinEfectos());

            Assert.AreEqual(0m, reembolso);
        }

        [TestMethod]
        public void PendientePicking0_DespuesPicking_AlRegistrarSale13416()
        {
            // Regresión 929609: pendiente a 0 → llega el picking → al registrar, 134,16.
            decimal calculado = GestorEnviosAgencia.ImporteReembolso(Pedido929609(), Lineas929609(), ServicioSinEfectos());

            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, 0m, calculado, false, 0m);

            Assert.AreEqual(DecisionReembolsoPendiente.Recalculado, r.Decision);
            Assert.AreEqual(134.16m, r.Reembolso);
            StringAssert.Contains(r.Aviso, "134,16");
            StringAssert.Contains(r.Aviso, ENVIO.ToString());
        }

        // ---------------------------------------------------------------- facturado sin cobrar

        [TestMethod]
        public void FacturadoSinCobrar_ContraReembolsoFacturado_LoPendienteMenosLoQueLlevanLosEnvios()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            A.CallTo(() => servicio.FacturasDelPedido("1", PEDIDO)).Returns(new List<string> { "NV2617620" });
            A.CallTo(() => servicio.PendienteEfectivoDeFacturas("1", "19682", A<IEnumerable<string>>._)).Returns(134.16m);
            A.CallTo(() => servicio.ReembolsosCobradosDelPedido("1", PEDIDO, A<DateTime>._)).Returns(34.16m);

            Assert.AreEqual(100m, GestorEnviosAgencia.FacturadoContraReembolsoSinCobrar(Pedido929609(), servicio));
        }

        [TestMethod]
        public void FacturadoSinCobrar_NoContraReembolso_0()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            A.CallTo(() => servicio.FacturasDelPedido(A<string>._, A<int>._)).Returns(new List<string> { "NV2617620" });
            A.CallTo(() => servicio.PendienteEfectivoDeFacturas(A<string>._, A<string>._, A<IEnumerable<string>>._)).Returns(134.16m);
            CabPedidoVta pedido = Pedido929609();
            pedido.CCC = "1";

            Assert.AreEqual(0m, GestorEnviosAgencia.FacturadoContraReembolsoSinCobrar(pedido, servicio));
        }

        [TestMethod]
        public void FacturadoSinCobrar_SinFacturar_0()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            A.CallTo(() => servicio.FacturasDelPedido(A<string>._, A<int>._)).Returns(new List<string>());

            Assert.AreEqual(0m, GestorEnviosAgencia.FacturadoContraReembolsoSinCobrar(Pedido929609(), servicio));
            A.CallTo(() => servicio.PendienteEfectivoDeFacturas(A<string>._, A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- la decisión

        [TestMethod]
        public void Decidir_CambiadoAMano_SeRespetaYSeAvisa()
        {
            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, 0m, 134.16m, true, 0m);

            Assert.AreEqual(DecisionReembolsoPendiente.RespetadoCambioManual, r.Decision);
            Assert.AreEqual(0m, r.Reembolso);
            StringAssert.Contains(r.Aviso, "cambió a mano");
        }

        [TestMethod]
        public void Decidir_NoContraReembolso_SigueA0SinAviso()
        {
            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, 0m, 0m, false, 0m);

            Assert.AreEqual(DecisionReembolsoPendiente.SinCambios, r.Decision);
            Assert.AreEqual(0m, r.Reembolso);
            Assert.IsNull(r.Aviso);
        }

        [TestMethod]
        public void Decidir_PendienteConImporte_NoSeToca()
        {
            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, 80m, 134.16m, false, 0m);

            Assert.AreEqual(DecisionReembolsoPendiente.SinCambios, r.Decision);
            Assert.AreEqual(80m, r.Reembolso);
            Assert.IsNull(r.Aviso);
        }

        [TestMethod]
        public void Decidir_NoCobrar_NoSeRecalculaNiSeBloquea()
        {
            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, Constantes.Agencias.REEMBOLSO_NO_COBRAR, 134.16m, false, 134.16m);

            Assert.AreEqual(DecisionReembolsoPendiente.SinCambios, r.Decision);
            Assert.AreEqual(0m, r.Reembolso);
        }

        [TestMethod]
        public void Decidir_ContraReembolsoACeroYFacturaPendiente_Bloquea()
        {
            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, 0m, 0m, false, 134.16m);

            Assert.AreEqual(DecisionReembolsoPendiente.Bloqueado, r.Decision);
            StringAssert.StartsWith(r.Aviso, ReembolsoEtiquetaPendiente.MENSAJE_BLOQUEO);
            StringAssert.Contains(r.Aviso, "134,16");
        }

        [TestMethod]
        public void Decidir_CambiadoAManoYFacturaPendiente_NoBloqueaSoloAvisa()
        {
            ResultadoReembolsoPendiente r = ReembolsoEtiquetaPendiente.Decidir(ENVIO, 0m, 0m, true, 134.16m);

            Assert.AreEqual(DecisionReembolsoPendiente.RespetadoCambioManual, r.Decision);
        }

        // ---------------------------------------------------------------- la marca «cambiado a mano»

        [TestMethod]
        public void HayCambioManual_FilaDeUnUsuario_Si()
        {
            var historia = new List<EnvioHistoria>
            {
                ReembolsoEtiquetaPendiente.FilaCambioManual(ENVIO, 134.16m, 0m, "NUEVAVISION\\Aida", DateTime.Now)
            };

            Assert.IsTrue(ReembolsoEtiquetaPendiente.HayCambioManual(historia));
        }

        [TestMethod]
        public void HayCambioManual_SoloElRecalculoOElSistemaOOtroCampo_No()
        {
            var historia = new List<EnvioHistoria>
            {
                ReembolsoEtiquetaPendiente.FilaRecalculado(ENVIO, 0m, "NUEVAVISION\\Andre", DateTime.Now),
                new EnvioHistoria { NumeroEnvio = ENVIO, Campo = "Reembolso", Usuario = "NestoAPI", Observaciones = "x" },
                new EnvioHistoria { NumeroEnvio = ENVIO, Campo = "Retorno", Usuario = "NUEVAVISION\\Andre", Observaciones = "x" }
            };

            Assert.IsFalse(ReembolsoEtiquetaPendiente.HayCambioManual(historia));
        }

        [TestMethod]
        public void FilaRecalculado_CampoValorAnteriorYObservaciones()
        {
            EnvioHistoria fila = ReembolsoEtiquetaPendiente.FilaRecalculado(ENVIO, 0m, "NUEVAVISION\\Andre", new DateTime(2026, 11, 3));

            Assert.AreEqual(ENVIO, fila.NumeroEnvio);
            Assert.AreEqual("Reembolso", fila.Campo);
            Assert.AreEqual("0.00", fila.ValorAnterior);
            Assert.AreEqual("Recalculado al registrar la etiqueta pendiente: el pedido ya tiene picking", fila.Observaciones);
            Assert.AreEqual("NUEVAVISION\\Andre", fila.Usuario);
        }
    }
}
