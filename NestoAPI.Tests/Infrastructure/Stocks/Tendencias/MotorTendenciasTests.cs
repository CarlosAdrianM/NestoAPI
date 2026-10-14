using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: fase, tendencia, exceso, propuesta y explicación de un producto (motor puro).</summary>
    [TestClass]
    public class MotorTendenciasTests
    {
        private static readonly ParametrosTendencias P = new ParametrosTendencias();

        private static ResultadoTendencia Calcular(DatosProductoTendencia d, decimal[] indices = null)
            => MotorTendencias.Calcular(d, indices, MesReferencia, P);

        /// <summary>Cinco años a 400/mes y los últimos 12 meses: seis a 450 y seis a 330 (como el 17404 en 2026).</summary>
        private static int[] BajonPuntual()
        {
            int[] serie = Constante(400);
            for (int k = 0; k < 12; k++)
            {
                serie[FIN - 11 + k] = k < 6 ? 450 : 330;
            }
            return serie;
        }

        /// <summary>Año a año 400, 300, 200 y 150 al mes, y los últimos 12 meses cayendo de 120 a 10.</summary>
        private static int[] DecliveConfirmado()
        {
            int[] serie = new int[N];
            int[] porAnio = { 400, 300, 200, 150 };
            for (int i = 0; i < 48; i++)
            {
                serie[i] = porAnio[i / 12];
            }
            for (int k = 0; k < 12; k++)
            {
                serie[48 + k] = 120 - 10 * k;
            }
            return serie;
        }

        [TestMethod]
        public void Fase_PorEdadYVolumen()
        {
            Assert.AreEqual(FasesProducto.NUEVO, MotorTendencias.Fase(4, 500, P));
            Assert.AreEqual(FasesProducto.MADURO, MotorTendencias.Fase(40, 12, P));
            Assert.AreEqual(FasesProducto.INTERMITENTE, MotorTendencias.Fase(40, 11, P));
            Assert.AreEqual(FasesProducto.INTERMITENTE, MotorTendencias.Fase(null, 0, P));
        }

        [TestMethod]
        public void Maduro_Estable_MaximoYMinimoConLaFormulaDeHoyYVentaPonderada()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90, diasEnServir: 10));

            Assert.AreEqual(FasesProducto.MADURO, r.Fase);
            Assert.AreEqual(TendenciasProducto.ESTABLE, r.Tendencia);
            Assert.AreEqual(3.0m, r.CoberturaMeses);
            Assert.AreEqual(17, r.CoberturaAlcanzableDias); // 10 + 7
            Assert.AreEqual(90, r.CoberturaObjetivoDias);
            Assert.AreEqual(0, r.ExcesoUnidades);
            // D = 1 ud/día: mínimo = 10 + 7; máximo = 7 + 2 × 10
            Assert.AreEqual(17, r.StockMinimoPropuesto);
            Assert.AreEqual(27, r.StockMaximoPropuesto);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR, r.Accion);
        }

        [TestMethod]
        public void Explicacion_CitaLasEntradas()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90, diasEnServir: 10));

            StringAssert.Contains(r.Explicacion, "Maduro. 6 meses: 30,0 uds/mes, ponderada 30,0 × 1,00 estacional (familia sin 3 años)");
            StringAssert.Contains(r.Explicacion, "proveedor 10 días + 7 de seguridad");
            StringAssert.Contains(r.Explicacion, "objetivo 90 días → máximo 27, mínimo 17 (hoy 555/295)");
            StringAssert.Contains(r.Explicacion, "Cobertura 3,0 meses (alcanzable 17 días)");
            StringAssert.Contains(r.Explicacion, "los últimos 12 meses son el pico de 5 años");
        }

        [TestMethod]
        public void Multiplos_SubenLaCoberturaAlcanzableYElMaximoCabeUnLote()
        {
            // 17404: 450 uds/mes (15 al día), cajas de 144, proveedor 10 días
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(450), stock: 1300, diasEnServir: 10, multiplos: 144));

            Assert.AreEqual(10 + 7 + 10, r.CoberturaAlcanzableDias); // 144 ÷ 15 = 9,6 → 10 días
            Assert.AreEqual(255, r.StockMinimoPropuesto);           // 15 × 17
            Assert.AreEqual(405, r.StockMaximoPropuesto);           // 15 × 27 (≥ 255 + 144)
            StringAssert.Contains(r.Explicacion, "múltiplos de 144");
        }

        [TestMethod]
        public void Exceso_ValoradoSobreLaCoberturaObjetivo()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 1000, precio: 2m));

            Assert.AreEqual(910, r.ExcesoUnidades);  // 1000 − 3 meses × 30
            Assert.AreEqual(1820m, r.ExcesoValorado);
        }

        [TestMethod]
        public void Exceso_NuncaPorDebajoDeLaCoberturaAlcanzable()
        {
            // Proveedor de 120 días: la alcanzable (127) manda sobre los 90 configurados
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 200, diasEnServir: 120));

            Assert.AreEqual(127, r.CoberturaObjetivoDias);
            Assert.AreEqual(200 - 127, r.ExcesoUnidades);
        }

        [TestMethod]
        public void Estacional_ConIndicesDeFamilia_NoEsTendenciaYElFactorMiraLosProximosMeses()
        {
            int[] serie = new int[N];
            for (int i = 0; i < N; i++)
            {
                int mes = CalculadoraSeries.MesDelAnio(MesReferencia, FIN, i);
                serie[i] = mes == 10 || mes == 11 ? 60 : 30;
            }
            decimal[] indices = CalculadoraSeries.IndicesEstacionales(serie, FIN, MesReferencia, 120, 0);
            DatosProductoTendencia d = Producto(ventas: serie, stock: 100);

            ResultadoTendencia septiembre = MotorTendencias.Calcular(d, indices, MesReferencia, P);
            Assert.AreEqual(TendenciasProducto.ESTABLE, septiembre.Tendencia);
            Assert.AreEqual(Math.Round(30m / 35m, 2), septiembre.FactorEstacional); // octubre
            Assert.IsTrue(septiembre.ConEstacionalidad);

            // Un mes después (la serie desplazada), el próximo es noviembre: el factor sube
            int[] desplazada = serie.Skip(1).Concat(new[] { 30 }).ToArray();
            ResultadoTendencia octubre = MotorTendencias.Calcular(Producto(ventas: desplazada, stock: 100), indices, MesReferencia.AddMonths(1), P);
            Assert.AreEqual(Math.Round(60m / 35m, 2), octubre.FactorEstacional);
            Assert.IsTrue(octubre.StockMaximoPropuesto > septiembre.StockMaximoPropuesto);
        }

        [TestMethod]
        public void BajonPuntual_ConContextoBueno_NoEsDeclive()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: BajonPuntual(), stock: 1000));

            Assert.IsTrue(r.PendienteRelativa <= P.PendienteBaja, $"pendiente {r.PendienteRelativa}");
            Assert.IsTrue(r.PorcentajeDelPico > 90m);
            Assert.AreEqual(TendenciasProducto.BAJON_PUNTUAL, r.Tendencia);
            Assert.AreNotEqual(AccionesPropuesta.LIQUIDAR, r.Accion);
            Assert.AreNotEqual(AccionesPropuesta.ESTADO_1, r.Accion);
            StringAssert.Contains(r.Explicacion, "Bajón puntual");
        }

        [TestMethod]
        public void DecliveConfirmado_ConMuchaCobertura_Liquidar()
        {
            // venta media de 6 meses = 35: 600 uds son 17 meses
            ResultadoTendencia r = Calcular(Producto(ventas: DecliveConfirmado(), stock: 600));

            Assert.AreEqual(TendenciasProducto.DECLIVE, r.Tendencia);
            Assert.AreEqual(4, r.AniosDesdePico);
            Assert.AreEqual(AccionesPropuesta.LIQUIDAR, r.Accion);
            Assert.AreEqual(0, r.StockMaximoPropuesto);
            StringAssert.Contains(r.Explicacion, "Outlet o Ganavisiones");
            StringAssert.Contains(r.Explicacion, "declive confirmado");
        }

        [TestMethod]
        public void DecliveConfirmado_CoberturaIntermedia_PasarAEstado1()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: DecliveConfirmado(), stock: 300)); // 8,6 meses

            Assert.AreEqual(AccionesPropuesta.ESTADO_1, r.Accion);
            Assert.IsNull(r.StockMaximoPropuesto);
            StringAssert.StartsWith(r.Explicacion, "Pasar a estado 1 (sobre pedido, sin máximos)");
        }

        [TestMethod]
        public void SinSalidaEnSeisMeses_ConStock_Liquidar()
        {
            int[] ventas = Constante(20);
            for (int i = FIN - 5; i <= FIN; i++)
            {
                ventas[i] = 0;
            }

            ResultadoTendencia r = Calcular(Producto(ventas: ventas, stock: 50, precio: 3m));

            Assert.AreEqual(AccionesPropuesta.LIQUIDAR, r.Accion);
            Assert.IsNull(r.CoberturaMeses);
            Assert.AreEqual(150m, r.ExcesoValorado);
            StringAssert.Contains(r.Explicacion, "ninguna venta en 6 meses");
        }

        [TestMethod]
        public void Intermitente_MaximoDeCeroADos()
        {
            int[] pocas = new int[N];
            pocas[FIN - 2] = 1;
            pocas[FIN - 7] = 2;
            int[] algunas = new int[N];
            for (int k = 0; k < 9; k++)
            {
                algunas[FIN - k] = 1;
            }

            ResultadoTendencia uno = Calcular(Producto(ventas: pocas, stock: 3));
            ResultadoTendencia dos = Calcular(Producto(ventas: algunas, stock: 3));

            Assert.AreEqual(FasesProducto.INTERMITENTE, uno.Fase);
            Assert.AreEqual(1, uno.StockMaximoPropuesto);
            Assert.AreEqual(0, uno.StockMinimoPropuesto);
            Assert.AreEqual(2, dos.StockMaximoPropuesto);
            Assert.AreEqual(1, dos.StockMinimoPropuesto);
            StringAssert.Contains(uno.Explicacion, "Intermitente: 3 uds en 12 meses → máximo 1, mínimo 0");
            Assert.IsNull(uno.Tendencia);
        }

        [TestMethod]
        public void Intermitente_SeisMesesSinVender_NoSeLiquidaSiVendioEnElAnio()
        {
            int[] ventas = new int[N];
            ventas[FIN - 8] = 2;

            ResultadoTendencia r = Calcular(Producto(ventas: ventas, stock: 2));

            Assert.AreEqual(FasesProducto.INTERMITENTE, r.Fase);
            Assert.AreNotEqual(AccionesPropuesta.LIQUIDAR, r.Accion);
        }

        [TestMethod]
        public void Nuevo_SinRecompra_TopeCorto()
        {
            // ImGolden: venta inicial alta en junio y nadie repite
            DatosProductoTendencia d = Producto("50001", ventas: Ultimos(200, 20, 5, 0), stock: 400);
            d.FechaPrimeraVenta = new DateTime(2026, 6, 5);
            d.FechaPrimeraEntrada = new DateTime(2026, 6, 1);
            d.VentasPorCliente = Enumerable.Range(1, 50)
                .Select(c => new VentaClienteTendencia { Cliente = c.ToString(), Fecha = new DateTime(2026, 6, 5).AddDays(c % 20) })
                .ToList();

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(FasesProducto.NUEVO, r.Fase);
            Assert.AreEqual(3, r.MesesDesdePrimeraVenta);
            Assert.AreEqual(0m, r.TasaRecompra);
            Assert.IsTrue(r.Velocidad > 50m);
            StringAssert.Contains(r.Explicacion, "recompra 0 % (baja: tope 30 días)");
            int maximoEsperado = (int)Math.Ceiling(r.Velocidad.Value / 30m * 30);
            Assert.AreEqual(maximoEsperado, r.StockMaximoPropuesto);
        }

        [TestMethod]
        public void Nuevo_ConPendientes_SubeElMaximo()
        {
            // 45958: recién llegado, aún sin ventas servidas y con 146 uds pendientes de servir
            DatosProductoTendencia d = Producto("45958", ventas: new int[N], stock: 0, diasEnServir: 15);
            d.FechaPrimeraVenta = null;
            d.FechaPrimeraEntrada = new DateTime(2026, 9, 8);
            d.UnidadesPendientes = 146;
            d.StockMaximoAlg = 54;
            d.StockMinimoAlg = 30;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(FasesProducto.NUEVO, r.Fase);
            Assert.AreEqual(146, r.DemandaNoServida);
            Assert.AreEqual(146, r.StockMaximoPropuesto);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR, r.Accion);
            StringAssert.Contains(r.Explicacion, "+ 146 pendientes de servir = 146");
            StringAssert.Contains(r.Explicacion, "aún sin ventas");
        }

        [TestMethod]
        public void Nuevo_ConRecompra_TopeNormal()
        {
            DatosProductoTendencia d = Producto("50002", ventas: Ultimos(30, 30, 30, 30, 30), stock: 40);
            d.FechaPrimeraVenta = new DateTime(2026, 5, 2);
            d.VentasPorCliente = new List<VentaClienteTendencia>
            {
                new VentaClienteTendencia { Cliente = "1", Fecha = new DateTime(2026, 5, 2) },
                new VentaClienteTendencia { Cliente = "1", Fecha = new DateTime(2026, 7, 2) }
            };

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(100m, r.TasaRecompra);
            StringAssert.Contains(r.Explicacion, "tope 45 días");
        }

        [TestMethod]
        public void PedidoDeCompraAbiertoYExceso_PararCompras()
        {
            DatosProductoTendencia d = Producto(ventas: Constante(30), stock: 1000);
            d.UnidadesPedidoCompraAbierto = 200;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(AccionesPropuesta.PARAR_COMPRAS, r.Accion);
            Assert.AreEqual(27, r.StockMaximoPropuesto);
            StringAssert.StartsWith(r.Explicacion, "Parar compras: hay 200 uds en pedidos de compra abiertos y sobran 910 uds");
        }

        [TestMethod]
        public void Estado4ConStock_Liquidar()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 10, estado: 4));

            Assert.AreEqual(AccionesPropuesta.LIQUIDAR, r.Accion);
            StringAssert.Contains(r.Explicacion, "está en estado 4");
        }

        [TestMethod]
        public void Estado1_NoProponeMaximos()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 20, estado: 1));

            Assert.AreEqual(AccionesPropuesta.MANTENER, r.Accion);
            Assert.IsNull(r.StockMaximoPropuesto);
        }

        [TestMethod]
        public void MaximoYMinimoIgualesALosDeHoy_Mantener()
        {
            DatosProductoTendencia d = Producto(ventas: Constante(30), stock: 90);
            d.StockMaximoAlg = 27;
            d.StockMinimoAlg = 17;

            Assert.AreEqual(AccionesPropuesta.MANTENER, Calcular(d).Accion);
        }

        [TestMethod]
        public void DiasEnServirSinProveedor_SeSuponenYSeDice()
        {
            DatosProductoTendencia d = Producto(ventas: Constante(30), stock: 90);
            d.DiasEnServir = null;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(P.DiasEnServirPorDefecto, r.DiasEnServir);
            StringAssert.Contains(r.Explicacion, "días (supuestos)");
        }

        [TestMethod]
        public void OrdenarPorExceso_MasEurosPrimero()
        {
            List<ResultadoTendencia> ordenados = MotorTendencias.OrdenarPorExceso(new[]
            {
                new ResultadoTendencia { Producto = "A", ExcesoValorado = 10m },
                new ResultadoTendencia { Producto = "B", ExcesoValorado = 500m },
                new ResultadoTendencia { Producto = "C", ExcesoValorado = 0m, Stock = 10, PrecioMedio = 5m },
                new ResultadoTendencia { Producto = "D", ExcesoValorado = 0m, Stock = 1, PrecioMedio = 5m }
            });

            CollectionAssert.AreEqual(new[] { "B", "A", "C", "D" }, ordenados.Select(r => r.Producto).ToArray());
        }

        [TestMethod]
        public void EsRelevante_SinStockNiVentaNiPendientes_No()
        {
            DatosProductoTendencia muerto = Producto(ventas: new int[N], stock: 0);
            DatosProductoTendencia conPendientes = Producto(ventas: new int[N], stock: 0);
            conPendientes.UnidadesPendientes = 3;

            Assert.IsFalse(MotorTendencias.EsRelevante(muerto));
            Assert.IsTrue(MotorTendencias.EsRelevante(conPendientes));
        }

        [TestMethod]
        public void Entradas_GuardaLaSerieEnJson()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90));

            StringAssert.Contains(r.EntradasJson, "\"mesReferencia\":\"2026-09\"");
            StringAssert.Contains(r.EntradasJson, "\"ventas\":[30,");
        }
    }
}
