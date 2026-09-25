using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#544 (f): GET api/Clientes con un número de factura (NV2615541) devuelve el cliente
    /// de esa factura cuando la búsqueda normal no encuentra nada. En las dos sobrecargas (con
    /// vendedor, la del SelectorCliente de Nesto, y sin él, la de NestoApp).
    /// </summary>
    [TestClass]
    public class ClientesControllerBusquedaPorFacturaTests
    {
        private NVEntities db;
        private IServicioVendedores servicioVendedores;
        private ClientesController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            DbSet<Cliente> clientes = Crear<Cliente>();
            DbSet<CabFacturaVta> facturas = Crear<CabFacturaVta>();
            DbSet<VendedorClienteGrupoProducto> vendedoresGrupos = Crear<VendedorClienteGrupoProducto>();
            A.CallTo(() => db.Clientes).Returns(clientes);
            A.CallTo(() => db.CabsFacturasVtas).Returns(facturas);
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(vendedoresGrupos);
            ConfigurarFakeDbSet(clientes, new List<Cliente>
            {
                Cliente("27120", "0", "PELUQUERÍA ANA", vendedor: "NV"),
                Cliente("27120", "1", "PELUQUERÍA ANA (COBROS)", vendedor: "NV"),
                Cliente("30676", "0", "ESTÉTICA LUZ", vendedor: "JE"),
                Cliente("40000", "0", "DE BAJA", vendedor: "NV", estado: -1)
            });
            ConfigurarFakeDbSet(facturas, new List<CabFacturaVta>
            {
                new CabFacturaVta { Empresa = "1", Número = "NV2615541", Nº_Cliente = "27120", Contacto = "1" },
                new CabFacturaVta { Empresa = "1", Número = "NV2615600", Nº_Cliente = "30676", Contacto = "0" },
                new CabFacturaVta { Empresa = "1", Número = "NV2615700", Nº_Cliente = "40000", Contacto = "0" }
            });
            ConfigurarFakeDbSet(vendedoresGrupos, new List<VendedorClienteGrupoProducto>());
            servicioVendedores = A.Fake<IServicioVendedores>();
            A.CallTo(() => servicioVendedores.VendedoresEquipo("1", "NV"))
                .Returns(Task.FromResult(new List<VendedorDTO> { new VendedorDTO { vendedor = "NV" } }));
            controller = new ClientesController(A.Fake<IGestorClientes>(), servicioVendedores, A.Fake<IGestorSincronizacion>(), dbInyectada: db);
        }

        private static DbSet<T> Crear<T>() where T : class
            => A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            IQueryable<T> data = lista.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        // La proyección a ClienteDTO hace Trim() de todo: en memoria, los strings no pueden ser null
        private static Cliente Cliente(string numero, string contacto, string nombre, string vendedor, short estado = 0)
            => new Cliente
            {
                Empresa = "1",
                Nº_Cliente = numero,
                Contacto = contacto,
                Nombre = nombre,
                Vendedor = vendedor,
                Estado = estado,
                Cadena = "",
                CCC = "",
                CIF_NIF = "",
                CodPostal = "",
                ComentarioPicking = "",
                ComentarioRuta = "",
                Comentarios = "",
                Dirección = "",
                Grupo = "",
                IVA = "",
                PeriodoFacturación = "",
                Población = "",
                Provincia = "",
                Ruta = "",
                Teléfono = "",
                Web = ""
            };

        [TestMethod]
        public void TieneFormatoDeFactura_DosLetrasYSieteDigitos()
        {
            Assert.IsTrue(BuscadorClientesPorFactura.TieneFormatoDeFactura("NV2615541"));
            Assert.IsTrue(BuscadorClientesPorFactura.TieneFormatoDeFactura(" nv2615541 "));
            Assert.IsFalse(BuscadorClientesPorFactura.TieneFormatoDeFactura("2615541"));
            Assert.IsFalse(BuscadorClientesPorFactura.TieneFormatoDeFactura("NV261554"));
            Assert.IsFalse(BuscadorClientesPorFactura.TieneFormatoDeFactura("NV26155411"));
            Assert.IsFalse(BuscadorClientesPorFactura.TieneFormatoDeFactura("NVX615541"));
            Assert.IsFalse(BuscadorClientesPorFactura.TieneFormatoDeFactura("PELUQUERIA"));
            Assert.IsFalse(BuscadorClientesPorFactura.TieneFormatoDeFactura(null));
            Assert.AreEqual("NV2615541", BuscadorClientesPorFactura.NormalizarNumero(" nv2615541 "));
        }

        [TestMethod]
        public void GetClientes_SinVendedor_NumeroDeFactura_DevuelveElClienteYContactoDeLaFactura()
        {
            List<ClienteDTO> resultado = controller.GetClientes("1", "NV2615541").ToList();

            ClienteDTO unico = resultado.Single();
            Assert.AreEqual("27120", unico.cliente);
            Assert.AreEqual("1", unico.contacto, "El contacto de la factura, no el 0");
            Assert.AreEqual("PELUQUERÍA ANA (COBROS)", unico.nombre);
        }

        [TestMethod]
        public void GetClientes_SinVendedor_SoloSiLaBusquedaNormalNoDaNada()
        {
            // "ANA" no es factura; y aunque lo pareciese, si el nombre coincide manda la búsqueda normal
            Assert.AreEqual(2, controller.GetClientes("1", "PELUQUERÍA ANA").Count());
            Assert.AreEqual(0, controller.GetClientes("1", "NV2699999").Count(), "Factura que no existe: nada");
            Assert.AreEqual(0, controller.GetClientes("1", "NADIE1234").Count(), "Sin formato de factura: nada");
        }

        [TestMethod]
        public void GetClientes_SinVendedor_LaFacturaDeUnClienteDeBajaTambienLoEncuentra()
        {
            // La búsqueda normal esconde a los de baja; para contabilizar un pago hay que llegar igual
            Assert.AreEqual(0, controller.GetClientes("1", "DE BAJA").Count());
            Assert.AreEqual("40000", controller.GetClientes("1", "NV2615700").Single().cliente);
        }

        [TestMethod]
        public async Task GetClientes_ConVendedor_RespetaElEquipoDelVendedor()
        {
            List<ClienteDTO> deSuEquipo = (await controller.GetClientes("1", "NV", "NV2615541")).ToList();
            Assert.AreEqual("27120", deSuEquipo.Single().cliente);
            Assert.AreEqual("1", deSuEquipo.Single().contacto);

            List<ClienteDTO> deOtro = (await controller.GetClientes("1", "NV", "NV2615600")).ToList();
            Assert.AreEqual(0, deOtro.Count, "La factura es de un cliente de otro vendedor: no se enseña");

            List<ClienteDTO> sinVendedor = (await controller.GetClientes("1", null, "NV2615600")).ToList();
            Assert.AreEqual("30676", sinVendedor.Single().cliente, "Sin vendedor no hay filtro de equipo");
        }

        [TestMethod]
        public async Task GetClientes_ConVendedor_SoloSiLaBusquedaNormalNoDaNada()
        {
            Assert.AreEqual(2, (await controller.GetClientes("1", "NV", "PELUQUERÍA ANA")).Count());
            Assert.AreEqual(0, (await controller.GetClientes("1", "NV", "NV2699999")).Count());
        }
    }
}
