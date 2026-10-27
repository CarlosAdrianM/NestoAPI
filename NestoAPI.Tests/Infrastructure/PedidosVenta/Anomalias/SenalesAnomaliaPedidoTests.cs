using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta.Anomalias;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta.Anomalias
{
    /// <summary>NestoAPI#715: cada regla de <see cref="SenalesAnomaliaPedido"/>, el interruptor y la inferencia de la aplicación.</summary>
    [TestClass]
    public class SenalesAnomaliaPedidoTests
    {
        internal static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas)
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 929057,
                cliente = "10257",
                contacto = "1",
                Usuario = "NUEVAVISION\\Carlos",
                vendedor = "JE",
                formaPago = "RCB"
            };
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                pedido.Lineas.Add(linea);
            }
            pedido.VincularLineas();
            return pedido;
        }

        internal static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, decimal descuento = 0, int id = 0, decimal? tarifa = null)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = descuento,
                AplicarDescuento = true,
                precioTarifa = tarifa ?? precio,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                almacen = "ALG",
                GrupoProducto = "COS",
                SubgrupoProducto = "ABC",
                estado = Constantes.EstadosLineaVenta.PENDIENTE,
                fechaEntrega = new DateTime(2026, 10, 28)
            };
        }

        internal static InstantaneaAnomalias Instantanea(PedidoVentaDTO pedido, IEnumerable<LineaGrabadaAnomalia> antes = null, RespuestaValidacion validacion = null)
            => InstantaneaAnomalias.Desde(OperacionesAnomalia.CREAR, pedido, antes, validacion, AplicacionesOrigen.NESTO, new DateTime(2026, 10, 27, 10, 0, 0));

        /// <summary>Histórico de un cliente que compra 3 unidades de 38406 a 20,45 con el 22 %, 8 veces; p99 de todos 13.</summary>
        internal static HistoricoCliente HistoricoHabitual()
        {
            var historico = new HistoricoCliente { Pedidos = 19, ImporteMedio = 70m };
            historico.Grupos.Add("COS");
            historico.Grupos.Add("PEL");
            HistoricoProductoCliente producto = historico.De("38406");
            foreach (int cantidad in new[] { 3, 4, 3, 2, 4, 3, 4, 3 })
            {
                producto.Cantidades.Add(cantidad);
                producto.Precios.Add(20.45m);
                producto.Descuentos.Add(0.22m);
            }
            producto.P99Todos = 13;
            producto.PrecioTarifa = 20.45m;
            producto.Grupo = "COS";
            return historico;
        }

        [TestMethod]
        public void SenalesAnomalia_CantidadNormal_NoHaySenales()
        {
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 4, 20.45m, 0.22m))), HistoricoHabitual());

            Assert.AreEqual(0, senales.Count);
        }

        [TestMethod]
        public void SenalesAnomalia_CantidadDiezVecesLaHabitual_CantidadAltaYVsMaximo()
        {
            // El caso del pedido 929057: 30 en vez de 3. Umbral: max(3 × mediana 3 = 9, p99 13) = 13; máximo anterior 4 × 5 = 20.
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 30, 20.45m, 0.22m))), HistoricoHabitual());

            SenalAnomalia alta = senales.Single(s => s.Regla == ReglasAnomalia.CANTIDAD_ALTA);
            Assert.AreEqual(30m, alta.Valor);
            Assert.AreEqual(13m, alta.Esperado);
            Assert.AreEqual(Math.Round(30m / 13m, 4), alta.Puntuacion);
            Assert.AreEqual("38406", alta.Producto);
            SenalAnomalia maximo = senales.Single(s => s.Regla == ReglasAnomalia.CANTIDAD_VS_MAXIMO);
            Assert.AreEqual(20m, maximo.Esperado);
        }

        [TestMethod]
        public void SenalesAnomalia_CantidadAlta_SoloP99CuandoElClienteNoLoHaComprado()
        {
            var historico = new HistoricoCliente { Pedidos = 5, ImporteMedio = 100m };
            historico.De("25720").P99Todos = 12;

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("25720", 13, 15.95m))), historico);

            Assert.AreEqual(1, senales.Count(s => s.Regla == ReglasAnomalia.CANTIDAD_ALTA));
            Assert.AreEqual(0, senales.Count(s => s.Regla == ReglasAnomalia.CANTIDAD_VS_MAXIMO), "sin compras no hay máximo anterior");
            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("25720", 12, 15.95m))), historico).Count(s => s.Regla == ReglasAnomalia.CANTIDAD_ALTA));
        }

        [TestMethod]
        public void SenalesAnomalia_SinHistoricoNinguno_NoHaySenalesDeCantidad()
        {
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("NUEVO", 500, 10m))), new HistoricoCliente());

            Assert.AreEqual(0, senales.Count);
        }

        [TestMethod]
        public void SenalesAnomalia_MedianaSoloConTresComprasOMas()
        {
            var historico = new HistoricoCliente { Pedidos = 2 };
            HistoricoProductoCliente producto = historico.De("38406");
            producto.Cantidades.Add(1);
            producto.Cantidades.Add(1);

            // 2 compras: no hay mediana que valga; tampoco p99. Solo salta la del máximo (1 × 5 = 5 < 6).
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 6, 20m))), historico);

            Assert.AreEqual(0, senales.Count(s => s.Regla == ReglasAnomalia.CANTIDAD_ALTA));
            Assert.AreEqual(1, senales.Count(s => s.Regla == ReglasAnomalia.CANTIDAD_VS_MAXIMO));
        }

        [TestMethod]
        public void SenalesAnomalia_PrecioMenorQueMitadDeTarifa_PrecioBajo()
        {
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 3, 2.045m, 0, 0, 20.45m))), HistoricoHabitual());

            SenalAnomalia bajo = senales.Single(s => s.Regla == ReglasAnomalia.PRECIO_BAJO);
            Assert.AreEqual(2.045m, bajo.Valor);
            Assert.AreEqual(10.225m, bajo.Esperado);
            Assert.AreEqual(5m, bajo.Puntuacion, "la puntuación del precio bajo es esperado / valor");
        }

        [TestMethod]
        public void SenalesAnomalia_PrecioMayorQueUnaVezYMediaElHabitual_PrecioAlto()
        {
            // 204,50 en vez de 20,45 (una coma de más). Habitual (mediana del cliente) 20,45 × 1,5 = 30,675.
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 3, 204.5m, 0, 0, 20.45m))), HistoricoHabitual());

            SenalAnomalia alto = senales.Single(s => s.Regla == ReglasAnomalia.PRECIO_ALTO);
            Assert.AreEqual(30.675m, alto.Esperado);
            Assert.AreEqual(0, senales.Count(s => s.Regla == ReglasAnomalia.PRECIO_BAJO));
        }

        [TestMethod]
        public void SenalesAnomalia_PrecioAlto_SinComprasUsaLaTarifa()
        {
            var historico = new HistoricoCliente();
            historico.De("38406").PrecioTarifa = 20m;

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 1, 31m, 0, 0, 20m))), historico);

            Assert.AreEqual(1, senales.Count(s => s.Regla == ReglasAnomalia.PRECIO_ALTO));
        }

        [TestMethod]
        public void SenalesAnomalia_OfertasMuestrasYRegalos_NoMiranPrecioNiDescuento()
        {
            LineaPedidoVentaDTO oferta = Linea("38406", 3, 1m, 0, 0, 20.45m);
            oferta.oferta = 7;
            LineaPedidoVentaDTO muestra = Linea("38406", 3, 1m, 0, 0, 20.45m);
            muestra.SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS;
            LineaPedidoVentaDTO regalo = Linea("38406", 3, 20.45m, 1m, 0, 20.45m);
            LineaPedidoVentaDTO gratis = Linea("38406", 3, 0m, 0, 0, 20.45m);

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(oferta, muestra, regalo, gratis)), HistoricoHabitual());

            Assert.AreEqual(0, senales.Count(s => s.Regla == ReglasAnomalia.PRECIO_BAJO || s.Regla == ReglasAnomalia.PRECIO_ALTO || s.Regla == ReglasAnomalia.DESCUENTO_ALTO));
        }

        [TestMethod]
        public void SenalesAnomalia_LineasExcluidasYNegativas_NoSeEvaluan()
        {
            LineaPedidoVentaDTO cheque = Linea("CHQ", 100, 1m);
            cheque.EsChequeRegalo = true;
            LineaPedidoVentaDTO texto = Linea("38406", 100, 1m);
            texto.tipoLinea = 2;
            LineaPedidoVentaDTO devolucion = Linea("38406", -100, 1m);
            LineaPedidoVentaDTO ficticia = Linea("38406", 100, 1m);
            ficticia.EsFicticio = true;
            LineaPedidoVentaDTO bonificado = Linea("38406", 100, 1m);
            bonificado.EsBonificadoGanavisiones = true;

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(cheque, texto, devolucion, ficticia, bonificado)), HistoricoHabitual());

            Assert.AreEqual(0, senales.Count);
        }

        [TestMethod]
        public void SenalesAnomalia_DescuentoAlto_SoloSiSuperaDiezPuntosYEstaFueraDeLoPermitido()
        {
            // Habitual 22 %. 30 % no pasa de 32 %; 40 % sí, pero si la validación lo dio por bueno, no es anomalía.
            var validacionOk = new RespuestaValidacion { ValidacionSuperada = true };
            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 3, 20.45m, 0.30m)), null, validacionOk), HistoricoHabitual())
                .Count(s => s.Regla == ReglasAnomalia.DESCUENTO_ALTO));
            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 3, 20.45m, 0.40m)), null, validacionOk), HistoricoHabitual())
                .Count(s => s.Regla == ReglasAnomalia.DESCUENTO_ALTO));

            // Denegado por la validación: salta.
            var denegado = new RespuestaValidacion { ValidacionSuperada = false };
            denegado.Errores.Add(new ErrorValidacion { ProductoId = "38406", Motivo = "Descuento no permitido" });
            SenalAnomalia senal = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 3, 20.45m, 0.40m)), null, denegado), HistoricoHabitual())
                .Single(s => s.Regla == ReglasAnomalia.DESCUENTO_ALTO);
            Assert.AreEqual(0.40m, senal.Valor);
            Assert.AreEqual(0.32m, senal.Esperado);

            // Sin validar (Dirección, Almacén): también salta, porque nadie lo ha comprobado.
            Assert.AreEqual(1, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 3, 20.45m, 0.40m))), HistoricoHabitual())
                .Count(s => s.Regla == ReglasAnomalia.DESCUENTO_ALTO));
        }

        [TestMethod]
        public void SenalesAnomalia_ImporteMayorQueCuatroVecesLaMedia_ImportePedido()
        {
            // Media 70 → umbral 280. 30 × 20,45 = 613,50 bruto.
            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 30, 20.45m, 0.22m))), HistoricoHabitual());

            SenalAnomalia importe = senales.Single(s => s.Regla == ReglasAnomalia.IMPORTE_PEDIDO);
            Assert.IsNull(importe.Linea);
            Assert.IsNull(importe.Producto);
            Assert.AreEqual(613.50m, importe.Valor);
            Assert.AreEqual(280m, importe.Esperado);
        }

        [TestMethod]
        public void SenalesAnomalia_ImportePedido_NecesitaTresPedidosDeHistorico()
        {
            var historico = new HistoricoCliente { Pedidos = 2, ImporteMedio = 10m };

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("38406", 10, 20m))), historico);

            Assert.AreEqual(0, senales.Count(s => s.Regla == ReglasAnomalia.IMPORTE_PEDIDO));
        }

        [TestMethod]
        public void SenalesAnomalia_GrupoNuncaCompradoConCantidadAlta_GrupoNuevo()
        {
            HistoricoCliente historico = HistoricoHabitual();
            historico.De("APA01").Grupo = "APA";

            Assert.AreEqual(1, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("APA01", 6, 100m))), historico).Count(s => s.Regla == ReglasAnomalia.GRUPO_NUEVO));
            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("APA01", 5, 100m))), historico).Count(s => s.Regla == ReglasAnomalia.GRUPO_NUEVO), "5 no es cantidad alta");
            LineaPedidoVentaDTO cosmetica = Linea("38406", 6, 20.45m);
            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(cosmetica)), historico).Count(s => s.Regla == ReglasAnomalia.GRUPO_NUEVO), "COS ya lo compra");
        }

        [TestMethod]
        public void SenalesAnomalia_GrupoNuevo_ClienteSinHistoricoNoCuenta()
        {
            var historico = new HistoricoCliente();
            historico.De("APA01").Grupo = "APA";

            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(Instantanea(Pedido(Linea("APA01", 10, 100m))), historico).Count(s => s.Regla == ReglasAnomalia.GRUPO_NUEVO));
        }

        [TestMethod]
        public void SenalesAnomalia_EnUnPut_SoloSeMiranLasLineasNuevasOQueCambian()
        {
            var antes = new List<LineaGrabadaAnomalia>
            {
                new LineaGrabadaAnomalia { Linea = 1, Producto = "38406", Cantidad = 30, Precio = 20.45m, Descuento = 0.22m },
                new LineaGrabadaAnomalia { Linea = 2, Producto = "38406", Cantidad = 3, Precio = 20.45m, Descuento = 0.22m }
            };
            PedidoVentaDTO pedido = Pedido(
                Linea("38406", 30, 20.45m, 0.22m, 1),   // igual que estaba: no se vuelve a señalar
                Linea("38406", 40, 20.45m, 0.22m, 2),   // cambia la cantidad
                Linea("38406", 50, 20.45m, 0.22m, 0));  // nueva
            InstantaneaAnomalias instantanea = InstantaneaAnomalias.Desde(OperacionesAnomalia.MODIFICAR, pedido, antes, null, AplicacionesOrigen.NESTO, DateTime.Now);

            List<int> evaluadas = SenalesAnomaliaPedido.LineasAEvaluar(instantanea).Select(l => l.Cantidad).ToList();
            CollectionAssert.AreEquivalent(new List<int> { 40, 50 }, evaluadas);

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(instantanea, HistoricoHabitual());
            Assert.AreEqual(2, senales.Count(s => s.Regla == ReglasAnomalia.CANTIDAD_ALTA));
            Assert.IsFalse(senales.Any(s => s.Linea == 1));
        }

        [TestMethod]
        public void SenalesAnomalia_EnUnPutSinCambiosDeLineas_NoHaySenalesNiDeImporte()
        {
            var antes = new List<LineaGrabadaAnomalia> { new LineaGrabadaAnomalia { Linea = 1, Producto = "38406", Cantidad = 30, Precio = 20.45m, Descuento = 0.22m } };
            InstantaneaAnomalias instantanea = InstantaneaAnomalias.Desde(OperacionesAnomalia.MODIFICAR, Pedido(Linea("38406", 30, 20.45m, 0.22m, 1)), antes, null, AplicacionesOrigen.NESTO, DateTime.Now);

            Assert.AreEqual(0, SenalesAnomaliaPedido.Evaluar(instantanea, HistoricoHabitual()).Count);
        }

        [TestMethod]
        public void SenalesAnomalia_SinStock_UnaSenalPorLineaConMasCantidadQueStock()
        {
            var contexto = new ContextoGrabacion { StockLeido = true };
            contexto.Stock[ContextoGrabacion.Clave("38406", "ALG")] = 2;
            PedidoVentaDTO pedido = Pedido(Linea("38406", 3, 20.45m), Linea("25720", 1, 15.95m));

            List<SenalAnomalia> senales = SenalesAnomaliaPedido.EvaluarStock(Instantanea(pedido), contexto);

            // 25720 no tiene movimientos: stock 0 < 1. 38406: 2 < 3.
            Assert.AreEqual(2, senales.Count);
            SenalAnomalia senal = senales.Single(s => s.Producto == "38406");
            Assert.AreEqual(ReglasAnomalia.SIN_STOCK, senal.Regla);
            Assert.AreEqual(2m, senal.Valor);
            Assert.AreEqual(3m, senal.Esperado);
        }

        [TestMethod]
        public void SenalesAnomalia_SinStock_SinLecturaDeStockNoHayNada()
        {
            Assert.AreEqual(0, SenalesAnomaliaPedido.EvaluarStock(Instantanea(Pedido(Linea("38406", 3, 20.45m))), new ContextoGrabacion()).Count);
        }

        [TestMethod]
        public void SenalAnomalia_Puntuar_TopeNoventaYNueve()
        {
            Assert.AreEqual(99m, SenalAnomalia.Puntuar(1000m, 1m));
            Assert.AreEqual(99m, SenalAnomalia.Puntuar(5m, 0m));
            Assert.AreEqual(2.5m, SenalAnomalia.Puntuar(5m, 2m));
        }

        [TestMethod]
        public void HistoricoProductoCliente_Mediana_ParEImpar()
        {
            Assert.AreEqual(3m, HistoricoProductoCliente.Mediana(new List<decimal> { 4, 3, 1 }));
            Assert.AreEqual(2.5m, HistoricoProductoCliente.Mediana(new List<decimal> { 4, 1, 3, 2 }));
            Assert.IsNull(HistoricoProductoCliente.Mediana(new List<decimal>()));
        }

        [TestMethod]
        public void InterruptorAnomalias_Interpretar_TresEstados()
        {
            Assert.AreEqual(ModoAnomalias.Sombra, InterruptorAnomalias.Interpretar("Sombra"));
            Assert.AreEqual(ModoAnomalias.Sombra, InterruptorAnomalias.Interpretar(" sombra "));
            Assert.AreEqual(ModoAnomalias.Activo, InterruptorAnomalias.Interpretar("ACTIVO"));
            Assert.AreEqual(ModoAnomalias.Apagado, InterruptorAnomalias.Interpretar("Apagado"));
            Assert.AreEqual(ModoAnomalias.Apagado, InterruptorAnomalias.Interpretar(null));
            Assert.AreEqual(ModoAnomalias.Apagado, InterruptorAnomalias.Interpretar("lo que sea"));
        }

        [TestMethod]
        public void InterruptorAnomalias_Modo_LeeLaClaveDelConfig()
        {
            try
            {
                InterruptorAnomalias.LeerAjuste = clave => clave == InterruptorAnomalias.CLAVE ? "Sombra" : null;
                Assert.AreEqual(ModoAnomalias.Sombra, InterruptorAnomalias.Modo);
                InterruptorAnomalias.LeerAjuste = clave => null;
                Assert.AreEqual(ModoAnomalias.Apagado, InterruptorAnomalias.Modo);
            }
            finally
            {
                InterruptorAnomalias.LeerAjuste = null;
            }
        }

        [TestMethod]
        public void AplicacionesOrigen_Inferir_CabeceraUsuarioYPrincipal()
        {
            Assert.AreEqual("Ariadna", AplicacionesOrigen.Inferir("Ariadna", "NUEVAVISION\\Carlos", "NUEVAVISION\\Carlos"), "la cabecera manda");
            Assert.AreEqual(AplicacionesOrigen.TIENDAS_NUEVA_VISION, AplicacionesOrigen.Inferir(null, "APP\\10257", "cliente@correo.es"));
            Assert.AreEqual(AplicacionesOrigen.TIENDAS_NUEVA_VISION, AplicacionesOrigen.Inferir(null, "Carlos", "cliente@correo.es"), "token de api/auth/token: email");
            Assert.AreEqual(AplicacionesOrigen.NESTO, AplicacionesOrigen.Inferir(null, "Carlos", "NUEVAVISION\\Carlos"), "windows-token: DOMINIO\\usuario");
            Assert.AreEqual(AplicacionesOrigen.NESTO_APP, AplicacionesOrigen.Inferir(null, "Carlos", "carlos"), "oauth/token: UserName de Identity");
            Assert.AreEqual(AplicacionesOrigen.NESTO, AplicacionesOrigen.Inferir(null, "NUEVAVISION\\Carlos", null), "sin principal (llamada interna): el usuario del pedido");
            Assert.AreEqual(AplicacionesOrigen.DESCONOCIDA, AplicacionesOrigen.Inferir(null, null, null));
            Assert.AreEqual(20, AplicacionesOrigen.Inferir(new string('x', 40), null, null).Length);
        }
    }
}
