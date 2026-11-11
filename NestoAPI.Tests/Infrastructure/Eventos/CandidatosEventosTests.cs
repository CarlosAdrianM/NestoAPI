using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Eventos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// Eventos: candidatos a invitar, en MODO SOMBRA (corte 1, Carlos 11/11/26). La puntuación por reglas (pura), la distancia, la
    /// selección con su muestra de control y el servicio (sin BD).
    /// </summary>
    [TestClass]
    public class CandidatosEventosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12);

        // ---------------------------------------------------------------- Regla (pura)

        [TestMethod]
        public void Puntuar_SinNadaDeAfinidad_Cero()
        {
            Assert.AreEqual(0m, ReglaCandidatosEventos.Puntuar(new FotoCandidatoEvento { EventoPresencial = true }));
        }

        [TestMethod]
        public void Puntuar_QuienCompraLoDelEventoYEstaCerca_PasaElCorte()
        {
            var foto = new FotoCandidatoEvento
            {
                EventoPresencial = true,
                ComproRequerido = true,
                DiasUltimaCompraRequerido = 60,
                ImporteMarca24m = 300m,
                DistanciaKm = 8m,
                DiasUltimaCompra = 30,
                TieneMovil = true
            };

            decimal p = ReglaCandidatosEventos.Puntuar(foto);

            Assert.AreEqual(0.85m, p); // 0.30 + 0.05 + 0.20 + 0.15 + 0.10 + 0.05 (sin tienda)
            Assert.IsTrue(p >= ReglaCandidatosEventos.UMBRAL);
        }

        [TestMethod]
        public void Puntuar_LaMismaMarcaCuentaYElSubgrupoNo_SoloHayMarcaEnLasCaracteristicas()
        {
            var conMarca = new FotoCandidatoEvento { ImporteMarca24m = 150m };

            Assert.AreEqual(0.15m, ReglaCandidatosEventos.Puntuar(conMarca));
        }

        [TestMethod]
        public void Puntuar_LaDistanciaSoloCuentaEnPresencial_YLejosResta()
        {
            var cerca = new FotoCandidatoEvento { DistanciaKm = 5m, CursosComprados = 1, EventoPresencial = true };
            var cercaOnline = new FotoCandidatoEvento { DistanciaKm = 5m, CursosComprados = 1, EventoPresencial = false };
            var lejos = new FotoCandidatoEvento { DistanciaKm = 300m, CursosComprados = 1, EventoPresencial = true };

            Assert.AreEqual(0.25m, ReglaCandidatosEventos.Puntuar(cerca));
            Assert.AreEqual(0.10m, ReglaCandidatosEventos.Puntuar(cercaOnline));
            Assert.AreEqual(0m, ReglaCandidatosEventos.Puntuar(lejos));
        }

        [TestMethod]
        public void Puntuar_NuncaPasaDeUno()
        {
            var todo = new FotoCandidatoEvento
            {
                EventoPresencial = true, ComproRequerido = true, DiasUltimaCompraRequerido = 1, ImporteMarca24m = 9999m, InscripcionesPrevias = 3,
                AsistenciasPrevias = 2, CursosComprados = 5, DistanciaKm = 1m, DiasUltimaCompra = 1, ImporteTienda24m = 100m, TieneMovil = true
            };

            Assert.AreEqual(1m, ReglaCandidatosEventos.Puntuar(todo));
        }

        [TestMethod]
        public void DistanciaKm_DeReinaAAlgete_UnosVeintiTantos()
        {
            decimal km = ReglaCandidatosEventos.DistanciaKm(SedesReparto.REINA, SedesReparto.ALGETE);

            Assert.IsTrue(km > 20m && km < 25m, km.ToString());
            Assert.AreEqual(0m, ReglaCandidatosEventos.DistanciaKm(SedesReparto.REINA, SedesReparto.REINA));
        }

        [TestMethod]
        public void Seleccionar_LosQuePasanYUnaMuestraDeControlDeLosQueNo_LaMismaConLaMismaSemilla()
        {
            List<FotoCandidatoEvento> fotos = Enumerable.Range(1, 1000)
                .Select(i => new FotoCandidatoEvento { Cliente = i.ToString(), Puntuacion = i <= 20 ? 0.5m : 0.1m }).ToList();

            List<FotoCandidatoEvento> una = ReglaCandidatosEventos.Seleccionar(fotos, 42);
            List<string> controlUna = una.Where(f => f.EsControl).Select(f => f.Cliente).ToList();
            List<FotoCandidatoEvento> otra = ReglaCandidatosEventos.Seleccionar(fotos, 42);

            Assert.AreEqual(20, una.Count(f => !f.EsControl));
            Assert.AreEqual(ReglaCandidatosEventos.TAMANO_CONTROL, controlUna.Count);
            Assert.IsTrue(una.Where(f => f.EsControl).All(f => f.Puntuacion < ReglaCandidatosEventos.UMBRAL));
            CollectionAssert.AreEqual(controlUna, otra.Where(f => f.EsControl).Select(f => f.Cliente).ToList(), "Idempotente en el día");
        }

        [TestMethod]
        public void Seleccionar_TopeDeCandidatos()
        {
            List<FotoCandidatoEvento> fotos = Enumerable.Range(1, ReglaCandidatosEventos.MAXIMO_CANDIDATOS + 50)
                .Select(i => new FotoCandidatoEvento { Cliente = i.ToString(), Puntuacion = 0.9m }).ToList();

            Assert.AreEqual(ReglaCandidatosEventos.MAXIMO_CANDIDATOS, ReglaCandidatosEventos.Seleccionar(fotos, 1).Count(f => !f.EsControl));
        }

        // ---------------------------------------------------------------- Servicio

        private IRepositorioCandidatosEventos repo;
        private List<FotoCandidatoEvento> guardadas;

        [TestInitialize]
        public void Setup()
        {
            repo = A.Fake<IRepositorioCandidatosEventos>();
            guardadas = null;
            A.CallTo(() => repo.HayTabla()).Returns(true);
            A.CallTo(() => repo.LeerEvento(4)).Returns(new EventoParaCandidatos { Id = 4, Empresa = "1", Fecha = HOY.AddDays(7), Almacen = "REI", Activo = true, Formato = "Presencial" });
            A.CallTo(() => repo.CoordenadasDirecciones()).Returns(new Dictionary<string, CoordenadasGeo>());
            A.CallTo(() => repo.CentrosCodigosPostales()).Returns(new Dictionary<string, CoordenadasGeo> { ["28004"] = new CoordenadasGeo(40.4240m, -3.7000m) });
            A.CallTo(() => repo.Guardar(A<EventoParaCandidatos>._, A<DateTime>._, A<string>._, A<List<FotoCandidatoEvento>>._))
                .ReturnsLazily((EventoParaCandidatos e, DateTime d, string m, List<FotoCandidatoEvento> f) => { guardadas = f; return Task.FromResult(f.Count); });
        }

        [TestMethod]
        public async Task Calcular_PuntuaGuardaYResume_ConLaDistanciaPorElCentroDelCodigoPostal()
        {
            A.CallTo(() => repo.LeerCaracteristicas(A<EventoParaCandidatos>._, HOY)).Returns(new List<CaracteristicasCandidatoEvento>
            {
                new CaracteristicasCandidatoEvento { Cliente = "1001      ", CodigoPostal = "28004", TieneMovil = true, UltimaCompraRequerido = HOY.AddDays(-30),
                    ImporteMarca24m = 300m, UltimaCompra = HOY.AddDays(-10), FechaAlta = HOY.AddDays(-400) },
                new CaracteristicasCandidatoEvento { Cliente = "1002", CodigoPostal = "08001", TieneCorreo = true }
            });

            ResumenCandidatosEventoDTO r = await new ServicioCandidatosEventos(repo).Calcular(4, HOY, ServicioCandidatosEventos.MOTIVO_DIARIO);

            Assert.IsNull(r.Omitido);
            Assert.AreEqual(2, r.Evaluados);
            Assert.AreEqual(1, r.Candidatos);
            Assert.AreEqual(1, r.Control);
            FotoCandidatoEvento uno = guardadas.Single(f => f.Cliente == "1001");
            Assert.AreEqual("CodigoPostal", uno.FuenteDistancia);
            Assert.IsTrue(uno.DistanciaKm < 1m);
            Assert.AreEqual(7, uno.DiasHastaEvento);
            Assert.AreEqual(30, uno.DiasUltimaCompraRequerido);
            Assert.AreEqual(400, uno.AntiguedadDias);
            Assert.IsTrue(uno.ComproRequerido);
            Assert.IsFalse(uno.EsControl);
            FotoCandidatoEvento dos = guardadas.Single(f => f.Cliente == "1002");
            Assert.IsNull(dos.DistanciaKm, "Sin su CP en la tabla de centros, la distancia queda NULL");
            Assert.IsTrue(dos.EsControl);
        }

        [TestMethod]
        public async Task Calcular_LaDireccionGeocodificadaManda()
        {
            string clave = new DireccionParada("Calle Mayor 1", "28004", "Madrid").Clave;
            A.CallTo(() => repo.CoordenadasDirecciones()).Returns(new Dictionary<string, CoordenadasGeo> { [clave] = SedesReparto.ALGETE });
            A.CallTo(() => repo.LeerCaracteristicas(A<EventoParaCandidatos>._, HOY)).Returns(new List<CaracteristicasCandidatoEvento>
            {
                new CaracteristicasCandidatoEvento { Cliente = "1001", Direccion = "Calle Mayor 1", CodigoPostal = "28004", Poblacion = "Madrid", TieneMovil = true }
            });

            _ = await new ServicioCandidatosEventos(repo).Calcular(4, HOY, ServicioCandidatosEventos.MOTIVO_ALTA);

            Assert.AreEqual("Direccion", guardadas.Single().FuenteDistancia);
            Assert.IsTrue(guardadas.Single().DistanciaKm > 20m);
        }

        [TestMethod]
        public async Task Calcular_SinTabla_NoHaceNada()
        {
            A.CallTo(() => repo.HayTabla()).Returns(false);

            ResumenCandidatosEventoDTO r = await new ServicioCandidatosEventos(repo).Calcular(4, HOY, ServicioCandidatosEventos.MOTIVO_DIARIO);

            Assert.IsNotNull(r.Omitido);
            A.CallTo(() => repo.LeerCaracteristicas(A<EventoParaCandidatos>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => repo.Guardar(A<EventoParaCandidatos>._, A<DateTime>._, A<string>._, A<List<FotoCandidatoEvento>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Calcular_EventoPasadoOInactivo_NoHaceNada()
        {
            A.CallTo(() => repo.LeerEvento(5)).Returns(new EventoParaCandidatos { Id = 5, Fecha = HOY.AddDays(-1), Activo = true });
            A.CallTo(() => repo.LeerEvento(6)).Returns(new EventoParaCandidatos { Id = 6, Fecha = HOY.AddDays(3), Activo = false });

            Assert.IsNotNull((await new ServicioCandidatosEventos(repo).Calcular(5, HOY, "Diario")).Omitido);
            Assert.IsNotNull((await new ServicioCandidatosEventos(repo).Calcular(6, HOY, "Diario")).Omitido);
            A.CallTo(() => repo.Guardar(A<EventoParaCandidatos>._, A<DateTime>._, A<string>._, A<List<FotoCandidatoEvento>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_CalculaTodosLosFuturosYUnFalloNoParaLosDemas()
        {
            A.CallTo(() => repo.EventosFuturosActivos(HOY)).Returns(new List<int> { 4, 7 });
            A.CallTo(() => repo.LeerEvento(7)).Throws(new TimeoutException("tiempo agotado"));
            A.CallTo(() => repo.LeerCaracteristicas(A<EventoParaCandidatos>._, HOY)).Returns(new List<CaracteristicasCandidatoEvento>());
            var errores = new List<Exception>();

            List<ResumenCandidatosEventoDTO> r = await new CandidatosEventosJobsService(repo, ex => errores.Add(ex)).CalcularTodosAsync(HOY);

            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(4, r[0].EventoId);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void TablaFotos_UnaFilaPorFotoConLaVersionYLosNulos()
        {
            System.Data.DataTable t = RepositorioCandidatosEventosSql.TablaFotos(4, "1", HOY, "Diario", new[]
            {
                new FotoCandidatoEvento { Cliente = "1001", Puntuacion = 0.5m }
            });

            Assert.AreEqual(1, t.Rows.Count);
            Assert.AreEqual(ReglaCandidatosEventos.VERSION, t.Rows[0]["VersionRegla"]);
            Assert.AreEqual(DBNull.Value, t.Rows[0]["DistanciaKm"]);
            Assert.AreEqual(32, t.Columns.Count);
        }
    }
}
