using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Issue #214: GetDireccionesEntrega devolvía 500 (NullReferenceException) cuando el cliente
    /// no tenía contacto principal, porque la proyección desreferenciaba clienteDireccionPorDefecto null.
    /// </summary>
    [TestClass]
    public class PlantillaVentasControllerTests
    {
        [TestMethod]
        public void GetDireccionesEntrega_ClienteSinContactoPrincipal_NoLanza500YNingunaEsPorDefecto()
        {
            // Cliente con una sola dirección y SIN ninguna fila ClientePrincipal (el caso que petaba)
            var cliente = CrearClienteCompleto("1", "12345", "0", clientePrincipal: false);
            var controller = CrearControllerConClientes(cliente);

            // Antes del fix esto lanzaba NullReferenceException (500)
            var resultado = controller.GetDireccionesEntrega("1", "12345", 0).ToList();

            Assert.AreEqual(1, resultado.Count);
            Assert.IsFalse(resultado[0].esDireccionPorDefecto, "Sin contacto principal, ninguna dirección debe quedar marcada como por defecto");
        }

        [TestMethod]
        public void GetDireccionesEntrega_ClienteConContactoPrincipal_MarcaSoloLaDireccionPorDefecto()
        {
            var principal = CrearClienteCompleto("1", "12345", "0", clientePrincipal: true);
            principal.ContactoDefecto = "0";
            var otraDireccion = CrearClienteCompleto("1", "12345", "1", clientePrincipal: false);
            var controller = CrearControllerConClientes(principal, otraDireccion);

            var resultado = controller.GetDireccionesEntrega("1", "12345", 0).ToList();

            Assert.AreEqual(2, resultado.Count);
            Assert.IsTrue(resultado.Single(d => d.contacto == "0").esDireccionPorDefecto);
            Assert.IsFalse(resultado.Single(d => d.contacto == "1").esDireccionPorDefecto);
        }

        #region Helpers

        private static PlantillaVentasController CrearControllerConClientes(params Cliente[] clientes)
        {
            var db = A.Fake<NVEntities>();
            var fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            ConfigurarFakeDbSet(fakeClientes, clientes.AsQueryable());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);

            return new PlantillaVentasController(A.Fake<IServicioPlantillaVenta>(), db);
        }

        private static Cliente CrearClienteCompleto(string empresa, string numero, string contacto, bool clientePrincipal)
        {
            return new Cliente
            {
                Empresa = empresa,
                Nº_Cliente = numero,
                Contacto = contacto,
                Estado = 0,
                ClientePrincipal = clientePrincipal,
                ContactoDefecto = contacto,
                CodPostal = "28000",
                ComentarioPicking = "",
                ComentarioRuta = "",
                Comentarios = "",
                Dirección = "Calle Test 1",
                IVA = "G21",
                Nombre = "CLIENTE TEST",
                Población = "MADRID",
                Provincia = "MADRID",
                Vendedor = "NV",
                PeriodoFacturación = "NOR",
                CCC = "",
                Ruta = "",
                CIF_NIF = null,
                CondPagoClientes = new List<CondPagoCliente>
                {
                    new CondPagoCliente { ImporteMínimo = 0, FormaPago = "CONT", PlazosPago = "CONTADO" }
                },
                PersonasContactoClientes = new List<PersonaContactoCliente>()
            };
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        #endregion
    }
}
