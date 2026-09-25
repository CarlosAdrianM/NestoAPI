using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Models.Cobros;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#544 (c): la cadencia decidida por Carlos (25/09/26): 1.º al cumplir el umbral,
    /// 2.º a los 10 días, 3.º a los 5, 4.º a los 2 y desde ahí diario. Nunca dos el mismo día.
    /// El reloj se reinicia si el cliente paga parte.
    /// </summary>
    [TestClass]
    public class CadenciaAvisosFacturasVencidasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);

        private static AvisoFacturaVencidaDTO Efecto(decimal importe = 100)
            => new AvisoFacturaVencidaDTO { NOrden = 1, Importe = importe };

        private static AvisoFacturaVencidaRegistrado Ultimo(int numero, int haceDias, decimal importe = 100)
            => new AvisoFacturaVencidaRegistrado { NumOrden = 1, NumeroAviso = numero, Fecha = HOY.AddDays(-haceDias), ImportePendiente = importe };

        [TestMethod]
        public void DiasHastaElSiguiente_10_5_2_YLuegoDiario()
        {
            Assert.AreEqual(10, CadenciaAvisosFacturasVencidas.DiasHastaElSiguiente(1));
            Assert.AreEqual(5, CadenciaAvisosFacturasVencidas.DiasHastaElSiguiente(2));
            Assert.AreEqual(2, CadenciaAvisosFacturasVencidas.DiasHastaElSiguiente(3));
            Assert.AreEqual(1, CadenciaAvisosFacturasVencidas.DiasHastaElSiguiente(4));
            Assert.AreEqual(1, CadenciaAvisosFacturasVencidas.DiasHastaElSiguiente(9));
        }

        [TestMethod]
        public void Aplicar_SinMemoria_EsElPrimerAvisoYTocaHoy()
        {
            AvisoFacturaVencidaDTO efecto = Efecto();

            CadenciaAvisosFacturasVencidas.Aplicar(efecto, null, HOY);

            Assert.AreEqual(1, efecto.NumeroAviso);
            Assert.IsTrue(efecto.TocaHoy);
            Assert.IsNull(efecto.FechaUltimoAviso);
            Assert.IsNull(efecto.FechaSiguienteAviso);
            Assert.AreEqual(0, efecto.NumeroUltimoAviso);
            Assert.IsFalse(efecto.ReinicioPorPagoParcial);
        }

        [TestMethod]
        public void Aplicar_PrimerAvisoHaceMenosDe10Dias_NoToca()
        {
            AvisoFacturaVencidaDTO efecto = Efecto();

            CadenciaAvisosFacturasVencidas.Aplicar(efecto, Ultimo(1, haceDias: 9), HOY);

            Assert.AreEqual(2, efecto.NumeroAviso, "El siguiente sería el 2.º");
            Assert.IsFalse(efecto.TocaHoy);
            Assert.AreEqual(HOY.AddDays(1), efecto.FechaSiguienteAviso);
            Assert.AreEqual(HOY.AddDays(-9), efecto.FechaUltimoAviso);
            Assert.AreEqual(1, efecto.NumeroUltimoAviso);
        }

        [TestMethod]
        public void Aplicar_PrimerAvisoHace10DiasOMas_TocaElSegundo()
        {
            AvisoFacturaVencidaDTO justo = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(justo, Ultimo(1, haceDias: 10), HOY);
            Assert.IsTrue(justo.TocaHoy);
            Assert.AreEqual(2, justo.NumeroAviso);

            AvisoFacturaVencidaDTO pasado = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(pasado, Ultimo(1, haceDias: 12), HOY);
            Assert.IsTrue(pasado.TocaHoy, "Si se pasó el día (fin de semana, job caído), toca igual");
            Assert.AreEqual(HOY.AddDays(-2), pasado.FechaSiguienteAviso);
        }

        [TestMethod]
        public void Aplicar_SegundoALos5_TerceroALos2_YDespuesDiario()
        {
            AvisoFacturaVencidaDTO tras2 = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(tras2, Ultimo(2, haceDias: 4), HOY);
            Assert.IsFalse(tras2.TocaHoy);
            CadenciaAvisosFacturasVencidas.Aplicar(tras2, Ultimo(2, haceDias: 5), HOY);
            Assert.IsTrue(tras2.TocaHoy);
            Assert.AreEqual(3, tras2.NumeroAviso);

            AvisoFacturaVencidaDTO tras3 = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(tras3, Ultimo(3, haceDias: 1), HOY);
            Assert.IsFalse(tras3.TocaHoy);
            CadenciaAvisosFacturasVencidas.Aplicar(tras3, Ultimo(3, haceDias: 2), HOY);
            Assert.IsTrue(tras3.TocaHoy);
            Assert.AreEqual(4, tras3.NumeroAviso);

            AvisoFacturaVencidaDTO tras4 = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(tras4, Ultimo(4, haceDias: 1), HOY);
            Assert.IsTrue(tras4.TocaHoy);
            Assert.AreEqual(5, tras4.NumeroAviso);

            AvisoFacturaVencidaDTO tras7 = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(tras7, Ultimo(7, haceDias: 1), HOY);
            Assert.IsTrue(tras7.TocaHoy);
            Assert.AreEqual(8, tras7.NumeroAviso);
        }

        [TestMethod]
        public void Aplicar_NuncaDosElMismoDia()
        {
            AvisoFacturaVencidaDTO diario = Efecto();
            CadenciaAvisosFacturasVencidas.Aplicar(diario, Ultimo(6, haceDias: 0), HOY);
            Assert.IsFalse(diario.TocaHoy, "Ya se ha avisado hoy");
            Assert.AreEqual(HOY.AddDays(1), diario.FechaSiguienteAviso);

            AvisoFacturaVencidaDTO pagoParcialHoy = Efecto(importe: 50);
            CadenciaAvisosFacturasVencidas.Aplicar(pagoParcialHoy, Ultimo(3, haceDias: 0, importe: 100), HOY);
            Assert.IsFalse(pagoParcialHoy.TocaHoy, "Ni aunque haya pagado parte hoy mismo");
        }

        [TestMethod]
        public void Aplicar_SiHaPagadoParte_LaCuentaEmpiezaDeNuevo()
        {
            AvisoFacturaVencidaDTO efecto = Efecto(importe: 60);

            CadenciaAvisosFacturasVencidas.Aplicar(efecto, Ultimo(3, haceDias: 1, importe: 100), HOY);

            Assert.IsTrue(efecto.ReinicioPorPagoParcial);
            Assert.AreEqual(1, efecto.NumeroAviso, "Vuelve a ser el primero");
            Assert.IsTrue(efecto.TocaHoy, "Y sale hoy (el último fue ayer)");
            Assert.AreEqual(3, efecto.NumeroUltimoAviso, "Pero se recuerda el histórico");
        }

        [TestMethod]
        public void Aplicar_SiElPendienteEsIgualOMayor_NoEsPagoParcial()
        {
            AvisoFacturaVencidaDTO igual = Efecto(importe: 100);
            CadenciaAvisosFacturasVencidas.Aplicar(igual, Ultimo(1, haceDias: 3, importe: 100), HOY);
            Assert.IsFalse(igual.ReinicioPorPagoParcial);
            Assert.AreEqual(2, igual.NumeroAviso);

            AvisoFacturaVencidaDTO mayor = Efecto(importe: 120);
            CadenciaAvisosFacturasVencidas.Aplicar(mayor, Ultimo(1, haceDias: 3, importe: 100), HOY);
            Assert.IsFalse(mayor.ReinicioPorPagoParcial, "Un desliquidado que sube el pendiente no reinicia");
        }

        [TestMethod]
        public void Ordinal_EnEspanol()
        {
            Assert.AreEqual("1.er aviso", CadenciaAvisosFacturasVencidas.Ordinal(1));
            Assert.AreEqual("2.º aviso", CadenciaAvisosFacturasVencidas.Ordinal(2));
            Assert.AreEqual("3.er aviso", CadenciaAvisosFacturasVencidas.Ordinal(3));
            Assert.AreEqual("4.º aviso", CadenciaAvisosFacturasVencidas.Ordinal(4));
        }
    }
}
