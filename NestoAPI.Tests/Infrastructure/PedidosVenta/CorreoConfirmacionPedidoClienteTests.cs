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
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "38697", Texto = "CHAMPU 1000 ML <Pro>", Cantidad = 2, PrecioUnitario = 25M, BaseImponible = 50M, Total = 60.5M });
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "44707", Texto = "SERUM", Cantidad = 1, PrecioUnitario = 50M, BaseImponible = 50M, Total = 60.5M });
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

        private static readonly System.Globalization.CultureInfo ES = System.Globalization.CultureInfo.GetCultureInfo("es-ES");

        private static string Euros(decimal importe) => importe.ToString("C", ES);

        private static decimal ImporteDeLaFila(string html, string concepto)
        {
            Match fila = Regex.Match(html, $"<tr><td>(<b>)?{Regex.Escape(concepto)}(</b>)?</td><td align=\"right\">(<b>)?(?<importe>[^<]+)(</b>)?</td></tr>");
            Assert.IsTrue(fila.Success, $"Falta la fila «{concepto}» en el pie");
            return decimal.Parse(fila.Groups["importe"].Value.Replace("€", string.Empty).Trim(), System.Globalization.NumberStyles.Number, ES);
        }

        [TestMethod]
        public void Generar_LaTarjetaVaSinIva_PrecioConDescuentoPorCantidadIgualALaBase()
        {
            // NestoAPI#660 (Carlos 20/10/26): «vendemos a profesionales: siempre bases imponibles»
            PedidoClienteResponse r = Respuesta();
            r.Lineas.Clear();
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "38697", Texto = "CHAMPU", Cantidad = 2, PrecioUnitario = 15M, Descuento = 0.2M, BaseImponible = 24M, Total = 29.04M });
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            StringAssert.Contains(html, $"<div>2 × {Euros(15M)} −20 % = {Euros(24M)}</div>");
            Assert.IsFalse(html.Contains("29,04"), "El importe con IVA no sale en la tarjeta");
            Assert.IsFalse(html.Contains("<th"), "Ya no hay cabecera de columnas");
        }

        [TestMethod]
        public void TextoCantidad_ConDescuento_PrecioSinDescuentoYElDescuentoAparte()
        {
            // NestoAPI#660 (Carlos 20/10/26): con el precio neto redondeado salía «1 × 53,98 € = 53,97 €»
            var linea = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 1, PrecioUnitario = 63.5M, Descuento = 0.15M, BaseImponible = 53.97M };
            Assert.AreEqual($"1 × {Euros(63.5M)} −15 % = {Euros(53.97M)}", CorreoConfirmacionPedidoCliente.TextoCantidad(linea, false));
        }

        [TestMethod]
        public void TextoCantidad_DescuentoConDecimales_LlevaLosQueHaganFalta()
        {
            var linea = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 4, PrecioUnitario = 10M, Descuento = 0.125M, BaseImponible = 35M };
            Assert.AreEqual($"4 × {Euros(10M)} −12,5 % = {Euros(35M)}", CorreoConfirmacionPedidoCliente.TextoCantidad(linea, false));
            Assert.AreEqual("15 %", CorreoConfirmacionPedidoCliente.Porcentaje(0.15M));
            Assert.AreEqual("15 %", CorreoConfirmacionPedidoCliente.Porcentaje(0.1500M), "Sin ceros de sobra");
            Assert.AreEqual("33,33 %", CorreoConfirmacionPedidoCliente.Porcentaje(0.3333M));
        }

        [TestMethod]
        public void TextoCantidad_SinDescuento_ComoAntes()
        {
            var linea = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 2, PrecioUnitario = 12.5M, BaseImponible = 25M };
            Assert.AreEqual($"2 × {Euros(12.5M)} = {Euros(25M)}", CorreoConfirmacionPedidoCliente.TextoCantidad(linea, false));
        }

        [TestMethod]
        public void TextoCantidad_Regalo_DiceRegaloYLoQueVale()
        {
            var regalo = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 1, PrecioUnitario = 10M, Descuento = 1M, BaseImponible = 0M };
            Assert.AreEqual($"Regalo: 1 × {Euros(10M)}", CorreoConfirmacionPedidoCliente.TextoCantidad(regalo, false));
            var sinPrecio = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 2, PrecioUnitario = 0M, Descuento = 1M, BaseImponible = 0M };
            Assert.AreEqual("Regalo: 2 unidades", CorreoConfirmacionPedidoCliente.TextoCantidad(sinPrecio, false));
            Assert.AreEqual("1 unidad", CorreoConfirmacionPedidoCliente.TextoCantidad(regalo, true), "Sin importes, ni siquiera lo que vale");
        }

        [TestMethod]
        public void Preparar_LlevaElPrecioSinDescuentoYElDescuentoDeLaLinea()
        {
            PedidoClienteResponse r = Respuesta();
            r.Lineas.Clear();
            r.Lineas.Add(new LineaPedidoClienteResponse { Producto = "38697", Texto = "CHAMPU", Cantidad = 1, PrecioUnitario = 63.5M, Descuento = 0.15M, BaseImponible = 53.97M });

            LineaCorreoConfirmacionPedidoDTO linea = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false).Lineas[0];

            Assert.AreEqual(63.5M, linea.PrecioUnitario);
            Assert.AreEqual(0.15M, linea.Descuento);
            Assert.AreEqual(53.97M, linea.BaseImponible);
        }

        [TestMethod]
        public void TextoCantidad_SinImportesSoloLaCantidad()
        {
            var tres = new LineaCorreoConfirmacionPedidoDTO { Cantidad = 3, PrecioUnitario = 3.5M, BaseImponible = 10.5M, Total = 12.71M };
            Assert.AreEqual($"3 × {Euros(3.5M)} = {Euros(10.5M)}", CorreoConfirmacionPedidoCliente.TextoCantidad(tres, false));
            Assert.AreEqual("3 unidades", CorreoConfirmacionPedidoCliente.TextoCantidad(tres, true));
            Assert.AreEqual("1 unidad", CorreoConfirmacionPedidoCliente.TextoCantidad(new LineaCorreoConfirmacionPedidoDTO { Cantidad = 1, BaseImponible = 5M }, true));
        }

        [TestMethod]
        public void Pie_BaseIvaYTotal_CuadranAlCentimoYElTotalEsElDelPedido()
        {
            PedidoClienteResponse r = Respuesta();
            r.BaseImponible = 100.85M;
            r.Total = 122.03M; // la suma de los totales de línea sin redondear, redondeada: el IVA no es round(base * 21 %)
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            decimal baseImponible = ImporteDeLaFila(html, "Base imponible");
            decimal iva = ImporteDeLaFila(html, "IVA");
            decimal total = ImporteDeLaFila(html, "Total");
            Assert.AreEqual(100.85M, baseImponible);
            Assert.AreEqual(122.03M, total, "El total sale de donde salía: no cambia, solo se desglosa");
            Assert.AreEqual(total, baseImponible + iva);
            Assert.IsFalse(html.Contains("Recargo de equivalencia"), "Sin RE no hay fila de RE");
            Assert.IsFalse(html.Contains("Gastos de envío"), "Sin portes no hay fila de portes");
            Assert.IsFalse(html.Contains(">Productos<"));
        }

        [TestMethod]
        public void Pie_ConRecargoDeEquivalencia_LoDesglosaYCuadra()
        {
            PedidoClienteResponse r = Respuesta();
            r.BaseImponible = 100M;
            r.ImporteRecargoEquivalencia = 5.2M;
            r.Total = 126.2M;
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            Assert.AreEqual(5.2M, ImporteDeLaFila(html, "Recargo de equivalencia"));
            Assert.AreEqual(21M, ImporteDeLaFila(html, "IVA"));
            Assert.AreEqual(126.2M, ImporteDeLaFila(html, "Total"));
            Assert.IsTrue(html.IndexOf(">IVA<", StringComparison.Ordinal) < html.IndexOf("Recargo de equivalencia", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Pie_ConPortes_ProductosMasEnvioEsLaBase()
        {
            PedidoClienteResponse r = Respuesta();
            r.Portes = 6.5M;
            r.BaseImponible = 106.5M;
            r.Total = 128.87M;
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", false);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            decimal productos = ImporteDeLaFila(html, "Productos");
            Assert.AreEqual(100M, productos, "La suma de las tarjetas (50 + 50)");
            Assert.AreEqual(dto.Lineas.Sum(l => l.BaseImponible), productos);
            Assert.AreEqual(106.5M, productos + ImporteDeLaFila(html, "Gastos de envío"));
            Assert.AreEqual(106.5M, ImporteDeLaFila(html, "Base imponible"));
            Assert.AreEqual(128.87M, ImporteDeLaFila(html, "Base imponible") + ImporteDeLaFila(html, "IVA"));
        }

        [TestMethod]
        public void Generar_SinImportes_NoEnsenaNingunPrecio()
        {
            CorreoConfirmacionPedidoDTO dto = CorreoConfirmacionPedidoCliente.Preparar(Respuesta(), "cliente@ejemplo.com", sinImportes: true);

            (_, string html) = CorreoConfirmacionPedidoCliente.Generar(dto);

            Assert.IsFalse(html.Contains("€"), "NestoAPI#446: sin precios tampoco por correo");
            Assert.IsFalse(html.Contains("Total"));
            Assert.IsFalse(html.Contains("Base imponible"), "Ni el pie de importes");
            Assert.IsFalse(html.Contains("IVA"));
            Assert.IsFalse(html.Contains("Recargo"));
            StringAssert.Contains(html, "<div>2 unidades</div>");
            StringAssert.Contains(html, "SERUM");
        }

        [TestMethod]
        public void OcultarImportes_TambienTapaElRecargoDeEquivalencia()
        {
            PedidoClienteResponse r = Respuesta();
            r.ImporteRecargoEquivalencia = 5.2M;

            NestoAPI.Infraestructure.Clientes.PoliticaPreciosOcultos.OcultarImportes(r);

            Assert.AreEqual(0M, r.ImporteRecargoEquivalencia, "NestoAPI#446: quien no ve precios tampoco ve el RE");
            Assert.AreEqual(0M, CorreoConfirmacionPedidoCliente.Preparar(r, "cliente@ejemplo.com", true).Lineas[0].PrecioUnitario);
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
