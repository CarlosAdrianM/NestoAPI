using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#500: un cliente puede acabar con DOS fichas marcadas como ClientePrincipal
    /// (el 19/09/2026 eran 79). Con Single/SingleOrDefault eso era un 500 "La secuencia contiene
    /// más de un elemento" en la plantilla de venta, la factura, los plazos de pago y el extracto.
    /// Estos tests fijan que ahora se atiende al contacto más bajo en vez de reventar.
    /// </summary>
    [TestClass]
    public class ConsultasClientePrincipalTests
    {
        private static Cliente Ficha(string cliente, string contacto, bool principal, short estado = 0)
        {
            return new Cliente
            {
                Empresa = "1",
                Nº_Cliente = cliente,
                Contacto = contacto,
                ClientePrincipal = principal,
                Estado = estado
            };
        }

        private static DbSet<Cliente> FakeClientes(IEnumerable<Cliente> fichas)
        {
            DbSet<Cliente> fake = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            IQueryable<Cliente> data = fichas.AsQueryable();

            A.CallTo(() => ((IDbAsyncEnumerable<Cliente>)fake).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<Cliente>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<Cliente>)fake).Provider)
                .Returns(new TestDbAsyncQueryProvider<Cliente>(data.Provider));
            A.CallTo(() => ((IQueryable<Cliente>)fake).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<Cliente>)fake).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<Cliente>)fake).GetEnumerator()).Returns(data.GetEnumerator());

            return fake;
        }

        [TestMethod]
        public void BuscarPrincipal_SiElClienteTieneDosFichasPrincipales_NoLanzaYDevuelveLaDelContactoMasBajo()
        {
            // El caso real: cliente 15296, con los contactos 0 y 1 marcados como principal.
            DbSet<Cliente> clientes = FakeClientes(new List<Cliente>
            {
                Ficha("15296", "1", principal: true, estado: 7),
                Ficha("15296", "0", principal: true, estado: -1),
                Ficha("15296", "2", principal: false, estado: 7)
            });

            Cliente principal = clientes.BuscarPrincipal("1", "15296");

            Assert.IsNotNull(principal);
            Assert.AreEqual("0", principal.Contacto);
        }

        [TestMethod]
        public void BuscarPrincipal_SiSoloHayUnaFichaPrincipal_LaDevuelve()
        {
            DbSet<Cliente> clientes = FakeClientes(new List<Cliente>
            {
                Ficha("39627", "0", principal: true),
                Ficha("39627", "1", principal: false)
            });

            Cliente principal = clientes.BuscarPrincipal("1", "39627");

            Assert.AreEqual("0", principal.Contacto);
        }

        [TestMethod]
        public void BuscarPrincipal_SiNingunaFichaEsPrincipal_DevuelveNull()
        {
            DbSet<Cliente> clientes = FakeClientes(new List<Cliente>
            {
                Ficha("39627", "0", principal: false)
            });

            Assert.IsNull(clientes.BuscarPrincipal("1", "39627"));
        }

        [TestMethod]
        public void BuscarPrincipal_NoSeLlevaLaFichaPrincipalDeOtroCliente()
        {
            DbSet<Cliente> clientes = FakeClientes(new List<Cliente>
            {
                Ficha("11111", "0", principal: true),
                Ficha("39627", "1", principal: true)
            });

            Cliente principal = clientes.BuscarPrincipal("1", "39627");

            Assert.AreEqual("39627", principal.Nº_Cliente);
            Assert.AreEqual("1", principal.Contacto);
        }

        [TestMethod]
        public async Task BuscarPrincipalAsync_SiElClienteTieneDosFichasPrincipales_NoLanzaYDevuelveLaDelContactoMasBajo()
        {
            DbSet<Cliente> clientes = FakeClientes(new List<Cliente>
            {
                Ficha("15296", "1", principal: true, estado: 7),
                Ficha("15296", "0", principal: true, estado: -1)
            });

            Cliente principal = await clientes.BuscarPrincipalAsync("1", "15296");

            Assert.AreEqual("0", principal.Contacto);
        }

        [TestMethod]
        public void Single_ConDosFichasPrincipales_Lanzaba_ElErrorQueSeArregla()
        {
            // Este test documenta el comportamiento ANTERIOR (el que producía el 500): si alguien
            // vuelve a escribir un Single sobre ClientePrincipal, esto explica por qué no debe.
            List<Cliente> fichas = new List<Cliente>
            {
                Ficha("15296", "0", principal: true),
                Ficha("15296", "1", principal: true)
            };

            _ = Assert.ThrowsException<InvalidOperationException>(() =>
                fichas.AsQueryable().Single(c => c.Empresa == "1" && c.Nº_Cliente == "15296" && c.ClientePrincipal));
        }
    }
}
