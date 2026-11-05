using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#830 (sugerencia 1065, Alfredo; Carlos 05/11/26): la ruta de reparto que vale es SIEMPRE la de la ficha del
    /// cliente en la empresa 1, aunque el pedido sea de la 3. Caso real: contacto con FW en la ficha de la 3 y 16 en la de la 1;
    /// su pedido de la 3 salía por agencia al día siguiente en vez de en su día de ruta.
    /// </summary>
    [TestClass]
    public class RutaQueValeTests
    {
        private const string CLIENTE = "15191";
        private const string CP_MIERCOLES = "28001";

        [TestInitialize]
        public void Preparar()
        {
            RutasPropiasConfiguradas.Reiniciar(); // AT y 16
            DiasRepartoCodigosPostales.ConfigurarPara(new Dictionary<string, string> { [CP_MIERCOLES] = "03" });
            DiasRepartoClientes.ConfigurarPara();
        }

        [TestCleanup]
        public void Limpiar()
        {
            RutasPropiasConfiguradas.Reiniciar();
            DiasRepartoCodigosPostales.Reiniciar();
            DiasRepartoClientes.Reiniciar();
        }

        // ============================================================ la regla (sin base de datos)

        [TestMethod]
        public void Elegir_Empresa3ConRutaEnLa1_LaDeLa1()
        {
            Assert.AreEqual("16", RutaQueVale.Elegir("3", "FW", "16"));
        }

        [TestMethod]
        public void Elegir_Empresa1_LaDeSuFicha()
        {
            Assert.AreEqual("FW", RutaQueVale.Elegir("1", "FW", "16"));
            Assert.AreEqual("FW", RutaQueVale.Elegir("1  ", "FW", "16"));
        }

        [TestMethod]
        public void Elegir_Empresa3SinRutaEnLa1_LaDeSuEmpresa()
        {
            Assert.AreEqual("FW", RutaQueVale.Elegir("3", "FW", null));
            Assert.AreEqual("FW", RutaQueVale.Elegir("3", "FW ", "  "));
        }

        [TestMethod]
        public void ParaGrabar_Empresa3ConLaRutaDeSuFicha_SeGrabaLaDeLa1()
        {
            Assert.AreEqual("16", RutaQueVale.ParaGrabar("3", "FW", "FW", "16"));
            Assert.AreEqual("16", RutaQueVale.ParaGrabar("3", "FW ", "FW", "16"));
        }

        [TestMethod]
        public void ParaGrabar_Empresa3SinRuta_SeGrabaLaDeLa1()
        {
            Assert.AreEqual("16", RutaQueVale.ParaGrabar("3", null, "FW", "16"));
            Assert.AreEqual("16", RutaQueVale.ParaGrabar("3", "", "FW", "16"));
        }

        [TestMethod]
        public void ParaGrabar_Empresa3ConOtraRutaElegidaAMano_SeRespeta()
        {
            Assert.AreEqual(Constantes.Pedidos.RUTA_GLOVO, RutaQueVale.ParaGrabar("3", Constantes.Pedidos.RUTA_GLOVO, "FW", "16"));
        }

        [TestMethod]
        public void ParaGrabar_Empresa1_LaQueVenga()
        {
            Assert.AreEqual("FW", RutaQueVale.ParaGrabar("1", "FW", "FW", "16"));
        }

        // ============================================================ el pedido (POST/PUT, fecha de entrega de la plantilla)

        [TestMethod]
        public void AjustarPedido_Empresa3ConFWYEnLa1Con16_SeGraba16YCogeElDiaDeRutaDeSuCP()
        {
            NVEntities db = DbConClientes(Ficha("3", "0", "FW"), Ficha("1", "0", "16"));
            PedidoVentaDTO pedido = Pedido("3", "0", "FW");
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna(pedido.ruta, CP_MIERCOLES, pedido.empresa, pedido.cliente, pedido.contacto),
                "Con FW no hay día de ruta (el fallo: salía por agencia)");

            string venia = RutaQueVale.AjustarPedido(db, pedido);

            Assert.AreEqual("FW", venia);
            Assert.AreEqual("16", pedido.ruta);
            Assert.AreEqual(DayOfWeek.Wednesday,
                DiasRepartoCodigosPostales.DiaRutaInterna(pedido.ruta, CP_MIERCOLES, pedido.empresa, pedido.cliente, pedido.contacto));
        }

        [TestMethod]
        public void AjustarPedido_Empresa1_ComoSiempre()
        {
            NVEntities db = DbConClientes(Ficha("3", "0", "16"), Ficha("1", "0", "FW"));
            PedidoVentaDTO pedido = Pedido("1", "0", "FW");

            Assert.IsNull(RutaQueVale.AjustarPedido(db, pedido));
            Assert.AreEqual("FW", pedido.ruta);
        }

        [TestMethod]
        public void AjustarPedido_ClienteSinFichaEnLa1_LaDeSuEmpresa()
        {
            NVEntities db = DbConClientes(Ficha("3", "0", "FW"));
            PedidoVentaDTO pedido = Pedido("3", "0", "FW");

            Assert.IsNull(RutaQueVale.AjustarPedido(db, pedido));
            Assert.AreEqual("FW", pedido.ruta);
        }

        [TestMethod]
        public void AjustarPedido_FichaDeLa1Anulada_LaDeSuEmpresa()
        {
            Cliente anulada = Ficha("1", "0", "16");
            anulada.Estado = -1;
            NVEntities db = DbConClientes(Ficha("3", "0", "FW"), anulada);
            PedidoVentaDTO pedido = Pedido("3", "0", "FW");

            Assert.IsNull(RutaQueVale.AjustarPedido(db, pedido));
            Assert.AreEqual("FW", pedido.ruta);
        }

        [TestMethod]
        public void AjustarPedido_OtroContacto_NoSeMezcla()
        {
            NVEntities db = DbConClientes(Ficha("3", "1", "FW"), Ficha("1", "0", "16"), Ficha("1", "1", "AT"));
            PedidoVentaDTO pedido = Pedido("3", "1", "FW");

            _ = RutaQueVale.AjustarPedido(db, pedido);

            Assert.AreEqual("AT", pedido.ruta);
        }

        [TestMethod]
        public void AjustarPedido_SinFichaLegible_NoLanzaYSeGrabaLoQueVenga()
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Clientes).Throws(new InvalidOperationException("sin BD"));
            PedidoVentaDTO pedido = Pedido("3", "0", "FW");

            Assert.IsNull(RutaQueVale.AjustarPedido(db, pedido));
            Assert.AreEqual("FW", pedido.ruta);
        }

        // ============================================================ lo que la API propone al montar el pedido

        [TestMethod]
        public void GetDireccionesEntrega_Empresa3_ProponeLaRutaDeLaFichaDeLa1()
        {
            NVEntities db = DbConClientes(Ficha("3", "0", "FW"), Ficha("3", "1", "FW"), Ficha("1", "0", "16"));
            var controller = new PlantillaVentasController(A.Fake<IServicioPlantillaVenta>(), db);

            List<DireccionesEntregaClienteDTO> direcciones = controller.GetDireccionesEntrega("3", CLIENTE, 0).ToList();

            Assert.AreEqual("16", direcciones.Single(d => d.contacto == "0").ruta);
            Assert.AreEqual("FW", direcciones.Single(d => d.contacto == "1").ruta, "Sin ficha en la 1, la de su empresa");
        }

        [TestMethod]
        public void GetDireccionesEntrega_Empresa1_LaRutaDeSuFicha()
        {
            NVEntities db = DbConClientes(Ficha("1", "0", "FW"), Ficha("3", "0", "16"));
            var controller = new PlantillaVentasController(A.Fake<IServicioPlantillaVenta>(), db);

            List<DireccionesEntregaClienteDTO> direcciones = controller.GetDireccionesEntrega("1", CLIENTE, 0).ToList();

            Assert.AreEqual("FW", direcciones.Single().ruta);
        }

        [TestMethod]
        public void GetCliente_Empresa3_ProponeLaRutaDeLaFichaDeLa1()
        {
            Cliente ficha3 = Ficha("3", "0", "FW");
            NVEntities db = DbConClientes(ficha3, Ficha("1", "0", "16"));
            ClientesController controller = Controller(db);

            Assert.AreEqual("16", controller.RutaQueValeContacto(ficha3));
        }

        [TestMethod]
        public void GetCliente_Empresa1_LaRutaDeSuFichaTalCual()
        {
            Cliente ficha1 = Ficha("1", "0", "FW ");
            NVEntities db = DbConClientes(ficha1, Ficha("3", "0", "16"));
            ClientesController controller = Controller(db);

            Assert.AreEqual("FW ", controller.RutaQueValeContacto(ficha1));
        }

        [TestMethod]
        public void GetCliente_Empresa3SinFichaEnLa1_LaDeSuEmpresa()
        {
            Cliente ficha3 = Ficha("3", "0", "FW");
            ClientesController controller = Controller(DbConClientes(ficha3));

            Assert.AreEqual("FW", controller.RutaQueValeContacto(ficha3));
        }

        // ============================================================ ayudas

        private static ClientesController Controller(NVEntities db) => new ClientesController(
            A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(),
            A.Fake<IServicioValidacionNif>(), dbInyectada: db, vendedoresVisibles: A.Fake<IServicioVendedoresVisibles>());

        private static PedidoVentaDTO Pedido(string empresa, string contacto, string ruta) => new PedidoVentaDTO
        {
            empresa = empresa,
            cliente = CLIENTE,
            contacto = contacto,
            ruta = ruta
        };

        private static Cliente Ficha(string empresa, string contacto, string ruta) => new Cliente
        {
            Empresa = empresa,
            Nº_Cliente = CLIENTE,
            Contacto = contacto,
            Estado = 0,
            ClientePrincipal = contacto == "0",
            ContactoDefecto = "0",
            CodPostal = CP_MIERCOLES,
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
            Ruta = ruta,
            CondPagoClientes = new List<CondPagoCliente>
            {
                new CondPagoCliente { ImporteMínimo = 0, FormaPago = "CONT", PlazosPago = "CONTADO" }
            },
            PersonasContactoClientes = new List<PersonaContactoCliente>()
        };

        private static NVEntities DbConClientes(params Cliente[] clientes)
        {
            NVEntities db = A.Fake<NVEntities>();
            var fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            IQueryable<Cliente> data = clientes.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<Cliente>)fakeClientes).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<Cliente>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).Provider).Returns(new TestDbAsyncQueryProvider<Cliente>(data.Provider));
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            return db;
        }
    }
}
