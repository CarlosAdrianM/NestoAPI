using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#476: el 40445 (Zulay) se anuló con productos pendientes de servir y nadie lo
    /// impidió. Ahora anular un cliente pasa por un único punto con comprobaciones enchufables:
    /// sin productos pendientes y sin deuda, y se devuelven TODOS los motivos.
    /// </summary>
    [TestClass]
    public class GuardiaAnulacionClienteTests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "40445";

        private NVEntities db;
        private DbSet<LinPedidoVta> fakeLineas;
        private DbSet<ExtractoCliente> fakeExtracto;
        private DbSet<Cliente> fakeClientes;
        private DbSet<VendedorClienteGrupoProducto> fakeVendedoresGrupo;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            fakeExtracto = A.Fake<DbSet<ExtractoCliente>>(o => o.Implements<IQueryable<ExtractoCliente>>().Implements<IDbAsyncEnumerable<ExtractoCliente>>());
            fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            fakeVendedoresGrupo = A.Fake<DbSet<VendedorClienteGrupoProducto>>(o => o.Implements<IQueryable<VendedorClienteGrupoProducto>>().Implements<IDbAsyncEnumerable<VendedorClienteGrupoProducto>>());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtracto);
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(fakeVendedoresGrupo);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            Lineas();
            Extracto();
            ConfigurarFakeDbSet(fakeVendedoresGrupo, new List<VendedorClienteGrupoProducto>().AsQueryable());
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>
            {
                new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = 0, Vendedor = "NV", ClientePrincipal = true }
            }.AsQueryable());
        }

        private void Lineas(params LinPedidoVta[] lineas)
        {
            ConfigurarFakeDbSet(fakeLineas, lineas.ToList().AsQueryable());
        }

        private void Extracto(params ExtractoCliente[] apuntes)
        {
            ConfigurarFakeDbSet(fakeExtracto, apuntes.ToList().AsQueryable());
        }

        private static LinPedidoVta Linea(int pedido, short estado, string cliente = CLIENTE)
        {
            return new LinPedidoVta { Empresa = EMPRESA, Nº_Cliente = cliente, Número = pedido, Estado = estado };
        }

        private static ExtractoCliente Apunte(decimal pendiente, string cliente = CLIENTE)
        {
            return new ExtractoCliente { Empresa = EMPRESA, Número = cliente, Contacto = "0", ImportePdte = pendiente };
        }

        [TestMethod]
        public void EsAnulacion_SoloCuandoSePasaDeVivoANegativo()
        {
            Assert.IsTrue(GuardiaAnulacionCliente.EsAnulacion(-1, 0));
            Assert.IsTrue(GuardiaAnulacionCliente.EsAnulacion(-1, 5), "también desde primera visita");
            Assert.IsFalse(GuardiaAnulacionCliente.EsAnulacion(0, -1), "reactivar no es anular");
            Assert.IsFalse(GuardiaAnulacionCliente.EsAnulacion(-1, -1), "ya estaba anulado");
            Assert.IsFalse(GuardiaAnulacionCliente.EsAnulacion(9, 0), "cambiar entre estados vivos");
            Assert.IsFalse(GuardiaAnulacionCliente.EsAnulacion(null, 0), "null = no tocar el estado");
        }

        [TestMethod]
        public async Task ClienteLimpio_SePuedeAnular()
        {
            List<string> motivos = await GuardiaAnulacionCliente.MotivosParaNoAnular(db, EMPRESA, CLIENTE);

            Assert.AreEqual(0, motivos.Count);
        }

        [TestMethod]
        public async Task ConProductosPendientes_NoSePuedeAnular_YDiceLosPedidos()
        {
            // 925758 pendiente (-1) y en curso (1); 900001 ya en albarán (2) no cuenta; otro cliente no cuenta.
            Lineas(Linea(925758, -1), Linea(925758, 1), Linea(925900, -1), Linea(900001, 2), Linea(777777, -1, "99999"));

            List<string> motivos = await GuardiaAnulacionCliente.MotivosParaNoAnular(db, EMPRESA, CLIENTE);

            Assert.AreEqual(1, motivos.Count);
            StringAssert.Contains(motivos[0], "pendientes de servir");
            StringAssert.Contains(motivos[0], "925758, 925900");
            Assert.IsFalse(motivos[0].Contains("900001"), "las líneas ya servidas no bloquean");
        }

        [TestMethod]
        public async Task ConDeuda_NoSePuedeAnular_YDiceElImporte()
        {
            Extracto(Apunte(120.50m), Apunte(-20.50m), Apunte(0m), Apunte(999m, "99999"));

            List<string> motivos = await GuardiaAnulacionCliente.MotivosParaNoAnular(db, EMPRESA, CLIENTE);

            Assert.AreEqual(1, motivos.Count);
            StringAssert.Contains(motivos[0], "100,00");
            StringAssert.Contains(motivos[0], "2 apuntes");
        }

        [TestMethod]
        public async Task ConPendientesYDeuda_LosDosMotivosDeUnaVez()
        {
            Lineas(Linea(925758, -1));
            Extracto(Apunte(50m));

            List<string> motivos = await GuardiaAnulacionCliente.MotivosParaNoAnular(db, EMPRESA, CLIENTE);

            Assert.AreEqual(2, motivos.Count, "el usuario quiere saber las dos cosas de una vez");
            await Assert.ThrowsExceptionAsync<ValidationException>(() => GuardiaAnulacionCliente.ExigirAnulable(db, EMPRESA, CLIENTE));
        }

        [TestMethod]
        public async Task PutClienteComercial_AnularConPendientes_Devuelve400ConElMotivo()
        {
            Lineas(Linea(925758, -1));
            var controller = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, db);
            var dto = new ClienteDTO { empresa = EMPRESA, cliente = CLIENTE, contacto = "0", estado = -1, vendedor = "NV", usuario = "alfredo" };

            var resultado = await controller.PutCliente(dto) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado, "antes se anulaba sin más (40445)");
            StringAssert.Contains(resultado.Message, "925758");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
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
