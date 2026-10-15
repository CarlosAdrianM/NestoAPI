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

        // NestoAPI#636: cada pedido rechazado (validación, error al guardar) se comía un número y dejaba
        // un hueco (14/10: Manuel, cuatro rechazos → 928500..928508 solo pares). Devolverlo es atómico y
        // seguro: solo baja si el contador sigue en ESE número (nadie ha reservado otro después) y no hay
        // ningún pedido con él. Si otro ya cogió el siguiente, el hueco queda (como antes), sin riesgo.
        internal const string SQL_DEVOLVER_NUMERO_PEDIDO =
            "UPDATE ContadoresGlobales SET Pedidos = Pedidos - 1 WHERE Pedidos = @numero " +
            "AND NOT EXISTS (SELECT 1 FROM CabPedidoVta WHERE Número = @numero);";

        /// <summary>
        /// Devuelve al contador un número reservado con <see cref="TomarSiguienteNumeroPedido"/> cuyo
        /// pedido no se ha llegado a guardar. True si se ha devuelto; false si ya no era el último.
        /// Virtual para poder fakearlo en tests.
        /// </summary>
        public virtual bool DevolverNumeroPedido(int numero)
        {
            return Database.ExecuteSqlCommand(SQL_DEVOLVER_NUMERO_PEDIDO,
                new System.Data.SqlClient.SqlParameter("@numero", numero)) > 0;
        }
    }
}
