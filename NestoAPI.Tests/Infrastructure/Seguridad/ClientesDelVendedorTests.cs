using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace NestoAPI.Tests.Infrastructure.Seguridad
{
    /// <summary>
    /// Vendedores de NestoApp (06/11/26): el cliente es del vendedor si alguna fila de Clientes de ese Nº Cliente (cualquier
    /// contacto) en esa empresa tiene ese Vendedor, o lo tiene VendedoresClientesGruposProductos. El Vendedor es char relleno.
    /// </summary>
    [TestClass]
    public class ClientesDelVendedorTests
    {
        private static IQueryable<Cliente> Clientes() => new List<Cliente>
        {
            new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Vendedor = "JE " },
            new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1  ", Vendedor = "NV " },
            new Cliente { Empresa = "3", Nº_Cliente = "20000", Contacto = "0  ", Vendedor = "NV " },
            new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0  ", Vendedor = "AL " }
        }.AsQueryable();

        /// <summary>El 15191 tiene a «PE» de vendedor de peluquería (solo en el contacto 1); el 30000, a nadie.</summary>
        private static IQueryable<VendedorClienteGrupoProducto> Grupos() => new List<VendedorClienteGrupoProducto>
        {
            new VendedorClienteGrupoProducto { Empresa = "1", Cliente = "15191", Contacto = "1  ", GrupoProducto = "PEL", Vendedor = "PE " },
            new VendedorClienteGrupoProducto { Empresa = "3", Cliente = "20000", Contacto = "0  ", GrupoProducto = "PEL", Vendedor = "PG " }
        }.AsQueryable();

        [TestMethod]
        public void EsDelVendedor_SoloEnLaTablaDeGrupos_Suyo()
        {
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "PE", "15191"));
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "3", "PG ", "20000 "));
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(null, Grupos(), "1", "PE", "15191"), "sin Clientes, solo los grupos");
        }

        [TestMethod]
        public void EsDelVendedor_NiEnClientesNiEnGrupos_NoEsSuyo()
        {
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "XX", "15191"));
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "PE", "20000"), "PE no es del 20000");
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "3", "PE", "15191"), "otra empresa");
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), null, "1", "PE", "15191"), "sin la tabla de grupos, solo Clientes");
        }

        [TestMethod]
        public void EsDelVendedor_CualquierContactoConEseVendedor_Suyo()
        {
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "NV", "15191"));
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", " JE", "15191 "));
        }

        [TestMethod]
        public void EsDelVendedor_SinEmpresa_LaPorDefecto()
        {
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), null, "NV", "15191"));
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), " ", "NV", "20000"), "en la 1 el 20000 es de AL");
        }

        [TestMethod]
        public void EsDelVendedor_OtraEmpresa_SeMiraEnEsa()
        {
            Assert.IsTrue(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "3", "NV", "20000"));
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "3", "NV", "15191"));
        }

        [TestMethod]
        public void EsDelVendedor_DeOtroVendedorOSinDatos_NoEsSuyo()
        {
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "AL", "15191"));
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "NV", "99999"));
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "", "15191"));
            Assert.IsFalse(ClientesDelVendedor.EsDelVendedor(Clientes(), Grupos(), "1", "NV", null));
        }

        [TestMethod]
        public void EsVendedorDeNestoApp_SoloConIsVendedorYCodigoYSinSerEmpleadoNiCliente()
        {
            Assert.IsTrue(ClientesDelVendedor.EsVendedorDeNestoApp(Identidad("IsVendedor", "true", "Vendedor", "NV")));
            Assert.IsFalse(ClientesDelVendedor.EsVendedorDeNestoApp(Identidad("IsVendedor", "true")), "sin código");
            Assert.IsFalse(ClientesDelVendedor.EsVendedorDeNestoApp(Identidad("IsVendedor", "true", "Vendedor", "NV", "cliente", "15191")));
            Assert.IsFalse(ClientesDelVendedor.EsVendedorDeNestoApp(Identidad("IsEmployee", "true", "IsVendedor", "true", "Vendedor", "NV")));
            Assert.IsFalse(ClientesDelVendedor.EsVendedorDeNestoApp(Identidad()));
            Assert.IsFalse(ClientesDelVendedor.EsVendedorDeNestoApp(null));
        }

        [TestMethod]
        public void PuedeElVendedor_PasaEmpresaVendedorYClienteSinEspacios()
        {
            string visto = null;
            ClaimsIdentity vendedor = Identidad("IsVendedor", "true", "Vendedor", "NV ");

            bool puede = ClientesDelVendedor.PuedeElVendedor(vendedor, "1", "15191 ", (e, v, c) => { visto = e + "|" + v + "|" + c; return true; });

            Assert.IsTrue(puede);
            Assert.AreEqual("1|NV|15191", visto);
            Assert.IsFalse(ClientesDelVendedor.PuedeElVendedor(Identidad("cliente", "15191"), "1", "15191", (e, v, c) => true), "un cliente de la tienda no");
        }

        private static ClaimsIdentity Identidad(params string[] tiposYValores)
        {
            var identity = new ClaimsIdentity("TestAuth");
            for (int i = 0; i < tiposYValores.Length; i += 2)
            {
                identity.AddClaim(new Claim(tiposYValores[i], tiposYValores[i + 1]));
            }
            return identity;
        }
    }
}
