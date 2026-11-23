using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Exceptions;
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

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#885 (corte 1): adelantar por excepción un cupón de evento (que vale desde el día del evento) a hoy. La regla de permisos
    /// (Dirección, «Cupones.PuedeAdelantar» = «1» o «Equipo»), los estados que no se adelantan, el motivo, el rastro, el aviso, el
    /// espejo de PrestaShop y <see cref="CuponDTO.SePuedeAdelantar"/>.
    /// </summary>
    [TestClass]
    public class AdelantarCuponTests
    {
        private const string PRODUCTO = "CUPONEVENTO";
        private const string CODIGO = "EV-2611-7K3Q";
        private static readonly DateTime HOY = new DateTime(2026, 11, 23, 10, 0, 0);
        private static readonly DateTime EVENTO = new DateTime(2026, 11, 30);

        private IRepositorioCupones repositorio;
        private ILectorPermisoAdelantarCupon lector;
        private IAvisadorCuponAdelantado avisador;
        private IEspejoCupones espejo;
        private CuponLeido cupon;

        [TestInitialize]
        public void Preparar()
        {
            cupon = Cupon();
            repositorio = A.Fake<IRepositorioCupones>();
            A.CallTo(() => repositorio.LeerPorCodigo(CODIGO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._))
                .Returns(Task.FromResult(new Dictionary<string, ProductoParaCupon>()));
            A.CallTo(() => repositorio.Adelantar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerPorId(7)).ReturnsLazily(() =>
            {
                CuponLeido c = Cupon(desde: HOY.Date);
                c.Estado = EstadosCupon.ACTIVO;
                return Task.FromResult(c);
            });
            lector = A.Fake<ILectorPermisoAdelantarCupon>();
            A.CallTo(() => lector.LeerParametro(A<string>._)).Returns(Task.FromResult<string>(null));
            avisador = A.Fake<IAvisadorCuponAdelantado>();
            espejo = A.Fake<IEspejoCupones>();
        }

        private static CuponLeido Cupon(string estado = EstadosCupon.EMITIDO, string origen = OrigenesCupon.EVENTO, DateTime? desde = null,
            string cliente = "40513", string contacto = "0", int id = 7) => new CuponLeido
            {
                Id = id,
                Codigo = CODIGO,
                Empresa = "1",
                Cliente = cliente,
                Contacto = contacto,
                Importe = 100,
                Origen = origen,
                EventoInscripcionId = 101,
                EventoId = 1,
                Evento = "Masterclass",
                FechaEmision = new DateTime(2026, 11, 10),
                ValidoDesde = desde ?? EVENTO,
                Caducidad = ReglasCupon.Caducidad(desde ?? EVENTO),
                Estado = estado
            };

        private ServicioCupones Servicio() =>
            new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO, espejo, null, null, null,
                new AutorizadorAdelantarCupon(lector), avisador);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda(string cliente) =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", cliente) }, "prueba"));

        private static IPrincipal Anonimo() => new ClaimsPrincipal(new ClaimsIdentity());

        // ---------------- La regla pura de permisos ----------------

        [TestMethod]
        public void Alcance_DireccionTodos_SinMirarElParametro()
        {
            Assert.AreEqual(AlcanceAdelantarCupon.Todos, ReglaAdelantarCupon.Alcance(false, true, null));
            Assert.IsFalse(ReglaAdelantarCupon.HayQueLeerParametro(false, true));
        }

        [TestMethod]
        public void Alcance_ParametroUno_Todos_YEquipo_Equipo()
        {
            Assert.AreEqual(AlcanceAdelantarCupon.Todos, ReglaAdelantarCupon.Alcance(false, false, "1"));
            Assert.AreEqual(AlcanceAdelantarCupon.Todos, ReglaAdelantarCupon.Alcance(false, false, " 1 "));
            Assert.AreEqual(AlcanceAdelantarCupon.Equipo, ReglaAdelantarCupon.Alcance(false, false, "Equipo"));
            Assert.AreEqual(AlcanceAdelantarCupon.Equipo, ReglaAdelantarCupon.Alcance(false, false, "equipo"));
        }

        [TestMethod]
        public void Alcance_SinFilaOValorRaro_Ninguno()
        {
            Assert.AreEqual(AlcanceAdelantarCupon.Ninguno, ReglaAdelantarCupon.Alcance(false, false, null));
            Assert.AreEqual(AlcanceAdelantarCupon.Ninguno, ReglaAdelantarCupon.Alcance(false, false, "0"));
            Assert.AreEqual(AlcanceAdelantarCupon.Ninguno, ReglaAdelantarCupon.Alcance(false, false, "Sí"));
            Assert.IsTrue(ReglaAdelantarCupon.HayQueLeerParametro(false, false));
        }

        [TestMethod]
        public void Alcance_ClienteDeLaTienda_Ninguno()
        {
            Assert.AreEqual(AlcanceAdelantarCupon.Ninguno, ReglaAdelantarCupon.Alcance(true, false, "1"));
            Assert.IsFalse(ReglaAdelantarCupon.HayQueLeerParametro(true, false));
        }

        [TestMethod]
        public void Puede_Equipo_SoloSiElVendedorDelClienteEsDeSuEquipo()
        {
            var equipo = new List<string> { "JE", "PA" };
            Assert.IsTrue(ReglaAdelantarCupon.Puede(AlcanceAdelantarCupon.Equipo, "pa ", equipo));
            Assert.IsFalse(ReglaAdelantarCupon.Puede(AlcanceAdelantarCupon.Equipo, "NV", equipo));
            Assert.IsFalse(ReglaAdelantarCupon.Puede(AlcanceAdelantarCupon.Equipo, null, equipo));
            Assert.IsTrue(ReglaAdelantarCupon.Puede(AlcanceAdelantarCupon.Todos, null, null));
            Assert.IsFalse(ReglaAdelantarCupon.Puede(AlcanceAdelantarCupon.Ninguno, "JE", equipo));
        }

        // ---------------- El autorizador (lecturas) ----------------

        [TestMethod]
        public async Task Autorizador_Direccion_NoLeeElParametro()
        {
            HashSet<int> puede = await new AutorizadorAdelantarCupon(lector).QuePuedeAdelantar(Usuario("NUEVAVISION\\Carlos", "Dirección"), new[] { cupon });

            Assert.IsTrue(puede.Contains(7));
            A.CallTo(() => lector.LeerParametro(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Autorizador_ParametroUno_CualquierCliente_LeidoSinDominio()
        {
            A.CallTo(() => lector.LeerParametro("Manuel")).Returns(Task.FromResult("1"));

            HashSet<int> puede = await new AutorizadorAdelantarCupon(lector).QuePuedeAdelantar(Usuario("NUEVAVISION\\Manuel", "Ventas"), new[] { cupon });

            Assert.IsTrue(puede.Contains(7));
            A.CallTo(() => lector.VendedoresDeLosClientes(A<string>._, A<IEnumerable<KeyValuePair<string, string>>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Autorizador_Equipo_ClienteDentroYFuera_UnaLecturaDeVendedores()
        {
            CuponLeido fuera = Cupon(cliente: "20000", id: 8);
            CuponLeido sinContacto = Cupon(cliente: "30000", contacto: null, id: 9);
            A.CallTo(() => lector.LeerParametro("Sancho")).Returns(Task.FromResult("Equipo"));
            A.CallTo(() => lector.VendedorDelUsuario(A<IPrincipal>._)).Returns("AS");
            A.CallTo(() => lector.VendedoresDelEquipo("1", "AS")).Returns(Task.FromResult(new List<string> { "PA" }));
            A.CallTo(() => lector.VendedoresDeLosClientes("1", A<IEnumerable<KeyValuePair<string, string>>>._))
                .Returns(Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["40513/0"] = "PA",
                    ["20000/0"] = "NV",
                    ["30000/"] = "AS"
                }));

            HashSet<int> puede = await new AutorizadorAdelantarCupon(lector)
                .QuePuedeAdelantar(Usuario("NUEVAVISION\\Sancho", "Ventas"), new[] { cupon, fuera, sinContacto });

            Assert.IsTrue(puede.Contains(7), "Cliente de alguien de su equipo");
            Assert.IsFalse(puede.Contains(8), "Cliente de fuera de su equipo");
            Assert.IsTrue(puede.Contains(9), "Su propio cliente (sin contacto: el principal)");
            A.CallTo(() => lector.VendedoresDeLosClientes(A<string>._, A<IEnumerable<KeyValuePair<string, string>>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Autorizador_EquipoSinVendedor_Nada()
        {
            A.CallTo(() => lector.LeerParametro("Laura")).Returns(Task.FromResult("Equipo"));
            A.CallTo(() => lector.VendedorDelUsuario(A<IPrincipal>._)).Returns(null);

            HashSet<int> puede = await new AutorizadorAdelantarCupon(lector).QuePuedeAdelantar(Usuario("NUEVAVISION\\Laura", "Ventas"), new[] { cupon });

            Assert.AreEqual(0, puede.Count);
        }

        [TestMethod]
        public async Task Autorizador_SinFila_Nada()
        {
            HashSet<int> puede = await new AutorizadorAdelantarCupon(lector).QuePuedeAdelantar(Usuario("NUEVAVISION\\Jesus", "Ventas"), new[] { cupon });

            Assert.AreEqual(0, puede.Count);
            A.CallTo(() => lector.LeerParametro("Jesus")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Autorizador_ClienteDeLaTiendaOApiKey_Nada()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._)).Returns(Task.FromResult("1"));
            var autorizador = new AutorizadorAdelantarCupon(lector);

            Assert.AreEqual(0, (await autorizador.QuePuedeAdelantar(ClienteTienda("40513"), new[] { cupon })).Count);
            Assert.AreEqual(0, (await autorizador.QuePuedeAdelantar(Anonimo(), new[] { cupon })).Count);
            Assert.AreEqual(0, (await autorizador.QuePuedeAdelantar(null, new[] { cupon })).Count);
            A.CallTo(() => lector.LeerParametro(A<string>._)).MustNotHaveHappened();
        }

        // ---------------- Estados y motivo ----------------

        [TestMethod]
        public void MotivoNoAdelantable_SoloEmitidoDeEvento()
        {
            Assert.IsNull(ReglaAdelantarCupon.MotivoNoAdelantable(Cupon(), HOY));
            StringAssert.Contains(ReglaAdelantarCupon.MotivoNoAdelantable(Cupon(desde: new DateTime(2026, 11, 20)), HOY),
                "ya se puede usar desde el 20/11/2026: no hace falta adelantarlo");
            StringAssert.Contains(ReglaAdelantarCupon.MotivoNoAdelantable(Cupon(origen: OrigenesCupon.MANUAL), HOY), "no es de un evento");
        }

        [TestMethod]
        public void MotivoNoAdelantable_UsadoAnuladoCaducado()
        {
            CuponLeido usado = Cupon(EstadosCupon.USADO);
            usado.UsadoEnPedido = 930500;
            usado.PedidoUsoTieneLaLinea = true;
            StringAssert.Contains(ReglaAdelantarCupon.MotivoNoAdelantable(usado, HOY), "pedido 930500");
            StringAssert.Contains(ReglaAdelantarCupon.MotivoNoAdelantable(Cupon(EstadosCupon.ANULADO), HOY), "anulado");
            StringAssert.Contains(ReglaAdelantarCupon.MotivoNoAdelantable(Cupon(EstadosCupon.CADUCADO), HOY), "caducó");
        }

        [TestMethod]
        public void MotivoNoValido_VacioOLargo()
        {
            Assert.AreEqual(ReglaAdelantarCupon.MENSAJE_SIN_MOTIVO, ReglaAdelantarCupon.MotivoNoValido(null));
            Assert.AreEqual(ReglaAdelantarCupon.MENSAJE_SIN_MOTIVO, ReglaAdelantarCupon.MotivoNoValido("   "));
            Assert.IsNotNull(ReglaAdelantarCupon.MotivoNoValido(new string('x', 501)));
            Assert.IsNull(ReglaAdelantarCupon.MotivoNoValido(new string('x', 500)));
        }

        // ---------------- El servicio ----------------

        [TestMethod]
        public async Task Adelantar_Direccion_CambiaValidoDesdeDejaRastroAvisaYSincroniza()
        {
            string anterior = null, nuevo = null;
            A.CallTo(() => repositorio.Adelantar(7, A<DateTime>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((int id, DateTime d, string u, string a, string n) => { anterior = a; nuevo = n; return Task.FromResult(true); });
            NotificacionPushDTO aviso = null;
            A.CallTo(() => avisador.Avisar(A<NotificacionPushDTO>._)).Invokes((NotificacionPushDTO n) => aviso = n).Returns(Task.CompletedTask);

            CuponDTO dto = await Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "  Viene a la tienda hoy  " },
                Usuario("NUEVAVISION\\Manuel", "Dirección"));

            A.CallTo(() => repositorio.Adelantar(7, HOY.Date, A<string>._, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(anterior, "\"ValidoDesde\":\"2026-11-30\"");
            StringAssert.Contains(nuevo, "\"ValidoDesdeAnterior\":\"2026-11-30\"");
            StringAssert.Contains(nuevo, "\"ValidoDesde\":\"2026-11-23\"");
            StringAssert.Contains(nuevo, "\"Motivo\":\"Viene a la tienda hoy\"");
            StringAssert.Contains(nuevo, "\"Cliente\":\"40513\"");
            StringAssert.Contains(nuevo, "\"Importe\":100");
            A.CallTo(() => espejo.Sincronizar(7)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(aviso.Cuerpo, "Cupón EV-2611-7K3Q de 100,00 € del cliente 40513 adelantado por Manuel: Viene a la tienda hoy");
            Assert.AreEqual(ReglaAdelantarCupon.TIPO_NOTIFICACION, aviso.Tipo);
            Assert.AreEqual(HOY.Date, dto.ValidoDesde);
            Assert.AreEqual(EstadosCupon.ACTIVO, dto.Estado);
            Assert.IsTrue(dto.SePuedeUsar);
            Assert.IsFalse(dto.SePuedeAdelantar);
        }

        [TestMethod]
        public async Task Adelantar_ElAvisoFalla_LaRespuestaSigue()
        {
            A.CallTo(() => avisador.Avisar(A<NotificacionPushDTO>._)).Throws(new InvalidOperationException("sin buzón"));

            CuponDTO dto = await Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "Excepción" }, Usuario("NUEVAVISION\\Carlos", "Dirección"));

            Assert.AreEqual(HOY.Date, dto.ValidoDesde);
        }

        [TestMethod]
        public async Task Adelantar_SinMotivoOLargo_400SinTocar()
        {
            IPrincipal direccion = Usuario("NUEVAVISION\\Carlos", "Dirección");
            var vacio = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Adelantar(CODIGO, new AdelantarCuponDTO(), direccion));
            var nulo = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Adelantar(CODIGO, null, direccion));
            var largo = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = new string('x', 501) }, direccion));

            Assert.AreNotEqual(HttpStatusCode.Forbidden, vacio.StatusCode);
            Assert.AreNotEqual(HttpStatusCode.Conflict, vacio.StatusCode);
            StringAssert.Contains(vacio.Message, "motivo");
            StringAssert.Contains(nulo.Message, "motivo");
            StringAssert.Contains(largo.Message, "500");
            A.CallTo(() => repositorio.Adelantar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Adelantar_NoExiste_404()
        {
            A.CallTo(() => repositorio.LeerPorCodigo("EV-0000-XXXX")).Returns(Task.FromResult<CuponLeido>(null));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Adelantar("EV-0000-XXXX", new AdelantarCuponDTO { Motivo = "x" }, Usuario("NUEVAVISION\\Carlos", "Dirección")));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task Adelantar_SinPermiso_403()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "x" }, Usuario("NUEVAVISION\\Jesus", "Ventas")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual(ReglaAdelantarCupon.MENSAJE_SIN_PERMISO, ex.Message);
            A.CallTo(() => repositorio.Adelantar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Adelantar_ClienteDeLaTienda_403()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "x" }, ClienteTienda("40513")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            A.CallTo(() => repositorio.LeerPorCodigo(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Adelantar_EquipoFueraDeSuEquipo_403()
        {
            A.CallTo(() => lector.LeerParametro("Sancho")).Returns(Task.FromResult("Equipo"));
            A.CallTo(() => lector.VendedorDelUsuario(A<IPrincipal>._)).Returns("AS");
            A.CallTo(() => lector.VendedoresDelEquipo("1", "AS")).Returns(Task.FromResult(new List<string> { "AS" }));
            A.CallTo(() => lector.VendedoresDeLosClientes("1", A<IEnumerable<KeyValuePair<string, string>>>._))
                .Returns(Task.FromResult(new Dictionary<string, string> { ["40513/0"] = "NV" }));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "x" }, Usuario("NUEVAVISION\\Sancho", "Ventas")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task Adelantar_ParametroUno_Adelanta()
        {
            A.CallTo(() => lector.LeerParametro("Manuel")).Returns(Task.FromResult("1"));

            CuponDTO dto = await Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "x" }, Usuario("NUEVAVISION\\Manuel", "Ventas"));

            Assert.AreEqual(HOY.Date, dto.ValidoDesde);
        }

        [TestMethod]
        public async Task Adelantar_EstadosQueNoSeAdelantan_409SinTocar()
        {
            CuponLeido usado = Cupon(EstadosCupon.USADO);
            usado.UsadoEnPedido = 930500;
            usado.PedidoUsoTieneLaLinea = true;
            var casos = new List<CuponLeido>
            {
                Cupon(desde: new DateTime(2026, 11, 20)),
                usado,
                Cupon(EstadosCupon.ANULADO),
                Cupon(EstadosCupon.CADUCADO),
                Cupon(origen: OrigenesCupon.MANUAL)
            };
            foreach (CuponLeido caso in casos)
            {
                cupon = caso;
                var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                    Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "x" }, Usuario("NUEVAVISION\\Carlos", "Dirección")));
                Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode, caso.Estado + " " + caso.Origen);
                Assert.AreEqual(ReglaAdelantarCupon.ERROR_NO_SE_PUEDE_ADELANTAR, ex.Context?.ErrorCode);
            }
            A.CallTo(() => repositorio.Adelantar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Adelantar_CambiaALaVez_409ConElEstadoDeAhora()
        {
            A.CallTo(() => repositorio.Adelantar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(false));
            A.CallTo(() => repositorio.LeerPorId(7)).Returns(Task.FromResult(Cupon(EstadosCupon.ANULADO)));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Adelantar(CODIGO, new AdelantarCuponDTO { Motivo = "x" }, Usuario("NUEVAVISION\\Carlos", "Dirección")));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, "anulado");
            A.CallTo(() => espejo.Sincronizar(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_PostAdelantar()
        {
            var controlador = new CuponesController(A.Fake<NVEntities>(), Servicio())
            {
                Request = new HttpRequestMessage(),
                User = Usuario("NUEVAVISION\\Carlos", "Dirección")
            };

            var resultado = await controlador.PostAdelantarCupon(CODIGO, new AdelantarCuponDTO { Motivo = "x" }) as OkNegotiatedContentResult<CuponDTO>;

            Assert.AreEqual(HOY.Date, resultado.Content.ValidoDesde);
        }

        // ---------------- SePuedeAdelantar ----------------

        [TestMethod]
        public async Task SePuedeAdelantar_PorCodigo_SoloEmitidoYConPermiso()
        {
            CuponDTO direccion = await Servicio().LeerPorCodigo(CODIGO, Usuario("NUEVAVISION\\Carlos", "Dirección"));
            CuponDTO sinPermiso = await Servicio().LeerPorCodigo(CODIGO, Usuario("NUEVAVISION\\Jesus", "Ventas"));
            CuponDTO cliente = await Servicio().LeerPorCodigo(CODIGO, ClienteTienda("40513"));
            cupon = Cupon(desde: new DateTime(2026, 11, 20));
            CuponDTO yaVale = await Servicio().LeerPorCodigo(CODIGO, Usuario("NUEVAVISION\\Carlos", "Dirección"));

            Assert.IsTrue(direccion.SePuedeAdelantar);
            Assert.IsFalse(sinPermiso.SePuedeAdelantar);
            Assert.IsFalse(cliente.SePuedeAdelantar);
            Assert.IsFalse(yaVale.SePuedeAdelantar);
        }

        [TestMethod]
        public async Task SePuedeAdelantar_DelCliente_UnaComprobacion_YSinEmitidosNiSeMira()
        {
            CuponLeido activo = Cupon(desde: new DateTime(2026, 11, 20), id: 8);
            CuponLeido manual = Cupon(origen: OrigenesCupon.MANUAL, id: 9);
            A.CallTo(() => repositorio.LeerDelCliente("1", "40513")).Returns(Task.FromResult(new List<CuponLeido> { cupon, activo, manual }));
            A.CallTo(() => lector.LeerParametro("Manuel")).Returns(Task.FromResult("1"));

            List<CuponDTO> lista = await Servicio().LeerDelCliente("1", "40513", false, Usuario("NUEVAVISION\\Manuel", "Ventas"));

            Assert.IsTrue(lista.Single(c => c.Id == 7).SePuedeAdelantar);
            Assert.IsFalse(lista.Single(c => c.Id == 8).SePuedeAdelantar);
            Assert.IsFalse(lista.Single(c => c.Id == 9).SePuedeAdelantar, "Manual: no es de evento");
            A.CallTo(() => lector.LeerParametro(A<string>._)).MustHaveHappenedOnceExactly();

            A.CallTo(() => repositorio.LeerDelCliente("1", "40513")).Returns(Task.FromResult(new List<CuponLeido> { activo }));
            Fake.ClearRecordedCalls(lector);
            List<CuponDTO> sinEmitidos = await Servicio().LeerDelCliente("1", "40513", false, Usuario("NUEVAVISION\\Manuel", "Ventas"));

            Assert.IsFalse(sinEmitidos.Single().SePuedeAdelantar);
            A.CallTo(() => lector.LeerParametro(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SePuedeAdelantar_ConApiKey_False()
        {
            A.CallTo(() => repositorio.LeerDelCliente("1", "40513")).Returns(Task.FromResult(new List<CuponLeido> { cupon }));

            List<CuponDTO> lista = await Servicio().LeerDelCliente("1", "40513", false, Anonimo());

            Assert.IsFalse(lista.Single().SePuedeAdelantar);
        }

        // ---------------- Espejo de PrestaShop ----------------

        [TestMethod]
        public void Espejo_ActivoAlliYMarcadoPendiente_SeVuelveAMandar()
        {
            var adelantado = new CuponEspejo
            {
                Id = 7, Codigo = CODIGO, Cliente = "40513", Origen = OrigenesCupon.EVENTO, Estado = EstadosCupon.ACTIVO,
                ValidoDesde = HOY.Date, Caducidad = ReglasCupon.Caducidad(HOY.Date), EstadoEspejo = EstadosEspejo.ACTIVO, IdCartRulePrestashop = 812
            };
            Assert.IsFalse(ReglasEspejoPrestashop.HayQueSincronizar(adelantado, HOY), "Sin marca: ya está");
            adelantado.EspejoPendienteDesde = HOY;
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(adelantado, HOY), "Adelantado: la fecha nueva tiene que llegar a la tienda");
            StringAssert.Contains(RepositorioEspejoCupones.SQL_PENDIENTES, "c.EspejoPendienteDesde IS NOT NULL");
            StringAssert.Contains(ReglasEspejoPrestashop.XmlCartRule(adelantado, 55, true, 812, 1, 1), "<date_from>2026-11-23 00:00:00</date_from>");
        }

        [TestMethod]
        public void Repositorio_SqlAdelantar_EnUnaTransaccionConRastroYEspejo()
        {
            string sql = RepositorioCupones.SQL_ADELANTAR;
            StringAssert.Contains(sql, "BEGIN TRANSACTION");
            StringAssert.Contains(sql, "INSERT INTO dbo.Modificaciones");
            StringAssert.Contains(sql, "c.Origen = 'Evento'");
            StringAssert.Contains(sql, "EspejoPendienteDesde");
            Assert.AreEqual("CuponAdelantado", ReglaAdelantarCupon.TABLA_MODIFICACIONES);
            Assert.AreEqual("Cupones.PuedeAdelantar", ReglaAdelantarCupon.CLAVE_PARAMETRO);
        }
    }
}
