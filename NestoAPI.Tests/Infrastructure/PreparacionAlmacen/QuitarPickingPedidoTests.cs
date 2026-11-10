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
    /// NestoAPI#590 (slice «Quitar picking», 09/11/26). Caso: picking 100033, Pedro recogiendo en Ariadna; Alfredo quita el pedido
    /// 930110 del picking (antes, desde Nesto viejo) y Pedro ya había leído productos de ese pedido.
    /// </summary>
    [TestClass]
    public class QuitarPickingPedidoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 930110;
        private const int PICKING = 100033;

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
            servicio = new ServicioQuitarPicking(repositorio);
            A.CallTo(() => tx.LeerLineasConPicking(EMPRESA, PEDIDO)).Returns(new List<LineaPedidoConPicking>
            {
                Linea(1, "12485", "Agujas Miliun", 20),
                Linea(2, "22530", "Crema", 3),
                Linea(3, "CHEQUE", "Cheque regalo", 1, ficticio: true)
            });
            A.CallTo(() => tx.LeerLineasDeOtrosPedidos(EMPRESA, PICKING, PEDIDO)).Returns(new List<LineaPedidoConPicking>
            {
                new LineaPedidoConPicking { Linea = 9, Pedido = 930111, Picking = PICKING, Estado = 1, TipoLinea = 1, Producto = "22530", Cantidad = 2 }
            });
            A.CallTo(() => tx.LeerLecturas(EMPRESA, PICKING)).Returns(new List<LecturaNetaPicking>());
        }

        private static LineaPedidoConPicking Linea(int linea, string producto, string nombre, int cantidad, short estado = 1, bool ficticio = false)
            => new LineaPedidoConPicking { Linea = linea, Pedido = PEDIDO, Picking = PICKING, Estado = estado, TipoLinea = 1, Producto = producto, Nombre = nombre, Cantidad = cantidad, Ficticio = ficticio };

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

        [TestMethod]
        public async Task Quitar_SinLecturas_SueltaLasReservasDeCadaLineaYLoApunta()
        {
            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"));

            Assert.AreEqual(EstadoQuitarPicking.Quitado, resultado.Estado);
            Assert.AreEqual(3, resultado.Respuesta.Lineas);
            CollectionAssert.AreEqual(new List<int> { PICKING }, resultado.Respuesta.Pickings);
            A.CallTo(() => tx.QuitarPickingDeLaLinea(1, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(2, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            // La línea del cheque regalo también sale del picking (la del 930110 se quedó fuera al volver a sacarlo)
            A.CallTo(() => tx.QuitarPickingDeLaLinea(3, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.InsertarEscaneo(A<string>._, A<EscaneoAlmacenDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.Auditar(A<string>.That.Contains("100033"), A<string>.That.Contains("sin picking"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Quitar_ConLecturasDelPedidoSinConfirmar_DevuelveLasLecturasYNoTocaNada()
        {
            PedroHaLeido(("12485", 20));

            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"));

            Assert.AreEqual(EstadoQuitarPicking.HayLecturas, resultado.Estado);
            LecturaQuitarPickingDTO lectura = resultado.Respuesta.Lecturas.Single();
            Assert.AreEqual("Pedro", lectura.Usuario);
            Assert.AreEqual("PDA-3", lectura.Dispositivo);
            Assert.AreEqual("12485", lectura.Producto);
            Assert.AreEqual("Agujas Miliun", lectura.Nombre);
            Assert.AreEqual(20, lectura.Cantidad);
            StringAssert.Contains(resultado.Respuesta.Message, "recogiendo el picking 100033");
            StringAssert.Contains(resultado.Respuesta.Message, "20 Agujas Miliun");
            StringAssert.Contains(resultado.Respuesta.Message, "apartarlas");
            A.CallTo(() => tx.QuitarPickingDeLaLinea(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tx.InsertarEscaneo(A<string>._, A<EscaneoAlmacenDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Quitar_ConfirmadoConLecturas_AnulaLoLeidoConElMotivoYAPedro()
        {
            PedroHaLeido(("12485", 20));

            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"));

            Assert.AreEqual(EstadoQuitarPicking.Quitado, resultado.Estado);
            Assert.AreEqual(20, resultado.Respuesta.Lecturas.Single().Cantidad);
            A.CallTo(() => tx.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>.That.Matches(e =>
                    e.TipoOrigen == "PICK" && e.NumeroOrigen == PICKING && e.Producto == "12485" && e.Cantidad == -20 &&
                    e.Fase == "PICK" && e.Metodo != "FALTA" && e.TotalParada == 10 && e.Pedido == PEDIDO &&
                    e.Motivo.StartsWith("Pedido 930110 sacado del picking por")), "Pedro"))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => tx.QuitarPickingDeLaLinea(1, EMPRESA, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void QueHayQueApartar_LoQueSiguenPidiendoLosDemasPedidosNoSeAparta()
        {
            // 22530: el 930110 pedía 3 y el 930111 (que sigue) 2; Pedro ha leído 4 → sobran 2, del 930110
            List<(LecturaNetaPicking Lectura, int Unidades)> apartar = ServicioQuitarPicking.QueHayQueApartar(
                new[] { Linea(2, "22530", "Crema", 3) },
                new[] { new LineaPedidoConPicking { Producto = "22530", TipoLinea = 1, Cantidad = 2, Estado = 1 } },
                new[] { new LecturaNetaPicking { Producto = "22530", Usuario = "Pedro", Unidades = 4 } });

            Assert.AreEqual(2, apartar.Single().Unidades);

            List<(LecturaNetaPicking Lectura, int Unidades)> nada = ServicioQuitarPicking.QueHayQueApartar(
                new[] { Linea(2, "22530", "Crema", 3) },
                new[] { new LineaPedidoConPicking { Producto = "22530", TipoLinea = 1, Cantidad = 2, Estado = 1 } },
                new[] { new LecturaNetaPicking { Producto = "22530", Usuario = "Pedro", Unidades = 2 } });

            Assert.AreEqual(0, nada.Count);
        }

        [TestMethod]
        public async Task Quitar_ConUnaLineaYaEnAlbaran_NoSePuede()
        {
            A.CallTo(() => tx.LeerLineasConPicking(EMPRESA, PEDIDO)).Returns(new List<LineaPedidoConPicking>
            {
                Linea(1, "12485", "Agujas Miliun", 20),
                Linea(2, "22530", "Crema", 3, estado: 2)
            });

            ResultadoQuitarPicking resultado = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"));

            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, resultado.Estado);
            StringAssert.Contains(resultado.Respuesta.Message, "albarán");
            A.CallTo(() => tx.QuitarPickingDeLaLinea(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Quitar_PickingTerminadoOEmpaquetando_NoSePuede()
        {
            A.CallTo(() => tx.BloquearYMirarSiTerminado(EMPRESA, PICKING)).Returns(true);
            ResultadoQuitarPicking terminado = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"));
            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, terminado.Estado);
            // Novedad 1146 (revisión): quien pulsa el botón ES el almacén; no se le manda a «hablar con el almacén».
            StringAssert.Contains(terminado.Respuesta.Message, "Desde Nesto todavía no se puede quitar el picking de lo ya cogido.");
            Assert.IsFalse(terminado.Respuesta.Message.Contains("hable con el almacén"));

            A.CallTo(() => tx.BloquearYMirarSiTerminado(EMPRESA, PICKING)).Returns(false);
            A.CallTo(() => tx.HayPacking(EMPRESA, PICKING, PEDIDO)).Returns(true);
            ResultadoQuitarPicking empaquetando = await servicio.Quitar(EMPRESA, PEDIDO, true, Usuario("Almacén"));
            Assert.AreEqual(EstadoQuitarPicking.NoSePuede, empaquetando.Estado);
            StringAssert.Contains(empaquetando.Respuesta.Message, "empaquetando");

            A.CallTo(() => tx.QuitarPickingDeLaLinea(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Quitar_SinPickingOSinPermiso()
        {
            A.CallTo(() => tx.LeerLineasConPicking(EMPRESA, PEDIDO)).Returns(new List<LineaPedidoConPicking>());
            Assert.AreEqual(EstadoQuitarPicking.SinPicking, (await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Almacén"))).Estado);

            Assert.AreEqual(EstadoQuitarPicking.SinPermiso, (await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Vendedores"))).Estado);
            // Carlos 09/11/26: Administración no quita picking (solo Almacén y Dirección)
            Assert.AreEqual(EstadoQuitarPicking.SinPermiso, (await servicio.Quitar(EMPRESA, PEDIDO, false, Usuario("Administración"))).Estado);
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionQuitarPicking, Task<ResultadoQuitarPicking>>>._)).MustHaveHappenedOnceExactly();
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
        public async Task Controlador_HayLecturas409ConEllas_Quitado200YAvisaALaPdaConLoQueHayQueApartar()
        {
            var falso = A.Fake<IServicioQuitarPicking>();
            var avisos = A.Fake<IAvisosAlmacen>();
            var lecturas = new List<LecturaQuitarPickingDTO>
            {
                new LecturaQuitarPickingDTO { Picking = PICKING, Usuario = "Pedro", Producto = "12485", Nombre = "Agujas Miliun", Cantidad = 20 }
            };
            A.CallTo(() => falso.Quitar(EMPRESA, PEDIDO, false, A<IPrincipal>._)).Returns(new ResultadoQuitarPicking
            {
                Estado = EstadoQuitarPicking.HayLecturas,
                Respuesta = new ResultadoQuitarPickingDTO { Message = "Pedro está recogiendo", Pedido = PEDIDO, Pickings = new List<int> { PICKING }, Lecturas = lecturas }
            });
            A.CallTo(() => falso.Quitar(EMPRESA, PEDIDO, true, A<IPrincipal>._)).Returns(new ResultadoQuitarPicking
            {
                Estado = EstadoQuitarPicking.Quitado,
                Respuesta = new ResultadoQuitarPickingDTO { Message = "Quitado", Pedido = PEDIDO, Pickings = new List<int> { PICKING }, Lecturas = lecturas }
            });
            QuitarPickingController controlador = Controlador(falso, avisos);

            var conflicto = (NegotiatedContentResult<ResultadoQuitarPickingDTO>)await controlador.PostQuitarPicking(EMPRESA, PEDIDO, new QuitarPickingDTO());
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(1, conflicto.Content.Lecturas.Count);
            A.CallTo(() => avisos.PedidoSacadoDelPicking(A<string>._, A<string>._, A<PedidoSacadoDelPickingAvisoDTO>._)).MustNotHaveHappened();

            IHttpActionResult hecho = await controlador.PostQuitarPicking(EMPRESA, PEDIDO, new QuitarPickingDTO { Confirmado = true });
            Assert.IsInstanceOfType(hecho, typeof(OkNegotiatedContentResult<ResultadoQuitarPickingDTO>));
            A.CallTo(() => avisos.PedidoSacadoDelPicking("Recogida:PICK:100033", A<string>._, A<PedidoSacadoDelPickingAvisoDTO>.That.Matches(a =>
                    a.Pedido == PEDIDO && a.NumeroOrigen == PICKING && a.Apartar.Count == 1 &&
                    a.Mensaje.Contains("El pedido 930110 se ha sacado de este picking") && a.Mensaje.Contains("aparte 20 Agujas Miliun"))))
                .MustHaveHappenedOnceExactly();
        }
    }
}
