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

        #region Grupo 2: Facturación después de crear albarán con MantenerJunto

        [TestMethod]
        public async Task FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto_CreaAlbaranYFactura()
        {
            // Arrange - Pedido NRM con MantenerJunto=1, con 2 líneas:
            // - Una línea EN_CURSO (se albaranará)
            // - Otra línea ya ALBARAN (ya albaranada)
            // Después de crear el albarán, TODAS las líneas tendrán Estado >= 2,
            // por lo que DEBERÍA crear la factura

            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                Nº_Cliente = "1001",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, // "NRM"
                MantenerJunto = true,
                NotaEntrega = false,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO, // Estado 1 - se albaranará
                        VtoBueno = true,
                        Base_Imponible = 100m
                    },
                    new LinPedidoVta
                    {
                        Estado = Constantes.EstadosLineaVenta.ALBARAN, // Estado 2 - ya albaranada
                        VtoBueno = true,
                        Base_Imponible = 50m
                    }
                }
            };

            var pedidos = new List<CabPedidoVta> { pedido };

            // Configurar mock del servicio de albaranes
            // Después de crear el albarán (número 1001), TODAS las líneas deben tener Estado >= 2
            A.CallTo(() => servicioAlbaranes.CrearAlbaran("1", 12345, "usuario"))
                .Returns(Task.FromResult(1001))
                .Invokes(() =>
                {
                    // Simular que el albarán se crea y actualiza el estado de la línea EN_CURSO a ALBARAN
                    pedido.LinPedidoVtas[0].Estado = Constantes.EstadosLineaVenta.ALBARAN;
                });

            // Configurar mock del servicio de facturas
            A.CallTo(() => servicioFacturas.CrearFactura("1", 12345, "usuario"))
                .Returns(Task.FromResult("A25/123"));

            // Configurar mock de traspaso (no debe traspasar)
            A.CallTo(() => servicioTraspaso.HayQueTraspasar(pedido))
                .Returns(false);

            // Configurar mock de SaveChangesAsync
            A.CallTo(() => db.SaveChangesAsync())
                .Returns(Task.FromResult(0));

            // Act
            var response = await gestor.FacturarRutas(pedidos, "usuario");

            // Assert
            Assert.AreEqual(1, response.PedidosProcesados, "Debe procesar 1 pedido");
            Assert.AreEqual(1, response.Albaranes.Count, "Debe crear 1 albarán");
            Assert.AreEqual(1, response.Facturas.Count, "Debe crear 1 factura (ESTE TEST FALLA ACTUALMENTE)");
            Assert.AreEqual(0, response.PedidosConErrores.Count, "No debe haber errores");

            // Verificar que se llamó a CrearAlbaran
            A.CallTo(() => servicioAlbaranes.CrearAlbaran("1", 12345, "usuario"))
                .MustHaveHappenedOnceExactly();

            // Verificar que se llamó a CrearFactura (ESTO FALLA ACTUALMENTE)
            A.CallTo(() => servicioFacturas.CrearFactura("1", 12345, "usuario"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturarRutas_PedidoNRMMantenerJuntoQueSigueIncompleto_CreaSoloAlbaranConError()
        {
            // Arrange - Pedido NRM con MantenerJunto=1, con 2 líneas:
            // - Una línea EN_CURSO (se albaranará)
            // - Otra línea PENDIENTE (NO se albaranará)
            // Después de crear el albarán, sigue habiendo líneas sin albarán,
            // por lo que NO debe crear la factura

            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12346,
                Nº_Cliente = "1002",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, // "NRM"
                MantenerJunto = true,
                NotaEntrega = false,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO, // Estado 1 - se albaranará
                        VtoBueno = true,
                        Base_Imponible = 100m
                    },
                    new LinPedidoVta
                    {
                        Estado = Constantes.EstadosLineaVenta.PENDIENTE, // Estado -1 - NO se albaranará
                        VtoBueno = true,
                        Base_Imponible = 50m
                    }
                }
            };

            var pedidos = new List<CabPedidoVta> { pedido };

            // Configurar mock del servicio de albaranes
            // Después de crear el albarán, solo la primera línea cambia a ALBARAN
            A.CallTo(() => servicioAlbaranes.CrearAlbaran("1", 12346, "usuario"))
                .Returns(Task.FromResult(1002))
                .Invokes(() =>
                {
                    // Simular que el albarán solo actualiza la línea EN_CURSO
                    pedido.LinPedidoVtas[0].Estado = Constantes.EstadosLineaVenta.ALBARAN;
                    // La línea PENDIENTE sigue en PENDIENTE
                });

            // Configurar mock de traspaso
            A.CallTo(() => servicioTraspaso.HayQueTraspasar(pedido))
                .Returns(false);

            // Configurar mock de SaveChangesAsync
            A.CallTo(() => db.SaveChangesAsync())
                .Returns(Task.FromResult(0));

            // Act
            var response = await gestor.FacturarRutas(pedidos, "usuario");

            // Assert
            Assert.AreEqual(1, response.PedidosProcesados, "Debe procesar 1 pedido");
            Assert.AreEqual(1, response.Albaranes.Count, "Debe crear 1 albarán");
            Assert.AreEqual(0, response.Facturas.Count, "NO debe crear factura (quedan líneas pendientes)");
            Assert.AreEqual(1, response.PedidosConErrores.Count, "Debe registrar 1 error");

            // Verificar el mensaje de error
            var error = response.PedidosConErrores[0];
            Assert.AreEqual(12346, error.NumeroPedido);
            Assert.AreEqual("Factura", error.TipoError);
            Assert.IsTrue(error.MensajeError.Contains("MantenerJunto=1"));
            Assert.IsTrue(error.MensajeError.Contains("1 línea(s) sin albarán"));

            // Verificar que NO se llamó a CrearFactura
            A.CallTo(() => servicioFacturas.CrearFactura(A<string>._, A<int>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarRutas_PedidoNRMMantenerJuntoTodasLineasAlbaranadasAntes_CreaAlbaranYFactura()
        {
            // Arrange - Pedido NRM con MantenerJunto=1, con 2 líneas:
            // - Ambas líneas ya tienen Estado = ALBARAN
            // Por lo tanto, DEBE poder facturar inmediatamente

            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12347,
                Nº_Cliente = "1003",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, // "NRM"
                MantenerJunto = true,
                NotaEntrega = false,
                Comentarios = "",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Estado = Constantes.EstadosLineaVenta.ALBARAN, // Ya albaranada
                        VtoBueno = true,
                        Base_Imponible = 100m
                    },
                    new LinPedidoVta
                    {
                        Estado = Constantes.EstadosLineaVenta.ALBARAN, // Ya albaranada
                        VtoBueno = true,
                        Base_Imponible = 50m
                    }
                }
            };

            var pedidos = new List<CabPedidoVta> { pedido };

            // Configurar mock del servicio de albaranes
            A.CallTo(() => servicioAlbaranes.CrearAlbaran("1", 12347, "usuario"))
                .Returns(Task.FromResult(1003));

            // Configurar mock del servicio de facturas
            A.CallTo(() => servicioFacturas.CrearFactura("1", 12347, "usuario"))
                .Returns(Task.FromResult("A25/124"));

            // Configurar mock de traspaso
            A.CallTo(() => servicioTraspaso.HayQueTraspasar(pedido))
                .Returns(false);

            // Configurar mock de SaveChangesAsync
            A.CallTo(() => db.SaveChangesAsync())
                .Returns(Task.FromResult(0));

            // Act
            var response = await gestor.FacturarRutas(pedidos, "usuario");

            // Assert
            Assert.AreEqual(1, response.PedidosProcesados, "Debe procesar 1 pedido");
            Assert.AreEqual(1, response.Albaranes.Count, "Debe crear 1 albarán");
            Assert.AreEqual(1, response.Facturas.Count, "Debe crear 1 factura");
            Assert.AreEqual(0, response.PedidosConErrores.Count, "No debe haber errores");

            // Verificar que se llamó a CrearFactura
            A.CallTo(() => servicioFacturas.CrearFactura("1", 12347, "usuario"))
                .MustHaveHappenedOnceExactly();
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

        #region Grupo 6: ObtenerDocumentosImpresion

        [TestMethod]
        public async Task ObtenerDocumentosImpresion_PedidoNRMConFactura_RetornaFacturaYDatosImpresion()
        {
            // Arrange
            string empresa = "1";
            int numeroPedido = 12345;
            string numeroFactura = "A25/123";
            int? numeroAlbaran = 1001;

            var pedido = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = "1001",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = false,
                Comentarios = "FACTURA FÍSICA requerida",
                Cliente = new Cliente
                {
                    Nº_Cliente = "1001",
                    Nombre = "Cliente Test",
                    ClienteGrupo = new ClienteGrupo
                    {
                        Grupo = "RU",
                        Copias = 2,
                        Bandeja = 2 // Middle
                    }
                }
            };

            var dbSetPedido = A.Fake<IDbSet<CabPedidoVta>>();
            A.CallTo(() => dbSetPedido.FindAsync(empresa, numeroPedido))
                .Returns(Task.FromResult(pedido));
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSetPedido);

            // Configurar generación de datos de impresión de factura
            A.CallTo(() => gestorFacturas.GenerarDatosImpresionFactura(
                A<string>._, A<int>._, A<string>._, A<string>._, A<bool>._))
                .Returns(new DatosImpresionDocumento
                {
                    ContenidoPdf = new byte[] { 1, 2, 3 },
                    NumeroCopias = 2,
                    Bandeja = System.Drawing.Printing.PaperSourceKind.Middle
                });

            // Act
            var resultado = await gestor.ObtenerDocumentosImpresion(empresa, numeroPedido, numeroFactura, numeroAlbaran);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Facturas.Count, "Debe contener 1 factura");
            Assert.AreEqual(numeroFactura, resultado.Facturas[0].NumeroFactura);
            Assert.IsNotNull(resultado.Facturas[0].DatosImpresion, "Debe tener datos de impresión");
            Assert.AreEqual(2, resultado.Facturas[0].DatosImpresion.NumeroCopias);
            Assert.AreEqual(0, resultado.Albaranes.Count, "No debe generar albarán para NRM");
            Assert.AreEqual(0, resultado.NotasEntrega.Count, "No debe generar nota de entrega");
            Assert.IsTrue(resultado.HayDocumentosParaImprimir);
        }

        [TestMethod]
        public async Task ObtenerDocumentosImpresion_PedidoFDMConAlbaran_RetornaAlbaranYDatosImpresion()
        {
            // Arrange
            string empresa = "1";
            int numeroPedido = 12346;
            string numeroFactura = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES; // "FDM"
            int? numeroAlbaran = 1002;

            var pedido = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = "1002",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                NotaEntrega = false,
                Comentarios = "ALBARÁN FÍSICO necesario",
                Cliente = new Cliente
                {
                    Nº_Cliente = "1002",
                    Nombre = "Cliente FDM",
                    ClienteGrupo = new ClienteGrupo
                    {
                        Grupo = "RU",
                        Copias = 1,
                        Bandeja = 1 // Upper
                    }
                }
            };

            var dbSetPedido = A.Fake<IDbSet<CabPedidoVta>>();
            A.CallTo(() => dbSetPedido.FindAsync(empresa, numeroPedido))
                .Returns(Task.FromResult(pedido));
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSetPedido);

            // Configurar generación de datos de impresión de albarán
            A.CallTo(() => servicioAlbaranes.GenerarDatosImpresionAlbaran(
                A<string>._, A<int>._, A<int>._, A<string>._, A<bool>._))
                .Returns(new DatosImpresionDocumento
                {
                    ContenidoPdf = new byte[] { 4, 5, 6 },
                    NumeroCopias = 1,
                    Bandeja = System.Drawing.Printing.PaperSourceKind.Upper
                });

            // Act
            var resultado = await gestor.ObtenerDocumentosImpresion(empresa, numeroPedido, numeroFactura, numeroAlbaran);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Facturas.Count, "No debe generar factura para FDM");
            Assert.AreEqual(1, resultado.Albaranes.Count, "Debe contener 1 albarán");
            Assert.AreEqual(numeroAlbaran.Value, resultado.Albaranes[0].NumeroAlbaran);
            Assert.IsNotNull(resultado.Albaranes[0].DatosImpresion, "Debe tener datos de impresión");
            Assert.AreEqual(1, resultado.Albaranes[0].DatosImpresion.NumeroCopias);
            Assert.AreEqual(0, resultado.NotasEntrega.Count, "No debe generar nota de entrega");
            Assert.IsTrue(resultado.HayDocumentosParaImprimir);
        }

        [TestMethod]
        public async Task ObtenerDocumentosImpresion_PedidoNotaEntrega_RetornaNotaEntregaYDatosImpresion()
        {
            // Arrange
            string empresa = "1";
            int numeroPedido = 12347;
            string numeroFactura = null;
            int? numeroAlbaran = null;

            var pedido = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = "1003",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = true,
                Comentarios = "Nota de entrega solicitada",
                Cliente = new Cliente
                {
                    Nº_Cliente = "1003",
                    Nombre = "Cliente Nota Entrega",
                    ClienteGrupo = new ClienteGrupo
                    {
                        Grupo = "RU",
                        Copias = 1,
                        Bandeja = 1
                    }
                }
            };

            var dbSetPedido = A.Fake<IDbSet<CabPedidoVta>>();
            A.CallTo(() => dbSetPedido.FindAsync(empresa, numeroPedido))
                .Returns(Task.FromResult(pedido));
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSetPedido);

            // Configurar generación de datos de impresión de nota de entrega
            A.CallTo(() => servicioNotasEntrega.GenerarDatosImpresionNotaEntrega(
                A<string>._, A<int>._, A<string>._, A<bool>._))
                .Returns(new DatosImpresionDocumento
                {
                    ContenidoPdf = new byte[] { 7, 8, 9 },
                    NumeroCopias = 1,
                    Bandeja = System.Drawing.Printing.PaperSourceKind.Upper
                });

            // Act
            var resultado = await gestor.ObtenerDocumentosImpresion(empresa, numeroPedido, numeroFactura, numeroAlbaran);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Facturas.Count, "No debe generar factura");
            Assert.AreEqual(0, resultado.Albaranes.Count, "No debe generar albarán");
            Assert.AreEqual(1, resultado.NotasEntrega.Count, "Debe contener 1 nota de entrega");
            Assert.IsNotNull(resultado.NotasEntrega[0].DatosImpresion, "Debe tener datos de impresión");
            Assert.AreEqual(1, resultado.NotasEntrega[0].DatosImpresion.NumeroCopias);
            Assert.IsTrue(resultado.HayDocumentosParaImprimir);
        }

        [TestMethod]
        public async Task ObtenerDocumentosImpresion_SinComentarioImpresion_RetornaSinDatosImpresion()
        {
            // Arrange
            string empresa = "1";
            int numeroPedido = 12348;
            string numeroFactura = "A25/124";
            int? numeroAlbaran = 1003;

            var pedido = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = "1004",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = false,
                Comentarios = "Pedido normal sin solicitud de impresión", // No contiene palabras clave
                Cliente = new Cliente
                {
                    Nº_Cliente = "1004",
                    Nombre = "Cliente Sin Impresión"
                }
            };

            var dbSetPedido = A.Fake<IDbSet<CabPedidoVta>>();
            A.CallTo(() => dbSetPedido.FindAsync(empresa, numeroPedido))
                .Returns(Task.FromResult(pedido));
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSetPedido);

            // Act
            var resultado = await gestor.ObtenerDocumentosImpresion(empresa, numeroPedido, numeroFactura, numeroAlbaran);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Facturas.Count, "Debe contener la factura");
            Assert.IsNull(resultado.Facturas[0].DatosImpresion, "NO debe tener datos de impresión");
            Assert.IsFalse(resultado.HayDocumentosParaImprimir, "No hay documentos para imprimir");
            Assert.AreEqual(0, resultado.TotalDocumentosParaImprimir);
        }

        [TestMethod]
        public async Task ObtenerDocumentosImpresion_PedidoNoEncontrado_RetornaListasVacias()
        {
            // Arrange
            string empresa = "1";
            int numeroPedido = 99999;
            string numeroFactura = "A25/125";
            int? numeroAlbaran = 1004;

            var dbSetPedido = A.Fake<IDbSet<CabPedidoVta>>();
            A.CallTo(() => dbSetPedido.FindAsync(empresa, numeroPedido))
                .Returns(Task.FromResult<CabPedidoVta>(null)); // Pedido no existe
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSetPedido);

            // Act
            var resultado = await gestor.ObtenerDocumentosImpresion(empresa, numeroPedido, numeroFactura, numeroAlbaran);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Facturas.Count);
            Assert.AreEqual(0, resultado.Albaranes.Count);
            Assert.AreEqual(0, resultado.NotasEntrega.Count);
            Assert.IsFalse(resultado.HayDocumentosParaImprimir);
        }

        [TestMethod]
        public async Task ObtenerDocumentosImpresion_ConVariasCopias_RetornaTotalDocumentosCorrect()
        {
            // Arrange
            string empresa = "1";
            int numeroPedido = 12349;
            string numeroFactura = "A25/126";
            int? numeroAlbaran = 1005;

            var pedido = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = "1005",
                Contacto = "0",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = false,
                Comentarios = "FACTURA FÍSICA con varias copias",
                Cliente = new Cliente
                {
                    Nº_Cliente = "1005",
                    Nombre = "Cliente Multi-Copia",
                    ClienteGrupo = new ClienteGrupo
                    {
                        Grupo = "RU",
                        Copias = 3, // 3 copias
                        Bandeja = 2
                    }
                }
            };

            var dbSetPedido = A.Fake<IDbSet<CabPedidoVta>>();
            A.CallTo(() => dbSetPedido.FindAsync(empresa, numeroPedido))
                .Returns(Task.FromResult(pedido));
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSetPedido);

            A.CallTo(() => gestorFacturas.GenerarDatosImpresionFactura(
                A<string>._, A<int>._, A<string>._, A<string>._, A<bool>._))
                .Returns(new DatosImpresionDocumento
                {
                    ContenidoPdf = new byte[] { 1, 2, 3 },
                    NumeroCopias = 3,
                    Bandeja = System.Drawing.Printing.PaperSourceKind.Middle
                });

            // Act
            var resultado = await gestor.ObtenerDocumentosImpresion(empresa, numeroPedido, numeroFactura, numeroAlbaran);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Facturas.Count);
            Assert.AreEqual(3, resultado.Facturas[0].DatosImpresion.NumeroCopias);
            Assert.AreEqual(3, resultado.TotalDocumentosParaImprimir, "Debe contar 3 copias");
        }

        #endregion
    }
}
