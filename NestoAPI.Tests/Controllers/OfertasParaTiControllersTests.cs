using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class OfertasParaTiControllersTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);

        private static OfertasClienteController Ofertas(IServicioOfertasCliente servicio, params Claim[] claims)
            => new OfertasClienteController(servicio)
            {
                RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT")) },
                Hoy = () => HOY
            };

        [TestMethod]
        public async Task Ofertas_SinClaimCliente_401()
        {
            IServicioOfertasCliente servicio = A.Fake<IServicioOfertasCliente>();
            IHttpActionResult r = await Ofertas(servicio, new Claim("IsEmployee", "true")).GetOfertas();

            Assert.IsInstanceOfType(r, typeof(UnauthorizedResult));
            A.CallTo(() => servicio.Calcular(A<string>._, A<string>._, A<int?>._, A<PoliticaPreciosOcultos.NivelPrecios>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ofertas_ElClienteSaleDelTokenYElNivelDePrecios()
        {
            IServicioOfertasCliente servicio = A.Fake<IServicioOfertasCliente>();
            var respuesta = new OfertasParaTiRespuestaDTO { HayOfertas = false, Campana = "Black Friday 2026" };
            A.CallTo(() => servicio.Calcular("15191", "Black Friday 2026", 4, PoliticaPreciosOcultos.NivelPrecios.SinDescuentos, HOY)).Returns(respuesta);

            IHttpActionResult r = await Ofertas(servicio, new Claim("cliente", " 15191 "),
                new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "SinDescuentos")).GetOfertas("Black Friday 2026", 4);

            Assert.AreSame(respuesta, ((OkNegotiatedContentResult<OfertasParaTiRespuestaDTO>)r).Content);
        }

        [TestMethod]
        public void Ofertas_Contrato_NombresDeCampo()
        {
            var dto = new OfertasParaTiRespuestaDTO
            {
                HayOfertas = true,
                Campana = "Black Friday 2026",
                Titulo = "Black Friday",
                FechaHasta = "2026-11-30",
                TextoHasta = "Hasta el lunes 30 de noviembre",
                Ofertas = new List<OfertaParaTiDTO>
                {
                    new OfertaParaTiDTO { Producto = "45929", Nombre = "Sicurezza BB Cream", Precio = 8.75m, PrecioAntes = 12.5m, Descuento = 0.3m,
                        CantidadSugerida = 2, Motivo = MotivosOfertaParaTi.TE_TOCA, Texto = "t" }
                }
            };
            JObject json = JObject.Parse(JsonConvert.SerializeObject(dto));
            foreach (string campo in new[] { "HayOfertas", "Campana", "Titulo", "FechaHasta", "TextoHasta", "PrecioOculto", "DescuentoOculto", "Ofertas" })
            {
                Assert.IsNotNull(json[campo], campo);
            }
            JObject oferta = (JObject)json["Ofertas"][0];
            foreach (string campo in new[] { "Producto", "Nombre", "UrlFoto", "Familia", "Campana", "PrecioAntes", "Precio", "Descuento", "CantidadSugerida", "Motivo", "Texto" })
            {
                Assert.IsTrue(oferta.ContainsKey(campo), campo);
            }
            Assert.AreEqual("TeToca", (string)oferta["Motivo"]);
            Assert.AreEqual("2026-11-30", (string)json["FechaHasta"]);
        }

        // ---------------------------------------------------------------- push

        private static CampanasPushController Push(ServicioPushCampana servicio, params string[] roles)
            => new CampanasPushController(servicio)
            {
                RequestContext = new HttpRequestContext { Principal = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Carlos", "Bearer"), roles) },
                Ahora = () => new DateTime(2026, 11, 26, 10, 0, 0)
            };

        [TestMethod]
        public async Task Push_SinPermiso_403DeUsted()
        {
            IHttpActionResult r = await Push(null, "Administración").PostPush(new PeticionCampanaPushDTO { Campana = "Black Friday 2026" });

            var contenido = (NegotiatedContentResult<string>)r;
            Assert.AreEqual(HttpStatusCode.Forbidden, contenido.StatusCode);
            Assert.AreEqual(ReglaPushCampana.MENSAJE_SIN_PERMISO, contenido.Content);
        }

        [TestMethod]
        public async Task Push_PeticionMal_400()
        {
            var nucleo = new ServicioOfertasParaTi(A.Fake<ILectorOfertasParaTi>(), A.Fake<IPuntuadorReposicion>());
            var servicio = new ServicioPushCampana(nucleo, A.Fake<IFuentePushCampana>(), A.Fake<IServicioNotificacionesPush>(), p => Task.FromResult<string>(null));

            IHttpActionResult r = await Push(servicio, "Dirección").PostPush(new PeticionCampanaPushDTO());

            Assert.AreEqual(ReglaPushCampana.MENSAJE_SIN_CAMPANA, ((BadRequestErrorMessageResult)r).Message);
        }

        [TestMethod]
        public async Task Push_EnsayoPorDefecto_200SinEnviar()
        {
            ILectorOfertasParaTi lector = A.Fake<ILectorOfertasParaTi>();
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<FilaCampanaOferta>
            {
                new FilaCampanaOferta { Campana = "Black Friday 2026", Producto = "1", Descuento = 0.3m, AudienciaOferta = 1, Estado = 0, Nombre = "UNO",
                    FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30) }
            });
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._)).Returns(new Dictionary<string, int> { { "1", 3 } });
            IPuntuadorReposicion puntuador = A.Fake<IPuntuadorReposicion>();
            A.CallTo(() => puntuador.ModeloDisponible()).Returns(null);
            IFuentePushCampana fuente = A.Fake<IFuentePushCampana>();
            A.CallTo(() => fuente.ClientesConConsejos()).Returns(new List<string> { "1000" });
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var servicio = new ServicioPushCampana(new ServicioOfertasParaTi(lector, puntuador), fuente, push, p => Task.FromResult<string>(null));

            IHttpActionResult r = await Push(servicio, "Informática").PostPush(new PeticionCampanaPushDTO { Campana = "Black Friday 2026" });

            RespuestaCampanaPushDTO respuesta = ((OkNegotiatedContentResult<RespuestaCampanaPushDTO>)r).Content;
            Assert.IsTrue(respuesta.Ensayo);
            Assert.AreEqual("Uno (−30 %), pensado para ti.", respuesta.Mensajes.Single().Cuerpo);
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NestoAPI.Models.NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => fuente.Anotar(A<string>._, A<string>._)).MustNotHaveHappened();
            JObject json = JObject.Parse(JsonConvert.SerializeObject(respuesta));
            foreach (string campo in new[] { "Ensayo", "Campana", "Destinatarios", "ConOfertas", "SinOfertas", "Saltados", "Mensajes" })
            {
                Assert.IsNotNull(json[campo], campo);
            }
        }
    }
}
