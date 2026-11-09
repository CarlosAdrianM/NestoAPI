using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;

namespace NestoAPI.Tests.Infraestructure.Clientes
{
    /// <summary>
    /// 09/11/26 (importar leads de Meta): el alta pone en PersonasContactoCliente.Saludo (char 15) el nombre entero de la persona de
    /// contacto y, con un nombre de más de 15 letras, falla TODA el alta («El campo Saludo debe ser… longitud máxima de '15'»): 3 de
    /// los 5 leads de Meta, y cualquier alta desde Nesto con un nombre largo.
    /// </summary>
    [TestClass]
    public class SaludoPersonaContactoTests
    {
        [TestMethod]
        public void NombreCorto_ElNombreEnteroConMayusculaInicial()
        {
            Assert.AreEqual("Ana López", GestorClientes.SaludoDesdeNombre("ANA LÓPEZ"));
        }

        [TestMethod]
        public void NombreLargo_SoloElNombreDePila()
        {
            Assert.AreEqual("María", GestorClientes.SaludoDesdeNombre("MARÍA DEL CARMEN FERNÁNDEZ"));
        }

        [TestMethod]
        public void NombreDePilaDeMasDe15_SeRecortaA15()
        {
            string saludo = GestorClientes.SaludoDesdeNombre("Maximilianoaurelio Pérez");
            Assert.AreEqual(15, saludo.Length);
            Assert.AreEqual("Maximilianoaure", saludo);
        }

        [TestMethod]
        public void SinNombre_Null()
        {
            Assert.IsNull(GestorClientes.SaludoDesdeNombre(null));
            Assert.IsNull(GestorClientes.SaludoDesdeNombre("   "));
        }

        [TestMethod]
        public void EspaciosDeMas_SeQuitan()
        {
            Assert.AreEqual("Ana López", GestorClientes.SaludoDesdeNombre("  ana   lópez  "));
        }

    }
}
