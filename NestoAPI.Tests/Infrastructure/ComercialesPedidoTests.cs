using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#665 (Sugerencia 584) / #666 (Sugerencia 672): a quién le toca un pedido. Comerciales según
    /// las líneas (estética = vendedor del pedido; peluquería = vendedor de peluquería del pedido) y, si un
    /// comercial no tiene correo, quien grabó el pedido.
    /// </summary>
    [TestClass]
    public class ComercialesPedidoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 925137;

        private NVEntities _db;
        private List<LinPedidoVta> _lineas;
        private List<VendedorPedidoGrupoProducto> _vendedoresPedido;
        private List<Vendedor> _vendedores;
        private List<ParametroUsuario> _parametros;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            _lineas = new List<LinPedidoVta>();
            _vendedoresPedido = new List<VendedorPedidoGrupoProducto>();
            _vendedores = new List<Vendedor>
            {
                new Vendedor { Empresa = EMPRESA, Número = "SAN", Descripción = "Santiago", Mail = "santiago@nuevavision.es" },
                new Vendedor { Empresa = EMPRESA, Número = "PEL", Descripción = "Peluquera", Mail = "peluqueria@nuevavision.es" },
                new Vendedor { Empresa = EMPRESA, Número = "SIN", Descripción = "Sin correo", Mail = null },
                new Vendedor { Empresa = EMPRESA, Número = Constantes.Vendedores.VENDEDOR_GENERAL, Descripción = "General", Mail = "general@nuevavision.es" }
            };
            _parametros = new List<ParametroUsuario>
            {
                new ParametroUsuario { Empresa = EMPRESA, Usuario = "MariaJose", Clave = Parametros.Claves.CorreoDefecto, Valor = "mariajose@nuevavision.es" },
                new ParametroUsuario { Empresa = EMPRESA, Usuario = "Santiago", Clave = "Vendedor", Valor = "SAN" },
                new ParametroUsuario { Empresa = EMPRESA, Usuario = "Peluquera", Clave = "Vendedor", Valor = "PEL  " },
                new ParametroUsuario { Empresa = EMPRESA, Usuario = "(defecto)", Clave = "Vendedor", Valor = "SAN" }
            };
            Configurar(() => _db.LinPedidoVtas, _lineas);
            Configurar(() => _db.VendedoresPedidosGruposProductos, _vendedoresPedido);
            Configurar(() => _db.Vendedores, _vendedores);
            Configurar(() => _db.ParametrosUsuario, _parametros);
            Configurar(() => _db.Clientes, new List<Cliente>
            {
                new Cliente { Empresa = EMPRESA, Nº_Cliente = "15191", Contacto = "0", Nombre = "Peluquería Ejemplo  " }
            });
        }

        private void Pedido(string vendedorEstetica)
        {
            Configurar(() => _db.CabPedidoVtas, new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Nº_Cliente = "15191", Contacto = "0", Vendedor = vendedorEstetica, Usuario = "NUEVAVISION\\MariaJose" }
            });
        }

        private void Lineas(params string[] grupos)
        {
            foreach (string grupo in grupos)
            {
                _lineas.Add(new LinPedidoVta { Empresa = EMPRESA, Número = PEDIDO, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Grupo = grupo });
            }
        }

        private void VendedorPeluqueria(string vendedor)
        {
            _vendedoresPedido.Add(new VendedorPedidoGrupoProducto { Empresa = EMPRESA, Pedido = PEDIDO, GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA, Vendedor = vendedor });
        }

        private ComercialesPedido Leer() => new LectorComercialesPedido(_db).Leer(EMPRESA, PEDIDO);

        [TestMethod]
        public void SoloEstetica_VaAlVendedorDelPedido_NoAQuienLoGrabo()
        {
            Pedido("SAN");
            Lineas(Constantes.Productos.GRUPO_COSMETICA, Constantes.Productos.GRUPO_APARATOS);
            VendedorPeluqueria("PEL");

            ComercialesPedido comerciales = Leer();

            CollectionAssert.AreEqual(new[] { "santiago@nuevavision.es" }, comerciales.Correos());
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Santiago" }, comerciales.UsuariosBuzon(), "Sin la fila (defecto)");
            Assert.AreEqual("Santiago", comerciales.NombresComerciales);
            Assert.AreEqual("Peluquería Ejemplo", comerciales.NombreCliente);
        }

        [TestMethod]
        public void SoloPeluqueria_VaAlVendedorDePeluqueria()
        {
            Pedido("SAN");
            Lineas("PEL");
            VendedorPeluqueria("PEL");
            // Los portes no tienen grupo: no deben meter al de estética en un pedido todo de peluquería
            _lineas.Add(new LinPedidoVta { Empresa = EMPRESA, Número = PEDIDO, TipoLinea = 3, Grupo = null });

            ComercialesPedido comerciales = Leer();

            CollectionAssert.AreEqual(new[] { "peluqueria@nuevavision.es" }, comerciales.Correos());
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Peluquera" }, comerciales.UsuariosBuzon());
            Assert.IsTrue(comerciales.Comerciales.Single().EsPeluqueria);
        }

        [TestMethod]
        public void EsteticaYPeluqueria_VaALosDos()
        {
            Pedido("SAN");
            Lineas(Constantes.Productos.GRUPO_COSMETICA, "PEL");
            VendedorPeluqueria("PEL");

            ComercialesPedido comerciales = Leer();

            CollectionAssert.AreEquivalent(new[] { "santiago@nuevavision.es", "peluqueria@nuevavision.es" }, comerciales.Correos());
            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Santiago", "NUEVAVISION\\Peluquera" }, comerciales.UsuariosBuzon());
        }

        [TestMethod]
        public void ComercialSinCorreo_VaAQuienGraboElPedido()
        {
            Pedido("SIN");
            Lineas(Constantes.Productos.GRUPO_COSMETICA);

            ComercialesPedido comerciales = Leer();

            CollectionAssert.AreEqual(new[] { "mariajose@nuevavision.es" }, comerciales.Correos());
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\MariaJose" }, comerciales.UsuariosBuzon(), "Tampoco tiene usuario: al buzón de quien lo grabó");
        }

        [TestMethod]
        public void DosComerciales_UnoSinCorreo_VaAlOtroYAQuienLoGrabo()
        {
            Pedido("SIN");
            Lineas(Constantes.Productos.GRUPO_COSMETICA, "PEL");
            VendedorPeluqueria("PEL");

            ComercialesPedido comerciales = Leer();

            CollectionAssert.AreEquivalent(new[] { "peluqueria@nuevavision.es", "mariajose@nuevavision.es" }, comerciales.Correos());
        }

        [TestMethod]
        public void VendedorGeneral_NoEsUnComercial_VaAQuienGraboElPedido()
        {
            Pedido(Constantes.Vendedores.VENDEDOR_GENERAL);
            Lineas(Constantes.Productos.GRUPO_COSMETICA);

            CollectionAssert.AreEqual(new[] { "mariajose@nuevavision.es" }, Leer().Correos());
        }

        [TestMethod]
        public void PeluqueriaSinVendedorDePeluqueria_AvisaAlDeEstetica()
        {
            Pedido("SAN");
            Lineas("PEL");

            CollectionAssert.AreEqual(new[] { "santiago@nuevavision.es" }, Leer().Correos());
        }

        [TestMethod]
        public void CodigosVendedores_LaFacturaSigueSinPonerANadieSiNoHayVendedorDePeluqueria()
        {
            // CargarVendedoresPedido (la factura) lo hacía así desde siempre: no se cambia.
            List<string> codigos = ComercialesPedido.CodigosVendedores(new[] { "PEL" }, "SAN", null, peluqueriaSinVendedorVaAlDeEstetica: false);

            Assert.AreEqual(0, codigos.Count);
        }

        [TestMethod]
        public void CodigosVendedores_GruposConEspaciosYMismoVendedor_SinRepetir()
        {
            List<string> codigos = ComercialesPedido.CodigosVendedores(new[] { "PEL", "COS" }, "SAN  ", "SAN", peluqueriaSinVendedorVaAlDeEstetica: false);

            CollectionAssert.AreEqual(new[] { "SAN" }, codigos);
        }

        // ===== GestorPrepagos (Sugerencia 584): el correo de retenidos =====

        [TestMethod]
        public void Retenidos_VaALosComercialesDeCadaPedido_ConCopiaAAdministracion()
        {
            var rellenador = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => rellenador.CorreoUsuario(A<string>._)).Returns("mariajose@nuevavision.es");
            var pedido1 = new PedidoPicking(rellenador) { Empresa = EMPRESA, Id = 925137, Usuario = "NUEVAVISION\\MariaJose" };
            var pedido2 = new PedidoPicking(rellenador) { Empresa = EMPRESA, Id = 925138, Usuario = "NUEVAVISION\\MariaJose" };
            var comerciales = new Dictionary<int, ComercialesPedido>
            {
                [925137] = new ComercialesPedido { Comerciales = { new ComercialPedido { Vendedor = "SAN", Nombre = "Santiago", Correo = "santiago@nuevavision.es" } } },
                [925138] = new ComercialesPedido { Comerciales = { new ComercialPedido { Vendedor = "PEL", Nombre = "Peluquera", Correo = "Peluqueria@nuevavision.es" } } }
            };

            MailMessage mail = GestorPrepagos.ComponerCorreo(new List<PedidoPicking> { pedido1, pedido2 }, p => comerciales[p.Id]);

            CollectionAssert.AreEquivalent(new[] { "santiago@nuevavision.es", "peluqueria@nuevavision.es" }, mail.To.Select(t => t.Address).ToList());
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.CC.Single().Address);
            StringAssert.Contains(mail.Body, "<th>Comercial</th>");
            StringAssert.Contains(mail.Body, "<td>Santiago</td>");
        }

        [TestMethod]
        public void Retenidos_SinComerciales_VaAQuienGraboElPedidoComoAntes()
        {
            var rellenador = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => rellenador.CorreoUsuario("NUEVAVISION\\MariaJose")).Returns("mariajose@nuevavision.es");
            var pedido = new PedidoPicking(rellenador) { Empresa = EMPRESA, Id = 925137, Usuario = "NUEVAVISION\\MariaJose" };

            MailMessage mail = GestorPrepagos.ComponerCorreo(new List<PedidoPicking> { pedido }, p => null);

            Assert.AreEqual("mariajose@nuevavision.es", mail.To.Single().Address);
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.CC.Single().Address);
        }

        private static void Configurar<T>(System.Linq.Expressions.Expression<System.Func<DbSet<T>>> propiedad, List<T> datos) where T : class
        {
            IQueryable<T> consulta = datos.AsQueryable();
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            A.CallTo(() => ((IQueryable<T>)fake).Provider).Returns(consulta.Provider);
            A.CallTo(() => ((IQueryable<T>)fake).Expression).Returns(consulta.Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(consulta.ElementType);
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(propiedad).Returns(fake);
        }
    }
}
