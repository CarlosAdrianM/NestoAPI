using System.Data.Common;
using System.Data.Entity;
using System.Linq;

namespace NestoAPI.Models
{
    /// <summary>
    /// Clase parcial de NVEntities para agregar constructores personalizados
    /// </summary>
    public partial class NVEntities : DbContext
    {
        /// <summary>
        /// Constructor que acepta una conexión existente.
        /// Útil para compartir una SqlConnection entre múltiples DbContext en una transacción.
        /// </summary>
        /// <param name="existingConnection">Conexión SQL existente</param>
        /// <param name="contextOwnsConnection">
        /// Si es false, el DbContext NO cerrará la conexión al hacer Dispose.
        /// Usar false cuando la conexión es compartida y gestionada externamente.
        /// </param>
        public NVEntities(DbConnection existingConnection, bool contextOwnsConnection)
            : base(existingConnection, contextOwnsConnection)
        {
        }

        // Issue #275: el patrón 'contador.Pedidos++' + SaveChanges NO es atómico: dos peticiones
        // concurrentes leen el mismo valor, el UPDATE del contador no detecta el conflicto
        // (last-write-wins, sin token de concurrencia) y ambas insertan el mismo Número de pedido
        // → violación de PK_CabPedidoVta y pedido perdido.
        internal const string SQL_SIGUIENTE_NUMERO_PEDIDO =
            "UPDATE ContadoresGlobales SET Pedidos = Pedidos + 1 OUTPUT INSERTED.Pedidos;";

        /// <summary>
        /// Reserva de forma ATÓMICA el siguiente número de pedido (el UPDATE toma bloqueo exclusivo
        /// de la fila, así que dos peticiones concurrentes reciben números distintos). El número se
        /// consume aunque el guardado posterior falle: los huecos en la numeración de pedidos son
        /// aceptables (borrar un pedido ya los produce). Virtual para poder fakearlo en tests.
        /// </summary>
        public virtual int TomarSiguienteNumeroPedido()
        {
            return Database.SqlQuery<int>(SQL_SIGUIENTE_NUMERO_PEDIDO).Single();
        }
    }
}
