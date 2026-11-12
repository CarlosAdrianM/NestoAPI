using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    public enum EstadoPasadaCorreosSeguimiento
    {
        /// <summary>Faltan las claves en secretos.config: no se hace nada ni se avisa.</summary>
        SinConfiguracion,
        /// <summary>Falta Scripts/Issue857_ComprasCorreosSeguimiento.sql: no se hace nada (se apunta una vez).</summary>
        SinTabla,
        Hecha,
        /// <summary>Graph ha fallado (401, 429, red…): se apunta y se sigue en la siguiente pasada.</summary>
        ConError
    }

    public class ResultadoPasadaCorreosSeguimiento
    {
        public EstadoPasadaCorreosSeguimiento Estado { get; set; }
        public int Leidos { get; set; }
        public int Guardados { get; set; }
        public int YaGuardados { get; set; }
        public int Medidos { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// NestoAPI#857 (fase 1, EN SOMBRA): una pasada del job «compras-correos-seguimiento». Lee lo nuevo del buzón de Compras, guarda
    /// SOLO los correos con algún seguimiento (o de un remitente marcado) con su agencia, código, enlace, extracto y la propuesta de
    /// proveedor y pedidos de compra candidatos; y, en un segundo paso, apunta qué candidato se recibió de verdad (para medir la
    /// asignación antes de usarla). No escribe NADA en los pedidos de compra.
    /// </summary>
    public class ServicioCorreosSeguimientoCompras
    {
        public const int DIAS_PRIMERA_LECTURA = 7;
        public const int DIAS_MEDICION = 60;
        public const int LONGITUD_ASUNTO = 255;
        private static bool sinTablaApuntado;

        private readonly ILectorBuzonCompras lector;
        private readonly IRepositorioCorreosSeguimiento repositorio;
        private readonly IReadOnlyList<IAgenciaEntrada> agencias;
        private readonly Action<Exception> registrar;
        private readonly string empresa;

        /// <param name="lector">Null = sin claves en secretos.config (no se hace nada).</param>
        public ServicioCorreosSeguimientoCompras(ILectorBuzonCompras lector, IRepositorioCorreosSeguimiento repositorio,
            Action<Exception> registrar = null, IReadOnlyList<IAgenciaEntrada> agencias = null, string empresa = null)
        {
            this.lector = lector;
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrar = registrar ?? (_ => { });
            this.agencias = agencias ?? AgenciasEntradaRegistradas.Todas;
            this.empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa;
        }

        /// <summary>Para los tests: que el «sin tabla» se vuelva a apuntar.</summary>
        internal static void OlvidarSinTablaApuntado() => sinTablaApuntado = false;

        public async Task<ResultadoPasadaCorreosSeguimiento> Pasada(DateTime ahora)
        {
            var resultado = new ResultadoPasadaCorreosSeguimiento { Estado = EstadoPasadaCorreosSeguimiento.Hecha };
            if (lector == null)
            {
                resultado.Estado = EstadoPasadaCorreosSeguimiento.SinConfiguracion;
                return resultado;
            }
            if (!repositorio.TablasExisten())
            {
                if (!sinTablaApuntado)
                {
                    sinTablaApuntado = true;
                    registrar(new Exception("[Correos de seguimiento de compras #857] (informativo) Faltan las tablas: lanzar " +
                        "Scripts/Issue857_ComprasCorreosSeguimiento.sql. Mientras, no se lee el buzón."));
                }
                resultado.Estado = EstadoPasadaCorreosSeguimiento.SinTabla;
                return resultado;
            }

            string guardado = await repositorio.LeerEstado(lector.Buzon).ConfigureAwait(false);
            LecturaBuzonCompras lectura = await lector.LeerNuevos(guardado, ahora.AddDays(-DIAS_PRIMERA_LECTURA)).ConfigureAwait(false)
                ?? new LecturaBuzonCompras { Error = "El lector no ha devuelto nada." };
            resultado.Leidos = lectura.Correos.Count;

            bool todoGuardado = true;
            if (lectura.Correos.Any())
            {
                todoGuardado = await Guardar(lectura.Correos, resultado).ConfigureAwait(false);
            }

            // Solo se avanza el delta si lo leído se ha podido guardar entero (si no, la próxima pasada lo vuelve a leer: es idempotente)
            if (todoGuardado)
            {
                if (lectura.Reiniciar)
                {
                    await repositorio.GuardarEstado(lector.Buzon, null, lectura.Error).ConfigureAwait(false);
                }
                else if (!string.IsNullOrEmpty(lectura.SiguienteEnlace) && lectura.SiguienteEnlace != guardado)
                {
                    await repositorio.GuardarEstado(lector.Buzon, lectura.SiguienteEnlace, lectura.Error).ConfigureAwait(false);
                }
            }
            if (lectura.Error != null)
            {
                resultado.Estado = EstadoPasadaCorreosSeguimiento.ConError;
                resultado.Error = lectura.Error;
                registrar(new Exception($"[Correos de seguimiento de compras #857] {lectura.Error} (se sigue en la siguiente pasada; leídos {resultado.Leidos})"));
            }

            resultado.Medidos = await Medir(ahora).ConfigureAwait(false);
            return resultado;
        }

        private async Task<bool> Guardar(List<CorreoCompras> correos, ResultadoPasadaCorreosSeguimiento resultado)
        {
            List<RemitenteMarcado> marcados;
            try
            {
                marcados = await repositorio.LeerRemitentes().ConfigureAwait(false) ?? new List<RemitenteMarcado>();
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido leer los remitentes marcados: " + ex.Message, ex));
                return false;
            }

            List<PedidoCompraEnAire> pedidos = null;
            bool todo = true;
            foreach (CorreoCompras correo in correos)
            {
                try
                {
                    AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(correo, agencias, marcados);
                    if (!analisis.Guardar || correo.Clave() == null)
                    {
                        continue;
                    }
                    if (pedidos == null)
                    {
                        pedidos = await LeerPedidos().ConfigureAwait(false);
                    }
                    AsignacionPedidoCompra asignacion = AsignadorPedidosCompra.Asignar(correo, analisis.RemitentesOriginales, pedidos);
                    CorreoSeguimientoFila fila = Fila(correo, analisis, asignacion);
                    if (await repositorio.Insertar(fila).ConfigureAwait(false))
                    {
                        resultado.Guardados++;
                    }
                    else
                    {
                        resultado.YaGuardados++;
                    }
                }
                catch (Exception ex)
                {
                    todo = false;
                    registrar(new Exception($"[Correos de seguimiento de compras #857] No se ha podido guardar el correo del {correo?.Recibido:dd/MM/yyyy HH:mm} " +
                        $"de {correo?.RemitenteDominio}: {ex.Message}", ex));
                }
            }
            return todo;
        }

        private async Task<List<PedidoCompraEnAire>> LeerPedidos()
        {
            try
            {
                return await repositorio.LeerPedidosEnAire(empresa).ConfigureAwait(false) ?? new List<PedidoCompraEnAire>();
            }
            catch (Exception ex)
            {
                // Sin pedidos se guarda igual, sin candidatos
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido leer los pedidos de compra en el aire: " + ex.Message, ex));
                return new List<PedidoCompraEnAire>();
            }
        }

        internal static CorreoSeguimientoFila Fila(CorreoCompras correo, AnalisisCorreoSeguimiento analisis, AsignacionPedidoCompra asignacion)
        {
            SeguimientoCorreo primero = analisis.Seguimientos.FirstOrDefault();
            return new CorreoSeguimientoFila
            {
                MensajeId = correo.Clave(),
                Recibido = correo.Recibido,
                RemitenteDireccion = TextoCorreo.Recortar(correo.RemitenteDireccion?.ToLowerInvariant(), 255),
                RemitenteDominio = correo.RemitenteDominio,
                Asunto = TextoCorreo.Recortar(correo.Asunto, LONGITUD_ASUNTO),
                Agencia = TextoCorreo.Recortar(analisis.Agencia, 50),
                Codigo = TextoCorreo.Recortar(primero?.Codigo, 50),
                Url = TextoCorreo.Recortar(primero?.Url, 1000),
                Seguimientos = analisis.Seguimientos.Count > 1
                    ? TextoCorreo.Recortar(JsonConvert.SerializeObject(analisis.Seguimientos.Take(20)), 4000)
                    : null,
                Extracto = TextoCorreo.Recortar(analisis.Extracto, 600),
                ProveedorPropuesto = asignacion?.ProveedorPropuesto,
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(asignacion?.Candidatos)
            };
        }

        /// <summary>Segundo paso: de los guardados con candidatos y sin real, si alguno de sus candidatos ya tiene albarán, cuál.</summary>
        internal async Task<int> Medir(DateTime ahora)
        {
            try
            {
                List<CorreoSeguimientoFila> pendientes = await repositorio.LeerPendientesDeMedir(ahora.AddDays(-DIAS_MEDICION)).ConfigureAwait(false)
                    ?? new List<CorreoSeguimientoFila>();
                if (!pendientes.Any())
                {
                    return 0;
                }
                var candidatos = pendientes.ToDictionary(p => p.Id, p => AsignadorPedidosCompra.Deserializar(p.PedidosCandidatos));
                List<CandidatoPedidoCompra> todos = candidatos.Values.SelectMany(c => c).ToList();
                if (!todos.Any())
                {
                    return 0;
                }
                List<AlbaranPedidoCompra> albaranes = await repositorio.LeerAlbaranes(todos).ConfigureAwait(false) ?? new List<AlbaranPedidoCompra>();
                int medidos = 0;
                foreach (CorreoSeguimientoFila pendiente in pendientes)
                {
                    AlbaranPedidoCompra real = AsignadorPedidosCompra.DecidirReal(pendiente.Recibido, candidatos[pendiente.Id], albaranes);
                    if (real != null)
                    {
                        await repositorio.GuardarReal(pendiente.Id, real).ConfigureAwait(false);
                        medidos++;
                    }
                }
                return medidos;
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se ha podido medir la asignación: " + ex.Message, ex));
                return 0;
            }
        }
    }

    /// <summary>NestoAPI#857: remitente captado aún sin marcar (para que Compras diga «este es GLS»).</summary>
    public class CandidatoRemitenteDTO
    {
        public string Remitente { get; set; }
        public string Dominio { get; set; }
        public int Correos { get; set; }
        public DateTime UltimoRecibido { get; set; }
        public string UltimoAsunto { get; set; }
        public List<string> DominiosEnlaces { get; set; } = new List<string>();
        public List<string> AgenciasReconocidas { get; set; } = new List<string>();
    }

    public static class CandidatosRemitentes
    {
        /// <summary>Los remitentes de lo captado que no están marcados (activos), con más correos primero.</summary>
        public static List<CandidatoRemitenteDTO> Calcular(IEnumerable<CorreoSeguimientoFila> correos, IEnumerable<RemitenteMarcado> marcados)
        {
            List<RemitenteMarcado> activos = (marcados ?? Enumerable.Empty<RemitenteMarcado>()).Where(m => m != null && m.Activo).ToList();
            return (correos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.RemitenteDireccion))
                .Where(c => !activos.Any(m => m.Casa(c.RemitenteDireccion)))
                .GroupBy(c => c.RemitenteDireccion.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    CorreoSeguimientoFila ultimo = g.OrderByDescending(c => c.Recibido).First();
                    return new CandidatoRemitenteDTO
                    {
                        Remitente = g.Key,
                        Dominio = TextoCorreo.Dominio(g.Key),
                        Correos = g.Count(),
                        UltimoRecibido = ultimo.Recibido,
                        UltimoAsunto = ultimo.Asunto,
                        DominiosEnlaces = g.SelectMany(Urls).Select(ExtractorEnlaces.Host).Where(h => h != null).Distinct().OrderBy(h => h).ToList(),
                        AgenciasReconocidas = g.Select(c => c.Agencia).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().OrderBy(a => a).ToList()
                    };
                })
                .OrderByDescending(c => c.Correos).ThenByDescending(c => c.UltimoRecibido)
                .ToList();
        }

        private static IEnumerable<string> Urls(CorreoSeguimientoFila fila)
        {
            if (!string.IsNullOrWhiteSpace(fila.Url))
            {
                yield return fila.Url;
            }
            if (!string.IsNullOrWhiteSpace(fila.Seguimientos))
            {
                List<SeguimientoCorreo> todos;
                try
                {
                    todos = JsonConvert.DeserializeObject<List<SeguimientoCorreo>>(fila.Seguimientos) ?? new List<SeguimientoCorreo>();
                }
                catch (JsonException)
                {
                    todos = new List<SeguimientoCorreo>();
                }
                foreach (SeguimientoCorreo s in todos.Where(s => !string.IsNullOrWhiteSpace(s.Url)))
                {
                    yield return s.Url;
                }
            }
        }
    }

    /// <summary>
    /// NestoAPI#857: el job de Hangfire «compras-correos-seguimiento», cada 30 minutos de lunes a viernes de 8:00 a 18:30 (hora de
    /// Madrid). Sin las claves de secretos.config no hace nada; sin las tablas, lo apunta una vez. Nunca lanza.
    /// </summary>
    public static class CorreosSeguimientoComprasJobsService
    {
        public const string JOB_ID = "compras-correos-seguimiento";
        public const string CRON = "*/30 8-18 * * 1-5";
        private const string USUARIO_ELMAH = "Sistema (correos de seguimiento de compras)";

        public static async Task Pasada()
        {
            try
            {
                ConfiguracionBuzonCompras configuracion = ConfiguracionBuzonCompras.Leer(clave => ConfigurationManager.AppSettings[clave]);
                ILectorBuzonCompras lector = configuracion == null ? null : new LectorBuzonComprasGraph(configuracion);
                var servicio = new ServicioCorreosSeguimientoCompras(lector, new RepositorioCorreosSeguimiento(), ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
                _ = await servicio.Pasada(DateTime.Now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Correos de seguimiento de compras #857] Error en la pasada: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }
    }
}
