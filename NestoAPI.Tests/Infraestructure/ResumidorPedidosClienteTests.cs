using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// TNV#66: lo que el cliente lee de su pedido después de comprar. Son las reglas que
    /// traducen nuestro modelo (líneas, estados, albaranes, prepagos) a su idioma.
    /// </summary>
    [TestClass]
    public class ResumidorPedidosClienteTests
    {
        [TestMethod]
        public void Resumir_SumaElTotalConIvaYCuentaSoloLosArticulos()
        {
            // La línea de portes cuenta para el importe (es lo que paga) pero no es un artículo.
            DatosPedidoCliente pedido = Pedido(
                Linea(total: 12.10M, cantidad: 2, texto: "LACA 500 ML"),
                Linea(total: 4.84M, cantidad: 1, tipoLinea: Constantes.TiposLineaVenta.CUENTA_CONTABLE, texto: "Portes"));

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.AreEqual(16.94M, resumen.Total);
            Assert.AreEqual(2, resumen.NumeroArticulos);
            CollectionAssert.AreEqual(new[] { "LACA 500 ML" }, resumen.Articulos.ToArray());
        }

        [TestMethod]
        public void Resumir_NombraSoloLosPrimerosArticulos()
        {
            // El resumen es la etiqueta de la caja, no el detalle del pedido.
            DatosPedidoCliente pedido = Pedido(
                Linea(texto: "UNO"), Linea(texto: "DOS"), Linea(texto: "TRES"), Linea(texto: "CUATRO"));

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.AreEqual(ResumidorPedidosCliente.ARTICULOS_QUE_SE_NOMBRAN, resumen.Articulos.Count);
        }

        // --- Pendiente de pago: la diferencia que no puede pasar desapercibida ---

        [TestMethod]
        public void Resumir_PrepagoSinCobrar_QuedaPendienteDePagoConLoQueFalta()
        {
            DatosPedidoCliente pedido = Pedido(Linea(total: 50M));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;
            pedido.ImportePrepagado = 0M;

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.IsTrue(resumen.PendienteDePago);
            Assert.AreEqual(50M, resumen.ImportePendiente);
            Assert.AreEqual(EstadoPedidoCliente.PendienteDePago, resumen.Estado);
        }

        [TestMethod]
        public void Resumir_PrepagoYaCobrado_NoQuedaNadaPendiente()
        {
            DatosPedidoCliente pedido = Pedido(Linea(total: 50M));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;
            pedido.ImportePrepagado = 50M;

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.IsFalse(resumen.PendienteDePago);
            Assert.AreEqual(0M, resumen.ImportePendiente);
        }

        [TestMethod]
        public void Resumir_DescuadreDeCentimos_NoSeLeReclamaAlCliente()
        {
            // El picking suelta el pedido con hasta 0,25 EUR de descuadre: si aquí fuéramos más
            // estrictos, le diríamos "pendiente de pago" a un pedido que ya se está preparando.
            DatosPedidoCliente pedido = Pedido(Linea(total: 50M));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;
            pedido.ImportePrepagado = 49.90M;

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.IsFalse(resumen.PendienteDePago);
        }

        [TestMethod]
        public void Resumir_SinPrepago_NoSeHablaDePagosPendientes()
        {
            // Con recibo o transferencia a vencimiento el pedido no espera al dinero: decirle
            // "pendiente de pago" sería reclamarle algo que todavía no le toca.
            DatosPedidoCliente pedido = Pedido(Linea(total: 50M));
            pedido.PlazosPago = "30";
            pedido.ImportePrepagado = 0M;

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.IsFalse(resumen.PendienteDePago);
            Assert.AreEqual(0M, resumen.ImportePendiente);
        }

        [TestMethod]
        public void Resumir_PedidoDeTarjetaYaFacturado_NoSaleComoPendienteDePago()
        {
            // Regresión con datos de producción (04/09/26): al facturar, el prepago del pedido se
            // enlaza a la factura y deja de contar (Prepagos.Factura). Sin tenerlo en cuenta,
            // TODOS los pedidos de tarjeta ya facturados —que están cobrados— le saldrían al
            // cliente como "pendiente de pago": el pedido 925495 y todos sus hermanos.
            DatosPedidoCliente pedido = Pedido(Linea(total: 12.99M, estado: Constantes.EstadosLineaVenta.FACTURA));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;
            pedido.ImportePrepagado = 0M;

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.IsFalse(resumen.PendienteDePago, "ya está servido: el cobro vive en su extracto, no aquí");
            Assert.AreEqual(0M, resumen.ImportePendiente);
            Assert.AreEqual(EstadoPedidoCliente.Servido, resumen.Estado);
        }

        [TestMethod]
        public void Resumir_ConAlgoTodaviaSinSalir_SiSeLeReclamaElPago()
        {
            // La otra cara: mientras quede algo por servir, el prepago sigue vivo y lo que falta
            // por cobrar es justo lo que está frenando el pedido.
            DatosPedidoCliente pedido = Pedido(
                Linea(total: 30M, estado: Constantes.EstadosLineaVenta.FACTURA),
                Linea(total: 20M, estado: Constantes.EstadosLineaVenta.PENDIENTE));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;
            pedido.ImportePrepagado = 0M;

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.IsTrue(resumen.PendienteDePago);
            Assert.AreEqual(50M, resumen.ImportePendiente);
        }

        // --- El estado, en el idioma del cliente ---

        [TestMethod]
        public void Resumir_RecienCreado_LoHemosRecibido()
        {
            // Los pedidos de la app nacen en EN_CURSO y sin picking: nadie los ha cogido todavía.
            DatosPedidoCliente pedido = Pedido(Linea(estado: Constantes.EstadosLineaVenta.EN_CURSO));

            Assert.AreEqual(EstadoPedidoCliente.Recibido, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_EsperandoExistencias_TodaviaEsLoHemosRecibido()
        {
            // Estado PENDIENTE es que falta recibirlo de una tienda o de un proveedor. Para el
            // cliente sigue siendo "lo tenemos": de dónde lo sacamos no es asunto suyo.
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.PENDIENTE),
                Linea(estado: Constantes.EstadosLineaVenta.EN_CURSO));

            Assert.AreEqual(EstadoPedidoCliente.Recibido, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_ConPickingAsignado_SeEstaPreparando()
        {
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.EN_CURSO, picking: 45231),
                Linea(estado: Constantes.EstadosLineaVenta.PENDIENTE));

            Assert.AreEqual(EstadoPedidoCliente.EnPreparacion, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_EnCursoSinPicking_NoSeEstaPreparandoTodavia()
        {
            // Lo que dice que el almacén lo está montando es el picking, no el estado 1: si no,
            // le diríamos "preparándolo" a un pedido que nadie ha tocado.
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.EN_CURSO, picking: 0),
                Linea(estado: Constantes.EstadosLineaVenta.EN_CURSO));

            Assert.AreEqual(EstadoPedidoCliente.Recibido, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_TodoEntregadoALaAgenciaSinEnvio_EsServido()
        {
            // Recogida en tienda o ruta propia: no hay paquete que seguir.
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.ALBARAN),
                Linea(estado: Constantes.EstadosLineaVenta.FACTURA));

            Assert.AreEqual(EstadoPedidoCliente.Servido, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_ConParteEntregadaALaAgencia_SeDiceQueVaEnDosVeces()
        {
            // Si no se le dice, abre una caja incompleta y cree que le falta algo.
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.ALBARAN),
                Linea(estado: Constantes.EstadosLineaVenta.EN_CURSO, picking: 45231));

            Assert.AreEqual(EstadoPedidoCliente.EnviadoEnParte, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_ParteEnviadaConSuEnvio_SigueSiendoEnviadoEnParte()
        {
            // El envío que ya salió tiene su seguimiento, pero el pedido NO está completo: decir
            // "en camino" a secas sería prometer que va todo.
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.ALBARAN),
                Linea(estado: Constantes.EstadosLineaVenta.PENDIENTE));
            pedido.Envio = new UltimoEnvioClienteDTO { Pedido = pedido.Numero, NumeroSeguimiento = "123" };

            Assert.AreEqual(EstadoPedidoCliente.EnviadoEnParte, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_TodoEnLaAgenciaConEnvio_EstaEnCamino()
        {
            DatosPedidoCliente pedido = Pedido(Linea(estado: Constantes.EstadosLineaVenta.ALBARAN));
            pedido.Envio = new UltimoEnvioClienteDTO { Pedido = pedido.Numero, NumeroSeguimiento = "123" };

            Assert.AreEqual(EstadoPedidoCliente.Enviado, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_EnvioConFechaDeEntrega_EstaEntregado()
        {
            // Entregado AL CLIENTE lo dice el seguimiento de la agencia, no nuestros estados.
            DatosPedidoCliente pedido = Pedido(Linea(estado: Constantes.EstadosLineaVenta.FACTURA));
            pedido.Envio = new UltimoEnvioClienteDTO
            {
                Pedido = pedido.Numero,
                NumeroSeguimiento = "123",
                FechaEntrega = DateTime.Today
            };

            Assert.AreEqual(EstadoPedidoCliente.Entregado, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_EnvioMarcadoEntregadoPorLaAgencia_EstaEntregado()
        {
            DatosPedidoCliente pedido = Pedido(Linea(estado: Constantes.EstadosLineaVenta.ALBARAN));
            pedido.Envio = new UltimoEnvioClienteDTO
            {
                Pedido = pedido.Numero,
                NumeroSeguimiento = "123",
                Estado = Constantes.Agencias.ESTADO_ENTREGADO
            };

            Assert.AreEqual(EstadoPedidoCliente.Entregado, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_PedidoYaEnviado_NoSeLeReclamaNadaAunqueElPrepagoNoAparezca()
        {
            // Un pedido que ya ha salido no se frena por dinero: o se cobró, o el cobro va por
            // otro lado. Reclamárselo en la app cuando el paquete está en la calle es alarmarle
            // por algo que no puede resolver ahí.
            DatosPedidoCliente pedido = Pedido(Linea(total: 50M, estado: Constantes.EstadosLineaVenta.ALBARAN));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;
            pedido.ImportePrepagado = 0M;
            pedido.Envio = new UltimoEnvioClienteDTO { Pedido = pedido.Numero, NumeroSeguimiento = "123" };

            PedidoClienteResumenDTO resumen = ResumidorPedidosCliente.Resumir(pedido);

            Assert.AreEqual(EstadoPedidoCliente.Enviado, resumen.Estado);
            Assert.IsFalse(resumen.PendienteDePago);
        }

        [TestMethod]
        public void Resumir_LasLineasDePresupuestoNoCuentanParaElEstado()
        {
            // Un presupuesto no es un pedido: si se cuela una línea así, no puede convertir en
            // "servido" un pedido que no lo está ni al revés.
            DatosPedidoCliente pedido = Pedido(
                Linea(estado: Constantes.EstadosLineaVenta.PRESUPUESTO),
                Linea(estado: Constantes.EstadosLineaVenta.ALBARAN));

            Assert.AreEqual(EstadoPedidoCliente.Servido, ResumidorPedidosCliente.Resumir(pedido).Estado);
        }

        [TestMethod]
        public void Resumir_TodosLosEstadosTienenTextoParaElCliente()
        {
            foreach (EstadoPedidoCliente estado in Enum.GetValues(typeof(EstadoPedidoCliente)))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(ResumidorPedidosCliente.TextoDe(estado)),
                    $"el estado {estado} llegaría a la pantalla del cliente en blanco");
            }
        }

        private static DatosPedidoCliente Pedido(params DatosLineaPedidoCliente[] lineas)
        {
            return new DatosPedidoCliente
            {
                Numero = 925368,
                Fecha = DateTime.Today,
                FormaPago = Constantes.FormasPago.TARJETA,
                PlazosPago = "CONTADO",
                Lineas = new List<DatosLineaPedidoCliente>(lineas)
            };
        }

        private static DatosLineaPedidoCliente Linea(
            decimal total = 10M,
            short cantidad = 1,
            int estado = Constantes.EstadosLineaVenta.PENDIENTE,
            int tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            string texto = "PRODUCTO",
            int? picking = null)
        {
            return new DatosLineaPedidoCliente
            {
                Total = total,
                Cantidad = cantidad,
                Estado = (short)estado,
                TipoLinea = (byte)tipoLinea,
                Texto = texto,
                Picking = picking
            };
        }
    }
}
