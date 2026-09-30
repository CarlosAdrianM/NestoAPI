using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: las LECTURAS del repositorio contra una base de datos de verdad. Las consultas
    /// van por SQL directo y los tests normales usan un repositorio falso, así que un nombre de
    /// columna o un tipo que no case con el DTO solo se vería al llamar a la API.
    ///
    /// <para>Solo corre si la variable de entorno NESTO_TEST_BD trae una cadena de conexión; en la
    /// suite normal sale como no concluyente. No escribe nada.</para>
    /// </summary>
    [TestClass]
    public class RepositorioPreparacionAlmacenIntegracionTests
    {
        private const string EMPRESA = "1";
        // Un picking ya servido del 24/09/26: 26 paradas, 7 pedidos
        private const int PICKING = 99633;
        private const int PEDIDO = 926940;

        // El app.config de los tests no declara el proveedor de SQL Server: se registra aquí, solo
        // para este contexto. Como la configuración de EF es única por proceso, estas pruebas se
        // lanzan solas (con el filtro «Integracion»), no mezcladas con el resto de la suite.
        private class ConfiguracionSqlServer : DbConfiguration
        {
            public ConfiguracionSqlServer()
            {
                // Por reflexión, para no añadir la referencia a EntityFramework.SqlServer al proyecto
                // de tests solo por esto: se carga el que ya compila NestoAPI
                Type proveedor = ensambladoProveedor.GetType("System.Data.Entity.SqlServer.SqlProviderServices", true);
                SetProviderServices("System.Data.SqlClient",
                    (System.Data.Entity.Core.Common.DbProviderServices)proveedor.GetProperty("Instance").GetValue(null));
            }
        }

        private static System.Reflection.Assembly ensambladoProveedor;

        private static bool CargarProveedor()
        {
            if (ensambladoProveedor != null)
            {
                return true;
            }
            string carpeta = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string ruta in new[]
            {
                System.IO.Path.Combine(carpeta, "EntityFramework.SqlServer.dll"),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(carpeta, "..", "..", "..", "NestoAPI", "bin", "EntityFramework.SqlServer.dll"))
            })
            {
                if (System.IO.File.Exists(ruta))
                {
                    ensambladoProveedor = System.Reflection.Assembly.LoadFrom(ruta);
                    return true;
                }
            }
            return false;
        }

        [DbConfigurationType(typeof(ConfiguracionSqlServer))]
        private class ContextoSoloSql : DbContext
        {
            static ContextoSoloSql()
            {
                Database.SetInitializer<ContextoSoloSql>(null);
            }

            public ContextoSoloSql(string conexion) : base(new SqlConnection(conexion), true)
            {
            }
        }

        private static ContextoSoloSql Abrir()
        {
            string conexion = Environment.GetEnvironmentVariable("NESTO_TEST_BD");
            if (string.IsNullOrWhiteSpace(conexion))
            {
                Assert.Inconclusive("Sin NESTO_TEST_BD: no se prueba contra la base de datos.");
            }
            if (!CargarProveedor())
            {
                Assert.Inconclusive("No se encuentra EntityFramework.SqlServer.dll (hay que compilar NestoAPI antes).");
            }
            return new ContextoSoloSql(conexion);
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeElPickingYLoOrdena()
        {
            using (ContextoSoloSql contexto = Abrir())
            {
                var repositorio = new RepositorioPreparacionAlmacen(contexto.Database);

                List<LineaPickingAlmacenDTO> lineas = CasadorEscaneos.OrdenarRecorrido(
                    await repositorio.LeerLineasPicking(EMPRESA, PICKING));

                Assert.IsTrue(lineas.Count > 0);
                Assert.IsTrue(lineas.All(l => l.Cantidad > 0), "Las reservas de ubicación van en negativo: aquí salen en positivo");
                Assert.IsTrue(lineas.All(l => !string.IsNullOrEmpty(l.Producto)));
                Assert.AreEqual(1, lineas.First().Orden);
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeElPackingElPickingDelPedidoYLaEvidencia()
        {
            using (ContextoSoloSql contexto = Abrir())
            {
                var repositorio = new RepositorioPreparacionAlmacen(contexto.Database);

                List<FilaPackingAlmacen> todo = await repositorio.LeerLineasPacking(EMPRESA, PICKING, null);
                List<FilaPackingAlmacen> delPedido = await repositorio.LeerLineasPacking(EMPRESA, PICKING, PEDIDO);
                PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, todo);

                Assert.IsTrue(todo.Count > delPedido.Count && delPedido.Count > 0);
                Assert.IsTrue(delPedido.All(f => f.Pedido == PEDIDO));
                Assert.IsTrue(packing.Entregas.Count > 1);

                Assert.IsTrue(await repositorio.ExistePedidoEnPicking(EMPRESA, PEDIDO, PICKING));
                Assert.IsFalse(await repositorio.ExistePedidoEnPicking(EMPRESA, PEDIDO, 1));
                // Ya está servido: no tiene picking en curso, y eso es un null, no un error
                _ = await repositorio.PickingEnCursoDelPedido(EMPRESA, PEDIDO);

                // Las tablas nuevas: que las consultas casen con los DTO aunque no haya filas
                _ = await repositorio.LeerLecturas(EMPRESA, PEDIDO, PICKING, CasadorEscaneos.FASE_PACKING);
                _ = await repositorio.LeerBultos(EMPRESA, PEDIDO);
                Assert.IsNull(await repositorio.LeerBulto(-1));
                Assert.IsNull(await repositorio.LeerBultoPorIdCliente(Guid.NewGuid()));
            }
        }
    }
}
