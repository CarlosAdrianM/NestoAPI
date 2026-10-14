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
                "¿hay? ALG→REI 16/10/2026 contando omitidas",
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
                "¿hay? REI→ALG 19/10/2026 contando omitidas",
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

        // ---------------------------------------------------------------- NestoAPI#577: cambiar el calendario el mismo día

        /// <summary>
        /// 09/10/26: con el calendario editable desde Nesto, cambiar hoy la hora de cierre de una ruta ya rellenada no puede
        /// hacer que el job vea un corte nuevo (sin cabecera) y la rellene otra vez o avise de fuera de plazo.
        /// </summary>
        [TestMethod]
        public async Task Job_CambiarHoyLaHoraDeCierreAMasTardeDeUnaRutaYaRellenada_NoRellenaOtraNiAvisa()
        {
            repositorio.Hechas.Add("REI→ALG 19/10/2026 10:00");
            repositorio.Calendario.Single(f => f.AlmacenOrigen == "REI").HoraCierre = TimeSpan.Parse("11:00");
            ahora = LUNES.AddHours(11).AddMinutes(5);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.IsFalse(creadas.Any(c => c.Peticion.Origen == "REI"), "Ya se rellenó a las 10:00: la hora nueva vale desde el siguiente viaje");
            Assert.IsFalse(resultados.Any(r => r.Origen == "REI"));
            Assert.AreEqual(0, repositorio.Omitidas.Count);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public async Task Job_CambiarHoyLaHoraDeCierreAMasTempranoDeUnaRutaYaTratada_NoApuntaNadaNuevo()
        {
            repositorio.HechasOmitidas.Add("REI→ALG 19/10/2026 10:00"); // salió vacía a las 10:00
            repositorio.Calendario.Single(f => f.AlmacenOrigen == "REI").HoraCierre = TimeSpan.Parse("09:00");
            ahora = LUNES.AddHours(10).AddMinutes(30);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.IsFalse(creadas.Any(c => c.Peticion.Origen == "REI"));
            Assert.IsFalse(resultados.Any(r => r.Origen == "REI"));
            Assert.AreEqual(0, repositorio.Omitidas.Count);
        }

        [TestMethod]
        public async Task Job_CambiarHoyLaHoraDeUnaRutaYaRellenada_PasadaLaLlegadaNueva_NoAvisaDeFueraDePlazo()
        {
            repositorio.Hechas.Add("REI→ALG 19/10/2026 10:00");
            ReposicionCalendario rei = repositorio.Calendario.Single(f => f.AlmacenOrigen == "REI");
            rei.HoraCierre = TimeSpan.Parse("11:00");
            rei.HoraLlegadaHabitual = TimeSpan.Parse("12:00");
            ahora = LUNES.AddHours(12).AddMinutes(30);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.IsFalse(resultados.Any(r => r.Origen == "REI"));
            Assert.IsFalse(avisos.Any(a => a.Message.Contains("REI → ALG")));
        }

        [TestMethod]
        public async Task Job_RutaDeHoySinRellenarConLaHoraCambiada_SeRellenaALaHoraNueva()
        {
            repositorio.Calendario.Single(f => f.AlmacenOrigen == "REI").HoraCierre = TimeSpan.Parse("11:00");
            ahora = LUNES.AddHours(10).AddMinutes(30);

            _ = await Servicio().RellenarPendientes();
            Assert.IsFalse(creadas.Any(c => c.Peticion.Origen == "REI"), "A las 10:30 aún no ha llegado la hora nueva");

            ahora = LUNES.AddHours(11).AddSeconds(3);
            _ = await Servicio().RellenarPendientes();

            Assert.AreEqual(LUNES.AddHours(11), creadas.Single(c => c.Peticion.Origen == "REI").Corte);
        }

        [TestMethod]
        public async Task Job_ConAntelacion_ElViernesRellenadaYElLunesConLaHoraCambiada_NoLaDuplica()
        {
            SoloAlgeteReinaDelLunes();
            repositorio.Hechas.Add("ALG→REI 16/10/2026 13:00");
            repositorio.Calendario.Single().HoraCierre = TimeSpan.Parse("14:00");
            ahora = LUNES.AddHours(6);

            List<ResultadoReposicionAutomaticaDTO> resultados = await Servicio().RellenarPendientes();

            Assert.AreEqual(0, creadas.Count, "La del lunes es el viaje que se cerró el viernes, aunque ahora cierre a las 14:00");
            Assert.AreEqual(0, resultados.Count);
        }

        [TestMethod]
        public async Task Relanzar_ConLaHoraCambiadaDespuesDeRellenarla_409()
        {
            repositorio.Hechas.Add("REI→ALG 19/10/2026 10:00");
            repositorio.Calendario.Single(f => f.AlmacenOrigen == "REI").HoraCierre = TimeSpan.Parse("11:00");
            ahora = LUNES.AddHours(11).AddMinutes(5);

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => Servicio().RellenarRuta("1", "REI", "ALG", Usuario("Carlos", "Informática")));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual(0, creadas.Count);
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
            CollectionAssert.Contains(repositorio.Pasos, "¿hay? REI→ALG 19/10/2026 sin contar omitidas");
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
        public void Sql_LaCabeceraDelCorteEsLaDelJobDeEsaRutaYEseDia()
        {
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_HAY_CABECERA, "Herramienta = 'Automatico'");
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_HAY_CABECERA, "FechaCorte >= @p3 AND FechaCorte < DATEADD(day, 1, @p3)",
                "El día del corte entero: cambiar la hora de cierre no hace un corte nuevo");
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_HAY_CABECERA, "(@p4 = 1 OR Omitida IS NULL)");
            StringAssert.Contains(RepositorioReposicionAutomaticaSql.SQL_BLOQUEAR, "@LockOwner = 'Session'");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA, "a.Omitida IS NULL",
                "Una marca del job sin reposición no es la cabecera abierta de la tienda");
            Assert.AreEqual("ReposicionAutomatica:1:REI:20261019", RepositorioReposicionAutomaticaSql.Recurso("1", "REI", LUNES.AddHours(10)));
        }

        // ---------------------------------------------------------------- NestoAPI#622: preparación sin enviar a la hora de corte

        private List<(CrearReposicionDTO Peticion, IPrincipal Usuario, DateTime Corte)> completadas;
        private Func<CrearReposicionDTO, ResultadoCompletarReposicionDTO> completar;
        private Func<CrearReposicionDTO, Exception> falloAlCompletar;
        private List<(string Empresa, string Origen, string Titulo, string Cuerpo)> avisosTienda;
        private Exception falloAlAvisarTienda;

        /// <summary>Con la parte de #622: completar la preparación sin enviar y avisar a la tienda.</summary>
        private ServicioReposicionAutomatica ServicioQueCompleta()
        {
            completadas = new List<(CrearReposicionDTO, IPrincipal, DateTime)>();
            avisosTienda = avisosTienda ?? new List<(string, string, string, string)>();
            var avisador = A.Fake<IAvisadorReposicionAutomatica>();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string empresa, string origen, string titulo, string cuerpo) =>
                {
                    if (falloAlAvisarTienda != null)
                    {
                        return Task.FromException(falloAlAvisarTienda);
                    }
                    avisosTienda.Add((empresa, origen, titulo, cuerpo));
                    return Task.CompletedTask;
                });
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
                    return Task.FromResult(new ReposicionEnPreparacionDTO { Origen = peticion.Origen, Destino = peticion.Destino });
                },
                new CalculadoraFechaReposicion((dia, almacen) => festivos.Contains($"{almacen}|{dia:yyyyMMdd}")),
                () => ahora,
                ex => avisos.Add(ex),
                (peticion, usuario, corte) =>
                {
                    repositorio.Pasos.Add($"completar {peticion.Origen}→{peticion.Destino}");
                    Exception ex = falloAlCompletar?.Invoke(peticion);
                    if (ex != null)
                    {
                        return Task.FromException<ResultadoCompletarReposicionDTO>(ex);
                    }
                    completadas.Add((peticion, usuario, corte));
                    return Task.FromResult(completar(peticion));
                },
                avisador);
        }

        private static ReposicionYaEnPreparacionException YaEnPreparacion(string origen, string destino)
        {
            return new ReposicionYaEnPreparacionException($"Ya hay una reposición en preparación de {origen} a {destino} con 8 líneas: termínala antes de crear otra.");
        }

        [TestMethod]
        public async Task Job_TiendaConPreparacionSinEnviar_SeCompletaEnVezDeOmitirse_YSeAvisaALaTienda()
        {
            // Caso real (09/10/26): el viernes el job rellenó REI → ALG y no se envió; el lunes a la hora de corte se omitía
            fallo = p => p.Origen == "REI" ? YaEnPreparacion("REI", "ALG") : null;
            completar = p => new ResultadoCompletarReposicionDTO
            {
                Completada = true, LineasNuevas = 3, LineasSubidas = 2, PreparacionDesde = VIERNES_ANTERIOR,
                Reposicion = new ReposicionEnPreparacionDTO { Origen = "REI", Destino = "ALG" }
            };

            List<ResultadoReposicionAutomaticaDTO> resultados = await ServicioQueCompleta().RellenarPendientes();

            ResultadoReposicionAutomaticaDTO rei = resultados.Single(r => r.Origen == "REI");
            Assert.AreEqual(ResultadosReposicionAutomatica.COMPLETADA, rei.Resultado);
            Assert.AreEqual(5, rei.Lineas);
            Assert.AreEqual(CORTE_LUNES, completadas.Single().Corte, "Con el corte de hoy, no con el reloj");
            Assert.AreEqual(HerramientasReposicion.AUTOMATICO, completadas.Single().Peticion.Herramienta);
            Assert.AreEqual(0, repositorio.Omitidas.Count, "No se apunta como omitida: la cabecera la escribe Completar");
            Assert.AreEqual(0, avisos.Count, "Completarla es normal: nada a ELMAH");
            CollectionAssert.AreEqual(new[]
            {
                "bloquear 1 REI 19/10/2026",
                "¿hay? REI→ALG 19/10/2026 contando omitidas",
                "crear REI→ALG",
                "completar REI→ALG",
                "soltar 1 REI 19/10/2026"
            }, repositorio.Pasos.Where(p => p.Contains("REI")).ToList(), "Todo dentro del bloqueo del origen");
            var aviso = avisosTienda.Single();
            Assert.AreEqual("REI", aviso.Origen);
            StringAssert.Contains(aviso.Titulo, "sin enviar");
            Assert.AreEqual("La reposición a ALG (corte de hoy a las 10:00) se ha sumado a la que tenías sin enviar del 16/10: " +
                "3 productos nuevos y 2 productos con más cantidad. Envíala desde «Enviar reposición».", aviso.Cuerpo);
            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single(r => r.Origen == "ALG").Resultado);
        }

        [TestMethod]
        public async Task Job_PreparacionSinEnviarQueNoSePuedeCompletar_SeOmiteComoAntes_YSeAvisaALaTienda()
        {
            // Hacia otro destino (o en Algete, de Nesto viejo): Completar vuelve a dar «ya en preparación»
            fallo = p => p.Origen == "ALG" ? YaEnPreparacion("ALG", "REI") : null;
            falloAlCompletar = p => new ReposicionYaEnPreparacionException("Ya hay una reposición en preparación de ALG a REI con 62 líneas: " +
                "la de ALC no se puede sumar a ella. Termínala para que se pueda rellenar la de ALC.");
            completar = p => null;

            List<ResultadoReposicionAutomaticaDTO> resultados = await ServicioQueCompleta().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.YA_EN_PREPARACION, resultados.Single(r => r.Origen == "ALG").Resultado);
            StringAssert.Contains(repositorio.Omitidas.Single().Motivo, "no se puede sumar");
            StringAssert.Contains(avisos.Single().Message, "ALG → ALC");
            var aviso = avisosTienda.Single();
            Assert.AreEqual("ALG", aviso.Origen);
            Assert.AreEqual("La reposición a ALC no se ha podido rellenar", aviso.Titulo);
            StringAssert.Contains(aviso.Cuerpo, "La reposición a ALC (corte de hoy a las 10:00) no se ha rellenado sola: Ya hay una reposición en preparación de ALG a REI");
        }

        [TestMethod]
        public async Task Job_SiAlCompletarYaSeHabiaEnviado_EsUnaCreadaNormalSinAviso()
        {
            fallo = p => p.Origen == "REI" ? YaEnPreparacion("REI", "ALG") : null;
            completar = p => new ResultadoCompletarReposicionDTO
            {
                Completada = false, LineasNuevas = 4,
                Reposicion = new ReposicionEnPreparacionDTO
                {
                    Origen = "REI", Destino = "ALG",
                    Lineas = Enumerable.Range(0, 4).Select(i => new LineaReposicionEnPreparacionDTO()).ToList()
                }
            };

            List<ResultadoReposicionAutomaticaDTO> resultados = await ServicioQueCompleta().RellenarPendientes();

            ResultadoReposicionAutomaticaDTO rei = resultados.Single(r => r.Origen == "REI");
            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, rei.Resultado);
            Assert.AreEqual(4, rei.Lineas);
            Assert.AreEqual(0, avisosTienda.Count);
        }

        [TestMethod]
        public async Task Job_NoSePuedeRellenar_AvisaALaTienda_YVaciaNo()
        {
            fallo = p => p.Origen == "REI"
                ? new NestoBusinessException("El almacén REI tiene un inventario en curso: termínalo antes de crear una reposición.") { StatusCode = HttpStatusCode.Conflict }
                : new ReposicionVaciaException("No hay nada que reponer de ALG a ALC.");
            completar = p => null;

            List<ResultadoReposicionAutomaticaDTO> resultados = await ServicioQueCompleta().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.NO_SE_PUEDE, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.AreEqual(ResultadosReposicionAutomatica.VACIA, resultados.Single(r => r.Origen == "ALG").Resultado);
            var aviso = avisosTienda.Single();
            Assert.AreEqual("REI", aviso.Origen, "Vacía es normal: no va a la campana");
            StringAssert.Contains(aviso.Cuerpo, "inventario en curso");
        }

        [TestMethod]
        public async Task Job_FueraDePlazo_AvisaALaTienda()
        {
            ahora = LUNES.AddHours(13).AddMinutes(31);
            completar = p => null;

            _ = await ServicioQueCompleta().RellenarPendientes();

            Assert.IsTrue(avisosTienda.Any(a => a.Origen == "REI" && a.Cuerpo.Contains("ese viaje ya ha pasado")));
        }

        [TestMethod]
        public async Task Job_SiFallaElAvisoALaTienda_SigueYLoDejaEnElmah()
        {
            fallo = p => p.Origen == "REI" ? YaEnPreparacion("REI", "ALG") : null;
            completar = p => new ResultadoCompletarReposicionDTO { Completada = true, PreparacionDesde = VIERNES_ANTERIOR };
            falloAlAvisarTienda = new InvalidOperationException("Sin conexión con el buzón");

            List<ResultadoReposicionAutomaticaDTO> resultados = await ServicioQueCompleta().RellenarPendientes();

            Assert.AreEqual(ResultadosReposicionAutomatica.COMPLETADA, resultados.Single(r => r.Origen == "REI").Resultado);
            Assert.AreEqual(ResultadosReposicionAutomatica.CREADA, resultados.Single(r => r.Origen == "ALG").Resultado);
            StringAssert.Contains(avisos.Single().Message, "No se ha podido avisar a la tienda");
        }

        [TestMethod]
        public async Task Relanzar_ConPreparacionSinEnviar_LaCompletaComoElJob()
        {
            ahora = LUNES.AddHours(11);
            fallo = p => YaEnPreparacion("REI", "ALG");
            completar = p => new ResultadoCompletarReposicionDTO { Completada = true, LineasNuevas = 1, PreparacionDesde = VIERNES_ANTERIOR };

            ResultadoReposicionAutomaticaDTO resultado = await ServicioQueCompleta().RellenarRuta("1", "REI", "ALG", Usuario("Carlos", "Informática"));

            Assert.AreEqual(ResultadosReposicionAutomatica.COMPLETADA, resultado.Resultado);
            Assert.AreEqual("Carlos", completadas.Single().Usuario.Identity.Name);
            Assert.AreEqual(1, avisosTienda.Count);
        }

        [TestMethod]
        public void TextoCompletada_SinNadaNuevo_LoDice()
        {
            string texto = ServicioReposicionAutomatica.TextoCompletada("ALG", CORTE_LUNES, VIERNES_ANTERIOR, 0, 0, LUNES.AddHours(11));

            Assert.AreEqual("La reposición a ALG (corte de hoy a las 10:00) no tenía nada nuevo: sigue pendiente la que tenías sin enviar del 16/10. " +
                "Envíala desde «Enviar reposición».", texto);
        }

        [TestMethod]
        public async Task Avisos_DiceLasOmitidasYLasCompletadasSinEnviar_YNadaDeLasNormalesNiDeLasYaEnviadas()
        {
            ahora = LUNES.AddHours(11);
            repositorio.CabecerasDelDia.AddRange(new[]
            {
                // ALC → ALG: vacía
                new CabeceraReposicionAutomaticaDelDia { Id = 1, Destino = "ALG", FechaCorte = CORTE_LUNES, Omitida = "No hay nada que reponer de ALC a ALG." },
                // ALC → REI: se sumó a la del viernes y sigue sin enviar
                new CabeceraReposicionAutomaticaDelDia { Id = 2, Destino = "REI", FechaCorte = CORTE_LUNES, PreparacionDesde = VIERNES_ANTERIOR }
            });

            List<AvisoReposicionAutomaticaDTO> avisosDeHoy = await Servicio().LeerAvisosDeHoy("1", "alc");

            Assert.AreEqual(2, avisosDeHoy.Count);
            Assert.AreEqual("La reposición a ALG (corte de hoy a las 10:00) no se ha rellenado sola: No hay nada que reponer de ALC a ALG.", avisosDeHoy[0].Texto);
            Assert.AreEqual(ServicioReposicionAutomatica.TIPO_AVISO_OMITIDA, avisosDeHoy[0].Tipo);
            Assert.AreEqual("La reposición a REI (corte de hoy a las 10:00) se ha sumado a la que tenías sin enviar del 16/10. Envíala desde «Enviar reposición».",
                avisosDeHoy[1].Texto);
            Assert.AreEqual(ServicioReposicionAutomatica.TIPO_AVISO_COMPLETADA, avisosDeHoy[1].Tipo);
            Assert.AreEqual("ALC", repositorio.OrigenAvisosLeido);
            Assert.AreEqual(LUNES, repositorio.DesdeAvisosLeido, "Las de hoy");
        }

        [TestMethod]
        public async Task Avisos_CreadaNormal_YaEnviada_OSoloConLineasDeHoy_NoDicenNada()
        {
            ahora = LUNES.AddHours(11);
            repositorio.CabecerasDelDia.AddRange(new[]
            {
                // Creada hoy, sin enviar: es la del día, nada que decir
                new CabeceraReposicionAutomaticaDelDia { Id = 1, Destino = "ALG", FechaCorte = CORTE_LUNES, PreparacionDesde = LUNES },
                // Completada pero ya enviada (con número)
                new CabeceraReposicionAutomaticaDelDia { Id = 2, Destino = "REI", FechaCorte = CORTE_LUNES, NumTraspaso = 80990, PreparacionDesde = null }
            });

            List<AvisoReposicionAutomaticaDTO> avisosDeHoy = await Servicio().LeerAvisosDeHoy("1", "ALC");

            Assert.AreEqual(0, avisosDeHoy.Count);
        }

        [TestMethod]
        public async Task Avisos_DeCadaDestinoSoloLaUltimaCabecera()
        {
            ahora = LUNES.AddHours(11);
            repositorio.CabecerasDelDia.AddRange(new[]
            {
                new CabeceraReposicionAutomaticaDelDia { Id = 1, Destino = "ALG", FechaCorte = CORTE_LUNES, Omitida = "El almacén ALC tiene un inventario en curso" },
                // Relanzada después a mano y creada
                new CabeceraReposicionAutomaticaDelDia { Id = 5, Destino = "ALG", FechaCorte = CORTE_LUNES, PreparacionDesde = LUNES }
            });

            Assert.AreEqual(0, (await Servicio().LeerAvisosDeHoy("1", "ALC")).Count);
        }

        [TestMethod]
        public async Task Avisos_SinLaTabla_ListaVacia()
        {
            repositorio.TablaLista = false;

            Assert.AreEqual(0, (await Servicio().LeerAvisosDeHoy("1", "ALC")).Count);
        }

        [TestMethod]
        public void Sql_NumerarAlTerminar_NumeraTodaLaPreparacionCompletada_YSinLaColumnaComoAntes()
        {
            string sql = RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA;
            StringAssert.Contains(sql, "COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL");
            StringAssert.Contains(sql, "(r.Id = @abierta OR r.Id = @raiz OR r.IdCompletada = @raiz)");
            StringAssert.Contains(sql, "@raiz = ISNULL(a.IdCompletada, a.Id)");
            StringAssert.Contains(sql, "EXEC sp_executesql", "Sin la columna el lote no compilaría si la nombra fuera de sp_executesql");
            string completando = RegistroReposicionesTraspasosSql.SQL_CREAR_COMPLETANDO;
            StringAssert.Contains(completando, "a.FechaCreacion >= @desde", "No se enlaza con una abierta vieja que terminó Nesto viejo");
            StringAssert.Contains(completando, "a.Omitida IS NULL");
            StringAssert.Contains(completando, "COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL");
        }

        [TestMethod]
        public void Sql_CabecerasDelDia_SoloLasDelJob_YLaPreparacionSinTraspaso()
        {
            string sql = RepositorioReposicionAutomaticaSql.SQL_CABECERAS_DEL_DIA;
            StringAssert.Contains(sql, "r.Herramienta = 'Automatico'");
            StringAssert.Contains(sql, "r.FechaCreacion >= @p2");
            StringAssert.Contains(sql, "ISNULL(p.[NºTraspaso], 0) = 0", "Lo que ya tiene traspaso está enviado: no es preparación");
        }

        [TestMethod]
        public void Script622_AnadeLaColumnaIdCompletada()
        {
            string raiz = System.IO.Path.GetDirectoryName(typeof(ServicioReposicionAutomaticaTests).Assembly.Location);
            while (raiz != null && !System.IO.Directory.Exists(System.IO.Path.Combine(raiz, "Scripts")))
            {
                raiz = System.IO.Path.GetDirectoryName(raiz);
            }
            Assert.IsNotNull(raiz, "No se encuentra la carpeta Scripts del repositorio");
            string script = System.IO.File.ReadAllText(System.IO.Path.Combine(raiz, "Scripts", "Issue622_ReposicionesTraspasos_IdCompletada.sql"));
            StringAssert.Contains(script, "ADD IdCompletada int NULL");
            StringAssert.Contains(script, "IF COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL", "Idempotente");
        }

        // ---------------------------------------------------------------- NestoAPI#622: a quién avisa

        [TestMethod]
        public async Task Avisador_DesdeUnaTienda_AvisaAQuienTieneEseAlmacenEnNesto()
        {
            var notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var pedidos = new List<string>();
            var avisador = new AvisadorReposicionAutomatica(notificaciones,
                (empresa, almacen) =>
                {
                    pedidos.Add($"{empresa}|{almacen}");
                    return Task.FromResult(new List<string> { "Paloma", "NUEVAVISION\\Eva", "paloma", "(defecto)", " " });
                },
                () => throw new InvalidOperationException("Desde una tienda no se mira el grupo"));

            await avisador.Avisar("1", "alc", "Tienes sin enviar la reposición a ALG", "La reposición a ALG…");

            CollectionAssert.AreEqual(new[] { "1|ALC" }, pedidos);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Paloma", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Tienes sin enviar la reposición a ALG" && n.Tipo == AvisadorReposicionAutomatica.TIPO_NOTIFICACION)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Eva", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task Avisador_DesdeAlgete_AvisaAlGrupoAlmacen()
        {
            var notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var avisador = new AvisadorReposicionAutomatica(notificaciones,
                (empresa, almacen) => throw new InvalidOperationException("Algete es el almacén por defecto de todos: no vale"),
                () => new List<string> { "Andre" });

            await avisador.Avisar("1", "ALG", "La reposición a REI no se ha podido rellenar", "…");

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Andre", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
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

            // Lo mismo que el SQL: misma ruta y FechaCorte en ese día, a cualquier hora
            public Task<bool> HayCabeceraDelDia(string empresa, string origen, string destino, DateTime diaCorte, bool contarOmitidas)
            {
                string prefijo = $"{origen}→{destino} {diaCorte:dd/MM/yyyy}";
                Pasos.Add($"¿hay? {prefijo} {(contarOmitidas ? "contando omitidas" : "sin contar omitidas")}");
                Assert.AreEqual(diaCorte.Date, diaCorte, "Se pasa el día, sin hora");
                return Task.FromResult(Hechas.Any(h => h.StartsWith(prefijo + " ")) || (contarOmitidas && HechasOmitidas.Any(h => h.StartsWith(prefijo + " "))));
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

            /// <summary>NestoAPI#622: lo que devuelve LeerCabecerasDelDia.</summary>
            public readonly List<CabeceraReposicionAutomaticaDelDia> CabecerasDelDia = new List<CabeceraReposicionAutomaticaDelDia>();
            public string OrigenAvisosLeido;
            public DateTime? DesdeAvisosLeido;

            public Task<List<CabeceraReposicionAutomaticaDelDia>> LeerCabecerasDelDia(string empresa, string origen, DateTime desde)
            {
                OrigenAvisosLeido = origen;
                DesdeAvisosLeido = desde;
                return Task.FromResult(CabecerasDelDia.ToList());
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
