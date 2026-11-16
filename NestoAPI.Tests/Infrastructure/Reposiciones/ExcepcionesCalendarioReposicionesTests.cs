using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#867: excepciones por fecha del calendario de reposiciones. Semana de referencia: lunes 16/11/2026 … viernes
    /// 20/11/2026 (el caso real: el viernes 20 no reciben las tiendas por el inventario del sábado 21).
    /// </summary>
    [TestClass]
    public class ExcepcionesCalendarioReposicionesTests
    {
        private static readonly TimeSpan CORTE_11 = new TimeSpan(11, 0, 0);
        private static readonly DateTime LUNES = new DateTime(2026, 11, 16);
        private static readonly DateTime MARTES = LUNES.AddDays(1);
        private static readonly DateTime MIERCOLES = LUNES.AddDays(2);
        private static readonly DateTime JUEVES = LUNES.AddDays(3);
        private static readonly DateTime VIERNES = LUNES.AddDays(4);
        private static readonly DateTime LUNES_SIGUIENTE = LUNES.AddDays(7);

        private List<ExcepcionCalendarioReposicionDTO> excepciones;
        private HashSet<string> festivos;
        private CalculadoraFechaReposicion calculadora;

        [TestInitialize]
        public void Preparar()
        {
            excepciones = new List<ExcepcionCalendarioReposicionDTO>();
            festivos = new HashSet<string>();
            calculadora = new CalculadoraFechaReposicion((dia, almacen) => festivos.Contains($"{almacen}|{dia:yyyyMMdd}"), () => excepciones);
        }

        [TestCleanup]
        public void Limpiar()
        {
            ExcepcionesCalendarioReposiciones.Reiniciar();
        }

        private static ReposicionCalendario Fila(string origen, string destino, byte dia, string cierre = "10:00", string llegada = "13:30",
            byte antelacion = 0, string empresa = "1  ", bool activo = true)
        {
            return new ReposicionCalendario
            {
                Empresa = empresa,
                AlmacenOrigen = origen,
                AlmacenDestino = destino,
                DiaSemana = dia,
                HoraCierre = TimeSpan.Parse(cierre),
                HoraLlegadaHabitual = TimeSpan.Parse(llegada),
                LaborablesAntelacionCierre = antelacion,
                Activo = activo
            };
        }

        /// <summary>REI → ALG lunes, miércoles y viernes (cierra el mismo día) y ALG → REI de lunes a viernes (cierra el laborable anterior).</summary>
        private static List<ReposicionCalendario> Calendario() => new List<ReposicionCalendario>
        {
            Fila("REI", "ALG", 1), Fila("REI", "ALG", 3), Fila("REI", "ALG", 5),
            Fila("ALG", "REI", 1, "16:00", "11:00", 1), Fila("ALG", "REI", 2, "16:00", "11:00", 1), Fila("ALG", "REI", 3, "16:00", "11:00", 1),
            Fila("ALG", "REI", 4, "16:00", "11:00", 1), Fila("ALG", "REI", 5, "16:00", "11:00", 1)
        };

        private static ExcepcionCalendarioReposicionDTO Quitar(string origen, string destino, DateTime dia, string empresa = "1") =>
            new ExcepcionCalendarioReposicionDTO
            {
                Id = 1, Empresa = empresa, Origen = origen, Destino = destino, FechaLlegada = dia,
                Tipo = TiposExcepcionCalendarioReposicion.SIN_REPOSICION, Motivo = "Inventario"
            };

        private static ExcepcionCalendarioReposicionDTO Extra(string origen, string destino, DateTime dia, string cierre = null, string llegada = null,
            byte? antelacion = null) =>
            new ExcepcionCalendarioReposicionDTO
            {
                Id = 2, Empresa = "1", Origen = origen, Destino = destino, FechaLlegada = dia,
                Tipo = TiposExcepcionCalendarioReposicion.REPOSICION_EXTRA, Motivo = "Feria",
                HoraCierre = cierre == null ? (TimeSpan?)null : TimeSpan.Parse(cierre),
                HoraLlegadaHabitual = llegada == null ? (TimeSpan?)null : TimeSpan.Parse(llegada),
                LaborablesAntelacionCierre = antelacion
            };

        #region LA REGLA

        [TestMethod]
        public void ViajesDelDia_SinExcepcion_LasFilasActivasDeEseDiaDeLaSemana()
        {
            List<ReposicionCalendario> viajes = ReglaCalendarioReposiciones.ViajesDelDia(Calendario(), excepciones, "1", "REI", "ALG", MIERCOLES);

            Assert.AreEqual(1, viajes.Count);
            Assert.AreEqual(3, viajes[0].DiaSemana);
        }

        [TestMethod]
        public void ViajesDelDia_DiaQuitado_Ninguno()
        {
            excepciones.Add(Quitar("REI", "ALG", MIERCOLES));

            Assert.AreEqual(0, ReglaCalendarioReposiciones.ViajesDelDia(Calendario(), excepciones, "1", "REI", "ALG", MIERCOLES).Count);
        }

        [TestMethod]
        public void ViajesDelDia_DiaQuitadoDeOtraRutaOEmpresa_NoCuenta()
        {
            excepciones.Add(Quitar("ALC", "ALG", MIERCOLES));
            excepciones.Add(Quitar("REI", "ALG", MIERCOLES, empresa: "3"));

            Assert.AreEqual(1, ReglaCalendarioReposiciones.ViajesDelDia(Calendario(), excepciones, "1", "REI", "ALG", MIERCOLES).Count);
        }

        [TestMethod]
        public void ViajesDelDia_DiaExtraConSusHoras_UnaConEsasHoras()
        {
            excepciones.Add(Extra("REI", "ALG", MARTES, "09:00", "12:00", 0));

            ReposicionCalendario viaje = ReglaCalendarioReposiciones.ViajesDelDia(Calendario(), excepciones, "1", "REI", "ALG", MARTES).Single();

            Assert.AreEqual(2, viaje.DiaSemana);
            Assert.AreEqual(new TimeSpan(9, 0, 0), viaje.HoraCierre);
            Assert.AreEqual(new TimeSpan(12, 0, 0), viaje.HoraLlegadaHabitual);
            Assert.AreEqual("REI", viaje.AlmacenOrigen);
        }

        [TestMethod]
        public void ViajesDelDia_DiaExtraSinHoras_LasDeLaRuta()
        {
            excepciones.Add(Extra("REI", "ALG", MARTES));

            ReposicionCalendario viaje = ReglaCalendarioReposiciones.ViajesDelDia(Calendario(), excepciones, "1", "REI", "ALG", MARTES).Single();

            Assert.AreEqual(new TimeSpan(10, 0, 0), viaje.HoraCierre);
            Assert.AreEqual(new TimeSpan(13, 30, 0), viaje.HoraLlegadaHabitual);
            Assert.AreEqual(0, viaje.LaborablesAntelacionCierre);
        }

        [TestMethod]
        public void ViajesDelDia_DiaExtraEnUnaRutaSinCalendario_Ninguno()
        {
            excepciones.Add(Extra("ALC", "REI", MARTES, "09:00", "12:00", 0));

            Assert.AreEqual(0, ReglaCalendarioReposiciones.ViajesDelDia(Calendario(), excepciones, "1", "ALC", "REI", MARTES).Count);
        }

        #endregion

        #region La calculadora

        [TestMethod]
        public void Calcular_ElMiercolesQuitado_DelLunesTrasElCierreSaltaAlViernes()
        {
            excepciones.Add(Quitar("REI", "ALG", MIERCOLES));

            ProximaReposicionDTO proxima = calculadora.Calcular(Calendario(), "REI", "ALG", CORTE_11, LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(VIERNES.AddHours(10), proxima.CierraEl);
        }

        [TestMethod]
        public void Calcular_ConUnMartesExtra_DelLunesTrasElCierreEsElMartes()
        {
            excepciones.Add(Extra("REI", "ALG", MARTES));

            ProximaReposicionDTO proxima = calculadora.Calcular(Calendario(), "REI", "ALG", CORTE_11, LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(MARTES.AddHours(10), proxima.CierraEl);
            Assert.AreEqual(MARTES.AddHours(13).AddMinutes(30), proxima.LlegaEl);
        }

        [TestMethod]
        public void Calcular_UnDiaExtraFestivoNoCuenta()
        {
            excepciones.Add(Extra("REI", "ALG", MARTES));
            festivos.Add($"REI|{MARTES:yyyyMMdd}");

            ProximaReposicionDTO proxima = calculadora.Calcular(Calendario(), "REI", "ALG", CORTE_11, LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(MIERCOLES.AddHours(10), proxima.CierraEl);
        }

        [TestMethod]
        public void CortesDelDia_ElViernes20Quitado_ElJuevesNoCierraLaDeAlgeteAReina()
        {
            // El caso real (sugerencia 1275): ALG → REI cierra el laborable anterior; la del viernes cerraría el jueves.
            excepciones.Add(Quitar("ALG", "REI", VIERNES));

            List<CorteReposicion> cortes = calculadora.CortesDelDia(Calendario(), JUEVES);

            Assert.IsFalse(cortes.Any(c => c.Origen == "ALG" && c.Destino == "REI"));
        }

        [TestMethod]
        public void CortesDelDia_SinExcepcion_ElJuevesCierraLaDelViernes()
        {
            List<CorteReposicion> cortes = calculadora.CortesDelDia(Calendario(), JUEVES);

            CorteReposicion corte = cortes.Single(c => c.Origen == "ALG" && c.Destino == "REI");
            Assert.AreEqual(JUEVES.AddHours(16), corte.Corte);
            Assert.AreEqual(VIERNES.AddHours(11), corte.Llegada);
        }

        [TestMethod]
        public void CortesQueLleganElDia_DiaQuitado_NoLlegaNada_YElJobNoAvisaFueraDePlazo()
        {
            excepciones.Add(Quitar("ALG", "REI", VIERNES));

            List<CorteReposicion> deHoy = calculadora.CortesDeHoy(Calendario(), VIERNES.AddHours(12));

            Assert.IsFalse(deHoy.Any(c => c.Origen == "ALG" && c.Destino == "REI" && c.Llegada.Date == VIERNES));
        }

        [TestMethod]
        public void CortesDelDia_DiaExtraConAntelacion_CierraElLaborableAnterior()
        {
            List<ReposicionCalendario> calendario = new List<ReposicionCalendario> { Fila("ALG", "ALC", 2, "16:00", "11:00", 1) };
            excepciones.Add(Extra("ALG", "ALC", JUEVES));

            CorteReposicion corte = calculadora.CortesDelDia(calendario, MIERCOLES).Single();

            Assert.AreEqual(MIERCOLES.AddHours(16), corte.Corte);
            Assert.AreEqual(JUEVES.AddHours(11), corte.Llegada);
        }

        [TestMethod]
        public void ExcepcionDelDia_DevuelveLaDeEsaRutaYDia()
        {
            excepciones.Add(Quitar("ALG", "REI", VIERNES));

            Assert.IsNotNull(calculadora.ExcepcionDelDia("1", "alg", "rei", VIERNES.AddHours(9)));
            Assert.IsNull(calculadora.ExcepcionDelDia("1", "ALG", "REI", JUEVES));
        }

        #endregion

        #region La caché

        [TestMethod]
        public void Cache_SinLector_Ninguna()
        {
            ExcepcionesCalendarioReposiciones.Reiniciar();

            Assert.AreEqual(0, ExcepcionesCalendarioReposiciones.Vigentes().Count);
        }

        [TestMethod]
        public void Cache_LeeUnaVezHastaQueCaducaOSeInvalida()
        {
            int lecturas = 0;
            DateTime ahora = LUNES;
            ExcepcionesCalendarioReposiciones.Configurar(() =>
            {
                lecturas++;
                return new List<ExcepcionCalendarioReposicionDTO> { Quitar("ALG", "REI", VIERNES) };
            }, null, () => ahora);

            Assert.AreEqual(1, ExcepcionesCalendarioReposiciones.Vigentes().Count);
            _ = ExcepcionesCalendarioReposiciones.Vigentes();
            Assert.AreEqual(1, lecturas);

            ExcepcionesCalendarioReposiciones.Invalidar();
            _ = ExcepcionesCalendarioReposiciones.Vigentes();
            Assert.AreEqual(2, lecturas);

            ahora = ahora + ExcepcionesCalendarioReposiciones.DURACION_CACHE;
            _ = ExcepcionesCalendarioReposiciones.Vigentes();
            Assert.AreEqual(3, lecturas);
        }

        [TestMethod]
        public void Cache_SiFallaLaLectura_LoUltimoLeidoYSeRegistra()
        {
            bool fallar = false;
            Exception registrada = null;
            ExcepcionesCalendarioReposiciones.Configurar(() =>
            {
                if (fallar)
                {
                    throw new InvalidOperationException("caída");
                }
                return new List<ExcepcionCalendarioReposicionDTO> { Quitar("ALG", "REI", VIERNES) };
            }, ex => registrada = ex, () => LUNES);

            Assert.AreEqual(1, ExcepcionesCalendarioReposiciones.Vigentes().Count);
            fallar = true;
            ExcepcionesCalendarioReposiciones.Invalidar();

            Assert.AreEqual(1, ExcepcionesCalendarioReposiciones.Vigentes().Count);
            Assert.IsNotNull(registrada);
        }

        [TestMethod]
        public void Calculadora_SinExcepcionesInyectadas_UsaLaCache()
        {
            ExcepcionesCalendarioReposiciones.Configurar(() => new List<ExcepcionCalendarioReposicionDTO> { Quitar("REI", "ALG", MIERCOLES) },
                null, () => LUNES);
            var deLaCache = new CalculadoraFechaReposicion((dia, almacen) => false);

            ProximaReposicionDTO proxima = deLaCache.Calcular(Calendario(), "REI", "ALG", CORTE_11, LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(VIERNES.AddHours(10), proxima.CierraEl);
        }

        #endregion

        #region El servicio

        private class RepositorioFalso : IRepositorioExcepcionesCalendarioReposiciones
        {
            internal bool HayTabla = true;
            internal List<ReposicionCalendario> Calendario = ExcepcionesCalendarioReposicionesTests.Calendario();
            internal List<ExcepcionCalendarioReposicionDTO> Filas = new List<ExcepcionCalendarioReposicionDTO>();
            internal int Guardadas;
            internal int Borradas;
            private int siguienteId = 100;

            public Task<bool> TablaCreada() => Task.FromResult(HayTabla);

            public Task<List<ExcepcionCalendarioReposicionDTO>> Leer(string empresa, DateTime desde) =>
                Task.FromResult(Filas.Where(f => f.Empresa == empresa && f.FechaLlegada >= desde).Select(Copia).ToList());

            public Task<ExcepcionCalendarioReposicionDTO> LeerPorId(int id) =>
                Task.FromResult(Filas.Where(f => f.Id == id).Select(Copia).FirstOrDefault());

            public Task<List<ReposicionCalendario>> LeerCalendario(string empresa) => Task.FromResult(Calendario.ToList());

            public Task<int> Guardar(ExcepcionCalendarioReposicionDTO excepcion, string usuario)
            {
                Guardadas++;
                ExcepcionCalendarioReposicionDTO existente = Filas.FirstOrDefault(f => f.Empresa == excepcion.Empresa && f.Origen == excepcion.Origen
                    && f.Destino == excepcion.Destino && f.FechaLlegada == excepcion.FechaLlegada);
                if (existente != null)
                {
                    _ = Filas.Remove(existente);
                }
                ExcepcionCalendarioReposicionDTO nueva = Copia(excepcion);
                nueva.Id = existente?.Id ?? siguienteId++;
                nueva.Usuario = usuario;
                Filas.Add(nueva);
                return Task.FromResult(nueva.Id.Value);
            }

            public Task<bool> Borrar(int id, string usuario)
            {
                Borradas++;
                return Task.FromResult(Filas.RemoveAll(f => f.Id == id) > 0);
            }

            private static ExcepcionCalendarioReposicionDTO Copia(ExcepcionCalendarioReposicionDTO e) => new ExcepcionCalendarioReposicionDTO
            {
                Id = e.Id, Empresa = e.Empresa, Origen = e.Origen, Destino = e.Destino, FechaLlegada = e.FechaLlegada, Tipo = e.Tipo,
                HoraCierre = e.HoraCierre, LaborablesAntelacionCierre = e.LaborablesAntelacionCierre, HoraLlegadaHabitual = e.HoraLlegadaHabitual,
                Motivo = e.Motivo, Usuario = e.Usuario, FechaModificacion = e.FechaModificacion
            };
        }

        private RepositorioFalso repositorio;
        private int invalidaciones;

        private ServicioExcepcionesCalendarioReposiciones Servicio(DateTime ahora)
        {
            repositorio = repositorio ?? new RepositorioFalso();
            return new ServicioExcepcionesCalendarioReposiciones(repositorio, (dia, almacen) => false, () => ahora, () => invalidaciones++);
        }

        private static async Task<NestoBusinessException> Falla(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (NestoBusinessException ex)
            {
                return ex;
            }
            Assert.Fail("Tenía que fallar.");
            return null;
        }

        [TestMethod]
        public async Task Guardar_QuitarElViernes20DeAlgeteAReina_SeGuardaConElCierreDelJuevesEInvalidaLaCache()
        {
            ExcepcionCalendarioReposicionDTO guardada = await Servicio(LUNES.AddHours(9)).Guardar(new ExcepcionCalendarioReposicionDTO
            {
                Origen = "alg", Destino = "rei", FechaLlegada = VIERNES, Tipo = "sinreposicion", Motivo = " Inventario del sábado 21 ",
                HoraCierre = new TimeSpan(9, 0, 0)
            }, @"NUEVAVISION\Carlos");

            Assert.AreEqual(1, repositorio.Guardadas);
            Assert.AreEqual(1, invalidaciones);
            Assert.AreEqual("1", guardada.Empresa);
            Assert.AreEqual("ALG", guardada.Origen);
            Assert.AreEqual(TiposExcepcionCalendarioReposicion.SIN_REPOSICION, guardada.Tipo);
            Assert.AreEqual("Inventario del sábado 21", guardada.Motivo);
            Assert.IsNull(guardada.HoraCierre, "Un día quitado no lleva horas.");
            Assert.AreEqual(JUEVES.AddHours(16), guardada.CierraEl);
        }

        [TestMethod]
        public async Task Guardar_QuitarUnDiaQueNoEstaEnElCalendario_400()
        {
            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(Quitar("REI", "ALG", MARTES), "Carlos"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            StringAssert.Contains(ex.Message, "no hay nada que quitar");
            Assert.AreEqual(0, repositorio.Guardadas);
        }

        [TestMethod]
        public async Task Guardar_UnDiaQueYaHaPasado_400()
        {
            NestoBusinessException ex = await Falla(() => Servicio(MARTES).Guardar(Quitar("REI", "ALG", LUNES), "Carlos"));

            StringAssert.Contains(ex.Message, "ya ha pasado");
        }

        [TestMethod]
        public async Task Guardar_SinMotivo_400()
        {
            ExcepcionCalendarioReposicionDTO sinMotivo = Quitar("REI", "ALG", MIERCOLES);
            sinMotivo.Motivo = "  ";

            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(sinMotivo, "Carlos"));

            StringAssert.Contains(ex.Message, "motivo");
        }

        [TestMethod]
        public async Task Guardar_TipoDesconocido_400()
        {
            ExcepcionCalendarioReposicionDTO mal = Quitar("REI", "ALG", MIERCOLES);
            mal.Tipo = "Festivo";

            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(mal, "Carlos"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public async Task Guardar_QuitarUnDiaCuyaReposicionYaHaCerrado_409()
        {
            // La del viernes de ALG a REI cierra el jueves a las 16:00.
            NestoBusinessException ex = await Falla(() => Servicio(JUEVES.AddHours(16).AddMinutes(5)).Guardar(Quitar("ALG", "REI", VIERNES), "Carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, "ya no se puede cambiar");
            Assert.AreEqual(0, repositorio.Guardadas);
        }

        [TestMethod]
        public async Task Guardar_QuitarUnDiaFestivo_400()
        {
            repositorio = new RepositorioFalso();
            var servicio = new ServicioExcepcionesCalendarioReposiciones(repositorio, (dia, almacen) => dia == MIERCOLES && almacen == "REI", () => LUNES, () => { });

            NestoBusinessException ex = await Falla(() => servicio.Guardar(Quitar("REI", "ALG", MIERCOLES), "Carlos"));

            StringAssert.Contains(ex.Message, "no hace falta quitarla");
        }

        [TestMethod]
        public async Task Guardar_DiaExtraQueYaEstaEnElCalendario_400()
        {
            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(Extra("REI", "ALG", MIERCOLES), "Carlos"));

            StringAssert.Contains(ex.Message, "no hace falta un día extra");
        }

        [TestMethod]
        public async Task Guardar_DiaExtraSinHoras_TomaLasDeLaRuta()
        {
            ExcepcionCalendarioReposicionDTO guardada = await Servicio(LUNES).Guardar(Extra("REI", "ALG", MARTES), "Carlos");

            Assert.AreEqual(new TimeSpan(10, 0, 0), guardada.HoraCierre);
            Assert.AreEqual(new TimeSpan(13, 30, 0), guardada.HoraLlegadaHabitual);
            Assert.AreEqual((byte)0, guardada.LaborablesAntelacionCierre);
            Assert.AreEqual(MARTES.AddHours(10), guardada.CierraEl);
        }

        [TestMethod]
        public async Task Guardar_DiaExtraConOtraAntelacion_400()
        {
            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(Extra("REI", "ALG", MARTES, "10:00", "13:30", 1), "Carlos"));

            StringAssert.Contains(ex.Message, "misma antelación");
        }

        [TestMethod]
        public async Task Guardar_DiaExtraEnUnaRutaSinCalendario_400()
        {
            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(Extra("ALC", "REI", MARTES), "Carlos"));

            StringAssert.Contains(ex.Message, "No hay ninguna reposición de ALC a REI");
        }

        [TestMethod]
        public async Task Guardar_DiaExtraCuyoCierreYaHaPasado_409()
        {
            NestoBusinessException ex = await Falla(() => Servicio(MARTES.AddHours(10).AddMinutes(1)).Guardar(Extra("REI", "ALG", MARTES), "Carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
        }

        [TestMethod]
        public async Task Guardar_ElMismoDiaOtraVez_CambiaLaQueHabia()
        {
            ServicioExcepcionesCalendarioReposiciones servicio = Servicio(LUNES);
            ExcepcionCalendarioReposicionDTO quitado = await servicio.Guardar(Quitar("ALG", "REI", VIERNES), "Carlos");
            ExcepcionCalendarioReposicionDTO otraVez = Quitar("ALG", "REI", VIERNES);
            otraVez.Motivo = "Inventario anual";

            ExcepcionCalendarioReposicionDTO cambiada = await servicio.Guardar(otraVez, "Carlos");

            Assert.AreEqual(quitado.Id, cambiada.Id);
            Assert.AreEqual(1, repositorio.Filas.Count);
            Assert.AreEqual("Inventario anual", repositorio.Filas[0].Motivo);
        }

        [TestMethod]
        public async Task Guardar_SinTabla_503()
        {
            repositorio = new RepositorioFalso { HayTabla = false };

            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Guardar(Quitar("REI", "ALG", MIERCOLES), "Carlos"));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        [TestMethod]
        public async Task Leer_DeHoyEnAdelante_ConSuCierre()
        {
            repositorio = new RepositorioFalso();
            repositorio.Filas.Add(new ExcepcionCalendarioReposicionDTO
            {
                Id = 1, Empresa = "1", Origen = "REI", Destino = "ALG", FechaLlegada = LUNES.AddDays(-7),
                Tipo = TiposExcepcionCalendarioReposicion.SIN_REPOSICION, Motivo = "Vieja"
            });
            repositorio.Filas.Add(new ExcepcionCalendarioReposicionDTO
            {
                Id = 2, Empresa = "1", Origen = "ALG", Destino = "REI", FechaLlegada = VIERNES,
                Tipo = TiposExcepcionCalendarioReposicion.SIN_REPOSICION, Motivo = "Inventario"
            });

            List<ExcepcionCalendarioReposicionDTO> leidas = await Servicio(LUNES.AddHours(9)).Leer(null, null);

            Assert.AreEqual(1, leidas.Count);
            Assert.AreEqual(2, leidas[0].Id);
            Assert.AreEqual(JUEVES.AddHours(16), leidas[0].CierraEl);
        }

        [TestMethod]
        public async Task Borrar_AntesDelCierre_SeBorraEInvalida()
        {
            ServicioExcepcionesCalendarioReposiciones servicio = Servicio(LUNES);
            ExcepcionCalendarioReposicionDTO guardada = await servicio.Guardar(Quitar("ALG", "REI", VIERNES), "Carlos");

            _ = await servicio.Borrar(guardada.Id.Value, "Carlos");

            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(2, invalidaciones);
        }

        [TestMethod]
        public async Task Borrar_UnDiaQuitadoCuyoCierreYaHaPasado_409()
        {
            ExcepcionCalendarioReposicionDTO guardada = await Servicio(LUNES).Guardar(Quitar("ALG", "REI", VIERNES), "Carlos");

            NestoBusinessException ex = await Falla(() => Servicio(JUEVES.AddHours(17)).Borrar(guardada.Id.Value, "Carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual(0, repositorio.Borradas);
        }

        [TestMethod]
        public async Task Borrar_NoExiste_404()
        {
            NestoBusinessException ex = await Falla(() => Servicio(LUNES).Borrar(999, "Carlos"));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public void TextoYaCerrada_DiceElDiaYLaHora()
        {
            string texto = ServicioExcepcionesCalendarioReposiciones.TextoYaCerrada("ALG", "REI", VIERNES, JUEVES.AddHours(16));

            StringAssert.Contains(texto, "el viernes 20/11/2026");
            StringAssert.Contains(texto, "el jueves 19/11/2026 a las 16:00");
        }

        #endregion
    }
}
