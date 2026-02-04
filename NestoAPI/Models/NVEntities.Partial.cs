using System.Data.Common;
using System.Data.Entity;

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
    }
}
