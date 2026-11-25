using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Proyectos;
using NestoAPI.Models.Proyectos;
using NestoAPI.Tests.Infrastructure.Proyectos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#901: api/Proyectos (permisos y códigos de estado).</summary>
    [TestClass]
    public class ProyectosControllerTests
    {
        private RepositorioProyectosFalso repositorio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioProyectosFalso();
        }

        private ProyectosController Controlador(params string[] roles) => Controlador(new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Carlos", "Bearer"), roles));

        private ProyectosController Controlador(IPrincipal usuario) =>
            new ProyectosController(new ServicioProyectos(repositorio, () => new DateTime(2026, 11, 25)))
            {
                User = usuario,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };

        private static HttpStatusCode Estado(IHttpActionResult resultado) =>
            resultado.ExecuteAsync(System.Threading.CancellationToken.None).Result.StatusCode;

        private static T Cuerpo<T>(IHttpActionResult resultado) =>
            resultado.ExecuteAsync(System.Threading.CancellationToken.None).Result.Content.ReadAsAsync<T>().Result;

        [TestMethod]
        public void Controlador_PideToken()
        {
            Assert.IsNotNull(typeof(ProyectosController).GetCustomAttribute<AuthorizeAttribute>());
        }

        [DataTestMethod]
        [DataRow("Administración")]
        [DataRow("Dirección")]
        [DataRow("Informática")]
        public async Task Grupos_PuedenVer(string grupo)
        {
            Assert.AreEqual(HttpStatusCode.OK, Estado(await Controlador(grupo).GetProyectos()));
        }

        [TestMethod]
        public async Task OtroGrupo_403EnTodo()
        {
            ProyectosController c = Controlador("Almacén");
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetProyectos()));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetProyecto(1)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.PostProyecto(new ProyectoDTO())));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.PutProyecto(1, new ProyectoDTO())));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.PostCerrar(1)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.DeleteProyecto(1)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetApuntesPorImputar()));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetImputacionesApunte(1)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.PutImputacionesApunte(1, new PeticionImputacionesApunteDTO())));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetCuentaExplotacion(1)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetCuentaExplotacionExcel(1)));
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await c.GetComparativa("1")));
        }

        [TestMethod]
        public async Task ClienteDeLaTienda_403AunqueTengaElGrupo()
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "cliente@x.es"), new Claim("cliente", "15191"), new Claim(ClaimTypes.Role, "Dirección") }, "Bearer");
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await Controlador(new ClaimsPrincipal(identidad)).GetProyectos()));
        }

        [TestMethod]
        public async Task Reabrir_SoloDireccion()
        {
            FilaProyecto p = repositorio.Proyecto("IMPE0210-2025-1", ReglasProyectos.ESTADO_CERRADO);
            IHttpActionResult administracion = await Controlador("Administración").PostReabrir(p.Id);
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(administracion));
            Assert.AreEqual(ProyectosController.MENSAJE_SOLO_DIRECCION, Cuerpo<RespuestaErrorProyectoDTO>(administracion).Message);
            Assert.AreEqual(HttpStatusCode.OK, Estado(await Controlador("Dirección").PostReabrir(p.Id)));
            Assert.AreEqual(ReglasProyectos.ESTADO_ABIERTO, repositorio.Proyectos.Single().Estado);
        }

        [TestMethod]
        public async Task SinTablas_503()
        {
            repositorio.HayTablas = false;
            IHttpActionResult r = await Controlador("Dirección").GetProyectos();
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, Estado(r));
            Assert.AreEqual("SIN_TABLA", Cuerpo<RespuestaErrorProyectoDTO>(r).Codigo);
        }

        [TestMethod]
        public async Task Post_Crea201_DatosMalos400_Repetido409()
        {
            ProyectosController c = Controlador("Administración");
            var dto = new ProyectoDTO { Codigo = "IMPE0210-2026-1", Nombre = "Primera", Tipo = "CursoSEPE", FechaDesde = new DateTime(2026, 2, 1) };
            IHttpActionResult creado = await c.PostProyecto(dto);
            Assert.AreEqual(HttpStatusCode.Created, Estado(creado));
            Assert.AreEqual("IMPE0210-2026-1", Cuerpo<ProyectoDTO>(creado).Codigo);
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(await c.PostProyecto(dto)));
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await c.PostProyecto(new ProyectoDTO { Codigo = "X" })));
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await c.PostProyecto(null)));
        }

        [TestMethod]
        public async Task Get_NoExiste404()
        {
            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await Controlador("Dirección").GetProyecto(42)));
            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await Controlador("Dirección").GetCuentaExplotacion(42)));
            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await Controlador("Dirección").GetImputacionesApunte(42)));
        }

        [TestMethod]
        public async Task Put_Cerrado409_Delete204YConImputaciones409()
        {
            FilaProyecto cerrado = repositorio.Proyecto("CERRADO", ReglasProyectos.ESTADO_CERRADO);
            FilaProyecto con = repositorio.Proyecto("CON");
            FilaProyecto sin = repositorio.Proyecto("SIN");
            repositorio.Imputar(con, repositorio.Apunte(1, "6230000", 100, 0), 10);
            ProyectosController c = Controlador("Informática");

            IHttpActionResult put = await c.PutProyecto(cerrado.Id, new ProyectoDTO { Codigo = "CERRADO", Nombre = "X", Tipo = "Otro", FechaDesde = new DateTime(2026, 1, 1) });
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(put));
            Assert.AreEqual("PROYECTO_CERRADO", Cuerpo<RespuestaErrorProyectoDTO>(put).Codigo);
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(await c.DeleteProyecto(con.Id)));
            Assert.AreEqual(HttpStatusCode.NoContent, Estado(await c.DeleteProyecto(sin.Id)));
        }

        [TestMethod]
        public async Task PutImputaciones_ApunteCambiado409_PasaDelApunte400_Bien200()
        {
            FilaProyecto p = repositorio.Proyecto("IMPE0210-2026-1");
            repositorio.Apunte(555, "6230000", 120, 0);
            ProyectosController c = Controlador("Administración");

            IHttpActionResult cambiado = await c.PutImputacionesApunte(555, new PeticionImputacionesApunteDTO
            {
                ImporteApunte = 100,
                Imputaciones = new List<ImputacionPedidaDTO> { new ImputacionPedidaDTO { ProyectoId = p.Id, Importe = 50 } }
            });
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(cambiado));
            Assert.AreEqual("APUNTE_CAMBIADO", Cuerpo<RespuestaErrorProyectoDTO>(cambiado).Codigo);

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await c.PutImputacionesApunte(555, new PeticionImputacionesApunteDTO
            {
                Imputaciones = new List<ImputacionPedidaDTO> { new ImputacionPedidaDTO { ProyectoId = p.Id, Importe = 130 } }
            })));
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(await c.PutImputacionesApunte(999, new PeticionImputacionesApunteDTO
            {
                Imputaciones = new List<ImputacionPedidaDTO> { new ImputacionPedidaDTO { ProyectoId = p.Id, Importe = 10 } }
            })));

            IHttpActionResult bien = await c.PutImputacionesApunte(555, new PeticionImputacionesApunteDTO
            {
                ImporteApunte = 120,
                Imputaciones = new List<ImputacionPedidaDTO> { new ImputacionPedidaDTO { ProyectoId = p.Id, Importe = 120 } }
            });
            Assert.AreEqual(HttpStatusCode.OK, Estado(bien));
            Assert.IsTrue(Cuerpo<ImputacionesApunteDTO>(bien).Cambiado);
        }

        [TestMethod]
        public async Task ApuntesPorImputar_CuentaMala400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await Controlador("Dirección").GetApuntesPorImputar(cuenta: "430")));
            Assert.AreEqual(HttpStatusCode.OK, Estado(await Controlador("Dirección").GetApuntesPorImputar(cuenta: "623")));
        }

        [TestMethod]
        public async Task Excel_DevuelveElFichero()
        {
            FilaProyecto p = repositorio.Proyecto("IMPE0210-2026-1");
            HttpResponseMessage respuesta = await (await Controlador("Dirección").GetCuentaExplotacionExcel(p.Id)).ExecuteAsync(System.Threading.CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual(GeneradorExcelCuentaExplotacionProyecto.CONTENT_TYPE, respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("CuentaExplotacion_IMPE0210-2026-1.xlsx", respuesta.Content.Headers.ContentDisposition.FileName);
        }

        [TestMethod]
        public async Task Comparativa_SinNada400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await Controlador("Dirección").GetComparativa()));
        }
    }
}
