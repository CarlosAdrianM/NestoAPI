using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#436: la política de cobro de la app. Es más restrictiva que la general:
    /// por defecto tarjeta al contado, crédito solo si su ficha lo permite y, con deuda, solo TAR
    /// (la general deja EFC y TRN y recomienda efectivo, que en una app no tiene sentido).
    /// </summary>
    [TestClass]
    public class PoliticaPagoCanalTests
    {
        private const string EFECTIVO = Constantes.FormasPago.EFECTIVO;
        private const string TRANSFERENCIA = Constantes.FormasPago.TRANSFERENCIA;
        private const string TARJETA = Constantes.FormasPago.TARJETA;
        private const string RECIBO = Constantes.FormasPago.RECIBO_BANCARIO;

        private static CondicionesPagoResponse CondicionesGenerales(bool conDeuda)
        {
            return new CondicionesPagoResponse
            {
                FormasPago = new List<FormaPagoDTO>
                {
                    new FormaPagoDTO { formaPago = EFECTIVO, descripcion = "Efectivo" },
                    new FormaPagoDTO { formaPago = TRANSFERENCIA, descripcion = "Transferencia" },
                    new FormaPagoDTO { formaPago = TARJETA, descripcion = "Tarjeta" },
                    new FormaPagoDTO { formaPago = RECIBO, descripcion = "Recibo bancario" }
                },
                PlazosPago = new List<PlazoPagoDTO>
                {
                    Plazo(Constantes.PlazosPago.PREPAGO, 1, 0, 0),
                    Plazo("CONTADO", 1, 0, 0),
                    Plazo("30", 1, 30, 0),
                    Plazo("30/60/90", 3, 30, 0)
                },
                InfoDeuda = new InfoDeudaClienteDTO { TieneDeudaVencida = conDeuda },
                FormaPagoRecomendada = conDeuda ? EFECTIVO : null,
                PlazoPagoRecomendado = conDeuda ? Constantes.PlazosPago.PREPAGO : null
            };
        }

        private static PlazoPagoDTO Plazo(string codigo, short numeroPlazos, short diasPrimerPlazo, short mesesPrimerPlazo)
        {
            return new PlazoPagoDTO
            {
                plazoPago = codigo,
                descripcion = codigo,
                numeroPlazos = numeroPlazos,
                diasPrimerPlazo = diasPrimerPlazo,
                mesesPrimerPlazo = mesesPrimerPlazo
            };
        }

        private static PoliticaPagoCanal.CondicionesFicha Ficha(string formaPago, string plazoPago)
        {
            return new PoliticaPagoCanal.CondicionesFicha
            {
                FormasPago = formaPago == null ? new List<string>() : new List<string> { formaPago },
                PlazosPago = plazoPago == null ? new List<string>() : new List<string> { plazoPago }
            };
        }

        // --- El canal ---

        [TestMethod]
        public void EsApp_SoloReconoceElCanalDeLaApp()
        {
            Assert.IsTrue(PoliticaPagoCanal.EsApp("APP"));
            Assert.IsTrue(PoliticaPagoCanal.EsApp(" app "));
            Assert.IsFalse(PoliticaPagoCanal.EsApp("WEB"));
            Assert.IsFalse(PoliticaPagoCanal.EsApp(null));
        }

        // --- Punto 3: con deuda, SOLO tarjeta (más restrictivo que la política general) ---

        [TestMethod]
        public void AplicarPoliticaApp_ClienteConDeuda_SoloDejaTarjeta()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: true), Ficha(RECIBO, "30/60/90"));

            CollectionAssert.AreEquivalent(
                new[] { TARJETA },
                condiciones.FormasPago.Select(f => f.formaPago).ToList(),
                "Con deuda, en la app no vale ni efectivo ni transferencia: solo tarjeta");
        }

        [TestMethod]
        public void AplicarPoliticaApp_ClienteConDeuda_NoDejaFinanciarNiConSuFicha()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: true), Ficha(RECIBO, "30/60/90"));

            Assert.IsFalse(condiciones.PlazosPago.Any(p => p.plazoPago == "30/60/90"));
            Assert.IsFalse(condiciones.PlazosPago.Any(p => p.plazoPago == "30"));
            Assert.IsTrue(condiciones.PlazosPago.Any(p => p.plazoPago == Constantes.PlazosPago.PREPAGO));
        }

        [TestMethod]
        public void AplicarPoliticaApp_ClienteConDeuda_RecomiendaTarjetaNoEfectivo()
        {
            // La política general recomienda efectivo ante deuda. En una app no hay efectivo.
            CondicionesPagoResponse generales = CondicionesGenerales(conDeuda: true);
            Assert.AreEqual(EFECTIVO, generales.FormaPagoRecomendada);

            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(generales, Ficha(null, null));

            Assert.AreEqual(TARJETA, condiciones.FormaPagoRecomendada);
        }

        // --- Puntos 1 y 2: tarjeta al contado por defecto, crédito solo con ficha ---

        [TestMethod]
        public void AplicarPoliticaApp_SinDeuda_RecomiendaTarjetaAlContado()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(null, null));

            Assert.AreEqual(TARJETA, condiciones.FormaPagoRecomendada);
            Assert.AreEqual(Constantes.PlazosPago.PREPAGO, condiciones.PlazoPagoRecomendado);
        }

        [TestMethod]
        public void AplicarPoliticaApp_SinDeudaYSinCreditoEnLaFicha_NoOfreceFinanciacion()
        {
            // El selector general le concede plazos "de cortesía" a los clientes buenos. En la app
            // no hay un vendedor decidiendo: si no está en su ficha, no se ofrece.
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(null, null));

            Assert.IsFalse(condiciones.PlazosPago.Any(p => p.plazoPago == "30"));
            Assert.IsFalse(condiciones.PlazosPago.Any(p => p.plazoPago == "30/60/90"));
            Assert.IsTrue(condiciones.PlazosPago.Any(p => p.plazoPago == Constantes.PlazosPago.PREPAGO));
        }

        [TestMethod]
        public void AplicarPoliticaApp_SinDeudaYConCreditoEnLaFicha_LoOfrecePeroNoPorDefecto()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(RECIBO, "30/60/90"));

            Assert.IsTrue(condiciones.PlazosPago.Any(p => p.plazoPago == "30/60/90"),
                "Si su ficha lo permite, el crédito se puede ofrecer");
            Assert.IsTrue(condiciones.FormasPago.Any(f => f.formaPago == RECIBO));
            Assert.AreEqual(Constantes.PlazosPago.PREPAGO, condiciones.PlazoPagoRecomendado,
                "Pero nunca por defecto: por defecto, tarjeta al contado");
        }

        [TestMethod]
        public void AplicarPoliticaApp_SinDeuda_LaTarjetaSiempreEstaDisponible()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(null, null));

            Assert.IsTrue(condiciones.FormasPago.Any(f => f.formaPago == TARJETA));
            Assert.IsFalse(condiciones.FormasPago.Any(f => f.formaPago == EFECTIVO),
                "El efectivo no se le ofrece a un cliente que compra por la app");
        }

        // --- Lo que se pide no manda: manda lo que la política autoriza ---

        [TestMethod]
        public void ResolverFormaPago_PideReciboSinTenerloEnLaFicha_SeQuedaEnTarjeta()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(null, null));

            Assert.AreEqual(TARJETA, PoliticaPagoCanal.ResolverFormaPago(condiciones, RECIBO));
        }

        [TestMethod]
        public void ResolverFormaPago_PideReciboYLoTieneEnLaFicha_SeLeRespeta()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(RECIBO, "30/60/90"));

            Assert.AreEqual(RECIBO, PoliticaPagoCanal.ResolverFormaPago(condiciones, RECIBO));
        }

        [TestMethod]
        public void ResolverFormaPago_ClienteConDeudaQuePideRecibo_SeQuedaEnTarjeta()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: true), Ficha(RECIBO, "30/60/90"));

            Assert.AreEqual(TARJETA, PoliticaPagoCanal.ResolverFormaPago(condiciones, RECIBO));
        }

        [TestMethod]
        public void ResolverPlazosPago_SinPedirNada_PrepagoPorDefecto()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(null, null));

            Assert.AreEqual(Constantes.PlazosPago.PREPAGO, PoliticaPagoCanal.ResolverPlazosPago(condiciones, null));
        }

        [TestMethod]
        public void ResolverPlazosPago_PideFinanciacionQueNoTiene_SeQuedaEnPrepago()
        {
            CondicionesPagoResponse condiciones = PoliticaPagoCanal.AplicarPoliticaApp(
                CondicionesGenerales(conDeuda: false), Ficha(null, null));

            Assert.AreEqual(Constantes.PlazosPago.PREPAGO, PoliticaPagoCanal.ResolverPlazosPago(condiciones, "30/60/90"));
        }

        // --- ¿Hay que llevar al cliente a la pasarela? ---

        [TestMethod]
        public void SeCobraEnElMomento_TarjetaYPrepago_Si()
        {
            Assert.IsTrue(PoliticaPagoCanal.SeCobraEnElMomento(TARJETA, Constantes.PlazosPago.PREPAGO));
        }

        [TestMethod]
        public void SeCobraEnElMomento_ReciboA90Dias_No()
        {
            Assert.IsFalse(PoliticaPagoCanal.SeCobraEnElMomento(RECIBO, "30/60/90"));
        }
    }
}
