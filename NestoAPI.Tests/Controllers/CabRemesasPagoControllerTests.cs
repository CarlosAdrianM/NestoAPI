using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Crear el fichero de una remesa de pagos. Estos tests cubren el caso que llenaba ELMAH de
    /// errores 500: pedir una remesa que no existe, que pasa de verdad porque en la pantalla se
    /// teclea en la casilla de remesa un nº de orden de movimiento de proveedor.
    /// </summary>
    [TestClass]
    public class CabRemesasPagoControllerTests
    {
        private NVEntities db;
        private DbSet<CabRemesaPago> fakeRemesas;
        private DbSet<ExtractoProveedor> fakeExtractos;
        private DbSet<Banco> fakeBancos;
        private CabRemesasPagoController controller;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeRemesas = A.Fake<DbSet<CabRemesaPago>>(o => o.Implements<IQueryable<CabRemesaPago>>().Implements<IDbAsyncEnumerable<CabRemesaPago>>());
            fakeExtractos = A.Fake<DbSet<ExtractoProveedor>>(o => o.Implements<IQueryable<ExtractoProveedor>>().Implements<IDbAsyncEnumerable<ExtractoProveedor>>());
            fakeBancos = A.Fake<DbSet<Banco>>(o => o.Implements<IQueryable<Banco>>().Implements<IDbAsyncEnumerable<Banco>>());

            A.CallTo(() => db.CabRemesasPago).Returns(fakeRemesas);
            A.CallTo(() => db.ExtractosProveedor).Returns(fakeExtractos);
            A.CallTo(() => db.Bancos).Returns(fakeBancos);

            ConfigurarFakeDbSet(fakeRemesas, new List<CabRemesaPago>().AsQueryable());
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoProveedor>().AsQueryable());
            ConfigurarFakeDbSet(fakeBancos, new List<Banco>().AsQueryable());

            controller = new CabRemesasPagoController(db);
        }

        [TestMethod]
        public async Task CrearFicheroRemesa_SiLaRemesaNoExiste_DevuelveNotFoundYNoRevienta()
        {
            // 10927 no es una remesa: es un nº de orden de ExtractoProveedor. Antes esto lanzaba
            // InvalidOperationException("Sequence contains no elements") desde SingleAsync y salía
            // un 500 a ELMAH, aunque el código de debajo ya pretendía devolver NotFound.
            var resultado = await controller.GetCrearFicheroRemesa(10927);

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido, "Debe responder con un contenido de error, no reventar");
            Assert.AreEqual(HttpStatusCode.NotFound, contenido.StatusCode);
            StringAssert.Contains(contenido.Content, "10927");
        }

        [TestMethod]
        public async Task CrearFicheroRemesa_SiElBancoDeLaRemesaNoExiste_DevuelveNotFound()
        {
            ConfigurarFakeDbSet(fakeRemesas, new List<CabRemesaPago>
            {
                new CabRemesaPago { Numero = 4286, Empresa = "1", Banco = "99" }
            }.AsQueryable());

            var resultado = await controller.GetCrearFicheroRemesa(4286);

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido, "Sin banco se llamaba a banco.Empresa y saltaba una NullReference");
            Assert.AreEqual(HttpStatusCode.NotFound, contenido.StatusCode);
        }

        [TestMethod]
        public async Task CrearFicheroMovimiento_SiElMovimientoNoExiste_DevuelveNotFoundYNoRevienta()
        {
            var resultado = await controller.GetCrearFicheroRemesa(99999, "5");

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido);
            Assert.AreEqual(HttpStatusCode.NotFound, contenido.StatusCode);
            StringAssert.Contains(contenido.Content, "99999");
        }

        [TestMethod]
        public async Task CrearFicheroMovimiento_SiElBancoNoExiste_DevuelveNotFound()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoProveedor>
            {
                new ExtractoProveedor { NºOrden = 10927, Empresa = "1" }
            }.AsQueryable());

            var resultado = await controller.GetCrearFicheroRemesa(10927, "99");

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido);
            Assert.AreEqual(HttpStatusCode.NotFound, contenido.StatusCode);
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
    }
}
