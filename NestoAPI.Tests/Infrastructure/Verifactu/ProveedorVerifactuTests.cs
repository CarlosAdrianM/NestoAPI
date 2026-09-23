using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Verifactu;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// Verifactu #326: el proveedor (hoy Verifacti) se decide en un único punto; facturas, PDF y jobs
    /// hablan con la interfaz genérica.
    /// </summary>
    [TestClass]
    public class ProveedorVerifactuTests
    {
        [TestMethod]
        public void Actual_EsUnaSolaInstanciaCompartida()
        {
            Assert.IsNotNull(ProveedorVerifactu.Actual);
            Assert.AreSame(ProveedorVerifactu.Actual, ProveedorVerifactu.Actual, "Un solo servicio (y un solo HttpClient) para toda la API");
        }

        [TestMethod]
        public void LeerBooleano_SoloTrueEnciende()
        {
            Assert.IsTrue(ProveedorVerifactu.LeerBooleano("true"));
            Assert.IsTrue(ProveedorVerifactu.LeerBooleano(" True "));
            Assert.IsFalse(ProveedorVerifactu.LeerBooleano("false"));
            Assert.IsFalse(ProveedorVerifactu.LeerBooleano(null), "Sin la clave, el QR no se imprime");
            Assert.IsFalse(ProveedorVerifactu.LeerBooleano("si"));
        }
    }
}
