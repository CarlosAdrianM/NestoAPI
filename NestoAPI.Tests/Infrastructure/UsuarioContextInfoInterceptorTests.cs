using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Issue #286: el interceptor estampa el usuario real en CONTEXT_INFO al abrir cada conexión
    /// de EF, para poder identificar a la persona en los diagnósticos de bloqueos (todas las
    /// conexiones entran en SQL Server con la cuenta de máquina del IIS).
    /// </summary>
    [TestClass]
    public class UsuarioContextInfoInterceptorTests
    {
        [TestMethod]
        public void BytesUsuario_NombreNormal_DevuelveSusBytesUtf8()
        {
            byte[] bytes = UsuarioContextInfoInterceptor.BytesUsuario(@"NUEVAVISION\Carlos");

            Assert.AreEqual(@"NUEVAVISION\Carlos", Encoding.UTF8.GetString(bytes));
        }

        [TestMethod]
        public void BytesUsuario_SinUsuario_DevuelveUnByteParaSobrescribirElDelPool()
        {
            // Las conexiones del pool conservan el CONTEXT_INFO del usuario anterior: una petición
            // anónima debe sobrescribirlo igualmente (SET CONTEXT_INFO no admite binario vacío).
            byte[] deNull = UsuarioContextInfoInterceptor.BytesUsuario(null);
            byte[] deVacio = UsuarioContextInfoInterceptor.BytesUsuario(string.Empty);

            Assert.AreEqual(1, deNull.Length);
            Assert.AreEqual(0, deNull[0]);
            Assert.AreEqual(1, deVacio.Length);
            Assert.AreEqual(0, deVacio[0]);
        }

        [TestMethod]
        public void BytesUsuario_MasDe128Bytes_TruncaA128()
        {
            // CONTEXT_INFO es varbinary(128).
            string largo = new string('a', 200);

            byte[] bytes = UsuarioContextInfoInterceptor.BytesUsuario(largo);

            Assert.AreEqual(128, bytes.Length);
        }

        [TestMethod]
        public void UsuarioActual_SinHttpContext_UsaElPrincipalDelHilo()
        {
            // En jobs (Hangfire) no hay HttpContext; el fallback es Thread.CurrentPrincipal.
            IPrincipal original = Thread.CurrentPrincipal;
            try
            {
                Thread.CurrentPrincipal = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Job"), null);

                Assert.AreEqual(@"NUEVAVISION\Job", UsuarioContextInfoInterceptor.UsuarioActual());
            }
            finally
            {
                Thread.CurrentPrincipal = original;
            }
        }

        [TestMethod]
        public void UsuarioActual_SinHttpContextNiPrincipal_DevuelveVacioSinLanzar()
        {
            IPrincipal original = Thread.CurrentPrincipal;
            try
            {
                Thread.CurrentPrincipal = new GenericPrincipal(new GenericIdentity(string.Empty), null);

                Assert.AreEqual(string.Empty, UsuarioContextInfoInterceptor.UsuarioActual());
            }
            finally
            {
                Thread.CurrentPrincipal = original;
            }
        }
    }
}
