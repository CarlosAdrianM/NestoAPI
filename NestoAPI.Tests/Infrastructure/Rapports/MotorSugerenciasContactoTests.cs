using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#603 (corte 1): prioridades, cadencia, exclusiones, orden, motivo y ritmo.</summary>
    [TestClass]
    public class MotorSugerenciasContactoTests
    {
        // Miércoles. Octubre de 2026: 22 días de lunes a viernes; con el 12 (festivo) quedan 21.
        private static readonly DateTime HOY = new DateTime(2026, 10, 7);
        private static readonly DateTime FESTIVO = new DateTime(2026, 10, 12);
        private static readonly Func<DateTime, bool> laborable = d => d.Date != FESTIVO;

        private static ClienteCarteraContacto Cliente(string id, int pedidos12, int? diasContacto = null, float probabilidad = 0f,
            int? diasIntento = null, int diasPedido = 40, int pedidos24 = -1)
        {
            return new ClienteCarteraContacto
            {
                Cliente = id,
                Contacto = "0",
                Nombre = "Centro " + id,
                Pedidos12Meses = pedidos12,
                Pedidos24Meses = pedidos24 >= 0 ? pedidos24 : Math.Max(pedidos12, 1),
                Importe12Meses = pedidos12 * 100m,
                UltimoContacto = diasContacto.HasValue ? HOY.AddDays(-diasContacto.Value).AddHours(10) : (DateTime?)null,
                UltimoIntento = diasIntento.HasValue ? HOY.AddDays(-diasIntento.Value).AddHours(11) : (DateTime?)null,
                UltimoPedido = HOY.AddDays(-diasPedido),
                Probabilidad = probabilidad
            };
        }

        // ---------------- Cadencia ----------------

        [TestMethod]
        public void Cadencia_365EntrePedidos_EntreSieteYTreinta()
        {
            Assert.AreEqual(30, MotorSugerenciasContacto.Cadencia(Cliente("a", 0, pedidos24: 2)), "solo 24 meses → mensual");
            Assert.AreEqual(30, MotorSugerenciasContacto.Cadencia(Cliente("a", 5)), "365/5 = 73 → 30");
            Assert.AreEqual(30, MotorSugerenciasContacto.Cadencia(Cliente("a", 12)), "365/12 = 30,4 → 30");
            Assert.AreEqual(28, MotorSugerenciasContacto.Cadencia(Cliente("a", 13)));
            Assert.AreEqual(10, MotorSugerenciasContacto.Cadencia(Cliente("a", 36)));
            Assert.AreEqual(7, MotorSugerenciasContacto.Cadencia(Cliente("a", 52)));
            Assert.AreEqual(7, MotorSugerenciasContacto.Cadencia(Cliente("a", 120)), "365/120 = 3 → 7");
        }

        // ---------------- Prioridades ----------------

        [TestMethod]
        public void Prioridad_BuenoConProbabilidadAltaYYaLeToca_Maxima()
        {
            // El ejemplo de la issue: contactado hace 10 días con cadencia 7.
            Assert.AreEqual(PrioridadesContacto.MAXIMA, MotorSugerenciasContacto.Prioridad(Cliente("a", 52, diasContacto: 10, probabilidad: 0.7f), HOY));
            Assert.AreEqual(PrioridadesContacto.MAXIMA, MotorSugerenciasContacto.Prioridad(Cliente("a", 3, diasContacto: null, probabilidad: 0.6f), HOY));
        }

        [TestMethod]
        public void Prioridad_BuenoConProbabilidadAltaPeroDentroDeSuCadencia_Nada()
        {
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 52, diasContacto: 5, probabilidad: 0.9f), HOY));
        }

        [TestMethod]
        public void Prioridad_BuenoContactadoHace20DiasConCadencia30_NadaTodavia()
        {
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 5, diasContacto: 20, probabilidad: 0.9f), HOY));
        }

        [TestMethod]
        public void Prioridad_BuenoSinContactoEn30Dias_Alta()
        {
            Assert.AreEqual(PrioridadesContacto.ALTA, MotorSugerenciasContacto.Prioridad(Cliente("a", 5, diasContacto: 30, probabilidad: 0.2f), HOY));
            Assert.AreEqual(PrioridadesContacto.ALTA, MotorSugerenciasContacto.Prioridad(Cliente("a", 3, diasContacto: null, probabilidad: 0.59f), HOY));
        }

        [TestMethod]
        public void Prioridad_BuenoFrecuenteAlQueLeTocaConProbabilidadBaja_AltaAunqueNoLleve30Dias()
        {
            // Compra cada semana (cadencia 7): a los 7 días ya le toca, aunque el modelo no llegue a 0,6.
            Assert.AreEqual(PrioridadesContacto.ALTA, MotorSugerenciasContacto.Prioridad(Cliente("a", 52, diasContacto: 15, probabilidad: 0.3f), HOY));
            Assert.AreEqual(PrioridadesContacto.ALTA, MotorSugerenciasContacto.Prioridad(Cliente("a", 52, diasContacto: 7, probabilidad: 0.3f), HOY));
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 52, diasContacto: 6, probabilidad: 0.3f), HOY));
            // Cadencia 10 (36 pedidos): a los 12 días le toca.
            Assert.AreEqual(PrioridadesContacto.ALTA, MotorSugerenciasContacto.Prioridad(Cliente("a", 36, diasContacto: 12, probabilidad: 0.1f), HOY));
        }

        [TestMethod]
        public void Prioridad_ComprasEn12MesesSinSerBueno_MediaALos30Dias()
        {
            Assert.AreEqual(PrioridadesContacto.MEDIA, MotorSugerenciasContacto.Prioridad(Cliente("a", 2, diasContacto: 40, probabilidad: 0.9f), HOY));
            Assert.AreEqual(PrioridadesContacto.MEDIA, MotorSugerenciasContacto.Prioridad(Cliente("a", 1, diasContacto: null), HOY));
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 2, diasContacto: 29), HOY));
        }

        [TestMethod]
        public void Prioridad_SoloComprasEn24Meses_BajaALos30Dias()
        {
            Assert.AreEqual(PrioridadesContacto.BAJA, MotorSugerenciasContacto.Prioridad(Cliente("a", 0, diasContacto: null, pedidos24: 4, diasPedido: 400), HOY));
            Assert.AreEqual(PrioridadesContacto.BAJA, MotorSugerenciasContacto.Prioridad(Cliente("a", 0, diasContacto: 31, pedidos24: 1, diasPedido: 400), HOY));
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 0, diasContacto: 25, pedidos24: 1, diasPedido: 400), HOY));
        }

        // ---------------- Exclusiones ----------------

        [TestMethod]
        public void Exclusiones_IntentoDeHaceMenosDeDosDias_NoSePropone()
        {
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 5, diasIntento: 1), HOY));
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 5, diasIntento: 0), HOY));
            Assert.AreEqual(PrioridadesContacto.ALTA, MotorSugerenciasContacto.Prioridad(Cliente("a", 5, diasIntento: 2), HOY));
        }

        [TestMethod]
        public void Exclusiones_PedidoDeHaceMenosDeSeisDias_NoSePropone()
        {
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(Cliente("a", 52, probabilidad: 0.9f, diasPedido: 5), HOY));
            Assert.AreEqual(PrioridadesContacto.MAXIMA, MotorSugerenciasContacto.Prioridad(Cliente("a", 52, probabilidad: 0.9f, diasPedido: 6), HOY));
        }

        [TestMethod]
        public void Exclusiones_PedidoRecienteDeOtroGrupo_TampocoSePropone_PeroNoCambiaLaCadencia()
        {
            // NestoAPI#620 (Carlos, 20/10/26): el de peluquería no llama a quien pidió estética hace 2 días, aunque su último
            // pedido de peluquería sea de hace 40; la cadencia sigue con sus compras.
            ClienteCarteraContacto cliente = Cliente("a", 12, probabilidad: 0.9f, diasPedido: 40);
            int cadencia = MotorSugerenciasContacto.Cadencia(cliente);
            cliente.UltimoPedidoCliente = HOY.AddDays(-2);

            Assert.IsTrue(MotorSugerenciasContacto.EstaExcluido(cliente, HOY));
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(cliente, HOY));
            Assert.AreEqual(cadencia, MotorSugerenciasContacto.Cadencia(cliente));
            Assert.AreEqual(40, MotorSugerenciasContacto.ADTO(cliente, HOY).DiasDesdeUltimoPedido, "lo que se enseña es su último pedido");

            cliente.UltimoPedidoCliente = HOY.AddDays(-6);
            Assert.AreEqual(PrioridadesContacto.MAXIMA, MotorSugerenciasContacto.Prioridad(cliente, HOY), "a los 6 días ya se le puede llamar");
        }

        // ---------------- Orden ----------------

        [TestMethod]
        public void Priorizar_OrdenaPorProbabilidadYDiasSinContacto_SinMirarLaPrioridad()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                Cliente("baja", 0, pedidos24: 2, diasPedido: 500),
                Cliente("media", 2, diasContacto: 45, probabilidad: 0.5f),
                Cliente("alta40", 6, diasContacto: 40, probabilidad: 0.3f),
                Cliente("altaNunca", 6, diasContacto: null, probabilidad: 0.3f),
                Cliente("alta50", 6, diasContacto: 50, probabilidad: 0.3f),
                Cliente("altaProb", 6, diasContacto: 31, probabilidad: 0.5f),
                Cliente("maxima", 40, diasContacto: 12, probabilidad: 0.65f),
                Cliente("maximaMas", 40, diasContacto: 12, probabilidad: 0.8f),
                Cliente("nada", 6, diasContacto: 3, probabilidad: 0.9f)
            };

            List<SugerenciaContactoDTO> lista = new MotorSugerenciasContacto().Priorizar(cartera, HOY);

            CollectionAssert.AreEqual(
                // NestoAPI#677: la prioridad ya no ordena; «media» (0,5 y 45 días) va delante de «altaProb» (0,5 y 31 días).
                new[] { "maximaMas", "maxima", "media", "altaProb", "altaNunca", "alta50", "alta40", "baja" },
                lista.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), lista.Select(s => s.Orden).ToArray());
            Assert.AreEqual(9, lista[0].CadenciaDias, "365 / 40 = 9,1");
            Assert.AreEqual(12, lista[0].DiasDesdeUltimoContacto);
            Assert.IsNull(lista[4].DiasDesdeUltimoContacto);
            Assert.AreEqual(9999, lista[4].DiasDesdeUltimaInteraccion, "compatibilidad con ClienteProbabilidadVenta");
            Assert.AreEqual(40, lista[0].DiasDesdeUltimoPedido);
        }

        // ---------------- Motivo ----------------

        [TestMethod]
        public void Motivo_Maxima_CadenciaDiasSinHablarYProbabilidad()
        {
            string motivo = MotorSugerenciasContacto.Motivo(Cliente("a", 36, diasContacto: 34, probabilidad: 0.72f), PrioridadesContacto.MAXIMA, HOY);

            Assert.AreEqual("Compra cada 10 días y lleva 34 días sin hablar con usted; probabilidad de pedido del 72 %", motivo);
        }

        [TestMethod]
        public void Motivo_Alta_PedidosYFechaDelUltimoContacto()
        {
            ClienteCarteraContacto cliente = Cliente("a", 12, probabilidad: 0.1f);
            cliente.UltimoContacto = new DateTime(2026, 9, 2, 10, 0, 0);

            Assert.AreEqual("Buen cliente (12 pedidos en el último año), sin contacto desde el 2 de septiembre",
                MotorSugerenciasContacto.Motivo(cliente, PrioridadesContacto.ALTA, HOY));

            cliente.UltimoContacto = null;
            StringAssert.EndsWith(MotorSugerenciasContacto.Motivo(cliente, PrioridadesContacto.ALTA, HOY), "sin ningún contacto registrado");

            // Comprador frecuente al que le toca por cadencia antes de los 30 días.
            Assert.AreEqual("Compra cada 7 días y lleva 15 días sin hablar con usted",
                MotorSugerenciasContacto.Motivo(Cliente("b", 52, diasContacto: 15, probabilidad: 0.3f), PrioridadesContacto.ALTA, HOY));
        }

        [TestMethod]
        public void Motivo_Baja_MesDeLaUltimaCompra()
        {
            ClienteCarteraContacto cliente = Cliente("a", 0, pedidos24: 1);
            cliente.UltimoPedido = new DateTime(2026, 3, 10);
            Assert.AreEqual("Compró por última vez en marzo: toca el repaso mensual",
                MotorSugerenciasContacto.Motivo(cliente, PrioridadesContacto.BAJA, HOY));

            cliente.UltimoPedido = new DateTime(2025, 3, 10);
            Assert.AreEqual("Compró por última vez en marzo de 2025: toca el repaso mensual",
                MotorSugerenciasContacto.Motivo(cliente, PrioridadesContacto.BAJA, HOY));
        }

        [TestMethod]
        public void Motivo_Media_VecesYRepasoMensual()
        {
            ClienteCarteraContacto cliente = Cliente("a", 2, diasContacto: null);
            cliente.UltimoPedido = new DateTime(2026, 8, 3);

            Assert.AreEqual("Ha comprado 2 veces en el último año (la última en agosto) y sin ningún contacto registrado: toca el repaso mensual",
                MotorSugerenciasContacto.Motivo(cliente, PrioridadesContacto.MEDIA, HOY));
        }

        // ---------------- Ritmo ----------------

        [TestMethod]
        public void DiasLaborables_SinFinesDeSemanaNiFestivos()
        {
            Assert.AreEqual(21, MotorSugerenciasContacto.DiasLaborablesDelMes(HOY, laborable));
            Assert.AreEqual(22, MotorSugerenciasContacto.DiasLaborablesDelMes(HOY, d => true));
            // Del miércoles 7 (incluido) al 30: 7, 8, 9, 13-16, 19-23 y 26-30 = 17 (el 12 es festivo).
            Assert.AreEqual(17, MotorSugerenciasContacto.DiasLaborablesRestantes(HOY, laborable));
            // Hoy festivo: no cuenta.
            Assert.AreEqual(14, MotorSugerenciasContacto.DiasLaborablesRestantes(FESTIVO, laborable));
            // Sábado 31: no queda ninguno.
            Assert.AreEqual(0, MotorSugerenciasContacto.DiasLaborablesRestantes(new DateTime(2026, 10, 31), laborable));
        }

        [DataTestMethod]
        [DataRow(52, -1, 4, DisplayName = "semanal: cada 7 días, 31 / 7 = 4,4 → 4")]
        [DataRow(24, -1, 2, DisplayName = "quincenal: cada 15 días, 31 / 15 = 2,1 → 2")]
        [DataRow(42, -1, 3, DisplayName = "cada 9 días: 31 / 9 = 3,4 → 3 (con pedidos / 12 salían 4)")]
        [DataRow(17, -1, 1, DisplayName = "cada 21 días: 31 / 21 = 1,5 → 1")]
        [DataRow(6, -1, 1, DisplayName = "cada 30 días (tope): 31 / 30 = 1")]
        [DataRow(0, 2, 1, DisplayName = "solo compró en 24 meses (cada 30 días) → 1")]
        [DataRow(100, -1, 4, DisplayName = "cada 7 días (tope): 4")]
        public void ObjetivoMes_DiasNaturalesDelMesEntreCadenciaConMinimoUnoYMaximoCuatro(int pedidos12, int pedidos24, int esperado)
        {
            var cartera = new List<ClienteCarteraContacto> { Cliente("c", pedidos12, pedidos24: pedidos24) };

            // Octubre de 2026: 31 días naturales.
            Assert.AreEqual(esperado, MotorSugerenciasContacto.ObjetivoMes(cartera, HOY));
        }

        [DataTestMethod]
        [DataRow(2026, 10, DisplayName = "octubre, 31 días")]
        [DataRow(2026, 11, DisplayName = "noviembre, 30 días")]
        [DataRow(2027, 2, DisplayName = "febrero, 28 días")]
        public void ObjetivoMes_ClienteSemanal_CuatroLlamadasAunqueElMesTengaUnos21Laborables(int ano, int mes)
        {
            // El fallo de partida: 21 laborables / cadencia de 7 días naturales = 3 llamadas a quien pide cada semana.
            var cartera = new List<ClienteCarteraContacto> { Cliente("semanal", 52) };

            Assert.AreEqual(4, MotorSugerenciasContacto.ObjetivoMes(cartera, new DateTime(ano, mes, 15)));
        }

        [TestMethod]
        public void ObjetivoMes_DependeDeLosDiasNaturalesDelMes()
        {
            // 30 pedidos al año: cada 12 días. Octubre: 31 / 12 = 2,6 → 3; febrero: 28 / 12 = 2,3 → 2.
            var cartera = new List<ClienteCarteraContacto> { Cliente("c", 30) };

            Assert.AreEqual(3, MotorSugerenciasContacto.ObjetivoMes(cartera, HOY));
            Assert.AreEqual(2, MotorSugerenciasContacto.ObjetivoMes(cartera, new DateTime(2027, 2, 10)));
        }

        [TestMethod]
        public void ObjetivoMes_SumaLaCarteraYNoDependeDeLosLaborables()
        {
            // Antes (laborables / cadencia en días naturales) quien pide cada 15 días sumaba 1 en un mes de 21 laborables.
            var cartera = new List<ClienteCarteraContacto>
            {
                Cliente("semanal", 52),          // 4
                Cliente("quincenal", 24),        // 2
                Cliente("bueno", 5),             // 1
                Cliente("poco", 1),              // 1
                Cliente("antiguo", 0, pedidos24: 2), // 1
                Cliente("fuera", 0, pedidos24: 0)    // ni en 12 ni en 24 meses: no cuenta
            };

            Assert.AreEqual(9, MotorSugerenciasContacto.ObjetivoMes(cartera, HOY));
        }

        [TestMethod]
        public void CalcularRitmo_ElObjetivoDelMesSeRepartePorLosLaborablesQueQuedan()
        {
            // 21 clientes semanales: 84 llamadas en octubre (31 días naturales). Sin llamadas aún el jueves 1, quedan los 21
            // laborables (22 de lunes a viernes menos el festivo del 12) → 84 / 21 = 4 al día. Sábados, domingos y festivos no
            // reparten: el sábado 3 no hay objetivo, y el lunes 5 (19 laborables por delante) tocan ⌈84 / 19⌉ = 5.
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 21).Select(i => Cliente("s" + i, 52)).ToList();
            var motor = new MotorSugerenciasContacto();
            var sinContactos = new ContactosVendedor();
            var pendientes = new List<SugerenciaContactoDTO>();

            RitmoContactosDTO dia1 = motor.CalcularRitmo(cartera, sinContactos, pendientes, new DateTime(2026, 10, 1), laborable);
            RitmoContactosDTO sabado = motor.CalcularRitmo(cartera, sinContactos, pendientes, new DateTime(2026, 10, 3), laborable);
            RitmoContactosDTO lunes = motor.CalcularRitmo(cartera, sinContactos, pendientes, new DateTime(2026, 10, 5), laborable);

            Assert.AreEqual(84, dia1.ObjetivoMes);
            Assert.AreEqual(21, dia1.DiasLaborablesRestantesMes);
            Assert.AreEqual(4, dia1.ObjetivoHoy);
            Assert.AreEqual(19, sabado.DiasLaborablesRestantesMes, "el sábado no cuenta, pero sí todo lo que queda");
            Assert.AreEqual(19, lunes.DiasLaborablesRestantesMes);
            Assert.AreEqual(5, lunes.ObjetivoHoy);
        }

        [TestMethod]
        public void CalcularRitmo_ObjetivoDeHoyConLoQueFaltaEntreLosDiasQueQuedan()
        {
            // 30 clientes semanales: objetivo 30 × 4 = 120. Llevan 22 este mes: faltan 98 en 17 días → 5,8 → 6 al día.
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 30).Select(i => Cliente("s" + i, 52, diasContacto: 3)).ToList();
            var pendientes = new List<SugerenciaContactoDTO>
            {
                new SugerenciaContactoDTO { Prioridad = PrioridadesContacto.MAXIMA },
                new SugerenciaContactoDTO { Prioridad = PrioridadesContacto.ALTA },
                new SugerenciaContactoDTO { Prioridad = PrioridadesContacto.ALTA },
                new SugerenciaContactoDTO { Prioridad = PrioridadesContacto.BAJA }
            };

            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera,
                new ContactosVendedor { Hoy = 9, Semana = 14, Mes = 22 }, pendientes, HOY, laborable);

            Assert.AreEqual(9, ritmo.ContactosHoy);
            Assert.AreEqual(14, ritmo.ContactosSemana);
            Assert.AreEqual(22, ritmo.ContactosMes);
            Assert.AreEqual(120, ritmo.ObjetivoMes);
            Assert.AreEqual(17, ritmo.DiasLaborablesRestantesMes);
            Assert.AreEqual(6, ritmo.ObjetivoHoy);
            Assert.AreEqual(1, ritmo.PendientesMaxima);
            Assert.AreEqual(2, ritmo.PendientesAlta);
            Assert.AreEqual(0, ritmo.PendientesMedia);
            Assert.AreEqual(1, ritmo.PendientesBaja);
            Assert.AreEqual("Lleva 9 contactos hoy; para cubrir la cartera este mes necesita 6 al día.", ritmo.Frase);
        }

        [TestMethod]
        public void CalcularRitmo_FinDeSemanaAFinDeMes_ObjetivoDeHoyCero()
        {
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 10).Select(i => Cliente("s" + i, 5)).ToList();

            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera,
                new ContactosVendedor { Hoy = 0, Mes = 4 }, new List<SugerenciaContactoDTO>(), new DateTime(2026, 10, 31), laborable);

            Assert.AreEqual(0, ritmo.DiasLaborablesRestantesMes);
            Assert.AreEqual(0, ritmo.ObjetivoHoy);
            Assert.AreEqual("Lleva 0 contactos hoy; este mes van 4 de los 10 que hacían falta para cubrir la cartera.", ritmo.Frase);
        }

        [TestMethod]
        public void CalcularRitmo_ObjetivoCubierto_CeroYFraseDeEnhorabuena()
        {
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 10).Select(i => Cliente("s" + i, 5)).ToList();

            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera,
                new ContactosVendedor { Hoy = 1, Mes = 12 }, new List<SugerenciaContactoDTO>(), HOY, laborable);

            Assert.AreEqual(0, ritmo.ObjetivoHoy);
            Assert.AreEqual("Lleva 1 contacto hoy y 12 este mes: ya ha cubierto el objetivo de 10. ¡Buen trabajo!", ritmo.Frase);
        }

        // ---------------- Sugerencia 782: clientes distintos de la cartera, no rapports ----------------

        private static ContactoClienteDia Hablo(string cliente, DateTime dia) => new ContactoClienteDia { Cliente = cliente, Contacto = "0", Dia = dia };

        [TestMethod]
        public void ContarContactos_VariosRapportsAlMismoClienteYFueraDeCartera_CuentanClientesDistintosDeLaCartera()
        {
            // a: el 1, dos el 2 y hoy. b: ayer y hoy (con el número relleno de espacios, como viene de la BD). c: el lunes 5.
            // d está en la lista pero sin compras en 24 meses (fuera del objetivo) y zz no es de la cartera: hoy no cuentan.
            var cartera = new List<ClienteCarteraContacto> { Cliente("a", 5), Cliente("b", 5), Cliente("c", 5), Cliente("d", 0, pedidos24: 0) };
            var registros = new List<ContactoClienteDia>
            {
                Hablo("a", new DateTime(2026, 10, 1)), Hablo("a", new DateTime(2026, 10, 2)), Hablo("a", new DateTime(2026, 10, 2)), Hablo("a", HOY),
                Hablo("b   ", HOY.AddDays(-1)), Hablo("b", HOY),
                Hablo("c", new DateTime(2026, 10, 5)),
                Hablo("d", HOY), Hablo("zz", HOY), Hablo("zz", new DateTime(2026, 10, 2))
            };

            ContactosVendedor contactos = MotorSugerenciasContacto.ContarContactos(registros, cartera, HOY);

            Assert.AreEqual(2, contactos.Hoy, "a y b, aunque hoy haya cuatro rapports");
            Assert.AreEqual(3, contactos.Semana, "a, b y c");
            Assert.AreEqual(3, contactos.Mes, "a con cuatro rapports y b ayer y hoy cuentan uno cada uno");
            Assert.AreEqual(3, contactos.MesAntesDeHoy, "a, b y c: no es Mes − Hoy");
            CollectionAssert.AreEquivalent(new[] { new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), new DateTime(2026, 10, 5), HOY.AddDays(-1) },
                contactos.PorDia.Keys.ToArray(), "los días anteriores a hoy con algún cliente de la cartera");
            Assert.IsTrue(contactos.PorDia.Values.All(n => n == 1), "el 2, a con dos rapports cuenta uno");
        }

        [TestMethod]
        public void CalcularRitmo_ConClientesDistintos_ElObjetivoDeHoyVaConLosClientesQueFaltan()
        {
            // 40 clientes de una llamada al mes: objetivo 40. s1 con cinco rapports este mes, s2-s5 con uno, y diez rapports a
            // clientes que no son de la cartera: van 5 clientes, faltan 35 en 17 laborables → ⌈2,06⌉ = 3 (con rapports serían 19 y 2).
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 40).Select(i => Cliente("s" + i, 5)).ToList();
            var registros = new[] { 1, 2, 5, 6, 7 }.Select(d => Hablo("s1", new DateTime(2026, 10, d)))
                .Concat(Enumerable.Range(2, 4).Select(i => Hablo("s" + i, new DateTime(2026, 10, 2))))
                .Concat(Enumerable.Range(1, 10).Select(i => Hablo("otro" + i, new DateTime(2026, 10, 6))))
                .ToList();

            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera,
                MotorSugerenciasContacto.ContarContactos(registros, cartera, HOY), new List<SugerenciaContactoDTO>(), HOY, laborable);

            Assert.AreEqual(40, ritmo.ObjetivoMes);
            Assert.AreEqual(5, ritmo.ContactosMes);
            Assert.AreEqual(1, ritmo.ContactosHoy);
            Assert.AreEqual(17, ritmo.DiasLaborablesRestantesMes);
            Assert.AreEqual(3, ritmo.ObjetivoHoy);
        }

        [TestMethod]
        public void Situacion_QuedanClientesDeLaCarteraSinContactar_NoEsMesCumplidoAunqueHayaMasRapportsQueObjetivo()
        {
            // 10 clientes de una llamada al mes; 9 contactados el día 2 y uno de ellos tres veces más (el 5, ayer y hoy): 12
            // rapports, 9 clientes. Queda uno: no es «mes cumplido» y el objetivo de hoy (del principio del día) es 1, porque
            // antes de hoy ya iban los 9 (Mes − Hoy daría 8).
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 10).Select(i => Cliente("s" + i, 5)).ToList();
            var registros = Enumerable.Range(1, 9).Select(i => Hablo("s" + i, new DateTime(2026, 10, 2)))
                .Concat(new[] { Hablo("s1", new DateTime(2026, 10, 5)), Hablo("s1", HOY.AddDays(-1)), Hablo("s1", HOY) })
                .ToList();

            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera,
                MotorSugerenciasContacto.ContarContactos(registros, cartera, HOY), new List<SugerenciaContactoDTO>(), HOY, laborable);
            SituacionRitmo s = GeneradorFrasesRitmo.CalcularSituacion(ritmo, HOY.AddHours(12), laborable, ritmo.ContactosPorDia, 0, "Javier");

            Assert.AreEqual(9, ritmo.ContactosMes);
            Assert.AreEqual(9, ritmo.ContactosMesAntesDeHoy);
            Assert.AreEqual(1, ritmo.ObjetivoHoy);
            Assert.AreNotEqual(SituacionesRitmo.MES_CUMPLIDO, s.Clave);
            Assert.AreEqual(1, s.ObjetivoHoy);
            Assert.AreEqual(9, s.Mes);
        }
    }
}
