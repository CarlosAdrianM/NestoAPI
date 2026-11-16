using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#865 (corte 1, Carlos 16/11/26): la fecha de recepción vencida de una línea de compra en el aire se recalcula en el job del
    /// correo de proveedores vencidos (correo de seguimiento si lo hay; si no, «según lo ya esperado»), se escribe con rastro y promesa en
    /// riesgo, y desde el 2.º vencimiento el correo dice que hay que llamar al proveedor.
    /// </summary>
    [TestClass]
    public class FechasVencidasTests
    {
        // Lunes
        private static readonly DateTime HOY = new DateTime(2026, 11, 16);
        private static readonly DateTime ANTERIOR = new DateTime(2026, 11, 13);
        private static readonly DateTime PEDIDO = new DateTime(2026, 10, 14); // 33 días antes de HOY

        private static bool FinDeSemana(DateTime d) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

        private static LineaCompraVencida Linea(int pedido = 220597, int orden = 1, string producto = "45945", string proveedor = "1480") => new LineaCompraVencida
        {
            Empresa = "1",
            Pedido = pedido,
            Orden = orden,
            Proveedor = proveedor,
            NombreProveedor = "Casajuana",
            Producto = producto,
            Descripcion = "Crema",
            Almacen = "ALG",
            Cantidad = 6,
            Estado = 1,
            FechaPrevista = ANTERIOR,
            FechaPedido = PEDIDO
        };

        #region Regla

        [TestMethod]
        public void Regla_ConCincoCasosQueTardaronMas_ElP80DesdeElDiaDelPedido()
        {
            // 33 días esperando; los que tardaron más: 34, 38, 40, 44, 60 → P80 = 44 → 14/10 + 44 = 27/11
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 5, 12, 20, 33, 34, 38, 40, 44, 60 }, 12, null, null, FinDeSemana);

            Assert.AreEqual(OrigenFechaRecalculada.HistorialProveedor, r.Origen);
            Assert.AreEqual(33, r.DiasEsperando);
            Assert.AreEqual(5, r.Casos);
            Assert.AreEqual(44, r.DiasP80);
            Assert.AreEqual(new DateTime(2026, 11, 27), r.FechaNueva);
            Assert.IsFalse(r.AjustadaAlLaborable);
        }

        [TestMethod]
        public void Regla_ConMenosDeCincoCasos_HoyMasElP80Generico()
        {
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 10, 40, 50 }, 9, null, null, FinDeSemana);

            Assert.AreEqual(OrigenFechaRecalculada.PlazoGenerico, r.Origen);
            Assert.AreEqual(2, r.Casos);
            Assert.AreEqual(9, r.DiasP80);
            Assert.AreEqual(new DateTime(2026, 11, 25), r.FechaNueva);
        }

        [TestMethod]
        public void Regla_SinDiaDelPedido_PlazoGenerico()
        {
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, null, new[] { 40, 41, 42, 43, 44 }, 9, null, null, FinDeSemana);

            Assert.AreEqual(OrigenFechaRecalculada.PlazoGenerico, r.Origen);
            Assert.IsNull(r.DiasEsperando);
            Assert.AreEqual(0, r.Casos);
        }

        [TestMethod]
        public void Regla_NuncaAntesDelSiguienteLaborable()
        {
            // Viernes 20/11 con plazo genérico 0: salía hoy → el lunes 23/11
            DateTime viernes = new DateTime(2026, 11, 20);
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(viernes, ANTERIOR, PEDIDO, null, 0, null, null, FinDeSemana);

            Assert.AreEqual(new DateTime(2026, 11, 23), r.FechaNueva);
            Assert.AreEqual(viernes, r.FechaEstimada);
            Assert.IsTrue(r.AjustadaAlLaborable);
        }

        [TestMethod]
        public void Regla_SiguienteLaborable_SaltaFestivosYFinesDeSemana()
        {
            DateTime viernes = new DateTime(2026, 12, 4);
            Assert.AreEqual(new DateTime(2026, 12, 9), ReglaFechaVencida.SiguienteLaborable(viernes,
                d => FinDeSemana(d) || d == new DateTime(2026, 12, 7) || d == new DateTime(2026, 12, 8)));
        }

        [TestMethod]
        public void Regla_ConCorreoDeHoyODespues_ManaElCorreoAunqueSeaHoy()
        {
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 34, 38, 40, 44, 60 }, 9, HOY, " GLS ", FinDeSemana);

            Assert.AreEqual(OrigenFechaRecalculada.CorreoSeguimiento, r.Origen);
            Assert.AreEqual(HOY, r.FechaNueva);
            Assert.AreEqual("GLS", r.Agencia);
            Assert.IsFalse(r.AjustadaAlLaborable);
        }

        [TestMethod]
        public void Regla_ConCorreoTambienVencido_SinNoticias()
        {
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 34, 38, 40, 44, 60 }, 9, HOY.AddDays(-1), "GLS", FinDeSemana);

            Assert.AreEqual(OrigenFechaRecalculada.HistorialProveedor, r.Origen);
        }

        [TestMethod]
        public void Regla_SinNadaConQueCalcular_Null()
        {
            Assert.IsNull(ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 40 }, null, null, null, FinDeSemana));
        }

        [TestMethod]
        public void Regla_PlazosPorProveedor_SinLosNegativos()
        {
            Dictionary<string, List<int>> plazos = ReglaFechaVencida.PlazosPorProveedor(new[]
            {
                new RecepcionCompra { Proveedor = "1480 ", FechaPedido = new DateTime(2026, 1, 1), FechaAlbaran = new DateTime(2026, 1, 11) },
                new RecepcionCompra { Proveedor = "1480", FechaPedido = new DateTime(2026, 2, 10), FechaAlbaran = new DateTime(2026, 2, 1) },
                new RecepcionCompra { Proveedor = "200", FechaPedido = new DateTime(2026, 3, 1), FechaAlbaran = new DateTime(2026, 3, 4) }
            });

            CollectionAssert.AreEqual(new[] { 10 }, plazos["1480"]);
            CollectionAssert.AreEqual(new[] { 3 }, plazos["200"]);
        }

        [TestMethod]
        public void Regla_HayQueLlamar_DesdeElSegundoVencimiento()
        {
            Assert.IsFalse(ReglaFechaVencida.HayQueLlamar(1));
            Assert.IsTrue(ReglaFechaVencida.HayQueLlamar(2));
            Assert.IsTrue(ReglaFechaVencida.HayQueLlamar(5));
        }

        [TestMethod]
        public void DiaDeLlegada_DelCorreo_SinElSueloDeHoy()
        {
            var correo = new CorreoSeguimientoFila { Id = 1, Recibido = new DateTime(2026, 11, 10, 9, 0, 0), EntregaEstimadaDia = new DateTime(2026, 11, 12) };

            Assert.AreEqual(new DateTime(2026, 11, 12), ReglaFechaRecepcionPorCorreo.DiaDeLlegada(new[] { correo }));
            Assert.AreEqual(HOY, ReglaFechaRecepcionPorCorreo.Calcular(new[] { correo }, HOY), "La de siempre sigue con el suelo de hoy");
            Assert.IsNull(ReglaFechaRecepcionPorCorreo.DiaDeLlegada(new CorreoSeguimientoFila[0]));
        }

        #endregion

        #region Correos del pedido

        private static CorreoSeguimientoFila Correo(int id, string codigo, string candidatos = null, int? aplicado = null, bool? descartado = null,
            int? pedidoReal = null) => new CorreoSeguimientoFila
            {
                Id = id,
                MensajeId = "m" + id,
                Recibido = HOY.AddDays(-1).AddHours(id),
                Codigo = codigo,
                Agencia = "GLS",
                PedidosCandidatos = candidatos,
                EmpresaAplicada = aplicado.HasValue ? "1" : null,
                PedidoAplicado = aplicado,
                Descartado = descartado,
                PedidoReal = pedidoReal
            };

        private static string Candidatos(params int[] pedidos) => AsignadorPedidosCompra.Serializar(
            pedidos.Select(p => new CandidatoPedidoCompra { Empresa = "1", Pedido = p, Proveedor = "1480", Motivo = AsignadorPedidosCompra.MOTIVO_NUMERO }).ToList());

        [TestMethod]
        public void CorreosDelPedido_AplicadoOInequivoco_SiDescartadoLlegadoOVariosCandidatos_No()
        {
            Dictionary<string, List<CorreoSeguimientoFila>> porPedido = CorreosDelPedidoCompra.Agrupar(new[]
            {
                Correo(1, "1ZAAA1234567", Candidatos(220597), aplicado: 220597),
                Correo(2, "1ZAAA1234567", null),
                Correo(3, "1ZBBB1234567", Candidatos(220856)),
                Correo(4, "1ZCCC1234567", Candidatos(220782), descartado: true),
                Correo(5, "1ZDDD1234567", Candidatos(220001, 220002)),
                Correo(6, "1ZEEE1234567", Candidatos(220500), pedidoReal: 220500)
            });

            CollectionAssert.AreEquivalent(new[] { "1/220597", "1/220856" }, porPedido.Keys.ToList());
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, porPedido["1/220597"].Select(f => f.Id).ToList(), "Todo el envío del aplicado");
        }

        #endregion

        #region Textos

        [TestMethod]
        public void Textos_Historial_ConLasCifras()
        {
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 34, 38, 40, 44, 60 }, 12, null, null, FinDeSemana);

            Assert.AreEqual("Había pasado la fecha (13/11/2026); la hemos cambiado al 27/11/2026: lleva 33 días esperando y, de las 5 veces que " +
                "este proveedor tardó más de 33 días, el 80 % llegó en 44 días o menos desde que se le pidió.", TextosFechaVencida.Linea(r));
        }

        [TestMethod]
        public void Textos_Generico_YAjustado()
        {
            RecalculoFechaVencida pocos = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new[] { 40 }, 9, null, null, FinDeSemana);
            RecalculoFechaVencida ninguno = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, new int[0], 9, null, null, FinDeSemana);
            RecalculoFechaVencida ajustado = ReglaFechaVencida.Calcular(new DateTime(2026, 11, 20), ANTERIOR, PEDIDO, null, 0, null, null, FinDeSemana);

            Assert.AreEqual("Había pasado la fecha (13/11/2026); la hemos cambiado al 25/11/2026: lleva 33 días esperando y en los dos últimos años " +
                "este proveedor solo tardó tanto una vez (muy pocas para fiarse), así que le damos desde hoy lo que suelen tardar todos los proveedores " +
                "(el 80 % llega en 9 días o menos).", TextosFechaVencida.Linea(pocos));
            StringAssert.Contains(TextosFechaVencida.Linea(ninguno), "este proveedor nunca tardó tanto");
            StringAssert.EndsWith(TextosFechaVencida.Linea(ajustado), "; salía el 20/11/2026, pero no puede llegar antes del siguiente día laborable.");
        }

        [TestMethod]
        public void Textos_Correo_YLlamar()
        {
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, null, null, new DateTime(2026, 11, 18), "GLS", FinDeSemana);

            Assert.AreEqual("Había pasado la fecha (13/11/2026); la hemos cambiado al 18/11/2026: lo dice el correo de seguimiento de GLS.", TextosFechaVencida.Linea(r));
            Assert.AreEqual("Hay que llamar al proveedor: es la 2.ª vez que pasa la fecha de esta línea.", TextosFechaVencida.Llamar(2));
            Assert.AreEqual(TextosFechaVencida.SIN_RECALCULAR, TextosFechaVencida.Linea(null));
        }

        #endregion

        #region Rastro e interruptor

        [TestMethod]
        public void Rastro_CuentaLasFilasDeCadaLinea_YLoIlegibleNoCuenta()
        {
            LineaCompraVencida linea = Linea();
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, null, 9, null, null, FinDeSemana);
            string nuevo = RastroFechaRecalculada.Nuevo(linea, r, 1);

            Dictionary<string, int> cuenta = RastroFechaRecalculada.ContarPorLinea(new[] { nuevo, nuevo, "no es json", null, RastroFechaRecalculada.Nuevo(Linea(orden: 2), r, 1) });

            Assert.AreEqual(2, cuenta["1/220597/1"]);
            Assert.AreEqual(1, cuenta["1/220597/2"]);
            StringAssert.Contains(nuevo, "\"FechaRecepcion\":\"2026-11-25\"");
            StringAssert.Contains(nuevo, "\"FechaAnterior\":\"2026-11-13\"");
            StringAssert.Contains(nuevo, "\"Origen\":\"PlazoGenerico\"");
            StringAssert.Contains(RastroFechaRecalculada.Anterior(linea), "\"FechaRecepcion\":\"2026-11-13\"");
        }

        [TestMethod]
        public void Interruptor_SinFilaOUno_Encendido_CeroOffApagado_Apagado()
        {
            Assert.IsTrue(InterruptorRecalcularFechasVencidas.Interpretar(null));
            Assert.IsTrue(InterruptorRecalcularFechasVencidas.Interpretar("1"));
            Assert.IsFalse(InterruptorRecalcularFechasVencidas.Interpretar("0"));
            Assert.IsFalse(InterruptorRecalcularFechasVencidas.Interpretar(" Off "));
            Assert.IsFalse(InterruptorRecalcularFechasVencidas.Interpretar("apagado"));
            Assert.IsFalse(InterruptorRecalcularFechasVencidas.Interpretar("false"));
        }

        #endregion

        #region Servicio

        private IRepositorioProveedorVencido repositorio;
        private IRepositorioPlazosProveedores plazos;
        private IRepositorioCorreosSeguimiento correos;
        private List<Exception> registradas;
        private List<string> anotadas;
        private bool encendido;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioProveedorVencido>();
            plazos = A.Fake<IRepositorioPlazosProveedores>();
            correos = A.Fake<IRepositorioCorreosSeguimiento>();
            registradas = new List<Exception>();
            anotadas = new List<string>();
            encendido = true;
            A.CallTo(() => plazos.LeerPlazos(A<string>._)).Returns(new List<PlazoProveedor>
            {
                new PlazoProveedor { Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 100, DiasP80 = 9 }
            });
            A.CallTo(() => plazos.LeerRecepcionesDeProveedores(A<string>._, A<DateTime>._, A<IEnumerable<string>>._)).Returns(new List<RecepcionCompra>());
            A.CallTo(() => repositorio.LeerRecalculosAnteriores(A<DateTime>._)).Returns(new List<string>());
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).Returns(true);
            A.CallTo(() => correos.TablasExisten()).Returns(true);
            A.CallTo(() => correos.AplicacionDisponible()).Returns(true);
            A.CallTo(() => correos.LeerCorreos(A<DateTime>._)).Returns(Task.FromResult(new List<CorreoSeguimientoFila>()));
        }

        private ServicioFechasVencidas Servicio() => new ServicioFechasVencidas(repositorio, plazos, correos, FinDeSemana, () => encendido,
            (empresa, productos, descripcion, causas) => anotadas.Add($"{empresa}|{string.Join(",", productos)}|{descripcion}"), registradas.Add);

        private static IEnumerable<RecepcionCompra> Recepciones(string proveedor, params int[] dias) => dias.Select((d, i) => new RecepcionCompra
        {
            Proveedor = proveedor, Pedido = 210000 + i, FechaPedido = new DateTime(2025, 6, 1), FechaAlbaran = new DateTime(2025, 6, 1).AddDays(d)
        });

        [TestMethod]
        public void Servicio_InterruptorApagado_NiLeeNiEscribe()
        {
            encendido = false;

            Assert.IsNull(Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY));

            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => plazos.LeerRecepcionesDeProveedores(A<string>._, A<DateTime>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Servicio_EscribeConElHistorialDelProveedor_YAnotaLaPromesaEnRiesgoUnaVezPorPedido()
        {
            A.CallTo(() => plazos.LeerRecepcionesDeProveedores("1", HOY.AddMonths(-24), A<IEnumerable<string>>.That.Contains("1480")))
                .Returns(Recepciones("1480", 34, 38, 40, 44, 60).ToList());

            Dictionary<string, ResultadoFechaVencida> r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea(orden: 1), Linea(orden: 2, producto: "45946") }, HOY);

            Assert.IsTrue(r["1/220597/1"].Cambiada);
            Assert.AreEqual(new DateTime(2026, 11, 27), r["1/220597/1"].Recalculo.FechaNueva);
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>.That.Matches(l => l.Orden == 1), new DateTime(2026, 11, 27),
                RastroFechaRecalculada.USUARIO, A<string>.That.Contains("2026-11-13"), A<string>.That.Contains("\"Vencimiento\":1"))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, anotadas.Count);
            Assert.AreEqual("1|45945,45946|pedido a proveedor 220597: fecha vencida recalculada", anotadas[0]);
        }

        [TestMethod]
        public void Servicio_SegundoVencimiento_HayQueLlamar()
        {
            string anterior = RastroFechaRecalculada.Nuevo(Linea(), ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, null, 9, null, null, FinDeSemana), 1);
            A.CallTo(() => repositorio.LeerRecalculosAnteriores(HOY.AddDays(-365))).Returns(new List<string> { anterior });

            Dictionary<string, ResultadoFechaVencida> r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea(), Linea(orden: 2) }, HOY);

            Assert.AreEqual(2, r["1/220597/1"].Vencimiento);
            Assert.IsTrue(r["1/220597/1"].HayQueLlamar);
            Assert.IsFalse(r["1/220597/2"].HayQueLlamar);
        }

        [TestMethod]
        public void Servicio_ConCorreoDeSeguimiento_ManaElCorreo()
        {
            A.CallTo(() => correos.LeerCorreos(HOY.AddDays(-60))).Returns(Task.FromResult(new List<CorreoSeguimientoFila>
            {
                new CorreoSeguimientoFila { Id = 7, MensajeId = "m7", Recibido = HOY.AddDays(-1), Codigo = "Z1", Agencia = "UPS",
                    EntregaEstimadaDia = new DateTime(2026, 11, 18), EmpresaAplicada = "1", PedidoAplicado = 220597 }
            }));

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY)["1/220597/1"];

            Assert.AreEqual(OrigenFechaRecalculada.CorreoSeguimiento, r.Recalculo.Origen);
            Assert.AreEqual(new DateTime(2026, 11, 18), r.Recalculo.FechaNueva);
            Assert.AreEqual("UPS", r.Recalculo.Agencia);
        }

        [TestMethod]
        public void Servicio_SinFase3DeLosCorreos_SinNoticias()
        {
            A.CallTo(() => correos.AplicacionDisponible()).Returns(false);

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY)["1/220597/1"];

            Assert.AreEqual(OrigenFechaRecalculada.PlazoGenerico, r.Recalculo.Origen);
            A.CallTo(() => correos.LeerCorreos(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Servicio_LineaCambiadaMientras_NoSeCuentaComoCambiada_NiSeAnota()
        {
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).Returns(false);

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY)["1/220597/1"];

            Assert.IsFalse(r.Cambiada);
            Assert.AreEqual(TextosFechaVencida.NO_ESCRITA, r.Texto);
            Assert.AreEqual(0, anotadas.Count);
        }

        [TestMethod]
        public void Servicio_SiFallanLasLecturasDeApoyo_SigueYLoApunta_YSiFallaLaEscritura_LaLineaSigueComoSiempre()
        {
            A.CallTo(() => plazos.LeerRecepcionesDeProveedores(A<string>._, A<DateTime>._, A<IEnumerable<string>>._)).Throws(new Exception("timeout"));
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, A<DateTime>._, A<string>._, A<string>._, A<string>._))
                .Throws(new Exception("bloqueo"));

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY)["1/220597/1"];

            Assert.IsFalse(r.Cambiada);
            Assert.AreEqual(TextosFechaVencida.ERROR, r.Texto);
            Assert.AreEqual(2, registradas.Count);
        }

        [TestMethod]
        public void Servicio_SinPlazoGenericoNiCasos_NoSeCambia()
        {
            A.CallTo(() => plazos.LeerPlazos(A<string>._)).Returns(new List<PlazoProveedor>());

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY)["1/220597/1"];

            Assert.IsFalse(r.Cambiada);
            Assert.IsNull(r.Recalculo);
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        #endregion

        #region Correo a Compras y promesa en riesgo

        [TestMethod]
        public void Correo_ConRecalculos_LlevaLaNuevaFechaYQuienHayQueLlamar()
        {
            var lineas = new List<LineaCompraVencida> { Linea(orden: 1), Linea(orden: 2, producto: "45946") };
            RecalculoFechaVencida r = ReglaFechaVencida.Calcular(HOY, ANTERIOR, PEDIDO, null, 9, null, null, FinDeSemana);
            var recalculos = new Dictionary<string, ResultadoFechaVencida>
            {
                ["1/220597/1"] = new ResultadoFechaVencida { Recalculo = r, Cambiada = true, Vencimiento = 2 },
                ["1/220597/2"] = new ResultadoFechaVencida { Recalculo = r, Cambiada = true, Vencimiento = 1 }
            };

            using (MailMessage correo = ProveedoresVencidosJobsService.ConstruirCorreo(lineas, new Dictionary<string, EsperaClientesProducto>(), HOY, recalculos))
            {
                StringAssert.Contains(correo.Subject, "1 línea para llamar al proveedor");
                StringAssert.Contains(correo.Body, ProveedoresVencidosJobsService.INTRODUCCION_RECALCULADA);
                StringAssert.Contains(correo.Body, "<th>Nueva fecha</th>");
                StringAssert.Contains(correo.Body, "<b>25/11/2026</b>");
                string texto = System.Net.WebUtility.HtmlDecode(correo.Body);
                StringAssert.Contains(texto, "Hay que llamar al proveedor: es la 2.ª vez");
                StringAssert.Contains(texto, "Había pasado la fecha (13/11/2026); la hemos cambiado al 25/11/2026");
                Assert.AreEqual(1, correo.Body.Split(new[] { "Hay que llamar al proveedor" }, StringSplitOptions.None).Length - 1);
            }
        }

        [TestMethod]
        public void Correo_SinRecalculos_ElDeSiempre()
        {
            using (MailMessage correo = ProveedoresVencidosJobsService.ConstruirCorreo(new List<LineaCompraVencida> { Linea() },
                new Dictionary<string, EsperaClientesProducto>(), HOY))
            {
                StringAssert.Contains(correo.Body, ProveedoresVencidosJobsService.INTRODUCCION);
                Assert.IsFalse(correo.Body.Contains("Nueva fecha"));
                Assert.IsFalse(correo.Subject.Contains("llamar"));
            }
        }

        [TestMethod]
        public void PromesaRiesgo_AnotarCambioFecha_SoloSiRetrasa()
        {
            var anotado = new List<string>();
            var antes = new[] { new LineaCompraPromesaRiesgo { Id = 1, Producto = "45945", Estado = 1, Enviado = true, Cantidad = 6, FechaRecepcion = ANTERIOR } };

            Assert.IsTrue(CambiosCompraPromesaRiesgo.AnotarCambioFecha("1", 220597, antes, _ => HOY.AddDays(5), "retrasa",
                (e, p, d, c) => anotado.Add(d + ":" + c["45945"].Texto)));
            Assert.IsFalse(CambiosCompraPromesaRiesgo.AnotarCambioFecha("1", 220597, antes, _ => ANTERIOR.AddDays(-1), "adelanta",
                (e, p, d, c) => anotado.Add(d)));

            CollectionAssert.AreEqual(new[] { "retrasa:el pedido a proveedor 220597 retrasa la fecha prevista del 13/11/2026 al 21/11/2026" }, anotado);
        }

        #endregion
    }
}
