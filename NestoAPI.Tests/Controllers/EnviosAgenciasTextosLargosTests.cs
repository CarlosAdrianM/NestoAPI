using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// ELMAH 20-21/08/26: el alta de un envío reventaba entera con DbEntityValidationException
    /// ("El campo Observaciones debe ser un tipo de cadena o matriz con una longitud máxima de
    /// '80'") y el expedidor no podía tramitar el envío. Caso real: Santiago, 21/08 07:36 local,
    /// primer envío del día.
    /// </summary>
    [TestClass]
    public class EnviosAgenciasTextosLargosTests
    {
        private const int MAXIMO = 80;

        [TestMethod]
        public void RecortarTextosLibres_ObservacionesDemasiadoLargas_LasRecortaYNoPierdeElEnvio()
        {
            string original = new string('A', 120);
            var envio = new EnviosAgencia { Observaciones = original };

            EnviosAgenciasController.RecortarTextosLibres(envio);

            Assert.AreEqual(MAXIMO, envio.Observaciones.Length, "Más de 80 no cabe en la columna: recortar es mejor que perder el envío");
            Assert.AreEqual(original.Substring(0, MAXIMO), envio.Observaciones, "Se conserva el principio del texto");
        }

        [TestMethod]
        public void RecortarTextosLibres_AtencionDemasiadoLarga_LaRecorta()
        {
            var envio = new EnviosAgencia { Atencion = new string('B', 81) };

            EnviosAgenciasController.RecortarTextosLibres(envio);

            Assert.AreEqual(MAXIMO, envio.Atencion.Length);
        }

        [TestMethod]
        public void RecortarTextosLibres_TextosQueCaben_NoLosToca()
        {
            var envio = new EnviosAgencia { Observaciones = "Llamar antes de subir", Atencion = "Recepción" };

            EnviosAgenciasController.RecortarTextosLibres(envio);

            Assert.AreEqual("Llamar antes de subir", envio.Observaciones);
            Assert.AreEqual("Recepción", envio.Atencion);
        }

        [TestMethod]
        public void RecortarTextosLibres_SinTextos_NoRompe()
        {
            var envio = new EnviosAgencia();

            EnviosAgenciasController.RecortarTextosLibres(envio);

            Assert.IsNull(envio.Observaciones);
            Assert.IsNull(envio.Atencion);
        }

        // La dirección NO se recorta a propósito: un recorte silencioso ahí es un paquete mal
        // entregado. Preferimos que falle de forma ruidosa y se corrija el dato.
        [TestMethod]
        public void RecortarTextosLibres_DireccionDemasiadoLarga_NoLaToca()
        {
            string direccion = new string('C', 120);
            var envio = new EnviosAgencia { Direccion = direccion };

            EnviosAgenciasController.RecortarTextosLibres(envio);

            Assert.AreEqual(direccion, envio.Direccion, "La dirección se deja fallar: recortarla entrega mal el paquete");
        }
    }
}
