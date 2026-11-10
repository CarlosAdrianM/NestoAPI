using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Novedad 1146 (Alfredo, 10/11/26): «No siempre se quita picking de un pedido entero, lo más común es quitar alguna línea, por
    /// ejemplo, los portes». QuitarPicking con Lineas: solo esas, con el mismo núcleo y las mismas reglas que el pedido entero.
    /// </summary>
    [TestClass]
    public class QuitarPickingLineasTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 930110;
        private const int PICKING = 100033;
        private const int PORTES = 4;

        private ITransaccionQuitarPicking tx;
        private IRepositorioQuitarPicking repositorio;
        private ServicioQuitarPicking servicio;

        [TestInitialize]
        public void Preparar()
        {
            tx = A.Fake<ITransaccionQuitarPicking>();
            repositorio = A.Fake<IRepositorioQuitarPicking>();
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionQuitarPicking, Task<ResultadoQuitarPicking>>>._))
                .ReturnsLazily((Func<ITransaccionQuitarPicking, Task<ResultadoQuitarPicking>> trabajo) => trabajo(tx));
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionQuitarPicking, Task<ResultadoLineasConPicking>>>._))
                .ReturnsLazily((Func<ITransaccionQuitarPicking, Task<ResultadoLineasConPicking>> trabajo) => trabajo(tx));
            servicio = new ServicioQuitarPicking(repositorio);
            LineasDelPedido(
                Linea(1, "12485", "Agujas Miliun", 20),
                Linea(2, "22530", "Crema", 3),
                Linea(PORTES, "62400003", "Portes", 1, tipoLinea: 2));
            A.CallTo(() => tx.LeerLineasDeOtrosPedidos(EMPRESA, PICKING, PEDIDO)).Returns(new List<LineaPedidoConPicking>());
            A.CallTo(() => tx.LeerLecturas(EMPRESA, PICKING)).Returns(new List<LecturaNetaPicking>());
        }

        private void LineasDelPedido(params LineaPedidoConPicking[] lineas)
        {
            A.CallTo(() => tx.LeerLineasConPicking(EMPRESA, PEDIDO)).Returns(lineas.ToList());
        }

        private static LineaPedidoConPicking Linea(int linea, string producto, string nombre, int cantidad, short estado = 1, short tipoLinea = 1)
            => new LineaPedidoConPicking { Linea = linea, Pedido = PEDIDO, Picking = PICKING, Estado = estado, TipoLinea = tipoLinea, Producto = producto, Nombre = nombre, Cantidad = cantidad };

        private static IPrincipal Usuario(params string[] grupos)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo") }, "prueba");
            return new GenericPrincipal(identidad, grupos);
        }

        private void PedroHaLeido(params (string Producto, int Unidades)[] lecturas)
        {
            A.CallTo(() => tx.LeerLecturas(EMPRESA, PICKING)).Returns(lecturas
                .Select(l => new LecturaNetaPicking { Producto = l.Producto, Usuario = "Pedro", Dispositivo = "PDA-3", TotalParada = 10, Unidades = l.Unidades })
                .ToList());
        }

        private void NadaTocado()
        {
            A.CallTo(() => tx.QuitarPickingDeLaLinea(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.InsertarEscaneo(A<string>._, A<EscaneoAlmacenDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.Auditar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SoloLosPortes_PierdenElPickingYSueltanSuReserva_ElRestoIntacto()
        {
            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"), new[] { PORTES });

            Assert.AreEqual(EstadoQuitarPicking.Quitado, resultado.Estado);
            Assert.AreEqual(1, resultado.Respuesta.Lineas);
            Assert.IsTrue(resultado.Respuesta.Parcial);
            LineaQuitadaPickingDTO quitada = resultado.Respuesta.LineasQuitadas.Single();
            Assert.AreEqual(PORTES, quitada.Linea);
            Assert.IsFalse(quitada.SeRecoge);
            StringAssert.Contains(resultado.Respuesta.Message, "Portes");
            StringAssert.Contains(resultado.Respuesta.Message, "el resto del pedido sigue en el picking");
            // prdDeshacerUbicacionPicking + Picking = 0, solo de los portes
            A.CallTo(() => tx.QuitarPickingDeLaLinea(PORTES, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(1, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(2, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.InsertarEscaneo(A<string>._, A<EscaneoAlmacenDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.Auditar(A<string>.That.Contains("en las líneas 4"), A<string>.That.Contains("líneas 4: sin picking"), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task VariasLineas_SoloEsas()
        {
            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Dirección"), new[] { 2, PORTES, 2 });

            Assert.AreEqual(EstadoQuitarPicking.Quitado, resultado.Estado);
            Assert.AreEqual(2, resultado.Respuesta.Lineas);
            A.CallTo(() => tx.QuitarPickingDeLaLinea(2, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(PORTES, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(1, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LineaAjenaOSinPicking_NoSeTocaNada()
        {
            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"), new[] { PORTES, 777 });

            Assert.AreEqual(EstadoQuitarPicking.LineasNoValidas, resultado.Estado);
            StringAssert.Contains(resultado.Respuesta.Message, "777");
            StringAssert.Contains(resultado.Respuesta.Message, "No se ha quitado nada");
            NadaTocado();
        }

        [TestMethod]
        public async Task LineaEnAlbaran_NoSePuede()
        {
            LineasDelPedido(Linea(1, "12485", "Agujas Miliun", 20), Linea(PORTES, "62400003", "Portes", 1, estado: 2, tipoLinea: 2));

            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"), new[] { PORTES });

            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, resultado.Estado);
            StringAssert.Contains(resultado.Respuesta.Message, "albarán");
            NadaTocado();
        }

        [TestMethod]
        public async Task OtraLineaDelPedidoEnAlbaranEnEsePicking_NoSePuede_ComoElPedidoEntero()
        {
            LineasDelPedido(Linea(1, "12485", "Agujas Miliun", 20, estado: 2), Linea(PORTES, "62400003", "Portes", 1, tipoLinea: 2));

            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"), new[] { PORTES });

            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, resultado.Estado);
            NadaTocado();
        }

        [TestMethod]
        public async Task PickingTerminadoOEmpaquetando_NoSePuede()
        {
            A.CallTo(() => tx.BloquearYMirarSiTerminado(EMPRESA, PICKING)).Returns(true);
            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, (await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"), new[] { PORTES })).Estado);
            A.CallTo(() => tx.BloquearYMirarSiTerminado(EMPRESA, PICKING)).Returns(false);
            A.CallTo(() => tx.HayPacking(EMPRESA, PICKING, PEDIDO)).Returns(true);
            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, (await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"), new[] { PORTES })).Estado);
            NadaTocado();
        }

        [TestMethod]
        public async Task SinPermiso_NiSiquieraAbreLaTransaccion()
        {
            Assert.AreEqual(EstadoQuitarPicking.SinPermiso, (await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Administración"), new[] { PORTES })).Estado);
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionQuitarPicking, Task<ResultadoQuitarPicking>>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LecturasSoloDeEsasLineas_409YLuegoConfirmado()
        {
            PedroHaLeido(("12485", 20), ("22530", 3));

            ResultadoQuitarPicking aviso = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"), new[] { 2 });

            Assert.AreEqual(EstadoQuitarPicking.HayLecturas, aviso.Estado);
            LecturaQuitarPickingDTO lectura = aviso.Respuesta.Lecturas.Single();
            Assert.AreEqual("22530", lectura.Producto);
            Assert.AreEqual(3, lectura.Cantidad);
            NadaTocado();

            ResultadoQuitarPicking hecho = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"), new[] { 2 });

            Assert.AreEqual(EstadoQuitarPicking.Quitado, hecho.Estado);
            A.CallTo(() => tx.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>.That.Matches(e =>
                    e.Producto == "22530" && e.Cantidad == -3 && e.NumeroOrigen == PICKING && e.Pedido == PEDIDO && e.Motivo.StartsWith("Líneas del pedido 930110")), "Pedro"))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.InsertarEscaneo(A<string>._, A<EscaneoAlmacenDTO>.That.Matches(e => e.Producto == "12485"), A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(2, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(1, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LoLeidoQueSigueNecesitandoOtraLineaDelPedido_NoSeAparta()
        {
            // 22530 en dos líneas del pedido (3 y 2); Pedro ha leído 2; se quita la de 2 → la de 3 sigue pidiendo esas 2
            LineasDelPedido(Linea(2, "22530", "Crema", 3), Linea(6, "22530", "Crema", 2));
            PedroHaLeido(("22530", 2));

            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"), new[] { 6 });

            Assert.AreEqual(EstadoQuitarPicking.Quitado, resultado.Estado);
            Assert.AreEqual(0, resultado.Respuesta.Lecturas.Count);
            A.CallTo(() => tx.InsertarEscaneo(A<string>._, A<EscaneoAlmacenDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(6, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(2, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ListaVacia_EsElPedidoEntero()
        {
            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"), new int[0]);

            Assert.AreEqual(EstadoQuitarPicking.Quitado, resultado.Estado);
            Assert.AreEqual(3, resultado.Respuesta.Lineas);
            Assert.IsFalse(resultado.Respuesta.Parcial);
            A.CallTo(() => tx.QuitarPickingDeLaLinea(A<int>._, EMPRESA, A<string>._)).MustHaveHappened(3, Times.Exactly);
        }

        [TestMethod]
        public async Task LeerLineas_DiceCualesSePuedenQuitarYPorQueNo()
        {
            const int OTRO_PICKING = 100040;
            LineasDelPedido(
                Linea(1, "12485", "Agujas Miliun", 20),
                Linea(PORTES, "62400003", "Portes", 1, tipoLinea: 2),
                new LineaPedidoConPicking { Linea = 7, Pedido = PEDIDO, Picking = OTRO_PICKING, Estado = 2, TipoLinea = 1, Producto = "17404", Nombre = "Laca", Cantidad = 1 },
                new LineaPedidoConPicking { Linea = 8, Pedido = PEDIDO, Picking = OTRO_PICKING, Estado = 1, TipoLinea = 1, Producto = "17405", Nombre = "Gel", Cantidad = 1 });

            ResultadoLineasConPicking resultado = await servicio.LeerLineas(EMPRESA, PEDIDO, Usuario("Almacén"));

            Assert.IsFalse(resultado.SinPermiso);
            CollectionAssert.AreEqual(new[] { 1, PORTES, 7, 8 }, resultado.Lineas.Select(l => l.Linea).ToArray());
            Assert.IsTrue(resultado.Lineas.Single(l => l.Linea == PORTES).SePuedeQuitar);
            Assert.AreEqual("Portes", resultado.Lineas.Single(l => l.Linea == PORTES).Texto);
            Assert.AreEqual("Ya está en albarán.", resultado.Lineas.Single(l => l.Linea == 7).Motivo);
            Assert.IsFalse(resultado.Lineas.Single(l => l.Linea == 8).SePuedeQuitar);
            StringAssert.Contains(resultado.Lineas.Single(l => l.Linea == 8).Motivo, "albarán");
            A.CallTo(() => tx.QuitarPickingDeLaLinea(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();

            A.CallTo(() => tx.BloquearYMirarSiTerminado(EMPRESA, PICKING)).Returns(true);
            ResultadoLineasConPicking terminado = await servicio.LeerLineas(EMPRESA, PEDIDO, Usuario("Almacén"));
            StringAssert.Contains(terminado.Lineas.Single(l => l.Linea == PORTES).Motivo, "terminado");

            Assert.IsTrue((await servicio.LeerLineas(EMPRESA, PEDIDO, Usuario("Vendedores"))).SinPermiso);
        }

        private static QuitarPickingController Controlador(IServicioQuitarPicking servicioQuitar, IAvisosAlmacen avisos)
        {
            return new QuitarPickingController(servicioQuitar, avisos)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("Almacén")
            };
        }

        [TestMethod]
        public async Task Controlador_ConLineas_SoloLosPortes_LaPdaReleeSinAvisoFijo()
        {
            var avisos = A.Fake<IAvisosAlmacen>();
            QuitarPickingController controlador = Controlador(servicio, avisos);

            IHttpActionResult hecho = await controlador.PostQuitarPicking(EMPRESA, PEDIDO, new QuitarPickingDTO { Lineas = new List<int> { PORTES } });

            Assert.IsInstanceOfType(hecho, typeof(OkNegotiatedContentResult<ResultadoQuitarPickingDTO>));
            A.CallTo(() => tx.QuitarPickingDeLaLinea(PORTES, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.PedidoSacadoDelPicking(A<string>._, A<string>._, A<PedidoSacadoDelPickingAvisoDTO>._)).MustNotHaveHappened();
            A.CallTo(() => avisos.HaCambiado("Recogida:PICK:100033", A<string>._, null, "Nesto")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_ConLineasDeProducto_AvisaALaPdaDeQueSoloSaleEsaLinea()
        {
            var avisos = A.Fake<IAvisosAlmacen>();
            QuitarPickingController controlador = Controlador(servicio, avisos);

            _ = await controlador.PostQuitarPicking(EMPRESA, PEDIDO, new QuitarPickingDTO { Lineas = new List<int> { 2 } });

            A.CallTo(() => avisos.PedidoSacadoDelPicking("Recogida:PICK:100033", A<string>._, A<PedidoSacadoDelPickingAvisoDTO>.That.Matches(a =>
                    a.Pedido == PEDIDO && a.Mensaje.Contains("Crema (22530)") && a.Mensaje.Contains("el resto del pedido sigue"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_LineaNoValida400_SinListaUsaElDeSiempre()
        {
            var falso = A.Fake<IServicioQuitarPicking>();
            A.CallTo(() => falso.Quitar(EMPRESA, PEDIDO, false, A<IPrincipal>._, A<IReadOnlyCollection<int>>._)).Returns(new ResultadoQuitarPicking
            {
                Estado = EstadoQuitarPicking.LineasNoValidas,
                Respuesta = new ResultadoQuitarPickingDTO { Message = "La línea 777 no es del pedido" }
            });
            A.CallTo(() => falso.Quitar(EMPRESA, PEDIDO, false, A<IPrincipal>._)).Returns(new ResultadoQuitarPicking { Estado = EstadoQuitarPicking.Quitado });
            QuitarPickingController controlador = Controlador(falso, A.Fake<IAvisosAlmacen>());

            var malo = (NegotiatedContentResult<ResultadoQuitarPickingDTO>)await controlador.PostQuitarPicking(EMPRESA, PEDIDO, new QuitarPickingDTO { Lineas = new List<int> { 777 } });
            Assert.AreEqual(HttpStatusCode.BadRequest, malo.StatusCode);

            _ = await controlador.PostQuitarPicking(EMPRESA, PEDIDO, new QuitarPickingDTO { Lineas = new List<int>() });
            _ = await controlador.PostQuitarPicking(EMPRESA, PEDIDO, null);
            A.CallTo(() => falso.Quitar(EMPRESA, PEDIDO, false, A<IPrincipal>._)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => falso.Quitar(EMPRESA, PEDIDO, false, A<IPrincipal>._, A<IReadOnlyCollection<int>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_GetLineasConPicking()
        {
            QuitarPickingController controlador = Controlador(servicio, A.Fake<IAvisosAlmacen>());

            var ok = (OkNegotiatedContentResult<List<LineaConPickingDTO>>)await controlador.GetLineasConPicking(EMPRESA, PEDIDO);

            Assert.AreEqual(3, ok.Content.Count);
            Assert.IsTrue(ok.Content.All(l => l.SePuedeQuitar));
        }
    }
}
