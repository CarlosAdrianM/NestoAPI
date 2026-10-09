using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#577 (corte 3b): el job que rellena cada reposición del calendario a su hora de corte. El corte es un DATO
    /// (día + HoraCierre), no el reloj: da igual que Hangfire arranque unos segundos antes o después.
    /// Semana de referencia: lunes 19/10/2026 (sin festivos salvo los que pone cada test).
    /// </summary>
    [TestClass]
    public class ServicioReposicionAutomaticaTests
    {
        private static readonly DateTime LUNES = new DateTime(2026, 10, 19);
        private static readonly DateTime CORTE_LUNES = LUNES.AddHours(10);

        private RepositorioEnMemoria repositorio;
        private List<(CrearReposicionDTO Peticion, IPrincipal Usuario, DateTime? Corte)> creadas;
        private Func<CrearReposicionDTO, Exception> fallo;
        private List<Exception> avisos;
        private HashSet<string> festivos;
        private DateTime ahora;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioEnMemoria();
            creadas = new List<(CrearReposicionDTO, IPrincipal, DateTime?)>();
            fallo = p => null;
            avisos = new List<Exception>();
            festivos = new HashSet<string>();
            ahora = LUNES.AddHours(10).AddSeconds(3);
            repositorio.Calendario.AddRange(new[]
            {
                Fila("REI", "ALG", 1), Fila("ALC", "ALG", 2), Fila("ALG", "ALC", 1)
            });
        }

        private ServicioReposicionAutomatica Servicio()
        {
            return new ServicioReposicionAutomatica(repositorio,
                (peticion, usuario, corte) =>
                {
                    repositorio.Pasos.Add($"crear {peticion.Origen}→{peticion.Destino}");
                    Exception ex = fallo(peticion);
                    if (ex != null)
                    {
                        return Task.FromException<ReposicionEnPreparacionDTO>(ex);
                    }
                    creadas.Add((peticion, usuario, corte));
                    var creada = new ReposicionEnPreparacionDTO
                    {
                        Origen = peticion.Origen,
                        Destino = peticion.Destino,
                        NumTraspaso = peticion.Origen == "ALG" ? 80999 : (int?)null,
                        Lineas = new List<LineaReposicionEnPreparacionDTO> { new LineaReposicionEnPreparacionDTO(), new LineaReposicionEnPreparacionDTO() }
                    };
                    return Task.FromResult(creada);
                },
                new CalculadoraFechaReposicion((dia, almacen) => festivos.Contains($"{almacen}|{dia:yyyyMMdd}")),
                () => ahora,
                ex => avisos.Add(ex));
        }

        private static ReposicionCalendario Fila(string origen, string destino, byte dia, string cierre = "10:00", string llegada = "13:30",
            byte antelacion = 0)
        {
            return new ReposicionCalendario
            {
                Empresa = "1  ", AlmacenOrigen = origen, AlmacenDestino = destino, DiaSemana = dia,
                HoraCierre = TimeSpan.Parse(cierre), HoraLlegadaHabitual = TimeSpan.Parse(llegada), LaborablesAntelacionCierre = antelacion,
                Activo = true
            };
        }

        // ---------------------------------------------------------------- NestoAPI#577 (corte 3d): cierre el laborable anterior

        private static readonly DateTime VIERNES_ANTERIOR = LUNES.AddDays(-3);

        /// <summary>Algete → Reina llega el lunes a las 11:00 y se cierra el laborable anterior (el viernes) a las 13:00.</summary>
        private void SoloAlgeteReinaDelLunes()
        {
            repositorio.Calendario.Clear();
            repositorio.Calendario.Add(Fila("ALG", "REI", 1, "13:00", "11:00", antelacion: 1));
        }

        [TestMethod]
        public async Task Job_ConAntelacion_ElViernesALas13RellenaLaDelLunesConElCorteDelViernes()
        {
            SoloAlgeteReinaDelLunes();
            ahora = VIERNES_ANTERIOR.AddHours(13).AddSeconds(4);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(VIERNES_ANTERIOR.AddHours(13), creadas.Single().Corte, "El instante de cierre real: el viernes a las 13:00");
            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single().Resultado);
            CollectionAssert.AreEqual(new[]
            {
                "bloquear 1 ALG 16/10/2026",
                "¿hay? ALG→REI 16/10/2026 13:00 contando omitidas",
                "crear ALG→REI",
                "soltar 1 ALG 16/10/2026"
            }, repositorio.Pasos);
        }

        [TestMethod]
        public async Task Job_ConAntelacion_ElViernesAntesDeLas13_NoHaceNada()
        {
            SoloAlgeteReinaDelLunes();
            ahora = VIERNES_ANTERIOR.AddHours(12).AddMinutes(59);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, resultados.Count);
            Assert.AreEqual(0, creadas.Count);
        }

        [TestMethod]
        public async Task Job_ConAntelacion_ElLunesYaRellenadaElViernes_NoLaDuplica()
        {
            SoloAlgeteReinaDelLunes();
            repositorio.Hechas.Add("ALG→REI 16/10/2026 13:00");
            ahora = LUNES.AddHours(6);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, creadas.Count);
            Assert.AreEqual(0, resultados.Count);
            CollectionAssert.Contains(repositorio.Pasos, "bloquear 1 ALG 16/10/2026", "Misma clave de bloqueo (el día del corte) que el viernes");
        }

        [TestMethod]
        public async Task Job_ConAntelacion_SiElViernesNoSeRellenó_ElLunesAntesDeLlegarSeRellenaConElCorteDelViernes()
        {
            SoloAlgeteReinaDelLunes();
            ahora = LUNES.AddHours(6).AddMinutes(5);

            _ = await Servicio().RellenarPendientes();

            Assert.AreEqual(VIERNES_ANTERIOR.AddHours(13), creadas.Single().Corte);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public async Task Job_ConAntelacion_ElLunesPasadaLaLlegadaSinRellenar_FueraDePlazo()
        {
            SoloAlgeteReinaDelLunes();
            ahora = LUNES.AddHours(11);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, creadas.Count);
            Assert.AreEqual(ResultadosReposicionAutomatica.FUERA_DE_PLAZO, resultados.Single().Resultado);
            Assert.AreEqual(VIERNES_ANTERIOR.AddHours(13), repositorio.Omitidas.Single().Cabecera.FechaCorte);
            Assert.AreEqual(1, avisos.Count);
        }

        [TestMethod]
        public async Task Job_ConAntelacion_FestivoEnAlgeteElViernes_SeRellenaElJueves()
        {
            SoloAlgeteReinaDelLunes();
            festivos.Add($"ALG|{VIERNES_ANTERIOR:yyyyMMdd}");
            ahora = VIERNES_ANTERIOR.AddDays(-1).AddHours(13).AddMinutes(1);

            _ = await Servicio().RellenarPendientes();

            Assert.AreEqual(VIERNES_ANTERIOR.AddDays(-1).AddHours(13), creadas.Single().Corte);
        }

        [TestMethod]
        public async Task Relanzar_ConAntelacion_ElLunesRelanzaLaDelLunesConElCorteDelViernes()
        {
            SoloAlgeteReinaDelLunes();
            ahora = LUNES.AddHours(8);

            ResultadoReposicionAutomaticaDTO resultado = await Servicio().RellenarRuta("1", "ALG", "REI", Usuario("Carlos", "Informática"));

            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultado.Resultado);
            Assert.AreEqual(VIERNES_ANTERIOR.AddHours(13), creadas.Single().Corte);
            CollectionAssert.Contains(repositorio.Pasos, "bloquear 1 ALG 16/10/2026");
        }

        [TestMethod]
        public async Task Relanzar_ConAntelacion_YaRellenada_409DiceElDiaDelCorte()
        {
            SoloAlgeteReinaDelLunes();
            repositorio.Hechas.Add("ALG→REI 16/10/2026 13:00");
            ahora = LUNES.AddHours(8);

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => Servicio().RellenarRuta("1", "ALG", "REI", Usuario("Carlos", "Informática")));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "de las 13:00 del 16/10");
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        // ---------------------------------------------------------------- job

        [TestMethod]
        public async Task Job_PasadoElCorte_RellenaCadaRutaDeHoyConElCorteDelCalendarioYNoConElReloj()
        {
            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(2, creadas.Count, "REI → ALG y ALG → ALC (ALC → ALG es el martes)");
            foreach (var creada in creadas)
            {
                Assert.AreEqual(CORTE_LUNES, creada.Corte, "El instante del calendario, no las 10:00:03 del reloj");
                Assert.AreEqual(HerramientasReposicion.AUTOMATICO, creada.Peticion.Herramienta);
                Assert.IsNull(creada.Peticion.Lineas, "Sin líneas: las calcula la propuesta con el corte");
                Assert.AreEqual("1", creada.Peticion.Empresa);
                Assert.IsTrue(creada.Usuario.IsInRoleSinDominio("Almacén"), "Tiene que poder escribir en cualquier origen, Algete incluido");
                Assert.AreEqual(ServicioReposicionAutomatica.USUARIO_AUTOMATICO, creada.Usuario.Identity.Name);
            }
            Assert.IsTrue(resultados.All(r => r.Resultado == ResultadosReposicionAutomatica.CREADA));
            Assert.AreEqual(80999, resultados.Single(r => r.Origen == "ALG").NumTraspaso);
            Assert.AreEqual(2, resultados.Single(r => r.Origen == "REI").Lineas);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public async Task Job_AntesDelCorte_NoHaceNada()
        {
            ahora = LUNES.AddHours(9).AddMinutes(59).AddSeconds(59);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, creadas.Count);
            Assert.AreEqual(0, resultados.Count);
        }

        [TestMethod]
        public async Task Job_ComprobarYCrear_VanDentroDelBloqueoDelOrigenYDelDia()
        {
            repositorio.Calendario.Clear();
            repositorio.Calendario.Add(Fila("REI", "ALG", 1));

            _ = await Servicio().RellenarPendientes();

            CollectionAssert.AreEqual(new[]
            {
                "bloquear 1 REI 19/10/2026",
                "¿hay? REI→ALG 19/10/2026 10:00 contando omitidas",
                "crear REI→ALG",
                "soltar 1 REI 19/10/2026"
            }, repositorio.Pasos);
        }

        [TestMethod]
        public async Task Job_YaRellenadaParaEseCorte_NoLaVuelveACrear()
        {
            repositorio.Hechas.Add("REI→ALG 19/10/2026 10:00");

            _ = await Servicio().RellenarPendientes();

            Assert.IsFalse(creadas.Any(c => c.Peticion.Origen == "REI"));
            Assert.IsTrue(creadas.Any(c => c.Peticion.Origen == "ALG"));
        }

        [TestMethod]
        public async Task Job_FestivoEnElOrigen_NoRellena()
        {
            festivos.Add("REI|20261019");

            _ = await Servicio().RellenarPendientes();

            Assert.IsFalse(creadas.Any(c => c.Peticion.Origen == "REI"));
        }

        [TestMethod]
        public async Task Job_PropuestaVacia_SeApuntaParaNoReintentarYSinAviso()
        {
            fallo = p => p.Origen == "REI" ? new ReposicionVaciaException("No hay nada que reponer de REI a ALG.") : null;

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.VACIA, resultados.Single(r => r.Origen == "REI").Resultado);
            CabeceraReposicionTraspaso apuntada = repositorio.Omitidas.Single().Cabecera;
            Assert.AreEqual("REI", apuntada.Origen);
            Assert.AreEqual(CORTE_LUNES, apuntada.FechaCorte);
            Assert.AreEqual(HerramientasReposicion.AUTOMATICO, apuntada.Herramienta);
            Assert.IsNull(apuntada.NumTraspaso);
            StringAssert.Contains(repositorio.Omitidas.Single().Motivo, "No hay nada que reponer");
            Assert.AreEqual(0, avisos.Count, "Que no haya nada que mandar es normal");
        }

        [TestMethod]
        public async Task Job_LaTiendaYaTieneUnaEnPreparacionAMano_NoCreaOtra_SeApuntaYSeAvisa()
        {
            fallo = p => p.Origen == "REI"
                ? new ReposicionYaEnPreparacionException("Ya hay una reposición en preparación de REI a ALG con 4 líneas: termínala antes de crear otra.")
                : null;

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.YA_EN_PREPARACION, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.AreEqual(1, repositorio.Omitidas.Count);
            StringAssert.Contains(avisos.Single().Message, "REI → ALG");
            StringAssert.Contains(avisos.Single().Message, "Ya hay una reposición en preparación");
            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single(r => r.Origen == "ALG").Resultado);
        }

        [TestMethod]
        public async Task Job_ErrorDeNegocio_SeApuntaSeAvisaYSigueConLasDemas()
        {
            fallo = p => p.Origen == "REI"
                ? new NestoBusinessException("El almacén REI tiene un inventario en curso") { StatusCode = HttpStatusCode.Conflict }
                : null;

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.NO_SE_PUEDE, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.AreEqual(1, repositorio.Omitidas.Count);
            Assert.AreEqual(1, avisos.Count);
            Assert.IsTrue(creadas.Any(c => c.Peticion.Origen == "ALG"), "Sigue con las demás rutas");
        }

        [TestMethod]
        public async Task Job_ExcepcionInesperada_AvisaNoApuntaParaReintentarYSigueConLasDemas()
        {
            fallo = p => p.Origen == "REI" ? new InvalidOperationException("deadlock") : null;

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.ERROR, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.AreEqual(0, repositorio.Omitidas.Count, "Sin marca: la siguiente pasada lo reintenta");
            Assert.AreEqual(1, avisos.Count);
            Assert.IsInstanceOfType(avisos.Single().InnerException, typeof(InvalidOperationException));
            Assert.IsTrue(creadas.Any(c => c.Peticion.Origen == "ALG"));
            CollectionAssert.Contains(repositorio.Pasos, "soltar 1 REI 19/10/2026", "El bloqueo se suelta aunque falle");
        }

        [TestMethod]
        public async Task Job_PasadaLaLlegadaHabitualSinRellenar_YaNoLaRellena_SeApuntaYSeAvisa()
        {
            ahora = LUNES.AddHours(13).AddMinutes(30);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, creadas.Count);
            Assert.IsTrue(resultados.All(r => r.Resultado == ResultadosReposicionAutomatica.FUERA_DE_PLAZO));
            Assert.AreEqual(2, repositorio.Omitidas.Count);
            Assert.AreEqual(2, avisos.Count);
        }

        [TestMethod]
        public async Task Job_SinLaTablaDeCabeceras_NoHaceNada()
        {
            // Sin ReposicionesTraspasos no hay forma de saber si ya está hecha: crearía una cada 5 minutos
            repositorio.TablaLista = false;

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, creadas.Count);
            Assert.AreEqual(0, resultados.Count);
        }

        // ---------------------------------------------------------------- endpoint manual

        [TestMethod]
        public async Task Relanzar_UsaElCorteDeHoyYApuntaQuienLoLanza()
        {
            ahora = LUNES.AddHours(11);
            repositorio.HechasOmitidas.Add("REI→ALG 19/10/2026 10:00"); // la propuesta salió vacía a las 10:00

            ResultadoReposicionAutomaticaDTO resultado = await Servicio().RellenarRuta("1", "rei", "ALG", Usuario("NUEVAVISION\\Alfredo", "Almacén"));

            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultado.Resultado);
            Assert.AreEqual(CORTE_LUNES, creadas.Single().Corte);
            Assert.AreEqual(HerramientasReposicion.AUTOMATICO, creadas.Single().Peticion.Herramienta);
            Assert.AreEqual("NUEVAVISION\\Alfredo", creadas.Single().Usuario.Identity.Name);
            Assert.IsTrue(creadas.Single().Usuario.IsInRoleSinDominio("Almacén"));
            CollectionAssert.Contains(repositorio.Pasos, "¿hay? REI→ALG 19/10/2026 10:00 sin contar omitidas");
        }

        [TestMethod]
        public async Task Relanzar_YaRellenada_409()
        {
            ahora = LUNES.AddHours(11);
            repositorio.Hechas.Add("REI→ALG 19/10/2026 10:00");

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => Servicio().RellenarRuta("1", "REI", "ALG", Usuario("Carlos", "Informática")));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual(0, creadas.Count);
        }

        [TestMethod]
        public async Task Relanzar_AntesDelCorte_409()
        {
            ahora = LUNES.AddHours(9);

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => Servicio().RellenarRuta("1", "REI", "ALG", Usuario("Carlos", "Informática")));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "10:00");
        }

        [TestMethod]
        public async Task Relanzar_HoyNoTocaEsaRuta_404()
        {
            ahora = LUNES.AddHours(11);

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => Servicio().RellenarRuta("1", "ALC", "ALG", Usuario("Carlos", "Informática")));

            Assert.AreEqual(HttpStatusCode.NotFound, error.StatusCode);
        }

        [TestMethod]
        public async Task Relanzar_LaTiendaYaTieneUnaEnPreparacion_EsUn409()
        {
            ahora = LUNES.AddHours(11);
            fallo = p => new ReposicionYaEnPreparacionException("Ya hay una reposición en preparación de REI a ALG con 4 líneas");

            _ = await Assert.ThrowsExceptionAsync<ReposicionYaEnPreparacionException>(
                () => Servicio().RellenarRuta("1", "REI", "ALG", Usuario("Carlos", "Informática")));
        }

        [TestMethod]
        public async Task Relanzar_Vacia_LoDiceYLoApunta()
        {
            ahora = LUNES.AddHours(11);
            fallo = p => new ReposicionVaciaException("No hay nada que reponer de REI a ALG.");

            ResultadoReposicionAutomaticaDTO resultado = await Servicio().RellenarRuta("1", "REI", "ALG", Usuario("Carlos", "Informática"));

            Assert.AreEqual(ResultadosReposicionAutomatica.VACIA, resultado.Resultado);
            StringAssert.Contains(resultado.Mensaje, "No hay nada que reponer");
            Assert.AreEqual(1, repositorio.Omitidas.Count);
        }

        [TestMethod]
        public async Task Controlador_Relanzar_SoloAlmacenDireccionEInformatica()
        {
            IServicioReposicionAutomatica servicio = A.Fake<IServicioReposicionAutomatica>();
            var controlador = new NestoAPI.Controllers.ReposicionesController(null, null, null, null, servicio)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                User = Usuario("NUEVAVISION\\Paloma", "Tiendas")
            };

            var respuesta = await controlador.PostRellenarAutomatica("REI", "ALG") as System.Web.Http.Results.ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.Response.StatusCode);
            A.CallTo(servicio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_Relanzar_ConPermiso_DevuelveElResultado()
        {
            IServicioReposicionAutomatica servicio = A.Fake<IServicioReposicionAutomatica>();
            IPrincipal alfredo = Usuario("NUEVAVISION\\Alfredo", "Almacén");
            var esperado = new ResultadoReposicionAutomaticaDTO { Origen = "REI", Destino = "ALG", Resultado = ResultadosReposicionAutomatica.CREADA };
            A.CallTo(() => servicio.RellenarRuta("1", "REI", "ALG", alfredo)).Returns(esperado);
            var controlador = new NestoAPI.Controllers.ReposicionesController(null, null, null, null, servicio)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                User = alfredo
            };

            var respuesta = await controlador.PostRellenarAutomatica("REI", "ALG") as System.Web.Http.Results.OkNegotiatedContentResult<ResultadoReposicionAutomaticaDTO>;

            Assert.AreSame(esperado, respuesta.Content);
        }

        // ---------------------------------------------------------------- NestoAPI#577: corte ya cubierto por otro traspaso

        /// <summary>
        /// Caso real del 09/10/26: Andre rellenó ALG → REI desde Nesto viejo a las 13:55 (traspaso 80929, sin cabecera en
        /// ReposicionesTraspasos) y el job, con el corte de las 13:00, creó otra (80932). Aquí, con REI → ALG del lunes.
        /// </summary>
        [TestMethod]
        public async Task Job_LaRutaYaTieneUnTraspasoCreadoDespuesDelCorte_SeApuntaOmitidaSinCrearNiAvisar()
        {
            repositorio.Existentes.Add(("REI", "ALG", new TraspasoReposicionExistente
            {
                NumTraspaso = 80929, Usuario = "NUEVAVISION\\Andre", Fecha = CORTE_LUNES.AddMinutes(1)
            }));
            ahora = CORTE_LUNES.AddMinutes(5);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            ResultadoReposicionAutomaticaDTO rei = resultados.Single(r => r.Origen == "REI");
            Assert.AreEqual(ResultadosReposicionAutomatica.YA_CUBIERTA, rei.Resultado);
            Assert.AreEqual("Ya la rellenó Andre el 19/10 10:01 (traspaso 80929)", rei.Mensaje);
            Assert.IsFalse(creadas.Any(c => c.Peticion.Origen == "REI"), "No se crea otra");
            CollectionAssert.DoesNotContain(repositorio.Pasos, "crear REI→ALG");
            (CabeceraReposicionTraspaso cabecera, string motivo) = repositorio.Omitidas.Single();
            Assert.AreEqual("REI", cabecera.Origen);
            Assert.AreEqual(CORTE_LUNES, cabecera.FechaCorte);
            Assert.AreEqual("Ya la rellenó Andre el 19/10 10:01 (traspaso 80929)", motivo);
            Assert.AreEqual(0, avisos.Count, "Que alguien ya la haya hecho es normal: sin aviso a ELMAH");
            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single(r => r.Origen == "ALG").Resultado, "Las demás rutas, como siempre");
        }

        [TestMethod]
        public async Task Job_LaRutaTieneUnTraspasoCreadoAntesDelCorte_SeRellenaComoSiempre()
        {
            repositorio.Existentes.Add(("REI", "ALG", new TraspasoReposicionExistente
            {
                NumTraspaso = 80931, Usuario = "NUEVAVISION\\Reina", Fecha = CORTE_LUNES.AddMinutes(-35)
            }));

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.IsTrue(creadas.Any(c => c.Peticion.Origen == "REI" && c.Corte == CORTE_LUNES));
            Assert.AreEqual(0, repositorio.Omitidas.Count);
        }

        [TestMethod]
        public async Task Job_TraspasoDeOtraRutaDelMismoOrigenDespuesDelCorte_NoLaCubre()
        {
            repositorio.Existentes.Add(("ALG", "REI", new TraspasoReposicionExistente
            {
                NumTraspaso = 80929, Usuario = "NUEVAVISION\\Andre", Fecha = CORTE_LUNES.AddMinutes(1)
            }));
            ahora = CORTE_LUNES.AddMinutes(5);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single(r => r.Origen == "ALG" && r.Destino == "ALC").Resultado);
            Assert.IsTrue(creadas.Any(c => c.Peticion.Origen == "ALG" && c.Peticion.Destino == "ALC"));
        }

        [TestMethod]
        public async Task Job_TraspasoDeOtraRutaDelMismoOrigenEnPreparacion_SigueSaltandoYaEnPreparacion()
        {
            repositorio.Existentes.Add(("ALG", "REI", new TraspasoReposicionExistente
            {
                NumTraspaso = null, Usuario = "NUEVAVISION\\Andre", Fecha = CORTE_LUNES.AddMinutes(1)
            }));
            fallo = p => p.Origen == "ALG"
                ? new ReposicionYaEnPreparacionException("Ya hay una reposición en preparación de ALG a REI con 62 líneas: termínala antes de crear otra.")
                : null;
            ahora = CORTE_LUNES.AddMinutes(5);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.YA_EN_PREPARACION, resultados.Single(r => r.Origen == "ALG").Resultado);
            StringAssert.Contains(avisos.Single().Message, "ALG → ALC");
        }

        [TestMethod]
        public async Task Job_CubiertaYFueraDePlazo_GanaCubiertaSinAviso()
        {
            repositorio.Existentes.Add(("REI", "ALG", new TraspasoReposicionExistente
            {
                NumTraspaso = 80929, Usuario = "Andre", Fecha = CORTE_LUNES.AddMinutes(1)
            }));
            ahora = LUNES.AddHours(14);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.YA_CUBIERTA, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.IsFalse(avisos.Any(a => a.Message.Contains("REI → ALG")));
        }

        [TestMethod]
        public void MotivoCubierta_EnPreparacionYSinUsuario()
        {
            Assert.AreEqual("Ya la rellenó Paloma el 19/10 10:01 (en preparación, aún sin número de traspaso)",
                ServicioReposicionAutomatica.MotivoCubierta(new TraspasoReposicionExistente { Usuario = "NUEVAVISION\\Paloma", Fecha = CORTE_LUNES.AddMinutes(1) }));
            Assert.AreEqual("Ya se rellenó el 19/10 10:01 (traspaso 80919)",
                ServicioReposicionAutomatica.MotivoCubierta(new TraspasoReposicionExistente { NumTraspaso = 80919, Fecha = CORTE_LUNES.AddMinutes(1) }));
            string largo = ServicioReposicionAutomatica.MotivoCubierta(new TraspasoReposicionExistente { NumTraspaso = 1, Usuario = new string('x', 400), Fecha = CORTE_LUNES });
            Assert.AreEqual(300, largo.Length);
        }

        [TestMethod]
        public void Sql_TraspasoQueCubre_LasTresFuentesPorRutaYCreadoDespuesDelCorte()
        {
            string sql = RepositorioReposicionAutomaticaSql.SQL_TRASPASO_QUE_CUBRE;
            StringAssert.Contains(sql, "r.Origen = @p1 AND r.Destino = @p2 AND r.Omitida IS NULL AND r.FechaCreacion >= @p3");
            StringAssert.Contains(sql, "p.Diario IN (@salida, @entrada) AND p.[Almacén] = @p2 AND p.Texto = @p4");
            StringAssert.Contains(sql, "HAVING MIN(p.[Fecha Modificación]) >= @p3", "En PreExtrProducto, Fecha se pisa al terminar: cuenta cuándo se insertaron las líneas");
            StringAssert.Contains(sql, "e.Diario = @entrada AND e.Fecha >= @p3", "ExtractoProducto, siempre acotado por diario y fecha");
            Assert.AreEqual("Traspaso por reposición de almacén ALG a REI", ServicioPreparacionReposicion.Texto("ALG", "REI"),
                "El texto que ponen Nesto viejo y la API (y por el que se reconocen las líneas)");
        }

        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_LaCabeceraDelCorteEsLaDelJobDeEsaRutaYEseInstante()
        {
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_HAY_CABECERA, "Herramienta = 'Automatico'");
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_HAY_CABECERA, "FechaCorte = @p3");
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_HAY_CABECERA, "(@p4 = 1 OR Omitida IS NULL)");
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_BLOQUEAR, "@LockOwner = 'Session'");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA, "a.Omitida IS NULL",
                "Una marca del job sin reposición no es la cabecera abierta de la tienda");
            Assert.AreEqual("ReposicionAutomatica:1:REI:20261019", RepositorioReposicionAutomaticaSql.Recurso("1", "REI", LUNES.AddHours(10)));
        }

        // ---------------------------------------------------------------- repositorio en memoria

        private class RepositorioEnMemoria : IRepositorioReposicionAutomatica
        {
            public bool TablaLista = true;
            public readonly List<ReposicionCalendario> Calendario = new List<ReposicionCalendario>();
            public readonly List<string> Pasos = new List<string>();
            /// <summary>"REI→ALG 19/10/2026 10:00": cabeceras del job con reposición.</summary>
            public readonly HashSet<string> Hechas = new HashSet<string>();
            /// <summary>Ídem, marcas sin reposición (vacía, ya había una…).</summary>
            public readonly HashSet<string> HechasOmitidas = new HashSet<string>();
            public readonly List<(CabeceraReposicionTraspaso Cabecera, string Motivo)> Omitidas = new List<(CabeceraReposicionTraspaso, string)>();
            /// <summary>Traspasos de reposición ya hechos (cualquier herramienta), por ruta.</summary>
            public readonly List<(string Origen, string Destino, TraspasoReposicionExistente Traspaso)> Existentes = new List<(string, string, TraspasoReposicionExistente)>();

            public Task<bool> TablaPreparada() => Task.FromResult(TablaLista);

            public Task<List<ReposicionCalendario>> LeerCalendario(string empresa) => Task.FromResult(Calendario.ToList());

            public Task<bool> HayCabecera(string empresa, string origen, string destino, DateTime corte, bool contarOmitidas)
            {
                string clave = $"{origen}→{destino} {corte:dd/MM/yyyy HH:mm}";
                Pasos.Add($"¿hay? {clave} {(contarOmitidas ? "contando omitidas" : "sin contar omitidas")}");
                return Task.FromResult(Hechas.Contains(clave) || (contarOmitidas && HechasOmitidas.Contains(clave)));
            }

            // Sin paso: lo mismo que el SQL (misma ruta, creado en el corte o después, el más antiguo)
            public Task<TraspasoReposicionExistente> BuscarTraspasoQueCubre(string empresa, string origen, string destino, DateTime corte)
            {
                return Task.FromResult(Existentes
                    .Where(e => e.Origen == origen && e.Destino == destino && e.Traspaso.Fecha >= corte)
                    .Select(e => e.Traspaso)
                    .OrderBy(t => t.Fecha)
                    .FirstOrDefault());
            }

            public Task ApuntarOmitida(CabeceraReposicionTraspaso cabecera, string motivo)
            {
                Pasos.Add($"omitida {cabecera.Origen}→{cabecera.Destino}");
                Omitidas.Add((cabecera, motivo));
                return Task.CompletedTask;
            }

            public Task<IDisposable> Bloquear(string empresa, string origen, DateTime dia)
            {
                Pasos.Add($"bloquear {empresa} {origen} {dia:dd/MM/yyyy}");
                return Task.FromResult<IDisposable>(new Soltar(() => Pasos.Add($"soltar {empresa} {origen} {dia:dd/MM/yyyy}")));
            }

            private sealed class Soltar : IDisposable
            {
                private readonly Action accion;
                public Soltar(Action accion) { this.accion = accion; }
                public void Dispose() => accion();
            }
        }
    }
}
