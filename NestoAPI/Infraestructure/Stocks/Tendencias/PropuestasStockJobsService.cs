using Hangfire;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#630 (corte 2 de la épica #628): job mensual «propuestas-stock-mensual». El día 1 a las 04:30 (el
    /// «indicadores-stock-mensual» del corte 1 va a las 03:30) calcula las propuestas del mes que acaba de cerrar, las guarda en
    /// PropuestasStock (una fila por producto y mes; nunca se pisa) y manda el Excel por correo a
    /// <see cref="ParametrosTendencias.CLAVE_DESTINATARIOS"/> (por defecto Carlos).
    /// <para>Va en la cola «entrenamiento» (servidor de un hilo propio, Startup): recorre la venta y el extracto de 5 años de todos
    /// los productos y en la cola normal dejaría parados los jobs de cada 5 minutos. Sin reintentos automáticos: un reintento
    /// caería en horario.</para>
    /// <para>A mano: <c>POST api/PropuestasStock/Generar?mes=AAAA-MM</c> (Dirección), que lo encola (o lo programa para la
    /// madrugada si es horario de trabajo).</para>
    /// <para>NestoAPI#631: si ha guardado filas nuevas y hay propuestas pendientes de decidir, avisa a Compras por la campana
    /// de Nesto (<see cref="TIPO_NOTIFICACION"/>).</para>
    /// </summary>
    public class PropuestasStockJobsService
    {
        public const string ID_JOB = "propuestas-stock-mensual";
        /// <summary>Día 1 de cada mes a las 04:30.</summary>
        public const string CRON = "30 4 1 * *";
        public const string COLA = Rapports.ReentrenamientoModeloLlamadasJobsService.COLA;
        internal const string ORIGEN_PROGRAMADO = "job mensual";
        internal const string REMITENTE = "nesto@nuevavision.es";
        /// <summary>NestoAPI#631: tipo del aviso en la campana de Nesto (al pulsarlo, la pantalla de propuestas de stock).</summary>
        public const string TIPO_NOTIFICACION = "PropuestasStock";

        private readonly Func<IRepositorioTendencias> crearRepositorio;
        private readonly IServicioCorreoElectronico correo;
        private readonly Func<string, string> leerParametro;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrarError;
        private readonly Func<IAvisadorCompras> crearAvisador;
        private readonly Func<IRepositorioDecisionesPropuestasStock> crearRepositorioDecisiones;
        private readonly Func<DateTime, IndicadoresDelMesPropuestas> leerIndicadores;

        public PropuestasStockJobsService() : this(null, leerIndicadores: LeerIndicadoresDeLaBd)
        {
        }

        internal PropuestasStockJobsService(Func<IRepositorioTendencias> crearRepositorio, IServicioCorreoElectronico correo = null,
            Func<string, string> leerParametro = null, Func<DateTime> ahora = null, Action<Exception> registrarError = null,
            Func<IAvisadorCompras> crearAvisador = null, Func<IRepositorioDecisionesPropuestasStock> crearRepositorioDecisiones = null,
            Func<DateTime, IndicadoresDelMesPropuestas> leerIndicadores = null)
        {
            this.crearRepositorio = crearRepositorio ?? (() => new RepositorioTendenciasSql());
            this.correo = correo ?? new ServicioCorreoElectronico();
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.crearAvisador = crearAvisador ?? (() => new AvisadorCompras(new ServicioNotificacionesPush()));
            this.crearRepositorioDecisiones = crearRepositorioDecisiones ?? (() => new RepositorioDecisionesPropuestasStockSql());
            // NestoAPI#628: sin lector (tests), el Excel sale sin el cuadre con los indicadores ni la rotación por exclusiva.
            this.leerIndicadores = leerIndicadores;
        }

        /// <summary>NestoAPI#628: lo de IndicadoresStock del mes para la hoja «Resumen», de la BD (con el tope de tiempo del job).</summary>
        internal static IndicadoresDelMesPropuestas LeerIndicadoresDeLaBd(DateTime mes)
        {
            using (NVEntities db = new NVEntities())
            {
                return LeerIndicadores(new RepositorioIndicadoresStockSql(db, RepositorioIndicadoresStockSql.SEGUNDOS_CONSULTA_JOB), mes, ElmahHelper.Log);
            }
        }

        /// <summary>
        /// NestoAPI#628: las filas de IndicadoresStock del mes y el stock de MTP y MMP por familia. Nunca lanza: lo que falla va a
        /// ELMAH y queda en <see cref="IndicadoresDelMesPropuestas.Aviso"/>, que sale en la hoja.
        /// </summary>
        internal static IndicadoresDelMesPropuestas LeerIndicadores(IRepositorioIndicadoresStock repositorio, DateTime mes, Action<Exception> registrarError)
        {
            DateTime primero = new DateTime(mes.Year, mes.Month, 1);
            IndicadoresDelMesPropuestas resultado = new IndicadoresDelMesPropuestas();
            List<string> avisos = new List<string>();
            try
            {
                if (repositorio.ExisteTabla().GetAwaiter().GetResult())
                {
                    resultado.Filas = repositorio.Leer(primero, primero, null).GetAwaiter().GetResult() ?? new List<FilaIndicadoresStock>();
                    if (resultado.Total == null)
                    {
                        avisos.Add($"IndicadoresStock no tiene todavía el mes {primero:yyyy-MM} (se calcula el día 1 a las 03:30).");
                    }
                }
                else
                {
                    avisos.Add("No existe la tabla IndicadoresStock (Scripts/Issue629_IndicadoresStock.sql).");
                }
            }
            catch (Exception ex)
            {
                avisos.Add("No se han podido leer los indicadores de stock del mes (detalle en ELMAH).");
                registrarError(new Exception($"[Propuestas de stock #628] No se han podido leer los indicadores de {primero:yyyy-MM} para el Excel: {ex.Message}", ex));
            }
            try
            {
                resultado.FueraDeLaDemanda = repositorio.LeerStockFueraDeLaDemanda(primero).GetAwaiter().GetResult() ?? new List<StockFueraDeLaDemanda>();
            }
            catch (Exception ex)
            {
                resultado.FueraDeLaDemanda = null;
                avisos.Add("No se ha podido leer el stock de materias primas y muestras (detalle en ELMAH).");
                registrarError(new Exception($"[Propuestas de stock #628] No se ha podido leer el stock de MTP y MMP de {primero:yyyy-MM} para el Excel: {ex.Message}", ex));
            }
            resultado.Aviso = avisos.Count == 0 ? null : string.Join(" ", avisos);
            return resultado;
        }

        /// <summary>Punto de entrada del job recurrente: el mes que acaba de cerrar.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void GenerarProgramado()
        {
            PropuestasStockJobsService servicio = new PropuestasStockJobsService();
            _ = servicio.Ejecutar(MesCerrado(servicio.ahora()), ORIGEN_PROGRAMADO);
        }

        /// <summary>Lo que encola POST api/PropuestasStock/Generar. <paramref name="mes"/> en formato AAAA-MM.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void GenerarManual(string mes, string usuario)
        {
            PropuestasStockJobsService servicio = new PropuestasStockJobsService();
            DateTime referencia = LeerMes(mes) ?? MesCerrado(servicio.ahora());
            _ = servicio.Ejecutar(referencia, string.IsNullOrWhiteSpace(usuario) ? "a mano" : $"a mano ({usuario})");
        }

        /// <summary>
        /// NestoAPI#630 (regenerar un mes): lo que encola POST api/PropuestasStock/Generar?regenerar=true. Borra las filas del mes
        /// (si ninguna tiene decisión), las vuelve a calcular y guardar, y manda otra vez el Excel. Método aparte (y no un parámetro
        /// más de <see cref="GenerarManual"/>) para no romper los jobs de Hangfire ya encolados con la firma de siempre.
        /// </summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void RegenerarManual(string mes, string usuario)
        {
            PropuestasStockJobsService servicio = new PropuestasStockJobsService();
            DateTime referencia = LeerMes(mes) ?? MesCerrado(servicio.ahora());
            _ = servicio.Ejecutar(referencia, string.IsNullOrWhiteSpace(usuario) ? "regenerado a mano" : $"regenerado a mano ({usuario})", regenerar: true);
        }

        /// <summary>El último mes cerrado: el anterior al de <paramref name="hoy"/>.</summary>
        public static DateTime MesCerrado(DateTime hoy) => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);

        /// <summary>«2026-10» → 01/10/2026; null si no es un mes válido.</summary>
        public static DateTime? LeerMes(string mes)
            => !string.IsNullOrWhiteSpace(mes) && DateTime.TryParseExact(mes.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
                ? fecha
                : (DateTime?)null;

        /// <summary>Calcula, guarda, genera el Excel y lo manda. Nunca lanza: los errores van a ELMAH. Devuelve el resultado (o null si falló todo).</summary>
        /// <param name="regenerar">
        /// NestoAPI#630: borra antes las filas del mes (en la misma transacción que la inserción) en vez de saltarse las que ya
        /// existen. Si alguna tiene decisión no se borra nada y el correo lo dice. No vuelve a avisar a Compras por la campana.
        /// </param>
        public ResultadoPropuestasStock Ejecutar(DateTime mes, string origen, bool regenerar = false)
        {
            DateTime inicio = ahora();
            ResultadoPropuestasStock resultado;
            IRepositorioTendencias repositorio;
            try
            {
                repositorio = crearRepositorio();
                ParametrosTendencias parametros = ParametrosTendencias.Desde(leerParametro);
                resultado = new ServicioPropuestasStock(repositorio, registrarError).Calcular(mes, parametros);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Propuestas de stock #630] Error al calcular {mes:yyyy-MM} ({origen}): {ex.Message}", ex));
                return null;
            }

            int guardadas = 0;
            string avisoGuardado = null;
            try
            {
                if (repositorio.ExisteTabla())
                {
                    guardadas = regenerar
                        ? repositorio.RegenerarPropuestas(resultado.Mes, resultado.Productos, inicio)
                        : repositorio.GuardarPropuestas(resultado.Mes, resultado.Productos, inicio);
                }
                else
                {
                    avisoGuardado = "La tabla PropuestasStock no existe (falta lanzar Scripts/Issue630_PropuestasStock.sql): no se ha guardado nada.";
                }
            }
            catch (PropuestasStockConDecisionesException ex)
            {
                avisoGuardado = ex.Message;
                registrarError(new Exception($"[Propuestas de stock #630] {ex.Message} ({origen})", ex));
            }
            catch (Exception ex)
            {
                avisoGuardado = "No se han podido guardar las propuestas (detalle en ELMAH).";
                registrarError(new Exception($"[Propuestas de stock #630] Error al guardar {resultado.Mes:yyyy-MM} ({origen}); el Excel sale igual: {ex.Message}", ex));
            }

            if (leerIndicadores != null)
            {
                try
                {
                    resultado.Indicadores = leerIndicadores(resultado.Mes);
                }
                catch (Exception ex)
                {
                    registrarError(new Exception($"[Propuestas de stock #628] No se han podido leer los indicadores de {resultado.Mes:yyyy-MM}; el Excel sale sin el cuadre: {ex.Message}", ex));
                }
            }

            try
            {
                byte[] excel = new GeneradorExcelPropuestasStock().Generar(resultado);
                using (MailMessage mail = ConstruirCorreo(resultado, excel, guardadas, avisoGuardado, origen, ahora() - inicio,
                    ParametrosTendencias.Destinatarios(leerParametro).ToArray(), regenerar))
                {
                    if (!correo.EnviarCorreoSMTP(mail))
                    {
                        registrarError(new Exception($"[Propuestas de stock #630] No se ha podido mandar el correo de {resultado.Mes:yyyy-MM} ({origen})."));
                    }
                }
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Propuestas de stock #630] Error al generar o mandar el Excel de {resultado.Mes:yyyy-MM} ({origen}): {ex.Message}", ex));
            }

            if (guardadas > 0 && !regenerar)
            {
                AvisarCompras(resultado);
            }
            return resultado;
        }

        /// <summary>
        /// NestoAPI#631: aviso a Compras por la campana con las propuestas pendientes del mes (sin las silenciadas, si ya está el
        /// script del corte 3; si no, todas las que tienen acción). Nunca lanza.
        /// </summary>
        internal void AvisarCompras(ResultadoPropuestasStock resultado)
        {
            try
            {
                int pendientes;
                decimal exceso;
                IRepositorioDecisionesPropuestasStock decisiones = crearRepositorioDecisiones();
                if (decisiones.ExisteEsquema())
                {
                    ResumenEstadoPropuestas fila = decisiones.Resumen(resultado.Mes).FirstOrDefault(r => r.Estado == EstadosPropuesta.PENDIENTE);
                    pendientes = fila?.Propuestas ?? 0;
                    exceso = fila?.ExcesoValorado ?? 0;
                }
                else
                {
                    List<ResultadoTendencia> conAccion = resultado.Productos
                        .Where(p => p.Accion != AccionesPropuesta.MANTENER && p.Accion != AccionesPropuesta.NADA).ToList();
                    pendientes = conAccion.Count;
                    exceso = conAccion.Sum(p => p.ExcesoValorado);
                }
                if (pendientes == 0)
                {
                    return;
                }
                string titulo = TituloAviso(resultado.Mes);
                string texto = TextoAviso(resultado.Mes, pendientes, exceso);
                crearAvisador().Avisar(titulo, new[] { texto }, TIPO_NOTIFICACION).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Propuestas de stock #631] No se ha podido avisar a Compras de las propuestas de {resultado.Mes:yyyy-MM}: {ex.Message}", ex));
            }
        }

        internal static string TituloAviso(DateTime mes) => $"Propuestas de stock de {mes.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-ES"))}";

        internal static string TextoAviso(DateTime mes, int pendientes, decimal exceso)
        {
            string cuantas = pendientes == 1 ? "Hay 1 propuesta" : $"Hay {pendientes} propuestas";
            return $"{cuantas} de máximos y mínimos de {mes.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-ES"))} pendientes de decidir " +
                $"({MotorTendencias.Euros(exceso)} de exceso). Revíselas en Nesto: puede aplicarlas, rechazarlas, posponerlas o silenciarlas.";
        }

        internal static string NombreFichero(DateTime mes) => $"PropuestasStock_{mes:yyyy-MM}.xlsx";

        internal static MailMessage ConstruirCorreo(ResultadoPropuestasStock r, byte[] excel, int guardadas, string avisoGuardado,
            string origen, TimeSpan duracion, string[] destinatarios, bool regenerado = false)
        {
            if (destinatarios == null || destinatarios.Length == 0)
            {
                throw new InvalidOperationException("No hay destinatarios para el correo de propuestas de stock.");
            }
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            string mes = r.Mes.ToString("MMMM yyyy", es);
            MailMessage mail = new MailMessage
            {
                From = new MailAddress(REMITENTE, "Nesto - Nueva Visión"),
                Subject = regenerado ? $"Propuestas de stock de {mes} (regeneradas)" : $"Propuestas de stock de {mes}",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(new MailAddress(destinatario));
            }

            int top = r.Parametros?.FilasTop ?? 50;
            var porAccion = r.Productos.GroupBy(p => p.Accion).OrderByDescending(g => g.Sum(p => p.ExcesoValorado))
                .Select(g => $"<li>{g.Key}: {g.Count()} productos, {MotorTendencias.Euros(g.Sum(p => p.ExcesoValorado))} de exceso</li>");
            StringBuilder cuerpo = new StringBuilder();
            cuerpo.Append($"<p>Propuestas de stock de <b>{mes}</b> ({origen}).</p>");
            cuerpo.Append($"<p>{r.Productos.Count} productos con propuesta; exceso total sobre la cobertura objetivo: <b>{MotorTendencias.Euros(r.Productos.Sum(p => p.ExcesoValorado))}</b>. " +
                $"Los {top} primeros suman {MotorTendencias.Euros(r.Productos.Take(top).Sum(p => p.ExcesoValorado))}.</p>");
            cuerpo.Append("<ul>").Append(string.Join("", porAccion)).Append("</ul>");
            // NestoAPI#655: los propios (fabricación propia) aparte, para que no tapen el exceso de verdad.
            List<ResultadoTendencia> propios = r.Productos.Where(p => p.FabricacionPropia).ToList();
            List<ResultadoTendencia> sinPropios = r.Productos.Where(p => !p.FabricacionPropia).ToList();
            cuerpo.Append($"<p>Sin propios: {sinPropios.Count} productos, {MotorTendencias.Euros(GeneradorExcelPropuestasStock.ValorStock(sinPropios))} de stock y " +
                $"<b>{MotorTendencias.Euros(sinPropios.Sum(p => p.ExcesoValorado))}</b> de exceso. Propios (fabricación propia): {propios.Count} productos, " +
                $"{MotorTendencias.Euros(GeneradorExcelPropuestasStock.ValorStock(propios))} de stock y {MotorTendencias.Euros(propios.Sum(p => p.ExcesoValorado))} de exceso " +
                "(nunca Outlet ni liquidar).</p>");
            cuerpo.Append($"<p>Hojas: «{GeneradorExcelPropuestasStock.HOJA_RESUMEN}», «{GeneradorExcelPropuestasStock.NombreHojaTop(top)}», «{GeneradorExcelPropuestasStock.HOJA_TODOS}», " +
                $"«{GeneradorExcelPropuestasStock.HOJA_NUEVOS}», «{GeneradorExcelPropuestasStock.HOJA_PROPIOS}» y «{GeneradorExcelPropuestasStock.HOJA_CALIBRACION}» " +
                "(las de productos, sin los propios). " +
                "Los umbrales son PROVISIONALES hasta revisar la calibración.</p>");
            cuerpo.Append($"<p style=\"color:#666\">{r.ProductosLeidos} productos leídos, {r.FamiliasConEstacionalidad} familias con estacionalidad, " +
                (regenerado ? $"{guardadas} filas en PropuestasStock (regeneradas: borradas las anteriores del mes)" : $"{guardadas} filas nuevas en PropuestasStock"));
            if (r.LotesConError > 0 || r.ProductosConError > 0)
            {
                cuerpo.Append($"; <b>{r.LotesConError} lotes y {r.ProductosConError} productos con error</b> (detalle en ELMAH)");
            }
            cuerpo.Append($"; {Math.Round(duracion.TotalMinutes, 1).ToString(es)} minutos.</p>");
            if (!string.IsNullOrEmpty(avisoGuardado))
            {
                cuerpo.Append($"<p><b>{avisoGuardado}</b></p>");
            }
            mail.Body = cuerpo.ToString();
            mail.Attachments.Add(new Attachment(new MemoryStream(excel), NombreFichero(r.Mes),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            return mail;
        }
    }
}
