using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Pedido 930942 (19/11/26): aviso de promesa en riesgo FALSO. Martes 17/11: 43818 × 2, Algete 1, Reina 2, Alcobendas 1; prometido el jueves
    /// 19/11 con 1 ud. de una reposición. Miércoles 18/11 a las 9:37 ALCOBENDAS manda la unidad (reposición 81463: sale del extracto de
    /// Alcobendas y queda la entrada en Algete). Un minuto después el motor repartía lo que faltaba a lo LIBRE de Reina (2) antes que a lo que
    /// ya venía de camino, y avisaba «tiene que venir de Reina… puede salir el lunes 23/11». Ahora lo de camino va primero, con su reposición y
    /// su tienda, y sale el jueves 19/11: no hay riesgo.
    /// </summary>
    [TestClass]
    public class PromesaRiesgoEnCaminoTests
    {
        private static readonly DateTime JUEVES_19 = new DateTime(2026, 11, 19);
        private static readonly DateTime LUNES_23 = new DateTime(2026, 11, 23);
        // Después del cierre de la reposición de Reina del miércoles (en producción salió a las 9:37 sin la 43818)
        private static readonly DateTime MIERCOLES_18 = new DateTime(2026, 11, 18, 10, 30, 0);

        private static bool EsFestivo(DateTime dia, string almacen) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        private static ReposicionCalendario Fila(string origen, byte dia) => new ReposicionCalendario
        {
            Empresa = "1  ",
            AlmacenOrigen = origen,
            AlmacenDestino = "ALG",
            DiaSemana = dia,
            HoraCierre = new TimeSpan(10, 0, 0),
            HoraLlegadaHabitual = new TimeSpan(13, 30, 0),
            Activo = true
        };

        private static List<ReposicionCalendario> Calendario() => new List<ReposicionCalendario>
        {
            Fila("REI", 1), Fila("REI", 3), Fila("REI", 5),
            Fila("ALC", 1), Fila("ALC", 2), Fila("ALC", 4)
        };

        private static void Stock(DatosSombraModoServicio datos, string producto, string almacen, int stock, int pendientes = 0)
        {
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, almacen), stock);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave(producto), stock);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(producto, almacen), pendientes);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(producto), pendientes);
        }

        /// <summary>El 18/11 a las 9:38: Alcobendas ya ha sacado la unidad (extracto 0) y la entrada en Algete de la 81463 está pendiente de recibir.</summary>
        private static DatosSombraModoServicio TrasLaSalidaDeAlcobendas(int pendientesOtrosAlgete = 0)
        {
            var datos = new DatosSombraModoServicio();
            Stock(datos, "43818", "ALG", 1, pendientesOtrosAlgete);
            Stock(datos, "43818", "REI", 2);
            Stock(datos, "43818", "ALC", 0);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteReposicion, ResumenStocksProductos.Clave("43818"), 1);
            datos.EnCaminoAAlgete.Add(new ReposicionEnCaminoAlgete { Producto = "43818", Traspaso = 81463, Tienda = "ALC", Unidades = 1 });
            return datos;
        }

        private static LineaPedidoFechaEntregaAgencia Linea(int cantidad = 2) =>
            new LineaPedidoFechaEntregaAgencia { Producto = "43818", Almacen = "ALG", Cantidad = cantidad, BaseImponible = 20 };

        private static ResultadoFechaEntregaAgencia Calcular(DatosSombraModoServicio datos, int cantidad = 2)
        {
            List<LineaFechaEntregaAgencia> lineas = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea(cantidad) }, datos, new[] { "REI", "ALC" }, MIERCOLES_18.Date);
            return new CalculadoraFechaEntregaAgencia(EsFestivo).Calcular(new EntradaFechaEntregaAgencia
            {
                Lineas = lineas,
                ModoServicio = Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                ModoFacturacion = 1,
                Ruta = "FW",
                Ahora = MIERCOLES_18,
                HoraCorte = new TimeSpan(11, 0, 0),
                Calendario = Calendario()
            });
        }

        private static FechaEntregaAgenciaDTO ComoDTO(ResultadoFechaEntregaAgencia r) => new FechaEntregaAgenciaDTO
        {
            FechaEntregaAgencia = r.FechaQueAplica,
            Motivo = r.Motivo,
            Desglose = r.Desglose
        };

        [TestMethod]
        public void Caso930942_LoQueYaVieneDeCaminoVaAntesQueLoLibreDeOtraTienda()
        {
            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea() }, TrasLaSalidaDeAlcobendas(),
                new[] { "REI", "ALC" }, MIERCOLES_18.Date).Single();

            Assert.AreEqual(1, linea.EnAlgete);
            Assert.AreEqual(1, linea.EnCaminoDeTiendas);
            Assert.AreEqual(0, linea.EnTiendas.Values.Sum(), "No se coge de Reina lo que ya viene de Alcobendas");
            ReposicionEnCaminoAlgete reposicion = linea.EnCaminoReposiciones.Single();
            Assert.AreEqual(81463, reposicion.Traspaso);
            Assert.AreEqual("ALC", reposicion.Tienda);
            Assert.AreEqual(1, reposicion.Unidades);
        }

        [TestMethod]
        public void Caso930942_SaleElJuevesComoSePrometio_YNoHayRiesgo()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular(TrasLaSalidaDeAlcobendas());

            Assert.AreEqual(JUEVES_19, resultado.FechaQueAplica, resultado.Motivo);
            StringAssert.Contains(resultado.Motivo, "ya viene de camino en la reposición 81463 de Alcobendas");
            PartidaFechaEntregaAgenciaDTO enCamino = resultado.Desglose.Single(p => p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO);
            Assert.AreEqual(81463, enCamino.TraspasoEnCamino);
            Assert.AreEqual("ALC", enCamino.TiendaEnCamino);
            Assert.IsNull(enCamino.Tienda, "No es una partida de tienda: no se reserva nada en Alcobendas");

            DecisionPromesaRiesgo decision = EvaluadorPromesaRiesgo.Decidir(JUEVES_19, ComoDTO(resultado), true, null);
            Assert.AreEqual(AccionPromesaRiesgo.Nada, decision.Accion);
        }

        [TestMethod]
        public void SiLaReposicionEsDeOtroPedidoQueYaEsperaba_ElAvisoDiceLaTiendaReal_YLasUnidadesQueFaltan()
        {
            // Algete 1 y otro pedido anterior debe 2 en Algete: la unidad de la 81463 es para él. A este le falta 1 (no 2) y viene de Reina.
            ResultadoFechaEntregaAgencia resultado = Calcular(TrasLaSalidaDeAlcobendas(pendientesOtrosAlgete: 2), cantidad: 1);

            Assert.AreEqual(LUNES_23, resultado.FechaQueAplica, resultado.Motivo);
            string motivo = TextosPromesaRiesgo.Motivo(JUEVES_19, ComoDTO(resultado), new Dictionary<string, string> { ["43818"] = "MULTIVIT GLOW CREMA RICA" },
                MIERCOLES_18.Date);
            StringAssert.StartsWith(motivo, "Falta 43818 MULTIVIT GLOW CREMA RICA (1 ud.): viene de Reina.");
            StringAssert.Contains(motivo, "el lunes 23/11");
        }

        [TestMethod]
        public void LaLineaDeDosConUnaEnAlgete_ElAvisoCuentaSoloLaQueFalta()
        {
            // Sin la reposición (aún no salió de ninguna tienda): falta 1 de 2 y viene de Reina.
            DatosSombraModoServicio datos = TrasLaSalidaDeAlcobendas();
            datos.EnCaminoAAlgete.Clear();
            datos.Resumen.PendienteReposicion.Clear();

            ResultadoFechaEntregaAgencia resultado = Calcular(datos);
            string motivo = TextosPromesaRiesgo.Motivo(JUEVES_19, ComoDTO(resultado), null, MIERCOLES_18.Date);

            StringAssert.StartsWith(motivo, "Falta 43818 (1 ud.):");
            Assert.AreEqual(1, TextosPromesaRiesgo.Faltas(JUEVES_19, ComoDTO(resultado), null).Single().Unidades);
        }

        [TestMethod]
        public void TextoDeCamino_ConYSinDatos()
        {
            Assert.AreEqual("en la reposición 81463 de Alcobendas",
                CalculadoraFechaEntregaAgencia.DeCamino(new PartidaFechaEntregaAgenciaDTO { TraspasoEnCamino = 81463, TiendaEnCamino = "ALC" }));
            Assert.AreEqual("en la reposición 81463", CalculadoraFechaEntregaAgencia.DeCamino(new PartidaFechaEntregaAgenciaDTO { TraspasoEnCamino = 81463 }));
            Assert.AreEqual("desde Reina", CalculadoraFechaEntregaAgencia.DeCamino(new PartidaFechaEntregaAgenciaDTO { TiendaEnCamino = "REI" }));
            Assert.AreEqual("desde una tienda", CalculadoraFechaEntregaAgencia.DeCamino(new PartidaFechaEntregaAgenciaDTO()));
        }

        [TestMethod]
        public void FraseDelAviso_NombraLaReposicionYLaTienda()
        {
            var partida = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO,
                Producto = "43818",
                Unidades = 1,
                PuedeSalirEl = LUNES_23,
                TraspasoEnCamino = 81463,
                TiendaEnCamino = "ALC"
            };

            Assert.AreEqual("viene de camino en la reposición 81463 de Alcobendas", TextosPromesaRiesgo.FraseLarga(partida, false, MIERCOLES_18.Date));
            Assert.AreEqual("viene de camino en la reposición 81463 de Alcobendas (lunes 23/11)", TextosPromesaRiesgo.FraseCorta(partida, false, MIERCOLES_18.Date));
        }

        [TestMethod]
        public void TomarEnCamino_LaDeudaDeOtrosSeLlevaLaReposicionMasAntigua()
        {
            var datos = new DatosSombraModoServicio();
            Stock(datos, "A", "ALG", 0, pendientes: 1);
            datos.EnCaminoAAlgete.Add(new ReposicionEnCaminoAlgete { Producto = "A", Traspaso = 200, Tienda = "REI", Unidades = 1 });
            datos.EnCaminoAAlgete.Add(new ReposicionEnCaminoAlgete { Producto = "A", Traspaso = 100, Tienda = "ALC", Unidades = 1 });
            int paraTiendas = 2;

            List<ReposicionEnCaminoAlgete> tomadas = RepartidorStockFechaEntregaAgencia.TomarEnCaminoAAlgete(datos, "A", 0, ref paraTiendas);

            Assert.AreEqual(200, tomadas.Single().Traspaso, "La 100 (la más antigua) es del pedido que ya esperaba");
            Assert.AreEqual(1, paraTiendas);
            Assert.AreEqual(1, datos.EnCaminoAAlgete.Single(r => r.Traspaso == 200).Unidades, "No toca los datos");
        }

        // ------------------------------------------------------------- correo: nunca lo guardado sin recalcular

        [TestMethod]
        public void Correo_SoloLoRevisadoOLoDetectadoDespuesDeEmpezar()
        {
            DateTime inicio = new DateTime(2026, 11, 18, 15, 0, 0);
            var revisado = new RiesgoPromesa { Pedido = 1, Detectado = inicio.AddHours(-5), Actualizado = inicio.AddHours(-5) };
            var sinRevisar = new RiesgoPromesa { Pedido = 930942, Detectado = inicio.AddHours(-5), Actualizado = inicio.AddHours(-5) };
            var nuevo = new RiesgoPromesa { Pedido = 3, Detectado = inicio.AddSeconds(10), Actualizado = inicio.AddSeconds(10) };
            var revisados = new HashSet<int> { 1 };

            Assert.IsTrue(ReglaCorreoPromesaRiesgo.PuedeIr(revisado, revisados, inicio));
            Assert.IsFalse(ReglaCorreoPromesaRiesgo.PuedeIr(sinRevisar, revisados, inicio));
            Assert.IsTrue(ReglaCorreoPromesaRiesgo.PuedeIr(nuevo, new HashSet<int>(), inicio));
            Assert.IsFalse(ReglaCorreoPromesaRiesgo.PuedeIr(null, revisados, inicio));
        }

        [TestMethod]
        public async Task Correo_LoQueNoSeHaPodidoRecalcular_NoSaleNiSeMarca()
        {
            DateTime inicio = new DateTime(2026, 11, 18, 15, 0, 0);
            var repositorio = A.Fake<IRepositorioPromesaRiesgo>();
            var avisador = A.Fake<IAvisadorPromesaRiesgo>();
            var revisado = new RiesgoPromesa { Id = 10, Pedido = 1, Detectado = inicio.AddHours(-5), Actualizado = inicio.AddHours(-5) };
            var sinRevisar = new RiesgoPromesa { Id = 11, Pedido = 930942, Detectado = inicio.AddHours(-5), Actualizado = inicio.AddHours(-5) };
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerSinCorreo("1")).Returns(Task.FromResult(new List<RiesgoPromesa> { revisado, sinRevisar }));
            A.CallTo(() => repositorio.LeerYaSalenSinCorreo(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new List<RiesgoPromesa>()));
            A.CallTo(() => avisador.Preparar(A<RiesgoPromesa>._, A<bool>._)).ReturnsLazily((RiesgoPromesa r, bool e) => new AvisoPromesaRiesgo { Riesgo = r });
            IEnumerable<int> marcados = null;
            A.CallTo(() => repositorio.MarcarAvisadoCorreo(A<IEnumerable<int>>._, A<DateTime>._))
                .Invokes((IEnumerable<int> ids, DateTime _) => marcados = ids.ToList()).Returns(Task.CompletedTask);

            int enviados = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => inicio.AddMinutes(1), () => ModoAvisoPromesaRiesgo.Activo, "1",
                r => ReglaCorreoPromesaRiesgo.PuedeIr(r, new HashSet<int> { 1 }, inicio));

            Assert.AreEqual(1, enviados);
            CollectionAssert.AreEqual(new[] { 10 }, marcados.ToList());
            A.CallTo(() => avisador.Preparar(sinRevisar, A<bool>._)).MustNotHaveHappened();
        }
    }
}
