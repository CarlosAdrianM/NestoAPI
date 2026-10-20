using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#678: los nuevos (fase A) con la velocidad hasta hoy y la caída reciente. Mes de referencia septiembre de 2026
    /// (corte el 01/10/2026) y «hoy» el 20/10/2026, como el 45958 (SERUM PERLÉ).
    /// </summary>
    [TestClass]
    public class MotorTendenciasNuevosTests
    {
        private static readonly ParametrosTendencias P = new ParametrosTendencias();
        private static readonly DateTime Corte = new DateTime(2026, 10, 1);
        private static readonly DateTime Hoy = new DateTime(2026, 10, 20);

        private static ResultadoTendencia Calcular(DatosProductoTendencia d) => MotorTendencias.Calcular(d, null, MesReferencia, P);

        /// <summary><paramref name="dias"/> días seguidos desde <paramref name="desde"/> con <paramref name="cantidad"/> uds cada uno.</summary>
        private static List<VentaDiariaTendencia> Dias(DateTime desde, int dias, int cantidad)
            => Enumerable.Range(0, dias).Select(i => new VentaDiariaTendencia { Fecha = desde.AddDays(i), Cantidad = cantidad }).ToList();

        /// <summary>El 45958: primera venta el 08/10, 125 uds hasta el 19/10 (12 días), proveedor y seguridad por defecto (15 + 7).</summary>
        private static DatosProductoTendencia SerumPerle(DateTime? primeraEntrada = null)
        {
            DatosProductoTendencia d = Producto("45958", ventas: new int[N], stock: 0);
            d.DiasEnServir = null; // los del motor por defecto: 15
            d.FechaPrimeraVenta = new DateTime(2026, 10, 8);
            d.FechaPrimeraEntrada = primeraEntrada ?? new DateTime(2026, 9, 8);
            d.StockMaximoAlg = 54;
            d.StockMinimoAlg = 30;
            d.FechaDatos = Hoy;
            d.VentasDiarias = Dias(new DateTime(2026, 10, 8), 12, 10);
            d.VentasDiarias[0].Cantidad = 15; // 15 + 11 × 10 = 125
            return d;
        }

        [TestMethod]
        public void NuevoDeEsteMes_45958_VelocidadHastaHoyConMaximoRazonable()
        {
            DatosProductoTendencia d = SerumPerle();
            Assert.AreEqual(125, d.VentasDiarias.Sum(v => v.Cantidad));

            ResultadoTendencia r = Calcular(d);

            // 125 uds en 12 días, contados como 30 (mínimo): 125 × 30,44 ÷ 30 = 126,83 uds/mes; recompra aún por medir: tope 30 días
            Assert.AreEqual(FasesProducto.NUEVO, r.Fase);
            Assert.AreEqual(0, r.MesesDesdePrimeraVenta);
            Assert.AreEqual(126.83m, r.Velocidad);
            Assert.AreEqual(15, r.DiasEnServir);
            Assert.AreEqual(7, r.DiasSeguridad);
            Assert.AreEqual(127, r.StockMaximoPropuesto);   // ⌈126,83 ÷ 30 × 30⌉
            Assert.AreEqual(94, r.StockMinimoPropuesto);    // ⌈126,83 ÷ 30 × (15 + 7)⌉
            Assert.AreEqual(AccionesPropuesta.AJUSTAR, r.Accion);
            Assert.AreEqual("A: velocidad hasta hoy × 30 días", r.Formula);
            StringAssert.Contains(r.Explicacion, "Nuevo de este mes: 125 uds en 12 días (del 08/10/2026 al 19/10/2026) = 126,8 uds/mes " +
                "(se cuentan al menos 30 días, para que el arranque no dispare el máximo)");
            StringAssert.Contains(r.Explicacion, "aún es pronto para ver si las ventas caen");
            StringAssert.Contains(r.Explicacion, "recompra aún sin datos (menos de 90 días: tope 30 días)");
            StringAssert.Contains(r.Explicacion, "máximo 127 = 127, mínimo 94 (hoy 54/30)");
            StringAssert.Contains(r.EntradasJson, "\"delMesEnCurso\":true");
        }

        [TestMethod]
        public void NuevoDeEsteMes_ConLaEntradaTambienDeEsteMes_EsNuevoYNoIntermitente()
        {
            ResultadoTendencia r = Calcular(SerumPerle(primeraEntrada: new DateTime(2026, 10, 6)));

            Assert.AreEqual(FasesProducto.NUEVO, r.Fase);
            Assert.AreEqual(127, r.StockMaximoPropuesto);
            Assert.IsTrue(MotorTendencias.EsRelevante(SerumPerle(primeraEntrada: new DateTime(2026, 10, 6))), "sin stock al corte, pero con venta este mes");
        }

        [TestMethod]
        public void NuevoDeEsteMes_ElExcelDelDia1NoVeNadaDelMesNuevo()
        {
            // El día 1 (corte) la primera venta del 8 aún no ha pasado: sale lo mismo que antes de #678.
            DatosProductoTendencia dia1 = SerumPerle();
            dia1.FechaDatos = Corte;
            dia1.VentasDiarias = new List<VentaDiariaTendencia>();
            DatosProductoTendencia antes = SerumPerle();
            antes.FechaPrimeraVenta = null;
            antes.FechaDatos = null;
            antes.VentasDiarias = null;

            ResultadoTendencia r = Calcular(dia1);
            ResultadoTendencia rAntes = Calcular(antes);

            Assert.AreEqual(rAntes.Fase, r.Fase);
            Assert.AreEqual(0, r.StockMaximoPropuesto);
            Assert.AreEqual(rAntes.StockMaximoPropuesto, r.StockMaximoPropuesto);
            Assert.AreEqual(rAntes.StockMinimoPropuesto, r.StockMinimoPropuesto);
        }

        [TestMethod]
        public void NuevoDeEsteMes_UnaVentaElPrimerDia_NoDisparaElMaximo()
        {
            DatosProductoTendencia d = Producto("50010", ventas: new int[N], stock: 0, diasEnServir: 15);
            d.FechaPrimeraVenta = new DateTime(2026, 10, 19);
            d.FechaPrimeraEntrada = new DateTime(2026, 10, 15);
            d.FechaDatos = Hoy;
            d.VentasDiarias = Dias(new DateTime(2026, 10, 19), 1, 1);

            ResultadoTendencia r = Calcular(d);

            // 1 ud ÷ 30 días × 30,44 = 1,01 uds/mes (con 1 día serían 30,44 y el máximo 31)
            Assert.AreEqual(FasesProducto.NUEVO, r.Fase);
            Assert.AreEqual(1.01m, r.Velocidad);
            Assert.AreEqual(2, r.StockMaximoPropuesto);
            Assert.AreEqual(1, r.StockMinimoPropuesto);
            StringAssert.Contains(r.Explicacion, "Nuevo de este mes: 1 ud en 1 día (del 19/10/2026 al 19/10/2026) = 1,0 uds/mes");
        }

        /// <summary>Nuevo desde el 01/07 (111 días hasta hoy): 3 uds al día hasta el 05/10 y luego <paramref name="ultimas"/> al día.</summary>
        private static DatosProductoTendencia NuevoDeJulio(int ultimas)
        {
            DatosProductoTendencia d = Producto("50020", ventas: Ultimos(93, 93, 90), stock: 100, diasEnServir: 15);
            d.FechaPrimeraVenta = new DateTime(2026, 7, 1);
            d.FechaPrimeraEntrada = new DateTime(2026, 6, 25);
            d.FechaDatos = Hoy;
            d.VentasDiarias = Dias(new DateTime(2026, 7, 1), 97, 3).Concat(Dias(new DateTime(2026, 10, 6), 14, ultimas)).ToList();
            return d;
        }

        [TestMethod]
        public void Nuevo_VentasQueCaenEnLasUltimasSemanas_ElMaximoBaja()
        {
            ResultadoTendencia sinCaida = Calcular(NuevoDeJulio(ultimas: 3));
            ResultadoTendencia conCaida = Calcular(NuevoDeJulio(ultimas: 1));

            // Sin caída: 333 uds en 111 días = 91,32 uds/mes; tope 45 días (ya hay 90 días para medir la recompra)
            Assert.AreEqual(91.32m, sinCaida.Velocidad);
            Assert.AreEqual(137, sinCaida.StockMaximoPropuesto);
            StringAssert.Contains(sinCaida.Explicacion, "los últimos 14 días van a 91,3 uds/mes (42 uds): sin caída");
            Assert.AreEqual("A: velocidad hasta hoy × 45 días", sinCaida.Formula);

            // Con caída: 305 uds en 111 días = 83,64 uds/mes, pero los últimos 14 días, 14 uds = 30,44 uds/mes: se usa la reciente
            Assert.AreEqual(30.44m, conCaida.Velocidad);
            Assert.AreEqual(46, conCaida.StockMaximoPropuesto);   // ⌈30,44 ÷ 30 × 45⌉
            Assert.AreEqual(23, conCaida.StockMinimoPropuesto);   // ⌈30,44 ÷ 30 × 22⌉
            Assert.IsTrue(conCaida.StockMaximoPropuesto < sinCaida.StockMaximoPropuesto);
            Assert.AreEqual("A: velocidad reciente × 45 días", conCaida.Formula);
            StringAssert.Contains(conCaida.Explicacion, "Nuevo (3 meses desde la primera venta): 305 uds en 111 días (del 01/07/2026 al 19/10/2026) = 83,6 uds/mes");
            StringAssert.Contains(conCaida.Explicacion, "las ventas de los últimos 14 días han caído un 63,6 % (más del 20 %) a 30,4 uds/mes (14 uds): se usa esa velocidad. Velocidad 30,4 uds/mes");
            StringAssert.Contains(conCaida.EntradasJson, "\"cae\":true");
        }

        [TestMethod]
        public void Nuevo_CaidaReciente_TambienEnElExcelDelDia1()
        {
            // Generado el 01/10: la ventana son los 14 últimos días de septiembre.
            DatosProductoTendencia d = Producto("50021", ventas: Ultimos(93, 93, 62), stock: 100, diasEnServir: 15);
            d.FechaPrimeraVenta = new DateTime(2026, 7, 1);
            d.FechaPrimeraEntrada = new DateTime(2026, 6, 25);
            d.FechaDatos = Corte;
            d.VentasDiarias = Dias(new DateTime(2026, 7, 1), 78, 3).Concat(Dias(new DateTime(2026, 9, 17), 14, 1)).ToList();

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(30.44m, r.Velocidad);
            StringAssert.Contains(r.Explicacion, "las ventas de los últimos 14 días han caído un 62,9 % (más del 20 %) a 30,4 uds/mes (14 uds)");
            StringAssert.Contains(r.Explicacion, "al 30/09/2026");
        }

        /// <summary>Nuevo desde el 01/07: 10 uds al día hasta el 05/10 (970) y <paramref name="ultimos14"/> uds en los 14 últimos días.</summary>
        private static DatosProductoTendencia NuevoConUltimos14(int ultimos14)
        {
            DatosProductoTendencia d = Producto("50022", ventas: Ultimos(310, 310, 300), stock: 100, diasEnServir: 15);
            d.FechaPrimeraVenta = new DateTime(2026, 7, 1);
            d.FechaPrimeraEntrada = new DateTime(2026, 6, 25);
            d.FechaDatos = Hoy;
            d.VentasDiarias = Dias(new DateTime(2026, 7, 1), 97, 10).Concat(Dias(new DateTime(2026, 10, 6), 14, ultimos14 / 14)).ToList();
            d.VentasDiarias[97].Cantidad += ultimos14 % 14;
            return d;
        }

        [TestMethod]
        public void Nuevo_CaidaDel15PorCiento_NoCambiaNada()
        {
            // 1086 uds en 111 días = 297,82 uds/mes; los 14 últimos, 116 uds = 252,22 uds/mes: un 15,3 % menos, no más del 20 %
            ResultadoTendencia r = Calcular(NuevoConUltimos14(116));

            Assert.AreEqual(297.82m, r.Velocidad);
            Assert.AreEqual(447, r.StockMaximoPropuesto);   // ⌈297,82 ÷ 30 × 45⌉
            Assert.AreEqual("A: velocidad hasta hoy × 45 días", r.Formula);
            StringAssert.Contains(r.Explicacion, "los últimos 14 días van a 252,2 uds/mes (116 uds), un 15,3 % menos (no más del 20 %): se sigue con la velocidad hasta hoy");
            StringAssert.Contains(r.EntradasJson, "\"cae\":false");
        }

        [TestMethod]
        public void Nuevo_CaidaDel25PorCiento_SeUsaLaReciente()
        {
            // 1071 uds en 111 días = 293,71 uds/mes; los 14 últimos, 101 uds = 219,60 uds/mes: un 25,2 % menos, más del 20 %
            ResultadoTendencia r = Calcular(NuevoConUltimos14(101));

            Assert.AreEqual(219.60m, r.Velocidad);
            Assert.AreEqual(330, r.StockMaximoPropuesto);   // ⌈219,60 ÷ 30 × 45⌉
            Assert.AreEqual(162, r.StockMinimoPropuesto);   // ⌈219,60 ÷ 30 × 22⌉
            Assert.AreEqual("A: velocidad reciente × 45 días", r.Formula);
            StringAssert.Contains(r.Explicacion, "las ventas de los últimos 14 días han caído un 25,2 % (más del 20 %) a 219,6 uds/mes (101 uds): se usa esa velocidad");
        }

        [TestMethod]
        public void Nuevo_SinVentasPorDias_SigueComoAntes()
        {
            // Sin VentasDiarias (no leídas) la velocidad sale de los meses cerrados, como antes de #678.
            DatosProductoTendencia d = NuevoDeJulio(ultimas: 1);
            d.VentasDiarias = null;
            d.FechaDatos = null;

            ResultadoTendencia r = Calcular(d);

            Assert.AreEqual(CalculadoraSeries.Velocidad(d.Ventas, FIN, new DateTime(2026, 7, 1), Corte), r.Velocidad);
            Assert.AreEqual("A: velocidad × 45 días", r.Formula);
            StringAssert.Contains(r.Explicacion, "Nuevo (3 meses desde la primera venta): velocidad");
        }

        [TestMethod]
        public void Maduro_LasVentasPorDiasYLaFechaDeHoyNoCambianNada()
        {
            DatosProductoTendencia sin = Producto(ventas: Constante(30), stock: 90);
            DatosProductoTendencia con = Producto(ventas: Constante(30), stock: 90);
            con.FechaDatos = Hoy;
            con.VentasDiarias = Dias(new DateTime(2026, 10, 6), 14, 0);

            ResultadoTendencia rSin = Calcular(sin);
            ResultadoTendencia rCon = Calcular(con);

            Assert.AreEqual(FasesProducto.MADURO, rCon.Fase);
            Assert.AreEqual(rSin.StockMaximoPropuesto, rCon.StockMaximoPropuesto);
            Assert.AreEqual(rSin.StockMinimoPropuesto, rCon.StockMinimoPropuesto);
            Assert.AreEqual(rSin.Formula, rCon.Formula);
            Assert.AreEqual(rSin.Explicacion, rCon.Explicacion);
        }

        [TestMethod]
        public void Intermitente_DeSiempreQueSeVendePorPrimeraVezEsteMes_NoSeVuelveNuevo()
        {
            DatosProductoTendencia antes = Producto("50030", ventas: new int[N], stock: 3);
            antes.FechaPrimeraVenta = null;
            antes.FechaPrimeraEntrada = new DateTime(2015, 1, 2);
            DatosProductoTendencia ahora = Producto("50030", ventas: new int[N], stock: 3);
            ahora.FechaPrimeraVenta = new DateTime(2026, 10, 10);
            ahora.FechaPrimeraEntrada = new DateTime(2015, 1, 2);
            ahora.FechaDatos = Hoy;
            ahora.VentasDiarias = Dias(new DateTime(2026, 10, 10), 1, 1);

            ResultadoTendencia rAntes = Calcular(antes);
            ResultadoTendencia rAhora = Calcular(ahora);

            Assert.AreEqual(rAntes.Fase, rAhora.Fase);
            Assert.AreNotEqual(FasesProducto.NUEVO, rAhora.Fase);
            Assert.AreEqual(rAntes.MesesDesdePrimeraVenta, rAhora.MesesDesdePrimeraVenta);
            Assert.AreEqual(rAntes.StockMaximoPropuesto, rAhora.StockMaximoPropuesto);
            Assert.AreEqual(rAntes.Accion, rAhora.Accion);
        }

        [TestMethod]
        public void DiasVentanaReciente_MitadDeLaVidaHasta14YAlMenos7()
        {
            Assert.AreEqual(0, CalculadoraSeries.DiasVentanaReciente(12, 14));
            Assert.AreEqual(0, CalculadoraSeries.DiasVentanaReciente(13, 14));
            Assert.AreEqual(7, CalculadoraSeries.DiasVentanaReciente(14, 14));
            Assert.AreEqual(10, CalculadoraSeries.DiasVentanaReciente(20, 14));
            Assert.AreEqual(14, CalculadoraSeries.DiasVentanaReciente(111, 14));
        }

        [TestMethod]
        public void FechaDatos_HoySiEsElUltimoMesCerradoYElCorteSiEsUnMesPasado()
        {
            DateTime ahora = new DateTime(2026, 10, 20, 10, 30, 0);
            Assert.AreEqual(new DateTime(2026, 10, 20), MotorTendencias.FechaDatos(new DateTime(2026, 9, 1), ahora));
            Assert.AreEqual(new DateTime(2026, 9, 1), MotorTendencias.FechaDatos(new DateTime(2026, 8, 1), ahora));
            Assert.AreEqual(new DateTime(2026, 10, 1), MotorTendencias.FechaDatos(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1, 6, 0, 0)));
            // Un mes que aún no ha cerrado: hasta su corte
            Assert.AreEqual(new DateTime(2026, 11, 1), MotorTendencias.FechaDatos(new DateTime(2026, 10, 1), ahora));
        }

        [TestMethod]
        public void Parametros_NuevosConfigurables()
        {
            Assert.AreEqual(30, P.DiasMinimosVelocidadNuevo);
            Assert.AreEqual(14, P.DiasVentanaRecienteNuevos);
            Assert.AreEqual(30, P.TopeDiasFaseASinDatosRecompra);
            Assert.AreEqual(0.20m, P.CaidaMinimaNuevos);
            ParametrosTendencias p = ParametrosTendencias.Desde(c => c == "PropuestasStock:DiasMinimosVelocidadNuevo" ? "21"
                : c == "PropuestasStock:DiasVentanaRecienteNuevos" ? "28" : c == "PropuestasStock:TopeDiasFaseASinDatosRecompra" ? "20"
                : c == "PropuestasStock:CaidaMinimaNuevos" ? "0,3" : null);
            Assert.AreEqual(21, p.DiasMinimosVelocidadNuevo);
            Assert.AreEqual(28, p.DiasVentanaRecienteNuevos);
            Assert.AreEqual(20, p.TopeDiasFaseASinDatosRecompra);
            Assert.AreEqual(0.3m, p.CaidaMinimaNuevos);
            StringAssert.Contains(string.Join("|", P.Descripcion().Select(x => x.Nombre + "=" + x.Valor)), "CaidaMinimaNuevos=0.20");
        }

        [TestMethod]
        public void Sql_VentasPorDiasYPrimeraVentaHastaHoy()
        {
            string sql = RepositorioTendenciasSql.SQL_VENTAS_DIA;
            StringAssert.Contains(sql, "WITH (NOLOCK)");
            StringAssert.Contains(sql, "l.Producto IN ({0})");
            StringAssert.Contains(sql, "l.[Fecha Albarán] >= @desdeDia AND l.[Fecha Albarán] < @hoy");
            StringAssert.Contains(sql, "GROUP BY RTRIM(l.Producto), CAST(l.[Fecha Albarán] AS date)");
            StringAssert.Contains(sql, DemandaStock.SQL_FILTRO_DEMANDA);
            StringAssert.Contains(RepositorioTendenciasSql.SQL_PRIMERA_VENTA, "l.[Fecha Albarán] < @hoy");
            StringAssert.Contains(RepositorioTendenciasSql.SQL_VENTAS_CLIENTE, "l.[Fecha Albarán] < @hoy");
        }
    }
}
