using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
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
    /// Nesto#340 (Agencias, slice A3): el historial de cambios de un envio, que Nesto leia con su
    /// propio DbContext. Era la unica consulta del cliente sobre EnviosHistoria, y la tabla ni
    /// siquiera estaba en el EDMX del servidor hasta el 31/08/2026.
    ///
    /// Solo lectura: las escrituras del historial siguen en el ViewModel de Nesto, dentro de las
    /// transacciones de contabilizacion de reembolsos.
    /// </summary>
    [TestClass]
    public class EnviosAgenciasHistoriaTests
    {
        private NVEntities db;
        private DbSet<EnvioHistoria> fakeHistoria;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeHistoria = A.Fake<DbSet<EnvioHistoria>>(o =>
                o.Implements<IQueryable<EnvioHistoria>>().Implements<IDbAsyncEnumerable<EnvioHistoria>>());
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            controller = new EnviosAgenciasController(db);
        }

        private void ConHistoria(params EnvioHistoria[] filas)
        {
            IQueryable<EnvioHistoria> data = filas.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<EnvioHistoria>)fakeHistoria).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<EnvioHistoria>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<EnvioHistoria>)fakeHistoria).Provider)
                .Returns(new TestDbAsyncQueryProvider<EnvioHistoria>(data.Provider));
            A.CallTo(() => ((IQueryable<EnvioHistoria>)fakeHistoria).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<EnvioHistoria>)fakeHistoria).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<EnvioHistoria>)fakeHistoria).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private static EnvioHistoria Fila(int numero, int numeroEnvio, string campo = "Reembolso",
            string valorAnterior = "142,05", string observaciones = "Lo cambia el cliente",
            string usuario = @"NUEVAVISION\Alfredo", DateTime? fecha = null) => new EnvioHistoria
            {
                Numero = numero,
                NumeroEnvio = numeroEnvio,
                Campo = campo,
                ValorAnterior = valorAnterior,
                Observaciones = observaciones,
                Usuario = usuario,
                FechaModificacion = fecha ?? new DateTime(2026, 8, 31, 12, 4, 0)
            };

        private async Task<List<EnvioHistoriaDTO>> Pedir(int envio)
        {
            var resultado = await controller.GetHistoriaEnvio(envio) as OkNegotiatedContentResult<List<EnvioHistoriaDTO>>;
            Assert.IsNotNull(resultado, "El endpoint debe devolver 200 con la lista");
            return resultado.Content;
        }

        [TestMethod]
        public async Task GetHistoriaEnvio_DevuelveLosCamposQuePintaLaRejilla()
        {
            ConHistoria(Fila(1, 248142));

            EnvioHistoriaDTO fila = (await Pedir(248142)).Single();

            Assert.AreEqual(1, fila.Numero);
            Assert.AreEqual(248142, fila.NumeroEnvio);
            Assert.AreEqual("Reembolso", fila.Campo);
            Assert.AreEqual("142,05", fila.ValorAnterior);
            Assert.AreEqual("Lo cambia el cliente", fila.Observaciones);
            Assert.AreEqual(@"NUEVAVISION\Alfredo", fila.Usuario);
            Assert.AreEqual(new DateTime(2026, 8, 31, 12, 4, 0), fila.FechaModificacion);
        }

        [TestMethod]
        public async Task GetHistoriaEnvio_ElHistorialDeOtroEnvio_NoSeMezcla()
        {
            ConHistoria(Fila(1, 248142), Fila(2, 248150), Fila(3, 248142));

            List<EnvioHistoriaDTO> historia = await Pedir(248142);

            Assert.AreEqual(2, historia.Count);
            CollectionAssert.AreEqual(new[] { 1, 3 }, historia.Select(h => h.Numero).ToArray());
        }

        /// <summary>
        /// La consulta de Nesto no ordenaba, asi que el orden dependia del plan de SQL Server.
        /// Ahora lo fija el servidor por numero, que es identity: orden de los hechos. Es lo que
        /// espera quien lee un historial de cambios.
        /// </summary>
        [TestMethod]
        public async Task GetHistoriaEnvio_SeDevuelveEnOrdenCronologico()
        {
            ConHistoria(Fila(30, 248142), Fila(10, 248142), Fila(20, 248142));

            List<EnvioHistoriaDTO> historia = await Pedir(248142);

            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, historia.Select(h => h.Numero).ToArray());
        }

        /// <summary>
        /// Un envio que nadie ha tocado no tiene historial, y eso es lo NORMAL. Tiene que devolver
        /// lista vacia y no 404: el llamante pinta una rejilla, no maneja "no encontrado".
        /// </summary>
        [TestMethod]
        public async Task GetHistoriaEnvio_EnvioSinHistorial_DevuelveListaVaciaNo404()
        {
            ConHistoria();

            List<EnvioHistoriaDTO> historia = await Pedir(248142);

            Assert.AreEqual(0, historia.Count);
        }
    }
}
