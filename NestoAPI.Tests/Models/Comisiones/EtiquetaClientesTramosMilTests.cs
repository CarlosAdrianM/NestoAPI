using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Comisiones;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class EtiquetaClientesTramosMilTests
    {
        [TestMethod]
        public void EtiquetaClientesTramosMil_SiNoHayNingunClienteConVenta_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>();
            A.CallTo(() => servicio.LeerClientesNuevosConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesTramosMil(servicio);

            // Act
            int resultado = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(0, resultado);
        }

        [TestMethod]
        public void EtiquetaClientesTramosMil_SiHayUnClienteConVentaDeMasDeMil_DevuelveUno()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>() { 
                new ClienteVenta
                {
                    Cliente = "1234",
                    Venta = 1500
                }
            };
            A.CallTo(() => servicio.LeerClientesConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesTramosMil(servicio);

            // Act
            int resultado = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(1, resultado);
        }

        [TestMethod]
        public void EtiquetaClientesTramosMil_SiHayUnClienteConVentaDeMenosDeMil_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>() {
                new ClienteVenta
                {
                    Cliente = "1234",
                    Venta = 500
                }
            };
            A.CallTo(() => servicio.LeerClientesConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesTramosMil(servicio);

            // Act
            int resultado = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(0, resultado);
        }

        [TestMethod]
        public void EtiquetaClientesTramosMil_SiHayUnClienteConVentaDeMasDeDosMil_DevuelveDos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>() {
                new ClienteVenta
                {
                    Cliente = "1234",
                    Venta = 2500
                }
            };
            A.CallTo(() => servicio.LeerClientesConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesTramosMil(servicio);

            // Act
            int resultado = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(2, resultado);
        }
    }
}
