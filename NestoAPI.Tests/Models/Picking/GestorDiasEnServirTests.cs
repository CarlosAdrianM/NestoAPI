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
        // ===== NestoAPI#497: el correo se entiende sin conocer el formato de la base de datos =====

        private static PedidoPicking PedidoConDias(int id, string cliente, string dias) =>
            new PedidoPicking { Id = id, Cliente = cliente, DiasEnServir = dias };

        [TestMethod]
        public void GenerarCuerpo_ClienteQueCierraLosLunes_UnaColumnaPorDiaConAbiertoCerrado()
        {
            // El caso real de la issue: 925239 / 29268 / "01111", entrega el lunes 21/09/2026.
            DateTime lunes21 = new DateTime(2026, 9, 21);
            string cuerpo = GestorDiasEnServir.GenerarCuerpo(new List<PedidoPicking> { PedidoConDias(925239, "29268     ", "01111") }, lunes21);

            Assert.IsFalse(cuerpo.Contains("01111"), "el formato interno no sale en el correo");
            StringAssert.Contains(cuerpo, "<th>Pedido</th><th>Cliente</th>");
            foreach (string dia in new[] { "lunes", "martes", "miércoles", "jueves", "viernes" })
            {
                StringAssert.Contains(cuerpo, $">{dia}</th>", $"falta la columna {dia}");
            }
            StringAssert.Contains(cuerpo, "<td>925239</td><td>29268</td>");
            string celdas = GestorDiasEnServir.CeldasDias("01111", lunes21);
            Assert.AreEqual("<td style='background-color:#fde2e2'><strong>Cerrado</strong></td><td>Abierto</td><td>Abierto</td><td>Abierto</td><td>Abierto</td>", celdas);
            StringAssert.Contains(cuerpo, "el cliente tiene el <strong>lunes</strong> marcado como <strong>cerrado</strong>");
        }

        [TestMethod]
        public void CeldasDias_EntregaEnJueves_ResaltaElJuevesYNoElResto()
        {
            string celdas = GestorDiasEnServir.CeldasDias("11101", JUEVES);

            Assert.AreEqual("<td>Abierto</td><td>Abierto</td><td>Abierto</td><td style='background-color:#fde2e2'><strong>Cerrado</strong></td><td>Abierto</td>", celdas);
        }

        // Caso real 01/10/26 (Alfredo, cliente 5057 «LOS LUNES CIERRA»): un jueves por la tarde el picking es
        // para el viernes y la entrega el lunes; el picking se quedaba vacío y decía «No hay stock suficiente…».

        [TestMethod]
        public void ErrorSinPicking_TodosRetiradosPorCierre_DiceQueElClienteCierraYCuandoSale()
        {
            DateTime lunes05 = new DateTime(2026, 10, 5);
            var retirados = new List<PedidoPicking> { PedidoConDias(927586, "5057      ", "01111") };

            var ex = GestorDiasEnServir.ErrorSinPicking(retirados, lunes05);

            Assert.AreEqual(NestoAPI.Models.Constantes.Picking.ERROR_CLIENTE_CERRADO, ex.GetErrorCode());
            StringAssert.Contains(ex.Message, "927586");
            StringAssert.Contains(ex.Message, "5057");
            StringAssert.Contains(ex.Message, "lunes 05/10/2026");
            StringAssert.Contains(ex.Message, "cierra");
            Assert.IsFalse(ex.Message.Contains("stock"), "No es un problema de stock");
            Assert.IsTrue(ex.IsWarning);
        }

        [TestMethod]
        public void ErrorSinPicking_SinRetiradosPorCierre_EsElDeSiempreDeStock()
        {
            var ex = GestorDiasEnServir.ErrorSinPicking(new List<PedidoPicking>(), new DateTime(2026, 10, 5));

            Assert.AreEqual(NestoAPI.Models.Constantes.Picking.ERROR_SIN_STOCK, ex.GetErrorCode());
            Assert.AreEqual("No hay stock suficiente para asignar picking a ninguna línea", ex.Message);
        }

        [TestMethod]
        public void ErrorSinPicking_VariosPedidos_LosNombraTodos()
        {
            var retirados = new List<PedidoPicking>
            {
                PedidoConDias(927519, "5057", "01111"),
                PedidoConDias(927586, "5057", "01111")
            };

            var ex = GestorDiasEnServir.ErrorSinPicking(retirados, new DateTime(2026, 10, 5));

            StringAssert.Contains(ex.Message, "927519");
            StringAssert.Contains(ex.Message, "927586");
        }

        [TestMethod]
        public void RetirarPedidosDeClientesCerrados_SiSePideIgnorarElCierre_NoRetiraNada()
        {
            // Picking de UN pedido: el usuario ha confirmado «¿Aún así quieres asignarle picking?».
            var pedido = PedidoConDias(927586, "5057", "01111");
            pedido.Lineas = new List<LineaPedidoPicking> { new LineaPedidoPicking { Id = 1, Cantidad = 1 } };

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, new DateTime(2026, 10, 5), ignorarCierre: true);

            Assert.AreEqual(0, retirados.Count);
            Assert.AreEqual(1, pedido.Lineas.Count);
        }

        [TestMethod]
        public void CeldasDias_DatoRaro_SinDatoEnVezDeInventarse()
        {
            Assert.AreEqual(5, System.Text.RegularExpressions.Regex.Matches(GestorDiasEnServir.CeldasDias(null, JUEVES), "Sin dato").Count);
            Assert.AreEqual(5, System.Text.RegularExpressions.Regex.Matches(GestorDiasEnServir.CeldasDias("1x111", JUEVES), "Sin dato").Count);
        }

        // ===== NestoAPI#588: el día de entrega depende de si va por nuestra ruta o por agencia =====
        // Caso real 01/10/26 (jueves por la tarde, pasado el corte): cliente 5057 «LOS LUNES CIERRA», pedidos 927519 y
        // 927586. Picking para el viernes 02/10: por agencia se entregaría el lunes 05/10 (cerrado); por nuestra ruta,
        // el viernes 02/10 (abierto).
        private static readonly DateTime JUEVES_01_10 = new DateTime(2026, 10, 1);
        private static readonly DateTime VIERNES_02_10 = new DateTime(2026, 10, 2);
        private static readonly DateTime LUNES_05_10 = new DateTime(2026, 10, 5);

        private static PedidoPicking PedidoConRuta(int id, string ruta, string dias) => new PedidoPicking
        {
            Id = id,
            Cliente = "5057      ",
            Ruta = ruta,
            DiasEnServir = dias,
            Lineas = new List<LineaPedidoPicking> { new LineaPedidoPicking { Id = 1, Cantidad = 1 } }
        };

        [TestMethod]
        public void CalcularDiaEntregaRutaPropia_JuevesPasadoElCorte_EsElViernesYNoElLunesDeLaAgencia()
        {
            Assert.AreEqual(VIERNES_02_10, GestorDiasEnServir.CalcularDiaEntregaRutaPropia(JUEVES_01_10, VIERNES_02_10, SoloFinde));
            Assert.AreEqual(LUNES_05_10, GestorDiasEnServir.CalcularDiaEntrega(VIERNES_02_10, SoloFinde), "la agencia, como siempre");
        }

        [TestMethod]
        public void CalcularDiaEntregaRutaPropia_AntesDelCorte_CoincideConLaAgencia()
        {
            // Jueves por la mañana: el picking es para hoy; los dos entregan el viernes
            Assert.AreEqual(VIERNES_02_10, GestorDiasEnServir.CalcularDiaEntregaRutaPropia(JUEVES_01_10, JUEVES_01_10, SoloFinde));
            Assert.AreEqual(VIERNES_02_10, GestorDiasEnServir.CalcularDiaEntrega(JUEVES_01_10, SoloFinde));
        }

        [TestMethod]
        public void CalcularDiaEntregaRutaPropia_ConFestivo_SaltaElFestivo()
        {
            // Jueves por la tarde con el viernes festivo: el picking es para el lunes y por ruta también se entrega el lunes
            bool FindeOViernesFestivo(DateTime f) => SoloFinde(f) || f == VIERNES_02_10;
            Assert.AreEqual(LUNES_05_10, GestorDiasEnServir.CalcularDiaEntregaRutaPropia(JUEVES_01_10, LUNES_05_10, FindeOViernesFestivo));
        }

        [TestMethod]
        public void EsRutaPropia_16YATSonNuestrasElRestoAgencia()
        {
            Assert.IsTrue(GestorDiasEnServir.EsRutaPropia("16"));
            Assert.IsTrue(GestorDiasEnServir.EsRutaPropia("AT "), "char con relleno");
            Assert.IsTrue(GestorDiasEnServir.EsRutaPropia("at"));
            Assert.IsFalse(GestorDiasEnServir.EsRutaPropia("FW "));
            Assert.IsFalse(GestorDiasEnServir.EsRutaPropia("00"));
            Assert.IsFalse(GestorDiasEnServir.EsRutaPropia("AM"));
            Assert.IsFalse(GestorDiasEnServir.EsRutaPropia(null), "sin ruta = agencia, como hasta ahora");
        }

        [TestMethod]
        public void Retirar_PorNuestraRutaYClienteQueCierraLunes_JuevesPorLaTardeSale()
        {
            // El caso del 5057 con la ruta propia puesta: se entrega el viernes, que abre
            PedidoPicking pedido = PedidoConRuta(927586, "AT ", "01111");

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, LUNES_05_10, VIERNES_02_10);

            Assert.AreEqual(0, retirados.Count, "por nuestra ruta se entrega el viernes: el cliente abre");
            Assert.AreEqual(1, pedido.Lineas.Count);
            Assert.IsNull(pedido.DiaEntregaRetiradoPorCierre);
        }

        [TestMethod]
        public void Retirar_PorAgenciaYClienteQueCierraLunes_SeRetiraConLaPistaDeNuestraRuta()
        {
            // El 5057 tal y como estaba al sacar el picking (ruta FW): sigue sin salir (la regla de #362 no cambia),
            // pero se sabe que por nuestra ruta llegaría el viernes con el cliente abierto
            PedidoPicking pedido = PedidoConRuta(927586, "FW ", "01111");

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, LUNES_05_10, VIERNES_02_10);

            Assert.AreEqual(1, retirados.Count);
            Assert.AreEqual(0, pedido.Lineas.Count);
            Assert.AreEqual(LUNES_05_10, pedido.DiaEntregaRetiradoPorCierre);
            Assert.AreEqual(VIERNES_02_10, pedido.DiaEntregaSiVaPorNuestraRuta);

            var ex = GestorDiasEnServir.ErrorSinPicking(retirados, LUNES_05_10);
            Assert.AreEqual(NestoAPI.Models.Constantes.Picking.ERROR_CLIENTE_CERRADO, ex.GetErrorCode());
            StringAssert.Contains(ex.Message, "lunes 05/10/2026");
            StringAssert.Contains(ex.Message, "viernes 02/10/2026");
            StringAssert.Contains(ex.Message, "ruta propia (16 o AT)");
        }

        [TestMethod]
        public void Retirar_PorNuestraRutaYClienteQueCierraViernes_JuevesPorLaTardeNoSale()
        {
            // Por agencia (lunes) saldría; por nuestra ruta se entregaría el viernes, que cierra
            PedidoPicking pedido = PedidoConRuta(1, "16", "11110");

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, LUNES_05_10, VIERNES_02_10);

            Assert.AreEqual(1, retirados.Count);
            Assert.AreEqual(VIERNES_02_10, pedido.DiaEntregaRetiradoPorCierre);
            Assert.IsNull(pedido.DiaEntregaSiVaPorNuestraRuta);
            StringAssert.Contains(GestorDiasEnServir.ErrorSinPicking(retirados, LUNES_05_10).Message, "viernes 02/10/2026");
        }

        [TestMethod]
        public void Retirar_PorAgenciaYClienteQueCierraLosDosDias_SinPista()
        {
            PedidoPicking pedido = PedidoConRuta(1, "00", "01110"); // cierra lunes y viernes

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, LUNES_05_10, VIERNES_02_10);

            Assert.AreEqual(1, retirados.Count);
            Assert.IsNull(pedido.DiaEntregaSiVaPorNuestraRuta, "por nuestra ruta también estaría cerrado");
            Assert.IsFalse(GestorDiasEnServir.ErrorSinPicking(retirados, LUNES_05_10).Message.Contains("ruta propia"));
        }

        [TestMethod]
        public void Retirar_AntesDelCorteLosDosDiasCoinciden_SinPista()
        {
            // Viernes por la mañana: agencia y ruta entregan el lunes; nada que sugerir
            PedidoPicking pedido = PedidoConRuta(1, "FW", "01111");

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, LUNES_05_10, LUNES_05_10);

            Assert.AreEqual(1, retirados.Count);
            Assert.IsNull(pedido.DiaEntregaSiVaPorNuestraRuta);
        }

        [TestMethod]
        public void Retirar_ClienteQueAbreTodo_NadaPorNingunaRuta()
        {
            var candidatos = new List<PedidoPicking> { PedidoConRuta(1, "AT", "11111"), PedidoConRuta(2, "FW", "11111") };

            Assert.AreEqual(0, GestorDiasEnServir.RetirarPedidosDeClientesCerrados(candidatos, LUNES_05_10, VIERNES_02_10).Count);
        }

        [TestMethod]
        public void ErrorSinPicking_RetiradosEnDiasDistintos_DiceElDiaDeCadaUno()
        {
            PedidoPicking porRuta = PedidoConRuta(927519, "AT", "11110");
            PedidoPicking porAgencia = PedidoConRuta(927586, "FW", "01110");
            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { porRuta, porAgencia }, LUNES_05_10, VIERNES_02_10);

            var ex = GestorDiasEnServir.ErrorSinPicking(retirados, LUNES_05_10);

            Assert.AreEqual(2, retirados.Count);
            StringAssert.Contains(ex.Message, "927519 el viernes 02/10/2026");
            StringAssert.Contains(ex.Message, "927586 el lunes 05/10/2026");
        }

        [TestMethod]
        public void PendientesDeAvisar_UsaElDiaDeEntregaDeCadaPedido()
        {
            PedidoPicking pedido = PedidoConRuta(927519, "16", "11110");
            _ = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(new List<PedidoPicking> { pedido }, LUNES_05_10, VIERNES_02_10);
            var avisados = new Dictionary<string, DateTime>();

            _ = GestorDiasEnServir.PendientesDeAvisar(new List<PedidoPicking> { pedido }, LUNES_05_10, avisados);

            Assert.IsTrue(avisados.ContainsKey($"927519|{VIERNES_02_10:yyyyMMdd}"));
        }

        [TestMethod]
        public void GenerarCuerpo_PedidoQuePorNuestraRutaSaldria_LlevaLaPista()
        {
            PedidoPicking pedido = PedidoConRuta(927586, "FW", "01111");
            _ = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(new List<PedidoPicking> { pedido }, LUNES_05_10, VIERNES_02_10);

            string cuerpo = GestorDiasEnServir.GenerarCuerpo(new List<PedidoPicking> { pedido }, LUNES_05_10);

            StringAssert.Contains(cuerpo, "ruta propia (16 o AT)");
            StringAssert.Contains(cuerpo, "viernes 02/10/2026");
        }
    }
}
