using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Productos
{
    /// <summary>NestoAPI#605: la tabla ProductosCodigosBarras y la ficha, en memoria.</summary>
    public class RepositorioCodigosBarrasFalso : IRepositorioCodigosBarras
    {
        public List<Producto> Productos { get; } = new List<Producto>();
        public List<ProductoCodigoBarras> Filas { get; } = new List<ProductoCodigoBarras>();
        public int Guardados { get; private set; }
        /// <summary>Qué había en la tabla en cada guardado: el principal de la tabla ANTES de tocar la ficha.</summary>
        public List<string> PrincipalDeLaTablaAlGuardar { get; } = new List<string>();
        public List<string> FichaAlGuardar { get; } = new List<string>();

        public Producto Producto(string numero, string codBarras, string nombre, short estado = 0)
        {
            var p = new Producto { Empresa = "1", Número = numero, CodBarras = codBarras, Nombre = nombre, Estado = estado };
            Productos.Add(p);
            if (!string.IsNullOrWhiteSpace(codBarras))
            {
                Fila(numero, codBarras, principal: true);
            }
            return p;
        }

        public ProductoCodigoBarras Fila(string producto, string codigo, bool principal = false, bool activo = true, int cantidad = 1)
        {
            var fila = new ProductoCodigoBarras
            {
                Id = Filas.Count + 1,
                Empresa = "1",
                Producto = producto,
                Codigo = codigo,
                Cantidad = cantidad,
                Principal = principal,
                Origen = "Ficha",
                Usuario = "NestoAPI#605",
                Fecha = new DateTime(2026, 10, 7),
                Activo = activo
            };
            Filas.Add(fila);
            return fila;
        }

        public Task<List<ProductoCodigoBarras>> LeerDelProducto(string empresa, string producto)
            => Task.FromResult(Filas.Where(f => f.Producto == producto).ToList());

        public Task<Producto> LeerProducto(string empresa, string producto)
            => Task.FromResult(Productos.SingleOrDefault(p => p.Número == producto));

        public Task<List<ProductoPorCodigoBarrasDTO>> ProductosConCodigo(string empresa, string codigo)
            => Task.FromResult(Filas
                .Where(f => f.Activo && f.Codigo == codigo)
                .Join(Productos.Where(p => p.Estado >= 0), f => f.Producto, p => p.Número,
                    (f, p) => new ProductoPorCodigoBarrasDTO { Producto = p.Número, Nombre = p.Nombre, Cantidad = f.Cantidad, Principal = f.Principal })
                .ToList());

        public Task<Dictionary<string, List<string>>> CodigosActivos(string empresa, IReadOnlyCollection<string> productos)
            => Task.FromResult(Filas.Where(f => f.Activo && productos.Contains(f.Producto))
                .GroupBy(f => f.Producto)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Principal).Select(f => f.Codigo).ToList(), StringComparer.OrdinalIgnoreCase));

        public void Anadir(ProductoCodigoBarras fila)
        {
            fila.Id = Filas.Count + 1;
            Filas.Add(fila);
        }

        public Task GuardarCambios()
        {
            Guardados++;
            PrincipalDeLaTablaAlGuardar.Add(string.Join(",", Filas.Where(f => f.Principal).Select(f => f.Producto + ":" + f.Codigo)));
            FichaAlGuardar.Add(string.Join(",", Productos.Select(p => p.Número + ":" + p.CodBarras)));
            return Task.CompletedTask;
        }
    }

    [TestClass]
    public class ServicioCodigosBarrasTests
    {
        private RepositorioCodigosBarrasFalso repositorio;
        private ServicioCodigosBarras servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new RepositorioCodigosBarrasFalso();
            servicio = new ServicioCodigosBarras(repositorio);
            repositorio.Producto("32564", "8437017506362", "GUANTES NITRILO T/P");
            repositorio.Producto("32565", "8437017506379", "GUANTES NITRILO T/M");
        }

        private Task<ResultadoCodigoBarras> Anadir(string producto, string codigo, bool permitirCompartido = false, bool principal = false,
            string origen = "Almacen", int? cantidad = null)
            => servicio.Anadir(producto, new NuevoCodigoBarrasDTO
            {
                Empresa = "1",
                Codigo = codigo,
                Origen = origen,
                PermitirCompartido = permitirCompartido,
                Principal = principal,
                Cantidad = cantidad
            }, "Alfredo");

        [TestMethod]
        public async Task Anadir_CodigoNuevo_LoCreaLimpioYNoTocaElPrincipal()
        {
            ResultadoCodigoBarras resultado = await Anadir("32565", " 8437017 5099 99 ", cantidad: 100);

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
            Assert.AreEqual("8437017509999", resultado.Codigo.Codigo);
            Assert.AreEqual(100, resultado.Codigo.Cantidad);
            Assert.AreEqual("Almacen", resultado.Codigo.Origen);
            Assert.AreEqual("Alfredo", resultado.Codigo.Usuario);
            Assert.IsFalse(resultado.Codigo.Principal);
            Assert.IsTrue(resultado.Codigo.Activo);
            Assert.AreEqual("8437017506379", repositorio.Productos.Single(p => p.Número == "32565").CodBarras);
            Assert.AreEqual(1, repositorio.Guardados);
        }

        [TestMethod]
        public async Task Anadir_CodigoActivoEnOtroProducto_SinPermitirCompartido_Conflicto()
        {
            ResultadoCodigoBarras resultado = await Anadir("32565", "8437017506362");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Compartido, resultado.Estado);
            Assert.AreEqual(1, resultado.Compartido.Productos.Count);
            Assert.AreEqual("32564", resultado.Compartido.Productos[0].Producto);
            Assert.AreEqual("GUANTES NITRILO T/P", resultado.Compartido.Productos[0].Nombre);
            StringAssert.Contains(resultado.Compartido.Message, "32564");
            Assert.IsFalse(repositorio.Filas.Any(f => f.Producto == "32565" && f.Codigo == "8437017506362"));
            Assert.AreEqual(0, repositorio.Guardados);
        }

        [TestMethod]
        public async Task Anadir_CodigoActivoEnOtroProducto_ConPermitirCompartido_LoAnadeIgual()
        {
            ResultadoCodigoBarras resultado = await Anadir("32565", "8437017506362", permitirCompartido: true);

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
            Assert.AreEqual(2, repositorio.Filas.Count(f => f.Codigo == "8437017506362" && f.Activo));
        }

        [TestMethod]
        public async Task Anadir_CodigoDeBajaEnOtroProducto_NoEsConflicto()
        {
            repositorio.Fila("32564", "8437017500000", activo: false);

            ResultadoCodigoBarras resultado = await Anadir("32565", "8437017500000");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
        }

        [TestMethod]
        public async Task Anadir_CodigoQueYaTieneElProductoDeBaja_LoReactivaY200()
        {
            ProductoCodigoBarras deBaja = repositorio.Fila("32565", "8437017501111", activo: false);
            int filas = repositorio.Filas.Count;

            ResultadoCodigoBarras resultado = await Anadir("32565", "8437017501111");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, resultado.Estado);
            Assert.AreEqual(deBaja.Id, resultado.Codigo.Id);
            Assert.IsTrue(deBaja.Activo);
            Assert.AreEqual(filas, repositorio.Filas.Count, "No se crea otra fila");
            Assert.AreEqual(1, repositorio.Guardados);
        }

        [TestMethod]
        public async Task Anadir_CodigoQueYaTieneActivo_200SinGuardar()
        {
            ResultadoCodigoBarras resultado = await Anadir("32565", "8437017506379");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, resultado.Estado);
            Assert.IsTrue(resultado.Codigo.Principal);
            Assert.AreEqual(0, repositorio.Guardados);
        }

        [TestMethod]
        public async Task Anadir_PrimerCodigoDeUnProductoSinCodigo_EsElPrincipalYVaALaFicha()
        {
            Producto sinCodigo = repositorio.Producto("40510", null, "PRODUCTO SIN CÓDIGO");

            ResultadoCodigoBarras resultado = await Anadir("40510", "8400000040510");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Creado, resultado.Estado);
            Assert.IsTrue(resultado.Codigo.Principal);
            Assert.AreEqual("8400000040510", sinCodigo.CodBarras);
            Assert.AreEqual("Alfredo", sinCodigo.Usuario);
        }

        [TestMethod]
        public async Task Anadir_ProductoQueNoExiste_NoEncontrado()
        {
            ResultadoCodigoBarras resultado = await Anadir("99999", "8400000000009");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoEncontrado, resultado.Estado);
        }

        [TestMethod]
        public async Task Anadir_SinCodigoOConOrigenRaro_NoValido()
        {
            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, (await Anadir("32565", "  ")).Estado);
            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, (await Anadir("32565", "123", origen: "Marte")).Estado);
            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, (await Anadir("32565", "123456789012345678901")).Estado);
            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, (await Anadir("32565", "12345678901234", principal: true)).Estado,
                "Un principal de más de 13 no cabe en la ficha");
        }

        [TestMethod]
        public async Task MarcarPrincipal_ActualizaLaFichaYElAnteriorSigueActivo()
        {
            ProductoCodigoBarras alternativo = repositorio.Fila("32565", "8437017502222");
            ProductoCodigoBarras anterior = repositorio.Filas.Single(f => f.Producto == "32565" && f.Principal);

            ResultadoCodigoBarras resultado = await servicio.MarcarPrincipal("1", "32565", alternativo.Id, "Compras1");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, resultado.Estado);
            Assert.IsTrue(alternativo.Principal);
            Assert.IsFalse(anterior.Principal);
            Assert.IsTrue(anterior.Activo, "El principal anterior se queda como alternativo");
            Producto ficha = repositorio.Productos.Single(p => p.Número == "32565");
            Assert.AreEqual("8437017502222", ficha.CodBarras);
            Assert.AreEqual("Compras1", ficha.Usuario);
            // Primero la tabla y luego la ficha: así el trigger de Productos no da de baja el principal anterior
            Assert.AreEqual(2, repositorio.Guardados);
            StringAssert.Contains(repositorio.PrincipalDeLaTablaAlGuardar[0], "32565:8437017502222");
            StringAssert.Contains(repositorio.FichaAlGuardar[0], "32565:8437017506379");
            StringAssert.Contains(repositorio.FichaAlGuardar[1], "32565:8437017502222");
        }

        [TestMethod]
        public async Task MarcarPrincipal_DeOtroProducto_NoEncontrado()
        {
            ProductoCodigoBarras deOtro = repositorio.Filas.Single(f => f.Producto == "32564");

            ResultadoCodigoBarras resultado = await servicio.MarcarPrincipal("1", "32565", deOtro.Id, "Compras1");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoEncontrado, resultado.Estado);
        }

        [TestMethod]
        public async Task DarDeBaja_ElPrincipal_NoValido()
        {
            ProductoCodigoBarras principal = repositorio.Filas.Single(f => f.Producto == "32565" && f.Principal);

            ResultadoCodigoBarras resultado = await servicio.DarDeBaja("1", "32565", principal.Id, "Compras1");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, resultado.Estado);
            Assert.IsTrue(principal.Activo);
            Assert.AreEqual(0, repositorio.Guardados);
        }

        [TestMethod]
        public async Task DarDeBaja_UnAlternativo_QuedaInactivoSinBorrarse()
        {
            ProductoCodigoBarras alternativo = repositorio.Fila("32565", "8437017503333");

            ResultadoCodigoBarras resultado = await servicio.DarDeBaja("1", "32565", alternativo.Id, "Compras1");

            Assert.AreEqual(EstadoOperacionCodigoBarras.Ok, resultado.Estado);
            Assert.IsFalse(alternativo.Activo);
            Assert.IsTrue(repositorio.Filas.Contains(alternativo));
        }

        [TestMethod]
        public async Task Listar_ActivosPrimeroYElPrincipalElPrimero()
        {
            repositorio.Fila("32565", "8437017504444", activo: false);
            repositorio.Fila("32565", "8437017505555");

            List<CodigoBarrasProductoDTO> codigos = await servicio.Listar("1", "32565");

            CollectionAssert.AreEqual(new[] { "8437017506379", "8437017505555", "8437017504444" }, codigos.Select(c => c.Codigo).ToList());
        }

        [TestMethod]
        public async Task BuscarPorCodigo_TodosLosProductosActivosConElCodigoYElPrincipalElPrimero()
        {
            repositorio.Fila("32565", "8437017506362", cantidad: 1);
            repositorio.Producto("11111", null, "ANULADO", estado: -1);
            repositorio.Fila("11111", "8437017506362");

            List<ProductoPorCodigoBarrasDTO> productos = await servicio.BuscarPorCodigo("1", " 8437017506362 ");

            CollectionAssert.AreEqual(new[] { "32564", "32565" }, productos.Select(p => p.Producto).ToList());
            Assert.IsTrue(productos[0].Principal);
            Assert.IsFalse(productos[1].Principal);
            Assert.AreEqual(0, (await servicio.BuscarPorCodigo("1", "0000")).Count);
        }

        [TestMethod]
        public async Task CompletadorCodigosBarras_PoneTodosLosCodigosConElDeLaLineaElPrimero()
        {
            repositorio.Fila("32565", "8437017506362");
            var linea = new LineaPickingAlmacenDTO { Producto = "32565", CodigoBarras = "8437017506379" };
            var sinCodigos = new LineaPickingAlmacenDTO { Producto = "77777", CodigoBarras = null };

            await CompletadorCodigosBarras.Completar(repositorio, "1", new[] { linea, sinCodigos });

            CollectionAssert.AreEqual(new[] { "8437017506379", "8437017506362" }, linea.CodigosBarras);
            Assert.AreEqual(0, sinCodigos.CodigosBarras.Count);
        }

        [TestMethod]
        public void PuedeEscribir_SoloLosGruposDeCompraAlmacenTiendasDireccionEInformatica()
        {
            Assert.IsTrue(ServicioCodigosBarras.PuedeEscribir(ConRol("NUEVAVISION\\Compras")));
            Assert.IsTrue(ServicioCodigosBarras.PuedeEscribir(ConRol("Almacén")));
            Assert.IsTrue(ServicioCodigosBarras.PuedeEscribir(ConRol("Tiendas")));
            Assert.IsFalse(ServicioCodigosBarras.PuedeEscribir(ConRol("Vendedores")));
            Assert.IsFalse(ServicioCodigosBarras.PuedeEscribir(null));
        }

        internal static ClaimsPrincipal ConRol(string rol)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Alfredo"), new Claim(ClaimTypes.Role, rol) }, "Test");
            return new ClaimsPrincipal(identidad);
        }
    }

    [TestClass]
    public class ProductosCodigosBarrasControllerTests
    {
        private RepositorioCodigosBarrasFalso repositorio;

        private ProductosCodigosBarrasController Controlador(string rol)
        {
            repositorio = new RepositorioCodigosBarrasFalso();
            repositorio.Producto("32564", "8437017506362", "GUANTES NITRILO T/P");
            repositorio.Producto("32565", "8437017506379", "GUANTES NITRILO T/M");
            var controller = new ProductosCodigosBarrasController(new ServicioCodigosBarras(repositorio))
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.RequestContext.Principal = ServicioCodigosBarrasTests.ConRol(rol);
            return controller;
        }

        [TestMethod]
        public async Task Post_CodigoNuevo_201()
        {
            IHttpActionResult resultado = await Controlador("Almacén").PostCodigoBarras("32565", new NuevoCodigoBarrasDTO { Codigo = "8437017509999", Origen = "Almacen" });

            var creado = resultado as NegotiatedContentResult<CodigoBarrasProductoDTO>;
            Assert.IsNotNull(creado);
            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            Assert.AreEqual("8437017509999", creado.Content.Codigo);
        }

        [TestMethod]
        public async Task Post_CodigoDeOtroProducto_409ConLosProductos()
        {
            IHttpActionResult resultado = await Controlador("Almacén").PostCodigoBarras("32565", new NuevoCodigoBarrasDTO { Codigo = "8437017506362" });

            var conflicto = resultado as NegotiatedContentResult<CodigoBarrasCompartidoDTO>;
            Assert.IsNotNull(conflicto);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual("32564", conflicto.Content.Productos.Single().Producto);
        }

        [TestMethod]
        public async Task Post_SinGrupoQuePuedaEscribir_403()
        {
            IHttpActionResult resultado = await Controlador("Vendedores").PostCodigoBarras("32565", new NuevoCodigoBarrasDTO { Codigo = "8437017509999" });

            var respuesta = resultado as ResponseMessageResult;
            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.Response.StatusCode);
            Assert.AreEqual(0, repositorio.Guardados);
        }

        [TestMethod]
        public async Task Delete_DelPrincipal_400()
        {
            ProductosCodigosBarrasController controller = Controlador("Compras");
            int id = repositorio.Filas.Single(f => f.Producto == "32565" && f.Principal).Id;

            IHttpActionResult resultado = await controller.DeleteCodigoBarras("32565", id);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PutPrincipal_ActualizaLaFicha()
        {
            ProductosCodigosBarrasController controller = Controlador("Compras");
            ProductoCodigoBarras alternativo = repositorio.Fila("32565", "8437017502222");

            IHttpActionResult resultado = await controller.PutPrincipal("32565", alternativo.Id);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<CodigoBarrasProductoDTO>));
            Assert.AreEqual("8437017502222", repositorio.Productos.Single(p => p.Número == "32565").CodBarras);
        }

        [TestMethod]
        public async Task GetPorCodigoBarras_DevuelveLosProductos()
        {
            ProductosCodigosBarrasController controller = Controlador("Vendedores");
            repositorio.Fila("32565", "8437017506362");

            var resultado = await controller.GetPorCodigoBarras("8437017506362") as OkNegotiatedContentResult<List<ProductoPorCodigoBarrasDTO>>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new[] { "32564", "32565" }, resultado.Content.Select(p => p.Producto).ToList());
        }
    }
}
