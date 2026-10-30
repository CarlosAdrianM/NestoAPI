using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Productos
{
    /// <summary>
    /// NestoAPI#762 (sugerencia 909, Carlos 30/10/26): el código de barras interno, EAN-13 con prefijo 29, para lo que no tiene
    /// código: 290 + la referencia a 9 cifras + control, o 299 + contador. Nunca el principal (la ficha va a PrestaShop).
    /// </summary>
    [TestClass]
    public class CodigoBarrasInternoTests
    {
        [TestMethod]
        public void DeReferencia_17404_Da2900000174047ConSuDigitoDeControl()
        {
            string codigo = CodigoBarrasInterno.DeReferencia("17404");

            Assert.AreEqual("2900000174047", codigo);
            Assert.IsTrue(ValidadorGtin.EsCodigoValido(codigo));
            Assert.IsTrue(CodigoBarrasInterno.EsInterno(codigo));
        }

        [TestMethod]
        public void DeReferencia_ConEspaciosDelChar_LaRecorta()
        {
            Assert.AreEqual(CodigoBarrasInterno.DeReferencia("17404"), CodigoBarrasInterno.DeReferencia("17404          "));
        }

        [TestMethod]
        public void DeReferencia_NueveCifras_Cabe()
        {
            Assert.AreEqual("290240809150", CodigoBarrasInterno.DeReferencia("240809150").Substring(0, 12));
        }

        [TestMethod]
        public void DeReferencia_LoQueNoEsUnNumeroDeHasta9Cifras_Null()
        {
            Assert.IsNull(CodigoBarrasInterno.DeReferencia("MF0581"));
            Assert.IsNull(CodigoBarrasInterno.DeReferencia("11-31907"));
            Assert.IsNull(CodigoBarrasInterno.DeReferencia("1234567890"));
            Assert.IsNull(CodigoBarrasInterno.DeReferencia("0"));
            Assert.IsNull(CodigoBarrasInterno.DeReferencia(null));
            Assert.IsNull(CodigoBarrasInterno.DeReferencia(" "));
        }

        [TestMethod]
        public void DeContador_299MasNueveCifrasYControl()
        {
            string codigo = CodigoBarrasInterno.DeContador(1);

            Assert.AreEqual("299000000001", codigo.Substring(0, 12));
            Assert.IsTrue(CodigoBarrasInterno.EsInterno(codigo));
        }

        [TestMethod]
        public void DeContador_FueraDeRango_Lanza()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => CodigoBarrasInterno.DeContador(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => CodigoBarrasInterno.DeContador(1000000000));
        }

        [TestMethod]
        public void SiguienteDelContador_ElMayorMasUno_SinContarLosDeLaReferenciaNiLosDeFuera()
        {
            var existentes = new[]
            {
                CodigoBarrasInterno.DeContador(7),
                CodigoBarrasInterno.DeContador(3),
                CodigoBarrasInterno.DeReferencia("999999999"),
                "2999999999990", // control mal: no es nuestro
                "8437017506362"
            };

            Assert.AreEqual(CodigoBarrasInterno.DeContador(8), CodigoBarrasInterno.SiguienteDelContador(existentes));
        }

        [TestMethod]
        public void SiguienteDelContador_SinNinguno_ElPrimero()
        {
            Assert.AreEqual(CodigoBarrasInterno.DeContador(1), CodigoBarrasInterno.SiguienteDelContador(null));
        }

        [TestMethod]
        public void EsInterno_LosDel2QueYaHayEnLaBD_NoSonNuestros()
        {
            // 30/10/26: en la BD hay 99 códigos que empiezan por 2 (20x a 27x); ninguno por 29
            Assert.IsFalse(CodigoBarrasInterno.EsInterno("2200200000017"));
            Assert.IsFalse(CodigoBarrasInterno.EsInterno("2512345678900"));
            Assert.IsFalse(CodigoBarrasInterno.EsInterno("8437017506362"));
            Assert.IsFalse(CodigoBarrasInterno.EsInterno("290000017404")); // 12 cifras
            Assert.IsFalse(CodigoBarrasInterno.EsInterno(null));
        }
    }

    [TestClass]
    public class AsignarCodigoInternoTests
    {
        private RepositorioCodigosBarrasFalso repositorio;
        private ServicioCodigosBarras servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new RepositorioCodigosBarrasFalso();
            servicio = new ServicioCodigosBarras(repositorio);
            repositorio.Producto("17404", null, "ROLLO PAPEL CAMILLA");
            repositorio.Producto("32565", "8437017506379", "GUANTES NITRILO T/M");
            repositorio.Producto("MF0581", null, "LIMA CERÁMICA");
        }

        [TestMethod]
        public async Task AsignarInterno_SinCodigo_LeDaElDeSuReferenciaSinSerPrincipalNiTocarLaFicha()
        {
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "17404", @"NUEVAVISION\Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
            Assert.AreEqual("2900000174047", resultado.Codigo.Codigo);
            Assert.AreEqual(1, resultado.Codigo.Cantidad);
            Assert.AreEqual(ServicioCodigosBarras.ORIGEN_ALMACEN, resultado.Codigo.Origen);
            Assert.IsFalse(resultado.Codigo.Principal);
            Assert.IsNull(repositorio.Productos.Single(p => p.Número == "17404").CodBarras);
        }

        [TestMethod]
        public async Task AsignarInterno_DosVeces_ElMismoYSinCrearOtro()
        {
            ResultadoCodigoBarras primera = await servicio.AsignarInterno("1", "17404", "Andre");
            ResultadoCodigoBarras segunda = await servicio.AsignarInterno("1", "17404", "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, segunda.Estado);
            Assert.AreEqual(primera.Codigo.Codigo, segunda.Codigo.Codigo);
            Assert.AreEqual(1, repositorio.Filas.Count(f => f.Producto == "17404"));
            Assert.AreEqual(1, repositorio.InternosPedidos);
        }

        [TestMethod]
        public async Task AsignarInterno_ConCodigoDelFabricante_LeDaElInternoIgual()
        {
            // Se pide expresamente (POST …/Interno); la etiqueta EAN-13 ya elige el del fabricante antes de llegar aquí
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "32565", "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
            Assert.AreEqual(CodigoBarrasInterno.DeReferencia("32565"), resultado.Codigo.Codigo);
            Assert.AreEqual("8437017506379", repositorio.Productos.Single(p => p.Número == "32565").CodBarras);
        }

        [TestMethod]
        public async Task AsignarInterno_ReferenciaQueNoEsNumero_ElDelContador()
        {
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "MF0581", "Andre");

            Assert.AreEqual(CodigoBarrasInterno.DeContador(1), resultado.Codigo.Codigo);
        }

        [TestMethod]
        public async Task AsignarInterno_ElDeLaReferenciaYaEsDeOtro_ElDelContador()
        {
            repositorio.Fila("32565", "2900000174047");

            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "17404", "Andre");

            Assert.AreEqual(CodigoBarrasInterno.DeContador(1), resultado.Codigo.Codigo);
        }

        [TestMethod]
        public async Task AsignarInterno_LoTeniaDeBaja_LoReactiva()
        {
            repositorio.Fila("17404", "2900000174047", activo: false);

            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "17404", "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, resultado.Estado);
            Assert.AreEqual("2900000174047", resultado.Codigo.Codigo);
            Assert.IsTrue(resultado.Codigo.Activo);
            Assert.AreEqual(1, repositorio.Filas.Count(f => f.Producto == "17404"));
        }

        [TestMethod]
        public async Task AsignarInterno_Ensayo_DiceCualPeroNoGuarda()
        {
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "17404", "Andre", ensayo: true);

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, resultado.Estado);
            Assert.AreEqual("2900000174047", resultado.Codigo.Codigo);
            Assert.AreEqual(0, repositorio.InternosPedidos);
            Assert.IsFalse(repositorio.Filas.Any(f => f.Producto == "17404"));
        }

        [TestMethod]
        public async Task AsignarInterno_Ensayo_DelContador_SinCodigo()
        {
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "MF0581", "Andre", ensayo: true);

            Assert.IsNull(resultado.Codigo);
            StringAssert.Contains(resultado.Mensaje, "contador");
        }

        [TestMethod]
        public async Task AsignarInterno_ProductoQueNoExiste_NoEncontrado()
        {
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno("1", "99999", "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoEncontrado, resultado.Estado);
            Assert.AreEqual(0, repositorio.InternosPedidos);
        }

        [TestMethod]
        public async Task Anadir_UnInternoAMano_SinPrincipal_NoPasaAPrincipalAunqueLaFichaEsteVacia()
        {
            ResultadoCodigoBarras resultado = await servicio.Anadir("17404", new NuevoCodigoBarrasDTO { Empresa = "1", Codigo = "2900000174047" }, "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
            Assert.IsFalse(resultado.Codigo.Principal);
            Assert.IsNull(repositorio.Productos.Single(p => p.Número == "17404").CodBarras);
        }

        [TestMethod]
        public async Task Anadir_UnInternoComoPrincipal_NoValido()
        {
            ResultadoCodigoBarras resultado = await servicio.Anadir("17404", new NuevoCodigoBarrasDTO { Empresa = "1", Codigo = "2900000174047", Principal = true }, "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, resultado.Estado);
            Assert.AreEqual(ServicioCodigosBarras.MENSAJE_INTERNO_NO_PRINCIPAL, resultado.Mensaje);
        }

        [TestMethod]
        public async Task MarcarPrincipal_UnInterno_NoValidoYLaFichaNoCambia()
        {
            NestoAPI.Models.ProductoCodigoBarras interno = repositorio.Fila("32565", CodigoBarrasInterno.DeReferencia("32565"));

            ResultadoCodigoBarras resultado = await servicio.MarcarPrincipal("1", "32565", interno.Id, "Andre");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, resultado.Estado);
            Assert.AreEqual("8437017506379", repositorio.Productos.Single(p => p.Número == "32565").CodBarras);
        }

        [TestMethod]
        public async Task BuscarPorCodigo_ElInterno_DaSuProducto()
        {
            await servicio.AsignarInterno("1", "17404", "Andre");

            List<ProductoPorCodigoBarrasDTO> productos = await servicio.BuscarPorCodigo("1", "2900000174047");

            Assert.AreEqual("17404", productos.Single().Producto);
        }
    }

    [TestClass]
    public class ProductosCodigosBarrasControllerInternoTests
    {
        private RepositorioCodigosBarrasFalso repositorio;

        private ProductosCodigosBarrasController Controlador(string rol, IRepositorioRepuestosSinCodigo repuestos = null)
        {
            repositorio = new RepositorioCodigosBarrasFalso();
            InformeRepuestosSinCodigo informe = repuestos == null ? null : new InformeRepuestosSinCodigo(repuestos, repositorio);
            repositorio.Producto("17404", null, "ROLLO PAPEL CAMILLA");
            repositorio.Producto("32565", "8437017506379", "GUANTES NITRILO T/M");
            var controller = new ProductosCodigosBarrasController(new ServicioCodigosBarras(repositorio), informe)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                Hoy = () => new DateTime(2026, 10, 30)
            };
            controller.RequestContext.Principal = ServicioCodigosBarrasTests.ConRol(rol);
            return controller;
        }

        [TestMethod]
        public async Task PostInterno_LaPrimeraVez201YLaSegunda200ConElMismo()
        {
            ProductosCodigosBarrasController controller = Controlador("Almacén");

            var creado = await controller.PostInterno("17404") as NegotiatedContentResult<CodigoBarrasProductoDTO>;
            var otraVez = await controller.PostInterno("17404") as OkNegotiatedContentResult<CodigoBarrasProductoDTO>;

            Assert.IsNotNull(creado);
            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            Assert.AreEqual("2900000174047", creado.Content.Codigo);
            Assert.IsNotNull(otraVez);
            Assert.AreEqual("2900000174047", otraVez.Content.Codigo);
        }

        [TestMethod]
        public async Task PostInterno_SinPermiso_403()
        {
            var resultado = await Controlador("Vendedores").PostInterno("17404") as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            Assert.IsFalse(repositorio.Filas.Any(f => f.Producto == "17404"));
        }

        [TestMethod]
        public async Task PostInterno_ProductoQueNoExiste_404()
        {
            IHttpActionResult resultado = await Controlador("Almacén").PostInterno("99999");
            HttpResponseMessage respuesta = await resultado.ExecuteAsync(System.Threading.CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetRepuestosSinCodigoBarras_SoloLosQueNoTienenNingunGtinValido_LosDeMasUnidadesPrimero()
        {
            var repuestos = A.Fake<IRepositorioRepuestosSinCodigo>();
            A.CallTo(() => repuestos.LeerRepuestos("1", new DateTime(2026, 7, 30))).Returns(new List<ProductoRepuestoSinCodigoDTO>
            {
                new ProductoRepuestoSinCodigoDTO { Producto = "17404", Nombre = "ROLLO PAPEL CAMILLA", Unidades = 5 },
                new ProductoRepuestoSinCodigoDTO { Producto = "32565", Nombre = "GUANTES NITRILO T/M", Unidades = 40 },
                new ProductoRepuestoSinCodigoDTO { Producto = "40000", Nombre = "LIMA", Unidades = 12 }
            });
            ProductosCodigosBarrasController controller = Controlador("Compras", repuestos);
            repositorio.Fila("40000", "1"); // un código que no es GTIN cuenta como sin código

            var resultado = await controller.GetRepuestosSinCodigoBarras() as OkNegotiatedContentResult<List<ProductoRepuestoSinCodigoDTO>>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new[] { "40000", "17404" }, resultado.Content.Select(p => p.Producto).ToList());
        }

        [TestMethod]
        public async Task GetRepuestosSinCodigoBarras_YaConSuInterno_NoSale()
        {
            var repuestos = A.Fake<IRepositorioRepuestosSinCodigo>();
            A.CallTo(() => repuestos.LeerRepuestos(A<string>._, A<DateTime>._)).Returns(new List<ProductoRepuestoSinCodigoDTO>
            {
                new ProductoRepuestoSinCodigoDTO { Producto = "17404", Unidades = 5 }
            });
            ProductosCodigosBarrasController controller = Controlador("Compras", repuestos);
            repositorio.Fila("17404", "2900000174047");

            var resultado = await controller.GetRepuestosSinCodigoBarras() as OkNegotiatedContentResult<List<ProductoRepuestoSinCodigoDTO>>;

            Assert.AreEqual(0, resultado.Content.Count);
        }

        [TestMethod]
        public async Task GetRepuestosSinCodigoBarras_MesesFueraDeRango_400()
        {
            var repuestos = A.Fake<IRepositorioRepuestosSinCodigo>();
            ProductosCodigosBarrasController controller = Controlador("Compras", repuestos);

            IHttpActionResult resultado = await controller.GetRepuestosSinCodigoBarras(meses: 0);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetRepuestosSinCodigoBarras_SinInforme_404()
        {
            IHttpActionResult resultado = await Controlador("Compras").GetRepuestosSinCodigoBarras();

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }
    }
}
