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
        /// <summary>Para los tests: quien manda el aviso (por defecto, el hub del almacén, que es donde Nesto entra en el grupo).</summary>
        internal static Func<IAvisosPedidoCambiado> Avisos { get; set; } = () => new AvisosAlmacenSignalR();

        /// <summary>Con la versión ya calculada (PUT y POST del pedido). Nunca lanza.</summary>
        public static void EnSegundoPlano(string empresa, int pedido, string usuario, string version)
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
                            aviso.Version = VersionPedidoVenta.Calcular(new LectorVersionPedidoSql(db).Leer(aviso.Empresa, pedido));
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
