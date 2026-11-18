using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Caso 931007: los textos (de usted) y los datos de las notificaciones de la ampliación de un pedido en preparación.</summary>
    public static class TextosAmpliacionPedido
    {
        /// <summary>NotificacionPushDTO.Tipo y Datos["tipo"].</summary>
        public const string TIPO_NOTIFICACION = "AmpliacionPedidoEnPreparacion";
        /// <summary>Datos["accion"]: la pregunta a Almacén (la única URGENTE, #775).</summary>
        public const string ACCION_PREGUNTA = "pregunta";
        /// <summary>Datos["accion"]: la respuesta a quien lo pidió.</summary>
        public const string ACCION_RESPUESTA = "respuesta";
        /// <summary>Datos["accion"]: un aviso normal a Almacén (lo añadido a un pedido que hoy no sale, o lo que no se ha podido mover).</summary>
        public const string ACCION_AVISO = "aviso";

        /// <summary>Resultado de «notificacionResuelta».</summary>
        public const string RESULTADO_ENTRA = "Entra";
        public const string RESULTADO_NO_ENTRA = "NoEntra";
        public const string RESULTADO_SIN_RESPUESTA = "SinRespuesta";

        public const string ERROR_SIN_TABLA = "Falta la tabla AmpliacionesPedidoEnPreparacion (Scripts/AmpliacionesPedidoEnPreparacion.sql): no se puede pedir a " +
            "Almacén que lo meta en el envío de hoy. Haga un pedido nuevo con lo que quiera añadir, o avise a Informática.";
        public const string ERROR_OPCION_NO_VALIDA = "AmpliacionEnPreparacion tiene que ser «PedidoNuevo» o «PedirAlmacen».";
        public const string ERROR_SIN_PERMISO = "Solo Almacén, Dirección o Informática pueden contestar si lo añadido entra en el envío de hoy.";
        public const string ERROR_NO_ENCONTRADA = "No existe esa solicitud.";

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static string Nombre(string usuario) => TextosRetencion.NombreUsuario(usuario);

        /// <summary>«del cliente 15191 (Peluquería Lucía)».</summary>
        public static string Cliente(string cliente, string nombre)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return string.Empty;
            }
            return string.IsNullOrWhiteSpace(nombre) ? $" del cliente {cliente.Trim()}" : $" del cliente {cliente.Trim()} ({nombre.Trim()})";
        }

        public static string Pickings(AmpliacionPedidoDTO s) => AmpliacionPedidoEnPreparacion.TextoPickings(s?.NumerosPicking());

        private static string Espera => ((int)AmpliacionPedidoEnPreparacion.ESPERA_RESPUESTA_ALMACEN.TotalMinutes).ToString(castellano);

        /// <summary>La pregunta URGENTE a Almacén (ventana roja con pitido en Nesto).</summary>
        public static NotificacionPushDTO Pregunta(AmpliacionPedidoDTO s, string cliente, string nombre)
        {
            NotificacionPushDTO n = Componer(s, ACCION_PREGUNTA, $"Pedido {s.Pedido}: ¿entra en el envío de hoy?",
                $"{Nombre(s.Usuario)} ha añadido {s.Productos} al pedido {s.Pedido}{Cliente(cliente, nombre)}, que ya está en preparación " +
                $"({Pickings(s)}). ¿Entra en el envío de hoy? «Sí, lo metemos» saca el picking de lo añadido para que vaya con el envío en curso; " +
                $"«No llega» lo pasa a un pedido nuevo con sus propios portes. Si nadie contesta en {Espera} minutos, irá en un pedido nuevo.");
            n.Datos[NotificacionesUrgentes.DATO_REFERENCIA] = Referencia(s.Id);
            Poner(n.Datos, "cliente", cliente);
            Poner(n.Datos, "nombre", nombre);
            return n;
        }

        /// <summary>A quien lo pidió: la respuesta (o lo que ha pasado sin ella).</summary>
        public static NotificacionPushDTO Respuesta(AmpliacionPedidoDTO s, string estado, int? picking, int? pedidoNuevo, string motivo)
        {
            string quien = Nombre(s.UsuarioRespuesta);
            string titulo;
            string cuerpo;
            if (estado == EstadosAmpliacionPedido.ENTRA && picking.HasValue)
            {
                titulo = $"Pedido {s.Pedido}: lo añadido entra en el envío de hoy";
                cuerpo = $"Almacén ({quien}) mete {s.Productos} en el envío de hoy del pedido {s.Pedido} (picking {picking.Value}).";
            }
            else if (pedidoNuevo.HasValue)
            {
                string porQue = estado == EstadosAmpliacionPedido.SIN_RESPUESTA
                    ? $"Almacén no ha contestado en {Espera} minutos"
                    : $"Almacén ({quien}) dice que no llega al envío de hoy";
                titulo = $"Pedido {s.Pedido}: lo añadido va en el pedido {pedidoNuevo.Value}";
                cuerpo = $"{porQue}: {s.Productos} va en el pedido nuevo {pedidoNuevo.Value}, con sus propios portes." + Motivo(motivo);
            }
            else
            {
                titulo = $"Pedido {s.Pedido}: revise lo añadido";
                cuerpo = $"No se ha podido pasar {s.Productos} a un pedido nuevo." + Motivo(motivo) + $" Sigue en el pedido {s.Pedido}: revíselo.";
            }
            NotificacionPushDTO n = Componer(s, ACCION_RESPUESTA, titulo, cuerpo);
            n.Datos["estado"] = estado;
            if (pedidoNuevo.HasValue)
            {
                n.Datos["pedidoNuevo"] = pedidoNuevo.Value.ToString(CultureInfo.InvariantCulture);
            }
            if (picking.HasValue)
            {
                n.Datos["picking"] = picking.Value.ToString(CultureInfo.InvariantCulture);
            }
            return n;
        }

        /// <summary>Campana normal a Almacén cuando lo añadido no se ha podido pasar al pedido nuevo.</summary>
        public static NotificacionPushDTO SinMover(AmpliacionPedidoDTO s, string motivo)
            => Componer(s, ACCION_AVISO, $"Pedido {s.Pedido}: lo añadido sigue en el pedido",
                $"Lo añadido por {Nombre(s.Usuario)} al pedido {s.Pedido} ({s.Productos}) no ha entrado en el envío de hoy y no se ha podido pasar a un " +
                $"pedido nuevo.{Motivo(motivo)} Sigue en el pedido {s.Pedido}, sin picking.");

        /// <summary>Campana normal a Almacén: se ha añadido algo a un pedido con picking que hoy no sale (retenido, aplazado…).</summary>
        public static NotificacionPushDTO AnadidoAPedidoQueNoSale(string empresa, int pedido, string cliente, string nombre, string usuario,
            IEnumerable<int> pickings, string productos, string motivoNoSale)
        {
            var s = new AmpliacionPedidoDTO { Empresa = empresa, Pedido = pedido, Usuario = usuario, Productos = productos };
            string lista = AmpliacionPedidoEnPreparacion.TextoPickings(pickings?.ToList());
            return Componer(s, ACCION_AVISO, $"Pedido {pedido}: añadido con el picking hecho",
                $"{Nombre(usuario)} ha añadido {productos} al pedido {pedido}{Cliente(cliente, nombre)}, que tiene {lista} pero hoy no sale " +
                $"({motivoNoSale?.Trim().TrimEnd('.')}). Se ha guardado en el mismo pedido: cuando salga, que vaya todo junto.");
        }

        /// <summary>«notificacionResuelta»: se cierra la ventana en todos los Nesto de Almacén.</summary>
        public static NotificacionResueltaDTO Resuelta(AmpliacionPedidoDTO s, string estado) => new NotificacionResueltaDTO
        {
            Tipo = TIPO_NOTIFICACION,
            Referencia = Referencia(s.Id),
            Respondio = estado == EstadosAmpliacionPedido.SIN_RESPUESTA ? null : Nombre(s.UsuarioRespuesta),
            Resultado = estado == EstadosAmpliacionPedido.ENTRA ? RESULTADO_ENTRA
                : estado == EstadosAmpliacionPedido.SIN_RESPUESTA ? RESULTADO_SIN_RESPUESTA
                : RESULTADO_NO_ENTRA
        };

        public static string Referencia(int id) => id.ToString(CultureInfo.InvariantCulture);

        private static string Motivo(string motivo) => string.IsNullOrWhiteSpace(motivo) ? string.Empty : $" Motivo: {motivo.Trim().TrimEnd('.')}.";

        /// <summary>Las push solo admiten textos: nada de valores nulos en Datos.</summary>
        private static void Poner(Dictionary<string, string> datos, string clave, string valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                datos[clave] = valor.Trim();
            }
        }

        private static NotificacionPushDTO Componer(AmpliacionPedidoDTO s, string accion, string titulo, string cuerpo)
        {
            var datos = new Dictionary<string, string>
            {
                [NotificacionesUrgentes.DATO_TIPO] = TIPO_NOTIFICACION,
                [NotificacionesUrgentes.DATO_ACCION] = accion,
                ["empresa"] = string.IsNullOrWhiteSpace(s.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : s.Empresa.Trim(),
                ["pedido"] = s.Pedido.ToString(CultureInfo.InvariantCulture),
                ["usuario"] = Nombre(s.Usuario)
            };
            if (s.Id > 0)
            {
                datos["id"] = s.Id.ToString(CultureInfo.InvariantCulture);
            }
            Poner(datos, "productos", s.Productos);
            return new NotificacionPushDTO { Titulo = titulo, Cuerpo = cuerpo, Tipo = TIPO_NOTIFICACION, Datos = datos };
        }
    }

    /// <summary>Caso 931007: a quién se avisa. Sustituible en los tests.</summary>
    public interface IAvisosAmpliacionPedido
    {
        /// <summary>La pregunta urgente a los Nesto del grupo Almacén. Devuelve a cuántos ha avisado.</summary>
        Task<int> Preguntar(NotificacionPushDTO pregunta);
        /// <summary>Campana normal al grupo Almacén (Nesto y Ariadna).</summary>
        Task AvisarAlmacen(NotificacionPushDTO aviso);
        /// <summary>A quien lo pidió: campana de Nesto (si lo pidió desde Nesto) y push de NestoApp.</summary>
        Task AvisarA(string usuario, NotificacionPushDTO aviso);
        /// <summary>«notificacionResuelta» a los Nesto de Almacén. Nunca lanza.</summary>
        void Resuelta(NotificacionResueltaDTO resuelta);
    }

    /// <summary>
    /// Caso 931007: los avisos van al buzón con los mecanismos de siempre. La pregunta es URGENTE por su tipo y acción
    /// (<see cref="NotificacionesUrgentes"/>): GuardarEnBuzonDeUsuario manda «hayNotificacionUrgente» y Nesto abre la ventana roja.
    /// </summary>
    public class AvisosAmpliacionPedido : IAvisosAmpliacionPedido
    {
        private const string DOMINIO = "NUEVAVISION\\";
        private readonly Lazy<IServicioNotificacionesPush> notificacionesPerezosas;
        private readonly Func<List<string>> usuariosDeAlmacen;
        private readonly Lazy<IAvisosTiempoReal> tiempoRealPerezoso;

        public AvisosAmpliacionPedido() : this(null, null, null)
        {
        }

        /// <summary>Lo de producción se crea al usarlo (el PUT lo construye siempre y casi nunca avisa).</summary>
        internal AvisosAmpliacionPedido(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeAlmacen, IAvisosTiempoReal tiempoReal)
        {
            notificacionesPerezosas = new Lazy<IServicioNotificacionesPush>(() => notificaciones ?? new ServicioNotificacionesPush());
            this.usuariosDeAlmacen = usuariosDeAlmacen ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN));
            tiempoRealPerezoso = new Lazy<IAvisosTiempoReal>(() => tiempoReal ?? new AvisosTiempoReal());
        }

        private IServicioNotificacionesPush notificaciones => notificacionesPerezosas.Value;

        private IAvisosTiempoReal tiempoReal => tiempoRealPerezoso.Value;

        /// <summary>El grupo Almacén, limpio y sin repetidos (con dominio).</summary>
        internal List<string> Destinatarios()
        {
            return (usuariosDeAlmacen() ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<int> Preguntar(NotificacionPushDTO pregunta)
        {
            if (pregunta == null)
            {
                return 0;
            }
            List<string> usuarios = Destinatarios();
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, pregunta).ConfigureAwait(false);
            }
            return usuarios.Count;
        }

        public Task AvisarAlmacen(NotificacionPushDTO aviso) => aviso == null ? Task.CompletedTask : new AvisadorRetencionAlmacen().Avisar(aviso);

        public async Task AvisarA(string usuario, NotificacionPushDTO aviso)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio) || aviso == null)
            {
                return;
            }
            string sinDominio = limpio.Substring(limpio.LastIndexOf('\\') + 1);
            if (limpio.Contains("\\"))
            {
                await notificaciones.GuardarEnBuzonDeUsuario(limpio, Constantes.Aplicaciones.NESTO, aviso).ConfigureAwait(false);
            }
            _ = await notificaciones.EnviarAUsuario(sinDominio, Constantes.Aplicaciones.NESTO_APP, aviso).ConfigureAwait(false);
        }

        public void Resuelta(NotificacionResueltaDTO resuelta)
        {
            try
            {
                tiempoReal.NotificacionResuelta(Destinatarios(), resuelta);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ampliación en preparación, caso 931007] No se ha podido avisar a los Nesto de que la solicitud " +
                    $"{resuelta?.Referencia} está contestada: {ex.Message}", ex));
            }
        }
    }

    /// <summary>Caso 931007: GET api/Notificaciones/Urgentes: las solicitudes a Almacén que siguen sin contestar.</summary>
    public class ComprobadorAmpliacionesPendientes : IComprobadorUrgentesPendientes
    {
        private readonly IRepositorioAmpliacionesPedido repositorio;

        public ComprobadorAmpliacionesPendientes(IRepositorioAmpliacionesPedido repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public string Tipo => TextosAmpliacionPedido.TIPO_NOTIFICACION;

        public async Task<ISet<string>> Pendientes(IReadOnlyCollection<string> referencias)
        {
            var pendientes = new HashSet<string>(StringComparer.Ordinal);
            if (referencias == null || referencias.Count == 0 || !repositorio.TablaExiste())
            {
                return pendientes;
            }
            foreach (string referencia in referencias.Distinct())
            {
                if (int.TryParse(referencia, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    && (await repositorio.Leer(id).ConfigureAwait(false))?.Estado == EstadosAmpliacionPedido.PENDIENTE)
                {
                    _ = pendientes.Add(referencia);
                }
            }
            return pendientes;
        }
    }
}
