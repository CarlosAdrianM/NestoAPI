using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 (encuestas, E1): simulación, comparación de intervalos, sombra y el job diario (con el lector simulado).</summary>
    [TestClass]
    public class ServicioEncuestasTests
    {
        private static readonly DateTime DESDE = new DateTime(2026, 9, 1);
        private static readonly DateTime HASTA = new DateTime(2026, 11, 30);

        private ILectorEncuestas lector;
        private ILectorParametrosUsuario parametros;
        private Dictionary<string, string> valores;
        private List<Exception> errores;

        [TestInitialize]
        public void Preparar()
        {
            ServicioEncuestas.ReiniciarAvisos();
            lector = A.Fake<ILectorEncuestas>();
            parametros = A.Fake<ILectorParametrosUsuario>();
            valores = new Dictionary<string, string>();
            errores = new List<Exception>();
            A.CallTo(() => parametros.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string u, string clave) => valores.TryGetValue(clave, out string v) ? v : null);
            A.CallTo(() => lector.LeerContexto(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new ContextoClientesEncuesta());
        }

        private ServicioEncuestas Servicio(DateTime? ahora = null) =>
            new ServicioEncuestas(() => lector, parametros, () => ahora ?? new DateTime(2026, 11, 30, 10, 0, 0), new MemoryCache("encuestas-" + Guid.NewGuid()),
                ex => errores.Add(ex));

        private static CandidataEncuesta Candidata(string cliente, DateTime dia, TemaEncuesta tema, string canal = CanalesEncuesta.CORREO,
            string vendedor = null) => new CandidataEncuesta
            {
                Empresa = "1",
                Cliente = cliente,
                Contacto = "0",
                Tema = tema,
                Clase = ClaseCandidataEncuesta.Otra,
                Origen = tema.ToString(),
                Vendedor = vendedor,
                Canal = canal,
                Dia = dia,
                FechaEvento = dia.AddDays(-2)
            };

        /// <summary>Un cliente que compra cada semana: una candidata de entrega cada 7 días, y otra de cobro a los 63 días.</summary>
        private static List<CandidataEncuesta> ClienteQueCompraCadaSemana(string cliente)
        {
            var lista = new List<CandidataEncuesta>();
            for (DateTime d = DESDE; d < HASTA; d = d.AddDays(7))
            {
                lista.Add(Candidata(cliente, d, TemaEncuesta.PreparacionEntrega));
            }
            lista.Add(Candidata(cliente, DESDE.AddDays(63), TemaEncuesta.AdministracionCobro));
            return lista;
        }

        private static ContextoClientesEncuesta ConCorreo(params string[] clientes)
        {
            var ctx = new ContextoClientesEncuesta();
            foreach (string c in clientes)
            {
                LectorEncuestasSql.AnadirCorreo(ctx, c, "0", c + "@cliente.es");
            }
            return ctx;
        }

        [TestMethod]
        public void SimularDias_ClienteQueCompraCadaSemana_NoSeLeAtosiga()
        {
            List<DecisionEncuestaCliente> d = ServicioEncuestas.SimularDias(ClienteQueCompraCadaSemana("1000"), ConCorreo("1000"), DESDE, HASTA, 60);

            List<CandidataEncuesta> elegidas = d.Where(x => x.Elegida != null).Select(x => x.Elegida).ToList();
            // 90 días con 60 de intervalo: el primer día (entrega); las entregas de las 8 semanas siguientes, por el tope; a los 63 días,
            // la entrega sería el mismo tema seguido y sale la de cobro.
            Assert.AreEqual(2, elegidas.Count);
            Assert.AreEqual(TemaEncuesta.PreparacionEntrega, elegidas[0].Tema);
            Assert.AreEqual(TemaEncuesta.AdministracionCobro, elegidas[1].Tema);
            Assert.IsTrue((elegidas[1].Dia - elegidas[0].Dia).TotalDays >= 60);
        }

        [TestMethod]
        public void SimularDias_HistorialEnMemoria_CadaIntervaloDaSuVolumen()
        {
            var candidatas = new List<CandidataEncuesta>();
            for (DateTime dia = DESDE; dia < HASTA; dia = dia.AddDays(7))
            {
                candidatas.Add(Candidata("1000", dia, TemaEncuesta.PreparacionEntrega));
                candidatas.Add(Candidata("1000", dia, TemaEncuesta.InformacionTecnica));
                candidatas.Add(Candidata("1000", dia, TemaEncuesta.Vendedor, vendedor: "PA"));
            }
            ContextoClientesEncuesta ctx = ConCorreo("1000");

            int con30 = ServicioEncuestas.SimularDias(candidatas, ctx, DESDE, HASTA, 30).Count(x => x.Elegida != null);
            int con60 = ServicioEncuestas.SimularDias(candidatas, ctx, DESDE, HASTA, 60).Count(x => x.Elegida != null);

            Assert.AreEqual(3, con30);
            Assert.AreEqual(2, con60);
        }

        [TestMethod]
        public void Comparar_LeeLasCandidatasUnaSolaVezYAplicaElGobernadorPorIntervalo()
        {
            List<CandidataEncuesta> candidatas = ClienteQueCompraCadaSemana("1000").Concat(ClienteQueCompraCadaSemana("2000")).ToList();
            A.CallTo(() => lector.LeerCandidatas("1", DESDE, HASTA, A<ParametrosFuentesEncuesta>._))
                .Returns(new LecturaCandidatasEncuesta { Candidatas = candidatas });
            A.CallTo(() => lector.LeerContexto(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(ConCorreo("1000", "2000"));

            ComparacionSimulacionEncuestasDTO r = Servicio().Comparar("1", DESDE, HASTA, new List<int> { 60, 30, 45 });

            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._)).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { 30, 45, 60 }, r.Comparacion.Select(c => c.DiasEntre).ToArray());
            Assert.AreEqual(3, r.Simulaciones.Count);
            Assert.IsTrue(r.Comparacion[0].Encuestas >= r.Comparacion[2].Encuestas);
            Assert.AreEqual(2, r.Comparacion[2].ClientesEncuestados);
            Assert.AreEqual(2, r.Comparacion[2].ClientesCon2);
            Assert.AreEqual(2, r.Comparacion[2].MaximoEncuestasMismoCliente);
            Assert.IsTrue(r.Comparacion[2].PerdidasPorTope > 0);
        }

        [TestMethod]
        public void Simular_SinDiasEntre_ElDelParametro_YSiEstaPorDebajoDelSueloLoDice()
        {
            valores[ParametrosEncuestas.DIAS_ENTRE] = "20";
            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._))
                .Returns(new LecturaCandidatasEncuesta());

            SimulacionEncuestasDTO r = Servicio().Simular("1", DESDE, HASTA, null);

            Assert.AreEqual(30, r.DiasEntre);
            StringAssert.Contains(r.Aviso, "mínimo de 30");
        }

        [TestMethod]
        public void Simular_SinParametro_60()
        {
            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._))
                .Returns(new LecturaCandidatasEncuesta());

            Assert.AreEqual(60, Servicio().Simular("1", DESDE, HASTA, null).DiasEntre);
        }

        [TestMethod]
        public void Simular_QuitaLosClientesEspeciales_YAvisaDeLasFuentesSinLeer()
        {
            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._))
                .Returns(new LecturaCandidatasEncuesta
                {
                    Candidatas = new List<CandidataEncuesta> { Candidata("15191", DESDE, TemaEncuesta.PreparacionEntrega), Candidata("1000", DESDE, TemaEncuesta.PreparacionEntrega) },
                    FuentesSinLeer = new List<string> { "ProblemaReenvioEntregado (falta dbo.EnviosAgenciaReenvios)" }
                });
            A.CallTo(() => lector.LeerContexto(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(ConCorreo("1000"));

            SimulacionEncuestasDTO r = Servicio().Simular("1", DESDE, HASTA, 60);

            Assert.AreEqual(1, r.Candidatas);
            Assert.AreEqual(1, r.Encuestas);
            StringAssert.Contains(r.Aviso, "EnviosAgenciaReenvios");
        }

        [TestMethod]
        public void Simular_LosParametrosDeLasFuentesSeLeenDeDefecto()
        {
            valores[ParametrosEncuestas.DIAS_TRAS_APARATO] = "21";
            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._))
                .Returns(new LecturaCandidatasEncuesta());

            _ = Servicio().Simular("1", DESDE, HASTA, 60);

            A.CallTo(() => lector.LeerCandidatas("1", DESDE, HASTA, A<ParametrosFuentesEncuesta>.That.Matches(p => p.DiasTrasAparato == 21 && p.DiasTrasFormacion == 7)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "(defecto)", ParametrosEncuestas.DIAS_TRAS_APARATO)).MustHaveHappened();
        }

        [TestMethod]
        public void Resumir_ClientesPorNumeroDeEncuestasYTopDelTope()
        {
            var encuestas = new List<CandidataEncuesta>
            {
                Candidata("1", DESDE, TemaEncuesta.Vendedor, CanalesEncuesta.PUSH),
                Candidata("2", DESDE, TemaEncuesta.Vendedor),
                Candidata("2", DESDE.AddDays(40), TemaEncuesta.PreparacionEntrega),
                Candidata("3", DESDE, TemaEncuesta.Vendedor),
                Candidata("3", DESDE.AddDays(40), TemaEncuesta.PreparacionEntrega),
                Candidata("3", DESDE.AddDays(80), TemaEncuesta.AdministracionCobro)
            };
            var descartes = new List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>>
            {
                new KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>(Candidata("3", DESDE.AddDays(7), TemaEncuesta.PreparacionEntrega), MotivoDescarteEncuesta.TopeEntreEncuestas),
                new KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>(Candidata("3", DESDE.AddDays(14), TemaEncuesta.PreparacionEntrega), MotivoDescarteEncuesta.TopeEntreEncuestas),
                new KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>(Candidata("4", DESDE, TemaEncuesta.PreparacionEntrega), MotivoDescarteEncuesta.Baja)
            };
            List<CandidataEncuesta> candidatas = encuestas.Concat(descartes.Select(d => d.Key)).ToList();

            SimulacionEncuestasDTO r = ServicioEncuestas.Resumir("1", DESDE, DESDE.AddDays(90), 40, candidatas, encuestas, descartes, null, null, DESDE);

            Assert.AreEqual(6, r.Encuestas);
            Assert.AreEqual(9, r.Candidatas);
            Assert.AreEqual(3, r.ClientesEncuestados);
            Assert.AreEqual(4, r.ClientesConCandidatas);
            Assert.AreEqual(1, r.ClientesPorNumeroDeEncuestas.Single(c => c.Encuestas == "1").Clientes);
            Assert.AreEqual(1, r.ClientesPorNumeroDeEncuestas.Single(c => c.Encuestas == "2").Clientes);
            Assert.AreEqual(1, r.ClientesPorNumeroDeEncuestas.Single(c => c.Encuestas == "3 o más").Clientes);
            Assert.AreEqual(3, r.MaximoEncuestasMismoCliente);
            Assert.AreEqual(3, r.MaximoPorDia);
            Assert.AreEqual(DESDE, r.DiaDelMaximo);
            Assert.AreEqual(2, r.PerdidasPorTope);
            Assert.AreEqual(1, r.Descartes.Single(d => d.Motivo == "Baja").Candidatas);
            Assert.AreEqual("3", r.MasDescartadosPorTope.Single().Cliente);
            Assert.AreEqual(2, r.MasDescartadosPorTope.Single().DescartadasPorTope);
            Assert.AreEqual(3, r.MasDescartadosPorTope.Single().Encuestas);
            Assert.AreEqual(1, r.PorCanal.Single(c => c.Canal == CanalesEncuesta.PUSH).Encuestas);
            Assert.AreEqual(90, r.PorDia.Count);
            Assert.AreEqual(0.1m, r.MediaPorDia);
        }

        [TestMethod]
        public void Sombra_SinTablas_Lanza()
        {
            A.CallTo(() => lector.ExistenTablas()).Returns(false);

            _ = Assert.ThrowsException<EncuestasSinTablasException>(() => Servicio().Sombra("1", 30));
        }

        [TestMethod]
        public void Sombra_ResumeLoGuardado()
        {
            A.CallTo(() => lector.ExistenTablas()).Returns(true);
            A.CallTo(() => lector.LeerSombra("1", new DateTime(2026, 10, 31), new DateTime(2026, 11, 30))).Returns(new LecturaSombraEncuestas
            {
                Encuestas = new List<CandidataEncuesta> { Candidata("1000", new DateTime(2026, 11, 20), TemaEncuesta.Vendedor) },
                CandidatasPorDia = new Dictionary<DateTime, int> { [new DateTime(2026, 11, 20)] = 5 },
                UltimoIntervalo = 45
            });

            SimulacionEncuestasDTO r = Servicio().Sombra("1", 30);

            Assert.AreEqual(1, r.Encuestas);
            Assert.AreEqual(45, r.DiasEntre);
            Assert.AreEqual(5, r.PorDia.Single(p => p.Dia == new DateTime(2026, 11, 20)).Candidatas);
            StringAssert.Contains(r.Aviso, "apagado");
        }

        // ------------------------------------------------------------ job diario

        private static readonly DateTime AYER = new DateTime(2026, 11, 26);

        [TestMethod]
        public void ProcesarDia_Apagado_NoHaceNada()
        {
            Assert.IsNull(Servicio().ProcesarDia("1", AYER));

            A.CallTo(() => lector.ExistenTablas()).MustNotHaveHappened();
            A.CallTo(() => lector.GuardarDia(A<string>._, A<DateTime>._, A<int>._, A<int>._, A<IReadOnlyList<DecisionEncuestaCliente>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ProcesarDia_ConUnValorQueNoEsUno_SigueApagado()
        {
            valores[ParametrosEncuestas.SOMBRA] = "true";

            Assert.IsNull(Servicio().ProcesarDia("1", AYER));
            A.CallTo(() => lector.ExistenTablas()).MustNotHaveHappened();
        }

        [TestMethod]
        public void ProcesarDia_SinTablas_ElmahUnaSolaVez()
        {
            valores[ParametrosEncuestas.SOMBRA] = "1";
            A.CallTo(() => lector.ExistenTablas()).Returns(false);
            ServicioEncuestas servicio = Servicio();

            _ = servicio.ProcesarDia("1", AYER);
            _ = servicio.ProcesarDia("1", AYER);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "Issue914e_CalidadEncuestas.sql");
        }

        [TestMethod]
        public void ProcesarDia_YaProcesado_NoRepite()
        {
            valores[ParametrosEncuestas.SOMBRA] = "1";
            A.CallTo(() => lector.ExistenTablas()).Returns(true);
            A.CallTo(() => lector.DiaProcesado("1", AYER)).Returns(true);

            Assert.IsNull(Servicio().ProcesarDia("1", AYER));
            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ProcesarDia_CuentaComoHistorialLasDeSombra_YGuardaLasDecididas()
        {
            valores[ParametrosEncuestas.SOMBRA] = "1";
            A.CallTo(() => lector.ExistenTablas()).Returns(true);
            A.CallTo(() => lector.LeerCandidatas("1", AYER, AYER.AddDays(1), A<ParametrosFuentesEncuesta>._)).Returns(new LecturaCandidatasEncuesta
            {
                Candidatas = new List<CandidataEncuesta> { Candidata("1000", AYER, TemaEncuesta.PreparacionEntrega), Candidata("2000", AYER, TemaEncuesta.PreparacionEntrega) }
            });
            A.CallTo(() => lector.LeerContexto(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(ConCorreo("1000", "2000"));
            A.CallTo(() => lector.LeerHistorial("1", A<DateTime>._, AYER)).Returns(new List<EncuestaHistorial>
            {
                new EncuestaHistorial { Cliente = "1000", Dia = AYER.AddDays(-10), Tema = TemaEncuesta.Vendedor, ClaveTema = "Vendedor|PA" }
            });
            IReadOnlyList<DecisionEncuestaCliente> guardadas = null;
            A.CallTo(() => lector.GuardarDia("1", AYER, 2, 60, A<IReadOnlyList<DecisionEncuestaCliente>>._))
                .Invokes((string e, DateTime d, int c, int i, IReadOnlyList<DecisionEncuestaCliente> decisiones) => guardadas = decisiones);

            int? encuestas = Servicio().ProcesarDia("1", AYER);

            Assert.AreEqual(1, encuestas);
            Assert.AreEqual("2000", guardadas.Single(d => d.Elegida != null).Elegida.Cliente);
            Assert.AreEqual(MotivoDescarteEncuesta.TopeEntreEncuestas, guardadas.Single(d => d.Elegida == null).Descartes.Single().Value);
        }

        [TestMethod]
        public void ProcesarDia_SiFallaLaLectura_NoLanzaYVaAElmah()
        {
            valores[ParametrosEncuestas.SOMBRA] = "1";
            A.CallTo(() => lector.ExistenTablas()).Returns(true);
            A.CallTo(() => lector.LeerCandidatas(A<string>._, A<DateTime>._, A<DateTime>._, A<ParametrosFuentesEncuesta>._)).Throws(new InvalidOperationException("caída"));

            Assert.IsNull(Servicio().ProcesarDia("1", AYER));
            Assert.AreEqual(1, errores.Count);
        }
    }
}
