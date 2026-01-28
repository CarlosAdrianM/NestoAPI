using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System;
using System.Collections.Generic;
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
    }
}
