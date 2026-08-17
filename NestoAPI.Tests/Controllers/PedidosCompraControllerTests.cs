using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models;
using NestoAPI.Models.PedidosCompra;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PedidosCompraControllerTests
    {
        private NVEntities db;
        private PedidosCompraController controller;
        private DbSet<CabFacturaCmp> fakeCabFacturasCmp;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeCabFacturasCmp = A.Fake<DbSet<CabFacturaCmp>>(o => o.Implements<IQueryable<CabFacturaCmp>>().Implements<IDbAsyncEnumerable<CabFacturaCmp>>());
            A.CallTo(() => db.CabFacturasCmp).Returns(fakeCabFacturasCmp);
            ConfigurarFakeDbSet(fakeCabFacturasCmp, new List<CabFacturaCmp>().AsQueryable());
            controller = new PedidosCompraController(db);
        }

        [TestMethod]
        public async Task GetFacturasContabilizadasProveedor_FiltraProveedorYRangoFechas()
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            var facturas = new List<CabFacturaCmp>
            {
                // Dentro del rango y proveedor correcto -> sí
                new CabFacturaCmp { Empresa = empresa, Número = "1", NºProveedor = "999", Fecha = new DateTime(2026, 2, 10), NºDocumentoProv = "INV-ES-2026-0001" },
                // Dentro del rango pero otro proveedor -> no
                new CabFacturaCmp { Empresa = empresa, Número = "2", NºProveedor = "888", Fecha = new DateTime(2026, 2, 10), NºDocumentoProv = "OTRO" },
                // Otro proveedor, fuera de rango -> no
                new CabFacturaCmp { Empresa = empresa, Número = "3", NºProveedor = "999", Fecha = new DateTime(2026, 1, 10), NºDocumentoProv = "INV-ES-2026-0000" },
                // Dentro del rango, proveedor correcto, sin documento -> no
                new CabFacturaCmp { Empresa = empresa, Número = "4", NºProveedor = "999", Fecha = new DateTime(2026, 2, 20), NºDocumentoProv = null },
                // Dentro del rango, proveedor correcto, documento con espacios -> sí (trimeado)
                new CabFacturaCmp { Empresa = empresa, Número = "5", NºProveedor = "999", Fecha = new DateTime(2026, 2, 28), NºDocumentoProv = "INV-ES-2026-0002  " }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeCabFacturasCmp, facturas);

            var result = await controller.GetFacturasContabilizadasProveedor("999", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)) as OkNegotiatedContentResult<List<NestoAPI.Models.PedidosCompra.FacturaContabilizadaProveedorDTO>>;

            Assert.IsNotNull(result);
            CollectionAssert.AreEquivalent(
                new List<string> { "INV-ES-2026-0001", "INV-ES-2026-0002" },
                result.Content.Select(f => f.NumeroDocumentoProv).ToList());
        }

        [TestMethod]
        public async Task GetFacturasContabilizadasProveedor_EliminaDuplicados()
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            var facturas = new List<CabFacturaCmp>
            {
                new CabFacturaCmp { Empresa = empresa, Número = "1", NºProveedor = "999", Fecha = new DateTime(2026, 2, 10), NºDocumentoProv = "INV-A" },
                new CabFacturaCmp { Empresa = empresa, Número = "2", NºProveedor = "999", Fecha = new DateTime(2026, 2, 11), NºDocumentoProv = "INV-A" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeCabFacturasCmp, facturas);

            var result = await controller.GetFacturasContabilizadasProveedor("999", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)) as OkNegotiatedContentResult<List<NestoAPI.Models.PedidosCompra.FacturaContabilizadaProveedorDTO>>;

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Content.Count);
            Assert.AreEqual("INV-A", result.Content[0].NumeroDocumentoProv);
        }

        [TestMethod]
        public void ToCabPedidoCmp_SinFormaVenta_UsaFormaVentaPorDefecto()
        {
            var dto = CrearDtoConUnaLinea();
            dto.Lineas.First().FormaVenta = null;

            var cab = dto.ToCabPedidoCmp();

            Assert.AreEqual(Constantes.Empresas.FORMA_VENTA_POR_DEFECTO, cab.LinPedidoCmps.First().FormaVenta);
        }

        [TestMethod]
        public void ToCabPedidoCmp_ConFormaVenta_UsaLaIndicada()
        {
            var dto = CrearDtoConUnaLinea();
            dto.Lineas.First().FormaVenta = "STK";

            var cab = dto.ToCabPedidoCmp();

            Assert.AreEqual("STK", cab.LinPedidoCmps.First().FormaVenta);
        }

        [TestMethod]
        public void ToCabPedidoCmp_MapeaFacturaProveedorANumDocumentoProv()
        {
            var dto = CrearDtoConUnaLinea();
            dto.FacturaProveedor = "INV-ES-2026-0001";

            var cab = dto.ToCabPedidoCmp();

            Assert.AreEqual("INV-ES-2026-0001", cab.NºDocumentoProv);
        }

        private PedidoCompraDTO CrearDtoConUnaLinea()
        {
            return new PedidoCompraDTO
            {
                Id = 1,
                Empresa = "1",
                Proveedor = "999",
                Contacto = "0",
                Fecha = new DateTime(2026, 2, 28),
                Lineas = new List<LineaPedidoCompraDTO>
                {
                    new LineaPedidoCompraDTO
                    {
                        TipoLinea = Constantes.TiposLineaCompra.CUENTA_CONTABLE,
                        Producto = "60000100",
                        Cantidad = 1,
                        PrecioUnitario = 10M,
                        FechaRecepcion = new DateTime(2026, 2, 28),
                        CodigoIvaProducto = "G21",
                        Estado = Constantes.EstadosLineaVenta.PENDIENTE
                    }
                }
            };
        }

        // NestoAPI#384: guarda de idempotencia de CrearAlbaranYFactura. Caso real 17/08/26:
        // contabilizando los gastos de una remesa (una llamada por factura), la segunda murió
        // por deadlock y al reintentar el botón se duplicó la primera (88130/88131). Con la
        // guarda, la factura ya contabilizada (proveedor + nº factura del proveedor) se
        // devuelve tal cual y solo se crea lo que falta.

        private DbSet<ExtractoProveedor> ConFakeExtractosProveedor(params ExtractoProveedor[] extractos)
        {
            var fakeExtractos = A.Fake<DbSet<ExtractoProveedor>>(o =>
                o.Implements<IQueryable<ExtractoProveedor>>().Implements<IDbAsyncEnumerable<ExtractoProveedor>>());
            A.CallTo(() => db.ExtractosProveedor).Returns(fakeExtractos);
            ConfigurarFakeDbSet(fakeExtractos, extractos.AsQueryable());
            return fakeExtractos;
        }

        [TestMethod]
        public async Task BuscarFacturaExistente_FacturaYaContabilizada_LaDevuelveConSusDatos()
        {
            ConfigurarFakeDbSet(fakeCabFacturasCmp, new List<CabFacturaCmp>
            {
                new CabFacturaCmp { Empresa = "1", Número = "88130", NºProveedor = "433", NºDocumentoProv = "189642364" }
            }.AsQueryable());
            _ = ConFakeExtractosProveedor(new ExtractoProveedor
            {
                Empresa = "1",
                Número = "433",
                TipoApunte = Constantes.TiposExtractoCliente.FACTURA,
                NºDocumento = "88130",
                Asiento = 555,
                Importe = 5.98M
            });

            var servicio = new PedidosCompraService();
            var resultado = await servicio.BuscarFacturaExistente("433", "189642364", db);

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.YaExistia);
            Assert.IsTrue(resultado.Exito);
            Assert.AreEqual(88130, resultado.Factura);
            Assert.AreEqual(555, resultado.AsientoFactura);
            Assert.AreEqual(5.98M, resultado.ImporteFactura);
        }

        [TestMethod]
        public async Task BuscarFacturaExistente_SinFacturaPrevia_DevuelveNull()
        {
            ConfigurarFakeDbSet(fakeCabFacturasCmp, new List<CabFacturaCmp>
            {
                new CabFacturaCmp { Empresa = "1", Número = "88130", NºProveedor = "433", NºDocumentoProv = "189642364" }
            }.AsQueryable());

            var servicio = new PedidosCompraService();
            var resultado = await servicio.BuscarFacturaExistente("433", "189642399", db);

            Assert.IsNull(resultado, "Sin factura previa con ese documento, se crea como siempre");
        }

        [TestMethod]
        public async Task BuscarFacturaExistente_SinNumeroDeFacturaDelProveedor_DevuelveNull()
        {
            // Sin clave natural no hay idempotencia posible: no debe bloquear la creación.
            var servicio = new PedidosCompraService();

            Assert.IsNull(await servicio.BuscarFacturaExistente("433", null, db));
            Assert.IsNull(await servicio.BuscarFacturaExistente("433", "  ", db));
            Assert.IsNull(await servicio.BuscarFacturaExistente(null, "189642364", db));
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
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
