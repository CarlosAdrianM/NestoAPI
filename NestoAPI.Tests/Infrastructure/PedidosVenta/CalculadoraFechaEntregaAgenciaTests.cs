using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606: la matriz completa modo de servicio (1-4) × modo de facturación (1-3) de la fecha de entrega a la
    /// agencia. Cada escenario es un [DataTestMethod] con las 12 combinaciones; cada fila dice la primera entrega y la
    /// completa esperadas ("-" = sin fecha).
    ///
    /// <para>Calendario de referencia (octubre de 2026): jueves 08, viernes 09, LUNES 12 FESTIVO (nacional), martes 13,
    /// miércoles 14, jueves 15, viernes 16, lunes 19, martes 20, miércoles 21. Diciembre: lunes 07, MARTES 08 FESTIVO,
    /// miércoles 09. Corte del picking a las 11:00. Ruta FW (agencia, con portes).</para>
    ///
    /// <para>Reposiciones (como en producción el 08/10/26): Reina → Algete lunes, miércoles y viernes; Alcobendas → Algete
    /// lunes, martes y jueves; cierre a las 10:00 y llegada a las 13:30 (después del corte: el pedido sale el laborable
    /// siguiente a la llegada).</para>
    ///
    /// <para>El pedido de los escenarios tiene dos líneas de pago: A (2 uds., siempre en Algete) y B (1 ud., donde diga el
    /// escenario).</para>
    /// </summary>
    [TestClass]
    public class CalculadoraFechaEntregaAgenciaTests
    {
        private static readonly HashSet<DateTime> festivos = new HashSet<DateTime>
        {
            new DateTime(2026, 10, 12),
            new DateTime(2026, 12, 8)
        };

        private static bool EsFestivo(DateTime dia, string almacen)
        {
            return dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday || festivos.Contains(dia.Date);
        }

        private static ReposicionCalendario Fila(string origen, byte dia)
        {
            return new ReposicionCalendario
            {
                Empresa = "1  ",
                AlmacenOrigen = origen,
                AlmacenDestino = "ALG",
                DiaSemana = dia,
                HoraCierre = new TimeSpan(10, 0, 0),
                HoraLlegadaHabitual = new TimeSpan(13, 30, 0),
                Activo = true
            };
        }

        private static List<ReposicionCalendario> CalendarioProduccion()
        {
            return new List<ReposicionCalendario>
            {
                Fila("REI", 1), Fila("REI", 3), Fila("REI", 5),
                Fila("ALC", 1), Fila("ALC", 2), Fila("ALC", 4)
            };
        }

        private static DateTime Instante(string valor) => DateTime.ParseExact(valor, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static DateTime? Dia(string valor) => valor == "-" ? (DateTime?)null : DateTime.ParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static LineaFechaEntregaAgencia LineaA() => new LineaFechaEntregaAgencia
        {
            Producto = "A",
            Cantidad = 2,
            BaseImponible = 20,
            EnAlgete = 2
        };

        private static ResultadoFechaEntregaAgencia Calcular(string ahora, byte servicio, byte facturacion, params LineaFechaEntregaAgencia[] lineas)
        {
            return Calcular(ahora, servicio, facturacion, null, "FW", false, lineas);
        }

        private static ResultadoFechaEntregaAgencia Calcular(string ahora, byte servicio, byte facturacion, string diasEnServir, string ruta,
            bool tieneLineasServidas, params LineaFechaEntregaAgencia[] lineas)
        {
            var calculadora = new CalculadoraFechaEntregaAgencia(EsFestivo);
            return calculadora.Calcular(new EntradaFechaEntregaAgencia
            {
                Lineas = lineas.ToList(),
                ModoServicio = servicio,
                ModoFacturacion = facturacion,
                Ruta = ruta,
                DiasEnServir = diasEnServir,
                TieneLineasServidas = tieneLineasServidas,
                Ahora = Instante(ahora),
                HoraCorte = new TimeSpan(11, 0, 0),
                Calendario = CalendarioProduccion()
            });
        }

        private static void Comprobar(ResultadoFechaEntregaAgencia resultado, string primera, string completa, byte servicio, byte facturacion)
        {
            string combinacion = $"servicio {servicio} × facturación {facturacion}. Motivo: {resultado.Motivo}";
            Assert.AreEqual(Dia(primera), resultado.PrimeraEntrega, "Primera entrega, " + combinacion);
            Assert.AreEqual(Dia(completa), resultado.EntregaCompleta, "Entrega completa, " + combinacion);
            Assert.IsFalse(string.IsNullOrWhiteSpace(resultado.Motivo), "Sin motivo, " + combinacion);
        }

        private static void ComprobarEscenario(string ahora, LineaFechaEntregaAgencia lineaB, byte servicio, byte facturacion, string primera, string completa)
        {
            var lineas = lineaB == null ? new[] { LineaA() } : new[] { LineaA(), lineaB };
            Comprobar(Calcular(ahora, servicio, facturacion, lineas), primera, completa, servicio, facturacion);
        }

        private static LineaFechaEntregaAgencia BEnAlgete() => new LineaFechaEntregaAgencia { Producto = "B", Cantidad = 1, BaseImponible = 10, EnAlgete = 1 };

        private static LineaFechaEntregaAgencia BEnReina() => new LineaFechaEntregaAgencia
        {
            Producto = "B",
            Cantidad = 1,
            BaseImponible = 10,
            EnTiendas = new Dictionary<string, int> { { "REI", 1 } }
        };

        private static LineaFechaEntregaAgencia BDelProveedor(string fechaPrevista) => new LineaFechaEntregaAgencia
        {
            Producto = "B",
            Cantidad = 1,
            BaseImponible = 10,
            DelProveedor = 1,
            FechaProveedor = Dia(fechaPrevista)
        };

        private static LineaFechaEntregaAgencia BSinStock() => new LineaFechaEntregaAgencia { Producto = "B", Cantidad = 1, BaseImponible = 10 };

        #region Todo en Algete

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-14", "2026-10-14")]
        [DataRow((byte)1, (byte)2, "2026-10-14", "2026-10-14")]
        [DataRow((byte)1, (byte)3, "2026-10-14", "2026-10-14")]
        [DataRow((byte)2, (byte)1, "2026-10-14", "2026-10-14")]
        [DataRow((byte)2, (byte)2, "2026-10-14", "2026-10-14")]
        [DataRow((byte)2, (byte)3, "2026-10-14", "2026-10-14")]
        [DataRow((byte)3, (byte)1, "2026-10-14", "2026-10-14")]
        [DataRow((byte)3, (byte)2, "2026-10-14", "2026-10-14")]
        [DataRow((byte)3, (byte)3, "2026-10-14", "2026-10-14")]
        [DataRow((byte)4, (byte)1, "2026-10-14", "2026-10-14")]
        [DataRow((byte)4, (byte)2, "2026-10-14", "2026-10-14")]
        [DataRow((byte)4, (byte)3, "2026-10-14", "2026-10-14")]
        public void TodoEnAlgete_UnSegundoAntesDelCorte_SaleHoy(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-14 10:59:59", BEnAlgete(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)3, "2026-10-15", "2026-10-15")]
        [DataRow((byte)2, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)2, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)2, (byte)3, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)3, "2026-10-15", "2026-10-15")]
        [DataRow((byte)4, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)4, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)4, (byte)3, "2026-10-15", "2026-10-15")]
        public void TodoEnAlgete_JustoALaHoraDelCorte_SaleManana(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-14 11:00:00", BEnAlgete(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-19", "2026-10-19")]
        [DataRow((byte)1, (byte)2, "2026-10-19", "2026-10-19")]
        [DataRow((byte)1, (byte)3, "2026-10-19", "2026-10-19")]
        [DataRow((byte)2, (byte)1, "2026-10-19", "2026-10-19")]
        [DataRow((byte)2, (byte)2, "2026-10-19", "2026-10-19")]
        [DataRow((byte)2, (byte)3, "2026-10-19", "2026-10-19")]
        [DataRow((byte)3, (byte)1, "2026-10-19", "2026-10-19")]
        [DataRow((byte)3, (byte)2, "2026-10-19", "2026-10-19")]
        [DataRow((byte)3, (byte)3, "2026-10-19", "2026-10-19")]
        [DataRow((byte)4, (byte)1, "2026-10-19", "2026-10-19")]
        [DataRow((byte)4, (byte)2, "2026-10-19", "2026-10-19")]
        [DataRow((byte)4, (byte)3, "2026-10-19", "2026-10-19")]
        public void TodoEnAlgete_ViernesPorLaTarde_SaleElLunes(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-16 17:00:00", BEnAlgete(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)1, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)1, (byte)3, "2026-10-13", "2026-10-13")]
        [DataRow((byte)2, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)2, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)2, (byte)3, "2026-10-13", "2026-10-13")]
        [DataRow((byte)3, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)3, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)3, (byte)3, "2026-10-13", "2026-10-13")]
        [DataRow((byte)4, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)4, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)4, (byte)3, "2026-10-13", "2026-10-13")]
        public void TodoEnAlgete_ViernesPorLaTardeConElLunesFestivo_SaleElMartes(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-09 17:00:00", BEnAlgete(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-12-09", "2026-12-09")]
        [DataRow((byte)1, (byte)2, "2026-12-09", "2026-12-09")]
        [DataRow((byte)1, (byte)3, "2026-12-09", "2026-12-09")]
        [DataRow((byte)2, (byte)1, "2026-12-09", "2026-12-09")]
        [DataRow((byte)2, (byte)2, "2026-12-09", "2026-12-09")]
        [DataRow((byte)2, (byte)3, "2026-12-09", "2026-12-09")]
        [DataRow((byte)3, (byte)1, "2026-12-09", "2026-12-09")]
        [DataRow((byte)3, (byte)2, "2026-12-09", "2026-12-09")]
        [DataRow((byte)3, (byte)3, "2026-12-09", "2026-12-09")]
        [DataRow((byte)4, (byte)1, "2026-12-09", "2026-12-09")]
        [DataRow((byte)4, (byte)2, "2026-12-09", "2026-12-09")]
        [DataRow((byte)4, (byte)3, "2026-12-09", "2026-12-09")]
        public void TodoEnAlgete_VisperaDeFestivoDespuesDelCorte_SaleElDiaDespuesDelFestivo(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-12-07 12:00:00", BEnAlgete(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-12-07", "2026-12-07")]
        [DataRow((byte)1, (byte)2, "2026-12-07", "2026-12-07")]
        [DataRow((byte)1, (byte)3, "2026-12-07", "2026-12-07")]
        [DataRow((byte)2, (byte)1, "2026-12-07", "2026-12-07")]
        [DataRow((byte)2, (byte)2, "2026-12-07", "2026-12-07")]
        [DataRow((byte)2, (byte)3, "2026-12-07", "2026-12-07")]
        [DataRow((byte)3, (byte)1, "2026-12-07", "2026-12-07")]
        [DataRow((byte)3, (byte)2, "2026-12-07", "2026-12-07")]
        [DataRow((byte)3, (byte)3, "2026-12-07", "2026-12-07")]
        [DataRow((byte)4, (byte)1, "2026-12-07", "2026-12-07")]
        [DataRow((byte)4, (byte)2, "2026-12-07", "2026-12-07")]
        [DataRow((byte)4, (byte)3, "2026-12-07", "2026-12-07")]
        public void TodoEnAlgete_VisperaDeFestivoAntesDelCorte_SaleHoy(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-12-07 10:59:59", BEnAlgete(), servicio, facturacion, primera, completa);
        }

        #endregion

        #region Parte en una tienda (Reina)

        // Martes 13 a las 10:30: la próxima de Reina cierra el miércoles 14 a las 10:00, llega a las 13:30 (pasado el corte)
        // y el pedido que la espera sale el jueves 15. «Todo ahora» con modo 1, 2 o 4: lo que no sale va a una nota sin fecha.
        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-13", "2026-10-15")]
        [DataRow((byte)2, (byte)2, "2026-10-13", "2026-10-15")]
        [DataRow((byte)2, (byte)3, "2026-10-13", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)3, "2026-10-15", "2026-10-15")]
        [DataRow((byte)4, (byte)1, "2026-10-13", "2026-10-15")]
        [DataRow((byte)4, (byte)2, "2026-10-13", "2026-10-15")]
        [DataRow((byte)4, (byte)3, "2026-10-13", "-")]
        public void ParteEnTienda_AntesDelCorte(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-13 10:30:00", BEnReina(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-14", "2026-10-15")]
        [DataRow((byte)2, (byte)2, "2026-10-14", "2026-10-15")]
        [DataRow((byte)2, (byte)3, "2026-10-14", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)3, "2026-10-15", "2026-10-15")]
        [DataRow((byte)4, (byte)1, "2026-10-14", "2026-10-15")]
        [DataRow((byte)4, (byte)2, "2026-10-14", "2026-10-15")]
        [DataRow((byte)4, (byte)3, "2026-10-14", "-")]
        public void ParteEnTienda_JustoALaHoraDelCorte(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-13 11:00:00", BEnReina(), servicio, facturacion, primera, completa);
        }

        // Viernes 16 por la tarde: primer picking el lunes 19; Reina cierra el lunes 19 a las 10:00 → sale el martes 20.
        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-20", "2026-10-20")]
        [DataRow((byte)1, (byte)2, "2026-10-20", "2026-10-20")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-19", "2026-10-20")]
        [DataRow((byte)2, (byte)2, "2026-10-19", "2026-10-20")]
        [DataRow((byte)2, (byte)3, "2026-10-19", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-20", "2026-10-20")]
        [DataRow((byte)3, (byte)2, "2026-10-20", "2026-10-20")]
        [DataRow((byte)3, (byte)3, "2026-10-20", "2026-10-20")]
        [DataRow((byte)4, (byte)1, "2026-10-19", "2026-10-20")]
        [DataRow((byte)4, (byte)2, "2026-10-19", "2026-10-20")]
        [DataRow((byte)4, (byte)3, "2026-10-19", "-")]
        public void ParteEnTienda_ViernesPorLaTarde(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-16 17:00:00", BEnReina(), servicio, facturacion, primera, completa);
        }

        // Viernes 9 por la tarde con el lunes 12 festivo: primer picking el martes 13; Reina no repone el lunes festivo, la
        // siguiente es la del miércoles 14 → sale el jueves 15.
        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-13", "2026-10-15")]
        [DataRow((byte)2, (byte)2, "2026-10-13", "2026-10-15")]
        [DataRow((byte)2, (byte)3, "2026-10-13", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)2, "2026-10-15", "2026-10-15")]
        [DataRow((byte)3, (byte)3, "2026-10-15", "2026-10-15")]
        [DataRow((byte)4, (byte)1, "2026-10-13", "2026-10-15")]
        [DataRow((byte)4, (byte)2, "2026-10-13", "2026-10-15")]
        [DataRow((byte)4, (byte)3, "2026-10-13", "-")]
        public void ParteEnTienda_VisperaDeFestivo(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-09 17:00:00", BEnReina(), servicio, facturacion, primera, completa);
        }

        #endregion

        #region Parte del proveedor (pedido de compra enviado con fecha prevista)

        // Prevista el viernes 16: se recibe ese día y sale en el picking del laborable siguiente, el lunes 19. El modo 3 no
        // espera al proveedor (solo a las tiendas): se comporta como el 2.
        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-19", "2026-10-19")]
        [DataRow((byte)1, (byte)2, "2026-10-19", "2026-10-19")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-13", "2026-10-19")]
        [DataRow((byte)2, (byte)2, "2026-10-13", "2026-10-19")]
        [DataRow((byte)2, (byte)3, "2026-10-13", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-13", "2026-10-19")]
        [DataRow((byte)3, (byte)2, "2026-10-13", "2026-10-19")]
        [DataRow((byte)3, (byte)3, "2026-10-13", "-")]
        [DataRow((byte)4, (byte)1, "2026-10-13", "2026-10-19")]
        [DataRow((byte)4, (byte)2, "2026-10-13", "2026-10-19")]
        [DataRow((byte)4, (byte)3, "2026-10-13", "-")]
        public void ParteDelProveedor_AntesDelCorte(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-13 10:30:00", BDelProveedor("2026-10-16"), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-19", "2026-10-19")]
        [DataRow((byte)1, (byte)2, "2026-10-19", "2026-10-19")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-14", "2026-10-19")]
        [DataRow((byte)2, (byte)2, "2026-10-14", "2026-10-19")]
        [DataRow((byte)2, (byte)3, "2026-10-14", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-14", "2026-10-19")]
        [DataRow((byte)3, (byte)2, "2026-10-14", "2026-10-19")]
        [DataRow((byte)3, (byte)3, "2026-10-14", "-")]
        [DataRow((byte)4, (byte)1, "2026-10-14", "2026-10-19")]
        [DataRow((byte)4, (byte)2, "2026-10-14", "2026-10-19")]
        [DataRow((byte)4, (byte)3, "2026-10-14", "-")]
        public void ParteDelProveedor_JustoALaHoraDelCorte(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-13 11:00:00", BDelProveedor("2026-10-16"), servicio, facturacion, primera, completa);
        }

        // Prevista el miércoles 21: sale el jueves 22. El primer picking, el lunes 19.
        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-22", "2026-10-22")]
        [DataRow((byte)1, (byte)2, "2026-10-22", "2026-10-22")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-19", "2026-10-22")]
        [DataRow((byte)2, (byte)2, "2026-10-19", "2026-10-22")]
        [DataRow((byte)2, (byte)3, "2026-10-19", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-19", "2026-10-22")]
        [DataRow((byte)3, (byte)2, "2026-10-19", "2026-10-22")]
        [DataRow((byte)3, (byte)3, "2026-10-19", "-")]
        [DataRow((byte)4, (byte)1, "2026-10-19", "2026-10-22")]
        [DataRow((byte)4, (byte)2, "2026-10-19", "2026-10-22")]
        [DataRow((byte)4, (byte)3, "2026-10-19", "-")]
        public void ParteDelProveedor_ViernesPorLaTarde(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-16 17:00:00", BDelProveedor("2026-10-21"), servicio, facturacion, primera, completa);
        }

        // Viernes 9 por la tarde, prevista para hoy: se recibe hoy y sale en el siguiente laborable, el martes 13 (el lunes
        // 12 es festivo), el mismo día que lo de Algete: una sola entrega en todos los modos.
        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)1, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)1, (byte)3, "2026-10-13", "2026-10-13")]
        [DataRow((byte)2, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)2, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)2, (byte)3, "2026-10-13", "2026-10-13")]
        [DataRow((byte)3, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)3, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)3, (byte)3, "2026-10-13", "2026-10-13")]
        [DataRow((byte)4, (byte)1, "2026-10-13", "2026-10-13")]
        [DataRow((byte)4, (byte)2, "2026-10-13", "2026-10-13")]
        [DataRow((byte)4, (byte)3, "2026-10-13", "2026-10-13")]
        public void ParteDelProveedor_VisperaDeFestivo_LlegaHoy(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-09 17:00:00", BDelProveedor("2026-10-09"), servicio, facturacion, primera, completa);
        }

        #endregion

        #region Parte sin stock ni fecha

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "-", "-")]
        [DataRow((byte)1, (byte)2, "-", "-")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-13", "-")]
        [DataRow((byte)2, (byte)2, "2026-10-13", "-")]
        [DataRow((byte)2, (byte)3, "2026-10-13", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-13", "-")]
        [DataRow((byte)3, (byte)2, "2026-10-13", "-")]
        [DataRow((byte)3, (byte)3, "2026-10-13", "-")]
        [DataRow((byte)4, (byte)1, "2026-10-13", "-")]
        [DataRow((byte)4, (byte)2, "2026-10-13", "-")]
        [DataRow((byte)4, (byte)3, "2026-10-13", "-")]
        public void ParteSinStock_AntesDelCorte(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-13 10:30:00", BSinStock(), servicio, facturacion, primera, completa);
        }

        [DataTestMethod]
        [DataRow((byte)1, (byte)1, "-", "-")]
        [DataRow((byte)1, (byte)2, "-", "-")]
        [DataRow((byte)1, (byte)3, "-", "-")]
        [DataRow((byte)2, (byte)1, "2026-10-19", "-")]
        [DataRow((byte)2, (byte)2, "2026-10-19", "-")]
        [DataRow((byte)2, (byte)3, "2026-10-19", "-")]
        [DataRow((byte)3, (byte)1, "2026-10-19", "-")]
        [DataRow((byte)3, (byte)2, "2026-10-19", "-")]
        [DataRow((byte)3, (byte)3, "2026-10-19", "-")]
        [DataRow((byte)4, (byte)1, "2026-10-19", "-")]
        [DataRow((byte)4, (byte)2, "2026-10-19", "-")]
        [DataRow((byte)4, (byte)3, "2026-10-19", "-")]
        public void ParteSinStock_ViernesPorLaTarde(byte servicio, byte facturacion, string primera, string completa)
        {
            ComprobarEscenario("2026-10-16 17:00:00", BSinStock(), servicio, facturacion, primera, completa);
        }

        #endregion

        #region Reglas sueltas del picking

        [DataTestMethod]
        [DataRow((byte)1, true)]
        [DataRow((byte)2, false)]
        [DataRow((byte)3, false)]
        [DataRow((byte)4, false)]
        public void LaFechaQueAplica_TodoJuntoLaCompleta_ElRestoLaPrimera(byte servicio, bool aplicaCompleta)
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", servicio, 1, LineaA(), BDelProveedor("2026-10-16"));

            Assert.AreEqual(aplicaCompleta, resultado.AplicaCompleta);
            Assert.AreEqual(aplicaCompleta ? resultado.EntregaCompleta : resultado.PrimeraEntrega, resultado.FechaQueAplica);
        }

        [TestMethod]
        public void VariasEntregas_LasDevuelveTodasEnOrden()
        {
            var c = new LineaFechaEntregaAgencia { Producto = "C", Cantidad = 1, BaseImponible = 5, EnTiendas = new Dictionary<string, int> { { "REI", 1 } } };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 2, 1, LineaA(), c, BDelProveedor("2026-10-16"));

            CollectionAssert.AreEqual(new[] { new DateTime(2026, 10, 13), new DateTime(2026, 10, 15), new DateTime(2026, 10, 19) }, resultado.Entregas);
        }

        [DataTestMethod]
        [DataRow((byte)2)]
        [DataRow((byte)3)]
        [DataRow((byte)4)]
        public void LoQueFaltaSonSoloRegalos_NoSeSirveAMedias(byte servicio)
        {
            // #529: si lo único que se quedaría pendiente es un regalo, se espera a tenerlo todo.
            var regalo = new LineaFechaEntregaAgencia { Producto = "R", Cantidad = 1, BaseImponible = 0, DelProveedor = 1, FechaProveedor = new DateTime(2026, 10, 16) };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", servicio, 1, LineaA(), regalo);

            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.EntregaCompleta);
            StringAssert.Contains(resultado.Motivo, "regalo");
        }

        [TestMethod]
        public void RegaloSinFecha_ElPedidoNoSaleNunca()
        {
            var regalo = new LineaFechaEntregaAgencia { Producto = "R", Cantidad = 1, BaseImponible = 0 };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 2, 1, LineaA(), regalo);

            Assert.IsNull(resultado.PrimeraEntrega);
            Assert.IsNull(resultado.EntregaCompleta);
        }

        [TestMethod]
        public void SiSoloHayRegalosEnAlgete_EsperaAQueHayaAlgoDePago()
        {
            // HayStockDeAlgo (incidencia 451): con productos de pago en el pedido, no sale un envío solo de regalos.
            var regaloEnAlgete = new LineaFechaEntregaAgencia { Producto = "R", Cantidad = 1, BaseImponible = 0, EnAlgete = 1 };
            var dePagoDelProveedor = BDelProveedor("2026-10-16");

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 2, 1, regaloEnAlgete, dePagoDelProveedor);

            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.EntregaCompleta);
        }

        [TestMethod]
        public void PedidoSoloDeRegalos_Sale()
        {
            var regalo = new LineaFechaEntregaAgencia { Producto = "R", Cantidad = 1, BaseImponible = 0, EnAlgete = 1 };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1, regalo);

            Assert.AreEqual(new DateTime(2026, 10, 13), resultado.PrimeraEntrega);
        }

        [TestMethod]
        public void ClienteQueCierraElDiaDeLaEntrega_SaleElPickingSiguiente()
        {
            // #362: el picking del martes 13 se entregaría el miércoles 14, y el cliente cierra los miércoles.
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1, "11011", "FW", false, LineaA());

            Assert.AreEqual(new DateTime(2026, 10, 14), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 14), resultado.EntregaCompleta);
            StringAssert.Contains(resultado.Motivo, "cierra");
        }

        [TestMethod]
        public void DiasEnServirConFormatoRaro_SeConsideraAbierto()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1, "00000", "FW", false, LineaA());

            Assert.AreEqual(new DateTime(2026, 10, 13), resultado.PrimeraEntrega);
        }

        [TestMethod]
        public void LineaConFechaDeEntregaFutura_NoEntraEnElPickingHastaEseDia_NiSiquieraEnTodoJunto()
        {
            // BorrarLineasEntregaFutura quita la línea ANTES de mirar si hay stock de todo: el resto sale solo.
            var futura = BEnAlgete();
            futura.FechaEntrega = new DateTime(2026, 10, 19);

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1, LineaA(), futura);

            Assert.AreEqual(new DateTime(2026, 10, 13), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.EntregaCompleta);
        }

        [TestMethod]
        public void ProveedorConFechaPrevistaYaPasada_SinFecha()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1, LineaA(), BDelProveedor("2026-10-09"));

            Assert.IsNull(resultado.EntregaCompleta);
            StringAssert.Contains(resultado.Motivo, "pasada");
        }

        [TestMethod]
        public void ProveedorSinFechaPrevista_SinFecha()
        {
            var b = BDelProveedor("-");

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 2, 1, LineaA(), b);

            Assert.AreEqual(new DateTime(2026, 10, 13), resultado.PrimeraEntrega);
            Assert.IsNull(resultado.EntregaCompleta);
        }

        [DataTestMethod]
        [DataRow((byte)2, "2026-10-13")]
        [DataRow((byte)3, "-")]
        public void TiendaSinCalendarioDeReposicion_SinFecha(byte servicio, string primera)
        {
            var desdeOtraTienda = new LineaFechaEntregaAgencia { Producto = "B", Cantidad = 1, BaseImponible = 10, EnTiendas = new Dictionary<string, int> { { "XXX", 1 } } };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", servicio, 1, LineaA(), desdeOtraTienda);

            Assert.AreEqual(Dia(primera), resultado.PrimeraEntrega);
            Assert.IsNull(resultado.EntregaCompleta);
            StringAssert.Contains(resultado.Motivo, "calendario");
        }

        [TestMethod]
        public void DosTiendas_TrasReponer_EsperaALaQueLlegaMasTarde()
        {
            // Martes 13 10:30: Reina sale el jueves 15; Alcobendas (ya cerró la del martes) repone el jueves 15 → viernes 16.
            var b = BEnReina();
            var c = new LineaFechaEntregaAgencia { Producto = "C", Cantidad = 1, BaseImponible = 5, EnTiendas = new Dictionary<string, int> { { "ALC", 1 } } };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 3, 1, LineaA(), b, c);

            Assert.AreEqual(new DateTime(2026, 10, 16), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 16), resultado.EntregaCompleta);
        }

        [TestMethod]
        public void UnaLineaConUnidadesEnVariosSitios_SaleCadaParteCuandoLlega()
        {
            var b = new LineaFechaEntregaAgencia
            {
                Producto = "B",
                Cantidad = 3,
                BaseImponible = 30,
                EnAlgete = 1,
                EnTiendas = new Dictionary<string, int> { { "REI", 1 } },
                DelProveedor = 1,
                FechaProveedor = new DateTime(2026, 10, 16)
            };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 2, 1, b);

            CollectionAssert.AreEqual(new[] { new DateTime(2026, 10, 13), new DateTime(2026, 10, 15), new DateTime(2026, 10, 19) }, resultado.Entregas);
        }

        [TestMethod]
        public void EnCaminoDesdeUnaTienda_SaleElLaborableSiguiente()
        {
            var b = new LineaFechaEntregaAgencia { Producto = "B", Cantidad = 1, BaseImponible = 10, EnCaminoDeTiendas = 1 };

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1, LineaA(), b);

            Assert.AreEqual(new DateTime(2026, 10, 14), resultado.EntregaCompleta);
        }

        [TestMethod]
        public void RutaSinPortes_NoTieneCorte()
        {
            // FechaEntregaAjustada: las rutas sin portes (aquí Glovo) no se cierran a la hora de corte.
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-14 12:00:00", 1, 1, null, Constantes.Pedidos.RUTA_GLOVO, false, LineaA());

            Assert.AreEqual(new DateTime(2026, 10, 14), resultado.PrimeraEntrega);
        }

        [TestMethod]
        public void LineasYaEnPicking_SalenHoy()
        {
            var enPicking = LineaA();
            enPicking.YaEnPicking = true;

            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 12:00:00", 2, 1, enPicking, BDelProveedor("2026-10-16"));

            Assert.AreEqual(new DateTime(2026, 10, 13), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.EntregaCompleta);
        }

        [TestMethod]
        public void Modo4ConLineasYaServidas_LoQueQuedaVaDeUnaVez()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 4, 1, null, "FW", true, LineaA(), BDelProveedor("2026-10-16"));

            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.PrimeraEntrega);
            Assert.AreEqual(new DateTime(2026, 10, 19), resultado.EntregaCompleta);
        }

        [TestMethod]
        public void SinLineasDeAlgete_SinFechaYLoDice()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 1);

            Assert.IsNull(resultado.PrimeraEntrega);
            Assert.IsNull(resultado.EntregaCompleta);
            Assert.IsFalse(string.IsNullOrWhiteSpace(resultado.Motivo));
        }

        [TestMethod]
        public void ElMotivoVaEnCastellanoConElDiaDeLaSemana()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 2, 1, LineaA(), BEnReina());

            StringAssert.Contains(resultado.Motivo, "martes 13/10");
            StringAssert.Contains(resultado.Motivo, "jueves 15/10");
            StringAssert.Contains(resultado.Motivo, "Reina");
        }

        [TestMethod]
        public void TodoAhoraConTodoJuntoYAlgoQueFalta_LoDiceEnElMotivo()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular("2026-10-13 10:30:00", 1, 3, LineaA(), BEnReina());

            StringAssert.Contains(resultado.Motivo, "nota de entrega");
        }

        #endregion
    }
}
