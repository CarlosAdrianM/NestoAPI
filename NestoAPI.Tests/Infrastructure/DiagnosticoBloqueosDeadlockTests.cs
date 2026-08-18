using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#364: resumen del deadlock graph de system_health. Para un DEADLOCK la consulta
    /// de bloqueadores activos no sirve por diseño (SQL Server mata a la víctima al instante y
    /// ya no hay bloqueo que ver); el causante está en el xml_deadlock_report del ring buffer.
    /// </summary>
    [TestClass]
    public class DiagnosticoBloqueosDeadlockTests
    {
        private static readonly DateTime Fecha = new DateTime(2026, 8, 18, 10, 15, 27);

        private const string GRAFO = @"<deadlock>
 <victim-list><victimProcess id=""process1fabc"" /></victim-list>
 <process-list>
  <process id=""process1fabc"" spid=""52"" loginname=""NUEVAVISION\RDS2016$"" hostname=""RDS2016"" clientapp="".Net SqlClient Data Provider"">
   <inputbuf>EXEC prdCrearFacturaCmp @Empresa, @Pedido</inputbuf>
  </process>
  <process id=""process2fdef"" spid=""67"" loginname=""NUEVAVISION\Aida"" hostname=""PC-AIDA"" clientapp=""Nesto"">
   <inputbuf>UPDATE ExtractoProveedor SET Estado = 1</inputbuf>
  </process>
 </process-list>
</deadlock>";

        [TestMethod]
        public void ResumirDeadlockGraph_ConVictimaYOtroProceso_NombraAmbosYMarcaLaVictima()
        {
            string resumen = DiagnosticoBloqueos.ResumirDeadlockGraph(GRAFO, Fecha);

            Assert.IsNotNull(resumen);
            StringAssert.Contains(resumen, "10:15:27");
            StringAssert.Contains(resumen, @"NUEVAVISION\RDS2016$");
            StringAssert.Contains(resumen, "víctima");
            StringAssert.Contains(resumen, @"NUEVAVISION\Aida");
            StringAssert.Contains(resumen, "PC-AIDA");
            StringAssert.Contains(resumen, "prdCrearFacturaCmp");
            StringAssert.Contains(resumen, "UPDATE ExtractoProveedor");
            // La víctima se cuenta primero (es la operación del usuario que ve el error)
            Assert.IsTrue(resumen.IndexOf(@"NUEVAVISION\RDS2016$", StringComparison.Ordinal)
                < resumen.IndexOf(@"NUEVAVISION\Aida", StringComparison.Ordinal));
        }

        [TestMethod]
        public void ResumirDeadlockGraph_InputbufLargo_SeTrunca()
        {
            string sqlLargo = new string('X', 500);
            string grafo = GRAFO.Replace("UPDATE ExtractoProveedor SET Estado = 1", sqlLargo);

            string resumen = DiagnosticoBloqueos.ResumirDeadlockGraph(grafo, Fecha);

            Assert.IsNotNull(resumen);
            Assert.IsFalse(resumen.Contains(sqlLargo), "el inputbuf de 500 caracteres debe truncarse");
            StringAssert.Contains(resumen, "XXX");
        }

        [TestMethod]
        public void ResumirDeadlockGraph_XmlInvalidoONulo_DevuelveNull()
        {
            Assert.IsNull(DiagnosticoBloqueos.ResumirDeadlockGraph("esto no es xml <", Fecha));
            Assert.IsNull(DiagnosticoBloqueos.ResumirDeadlockGraph(null, Fecha));
            Assert.IsNull(DiagnosticoBloqueos.ResumirDeadlockGraph("<deadlock/>", Fecha));
        }
    }
}
