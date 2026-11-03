using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#778 (Carlos 03/11/26, «al crear el envío»): el reembolso del reparto propio nace con FechaPagoReembolso = Fecha
    /// para que el correo de cuadre de agencias con la 55500092 (Reembolso &lt;&gt; 0 AND FechaPagoReembolso IS NULL) no lo vea.
    /// </summary>
    [TestClass]
    public class ReembolsoRepartoPropioTests
    {
        private const int PROPIO = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;
        private const int CTT = Constantes.Agencias.AGENCIA_CTT;
        private static readonly DateTime FECHA = new DateTime(2026, 10, 30);

        private static EnviosAgencia Envio(int agencia, decimal reembolso, DateTime? fechaPago = null) => new EnviosAgencia
        {
            Numero = 250376,
            Empresa = "1  ",
            Pedido = 928500,
            Agencia = agencia,
            Fecha = FECHA.AddHours(9.5),
            Reembolso = reembolso,
            FechaPagoReembolso = fechaPago,
            AgenciasTransporte = new AgenciaTransporte { Numero = (short)agencia, CuentaReembolsos = "5700001   " }
        };

        // ---- La regla ----

        [TestMethod]
        public void EsAgenciaRepartoPropio_SoloLa14()
        {
            Assert.IsTrue(ReembolsoRepartoPropio.EsAgenciaRepartoPropio(PROPIO));
            Assert.IsFalse(ReembolsoRepartoPropio.EsAgenciaRepartoPropio(CTT));
            Assert.IsFalse(ReembolsoRepartoPropio.EsAgenciaRepartoPropio(Constantes.Agencias.AGENCIA_GLS));
        }

        [TestMethod]
        public void AplicarFechaPago_RepartoPropioConReembolso_FechaDelEnvioSinHora()
        {
            EnviosAgencia envio = Envio(PROPIO, 453.87M);

            ReembolsoRepartoPropio.AplicarFechaPago(envio);

            Assert.AreEqual(FECHA, envio.FechaPagoReembolso);
            Assert.AreEqual(453.87M, envio.Reembolso, "el importe no se toca");
        }

        [TestMethod]
        public void AplicarFechaPago_RepartoPropioConFechaYa_NoLaCambia()
        {
            EnviosAgencia envio = Envio(PROPIO, 453.87M, new DateTime(2026, 10, 29));

            ReembolsoRepartoPropio.AplicarFechaPago(envio);

            Assert.AreEqual(new DateTime(2026, 10, 29), envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void AplicarFechaPago_RepartoPropioSinReembolso_Null()
        {
            EnviosAgencia envio = Envio(PROPIO, 0M);

            ReembolsoRepartoPropio.AplicarFechaPago(envio);

            Assert.IsNull(envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void AplicarFechaPago_CttConReembolso_Null()
        {
            EnviosAgencia envio = Envio(CTT, 120M);

            ReembolsoRepartoPropio.AplicarFechaPago(envio);

            Assert.IsNull(envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void AplicarFechaPago_DeRepartoPropioACtt_VuelveANull()
        {
            EnviosAgencia envio = Envio(CTT, 120M, FECHA);

            ReembolsoRepartoPropio.AplicarFechaPago(envio, agenciaAnterior: PROPIO);

            Assert.IsNull(envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void AplicarFechaPago_CttYaPagadoPorLaAgencia_NoSeToca()
        {
            EnviosAgencia envio = Envio(CTT, 120M, FECHA);

            ReembolsoRepartoPropio.AplicarFechaPago(envio, agenciaAnterior: CTT);

            Assert.AreEqual(FECHA, envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void AplicarFechaPago_Null_NoRevienta()
        {
            ReembolsoRepartoPropio.AplicarFechaPago(null);
        }

        [TestMethod]
        public void PagadoPorLaAgencia_RepartoPropioConFecha_No()
        {
            // Su fecha es de nacimiento: ni bloquea cambiar el reembolso ni anular, como antes de #778.
            Assert.IsFalse(ReembolsoRepartoPropio.PagadoPorLaAgencia(Envio(PROPIO, 100M, FECHA)));
            Assert.IsTrue(ReembolsoRepartoPropio.PagadoPorLaAgencia(Envio(CTT, 100M, FECHA)));
            Assert.IsFalse(ReembolsoRepartoPropio.PagadoPorLaAgencia(Envio(CTT, 100M)));
            Assert.IsFalse(ReembolsoRepartoPropio.PagadoPorLaAgencia(null));
        }

        // ---- Los caminos ----

        [TestMethod]
        public void ModificarDatos_RepartoPropioDeCeroAReembolso_PoneLaFecha()
        {
            EnviosAgencia envio = Envio(PROPIO, 0M);

            TramitacionEnviosService.AplicarCambios(envio, new ModificarDatosEnvioDTO { Reembolso = 80M, Estado = 1 });

            Assert.AreEqual(80M, envio.Reembolso);
            Assert.AreEqual(FECHA, envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void ModificarDatos_CttDeCeroAReembolso_SinFecha()
        {
            EnviosAgencia envio = Envio(CTT, 0M);

            TramitacionEnviosService.AplicarCambios(envio, new ModificarDatosEnvioDTO { Reembolso = 80M, Estado = 1 });

            Assert.IsNull(envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void DevueltoDelRepartoPropioConFecha_SeLeQuitaElReembolsoComoAntes()
        {
            // Sugerencia 544: un devuelto no cobra; la fecha de nacimiento (#778) no cuenta como «pagado por la agencia».
            EnviosAgencia envio = Envio(PROPIO, 100M, FECHA);
            envio.Estado = Constantes.Agencias.ESTADO_DEVUELTO;

            Assert.IsTrue(TramitacionEnviosService.DebeQuitarseReembolsoPorDevuelto(envio));
        }

        [TestMethod]
        public void NotaDeEntrega_SinReembolso_SinFecha()
        {
            EnviosAgencia envio = ServicioParadasNotaEntrega.ConstruirEnvio(
                new CabPedidoVta { Empresa = "1  ", Número = 930123, Nº_Cliente = "15191     ", Contacto = "0  ", Vendedor = "NV " },
                new DestinoNotaEntrega { Cliente = "15191", Contacto = "0", Nombre = "CLIENTE", CodPostal = "28001" },
                "1", 1, "Alfredo", FECHA);

            Assert.AreEqual(PROPIO, envio.Agencia);
            Assert.IsNull(envio.FechaPagoReembolso);
        }

        // ---- «Contabilizar reembolsos» no los toca ----

        [TestMethod]
        public void ContabilizarReembolsos_RepartoPropio_NoSePuedePagar()
        {
            // Con la fecha que trae al nacer, y también sin ella (los de antes del script de datos).
            var envios = new List<EnviosAgencia> { Envio(PROPIO, 453.87M, FECHA) };
            envios[0].Numero = 1;
            var sinFecha = Envio(PROPIO, 206.81M);
            sinFecha.Numero = 2;
            envios.Add(sinFecha);

            string error = TramitacionEnviosService.ErrorEnviosAPagar(envios, new List<int> { 1, 2 }, "1");

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "El envío 2 (pedido 928500) es del reparto propio");
            StringAssert.Contains(error, "El envío 1 (pedido 928500) es del reparto propio");
        }

        [TestMethod]
        public void ContabilizarReembolsos_Ctt_SiSePuedePagar()
        {
            string error = TramitacionEnviosService.ErrorEnviosAPagar(new List<EnviosAgencia> { Envio(CTT, 120M) }, new List<int> { 250376 }, "1");

            Assert.IsNull(error);
        }
    }
}
