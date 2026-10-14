using Hangfire;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#577 (corte 3b): qué ha pasado con una ruta al rellenarla sola.</summary>
    public static class ResultadosReposicionAutomatica
    {
        /// <summary>Creada (desde una tienda, en preparación; desde Algete, cerrada y por recoger en Ariadna).</summary>
        public const string CREADA = "Creada";
        /// <summary>
        /// NestoAPI#622: la tienda aún tenía sin enviar la de un viaje anterior y la de hoy se le ha sumado (ver
        /// <see cref="ServicioPreparacionReposicion.Completar"/>). Se avisa a la tienda.
        /// </summary>
        public const string COMPLETADA = "Completada";
        /// <summary>La propuesta no tenía nada que mandar. Se apunta para no reintentar.</summary>
        public const string VACIA = "Vacia";
        /// <summary>
        /// El origen ya tenía una en preparación que no se puede completar (hacia otro destino, o en Algete hecha en Nesto
        /// viejo): no se crea otra. Se apunta y se avisa.
        /// </summary>
        public const string YA_EN_PREPARACION = "YaEnPreparacion";
        /// <summary>
        /// Esa ruta ya tiene un traspaso de reposición creado después del corte, con cualquier herramienta (Nesto viejo
        /// incluido), en preparación, salido o recibido: alguien ya la ha rellenado. Se apunta sin aviso (es normal).
        /// </summary>
        public const string YA_CUBIERTA = "YaCubierta";
        /// <summary>Otro motivo de negocio (inventario en curso, almacén sin diario…). Se apunta y se avisa.</summary>
        public const string NO_SE_PUEDE = "NoSePuede";
        /// <summary>A la hora de llegada habitual aún no se había rellenado: ese viaje ya ha pasado. Se apunta y se avisa.</summary>
        public const string FUERA_DE_PLAZO = "FueraDePlazo";
        /// <summary>Fallo inesperado (BD, bloqueo…): se avisa y NO se apunta, para que la siguiente pasada lo reintente.</summary>
        public const string ERROR = "Error";
    }

    /// <summary>NestoAPI#577 (corte 3b): resultado de rellenar una ruta (job y POST api/Reposiciones/RellenarAutomatica).</summary>
    public class ResultadoReposicionAutomaticaDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>El instante de corte del calendario con el que se ha rellenado (día + HoraCierre).</summary>
        public DateTime Corte { get; set; }
        /// <summary>Ver <see cref="ResultadosReposicionAutomatica"/>.</summary>
        public string Resultado { get; set; }
        /// <summary>Solo desde Algete (nace numerada). Desde una tienda, null hasta que la terminen.</summary>
        public int? NumTraspaso { get; set; }
        public int Lineas { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#577: un traspaso de reposición de la ruta creado después del corte (ver <see cref="IRepositorioReposicionAutomatica.BuscarTraspasoQueCubre"/>).</summary>
    public class TraspasoReposicionExistente
    {
        /// <summary>Null si aún está en preparación en una tienda (sin número hasta terminarla).</summary>
        public int? NumTraspaso { get; set; }
        /// <summary>Quién la creó. Null si no se sabe (ya recibida: en ExtractoProducto el usuario es el de la recepción).</summary>
        public string Usuario { get; set; }
        /// <summary>Cuándo se creó (ver el SQL: cada estado guarda el instante en un sitio).</summary>
        public DateTime Fecha { get; set; }
    }

    /// <summary>
    /// NestoAPI#622: lo que la pantalla «Enviar reposición» dice arriba de lo que ha hecho hoy el job con una ruta de ese
    /// origen (GET api/Reposiciones/AvisosAutomatica).
    /// </summary>
    public class AvisoReposicionAutomaticaDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        public DateTime? Corte { get; set; }
        /// <summary>«Omitida» (no se ha rellenado: el motivo va en el texto) o «Completada» (sumada a una sin enviar).</summary>
        public string Tipo { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#622: una cabecera del job de hoy de un origen, con lo que hace falta para avisar a la tienda.</summary>
    public class CabeceraReposicionAutomaticaDelDia
    {
        public int Id { get; set; }
        public string Destino { get; set; }
        public DateTime? FechaCorte { get; set; }
        public int? NumTraspaso { get; set; }
        public string Omitida { get; set; }
        /// <summary>El día de la línea más antigua que el origen tiene en preparación hacia ese destino (null si no hay ninguna).</summary>
        public DateTime? PreparacionDesde { get; set; }
    }

    public interface IServicioReposicionAutomatica
    {
        /// <summary>Ver <see cref="ServicioReposicionAutomatica.RellenarRuta"/>.</summary>
        Task<ResultadoReposicionAutomaticaDTO> RellenarRuta(string empresa, string origen, string destino, IPrincipal usuario);
        /// <summary>Ver <see cref="ServicioReposicionAutomatica.LeerAvisosDeHoy"/>.</summary>
        Task<List<AvisoReposicionAutomaticaDTO>> LeerAvisosDeHoy(string empresa, string origen);
    }

    public interface IRepositorioReposicionAutomatica
    {
        /// <summary>¿Existe ReposicionesTraspasos con la columna Omitida? Sin ella el job no sabe qué ha hecho ya.</summary>
        Task<bool> TablaPreparada();
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);
        /// <summary>
        /// ¿Hay ya una cabecera del job (Herramienta 'Automatico') para esa ruta con el corte (FechaCorte) en el día
        /// <paramref name="diaCorte"/>, a cualquier hora? Con <paramref name="contarOmitidas"/>, también las marcas sin
        /// reposición (vacía, ya había una, fuera de plazo…). Ver <see cref="ServicioReposicionAutomatica"/>, «día ya tratado».
        /// </summary>
        Task<bool> HayCabeceraDelDia(string empresa, string origen, string destino, DateTime diaCorte, bool contarOmitidas);
        /// <summary>
        /// NestoAPI#577: el traspaso de reposición de esa ruta (mismo origen y destino) creado en <paramref name="corte"/> o
        /// después, con cualquier herramienta, en preparación, salido o recibido (el más antiguo), o null si no hay. Con uno,
        /// el corte ya está cubierto y el job no rellena otro (09/10/26: la 80932 duplicó la 80929 de Andre en Nesto viejo).
        /// </summary>
        Task<TraspasoReposicionExistente> BuscarTraspasoQueCubre(string empresa, string origen, string destino, DateTime corte);
        /// <summary>Marca sin reposición (NumTraspaso NULL, Omitida = motivo) para no reintentar ese corte.</summary>
        Task ApuntarOmitida(CabeceraReposicionTraspaso cabecera, string motivo);
        /// <summary>Exclusión entre ejecuciones (job, endpoint, dos servidores): applock por origen y día. Dispose lo suelta.</summary>
        Task<IDisposable> Bloquear(string empresa, string origen, DateTime dia);
        /// <summary>
        /// NestoAPI#622: las cabeceras del job (Herramienta 'Automatico') de las rutas de <paramref name="origen"/> creadas desde
        /// <paramref name="desde"/>, con lo que el origen tiene en preparación hacia cada destino.
        /// </summary>
        Task<List<CabeceraReposicionAutomaticaDelDia>> LeerCabecerasDelDia(string empresa, string origen, DateTime desde);
    }

    /// <summary>
    /// NestoAPI#577 (corte 3b): rellena cada reposición del calendario (ReposicionesCalendario) a su hora de corte.
    ///
    /// <para><b>El corte es un dato</b>, como el horizonte del picking de cierre (NestoAPI#361): para cada reposición
    /// que cierra (o llega) HOY se calcula el instante día de cierre + HoraCierre y, si ya ha pasado y esa ruta no tiene todavía cabecera del job con el corte
    /// ese día (ReposicionesTraspasos: Herramienta 'Automatico' + FechaCorte), se llama a
    /// <see cref="ServicioPreparacionReposicion.Crear(CrearReposicionDTO, IPrincipal, DateTime?)"/> con ESE instante (no
    /// con DateTime.Now): solo cuentan las líneas de pedido anteriores. Da igual que Hangfire arranque a las 10:00:03 o a
    /// las 10:05: el resultado es el mismo. Festivos y fines de semana como <see cref="CalculadoraFechaReposicion"/>.</para>
    ///
    /// <para><b>Cierre con antelación</b> (corte 3d, decisión de Carlos 08/10): la fila es la del día de LLEGADA y se cierra
    /// LaborablesAntelacionCierre laborables del origen antes (Algete → tienda: el laborable anterior a las 13:00; la del
    /// lunes, el viernes). Cada pasada mira <see cref="CalculadoraFechaReposicion.CortesDeHoy"/>: las que cierran hoy (la
    /// del lunes se rellena el viernes a las 13:00, con ese instante como corte) y las que llegan hoy (si el viernes no se
    /// rellenó, el lunes aún se rellena, con el corte del viernes, hasta la hora de llegada; después, fuera de plazo). La
    /// cabecera se identifica por ruta + día del corte (FechaCorte), así que las dos vías no duplican.</para>
    ///
    /// <para><b>Día ya tratado</b> (09/10/26, al poder cambiar el calendario desde Nesto): una ruta se da por tratada para
    /// un viaje si ya hay CUALQUIER cabecera del job de esa ruta (empresa, origen, destino) con el corte en el mismo DÍA DE
    /// CIERRE que el corte de ahora, sea cual sea la hora. Antes se buscaba el instante exacto, y cambiar hoy la hora de
    /// cierre de una ruta ya rellenada hacía que el job viera un corte nuevo sin cabecera y la rellenara otra vez (o avisara
    /// de fuera de plazo). Se compara el día de cierre y no el de llegada porque la cabecera solo guarda FechaCorte; con
    /// antelación es el mismo viaje: la del lunes se cierra el viernes, y el lunes (vía «las que llegan hoy») su corte
    /// vuelve a ser del viernes, que es donde está la cabecera. Dentro de una ruta, día de cierre y día de llegada se
    /// corresponden uno a uno porque todas sus filas activas tienen la misma antelación y una sola fila por día de llegada
    /// (lo exige <see cref="ServicioCalendarioReposiciones"/>). La hora nueva vale desde el siguiente viaje.</para>
    ///
    /// <para><b>Exclusión</b>: applock por ORIGEN y día DE CIERRE (el del instante de corte, no por ruta): Algete → Reina y
    /// Algete → Alcobendas escriben en el mismo diario de salida de Algete y no deben solaparse. El job y el endpoint manual
    /// usan la misma clave (el día del corte), así que una misma reposición siempre se bloquea con el mismo recurso. Dentro
    /// del bloqueo se vuelve a mirar si ya está hecha.</para>
    ///
    /// <para><b>Qué se apunta</b> (para no reintentar cada 5 minutos): la reposición creada lleva su cabecera; si no se crea
    /// por un motivo de negocio, una marca en ReposicionesTraspasos sin número y con Omitida = el motivo:
    /// propuesta vacía (sin aviso: es normal), la tienda ya tenía una en preparación que no se puede completar (no se crea
    /// otra; aviso informativo a ELMAH), otro error de negocio (aviso) o fuera de plazo (aviso). Un fallo inesperado (BD,
    /// bloqueo, timeout) se avisa y NO se apunta: la siguiente pasada lo reintenta. Un error en una ruta no para las demás.</para>
    ///
    /// <para><b>Preparación sin enviar</b> (NestoAPI#622): si a la hora de corte la tienda aún tiene sin enviar la de un viaje
    /// anterior, la de hoy se SUMA a ella (<see cref="ServicioPreparacionReposicion.Completar"/>, resultado
    /// <see cref="ResultadosReposicionAutomatica.COMPLETADA"/>) en vez de omitirse. Lo mismo al relanzarla a mano.</para>
    ///
    /// <para><b>Aviso a la tienda</b> (NestoAPI#622): cuando la de hoy se completa, no se puede rellenar (ya en preparación sin
    /// poder completarla, inventario en curso…) o se queda fuera de plazo, además de ELMAH va a la campana de Nesto de la
    /// gente del origen (<see cref="IAvisadorReposicionAutomatica"/>). La pantalla «Enviar reposición» lo lee con
    /// <see cref="LeerAvisosDeHoy"/>. Vacía y ya cubierta no van a la campana (son normales; la pantalla sí lo dice).</para>
    ///
    /// <para><b>Fuera de plazo</b>: si a la hora de llegada habitual de esa fila aún no se ha rellenado (la API estuvo caída),
    /// ya no se rellena sola: ese viaje ha pasado. Se puede relanzar a mano con <see cref="RellenarRuta"/>.</para>
    /// </summary>
    public class ServicioReposicionAutomatica : IServicioReposicionAutomatica
    {
        /// <summary>El usuario con el que el job escribe (PreExtrProducto.Usuario, ReposicionesTraspasos.UsuarioCreacion).</summary>
        public const string USUARIO_AUTOMATICO = "ReposicionAutomatica";
        private const string USUARIO_ELMAH = "Sistema (reposición automática)";

        private readonly IRepositorioReposicionAutomatica repositorio;
        private readonly Func<CrearReposicionDTO, IPrincipal, DateTime?, Task<ReposicionEnPreparacionDTO>> crear;
        private readonly CalculadoraFechaReposicion calculadora;
        private readonly Func<DateTime> reloj;
        private readonly Action<Exception> avisar;
        private readonly Func<CrearReposicionDTO, IPrincipal, DateTime, Task<ResultadoCompletarReposicionDTO>> completar;
        private readonly IAvisadorReposicionAutomatica avisarTienda;

        public ServicioReposicionAutomatica(NVEntities db)
            : this(new RepositorioReposicionAutomaticaSql(db),
                  (peticion, usuario, corte) => new ServicioPreparacionReposicion(db).Crear(peticion, usuario, corte),
                  new CalculadoraFechaReposicion(),
                  () => DateTime.Now,
                  ex => ElmahHelper.Log(ex, USUARIO_ELMAH),
                  (peticion, usuario, corte) => new ServicioPreparacionReposicion(db).Completar(peticion, usuario, corte),
                  new AvisadorReposicionAutomatica(db))
        {
        }

        /// <param name="completar">
        /// NestoAPI#622: (petición, usuario, corte) → suma la de hoy a la preparación sin enviar. Null: como antes de #622 (se
        /// omite con «ya en preparación»).
        /// </param>
        /// <param name="avisarTienda">NestoAPI#622: la campana de la gente del origen. Null: no se avisa.</param>
        internal ServicioReposicionAutomatica(IRepositorioReposicionAutomatica repositorio,
            Func<CrearReposicionDTO, IPrincipal, DateTime?, Task<ReposicionEnPreparacionDTO>> crear,
            CalculadoraFechaReposicion calculadora, Func<DateTime> reloj, Action<Exception> avisar,
            Func<CrearReposicionDTO, IPrincipal, DateTime, Task<ResultadoCompletarReposicionDTO>> completar = null,
            IAvisadorReposicionAutomatica avisarTienda = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.crear = crear ?? throw new ArgumentNullException(nameof(crear));
            this.calculadora = calculadora ?? throw new ArgumentNullException(nameof(calculadora));
            this.reloj = reloj ?? (() => DateTime.Now);
            this.avisar = avisar ?? (ex => { });
            this.completar = completar;
            this.avisarTienda = avisarTienda;
        }

        /// <summary>
        /// Con quién escribe: <paramref name="nombre"/> con el rol Almacén, que puede crear desde cualquier origen (Algete
        /// incluido, ServicioPreparacionReposicion.PuedeEscribir). El job usa <see cref="USUARIO_AUTOMATICO"/>; el endpoint
        /// manual, el nombre de quien lo lanza (para la auditoría).
        /// </summary>
        internal static IPrincipal Principal(string nombre)
        {
            var identidad = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, nombre),
                new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
            }, "NestoAPI#577");
            return new ClaimsPrincipal(identidad);
        }

        /// <summary>El job: todas las rutas de hoy cuyo corte ya ha pasado y aún no están hechas.</summary>
        public async Task<List<ResultadoReposicionAutomaticaDTO>> RellenarPendientes(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            var resultados = new List<ResultadoReposicionAutomaticaDTO>();
            if (!await repositorio.TablaPreparada().ConfigureAwait(false))
            {
                return resultados;
            }
            DateTime ahora = reloj();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresa).ConfigureAwait(false);
            IPrincipal automatico = Principal(USUARIO_AUTOMATICO);

            foreach (CorteReposicion corte in calculadora.CortesDeHoy(calendario, ahora).Where(c => c.Corte <= ahora))
            {
                try
                {
                    using (await repositorio.Bloquear(corte.Empresa, corte.Origen, corte.Corte.Date).ConfigureAwait(false))
                    {
                        if (await repositorio.HayCabeceraDelDia(corte.Empresa, corte.Origen, corte.Destino, corte.Corte.Date, contarOmitidas: true).ConfigureAwait(false))
                        {
                            continue;
                        }
                        // NestoAPI#577: alguien ya la ha rellenado después del corte (Nesto viejo no escribe cabecera). Antes que
                        // el fuera de plazo: si ya está hecha, no hay nada que avisar.
                        TraspasoReposicionExistente cubre = await repositorio.BuscarTraspasoQueCubre(corte.Empresa, corte.Origen, corte.Destino, corte.Corte).ConfigureAwait(false);
                        if (cubre != null)
                        {
                            string motivo = MotivoCubierta(cubre);
                            await Apuntar(corte, automatico, motivo).ConfigureAwait(false);
                            resultados.Add(Resultado(corte, ResultadosReposicionAutomatica.YA_CUBIERTA, motivo));
                            continue;
                        }
                        if (ahora >= corte.Llegada)
                        {
                            string motivo = $"Fuera de plazo: a las {corte.Llegada:HH:mm} (llegada habitual) aún no se había rellenado. " +
                                "Se puede relanzar a mano (POST api/Reposiciones/RellenarAutomatica).";
                            await Apuntar(corte, automatico, motivo).ConfigureAwait(false);
                            avisar(new Exception(Prefijo(corte) + motivo));
                            await AvisarNoRellenada(corte, $"A las {corte.Llegada:HH:mm} (llegada habitual) aún no se había rellenado: " +
                                "ese viaje ya ha pasado. Pide a Almacén que la relance si hace falta.").ConfigureAwait(false);
                            resultados.Add(Resultado(corte, ResultadosReposicionAutomatica.FUERA_DE_PLAZO, motivo));
                            continue;
                        }
                        resultados.Add(await Intentar(corte, automatico, manual: false).ConfigureAwait(false));
                    }
                }
                catch (Exception ex)
                {
                    // Inesperado: sin marca, para que la siguiente pasada lo reintente. Las demás rutas siguen.
                    avisar(new Exception(Prefijo(corte) + "Error al rellenarla: " + ex.Message, ex));
                    resultados.Add(Resultado(corte, ResultadosReposicionAutomatica.ERROR, ex.Message));
                }
            }
            return resultados;
        }

        /// <summary>
        /// POST api/Reposiciones/RellenarAutomatica: relanza a mano la de hoy de una ruta, con el corte del calendario (el
        /// último que ya ha pasado de las que cierran o llegan hoy: con antelación, el lunes relanza la del lunes con el corte
        /// del viernes). 404 si hoy no toca (o es festivo); 409 si aún no ha llegado la hora de cierre,
        /// si ya está rellena (una marca sin reposición no cuenta: se puede relanzar) o si el origen ya tiene una en
        /// preparación que no se puede completar (NestoAPI#622: si se puede, se le suma la de hoy, como en el job).
        /// Escribe con el nombre de quien lo lanza y Herramienta 'Automatico'.
        /// </summary>
        public async Task<ResultadoReposicionAutomaticaDTO> RellenarRuta(string empresa, string origen, string destino, IPrincipal usuario)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string origenLimpio = origen?.Trim().ToUpperInvariant();
            string destinoLimpio = destino?.Trim().ToUpperInvariant();
            if (!await repositorio.TablaPreparada().ConfigureAwait(false))
            {
                throw new NestoBusinessException("Falta lanzar Scripts/Issue577_ReposicionesTraspasos.sql en la base de datos.")
                {
                    StatusCode = HttpStatusCode.Conflict
                };
            }
            DateTime ahora = reloj();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresaLimpia).ConfigureAwait(false);
            List<CorteReposicion> deHoy = calculadora.CortesDeHoy(calendario, ahora)
                .Where(c => c.Empresa == empresaLimpia && c.Origen == origenLimpio && c.Destino == destinoLimpio)
                .ToList();
            if (!deHoy.Any())
            {
                throw new NestoBusinessException($"Hoy no hay reposición de {origenLimpio} a {destinoLimpio} en el calendario (o es festivo en alguno de los dos).")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
            CorteReposicion corte = deHoy.Where(c => c.Corte <= ahora).OrderByDescending(c => c.Corte).FirstOrDefault();
            if (corte == null)
            {
                throw new NestoBusinessException($"La reposición de hoy de {origenLimpio} a {destinoLimpio} cierra a las {deHoy.Where(c => c.Corte > ahora).Min(c => c.Corte):HH:mm}: " +
                    "se rellenará sola a esa hora.")
                {
                    StatusCode = HttpStatusCode.Conflict
                };
            }
            string nombre = usuario?.Identity?.Name;
            IPrincipal quien = Principal(string.IsNullOrWhiteSpace(nombre) ? USUARIO_AUTOMATICO : nombre);
            using (await repositorio.Bloquear(corte.Empresa, corte.Origen, corte.Corte.Date).ConfigureAwait(false))
            {
                if (await repositorio.HayCabeceraDelDia(corte.Empresa, corte.Origen, corte.Destino, corte.Corte.Date, contarOmitidas: false).ConfigureAwait(false))
                {
                    throw new NestoBusinessException($"La reposición de {origenLimpio} a {destinoLimpio} de las {corte.Corte:HH:mm} {DiaDelCorte(corte, ahora)} ya está rellena.")
                    {
                        StatusCode = HttpStatusCode.Conflict
                    };
                }
                return await Intentar(corte, quien, manual: true).ConfigureAwait(false);
            }
        }

        private async Task<ResultadoReposicionAutomaticaDTO> Intentar(CorteReposicion corte, IPrincipal quien, bool manual)
        {
            var peticion = new CrearReposicionDTO
            {
                Empresa = corte.Empresa,
                Origen = corte.Origen,
                Destino = corte.Destino,
                Herramienta = HerramientasReposicion.AUTOMATICO
            };
            try
            {
                ReposicionEnPreparacionDTO creada;
                try
                {
                    creada = await crear(peticion, quien, corte.Corte).ConfigureAwait(false);
                }
                catch (ReposicionYaEnPreparacionException) when (completar != null)
                {
                    // NestoAPI#622: la de hoy se suma a la que la tienda tiene sin enviar. Si no se puede (otro destino,
                    // Algete), Completar vuelve a lanzar ReposicionYaEnPreparacionException y sigue como antes (abajo).
                    ResultadoCompletarReposicionDTO completada = await completar(peticion, quien, corte.Corte).ConfigureAwait(false);
                    if (completada?.Completada != true)
                    {
                        creada = completada?.Reposicion;
                    }
                    else
                    {
                        return await Completada(corte, completada).ConfigureAwait(false);
                    }
                }
                ResultadoReposicionAutomaticaDTO resultado = Resultado(corte, ResultadosReposicionAutomatica.CREADA, null);
                resultado.NumTraspaso = creada?.NumTraspaso;
                resultado.Lineas = creada?.Lineas?.Count ?? 0;
                return resultado;
            }
            catch (ReposicionVaciaException ex)
            {
                await Apuntar(corte, quien, ex.Message).ConfigureAwait(false);
                return Resultado(corte, ResultadosReposicionAutomatica.VACIA, ex.Message);
            }
            catch (ReposicionYaEnPreparacionException ex) when (!manual)
            {
                await Apuntar(corte, quien, ex.Message).ConfigureAwait(false);
                avisar(new Exception(Prefijo(corte) + "No se rellena: " + ex.Message));
                await AvisarNoRellenada(corte, ex.Message).ConfigureAwait(false);
                return Resultado(corte, ResultadosReposicionAutomatica.YA_EN_PREPARACION, ex.Message);
            }
            catch (NestoBusinessException ex) when (!manual && !(ex is ReposicionYaEnPreparacionException))
            {
                await Apuntar(corte, quien, ex.Message).ConfigureAwait(false);
                avisar(new Exception(Prefijo(corte) + "No se puede rellenar: " + ex.Message, ex));
                await AvisarNoRellenada(corte, ex.Message).ConfigureAwait(false);
                return Resultado(corte, ResultadosReposicionAutomatica.NO_SE_PUEDE, ex.Message);
            }
        }

        /// <summary>NestoAPI#622: la de hoy se ha sumado a la que la tienda tenía sin enviar. Se le avisa (no es un error: sin ELMAH).</summary>
        private async Task<ResultadoReposicionAutomaticaDTO> Completada(CorteReposicion corte, ResultadoCompletarReposicionDTO completada)
        {
            string texto = TextoCompletada(corte.Destino, corte.Corte, completada.PreparacionDesde, completada.LineasNuevas, completada.LineasSubidas, reloj());
            ResultadoReposicionAutomaticaDTO resultado = Resultado(corte, ResultadosReposicionAutomatica.COMPLETADA, texto);
            resultado.Lineas = completada.LineasNuevas + completada.LineasSubidas;
            await AvisarTienda(corte, $"Tienes sin enviar la reposición a {corte.Destino}", texto).ConfigureAwait(false);
            return resultado;
        }

        private Task AvisarNoRellenada(CorteReposicion corte, string motivo)
        {
            return AvisarTienda(corte, $"La reposición a {corte.Destino} no se ha podido rellenar",
                $"La reposición a {corte.Destino} ({Corte(corte.Corte, reloj())}) no se ha rellenado sola: {motivo}");
        }

        /// <summary>La campana es un extra: si falla, a ELMAH y el job sigue.</summary>
        private async Task AvisarTienda(CorteReposicion corte, string titulo, string cuerpo)
        {
            if (avisarTienda == null)
            {
                return;
            }
            try
            {
                await avisarTienda.Avisar(corte.Empresa, corte.Origen, titulo, cuerpo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                avisar(new Exception(Prefijo(corte) + "No se ha podido avisar a la tienda: " + ex.Message, ex));
            }
        }

        /// <summary>
        /// GET api/Reposiciones/AvisosAutomatica (NestoAPI#622): lo que «Enviar reposición» dice arriba de lo que ha hecho hoy el
        /// job con cada ruta de <paramref name="origen"/> (la última cabecera de hoy de cada destino): si no se ha rellenado, por
        /// qué (vacía, ya cubierta, inventario…); si se ha sumado a una sin enviar y sigue sin enviar, que la envíe. Si se ha
        /// creado normal o ya se ha enviado, nada. Lista vacía sin la tabla (script de #577 sin lanzar).
        /// </summary>
        public async Task<List<AvisoReposicionAutomaticaDTO>> LeerAvisosDeHoy(string empresa, string origen)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string origenLimpio = origen?.Trim().ToUpperInvariant();
            var avisos = new List<AvisoReposicionAutomaticaDTO>();
            if (string.IsNullOrEmpty(origenLimpio) || !await repositorio.TablaPreparada().ConfigureAwait(false))
            {
                return avisos;
            }
            DateTime ahora = reloj();
            List<CabeceraReposicionAutomaticaDelDia> cabeceras = await repositorio.LeerCabecerasDelDia(empresaLimpia, origenLimpio, ahora.Date).ConfigureAwait(false)
                ?? new List<CabeceraReposicionAutomaticaDelDia>();
            foreach (CabeceraReposicionAutomaticaDelDia cabecera in cabeceras
                .GroupBy(c => c.Destino?.Trim())
                .Select(g => g.OrderByDescending(c => c.Id).First())
                .OrderBy(c => c.Destino?.Trim(), StringComparer.Ordinal))
            {
                string destino = cabecera.Destino?.Trim();
                if (!string.IsNullOrWhiteSpace(cabecera.Omitida))
                {
                    avisos.Add(new AvisoReposicionAutomaticaDTO
                    {
                        Origen = origenLimpio, Destino = destino, Corte = cabecera.FechaCorte, Tipo = TIPO_AVISO_OMITIDA,
                        Texto = $"La reposición a {destino} ({Corte(cabecera.FechaCorte, ahora)}) no se ha rellenado sola: {cabecera.Omitida.Trim()}"
                    });
                }
                else if (cabecera.NumTraspaso == null && cabecera.PreparacionDesde.HasValue && cabecera.PreparacionDesde.Value.Date < ahora.Date)
                {
                    // Sin enviar y con líneas de antes de hoy: la de hoy se sumó a la de un viaje anterior
                    avisos.Add(new AvisoReposicionAutomaticaDTO
                    {
                        Origen = origenLimpio, Destino = destino, Corte = cabecera.FechaCorte, Tipo = TIPO_AVISO_COMPLETADA,
                        Texto = TextoCompletada(destino, cabecera.FechaCorte, cabecera.PreparacionDesde, null, null, ahora)
                    });
                }
            }
            return avisos;
        }

        public const string TIPO_AVISO_OMITIDA = "Omitida";
        public const string TIPO_AVISO_COMPLETADA = "Completada";

        /// <summary>
        /// «La reposición a ALG (corte de hoy a las 09:00) se ha sumado a la que tenías sin enviar del 09/10: 3 productos nuevos y 2
        /// con más cantidad. Envíala desde «Enviar reposición».» Sin cuentas (la pantalla), sin esa parte.
        /// </summary>
        internal static string TextoCompletada(string destino, DateTime? corte, DateTime? desde, int? nuevas, int? subidas, DateTime ahora)
        {
            string deCuando = desde.HasValue ? string.Format(CultureInfo.InvariantCulture, " del {0:dd/MM}", desde.Value) : string.Empty;
            string inicio = $"La reposición a {destino} ({Corte(corte, ahora)})";
            string cuerpo;
            if (nuevas == null || subidas == null)
            {
                cuerpo = $"{inicio} se ha sumado a la que tenías sin enviar{deCuando}.";
            }
            else if (nuevas.Value + subidas.Value == 0)
            {
                cuerpo = $"{inicio} no tenía nada nuevo: sigue pendiente la que tenías sin enviar{deCuando}.";
            }
            else
            {
                cuerpo = $"{inicio} se ha sumado a la que tenías sin enviar{deCuando}: " +
                    $"{Productos(nuevas.Value, "nuevo", "nuevos")} y {Productos(subidas.Value, "con más cantidad", "con más cantidad")}.";
            }
            return cuerpo + " Envíala desde «Enviar reposición».";
        }

        private static string Productos(int cuantos, string singular, string plural)
        {
            return cuantos == 1 ? $"1 producto {singular}" : $"{cuantos} productos {plural}";
        }

        /// <summary>«corte de hoy a las 09:00» o «corte del 16/10 a las 13:00».</summary>
        private static string Corte(DateTime? corte, DateTime ahora)
        {
            if (!corte.HasValue)
            {
                return "sin corte";
            }
            return corte.Value.Date == ahora.Date
                ? string.Format(CultureInfo.InvariantCulture, "corte de hoy a las {0:HH:mm}", corte.Value)
                : string.Format(CultureInfo.InvariantCulture, "corte del {0:dd/MM} a las {0:HH:mm}", corte.Value);
        }

        private Task Apuntar(CorteReposicion corte, IPrincipal quien, string motivo)
        {
            return repositorio.ApuntarOmitida(new CabeceraReposicionTraspaso
            {
                Empresa = corte.Empresa,
                Origen = corte.Origen,
                Destino = corte.Destino,
                Herramienta = HerramientasReposicion.AUTOMATICO,
                UsuarioCreacion = quien?.Identity?.Name ?? USUARIO_AUTOMATICO,
                FechaCreacion = reloj(),
                FechaCorte = corte.Corte
            }, motivo);
        }

        /// <summary>«Ya la rellenó Andre el 08/10 13:55 (traspaso 80929)», recortado a los 300 de ReposicionesTraspasos.Omitida.</summary>
        internal static string MotivoCubierta(TraspasoReposicionExistente traspaso)
        {
            string usuario = traspaso.Usuario?.Trim();
            if (!string.IsNullOrEmpty(usuario) && usuario.Contains("\\"))
            {
                usuario = usuario.Substring(usuario.LastIndexOf('\\') + 1);
            }
            string quien = string.IsNullOrEmpty(usuario) ? "Ya se rellenó" : "Ya la rellenó " + usuario;
            string cual = traspaso.NumTraspaso.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "traspaso {0}", traspaso.NumTraspaso.Value)
                : "en preparación, aún sin número de traspaso";
            string motivo = string.Format(CultureInfo.InvariantCulture, "{0} el {1:dd/MM HH:mm} ({2})", quien, traspaso.Fecha, cual);
            return motivo.Length > 300 ? motivo.Substring(0, 300) : motivo;
        }

        private static string DiaDelCorte(CorteReposicion corte, DateTime ahora)
        {
            return corte.Corte.Date == ahora.Date ? "de hoy" : string.Format(CultureInfo.InvariantCulture, "del {0:dd/MM}", corte.Corte);
        }

        private static string Prefijo(CorteReposicion corte)
        {
            return string.Format(CultureInfo.InvariantCulture, "[Reposición automática #577] {0} → {1}, corte {2:dd/MM/yyyy HH:mm}: ",
                corte.Origen, corte.Destino, corte.Corte);
        }

        private static ResultadoReposicionAutomaticaDTO Resultado(CorteReposicion corte, string resultado, string mensaje)
        {
            return new ResultadoReposicionAutomaticaDTO
            {
                Origen = corte.Origen,
                Destino = corte.Destino,
                Corte = corte.Corte,
                Resultado = resultado,
                Mensaje = mensaje
            };
        }
    }

    public class RepositorioReposicionAutomaticaSql : IRepositorioReposicionAutomatica
    {
        private const int ESPERA_BLOQUEO_MS = 30000;

        internal const string SQL_TABLA_PREPARADA =
            "SELECT CAST(CASE WHEN COL_LENGTH('dbo.ReposicionesTraspasos', 'Omitida') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_HAY_CABECERA = @"
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ReposicionesTraspasos
    WHERE Empresa = @p0 AND Origen = @p1 AND Destino = @p2 AND Herramienta = 'Automatico'
      AND FechaCorte >= @p3 AND FechaCorte < DATEADD(day, 1, @p3)
      AND (@p4 = 1 OR Omitida IS NULL)) THEN 1 ELSE 0 END AS bit)";

        /// <summary>
        /// NestoAPI#577: ¿hay ya un traspaso de reposición de la ruta @p1 → @p2 creado en @p3 (el corte) o después? Con
        /// cualquier herramienta: las nuevas dejan cabecera en ReposicionesTraspasos, Nesto viejo no. Las líneas se reconocen
        /// por el almacén destino y el texto que ponen las dos herramientas («Traspaso por reposición de almacén ALG a REI»,
        /// @p4), en los diarios de reposición de la tabla Almacenes:
        /// <list type="bullet">
        /// <item>ReposicionesTraspasos (no Omitida): FechaCreacion.</item>
        /// <item>PreExtrProducto en el diario de salida del origen (en preparación, o en Algete por recoger) o en el de entrada
        /// del destino (salida, pendiente de recibir; p. ej. PendRepo): MIN([Fecha Modificación]), el instante en que se
        /// insertaron las líneas. Fecha no vale: en preparación es solo el día, y al terminar se pisa con la hora de salida
        /// (80929: líneas a las 13:55, Fecha 15:20).</item>
        /// <item>ExtractoProducto en el diario de entrada del destino (ya recibida): Fecha, la de salida que trae de
        /// PreExtrProducto ([Fecha Modificación] y Usuario ahí son los de la recepción, por eso Usuario va NULL). Acotada por
        /// Diario (índice) y Fecha &gt;= corte: nunca un barrido de la tabla.</item>
        /// </list>
        /// Sugerencia 581: las reposiciones PUNTUALES (Herramienta 'Puntual', lo que una tienda manda aparte a Algete) no
        /// cubren el viaje: se descartan en las tres partes (por su cabecera, que siempre nace con número). Si no, una
        /// puntual hecha justo después de la hora de cierre y antes de la pasada del job dejaría ese día sin la automática.
        /// </summary>
        internal const string SQL_TRASPASO_QUE_CUBRE = @"
