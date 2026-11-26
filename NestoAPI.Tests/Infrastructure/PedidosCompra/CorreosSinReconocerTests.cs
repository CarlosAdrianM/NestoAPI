using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857j (Carlos 26/11/26, caso SEUR de Iberocel): los correos de una agencia conocida que el lector no reconoce dejan rastro aparte
    /// (sin proveedor, pedido ni fecha), sin su publicidad ni sus encuestas; Reanalizar los pasa a correos normales cuando ya se reconocen.
    /// </summary>
    [TestClass]
    public class CorreosSinReconocerTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 26, 10, 0, 0);
        private static int siguiente = 1;

        // Un correo de la propia SEUR con un formato que su clase no reconoce: ni código, ni entrega, ni estado
        private const string SEUR_SIN_RECONOCER = "<html><body><p>¡Hola, Nueva Vision S.a.!</p><p>Hay novedades sobre un paquete que te envían.</p>" +
            "<p>Abre la app de SEUR para ver los detalles.</p></body></html>";

        private ILectorBuzonCompras lector;
        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private IRepositorioCorreosSinReconocer sinReconocer;
        private List<CorreoSinReconocerFila> apuntados;
        private List<Exception> errores;

        [TestInitialize]
        public void Setup()
        {
            ServicioCorreosSeguimientoCompras.OlvidarSinTablaApuntado();
            ServicioCorreosSeguimientoCompras.OlvidarSinTablaSinReconocerApuntado();
            errores = new List<Exception>();
            apuntados = new List<CorreoSinReconocerFila>();
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon-prueba@ejemplo.test");
            sinReconocer = A.Fake<IRepositorioCorreosSinReconocer>();
            A.CallTo(() => sinReconocer.Disponible()).Returns(true);
            A.CallTo(() => sinReconocer.Insertar(A<CorreoSinReconocerFila>._)).ReturnsLazily((CorreoSinReconocerFila f) =>
            {
                if (apuntados.Any(a => a.MensajeId == f.MensajeId))
                {
                    return Task.FromResult(false);
                }
                apuntados.Add(f);
                return Task.FromResult(true);
            });
            A.CallTo(() => sinReconocer.Leer(A<DateTime>._)).ReturnsLazily(() => Task.FromResult(apuntados.ToList()));
            A.CallTo(() => sinReconocer.MarcarReconocido(A<string>._, A<int?>._)).Returns(true);
        }

        private static CorreoCompras Correo(string remitente, string asunto, string cuerpo, bool html = true) => new CorreoCompras
        {
            Id = "s" + siguiente, MensajeId = "<s" + siguiente++ + "@ejemplo.test>", Recibido = AHORA.AddHours(-1), RemitenteDireccion = remitente,
            Asunto = asunto, Cuerpo = cuerpo, CuerpoEsHtml = html
        };

        private static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo) =>
            AnalizadorCorreoSeguimiento.Analizar(correo, AgenciasEntradaRegistradas.Todas, new List<RemitenteMarcado>());

        private ServicioCorreosSeguimientoCompras Servicio(IRepositorioCorreosSinReconocer conSinReconocer) =>
            new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, sinReconocer: conSinReconocer);

        private void Buzon(params CorreoCompras[] correos)
        {
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new LecturaBuzonCompras { Correos = correos.ToList() }));
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Task.FromResult(new LecturaBuzonCompras { Correos = correos.ToList() }));
        }

        #region La regla

        [TestMethod]
        public void Analizar_CorreoDeLaAgenciaSinReconocer_NoSeGuardaPeroSeApunta()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("infoenvios@mail.seur.info", "Novedades de tu envío", SEUR_SIN_RECONOCER));

            Assert.IsFalse(a.Guardar);
            Assert.AreEqual("SEUR", a.AgenciaSinReconocer);
            Assert.IsNull(a.MotivoDescarteAgencia);
        }

        [TestMethod]
        public void Analizar_PublicidadYEncuestaDeLaAgencia_NiSeGuardanNiSeApuntan()
        {
            AnalisisCorreoSeguimiento publicidad = Analizar(Correo("novedades@mail.seur.info", "Black Friday en SEUR", CorreosSeguimientoDosNivelesTests.SEUR_PUBLICIDAD_HTML));
            AnalisisCorreoSeguimiento encuesta = Analizar(Correo("infoenvios@mail.seur.info", "Tu opinión nos importa",
                "<p>¿Qué tal fue tu última entrega con nosotros?</p>"));

            Assert.IsFalse(publicidad.Guardar);
            Assert.IsNull(publicidad.AgenciaSinReconocer);
            Assert.AreEqual(ReglaCorreoAgenciaSinReconocer.MOTIVO_BOLETIN, publicidad.MotivoDescarteAgencia);
            Assert.IsFalse(encuesta.Guardar);
            Assert.IsNull(encuesta.AgenciaSinReconocer);
            Assert.AreEqual(ReglaCorreoAgenciaSinReconocer.MOTIVO_ENCUESTA, encuesta.MotivoDescarteAgencia);
        }

        [TestMethod]
        public void Analizar_CorreoReconocidoODeOtroQueNoEsAgencia_NoSeApunta()
        {
            AnalisisCorreoSeguimiento reconocido = Analizar(Correo("infoenvios@mail.seur.info", CorreosSeguimientoDosNivelesTests.SEUR_PREVISTA_ASUNTO,
                CorreosSeguimientoDosNivelesTests.SEUR_PREVISTA_HTML));
            AnalisisCorreoSeguimiento factura = Analizar(Correo("facturas@proveedor-inventado.es", "Factura", "Adjunto la factura de octubre.", html: false));

            Assert.IsTrue(reconocido.Guardar);
            Assert.IsNull(reconocido.AgenciaSinReconocer);
            Assert.IsFalse(factura.Guardar);
            Assert.IsNull(factura.AgenciaSinReconocer);
        }

        [TestMethod]
        public void Fila_ExtractoDeComoMucho2000YRemitenteEnMinusculas()
        {
            CorreoCompras correo = Correo("InfoEnvios@Mail.SEUR.info", "Novedades", new string('x', 5000), html: false);

            CorreoSinReconocerFila fila = ReglaCorreoAgenciaSinReconocer.Fila(correo, "SEUR");

            Assert.AreEqual(correo.Clave(), fila.MensajeId);
            Assert.AreEqual(ReglaCorreoAgenciaSinReconocer.LONGITUD_EXTRACTO, fila.Extracto.Length);
            Assert.AreEqual("infoenvios@mail.seur.info", fila.Remitente);
            Assert.AreEqual("SEUR", fila.Agencia);
        }

        #endregion

        #region El job

        [TestMethod]
        public async Task Pasada_ApuntaElDeLaAgenciaSinReconocer_SinGuardarloComoCorreoNiDuplicarlo()
        {
            CorreoCompras correo = Correo("infoenvios@mail.seur.info", "Novedades de tu envío", SEUR_SIN_RECONOCER);
            Buzon(correo);

            ResultadoPasadaCorreosSeguimiento primera = await Servicio(sinReconocer).Pasada(AHORA);
            ResultadoPasadaCorreosSeguimiento segunda = await Servicio(sinReconocer).Pasada(AHORA);

            Assert.AreEqual(1, primera.SinReconocer);
            Assert.AreEqual(0, segunda.SinReconocer, "Por MensajeId: no se duplica");
            Assert.AreEqual(1, apuntados.Count);
            Assert.AreEqual(0, repositorio.Correos.Count, "No es un correo de seguimiento: ni por asignar ni fechas");
            Assert.AreEqual("SEUR", apuntados[0].Agencia);
        }

        [TestMethod]
        public async Task Pasada_SinLaTabla_NoApuntaYAvisaUnaVezSinRomper()
        {
            A.CallTo(() => sinReconocer.Disponible()).Returns(false);
            Buzon(Correo("infoenvios@mail.seur.info", "Novedades de tu envío", SEUR_SIN_RECONOCER));

            ResultadoPasadaCorreosSeguimiento primera = await Servicio(sinReconocer).Pasada(AHORA);
            _ = await Servicio(sinReconocer).Pasada(AHORA);

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.Hecha, primera.Estado);
            A.CallTo(() => sinReconocer.Insertar(A<CorreoSinReconocerFila>._)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count(e => e.Message.Contains("#857j")));
        }

        [TestMethod]
        public async Task Pasada_SiFallaAlApuntar_NoRompeLaPasada()
        {
            A.CallTo(() => sinReconocer.Insertar(A<CorreoSinReconocerFila>._)).Throws(new InvalidOperationException("caída"));
            CorreoCompras reconocido = Correo("infoenvios@mail.seur.info", CorreosSeguimientoDosNivelesTests.SEUR_PREVISTA_ASUNTO,
                CorreosSeguimientoDosNivelesTests.SEUR_PREVISTA_HTML);
            Buzon(Correo("infoenvios@mail.seur.info", "Novedades de tu envío", SEUR_SIN_RECONOCER), reconocido);

            ResultadoPasadaCorreosSeguimiento resultado = await Servicio(sinReconocer).Pasada(AHORA);

            Assert.AreEqual(1, resultado.Guardados, "El reconocido se guarda igual");
            Assert.AreEqual(0, resultado.SinReconocer);
        }

        [TestMethod]
        public async Task Historico_NoApunta()
        {
            var resultado = new ResultadoPasadaCorreosSeguimiento();

            _ = await Servicio(sinReconocer).GuardarCorreos(new List<CorreoCompras> { Correo("infoenvios@mail.seur.info", "Novedades", SEUR_SIN_RECONOCER) },
                resultado, _ => Task.FromResult(new List<PedidoCompraEnAire>()));

            A.CallTo(() => sinReconocer.Insertar(A<CorreoSinReconocerFila>._)).MustNotHaveHappened();
        }

        #endregion

        #region Reanalizar

        [TestMethod]
        public async Task Reanalizar_ElQueAhoraSeReconoce_PasaACorreoNormalYQuedaMarcado()
        {
            CorreoCompras correo = Correo("infoenvios@mail.seur.info", CorreosSeguimientoDosNivelesTests.SEUR_PREVISTA_ASUNTO,
                CorreosSeguimientoDosNivelesTests.SEUR_PREVISTA_HTML);
            apuntados.Add(new CorreoSinReconocerFila { Id = 7, MensajeId = correo.Clave(), Agencia = "SEUR", Recibido = correo.Recibido });
            Buzon(correo);

            ResultadoReanalisisCorreos resultado = await Servicio(sinReconocer).Reanalizar(7, AHORA);

            Assert.AreEqual(1, resultado.Insertados);
            Assert.AreEqual(1, resultado.SinReconocerReconocidos);
            int idCorreo = repositorio.Correos.Single().Id;
            A.CallTo(() => sinReconocer.MarcarReconocido(correo.Clave(), idCorreo)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reanalizar_ElQueSigueSinReconocer_SeApuntaUnaSolaVez()
        {
            CorreoCompras yaApuntado = Correo("infoenvios@mail.seur.info", "Novedades de tu envío", SEUR_SIN_RECONOCER);
            CorreoCompras nuevo = Correo("infoenvios@mail.seur.info", "Más novedades", SEUR_SIN_RECONOCER);
            apuntados.Add(new CorreoSinReconocerFila { Id = 1, MensajeId = yaApuntado.Clave(), Agencia = "SEUR", Recibido = yaApuntado.Recibido });
            Buzon(yaApuntado, nuevo);

            ResultadoReanalisisCorreos resultado = await Servicio(sinReconocer).Reanalizar(7, AHORA);

            Assert.AreEqual(1, resultado.SinReconocerApuntados);
            Assert.AreEqual(0, resultado.SinReconocerReconocidos);
            Assert.AreEqual(2, apuntados.Count);
            Assert.AreEqual(0, repositorio.Correos.Count);
            A.CallTo(() => sinReconocer.MarcarReconocido(A<string>._, A<int?>._)).MustNotHaveHappened();
        }

        #endregion

        #region El endpoint

        private static IPrincipal Usuario(string grupo) =>
            new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Manuel"), new[] { "NUEVAVISION\\" + grupo });

        private ComprasAgenciasEntradaController Controlador(string grupo) =>
            new ComprasAgenciasEntradaController(repositorio, sinReconocer: sinReconocer) { User = Usuario(grupo) };

        [TestMethod]
        public async Task GetSinReconocer_Compras_DevuelveLosApuntados()
        {
            apuntados.Add(new CorreoSinReconocerFila { Id = 1, MensajeId = "<a@b>", Agencia = "SEUR", Asunto = "Novedades", Recibido = AHORA });

            var r = await Controlador("Compras").GetCorreosSinReconocer() as OkNegotiatedContentResult<List<CorreoSinReconocerFila>>;

            Assert.IsNotNull(r);
            Assert.AreEqual("SEUR", r.Content.Single().Agencia);
        }

        [TestMethod]
        public async Task GetSinReconocer_SinPermiso403_SinTabla503_DiasFuera400()
        {
            var sinPermiso = await Controlador("Almacén").GetCorreosSinReconocer() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.Forbidden, sinPermiso.StatusCode);

            Assert.IsInstanceOfType(await Controlador("Compras").GetCorreosSinReconocer(0), typeof(BadRequestErrorMessageResult));

            A.CallTo(() => sinReconocer.Disponible()).Returns(false);
            var sinTabla = await Controlador("Compras").GetCorreosSinReconocer() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, sinTabla.StatusCode);
        }

        #endregion
    }
}
