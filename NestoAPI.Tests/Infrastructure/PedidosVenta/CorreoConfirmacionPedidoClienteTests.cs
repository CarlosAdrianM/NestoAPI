using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#444 (parte 1): el correo de confirmación al cliente de la tienda online / app.
    /// </summary>
    [TestClass]
    public class CorreoConfirmacionPedidoClienteTests
    {
        private static PedidoClienteResponse Respuesta()
        {
            PedidoClienteResponse r = new PedidoClienteResponse
            {
                Empresa = "1", Numero = 925347, Cliente = "15191", BaseImponible = 100M, Portes = 0M, Total = 121M
            };
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "38697", Texto = "CHAMPU 1000 ML <Pro>", Cantidad = 2, Total = 60.5M });
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "44707", Texto = "SERUM", Cantidad = 1, Total = 60.5M });
            return r;
        }

        [TestMethod]
        public void Preparar_SinCorreo_NoHayNadaQueMandar()
        {
            Assert.IsNull(CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), null, false));
            Assert.IsNull(CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "  ", false));
            Assert.IsNull(CorreoConfirmacionPedidoCliente.Preparar(null, "cliente@ejemplo.com", false));
        }

        [TestMethod]
        public void Preparar_LlevaSoloLoQueSaleEnElCorreo()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), " cliente@ejemplo.com ", sinImportes: false);

            Assert.AreEqual("cliente@ejemplo.com", dto.Correo);
            Assert.AreEqual(925347, dto.Numero);
            Assert.AreEqual(2, dto.Lineas.Count);
            Assert.AreEqual("CHAMPU 1000 ML <Pro>", dto.Lineas[0].Texto);
            Assert.AreEqual(121M, dto.Total);
            Assert.AreEqual(CorreoConfirmacionPedidoDTO.CONDICIONES_HABITUALES, dto.SituacionPago);
        }

        [TestMethod]
        public void SituacionPago_LasCuatroSituaciones()
        {
            Assert.AreEqual(CorreoConfirmacionPedidoDTO.PAGADO,
                CorreoConfirmacionPedidoCliente.SituacionPagoDe(new PedidoClienteResponse { Pagado = true, RequierePago = true }));
            Assert.AreEqual(CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE_PASARELA,
                CorreoConfirmacionPedidoCliente.SituacionPagoDe(new PedidoClienteResponse { RequierePago = true, Pago = new NestoAPI.Models.Pagos.RespuestaIniciarPago() }));
            Assert.AreEqual(CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE,
                CorreoConfirmacionPedidoCliente.SituacionPagoDe(new PedidoClienteResponse { RequierePago = true, Pago = null }));
            Assert.AreEqual(CorreoConfirmacionPedidoDTO.CONDICIONES_HABITUALES,
                CorreoConfirmacionPedidoCliente.SituacionPagoDe(new PedidoClienteResponse { RequierePago = false }));
        }

        [TestMethod]
        public void Generar_AsuntoLineasTotalesYTextoDePago_ConElTextoEscapado()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);
            dto.SituacionPago = CorreoConfirmacionPedidoDTO.PAGADO;

            (string asunto, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            Assert.AreEqual("Gracias por tu pedido nº 925347", asunto);
            StringAssert.Contains(html, "CHAMPU 1000 ML &lt;Pro&gt;");
            StringAssert.Contains(html, "SERUM");
            // Carlos (25/09/26): la referencia del producto, en su columna
            StringAssert.Contains(html, "<th align=\"left\">Referencia</th>");
            StringAssert.Contains(html, ">38697</td>");
            StringAssert.Contains(html, ">44707</td>");
            StringAssert.Contains(html, "121,00");
            StringAssert.Contains(html, "100,00");
            StringAssert.Contains(html, "21,00");
            StringAssert.Contains(html, "Pago recibido con tu tarjeta");
            StringAssert.Contains(html, "Te avisaremos cuando salga");
        }

        [TestMethod]
        public void Generar_SinImportes_NoEnsenaNingunPrecio()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", sinImportes: true);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            Assert.IsFalse(html.Contains("€"), "NestoAPI#446: sin precios tampoco por correo");
            Assert.IsFalse(html.Contains("Total"));
            StringAssert.Contains(html, "SERUM");
        }

        [TestMethod]
        public void Generar_ConPortes_LosDesglosa()
        {
            PedidoClienteResponse r = Respuesta();
            r.Portes = 6.5M;
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            StringAssert.Contains(html, "Gastos de envío");
            StringAssert.Contains(html, "6,50");
        }

        [TestMethod]
        public void Encolar_UsaElEncoladorYNuncaLanza()
        {
            List<CorreoConfirmacionPedidoDTO> encolados = new List<CorreoConfirmacionPedidoDTO>();
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);

            CorreoConfirmacionPedidoCliente.Encolar(dto, encolados.Add);
            CorreoConfirmacionPedidoCliente.Encolar(null, encolados.Add);
            CorreoConfirmacionPedidoCliente.Encolar(dto, d => throw new System.InvalidOperationException("sin Hangfire"));

            Assert.AreEqual(1, encolados.Count, "Con dto null no se encola; el fallo del encolador no lanza");
            Assert.AreSame(dto, encolados[0]);
        }
    }
}
