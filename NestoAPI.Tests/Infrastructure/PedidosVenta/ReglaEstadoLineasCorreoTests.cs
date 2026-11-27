using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Pedido 929421 (Carlos, 27/11/26): el correo «Modificación Pedido» de un pedido ya servido dice en qué está cada línea y, si no
    /// queda nada por entregar, no lleva el bloque de entrega (modo de entrega, fecha de entrega ni «⚠ Cambia la fecha… Ahora: sin fecha»).
    /// </summary>
    [TestClass]
    public class ReglaEstadoLineasCorreoTests
    {
        private const int COLSPAN = 6;

        private static LineaPedidoVentaDTO Linea(int id, string producto, short estado, DateTime fechaEntrega,
            byte tipo = Constantes.TiposLineaVenta.PRODUCTO, int cantidad = 1)
            => new LineaPedidoVentaDTO
            {
                id = id,
                Producto = producto,
                texto = "TEXTO " + producto,
                estado = estado,
                tipoLinea = tipo,
                Cantidad = cantidad,
                fechaEntrega = fechaEntrega,
                almacen = Constantes.Almacenes.ALGETE
            };

        private static LineaPedidoVentaDTO Facturada(int id, string producto, string factura, DateTime fecha)
        {
            LineaPedidoVentaDTO linea = Linea(id, producto, Constantes.EstadosLineaVenta.FACTURA, new DateTime(2026, 10, 27));
            linea.SituacionGuardada = new SituacionGuardadaLinea
            {
                Estado = Constantes.EstadosLineaVenta.FACTURA,
                Albaran = 732939,
                FechaAlbaran = fecha,
                Factura = factura,
                FechaFactura = fecha
            };
            return linea;
        }

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas)
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 929421,
                cliente = "37031",
                contacto = "0",
                modoServicio = Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ,
                servirJunto = false,
                modoFacturacion = Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS
            };
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                pedido.Lineas.Add(linea);
            }
            return pedido;
        }

        // Prometida al crear: miércoles 28/10/2026. Ahora, sin fecha (no queda nada por entregar).
        private static FechaEntregaAgenciaDTO FechaSinNadaPendiente()
            => new FechaEntregaAgenciaDTO { FechaPrometida = new DateTime(2026, 10, 28), FechaEntregaAgencia = null };

        /// <summary>Lo que el correo pintaba antes de este cambio (los tres trozos tal cual).</summary>
        private static string BloqueDeSiempre(PedidoVentaDTO pedido, FechaEntregaAgenciaDTO fecha, string retorno = null)
            => GestorPresupuestos.GenerarHtmlModoServicio(pedido, false, false, COLSPAN, retorno)
                + GestorPresupuestos.GenerarHtmlFechaEntregaComun(pedido.Lineas, COLSPAN)
                + GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(fecha, pedido.EsPresupuesto, true, pedido.Lineas, COLSPAN,
                    pedido.servirPendienteAlHaberStock == true, null);

        // ---- El caso real: pedido 929421, se borra la única línea pendiente y lo que queda está facturado ----

        [TestMethod]
        public void CasoReal929421_TodoFacturado_SinBloqueDeEntregaNiCambioDeFecha()
        {
            PedidoVentaDTO pedido = Pedido(
                Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)),
                Facturada(2, "40123", "NV26/012345", new DateTime(2026, 10, 28)));

            // Antes salía todo esto (así se comprueba que el caso de prueba es el del correo real)
            string antes = BloqueDeSiempre(pedido, FechaSinNadaPendiente());
            StringAssert.Contains(antes, "Modo de entrega: Ahora lo que hay, el resto de una vez");
            StringAssert.Contains(antes, "Fecha de entrega: ");
            StringAssert.Contains(antes, "Cambia la fecha de entrega a la agencia. Antes: miércoles 28/10/2026 &rarr; Ahora: sin fecha");

            string bloque = GestorPresupuestos.GenerarHtmlBloqueEntrega(pedido, false, false, COLSPAN, null, FechaSinNadaPendiente(), true);

            Assert.AreEqual(string.Empty, bloque);
        }

        [TestMethod]
        public void CasoReal929421_FraseArribaYMarcaDeFacturada()
        {
            PedidoVentaDTO pedido = Pedido(
                Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)),
                Facturada(2, "40123", "NV26/012345", new DateTime(2026, 10, 28)));

            Assert.AreEqual("<p style=\"background-color:#e7f1ff; border:2px solid #0d6efd; padding:10px\"><b>&#9432; " +
                "Este pedido ya está entregado y facturado; el cambio no afecta a ninguna entrega.</b></p>" + Environment.NewLine,
                GestorPresupuestos.GenerarHtmlTodoServido(pedido.Lineas));
            Assert.AreEqual("<br><small style=\"color:#555555\"><b>Facturada: factura NV26/012345 del 28/10/2026</b></small>",
                GestorPresupuestos.HtmlMarcaEstadoLinea(pedido.Lineas.First(), ReglaEstadoLineasCorreo.EsPedidoAMedias(pedido.Lineas)));
        }

        [TestMethod]
        public void SinNadaPorEntregar_ConEnvioEnCurso_SoloElRetorno()
        {
            PedidoVentaDTO pedido = Pedido(Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)));

            string bloque = GestorPresupuestos.GenerarHtmlBloqueEntrega(pedido, false, false, COLSPAN, "Retorno: No", FechaSinNadaPendiente(), true);

            Assert.AreEqual("<tr>" + Environment.NewLine + "<td colspan='6'>Retorno: No</td>" + Environment.NewLine + "</tr>" + Environment.NewLine, bloque);
        }

        [TestMethod]
        public void SoloEntregado_FraseSinFacturado_YMarcaConAlbaran()
        {
            LineaPedidoVentaDTO linea = Linea(1, "17404", Constantes.EstadosLineaVenta.ALBARAN, new DateTime(2026, 10, 27));
            linea.SituacionGuardada = new SituacionGuardadaLinea { Estado = Constantes.EstadosLineaVenta.ALBARAN, Albaran = 732939, FechaAlbaran = new DateTime(2026, 10, 28) };
            PedidoVentaDTO pedido = Pedido(linea);

            Assert.AreEqual("Este pedido ya está entregado; el cambio no afecta a ninguna entrega.", ReglaEstadoLineasCorreo.FraseTodoServido(pedido.Lineas));
            Assert.AreEqual("Entregada: albarán 732939 del 28/10/2026", ReglaEstadoLineasCorreo.Marca(linea, false));
        }

        [TestMethod]
        public void EntregadoYEnParteFacturado_LaFraseLoDice()
        {
            LineaPedidoVentaDTO enAlbaran = Linea(1, "17404", Constantes.EstadosLineaVenta.ALBARAN, new DateTime(2026, 10, 27));
            PedidoVentaDTO pedido = Pedido(enAlbaran, Facturada(2, "40123", "NV26/012345", new DateTime(2026, 10, 28)));

            Assert.AreEqual("Este pedido ya está entregado y facturado en parte; el cambio no afecta a ninguna entrega.",
                ReglaEstadoLineasCorreo.FraseTodoServido(pedido.Lineas));
        }

        [TestMethod]
        public void NotaDeEntregaProcesada_CuentaComoEntregada()
        {
            LineaPedidoVentaDTO linea = Linea(1, "17404", Constantes.EstadosLineaVenta.NOTA_ENTREGA, new DateTime(2026, 10, 27));

            Assert.IsFalse(ReglaEstadoLineasCorreo.QuedaAlgoPorEntregar(new[] { linea }));
            Assert.AreEqual("Entregada (nota de entrega)", ReglaEstadoLineasCorreo.Marca(linea, false));
        }

        [TestMethod]
        public void PortesPendientesConTodoElProductoFacturado_NoCuentanComoEntrega()
        {
            PedidoVentaDTO pedido = Pedido(
                Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)),
                Linea(2, "62400003", Constantes.EstadosLineaVenta.PENDIENTE, new DateTime(2026, 10, 27), Constantes.TiposLineaVenta.CUENTA_CONTABLE));

            Assert.IsFalse(ReglaEstadoLineasCorreo.QuedaAlgoPorEntregar(pedido.Lineas));
        }

        [TestMethod]
        public void LineaBorradaQueVieneConCantidadCero_NoCuenta()
        {
            PedidoVentaDTO pedido = Pedido(
                Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)),
                Linea(2, "99999", Constantes.EstadosLineaVenta.PENDIENTE, new DateTime(2026, 10, 27), cantidad: 0));

            Assert.IsFalse(ReglaEstadoLineasCorreo.QuedaAlgoPorEntregar(pedido.Lineas));
        }

        // ---- Pedido a medias ----

        [TestMethod]
        public void AMedias_ElBloqueSigueYSeRefiereSoloALoPendiente()
        {
            PedidoVentaDTO pedido = Pedido(
                Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)),
                Linea(2, "40123", Constantes.EstadosLineaVenta.PENDIENTE, new DateTime(2026, 12, 2)));
            var fecha = new FechaEntregaAgenciaDTO { FechaPrometida = new DateTime(2026, 10, 28), FechaEntregaAgencia = new DateTime(2026, 12, 2) };

            string bloque = GestorPresupuestos.GenerarHtmlBloqueEntrega(pedido, false, false, COLSPAN, null, fecha, true);

            StringAssert.StartsWith(bloque, "<tr>" + Environment.NewLine + "<td colspan='6' style=\"text-align:left\"><b>Parte del pedido ya está entregada. " +
                "Lo que sigue (modo de entrega y fechas) es solo para lo que queda por entregar: 1 línea marcada «Pendiente de entregar».</b></td>");
            StringAssert.Contains(bloque, "Modo de entrega: Ahora lo que hay, el resto de una vez. Modo de facturación: Por entregas");
            // La fecha común es la de lo pendiente (antes, con dos fechas distintas, no salía y cada línea llevaba la suya)
            StringAssert.Contains(bloque, "Fecha de entrega: " + new DateTime(2026, 12, 2).ToShortDateString());
            StringAssert.Contains(bloque, "Cambia la fecha de entrega a la agencia. Antes: miércoles 28/10/2026 &rarr; Ahora: miércoles 02/12/2026");
            Assert.AreEqual(1, ReglaEstadoLineasCorreo.LineasParaFechas(pedido.Lineas).Count);
        }

        [TestMethod]
        public void AMedias_LasMarcas()
        {
            LineaPedidoVentaDTO pendiente = Linea(2, "40123", Constantes.EstadosLineaVenta.PENDIENTE, new DateTime(2026, 12, 2));
            PedidoVentaDTO pedido = Pedido(Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28)), pendiente);
            bool aMedias = ReglaEstadoLineasCorreo.EsPedidoAMedias(pedido.Lineas);

            Assert.IsTrue(aMedias);
            Assert.AreEqual("<br><small><b>Pendiente de entregar</b></small>", GestorPresupuestos.HtmlMarcaEstadoLinea(pendiente, aMedias));
            Assert.AreEqual(string.Empty, GestorPresupuestos.GenerarHtmlTodoServido(pedido.Lineas));
            pendiente.yaFacturado = true;
            Assert.AreEqual("Pendiente de entregar (ya facturada)", ReglaEstadoLineasCorreo.Marca(pendiente, aMedias));
        }

        // ---- El de siempre: nada servido, correo idéntico ----

        [TestMethod]
        public void TodoPendiente_BloqueIdenticoAlDeSiempre_YSinMarcas()
        {
            PedidoVentaDTO pedido = Pedido(
                Linea(1, "17404", Constantes.EstadosLineaVenta.PENDIENTE, new DateTime(2026, 10, 28)),
                Linea(2, "40123", Constantes.EstadosLineaVenta.EN_CURSO, new DateTime(2026, 10, 28)),
                Linea(0, "50000", 0, new DateTime(2026, 10, 28))); // línea nueva sin estado en el DTO
            var fecha = new FechaEntregaAgenciaDTO { FechaPrometida = new DateTime(2026, 10, 28), FechaEntregaAgencia = new DateTime(2026, 10, 29) };

            Assert.AreEqual(BloqueDeSiempre(pedido, fecha, "Retorno: No"),
                GestorPresupuestos.GenerarHtmlBloqueEntrega(pedido, false, false, COLSPAN, "Retorno: No", fecha, true));
            Assert.AreEqual(string.Empty, GestorPresupuestos.GenerarHtmlTodoServido(pedido.Lineas));
            Assert.IsTrue(pedido.Lineas.All(l => GestorPresupuestos.HtmlMarcaEstadoLinea(l, ReglaEstadoLineasCorreo.EsPedidoAMedias(pedido.Lineas)) == string.Empty));
            CollectionAssert.AreEqual(pedido.Lineas.ToList(), ReglaEstadoLineasCorreo.LineasParaFechas(pedido.Lineas));
        }

        [TestMethod]
        public void Presupuesto_NoCambia()
        {
            PedidoVentaDTO pedido = Pedido(Linea(1, "17404", Constantes.EstadosLineaVenta.PRESUPUESTO, new DateTime(2026, 10, 28)));
            pedido.EsPresupuesto = true;

            Assert.IsTrue(ReglaEstadoLineasCorreo.QuedaAlgoPorEntregar(pedido.Lineas));
            Assert.AreEqual(BloqueDeSiempre(pedido, null), GestorPresupuestos.GenerarHtmlBloqueEntrega(pedido, false, false, COLSPAN, null, null, true));
        }

        // ---- El PUT copia lo guardado ----

        [TestMethod]
        public void CopiarSituacionGuardada_ManaLaBD_YLasNuevasNoSeTocan()
        {
            LineaPedidoVentaDTO existente = Linea(5, "17404", Constantes.EstadosLineaVenta.PENDIENTE, new DateTime(2026, 10, 27)); // el cliente la mandó vieja
            LineaPedidoVentaDTO nueva = Linea(0, "40123", 0, new DateTime(2026, 10, 27));
            var guardadas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 5, Estado = Constantes.EstadosLineaVenta.FACTURA, Nº_Albarán = 732939, Fecha_Albarán = new DateTime(2026, 10, 28),
                    Nº_Factura = "NV26/012345   ", Fecha_Factura = new DateTime(2026, 10, 28) }
            };

            ReglaEstadoLineasCorreo.CopiarSituacionGuardada(new[] { existente, nueva }, guardadas);

            Assert.AreEqual(SituacionLineaCorreo.Facturada, ReglaEstadoLineasCorreo.Situacion(existente));
            Assert.AreEqual("Facturada: factura NV26/012345 del 28/10/2026", ReglaEstadoLineasCorreo.Marca(existente, false));
            Assert.AreEqual((short)Constantes.EstadosLineaVenta.PENDIENTE, existente.estado); // el DTO no se toca
            Assert.IsNull(nueva.SituacionGuardada);
        }

        [TestMethod]
        public void CopiaParaPartir_NoArrastraLaSituacionGuardada()
        {
            LineaPedidoVentaDTO linea = Facturada(1, "17404", "NV26/012345", new DateTime(2026, 10, 28));
            Assert.IsNull(linea.CopiaParaPartir().SituacionGuardada);
        }
    }
}
