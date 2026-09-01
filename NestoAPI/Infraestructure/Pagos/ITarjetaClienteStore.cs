using NestoAPI.Models.Pagos;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#178: acceso a las tarjetas guardadas de los clientes (tabla TarjetasClientes).
    /// </summary>
    public interface ITarjetaClienteStore
    {
        List<TarjetaCliente> ListarActivas(string empresa, string cliente);

        TarjetaCliente ObtenerPorId(int id);

        /// <summary>
        /// Guarda el token capturado en una notificación de Redsys. Deduplica por
        /// (Empresa, Cliente, TokenRedsys): si la tarjeta ya estaba, actualiza FechaUltimoUso,
        /// caducidad y CofTxnId y resetea los fallos consecutivos (y la reactiva si estaba
        /// desactivada por fallos, porque acaba de funcionar).
        /// </summary>
        void GuardarOActualizar(TarjetaCliente tarjeta);

        /// <summary>Desactivación lógica (auditoría): la fila no se borra nunca.</summary>
        void Desactivar(int id, string motivo);

        /// <summary>
        /// Apunta el resultado de un cobro con la tarjeta: bueno resetea los fallos consecutivos
        /// y actualiza FechaUltimoUso; fallido los incrementa.
        /// </summary>
        void RegistrarUso(int id, bool cobroAutorizado);
    }
}
