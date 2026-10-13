using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Sugerencia 582 (María José): en Rapports, a la vendedora telefónica le sale primero el
    /// contacto del cliente que está en estado 9, no el principal.
    /// </summary>
    [TestClass]
    public class ContactoTelefonicoPrimeroTests
    {
        private static IQueryable<Vendedor> Vendedores() => new List<Vendedor>
        {
            new Vendedor { Empresa = "1", Número = "MPP", Estado = 9 },
            new Vendedor { Empresa = "1", Número = "JE", Estado = 0 }
        }.AsQueryable();

        [TestMethod]
        public void EsVendedorTelefonico_VendedorEnEstado9_True()
        {
            Assert.IsTrue(ContactoTelefonicoPrimero.EsVendedorTelefonico(Vendedores(), "1", "MPP"));
        }

        [TestMethod]
        public void EsVendedorTelefonico_VendedorPresencialOVacio_False()
        {
            Assert.IsFalse(ContactoTelefonicoPrimero.EsVendedorTelefonico(Vendedores(), "1", "JE"));
            Assert.IsFalse(ContactoTelefonicoPrimero.EsVendedorTelefonico(Vendedores(), "1", ""));
            Assert.IsFalse(ContactoTelefonicoPrimero.EsVendedorTelefonico(Vendedores(), "1", null));
        }

        [TestMethod]
        public void ContactosTelefonicos_ClienteConPrincipalEnEstado7YOtroEn9_DevuelveElDeEstado9()
        {
            // Caso real 10932: contacto 0 principal en estado 7 y contacto 1 en estado 9
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "10932", Contacto = "0", Estado = 7, ClientePrincipal = true, Vendedor = "MPP" },
                new Cliente { Empresa = "1", Nº_Cliente = "10932", Contacto = "1", Estado = 9, ClientePrincipal = false, Vendedor = "MPP" }
            }.AsQueryable();

            Cliente elegido = ContactoTelefonicoPrimero.ContactosTelefonicos(clientes, "1", "10932", "MPP").FirstOrDefault();

            Assert.AreEqual("1", elegido.Contacto);
        }

        [TestMethod]
        public void ContactosTelefonicos_VariosEnEstado9_PrimeroElDelPropioVendedor()
        {
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "200", Contacto = "1", Estado = 9, Vendedor = "NV" },
                new Cliente { Empresa = "1", Nº_Cliente = "200", Contacto = "2", Estado = 9, Vendedor = "MPP" },
                new Cliente { Empresa = "1", Nº_Cliente = "200", Contacto = "3", Estado = 9, Vendedor = "NV" }
            }.AsQueryable();

            List<Cliente> orden = ContactoTelefonicoPrimero.ContactosTelefonicos(clientes, "1", "200", "MPP").ToList();

            CollectionAssert.AreEqual(new[] { "2", "1", "3" }, orden.Select(c => c.Contacto).ToArray());
        }

        [TestMethod]
        public void ContactosTelefonicos_SinNingunoEnEstado9_Vacio()
        {
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "300", Contacto = "0", Estado = 0, ClientePrincipal = true }
            }.AsQueryable();

            Assert.IsFalse(ContactoTelefonicoPrimero.ContactosTelefonicos(clientes, "1", "300", "MPP").Any());
        }

        [TestMethod]
        public void Ordenar_DentroDeCadaClienteElDeEstado9Delante_YLosClientesConservanSuOrden()
        {
            var lista = new List<ClienteDTO>
            {
                new ClienteDTO { cliente = "10932", contacto = "0", estado = 7 },
                new ClienteDTO { cliente = "500", contacto = "0", estado = 0 },
                new ClienteDTO { cliente = "10932", contacto = "1", estado = 9 },
                new ClienteDTO { cliente = "500", contacto = "1", estado = 0 }
            };

            List<ClienteDTO> ordenada = ContactoTelefonicoPrimero.Ordenar(lista);

            CollectionAssert.AreEqual(
                new[] { "10932/1", "10932/0", "500/0", "500/1" },
                ordenada.Select(c => c.cliente + "/" + c.contacto).ToArray());
        }
    }
}
