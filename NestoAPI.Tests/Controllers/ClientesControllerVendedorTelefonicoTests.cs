using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Sugerencia 582 + PermitirVerClientesTodosLosVendedores (Carlos 27/11/26): GET api/Clientes acepta
    /// vendedorTelefonico. Si viene, decide él si quien busca es telefónico (contacto en estado 9 delante) y
    /// «vendedor» queda solo como filtro (vacío = todos). Sin él, todo como antes.
    /// </summary>
    [TestClass]
    public class ClientesControllerVendedorTelefonicoTests
    {
        private const string FILTRO = "PELUQUERÍA";
        private NVEntities db;
        private IServicioVendedores servicioVendedores;
        private ClientesController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            DbSet<Cliente> clientes = Crear<Cliente>();
            DbSet<CabFacturaVta> facturas = Crear<CabFacturaVta>();
            DbSet<VendedorClienteGrupoProducto> vendedoresGrupos = Crear<VendedorClienteGrupoProducto>();
            DbSet<Vendedor> vendedores = Crear<Vendedor>();
            A.CallTo(() => db.Clientes).Returns(clientes);
            A.CallTo(() => db.CabsFacturasVtas).Returns(facturas);
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(vendedoresGrupos);
            A.CallTo(() => db.Vendedores).Returns(vendedores);
            ConfigurarFakeDbSet(clientes, new List<Cliente>
            {
                // El principal (0) va antes que el contacto telefónico (1) en el orden de llegada
                Cliente("27120", "0", "PELUQUERÍA ANA", vendedor: "PA", estado: 0),
                Cliente("27120", "1", "PELUQUERÍA ANA (TELÉFONO)", vendedor: "PA", estado: Constantes.Clientes.Estados.VISITA_TELEFONICA),
                Cliente("30676", "0", "PELUQUERÍA LUZ", vendedor: "JE", estado: 0)
            });
            ConfigurarFakeDbSet(facturas, new List<CabFacturaVta>());
            ConfigurarFakeDbSet(vendedoresGrupos, new List<VendedorClienteGrupoProducto>());
            ConfigurarFakeDbSet(vendedores, new List<Vendedor>
            {
                new Vendedor { Empresa = "1", Número = "MPP", Estado = Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO },
                new Vendedor { Empresa = "1", Número = "PA", Estado = 0 },
                new Vendedor { Empresa = "1", Número = "JE", Estado = 0 }
            });
            servicioVendedores = A.Fake<IServicioVendedores>();
            A.CallTo(() => servicioVendedores.VendedoresEquipo("1", "PA"))
                .Returns(Task.FromResult(new List<VendedorDTO> { new VendedorDTO { vendedor = "PA" } }));
            A.CallTo(() => servicioVendedores.VendedoresEquipo("1", "MPP"))
                .Returns(Task.FromResult(new List<VendedorDTO> { new VendedorDTO { vendedor = "MPP" } }));
            controller = new ClientesController(A.Fake<IGestorClientes>(), servicioVendedores, A.Fake<IGestorSincronizacion>(), dbInyectada: db);
        }

        private static DbSet<T> Crear<T>() where T : class
            => A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            IQueryable<T> data = lista.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        private static Cliente Cliente(string numero, string contacto, string nombre, string vendedor, short estado)
            => new Cliente
            {
                Empresa = "1",
                Nº_Cliente = numero,
                Contacto = contacto,
                Nombre = nombre,
                Vendedor = vendedor,
                Estado = estado,
                Cadena = "",
                CCC = "",
                CIF_NIF = "",
                CodPostal = "",
                ComentarioPicking = "",
                ComentarioRuta = "",
                Comentarios = "",
                Dirección = "",
                Grupo = "",
                IVA = "",
                PeriodoFacturación = "",
                Población = "",
                Provincia = "",
                Ruta = "",
                Teléfono = "",
                Web = ""
            };

        private static List<string> Claves(IEnumerable<ClienteDTO> lista)
            => lista.Select(c => c.cliente + "/" + c.contacto).ToList();

        [TestMethod]
        public async Task GetClientes_SinVendedorYConVendedorTelefonicoDeUnTelefonico_BuscaEnTodosYPoneDelanteElContactoTelefonico()
        {
            List<string> resultado = Claves(await controller.GetClientes("1", "", FILTRO, contactoTelefonicoPrimero: true, vendedorTelefonico: "MPP"));

            CollectionAssert.AreEqual(new List<string> { "27120/1", "27120/0", "30676/0" }, resultado,
                "Todos los clientes (vendedor vacío) y, dentro del 27120, el contacto en estado 9 delante");
        }

        [TestMethod]
        public async Task GetClientes_ConVendedorYSinVendedorTelefonico_ComoAntes()
        {
            // PA no es telefónico: solo sus clientes y en el orden de siempre
            List<string> deUnPresencial = Claves(await controller.GetClientes("1", "PA", FILTRO, contactoTelefonicoPrimero: true));
            CollectionAssert.AreEqual(new List<string> { "27120/0", "27120/1" }, deUnPresencial);

            // Sin vendedorTelefonico decide «vendedor», como en el Nesto ya publicado
            List<string> sinVendedor = Claves(await controller.GetClientes("1", null, FILTRO, contactoTelefonicoPrimero: true));
            CollectionAssert.AreEqual(new List<string> { "27120/0", "27120/1", "30676/0" }, sinVendedor);
        }

        [TestMethod]
        public async Task GetClientes_ConVendedorTelefonicoVacio_DecideVendedorComoAntes()
        {
            var equipoMpp = new List<VendedorDTO> { new VendedorDTO { vendedor = "MPP" }, new VendedorDTO { vendedor = "PA" } };
            A.CallTo(() => servicioVendedores.VendedoresEquipo("1", "MPP")).Returns(Task.FromResult(equipoMpp));

            List<string> resultado = Claves(await controller.GetClientes("1", "MPP", FILTRO, contactoTelefonicoPrimero: true, vendedorTelefonico: ""));

            CollectionAssert.AreEqual(new List<string> { "27120/1", "27120/0" }, resultado,
                "vendedorTelefonico vacío = no viene: MPP es telefónico y filtra por su equipo");
        }

        [TestMethod]
        public async Task GetClientes_ConVendedorTelefonicoDeUnoQueNoEsTelefonico_SinPrioridadNiError()
        {
            List<string> resultado = Claves(await controller.GetClientes("1", "", FILTRO, contactoTelefonicoPrimero: true, vendedorTelefonico: "PA"));

            CollectionAssert.AreEqual(new List<string> { "27120/0", "27120/1", "30676/0" }, resultado);
        }

        [TestMethod]
        public async Task GetClientes_ConVendedorTelefonicoPeroSinContactoTelefonicoPrimero_SinPrioridad()
        {
            List<string> resultado = Claves(await controller.GetClientes("1", "", FILTRO, contactoTelefonicoPrimero: false, vendedorTelefonico: "MPP"));

            CollectionAssert.AreEqual(new List<string> { "27120/0", "27120/1", "30676/0" }, resultado);
        }

        [TestMethod]
        public async Task GetClientes_ConVendedorDeOtroYVendedorTelefonico_FiltraPorVendedorYPrioridadPorElTelefonico()
        {
            // «vendedor» solo filtra (equipo de PA); quién busca lo dice vendedorTelefonico
            List<string> resultado = Claves(await controller.GetClientes("1", "PA", FILTRO, contactoTelefonicoPrimero: true, vendedorTelefonico: "MPP"));

            CollectionAssert.AreEqual(new List<string> { "27120/1", "27120/0" }, resultado);
        }

        // Enrutado: varias acciones GET convencionales en ClientesController se distinguen por sus
        // parámetros. Se comprueba con el selector real de Web API y la ruta DefaultApi de WebApiConfig.
        private static HttpActionDescriptor AccionPara(string consulta)
        {
            // Las rutas por atributo también: así las acciones con [Route] quedan fuera de la ruta convencional,
            // igual que en producción
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute("DefaultApi", "api/{controller}/{id}", new { id = RouteParameter.Optional });
            config.EnsureInitialized();
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/Clientes?" + consulta);
            IHttpRouteData routeData = config.Routes.GetRouteData(request);
            request.SetConfiguration(config);
            request.SetRouteData(routeData);
            HttpControllerDescriptor descriptor = config.Services.GetHttpControllerSelector().SelectController(request);
            var contexto = new HttpControllerContext(config, routeData, request) { ControllerDescriptor = descriptor };
            return config.Services.GetActionSelector().SelectAction(contexto);
        }

        private static string Firma(HttpActionDescriptor accion)
            => accion.ActionName + "(" + string.Join(",", accion.GetParameters().Select(p => p.ParameterName)) + ")";

        private const string BUSQUEDA_CON_VENDEDOR = "GetClientes(empresa,vendedor,filtro,contactoTelefonicoPrimero,vendedorTelefonico)";

        [TestMethod]
        public void Enrutado_LasCombinacionesQueSeUsanHoyYLaNueva_VanALaAccionDeSiempre()
        {
            // Nesto: SelectorCliente, plantilla de venta, ficha comercial
            Assert.AreEqual(BUSQUEDA_CON_VENDEDOR, Firma(AccionPara("empresa=1&vendedor=PA&filtro=peluqueria")));
            Assert.AreEqual(BUSQUEDA_CON_VENDEDOR, Firma(AccionPara("empresa=1&vendedor=&filtro=peluqueria")));
            Assert.AreEqual(BUSQUEDA_CON_VENDEDOR, Firma(AccionPara("empresa=1&vendedor=PA&filtro=peluqueria&contactoTelefonicoPrimero=true")));
            // Nesto nuevo (Rapports con PermitirVerClientesTodosLosVendedores)
            Assert.AreEqual(BUSQUEDA_CON_VENDEDOR, Firma(AccionPara("empresa=1&vendedor=&filtro=peluqueria&contactoTelefonicoPrimero=true&vendedorTelefonico=MPP")));
            Assert.AreEqual(BUSQUEDA_CON_VENDEDOR, Firma(AccionPara("empresa=1&vendedor=MPP&filtro=peluqueria&contactoTelefonicoPrimero=true&vendedorTelefonico=MPP")));
            // NestoApp y Nesto sin vendedor
            Assert.AreEqual("GetClientes(empresa,filtro)", Firma(AccionPara("empresa=1&filtro=peluqueria")));
            Assert.AreEqual("GetClientes(filtro)", Firma(AccionPara("filtro=peluqueria")));
            // La carga de un cliente (con y sin vendedorTelefonico) no cambia
            Assert.AreEqual("GetCliente(empresa,cliente,contacto,vendedorTelefonico)", Firma(AccionPara("empresa=1&cliente=27120&contacto=")));
            Assert.AreEqual("GetCliente(empresa,cliente,contacto,vendedorTelefonico)", Firma(AccionPara("empresa=1&cliente=27120&contacto=&vendedorTelefonico=MPP")));
        }
    }
}
