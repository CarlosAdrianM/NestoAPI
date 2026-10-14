using Hangfire;
using System;
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
    /// </summary>
    public class PropuestasStockJobsService
    {
        public const string ID_JOB = "propuestas-stock-mensual";
        /// <summary>Día 1 de cada mes a las 04:30.</summary>
        public const string CRON = "30 4 1 * *";
        public const string COLA = Rapports.ReentrenamientoModeloLlamadasJobsService.COLA;
        internal const string ORIGEN_PROGRAMADO = "job mensual";
        internal const string REMITENTE = "nesto@nuevavision.es";

        private readonly Func<IRepositorioTendencias> crearRepositorio;
        private readonly IServicioCorreoElectronico correo;
        private readonly Func<string, string> leerParametro;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrarError;

        public PropuestasStockJobsService() : this(null)
        {
        }

        internal PropuestasStockJobsService(Func<IRepositorioTendencias> crearRepositorio, IServicioCorreoElectronico correo = null,
            Func<string, string> leerParametro = null, Func<DateTime> ahora = null, Action<Exception> registrarError = null)
        {
            this.crearRepositorio = crearRepositorio ?? (() => new RepositorioTendenciasSql());
            this.correo = correo ?? new ServicioCorreoElectronico();
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrarError = registrarError ?? ElmahHelper.Log;
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

        /// <summary>El último mes cerrado: el anterior al de <paramref name="hoy"/>.</summary>
        public static DateTime MesCerrado(DateTime hoy) => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);

        /// <summary>«2026-10» → 01/10/2026; null si no es un mes válido.</summary>
        public static DateTime? LeerMes(string mes)
            => !string.IsNullOrWhiteSpace(mes) && DateTime.TryParseExact(mes.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
                ? fecha
                : (DateTime?)null;

        /// <summary>Calcula, guarda, genera el Excel y lo manda. Nunca lanza: los errores van a ELMAH. Devuelve el resultado (o null si falló todo).</summary>
        public ResultadoPropuestasStock Ejecutar(DateTime mes, string origen)
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
                    guardadas = repositorio.GuardarPropuestas(resultado.Mes, resultado.Productos, inicio);
                }
                else
                {
                    avisoGuardado = "La tabla PropuestasStock no existe (falta lanzar Scripts/Issue630_PropuestasStock.sql): no se ha guardado nada.";
                }
            }
            catch (Exception ex)
            {
                avisoGuardado = "No se han podido guardar las propuestas (detalle en ELMAH).";
                registrarError(new Exception($"[Propuestas de stock #630] Error al guardar {resultado.Mes:yyyy-MM} ({origen}); el Excel sale igual: {ex.Message}", ex));
            }

            try
            {
                byte[] excel = new GeneradorExcelPropuestasStock().Generar(resultado);
                using (MailMessage mail = ConstruirCorreo(resultado, excel, guardadas, avisoGuardado, origen, ahora() - inicio,
                    ParametrosTendencias.Destinatarios(leerParametro).ToArray()))
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
            return resultado;
        }

        internal static string NombreFichero(DateTime mes) => $"PropuestasStock_{mes:yyyy-MM}.xlsx";

        internal static MailMessage ConstruirCorreo(ResultadoPropuestasStock r, byte[] excel, int guardadas, string avisoGuardado,
            string origen, TimeSpan duracion, string[] destinatarios)
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
                Subject = $"Propuestas de stock de {mes}",
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
            cuerpo.Append($"<p>Hojas: «{GeneradorExcelPropuestasStock.NombreHojaTop(top)}», «{GeneradorExcelPropuestasStock.HOJA_TODOS}», " +
                $"«{GeneradorExcelPropuestasStock.HOJA_NUEVOS}» y «{GeneradorExcelPropuestasStock.HOJA_CALIBRACION}». " +
                "Los umbrales son PROVISIONALES hasta revisar la calibración.</p>");
            cuerpo.Append($"<p style=\"color:#666\">{r.ProductosLeidos} productos leídos, {r.FamiliasConEstacionalidad} familias con estacionalidad, " +
                $"{guardadas} filas nuevas en PropuestasStock");
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
