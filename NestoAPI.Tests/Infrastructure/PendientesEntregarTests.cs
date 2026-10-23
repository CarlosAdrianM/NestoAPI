using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#552 (sugerencia 717, Manuel): listado de las líneas de producto pendientes de entregar de todos los
    /// almacenes (estado -1 o 1; sin presupuestos, albaranes ni facturas).
    /// </summary>
    [TestClass]
    public class PendientesEntregarTests
    {
        private List<CabPedidoVta> cabeceras;
        private List<LinPedidoVta> lineas;
        private List<Cliente> clientes;
        private ServicioPendientesEntregar servicio;

        [TestInitialize]
        public void Setup()
        {
            cabeceras = new List<CabPedidoVta>();
            lineas = new List<LinPedidoVta>();
            clientes = new List<Cliente>();
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.CabPedidoVtas).Returns(DbSetCon(cabeceras));
            A.CallTo(() => db.LinPedidoVtas).Returns(DbSetCon(lineas));
            A.CallTo(() => db.Clientes).Returns(DbSetCon(clientes));
            servicio = new ServicioPendientesEntregar(db);
        }

        private void Pedido(int numero, string vendedor, string cliente = "1000", string empresa = "1")
        {
            cabeceras.Add(new CabPedidoVta { Empresa = empresa, Número = numero, Nº_Cliente = cliente, Contacto = "0", Vendedor = vendedor });
        }

        private LinPedidoVta Linea(int pedido, int orden, short estado, string producto = "45958", string almacen = "ALG",
            byte tipoLinea = 1, DateTime? fecha = null, string cliente = "1000", string empresa = "1")
        {
            var linea = new LinPedidoVta
            {
                Empresa = empresa, Número = pedido, Nº_Orden = orden, Nº_Cliente = cliente, Contacto = "0", Estado = estado,
                Producto = producto, Almacén = almacen, TipoLinea = tipoLinea, Fecha_Entrega = fecha ?? new DateTime(2026, 10, 23),
                Texto = "ROLLO CAMILLA", Cantidad = 2, Bruto = 10.5M, Usuario = "NUEVAVISION\\Manuel"
            };
            lineas.Add(linea);
            return linea;
        }

        [TestMethod]
        public async Task Listar_SoloLineasDeProductoPendientesOEnCurso()
        {
            Pedido(1, "ASH");
            Linea(1, 1, -1);
            Linea(1, 2, 1);
            Linea(1, 3, -3);            // presupuesto
            Linea(1, 4, 2);             // albarán
            Linea(1, 5, 4);             // factura
            Linea(1, 6, -2);            // nota de entrega
            Linea(1, 7, 1, tipoLinea: 0); // texto
            Linea(1, 8, -1, tipoLinea: 2); // cuenta contable

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar("1");

            CollectionAssert.AreEqual(new[] { 1, 2 }, filas.Select(f => f.Linea).ToArray());
        }

        [TestMethod]
        public async Task Listar_NoTraeLosDeOtraEmpresa()
        {
            Pedido(1, "ASH");
            Pedido(1, "ASH", empresa: "3");
            Linea(1, 1, -1);
            Linea(1, 2, -1, empresa: "3");

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar("1");

            Assert.AreEqual(1, filas.Count);
            Assert.AreEqual(1, filas[0].Linea);
        }

        [TestMethod]
        public async Task Listar_RellenaColumnasRecortadasYNombreDelCliente()
        {
            cabeceras.Add(new CabPedidoVta { Empresa = "1", Número = 928000, Nº_Cliente = "1000      ", Contacto = "0  ", Vendedor = "ASH " });
            clientes.Add(new Cliente { Empresa = "1", Nº_Cliente = "1000      ", Contacto = "0  ", Nombre = "PELUQUERÍA MANUELA   " });
            lineas.Add(new LinPedidoVta
            {
                Empresa = "1", Número = 928000, Nº_Orden = 3, Nº_Cliente = "1000      ", Contacto = "0  ", Estado = -1, TipoLinea = 1,
                Producto = "45958          ", Almacén = "REI", Fecha_Entrega = new DateTime(2026, 10, 24), Texto = "ROLLO CAMILLA   ",
                Cantidad = 3, Bruto = 31.5M, Usuario = "NUEVAVISION\\Manuel   "
            });

            LineaPendienteEntregarDTO fila = (await servicio.Listar("1")).Single();

            Assert.AreEqual(928000, fila.Pedido);
            Assert.AreEqual("1000", fila.Cliente);
            Assert.AreEqual("0", fila.Contacto);
            Assert.AreEqual("PELUQUERÍA MANUELA", fila.NombreCliente);
            Assert.AreEqual("ASH", fila.Vendedor);
            Assert.AreEqual("REI", fila.Almacen);
            Assert.AreEqual(new DateTime(2026, 10, 24), fila.FechaEntrega);
            Assert.AreEqual("45958", fila.Producto);
            Assert.AreEqual("ROLLO CAMILLA", fila.Texto);
            Assert.AreEqual((short)3, fila.Cantidad);
            Assert.AreEqual(31.5M, fila.Bruto);
            Assert.AreEqual("NUEVAVISION\\Manuel", fila.Usuario);
        }

        [TestMethod]
        public async Task Listar_SinClienteEnLaFichaSaleIgualSinNombre()
        {
            Pedido(1, "ASH");
            Linea(1, 1, -1);

            LineaPendienteEntregarDTO fila = (await servicio.Listar("1")).Single();

            Assert.IsNull(fila.NombreCliente);
        }

        [TestMethod]
        public async Task Listar_FiltraPorAlmacen()
        {
            Pedido(1, "ASH");
            Linea(1, 1, -1, almacen: "ALG");
            Linea(1, 2, -1, almacen: "REI");

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar("1", almacen: "REI");

            Assert.AreEqual(2, filas.Single().Linea);
        }

        [TestMethod]
        public async Task Listar_FiltraPorProducto()
        {
            Pedido(1, "ASH");
            Linea(1, 1, -1, producto: "45958");
            Linea(1, 2, -1, producto: "17404");

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar("1", producto: " 17404 ");

            Assert.AreEqual(2, filas.Single().Linea);
        }

        [TestMethod]
        public async Task Listar_FiltraPorVendedorDelPedido()
        {
            Pedido(1, "ASH");
            Pedido(2, "JE");
            Linea(1, 1, -1);
            Linea(2, 1, -1);

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar("1", vendedor: "JE");

            Assert.AreEqual(2, filas.Single().Pedido);
        }

        [TestMethod]
        public async Task Listar_FiltrosVaciosNoFiltran()
        {
            Pedido(1, "ASH");
            Linea(1, 1, -1, almacen: "ALG");
            Linea(1, 2, 1, almacen: "REI");

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar(null, "", " ", null);

            Assert.AreEqual(2, filas.Count);
        }

        [TestMethod]
        public async Task Listar_OrdenaPorFechaDeEntregaYPedido()
        {
            Pedido(1, "ASH");
            Pedido(2, "ASH");
            Linea(2, 1, -1, fecha: new DateTime(2026, 10, 25));
            Linea(2, 2, -1, fecha: new DateTime(2026, 10, 20));
            Linea(1, 1, -1, fecha: new DateTime(2026, 10, 25));

            List<LineaPendienteEntregarDTO> filas = await servicio.Listar("1");

            CollectionAssert.AreEqual(new[] { "2-2", "1-1", "2-1" }, filas.Select(f => f.Pedido + "-" + f.Linea).ToArray());
        }

        [TestMethod]
        public void Excel_EsUnXlsxConNombreDelDia()
        {
            var filas = new List<LineaPendienteEntregarDTO>
            {
                new LineaPendienteEntregarDTO { Pedido = 928000, Cliente = "1000", NombreCliente = "PELUQUERÍA", FechaEntrega = new DateTime(2026, 10, 24), Producto = "45958", Cantidad = 2, Bruto = 21M }
            };

            var contenido = new GeneradorExcelPendientesEntregar().GenerarExcel(filas, new DateTime(2026, 10, 23));
            byte[] bytes = contenido.ReadAsByteArrayAsync().Result;

            Assert.AreEqual(GeneradorExcelPendientesEntregar.CONTENT_TYPE, contenido.Headers.ContentType.MediaType);
            Assert.AreEqual("PendientesEntregar_20261023.xlsx", contenido.Headers.ContentDisposition.FileName);
            Assert.AreEqual((byte)'P', bytes[0], "Un .xlsx es un zip (empieza por PK)");
            Assert.AreEqual((byte)'K', bytes[1]);
        }

        private static DbSet<T> DbSetCon<T>(List<T> datos) where T : class
        {
            var fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            return fake;
        }
    }
}