DECLARE @salida char(10) = (SELECT DiarioSalidaRep FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1);
DECLARE @entrada char(10) = (SELECT DiarioEntradaRep FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p2);
SELECT TOP 1 x.NumTraspaso, x.Usuario, x.Fecha FROM (
    SELECT r.NumTraspaso, CAST(RTRIM(r.UsuarioCreacion) AS varchar(30)) AS Usuario, r.FechaCreacion AS Fecha
    FROM dbo.ReposicionesTraspasos r
    WHERE r.Empresa = @p0 AND r.Origen = @p1 AND r.Destino = @p2 AND r.Omitida IS NULL AND r.FechaCreacion >= @p3
      AND r.Herramienta <> 'Puntual'
    UNION ALL
    SELECT p.[NºTraspaso], MIN(RTRIM(p.Usuario)), MIN(p.[Fecha Modificación])
    FROM PreExtrProducto p
    WHERE p.Empresa = @p0 AND p.Diario IN (@salida, @entrada) AND p.[Almacén] = @p2 AND p.Texto = @p4 AND p.Estado >= 0
      AND NOT EXISTS (SELECT 1 FROM dbo.ReposicionesTraspasos t
                      WHERE t.Empresa = @p0 AND t.NumTraspaso = p.[NºTraspaso] AND t.Herramienta = 'Puntual')
    GROUP BY p.[NºTraspaso]
    HAVING MIN(p.[Fecha Modificación]) >= @p3
    UNION ALL
    SELECT e.[NºTraspaso], CAST(NULL AS varchar(30)), MIN(e.Fecha)
    FROM ExtractoProducto e
    WHERE e.Diario = @entrada AND e.Fecha >= @p3 AND e.Empresa = @p0 AND e.[Almacén] = @p2 AND e.Texto = @p4
      AND NOT EXISTS (SELECT 1 FROM dbo.ReposicionesTraspasos t
                      WHERE t.Empresa = @p0 AND t.NumTraspaso = e.[NºTraspaso] AND t.Herramienta = 'Puntual')
    GROUP BY e.[NºTraspaso]
) x ORDER BY x.Fecha";

        /// <summary>
        /// NestoAPI#622: las cabeceras del job de hoy de las rutas de un origen, y desde qué día tiene el origen líneas en
        /// preparación hacia cada destino (las mismas que LeerLineasEnPreparacion: su diario de salida, sin traspaso).
        /// </summary>
        internal const string SQL_CABECERAS_DEL_DIA = @"
