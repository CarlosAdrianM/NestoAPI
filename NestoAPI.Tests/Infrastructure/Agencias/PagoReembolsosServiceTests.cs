using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Nesto#415 / Nesto#340 (Agencias, slice A4.3): PARIDAD DE CAMPOS con el cliente antes de
    /// migrar el pago de reembolsos. Fija, campo a campo, los apuntes de PreContabilidad que
    /// construía <c>AgenciasViewModel.OnContabilizarReembolso</c> en Nesto (VB.NET): uno al haber de
    /// la cuenta de reembolsos de la agencia de cada envío y uno al debe del cliente por la suma.
    /// Una sola diferencia aquí descuadra el cuadre de reembolsos (la 555 de la agencia) y no se
    /// vería hasta la conciliación.
    /// </summary>
    [TestClass]
    public class PagoReembolsosServiceTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 18);
        private const string USUARIO = @"NUEVAVISION\laura";

        private static Empresa EmpresaUno() => new Empresa
        {
            Número = "1  ",
            FormaPagoEfectivo = "EFC",
            DelegaciónVarios = "ALC",   // distinto de ALG a propósito: el cliente NO usaba los "varios"
            FormaVentaVarios = "DIR"
        };

        private static AgenciaTransporte Innovatrans() => new AgenciaTransporte
        {
            Numero = 12,
            Nombre = "Innovatrans",
            CuentaReembolsos = "5720000001 "
        };

        private static AgenciaTransporte Asm() => new AgenciaTransporte
        {
            Numero = 3,
            Nombre = "ASM Envíos Urgentes",
            CuentaReembolsos = "5720000003 "
        };

        private static EnviosAgencia Envio(int numero, int pedido, string cliente, decimal reembolso, AgenciaTransporte agencia,
            string empresa = "1  ", DateTime? fechaPago = null) => new EnviosAgencia
            {
                Numero = numero,
                Empresa = empresa,
                Cliente = cliente,
                Pedido = pedido,
                Reembolso = reembolso,
                Agencia = agencia?.Numero ?? 0,
                AgenciasTransporte = agencia,
                FechaPagoReembolso = fechaPago
            };

        // ===== Construcción de los apuntes (paridad con el cliente) =====

        [TestMethod]
        public void ConstruirApuntesPagoReembolsos_DosEnviosDeAgenciasDistintas_UnHaberPorEnvioYUnDebeAlCliente()
        {
            List<EnviosAgencia> envios = new List<EnviosAgencia>
            {
                Envio(248001, 925100, "15191     ", 121.50M, Innovatrans()),
                Envio(248002, 925101, "29268     ", 30M, Asm())
            };

            List<PreContabilidad> lineas = TramitacionEnviosService.ConstruirApuntesPagoReembolsos(
                envios, Innovatrans(), EmpresaUno(), "12345     ", HOY, USUARIO);

            Assert.AreEqual(3, lineas.Count, "un haber por envío + un debe al cliente");

            PreContabilidad haber1 = lineas[0];
            Assert.AreEqual("1", haber1.Empresa, "La empresa va trimeada");
            Assert.AreEqual("_PagoReemb", haber1.Diario);
            Assert.AreEqual(1, haber1.Asiento);
            Assert.AreEqual(HOY, haber1.Fecha);
            Assert.AreEqual(HOY, haber1.FechaVto);
            Assert.AreEqual("3", haber1.TipoApunte, "Pago");
            Assert.AreEqual("1", haber1.TipoCuenta, "Cuenta contable: la de reembolsos de la agencia del envío");
            Assert.AreEqual("5720000001", haber1.Nº_Cuenta, "La cuenta de reembolsos va trimeada");
            Assert.AreEqual("Pago reembolso 15191", haber1.Concepto);
            Assert.AreEqual(121.50M, haber1.Haber);
            Assert.AreEqual(0M, haber1.Debe);
            Assert.AreEqual("Innovatran", haber1.Nº_Documento, "10 primeras letras del nombre de la agencia del envío");
            Assert.AreEqual("ALG", haber1.Delegación, "A pelo, como en el cliente: NO DelegaciónVarios de la empresa");
            Assert.AreEqual("VAR", haber1.FormaVenta, "A pelo, como en el cliente: NO FormaVentaVarios de la empresa");
            Assert.IsNull(haber1.Contacto);
            Assert.IsNull(haber1.FormaPago);
            Assert.IsNull(haber1.Vendedor);
            Assert.AreEqual(USUARIO, haber1.Usuario);
            Assert.IsTrue(haber1.Fecha_Modificación > DateTime.Now.AddMinutes(-1), "Fecha Modificación es NOT NULL: sin asignarla EF manda 01/01/0001");

            PreContabilidad haber2 = lineas[1];
            Assert.AreEqual("5720000003", haber2.Nº_Cuenta, "Cada envío contra la cuenta de SU agencia");
            Assert.AreEqual(30M, haber2.Haber);
            Assert.AreEqual("Pago reembolso 29268", haber2.Concepto);
            Assert.AreEqual("ASM Envíos", haber2.Nº_Documento);

            PreContabilidad debe = lineas[2];
            Assert.AreEqual("1", debe.Empresa);
            Assert.AreEqual("_PagoReemb", debe.Diario);
            Assert.AreEqual(1, debe.Asiento);
            Assert.AreEqual(HOY, debe.Fecha);
            Assert.AreEqual(HOY, debe.FechaVto);
            Assert.AreEqual("3", debe.TipoApunte);
            Assert.AreEqual("2", debe.TipoCuenta, "Cliente");
            Assert.AreEqual("12345", debe.Nº_Cuenta, "El cliente al que se paga, trimeado");
            Assert.AreEqual("0", debe.Contacto);
            Assert.AreEqual("Pago reembolso Innovatrans", debe.Concepto, "Con el nombre de la agencia SELECCIONADA, no la de cada envío");
            Assert.AreEqual(151.50M, debe.Debe, "La suma de los reembolsos");
            Assert.AreEqual(0M, debe.Haber);
            Assert.AreEqual("Innovatran", debe.Nº_Documento);
            Assert.AreEqual("ALG", debe.Delegación);
            Assert.AreEqual("VAR", debe.FormaVenta);
            Assert.AreEqual("EFC", debe.FormaPago, "La forma de pago en efectivo de la empresa");
            Assert.AreEqual("NV", debe.Vendedor, "El vendedor general");
            Assert.AreEqual(USUARIO, debe.Usuario);
        }

        [TestMethod]
        public void ConstruirApuntesPagoReembolsos_NombreDeAgenciaCorto_ElNumeroDeDocumentoNoSeRecorta()
        {
            AgenciaTransporte gls = new AgenciaTransporte { Numero = 5, Nombre = "GLS", CuentaReembolsos = "5720000005" };

            List<PreContabilidad> lineas = TramitacionEnviosService.ConstruirApuntesPagoReembolsos(
                new List<EnviosAgencia> { Envio(1, 1, "1", 10M, gls) }, gls, EmpresaUno(), "1", HOY, USUARIO);

            Assert.AreEqual("GLS", lineas[0].Nº_Documento);
            Assert.AreEqual("GLS", lineas[1].Nº_Documento);
        }

        // ===== Validaciones =====

        [TestMethod]
        public void ErrorEnviosAPagar_TodoEnOrden_DevuelveNulo()
        {
            List<EnviosAgencia> envios = new List<EnviosAgencia> { Envio(248001, 925100, "15191", 121.50M, Innovatrans()) };

            Assert.IsNull(TramitacionEnviosService.ErrorEnviosAPagar(envios, new List<int> { 248001 }, "1"));
        }

        [TestMethod]
        public void ErrorEnviosAPagar_EnvioQueNoExiste_LoDice()
        {
            string error = TramitacionEnviosService.ErrorEnviosAPagar(new List<EnviosAgencia>(), new List<int> { 999 }, "1");

            StringAssert.Contains(error, "No existe el envío 999");
        }

        [TestMethod]
        public void ErrorEnviosAPagar_YaPagado_LoDiceConLaFecha()
        {
            List<EnviosAgencia> envios = new List<EnviosAgencia>
            {
                Envio(248001, 925100, "15191", 121.50M, Innovatrans(), fechaPago: new DateTime(2026, 9, 17))
            };

            string error = TramitacionEnviosService.ErrorEnviosAPagar(envios, new List<int> { 248001 }, "1");

            StringAssert.Contains(error, "248001");
            StringAssert.Contains(error, "925100");
            StringAssert.Contains(error, "17/09/2026");
        }

        [TestMethod]
        public void ErrorEnviosAPagar_DeOtraEmpresa_LoDice()
        {
            // "1  " frente a "1": el relleno de los char no cuenta (reference_migracion_ef_padding_cadenas).
            List<EnviosAgencia> envios = new List<EnviosAgencia>
            {
                Envio(248001, 925100, "15191", 121.50M, Innovatrans(), empresa: "1  "),
                Envio(248002, 925101, "15191", 10M, Innovatrans(), empresa: "3  ")
            };

            string error = TramitacionEnviosService.ErrorEnviosAPagar(envios, new List<int> { 248001, 248002 }, "1");

            Assert.IsFalse(error.Contains("248001 es de la empresa"), "el relleno no es una diferencia");
            StringAssert.Contains(error, "El envío 248002 es de la empresa 3, no de la 1");
        }

        [TestMethod]
        public void ErrorEnviosAPagar_SinReembolsoOSinCuenta_AcumulaTodosLosMotivos()
        {
            AgenciaTransporte sinCuenta = new AgenciaTransporte { Numero = 9, Nombre = "Canteras", CuentaReembolsos = "  " };
            List<EnviosAgencia> envios = new List<EnviosAgencia>
            {
                Envio(248001, 925100, "15191", 0M, Innovatrans()),
                Envio(248002, 925101, "15191", 10M, sinCuenta)
            };

            string error = TramitacionEnviosService.ErrorEnviosAPagar(envios, new List<int> { 248001, 248002 }, "1");

            StringAssert.Contains(error, "El envío 248001 (pedido 925100) no tiene reembolso");
            StringAssert.Contains(error, "La agencia del envío 248002 no tiene establecida una cuenta de reembolsos");
        }

        // ===== Cableado =====

        [TestMethod]
        public async Task PagarReembolsosAsync_SinEnviosSeleccionados_LanzaSinTocarLaBase()
        {
            NVEntities db = A.Fake<NVEntities>();
            IContabilidadService contabilidad = A.Fake<IContabilidadService>();
            TramitacionEnviosService servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.PagarReembolsosAsync(new PagoReembolsosDTO { Empresa = "1", Cliente = "12345", Agencia = 12, NumerosEnvio = new List<int>() }, USUARIO));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.PagarReembolsosAsync(new PagoReembolsosDTO { Empresa = "1", Cliente = "  ", Agencia = 12, NumerosEnvio = new List<int> { 1 } }, USUARIO));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.PagarReembolsosAsync(null, USUARIO));

            A.CallTo(() => db.EnviosAgencias).MustNotHaveHappened();
            A.CallTo(() => contabilidad.CrearLineas(A<NVEntities>.Ignored, A<List<PreContabilidad>>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PagoReembolsosDTO_ContratoDelJson_NombresQueMandaNesto()
        {
            // Espejo en AgenciaServicePagoReembolsosTests (Nesto). Si un nombre cambia, Web API deja
            // la propiedad en su valor por defecto sin dar ningún error: NumerosEnvio = null → 400.
            string[] propiedades = typeof(PagoReembolsosDTO).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Agencia", "Cliente", "Empresa", "NumerosEnvio" }, propiedades);

            string[] respuesta = typeof(ResultadoPagoReembolsos).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Asiento", "Envios", "Importe", "Mensaje" }, respuesta);
        }
    }
}
