using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.ParametrosUsuario;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Caso real 20/08/26: el usuario de Tienda Online que factura FBA (almacén AMZ) necesita
    /// pasar a ALG los días que cubre rutas, y volver. El catálogo de parámetros editables por
    /// el propio usuario vive server-side; el POST valida grupo, clave y valor y captura el
    /// valor TITULAR la primera vez (para ofrecer restaurarlo al arrancar Nesto).
    /// </summary>
    [TestClass]
    public class ServicioParametrosEditablesTests
    {
        private NVEntities db;
        private DbSet<ParametroUsuario> fakeParametros;
        private DbSet<Modificacion> fakeModificaciones;
        private List<ParametroUsuario> parametrosAnadidos;
        private List<Modificacion> modificacionesAnadidas;
        private ServicioParametrosEditables servicio;

        private static readonly List<OpcionParametroDTO> ALMACENES = new List<OpcionParametroDTO>
        {
            new OpcionParametroDTO { Valor = "ALG", Descripcion = "Algete" },
            new OpcionParametroDTO { Valor = "AMZ", Descripcion = "Amazon" }
        };

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeParametros = A.Fake<DbSet<ParametroUsuario>>(o => o.Implements<IQueryable<ParametroUsuario>>().Implements<IDbAsyncEnumerable<ParametroUsuario>>());
            fakeModificaciones = A.Fake<DbSet<Modificacion>>(o => o.Implements<IQueryable<Modificacion>>().Implements<IDbAsyncEnumerable<Modificacion>>());
            A.CallTo(() => db.ParametrosUsuario).Returns(fakeParametros);
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            parametrosAnadidos = new List<ParametroUsuario>();
            modificacionesAnadidas = new List<Modificacion>();
            _ = A.CallTo(() => fakeParametros.Add(A<ParametroUsuario>.Ignored))
                .Invokes((ParametroUsuario p) => parametrosAnadidos.Add(p))
                .ReturnsLazily((ParametroUsuario p) => p);
            _ = A.CallTo(() => fakeModificaciones.Add(A<Modificacion>.Ignored))
                .Invokes((Modificacion m) => modificacionesAnadidas.Add(m))
                .ReturnsLazily((Modificacion m) => m);
            ConParametros();

            // Catálogo de test con las opciones inyectadas (Almacenes no está en el EDMX)
            var catalogo = new List<ServicioParametrosEditables.DefinicionParametroEditable>
            {
                new ServicioParametrosEditables.DefinicionParametroEditable
                {
                    Clave = "AlmacénPedidoVta",
                    Descripcion = "Almacén de pedidos de venta",
                    Grupos = new[] { Constantes.GruposSeguridad.TIENDA_ON_LINE },
                    ClaveTitular = "AlmacénPedidoVtaTitular",
                    CargarOpciones = (bd, empresa) => Task.FromResult(ALMACENES)
                }
            };
            servicio = new ServicioParametrosEditables(db, catalogo);
        }

        private void ConParametros(params ParametroUsuario[] parametros)
        {
            var data = parametros.ToList().AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<ParametroUsuario>)fakeParametros).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<ParametroUsuario>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<ParametroUsuario>)fakeParametros).Provider)
                .Returns(new TestDbAsyncQueryProvider<ParametroUsuario>(data.Provider));
            A.CallTo(() => ((IQueryable<ParametroUsuario>)fakeParametros).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<ParametroUsuario>)fakeParametros).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<ParametroUsuario>)fakeParametros).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private static ParametroUsuario Parametro(string clave, string valor, string usuario = "Laura")
            => new ParametroUsuario { Empresa = "1", Usuario = usuario, Clave = clave, Valor = valor };

        // El JWT de empleado trae los roles como "NUEVAVISION\Grupo": IsInRoleSinDominio los pela
        private static ClaimsPrincipal UsuarioDe(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Laura") };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        [TestMethod]
        public async Task LeerEditables_UsuarioDelGrupo_DevuelveElParametroConOpcionesYValores()
        {
            ConParametros(Parametro("AlmacénPedidoVta", "AMZ"));

            List<ParametroEditableDTO> editables = await servicio.LeerEditables(
                UsuarioDe(Constantes.GruposSeguridad.TIENDA_ON_LINE), "1");

            ParametroEditableDTO almacen = editables.Single();
            Assert.AreEqual("AlmacénPedidoVta", almacen.Clave);
            Assert.AreEqual("AMZ", almacen.ValorActual);
            Assert.IsNull(almacen.ValorTitular, "Aún no ha cambiado nunca: sin titular");
            CollectionAssert.AreEqual(new[] { "ALG", "AMZ" }, almacen.Opciones.Select(o => o.Valor).ToArray());
        }

        [TestMethod]
        public async Task LeerEditables_UsuarioSinElGrupo_NoVeNada()
        {
            List<ParametroEditableDTO> editables = await servicio.LeerEditables(
                UsuarioDe(Constantes.GruposSeguridad.COMPRAS), "1");

            Assert.AreEqual(0, editables.Count);
        }

        [TestMethod]
        public async Task Cambiar_PrimeraVez_EscribeElValorYCapturaElTitular()
        {
            // El caso real: tenía AMZ (factura FBA), cambia a ALG para cubrir las rutas
            ConParametros(Parametro("AlmacénPedidoVta", "AMZ"));

            ParametroEditableDTO resultado = await servicio.Cambiar(
                UsuarioDe(Constantes.GruposSeguridad.TIENDA_ON_LINE),
                new CambioParametroRequest { Empresa = "1", Clave = "AlmacénPedidoVta", Valor = "ALG" });

            Assert.AreEqual("ALG", resultado.ValorActual);
            Assert.AreEqual("AMZ", resultado.ValorTitular, "El valor que tenía pasa a ser su titular");
            Assert.IsTrue(parametrosAnadidos.Any(p => p.Clave == "AlmacénPedidoVtaTitular" && p.Valor == "AMZ"),
                "El titular se persiste");
            Assert.IsTrue(modificacionesAnadidas.Any(m => m.Tabla == "ParametrosUsuario"),
                "El cambio de un dato de configuración se audita");
        }

        [TestMethod]
        public async Task Cambiar_ConTitularYaCapturado_NoLoMachaca()
        {
            // Vuelta atrás: está en ALG (titular AMZ) y restaura AMZ. El titular no cambia.
            ConParametros(Parametro("AlmacénPedidoVta", "ALG"), Parametro("AlmacénPedidoVtaTitular", "AMZ"));

            ParametroEditableDTO resultado = await servicio.Cambiar(
                UsuarioDe(Constantes.GruposSeguridad.TIENDA_ON_LINE),
                new CambioParametroRequest { Empresa = "1", Clave = "AlmacénPedidoVta", Valor = "AMZ" });

            Assert.AreEqual("AMZ", resultado.ValorActual);
            Assert.AreEqual("AMZ", resultado.ValorTitular);
            Assert.IsFalse(parametrosAnadidos.Any(p => p.Clave == "AlmacénPedidoVtaTitular"),
                "El titular ya existía: no se crea otro");
        }

        [TestMethod]
        public async Task Cambiar_ValorFueraDeLasOpciones_SeRechaza()
        {
            ConParametros(Parametro("AlmacénPedidoVta", "AMZ"));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.Cambiar(
                UsuarioDe(Constantes.GruposSeguridad.TIENDA_ON_LINE),
                new CambioParametroRequest { Empresa = "1", Clave = "AlmacénPedidoVta", Valor = "XXX" }));
            Assert.AreEqual(0, parametrosAnadidos.Count);
        }

        [TestMethod]
        public async Task Cambiar_ClaveFueraDelCatalogo_SeRechaza()
        {
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.Cambiar(
                UsuarioDe(Constantes.GruposSeguridad.TIENDA_ON_LINE),
                new CambioParametroRequest { Empresa = "1", Clave = "PermitirOmitirValidacion", Valor = "1" }));
        }

        [TestMethod]
        public async Task Cambiar_UsuarioSinElGrupo_SeRechaza()
        {
            ConParametros(Parametro("AlmacénPedidoVta", "AMZ"));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.Cambiar(
                UsuarioDe(Constantes.GruposSeguridad.COMPRAS),
                new CambioParametroRequest { Empresa = "1", Clave = "AlmacénPedidoVta", Valor = "ALG" }));
            Assert.AreEqual(0, parametrosAnadidos.Count);
        }
    }
}
