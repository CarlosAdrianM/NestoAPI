using NestoAPI.Models;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Traspasos
{
    /// <summary>
    /// Servicio para traspasar pedidos entre empresas antes de facturar.
    /// Actualmente configurado para traspasar a empresa "3".
    /// </summary>
    public interface IServicioTraspasoEmpresa
    {
        /// <summary>
        /// Determina si un pedido debe ser traspasado a otra empresa antes de facturar.
        /// </summary>
        /// <param name="pedido">Pedido a evaluar</param>
        /// <returns>True si el pedido debe ser traspasado, false en caso contrario</returns>
        bool HayQueTraspasar(CabPedidoVta pedido);

        /// <summary>
        /// Traspasa un pedido de una empresa a otra.
        /// Copia todos los elementos necesarios: cliente, productos, cuentas contables.
        /// Usa procedimientos almacenados legacy: prdCopiarCliente, prdCopiarProducto, prdCopiarCuentaContable.
        /// </summary>
        /// <param name="pedido">Pedido a traspasar</param>
        /// <param name="empresaOrigen">Empresa de origen (ej: "1")</param>
        /// <param name="empresaDestino">Empresa de destino (ej: "3")</param>
        /// <param name="usuario">Usuario que ejecuta la operación (para leer parámetros de usuario)</param>
        /// <returns>Task</returns>
        /// <exception cref="System.ArgumentNullException">Si pedido es null</exception>
        /// <exception cref="System.ArgumentException">Si empresaOrigen o empresaDestino son null/vacías o iguales</exception>
        /// <exception cref="System.InvalidOperationException">Si el pedido no pertenece a empresaOrigen</exception>
        /// <exception cref="System.NotImplementedException">
        /// Actualmente no implementado. Pendiente de implementación futura.
        /// </exception>
        Task TraspasarPedidoAEmpresa(CabPedidoVta pedido, string empresaOrigen, string empresaDestino, string usuario);
    }
}
