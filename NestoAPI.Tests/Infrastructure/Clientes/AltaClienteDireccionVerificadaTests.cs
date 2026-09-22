using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#499: guardarraíl de servidor para Nesto#480 / NestoApp#180. La dirección del alta
    /// tiene que venir elegida de las que propone Google. La validación vive detrás del parámetro
    /// ExigirDireccionVerificadaAlta porque los clientes que aún no mandan el flag lo dejan a false.
    /// </summary>
    [TestClass]
    public class AltaClienteDireccionVerificadaTests
    {
        [TestMethod]
        public void MotivoRechazo_InterruptorApagado_NuncaRechaza()
        {
            // El estado con el que se despliega: un Nesto viejo que no manda el flag sigue dando de alta.
            var alta = new ClienteCrear { Direccion = "C/ Río Tiétar, 11", DireccionVerificada = false };

            Assert.IsNull(GestorClientes.MotivoRechazoDireccionNoVerificada(alta, exigir: false));
        }

        [TestMethod]
        public void MotivoRechazo_InterruptorEncendidoYDireccionSinVerificar_Rechaza()
        {
            var alta = new ClienteCrear { Direccion = "C/ Río Tiétar, 11", DireccionVerificada = false };

            string motivo = GestorClientes.MotivoRechazoDireccionNoVerificada(alta, exigir: true);

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "Google");
        }

        [TestMethod]
        public void MotivoRechazo_InterruptorEncendidoYDireccionVerificada_Pasa()
        {
            var alta = new ClienteCrear { Direccion = "C/ Río Tiétar, 11", DireccionVerificada = true };

            Assert.IsNull(GestorClientes.MotivoRechazoDireccionNoVerificada(alta, exigir: true));
        }

        [TestMethod]
        public void MotivoRechazo_SinDireccion_NoHayNadaQueVerificar()
        {
            // Contactos de cobro o altas parciales sin dirección no se bloquean.
            Assert.IsNull(GestorClientes.MotivoRechazoDireccionNoVerificada(new ClienteCrear { Direccion = null, EsContacto = true }, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoDireccionNoVerificada(new ClienteCrear { Direccion = "   " }, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoDireccionNoVerificada(null, exigir: true));
        }

        [TestMethod]
        public void ExigirDireccionVerificadaAlta_LeeElParametroDeDefecto()
        {
            var lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro("1", Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.EXIGIR_DIRECCION_VERIFICADA_ALTA))
                .Returns("1 ");
            var gestor = new GestorClientes(A.Fake<IServicioGestorClientes>(), A.Fake<IServicioAgencias>(), null) { LectorParametros = lector };

            Assert.IsTrue(gestor.ExigirDireccionVerificadaAlta("1"));
        }

        [TestMethod]
        public void ExigirDireccionVerificadaAlta_SinParametroOConFallo_Apagado()
        {
            var sinFila = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => sinFila.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            var conFallo = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => conFallo.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new System.Exception("BD caída"));

            Assert.IsFalse(new GestorClientes(A.Fake<IServicioGestorClientes>(), A.Fake<IServicioAgencias>(), null) { LectorParametros = sinFila }.ExigirDireccionVerificadaAlta("1"));
            Assert.IsFalse(new GestorClientes(A.Fake<IServicioGestorClientes>(), A.Fake<IServicioAgencias>(), null) { LectorParametros = conFallo }.ExigirDireccionVerificadaAlta("1"));
        }
    }
}
