using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ServicioExtractoRutaTests
    {
        private NVEntities _db;
        private ServicioExtractoRuta _servicio;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            _servicio = new ServicioExtractoRuta(_db);
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_ConFacturaValida_InsertaExtractoRuta()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";

            var extractoCliente = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "1",
                Fecha = DateTime.Now,
                Importe = 100m,
                ImportePdte = 100m,
                Delegación = "ALG",
                FormaVenta = "TEL",
                Vendedor = "JE ",
                FechaVto = DateTime.Now.AddDays(30),
                FormaPago = "EFC",
                Efecto = "1  "
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoCliente });

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);

            // Assert
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Empresa == pedido.Empresa &&
                e.Número == pedido.Nº_Cliente &&
                e.Contacto == pedido.Contacto &&
                e.Nº_Documento == numeroFactura.PadRight(10) &&
                e.Concepto == pedido.Comentarios &&
                e.Importe == 100m &&
                e.TipoRuta == "P"
            ))).MustHaveHappenedOnceExactly();

            A.CallTo(() => _db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_ConAutoSaveFalse_NoGuardaCambios()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";

            var extractoCliente = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "1",
                Fecha = DateTime.Now
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoCliente });

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: false);

            // Assert
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task InsertarDesdeFactura_SinExtractoCliente_LanzaExcepcion()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente>()); // Sin datos

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario);

            // Assert - Excepción esperada
        }

        [TestMethod]
        public async Task InsertarDesdeAlbaran_ConAlbaranValido_InsertaExtractoRutaConOrdenNegativo()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            pedido.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Nº_Orden = 1,
                    Delegación = "ALG",
                    Forma_Venta = "TEL"
                }
            };

            int numeroAlbaran = 5001;
            string usuario = "testuser";

            var cliente = new Cliente
            {
                Empresa = pedido.Empresa,
                Nº_Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                CodPostal = "28001"
            };

            A.CallTo(() => _db.Clientes.FindAsync(pedido.Empresa, pedido.Nº_Cliente, pedido.Contacto))
                .Returns(Task.FromResult(cliente));

            // ExtractoRuta existente con Nº_Orden mínimo = -100
            var extractosRutaExistentes = new List<ExtractoRuta>
            {
                new ExtractoRuta { Empresa = pedido.Empresa, Nº_Orden = -100 },
                new ExtractoRuta { Empresa = pedido.Empresa, Nº_Orden = -50 }
            };

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);
            ConfigurarDbSetFalso(mockExtractosRuta, extractosRutaExistentes);

            // Act
            await _servicio.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario, autoSave: true);

            // Assert
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Empresa == pedido.Empresa &&
                e.Nº_Orden == -101 && // MIN (-100) - 1
                e.Número == pedido.Nº_Cliente &&
                e.Contacto == pedido.Contacto &&
                e.Nº_Documento == numeroAlbaran.ToString().PadLeft(10) &&
                e.Importe == 0 &&
                e.ImportePdte == 0 &&
                e.TipoRuta == "P"
            ))).MustHaveHappenedOnceExactly();

            A.CallTo(() => _db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task InsertarDesdeAlbaran_SinExtractosRutaPrevios_UsaOrdenMenosUno()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            pedido.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Delegación = "ALG", Forma_Venta = "TEL" }
            };

            int numeroAlbaran = 5001;
            string usuario = "testuser";

            var cliente = new Cliente
            {
                Empresa = pedido.Empresa,
                Nº_Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto
            };

            A.CallTo(() => _db.Clientes.FindAsync(pedido.Empresa, pedido.Nº_Cliente, pedido.Contacto))
                .Returns(Task.FromResult(cliente));

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);
            ConfigurarDbSetFalso(mockExtractosRuta, new List<ExtractoRuta>()); // Sin datos

            // Act
            await _servicio.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario, autoSave: true);

            // Assert
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Nº_Orden == -1 // Primera vez, usa -1
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task InsertarDesdeAlbaran_SinLineasPedido_LanzaExcepcion()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            pedido.LinPedidoVtas = new List<LinPedidoVta>(); // Sin líneas

            int numeroAlbaran = 5001;
            string usuario = "testuser";

            var cliente = new Cliente();
            A.CallTo(() => _db.Clientes.FindAsync(pedido.Empresa, pedido.Nº_Cliente, pedido.Contacto))
                .Returns(Task.FromResult(cliente));

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);
            ConfigurarDbSetFalso(mockExtractosRuta, new List<ExtractoRuta>());

            // Act
            await _servicio.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario);

            // Assert - Excepción esperada
        }

        private CabPedidoVta CrearPedidoPrueba()
        {
            return new CabPedidoVta
            {
                Empresa = "1  ",
                Número = 123456,
                Nº_Cliente = "1001      ",
                Contacto = "0  ",
                Comentarios = "Pedido de prueba",
                Vendedor = "JE ",
                Forma_Pago = "EFC",
                Ruta = "AT "
            };
        }

        private void ConfigurarDbSetFalso<T>(DbSet<T> mockSet, List<T> data) where T : class
        {
            var queryable = data.AsQueryable();
            A.CallTo(() => ((IQueryable<T>)mockSet).Provider).Returns(queryable.Provider);
            A.CallTo(() => ((IQueryable<T>)mockSet).Expression).Returns(queryable.Expression);
            A.CallTo(() => ((IQueryable<T>)mockSet).ElementType).Returns(queryable.ElementType);
            A.CallTo(() => ((IQueryable<T>)mockSet).GetEnumerator()).Returns(queryable.GetEnumerator());
        }
    }
}
