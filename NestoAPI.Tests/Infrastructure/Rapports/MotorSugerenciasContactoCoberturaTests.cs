using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>
    /// NestoAPI#677 (corte 1): la lista del día por probabilidad con un cupo de cobertura que crece con el mes (a fin de
    /// mes, todos los compradores del vendedor contactados) y el origen de cada sugerencia para medir.
    /// </summary>
    [TestClass]
    public class MotorSugerenciasContactoCoberturaTests
    {
        // Octubre de 2026: el 1 es jueves; 22 días de lunes a viernes y, con el 12 festivo, 21 laborables.
        private static readonly DateTime FESTIVO = new DateTime(2026, 10, 12);
        private static readonly Func<DateTime, bool> laborable = d => d.Date != FESTIVO;
        private static readonly DateTime DIA_1 = new DateTime(2026, 10, 1);    // 21 laborables por delante
        private static readonly DateTime MITAD = new DateTime(2026, 10, 15);   // jueves: 15, 16, 19-23 y 26-30 = 12
        private static readonly DateTime ULTIMO = new DateTime(2026, 10, 30);  // viernes, último laborable

        private static ClienteCarteraContacto Cliente(string id, int pedidos12, DateTime? ultimoContacto, float probabilidad = 0f,
            DateTime? ultimoIntento = null, DateTime? ultimoPedido = null, int pedidos24 = -1)
        {
            return new ClienteCarteraContacto
            {
                Cliente = id,
                Contacto = "0",
                Nombre = "Centro " + id,
                Pedidos12Meses = pedidos12,
                Pedidos24Meses = pedidos24 >= 0 ? pedidos24 : Math.Max(pedidos12, 1),
                Importe12Meses = pedidos12 * 100m,
                UltimoContacto = ultimoContacto?.AddHours(10),
                UltimoIntento = ultimoIntento?.AddHours(11),
                UltimoPedido = ultimoPedido ?? new DateTime(2026, 8, 1),
                Probabilidad = probabilidad
            };
        }

        private static List<SugerenciaContactoDTO> Elegir(List<ClienteCarteraContacto> cartera, DateTime hoy, int cuantas, int cupo,
            IEnumerable<string> yaSugeridas = null)
            => MotorSugerenciasContacto.ElegirDelDia(new MotorSugerenciasContacto().Priorizar(cartera, hoy), cartera, hoy, cuantas, cupo, yaSugeridas);

        // ---------------- Comprador y pendiente de cobertura ----------------

        [TestMethod]
        public void PendienteCobertura_CompradorSinContactoDesdeElDia1()
        {
            Assert.IsTrue(MotorSugerenciasContacto.PendienteCobertura(Cliente("nunca", 2, null), MITAD));
            Assert.IsTrue(MotorSugerenciasContacto.PendienteCobertura(Cliente("septiembre", 2, new DateTime(2026, 9, 30)), MITAD));
            Assert.IsFalse(MotorSugerenciasContacto.PendienteCobertura(Cliente("dia1", 2, new DateTime(2026, 10, 1)), MITAD), "ya contactado este mes");
            Assert.IsFalse(MotorSugerenciasContacto.PendienteCobertura(Cliente("antiguo", 0, null, pedidos24: 3), MITAD),
                "sin compras en 12 meses no es comprador");
        }

        [TestMethod]
        public void DisponibleParaCobertura_LaCadenciaNoCuentaPeroElIntentoYElPedidoRecienteSi()
        {
            // Buen cliente con cadencia 30 contactado hace 20 días (en septiembre): por cadencia hoy no le toca, por cupo sí.
            ClienteCarteraContacto enCadencia = Cliente("cadencia", 6, new DateTime(2026, 9, 25));
            Assert.IsNull(MotorSugerenciasContacto.Prioridad(enCadencia, MITAD));
            Assert.IsTrue(MotorSugerenciasContacto.DisponibleParaCobertura(enCadencia, MITAD));

            Assert.IsFalse(MotorSugerenciasContacto.DisponibleParaCobertura(Cliente("intento", 6, null, ultimoIntento: MITAD.AddDays(-1)), MITAD));
            Assert.IsFalse(MotorSugerenciasContacto.DisponibleParaCobertura(Cliente("pedido", 6, null, ultimoPedido: MITAD.AddDays(-3)), MITAD));
        }

        // ---------------- El cupo crece con el mes ----------------

        [DataTestMethod]
        [DataRow(1, 42, 2, DisplayName = "Día 1: 42 / 21 laborables")]
        [DataRow(15, 42, 4, DisplayName = "Mitad: ⌈42 / 12⌉")]
        [DataRow(29, 42, 21, DisplayName = "Penúltimo laborable: la mitad")]
        [DataRow(30, 42, 42, DisplayName = "Último laborable: todos los que faltan")]
        [DataRow(31, 42, 42, DisplayName = "Sábado después del último laborable: todos")]
        [DataRow(15, 0, 0, DisplayName = "Sin pendientes, sin cupo")]
        public void CupoCobertura_PendientesEntreLaborablesQueQuedan(int dia, int pendientes, int esperado)
        {
            Assert.AreEqual(esperado, MotorSugerenciasContacto.CupoCobertura(pendientes, new DateTime(2026, 10, dia), laborable));
        }

        [TestMethod]
        public void CupoCobertura_LosFestivosNoReparten()
        {
            // Viernes 9: con el 12 festivo quedan 15 laborables (⌈31 / 15⌉ = 3); sin festivo, 16 (⌈31 / 16⌉ = 2).
            DateTime viernes9 = new DateTime(2026, 10, 9);
            Assert.AreEqual(3, MotorSugerenciasContacto.CupoCobertura(31, viernes9, laborable));
            Assert.AreEqual(2, MotorSugerenciasContacto.CupoCobertura(31, viernes9, d => true));

            // Si el 30 es festivo, el último laborable es el jueves 29: ese día entran todos.
            Assert.AreEqual(31, MotorSugerenciasContacto.CupoCobertura(31, new DateTime(2026, 10, 29), d => d.Date != new DateTime(2026, 10, 30)));
        }

        [TestMethod]
        public void CalcularRitmo_DevuelveLosCompradoresSinContactoYElCupoDeHoy()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                Cliente("a", 6, null), Cliente("b", 2, new DateTime(2026, 9, 2)), Cliente("c", 1, new DateTime(2026, 9, 20)),
                Cliente("d", 52, new DateTime(2026, 10, 5)),        // contactado este mes
                Cliente("e", 0, null, pedidos24: 2)                 // no es comprador (solo 24 meses)
            };

            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera, new ContactosVendedor(), new List<SugerenciaContactoDTO>(), MITAD, laborable);

            Assert.AreEqual(3, ritmo.CompradoresSinContactoMes);
            Assert.AreEqual(1, ritmo.CupoCoberturaHoy, "⌈3 / 12⌉");
        }

        // ---------------- La lista del día ----------------

        [TestMethod]
        public void ElegirDelDia_OrdenPorProbabilidad_YSiYaSonCompradoresSinContactoElCupoNoGastaSitio()
        {
            // Día 1: nadie contactado en octubre, así que los más probables ya cubren el cupo.
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 10)
                .Select(i => Cliente("c" + i, 6, new DateTime(2026, 9, 1), probabilidad: i / 20f))
                .ToList();
            int cupo = MotorSugerenciasContacto.CupoCobertura(MotorSugerenciasContacto.CompradoresSinContactoMes(cartera, DIA_1), DIA_1, laborable);
            Assert.AreEqual(1, cupo);

            List<SugerenciaContactoDTO> lista = Elegir(cartera, DIA_1, 5, cupo);

            CollectionAssert.AreEqual(new[] { "c10", "c9", "c8", "c7", "c6" }, lista.Select(s => s.Cliente).ToArray());
            Assert.IsTrue(lista.All(s => s.Origen == OrigenesSugerenciaContacto.PROBABILIDAD));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, lista.Select(s => s.Orden).ToArray());
        }

        [TestMethod]
        public void ElegirDelDia_MitadDeMes_ElCupoEntraIntercaladoConLosQueMasTiempoLlevanSinContacto()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                // Semanales ya contactados este mes, con probabilidad alta: les toca por cadencia, pero no son pendientes.
                Cliente("s90", 52, new DateTime(2026, 10, 5), 0.9f),
                Cliente("s80", 52, new DateTime(2026, 10, 5), 0.8f),
                Cliente("s70", 52, new DateTime(2026, 10, 5), 0.7f),
                Cliente("s60", 52, new DateTime(2026, 10, 5), 0.6f),
                // Compradores sin contacto en octubre, con probabilidad baja.
                Cliente("sep20", 2, new DateTime(2026, 9, 20), 0.05f),   // dentro de su cadencia (30): solo entra por cupo
                Cliente("sep01", 2, new DateTime(2026, 9, 1), 0.01f),
                Cliente("sep25", 2, new DateTime(2026, 9, 25), 0.30f)
            };

            List<SugerenciaContactoDTO> lista = Elegir(cartera, MITAD, 5, 2);

            // 3 por probabilidad y 2 por cupo, en los puestos 2 y 4.
            CollectionAssert.AreEqual(new[] { "s90", "sep01", "s80", "sep20", "s70" }, lista.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "Probabilidad", "Cobertura", "Probabilidad", "Cobertura", "Probabilidad" }, lista.Select(s => s.Origen).ToArray());
            SugerenciaContactoDTO sep20 = lista.Single(s => s.Cliente == "sep20");
            Assert.AreEqual(PrioridadesContacto.MEDIA, sep20.Prioridad, "por cadencia no le tocaba: etiqueta Media (no es buen cliente)");
            Assert.AreEqual("Cobertura del mes: sin contacto desde el 20 de septiembre; ha comprado 2 veces en el último año", sep20.Motivo);
        }

        [TestMethod]
        public void ElegirDelDia_UltimoLaborable_EntranTodosLosCompradoresQueFaltan()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                Cliente("s90", 52, new DateTime(2026, 10, 20), 0.9f),
                Cliente("s80", 52, new DateTime(2026, 10, 20), 0.8f),
                Cliente("s70", 52, new DateTime(2026, 10, 20), 0.7f)
            };
            cartera.AddRange(Enumerable.Range(1, 7).Select(i => Cliente("p" + i, 2, new DateTime(2026, 9, 10 + i), 0.01f)));
            RitmoContactosDTO ritmo = new MotorSugerenciasContacto().CalcularRitmo(cartera, new ContactosVendedor(), new List<SugerenciaContactoDTO>(), ULTIMO, laborable);
            Assert.AreEqual(7, ritmo.CupoCoberturaHoy);

            // Aunque se pidan 5, el último día el cupo manda: los 7 pendientes, ninguno por probabilidad.
            List<SugerenciaContactoDTO> justas = Elegir(cartera, ULTIMO, Math.Max(5, ritmo.CupoCoberturaHoy), ritmo.CupoCoberturaHoy);
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 7).Select(i => "p" + i).ToArray(), justas.Select(s => s.Cliente).ToArray());

            // Con sitio para 10, los 3 más probables y los 7 pendientes.
            List<SugerenciaContactoDTO> conSitio = Elegir(cartera, ULTIMO, 10, ritmo.CupoCoberturaHoy);
            Assert.AreEqual(10, conSitio.Count);
            Assert.IsTrue(Enumerable.Range(1, 7).All(i => conSitio.Any(s => s.Cliente == "p" + i)));
            CollectionAssert.AreEqual(new[] { "s90", "s80", "s70" }, conSitio.Where(s => s.Cliente.StartsWith("s")).Select(s => s.Cliente).ToArray());
        }

        [TestMethod]
        public void ElegirDelDia_PendienteQuePidioHaceTresDias_NoEntraNiElUltimoDia()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                Cliente("pidio", 2, new DateTime(2026, 9, 1), ultimoPedido: ULTIMO.AddDays(-3)),
                Cliente("falta", 2, new DateTime(2026, 9, 2))
            };

            List<SugerenciaContactoDTO> lista = Elegir(cartera, ULTIMO, 5, 2);

            CollectionAssert.AreEqual(new[] { "falta" }, lista.Select(s => s.Cliente).ToArray());
        }

        [TestMethod]
        public void ElegirDelDia_SinCandidatosParaElCupo_ElSitioVuelveALaProbabilidad()
        {
            List<ClienteCarteraContacto> cartera = Enumerable.Range(1, 6)
                .Select(i => Cliente("s" + i, 52, new DateTime(2026, 10, 5), i / 10f))
                .ToList();

            List<SugerenciaContactoDTO> lista = Elegir(cartera, MITAD, 4, 3);

            CollectionAssert.AreEqual(new[] { "s6", "s5", "s4", "s3" }, lista.Select(s => s.Cliente).ToArray());
            Assert.IsTrue(lista.All(s => s.Origen == OrigenesSugerenciaContacto.PROBABILIDAD));
        }

        [TestMethod]
        public void ElegirDelDia_LosDelChequeDelanteYLasYaSugeridasNoSeRepiten()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                Cliente("s90", 52, new DateTime(2026, 10, 5), 0.9f),
                Cliente("s80", 52, new DateTime(2026, 10, 5), 0.8f),
                Cliente("s70", 52, new DateTime(2026, 10, 5), 0.7f),
                Cliente("cheque", 52, new DateTime(2026, 10, 14), 0.1f),
                Cliente("sep01", 2, new DateTime(2026, 9, 1)),
                Cliente("sep02", 2, new DateTime(2026, 9, 2))
            };
            List<SugerenciaContactoDTO> pendientes = new MotorSugerenciasContacto().Priorizar(cartera, MITAD);
            SugerenciaContactoDTO conCheque = MotorSugerenciasContacto.ADTO(cartera.Single(c => c.Cliente == "cheque"), MITAD);
            conCheque.Prioridad = PrioridadesContacto.MAXIMA;
            conCheque.TieneChequeRegalo = true;
            conCheque.Origen = OrigenesSugerenciaContacto.CHEQUE_REGALO;
            pendientes.Insert(0, conCheque);

            List<SugerenciaContactoDTO> lista = MotorSugerenciasContacto.ElegirDelDia(pendientes, cartera, MITAD, 4, 1, new[] { "s90/0" });

            CollectionAssert.AreEqual(new[] { "cheque", "s80", "sep01", "s70" }, lista.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "ChequeRegalo", "Probabilidad", "Cobertura", "Probabilidad" }, lista.Select(s => s.Origen).ToArray());
        }

        [TestMethod]
        public void Intercalar_ADistanciasIgualesSinCambiarElOrdenDeNinguna()
        {
            List<string> base16 = Enumerable.Range(1, 16).Select(i => "p" + i).ToList();
            List<string> cupo4 = Enumerable.Range(1, 4).Select(i => "c" + i).ToList();

            List<string> lista = MotorSugerenciasContacto.Intercalar(base16, cupo4);

            Assert.AreEqual(20, lista.Count);
            CollectionAssert.AreEqual(new[] { 2, 7, 12, 17 }, cupo4.Select(c => lista.IndexOf(c)).ToArray());
            CollectionAssert.AreEqual(base16, lista.Where(x => x.StartsWith("p")).ToList());
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, MotorSugerenciasContacto.Intercalar(new List<string>(), new List<string> { "c1", "c2" }));
            CollectionAssert.AreEqual(new[] { "p1", "c1", "p2" }, MotorSugerenciasContacto.Intercalar(new List<string> { "p1", "p2" }, new List<string> { "c1" }));
        }

        // ---------------- Origen (medición sin cambiar la BD) ----------------

        [TestMethod]
        public void Origen_SeLeeDelPrincipioDelMotivo()
        {
            Assert.AreEqual(OrigenesSugerenciaContacto.COBERTURA, OrigenesSugerenciaContacto.DeMotivo("Cobertura del mes: sin ningún contacto registrado; ha comprado 1 vez en el último año"));
            Assert.AreEqual(OrigenesSugerenciaContacto.CHEQUE_REGALO, OrigenesSugerenciaContacto.DeMotivo("Tiene un cheque de 50 € sin usar hasta el 07/11"));
            Assert.AreEqual(OrigenesSugerenciaContacto.PROBABILIDAD, OrigenesSugerenciaContacto.DeMotivo("Compra cada 7 días y lleva 15 días sin hablar contigo"));
            Assert.AreEqual(OrigenesSugerenciaContacto.PROBABILIDAD, OrigenesSugerenciaContacto.DeMotivo(null));
        }

        [TestMethod]
        public void MotivoCobertura_EmpiezaPorElPrefijoQueSeMide()
        {
            string motivo = MotorSugerenciasContacto.MotivoCobertura(Cliente("nunca", 1, null), MITAD);

            Assert.AreEqual("Cobertura del mes: sin ningún contacto registrado; ha comprado 1 vez en el último año", motivo);
            Assert.AreEqual(OrigenesSugerenciaContacto.COBERTURA, OrigenesSugerenciaContacto.DeMotivo(motivo));
        }
    }
}
