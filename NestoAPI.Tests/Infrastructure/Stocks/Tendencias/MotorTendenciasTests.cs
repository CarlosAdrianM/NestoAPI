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
        public void Multiplos_NoSubenLaCoberturaAlcanzableNiElMaximo()
        {
            // 17404: 450 uds/mes (15 al día), cajas de 144, proveedor 10 días. NestoAPI#630 (16/10/26): el lote se respeta al pedir.
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(450), stock: 1300, diasEnServir: 10, multiplos: 144));

            Assert.AreEqual(10 + 7, r.CoberturaAlcanzableDias);
            Assert.AreEqual(255, r.StockMinimoPropuesto);           // 15 × 17
            Assert.AreEqual(405, r.StockMaximoPropuesto);           // 15 × 27
            StringAssert.Contains(r.Explicacion, "múltiplos de 144 (se respetan al pedir, no en el máximo)");
            Assert.IsFalse(r.Explicacion.Contains("proponga al proveedor"), r.Explicacion); // 144 uds son 0,3 meses
        }

        [TestMethod]
        public void Multiplos_Caso42274_MaximoSinElLoteYAvisoDeMultiploGrande()
        {
            // 42274 en septiembre de 2026 (EntradasJson real): ALG múltiplo 12, ponderada 1,44 × 1,04 estacional, proveedor 20 días
            // + 7. Antes: mínimo 2 / máximo 14 (2 + 12) y objetivo 243 días (el lote, 12 ÷ 1,67 × 30 = 216 días, subía la alcanzable).
            int[] ventas = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, 7, 0, 0, 0, 1, 2, 5, 2, 0, 0, 1, 1, 0, 3, 0, 5, 4, 2, 2, 0, 3, 1,
                2, 1, 0, 0, 1, 2, 0, 1, 1, 0, 0, 0, 0, 5, 0, 0, 1, 0, 2, 3, 1, 2, 0, 2 };
            decimal[] indices = { 0.8930936613m, 1.2992746768m, 0.9801324503m, 1.1542100284m, 0.9649952696m, 0.8325449385m,
                1.0343740145m, 0.4213181962m, 1.4985808893m, 1.0444654683m, 1.0305897193m, 0.8464206875m };
            DatosProductoTendencia d = Producto("42274", ventas: ventas, stock: 248, diasEnServir: 20, multiplos: 12);
            d.FechaPrimeraVenta = new DateTime(2022, 9, 8);
            d.StockMaximoAlg = 2;
            d.StockMinimoAlg = 1;

            ResultadoTendencia r = Calcular(d, indices);

            Assert.AreEqual(1.44m, r.VentaMediaPonderada);
            Assert.AreEqual(1.04m, r.FactorEstacional);
            Assert.AreEqual(2, r.StockMinimoPropuesto);
            Assert.AreEqual(3, r.StockMaximoPropuesto);
            Assert.AreEqual(27, r.CoberturaAlcanzableDias);
            Assert.AreEqual(90, r.CoberturaObjetivoDias);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR, r.Accion); // el aviso no cambia la acción
            Assert.AreEqual(7.2m, r.MesesVentaMultiplo);          // 12 ÷ 1,67
            StringAssert.Contains(r.Explicacion, "El múltiplo de compra (12 uds) son 7,2 meses de venta: proponga al proveedor un múltiplo menor.");
        }

        [TestMethod]
        public void Multiplos_LoteGrande_SeConfiguraYSinComprarNoSeAvisa()
        {
            ParametrosTendencias p = ParametrosTendencias.Desde(c => c == "PropuestasStock:MesesLoteGrande" ? "12" : null);
            DatosProductoTendencia d = Producto(ventas: Constante(2), stock: 5, multiplos: 20); // 10 meses de venta

            Assert.IsFalse(MotorTendencias.Calcular(d, null, MesReferencia, p).Explicacion.Contains("proponga al proveedor"));
            Assert.IsTrue(Calcular(d).Explicacion.Contains("son 10,0 meses de venta: proponga al proveedor un múltiplo menor"));

            // Estado 4 con stock: liquidar, no se vuelve a comprar → sin aviso aunque el múltiplo sean 10 meses
            DatosProductoTendencia liquidar = Producto(ventas: Constante(2), stock: 50, multiplos: 20, estado: 4);
            ResultadoTendencia r = Calcular(liquidar);
            Assert.AreEqual(AccionesPropuesta.LIQUIDAR, r.Accion);
            Assert.IsFalse(r.Explicacion.Contains("proponga al proveedor"), r.Explicacion);
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
            StringAssert.Contains(r.Explicacion, "+ 146 pendientes de servir (vencidos) = 146");
            StringAssert.Contains(r.Explicacion, "aún sin ventas");
        }

        // ---------- NestoAPI#656: servido con retraso ----------

        /// <summary>Un nuevo de 5 meses que vende 30 al mes, sin recompra conocida: máximo por velocidad 30 × 45 ÷ 30 = 45.</summary>
        private static DatosProductoTendencia NuevoDeCincoMeses()
        {
            DatosProductoTendencia d = Producto("50003", ventas: Ultimos(30, 30, 30, 30, 30), stock: 10);
            d.FechaPrimeraVenta = new DateTime(2026, 5, 1);
            return d;
        }

        [TestMethod]
        public void Nuevo_ConServidoConRetraso_ElMaximoSumaElRetrasoMensual()
        {
            DatosProductoTendencia d = NuevoDeCincoMeses();
            d.UnidadesServidasConRetraso = 48; // en 5 meses: 9,6 → 10 al mes
            d.UnidadesPendientes = 4;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(48, r.ServidoConRetraso);
            Assert.AreEqual(5, r.MesesDesdePrimeraVenta);
            Assert.AreEqual(45 + 10, r.StockMaximoPropuesto);
            StringAssert.Contains(r.Explicacion, "máximo 45 + 10 de retraso al mes (48 uds servidas con más de 2 días de retraso en 5 meses; pendientes vencidos 4) = 55");
            StringAssert.Contains(r.Explicacion, "Servidas con retraso (más de 2 días) en 12 meses: 48 uds.");
            StringAssert.Contains(r.EntradasJson, "\"pendienteVencido\":4,\"servidoConRetraso\":48,\"diasRetraso\":2");
        }

        [TestMethod]
        public void Nuevo_PendienteVencidoMayorQueElRetraso_SumaElPendienteYNoLosDos()
        {
            DatosProductoTendencia d = NuevoDeCincoMeses();
            d.UnidadesServidasConRetraso = 10; // 2 al mes
            d.UnidadesPendientes = 7;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(45 + 7, r.StockMaximoPropuesto);
            StringAssert.Contains(r.Explicacion, "máximo 45 + 7 pendientes de servir (vencidos) = 52");
        }

        [TestMethod]
        public void Maduro_ConServidoConRetraso_NoCambiaElMaximoPeroLoDice()
        {
            DatosProductoTendencia sin = Producto(ventas: Constante(30), stock: 90);
            DatosProductoTendencia con = Producto(ventas: Constante(30), stock: 90);
            con.UnidadesServidasConRetraso = 25;

            ResultadoTendencia rSin = Calcular(sin);
            ResultadoTendencia rCon = Calcular(con);

            Assert.AreEqual(rSin.StockMaximoPropuesto, rCon.StockMaximoPropuesto);
            StringAssert.Contains(rCon.Explicacion, "Servidas con retraso (más de 2 días) en 12 meses: 25 uds.");
            Assert.IsFalse(rSin.Explicacion.Contains("retraso"), rSin.Explicacion);
        }

        [TestMethod]
        public void DiasRetraso_PorDefecto2YConfigurable()
        {
            Assert.AreEqual(2, new ParametrosTendencias().DiasRetraso);
            Assert.AreEqual(5, ParametrosTendencias.Desde(c => c == "PropuestasStock:DiasRetraso" ? "5" : null).DiasRetraso);
        }

        [TestMethod]
        public void Sql_PendienteSoloVencidoYServidoConRetrasoAgregadoPorProducto()
        {
            // Corte 1: solo −1 con la fecha de entrega vencida (ni el estado 1 ni lo de entrega futura)
            StringAssert.Contains(RepositorioTendenciasSql.SQL_PENDIENTES, "l.Estado = -1");
            StringAssert.Contains(RepositorioTendenciasSql.SQL_PENDIENTES, "l.[Fecha Entrega] < @hasta");
            Assert.IsFalse(RepositorioTendenciasSql.SQL_PENDIENTES.Contains("IN (-1, 1)"));
            // Corte 2: una consulta por lote, agrupada por producto, 12 meses y más de @dias días
            string sql = RepositorioTendenciasSql.SQL_SERVIDO_CON_RETRASO;
            StringAssert.Contains(sql, "l.Producto IN ({0})");
            StringAssert.Contains(sql, "GROUP BY RTRIM(l.Producto)");
            StringAssert.Contains(sql, "l.Estado >= 2 AND l.TipoLinea = 1 AND l.Cantidad > 0");
            StringAssert.Contains(sql, "l.[Fecha Albarán] >= @desde12");
            StringAssert.Contains(sql, "DATEDIFF(day, l.[Fecha Entrega], l.[Fecha Albarán]) > @dias");
            StringAssert.Contains(RepositorioTendenciasSql.SQL_LEER, "ServidoConRetraso");
        }

        [TestMethod]
        public void TablaPropuestas_GuardaServidoConRetraso()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90));
            r.ServidoConRetraso = 17;

            System.Data.DataTable t = RepositorioTendenciasSql.TablaPropuestas(MesReferencia, new[] { r }, DateTime.Now);

            Assert.AreEqual(17, t.Rows[0]["ServidoConRetraso"]);
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

        // ---------- NestoAPI#655: fórmula P (fabricación propia) ----------

        private static readonly string[] AccionesPropios = { AccionesPropuesta.AJUSTAR_LOTE, AccionesPropuesta.POTENCIAR, AccionesPropuesta.CADUCA };

        [TestMethod]
        public void Propio_FormulaP_MinimoConSeguridadDePropiosYMaximoSinElLote()
        {
            // 30 uds/mes, proveedor 60 días, lote 100: mínimo = 30 × (60 + 30) ÷ 30 = 90; máximo = 30 × (30 + 2 × 60) ÷ 30 = 150
            // (NestoAPI#630, 16/10/26: antes 90 + 100 = 190; el lote se respeta al fabricar, no en el máximo)
            ResultadoTendencia r = Calcular(Propio(ventas: Constante(30), stock: 150, diasEnServir: 60, lote: 100));

            Assert.IsTrue(r.FabricacionPropia);
            StringAssert.StartsWith(r.Formula, FormulasPropuesta.PREFIJO_PROPIOS);
            Assert.AreEqual(P.DiasSeguridadPropios, r.DiasSeguridad);
            Assert.AreEqual(90, r.StockMinimoPropuesto);
            Assert.AreEqual(150, r.StockMaximoPropuesto);
            Assert.AreEqual(0, r.ExcesoUnidades);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR_LOTE, r.Accion);
            StringAssert.Contains(r.Explicacion, "proveedor 60 días + 30 de seguridad de propios → mínimo 90; máximo = 30,0 × (30 + 2 × 60) ÷ 30 = 150; lote mínimo 100 (se respeta al fabricar, no en el máximo); vida útil 12 meses (grupo COS) = 360 uds");
            StringAssert.Contains(r.Explicacion, "(hoy 555/295)");
            StringAssert.Contains(r.EntradasJson, "\"tipoExclusiva\":\"PRP\"");
            StringAssert.Contains(r.EntradasJson, "\"propios\":{\"lote\":100,\"diasSeguridad\":30,\"grupo\":\"COS\",\"caduca\":true,\"vidaUtilMeses\":12,\"tope\":360}");
        }

        [TestMethod]
        public void Propio_MaximoYMinimoIgualesALosDeHoy_Mantener()
        {
            DatosProductoTendencia d = Propio(ventas: Constante(30), stock: 150, diasEnServir: 60, lote: 100);
            d.StockMaximoAlg = 150;
            d.StockMinimoAlg = 90;

            Assert.AreEqual(AccionesPropuesta.MANTENER, Calcular(d).Accion);
        }

        [TestMethod]
        public void Propio_TopeDeVidaUtil_ElMaximoNuncaPasaDeDoceMesesDeVenta()
        {
            // Proveedor de 300 días: mínimo 330, máximo 30 × (30 + 600) ÷ 30 = 630, pero 12 meses de venta son 360
            ResultadoTendencia r = Calcular(Propio(ventas: Constante(30), stock: 200, diasEnServir: 300, lote: 100));

            Assert.AreEqual(360, r.StockMaximoPropuesto);
            Assert.AreEqual(330, r.StockMinimoPropuesto);
            StringAssert.Contains(r.Explicacion, "máximo = 30,0 × (30 + 2 × 300) ÷ 30 = 630 (acotado a 360 por la vida útil de 12 meses)");
            Assert.IsFalse(r.Explicacion.Contains("pedir lote menor"), r.Explicacion);
        }

        [TestMethod]
        public void Propio_LoteDeMasMesesQueLaVidaUtil_LoDiceYProponeAjustarElLote()
        {
            // Lote de 500 con 30 uds/mes: 16,7 meses de venta, más que los 12 de vida útil
            DatosProductoTendencia d = Propio(ventas: Constante(30), stock: 150, diasEnServir: 60, lote: 500);
            d.StockMaximoAlg = 150;
            d.StockMinimoAlg = 90;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(150, r.StockMaximoPropuesto);
            Assert.AreEqual(90, r.StockMinimoPropuesto);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR_LOTE, r.Accion); // aunque el máximo y el mínimo sean los de hoy
            StringAssert.Contains(r.Explicacion, "El lote mínimo son 16,7 meses de venta, más que la vida útil: pedir lote menor");
        }

        [TestMethod]
        public void Propio_SobraPeroSeVendeAntesDeCaducar_PotenciarVentas()
        {
            ResultadoTendencia r = Calcular(Propio(ventas: Constante(30), stock: 300, diasEnServir: 60, lote: 100, precio: 2m));

            Assert.AreEqual(AccionesPropuesta.POTENCIAR, r.Accion);
            Assert.AreEqual(150, r.ExcesoUnidades); // 300 − 150
            Assert.AreEqual(300m, r.ExcesoValorado);
            Assert.AreEqual(150, r.StockMaximoPropuesto);
            StringAssert.StartsWith(r.Explicacion, "Potenciar ventas: sobran 150 uds (300,00 €) sobre el máximo, pero 300 uds son 10,0 meses de venta");
            StringAssert.Contains(r.Explicacion, "nunca Outlet ni liquidar");
        }

        [TestMethod]
        public void Propio_MasStockQueVidaUtil_CaducaAntesDeVenderse()
        {
            DatosProductoTendencia d = Propio(ventas: Constante(30), stock: 1000, diasEnServir: 60, lote: 100, precio: 2m);
            d.UnidadesPedidoCompraAbierto = 200;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(AccionesPropuesta.CADUCA, r.Accion); // y no «Parar compras»
            Assert.AreEqual(850, r.ExcesoUnidades);              // exceso = stock − máximo (150)
            StringAssert.StartsWith(r.Explicacion, "Caduca antes de venderse: 1000 uds son 33,3 meses de venta, más que la vida útil de 12 meses; 640 uds (1.280,00 €) caducarán");
            StringAssert.Contains(r.Explicacion, "Hay 200 uds en pedidos de compra abiertos (el siguiente lote): revisarlo.");
        }

        [TestMethod]
        public void Propio_SinSalidaEnSeisMeses_NuncaSeLiquida()
        {
            int[] ventas = Constante(20);
            for (int i = FIN - 5; i <= FIN; i++)
            {
                ventas[i] = 0;
            }

            ResultadoTendencia normal = Calcular(Producto(ventas: ventas, stock: 50));
            ResultadoTendencia propio = Calcular(Propio(ventas: ventas, stock: 50));

            Assert.AreEqual(AccionesPropuesta.LIQUIDAR, normal.Accion);
            Assert.AreEqual(AccionesPropuesta.CADUCA, propio.Accion);
            Assert.AreEqual(0, propio.StockMaximoPropuesto);
            Assert.AreEqual(50, propio.ExcesoUnidades);
            StringAssert.Contains(propio.Explicacion, "no se vende nada y quedan 50 uds");
            StringAssert.Contains(propio.Explicacion, "Sin venta: cualquier lote caduca antes de venderse");
        }

        [TestMethod]
        public void Propio_EnDeclive_NiSeLiquidaNiPasaAEstado1()
        {
            foreach (int stock in new[] { 300, 600 })
            {
                ResultadoTendencia normal = Calcular(Producto(ventas: DecliveConfirmado(), stock: stock));
                ResultadoTendencia propio = Calcular(Propio(ventas: DecliveConfirmado(), stock: stock));

                CollectionAssert.Contains(new[] { AccionesPropuesta.LIQUIDAR, AccionesPropuesta.ESTADO_1 }, normal.Accion);
                CollectionAssert.Contains(AccionesPropios, propio.Accion, $"stock {stock}: {propio.Accion}");
                Assert.IsFalse(propio.Explicacion.Contains("Outlet o Ganavisiones"), propio.Explicacion);
            }
        }

        [TestMethod]
        public void Propio_EnEstado4_NoSeFabricaOtroLotePeroNoSeLiquida()
        {
            ResultadoTendencia r = Calcular(Propio(ventas: Constante(30), stock: 10, estado: 4));

            Assert.AreEqual(AccionesPropuesta.POTENCIAR, r.Accion);
            Assert.AreEqual(0, r.StockMaximoPropuesto);
            Assert.AreEqual(0, r.StockMinimoPropuesto);
            Assert.AreEqual("P: estado 4", r.Formula);
            StringAssert.Contains(r.Explicacion, "fabricación propia en estado 4, no se fabrica otro lote");
        }

        // ---------- NestoAPI#655: vida útil por grupo (Carlos, 16/10/26) ----------

        [TestMethod]
        public void VidaUtil_PorDefecto_Cosmetica12Peluqueria24YAparatosYAccesoriosNoCaducan()
        {
            Assert.AreEqual(12, P.VidaUtilMeses("COS"));
            Assert.AreEqual(24, P.VidaUtilMeses("PEL"));
            Assert.IsNull(P.VidaUtilMeses("ACC"));
            Assert.IsNull(P.VidaUtilMeses("APA"));
            Assert.IsNull(P.VidaUtilMeses("acp "));
            Assert.AreEqual(12, P.VidaUtilMeses("XYZ")); // sin regla: lo conservador
            Assert.AreEqual(12, P.VidaUtilMeses(null));
        }

        [TestMethod]
        public void VidaUtil_SeConfiguraEnAppSettings()
        {
            ParametrosTendencias p = ParametrosTendencias.Desde(c => c == "PropuestasStock:VidaUtilMesesPorGrupo" ? "COS=6; APA=36"
                : c == "PropuestasStock:VidaUtilMesesPropios" ? "9" : null);

            Assert.AreEqual(6, p.VidaUtilMeses("COS"));
            Assert.AreEqual(36, p.VidaUtilMeses("APA"));
            Assert.AreEqual(9, p.VidaUtilMeses("PEL")); // ya no tiene regla propia
            StringAssert.Contains(string.Join("|", p.Descripcion().Select(d => d.Nombre + "=" + d.Valor)), "VidaUtilMesesPorGrupo=COS=6; APA=36");
        }

        [TestMethod]
        public void Propio_Aparato_NoCaduca_SinTopeYPotenciarEnVezDeCaduca()
        {
            // 1000 uds con 30 al mes son 33 meses: en cosmética «Caduca antes de venderse»; un aparato no caduca
            DatosProductoTendencia d = Propio(ventas: Constante(30), stock: 1000, diasEnServir: 300, lote: 100, grupo: "APA");

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(AccionesPropuesta.POTENCIAR, r.Accion);
            Assert.AreEqual(630, r.StockMaximoPropuesto); // 30 × (30 + 600) ÷ 30, sin el tope de 12 meses (360)
            Assert.AreEqual(330, r.StockMinimoPropuesto);
            Assert.AreEqual(370, r.ExcesoUnidades);
            Assert.AreEqual("P: seg. + 2 × días, no caduca", r.Formula);
            StringAssert.StartsWith(r.Explicacion, "Potenciar ventas: sobran 370 uds (740,00 €) sobre el máximo; 1000 uds son 33,3 meses de venta, pero no caducan (nunca Outlet ni liquidar)");
            StringAssert.Contains(r.Explicacion, "máximo = 30,0 × (30 + 2 × 300) ÷ 30 = 630; lote mínimo 100 (se respeta al fabricar, no en el máximo); no caduca (grupo APA): sin tope de vida útil");
            Assert.IsFalse(r.Explicacion.Contains("acotado"), r.Explicacion);
            StringAssert.Contains(r.EntradasJson, "\"propios\":{\"lote\":100,\"diasSeguridad\":30,\"grupo\":\"APA\",\"caduca\":false,\"vidaUtilMeses\":null,\"tope\":null}");
        }

        [TestMethod]
        public void Propio_AccesorioOAparato_NuncaCaducaNiSeLiquida()
        {
            int[] sinVenta = Constante(20);
            for (int i = FIN - 11; i <= FIN; i++)
            {
                sinVenta[i] = 0;
            }
            foreach (string grupo in new[] { "ACC", "APA", "ACP" })
            {
                foreach (DatosProductoTendencia d in new[]
                {
                    Propio(ventas: Constante(30), stock: 5000, grupo: grupo),
                    Propio(ventas: DecliveConfirmado(), stock: 5000, grupo: grupo),
                    Propio(ventas: sinVenta, stock: 50, grupo: grupo),
                    Propio(ventas: Constante(30), stock: 5000, estado: 4, grupo: grupo)
                })
                {
                    ResultadoTendencia r = Calcular(d);

                    Assert.AreEqual(AccionesPropuesta.POTENCIAR, r.Accion, $"{grupo}: {r.Explicacion}");
                    Assert.IsFalse(r.Explicacion.Contains("Caduca") || r.Explicacion.Contains("caducan si") || r.Explicacion.Contains("Outlet o Ganavisiones"), r.Explicacion);
                }
            }
        }

        [TestMethod]
        public void Propio_AparatoSinVenta_NoSeFabricaOtroLote()
        {
            int[] ventas = Constante(20);
            for (int i = FIN - 11; i <= FIN; i++)
            {
                ventas[i] = 0;
            }

            ResultadoTendencia r = Calcular(Propio(ventas: ventas, stock: 50, grupo: "APA"));

            Assert.AreEqual(AccionesPropuesta.POTENCIAR, r.Accion);
            Assert.AreEqual(0, r.StockMaximoPropuesto);
            Assert.AreEqual(0, r.StockMinimoPropuesto);
            Assert.AreEqual(50, r.ExcesoUnidades);
            StringAssert.Contains(r.Explicacion, "Sin venta: no se fabrica otro lote (no caduca, pero no sale)");
        }

        [TestMethod]
        public void Propio_Peluqueria_VidaUtilDe24Meses()
        {
            // 500 uds con 30 al mes son 16,7 meses: en cosmética caducan; en peluquería (24 meses, tope 720) no
            ResultadoTendencia cosmetica = Calcular(Propio(ventas: Constante(30), stock: 500, grupo: "COS"));
            ResultadoTendencia peluqueria = Calcular(Propio(ventas: Constante(30), stock: 500, grupo: "PEL"));

            Assert.AreEqual(AccionesPropuesta.CADUCA, cosmetica.Accion);
            Assert.AreEqual(AccionesPropuesta.POTENCIAR, peluqueria.Accion);
            Assert.AreEqual("P: seg. + 2 × días, tope 24 meses", peluqueria.Formula);
            StringAssert.Contains(peluqueria.Explicacion, "vida útil 24 meses (grupo PEL) = 720 uds");
        }

        [TestMethod]
        public void Propio_GrupoSinRegla_DoceMesesYLoDice()
        {
            ResultadoTendencia r = Calcular(Propio(ventas: Constante(30), stock: 1000, grupo: "XYZ"));

            Assert.AreEqual(AccionesPropuesta.CADUCA, r.Accion);
            StringAssert.Contains(r.Explicacion, "vida útil 12 meses (grupo XYZ sin regla propia: se suponen 12) = 360 uds");
        }

        [TestMethod]
        public void NoPropio_SigueConLaFormulaDeSiempre()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90));

            Assert.IsFalse(r.FabricacionPropia);
            Assert.AreEqual(P.DiasSeguridad, r.DiasSeguridad);
            StringAssert.Contains(r.EntradasJson, "\"propios\":null");
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
