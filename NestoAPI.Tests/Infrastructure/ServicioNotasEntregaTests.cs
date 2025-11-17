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

        #region ProcesarNotaEntrega - Campos NºPedido y NºTraspaso

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasYaFacturadas_RellenaCampoNumeroPedido()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 99999, // Este número debe aparecer en PreExtrProducto.NºPedido
                Nº_Cliente = "1006",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 100m,
                        Producto = "PROD010",
                        Cantidad = 2,
                        Grupo = "GRP001",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Pedido" };
            A.CallTo(() => db.Clientes.Find("1", "1006", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 5000 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            Assert.IsTrue(resultado.TeniaLineasYaFacturadas);

            // Verificar que se insertó en PreExtrProducto con NºPedido correcto
            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.NºPedido == 99999 && // Campo Pedido debe tener el número del pedido
                p.Empresa == "1" &&
                p.Número == "PROD010" &&
                p.Usuario == "NUEVAVISION\\Carlos" // Usuario con dominio
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasYaFacturadas_RellenaCampoTraspaso()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 88888,
                Nº_Cliente = "1007",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 150m,
                        Producto = "PROD011",
                        Cantidad = 3,
                        Grupo = "GRP002",
                        Almacén = Constantes.Almacenes.REINA,
                        Forma_Venta = "VAR",
                        Delegación = "REI"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Traspaso" };
            A.CallTo(() => db.Clientes.Find("1", "1007", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 7777 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            // Act
            var resultado = await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            Assert.IsTrue(resultado.TeniaLineasYaFacturadas);

            // Verificar que se insertó en PreExtrProducto con NºTraspaso de ContadoresGlobales
            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.NºTraspaso == 7777 && // Campo Traspaso debe venir de ContadoresGlobales.TraspasoAlmacén
                p.NºPedido == 88888 &&
                p.Número == "PROD011"
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasYaFacturadas_IncrementaContadorTraspasoAlmacen()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 77777,
                Nº_Cliente = "1008",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 200m,
                        Producto = "PROD012",
                        Cantidad = 4,
                        Grupo = "GRP003",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Incremento" };
            A.CallTo(() => db.Clientes.Find("1", "1008", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 5000 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            // Act
            await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            // Verificar que el contador de TraspasoAlmacén se incrementó en 1
            Assert.AreEqual(5001, fakeContador.TraspasoAlmacén, "ContadoresGlobales.TraspasoAlmacén debe incrementarse en 1");
            Assert.AreEqual(1001, fakeContador.NotaEntrega, "ContadoresGlobales.NotaEntrega también debe incrementarse");
        }

        [TestMethod]
        public async Task ProcesarNotaEntrega_VariasLineasYaFacturadas_UsanMismoNumeroTraspaso()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 66666,
                Nº_Cliente = "1009",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 100m,
                        Producto = "PROD013",
                        Cantidad = 1,
                        Grupo = "GRP001",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 2,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 150m,
                        Producto = "PROD014",
                        Cantidad = 2,
                        Grupo = "GRP002",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 3,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 200m,
                        Producto = "PROD015",
                        Cantidad = 3,
                        Grupo = "GRP003",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Múltiples Líneas" };
            A.CallTo(() => db.Clientes.Find("1", "1009", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 9000 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            // Act
            await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            // Verificar que TODAS las líneas usaron el MISMO número de traspaso (9000)
            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.NºTraspaso == 9000 && p.Número == "PROD013"
            ))).MustHaveHappenedOnceExactly();

            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.NºTraspaso == 9000 && p.Número == "PROD014"
            ))).MustHaveHappenedOnceExactly();

            A.CallTo(() => fakePreExtr.Add(A<PreExtrProducto>.That.Matches(p =>
                p.NºTraspaso == 9000 && p.Número == "PROD015"
            ))).MustHaveHappenedOnceExactly();

            // Verificar que el contador solo se incrementó UNA VEZ (no 3 veces)
            Assert.AreEqual(9001, fakeContador.TraspasoAlmacén, "Contador debe incrementarse solo una vez por pedido, no por línea");
        }

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasNoFacturadas_NoIncrementaContadorTraspaso()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 55555,
                Nº_Cliente = "1010",
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
                        Producto = "PROD016"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Sin Traspaso" };
            A.CallTo(() => db.Clientes.Find("1", "1010", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 5000 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            // Act
            await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            // Verificar que el contador de TraspasoAlmacén NO se incrementó (porque YaFacturado=false)
            Assert.AreEqual(5000, fakeContador.TraspasoAlmacén, "TraspasoAlmacén NO debe incrementarse si no hay líneas YaFacturado=true");
            Assert.AreEqual(1001, fakeContador.NotaEntrega, "NotaEntrega sí debe incrementarse siempre");
        }

        #endregion

        #region ProcesarNotaEntrega - Ejecución de prdExtrProducto

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasYaFacturadas_EjecutaPrdExtrProductoDespuesDeSaveChanges()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 44444,
                Nº_Cliente = "1011",
                Contacto = "0",
                NotaEntrega = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        YaFacturado = true,
                        Base_Imponible = 300m,
                        Producto = "PROD017",
                        Cantidad = 5,
                        Grupo = "GRP001",
                        Almacén = Constantes.Almacenes.ALGETE,
                        Forma_Venta = "VAR",
                        Delegación = "ALG"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Procedimiento" };
            A.CallTo(() => db.Clientes.Find("1", "1011", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 5000 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            var fakePreExtr = A.Fake<System.Data.Entity.DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(fakePreExtr);

            var fakeDatabase = A.Fake<System.Data.Entity.Database>();
            A.CallTo(() => db.Database).Returns(fakeDatabase);

            // Act
            await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            // Verificar que SaveChangesAsync se llamó
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();

            // Verificar que prdExtrProducto se ejecutó con los parámetros correctos
            A.CallTo(() => fakeDatabase.ExecuteSqlCommandAsync(
                "EXEC prdExtrProducto @Empresa, @Diario",
                A<object[]>.That.Matches(args =>
                    args.Length == 2 &&
                    ((System.Data.SqlClient.SqlParameter)args[0]).Value.ToString() == "1" &&
                    ((System.Data.SqlClient.SqlParameter)args[1]).Value.ToString() == Constantes.DiariosProducto.ENTREGA_FACTURADA
                )
            )).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarNotaEntrega_LineasNoFacturadas_NoEjecutaPrdExtrProducto()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 33333,
                Nº_Cliente = "1012",
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
                        Producto = "PROD018"
                    }
                }
            };

            var fakeCliente = new Cliente { Nombre = "Cliente Test Sin Procedimiento" };
            A.CallTo(() => db.Clientes.Find("1", "1012", "0")).Returns(fakeCliente);

            var fakeContador = new ContadorGlobal { NotaEntrega = 1000, TraspasoAlmacén = 5000 };
            A.CallTo(() => db.ContadoresGlobales.FirstOrDefaultAsync()).Returns(Task.FromResult(fakeContador));

            var fakeDatabase = A.Fake<System.Data.Entity.Database>();
            A.CallTo(() => db.Database).Returns(fakeDatabase);

            // Act
            await servicio.ProcesarNotaEntrega(pedido, "NUEVAVISION\\Carlos");

            // Assert
            // Verificar que SaveChangesAsync se llamó
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();

            // Verificar que prdExtrProducto NO se ejecutó (porque no hay líneas YaFacturado=true)
            A.CallTo(() => fakeDatabase.ExecuteSqlCommandAsync(
                A<string>.That.Contains("prdExtrProducto"),
                A<object[]>._
            )).MustNotHaveHappened();
        }

        #endregion
    }
}
