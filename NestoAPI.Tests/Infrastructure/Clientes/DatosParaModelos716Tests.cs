using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#716 (2.2 y 2.3): motivo al cambiar el estado del cliente (catálogo, DTO opcional, columnas que pueden no
    /// existir) y datos del alta (aplicación deducida del token, columnas que pueden no existir). Sin base de datos.
    /// </summary>
    [TestClass]
    public class DatosParaModelos716Tests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "40445";

        [TestCleanup]
        public void Limpiar()
        {
            ColumnasOpcionales.ForzarParaTests = null;
        }

        // ---------- 2.2 catálogo ----------

        [TestMethod]
        public void MotivoCambioEstado_CatalogoConNombresYLongitud()
        {
            foreach (string codigo in MotivoCambioEstadoCliente.Nombres.Keys)
            {
                Assert.IsTrue(MotivoCambioEstadoCliente.EsValido(codigo), codigo);
                Assert.IsTrue(codigo.Length <= MotivoCambioEstadoCliente.LONGITUD_MAXIMA, $"{codigo} no cabe en varchar(30)");
            }
            Assert.IsTrue(MotivoCambioEstadoCliente.CodigosManuales.Contains(MotivoCambioEstadoCliente.CIERRE));
            Assert.IsFalse(MotivoCambioEstadoCliente.CodigosManuales.Contains(MotivoCambioEstadoCliente.DEJAR_DE_VISITAR), "los automáticos no se ofrecen en el desplegable");
            Assert.IsTrue(MotivoCambioEstadoCliente.EsValido(MotivoCambioEstadoCliente.DEJAR_DE_VISITAR), "pero sí son válidos para la API");
        }

        [TestMethod]
        public void MotivoCambioEstado_NormalizaYRechazaLoQueNoEsta()
        {
            Assert.AreEqual("PeticionCliente", MotivoCambioEstadoCliente.Normalizar("peticion cliente"));
            Assert.AreEqual("Reactivacion", MotivoCambioEstadoCliente.Normalizar("Reactivación"));
            Assert.IsNull(MotivoCambioEstadoCliente.Normalizar("Porque sí"));
            Assert.IsNull(MotivoCambioEstadoCliente.Normalizar(null));
        }

        [TestMethod]
        public void MotivoCambioEstado_MotivoParaNoGuardar()
        {
            Assert.IsNull(MotivoCambioEstadoCliente.MotivoParaNoGuardar(null, null), "opcional");
            Assert.IsNull(MotivoCambioEstadoCliente.MotivoParaNoGuardar("Cierre", "Ha cerrado en septiembre"));
            Assert.IsNull(MotivoCambioEstadoCliente.MotivoParaNoGuardar(null, "solo comentario, sin motivo"), "el comentario solo también vale");
            StringAssert.Contains(MotivoCambioEstadoCliente.MotivoParaNoGuardar("Inventado", null), "'Inventado'");
            StringAssert.Contains(MotivoCambioEstadoCliente.MotivoParaNoGuardar("Cierre", new string('x', 201)), "200");
        }

        // ---------- 2.2 anotación best-effort ----------

        [TestMethod]
        public void Anotar_SinLasColumnas_NoEscribeNada()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            bool anotado = RegistroMotivoCambioEstado.Anotar(A.Fake<NVEntities>(), EMPRESA, CLIENTE, "0", -1, "Cierre", "cerrado", "carlos");
            Assert.IsFalse(anotado, "el script no se ha lanzado: todo sigue igual");
        }

        [TestMethod]
        public void Anotar_SinMotivoNiComentario_NiMiraLasColumnas()
        {
            ColumnasOpcionales.ForzarParaTests = true;
            Assert.IsFalse(RegistroMotivoCambioEstado.Anotar(A.Fake<NVEntities>(), EMPRESA, CLIENTE, "0", -1, null, "   ", "carlos"));
            Assert.IsFalse(RegistroMotivoCambioEstado.Anotar(A.Fake<NVEntities>(), EMPRESA, CLIENTE, "0", -1, "Inventado", null, "carlos"), "un motivo que no casa no se guarda");
        }

        [TestMethod]
        public void AnotarVarios_SinColumnas_Cero()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            var clientes = new List<Cliente> { new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = 9 }, null };
            Assert.AreEqual(0, RegistroMotivoCambioEstado.AnotarVarios(A.Fake<NVEntities>(), clientes, "DejarDeVisitar", null, "carlos"));
        }

        [TestMethod]
        public void Recortar_RespetaElMaximoYDevuelveNullSiVacio()
        {
            Assert.IsNull(RegistroMotivoCambioEstado.Recortar("  ", 10));
            Assert.AreEqual("abc", RegistroMotivoCambioEstado.Recortar(" abc ", 10));
            Assert.AreEqual("abcdefghij", RegistroMotivoCambioEstado.Recortar("abcdefghijklm", 10));
        }

        [TestMethod]
        public void DTOs_LlevanLosCamposOpcionalesANull()
        {
            var dto = new ClienteDTO();
            Assert.IsNull(dto.motivoCambioEstado);
            Assert.IsNull(dto.comentarioCambioEstado);
            var crear = new ClienteCrear();
            Assert.IsNull(crear.MotivoCambioEstado);
            Assert.IsNull(crear.ComentarioCambioEstado);
            Assert.IsNull(crear.AplicacionAlta);
        }

        // ---------- 2.2 en el PUT de ClienteComercial ----------

        private static NVEntities DbConCliente(short estado)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<Cliente> clientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            DbSet<VendedorClienteGrupoProducto> grupos = A.Fake<DbSet<VendedorClienteGrupoProducto>>(o => o.Implements<IQueryable<VendedorClienteGrupoProducto>>().Implements<IDbAsyncEnumerable<VendedorClienteGrupoProducto>>());
            A.CallTo(() => db.Clientes).Returns(clientes);
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(grupos);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            ConfigurarFakeDbSet(grupos, new List<VendedorClienteGrupoProducto>().AsQueryable());
            ConfigurarFakeDbSet(clientes, new List<Cliente>
            {
                new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = estado, Vendedor = "NV", ClientePrincipal = true }
            }.AsQueryable());
            return db;
        }

        [TestMethod]
        public async Task PutClienteComercial_MotivoFueraDelCatalogo_400SinGuardar()
        {
            NVEntities db = DbConCliente(0);
            var controller = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, db);
            var dto = new ClienteDTO { empresa = EMPRESA, cliente = CLIENTE, contacto = "0", estado = 9, vendedor = "NV", usuario = "alfredo", motivoCambioEstado = "Porque sí" };

            var resultado = await controller.PutCliente(dto) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "'Porque sí'");
            StringAssert.Contains(resultado.Message, "Cierre, Traspaso");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        // El camino feliz del PUT (204) pasa por db.Entry(), que exige un contexto real: la parte pura (AplicarCambiosClienteComercial)
        // ya tiene sus tests y la anotación best-effort se prueba arriba (Anotar_*). Los clientes que no mandan motivo no cambian nada.

        [TestMethod]
        public async Task DejarDeVisitar_MotivoFueraDelCatalogo_400SinTocarNada()
        {
            NVEntities db = DbConCliente(0);
            IGestorClientes gestor = A.Fake<IGestorClientes>();
            var controller = new ClientesController(gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, db);

            var resultado = await controller.DejarDeVisitar(new ClienteCrear { Empresa = EMPRESA, Cliente = CLIENTE, Contacto = "0", MotivoCambioEstado = "Inventado" }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            A.CallTo(() => gestor.DejarDeVisitar(A<NVEntities>._, A<ClienteCrear>._)).MustNotHaveHappened();
        }

        // ---------- 2.3 alta ----------

        private static IPrincipal Usuario(string nombre)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre) }, "Bearer"));
        }

        [TestMethod]
        public void DeducirAplicacion_DelToken()
        {
            Assert.AreEqual("Nesto", DatosAltaCliente.DeducirAplicacion(Usuario(@"NUEVAVISION\Carlos"), null), "Nesto entra con la cuenta de Windows");
            Assert.AreEqual("TiendaOnline", DatosAltaCliente.DeducirAplicacion(Usuario("clienta@salon.es"), null), "la tienda entra con el correo");
            Assert.AreEqual("NestoApp", DatosAltaCliente.DeducirAplicacion(Usuario("alfredo"), null), "NestoApp entra con el usuario del Identity");
            Assert.AreEqual("Desconocida", DatosAltaCliente.DeducirAplicacion(null, null));
            Assert.AreEqual("Desconocida", DatosAltaCliente.DeducirAplicacion(new ClaimsPrincipal(new ClaimsIdentity()), "  "), "sin autenticar");
        }

        [TestMethod]
        public void DeducirAplicacion_LoExplicitoManda_YSeRecortaA20()
        {
            Assert.AreEqual("ImportacionMeta", DatosAltaCliente.DeducirAplicacion(Usuario(@"NUEVAVISION\Carlos"), " ImportacionMeta "));
            Assert.AreEqual(20, DatosAltaCliente.DeducirAplicacion(null, new string('a', 30)).Length);
        }

        [TestMethod]
        public void RegistrarAlta_SinLasColumnas_NoEscribeNada()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            var cliente = new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0" };
            Assert.IsFalse(DatosAltaCliente.Registrar(A.Fake<NVEntities>(), cliente, "carlos", "Nesto", DateTime.Now));
            Assert.IsFalse(DatosAltaCliente.Registrar(A.Fake<NVEntities>(), null, "carlos", "Nesto", DateTime.Now));
        }

        [TestMethod]
        public async Task PostCliente_RellenaLaAplicacionAntesDeCrear()
        {
            IGestorClientes gestor = A.Fake<IGestorClientes>();
            ClienteCrear recibido = null;
            A.CallTo(() => gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._))
                .Invokes((ClienteCrear c, NVEntities d) => recibido = c)
                .Returns(Task.FromResult(new Cliente { Empresa = EMPRESA, Nº_Cliente = "99999", Contacto = "0" }));
            var controller = new ClientesController(gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, A.Fake<NVEntities>())
            {
                User = Usuario(@"NUEVAVISION\Alfredo")
            };

            _ = await controller.PostCliente(new ClienteCrear { Nombre = "SALÓN NUEVO", Usuario = "alfredo" });

            Assert.IsNotNull(recibido);
            Assert.AreEqual("Nesto", recibido.AplicacionAlta, "el gestor recibe la aplicación ya deducida y la guarda en Clientes.AplicacionAlta");
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
