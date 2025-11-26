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
        public async Task InsertarDesdeFactura_ConEfectoValido_InsertaExtractoRutaConImportePendiente()
        {
            // Arrange
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";
            var fechaVto = DateTime.Now.AddDays(30);

            // Creamos el efecto (TipoApunte = "2", Efecto = "1") que es el que debe usarse
            var extractoEfecto = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "2", // Cartera (efecto)
                Efecto = "1",     // Primer efecto
                Fecha = DateTime.Now,
                Importe = 100m,
                ImportePdte = 0m, // En ExtractoCliente puede ser 0, pero en ExtractoRuta será igual a Importe
                Delegación = "ALG",
                FormaVenta = "TEL",
                Vendedor = "JE ",
                FechaVto = fechaVto,
                FormaPago = "EFC"
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoEfecto });

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
                e.Efecto == "1" &&
                e.Concepto == pedido.Comentarios &&
                e.Importe == 100m &&
                e.ImportePdte == 100m && // ImportePdte = Importe del efecto
                e.FechaVto == fechaVto &&
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

            var extractoEfecto = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "2", // Cartera (efecto)
                Efecto = "1",     // Primer efecto
                Fecha = DateTime.Now,
                Importe = 50m
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoEfecto });

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
        public async Task InsertarDesdeFactura_SinEfectoEnExtractoCliente_LanzaExcepcion()
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
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task InsertarDesdeFactura_ConSoloFacturaSinEfecto_LanzaExcepcion()
        {
            // Arrange - REGRESIÓN: Antes buscábamos TipoApunte="1" (factura), ahora debe buscar TipoApunte="2" (efecto)
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";

            // Solo existe el registro de factura (TipoApunte = "1"), NO el efecto
            var extractoFactura = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "1", // Factura, NO efecto
                Efecto = null,
                Fecha = DateTime.Now,
                Importe = 100m,
                ImportePdte = 0m
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoFactura });

            // Act - Debe lanzar excepción porque no encuentra el efecto (TipoApunte="2")
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario);

            // Assert - Excepción esperada
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_ConVariosEfectos_UsaPrimerEfecto()
        {
            // Arrange - Cuando hay varios efectos, debe usar el primero (Efecto = "1")
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";
            var fechaVtoPrimerEfecto = new DateTime(2025, 12, 1);
            var fechaVtoSegundoEfecto = new DateTime(2025, 12, 31);

            var extractos = new List<ExtractoCliente>
            {
                // Primer efecto - este debe usarse
                new ExtractoCliente
                {
                    Empresa = pedido.Empresa,
                    Nº_Orden = 12345,
                    Número = pedido.Nº_Cliente,
                    Contacto = pedido.Contacto,
                    Nº_Documento = numeroFactura,
                    TipoApunte = "2",
                    Efecto = "1",
                    Fecha = DateTime.Now,
                    Importe = 50m,
                    FechaVto = fechaVtoPrimerEfecto
                },
                // Segundo efecto - NO debe usarse
                new ExtractoCliente
                {
                    Empresa = pedido.Empresa,
                    Nº_Orden = 12346,
                    Número = pedido.Nº_Cliente,
                    Contacto = pedido.Contacto,
                    Nº_Documento = numeroFactura,
                    TipoApunte = "2",
                    Efecto = "2",
                    Fecha = DateTime.Now,
                    Importe = 50m,
                    FechaVto = fechaVtoSegundoEfecto
                }
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, extractos);

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);

            // Assert - Debe usar el primer efecto con Importe=50 y FechaVto del primer efecto
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Efecto == "1" &&
                e.Importe == 50m &&
                e.ImportePdte == 50m &&
                e.FechaVto == fechaVtoPrimerEfecto
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_ConEmpresaTraspasada_UsaEmpresaDelPedido()
        {
            // Arrange - REGRESIÓN: Después del traspaso, el pedido tiene Empresa="3"
            // y el ExtractoCliente también debe estar en Empresa="3"
            var pedido = CrearPedidoPrueba();
            pedido.Empresa = "3  "; // Empresa después del traspaso
            string numeroFactura = "GB2501944";
            string usuario = "testuser";

            var extractoEfecto = new ExtractoCliente
            {
                Empresa = "3  ", // Debe coincidir con la empresa del pedido
                Nº_Orden = 2963056,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "2",
                Efecto = "1",
                Fecha = DateTime.Now,
                Importe = 50m,
                FechaVto = new DateTime(2025, 11, 25)
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoEfecto });

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);

            // Assert - El ExtractoRuta debe tener Empresa="3"
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Empresa == "3  " &&
                e.Importe == 50m &&
                e.ImportePdte == 50m
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_ImportePdteEsIgualAImporte()
        {
            // Arrange - REGRESIÓN CRÍTICA: ImportePdte debe ser igual a Importe del efecto
            // Este era el bug original: ImportePdte se copiaba del ExtractoCliente donde era 0
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";

            var extractoEfecto = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "2",
                Efecto = "1",
                Fecha = DateTime.Now,
                Importe = 150.75m,
                ImportePdte = 0m, // En ExtractoCliente es 0, pero en ExtractoRuta debe ser = Importe
                FechaVto = DateTime.Now.AddDays(30)
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoEfecto });

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);

            // Assert - ImportePdte DEBE ser igual a Importe (150.75), NO 0
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Importe == 150.75m &&
                e.ImportePdte == 150.75m // Este era el bug: antes era 0
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_EfectoSeCopiaCorrectamente()
        {
            // Arrange - REGRESIÓN: El campo Efecto debe copiarse del ExtractoCliente
            // Antes se copiaba de TipoApunte="1" donde Efecto=NULL
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";

            var extractoEfecto = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "2",
                Efecto = "1", // Este valor debe copiarse
                Fecha = DateTime.Now,
                Importe = 100m
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoEfecto });

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);

            // Assert - Efecto debe ser "1", NO null
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.Efecto == "1" // Este era el bug: antes era NULL
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task InsertarDesdeFactura_FechaVtoSeCopiaDelEfecto()
        {
            // Arrange - REGRESIÓN: FechaVto debe ser la del efecto, no la de la factura
            // La factura (TipoApunte="1") tiene FechaVto=2001-01-01 (incorrecta)
            // El efecto (TipoApunte="2") tiene la fecha correcta de vencimiento
            var pedido = CrearPedidoPrueba();
            string numeroFactura = "FV123";
            string usuario = "testuser";
            var fechaVtoCorrecta = new DateTime(2025, 12, 15);

            var extractoEfecto = new ExtractoCliente
            {
                Empresa = pedido.Empresa,
                Nº_Orden = 12345,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Nº_Documento = numeroFactura,
                TipoApunte = "2",
                Efecto = "1",
                Fecha = DateTime.Now,
                Importe = 100m,
                FechaVto = fechaVtoCorrecta // Esta es la fecha correcta de vencimiento
            };

            var mockExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(opt => opt.Implements<IQueryable<ExtractoCliente>>());
            A.CallTo(() => _db.ExtractosCliente).Returns(mockExtractosCliente);
            ConfigurarDbSetFalso(mockExtractosCliente, new List<ExtractoCliente> { extractoEfecto });

            var mockExtractosRuta = A.Fake<DbSet<ExtractoRuta>>(opt => opt.Implements<IQueryable<ExtractoRuta>>());
            A.CallTo(() => _db.ExtractosRuta).Returns(mockExtractosRuta);

            // Act
            await _servicio.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);

            // Assert - FechaVto debe ser 2025-12-15, NO 2001-01-01
            A.CallTo(() => mockExtractosRuta.Add(A<ExtractoRuta>.That.Matches(e =>
                e.FechaVto == fechaVtoCorrecta
            ))).MustHaveHappenedOnceExactly();
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
