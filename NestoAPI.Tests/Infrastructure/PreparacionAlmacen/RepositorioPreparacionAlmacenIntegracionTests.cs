using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
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
        private static DbContext Abrir()
        {
            return BaseDeDatosDeIntegracion.AbrirSoloSql();
        }

        // Un picking servido ya no sale (solo las líneas en Estado 1): se coge uno en curso de Algete con varios pedidos
        private static async Task<int> PickingEnCurso(RepositorioPreparacionAlmacen repositorio)
        {
            List<PickingEnCursoDTO> enCurso = await repositorio.LeerPickingsEnCurso(EMPRESA, "ALG");
            PickingEnCursoDTO conVarios = enCurso.FirstOrDefault(p => p.Pedidos > 1);
            if (conVarios == null)
            {
                Assert.Inconclusive("No hay ningún picking en curso con varios pedidos en Algete.");
            }
            return conVarios.Picking;
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeElPickingYLoOrdena()
        {
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioPreparacionAlmacen(contexto.Database);
                int PICKING = await PickingEnCurso(repositorio);

                List<LineaPickingAlmacenDTO> lineas = CasadorEscaneos.OrdenarRecorrido(
                    await repositorio.LeerLineasPicking(EMPRESA, PICKING));

                Assert.IsTrue(lineas.Count > 0);
                Assert.IsTrue(lineas.All(l => l.Cantidad > 0), "Las reservas de ubicación van en negativo: aquí salen en positivo");
                Assert.IsTrue(lineas.All(l => !string.IsNullOrEmpty(l.Producto)));
                Assert.AreEqual(1, lineas.First().Orden);

                // Siempre hay algún picking sacado sin servir en Algete
                List<PickingEnCursoDTO> enCurso = await repositorio.LeerPickingsEnCurso(EMPRESA, "ALG");
                Assert.IsTrue(enCurso.Count > 0 && enCurso.All(p => p.Picking > 0 && p.Lineas > 0));
                _ = await repositorio.LeerLecturasDelPicking(EMPRESA, PICKING);
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeElPackingElPickingDelPedidoYLaEvidencia()
        {
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioPreparacionAlmacen(contexto.Database);
                int PICKING = await PickingEnCurso(repositorio);

                List<FilaPackingAlmacen> todo = await repositorio.LeerLineasPacking(EMPRESA, PICKING, null);
                // El pedido se coge del propio picking: los datos de producción se mueven (el 30/09
                // el pedido que había aquí a fuego dejó de estar en este picking)
                int PEDIDO = todo.GroupBy(f => f.Pedido).OrderBy(g => g.Count()).First().Key;
                List<FilaPackingAlmacen> delPedido = await repositorio.LeerLineasPacking(EMPRESA, PICKING, PEDIDO);
                PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, todo);

                Assert.IsTrue(todo.Count > delPedido.Count && delPedido.Count > 0);
                Assert.IsTrue(delPedido.All(f => f.Pedido == PEDIDO));
                Assert.IsTrue(packing.Entregas.Count > 1);

                Assert.IsTrue(await repositorio.ExistePedidoEnPicking(EMPRESA, PEDIDO, PICKING));
                Assert.IsFalse(await repositorio.ExistePedidoEnPicking(EMPRESA, PEDIDO, 1));
                Assert.AreEqual(PICKING, await repositorio.PickingEnCursoDelPedido(EMPRESA, PEDIDO));

                // Las tablas nuevas: que las consultas casen con los DTO aunque no haya filas
                _ = await repositorio.LeerLecturas(EMPRESA, PEDIDO, PICKING, CasadorEscaneos.FASE_PACKING);
                _ = await repositorio.LeerBultos(EMPRESA, PEDIDO);
                Assert.IsNull(await repositorio.LeerBulto(-1));
                Assert.IsNull(await repositorio.LeerBultoPorIdCliente(Guid.NewGuid()));
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeLoPendienteDeUbicarYDondeEstaUnProducto()
        {
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioUbicacionesAlmacen(contexto.Database);

                List<FilaPendienteDeUbicar> pendiente = await repositorio.LeerPendienteDeUbicar(EMPRESA, "ALG");
                List<FilaUbicacionProducto> huecos = await repositorio.LeerHuecosDeLoPendiente(EMPRESA, "ALG");
                List<FilaUbicacionProducto> ultimos = await repositorio.LeerUltimoHuecoDeLoPendiente(EMPRESA, "ALG");
                Assert.IsTrue(ultimos.All(u => u.Cantidad == 0 && !string.IsNullOrWhiteSpace(u.Pasillo)));
                List<ProductoPendienteDeUbicarDTO> productos = ServicioUbicacionesAlmacen.MontarPendiente(pendiente, huecos);

                // En Algete siempre hay mercancía recibida por ubicar
                Assert.IsTrue(productos.Count > 0);
                Assert.IsTrue(productos.All(p => p.Cantidad > 0));
                Assert.IsTrue(huecos.All(h => h.Estado == 0));

                // Un producto que seguro que existe: el primero de lo pendiente, buscado por su número
                string numero = productos.First().Producto;
                List<FilaProductoAlmacen> encontrados = await repositorio.BuscarProductos(EMPRESA, numero);
                Assert.AreEqual(numero, encontrados.Single().Producto);
                List<FilaUbicacionProducto> ubicaciones = await repositorio.LeerUbicacionesDelProducto(EMPRESA, "ALG", numero);
                Assert.IsTrue(ubicaciones.Any(u => u.Estado == 2));
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeLosPedidosDeCompraPendientesYSusLineas()
        {
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioRecepcionCompras(contexto.Database);

                List<PedidoCompraPendienteDTO> pedidos = await repositorio.LeerPedidosPendientes(EMPRESA, "ALG");

                // Siempre hay pedidos de compra por recibir en Algete
                Assert.IsTrue(pedidos.Count > 0);
                Assert.IsTrue(pedidos.All(p => p.Lineas > 0 && p.Unidades > 0));

                List<FilaRecepcionCompra> lineas = await repositorio.LeerLineasPendientes(EMPRESA, pedidos.First().Pedido);
                Assert.AreEqual(pedidos.First().Lineas, lineas.Count);
                Assert.IsTrue(lineas.All(l => !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0));
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_RecepcionPorProveedor_LasConsultasCasanConLosDTO()
        {
            // NestoAPI#559: solo lectura. La de LeerLineasBloqueando se lanza dentro de una transacción que se deshace
            // (el UPDLOCK no deja nada bloqueado al salir).
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioRecepcionCompras(contexto.Database);
                PedidoCompraPendienteDTO pedido = (await repositorio.LeerPedidosPendientes(EMPRESA, "ALG")).First();

                List<FilaRecepcionCompra> delProveedor = await repositorio.LeerLineasPendientesProveedor(EMPRESA, "ALG", pedido.Proveedor);
                Assert.IsTrue(delProveedor.Any(l => l.Pedido == pedido.Pedido));
                Assert.IsTrue(delProveedor.All(l => l.Proveedor == pedido.Proveedor && l.Cantidad > 0));

                using (DbContextTransaction transaccion = contexto.Database.BeginTransaction())
                {
                    List<LineaCompraPendiente> lineas = await contexto.Database.SqlQuery<LineaCompraPendiente>(
                        TransaccionRecepcionComprasSql.SQL_LINEAS_BLOQUEANDO, EMPRESA, "ALG", pedido.Proveedor).ToListAsync();
                    transaccion.Rollback();

                    Assert.AreEqual(delProveedor.Count, lineas.Count);
                    Assert.IsTrue(lineas.All(l => l.VistoBueno && l.Precio >= 0 && l.FechaPedido.Year > 2000));
                }
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeLasReposicionesPendientesDeCadaAlmacen()
        {
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioRecepcionReposiciones(contexto.Database);

                // Puede no haber ninguna de camino: lo que se comprueba es que las consultas casan con los DTO
                foreach (string almacen in new[] { "ALG", "REI", "ALC" })
                {
                    List<ReposicionPendienteDTO> pendientes = await repositorio.LeerPendientes(EMPRESA, almacen);
                    Assert.IsTrue(pendientes.All(p => p.Traspaso > 0 && p.Lineas > 0));
                    if (pendientes.Any())
                    {
                        List<FilaReposicion> lineas = await repositorio.LeerLineas(EMPRESA, almacen, pendientes.First().Traspaso);
                        Assert.IsTrue(lineas.All(l => !string.IsNullOrWhiteSpace(l.Producto)));
                    }
                }
                Assert.AreEqual(0, (await repositorio.LeerLineas(EMPRESA, "ALG", -1)).Count);
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_LeeLasReposicionesPorSalirYSuRecorrido()
        {
            // NestoAPI#556: la salida de un traspaso (diario de salida del origen) y sus huecos reservados (estado 4).
            // El diario «General» de Algete solo tiene filas mientras un traspaso está sin contabilizar: puede salir
            // vacío; lo que se comprueba es que las consultas casan con la base de datos y con los DTO.
            using (DbContext contexto = Abrir())
            {
                var repositorio = new RepositorioPreparacionAlmacen(contexto.Database);

                List<ReposicionPorSalir> porSalir = await repositorio.LeerReposicionesPorSalir(EMPRESA, "ALG");
                Assert.IsTrue(porSalir.All(r => r.Traspaso > 0 && r.Lineas > 0 && r.Unidades > 0));
                foreach (ReposicionPorSalir reposicion in porSalir.Take(2))
                {
                    ReposicionSalida salida = await repositorio.LeerReposicionSalida(EMPRESA, reposicion.Traspaso);
                    Assert.IsNotNull(salida);
                    Assert.IsTrue(salida.Lineas.All(l => l.Cantidad > 0 && !string.IsNullOrEmpty(l.Producto)));
                }
                // Un traspaso ya contabilizado (02/10/26, ALG → REI): no queda nada por sacar
                Assert.IsNull(await repositorio.LeerReposicionSalida(EMPRESA, 80872));
                _ = await repositorio.LeerLecturasDeSalida(EMPRESA, "REPO", 80872);
                _ = await repositorio.LeerLecturasDeSalida(EMPRESA, "PICK", 99633);
            }
        }
    }
}
