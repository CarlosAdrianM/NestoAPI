using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Productos
{
    /// <summary>NestoAPI#581: la tabla ProductosSustituciones y los nombres de los productos, en memoria.</summary>
    public class RepositorioSustitucionesFalso : IRepositorioSustitucionesProducto
    {
        public Dictionary<string, string> Productos { get; } = new Dictionary<string, string>
        {
            { "25539", "PANTALON PRESOTERAPIA LOTE RT" },
            { "45685", "PANTALON PRESOTERAPIA COD040308" },
            { "17404", "ROLLO CAMILLA" }
        };
        public List<SustitucionProductoFila> Filas { get; } = new List<SustitucionProductoFila>();

        public SustitucionProductoFila Fila(string producto, string sustituto, bool mientrasNoHayaStock = true, DateTime? hasta = null,
            string motivo = null, DateTime? anulada = null)
        {
            var fila = new SustitucionProductoFila
            {
                Id = Filas.Count + 1,
                Empresa = "1",
                Producto = producto,
                ProductoSustituto = sustituto,
                MientrasNoHayaStock = mientrasNoHayaStock,
                FechaHasta = hasta,
                Motivo = motivo,
                Usuario = "NUEVAVISION\\Santi",
                FechaCreacion = new DateTime(2026, 9, 30, 10, 0, 0).AddMinutes(Filas.Count),
                FechaAnulacion = anulada,
                NombreProducto = Productos.TryGetValue(producto, out string n) ? n : null,
                NombreSustituto = Productos.TryGetValue(sustituto, out string ns) ? ns : null
            };
            Filas.Add(fila);
            return fila;
        }

        public Task<SustitucionProductoFila> LeerActiva(string empresa, string producto)
            => Task.FromResult(Filas.FirstOrDefault(f => f.Producto == producto && f.FechaAnulacion == null));

        public Task<List<SustitucionProductoFila>> Listar(string empresa, string producto)
            => Task.FromResult(Filas.Where(f => f.Producto == producto).OrderByDescending(f => f.FechaCreacion).ToList());

        public Task<string> NombreProducto(string empresa, string producto)
            => Task.FromResult(Productos.TryGetValue(producto, out string nombre) ? nombre : null);

        public Task<int> Crear(SustitucionProductoFila fila)
        {
            foreach (SustitucionProductoFila activa in Filas.Where(f => f.Producto == fila.Producto && f.FechaAnulacion == null))
            {
                activa.FechaAnulacion = new DateTime(2026, 10, 9);
                activa.UsuarioAnulacion = fila.Usuario;
            }
            fila.Id = Filas.Count + 1;
            Filas.Add(fila);
            return Task.FromResult(fila.Id);
        }

        public Task<bool> Anular(string empresa, string producto, int id, string usuario)
        {
            SustitucionProductoFila fila = Filas.FirstOrDefault(f => f.Id == id && f.Producto == producto && f.FechaAnulacion == null);
            if (fila == null)
            {
                return Task.FromResult(false);
            }
            fila.FechaAnulacion = new DateTime(2026, 10, 9);
            fila.UsuarioAnulacion = usuario;
            return Task.FromResult(true);
        }
    }

    [TestClass]
    public class ServicioSustitucionesProductoTests
    {
        private static readonly DateTime Hoy = new DateTime(2026, 10, 9);

        private RepositorioSustitucionesFalso repositorio;
        private Dictionary<string, int> disponibles;
        private List<string> consultasStock;
        private ServicioSustitucionesProducto servicio;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = new RepositorioSustitucionesFalso();
            disponibles = new Dictionary<string, int>();
            consultasStock = new List<string>();
            servicio = new ServicioSustitucionesProducto(repositorio, p =>
            {
                consultasStock.Add(p);
                return disponibles.TryGetValue(p, out int d) ? d : 0;
            }, () => Hoy);
        }

        // --- Vigente: cuándo avisa ---

        [TestMethod]
        public async Task Vigente_SinSustitucion_EsNull()
        {
            Assert.IsNull(await servicio.Vigente("1", "25539", 100));
        }

        [TestMethod]
        public async Task Vigente_MientrasNoHayaStock_SinStock_AvisaConElTextoDeCompras()
        {
            repositorio.Fila("25539", "45685", motivo: "El proveedor tarda en servir el lote RT");

            SustitucionProductoDTO vigente = await servicio.Vigente("1", "25539", 100);

            Assert.IsNotNull(vigente);
            Assert.IsTrue(vigente.Vigente);
            Assert.AreEqual(ServicioSustitucionesProducto.ESTADO_VIGENTE, vigente.Estado);
            Assert.AreEqual("45685", vigente.ProductoSustituto);
            Assert.AreEqual("PANTALON PRESOTERAPIA COD040308", vigente.NombreSustituto);
            Assert.AreEqual("Compras pide servir la 45685 (PANTALON PRESOTERAPIA COD040308) en lugar de la 25539 mientras no haya stock. " +
                "Motivo: El proveedor tarda en servir el lote RT.", vigente.Aviso);
        }

        [TestMethod]
        public async Task Vigente_MientrasNoHayaStock_AvisaSiElDisponibleNoLlegaALoQueSePide()
        {
            repositorio.Fila("25539", "45685");
            disponibles["25539"] = 5;

            Assert.IsNotNull(await servicio.Vigente("1", "25539", 100), "5 disponibles y 100 pedidas: hay que avisar");
            Assert.IsNull(await servicio.Vigente("1", "25539", 5), "Con 5 disponibles, 5 se sirven");
            Assert.IsNull(await servicio.Vigente("1", "25539", 0), "Sin cantidad cuenta como 1");
        }

        [TestMethod]
        public async Task Vigente_HastaUnaFecha_AvisaAunqueHayaStock_YElUltimoDiaIncluido()
        {
            repositorio.Fila("25539", "45685", mientrasNoHayaStock: false, hasta: Hoy);
            disponibles["25539"] = 1000;

            SustitucionProductoDTO vigente = await servicio.Vigente("1", "25539", 1);

            Assert.IsNotNull(vigente);
            StringAssert.Contains(vigente.Aviso, "en lugar de la 25539 hasta el 09/10/2026.");
            Assert.AreEqual(0, consultasStock.Count, "Si no es por stock, no se mira el stock");
        }

        [TestMethod]
        public async Task Vigente_FechaPasada_NoAvisa_NiMiraElStock()
        {
            repositorio.Fila("25539", "45685", mientrasNoHayaStock: true, hasta: Hoy.AddDays(-1));

            Assert.IsNull(await servicio.Vigente("1", "25539", 100));
            Assert.AreEqual(0, consultasStock.Count);
        }

        [TestMethod]
        public async Task Vigente_LasDos_LoQueOcurraAntes()
        {
            repositorio.Fila("25539", "45685", mientrasNoHayaStock: true, hasta: Hoy.AddDays(20));

            SustitucionProductoDTO sinStock = await servicio.Vigente("1", "25539", 10);
            disponibles["25539"] = 50;
            SustitucionProductoDTO conStock = await servicio.Vigente("1", "25539", 10);

            Assert.IsNotNull(sinStock);
            StringAssert.Contains(sinStock.Aviso, "mientras no haya stock, como mucho hasta el 29/10/2026.");
            Assert.IsNull(conStock, "Ha llegado la mercancía antes de la fecha: deja de avisar");
        }

        [TestMethod]
        public async Task Vigente_Anulada_NoAvisa()
        {
            repositorio.Fila("25539", "45685", anulada: Hoy);

            Assert.IsNull(await servicio.Vigente("1", "25539", 100));
        }

        [TestMethod]
        public async Task Vigente_ProductoConEspacios_SeRecorta()
        {
            repositorio.Fila("25539", "45685");

            Assert.IsNotNull(await servicio.Vigente("1", "25539   ", 1));
            Assert.AreEqual("25539", consultasStock.Single());
        }

        // --- Listar ---

        [TestMethod]
        public async Task Listar_LaActivaPrimero_YCadaUnaConSuEstado()
        {
            repositorio.Fila("25539", "17404", anulada: new DateTime(2026, 9, 30));
            repositorio.Fila("25539", "45685");
            disponibles["25539"] = 20;

            List<SustitucionProductoDTO> lista = await servicio.Listar("1", "25539");

            Assert.AreEqual(2, lista.Count);
            Assert.AreEqual("45685", lista[0].ProductoSustituto);
            Assert.AreEqual(ServicioSustitucionesProducto.ESTADO_HAY_STOCK, lista[0].Estado);
            Assert.IsFalse(lista[0].Vigente);
            Assert.AreEqual(ServicioSustitucionesProducto.ESTADO_ANULADA, lista[1].Estado);
            Assert.AreEqual(1, consultasStock.Count, "El stock, una sola vez");
        }

        [TestMethod]
        public async Task Listar_Caducada()
        {
            repositorio.Fila("25539", "45685", mientrasNoHayaStock: false, hasta: Hoy.AddDays(-3));

            List<SustitucionProductoDTO> lista = await servicio.Listar("1", "25539");

            Assert.AreEqual(ServicioSustitucionesProducto.ESTADO_CADUCADA, lista.Single().Estado);
            Assert.AreEqual(0, consultasStock.Count);
        }

        // --- Crear ---

        [TestMethod]
        public async Task Crear_PorDefectoMientrasNoHayaStock_YAnulaLaAnterior()
        {
            repositorio.Fila("25539", "17404");

            ResultadoSustitucion resultado = await servicio.Crear("25539", new NuevaSustitucionProductoDTO
            {
                Empresa = "1",
                ProductoSustituto = " 45685 ",
                Motivo = "  El proveedor tarda  "
            }, "NUEVAVISION\\Santi");

            Assert.AreEqual(EstadoOperacionSustitucion.Ok, resultado.Estado);
            Assert.IsTrue(resultado.Sustitucion.MientrasNoHayaStock);
            Assert.AreEqual("45685", resultado.Sustitucion.ProductoSustituto);
            Assert.AreEqual("El proveedor tarda", resultado.Sustitucion.Motivo);
            Assert.AreEqual("NUEVAVISION\\Santi", resultado.Sustitucion.Usuario);
            Assert.AreEqual(1, repositorio.Filas.Count(f => f.FechaAnulacion == null), "Una sola activa por producto");
            Assert.AreEqual("17404", repositorio.Filas.Single(f => f.FechaAnulacion != null).ProductoSustituto);
        }

        [TestMethod]
        public async Task Crear_SinStockNiFecha_NoVale()
        {
            ResultadoSustitucion resultado = await servicio.Crear("25539", new NuevaSustitucionProductoDTO
            {
                ProductoSustituto = "45685",
                MientrasNoHayaStock = false
            }, "Santi");

            Assert.AreEqual(EstadoOperacionSustitucion.Invalido, resultado.Estado);
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task Crear_FechaPasada_NoVale_PeroHoySi()
        {
            ResultadoSustitucion ayer = await servicio.Crear("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685", FechaHasta = Hoy.AddDays(-1) }, "Santi");
            ResultadoSustitucion hoy = await servicio.Crear("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685", FechaHasta = Hoy }, "Santi");

            Assert.AreEqual(EstadoOperacionSustitucion.Invalido, ayer.Estado);
            Assert.AreEqual(EstadoOperacionSustitucion.Ok, hoy.Estado);
        }

        [TestMethod]
        public async Task Crear_ProductosQueNoExisten_OElMismo()
        {
            ResultadoSustitucion productoNoExiste = await servicio.Crear("99999", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685" }, "Santi");
            ResultadoSustitucion sustitutoNoExiste = await servicio.Crear("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "99999" }, "Santi");
            ResultadoSustitucion elMismo = await servicio.Crear("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "25539" }, "Santi");

            Assert.AreEqual(EstadoOperacionSustitucion.NoEncontrado, productoNoExiste.Estado);
            Assert.AreEqual(EstadoOperacionSustitucion.Invalido, sustitutoNoExiste.Estado);
            Assert.AreEqual(EstadoOperacionSustitucion.Invalido, elMismo.Estado);
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task Crear_ElSustitutoYaSeSustituyePorEste_NoVale()
        {
            repositorio.Fila("45685", "25539");

            ResultadoSustitucion resultado = await servicio.Crear("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685" }, "Santi");

            Assert.AreEqual(EstadoOperacionSustitucion.Invalido, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "se avisarían el uno al otro");
        }

        [TestMethod]
        public async Task Crear_MotivoDemasiadoLargo_NoVale()
        {
            ResultadoSustitucion resultado = await servicio.Crear("25539", new NuevaSustitucionProductoDTO
            {
                ProductoSustituto = "45685",
                Motivo = new string('x', ServicioSustitucionesProducto.LONGITUD_MOTIVO + 1)
            }, "Santi");

            Assert.AreEqual(EstadoOperacionSustitucion.Invalido, resultado.Estado);
        }

        // --- Anular ---

        [TestMethod]
        public async Task Anular_LaActiva_YDosVecesNo()
        {
            SustitucionProductoFila fila = repositorio.Fila("25539", "45685");

            Assert.IsTrue(await servicio.Anular("1", "25539", fila.Id, "NUEVAVISION\\Laura"));
            Assert.IsFalse(await servicio.Anular("1", "25539", fila.Id, "NUEVAVISION\\Laura"));
            Assert.AreEqual("NUEVAVISION\\Laura", fila.UsuarioAnulacion);
            Assert.IsNull(await servicio.Vigente("1", "25539", 100));
        }

        // --- Texto ---

        [TestMethod]
        public void TextoAviso_SoloFecha_SinMotivo_YSinNombre()
        {
            string texto = ServicioSustitucionesProducto.TextoAviso(new SustitucionProductoFila
            {
                Producto = "25539",
                ProductoSustituto = "45685",
                MientrasNoHayaStock = false,
                FechaHasta = new DateTime(2026, 10, 31)
            });

            Assert.AreEqual("Compras pide servir la 45685 en lugar de la 25539 hasta el 31/10/2026.", texto);
        }

        // --- Controlador ---

        [TestMethod]
        public async Task Controlador_GetSinSustitucion_Devuelve200ConNull()
        {
            var controlador = new ProductosSustitucionesController(servicio);

            IHttpActionResult respuesta = await controlador.GetSustitucionVigente("25539", "1", 100);

            var ok = respuesta as OkNegotiatedContentResult<SustitucionProductoDTO>;
            Assert.IsNotNull(ok);
            Assert.IsNull(ok.Content);
        }

        [TestMethod]
        public async Task Controlador_GetConSustitucion_LaDevuelve()
        {
            repositorio.Fila("25539", "45685");
            var controlador = new ProductosSustitucionesController(servicio);

            var ok = await controlador.GetSustitucionVigente("25539", "1", 100) as OkNegotiatedContentResult<SustitucionProductoDTO>;

            Assert.AreEqual("45685", ok.Content.ProductoSustituto);
        }

        [TestMethod]
        public async Task Controlador_Post_201_404_Y_400()
        {
            var controlador = new ProductosSustitucionesController(servicio);

            var creada = await controlador.PostSustitucion("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685" })
                as NegotiatedContentResult<SustitucionProductoDTO>;
            IHttpActionResult noExiste = await controlador.PostSustitucion("99999", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685" });
            IHttpActionResult invalida = await controlador.PostSustitucion("25539", new NuevaSustitucionProductoDTO { ProductoSustituto = "45685", MientrasNoHayaStock = false });

            Assert.AreEqual(HttpStatusCode.Created, creada.StatusCode);
            Assert.AreEqual("45685", creada.Content.ProductoSustituto);
            Assert.IsNotNull(noExiste);
            Assert.AreEqual("NegotiatedContentResult`1", noExiste.GetType().Name);
            Assert.IsInstanceOfType(invalida, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Controlador_Delete_204_Y_404()
        {
            SustitucionProductoFila fila = repositorio.Fila("25539", "45685");
            var controlador = new ProductosSustitucionesController(servicio);

            var anulada = await controlador.DeleteSustitucion("25539", fila.Id) as StatusCodeResult;
            IHttpActionResult otraVez = await controlador.DeleteSustitucion("25539", fila.Id);

            Assert.AreEqual(HttpStatusCode.NoContent, anulada.StatusCode);
            Assert.IsNotInstanceOfType(otraVez, typeof(StatusCodeResult));
        }
    }
}