DECLARE @salida char(10) = (SELECT DiarioSalidaRep FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1);
SELECT r.Id, RTRIM(r.Destino) AS Destino, r.FechaCorte, r.NumTraspaso, r.Omitida,
       (SELECT MIN(p.Fecha) FROM PreExtrProducto p
        WHERE p.Empresa = @p0 AND p.Diario = @salida AND p.[Almacén] = r.Destino AND p.Estado >= 0
          AND ISNULL(p.[NºTraspaso], 0) = 0) AS PreparacionDesde
FROM dbo.ReposicionesTraspasos r
WHERE r.Empresa = @p0 AND r.Origen = @p1 AND r.Herramienta = 'Automatico' AND r.FechaCreacion >= @p2";

        internal const string SQL_APUNTAR_OMITIDA = @"
INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte, Omitida)
VALUES (@p0, NULL, @p1, @p2, @p3, @p4, @p5, @p6, @p7)";

        // Ámbito Session (como GestorPicking, NestoAPI#405): Crear abre y cierra su propia transacción dentro del bloqueo, así
        // que no puede ir ligado a una transacción. La conexión se abre a mano para que EF no la devuelva al pool a mitad.
        internal const string SQL_BLOQUEAR = @"
DECLARE @resultado int;
EXEC @resultado = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @p1;
IF @resultado < 0 RAISERROR('Otra ejecución está rellenando las reposiciones de este almacén. Se reintentará en la siguiente pasada.', 16, 1);";

        internal const string SQL_SOLTAR = "EXEC sp_releaseapplock @Resource = @p0, @LockOwner = 'Session';";

        private readonly NVEntities db;

        public RepositorioReposicionAutomaticaSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal static string Recurso(string empresa, string origen, DateTime dia)
        {
            return string.Format(CultureInfo.InvariantCulture, "ReposicionAutomatica:{0}:{1}:{2:yyyyMMdd}", empresa?.Trim(), origen?.Trim(), dia);
        }

        public Task<bool> TablaPreparada()
        {
            return db.Database.SqlQuery<bool>(SQL_TABLA_PREPARADA).SingleAsync();
        }

        public async Task<List<ReposicionCalendario>> LeerCalendario(string empresa)
        {
            return await db.ReposicionesCalendario.AsNoTracking()
                .Where(f => f.Empresa == empresa && f.Activo)
                .ToListAsync().ConfigureAwait(false);
        }

        public Task<bool> HayCabeceraDelDia(string empresa, string origen, string destino, DateTime diaCorte, bool contarOmitidas)
        {
            return db.Database.SqlQuery<bool>(SQL_HAY_CABECERA,
                RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", destino, 3), RegistroReposicionesTraspasosSql.Fecha("@p3", diaCorte.Date),
                new System.Data.SqlClient.SqlParameter("@p4", SqlDbType.Bit) { Value = contarOmitidas }).SingleAsync();
        }

        public async Task<TraspasoReposicionExistente> BuscarTraspasoQueCubre(string empresa, string origen, string destino, DateTime corte)
        {
            List<TraspasoReposicionExistente> filas = await db.Database.SqlQuery<TraspasoReposicionExistente>(SQL_TRASPASO_QUE_CUBRE,
                RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", destino, 3), RegistroReposicionesTraspasosSql.Fecha("@p3", corte),
                RegistroReposicionesTraspasosSql.Char("@p4", ServicioPreparacionReposicion.Texto(origen?.Trim(), destino?.Trim()), 50))
                .ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public Task<List<CabeceraReposicionAutomaticaDelDia>> LeerCabecerasDelDia(string empresa, string origen, DateTime desde)
        {
            return db.Database.SqlQuery<CabeceraReposicionAutomaticaDelDia>(SQL_CABECERAS_DEL_DIA,
                RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                RegistroReposicionesTraspasosSql.Fecha("@p2", desde)).ToListAsync();
        }

        public Task ApuntarOmitida(CabeceraReposicionTraspaso cabecera, string motivo)
        {
            string texto = string.IsNullOrWhiteSpace(motivo) ? "Sin motivo" : motivo.Trim();
            return db.Database.ExecuteSqlCommandAsync(SQL_APUNTAR_OMITIDA,
                RegistroReposicionesTraspasosSql.Char("@p0", cabecera.Empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", cabecera.Origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", cabecera.Destino, 3), RegistroReposicionesTraspasosSql.VarChar("@p3", cabecera.Herramienta, 20),
                RegistroReposicionesTraspasosSql.VarChar("@p4", UsuarioAuditoriaHelper.ParaAuditoria(cabecera.UsuarioCreacion), 30),
                RegistroReposicionesTraspasosSql.Fecha("@p5", cabecera.FechaCreacion), RegistroReposicionesTraspasosSql.Fecha("@p6", cabecera.FechaCorte),
                RegistroReposicionesTraspasosSql.VarChar("@p7", texto.Length > 300 ? texto.Substring(0, 300) : texto, 300));
        }

        public async Task<IDisposable> Bloquear(string empresa, string origen, DateTime dia)
        {
            DbConnection conexion = db.Database.Connection;
            bool laAbrimosAqui = conexion.State != ConnectionState.Open;
            if (laAbrimosAqui)
            {
                await conexion.OpenAsync().ConfigureAwait(false);
            }
            string recurso = Recurso(empresa, origen, dia);
            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_BLOQUEAR, recurso, ESPERA_BLOQUEO_MS).ConfigureAwait(false);
            }
            catch
            {
                if (laAbrimosAqui)
                {
                    conexion.Close();
                }
                throw;
            }
            return new Bloqueo(db, conexion, recurso, laAbrimosAqui);
        }

        private sealed class Bloqueo : IDisposable
        {
            private readonly NVEntities db;
            private readonly DbConnection conexion;
            private readonly string recurso;
            private readonly bool cerrar;

            public Bloqueo(NVEntities db, DbConnection conexion, string recurso, bool cerrar)
            {
                this.db = db;
                this.conexion = conexion;
                this.recurso = recurso;
                this.cerrar = cerrar;
            }

            public void Dispose()
            {
                try
                {
                    _ = db.Database.ExecuteSqlCommand(SQL_SOLTAR, recurso);
                }
                catch
                {
                    // Si no se puede soltar, se suelta solo al cerrar la conexión (ámbito Session)
                }
                finally
                {
                    if (cerrar)
                    {
                        conexion.Close();
                    }
                }
            }
        }
    }

    /// <summary>
    /// NestoAPI#577 (corte 3b): job recurrente <c>reposiciones-automaticas</c> (Startup). Cada 5 minutos en horario laboral
    /// mira qué reposiciones del calendario de hoy han pasado su hora de cierre y las rellena (ver
    /// <see cref="ServicioReposicionAutomatica"/>). Si se pone en el calendario una hora de cierre fuera de
    /// <see cref="CRON"/>, hay que ampliar el cron.
    /// </summary>
    public static class ReposicionesAutomaticasJobsService
    {
        /// <summary>Cada 5 minutos de lunes a viernes de 6:00 a 21:55 (las horas de cierre tienen que caer dentro).</summary>
        public const string CRON = "*/5 6-21 * * 1-5";

        /// <summary>
        /// Sin reintento automático de Hangfire: lo que falla sin apuntarse lo reintenta la siguiente pasada (5 minutos), y
        /// cada ruta lleva su propio applock, así que dos ejecuciones solapadas tampoco duplican.
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        public static async Task RellenarPendientes()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await new ServicioReposicionAutomatica(db).RellenarPendientes().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Reposición automática #577] Error en el job: " + ex.Message, ex), "Sistema (reposición automática)");
                throw;
            }
        }
    }
}
