using Hangfire;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>
    /// NestoAPI#883 (corte 1; decisión de Carlos 20/11/26: «sí, con fecha de inventario»): el día de inventario de cada almacén (ALG, REI, ALC)
    /// como DATO (<c>dbo.InventariosFechas</c>) y, tres días laborables antes, el aviso de las reposiciones que tocan stock alrededor del
    /// recuento y siguen activas: las que LLEGAN a ese almacén la víspera o el día y las que SALEN de él y CIERRAN la víspera o el día (se
    /// reservan y, si no se preparan enteras, se cuentan de más). Cada viaje se decide: «Quitar» (la excepción SinReposicion de #867, con sus
    /// validaciones y su 409 pasada la hora de cierre) o «Mantener» (queda confirmado y deja de avisar). Lo que hay lo dice LA REGLA de
    /// siempre (<see cref="CalculadoraFechaReposicion"/> + <see cref="ReglaCalendarioReposiciones"/>). Caso: inventario del 21/11/26.
    /// </summary>
    public class FechaInventarioDTO
    {
        /// <summary>Solo lectura (el POST busca por empresa + almacén + fecha).</summary>
        public int? Id { get; set; }
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Comentario { get; set; }
        /// <summary>Solo lectura.</summary>
        public string Usuario { get; set; }
        /// <summary>Solo lectura.</summary>
        public DateTime? FechaModificacion { get; set; }
        /// <summary>Solo lectura: cuándo salió el aviso (null = aún no).</summary>
        public DateTime? AvisoEnviado { get; set; }
        /// <summary>Solo lectura: el día que toca el aviso (tres laborables del almacén antes).</summary>
        public DateTime DiaAviso { get; set; }
        /// <summary>Solo lectura: las reposiciones que tocan stock alrededor del inventario, con su estado.</summary>
        public List<ReposicionAfectadaInventarioDTO> Reposiciones { get; set; } = new List<ReposicionAfectadaInventarioDTO>();
        /// <summary>Solo lectura: cuántas siguen sin decidir.</summary>
        public int Pendientes { get; set; }
    }

    /// <summary>Un viaje que toca stock alrededor del inventario.</summary>
    public class ReposicionAfectadaInventarioDTO
    {
        public const string LLEGA = "Llega";
        public const string SALE = "Sale";
        public const string PENDIENTE = "Pendiente";
        public const string MANTENIDA = "Mantenida";
        public const string QUITADA = "Quitada";

        public string Origen { get; set; }
        public string Destino { get; set; }
        public DateTime FechaLlegada { get; set; }
        /// <summary>Cuándo cierra (desde ese momento ya no se puede quitar).</summary>
        public DateTime CierraEl { get; set; }
        /// <summary>«Llega» (al almacén del inventario) o «Sale» (de él).</summary>
        public string Motivo { get; set; }
        /// <summary>«Pendiente», «Mantenida» o «Quitada».</summary>
        public string Estado { get; set; }
        public string DecididoPor { get; set; }
        public DateTime? FechaDecision { get; set; }
        /// <summary>Aún no ha cerrado y no está quitada.</summary>
        public bool SePuedeQuitar { get; set; }
        /// <summary>«Reposición Reina → Algete, llega el viernes 20/11 (cierra el viernes 20/11 a las 9:00)».</summary>
        public string Texto { get; set; }
    }

    /// <summary>POST api/Inventarios/Fechas/{id}/Reposiciones.</summary>
    public class DecisionReposicionInventarioDTO
    {
        public const string QUITAR = "Quitar";
        public const string MANTENER = "Mantener";

        public string Origen { get; set; }
        public string Destino { get; set; }
        public DateTime FechaLlegada { get; set; }
        /// <summary>«Quitar» o «Mantener».</summary>
        public string Decision { get; set; }
    }

    /// <summary>Una decisión guardada (dbo.InventariosFechasViajes).</summary>
    public class DecisionViajeInventario
    {
        public int InventarioFechaId { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public DateTime FechaLlegada { get; set; }
        public string Decision { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>LAS REGLAS (puras) de la fecha de inventario.</summary>
    public static class ReglasFechasInventario
    {
        public const int LABORABLES_ANTES_AVISO = 3;
        public const string TIPO_NOTIFICACION = "InventarioReposicionesPendientes";
        public static readonly string[] ALMACENES = { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static string Almacen(string almacen)
        {
            string limpio = almacen?.Trim().ToUpperInvariant();
            return ALMACENES.Contains(limpio) ? limpio : null;
        }

        public static string NombreAlmacen(string almacen)
        {
            switch (Almacen(almacen))
            {
                case Constantes.Almacenes.ALGETE: return "Algete";
                case Constantes.Almacenes.REINA: return "Reina";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                default: return almacen?.Trim();
            }
        }

        /// <summary>Tres laborables del almacén antes del inventario (L-V sin sus festivos): inventario el sábado 21 → aviso el miércoles 18.</summary>
        public static DateTime DiaAviso(DateTime fechaInventario, string almacen, Func<DateTime, string, bool> esFestivo)
        {
            DateTime dia = fechaInventario.Date;
            int pendientes = LABORABLES_ANTES_AVISO;
            for (int i = 0; pendientes > 0 && i < 60; i++)
            {
                dia = dia.AddDays(-1);
                if (dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !(esFestivo?.Invoke(dia, almacen) ?? false))
                {
                    pendientes--;
                }
            }
            return dia;
        }

        /// <summary>Le toca el aviso hoy: aún no ha salido, ya es el día (o después, si la fecha se dio tarde) y el inventario no ha pasado.</summary>
        public static bool TocaAvisar(FechaInventarioDTO fecha, DateTime hoy, Func<DateTime, string, bool> esFestivo)
            => fecha != null && !fecha.AvisoEnviado.HasValue && hoy.Date <= fecha.Fecha.Date
               && hoy.Date >= DiaAviso(fecha.Fecha, fecha.Almacen, esFestivo);

        /// <summary>
        /// Los viajes que tocan stock alrededor del inventario de <paramref name="almacen"/> el día <paramref name="fechaInventario"/> (la víspera
        /// natural y el día): llegan a él o salen de él y cierran. <paramref name="conExcepciones"/> dice lo que hay HOY (con las excepciones de #867);
        /// <paramref name="sinExcepciones"/>, lo que habría sin ellas (para enseñar también los ya quitados). Las decisiones «Mantener» los confirman.
        /// </summary>
        public static List<ReposicionAfectadaInventarioDTO> Afectadas(CalculadoraFechaReposicion conExcepciones, CalculadoraFechaReposicion sinExcepciones,
            IEnumerable<ReposicionCalendario> calendario, string empresa, string almacen, DateTime fechaInventario,
            IEnumerable<DecisionViajeInventario> decisiones, DateTime ahora)
        {
            string a = Almacen(almacen);
            List<ReposicionCalendario> filas = (calendario ?? Enumerable.Empty<ReposicionCalendario>()).ToList();
            var dias = new[] { fechaInventario.Date.AddDays(-1), fechaInventario.Date };
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

            IEnumerable<(CorteReposicion Corte, string Motivo)> Cortes(CalculadoraFechaReposicion calc)
                => dias.SelectMany(d => calc.CortesQueLleganElDia(filas, d).Where(c => c.Destino == a).Select(c => (c, ReposicionAfectadaInventarioDTO.LLEGA))
                    .Concat(calc.CortesDelDia(filas, d).Where(c => c.Origen == a).Select(c => (c, ReposicionAfectadaInventarioDTO.SALE))));

            HashSet<string> vivos = new HashSet<string>(Cortes(conExcepciones).Select(c => Clave(c.Corte.Origen, c.Corte.Destino, c.Corte.Llegada)));
            List<DecisionViajeInventario> lista = (decisiones ?? Enumerable.Empty<DecisionViajeInventario>()).ToList();

            return Cortes(conExcepciones).Concat(Cortes(sinExcepciones))
                .Where(c => string.Equals(c.Corte.Empresa, empresaLimpia, StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => Clave(c.Corte.Origen, c.Corte.Destino, c.Corte.Llegada))
                .Select(g =>
                {
                    (CorteReposicion corte, string motivo) = g.First();
                    bool vivo = vivos.Contains(g.Key);
                    DecisionViajeInventario decision = lista
                        .Where(d => Clave(d.Origen, d.Destino, d.FechaLlegada) == g.Key)
                        .OrderByDescending(d => d.Fecha).FirstOrDefault();
                    string estado = !vivo
                        ? ReposicionAfectadaInventarioDTO.QUITADA
                        : string.Equals(decision?.Decision, DecisionReposicionInventarioDTO.MANTENER, StringComparison.OrdinalIgnoreCase)
                            ? ReposicionAfectadaInventarioDTO.MANTENIDA
                            : ReposicionAfectadaInventarioDTO.PENDIENTE;
                    return new ReposicionAfectadaInventarioDTO
                    {
                        Origen = corte.Origen,
                        Destino = corte.Destino,
                        FechaLlegada = corte.Llegada.Date,
                        CierraEl = corte.Corte,
                        Motivo = motivo,
                        Estado = estado,
                        DecididoPor = estado == ReposicionAfectadaInventarioDTO.PENDIENTE ? null : decision?.Usuario,
                        FechaDecision = estado == ReposicionAfectadaInventarioDTO.PENDIENTE ? (DateTime?)null : decision?.Fecha,
                        SePuedeQuitar = vivo && corte.Corte > ahora,
                        Texto = Texto(corte)
                    };
                })
                .OrderBy(r => r.CierraEl).ThenBy(r => r.Origen).ThenBy(r => r.Destino)
                .ToList();
        }

        public static string Texto(CorteReposicion corte)
            => $"Reposición {NombreAlmacen(corte.Origen)} → {NombreAlmacen(corte.Destino)}, llega {ElDia(corte.Llegada)} " +
               $"(cierra {ElDia(corte.Corte)} a las {corte.Corte:H:mm})";

        public static string ElDia(DateTime fecha) => "el " + fecha.ToString("dddd dd/MM", castellano);

        /// <summary>El aviso de la campana: «El sábado 21/11 hay inventario en Algete y siguen activas 3 reposiciones…».</summary>
        public static string Cuerpo(FechaInventarioDTO fecha, IEnumerable<ReposicionAfectadaInventarioDTO> pendientes)
        {
            List<ReposicionAfectadaInventarioDTO> lista = (pendientes ?? Enumerable.Empty<ReposicionAfectadaInventarioDTO>()).ToList();
            string cuantas = lista.Count == 1 ? "sigue activa 1 reposición que toca su stock" : $"siguen activas {lista.Count} reposiciones que tocan su stock";
            return $"{Mayuscula(ElDia(fecha.Fecha))} hay inventario en {NombreAlmacen(fecha.Almacen)} y {cuantas}: " +
                   string.Join("; ", lista.Select(r => r.Texto)) +
                   ". Decida en Nesto (Inventario › Fechas de inventario) si se quitan o se mantienen.";
        }

        public static string Mayuscula(string texto) => string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0], castellano) + texto.Substring(1);

        internal static string Clave(string origen, string destino, DateTime llegada)
            => $"{origen?.Trim().ToUpperInvariant()}|{destino?.Trim().ToUpperInvariant()}|{llegada:yyyyMMdd}";
    }

    public interface IRepositorioFechasInventario
    {
        Task<bool> TablaCreada();
        Task<List<FechaInventarioDTO>> Leer(string empresa, DateTime desde);
        Task<FechaInventarioDTO> LeerPorId(int id);
        Task<int> Guardar(FechaInventarioDTO fecha, string usuario);
        Task<bool> Borrar(int id);
        Task<List<DecisionViajeInventario>> LeerDecisiones(IEnumerable<int> ids);
        Task GuardarDecision(DecisionViajeInventario decision);
        Task MarcarAvisado(int id, DateTime cuando);
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);
    }

    public interface IAvisadorFechasInventario
    {
        /// <summary>Campana de Nesto a Almacén, a la gente de las tiendas afectadas y a Dirección. Nunca rompe.</summary>
        Task Avisar(string empresa, IEnumerable<string> almacenes, string titulo, string cuerpo, int id);
    }

    public class ServicioFechasInventario
    {
        public const string MENSAJE_SIN_TABLA =
            "Falta lanzar Scripts/Issue883_InventariosFechas.sql en la base de datos: todavía no se pueden poner fechas de inventario.";
        public const int LONGITUD_MAXIMA_COMENTARIO = 200;

        private readonly IRepositorioFechasInventario repositorio;
        private readonly Func<IServicioExcepcionesCalendarioReposiciones> excepciones;
        private readonly IAvisadorFechasInventario avisador;
        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly Func<IReadOnlyList<ExcepcionCalendarioReposicionDTO>> excepcionesVigentes;
        private readonly Func<DateTime> reloj;

        public ServicioFechasInventario(NVEntities db)
            : this(new RepositorioFechasInventarioSql(db), () => new ServicioExcepcionesCalendarioReposiciones(db),
                  new AvisadorFechasInventario(new ServicioNotificacionesPush()), GestorFestivos.EsFestivo, null, () => DateTime.Now)
        {
        }

        internal ServicioFechasInventario(IRepositorioFechasInventario repositorio, Func<IServicioExcepcionesCalendarioReposiciones> excepciones,
            IAvisadorFechasInventario avisador, Func<DateTime, string, bool> esFestivo,
            Func<IReadOnlyList<ExcepcionCalendarioReposicionDTO>> excepcionesVigentes, Func<DateTime> reloj)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.excepciones = excepciones;
            this.avisador = avisador;
            this.esFestivo = esFestivo ?? GestorFestivos.EsFestivo;
            this.excepcionesVigentes = excepcionesVigentes ?? ExcepcionesCalendarioReposiciones.Vigentes;
            this.reloj = reloj ?? (() => DateTime.Now);
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private async Task ComprobarTabla()
        {
            if (!await repositorio.TablaCreada().ConfigureAwait(false))
            {
                throw new NestoBusinessException(MENSAJE_SIN_TABLA) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
        }

        public async Task<List<FechaInventarioDTO>> Leer(string empresa, string almacen, DateTime? desde)
        {
            await ComprobarTabla().ConfigureAwait(false);
            string empresaLimpia = Empresa(empresa);
            List<FechaInventarioDTO> fechas = (await repositorio.Leer(empresaLimpia, (desde ?? reloj()).Date).ConfigureAwait(false) ?? new List<FechaInventarioDTO>())
                .Where(f => string.IsNullOrWhiteSpace(almacen) || string.Equals(f.Almacen?.Trim(), almacen.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Fecha).ThenBy(f => f.Almacen)
                .ToList();
            await Completar(empresaLimpia, fechas).ConfigureAwait(false);
            return fechas;
        }

        public async Task<FechaInventarioDTO> Guardar(FechaInventarioDTO fecha, string usuario)
        {
            if (fecha == null)
            {
                throw new NestoBusinessException("Faltan los datos de la fecha de inventario.");
            }
            await ComprobarTabla().ConfigureAwait(false);
            string almacen = ReglasFechasInventario.Almacen(fecha.Almacen)
                ?? throw new NestoBusinessException($"El almacén tiene que ser {string.Join(", ", ReglasFechasInventario.ALMACENES)}.");
            if (fecha.Fecha == default)
            {
                throw new NestoBusinessException("Falta el día del inventario.");
            }
            if (fecha.Fecha.Date < reloj().Date)
            {
                throw new NestoBusinessException("No se puede poner una fecha de inventario en un día que ya ha pasado.");
            }
            string comentario = string.IsNullOrWhiteSpace(fecha.Comentario) ? null : fecha.Comentario.Trim();
            if (comentario?.Length > LONGITUD_MAXIMA_COMENTARIO)
            {
                throw new NestoBusinessException($"El comentario no puede pasar de {LONGITUD_MAXIMA_COMENTARIO} caracteres.");
            }
            var nueva = new FechaInventarioDTO { Empresa = Empresa(fecha.Empresa), Almacen = almacen, Fecha = fecha.Fecha.Date, Comentario = comentario };
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.SinDominio(usuario));
            nueva.Id = await repositorio.Guardar(nueva, usuarioAuditoria).ConfigureAwait(false);
            FechaInventarioDTO guardada = await repositorio.LeerPorId(nueva.Id.Value).ConfigureAwait(false) ?? nueva;
            await Completar(nueva.Empresa, new List<FechaInventarioDTO> { guardada }).ConfigureAwait(false);
            return guardada;
        }

        public async Task<FechaInventarioDTO> Borrar(int id)
        {
            await ComprobarTabla().ConfigureAwait(false);
            FechaInventarioDTO fecha = await repositorio.LeerPorId(id).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe la fecha de inventario {id}.") { StatusCode = HttpStatusCode.NotFound };
            if (fecha.Fecha.Date < reloj().Date)
            {
                throw new NestoBusinessException("Esa fecha de inventario ya ha pasado: no se borra.");
            }
            _ = await repositorio.Borrar(id).ConfigureAwait(false);
            return fecha;
        }

        /// <summary>«Quitar» (excepción SinReposicion de #867, con su 400/409) o «Mantener» (deja de avisar). Devuelve la fecha con sus viajes.</summary>
        public async Task<FechaInventarioDTO> Decidir(int id, DecisionReposicionInventarioDTO peticion, string usuario)
        {
            await ComprobarTabla().ConfigureAwait(false);
            string decision = string.Equals(peticion?.Decision?.Trim(), DecisionReposicionInventarioDTO.QUITAR, StringComparison.OrdinalIgnoreCase)
                ? DecisionReposicionInventarioDTO.QUITAR
                : string.Equals(peticion?.Decision?.Trim(), DecisionReposicionInventarioDTO.MANTENER, StringComparison.OrdinalIgnoreCase)
                    ? DecisionReposicionInventarioDTO.MANTENER
                    : throw new NestoBusinessException("La decisión tiene que ser «Quitar» o «Mantener».");
            FechaInventarioDTO fecha = await repositorio.LeerPorId(id).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe la fecha de inventario {id}.") { StatusCode = HttpStatusCode.NotFound };
            await Completar(fecha.Empresa, new List<FechaInventarioDTO> { fecha }).ConfigureAwait(false);
            ReposicionAfectadaInventarioDTO viaje = fecha.Reposiciones.FirstOrDefault(r =>
                ReglasFechasInventario.Clave(r.Origen, r.Destino, r.FechaLlegada) == ReglasFechasInventario.Clave(peticion.Origen, peticion.Destino, peticion.FechaLlegada));
            if (viaje == null)
            {
                throw new NestoBusinessException("Esa reposición no toca el inventario de ese día.") { StatusCode = HttpStatusCode.NotFound };
            }
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.SinDominio(usuario));
            if (decision == DecisionReposicionInventarioDTO.QUITAR && viaje.Estado != ReposicionAfectadaInventarioDTO.QUITADA)
            {
                if (excepciones == null)
                {
                    throw new NestoBusinessException(ServicioExcepcionesCalendarioReposiciones.MENSAJE_SIN_TABLA) { StatusCode = HttpStatusCode.ServiceUnavailable };
                }
                _ = await excepciones().Guardar(new ExcepcionCalendarioReposicionDTO
                {
                    Empresa = fecha.Empresa,
                    Origen = viaje.Origen,
                    Destino = viaje.Destino,
                    FechaLlegada = viaje.FechaLlegada,
                    Tipo = TiposExcepcionCalendarioReposicion.SIN_REPOSICION,
                    Motivo = $"Inventario de {ReglasFechasInventario.NombreAlmacen(fecha.Almacen)} del {fecha.Fecha:dd/MM/yyyy}"
                }, usuario).ConfigureAwait(false);
            }
            await repositorio.GuardarDecision(new DecisionViajeInventario
            {
                InventarioFechaId = id,
                Origen = viaje.Origen,
                Destino = viaje.Destino,
                FechaLlegada = viaje.FechaLlegada,
                Decision = decision,
                Usuario = usuarioAuditoria,
                Fecha = reloj()
            }).ConfigureAwait(false);
            FechaInventarioDTO despues = await repositorio.LeerPorId(id).ConfigureAwait(false) ?? fecha;
            // La excepción nueva ya está en la caché (Guardar la invalida); la calculadora vuelve a leerla
            await Completar(despues.Empresa, new List<FechaInventarioDTO> { despues }).ConfigureAwait(false);
            return despues;
        }

        /// <summary>El job: el aviso de las fechas a las que les toca hoy. Devuelve cuántos avisos han salido. Sin la tabla, nada.</summary>
        public async Task<int> Avisar(string empresa)
        {
            if (!await repositorio.TablaCreada().ConfigureAwait(false))
            {
                return 0;
            }
            DateTime ahora = reloj();
            string empresaLimpia = Empresa(empresa);
            List<FechaInventarioDTO> fechas = (await repositorio.Leer(empresaLimpia, ahora.Date).ConfigureAwait(false) ?? new List<FechaInventarioDTO>())
                .Where(f => ReglasFechasInventario.TocaAvisar(f, ahora, esFestivo))
                .ToList();
            if (!fechas.Any())
            {
                return 0;
            }
            await Completar(empresaLimpia, fechas).ConfigureAwait(false);
            int avisos = 0;
            foreach (FechaInventarioDTO fecha in fechas)
            {
                List<ReposicionAfectadaInventarioDTO> pendientes = fecha.Reposiciones.Where(r => r.Estado == ReposicionAfectadaInventarioDTO.PENDIENTE).ToList();
                if (pendientes.Any() && avisador != null)
                {
                    var almacenes = new List<string> { Constantes.Almacenes.ALGETE, fecha.Almacen };
                    almacenes.AddRange(pendientes.SelectMany(p => new[] { p.Origen, p.Destino }));
                    try
                    {
                        await avisador.Avisar(empresaLimpia, almacenes.Distinct(StringComparer.OrdinalIgnoreCase),
                            $"Inventario en {ReglasFechasInventario.NombreAlmacen(fecha.Almacen)} {ReglasFechasInventario.ElDia(fecha.Fecha)}: reposiciones por decidir",
                            ReglasFechasInventario.Cuerpo(fecha, pendientes), fecha.Id ?? 0).ConfigureAwait(false);
                        avisos++;
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Fecha de inventario #883] No se ha podido avisar de la {fecha.Id}: {ex.Message}", ex));
                        continue;
                    }
                }
                // Sin nada pendiente también se marca: no hay de qué avisar
                await repositorio.MarcarAvisado(fecha.Id ?? 0, ahora).ConfigureAwait(false);
            }
            return avisos;
        }

        private async Task Completar(string empresa, List<FechaInventarioDTO> fechas)
        {
            if (fechas == null || !fechas.Any())
            {
                return;
            }
            DateTime ahora = reloj();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresa).ConfigureAwait(false) ?? new List<ReposicionCalendario>();
            List<DecisionViajeInventario> decisiones = await repositorio.LeerDecisiones(fechas.Where(f => f.Id.HasValue).Select(f => f.Id.Value)).ConfigureAwait(false)
                ?? new List<DecisionViajeInventario>();
            var conExcepciones = new CalculadoraFechaReposicion(esFestivo, excepcionesVigentes);
            var sinExcepciones = new CalculadoraFechaReposicion(esFestivo, () => new List<ExcepcionCalendarioReposicionDTO>());
            foreach (FechaInventarioDTO fecha in fechas)
            {
                fecha.DiaAviso = ReglasFechasInventario.DiaAviso(fecha.Fecha, fecha.Almacen, esFestivo);
                fecha.Reposiciones = ReglasFechasInventario.Afectadas(conExcepciones, sinExcepciones, calendario, empresa, fecha.Almacen, fecha.Fecha,
                    decisiones.Where(d => d.InventarioFechaId == fecha.Id), ahora);
                fecha.Pendientes = fecha.Reposiciones.Count(r => r.Estado == ReposicionAfectadaInventarioDTO.PENDIENTE);
            }
        }
    }

    public class AvisadorFechasInventario : IAvisadorFechasInventario
    {
        private const string DOMINIO = "NUEVAVISION\\";
        private readonly IServicioNotificacionesPush notificaciones;

        public AvisadorFechasInventario(IServicioNotificacionesPush notificaciones)
        {
            this.notificaciones = notificaciones;
        }

        public async Task Avisar(string empresa, IEnumerable<string> almacenes, string titulo, string cuerpo, int id)
        {
            var candidatos = new List<string>();
            candidatos.AddRange(Verifactu.MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>());
            candidatos.AddRange(Verifactu.MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION) ?? new List<string>());
            foreach (string almacen in (almacenes ?? Enumerable.Empty<string>()).Select(a => a?.Trim().ToUpperInvariant())
                .Where(a => !string.IsNullOrEmpty(a) && a != Constantes.Almacenes.ALGETE).Distinct())
            {
                using (var db = new NVEntities())
                {
                    candidatos.AddRange(await AvisadorReposicionAutomatica.UsuariosDelAlmacen(db, empresa, almacen).ConfigureAwait(false) ?? new List<string>());
                }
            }
            List<string> usuarios = candidatos.Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = ReglasFechasInventario.TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = ReglasFechasInventario.TIPO_NOTIFICACION,
                    ["referencia"] = id.ToString(CultureInfo.InvariantCulture)
                }
            };
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
        }
    }

    public class RepositorioFechasInventarioSql : IRepositorioFechasInventario
    {
        private readonly NVEntities db;

        public RepositorioFechasInventarioSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string SQL_TABLA_CREADA = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.InventariosFechas', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.InventariosFechasViajes', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit)";

        private const string COLUMNAS = @"Id, RTRIM(Empresa) AS Empresa, RTRIM(Almacen) AS Almacen, CAST(Fecha AS datetime) AS Fecha, Comentario, Usuario,
       FechaModificacion, AvisoEnviado";

        internal const string SQL_LEER = "SELECT " + COLUMNAS + " FROM dbo.InventariosFechas WHERE Empresa = @empresa AND Fecha >= @desde ORDER BY Fecha, Almacen";
        internal const string SQL_LEER_ID = "SELECT " + COLUMNAS + " FROM dbo.InventariosFechas WHERE Id = @id";

        // Crear o cambiar por la clave; si cambia el comentario, el aviso no se repite
        internal const string SQL_GUARDAR = @"
