using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    /// <summary>
    /// NestoAPI#777: el lunes 02/11/2026 (traslado de Todos los Santos en la Comunidad de Madrid) es festivo, y las fiestas
    /// autonómicas cuentan en Algete, Alcobendas, Reina y sin delegación. Con el calendario real de <see cref="GestorFestivos"/>.
    /// Noviembre de 2026: viernes 30/10, sábado 31/10, domingo 01/11 (Todos los Santos), LUNES 02/11 (traslado), martes 03/11.
    /// </summary>
    [TestClass]
    public class GestorFestivosAutonomicasTests
    {
        private static readonly DateTime VIERNES_30_10 = new DateTime(2026, 10, 30);
        private static readonly DateTime LUNES_02_11 = new DateTime(2026, 11, 2);
        private static readonly DateTime MARTES_03_11 = new DateTime(2026, 11, 3);

        [DataTestMethod]
        [DataRow(Constantes.Almacenes.ALGETE)]
        [DataRow(Constantes.Almacenes.ALCOBENDAS)]
        [DataRow(Constantes.Almacenes.REINA)]
        [DataRow("")]
        public void EsFestivo_TrasladoDeTodosLosSantos2026_EsFestivoEnTodas(string delegacion)
        {
            Assert.IsTrue(GestorFestivos.EsFestivo(LUNES_02_11, delegacion));
        }

        [TestMethod]
        public void EsFestivo_TrasladoDeTodosLosSantos2026_TambienSinDelegacion()
        {
            Assert.IsTrue(GestorFestivos.EsFestivo(LUNES_02_11, null));
        }

        [DataTestMethod]
        [DataRow(Constantes.Almacenes.ALGETE)]
        [DataRow(Constantes.Almacenes.ALCOBENDAS)]
        [DataRow(Constantes.Almacenes.REINA)]
        [DataRow("")]
        public void EsFestivo_Martes03_11_2026_EsLaborable(string delegacion)
        {
            Assert.IsFalse(GestorFestivos.EsFestivo(MARTES_03_11, delegacion));
        }

        [TestMethod]
        public void EsFestivo_DosDeMayo2026EnSabado_EsFestivoEnAlgete()
        {
            Assert.IsTrue(GestorFestivos.EsFestivo(new DateTime(2026, 5, 2), Constantes.Almacenes.ALGETE));
        }

        [TestMethod]
        public void EsFestivo_JuevesSanto2026_EsFestivoEnAlgete()
        {
            Assert.IsTrue(GestorFestivos.EsFestivo(new DateTime(2026, 4, 2), Constantes.Almacenes.ALGETE));
        }

        [TestMethod]
        public void EsFestivo_FiestasDeMadridYAlcobendas_SiguenSiendoSoloDeSuDelegacion()
        {
            // San Isidro (15/05/2026, viernes): Madrid y Alcobendas, no Algete.
            DateTime sanIsidro = new DateTime(2026, 5, 15);
            Assert.IsTrue(GestorFestivos.EsFestivo(sanIsidro, Constantes.Almacenes.REINA));
            Assert.IsTrue(GestorFestivos.EsFestivo(sanIsidro, Constantes.Almacenes.ALCOBENDAS));
            Assert.IsFalse(GestorFestivos.EsFestivo(sanIsidro, Constantes.Almacenes.ALGETE));
            Assert.IsFalse(GestorFestivos.EsFestivo(sanIsidro, ""));
        }

        [TestMethod]
        public void PrimerLaborableDesde_SabadoAntesDelTraslado_EsElMartes03_11()
        {
            Assert.AreEqual(MARTES_03_11, GestorFestivos.PrimerLaborableDesde(VIERNES_30_10.AddDays(1), Constantes.Almacenes.ALGETE));
        }

        [TestMethod]
        public void ProximaFechaCargo_RemesaDelViernes30_10_ConAntelacion1_EsElMartes03_11()
        {
            DateTime cargo = CrearRemesaService.ProximaFechaCargo(VIERNES_30_10, 1,
                f => GestorFestivos.EsFestivo(f, Constantes.Almacenes.ALGETE));

            Assert.AreEqual(MARTES_03_11, cargo);
        }

        [TestMethod]
        public void CalculadoraFechaEntregaAgencia_ViernesPorLaTardeTodoEnAlgete_SaleElMartes03_11()
        {
            var calculadora = new CalculadoraFechaEntregaAgencia(GestorFestivos.EsFestivo);

            ResultadoFechaEntregaAgencia resultado = calculadora.Calcular(new EntradaFechaEntregaAgencia
            {
                Lineas = new List<LineaFechaEntregaAgencia>
                {
                    new LineaFechaEntregaAgencia { Producto = "A", Cantidad = 2, BaseImponible = 20, EnAlgete = 2 }
                },
                Ruta = "FW",
                Ahora = new DateTime(2026, 10, 30, 17, 0, 0),
                HoraCorte = new TimeSpan(11, 0, 0),
                Calendario = new List<ReposicionCalendario>()
            });

            Assert.AreEqual(MARTES_03_11, resultado.PrimeraEntrega, resultado.Motivo);
            Assert.AreEqual(MARTES_03_11, resultado.EntregaCompleta, resultado.Motivo);
        }
    }
}
