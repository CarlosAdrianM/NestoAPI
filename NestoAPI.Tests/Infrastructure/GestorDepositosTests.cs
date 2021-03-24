using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Depositos;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Depositos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorDepositosTests
    {
        [TestMethod]
        public void GestorDepositos_EnviarCorreos_DevuelveUnoPorCadaProveedor()
        {
            IServicioDeposito servicio = A.Fake<IServicioDeposito>();
            IServicioGestorStocks servicioGestorStocks = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.LeerProveedoresEnDeposito()).Returns(new List<PersonaContactoProveedorDTO> {
                new PersonaContactoProveedorDTO
                {
                    ProveedorId = "1",
                    NombrePersonaContacto = "Pepito",
                    CorreoElectronico = "pepito@proveedor.com"
                }
            });
            A.CallTo(() => servicio.EnviarCorreoSMTP(A<MailMessage>.Ignored)).Returns(true);
            GestorDepositos gestor = new GestorDepositos(servicio, servicioGestorStocks);

            var resultado = gestor.EnviarCorreos().Result;

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(true, resultado.First().EnvioConExito);
        }

        // Si no hay ningún producto, no se manda

        // Si el proveedor está nulo, no se manda
    }
}
