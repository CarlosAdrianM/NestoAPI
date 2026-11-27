using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Remesas.ModeloImpagados;
using NestoAPI.Models.Remesas;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Remesas
{
    /// <summary>
    /// NestoAPI#896 (27/11/26): el riesgo de los candidatos a remesa ANTES de girar (GET api/Remesas/ModeloImpagados/Candidatos) y el
    /// núcleo común que comparte con la sombra.
    /// </summary>
    [TestClass]
    public class CandidatosRiesgoImpagosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 27);

        private sealed class PredictorFalso : IPredictorImpagos
        {
            public bool HayAlgo { get; set; } = true;
            public bool ConModelo { get; set; } = true;
            public List<IList<RasgosImpago>> Llamadas { get; } = new List<IList<RasgosImpago>>();

            public List<PrediccionRiesgoImpago> Predecir(IList<RasgosImpago> rasgos)
            {
                Llamadas.Add(rasgos);
                return rasgos.Select(r => new PrediccionRiesgoImpago
                {
                    Probabilidad = ConModelo ? (r.EsRegiro > 0 ? 0.4 : 0.02) + r.RecibosUltimos45 / 100.0 : (double?)null,
                    ProbabilidadRegla = r.EsRegiro > 0 ? 0.35 : 0.03,
                    VersionModelo = ConModelo ? "impagos-1 2026-11-22" : null,
                    Motivos = r.EsRegiro > 0 ? new List<string> { "se vuelve a girar un recibo devuelto ", "devolvió un recibo hace 20 días" } : new List<string>()
                }).ToList();
            }
        }

        private static ReciboGirado Recibo(int orden, string cliente, string documento, DateTime giro, int? remesa, double importe = 100)
            => new ReciboGirado
            {
                Empresa = "1",
                NumOrden = orden,
                Cliente = cliente,
                Contacto = "0",
                FechaFactura = new DateTime(2026, 10, 15),
                FechaVto = new DateTime(2026, 11, 25),
                FechaGiro = giro,
                Documento = documento,
                Efecto = "1",
                Importe = importe,
                Remesa = remesa,
                Vendedor = "NV",
                FormaVenta = "DIR",
                Ccc = "1",
                Entidad = "2100",
                Nombre = "PELUQUERÍA PRUEBA  "
            };

        /// <summary>
        /// La historia del cliente 200: un giro de NV500 hace 30 días que volvió hace 20 (el efecto de hoy es su REGIRO) y otro recibo
        /// girado hace 10 días que aún no ha vuelto (cuenta en «recibos en 45 días»). Cada llamada da objetos nuevos (el enlace los toca).
        /// </summary>
        private static List<ReciboGirado> HistoriaAnterior() => new List<ReciboGirado>
        {
            Recibo(10, "200", "NV500", HOY.AddDays(-30), 10900),
            Recibo(11, "200", "NV510", HOY.AddDays(-10), 10940)
        };

        private static List<ImpagadoLeido> Impagados() => new List<ImpagadoLeido>
        {
            new ImpagadoLeido { Empresa = "1", Cliente = "200", Documento = "NV500", Efecto = "1", Fecha = HOY.AddDays(-20), Concepto = "Saldo insuficiente." }
        };

        private static EfectoCandidatoDTO Candidato(int id, string cliente, string documento, decimal importe, bool preseleccionado = true)
            => new EfectoCandidatoDTO
            {
                Id = id,
                Cliente = cliente + "  ",
                Contacto = "0 ",
                Documento = documento + " ",
                Efecto = "1",
                Fecha = new DateTime(2026, 10, 15),
                Vencimiento = new DateTime(2026, 11, 25),
                ImportePendiente = importe,
                Ccc = "1",
                Preseleccionado = preseleccionado,
                Motivo = preseleccionado ? null : "Retenido: prueba "
            };

        [TestMethod]
        public void Nucleo_ElCandidatoDaLoMismoQueEseEfectoYaGiradoHoyEnLaSombra()
        {
            // La sombra: el efecto 50 (regiro de NV500) ya está en la remesa 10950 de hoy y por eso también sale en la historia.
            IRepositorioModeloImpagos repoSombra = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repoSombra.TablaPrediccionesExiste()).Returns(true);
            _ = A.CallTo(() => repoSombra.LeerEfectosRemesa("1", 10950)).Returns(new List<ReciboGirado> { Recibo(50, "200", "NV500", HOY, 10950, 250) });
            _ = A.CallTo(() => repoSombra.LeerHistorialClientes("1", A<IReadOnlyCollection<string>>._, A<DateTime>._)).ReturnsLazily(() => new HistoricoImpagos
            {
                Recibos = HistoriaAnterior().Concat(new[] { Recibo(50, "200", "NV500", HOY, 10950, 250) }).ToList(),
                Impagados = Impagados()
            });
            _ = A.CallTo(() => repoSombra.LeerNivelGeneral(HOY)).Returns(new NivelGeneralImpagos(null, null));
            List<FilaPrediccionImpago> guardadas = null;
            _ = A.CallTo(() => repoSombra.GuardarPredicciones(A<IReadOnlyCollection<FilaPrediccionImpago>>._))
                .ReturnsLazily((IReadOnlyCollection<FilaPrediccionImpago> f) => { guardadas = f.ToList(); return f.Count; });
            var predictorSombra = new PredictorFalso();
            Assert.AreEqual(1, new SombraImpagosRemesa(repoSombra, predictorSombra, () => HOY.AddHours(10), _ => { }).RegistrarRemesa("1", 10950));

            // Los candidatos: el mismo efecto, todavía sin girar (sin remesa) y fuera de la historia.
            IRepositorioModeloImpagos repoCandidatos = A.Fake<IRepositorioModeloImpagos>();
            ReciboGirado sinGirar = Recibo(50, "200", "NV500", new DateTime(2026, 10, 15), null, 250);
            _ = A.CallTo(() => repoCandidatos.LeerEfectosCartera("1", A<IReadOnlyCollection<int>>._)).Returns(new List<ReciboGirado> { sinGirar });
            _ = A.CallTo(() => repoCandidatos.LeerHistorialClientes("1", A<IReadOnlyCollection<string>>._, A<DateTime>._)).ReturnsLazily(() => new HistoricoImpagos
            {
                Recibos = HistoriaAnterior(),
                Impagados = Impagados()
            });
            _ = A.CallTo(() => repoCandidatos.LeerNivelGeneral(HOY)).Returns(new NivelGeneralImpagos(null, null));
            var predictorCandidatos = new PredictorFalso();

            CandidatosRiesgoImpagosDTO dto = CandidatosRiesgoImpagos.Construir(repoCandidatos, predictorCandidatos, "1 ",
                new List<EfectoCandidatoDTO> { Candidato(50, "200", "NV500", 250m) }, HOY.AddHours(9));

            RasgosImpago enSombra = predictorSombra.Llamadas.Single().Single();
            RasgosImpago enCandidatos = predictorCandidatos.Llamadas.Single().Single();
            Assert.AreEqual(JsonConvert.SerializeObject(enSombra), JsonConvert.SerializeObject(enCandidatos), "mismos rasgos girado hoy que sin girar");
            Assert.AreEqual(1f, enCandidatos.EsRegiro, "regiro: el giro anterior de ese documento y efecto volvió");
            Assert.AreEqual(1f, enCandidatos.RecibosUltimos45, "solo el girado hace 10 días: el propio efecto no cuenta");
            Assert.AreEqual(2f, enCandidatos.RecibosAnteriores);
            Assert.AreEqual(HOY.Day, (int)enCandidatos.DiaMes, "el día del giro es hoy");
            Assert.AreEqual(guardadas.Single().Probabilidad, dto.Efectos.Single().Probabilidad);
            Assert.AreEqual(guardadas.Single().ProbabilidadRegla, dto.Efectos.Single().ProbabilidadRegla);
            Assert.AreEqual(guardadas.Single().Rasgos, JsonConvert.SerializeObject(enCandidatos));
            A.CallTo(() => repoCandidatos.GuardarPredicciones(A<IReadOnlyCollection<FilaPrediccionImpago>>._)).MustNotHaveHappened();
            A.CallTo(() => repoCandidatos.LeerHistorico(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Construir_DaLosCandidatosConSuRiesgoDeMayorAMenor_YRecortaLasCadenas()
        {
            IRepositorioModeloImpagos repo = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repo.LeerEfectosCartera("1", A<IReadOnlyCollection<int>>._)).Returns(new List<ReciboGirado>
            {
                Recibo(60, "100", "NV600", new DateTime(2026, 10, 15), null, 80),
                Recibo(50, "200", "NV500", new DateTime(2026, 10, 15), null, 250)
            });
            _ = A.CallTo(() => repo.LeerHistorialClientes("1", A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .ReturnsLazily(() => new HistoricoImpagos { Recibos = HistoriaAnterior(), Impagados = Impagados() });
            _ = A.CallTo(() => repo.LeerNivelGeneral(HOY)).Returns(new NivelGeneralImpagos(null, null));

            CandidatosRiesgoImpagosDTO dto = CandidatosRiesgoImpagos.Construir(repo, new PredictorFalso(), "1",
                new List<EfectoCandidatoDTO> { Candidato(60, "100", "NV600", 80m), Candidato(50, "200", "NV500", 250m, preseleccionado: false) }, HOY.AddHours(9));

            Assert.IsTrue(dto.ConModelo);
            Assert.AreEqual("impagos-1 2026-11-22", dto.VersionModelo);
            Assert.IsNull(dto.Mensaje);
            Assert.AreEqual(HOY.AddHours(9), dto.Fecha);
            CollectionAssert.AreEqual(new[] { 50, 60 }, dto.Efectos.Select(e => e.NumOrden).ToList());
            EfectoRiesgoImpagoDTO alto = dto.Efectos[0];
            Assert.AreEqual("200", alto.Cliente);
            Assert.AreEqual("0", alto.Contacto);
            Assert.AreEqual("NV500", alto.Documento);
            Assert.AreEqual("PELUQUERÍA PRUEBA", alto.Nombre);
            Assert.AreEqual("1", alto.Empresa);
            Assert.AreEqual(0.41, alto.Probabilidad.Value, 1e-9);
            Assert.AreEqual(0.35, alto.ProbabilidadRegla);
            Assert.AreEqual("≥ 30 %", alto.Tramo);
            Assert.IsFalse(alto.Preseleccionado);
            Assert.AreEqual("Retenido: prueba", alto.MotivoRetencion);
            CollectionAssert.AreEqual(new[] { "se vuelve a girar un recibo devuelto", "devolvió un recibo hace 20 días" }, alto.Motivos);
            Assert.AreEqual("< 5 %", dto.Efectos[1].Tramo);
            Assert.AreEqual(2, dto.Total.Recibos);
            Assert.AreEqual(330m, dto.Total.Importe);
        }

        private static Dictionary<int, RiesgoEfectoImpago> Riesgos(params (int Orden, double? Modelo, double? Regla)[] filas)
            => filas.ToDictionary(f => f.Orden, f => new RiesgoEfectoImpago
            {
                Efecto = new ReciboGirado { Empresa = "1", NumOrden = f.Orden },
                Prediccion = new PrediccionRiesgoImpago { Probabilidad = f.Modelo, ProbabilidadRegla = f.Regla, VersionModelo = f.Modelo.HasValue ? "impagos-1" : null }
            });

        [TestMethod]
        public void Armar_TramosYTotales()
        {
            var candidatos = new List<EfectoCandidatoDTO>
            {
                Candidato(1, "1", "A", 100m), Candidato(2, "2", "B", 200m), Candidato(3, "3", "C", 300m),
                Candidato(4, "4", "D", 400m), Candidato(5, "5", "E", 500m)
            };
            Dictionary<int, RiesgoEfectoImpago> riesgos = Riesgos((1, 0.01, 0.02), (2, 0.05, 0.02), (3, 0.149, 0.1), (4, 0.15, 0.2), (5, 0.30, 0.5));

            CandidatosRiesgoImpagosDTO dto = CandidatosRiesgoImpagos.Armar(HOY, "1", candidatos, riesgos, null);

            CollectionAssert.AreEqual(new[] { "< 5 %", "5-15 %", "15-30 %", "≥ 30 %" }, dto.Tramos.Select(t => t.Tramo).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 1 }, dto.Tramos.Select(t => t.Recibos).ToList());
            CollectionAssert.AreEqual(new[] { 100m, 500m, 400m, 500m }, dto.Tramos.Select(t => t.Importe).ToList());
            Assert.AreEqual(0.20, dto.Tramos[1].DevolucionesEsperadas, 1e-9);
            Assert.AreEqual(5, dto.Total.Recibos);
            Assert.AreEqual(1500m, dto.Total.Importe);
            Assert.AreEqual(0.66, dto.Total.DevolucionesEsperadas.Value, 1e-9);
            // 1 + 10 + 44,7 + 60 + 150
            Assert.AreEqual(265.70m, dto.Total.ImporteEnRiesgo);
            Assert.AreEqual(5, dto.Efectos[0].NumOrden);
            Assert.IsTrue(dto.ConModelo);
        }

        [TestMethod]
        public void Armar_SinModeloPeroConRegla_LaProbabilidadEsLaDeLaRegla()
        {
            var candidatos = new List<EfectoCandidatoDTO> { Candidato(1, "1", "A", 100m), Candidato(2, "2", "B", 200m) };

            CandidatosRiesgoImpagosDTO dto = CandidatosRiesgoImpagos.Armar(HOY, "1", candidatos, Riesgos((1, null, 0.02), (2, null, 0.2)), null);

            Assert.IsFalse(dto.ConModelo);
            Assert.IsNull(dto.VersionModelo);
            Assert.AreEqual(CandidatosRiesgoImpagos.MENSAJE_SOLO_REGLA, dto.Mensaje);
            Assert.AreEqual(0.2, dto.Efectos[0].Probabilidad);
            Assert.AreEqual("15-30 %", dto.Efectos[0].Tramo);
            Assert.AreEqual(0.22, dto.Total.DevolucionesEsperadas.Value, 1e-9);
        }

        [TestMethod]
        public void Construir_SinReglaNiModeloGuardados_DevuelveLosCandidatosSinProbabilidadYNoLeeNada()
        {
            IRepositorioModeloImpagos repo = A.Fake<IRepositorioModeloImpagos>();

            CandidatosRiesgoImpagosDTO dto = CandidatosRiesgoImpagos.Construir(repo, new PredictorFalso { HayAlgo = false }, "1",
                new List<EfectoCandidatoDTO> { Candidato(1, "1", "A", 100m), Candidato(2, "2", "B", 50m) }, HOY);

            Assert.IsFalse(dto.ConModelo);
            Assert.IsNull(dto.VersionModelo);
            Assert.AreEqual(CandidatosRiesgoImpagos.MENSAJE_SIN_MODELO, dto.Mensaje);
            Assert.AreEqual(2, dto.Efectos.Count);
            Assert.IsTrue(dto.Efectos.All(e => !e.Probabilidad.HasValue && !e.ProbabilidadRegla.HasValue && e.Tramo == null));
            Assert.AreEqual(2, dto.Total.Recibos);
            Assert.AreEqual(150m, dto.Total.Importe);
            Assert.IsNull(dto.Total.DevolucionesEsperadas);
            Assert.IsNull(dto.Total.ImporteEnRiesgo);
            Assert.AreEqual(0, dto.Tramos.Count);
            A.CallTo(repo).MustNotHaveHappened();
        }

        [TestMethod]
        public void Construir_SinCandidatos_NoLeeLaHistoria()
        {
            IRepositorioModeloImpagos repo = A.Fake<IRepositorioModeloImpagos>();

            CandidatosRiesgoImpagosDTO dto = CandidatosRiesgoImpagos.Construir(repo, new PredictorFalso(), "1", new List<EfectoCandidatoDTO>(), HOY);

            Assert.AreEqual(0, dto.Total.Recibos);
            A.CallTo(() => repo.LeerHistorialClientes(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void NombreTramo_LosCuatroLimitesDeLaMedida()
        {
            Assert.AreEqual("< 5 %", CandidatosRiesgoImpagos.NombreTramo(0));
            Assert.AreEqual("< 5 %", CandidatosRiesgoImpagos.NombreTramo(0.0499));
            Assert.AreEqual("5-15 %", CandidatosRiesgoImpagos.NombreTramo(0.05));
            Assert.AreEqual("15-30 %", CandidatosRiesgoImpagos.NombreTramo(0.15));
            Assert.AreEqual("≥ 30 %", CandidatosRiesgoImpagos.NombreTramo(0.30));
            Assert.AreEqual("≥ 30 %", CandidatosRiesgoImpagos.NombreTramo(1));
        }
    }
}
