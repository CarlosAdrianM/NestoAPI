using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Comisiones;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class EtiquetaClientesNuevosTests
    {
        [TestMethod]
        public void EtiquetaClientesNuevos_LeerClientesMes_SiNoHayNingunoDevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>();
            A.CallTo(() => servicio.LeerClientesNuevosConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesNuevos(servicio);

            // Act
            int clientes = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(0, clientes);
        }

        [TestMethod]
        public void EtiquetaClientesNuevos_LeerClientesMes_SiHayUnClienteDeMasDeTrescientosDevuelveUno()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta> 
            {
                new ClienteVenta{
                    Cliente = "1",
                    Venta = 1000
                },
            };
            A.CallTo(() => servicio.LeerClientesNuevosConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesNuevos(servicio);

            // Act
            int clientes = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(1, clientes);
        }

        [TestMethod]
        public void EtiquetaClientesNuevos_LeerClientesMes_SiHayUnClienteDeMenosDeTrescientosDevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>
            {
                new ClienteVenta{
                    Cliente = "1",
                    Venta = 200
                },
            };
            A.CallTo(() => servicio.LeerClientesNuevosConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            var sut = new EtiquetaClientesNuevos(servicio);

            // Act
            int clientes = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(0, clientes);
        }

        [TestMethod]
        public void EtiquetaClientesNuevos_LeerClientesMes_SiHayComisionesDeMesesAnterioresSeRestanAlMesActual()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var listaClientes = new List<ClienteVenta>
            {
                new ClienteVenta{
                    Cliente = "1",
                    Venta = 400
                },
            };
            A.CallTo(() => servicio.LeerClientesNuevosConVenta(A<string>._, A<int>._, A<int>._)).Returns(listaClientes);
            A.CallTo(() => servicio.LeerComisionesAnualesResumenMes(A<List<string>>._, A<int>._)).Returns(new List<ComisionAnualResumenMes>
            {
                new ComisionAnualResumenMes
                {
                    Etiqueta = "Clientes nuevos",
                    Venta = 1// el campo en la BBDD se llama "Venta" pero es el "Recuento"
                }
            });
            var sut = new EtiquetaClientesNuevos(servicio);

            // Act
            int clientes = sut.LeerClientesMes("VD", 2024, 1);

            // Assert
            Assert.AreEqual(0, clientes);
        }
    }
}
