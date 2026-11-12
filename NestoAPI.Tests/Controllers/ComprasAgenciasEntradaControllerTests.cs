using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#857: api/Compras/AgenciasEntrada (remitentes marcados, candidatos y correos captados).</summary>
    [TestClass]
    public class ComprasAgenciasEntradaControllerTests
    {
        private IRepositorioCorreosSeguimiento repositorio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repositorio.TablasExisten()).Returns(true);
        }

        private ComprasAgenciasEntradaController Controller(params string[] roles) => new ComprasAgenciasEntradaController(repositorio)
        {
            User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
            Request = new HttpRequestMessage()
        };

        [TestMethod]
        public async Task SinGrupo_403EnTodos()
        {
            ComprasAgenciasEntradaController c = Controller("Almacén");
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.GetRemitentes()).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.PostRemitente(new PeticionRemitenteAgenciaDTO { Agencia = "GLS", Remitente = "x.es" })).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.DeleteRemitente(1)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.GetCandidatos()).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.GetCorreos()).StatusCode);
            A.CallTo(() => repositorio.GuardarRemitente(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Post_DeCompras_NormalizaYUsaElNombreDeLaAgenciaConocida()
        {
            A.CallTo(() => repositorio.GuardarRemitente("GLS", "avisos.ejemplo-agencia.es", A<string>._)).Returns(12);

            var resultado = await Controller("Compras").PostRemitente(new PeticionRemitenteAgenciaDTO { Agencia = " gls ", Remitente = " @Avisos.Ejemplo-Agencia.ES " })
                as OkNegotiatedContentResult<RemitenteMarcado>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(12, resultado.Content.Id);
            Assert.AreEqual("GLS", resultado.Content.Agencia);
            Assert.AreEqual("avisos.ejemplo-agencia.es", resultado.Content.Remitente);
        }

        [DataTestMethod]
        [DataRow("no es un remitente")]
        [DataRow("")]
        [DataRow("alguien@nuevavision.es")]
        [DataRow("gmail.com")]
        public async Task Post_RemitenteNoValido_400(string remitente)
        {
            Assert.IsInstanceOfType(await Controller("Dirección").PostRemitente(new PeticionRemitenteAgenciaDTO { Agencia = "X", Remitente = remitente }),
                typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Post_UnaDireccionDeGmail_SiVale()
        {
            Assert.IsInstanceOfType(await Controller("Informática").PostRemitente(new PeticionRemitenteAgenciaDTO { Agencia = "Transportista de prueba", Remitente = "transportista.prueba@gmail.com" }),
                typeof(OkNegotiatedContentResult<RemitenteMarcado>));
        }

        [TestMethod]
        public async Task Post_SinTabla_409()
        {
            A.CallTo(() => repositorio.TablasExisten()).Returns(false);
            var resultado = await Controller("Compras").PostRemitente(new PeticionRemitenteAgenciaDTO { Agencia = "GLS", Remitente = "ejemplo.es" }) as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
        }

        [TestMethod]
        public async Task Delete_QueNoExiste_404()
        {
            A.CallTo(() => repositorio.DesactivarRemitente(99, A<string>._)).Returns(false);
            Assert.IsInstanceOfType(await Controller("Compras").DeleteRemitente(99), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Candidatos_LosRemitentesSinMarcarConSusDominios()
        {
            A.CallTo(() => repositorio.LeerCorreos(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>
            {
                new CorreoSeguimientoFila { Recibido = new DateTime(2026, 11, 10), RemitenteDireccion = "trafico@transportes-inventados.es", Asunto = "Entrega 1",
                    Url = "https://portal.transportes-inventados.es/tracking?expedicion=TI1" },
                new CorreoSeguimientoFila { Recibido = new DateTime(2026, 11, 11), RemitenteDireccion = "trafico@transportes-inventados.es", Asunto = "Entrega 2",
                    Url = "https://portal.transportes-inventados.es/tracking?expedicion=TI2" },
                new CorreoSeguimientoFila { Recibido = new DateTime(2026, 11, 11), RemitenteDireccion = "noreply@gls-spain.es", Agencia = "GLS",
                    Url = "https://www.gls-spain.es/x?match=1" }
            });
            A.CallTo(() => repositorio.LeerRemitentes()).Returns(new List<RemitenteMarcado>
            {
                new RemitenteMarcado { Agencia = "GLS", Remitente = "gls-spain.es", Activo = true }
            });

            var resultado = await Controller("Compras").GetCandidatos(30) as OkNegotiatedContentResult<List<CandidatoRemitenteDTO>>;

            CandidatoRemitenteDTO candidato = resultado.Content.Single();
            Assert.AreEqual("trafico@transportes-inventados.es", candidato.Remitente);
            Assert.AreEqual(2, candidato.Correos);
            Assert.AreEqual("Entrega 2", candidato.UltimoAsunto);
            CollectionAssert.AreEqual(new[] { "portal.transportes-inventados.es" }, candidato.DominiosEnlaces);
        }

        [TestMethod]
        public async Task Correos_DevuelveLosCandidatosYaLeidos()
        {
            A.CallTo(() => repositorio.LeerCorreos(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>
            {
                new CorreoSeguimientoFila { Id = 3, Codigo = "C1", Url = "https://ejemplo.test/t?ref=C1",
                    PedidosCandidatos = "[{\"Empresa\":\"1\",\"Pedido\":912001,\"Proveedor\":\"9001\",\"Motivo\":\"Nombre\"}]" }
            });

            var resultado = await Controller("Dirección").GetCorreos(7) as OkNegotiatedContentResult<List<CorreoSeguimientoCompraDTO>>;

            CorreoSeguimientoCompraDTO dto = resultado.Content.Single();
            Assert.AreEqual(912001, dto.PedidosCandidatos.Single().Pedido);
            Assert.AreEqual("C1", dto.Seguimientos.Single().Codigo);
        }

        [TestMethod]
        public async Task Get_SinTabla_ListaVacia()
        {
            A.CallTo(() => repositorio.TablasExisten()).Returns(false);
            var resultado = await Controller("Compras").GetCorreos(7) as OkNegotiatedContentResult<List<CorreoSeguimientoCompraDTO>>;
            Assert.AreEqual(0, resultado.Content.Count);
            A.CallTo(() => repositorio.LeerCorreos(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DiasFueraDeRango_400()
        {
            Assert.IsInstanceOfType(await Controller("Compras").GetCorreos(0), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await Controller("Compras").GetCandidatos(1000), typeof(BadRequestErrorMessageResult));
        }

        #region Fase 3

        [TestMethod]
        public async Task Fase3_SinGrupo_403EnTodos()
        {
            ComprasAgenciasEntradaController c = Controller("Almacén");
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.GetPorAsignar()).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.PostAsignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 })).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.PostDescartar(1)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.GetRemitentesPaquete()).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.DeleteRemitentePaquete(1)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await c.PostReanalizar()).StatusCode);
            A.CallTo(() => repositorio.AplicarFecha(A<PeticionAplicacionFecha>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Fase3_Reanalizar_SoloDireccionOInformatica()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await Controller("Compras").PostReanalizar()).StatusCode);
            Assert.IsInstanceOfType(await Controller("Dirección").PostReanalizar(31), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Fase3_SinScript_503()
        {
            A.CallTo(() => repositorio.AplicacionDisponible()).Returns(false);
            ComprasAgenciasEntradaController c = Controller("Compras");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await c.GetPorAsignar()).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await c.PostAsignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 })).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await c.PostDescartar(1)).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await c.GetRemitentesPaquete()).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)await c.DeleteRemitentePaquete(1)).StatusCode);
            A.CallTo(() => repositorio.AplicarFecha(A<PeticionAplicacionFecha>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Fase3_Asignar_CodigosHttp()
        {
            A.CallTo(() => repositorio.AplicacionDisponible()).Returns(true);
            A.CallTo(() => repositorio.LeerCorreo(1)).Returns(new CorreoSeguimientoFila { Id = 1, Recibido = DateTime.Now, Codigo = "1Z21E7X76890089163" });
            A.CallTo(() => repositorio.LeerCorreo(2)).Returns(Task.FromResult<CorreoSeguimientoFila>(null));
            A.CallTo(() => repositorio.LeerCorreos(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>());
            A.CallTo(() => repositorio.AplicarFecha(A<PeticionAplicacionFecha>.That.Matches(p => p.Pedido == 912001)))
                .Returns(new ResultadoAplicacionFecha { Estado = EstadoAplicacionFecha.Aplicada, LineasCambiadas = 2, Proveedor = "9100" });
            A.CallTo(() => repositorio.AplicarFecha(A<PeticionAplicacionFecha>.That.Matches(p => p.Pedido == 912002)))
                .Returns(new ResultadoAplicacionFecha { Estado = EstadoAplicacionFecha.NoEnElAire });
            A.CallTo(() => repositorio.AplicarFecha(A<PeticionAplicacionFecha>.That.Matches(p => p.Pedido == 912003)))
                .Returns(new ResultadoAplicacionFecha { Estado = EstadoAplicacionFecha.AplicadoAOtro, PedidoAnterior = 912001 });
            ComprasAgenciasEntradaController c = Controller("Compras");

            var ok = await c.PostAsignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 }) as OkNegotiatedContentResult<RespuestaAsignacionCorreoCompraDTO>;
            Assert.AreEqual(2, ok.Content.LineasCambiadas);
            Assert.IsInstanceOfType(await c.PostAsignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912002 }), typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(HttpStatusCode.Conflict, ((NegotiatedContentResult<string>)await c.PostAsignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912003 })).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, ((NegotiatedContentResult<string>)await c.PostAsignar(2, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 })).StatusCode);
        }

        #endregion
    }
}
