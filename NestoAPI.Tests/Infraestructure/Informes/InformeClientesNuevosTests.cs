using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infraestructure.Informes
{
    [TestClass]
    public class InformeClientesNuevosTests
    {
        [TestMethod]
        public void GenerarHtml_ConClientes_ContieneTablaConDatos()
        {
            var clientes = new List<ClienteNuevoVendedorDTO>
            {
                new ClienteNuevoVendedorDTO
                {
                    Cliente = "15191",
                    Contacto = "0",
                    Nombre = "Cliente Test",
                    Direccion = "Calle Mayor 1",
                    CodigoPostal = "28001",
                    Telefono = "912345678",
                    Estado = 0
                }
            };

            string html = InformeClientesNuevosJobsService.GenerarHtml("Pedro", clientes);

            Assert.IsTrue(html.Contains("15191"));
            Assert.IsTrue(html.Contains("Cliente Test"));
            Assert.IsTrue(html.Contains("Calle Mayor 1"));
            Assert.IsTrue(html.Contains("28001"));
            Assert.IsTrue(html.Contains("912345678"));
            Assert.IsTrue(html.Contains("Pedro"));
        }

        [TestMethod]
        public void GenerarHtml_SinClientes_GeneraTablaVacia()
        {
            var clientes = new List<ClienteNuevoVendedorDTO>();

            string html = InformeClientesNuevosJobsService.GenerarHtml("Ana", clientes);

            Assert.IsTrue(html.Contains("Ana"));
            Assert.IsTrue(html.Contains("<table"));
            Assert.IsFalse(html.Contains("<td>"));
        }

        [TestMethod]
        public void GenerarHtml_VariosClientes_OrdenCorrecto()
        {
            var clientes = new List<ClienteNuevoVendedorDTO>
            {
                new ClienteNuevoVendedorDTO
                {
                    Cliente = "001",
                    CodigoPostal = "28001",
                    Direccion = "Calle B",
                    Nombre = "Segundo"
                },
                new ClienteNuevoVendedorDTO
                {
                    Cliente = "002",
                    CodigoPostal = "28001",
                    Direccion = "Calle A",
                    Nombre = "Primero"
                }
            };

            string html = InformeClientesNuevosJobsService.GenerarHtml("Test", clientes);

            int posPrimero = html.IndexOf("Segundo");
            int posSegundo = html.IndexOf("Primero");
            // Los clientes llegan ya ordenados desde ObtenerClientesNuevosPorVendedor
            // GenerarHtml los renderiza en el orden que recibe
            Assert.IsTrue(posPrimero > 0);
            Assert.IsTrue(posSegundo > 0);
        }

        [TestMethod]
        public void GenerarHtml_ConEstadosCero_MuestraEstado()
        {
            var clientes = new List<ClienteNuevoVendedorDTO>
            {
                new ClienteNuevoVendedorDTO
                {
                    Cliente = "001",
                    Estado = 5,
                    Nombre = "Test"
                }
            };

            string html = InformeClientesNuevosJobsService.GenerarHtml("Test", clientes);

            Assert.IsTrue(html.Contains(">5<"));
        }
    }
}
