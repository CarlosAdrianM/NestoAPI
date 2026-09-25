using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>Nivel del interruptor del recordatorio de reposición (NestoAPI#532).</summary>
    public enum ModoRecordatorioReposicion
    {
        /// <summary>No hace nada. Es el valor por defecto (sin fila, "0" o cualquier cosa no reconocida).</summary>
        Apagado,
        /// <summary>Calcula la lista y la manda SOLO al equipo interno. No escribe a ningún cliente.</summary>
        Sombra
    }

    /// <summary>
    /// NestoAPI#532 (corte 1, cálculo en seco + modo sombra): job semanal del recordatorio de reposición.
    /// El criterio vive en <see cref="CalculadoraReposicion"/>, la lectura en
    /// <see cref="SelectorRecordatoriosReposicion"/> y el texto en <see cref="PlantillaRecordatorioReposicion"/>.
    ///
    /// Interruptor: parámetro <c>RecordatorioReposicion</c> bajo «(defecto)» en ParámetrosUsuario.
    /// Sin fila (así nace) = APAGADO. Con "Sombra", cada jueves manda UN correo al equipo que revisa los
    /// correos posventa (CorreosPostCompra:EmailsTest: Laura, Manuel y Carlos) con los clientes y productos
    /// que recibirían el aviso, el grupo de control, los que se quedan fuera y por qué, y una muestra del
    /// correo. En este corte NO se escribe a clientes ni se registra nada.
    /// </summary>
    public static class RecordatorioReposicionJobsService
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        internal const string VALOR_SOMBRA = "Sombra";
        internal const int MAXIMO_FILAS_DESCARTES = 300;

        // Sin reintentos: si falla, la semana que viene vuelve a pasar. Y nunca dos a la vez.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task Procesar()
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await Procesar(
                        new LectorParametrosUsuario(),
                        new ServicioCorreoElectronico(),
                        (consumibles, hoy) => new SelectorRecordatoriosReposicion(db).Calcular(EMPRESA, hoy, consumibles),
                        RellenarEnlacesTienda,
                        DestinatariosSombra(),
                        DateTime.Today).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>
        /// Núcleo del job, con las dependencias inyectadas para testearlo sin BD ni SMTP.
        /// Devuelve el modo con el que se ha ejecutado.
        /// </summary>
        internal static async Task<ModoRecordatorioReposicion> Procesar(ILectorParametrosUsuario lector,
            IServicioCorreoElectronico servicioCorreo,
            Func<string, DateTime, Task<ResultadoRecordatorioReposicionDTO>> calcular,
            Func<RecordatorioReposicionClienteDTO, Task> rellenarEnlaces,
            string destinatarios,
            DateTime hoy)
        {
            ModoRecordatorioReposicion modo = LeerModo(lector);
            if (modo == ModoRecordatorioReposicion.Apagado)
            {
                return modo;
            }

            ResultadoRecordatorioReposicionDTO resultado = await calcular(LeerConsumibles(lector), hoy).ConfigureAwait(false);
            _ = await EnviarSombra(resultado, servicioCorreo, rellenarEnlaces, destinatarios).ConfigureAwait(false);
            return modo;
        }

        /// <summary>
        /// Manda el correo sombra (solo al equipo interno). Lo usa el job y el endpoint para lanzarlo a mano.
        /// </summary>
        internal static async Task<bool> EnviarSombra(ResultadoRecordatorioReposicionDTO resultado,
            IServicioCorreoElectronico servicioCorreo,
            Func<RecordatorioReposicionClienteDTO, Task> rellenarEnlaces,
            string destinatarios)
        {
            RecordatorioReposicionClienteDTO muestra = resultado?.SeAvisarian?.FirstOrDefault();
            if (muestra != null && rellenarEnlaces != null)
            {
                try
                {
                    await rellenarEnlaces(muestra).ConfigureAwait(false);
                }
                catch
                {
                    // Sin enlaces la muestra sigue sirviendo para revisar el texto
                }
            }

            using (MailMessage correo = ConstruirCorreoSombra(resultado, destinatarios))
            {
                bool enviado = servicioCorreo.EnviarCorreoSMTP(correo);
                if (!enviado)
                {
                    ElmahHelper.Log(new Exception(
                        $"[Recordatorio reposición #532] No se ha podido mandar el correo sombra ({resultado?.Correos ?? 0} correos)."));
                }
                return enviado;
            }
        }

        /// <summary>Cualquier fallo al leer el parámetro cuenta como apagado.</summary>
        internal static ModoRecordatorioReposicion LeerModo(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarModo(lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION));
            }
            catch
            {
                return ModoRecordatorioReposicion.Apagado;
            }
        }

        internal static ModoRecordatorioReposicion InterpretarModo(string valor)
            => string.Equals(valor?.Trim(), VALOR_SOMBRA, StringComparison.OrdinalIgnoreCase)
                ? ModoRecordatorioReposicion.Sombra
                : ModoRecordatorioReposicion.Apagado;

        /// <summary>Sin fila o con error: null (la calculadora usa la lista por defecto).</summary>
        internal static string LeerConsumibles(ILectorParametrosUsuario lector)
        {
            try
            {
                return lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_CONSUMIBLES);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Los mismos que revisan el modo test de los correos posventa (#74); si no hay, Carlos.</summary>
        internal static string DestinatariosSombra()
        {
            string configurados = ConfigurationManager.AppSettings["CorreosPostCompra:EmailsTest"];
            return string.IsNullOrWhiteSpace(configurados) ? Constantes.Correos.INFORMATICA : configurados;
        }

        /// <summary>Enlace a la ficha de la tienda de cada producto (una llamada a PrestaShop por producto).</summary>
        internal static async Task RellenarEnlacesTienda(RecordatorioReposicionClienteDTO correo)
        {
            foreach (CandidatoReposicionDTO producto in correo.Productos)
            {
                string url = await ProductoDTO.LeerUrlTiendaOnline(producto.Producto).ConfigureAwait(false);
                producto.EnlaceTienda = !string.IsNullOrWhiteSpace(url)
                    ? url
                    : ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda(producto.NombreProducto);
            }
        }

        internal static MailMessage ConstruirCorreoSombra(ResultadoRecordatorioReposicionDTO resultado, string destinatarios)
        {
            resultado = resultado ?? new ResultadoRecordatorioReposicionDTO { Fecha = DateTime.Today };
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"[Sombra] Recordatorio de reposición {resultado.Fecha:dd/MM/yyyy}: {resultado.Correos} correos " +
                    $"({resultado.Productos} productos), {resultado.GrupoControl.Count} en el grupo de control",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in (destinatarios ?? Constantes.Correos.INFORMATICA)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                mail.To.Add(destinatario);
            }
            if (mail.To.Count == 0)
            {
                mail.To.Add(Constantes.Correos.INFORMATICA);
            }
            mail.Body = GenerarHtmlSombra(resultado);
            return mail;
        }

        internal static string GenerarHtmlSombra(ResultadoRecordatorioReposicionDTO resultado)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<h2>Recordatorio de reposición (modo sombra)</h2>");
            sb.AppendLine("<p><b>Esto es una prueba: no se ha escrito a ningún cliente.</b> Es la lista de clientes a los que esta " +
                "semana se les recordaría reponer un producto (NestoAPI#532), calculada con TODAS las ventas de Nesto (vendedor, " +
                "teléfono, tienda online...). Revisad sobre todo si a alguno le habría sentado mal el correo.</p>");
            sb.AppendLine($"<p>Criterio: productos consumibles comprados al menos {CalculadoraReposicion.MINIMO_COMPRAS} días distintos " +
                $"desde el {resultado.HistorialDesde:dd/MM/yyyy}. Su ritmo es la mediana de días entre compras (mínimo " +
                $"{CalculadoraReposicion.INTERVALO_MINIMO_DIAS}); toca reponer cuando ha pasado 1,25 veces ese ritmo y menos de 3 veces " +
                $"(más allá ya no es reposición, es un cliente que se ha ido). Hasta {CalculadoraReposicion.MAXIMO_PRODUCTOS_POR_CORREO} " +
                "productos por correo, los de más importe habitual. Consumibles: " +
                $"<code>{WebUtility.HtmlEncode(resultado.Consumibles)}</code>.</p>");

            sb.AppendLine($"<h3>Se les escribiría ({resultado.SeAvisarian.Count} correos, {resultado.Productos} productos)</h3>");
            if (resultado.SeAvisarian.Any())
            {
                AppendTablaCorreos(sb, resultado.SeAvisarian);
            }
            else
            {
                sb.AppendLine("<p>Esta semana no se escribiría a nadie.</p>");
            }

            if (resultado.GrupoControl.Any())
            {
                sb.AppendLine($"<h3>Grupo de control ({resultado.GrupoControl.Count}): cumplen, pero no se les escribiría</h3>");
                sb.AppendLine($"<p>Un {CalculadoraReposicion.PORCENTAJE_GRUPO_CONTROL} % de los clientes (siempre los mismos) no recibe el " +
                    "correo, para comparar si los que lo reciben reponen más que los que no.</p>");
                AppendTablaCorreos(sb, resultado.GrupoControl);
            }

            if (resultado.Descartes.Any())
            {
                sb.AppendLine($"<h3>Tocaría por ritmo, pero se quedan fuera ({resultado.Descartes.Count})</h3>");
                sb.AppendLine("<ul>");
                foreach (var motivo in resultado.Descartes
                    .GroupBy(d => MotivoResumido(d.Motivo))
                    .OrderByDescending(g => g.Count()))
                {
                    sb.AppendLine($"<li>{WebUtility.HtmlEncode(motivo.Key)}: {motivo.Count()}</li>");
                }
                sb.AppendLine("</ul>");
                AppendTablaDescartes(sb, resultado.Descartes.Take(MAXIMO_FILAS_DESCARTES).ToList());
                if (resultado.Descartes.Count > MAXIMO_FILAS_DESCARTES)
                {
                    sb.AppendLine($"<p>(Solo los {MAXIMO_FILAS_DESCARTES} primeros. La lista completa, en GET api/CorreosPostCompra/Reposicion.)</p>");
                }
            }

            RecordatorioReposicionClienteDTO muestra = resultado.SeAvisarian.FirstOrDefault();
            if (muestra != null)
            {
                sb.AppendLine("<h3>Muestra: el correo que recibiría el primero</h3>");
                sb.AppendLine("<div style='border: 1px solid #ccc; padding: 12px; background-color: #fafafa;'>");
                sb.AppendLine($"<p><b>Para:</b> {WebUtility.HtmlEncode(muestra.Email)} ({WebUtility.HtmlEncode(muestra.Cliente)} " +
                    $"{WebUtility.HtmlEncode(muestra.Nombre)})</p>");
                sb.AppendLine($"<p><b>Asunto:</b> {WebUtility.HtmlEncode(PlantillaRecordatorioReposicion.Asunto(muestra))}</p>");
                sb.AppendLine(PlantillaRecordatorioReposicion.CuerpoHtml(muestra));
                sb.AppendLine("</div>");
            }

            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Generado el {DateTime.Now:dd/MM/yyyy HH:mm}. " +
                "Para apagarlo: parámetro RecordatorioReposicion de (defecto) a 0 (Scripts/Issue532_RecordatorioReposicion.sql).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        /// <summary>Los motivos de sustituto y correo duplicado llevan el producto/cliente al final: se agrupan sin él.</summary>
        internal static string MotivoResumido(string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                return "(sin motivo)";
            }
            int dosPuntos = motivo.IndexOf(": ", StringComparison.Ordinal);
            return dosPuntos > 0 ? motivo.Substring(0, dosPuntos) : motivo.TrimEnd('.');
        }

        private static void AppendTablaCorreos(StringBuilder sb, List<RecordatorioReposicionClienteDTO> correos)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'><th>Cliente</th><th>Nombre</th><th>Correo</th>" +
                "<th>Comercial</th><th>Producto</th><th>Compras</th><th>Ritmo (días)</th><th>Última compra</th>" +
                "<th>Días desde</th><th>Importe habitual</th></tr>");
            bool alternar = false;
            foreach (RecordatorioReposicionClienteDTO correo in correos)
            {
                string fondo = alternar ? "#f2f2f2" : "#ffffff";
                int filas = Math.Max(1, correo.Productos.Count);
                bool primera = true;
                foreach (CandidatoReposicionDTO p in correo.Productos)
                {
                    sb.Append($"<tr style='background-color: {fondo};'>");
                    if (primera)
                    {
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Cliente)}</td>");
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Nombre)}</td>");
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Email)}</td>");
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.VendedorNombre ?? "")}</td>");
                        primera = false;
                    }
                    AppendCeldasProducto(sb, p);
                    sb.AppendLine("</tr>");
                }
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        private static void AppendTablaDescartes(StringBuilder sb, List<CandidatoReposicionDTO> descartes)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.AppendLine("<tr style='background-color: #888; color: white;'><th>Cliente</th><th>Producto</th><th>Compras</th>" +
                "<th>Ritmo (días)</th><th>Última compra</th><th>Días desde</th><th>Importe habitual</th><th>Motivo</th></tr>");
            bool alternar = false;
            foreach (CandidatoReposicionDTO d in descartes)
            {
                sb.Append($"<tr style='background-color: {(alternar ? "#f2f2f2" : "#ffffff")};'>");
                sb.Append($"<td>{WebUtility.HtmlEncode(d.Cliente)}</td>");
                AppendCeldasProducto(sb, d);
                sb.Append($"<td>{WebUtility.HtmlEncode(d.Motivo)}</td>");
                sb.AppendLine("</tr>");
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        private static void AppendCeldasProducto(StringBuilder sb, CandidatoReposicionDTO p)
        {
            sb.Append($"<td>{WebUtility.HtmlEncode(p.Producto)} {WebUtility.HtmlEncode(p.NombreProducto)}</td>");
            sb.Append($"<td style='text-align: center;'>{p.NumeroCompras}</td>");
            sb.Append($"<td style='text-align: center;'>{p.IntervaloDias}</td>");
            sb.Append($"<td>{p.UltimaCompra:dd/MM/yyyy}</td>");
            sb.Append($"<td style='text-align: center;'>{p.DiasDesdeUltimaCompra}</td>");
            sb.Append($"<td style='text-align: right;'>{p.ImporteHabitual:N2} €</td>");
        }
    }
}
