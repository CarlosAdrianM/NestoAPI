using System.Data.Entity;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// #291: rollback que nunca enmascara el error real. Cuando un SP aborta por dentro (ROLLBACK
    /// interno + THROW), la transacción de EF queda zombi (conexión cerrada o transacción ya
    /// revertida) y transaction.Rollback() lanza su propia excepción ("failed on Rollback /
    /// connection null") ocultando la causa original — pasó en el incidente de la #287 y costó
    /// diagnóstico extra en producción. Si la transacción ya no está viva, el servidor ya ha
    /// revertido: intentar el rollback no aporta nada y la excepción que debe viajar es SIEMPRE
    /// la original del catch del llamante.
    /// </summary>
    public static class TransaccionesSeguras
    {
        public static void RollbackSeguro(this DbContextTransaction transaccion)
        {
            if (transaccion == null)
            {
                return;
            }
            try
            {
                // Transacción zombi: el DbTransaction subyacente pierde la conexión cuando el
                // servidor ya la ha abortado. En ese caso no hay nada que revertir.
                if (transaccion.UnderlyingTransaction?.Connection != null)
                {
                    transaccion.Rollback();
                }
            }
            catch
            {
                // El rollback ya lo hizo el servidor (o la conexión murió a la vez): tragarse
                // este fallo es deliberado para no pisar la excepción original del llamante.
            }
        }
    }
}
