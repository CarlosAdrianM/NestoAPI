using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#873: lo que lleva «pedidoCambiado» (los mismos nombres en Nesto).</summary>
    public class PedidoCambiadoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Quien lo ha guardado, sin dominio («Aida»).</summary>
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>La versión del pedido tras guardarlo (<see cref="VersionPedidoVenta"/>). Null si no se ha podido calcular.</summary>
        public string Version { get; set; }
        /// <summary>Carlos 26/11/26 (aditivo): los pickings de las líneas (<see cref="VersionPedidoVenta.Pickings"/>). Null si no se ha podido leer.</summary>
        public System.Collections.Generic.List<int> Pickings { get; set; }
        /// <summary>Carlos 26/11/26 (aditivo): <see cref="VersionPedidoVenta.HuellaContenido"/>: si no cambia, no ha cambiado nada que edite un usuario.</summary>
        public string HuellaContenido { get; set; }
        /// <summary>Carlos 26/11/26 (aditivo): null en un guardado; «PickingSacado» o «PickingQuitado» cuando lo que ha cambiado es el picking.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>NestoAPI#873: avisar por SignalR a quien tenga abierto el pedido de que alguien lo ha guardado. Nunca lanza.</summary>
    public interface IAvisosPedidoCambiado
    {
        void PedidoCambiado(PedidoCambiadoDTO aviso);
    }

    /// <summary>
    /// NestoAPI#873: el aviso «pedidoCambiado» SIEMPRE en segundo plano y sin romper nunca el guardado (el guardado ya está hecho;
    /// si el aviso no sale, el 409 del siguiente PUT cubre lo que no llegue).
    /// </summary>
    public static class AvisoPedidoCambiado
    {
        public const string MOTIVO_PICKING_SACADO = "PickingSacado";
        public const string MOTIVO_PICKING_QUITADO = "PickingQuitado";

        /// <summary>Para los tests: quien manda el aviso (por defecto, el hub del almacén, que es donde Nesto entra en el grupo).</summary>
        internal static Func<IAvisosPedidoCambiado> Avisos { get; set; } = () => new AvisosAlmacenSignalR();

        /// <summary>Para los tests: si alguien tiene el pedido abierto (el grupo «Pedido:{empresa}:{numero}» del hub del almacén).</summary>
        internal static Func<string, int, bool> HayAlguienConElPedido { get; set; } =
            (empresa, pedido) => ConexionesAlmacen.HayOtros(AvisosAlmacen.GrupoPedido(empresa, pedido), null);

        /// <summary>Para los tests: lee los datos de la versión con su propio contexto (en segundo plano).</summary>
        internal static Func<string, int, DatosVersionPedido> LeerDatos { get; set; } = (empresa, pedido) =>
        {
            using (var db = new NVEntities())
            {
                return new LectorVersionPedidoSql(db).Leer(empresa, pedido);
            }
        };

        /// <summary>Para los tests: cómo se lanza el trabajo en segundo plano.</summary>
        internal static Action<Action> EnOtroHilo { get; set; } = accion => _ = Task.Run(accion);

        /// <summary>
        /// Carlos 26/11/26: el picking de un pedido ha cambiado (sacado o quitado). Solo si alguien tiene el pedido abierto; en segundo
        /// plano lee la versión, los pickings y la huella del contenido y manda «pedidoCambiado» con <paramref name="motivo"/> y quien lo
        /// ha hecho. Nunca lanza.
        /// </summary>
        public static void PorPicking(string empresa, int pedido, string usuario, string motivo)
        {
            if (string.IsNullOrWhiteSpace(empresa) || pedido <= 0)
            {
                return;
            }
            try
            {
                if (!(HayAlguienConElPedido?.Invoke(empresa.Trim(), pedido) ?? false))
                {
                    return; // nadie lo tiene abierto: ni se lee nada
                }
                DateTime fecha = DateTime.Now;
                EnOtroHilo(() =>
                {
                    var aviso = new PedidoCambiadoDTO
                    {
                        Empresa = empresa.Trim(),
                        Pedido = pedido,
                        Usuario = VersionPedidoVenta.SinDominio(usuario),
                        Fecha = fecha,
                        Motivo = motivo
                    };
                    try
                    {
                        Completar(aviso, LeerDatos?.Invoke(aviso.Empresa, pedido));
                    }
                    catch (Exception ex)
                    {
                        Registrar(aviso, ex);
                    }
                    Mandar(aviso);
                });
            }
            catch (Exception ex)
            {
                Registrar(new PedidoCambiadoDTO { Empresa = empresa, Pedido = pedido }, ex);
            }
        }

        /// <summary>Version, Pickings y HuellaContenido de lo leído (si se leyó).</summary>
        internal static void Completar(PedidoCambiadoDTO aviso, DatosVersionPedido datos)
        {
            if (aviso == null || datos == null)
            {
                return;
            }
            aviso.Version = aviso.Version ?? VersionPedidoVenta.Calcular(datos);
            aviso.Pickings = VersionPedidoVenta.Pickings(datos);
            aviso.HuellaContenido = VersionPedidoVenta.HuellaContenido(datos);
        }

        /// <summary>Con la versión ya calculada (PUT y POST del pedido). Nunca lanza.</summary>
        public static void EnSegundoPlano(string empresa, int pedido, string usuario, string version)
            => EnSegundoPlano(empresa, pedido, usuario, version, null);

        /// <summary>Con la versión ya calculada y, si se tienen, los datos leídos (para Pickings y HuellaContenido). Nunca lanza.</summary>
        public static void EnSegundoPlano(string empresa, int pedido, string usuario, string version, DatosVersionPedido datos)
        {
            if (string.IsNullOrWhiteSpace(empresa) || pedido <= 0)
            {
                return;
            }
            var aviso = new PedidoCambiadoDTO
            {
                Empresa = empresa.Trim(),
                Pedido = pedido,
                Usuario = VersionPedidoVenta.SinDominio(usuario),
                Fecha = DateTime.Now,
                Version = version
            };
            try
            {
                Completar(aviso, datos);
                _ = Task.Run(() => Mandar(aviso));
            }
            catch (Exception ex)
            {
                Registrar(aviso, ex);
            }
        }

        /// <summary>
        /// Calculando la versión en segundo plano, con su propio contexto (los prepagos que se apuntan fuera del PUT: cobro de la
        /// app, pago a plazos). Nunca lanza.
        /// </summary>
        public static void CalcularYAvisarEnSegundoPlano(string empresa, int pedido, string usuario)
        {
            if (string.IsNullOrWhiteSpace(empresa) || pedido <= 0)
            {
                return;
            }
            DateTime fecha = DateTime.Now;
            try
            {
                _ = Task.Run(() =>
                {
                    var aviso = new PedidoCambiadoDTO
                    {
                        Empresa = empresa.Trim(),
                        Pedido = pedido,
                        Usuario = VersionPedidoVenta.SinDominio(usuario),
                        Fecha = fecha
                    };
                    try
                    {
                        using (var db = new NVEntities())
                        {
                            Completar(aviso, new LectorVersionPedidoSql(db).Leer(aviso.Empresa, pedido));
                        }
                    }
                    catch (Exception ex)
                    {
                        Registrar(aviso, ex); // sin versión el aviso sale igual: Nesto lo trata como de otro
                    }
                    Mandar(aviso);
                });
            }
            catch (Exception ex)
            {
                Registrar(new PedidoCambiadoDTO { Empresa = empresa, Pedido = pedido }, ex);
            }
        }

        private static void Mandar(PedidoCambiadoDTO aviso)
        {
            try
            {
                Avisos?.Invoke()?.PedidoCambiado(aviso);
            }
            catch (Exception ex)
            {
                Registrar(aviso, ex);
            }
        }

        private static void Registrar(PedidoCambiadoDTO aviso, Exception ex)
        {
            try
            {
                ElmahHelper.Log(new Exception($"[NestoAPI#873] No se ha podido avisar de que ha cambiado el pedido {aviso?.Pedido}: {ex.Message}", ex));
            }
            catch
            {
                // nada
            }
        }
    }
}
