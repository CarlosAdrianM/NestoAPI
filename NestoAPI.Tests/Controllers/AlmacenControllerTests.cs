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
        private AlmacenController controller;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioPreparacionAlmacen>();
            controller = new AlmacenController(servicio)
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
        public async Task GetFotoBulto_BultoSinFoto_NotFound()
        {
            A.CallTo(() => servicio.EnlaceFotoBulto(7)).Returns(Task.FromResult<Uri>(null));

            Assert.IsInstanceOfType(await controller.GetFotoBulto(7), typeof(NotFoundResult));
        }
    }
}
