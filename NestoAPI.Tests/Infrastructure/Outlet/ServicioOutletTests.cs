using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Outlet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Outlet
{
    /// <summary>NestoAPI#643: el cuadre del Outlet (dry-run con el interruptor apagado; escribe y republica encendido) y su job.</summary>
    [TestClass]
    public class ServicioOutletTests
    {
        private IRepositorioOutlet repositorio;
        private List<Exception> errores;

        private static List<SituacionOutletProducto> Ejemplo()
        {
            return new List<SituacionOutletProducto>
            {
                ReglasOutletTests.Producto("1", "PEL", "TIN"),                                                     // entra
                ReglasOutletTests.Producto("2", "COS", "124", estado: 1, actuales: new[] { ReglasOutlet.ESTETICA }), // sale (con ficha)
                ReglasOutletTests.Producto("3", "COS", "124", ficha: false, actuales: new[] { ReglasOutlet.ESTETICA }), // sale sin ficha: no se republica
                ReglasOutletTests.Producto("4", "COS", "201", actuales: new[] { ReglasOutlet.ESTETICA }),           // cambia de grupo
                ReglasOutletTests.Producto("5", "PEL", "TIN", ficha: false)                                       // no está en la tienda: nada
            };
        }

        private ServicioOutlet Servicio(string interruptor, List<SituacionOutletProducto> situacion = null)
        {
            repositorio = A.Fake<IRepositorioOutlet>();
            errores = new List<Exception>();
            _ = A.CallTo(() => repositorio.CargarSituacion()).Returns(Task.FromResult(situacion ?? Ejemplo()));
            _ = A.CallTo(() => repositorio.EncolarRepublicacion(A<IEnumerable<string>>._, A<string>._))
                .ReturnsLazily((IEnumerable<string> p, string u) => Task.FromResult(p.Count()));
            return new ServicioOutlet(repositorio, clave => clave == ServicioOutlet.CLAVE_INTERRUPTOR ? interruptor : null, errores.Add);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("false")]
        [DataRow("si")]
        public void InterruptorEncendido_SoloConTrue(string valor)
        {
            Assert.IsFalse(Servicio(valor).InterruptorEncendido());
        }

        [TestMethod]
        public async Task Cuadrar_InterruptorApagado_EsDryRunYNoEscribeNiEncola()
        {
            ResultadoCuadreOutlet resultado = await Servicio(null).Cuadrar();

            Assert.IsFalse(resultado.Aplicado);
            Assert.AreEqual(1, resultado.Entran);
            Assert.AreEqual(2, resultado.Salen);
            Assert.AreEqual(1, resultado.CambianDeGrupo);
            Assert.AreEqual(3, resultado.SeRepublicarian);
            A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.EncolarRepublicacion(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Previsualizar_AunqueEsteEncendido_NoEscribe()
        {
            ResultadoCuadreOutlet resultado = await Servicio("true").Previsualizar();

            Assert.AreEqual(4, resultado.Cambios.Count);
            A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.EncolarRepublicacion(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cuadrar_InterruptorEncendido_EscribeCadaCambioYRepublicaSoloLosDeLaTienda()
        {
            ResultadoCuadreOutlet resultado = await Servicio("True").Cuadrar();

            Assert.IsTrue(resultado.Aplicado);
            Assert.AreEqual(4, resultado.Aplicados);
            A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>._, ServicioOutlet.USUARIO)).MustHaveHappened(4, Times.Exactly);
            A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>.That.Matches(c => c.Numero == "5"), A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.EncolarRepublicacion(
                    A<IEnumerable<string>>.That.Matches(p => p.OrderBy(x => x).SequenceEqual(new[] { "1", "2", "4" })),
                    ServicioOutlet.USUARIO))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(3, resultado.Encolados);
        }

        [TestMethod]
        public async Task Cuadrar_Encendido_NuncaEncolaLoQueNoEstaEnLaTienda()
        {
            List<string> encolados = new List<string>();
            ServicioOutlet servicio = Servicio("true", new List<SituacionOutletProducto>
            {
                ReglasOutletTests.Producto("10", "PEL", "TIN", ficha: false),
                ReglasOutletTests.Producto("11", "PEL", "TIN", publicable: false),
                ReglasOutletTests.Producto("12", "PEL", "TIN", ficha: false, actuales: new[] { ReglasOutlet.PELUQUERIA })
            });
            _ = A.CallTo(() => repositorio.EncolarRepublicacion(A<IEnumerable<string>>._, A<string>._))
                .Invokes((IEnumerable<string> p, string u) => encolados.AddRange(p))
                .Returns(Task.FromResult(0));

            ResultadoCuadreOutlet resultado = await servicio.Cuadrar();

            Assert.AreEqual(0, resultado.Entran);
            Assert.AreEqual(1, resultado.Salen, "El 12 sale de la categoría (no está en la tienda)…");
            CollectionAssert.DoesNotContain(encolados, "10");
            CollectionAssert.DoesNotContain(encolados, "11");
            CollectionAssert.DoesNotContain(encolados, "12", "…pero no se republica: sin ficha web no se publica por este motivo");
        }

        [TestMethod]
        public async Task Cuadrar_Encendido_EnTandas()
        {
            List<SituacionOutletProducto> muchos = Enumerable.Range(1, ServicioOutlet.TAMANO_TANDA * 2 + 5)
                .Select(i => ReglasOutletTests.Producto(i.ToString(), "PEL", "TIN")).ToList();

            ResultadoCuadreOutlet resultado = await Servicio("true", muchos).Cuadrar();

            Assert.AreEqual(muchos.Count, resultado.Aplicados);
            A.CallTo(() => repositorio.EncolarRepublicacion(A<IEnumerable<string>>._, A<string>._)).MustHaveHappened(3, Times.Exactly);
        }

        [TestMethod]
        public async Task Cuadrar_Encendido_UnProductoQueFallaNoParaALosDemas()
        {
            ServicioOutlet servicio = Servicio("true");
            _ = A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>.That.Matches(c => c.Numero == "2"), A<string>._))
                .ThrowsAsync(new InvalidOperationException("bloqueo"));

            ResultadoCuadreOutlet resultado = await servicio.Cuadrar();

            Assert.AreEqual(3, resultado.Aplicados);
            Assert.AreEqual(1, resultado.ConError);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.EncolarRepublicacion(
                    A<IEnumerable<string>>.That.Matches(p => !p.Contains("2")), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_DryRun_MandaElExcelConLaSimulacion()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            _ = A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => enviado = new MailMessage { Subject = m.Subject, Body = m.Body })
                .Returns(true);
            ServicioOutlet servicio = Servicio(null);

            ResultadoCuadreOutlet resultado = await new OutletJobsService(servicio, correo, errores.Add, new[] { "carlos@example.com" }).Ejecutar();

            Assert.IsFalse(resultado.Aplicado);
            Assert.IsNotNull(enviado);
            StringAssert.Contains(enviado.Subject, "simulación");
            StringAssert.Contains(enviado.Body, ServicioOutlet.CLAVE_INTERRUPTOR);
            Assert.AreEqual(0, errores.Count);
            A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_SinCambios_NoMandaCorreo()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            ServicioOutlet servicio = Servicio(null, new List<SituacionOutletProducto>
            {
                ReglasOutletTests.Producto("1", "PEL", "TIN", actuales: new[] { ReglasOutlet.PELUQUERIA })
            });

            _ = await new OutletJobsService(servicio, correo, errores.Add, new[] { "carlos@example.com" }).Ejecutar();

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void WebConfig_ElInterruptorNaceApagado()
        {
            // bin\Debug -> NestoAPI.Tests -> raíz de la solución -> NestoAPI\Web.config
            string ruta = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(System.IO.File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");
            System.Xml.XmlDocument xml = new System.Xml.XmlDocument();
            xml.Load(ruta);
            System.Xml.XmlNode nodo = xml.SelectSingleNode("/configuration/appSettings/add[@key='" + ServicioOutlet.CLAVE_INTERRUPTOR + "']");

            Assert.IsNotNull(nodo, "Falta la clave del interruptor en Web.config");
            Assert.IsFalse(new ServicioOutlet(A.Fake<IRepositorioOutlet>(), c => nodo.Attributes["value"].Value).InterruptorEncendido(),
                "Nace apagado: se enciende cuando Carlos dé el visto bueno a la simulación");
        }

        [TestMethod]
        public void GenerarExcel_DevuelveUnLibro()
        {
            ResultadoCuadreOutlet resultado = ServicioOutlet.Resumir(Ejemplo(), ReglasOutlet.Calcular(Ejemplo()), aplicado: false);

            byte[] excel = OutletJobsService.GenerarExcel(resultado);

            Assert.IsTrue(excel.Length > 0);
            Assert.AreEqual((byte)'P', excel[0], "Un .xlsx es un zip (empieza por PK)");
            Assert.AreEqual((byte)'K', excel[1]);
        }
    }
}