DECLARE @id int = (SELECT Id FROM dbo.InventariosFechas WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Almacen = @almacen AND Fecha = @fecha);
IF @id IS NULL
BEGIN
    INSERT INTO dbo.InventariosFechas (Empresa, Almacen, Fecha, Comentario, Usuario, FechaModificacion)
    VALUES (@empresa, @almacen, @fecha, @comentario, @usuario, GETDATE());
    SET @id = SCOPE_IDENTITY();
END
ELSE
    UPDATE dbo.InventariosFechas SET Comentario = @comentario, Usuario = @usuario, FechaModificacion = GETDATE() WHERE Id = @id;
SELECT @id;";

        internal const string SQL_BORRAR = @"
DELETE FROM dbo.InventariosFechasViajes WHERE InventarioFechaId = @id;
DELETE FROM dbo.InventariosFechas WHERE Id = @id;";

        internal const string SQL_DECISIONES = @"
SELECT InventarioFechaId, RTRIM(Origen) AS Origen, RTRIM(Destino) AS Destino, CAST(FechaLlegada AS datetime) AS FechaLlegada, RTRIM(Decision) AS Decision,
       Usuario, Fecha
FROM dbo.InventariosFechasViajes WHERE InventarioFechaId IN ({0})";

        internal const string SQL_GUARDAR_DECISION = @"
