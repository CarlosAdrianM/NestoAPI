using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Campanas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class ServicioCampanaBloqueTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 23);
        private IRepositorioCampanaBloque repositorio;
        private IUnidadCampanaBloque unidad;
        private DatosCampanaBloque datos;
        private ServicioCampanaBloque servicio;
        private List<Exception> registrados;

        [TestInitialize]
        public void Inicializar()
        {
            datos = new DatosCampanaBloque
            {
                TablaAplicarDto = true,
                TablaCondicionContado = true,
                Candidatos = new List<ProductoCandidatoCampana>
                {
                    new ProductoCandidatoCampana { Numero = "1", Nombre = "CHAMPU", Familia = "Lisap", Grupo = "PEL", SubGrupo = "CHA", PVP = 10M, AplicarDto = true, Estado = 0 },
                    new ProductoCandidatoCampana { Numero = "2", Nombre = "MASCARILLA", Familia = "Lisap", Grupo = "PEL", SubGrupo = "CHA", PVP = 20M, AplicarDto = false, Estado = 0 },
                    new ProductoCandidatoCampana { Numero = "3", Nombre = "TINTE", Familia = "Lisap", Grupo = "MTP", SubGrupo = "TIN", PVP = 5M, AplicarDto = true, Estado = 0 }
                }
            };
            repositorio = A.Fake<IRepositorioCampanaBloque>();
            unidad = A.Fake<IUnidadCampanaBloque>();
            A.CallTo(() => repositorio.Leer(A<ConsultaCampanaBloque>._)).ReturnsLazily(() => Task.FromResult(datos));
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).Returns(Task.FromResult(unidad));
            A.CallTo(() => unidad.Leer(A<ConsultaCampanaBloque>._)).ReturnsLazily(() => Task.FromResult(datos));
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>._, A<DatosCampanaBloque>._)).ReturnsLazily((AltaCampanaBloque alta, DatosCampanaBloque _) =>
                Task.FromResult(new ResultadoInsercionCampanaBloque
                {
                    ProductosCreados = alta.Filas.Where(f => f.CrearDescuento).Select(f => f.Producto).ToList(),
                    AplicarDtoCreadas = alta.Filas.Count(f => f.Desbloquear),
                    CondicionContadoCreadas = alta.Filas.Count(f => f.Contado)
                }));
            registrados = new List<Exception>();
            servicio = new ServicioCampanaBloque(repositorio, ex => registrados.Add(ex));
        }

        private static PeticionCampanaBloqueDTO Peticion(DateTime? desde = null, DateTime? hasta = null) => new PeticionCampanaBloqueDTO
        {
            Nombre = "Black Friday 2026",
            FechaDesde = desde ?? new DateTime(2026, 11, 25),
            FechaHasta = hasta ?? new DateTime(2026, 11, 30),
            Audiencia = 2,
            DesbloquearAplicarDto = true,
            Reglas = new List<ReglaCampanaBloqueDTO> { new ReglaCampanaBloqueDTO { Familia = "Lisap", Descuento = 0.20M } }
        };

        [TestMethod]
        public async Task Ensayo_NoEscribeNada()
        {
            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), true, false, HOY, "NUEVAVISION\\Carlos");

            Assert.AreEqual(EstadoCampanaBloque.Ok, r.Estado);
            Assert.IsTrue(r.Respuesta.Ensayo);
            Assert.AreEqual(2, r.Respuesta.Total.Entran);
            Assert.AreEqual(1, r.Respuesta.Total.FueraPorMotivo[MotivosCampanaBloque.MATERIA_PRIMA]);
            Assert.AreEqual(1, r.Respuesta.Total.FichasDesbloqueadas);
            Assert.IsNull(r.Respuesta.Lineas, "Sin incluirLineas no van todas");
            Assert.AreEqual(3, r.Respuesta.Muestra.Count);
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.Encolar(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ensayo_ConIncluirLineas_TraeTodas()
        {
            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), true, true, HOY, "x");

            Assert.AreEqual(3, r.Respuesta.Lineas.Count);
        }

        [TestMethod]
        public async Task Ensayo_SinTablaDeDesbloqueo_AvisaPeroNoFalla()
        {
            datos.TablaAplicarDto = false;

            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), true, false, HOY, "x");

            Assert.AreEqual(EstadoCampanaBloque.Ok, r.Estado);
            Assert.IsTrue(r.Respuesta.Avisos.Any(a => a.Contains("CampanasAplicarDto")));
        }

        [TestMethod]
        public async Task PeticionIncorrecta_NoLeeNiEscribe()
        {
            PeticionCampanaBloqueDTO p = Peticion();
            p.Nombre = null;

            ResultadoCampanaBloque r = await servicio.Ejecutar(p, false, false, HOY, "x");

            Assert.AreEqual(EstadoCampanaBloque.PeticionIncorrecta, r.Estado);
            A.CallTo(() => repositorio.Leer(A<ConsultaCampanaBloque>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeVerdad_InsertaEnUnaTransaccionConUnRastroYConfirma()
        {
            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), false, false, HOY, "NUEVAVISION\\Carlos");

            Assert.AreEqual(EstadoCampanaBloque.Ok, r.Estado);
            Assert.AreEqual(2, r.Respuesta.FilasCreadas);
            Assert.AreEqual(1, r.Respuesta.FichasDesbloqueoCreadas);
            A.CallTo(() => repositorio.Empezar("1", "Black Friday 2026")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>.That.Matches(a =>
                    a.Campana == "Black Friday 2026" && a.Desde == new DateTime(2026, 11, 25) && a.Hasta == new DateTime(2026, 11, 30)
                    && a.Filas.Count == 2 && a.Filas.Single(f => f.Producto == "2").Desbloquear && a.Filas.All(f => f.Descuento == 0.20M)),
                    A<DatosCampanaBloque>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(ServicioCampanaBloque.TABLA_MODIFICACIONES, A<string>._, A<string>.That.Contains("2 filas nuevas"), A<string>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Dispose()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_CampanaQueAunNoEmpieza_NoEncolaNadaEnLaTienda()
        {
            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), false, false, HOY, "x");

            Assert.IsFalse(r.Respuesta.Vigente);
            Assert.AreEqual(0, r.Respuesta.ProductosEncolados);
            A.CallTo(() => repositorio.Encolar(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
            StringAssert.Contains(r.Respuesta.Mensaje, "cuando empiece");
        }

        [TestMethod]
        public async Task DeVerdad_CampanaYaVigente_EncolaUnaVezLosCreadosQueViajan()
        {
            PeticionCampanaBloqueDTO p = Peticion(HOY, HOY.AddDays(3));

            ResultadoCampanaBloque r = await servicio.Ejecutar(p, false, false, HOY, "x");

            Assert.IsTrue(r.Respuesta.Vigente);
            Assert.AreEqual(2, r.Respuesta.ProductosEncolados);
            A.CallTo(() => repositorio.Encolar(A<IEnumerable<string>>.That.Matches(l => l.Count() == 2 && l.Contains("1") && l.Contains("2")), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_YaVigenteConAudienciaCero_NoEncola()
        {
            PeticionCampanaBloqueDTO p = Peticion(HOY, HOY.AddDays(3));
            p.Audiencia = 0;

            ResultadoCampanaBloque r = await servicio.Ejecutar(p, false, false, HOY, "x");

            Assert.AreEqual(0, r.Respuesta.ProductosEncolados);
            A.CallTo(() => repositorio.Encolar(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeVerdad_FallaElEncolado_NoRompeYAvisa()
        {
            A.CallTo(() => repositorio.Encolar(A<IEnumerable<string>>._, A<string>._)).Throws(new InvalidOperationException("caído"));

            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(HOY, HOY.AddDays(3)), false, false, HOY, "x");

            Assert.AreEqual(EstadoCampanaBloque.Ok, r.Estado);
            Assert.AreEqual(1, registrados.Count);
            Assert.IsTrue(r.Respuesta.Avisos.Any());
        }

        [TestMethod]
        public async Task DeVerdad_RepetirLaPeticion_NoDuplicaNiDejaRastro()
        {
            datos.Existentes = new List<FilaExistenteCampana>
            {
                new FilaExistenteCampana { Id = 1, Producto = "1", Descuento = 0.2M, Campana = "Black Friday 2026" },
                new FilaExistenteCampana { Id = 2, Producto = "2", Descuento = 0.2M, Campana = "Black Friday 2026" }
            };
            // La segunda vez el desbloqueo de la 2 ya existe: el repositorio no crea nada.
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>._, A<DatosCampanaBloque>._)).Returns(Task.FromResult(new ResultadoInsercionCampanaBloque()));

            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), false, false, HOY, "x");

            Assert.AreEqual(0, r.Respuesta.FilasCreadas);
            Assert.AreEqual(2, r.Respuesta.Total.YaEstaban);
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>.That.Matches(a => a.Filas.All(f => !f.CrearDescuento)), A<DatosCampanaBloque>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_SinLaTablaQueHaceFalta_NoEscribeYDevuelveSinTabla()
        {
            datos.TablaAplicarDto = false;

            ResultadoCampanaBloque r = await servicio.Ejecutar(Peticion(), false, false, HOY, "x");

            Assert.AreEqual(EstadoCampanaBloque.SinTabla, r.Estado);
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>._, A<DatosCampanaBloque>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
            A.CallTo(() => unidad.Dispose()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_SiFallaLaInsercion_NoConfirma()
        {
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>._, A<DatosCampanaBloque>._)).Throws(new InvalidOperationException("boom"));

            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.Ejecutar(Peticion(), false, false, HOY, "x"));

            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
            A.CallTo(() => unidad.Dispose()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_CondicionContado_VaEnLasQueEntran()
        {
            PeticionCampanaBloqueDTO p = Peticion();
            p.CondicionContado = true;
            p.TextoAvisoContado = "  Solo al contado  ";

            ResultadoCampanaBloque r = await servicio.Ejecutar(p, false, false, HOY, "x");

            Assert.AreEqual(2, r.Respuesta.CondicionContadoCreadas);
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>.That.Matches(a => a.TextoAvisoContado == "Solo al contado" && a.Filas.All(f => f.Contado)),
                A<DatosCampanaBloque>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_AcumulaParaDistribuidores_LaMarcaEnLaMismaTransaccion()
        {
            datos.TablaAcumulaDistribuidores = true;
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>._, A<DatosCampanaBloque>._))
                .Returns(Task.FromResult(new ResultadoInsercionCampanaBloque { ProductosCreados = new List<string> { "1" }, MarcaDistribuidoresCreada = true }));
            PeticionCampanaBloqueDTO p = Peticion();
            p.AcumulaParaDistribuidores = true;

            ResultadoCampanaBloque r = await servicio.Ejecutar(p, false, false, HOY, "x");

            Assert.IsTrue(r.Respuesta.MarcaDistribuidoresCreada);
            A.CallTo(() => unidad.Insertar(A<AltaCampanaBloque>.That.Matches(a => a.AcumulaParaDistribuidores), A<DatosCampanaBloque>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(A<string>._, A<string>._, A<string>.That.Contains("distribuidores"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeVerdad_AcumulaParaDistribuidoresSinLaTabla_SinTabla()
        {
            datos.TablaAcumulaDistribuidores = false;
            PeticionCampanaBloqueDTO p = Peticion();
            p.AcumulaParaDistribuidores = true;

            ResultadoCampanaBloque r = await servicio.Ejecutar(p, false, false, HOY, "x");

            Assert.AreEqual(EstadoCampanaBloque.SinTabla, r.Estado);
            StringAssert.Contains(r.Mensaje, "CampanasAcumulanDistribuidores");
        }

        [TestMethod]
        public void DescripcionRetirada_DiceQueHaHecho()
        {
            string texto = ServicioCampanaBloque.DescripcionRetirada(
                new RetiradaCampanaBloque { Campana = "BF", Usuario = "Carlos" },
                new ResultadoRetiradaCampanaBloque { FilasDescuento = 6512, FilasAplicarDto = 120, FilasCondicionContado = 0 });

            Assert.AreEqual("Campaña «BF» borrada: 6.512 filas de descuento, 120 de desbloqueo de ficha y 0 de contado. Por Carlos", texto);
        }

        [TestMethod]
        public void GeneradorExcel_GeneraLasDosHojas()
        {
            var respuesta = new RespuestaCampanaBloqueDTO
            {
                Nombre = "Black Friday 2026",
                FechaDesde = new DateTime(2026, 11, 25),
                Lineas = new List<LineaCampanaBloqueDTO>
                {
                    new LineaCampanaBloqueDTO { Producto = "1", Nombre = "CHAMPU", Familia = "Lisap", PVP = 10M, Descuento = 0.2M, PrecioResultante = 8M, Audiencia = 2, Estado = EstadoLineaCampanaBloque.Entra },
                    new LineaCampanaBloqueDTO { Producto = "3", Familia = "Lisap", Descuento = 0.2M, Estado = EstadoLineaCampanaBloque.Fuera, Motivo = MotivosCampanaBloque.MATERIA_PRIMA }
                },
                PorFamilia = new List<TotalesCampanaBloqueDTO> { new TotalesCampanaBloqueDTO { Familia = "Lisap", Productos = 2, Entran = 1, Fuera = 1 } },
                Total = new TotalesCampanaBloqueDTO { Productos = 2, Entran = 1, Fuera = 1 }
            };

            byte[] bytes = new GeneradorExcelCampanaBloque().GenerarBytes(respuesta);

            Assert.IsTrue(bytes.Length > 100);
            Assert.AreEqual((byte)'P', bytes[0], "Un .xlsx es un ZIP");
            Assert.AreEqual((byte)'K', bytes[1]);
        }
    }
}
