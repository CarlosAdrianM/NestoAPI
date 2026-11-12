using Hangfire;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857: el progreso de la lectura hacia atrás del buzón de Compras. Se guarda como JSON en la tabla de estado del job
    /// (dbo.ComprasCorreosSeguimientoEstado) en una fila APARTE, de clave «historico|{buzón}»: la del delta no se toca.
    /// </summary>
    public class EstadoHistoricoCorreosCompras
    {
        public const string PROGRAMADO = "Programado";
        public const string EN_MARCHA = "EnMarcha";
        public const string TERMINADO = "Terminado";
        public const string FALLO = "Fallo";

        public string Estado { get; set; }
        public int Meses { get; set; }
        /// <summary>Recibidos desde (incluido)…</summary>
        public DateTime Desde { get; set; }
        /// <summary>…hasta (excluido): el momento en que se lanzó; lo de después ya lo lee el job (y si se solapa, es idempotente).</summary>
        public DateTime Hasta { get; set; }
        /// <summary>Hasta dónde ha llegado: el receivedDateTime del último correo de la última página guardada entera. Se sigue desde aquí.</summary>
        public DateTime? Cursor { get; set; }
        public int Paginas { get; set; }
        public int Leidos { get; set; }
        public int Guardados { get; set; }
        public int YaGuardados { get; set; }
        public int Medidos { get; set; }
        public int FallosSeguidos { get; set; }
        public string UltimoError { get; set; }
        public string Mensaje { get; set; }
        public bool Forzado { get; set; }
        public string LanzadoPor { get; set; }
        public DateTime? Lanzado { get; set; }
        public DateTime? ProgramadoPara { get; set; }
        public DateTime? Empezado { get; set; }
        /// <summary>Latido: se pone en cada página. Un EnMarcha sin latido en <see cref="ServicioHistoricoCorreosCompras.MINUTOS_SIN_LATIDO"/> minutos se da por cortado.</summary>
        public DateTime? Actualizado { get; set; }
        public DateTime? Terminado { get; set; }
        /// <summary>Sin las columnas de Scripts/Issue857b no se apuntan los adjuntos (lo demás, igual).</summary>
        public bool? ConAdjuntos { get; set; }
    }

    public enum CodigoLanzamientoHistorico
    {
        Aceptado,
        /// <summary>409: ya hay una en marcha (o programada).</summary>
        YaEnMarcha,
        /// <summary>503: faltan las claves del buzón.</summary>
        SinConfiguracion,
        /// <summary>503: falta Scripts/Issue857_ComprasCorreosSeguimiento.sql.</summary>
        SinTabla
    }

    public class ResultadoLanzamientoHistorico
    {
        public CodigoLanzamientoHistorico Codigo { get; set; }
        public string Mensaje { get; set; }
        public EstadoHistoricoCorreosCompras Estado { get; set; }
    }

    /// <summary>NestoAPI#857: cómo se pone en cola el trabajo (Hangfire en producción; en los tests, un falso).</summary>
    public interface IProgramadorHistoricoCorreos
    {
        void Encolar(bool forzado);
        /// <summary><paramref name="cuandoMadrid"/> en hora de Madrid.</summary>
        void Programar(DateTime cuandoMadrid, bool forzado);
    }

    /// <summary>NestoAPI#857: nada de trabajo pesado en horario (regla de la casa). Pura.</summary>
    public static class HorarioHistoricoCorreos
    {
        public const int HORA_INICIO = 8;
        public const int HORA_FIN = 20;
        public const int HORA_PROGRAMADA = 21;

        /// <summary>De lunes a viernes, de 8:00 a 20:00 (hora de Madrid). Los festivos entre semana cuentan como laborables (se espera igual).</summary>
        public static bool EnHorario(DateTime madrid) =>
            madrid.DayOfWeek != DayOfWeek.Saturday && madrid.DayOfWeek != DayOfWeek.Sunday
            && madrid.Hour >= HORA_INICIO && madrid.Hour < HORA_FIN;

        /// <summary>Las 21:00 de ese día.</summary>
        public static DateTime CuandoEmpezar(DateTime madrid) => madrid.Date.AddHours(HORA_PROGRAMADA);
    }

    /// <summary>
    /// NestoAPI#857 (en sombra): lectura HACIA ATRÁS de la bandeja de entrada del buzón de Compras (SOLO lectura, nunca marca ni mueve),
    /// lanzada a mano. Página a página por fechas (sin el delta del job, que no se toca), con EXACTAMENTE el mismo análisis, asignación,
    /// adjuntos y guardado que el job (<see cref="ServicioCorreosSeguimientoCompras.GuardarCorreos"/>), salvo que cada correo se asigna
    /// contra los pedidos que estaban en el aire el día que llegó. Al final, la medición (PedidoReal) de lo guardado, por lotes y por Id.
    /// Reanudable (cursor por fecha), respeta el Retry-After y se para con Fallo tras <see cref="MAXIMO_FALLOS_SEGUIDOS"/> fallos seguidos.
    /// </summary>
    public class ServicioHistoricoCorreosCompras
    {
        public const string PREFIJO_CLAVE = "historico|";
        public const int MESES_POR_DEFECTO = 12;
        public const int MESES_MAXIMOS = 24;
        public const int MAXIMO_FALLOS_SEGUIDOS = 5;
        public const int MINUTOS_SIN_LATIDO = 30;
        /// <summary>Un programado que no ha arrancado en estas horas desde su hora se da por perdido (se puede volver a lanzar).</summary>
        public const int HORAS_PROGRAMADO_PERDIDO = 2;
        public const int LOTE_MEDICION = 200;
        public const int MAXIMO_PAGINAS = 5000;
        public static readonly TimeSpan ESPERA_POR_DEFECTO = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ESPERA_MAXIMA = TimeSpan.FromMinutes(5);

        private readonly ILectorBuzonCompras lector;
        private readonly IRepositorioCorreosSeguimiento repositorio;
        private readonly IProgramadorHistoricoCorreos programador;
        private readonly Func<DateTime> ahora;
        private readonly Func<DateTime> ahoraMadrid;
        private readonly Func<TimeSpan, Task> esperar;
        private readonly Action<Exception> registrar;
        private readonly string empresa;
        private readonly IReadOnlyList<IAgenciaEntrada> agencias;

        /// <param name="lector">Null = sin claves (no se hace nada).</param>
        /// <param name="ahora">Hora del servidor (la de Recibido); por defecto DateTime.Now.</param>
        /// <param name="ahoraMadrid">Hora de Madrid (para el horario); por defecto, la de la zona peninsular.</param>
        public ServicioHistoricoCorreosCompras(ILectorBuzonCompras lector, IRepositorioCorreosSeguimiento repositorio,
            IProgramadorHistoricoCorreos programador, Func<DateTime> ahora = null, Func<DateTime> ahoraMadrid = null,
            Func<TimeSpan, Task> esperar = null, Action<Exception> registrar = null, string empresa = null,
            IReadOnlyList<IAgenciaEntrada> agencias = null)
        {
            this.lector = lector;
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.programador = programador ?? throw new ArgumentNullException(nameof(programador));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.ahoraMadrid = ahoraMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()));
            this.esperar = esperar ?? (espera => Task.Delay(espera));
            this.registrar = registrar ?? (_ => { });
            this.empresa = empresa;
            this.agencias = agencias;
        }

        public bool Configurado => lector != null;

        internal static string Clave(string buzon)
        {
            string clave = PREFIJO_CLAVE + (buzon ?? string.Empty).Trim();
            return clave.Length <= 100 ? clave : clave.Substring(0, 100);
        }

        public async Task<EstadoHistoricoCorreosCompras> LeerEstado()
        {
            if (lector == null || !repositorio.TablasExisten())
            {
                return null;
            }
            return Deserializar(await repositorio.LeerEstado(Clave(lector.Buzon)).ConfigureAwait(false));
        }

        internal static EstadoHistoricoCorreosCompras Deserializar(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<EstadoHistoricoCorreosCompras>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private Task Guardar(EstadoHistoricoCorreosCompras estado)
        {
            estado.Actualizado = ahora();
            return repositorio.GuardarEstado(Clave(lector.Buzon), JsonConvert.SerializeObject(estado), estado.UltimoError);
        }

        /// <summary>¿Hay una lectura viva? (en marcha con latido reciente, o programada que aún no ha tocado).</summary>
        internal static bool EstaViva(EstadoHistoricoCorreosCompras estado, DateTime ahora)
        {
            if (estado == null)
            {
                return false;
            }
            if (estado.Estado == EstadoHistoricoCorreosCompras.EN_MARCHA)
            {
                return (estado.Actualizado ?? DateTime.MinValue) > ahora.AddMinutes(-MINUTOS_SIN_LATIDO);
            }
            if (estado.Estado == EstadoHistoricoCorreosCompras.PROGRAMADO)
            {
                return (estado.ProgramadoPara ?? estado.Lanzado ?? DateTime.MinValue) > ahora.AddHours(-HORAS_PROGRAMADO_PERDIDO);
            }
            return false;
        }

        /// <summary>
        /// POST: valida, deja el estado en Programado y lo encola (o, en horario y sin <paramref name="forzar"/>, lo programa para las
        /// 21:00). Si la anterior quedó a medias con los mismos meses, sigue desde su cursor; si no, empieza de cero (es idempotente).
        /// </summary>
        public async Task<ResultadoLanzamientoHistorico> Lanzar(int meses, bool forzar, string usuario)
        {
            if (lector == null)
            {
                return new ResultadoLanzamientoHistorico
                {
                    Codigo = CodigoLanzamientoHistorico.SinConfiguracion,
                    Mensaje = "Faltan las claves del buzón de Compras en secretos.config (ComprasBuzonTenant, ComprasBuzonClientId, ComprasBuzonSecreto y ComprasBuzon)."
                };
            }
            if (!repositorio.TablasExisten())
            {
                return new ResultadoLanzamientoHistorico
                {
                    Codigo = CodigoLanzamientoHistorico.SinTabla,
                    Mensaje = "Todavía no existen las tablas: hay que lanzar Scripts/Issue857_ComprasCorreosSeguimiento.sql."
                };
            }
            DateTime momento = ahora();
            EstadoHistoricoCorreosCompras anterior = Deserializar(await repositorio.LeerEstado(Clave(lector.Buzon)).ConfigureAwait(false));
            bool programadoSinEmpezar = anterior?.Estado == EstadoHistoricoCorreosCompras.PROGRAMADO;
            if (EstaViva(anterior, momento) && !(forzar && programadoSinEmpezar))
            {
                return new ResultadoLanzamientoHistorico
                {
                    Codigo = CodigoLanzamientoHistorico.YaEnMarcha,
                    Mensaje = anterior.Estado == EstadoHistoricoCorreosCompras.EN_MARCHA
                        ? "Ya hay una lectura del histórico en marcha."
                        : $"Ya hay una lectura del histórico programada para el {anterior.ProgramadoPara:dd/MM/yyyy HH:mm}.",
                    Estado = anterior
                };
            }

            bool reanudar = anterior != null && anterior.Estado != EstadoHistoricoCorreosCompras.TERMINADO && anterior.Meses == meses
                && anterior.Hasta > anterior.Desde;
            EstadoHistoricoCorreosCompras estado = reanudar ? anterior : new EstadoHistoricoCorreosCompras
            {
                Meses = meses,
                Desde = momento.AddMonths(-meses),
                Hasta = momento
            };
            estado.Estado = EstadoHistoricoCorreosCompras.PROGRAMADO;
            estado.FallosSeguidos = 0;
            estado.UltimoError = null;
            estado.Terminado = null;
            estado.Forzado = forzar;
            estado.LanzadoPor = usuario;
            estado.Lanzado = momento;
            estado.ConAdjuntos = repositorio.AdjuntosDisponibles();
            string sigue = reanudar && estado.Cursor.HasValue ? $" Sigue desde el {estado.Cursor:dd/MM/yyyy HH:mm} (ya leídos {estado.Leidos})." : string.Empty;
            string adjuntos = estado.ConAdjuntos == true ? string.Empty : " Sin el script Scripts/Issue857b_ComprasCorreosHistoricoYAdjuntos.sql no se apuntan los adjuntos.";

            DateTime madrid = ahoraMadrid();
            if (!forzar && HorarioHistoricoCorreos.EnHorario(madrid))
            {
                DateTime cuando = HorarioHistoricoCorreos.CuandoEmpezar(madrid);
                estado.ProgramadoPara = momento + (cuando - madrid);
                estado.Mensaje = $"Es horario de trabajo: empezará a las {cuando:HH:mm} (hora de Madrid).{sigue}{adjuntos}";
                await Guardar(estado).ConfigureAwait(false);
                programador.Programar(cuando, false);
            }
            else
            {
                estado.ProgramadoPara = momento;
                estado.Mensaje = $"Encolada: empieza ya.{sigue}{adjuntos}";
                await Guardar(estado).ConfigureAwait(false);
                programador.Encolar(forzar);
            }
            return new ResultadoLanzamientoHistorico { Codigo = CodigoLanzamientoHistorico.Aceptado, Mensaje = estado.Mensaje, Estado = estado };
        }

        /// <summary>El trabajo (Hangfire). Nunca lanza: los fallos quedan en el estado (y en ELMAH una vez, al pararse).</summary>
        public async Task Ejecutar(bool forzado)
        {
            if (lector == null || !repositorio.TablasExisten())
            {
                return;
            }
            EstadoHistoricoCorreosCompras estado = null;
            try
            {
                estado = Deserializar(await repositorio.LeerEstado(Clave(lector.Buzon)).ConfigureAwait(false));
                if (estado == null || estado.Estado == EstadoHistoricoCorreosCompras.TERMINADO || estado.Estado == EstadoHistoricoCorreosCompras.FALLO)
                {
                    return;
                }
                if (estado.Estado == EstadoHistoricoCorreosCompras.EN_MARCHA && EstaViva(estado, ahora()))
                {
                    return; // otro trabajo la está haciendo
                }
                if (await ReprogramarSiEsHorario(estado, forzado).ConfigureAwait(false))
                {
                    return;
                }
                estado.Estado = EstadoHistoricoCorreosCompras.EN_MARCHA;
                estado.Empezado = estado.Empezado ?? ahora();
                estado.Mensaje = "Leyendo el buzón.";
                await Guardar(estado).ConfigureAwait(false);

                if (!await Leer(estado, forzado).ConfigureAwait(false))
                {
                    return;
                }
                await MedirHistorico(estado).ConfigureAwait(false);

                estado.Estado = EstadoHistoricoCorreosCompras.TERMINADO;
                estado.Terminado = ahora();
                estado.Mensaje = $"Terminado: {estado.Leidos} leídos, {estado.Guardados} guardados ({estado.YaGuardados} ya estaban), {estado.Medidos} medidos.";
                await Guardar(estado).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Parar(estado, "Error inesperado: " + ex.Message, ex).ConfigureAwait(false);
            }
        }

        private async Task<bool> ReprogramarSiEsHorario(EstadoHistoricoCorreosCompras estado, bool forzado)
        {
            DateTime madrid = ahoraMadrid();
            if (forzado || !HorarioHistoricoCorreos.EnHorario(madrid))
            {
                return false;
            }
            DateTime cuando = HorarioHistoricoCorreos.CuandoEmpezar(madrid);
            estado.Estado = EstadoHistoricoCorreosCompras.PROGRAMADO;
            estado.ProgramadoPara = ahora() + (cuando - madrid);
            estado.Mensaje = $"Es horario de trabajo: se sigue a las {cuando:HH:mm} (hora de Madrid).";
            await Guardar(estado).ConfigureAwait(false);
            programador.Programar(cuando, false);
            return true;
        }

        /// <summary>La lectura página a página. False si se ha parado (Fallo o reprogramada).</summary>
        private async Task<bool> Leer(EstadoHistoricoCorreosCompras estado, bool forzado)
        {
            string errorNucleo = null;
            var nucleo = new ServicioCorreosSeguimientoCompras(lector, repositorio, ex => errorNucleo = ex.Message, agencias, empresa);
            List<PedidoCompraEnAire> pedidos = null;
            string enlace = null;
            for (int pagina = 0; pagina < MAXIMO_PAGINAS; pagina++)
            {
                if (await ReprogramarSiEsHorario(estado, forzado).ConfigureAwait(false))
                {
                    return false;
                }
                LecturaBuzonCompras lectura = await lector.LeerPagina(enlace, estado.Cursor ?? estado.Desde, estado.Hasta).ConfigureAwait(false)
                    ?? new LecturaBuzonCompras { Error = "El lector no ha devuelto nada." };
                string fallo = lectura.Error;
                var resultado = new ResultadoPasadaCorreosSeguimiento();
                if (fallo == null && lectura.Correos.Any())
                {
                    if (pedidos == null)
                    {
                        pedidos = await LeerPedidos(estado).ConfigureAwait(false);
                    }
                    errorNucleo = null;
                    if (!await nucleo.GuardarCorreos(lectura.Correos, resultado,
                        c => Task.FromResult(AsignadorPedidosCompra.EnAireEl(pedidos, c.Recibido))).ConfigureAwait(false))
                    {
                        fallo = "No se han podido guardar todos los correos de la página: " + (errorNucleo ?? "sin detalle");
                    }
                }
                if (fallo != null)
                {
                    estado.FallosSeguidos++;
                    estado.UltimoError = TextoCorreo.Recortar(fallo, 500);
                    if (estado.FallosSeguidos >= MAXIMO_FALLOS_SEGUIDOS)
                    {
                        await Parar(estado, fallo, null).ConfigureAwait(false);
                        return false;
                    }
                    estado.Mensaje = $"Reintentando tras un fallo ({estado.FallosSeguidos} de {MAXIMO_FALLOS_SEGUIDOS}).";
                    await Guardar(estado).ConfigureAwait(false);
                    TimeSpan espera = lectura.EsperarAntesDeReintentar ?? TimeSpan.FromTicks(ESPERA_POR_DEFECTO.Ticks * estado.FallosSeguidos);
                    await esperar(espera > ESPERA_MAXIMA ? ESPERA_MAXIMA : espera < TimeSpan.Zero ? TimeSpan.Zero : espera).ConfigureAwait(false);
                    pagina--; // la misma página otra vez (no cuenta para el tope)
                    continue;
                }

                // Página guardada entera: avanza el cursor
                estado.FallosSeguidos = 0;
                estado.UltimoError = null;
                estado.Paginas++;
                estado.Leidos += lectura.Correos.Count;
                estado.Guardados += resultado.Guardados;
                estado.YaGuardados += resultado.YaGuardados;
                if (lectura.Correos.Any())
                {
                    DateTime ultimo = lectura.Correos.Max(c => c.Recibido);
                    if (!estado.Cursor.HasValue || ultimo > estado.Cursor.Value)
                    {
                        estado.Cursor = ultimo;
                    }
                }
                estado.Mensaje = $"Leyendo el buzón: {estado.Leidos} leídos, {estado.Guardados} guardados.";
                await Guardar(estado).ConfigureAwait(false);
                enlace = lectura.SiguienteEnlace;
                if (string.IsNullOrEmpty(enlace))
                {
                    return true;
                }
            }
            return true;
        }

        private async Task<List<PedidoCompraEnAire>> LeerPedidos(EstadoHistoricoCorreosCompras estado)
        {
            try
            {
                return await repositorio.LeerPedidosDesde(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa,
                    estado.Desde.Date).ConfigureAwait(false) ?? new List<PedidoCompraEnAire>();
            }
            catch (Exception ex)
            {
                // Como en el job: sin pedidos se guarda igual, sin candidatos
                registrar(new Exception("[Correos de seguimiento de compras #857, histórico] No se han podido leer los pedidos de compra: " + ex.Message, ex));
                return new List<PedidoCompraEnAire>();
            }
        }

        /// <summary>La medición de lo guardado desde <c>Desde</c>, por lotes de <see cref="LOTE_MEDICION"/> y por Id (el mismo núcleo que el job).</summary>
        private async Task MedirHistorico(EstadoHistoricoCorreosCompras estado)
        {
            var nucleo = new ServicioCorreosSeguimientoCompras(lector, repositorio, registrar, agencias, empresa);
            estado.Mensaje = "Midiendo (qué pedido se recibió de verdad).";
            await Guardar(estado).ConfigureAwait(false);
            int ultimoId = 0;
            try
            {
                while (true)
                {
                    List<CorreoSeguimientoFila> lote = await repositorio.LeerPendientesDeMedirPorId(estado.Desde, ultimoId, LOTE_MEDICION).ConfigureAwait(false);
                    if (lote == null || !lote.Any())
                    {
                        return;
                    }
                    estado.Medidos += await nucleo.MedirLote(lote).ConfigureAwait(false);
                    int maximo = lote.Max(f => f.Id);
                    if (maximo <= ultimoId)
                    {
                        return;
                    }
                    ultimoId = maximo;
                }
            }
            catch (Exception ex)
            {
                // Lo leído vale; la medición la seguirá el job (lo de los últimos 60 días)
                estado.UltimoError = TextoCorreo.Recortar("No se ha podido medir: " + ex.Message, 500);
                registrar(new Exception("[Correos de seguimiento de compras #857, histórico] No se ha podido medir: " + ex.Message, ex));
            }
        }

        /// <summary>Fallo: estado Fallo y UNA línea en ELMAH. No se reintenta sola (se vuelve a lanzar a mano y sigue desde el cursor).</summary>
        private async Task Parar(EstadoHistoricoCorreosCompras estado, string motivo, Exception ex)
        {
            registrar(new Exception($"[Correos de seguimiento de compras #857, histórico] Parado: {motivo} " +
                $"(leídos {estado?.Leidos ?? 0}, guardados {estado?.Guardados ?? 0}; al volver a lanzarlo sigue desde {estado?.Cursor:dd/MM/yyyy HH:mm})", ex));
            if (estado == null)
            {
                return;
            }
            estado.Estado = EstadoHistoricoCorreosCompras.FALLO;
            estado.UltimoError = TextoCorreo.Recortar(motivo, 500);
            estado.Mensaje = "Parado tras fallar: vuelva a lanzarlo y seguirá desde donde iba.";
            try
            {
                await Guardar(estado).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Sin poder guardar el estado, el latido caduca y se puede volver a lanzar
            }
        }
    }

    /// <summary>NestoAPI#857: el trabajo de Hangfire del histórico. Sin reintentos automáticos (el estado manda; se relanza a mano).</summary>
    public class ProgramadorHistoricoCorreosHangfire : IProgramadorHistoricoCorreos
    {
        public void Encolar(bool forzado) => _ = BackgroundJob.Enqueue(() => HistoricoCorreosComprasJobs.Ejecutar(forzado));

        public void Programar(DateTime cuandoMadrid, bool forzado)
        {
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cuandoMadrid, DateTimeKind.Unspecified), Startup.ZonaHorariaPeninsular());
            _ = BackgroundJob.Schedule(() => HistoricoCorreosComprasJobs.Ejecutar(forzado), new DateTimeOffset(utc, TimeSpan.Zero));
        }
    }

    public static class HistoricoCorreosComprasJobs
    {
        private const string USUARIO_ELMAH = "Sistema (histórico de correos de compras)";

        public static ServicioHistoricoCorreosCompras CrearServicio()
        {
            ConfiguracionBuzonCompras configuracion = ConfiguracionBuzonCompras.Leer(clave => ConfigurationManager.AppSettings[clave]);
            ILectorBuzonCompras lector = configuracion == null ? null : new LectorBuzonComprasGraph(configuracion);
            return new ServicioHistoricoCorreosCompras(lector, new RepositorioCorreosSeguimiento(), new ProgramadorHistoricoCorreosHangfire(),
                registrar: ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
        }

        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static async Task Ejecutar(bool forzado)
        {
            try
            {
                await CrearServicio().Ejecutar(forzado).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Correos de seguimiento de compras #857, histórico] Error: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }
    }
}
