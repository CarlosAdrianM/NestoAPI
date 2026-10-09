using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Vendedores
{
    /// <summary>
    /// Nesto#521 / NestoApp#219: de qué vendedores puede ver un usuario los «clientes para contactar». Jefe de ventas: el
    /// suyo y su equipo; Dirección: todos; resto: el suyo. Y el 403 de los endpoints que reciben vendedor.
    /// </summary>
    [TestClass]
    public class ServicioVendedoresVisiblesTests
    {
        private NVEntities db;
        private IServicioVendedores servicioVendedores;
        private List<ParametroUsuario> parametros;
        private List<UsuarioVendedor> usuariosVendedores;
        private List<Vendedor> vendedores;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            parametros = new List<ParametroUsuario>
            {
                Parametro("Sancho", "ASH"),
                Parametro("MariaJose", "MPP"),
                Parametro("Carlos", "CAM"),
                Parametro("Manuel", "MR"),
                Parametro("Eva", "NV"),          // solo en ParametrosUsuario
                Parametro("Ana", ""),            // con la fila pero sin vendedor
                new ParametroUsuario { Empresa = "1", Usuario = "MariaJose", Clave = "OtraClave", Valor = "ASH" }
            };
            usuariosVendedores = new List<UsuarioVendedor>
            {
                new UsuarioVendedor { Usuario = "Sancho", Vendedor = "ASH" },
                new UsuarioVendedor { Usuario = "MariaJose", Vendedor = "MPP" },
                new UsuarioVendedor { Usuario = "Carlos", Vendedor = "CAM" },
                new UsuarioVendedor { Usuario = "Pepe", Vendedor = "PEP" }   // solo en UsuarioVendedor
            };
            vendedores = new List<Vendedor>
            {
                VendedorDe("ASH", "Alberto Sancho"),
                VendedorDe("DV", "David"),
                VendedorDe("JE", "Jesús"),
                VendedorDe("MPP", "María José"),
                VendedorDe("CAM", "Carlos"),
                VendedorDe("MR", "Manuel"),
                VendedorDe("NV", "General"),
                VendedorDe("PEP", "Pepe"),
                VendedorDe("BAJ", "De baja", estado: -1)
            };
            Configurar(() => db.ParametrosUsuario, parametros);
            Configurar(() => db.UsuarioVendedores, usuariosVendedores);
            Configurar(() => db.Vendedores, vendedores);

            servicioVendedores = A.Fake<IServicioVendedores>();
            // Como ServicioVendedores.VendedoresEquipo: el equipo y, al final, el propio.
            A.CallTo(() => servicioVendedores.VendedoresEquipo(A<string>._, A<string>._)).ReturnsLazily((string empresa, string vendedor) =>
                Task.FromResult(vendedores.Where(v => v.Número == vendedor.Trim().ToUpperInvariant())
                    .Select(v => new VendedorDTO { vendedor = v.Número, nombre = v.Descripción }).ToList()));
            A.CallTo(() => servicioVendedores.VendedoresEquipo("1", "ASH")).Returns(Task.FromResult(new List<VendedorDTO>
            {
                new VendedorDTO { vendedor = "DV", nombre = "David" },
                new VendedorDTO { vendedor = "JE", nombre = "Jesús" },
                new VendedorDTO { vendedor = "ASH", nombre = "Alberto Sancho" }
            }));
        }

        private static ParametroUsuario Parametro(string usuario, string vendedor) =>
            new ParametroUsuario { Empresa = "1", Usuario = usuario, Clave = "Vendedor", Valor = vendedor };

        private static Vendedor VendedorDe(string numero, string nombre, short estado = 0) =>
            new Vendedor { Empresa = "1", Número = numero, Descripción = nombre, Estado = estado };

        private ServicioVendedoresVisibles Servicio() => new ServicioVendedoresVisibles(db, servicioVendedores);

        /// <summary>Nesto: JWT de windows-token, nombre con dominio y grupos de AD como roles.</summary>
        private static IPrincipal Empleado(string usuario, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + usuario) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"));
        }

        /// <summary>NestoApp: JWT de oauth/token, nombre de Identity sin dominio, claim Vendedor y roles de Identity.</summary>
        private static IPrincipal UsuarioNestoApp(string usuario, string vendedor)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, usuario), new Claim(ClaimTypes.Role, "Vendedor") };
            if (vendedor != null)
            {
                claims.Add(new Claim("IsVendedor", "true"));
                claims.Add(new Claim("Vendedor", vendedor));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"));
        }

        // ---------------- PuedeVer ----------------

        [TestMethod]
        public async Task PuedeVer_JefeDeVentas_LosDeSuEquipoYElSuyo()
        {
            IPrincipal sancho = Empleado("Sancho", "Ventas");

            Assert.IsTrue(await Servicio().PuedeVer(sancho, "1", "dv "));
            Assert.IsTrue(await Servicio().PuedeVer(sancho, "1", "JE"));
            Assert.IsTrue(await Servicio().PuedeVer(sancho, "1", "ASH"));
        }

        [TestMethod]
        public async Task PuedeVer_JefeDeVentas_FueraDeSuEquipo_False()
        {
            Assert.IsFalse(await Servicio().PuedeVer(Empleado("Sancho", "Ventas"), "1", "MPP"));
        }

        [TestMethod]
        public async Task PuedeVer_Direccion_CualquierVendedorInclusoDeBaja()
        {
            IPrincipal manuel = Empleado("Manuel", "Dirección");

            Assert.IsTrue(await Servicio().PuedeVer(manuel, "1", "MPP"));
            Assert.IsTrue(await Servicio().PuedeVer(manuel, "1", "BAJ"));
            A.CallTo(() => servicioVendedores.VendedoresEquipo(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PuedeVer_VendedorNormalDesdeNesto_SoloElSuyo()
        {
            IPrincipal mariaJose = Empleado("MariaJose", "Ventas");

            Assert.IsTrue(await Servicio().PuedeVer(mariaJose, "1", "MPP"));
            Assert.IsFalse(await Servicio().PuedeVer(mariaJose, "1", "ASH"));
            Assert.IsFalse(await Servicio().PuedeVer(mariaJose, "1", "DV"));
        }

        [TestMethod]
        public async Task PuedeVer_VendedorNormalDesdeNestoApp_ElDelClaimSinDominio()
        {
            IPrincipal mariaJose = UsuarioNestoApp("MariaJose", "MPP");

            Assert.IsTrue(await Servicio().PuedeVer(mariaJose, "1", "mpp"));
            Assert.IsFalse(await Servicio().PuedeVer(mariaJose, "1", "DV"));
        }

        [TestMethod]
        public async Task PuedeVer_VendedorSoloEnElClaim_Vale()
        {
            // Usuario de Identity cuyo nombre no está en ninguna tabla de la BD de negocio.
            Assert.IsTrue(await Servicio().PuedeVer(UsuarioNestoApp("vendedora@nuevavision.es", "AGR"), "1", "AGR"));
        }

        [TestMethod]
        public async Task PuedeVer_VendedorSoloEnParametrosUsuario_Vale()
        {
            // Lo que Nesto y NestoApp piden por defecto sale de ParametrosUsuario: aunque no esté en UsuarioVendedor.
            Assert.IsTrue(await Servicio().PuedeVer(Empleado("Eva"), "1", "NV"));
        }

        [TestMethod]
        public async Task PuedeVer_VendedorSoloEnUsuarioVendedor_Vale()
        {
            Assert.IsTrue(await Servicio().PuedeVer(Empleado("Pepe"), "1", "PEP"));
        }

        [TestMethod]
        public async Task PuedeVer_SinVendedorNiDireccion_False()
        {
            Assert.IsFalse(await Servicio().PuedeVer(Empleado("Ana", "Administración"), "1", "MPP"));
        }

        [TestMethod]
        public async Task PuedeVer_SinAutenticarOSinVendedorPedido_False()
        {
            var anonimo = new ClaimsPrincipal(new ClaimsIdentity());
            Assert.IsFalse(await Servicio().PuedeVer(anonimo, "1", "MPP"));
            Assert.IsFalse(await Servicio().PuedeVer(null, "1", "MPP"));
            Assert.IsFalse(await Servicio().PuedeVer(Empleado("Manuel", "Dirección"), "1", " "));
        }

        // ---------------- LeerVisibles ----------------

        [TestMethod]
        public async Task LeerVisibles_JefeDeVentas_ElSuyoPrimeroYLuegoSuEquipoPorNombre()
        {
            List<VendedorVisibleDTO> visibles = await Servicio().LeerVisibles(Empleado("Sancho", "Ventas"), null);

            CollectionAssert.AreEqual(new[] { "ASH", "DV", "JE" }, visibles.Select(v => v.Vendedor).ToArray());
            CollectionAssert.AreEqual(new[] { "Alberto Sancho", "David", "Jesús" }, visibles.Select(v => v.Nombre).ToArray());
            A.CallTo(() => servicioVendedores.VendedoresEquipo("1", "ASH")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerVisibles_Direccion_ElSuyoPrimeroYTodosLosActivos()
        {
            List<VendedorVisibleDTO> visibles = await Servicio().LeerVisibles(Empleado("Manuel", "Dirección"), "1");

            Assert.AreEqual("MR", visibles[0].Vendedor);
            Assert.AreEqual("Manuel", visibles[0].Nombre);
            CollectionAssert.AreEquivalent(new[] { "ASH", "DV", "JE", "MPP", "CAM", "MR", "NV", "PEP" }, visibles.Select(v => v.Vendedor).ToArray());
            Assert.IsFalse(visibles.Any(v => v.Vendedor == "BAJ"), "solo los activos");
            CollectionAssert.AreEqual(visibles.Skip(1).Select(v => v.Nombre).OrderBy(n => n).ToArray(), visibles.Skip(1).Select(v => v.Nombre).ToArray());
        }

        [TestMethod]
        public async Task LeerVisibles_VendedorSinEquipo_SoloElSuyo()
        {
            List<VendedorVisibleDTO> visibles = await Servicio().LeerVisibles(UsuarioNestoApp("MariaJose", "MPP"), "1");

            Assert.AreEqual(1, visibles.Count);
            Assert.AreEqual("MPP", visibles[0].Vendedor);
            Assert.AreEqual("María José", visibles[0].Nombre);
        }

        [TestMethod]
        public async Task LeerVisibles_SinVendedorNiDireccion_Vacia()
        {
            Assert.AreEqual(0, (await Servicio().LeerVisibles(Empleado("Ana", "Administración"), "1")).Count);
        }

        // ---------------- Controladores ----------------

        [TestMethod]
        public async Task SugerenciasContacto_JefePideUnoDeSuEquipo_200ConEseVendedor()
        {
            var sugerencias = A.Fake<IServicioSugerenciasContacto>();
            A.CallTo(() => sugerencias.Leer("DV", A<string>._, A<int>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new SugerenciasContactoDTO { Vendedor = "DV" }));
            var controlador = new SugerenciasContactoController(db, sugerencias, null, Servicio())
            {
                Request = new HttpRequestMessage(),
                User = Empleado("Sancho", "Ventas")
            };

            var resultado = await controlador.GetSugerenciasContacto("DV") as OkNegotiatedContentResult<SugerenciasContactoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("DV", resultado.Content.Vendedor);
            A.CallTo(() => sugerencias.Leer("DV", A<string>._, A<int>._, A<string>._, "NUEVAVISION\\Sancho")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SugerenciasContacto_FueraDelEquipo_403SinLeerNada()
        {
            var sugerencias = A.Fake<IServicioSugerenciasContacto>();
            var controlador = new SugerenciasContactoController(db, sugerencias, null, Servicio())
            {
                Request = new HttpRequestMessage(),
                User = Empleado("Sancho", "Ventas")
            };

            var resultado = await controlador.GetSugerenciasContacto("MPP") as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => sugerencias.Leer(A<string>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SugerenciasContacto_VendedorNormalConElSuyoDesdeNestoApp_200()
        {
            var sugerencias = A.Fake<IServicioSugerenciasContacto>();
            var controlador = new SugerenciasContactoController(db, sugerencias, null, Servicio())
            {
                Request = new HttpRequestMessage(),
                User = UsuarioNestoApp("MariaJose", "MPP")
            };

            Assert.IsInstanceOfType(await controlador.GetSugerenciasContacto("MPP"), typeof(OkNegotiatedContentResult<SugerenciasContactoDTO>));
        }

        [TestMethod]
        public async Task SugerenciasContacto_Direccion_CualquierVendedor200()
        {
            var sugerencias = A.Fake<IServicioSugerenciasContacto>();
            var controlador = new SugerenciasContactoController(db, sugerencias, null, Servicio())
            {
                Request = new HttpRequestMessage(),
                User = Empleado("Carlos", "Dirección")
            };

            Assert.IsInstanceOfType(await controlador.GetSugerenciasContacto("MPP"), typeof(OkNegotiatedContentResult<SugerenciasContactoDTO>));
        }

        [TestMethod]
        public async Task GetClientesProbabilidadVenta_FueraDelEquipo_403SinCalcular()
        {
            var gestor = A.Fake<IGestorClientes>();
            var controlador = new ClientesController(gestor, servicioVendedores, A.Fake<IGestorSincronizacion>(), null, db, Servicio())
            {
                Request = new HttpRequestMessage(),
                User = Empleado("MariaJose", "Ventas")
            };

            var resultado = await controlador.GetClientesProbabilidadVenta("ASH") as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => gestor.BuscarClientesPorProbabilidadVenta(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetClientesProbabilidadVenta_JefeConUnoDeSuEquipo_200()
        {
            var gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.BuscarClientesPorProbabilidadVenta("JE", 20, A<string>._, A<string>._))
                .Returns(Task.FromResult(new List<ClienteProbabilidadVenta>()));
            var controlador = new ClientesController(gestor, servicioVendedores, A.Fake<IGestorSincronizacion>(), null, db, Servicio())
            {
                Request = new HttpRequestMessage(),
                User = Empleado("Sancho", "Ventas")
            };

            Assert.IsInstanceOfType(await controlador.GetClientesProbabilidadVenta("JE"), typeof(OkNegotiatedContentResult<List<ClienteProbabilidadVenta>>));
            A.CallTo(() => gestor.BuscarClientesPorProbabilidadVenta("JE", 20, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task VisiblesEnSugerencias_LosTresCasos()
        {
            async Task<string[]> Visibles(IPrincipal usuario)
            {
                var controlador = new VendedoresController(servicioVendedores, Servicio(), db) { Request = new HttpRequestMessage(), User = usuario };
                var resultado = await controlador.GetVisiblesEnSugerencias() as OkNegotiatedContentResult<List<VendedorVisibleDTO>>;
                Assert.IsNotNull(resultado);
                return resultado.Content.Select(v => v.Vendedor).ToArray();
            }

            CollectionAssert.AreEqual(new[] { "ASH", "DV", "JE" }, await Visibles(Empleado("Sancho", "Ventas")), "jefe: el suyo y su equipo");
            CollectionAssert.AreEqual(new[] { "MPP" }, await Visibles(UsuarioNestoApp("MariaJose", "MPP")), "sin equipo: solo el suyo");
            string[] direccion = await Visibles(Empleado("Carlos", "Dirección"));
            Assert.AreEqual("CAM", direccion[0], "Dirección: el suyo primero");
            Assert.AreEqual(8, direccion.Length, "Dirección: todos los activos");
        }

        private void Configurar<T>(System.Linq.Expressions.Expression<System.Func<DbSet<T>>> propiedad, List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(propiedad).Returns(fake);
        }
    }
}
