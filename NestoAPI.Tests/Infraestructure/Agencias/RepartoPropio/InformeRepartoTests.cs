using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure.Agencias.RepartoPropio
{
    // NestoAPI#881 (corte 4): el informe de reparto por repartidor y día.
    [TestClass]
    public class InformeRepartoTests
    {
        private static readonly DateTime JUEVES = new DateTime(2026, 11, 19);
        private static readonly DateTime VIERNES = new DateTime(2026, 11, 20);

        private static TiempoParadaReparto Parada(DateTime dia, int hora, int minuto, string tipo = TiposParadaReparto.ENVIO,
            string resultado = TiempoParadaReparto.RESULTADO_ENTREGADA, string repartidor = "Miguel", int bultos = 2, string forma = null,
            decimal? importe = null, int? minutosLlegada = 10, decimal previstos = 15, string cp = "28100")
        {
            DateTime fin = dia.AddHours(hora).AddMinutes(minuto);
            return new TiempoParadaReparto
            {
                Repartidor = repartidor,
                Dia = dia,
                TipoParada = tipo,
                Resultado = resultado,
                Bultos = bultos,
                FormaCobro = forma,
                ImporteCobro = importe,
                FechaHoraFin = fin,
                FechaHoraLlegada = minutosLlegada.HasValue ? fin.AddMinutes(-minutosLlegada.Value) : (DateTime?)null,
                FechaHoraAnterior = fin.AddMinutes(-30),
                MinutosPrevistos = previstos,
                CodigoPostal = cp
            };
        }

        private static DatosInformeReparto Datos()
        {
            return new DatosInformeReparto
            {
                Tiempos = new List<TiempoParadaReparto>
                {
                    Parada(JUEVES, 9, 30, forma: PruebaEntregaReparto.COBRO_EFECTIVO, importe: 50),
                    Parada(JUEVES, 10, 30, tipo: TiposParadaReparto.NOTA_COBRO, bultos: 0, forma: PruebaEntregaReparto.COBRO_ENLACE, importe: 30),
                    Parada(JUEVES, 11, 0, resultado: TiempoParadaReparto.RESULTADO_INCIDENCIA, minutosLlegada: 5),
                    Parada(JUEVES, 12, 0, tipo: TiposParadaReparto.NOTA_RECOGIDA, bultos: 3, minutosLlegada: 200),
                    Parada(JUEVES, 13, 0, forma: PruebaEntregaReparto.COBRO_TARJETA, importe: 20, cp: "28001"),
                    Parada(JUEVES, 13, 30, forma: PruebaEntregaReparto.COBRO_NO_COBRADO, importe: 40),
                    Parada(VIERNES, 10, 0, repartidor: "Pedro")
                },
                Movimientos = new List<MovimientosDiaReparto>
                {
                    new MovimientosDiaReparto { Usuario = "NUEVAVISION\\Miguel", Dia = JUEVES, PrimeraCarga = JUEVES.AddHours(8), PrimerMovimiento = JUEVES.AddHours(7.5),
                        UltimoMovimiento = JUEVES.AddHours(15), BultosCargados = 9 }
                },
                Jornadas = new List<JornadaTerminadaReparto>
                {
                    new JornadaTerminadaReparto { Usuario = "NUEVAVISION\\Miguel", Dia = JUEVES, FechaFin = JUEVES.AddHours(16) }
                },
                Estimaciones = new List<PrimeraEstimacionReparto>
                {
                    new PrimeraEstimacionReparto { Repartidor = "Miguel", Dia = JUEVES, FinEstimado = JUEVES.AddHours(15.5), HoraCierre = JUEVES.AddHours(17), PasaDelCierre = false }
                }
            };
        }

        private static InformeRepartoDTO Calcular(DatosInformeReparto datos = null, string solo = null)
            => CalculoInformeReparto.Calcular(JUEVES, VIERNES, datos ?? Datos(), solo, VIERNES.AddHours(18));

        [TestMethod]
        public void Calcular_UnaJornada_ParadasIncidenciasYBultos()
        {
            JornadaInformeRepartoDTO j = Calcular().Repartidores.Single(r => r.Repartidor == "Miguel").Dias.Single();
            Assert.AreEqual(5, j.Paradas, "entregadas y recogidas, sin la incidencia");
            Assert.AreEqual(4, j.Entregas);
            Assert.AreEqual(1, j.Recogidas);
            Assert.AreEqual(1, j.Incidencias);
            Assert.AreEqual(6, j.BultosEntregados);
            Assert.AreEqual(3, j.BultosRecogidos);
            Assert.AreEqual(9, j.BultosCargados);
            Assert.AreEqual(1, j.ParadasPorTipo[TiposParadaReparto.NOTA_COBRO]);
            Assert.AreEqual(3, j.ParadasPorTipo[TiposParadaReparto.ENVIO]);
            Assert.AreEqual(4, j.DiaSemana);
            Assert.AreEqual("jueves", j.NombreDiaSemana);
        }

        [TestMethod]
        public void Calcular_Dinero_EfectivoTarjetaYOtrosCobros()
        {
            JornadaInformeRepartoDTO j = Calcular().Repartidores.Single(r => r.Repartidor == "Miguel").Dias.Single();
            Assert.AreEqual(50m, j.CobradoEfectivo);
            Assert.AreEqual(20m, j.CobradoTarjeta);
            Assert.AreEqual(30m, j.OtrosCobros, "la nota de cobro va en otros cobros aunque sea con enlace");
            Assert.AreEqual(100m, j.TotalCobrado);
            Assert.AreEqual(1, j.SinCobrar);
        }

        [TestMethod]
        public void Calcular_Horas_DeLaPrimeraCargaAlUltimoMovimiento_YEnRuta()
        {
            JornadaInformeRepartoDTO j = Calcular().Repartidores.Single(r => r.Repartidor == "Miguel").Dias.Single();
            Assert.AreEqual(JUEVES.AddHours(8), j.Inicio, "la primera carga, no el primer movimiento");
            Assert.AreEqual(JUEVES.AddHours(16), j.Fin, "«Terminar jornada» es el último");
            Assert.AreEqual(8m, j.Horas);
            Assert.AreEqual(4m, j.HorasEnRuta, "de 9:30 a 13:30");
            Assert.AreEqual(30, j.MinutosSobrePrevisto);
        }

        [TestMethod]
        public void Calcular_MinutosDeParada_RealFrenteAPrevisto_SinLosDisparatados()
        {
            JornadaInformeRepartoDTO j = Calcular().Repartidores.Single(r => r.Repartidor == "Miguel").Dias.Single();
            Assert.AreEqual(5, j.ParadasConLlegada, "la de 200 minutos no cuenta");
            Assert.AreEqual(9m, j.MinutosParadaMedioReal);
            Assert.AreEqual(15m, j.MinutosParadaMedioPrevisto);
            Assert.AreEqual(30m, j.MinutosEntreParadasMedio);
            Assert.IsNull(j.Km, "corte 5");
        }

        [TestMethod]
        public void Calcular_TotalesYMediasPorDiaSemana()
        {
            InformeRepartoDTO informe = Calcular();
            Assert.AreEqual(2, informe.Totales.Jornadas);
            Assert.AreEqual(6, informe.Totales.Paradas);
            CollectionAssert.AreEqual(new[] { 4, 5 }, informe.PorDiaSemana.Select(d => d.DiaSemana).ToList());
            Assert.AreEqual(5m, informe.PorDiaSemana[0].MediaParadas);
            Assert.AreEqual("viernes", informe.PorDiaSemana[1].NombreDiaSemana);
            CollectionAssert.AreEqual(new[] { "Miguel", "Pedro" }, informe.Repartidores.Select(r => r.Repartidor).ToList());
        }

        [TestMethod]
        public void Calcular_PorCodigoPostal()
        {
            List<CodigoPostalInformeRepartoDTO> cps = Calcular().PorCodigoPostal;
            CodigoPostalInformeRepartoDTO alcobendas = cps.Single(c => c.CodigoPostal == "28100" && c.DiaSemana == 4);
            Assert.AreEqual(4, alcobendas.Paradas);
            Assert.AreEqual(1, alcobendas.Incidencias);
            Assert.AreEqual(1, cps.Single(c => c.CodigoPostal == "28001").Paradas);
        }

        [TestMethod]
        public void Calcular_SoloUnRepartidor_ConOSinDominio()
        {
            InformeRepartoDTO informe = Calcular(solo: "NUEVAVISION\\pedro");
            Assert.AreEqual("Pedro", informe.Repartidores.Single().Repartidor);
            Assert.AreEqual(1, informe.Totales.Paradas);
        }

        [TestMethod]
        public void Calcular_SinTablasOpcionales_InicioEnLaPrimeraParadaYAviso()
        {
            DatosInformeReparto datos = Datos();
            datos.Movimientos = null;
            datos.Jornadas = null;
            datos.Estimaciones = null;
            JornadaInformeRepartoDTO j = Calcular(datos).Repartidores.Single(r => r.Repartidor == "Miguel").Dias.Single();
            Assert.IsNull(j.BultosCargados);
            Assert.AreEqual(JUEVES.AddHours(9.5), j.Inicio);
            Assert.AreEqual(JUEVES.AddHours(13.5), j.Fin);
            Assert.IsNull(j.MinutosSobrePrevisto);
            StringAssert.Contains(CalculoInformeReparto.Aviso(datos), "movimientos");
            Assert.IsNull(CalculoInformeReparto.Aviso(Datos()));
        }

        [TestMethod]
        public void Rango_NoValido()
        {
            Assert.IsNull(CalculoInformeReparto.MotivoRangoNoValido(JUEVES, JUEVES));
            Assert.IsNotNull(CalculoInformeReparto.MotivoRangoNoValido(VIERNES, JUEVES));
            Assert.IsNotNull(CalculoInformeReparto.MotivoRangoNoValido(JUEVES.AddDays(-400), JUEVES));
        }

        // ---------- controlador ----------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static InformeRepartoController Controlador(IRepositorioInformeReparto repo, params string[] grupos) => new InformeRepartoController
        {
            CrearRepositorio = () => repo,
            Ahora = () => VIERNES.AddHours(18),
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration(),
            User = Usuario("NUEVAVISION\\Miguel", grupos)
        };

        [TestMethod]
        public async Task Controlador_SinGrupo403_SinTabla503()
        {
            IRepositorioInformeReparto repo = A.Fake<IRepositorioInformeReparto>();
            A.CallTo(() => repo.TablaTiemposExiste()).Returns(false);
            HttpResponseMessage sin = await (await Controlador(repo, "Administración").GetInforme()).ExecuteAsync(default);
            Assert.AreEqual(HttpStatusCode.Forbidden, sin.StatusCode);
            HttpResponseMessage sinTabla = await (await Controlador(repo, "Almacén").GetInforme()).ExecuteAsync(default);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, sinTabla.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_PorDefectoLaUltimaSemana_YSoloMio()
        {
            IRepositorioInformeReparto repo = A.Fake<IRepositorioInformeReparto>();
            A.CallTo(() => repo.TablaTiemposExiste()).Returns(true);
            A.CallTo(() => repo.Leer(A<DateTime>._, A<DateTime>._)).Returns(Datos());
            var ok = (OkNegotiatedContentResult<InformeRepartoDTO>)await Controlador(repo, "Almacén").GetInforme(soloMio: true);
            A.CallTo(() => repo.Leer(VIERNES.AddDays(-6), VIERNES)).MustHaveHappenedOnceExactly();
            Assert.AreEqual("Miguel", ok.Content.Repartidores.Single().Repartidor);
        }

        [TestMethod]
        public async Task Controlador_RangoAlReves_400()
        {
            IRepositorioInformeReparto repo = A.Fake<IRepositorioInformeReparto>();
            HttpResponseMessage m = await (await Controlador(repo, "Dirección").GetInforme(VIERNES, JUEVES)).ExecuteAsync(default);
            Assert.AreEqual(HttpStatusCode.BadRequest, m.StatusCode);
        }
    }
}
