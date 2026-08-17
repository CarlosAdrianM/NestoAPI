using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#340: GET api/ExtractoProveedores/BuscarPago sustituye la consulta EF directa de
    /// CanalesExternosPagosService (Nesto WPF), que buscaba el asiento del pago de Amazon en
    /// ExtractoProveedor por proveedor + fecha + importe exactos (TipoApunte 3).
    /// </summary>
    [TestClass]
    public class ExtractoProveedoresControllerTests
    {
        private NVEntities db;
        private DbSet<ExtractoProveedor> fakeExtractos;
        private ExtractoProveedoresController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractos = A.Fake<DbSet<ExtractoProveedor>>(o =>
                o.Implements<IQueryable<ExtractoProveedor>>().Implements<IDbAsyncEnumerable<ExtractoProveedor>>());
            A.CallTo(() => db.ExtractosProveedor).Returns(fakeExtractos);
            controller = new ExtractoProveedoresController(db);
        }

        private void ConExtractos(params ExtractoProveedor[] extractos)
        {
            var data = extractos.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<ExtractoProveedor>)fakeExtractos).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<ExtractoProveedor>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<ExtractoProveedor>)fakeExtractos).Provider)
                .Returns(new TestDbAsyncQueryProvider<ExtractoProveedor>(data.Provider));
            A.CallTo(() => ((IQueryable<ExtractoProveedor>)fakeExtractos).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<ExtractoProveedor>)fakeExtractos).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<ExtractoProveedor>)fakeExtractos).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private static ExtractoProveedor Apunte(string proveedor, DateTime fecha, decimal importe,
            string tipoApunte = "3", int asiento = 0)
        {
            return new ExtractoProveedor
            {
                Empresa = "1",
                Número = proveedor,
                Fecha = fecha,
                Importe = importe,
                TipoApunte = tipoApunte,
                Asiento = asiento
            };
        }

        [TestMethod]
        public async Task BuscarPago_ConApunteDePagoQueCasa_DevuelveSuAsiento()
        {
            var fecha = new DateTime(2026, 8, 10);
            ConExtractos(
                Apunte("450", fecha, 1234.56M, tipoApunte: "1", asiento: 11),   // factura: no es pago
                Apunte("450", fecha, 999.99M, asiento: 22),                     // otro importe
                Apunte("450", fecha, 1234.56M, asiento: 77));                   // el pago buscado

            var resultado = await controller.BuscarPago("450", fecha, 1234.56M) as OkNegotiatedContentResult<int>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(77, resultado.Content);
        }

        [TestMethod]
        public async Task BuscarPago_SinApunteQueCase_DevuelveCero()
        {
            // El cliente WPF ponía 0 cuando no había coincidencia (apunte?.Asiento ?? 0):
            // mismo contrato para no cambiar el comportamiento de la pantalla de pagos.
            ConExtractos(Apunte("450", new DateTime(2026, 8, 10), 999.99M, asiento: 22));

            var resultado = await controller.BuscarPago("450", new DateTime(2026, 8, 11), 1234.56M) as OkNegotiatedContentResult<int>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Content);
        }
    }
}
