using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Novedades 1162 (envío 250772, pedido 930294): la regla de si un envío del reparto propio se puede anular (volver a etiqueta
    /// pendiente). Solo si no ha empezado a repartirse.
    /// </summary>
    [TestClass]
    public class AnulacionEnvioRepartoPropioTests
    {
        private static EnviosAgencia Envio(short estado = Constantes.Agencias.ESTADO_TRAMITADO) => new EnviosAgencia
        {
            Numero = 250772,
            Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO,
            Pedido = 930294,
            Estado = estado,
            CodigoBarras = "RP250772",
            Reembolso = 53m,
            Fecha = new DateTime(2026, 11, 10),
            FechaEntrega = new DateTime(2026, 11, 11),
            FechaPagoReembolso = new DateTime(2026, 11, 11)
        };

        [TestMethod]
        public void Validar_TramitadoSinNada_SePuede()
        {
            Assert.IsNull(AnulacionEnvioRepartoPropio.Validar(Envio(), new DatosAnulacionEnvioRepartoPropio()));
            Assert.IsNull(AnulacionEnvioRepartoPropio.Validar(Envio((short)Constantes.Agencias.ESTADO_EN_CURSO), null));
        }

        [TestMethod]
        public void Aplica_SoloAlRepartoPropio()
        {
            Assert.IsTrue(AnulacionEnvioRepartoPropio.Aplica(Envio()));
            EnviosAgencia ctt = Envio();
            ctt.Agencia = Constantes.Agencias.AGENCIA_CTT;
            Assert.IsFalse(AnulacionEnvioRepartoPropio.Aplica(ctt));
            Assert.IsFalse(AnulacionEnvioRepartoPropio.Aplica(null));
        }

        private static string Motivo(Action<DatosAnulacionEnvioRepartoPropio> preparar)
        {
            var datos = new DatosAnulacionEnvioRepartoPropio();
            preparar(datos);
            return AnulacionEnvioRepartoPropio.Validar(Envio(), datos);
        }

        [TestMethod]
        public void Validar_Cargado_NoSePuede()
            => StringAssert.StartsWith(Motivo(d => d.Cargado = true), "El envío 250772 ya está cargado en la furgoneta: no se puede anular.");

        [TestMethod]
        public void Validar_MovimientosFurgoneta_NoSePuede()
            => StringAssert.Contains(Motivo(d => d.MovimientosFurgoneta = true), "ya ha pasado por la furgoneta");

        [TestMethod]
        public void Validar_CobroTarjeta_NoSePuede()
            => StringAssert.Contains(Motivo(d => d.CobroTarjeta = true), "cobro con tarjeta o un enlace de pago");

        [TestMethod]
        public void Validar_AutorizacionReembolso_NoSePuede()
            => StringAssert.Contains(Motivo(d => d.AutorizacionReembolso = true), "autorización de reembolso");

        [TestMethod]
        public void Validar_PruebaEntrega_NoSePuede()
            => StringAssert.Contains(Motivo(d => d.PruebaEntrega = true), "entrega o una incidencia del repartidor");

        [TestMethod]
        public void Validar_ReembolsoAlEntregar_NoSePuede()
            => StringAssert.Contains(Motivo(d => d.ReembolsoAlEntregar = true), "cobro del reembolso al entregar");

        [TestMethod]
        public void Validar_Reenvio_NoSePuede()
            => StringAssert.Contains(Motivo(d => d.Reenvio = true), "tiene un reenvío");

        [TestMethod]
        public void Validar_ParadaDeNota_NoSePuedeYDiceDeQueNota()
        {
            StringAssert.Contains(Motivo(d => d.TipoNota = TiposNotaEntrega.ENTREGA), "la parada de la nota de entrega del pedido 930294");
            StringAssert.Contains(Motivo(d => d.TipoNota = TiposNotaEntrega.RECOGIDA), "la nota de recogida");
            StringAssert.Contains(Motivo(d => d.TipoNota = TiposNotaEntrega.COBRO), "la nota de cobro");
        }

        [TestMethod]
        public void Validar_EntregadoIncidentadoDevueltoOCerrado_NoSePuede()
        {
            var esperado = new Dictionary<short, string>
            {
                { Constantes.Agencias.ESTADO_ENTREGADO, "ya está entregado" },
                { Constantes.Agencias.ESTADO_INCIDENTADO, "tiene una incidencia" },
                { Constantes.Agencias.ESTADO_DEVUELTO, "ya está devuelto" },
                { Constantes.Agencias.ESTADO_CERRADO_A_MANO, "ya está cerrado" },
                { 6, "estado 6" }
            };
            foreach (KeyValuePair<short, string> caso in esperado)
            {
                string motivo = AnulacionEnvioRepartoPropio.Validar(Envio(caso.Key), new DatosAnulacionEnvioRepartoPropio());
                Assert.IsNotNull(motivo, $"estado {caso.Key}");
                StringAssert.Contains(motivo, caso.Value);
                StringAssert.Contains(motivo, "no se puede anular");
            }
        }

        [TestMethod]
        public void Aplicar_DejaEtiquetaPendienteSinAlbaranNiFechaPagoYConSuHistoria()
        {
            EnviosAgencia envio = Envio();
            var ahora = new DateTime(2026, 11, 10, 12, 30, 0);

            EnvioHistoria fila = AnulacionEnvioRepartoPropio.Aplicar(envio, "NUEVAVISION\\Alfredo", ahora);

            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado);
            Assert.AreEqual(string.Empty, envio.CodigoBarras);
            Assert.IsNull(envio.FechaPagoReembolso);
            Assert.AreEqual(53m, envio.Reembolso, "el importe se corrige en Pendientes");
            Assert.AreEqual(250772, fila.NumeroEnvio);
            Assert.AreEqual("Estado", fila.Campo);
            Assert.AreEqual("1", fila.ValorAnterior);
            Assert.AreEqual("Anulado el envío tramitado del reparto propio: vuelve a etiqueta pendiente", fila.Observaciones);
            Assert.AreEqual("NUEVAVISION\\Alfredo", fila.Usuario);
            Assert.AreEqual(ahora, fila.FechaModificacion);
        }

        [TestMethod]
        public void Aplicar_LaFilaDeHistoriaNoEsUnCambioManualDelReembolso()
        {
            // La marca de #785 es solo la del reembolso cambiado en Pendientes: anular no la deja.
            EnvioHistoria fila = AnulacionEnvioRepartoPropio.Aplicar(Envio(), "NUEVAVISION\\Alfredo", DateTime.Now);

            Assert.IsFalse(ReembolsoEtiquetaPendiente.HayCambioManual(new[] { fila }));
        }

        [TestMethod]
        public void VueltaARegistrar_ConReembolso_RecuperaLaFechaDePago()
        {
            // #778: al registrarlo otra vez (PUT a En curso) AplicarFechaPago la vuelve a poner con el día de reparto.
            EnviosAgencia envio = Envio();
            AnulacionEnvioRepartoPropio.Aplicar(envio, "Alfredo", DateTime.Now);
            envio.Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO;

            ReembolsoRepartoPropio.AplicarFechaPago(envio, Constantes.Agencias.AGENCIA_REPARTO_PROPIO);

            Assert.AreEqual(new DateTime(2026, 11, 11), envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void VueltaARegistrar_CorregidoACero_SinFechaDePago()
        {
            EnviosAgencia envio = Envio();
            AnulacionEnvioRepartoPropio.Aplicar(envio, "Alfredo", DateTime.Now);
            envio.Reembolso = 0m;

            ReembolsoRepartoPropio.AplicarFechaPago(envio, Constantes.Agencias.AGENCIA_REPARTO_PROPIO);

            Assert.IsNull(envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void VueltaARegistrar_CorregidoACeroAMano_ElRegistroLoRespeta()
        {
            // #785: el pendiente que se cambió a 0 en Pendientes no se recalcula al registrarlo.
            ResultadoReembolsoPendiente decision = ReembolsoEtiquetaPendiente.Decidir(250772, 0m, 53m, cambiadoAMano: true, facturadoSinCobrar: 0m);

            Assert.AreEqual(DecisionReembolsoPendiente.RespetadoCambioManual, decision.Decision);
            Assert.AreEqual(0m, decision.Reembolso);
        }
    }
}
