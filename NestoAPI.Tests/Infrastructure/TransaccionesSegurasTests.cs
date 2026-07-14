using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System.Data.Entity;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// #291: RollbackSeguro nunca debe lanzar (su razón de ser es que la excepción que viaje sea
    /// SIEMPRE la original del catch del llamante). El caso zombi real (SP con ROLLBACK interno)
    /// no se puede reproducir sin BD: queda cubierto por el guard de UnderlyingTransaction y el
    /// catch envolvente, y verificado en el incidente de la #287.
    /// </summary>
    [TestClass]
    public class TransaccionesSegurasTests
    {
        [TestMethod]
        public void RollbackSeguro_ConTransaccionNull_NoLanza()
        {
            DbContextTransaction transaccion = null;

            transaccion.RollbackSeguro();
        }
    }
}
