using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#362: no dar picking a pedidos cuya entrega caería en un día que el cliente
    /// cierra (Clientes.DiasEnServir: 5 posiciones lunes..viernes, '1' abre, '0' cierra).
    /// </summary>
    [TestClass]
    public class GestorDiasEnServirTests
    {
        // Semana de referencia: lunes 07/09/2026 a viernes 11/09/2026 (sin festivos salvo test).
        private static readonly DateTime LUNES = new DateTime(2026, 9, 7);
        private static readonly DateTime JUEVES = new DateTime(2026, 9, 10);
        private static readonly DateTime VIERNES = new DateTime(2026, 9, 11);

        private static bool SoloFinde(DateTime f) =>
            f.DayOfWeek == DayOfWeek.Saturday || f.DayOfWeek == DayOfWeek.Sunday;

        [TestMethod]
        public void CalcularDiaEntrega_EnMitadDeSemana_EsElDiaSiguiente()
        {
            Assert.AreEqual(VIERNES, GestorDiasEnServir.CalcularDiaEntrega(JUEVES, SoloFinde));
        }

        [TestMethod]
        public void CalcularDiaEntrega_SalidaEnViernes_LaEntregaSaltaAlLunes()
        {
            Assert.AreEqual(LUNES.AddDays(7), GestorDiasEnServir.CalcularDiaEntrega(VIERNES, SoloFinde));
            Assert.AreEqual(LUNES, GestorDiasEnServir.CalcularDiaEntrega(VIERNES.AddDays(-7), SoloFinde));
        }

        [TestMethod]
        public void CalcularDiaEntrega_VisperaDeFestivo_SaltaTambienElFestivo()
        {
            // Salida el jueves con el viernes festivo: la entrega se va al lunes.
            bool FindeOViernesFestivo(DateTime f) => SoloFinde(f) || f == VIERNES;

            Assert.AreEqual(LUNES.AddDays(7), GestorDiasEnServir.CalcularDiaEntrega(JUEVES, FindeOViernesFestivo));
        }

        [TestMethod]
        public void EstaAbierto_CierraLosLunes_SoloElLunesDaCerrado()
        {
            // "01111" = cierra los lunes (posición 1 = lunes), el ejemplo canónico de la issue
            Assert.IsFalse(GestorDiasEnServir.EstaAbierto("01111", LUNES));
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("01111", LUNES.AddDays(1)));
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("01111", VIERNES));
        }

        [TestMethod]
        public void EstaAbierto_TodoAbiertoOConRelleno_Abierto()
        {
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("11111", JUEVES));
            // La columna es char y llega con relleno: el Trim es obligatorio
            Assert.IsFalse(GestorDiasEnServir.EstaAbierto(" 00111 ", LUNES.AddDays(1)), "Martes cerrado con relleno");
        }

        [TestMethod]
        public void EstaAbierto_DatoRaro_SeConsideraAbierto()
        {
            // Un dato defectuoso no debe dejar pedidos sin salir (fail-open)
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto(null, LUNES));
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("", LUNES));
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("0111", LUNES), "Longitud 4");
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("01x11", LUNES), "Caracter raro");
            // Medido en prod: 57 fichas con "00000" y 65 con "0", todas sirviéndose hoy con
            // normalidad. Cerrado-todos-los-días = dato roto: tomarlo en serio las bloquearía
            // PARA SIEMPRE.
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("00000", JUEVES), "Todo cerrado = dato roto");
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("0", JUEVES), "El '0' suelto de 65 fichas");
        }

        [TestMethod]
        public void RetirarPedidos_ClienteCerradoElDiaDeEntrega_SeQuedaSinLineasYSeDevuelve()
        {
            PedidoPicking cerrado = PedidoCon("01111"); // cierra los lunes
            PedidoPicking abierto = PedidoCon("11111");
            PedidoPicking sinDato = PedidoCon(null);
            List<PedidoPicking> candidatos = new List<PedidoPicking> { cerrado, abierto, sinDato };

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(candidatos, LUNES);

            Assert.AreEqual(1, retirados.Count);
            Assert.AreSame(cerrado, retirados[0]);
            Assert.AreEqual(0, cerrado.Lineas.Count, "Sin líneas: no sale, y se reevalúa en la siguiente pasada");
            Assert.AreEqual(1, abierto.Lineas.Count, "El abierto no se toca");
            Assert.AreEqual(1, sinDato.Lineas.Count, "Sin dato = abierto");
        }

        /// <summary>
        /// El escenario completo de la issue: cliente que cierra los lunes, picking que corre el
        /// viernes (salida viernes) → entregaría el lunes → NO sale. La pasada del lunes (salida
        /// lunes → entrega martes) SÍ lo saca, sin que nadie toque nada.
        /// </summary>
        [TestMethod]
        public void EscenarioIssue_CierraLunes_LaVentanaDelViernesNoSaleYLaDelLunesSi()
        {
            DateTime salidaViernes = VIERNES.AddDays(-7);
            DateTime entregaDesdeViernes = GestorDiasEnServir.CalcularDiaEntrega(salidaViernes, SoloFinde);
            Assert.IsFalse(GestorDiasEnServir.EstaAbierto("01111", entregaDesdeViernes), "Entrega en lunes: cerrado");

            DateTime entregaDesdeLunes = GestorDiasEnServir.CalcularDiaEntrega(LUNES, SoloFinde);
            Assert.IsTrue(GestorDiasEnServir.EstaAbierto("01111", entregaDesdeLunes), "Entrega en martes: abierto");
        }

        private static PedidoPicking PedidoCon(string diasEnServir)
        {
            return new PedidoPicking
            {
                Id = 1,
                Cliente = "15191",
                DiasEnServir = diasEnServir,
                Lineas = new List<LineaPedidoPicking> { new LineaPedidoPicking { Cantidad = 1 } }
            };
        }
            // NestoAPI#471: la ficha del cliente expone y guarda DiasEnServir por la API

        [TestMethod]
        public void EsFormatoValido_SoloCincoCerosYUnos()
        {
            Assert.IsTrue(GestorDiasEnServir.EsFormatoValido("11111"));
            Assert.IsTrue(GestorDiasEnServir.EsFormatoValido("01111"));
            Assert.IsTrue(GestorDiasEnServir.EsFormatoValido(" 11110 "), "la BD lo guarda en char(5): se tolera el relleno");
            Assert.IsFalse(GestorDiasEnServir.EsFormatoValido(null));
            Assert.IsFalse(GestorDiasEnServir.EsFormatoValido(""));
            Assert.IsFalse(GestorDiasEnServir.EsFormatoValido("1111"));
            Assert.IsFalse(GestorDiasEnServir.EsFormatoValido("111111"));
            Assert.IsFalse(GestorDiasEnServir.EsFormatoValido("1111X"));
        }

        [TestMethod]
        public void AplicarCambio_SinValorNuevo_NoTocaElActual()
        {
            Assert.AreEqual("01111", GestorDiasEnServir.AplicarCambio("01111", null), "el PUT de un cliente viejo no debe blanquear");
            Assert.AreEqual("01111", GestorDiasEnServir.AplicarCambio("01111", "  "));
        }

        [TestMethod]
        public void AplicarCambio_SinValorNuevoNiActual_PonePorDefecto()
        {
            Assert.AreEqual("11111", GestorDiasEnServir.AplicarCambio(null, null), "al crear un cliente abre todos los días");
        }

        [TestMethod]
        public void AplicarCambio_ConValorNuevoValido_LoGuardaRecortado()
        {
            Assert.AreEqual("01111", GestorDiasEnServir.AplicarCambio("11111", "01111"));
            Assert.AreEqual("11110", GestorDiasEnServir.AplicarCambio(null, " 11110 "));
        }

        [TestMethod]
        public void AplicarCambio_ConValorMalFormado_Rechaza()
        {
            Assert.ThrowsException<System.ComponentModel.DataAnnotations.ValidationException>(
                () => GestorDiasEnServir.AplicarCambio("11111", "1111"));
            Assert.ThrowsException<System.ComponentModel.DataAnnotations.ValidationException>(
                () => GestorDiasEnServir.AplicarCambio("11111", "LMXJV"));
        }

        // 10/09/26: almacén se quejó de un correo por cada picking con los mismos pedidos

        [TestMethod]
        public void PendientesDeAvisar_UnPedidoSoloSeAvisaUnaVezPorDiaDeEntrega()
        {
            PedidoPicking pedido = PedidoCon("01111");
            pedido.Id = 925758;
            Dictionary<string, DateTime> avisados = new Dictionary<string, DateTime>();

            List<PedidoPicking> primera = GestorDiasEnServir.PendientesDeAvisar(new List<PedidoPicking> { pedido }, LUNES, avisados);
            List<PedidoPicking> segunda = GestorDiasEnServir.PendientesDeAvisar(new List<PedidoPicking> { pedido }, LUNES, avisados);

            Assert.AreEqual(1, primera.Count, "la primera pasada avisa");
            Assert.AreEqual(0, segunda.Count, "la siguiente pasada del picking del mismo día no repite el correo");
        }

        [TestMethod]
        public void PendientesDeAvisar_SiCambiaElDiaDeEntrega_SeVuelveAAvisar()
        {
            PedidoPicking pedido = PedidoCon("01110"); // cierra lunes y viernes
            pedido.Id = 925758;
            Dictionary<string, DateTime> avisados = new Dictionary<string, DateTime>();

            _ = GestorDiasEnServir.PendientesDeAvisar(new List<PedidoPicking> { pedido }, LUNES, avisados);
            List<PedidoPicking> otroDia = GestorDiasEnServir.PendientesDeAvisar(new List<PedidoPicking> { pedido }, VIERNES, avisados);

            Assert.AreEqual(1, otroDia.Count, "otro día de entrega es otro aviso: el usuario debe saber que sigue sin salir");
        }

        [TestMethod]
        public void PendientesDeAvisar_LosAvisosViejosSeOlvidan()
        {
            PedidoPicking pedido = PedidoCon("01111");
            pedido.Id = 900001;
            Dictionary<string, DateTime> avisados = new Dictionary<string, DateTime> { { "777777|20260101", DateTime.Now.AddDays(-30) } };

            _ = GestorDiasEnServir.PendientesDeAvisar(new List<PedidoPicking> { pedido }, LUNES, avisados);

            Assert.IsFalse(avisados.ContainsKey("777777|20260101"), "el registro no crece sin fin");
            Assert.IsTrue(avisados.ContainsKey($"900001|{LUNES:yyyyMMdd}"));
        }
    }
}
