using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas; // Incluye NivelSeveridad
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorFacturacionRutasTests
    {
        // Issue #198 (recuperación del fichero huérfano):
        // - Eliminado Grupo 3 (PuedeFacturarPedido): redundante, ya cubierto por
        //   Facturas/GestorFacturacionRutas_PuedeFacturarPedidoTests.cs (NestoAPI#195).
        // - Eliminado Grupo 6 (ObtenerDocumentosImpresion): usaba API de impresión ya desaparecida
        //   (Cliente.ClienteGrupo, DatosImpresionDocumento, IGestorFacturas.GenerarDatosImpresionFactura).
        // - Eliminados los tests de FacturarRutas que crean albarán (Grupo 2, Grupo 9 multi-error y el
        //   de severidad MantenerJunto): ProcesarPedido ahora hace db.Entry(linea).ReloadAsync(), que
        //   FakeItEasy no puede interceptar (DbContext.Entry no es virtual) -> no testeables con NVEntities fake.
        //   La elegibilidad queda cubierta por ObtenerLineasProcesables_*, ObtenerNumeroAlbaranExistente_*
        //   y PuedeFacturarPedido (fichero hermano).
        // - Eliminado el test de VtoBueno = null: LinPedidoVta.VtoBueno pasó a bool no-nullable.

        private NVEntities db;
        private IServicioAlbaranesVenta servicioAlbaranes;
        private IServicioFacturas servicioFacturas;
        private IGestorFacturas gestorFacturas;
        private IServicioTraspasoEmpresa servicioTraspaso;
        private IServicioNotasEntrega servicioNotasEntrega;
        private IServicioExtractoRuta servicioExtractoRuta;
        private GestorFacturacionRutas gestor;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();

            // #198: PreviewFacturarRutas consulta db.Clientes (muestra de pedidos), así que el DbSet
            // debe ser un fake consultable aunque esté vacío (el código es null-safe -> "Desconocido").
            var fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>().AsQueryable());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);

            servicioAlbaranes = A.Fake<IServicioAlbaranesVenta>();
            servicioFacturas = A.Fake<IServicioFacturas>();
            gestorFacturas = A.Fake<IGestorFacturas>();
            servicioTraspaso = A.Fake<IServicioTraspasoEmpresa>();
            servicioNotasEntrega = A.Fake<IServicioNotasEntrega>();
            servicioExtractoRuta = A.Fake<IServicioExtractoRuta>();

            gestor = new GestorFacturacionRutas(
                db,
                servicioAlbaranes,
                servicioFacturas,
                gestorFacturas,
                servicioTraspaso,
                servicioNotasEntrega,
                servicioExtractoRuta);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_ConDependenciasValidas_CreaInstancia()
        {
            // Arrange, Act & Assert
            Assert.IsNotNull(gestor);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConDbNull_LanzaArgumentNullException()
        {
            // Arrange, Act & Assert
            var _ = new GestorFacturacionRutas(
                null,
                servicioAlbaranes,
                servicioFacturas,
                gestorFacturas,
                servicioTraspaso,
                servicioNotasEntrega,
                servicioExtractoRuta);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConServicioAlbaranesNull_LanzaArgumentNullException()
        {
            // Arrange, Act & Assert
            var _ = new GestorFacturacionRutas(
                db,
                null,
                servicioFacturas,
                gestorFacturas,
                servicioTraspaso,
                servicioNotasEntrega,
                servicioExtractoRuta);
        }

        #endregion

        #region Grupo 1: Detección de comentarios de impresión

        [TestMethod]
        public void DebeImprimirDocumento_FacturaFisica_RetornaTrue()
        {
            // Arrange
            string comentarios = "El cliente solicita FACTURA FÍSICA para su contabilidad";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsTrue(resultado, "Debe detectar 'FACTURA FÍSICA' (mayúsculas con tilde)");
        }

        [TestMethod]
        public void DebeImprimirDocumento_AlbaranFisico_RetornaTrue()
        {
            // Arrange
            string comentarios = "Necesita ALBARÁN FÍSICO firmado";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsTrue(resultado, "Debe detectar 'ALBARÁN FÍSICO' (mayúsculas con tilde)");
        }

        [TestMethod]
        public void DebeImprimirDocumento_ComentarioConTextoAdicional_RetornaTrue()
        {
            // Arrange
            string comentarios = "Cliente nuevo. Enviar factura física junto con el pedido. Urgente.";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsTrue(resultado, "Debe detectar 'factura física' en medio de otros textos");
        }

        [TestMethod]
        public void DebeImprimirDocumento_CaseInsensitive_RetornaTrue()
        {
            // Arrange - minúsculas
            string comentarios1 = "enviar factura física por favor";
            string comentarios2 = "necesito albarán físico";

            // Act
            bool resultado1 = gestor.DebeImprimirDocumento(comentarios1);
            bool resultado2 = gestor.DebeImprimirDocumento(comentarios2);

            // Assert
            Assert.IsTrue(resultado1, "Debe detectar 'factura física' en minúsculas");
            Assert.IsTrue(resultado2, "Debe detectar 'albarán físico' en minúsculas");
        }

        [TestMethod]
        public void DebeImprimirDocumento_SinTildes_RetornaTrue()
        {
            // Arrange - sin tildes
            string comentarios1 = "FACTURA FISICA requerida";
            string comentarios2 = "ALBARAN FISICO necesario";

            // Act
            bool resultado1 = gestor.DebeImprimirDocumento(comentarios1);
            bool resultado2 = gestor.DebeImprimirDocumento(comentarios2);

            // Assert
            Assert.IsTrue(resultado1, "Debe detectar 'FACTURA FISICA' sin tildes");
            Assert.IsTrue(resultado2, "Debe detectar 'ALBARAN FISICO' sin tildes");
        }

        [TestMethod]
        public void DebeImprimirDocumento_SinComentario_RetornaFalse()
        {
            // Arrange
            string comentariosNull = null;
            string comentariosVacio = "";
            string comentariosEspacios = "   ";

            // Act
            bool resultadoNull = gestor.DebeImprimirDocumento(comentariosNull);
            bool resultadoVacio = gestor.DebeImprimirDocumento(comentariosVacio);
            bool resultadoEspacios = gestor.DebeImprimirDocumento(comentariosEspacios);

            // Assert
            Assert.IsFalse(resultadoNull, "Null debe retornar false");
            Assert.IsFalse(resultadoVacio, "String vacío debe retornar false");
            Assert.IsFalse(resultadoEspacios, "Solo espacios debe retornar false");
        }

        [TestMethod]
        public void DebeImprimirDocumento_ComentarioSinPalabrasClave_RetornaFalse()
        {
            // Arrange
            string comentarios = "Cliente solicita entrega urgente mañana por la mañana";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsFalse(resultado, "No debe detectar sin palabras clave");
        }

        #endregion

        #region Grupo 4: PreviewFacturarRutas

        [TestMethod]
        public void PreviewFacturarRutas_ListaVacia_RetornaPreviewVacio()
        {
            // Arrange
            var pedidos = new List<CabPedidoVta>();

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.NumeroPedidos);
            Assert.AreEqual(0, resultado.NumeroAlbaranes);
            Assert.AreEqual(0, resultado.NumeroFacturas);
            Assert.AreEqual(0, resultado.NumeroNotasEntrega);
            Assert.AreEqual(0m, resultado.BaseImponibleAlbaranes);
            Assert.AreEqual(0m, resultado.BaseImponibleFacturas);
            Assert.AreEqual(0m, resultado.BaseImponibleNotasEntrega);
        }

        [TestMethod]
        public void PreviewFacturarRutas_PedidoNRM_CreaAlbaranYFactura()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                Nº_Cliente = "1001",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, // "NRM"
                NotaEntrega = false,
                MantenerJunto = false,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Base_Imponible = 100m,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        Picking = 1,
                        Fecha_Entrega = DateTime.Today,
                        VtoBueno = true
                    }
                }
            };
            var pedidos = new List<CabPedidoVta> { pedido };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(1, resultado.NumeroPedidos);
            Assert.AreEqual(0, resultado.NumeroAlbaranes, "NO debe contar en albaranes si se crea factura");
            Assert.AreEqual(1, resultado.NumeroFacturas, "Debe crear 1 factura para pedido NRM");
            Assert.AreEqual(0, resultado.NumeroNotasEntrega);
            Assert.AreEqual(0m, resultado.BaseImponibleAlbaranes, "NO debe sumar en albaranes si se crea factura");
            Assert.AreEqual(100m, resultado.BaseImponibleFacturas, "Solo debe sumar en facturas");
        }

        [TestMethod]
        public void PreviewFacturarRutas_PedidoFDM_CreaSoloAlbaran()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12346,
                Nº_Cliente = "1002",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES, // "FDM"
                NotaEntrega = false,
                MantenerJunto = false,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Base_Imponible = 200m,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        Picking = 1,
                        Fecha_Entrega = DateTime.Today,
                        VtoBueno = true
                    }
                }
            };
            var pedidos = new List<CabPedidoVta> { pedido };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(1, resultado.NumeroPedidos);
            Assert.AreEqual(1, resultado.NumeroAlbaranes, "Debe crear 1 albarán para pedido FDM");
            Assert.AreEqual(0, resultado.NumeroFacturas, "NO debe crear factura para pedido FDM");
            Assert.AreEqual(0, resultado.NumeroNotasEntrega);
            Assert.AreEqual(200m, resultado.BaseImponibleAlbaranes);
            Assert.AreEqual(0m, resultado.BaseImponibleFacturas);
        }

        [TestMethod]
        public void PreviewFacturarRutas_NotaEntrega_CreaSoloNotaEntrega()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12347,
                Nº_Cliente = "1003",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = true,
                MantenerJunto = false,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Base_Imponible = 50m,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        Picking = 1,
                        Fecha_Entrega = DateTime.Today,
                        VtoBueno = true
                    }
                }
            };
            var pedidos = new List<CabPedidoVta> { pedido };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(1, resultado.NumeroPedidos);
            Assert.AreEqual(0, resultado.NumeroAlbaranes, "NO debe crear albarán para nota de entrega");
            Assert.AreEqual(0, resultado.NumeroFacturas, "NO debe crear factura para nota de entrega");
            Assert.AreEqual(1, resultado.NumeroNotasEntrega, "Debe crear 1 nota de entrega");
            Assert.AreEqual(0m, resultado.BaseImponibleAlbaranes);
            Assert.AreEqual(0m, resultado.BaseImponibleFacturas);
            Assert.AreEqual(50m, resultado.BaseImponibleNotasEntrega);
        }

        [TestMethod]
        public void PreviewFacturarRutas_PedidoNRMConMantenerJuntoSinAlbaran_CreaSoloAlbaran()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12348,
                Nº_Cliente = "1004",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, // "NRM"
                NotaEntrega = false,
                MantenerJunto = true,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    // Solo la línea EN_CURSO con picking se contará
                    new LinPedidoVta
                    {
                        Base_Imponible = 150m,
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        Picking = 1,
                        Fecha_Entrega = DateTime.Today,
                        VtoBueno = true
                    },
                    // Esta línea PENDIENTE NO se contará (no pasa el filtro)
                    new LinPedidoVta
                    {
                        Base_Imponible = 50m,
                        Estado = Constantes.EstadosLineaVenta.PENDIENTE,
                        VtoBueno = true
                    }
                }
            };
            var pedidos = new List<CabPedidoVta> { pedido };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(1, resultado.NumeroPedidos);
            Assert.AreEqual(1, resultado.NumeroAlbaranes, "Debe crear albarán");
            Assert.AreEqual(0, resultado.NumeroFacturas, "NO debe crear factura por MantenerJunto con líneas sin albarán");
            Assert.AreEqual(0, resultado.NumeroNotasEntrega);
            Assert.AreEqual(150m, resultado.BaseImponibleAlbaranes, "Solo debe contar la línea EN_CURSO con picking");
            Assert.AreEqual(0m, resultado.BaseImponibleFacturas);
        }

        [TestMethod]
        public void PreviewFacturarRutas_VariosTypes_CalculaCorrectamente()
        {
            // Arrange
            var pedidos = new List<CabPedidoVta>
            {
                // Pedido 1: NRM - Albarán + Factura (pero solo cuenta en facturas)
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 1,
                    Nº_Cliente = "1001",
                    Contacto = "0",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                    NotaEntrega = false,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta
                        {
                            Base_Imponible = 100m,
                            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                            Picking = 1,
                            Fecha_Entrega = DateTime.Today,
                            VtoBueno = true
                        }
                    }
                },
                // Pedido 2: FDM - Solo Albarán
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 2,
                    Nº_Cliente = "1002",
                    Contacto = "0",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                    NotaEntrega = false,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta
                        {
                            Base_Imponible = 200m,
                            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                            Picking = 1,
                            Fecha_Entrega = DateTime.Today,
                            VtoBueno = true
                        }
                    }
                },
                // Pedido 3: Nota de Entrega
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 3,
                    Nº_Cliente = "1003",
                    Contacto = "0",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                    NotaEntrega = true,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta
                        {
                            Base_Imponible = 50m,
                            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                            Picking = 1,
                            Fecha_Entrega = DateTime.Today,
                            VtoBueno = true
                        }
                    }
                }
            };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(3, resultado.NumeroPedidos);
            Assert.AreEqual(1, resultado.NumeroAlbaranes, "Solo pedido 2 (FDM) cuenta en albaranes");
            Assert.AreEqual(1, resultado.NumeroFacturas, "Solo pedido 1 (NRM) crea factura");
            Assert.AreEqual(1, resultado.NumeroNotasEntrega, "Solo pedido 3 crea nota de entrega");
            Assert.AreEqual(200m, resultado.BaseImponibleAlbaranes, "Solo pedido 2 (FDM)");
            Assert.AreEqual(100m, resultado.BaseImponibleFacturas, "Solo pedido 1 (NRM)");
            Assert.AreEqual(50m, resultado.BaseImponibleNotasEntrega, "Solo pedido 3");
        }

        [TestMethod]
        public void PreviewFacturarRutas_PedidoSinLineas_CuentaPeroSinBase()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12349,
                Nº_Cliente = "1005",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = false,
                MantenerJunto = false,
                LinPedidoVtas = new List<LinPedidoVta>() // Sin líneas
            };
            var pedidos = new List<CabPedidoVta> { pedido };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(1, resultado.NumeroPedidos);
            // Un NRM que puede facturar cuenta como FACTURA, no albarán (evita doble contabilización)
            Assert.AreEqual(0, resultado.NumeroAlbaranes, "NRM facturable no cuenta en albaranes");
            Assert.AreEqual(1, resultado.NumeroFacturas);
            Assert.AreEqual(0m, resultado.BaseImponibleAlbaranes, "Sin líneas, base = 0");
            Assert.AreEqual(0m, resultado.BaseImponibleFacturas, "Sin líneas, base = 0");
        }

        [TestMethod]
        public void PreviewFacturarRutas_PedidosNull_RetornaPreviewVacio()
        {
            // Arrange
            List<CabPedidoVta> pedidos = null;

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.NumeroPedidos);
            Assert.AreEqual(0, resultado.NumeroAlbaranes);
            Assert.AreEqual(0, resultado.NumeroFacturas);
            Assert.AreEqual(0, resultado.NumeroNotasEntrega);
        }

        [TestMethod]
        public void PreviewFacturarRutas_PedidoSinVistoBueno_NoSeIncluye()
        {
            // Arrange
            var pedidos = new List<CabPedidoVta>
            {
                // Pedido 1: Con visto bueno - debe procesarse
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 1,
                    Nº_Cliente = "1001",
                    Contacto = "0",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                    NotaEntrega = false,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta
                        {
                            Base_Imponible = 100m,
                            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                            Picking = 1,
                            Fecha_Entrega = DateTime.Today,
                            VtoBueno = true
                        }
                    }
                },
                // Pedido 2: Sin visto bueno - NO debe procesarse
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 2,
                    Nº_Cliente = "1002",
                    Contacto = "0",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                    NotaEntrega = false,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta
                        {
                            Base_Imponible = 200m,
                            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                            Picking = 1,
                            Fecha_Entrega = DateTime.Today,
                            VtoBueno = false // Sin visto bueno
                        }
                    }
                }
            };

            // Act
            var resultado = gestor.PreviewFacturarRutas(pedidos, DateTime.Today);

            // Assert
            Assert.AreEqual(1, resultado.NumeroPedidos, "Solo debe contar el pedido con visto bueno");
            Assert.AreEqual(1, resultado.NumeroFacturas, "Solo debe crear factura para pedido con visto bueno");
            Assert.AreEqual(100m, resultado.BaseImponibleFacturas, "Solo suma el importe del pedido con visto bueno");
        }

        #endregion

        #region Grupo 5: Validación de Visto Bueno

        [TestMethod]
        public void TieneTodasLasLineasConVistoBueno_TodasConVistoBueno_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { VtoBueno = true },
                    new LinPedidoVta { VtoBueno = true },
                    new LinPedidoVta { VtoBueno = true }
                }
            };

            // Act
            bool resultado = gestor.TieneTodasLasLineasConVistoBueno(pedido);

            // Assert
            Assert.IsTrue(resultado, "Todas las líneas tienen VtoBueno = true");
        }

        [TestMethod]
        public void TieneTodasLasLineasConVistoBueno_AlgunaLineaSinVistoBueno_RetornaFalse()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { VtoBueno = true },
                    new LinPedidoVta { VtoBueno = false }, // Una sin visto bueno
                    new LinPedidoVta { VtoBueno = true }
                }
            };

            // Act
            bool resultado = gestor.TieneTodasLasLineasConVistoBueno(pedido);

            // Assert
            Assert.IsFalse(resultado, "Hay al menos una línea sin VtoBueno = true");
        }

        // Nota (#198): se eliminó TieneTodasLasLineasConVistoBueno_AlgunaLineaConVistoBuenoNull_RetornaFalse
        // porque LinPedidoVta.VtoBueno pasó a ser bool no-nullable; el caso "null" ya no existe y queda
        // cubierto por TieneTodasLasLineasConVistoBueno_AlgunaLineaSinVistoBueno_RetornaFalse (VtoBueno = false).

        [TestMethod]
        public void TieneTodasLasLineasConVistoBueno_PedidoSinLineas_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = new List<LinPedidoVta>()
            };

            // Act
            bool resultado = gestor.TieneTodasLasLineasConVistoBueno(pedido);

            // Assert
            Assert.IsTrue(resultado, "Pedido sin líneas se considera válido");
        }

        [TestMethod]
        public void TieneTodasLasLineasConVistoBueno_PedidoNull_RetornaFalse()
        {
            // Arrange
            CabPedidoVta pedido = null;

            // Act
            bool resultado = gestor.TieneTodasLasLineasConVistoBueno(pedido);

            // Assert
            Assert.IsFalse(resultado, "Pedido null retorna false");
        }

        #endregion


        #region ObtenerLineasProcesables Tests

        [TestMethod]
        public void ObtenerLineasProcesables_ConPedidoNull_RetornaListaVacia()
        {
            // Arrange
            CabPedidoVta pedido = null;
            DateTime fechaDesde = DateTime.Today;

            // Act
            var resultado = gestor.ObtenerLineasProcesables(pedido, fechaDesde);

            // Assert
            Assert.IsNotNull(resultado, "Debe retornar lista vacía, no null");
            Assert.AreEqual(0, resultado.Count, "Lista debe estar vacía");
        }

        [TestMethod]
        public void ObtenerLineasProcesables_SoloLineasConPickingYFechaValida_DevuelveSoloEsas()
        {
            // Arrange
            var hoy = DateTime.Today;
            var ayer = hoy.AddDays(-1);
            var manana = hoy.AddDays(1);

            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Número = 1, Nº_Orden = 1, Picking = 5, Fecha_Entrega = ayer },    // ✓ Válida
                    new LinPedidoVta { Número = 1, Nº_Orden = 2, Picking = null, Fecha_Entrega = ayer }, // ✗ Sin picking
                    new LinPedidoVta { Número = 1, Nº_Orden = 3, Picking = 0, Fecha_Entrega = ayer },    // ✗ Picking = 0
                    new LinPedidoVta { Número = 1, Nº_Orden = 4, Picking = 3, Fecha_Entrega = manana },  // ✗ Fecha futura
                    new LinPedidoVta { Número = 1, Nº_Orden = 5, Picking = 2, Fecha_Entrega = hoy }      // ✓ Válida
                }
            };

            // Act
            var resultado = gestor.ObtenerLineasProcesables(pedido, hoy);

            // Assert
            Assert.AreEqual(2, resultado.Count, "Solo 2 líneas son procesables");
            Assert.IsTrue(resultado.Any(l => l.Nº_Orden == 1), "Debe incluir línea 1");
            Assert.IsTrue(resultado.Any(l => l.Nº_Orden == 5), "Debe incluir línea 5");
            Assert.IsFalse(resultado.Any(l => l.Nº_Orden == 2), "NO debe incluir línea sin picking");
            Assert.IsFalse(resultado.Any(l => l.Nº_Orden == 3), "NO debe incluir línea con picking=0");
            Assert.IsFalse(resultado.Any(l => l.Nº_Orden == 4), "NO debe incluir línea con fecha futura");
        }

        #endregion

        #region ObtenerNumeroAlbaranExistente Tests

        [TestMethod]
        public void ObtenerNumeroAlbaranExistente_PedidoSinLineas_RetornaNull()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            DateTime fechaDesde = DateTime.Today;

            // Act
            var resultado = gestor.ObtenerNumeroAlbaranExistente(pedido, fechaDesde);

            // Assert
            Assert.IsNull(resultado, "Sin líneas procesables debe retornar null");
        }

        [TestMethod]
        public void ObtenerNumeroAlbaranExistente_AlgunaLineaProcesableSinAlbaran_RetornaNull()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Picking = 5, Fecha_Entrega = DateTime.Today, Estado = Constantes.EstadosLineaVenta.ALBARAN, Nº_Albarán = 100 },
                    new LinPedidoVta { Nº_Orden = 2, Picking = 3, Fecha_Entrega = DateTime.Today, Estado = Constantes.EstadosLineaVenta.EN_CURSO, Nº_Albarán = null } // Sin albarán
                }
            };
            DateTime fechaDesde = DateTime.Today;

            // Act
            var resultado = gestor.ObtenerNumeroAlbaranExistente(pedido, fechaDesde);

            // Assert
            Assert.IsNull(resultado, "Si alguna línea procesable no tiene albarán, retorna null");
        }

        [TestMethod]
        public void ObtenerNumeroAlbaranExistente_TodasLasLineasProcesablesTienenAlbaran_RetornaNumeroAlbaran()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Picking = 5, Fecha_Entrega = DateTime.Today, Estado = Constantes.EstadosLineaVenta.ALBARAN, Nº_Albarán = 100 },
                    new LinPedidoVta { Nº_Orden = 2, Picking = 3, Fecha_Entrega = DateTime.Today, Estado = Constantes.EstadosLineaVenta.ALBARAN, Nº_Albarán = 100 },
                    new LinPedidoVta { Nº_Orden = 3, Picking = null, Fecha_Entrega = DateTime.Today, Estado = Constantes.EstadosLineaVenta.EN_CURSO, Nº_Albarán = null } // No procesable
                }
            };
            DateTime fechaDesde = DateTime.Today;

            // Act
            var resultado = gestor.ObtenerNumeroAlbaranExistente(pedido, fechaDesde);

            // Assert
            Assert.IsNotNull(resultado, "Todas las líneas procesables tienen albarán");
            Assert.AreEqual(100, resultado.Value, "Debe retornar el número de albarán");
        }

        [TestMethod]
        public void ObtenerNumeroAlbaranExistente_LineasFuturasNoImportan_SoloVeProcesables()
        {
            // Arrange
            var hoy = DateTime.Today;
            var manana = hoy.AddDays(1);

            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Picking = 5, Fecha_Entrega = hoy, Estado = Constantes.EstadosLineaVenta.ALBARAN, Nº_Albarán = 100 },    // Procesable con albarán
                    new LinPedidoVta { Nº_Orden = 2, Picking = 3, Fecha_Entrega = manana, Estado = Constantes.EstadosLineaVenta.EN_CURSO, Nº_Albarán = null } // Futura, no procesable
                }
            };
            DateTime fechaDesde = hoy;

            // Act
            var resultado = gestor.ObtenerNumeroAlbaranExistente(pedido, fechaDesde);

            // Assert
            Assert.IsNotNull(resultado, "Líneas futuras no deben contar");
            Assert.AreEqual(100, resultado.Value, "Solo importa que la línea procesable (hoy) tenga albarán");
        }

        #endregion

        #region Grupo 7: DebeImprimirDocumento - Nuevas opciones

        [TestMethod]
        public void DebeImprimirDocumento_FacturaEnPapel_RetornaTrue()
        {
            // Arrange - Nueva opción añadida
            string comentarios = "El cliente solicita FACTURA EN PAPEL";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsTrue(resultado, "Debe detectar 'FACTURA EN PAPEL'");
        }

        [TestMethod]
        public void DebeImprimirDocumento_FacturaEnPapelMinusculas_RetornaTrue()
        {
            // Arrange
            string comentarios = "enviar factura en papel por favor";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsTrue(resultado, "Debe detectar 'factura en papel' en minúsculas");
        }

        [TestMethod]
        public void DebeImprimirDocumento_FacturaEnPapelConTextoAdicional_RetornaTrue()
        {
            // Arrange
            string comentarios = "Cliente VIP. Necesita factura en papel para contabilidad. Entregar antes de las 10.";

            // Act
            bool resultado = gestor.DebeImprimirDocumento(comentarios);

            // Assert
            Assert.IsTrue(resultado, "Debe detectar 'factura en papel' en medio de otros textos");
        }

        [TestMethod]
        public void DebeImprimirDocumento_TodasLasOpciones_RetornaTrue()
        {
            // Arrange - Test de todas las opciones
            var opciones = new[]
            {
                "FACTURA FÍSICA",
                "factura fisica",
                "FACTURA EN PAPEL",
                "factura en papel",
                "ALBARÁN FÍSICO",
                "albaran fisico"
            };

            // Act & Assert
            foreach (var opcion in opciones)
            {
                bool resultado = gestor.DebeImprimirDocumento(opcion);
                Assert.IsTrue(resultado, $"Debe detectar '{opcion}'");
            }
        }

        #endregion

        #region Grupo 8: HayDocumentosParaImprimir - NumeroCopias > 0

        [TestMethod]
        public void HayDocumentosParaImprimir_ConNumeroCopiasCero_RetornaFalse()
        {
            // Arrange - REGRESIÓN: Antes devolvía true si DatosImpresion != null
            var documentos = new NestoAPI.Models.PedidosVenta.DocumentosImpresionPedidoDTO();
            documentos.Facturas.Add(new FacturaCreadaDTO
            {
                NumeroFactura = "A25/001",
                DatosImpresion = new DocumentoParaImprimir
                {
                    BytesPDF = new byte[] { 1, 2, 3 },
                    NumeroCopias = 0, // Cero copias
                    TipoBandeja = TipoBandejaImpresion.Middle
                }
            });

            // Act
            bool resultado = documentos.HayDocumentosParaImprimir;

            // Assert
            Assert.IsFalse(resultado, "NO debe haber documentos para imprimir si NumeroCopias = 0");
        }

        [TestMethod]
        public void HayDocumentosParaImprimir_ConNumeroCopiasMayorQueCero_RetornaTrue()
        {
            // Arrange
            var documentos = new NestoAPI.Models.PedidosVenta.DocumentosImpresionPedidoDTO();
            documentos.Facturas.Add(new FacturaCreadaDTO
            {
                NumeroFactura = "A25/001",
                DatosImpresion = new DocumentoParaImprimir
                {
                    BytesPDF = new byte[] { 1, 2, 3 },
                    NumeroCopias = 1, // Al menos 1 copia
                    TipoBandeja = TipoBandejaImpresion.Middle
                }
            });

            // Act
            bool resultado = documentos.HayDocumentosParaImprimir;

            // Assert
            Assert.IsTrue(resultado, "Debe haber documentos para imprimir si NumeroCopias > 0");
        }

        [TestMethod]
        public void TotalDocumentosParaImprimir_ConNumeroCopiasCero_RetornaCero()
        {
            // Arrange
            var documentos = new NestoAPI.Models.PedidosVenta.DocumentosImpresionPedidoDTO();
            documentos.Facturas.Add(new FacturaCreadaDTO
            {
                NumeroFactura = "A25/001",
                DatosImpresion = new DocumentoParaImprimir
                {
                    BytesPDF = new byte[] { 1, 2, 3 },
                    NumeroCopias = 0,
                    TipoBandeja = TipoBandejaImpresion.Middle
                }
            });

            // Act
            int total = documentos.TotalDocumentosParaImprimir;

            // Assert
            Assert.AreEqual(0, total, "Total debe ser 0 si NumeroCopias = 0");
        }

        #endregion

        #region Re-facturación de Albaranes NRM Tests

        [TestMethod]
        public void ObtenerNumeroAlbaranExistente_PedidoConAlbaranNRM_PermiteRefacturar()
        {
            // Arrange: Pedido NRM con albarán pero sin factura
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 903241,
                Periodo_Facturacion = "NRM",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Nº_Orden = 1,
                        Picking = 5,
                        Fecha_Entrega = DateTime.Today,
                        Estado = Constantes.EstadosLineaVenta.ALBARAN, // Ya tiene albarán
                        Nº_Albarán = 707567
                    },
                    new LinPedidoVta
                    {
                        Nº_Orden = 2,
                        Picking = 3,
                        Fecha_Entrega = DateTime.Today,
                        Estado = Constantes.EstadosLineaVenta.ALBARAN, // Ya tiene albarán
                        Nº_Albarán = 707567
                    }
                }
            };
            DateTime fechaDesde = DateTime.Today;

            // Act
            var numeroAlbaran = gestor.ObtenerNumeroAlbaranExistente(pedido, fechaDesde);

            // Assert
            Assert.IsNotNull(numeroAlbaran, "Debe detectar que ya tiene albarán");
            Assert.AreEqual(707567, numeroAlbaran.Value, "Debe retornar el número de albarán existente");
            // NOTA: En el flujo real, al detectar albarán existente, NO creará albarán nuevo
            // y continuará con la facturación NRM
        }

        #endregion

        #region Grupo 10: Tests de ServicioFacturas con NVEntities inyectado

        /// <summary>
        /// Verifica que ServicioFacturas puede recibir un NVEntities externo.
        /// Esto es crítico para evitar conflictos de concurrencia.
        /// </summary>
        [TestMethod]
        public void ServicioFacturas_ConDbExterno_UsaElMismoContexto()
        {
            // Arrange
            var dbExterno = A.Fake<NVEntities>();

            // Act
            var servicio = new ServicioFacturas(dbExterno);

            // Assert - El servicio debe haberse creado sin errores
            Assert.IsNotNull(servicio);
        }

        /// <summary>
        /// Verifica que ServicioFacturas sin parámetro sigue funcionando (backward compatibility).
        /// </summary>
        [TestMethod]
        public void ServicioFacturas_SinParametro_CreaContextoInterno()
        {
            // Act
            var servicio = new ServicioFacturas();

            // Assert - El servicio debe haberse creado sin errores
            Assert.IsNotNull(servicio);
        }

        #endregion

        #region Grupo 11: Tests de ServicioAlbaranesVenta con NVEntities inyectado

        /// <summary>
        /// Verifica que ServicioAlbaranesVenta puede recibir un NVEntities externo.
        /// Esto es crítico para evitar conflictos de concurrencia.
        /// </summary>
        [TestMethod]
        public void ServicioAlbaranesVenta_ConDbExterno_UsaElMismoContexto()
        {
            // Arrange
            var dbExterno = A.Fake<NVEntities>();

            // Act
            var servicio = new ServicioAlbaranesVenta(dbExterno);

            // Assert - El servicio debe haberse creado sin errores
            Assert.IsNotNull(servicio);
        }

        /// <summary>
        /// Verifica que ServicioAlbaranesVenta sin parámetro sigue funcionando (backward compatibility).
        /// </summary>
        [TestMethod]
        public void ServicioAlbaranesVenta_SinParametro_CreaContextoInterno()
        {
            // Act
            var servicio = new ServicioAlbaranesVenta();

            // Assert - El servicio debe haberse creado sin errores
            Assert.IsNotNull(servicio);
        }

        #endregion

        #region Grupo 12: Tests de LimpiarContextoDespuesDeError

        /// <summary>
        /// Verifica que LimpiarContextoDespuesDeError no lanza excepciones cuando el pedido es null.
        /// Esto es importante porque el método se llama en el catch y no debe fallar.
        /// </summary>
        [TestMethod]
        public void LimpiarContextoDespuesDeError_ConPedidoNull_NoLanzaExcepcion()
        {
            // Arrange
            CabPedidoVta pedidoNull = null;

            // Act & Assert - No debe lanzar excepción
            gestor.LimpiarContextoDespuesDeError(pedidoNull);
        }

        /// <summary>
        /// Verifica que LimpiarContextoDespuesDeError maneja correctamente un pedido sin líneas.
        /// </summary>
        [TestMethod]
        public void LimpiarContextoDespuesDeError_ConPedidoSinLineas_NoLanzaExcepcion()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = null
            };

            // Act & Assert - No debe lanzar excepción
            gestor.LimpiarContextoDespuesDeError(pedido);
        }

        /// <summary>
        /// Verifica que LimpiarContextoDespuesDeError limpia el contexto sin errores cuando hay líneas.
        /// </summary>
        [TestMethod]
        public void LimpiarContextoDespuesDeError_ConPedidoYLineas_NoLanzaExcepcion()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Base_Imponible = 100m },
                    new LinPedidoVta { Nº_Orden = 2, Base_Imponible = 200m }
                }
            };

            // Act & Assert - No debe lanzar excepción
            gestor.LimpiarContextoDespuesDeError(pedido);
        }

        /// <summary>
        /// Verifica que después de llamar a LimpiarContextoDespuesDeError,
        /// el método puede llamarse múltiples veces sin problemas (idempotencia).
        /// </summary>
        [TestMethod]
        public void LimpiarContextoDespuesDeError_LlamarMultiplesVeces_NoLanzaExcepcion()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 1, Base_Imponible = 100m }
                }
            };

            // Act & Assert - Llamar múltiples veces no debe causar problemas
            gestor.LimpiarContextoDespuesDeError(pedido);
            gestor.LimpiarContextoDespuesDeError(pedido);
            gestor.LimpiarContextoDespuesDeError(pedido);
        }

        #endregion

        #region Grupo 13: Detección de Severidad (Issue #267)

        /// <summary>
        /// Verifica que un mensaje sin prefijo se considera Error por defecto.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridadPorPrefijo_SinPrefijo_DevuelveError()
        {
            // Arrange
            string mensaje = "Este es un mensaje de error normal";

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridadPorPrefijo(ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Error, severidad);
            Assert.AreEqual("Este es un mensaje de error normal", mensaje, "El mensaje no debe modificarse");
        }

        /// <summary>
        /// Verifica que el prefijo [WARNING] se detecta y elimina del mensaje.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridadPorPrefijo_ConWarning_DevuelveWarningYQuitaPrefijo()
        {
            // Arrange
            string mensaje = "[WARNING] No se puede facturar porque tiene MantenerJunto=1";

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridadPorPrefijo(ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Warning, severidad);
            Assert.AreEqual("No se puede facturar porque tiene MantenerJunto=1", mensaje, "Debe quitar el prefijo [WARNING]");
        }

        /// <summary>
        /// Verifica que el prefijo [WARNING] es case-insensitive.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridadPorPrefijo_ConWarningMinusculas_DevuelveWarning()
        {
            // Arrange
            string mensaje = "[warning] Este es un aviso";

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridadPorPrefijo(ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Warning, severidad);
            Assert.AreEqual("Este es un aviso", mensaje);
        }

        /// <summary>
        /// Verifica que el prefijo [INFO] se detecta correctamente.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridadPorPrefijo_ConInfo_DevuelveInfoYQuitaPrefijo()
        {
            // Arrange
            string mensaje = "[INFO] Información adicional sobre el proceso";

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridadPorPrefijo(ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Info, severidad);
            Assert.AreEqual("Información adicional sobre el proceso", mensaje);
        }

        /// <summary>
        /// Verifica que un mensaje null o vacío devuelve Error.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridadPorPrefijo_MensajeNullOVacio_DevuelveError()
        {
            // Arrange
            string mensajeNull = null;
            string mensajeVacio = "";

            // Act
            var severidadNull = GestorFacturacionRutas.DetectarSeveridadPorPrefijo(ref mensajeNull);
            var severidadVacio = GestorFacturacionRutas.DetectarSeveridadPorPrefijo(ref mensajeVacio);

            // Assert
            Assert.AreEqual(NivelSeveridad.Error, severidadNull);
            Assert.AreEqual(NivelSeveridad.Error, severidadVacio);
        }

        /// <summary>
        /// Verifica que DetectarSeveridad detecta el prefijo [WARNING] en excepciones normales.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridad_ExcepcionConPrefijoWarning_DevuelveWarning()
        {
            // Arrange
            var ex = new Exception("[WARNING] El albarán se creó pero la factura queda pendiente");
            string mensaje = ex.Message;

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridad(ex, ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Warning, severidad);
            Assert.AreEqual("El albarán se creó pero la factura queda pendiente", mensaje);
        }

        /// <summary>
        /// Verifica que DetectarSeveridad devuelve Error para excepciones normales sin prefijo.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridad_ExcepcionSinPrefijo_DevuelveError()
        {
            // Arrange
            var ex = new Exception("Error de base de datos");
            string mensaje = ex.Message;

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridad(ex, ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Error, severidad);
            Assert.AreEqual("Error de base de datos", mensaje);
        }

        /// <summary>
        /// Verifica que el prefijo tiene prioridad sobre el State de SqlException.
        /// Si el mensaje tiene [WARNING], debe devolver Warning aunque SqlException tenga state=1.
        /// </summary>
        [TestMethod]
        public void DetectarSeveridad_PrefijoTienePrioridadSobreSqlState()
        {
            // Arrange - Excepción normal con prefijo (simula el caso real)
            var ex = new Exception("[WARNING] Mensaje con prefijo");
            string mensaje = ex.Message;

            // Act
            var severidad = GestorFacturacionRutas.DetectarSeveridad(ex, ref mensaje);

            // Assert
            Assert.AreEqual(NivelSeveridad.Warning, severidad, "El prefijo debe tener prioridad");
            Assert.AreEqual("Mensaje con prefijo", mensaje);
        }

        #endregion

        #region Helpers

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
