using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
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

            Assert.AreEqual("Gracias por su pedido nº 925347", asunto);
            StringAssert.Contains(html, "CHAMPU 1000 ML &lt;Pro&gt;");
            StringAssert.Contains(html, "SERUM");
            // Carlos (25/09/26): la referencia del producto, ahora debajo del nombre en su tarjeta (NestoAPI#660)
            StringAssert.Contains(html, ">Ref. 38697</div>");
            StringAssert.Contains(html, ">Ref. 44707</div>");
            StringAssert.Contains(html, "121,00");
            StringAssert.Contains(html, "100,00");
            StringAssert.Contains(html, "21,00");
            StringAssert.Contains(html, "Pago recibido con su tarjeta");
            StringAssert.Contains(html, "Le avisaremos cuando salga");
        }

        [TestMethod]
        public void Generar_TodoDeUsted_SinNingunTuteo()
        {
            // NestoAPI#660 (Carlos 20/10/26): de usted y sin mezclar, en todas las situaciones de pago
            foreach (string situacion in new[] { CorreoConfirmacionPedidoDTO.PAGADO, CorreoConfirmacionPedidoDTO.PAGADO_APLAZAME,
                CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE_PASARELA, CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE, CorreoConfirmacionPedidoDTO.CONDICIONES_HABITUALES })
            {
                CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);
                dto.SituacionPago = situacion;

                (string asunto, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

                string texto = Regex.Replace(asunto + " " + html, "<[^>]+>", " ");
                foreach (string tuteo in new[] { "tu", "tus", "te", "ti", "tienes", "has", "completes", "responde", "ayudamos" })
                {
                    Assert.IsFalse(Regex.IsMatch(texto, $@"\b{tuteo}\b", RegexOptions.IgnoreCase), $"«{tuteo}» en el correo ({situacion})");
                }
                StringAssert.Contains(html, "Si tiene cualquier duda, responda a este correo y le ayudaremos.");
            }
        }

        [TestMethod]
        public void Generar_CadaLineaEsUnaTarjetaConCantidadPrecioEImporte()
        {
            PedidoClienteResponse r = Respuesta();
            r.Lineas.Clear();
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "38697", Texto = "CHAMPU", Cantidad = 2, Total = 25M });
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            StringAssert.Contains(html, $"<div>2 × {12.5M.ToString("C", es)} = {25M.ToString("C", es)}</div>");
            StringAssert.Contains(html, "12,50");
            StringAssert.Contains(html, "25,00");
            Assert.IsFalse(html.Contains("<th"), "Ya no hay cabecera de columnas");
            Assert.IsFalse(html.Contains("Referencia</th>"));
        }

        [TestMethod]
        public void TextoCantidad_RedondeaElPrecioPorUnidadYSinImportesSoloLaCantidad()
        {
            var tres = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 3, Total = 10M };
            StringAssert.Contains(CorreoConfirmacionPedidoCliente.TextoCantidad(tres, false), "3,33");
            StringAssert.Contains(CorreoConfirmacionPedidoCliente.TextoCantidad(tres, false), "10,00");
            Assert.AreEqual("3 unidades", CorreoConfirmacionPedidoCliente.TextoCantidad(tres, true));
            Assert.AreEqual("1 unidad", CorreoConfirmacionPedidoCliente.TextoCantidad(new LineaCorreoConfirmacionPedidoDTO { Cantidad = 1, Total = 5M }, true));
            StringAssert.Contains(CorreoConfirmacionPedidoCliente.TextoCantidad(new LineaCorreoConfirmacionPedidoDTO { Cantidad = 0, Total = 5M }, false), "5,00");
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

        // NestoAPI#660: la foto de cada producto en el correo

        private const string FOTO_CHAMPU = "https://www.productosdeesteticaypeluqueriaprofesional.com/1234-home_default/champu.jpg";

        [TestMethod]
        public void Generar_ConFoto_PintaLaMiniaturaConAnchoFijoYElNombreEnElAlt()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);
            dto.Lineas[0].UrlFoto = FOTO_CHAMPU;

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            StringAssert.Contains(html, $"<img src=\"{FOTO_CHAMPU}\" alt=\"CHAMPU 1000 ML &lt;Pro&gt;\" width=\"64\"");
            StringAssert.Contains(html, "width: 64px");
            Assert.AreEqual(1, Regex.Matches(html, "<img ").Count, "La línea sin foto no lleva imagen");
            Assert.IsFalse(html.Contains("cid:"), "Por URL, sin adjuntar");
        }

        [TestMethod]
        public void Generar_SinFoto_LaLineaSaleComoAntesSinHueco()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            Assert.IsFalse(html.Contains("<img"));
            Assert.IsFalse(html.Contains("width=\"64\""), "Sin foto no hay celda de la foto: no queda hueco");
            StringAssert.Contains(html, "<tr><td valign=\"top\" style=\"font-size: 14px; line-height: 20px;\"><div style=\"font-weight: bold; color: #222;\">SERUM</div>");
        }

        [TestMethod]
        public void Generar_FotoRotaRelativaOHttp_NoSePinta()
        {
            foreach (string mala in new[] { "https://", "https://www.tienda.com/-home_default/.jpg", "https://www.tienda.com/img/-", "/img/p/1.jpg", "http://www.tienda.com/1.jpg", "javascript:alert(1)", "  " })
            {
                CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);
                dto.Lineas[0].UrlFoto = mala;

                (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

                Assert.IsFalse(html.Contains("<img"), $"«{mala}» no es una foto que se pueda poner");
                StringAssert.Contains(html, "CHAMPU 1000 ML &lt;Pro&gt;");
            }
        }

        [TestMethod]
        public void Generar_ConFoto_EscapaElNombreEnElAltYLaUrl()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);
            dto.Lineas[0].Texto = "CERA \"FRIA\" & <B> 'MIEL'";
            dto.Lineas[0].UrlFoto = "https://www.tienda.com/1-home_default/cera.jpg?a=1&b=\"2\"";

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            StringAssert.Contains(html, "alt=\"CERA &quot;FRIA&quot; &amp; &lt;B&gt; &#39;MIEL&#39;\"");
            StringAssert.Contains(html, "src=\"https://www.tienda.com/1-home_default/cera.jpg?a=1&amp;b=%222%22\"");
            Assert.IsFalse(html.Contains("\"FRIA\""), "Las comillas del nombre no pueden cerrar el atributo");
            Assert.IsFalse(html.Contains("<B>"));
        }

        [TestMethod]
        public void Generar_VariasLineas_CadaUnaConSuFoto()
        {
            PedidoClienteResponse r = Respuesta();
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "17004", Texto = "TINTE 5.0", Cantidad = 3, Total = 9M });
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);
            dto.Lineas[0].UrlFoto = FOTO_CHAMPU;
            dto.Lineas[2].UrlFoto = "https://www.tienda.com/9-home_default/tinte.jpg";

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            Assert.AreEqual(2, Regex.Matches(html, "<img ").Count);
            StringAssert.Contains(html, "alt=\"TINTE 5.0\"");
            Assert.IsTrue(html.IndexOf(FOTO_CHAMPU, StringComparison.Ordinal) < html.IndexOf("tinte.jpg", StringComparison.Ordinal), "En el orden de las líneas");
            StringAssert.Contains(html, ">SERUM</div>");
        }

        [TestMethod]
        public void CompletarFotos_PoneLaDeCadaProductoYDejaSinFotoLasQueNoTienen()
        {
            PedidoClienteResponse r = Respuesta();
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "38697", Texto = "CHAMPU 1000 ML <Pro>", Cantidad = 1, Total = 30M });
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);
            IFotosProductoAlmacen fotos = A.Fake<IFotosProductoAlmacen>();
            IEnumerable<string> preguntados = null;
            _ = A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._))
                .Invokes((IEnumerable<string> p) => preguntados = p.ToList())
                .Returns(Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string> { ["38697"] = FOTO_CHAMPU, ["44707"] = null }));

            CorreoConfirmacionPedidoCliente.CompletarFotos(dto, fotos);

            Assert.AreEqual(FOTO_CHAMPU, dto.Lineas[0].UrlFoto);
            Assert.IsNull(dto.Lineas[1].UrlFoto);
            Assert.AreEqual(FOTO_CHAMPU, dto.Lineas[2].UrlFoto, "El mismo producto en dos líneas, la misma foto");
            CollectionAssert.AreEquivalent(new[] { "38697", "44707", "38697" }, preguntados.ToArray());
        }

        [TestMethod]
        public void CompletarFotos_SiElServicioFalla_NoLanzaYElCorreoSaleSinFotos()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", false);
            IFotosProductoAlmacen fotos = A.Fake<IFotosProductoAlmacen>();
            _ = A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).Throws(new InvalidOperationException("la tienda no contesta"));

            CorreoConfirmacionPedidoCliente.CompletarFotos(dto, fotos);
            CorreoConfirmacionPedidoCliente.CompletarFotos(null, fotos);
            CorreoConfirmacionPedidoCliente.CompletarFotos(dto, null);

            Assert.IsTrue(dto.Lineas.All(l => l.UrlFoto == null));
            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);
            Assert.IsFalse(html.Contains("<img"));
        }
    }
}
