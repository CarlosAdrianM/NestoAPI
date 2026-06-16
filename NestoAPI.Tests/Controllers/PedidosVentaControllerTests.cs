using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PedidosVentaControllerTests
    {

        [TestMethod]
        public void PedidoVentaController_ComprobarSiSePuedenInsertarLineas_SiElPedidoTienePickingYEtiquetaImpresaNoSePuedeCambiarElContacto()
        {

        }

        // NOTA: Los tests del endpoint ObtenerDocumentosImpresion son complejos de mockear
        // debido a la estructura de Entity Framework y las relaciones de navegación.
        // La lógica crítica está testeada en GestorFacturacionRutasTests.ObtenerDocumentosImpresion_*
        // que cubren todos los escenarios de negocio.

        // NestoAPI#200: el correo del pedido lee del DTO 'pedido' (no de la cabecera EF),
        // así que el DTO debe normalizarse cuando iva=null para que coincida con lo persistido.
        // El helper centraliza la lógica de POST y PUT.

        // Pedido 918386: un pedido RCB con el CCC a NULL que ya está en albarán necesita que se
        // le pueda actualizar SOLO la cabecera (el CCC) para que el recibo de la factura salga
        // bien. CAMBIO DELICADO: las líneas en albarán (Estado 2) NO se pueden tocar; solo se
        // admite el cambio de cabecera que no afecte a las líneas.
        private static LinPedidoVta LineaEnEstado(short estado, int numOrden = 1, short cantidad = 1)
        {
            return new LinPedidoVta { Estado = estado, Nº_Orden = numOrden, Cantidad = cantidad };
        }

        // Cabecera "espejo" del DTO: mismos cliente/contacto/IVA y mismas líneas, para que por
        // defecto el cambio sea SOLO de cabecera salvo lo que cada test cambie a propósito.
        private static CabPedidoVta CrearCabeceraConLineas(params LinPedidoVta[] lineas)
        {
            foreach (var l in lineas)
            {
                l.Nº_Cliente = l.Nº_Cliente ?? "15339";
                l.Contacto = l.Contacto ?? "0";
            }
            return new CabPedidoVta
            {
                Nº_Cliente = "15339",
                Contacto = "0",
                IVA = "G21",
                LinPedidoVtas = new List<LinPedidoVta>(lineas)
            };
        }

        private static PedidoVentaDTO CrearDtoEspejo(CabPedidoVta cab)
        {
            var dto = new PedidoVentaDTO
            {
                cliente = cab.Nº_Cliente,
                contacto = cab.Contacto,
                iva = cab.IVA
            };
            foreach (var l in cab.LinPedidoVtas)
            {
                dto.Lineas.Add(new LineaPedidoVentaDTO { id = l.Nº_Orden, Cantidad = l.Cantidad ?? 0 });
            }
            return dto;
        }

        // ---- PedidoTieneLineasPendientes: conjunto histórico (sin albarán) ----

        [DataTestMethod]
        [DataRow((short)(-3), true, DisplayName = "PRESUPUESTO es pendiente")]
        [DataRow((short)(-2), false, DisplayName = "NOTA_ENTREGA NO es pendiente")]
        [DataRow((short)(-1), true, DisplayName = "PENDIENTE")]
        [DataRow((short)0, true, DisplayName = "EN_CURSO (0)")]
        [DataRow((short)1, true, DisplayName = "EN_CURSO (1)")]
        [DataRow((short)2, false, DisplayName = "ALBARAN NO es pendiente")]
        [DataRow((short)4, false, DisplayName = "FACTURA NO es pendiente")]
        public void PedidosVentaController_PedidoTieneLineasPendientes_PorEstadoUnico(short estado, bool esperado)
        {
            var lineas = new List<LinPedidoVta> { LineaEnEstado(estado) };

            Assert.AreEqual(esperado, PedidosVentaController.PedidoTieneLineasPendientes(lineas),
                $"El estado {estado} debería dar pendiente={esperado}");
        }

        [TestMethod]
        public void PedidosVentaController_PedidoTieneLineasPendientes_SinLineas_DevuelveFalse()
        {
            Assert.IsFalse(PedidosVentaController.PedidoTieneLineasPendientes(new List<LinPedidoVta>()));
        }

        // ---- PuedeModificarsePedido: decisión completa del guard ----

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_LineasPendientes_SiempreSePuede()
        {
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.PENDIENTE));
            var dto = CrearDtoEspejo(cab);

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto));
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_PendienteYAlbaranMezclados_SePuede()
        {
            // Mezcla pendiente (-1) + albarán (2): hay líneas pendientes, así que es el flujo
            // normal de siempre. Se puede modificar (incluso las pendientes); las de albarán las
            // protege el bucle de líneas.
            var cab = CrearCabeceraConLineas(
                LineaEnEstado(Constantes.EstadosLineaVenta.PENDIENTE, 1),
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 2));
            var dto = CrearDtoEspejo(cab);

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "Un pedido con líneas pendientes y otras en albarán se puede modificar (flujo de siempre)");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_EnCursoYAlbaranMezclados_SePuede()
        {
            // Mezcla en curso (1) + albarán (2): igual, hay pendientes -> flujo normal.
            var cab = CrearCabeceraConLineas(
                LineaEnEstado(Constantes.EstadosLineaVenta.EN_CURSO, 1),
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 2));
            var dto = CrearDtoEspejo(cab);

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "Un pedido con líneas en curso y otras en albarán se puede modificar (flujo de siempre)");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_PendienteYAlbaranAunqueCambieCliente_SePuede()
        {
            // Importante: al haber pendientes, NO entra por la rama restrictiva nueva, así que el
            // cambio de cliente se permite igual que antes (comportamiento histórico intacto).
            var cab = CrearCabeceraConLineas(
                LineaEnEstado(Constantes.EstadosLineaVenta.PENDIENTE, 1),
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 2));
            var dto = CrearDtoEspejo(cab);
            dto.cliente = "99999";

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "Con líneas pendientes el guard no cambia el comportamiento histórico");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_TodoAlbaran_SoloCabecera_SePuede()
        {
            // El caso del pedido 918386: todas las líneas en albarán y solo cambia la cabecera.
            var cab = CrearCabeceraConLineas(
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 1),
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 2));
            var dto = CrearDtoEspejo(cab);
            cab.CCC = null;
            dto.ccc = "1"; // se asigna el CCC: es cabecera, no toca líneas

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "Cambiar solo el CCC de un pedido RCB en albarán debe permitirse");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_TodoFacturado_NoSePuede()
        {
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.FACTURA));
            var dto = CrearDtoEspejo(cab);

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto));
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_SoloNotaEntrega_NoSePuede()
        {
            // Regresión: una nota de entrega (Estado -2) sigue sin poder modificarse.
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.NOTA_ENTREGA));
            var dto = CrearDtoEspejo(cab);

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "Una nota de entrega no se puede modificar (sin cambio respecto a antes)");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_NotaEntregaYAlbaran_NoSePuede()
        {
            // No TODAS las líneas están en albarán (hay una en nota de entrega), así que el
            // camino "solo cabecera en albarán" no aplica y no hay pendientes -> se bloquea.
            var cab = CrearCabeceraConLineas(
                LineaEnEstado(Constantes.EstadosLineaVenta.NOTA_ENTREGA, 1),
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 2));
            var dto = CrearDtoEspejo(cab);

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto));
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_AlbaranYFactura_NoSePuede()
        {
            // Parcialmente facturado: no son todas albarán -> no entra por el camino nuevo.
            var cab = CrearCabeceraConLineas(
                LineaEnEstado(Constantes.EstadosLineaVenta.FACTURA, 1),
                LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN, 2));
            var dto = CrearDtoEspejo(cab);

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto));
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_AlbaranPeroCambiaCliente_NoSePuede()
        {
            // Cambiar el cliente se propaga a las líneas -> tocaría líneas en albarán -> bloqueado.
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN));
            var dto = CrearDtoEspejo(cab);
            dto.cliente = "99999";

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "No se puede cambiar el cliente de un pedido en albarán (tocaría las líneas)");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_AlbaranPeroCambiaContacto_NoSePuede()
        {
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN));
            var dto = CrearDtoEspejo(cab);
            dto.contacto = "5";

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "No se puede cambiar el contacto de un pedido en albarán (tocaría las líneas)");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_AlbaranPeroCambiaIva_NoSePuede()
        {
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN));
            var dto = CrearDtoEspejo(cab);
            dto.iva = null; // quitar el IVA recalcula las líneas

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "No se puede cambiar el IVA de un pedido en albarán (recalcularía las líneas)");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_AlbaranPeroAnadeLinea_NoSePuede()
        {
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN));
            var dto = CrearDtoEspejo(cab);
            dto.Lineas.Add(new LineaPedidoVentaDTO { id = 0, Cantidad = 1 }); // línea nueva

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "No se pueden añadir líneas nuevas a un pedido en albarán");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_AlbaranConCambiosDePaddingEnCabecera_SePuede()
        {
            // Los campos char de BD vienen con padding; comparar con Trim no debe verlo como cambio.
            var cab = CrearCabeceraConLineas(LineaEnEstado(Constantes.EstadosLineaVenta.ALBARAN));
            cab.Nº_Cliente = "15339   ";
            cab.Contacto = "0 ";
            var dto = CrearDtoEspejo(cab);
            dto.cliente = "15339";
            dto.contacto = "0";

            Assert.IsTrue(PedidosVentaController.PuedeModificarsePedido(cab, dto),
                "El padding de los char de BD no debe contar como cambio de cliente/contacto");
        }

        [TestMethod]
        public void PedidosVentaController_PuedeModificarsePedido_SinLineas_NoSePuede()
        {
            var cab = new CabPedidoVta { Nº_Cliente = "15339", Contacto = "0", LinPedidoVtas = new List<LinPedidoVta>() };
            var dto = CrearDtoEspejo(cab);

            Assert.IsFalse(PedidosVentaController.PuedeModificarsePedido(cab, dto));
        }

        private static Empresa CrearEmpresaConDefaults()
        {
            return new Empresa
            {
                Número = "1",
                FormaPagoEfectivo = "EFC",
                PlazosPagoDefecto = "CONTADO"
            };
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNoNull_NoTocaDto()
        {
            var pedido = new PedidoVentaDTO
            {
                iva = "G",
                formaPago = "RCB",
                plazosPago = "30D",
                ccc = "2",
                periodoFacturacion = "FDM"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("RCB", pedido.formaPago);
            Assert.AreEqual("30D", pedido.plazosPago);
            Assert.AreEqual("2", pedido.ccc);
            Assert.AreEqual("FDM", pedido.periodoFacturacion);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNull_PoneFormaPagoYPlazosPagoDeEmpresa()
        {
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "RCB",
                plazosPago = "30D",
                ccc = "2"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("EFC", pedido.formaPago);
            Assert.AreEqual("CONTADO", pedido.plazosPago);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNull_PoneCccNull()
        {
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "RCB",
                plazosPago = "30D",
                ccc = "2"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.IsNull(pedido.ccc);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNullYPlazoPRE_RespetaFormaPagoYPlazo()
        {
            // En prepago se respeta plazosPago=PRE y la forma de pago original
            // (TARJETA, TRANSFERENCIA, etc.), aunque iva venga null.
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "TAR",
                plazosPago = "PRE",
                ccc = "5"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("TAR", pedido.formaPago);
            Assert.AreEqual("PRE", pedido.plazosPago);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNullYPlazoPRE_PoneCccNull()
        {
            // El CCC siempre se anula cuando iva=null, incluso en prepago.
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "TAR",
                plazosPago = "PRE",
                ccc = "5"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.IsNull(pedido.ccc);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_NoTocaPeriodoFacturacion()
        {
            // El helper no gestiona periodoFacturacion. PUT lo fuerza a NRM aparte;
            // POST lo deja como venga del DTO.
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                plazosPago = "30D",
                periodoFacturacion = "FDM"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("FDM", pedido.periodoFacturacion);
        }
    }
}
