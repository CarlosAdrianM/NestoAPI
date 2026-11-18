using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857e: boletines y remitentes ignorados. Falso positivo del 18/11/26: el boletín «Perfect Beauty News: Quies Warm» de
    /// comunicaciones@perfectbeauty.es, captado por el genérico solo por su enlace de rastreo de clics (track.perfectbeauty.es/click.php?L=…&amp;J=…).
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoBoletinesTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 18, 12, 0, 0);

        internal static CorreoCompras PerfectBeauty(string remitente = "comunicaciones@perfectbeauty.es", bool conBaja = true) => new CorreoCompras
        {
            Id = "graph-pb",
            MensajeId = "<news-quies-warm@perfectbeauty.es>",
            Recibido = AHORA.AddHours(-1),
            RemitenteDireccion = remitente,
            RemitenteNombre = "Perfect Beauty",
            Asunto = "Perfect Beauty News: Quies Warm",
            Cuerpo = "<p>Perfect Beauty presenta una manta eléctrica para camilla, ideal para el invierno.</p>" +
                "<a href=\"https://track.perfectbeauty.es/click.php?L=8a7sd6f5&amp;J=12345&amp;C=99\">Ver la Quies Warm</a>" +
                (conBaja ? "<p><a href=\"https://track.perfectbeauty.es/unsub.php?L=8a7sd6f5\">Darse de baja</a></p>" : string.Empty),
            CuerpoEsHtml = true
        };

        private static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo, params RemitenteMarcado[] marcados) =>
            AnalizadorCorreoSeguimiento.Analizar(correo, AgenciasEntradaRegistradas.Todas, marcados);

        #region Regla pura: redirectores de clic y boletines

        [DataTestMethod]
        [DataRow("https://track.perfectbeauty.es/click.php?L=8a7sd6f5&J=12345", true)]
        [DataRow("https://u123.ct.sendgrid.net/ls/click?upn=abc", true)]
        [DataRow("https://ejemplo.us21.list-manage.com/track/click?u=1&id=2", true)]
        [DataRow("https://news.ejemplo.es/r/?L=1&J=2", true)]
        [DataRow("https://links.ejemplo.es/track/click/ABC123456", true)]
        [DataRow("https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=61770001234", false)]
        [DataRow("https://portal.transportes-inventados.es/tracking?expedicion=TI2026111201", false)]
        [DataRow("https://www.mbe.es/es/tracking?c=1Z21E7X76890915153", false)]
        [DataRow("https://www.ups.com/track?tracknum=1Z21E7X76890915153", false)]
        public void Redirector_DeMailingSiDeEnvioNo(string url, bool esRedirector)
        {
            Assert.AreEqual(esRedirector, RedirectorMailing.Es(url));
        }

        [DataTestMethod]
        [DataRow("comunicaciones@perfectbeauty.es", "Nada", true)]
        [DataRow("newsletter@ejemplo.es", "Nada", true)]
        [DataRow("ventas@ejemplo.es", "Si no quiere recibir más correos, puede darse de baja aquí.", true)]
        [DataRow("ventas@ejemplo.es", "Click here to unsubscribe", true)]
        [DataRow("ventas@ejemplo.es", "¿No lo ve bien? Ver en el navegador", true)]
        [DataRow("ventas@ejemplo.es", "Su pedido ha salido hoy. Un saludo.", false)]
        public void Boletin_PorRemitenteOTexto(string remitente, string texto, bool esBoletin)
        {
            var correo = new CorreoCompras { RemitenteDireccion = remitente, Asunto = "Asunto", Cuerpo = texto, CuerpoEsHtml = false };
            Assert.AreEqual(esBoletin, BoletinComercial.Es(correo));
        }

        #endregion

        #region Analizador

        [TestMethod]
        public void PerfectBeauty_ElBoletinDeLosClics_NoSeGuarda()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(PerfectBeauty());

            Assert.IsFalse(analisis.Guardar);
            Assert.IsFalse(analisis.Seguimientos.Any());
        }

        [TestMethod]
        public void RedirectorDeClics_DeUnRemitenteCualquiera_TampocoEsSeguimiento()
        {
            Assert.IsFalse(Analizar(PerfectBeauty("ventas@perfectbeauty.es", conBaja: false)).Guardar);
        }

        [TestMethod]
        public void Boletin_ConCodigoGenericoConEtiqueta_NoSeGuarda()
        {
            CorreoCompras correo = PerfectBeauty();
            correo.Cuerpo = "<p>Nº de seguimiento: AB12345678</p><p>Darse de baja</p>";
            AnalisisCorreoSeguimiento analisis = Analizar(correo);

            Assert.IsFalse(analisis.Guardar);
            Assert.IsTrue(analisis.DescartadoPorBoletin);
        }

        [TestMethod]
        public void Boletin_ConSeguimientoDeUnaAgenciaRegistrada_SiSeGuarda()
        {
            CorreoCompras correo = PerfectBeauty();
            correo.Cuerpo += "<p>Su pedido va con UPS: 1Z21E7X76890915153</p>";
            AnalisisCorreoSeguimiento analisis = Analizar(correo);

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("UPS", analisis.Agencia);
        }

        [TestMethod]
        public void Boletin_DeUnRemitenteMarcado_SiSeGuarda()
        {
            CorreoCompras correo = PerfectBeauty();
            correo.Cuerpo = "<p>Nº de seguimiento: AB12345678</p><p>Darse de baja</p>";
            AnalisisCorreoSeguimiento analisis = Analizar(correo, new RemitenteMarcado { Remitente = "perfectbeauty.es", Agencia = "Transportes PB", Activo = true });

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("AB12345678", analisis.Seguimientos.Single().Codigo);
        }

        [TestMethod]
        public void Generico_SinBoletin_SigueCaptando()
        {
            var correo = new CorreoCompras
            {
                Id = "x", MensajeId = "<x@ejemplo.test>", Recibido = AHORA, RemitenteDireccion = "trafico@transportes-inventados.es", Asunto = "Su envío",
                Cuerpo = "<p>Sale hoy. <a href=\"https://portal.transportes-inventados.es/tracking?expedicion=TI2026111201\">Seguimiento</a></p>", CuerpoEsHtml = true
            };
            Assert.AreEqual("TI2026111201", Analizar(correo).Seguimientos.Single().Codigo);
        }

        [TestMethod]
        public void RemitenteIgnorado_NoSeGuardaTraigaLoQueTraiga()
        {
            CorreoCompras correo = PerfectBeauty();
            correo.Cuerpo += "<p>Su pedido va con UPS: 1Z21E7X76890915153</p>";
            AnalisisCorreoSeguimiento analisis = Analizar(correo,
                new RemitenteMarcado { Remitente = "comunicaciones@perfectbeauty.es", Activo = true, Ignorar = true });

            Assert.IsFalse(analisis.Guardar);
            Assert.IsTrue(analisis.Ignorado);
        }

        [TestMethod]
        public void RemitenteIgnoradoDadoDeBaja_YaNoIgnora()
        {
            CorreoCompras correo = PerfectBeauty();
            correo.Cuerpo += "<p>Su pedido va con UPS: 1Z21E7X76890915153</p>";
            Assert.IsTrue(Analizar(correo, new RemitenteMarcado { Remitente = "comunicaciones@perfectbeauty.es", Activo = false, Ignorar = true }).Guardar);
        }

        [DataTestMethod]
        [DataRow(" Comunicaciones@PerfectBeauty.es ", "comunicaciones@perfectbeauty.es")]
        [DataRow("alguien@nuevavision.es", null)]
        [DataRow("perfectbeauty.es", null)]
        [DataRow(null, null)]
        public void ReglaIgnorarRemitente_LaDireccionExactaNuncaNuestra(string direccion, string esperado)
        {
            Assert.AreEqual(esperado, ReglaIgnorarRemitente.Remitente(direccion));
        }

        #endregion

        #region Job, descartar e ignorar

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private List<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)> reenviados;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            reenviados = new List<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>();
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
        }

        private async Task Pasada(params CorreoCompras[] correos)
        {
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(new LecturaBuzonCompras { Correos = correos.ToList() });
            IReenviadorCorreosSeguimiento reenviador = A.Fake<IReenviadorCorreosSeguimiento>();
            A.CallTo(() => reenviador.Reenviar(A<IEnumerable<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>>._))
                .ReturnsLazily((IEnumerable<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)> l) => { reenviados.AddRange(l); return l.Count(); });
            var aplicacion = new ServicioAplicacionCorreosCompras(repositorio, () => true, A.Fake<IAvisadorCorreosPorAsignar>(), (a, b, c, d) => { }, () => AHORA);
            _ = await new ServicioCorreosSeguimientoCompras(lector, repositorio, aplicacion: aplicacion, reenviador: reenviador,
                horaCorte: () => new TimeSpan(11, 0, 0), esFestivo: _ => false).Pasada(AHORA);
        }

        [TestMethod]
        public async Task Job_ElBoletin_NiSeGuardaNiSeReenvia()
        {
            await Pasada(PerfectBeauty());

            Assert.AreEqual(0, repositorio.Correos.Count);
            Assert.AreEqual(0, reenviados.Count);
        }

        private async Task<int> GuardadoDeUnaAgenciaGenerica()
        {
            var correo = new CorreoCompras
            {
                Id = "g1", MensajeId = "<g1@ejemplo.test>", Recibido = AHORA.AddHours(-1), RemitenteDireccion = "avisos@tienda-inventada.es", Asunto = "Novedades",
                Cuerpo = "Nº de seguimiento: AB12345678", CuerpoEsHtml = false
            };
            await Pasada(correo);
            return repositorio.Correos.Single().Id;
        }

        [TestMethod]
        public async Task Descartar_ConIgnorarRemitente_LoMarcaYYaNoSeGuardaLoSiguiente()
        {
            int id = await GuardadoDeUnaAgenciaGenerica();
            var servicio = new ServicioAplicacionCorreosCompras(repositorio);

            ResultadoOperacionCorreoCompra resultado = await servicio.Descartar(id, "Carlos", ignorarRemitente: true);

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, resultado.Codigo);
            Assert.AreEqual("avisos@tienda-inventada.es", resultado.RemitenteIgnorado);
            StringAssert.Contains(resultado.Mensaje, "no se mirarán los correos de avisos@tienda-inventada.es");
            Assert.AreEqual(("avisos@tienda-inventada.es", "Carlos", id), repositorio.Ignorados.Single());
            Assert.IsTrue(repositorio.Correo(id).Descartado == true);

            reenviados.Clear();
            var otro = new CorreoCompras
            {
                Id = "g2", MensajeId = "<g2@ejemplo.test>", Recibido = AHORA, RemitenteDireccion = "avisos@tienda-inventada.es", Asunto = "Más novedades",
                Cuerpo = "Nº de seguimiento: CD87654321", CuerpoEsHtml = false
            };
            await Pasada(otro);
            Assert.AreEqual(1, repositorio.Correos.Count, "El siguiente correo del remitente ignorado no se guarda");
            Assert.AreEqual(0, reenviados.Count);
        }

        [TestMethod]
        public async Task Descartar_SinIgnorar_ComoHoy()
        {
            int id = await GuardadoDeUnaAgenciaGenerica();

            ResultadoOperacionCorreoCompra resultado = await new ServicioAplicacionCorreosCompras(repositorio).Descartar(id, "Carlos");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, resultado.Codigo);
            Assert.IsNull(resultado.RemitenteIgnorado);
            Assert.AreEqual(0, repositorio.Ignorados.Count);
        }

        [TestMethod]
        public async Task Endpoint_Descartar_ConIgnorarRemitente_DevuelveElRemitente()
        {
            int id = await GuardadoDeUnaAgenciaGenerica();
            var controller = new ComprasAgenciasEntradaController(repositorio, crearAplicacion: () => new ServicioAplicacionCorreosCompras(repositorio))
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), new[] { "Compras" }),
                Request = new HttpRequestMessage()
            };

            var resultado = await controller.PostDescartar(id, ignorarRemitente: true) as OkNegotiatedContentResult<RespuestaDescarteCorreoCompraDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("avisos@tienda-inventada.es", resultado.Content.RemitenteIgnorado);
        }

        [TestMethod]
        public async Task Endpoint_PostRemitenteIgnorado_SeGuardaConLaMarca()
        {
            IRepositorioCorreosSeguimiento repo = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repo.TablasExisten()).Returns(true);
            A.CallTo(() => repo.AvisosProveedorDisponibles()).Returns(true);
            A.CallTo(() => repo.GuardarRemitente(null, "comunicaciones@perfectbeauty.es", A<string>._, null, false, true)).Returns(9);
            var controller = new ComprasAgenciasEntradaController(repo)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), new[] { "Compras" }),
                Request = new HttpRequestMessage()
            };

            var resultado = await controller.PostRemitente(new PeticionRemitenteAgenciaDTO { Remitente = "comunicaciones@perfectbeauty.es", Ignorar = true })
                as OkNegotiatedContentResult<RemitenteMarcado>;

            Assert.AreEqual(9, resultado.Content.Id);
            Assert.IsTrue(resultado.Content.Ignorar);
        }

        #endregion
    }
}
