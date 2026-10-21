using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Transactions;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#687 (sugerencia 579, Marta): POST api/PedidosVenta/UnirPedidos corre en un TransactionScope y lo que abre su
    /// propia conexión por el camino (la sombra del modo de servicio, el stock de la fecha de entrega a la agencia, el enlace
    /// de las sugerencias) no debe alistarse: con una segunda conexión la transacción escala a distribuida, MSDTC está
    /// deshabilitado en el servidor y la unión falla entera.
    /// </summary>
    [TestClass]
    public class Issue687TransaccionAmbienteTests
    {
        private const string CADENA_LOCALDB = @"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;Connect Timeout=30";

        [TestMethod]
        public void RepositorioSombra_LaCadenaLlevaEnlistFalse_AunqueLaDeOrigenDigaTrue()
        {
            var repositorio = new RepositorioSombraModoServicioSql("Data Source=servidor;Initial Catalog=NV;Integrated Security=True;Enlist=True;Application Name=NestoAPI-ModoServicioSombra");

            var cadena = new SqlConnectionStringBuilder(repositorio.CadenaConexion);
            Assert.IsFalse(cadena.Enlist);
            Assert.AreEqual("NestoAPI-ModoServicioSombra", cadena.ApplicationName);
            Assert.AreEqual("NV", cadena.InitialCatalog);
        }

        /// <summary>
        /// Regresión con una BD de verdad (LocalDB de Visual Studio; sin ella, Inconclusive): dentro de un TransactionScope con
        /// la conexión del pedido abierta (como la de EF en PutPedidoVenta), la sombra abre la suya con otra cadena. Sin el
        /// arreglo, la transacción se promociona a distribuida (con MSDTC deshabilitado, excepción 0x8004D024; con él
        /// habilitado, DistributedIdentifier distinto de vacío): rojo en los dos casos.
        /// </summary>
        [TestMethod]
        public void RepositorioSombra_DentroDeUnTransactionScope_NoPromocionaLaTransaccion()
        {
            ComprobarLocalDb();
            var repositorio = new RepositorioSombraModoServicioSql(new SqlConnectionStringBuilder(CADENA_LOCALDB)
            {
                ApplicationName = "NestoAPI-ModoServicioSombra"
            }.ConnectionString);

            using (var transaccion = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
            using (var conexionDelPedido = new SqlConnection(CADENA_LOCALDB))
            {
                conexionDelPedido.Open(); // se alista: es la transacción de la unión

                bool existe = repositorio.ExisteTabla(); // la sombra abre la suya mientras la del pedido sigue abierta

                Assert.IsFalse(existe); // en master no está ModoServicioSombra: lo que importa es que no haya reventado
                Assert.AreEqual(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier,
                    "La conexión de la sombra se ha alistado y ha promocionado la transacción a distribuida");
                Assert.AreEqual(TransactionStatus.Active, Transaction.Current.TransactionInformation.Status);
                transaccion.Complete();
            }
        }

        [TestMethod]
        public async Task EnlazarSugerencias_DentroDeUnTransactionScope_SeHaceSinTransaccionYLaDeFueraSigueViva()
        {
            var pedido = new PedidoVentaDTO { numero = 928235, IdSesionSugerencias = Guid.NewGuid() };
            bool enlazado = false;
            Transaction dentro = null;
            Exception registrada = null;

            using (var transaccion = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
            {
                Transaction fuera = Transaction.Current;
                await ServicioSugerenciasProductos.EnlazarPedidoSinFallar(pedido, async p =>
                {
                    await Task.Yield();
                    dentro = Transaction.Current;
                    enlazado = true;
                }, ex => registrada = ex);

                Assert.IsTrue(enlazado);
                Assert.IsNull(dentro, "El enlace de las sugerencias se ha hecho dentro de la transacción del pedido");
                Assert.AreSame(fuera, Transaction.Current);
                Assert.AreEqual(TransactionStatus.Active, Transaction.Current.TransactionInformation.Status);
                transaccion.Complete();
            }
            Assert.IsNull(registrada);
        }

        [TestMethod]
        public async Task EnlazarSugerencias_SiFalla_NoRompeElGuardadoNiLaTransaccion()
        {
            var pedido = new PedidoVentaDTO { numero = 928235, IdSesionSugerencias = Guid.NewGuid() };
            Exception registrada = null;

            using (var transaccion = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
            {
                await ServicioSugerenciasProductos.EnlazarPedidoSinFallar(pedido,
                    p => throw new InvalidOperationException("El administrador de transacciones ha deshabilitado su soporte"),
                    ex => registrada = ex);

                Assert.AreEqual(TransactionStatus.Active, Transaction.Current.TransactionInformation.Status);
                transaccion.Complete();
            }
            Assert.IsNotNull(registrada);
            StringAssert.Contains(registrada.Message, "928235");
        }

        [TestMethod]
        public async Task EnlazarSugerencias_SinSesion_NoHaceNada()
        {
            bool llamado = false;
            await ServicioSugerenciasProductos.EnlazarPedidoSinFallar(new PedidoVentaDTO { numero = 1 }, p =>
            {
                llamado = true;
                return Task.CompletedTask;
            }, ex => { });

            Assert.IsFalse(llamado);
        }

        private static void ComprobarLocalDb()
        {
            try
            {
                using (var conexion = new SqlConnection(CADENA_LOCALDB))
                {
                    conexion.Open();
                }
            }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("Sin LocalDB ((localdb)\\MSSQLLocalDB) no se puede comprobar la promoción a distribuida: " + ex.Message);
            }
        }
    }
}
