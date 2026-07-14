using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Issue #214: GetDireccionesEntrega devolvía 500 (NullReferenceException) cuando el cliente
    /// no tenía contacto principal, porque la proyección desreferenciaba clienteDireccionPorDefecto null.
    /// </summary>
    [TestClass]
    public class PlantillaVentasControllerTests
    {
        [TestMethod]
        public void GetDireccionesEntrega_ClienteSinContactoPrincipal_NoLanza500YNingunaEsPorDefecto()
        {
            // Cliente con una sola dirección y SIN ninguna fila ClientePrincipal (el caso que petaba)
            var cliente = CrearClienteCompleto("1", "12345", "0", clientePrincipal: false);
            var controller = CrearControllerConClientes(cliente);

            // Antes del fix esto lanzaba NullReferenceException (500)
            var resultado = controller.GetDireccionesEntrega("1", "12345", 0).ToList();

            Assert.AreEqual(1, resultado.Count);
            Assert.IsFalse(resultado[0].esDireccionPorDefecto, "Sin contacto principal, ninguna dirección debe quedar marcada como por defecto");
        }

        [TestMethod]
        public void GetDireccionesEntrega_ClienteConContactoPrincipal_MarcaSoloLaDireccionPorDefecto()
        {
            var principal = CrearClienteCompleto("1", "12345", "0", clientePrincipal: true);
            principal.ContactoDefecto = "0";
            var otraDireccion = CrearClienteCompleto("1", "12345", "1", clientePrincipal: false);
            var controller = CrearControllerConClientes(principal, otraDireccion);

            var resultado = controller.GetDireccionesEntrega("1", "12345", 0).ToList();

            Assert.AreEqual(2, resultado.Count);
            Assert.IsTrue(resultado.Single(d => d.contacto == "0").esDireccionPorDefecto);
            Assert.IsFalse(resultado.Single(d => d.contacto == "1").esDireccionPorDefecto);
        }

        #region PonerStock (#257: de N+1 consultas a 2 agrupadas)

        // Los valores devueltos deben coincidir EXACTAMENTE con los del bucle antiguo
        // (ProductoPlantillaDTO.Stock/CantidadDisponible producto a producto).

        [TestMethod]
        public void PonerStock_VariosProductosYAlmacenes_CalculaStockDisponibleYLegacy()
        {
            var controller = CrearControllerConStocks(
                extractos: new[]
                {
                    Extracto("38697", "ALG", 10), Extracto("38697", "ALG", 5),   // se suman: 15
                    Extracto("38697", "REI", 3),
                    Extracto("12345", "ALG", 7)
                },
                reservas: new[]
                {
                    Reserva("38697", "ALG", 4, Constantes.EstadosLineaVenta.EN_CURSO),
                    Reserva("38697", "ALG", 2, Constantes.EstadosLineaVenta.PENDIENTE), // reservado ALG: 6
                    Reserva("38697", "REI", 1, Constantes.EstadosLineaVenta.EN_CURSO)
                });
            var param = new PlantillaVentasController.PonerStockParam
            {
                Almacenes = new List<string> { "ALG", "REI" },
                Lineas = new List<LineaPlantillaVenta>
                {
                    new LineaPlantillaVenta { producto = "38697" },
                    new LineaPlantillaVenta { producto = "12345" }
                }
            };

            List<LineaPlantillaVenta> resultado = controller.PonerStock(param);

            LineaPlantillaVenta linea = resultado.Single(l => l.producto == "38697");
            Assert.AreEqual(15, linea.stocks.Single(s => s.almacen == "ALG").stock);
            Assert.AreEqual(9, linea.stocks.Single(s => s.almacen == "ALG").cantidadDisponible); // 15 - 6
            Assert.AreEqual(2, linea.stocks.Single(s => s.almacen == "REI").cantidadDisponible); // 3 - 1
            Assert.AreEqual(9, linea.cantidadDisponible, "El campo legacy sale del primer almacén");
            Assert.AreEqual(11, linea.StockDisponibleTodosLosAlmacenes, "Suma de disponibles: 9 + 2");

            LineaPlantillaVenta sinReservas = resultado.Single(l => l.producto == "12345");
            Assert.AreEqual(7, sinReservas.stocks.Single(s => s.almacen == "ALG").cantidadDisponible);
            Assert.AreEqual(0, sinReservas.stocks.Single(s => s.almacen == "REI").stock);
        }

        [TestMethod]
        public void PonerStock_ProductoSinMovimientos_TodoACero()
        {
            var controller = CrearControllerConStocks(new ExtractoProducto[0], new LinPedidoVta[0]);
            var param = new PlantillaVentasController.PonerStockParam
            {
                Almacen = "ALG",
                Lineas = new List<LineaPlantillaVenta> { new LineaPlantillaVenta { producto = "99999" } }
            };

            List<LineaPlantillaVenta> resultado = controller.PonerStock(param);

            Assert.AreEqual(0, resultado[0].stocks.Single().stock);
            Assert.AreEqual(0, resultado[0].cantidadDisponible);
            Assert.AreEqual(0, resultado[0].StockDisponibleTodosLosAlmacenes);
        }

        [TestMethod]
        public void PonerStock_SumaLaEmpresaEspejoYExcluyeOtrasEmpresasYEstados()
        {
            var extractoOtraEmpresa = Extracto("38697", "ALG", 100);
            extractoOtraEmpresa.Empresa = "5";
            var reservaFacturada = Reserva("38697", "ALG", 50, Constantes.EstadosLineaVenta.FACTURA);
            var controller = CrearControllerConStocks(
                extractos: new[]
                {
                    Extracto("38697", "ALG", 10),
                    ExtractoEnEmpresa("38697", "ALG", 5, Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
                    extractoOtraEmpresa
                },
                reservas: new[]
                {
                    Reserva("38697", "ALG", 4, Constantes.EstadosLineaVenta.EN_CURSO),
                    reservaFacturada
                });
            var param = new PlantillaVentasController.PonerStockParam
            {
                Almacen = "ALG",
                Lineas = new List<LineaPlantillaVenta> { new LineaPlantillaVenta { producto = "38697" } }
            };

            List<LineaPlantillaVenta> resultado = controller.PonerStock(param);

            Assert.AreEqual(15, resultado[0].stocks.Single().stock, "Empresa 1 + espejo 3; la 5 no cuenta");
            Assert.AreEqual(11, resultado[0].stocks.Single().cantidadDisponible, "Las líneas facturadas no reservan");
        }

        private static PlantillaVentasController CrearControllerConStocks(ExtractoProducto[] extractos, LinPedidoVta[] reservas)
        {
            var db = A.Fake<NVEntities>();
            var fakeExtractos = A.Fake<DbSet<ExtractoProducto>>(o => o.Implements<IQueryable<ExtractoProducto>>().Implements<IDbAsyncEnumerable<ExtractoProducto>>());
            ConfigurarFakeDbSet(fakeExtractos, extractos.AsQueryable());
            A.CallTo(() => db.ExtractosProducto).Returns(fakeExtractos);
            var fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            ConfigurarFakeDbSet(fakeLineas, reservas.AsQueryable());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);

            return new PlantillaVentasController(A.Fake<IServicioPlantillaVenta>(), db);
        }

        private static ExtractoProducto Extracto(string producto, string almacen, short cantidad)
            => ExtractoEnEmpresa(producto, almacen, cantidad, Constantes.Empresas.EMPRESA_POR_DEFECTO);

        private static ExtractoProducto ExtractoEnEmpresa(string producto, string almacen, short cantidad, string empresa)
            => new ExtractoProducto { Empresa = empresa, Número = producto, Almacén = almacen, Cantidad = cantidad };

        private static LinPedidoVta Reserva(string producto, string almacen, short cantidad, int estado)
            => new LinPedidoVta
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Producto = producto,
                Almacén = almacen,
                Cantidad = cantidad,
                Estado = (short)estado
            };

        #endregion

        #region Helpers

        private static PlantillaVentasController CrearControllerConClientes(params Cliente[] clientes)
        {
            var db = A.Fake<NVEntities>();
            var fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            ConfigurarFakeDbSet(fakeClientes, clientes.AsQueryable());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);

            return new PlantillaVentasController(A.Fake<IServicioPlantillaVenta>(), db);
        }

        private static Cliente CrearClienteCompleto(string empresa, string numero, string contacto, bool clientePrincipal)
        {
            return new Cliente
            {
                Empresa = empresa,
                Nº_Cliente = numero,
                Contacto = contacto,
                Estado = 0,
                ClientePrincipal = clientePrincipal,
                ContactoDefecto = contacto,
                CodPostal = "28000",
                ComentarioPicking = "",
                ComentarioRuta = "",
                Comentarios = "",
                Dirección = "Calle Test 1",
                IVA = "G21",
                Nombre = "CLIENTE TEST",
                Población = "MADRID",
                Provincia = "MADRID",
                Vendedor = "NV",
                PeriodoFacturación = "NOR",
                CCC = "",
                Ruta = "",
                CIF_NIF = null,
                CondPagoClientes = new List<CondPagoCliente>
                {
                    new CondPagoCliente { ImporteMínimo = 0, FormaPago = "CONT", PlazosPago = "CONTADO" }
                },
                PersonasContactoClientes = new List<PersonaContactoCliente>()
            };
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

        #endregion
    }
}
