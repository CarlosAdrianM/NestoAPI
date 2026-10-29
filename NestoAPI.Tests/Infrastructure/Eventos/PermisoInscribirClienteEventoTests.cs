using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#741 (sugerencia 863): quién puede inscribir a quién en un evento. Cada comercial a sus clientes (estética o
    /// peluquería), el jefe de equipo a los de su equipo, Administración/Tiendas/Tienda online/Dirección/Informática a todos, el
    /// cliente de la tienda a sí mismo; el resto, 403 de usted.
    /// </summary>
    [TestClass]
    public class PermisoInscribirClienteEventoTests
    {
        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda(string cliente) =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", cliente) }, "prueba"));

        private static readonly IPrincipal COMERCIAL = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal JEFE = Usuario("NUEVAVISION\\Sancho", "Ventas");

        // ---------------- La regla (PermisosEventos.PuedeInscribir) ----------------

        [TestMethod]
        public void Regla_ComercialConSuClienteDeEstetica_Puede()
        {
            Assert.IsTrue(PermisosEventos.PuedeInscribir(COMERCIAL, "15191", "JE", new[] { "JE" }, new[] { "JE " }));
        }

        [TestMethod]
        public void Regla_ComercialConClienteAjeno_NoPuede()
        {
            Assert.IsFalse(PermisosEventos.PuedeInscribir(COMERCIAL, "20000", "JE", new[] { "JE" }, new[] { "PA" }));
        }

        [TestMethod]
        public void Regla_ComercialDePeluqueriaDelCliente_Puede()
        {
            // Estética de otro (PA), peluquería suya (JE)
            Assert.IsTrue(PermisosEventos.PuedeInscribir(COMERCIAL, "15191", "je", null, new[] { "PA", "JE" }));
        }

        [TestMethod]
        public void Regla_JefeDeEquipo_ClienteDeSuEquipo_Puede()
        {
            Assert.IsTrue(PermisosEventos.PuedeInscribir(JEFE, "15191", "ASH", new[] { "JE", "PA", "ASH" }, new[] { "PA" }));
        }

        [TestMethod]
        public void Regla_JefeDeEquipo_ClienteDeOtroEquipo_NoPuede()
        {
            Assert.IsFalse(PermisosEventos.PuedeInscribir(JEFE, "15191", "ASH", new[] { "JE", "PA", "ASH" }, new[] { "RV" }));
        }

        [TestMethod]
        public void Regla_AdministracionTiendasTiendaOnlineDireccionInformatica_PuedenConCualquiera()
        {
            foreach (string grupo in new[] { "Administración", "Tiendas", "TiendaOnline", "Dirección", "Informática" })
            {
                Assert.IsTrue(PermisosEventos.PuedeInscribir(Usuario("NUEVAVISION\\Alguien", grupo), "20000", null, null, new[] { "PA" }), grupo);
                Assert.IsTrue(PermisosEventos.PuedeInscribirACualquiera(Usuario("NUEVAVISION\\Alguien", grupo)), grupo);
            }
        }

        [TestMethod]
        public void Regla_ClienteDeLaTienda_SoloASiMismo()
        {
            Assert.IsTrue(PermisosEventos.PuedeInscribir(ClienteTienda("15191"), "15191", null, null, null));
            Assert.IsFalse(PermisosEventos.PuedeInscribir(ClienteTienda("15191"), "20000", null, null, null));
            Assert.IsFalse(PermisosEventos.PuedeInscribirACualquiera(ClienteTienda("15191")));
        }

        [TestMethod]
        public void Regla_UsuarioSinVendedor_NoPuede()
        {
            Assert.IsFalse(PermisosEventos.PuedeInscribir(Usuario("NUEVAVISION\\Almacen", "Almacén"), "15191", null, null, new[] { "JE" }));
            Assert.IsFalse(PermisosEventos.PuedeInscribir(null, "15191", "JE", null, new[] { "JE" }));
        }

        // ---------------- El autorizador (datos + regla + 403) ----------------

        private static AutorizadorInscripcionEventos Autorizador(ILectorVendedoresInscripcion lector) => new AutorizadorInscripcionEventos(lector);

        private static ILectorVendedoresInscripcion Lector(string vendedorUsuario, List<string> equipo, List<string> delCliente)
        {
            var lector = A.Fake<ILectorVendedoresInscripcion>();
            A.CallTo(() => lector.VendedorDelUsuario(A<IPrincipal>._)).Returns(vendedorUsuario);
            A.CallTo(() => lector.VendedoresDelEquipo(A<string>._, A<string>._)).Returns(Task.FromResult(equipo));
            A.CallTo(() => lector.VendedoresDelCliente(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(delCliente));
            return lector;
        }

        [TestMethod]
        public async Task Autorizador_ComercialPropio_NoLanza()
        {
            var lector = Lector("JE", new List<string> { "JE" }, new List<string> { "JE" });
            await Autorizador(lector).Comprobar(COMERCIAL, "1", "15191", "0");
            A.CallTo(() => lector.VendedoresDelCliente("1", "15191", "0")).MustHaveHappened();
        }

        [TestMethod]
        public async Task Autorizador_ComercialAjeno_403DeUsted()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Autorizador(Lector("JE", new List<string> { "JE" }, new List<string> { "PA" })).Comprobar(COMERCIAL, "1", "20000", "0"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual(PermisosEventos.MENSAJE_SIN_PERMISO_INSCRIBIR, ex.Message);
            StringAssert.StartsWith(ex.Message, "Solo puede inscribir a sus clientes");
        }

        [TestMethod]
        public async Task Autorizador_UsuarioSinVendedor_403SinMirarElCliente()
        {
            var lector = Lector(null, null, new List<string> { "JE" });
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Autorizador(lector).Comprobar(Usuario("NUEVAVISION\\Almacen", "Almacén"), "1", "15191", "0"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            A.CallTo(() => lector.VendedoresDelCliente(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Autorizador_JefeDeEquipo_ClienteDeSuEquipo()
        {
            var lector = Lector("ASH", new List<string> { "JE", "PA", "ASH" }, new List<string> { "PA" });
            Assert.IsTrue(await Autorizador(lector).PuedeInscribir(JEFE, "1", "15191", "0"));
            A.CallTo(() => lector.VendedoresDelEquipo("1", "ASH")).MustHaveHappened();
        }

        [TestMethod]
        public async Task Autorizador_Administracion_NoLeeNada()
        {
            var lector = Lector(null, null, null);
            await Autorizador(lector).Comprobar(Usuario("NUEVAVISION\\Admin", "Administración"), "1", "20000", "0");
            A.CallTo(() => lector.VendedorDelUsuario(A<IPrincipal>._)).MustNotHaveHappened();
        }

        // ---------------- El lector de producción ----------------

        [TestMethod]
        public async Task Lector_VendedoresDelCliente_EsteticaDelContactoYPeluqueriaDelContactoYDelPrincipal()
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Clientes).Returns(Crear(new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", ClientePrincipal = true, Vendedor = "JE " },
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1  ", ClientePrincipal = false, Vendedor = "PA " }
            }));
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(Crear(new List<VendedorClienteGrupoProducto>
            {
                new VendedorClienteGrupoProducto { Empresa = "1", Cliente = "15191", Contacto = "0  ", GrupoProducto = "PEL", Vendedor = "RV " },
                new VendedorClienteGrupoProducto { Empresa = "1", Cliente = "15191", Contacto = "1  ", GrupoProducto = "PEL", Vendedor = "AL " },
                new VendedorClienteGrupoProducto { Empresa = "1", Cliente = "15191", Contacto = "1  ", GrupoProducto = "COS", Vendedor = "XX " }
            }));

            List<string> vendedores = await new LectorVendedoresInscripcion(db, A.Fake<IServicioVendedores>())
                .VendedoresDelCliente("1", "15191", "1");

            CollectionAssert.AreEquivalent(new[] { "PA", "AL", "RV" }, vendedores);
        }

        [TestMethod]
        public void Lector_VendedorDelUsuario_ClaimYSiNoSuParametroSinDominio_NoElDeDefecto()
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.ParametrosUsuario).Returns(Crear(new List<ParametroUsuario>
            {
                new ParametroUsuario { Empresa = "1", Usuario = "Jesus", Clave = "Vendedor", Valor = "JE  " },
                new ParametroUsuario { Empresa = "1", Usuario = "(defecto)", Clave = "Vendedor", Valor = "NV" }
            }));
            var lector = new LectorVendedoresInscripcion(db, A.Fake<IServicioVendedores>());
            var conClaim = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "paloma"), new Claim("Vendedor", "PA") }, "prueba"));

            Assert.AreEqual("PA", lector.VendedorDelUsuario(conClaim));
            Assert.AreEqual("JE", lector.VendedorDelUsuario(COMERCIAL));
            Assert.IsNull(lector.VendedorDelUsuario(Usuario("NUEVAVISION\\Almacen", "Almacén")));
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
