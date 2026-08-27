using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Models
{
    public partial class NVEntities
    {
        /// <summary>
        /// Encola el producto en Nesto_sync para que la pasada de sincronización de los 5
        /// minutos lo publique (mismo mecanismo que el trigger y el job nocturno de stocks).
        /// Nesto_sync no está en el modelo EF, así que se inserta por SQL; el método es
        /// VIRTUAL para poder fakearlo en los tests de los controllers que encolan.
        /// No encola si ya hay una fila pendiente del mismo producto (la publicación siempre
        /// manda el estado ACTUAL, con una basta).
        /// </summary>
        public virtual Task<int> EncolarProductoSync(string producto, string usuario)
        {
            return Database.ExecuteSqlCommandAsync(@"
                INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
                SELECT 'Productos', @p0, @p1, GETDATE()
                WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                                  WHERE ns.Tabla = 'Productos'
                                    AND ns.ModificadoId = @p0
                                    AND ns.Sincronizado IS NULL)",
                new SqlParameter("@p0", producto),
                new SqlParameter("@p1", usuario ?? "NestoAPI"));
        }
    }
}
