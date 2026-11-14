using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857 (fase 3): cuerpo de POST api/Compras/AgenciasEntrada/Correos/{id}/Asignar.</summary>
    public class PeticionAsignarCorreoCompraDTO
    {
        /// <summary>Empresa del pedido de compra (vacía = «1»).</summary>
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Guardar quién expide el paquete (RemitentePaquete del correo) como de este proveedor para la próxima vez.</summary>
        public bool RecordarRemitente { get; set; }
    }

    /// <summary>NestoAPI#857 (fase 3): lo que ha pasado al poner la fecha de un correo en un pedido de compra.</summary>
    public class RespuestaAsignacionCorreoCompraDTO
    {
        public int CorreoId { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        /// <summary>Líneas enviadas y sin recibir del pedido.</summary>
        public int LineasPendientes { get; set; }
        /// <summary>Las que tenían otra fecha y se han cambiado (0 = ya la tenían todas).</summary>
        public int LineasCambiadas { get; set; }
        /// <summary>Las fechas que tenían las líneas cambiadas (null = sin fecha), sin repetir.</summary>
        public List<DateTime?> FechasAnteriores { get; set; } = new List<DateTime?>();
        public DateTime FechaNueva { get; set; }
        /// <summary>El correo ya estaba aplicado a este mismo pedido: no se ha tocado nada.</summary>
        public bool YaEstabaAplicado { get; set; }
        /// <summary>Al reasignar: el pedido al que estaba aplicado (su fecha NO se deshace).</summary>
        public string EmpresaAnterior { get; set; }
        public int? PedidoAnterior { get; set; }
        public string RemitentePaquete { get; set; }
        public bool RemitenteRecordado { get; set; }
        /// <summary>Correos más del mismo envío (comparten código) a los que también se ha puesto este pedido.</summary>
        public int CorreosDelMismoEnvio { get; set; }
        /// <summary>Si el pedido elegido era el sugerido (null = no había sugerido).</summary>
        public bool? EligioSugerido { get; set; }
        public string Mensaje { get; set; }
    }

    public enum CodigoOperacionCorreoCompra
    {
        Ok,
        /// <summary>503: falta Scripts/Issue857_… o Scripts/Issue857d_….</summary>
        SinTabla,
        /// <summary>404.</summary>
        NoExiste,
        /// <summary>400.</summary>
        PeticionIncorrecta,
        /// <summary>400: el pedido no tiene líneas enviadas y sin recibir.</summary>
        NoEnElAire,
        /// <summary>409: ya aplicado a otro pedido (sin reasignar) o, al descartar, ya aplicado.</summary>
        Conflicto
    }

    public class ResultadoOperacionCorreoCompra
    {
        public CodigoOperacionCorreoCompra Codigo { get; set; }
        public string Mensaje { get; set; }
        public RespuestaAsignacionCorreoCompraDTO Respuesta { get; set; }
        /// <summary>Al descartar: cuántos correos más del mismo envío se han descartado.</summary>
        public int CorreosDelMismoEnvio { get; set; }
    }

    /// <summary>NestoAPI#857 (fase 3): la campana a Compras de un correo que se queda por asignar.</summary>
    public interface IAvisadorCorreosPorAsignar
    {
        Task Avisar(CorreoSeguimientoFila fila, int candidatos);
    }

    /// <summary>Al buzón de Nesto de cada miembro de Compras (el mismo camino que las faltas del picking, <see cref="AvisadorCompras"/>).</summary>
    public class AvisadorCorreosPorAsignar : IAvisadorCorreosPorAsignar
    {
        public const string TIPO = "CompraSeguimientoPorAsignar";
        private readonly AvisadorCompras compras;

        public AvisadorCorreosPorAsignar(AvisadorCompras compras = null)
        {
            this.compras = compras ?? new AvisadorCompras(new ServicioNotificacionesPush());
        }

        public Task Avisar(CorreoSeguimientoFila fila, int candidatos) => compras.Avisar(AvisoCorreoPorAsignar.Titulo(fila),
            AvisoCorreoPorAsignar.Lineas(fila, candidatos), TIPO, AvisoCorreoPorAsignar.Datos(fila, TIPO));
    }

    /// <summary>
    /// NestoAPI#857 (fase 3, decisión de Carlos 12/11/26): el correo de seguimiento pone la fecha de recepción del pedido de compra.
    /// <list type="bullet">
    /// <item>El job, al guardar correos NUEVOS (uno de ellos de los últimos <see cref="ReglaFechaRecepcionPorCorreo.DIAS_APLICACION_AUTOMATICA"/>
    /// días): los junta por mismo envío (<see cref="MismoEnvio"/>) y, si el envío es inequívoco y el interruptor está encendido, pone el día de
    /// llegada (<see cref="ReglaFechaRecepcionPorCorreo"/>) a TODAS las líneas enviadas y sin recibir del pedido; si no, UNA campana a Compras
    /// por envío (<see cref="AvisarPendientes"/>). El histórico NUNCA escribe en compras.</item>
    /// <item>Compras: asignar a mano (también con el interruptor apagado y sin límite de días), reasignar, descartar y la memoria de
    /// remitentes del paquete.</item>
    /// </list>
    /// Sin Scripts/Issue857d no hace nada (el job, como antes; los endpoints, 503).
    /// </summary>
    public class ServicioAplicacionCorreosCompras
    {
        public const string CLAVE_INTERRUPTOR = "Compras.CorreosSeguimientoAplicar";
        public const string USUARIO_DEFECTO = "(defecto)";
        public const string APLICADO_POR_AUTOMATICO = "(automático)";
        /// <summary>LinPedidoCmp.Usuario de lo que cambia el job (varchar(30)).</summary>
        public const string USUARIO_LINEAS_AUTOMATICO = "Correo de seguimiento";
        public const string MENSAJE_SIN_SCRIPT = "Todavía no se puede: hay que lanzar Scripts/Issue857d_ComprasCorreosAplicarFecha.sql.";

        private readonly IRepositorioCorreosSeguimiento repositorio;
        private readonly Func<bool> interruptor;
        private readonly IAvisadorCorreosPorAsignar avisador;
        private readonly Action<string, IEnumerable<string>, string, IDictionary<string, CausaPromesaRiesgo>> anotarPromesaRiesgo;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;

        /// <param name="interruptor">Por defecto, ParametrosUsuario «(defecto)» <see cref="CLAVE_INTERRUPTOR"/> = «1».</param>
        /// <param name="anotarPromesaRiesgo">Por defecto, <see cref="EventosPromesaRiesgo.Anotar"/> (como el PUT de PedidosCompra).</param>
        public ServicioAplicacionCorreosCompras(IRepositorioCorreosSeguimiento repositorio, Func<bool> interruptor = null,
            IAvisadorCorreosPorAsignar avisador = null,
            Action<string, IEnumerable<string>, string, IDictionary<string, CausaPromesaRiesgo>> anotarPromesaRiesgo = null,
            Func<DateTime> ahora = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrar = registrar ?? (_ => { });
            this.interruptor = interruptor ?? (() => InterruptorEncendido(this.registrar));
            this.avisador = avisador;
            this.anotarPromesaRiesgo = anotarPromesaRiesgo ?? EventosPromesaRiesgo.Anotar;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Encendido SOLO con «1» (sin fila, vacío u otro valor = apagado). Se lee cada vez, sin caché. Si no se puede leer, apagado.</summary>
        internal static bool Interpretar(string valor) => valor?.Trim() == "1";

        internal static bool InterruptorEncendido(Action<Exception> registrar)
        {
            try
            {
                return Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE_INTERRUPTOR));
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Correos de seguimiento de compras #857] No se ha podido leer el interruptor {CLAVE_INTERRUPTOR}: no se aplica. {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>¿Están las tablas de #857 y lo de Scripts/Issue857d?</summary>
        public bool Disponible => repositorio.TablasExisten() && repositorio.AplicacionDisponible();

        /// <summary>
        /// El job, tras guardar una tanda de correos NUEVOS (nunca el histórico): los junta con los de su mismo envío (<see cref="MismoEnvio"/>),
        /// les pone los candidatos del envío a los que no tienen, y por envío: si es inequívoco (y el interruptor está encendido y alguno de los
        /// nuevos es reciente), pone la fecha del envío en el pedido y marca todos sus correos; si no, UNA campana a Compras por envío (no si ya
        /// se avisó por otro de sus correos). Se hace al final de la tanda para que los correos de un mismo envío que llegan juntos (UPS + MBE)
        /// se decidan juntos. Nunca lanza: un fallo se apunta y el correo queda por asignar.
        /// </summary>
        public async Task AlGuardar(IReadOnlyCollection<CorreoSeguimientoFila> nuevas)
        {
            List<CorreoSeguimientoFila> lista = (nuevas ?? new List<CorreoSeguimientoFila>()).Where(f => f != null && f.EsSalidaNuestra != true).ToList();
            if (!lista.Any())
            {
                return;
            }
            try
            {
                if (!repositorio.AplicacionDisponible())
                {
                    return;
                }
                DateTime desde = lista.Min(f => f.Recibido).AddDays(-MismoEnvio.DIAS);
                List<CorreoSeguimientoFila> guardados = await repositorio.LeerCorreos(desde).ConfigureAwait(false) ?? new List<CorreoSeguimientoFila>();
                var claves = new HashSet<string>(lista.Select(f => f.MensajeId));
                List<CorreoSeguimientoFila> nuevasGuardadas = guardados.Where(g => claves.Contains(g.MensajeId)).ToList();
                foreach (List<CorreoSeguimientoFila> grupo in MismoEnvio.Agrupar(guardados).Where(g => g.Any(nuevasGuardadas.Contains)).ToList())
                {
                    try
                    {
                        _ = await ProcesarEnvio(grupo, grupo.Where(nuevasGuardadas.Contains).ToList(), true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception("[Correos de seguimiento de compras #857] No se ha podido enlazar, aplicar o avisar el envío de los correos " +
                            string.Join(", ", grupo.Select(f => f.Id)) + ": " + ex.Message, ex));
                    }
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido aplicar o avisar los correos nuevos: " + ex.Message, ex));
            }
        }

        /// <summary>
        /// Reanálisis: rehace los enlaces por mismo envío de lo guardado desde <paramref name="desde"/> (candidatos heredados), sin aplicar ni
        /// avisar. Devuelve cuántos correos han cambiado.
        /// </summary>
        public async Task<int> Reenlazar(DateTime desde)
        {
            int cambiados = 0;
            try
            {
                if (!repositorio.AplicacionDisponible())
                {
                    return 0;
                }
                List<CorreoSeguimientoFila> guardados = await repositorio.LeerCorreos(desde.AddDays(-MismoEnvio.DIAS)).ConfigureAwait(false)
                    ?? new List<CorreoSeguimientoFila>();
                foreach (List<CorreoSeguimientoFila> grupo in MismoEnvio.Agrupar(guardados).Where(g => g.Count > 1 && g.Any(f => f.Recibido >= desde)).ToList())
                {
                    cambiados += await ProcesarEnvio(grupo, new List<CorreoSeguimientoFila>(), false).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857, reanálisis] No se han podido rehacer los enlaces por mismo envío: " + ex.Message, ex));
            }
            return cambiados;
        }

        /// <summary>Un envío: hereda candidatos y, si <paramref name="decidir"/>, aplica o avisa. Devuelve cuántos correos han heredado.</summary>
        private async Task<int> ProcesarEnvio(List<CorreoSeguimientoFila> grupo, List<CorreoSeguimientoFila> nuevas, bool decidir)
        {
            DecisionEnvio decision = MismoEnvio.Decidir(grupo);
            int heredados = 0;
            string json = AsignadorPedidosCompra.Serializar(decision.Candidatos);
            foreach (CorreoSeguimientoFila fila in decision.Correos.Where(f => MismoEnvio.Hereda(f, decision) && f.PedidosCandidatos != json).ToList())
            {
                if (await repositorio.ActualizarAsignacion(fila.Id, decision.ProveedorPropuesto, json).ConfigureAwait(false))
                {
                    fila.PedidosCandidatos = json;
                    fila.ProveedorPropuesto = decision.ProveedorPropuesto;
                    heredados++;
                }
            }
            DateTime momento = ahora();
            if (!decidir || !nuevas.Any(f => ReglaFechaRecepcionPorCorreo.Reciente(f.Recibido, momento)))
            {
                return heredados;
            }

            List<CorreoSeguimientoFila> pendientes = decision.Correos.Where(f => !f.PedidoAplicado.HasValue && f.Descartado != true).ToList();
            if (decision.Descartado)
            {
                // Compras ya dijo que este envío no es de ninguna compra: lo que llega después, igual
                foreach (CorreoSeguimientoFila fila in pendientes)
                {
                    _ = await repositorio.Descartar(fila.Id, APLICADO_POR_AUTOMATICO).ConfigureAwait(false);
                }
                return heredados;
            }
            if (decision.YaLlego)
            {
                // Un candidato del envío ya tiene albarán: el envío ha llegado (era ese pedido). Ni se pone fecha a los que quedan en el
                // aire (que el que quede sea uno solo no lo hace suyo) ni se avisa a Compras.
                return heredados;
            }
            CandidatoPedidoCompra unico = decision.Unico;
            if (unico != null)
            {
                if (!interruptor())
                {
                    return heredados; // inequívoco con el interruptor apagado: ni se escribe ni se avisa (queda en la lista por asignar)
                }
                DateTime fecha = ReglaFechaRecepcionPorCorreo.Calcular(decision.Correos, momento.Date);
                foreach (CorreoSeguimientoFila fila in pendientes)
                {
                    ResultadoOperacionCorreoCompra resultado = await Aplicar(fila, unico.Empresa, unico.Pedido, false, APLICADO_POR_AUTOMATICO,
                        USUARIO_LINEAS_AUTOMATICO, true, fecha).ConfigureAwait(false);
                    if (resultado.Codigo != CodigoOperacionCorreoCompra.Ok)
                    {
                        registrar(new Exception($"[Correos de seguimiento de compras #857] (informativo) No se ha aplicado el correo {fila.Id} al pedido {unico.Pedido}: {resultado.Mensaje}"));
                    }
                }
                return heredados;
            }
            return heredados; // por asignar: el aviso lo da AvisarPendientes, cuando el envío lleva un rato sin correos nuevos
        }

        /// <summary>Lo que se espera desde el último correo de un envío antes de avisar (sus otros correos suelen llegar a la vez).</summary>
        public static readonly TimeSpan ESPERA_AVISO = TimeSpan.FromMinutes(20);
        /// <summary>Solo se avisa de envíos con algún correo capturado en este plazo (al publicar, no se avisa de todo lo de antes).</summary>
        public static readonly TimeSpan CAPTURA_AVISO = TimeSpan.FromDays(1);

        /// <summary>
        /// El job, en cada pasada: UNA campana a Compras por envío que se queda por asignar (0 o varios candidatos, ninguno de sus correos
        /// aplicado ni descartado, y sin aviso aún), cuando su último correo tiene al menos <see cref="ESPERA_AVISO"/> (así los correos de un
        /// mismo envío que llegan seguidos —UPS y MBE— se juntan antes) y alguno es de los últimos 3 días. Nunca lanza.
        /// </summary>
        public async Task<int> AvisarPendientes()
        {
            int avisados = 0;
            try
            {
                if (avisador == null || !repositorio.AplicacionDisponible())
                {
                    return 0;
                }
                DateTime momento = ahora();
                List<CorreoSeguimientoFila> guardados = await repositorio.LeerCorreos(
                    momento.AddDays(-ReglaFechaRecepcionPorCorreo.DIAS_APLICACION_AUTOMATICA - MismoEnvio.DIAS)).ConfigureAwait(false)
                    ?? new List<CorreoSeguimientoFila>();
                foreach (List<CorreoSeguimientoFila> grupo in MismoEnvio.Agrupar(guardados))
                {
                    DecisionEnvio decision = MismoEnvio.Decidir(grupo);
                    if (decision.Avisado || decision.Aplicado != null || decision.Descartado || decision.YaLlego || decision.Unico != null
                        || !decision.Correos.Any(CorreosPorAsignar.Es)
                        || !decision.Correos.Any(f => ReglaFechaRecepcionPorCorreo.Reciente(f.Recibido, momento))
                        || !decision.Correos.Any(f => f.FechaCaptura >= momento - CAPTURA_AVISO)
                        || momento - decision.Correos.Max(f => f.Recibido) < ESPERA_AVISO)
                    {
                        continue;
                    }
                    CorreoSeguimientoFila ultimo = decision.Correos.OrderByDescending(f => f.Recibido).ThenByDescending(f => f.Id).First();
                    try
                    {
                        await avisador.Avisar(ultimo, decision.Candidatos.Count).ConfigureAwait(false);
                        await repositorio.MarcarAvisado(ultimo.Id).ConfigureAwait(false);
                        avisados++;
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception($"[Correos de seguimiento de compras #857] No se ha podido avisar a Compras del correo {ultimo.Id}: {ex.Message}", ex));
                    }
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido revisar los avisos por asignar: " + ex.Message, ex));
            }
            return avisados;
        }

        /// <summary>Los correos del envío de <paramref name="fila"/> (ella incluida), de lo guardado en su ventana.</summary>
        private async Task<List<CorreoSeguimientoFila>> EnvioDe(CorreoSeguimientoFila fila)
        {
            List<CorreoSeguimientoFila> guardados = await repositorio.LeerCorreos(fila.Recibido.AddDays(-MismoEnvio.DIAS)).ConfigureAwait(false)
                ?? new List<CorreoSeguimientoFila>();
            return MismoEnvio.Grupo(fila, guardados);
        }

        /// <summary>El núcleo común (job y asignación a mano): la fecha del correo en las líneas en el aire del pedido, en una transacción.</summary>
        internal async Task<ResultadoOperacionCorreoCompra> Aplicar(CorreoSeguimientoFila fila, string empresa, int pedido, bool reasignar,
            string aplicadoPor, string usuarioLineas, bool automatico, DateTime fecha, bool? eligioSugerido = null)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            var peticion = new PeticionAplicacionFecha
            {
                EligioSugerido = eligioSugerido,
                CorreoId = fila.Id,
                Empresa = empresa,
                Pedido = pedido,
                Fecha = fecha,
                Reasignar = reasignar,
                Automatico = automatico,
                AplicadoPor = aplicadoPor,
                UsuarioLineas = usuarioLineas,
                Agencia = fila.Agencia,
                Codigo = fila.Codigo
            };
            ResultadoAplicacionFecha r = await repositorio.AplicarFecha(peticion).ConfigureAwait(false)
                ?? new ResultadoAplicacionFecha { Estado = EstadoAplicacionFecha.NoExisteCorreo };
            var respuesta = new RespuestaAsignacionCorreoCompraDTO
            {
                CorreoId = fila.Id,
                Empresa = empresa,
                Pedido = pedido,
                Proveedor = r.Proveedor,
                LineasPendientes = r.LineasAntes?.Count ?? 0,
                FechaNueva = fecha,
                RemitentePaquete = fila.RemitentePaquete
            };
            switch (r.Estado)
            {
                case EstadoAplicacionFecha.NoExisteCorreo:
                    return Error(CodigoOperacionCorreoCompra.NoExiste, $"No existe el correo {fila.Id}.");
                case EstadoAplicacionFecha.NoEnElAire:
                    return Error(CodigoOperacionCorreoCompra.NoEnElAire,
                        $"El pedido de compra {pedido} no tiene líneas enviadas y sin recibir: no se le puede poner la fecha del correo.");
                case EstadoAplicacionFecha.AplicadoAOtro:
                    return new ResultadoOperacionCorreoCompra
                    {
                        Codigo = CodigoOperacionCorreoCompra.Conflicto,
                        Mensaje = $"Este correo ya está aplicado al pedido de compra {r.PedidoAnterior}. Si es de otro pedido, vuelva a asignarlo " +
                            "indicando que quiere reasignarlo (la fecha que se puso en el anterior no se deshace).",
                        Respuesta = respuesta
                    };
                case EstadoAplicacionFecha.YaAplicadoAlMismo:
                    respuesta.YaEstabaAplicado = true;
                    respuesta.Mensaje = $"Este correo ya estaba aplicado al pedido de compra {pedido}: no se ha cambiado nada.";
                    return Ok(respuesta);
            }

            List<LineaCompraCorreo> cambian = ReglaAplicacionCorreo.QueCambian(r.LineasAntes, fecha);
            respuesta.LineasCambiadas = r.LineasCambiadas;
            respuesta.FechasAnteriores = cambian.Select(l => l.FechaRecepcion?.Date).Distinct().OrderBy(f => f ?? DateTime.MinValue).ToList();
            if (reasignar && r.PedidoAnterior.HasValue)
            {
                respuesta.EmpresaAnterior = r.EmpresaAnterior;
                respuesta.PedidoAnterior = r.PedidoAnterior;
            }
            string dia = fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            respuesta.Mensaje = r.LineasCambiadas > 0
                ? $"Puesta la fecha de recepción {dia} en {r.LineasCambiadas} línea(s) del pedido de compra {pedido}."
                : $"Las líneas del pedido de compra {pedido} ya tenían la fecha {dia}: no se ha cambiado nada.";
            if (respuesta.PedidoAnterior.HasValue)
            {
                respuesta.Mensaje += $" Estaba aplicado al pedido {respuesta.PedidoAnterior}: su fecha no se ha deshecho.";
            }
            if (r.LineasCambiadas > 0)
            {
                AnotarPromesaRiesgo(empresa, pedido, r.LineasAntes, fecha);
            }
            return Ok(respuesta);
        }

        /// <summary>NestoAPI#693: si la nueva fecha retrasa líneas con las que contaban pedidos de venta, se recalculan (como el PUT de PedidosCompra).</summary>
        private void AnotarPromesaRiesgo(string empresa, int pedido, IEnumerable<LineaCompraCorreo> lineasAntes, DateTime fecha)
        {
            try
            {
                List<LineaCompraPromesaRiesgo> antes = (lineasAntes ?? Enumerable.Empty<LineaCompraCorreo>()).Select(l => new LineaCompraPromesaRiesgo
                {
                    Id = l.NumeroOrden, Producto = l.Producto, Estado = l.Estado, Enviado = l.Enviado, Cantidad = l.Cantidad, FechaRecepcion = l.FechaRecepcion
                }).ToList();
                List<LineaCompraPromesaRiesgo> despues = antes.Select(l => new LineaCompraPromesaRiesgo
                {
                    Id = l.Id, Producto = l.Producto, Estado = l.Estado, Enviado = l.Enviado, Cantidad = l.Cantidad, FechaRecepcion = fecha
                }).ToList();
                Dictionary<string, CausaPromesaRiesgo> empeoran = CambiosCompraPromesaRiesgo.QueEmpeora(pedido, antes, despues);
                if (empeoran.Any())
                {
                    anotarPromesaRiesgo(empresa, empeoran.Keys, $"pedido a proveedor {pedido}: fecha de un correo de seguimiento", empeoran);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Correos de seguimiento de compras #857] No se ha podido anotar el cambio del pedido {pedido} para la promesa en riesgo: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Compras dice de qué pedido es un correo: valida que esté en el aire y pone la fecha como el job (sin límite de días y con el
        /// interruptor apagado: lo pide una persona). Con <paramref name="reasignar"/> se puede cambiar de pedido (la del anterior no se deshace).
        /// </summary>
        public async Task<ResultadoOperacionCorreoCompra> Asignar(int id, PeticionAsignarCorreoCompraDTO peticion, bool reasignar, string usuario)
        {
            if (!Disponible)
            {
                return Error(CodigoOperacionCorreoCompra.SinTabla, MENSAJE_SIN_SCRIPT);
            }
            if (peticion == null || peticion.Pedido <= 0)
            {
                return Error(CodigoOperacionCorreoCompra.PeticionIncorrecta, "Falta el pedido de compra.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            if (empresa.Length > 3)
            {
                return Error(CodigoOperacionCorreoCompra.PeticionIncorrecta, "La empresa no es válida.");
            }
            CorreoSeguimientoFila fila = await repositorio.LeerCorreo(id).ConfigureAwait(false);
            if (fila == null)
            {
                return Error(CodigoOperacionCorreoCompra.NoExiste, $"No existe el correo {id}.");
            }
            // El envío entero: su fecha (la entrega más tardía de sus correos) y los demás correos, que van al mismo pedido
            List<CorreoSeguimientoFila> envio = await EnvioDe(fila).ConfigureAwait(false);
            DecisionEnvio decision = MismoEnvio.Decidir(envio);
            DateTime fecha = ReglaFechaRecepcionPorCorreo.Calcular(envio, ahora().Date);
            bool? eligioSugerido = await EligioSugerido(envio, decision, empresa, peticion.Pedido).ConfigureAwait(false);
            string auditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            ResultadoOperacionCorreoCompra resultado = await Aplicar(fila, empresa, peticion.Pedido, reasignar, usuario, auditoria, false, fecha,
                eligioSugerido).ConfigureAwait(false);
            if (resultado.Codigo != CodigoOperacionCorreoCompra.Ok)
            {
                return resultado;
            }
            RespuestaAsignacionCorreoCompraDTO respuesta = resultado.Respuesta;
            respuesta.EligioSugerido = eligioSugerido;
            foreach (CorreoSeguimientoFila otro in envio.Where(f => f.Id != fila.Id && !f.PedidoAplicado.HasValue && f.Descartado != true))
            {
                ResultadoOperacionCorreoCompra delEnvio = await Aplicar(otro, empresa, peticion.Pedido, false, usuario, auditoria, false, fecha,
                    eligioSugerido).ConfigureAwait(false);
                if (delEnvio.Codigo == CodigoOperacionCorreoCompra.Ok)
                {
                    respuesta.CorreosDelMismoEnvio++;
                    respuesta.LineasCambiadas += delEnvio.Respuesta?.LineasCambiadas ?? 0;
                }
            }
            if (respuesta.CorreosDelMismoEnvio > 0)
            {
                respuesta.Mensaje += $" También se han asignado {respuesta.CorreosDelMismoEnvio} correo(s) más del mismo envío.";
            }
            if (peticion.RecordarRemitente)
            {
                string remitente = ExtractorRemitentePaquete.Normalizar(fila.RemitentePaquete
                    ?? envio.Select(f => f.RemitentePaquete).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)));
                if (remitente == null || string.IsNullOrWhiteSpace(respuesta.Proveedor))
                {
                    respuesta.Mensaje += " No se ha podido recordar quién lo envía: el correo no lo dice.";
                }
                else
                {
                    _ = await repositorio.GuardarRemitentePaquete(remitente, empresa, respuesta.Proveedor.Trim(), usuario).ConfigureAwait(false);
                    respuesta.RemitenteRecordado = true;
                    respuesta.Mensaje += $" Desde ahora, lo que envíe «{remitente}» se propondrá para el proveedor {respuesta.Proveedor.Trim()}.";
                }
            }
            resultado.Mensaje = respuesta.Mensaje;
            return resultado;
        }

        /// <summary>
        /// Para medir la sugerencia: ¿el pedido elegido es el sugerido (<see cref="ReglaPedidoSugerido"/>) entre los candidatos del envío?
        /// Null si no había sugerido (o no se han podido leer los pedidos).
        /// </summary>
        private async Task<bool?> EligioSugerido(List<CorreoSeguimientoFila> envio, DecisionEnvio decision, string empresa, int pedido)
        {
            try
            {
                List<CandidatoPedidoCompra> candidatos = decision.Aplicado == null ? decision.Candidatos : new List<CandidatoPedidoCompra>();
                if (!candidatos.Any() || !envio.Any())
                {
                    return null;
                }
                List<PedidoCompraEnAire> enAire = await repositorio.LeerPedidosEnAire(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                PedidoCompraEnAire sugerido = ReglaPedidoSugerido.Sugerido(ReglaPedidoSugerido.DelEnvio(enAire, candidatos), envio.Min(f => f.Recibido));
                return sugerido == null ? (bool?)null
                    : AsignadorPedidosCompra.Clave(sugerido.Empresa, sugerido.Pedido) == AsignadorPedidosCompra.Clave(empresa, pedido);
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se ha podido calcular el pedido sugerido: " + ex.Message, ex));
                return null;
            }
        }

        /// <summary>
        /// Compras dice que el correo no es de ninguna compra (sale de la lista por asignar), y con él los demás de su envío que no estén
        /// aplicados. 409 si ya está aplicado.
        /// </summary>
        public async Task<ResultadoOperacionCorreoCompra> Descartar(int id, string usuario)
        {
            if (!Disponible)
            {
                return Error(CodigoOperacionCorreoCompra.SinTabla, MENSAJE_SIN_SCRIPT);
            }
            CorreoSeguimientoFila fila = await repositorio.Descartar(id, usuario).ConfigureAwait(false);
            if (fila == null)
            {
                return Error(CodigoOperacionCorreoCompra.NoExiste, $"No existe el correo {id}.");
            }
            if (fila.PedidoAplicado.HasValue)
            {
                return Error(CodigoOperacionCorreoCompra.Conflicto,
                    $"Este correo ya está aplicado al pedido de compra {fila.PedidoAplicado}: no se puede descartar.");
            }
            int delEnvio = 0;
            foreach (CorreoSeguimientoFila otro in (await EnvioDe(fila).ConfigureAwait(false))
                .Where(f => f.Id != fila.Id && !f.PedidoAplicado.HasValue && f.Descartado != true))
            {
                CorreoSeguimientoFila antes = await repositorio.Descartar(otro.Id, usuario).ConfigureAwait(false);
                if (antes != null && !antes.PedidoAplicado.HasValue && antes.Descartado != true)
                {
                    delEnvio++;
                }
            }
            string mensaje = fila.Descartado == true ? "Este correo ya estaba descartado." : "Descartado: no es de ninguna compra.";
            if (delEnvio > 0)
            {
                mensaje += $" También se han descartado {delEnvio} correo(s) más del mismo envío.";
            }
            return new ResultadoOperacionCorreoCompra { Codigo = CodigoOperacionCorreoCompra.Ok, Mensaje = mensaje, CorreosDelMismoEnvio = delEnvio };
        }
        private static ResultadoOperacionCorreoCompra Ok(RespuestaAsignacionCorreoCompraDTO respuesta) =>
            new ResultadoOperacionCorreoCompra { Codigo = CodigoOperacionCorreoCompra.Ok, Mensaje = respuesta.Mensaje, Respuesta = respuesta };

        private static ResultadoOperacionCorreoCompra Error(CodigoOperacionCorreoCompra codigo, string mensaje) =>
            new ResultadoOperacionCorreoCompra { Codigo = codigo, Mensaje = mensaje };
    }
}
