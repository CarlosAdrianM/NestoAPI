using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#556: las rutas de Ariadna (api/Almacen).</summary>
    [TestClass]
    public class AlmacenControllerTests
    {
        private IServicioPreparacionAlmacen servicio;
        private IServicioUbicacionesAlmacen ubicaciones;
        private IServicioRecepcionCompras compras;
        private IServicioRecepcionReposiciones reposiciones;
        private AlmacenController controller;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioPreparacionAlmacen>();
            ubicaciones = A.Fake<IServicioUbicacionesAlmacen>();
            compras = A.Fake<IServicioRecepcionCompras>();
            reposiciones = A.Fake<IServicioRecepcionReposiciones>();
            controller = new AlmacenController(servicio, ubicaciones, compras, reposiciones)
            {
                User = new GenericPrincipal(new GenericIdentity("Andrey", "Bearer"), new string[0]),
                Request = new HttpRequestMessage()
            };
        }

        [TestMethod]
        public void ElControlador_ExigeAutenticacion()
        {
            // Rutas nuevas: con [Authorize] desde el primer día
            Assert.IsTrue(typeof(AlmacenController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public void GetPing_DevuelveLaHoraElUsuarioDelTokenYSiHayAlmacenamientoDeFotos()
        {
            A.CallTo(() => servicio.FotosConfiguradas).Returns(true);

            PingAlmacenDTO ping = ((OkNegotiatedContentResult<PingAlmacenDTO>)controller.GetPing()).Content;

            Assert.AreEqual("Andrey", ping.Usuario);
            Assert.IsTrue(ping.FotosConfiguradas);
            Assert.IsTrue((DateTime.Now - ping.HoraServidor).TotalSeconds < 5);
        }

        [TestMethod]
        public async Task GetRecogida_TipoQueNoExiste_BadRequest()
        {
            Assert.IsInstanceOfType(await controller.GetRecogida("OTRO", 5), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.LeerRecogida(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetRecogida_NoExiste_NotFound()
        {
            A.CallTo(() => servicio.LeerRecogida("1", "REPO", 5)).Returns(Task.FromResult<RecogidaAlmacenDTO>(null));

            Assert.IsInstanceOfType(await controller.GetRecogida("REPO", 5), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetRecogida_Existe_LaDevuelve()
        {
            var recogida = new RecogidaAlmacenDTO { Tipo = "PICK", Numero = 5, SiguienteOrden = 2 };
            A.CallTo(() => servicio.LeerRecogida("1", "pick", 5)).Returns(recogida);

            IHttpActionResult resultado = await controller.GetRecogida("pick", 5);

            Assert.AreSame(recogida, ((OkNegotiatedContentResult<RecogidaAlmacenDTO>)resultado).Content);
        }

        [TestMethod]
        public async Task GetRecogidas_PorDefectoLasDeAlgete()
        {
            var lista = new List<RecogidaPendienteDTO> { new RecogidaPendienteDTO { Tipo = "PICK", Numero = 5 } };
            A.CallTo(() => servicio.LeerRecogidasPendientes("1", "ALG")).Returns(lista);

            IHttpActionResult resultado = await controller.GetRecogidas();

            Assert.AreSame(lista, ((OkNegotiatedContentResult<List<RecogidaPendienteDTO>>)resultado).Content);
        }

        [TestMethod]
        public async Task GetPicking_PickingSinLineas_NotFound()
        {
            A.CallTo(() => servicio.LeerPicking("1", 5)).Returns(new PickingAlmacenDTO());

            Assert.IsInstanceOfType(await controller.GetPicking(5), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetPicking_EmpresaEnBlanco_UsaLaEmpresaPorDefecto()
        {
            var picking = new PickingAlmacenDTO { Lineas = { new LineaPickingAlmacenDTO { Producto = "A" } } };
            A.CallTo(() => servicio.LeerPicking("1", 5)).Returns(picking);

            IHttpActionResult resultado = await controller.GetPicking(5, "  ");

            Assert.AreSame(picking, ((OkNegotiatedContentResult<PickingAlmacenDTO>)resultado).Content);
        }

        [TestMethod]
        public async Task GetPackingDelPedido_PedidoSinPicking_NotFound()
        {
            A.CallTo(() => servicio.LeerPackingDePedido("1", 926940)).Returns(Task.FromResult<PackingAlmacenDTO>(null));

            Assert.IsInstanceOfType(await controller.GetPackingDelPedido(926940), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task PostEscaneos_SinEscaneos_BadRequest()
        {
            Assert.IsInstanceOfType(await controller.PostEscaneos(new List<EscaneoAlmacenDTO>()), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await controller.PostEscaneos(null), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostEscaneos_GuardaConElUsuarioAutenticadoNoConUnoQueMandeElMovil()
        {
            var escaneos = new List<EscaneoAlmacenDTO> { new EscaneoAlmacenDTO { IdCliente = Guid.NewGuid() } };

            _ = await controller.PostEscaneos(escaneos);

            A.CallTo(() => servicio.GuardarEscaneos("1", escaneos, "Andrey")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostFotoBulto_MandaAlServicioLaImagenDelCuerpoYLosDatosDeLaRuta()
        {
            byte[] imagen = { 0xFF, 0xD8, 0xFF, 1, 2 };
            controller.Request.Content = new ByteArrayContent(imagen);
            Guid id = Guid.NewGuid();
            FotoBultoAlmacen recibida = null;
            A.CallTo(() => servicio.GuardarFotoBulto(A<FotoBultoAlmacen>._, "Andrey"))
                .Invokes((FotoBultoAlmacen f, string u) => recibida = f)
                .Returns(new BultoAlmacenDTO { Id = 7 });

            IHttpActionResult resultado = await controller.PostFotoBulto(id, 926940, 99633, 2, dispositivo: "PDA-1");

            Assert.AreEqual(7, ((OkNegotiatedContentResult<BultoAlmacenDTO>)resultado).Content.Id);
            Assert.AreEqual(id, recibida.IdCliente);
            Assert.AreEqual(926940, recibida.Pedido);
            Assert.AreEqual(99633, recibida.Picking);
            Assert.AreEqual(2, recibida.Bulto);
            Assert.AreEqual("1", recibida.Empresa);
            Assert.AreEqual("PDA-1", recibida.Dispositivo);
            CollectionAssert.AreEqual(imagen, recibida.Imagen);
        }

        [TestMethod]
        public async Task PostFotoBulto_OtrosPedidosDeLaCaja_LleganAlServicio()
        {
            controller.Request.Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF });
            FotoBultoAlmacen recibida = null;
            A.CallTo(() => servicio.GuardarFotoBulto(A<FotoBultoAlmacen>._, "Andrey"))
                .Invokes((FotoBultoAlmacen f, string u) => recibida = f)
                .Returns(new BultoAlmacenDTO());

            _ = await controller.PostFotoBulto(Guid.NewGuid(), 926940, 99633, 1, otrosPedidos: "926941, 926945");

            CollectionAssert.AreEqual(new[] { 926941, 926945 }, recibida.OtrosPedidos);
        }

        [TestMethod]
        public async Task PostFotoBulto_OtrosPedidosMalEscritos_BadRequest()
        {
            controller.Request.Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF });

            Assert.IsInstanceOfType(await controller.PostFotoBulto(Guid.NewGuid(), 926940, 99633, 1, otrosPedidos: "926941,abc"),
                typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostFotoBulto_FotoDesproporcionada_NoLlegaAlServicio()
        {
            controller.Request.Content = new ByteArrayContent(new byte[ServicioPreparacionAlmacen.TAMANO_MAXIMO_FOTO + 1]);

            IHttpActionResult resultado = await controller.PostFotoBulto(Guid.NewGuid(), 926940, 99633, 1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.GuardarFotoBulto(A<FotoBultoAlmacen>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFotoBulto_DevuelveElEnlaceTemporal()
        {
            A.CallTo(() => servicio.EnlaceFotoBulto(7)).Returns(new Uri("https://cuenta.blob.core.windows.net/bultos/x.jpg?sig=abc"));

            var resultado = (OkNegotiatedContentResult<EnlaceFotoBultoDTO>)await controller.GetFotoBulto(7);

            Assert.AreEqual("https://cuenta.blob.core.windows.net/bultos/x.jpg?sig=abc", resultado.Content.Url);
            Assert.AreEqual(15, resultado.Content.MinutosDeVigencia);
        }

        [TestMethod]
        public async Task GetPendienteDeUbicar_SinAlmacen_EsAlgete()
        {
            var pendiente = new PendienteDeUbicarDTO { Almacen = "ALG" };
            A.CallTo(() => ubicaciones.LeerPendienteDeUbicar("1", "ALG")).Returns(pendiente);

            var resultado = (OkNegotiatedContentResult<PendienteDeUbicarDTO>)await controller.GetPendienteDeUbicar(" ");

            Assert.AreSame(pendiente, resultado.Content);
        }

        [TestMethod]
        public async Task PostUbicar_UbicaConElUsuarioDelToken()
        {
            var ubicar = new UbicarProductoDTO { Producto = "18004", Pasillo = "009", Fila = "003", Columna = "008", Cantidad = 5 };
            var comoQueda = new ProductoAlmacenDTO { Producto = "18004" };
            A.CallTo(() => ubicaciones.Ubicar("1", ubicar, "Andrey")).Returns(comoQueda);

            var resultado = (OkNegotiatedContentResult<ProductoAlmacenDTO>)await controller.PostUbicar(ubicar);

            Assert.AreSame(comoQueda, resultado.Content);
        }

        [TestMethod]
        public async Task GetProductoPorCodigo_SinCodigo_BadRequest()
        {
            Assert.IsInstanceOfType(await controller.GetProductoPorCodigo("  "), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetProductoPorCodigo_PasaElAlmacenEnMayusculas()
        {
            _ = await controller.GetProductoPorCodigo("8436620930427", "rei");

            A.CallTo(() => ubicaciones.BuscarProducto("1", "REI", "8436620930427")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetRecepcionDeCompra_PedidoSinNadaPendiente_NotFound()
        {
            A.CallTo(() => compras.LeerRecepcion("1", 220438)).Returns(Task.FromResult<RecepcionCompraDTO>(null));

            Assert.IsInstanceOfType(await controller.GetRecepcionDeCompra(220438), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task PostCasarCompra_DevuelveLasDiferencias()
        {
            var lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "37049", Cantidad = 1 } };
            var resultado = new ResultadoRecepcionCompraDTO { Pedido = 220438, Cuadra = true };
            A.CallTo(() => compras.Casar("1", 220438, lecturas)).Returns(resultado);

            var respuesta = (OkNegotiatedContentResult<ResultadoRecepcionCompraDTO>)await controller.PostCasarCompra(220438, lecturas);

            Assert.AreSame(resultado, respuesta.Content);
        }

        [TestMethod]
        public async Task GetReposicion_NoExisteParaEseAlmacen_NotFound()
        {
            A.CallTo(() => reposiciones.LeerRecepcion("1", "REI", 80841)).Returns(Task.FromResult<RecepcionReposicionDTO>(null));

            Assert.IsInstanceOfType(await controller.GetReposicion(80841, "rei"), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetFotoBulto_BultoSinFoto_NotFound()
        {
            A.CallTo(() => servicio.EnlaceFotoBulto(7)).Returns(Task.FromResult<Uri>(null));

            Assert.IsInstanceOfType(await controller.GetFotoBulto(7), typeof(NotFoundResult));
        }
    }
}
