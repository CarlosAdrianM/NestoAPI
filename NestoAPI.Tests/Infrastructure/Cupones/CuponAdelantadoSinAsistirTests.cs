using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#910 (fleco de #885, para NestoApp#297): Adelantado/AdelantadoPor/AdelantadoEl en CuponDTO (del rastro «CuponAdelantado» de
    /// Modificaciones, una lectura por petición) y el aviso CuponAdelantadoSinAsistir, una vez, cuando quien lo tenía adelantado no asiste.
    /// </summary>
    [TestClass]
    public class CuponAdelantadoSinAsistirTests
    {
        private const string PRODUCTO = "CUPONEVENTO";
        private const string CODIGO = "EV-2611-7K3Q";
        private static readonly DateTime HOY = new DateTime(2026, 12, 1, 10, 0, 0);
        private static readonly DateTime ADELANTO = new DateTime(2026, 11, 23, 9, 15, 0);

        private IRepositorioCupones repositorio;
        private IAvisadorCuponAdelantadoSinAsistir avisador;
        private CuponLeido cupon;

        [TestInitialize]
        public void Preparar()
        {
            cupon = Cupon();
            repositorio = A.Fake<IRepositorioCupones>();
            A.CallTo(() => repositorio.LeerPorCodigo(CODIGO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerDeInscripcion(101)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerDelCliente(A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(new List<CuponLeido>
            {
                cupon,
                Cupon(codigo: "EV-OTRO", id: 8)
            }));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._))
                .Returns(Task.FromResult(new Dictionary<string, ProductoParaCupon>()));
            A.CallTo(() => repositorio.LeerRastrosAdelanto(A<DateTime>._)).Returns(Task.FromResult(new List<RastroAdelantoCupon>
            {
                Rastro(CODIGO, "NUEVAVISION\\Manuel", ADELANTO),
                Rastro("EV-DE-OTRO-CLIENTE", "NUEVAVISION\\Pilar", ADELANTO)
            }));
            A.CallTo(() => repositorio.AnotarAvisoSinAsistir(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(true));
            avisador = A.Fake<IAvisadorCuponAdelantadoSinAsistir>();
        }

        private static RastroAdelantoCupon Rastro(string codigo, string usuario, DateTime fecha) => new RastroAdelantoCupon
        {
            Nuevo = Newtonsoft.Json.JsonConvert.SerializeObject(new { Codigo = codigo, Importe = 50m, Usuario = usuario, Motivo = "Lo necesita ya" }),
            Usuario = "sa",
            Fecha = fecha
        };

        private static CuponLeido Cupon(string estado = EstadosCupon.ACTIVO, string codigo = CODIGO, int id = 7, string origen = OrigenesCupon.EVENTO) => new CuponLeido
        {
            Id = id,
            Codigo = codigo,
            Empresa = "1",
            Cliente = "40513",
            Contacto = "0",
            Importe = 50,
            Origen = origen,
            EventoInscripcionId = 101,
            EventoId = 1,
            Evento = "Masterclass Rosácea",
            FechaEmision = new DateTime(2026, 11, 10),
            ValidoDesde = new DateTime(2026, 11, 23),
            Caducidad = new DateTime(2027, 5, 30),
            Estado = estado
        };

        private ServicioCupones Servicio(bool conAvisador = true) =>
            new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO, null, null, null, null, null, null,
                conAvisador ? avisador : null);

        private static ClaimsPrincipal Empleado() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Laura") }, "Bearer"));

        [TestMethod]
        public void Adelantos_ElUltimoDeCadaCodigo_ConElUsuarioDelJsonSinDominio()
        {
            var rastros = new List<RastroAdelantoCupon>
            {
                Rastro(CODIGO, "NUEVAVISION\\Manuel", ADELANTO),
                Rastro(CODIGO.ToLowerInvariant(), "NUEVAVISION\\Carlos", ADELANTO.AddDays(1)),
                new RastroAdelantoCupon { Nuevo = "esto no es JSON", Usuario = "x", Fecha = ADELANTO.AddDays(5) },
                new RastroAdelantoCupon { Nuevo = "{\"Codigo\":\"EV-SIN-USUARIO\"}", Usuario = "NUEVAVISION\\Alfredo ", Fecha = ADELANTO }
            };

            Dictionary<string, AdelantoCupon> a = ReglaAdelantarCupon.Adelantos(rastros, new[] { CODIGO, "EV-SIN-USUARIO", "EV-NADA" });

            Assert.AreEqual(2, a.Count);
            Assert.AreEqual("Carlos", a[CODIGO].Usuario);
            Assert.AreEqual(ADELANTO.AddDays(1), a[CODIGO].Fecha);
            Assert.AreEqual("Alfredo", a["EV-SIN-USUARIO"].Usuario);
        }

        [TestMethod]
        public async Task LeerPorCodigo_TraeQuienYCuandoLoAdelanto()
        {
            CuponDTO dto = await Servicio().LeerPorCodigo(CODIGO, Empleado());

            Assert.IsTrue(dto.Adelantado);
            Assert.AreEqual("Manuel", dto.AdelantadoPor);
            Assert.AreEqual(ADELANTO, dto.AdelantadoEl);
            A.CallTo(() => repositorio.LeerRastrosAdelanto(new DateTime(2026, 11, 10))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerDelCliente_UnaSolaLecturaDelRastro_YElNoAdelantadoVaSinDatos()
        {
            List<CuponDTO> lista = await Servicio().LeerDelCliente("1", "40513", true, Empleado());

            A.CallTo(() => repositorio.LeerRastrosAdelanto(A<DateTime>._)).MustHaveHappenedOnceExactly();
            CuponDTO otro = lista.Single(c => c.Codigo == "EV-OTRO");
            Assert.IsFalse(otro.Adelantado);
            Assert.IsNull(otro.AdelantadoPor);
            Assert.IsNull(otro.AdelantadoEl);
            Assert.IsTrue(lista.Single(c => c.Codigo == CODIGO).Adelantado);
        }

        [TestMethod]
        public async Task SinCuponesDeEvento_NoSeLeeElRastro()
        {
            cupon = Cupon(origen: "Manual");

            CuponDTO dto = await Servicio().LeerPorCodigo(CODIGO, Empleado());

            Assert.IsFalse(dto.Adelantado);
            A.CallTo(() => repositorio.LeerRastrosAdelanto(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiFallaElRastro_ElCuponSaleIgualSinAdelanto()
        {
            A.CallTo(() => repositorio.LeerRastrosAdelanto(A<DateTime>._)).Throws(new InvalidOperationException("sin permiso"));

            CuponDTO dto = await Servicio().LeerPorCodigo(CODIGO, Empleado());

            Assert.IsFalse(dto.Adelantado);
        }

        [TestMethod]
        public async Task NoAsistio_ConElCuponAdelantadoYUsado_AvisaUnaVez()
        {
            cupon = Cupon(EstadosCupon.USADO);
            cupon.UsadoEnPedido = 930535;
            A.CallTo(() => repositorio.AnularDeInscripcion(101, A<string>._, A<string>._)).Returns(Task.FromResult(0));

            await Servicio().AplicarAsistencia(new[] { new KeyValuePair<int, bool>(101, false) }, "Laura");

            A.CallTo(() => repositorio.AnotarAvisoSinAsistir(CODIGO, A<string>._, "Laura")).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(A<CuponLeido>.That.Matches(c => c.Codigo == CODIGO), A<AdelantoCupon>.That.Matches(a => a.Usuario == "Manuel"), false))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task NoAsistio_YaAvisado_NoRepite()
        {
            A.CallTo(() => repositorio.AnotarAvisoSinAsistir(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(false));
            A.CallTo(() => repositorio.AnularDeInscripcion(101, A<string>._, A<string>._)).Returns(Task.FromResult(1));

            await Servicio().AplicarAsistencia(new[] { new KeyValuePair<int, bool>(101, false) }, "Laura");

            A.CallTo(() => avisador.Avisar(A<CuponLeido>._, A<AdelantoCupon>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task NoAsistio_CuponSinAdelantar_NoAvisa()
        {
            A.CallTo(() => repositorio.LeerRastrosAdelanto(A<DateTime>._)).Returns(Task.FromResult(new List<RastroAdelantoCupon>()));
            A.CallTo(() => repositorio.AnularDeInscripcion(101, A<string>._, A<string>._)).Returns(Task.FromResult(1));

            await Servicio().AplicarAsistencia(new[] { new KeyValuePair<int, bool>(101, false) }, "Laura");

            A.CallTo(() => repositorio.AnotarAvisoSinAsistir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<CuponLeido>._, A<AdelantoCupon>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiVino_NoAvisa()
        {
            await Servicio().AplicarAsistencia(new[] { new KeyValuePair<int, bool>(101, true) }, "Laura");

            A.CallTo(() => avisador.Avisar(A<CuponLeido>._, A<AdelantoCupon>._, A<bool>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerRastrosAdelanto(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Notificacion_DiceEventoImporteQuienYQuePasaConElCupon()
        {
            CuponLeido usado = Cupon(EstadosCupon.USADO);
            usado.UsadoEnPedido = 930535;
            var adelanto = new AdelantoCupon { Usuario = "Manuel", Fecha = ADELANTO };

            var n = ReglaAdelantarCupon.NotificacionSinAsistir(usado, adelanto, "RAQUEL", false);

            Assert.AreEqual(ReglaAdelantarCupon.TIPO_NOTIFICACION_SIN_ASISTIR, n.Tipo);
            StringAssert.Contains(n.Cuerpo, "RAQUEL (40513) no asistió a «Masterclass Rosácea»");
            StringAssert.Contains(n.Cuerpo, "50,00 €");
            StringAssert.Contains(n.Cuerpo, "lo adelantó Manuel el 23/11/2026");
            StringAssert.Contains(n.Cuerpo, "en el pedido 930535");
            Assert.AreEqual("40513", n.Datos["cliente"]);
            Assert.AreEqual("0", n.Datos["contacto"]);
            Assert.AreEqual("1", n.Datos["empresa"]);
            Assert.AreEqual("CuponAdelantadoSinAsistir", n.Datos["tipo"]);

            StringAssert.Contains(ReglaAdelantarCupon.NotificacionSinAsistir(Cupon(), adelanto, null, true).Cuerpo, "se ha anulado");
        }
    }
}
