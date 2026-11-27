using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 (encuestas, E1): el gobernador (puro) y el intervalo entre encuestas.</summary>
    [TestClass]
    public class GobernadorEncuestasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 27);

        private static CandidataEncuesta Candidata(TemaEncuesta tema, ClaseCandidataEncuesta clase = ClaseCandidataEncuesta.Otra, string vendedor = null,
            string canal = CanalesEncuesta.CORREO, string cliente = "1000", int? pedido = null, DateTime? evento = null) => new CandidataEncuesta
            {
                Empresa = "1",
                Cliente = cliente,
                Contacto = "0",
                Tema = tema,
                Clase = clase,
                Vendedor = vendedor,
                Canal = canal,
                Origen = tema.ToString(),
                Pedido = pedido,
                Dia = HOY,
                FechaEvento = evento ?? HOY.AddDays(-2)
            };

        private static EncuestaHistorial Historial(int haceDias, TemaEncuesta tema, string vendedor = null, bool ignorada = false) => new EncuestaHistorial
        {
            Cliente = "1000",
            Dia = HOY.AddDays(-haceDias),
            Tema = tema,
            ClaveTema = CandidataEncuesta.ClaveDeTema(tema, vendedor),
            Ignorada = ignorada
        };

        private static DecisionEncuestaCliente Decidir(IEnumerable<EncuestaHistorial> historial, int diasEntre = 60,
            SituacionClienteEncuesta situacion = null, params CandidataEncuesta[] candidatas)
            => GobernadorEncuestas.Decidir(HOY, (historial ?? Enumerable.Empty<EncuestaHistorial>()).ToList(), situacion ?? new SituacionClienteEncuesta(),
                candidatas, diasEntre);

        private static MotivoDescarteEncuesta MotivoDe(DecisionEncuestaCliente d) => d.Descartes.Single().Value;

        [TestMethod]
        public void SinHistorial_EligeLaCandidata()
        {
            DecisionEncuestaCliente d = Decidir(null, 60, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.IsNotNull(d.Elegida);
            Assert.AreEqual(0, d.Descartes.Count);
            Assert.AreEqual(60, d.IntervaloDias);
        }

        [TestMethod]
        public void UnaEncuestaHace59Dias_TopeEntreEncuestas()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(59, TemaEncuesta.Vendedor, "PA") }, 60, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.IsNull(d.Elegida);
            Assert.AreEqual(MotivoDescarteEncuesta.TopeEntreEncuestas, MotivoDe(d));
        }

        [TestMethod]
        public void UnaEncuestaHace60Dias_YaSePuede()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(60, TemaEncuesta.Vendedor, "PA") }, 60, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.IsNotNull(d.Elegida);
        }

        [TestMethod]
        public void Suelo_EsTreinta()
        {
            Assert.AreEqual(30, ParametrosEncuestas.DIAS_ENTRE_MINIMO);
            Assert.AreEqual(30, GobernadorEncuestas.ConSuelo(10));
            Assert.AreEqual(45, GobernadorEncuestas.ConSuelo(45));
        }

        [TestMethod]
        public void IntervaloPorDebajoDelSuelo_SeAplicaTreinta()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(29, TemaEncuesta.Vendedor, "PA") }, 10, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.AreEqual(30, d.IntervaloDias);
            Assert.AreEqual(MotivoDescarteEncuesta.TopeEntreEncuestas, MotivoDe(d));
        }

        [DataTestMethod]
        [DataRow(null, 60, false)]
        [DataRow("", 60, false)]
        [DataRow("45", 45, false)]
        [DataRow("30", 30, false)]
        [DataRow("29", 30, true)]
        [DataRow("10", 30, true)]
        [DataRow("181", 180, true)]
        [DataRow("mucho", 60, true)]
        public void DiasEntreDesdeParametro_SueloYTecho(string valor, int esperado, bool conAviso)
        {
            int dias = ParametrosEncuestas.DiasEntreDesdeParametro(valor, out string aviso);

            Assert.AreEqual(esperado, dias);
            Assert.AreEqual(conAviso, aviso != null);
        }

        [TestMethod]
        public void ParametroPorDebajoDelSuelo_ElAvisoLoDice()
        {
            _ = ParametrosEncuestas.DiasEntreDesdeParametro("15", out string aviso);

            StringAssert.Contains(aviso, "mínimo de 30");
        }

        [TestMethod]
        public void Intervalo_UnaIgnorada_NoDuplica()
        {
            Assert.AreEqual(60, GobernadorEncuestas.Intervalo(new[] { Historial(70, TemaEncuesta.Vendedor, ignorada: true) }, 60));
        }

        [TestMethod]
        public void Intervalo_DosUltimasIgnoradas_Duplica()
        {
            var historial = new[] { Historial(70, TemaEncuesta.Vendedor, ignorada: true), Historial(140, TemaEncuesta.PreparacionEntrega, ignorada: true) };

            Assert.AreEqual(120, GobernadorEncuestas.Intervalo(historial, 60));
        }

        [TestMethod]
        public void Intervalo_TresIgnoradas_VuelveADuplicar()
        {
            var historial = new[]
            {
                Historial(70, TemaEncuesta.Vendedor, ignorada: true), Historial(140, TemaEncuesta.PreparacionEntrega, ignorada: true),
                Historial(210, TemaEncuesta.AdministracionCobro, ignorada: true)
            };

            Assert.AreEqual(240, GobernadorEncuestas.Intervalo(historial, 60));
        }

        [TestMethod]
        public void Intervalo_MuchasIgnoradas_ConTopeDeUnAno()
        {
            List<EncuestaHistorial> historial = Enumerable.Range(1, 8).Select(i => Historial(i * 70, TemaEncuesta.Vendedor, ignorada: true)).ToList();

            Assert.AreEqual(GobernadorEncuestas.INTERVALO_MAXIMO_DIAS, GobernadorEncuestas.Intervalo(historial, 60));
        }

        [TestMethod]
        public void Intervalo_LaUltimaContestada_NoDuplicaAunqueAntesIgnorase()
        {
            var historial = new[]
            {
                Historial(70, TemaEncuesta.Vendedor, ignorada: false), Historial(140, TemaEncuesta.PreparacionEntrega, ignorada: true),
                Historial(210, TemaEncuesta.AdministracionCobro, ignorada: true)
            };

            Assert.AreEqual(60, GobernadorEncuestas.Intervalo(historial, 60));
        }

        [TestMethod]
        public void Intervalo_DuplicaElQueToque_ConTreintaSon60()
        {
            var historial = new[] { Historial(40, TemaEncuesta.Vendedor, ignorada: true), Historial(80, TemaEncuesta.PreparacionEntrega, ignorada: true) };

            Assert.AreEqual(60, GobernadorEncuestas.Intervalo(historial, 30));
            DecisionEncuestaCliente d = Decidir(historial, 30, null, Candidata(TemaEncuesta.AdministracionCobro));
            Assert.AreEqual(MotivoDescarteEncuesta.TopeEntreEncuestas, MotivoDe(d));
        }

        [TestMethod]
        public void MismoTemaSeguido_TemaRepetido()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(400, TemaEncuesta.PreparacionEntrega) }, 60, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.AreEqual(MotivoDescarteEncuesta.TemaRepetido, MotivoDe(d));
        }

        [TestMethod]
        public void MismoTemaHaceMenosDe12Meses_TemaRepetido()
        {
            var historial = new[] { Historial(100, TemaEncuesta.AdministracionCobro), Historial(300, TemaEncuesta.PreparacionEntrega) };

            DecisionEncuestaCliente d = Decidir(historial, 60, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.AreEqual(MotivoDescarteEncuesta.TemaRepetido, MotivoDe(d));
        }

        [TestMethod]
        public void MismoTemaHaceMasDe12Meses_SiNoEsLaUltima_SeVuelveAPreguntar()
        {
            var historial = new[] { Historial(100, TemaEncuesta.AdministracionCobro), Historial(400, TemaEncuesta.PreparacionEntrega) };

            DecisionEncuestaCliente d = Decidir(historial, 60, null, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.IsNotNull(d.Elegida);
        }

        [TestMethod]
        public void Vendedor_MismoVendedor_TemaRepetido()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(90, TemaEncuesta.Vendedor, "PA") }, 60, null,
                Candidata(TemaEncuesta.Vendedor, ClaseCandidataEncuesta.PrimeraVez, "PA"));

            Assert.AreEqual(MotivoDescarteEncuesta.TemaRepetido, MotivoDe(d));
        }

        [TestMethod]
        public void Vendedor_OtroVendedor_SeLePreguntaPorElNuevo()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(90, TemaEncuesta.Vendedor, "PA") }, 60, null,
                Candidata(TemaEncuesta.Vendedor, ClaseCandidataEncuesta.Cambio, "MJ"));

            Assert.IsNotNull(d.Elegida);
            Assert.AreEqual("MJ", d.Elegida.Vendedor);
        }

        [TestMethod]
        public void Vendedor_OtroVendedor_ElTopeGeneralSigueMandando()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(20, TemaEncuesta.Vendedor, "PA") }, 60, null,
                Candidata(TemaEncuesta.Vendedor, ClaseCandidataEncuesta.Cambio, "MJ"));

            Assert.AreEqual(MotivoDescarteEncuesta.TopeEntreEncuestas, MotivoDe(d));
        }

        [TestMethod]
        public void Baja_FueraAntesQueNada()
        {
            DecisionEncuestaCliente d = Decidir(new[] { Historial(10, TemaEncuesta.Vendedor) }, 60, new SituacionClienteEncuesta { DeBaja = true, ImpagoEnGestion = true },
                Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.AreEqual(MotivoDescarteEncuesta.Baja, MotivoDe(d));
        }

        [TestMethod]
        public void Impago_Fuera()
        {
            DecisionEncuestaCliente d = Decidir(null, 60, new SituacionClienteEncuesta { ImpagoEnGestion = true }, Candidata(TemaEncuesta.PreparacionEntrega));

            Assert.AreEqual(MotivoDescarteEncuesta.Impago, MotivoDe(d));
        }

        [TestMethod]
        public void SinCanal_Fuera()
        {
            DecisionEncuestaCliente d = Decidir(null, 60, null, Candidata(TemaEncuesta.PreparacionEntrega, canal: CanalesEncuesta.SIN_CANAL));

            Assert.AreEqual(MotivoDescarteEncuesta.SinCanal, MotivoDe(d));
        }

        [TestMethod]
        public void Prioridad_ProblemaResuelto_Cambio_PrimeraVez_Otra()
        {
            DecisionEncuestaCliente d = Decidir(null, 60, null,
                Candidata(TemaEncuesta.PreparacionEntrega, ClaseCandidataEncuesta.Otra),
                Candidata(TemaEncuesta.AdministracionCobro, ClaseCandidataEncuesta.PrimeraVez),
                Candidata(TemaEncuesta.Vendedor, ClaseCandidataEncuesta.Cambio, "MJ"),
                Candidata(TemaEncuesta.ProblemaResuelto, ClaseCandidataEncuesta.ProblemaResuelto));

            Assert.AreEqual(TemaEncuesta.ProblemaResuelto, d.Elegida.Tema);
            Assert.AreEqual(3, d.Descartes.Count);
            Assert.IsTrue(d.Descartes.All(x => x.Value == MotivoDescarteEncuesta.OtraCandidataElegida));
        }

        [TestMethod]
        public void Prioridad_CambioAntesQuePrimeraVez()
        {
            DecisionEncuestaCliente d = Decidir(null, 60, null,
                Candidata(TemaEncuesta.AdministracionCobro, ClaseCandidataEncuesta.PrimeraVez),
                Candidata(TemaEncuesta.Vendedor, ClaseCandidataEncuesta.Cambio, "MJ"));

            Assert.AreEqual(TemaEncuesta.Vendedor, d.Elegida.Tema);
        }

        [TestMethod]
        public void Prioridad_AIgualdad_ElTemaQueHaceMasQueNoSePregunta()
        {
            var historial = new[] { Historial(100, TemaEncuesta.InformacionTecnica), Historial(500, TemaEncuesta.PreparacionEntrega) };

            DecisionEncuestaCliente d = Decidir(historial, 60, null,
                Candidata(TemaEncuesta.InformacionTecnica, ClaseCandidataEncuesta.Otra, evento: HOY.AddDays(-20)),
                Candidata(TemaEncuesta.PreparacionEntrega, ClaseCandidataEncuesta.Otra));

            // Información técnica se preguntó hace 100 días (< 12 meses): repetido. Entre las que quedan, entrega.
            Assert.AreEqual(TemaEncuesta.PreparacionEntrega, d.Elegida.Tema);
        }

        [TestMethod]
        public void Prioridad_AIgualdad_NuncaPreguntadoGana()
        {
            var historial = new[] { Historial(400, TemaEncuesta.AdministracionCobro), Historial(450, TemaEncuesta.InformacionTecnica) };

            DecisionEncuestaCliente d = Decidir(historial, 60, null,
                Candidata(TemaEncuesta.InformacionTecnica, ClaseCandidataEncuesta.Otra),
                Candidata(TemaEncuesta.PreparacionEntrega, ClaseCandidataEncuesta.Otra));

            Assert.AreEqual(TemaEncuesta.PreparacionEntrega, d.Elegida.Tema);
        }

        [TestMethod]
        public void DosPedidosElMismoDia_SoloUna()
        {
            DecisionEncuestaCliente d = Decidir(null, 60, null,
                Candidata(TemaEncuesta.PreparacionEntrega, pedido: 2), Candidata(TemaEncuesta.PreparacionEntrega, pedido: 1));

            Assert.IsNotNull(d.Elegida);
            Assert.AreEqual(MotivoDescarteEncuesta.OtraCandidataElegida, MotivoDe(d));
        }

        [TestMethod]
        public void DecidirDia_CadaClienteConSuHistorial()
        {
            var historial = new Dictionary<string, List<EncuestaHistorial>>(StringComparer.OrdinalIgnoreCase)
            {
                ["1000"] = new List<EncuestaHistorial> { Historial(10, TemaEncuesta.Vendedor) }
            };

            List<DecisionEncuestaCliente> d = GobernadorEncuestas.DecidirDia(HOY,
                new[] { Candidata(TemaEncuesta.PreparacionEntrega, cliente: "1000"), Candidata(TemaEncuesta.PreparacionEntrega, cliente: "2000") },
                historial, _ => new SituacionClienteEncuesta(), 60);

            Assert.AreEqual(2, d.Count);
            Assert.AreEqual(1, d.Count(x => x.Elegida != null));
            Assert.AreEqual("2000", d.Single(x => x.Elegida != null).Elegida.Cliente);
        }

        [TestMethod]
        public void Ignorada_SoloSiSeEnvioYPasan14DiasSinRespuesta()
        {
            Assert.IsFalse(ReglaEncuestaIgnorada.EsIgnorada(null, null, HOY), "En sombra no se envía: nunca ignorada");
            Assert.IsFalse(ReglaEncuestaIgnorada.EsIgnorada(HOY.AddDays(-13), null, HOY));
            Assert.IsTrue(ReglaEncuestaIgnorada.EsIgnorada(HOY.AddDays(-14), null, HOY));
            Assert.IsFalse(ReglaEncuestaIgnorada.EsIgnorada(HOY.AddDays(-30), HOY.AddDays(-29), HOY));
        }
    }
}
