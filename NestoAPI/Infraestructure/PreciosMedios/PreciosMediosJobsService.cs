using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>Interruptor de la sombra semanal de precios medios (Issue #547, corte b).</summary>
    public enum ModoSombraPreciosMedios
    {
        /// <summary>No hace nada. Es el valor por defecto (sin fila, "0" o cualquier cosa no reconocida).</summary>
        Apagado,

        /// <summary>Hace la pasada completa, registra en PreciosMediosSombra, resume en ELMAH y avisa por correo si hay diferencias no esperadas.</summary>
        Activo
    }

    /// <summary>
    /// Tipo propio para el resumen informativo de la sombra en ELMAH (no es un error: se reconoce por la columna Type).
    /// </summary>
    public class SombraPreciosMediosInfo : Exception
    {
        public SombraPreciosMediosInfo(string mensaje) : base(mensaje) { }
    }

    /// <summary>
    /// Issue #547, corte (b): la SOMBRA de los precios medios. Los domingos a las 06:30, después del SP
    /// «Precios Medios» de msdb (00:30), calcula con <see cref="CalculadoraPrecioMedio"/> la media de cada producto
    /// con compras de las empresas 1, 4 y 5 y la compara al diezmilésimo con lo que ha dejado el SP
    /// (Productos.PrecioMedio, LinPedidoCmp.Coste y, por muestreo, LinPedidoVta.Coste). NO ESCRIBE en ninguna de
    /// esas tablas: solo en PreciosMediosSombra (diagnóstico), un resumen en ELMAH y, si hay diferencias no
    /// esperadas, un correo a Carlos.
    ///
    /// Interruptor: parámetro <c>PreciosMediosSombra</c> bajo «(defecto)» en ParámetrosUsuario. Sin fila (así nace)
    /// = APAGADO. Con "1" o "Activo" hace la pasada. Se lee en cada ejecución: encender o apagar no necesita publicar.
    /// Si la tabla aún no existe (Scripts/Issue547_PreciosMediosSombra.sql sin ejecutar) no lee nada: lo deja en ELMAH.
    /// </summary>
    public static class PreciosMediosJobsService
    {
        internal const string VALOR_ACTIVO = "Activo";
        internal const string USUARIO_ELMAH = "Sistema (sombra de precios medios)";

        /// <summary>El job se corta a las 3 horas (el SP de los domingos ya tarda lo suyo: no competir con la mañana).</summary>
        internal static readonly TimeSpan LIMITE_PASADA = TimeSpan.FromHours(3);

        /// <summary>Hora a la que arranca el SP «Precios Medios» de msdb los domingos (paso 0 del plan).</summary>
        internal static readonly TimeSpan HORA_SP_DOMINGO = new TimeSpan(0, 30, 0);

        /// <summary>Punto de entrada del job recurrente (domingo 06:30). No hace nada si el interruptor está apagado.</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 4 * 3600)]
        public static void ProcesarSombraSemanal()
        {
            try
            {
                _ = Procesar(new LectorParametrosUsuario(), CrearServicio, new ServicioCorreoElectronico(), DateTime.Now, forzar: false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Precios medios #547] Error en la sombra semanal: " + ex.Message, ex), USUARIO_ELMAH);
                // Sin relanzar: la sombra no debe dejar el job en Failed ni reintentar (la semana que viene vuelve a pasar).
            }
        }

        /// <summary>
        /// Lanzada a mano (POST api/PreciosMedios/Sombra?completa=true, encolada en Hangfire): hace la pasada aunque
        /// el interruptor esté apagado, porque la pide alguien de Informática o Dirección a propósito.
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 4 * 3600)]
        public static void ProcesarSombraManual()
        {
            try
            {
                _ = Procesar(new LectorParametrosUsuario(), CrearServicio, new ServicioCorreoElectronico(), DateTime.Now, forzar: true);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Precios medios #547] Error en la sombra lanzada a mano: " + ex.Message, ex), USUARIO_ELMAH);
            }
        }

        internal static ServicioSombraPreciosMedios CrearServicio()
        {
            return new ServicioSombraPreciosMedios(RepositorioPreciosMediosSql.DesdeConfiguracion());
        }

        /// <summary>
        /// Núcleo del job con las dependencias inyectadas (tests sin BD ni SMTP). Devuelve null si el interruptor está
        /// apagado (y no se ha forzado): en ese caso no se crea el servicio ni se lee nada.
        /// </summary>
        internal static ResumenPasadaSombraPrecioMedio Procesar(ILectorParametrosUsuario lector, Func<ServicioSombraPreciosMedios> crearServicio,
            IServicioCorreoElectronico correo, DateTime ahora, bool forzar)
        {
            if (!forzar && LeerModo(lector) == ModoSombraPreciosMedios.Apagado)
            {
                return null;
            }

            ServicioSombraPreciosMedios servicio = crearServicio();

            // Si el SP del domingo sigue corriendo, compararíamos con un estado a medias: mejor no hacer nada.
            // El corte es el inicio real de su última pasada (msdb); si no se puede leer, el domingo a las 00:30.
            EjecucionSPPreciosMedios ejecucion = servicio.UltimaEjecucionSP();
            if (ejecucion != null && ejecucion.EnEjecucion)
            {
                ResumenPasadaSombraPrecioMedio aplazada = new ResumenPasadaSombraPrecioMedio
                {
                    FechaPasada = ahora.Date,
                    CorteSP = ejecucion.Inicio,
                    Error = $"el SP «Precios Medios» sigue en ejecución (empezó el {ejecucion.Inicio:dd/MM/yyyy HH:mm}); no se compara con un estado a medias"
                };
                ElmahHelper.Log(new SombraPreciosMediosInfo("[Precios medios #547] " + aplazada), USUARIO_ELMAH);
                return aplazada;
            }
            DateTime corte = ejecucion?.Inicio ?? CorteSP(ahora);

            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(ServicioSombraPreciosMedios.EMPRESAS, ahora.Date,
                corte, registrar: true, limite: LIMITE_PASADA);

            if (resumen.Error != null)
            {
                ElmahHelper.Log(new Exception("[Precios medios #547] " + resumen), USUARIO_ELMAH);
                return resumen;
            }

            ElmahHelper.Log(new SombraPreciosMediosInfo("[Precios medios #547] " + resumen), USUARIO_ELMAH);
            if (resumen.NoEsperados > 0 && correo != null)
            {
                try
                {
                    using (MailMessage mail = ConstruirCorreo(resumen))
                    {
                        if (!correo.EnviarCorreoSMTP(mail))
                        {
                            ElmahHelper.Log(new Exception("[Precios medios #547] No se ha podido mandar el correo de diferencias de la sombra"), USUARIO_ELMAH);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception("[Precios medios #547] Error mandando el correo de la sombra: " + ex.Message, ex), USUARIO_ELMAH);
                }
            }
            return resumen;
        }

        /// <summary>Cualquier fallo al leer el parámetro cuenta como apagado.</summary>
        internal static ModoSombraPreciosMedios LeerModo(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarModo(lector?.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.PRECIOS_MEDIOS_SOMBRA));
            }
            catch
            {
                return ModoSombraPreciosMedios.Apagado;
            }
        }

        internal static ModoSombraPreciosMedios InterpretarModo(string valor)
        {
            string limpio = valor?.Trim();
            return limpio == "1" || string.Equals(limpio, VALOR_ACTIVO, StringComparison.OrdinalIgnoreCase)
                ? ModoSombraPreciosMedios.Activo
                : ModoSombraPreciosMedios.Apagado;
        }

        /// <summary>
        /// Inicio de la última pasada del SP: el domingo más reciente a las 00:30 que no sea posterior a
        /// <paramref name="ahora"/>. Lo facturado desde entonces el SP aún no lo ha visto («pendiente»).
        /// </summary>
        internal static DateTime CorteSP(DateTime ahora)
        {
            int diasDesdeDomingo = (int)ahora.DayOfWeek; // domingo = 0
            DateTime corte = ahora.Date.AddDays(-diasDesdeDomingo).Add(HORA_SP_DOMINGO);
            return corte > ahora ? corte.AddDays(-7) : corte;
        }

        internal static MailMessage ConstruirCorreo(ResumenPasadaSombraPrecioMedio resumen)
        {
            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"[Sombra precios medios] {resumen.NoEsperados} productos con diferencias no esperadas ({resumen.FechaPasada:dd/MM/yyyy})",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.INFORMATICA);
            mail.Body = GenerarHtml(resumen);
            return mail;
        }

        internal static string GenerarHtml(ResumenPasadaSombraPrecioMedio resumen)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<h2>Sombra de precios medios (NestoAPI#547)</h2>");
            sb.AppendLine("<p>La calculadora en C# no coincide al diezmilésimo con lo que ha dejado el SP del domingo en estos productos, " +
                "y la diferencia no se explica por facturas posteriores al SP ni por empates de fecha. <b>No se ha escrito nada</b>: " +
                "es solo diagnóstico. Para cada uno, sacar su fixture con la consulta del §3 del plan, añadirla a los tests y corregir la calculadora.</p>");
            sb.AppendLine($"<p>{WebUtility.HtmlEncode(resumen.ToString())}</p>");
            foreach (ResumenEmpresaSombraPrecioMedio empresa in resumen.Empresas.Where(e => e.PrimerosNoEsperados.Any()))
            {
                sb.AppendLine($"<h3>Empresa {WebUtility.HtmlEncode(empresa.Empresa)}: {empresa.NoEsperados} (los {empresa.PrimerosNoEsperados.Count} primeros)</h3>");
                sb.AppendLine("<table border='1' cellpadding='4' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
                sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'><th>Producto</th><th>Clasificación</th><th>Media BD</th>" +
                    "<th>Media C#</th><th>Líneas distintas</th><th>Primera línea distinta</th><th>Ventas distintas</th><th>Avisos / error</th></tr>");
                foreach (ResultadoSombraPrecioMedio p in empresa.PrimerosNoEsperados)
                {
                    DiferenciaPrecioMedio d = p.Diferencia;
                    string avisos = p.Error ?? string.Join("; ", (p.Calculo?.Avisos ?? new List<AvisoPrecioMedio>()).Select(a => a.ToString()));
                    sb.AppendLine("<tr>" +
                        $"<td>{WebUtility.HtmlEncode(p.Producto)}</td><td>{p.Clasificacion}</td>" +
                        $"<td style='text-align: right;'>{d?.PrecioMedioBD:0.0000}</td><td style='text-align: right;'>{d?.PrecioMedioCalculado:0.0000}</td>" +
                        $"<td style='text-align: center;'>{d?.LineasDistintas}/{d?.LineasComparadas}</td><td>{d?.PrimeraLineaDistinta}</td>" +
                        $"<td style='text-align: center;'>{d?.VentasDistintas}</td><td>{WebUtility.HtmlEncode(avisos)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("<p style='color: #888; font-size: 11px;'>Detalle de cada producto: tabla PreciosMediosSombra (FechaPasada = " +
                $"{resumen.FechaPasada:yyyy-MM-dd}) o GET api/PreciosMedios/{{empresa}}/{{producto}}. Para apagar la sombra: parámetro " +
                "PreciosMediosSombra de (defecto) a 0.</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }
}
