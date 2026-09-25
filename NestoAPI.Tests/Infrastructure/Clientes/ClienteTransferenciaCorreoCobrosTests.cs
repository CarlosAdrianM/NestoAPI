using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#544 (a): un cliente que paga por transferencia a plazo (TRN y plazos distintos de
    /// prepago y contado) recibe las facturas y los avisos de pago por correo, así que necesita una
    /// persona de contacto activa con correo y cargo Cobros (1) o Factura por correo (22). Detrás del
    /// parámetro ExigirCorreoCobrosTransferencia (apagado al desplegar) porque afecta a las altas de
    /// Nesto, NestoApp y TNV.
    /// </summary>
    [TestClass]
    public class ClienteTransferenciaCorreoCobrosTests
    {
        private static PersonaContactoCliente Persona(string correo, short cargo, short estado = 0)
            => new PersonaContactoCliente { CorreoElectrónico = correo, Cargo = cargo, Estado = estado };

        private static readonly List<PersonaContactoCliente> SIN_NADIE = new List<PersonaContactoCliente>();

        [TestMethod]
        public void MotivoRechazo_InterruptorApagado_NuncaRechaza()
        {
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/30", SIN_NADIE, exigir: false));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/30", null, exigir: false));
        }

        [TestMethod]
        public void MotivoRechazo_TransferenciaAPlazoSinCorreoDeCobros_Rechaza()
        {
            string motivo = GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/30", SIN_NADIE, exigir: true);

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "transferencia a plazo (TRN 1/30)");
            StringAssert.Contains(motivo, "Cobros o Factura por correo");
            Assert.IsNotNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/60", null, exigir: true), "Sin lista tampoco");
            Assert.IsNotNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN ", " 1/90 ", SIN_NADIE, exigir: true), "Con relleno de char()");
        }

        [TestMethod]
        public void MotivoRechazo_PersonaSinCorreoDeBajaOConOtroCargo_NoVale()
        {
            List<PersonaContactoCliente> personas = new List<PersonaContactoCliente>
            {
                Persona(null, Constantes.Clientes.PersonasContacto.CARGO_COBROS),
                Persona("  ", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO),
                Persona("baja@cliente.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS, estado: -1),
                Persona("jefa@cliente.es", Constantes.Clientes.CARGO_POR_DEFECTO)
            };

            Assert.IsNotNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/30", personas, exigir: true));
        }

        [TestMethod]
        public void MotivoRechazo_ConPersonaDeCobrosOFacturaPorCorreo_Pasa()
        {
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/30", new List<PersonaContactoCliente>
            {
                Persona("cobros@cliente.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS)
            }, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "1/30", new List<PersonaContactoCliente>
            {
                Persona("jefa@cliente.es", Constantes.Clientes.CARGO_POR_DEFECTO),
                Persona("facturas@cliente.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
            }, exigir: true));
        }

        [TestMethod]
        public void MotivoRechazo_PrepagoContadoUOtraFormaDePago_NoSeExige()
        {
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "PRE", SIN_NADIE, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "CONTADO", SIN_NADIE, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", "CR", SIN_NADIE, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("RCB", "1/30", SIN_NADIE, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("EFC", "1/30", SIN_NADIE, exigir: true));
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros(null, "1/30", SIN_NADIE, exigir: true), "Sin forma de pago no hay nada que exigir");
            Assert.IsNull(GestorClientes.MotivoRechazoSinCorreoCobros("TRN", null, SIN_NADIE, exigir: true));
        }

        [TestMethod]
        public void ExigirCorreoCobrosTransferencia_LeeElParametroDeDefecto_YSinFilaOConFalloApagado()
        {
            var encendido = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => encendido.LeerParametro("1", Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.EXIGIR_CORREO_COBROS_TRANSFERENCIA))
                .Returns("1 ");
            var sinFila = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => sinFila.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            var conFallo = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => conFallo.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new System.Exception("BD caída"));

            Assert.IsTrue(Gestor(encendido).ExigirCorreoCobrosTransferencia("1"));
            Assert.IsTrue(Gestor(encendido).ExigirCorreoCobrosTransferencia(null), "Sin empresa, la de defecto");
            Assert.IsFalse(Gestor(sinFila).ExigirCorreoCobrosTransferencia("1"));
            Assert.IsFalse(Gestor(conFallo).ExigirCorreoCobrosTransferencia("1"));
        }

        private static GestorClientes Gestor(ILectorParametrosUsuario lector)
            => new GestorClientes(A.Fake<IServicioGestorClientes>(), A.Fake<IServicioAgencias>(), null) { LectorParametros = lector };
    }
}
