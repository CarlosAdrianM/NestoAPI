using Elmah;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Notificaciones;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// TNV#66: job de Hangfire que avisa al cliente por push cuando su pedido cambia de estado.
    ///
    /// <para>El estado lo calcula <see cref="ResumidorPedidosCliente"/>, el mismo que pinta la
    /// pantalla «Mis pedidos»: el aviso y lo que ve al abrir la app no pueden contradecirse. Qué
    /// se avisa y con qué texto está en <see cref="AvisosEstadoPedido"/>.</para>
    ///
    /// <para>Solo se miran los clientes que tienen la app instalada (los que tienen dispositivo
    /// registrado): para el resto no habría a dónde mandar nada, y recorrer todos los pedidos de
    /// la empresa cada media hora para no avisar a nadie es tirar consultas.</para>
    /// </summary>
    public class AvisosPedidosJobsService
    {
        /// <summary>
        /// Ventana de pedidos que se vigila. Más corta que la de la pantalla (60 días) a
        /// propósito: aquí solo interesan los que todavía se pueden mover.
        /// </summary>
        internal const int DIAS_VIGILADOS = 15;

        /// <summary>
        /// Clave de Web.config que enciende los avisos. Nace apagada: los avisos remiten a la
        /// pantalla «Mis pedidos», que hasta que la app no esté publicada EN PRODUCCIÓN no existe
        /// en el móvil de nadie. Se enciende el día que se publique.
        /// </summary>
        internal const string CLAVE_ACTIVO = "AvisosPedidos:Activo";

        /// <summary>
        /// ¿Están encendidos los avisos? Apagados si la clave falta o no dice true: encender algo
        /// que le escribe a 45 clientes tiene que ser una decisión explícita, no un descuido.
        /// </summary>
        internal static bool EstaActivo(string valorConfigurado)
        {
            return string.Equals(valorConfigurado?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Job recurrente. Lo llama Hangfire; no lanza nunca: un fallo avisando no puede tumbar la
        /// cadena de jobs, y lo que pase se ve en ELMAH.
        /// </summary>
        public static async Task AvisarCambiosDeEstado()
        {
            if (!EstaActivo(ConfigurationManager.AppSettings[CLAVE_ACTIVO]))
            {
                // Apagado: ni se abre la conexión. Así el despliegue puede ir por delante de la
                // publicación de la app, y la tabla de control puede crearse más tarde.
                return;
            }

            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;

                    await AvisarCambiosDeEstado(
                        db,
                        new ServicioPedidosCliente(db),
                        new AlmacenEstadoNotificadoPedido(db),
                        new ServicioNotificacionesPush(),
                        DateTime.Now).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.GetDefault(null)?.Log(new Error(
                    new Exception($"[AvisosPedidos] La pasada ha fallado: {ex.Message}", ex)));
            }
        }

        /// <summary>
        /// El cuerpo del job, con todo inyectado para poder probarlo. Devuelve cuántos avisos se
        /// han mandado.
        /// </summary>
        internal static async Task<int> AvisarCambiosDeEstado(
            NVEntities db,
            IServicioPedidosCliente servicioPedidos,
            IAlmacenEstadoNotificadoPedido almacen,
            IServicioNotificacionesPush push,
            DateTime ahora)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;

            List<string> clientes = await ClientesConLaAppInstalada(db, empresa).ConfigureAwait(false);
            if (clientes.Count == 0)
            {
                return 0;
            }

            int avisados = 0;
            foreach (string cliente in clientes)
            {
                try
                {
                    avisados += await AvisarCambiosDeUnCliente(
                        servicioPedidos, almacen, push, empresa, cliente, ahora).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Un cliente que falla no puede dejar sin avisar a los demás.
                    ErrorLog.GetDefault(null)?.Log(new Error(
                        new Exception($"[AvisosPedidos] Cliente {cliente}: {ex.Message}", ex)));
                }
            }

            return avisados;
        }

        private static async Task<int> AvisarCambiosDeUnCliente(
            IServicioPedidosCliente servicioPedidos,
            IAlmacenEstadoNotificadoPedido almacen,
            IServicioNotificacionesPush push,
            string empresa,
            string cliente,
            DateTime ahora)
        {
            List<PedidoClienteResumenDTO> pedidos = await servicioPedidos
                .LeerPedidosRecientes(empresa, cliente, DIAS_VIGILADOS).ConfigureAwait(false);

            if (pedidos.Count == 0)
            {
                return 0;
            }

            Dictionary<int, EstadoNotificadoPedido> registros =
                almacen.Obtener(empresa, pedidos.Select(p => p.Numero).ToList());

            int avisados = 0;
            foreach (PedidoClienteResumenDTO pedido in pedidos)
            {
                registros.TryGetValue(pedido.Numero, out EstadoNotificadoPedido registro);

                bool avisar = AvisosEstadoPedido.HayQueAvisar(pedido.Estado, registro, ahora);

                // El estado se registra SIEMPRE, se avise o no: es lo que convierte la primera
                // pasada en una línea base y lo que da la fecha "desde cuándo está así".
                almacen.RegistrarEstado(empresa, pedido.Numero, pedido.Estado.ToString(), ahora);

                if (!avisar)
                {
                    continue;
                }

                NotificacionPushDTO aviso = AvisosEstadoPedido.Construir(pedido);
                if (aviso == null)
                {
                    continue;
                }

                _ = await push.EnviarACliente(empresa, cliente, aviso).ConfigureAwait(false);

                // Se apunta aunque FCM no haya podido entregarlo: el buzón (#387) ya lo guardó, y
                // reintentarlo cada media hora sería insistir sin saber si molesta.
                almacen.RegistrarAviso(empresa, pedido.Numero, pedido.Estado.ToString(), ahora);
                avisados++;
            }

            return avisados;
        }

        /// <summary>
        /// Los clientes con algún dispositivo activo de la app de tiendas. A quien no la tiene no
        /// hay a dónde avisarle.
        /// </summary>
        private static async Task<List<string>> ClientesConLaAppInstalada(NVEntities db, string empresa)
        {
            List<string> clientes = await db.DispositivosNotificaciones
                .Where(d => d.Aplicacion == Constantes.Aplicaciones.NESTO_TIENDAS
                            && d.Activo
                            && d.Cliente != null)
                .Select(d => d.Cliente)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            return clientes
                .Select(c => c?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();
        }
    }
}
