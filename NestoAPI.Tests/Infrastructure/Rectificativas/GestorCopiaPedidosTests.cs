using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Rectificativas
{
    /// <summary>
    /// Tests para GestorCopiaPedidos (Issue #85)
    /// </summary>
    [TestClass]
    public class GestorCopiaPedidosTests
    {
        #region Tests de validacion de request

        [TestMethod]
        public void CopiarFacturaRequest_EsCambioCliente_DevuelveTrueSiClienteDestinoDiferente()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = "18456"
            };

            // Act & Assert
            Assert.IsTrue(request.EsCambioCliente);
        }

        [TestMethod]
        public void CopiarFacturaRequest_EsCambioCliente_DevuelveFalseSiClienteDestinoIgual()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = "15234"
            };

            // Act & Assert
            Assert.IsFalse(request.EsCambioCliente);
        }

        [TestMethod]
        public void CopiarFacturaRequest_EsCambioCliente_DevuelveFalseSiClienteDestinoNulo()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = null
            };

            // Act & Assert
            Assert.IsFalse(request.EsCambioCliente);
        }

        [TestMethod]
        public void CopiarFacturaRequest_EsCambioCliente_DevuelveFalseSiClienteDestinoVacio()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = ""
            };

            // Act & Assert
            Assert.IsFalse(request.EsCambioCliente);
        }

        [TestMethod]
        public void CopiarFacturaRequest_EsCambioCliente_IgnoraEspacios()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = "  15234  "
            };

            // Act & Assert
            Assert.IsFalse(request.EsCambioCliente, "Debe ignorar espacios al comparar clientes");
        }

        [TestMethod]
        public void CopiarFacturaRequest_ValoresPorDefecto_SonCorrectos()
        {
            // Arrange & Act
            var request = new CopiarFacturaRequest();

            // Assert
            Assert.IsTrue(request.MantenerCondicionesOriginales, "Por defecto debe mantener condiciones originales");
            Assert.IsFalse(request.InvertirCantidades, "Por defecto no invierte cantidades");
            Assert.IsFalse(request.AnadirAPedidoOriginal, "Por defecto crea pedido nuevo");
            Assert.IsFalse(request.CrearAlbaranYFactura, "Por defecto no crea albaran/factura automaticamente");
            Assert.IsTrue(request.UsarVendedorFacturaOriginal, "Por defecto usa vendedor de la factura original");
        }

        #endregion

        #region Tests de LineaCopiadaDTO

        [TestMethod]
        public void LineaCopiadaDTO_ContieneMetadataParaVinculacion()
        {
            // Arrange & Act
            var linea = new LineaCopiadaDTO
            {
                NumeroLineaNueva = 1,
                Producto = "PROD01",
                Descripcion = "Producto de prueba",
                FacturaOrigen = "NV26/001234",
                LineaOrigen = 5,
                CantidadOriginal = 10,
                CantidadCopiada = -10,
                PrecioUnitario = 25.50m,
                BaseImponible = 255.00m
            };

            // Assert - Verificar que tiene todos los campos necesarios para #38
            Assert.AreEqual("NV26/001234", linea.FacturaOrigen);
            Assert.AreEqual(5, linea.LineaOrigen);
            Assert.AreEqual(10, linea.CantidadOriginal);
            Assert.AreEqual(-10, linea.CantidadCopiada);
        }

        [TestMethod]
        public void LineaCopiadaDTO_CantidadCopiada_PuedeSerNegativa()
        {
            // Arrange & Act
            var linea = new LineaCopiadaDTO
            {
                CantidadOriginal = 5,
                CantidadCopiada = -5  // Rectificativa
            };

            // Assert
            Assert.IsTrue(linea.CantidadCopiada < 0, "Las rectificativas tienen cantidad negativa");
            Assert.AreEqual(-linea.CantidadOriginal, linea.CantidadCopiada);
        }

        [TestMethod]
        public void LineaCopiadaDTO_BaseImponible_CalculaCorrectamente()
        {
            // Arrange & Act
            var linea = new LineaCopiadaDTO
            {
                CantidadCopiada = -3,
                PrecioUnitario = 100m,
                BaseImponible = -300m
            };

            // Assert
            Assert.AreEqual(linea.CantidadCopiada * linea.PrecioUnitario, linea.BaseImponible);
        }

        #endregion

        #region Tests de CopiarFacturaResponse

        [TestMethod]
        public void CopiarFacturaResponse_InicializaListaVacia()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse();

            // Assert
            Assert.IsNotNull(response.LineasCopiadas);
            Assert.AreEqual(0, response.LineasCopiadas.Count);
        }

        [TestMethod]
        public void CopiarFacturaResponse_ContieneInformacionCompleta()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse
            {
                NumeroPedido = 12345,
                NumeroAlbaran = 67890,
                NumeroFactura = "NV26/001235",
                Exitoso = true,
                Mensaje = "Operacion completada",
                LineasCopiadas = new List<LineaCopiadaDTO>
                {
                    new LineaCopiadaDTO { NumeroLineaNueva = 1, Producto = "PROD01" }
                }
            };

            // Assert
            Assert.AreEqual(12345, response.NumeroPedido);
            Assert.AreEqual(67890, response.NumeroAlbaran);
            Assert.AreEqual("NV26/001235", response.NumeroFactura);
            Assert.IsTrue(response.Exitoso);
            Assert.AreEqual(1, response.LineasCopiadas.Count);
        }

        [TestMethod]
        public void CopiarFacturaResponse_NumeroAlbaran_PuedeSerNull()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse
            {
                NumeroPedido = 12345,
                NumeroAlbaran = null, // No se creo albaran
                Exitoso = true
            };

            // Assert
            Assert.IsNull(response.NumeroAlbaran);
            Assert.IsTrue(response.Exitoso);
        }

        [TestMethod]
        public void CopiarFacturaResponse_NumeroFactura_PuedeSerNull()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse
            {
                NumeroPedido = 12345,
                NumeroFactura = null, // No se creo factura
                Exitoso = true
            };

            // Assert
            Assert.IsNull(response.NumeroFactura);
            Assert.IsTrue(response.Exitoso);
        }

        #endregion

        #region Tests de escenarios de negocio

        [TestMethod]
        public void Escenario_RectificativaMismoCliente_DebeFacturarNegativas()
        {
            // Documenta el comportamiento esperado:
            // Cuando es el mismo cliente y se invierten cantidades (rectificativa),
            // se debe crear albaran y factura automaticamente.

            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = null, // Mismo cliente
                InvertirCantidades = true, // Rectificativa
                CrearAlbaranYFactura = true
            };

            // Verificar condiciones
            Assert.IsFalse(request.EsCambioCliente, "Es el mismo cliente");
            Assert.IsTrue(request.InvertirCantidades, "Es rectificativa");
            Assert.IsTrue(request.CrearAlbaranYFactura, "Debe facturar automaticamente");
        }

        [TestMethod]
        public void Escenario_CargoMismoCliente_NoDebeFacturar()
        {
            // Documenta el comportamiento esperado:
            // Cuando es el mismo cliente y NO se invierten cantidades (cargo),
            // NO se debe crear albaran ni factura (para permitir modificaciones).

            var request = new CopiarFacturaRequest
            {
                Cliente = "15234",
                ClienteDestino = null, // Mismo cliente
                InvertirCantidades = false, // Cargo (cantidades positivas)
                CrearAlbaranYFactura = true // Aunque pida facturar...
            };

            // La logica del gestor debe ignorar CrearAlbaranYFactura
            // cuando es mismo cliente + cargo (positivo)
            Assert.IsFalse(request.EsCambioCliente, "Es el mismo cliente");
            Assert.IsFalse(request.InvertirCantidades, "Es cargo (positivo)");
        }

        [TestMethod]
        public void Escenario_TraspasoClienteAClienteB()
        {
            // Documenta el flujo completo de traspaso:
            // 1. Rectificativa al cliente A (abono)
            // 2. Cargo al cliente B (nueva factura)

            // Paso 1: Rectificativa a cliente A
            var requestAbono = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234", // Cliente A
                NumeroFactura = "NV26/001234",
                InvertirCantidades = true,
                AnadirAPedidoOriginal = false,
                MantenerCondicionesOriginales = true,
                CrearAlbaranYFactura = true,
                ClienteDestino = null // Mismo cliente A
            };

            Assert.IsFalse(requestAbono.EsCambioCliente);
            Assert.IsTrue(requestAbono.InvertirCantidades);

            // Paso 2: Cargo a cliente B
            var requestCargo = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234", // Cliente A (origen)
                NumeroFactura = "NV26/001234",
                InvertirCantidades = false, // Cantidades positivas
                AnadirAPedidoOriginal = false,
                MantenerCondicionesOriginales = false, // Recalcular para cliente B
                CrearAlbaranYFactura = true,
                ClienteDestino = "18456", // Cliente B
                ContactoDestino = "0"
            };

            Assert.IsTrue(requestCargo.EsCambioCliente);
            Assert.IsFalse(requestCargo.InvertirCantidades);
            Assert.IsFalse(requestCargo.MantenerCondicionesOriginales);
        }

        [TestMethod]
        public void Escenario_CopiaSinFacturar_ParaRevision()
        {
            // Documenta el caso de uso:
            // El usuario quiere copiar una factura para revisar/modificar antes de procesar

            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = "NV26/001234",
                InvertirCantidades = false, // Copia normal
                AnadirAPedidoOriginal = false, // Pedido nuevo
                MantenerCondicionesOriginales = true, // Mismas condiciones
                CrearAlbaranYFactura = false // NO facturar automaticamente
            };

            Assert.IsFalse(request.CrearAlbaranYFactura, "No debe facturar automaticamente");
            Assert.IsFalse(request.InvertirCantidades, "Copia normal, no rectificativa");
        }

        [TestMethod]
        public void Escenario_AnadirAPedidoExistente()
        {
            // Documenta el caso de uso:
            // Anadir lineas de una factura a un pedido ya existente

            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = "NV26/001234",
                InvertirCantidades = true, // Rectificativa
                AnadirAPedidoOriginal = true, // Al pedido original
                MantenerCondicionesOriginales = true,
                CrearAlbaranYFactura = false // No facturar para revisar primero
            };

            Assert.IsTrue(request.AnadirAPedidoOriginal, "Debe anadir al pedido original");
            Assert.IsTrue(request.InvertirCantidades, "Es rectificativa");
        }

        [TestMethod]
        public void Escenario_CambioClienteConRecalculo()
        {
            // Documenta el caso de uso:
            // Traspasar factura a otro cliente recalculando sus condiciones

            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234", // Cliente origen
                NumeroFactura = "NV26/001234",
                InvertirCantidades = false, // Copia normal (cargo)
                AnadirAPedidoOriginal = false,
                MantenerCondicionesOriginales = false, // Recalcular para nuevo cliente
                CrearAlbaranYFactura = true,
                ClienteDestino = "18456", // Cliente destino diferente
                ContactoDestino = "0"
            };

            Assert.IsTrue(request.EsCambioCliente);
            Assert.IsFalse(request.MantenerCondicionesOriginales, "Debe recalcular condiciones");
            Assert.IsTrue(request.CrearAlbaranYFactura, "Facturar automaticamente");
        }

        #endregion

        #region Tests de inversion de cantidades

        [TestMethod]
        public void InvertirCantidades_True_CantidadesDebenSerNegativas()
        {
            // Arrange
            decimal cantidadOriginal = 5;

            // Act - Simula la logica de inversion
            decimal cantidadInvertida = -cantidadOriginal;

            // Assert
            Assert.IsTrue(cantidadInvertida < 0, "La cantidad invertida debe ser negativa");
            Assert.AreEqual(-5, cantidadInvertida);
        }

        [TestMethod]
        public void InvertirCantidades_False_CantidadesDebenSerPositivas()
        {
            // Arrange
            decimal cantidadOriginal = 5;

            // Act - Sin inversion
            decimal cantidadCopiada = cantidadOriginal;

            // Assert
            Assert.IsTrue(cantidadCopiada > 0, "La cantidad copiada debe ser positiva");
            Assert.AreEqual(5, cantidadCopiada);
        }

        [TestMethod]
        public void InvertirCantidades_CantidadCero_PermaneceEnCero()
        {
            // Arrange
            decimal cantidadOriginal = 0;

            // Act
            decimal cantidadInvertida = -cantidadOriginal;

            // Assert
            Assert.AreEqual(0, cantidadInvertida);
        }

        #endregion

        #region Tests de mensajes de respuesta

        [TestMethod]
        public void Response_Exitoso_MensajeDescriptivo()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse
            {
                Exitoso = true,
                NumeroPedido = 12345,
                NumeroAlbaran = 67890,
                NumeroFactura = "NV26/001235",
                Mensaje = "Rectificativa creada: Pedido 12345, Albaran 67890, Factura NV26/001235. 3 lineas procesadas.",
                LineasCopiadas = new List<LineaCopiadaDTO>
                {
                    new LineaCopiadaDTO { NumeroLineaNueva = 1 },
                    new LineaCopiadaDTO { NumeroLineaNueva = 2 },
                    new LineaCopiadaDTO { NumeroLineaNueva = 3 }
                }
            };

            // Assert
            Assert.IsTrue(response.Mensaje.Contains("Pedido 12345"));
            Assert.IsTrue(response.Mensaje.Contains("Albaran 67890"));
            Assert.IsTrue(response.Mensaje.Contains("Factura NV26/001235"));
            Assert.IsTrue(response.Mensaje.Contains("3 lineas"));
        }

        [TestMethod]
        public void Response_Error_MensajeConDetalle()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse
            {
                Exitoso = false,
                Mensaje = "Error al copiar factura: No se encontraron lineas para la factura NV26/999999"
            };

            // Assert
            Assert.IsFalse(response.Exitoso);
            Assert.IsTrue(response.Mensaje.StartsWith("Error"));
            Assert.IsTrue(response.Mensaje.Contains("NV26/999999"));
        }

        #endregion

        #region Tests de CrearAbonoYCargo (Issue #85 - Abono+Cargo en un clic)

        [TestMethod]
        public void CopiarFacturaRequest_CrearAbonoYCargo_PorDefectoEsFalse()
        {
            // Arrange & Act
            var request = new CopiarFacturaRequest();

            // Assert
            Assert.IsFalse(request.CrearAbonoYCargo, "Por defecto CrearAbonoYCargo debe ser false");
        }

        [TestMethod]
        public void CopiarFacturaRequest_CrearAbonoYCargo_RequiereClienteDestino()
        {
            // Documenta que CrearAbonoYCargo requiere ClienteDestino y ContactoDestino
            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = "NV26/001234",
                CrearAbonoYCargo = true,
                ClienteDestino = "15234",  // Mismo cliente, diferente contacto
                ContactoDestino = "1"      // Nuevo contacto con direccion correcta
            };

            // Assert
            Assert.IsTrue(request.CrearAbonoYCargo);
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.ClienteDestino),
                "ClienteDestino es requerido para CrearAbonoYCargo");
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.ContactoDestino),
                "ContactoDestino es requerido para CrearAbonoYCargo");
        }

        [TestMethod]
        public void Escenario_CorregirDireccion_ConAbonoYCargo()
        {
            // Documenta el caso de uso principal:
            // El cliente dice que la direccion de la factura esta mal.
            // 1. Se crea un nuevo contacto con la direccion correcta
            // 2. Se usa CrearAbonoYCargo para hacer rectificativa + factura nueva

            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "12345",               // Cliente original
                NumeroFactura = "NV26/001234",   // Factura con direccion erronea
                CrearAbonoYCargo = true,         // Hacer abono + cargo
                ClienteDestino = "12345",        // Mismo cliente
                ContactoDestino = "1",           // Nuevo contacto con direccion correcta
                MantenerCondicionesOriginales = true
            };

            // El gestor debe:
            // 1. Crear rectificativa al cliente 12345/0 (direccion original erronea)
            // 2. Crear factura al cliente 12345/1 (direccion correcta)

            Assert.IsTrue(request.CrearAbonoYCargo);
            Assert.AreEqual(request.Cliente, request.ClienteDestino,
                "En correccion de direccion, el cliente es el mismo");
            Assert.AreNotEqual("0", request.ContactoDestino,
                "El contacto destino debe ser diferente (nueva direccion)");
        }

        [TestMethod]
        public void Escenario_TraspasoCompleto_ConAbonoYCargo()
        {
            // Documenta el caso de uso:
            // Traspasar una factura de cliente A a cliente B en un solo clic

            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",               // Cliente A (origen)
                NumeroFactura = "NV26/001234",
                CrearAbonoYCargo = true,         // Abono a A + Cargo a B
                ClienteDestino = "18456",        // Cliente B (destino)
                ContactoDestino = "0",
                MantenerCondicionesOriginales = false  // Recalcular para cliente B
            };

            Assert.IsTrue(request.CrearAbonoYCargo);
            Assert.AreNotEqual(request.Cliente, request.ClienteDestino,
                "En traspaso, los clientes son diferentes");
        }

        #endregion

        #region Tests de OperacionFacturaDTO

        [TestMethod]
        public void OperacionFacturaDTO_InicializaListaLineasVacia()
        {
            // Arrange & Act
            var operacion = new OperacionFacturaDTO();

            // Assert
            Assert.IsNotNull(operacion.Lineas);
            Assert.AreEqual(0, operacion.Lineas.Count);
        }

        [TestMethod]
        public void OperacionFacturaDTO_ContieneInformacionCompleta()
        {
            // Arrange & Act
            var operacion = new OperacionFacturaDTO
            {
                Cliente = "12345",
                Contacto = "0",
                NumeroPedido = 98765,
                NumeroAlbaran = 11111,
                NumeroFactura = "NV26/001235",
                Lineas = new List<LineaCopiadaDTO>
                {
                    new LineaCopiadaDTO { NumeroLineaNueva = 1, Producto = "PROD01" },
                    new LineaCopiadaDTO { NumeroLineaNueva = 2, Producto = "PROD02" }
                }
            };

            // Assert
            Assert.AreEqual("12345", operacion.Cliente);
            Assert.AreEqual("0", operacion.Contacto);
            Assert.AreEqual(98765, operacion.NumeroPedido);
            Assert.AreEqual(11111, operacion.NumeroAlbaran);
            Assert.AreEqual("NV26/001235", operacion.NumeroFactura);
            Assert.AreEqual(2, operacion.Lineas.Count);
        }

        #endregion

        #region Tests de CopiarFacturaResponse con Abono y Cargo

        [TestMethod]
        public void CopiarFacturaResponse_ConAbonoYCargo_ContieneAmbosResultados()
        {
            // Arrange & Act
            var response = new CopiarFacturaResponse
            {
                Exitoso = true,
                Mensaje = "Abono+Cargo completado",
                Abono = new OperacionFacturaDTO
                {
                    Cliente = "12345",
                    Contacto = "0",
                    NumeroPedido = 100001,
                    NumeroAlbaran = 200001,
                    NumeroFactura = "NV26/001235"
                },
                Cargo = new OperacionFacturaDTO
                {
                    Cliente = "12345",
                    Contacto = "1",
                    NumeroPedido = 100002,
                    NumeroAlbaran = 200002,
                    NumeroFactura = "NV26/001236"
                }
            };

            // Assert
            Assert.IsNotNull(response.Abono, "Debe tener informacion del abono");
            Assert.IsNotNull(response.Cargo, "Debe tener informacion del cargo");
            Assert.AreEqual("NV26/001235", response.Abono.NumeroFactura);
            Assert.AreEqual("NV26/001236", response.Cargo.NumeroFactura);
            Assert.AreEqual(response.Abono.Cliente, response.Cargo.Cliente,
                "En correccion de direccion, el cliente es el mismo");
            Assert.AreNotEqual(response.Abono.Contacto, response.Cargo.Contacto,
                "Los contactos deben ser diferentes");
        }

        [TestMethod]
        public void CopiarFacturaResponse_SinAbonoYCargo_AbonoYCargoSonNull()
        {
            // Arrange & Act - Operacion normal sin AbonoYCargo
            var response = new CopiarFacturaResponse
            {
                Exitoso = true,
                NumeroPedido = 12345,
                NumeroFactura = "NV26/001234"
                // Abono y Cargo no se establecen
            };

            // Assert
            Assert.IsNull(response.Abono, "En operacion normal, Abono debe ser null");
            Assert.IsNull(response.Cargo, "En operacion normal, Cargo debe ser null");
        }

        [TestMethod]
        public void CopiarFacturaResponse_AbonoYCargo_MensajeDescriptivo()
        {
            // Arrange
            var response = new CopiarFacturaResponse
            {
                Exitoso = true,
                Mensaje = "Abono+Cargo completado. ABONO: Factura NV26/001235 (cliente 12345/0). CARGO: Factura NV26/001236 (cliente 12345/1). 5 lineas procesadas.",
                Abono = new OperacionFacturaDTO { NumeroFactura = "NV26/001235" },
                Cargo = new OperacionFacturaDTO { NumeroFactura = "NV26/001236" }
            };

            // Assert
            Assert.IsTrue(response.Mensaje.Contains("Abono+Cargo"));
            Assert.IsTrue(response.Mensaje.Contains("ABONO"));
            Assert.IsTrue(response.Mensaje.Contains("CARGO"));
            Assert.IsTrue(response.Mensaje.Contains(response.Abono.NumeroFactura));
            Assert.IsTrue(response.Mensaje.Contains(response.Cargo.NumeroFactura));
        }

        [TestMethod]
        public void CopiarFacturaResponse_AbonoYCargo_ErrorEnCargo_MensajeDeAdvertencia()
        {
            // Documenta el comportamiento cuando el abono se crea pero el cargo falla
            var response = new CopiarFacturaResponse
            {
                Exitoso = false,
                Mensaje = "ATENCION: Abono creado (NV26/001235) pero error al crear cargo: Cliente destino no encontrado",
                Abono = new OperacionFacturaDTO { NumeroFactura = "NV26/001235" },
                Cargo = null  // No se pudo crear
            };

            // Assert
            Assert.IsFalse(response.Exitoso);
            Assert.IsTrue(response.Mensaje.Contains("ATENCION"));
            Assert.IsTrue(response.Mensaje.Contains("Abono creado"));
            Assert.IsTrue(response.Mensaje.Contains("error al crear cargo"));
            Assert.IsNotNull(response.Abono, "El abono si se creo");
            Assert.IsNull(response.Cargo, "El cargo no se pudo crear");
        }

        #endregion

        #region Tests de validacion de campos requeridos

        [TestMethod]
        public void Request_ClienteDestinoConContacto_EsValido()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = "NV26/001234",
                ClienteDestino = "18456",
                ContactoDestino = "0"
            };

            // Assert
            Assert.IsTrue(request.EsCambioCliente);
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.ContactoDestino),
                "Contacto destino es requerido cuando hay cambio de cliente");
        }

        [TestMethod]
        public void Request_NumeroFactura_FormatoEsperado()
        {
            // Arrange
            var request = new CopiarFacturaRequest
            {
                NumeroFactura = "NV26/001234"
            };

            // Assert - El formato esperado es SERIE/NUMERO
            Assert.IsTrue(request.NumeroFactura.Contains("/"),
                "El numero de factura debe contener / como separador");
        }

        #endregion

        #region Tests de validacion de lineas para copia

        [TestMethod]
        public void ValidarLineasParaCopia_LineaConYaFacturadoTrue_DebeRetornarError()
        {
            // Arrange
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Producto = "PROD01", YaFacturado = false, Recoger = 0 },
                new LinPedidoVta { Producto = "PROD02", YaFacturado = true, Recoger = 0 },  // YaFacturado!
                new LinPedidoVta { Producto = "PROD03", YaFacturado = false, Recoger = 0 }
            };

            // Act
            var lineasYaFacturadas = lineas.Where(l => l.YaFacturado).ToList();

            // Assert
            Assert.IsTrue(lineasYaFacturadas.Any(), "Debe detectar lineas con YaFacturado=true");
            Assert.AreEqual(1, lineasYaFacturadas.Count);
            Assert.AreEqual("PROD02", lineasYaFacturadas.First().Producto);
        }

        [TestMethod]
        public void ValidarLineasParaCopia_LineaConRecogerDistintoDeCero_DebeRetornarError()
        {
            // Arrange
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Producto = "PROD01", YaFacturado = false, Recoger = 0 },
                new LinPedidoVta { Producto = "PROD02", YaFacturado = false, Recoger = 2 },  // Recoger != 0
                new LinPedidoVta { Producto = "PROD03", YaFacturado = false, Recoger = 0 }
            };

            // Act
            var lineasConRecoger = lineas.Where(l => l.Recoger != 0).ToList();

            // Assert
            Assert.IsTrue(lineasConRecoger.Any(), "Debe detectar lineas con Recoger != 0");
            Assert.AreEqual(1, lineasConRecoger.Count);
            Assert.AreEqual("PROD02", lineasConRecoger.First().Producto);
        }

        [TestMethod]
        public void ValidarLineasParaCopia_TodasLasLineasValidas_NoDebeRetornarError()
        {
            // Arrange
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Producto = "PROD01", YaFacturado = false, Recoger = 0 },
                new LinPedidoVta { Producto = "PROD02", YaFacturado = false, Recoger = 0 },
                new LinPedidoVta { Producto = "PROD03", YaFacturado = false, Recoger = 0 }
            };

            // Act
            var lineasYaFacturadas = lineas.Where(l => l.YaFacturado).ToList();
            var lineasConRecoger = lineas.Where(l => l.Recoger != 0).ToList();

            // Assert
            Assert.IsFalse(lineasYaFacturadas.Any(), "No debe haber lineas con YaFacturado=true");
            Assert.IsFalse(lineasConRecoger.Any(), "No debe haber lineas con Recoger != 0");
        }

        [TestMethod]
        public void ValidarLineasParaCopia_RecogerNegativo_TambienDebeRetornarError()
        {
            // Arrange - Recoger negativo tambien es != 0
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Producto = "PROD01", YaFacturado = false, Recoger = -1 }
            };

            // Act
            var lineasConRecoger = lineas.Where(l => l.Recoger != 0).ToList();

            // Assert
            Assert.IsTrue(lineasConRecoger.Any(), "Recoger negativo tambien debe detectarse");
        }

        #endregion

        #region Tests de copia de campos adicionales

        [TestMethod]
        public void CopiarLinea_DebeCopiarCentroCoste()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { CentroCoste = "CC1" };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { CentroCoste = lineaOrigen.CentroCoste };

            // Assert
            Assert.AreEqual("CC1", lineaNueva.CentroCoste);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarDepartamento()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { Departamento = "DEP" };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { Departamento = lineaOrigen.Departamento };

            // Assert
            Assert.AreEqual("DEP", lineaNueva.Departamento);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarNumeroOferta()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { NºOferta = 12345 };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { NºOferta = lineaOrigen.NºOferta };

            // Assert
            Assert.AreEqual(12345, lineaNueva.NºOferta);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarGeneraBonificacion()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { GeneraBonificación = true };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { GeneraBonificación = lineaOrigen.GeneraBonificación };

            // Assert
            Assert.IsTrue(lineaNueva.GeneraBonificación);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarReponer()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { Reponer = true };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { Reponer = lineaOrigen.Reponer };

            // Assert
            Assert.IsTrue(lineaNueva.Reponer);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarEstadoProducto()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { EstadoProducto = 2 };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { EstadoProducto = lineaOrigen.EstadoProducto };

            // Assert
            Assert.AreEqual(2, lineaNueva.EstadoProducto);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarNumSerie()
        {
            // Arrange
            var lineaOrigen = new LinPedidoVta { NumSerie = "SN123456" };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { NumSerie = lineaOrigen.NumSerie };

            // Assert
            Assert.AreEqual("SN123456", lineaNueva.NumSerie);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarLineaParcial()
        {
            // Arrange - LineaParcial puede ser true o false, debe copiarse
            var lineaOrigenParcial = new LinPedidoVta { LineaParcial = true };
            var lineaOrigenNoParcial = new LinPedidoVta { LineaParcial = false };

            // Act - Simula la copia
            var lineaNuevaParcial = new LinPedidoVta { LineaParcial = lineaOrigenParcial.LineaParcial };
            var lineaNuevaNoParcial = new LinPedidoVta { LineaParcial = lineaOrigenNoParcial.LineaParcial };

            // Assert
            Assert.IsTrue(lineaNuevaParcial.LineaParcial, "LineaParcial=true debe copiarse");
            Assert.IsFalse(lineaNuevaNoParcial.LineaParcial, "LineaParcial=false debe copiarse");
        }

        [TestMethod]
        public void CopiarLinea_NoDebeCopiarRecoger()
        {
            // Arrange - Recoger NO debe copiarse (ya validamos que sea 0)
            var lineaOrigen = new LinPedidoVta { Recoger = 5 };

            // Act - En la copia, Recoger no se asigna (queda en valor por defecto)
            var lineaNueva = new LinPedidoVta();
            // NO hacemos: lineaNueva.Recoger = lineaOrigen.Recoger

            // Assert
            Assert.AreEqual(0, lineaNueva.Recoger, "Recoger no debe copiarse, queda en 0");
        }

        [TestMethod]
        public void CopiarLinea_CamposNulos_DebenCopiarseComoNulos()
        {
            // Arrange - Campos opcionales pueden ser null
            var lineaOrigen = new LinPedidoVta
            {
                CentroCoste = null,
                Departamento = null,
                NºOferta = null,
                EstadoProducto = null,
                NumSerie = null
            };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta
            {
                CentroCoste = lineaOrigen.CentroCoste,
                Departamento = lineaOrigen.Departamento,
                NºOferta = lineaOrigen.NºOferta,
                EstadoProducto = lineaOrigen.EstadoProducto,
                NumSerie = lineaOrigen.NumSerie
            };

            // Assert
            Assert.IsNull(lineaNueva.CentroCoste);
            Assert.IsNull(lineaNueva.Departamento);
            Assert.IsNull(lineaNueva.NºOferta);
            Assert.IsNull(lineaNueva.EstadoProducto);
            Assert.IsNull(lineaNueva.NumSerie);
        }

        [TestMethod]
        public void CopiarLinea_DebeCopiarDelegacion()
        {
            // Arrange - Delegacion ya se copiaba, verificamos que sigue funcionando
            var lineaOrigen = new LinPedidoVta { Delegación = "MAD" };

            // Act - Simula la copia
            var lineaNueva = new LinPedidoVta { Delegación = lineaOrigen.Delegación };

            // Assert
            Assert.AreEqual("MAD", lineaNueva.Delegación);
        }

        #endregion

        #region Tests de datos fiscales del abono (NestoAPI#268)

        // El abono es el espejo documental de la factura que anula: debe llevar los datos fiscales que
        // constaban en la factura original (dirección histórica), no los de la ficha actual del cliente.

        [TestMethod]
        public async Task CopiarDatosFiscalesDesdeFacturaOriginal_CopiaLosDatosDeLaFacturaRectificada()
        {
            // Arrange: la factura original tiene la dirección ANTIGUA (erronea); ServicioFacturas ya ha
            // rellenado el abono con la dirección NUEVA (de la ficha actual). Tras la corrección, el abono
            // debe quedar con la dirección ANTIGUA de la factura original.
            var facturaOriginal = new CabFacturaVta
            {
                Empresa = "1",
                Número = "NV26/001234",
                NombreFiscal = "CLIENTE EJEMPLO SL",
                CifNif = "B12345678",
                DireccionFiscal = "C/ Antigua, 1",
                CodPostalFiscal = "28001",
                PoblacionFiscal = "Madrid",
                ProvinciaFiscal = "Madrid"
            };
            var abono = new CabFacturaVta
            {
                Empresa = "1",
                Número = "NV26/005678",
                NombreFiscal = "CLIENTE EJEMPLO SL",
                CifNif = "B12345678",
                DireccionFiscal = "C/ Nueva, 99",
                CodPostalFiscal = "08001",
                PoblacionFiscal = "Barcelona",
                ProvinciaFiscal = "Barcelona"
            };
            var gestor = CrearGestorConFacturas(facturaOriginal, abono);

            // Act
            await gestor.CopiarDatosFiscalesDesdeFacturaOriginal("1", "NV26/001234", "NV26/005678");

            // Assert: el abono queda con los datos fiscales de la factura original (dirección antigua).
            Assert.AreEqual("C/ Antigua, 1", abono.DireccionFiscal);
            Assert.AreEqual("28001", abono.CodPostalFiscal);
            Assert.AreEqual("Madrid", abono.PoblacionFiscal);
            Assert.AreEqual("Madrid", abono.ProvinciaFiscal);
            Assert.AreEqual("CLIENTE EJEMPLO SL", abono.NombreFiscal);
            Assert.AreEqual("B12345678", abono.CifNif);
        }

        [TestMethod]
        public async Task CopiarDatosFiscalesDesdeFacturaOriginal_FacturaPreVerifactuSinDatos_ConservaElSnapshotActual()
        {
            // Arrange: factura original anterior a Verifactu (sin NombreFiscal persistido). El abono debe
            // conservar el snapshot que ya hizo ServicioFacturas desde la ficha actual (fallback).
            var facturaOriginal = new CabFacturaVta
            {
                Empresa = "1",
                Número = "NV20/000001",
                NombreFiscal = null // pre-Verifactu
            };
            var abono = new CabFacturaVta
            {
                Empresa = "1",
                Número = "NV26/005678",
                NombreFiscal = "CLIENTE EJEMPLO SL",
                DireccionFiscal = "C/ Actual, 5"
            };
            var gestor = CrearGestorConFacturas(facturaOriginal, abono);

            // Act
            await gestor.CopiarDatosFiscalesDesdeFacturaOriginal("1", "NV20/000001", "NV26/005678");

            // Assert: no se sobrescribe nada.
            Assert.AreEqual("CLIENTE EJEMPLO SL", abono.NombreFiscal);
            Assert.AreEqual("C/ Actual, 5", abono.DireccionFiscal);
        }

        // ===== NestoAPI#400: las validaciones que faltan datos son de NEGOCIO, no fallos =====
        // CopiarFactura tiene un catch que se lo traga TODO: llama a ElmahHelper.Notificar y devuelve
        // Exitoso=false. Ese Notificar es una llamada EXPLICITA y no pasa por GlobalExceptionFilter,
        // asi que el filtro de #361 no lo cubria. Ahora se aplica ahi la misma regla.
        // Caso real: Santiago, 20/08/26, dos intentos en un minuto porque el mensaje parecia un
        // error del sistema en vez de "te falta un dato".

        [TestMethod]
        public async Task CopiarFactura_AbonoYCargoSinClienteDestino_DevuelveElMensajeDeNegocioSinDisfrazarlo()
        {
            GestorCopiaPedidos gestor = CrearGestorConFacturas();
            var request = new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = "NV26/001234",
                CrearAbonoYCargo = true,
                ClienteDestino = null,
                ContactoDestino = null
            };

            CopiarFacturaResponse respuesta = await gestor.CopiarFactura(request, "Carlos");

            Assert.IsFalse(respuesta.Exitoso);
            Assert.IsTrue(respuesta.Mensaje.Contains("ClienteDestino"), respuesta.Mensaje);
            Assert.IsFalse(respuesta.Mensaje.StartsWith("Error al copiar factura"),
                $"Es una validacion de negocio, no un error del sistema: '{respuesta.Mensaje}'");
            Assert.IsFalse(respuesta.Mensaje.StartsWith("Error en operación"),
                $"Es una validacion de negocio, no un error del sistema: '{respuesta.Mensaje}'");
        }

        [TestMethod]
        public async Task CopiarFactura_SinDatosObligatorios_NoSeRegistranEnElmah()
        {
            // La regla se comparte con GlobalExceptionFilter para no tener dos criterios distintos.
            GestorCopiaPedidos gestor = CrearGestorConFacturas();

            foreach (CopiarFacturaRequest request in new[]
            {
                new CopiarFacturaRequest { Empresa = null, Cliente = "15234", NumeroFactura = "NV26/001234" },
                new CopiarFacturaRequest { Empresa = "1", Cliente = null, NumeroFactura = "NV26/001234" },
                new CopiarFacturaRequest { Empresa = "1", Cliente = "15234", NumeroFactura = null },
                new CopiarFacturaRequest { Empresa = "1", Cliente = "15234", NumeroFactura = "NV26/001234", ClienteDestino = "18456", ContactoDestino = null }
            })
            {
                CopiarFacturaResponse respuesta = await gestor.CopiarFactura(request, "Carlos");

                Assert.IsFalse(respuesta.Exitoso, respuesta.Mensaje);
                Assert.IsFalse(respuesta.Mensaje.StartsWith("Error al copiar factura"),
                    $"Deberia salir el mensaje de negocio tal cual: '{respuesta.Mensaje}'");
            }
        }

        [TestMethod]
        public void ValidacionesDeCopiaDeFactura_SonExcepcionesDeNegocio_YPorTantoNoVanAElmah()
        {
            // Lo que decide si va a ELMAH es DebeRegistrarseEnElmah, compartido con el filtro global.
            var sinDestino = new NestoBusinessException("Para la operación Abono+Cargo debe especificar ClienteDestino y ContactoDestino",
                new ErrorContext { ErrorCode = "ABONO_CARGO_SIN_DESTINO" });

            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, sinDestino.StatusCode);
            Assert.IsFalse(NestoAPI.Infraestructure.Filters.GlobalExceptionFilter.DebeRegistrarseEnElmah(sinDestino));
            // Un fallo de verdad si se sigue registrando.
            Assert.IsTrue(NestoAPI.Infraestructure.Filters.GlobalExceptionFilter.DebeRegistrarseEnElmah(
                new InvalidOperationException("La secuencia contiene mas de un elemento")));
        }

        // NestoAPI#538: la cabecera del pedido nuevo se guardaba ANTES de copiar las líneas
        // (SaveChanges en CrearPedidoNuevo). Si la copia de líneas fallaba después, quedaba en
        // CabPedidoVta una cabecera con cliente pero sin ninguna línea. Ahora la cabecera se guarda
        // en el mismo SaveChanges que las líneas: o entra todo, o no entra nada.

        [TestMethod]
        public async Task CopiarFactura_SiFallaLaCopiaDeLineas_NoGuardaLaCabeceraSinLineas()
        {
            var db = A.Fake<NVEntities>();
            var lineas = A.Fake<DbSet<LinPedidoVta>>(o =>
                o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            ConfigurarFakeDbSet(lineas, new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1", Número = 900001, Nº_Orden = 1, Nº_Factura = "NV26/000001",
                    Estado = Constantes.EstadosLineaVenta.FACTURA, TipoLinea = 1, Producto = "P1",
                    Cantidad = 1, Precio = 10, Contacto = "0", IVA = "G21"
                }
            }.AsQueryable());
            A.CallTo(() => lineas.Add(A<LinPedidoVta>._)).Throws(new InvalidOperationException("fallo al copiar la línea"));
            A.CallTo(() => db.LinPedidoVtas).Returns(lineas);

            var clientes = A.Fake<DbSet<Cliente>>(o =>
                o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            ConfigurarFakeDbSet(clientes, new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15234", Contacto = "0", IVA = "G21" }
            }.AsQueryable());
            A.CallTo(() => clientes.Include(A<string>._)).Returns(clientes);
            A.CallTo(() => db.Clientes).Returns(clientes);

            var cabeceras = A.Fake<DbSet<CabPedidoVta>>(o =>
                o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            ConfigurarFakeDbSet(cabeceras, new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = "1", Número = 900001, Serie = "NV", Nº_Cliente = "15234", Contacto = "0" }
            }.AsQueryable());
            CabPedidoVta cabeceraConstruida = null;
            A.CallTo(() => cabeceras.Add(A<CabPedidoVta>._)).ReturnsLazily((CabPedidoVta c) => cabeceraConstruida = c);
            A.CallTo(() => db.CabPedidoVtas).Returns(cabeceras);

            var plazos = A.Fake<DbSet<PlazoPago>>(o =>
                o.Implements<IQueryable<PlazoPago>>().Implements<IDbAsyncEnumerable<PlazoPago>>());
            ConfigurarFakeDbSet(plazos, new List<PlazoPago>().AsQueryable());
            A.CallTo(() => db.PlazosPago).Returns(plazos);

            A.CallTo(() => db.TomarSiguienteNumeroPedido()).Returns(927999);
            int guardados = 0;
            A.CallTo(() => db.SaveChangesAsync()).ReturnsLazily(() => { guardados++; return Task.FromResult(1); });
            A.CallTo(() => db.SaveChanges()).ReturnsLazily(() => { guardados++; return 1; });

            var gestor = new GestorCopiaPedidos(
                db,
                A.Fake<IServicioPedidosVenta>(),
                A.Fake<IServicioAlbaranesVenta>(),
                A.Fake<IServicioFacturas>());

            CopiarFacturaResponse respuesta = await gestor.CopiarFactura(new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = "NV26/000001"
            }, "Carlos");

            Assert.IsFalse(respuesta.Exitoso, respuesta.Mensaje);
            // La cabecera sí se llegó a construir (el fallo es en las líneas, no antes)...
            Assert.AreEqual(927999, cabeceraConstruida?.Número, respuesta.Mensaje);
            // ...pero no se ha guardado nada: no queda una cabecera huérfana en la BD.
            Assert.AreEqual(0, guardados, "Si fallan las líneas no se puede haber guardado la cabecera sola");
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        private static GestorCopiaPedidos CrearGestorConFacturas(params CabFacturaVta[] facturas)
        {
            var db = A.Fake<NVEntities>();
            var fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o =>
                o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            var data = facturas.AsQueryable();

            A.CallTo(() => ((IDbAsyncEnumerable<CabFacturaVta>)fakeFacturas).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<CabFacturaVta>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).Provider)
                .Returns(new TestDbAsyncQueryProvider<CabFacturaVta>(data.Provider));
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<CabFacturaVta>)fakeFacturas).GetEnumerator()).Returns(data.GetEnumerator());

            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(0));

            return new GestorCopiaPedidos(
                db,
                A.Fake<IServicioPedidosVenta>(),
                A.Fake<IServicioAlbaranesVenta>(),
                A.Fake<IServicioFacturas>());
        }

        #endregion

        #region Rehacer el pedido devuelto por la agencia (NestoAPI#699)

        // La agencia devuelve el pedido y el cliente lo sigue queriendo: al hacer el abono, el pedido
        // se rehace en el mismo paso, para el mismo cliente y con las mismas condiciones, y queda
        // pendiente de servir para que salga por el picking normal. Los portes «nos los comemos».

        private const string FACTURA_ORIGINAL = "NV26/000001";
        private const int PEDIDO_ORIGINAL = 900001;
        private const int PEDIDO_ABONO = 927001;
        private const int PEDIDO_SEGUNDO = 927002;

        private sealed class EscenarioCopia
        {
            public NVEntities Db;
            public IServicioPedidosVenta ServicioPedidos;
            public IServicioAlbaranesVenta ServicioAlbaranes;
            public IServicioFacturas ServicioFacturas;
            public IAlmacenRectificativasPendientes Pendientes;
            public List<CabPedidoVta> CabecerasNuevas = new List<CabPedidoVta>();
            public List<LinPedidoVta> LineasNuevas = new List<LinPedidoVta>();
            public List<VendedorPedidoGrupoProducto> VendedoresNuevos = new List<VendedorPedidoGrupoProducto>();

            public GestorCopiaPedidos Gestor() => new GestorCopiaPedidos(Db, ServicioPedidos, ServicioAlbaranes, ServicioFacturas, Pendientes);
            public CabPedidoVta Cabecera(int numero) => CabecerasNuevas.Single(c => c.Número == numero);
            public List<LinPedidoVta> Lineas(int numero) => LineasNuevas.Where(l => l.Número == numero).ToList();
        }

        private static DbSet<T> FakeDbSet<T>(IEnumerable<T> datos, List<T> anadidos) where T : class
        {
            var set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            ConfigurarFakeDbSet(set, datos.ToList().AsQueryable());
            A.CallTo(() => set.Add(A<T>._)).ReturnsLazily((T e) => { anadidos?.Add(e); return e; });
            return set;
        }

        private static EscenarioCopia CrearEscenarioFacturaDevuelta(bool conReembolso = false)
        {
            var e = new EscenarioCopia
            {
                Db = A.Fake<NVEntities>(),
                ServicioPedidos = A.Fake<IServicioPedidosVenta>(),
                ServicioAlbaranes = A.Fake<IServicioAlbaranesVenta>(),
                ServicioFacturas = A.Fake<IServicioFacturas>(),
                Pendientes = A.Fake<IAlmacenRectificativasPendientes>()
            };

            LinPedidoVta Linea(int orden, byte tipo, string producto, short cantidad, decimal precio, decimal descuento, string texto) => new LinPedidoVta
            {
                Empresa = "1", Número = PEDIDO_ORIGINAL, Nº_Orden = orden, Nº_Factura = FACTURA_ORIGINAL,
                Estado = Constantes.EstadosLineaVenta.FACTURA, TipoLinea = tipo, Producto = producto, Texto = texto,
                Cantidad = cantidad, Precio = precio, Descuento = descuento, DescuentoCliente = 0.05M, Aplicar_Dto = tipo == 1,
                Nº_Cliente = "15234", Contacto = "2", IVA = "G21", Almacén = "ALG", Forma_Venta = "VAR"
            };
            var lineasOriginales = new List<LinPedidoVta>
            {
                Linea(1, 1, "17404", 3, 12.5M, 0.10M, "CHAMPÚ"),
                Linea(2, 1, "29353", 2, 8M, 0M, "MASCARILLA"),
                Linea(3, 2, Constantes.Cuentas.CUENTA_PORTES_CEX, 1, 7M, 0M, "Portes")
            };
            if (conReembolso)
            {
                lineasOriginales.Add(Linea(4, 2, Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL, 1, 3M, 0M, Constantes.Portes.TEXTO_COMISION_REEMBOLSO));
            }
            A.CallTo(() => e.Db.LinPedidoVtas).Returns(FakeDbSet(lineasOriginales, e.LineasNuevas));

            var cliente = new Cliente { Empresa = "1", Nº_Cliente = "15234", Contacto = "2", IVA = "G21", Vendedor = "FICHA", CCC = "9", Ruta = "FW", PeriodoFacturación = "NRM" };
            var clientes = FakeDbSet(new[] { cliente }, null);
            A.CallTo(() => clientes.Include(A<string>._)).Returns(clientes);
            A.CallTo(() => e.Db.Clientes).Returns(clientes);

            var original = new CabPedidoVta
            {
                Empresa = "1", Número = PEDIDO_ORIGINAL, Nº_Cliente = "15234", Contacto = "2", Serie = "NV",
                Forma_Pago = "RCB", PlazosPago = "30D", Vendedor = "PA", CCC = "1", Ruta = "AT", Periodo_Facturacion = "FDM",
                Comentarios = "Llamar antes de ir", ComentarioPicking = "Frágil", IVA = "G21"
            };
            A.CallTo(() => e.Db.CabPedidoVtas).Returns(FakeDbSet(new[] { original }, e.CabecerasNuevas));

            A.CallTo(() => e.Db.PlazosPago).Returns(FakeDbSet(new[]
            {
                new PlazoPago { Empresa = "1", Número = "30D", DíasPrimerPlazo = 30 }
            }, null));
            A.CallTo(() => e.Db.VendedoresPedidosGruposProductos).Returns(FakeDbSet(new[]
            {
                new VendedorPedidoGrupoProducto { Empresa = "1", Pedido = PEDIDO_ORIGINAL, GrupoProducto = "PEL", Vendedor = "JE" }
            }, e.VendedoresNuevos));
            A.CallTo(() => e.Db.CabsFacturasVtas).Returns(FakeDbSet(new List<CabFacturaVta>(), null));
            A.CallTo(() => e.Db.LinFacturaVtaRectificaciones).Returns(FakeDbSet(new List<LinFacturaVtaRectificacion>(), null));

            A.CallTo(() => e.Db.TomarSiguienteNumeroPedido()).ReturnsNextFromSequence(PEDIDO_ABONO, PEDIDO_SEGUNDO);
            A.CallTo(() => e.Db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => e.ServicioPedidos.LeerParametroIVA(A<string>._, A<string>._, A<string>._))
                .Returns(new ParametroIVA { C__IVA = 21, C__RE = 0 });
            A.CallTo(() => e.ServicioAlbaranes.CrearAlbaran(A<string>._, A<int>._, A<string>._, A<DateTime?>._))
                .Returns(Task.FromResult(555));
            A.CallTo(() => e.ServicioFacturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._))
                .ReturnsLazily((string emp, int pedido, string u, string ua) => Task.FromResult(
                    new NestoAPI.Models.Facturas.CrearFacturaResponseDTO { NumeroFactura = pedido == PEDIDO_ABONO ? "RV26/000001" : "NV26/999999" }));
            return e;
        }

        private static CopiarFacturaRequest RequestRehacer(bool crearAlbaranYFactura = true, bool incluirPortes = false) => new CopiarFacturaRequest
        {
            Empresa = "1",
            Cliente = "15234",
            NumeroFactura = FACTURA_ORIGINAL,
            InvertirCantidades = true, // como viene del diálogo; se ignora
            CrearAlbaranYFactura = crearAlbaranYFactura,
            RehacerPedido = true,
            IncluirPortes = incluirPortes
        };

        [TestMethod]
        public void CopiarFacturaRequest_RehacerPedidoEIncluirPortes_PorDefectoSonFalse()
        {
            var request = new CopiarFacturaRequest();

            Assert.IsFalse(request.RehacerPedido);
            Assert.IsFalse(request.IncluirPortes, "Por defecto los portes del reenvío no se cobran otra vez");
        }

        [TestMethod]
        public async Task RehacerPedido_MismoCliente_HaceElAbonoYDejaElPedidoNuevoPendiente()
        {
            var e = CrearEscenarioFacturaDevuelta();

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestRehacer(), "Carlos");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            Assert.AreEqual(PEDIDO_ABONO, respuesta.Abono.NumeroPedido);
            Assert.AreEqual("RV26/000001", respuesta.Abono.NumeroFactura);
            Assert.AreEqual(PEDIDO_SEGUNDO, respuesta.PedidoRehecho.NumeroPedido);
            Assert.AreEqual(PEDIDO_SEGUNDO, respuesta.NumeroPedido, "«Abrir pedido» abre el pedido nuevo");
            Assert.IsNull(respuesta.Cargo);
            // El abono se albaranea y factura; el pedido nuevo, no: sale por el picking normal.
            A.CallTo(() => e.ServicioAlbaranes.CrearAlbaran("1", PEDIDO_ABONO, A<string>._, A<DateTime?>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => e.ServicioFacturas.CrearFactura("1", PEDIDO_ABONO, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => e.ServicioAlbaranes.CrearAlbaran(A<string>._, PEDIDO_SEGUNDO, A<string>._, A<DateTime?>._)).MustNotHaveHappened();
            A.CallTo(() => e.ServicioFacturas.CrearFactura(A<string>._, PEDIDO_SEGUNDO, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => e.ServicioFacturas.EnviarRectificativaAVerifactu("1", "RV26/000001")).MustHaveHappenedOnceExactly();

            List<LinPedidoVta> abono = e.Lineas(PEDIDO_ABONO);
            CollectionAssert.AreEqual(new short?[] { -3, -2, -1 }, abono.Select(l => l.Cantidad).ToList());
            Assert.AreEqual("RV", e.Cabecera(PEDIDO_ABONO).Serie);

            List<LinPedidoVta> nuevo = e.Lineas(PEDIDO_SEGUNDO);
            CollectionAssert.AreEqual(new short?[] { 3, 2, 1 }, nuevo.Select(l => l.Cantidad).ToList());
            Assert.IsTrue(nuevo.All(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO && l.Picking == 0),
                "El pedido nuevo queda por servir, sin picking");
        }

        [TestMethod]
        public async Task RehacerPedido_PedidoNuevo_LlevaLasMismasCondicionesQueElOriginal()
        {
            var e = CrearEscenarioFacturaDevuelta();

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestRehacer(), "Carlos");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            CabPedidoVta cab = e.Cabecera(PEDIDO_SEGUNDO);
            Assert.AreEqual("15234", cab.Nº_Cliente.Trim());
            Assert.AreEqual("2", cab.Contacto.Trim(), "Misma dirección de entrega");
            Assert.AreEqual("RCB", cab.Forma_Pago);
            Assert.AreEqual("30D", cab.PlazosPago);
            Assert.AreEqual(DateTime.Today.AddDays(30), cab.Primer_Vencimiento, "El vencimiento con los plazos del pedido");
            Assert.AreEqual("PA", cab.Vendedor);
            Assert.AreEqual("1", cab.CCC);
            Assert.AreEqual("AT", cab.Ruta);
            Assert.AreEqual("FDM", cab.Periodo_Facturacion);
            Assert.AreEqual("NV", cab.Serie);
            Assert.AreEqual("Frágil", cab.ComentarioPicking);
            Assert.IsNull(cab.PedidoOrigen, "PedidoOrigen es de las notas de entrega");
            StringAssert.Contains(cab.Comentarios, "[Rehecho de la factura NV26/000001 (pedido 900001), devuelto por la agencia]");
            StringAssert.Contains(cab.Comentarios, "Llamar antes de ir");

            List<LinPedidoVta> nuevo = e.Lineas(PEDIDO_SEGUNDO);
            LinPedidoVta champu = nuevo.Single(l => l.Producto == "17404");
            Assert.AreEqual(12.5M, champu.Precio);
            Assert.AreEqual(0.10M, champu.Descuento);
            Assert.AreEqual(0.05M, champu.DescuentoCliente);
            Assert.AreEqual(champu.Precio * champu.Cantidad, champu.Bruto, "CK_LinPedidoVta_5");

            VendedorPedidoGrupoProducto vendedorGrupo = e.VendedoresNuevos.Single();
            Assert.AreEqual(PEDIDO_SEGUNDO, vendedorGrupo.Pedido);
            Assert.AreEqual("PEL", vendedorGrupo.GrupoProducto);
            Assert.AreEqual("JE", vendedorGrupo.Vendedor);
        }

        [TestMethod]
        public async Task RehacerPedido_PorDefecto_LosPortesNoSeCobranOtraVez()
        {
            var e = CrearEscenarioFacturaDevuelta(conReembolso: true);

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestRehacer(), "Carlos");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            List<LinPedidoVta> gastosEnvio = e.Lineas(PEDIDO_SEGUNDO).Where(l => l.TipoLinea == 2).ToList();
            Assert.AreEqual(2, gastosEnvio.Count, "Siguen en el pedido para que el picking no los vuelva a añadir");
            foreach (LinPedidoVta linea in gastosEnvio)
            {
                Assert.AreEqual(1M, linea.Descuento, linea.Texto);
                Assert.AreEqual(0M, linea.Base_Imponible, linea.Texto);
                Assert.AreEqual(linea.Precio * linea.Cantidad, linea.Bruto, "CK_LinPedidoVta_5: el precio no se toca");
            }
            // Los productos sí se cobran
            Assert.AreEqual(Math.Round(3 * 12.5M, 2) - Math.Round(3 * 12.5M * (1 - 0.95M * 0.9M), 2),
                e.Lineas(PEDIDO_SEGUNDO).Single(l => l.Producto == "17404").Base_Imponible);
            // El abono devuelve los portes que pagó
            Assert.AreEqual(-7M, e.Lineas(PEDIDO_ABONO).Single(l => l.Producto == Constantes.Cuentas.CUENTA_PORTES_CEX).Base_Imponible);
        }

        [TestMethod]
        public async Task RehacerPedido_ConIncluirPortes_LosPortesSeCopianTalCual()
        {
            var e = CrearEscenarioFacturaDevuelta();

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestRehacer(incluirPortes: true), "Carlos");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            LinPedidoVta portes = e.Lineas(PEDIDO_SEGUNDO).Single(l => l.Producto == Constantes.Cuentas.CUENTA_PORTES_CEX);
            Assert.AreEqual(0M, portes.Descuento);
            Assert.AreEqual(7M, portes.Base_Imponible);
        }

        [TestMethod]
        public async Task RehacerPedido_SinFacturarElAbono_NoFacturaNadaYGuardaLaRectificacionPendiente()
        {
            var e = CrearEscenarioFacturaDevuelta();

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestRehacer(crearAlbaranYFactura: false), "Carlos");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            Assert.IsNull(respuesta.Abono.NumeroFactura);
            Assert.AreEqual(PEDIDO_SEGUNDO, respuesta.NumeroPedido);
            A.CallTo(() => e.ServicioAlbaranes.CrearAlbaran(A<string>._, A<int>._, A<string>._, A<DateTime?>._)).MustNotHaveHappened();
            A.CallTo(() => e.ServicioFacturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => e.Pendientes.GuardarPendientes("1", PEDIDO_ABONO, A<List<RectificativaPendienteDTO>>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => e.Pendientes.GuardarPendientes(A<string>._, PEDIDO_SEGUNDO, A<List<RectificativaPendienteDTO>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RehacerPedido_ConOtroCliente_NoHaceNadaYDiceQueUseAbonoYCargo()
        {
            var e = CrearEscenarioFacturaDevuelta();
            CopiarFacturaRequest request = RequestRehacer();
            request.ClienteDestino = "18456";
            request.ContactoDestino = "0";

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(request, "Carlos");

            Assert.IsFalse(respuesta.Exitoso);
            StringAssert.Contains(respuesta.Mensaje, "mismo cliente");
            Assert.AreEqual(0, e.CabecerasNuevas.Count + e.LineasNuevas.Count);
        }

        [TestMethod]
        public async Task AbonoYCargo_ConOtroCliente_SigueFacturandoElCargoAlDestinoConSusPortes()
        {
            // El comportamiento de CrearAbonoYCargo no cambia con #699.
            var e = CrearEscenarioFacturaDevuelta();
            var destino = new Cliente { Empresa = "1", Nº_Cliente = "18456", Contacto = "0", IVA = "G21", Vendedor = "DE", CCC = "5", Ruta = "FW" };
            var clientes = FakeDbSet(new[]
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15234", Contacto = "2", IVA = "G21", Vendedor = "FICHA" },
                destino
            }, null);
            A.CallTo(() => clientes.Include(A<string>._)).Returns(clientes);
            A.CallTo(() => e.Db.Clientes).Returns(clientes);

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(new CopiarFacturaRequest
            {
                Empresa = "1",
                Cliente = "15234",
                NumeroFactura = FACTURA_ORIGINAL,
                CrearAbonoYCargo = true,
                ClienteDestino = "18456",
                ContactoDestino = "0"
            }, "Carlos");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            Assert.IsNull(respuesta.PedidoRehecho);
            Assert.AreEqual(PEDIDO_SEGUNDO, respuesta.Cargo.NumeroPedido);
            Assert.AreEqual("NV26/999999", respuesta.Cargo.NumeroFactura);
            A.CallTo(() => e.ServicioFacturas.CrearFactura("1", PEDIDO_SEGUNDO, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
            CabPedidoVta cargo = e.Cabecera(PEDIDO_SEGUNDO);
            Assert.AreEqual("18456", cargo.Nº_Cliente);
            Assert.AreEqual("FW", cargo.Ruta, "El cargo lleva la ruta de la ficha del destino, como siempre");
            Assert.AreEqual(7M, e.Lineas(PEDIDO_SEGUNDO).Single(l => l.Producto == Constantes.Cuentas.CUENTA_PORTES_CEX).Base_Imponible);
            Assert.AreEqual(0, e.VendedoresNuevos.Count);
        }

        [TestMethod]
        public void EsLineaGastosEnvio_PortesYReembolsoSi_ProductosYOtrasCuentasNo()
        {
            Assert.IsTrue(GestorCopiaPedidos.EsLineaGastosEnvio(new LinPedidoVta { TipoLinea = 2, Producto = "62400005 " }));
            Assert.IsTrue(GestorCopiaPedidos.EsLineaGastosEnvio(new LinPedidoVta { TipoLinea = 2, Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL, Texto = "Comisión contra reembolso" }));
            Assert.IsFalse(GestorCopiaPedidos.EsLineaGastosEnvio(new LinPedidoVta { TipoLinea = 1, Producto = "62400" }));
            Assert.IsFalse(GestorCopiaPedidos.EsLineaGastosEnvio(new LinPedidoVta { TipoLinea = 2, Producto = "75900000" }));
        }

        #endregion

        #region SerieParaPedidoNuevo (NestoAPI#39: rectificativas a serie RV/RC)

        // Art. 6.1.a RD 1619/2012: las rectificativas exigen serie específica. Con la serie
        // RV/RC el mapeador de Verifactu (#36) declara tipo R con las facturas vinculadas.

        [TestMethod]
        public void SerieParaPedidoNuevo_RectificativaDeNV_VaASerieRV()
        {
            var request = new CopiarFacturaRequest { InvertirCantidades = true };
            var original = new CabPedidoVta { Serie = "NV " }; // padded, como en BD

            Assert.AreEqual("RV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, original));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_RectificativaDeCV_VaASerieRC()
        {
            var request = new CopiarFacturaRequest { InvertirCantidades = true };
            var original = new CabPedidoVta { Serie = "CV" };

            Assert.AreEqual("RC", GestorCopiaPedidos.SerieParaPedidoNuevo(request, original));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_InvertirUnaRectificativaRV_RehaceEnLaSerieDeLaRectificada_RV2600154()
        {
            // RV2600154: copia invertida de la RV2600152 (abono de la NV2617228): la factura rehecha es positiva y va en NV.
            var request = new CopiarFacturaRequest { InvertirCantidades = true };
            var original = new CabPedidoVta { Serie = "RV " };

            Assert.AreEqual("NV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, original, "NV "));
            Assert.AreEqual("EV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, original, "EV"));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_InvertirRectificativaSinSaberLaRectificada_SuSerieNormal()
        {
            var request = new CopiarFacturaRequest { InvertirCantidades = true };

            Assert.AreEqual("NV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, new CabPedidoVta { Serie = "RV" }));
            Assert.AreEqual("CV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, new CabPedidoVta { Serie = "RC" }));
            Assert.AreEqual("NV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, new CabPedidoVta { Serie = "RV" }, "RV"), "nunca otra rectificativa");
        }

        [TestMethod]
        public void EsDeshacerAbono_SoloAlInvertirUnaRectificativaHaciaSuSerieNormal()
        {
            var invertir = new CopiarFacturaRequest { InvertirCantidades = true };

            Assert.IsTrue(GestorCopiaPedidos.EsDeshacerAbono(invertir, "RV", "NV"), "RV2600154: es la factura rehecha");
            Assert.IsFalse(GestorCopiaPedidos.EsDeshacerAbono(invertir, "NV", "RV"), "el abono de siempre");
            Assert.IsFalse(GestorCopiaPedidos.EsDeshacerAbono(invertir, "NV", "NV"), "abono añadido al pedido original NV");
            Assert.IsFalse(GestorCopiaPedidos.EsDeshacerAbono(invertir, "GB", "GB"));
            Assert.IsFalse(GestorCopiaPedidos.EsDeshacerAbono(new CopiarFacturaRequest { InvertirCantidades = false }, "RV", "RV"));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_CopiaSinInvertirDeUnaRectificativa_SigueEnRV()
        {
            Assert.AreEqual("RV", GestorCopiaPedidos.SerieParaPedidoNuevo(new CopiarFacturaRequest { InvertirCantidades = false }, new CabPedidoVta { Serie = "RV" }, "NV"));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_CopiaNormal_MantieneLaSerie()
        {
            var request = new CopiarFacturaRequest { InvertirCantidades = false };
            var original = new CabPedidoVta { Serie = "NV" };

            Assert.AreEqual("NV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, original));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_SerieSinRectificativaAsociada_MantieneLaSerie()
        {
            // GB (interna, no registrada en Verifactu) no tiene rectificativa asociada:
            // la rectificativa se queda en su serie, como hasta ahora.
            var request = new CopiarFacturaRequest { InvertirCantidades = true };
            var original = new CabPedidoVta { Serie = "GB" };

            Assert.AreEqual("GB", GestorCopiaPedidos.SerieParaPedidoNuevo(request, original));
        }

        [TestMethod]
        public void SerieParaPedidoNuevo_SinPedidoOriginal_RectificativaVaARV()
        {
            var request = new CopiarFacturaRequest { InvertirCantidades = true };

            Assert.AreEqual("RV", GestorCopiaPedidos.SerieParaPedidoNuevo(request, null));
        }

        #endregion
    }
}