UPDATE dbo.InventariosFechasViajes SET Decision = @decision, Usuario = @usuario, Fecha = @cuando
WHERE InventarioFechaId = @id AND Origen = @origen AND Destino = @destino AND FechaLlegada = @llegada;
IF @@ROWCOUNT = 0
    INSERT INTO dbo.InventariosFechasViajes (InventarioFechaId, Origen, Destino, FechaLlegada, Decision, Usuario, Fecha)
    VALUES (@id, @origen, @destino, @llegada, @decision, @usuario, @cuando);";

        internal const string SQL_MARCAR_AVISADO = "UPDATE dbo.InventariosFechas SET AvisoEnviado = @cuando WHERE Id = @id AND AvisoEnviado IS NULL";

        public Task<bool> TablaCreada() => db.Database.SqlQuery<bool>(SQL_TABLA_CREADA).FirstAsync();

        /// <summary>Lo que trae la tabla (EF exige una columna por cada propiedad simple del tipo).</summary>
        private class FilaFechaInventario
        {
            public int Id { get; set; }
            public string Empresa { get; set; }
            public string Almacen { get; set; }
            public DateTime Fecha { get; set; }
            public string Comentario { get; set; }
            public string Usuario { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public DateTime? AvisoEnviado { get; set; }

            public FechaInventarioDTO Dto() => new FechaInventarioDTO
            {
                Id = Id, Empresa = Empresa, Almacen = Almacen, Fecha = Fecha, Comentario = Comentario, Usuario = Usuario,
                FechaModificacion = FechaModificacion, AvisoEnviado = AvisoEnviado
            };
        }

        public async Task<List<FechaInventarioDTO>> Leer(string empresa, DateTime desde)
            => (await db.Database.SqlQuery<FilaFechaInventario>(SQL_LEER, Char("@empresa", empresa, 3), Fecha("@desde", desde)).ToListAsync().ConfigureAwait(false))
                .Select(f => f.Dto()).ToList();

        public async Task<FechaInventarioDTO> LeerPorId(int id)
            => (await db.Database.SqlQuery<FilaFechaInventario>(SQL_LEER_ID, new SqlParameter("@id", SqlDbType.Int) { Value = id }).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault()?.Dto();

        public Task<int> Guardar(FechaInventarioDTO fecha, string usuario)
            => db.Database.SqlQuery<int>(SQL_GUARDAR, Char("@empresa", fecha.Empresa, 3), Char("@almacen", fecha.Almacen, 3), Fecha("@fecha", fecha.Fecha),
                new SqlParameter("@comentario", SqlDbType.NVarChar, 200) { Value = (object)fecha.Comentario ?? DBNull.Value },
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)usuario ?? DBNull.Value }).FirstAsync();

        public async Task<bool> Borrar(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_BORRAR, new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false) > 0;

        public async Task<List<DecisionViajeInventario>> LeerDecisiones(IEnumerable<int> ids)
        {
            List<int> lista = (ids ?? Enumerable.Empty<int>()).Distinct().Take(500).ToList();
            if (!lista.Any())
            {
                return new List<DecisionViajeInventario>();
            }
            var parametros = lista.Select((v, i) => new SqlParameter("@i" + i, SqlDbType.Int) { Value = v }).ToArray();
            return await db.Database.SqlQuery<DecisionViajeInventario>(string.Format(SQL_DECISIONES, string.Join(", ", parametros.Select(p => p.ParameterName))),
                parametros.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false);
        }

        public Task GuardarDecision(DecisionViajeInventario d)
            => db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR_DECISION,
                new SqlParameter("@id", SqlDbType.Int) { Value = d.InventarioFechaId },
                Char("@origen", d.Origen, 3), Char("@destino", d.Destino, 3), Fecha("@llegada", d.FechaLlegada),
                new SqlParameter("@decision", SqlDbType.VarChar, 20) { Value = d.Decision },
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)d.Usuario ?? DBNull.Value },
                new SqlParameter("@cuando", SqlDbType.DateTime) { Value = d.Fecha });

        public Task MarcarAvisado(int id, DateTime cuando)
            => db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_AVISADO, new SqlParameter("@id", SqlDbType.Int) { Value = id },
                new SqlParameter("@cuando", SqlDbType.DateTime) { Value = cuando });

        public Task<List<ReposicionCalendario>> LeerCalendario(string empresa) => new RepositorioExcepcionesCalendarioReposicionesSql(db).LeerCalendario(empresa);

        private static SqlParameter Char(string nombre, string valor, int largo) => new SqlParameter(nombre, SqlDbType.Char, largo) { Value = (object)valor?.Trim() ?? DBNull.Value };

        private static SqlParameter Fecha(string nombre, DateTime valor) => new SqlParameter(nombre, SqlDbType.Date) { Value = valor.Date };
    }

    /// <summary>NestoAPI#883: el job del aviso (Hangfire). Nunca rompe (ELMAH).</summary>
    public static class FechasInventarioJobsService
    {
        public const string ID_JOB = "inventario-fechas-aviso";
        /// <summary>De lunes a viernes a las 8:30 de Madrid.</summary>
        public const string CRON = "30 8 * * 1-5";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Avisar()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    _ = await new ServicioFechasInventario(db).Avisar(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Fecha de inventario #883] Job del aviso: {ex.Message}", ex));
            }
        }
    }
}
