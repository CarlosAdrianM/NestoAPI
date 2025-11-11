using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorFacturacionRutasTests
    {
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

        #region Grupo 3: Validación MantenerJunto

        [TestMethod]
        public void PuedeFacturarPedido_MantenerJuntoConLineasSinAlbaran_RetornaFalse()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                MantenerJunto = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.EN_CURSO, VtoBueno = true }, // Estado 1 < 2
                    new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.ALBARAN, VtoBueno = true }   // Estado 2
                }
            };

            // Act
            bool resultado = gestor.PuedeFacturarPedido(pedido);

            // Assert
            Assert.IsFalse(resultado, "No debe poder facturar: MantenerJunto=true y hay líneas sin albarán (Estado < 2)");
        }

        [TestMethod]
        public void PuedeFacturarPedido_MantenerJuntoTodasConAlbaran_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                MantenerJunto = true,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.ALBARAN, VtoBueno = true },   // Estado 2
                    new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.FACTURA, VtoBueno = true }    // Estado 4
                }
            };

            // Act
            bool resultado = gestor.PuedeFacturarPedido(pedido);

            // Assert
            Assert.IsTrue(resultado, "Debe poder facturar: MantenerJunto=true pero todas las líneas tienen albarán (Estado >= 2)");
        }

        [TestMethod]
        public void PuedeFacturarPedido_NoMantenerJunto_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                MantenerJunto = false,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.EN_CURSO, VtoBueno = true }, // Estado 1 < 2
                    new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.PENDIENTE, VtoBueno = true } // Estado -1 < 2
                }
            };

            // Act
            bool resultado = gestor.PuedeFacturarPedido(pedido);

            // Assert
            Assert.IsTrue(resultado, "Debe poder facturar: MantenerJunto=false (no importa el estado de las líneas)");
        }

        [TestMethod]
        public void PuedeFacturarPedido_MantenerJuntoSinLineas_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                MantenerJunto = true,
                LinPedidoVtas = new List<LinPedidoVta>() // Sin líneas
            };

            // Act
            bool resultado = gestor.PuedeFacturarPedido(pedido);

            // Assert
            Assert.IsTrue(resultado, "Debe poder facturar: sin líneas no hay restricciones");
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
                Cliente = "1001",
                Contacto = "0",
                NombreCliente = "Cliente Test",
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
                Cliente = "1002",
                Contacto = "0",
                NombreCliente = "Cliente FDM",
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
                Cliente = "1003",
                Contacto = "0",
                NombreCliente = "Cliente Nota Entrega",
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
                Cliente = "1004",
                Contacto = "0",
                NombreCliente = "Cliente MantenerJunto",
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
                    Cliente = "1001",
                    Contacto = "0",
                    NombreCliente = "Cliente 1",
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
                    Cliente = "1002",
                    Contacto = "0",
                    NombreCliente = "Cliente 2",
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
                    Cliente = "1003",
                    Contacto = "0",
                    NombreCliente = "Cliente 3",
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
                Cliente = "1005",
                Contacto = "0",
                NombreCliente = "Cliente Sin Líneas",
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
            Assert.AreEqual(1, resultado.NumeroAlbaranes);
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
                    Cliente = "1001",
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
                    Cliente = "1002",
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

        [TestMethod]
        public void TieneTodasLasLineasConVistoBueno_AlgunaLineaConVistoBuenoNull_RetornaFalse()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { VtoBueno = true },
                    new LinPedidoVta { VtoBueno = null }, // Una con null
                    new LinPedidoVta { VtoBueno = true }
                }
            };

            // Act
            bool resultado = gestor.TieneTodasLasLineasConVistoBueno(pedido);

            // Assert
            Assert.IsFalse(resultado, "Hay al menos una línea con VtoBueno = null (no es true)");
        }

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
    }
}
