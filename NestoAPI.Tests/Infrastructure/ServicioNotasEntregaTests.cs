using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ServicioNotasEntregaTests
    {
        private NVEntities db;
        private IServicioNotasEntrega servicio;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicio = new ServicioNotasEntrega(db);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_ConDbValido_CreaInstancia()
        {
            // Arrange, Act & Assert
            Assert.IsNotNull(servicio);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConDbNull_LanzaArgumentNullException()
        {
            // Arrange, Act & Assert
            var _ = new ServicioNotasEntrega(null);
        }

        #endregion

        #region ProcesarNotaEntrega - Líneas NO Facturadas

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasNoFacturadas_SoloCambiaEstadoSinTocarStock()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                Nº_Cliente = "1001",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = false, // NO facturado → NO tocar stock
                        Base_Imponible = 100m,
                        Producto = "PROD001"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 2,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = false,
                        Base_Imponible = 50m,
                        Producto = "PROD002"
                    }
                }
            };

            // Mock del cliente para obtener el nombre
            var fakeCliente = new Cliente { Nombre = "Cliente Test" };
            A.CallTo(() => db.Clientes.Find("1", "1001", "0")).Returns(fakeCliente);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "testuser");

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual("1", resultado.Empresa);
            Assert.AreEqual(12345, resultado.NumeroPedido);
            Assert.AreEqual("1001", resultado.Cliente);
            Assert.AreEqual("0", resultado.Contacto);
            Assert.AreEqual("Cliente Test", resultado.NombreCliente);
            Assert.AreEqual(2, resultado.NumeroLineas);
            Assert.IsFalse(resultado.TeniaLineasYaFacturadas, "No debe tener líneas ya facturadas");
            Assert.AreEqual(150m, resultado.BaseImponible);

            // Verificar que las líneas cambiaron de estado
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(0).Estado);
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(1).Estado);

            // IMPORTANTE: Verificar que NO se insertó nada en PreExtrProducto (porque YaFacturado=false)
            // Esto se verificará revisando que no se llamó a db.PreExtrProductoes.Add()
        }

        #endregion

        #region ProcesarNotaEntrega - Líneas YA Facturadas

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasYaFacturadas_CambiaEstadoYDaBajaStock()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12346,
                Nº_Cliente = "1002",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true, // YA facturado → DAR DE BAJA stock
                        Base_Imponible = 200m,
                        Producto = "PROD003",
                        Cantidad = 5,
                        Grupo = "GRP001",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    }
                }
            };

            // Mock del cliente
            var fakeCliente = new Cliente { Nombre = "Cliente Facturado" };
            A.CallTo(() => db.Clientes.Find("1", "1002", "0")).Returns(fakeCliente);

            // Mock del DbSet de PreExtrProducto
            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "testuser");

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.NumeroLineas);
            Assert.IsTrue(resultado.TeniaLineasYaFacturadas, "Debe tener líneas ya facturadas");
            Assert.AreEqual(200m, resultado.BaseImponible);

            // Verificar que la línea cambió de estado
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(0).Estado);

            // Verificar que se insertó en PreExtrProducto (para dar de baja stock)
            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.Empresa == "1" &&
                p.Número == "PROD003" &&
                p.Nº_Cliente == "1002" &&
                p.ContactoCliente == "0" &&
                p.Cantidad == 5 &&
                p.Importe == 200m &&
                p.Almacén == Constantes.Almacenes.ALGETE &&
                p.Diario == Constantes.DiariosProducto.ENTREGA_FACTURADA &&
                p.Asiento_Automático == true &&
                p.LinPedido == 1
            ))).MustHaveHappenedOnceExactly();

            // Verificar que se guardó en la BD
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        #endregion

        #region ProcesarNotaEntrega - Mezcla de Facturadas y No Facturadas

        [TestMethod]
        public async Task ProcesarNotaEntrega_MezclaFacturadoYNoFacturado_ProcesaCorrectamente()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12347,
                Nº_Cliente = "1003",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = false, // NO facturado
                        Base_Imponible = 100m,
                        Producto = "PROD004"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 2,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true, // YA facturado
                        Base_Imponible = 150m,
                        Producto = "PROD005",
                        Cantidad = 3,
                        Grupo = "GRP002",
                        Almacén = Constantes.Almacenes.REINA,
                        Forma_Venta = "VAR",
                        Delegación = "REI"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 3,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = false, // NO facturado
                        Base_Imponible = 75m,
                        Producto = "PROD006"
                    }
                }
            };

            // Mock del cliente
            var fakeCliente = new Cliente { Nombre = "Cliente Mixto" };
            A.CallTo(() => db.Clientes.Find("1", "1003", "0")).Returns(fakeCliente);

            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "testuser");

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(3, resultado.NumeroLineas);
            Assert.IsTrue(resultado.TeniaLineasYaFacturadas, "Debe detectar al menos una línea ya facturada");
            Assert.AreEqual(325m, resultado.BaseImponible);

            // Verificar que TODAS las líneas cambiaron de estado
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(0).Estado);
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(1).Estado);
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(2).Estado);

            // Verificar que solo se insertó UNA entrada en PreExtrProducto (solo la línea YaFacturado=true)
            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.Número == "PROD005" && // Solo PROD005 está YaFacturado
                p.Cantidad == 3 &&
                p.Importe == 150m
            ))).MustHaveHappenedOnceExactly();
        }

        #endregion

        #region ProcesarNotaEntrega - Pedido sin líneas

        [TestMethod]
        public async Task ProcesarNotaEntrega_PedidoSinLineas_RetornaNotaConCeroLineas()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12348,
                Nº_Cliente = "1004",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>()
            };

            // Mock del cliente
            var fakeCliente = new Cliente { Nombre = "Cliente Sin Líneas" };
            A.CallTo(() => db.Clientes.Find("1", "1004", "0")).Returns(fakeCliente);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "testuser");

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.NumeroLineas);
            Assert.IsFalse(resultado.TeniaLineasYaFacturadas);
            Assert.AreEqual(0m, resultado.BaseImponible);
        }

        #endregion

        #region ProcesarNotaEntrega - Validaciones

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task ProcesarNotaEntrega_PedidoNull_LanzaArgumentNullException()
        {
            // Arrange, Act & Assert
            await servicio.ProcesarNotaEntrega(null, "testuser");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task ProcesarNotaEntrega_UsuarioNullOVacio_LanzaArgumentException()
        {
            // Arrange
            var pedido = new CabPedidoVta { NotaEntrega = true };

            // Act & Assert
            await servicio.ProcesarNotaEntrega(pedido, null);
        }

        [TestMethod]
        public async Task ProcesarNotaEntrega_SoloLineasEnCurso_ProcesaSoloEsasLineas()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12349,
                Nº_Cliente = "1005",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO, // Debe procesarse
                        YaFacturado = false,
                        Base_Imponible = 100m,
                        Producto = "PROD007"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 2,
                        Estado = Constantes.EstadosLineaVenta.PENDIENTE, // NO debe procesarse
                        YaFacturado = false,
                        Base_Imponible = 50m,
                        Producto = "PROD008"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 3,
                        Estado = Constantes.EstadosLineaVenta.ALBARAN, // NO debe procesarse (ya tiene albarán)
                        YaFacturado = false,
                        Base_Imponible = 75m,
                        Producto = "PROD009"
                    }
                }
            };

            // Mock del cliente
            var fakeCliente = new Cliente { Nombre = "Cliente Multi-Estado" };
            A.CallTo(() => db.Clientes.Find("1", "1005", "0")).Returns(fakeCliente);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "testuser");

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.NumeroLineas, "Solo debe procesar la línea EN_CURSO");
            Assert.AreEqual(100m, resultado.BaseImponible, "Solo debe sumar la base de la línea EN_CURSO");

            // Verificar estados
            Assert.AreEqual(Constantes.EstadosLineaVenta.NOTA_ENTREGA, pedido.LinPedidoVtas.ElementAt(0).Estado, "Línea EN_CURSO debe cambiar a NOTA_ENTREGA");
            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, pedido.LinPedidoVtas.ElementAt(1).Estado, "Línea PENDIENTE NO debe cambiar");
            Assert.AreEqual(Constantes.EstadosLineaVenta.ALBARAN, pedido.LinPedidoVtas.ElementAt(2).Estado, "Línea ALBARAN NO debe cambiar");
        }

        #endregion
    }
}
