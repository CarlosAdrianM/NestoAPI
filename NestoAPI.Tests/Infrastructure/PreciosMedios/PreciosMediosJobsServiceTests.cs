using System;
using System.Collections.Generic;
using System.Net.Mail;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreciosMedios;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (b): el interruptor del job semanal (nace apagado), el aplazamiento si el SP sigue
    /// corriendo, el corte de la pasada del SP y el correo solo cuando hay diferencias no esperadas.
    /// </summary>
    [TestClass]
    public class PreciosMediosJobsServiceTests
    {
        private static readonly DateTime DOMINGO_0630 = new DateTime(2026, 10, 4, 6, 30, 0);

        private ILectorParametrosUsuario lector;
        private IRepositorioPreciosMedios repositorio;
        private IServicioCorreoElectronico correo;
        private int serviciosCreados;

        [TestInitialize]
        public void Inicializar()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            repositorio = A.Fake<IRepositorioPreciosMedios>();
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            A.CallTo(() => repositorio.ExisteTablaSombra()).Returns(true);
            A.CallTo(() => repositorio.EmpresaEspejo(A<string>._)).ReturnsLazily((string e) => e == "1" ? "3" : e);
            A.CallTo(() => repositorio.ProductosConCompras(A<string>._, A<string>._)).Returns(new List<string>());
            A.CallTo(() => repositorio.ProductosConCompras("1", "3")).Returns(new List<string> { "IGUAL" });
            A.CallTo(() => repositorio.LeerDatos("1", "3", "IGUAL", A<bool>._)).Returns(ServicioSombraPreciosMediosTests.ProductoIgual());
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Returns(null);
            serviciosCreados = 0;
        }

        private ServicioSombraPreciosMedios CrearServicio()
        {
            serviciosCreados++;
            return new ServicioSombraPreciosMedios(repositorio);
        }

        private void Interruptor(string valor)
        {
            A.CallTo(() => lector.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                Constantes.ParametrosUsuario.PRECIOS_MEDIOS_SOMBRA)).Returns(valor);
        }

        [TestMethod]
        public void InterpretarModo_SoloUnoOActivoLoEnciende()
        {
            Assert.AreEqual(ModoSombraPreciosMedios.Activo, PreciosMediosJobsService.InterpretarModo("1"));
            Assert.AreEqual(ModoSombraPreciosMedios.Activo, PreciosMediosJobsService.InterpretarModo(" activo "));
            Assert.AreEqual(ModoSombraPreciosMedios.Apagado, PreciosMediosJobsService.InterpretarModo(null));
            Assert.AreEqual(ModoSombraPreciosMedios.Apagado, PreciosMediosJobsService.InterpretarModo(""));
            Assert.AreEqual(ModoSombraPreciosMedios.Apagado, PreciosMediosJobsService.InterpretarModo("0"));
            Assert.AreEqual(ModoSombraPreciosMedios.Apagado, PreciosMediosJobsService.InterpretarModo("Sombra"));
        }

        [TestMethod]
        public void LeerModo_SiFallaLaLectura_Apagado()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("BD caída"));
            Assert.AreEqual(ModoSombraPreciosMedios.Apagado, PreciosMediosJobsService.LeerModo(lector));
            Assert.AreEqual(ModoSombraPreciosMedios.Apagado, PreciosMediosJobsService.LeerModo(null));
        }

        [TestMethod]
        public void Procesar_SinParametro_NoHaceNada()
        {
            Interruptor(null);

            ResumenPasadaSombraPrecioMedio resumen = PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false);

            Assert.IsNull(resumen);
            Assert.AreEqual(0, serviciosCreados, "apagado: ni siquiera se crea el servicio (no se abre ninguna conexión)");
            A.CallTo(repositorio).MustNotHaveHappened();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Procesar_ConParametroACero_NoHaceNada()
        {
            Interruptor("0");
            Assert.IsNull(PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false));
            Assert.AreEqual(0, serviciosCreados);
        }

        [TestMethod]
        public void Procesar_Encendido_HaceLaPasadaDeLasTresEmpresasYSinDiferenciasNoMandaCorreo()
        {
            Interruptor("1");

            ResumenPasadaSombraPrecioMedio resumen = PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false);

            Assert.IsNotNull(resumen);
            Assert.IsNull(resumen.Error);
            CollectionAssert.AreEqual(new[] { "1", "4", "5" }, resumen.Empresas.ConvertAll(e => e.Empresa));
            Assert.AreEqual(1, resumen.Empresas[0].Iguales);
            Assert.AreEqual(DOMINGO_0630.Date, resumen.FechaPasada);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Procesar_ForzadoConElInterruptorApagado_HaceLaPasada()
        {
            Interruptor(null);
            Assert.IsNotNull(PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: true));
            Assert.AreEqual(1, serviciosCreados);
        }

        [TestMethod]
        public void Procesar_ConDiferenciasNoEsperadas_MandaUnCorreo()
        {
            Interruptor("1");
            A.CallTo(() => repositorio.LeerDatos("1", "3", "IGUAL", A<bool>._)).Returns(ServicioSombraPreciosMediosTests.ProductoIgual("IGUAL", 9m));
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            ResumenPasadaSombraPrecioMedio resumen = PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false);

            Assert.AreEqual(1, resumen.NoEsperados);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(Constantes.Correos.INFORMATICA, enviado.To[0].Address);
            StringAssert.Contains(enviado.Subject, "1 productos con diferencias no esperadas");
            StringAssert.Contains(enviado.Body, "IGUAL");
        }

        [TestMethod]
        public void Procesar_SoloPendientesYEmpates_NoMandaCorreo()
        {
            Interruptor("1");
            A.CallTo(() => repositorio.ProductosConCompras("1", "3")).Returns(new List<string> { "PENDIENTE", "EMPATE" });
            A.CallTo(() => repositorio.LeerDatos("1", "3", "PENDIENTE", A<bool>._)).Returns(ServicioSombraPreciosMediosTests.ProductoConFacturaPosterior());
            A.CallTo(() => repositorio.LeerDatos("1", "3", "EMPATE", A<bool>._)).Returns(ServicioSombraPreciosMediosTests.ProductoConEmpate());
            // Corte = inicio real de la pasada del SP: la factura «posterior» del escenario es de después.
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios
            {
                Inicio = new DateTime(2026, 9, 27, 0, 30, 0),
                Fin = new DateTime(2026, 9, 27, 3, 0, 0)
            });

            ResumenPasadaSombraPrecioMedio resumen = PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false);

            Assert.AreEqual(1, resumen.Empresas[0].Pendientes);
            Assert.AreEqual(1, resumen.Empresas[0].Empates);
            Assert.AreEqual(0, resumen.NoEsperados);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Procesar_SinLaTabla_FallaDeFormaControlada()
        {
            Interruptor("1");
            A.CallTo(() => repositorio.ExisteTablaSombra()).Returns(false);

            ResumenPasadaSombraPrecioMedio resumen = PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false);

            StringAssert.Contains(resumen.Error, "PreciosMediosSombra");
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Procesar_SiElSPSigueCorriendo_SeAplazaSinLeerNada()
        {
            Interruptor("1");
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios { Inicio = DOMINGO_0630.Date.AddMinutes(30), Fin = null });

            ResumenPasadaSombraPrecioMedio resumen = PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, forzar: false);

            StringAssert.Contains(resumen.Error, "sigue en ejecución");
            A.CallTo(() => repositorio.ProductosConCompras(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Procesar_ElCorteEsElInicioRealDelSPSiSePuedeLeer()
        {
            Interruptor("1");
            DateTime inicio = new DateTime(2026, 10, 4, 0, 30, 2);
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios { Inicio = inicio, Fin = inicio.AddHours(4) });

            Assert.AreEqual(inicio, PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, false).CorteSP);
        }

        [TestMethod]
        public void Procesar_SinMsdb_ElCorteEsElDomingoALas0030()
        {
            Interruptor("1");
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Throws(new Exception("sin permiso"));

            Assert.AreEqual(new DateTime(2026, 10, 4, 0, 30, 0), PreciosMediosJobsService.Procesar(lector, CrearServicio, correo, DOMINGO_0630, false).CorteSP);
        }

        [TestMethod]
        public void CorteSP_ElDomingoMasRecienteALas0030()
        {
            Assert.AreEqual(new DateTime(2026, 10, 4, 0, 30, 0), PreciosMediosJobsService.CorteSP(DOMINGO_0630));
            Assert.AreEqual(new DateTime(2026, 10, 4, 0, 30, 0), PreciosMediosJobsService.CorteSP(new DateTime(2026, 10, 4, 0, 30, 0)));
            Assert.AreEqual(new DateTime(2026, 9, 27, 0, 30, 0), PreciosMediosJobsService.CorteSP(new DateTime(2026, 10, 4, 0, 10, 0)),
                "el domingo antes de las 00:30, la pasada anterior");
            Assert.AreEqual(new DateTime(2026, 9, 27, 0, 30, 0), PreciosMediosJobsService.CorteSP(new DateTime(2026, 9, 30, 17, 0, 0)));
            Assert.AreEqual(new DateTime(2026, 9, 27, 0, 30, 0), PreciosMediosJobsService.CorteSP(new DateTime(2026, 10, 3, 23, 59, 0)));
        }
    }
}
