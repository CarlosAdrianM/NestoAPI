using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    public enum EstadoConsultaEntregaAgencias
    {
        /// <summary>Interruptor Compras.CorreosSeguimientoConsultarAgencia apagado (sin fila o distinto de «1»): no se hace nada.</summary>
        Apagado,
        /// <summary>Falta Scripts/Issue857d o Scripts/Issue857f: no se hace nada (se apunta una vez).</summary>
        SinTabla,
        Hecha
    }

    public class ResultadoConsultaEntregaAgencias
    {
        public EstadoConsultaEntregaAgencias Estado { get; set; }
        /// <summary>Envíos (agencia + código) por los que se ha preguntado.</summary>
        public int Consultados { get; set; }
        /// <summary>Los que han contestado algo (fecha o estado).</summary>
        public int Contestados { get; set; }
        /// <summary>Pedidos a los que se ha cambiado la fecha.</summary>
        public int Corregidos { get; set; }
    }

    /// <summary>
    /// NestoAPI#857f (punto 5, marco): una vez al día vuelve a preguntar a la agencia por los envíos cuyo correo está aplicado a un pedido que
    /// sigue en el aire, y corrige la fecha por el mismo camino que un correo nuevo de la agencia (<see cref="ServicioAplicacionCorreosCompras.Corregir"/>:
    /// la de la agencia manda, rastro, promesa en riesgo, nunca antes de hoy). Solo pregunta a las agencias que saben
    /// (<see cref="IConsultaEntregaAgencia"/>; hoy ninguna), con tiempo máximo y sin lanzar. Interruptor propio: ParametrosUsuario «(defecto)»
    /// <see cref="CLAVE_INTERRUPTOR"/> = «1» (sin fila, apagado); escribir en compras exige además el de aplicar (Compras.CorreosSeguimientoAplicar).
    /// </summary>
    public class ServicioConsultaEntregaAgencias
    {
        public const string CLAVE_INTERRUPTOR = "Compras.CorreosSeguimientoConsultarAgencia";
        /// <summary>Solo los correos de este plazo (un envío que no ha llegado en un mes ya no se pregunta solo).</summary>
        public const int DIAS_MAXIMOS = 30;
        private static bool sinTablaApuntado;

        private readonly IRepositorioCorreosSeguimiento repositorio;
        private readonly ServicioAplicacionCorreosCompras aplicacion;
        private readonly IReadOnlyList<IAgenciaEntrada> agencias;
        private readonly Func<bool> interruptor;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;
        private readonly TimeSpan? tiempoMaximo;

        public ServicioConsultaEntregaAgencias(IRepositorioCorreosSeguimiento repositorio, ServicioAplicacionCorreosCompras aplicacion,
            IReadOnlyList<IAgenciaEntrada> agencias = null, Func<bool> interruptor = null, Func<DateTime> ahora = null,
            Action<Exception> registrar = null, TimeSpan? tiempoMaximo = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.aplicacion = aplicacion ?? throw new ArgumentNullException(nameof(aplicacion));
            this.agencias = agencias ?? AgenciasEntradaRegistradas.Todas;
            this.registrar = registrar ?? (_ => { });
            this.interruptor = interruptor ?? (() => InterruptorEncendido(this.registrar));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.tiempoMaximo = tiempoMaximo;
        }

        internal static bool InterruptorEncendido(Action<Exception> registrar)
        {
            try
            {
                return ServicioAplicacionCorreosCompras.Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, ServicioAplicacionCorreosCompras.USUARIO_DEFECTO, CLAVE_INTERRUPTOR));
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Correos de seguimiento de compras #857f] No se ha podido leer el interruptor {CLAVE_INTERRUPTOR}: no se consulta. {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>Para los tests: que el «sin tabla» se vuelva a apuntar.</summary>
        internal static void OlvidarSinTablaApuntado() => sinTablaApuntado = false;

        /// <summary>
        /// Los correos a consultar: aplicados, no entregados, con código, de una agencia que sabe consultar y cuyo pedido sigue en el aire; uno
        /// por agencia + código (el más reciente). PURA.
        /// </summary>
        public static List<(CorreoSeguimientoFila Fila, IConsultaEntregaAgencia Agencia)> AConsultar(IEnumerable<CorreoSeguimientoFila> correos,
            IEnumerable<PedidoCompraEnAire> enAire, IEnumerable<IAgenciaEntrada> agencias)
        {
            var claves = new HashSet<string>((enAire ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null)
                .Select(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)));
            List<IAgenciaEntrada> lista = (agencias ?? Enumerable.Empty<IAgenciaEntrada>()).ToList();
            return (correos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(f => f != null && f.PedidoAplicado.HasValue && f.EsSalidaNuestra != true && f.Descartado != true
                    && !string.IsNullOrWhiteSpace(f.Codigo) && !string.IsNullOrWhiteSpace(f.Agencia) && !EntregaConfirmadaCorreo.Es(f)
                    && claves.Contains(AsignadorPedidosCompra.Clave(f.EmpresaAplicada, f.PedidoAplicado.Value)))
                .Select(f => (Fila: f, Agencia: lista.FirstOrDefault(a => string.Equals(a.Nombre, f.Agencia.Trim(), StringComparison.OrdinalIgnoreCase)) as IConsultaEntregaAgencia))
                .Where(x => x.Agencia != null)
                .GroupBy(x => x.Fila.Agencia.Trim().ToUpperInvariant() + "|" + x.Fila.Codigo.Trim().ToUpperInvariant())
                .Select(g => g.OrderByDescending(x => x.Fila.Recibido).ThenByDescending(x => x.Fila.Id).First())
                .ToList();
        }

        /// <summary>Una pasada. Nunca lanza.</summary>
        public async Task<ResultadoConsultaEntregaAgencias> Pasada()
        {
            var resultado = new ResultadoConsultaEntregaAgencias { Estado = EstadoConsultaEntregaAgencias.Hecha };
            try
            {
                if (!interruptor())
                {
                    resultado.Estado = EstadoConsultaEntregaAgencias.Apagado;
                    return resultado;
                }
                if (!repositorio.TablasExisten() || !repositorio.AplicacionDisponible() || !repositorio.EstadoEnvioDisponible())
                {
                    if (!sinTablaApuntado)
                    {
                        sinTablaApuntado = true;
                        registrar(new Exception("[Correos de seguimiento de compras #857f] (informativo) Para consultar a las agencias hace falta " +
                            "Scripts/Issue857d_ComprasCorreosAplicarFecha.sql y Scripts/Issue857f_ComprasCorreosEstadoEnvio.sql. Mientras, no se consulta."));
                    }
                    resultado.Estado = EstadoConsultaEntregaAgencias.SinTabla;
                    return resultado;
                }
                // Si ninguna agencia sabe consultar (hoy), ni se lee nada
                if (!agencias.OfType<IConsultaEntregaAgencia>().Any())
                {
                    return resultado;
                }
                DateTime momento = ahora();
                List<CorreoSeguimientoFila> correos = await repositorio.LeerCorreos(momento.AddDays(-DIAS_MAXIMOS)).ConfigureAwait(false)
                    ?? new List<CorreoSeguimientoFila>();
                List<PedidoCompraEnAire> enAire = await repositorio.LeerPedidosEnAire(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false)
                    ?? new List<PedidoCompraEnAire>();
                foreach ((CorreoSeguimientoFila fila, IConsultaEntregaAgencia agencia) in AConsultar(correos, enAire, agencias))
                {
                    resultado.Consultados++;
                    EntregaAgencia respuesta = await ConsultaEntregaSegura.Consultar(agencia, fila.Codigo, tiempoMaximo, registrar).ConfigureAwait(false);
                    if (respuesta == null || !respuesta.DiceAlgo)
                    {
                        continue;
                    }
                    resultado.Contestados++;
                    await repositorio.GuardarConsulta(fila.Id, respuesta, momento).ConfigureAwait(false);
                    if (respuesta.Estado == EstadoEnvioAgencia.Entregado || respuesta.Entrega == null)
                    {
                        continue; // entregado: la recepción la marca el albarán; sin fecha: nada que corregir
                    }
                    CorreoSeguimientoFila actualizada = await repositorio.LeerCorreo(fila.Id).ConfigureAwait(false) ?? fila;
                    ResultadoCorreoGuardado corregido = await aplicacion.Corregir(actualizada).ConfigureAwait(false);
                    if (corregido.Desenlace == DesenlaceCorreoSeguimiento.FechaCambiada)
                    {
                        resultado.Corregidos++;
                    }
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857f] Error en la consulta de entregas a las agencias: " + ex.Message, ex));
            }
            return resultado;
        }
    }

    /// <summary>
    /// NestoAPI#857f: el job de Hangfire «compras-consulta-entrega-agencias», de lunes a viernes a las 7:30 (hora de Madrid), en la cola por
    /// defecto (como «compras-correos-seguimiento»). Apagado salvo ParametrosUsuario «(defecto)» Compras.CorreosSeguimientoConsultarAgencia = «1».
    /// Nunca lanza.
    /// </summary>
    public static class ConsultaEntregaAgenciasJobsService
    {
        public const string JOB_ID = "compras-consulta-entrega-agencias";
        public const string CRON = "30 7 * * 1-5";
        private const string USUARIO_ELMAH = "Sistema (consulta de entregas a las agencias)";

        public static async Task Pasada()
        {
            try
            {
                Action<Exception> registrar = ex => ElmahHelper.Log(ex, USUARIO_ELMAH);
                var repositorio = new RepositorioCorreosSeguimiento();
                var aplicacion = new ServicioAplicacionCorreosCompras(repositorio, registrar: registrar);
                _ = await new ServicioConsultaEntregaAgencias(repositorio, aplicacion, registrar: registrar).Pasada().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Correos de seguimiento de compras #857f] Error en la consulta a las agencias: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }
    }
}
