using Hangfire;
using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    /// <summary>
    /// NestoAPI#864: el trabajo de Hangfire del pago a plazos de los pedidos (el gemelo neutro de «aplazame-capturas» de #663,
    /// que sigue llevando los cobros del carrito de la app). Cada hora en horario laboral, por cada solicitud viva:
    /// <list type="bullet">
    /// <item><description>aprobada y con el pedido entero en albarán o factura → confirma el cobro al proveedor (captura);</description></item>
    /// <item><description>aprobada, sin salir y a pocos días del límite → aviso «quedan N días» (una vez);</description></item>
    /// <item><description>aprobada y con el plazo vencido → se cancela en el proveedor y se quita el prepago (o, si ya había
    /// salido una parte, se cobra lo servido);</description></item>
    /// <item><description>el pedido ya no existe → se cancela; pendiente que el cliente no termina → caduca.</description></item>
    /// </list>
    /// Sin el script (sin tabla) no hace nada.
    /// </summary>
    public static class PagoAPlazosJobsService
    {
        public const string ID_JOB = "pago-a-plazos-pedidos";

        /// <summary>De lunes a sábado, cada hora de 8:35 a 20:35.</summary>
        public const string CRON = "35 8-20 * * 1-6";

        // Sin reintentos: si falla, dentro de una hora vuelve a pasar. Y nunca dos a la vez, que capturarían dos veces.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task ProcesarPlazos()
        {
            try
            {
                _ = await ServicioPagoAPlazos.CrearPorDefecto().ProcesarPlazos(DateTime.Now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Pago a plazos #864] Job {ID_JOB}: {ex.Message}", ex));
            }
        }
    }

    /// <summary>
    /// NestoAPI#864: quién puede ofrecer, pedir, consultar y cancelar el pago a plazos de un pedido. Lo mismo que los enlaces
    /// de pago (cualquier usuario autenticado de Nesto o NestoApp), con dos cortes: un cliente de la tienda nunca, y un
    /// vendedor de NestoApp solo con los pedidos de sus clientes.
    /// </summary>
    public static class PermisosPagoAPlazos
    {
        /// <summary>Empleado o vendedor; un cliente de la tienda (claim «cliente»), no.</summary>
        public static bool EsDeLaCasa(IPrincipal usuario)
        {
            ClaimsIdentity identidad = usuario?.Identity as ClaimsIdentity;
            return identidad != null && identidad.IsAuthenticated && string.IsNullOrWhiteSpace(identidad.FindFirst("cliente")?.Value);
        }

        /// <summary>¿Es un vendedor de NestoApp (al que hay que mirarle si el cliente es suyo)?</summary>
        public static bool EsVendedor(IPrincipal usuario)
            => Seguridad.ClientesDelVendedor.EsVendedorDeNestoApp(usuario?.Identity as ClaimsIdentity);

        /// <summary>
        /// <see cref="EsDeLaCasa"/> y, si es un vendedor de NestoApp, que el cliente del pedido sea suyo (empresa, usuario,
        /// vendedor, cliente → <paramref name="esDelVendedor"/>).
        /// </summary>
        public static bool PuedeConElCliente(IPrincipal usuario, string empresa, string cliente, Func<string, string, string, string, bool> esDelVendedor)
        {
            if (!EsDeLaCasa(usuario))
            {
                return false;
            }
            if (!EsVendedor(usuario))
            {
                return true;
            }
            string emp = string.IsNullOrWhiteSpace(empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return !string.IsNullOrWhiteSpace(cliente)
                && Seguridad.ClientesDelVendedor.PuedeElVendedor((ClaimsIdentity)usuario.Identity, emp, cliente.Trim(), esDelVendedor);
        }
    }
}
