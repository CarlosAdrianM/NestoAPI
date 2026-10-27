using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>NestoAPI#698: lo que devuelven Retener y Liberar al controlador (que solo traduce a 404/400/200).</summary>
    public class ResultadoRetencion
    {
        public bool NoEncontrado { get; set; }
        public string Error { get; set; }
        public RetencionPedidoDTO Retencion { get; set; }

        public static ResultadoRetencion Fallo(string error) => new ResultadoRetencion { Error = error };
        public static ResultadoRetencion SinPedido() => new ResultadoRetencion { NoEncontrado = true };
        public static ResultadoRetencion Exito(RetencionPedidoDTO retencion) => new ResultadoRetencion { Retencion = retencion };
    }

    /// <summary>
    /// NestoAPI#698 (decisión de Carlos, 23/10/26): «Retener» pasa el pedido al modo de servicio 5 (<see cref="Constantes.Pedidos.ModosServicio.RETENIDO"/>),
    /// que nunca sale en el picking, y guarda en PedidosRetenidos el motivo, quién, hasta cuándo y el modo que tenía; «Liberar»
    /// cierra la fila y devuelve el pedido a su modo. La fecha de entrega de las líneas no se toca (se acaba con la costumbre
    /// de ponerla al año que viene). Lo pueden hacer el vendedor y el almacén. Al retener, el riesgo de promesa (#693) del
    /// pedido se resuelve con «Pedido retenido»: un retenido no es una promesa incumplida.
    /// </summary>
    public class ServicioRetencionPedidos
    {
        public const int LONGITUD_MAXIMA_MOTIVO = 500;
        public const string ERROR_SIN_TABLA = "Falta la tabla PedidosRetenidos (Scripts/Issue698_PedidosRetenidos.sql). Avise a Informática.";
        public const string ERROR_SIN_MOTIVO = "Indique el motivo por el que se retiene el pedido (quién lo pidió y por qué).";
        public const string ERROR_FECHA_PASADA = "La fecha de revisión tiene que ser posterior a hoy. Si quiere que salga ya, no lo retenga.";
        public const string ERROR_SIN_PENDIENTE = "El pedido no tiene nada pendiente de servir: no hay nada que retener.";
        public const string ERROR_NO_RETENIDO = "El pedido no está retenido.";
        public const string RESOLUCION_PROMESA = "Pedido retenido hasta nuevo aviso (#698)";
        public const string USUARIO_SISTEMA = "Sistema (retenidos)";

        private readonly IRepositorioPedidosRetenidos repositorio;
        private readonly IRepositorioPromesaRiesgo promesas;
        private readonly Func<DateTime> reloj;
        private readonly Action<Exception> registrar;

        public ServicioRetencionPedidos(IRepositorioPedidosRetenidos repositorio, IRepositorioPromesaRiesgo promesas = null,
            Func<DateTime> reloj = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.promesas = promesas;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (retenidos)"));
        }

        /// <summary>La retención viva del pedido, o null.</summary>
        public Task<RetencionPedidoDTO> Leer(string empresa, int numero) => repositorio.LeerActiva(Empresa(empresa), numero);

        /// <summary>Las vivas de la empresa, con cliente y vendedor.</summary>
        public Task<List<PedidoRetenidoListadoDTO>> Listado(string empresa) => repositorio.LeerListado(Empresa(empresa));

        public async Task<ResultadoRetencion> Retener(string empresa, int numero, RetenerPedidoRequest peticion, string usuario)
        {
            empresa = Empresa(empresa);
            if (!repositorio.TablaExiste())
            {
                return ResultadoRetencion.Fallo(ERROR_SIN_TABLA);
            }
            string motivo = peticion?.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo))
            {
                return ResultadoRetencion.Fallo(ERROR_SIN_MOTIVO);
            }
            if (motivo.Length > LONGITUD_MAXIMA_MOTIVO)
            {
                motivo = motivo.Substring(0, LONGITUD_MAXIMA_MOTIVO);
            }
            DateTime ahora = reloj();
            DateTime? hasta = peticion.HastaFecha?.Date;
            if (hasta.HasValue && hasta.Value <= ahora.Date)
            {
                return ResultadoRetencion.Fallo(ERROR_FECHA_PASADA);
            }

            CabeceraPedidoRetencion cabecera = await repositorio.LeerCabecera(empresa, numero).ConfigureAwait(false);
            if (cabecera == null)
            {
                return ResultadoRetencion.SinPedido();
            }
            if (!cabecera.QuedaPendiente)
            {
                return ResultadoRetencion.Fallo(ERROR_SIN_PENDIENTE);
            }
            RetencionPedidoDTO viva = await repositorio.LeerActiva(empresa, numero).ConfigureAwait(false);
            if (viva != null)
            {
                return ResultadoRetencion.Fallo($"El pedido ya está retenido: {TextosRetencion.Descripcion(viva)}. Libérelo antes de volver a retenerlo.");
            }

            var retencion = new RetencionPedidoDTO
            {
                Empresa = empresa,
                Pedido = numero,
                Motivo = motivo,
                HastaFecha = hasta,
                // Si alguien dejó el 5 sin fila (no debería), no se guarda 5 como «anterior»: al liberar volvería a quedarse retenido.
                ModoServicioAnterior = Constantes.Pedidos.ModosServicio.EsRetenido(cabecera.ModoServicio) ? null : cabecera.ModoServicio,
                Usuario = usuario?.Trim(),
                Fecha = ahora
            };
            retencion.Id = await repositorio.Insertar(retencion).ConfigureAwait(false);
            await repositorio.CambiarModoServicio(empresa, numero, Constantes.Pedidos.ModosServicio.RETENIDO, usuario,
                $"Pedido {numero} ModoServicio={Texto(cabecera.ModoServicio)}",
                $"ModoServicio={Constantes.Pedidos.ModosServicio.RETENIDO} (Retenido #698 {(hasta.HasValue ? "hasta el " + TextosRetencion.Fecha(hasta) : "hasta nuevo aviso")}: {motivo})")
                .ConfigureAwait(false);
            await ResolverPromesaRiesgo(empresa, numero, ahora).ConfigureAwait(false);
            return ResultadoRetencion.Exito(retencion);
        }

        public async Task<ResultadoRetencion> Liberar(string empresa, int numero, string usuario, string motivoLiberacion = TextosRetencion.LIBERADO_A_MANO)
        {
            empresa = Empresa(empresa);
            CabeceraPedidoRetencion cabecera = await repositorio.LeerCabecera(empresa, numero).ConfigureAwait(false);
            if (cabecera == null)
            {
                return ResultadoRetencion.SinPedido();
            }
            RetencionPedidoDTO viva = await repositorio.LeerActiva(empresa, numero).ConfigureAwait(false);
            bool modoRetenido = Constantes.Pedidos.ModosServicio.EsRetenido(cabecera.ModoServicio);
            if (viva == null && !modoRetenido)
            {
                return ResultadoRetencion.Fallo(ERROR_NO_RETENIDO);
            }
            DateTime ahora = reloj();
            byte? modoAnterior = viva?.ModoServicioAnterior;
            if (viva != null)
            {
                _ = await repositorio.Liberar(viva.Id, ahora, usuario?.Trim(), motivoLiberacion).ConfigureAwait(false);
                viva.Liberado = ahora;
                viva.UsuarioLiberacion = usuario?.Trim();
                viva.MotivoLiberacion = motivoLiberacion;
            }
            if (modoRetenido)
            {
                await repositorio.CambiarModoServicio(empresa, numero, modoAnterior, usuario,
                    $"Pedido {numero} ModoServicio={Constantes.Pedidos.ModosServicio.RETENIDO} (Retenido)",
                    $"ModoServicio={Texto(modoAnterior)} ({motivoLiberacion} #698)").ConfigureAwait(false);
            }
            return ResultadoRetencion.Exito(viva ?? new RetencionPedidoDTO
            {
                Empresa = empresa,
                Pedido = numero,
                Liberado = ahora,
                UsuarioLiberacion = usuario?.Trim(),
                MotivoLiberacion = motivoLiberacion
            });
        }

        /// <summary>#693: un retenido no es una promesa incumplida. Si falla, se registra y Retener sigue.</summary>
        private async Task ResolverPromesaRiesgo(string empresa, int numero, DateTime ahora)
        {
            if (promesas == null)
            {
                return;
            }
            try
            {
                if (!promesas.TablaExiste())
                {
                    return;
                }
                List<RiesgoPromesa> abiertos = await promesas.LeerSinResolver(empresa, new[] { numero }).ConfigureAwait(false) ?? new List<RiesgoPromesa>();
                foreach (RiesgoPromesa riesgo in abiertos)
                {
                    await promesas.Resolver(riesgo.Id, ahora, RESOLUCION_PROMESA).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Retenidos #698] No se ha podido resolver la promesa en riesgo del pedido {numero}: {ex.Message}", ex));
            }
        }

        private static string Texto(byte? modo) => modo.HasValue ? modo.Value.ToString() : "NULL";

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
