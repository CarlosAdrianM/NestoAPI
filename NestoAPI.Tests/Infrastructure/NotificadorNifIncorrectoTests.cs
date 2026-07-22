using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#327 (Carlos 22/07): el aviso de NIF incorrecto va al vendedor SOLO si es un
    /// vendedor real con correo; con el vendedor general (NV) o sin correo, va al USUARIO que
    /// procesó el documento (su CorreoDefecto de ParametrosUsuario); y si tampoco tiene correo,
    /// solo a administración. Antes el fallback era el correo de informática, que recibía en
    /// "Para" todos los avisos de clientes del vendedor general.
    /// </summary>
    [TestClass]
    public class NotificadorNifIncorrectoTests
    {
        private NVEntities db;
        private DbSet<Cliente> fakeClientes;
        private DbSet<Vendedor> fakeVendedores;
        private DbSet<ParametroUsuario> fakeParametros;
        private IServicioCorreoElectronico correo;
        private MailMessage enviado;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            fakeVendedores = A.Fake<DbSet<Vendedor>>(o => o.Implements<IQueryable<Vendedor>>());
            A.CallTo(() => db.Vendedores).Returns(fakeVendedores);
            ConfigurarSync(fakeVendedores, new List<Vendedor>().AsQueryable());
            fakeParametros = A.Fake<DbSet<ParametroUsuario>>(o => o.Implements<IQueryable<ParametroUsuario>>());
            A.CallTo(() => db.ParametrosUsuario).Returns(fakeParametros);
            ConfigurarSync(fakeParametros, new List<ParametroUsuario>().AsQueryable());
            correo = A.Fake<IServicioCorreoElectronico>();
            enviado = null;
            _ = A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.Ignored))
                .Invokes((MailMessage m) => enviado = m).Returns(true);
        }

        private void ConFicha(string vendedor)
        {
            var data = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "26760", Contacto = "0", Nombre = "ZHANNA YURCHYK", Vendedor = vendedor, ClientePrincipal = true }
            }.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<Cliente>)fakeClientes).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<Cliente>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).Provider)
                .Returns(new TestDbAsyncQueryProvider<Cliente>(data.Provider));
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<Cliente>)fakeClientes).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private static void ConfigurarSync<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider).Returns(data.Provider);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private void ConVendedor(string numero, string mail)
        {
            ConfigurarSync(fakeVendedores, new List<Vendedor>
            {
                new Vendedor { Empresa = "1", Número = numero, Mail = mail }
            }.AsQueryable());
        }

        private void ConCorreoUsuario(string usuario, string correoUsuario)
        {
            ConfigurarSync(fakeParametros, new List<ParametroUsuario>
            {
                new ParametroUsuario { Empresa = "1", Usuario = usuario, Clave = Parametros.Claves.CorreoDefecto, Valor = correoUsuario }
            }.AsQueryable());
        }

        private Task Enviar(string usuario = null)
        {
            return new NotificadorNifIncorrecto(db, correo).Enviar(
                "1", "26760", "el pedido 922900", esFactura: false,
                nif: "X9999999X", nombre: "ZHANNA YURCHYK", resultadoAeat: "NO IDENTIFICADO",
                usuario: usuario);
        }

        [TestMethod]
        public async Task Enviar_VendedorRealConCorreo_VaAlVendedorConCopiaAAdministracion()
        {
            ConFicha(vendedor: "DV ");
            ConVendedor("DV ", "vendedora@nuevavision.es");

            await Enviar(usuario: "NUEVAVISION\\Laura");

            Assert.AreEqual("vendedora@nuevavision.es", enviado.To.Single().Address);
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, enviado.CC.Single().Address);
        }

        [TestMethod]
        public async Task Enviar_VendedorGeneral_VaAlUsuarioQueMetioElDocumento()
        {
            // El caso del correo del 22/07: vendedor NV (General) → el aviso iba a informática.
            ConFicha(vendedor: Constantes.Vendedores.VENDEDOR_GENERAL);
            ConVendedor(Constantes.Vendedores.VENDEDOR_GENERAL, "informatica@nuevavision.es");
            ConCorreoUsuario("Laura", "laura@nuevavision.es");

            await Enviar(usuario: "NUEVAVISION\\Laura");

            Assert.AreEqual("laura@nuevavision.es", enviado.To.Single().Address);
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, enviado.CC.Single().Address);
        }

        [TestMethod]
        public async Task Enviar_VendedorGeneralYUsuarioSinCorreo_SoloAAdministracion()
        {
            ConFicha(vendedor: Constantes.Vendedores.VENDEDOR_GENERAL);

            await Enviar(usuario: "NUEVAVISION\\Almacen1");

            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, enviado.To.Single().Address);
            Assert.AreEqual(0, enviado.CC.Count, "Administración no debe ir duplicada en CC");
        }

        [TestMethod]
        public async Task Enviar_VendedorRealSinCorreo_TambienVaAlUsuario()
        {
            ConFicha(vendedor: "DV ");
            ConVendedor("DV ", mail: null);
            ConCorreoUsuario("Laura", "laura@nuevavision.es");

            await Enviar(usuario: "NUEVAVISION\\Laura");

            Assert.AreEqual("laura@nuevavision.es", enviado.To.Single().Address);
        }
    }
}
