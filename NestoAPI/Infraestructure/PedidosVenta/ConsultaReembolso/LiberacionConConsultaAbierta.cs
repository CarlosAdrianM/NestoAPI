using NestoAPI.Models.PedidosVenta;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>
    /// NestoAPI#734 (pedido 929559, 04/11/26): treinta segundos después de consultar a Administración, alguien de Almacén liberó la
    /// retención a mano; la consulta siguió abierta y es la que bloquea la etiqueta, así que el pedido quedó «liberado» sin poder
    /// salir y sin que nadie lo supiera. Con una consulta abierta, el botón «Liberar» (#698) contesta 409 con este motivo; la
    /// retención la levanta la respuesta de Administración (<see cref="ServicioConsultasReembolso.Responder"/>).
    /// </summary>
    public static class LiberacionConConsultaAbierta
    {
        /// <summary>
        /// (empresa, pedido) → el mensaje para el usuario si hay una consulta abierta, o null. Si no se puede leer la consulta (o no
        /// está la tabla), se deja liberar: la comprobación nunca bloquea por un fallo suyo (va a ELMAH).
        /// </summary>
        public static Func<string, int, Task<string>> Comprobador(IRepositorioConsultasReembolso repositorio, Action<Exception> registrar = null)
        {
            if (repositorio == null)
            {
                throw new ArgumentNullException(nameof(repositorio));
            }
            Action<Exception> log = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (consulta reembolso)"));
            return async (empresa, pedido) =>
            {
                try
                {
                    ConsultaReembolsoDTO abierta = await repositorio.LeerAbierta(empresa, pedido).ConfigureAwait(false);
                    return abierta == null ? null : TextosConsultaReembolso.ErrorLiberarConConsultaAbierta(abierta);
                }
                catch (Exception ex)
                {
                    log(new Exception($"[Consulta reembolso #734] Pedido {pedido}: no se ha podido comprobar si tiene una consulta abierta " +
                        $"antes de liberarlo a mano (se libera): {ex.Message}", ex));
                    return null;
                }
            };
        }
    }
}
