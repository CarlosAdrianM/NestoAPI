using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#694: los cupones en la tienda PrestaShop por el módulo nestocheques, como el cheque regalo (#652):
    /// POST api/Cupones/ComprobarCarrito (mínimo sin cursos ni portes, con el MISMO ServicioCanjeCupones que POST api/PedidosVenta),
    /// GET de los cupones del cliente y POST api/Cupones/{codigo}/Usar con la X-API-KEY, el uso desde la tienda sin pedido de Nesto
    /// (idempotente por la referencia de la tienda) y el pedido que entra después por CanalesExternos sin volver a canjearlo.
    /// </summary>
    [TestClass]
    public class CuponesTiendaPrestashopTests
    {
        private const string PRODUCTO = "CUPONEVENTO";
        private const string CLIENTE = "15191";
        private const string CODIGO = "EV-2610-7K3Q";
        private const string REFERENCIA = "XKBKNABJK";
        private const int PEDIDO = 930500;
        private static readonly DateTime HOY = new DateTime(2026, 11, 2, 10, 0, 0);

        private IRepositorioCupones repositorio;
        private IServicioCorreoElectronico correo;
        private IEspejoCupones espejo;
        private CuponLeido cupon;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioCupones>();
            correo = A.Fake<IServicioCorreoElectronico>();
            espejo = A.Fake<IEspejoCupones>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            cupon = CuponesTests.Cupon();
            A.CallTo(() => repositorio.LeerPorCodigo(A<string>._)).Returns(Task.FromResult<CuponLeido>(null));
            A.CallTo(() => repositorio.LeerPorCodigo(CODIGO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerPorId(7)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).ReturnsLazily(() => Task.FromResult(new List<CuponLeido> { cupon }));
            A.CallTo(() => repositorio.LeerIvaCliente("1", CLIENTE)).Returns(Task.FromResult("G21"));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily(() => Task.FromResult(Productos()));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 0 }));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, "G21", "EX"))
                .Returns(Task.FromResult(new PorcentajesIvaCupon { Iva = 0, RecargoEquivalencia = 0 }));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, "E52", "G21"))
                .Returns(Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 5.2M }));
            A.CallTo(() => repositorio.LeerUsoTienda(A<int>._)).Returns(Task.FromResult<UsoTiendaCupon>(null));
        }

        private static Dictionary<string, ProductoParaCupon> Productos() => new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
        {
            [PRODUCTO] = new ProductoParaCupon { Numero = PRODUCTO, Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            ["CREMA"] = new ProductoParaCupon { Numero = "CREMA", Grupo = "COS", IvaRepercutido = "G21" },
            ["CURSO"] = new ProductoParaCupon { Numero = "CURSO", Grupo = Constantes.Productos.GRUPO_CURSOS, Ficticio = true, IvaRepercutido = "EX" },
            ["CURSONOFICT"] = new ProductoParaCupon { Numero = "CURSONOFICT", Grupo = Constantes.Productos.GRUPO_CURSOS, Ficticio = false, IvaRepercutido = "EX" }
        };

        private ServicioCanjeCupones Canje() => new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => false);

        private static ComprobarCarritoCuponDTO Carrito(string codigo, params (string producto, decimal baseImponible)[] lineas) => new ComprobarCarritoCuponDTO
        {
            Empresa = "1",
            Cliente = CLIENTE,
            Codigo = codigo,
            Lineas = lineas.Select(l => new LineaCarritoCuponDTO { Producto = l.producto, Cantidad = 1, BaseImponible = l.baseImponible }).ToList()
        };

        // ================= ComprobarCarrito =================

        [TestMethod]
        public async Task ComprobarCarrito_Aplicable_ConImporteYBaseDelCupon_YNoReservaNada()
        {
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
            Assert.AreEqual(CODIGO, r.Codigo);
            Assert.AreEqual(50M, r.Importe, "IVA incluido");
            Assert.AreEqual(41.32M, r.BaseImponible, "50 € al 21 %");
            Assert.IsNull(r.ErrorCode);
            Assert.IsNull(r.Falta);
            Assert.IsTrue(r.CombinableConChequeRegalo);
            StringAssert.Contains(r.Mensaje, "Puede usar su cupón de 50,00 €");
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.UsarDesdeTienda(A<int>._, A<string>._, A<string>._, A<string>._, A<decimal?>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ComprobarCarrito_300EurosDeLosQue150SonDeUnCurso_NoLleganAUnMinimoDe250()
        {
            cupon.Importe = 125; // mínimo 250 IVA incluido
            // 150 € de crema (123,97 de base al 21 %) + 150 € de un curso (exento): 300 € en el carrito, 150 € que cuentan
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 150M / 1.21M), ("CURSO", 150M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, r.ErrorCode);
            Assert.AreEqual(100M, r.Falta, "Faltan 100 € de productos: el curso no cuenta");
            Assert.AreEqual(125M, r.Importe, "El importe va también cuando todavía no se puede usar, para el «le faltan…»");
            StringAssert.Contains(r.Mensaje, "sin contar cursos ni portes");
            StringAssert.Contains(r.Mensaje, "faltan 100,00 €");
        }

        [TestMethod]
        public async Task ComprobarCarrito_UnCursoQueNoEsFicticioTampocoCuenta()
        {
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 40M), ("CURSONOFICT", 500M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, r.ErrorCode);
            Assert.AreEqual(51.60M, r.Falta, "100 − 48,40 (40 + IVA)");
        }

        [TestMethod]
        public async Task ComprobarCarrito_ElMinimoEsIvaIncluido_LaTiendaMandaLaBase()
        {
            // 82,65 de base al 21 % = 100,01 € IVA incluido: llega al mínimo de 100 (con la base sola no llegaría)
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 82.65M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_PortesUOtraCuentaQueNoEsProducto_NoCuentan()
        {
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 50M), ("62400003", 200M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, r.ErrorCode);
        }

        [TestMethod]
        public async Task ComprobarCarrito_SiLaTiendaMandaLaLineaDelPropioCupon_NoSeCuentaDosVeces()
        {
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M), (PRODUCTO, -41.32M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_ConRecargoDeEquivalencia_LaBaseDelCuponEsMenor()
        {
            A.CallTo(() => repositorio.LeerIvaCliente("1", CLIENTE)).Returns(Task.FromResult("E52"));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
            Assert.AreEqual(39.62M, r.BaseImponible, "50 / 1,262: descuenta exactamente 50 € con el recargo");
        }

        [TestMethod]
        public async Task ComprobarCarrito_ClienteSinIva_LaBaseEsElImporte()
        {
            A.CallTo(() => repositorio.LeerIvaCliente("1", CLIENTE)).Returns(Task.FromResult<string>(null));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
            Assert.AreEqual(50M, r.BaseImponible);
        }

        [TestMethod]
        public async Task ComprobarCarrito_SinCodigo_UsaElCuponDelClienteQueSePuedeUsarYCaducaAntes()
        {
            CuponLeido masTarde = CuponesTests.Cupon();
            masTarde.Id = 8;
            masTarde.Codigo = "EV-2610-AAAA";
            masTarde.Caducidad = cupon.Caducidad.AddMonths(1);
            CuponLeido emitido = CuponesTests.Cupon(EstadosCupon.EMITIDO, desde: HOY.Date.AddDays(10));
            emitido.Id = 9;
            emitido.Codigo = "EV-2611-BBBB";
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).Returns(Task.FromResult(new List<CuponLeido> { emitido, masTarde, cupon }));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(null, ("CREMA", 100M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
            Assert.AreEqual(CODIGO, r.Codigo, "El que caduca antes, como ReglasCupon.ElegirParaPedido de Nesto");
        }

        [TestMethod]
        public async Task ComprobarCarrito_SinCodigo_SoloUnoQueValdraMasAdelante_DiceDesdeCuando()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.EMITIDO, desde: HOY.Date.AddDays(10));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(null, ("CREMA", 100M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_TODAVIA_NO, r.ErrorCode);
            StringAssert.Contains(r.Mensaje, "vale desde el 12/11/2026");
        }

        [TestMethod]
        public async Task ComprobarCarrito_SinCodigo_ClienteSinCupones()
        {
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).Returns(Task.FromResult(new List<CuponLeido>()));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(null, ("CREMA", 100M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_SIN_CUPON, r.ErrorCode);
            Assert.IsNull(r.Codigo);
            Assert.AreEqual(0M, r.Importe);
            Assert.AreEqual("No tiene ningún cupón para usar en este pedido.", r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_SinCodigo_LosUsadosYCaducadosNoSeOfrecen()
        {
            CuponLeido usado = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: 930400);
            CuponLeido caducado = CuponesTests.Cupon(EstadosCupon.CADUCADO);
            caducado.Codigo = "EV-2604-CCCC";
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).Returns(Task.FromResult(new List<CuponLeido> { usado, caducado }));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(null, ("CREMA", 100M)));

            Assert.AreEqual(ReglasCupon.ERROR_SIN_CUPON, r.ErrorCode);
        }

        [TestMethod]
        public async Task ComprobarCarrito_CodigoQueNoExiste()
        {
            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito("ev-0000-xxxx", ("CREMA", 100M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_NO_EXISTE, r.ErrorCode);
            StringAssert.Contains(r.Mensaje, "EV-0000-XXXX");
        }

        [TestMethod]
        public async Task ComprobarCarrito_CuponDeOtroCliente_NoDiceDeQuienEs()
        {
            cupon = CuponesTests.Cupon(cliente: "20000");

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_OTRO_CLIENTE, r.ErrorCode);
            Assert.IsFalse(r.Mensaje.Contains("20000"), r.Mensaje);
            Assert.AreEqual(0M, r.Importe);
        }

        [TestMethod]
        public async Task ComprobarCarrito_CuponYaUsado()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: 930400);

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_YA_USADO, r.ErrorCode);
            StringAssert.Contains(r.Mensaje, "930400");
        }

        [TestMethod]
        public async Task ComprobarCarrito_YaGastadoEnLaTienda_NoSePuedeVolverAUsar()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO);
            cupon.CanalUso = "PrestaShop";

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_YA_USADO, r.ErrorCode);
            StringAssert.Contains(r.Mensaje, "ya se ha usado en la tienda online");
        }

        [TestMethod]
        public async Task ComprobarCarrito_Caducado()
        {
            cupon = CuponesTests.Cupon(desde: HOY.Date.AddMonths(-7));

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));

            Assert.AreEqual(ReglasCupon.ERROR_CADUCADO, r.ErrorCode);
        }

        // ================= ReglasCupon.ElegirParaPedido (la regla de Nesto, ahora en la API) =================

        private static CuponDTO Dto(string codigo, string estado, DateTime caducidad, int? pedido = null) => new CuponDTO
        {
            Codigo = codigo,
            Estado = estado,
            SePuedeUsar = estado == EstadosCupon.ACTIVO,
            Caducidad = caducidad,
            UsadoEnPedido = pedido
        };

        [TestMethod]
        public void ElegirParaPedido_ElDeEstePedido_LuegoElQueSePuedeUsarYCaducaAntes_LuegoElEmitido()
        {
            CuponDTO enEste = Dto("A", EstadosCupon.USADO, HOY.AddMonths(5), PEDIDO);
            CuponDTO activoTarde = Dto("B", EstadosCupon.ACTIVO, HOY.AddMonths(4));
            CuponDTO activoPronto = Dto("C", EstadosCupon.ACTIVO, HOY.AddMonths(2));
            CuponDTO emitido = Dto("D", EstadosCupon.EMITIDO, HOY.AddMonths(1));

            Assert.AreEqual("A", ReglasCupon.ElegirParaPedido(new[] { activoTarde, emitido, enEste, activoPronto }, PEDIDO).Codigo);
            Assert.AreEqual("C", ReglasCupon.ElegirParaPedido(new[] { activoTarde, emitido, enEste, activoPronto }).Codigo);
            Assert.AreEqual("D", ReglasCupon.ElegirParaPedido(new[] { emitido, enEste }).Codigo);
            Assert.IsNull(ReglasCupon.ElegirParaPedido(new[] { enEste }));
            Assert.IsNull(ReglasCupon.ElegirParaPedido(null));
        }

        // ================= POST api/Cupones/{codigo}/Usar desde la tienda (sin pedido de Nesto) =================

        private ServicioCupones Servicio(bool espejoEncendido = false) =>
            new ServicioCupones(repositorio, null, () => HOY, PRODUCTO, espejo, () => correo, () => espejoEncendido);

        private static UsarCuponTiendaDTO UsoTienda(decimal? importe = 50) =>
            new UsarCuponTiendaDTO { Empresa = "1", Canal = "Prestashop", PedidoCanal = REFERENCIA, ImporteDescontado = importe };

        private static IPrincipal SinJwt() => new ClaimsPrincipal(new ClaimsIdentity());

        private static IPrincipal Casa() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") }, "prueba"));

        private void ResultadoUso(ResultadoUsoDesdeTienda resultado) =>
            A.CallTo(() => repositorio.UsarDesdeTienda(A<int>._, A<string>._, A<string>._, A<string>._, A<decimal?>._, A<string>._))
                .Returns(Task.FromResult(resultado));

        [TestMethod]
        public async Task UsarDesdeTienda_ConApiKey_QuedaUsadoSinPedidoConLaReferenciaYElCanal_ConElEspejoApagado()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.Usado);

            UsoCuponTiendaDTO r = await Servicio(espejoEncendido: false).UsarEnTienda(CODIGO, UsoTienda(), SinJwt(), conApiKey: true);

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, r.Resultado);
            Assert.IsFalse(r.AvisoAdministracion);
            StringAssert.Contains(r.Mensaje, REFERENCIA);
            A.CallTo(() => repositorio.UsarDesdeTienda(7, "1", "PrestaShop", REFERENCIA, 50M, CarritoCupones.USUARIO_TIENDA)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.UsarEnTienda(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarDesdeTienda_ElMismoPedidoDosVeces_NoFalla()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.YaConEstaReferencia);

            UsoCuponTiendaDTO r = await Servicio().UsarEnTienda(CODIGO, UsoTienda(), SinJwt(), conApiKey: true);

            Assert.AreEqual(ResultadosUsoCuponTienda.YA_EN_ESTE_PEDIDO, r.Resultado);
            Assert.IsFalse(r.AvisoAdministracion);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarDesdeTienda_YaUsadoEnUnPedidoDeNesto_ElPedidoDeLaTiendaSigueYSeAvisaAAdministracion()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.NoLibre);
            cupon = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: 930400);
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            UsoCuponTiendaDTO r = await Servicio().UsarEnTienda(CODIGO, UsoTienda(), SinJwt(), conApiKey: true);

            Assert.AreEqual(ResultadosUsoCuponTienda.CONFLICTO, r.Resultado);
            Assert.IsTrue(r.AvisoAdministracion);
            StringAssert.Contains(r.Mensaje, "ya estaba usado en el pedido 930400");
            StringAssert.Contains(r.Mensaje, REFERENCIA);
            StringAssert.Contains(enviado.Body, "todavía no ha entrado en Nesto");
        }

        [TestMethod]
        public async Task UsarDesdeTienda_YaGastadoEnOtroPedidoDeLaTienda_Conflicto()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.NoLibre);
            cupon = CuponesTests.Cupon(EstadosCupon.USADO);
            cupon.CanalUso = "PrestaShop";
            A.CallTo(() => repositorio.LeerUsoTienda(7)).Returns(Task.FromResult(new UsoTiendaCupon { PedidoCanal = "OTRAREFER" }));

            UsoCuponTiendaDTO r = await Servicio().UsarEnTienda(CODIGO, UsoTienda(), SinJwt(), conApiKey: true);

            Assert.AreEqual(ResultadosUsoCuponTienda.CONFLICTO, r.Resultado);
            StringAssert.Contains(r.Mensaje, "ya se había usado en la tienda online (pedido OTRAREFER de la tienda)");
        }

        [TestMethod]
        public async Task UsarDesdeTienda_SinElScript_503ParaQueElModuloLoReintente()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.SinColumnas);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().UsarEnTienda(CODIGO, UsoTienda(), SinJwt(), conApiKey: true));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            StringAssert.Contains(ex.Message, "Issue694");
        }

        [TestMethod]
        public async Task UsarDesdeTienda_ImporteDistintoDelCupon_SeUsaPeroLoDice()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.Usado);

            UsoCuponTiendaDTO r = await Servicio().UsarEnTienda(CODIGO, UsoTienda(45), SinJwt(), conApiKey: true);

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, r.Resultado);
            StringAssert.Contains(r.Mensaje, "la tienda ha descontado 45,00 €");
        }

        [TestMethod]
        public async Task UsarDesdeTienda_NoExiste_404()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().UsarEnTienda("EV-0000-XXXX", UsoTienda(), SinJwt(), conApiKey: true));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task UsarDesdeTienda_ElClienteDeLaTiendaConJwt_403()
        {
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", CLIENTE) }, "prueba"));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().UsarEnTienda(CODIGO, UsoTienda(), cliente));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task UsarDesdeTienda_SinPedidoNiReferencia_400()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().UsarEnTienda(CODIGO, new UsarCuponTiendaDTO { Empresa = "1" }, SinJwt(), conApiKey: true));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public async Task UsarDesdeNesto_ConElPedido_YaApuntadoPorLaTiendaConEsaReferencia_NoEsConflictoAunqueElEspejoEsteApagado()
        {
            // Nesto#518 llama a Usar al importar el pedido; el módulo ya lo apuntó al validar el pedido en la tienda
            cupon = CuponesTests.Cupon(EstadosCupon.USADO);
            cupon.CanalUso = "PrestaShop";
            A.CallTo(() => repositorio.LeerUsoTienda(7)).Returns(Task.FromResult(new UsoTiendaCupon { PedidoCanal = REFERENCIA, ImporteDescontado = 50 }));
            var peticion = new UsarCuponTiendaDTO { Empresa = "1", Pedido = PEDIDO, Canal = "PrestaShop", PedidoCanal = REFERENCIA, ImporteDescontado = 50 };

            UsoCuponTiendaDTO r = await Servicio(espejoEncendido: false).UsarEnTienda(CODIGO, peticion, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.YA_EN_ESTE_PEDIDO, r.Resultado);
            Assert.IsFalse(r.AvisoAdministracion);
            Assert.AreEqual(REFERENCIA, r.Cupon.PedidoCanal);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarDesdeNesto_ConElPedido_ConElEspejoApagado_SigueSiendo400()
        {
            var peticion = new UsarCuponTiendaDTO { Empresa = "1", Pedido = PEDIDO, PedidoCanal = REFERENCIA };

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(espejoEncendido: false).UsarEnTienda(CODIGO, peticion, Casa()));

            StringAssert.Contains(ex.Message, ReglasEspejoPrestashop.CLAVE_INTERRUPTOR);
        }

        // ================= El cupón gastado en la tienda (Usado sin pedido de Nesto) =================

        [TestMethod]
        public void GastadoEnLaTiendaSinPedido_NoEstaLibre()
        {
            CuponLeido gastado = CuponesTests.Cupon(EstadosCupon.USADO);
            gastado.CanalUso = "PrestaShop";

            Assert.AreEqual(EstadosCupon.USADO, ReglasCupon.EstadoEfectivo(gastado, HOY), "Sin pedido no es «su pedido ya no tiene la línea»");
            CuponDTO dto = ReglasCupon.ADTO(gastado, HOY, PRODUCTO);
            Assert.IsFalse(dto.SePuedeUsar);
            Assert.AreEqual("PrestaShop", dto.CanalUso);
            StringAssert.Contains(dto.Mensaje, "ya se ha usado en la tienda online");
        }

        [TestMethod]
        public void UsadoEnUnPedidoQueYaNoTieneLaLinea_SigueLibreComoAntes()
        {
            CuponLeido suelto = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: 930400);
            suelto.PedidoUsoTieneLaLinea = false;

            Assert.AreEqual(EstadosCupon.ACTIVO, ReglasCupon.EstadoEfectivo(suelto, HOY));
        }

        [TestMethod]
        public async Task LeerPorCodigo_GastadoEnLaTienda_TraeLaReferenciaYLoDescontado()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO);
            cupon.CanalUso = "PrestaShop";
            A.CallTo(() => repositorio.LeerUsoTienda(7)).Returns(Task.FromResult(new UsoTiendaCupon { PedidoCanal = REFERENCIA, ImporteDescontado = 50 }));

            CuponDTO c = await Servicio().LeerPorCodigo(CODIGO, Casa());

            Assert.AreEqual(REFERENCIA, c.PedidoCanal);
            Assert.AreEqual(50M, c.ImporteDescontado);
            Assert.IsNull(c.UsadoEnPedido);
        }

        [TestMethod]
        public async Task LeerPorCodigo_LibreNoPreguntaPorLaTienda()
        {
            CuponDTO c = await Servicio().LeerPorCodigo(CODIGO, Casa());

            Assert.IsNull(c.PedidoCanal);
            A.CallTo(() => repositorio.LeerUsoTienda(A<int>._)).MustNotHaveHappened();
        }

        // ================= El pedido de la tienda entra por CanalesExternos =================

        private static LinPedidoVta Linea(string producto, decimal total) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = producto,
            Cantidad = 1,
            Precio = total,
            Total = total,
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            Forma_Venta = Constantes.FormasVenta.TIENDA_ONLINE
        };

        private static LinPedidoVta LineaCupon(string formaVenta) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = PRODUCTO,
            Cantidad = -1,
            Precio = 41.3223M,
            Total = -50,
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            Forma_Venta = formaVenta
        };

        private static PedidoVentaDTO PedidoConCodigo(string formaVenta) => new PedidoVentaDTO
        {
            empresa = "1",
            numero = PEDIDO,
            cliente = CLIENTE,
            iva = "G21",
            Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "CREMA", Cantidad = 1, PrecioUnitario = 40, formaVenta = formaVenta },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = PRODUCTO, Cantidad = -1, CodigoCupon = CODIGO, formaVenta = formaVenta }
            }
        };

        [TestMethod]
        public async Task PedidoDeCanalesExternos_ConElCuponGastadoEnLaTienda_SeAtaAlPedidoSinMirarElMinimo()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO);
            cupon.CanalUso = "PrestaShop";
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).Returns(Task.FromResult(true));
            ServicioCanjeCupones canje = Canje();
            await canje.NormalizarLineas(PedidoConCodigo(Constantes.FormasVenta.TIENDA_ONLINE));

            // 48,40 € de productos: por debajo del mínimo de 100, pero la tienda ya lo aplicó y lo cobró
            PlanCanjeCupon plan = await canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 48.40M), LineaCupon(Constantes.FormasVenta.TIENDA_ONLINE) }, "G21",
                null, "x", pedidoNuevo: true, teniaLineaCupon: false);
            await canje.Reservar(plan);

            Assert.AreEqual(7, plan.CuponAReservar.Id, "El cupón se ata al pedido de Nesto");
            A.CallTo(() => repositorio.Reservar(7, "1", PEDIDO, Constantes.FormasVenta.TIENDA_ONLINE, "x")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PedidoDeCanalesExternos_AlModificarloDespues_TampocoMiraElMinimo()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO);
            cupon.CanalUso = "PrestaShop";
            A.CallTo(() => repositorio.LeerDelPedido(PEDIDO)).ReturnsLazily(() => Task.FromResult(cupon));

            PlanCanjeCupon plan = await Canje().Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 20), LineaCupon(Constantes.FormasVenta.TIENDA_ONLINE) }, "G21",
                null, "x", pedidoNuevo: false, teniaLineaCupon: true);

            Assert.AreEqual(7, plan.CuponAReservar.Id);
        }

        [TestMethod]
        public async Task OtroPedidoQueNoEsDeLaTienda_ConElCuponGastadoEnLaTienda_YaUsado()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO);
            cupon.CanalUso = "PrestaShop";
            ServicioCanjeCupones canje = Canje();
            await canje.NormalizarLineas(PedidoConCodigo("TIE"));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 200), LineaCupon("TIE") }, "G21", null, "x", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_YA_USADO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "ya se ha usado en la tienda online");
        }

        [TestMethod]
        public async Task PedidoDeCanalesExternos_ConElCuponGastadoEnLaTiendaPorOtroCliente_DeOtroCliente()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO, cliente: "20000");
            cupon.CanalUso = "PrestaShop";
            ServicioCanjeCupones canje = Canje();
            await canje.NormalizarLineas(PedidoConCodigo(Constantes.FormasVenta.TIENDA_ONLINE));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 200), LineaCupon(Constantes.FormasVenta.TIENDA_ONLINE) }, "G21", null, "x",
                pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_OTRO_CLIENTE, ex.Context.ErrorCode);
        }

        [TestMethod]
        public void Sql_ElCuponGastadoEnLaTiendaSinPedidoNoSeAnulaNiSeSueltaYConservaSuCanalAlAtarse()
        {
            StringAssert.Contains(RepositorioCupones.SQL_RESERVAR, "c.UsadoEnPedido = @p2 OR c.UsadoEnPedido IS NULL) THEN c.CanalUso");
            StringAssert.Contains(RepositorioCupones.SQL_ANULAR.Replace("\r", "").Replace("\n", " "), "c.UsadoEnPedido IS NOT NULL");
            StringAssert.Contains(RepositorioCupones.SQL_USAR_DESDE_TIENDA, "COL_LENGTH('dbo.Cupones', 'PedidoCanalUso') IS NULL");
            StringAssert.Contains(RepositorioCupones.SQL_USAR_DESDE_TIENDA, "PedidoCanalUso = @ref");
            StringAssert.Contains(RepositorioCupones.SQL_LEER_USO_TIENDA, "COL_LENGTH('dbo.Cupones', 'PedidoCanalUso') IS NULL");
        }

        // ================= El controller y la credencial =================

        private CuponesController Controlador(IPrincipal usuario = null)
        {
            var controller = new CuponesController(A.Fake<NVEntities>(), Servicio())
            {
                ServicioCanje = Canje(),
                Request = new HttpRequestMessage()
            };
            controller.RequestContext = new HttpRequestContext { Principal = usuario ?? SinJwt() };
            return controller;
        }

        [TestMethod]
        public async Task ControllerComprobarCarrito_ConApiKey_200ConElResultado()
        {
            var ok = await Controlador().ComprobarCarrito(Carrito(CODIGO, ("CREMA", 100M))) as OkNegotiatedContentResult<ComprobacionCarritoCuponDTO>;

            Assert.IsNotNull(ok);
            Assert.IsTrue(ok.Content.Aplicable);
        }

        [TestMethod]
        public async Task ControllerComprobarCarrito_SinCliente_400()
        {
            Assert.IsInstanceOfType(await Controlador().ComprobarCarrito(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await Controlador().ComprobarCarrito(new ComprobarCarritoCuponDTO { Empresa = "1" }), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ControllerComprobarCarrito_UnClienteDeLaTiendaConJwt_SoloElSuyo()
        {
            var otro = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", "20000") }, "JWT"));

            var resultado = await Controlador(otro).ComprobarCarrito(Carrito(CODIGO, ("CREMA", 100M))) as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public async Task ControllerGetCuponesCliente_ConApiKey_LosDelCliente()
        {
            var ok = await Controlador().GetCuponesCliente("1", CLIENTE) as OkNegotiatedContentResult<List<CuponDTO>>;

            Assert.AreEqual(CODIGO, ok.Content.Single().Codigo);
            Assert.IsTrue(ok.Content.Single().SePuedeUsar);
        }

        [TestMethod]
        public async Task ControllerUsar_ConApiKey_UsaDesdeLaTienda()
        {
            ResultadoUso(ResultadoUsoDesdeTienda.Usado);

            var ok = await Controlador().PostUsarCupon(CODIGO, UsoTienda()) as OkNegotiatedContentResult<UsoCuponTiendaDTO>;

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, ok.Content.Resultado);
            A.CallTo(() => repositorio.UsarDesdeTienda(7, "1", "PrestaShop", REFERENCIA, 50M, CarritoCupones.USUARIO_TIENDA)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        [DataRow(nameof(CuponesController.GetCuponesCliente))]
        [DataRow(nameof(CuponesController.ComprobarCarrito))]
        [DataRow(nameof(CuponesController.PostUsarCupon))]
        public void Endpoints_AdmitenJwtOApiKeyDePrestashop_SinModoSombra(string metodo)
        {
            MethodInfo info = typeof(CuponesController).GetMethod(metodo);
            AutorizadoOApiKeyAttribute atributo = info.GetCustomAttributes<AutorizadoOApiKeyAttribute>(inherit: false).FirstOrDefault();

            Assert.IsNotNull(atributo, $"{metodo} tiene que exigir JWT o la API key de PrestaShop");
            Assert.AreEqual("ApiKeyPrestashop", atributo.NombreSetting, "La misma clave que el cheque regalo");
            Assert.AreEqual("X-API-KEY", atributo.Cabecera);
            Assert.IsTrue(atributo.ExigirSiempre, "Sin ExigirSiempre, con el interruptor de #459 apagado quedaría abierto a internet");
            Assert.IsFalse(info.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any(),
                "[Authorize] junto al atributo exigiría las dos credenciales y la tienda (solo API key) no pasaría");
        }

        [TestMethod]
        public void LaClaseNoLlevaAuthorize_YElRestoDeEndpointsSiguenConJwt()
        {
            Assert.IsFalse(typeof(CuponesController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(),
                "En la clase se sumaría al atributo de la API key");
            foreach (string metodo in new[] { nameof(CuponesController.GetCupon), nameof(CuponesController.PostCupon), nameof(CuponesController.PostAnularCupon) })
            {
                Assert.IsTrue(typeof(CuponesController).GetMethod(metodo).GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any(), metodo);
            }
        }
    }
}
