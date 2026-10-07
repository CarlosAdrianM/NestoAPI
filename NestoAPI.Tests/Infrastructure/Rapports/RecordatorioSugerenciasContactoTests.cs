using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#603 (corte 5): a quién se recuerda la lista de sugerencias, con qué texto y cada cuánto.</summary>
    [TestClass]
    public class RecordatorioSugerenciasContactoTests
    {
        // Miércoles: los 7 laborables anteriores empiezan el lunes 28/09.
        private static readonly DateTime AHORA = new DateTime(2026, 10, 7, 9, 30, 0);
        private static readonly DateTime DESDE = new DateTime(2026, 9, 28);

        private IRepositorioRecordatorioSugerencias repositorio;
        private IServicioNotificacionesPush notificaciones;
        private string lista;
        private Dictionary<string, int> atendidas;
        private Dictionary<string, DateTime?> ultimoAviso;
        private Dictionary<string, List<string>> usuarios;
        private PendientesVendedor pendientes;
        private HashSet<string> sinActividad;
        private DateTime? desdeActividad;

        [TestInitialize]
        public void Preparar()
        {
            lista = "MPP,LHY";
            atendidas = new Dictionary<string, int> { ["MPP"] = 0, ["LHY"] = 0, ["JE"] = 0 };
            ultimoAviso = new Dictionary<string, DateTime?>();
            usuarios = new Dictionary<string, List<string>>
            {
                ["MPP"] = new List<string> { "Mariajose" },
                ["LHY"] = new List<string> { "Lidia" },
                ["JE"] = new List<string> { "Jefe" }
            };
            pendientes = new PendientesVendedor { Maxima = 0, Alta = 23, Media = 40, Baja = 10 };

            repositorio = A.Fake<IRepositorioRecordatorioSugerencias>();
            A.CallTo(() => repositorio.LeerListaAvisar()).ReturnsLazily(() => Task.FromResult(lista));
            A.CallTo(() => repositorio.LeerUsuarios(A<string>._)).ReturnsLazily((string v) => Task.FromResult(usuarios.TryGetValue(v, out List<string> u) ? u : new List<string>()));
            A.CallTo(() => repositorio.ContarAtendidas(A<string>._, A<DateTime>._)).ReturnsLazily((string v, DateTime d) => Task.FromResult(atendidas.TryGetValue(v, out int n) ? n : 0));
            A.CallTo(() => repositorio.LeerUltimoAviso(A<string>._)).ReturnsLazily((string v) => Task.FromResult(ultimoAviso.TryGetValue(v, out DateTime? f) ? f : null));
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(pendientes));
            sinActividad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            A.CallTo(() => repositorio.FiltrarUsuariosActivos(A<IEnumerable<string>>._, A<DateTime>._))
                .ReturnsLazily((IEnumerable<string> u, DateTime d) => { desdeActividad = d; return Task.FromResult(u.Where(x => !sinActividad.Contains(x)).ToList()); });
            notificaciones = A.Fake<IServicioNotificacionesPush>();
        }

        private RecordatorioSugerenciasContacto Recordatorio() =>
            new RecordatorioSugerenciasContacto(repositorio, notificaciones, () => AHORA, d => true);

        private static ResultadoRecordatorioSugerenciasDTO De(List<ResultadoRecordatorioSugerenciasDTO> resultados, string vendedor) =>
            resultados.Single(r => r.Vendedor == vendedor);

        [TestMethod]
        public async Task NoUsaLaLista_RecibeElAvisoEnLaCampanaYQuedaRegistrado()
        {
            atendidas["LHY"] = 4;

            List<ResultadoRecordatorioSugerenciasDTO> resultados = await Recordatorio().Ejecutar(soloListar: false);

            ResultadoRecordatorioSugerenciasDTO mpp = De(resultados, "MPP");
            Assert.AreEqual(EstadosRecordatorioSugerencias.AVISADO, mpp.Estado);
            Assert.AreEqual(DESDE, mpp.Desde);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Mariajose", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == RecordatorioSugerenciasContacto.TIPO_NOTIFICACION
                    && n.Cuerpo.StartsWith("Tienes 23 clientes de prioridad Alta esperando en Rapports › Clientes para contactar"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.GuardarAviso("MPP", A<IEnumerable<string>>.That.Contains("Mariajose"), AHORA, mpp.Texto)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task UsaLaLista_NoRecibeNada()
        {
            atendidas["LHY"] = 1;

            List<ResultadoRecordatorioSugerenciasDTO> resultados = await Recordatorio().Ejecutar(soloListar: false);

            Assert.AreEqual(EstadosRecordatorioSugerencias.USA_LA_LISTA, De(resultados, "LHY").Estado);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Lidia", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarAviso("LHY", A<IEnumerable<string>>._, A<DateTime>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.ContarAtendidas("LHY", DESDE)).MustHaveHappened();
        }

        [TestMethod]
        public async Task FueraDeLaLista_NiSeMira()
        {
            List<ResultadoRecordatorioSugerenciasDTO> resultados = await Recordatorio().Ejecutar(soloListar: false);

            Assert.IsFalse(resultados.Any(r => r.Vendedor == "JE"));
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Jefe", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ListaVacia_ANadie()
        {
            lista = " ";

            Assert.AreEqual(0, (await Recordatorio().Ejecutar(soloListar: false)).Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AvisadoHace3Dias_NoSeRepite()
        {
            ultimoAviso["MPP"] = AHORA.AddDays(-3);

            List<ResultadoRecordatorioSugerenciasDTO> resultados = await Recordatorio().Ejecutar(soloListar: false);

            Assert.AreEqual(EstadosRecordatorioSugerencias.YA_AVISADO, De(resultados, "MPP").Estado);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Mariajose", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AvisadoHaceUnaSemana_SeLeVuelveAAvisar()
        {
            ultimoAviso["MPP"] = AHORA.AddDays(-7).AddHours(1);

            Assert.AreEqual(EstadosRecordatorioSugerencias.AVISADO, De(await Recordatorio().Ejecutar(soloListar: false), "MPP").Estado);
        }

        [TestMethod]
        public async Task SoloListar_DiceAQuienYConQueTextoSinMandarNiRegistrar()
        {
            List<ResultadoRecordatorioSugerenciasDTO> resultados = await Recordatorio().Ejecutar(soloListar: true);

            ResultadoRecordatorioSugerenciasDTO mpp = De(resultados, "MPP");
            Assert.AreEqual(EstadosRecordatorioSugerencias.POR_AVISAR, mpp.Estado);
            StringAssert.StartsWith(mpp.Texto, "Tienes 23 clientes de prioridad Alta");
            CollectionAssert.AreEqual(new[] { "Mariajose" }, mpp.Usuarios);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarAviso(A<string>._, A<IEnumerable<string>>._, A<DateTime>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinUsuarioOSinPendientes_NoSeAvisa()
        {
            usuarios["LHY"] = new List<string>();
            pendientes = new PendientesVendedor();

            List<ResultadoRecordatorioSugerenciasDTO> resultados = await Recordatorio().Ejecutar(soloListar: false);

            Assert.AreEqual(EstadosRecordatorioSugerencias.SIN_USUARIO, De(resultados, "LHY").Estado);
            Assert.AreEqual(EstadosRecordatorioSugerencias.SIN_PENDIENTES, De(resultados, "MPP").Estado);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task VariosUsuariosConElMismoVendedor_AvisaATodos()
        {
            usuarios["MPP"] = new List<string> { "Mariajose", "NUEVAVISION\\Ayudante" };

            _ = await Recordatorio().Ejecutar(soloListar: false);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Mariajose", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Ayudante", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DosUsuariosDelMismoVendedor_SoloSeAvisaAlQueTieneRapportsEn90Dias()
        {
            // Elena ya no está en la empresa, pero su usuario sigue con Vendedor = PA.
            lista = "PA";
            usuarios["PA"] = new List<string> { "Paloma", "Elena" };
            sinActividad.Add("Elena");

            ResultadoRecordatorioSugerenciasDTO pa = De(await Recordatorio().Ejecutar(soloListar: false), "PA");

            Assert.AreEqual(EstadosRecordatorioSugerencias.AVISADO, pa.Estado);
            CollectionAssert.AreEqual(new[] { "Paloma" }, pa.Usuarios);
            CollectionAssert.AreEqual(new[] { "Elena" }, pa.UsuariosInactivos);
            Assert.AreEqual(AHORA.Date.AddDays(-90), desdeActividad);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Paloma", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Elena", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarAviso("PA", A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "Paloma" }), A<DateTime>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task NingunUsuarioConActividadReciente_SinUsuarioActivoYNoSeAvisa()
        {
            sinActividad.Add("Mariajose");

            ResultadoRecordatorioSugerenciasDTO mpp = De(await Recordatorio().Ejecutar(soloListar: false), "MPP");

            Assert.AreEqual(EstadosRecordatorioSugerencias.SIN_USUARIO_ACTIVO, mpp.Estado);
            CollectionAssert.AreEqual(new[] { "Mariajose" }, mpp.UsuariosInactivos);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Mariajose", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarAviso("MPP", A<IEnumerable<string>>._, A<DateTime>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Texto_ConElDatoConcretoDeSuCartera()
        {
            Assert.AreEqual("Tienes 5 clientes de prioridad Máxima y 23 de prioridad Alta esperando en Rapports › Clientes para contactar. La lista te dice a quién llamar primero y por qué.",
                RecordatorioSugerenciasContacto.Texto(new PendientesVendedor { Maxima = 5, Alta = 23, Media = 3 }));
            StringAssert.StartsWith(RecordatorioSugerenciasContacto.Texto(new PendientesVendedor { Maxima = 1 }), "Tienes 1 cliente de prioridad Máxima esperando");
            StringAssert.StartsWith(RecordatorioSugerenciasContacto.Texto(new PendientesVendedor { Media = 30, Baja = 12 }), "Tienes 42 clientes de tu cartera esperando");
            Assert.IsNull(RecordatorioSugerenciasContacto.Texto(new PendientesVendedor()));
        }

        [TestMethod]
        public void InicioVentana_SieteLaborablesAntesDeHoySaltandoFestivos()
        {
            Assert.AreEqual(DESDE, RecordatorioSugerenciasContacto.InicioVentana(AHORA.Date, d => true));
            Assert.AreEqual(new DateTime(2026, 9, 25), RecordatorioSugerenciasContacto.InicioVentana(AHORA.Date, d => d != new DateTime(2026, 10, 1)));
        }

        [TestMethod]
        public void Lista_MayusculasSinRepetirYCeroEsNadie()
        {
            CollectionAssert.AreEqual(new[] { "MPP", "LHY", "PA" }, RecordatorioSugerenciasContacto.Lista("mpp, LHY,PA,MPP"));
            Assert.AreEqual(0, RecordatorioSugerenciasContacto.Lista("0").Count);
            Assert.AreEqual(0, RecordatorioSugerenciasContacto.Lista(null).Count);
        }

        // ---------------- Endpoint ----------------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestMethod]
        public async Task PostRecordar_SinPermiso_403()
        {
            IRecordatorioSugerenciasContacto recordatorio = A.Fake<IRecordatorioSugerenciasContacto>();
            var controlador = new SugerenciasContactoController(A.Fake<NVEntities>(), A.Fake<IServicioSugerenciasContacto>(), recordatorio)
            {
                Request = new HttpRequestMessage(),
                User = Usuario("NUEVAVISION\\Mariajose", "Ventas")
            };

            var resultado = await controlador.PostRecordar() as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => recordatorio.Ejecutar(A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostRecordar_PorDefectoSoloLista()
        {
            IRecordatorioSugerenciasContacto recordatorio = A.Fake<IRecordatorioSugerenciasContacto>();
            A.CallTo(() => recordatorio.Ejecutar(A<bool>._)).Returns(Task.FromResult(new List<ResultadoRecordatorioSugerenciasDTO>()));
            var controlador = new SugerenciasContactoController(A.Fake<NVEntities>(), A.Fake<IServicioSugerenciasContacto>(), recordatorio)
            {
                Request = new HttpRequestMessage(),
                User = Usuario("NUEVAVISION\\Carlos", "Dirección")
            };

            Assert.IsInstanceOfType(await controlador.PostRecordar(), typeof(OkNegotiatedContentResult<List<ResultadoRecordatorioSugerenciasDTO>>));
            A.CallTo(() => recordatorio.Ejecutar(true)).MustHaveHappenedOnceExactly();
        }
    }
}
