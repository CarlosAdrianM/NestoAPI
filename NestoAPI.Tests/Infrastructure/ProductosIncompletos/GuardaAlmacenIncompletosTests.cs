using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.ProductosIncompletos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ProductosIncompletos
{
    /// <summary>
    /// NestoAPI#902 (26/11/26): el almacén de incompletos no se cuela por otros caminos (inventario, traspasos de ficticios de #894, lista
    /// de almacenes de los pedidos). Su código se lee del parámetro AlmacenIncompletos, nunca «INC» a fuego.
    /// </summary>
    [TestClass]
    public class GuardaAlmacenIncompletosTests
    {
        [TestCleanup]
        public void Limpiar()
        {
            GuardaAlmacenIncompletos.FijarLectorParaTests(null);
        }

        [TestMethod]
        public void EsElDeIncompletos_SinRellenoNiMayusculas_YSinCodigoNunca()
        {
            Assert.IsTrue(GuardaAlmacenIncompletos.EsElDeIncompletos("inc ", "INC"));
            Assert.IsTrue(GuardaAlmacenIncompletos.EsElDeIncompletos("XYZ", "xyz"), "el que diga el parámetro, no INC a fuego");
            Assert.IsFalse(GuardaAlmacenIncompletos.EsElDeIncompletos("INC", null), "sin parámetro no se frena nada");
            Assert.IsFalse(GuardaAlmacenIncompletos.EsElDeIncompletos("INC", " "));
            Assert.IsFalse(GuardaAlmacenIncompletos.EsElDeIncompletos(null, "INC"));
            Assert.IsFalse(GuardaAlmacenIncompletos.EsElDeIncompletos("REI", "INC"));
        }

        [TestMethod]
        public void Codigo_LoLeeDelParametroUnaVezYLoGuarda()
        {
            int lecturas = 0;
            GuardaAlmacenIncompletos.FijarLectorParaTests(() => { lecturas++; return " inc "; });

            Assert.AreEqual("INC", GuardaAlmacenIncompletos.Codigo());
            Assert.IsTrue(GuardaAlmacenIncompletos.Es("INC"));
            Assert.AreEqual(1, lecturas, "con caché");
        }

        [TestMethod]
        public void Codigo_SiNoSePuedeLeer_NullYNoLanza()
        {
            GuardaAlmacenIncompletos.FijarLectorParaTests(() => throw new InvalidOperationException("sin base de datos"));

            Assert.IsNull(GuardaAlmacenIncompletos.Codigo());
            Assert.IsFalse(GuardaAlmacenIncompletos.Es("INC"));
        }

        // ---------- (a) inventario ----------

        private static void ConIncompletosEn(string codigo) => GuardaAlmacenIncompletos.FijarLectorParaTests(() => codigo);

        private static void Es400DeIncompletos(IHttpActionResult resultado)
        {
            Assert.IsNotNull(resultado);
            Type tipo = resultado.GetType();
            Assert.AreEqual(HttpStatusCode.BadRequest, (HttpStatusCode)tipo.GetProperty("StatusCode").GetValue(resultado));
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(tipo.GetProperty("Content").GetValue(resultado));
            StringAssert.Contains(json, GuardaAlmacenIncompletos.MENSAJE_INVENTARIO);
        }

        private static IPrincipal Almacen() => new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, @"NUEVAVISION\Andre"),
            new Claim(ClaimTypes.Role, @"NUEVAVISION\Almacén"),
            new Claim(ClaimTypes.Role, @"NUEVAVISION\Dirección")
        }, "prueba"));

        [TestMethod]
        public async Task PostInventario_EnElAlmacenDeIncompletos_400()
        {
            ConIncompletosEn("INC");
            var controller = new InventariosController(A.Fake<NVEntities>()) { User = Almacen() };

            Es400DeIncompletos(await controller.PostInventario(new Inventario { Empresa = "1", Almacén = "INC", Número = "40252" }));
        }

        [TestMethod]
        public async Task PostRellenar_EnElAlmacenDeIncompletos_400()
        {
            ConIncompletosEn("INC");
            var controller = new InventariosController(A.Fake<NVEntities>()) { User = Almacen() };
            controller.Request = new System.Net.Http.HttpRequestMessage();

            Es400DeIncompletos(await controller.PostRellenar(new RellenarInventarioDTO { Empresa = "1", Almacen = "inc", Fecha = DateTime.Today }));
        }

        [TestMethod]
        public async Task PostContabilizar_EnElAlmacenDeIncompletos_400()
        {
            ConIncompletosEn("INC");
            var controller = new InventariosController(A.Fake<NVEntities>()) { User = Almacen() };

            Es400DeIncompletos(await controller.PostContabilizar(new ContabilizarInventarioParametrosDTO { Empresa = "1", Almacen = "INC", Fecha = DateTime.Today }));
        }

        [TestMethod]
        public async Task PostDejarACero_EnElAlmacenDeIncompletos_400()
        {
            ConIncompletosEn("INC");
            var controller = new InventariosController(A.Fake<NVEntities>()) { User = Almacen() };

            Es400DeIncompletos(await controller.PostDejarACero(new DejarACeroInventarioDTO { Empresa = "1", Almacen = "INC", Producto = "40252" }));
        }

        // ---------- (b) traspasos de almacenes ficticios ----------

        private static IRepositorioAlmacenesFicticios Repositorio()
        {
            IRepositorioAlmacenesFicticios repositorio = A.Fake<IRepositorioAlmacenesFicticios>();
            A.CallTo(() => repositorio.LeerAlmacenes(A<string>._)).Returns(Task.FromResult(new List<AlmacenLeido>
            {
                new AlmacenLeido { Codigo = "ALG", Descripcion = "Algete", ControlUbicaciones = true },
                new AlmacenLeido { Codigo = "PRV", Descripcion = "Proveedores", EsFicticio = true },
                new AlmacenLeido { Codigo = "INC", Descripcion = "Incompletos", EsFicticio = true }
            }));
            A.CallTo(() => repositorio.LeerDuenos()).Returns(Task.FromResult(new List<DuenoAlmacenFicticio>()));
            return repositorio;
        }

        [TestMethod]
        public async Task Ficticios_Listar_NoSaleElDeIncompletos()
        {
            var servicio = new ServicioAlmacenesFicticios(Repositorio(), A.Fake<IAvisadorAlmacenFicticio>(), () => "INC");

            List<AlmacenFicticioDTO> lista = await servicio.Listar(Almacen());

            CollectionAssert.AreEqual(new[] { "PRV" }, lista.Select(a => a.Codigo).ToArray());
        }

        [TestMethod]
        public async Task Ficticios_Listar_SinParametro_ComoAntes()
        {
            var servicio = new ServicioAlmacenesFicticios(Repositorio(), A.Fake<IAvisadorAlmacenFicticio>(), () => null);

            List<AlmacenFicticioDTO> lista = await servicio.Listar(Almacen());

            Assert.AreEqual(2, lista.Count);
        }

        [TestMethod]
        public async Task Ficticios_Traspasar_ElDeIncompletos_403SinTocarNada()
        {
            IRepositorioAlmacenesFicticios repositorio = Repositorio();
            var servicio = new ServicioAlmacenesFicticios(repositorio, A.Fake<IAvisadorAlmacenFicticio>(), () => "INC");
            var peticion = new TraspasoAlmacenFicticioDTO
            {
                AlmacenFicticio = "inc",
                Sentido = "Recoger",
                Lineas = new List<LineaTraspasoFicticioDTO> { new LineaTraspasoFicticioDTO { Producto = "40252", Cantidad = 1 } }
            };

            try
            {
                _ = await servicio.Traspasar(peticion, false, Almacen());
                Assert.Fail("Tenía que lanzar");
            }
            catch (NestoBusinessException ex)
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
                Assert.AreEqual(GuardaAlmacenIncompletos.MENSAJE_TRASPASO, ex.Message);
            }
            A.CallTo(() => repositorio.LeerProductos(A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        // ---------- (c) almacenes de los pedidos ----------

        [TestMethod]
        public void QuitarAlmacenIncompletos_LoQuitaSoloSiHayParametro()
        {
            var almacenes = new List<AlmacenDTO> { new AlmacenDTO { Codigo = "ALG" }, new AlmacenDTO { Codigo = "INC" }, new AlmacenDTO { Codigo = "PRV" } };

            CollectionAssert.AreEqual(new[] { "ALG", "PRV" },
                PedidosVentaController.QuitarAlmacenIncompletos(almacenes, "INC").Select(a => a.Codigo).ToArray());
            Assert.AreEqual(3, PedidosVentaController.QuitarAlmacenIncompletos(almacenes, null).Count);
        }
    }
}
